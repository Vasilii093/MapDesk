using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AlpineQuestPort
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--smoke-test") return Smoke.Run(args);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e) {
                Diagnostics.Show(null,"ui.unhandled",e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException+=delegate(object sender,UnhandledExceptionEventArgs e){Diagnostics.Record("process.unhandled",e.ExceptionObject as Exception);};
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException+=delegate(object sender,System.Threading.Tasks.UnobservedTaskExceptionEventArgs e){Diagnostics.Record("task.unobserved",e.Exception);e.SetObserved();};
            string workspaceDirectory=null;string initialFile=null;
            for(int i=0;i<args.Length;i++){if(args[i]=="--data-dir"&&i+1<args.Length)workspaceDirectory=Path.GetFullPath(args[++i]);else initialFile=args[i];}
            if(workspaceDirectory==null)workspaceDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MapDeskExperimental038");
            string mutexName="Local\\MapDeskPreview.Workspace";
            if(workspaceDirectory!=null)using(var hash=System.Security.Cryptography.SHA256.Create())mutexName+="."+BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(workspaceDirectory.ToUpperInvariant()))).Replace("-","");
            using (var mutex = new System.Threading.Mutex(false, mutexName)) {
                bool acquired;
                try { acquired = mutex.WaitOne(0); } catch (System.Threading.AbandonedMutexException) { acquired = true; }
                if (!acquired) { MessageBox.Show("MapDesk уже открыт. Используйте «Открыть» в работающем окне.", "MapDesk"); return 1; }
                try { Application.Run(new MainForm(initialFile,false,workspaceDirectory)); }
                finally { mutex.ReleaseMutex(); }
            }
            return 0;
        }
    }

    sealed partial class MainForm : Form
    {
        readonly MapCanvas map;
        readonly TreeView objects = new TreeView();
        readonly Label summary = new Label();
        readonly ToolStripStatusLabel message = new ToolStripStatusLabel();
        readonly ToolStripStatusLabel gpsLabel = new ToolStripStatusLabel();
        readonly ToolStripButton online = new ToolStripButton("Онлайн-карта");
        readonly ToolStripButton record = new ToolStripButton("Запись трека");
        readonly GpsService gps = new GpsService();
        readonly List<MapSource> sources=MapSource.BuiltIn();
        readonly ToolStripButton sourceButton=new ToolStripButton("Каталог карт…");
        ProjectionEngine projections;
        List<CrsEntry> crsCatalog;
        CrsEntry currentCrs=new CrsEntry{Id="EPSG:4326",Name="WGS 84",Kind="geographic 2D",Unit="degree"};
        MapSource currentSource;
        readonly string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapDeskExperimental038");
        bool dirty, restoring;
        Track recording;
        bool refreshingObjects;
        DateTime lastFix = DateTime.MinValue;
        readonly Timer fixTimer = new Timer();
        readonly bool previewMode;
        readonly Timer centerTimer = new Timer();
        readonly Label centerInfo = new Label();
        ViewSettings settings=new ViewSettings();
        readonly ToolStripButton rayButton=new ToolStripButton("Луч"){ToolTipText="Закрепить луч на выбранной метке / снять фиксацию"};
        readonly ToolStripButton ringsButton=new ToolStripButton("Кольца"){CheckOnClick=true,ToolTipText="Круговая линейка расстояний от центра карты"};
        readonly ToolStripButton scaleButton=new ToolStripButton("Линейка"){CheckOnClick=true,ToolTipText="Показать масштабную линейку на карте"};
        readonly ToolStripButton elevationButton=new ToolStripButton("Высота DEM"){CheckOnClick=true,ToolTipText="Матрица высот для координат и перепада. Без наложения на карту."};
        readonly ToolStripButton perspectiveButton=new ToolStripButton("Перспектива"){CheckOnClick=true};

        public MainForm(string initialFile, bool preview=false, string workspaceDirectory=null)
        {
            previewMode=preview;
            if(workspaceDirectory!=null)dataDir=Path.GetFullPath(workspaceDirectory);
            try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}
            Text = "MapDesk — Windows 0.40.4 experimental (" + (Environment.Is64BitProcess ? "x64" : "x86") + ")";
            MinimumSize = new Size(920, 600); Size = new Size(1280, 820); StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10); KeyPreview = true;
            Directory.CreateDirectory(dataDir);Diagnostics.Initialize(dataDir);
            LoadCatalogFiles();try{sources.AddRange(UserMapCatalog.Load(Path.Combine(dataDir,"custom-maps.xml")));}catch(Exception ex){Diagnostics.Record("Program.failure",ex);MessageBox.Show(this,"Не удалось прочитать пользовательские карты: "+ex.Message);}currentSource=sources[0];
            map = new MapCanvas(CreateTiles(currentSource));
            map.Elevation=new ElevationStore(Path.Combine(dataDir,"elevation-cache"));
            map.Elevation.Tiles.Changed+=delegate{map.RequestFrame();};
            map.Elevation.Changed+=delegate{map.RequestFrame();};
            map.Settings=settings;
            map.CoordinateSearchRequested+=ChooseCoordinates;
            map.CoordinateCopyRequested+=CopyCoordinates;
            var tools = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, AutoSize = false, Height = 44, Padding = new Padding(10, 5, 6, 5), BackColor = Color.FromArgb(243, 246, 243), Font = Font };
            AddTool(tools,"Диагностика…","Журнал ошибок без координат и ключей",delegate{Diagnostics.Show(this,"diagnostics.open",null);});
            AddTool(tools, "Открыть…", "GPX, KML, KMZ, LDK · Ctrl+O", delegate { OpenFile(); });
            AddTool(tools, "Экспорт…", "Сохранить GPX или KML · Ctrl+S", delegate { SaveFile(); });
            tools.Items.Add(new ToolStripSeparator());
            AddTool(tools,"Группы…","Перенести выбранные метки в группу",delegate{ManageGroups();});
            AddTool(tools, "Точка", "Поставить точку на карте · W", delegate { SetMode(MapMode.Waypoint); });
            AddTool(tools, "Маршрут", "Нарисовать маршрут по точкам · R. Enter — завершить", delegate { SetMode(MapMode.Route); });
            var zonesMenu=new ToolStripDropDownButton("Зона");
            zonesMenu.DropDownItems.Add("Многоугольник по точкам",null,delegate{SetMode(MapMode.Polygon);});
            zonesMenu.DropDownItems.Add("Прямоугольник",null,delegate{SetMode(MapMode.Rectangle);});
            zonesMenu.DropDownItems.Add("Круг",null,delegate{SetMode(MapMode.Circle);});tools.Items.Add(zonesMenu);
            AddTool(tools,"Правка","Изменить выбранный маршрут или зону · E",delegate{map.BeginEdit();map.Focus();});
            AddTool(tools,"Готово","Применить правку / закончить построение · Enter",delegate{map.FinishRoute();});
            AddTool(tools, "Обзор", "Показать все объекты · Home", delegate { map.Fit(); });
            tools.Items.Add(new ToolStripSeparator());
            online.CheckOnClick = true; online.ToolTipText = "Загрузка видимой области выбранной карты и включённого слоя высот";
            online.CheckedChanged += delegate { map.Tiles.OnlineEnabled = online.Checked; map.Elevation.Tiles.OnlineEnabled=online.Checked&&settings.Elevation; map.RequestFrame(); SetMessage(online.Checked ? "Онлайн: "+currentSource.Name+(settings.RevalidateSaved?". Сохранённые фрагменты проверяются по сроку сервера.":". Загружаются только отсутствующие фрагменты.") : "Офлайн: базовая карта и ранее просмотренные тайлы выбранного источника."); };
            tools.Items.Add(online);
            var gpsMenu = new ToolStripDropDownButton("GPS / позиция");
            gpsMenu.DropDownItems.Add("Источник Windows", null, delegate { gps.StartWindows(); });
            gpsMenu.DropDownItems.Add("Приёмник NMEA (COM)…", null, delegate { ConnectSerial(); });
            gpsMenu.DropDownItems.Add("К позиции", null, delegate { if (map.Fix != null) map.GoTo(map.Fix, 15); else SetMessage("Координаты ещё не получены."); });
            gpsMenu.DropDownItems.Add("Остановить", null, delegate { StopGps(); });
            record.CheckOnClick = true; record.ToolTipText = "Записывать полученные координаты в отдельный трек";
            record.CheckedChanged += delegate { recording = null; SetMessage(record.Checked ? "Запись включена. Ожидание новых координат от выбранного источника." : "Запись остановлена."); };
            AddTool(tools, "?", "Справка · F1", delegate { ShowHelp(); });

            var sidebar = new Panel { Dock = DockStyle.Left, Width = 265, Padding = new Padding(15), BackColor = Color.FromArgb(246, 248, 246) };
            summary.Dock = DockStyle.Top; summary.Height = 62; summary.Text = "МОИ ОБЪЕКТЫ\n\nОткройте GPX/KML или создайте точки."; summary.ForeColor = Color.FromArgb(40, 67, 59);
            objects.Dock = DockStyle.Fill; objects.BorderStyle = BorderStyle.None; objects.BackColor = sidebar.BackColor; objects.HideSelection = false;
            objects.NodeMouseDoubleClick += delegate(object s, TreeNodeMouseClickEventArgs e) { FocusObject(e.Node.Tag); };
            objects.AfterSelect += delegate { if(refreshingObjects)return; var next=objects.SelectedNode==null?null:objects.SelectedNode.Tag; bool wasEditing=map.IsEditing; if(wasEditing)map.EndEdit(true); map.Selected=next; if(wasEditing)RefreshObjects();else map.RequestFrame(); };
            var objectMenu = new ContextMenuStrip();
            objectMenu.Items.Add("Редактировать маршрут / зону",null,delegate{map.BeginEdit();map.Focus();});
            objectMenu.Items.Add("Цвет зоны…",null,delegate{map.ChangeZoneColor();});
            objectMenu.Items.Add("Показать на карте", null, delegate { if (objects.SelectedNode != null) FocusObject(objects.SelectedNode.Tag); });
            objectMenu.Items.Add("Изменить метку…",null,delegate{EditSelectedMarker();});
            objectMenu.Items.Add("Цвет метки…",null,delegate{ColorSelectedMarker();});
            objectMenu.Items.Add("Переименовать…", null, delegate { RenameSelected(); });
            objectMenu.Items.Add("Удалить", null, delegate { DeleteSelected(); });
            var rayItem=objectMenu.Items.Add("Закрепить начало луча",null,delegate{PinRay(map.Selected as GeoPoint);});
            objectMenu.Opening+=delegate{bool point=map.Selected is GeoPoint;rayItem.Enabled=point;objectMenu.Items[0].Enabled=map.Selected is Track||map.Selected is Zone;objectMenu.Items[1].Enabled=map.Selected is Zone;objectMenu.Items[2].Enabled=map.Selected!=null;objectMenu.Items[3].Enabled=point;objectMenu.Items[4].Enabled=point;};
            objects.ContextMenuStrip = objectMenu;
            map.PointSelected+=SelectMapPoint;
            map.MarkerAction+=delegate(GeoPoint point,string action){SelectMapPoint(point);if(action=="edit")EditSelectedMarker();else if(action=="color")ColorSelectedMarker();else if(action=="rename")RenameSelected();else if(action=="delete")DeleteSelected();else if(action=="ray")PinRay(point);};
            objects.NodeMouseClick += delegate(object s, TreeNodeMouseClickEventArgs e) { objects.SelectedNode = e.Node; };
            var hints = new Label { Dock = DockStyle.Bottom, Height = 124, ForeColor = Color.FromArgb(90, 110, 101), Text = "Мышь — перемещение\nКолесо — масштаб\nW — точка · R — маршрут · E — правка\nEnter — завершить · Esc — отмена\nCtrl+L — координаты\n\nДвойной щелчок по объекту — обзор", Font = new Font("Segoe UI", 9) };
            centerInfo.Dock=DockStyle.Top;centerInfo.Height=174;centerInfo.ForeColor=Color.FromArgb(22,54,48);centerInfo.Font=new Font("Segoe UI",10,FontStyle.Bold);centerInfo.Text="ЦЕНТР КАРТЫ";
            sidebar.Controls.Add(objects); sidebar.Controls.Add(hints); sidebar.Controls.Add(summary);sidebar.Controls.Add(centerInfo);
            map.Dock = DockStyle.Fill;
            var sourceTools=new ToolStrip{Dock=DockStyle.Top,Height=36,AutoSize=false,GripStyle=ToolStripGripStyle.Hidden,Font=Font,BackColor=Color.WhiteSmoke};
            sourceButton.Text="Карты: "+currentSource.Name+" ▾";sourceButton.Click+=delegate{ChooseMap();};sourceTools.Items.Add(sourceButton);sourceTools.Items.Add(new ToolStripSeparator());
            AddTool(sourceTools,"Координаты…","СК-42 и WGS84, ввод координат · Ctrl+L",delegate{ChooseCoordinates();});
            sourceTools.Items.Add(new ToolStripSeparator());sourceTools.Items.Add(elevationButton);sourceTools.Items.Add(perspectiveButton);sourceTools.Items.Add(scaleButton);sourceTools.Items.Add(ringsButton);sourceTools.Items.Add(rayButton);rayButton.Click+=delegate{if(settings.RayEnabled&&(map.LiveRay==null||!map.LiveRay.Visible)){var anchor=map.Document.Waypoints.FirstOrDefault(p=>p.Lat==settings.RayLat&&p.Lon==settings.RayLon);PinRay(anchor??map.Selected as GeoPoint);return;}if(settings.RayEnabled){settings.RayEnabled=false;rayButton.Checked=false;if(map.LiveRay!=null)map.LiveRay.Stop();map.RequestFrame();}else PinRay(map.Selected as GeoPoint);};ringsButton.CheckedChanged+=delegate{settings.RangeRings=ringsButton.Checked;map.RequestFrame();};scaleButton.CheckedChanged+=delegate{settings.ScaleBar=scaleButton.Checked;map.RequestFrame();};
            elevationButton.CheckedChanged+=delegate{settings.Elevation=elevationButton.Checked;map.Elevation.Tiles.OnlineEnabled=online.Checked&&settings.Elevation;map.RequestFrame();UpdateCenter();};
            perspectiveButton.CheckedChanged+=delegate{settings.Perspective=perspectiveButton.Checked;map.RequestFrame();};
            AddTool(sourceTools,"Слои…","Наложение карт, прозрачность, цветной DEM и кэш",delegate{using(var dialog=new LayersDialog(map,sources,SaveLayers))dialog.ShowDialog(this);});
            AddTool(sourceTools,"Настройки…","Единицы, координаты, карта, высоты, оформление",delegate{using(var dialog=new SettingsDialog(settings))if(dialog.ShowDialog(this)==DialogResult.OK){settings=dialog.Settings;ApplySettings();try{settings.Save(Path.Combine(dataDir,"view-settings.xml"));}catch(Exception ex){Diagnostics.Record("Program.failure",ex);MessageBox.Show(this,ex.Message,"Не удалось сохранить настройки");}}});
            // Coordinate-system selection is available through the Coordinates dialog.
            var content = new Panel { Dock = DockStyle.Fill }; content.Controls.Add(map); content.Controls.Add(sidebar);
            var status = new StatusStrip { Font = new Font("Segoe UI", 9) };
            message.Spring = true; message.TextAlign = ContentAlignment.MiddleLeft; gpsLabel.Text = "GPS: выключен";
            status.Items.Add(message);
            InitializeWorkspace(tools,status,sidebar);InitializeOptimizations(tools,sourceTools);InitializeWorkspaceTools(tools,sourceTools);InitializeDownloads(sourceTools);InitializeVisibility(sourceTools);InitializeRoutes();
            InitializeDisplayLayout(sidebar,content,sourceTools); 
            Controls.Add(content);Controls.Add(sourceTools); Controls.Add(tools); Controls.Add(status);content.BringToFront();

            map.DocumentChanged += delegate { MarkChanged(); RefreshObjects(); };
            map.ReadoutRequested+=UpdateCoordinates;
            map.Message += SetMessage;
            centerTimer.Interval=150;centerTimer.Tick+=delegate{UpdateCenter();};centerTimer.Start();
            gps.StatusChanged += delegate { SafeUi(delegate { gpsLabel.Text = gps.Status; }); };
            gps.Fix += delegate(object s, GpsFixEventArgs e) { SafeUi(delegate { OnFix(e); }); };
            fixTimer.Interval = 5000; fixTimer.Tick += delegate { if (map.Fix != null && (DateTime.UtcNow - lastFix).TotalSeconds > 30) { map.Fix = null; recording = null; map.RequestFrame(); gpsLabel.Text = "Позиция устарела — ожидание"; } }; 
            FormClosing += SaveOnClosing;
            Shown += delegate {
                if(previewMode){ApplySettings();ResetHistory();return;}
                Restore();
                ResetHistory();LoadGroupVisibility();
                RestorePreferences();
                if((currentCrs.Id=="EPSG:4326"&&!File.Exists(Path.Combine(dataDir,"preferences.xml")))||(currentCrs.Sk42&&currentCrs.Geographic))try{SelectSk42();}catch(Exception ex){Diagnostics.Record("Program.failure",ex);SetMessage("Выберите зону координат: "+ex.Message);}
                try{string viewPath=Path.Combine(dataDir,"view-settings.xml");if(File.Exists(viewPath))settings=ViewSettings.Load(viewPath);}catch(Exception ex){Diagnostics.Record("Program.failure",ex);SetMessage("Настройки вида не загружены: "+ex.Message);}ApplySettings();
                try{string layersFile=Path.Combine(dataDir,"layers.xml");if(File.Exists(layersFile))map.Layers=LayerConfiguration.Load(layersFile).Layers;map.ConfigureLayers(dataDir);}catch(Exception ex){Diagnostics.Record("Program.failure",ex);SetMessage("Слои не загружены: "+ex.Message);}
                if(settings.RayEnabled){var anchor=map.Document.Waypoints.FirstOrDefault(p=>p.Lat==settings.RayLat&&p.Lon==settings.RayLon);if(anchor!=null&&anchor.Radio.Enabled)map.StartLiveRay(anchor,Path.Combine(dataDir,"elevation-cache"));}
                preferencesReady=true;if (!String.IsNullOrEmpty(initialFile)) Import(initialFile);
                map.Focus();
            };
            SetMessage("Готово.");
        }

        protected override void Dispose(bool disposing){if(disposing){if(cardTimer!=null)cardTimer.Dispose();if(routeRefreshTimer!=null)routeRefreshTimer.Dispose();if(cardElevation!=null)cardElevation.Dispose();if(routeDetails!=null)routeDetails.Dispose();searchTimer.Dispose();cacheTimer.Dispose();autosaveTimer.Dispose();centerTimer.Dispose();fixTimer.Dispose();gps.Dispose();if(map!=null){map.Tiles.Dispose();if(map.Elevation!=null)map.Elevation.Dispose();}if(projections!=null)projections.Dispose();}base.Dispose(disposing);}
        static void AddTool(ToolStrip bar, string text, string hint, EventHandler handler) { var b = new ToolStripButton(text) { ToolTipText = hint, DisplayStyle = ToolStripItemDisplayStyle.Text }; b.Click += handler; bar.Items.Add(b); }
        TileStore CreateTiles(MapSource source){return new TileStore(Path.Combine(dataDir,"tile-cache-v2",source.CacheKey),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"default_map"),source){ForceDiskCache=settings.ForceTileCache,RevalidateSaved=settings.RevalidateSaved};}
        void LoadCatalogFiles(){string dir=Path.Combine(dataDir,"maps");if(!Directory.Exists(dir))return;foreach(string path in Directory.GetFiles(dir,"*.aqx"))try{string report;sources.AddRange(MapSource.LoadAqx(path,out report));}catch(Exception){}}
        void ImportCatalog(string path){string report;var added=MapSource.LoadAqx(path,out report);if(added.Count>0){string dir=Path.Combine(dataDir,"maps");Directory.CreateDirectory(dir);File.Copy(path,Path.Combine(dir,Guid.NewGuid().ToString("N")+".aqx"));sources.AddRange(added);}MessageBox.Show(this,report,"Импорт AQX");}
        void SaveCustomMap(MapSource source){
            UserMapCatalog.Validate(source);if(sources.Any(s=>s.Id!=source.Id&&s.CacheKey==source.CacheKey))throw new InvalidDataException("Источник с таким URL уже есть в каталоге.");
            var next=sources.Where(s=>s.UserDefined&&s.Id!=source.Id).Concat(new[]{source}).ToList();UserMapCatalog.Save(Path.Combine(dataDir,"custom-maps.xml"),next);
            int index=sources.FindIndex(s=>s.UserDefined&&s.Id==source.Id);if(index>=0)sources[index]=source;else sources.Add(source);if(currentSource.Id==source.Id)SetSource(source);
        }
        void DeleteCustomMap(MapSource source){if(!source.UserDefined)return;UserMapCatalog.Save(Path.Combine(dataDir,"custom-maps.xml"),sources.Where(s=>s.UserDefined&&s.Id!=source.Id));sources.RemoveAll(s=>s.UserDefined&&s.Id==source.Id);if(currentSource.Id==source.Id)SetSource(sources[0]);}
        void ChooseMap(){using(var d=new MapsDialog(sources,currentSource,ImportCatalog,SaveCustomMap,DeleteCustomMap,map.Center,map.Zoom))if(d.ShowDialog(this)==DialogResult.OK){SetSource(d.Chosen);online.Checked=true;}}
        void SetSource(MapSource selected){selected=selected.Copy();var catalogSource=sources.FirstOrDefault(s=>s.Id==selected.Id&&s.Template==selected.Template);if(catalogSource!=null)selected.Name=catalogSource.Name;bool enabled=online.Checked;var replacement=CreateTiles(selected);replacement.OnlineEnabled=enabled;var old=map.Tiles;currentSource=selected;map.SetTileStore(replacement);old.Dispose();sourceButton.Text="Карты: "+selected.Name+(selected.Template.Contains("{lang}")?" ["+selected.Language+"]":"")+" ▾";sourceButton.ToolTipText=selected.Attribution;map.GoTo(map.Center,Math.Max(selected.MinZoom,Math.Min(MapCanvas.MaxDisplayZoom,map.Zoom)));SetMessage("");RememberSource();}
        void EnsureProjections(){if(projections==null)projections=new ProjectionEngine();if(crsCatalog==null)crsCatalog=CrsEntry.Load();}
        void SelectSk42(){EnsureProjections();var chosen=CrsEntry.Sk42Zone(crsCatalog,map.Center);projections.FromWgs(map.Center,chosen.Id);currentCrs=chosen;lastCoordinateKey=null;UpdateCenter();map.RequestFrame();}
        string FormatPoint(GeoPoint point){if(currentCrs.Id=="EPSG:4326")return settings.Angle(point.Lat)+", "+settings.Angle(point.Lon);EnsureProjections();var r=projections.FromWgs(point,currentCrs.Id);if(currentCrs.Geographic)return "Ш = "+settings.Angle(r.Coordinate.Y)+"   Д = "+settings.Angle(r.Coordinate.X);return currentCrs.NorthFirst?"X = "+r.Coordinate.Y.ToString(currentCrs.Sk42?"F0":"F1")+"   Y = "+r.Coordinate.X.ToString(currentCrs.Sk42?"F0":"F1"):"E = "+r.Coordinate.X.ToString(currentCrs.Sk42?"F0":"F1")+"   N = "+r.Coordinate.Y.ToString(currentCrs.Sk42?"F0":"F1");}
        internal string CopyText(GeoPoint point,int format){
            if(format==0)return FormatPoint(point)+" · "+currentCrs.Id;
            if(format==1){EnsureProjections();var crs=CrsEntry.Sk42Zone(crsCatalog,point);var result=projections.FromWgs(point,crs.Id);return "X = "+result.Coordinate.Y.ToString("F0",CultureInfo.InvariantCulture)+"; Y = "+result.Coordinate.X.ToString("F0",CultureInfo.InvariantCulture)+" · СК-42 · "+crs.Id;}
            if(format==2)return point.Lat.ToString("F6",CultureInfo.InvariantCulture)+", "+point.Lon.ToString("F6",CultureInfo.InvariantCulture);
            var view=new ViewSettings{Format=format==3?AngleFormat.Градусы_минуты:AngleFormat.Градусы_минуты_секунды};return view.Angle(point.Lat)+", "+view.Angle(point.Lon)+" · WGS84";
        }
        void CopyCoordinates(GeoPoint point){using(var dialog=new CoordinateCopyDialog(point,delegate(int format){return CopyText(point,format);})){dialog.ShowDialog(this);}}
        void ChooseCoordinates(){try{EnsureProjections();using(var d=new CoordinatesDialog(projections,crsCatalog,currentCrs,map.Center,settings.Format))if(d.ShowDialog(this)==DialogResult.OK){currentCrs=d.Chosen;settings.Format=d.SelectedFormat;lastCoordinateKey=null;if(d.Target!=null)map.ShowTemporaryPoint(d.Target,true,14);UpdateCoordinates(map.Center);}}catch(Exception e){Diagnostics.Record("Program.failure",e);MessageBox.Show(this,e.Message,"Системы координат");}}
        void ApplySettings(){map.Settings=settings;map.Tiles.RevalidateSaved=settings.RevalidateSaved;map.Elevation.Tiles.RevalidateSaved=settings.RevalidateSaved;ApplySidebarLayout();rayButton.Checked=settings.RayEnabled;centerInfo.Visible=!settings.DataCards;scaleButton.Checked=settings.ScaleBar;ringsButton.Checked=settings.RangeRings;elevationButton.Checked=settings.Elevation;perspectiveButton.Checked=settings.Perspective;map.Elevation.Tiles.OnlineEnabled=online.Checked&&settings.Elevation;RefreshObjects();UpdateCenter();map.RequestFrame();}
        void UpdateCenter(){UpdateCoordinates(map.Center);UpdateContextTools();RememberView();}
        string lastCoordinateKey,lastCoordinateText,lastHudKey,lastTargetKey,lastTargetText;
        void UpdateCoordinates(GeoPoint p){
            string key=p.Lat.ToString("R",CultureInfo.InvariantCulture)+"|"+p.Lon.ToString("R",CultureInfo.InvariantCulture)+"|"+currentCrs.Id+"|"+settings.Format;
            string text=lastCoordinateText;if(key!=lastCoordinateKey){try{
                if(currentCrs.Id=="EPSG:4326")text="Ш: "+settings.Angle(p.Lat)+"\nД: "+settings.Angle(p.Lon);
                else {EnsureProjections();var r=projections.FromWgs(p,currentCrs.Id);text=currentCrs.Geographic?"Ш: "+settings.Angle(r.Coordinate.Y)+"\nД: "+settings.Angle(r.Coordinate.X):currentCrs.NorthFirst?"X: "+r.Coordinate.Y.ToString(currentCrs.Sk42?"F0":"F1")+"\nY: "+r.Coordinate.X.ToString(currentCrs.Sk42?"F0":"F1"):"E: "+r.Coordinate.X.ToString(currentCrs.Sk42?"F0":"F1")+"\nN: "+r.Coordinate.Y.ToString(currentCrs.Sk42?"F0":"F1");}
            }catch(Exception){text="Вне области применения\nили нет преобразования";}
            lastCoordinateKey=key;lastCoordinateText=text;}
            double? centerHeight=null;string height="Матрица высот выключена";if(settings.Elevation){double? h=map.Elevation.AtAvailable(p,map.Zoom);centerHeight=h;height=h.HasValue?"Высота DEM ≈ "+settings.Height(h.Value):map.Elevation.Tiles.PendingCount>0||map.Elevation.PendingBuilds>0?"Высота: загрузка…":!String.IsNullOrEmpty(map.Elevation.Tiles.LastError)?"Высота: ошибка загрузки":"Высота: нет данных в кеше";}
            string extra=settings.SecondaryWgs&&currentCrs.Id!="EPSG:4326"?"\nWGS84: "+p.Lat.ToString("F5")+", "+p.Lon.ToString("F5"):"";
            centerInfo.Text="ЦЕНТР КАРТЫ · "+currentCrs.Id+"\n"+text+extra+"\n"+height;
            map.Hud.Title="ЦЕНТР КАРТЫ · "+(currentCrs.Sk42?"СК-42 · ":"")+currentCrs.Id;
            map.Hud.Primary=text.Replace("\n","   ").Replace("X:","X =").Replace("Y:","Y =");map.Hud.Secondary=settings.SecondaryWgs&&currentCrs.Id!="EPSG:4326"?"WGS84: "+PointReadout.Wgs(p):"";map.Hud.HeightText=centerHeight.HasValue?"↑ ≈ "+settings.Height(centerHeight.Value)+" · DEM Z"+map.Elevation.LastSampleZoom:"↑ —";
            var selected=map.Selected as GeoPoint;map.Hud.TargetTitle="";map.Hud.TargetCoordinates="";map.Hud.TargetInfo="";
            if(selected!=null){double distance=GeoMath.DistanceMeters(p,selected);double? targetHeight=settings.Elevation?map.Elevation.AtAvailable(selected,map.Zoom):(double?)null;
                string delta=centerHeight.HasValue&&targetHeight.HasValue?((targetHeight.Value-centerHeight.Value)>=0?"+":"−")+settings.Height(Math.Abs(targetHeight.Value-centerHeight.Value)):"—";
                map.Hud.TargetTitle="ТОЧКА: "+(selected.Name??"Без названия")+" · от центра карты";
                map.Hud.TargetInfo="→ "+settings.Distance(distance)+"   "+(distance<0.05?"Направление —":PointReadout.Direction(PointReadout.Bearing(p,selected),settings.BearingFormat))+"   Δh "+delta;
                string targetKey=selected.Lat.ToString("R")+"|"+selected.Lon.ToString("R")+"|"+currentCrs.Id+"|"+settings.Format;string targetCoords=lastTargetText;if(targetKey!=lastTargetKey){try{targetCoords=FormatPoint(selected);}catch(Exception){targetCoords="Вне зоны / преобразование недоступно";}lastTargetKey=targetKey;lastTargetText=targetCoords;}map.Hud.TargetCoordinates=targetCoords+"  ↑ "+(targetHeight.HasValue?"≈ "+settings.Height(targetHeight.Value)+" DEM Z"+map.Elevation.LastSampleZoom:selected.Elevation.HasValue?settings.Height(selected.Elevation.Value)+" (файл)":"—");
            }
            string hudKey=map.Hud.Title+map.Hud.Primary+map.Hud.Secondary+map.Hud.HeightText+map.Hud.TargetTitle+map.Hud.TargetInfo+map.Hud.TargetCoordinates;if(hudKey!=lastHudKey){lastHudKey=hudKey;map.RequestFrame();}

        }
        System.Xml.Linq.XElement CapturePreferences(){var x=new System.Xml.Linq.XElement("preferences",new System.Xml.Linq.XElement("map",currentSource.Id),new System.Xml.Linq.XElement("language",currentSource.Language),new System.Xml.Linq.XElement("template",currentSource.Template),new System.Xml.Linq.XElement("crs",currentCrs.Id));x.Add(new System.Xml.Linq.XElement("lat",map.Center.Lat),new System.Xml.Linq.XElement("lon",map.Center.Lon),new System.Xml.Linq.XElement("zoom",map.Zoom));return x;}
        void SavePreferences(){AtomicSettings.SaveXml(Path.Combine(dataDir,"preferences.xml"),CapturePreferences());}
        void RestorePreferences(){try{string path=Path.Combine(dataDir,"preferences.xml");if(!File.Exists(path))return;var x=AtomicSettings.LoadXml(path);string template=(string)x.Element("template");var source=sources.Find(s=>s.Template==template);if(source!=null){source.Language=(string)x.Element("language")??"x-local";SetSource(source);}if(x.Element("lat")!=null&&x.Element("lon")!=null&&x.Element("zoom")!=null){double lat=(double)x.Element("lat"),lon=(double)x.Element("lon");int zoom=(int)x.Element("zoom");if(GeoMath.IsValidCoordinate(lat,lon)&&zoom>=1&&zoom<=MapCanvas.MaxDisplayZoom)map.GoTo(new GeoPoint(lat,lon),zoom);}string id=(string)x.Element("crs");if(!String.IsNullOrWhiteSpace(id)&&id!="EPSG:4326"){EnsureProjections();currentCrs=crsCatalog.Find(c=>c.Id==id&&(c.Id=="EPSG:4326"||(c.Sk42&&!c.Geographic&&c.Id.StartsWith("EPSG:284"))))??CrsEntry.Sk42Zone(crsCatalog,map.Center);}}catch(Exception e){Diagnostics.Record("Program.failure",e);SetMessage("Настройки координат не восстановлены: "+e.Message);}}
        void SafeUi(Action a) { if (IsDisposed || Disposing || !IsHandleCreated) return; try { BeginInvoke(a); } catch (InvalidOperationException) { } }
        void SetMessage(string text) { message.Text = text; }
        void SaveLayers(){map.ConfigureLayers(dataDir);try{new LayerConfiguration{Layers=map.Layers}.Save(Path.Combine(dataDir,"layers.xml"));settings.Save(Path.Combine(dataDir,"view-settings.xml"));}catch(Exception ex){Diagnostics.Record("Program.failure",ex);MessageBox.Show(this,ex.Message,"Не удалось сохранить слои");}}
        void SetMode(MapMode mode) { map.BeginMode(mode); map.Focus(); }
        void OpenFile() { using (var d = new OpenFileDialog { Filter = "Точки и треки|*.gpx;*.kml;*.kmz;*.ldk", Multiselect = true }) if (d.ShowDialog(this) == DialogResult.OK) foreach (var p in d.FileNames) Import(p); }
        void Import(string path) { try { var d = MapFiles.Load(path); var plan=new ImportPlan(map.Document,d); using(var dialog=new ImportReviewDialog(plan,Path.GetFileName(path))){if(dialog.ShowDialog(this)!=DialogResult.OK)return;if(map.IsEditing)map.EndEdit(true);plan=new ImportPlan(map.Document,d);int changed=plan.Apply(dialog.Policy);if(changed>0){MarkChanged();RefreshObjects();map.Fit();}SetMessage("Импорт: изменено объектов "+changed+" · "+Path.GetFileName(path));} } catch (Exception e) {Diagnostics.Record("Program.failure",e); MessageBox.Show(this, e.Message, "Не удалось открыть файл", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
        void SaveFile() { if(map.IsEditing)map.EndEdit(true); using (var d = new SaveFileDialog { Filter = "GPX|*.gpx|KML|*.kml", FileName = "Мои маршруты.gpx" }) if (d.ShowDialog(this) == DialogResult.OK) try { if (Path.GetExtension(d.FileName).ToLowerInvariant() == ".kml") MapFiles.SaveKml(map.Document, d.FileName); else MapFiles.SaveGpx(map.Document, d.FileName); SetMessage("Экспортировано: " + d.FileName); } catch (Exception e) {Diagnostics.Record("Program.failure",e); MessageBox.Show(this, e.Message, "Ошибка экспорта"); } }
        internal sealed class PointGroup { public string Name; public PointGroup(string name){Name=name;} }
        void FocusObject(object o) { var page=o as ObjectPage;if(page!=null){objectPages[page.Key]=page.Count+100;RefreshObjects();return;} var p = o as GeoPoint; var t = o as Track; if (p != null) map.GoTo(p, 15); else if (t != null) map.FitPoints(t.Points); else if(o is Zone)map.FitPoints(((Zone)o).Outline());else if(o is PointGroup)map.FitPoints(map.Document.Waypoints.Where(v=>(v.Group??"")==((PointGroup)o).Name)); }
        static IEnumerable<TreeNode> AllNodes(TreeNodeCollection nodes){foreach(TreeNode node in nodes){yield return node;foreach(TreeNode child in AllNodes(node.Nodes))yield return child;}}
        internal void PinRay(GeoPoint point){if(point==null||!map.Document.Waypoints.Contains(point)){SetMessage("Выберите метку для начала луча.");return;}point.Radio.Enabled=true;MarkChanged();settings.RayLat=point.Lat;settings.RayLon=point.Lon;settings.RayEnabled=true;rayButton.Checked=true;map.StartLiveRay(point,Path.Combine(dataDir,"elevation-cache"));map.RequestFrame();}
        void ManageGroups(){if(map.Document.Waypoints.Count==0){SetMessage("Сначала создайте или импортируйте метки.");return;}using(var d=new MarkerGroupsDialog(map.Document.Waypoints,map.Selected as GeoPoint))if(d.ShowDialog(this)==DialogResult.OK)AssignGroup(d.Chosen,d.GroupName);}
        internal void AssignGroup(IEnumerable<GeoPoint> points,string group){foreach(var point in points)if(map.Document.Waypoints.Contains(point))point.Group=(group??"").Trim();MarkChanged();RefreshObjects();}
        void SelectMapPoint(GeoPoint point){if(point!=null){EnsurePointPage(point);}if(point==null){objects.SelectedNode=null;map.Selected=null;return;}foreach(TreeNode node in AllNodes(objects.Nodes))if(Object.ReferenceEquals(node.Tag,point)){objects.SelectedNode=node;return;}objects.SelectedNode=null;map.Selected=point;}
        void EditSelectedMarker(){var point=map.Selected as GeoPoint;if(point==null)return;using(var d=new MarkerDialog(point,map.Document.Waypoints.Select(p=>p.Group),map,Path.Combine(dataDir,"elevation-cache")))if(d.ShowDialog(this)==DialogResult.OK){point.Radio=d.RadioOptions.Clone();if(d.StartRay)point.Radio.Enabled=true;ApplyMarkerChanges(point,d.MarkerName,d.ColorRgb,d.GroupName);if(d.StartRay){point.Radio.Enabled=true;PinRay(point);}else if(map.LiveRay!=null&&map.LiveRay.Anchor==point){if(point.Radio.Enabled)map.StartLiveRay(point,Path.Combine(dataDir,"elevation-cache"));else map.LiveRay.Stop();}}}
        internal void ApplyMarkerChanges(GeoPoint point,string name,int color,string group=null){if(!map.Document.Waypoints.Contains(point))return;point.Name=name;point.ColorRgb=color&0xFFFFFF;if(group!=null)point.Group=group.Trim();MarkChanged();RefreshObjects();}
        void ColorSelectedMarker(){var point=map.Selected as GeoPoint;if(point==null)return;using(var d=new ColorDialog{Color=Color.FromArgb(unchecked((int)0xFF000000)|point.ColorRgb),FullOpen=true})if(d.ShowDialog(this)==DialogResult.OK){ApplyMarkerChanges(point,point.Name,d.Color.ToArgb());}}
        void RenameSelected() { if (objects.SelectedNode == null || objects.SelectedNode.Tag == null) return; var p = objects.SelectedNode.Tag as GeoPoint; var t = objects.SelectedNode.Tag as Track; var z=objects.SelectedNode.Tag as Zone; if(p==null&&t==null&&z==null)return; var n = TextPrompt.Ask(this, "Название объекта", p != null ? p.Name : t!=null?t.Name:z.Name); if (n == null) return; if (p != null) p.Name = n; else if(t!=null)t.Name = n;else z.Name=n; MarkChanged(); RefreshObjects(); }
        void DeleteSelected() {
            if(objects.SelectedNode==null||objects.SelectedNode.Tag==null)return;
            var item=objects.SelectedNode.Tag;var group=item as PointGroup;
            string question=group==null?"Удалить выбранный объект из рабочего набора?":"Удалить группу «"+(String.IsNullOrEmpty(group.Name)?"Без группы":group.Name)+"» и все её точки ("+map.Document.Waypoints.Count(p=>(p.Group??"")==group.Name)+")?";
            if(MessageBox.Show(this,question,"Удаление",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            RemoveObject(item);
        }
        internal void RemoveGroup(string name){
            name=name??"";var removed=new HashSet<GeoPoint>(map.Document.Waypoints.Where(p=>(p.Group??"")==name));
            if(removed.Count==0)return;
            if(map.IsEditing)map.EndEdit(false);
            if(map.LiveRay!=null&&removed.Contains(map.LiveRay.Anchor))map.LiveRay.Stop();
            if(settings.RayEnabled&&removed.Any(p=>p.Lat==settings.RayLat&&p.Lon==settings.RayLon)){settings.RayEnabled=false;rayButton.Checked=false;}
            map.Document.Waypoints.RemoveAll(p=>removed.Contains(p));map.Selected=null;MarkChanged();RefreshObjects();
            SetMessage("Удалено точек: "+removed.Count);
        }
        internal void RemoveObject(object o){if(o==null)return;if(o is PointGroup){RemoveGroup(((PointGroup)o).Name);return;}if(map.IsEditing)map.EndEdit(false);if(o==recording){record.Checked=false;recording=null;}var point=o as GeoPoint;if(point!=null){map.Document.Waypoints.Remove(point);if(settings.RayEnabled&&point.Lat==settings.RayLat&&point.Lon==settings.RayLon){settings.RayEnabled=false;rayButton.Checked=false;}}else if(o is Track)map.Document.Tracks.Remove((Track)o);else if(o is Zone)map.Document.Zones.Remove((Zone)o);map.Selected=null;MarkChanged();RefreshObjects();}
        void ConnectSerial() { var ports = System.IO.Ports.SerialPort.GetPortNames(); var s = TextPrompt.Ask(this, "NMEA: порт и скорость (например, COM3 4800)", ports.Length > 0 ? ports[0] + " 4800" : "COM3 4800"); if (s == null) return; var a = s.Split(new[] { ' ', ';' }, StringSplitOptions.RemoveEmptyEntries); int baud; if (a.Length != 2 || !Int32.TryParse(a[1], out baud) || baud < 300 || baud > 921600) { MessageBox.Show(this, "Укажите COM-порт и скорость, например COM3 4800."); return; } try { gps.StartSerial(a[0], baud); } catch (Exception e) {Diagnostics.Record("Program.failure",e); MessageBox.Show(this, e.Message, "Ошибка подключения GPS"); } }
        void OnFix(GpsFixEventArgs e) { map.Fix = e.Point;if(settings.FollowGps)map.GoTo(e.Point,map.Zoom); lastFix = DateTime.UtcNow; gpsLabel.Text = gps.Status; map.RequestFrame(); if (!record.Checked) return; if(map.IsEditing&&map.Selected==recording){record.Checked=false;recording=null;SetMessage("Запись остановлена для редактирования трека.");return;} if (recording == null) { recording = new Track { Name = "GPS " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }; map.Document.Tracks.Add(recording); } if (recording.Points.Count == 0 || GeoMath.DistanceMeters(recording.Points[recording.Points.Count - 1], e.Point) >= settings.RecordStep) { recording.Points.Add(e.Point); MarkChanged(); RefreshObjects(); } }
        void StopGps() { gps.Stop(); record.Checked = false; recording = null; map.Fix = null; map.RequestFrame(); }
        void Restore() {try {restoring=true;bool recovered;map.Document=WorkspaceStore.Load(dataDir,out recovered);RefreshObjects();map.Fit();dirty=recovered;saveStatus.Text=recovered?"Восстановлено из резервной копии":"Сохранено";SetMessage(recovered?"Основной файл повреждён. Восстановлена резервная копия.":"Рабочий набор восстановлен.");}catch(Exception ex){Diagnostics.Record("Program.failure",ex);workspaceLoadFailed=true;saveStatus.Text="Автосохранение отключено";MessageBox.Show(this,"Не удалось восстановить рабочий набор. Автосохранение отключено, исходные файлы сохранены. Новую работу можно экспортировать вручную.\n"+ex.Message);}finally{restoring=false;}}
        bool closingInProgress,closeReady;
        async void SaveOnClosing(object sender, FormClosingEventArgs e) {
            if(previewMode||closeReady)return;
            e.Cancel=true;if(closingInProgress)return;
            if(map.HasDraft&&MessageBox.Show(this,"Незавершённое построение или правка не будут сохранены. Закрыть?","Маршрут",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
            if(map.IsEditing)map.EndEdit(false);
            closingInProgress=true;Enabled=false;map.Visible=false;autosaveTimer.Stop();centerTimer.Stop();fixTimer.Stop();cacheTimer.Stop();StopGps();
            if(visibilityForm!=null)visibilityForm.Close();if(routeDetails!=null&&!routeDetails.IsDisposed)routeDetails.Close();
            if(cardElevation!=null){cardElevation.Tiles.OnlineEnabled=false;cardElevation.Tiles.DiscardQueued();}map.CancelBackgroundForClose();
            try{
                await BackgroundJobs.Drain();
                if(saveTask!=null){try{await saveTask;}catch(Exception ex){Diagnostics.Record("workspace.pending.save",ex);}FinishSave();}
                if(workspaceLoadFailed&&dirty)throw new IOException("Рабочий набор не восстановлен. Экспортируйте новые данные вручную.");
                var snapshot=history.Current;var preferences=CapturePreferences();var view=settings.Clone();
                await System.Threading.Tasks.Task.Run(()=>{if(!restoring&&dirty)WorkspaceStore.Save(snapshot,dataDir);AtomicSettings.SaveXml(Path.Combine(dataDir,"preferences.xml"),preferences);view.Save(Path.Combine(dataDir,"view-settings.xml"));});
                dirty=false;closeReady=true;Close();
            }catch(Exception ex){Diagnostics.Record("Program.failure",ex);Enabled=true;map.Visible=true;map.ConfigureLayers(dataDir);closingInProgress=false;autosaveTimer.Start();centerTimer.Start();fixTimer.Start();cacheTimer.Start();saveStatus.Text="Не сохранено";Diagnostics.Show(this,"application.close.save",ex);}
        }
        void ShowHelp() { MessageBox.Show(this, "Правка: выберите маршрут или зону → «Правка» (E). Перетаскивайте вершины; середины рёбер добавляют вершину. Delete удаляет выбранную вершину. Ctrl+Z — назад, Enter — применить, Esc — отменить правку. Правой кнопкой доступны продолжение/разворот маршрута и масштабирование зоны. Зона → многоугольник по щелчкам или фигура протягиванием мыши.\n\nMapDesk 0.40.4 — самостоятельный Windows-прототип выбранных функций AlpineQuest.\n\nGPX/KML/KMZ: импорт. GPX/KML: экспорт. Точки, линии маршрутов, просмотр и запись треков. Маршрут рисуется отрезками: автоматического построения по дорогам пока нет.\n\nЛевая кнопка — перемещение; колесо и +/- — масштаб. Стрелки — перемещение; Home — все объекты. W — новая точка; R — новый маршрут; Enter — закончить; Esc — отменить; Ctrl+Z — убрать последнюю вершину. Ctrl+O — открыть; Ctrl+S — экспорт; Ctrl+L — координаты.\n\nКарты: Esri World Imagery, Google Maps Localized, Satellite, Hybrid, OpenStreetMap и Esri Topographic. Импорт совместимых AQX. Полный серверный каталог AlpineQuest недоступен. Сохранение просмотренных фрагментов регулируется настройками кэша. По умолчанию онлайн загружает только отсутствующие или повреждённые фрагменты.\n\nКоординаты: Ctrl+L, выбор СК42 или одного из трёх форматов WGS84. Для перехода введите X/Y и нажмите «Перейти к координатам» или Enter; пробелы в числах допустимы. СК-42: X — север, Y — восток; выбирайте правильную зону. PROJ поддерживает доступные горизонтальные 2D-преобразования; некоторые требуют дополнительных сеток поправок. Геолокация: источник Windows (может быть Wi-Fi) либо NMEA через COM-порт. Без источника GPS-координаты не имитируются.\n\nРабочий набор сохраняется при закрытии в %LOCALAPPDATA%\\MapDeskExperimental038.\n\nВерсия 0.17: Shift + колесо — поворот; N — север вверх. Правая кнопка по метке → «Закрепить начало луча»: линия от этой метки к центру карты; кнопка «Луч» снимает фиксацию. Кнопка «Кольца» — круговая линейка от центра карты. Шаг в настройках: 0 — автоматически, 50000 — кольца 50/100/150 км; стрелка показывает истинный север. «Группы…» переносит выбранные метки в группу. Приближение разрешено сверх уровня источника до Z40; для заглушки Map data not yet available уменьшите детализацию источника в настройках. Правая кнопка по метке — название, цвет и удаление. F2 — переименовать, Delete — удалить выбранный объект. Кнопка «Линейка» включает отдельную масштабную линейку. GPU-ускорение растра включено по умолчанию; адаптер показан на карте, при ошибке используется CPU. Координаты центра обновляются слева сверху. Зона СК-42 выбирается через «Координаты…». Матрица DEM используется для расчётов; цветной слой можно добавить отдельно через «Слои…». Для загрузки включите «Высота DEM» и «Онлайн-карта». Перспектива — наклон плоскости карты, не объёмная модель гор. Настройки: единицы, формат координат, вид карты, матрица высот и GPS. Источники высот и их атрибуция: licenses/Terrain-data-attribution.md.\n\nЭто не полная пересборка AlpineQuest. AQM/PGD, компас, барометр и другие функции оригинала пока не перенесены.", "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            if(keyData==Keys.F11||(keyData==Keys.Escape&&mapFullscreen)){ToggleMapFullscreen();return true;}
            if(objectSearch.Focused)return base.ProcessCmdKey(ref msg,keyData);
            if(keyData==(Keys.Control|Keys.F)){if(mapFullscreen)ToggleMapFullscreen();SetSidebarCollapsed(false);objectSearch.Focus();objectSearch.SelectAll();return true;}
            if(keyData==(Keys.Control|Keys.Z)){HistoryMove(false);return true;}
            if(keyData==(Keys.Control|Keys.Y)||keyData==(Keys.Control|Keys.Shift|Keys.Z)){HistoryMove(true);return true;}
            if (keyData == (Keys.Control | Keys.O)) { OpenFile(); return true; }
            if (keyData == (Keys.Control | Keys.S)) { SaveFile(); return true; }
            if (keyData == (Keys.Control | Keys.L)) { ChooseCoordinates(); return true; }
            if ((map.Focused||objects.Focused)&&keyData==Keys.F2){RenameSelected();return true;}
            if ((map.Focused||objects.Focused)&&keyData==Keys.Delete){if(map.IsEditing)map.DeleteVertex();else DeleteSelected();return true;}
            if((map.Focused||objects.Focused)&&keyData==Keys.E){map.BeginEdit();map.Focus();return true;}
            if (keyData == Keys.F1) { ShowHelp(); return true; }
            if (keyData == Keys.W) { SetMode(MapMode.Waypoint); return true; }
            if (keyData == Keys.R) { SetMode(MapMode.Route); return true; }
            if (keyData == Keys.Escape) { map.CancelDraft(); return true; }
            if (keyData == Keys.Enter && map.HasDraft) { map.FinishRoute(); return true; }
            if (keyData == (Keys.Control | Keys.Z) && map.HasDraft) { map.Undo(); return true; }
            if (keyData == Keys.Home) { map.Fit(); return true; }
            if (map.Focused) {
                if(keyData==Keys.N){settings.Rotation=0;map.RequestFrame();return true;}
                if (keyData == Keys.Add || keyData == Keys.Oemplus) { map.ChangeZoom(1, new Point(map.Width / 2, map.Height / 2)); return true; }
                if (keyData == Keys.Subtract || keyData == Keys.OemMinus) { map.ChangeZoom(-1, new Point(map.Width / 2, map.Height / 2)); return true; }
                if (keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down) { map.Pan(keyData == Keys.Left ? -100 : keyData == Keys.Right ? 100 : 0, keyData == Keys.Up ? -100 : keyData == Keys.Down ? 100 : 0); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    enum MapMode { Pan, Waypoint, Route, Polygon, Rectangle, Circle, Edit }

    sealed partial class MapCanvas : Control
    {
        internal MapMode CurrentMode {get{return mode;}}
        public TileStore Tiles;
        public ElevationStore Elevation;
        public readonly MapHud Hud=new MapHud();
        bool hudPress;
        public ViewSettings Settings=new ViewSettings();
        readonly PerspectiveRaster perspectiveRaster=new PerspectiveRaster();
        Bitmap planeBuffer;
        readonly Dictionary<string,Image> fallbackImages=new Dictionary<string,Image>();
        readonly HashSet<string> missingFallbacks=new HashSet<string>();
        readonly FramePump framePump;
        public MapDocument Document = new MapDocument();
        public readonly HashSet<string> HiddenGroups=new HashSet<string>(StringComparer.Ordinal);
        public bool IsPointVisible(GeoPoint point){return !HiddenGroups.Contains(point.Group??"");}
        public GeoPoint Fix;
        object selectedObject;public object Selected {get{return selectedObject;}set{if(!Object.ReferenceEquals(selectedObject,value))ClearTemporaryPoint();selectedObject=value;}}
        public event Action<GeoPoint> CoordinateCopyRequested;
        public event Action CoordinateSearchRequested;
        public event Action<GeoPoint> PointSelected;
        public event Action<GeoPoint,string> MarkerAction;
        public event EventHandler DocumentChanged;
        public event Action<string> Message;
        public event Action<GeoPoint> PointerMoved;
        public event Action<GeoPoint> ReadoutRequested;
        public const int MaxDisplayZoom=40;
        public int Zoom = 3;
        internal int SourceZoom {get{return Math.Max(Tiles.Source.MinZoom,Math.Min(Zoom,Tiles.Source.MaxZoom)-Settings.SourceZoomOffset);}}
        public GeoPoint Center {get{return new GeoPoint(GeoMath.PixelToLat(centerY,Zoom),WrapLongitude(GeoMath.PixelToLon(centerX,Zoom)));}}
        double centerX = 1024, centerY = 800;
        Point dragStart, lastMouse;
        double dragX, dragY;
        bool dragging, moved;
        MapMode mode;
        readonly List<GeoPoint> draft = new List<GeoPoint>();
        public bool HasDraft { get { return draft.Count > 0 || IsEditing; } }
        static readonly Color green = Color.FromArgb(23, 107, 80);

        public MapCanvas(TileStore tiles) {
            SetTileStore(tiles); DoubleBuffered = true; SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw, true); TabStop = true;
            BackColor = Color.FromArgb(225, 233, 231); Cursor = Cursors.Hand;
            var menu = new ContextMenuStrip();
            var edit=menu.Items.Add("Изменить метку…",null,delegate{RunMarkerAction("edit");});
            var rename=menu.Items.Add("Переименовать…",null,delegate{RunMarkerAction("rename");});
            var color=menu.Items.Add("Цвет метки…",null,delegate{RunMarkerAction("color");});
            var delete=menu.Items.Add("Удалить метку",null,delegate{RunMarkerAction("delete");});var ray=menu.Items.Add("Закрепить начало луча",null,delegate{RunMarkerAction("ray");});var separator=new ToolStripSeparator();menu.Items.Add(separator);
            menu.Items.Add("Перейти к выбранной точке",null,delegate{var point=Selected as GeoPoint;if(point!=null)GoTo(point,Zoom);});
            menu.Items.Add("Добавить точку здесь", null, delegate { AddWaypoint(FromScreen(lastMouse)); });
            menu.Items.Add("Центрировать здесь", null, delegate { GoTo(FromScreen(lastMouse), Zoom); });
            menu.Items.Add("Копировать координаты…", null, delegate { if(CoordinateCopyRequested!=null)CoordinateCopyRequested(HitMarker(lastMouse)??FromScreen(lastMouse)); });
            menu.Opening+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(HitOverlay(lastMouse)){e.Cancel=true;return;}var point=IsEditing?null:HitMarker(lastMouse);edit.Visible=rename.Visible=color.Visible=delete.Visible=ray.Visible=separator.Visible=point!=null;if(point!=null&&!IsEditing){Selected=point;if(PointSelected!=null)PointSelected(point);RequestFrame();}};
            SetupEditorMenu(menu);
            ContextMenuStrip = menu;
            framePump=new FramePump(this,delegate{Refresh();});
        }
        public void RequestFrame(){if(framePump!=null){framePump.Limit=Settings.MaxFps;framePump.Request();}}
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);RequestFrame();}
        public void SetTileStore(TileStore store){Tiles=store;store.Changed+=delegate{RequestFrame();};RequestFrame();}
        GeoPoint HitMarker(Point location){
            // Reverse paint order: the first match is the topmost marker.
            for(int i=Document.Waypoints.Count-1;i>=0;i--){var p=Document.Waypoints[i];if(!IsPointVisible(p))continue;var q=ToScreen(p);if(Math.Abs(q.X-location.X)<14&&Math.Abs(q.Y-location.Y)<14)return p;}
            return null;
        }
        sealed class MarkerSprite { public Bitmap Image; public long Bytes; public string Key; }
        readonly Dictionary<string,LinkedListNode<MarkerSprite>> markerSprites=new Dictionary<string,LinkedListNode<MarkerSprite>>();
        readonly LinkedList<MarkerSprite> markerLru=new LinkedList<MarkerSprite>();
        long markerSpriteBytes;
        Font markerSpriteFont;
        void ClearMarkerSprites(){foreach(var sprite in markerLru)sprite.Image.Dispose();markerLru.Clear();markerSprites.Clear();markerSpriteBytes=0;}
        Bitmap MarkerImage(Graphics target,GeoPoint point){
            if(!Object.Equals(markerSpriteFont,Font)){ClearMarkerSprites();markerSpriteFont=Font;}
            string name=Settings.PointNames?(point.Name??""):"";
            string key=point.ColorRgb.ToString("X6")+":"+name;
            LinkedListNode<MarkerSprite> node;
            if(markerSprites.TryGetValue(key,out node)){markerLru.Remove(node);markerLru.AddLast(node);return node.Value.Image;}
            var size=name.Length==0?SizeF.Empty:target.MeasureString(name,Font);
            // Cap pathological imported names; normal labels retain their full width.
            int width=name.Length==0?16:Math.Min(4096,Math.Max(16,(int)Math.Ceiling(size.Width)+21));
            int height=Math.Max(24,(int)Math.Ceiling(size.Height)+2);
            var bitmap=new Bitmap(width,height,PixelFormat.Format32bppPArgb);
            using(var g=Graphics.FromImage(bitmap)){
                g.SmoothingMode=SmoothingMode.AntiAlias;
                using(var brush=new SolidBrush(Color.FromArgb(unchecked((int)0xFF000000)|point.ColorRgb)))g.FillEllipse(brush,2,6,12,12);
                g.DrawEllipse(Pens.White,2,6,12,12);
                if(name.Length!=0){g.FillRectangle(Brushes.White,16,1,size.Width+5,size.Height);g.DrawString(name,Font,Brushes.Black,18,1);}
            }
            long bytes=(long)width*height*4;
            while(markerLru.Count>0&&(markerSpriteBytes+bytes>32*1024*1024||markerLru.Count>=4096)){
                var old=markerLru.First.Value;markerSpriteBytes-=old.Bytes;markerSprites.Remove(old.Key);markerLru.RemoveFirst();old.Image.Dispose();
            }
            var sprite=new MarkerSprite{Image=bitmap,Bytes=bytes,Key=key};markerSprites.Add(key,markerLru.AddLast(sprite));markerSpriteBytes+=bytes;return bitmap;
        }
        void DrawCachedMarkers(Graphics g){
            foreach(var point in Document.Waypoints){if(!IsPointVisible(point))continue;var s=ToScreen(point);if(!VisiblePoint(s))continue;
                g.DrawImageUnscaled(MarkerImage(g,point),(int)Math.Round(s.X)-8,(int)Math.Round(s.Y)-12);
                if(point==Selected)using(var ring=new Pen(Color.DarkOrange,2))g.DrawEllipse(ring,s.X-10,s.Y-10,20,20);
            }
        }
        void RunMarkerAction(string action){var point=Selected as GeoPoint;if(point!=null&&Document.Waypoints.Contains(point)&&MarkerAction!=null)MarkerAction(point,action);}
        RectangleF scaleBounds;
        bool HitOverlay(Point point){return (LiveRay!=null&&LiveRay.Hit(point))||(Settings.DataCards&&Hud.Contains(point))||(Settings.ScaleBar&&scaleBounds.Contains(point));}
        void Say(string s) { if (Message != null) Message(s); }
        void Changed() { if (DocumentChanged != null) DocumentChanged(this, EventArgs.Empty); RequestFrame(); }
        public void BeginMode(MapMode value) { CancelRegion(); if(ProfilePick!=null){Say("Сначала выберите точку профиля или нажмите Esc.");return;} if (HasDraft) { Say("Сначала завершите маршрут (Enter) или отмените (Esc)."); return; } mode = value; Cursor = value == MapMode.Pan ? Cursors.Hand : Cursors.Cross; Say(value == MapMode.Route ? "Маршрут: щёлкайте по карте. Enter — завершить, Ctrl+Z — назад, Esc — отмена. Линии соединяются напрямую." : value==MapMode.Polygon?"Зона: щёлкайте вершины, Enter — закончить, Esc — отменить.": value==MapMode.Rectangle||value==MapMode.Circle?"Зона: нажмите и протяните мышь. Enter — сохранить, Esc — отменить.": "Щёлкните по карте, чтобы создать точку."); }
        public void CancelDraft() { if(CancelRegion())return; if(ProfilePick!=null){var pick=ProfilePick;ProfilePick=null;pick(null);RequestFrame();return;} shapeDrag=false;dragging=false;Capture=false; if(IsEditing)EndEdit(false); draft.Clear(); mode = MapMode.Pan; Cursor = Cursors.Hand; RequestFrame(); Say("Режим перемещения."); }
        public void Undo() { if(shapeDrag){shapeDrag=false;Capture=false;} if(IsEditing){UndoEdit();return;} if (draft.Count > 0) draft.RemoveAt(draft.Count - 1); RequestFrame(); }
        public void FinishRoute() { if(IsEditing){EndEdit(true);return;} if(mode==MapMode.Polygon||mode==MapMode.Circle||mode==MapMode.Rectangle){FinishZone();return;} if (draft.Count < 2) { Say("Для маршрута нужны как минимум две точки."); return; } var name = TextPrompt.Ask(FindForm(), "Название маршрута", "Маршрут " + (Document.Tracks.Count + 1)); if (name == null) return; var t = new Track { Name = name, IsRoute = true }; t.Points.AddRange(draft); Document.Tracks.Add(t); CancelDraft(); Selected=t;Changed(); NotifyRoute(); Say("Маршрут создан · " + (GeoMath.TrackLengthMeters(t) / 1000).ToString("F2") + " км"); }
        void AddWaypoint(GeoPoint p) { var name = TextPrompt.Ask(FindForm(), "Название точки", "Точка " + (Document.Waypoints.Count + 1)); if (name == null) return; p.Name = name; Document.Waypoints.Add(p); if (!HasDraft) CancelDraft(); Changed(); Say("Точка добавлена."); }
        public void GoTo(GeoPoint p, int zoom) { Zoom = Math.Max(Tiles.Source.MinZoom, Math.Min(MaxDisplayZoom, zoom)); centerX = GeoMath.LonToPixel(p.Lon, Zoom); centerY = GeoMath.LatToPixel(p.Lat, Zoom); RequestFrame(); }
        public void Pan(int x, int y) {var d=PerspectiveMath.Rotate(x,y,-Settings.Rotation);centerX += d.X; centerY += d.Y; Normalize(); RequestFrame(); }
        void Normalize() { var size = GeoMath.WorldSize(Zoom); centerX = ((centerX % size) + size) % size; centerY = Math.Max(0, Math.Min(size, centerY)); }
        PointF Plane(Point p){var q=PerspectiveMath.Inverse(p.X-Width/2.0,p.Y-Height/2.0,Math.Max(1,Height),Settings.Perspective?Settings.Tilt:0);return PerspectiveMath.Rotate(q.X,q.Y,-Settings.Rotation);}
        PointF Project(double x,double y){var r=PerspectiveMath.Rotate(x-Width/2.0,y-Height/2.0,Settings.Rotation);var p=PerspectiveMath.Forward(r.X,r.Y,Math.Max(1,Height),Settings.Perspective?Settings.Tilt:0);return new PointF(p.X+Width/2f,p.Y+Height/2f);}
        public void ChangeZoom(int delta, Point anchor) { int next = Math.Max(Tiles.Source.MinZoom, Math.Min(MaxDisplayZoom, Zoom + delta)); if (next == Zoom) return;double factor=Math.Pow(2,next-Zoom);var a=Plane(anchor);centerX=(centerX+a.X)*factor-a.X;centerY=(centerY+a.Y)*factor-a.Y;Zoom=next;Normalize();RequestFrame(); }
        GeoPoint FromScreen(Point p) {var a=Plane(p);return new GeoPoint(GeoMath.PixelToLat(Math.Max(0,Math.Min(GeoMath.WorldSize(Zoom),centerY+a.Y)),Zoom),WrapLongitude(GeoMath.PixelToLon(centerX+a.X,Zoom)));}
        static double WrapLongitude(double lon) { return ((lon + 180) % 360 + 360) % 360 - 180; }
        PointF ToPlane(GeoPoint p) { double size = GeoMath.WorldSize(Zoom), dx = GeoMath.LonToPixel(p.Lon, Zoom) - centerX; if (dx > size / 2) dx -= size; if (dx < -size / 2) dx += size; return new PointF((float)(Width / 2.0 + dx), (float)(Height / 2.0 + GeoMath.LatToPixel(p.Lat, Zoom) - centerY)); }
        PointF ToScreen(GeoPoint p){var a=ToPlane(p);return Project(a.X,a.Y);}
        public void Fit() { FitPoints(Document.Waypoints.Where(IsPointVisible).Concat(Document.Tracks.SelectMany(t => t.Points)).Concat(Document.Zones.SelectMany(z=>z.Outline()))); }
        public void FitPoints(IEnumerable<GeoPoint> source) {
            var points = source.ToList(); if (points.Count == 0) { Say("Нет объектов для обзора."); return; }
            var xs = points.Select(p => GeoMath.LonToPixel(p.Lon, 0)).OrderBy(x => x).ToList(); double gap = -1, start = xs[0];
            for (int i = 0; i < xs.Count; i++) { double a = xs[i], b = i + 1 < xs.Count ? xs[i + 1] : xs[0] + 256; if (b - a > gap) { gap = b - a; start = b % 256; } }
            double span = 256 - gap, minY = points.Min(p => GeoMath.LatToPixel(p.Lat, 0)), maxY = points.Max(p => GeoMath.LatToPixel(p.Lat, 0));
            int z=Math.Max(Tiles.Source.MinZoom,Math.Min(16,Tiles.Source.MaxZoom));
            while(z>Tiles.Source.MinZoom){double factor=Math.Pow(2,z),halfX=span*factor/2,halfY=(maxY-minY)*factor/2;bool fits=true;foreach(double xx in new[]{-halfX,halfX})foreach(double yy in new[]{-halfY,halfY}){var r=PerspectiveMath.Rotate(xx,yy,Settings.Rotation);var q=PerspectiveMath.Forward(r.X,r.Y,Math.Max(1,Height),Settings.Perspective?Settings.Tilt:0);if(Single.IsNaN(q.X)||Single.IsNaN(q.Y)||Math.Abs(q.X)>Math.Max(50,(Width-140)/2.0)||Math.Abs(q.Y)>Math.Max(50,(Height-140)/2.0))fits=false;}if(fits)break;z--;}

            Zoom = z; centerX = ((start + span / 2) % 256) * Math.Pow(2, z); centerY = (minY + maxY) / 2 * Math.Pow(2, z); Normalize(); RequestFrame();
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if(e.Button==MouseButtons.Right&&LiveRay!=null&&LiveRay.ContextGraph(e.Location))return; if(RegionDown(e))return; Focus(); lastMouse = e.Location;if(e.Button==MouseButtons.Left&&Settings.DataCards&&Hud.SearchBounds.Contains(e.Location)){hudPress=true;if(CoordinateSearchRequested!=null)CoordinateSearchRequested();return;}if(e.Button==MouseButtons.Left&&Settings.DataCards&&Hud.CoordinateBounds.Contains(e.Location)){hudPress=true;if(CoordinateCopyRequested!=null)CoordinateCopyRequested(Center);return;}if(e.Button==MouseButtons.Left&&LiveRay!=null&&LiveRay.Press(e.Location)){hudPress=true;return;}hudPress=HitOverlay(e.Location);if(hudPress)return; if(ProfilePick==null&&EditorDown(e))return; if (e.Button == MouseButtons.Left) { dragging = true; moved = false; dragStart = e.Location; dragX = centerX; dragY = centerY; Capture = true; } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if(LiveRay!=null)LiveRay.MoveProbe(e.Location); if(RegionMove(e))return; lastMouse = e.Location; if(EditorMove(e))return; if (dragging) { if (Math.Abs(e.X - dragStart.X) + Math.Abs(e.Y - dragStart.Y) > 5) moved = true; if (moved) { var a=Plane(e.Location);var b=Plane(dragStart);centerX = dragX - (a.X-b.X); centerY = dragY - (a.Y-b.Y); Normalize(); RequestFrame(); } } if (PointerMoved != null) PointerMoved(FromScreen(e.Location)); }
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);if(LiveRay!=null)LiveRay.MoveProbe(null);}
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e);if(RegionUp(e))return;if(hudPress){hudPress=false;return;} if (e.Button != MouseButtons.Left) return; if(EditorUp(e))return; dragging = false; Capture = false; if (moved) return; if(ProfilePick!=null){var pick=ProfilePick;ProfilePick=null;pick(FromScreen(e.Location));RequestFrame();return;} if (mode == MapMode.Waypoint) AddWaypoint(FromScreen(e.Location)); else if (mode == MapMode.Route||mode==MapMode.Polygon) { draft.Add(FromScreen(e.Location)); RequestFrame(); } else {var hit=HitMarker(e.Location);if(PointSelected!=null)PointSelected(hit);Selected=(object)hit??HitGeometry(e.Location);if(hit==null&&Selected!=null){Changed();NotifyRoute();}RequestFrame();} }
        internal void Wheel(Point location,int delta,bool shift){if(delta==0||regionDragging)return;if(LiveRay!=null&&LiveRay.Wheel(location,delta))return;if(HitOverlay(location))return;if(shift){Settings.Rotation=((Settings.Rotation+delta/120.0*15)%360+360)%360;RequestFrame();}else ChangeZoom(delta>0?1:-1,location);}
        protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);Wheel(e.Location,e.Delta,(ModifierKeys&Keys.Shift)!=0);}
        GpuPerspective mapGpu;bool mapGpuFailed,mapGpuDrawing;string gpuMessage="CPU";
        internal string RenderBackend {get{return gpuMessage;}}
        long fpsWindowStart;int fpsFrameCount;double measuredFps;
        string FpsText {get{return "Последний замер: "+(measuredFps>0?measuredFps.ToString("F0")+" кадр/с":"—")+" · предел "+Settings.MaxFps;}}
        void CountFrame(){long now=System.Diagnostics.Stopwatch.GetTimestamp();if(fpsWindowStart==0){fpsWindowStart=now;fpsFrameCount=0;return;}double elapsed=(now-fpsWindowStart)/(double)System.Diagnostics.Stopwatch.Frequency;if(elapsed>1){fpsWindowStart=now;fpsFrameCount=0;return;}fpsFrameCount++;if(elapsed>=0.25){measuredFps=fpsFrameCount/elapsed;fpsWindowStart=now;fpsFrameCount=0;}}

        void PaintBaseTiles(Graphics g,double minX,double maxX,double minY,double maxY,double left,double top,long n){
            int sourceZoom=SourceZoom;
            Tiles.BeginFrame();try{
            for(long y=(long)Math.Floor(minY/256);y<=(long)Math.Floor(maxY/256);y++){
                if(y<0||y>=n)continue;
                for(long x=(long)Math.Floor(minX/256);x<=(long)Math.Floor(maxX/256);x++){
                    long tx=((x%n)+n)%n;var rect=new RectangleF((float)(x*256.0-left),(float)(y*256.0-top),257,257);
                    bool drawn=false;
                    for(int level=sourceZoom;level>=Math.Max(Tiles.Source.MinZoom,sourceZoom-4);level--){
                        long divisor=1L<<(Zoom-level);int sx=(int)(tx/divisor),sy=(int)(y/divisor);
                        using(var image=level>=sourceZoom-1?Tiles.GetTile(level,sx,sy):Tiles.GetCachedTile(level,sx,sy)){
                            if(image==null)continue;
                            DrawTileImage(g,image,rect,new RectangleF((float)((tx%divisor)*image.Width/(double)divisor),(float)((y%divisor)*image.Height/(double)divisor),(float)(image.Width/(double)divisor),(float)(image.Height/(double)divisor)),null);drawn=true;break;
                        }
                    }
                    if(!drawn)DrawFallback(g,Zoom,tx,y,rect);
                }
            }}finally{Tiles.EndFrame();}
        }
        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics; double left = centerX - Width / 2.0, top = centerY - Height / 2.0; long n = 1L << Zoom;
            var corners=new[]{Plane(new Point(0,0)),Plane(new Point(Width,0)),Plane(new Point(0,Height)),Plane(new Point(Width,Height))};
            double minX=centerX+corners.Min(q=>q.X),maxX=centerX+corners.Max(q=>q.X),minY=centerY+corners.Min(q=>q.Y),maxY=centerY+corners.Max(q=>q.Y);
            bool gpuDrawn=false;
            if(Settings.Gpu&&!mapGpuFailed&&Width>0&&Height>0)try{
                if(mapGpu==null)mapGpu=new GpuPerspective();mapGpuDrawing=true;
                mapGpu.DrawScene(g,Width,Height,Settings.Perspective?Settings.Tilt:0,delegate{PaintTiles(g,minX,maxX,minY,maxY,left,top,n);},Settings.Rotation);gpuDrawn=true;gpuMessage="GPU · "+mapGpu.Renderer;
            }catch(Exception ex){Diagnostics.Record("Program.failure",ex);mapGpuFailed=true;gpuMessage="CPU · GPU недоступен: "+ex.Message;if(mapGpu!=null){mapGpu.Dispose();mapGpu=null;}}
            finally{mapGpuDrawing=false;}
            if(!gpuDrawn){
                if(!Settings.Gpu)gpuMessage="CPU";
                Bitmap plane=null;
                if((Settings.Perspective||Settings.Rotation!=0)&&Width>0&&Height>0){int pw=Math.Max(2,(int)Math.Ceiling(maxX-minX)+2),ph=Math.Max(2,(int)Math.Ceiling(maxY-minY)+2);if(planeBuffer==null||planeBuffer.Width!=pw||planeBuffer.Height!=ph){if(planeBuffer!=null)planeBuffer.Dispose();planeBuffer=new Bitmap(pw,ph,PixelFormat.Format32bppArgb);}plane=planeBuffer;g=Graphics.FromImage(plane);g.Clear(BackColor);g.TranslateTransform((float)(left-minX),(float)(top-minY));}
                g.InterpolationMode=InterpolationMode.HighQualityBilinear;PaintTiles(g,minX,maxX,minY,maxY,left,top,n);
                if(plane!=null){g.Dispose();g=e.Graphics;perspectiveRaster.Draw(g,plane,Width,Height,minX-centerX,minY-centerY,Settings.Perspective?Settings.Tilt:0,Settings.Rotation);}
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if(Settings.RangeRings)RangeRings.Draw(g,Width,Height,Center,Zoom,Settings,ToScreen,Font);
            DrawVisibility(g);DrawZones(g);
            foreach (var t in Document.Tracks) DrawTrack(g, t.Points, t == Selected ? Color.FromArgb(222, 119, 36) : green, t == Selected ? Settings.LineWidth+2 : Settings.LineWidth);
            if(Settings.RayEnabled){var origin=new GeoPoint(Settings.RayLat,Settings.RayLon);var ray=new[]{origin,Center};DrawTrack(g,ray,Color.White,5);DrawTrack(g,ray,Color.DodgerBlue,2);var marker=ToScreen(origin);if(VisiblePoint(marker)){g.FillEllipse(Brushes.DodgerBlue,marker.X-8,marker.Y-8,16,16);g.DrawEllipse(Pens.White,marker.X-8,marker.Y-8,16,16);}}
            DrawCachedMarkers(g);DrawTemporaryPoint(g);DrawCachedCoverage(g);
            DrawPointPick(g);
            DrawEditor(g);
            DrawTrack(g, draft, Color.FromArgb(218, 118, 32), 3);
            foreach (var p in draft) { var s = ToScreen(p);if(!VisiblePoint(s))continue; g.FillEllipse(Brushes.DarkOrange, s.X - 4, s.Y - 4, 8, 8); }
            if (Fix != null) { var s = ToScreen(Fix);if(VisiblePoint(s)){ using (var b = new SolidBrush(Color.FromArgb(50, 40, 115, 228))) g.FillEllipse(b, s.X - 15, s.Y - 15, 30, 30); g.FillEllipse(Brushes.DodgerBlue, s.X - 6, s.Y - 6, 12, 12); g.DrawEllipse(Pens.White, s.X - 6, s.Y - 6, 12, 12); } }
            if(Settings.Crosshair){int cx=Width/2,cy=Height/2;using(var p=new Pen(Color.White,4)){g.DrawLine(p,cx-11,cy,cx+11,cy);g.DrawLine(p,cx,cy-11,cx,cy+11);}g.DrawLine(Pens.Black,cx-11,cy,cx+11,cy);g.DrawLine(Pens.Black,cx,cy-11,cx,cy+11);}
            int noticeY=16;
            if(ReadoutRequested!=null)ReadoutRequested(Center);
            if(Settings.DataCards)noticeY=(int)Hud.Draw(g,Width,Height,Center,Zoom,Tiles.Source.MaxZoom,Settings)+8;else Hud.ClearBounds();
            if(Settings.Diagnostics){
            DrawLabel(g,(Tiles.OnlineEnabled?Tiles.Source.Name:"ОФЛАЙН · "+Tiles.Source.Name)+" · Z"+Zoom+(Zoom>SourceZoom?" · увеличение снимка ×"+Math.Pow(2,Zoom-SourceZoom).ToString("N0"):""),16,noticeY);noticeY+=29;
            DrawLabel(g,gpuMessage,16,noticeY);noticeY+=29;DrawLabel(g,FpsText+" · поворот "+Settings.Rotation.ToString("0.#")+"°",16,noticeY);noticeY+=29;
            if(Settings.RayEnabled){var origin=new GeoPoint(Settings.RayLat,Settings.RayLon);double distance=GeoMath.DistanceMeters(origin,Center);DrawLabel(g,"Луч до центра: "+Settings.Distance(distance)+(distance>0.05?" · "+PointReadout.Direction(PointReadout.Bearing(origin,Center),Settings.BearingFormat):""),16,noticeY);noticeY+=29;}
            }
            if(Tiles.PendingCount>0){DrawLabel(g,"Загрузка карты…",16,noticeY);noticeY+=29;}else if(!String.IsNullOrEmpty(Tiles.LastError)){DrawLabel(g,Tiles.LastError,16,noticeY);noticeY+=29;}
            if(IsEditing){DrawLabel(g,"ПРАВКА · вершины: тянуть · середины: добавить · Delete · Ctrl+Z · Enter: готово · Esc: отмена",16,noticeY);noticeY+=29;if(Selected is Zone){DrawLabel(g,"Фиолетовая ручка — перенос · синяя — размер · оранжевые — форма",16,noticeY);noticeY+=29;}}
            if(mode==MapMode.Polygon||mode==MapMode.Rectangle||mode==MapMode.Circle){DrawLabel(g,"ЗОНА · Enter — сохранить · Esc — отменить",16,noticeY);noticeY+=29;}
            if(mode==MapMode.Route){DrawLabel(g,"МАРШРУТ · "+draft.Count+" точек · Enter — готово",16,noticeY);noticeY+=29;}
            if(Settings.Elevation){if(Elevation!=null&&!String.IsNullOrEmpty(Elevation.Tiles.LastError)){DrawLabel(g,"Высоты: "+Elevation.Tiles.LastError,16,noticeY);noticeY+=29;}DrawLabel(g,"Высоты точек · Terrain Tiles / Mapzen · AWS",16,Height-76);}
            string attribution=Tiles.Source.Attribution??"";SizeF attribSize=g.MeasureString(attribution,Font);DrawLabel(g,attribution,Math.Max(8,(int)(Width-attribSize.Width-12)),Height-31);

            scaleBounds=Settings.ScaleBar?Hud.DrawScale(g,Width,Height,Center,Zoom,Settings):RectangleF.Empty;
            if(LiveRay!=null)LiveRay.Draw(g);
            DrawLayerCredits(g);
            DrawRegion(g);CountFrame();
        }
        void DrawTileImage(Graphics g,Image image,RectangleF rect,RectangleF source,ImageAttributes attributes){
            if(mapGpuDrawing){mapGpu.DrawTile(image,rect,source,rasterOpacity);return;}
            // GDI rounds subpixel source rectangles to zero at extreme overzoom.
            // Sample the effectively constant texel area directly in that case.
            if(source.Width<0.01f&&source.Height<0.01f&&image is Bitmap){var bitmap=(Bitmap)image;double fx=Math.Max(0,Math.Min(image.Width-1,source.Left+source.Width/2.0-0.5)),fy=Math.Max(0,Math.Min(image.Height-1,source.Top+source.Height/2.0-0.5));int x=(int)fx,y=(int)fy;var a=bitmap.GetPixel(x,y);var b=bitmap.GetPixel(Math.Min(x+1,image.Width-1),y);var c=bitmap.GetPixel(x,Math.Min(y+1,image.Height-1));var d=bitmap.GetPixel(Math.Min(x+1,image.Width-1),Math.Min(y+1,image.Height-1));double tx=fx-x,ty=fy-y;Func<int,int,int,int,int> blend=(aa,bb,cc,dd)=>(int)Math.Round((aa*(1-tx)+bb*tx)*(1-ty)+(cc*(1-tx)+dd*tx)*ty);using(var brush=new SolidBrush(Color.FromArgb((int)(blend(a.A,b.A,c.A,d.A)*rasterOpacity),blend(a.R,b.R,c.R,d.R),blend(a.G,b.G,c.G,d.G),blend(a.B,b.B,c.B,d.B))))g.FillRectangle(brush,rect);return;}
            if(rasterOpacity<1){using(var alpha=new ImageAttributes()){alpha.SetColorMatrix(new ColorMatrix{Matrix33=rasterOpacity});g.DrawImage(image,new[]{new PointF(rect.Left,rect.Top),new PointF(rect.Right,rect.Top),new PointF(rect.Left,rect.Bottom)},source,GraphicsUnit.Pixel,alpha);}return;}
            g.DrawImage(image,new[]{new PointF(rect.Left,rect.Top),new PointF(rect.Right,rect.Top),new PointF(rect.Left,rect.Bottom)},source,GraphicsUnit.Pixel,attributes);
        }
        bool VisiblePoint(PointF p){return !float.IsNaN(p.X)&&!float.IsNaN(p.Y)&&p.X>=-200&&p.X<=Width+200&&p.Y>=-30&&p.Y<=Height+30;}
        void DrawProjectedLine(Graphics g,Pen pen,PointF a,PointF b){
            var ra=PerspectiveMath.Rotate(a.X-Width/2.0,a.Y-Height/2.0,Settings.Rotation);var rb=PerspectiveMath.Rotate(b.X-Width/2.0,b.Y-Height/2.0,Settings.Rotation);a=new PointF(ra.X+Width/2f,ra.Y+Height/2f);b=new PointF(rb.X+Width/2f,rb.Y+Height/2f);
            if(Settings.Perspective){var corners=new[]{PerspectiveMath.Inverse(-Width/2.0,-Height/2.0,Height,Settings.Tilt),PerspectiveMath.Inverse(Width/2.0,-Height/2.0,Height,Settings.Tilt),PerspectiveMath.Inverse(-Width/2.0,Height/2.0,Height,Settings.Tilt),PerspectiveMath.Inverse(Width/2.0,Height/2.0,Height,Settings.Tilt)};double left=corners.Min(p=>p.X)+Width/2.0-8,right=corners.Max(p=>p.X)+Width/2.0+8,top=corners.Min(p=>p.Y)+Height/2.0-8,bottom=corners.Max(p=>p.Y)+Height/2.0+8;
                double dx=b.X-a.X,dy=b.Y-a.Y,lo=0,hi=1;double[] ps={-dx,dx,-dy,dy},qs={a.X-left,right-a.X,a.Y-top,bottom-a.Y};for(int i=0;i<4;i++){if(ps[i]==0){if(qs[i]<0)return;}else{double r=qs[i]/ps[i];if(ps[i]<0)lo=Math.Max(lo,r);else hi=Math.Min(hi,r);if(lo>hi)return;}}b=new PointF((float)(a.X+hi*dx),(float)(a.Y+hi*dy));a=new PointF((float)(a.X+lo*dx),(float)(a.Y+lo*dy));
                var pa=PerspectiveMath.Forward(a.X-Width/2.0,a.Y-Height/2.0,Height,Settings.Tilt);var pb=PerspectiveMath.Forward(b.X-Width/2.0,b.Y-Height/2.0,Height,Settings.Tilt);a=new PointF(pa.X+Width/2f,pa.Y+Height/2f);b=new PointF(pb.X+Width/2f,pb.Y+Height/2f);
            }if(!Single.IsNaN(a.X)&&!Single.IsNaN(a.Y)&&!Single.IsNaN(b.X)&&!Single.IsNaN(b.Y))DrawClippedLine(g,pen,a,b);
        }
        Image GetFallback(string path){Image image;if(fallbackImages.TryGetValue(path,out image))return image;if(missingFallbacks.Contains(path))return null;if(!File.Exists(path)){missingFallbacks.Add(path);return null;}try{using(var raw=Image.FromFile(path))image=new Bitmap(raw);fallbackImages.Add(path,image);return image;}catch(ArgumentException){missingFallbacks.Add(path);return null;}}
        protected override void Dispose(bool disposing){if(disposing){ClearVisibilityScreen();if(VisibilityOverlay!=null){VisibilityOverlay.Dispose();VisibilityOverlay=null;}ClearMarkerSprites();foreach(var layer in rasterLayers)layer.Dispose();rasterLayers.Clear();if(LiveRay!=null){LiveRay.Dispose();LiveRay=null;}if(framePump!=null)framePump.Dispose();Hud.Dispose();if(mapGpu!=null){mapGpu.Dispose();mapGpu=null;}perspectiveRaster.Dispose();if(planeBuffer!=null){planeBuffer.Dispose();planeBuffer=null;}foreach(var image in fallbackImages.Values)image.Dispose();fallbackImages.Clear();}base.Dispose(disposing);}
        void DrawFallback(Graphics g, int z, long x, long y, RectangleF rect) {
            // Only bundled low-resolution tiles are considered; no off-screen network requests.
            for (int pz = Math.Min(3, z - 1); pz >= 1; pz--) { long scale = 1L << (z - pz); string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "default_map", pz.ToString("00"), (x / scale) + "_" + (y / scale) + ".jpg"); var img=GetFallback(path);if(img==null)continue;try { DrawTileImage(g,img,rect,new RectangleF((x % scale) * 256f / scale, (y % scale) * 256f / scale, 256f / scale, 256f / scale),null); return; } catch (ArgumentException) { } }
            g.FillRectangle(Brushes.WhiteSmoke, rect); g.DrawRectangle(Pens.LightGray, rect.X, rect.Y, rect.Width, rect.Height);
        }
        void DrawTrack(Graphics g, IList<GeoPoint> points, Color color, int width) {
            using (var pen = new Pen(color, width) { LineJoin = LineJoin.Round }) for (int i = 1; i < points.Count; i++) {
                var a = ToPlane(points[i - 1]); var b = ToPlane(points[i]); float world = (float)GeoMath.WorldSize(Zoom);
                if (Math.Abs(a.X - b.X) > world / 2) { if (a.X < b.X) a.X += world; else b.X += world; }
                DrawProjectedLine(g, pen, a,b);
                if (a.X > Width || b.X > Width) { a.X -= world; b.X -= world; DrawProjectedLine(g, pen, a,b); }
            }
        }
        void DrawClippedLine(Graphics g, Pen pen, PointF a, PointF b) {
            if(float.IsNaN(a.X)||float.IsNaN(b.X))return;
            double dx = b.X - a.X, dy = b.Y - a.Y, lo = 0, hi = 1;
            double[] ps = { -dx, dx, -dy, dy }, qs = { a.X + 8, Width + 8 - a.X, a.Y + 8, Height + 8 - a.Y };
            for (int i = 0; i < 4; i++) { if (ps[i] == 0) { if (qs[i] < 0) return; } else { double r = qs[i] / ps[i]; if (ps[i] < 0) lo = Math.Max(lo, r); else hi = Math.Min(hi, r); if (lo > hi) return; } }
            g.DrawLine(pen, (float)(a.X + lo * dx), (float)(a.Y + lo * dy), (float)(a.X + hi * dx), (float)(a.Y + hi * dy));
        }
        void DrawLabel(Graphics g, string text, int x, int y) { var size = g.MeasureString(text, Font); using (var b = new SolidBrush(Color.FromArgb(235, 255, 255, 255))) g.FillRectangle(b, x - 5, y - 3, size.Width + 10, size.Height + 6); g.DrawString(text, Font, Brushes.DarkSlateGray, x, y); }
    }

    static class TextPrompt
    {
        public static string Ask(IWin32Window owner, string title, string initial) {
            using (var f = new Form { Text = title, ClientSize = new Size(460, 112), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Font = new Font("Segoe UI", 10) }) {
                var text = new TextBox { Left = 15, Top = 16, Width = 430, Text = initial ?? "", MaxLength = 300 };
                var ok = new Button { Text = "Готово", DialogResult = DialogResult.OK, Left = 248, Top = 66, Width = 95, Height = 30 };
                var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Left = 350, Top = 66, Width = 95, Height = 30 };
                f.Controls.Add(text); f.Controls.Add(ok); f.Controls.Add(cancel); f.AcceptButton = ok; f.CancelButton = cancel;
                f.Shown += delegate { text.Focus(); text.SelectAll(); };
                return f.ShowDialog(owner) == DialogResult.OK ? text.Text.Trim() : null;
            }
        }
    }
}














