using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace AlpineQuestPort
{
    // Editing is a transaction: Enter commits, Escape restores the original geometry.
    sealed partial class MapCanvas
    {
        object editTarget;
        List<GeoPoint> originalGeometry, gestureGeometry;
        readonly List<List<GeoPoint>> editUndo = new List<List<GeoPoint>>();
        int activeVertex = -1, dragVertex = -1, originalZoneColor;
        bool geometryDrag, shapeDrag, appendRoute;
        GeoPoint gestureStart, transformCenter;
        double scaleStart;
        public bool IsEditing { get { return editTarget != null; } }
        internal MapDocument CommittedDocument() {
            if(!IsEditing)return Document;
            var copy=DocumentSnapshot.Restore(DocumentSnapshot.Capture(Document));
            int track=Document.Tracks.IndexOf(editTarget as Track),zone=Document.Zones.IndexOf(editTarget as Zone);
            if(track>=0)ReplacePoints(copy.Tracks[track].Points,originalGeometry);
            if(zone>=0){ReplacePoints(copy.Zones[zone].Points,originalGeometry);copy.Zones[zone].ColorRgb=originalZoneColor;}
            return copy;
        }
        List<GeoPoint> EditablePoints { get { return editTarget is Track ? ((Track)editTarget).Points : editTarget is Zone ? ((Zone)editTarget).Points : null; } }
        static GeoPoint CopyPoint(GeoPoint p) { return new GeoPoint(p.Lat,p.Lon){Name=p.Name,Elevation=p.Elevation,Time=p.Time,ColorRgb=p.ColorRgb,Group=p.Group,Radio=p.Radio.Clone()}; }
        static List<GeoPoint> CopyPoints(IEnumerable<GeoPoint> points) { return points.Select(CopyPoint).ToList(); }
        static void ReplacePoints(List<GeoPoint> target,IEnumerable<GeoPoint> values) { var copy=CopyPoints(values);target.Clear();target.AddRange(copy); }
        void SaveEditUndo() { while(editUndo.Count>0&&(editUndo.Count>=50||editUndo.Sum(v=>v.Count)+EditablePoints.Count>250000))editUndo.RemoveAt(0);editUndo.Add(CopyPoints(EditablePoints)); }
        public void BeginEdit() {
            if(IsEditing)return;
            if(HasDraft){Say("Сначала завершите построение (Enter) или отмените (Esc).");return;}
            if(!(Selected is Track)&&!(Selected is Zone)){Say("Выберите маршрут или зону на карте либо в списке, затем нажмите «Правка».");return;}
            editTarget=Selected;if(editTarget is Zone)originalZoneColor=((Zone)editTarget).ColorRgb;originalGeometry=CopyPoints(EditablePoints);editUndo.Clear();activeVertex=-1;appendRoute=false;mode=MapMode.Edit;Cursor=Cursors.Cross;
            Say("Тяните вершины. Щелчок по середине ребра добавляет вершину; Delete удаляет выбранную. Enter — применить; Esc — отменить; Ctrl+Z — назад.");RequestFrame();
        }
        public void EndEdit(bool apply) {
            if(!IsEditing)return;
            if(!apply){ReplacePoints(EditablePoints,originalGeometry);if(editTarget is Zone)((Zone)editTarget).ColorRgb=originalZoneColor;}
            editTarget=null;originalGeometry=null;gestureGeometry=null;editUndo.Clear();geometryDrag=false;Capture=false;activeVertex=-1;appendRoute=false;mode=MapMode.Pan;Cursor=Cursors.Hand;
            if(apply)Changed();else RequestFrame();Say(apply?"Изменения применены.":"Правка отменена, исходная геометрия восстановлена.");
        }
        void UndoEdit() {
            if(geometryDrag){ReplacePoints(EditablePoints,gestureGeometry);geometryDrag=false;Capture=false;}
            if(editUndo.Count==0)return;
            ReplacePoints(EditablePoints,editUndo[editUndo.Count-1]);editUndo.RemoveAt(editUndo.Count-1);activeVertex=-1;RequestFrame();
        }
        public void DeleteVertex() {
            if(!IsEditing||activeVertex<0||activeVertex>=EditablePoints.Count){Say("Сначала щёлкните вершину в режиме правки.");return;}
            var zone=editTarget as Zone;
            if(zone!=null&&zone.Kind!="Polygon"){Say("У фигуры меняется размер ручками. Удаление вершин доступно многоугольнику.");return;}
            if(EditablePoints.Count<=(zone==null?2:3)){Say("Нужно оставить минимум "+(zone==null?"две":"три")+" вершины.");return;}
            SaveEditUndo();EditablePoints.RemoveAt(activeVertex);activeVertex=-1;RequestFrame();
        }
        public void ChangeZoneColor() {
            var zone=Selected as Zone;if(zone==null)return;
            using(var dialog=new ColorDialog{Color=Color.FromArgb(unchecked((int)0xff000000)|zone.ColorRgb),FullOpen=true})if(dialog.ShowDialog(FindForm())==DialogResult.OK){zone.ColorRgb=dialog.Color.ToArgb()&0xffffff;if(IsEditing)RequestFrame();else Changed();}
        }
        void SetupEditorMenu(ContextMenuStrip menu) {
            var separator=new ToolStripSeparator();menu.Items.Add(separator);
            var edit=menu.Items.Add("Редактировать маршрут / зону",null,delegate{BeginEdit();});
            var vertex=menu.Items.Add("Удалить выбранную вершину",null,delegate{DeleteVertex();});
            var extend=menu.Items.Add("Продолжить маршрут щелчками",null,delegate{BeginEdit();if(editTarget is Track){appendRoute=true;Say("Щёлкайте для продолжения с последней точки. Enter — готово, Esc — отмена.");}});
            var reverse=menu.Items.Add("Развернуть порядок точек маршрута",null,delegate{BeginEdit();if(editTarget is Track){SaveEditUndo();EditablePoints.Reverse();RequestFrame();}});
            var color=menu.Items.Add("Цвет зоны…",null,delegate{ChangeZoneColor();});
            var finish=menu.Items.Add("Применить правку (Enter)",null,delegate{EndEdit(true);});
            var cancel=menu.Items.Add("Отменить правку (Esc)",null,delegate{EndEdit(false);});
            menu.Opening+=delegate {
                if(!IsEditing&&!HasDraft&&HitMarker(lastMouse)==null){var hit=HitGeometry(lastMouse);if(hit!=null){Selected=hit;Changed();}}
                bool geometry=Selected is Track||Selected is Zone;
                edit.Visible=geometry&&!IsEditing;vertex.Visible=IsEditing;extend.Visible=reverse.Visible=Selected is Track;color.Visible=Selected is Zone;finish.Visible=cancel.Visible=IsEditing;separator.Visible=geometry;
                if(IsEditing){int index=HitVertex(lastMouse);if(index>=0)activeVertex=index;}
            };
        }
        internal Zone CreateZone(string kind,IEnumerable<GeoPoint> points,string name) {
            var zone=new Zone{Kind=kind,Name=name};zone.Points.AddRange(CopyPoints(points));
            if(zone.Points.Count<(kind=="Polygon"?3:2))return null;
            Document.Zones.Add(zone);Selected=zone;Changed();return zone;
        }
        void FinishZone() {
            string kind=mode==MapMode.Circle?"Circle":mode==MapMode.Rectangle?"Rectangle":"Polygon";
            if(draft.Count<(kind=="Polygon"?3:2)){Say("Для многоугольника нужны три вершины; фигуру задайте протягиванием мыши.");return;}
            if(kind!="Polygon"&&GeoMath.DistanceMeters(draft[0],draft[1])<0.001){Say("Размер фигуры должен быть больше нуля.");return;}
            string name=TextPrompt.Ask(FindForm(),"Название зоны","Зона "+(Document.Zones.Count+1));if(name==null)return;
            var points=CopyPoints(draft);CancelDraft();CreateZone(kind,points,name);Say("Зона создана. «Правка» — вершины, перенос и размер.");
        }
        static double DistanceSquared(PointF a,PointF b){double x=a.X-b.X,y=a.Y-b.Y;return x*x+y*y;}
        int HitVertex(Point location) {
            if(!IsEditing)return -1;
            for(int i=EditablePoints.Count-1;i>=0;i--)if(DistanceSquared(ToScreen(EditablePoints[i]),location)<=100)return i;
            return -1;
        }
        GeoPoint Midpoint(GeoPoint a,GeoPoint b) {
            double x=GeoMath.LonToPixel(a.Lon,0), dx=GeoMath.LonToPixel(b.Lon,0)-x;if(dx>128)dx-=256;if(dx< -128)dx+=256;
            return new GeoPoint(GeoMath.PixelToLat((GeoMath.LatToPixel(a.Lat,0)+GeoMath.LatToPixel(b.Lat,0))/2,0),WrapLongitude(GeoMath.PixelToLon(x+dx/2,0)));
        }
        int HitMidpoint(Point location) {
            if(!IsEditing||editTarget is Zone&&((Zone)editTarget).Kind!="Polygon")return -1;
            int count=EditablePoints.Count, edges=editTarget is Zone?count:count-1;
            for(int i=0;i<edges;i++)if(DistanceSquared(ToScreen(Midpoint(EditablePoints[i],EditablePoints[(i+1)%count])),location)<64)return i+1;
            return -1;
        }
        GeoPoint GeometryCenter(IList<GeoPoint> points) {
            double lon=points[0].Lon,x=0,y=0;foreach(var p in points){x+=WrapLongitude(p.Lon-lon);y+=GeoMath.LatToPixel(p.Lat,0);}return new GeoPoint(GeoMath.PixelToLat(y/points.Count,0),WrapLongitude(lon+x/points.Count));
        }
        GeoPoint ScaleHandle(Zone zone) {
            var outline=zone.Outline();var center=GeometryCenter(zone.Points);
            double dx=outline.Max(p=>Math.Abs(WrapLongitude(p.Lon-center.Lon))),y=outline.Min(p=>GeoMath.LatToPixel(p.Lat,0));
            return new GeoPoint(GeoMath.PixelToLat(y,0),WrapLongitude(center.Lon+dx));
        }
        internal static void TransformGeometry(List<GeoPoint> target,IList<GeoPoint> source,GeoPoint center,double factor,double shiftLon,double shiftY) {
            double cy=GeoMath.LatToPixel(center.Lat,0);
            // Compute and validate the entire result before replacing anything.
            var result=CopyPoints(source);
            for(int i=0;i<result.Count;i++){
                double y=cy+(GeoMath.LatToPixel(source[i].Lat,0)-cy)*factor+shiftY;
                if(y<0||y>256||!GeoMath.IsFinite(y))return;
                result[i].Lon=WrapLongitude(center.Lon+WrapLongitude(source[i].Lon-center.Lon)*factor+shiftLon);result[i].Lat=GeoMath.PixelToLat(y,0);
                result[i].Elevation=null;result[i].Time=null;
            }
            ReplacePoints(target,result);
        }
        bool EditorDown(MouseEventArgs e) {
            if(e.Button!=MouseButtons.Left)return false;
            if(mode==MapMode.Circle||mode==MapMode.Rectangle){draft.Clear();draft.Add(FromScreen(e.Location));draft.Add(FromScreen(e.Location));shapeDrag=true;Capture=true;RequestFrame();return true;}
            if(!IsEditing)return false;
            int index=HitVertex(e.Location);dragVertex=index;
            var zone=editTarget as Zone;
            if(index<0&&zone!=null){
                if(DistanceSquared(ToScreen(ScaleHandle(zone)),e.Location)<144)dragVertex=-3;
                else if(DistanceSquared(ToScreen(GeometryCenter(zone.Points)),e.Location)<144)dragVertex=-2;
            }
            if(dragVertex==-1){
                int edge=HitMidpoint(e.Location);
                if(edge>=0){SaveEditUndo();EditablePoints.Insert(edge,FromScreen(e.Location));activeVertex=edge;RequestFrame();return true;}
                if(appendRoute&&editTarget is Track){SaveEditUndo();EditablePoints.Add(FromScreen(e.Location));activeVertex=EditablePoints.Count-1;RequestFrame();return true;}
                // Empty map area still pans while the editor is open.
                return false;
            }
            activeVertex=index;SaveEditUndo();gestureGeometry=editUndo[editUndo.Count-1];gestureStart=FromScreen(e.Location);transformCenter=GeometryCenter(EditablePoints);
            scaleStart=GeoMath.DistanceMeters(transformCenter,gestureStart);geometryDrag=true;Capture=true;RequestFrame();return true;
        }
        bool EditorMove(MouseEventArgs e) {
            if(shapeDrag){draft[1]=FromScreen(e.Location);RequestFrame();return true;}
            if(!geometryDrag||!IsEditing)return false;
            var point=FromScreen(e.Location);var zone=editTarget as Zone;
            if(dragVertex==-3){double factor=scaleStart>0?GeoMath.DistanceMeters(transformCenter,point)/scaleStart:1;TransformGeometry(EditablePoints,gestureGeometry,transformCenter,Math.Max(.01,Math.Min(100,factor)),0,0);}
            else if(dragVertex==-2||(zone!=null&&zone.Kind=="Circle"&&dragVertex==0)){
                TransformGeometry(EditablePoints,gestureGeometry,transformCenter,1,WrapLongitude(point.Lon-gestureStart.Lon),GeoMath.LatToPixel(point.Lat,0)-GeoMath.LatToPixel(gestureStart.Lat,0));
            }else if(dragVertex>=0){EditablePoints[dragVertex].Lat=point.Lat;EditablePoints[dragVertex].Lon=point.Lon;EditablePoints[dragVertex].Time=null;EditablePoints[dragVertex].Elevation=null;}
            RequestFrame();return true;
        }
        bool EditorUp(MouseEventArgs e) {
            if(shapeDrag){shapeDrag=false;Capture=false;RequestFrame();return true;}
            if(geometryDrag){geometryDrag=false;Capture=false;gestureGeometry=null;RequestFrame();return true;}
            if(IsEditing&&!dragging)return true;
            if(IsEditing){dragging=false;Capture=false;RequestFrame();return true;}
            return false;
        }
        object HitGeometry(Point location) {
            foreach(var track in Document.Tracks.AsEnumerable().Reverse())if(HitLine(track.Points,location,false))return track;
            foreach(var zone in Document.Zones.AsEnumerable().Reverse()){
                var outline=zone.Outline();if(HitLine(outline,location,true))return zone;
                var points=outline.Select(ToScreen).ToArray();if(SafePolygon(points))using(var path=new GraphicsPath()){path.AddPolygon(points);if(path.IsVisible(location))return zone;}
            }
            return null;
        }
        bool HitLine(IList<GeoPoint> points,Point location,bool closed) {
            int count=points.Count;if(count<2)return false;
            for(int i=0;i<(closed?count:count-1);i++){
                var a=ToScreen(points[i]);var b=ToScreen(points[(i+1)%count]);if(!SafePoint(a)||!SafePoint(b))continue;
                double dx=b.X-a.X,dy=b.Y-a.Y,len=dx*dx+dy*dy,t=len>0?Math.Max(0,Math.Min(1,((location.X-a.X)*dx+(location.Y-a.Y)*dy)/len)):0;
                if(DistanceSquared(location,new PointF((float)(a.X+t*dx),(float)(a.Y+t*dy)))<=64)return true;
            }return false;
        }
        static bool SafePoint(PointF p){return !Single.IsNaN(p.X)&&!Single.IsNaN(p.Y)&&Math.Abs(p.X)<1000000&&Math.Abs(p.Y)<1000000;}
        static bool SafePolygon(PointF[] points){return points.Length>=3&&points.All(SafePoint);}
        void DrawZone(Graphics g,Zone zone) {
            var outline=zone.Outline();if(outline.Count<3)return;
            var points=outline.Select(ToScreen).ToArray();var color=Color.FromArgb(unchecked((int)0xff000000)|zone.ColorRgb);
            if(SafePolygon(points))using(var brush=new SolidBrush(Color.FromArgb(45,color)))g.FillPolygon(brush,points);
            outline.Add(outline[0]);DrawTrack(g,outline,color,zone==Selected?4:2);
            if(Settings.PointNames){var p=ToScreen(outline[0]);if(VisiblePoint(p))DrawLabel(g,zone.Name,(int)p.X+10,(int)p.Y+10);}
        }
        void DrawZones(Graphics g) { foreach(var zone in Document.Zones)DrawZone(g,zone); }
        void DrawHandle(Graphics g,GeoPoint point,Color color,bool square,int size) {
            var p=ToScreen(point);if(!VisiblePoint(p))return;
            using(var brush=new SolidBrush(color)){if(square){g.FillRectangle(brush,p.X-size/2,p.Y-size/2,size,size);g.DrawRectangle(Pens.White,p.X-size/2,p.Y-size/2,size,size);}else{g.FillEllipse(brush,p.X-size/2,p.Y-size/2,size,size);g.DrawEllipse(Pens.White,p.X-size/2,p.Y-size/2,size,size);}}
        }
        void DrawEditor(Graphics g) {
            if((mode==MapMode.Circle||mode==MapMode.Rectangle)&&draft.Count==2)DrawZone(g,new Zone{Kind=mode==MapMode.Circle?"Circle":"Rectangle",Name="Предпросмотр",Points=draft});
            if(mode==MapMode.Polygon&&draft.Count>=3)DrawZone(g,new Zone{Name="Предпросмотр",Points=draft});
            if(!IsEditing)return;
            var zone=editTarget as Zone;int count=EditablePoints.Count;
            if(zone==null||zone.Kind=="Polygon")for(int i=0;i<(zone==null?count-1:count);i++)DrawHandle(g,Midpoint(EditablePoints[i],EditablePoints[(i+1)%count]),Color.SeaGreen,false,8);
            if(zone!=null){DrawHandle(g,GeometryCenter(zone.Points),Color.MediumPurple,true,14);DrawHandle(g,ScaleHandle(zone),Color.DodgerBlue,true,16);}
            for(int i=0;i<count;i++)DrawHandle(g,EditablePoints[i],i==activeVertex?Color.Red:Color.DarkOrange,true,12);
        }
    }
}
