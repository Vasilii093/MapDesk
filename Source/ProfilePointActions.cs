using System;
using System.Drawing;
using System.Windows.Forms;
namespace AlpineQuestPort {
 internal static class ProfilePointActions {
  internal static GeoPoint At(TerrainProfile profile,ProfileAxes axes,Point location){if(profile==null||axes==null)return null;int i=axes.Sample(location,profile.Points.Length);if(i<0)return null;var p=profile.Points[i];return new GeoPoint(p.Lat,p.Lon){Elevation=GeoMath.IsFinite(profile.Heights[i])?(double?)profile.Heights[i]:null,Name="Профиль · "+(profile.X[i]/1000).ToString("0.000")+" км"};}
  internal static GeoPoint HitTemporary(TerrainProfile profile,ProfileAxes axes,Point location,System.Collections.Generic.IEnumerable<GeoPoint> marks){if(profile==null||axes==null||marks==null||!axes.Plot.Contains(location))return null;GeoPoint hit=null;double best=11;foreach(var mark in marks){int index=RayProfilePainter.NearestTemporarySample(profile,mark);if(index<0)continue;double x=axes.Plot.Left+profile.X[index]/profile.Length*axes.Plot.Width;double dx=Math.Abs(x-location.X);if(dx<best){best=dx;hit=mark;}}return hit;}
  internal static ContextMenuStrip CreateMenu(GeoPoint point,Action<GeoPoint> add,Action<GeoPoint> locate,Action remove=null){var menu=new ContextMenuStrip();menu.Items.Add("Добавить временную точку",null,delegate{add(point);});var delete=menu.Items.Add("Удалить временную точку",null,delegate{if(remove!=null)remove();});delete.Enabled=remove!=null;menu.Items.Add("Перейти к точке на карте",null,delegate{locate(point);});return menu;}
  internal static void Menu(Control control,Point location,GeoPoint point,Action<GeoPoint> add,Action<GeoPoint> locate,Action remove=null){if(point==null)return;var menu=CreateMenu(point,add,locate,remove);menu.Closed+=delegate{var context=System.Threading.SynchronizationContext.Current;if(context!=null)context.Post(state=>menu.Dispose(),null);else menu.Dispose();};menu.Show(control,location);}
 }
 sealed partial class MapCanvas {
  internal readonly System.Collections.Generic.List<GeoPoint> RayTemporaryPoints=new System.Collections.Generic.List<GeoPoint>();
  GeoPoint temporaryRayAnchor;
  internal void SetTemporaryRayAnchor(GeoPoint anchor){if(Object.ReferenceEquals(temporaryRayAnchor,anchor))return;temporaryRayAnchor=anchor;RayTemporaryPoints.Clear();if(LiveRay!=null)LiveRay.RefreshGraph();RequestFrame();}
  internal void AddRayTemporaryPoint(GeoPoint anchor,GeoPoint point){if(point==null)return;SetTemporaryRayAnchor(anchor);if(!RayTemporaryPoints.Exists(p=>GeoMath.DistanceMeters(p,point)<1))RayTemporaryPoints.Add(new GeoPoint(point.Lat,point.Lon){Name=point.Name,Elevation=point.Elevation});if(LiveRay!=null)LiveRay.RefreshGraph();RequestFrame();}
  internal void RemoveRayTemporaryPoint(GeoPoint point){if(!RayTemporaryPoints.Remove(point))return;if(LiveRay!=null)LiveRay.RefreshGraph();RequestFrame();}
  internal GeoPoint TemporaryPoint {get;private set;}
  internal void ShowTemporaryPoint(GeoPoint source,bool navigate=false,int zoom=-1){
   if(source==null)return;TemporaryPoint=new GeoPoint(source.Lat,source.Lon){Name=String.IsNullOrWhiteSpace(source.Name)?"Поиск по координатам":source.Name,Elevation=source.Elevation,ColorRgb=0xFFB000};
   if(navigate)GoTo(TemporaryPoint,zoom<0?Zoom:zoom);else RequestFrame();
  }
  internal void ClearTemporaryPoint(){if(TemporaryPoint==null)return;TemporaryPoint=null;RequestFrame();}
  internal void AddProfilePoint(GeoPoint source){ShowTemporaryPoint(source);Say("Временная точка профиля · следующий выбор заменит её");}
  void DrawTemporaryPoint(Graphics g){foreach(var mark in RayTemporaryPoints)DrawTemporaryMarker(g,mark);if(TemporaryPoint!=null)DrawTemporaryMarker(g,TemporaryPoint);}
  void DrawTemporaryMarker(Graphics g,GeoPoint point){var screen=ToScreen(point);if(!VisiblePoint(screen))return;
   g.FillEllipse(Brushes.Orange,screen.X-6,screen.Y-6,12,12);using(var pen=new Pen(Color.White,2))g.DrawEllipse(pen,screen.X-6,screen.Y-6,12,12);
   DrawLabel(g,point.Name+" · временная",(int)screen.X+10,(int)screen.Y-12);
  }
  internal void SaveProfilePoint(GeoPoint source){if(source==null)return;var point=new GeoPoint(source.Lat,source.Lon){Name=source.Name,Elevation=source.Elevation,Group="Точки профилей",ColorRgb=0xFFB000};Document.Waypoints.Add(point);Changed();Say("Точка профиля добавлена. Правой кнопкой — переход к месту на карте.");}
 }
}
