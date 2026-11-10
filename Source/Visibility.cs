using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace AlpineQuestPort {
    internal sealed class VisibilityOptions {
        public double Antenna=11,Drone=50,Frequency=1080,Radius=1000,Clearance=.6,K=4.0/3;
        public int Quality=1,Opacity=45;
        public bool AboveStart,Curvature=true,Fresnel=true;
        public VisibilityOptions Copy(){return (VisibilityOptions)MemberwiseClone();}
        public void Validate(){if(new[]{Antenna,Drone,Frequency,Radius,Clearance,K}.Any(v=>!GeoMath.IsFinite(v))||Antenna<0||Antenna>1000||Drone<0||Drone>10000||Frequency<1||Frequency>300000||Radius<100||Radius>20000||Clearance<0||Clearance>1||K<.1||K>10||Quality<0||Quality>2||Opacity<5||Opacity>90)throw new ArgumentException("Проверьте параметры радиовидимости.");}
        public void Save(string path){Validate();var x=new XElement("visibility",new XAttribute("antenna",Antenna),new XAttribute("drone",Drone),new XAttribute("frequency",Frequency),new XAttribute("radius",Radius),new XAttribute("clearance",Clearance),new XAttribute("k",K),new XAttribute("quality",Quality),new XAttribute("opacity",Opacity),new XAttribute("aboveStart",AboveStart),new XAttribute("curvature",Curvature),new XAttribute("fresnel",Fresnel));Directory.CreateDirectory(Path.GetDirectoryName(path));AtomicSettings.SaveXml(path,x);}
        public static VisibilityOptions Load(string path){return AtomicSettings.Recover(path,LoadCore);}
        static VisibilityOptions LoadCore(string path){if(!File.Exists(path))return new VisibilityOptions();var x=AtomicSettings.LoadXml(path);var o=new VisibilityOptions{Antenna=(double)x.Attribute("antenna"),Drone=(double)x.Attribute("drone"),Frequency=(double)x.Attribute("frequency"),Radius=(double)x.Attribute("radius"),Clearance=(double)x.Attribute("clearance"),K=(double)x.Attribute("k"),Quality=(int)x.Attribute("quality"),Opacity=(int)x.Attribute("opacity"),AboveStart=(bool)x.Attribute("aboveStart"),Curvature=(bool)x.Attribute("curvature"),Fresnel=(bool)x.Attribute("fresnel")};o.Validate();return o;}
    }
    internal enum VisibilityClass:byte {Unknown,Clear,FresnelBlocked,Blocked}
    internal sealed class VisibilityGrid {
        public GeoPoint Origin;public VisibilityOptions Options;public int Rays,Steps;public double Step;
        public GeoPoint[] Points;public double[] Heights;public VisibilityClass[] Classes;
        public static VisibilityGrid Create(GeoPoint origin,VisibilityOptions options,CancellationToken token){
            options.Validate();if(origin==null||!GeoMath.IsValidCoordinate(origin.Lat,origin.Lon))throw new ArgumentException("Выберите точку антенны.");
            var g=new VisibilityGrid{Origin=new GeoPoint(origin.Lat,origin.Lon),Options=options.Copy(),Rays=new[]{180,360,720}[options.Quality]};
            g.Steps=Math.Max(4,Math.Min(new[]{128,256,512}[options.Quality],(int)Math.Ceiling(options.Radius/new[]{40,20,10}[options.Quality])));g.Step=options.Radius/g.Steps;
            g.Points=new GeoPoint[1+g.Rays*g.Steps];g.Heights=Enumerable.Repeat(double.NaN,g.Points.Length).ToArray();g.Points[0]=g.Origin;
            for(int ray=0;ray<g.Rays;ray++){token.ThrowIfCancellationRequested();for(int j=1;j<=g.Steps;j++){var p=RangeRings.Destination(origin,j*g.Step,ray*360.0/g.Rays);if(Math.Abs(p.Lat)>84.9||Math.Abs(p.Lon-origin.Lon)>180)throw new ArgumentException("Область пересекает край карты. Уменьшите радиус или сместите центр.");g.Points[1+ray*g.Steps+j-1]=p;}}
            return g;
        }
        // A straight path over an effective spherical Earth. Fresnel distance is along the 3D ray;
        // vertical clearance is projected onto the ray normal before comparing with its radius.
        internal static VisibilityClass Classify(double[] heights,int end,double step,VisibilityOptions o){
            for(int i=0;i<=end;i++)if(!GeoMath.IsFinite(heights[i]))return VisibilityClass.Unknown;
            return ClassifyKnown(heights,end,step,o);
        }
        static VisibilityClass ClassifyKnown(double[] heights,int end,double step,VisibilityOptions o){
            double distance=end*step,tx=heights[0]+o.Antenna,rx=(o.AboveStart?heights[0]:heights[end])+o.Drone;
            if(rx<heights[end]||tx<heights[0])return VisibilityClass.Blocked;
            double delta=rx-tx,length=Math.Sqrt(distance*distance+delta*delta),cosine=distance/length,lambda=299792458.0/(o.Frequency*1e6);
            if(o.Fresnel&&length<10*lambda)return VisibilityClass.Unknown;
            bool fresnel=false;
            for(int i=0;i<=end;i++){
                double d=i*step,t=i/(double)end,ground=heights[i]+(o.Curvature?RadioResult.Bulge(d,Math.Max(0,distance-d),o.K):0),clear=(tx+delta*t-ground)*cosine;
                if(clear < -1e-8)return VisibilityClass.Blocked;
                if(o.Fresnel&&clear+1e-8<o.Clearance*Math.Sqrt(lambda*length*t*(1-t)))fresnel=true;
            }return fresnel?VisibilityClass.FresnelBlocked:VisibilityClass.Clear;
        }
        public void Calculate(CancellationToken token){var classes=new VisibilityClass[Points.Length];Parallel.For(0,Rays,new ParallelOptions{CancellationToken=token,MaxDegreeOfParallelism=Math.Max(1,Math.Min(4,Environment.ProcessorCount-1))},ray=>{
            var line=new double[Steps+1];line[0]=Heights[0];Array.Copy(Heights,1+ray*Steps,line,1,Steps);
            int firstMissing=Array.FindIndex(line,h=>!GeoMath.IsFinite(h));
            for(int j=1;j<=Steps;j++){if((j&15)==0)token.ThrowIfCancellationRequested();classes[1+ray*Steps+j-1]=firstMissing>=0&&j>=firstMissing?VisibilityClass.Unknown:ClassifyKnown(line,j,Step,Options);}
        });Classes=classes;}
    }
    internal sealed class VisibilityRaster:IDisposable {
        public byte[] Pixels;public Bitmap Image;public double Left,Top,Right,Bottom;public GeoPoint Origin;public VisibilityOptions Options;
        public static VisibilityRaster Create(VisibilityGrid grid,CancellationToken token){
            var result=new VisibilityRaster{Origin=grid.Origin,Options=grid.Options.Copy()};
            // Coordinate rectangle in fixed Web Mercator pixel space; it stays attached during pan/zoom.
            const int z=14,size=640;result.Left=grid.Points.Min(p=>GeoMath.LonToPixel(p.Lon,z));result.Right=grid.Points.Max(p=>GeoMath.LonToPixel(p.Lon,z));result.Top=grid.Points.Min(p=>GeoMath.LatToPixel(p.Lat,z));result.Bottom=grid.Points.Max(p=>GeoMath.LatToPixel(p.Lat,z));var bytes=new byte[size*size*4];
            for(int y=0;y<size;y++){token.ThrowIfCancellationRequested();for(int x=0;x<size;x++){
                var point=new GeoPoint(GeoMath.PixelToLat(result.Top+(y+.5)/size*(result.Bottom-result.Top),z),GeoMath.PixelToLon(result.Left+(x+.5)/size*(result.Right-result.Left),z));double distance=GeoMath.DistanceMeters(grid.Origin,point);if(distance>grid.Options.Radius)continue;
                int ray=(int)Math.Floor(((TerrainProfile.Bearing(grid.Origin,point)+360)%360)/360*grid.Rays+.5)%grid.Rays;
                int step=Math.Max(1,Math.Min(grid.Steps,(int)Math.Ceiling(distance/grid.Step)));var state=grid.Classes[1+ray*grid.Steps+step-1];Color c=state==VisibilityClass.Clear?Color.FromArgb(32,205,65):state==VisibilityClass.FresnelBlocked?Color.FromArgb(250,209,35):state==VisibilityClass.Blocked?Color.FromArgb(229,49,57):((x+y)%14<7?Color.FromArgb(125,137,155):Color.FromArgb(194,201,214));int at=(y*size+x)*4;bytes[at]=c.B;bytes[at+1]=c.G;bytes[at+2]=c.R;bytes[at+3]=255;
            }}result.Image=new Bitmap(size,size,PixelFormat.Format32bppArgb);var data=result.Image.LockBits(new Rectangle(0,0,size,size),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);try{Marshal.Copy(bytes,0,data.Scan0,bytes.Length);}finally{result.Image.UnlockBits(data);}result.Pixels=bytes;return result;
        }
        public void Dispose(){if(Image!=null){Image.Dispose();Image=null;}Pixels=null;}
    }
    sealed partial class MapCanvas {
        internal VisibilityRaster VisibilityOverlay;
        internal bool VisibilityShown=true;
        internal int VisibilityOpacity=45;
        internal void SetVisibility(VisibilityRaster value){ClearVisibilityScreen();var old=VisibilityOverlay;VisibilityOverlay=value;if(old!=null)old.Dispose();RequestFrame();}
        Bitmap visibilityScreen;string visibilityScreenKey,visibilityPendingKey;
        bool visibilityRendering;int visibilityRevision;CancellationTokenSource visibilityRenderCancellation;
        void ClearVisibilityScreen(){visibilityRevision++;if(visibilityRenderCancellation!=null)visibilityRenderCancellation.Cancel();if(visibilityScreen!=null){visibilityScreen.Dispose();visibilityScreen=null;}visibilityScreenKey=null;}
        async void PrepareVisibility(VisibilityRaster r,string key){
            if(visibilityRendering){if(key!=visibilityPendingKey&&visibilityRenderCancellation!=null)visibilityRenderCancellation.Cancel();return;}
            visibilityRendering=true;visibilityPendingKey=key;int revision=visibilityRevision;var cancellation=new CancellationTokenSource();visibilityRenderCancellation=cancellation;
            var input=new VisibilityScreenInput{Width=Width,Height=Height,Size=r.Image.Width,Source=r.Pixels,Left=r.Left,Top=r.Top,Right=r.Right,Bottom=r.Bottom,Zoom=Zoom,CenterX=centerX,CenterY=centerY,Tilt=Settings.Perspective?Settings.Tilt:0,Rotation=Settings.Rotation,Opacity=VisibilityOpacity};
            Bitmap result=null;
            try{result=await BackgroundJobs.Run("visibility",()=>input.Render(cancellation.Token));if(!IsDisposed&&!Disposing&&revision==visibilityRevision&&!cancellation.IsCancellationRequested){if(visibilityScreen!=null)visibilityScreen.Dispose();visibilityScreen=result;result=null;visibilityScreenKey=key;}}
            catch(OperationCanceledException){}catch(Exception ex){Diagnostics.Record("visibility.reprojection",ex);if(revision==visibilityRevision)visibilityScreenKey=key;}
            finally{if(result!=null)result.Dispose();visibilityRendering=false;visibilityRenderCancellation=null;cancellation.Dispose();if(!IsDisposed&&!Disposing)RequestFrame();}
        }
        void DrawVisibility(Graphics g){var r=VisibilityOverlay;if(r==null||!VisibilityShown||Width<1||Height<1)return;
            string key=Width+"/"+Height+"/"+Zoom+"/"+centerX.ToString("R")+"/"+centerY.ToString("R")+"/"+Settings.Perspective+"/"+Settings.Tilt+"/"+Settings.Rotation+"/"+VisibilityOpacity;
            if(key!=visibilityScreenKey)PrepareVisibility(r,key);
            if(visibilityScreen!=null&&key==visibilityScreenKey)g.DrawImageUnscaled(visibilityScreen,0,0);
            var center=ToScreen(r.Origin);if(VisiblePoint(center)){g.FillEllipse(Brushes.White,center.X-7,center.Y-7,14,14);g.DrawEllipse(Pens.Black,center.X-7,center.Y-7,14,14);DrawLabel(g,"Антенна",(int)center.X+11,(int)center.Y);}
            DrawLabel(g,"Радиовидимость · зелёный: свободно · жёлтый: Френель · красный: закрыто · серый: нет расчёта",12,Height-145);
        }
    }
    sealed partial class MainForm {
        VisibilityForm visibilityForm;
        void InitializeVisibility(ToolStrip tools){AddTool(tools,"Радиовидимость…","Круговая радиовидимость: антенна, дрон и зона Френеля",delegate{
            if(visibilityForm==null||visibilityForm.IsDisposed)visibilityForm=new VisibilityForm(map,dataDir);
            if(!visibilityForm.Visible)visibilityForm.Show(this);visibilityForm.Activate();
        });FormClosed+=delegate{if(visibilityForm!=null)visibilityForm.Dispose();map.SetVisibility(null);};}
    }
    internal sealed class VisibilityForm:Form {
        readonly MapCanvas map;readonly string folder;readonly ElevationStore elevation;
        readonly NumericUpDown antenna=Number(0,1000,11,1),drone=Number(0,10000,50,1),frequency=Number(1,300000,1080,3),radius=Number(100,20000,1000,0),clearance=Number(0,100,60,0),k=Number(.1m,10,1.333333m,6);
        readonly ComboBox altitude=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList},quality=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};
        readonly CheckBox fresnel=new CheckBox{Text="Учитывать первую зону Френеля",AutoSize=true,Checked=true},curvature=new CheckBox{Text="Кривизна Земли",AutoSize=true,Checked=true},online=new CheckBox{Text="Дозагружать высоты онлайн",AutoSize=true},automatic=new CheckBox{Text="Авторасчёт при изменении параметров",AutoSize=true};
        readonly TrackBar opacity=new TrackBar{Minimum=5,Maximum=90,Value=45,TickFrequency=10};
        readonly Label position=new Label(),status=new Label(),resolution=new Label();readonly ProgressBar progress=new ProgressBar();readonly Button calculate=new Button{Text="Рассчитать"},stop=new Button{Text="Остановить",Enabled=false},show=new Button{Text="Скрыть слой"};
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=80},debounce=new System.Windows.Forms.Timer{Interval=700};
        Task calculationTask;GeoPoint origin;VisibilityGrid grid;CancellationTokenSource cancellation;int generation,cursor,ready;DateTime started;bool loading,closed,initializing=true;Action<GeoPoint> pickCallback;
        static NumericUpDown Number(decimal min,decimal max,decimal value,int decimals){return new NumericUpDown{Minimum=min,Maximum=max,Value=value,DecimalPlaces=decimals,ThousandsSeparator=true};}
        void Row(string label,Control control,int y){Controls.Add(new Label{Text=label,Location=new Point(18,y+4),Size=new Size(240,25)});control.SetBounds(270,y,235,29);Controls.Add(control);}
        Button Button(string text,int x,int y,int width,Action action){var b=new Button{Text=text,Location=new Point(x,y),Size=new Size(width,30)};b.Click+=delegate{action();};Controls.Add(b);return b;}
        public VisibilityForm(MapCanvas canvas,string data){map=canvas;folder=data;elevation=new ElevationStore(Path.Combine(data,"elevation-cache"));Text="Круговая радиовидимость";Font=new Font("Segoe UI",10);ClientSize=new Size(535,740);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterParent;
            position.SetBounds(18,12,500,26);Controls.Add(position);Button("Центр карты",18,43,148,()=>SetOrigin(map.Center));Button("Выбранная метка",177,43,165,()=>{var point=map.Selected as GeoPoint;if(point==null){status.Text="Выберите метку на карте и нажмите ещё раз.";return;}SetOrigin(point);});Button("Указать…",354,43,150,Pick);
            Row("Высота антенны над землёй, м",antenna,88);Row("Высота дрона, м",drone,126);altitude.Items.AddRange(new object[]{"Над рельефом в каждой точке","От земли в точке старта"});altitude.SelectedIndex=0;Row("Отсчёт высоты дрона",altitude,164);Row("Частота связи, МГц",frequency,202);Row("Радиус расчёта, м",radius,240);quality.Items.AddRange(new object[]{"Быстро","Обычное","Подробно"});quality.SelectedIndex=1;Row("Детальность выборки",quality,278);
            fresnel.SetBounds(18,321,295,25);Controls.Add(fresnel);Row("Свободная доля радиуса F1, %",clearance,351);curvature.SetBounds(18,394,230,26);Controls.Add(curvature);k.SetBounds(270,390,235,29);Controls.Add(k);Controls.Add(new Label{Text="k = 4/3 по умолчанию",Location=new Point(270,422),Size=new Size(235,24)});
            online.SetBounds(18,424,250,26);online.Checked=map.Tiles.OnlineEnabled;Controls.Add(online);automatic.SetBounds(18,457,490,25);Controls.Add(automatic);Row("Непрозрачность слоя",opacity,492);
            resolution.SetBounds(18,535,495,35);Controls.Add(resolution);progress.SetBounds(18,578,488,16);Controls.Add(progress);status.SetBounds(18,603,495,44);Controls.Add(status);
            calculate.SetBounds(18,674,145,32);stop.SetBounds(174,674,145,32);show.SetBounds(330,674,175,32);Controls.Add(calculate);Controls.Add(stop);Controls.Add(show);Controls.Add(new Label{Text="По рельефу DEM; без зданий и деревьев. Не прогноз качества связи.",Location=new Point(18,651),Size=new Size(495,25),Font=new Font("Segoe UI",8)});
            VisibilityOptions saved;try{saved=VisibilityOptions.Load(Path.Combine(folder,"visibility.xml"));}catch{saved=new VisibilityOptions();}antenna.Value=(decimal)saved.Antenna;drone.Value=(decimal)saved.Drone;frequency.Value=(decimal)saved.Frequency;radius.Value=(decimal)saved.Radius;clearance.Value=(decimal)(saved.Clearance*100);k.Value=(decimal)saved.K;quality.SelectedIndex=saved.Quality;altitude.SelectedIndex=saved.AboveStart?1:0;fresnel.Checked=saved.Fresnel;curvature.Checked=saved.Curvature;opacity.Value=saved.Opacity;map.VisibilityOpacity=saved.Opacity;
            foreach(var n in new[]{antenna,drone,frequency,radius,clearance,k})n.ValueChanged+=delegate{ParametersChanged();};quality.SelectedIndexChanged+=delegate{ParametersChanged();};altitude.SelectedIndexChanged+=delegate{ParametersChanged();};fresnel.CheckedChanged+=delegate{clearance.Enabled=fresnel.Checked;ParametersChanged();};curvature.CheckedChanged+=delegate{k.Enabled=curvature.Checked;ParametersChanged();};online.CheckedChanged+=delegate{ParametersChanged();};automatic.CheckedChanged+=delegate{debounce.Stop();if(automatic.Checked)debounce.Start();};opacity.ValueChanged+=delegate{map.VisibilityOpacity=opacity.Value;map.RequestFrame();};
            calculate.Click+=async delegate{await StartCalculation();};stop.Click+=delegate{CancelWork();status.Text="Расчёт остановлен. Полученные высоты остаются в кэше.";};show.Click+=delegate{map.VisibilityShown=!map.VisibilityShown;show.Text=map.VisibilityShown?"Скрыть слой":"Показать слой";map.RequestFrame();};timer.Tick+=delegate{LoadBatch();};debounce.Tick+=async delegate{debounce.Stop();await StartCalculation();};
            FormClosing+=delegate{CancelWork();try{Options().Save(Path.Combine(folder,"visibility.xml"));}catch(Exception ex){Diagnostics.Record("Visibility.failure",ex);status.Text="Не удалось сохранить параметры: "+ex.Message;}};FormClosed+=delegate{Cleanup();};origin=map.Selected as GeoPoint??map.Center;initializing=false;SetOrigin(origin);clearance.Enabled=fresnel.Checked;k.Enabled=curvature.Checked;
        }
        VisibilityOptions Options(){return new VisibilityOptions{Antenna=(double)antenna.Value,Drone=(double)drone.Value,Frequency=(double)frequency.Value,Radius=(double)radius.Value,Clearance=(double)clearance.Value/100,K=(double)k.Value,Quality=quality.SelectedIndex,AboveStart=altitude.SelectedIndex==1,Curvature=curvature.Checked,Fresnel=fresnel.Checked,Opacity=opacity.Value};}
        void SetOrigin(GeoPoint p){origin=new GeoPoint(p.Lat,p.Lon);position.Text="Антенна: "+p.Lat.ToString("F6")+", "+p.Lon.ToString("F6");ParametersChanged();}
        void Pick(){if(map.HasDraft||map.ProfilePick!=null){status.Text="Завершите построение или выбор точки на карте.";return;}CancelWork();Hide();pickCallback=delegate(GeoPoint p){map.ProfilePick=null;pickCallback=null;if(closed)return;if(p!=null)SetOrigin(p);Show();Activate();};map.ProfilePick=pickCallback;map.Focus();}
        void ParametersChanged(){if(initializing)return;CancelWork();map.SetVisibility(null);status.Text="Параметры изменены. Нажмите «Рассчитать».";var o=Options();int steps=Math.Max(4,Math.Min(new[]{128,256,512}[o.Quality],(int)Math.Ceiling(o.Radius/new[]{40,20,10}[o.Quality])));resolution.Text="Шаг по дальности ≈ "+(o.Radius/steps).ToString("0.#")+" м; по окружности ≈ "+(2*Math.PI*o.Radius/new[]{180,360,720}[o.Quality]).ToString("0.#")+" м.\nДетальность выборки не повышает исходную точность DEM.";if(automatic.Checked)debounce.Start();}
        void CancelWork(){generation++;timer.Stop();debounce.Stop();loading=false;if(cancellation!=null){cancellation.Cancel();BackgroundJobs.DisposeAfter(calculationTask,cancellation);cancellation=null;}elevation.Tiles.DiscardQueued();elevation.Tiles.OnlineEnabled=false;calculate.Enabled=true;stop.Enabled=false;}
        internal async Task StartCalculation(){CancelWork();int id=generation;cancellation=new CancellationTokenSource();var token=cancellation.Token;var o=Options();var center=new GeoPoint(origin.Lat,origin.Lon);calculate.Enabled=false;stop.Enabled=true;map.SetVisibility(null);status.Text="Подготовка области…";
            try{var preparation=BackgroundJobs.Run("visibility",()=>VisibilityGrid.Create(center,o,token));calculationTask=preparation;var prepared=await preparation;if(closed||id!=generation)return;grid=prepared;cursor=ready=0;started=DateTime.UtcNow;loading=true;progress.Maximum=grid.Points.Length;progress.Value=0;elevation.Tiles.OnlineEnabled=online.Checked;timer.Start();LoadBatch();}catch(OperationCanceledException){}catch(Exception ex){Diagnostics.Record("Visibility.failure",ex);if(!closed&&id==generation){CancelWork();status.Text=ex.Message;}}
        }
        void LoadBatch(){if(!loading||grid==null)return;try{var watch=System.Diagnostics.Stopwatch.StartNew();int n=grid.Points.Length;for(int j=0;j<2048&&watch.ElapsedMilliseconds<9;j++){int index=cursor++%n;if(GeoMath.IsFinite(grid.Heights[index]))continue;var height=elevation.At(grid.Points[index],14);if(height.HasValue){grid.Heights[index]=height.Value;ready++;}}
            progress.Value=ready;status.Text="Высоты: "+ready+" / "+n+". Ожидание "+(int)(DateTime.UtcNow-started).TotalSeconds+" с из 180.";
            bool offlineDone=!online.Checked&&cursor>=n&&(DateTime.UtcNow-started).TotalSeconds>3&&elevation.Tiles.PendingCount==0&&elevation.PendingBuilds==0;
            if(ready==n||offlineDone||(DateTime.UtcNow-started).TotalSeconds>=180){timer.Stop();loading=false;FinishCalculation();}
        }catch(Exception ex){Diagnostics.Record("Visibility.failure",ex);CancelWork();status.Text="Не удалось прочитать высоты: "+ex.Message;}}
        async void FinishCalculation(){int id=generation;var token=cancellation.Token;var current=grid;status.Text="Расчёт прямой видимости и зоны Френеля…";try{var calculation=BackgroundJobs.Run("visibility",()=>{current.Calculate(token);return VisibilityRaster.Create(current,token);});calculationTask=calculation;var raster=await calculation;if(closed||id!=generation){raster.Dispose();return;}map.SetVisibility(raster);map.VisibilityShown=true;show.Text="Скрыть слой";int unknown=current.Classes.Skip(1).Count(v=>v==VisibilityClass.Unknown);status.Text=unknown==0?"Готово. Зелёный — свободно; жёлтый — Френель; красный — прямая закрыта.":"Готово частично: "+unknown+" ячеек без расчёта (штриховка). Нет DEM или слишком короткая трасса. Повторите после дозагрузки.";calculate.Enabled=true;stop.Enabled=false;elevation.Tiles.OnlineEnabled=false;Options().Save(Path.Combine(folder,"visibility.xml"));}catch(OperationCanceledException){}catch(Exception ex){Diagnostics.Record("Visibility.failure",ex);if(!closed&&id==generation){CancelWork();status.Text="Расчёт не завершён: "+ex.Message;}}
        }
        void Cleanup(){if(closed)return;closed=true;CancelWork();if(pickCallback!=null&&map.ProfilePick==pickCallback)map.ProfilePick=null;timer.Dispose();debounce.Dispose();elevation.Dispose();}
        protected override void Dispose(bool disposing){if(disposing)Cleanup();base.Dispose(disposing);}
    }
}


