using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using AlpineQuestPort;
class MarkerPerf {
 [STAThread] static void Main(string[] args) {
 foreach(bool perspective in new[]{false,true}) using(var tiles=new TileStore(Path.Combine(Path.GetTempPath(),"MapDesk-markerbench"),null)) using(var c=new MapCanvas(tiles){Size=new Size(1920,1080)}) using(var bmp=new Bitmap(1920,1080)) using(var g=Graphics.FromImage(bmp)) {
 tiles.OnlineEnabled=false;c.Settings.Gpu=true;c.Settings.Perspective=perspective;c.Settings.PointNames=true;c.Settings.RangeRings=false;
 c.Document=MapFiles.Load(args[0]);c.Fit();
 var paint=typeof(MapCanvas).GetMethod("OnPaint",BindingFlags.Instance|BindingFlags.NonPublic);
 var a=new object[]{new PaintEventArgs(g,c.ClientRectangle)};
 for(int i=0;i<5;i++)paint.Invoke(c,a);
 double total=0;for(int i=0;i<40;i++){c.Pan(i%2==0?3:-3,0);var sw=Stopwatch.StartNew();paint.Invoke(c,a);sw.Stop();total+=sw.Elapsed.TotalMilliseconds;}
 Console.WriteLine("points="+c.Document.Waypoints.Count+" perspective="+perspective+" mean_ms="+(total/40).ToString("F2")+" backend="+c.RenderBackend);
 }
 }
}
