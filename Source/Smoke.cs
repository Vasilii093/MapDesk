using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AlpineQuestPort
{
    static class Smoke
    {
        public static int Run(string[] args)
        {
            string destination = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smoke-results");
            Directory.CreateDirectory(destination);
            try {
                var d = new MapDocument();
                d.Waypoints.Add(new GeoPoint(55.733, 37.594) { Name = "Тестовая точка" });
                var t = new Track { Name = "Проверочный маршрут", IsRoute = true };
                t.Points.Add(new GeoPoint(55.733, 37.594)); t.Points.Add(new GeoPoint(55.736, 37.603)); d.Tracks.Add(t);
                MapFiles.SaveGpx(d, Path.Combine(destination, "roundtrip.gpx"));
                MapFiles.SaveKml(d, Path.Combine(destination, "roundtrip.kml"));
                foreach (string ext in new[] { "gpx", "kml" }) {
                    var loaded = MapFiles.Load(Path.Combine(destination, "roundtrip." + ext));
                    if (loaded.Waypoints.Count != 1 || loaded.Tracks.Count != 1 || loaded.Tracks[0].Points.Count != 2) throw new Exception("Roundtrip failed: " + ext);
                    if (ext == "gpx" && !loaded.Tracks[0].IsRoute) throw new Exception("GPX route type was lost");
                }
                using (var tiles = new TileStore(Path.Combine(destination, "cache"), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "default_map")))
                using (var c = new MapCanvas(tiles) { Size = new Size(1000, 600), Document = d })
                using (var bmp = new Bitmap(1000, 600)) {
                    c.Settings.Crosshair=false;c.Settings.DataCards=false;
                    c.CreateControl(); c.Fit(); c.DrawToBitmap(bmp, c.ClientRectangle);
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var fromScreen = typeof(MapCanvas).GetMethod("FromScreen", flags);
                    var anchor = new Point(300, 200);
                    var before = (GeoPoint)fromScreen.Invoke(c, new object[] { anchor });
                    c.ChangeZoom(1, anchor);
                    var after = (GeoPoint)fromScreen.Invoke(c, new object[] { anchor });
                    if (GeoMath.DistanceMeters(before, after) > 0.01) throw new Exception("Zoom anchor moved");
                    c.BeginMode(MapMode.Route);
                    var down = typeof(MapCanvas).GetMethod("OnMouseDown", flags); var up = typeof(MapCanvas).GetMethod("OnMouseUp", flags);
                    down.Invoke(c, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 400, 300, 0) }); up.Invoke(c, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 400, 300, 0) });
                    if (!c.HasDraft) throw new Exception("Route click failed");
                    c.Undo(); if (c.HasDraft) throw new Exception("Route undo failed");
                    c.CancelDraft(); c.Pan(20, 10); c.DrawToBitmap(bmp, c.ClientRectangle);
                    bmp.Save(Path.Combine(destination, "map-render.png"), System.Drawing.Imaging.ImageFormat.Png);
                    c.Document.Tracks.Clear(); var longLine = new Track { Name = "Clipping" }; longLine.Points.Add(new GeoPoint(0, -30)); longLine.Points.Add(new GeoPoint(0, 30)); c.Document.Tracks.Add(longLine); c.GoTo(new GeoPoint(0, 0), 19); c.DrawToBitmap(bmp, c.ClientRectangle);
                    var pixel = bmp.GetPixel(500, 300); if (pixel.G < 60 || pixel.G > 150 || pixel.R > 80) throw new Exception("Long segment clipping failed");
                    c.FitPoints(new[] { new GeoPoint(10, 179.9), new GeoPoint(10, -179.9) }); if (c.Zoom < 8) throw new Exception("Antimeridian fit failed");
                    c.DrawToBitmap(bmp, c.ClientRectangle);
                }
                using (var form = new MainForm(null)) { if (form.Controls.Count < 3) throw new Exception("Form construction failed"); }
                File.WriteAllText(Path.Combine(destination, "result.txt"), "PASS\r\nArchitecture: " + (Environment.Is64BitProcess ? "x64" : "x86") + "\r\nGPX/KML roundtrip, GPX route type, map rendering, cursor-anchored zoom, pan, route click/undo, antimeridian fit, long-segment clipping, full form construction.\r\nNo live GPS or network tested by this command.\r\n");
                return 0;
            } catch (Exception e) { File.WriteAllText(Path.Combine(destination, "result.txt"), "FAIL\r\n" + e); return 2; }
        }
    }
}
