using System;
using System.Drawing;
using System.Drawing.Drawing2D;
namespace AlpineQuestPort {
 internal static class RangeRings {
  internal static GeoPoint Destination(GeoPoint center,double meters,double bearing){
   double d=meters/GeoMath.EarthRadiusMeters,a=center.Lat*Math.PI/180,b=bearing*Math.PI/180;
   double lat=Math.Asin(Math.Max(-1,Math.Min(1,Math.Sin(a)*Math.Cos(d)+Math.Cos(a)*Math.Sin(d)*Math.Cos(b))));
   double lon=center.Lon*Math.PI/180+Math.Atan2(Math.Sin(b)*Math.Sin(d)*Math.Cos(a),Math.Cos(d)-Math.Sin(a)*Math.Sin(lat));
   return new GeoPoint(lat*180/Math.PI,GeoMath.NormalizeLongitude(lon*180/Math.PI));
  }
  internal static double Step(GeoPoint center,int zoom,int width,int height,double manual){
   if(manual>0)return manual;double mpp=Math.Cos(center.Lat*Math.PI/180)*40075016.686/GeoMath.WorldSize(zoom);
   return Math.Min(2000000,PointReadout.ScaleLength(mpp*Math.Min(width,height)*0.24));
  }
  static bool Valid(PointF p){return !Single.IsNaN(p.X)&&!Single.IsNaN(p.Y)&&!Single.IsInfinity(p.X)&&!Single.IsInfinity(p.Y)&&Math.Abs(p.X)<1000000&&Math.Abs(p.Y)<1000000;}
  static PointF Position(GeoPoint center,double radius,double bearing,Func<GeoPoint,PointF> project){var point=Destination(center,radius,bearing);return Math.Abs(point.Lat)>GeoMath.MaxMercatorLatitude?new PointF(float.NaN,float.NaN):project(point);}
  static void Segment(GraphicsPath path,PointF a,PointF b,int width,int height){if(!Valid(a)||!Valid(b)||Math.Abs(a.X-b.X)>width*2||Math.Abs(a.Y-b.Y)>height*2)return;path.StartFigure();path.AddLine(a,b);}
  static void Label(Graphics g,string text,PointF point,Font font,float dpi,int width,int height){if(!Valid(point)||point.X<5||point.Y<5||point.X>width-5||point.Y>height-5)return;var size=g.MeasureString(text,font);using(var path=new GraphicsPath())using(var pen=new Pen(Color.White,3*dpi){LineJoin=LineJoin.Round}){path.AddString(text,font.FontFamily,(int)FontStyle.Bold,font.SizeInPoints*g.DpiY/72,new PointF(point.X-size.Width/2,point.Y-size.Height/2),StringFormat.GenericDefault);g.DrawPath(pen,path);g.FillPath(Brushes.Black,path);}}
  internal static void Draw(Graphics g,int width,int height,GeoPoint center,int zoom,ViewSettings settings,Func<GeoPoint,PointF> project,Font font){
   if(width<150||height<150)return;float dpi=g.DpiX/96f;double step=Step(center,zoom,width,height,settings.RingStepMeters);string[] directions={"С","СВ","В","ЮВ","Ю","ЮЗ","З","СЗ"};
   using(var labelFont=new Font(font.FontFamily,12,FontStyle.Bold)){
   using(var path=new GraphicsPath())using(var white=new Pen(Color.White,4*dpi){LineJoin=LineJoin.Round})using(var black=new Pen(Color.Black,1.8f*dpi){LineJoin=LineJoin.Round}){
    for(int ring=1;ring<=3;ring++){
     double radius=ring*step;var previous=Position(center,radius,0,project);
     for(int bearing=2;bearing<=360;bearing+=2){var next=Position(center,radius,bearing,project);Segment(path,previous,next,width,height);previous=next;}
     int increment=ring==1?5:90;
     for(int bearing=0;bearing<360;bearing+=increment){var point=Position(center,radius,bearing,project);var inward=Position(center,radius*0.98,bearing,project);if(!Valid(point)||!Valid(inward))continue;double dx=point.X-inward.X,dy=point.Y-inward.Y,len=Math.Sqrt(dx*dx+dy*dy);if(len<0.001)continue;double pixels=(bearing%45==0?12:6)*dpi;Segment(path,point,new PointF((float)(point.X-dx/len*pixels),(float)(point.Y-dy/len*pixels)),width,height);}
    }
    g.DrawPath(white,path);g.DrawPath(black,path);
   }
   for(int ring=1;ring<=3;ring++){foreach(int bearing in new[]{145,135,120,60,45,30,0,225,240,300,315}){var label=Position(center,ring*step,bearing,project);if(!Valid(label)||label.X<65*dpi||label.X>width-65*dpi||label.Y<25*dpi||label.Y>height-35*dpi)continue;Label(g,settings.Distance(ring*step),label,labelFont,dpi,width,height);break;}}
   for(int i=0;i<8;i++){
    var point=Position(center,step,i*45,project);var inward=Position(center,step*0.98,i*45,project);if(!Valid(point)||!Valid(inward))continue;double dx=point.X-inward.X,dy=point.Y-inward.Y,len=Math.Sqrt(dx*dx+dy*dy);if(len<0.001)continue;
    Label(g,directions[i],new PointF((float)(point.X+dx/len*16*dpi),(float)(point.Y+dy/len*16*dpi)),labelFont,dpi,width,height);
    if(i==0){var tip=point;var basePoint=new PointF((float)(point.X-dx/len*14*dpi),(float)(point.Y-dy/len*14*dpi));g.FillPolygon(Brushes.DodgerBlue,new[]{tip,new PointF((float)(basePoint.X-dy/len*6*dpi),(float)(basePoint.Y+dx/len*6*dpi)),new PointF((float)(basePoint.X+dy/len*6*dpi),(float)(basePoint.Y-dx/len*6*dpi))});}
   }
   var origin=project(center);if(Valid(origin)){g.FillEllipse(Brushes.White,origin.X-4*dpi,origin.Y-4*dpi,8*dpi,8*dpi);g.DrawEllipse(Pens.Black,origin.X-4*dpi,origin.Y-4*dpi,8*dpi,8*dpi);}
   }
  }
 }
}
