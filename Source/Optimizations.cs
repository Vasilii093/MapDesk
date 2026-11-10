using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlpineQuestPort {
    sealed partial class MainForm {
        readonly Timer searchTimer=new Timer{Interval=220};
        readonly Timer cacheTimer=new Timer{Interval=60000};
        Task cacheTask;
        ToolStrip mainTools;
        readonly Dictionary<string,int> objectPages=new Dictionary<string,int>();
        string lastSearch="";
        readonly HashSet<string> collapsedObjectNodes=new HashSet<string>();
        sealed class ObjectPage {public string Key;public int Count;}
        void InitializeOptimizations(ToolStrip tools,ToolStrip sourceTools){
            mainTools=tools;searchTimer.Tick+=delegate{searchTimer.Stop();RefreshObjects();};
            AddTool(sourceTools,"Кэш…","Место на диске, сохранённые масштабы и лимит",delegate{
                using(var dialog=new MapCacheDialog(Path.Combine(dataDir,"tile-cache-v2"),sources,currentSource,settings.CacheLimitGb,ShowCoverage))
                if(dialog.ShowDialog(this)==DialogResult.OK){settings.CacheLimitGb=dialog.LimitGb;try{settings.Save(Path.Combine(dataDir,"view-settings.xml"));}catch(Exception ex){SetMessage("Настройки кэша не сохранены: "+ex.Message);}MaintainCache();}
            });
            cacheTimer.Tick+=delegate{MaintainCache();};if(!previewMode)cacheTimer.Start();
            objects.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Enter&&objects.SelectedNode!=null&&objects.SelectedNode.Tag is ObjectPage){FocusObject(objects.SelectedNode.Tag);e.Handled=true;}};
            UpdateContextTools();
        }
        void MaintainCache(){
            if(cacheTask!=null){if(!cacheTask.IsCompleted)return;if(cacheTask.IsFaulted)SetMessage("Не удалось ограничить кэш: "+cacheTask.Exception.GetBaseException().Message);cacheTask=null;}
            if(settings.CacheLimitGb<=0)return;
            string root=Path.Combine(dataDir,"tile-cache-v2");long limit=(long)(settings.CacheLimitGb*1073741824m);
            cacheTask=BackgroundJobs.Run("Optimizations",delegate{long removed=TileCacheInventory.Trim(root,limit);if(removed>0)File.AppendAllText(Path.Combine(dataDir,"cache-cleanup.log"),DateTime.UtcNow.ToString("o")+" removedBytes="+removed+" quotaBytes="+limit+Environment.NewLine);});
        }
        void UpdateContextTools(){if(mainTools==null)return;foreach(ToolStripItem item in mainTools.Items){
            var button=item as ToolStripButton;if(button!=null&&(item.Text=="Точка"||item.Text=="Маршрут"))button.Checked=map.CurrentMode==(item.Text=="Точка"?MapMode.Waypoint:MapMode.Route);if(item.Text=="Зона")item.BackColor=map.CurrentMode==MapMode.Polygon||map.CurrentMode==MapMode.Rectangle||map.CurrentMode==MapMode.Circle?Color.LightSteelBlue:Color.Transparent;if(item.Text=="Правка")item.Enabled=!map.IsEditing&&(map.Selected is Track||map.Selected is Zone);
            if(item.Text=="Готово")item.Enabled=map.HasDraft;
            if(item.Text=="Группы…")item.Enabled=map.Document.Waypoints.Count>0;
        }}
        int PageSize(string key){int count;return objectPages.TryGetValue(key,out count)?count:100;}
        void EnsurePointPage(GeoPoint point){if(!AllNodes(objects.Nodes).Any(n=>Object.ReferenceEquals(n.Tag,point))){map.Selected=point;RefreshObjects();}}
        static object NodeKey(TreeNode node){return node.Tag is GeoPoint||node.Tag is Track||node.Tag is Zone?node.Tag:(object)node.Name;}
        void SyncNodes(TreeNodeCollection live,TreeNodeCollection desired){
            var lookup=new Dictionary<object,TreeNode>();foreach(TreeNode node in live)lookup[NodeKey(node)]=node;
            var wanted=desired.Cast<TreeNode>().ToArray();var keys=new HashSet<object>(wanted.Select(NodeKey));
            for(int i=live.Count-1;i>=0;i--)if(!keys.Contains(NodeKey(live[i])))live[i].Remove();
            for(int i=0;i<wanted.Length;i++){
                var next=wanted[i];TreeNode node;
                if(lookup.TryGetValue(NodeKey(next),out node)){
                    if(node.Index!=i){node.Remove();live.Insert(i,node);foreach(var n in Subtree(node))HideNonGroupCheckbox(n);}
                    if(node.Text!=next.Text)node.Text=next.Text;if(node.Checked!=next.Checked)node.Checked=next.Checked;if(node.ForeColor!=next.ForeColor)node.ForeColor=next.ForeColor;
                    node.Tag=next.Tag;SyncNodes(node.Nodes,next.Nodes);
                }else{
                    next.Remove();live.Insert(i,next);foreach(TreeNode n in Subtree(next)){HideNonGroupCheckbox(n);if(n.Nodes.Count>0&&!collapsedObjectNodes.Contains(n.Name))n.Expand();}
                }
            }
        }
        // Enumerate a newly attached subtree, including its root.
        static IEnumerable<TreeNode> Subtree(TreeNode node){yield return node;foreach(TreeNode child in node.Nodes)foreach(var n in Subtree(child))yield return n;}
        void RefreshObjects(){
            searchTimer.Stop();string query=objectSearch.Text.Trim();if(lastSearch.Length==0)foreach(var n in AllNodes(objects.Nodes))if(n.Nodes.Count>0){if(n.IsExpanded)collapsedObjectNodes.Remove(n.Name);else collapsedObjectNodes.Add(n.Name);}if(query!=lastSearch){objectPages.Clear();lastSearch=query;}
            Func<string,string,bool> matchesQuery=(name,group)=>query.Length==0||(name??"").IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0||(group??"").IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0; object selected=map.Selected;refreshingObjects=true;objects.BeginUpdate();
            try{
                var root=new TreeNode();var points=new TreeNode("Точки ("+map.Document.Waypoints.Count+")"){Name="points",Checked=true};
                foreach(var group in map.Document.Waypoints.GroupBy(p=>p.Group??"").OrderBy(g=>g.Key)){
                    var matches=group.Where(p=>matchesQuery(p.Name,String.IsNullOrEmpty(group.Key)?"Без группы":group.Key)).ToList();if(matches.Count==0)continue;
                    bool visible=!hiddenGroups.Contains(group.Key);string key="group:"+group.Key;
                    var node=new TreeNode((String.IsNullOrEmpty(group.Key)?"Без группы":group.Key)+" ("+matches.Count+")"){Name=key,Tag=new PointGroup(group.Key),Checked=visible};
                    int limit=PageSize(key);foreach(var p in matches.Take(limit))node.Nodes.Add(new TreeNode(String.IsNullOrEmpty(p.Name)?"Без названия":p.Name){Tag=p,ForeColor=visible?objects.ForeColor:Color.Gray});
                    var selectedPoint=selected as GeoPoint;if(selectedPoint!=null&&matches.IndexOf(selectedPoint)>=limit)node.Nodes.Add(new TreeNode((String.IsNullOrEmpty(selectedPoint.Name)?"Без названия":selectedPoint.Name)+" · выбранная"){Tag=selectedPoint,ForeColor=visible?objects.ForeColor:Color.Gray});if(matches.Count>limit)node.Nodes.Add(new TreeNode("Ещё "+(matches.Count-limit)+" · двойной щелчок / Enter"){Name="page:"+key,Tag=new ObjectPage{Key=key,Count=limit},ForeColor=Color.SteelBlue});points.Nodes.Add(node);
                }
                var tracks=new TreeNode("Маршруты и треки ("+map.Document.Tracks.Count+")"){Name="tracks",Checked=true};
                var trackList=map.Document.Tracks.Where(t=>matchesQuery(t.Name,"")).ToList();int trackLimit=PageSize("tracks");foreach(var t in trackList.Take(trackLimit))tracks.Nodes.Add(new TreeNode((String.IsNullOrEmpty(t.Name)?"Без названия":t.Name)+" · "+settings.Distance(GeoMath.TrackLengthMeters(t))){Tag=t});
                if(trackList.Count>trackLimit)tracks.Nodes.Add(new TreeNode("Ещё "+(trackList.Count-trackLimit)+" · двойной щелчок / Enter"){Name="page:tracks",Tag=new ObjectPage{Key="tracks",Count=trackLimit}});
                var zones=new TreeNode("Зоны ("+map.Document.Zones.Count+")"){Name="zones",Checked=true};var zoneList=map.Document.Zones.Where(z=>matchesQuery(z.Name,"")).ToList();int zoneLimit=PageSize("zones");foreach(var z in zoneList.Take(zoneLimit))zones.Nodes.Add(new TreeNode(z.Name){Tag=z});
                if(zoneList.Count>zoneLimit)zones.Nodes.Add(new TreeNode("Ещё "+(zoneList.Count-zoneLimit)+" · двойной щелчок / Enter"){Name="page:zones",Tag=new ObjectPage{Key="zones",Count=zoneLimit}});
                root.Nodes.AddRange(new[]{zones,points,tracks});if(query.Length>0&&points.Nodes.Count+tracks.Nodes.Count+zones.Nodes.Count==0)root.Nodes.Add(new TreeNode("Ничего не найдено"){Name="empty"});
                SyncNodes(objects.Nodes,root.Nodes);
                foreach(TreeNode n in AllNodes(objects.Nodes))if(selected!=null&&(Object.ReferenceEquals(n.Tag,selected)||(n.Tag is PointGroup&&selected is PointGroup&&((PointGroup)n.Tag).Name==((PointGroup)selected).Name))){if(objects.SelectedNode!=n)objects.SelectedNode=n;break;}
                map.Selected=selected;
                summary.Text="МОИ ОБЪЕКТЫ\nТочек: "+map.Document.Waypoints.Count+" · линий: "+map.Document.Tracks.Count+(query.Length>0?"\nПоиск фильтрует только список":"\nГалочки — видимость групп");
            }finally{objects.EndUpdate();refreshingObjects=false;}UpdateContextTools();map.RequestFrame();
        }
    }

    internal enum ImportPolicy { Skip,Update,Copies }
    internal sealed class ImportPlan {
        readonly MapDocument target,incoming;
        public readonly int Duplicates,Total;
        static string Coordinate(GeoPoint p){return p.Lat.ToString("R",CultureInfo.InvariantCulture)+","+p.Lon.ToString("R",CultureInfo.InvariantCulture);}
        static string Name(string s){s=s??"";return s.Length+":"+s;}
        static string Key(GeoPoint p){return Name(p.Group)+Name(p.Name)+Coordinate(p);}
        static string Key(Track t){return Name(t.Name)+t.IsRoute+"|"+String.Join(";",t.Points.Select(Coordinate));}
        static string Key(Zone z){return Name(z.Name)+Name(z.Kind)+String.Join(";",z.Points.Select(Coordinate));}
        static int Count<T>(IEnumerable<T> old,IEnumerable<T> added,Func<T,string> key){var keys=new HashSet<string>(old.Select(key));int count=0;foreach(var item in added)if(!keys.Add(key(item)))count++;return count;}
        public ImportPlan(MapDocument target,MapDocument incoming){this.target=target;this.incoming=incoming;Total=incoming.Waypoints.Count+incoming.Tracks.Count+incoming.Zones.Count;Duplicates=Count(target.Waypoints,incoming.Waypoints,Key)+Count(target.Tracks,incoming.Tracks,Key)+Count(target.Zones,incoming.Zones,Key);}
        static void Copy(GeoPoint a,GeoPoint b){a.Name=b.Name;a.Group=b.Group;a.Lat=b.Lat;a.Lon=b.Lon;a.ColorRgb=b.ColorRgb;a.Elevation=b.Elevation;a.Time=b.Time;a.Radio=b.Radio.Clone();}
        static void Copy(Track a,Track b){a.Name=b.Name;a.IsRoute=b.IsRoute;a.Points.Clear();a.Points.AddRange(b.Points);}
        static void Copy(Zone a,Zone b){a.Name=b.Name;a.Kind=b.Kind;a.ColorRgb=b.ColorRgb;a.Points.Clear();a.Points.AddRange(b.Points);}
        static int Merge<T>(List<T> old,List<T> added,Func<T,string> key,Action<T,T> copy,ImportPolicy policy){var index=new Dictionary<string,int>();for(int i=0;i<old.Count;i++)if(!index.ContainsKey(key(old[i])))index.Add(key(old[i]),i);int changed=0;foreach(var item in added){string k=key(item);int at;if(policy==ImportPolicy.Copies||!index.TryGetValue(k,out at)){if(!index.ContainsKey(k))index.Add(k,old.Count);old.Add(item);changed++;}else if(policy==ImportPolicy.Update){if(!Object.ReferenceEquals(old[at],item))copy(old[at],item);changed++;}}return changed;}
        public int Apply(ImportPolicy policy){return Merge(target.Waypoints,incoming.Waypoints,Key,Copy,policy)+Merge(target.Tracks,incoming.Tracks,Key,Copy,policy)+Merge(target.Zones,incoming.Zones,Key,Copy,policy);}
    }
    sealed class ImportReviewDialog:Form {
        public ImportPolicy Policy {get;private set;}
        public ImportReviewDialog(ImportPlan plan,string filename){Text="Импорт объектов";ClientSize=new Size(620,305);Font=new Font("Segoe UI",10);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
            Controls.Add(new Label{Text=filename+"\n\nОбъектов: "+plan.Total+" · совпадений: "+plan.Duplicates+"\n\nСовпадение: название и точные координаты; для точек также группа,\nдля маршрутов и зон — вся геометрия. Другая позиция — новый объект.",Location=new Point(16,15),Size=new Size(590,140)});
            var choice=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(16,174),Width=585};choice.Items.AddRange(new object[]{"Пропустить совпадения","Обновить свойства совпавших объектов из файла","Добавить всё как копии"});choice.SelectedIndex=0;Controls.Add(choice);
            Controls.Add(new Label{Text="Импорт можно отменить одним действием Ctrl+Z.",Location=new Point(16,215),AutoSize=true});
            var accept=new Button{Text="Импортировать",Location=new Point(310,257),Size=new Size(160,32)};accept.Click+=delegate{Policy=(ImportPolicy)choice.SelectedIndex;DialogResult=DialogResult.OK;};var cancel=new Button{Text="Отмена",Location=new Point(485,257),Size=new Size(115,32),DialogResult=DialogResult.Cancel};Controls.Add(accept);Controls.Add(cancel);AcceptButton=accept;CancelButton=cancel;
        }
    }

    internal sealed class CachedFragment {public string Path,Source;public int Z,X,Y;public long Bytes;public DateTime Written;}
    internal static class TileCacheInventory {
        static bool PlainDirectory(string path){return (File.GetAttributes(path)&FileAttributes.ReparsePoint)==0;}
        public static List<CachedFragment> Scan(string root){
            root=Path.GetFullPath(root);var result=new List<CachedFragment>();if(!Directory.Exists(root)||!PlainDirectory(root))return result;
            foreach(string source in Directory.EnumerateDirectories(root)){if(!PlainDirectory(source))continue;foreach(string zoom in Directory.EnumerateDirectories(source)){int z;if(!Int32.TryParse(Path.GetFileName(zoom),out z)||z<0||z>20||!PlainDirectory(zoom))continue;
                foreach(string column in Directory.EnumerateDirectories(zoom)){int x;if(!Int32.TryParse(Path.GetFileName(column),out x)||x<0||x>=(1<<z)||!PlainDirectory(column))continue;
                    foreach(string tile in Directory.EnumerateFiles(column,"*.png")){int y;if(!Int32.TryParse(Path.GetFileNameWithoutExtension(tile),out y)||y<0||y>=(1<<z))continue;try{var f=new FileInfo(tile);if((f.Attributes&FileAttributes.ReparsePoint)!=0)continue;long bytes=f.Length;var meta=new FileInfo(tile+".meta");if(meta.Exists)bytes+=meta.Length;result.Add(new CachedFragment{Path=f.FullName,Source=Path.GetFileName(source),Z=z,X=x,Y=y,Bytes=bytes,Written=f.LastWriteTimeUtc});}catch(FileNotFoundException){}}
                }
            }}return result;
        }
        public static long Trim(string root,long limit){if(limit<=0)return 0;root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;var tiles=Scan(root);long total=tiles.Sum(t=>t.Bytes),removed=0;
            foreach(var tile in tiles.OrderBy(t=>t.Written)){if(total<=limit)break;if(File.Exists(tile.Path+".keep"))continue;if(tile.Written>DateTime.UtcNow.AddMinutes(-2))continue;
                if(!tile.Path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new IOException("Фрагмент вне каталога кэша.");
                // Re-scan paths immediately before deletion; never follow links or touch workspace files.
                string dir=Path.GetDirectoryName(tile.Path);bool safe=true;while(dir.Length>=root.TrimEnd(Path.DirectorySeparatorChar).Length){if(!PlainDirectory(dir)){safe=false;break;}dir=Path.GetDirectoryName(dir);if(dir==null)break;}if(!safe)continue;
                try{if((File.GetAttributes(tile.Path)&FileAttributes.ReparsePoint)!=0)continue;File.Delete(tile.Path);string metadata=tile.Path+".meta";if(File.Exists(metadata)&&(File.GetAttributes(metadata)&FileAttributes.ReparsePoint)==0)File.Delete(metadata);total-=tile.Bytes;removed+=tile.Bytes;}catch(IOException){}catch(UnauthorizedAccessException){}
            }return removed;
        }
    }
    sealed class CoveragePanel:Control {
        public List<CachedFragment> Tiles=new List<CachedFragment>();
        public CoveragePanel(){DoubleBuffered=true;BackColor=Color.FromArgb(239,243,239);}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Tiles.Count==0){e.Graphics.DrawString("Нет сохранённых фрагментов этого масштаба.",Font,Brushes.DimGray,12,12);return;}int minX=Tiles.Min(t=>t.X),maxX=Tiles.Max(t=>t.X),minY=Tiles.Min(t=>t.Y),maxY=Tiles.Max(t=>t.Y);double scale=Math.Min((Width-24.0)/(maxX-minX+1.0),(Height-50.0)/(maxY-minY+1.0));foreach(var t in Tiles)e.Graphics.FillRectangle(Brushes.SeaGreen,(float)(12+(t.X-minX)*scale),(float)(12+(t.Y-minY)*scale),(float)Math.Max(1,scale),(float)Math.Max(1,scale));e.Graphics.DrawString("Зелёный — сохранено; светлый — пробелы. Север сверху.",Font,Brushes.DimGray,12,Height-28);}
    }
    sealed class MapCacheDialog:Form {
        public decimal LimitGb {get;private set;}
        readonly ComboBox source=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};
        readonly ComboBox zoom=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};
        readonly Label stats=new Label();readonly CoveragePanel coverage=new CoveragePanel{Dock=DockStyle.Fill};
        List<CachedFragment> tiles=new List<CachedFragment>();
        public MapCacheDialog(string root,List<MapSource> sources,MapSource current,decimal limit,Action<MapSource,List<CachedFragment>,int> preview=null){Text="Сохранённые карты";ClientSize=new Size(800,570);MinimumSize=new Size(760,570);Font=new Font("Segoe UI",10);StartPosition=FormStartPosition.CenterParent;
            var top=new Panel{Dock=DockStyle.Top,Height=140,Width=800};source.SetBounds(16,14,530,30);zoom.SetBounds(560,14,220,30);source.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top;zoom.Anchor=AnchorStyles.Right|AnchorStyles.Top;stats.SetBounds(16,53,530,80);stats.Text="Подсчёт кэша…";top.Controls.AddRange(new Control[]{source,zoom,stats});
            var bottom=new Panel{Dock=DockStyle.Bottom,Height=155,Width=800};bottom.Controls.Add(new Label{Text="Лимит кэша карт, ГиБ (0 — без удаления):",Location=new Point(16,14),AutoSize=true});var quota=new NumericUpDown{Location=new Point(360,10),Width=110,Minimum=0,Maximum=10000,DecimalPlaces=1,Increment=0.5m,Value=limit};bottom.Controls.Add(quota);
            bottom.Controls.Add(new Label{Text="При ненулевом лимите старые фрагменты удаляются автоматически раз в минуту.\nСначала удаляются давно загруженные; последние 2 минуты защищены. DEM не входит.\nСхема показывает наличие файлов; их целостность проверяется при открытии карты.",Location=new Point(16,47),Size=new Size(760,66)});
            var save=new Button{Text="Применить",Location=new Point(510,115),Size=new Size(130,32),Anchor=AnchorStyles.Right|AnchorStyles.Bottom};save.Click+=delegate{LimitGb=quota.Value;DialogResult=DialogResult.OK;};var cancel=new Button{Text="Закрыть",Location=new Point(650,115),Size=new Size(130,32),DialogResult=DialogResult.Cancel,Anchor=AnchorStyles.Right|AnchorStyles.Bottom};bottom.Controls.Add(save);bottom.Controls.Add(cancel);AcceptButton=save;CancelButton=cancel;
            var show=new Button{Text="Покрытие на карте",Location=new Point(560,94),Size=new Size(220,32),Anchor=AnchorStyles.Top|AnchorStyles.Right,Enabled=preview!=null};show.Click+=delegate{var selected=source.SelectedItem as MapSource;var level=zoom.SelectedItem as ZoomCount;if(selected==null||level==null)return;preview(selected,tiles.Where(t=>t.Source==selected.CacheKey&&t.Z==level.Z).ToList(),level.Z);DialogResult=DialogResult.Cancel;};top.Controls.Add(show);Controls.Add(coverage);Controls.Add(top);Controls.Add(bottom);source.DisplayMember="";foreach(var s in sources.GroupBy(s=>s.CacheKey).Select(g=>g.First()))source.Items.Add(s);source.SelectedItem=sources.FirstOrDefault(s=>s.CacheKey==current.CacheKey);if(source.SelectedIndex<0&&source.Items.Count>0)source.SelectedIndex=0;
            source.SelectedIndexChanged+=delegate{FillZoom();};zoom.SelectedIndexChanged+=delegate{var s=source.SelectedItem as MapSource;var z=zoom.SelectedItem as ZoomCount;coverage.Tiles=s==null||z==null?new List<CachedFragment>():tiles.Where(t=>t.Source==s.CacheKey&&t.Z==z.Z).ToList();coverage.Invalidate();};
            Shown+=async delegate{try{tiles=await BackgroundJobs.Run("Optimizations",()=>CachedSourceVariants.ScanWithDem(root));if(IsDisposed)return;source.Items.Clear();foreach(var variant in CachedSourceVariants.Find(sources,tiles,current))source.Items.Add(variant);source.SelectedItem=source.Items.Cast<MapSource>().FirstOrDefault(s=>s.CacheKey==current.CacheKey);if(source.SelectedIndex<0&&source.Items.Count>0)source.SelectedIndex=0;FillZoom();}catch(Exception ex){if(!IsDisposed)stats.Text="Не удалось прочитать кэш: "+ex.Message;}};
        }
        sealed class ZoomCount {public int Z,Count;public override string ToString(){return "Масштаб "+Z+": "+Count+" шт.";}}
        void FillZoom(){var s=source.SelectedItem as MapSource;var selected=tiles.Where(t=>s!=null&&t.Source==s.CacheKey).ToList();stats.Text="Всего, включая DEM: "+tiles.Count+" фрагментов · "+(tiles.Sum(t=>t.Bytes)/1073741824.0).ToString("F2")+" ГиБ\nЭтот источник: "+selected.Count+" фрагментов · "+(selected.Sum(t=>t.Bytes)/1048576.0).ToString("F1")+" МиБ\nКаждый масштаб хранится отдельно. Выберите масштаб для схемы покрытия.";zoom.Items.Clear();foreach(var g in selected.GroupBy(t=>t.Z).OrderBy(g=>g.Key))zoom.Items.Add(new ZoomCount{Z=g.Key,Count=g.Count()});if(zoom.Items.Count>0)zoom.SelectedIndex=zoom.Items.Count-1;else{coverage.Tiles=new List<CachedFragment>();coverage.Invalidate();}}
    }
}







