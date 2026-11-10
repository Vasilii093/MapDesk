using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using AlpineQuestPort;

internal static class CoreTests
{
    static int checks;
    static string folder;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static string Input(string name, string body) { string path = Path.Combine(folder, name); File.WriteAllText(path, body, new UTF8Encoding(false)); return path; }
    static void Reject(string name, string body) { bool rejected = false; try { MapFiles.Load(Input(name, body)); } catch { rejected = true; } Check(rejected, "Expected rejection: " + name); }
    static int Main(string[] args)
    {
        folder = Path.Combine(Path.GetDirectoryName(typeof(CoreTests).Assembly.Location), "fixtures");
        Directory.CreateDirectory(folder);
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        string gpx = "<gpx xmlns='http://www.topografix.com/GPX/1/1' version='1.1'><wpt lat='55.5' lon='37.5'><ele>10.25</ele><name>Точка &amp; A</name><time>2026-10-12T12:34:56.123Z</time></wpt><rte><name>Route</name><rtept lat='0' lon='0'/><rtept lat='0' lon='1'/></rte><trk><name>Segments</name><trkseg><trkpt lat='50' lon='30'/><trkpt lat='50' lon='30.01'/></trkseg><trkseg><trkpt lat='60' lon='60'/><trkpt lat='60' lon='60.01'/></trkseg></trk></gpx>";
        MapDocument doc = MapFiles.Load(Input("mixed.gpx", gpx));
        Check(doc.Waypoints.Count == 1 && doc.Tracks.Count == 3, "Mixed routes/segments imported");
        Check(doc.Waypoints[0].Elevation == 10.25 && doc.Waypoints[0].Time.HasValue, "Invariant elevation and time");
        Check(doc.Tracks[1].Points.Count == 2 && doc.Tracks[2].Points.Count == 2, "Segments kept separate");
        Check(GeoMath.TrackLengthMeters(doc.Tracks[1]) < 1000 && GeoMath.TrackLengthMeters(doc.Tracks[2]) < 1000, "No artificial segment jump");
        string exported = Path.Combine(folder, "roundtrip.gpx");
        MapFiles.SaveGpx(doc, exported); MapFiles.SaveGpx(doc, exported);
        MapDocument again = MapFiles.Load(exported);
        Check(again.Waypoints[0].Name == "Точка & A" && again.Tracks.Count == 3, "GPX roundtrip and replacement");
        Check(again.Waypoints[0].Time.Value == doc.Waypoints[0].Time.Value, "GPX timestamp preserved");
        Check(again.Tracks[0].IsRoute && !again.Tracks[1].IsRoute, "GPX route/track distinction preserved");
        string kml = "<kml xmlns='http://www.opengis.net/kml/2.2' xmlns:gx='http://www.google.com/kml/ext/2.2'><Document><Placemark><name>Multi</name><MultiGeometry><Point><coordinates>37.5,55.5,100</coordinates></Point><LineString><coordinates>0,0 1,0</coordinates></LineString><LineString><coordinates>50,50 50.1,50</coordinates></LineString></MultiGeometry></Placemark><Placemark><name>Time</name><gx:Track><when>2026-10-12T10:00:00Z</when><when>2026-10-12T10:00:01Z</when><gx:coord>30 50 100</gx:coord><gx:coord>30.1 50.1 101</gx:coord></gx:Track></Placemark></Document></kml>";
        MapDocument kd = MapFiles.Load(Input("multiple.kml", kml));
        Check(kd.Tracks.Count == 3 && kd.Waypoints.Count == 1, "KML multiple geometries");
        Check(kd.Tracks[2].Points[0].Time.HasValue, "KML gx time");
        string outKml = Path.Combine(folder, "roundtrip.kml"); MapFiles.SaveKml(kd, outKml);
        MapDocument ka = MapFiles.Load(outKml);
        Check(ka.Tracks.Count == 3 && ka.Tracks[2].Points[1].Time.HasValue, "KML gx roundtrip");
        string kmz = Path.Combine(folder, "archive.kmz");
        using (FileStream stream = new FileStream(kmz, FileMode.Create)) using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
        { using (StreamWriter writer = new StreamWriter(archive.CreateEntry("../../doc.kml").Open())) writer.Write(kml); }
        MapDocument zipped = MapFiles.Load(kmz);
        Check(zipped.Tracks.Count == 3 && !File.Exists(Path.Combine(folder, "doc.kml")), "KMZ imports without extraction");
        Reject("xxe.gpx", "<!DOCTYPE gpx [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><gpx><wpt lat='0' lon='0'><name>&x;</name></wpt></gpx>");
        Reject("nan.gpx", "<gpx><wpt lat='NaN' lon='0'/></gpx>");
        Reject("infinity.gpx", "<gpx><wpt lat='0' lon='Infinity'/></gpx>");
        Reject("range.gpx", "<gpx><wpt lat='91' lon='0'/></gpx>");
        Reject("missing.gpx", "<gpx><wpt lat='20'/></gpx>");
        Reject("wrong.gpx", "<kml/>");
        Reject("bad.kml", "<kml><Placemark><Point><coordinates>1,2 3,4</coordinates></Point></Placemark></kml>");
        double lat = 55.7522, lon = 37.6156;
        Check(Math.Abs(GeoMath.PixelToLat(GeoMath.LatToPixel(lat, 14), 14) - lat) < 1e-10, "Mercator latitude roundtrip");
        Check(Math.Abs(GeoMath.PixelToLon(GeoMath.LonToPixel(lon, 14), 14) - lon) < 1e-10, "Mercator longitude roundtrip");
        Check(GeoMath.IsFinite(GeoMath.LatToPixel(90, 8)) && GeoMath.IsFinite(GeoMath.LatToPixel(-90, 8)), "Polar clamp finite");
        Check(Math.Abs(GeoMath.DistanceMeters(new GeoPoint(0, 0), new GeoPoint(0, 1)) - 111195.0802) < 0.1, "Known equatorial distance");
        Check(GeoMath.DistanceMeters(new GeoPoint(0, 179.9), new GeoPoint(0, -179.9)) < 22300, "Dateline shortest distance");
        Check(GeoMath.DistanceMeters(new GeoPoint(0, 0), new GeoPoint(0, 180)) > 20000000, "Antipode distance finite");
        Console.WriteLine("PASS: " + checks + " core checks");
        return 0;
    }
}
