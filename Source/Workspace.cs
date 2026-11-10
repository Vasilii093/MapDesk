using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Runtime.InteropServices;

namespace AlpineQuestPort {
    // Immutable snapshots include all in-memory fields, including per-point radio settings.
    internal static class DocumentSnapshot {
        static void Point(BinaryWriter w, GeoPoint p) {
            w.Write(p.Lat); w.Write(p.Lon); w.Write(p.Name ?? ""); w.Write(p.Group ?? ""); w.Write(p.ColorRgb);
            w.Write(p.Elevation.HasValue); if(p.Elevation.HasValue)w.Write(p.Elevation.Value);
            w.Write(p.Time.HasValue); if(p.Time.HasValue){w.Write(p.Time.Value.Ticks);w.Write(p.Time.Value.Offset.Ticks);}
            var r=p.Radio; w.Write(r.Enabled);w.Write(r.Fresnel);w.Write(r.Curvature);w.Write(r.Count);
            w.Write(r.HeightA);w.Write(r.HeightB);w.Write(r.K);foreach(var f in r.Frequencies)w.Write(f);
        }
        static GeoPoint Point(BinaryReader r) {
            var p=new GeoPoint(r.ReadDouble(),r.ReadDouble()){Name=r.ReadString(),Group=r.ReadString(),ColorRgb=r.ReadInt32()};
            if(r.ReadBoolean())p.Elevation=r.ReadDouble();if(r.ReadBoolean())p.Time=new DateTimeOffset(r.ReadInt64(),new TimeSpan(r.ReadInt64()));
            p.Radio=new PointRadioOptions{Enabled=r.ReadBoolean(),Fresnel=r.ReadBoolean(),Curvature=r.ReadBoolean(),Count=r.ReadInt32(),HeightA=r.ReadDouble(),HeightB=r.ReadDouble(),K=r.ReadDouble(),Frequencies=new[]{r.ReadDouble(),r.ReadDouble(),r.ReadDouble()}};return p;
        }
        public static byte[] Capture(MapDocument d) {
            using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream,Encoding.UTF8)) {
                w.Write(d.Waypoints.Count);foreach(var p in d.Waypoints)Point(w,p);
                w.Write(d.Tracks.Count);foreach(var t in d.Tracks){w.Write(t.Name??"");w.Write(t.IsRoute);w.Write(t.Points.Count);foreach(var p in t.Points)Point(w,p);}
                w.Write(d.Zones.Count);foreach(var z in d.Zones){w.Write(z.Name??"");w.Write(z.Kind);w.Write(z.ColorRgb);w.Write(z.Points.Count);foreach(var p in z.Points)Point(w,p);}
                w.Flush();return stream.ToArray();
            }
        }
        public static MapDocument Restore(byte[] bytes) {
            using(var stream=new MemoryStream(bytes,false))using(var r=new BinaryReader(stream,Encoding.UTF8)) {
                var d=new MapDocument();int n=r.ReadInt32();for(int i=0;i<n;i++)d.Waypoints.Add(Point(r));
                n=r.ReadInt32();for(int i=0;i<n;i++){var t=new Track{Name=r.ReadString(),IsRoute=r.ReadBoolean()};int count=r.ReadInt32();for(int j=0;j<count;j++)t.Points.Add(Point(r));d.Tracks.Add(t);}
                n=r.ReadInt32();for(int i=0;i<n;i++){var z=new Zone{Name=r.ReadString(),Kind=r.ReadString(),ColorRgb=r.ReadInt32()};int count=r.ReadInt32();for(int j=0;j<count;j++)z.Points.Add(Point(r));d.Zones.Add(z);}return d;
            }
        }
    }
    internal sealed class DocumentHistory {
        readonly List<byte[]> states=new List<byte[]>();int position;long bytes;
        public byte[] Current {get{return states[position];}}
        public bool CanUndo {get{return position>0;}}
        public bool CanRedo {get{return position+1<states.Count;}}
        public void Reset(MapDocument d){states.Clear();states.Add(DocumentSnapshot.Capture(d));position=0;bytes=Current.Length;}
        public bool Record(MapDocument d){var next=DocumentSnapshot.Capture(d);if(Current.SequenceEqual(next))return false;
            while(states.Count>position+1){bytes-=states.Last().Length;states.RemoveAt(states.Count-1);}
            states.Add(next);bytes+=next.Length;position++;
            while(states.Count>1&&(states.Count>51||bytes>64L*1024*1024)){bytes-=states[0].Length;states.RemoveAt(0);position--;}
            return true;
        }
        public MapDocument Move(bool redo){if(redo?!CanRedo:!CanUndo)return null;position+=redo?1:-1;return DocumentSnapshot.Restore(Current);}
    }
    internal static class WorkspaceStore {
        public static void Save(byte[] snapshot,string folder){
            Directory.CreateDirectory(folder);string path=Path.Combine(folder,"workspace.gpx"),backup=Path.Combine(folder,"workspace.backup.gpx"),temp=Path.Combine(folder,"workspace-"+Guid.NewGuid().ToString("N")+".gpx");
            try {MapFiles.SaveGpx(DocumentSnapshot.Restore(snapshot),temp);
                // Never replace a healthy backup with a damaged primary file after recovery.
                if(File.Exists(path)){bool valid=true;try{MapFiles.Load(path);}catch{valid=false;}
                    if(valid)File.Replace(temp,path,backup);else File.Replace(temp,path,null);
                }else File.Move(temp,path);
            } finally {if(File.Exists(temp))File.Delete(temp);}
        }
        public static MapDocument Load(string folder,out bool recovered){recovered=false;string path=Path.Combine(folder,"workspace.gpx"),backup=Path.Combine(folder,"workspace.backup.gpx");
            if(File.Exists(path))try{return MapFiles.Load(path);}catch{if(!File.Exists(backup))throw;}
            if(File.Exists(backup)){var d=MapFiles.Load(backup);recovered=true;return d;}return new MapDocument();
        }
    }
    sealed partial class MainForm {
        [StructLayout(LayoutKind.Sequential)] struct TreeItem {public uint Mask;public IntPtr Handle;public uint State,StateMask;public IntPtr Text;public int TextLength,Image,SelectedImage,Children;public IntPtr Data;}
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wparam,ref TreeItem item);
        void HideNonGroupCheckbox(TreeNode node){if(node.Tag is PointGroup)return;var item=new TreeItem{Mask=8,Handle=node.Handle,StateMask=0xF000,State=0};SendMessage(objects.Handle,0x113F,IntPtr.Zero,ref item);}
        readonly DocumentHistory history=new DocumentHistory();
        readonly Timer autosaveTimer=new Timer{Interval=1500};
        readonly ToolStripStatusLabel saveStatus=new ToolStripStatusLabel("Сохранено");
        readonly ToolStripButton undoButton=new ToolStripButton("↶ Отмена"),redoButton=new ToolStripButton("↷ Повтор");
        readonly TextBox objectSearch=new TextBox{Dock=DockStyle.Top,AccessibleName="Поиск по названию или группе"};
        readonly HashSet<string> hiddenGroups=new HashSet<string>(StringComparer.Ordinal);
        Task saveTask;byte[] savingSnapshot;bool historyReady,workspaceLoadFailed;
        void InitializeWorkspace(ToolStrip bar,StatusStrip status,Panel sidebar){
            history.Reset(map.Document);historyReady=true;
            undoButton.ToolTipText="Отменить завершённое действие · Ctrl+Z";redoButton.ToolTipText="Повторить · Ctrl+Y";undoButton.Enabled=redoButton.Enabled=false;
            undoButton.Click+=delegate{HistoryMove(false);};redoButton.Click+=delegate{HistoryMove(true);};bar.Items.Insert(2,undoButton);bar.Items.Insert(3,redoButton);
            status.Items.Insert(0,saveStatus);saveStatus.BorderSides=ToolStripStatusLabelBorderSides.Right;
            var searchPanel=new Panel{Dock=DockStyle.Top,Height=62};searchPanel.Controls.Add(objectSearch);
            var hint=new Label{Dock=DockStyle.Top,Height=24,Text="Поиск по названию или группе",ForeColor=System.Drawing.Color.DimGray};searchPanel.Controls.Add(hint);
            sidebar.Controls.Add(searchPanel);sidebar.Controls.SetChildIndex(searchPanel,sidebar.Controls.GetChildIndex(summary));
            objects.CheckBoxes=true;objects.BeforeCheck+=delegate(object s,TreeViewCancelEventArgs e){if(!refreshingObjects&&!(e.Node.Tag is PointGroup))e.Cancel=true;};
            objects.AfterCheck+=delegate(object s,TreeViewEventArgs e){if(refreshingObjects)return;var group=e.Node.Tag as PointGroup;if(group!=null)SetGroupVisible(group.Name,e.Node.Checked);};
            objectSearch.TextChanged+=delegate{searchTimer.Stop();searchTimer.Start();};objectSearch.KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Escape){objectSearch.Clear();e.Handled=true;e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Enter){searchTimer.Stop();RefreshObjects();objects.Focus();e.Handled=true;e.SuppressKeyPress=true;}};
            autosaveTimer.Tick+=delegate{AutosaveTick();};if(!previewMode)autosaveTimer.Start();
        }
        void ResetHistory(){history.Reset(map.Document);historyReady=true;UpdateHistoryButtons();}
        void UpdateHistoryButtons(){undoButton.Enabled=history.CanUndo;redoButton.Enabled=history.CanRedo;}
        void MarkChanged(){if(restoring||!historyReady)return;if(history.Record(map.CommittedDocument())){dirty=true;saveStatus.Text="Есть изменения…";UpdateHistoryButtons();}}
        void HistoryMove(bool redo){
            if(map.HasDraft){if(!redo)map.Undo();else SetMessage("Сначала завершите правку: Enter, либо отмените: Esc.");return;}
            int point=map.Document.Waypoints.IndexOf(map.Selected as GeoPoint),track=map.Document.Tracks.IndexOf(map.Selected as Track),zone=map.Document.Zones.IndexOf(map.Selected as Zone);
            var document=history.Move(redo);if(document==null)return;
            if(map.LiveRay!=null)map.LiveRay.Stop();record.Checked=false;recording=null;map.Document=document;
            map.Selected=point>=0&&point<document.Waypoints.Count?(object)document.Waypoints[point]:track>=0&&track<document.Tracks.Count?(object)document.Tracks[track]:zone>=0&&zone<document.Zones.Count?(object)document.Zones[zone]:null;
            if(settings.RayEnabled){var anchor=document.Waypoints.FirstOrDefault(p=>p.Lat==settings.RayLat&&p.Lon==settings.RayLon);if(anchor!=null&&anchor.Radio.Enabled)map.StartLiveRay(anchor,Path.Combine(dataDir,"elevation-cache"));else{settings.RayEnabled=false;rayButton.Checked=false;}}
            dirty=true;saveStatus.Text="Есть изменения…";UpdateHistoryButtons();RefreshObjects();SetMessage(redo?"Действие повторено.":"Действие отменено.");
        }
        void FinishSave(){if(saveTask==null||!saveTask.IsCompleted)return;try{saveTask.GetAwaiter().GetResult();if(history.Current.SequenceEqual(savingSnapshot)){dirty=false;saveStatus.Text="Сохранено · "+DateTime.Now.ToString("HH:mm:ss");}else saveStatus.Text="Есть изменения…";}catch(Exception ex){Diagnostics.Record("workspace.autosave",ex);saveStatus.Text="Не сохранено";saveStatus.ToolTipText=ex.Message;SetMessage("Ошибка автосохранения: "+ex.Message);}saveTask=null;savingSnapshot=null;}
        void AutosaveTick(){FinishSave();if(previewMode||workspaceLoadFailed||!dirty||restoring||saveTask!=null)return;savingSnapshot=history.Current;var snapshot=savingSnapshot;saveStatus.Text="Сохранение…";saveTask=Task.Run(()=>WorkspaceStore.Save(snapshot,dataDir));}
        void SaveWorkspaceNow(){if(workspaceLoadFailed){if(dirty)throw new IOException("Рабочий набор не был восстановлен. Экспортируйте новые данные вручную.");return;}if(saveTask!=null){try{saveTask.GetAwaiter().GetResult();}catch{}FinishSave();}if(dirty){WorkspaceStore.Save(history.Current,dataDir);dirty=false;saveStatus.Text="Сохранено";}}
        internal void SetGroupVisible(string name,bool visible){if(visible)hiddenGroups.Remove(name??"");else hiddenGroups.Add(name??"");ApplyGroupVisibility();RefreshObjects();if(previewMode)return;try{var xml=new XElement("groups",hiddenGroups.Select(n=>new XElement("hidden",n)));string path=Path.Combine(dataDir,"group-visibility.xml");AtomicSettings.SaveXml(path,xml);}catch(Exception ex){Diagnostics.Record("Workspace.failure",ex);SetMessage("Не удалось сохранить видимость: "+ex.Message);}}
        void ApplyGroupVisibility(){map.HiddenGroups.Clear();foreach(var group in hiddenGroups)map.HiddenGroups.Add(group);if(map.Selected is GeoPoint&&!map.IsPointVisible((GeoPoint)map.Selected))map.Selected=null;map.RequestFrame();}
        void LoadGroupVisibility(){try{string path=Path.Combine(dataDir,"group-visibility.xml");if(File.Exists(path))foreach(var e in AtomicSettings.LoadXml(path).Elements("hidden"))hiddenGroups.Add(e.Value);ApplyGroupVisibility();RefreshObjects();}catch(Exception ex){Diagnostics.Record("Workspace.failure",ex);SetMessage("Видимость групп не восстановлена: "+ex.Message);}}
    }
}

