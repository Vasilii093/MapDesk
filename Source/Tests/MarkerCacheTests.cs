using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using AlpineQuestPort;
class MarkerCacheTests {
 static object Call(MapCanvas c,string name,params object[] args){return typeof(MapCanvas).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c,args);}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 [STAThread]static void Main(){
 using(var tiles=new TileStore(Path.Combine(Path.GetTempPath(),"MapDesk-marker-cache-tests"),null))using(var c=new MapCanvas(tiles){Size=new Size(800,600)})using(var image=new Bitmap(800,600))using(var g=Graphics.FromImage(image)){
 c.Settings.PointNames=true;var p=new GeoPoint(48,37){Name="First",ColorRgb=0x00ff00};
 var a=(Bitmap)Call(c,"MarkerImage",g,p);Check(Object.ReferenceEquals(a,Call(c,"MarkerImage",g,p)),"reuse unchanged sprite");
 p.Name="Second";var b=(Bitmap)Call(c,"MarkerImage",g,p);Check(!Object.ReferenceEquals(a,b),"rename refreshes label");
 p.ColorRgb=0xff0000;Check(!Object.ReferenceEquals(b,Call(c,"MarkerImage",g,p)),"color refreshes sprite");
 c.Settings.PointNames=false;var noName=(Bitmap)Call(c,"MarkerImage",g,p);Check(noName.Width==16,"hide names");
 int oldHeight=noName.Height;c.Settings.PointNames=true;c.Font=new Font(c.Font.FontFamily,18);var bigger=(Bitmap)Call(c,"MarkerImage",g,p);Check(bigger.Height>oldHeight,"font change invalidates cache");
 c.Document.Waypoints.Add(p);var top=new GeoPoint(48,37){Name="Top"};c.Document.Waypoints.Add(top);c.GoTo(p,12);
 Check(Object.ReferenceEquals(top,Call(c,"HitMarker",new Point(400,300))),"select topmost overlapping point");
 c.Document.Waypoints.Remove(top);Check(Object.ReferenceEquals(p,Call(c,"HitMarker",new Point(400,300))),"deletion reflected in hit test");
 p.Lat=49;Check(Call(c,"HitMarker",new Point(400,300))==null,"moved point is no longer hit");
 for(int i=0;i<5000;i++){p.Name="Marker "+i;Call(c,"MarkerImage",g,p);}
 var bytes=(long)typeof(MapCanvas).GetField("markerSpriteBytes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c);Check(bytes<=32*1024*1024,"cache stays within 32 MiB");
 Call(c,"ClearMarkerSprites");Check((long)typeof(MapCanvas).GetField("markerSpriteBytes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(c)==0,"cache cleanup");
 }
 }
}
