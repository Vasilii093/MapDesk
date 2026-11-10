using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace AlpineQuestPort {
 public enum BearingUnits { Градусы, Деления_6000, Деления_6400 }
 public static class PointReadout {
  public static string Wgs(GeoPoint p){return Math.Abs(p.Lat).ToString("F6",CultureInfo.InvariantCulture)+"° "+(p.Lat<0?"S":"N")+"  "+Math.Abs(p.Lon).ToString("F6",CultureInfo.InvariantCulture)+"° "+(p.Lon<0?"W":"E");}
  public static double Bearing(GeoPoint from,GeoPoint to){double a=from.Lat*Math.PI/180,b=to.Lat*Math.PI/180,d=(to.Lon-from.Lon)*Math.PI/180;double y=Math.Sin(d)*Math.Cos(b),x=Math.Cos(a)*Math.Sin(b)-Math.Sin(a)*Math.Cos(b)*Math.Cos(d);return (Math.Atan2(y,x)*180/Math.PI+360)%360;}
  public static string Direction(double degrees,BearingUnits units){if(units==BearingUnits.Градусы)return degrees.ToString("F1")+"° ист.";int turn=units==BearingUnits.Деления_6000?6000:6400;return (((int)Math.Round(degrees*turn/360))%turn).ToString()+" дел. ("+turn+") ист.";}
  public static double ScaleLength(double desiredMeters){double power=Math.Pow(10,Math.Floor(Math.Log10(Math.Max(0.000000000001,desiredMeters))));double n=desiredMeters/power;return (n>=5?5:n>=2?2:1)*power;}
 }
 public sealed class MapHud:IDisposable {
  readonly Font compact=new Font("Segoe UI",11),large=new Font("Segoe UI",19),secondary=new Font("Segoe UI",16),small=new Font("Segoe UI",9),targetFont=new Font("Segoe UI",13);
  readonly List<RectangleF> cards=new List<RectangleF>();
  public string Title="ЦЕНТР КАРТЫ",Primary="",Secondary="",HeightText="↑ —",TargetTitle="",TargetCoordinates="",TargetInfo="";
  public RectangleF CoordinateBounds {get;private set;}
  public RectangleF SearchBounds {get;private set;}
  public bool Contains(Point p){return cards.Exists(r=>r.Contains(p));}
  public void ClearBounds(){cards.Clear();SearchBounds=RectangleF.Empty;CoordinateBounds=RectangleF.Empty;}
  static GraphicsPath Rounded(RectangleF r,float radius){var p=new GraphicsPath();float d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
  int cardAlpha=191;
  void Card(Graphics g,RectangleF rect){cards.Add(rect);using(var shadow=Rounded(new RectangleF(rect.X+2,rect.Y+3,rect.Width,rect.Height),10))using(var b=new SolidBrush(Color.FromArgb(55,0,0,0)))g.FillPath(b,shadow);using(var path=Rounded(rect,10)){using(var fill=new SolidBrush(Color.FromArgb(cardAlpha,255,255,255)))g.FillPath(fill,path);using(var pen=new Pen(Color.FromArgb(210,215,215)))g.DrawPath(pen,path);}}
  void Line(Graphics g,string text,Font font,Brush color,RectangleF r){using(var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(text,font,color,r,format);}
  public float Draw(Graphics g,int width,int height,GeoPoint center,int zoom,int maxZoom,ViewSettings settings){cardAlpha=(int)(Math.Max(30,Math.Min(100,settings.CardOpacity))*2.55);cards.Clear();SearchBounds=RectangleF.Empty;CoordinateBounds=RectangleF.Empty;if(width<120||height<120)return 0;float dpi=g.DpiX/96f,pad=12*dpi,x=14*dpi,y=14*dpi,available=Math.Max(100,width-2*x),lh=large.GetHeight(g),sh=secondary.GetHeight(g),mh=small.GetHeight(g);
   if(settings.CompactCards)return DrawCompact(g,width,height,center,zoom,maxZoom,settings);
   string main=Primary+"  "+HeightText;float w=Math.Min(available,Math.Max(320*dpi,Math.Max(g.MeasureString(main,large).Width,g.MeasureString(Secondary,secondary).Width)+2*pad+44*dpi));
   float h=mh+lh+(String.IsNullOrEmpty(Secondary)?0:sh)+2*pad;CoordinateBounds=new RectangleF(x,y,w,h);Card(g,CoordinateBounds);Line(g,Title,small,Brushes.DimGray,new RectangleF(x+pad,y+7*dpi,w-2*pad-44*dpi,mh));
   Line(g,main,large,Brushes.Black,new RectangleF(x+pad,y+mh+8*dpi,w-2*pad-44*dpi,lh+2));if(!String.IsNullOrEmpty(Secondary))Line(g,Secondary,secondary,Brushes.DarkBlue,new RectangleF(x+pad,y+mh+lh+8*dpi,w-2*pad-44*dpi,sh+2));SearchBounds=new RectangleF(x+w-44*dpi,y+10*dpi,36*dpi,36*dpi);
   using(var pen=new Pen(Color.DarkSlateGray,2.5f*dpi)){float sx=SearchBounds.X+7*dpi,sy=SearchBounds.Y+6*dpi;g.DrawEllipse(pen,sx,sy,15*dpi,15*dpi);g.DrawLine(pen,sx+13*dpi,sy+13*dpi,sx+24*dpi,sy+24*dpi);}y+=h+6*dpi;
   double metersPerPixel=Math.Cos(center.Lat*Math.PI/180)*40075016.686/GeoMath.WorldSize(zoom),scale=metersPerPixel*g.DpiX/0.0254;
   string scaleText="≈ 1:"+(scale>=1?Math.Round(scale).ToString("N0"):scale.ToString("0.######"))+"   "+zoom+"/"+maxZoom;float sw=Math.Min(available,Math.Max(240*dpi,g.MeasureString(scaleText,targetFont).Width+2*pad)),sheight=targetFont.GetHeight(g)+16*dpi;
   Card(g,new RectangleF(x,y,sw,sheight));Line(g,scaleText,targetFont,Brushes.Black,new RectangleF(x+pad,y+7*dpi,sw-2*pad,targetFont.GetHeight(g)+2));y+=sheight+6*dpi;
   if(!String.IsNullOrEmpty(TargetTitle)){float tw=Math.Min(available,Math.Max(360*dpi,Math.Max(g.MeasureString(TargetInfo,targetFont).Width,g.MeasureString(TargetCoordinates,small).Width)+2*pad));float th=2*mh+targetFont.GetHeight(g)+25*dpi;Card(g,new RectangleF(x,y,tw,th));Line(g,TargetTitle,small,Brushes.DimGray,new RectangleF(x+pad,y+6*dpi,tw-2*pad,mh+2));Line(g,TargetInfo,targetFont,Brushes.Black,new RectangleF(x+pad,y+mh+8*dpi,tw-2*pad,targetFont.GetHeight(g)+2));Line(g,TargetCoordinates,small,Brushes.DarkBlue,new RectangleF(x+pad,y+mh+targetFont.GetHeight(g)+12*dpi,tw-2*pad,mh+2));y+=th+6*dpi;}
   return y;
  }
  float DrawCompact(Graphics g,int width,int height,GeoPoint center,int zoom,int maxZoom,ViewSettings settings){
   float dpi=g.DpiX/96f,x=14*dpi,y=14*dpi,pad=10*dpi,lh=compact.GetHeight(g),sh=small.GetHeight(g),available=width-2*x;
   string main=Primary;float w=Math.Min(available,Math.Max(280*dpi,g.MeasureString(main,compact).Width+2*pad+40*dpi));
   float h=lh+2*sh+24*dpi+(String.IsNullOrEmpty(Secondary)?0:sh);
   CoordinateBounds=new RectangleF(x,y,w,h);Card(g,CoordinateBounds);
   Line(g,Title,small,Brushes.DimGray,new RectangleF(x+pad,y+5*dpi,w-2*pad-34*dpi,sh+2));
   Line(g,main,compact,Brushes.Black,new RectangleF(x+pad,y+sh+7*dpi,w-2*pad-34*dpi,lh+2));
   double mpp=Math.Cos(center.Lat*Math.PI/180)*40075016.686/GeoMath.WorldSize(zoom),scale=mpp*g.DpiX/0.0254;
   string footer=HeightText+"  ·  ≈ 1:"+(scale>=1?Math.Round(scale).ToString("N0"):scale.ToString("0.######"));
   Line(g,footer,small,Brushes.DimGray,new RectangleF(x+pad,y+sh+lh+10*dpi,w-2*pad,sh+2));
   if(!String.IsNullOrEmpty(Secondary))Line(g,Secondary,small,Brushes.DarkBlue,new RectangleF(x+pad,y+2*sh+lh+12*dpi,w-2*pad,sh+2));
   SearchBounds=new RectangleF(x+w-34*dpi,y+9*dpi,28*dpi,28*dpi);
   using(var pen=new Pen(Color.DarkSlateGray,2*dpi)){float sx=SearchBounds.X+3*dpi,sy=SearchBounds.Y+3*dpi;g.DrawEllipse(pen,sx,sy,12*dpi,12*dpi);g.DrawLine(pen,sx+10*dpi,sy+10*dpi,sx+20*dpi,sy+20*dpi);}
   y+=h+6*dpi;
   if(!String.IsNullOrEmpty(TargetTitle)){float tw=Math.Min(available,Math.Max(w,g.MeasureString(TargetCoordinates,small).Width+2*pad)),th=3*sh+22*dpi;Card(g,new RectangleF(x,y,tw,th));Line(g,TargetTitle,small,Brushes.DimGray,new RectangleF(x+pad,y+5*dpi,tw-2*pad,sh+2));Line(g,TargetInfo,small,Brushes.Black,new RectangleF(x+pad,y+sh+9*dpi,tw-2*pad,sh+2));Line(g,TargetCoordinates,small,Brushes.DarkBlue,new RectangleF(x+pad,y+2*sh+13*dpi,tw-2*pad,sh+2));y+=th+6*dpi;}
   return y;
  }
  public RectangleF DrawScale(Graphics g,int width,int height,GeoPoint center,int zoom,ViewSettings settings){
   cardAlpha=(int)(Math.Max(30,Math.Min(100,settings.CardOpacity))*2.55);if(width<160||height<220)return RectangleF.Empty;float dpi=g.DpiX/96f,pad=10*dpi,x=16*dpi;
   double mpp=Math.Cos(center.Lat*Math.PI/180)*40075016.686/GeoMath.WorldSize(zoom),desired=Math.Min(180*dpi,width-2*x-2*pad),meters=PointReadout.ScaleLength(mpp*desired);float pixels=(float)(meters/mpp);
   string text=settings.Distance(meters)+(settings.Perspective?" · в центре":"");float w=Math.Min(width-2*x,Math.Max(pixels+2*pad,g.MeasureString(text,small).Width+2*pad)),h=small.GetHeight(g)+30*dpi,y=height-88*dpi-h;
   var rect=new RectangleF(x,y,w,h);using(var path=Rounded(rect,7*dpi)){using(var fill=new SolidBrush(Color.FromArgb(cardAlpha,255,255,255)))g.FillPath(fill,path);g.DrawPath(Pens.Gray,path);}float by=y+12*dpi;using(var pen=new Pen(Color.Black,3*dpi)){g.DrawLine(pen,x+pad,by,x+pad+pixels,by);g.DrawLine(pen,x+pad,by,x+pad,by+6*dpi);g.DrawLine(pen,x+pad+pixels,by,x+pad+pixels,by+6*dpi);}Line(g,text,small,Brushes.Black,new RectangleF(x+pad,by+9*dpi,w-2*pad,small.GetHeight(g)+2));return rect;
  }
  public void Dispose(){compact.Dispose();large.Dispose();secondary.Dispose();small.Dispose();targetFont.Dispose();}
 }
}
