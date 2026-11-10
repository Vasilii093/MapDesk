using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using AlpineQuestPort;

static class LiveRayTests {
    static readonly BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
    static T Field<T>(object o,string name){return (T)o.GetType().GetField(name,flags).GetValue(o);}
    public static void Live(string output){
        var a=new GeoPoint(46.57,8.4){Name="Контрольный профиль DEM"};var b=RangeRings.Destination(a,3000,90);
        using(var engine=new RayProfileEngine(Path.Combine(output,"ray-live-network-"+Guid.NewGuid().ToString("N")))){
            var end=DateTime.UtcNow.AddSeconds(40);while(DateTime.UtcNow<end){engine.Step(a,b,a.Radio,true,DateTime.UtcNow);Application.DoEvents();if(engine.Radio!=null&&engine.Radio.Complete)break;Thread.Sleep(20);}
            Check(engine.Radio!=null&&engine.Radio.Complete,"live ray fresh online DEM profile completes");
            b=RangeRings.Destination(a,1800,90);end=DateTime.UtcNow.AddSeconds(10);while(DateTime.UtcNow<end){engine.Step(a,b,a.Radio,true,DateTime.UtcNow);Application.DoEvents();if(!engine.Stale&&engine.Radio!=null&&engine.Radio.Complete&&GeoMath.DistanceMeters(b,engine.Profile.Points.Last())<.01)break;Thread.Sleep(20);}
            Check(!engine.Stale&&engine.Radio.Complete&&GeoMath.DistanceMeters(b,engine.Profile.Points.Last())<.01,"live network profile follows new endpoint without stale samples");
            using(var image=RayProfilePainter.Render(engine,760,285,a.Name,true))image.Save(Path.Combine(output,"live-ray-network.png"));
        }
    }
    static void Load(RayProfileEngine engine,GeoPoint a,GeoPoint b,PointRadioOptions options){var end=DateTime.UtcNow.AddSeconds(5);while(DateTime.UtcNow<end){engine.Step(a,b,options,false,DateTime.UtcNow);Application.DoEvents();if(engine.Profile!=null&&engine.Profile.Heights.All(GeoMath.IsFinite))return;Thread.Sleep(10);}throw new Exception("live ray DEM timed out");}
    public static void Run(string output){
        var a=new GeoPoint(55.75,37.62){Name="Передатчик"};var b=RangeRings.Destination(a,900,90);a.Radio.Count=3;a.Radio.Frequencies=new[]{1080.0,2400,5800};a.Radio.HeightA=20;a.Radio.HeightB=12;
        var d=new MapDocument();d.Waypoints.Add(a);d.Waypoints.Add(new GeoPoint(55.76,37.63){Name="Другая точка"});
        foreach(string extension in new[]{"gpx","kml"}){string file=Path.Combine(output,"point-radio."+extension);if(extension=="gpx")MapFiles.SaveGpx(d,file);else MapFiles.SaveKml(d,file);var loaded=MapFiles.Load(file);Check(loaded.Waypoints[0].Radio.Count==3&&loaded.Waypoints[0].Radio.Frequencies[0]==1080&&loaded.Waypoints[0].Radio.HeightB==12&&loaded.Waypoints[1].Radio.Count==1,"per-point radio options persist independently / "+extension);}
        string cache=Path.Combine(output,"live-ray-dem");var profile=TerrainProfile.Create(a,b,false,0,30);
        foreach(var point in profile.Points){int x=(int)(GeoMath.LonToPixel(point.Lon,14)/256),y=(int)(GeoMath.LatToPixel(point.Lat,14)/256);string folder=Path.Combine(cache,"14",x.ToString());Directory.CreateDirectory(folder);string path=Path.Combine(folder,y+".png");if(!File.Exists(path))using(var bitmap=new Bitmap(256,256)){using(var g=Graphics.FromImage(bitmap))g.Clear(Color.FromArgb(128,120,0));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}}
        var longEnd=RangeRings.Destination(a,220000,90);string longCache=Path.Combine(output,"long-ray-"+Guid.NewGuid().ToString("N"));var longProfile=TerrainProfile.Create(a,longEnd,false,0,200);
        foreach(var point in longProfile.Points){int x=(int)(GeoMath.LonToPixel(point.Lon,14)/256),y=(int)(GeoMath.LatToPixel(point.Lat,14)/256);string folder=Path.Combine(longCache,"14",x.ToString());Directory.CreateDirectory(folder);string file=Path.Combine(folder,y+".png");if(!File.Exists(file))using(var tile=new Bitmap(256,256)){using(var g=Graphics.FromImage(tile))g.Clear(Color.FromArgb(128,120,0));tile.Save(file,System.Drawing.Imaging.ImageFormat.Png);}}
        using(var engine=new RayProfileEngine(longCache)){
            var start=DateTime.UtcNow;engine.Step(a,longEnd,a.Radio,false,start);engine.Step(a,longEnd,a.Radio,false,start.AddSeconds(1));
            var until=DateTime.UtcNow.AddSeconds(20);while(DateTime.UtcNow<until){engine.Step(a,longEnd,a.Radio,false,start.AddSeconds(61));if(engine.Radio!=null&&engine.Radio.Complete)break;Thread.Sleep(5);}
            Check(engine.Radio!=null&&engine.Radio.Complete,"long ray over 64 DEM tiles completes after former 60 second deadline");
        }
        using(var engine=new RayProfileEngine(cache)){
            Load(engine,a,b,a.Radio);Check(engine.Radio.Complete&&engine.Radio.Frequencies.Length==3,"live radio engine loads all three frequencies");
            a.Radio.Curvature=false;Load(engine,a,b,a.Radio);Check(engine.Radio.Ground.SequenceEqual(engine.Profile.Heights),"curvature checkbox removes only earth correction");
            a.Radio.Fresnel=false;Load(engine,a,b,a.Radio);Check(engine.Radio==null&&engine.Profile.Heights.All(GeoMath.IsFinite),"live terrain works with Fresnel disabled");a.Radio.Fresnel=true;a.Radio.Curvature=true;
            var shifted=RangeRings.Destination(a,800,90);engine.Step(a,shifted,a.Radio,false,DateTime.UtcNow.AddMilliseconds(500));Check(GeoMath.DistanceMeters(engine.Profile.Points.Last(),shifted)<.01,"live ray endpoint follows changed center");
            engine.Step(a,b,a.Radio,false,DateTime.UtcNow.AddMilliseconds(501));Check(engine.Stale,"throttled old plot explicitly marked stale");
            engine.Step(a,b,a.Radio,false,DateTime.UtcNow.AddMilliseconds(1000));Check(!engine.Stale&&GeoMath.DistanceMeters(engine.Profile.Points.Last(),b)<.01,"latest center replaces stale trajectory");
            Load(engine,a,b,a.Radio);using(var image=RayProfilePainter.Render(engine,760,285,a.Name,true))image.Save(Path.Combine(output,"live-ray-panel.png"));
            int revision=engine.Revision;engine.Step(a,b,a.Radio,false,DateTime.UtcNow.AddSeconds(2));Check(engine.Profile.Points.Length<=1025,"stationary live ray sample cap");revision=engine.Revision;engine.Step(a,b,a.Radio,false,DateTime.UtcNow.AddSeconds(3));Check(engine.Revision==revision,"unchanged ray does not recreate profile");
        }
        using(var tiles=new TileStore(Path.Combine(output,"live-ray-map"),null))using(var map=new MapCanvas(tiles){Size=new Size(1040,780)}){
            map.Settings.DataCards=true;using(var image=new Bitmap(map.Width,map.Height))map.DrawToBitmap(image,map.ClientRectangle);
            int searches=0;map.CoordinateSearchRequested+=delegate{searches++;};var search=map.Hud.SearchBounds;Check(search.Width>0&&search.Right<=map.Width,"search magnifier beside coordinate card");
            map.BeginMode(MapMode.Route);var click=new Point((int)(search.X+search.Width/2),(int)(search.Y+search.Height/2));
            typeof(MapCanvas).GetMethod("OnMouseDown",flags).Invoke(map,new object[]{new MouseEventArgs(MouseButtons.Left,1,click.X,click.Y,0)});typeof(MapCanvas).GetMethod("OnMouseUp",flags).Invoke(map,new object[]{new MouseEventArgs(MouseButtons.Left,1,click.X,click.Y,0)});
            Check(searches==1&&!map.HasDraft,"magnifier opens coordinate search without adding route vertex");map.CancelDraft();
            map.Settings.DataCards=false;map.Settings.RangeRings=false;map.Settings.ScaleBar=false;map.Settings.Elevation=false;map.Document.Waypoints.Add(a);map.Settings.RayEnabled=true;map.GoTo(b,14);
            using(var dialog=new MarkerDialog(a,null,map,cache)){
                dialog.Show();dialog.Controls.OfType<Button>().First(button=>button.Text.StartsWith("Профиль и параметры")).PerformClick();var editor=Field<PointRadioEditor>(dialog,"radioEditor");Field<NumericUpDown>(editor,"ha").Value=35;Check(a.Radio.HeightA==20,"point dialog edits isolated before Save");
                var until=DateTime.UtcNow.AddSeconds(1);while(DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(15);}
                using(var image=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new Rectangle(0,0,dialog.Width,dialog.Height));image.Save(Path.Combine(output,"point-radio-dialog.png"));}
                ((Button)dialog.AcceptButton).PerformClick();Check(dialog.RadioOptions.HeightA==35&&a.Radio.HeightA==20,"point dialog returns new options without mutating original");
            }
            map.StartLiveRay(a,cache);var untilReady=DateTime.UtcNow.AddSeconds(1);while(DateTime.UtcNow<untilReady){Application.DoEvents();Thread.Sleep(10);}
            using(var image=new Bitmap(map.Width,map.Height)){map.DrawToBitmap(image,map.ClientRectangle);image.Save(Path.Combine(output,"live-ray-map.png"));}
            var live=map.LiveRay;var engine=Field<RayProfileEngine>(live,"engine");var beforeProfile=engine.Profile;double originalA=a.Radio.HeightA,originalB=a.Radio.HeightB;var box=live.handles.A;Check(box.Width>0,"height endpoint hit region");var hit=new Point(live.Bounds.X+(int)(box.X+box.Width/2),live.Bounds.Y+(int)(box.Y+box.Height/2));
            live.Press(hit);int beforeZoom=map.Zoom;map.Wheel(hit,120,false);Check(a.Radio.HeightA==originalA+1&&a.Radio.HeightB==originalB&&map.Zoom==beforeZoom,"selected A wheel adds one metre without zooming map");Check(Object.ReferenceEquals(beforeProfile,engine.Profile),"height adjustment reuses DEM profile");
            map.Wheel(hit,60,false);Check(a.Radio.HeightA==originalA+1,"partial wheel notch accumulated");map.Wheel(hit,60,false);Check(a.Radio.HeightA==originalA+2,"two partial notches make one metre");
            a.Radio.HeightA=0;live.AdjustHeight(-120);Check(a.Radio.HeightA==0,"height wheel lower bound zero");a.Radio.HeightA=1000;live.AdjustHeight(120);Check(a.Radio.HeightA==1000,"height wheel upper bound 1000");a.Radio.HeightA=originalA;live.Tick();
            map.LiveRay.Expand();Application.DoEvents();var full=map.LiveRay.fullscreen;Check(full!=null&&full.Visible&&full.FormBorderStyle==FormBorderStyle.None,"live graph fullscreen opens");
            using(var image=new Bitmap(full.Width,full.Height)){full.DrawToBitmap(image,new Rectangle(0,0,full.Width,full.Height));image.Save(Path.Combine(output,"fullscreen-ray.png"));}
            var fullHandles=Field<EndpointHandles>(full,"handles");var graphBeforeHover=Field<Bitmap>(full,"image");var plot=fullHandles.Axes.Plot;typeof(RayFullscreen).GetMethod("OnMouseMove",flags).Invoke(full,new object[]{new MouseEventArgs(MouseButtons.None,0,(int)(plot.Left+plot.Width*.5),40+(int)(plot.Top+plot.Height*.5),0)});
            using(var shot=new Bitmap(full.Width,full.Height)){full.DrawToBitmap(shot,new Rectangle(0,0,full.Width,full.Height));shot.Save(Path.Combine(output,"fullscreen-ray-crosshair.png"));}Check(Object.ReferenceEquals(graphBeforeHover,Field<Bitmap>(full,"image")),"crosshair motion reuses graph bitmap without recalculation");Check(Field<Point?>(full,"probe").HasValue,"fullscreen mouse position drives probe");typeof(RayFullscreen).GetMethod("OnMouseLeave",flags).Invoke(full,new object[]{EventArgs.Empty});Check(!Field<Point?>(full,"probe").HasValue,"crosshair clears when mouse leaves");var endB=fullHandles.B;Console.WriteLine("Endpoint before click: "+endB+" hit="+fullHandles.Hit(new Point((int)(endB.X+endB.Width/2),(int)(endB.Y+endB.Height/2)))+" A="+a.Radio.HeightA+" B="+a.Radio.HeightB);typeof(RayFullscreen).GetMethod("OnMouseDown",flags).Invoke(full,new object[]{new MouseEventArgs(MouseButtons.Left,1,(int)(endB.X+endB.Width/2),40+(int)(endB.Y+endB.Height/2),0)});typeof(RayFullscreen).GetMethod("OnMouseWheel",flags).Invoke(full,new object[]{new MouseEventArgs(MouseButtons.None,0,50,50,120)});Console.WriteLine("Endpoint after wheel: selected="+live.SelectedEndpoint+" A="+a.Radio.HeightA+" B="+a.Radio.HeightB+" expected="+originalA+" / "+(originalB+1));Check(a.Radio.HeightB==originalB+1&&a.Radio.HeightA==originalA,"fullscreen B wheel edits only B");a.Radio.HeightB=originalB;live.Tick();
            typeof(Form).GetMethod("OnKeyDown",flags).Invoke(full,new object[]{new KeyEventArgs(Keys.Escape)});Check(full.IsDisposed&&map.LiveRay.Visible,"Escape returns from fullscreen without disabling ray");
            using(var warm=new Bitmap(map.Width,map.Height))using(var graphics=Graphics.FromImage(warm))map.LiveRay.Draw(graphics);
            var cachedImage=Field<Bitmap>(map.LiveRay,"image");using(var target=new Bitmap(map.Width,map.Height))using(var g=Graphics.FromImage(target)){var watch=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<100;i++)map.LiveRay.Draw(g);watch.Stop();Console.WriteLine("LIVE PANEL cached composition mean ms="+(watch.Elapsed.TotalMilliseconds/100).ToString("F3"));}Check(Object.ReferenceEquals(cachedImage,Field<Bitmap>(map.LiveRay,"image")),"cached overlay painting reuses graph bitmap");
            var bounds=map.LiveRay.Bounds;Check(bounds.Width>0&&map.LiveRay.Hit(new Point(bounds.Left+30,bounds.Top+80)),"live panel hit area");
            map.BeginMode(MapMode.Route);var inside=new Point(bounds.Left+50,bounds.Top+85);typeof(MapCanvas).GetMethod("OnMouseDown",flags).Invoke(map,new object[]{new MouseEventArgs(MouseButtons.Left,1,inside.X,inside.Y,0)});typeof(MapCanvas).GetMethod("OnMouseUp",flags).Invoke(map,new object[]{new MouseEventArgs(MouseButtons.Left,1,inside.X,inside.Y,0)});Check(!map.HasDraft,"live overlay click never adds route vertex");map.CancelDraft();
            int zoom=map.Zoom;map.Wheel(inside,120,false);Check(map.Zoom==zoom,"live overlay wheel never zooms map");
            map.LiveRay.Press(new Point(bounds.Right-12,bounds.Top+12));Check(!map.LiveRay.Visible&&map.Settings.RayEnabled&&a.Radio.Enabled,"closing panel preserves anchored ray");
            Check(!Field<System.Windows.Forms.Timer>(map.LiveRay,"timer").Enabled,"closed profile stops timer");map.StartLiveRay(a,cache);Check(map.LiveRay.Visible,"same point profile reopens after close");map.LiveRay.Stop();map.StartLiveRay(a,cache);Check(map.LiveRay.Visible,"repeated close and reopen");map.Document.Waypoints.Remove(a);map.LiveRay.Tick();Check(!map.LiveRay.Visible,"deleted anchor stops live profile");
        }
    }
}




