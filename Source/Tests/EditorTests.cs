using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using AlpineQuestPort;

static class EditorTests
{
    static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
    static object Call(MapCanvas map,string name,params object[] args){return typeof(MapCanvas).GetMethod(name,flags).Invoke(map,args);}
    static Point Screen(MapCanvas map,GeoPoint point){return Point.Round((PointF)Call(map,"ToScreen",point));}
    static GeoPoint From(MapCanvas map,Point point){return (GeoPoint)Call(map,"FromScreen",point);}
    static void Mouse(MapCanvas map,string method,Point point){Call(map,method,new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0));}
    static void Click(MapCanvas map,Point point){Mouse(map,"OnMouseDown",point);Mouse(map,"OnMouseUp",point);}
    static void Drag(MapCanvas map,Point start,Point end){Mouse(map,"OnMouseDown",start);Mouse(map,"OnMouseMove",end);Mouse(map,"OnMouseUp",end);}
    public static void Run(string output)
    {
        using(var tiles=new TileStore(Path.Combine(output,"editor-cache"),null))using(var map=new MapCanvas(tiles){Size=new Size(1100,800)}){
            map.Settings.DataCards=false;map.Settings.ScaleBar=false;map.Settings.RangeRings=false;map.Settings.Elevation=false;
            foreach(int angle in new[]{0,75}){
                map.Settings.Rotation=angle;map.Settings.Perspective=angle!=0;map.GoTo(new GeoPoint(55.75,37.62),14);
                var track=new Track{Name="Редактируемый маршрут",IsRoute=true};track.Points.AddRange(new[]{From(map,new Point(350,350)),From(map,new Point(650,350)),From(map,new Point(650,600))});
                track.Points[0].Elevation=120;track.Points[0].Time=DateTimeOffset.UtcNow;
                map.Document.Tracks.Add(track);map.Selected=track;map.BeginEdit();var original=new GeoPoint(track.Points[0].Lat,track.Points[0].Lon);var center=map.Center;var target=new Point(390,420);
                Drag(map,Screen(map,track.Points[0]),target);
                Check(GeoMath.DistanceMeters(track.Points[0],From(map,target))<.01&&GeoMath.DistanceMeters(center,map.Center)<.01,"vertex drag follows cursor and preserves camera / rotation "+angle);
                Check(!track.Points[0].Elevation.HasValue&&!track.Points[0].Time.HasValue,"moving imported vertex clears obsolete height and timestamp");
                map.Undo();Check(GeoMath.DistanceMeters(original,track.Points[0])<.01&&track.Points[0].Elevation==120,"undo restores vertex and metadata");
                var midpoint=(GeoPoint)Call(map,"Midpoint",track.Points[0],track.Points[1]);Click(map,Screen(map,midpoint));Check(track.Points.Count==4,"midpoint inserts vertex");
                map.DeleteVertex();Check(track.Points.Count==3,"selected vertex deletion");map.Undo();Check(track.Points.Count==4,"undo deletion");map.EndEdit(false);Check(track.Points.Count==3&&GeoMath.DistanceMeters(original,track.Points[0])<.01,"escape restores whole edit transaction");
                map.Selected=track;map.BeginEdit();Drag(map,Screen(map,track.Points[0]),target);map.EndEdit(true);Check(GeoMath.DistanceMeters(original,track.Points[0])>1,"apply keeps route edits");
                map.Selected=track;var oldLast=track.Points.Last();foreach(ToolStripItem item in map.ContextMenuStrip.Items)if(item.Text=="Развернуть порядок точек маршрута")item.PerformClick();Check(Object.ReferenceEquals(track.Points[0],oldLast),"context menu reverses route order");map.EndEdit(false);
                map.Selected=track;foreach(ToolStripItem item in map.ContextMenuStrip.Items)if(item.Text=="Продолжить маршрут щелчками")item.PerformClick();Click(map,new Point(850,650));Check(track.Points.Count==4,"context menu continues existing route");map.EndEdit(false);
                map.Document.Tracks.Clear();
            }
            map.Settings.Rotation=35;map.Settings.Perspective=true;map.Settings.Tilt=45;
            var corners=new[]{From(map,new Point(320,320)),From(map,new Point(760,330)),From(map,new Point(700,600)),From(map,new Point(330,570))};
            var polygon=map.CreateZone("Polygon",corners,"Многоугольник <А>");polygon.ColorRgb=0xe05030;
            var rectangle=map.CreateZone("Rectangle",new[]{From(map,new Point(300,300)),From(map,new Point(680,530))},"Прямоугольник");
            var circle=map.CreateZone("Circle",new[]{From(map,new Point(500,430)),From(map,new Point(660,430))},"Круг");
            Check(polygon.Outline().Count==4&&rectangle.Outline().Count==4&&circle.Outline().Count==120,"zone shape outlines");
            double radius=GeoMath.DistanceMeters(circle.Points[0],circle.Points[1]);Check(circle.Outline().All(p=>Math.Abs(GeoMath.DistanceMeters(p,circle.Points[0])-radius)<.001),"circle uses geodesic radius");
            map.Selected=polygon;map.BeginEdit();var scale=(GeoPoint)Call(map,"ScaleHandle",polygon);var anchor=(GeoPoint)Call(map,"GeometryCenter",polygon.Points);var before=GeoMath.DistanceMeters(polygon.Points[0],polygon.Points[1]);var screen=Screen(map,scale);var pivot=Screen(map,anchor);
            Drag(map,screen,new Point(screen.X+(screen.X-pivot.X)/2,screen.Y+(screen.Y-pivot.Y)/2));
            Check(GeoMath.DistanceMeters(polygon.Points[0],polygon.Points[1])>before*1.1,"blue handle scales polygon with rotated perspective");map.Undo();Check(Math.Abs(GeoMath.DistanceMeters(polygon.Points[0],polygon.Points[1])-before)<.001,"scale undo");
            var originalLon=polygon.Points[0].Lon;Drag(map,pivot,new Point(pivot.X+45,pivot.Y+40));Check(Math.Abs(polygon.Points[0].Lon-originalLon)>1e-6,"center handle moves entire zone");map.EndEdit(false);
            map.Selected=rectangle;map.BeginEdit();var corner=Screen(map,rectangle.Points[1]);Drag(map,corner,new Point(corner.X+80,corner.Y+30));map.EndEdit(true);
            map.Selected=circle;map.BeginEdit();var edge=Screen(map,circle.Points[1]);Drag(map,edge,new Point(edge.X+70,edge.Y));Check(Math.Abs(GeoMath.DistanceMeters(circle.Points[0],circle.Points[1])-radius)>10,"circle radius handle resizes");map.EndEdit(false);
            foreach(var mode in new[]{MapMode.Rectangle,MapMode.Circle}){map.BeginMode(mode);Drag(map,new Point(200,200),new Point(450,500));var draft=(List<GeoPoint>)typeof(MapCanvas).GetField("draft",flags).GetValue(map);Check(draft.Count==2&&GeoMath.DistanceMeters(draft[0],draft[1])>10,"mouse shape construction "+mode);map.CancelDraft();}
            foreach(string extension in new[]{"gpx","kml"}){
                string path=Path.Combine(output,"zones."+extension);if(extension=="gpx")MapFiles.SaveGpx(map.Document,path);else MapFiles.SaveKml(map.Document,path);
                var read=MapFiles.Load(path);Check(read.Zones.Count==3&&read.Tracks.Count==0&&read.Zones[0].Name==polygon.Name&&read.Zones[0].ColorRgb==polygon.ColorRgb&&read.Zones[1].Kind=="Rectangle"&&read.Zones[2].Kind=="Circle","zone type/name/color persistence without duplicate polygons / "+extension);
                Check(GeoMath.DistanceMeters(read.Zones[1].Points[1],rectangle.Points[1])<.001,"edited zone geometry persists / "+extension);
            }
            map.Selected=polygon;map.BeginEdit();using(var shot=new Bitmap(map.Width,map.Height)){map.DrawToBitmap(shot,map.ClientRectangle);shot.Save(Path.Combine(output,"zone-editor.png"));}map.EndEdit(false);
            string plain=Path.Combine(output,"plain-polygon.kml");File.WriteAllText(plain,"<kml xmlns='http://www.opengis.net/kml/2.2'><Document><Placemark><name>Зона</name><Polygon><outerBoundaryIs><LinearRing><coordinates>37,55 38,55 38,56 37,55</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark></Document></kml>");Check(MapFiles.Load(plain).Zones[0].Points.Count==3,"standard KML polygon import removes closing duplicate");
            File.WriteAllText(plain,"<gpx xmlns='http://www.topografix.com/GPX/1/1'><extensions><zone xmlns='urn:mapdesk:waypoint:1' kind='Circle'><point lat='55' lon='37'/></zone></extensions></gpx>");bool invalid=false;try{MapFiles.Load(plain);}catch(InvalidDataException){invalid=true;}Check(invalid,"invalid zone rejected on import");
        }
        using(var form=new MainForm(null,true)){
            form.Show();var map=(MapCanvas)typeof(MainForm).GetField("map",flags).GetValue(form);map.Settings.Elevation=false;
            var track=new Track{Name="Существующий маршрут"};track.Points.AddRange(new[]{new GeoPoint(55.75,37.60),new GeoPoint(55.76,37.64)});map.Document.Tracks.Add(track);map.Selected=track;
            typeof(MainForm).GetMethod("RefreshObjects",flags).Invoke(form,null);map.BeginEdit();typeof(MainForm).GetMethod("RefreshObjects",flags).Invoke(form,null);Check(map.IsEditing,"sidebar refresh preserves active edit transaction");map.EndEdit(false);
            var zone=map.CreateZone("Rectangle",new[]{new GeoPoint(55.73,37.58),new GeoPoint(55.78,37.67)},"Тестовая зона");map.Fit();map.Selected=zone;map.BeginEdit();
            using(var shot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(shot,new Rectangle(0,0,form.Width,form.Height));shot.Save(Path.Combine(output,"editor-main.png"));}map.EndEdit(false);
            var tree=(TreeView)typeof(MainForm).GetField("objects",flags).GetValue(form);var context=tree.ContextMenuStrip;typeof(ContextMenuStrip).GetMethod("OnOpening",flags).Invoke(context,new object[]{new System.ComponentModel.CancelEventArgs()});Check(context.Items[1].Enabled&&context.Items[2].Enabled&&!context.Items[3].Enabled,"zone context menu enables zone color and overview");
            map.Selected=zone;map.BeginEdit();TreeNode trackNode=null;foreach(TreeNode root in tree.Nodes)foreach(TreeNode node in root.Nodes)if(node.Tag==track)trackNode=node;tree.SelectedNode=trackNode;Check(!map.IsEditing&&map.Selected==track&&tree.SelectedNode.Tag==track,"changing sidebar selection commits edit and selects requested object");
            form.RemoveObject(zone);Check(!map.Document.Zones.Contains(zone),"zone removal from main form");var ga=new GeoPoint(10,20){Name="A",Group="Group"};var gb=new GeoPoint(11,21){Name="B",Group="Group"};var gc=new GeoPoint(12,22){Name="C",Group="Group / Other"};var gd=new GeoPoint(13,23){Name="D",Group=""};
            map.Document.Waypoints.AddRange(new[]{ga,gb,gc,gd});typeof(MainForm).GetMethod("RefreshObjects",flags).Invoke(form,null);
            TreeNode groupNode=null;foreach(TreeNode parent in tree.Nodes)foreach(TreeNode child in parent.Nodes)if(child.Tag is MainForm.PointGroup&&((MainForm.PointGroup)child.Tag).Name=="Group")groupNode=child;
            Check(groupNode!=null,"point group has actionable tree tag");tree.SelectedNode=groupNode;form.RemoveObject(groupNode.Tag);
            Check(!map.Document.Waypoints.Contains(ga)&&!map.Document.Waypoints.Contains(gb)&&map.Document.Waypoints.Contains(gc)&&map.Document.Waypoints.Contains(gd),"delete exact group and preserve other groups");
            string saved=Path.Combine(output,"groups-deleted.gpx");MapFiles.SaveGpx(map.Document,saved);var restored=MapFiles.Load(saved);Check(!restored.Waypoints.Any(p=>p.Group=="Group")&&restored.Waypoints.Any(p=>p.Group=="Group / Other"),"deleted group stays deleted after save and reload");
            form.RemoveGroup("");Check(!map.Document.Waypoints.Contains(gd)&&map.Document.Waypoints.Contains(gc),"delete ungrouped points independently");
            form.Close();
        }
    }
}
