using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AlpineQuestPort {
    // All geometry and presentation state belongs to the UI thread. Only TileStore/DEM workers do I/O and decoding.
    internal sealed class RayProfileEngine:IDisposable {
        readonly ElevationStore elevation;
        public TerrainProfile Profile {get;private set;}
        public RadioResult Radio {get;private set;}
        public PointRadioOptions Options {get;private set;}
        public string Status="Ожидание профиля";
        public bool Stale {get;private set;}
        public int Revision {get;private set;}
        GeoPoint lastA,lastB,observed;
        string key;
        bool fine,wasOnline,complete;
        int cursor;
        DateTime started,lastChange,lastBuild;
        public RayProfileEngine(string cache){elevation=new ElevationStore(cache);}
        internal bool Step(GeoPoint a,GeoPoint b,PointRadioOptions options,bool online,DateTime now){
            bool changed=false;string nextKey=options.Key();
            if(observed==null||GeoMath.DistanceMeters(observed,b)>.1){observed=new GeoPoint(b.Lat,b.Lon);lastChange=now;}
            bool needsFine=(now-lastChange).TotalMilliseconds>=600;
            bool pathChanged=lastA==null||GeoMath.DistanceMeters(lastA,a)>.1||GeoMath.DistanceMeters(lastB,b)>.1;
            if(key!=null&&!pathChanged&&Profile!=null&&wasOnline==online&&key!=nextKey){Options=options.Clone();key=nextKey;Radio=Options.Fresnel&&Profile.Heights.All(GeoMath.IsFinite)?RadioResult.Calculate(Profile,Options.Frequencies.Take(Options.Count).ToArray(),Options.HeightA,Options.HeightB,Options.K,Options.Curvature):null;changed=true;}
            bool configChanged=key!=nextKey||wasOnline!=online;
            bool outdated=pathChanged||configChanged;
            if(outdated!=Stale){Stale=outdated;changed=true;}
            if((outdated||needsFine&&!fine)&&(configChanged||(now-lastBuild).TotalMilliseconds>=220)){
                lastBuild=started=now;lastA=new GeoPoint(a.Lat,a.Lon);lastB=new GeoPoint(b.Lat,b.Lon);key=nextKey;wasOnline=online;fine=needsFine;Options=options.Clone();cursor=0;complete=false;Radio=null;Profile=null;elevation.Tiles.DiscardQueued();elevation.Tiles.OnlineEnabled=online;
                try{double distance=GeoMath.DistanceMeters(a,b);Profile=TerrainProfile.Create(a,b,false,0,Math.Max(fine?30:60,distance/(fine?1024:256)));Status="Загрузка высот…";}catch(ArgumentException ex){Status=ex.Message;}
                Stale=false;changed=true;Revision++;
            }
            if(Profile!=null&&!complete&&(now-started).TotalSeconds<180){
                bool got=false;var watch=Stopwatch.StartNew();int n=Profile.Points.Length;
                // Endpoints first, then interleaved progress through the whole path. Missing samples never become zero.
                for(int j=0;j<Math.Min(n,256)&&watch.ElapsedMilliseconds<6;j++){
                    int sequence=cursor++%n,index=sequence==0?0:sequence==1?n-1:sequence-1;
                    if(GeoMath.IsFinite(Profile.Heights[index]))continue;
                    var height=elevation.At(Profile.Points[index],14);if(height.HasValue){Profile.Heights[index]=height.Value;got=true;}
                }
                if(got||changed){Radio=Options.Fresnel&&Profile.Heights.All(GeoMath.IsFinite)?RadioResult.Calculate(Profile,Options.Frequencies.Take(Options.Count).ToArray(),Options.HeightA,Options.HeightB,Options.K,Options.Curvature):null;changed=true;}
                int ready=Profile.Heights.Count(GeoMath.IsFinite);complete=ready==n;string status=(ready==n?"Высоты готовы":online?"Ожидание высот для расчёта · "+ready+"/"+n:"Офлайн DEM Z14 "+ready+"/"+n)+" · шаг ≈ "+(Profile.Length/(n-1)).ToString("F0")+" м";
                if(ready<n&&!String.IsNullOrEmpty(elevation.Tiles.LastError))status+=" · "+elevation.Tiles.LastError;
                if(status!=Status){Status=status;changed=true;}
            }else if(Profile!=null&&!complete&&Profile.Heights.Any(v=>!GeoMath.IsFinite(v))){string status="Высоты неполные — сдвиньте карту для повторной загрузки";if(Status!=status){Status=status;changed=true;}}
            return changed;
        }
        public void Suspend(){elevation.Tiles.DiscardQueued();elevation.Tiles.OnlineEnabled=false;key=null;}
        public void Dispose(){elevation.Dispose();}
    }

    internal sealed class EndpointHandles {
        public RectangleF A,B; public ProfileAxes Axes;
        public int Hit(Point p){if(A.Contains(p))return 0;if(B.Contains(p))return 1;return -1;}
    }
    internal static class RayProfilePainter {
        internal static int NearestTemporarySample(TerrainProfile profile,GeoPoint mark){int best=-1;double distance=double.MaxValue;for(int i=0;i<profile.Points.Length;i++){double d=GeoMath.DistanceMeters(profile.Points[i],mark);if(d<distance){distance=d;best=i;}}return distance<=Math.Max(10,profile.Length/(profile.Points.Length-1)*.75)?best:-1;}
        static readonly Color[] colors={Color.FromArgb(249,245,92),Color.Cyan,Color.FromArgb(247,132,240)};
        static void Text(Graphics g,string text,Font font,Brush brush,RectangleF box){using(var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(text,font,brush,box,format);}
        internal static Bitmap Render(RayProfileEngine engine,int width,int height,string name,bool overlay,EndpointHandles handles=null,int selected=-1,System.Collections.Generic.IEnumerable<GeoPoint> temporaryPoints=null){
            if(handles!=null){handles.A=RectangleF.Empty;handles.B=RectangleF.Empty;handles.Axes=null;}
            var image=new Bitmap(Math.Max(100,width),Math.Max(100,height));using(var g=Graphics.FromImage(image))using(var font=new Font("Segoe UI",9))using(var bold=new Font("Segoe UI",10,FontStyle.Bold)){
                g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);using(var fill=new SolidBrush(Color.FromArgb(222,24,28,30)))g.FillRectangle(fill,0,0,width,height);
                Text(g,name+" → центр карты",bold,Brushes.White,new RectangleF(12,8,width-(overlay?235:24),25));
                if(overlay){Text(g,"⛶",bold,Brushes.White,new RectangleF(width-174,7,32,25));Text(g,"Настройки",font,Brushes.LightGray,new RectangleF(width-125,9,88,22));g.DrawString("×",bold,Brushes.White,width-27,6);}
                Text(g,(engine.Stale?"Обновление: график предыдущего положения · ":"")+engine.Status+(selected>=0?" · "+(selected==0?"A":"B")+": колесо ±1 м":""),font,engine.Stale?Brushes.Gold:Brushes.LightGray,new RectangleF(12,32,width-24,21));
                var p=engine.Profile;var radio=engine.Radio;var options=engine.Options;
                if(p==null||options==null)return image;
                double[] ground=radio==null?p.Heights:radio.Ground;
                var values=ground.Where(GeoMath.IsFinite).ToList();
                if(radio!=null&&radio.Endpoints)for(int i=0;i<p.Points.Length;i++){double radius=radio.Radii.Max(v=>v[i]);values.Add(radio.Sight[i]+radius);values.Add(radio.Sight[i]-radius);}
                if(values.Count==0){Text(g,"Нет высот для этой трассы. Включите онлайн-карту или дождитесь DEM.",font,Brushes.Gold,new RectangleF(12,70,width-24,25));return image;}
                int lines=options.Fresnel?options.Count:1;
                var axes=ProfileAxes.Create(values.Min(),values.Max(),p.Heights,width,height,lines,radio!=null&&options.Curvature);var plot=axes.Plot;axes.Draw(g,font);if(handles!=null)handles.Axes=axes;
                Func<int,double,PointF> xy=(i,h)=>new PointF(plot.Left+i*plot.Width/(p.Points.Length-1),axes.Y(h));
                Text(g,(axes.Curvature?"Высота с поправкой кривизны":"Высота DEM")+" · деления 10 м",font,Brushes.LightGray,new RectangleF(plot.Left,55,plot.Width,20));
                using(var terrain=new Pen(Color.WhiteSmoke,1.7f))for(int i=1;i<ground.Length;i++)if(GeoMath.IsFinite(ground[i-1])&&GeoMath.IsFinite(ground[i]))g.DrawLine(terrain,xy(i-1,ground[i-1]),xy(i,ground[i]));
                if(radio!=null&&radio.Endpoints){
                    using(var good=new Pen(Color.LimeGreen,1.5f){DashStyle=DashStyle.Dash})using(var bad=new Pen(Color.Red,1.5f){DashStyle=DashStyle.Dash})using(var unknown=new Pen(Color.Gray,1.5f){DashStyle=DashStyle.Dash}){
                        for(int i=1;i<ground.Length;i++){bool known=GeoMath.IsFinite(ground[i-1])&&GeoMath.IsFinite(ground[i]);bool blocked=known&&(radio.Sight[i]-ground[i]<0||radio.Sight[i-1]-ground[i-1]<0);g.DrawLine(!known?unknown:blocked?bad:good,xy(i-1,radio.Sight[i-1]),xy(i,radio.Sight[i]));}
                    }
                    for(int j=0;j<radio.Frequencies.Length;j++)using(var pen=new Pen(colors[j],1.5f))using(var dash=new Pen(colors[j],1){DashStyle=DashStyle.Dot}){
                        foreach(int sign in new[]{-1,1}){var boundary=new PointF[ground.Length];var clearance=new PointF[ground.Length];for(int i=0;i<ground.Length;i++){boundary[i]=xy(i,radio.Sight[i]+sign*radio.Radii[j][i]);clearance[i]=xy(i,radio.Sight[i]+sign*.6*radio.Radii[j][i]);}g.DrawLines(pen,boundary);g.DrawLines(dash,clearance);}
                    }
                    var first=xy(0,radio.Sight[0]);var last=xy(ground.Length-1,radio.Sight.Last());g.FillEllipse(Brushes.Lime,first.X-5,first.Y-5,10,10);g.FillEllipse(Brushes.Yellow,last.X-5,last.Y-5,10,10);
                    g.DrawString("+"+options.HeightA.ToString("0.#")+" м",bold,Brushes.Lime,first.X+7,Math.Max(plot.Top-15,first.Y-22));g.DrawString("+"+options.HeightB.ToString("0.#")+" м",bold,Brushes.Lime,last.X-65,Math.Max(plot.Top-15,last.Y-22));
                    if(handles!=null){handles.A=RectangleF.Union(new RectangleF(first.X-12,first.Y-12,24,24),new RectangleF(first.X+7,Math.Max(plot.Top-15,first.Y-22),90,24));handles.B=RectangleF.Union(new RectangleF(last.X-12,last.Y-12,24,24),new RectangleF(last.X-65,Math.Max(plot.Top-15,last.Y-22),90,24));}
                    if(selected>=0){var mark=selected==0?first:last;using(var outline=new Pen(Color.White,2))g.DrawEllipse(outline,mark.X-9,mark.Y-9,18,18);}
                    int obstacle=-1;for(int i=1;i<ground.Length-1;i++)if(GeoMath.IsFinite(ground[i])&&ground[i]>radio.Sight[i]){obstacle=i;break;}if(obstacle>=0){var point=xy(obstacle,ground[obstacle]);g.FillEllipse(Brushes.Red,point.X-4,point.Y-4,8,8);}
                }
                if(temporaryPoints!=null)foreach(var mark in temporaryPoints){int index=NearestTemporarySample(p,mark);if(index<0||!GeoMath.IsFinite(ground[index]))continue;var pos=xy(index,ground[index]);using(var pen=new Pen(Color.Orange,1){DashStyle=DashStyle.Dot})g.DrawLine(pen,pos.X,plot.Top,pos.X,plot.Bottom);g.FillEllipse(Brushes.Orange,pos.X-5,pos.Y-5,10,10);g.DrawEllipse(Pens.White,pos.X-5,pos.Y-5,10,10);g.DrawString((p.X[index]/1000).ToString("0.000")+" км",font,Brushes.Orange,Math.Max(plot.Left,Math.Min(plot.Right-75,pos.X+7)),Math.Max(plot.Top,pos.Y-22));}
                Text(g,"Отсчёт Δh: min DEM = "+(GeoMath.IsFinite(axes.MinimumDem)?axes.MinimumDem.ToString("0.0")+" м":"нет данных")+" · наведите курсор на профиль",font,Brushes.LightGray,new RectangleF(12,plot.Bottom+3,width-24,20));
                string delta=GeoMath.IsFinite(p.Heights[0])&&GeoMath.IsFinite(p.Heights.Last())?" · Δh "+(p.Heights.Last()-p.Heights[0]).ToString("+0.0;-0.0;0")+" м":" · Δh —";
                Text(g,(p.Length/1000).ToString("F3")+" км"+delta+" · "+(radio!=null&&options.Curvature?"кривизна вкл., k="+options.K.ToString("0.###"):"высота DEM"),font,Brushes.LightGray,new RectangleF(12,plot.Bottom+23,width-24,20));
                for(int j=0;j<lines;j++){
                    string text="Профиль высот · зоны Френеля выключены";if(options.Fresnel){text=options.Frequencies[j].ToString("0.###")+" МГц · ";if(radio==null||!radio.Endpoints)text+="нет высоты концов";else{text+="запас 60%: "+radio.Margin60[j].ToString("+0.0;-0.0;0")+" м";text+=engine.Stale?" · устарело":!radio.Complete?" · НЕПОЛНЫЕ ДАННЫЕ":!radio.ApproximationValid?" · вне приближения":radio.Margin60[j]<0?" · перекрытие":" · свободно по выборке";}}
                    using(var brush=new SolidBrush(colors[j]))Text(g,text,font,brush,new RectangleF(12,plot.Bottom+44+j*19,width-24,20));
                }
            }return image;
        }
    }

    internal sealed class PointRadioEditor:Panel {
        readonly CheckBox enabled=new CheckBox{Text="Показывать профиль при ведении луча",AutoSize=true},fresnel=new CheckBox{Text="Зоны Френеля",AutoSize=true},curvature=new CheckBox{Text="Кривизна Земли",AutoSize=true};
        readonly NumericUpDown count=Number(1,3,1,0),ha=Number(0,1000,20,1),hb=Number(0,1000,12,1),k=Number(.1m,10,1.333333m,6);
        readonly NumericUpDown[] frequencies={Number(.001m,300000,900,3),Number(.001m,300000,2400,3),Number(.001m,300000,5800,3)};
        readonly ComboBox[] units=new ComboBox[3];
        public event Action Changed;
        static NumericUpDown Number(decimal min,decimal max,decimal value,int places){return new NumericUpDown{Minimum=min,Maximum=max,Value=value,DecimalPlaces=places,Width=85};}
        void Label(string text,int x,int y){Controls.Add(new Label{Text=text,AutoSize=true,Left=x,Top=y+4});}
        public PointRadioEditor(PointRadioOptions original){
            Size=new Size(620,164);enabled.Checked=original.Enabled;fresnel.Checked=original.Fresnel;curvature.Checked=original.Curvature;enabled.SetBounds(0,0,340,24);fresnel.SetBounds(0,31,145,24);curvature.SetBounds(160,31,160,24);Controls.AddRange(new Control[]{enabled,fresnel,curvature});
            Label("Частот:",380,31);count.SetBounds(445,31,50,26);count.Value=original.Count;Controls.Add(count);
            Label("Антенна A, м",0,65);ha.SetBounds(110,65,70,26);ha.Value=(decimal)original.HeightA;Controls.Add(ha);Label("B, м",194,65);hb.SetBounds(240,65,70,26);hb.Value=(decimal)original.HeightB;Controls.Add(hb);Label("k:",345,65);k.SetBounds(372,65,115,26);k.Value=(decimal)original.K;Controls.Add(k);
            for(int i=0;i<3;i++){int x=i*198;Label("f"+(i+1),x,103);frequencies[i].SetBounds(x+25,103,95,26);frequencies[i].Value=(decimal)original.Frequencies[i];Controls.Add(frequencies[i]);units[i]=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Left=x+124,Top=103,Width=65};units[i].Items.AddRange(new object[]{"МГц","ГГц"});units[i].SelectedIndex=0;Controls.Add(units[i]);}
            Label("A — выбранная точка; B — центр экрана. Высоты антенн над землёй.",0,136);
            foreach(var n in new[]{ha,hb,k,count}.Concat(frequencies))n.ValueChanged+=delegate{UpdateEnabled();if(Changed!=null)Changed();};foreach(var unit in units)unit.SelectedIndexChanged+=delegate{if(Changed!=null)Changed();};foreach(var c in new[]{enabled,fresnel,curvature})c.CheckedChanged+=delegate{UpdateEnabled();if(Changed!=null)Changed();};UpdateEnabled();
        }
        void UpdateEnabled(){count.Enabled=fresnel.Checked;k.Enabled=curvature.Checked&&fresnel.Checked;curvature.Enabled=fresnel.Checked;ha.Enabled=hb.Enabled=fresnel.Checked;for(int i=0;i<3;i++)frequencies[i].Enabled=units[i].Enabled=fresnel.Checked&&i<(int)count.Value;}
        public PointRadioOptions Value(){var v=new PointRadioOptions{Enabled=enabled.Checked,Fresnel=fresnel.Checked,Curvature=curvature.Checked,Count=(int)count.Value,HeightA=(double)ha.Value,HeightB=(double)hb.Value,K=(double)k.Value};for(int i=0;i<3;i++)v.Frequencies[i]=(double)frequencies[i].Value*(units[i].SelectedIndex==1?1000:1);v.Validate();return v;}
    }

    internal sealed class RayPreview:Control {
        readonly RayProfileEngine engine;
        readonly MapCanvas map;
        readonly GeoPoint anchor;
        readonly PointRadioEditor editor;
        readonly Timer timer=new Timer{Interval=100};
        Bitmap image;
        readonly EndpointHandles probeHandles=new EndpointHandles();Point? probe;
        public RayPreview(MapCanvas map,GeoPoint anchor,PointRadioEditor editor,string cache){this.map=map;this.anchor=anchor;this.editor=editor;engine=new RayProfileEngine(cache);DoubleBuffered=true;BackColor=Color.FromArgb(24,28,30);timer.Tick+=delegate{if(!Visible){engine.Suspend();return;}try{if(engine.Step(anchor,map.Center,editor.Value(),map.Tiles.OnlineEnabled,DateTime.UtcNow)||image==null){if(image!=null)image.Dispose();image=RayProfilePainter.Render(engine,Width,Height,anchor.Name,false,probeHandles,-1,map.RayTemporaryPoints);Invalidate();}}catch(ArgumentException ex){ShowError(ex.Message);}catch(System.IO.InvalidDataException ex){ShowError(ex.Message);}};timer.Start();}
        protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);var point=ProfilePointActions.At(engine.Profile,probeHandles.Axes,e.Location);if(point==null)return;if(e.Button==MouseButtons.Left)AddTemporary(point);else if(e.Button==MouseButtons.Right){var hit=ProfilePointActions.HitTemporary(engine.Profile,probeHandles.Axes,e.Location,map.RayTemporaryPoints);ProfilePointActions.Menu(this,e.Location,point,AddTemporary,p=>{AddTemporary(p);map.GoTo(p,map.Zoom);},hit==null?(Action)null:()=>{map.RemoveRayTemporaryPoint(hit);if(image!=null){image.Dispose();image=null;}Invalidate();});}}
        void AddTemporary(GeoPoint point){map.AddRayTemporaryPoint(anchor,point);if(image!=null){image.Dispose();image=null;}Invalidate();}
        void ShowError(string message){if(image!=null)image.Dispose();image=new Bitmap(Math.Max(1,Width),Math.Max(1,Height));using(var g=Graphics.FromImage(image)){g.Clear(BackColor);g.DrawString(message,Font,Brushes.Gold,new RectangleF(12,12,Width-24,Height-24));}Invalidate();}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);probe=e.Location;Invalidate();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);probe=null;Invalidate();}
        protected override void OnResize(EventArgs e){base.OnResize(e);if(image!=null){image.Dispose();image=null;}Invalidate();}
        protected override void OnPaint(PaintEventArgs e){if(image!=null)e.Graphics.DrawImageUnscaled(image,0,0);if(probeHandles.Axes!=null)using(var font=new Font("Segoe UI",9))probeHandles.Axes.DrawProbe(e.Graphics,engine,probe,font);}
        protected override void Dispose(bool disposing){if(disposing){timer.Dispose();engine.Dispose();if(image!=null)image.Dispose();}base.Dispose(disposing);}
    }

    internal sealed class LiveRayProfile:IDisposable {
        readonly MapCanvas map;
        readonly RayProfileEngine engine;
        readonly Timer timer=new Timer{Interval=100};
        public GeoPoint Anchor {get;private set;}
        public Rectangle Bounds {get;private set;}
        Bitmap image;
        bool repaint;string shownName;Point? probe;
        internal bool Collapsed {get;private set;}
        internal bool Compact {get{return Collapsed||engine.Profile==null;}}
        internal void ToggleCollapsed(){Collapsed=!Collapsed;SelectedEndpoint=-1;RefreshGraph();}
        internal readonly EndpointHandles handles=new EndpointHandles();
        internal int SelectedEndpoint=-1;int wheelRemainder;
        internal void SelectEndpoint(int endpoint){SelectedEndpoint=endpoint;wheelRemainder=0;RefreshGraph();}
        internal void RefreshGraph(){repaint=true;if(fullscreen!=null&&!fullscreen.IsDisposed)fullscreen.RefreshProfile();map.RequestFrame();}
        internal void AdjustHeight(int delta){if(!Visible||SelectedEndpoint<0)return;wheelRemainder+=delta;int steps=wheelRemainder/120;wheelRemainder%=120;if(steps==0)return;var options=Anchor.Radio;double value=SelectedEndpoint==0?options.HeightA:options.HeightB;value=Math.Max(0,Math.Min(1000,value+steps));if(SelectedEndpoint==0)options.HeightA=value;else options.HeightB=value;map.NotifyRadioSettings();Tick();RefreshGraph();}
        public bool Wheel(Point location,int delta){if(!Hit(location))return false;AdjustHeight(delta);return true;}
        internal RayFullscreen fullscreen;
        internal void Expand(){if(!Visible)return;if(fullscreen==null||fullscreen.IsDisposed)fullscreen=new RayFullscreen(engine,()=>Anchor==null?"Профиль":Anchor.Name,this);fullscreen.Show(map.FindForm());fullscreen.Activate();}
        public bool Visible {get{return Anchor!=null&&Anchor.Radio.Enabled&&map.Settings.RayEnabled;}}
        public LiveRayProfile(MapCanvas map,string cache){this.map=map;engine=new RayProfileEngine(cache);timer.Tick+=delegate{Tick();};}
        public void Start(GeoPoint anchor){map.SetTemporaryRayAnchor(anchor);Collapsed=false;if(Anchor!=anchor){SelectedEndpoint=-1;wheelRemainder=0;}Anchor=anchor;repaint=true;timer.Start();Tick();}
        public void Stop(){map.SetTemporaryRayAnchor(null);if(fullscreen!=null&&!fullscreen.IsDisposed)fullscreen.Close();timer.Stop();engine.Suspend();Anchor=null;Bounds=Rectangle.Empty;if(image!=null){image.Dispose();image=null;}map.RequestFrame();}
        internal void Tick(){if(Anchor==null)return;if(!map.Settings.RayEnabled||!map.Document.Waypoints.Contains(Anchor)||!Anchor.Radio.Enabled){Stop();return;}
            if(shownName!=Anchor.Name){shownName=Anchor.Name;repaint=true;map.RequestFrame();}
            map.Settings.RayLat=Anchor.Lat;map.Settings.RayLon=Anchor.Lon;
            if(engine.Step(Anchor,map.Center,Anchor.Radio,map.Tiles.OnlineEnabled,DateTime.UtcNow)){repaint=true;if(fullscreen!=null&&!fullscreen.IsDisposed)fullscreen.RefreshProfile();map.RequestFrame();}
        }
        public void Draw(Graphics g){if(!Visible){Bounds=Rectangle.Empty;return;}
            int width=Math.Min(680,Math.Max(200,map.Width-32)),height=Compact?66:Math.Min(260,Math.Max(190,(int)(map.Height*.34)));height=Math.Min(height,Math.Max(66,map.Height-120));Bounds=new Rectangle(map.Width-width-16,map.Height-height-42,width,height);
            if(Compact){handles.A=handles.B=RectangleF.Empty;using(var bg=new SolidBrush(Color.FromArgb(235,24,28,30)))g.FillRectangle(bg,Bounds);using(var font=new Font("Segoe UI",9))using(var bold=new Font("Segoe UI",10,FontStyle.Bold))using(var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap}){
                g.DrawString(Anchor.Name+" → центр карты",bold,Brushes.White,new RectangleF(Bounds.X+12,Bounds.Y+7,width-245,23),format);
                double distance=GeoMath.DistanceMeters(Anchor,map.Center);string text=engine.Profile==null?(distance<10?"Отодвиньте центр карты минимум на 10 м":distance>300000?"Приблизьте центр карты: предел трассы 300 км":engine.Status):"Профиль свёрнут · "+engine.Status;
                g.DrawString(text,font,Brushes.LightGray,new RectangleF(Bounds.X+12,Bounds.Y+35,width-24,23),format);
                g.DrawString("⛶",bold,Brushes.White,Bounds.Right-174,Bounds.Y+7);g.DrawString("Настройки",font,Brushes.LightGray,Bounds.Right-125,Bounds.Y+9);g.DrawString("×",bold,Brushes.White,Bounds.Right-27,Bounds.Y+6);
            }}else{if(image==null||image.Width!=width||image.Height!=height||repaint){if(image!=null)image.Dispose();image=RayProfilePainter.Render(engine,width,height,Anchor.Name,true,handles,SelectedEndpoint,map.RayTemporaryPoints);repaint=false;}g.DrawImageUnscaled(image,Bounds.Location);if(handles.Axes!=null&&probe.HasValue){var state=g.Save();try{g.TranslateTransform(Bounds.X,Bounds.Y);using(var font=new Font("Segoe UI",9))handles.Axes.DrawProbe(g,engine,probe,font);}finally{g.Restore(state);}}}
            using(var font=new Font("Segoe UI",10,FontStyle.Bold))g.DrawString(Collapsed?"▴":"▾",font,Brushes.White,Bounds.Right-212,Bounds.Y+6);
        }
        public void MoveProbe(Point? location){Point? next=location.HasValue&&Hit(location.Value)&&!Compact?(Point?)new Point(location.Value.X-Bounds.X,location.Value.Y-Bounds.Y):null;if(probe!=next){probe=next;map.RequestFrame();}}
        public bool Hit(Point location){return Visible&&Bounds.Contains(location);}
        internal System.Collections.Generic.IEnumerable<GeoPoint> TemporaryPoints {get{return map.RayTemporaryPoints;}}
        void AddTemporary(GeoPoint point){map.AddRayTemporaryPoint(Anchor,point);RefreshGraph();}
        internal void AddGraphPoint(ProfileAxes axes,Point location){var point=ProfilePointActions.At(engine.Profile,axes,location);if(point!=null)AddTemporary(point);}
        internal void GraphMenu(Control owner,Point screenPoint,ProfileAxes axes,Point location){var hit=ProfilePointActions.HitTemporary(engine.Profile,axes,location,map.RayTemporaryPoints);ProfilePointActions.Menu(owner,screenPoint,ProfilePointActions.At(engine.Profile,axes,location),AddTemporary,p=>{AddTemporary(p);map.GoTo(p,map.Zoom);if(fullscreen!=null&&!fullscreen.IsDisposed)fullscreen.Close();map.FindForm().Activate();},hit==null?(Action)null:()=>{map.RemoveRayTemporaryPoint(hit);RefreshGraph();});}
        internal bool ContextGraph(Point location){if(!Hit(location)||Compact)return false;GraphMenu(map,location,handles.Axes,new Point(location.X-Bounds.X,location.Y-Bounds.Y));return true;}
        public bool Press(Point location){if(!Hit(location))return false;if(location.X>=Bounds.Right-36&&location.Y<Bounds.Top+32){Stop();}else if(location.X>=Bounds.Right-138&&location.Y<Bounds.Top+32)map.EditRadioAnchor(Anchor);else if(location.X>=Bounds.Right-180&&location.Y<Bounds.Top+32)Expand();else if(location.X>=Bounds.Right-222&&location.Y<Bounds.Top+32)ToggleCollapsed();else if(!Compact){var local=new Point(location.X-Bounds.X,location.Y-Bounds.Y);int endpoint=handles.Hit(local);SelectEndpoint(endpoint);if(endpoint<0)AddGraphPoint(handles.Axes,local);}return true;}
        public void Dispose(){if(fullscreen!=null&&!fullscreen.IsDisposed)fullscreen.Close();timer.Dispose();engine.Dispose();if(image!=null)image.Dispose();}
    }
    internal sealed class RayFullscreen:Form {
        readonly RayProfileEngine engine;readonly Func<string> title;readonly LiveRayProfile controller;readonly EndpointHandles handles=new EndpointHandles();Bitmap image;Point? probe;
        public RayFullscreen(RayProfileEngine engine,Func<string> title,LiveRayProfile controller){this.controller=controller;this.engine=engine;this.title=title;DoubleBuffered=true;KeyPreview=true;FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;BackColor=Color.FromArgb(24,28,30);var close=new Button{Text="Вернуться · Esc",Left=12,Top=6,Width=160,Height=30};close.Click+=delegate{Close();};Controls.Add(close);Shown+=delegate{Bounds=Screen.FromControl(Owner??this).Bounds;};KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Escape||e.KeyCode==Keys.F11){e.Handled=true;Close();}};}
        public void RefreshProfile(){if(image!=null){image.Dispose();image=null;}Invalidate();}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){ActiveControl=null;Focus();var local=new Point(e.X,e.Y-40);int endpoint=handles.Hit(local);controller.SelectEndpoint(endpoint);if(endpoint<0)controller.AddGraphPoint(handles.Axes,local);}}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Right)controller.GraphMenu(this,e.Location,handles.Axes,new Point(e.X,e.Y-40));}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);probe=new Point(e.X,e.Y-40);Invalidate();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);probe=null;Invalidate();}
        protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);controller.AdjustHeight(e.Delta);}
        protected override void OnResize(EventArgs e){base.OnResize(e);RefreshProfile();}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(image==null)image=RayProfilePainter.Render(engine,Math.Max(100,ClientSize.Width),Math.Max(100,ClientSize.Height-40),title(),false,handles,controller.SelectedEndpoint,controller.TemporaryPoints);e.Graphics.DrawImageUnscaled(image,0,40);if(handles.Axes!=null){var state=e.Graphics.Save();try{e.Graphics.TranslateTransform(0,40);using(var font=new Font("Segoe UI",9))handles.Axes.DrawProbe(e.Graphics,engine,probe,font);}finally{e.Graphics.Restore(state);}}}
        protected override void Dispose(bool disposing){if(disposing&&image!=null){image.Dispose();image=null;}base.Dispose(disposing);}
    }
    sealed partial class MapCanvas {
        public LiveRayProfile LiveRay;
        public void StartLiveRay(GeoPoint anchor,string cache){if(LiveRay==null)LiveRay=new LiveRayProfile(this,cache);LiveRay.Start(anchor);}
        public void NotifyRadioSettings(){Changed();}
        public void EditRadioAnchor(GeoPoint point){if(MarkerAction!=null)MarkerAction(point,"edit");}
    }
}






