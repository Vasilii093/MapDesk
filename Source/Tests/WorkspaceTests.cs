using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using AlpineQuestPort;

class WorkspaceTests {
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Field<T>(object value,string name){return (T)value.GetType().GetField(name,Flags).GetValue(value);}
    static object Call(object value,string name,params object[] args){return value.GetType().GetMethod(name,Flags).Invoke(value,args);}
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
    static void Pump(int milliseconds){var end=DateTime.UtcNow.AddMilliseconds(milliseconds);while(DateTime.UtcNow<end){Application.DoEvents();Thread.Sleep(10);}}
    static MapDocument Fixture(){var d=new MapDocument();d.Waypoints.Add(new GeoPoint(46.57,8.4){Name="Передатчик",Group="Основная",Elevation=150,ColorRgb=0xAABBCC,Time=new DateTimeOffset(2026,10,14,12,0,0,TimeSpan.FromHours(3))});d.Waypoints[0].Radio.Frequencies[1]=2300;d.Waypoints.Add(new GeoPoint(46.58,8.42){Name="Приёмник",Group="Резерв"});var t=new Track{Name="Путь",IsRoute=true};t.Points.Add(new GeoPoint(46.57,8.4));t.Points.Add(new GeoPoint(46.58,8.42));d.Tracks.Add(t);var z=new Zone{Name="Область",Kind="Rectangle",ColorRgb=123};z.Points.Add(new GeoPoint(46.56,8.39));z.Points.Add(new GeoPoint(46.59,8.43));d.Zones.Add(z);return d;}
    [STAThread]static int Main(string[] args){try{Application.EnableVisualStyles();string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        var document=Fixture();var bytes=DocumentSnapshot.Capture(document);var clone=DocumentSnapshot.Restore(bytes);
        Check(bytes.SequenceEqual(DocumentSnapshot.Capture(clone)),"exact snapshot round trip: coordinates, colors, groups, time offset, geometry and radio");clone.Waypoints[0].Radio.Frequencies[0]=123;
        Check(document.Waypoints[0].Radio.Frequencies[0]==900,"snapshot does not share mutable point options");
        var history=new DocumentHistory();history.Reset(document);document.Waypoints.RemoveAt(0);history.Record(document);
        Check(history.Move(false).Waypoints[0].Name=="Передатчик","undo deletion restores complete point");Check(history.Move(true).Waypoints.Count==1,"redo deletion");
        document=history.Move(false);document.Waypoints[0].Name="Изменено";history.Record(document);Check(!history.CanRedo,"new edit discards redo branch");Check(!history.Record(document),"selection/no-op does not create undo entry");
        for(int i=0;i<60;i++){document.Waypoints[0].Name="Пункт "+i;history.Record(document);}int undoCount=0;while(history.CanUndo){history.Move(false);undoCount++;}Check(undoCount==50,"history bounded to 50 completed operations");
        string store=Path.Combine(output,"storage-"+Guid.NewGuid().ToString("N"));WorkspaceStore.Save(bytes,store);var second=Fixture();second.Waypoints[0].Name="После";WorkspaceStore.Save(DocumentSnapshot.Capture(second),store);
        bool recovered;Check(WorkspaceStore.Load(store,out recovered).Waypoints[0].Name=="После"&&!recovered,"atomic save loads latest completed document");
        Check(MapFiles.Load(Path.Combine(store,"workspace.backup.gpx")).Waypoints[0].Name=="Передатчик","backup keeps previous healthy document");
        File.WriteAllText(Path.Combine(store,"workspace.gpx"),"damaged");var repaired=WorkspaceStore.Load(store,out recovered);Check(recovered&&repaired.Waypoints[0].Name=="Передатчик","corrupt primary recovers backup");WorkspaceStore.Save(DocumentSnapshot.Capture(repaired),store);
        Check(MapFiles.Load(Path.Combine(store,"workspace.backup.gpx")).Waypoints[0].Name=="Передатчик","recovery save does not overwrite backup with corrupt data");
        using(var locked=new FileStream(Path.Combine(store,"workspace.gpx"),FileMode.Open,FileAccess.Read,FileShare.None)){bool failed=false;try{WorkspaceStore.Save(DocumentSnapshot.Capture(second),store);}catch(IOException){failed=true;}Check(failed,"locked destination reports save failure");}
        Check(MapFiles.Load(Path.Combine(store,"workspace.gpx")).Waypoints[0].Name=="Передатчик","failed save keeps original intact");
        string ui=Path.Combine(output,"ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(ui);
        using(var form=new MainForm(null,true,ui)){
            form.Show();Pump(100);var map=Field<MapCanvas>(form,"map");map.Settings.Elevation=false;map.Settings.Perspective=false;map.Document=Fixture();Call(form,"ResetHistory");Call(form,"RefreshObjects");
            form.RemoveGroup("Основная");Check(map.Document.Waypoints.Count==1,"group deletion affects only exact group");Call(form,"HistoryMove",false);Check(map.Document.Waypoints.Count==2&&map.Document.Waypoints[0].Radio.Frequencies[1]==2300,"undo group deletion restores properties");Call(form,"HistoryMove",true);Check(map.Document.Waypoints.Count==1,"redo group deletion");Call(form,"HistoryMove",false);
            var point=map.Document.Waypoints[0];form.ApplyMarkerChanges(point,"Новая точка",0xFFAA11,"Другая");Call(form,"HistoryMove",false);Check(map.Document.Waypoints[0].Name=="Передатчик"&&map.Document.Waypoints[0].Group=="Основная","undo name, color and group as one operation");
            map.Selected=map.Document.Tracks[0];map.BeginEdit();map.Document.Tracks[0].Points[0].Lat=47;Call(form,"MarkChanged");Call(form,"SaveWorkspaceNow");
            Check(MapFiles.Load(Path.Combine(ui,"workspace.gpx")).Tracks[0].Points[0].Lat==46.57,"autosave excludes unfinished geometry even after another notification");map.EndEdit(true);Call(form,"HistoryMove",false);Check(map.Document.Tracks[0].Points[0].Lat==46.57,"undo committed geometry");
            var search=Field<TextBox>(form,"objectSearch");search.Text="передат";Pump(300);var tree=Field<TreeView>(form,"objects");Check(tree.Nodes[1].Nodes.Count==1&&tree.Nodes[1].Nodes[0].Nodes.Count==1,"case-insensitive search by partial object name");Check(map.Document.Waypoints.Count==2&&map.HiddenGroups.Count==0,"search leaves document and map visibility intact");search.Text="резерв";Pump(300);Check(tree.Nodes[1].Nodes[0].Nodes[0].Text=="Приёмник","search also matches group name");search.Clear();Pump(300);Check(!search.RectangleToScreen(search.ClientRectangle).IntersectsWith(tree.RectangleToScreen(tree.ClientRectangle)),"search field does not overlap object list");tree.Nodes[1].Nodes[0].Collapse();search.Text="передат";Pump(300);search.Clear();Pump(300);Check(!tree.Nodes[1].Nodes[0].IsExpanded,"search preserves collapsed groups");tree.Nodes[1].Nodes[0].Expand();
            form.SetGroupVisible("Основная",false);map.GoTo(map.Document.Waypoints[0],16);map.Settings.DataCards=false;map.Settings.ScaleBar=false;map.Settings.RangeRings=false;
            Check(Call(map,"HitMarker",new Point(map.Width/2,map.Height/2))==null,"hidden markers cannot be selected on map");Check(map.Document.Waypoints.Count==2,"hiding group preserves export data");form.SetGroupVisible("Основная",true);Check(Call(map,"HitMarker",new Point(map.Width/2,map.Height/2))!=null,"showing group restores hit testing");
            map.Settings.DataCards=true;map.Hud.Primary="X = 6181692,0   Y = 7413462,2";using(var bitmap=new Bitmap(map.Width,map.Height))using(var g=Graphics.FromImage(bitmap)){map.Settings.CompactCards=false;map.Hud.Draw(g,map.Width,map.Height,map.Center,map.Zoom,19,map.Settings);var full=map.Hud.CoordinateBounds;map.Settings.CompactCards=true;map.Hud.Draw(g,map.Width,map.Height,map.Center,map.Zoom,19,map.Settings);Check(map.Hud.CoordinateBounds.Width<full.Width,"compact coordinate card uses less map width");Check(map.Hud.SearchBounds.Right<=map.Hud.CoordinateBounds.Right,"search remains inside compact card");}
            map.Settings.RayEnabled=true;map.StartLiveRay(map.Document.Waypoints[0],Path.Combine(ui,"elevation-cache"));using(var bitmap=new Bitmap(map.Width,map.Height)){map.DrawToBitmap(bitmap,map.ClientRectangle);Check(map.LiveRay.Bounds.Height==66,"invalid short ray collapses to useful hint");bitmap.Save(Path.Combine(output,"profile-collapsed.png"));}
            map.GoTo(new GeoPoint(46.58,8.42),14);Pump(350);map.LiveRay.Tick();using(var bitmap=new Bitmap(map.Width,map.Height)){map.DrawToBitmap(bitmap,map.ClientRectangle);Check(map.LiveRay.Bounds.Height>66,"valid ray restores full profile");map.LiveRay.ToggleCollapsed();map.DrawToBitmap(bitmap,map.ClientRectangle);Check(map.LiveRay.Bounds.Height==66&&map.LiveRay.Visible,"manual collapse retains active ray");map.LiveRay.ToggleCollapsed();map.DrawToBitmap(bitmap,map.ClientRectangle);Check(map.LiveRay.Bounds.Height>66,"manual expand restores profile");bitmap.Save(Path.Combine(output,"profile-expanded.png"));}
            map.LiveRay.Stop();map.Settings.RayEnabled=false;map.GoTo(new GeoPoint(46.57,8.4),8);Call(form,"UpdateCenter");Pump(300);
            using(var screenshot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(screenshot,new Rectangle(0,0,form.Width,form.Height));screenshot.Save(Path.Combine(output,"MapDesk-0.28-overview.png"));}
            int wide=map.Width;form.SetSidebarCollapsed(true);Pump(100);Check(map.Width>wide+150,"collapsed sidebar gives its width to map");Check(Field<Panel>(form,"sidebarRail").Visible,"collapsed sidebar retains expand button");
            using(var shot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(shot,form.ClientRectangle);shot.Save(Path.Combine(output,"sidebar-collapsed.png"));}
            form.SetSidebarCollapsed(false);Pump(100);Check(map.Width==wide,"expanded sidebar restores original map width");
            map.GoTo(new GeoPoint(55.75,37.62),14);Call(form,"SelectSk42");Check(map.Hud.Primary=="X = 6181692   Y = 7413462","SK42 HUD rounds to integer metres");Check(form.CopyText(map.Center,1)=="X = 6181692; Y = 7413462 · СК-42 · EPSG:28407","SK42 copy has no decimal digits");Check(form.CopyText(map.Center,2)=="55.750000, 37.620000","WGS84 precision unchanged");
            var beforeCenter=map.Center;int beforeZoom=map.Zoom;var windowBounds=form.Bounds;
            form.SetSidebarCollapsed(true);form.ToggleMapFullscreen();Pump(150);Check(form.FormBorderStyle==FormBorderStyle.None&&map.ClientSize==form.ClientSize,"fullscreen map occupies full client area");Check(form.Bounds==Screen.FromControl(form).Bounds,"fullscreen fills monitor");Check(Field<Button>(form,"exitMapFullscreen").Visible,"fullscreen exit button visible");
            using(var shot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(shot,form.ClientRectangle);shot.Save(Path.Combine(output,"fullscreen-map.png"));}
            form.ToggleMapFullscreen();Pump(100);Check(form.Bounds==windowBounds&&Field<Panel>(form,"sidebarRail").Visible,"exit restores window and collapsed sidebar");Check(GeoMath.DistanceMeters(beforeCenter,map.Center)<.001&&map.Zoom==beforeZoom,"layout transitions preserve map coordinates and zoom");
            form.WindowState=FormWindowState.Maximized;Pump(50);form.ToggleMapFullscreen();form.ToggleMapFullscreen();Check(form.WindowState==FormWindowState.Maximized,"fullscreen restores maximized state");form.WindowState=FormWindowState.Normal;
            form.Close();
        }
        // Real timer and background writer; no writes to the user's workspace.
        string asyncDir=Path.Combine(output,"async-"+Guid.NewGuid().ToString("N"));using(var form=new MainForm(null,false,asyncDir)){
            form.Show();Pump(200);var map=Field<MapCanvas>(form,"map");map.Document=Fixture();Call(form,"MarkChanged");Pump(3500);
            Check(File.Exists(Path.Combine(asyncDir,"workspace.gpx"))&&!Field<bool>(form,"dirty"),"automatic timer saves without closing window");
            form.SetGroupVisible("Основная",false);form.SetSidebarCollapsed(true);form.Close();
        }
        using(var form=new MainForm(null,false,asyncDir)){form.Show();Pump(100);var map=Field<MapCanvas>(form,"map");Check(map.Document.Waypoints.Count==2&&!map.IsPointVisible(map.Document.Waypoints[0]),"restart restores document and hidden groups");Check(Field<Panel>(form,"sidebarRail").Visible,"sidebar collapse persists across restart");form.Close();}
        Console.WriteLine("PASS: MapDesk 0.29 workspace integration complete");return 0;
    }catch(Exception e){Console.WriteLine(e);return 1;}}
}

