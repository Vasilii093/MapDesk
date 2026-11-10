using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using AlpineQuestPort;
class PerfBench {
 [STAThread]static void Main(string[] argv){if(argv.Length>0)PerspectiveRaster.WorkerOverride=int.Parse(argv[0]);foreach(bool perspective in new[]{false,true})using(var tiles=new TileStore(Path.Combine(Path.GetTempPath(),"MapDesk-perf-no-network"),null))using(var c=new MapCanvas(tiles){Size=new Size(1920,1080)})using(var bmp=new Bitmap(1920,1080))using(var g=Graphics.FromImage(bmp)){
 c.Settings.Gpu=argv.Length<2||argv[1]!="cpu";c.Settings.Perspective=perspective;c.Settings.Tilt=45;c.GoTo(new GeoPoint(46.57,8.4),8);var paint=typeof(MapCanvas).GetMethod("OnPaint",BindingFlags.Instance|BindingFlags.NonPublic);var args=new object[]{new System.Windows.Forms.PaintEventArgs(g,c.ClientRectangle)};
 paint.Invoke(c,args);Console.WriteLine(c.RenderBackend);double total=0,max=0;for(int i=0;i<10;i++){c.Pan(5,3);var sw=Stopwatch.StartNew();paint.Invoke(c,args);sw.Stop();total+=sw.Elapsed.TotalMilliseconds;max=Math.Max(max,sw.Elapsed.TotalMilliseconds);}Console.WriteLine((perspective?"perspective":"flat")+" 1920x1080 pan: mean_ms="+(total/10).ToString("F2")+" max_ms="+max.ToString("F2"));}
 }
}
