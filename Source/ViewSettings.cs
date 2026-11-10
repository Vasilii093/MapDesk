using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AlpineQuestPort {
 public sealed class YesNoConverter:BooleanConverter {
  public override object ConvertTo(ITypeDescriptorContext context,CultureInfo culture,object value,Type destinationType){if(destinationType==typeof(string)&&value is bool)return (bool)value?"Да":"Нет";return base.ConvertTo(context,culture,value,destinationType);}
  public override object ConvertFrom(ITypeDescriptorContext context,CultureInfo culture,object value){if(value is string){if((string)value=="Да")return true;if((string)value=="Нет")return false;}return base.ConvertFrom(context,culture,value);}
 }
 public enum DistanceUnits { Метрические, Мили_и_футы, Морские_мили }
 public enum AngleFormat { Десятичные_градусы, Градусы_минуты, Градусы_минуты_секунды }
 public sealed class ViewSettings {
  [Category("Слои и кэш"),DisplayName("Обновлять сохранённые фрагменты"),Description("Нет: сеть используется только для отсутствующих или повреждённых фрагментов. Да: проверять сохранённые после истечения срока сервера.")] public bool RevalidateSaved {get;set;}
  [Browsable(false)] public decimal CacheLimitGb {get;set;}
  [Category("Оформление"),DisplayName("Непрозрачность карточек карты (30–100)")] public int CardOpacity {get;set;}
  [Category("Оформление"),DisplayName("Компактные карточки карты")] public bool CompactCards {get;set;}
  [Browsable(false)] public bool SidebarCollapsed {get;set;}
  [Category("Карта"),DisplayName("Служебная информация поверх карты"),Description("Режим онлайн/офлайн, источник и масштаб, GPU, частота кадров, поворот и расстояние луча до центра.")] public bool Diagnostics {get;set;}
  [Category("Слои и кэш"),DisplayName("Принудительно сохранять просмотренные тайлы")] public bool ForceTileCache {get;set;}
  [Category("Слои и кэш"),DisplayName("Показывать базовую карту")] public bool BaseVisible {get;set;}
  [Category("Слои и кэш"),DisplayName("Непрозрачность базовой карты (0–100)")] public int BaseOpacity {get;set;}
  [Category("Единицы и координаты"),DisplayName("Единицы направления")] public BearingUnits BearingFormat {get;set;}
  [Category("Карта"),DisplayName("Детализация источника: ступеней ниже (0–8)"),Description("Если источник показывает Map data not yet available, выберите 1–3. Масштаб карты сохранится, снимок будет взят с более низкого уровня.")] public int SourceZoomOffset {get;set;}
  [Category("Круговая линейка"),DisplayName("Показывать кольца расстояний")] public bool RangeRings {get;set;}
  [Category("Круговая линейка"),DisplayName("Шаг колец в метрах (0 — автоматически)"),Description("Например, 50000 для колец 50, 100 и 150 км. Максимум 2000000 м.")] public double RingStepMeters {get;set;}
  [Category("Карта"),DisplayName("Масштабная линейка")] public bool ScaleBar {get;set;}
  [Category("Карта"),DisplayName("Карточки данных поверх карты")] public bool DataCards {get;set;}
  [Category("Единицы и координаты"),DisplayName("Единицы расстояния")] public DistanceUnits Units {get;set;}
  [Category("Единицы и координаты"),DisplayName("Формат широты и долготы")] public AngleFormat Format {get;set;}
  [Category("Единицы и координаты"),DisplayName("Дополнительно показывать WGS84")] public bool SecondaryWgs {get;set;}
  [Category("Карта"),DisplayName("Перекрестие центра")] public bool Crosshair {get;set;}
  [Category("Карта"),DisplayName("Подписи точек")] public bool PointNames {get;set;}
  [Category("Карта"),DisplayName("Ширина линии (1–10)")] public int LineWidth {get;set;}
  [Browsable(false)] public bool RayEnabled {get;set;}
  [Browsable(false)] public double RayLat {get;set;}
  [Browsable(false)] public double RayLon {get;set;}
  [Category("Карта"),DisplayName("Поворот карты (0–359 градусов)")] public double Rotation {get;set;}
  [Category("Карта"),DisplayName("Перспективный вид")] public bool Perspective {get;set;}
  [Category("Карта"),DisplayName("Предел кадров/с (15–300)")] public int MaxFps {get;set;}
  [Category("Карта"),DisplayName("GPU-ускорение карты")] public bool Gpu {get;set;}
  [Category("Карта"),DisplayName("Наклон (15–60 градусов)")] public int Tilt {get;set;}
  [Category("Высоты"),DisplayName("Матрица высот для точек")] public bool Elevation {get;set;}
  [Browsable(false)] public int Opacity {get;set;}
  [Browsable(false)] public bool FollowGps {get;set;}
  [Browsable(false)] public int RecordStep {get;set;}
  public ViewSettings(){CompactCards=true;CardOpacity=75;ForceTileCache=true;BaseVisible=true;BaseOpacity=100;RangeRings=true;ScaleBar=true;MaxFps=300;Gpu=true;Elevation=true;DataCards=true;Crosshair=true;PointNames=true;LineWidth=3;Tilt=45;Opacity=65;RecordStep=2;}
  public ViewSettings Clone(){return (ViewSettings)MemberwiseClone();}
  public void Validate(){if(CacheLimitGb<0||CacheLimitGb>10000||CardOpacity<30||CardOpacity>100||BaseOpacity<0||BaseOpacity>100||!GeoMath.IsValidCoordinate(RayLat,RayLon)||!GeoMath.IsFinite(Rotation)||Rotation<0||Rotation>=360||!GeoMath.IsFinite(RingStepMeters)||RingStepMeters<0||RingStepMeters>2000000||SourceZoomOffset<0||SourceZoomOffset>8||MaxFps<15||MaxFps>300||!Enum.IsDefined(typeof(BearingUnits),BearingFormat)||!Enum.IsDefined(typeof(DistanceUnits),Units)||!Enum.IsDefined(typeof(AngleFormat),Format)||LineWidth<1||LineWidth>10||Tilt<15||Tilt>60||Opacity<10||Opacity>100||RecordStep<0||RecordStep>100)throw new ArgumentException("Проверьте диапазоны числовых настроек.");}
        public void Save(string path){Validate();AtomicSettings.Save(path,stream=>new XmlSerializer(typeof(ViewSettings)).Serialize(stream,this));}
        public static ViewSettings Load(string path){return AtomicSettings.Load(path,stream=>{using(var r=XmlReader.Create(stream,AtomicSettings.ReaderSettings())){var s=(ViewSettings)new XmlSerializer(typeof(ViewSettings)).Deserialize(r);s.Validate();return s;}});}
  public string Distance(double m){if(Units==DistanceUnits.Мили_и_футы)return m>=1609.344?(m/1609.344).ToString("F2")+" mi":(m/0.3048).ToString("F0")+" ft";if(Units==DistanceUnits.Морские_мили)return (m/1852).ToString("F2")+" NM";return m>=1000?(m/1000).ToString("F2")+" км":m>=1?m.ToString("F0")+" м":m>=0.01?(m*100).ToString("0.##")+" см":(m*1000).ToString("0.###")+" мм";}
  public string Height(double m){return Units==DistanceUnits.Мили_и_футы?(m/0.3048).ToString("F0")+" ft":m.ToString("F0")+" м";}
  public string Angle(double value){string sign=value<0?"−":"";double a=Math.Abs(value);if(Format==AngleFormat.Десятичные_градусы)return value.ToString("F6",CultureInfo.InvariantCulture)+"°";if(Format==AngleFormat.Градусы_минуты){long total=(long)Math.Round(a*60000);return sign+(total/60000)+"° "+((total%60000)/1000.0).ToString("00.000",CultureInfo.InvariantCulture)+"′";}long ticks=(long)Math.Round(a*360000);return sign+(ticks/360000)+"° "+(ticks/6000%60).ToString("00")+"′ "+((ticks%6000)/100.0).ToString("00.00",CultureInfo.InvariantCulture)+"″";}
 }
 sealed class SettingsDialog:Form {
  public ViewSettings Settings;
  public SettingsDialog(ViewSettings settings){TypeDescriptor.AddAttributes(typeof(bool),new TypeConverterAttribute(typeof(YesNoConverter)));Settings=settings.Clone();Text="Настройки MapDesk";ClientSize=new Size(660,570);MinimumSize=new Size(580,480);Font=new Font("Segoe UI",10);StartPosition=FormStartPosition.CenterParent;
   var grid=new PropertyGrid{Dock=DockStyle.Fill,SelectedObject=Settings,ToolbarVisible=false,HelpVisible=true};
   var note=new Label{Dock=DockStyle.Top,Height=74,Padding=new Padding(12),Text="Единицы, формат координат, вид карты и высоты.\nСистема координат (включая СК-42) выбирается кнопкой «Координаты…»."};
   var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(5)};
   var cancel=new Button{Text="Отмена",Width=120,Height=32,DialogResult=DialogResult.Cancel};var apply=new Button{Text="Применить",Width=130,Height=32};apply.Click+=delegate{try{grid.Refresh();Settings.Validate();DialogResult=DialogResult.OK;Close();}catch(Exception e){MessageBox.Show(this,e.Message);}};buttons.Controls.Add(cancel);buttons.Controls.Add(apply);CancelButton=cancel;AcceptButton=apply;Controls.Add(grid);Controls.Add(note);Controls.Add(buttons);
  }
 }
 public static class PerspectiveMath {
  public static PointF Rotate(double x,double y,double degrees){double a=degrees*Math.PI/180,c=Math.Cos(a),s=Math.Sin(a);return new PointF((float)(x*c-y*s),(float)(x*s+y*c));}
  public static PointF Forward(double x,double y,double height,double degrees){double r=degrees*Math.PI/180,c=Math.Cos(r),k=Math.Sin(r)*0.6,d=1-k*y/height;if(d<=0.08)return new PointF(float.NaN,float.NaN);return new PointF((float)(x/d),(float)(c*y/d));}
  public static PointF Inverse(double x,double y,double height,double degrees){double r=degrees*Math.PI/180,c=Math.Cos(r),k=Math.Sin(r)*0.6,py=y/(c+k*y/height),d=1-k*py/height;return new PointF((float)(x*d),(float)py);}
 }
 public sealed class PerspectiveRaster : IDisposable {
  Bitmap target;

  internal static int WorkerOverride=0;
  internal static int WorkerCount(int width,int height,int processors){
   int budget=Math.Max(1,Math.Min(12,processors-2));
   return Math.Min(budget,Math.Max(1,(int)(((long)width*height+262143)/262144)));
  }
  byte[] sourceBytes=new byte[0],targetBytes=new byte[0];
  public void Draw(Graphics graphics,Bitmap plane,int width,int height,double minX,double minY,double degrees,double rotation=0){
   int sw=plane.Width,sh=plane.Height,count=sw*sh*4;if(sourceBytes.Length!=count)sourceBytes=new byte[count];if(targetBytes.Length!=width*height*4)targetBytes=new byte[width*height*4];
   var data=plane.LockBits(new Rectangle(0,0,sw,sh),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);try{Marshal.Copy(data.Scan0,sourceBytes,0,count);}finally{plane.UnlockBits(data);}
   double r=degrees*Math.PI/180,c=Math.Cos(r),k=Math.Sin(r)*0.6;
   int workers=WorkerOverride>0?WorkerOverride:WorkerCount(width,height,Environment.ProcessorCount);
   double yaw=rotation*Math.PI/180,rc=Math.Cos(yaw),rs=Math.Sin(yaw);
   Action<int> renderRow=row=>{double sy=row+0.5-height/2.0,py=sy/(c+k*sy/height),factor=1-k*py/height,fy=py-minY-0.5;int y=Math.Max(0,Math.Min(sh-2,(int)Math.Floor(fy)));double ty=Math.Max(0,Math.Min(1,fy-y));double fx=(0.5-width/2.0)*factor-minX-0.5;
    if(rotation!=0){double vx=(0.5-width/2.0)*factor,wx=vx*rc+py*rs-minX-0.5,wy=-vx*rs+py*rc-minY-0.5;
     for(int col=0;col<width;col++,wx+=factor*rc,wy-=factor*rs){int yy=Math.Max(0,Math.Min(sh-2,(int)Math.Floor(wy))),xx=Math.Max(0,Math.Min(sw-2,(int)Math.Floor(wx))),aa=(yy*sw+xx)*4,bb=aa+sw*4,dd=(row*width+col)*4;double tx2=Math.Max(0,Math.Min(1,wx-xx)),ty2=Math.Max(0,Math.Min(1,wy-yy));for(int ch=0;ch<3;ch++){double top=sourceBytes[aa+ch]*(1-tx2)+sourceBytes[aa+4+ch]*tx2,bottom=sourceBytes[bb+ch]*(1-tx2)+sourceBytes[bb+4+ch]*tx2;targetBytes[dd+ch]=(byte)(top*(1-ty2)+bottom*ty2);}targetBytes[dd+3]=255;}return;
    }
    for(int col=0;col<width;col++,fx+=factor){int x=Math.Max(0,Math.Min(sw-2,(int)Math.Floor(fx))),a=(y*sw+x)*4,b=a+sw*4,d=(row*width+col)*4;double tx=Math.Max(0,Math.Min(1,fx-x));for(int channel=0;channel<3;channel++){double top=sourceBytes[a+channel]*(1-tx)+sourceBytes[a+4+channel]*tx,bottom=sourceBytes[b+channel]*(1-tx)+sourceBytes[b+4+channel]*tx;targetBytes[d+channel]=(byte)(top*(1-ty)+bottom*ty);}targetBytes[d+3]=255;}
   };
   // Contiguous row bands avoid scheduling a work item for each scanline.
   if(workers==1){for(int row=0;row<height;row++)renderRow(row);}
   else {int bands=Math.Min(height,workers*4);Parallel.For(0,bands,new ParallelOptions{MaxDegreeOfParallelism=workers},band=>{int end=(int)((long)(band+1)*height/bands);for(int row=(int)((long)band*height/bands);row<end;row++)renderRow(row);});}
   if(target==null||target.Width!=width||target.Height!=height){if(target!=null)target.Dispose();target=new Bitmap(width,height,PixelFormat.Format32bppArgb);}
   {var dest=target.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);try{Marshal.Copy(targetBytes,0,dest.Scan0,targetBytes.Length);}finally{target.UnlockBits(dest);}graphics.DrawImageUnscaled(target,0,0);}
  }
  public void Dispose(){if(target!=null){target.Dispose();target=null;}sourceBytes=new byte[0];targetBytes=new byte[0];}
 }
}
