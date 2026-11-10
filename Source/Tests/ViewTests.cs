using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using AlpineQuestPort;
class ViewTests {
 static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS: "+name);}
 static GeoPoint From(MapCanvas c,Point p){return (GeoPoint)typeof(MapCanvas).GetMethod("FromScreen",flags).Invoke(c,new object[]{p});}
 [STAThread] static int Main(string[] args){try{Application.EnableVisualStyles();string output=args[0];Directory.CreateDirectory(output);LayerTests.Run(output);EditorTests.Run(output);LiveRayTests.Run(output);ProfileTests.Run(output);if(args.Length>1&&args[1]=="--network"){LiveRayTests.Live(output);LayerTests.Live(output);}
  foreach(var origin in new[]{new GeoPoint(50.25,36.46),new GeoPoint(70,179.9)})foreach(double radius in new[]{50.0,50000,150000})foreach(int bearing in new[]{0,45,90,180,270}){var point=RangeRings.Destination(origin,radius,bearing);Check(Math.Abs(GeoMath.DistanceMeters(origin,point)-radius)<0.001,"range ring geodesic radius "+radius+" / "+bearing);}
  Check(RangeRings.Step(new GeoPoint(50,36),9,900,1200,50000)==50000,"manual ring spacing");
  var ringSettings=new ViewSettings{RangeRings=true,RingStepMeters=50000};ringSettings.Save(Path.Combine(output,"ring-settings.xml"));var restoredRing=ViewSettings.Load(Path.Combine(output,"ring-settings.xml"));Check(restoredRing.RangeRings&&restoredRing.RingStepMeters==50000,"ring settings persist");
  using(var tiles=new TileStore(Path.Combine(output,"rings-preview"),null))using(var canvas=new MapCanvas(tiles){Size=new Size(1000,800)}){
   canvas.Settings.DataCards=false;canvas.Settings.ScaleBar=false;canvas.Settings.Elevation=false;canvas.Settings.RingStepMeters=50000;canvas.GoTo(new GeoPoint(50.25,36.46),9);
   foreach(bool tilt in new[]{false,true}){canvas.Settings.Perspective=tilt;using(var shot=new Bitmap(1000,800)){canvas.DrawToBitmap(shot,canvas.ClientRectangle);shot.Save(Path.Combine(output,tilt?"rings-perspective.png":"rings-flat.png"));var centerPixel=shot.GetPixel(500,400);Check(centerPixel.A==255,"ring renderer flat and perspective "+tilt);}}
   canvas.Settings.RangeRings=false;using(var shot=new Bitmap(1000,800)){canvas.DrawToBitmap(shot,canvas.ClientRectangle);Check(!canvas.Settings.RangeRings,"rings can be disabled independently");}
  }
  var directionSettings=new ViewSettings{Rotation=75,RayEnabled=true,RayLat=55.75,RayLon=37.62};directionSettings.Save(Path.Combine(output,"direction-settings.xml"));var restoredDirection=ViewSettings.Load(Path.Combine(output,"direction-settings.xml"));Check(restoredDirection.Rotation==75&&restoredDirection.RayEnabled&&restoredDirection.RayLat==55.75&&restoredDirection.RayLon==37.62,"rotation and ray anchor settings persist");
  var colored=new MapDocument();colored.Waypoints.Add(new GeoPoint(55.75,37.62){Name="Красная метка",ColorRgb=0xFA1234,Group="Маршрут А"});colored.Waypoints.Add(new GeoPoint(55.76,37.63){Name="Чёрная",ColorRgb=0});
  foreach(string extension in new[]{"gpx","kml"}){string file=Path.Combine(output,"marker-color."+extension);if(extension=="gpx")MapFiles.SaveGpx(colored,file);else MapFiles.SaveKml(colored,file);var read=MapFiles.Load(file);Check(read.Waypoints[0].ColorRgb==0xFA1234&&read.Waypoints[1].ColorRgb==0&&read.Waypoints[0].Name=="Красная метка"&&read.Waypoints[0].Group=="Маршрут А"&&read.Waypoints[1].Group=="","marker name and color round trip "+extension);}
  using(var dialog=new MarkerDialog(colored.Waypoints[0])){dialog.Show();((TextBox)typeof(MarkerDialog).GetField("name",flags).GetValue(dialog)).Text=" Новое название ";((Button)dialog.AcceptButton).PerformClick();Check(dialog.MarkerName=="Новое название"&&colored.Waypoints[0].Name=="Красная метка","marker dialog keeps edits isolated until apply");}
  using(var groups=new MarkerGroupsDialog(colored.Waypoints,colored.Waypoints[0])){groups.Show();var choices=(CheckedListBox)typeof(MarkerGroupsDialog).GetField("list",flags).GetValue(groups);choices.SetItemChecked(1,true);((ComboBox)typeof(MarkerGroupsDialog).GetField("group",flags).GetValue(groups)).Text="Новая группа";using(var shot=new Bitmap(groups.Width,groups.Height)){groups.DrawToBitmap(shot,groups.ClientRectangle);shot.Save(Path.Combine(output,"groups.png"));}((Button)groups.AcceptButton).PerformClick();Check(groups.Chosen.Count==2&&groups.GroupName=="Новая группа"&&colored.Waypoints[0].Group=="Маршрут А","bulk group dialog selects existing points without premature changes");}
  string overCache=Path.Combine(output,"overzoom-cache");Directory.CreateDirectory(Path.Combine(overCache,"18","100000"));using(var tile=new Bitmap(256,256)){using(var g=Graphics.FromImage(tile))g.Clear(Color.FromArgb(220,110,40));tile.Save(Path.Combine(overCache,"18","100000","100000.png"),System.Drawing.Imaging.ImageFormat.Png);}
  using(var tiles=new TileStore(overCache,null,new MapSource{Id="overzoom-test",Name="Test",MinZoom=0,MaxZoom=18,Template="https://example.invalid/{z}/{x}/{y}.png",DiskCache=true}))using(var canvas=new MapCanvas(tiles){Size=new Size(800,500)}){
   bool loaded=false;for(int i=0;i<100&&!loaded;i++){using(var img=tiles.GetTile(18,100000,100000))loaded=img!=null;if(!loaded)Thread.Sleep(10);}Check(loaded,"overzoom source cached");
   var center=new GeoPoint(GeoMath.PixelToLat(100000*256.0+128,18),GeoMath.PixelToLon(100000*256.0+128,18));canvas.Settings.DataCards=false;canvas.Settings.Crosshair=false;canvas.Settings.ScaleBar=false;canvas.Settings.RangeRings=false;
   foreach(bool gpu in new[]{false,true})foreach(int zoom in new[]{19,30,40}){canvas.Settings.Gpu=gpu;canvas.Settings.Perspective=true;canvas.Settings.Tilt=45;canvas.GoTo(center,zoom);using(var frame=new Bitmap(800,500)){canvas.DrawToBitmap(frame,canvas.ClientRectangle);var color=frame.GetPixel(400,250);Check(color.R>200&&color.G>90&&color.B<60,"overzoom retains source tile GPU="+gpu+" z="+zoom);}Check(canvas.SourceZoom==18,"source zoom never exceeds provider limit");}
   canvas.GoTo(center,30);var anchor=new Point(550,270);var before=From(canvas,anchor);canvas.ChangeZoom(1,anchor);Check(canvas.Zoom==31&&GeoMath.DistanceMeters(before,From(canvas,anchor))<0.001,"overzoom preserves cursor anchor beyond int shifts");canvas.Settings.SourceZoomOffset=2;Check(canvas.SourceZoom==16,"manual lower detail for provider placeholder");
   Check(GeoMath.WorldSize(40)>1e14&&40075016.686/GeoMath.WorldSize(30)*96/0.0254<1,"display supports scale larger than 1 to 1");
  }
  var scaleSettings=new ViewSettings{ScaleBar=false,DataCards=false};scaleSettings.Save(Path.Combine(output,"scale-settings.xml"));Check(!ViewSettings.Load(Path.Combine(output,"scale-settings.xml")).ScaleBar,"scale bar preference saved");
  using(var hud=new MapHud())using(var bitmap=new Bitmap(800,600))using(var g=Graphics.FromImage(bitmap)){var bounds=hud.DrawScale(g,800,600,new GeoPoint(55.75,37.62),14,new ViewSettings{DataCards=false,Perspective=true});Check(bounds.Width>0&&bounds.Bottom<600,"standalone scale independent of cards");bitmap.Save(Path.Combine(output,"scale-bar.png"));}
  var defaults=new ViewSettings();Check(defaults.MaxFps==300,"default frame ceiling 300");defaults.MaxFps=301;bool badFps=false;try{defaults.Validate();}catch(ArgumentException){badFps=true;}Check(badFps,"frame limit validation");
  foreach(int ceiling in new[]{60,300})using(var pacingForm=new Form{Size=new Size(100,80),ShowInTaskbar=false}){
   FramePump pump=null;int frames=0;var stamps=new System.Collections.Generic.List<long>();
   using(var finish=new System.Windows.Forms.Timer{Interval=700}){
    pump=new FramePump(pacingForm,delegate{frames++;stamps.Add(System.Diagnostics.Stopwatch.GetTimestamp());pump.Request();});pump.Limit=ceiling;
    finish.Tick+=delegate{finish.Stop();pacingForm.Close();};pacingForm.Shown+=delegate{finish.Start();pump.Request();};Application.Run(pacingForm);
    bool precise=pump.HighResolution;pump.Dispose();double rate=stamps.Count>1?(stamps.Count-1)*System.Diagnostics.Stopwatch.Frequency/(double)(stamps[stamps.Count-1]-stamps[0]):0;
    Console.WriteLine("FRAME PACING: limit="+ceiling+" delivered="+rate.ToString("F1")+" high_resolution="+precise);Check(frames>2&&rate<=ceiling*1.1,"frame pacing respects ceiling "+ceiling);
   }
  }
  using(var idleForm=new Form{Size=new Size(100,80),ShowInTaskbar=false}){
   int calls=0;using(var pump=new FramePump(idleForm,delegate{calls++;}))using(var finish=new System.Windows.Forms.Timer{Interval=150}){finish.Tick+=delegate{idleForm.Close();};idleForm.Shown+=delegate{pump.Request();finish.Start();};Application.Run(idleForm);Check(calls==1,"frame scheduler sleeps with unchanged content");}
  }
  using(var engine=new ProjectionEngine()){
   var catalog=CrsEntry.Load();var seven=catalog.Find(v=>v.Id=="EPSG:28407");
   foreach(string query in new[]{"СК42","СК-42","СК‑42","ск 42","sk42","СК42 зона 7"})Check(CoordinatesDialog.Matches(seven,query),"SK42 catalog spelling "+query);
   Check(!CoordinatesDialog.Matches(catalog.Find(v=>v.Id=="EPSG:4326"),"СК42"),"SK42 excludes WGS84");
   Check(CoordinatesDialog.Parse("X = 6 181 691,999")==6181691.999&&CoordinatesDialog.Parse("Y = 7\u00a0413\u202f462.200")==7413462.2,"SK42 coordinate separators and labels");
   using(var dialog=new CoordinatesDialog(engine,catalog,seven,new GeoPoint(55.75,37.62))){
    dialog.Show();Application.DoEvents();
    Check(((ListBox)typeof(CoordinatesDialog).GetField("list",flags).GetValue(dialog)).Items.Count==4,"only four coordinate choices");
    Check(CoordinatesDialog.ParseAngle("48° 30′ 0″")==48.5,"WGS84 degrees minutes seconds input");
    foreach(AngleFormat af in Enum.GetValues(typeof(AngleFormat))){var view=new ViewSettings{Format=af};Check(Math.Abs(CoordinatesDialog.ParseAngle(view.Angle(-0.5))+0.5)<0.000001,"negative WGS84 format round trip "+af);}
    var formats=(ListBox)typeof(CoordinatesDialog).GetField("list",flags).GetValue(dialog);formats.SelectedIndex=3;
    Check(dialog.SelectedFormat==AngleFormat.Градусы_минуты_секунды,"select WGS84 seconds format");formats.SelectedIndex=0;
    ((TextBox)typeof(CoordinatesDialog).GetField("definition",flags).GetValue(dialog)).Text="EPSG:28408";
    ((TextBox)typeof(CoordinatesDialog).GetField("first",flags).GetValue(dialog)).Text="6 181 691,999";
    ((TextBox)typeof(CoordinatesDialog).GetField("second",flags).GetValue(dialog)).Text="7 413 462,200";
    ((Button)dialog.AcceptButton).PerformClick();Check(dialog.DialogResult==DialogResult.OK&&dialog.Chosen.Id=="EPSG:28407"&&GeoMath.DistanceMeters(dialog.Target,new GeoPoint(55.75,37.62))<0.02,"SK42 go by XY derives zone from full Y");
   }
  }
  using(var gpuCache=new GpuPerspective())using(var original=new Bitmap(256,256))using(var replacement=new Bitmap(256,256))using(var target=new Bitmap(300,300))using(var graphics=Graphics.FromImage(target)){
   using(var draw=Graphics.FromImage(original))draw.Clear(Color.Red);using(var draw=Graphics.FromImage(replacement))draw.Clear(Color.Blue);
   for(int frame=0;frame<4;frame++)using(var clone=(Image)original.Clone()){clone.Tag=original;gpuCache.DrawScene(graphics,300,300,0,delegate(GpuPerspective r){r.DrawTile(clone,new RectangleF(0,0,300,300),new RectangleF(0,0,256,256));});}
   Check(gpuCache.TextureUploads==1&&target.GetPixel(150,150).R>250,"GPU tile survives cloned images across frames with one upload");
   using(var clone=(Image)replacement.Clone()){clone.Tag=replacement;gpuCache.DrawScene(graphics,300,300,0,delegate(GpuPerspective r){r.DrawTile(clone,new RectangleF(0,0,300,300),new RectangleF(0,0,256,256));});}
   Check(gpuCache.TextureUploads==2&&target.GetPixel(150,150).B>250,"GPU refreshed tile replaces old pixels");
   for(int frame=0;frame<120;frame++)gpuCache.DrawScene(graphics,300,300,0,delegate(GpuPerspective r){});
   Check(gpuCache.TextureBytes==0,"unused GPU textures evicted");
  }
  using(var gpu=new GpuPerspective())using(var cpu=new PerspectiveRaster())using(var plane=new Bitmap(500,500)){
   Console.WriteLine("GPU VERIFIED: "+gpu.Renderer);
   for(int y=0;y<500;y++)for(int x=0;x<500;x++)plane.SetPixel(x,y,Color.FromArgb(x/2,y/2,80));
   foreach(int rotation in new[]{0,45,90,225})foreach(int angle in new[]{0,15,45,60})foreach(int width in new[]{129,173})using(var expected=new Bitmap(width,97))using(var actual=new Bitmap(width,97)){
    using(var g=Graphics.FromImage(expected))cpu.Draw(g,plane,width,97,-250,-250,angle,rotation);
    using(var g=Graphics.FromImage(actual))gpu.DrawScene(g,width,97,angle,delegate(GpuPerspective renderer){renderer.DrawTile(plane,new RectangleF(width/2f-250,97/2f-250,500,500),new RectangleF(0,0,500,500));},rotation);
    int error=0;for(int y=3;y<94;y++)for(int x=3;x<width-3;x++){var a=actual.GetPixel(x,y);var b=expected.GetPixel(x,y);error=Math.Max(error,Math.Max(Math.Abs(a.R-b.R),Math.Max(Math.Abs(a.G-b.G),Math.Abs(a.B-b.B))));}
    Check(error<=2,"GPU projection rotation "+rotation+", resize, pixel alignment "+angle+" / "+width+" error="+error);
   }
  }
  Check(PerspectiveRaster.WorkerCount(1920,1080,32)==8,"Full HD adaptive worker budget");
  Check(PerspectiveRaster.WorkerCount(3840,2160,32)==12,"large frame worker cap");
  Check(PerspectiveRaster.WorkerCount(160,100,32)==1,"small frame avoids parallel overhead");
  Check(PerspectiveRaster.WorkerCount(1920,1080,1)==1&&PerspectiveRaster.WorkerCount(1920,1080,4)==2,"low core count leaves CPU headroom");
  using(var raster=new PerspectiveRaster())using(var plane=new Bitmap(161,131)){
   for(int y=0;y<plane.Height;y++)for(int x=0;x<plane.Width;x++)plane.SetPixel(x,y,Color.FromArgb((x*7+y)%256,(x+y*3)%256,(x*3+y*5)%256));
   foreach(int angle in new[]{15,45,60})foreach(int width in new[]{67,99})using(var serial=new Bitmap(width,73))using(var parallel=new Bitmap(width,73)){
    using(var g=Graphics.FromImage(serial)){PerspectiveRaster.WorkerOverride=1;raster.Draw(g,plane,width,73,-80,-65,angle);}
    foreach(int workers in new[]{2,8,12}){using(var g=Graphics.FromImage(parallel)){PerspectiveRaster.WorkerOverride=workers;raster.Draw(g,plane,width,73,-80,-65,angle);}bool same=true;for(int y=0;y<73;y++)for(int x=0;x<width;x++)if(serial.GetPixel(x,y)!=parallel.GetPixel(x,y))same=false;Check(same,"parallel raster pixel equivalence / resize / band edges "+angle+" / "+width+" / "+workers);}
   }
  }PerspectiveRaster.WorkerOverride=0;
  foreach(int angle in new[]{0,15,45,60})foreach(var p in new[]{new PointF(-600,-300),new PointF(0,0),new PointF(600,300)}){var q=PerspectiveMath.Forward(p.X,p.Y,800,angle);var r=PerspectiveMath.Inverse(q.X,q.Y,800,angle);Check(Math.Abs(p.X-r.X)<0.001&&Math.Abs(p.Y-r.Y)<0.001,"perspective inverse "+angle);}
  var settings=new ViewSettings{Perspective=true,Tilt=60,Format=AngleFormat.Градусы_минуты_секунды,Units=DistanceUnits.Мили_и_футы,Elevation=true};settings.Save(Path.Combine(output,"settings.xml"));var restored=ViewSettings.Load(Path.Combine(output,"settings.xml"));Check(restored.Tilt==60&&restored.Perspective&&restored.Units==settings.Units,"settings persist");Check(settings.Angle(12.9999999).StartsWith("13° 00′ 00.00"),"DMS carry");Check(settings.Distance(1609.344).StartsWith("1.00")||settings.Distance(1609.344).StartsWith("1,00"),"mile units");
  Check(Math.Abs(ElevationStore.Decode(Color.FromArgb(137,219,68))-2523.265625)<1e-8,"Terrarium official example");
  Check(Math.Abs(PointReadout.Bearing(new GeoPoint(0,0),new GeoPoint(1,0)))<1e-8,"true bearing north");Check(Math.Abs(PointReadout.Bearing(new GeoPoint(0,0),new GeoPoint(0,1))-90)<1e-8,"true bearing east");Check(PointReadout.Direction(270,BearingUnits.Деления_6000).StartsWith("4500"),"6000 bearing divisions");Check(PointReadout.Direction(270,BearingUnits.Деления_6400).StartsWith("4800"),"6400 bearing divisions");Check(PointReadout.Wgs(new GeoPoint(-12,-34)).Contains("S")&&PointReadout.Wgs(new GeoPoint(-12,-34)).Contains("W"),"coordinate hemispheres");Check(PointReadout.ScaleLength(340)==200,"scale nice length");
  string asyncCache=Path.Combine(output,"async-dem");Directory.CreateDirectory(Path.Combine(asyncCache,"2","1"));using(var b=new Bitmap(256,256)){using(var g=Graphics.FromImage(b))g.Clear(Color.FromArgb(128,100,128));b.Save(Path.Combine(asyncCache,"2","1","1.png"),System.Drawing.Imaging.ImageFormat.Png);}
  using(var asyncDem=new ElevationStore(asyncCache)){
   int uiThread=Thread.CurrentThread.ManagedThreadId,completedThread=uiThread;asyncDem.Changed+=delegate{completedThread=Thread.CurrentThread.ManagedThreadId;};ElevationTile decoded=null;
   for(int i=0;i<200&&decoded==null;i++){decoded=asyncDem.Get(2,1,1);Thread.Sleep(10);}Check(decoded!=null&&decoded.Heights[0]==100.5f,"background DEM decode result");Check(decoded.Image==null,"DEM matrix does not allocate a colored layer");Check(completedThread!=uiThread,"DEM conversion completes off UI thread");Check(Object.ReferenceEquals(decoded,asyncDem.Get(2,1,1)),"decoded DEM reused without conversion");Check(asyncDem.PendingBuilds==0,"DEM work queue drains");
  }
  using(var raw=new Bitmap(256,256)){using(var g=Graphics.FromImage(raw))g.Clear(Color.FromArgb(128,100,128));using(var dem=ElevationStore.Build(raw,10,400)){Check(dem.Heights[100]==100.5f,"DEM height decoding");Check(dem.Image.GetPixel(10,10).A==255,"DEM color map");}}
  using(var tiles=new TileStore(Path.Combine(output,"tile-cache"),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"default_map")))using(var c=new MapCanvas(tiles){Size=new Size(1000,600)}){
   c.Settings.DataCards=false;c.GoTo(new GeoPoint(55.75,37.62),14);var marker=new GeoPoint(55.75,37.62){Name="Метка",ColorRgb=0xAA2244};c.Document.Waypoints.Add(marker);
   typeof(MapCanvas).GetMethod("OnMouseDown",flags).Invoke(c,new object[]{new MouseEventArgs(MouseButtons.Right,1,500,300,0)});typeof(ContextMenuStrip).GetMethod("OnOpening",flags).Invoke(c.ContextMenuStrip,new object[]{new System.ComponentModel.CancelEventArgs()});
   Check(Object.ReferenceEquals(c.Selected,marker)&&c.ContextMenuStrip.Items[0].Available,"right click targets marker");GeoPoint actionPoint=null;string actionName=null;c.MarkerAction+=delegate(GeoPoint point,string action){actionPoint=point;actionName=action;};typeof(MapCanvas).GetMethod("RunMarkerAction",flags).Invoke(c,new object[]{"color"});Check(Object.ReferenceEquals(actionPoint,marker)&&actionName=="color","marker context action preserves target");
   typeof(MapCanvas).GetMethod("OnMouseDown",flags).Invoke(c,new object[]{new MouseEventArgs(MouseButtons.Right,1,800,500,0)});typeof(ContextMenuStrip).GetMethod("OnOpening",flags).Invoke(c.ContextMenuStrip,new object[]{new System.ComponentModel.CancelEventArgs()});Check(!c.ContextMenuStrip.Items[0].Available,"empty map hides marker editing actions");c.Document.Waypoints.Clear();c.Selected=null;c.Settings.DataCards=true;
   c.Settings.Gpu=false;c.Settings.Perspective=true;c.GoTo(new GeoPoint(46.57,8.4),8);
   using(var cpuFrame=new Bitmap(1000,600))using(var fallbackFrame=new Bitmap(1000,600)){
    c.DrawToBitmap(cpuFrame,c.ClientRectangle);Check(c.RenderBackend=="CPU","GPU can be disabled");
    c.Settings.Gpu=true;typeof(MapCanvas).GetField("mapGpuFailed",flags).SetValue(c,true);c.DrawToBitmap(fallbackFrame,c.ClientRectangle);
    Check(c.RenderBackend=="CPU"&&cpuFrame.GetPixel(750,400)==fallbackFrame.GetPixel(750,400),"GPU unavailable state uses CPU raster");
    typeof(MapCanvas).GetField("mapGpuFailed",flags).SetValue(c,false);
   }
   c.Settings.Rotation=45;var priorCenter=c.Center;int priorZoom=c.Zoom;c.Settings.DataCards=false;c.Settings.ScaleBar=false;c.Wheel(new Point(500,300),120,true);Check(c.Settings.Rotation==60&&c.Zoom==priorZoom&&GeoMath.DistanceMeters(c.Center,priorCenter)<0.001,"shift wheel rotates without moving center or zoom");c.Wheel(new Point(500,300),-120,true);Check(c.Settings.Rotation==45,"reverse shift wheel");
   c.Settings.Rotation=45;c.Settings.Perspective=true;c.Settings.Tilt=60;c.GoTo(new GeoPoint(55.75,37.62),12);var a=new Point(220,160);GeoPoint before=From(c,a);c.ChangeZoom(1,a);Check(GeoMath.DistanceMeters(before,From(c,a))<0.01,"perspective cursor anchored zoom");
   var start=new Point(400,220);var end=new Point(520,330);before=From(c,start);typeof(MapCanvas).GetMethod("OnMouseDown",flags).Invoke(c,new object[]{new MouseEventArgs(MouseButtons.Left,1,start.X,start.Y,0)});typeof(MapCanvas).GetMethod("OnMouseMove",flags).Invoke(c,new object[]{new MouseEventArgs(MouseButtons.Left,0,end.X,end.Y,0)});Check(GeoMath.DistanceMeters(before,From(c,end))<0.02,"perspective dragging keeps geographic anchor");typeof(MapCanvas).GetMethod("OnMouseUp",flags).Invoke(c,new object[]{new MouseEventArgs(MouseButtons.Left,1,end.X,end.Y,0)});
   c.Settings.Crosshair=false;c.Settings.DataCards=false;c.Settings.Elevation=false;using(var off=new Bitmap(1000,600))using(var on=new Bitmap(1000,600)){c.DrawToBitmap(off,c.ClientRectangle);c.Settings.Elevation=true;c.DrawToBitmap(on,c.ClientRectangle);Check(off.GetPixel(500,300).ToArgb()==on.GetPixel(500,300).ToArgb(),"DEM does not tint map raster");}
   c.Settings.Rotation=0;c.Settings.Crosshair=false;c.Settings.DataCards=false;c.GoTo(new GeoPoint(0,0),19);c.Document.Waypoints.Add(new GeoPoint(-80,0));c.Fix=new GeoPoint(-80,1);var longTrack=new Track();longTrack.Points.Add(new GeoPoint(-50,0));longTrack.Points.Add(new GeoPoint(50,0));c.Document.Tracks.Add(longTrack);using(var b=new Bitmap(1000,600)){c.CreateControl();c.DrawToBitmap(b,c.ClientRectangle);Color pixel=b.GetPixel(500,300);Check(pixel.G>60&&pixel.G<150&&pixel.R<80,"perspective long line clipping and far markers");}
  }
  using(var form=new MainForm(null,true)){
   form.Show();Application.DoEvents();var c=(MapCanvas)typeof(MainForm).GetField("map",flags).GetValue(form);var editPoint=new GeoPoint(55.75,37.62){Name="До"};c.Document.Waypoints.Add(editPoint);c.Selected=editPoint;form.ApplyMarkerChanges(editPoint,"После",0xAABBCC);Check(editPoint.Name=="После"&&editPoint.ColorRgb==0xAABBCC&&editPoint.Lat==55.75&&Object.ReferenceEquals(c.Selected,editPoint),"editing marker preserves position and selection");editPoint.Radio.Enabled=false;form.PinRay(editPoint);Check(editPoint.Radio.Enabled&&c.LiveRay.Visible,"explicit pin restores previously disabled point profile");c.LiveRay.Stop();var rayControl=(ToolStripButton)typeof(MainForm).GetField("rayButton",flags).GetValue(form);rayControl.PerformClick();Check(c.LiveRay.Visible&&c.LiveRay.Anchor==editPoint,"ray button reopens closed graph on same point");Check(c.Top<180,"map uses removed banner space");var rayLat=c.Settings.RayLat;var rayLon=c.Settings.RayLon;c.GoTo(new GeoPoint(55.76,37.65),14);c.Settings.Rotation=75;c.Settings.Perspective=true;using(var shot=new Bitmap(c.Width,c.Height)){c.DrawToBitmap(shot,c.ClientRectangle);shot.Save(Path.Combine(output,"rotation-ray.png"));}Check(c.Settings.RayEnabled&&c.Settings.RayLat==rayLat&&c.Settings.RayLon==rayLon,"pinned ray origin survives pan and rotation");form.AssignGroup(new[]{editPoint},"Группа 1");Check(editPoint.Group=="Группа 1"&&Object.ReferenceEquals(c.Selected,editPoint),"existing marker grouped without losing selection");form.AssignGroup(new[]{editPoint},"");Check(editPoint.Group=="","remove marker from group");form.RemoveObject(editPoint);Check(!c.Settings.RayEnabled,"deleting anchor removes ray");c.Settings.Rotation=0;Check(!c.Document.Waypoints.Contains(editPoint)&&c.Selected==null,"delete marker clears document and selection");c.GoTo(new GeoPoint(46.57,8.4),8);c.Settings.Perspective=true;c.Settings.Tilt=45;c.Settings.Elevation=true;c.Document.Waypoints.Add(new GeoPoint(46.575,8.408){Name="Учебная точка"});c.Selected=c.Document.Waypoints[0];
   if(args.Length>1&&args[1]=="--network"){
    c.Elevation.Tiles.OnlineEnabled=true;double? h=null;for(int i=0;i<150;i++){h=c.Elevation.At(c.Center,c.Zoom);Thread.Sleep(100);Application.DoEvents();if(h.HasValue&&i>10&&c.Elevation.Tiles.PendingCount==0)break;}Check(h.HasValue&&h.Value>-500&&h.Value<9000,"DEM sample in Alps (network enabled; disk cache may be used)");Console.WriteLine("DEM sample: "+h);c.Elevation.Tiles.OnlineEnabled=false;
   }
   typeof(MainForm).GetMethod("UpdateCenter",flags).Invoke(form,null);var label=(Label)typeof(MainForm).GetField("centerInfo",flags).GetValue(form);Check(label.Text.Contains("46.570000")&&label.Text.Contains("8.400000"),"top left coordinates equal center");c.Pan(25,20);typeof(MainForm).GetMethod("UpdateCenter",flags).Invoke(form,null);Check(!label.Text.Contains("46.570000"),"center updates after pan");c.GoTo(new GeoPoint(46.57,8.4),8);
   Application.DoEvents();using(var b=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(Path.Combine(output,"view03.png"));}
   Check(c.Hud.TargetTitle.Contains("Учебная точка")&&c.Hud.TargetInfo.Contains("Δh"),"selected point HUD data");Check(String.IsNullOrEmpty(c.Hud.Secondary),"WGS84 hidden when setting disabled");Check(c.Hud.Contains(new Point(30,30)),"card hit region");
   c.GoTo(new GeoPoint(55.75,37.62),14);c.Selected=new GeoPoint(55.751,37.621);typeof(MainForm).GetMethod("SelectSk42",flags).Invoke(form,null);
   var copyPoint=new GeoPoint(55.75,37.62);string skCopy=form.CopyText(copyPoint,1);Check(skCopy.Contains("X = 6181692")&&skCopy.Contains("Y = 7413462")&&skCopy.Contains("28407"),"copy SK42 north/east and zone");Check(form.CopyText(copyPoint,2)=="55.750000, 37.620000","copy WGS84 decimal explicitly selected");Check(form.CopyText(copyPoint,3).Contains("′")&&form.CopyText(copyPoint,4).Contains("″"),"copy minutes and seconds formats");
   Check(c.Hud.Primary.Contains("X = 6181692")&&c.Hud.Primary.Contains("Y = 7413462"),"SK42 center HUD X north Y east regression reference");Check(c.Hud.TargetCoordinates.StartsWith("X =")&&c.Hud.TargetCoordinates.Contains("Y ="),"selected point uses SK42 not WGS84");Check(String.IsNullOrEmpty(c.Hud.Secondary),"SK42 has no forced WGS84 line");
   c.Settings.SecondaryWgs=true;typeof(MainForm).GetMethod("UpdateCenter",flags).Invoke(form,null);Check(c.Hud.Secondary.StartsWith("WGS84:"),"WGS84 shown only when explicitly enabled");c.Settings.SecondaryWgs=false;
   c.GoTo(new GeoPoint(55.75,43.1),14);typeof(MainForm).GetMethod("SelectSk42",flags).Invoke(form,null);Check(c.Hud.Title.Contains("28408"),"SK42 quick selection zone 8");
   c.GoTo(new GeoPoint(55.75,37.62),14);typeof(MainForm).GetMethod("SelectSk42",flags).Invoke(form,null);using(var b=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(Path.Combine(output,"sk42-fixed.png"));}
   bool copyOpened=false;using(var closer=new System.Windows.Forms.Timer{Interval=100}){
   closer.Tick+=delegate{foreach(Form window in Application.OpenForms){if(window is CoordinateCopyDialog){copyOpened=true;using(var image=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(image,new Rectangle(0,0,window.Width,window.Height));image.Save(Path.Combine(output,"copy-coordinates.png"));}window.Close();break;}}};closer.Start();
   var previous=c.Selected;c.BeginMode(MapMode.Route);typeof(MapCanvas).GetMethod("OnMouseDown",flags).Invoke(c,new object[]{new MouseEventArgs(MouseButtons.Left,1,30,30,0)});typeof(MapCanvas).GetMethod("OnMouseUp",flags).Invoke(c,new object[]{new MouseEventArgs(MouseButtons.Left,1,30,30,0)});Check(!c.HasDraft&&copyOpened,"HUD opens coordinate copy without route vertices");c.CancelDraft();}
   using(var dialog=new SettingsDialog(c.Settings)){dialog.Show();Application.DoEvents();using(var b=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(Path.Combine(output,"settings03.png"));}dialog.Close();}form.Close();
  }
  return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}}
}

