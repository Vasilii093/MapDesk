using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlpineQuestPort {
    internal sealed class OfflineTile { public int Z,X,Y; }
    internal static class OfflinePlan {
        internal static List<OfflineTile> Create(GeoPoint a,GeoPoint b,int min,int max) {
            if(a==null||b==null||!GeoMath.IsValidCoordinate(a.Lat,a.Lon)||!GeoMath.IsValidCoordinate(b.Lat,b.Lon)||min<0||max>20||min>max)throw new ArgumentException("Проверьте область и детализацию.");
            double west=Math.Min(a.Lon,b.Lon),east=Math.Max(a.Lon,b.Lon);
            if(east-west>180)throw new ArgumentException("Выделите область с одной стороны от меридиана 180°.");
            var result=new List<OfflineTile>();
            for(int z=min;z<=max;z++) {
                int n=1<<z;Func<double,int> tx=lon=>Math.Max(0,Math.Min(n-1,(int)Math.Floor((lon+180)/360*n)));
                Func<double,int> ty=lat=>{lat=Math.Max(-GeoMath.MaxMercatorLatitude,Math.Min(GeoMath.MaxMercatorLatitude,lat))*Math.PI/180;return Math.Max(0,Math.Min(n-1,(int)Math.Floor((1-Math.Log(Math.Tan(lat)+1/Math.Cos(lat))/Math.PI)/2*n)));};
                int x0=tx(west),x1=tx(east),y0=ty(Math.Max(a.Lat,b.Lat)),y1=ty(Math.Min(a.Lat,b.Lat));
                if((long)(x1-x0+1)*(y1-y0+1)+result.Count>20000)throw new ArgumentException("Более 20 000 фрагментов: уменьшите область или детализацию.");
                for(int x=x0;x<=x1;x++)for(int y=y0;y<=y1;y++)result.Add(new OfflineTile{Z=z,X=x,Y=y});
            }return result;
        }
    }
    sealed partial class MapCanvas {
        Action<GeoPoint,GeoPoint> regionDone;
        Point regionStart,regionEnd;bool regionDragging;
        public void SelectOfflineRegion(Action<GeoPoint,GeoPoint> callback){CancelDraft();regionDone=callback;Cursor=Cursors.Cross;Focus();RequestFrame();}
        bool CancelRegion(){if(regionDone==null)return false;regionDone=null;regionDragging=false;Capture=false;Cursor=Cursors.Hand;RequestFrame();return true;}
        bool RegionDown(MouseEventArgs e){if(regionDone==null)return false;if(e.Button==MouseButtons.Right){CancelRegion();return true;}if(e.Button==MouseButtons.Left){regionStart=regionEnd=e.Location;regionDragging=true;Capture=true;}return true;}
        bool RegionMove(MouseEventArgs e){if(regionDone==null)return false;if(regionDragging){regionEnd=e.Location;RequestFrame();}return true;}
        bool RegionUp(MouseEventArgs e){if(regionDone==null)return false;if(e.Button!=MouseButtons.Left||!regionDragging)return true;regionEnd=e.Location;
            if(Math.Abs(regionStart.X-regionEnd.X)<8||Math.Abs(regionStart.Y-regionEnd.Y)<8){regionDragging=false;Capture=false;RequestFrame();return true;}
            var points=new[]{FromScreen(regionStart),FromScreen(regionEnd),FromScreen(new Point(regionStart.X,regionEnd.Y)),FromScreen(new Point(regionEnd.X,regionStart.Y))};
            var a=new GeoPoint(points.Min(p=>p.Lat),points.Min(p=>p.Lon));var b=new GeoPoint(points.Max(p=>p.Lat),points.Max(p=>p.Lon));var callback=regionDone;CancelRegion();BeginInvoke(new Action(()=>callback(a,b)));return true;
        }
        void DrawRegion(Graphics g){if(regionDone==null)return;DrawLabel(g,"Выделите область мышью · Esc — отмена",16,Height-110);if(!regionDragging)return;var rect=Rectangle.FromLTRB(Math.Min(regionStart.X,regionEnd.X),Math.Min(regionStart.Y,regionEnd.Y),Math.Max(regionStart.X,regionEnd.X),Math.Max(regionStart.Y,regionEnd.Y));using(var fill=new SolidBrush(Color.FromArgb(50,0,180,220)))using(var pen=new Pen(Color.DeepSkyBlue,3)){g.FillRectangle(fill,rect);g.DrawRectangle(pen,rect);}}
    }
    sealed partial class MainForm {
        void InitializeDownloads(ToolStrip tools){AddTool(tools,"Сохранённые области…","Список областей и возобновление загрузки",delegate{using(var dialog=new OfflineAreasForm(dataDir))dialog.ShowDialog(this);});AddTool(tools,"Скачать область…","Выделить участок и сохранить карту с выбором детализации",delegate{
            if(map.HasDraft){MessageBox.Show(this,"Завершите построение или правку.");return;}
            map.SelectOfflineRegion(delegate(GeoPoint a,GeoPoint b){using(var dialog=new OfflineDownloadForm(dataDir,currentSource,map.Zoom,a,b))dialog.ShowDialog(this);SetSource(currentSource);map.RequestFrame();});
        });}
    }
    internal sealed class OfflineDownloadForm:Form {
        readonly string root;readonly MapSource source;readonly GeoPoint a,b;
        readonly NumericUpDown min=new NumericUpDown(),max=new NumericUpDown();
        readonly CheckBox dem=new CheckBox{Text="Также сохранить высоты DEM Z14 для профиля и Френеля",AutoSize=true,Checked=true};
        readonly Label estimate=new Label{AutoSize=false},status=new Label{AutoSize=false};
        readonly ProgressBar progress=new ProgressBar();readonly Button start=new Button{Text="Сохранить"},cancel=new Button{Text="Закрыть"};
        OfflineArea area;CancellationTokenSource cancellation;bool running;List<OfflineTile> mapPlan,demPlan;
        public OfflineDownloadForm(string folder,MapSource selected,int zoom,GeoPoint first,GeoPoint second,OfflineArea previous=null){
            area=previous;root=folder;source=selected.Copy();a=first;b=second;Text="Сохранить область карты";Font=new Font("Segoe UI",10);ClientSize=new Size(640,390);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;
            Controls.Add(new Label{Text=source+" · язык: "+(source.Language=="x-local"?"автоматически":source.Language),Location=new Point(20,15),Size=new Size(600,30)});
            Controls.Add(new Label{Text="Детализация: от Z",Location=new Point(20,59),AutoSize=true});min.SetBounds(160,55,65,30);max.SetBounds(310,55,65,30);Controls.Add(min);Controls.Add(max);Controls.Add(new Label{Text="до Z",Location=new Point(260,59),AutoSize=true});
            min.Minimum=max.Minimum=source.MinZoom;min.Maximum=max.Maximum=source.MaxZoom;max.Value=Math.Max(source.MinZoom,Math.Min(source.MaxZoom,zoom));min.Value=Math.Max(source.MinZoom,Math.Min((int)max.Value,10));
            Controls.Add(new Label{Text="Z10–12 — обзор · Z14–16 — местность · Z17–20 — подробности.\nКаждый масштаб сохраняется отдельно; больший Z требует больше места.",Location=new Point(20,95),Size=new Size(600,48)});
            dem.Location=new Point(20,150);Controls.Add(dem);estimate.SetBounds(20,183,600,63);Controls.Add(estimate);
            progress.SetBounds(20,253,600,20);Controls.Add(progress);status.SetBounds(20,280,600,55);Controls.Add(status);start.SetBounds(365,345,125,32);cancel.SetBounds(500,345,120,32);Controls.Add(start);Controls.Add(cancel);
            if(area!=null){min.Value=area.Min;max.Value=area.Max;dem.Checked=area.Dem;}min.ValueChanged+=delegate{UpdateEstimate();};max.ValueChanged+=delegate{UpdateEstimate();};dem.CheckedChanged+=delegate{UpdateEstimate();};start.Click+=async delegate{await Download();};cancel.Click+=delegate{if(running){cancellation.Cancel();cancel.Enabled=false;status.Text="Останавливаем загрузку…";}else Close();};FormClosing+=delegate(object sender,FormClosingEventArgs e){if(running){e.Cancel=true;cancellation.Cancel();status.Text="Останавливаем загрузку… Дождитесь завершения.";}};UpdateEstimate();
        }
        void UpdateEstimate(){try{mapPlan=OfflinePlan.Create(a,b,(int)min.Value,(int)max.Value);demPlan=dem.Checked?OfflinePlan.Create(a,b,14,14):new List<OfflineTile>();int count=mapPlan.Count+demPlan.Count;if(count>20000)throw new ArgumentException("Всего более 20 000 фрагментов: уменьшите детализацию.");estimate.Text=count+" фрагментов (карта: "+mapPlan.Count+", высоты: "+demPlan.Count+").\nОриентировочно "+(count*10/1024.0).ToString("F0")+"–"+(count*200/1024.0).ToString("F0")+" МиБ до учёта кэша; размер зависит от источника.\nЗащищено от автоочистки; свободно на диске: "+(new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))).AvailableFreeSpace/1073741824.0).ToString("F1")+" ГиБ.";start.Enabled=true;}catch(Exception ex){estimate.Text=ex.Message;start.Enabled=false;}}
        internal async Task Download(){if(running)return;UpdateEstimate();if(!start.Enabled)return;running=true;start.Enabled=min.Enabled=max.Enabled=dem.Enabled=false;cancel.Text="Остановить";cancellation=new CancellationTokenSource();int done=0,failed=0,total=mapPlan.Count+demPlan.Count;progress.Maximum=total;progress.Value=0;string lastError="";
            var report=new Progress<int>(value=>{progress.Value=Math.Min(total,value);status.Text="Проверено и обработано: "+value+" / "+total+". Уже сохранённое не скачивается повторно.";});
            try{
                if(area==null)area=new OfflineArea{Source=source.Copy(),Lat1=a.Lat,Lon1=a.Lon,Lat2=b.Lat,Lon2=b.Lon};
                area.Min=(int)min.Value;area.Max=(int)max.Value;area.Dem=dem.Checked;area.Total=total;area.Done=area.Failed=0;area.State="Загрузка";OfflineAreaCatalog.Update(root,area);
                await BackgroundJobs.Run("offline.download",async delegate{
                using(var mapStore=new TileStore(Path.Combine(root,"tile-cache-v2",source.CacheKey),null,source))using(var heights=new ElevationStore(Path.Combine(root,"elevation-cache"))){
                    mapStore.ForceDiskCache=true;mapStore.OnlineEnabled=true;heights.Tiles.OnlineEnabled=true;
                    foreach(var group in new[]{new{Store=mapStore,Plan=mapPlan},new{Store=heights.Tiles,Plan=demPlan}})foreach(var tile in group.Plan){cancellation.Token.ThrowIfCancellationRequested();if(!await group.Store.SaveOfflineTile(tile.Z,tile.X,tile.Y,cancellation.Token)){failed++;lastError=group.Store.LastError;}((IProgress<int>)report).Report(++done);if(done%32==0){area.Done=done;area.Failed=failed;OfflineAreaCatalog.Update(root,area);}}
                }});area.State=failed==0?"Готово":"Частично";status.Text=failed==0?"Готово: "+done+" фрагментов доступны офлайн.":"Не удалось сохранить "+failed+" из "+total+". Повторите сохранение для дозагрузки. "+lastError;
            }catch(OperationCanceledException){if(area!=null)area.State="Остановлено";status.Text="Остановлено: обработано "+done+" из "+total+". Повторите сохранение — готовые фрагменты останутся.";}catch(Exception ex){Diagnostics.Record("offline.download",ex);if(area!=null)area.State="Ошибка";status.Text="Загрузка прервана: "+ex.Message+". Готовые фрагменты сохранены.";}
            finally{if(area!=null)try{area.Done=done;area.Failed=failed;OfflineAreaCatalog.Update(root,area);}catch(Exception ex){Diagnostics.Record("offline.checkpoint",ex);status.Text+=" Не удалось сохранить состояние области: "+ex.Message;}running=false;cancellation.Dispose();cancellation=null;min.Enabled=max.Enabled=dem.Enabled=cancel.Enabled=true;cancel.Text="Закрыть";start.Text="Дозагрузить";UpdateEstimate();}
        }
    }
}


