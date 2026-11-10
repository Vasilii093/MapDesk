using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace AlpineQuestPort {
    internal sealed class ProfileAxes {
        public RectangleF Plot;
        public double Low,High,MinimumDem;
        public bool Curvature;
        public float Y(double height){return Plot.Bottom-(float)((height-Low)/(High-Low)*Plot.Height);}
        public double HeightAtY(float y){return Low+(Plot.Bottom-y)/Plot.Height*(High-Low);}
        public int Sample(Point location,int count){if(count<2||!Plot.Contains(location))return -1;return Math.Max(0,Math.Min(count-1,(int)Math.Round((location.X-Plot.Left)/Plot.Width*(count-1))));}
        public IEnumerable<double> Ticks(){int count=(int)Math.Round((High-Low)/10);for(int i=0;i<=count;i++)yield return Low+i*10;}
        public static ProfileAxes Create(double low,double high,double[] dem,int width,int height,int lines,bool curvature){
            double pad=Math.Max(3,(high-low)*.08);low=Math.Floor((low-pad)/10)*10;high=Math.Ceiling((high+pad)/10)*10;if(high<=low)high=low+10;
            return new ProfileAxes{Low=low,High=high,MinimumDem=dem.Where(GeoMath.IsFinite).DefaultIfEmpty(double.NaN).Min(),Curvature=curvature,Plot=new RectangleF(66,80,Math.Max(20,width-132),Math.Max(24,height-136-lines*19))};
        }
        public void Draw(Graphics g,Font font){
            using(var axis=new Pen(Color.FromArgb(170,170,180),1))using(var grid=new Pen(Color.FromArgb(38,180,190,200),1)){grid.DashStyle=DashStyle.Dot;g.DrawLine(axis,Plot.Left,Plot.Top,Plot.Left,Plot.Bottom);g.DrawLine(axis,Plot.Right,Plot.Top,Plot.Right,Plot.Bottom);
                float last=Single.NegativeInfinity;var ticks=Ticks().Reverse().ToArray();
                foreach(double h in ticks){float y=Y(h);g.DrawLine(axis,Plot.Left-5,y,Plot.Left,y);g.DrawLine(axis,Plot.Right,y,Plot.Right+5,y);if(h==High||h==Low||(y-last>=font.Height+3&&Plot.Bottom-y>=font.Height+3)){string label=h.ToString("0");var size=g.MeasureString(label,font);g.DrawString(label,font,Brushes.LightGray,Plot.Left-9-size.Width,y-font.Height/2f);g.DrawString(label,font,Brushes.LightGray,Plot.Right+9,y-font.Height/2f);g.DrawLine(grid,Plot.Left,y,Plot.Right,y);last=y;}}
            }
            g.DrawString("м",font,Brushes.LightGray,Plot.Left-22,Plot.Top-25);g.DrawString("м",font,Brushes.LightGray,Plot.Right+9,Plot.Top-25);
        }
        internal string Readout(TerrainProfile p,int index,double graphHeight){
            string distance=(p.X[index]/1000).ToString("0.000")+" км";
            if(!GeoMath.IsFinite(p.Heights[index])||!GeoMath.IsFinite(graphHeight))return distance+" · нет DEM · Δh —";
            string text=distance+" · DEM "+p.Heights[index].ToString("0.0")+" м · Δh от min "+(p.Heights[index]-MinimumDem).ToString("+0.0;-0.0;0.0")+" м";
            if(Curvature&&Math.Abs(graphHeight-p.Heights[index])>=.05)text+=" · на графике "+graphHeight.ToString("0.0")+" м";
            return text;
        }
        public void DrawProbe(Graphics g,RayProfileEngine engine,Point? mouse,Font font){
            var p=engine.Profile;if(!mouse.HasValue||p==null)return;int index=Sample(mouse.Value,p.Points.Length);if(index<0)return;
            double h=engine.Radio==null?p.Heights[index]:engine.Radio.Ground[index];float x=mouse.Value.X;
            using(var cross=new Pen(Color.FromArgb(210,90,215,255),1){DashStyle=DashStyle.Dash}){
                g.DrawLine(cross,x,Plot.Top,x,Plot.Bottom);
                if(GeoMath.IsFinite(h)){float y=Y(h);g.DrawLine(cross,Plot.Left,y,Plot.Right,y);g.FillEllipse(Brushes.DeepSkyBlue,x-4,y-4,8,8);g.DrawEllipse(Pens.White,x-4,y-4,8,8);}
            }
            // Free cursor level is independent of the terrain sample and uses the same axis transform.
            float cursorY=mouse.Value.Y;double cursorHeight=HeightAtY(cursorY);
            using(var level=new Pen(Color.FromArgb(255,198,74),1.2f){DashStyle=DashStyle.Dash}){
                g.DrawLine(level,Plot.Left,cursorY,Plot.Right,cursorY);
                g.DrawLine(level,x-6,cursorY,x+6,cursorY);g.DrawLine(level,x,cursorY-6,x,cursorY+6);
                g.DrawEllipse(level,x-4,cursorY-4,8,8);
            }
            string cursorLabel=cursorHeight.ToString("0.0");
            using(var fill=new SolidBrush(Color.FromArgb(255,24,28,30))){
                g.FillRectangle(fill,Plot.Right+6,cursorY-font.Height/2f-2,58,font.Height+4);
                g.DrawString(cursorLabel,font,Brushes.Gold,Plot.Right+8,cursorY-font.Height/2f);
                if(Plot.Height>=90){
                    double startGround=p.Heights.Length>0?p.Heights[0]:double.NaN;
                    string startLabel=GeoMath.IsFinite(startGround)?"От рельефа A: "+(cursorHeight-startGround).ToString("+0.0;-0.0;0.0")+" м":"От рельефа A: нет DEM";
                    var startExtent=g.MeasureString(startLabel,font);
                    float startTop=Math.Max(Plot.Top+30,Math.Min(Plot.Bottom-font.Height-10,cursorY-font.Height-14));
                    float startWidth=Math.Min(Plot.Width,startExtent.Width+14);
                    g.FillRectangle(fill,Plot.Left+4,startTop,startWidth,font.Height+10);
                    using(var startFormat=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(startLabel,font,Brushes.Gold,new RectangleF(Plot.Left+11,startTop+5,startWidth-14,font.Height+2),startFormat);

                    string readout="Курсор: "+cursorLabel+" м"+(GeoMath.IsFinite(h)?" · Δ к рельефу "+(cursorHeight-h).ToString("+0.0;-0.0;0.0")+" м":"");
                    var extent=g.MeasureString(readout,font);float boxWidth=Math.Min(Plot.Width,extent.Width+14),boxLeft=Math.Max(Plot.Left,Math.Min(Plot.Right-boxWidth,x+12)),boxTop=Math.Max(Plot.Top+30,Math.Min(Plot.Bottom-font.Height-12,cursorY-font.Height-14));
                    g.FillRectangle(fill,boxLeft,boxTop,boxWidth,font.Height+10);
                    using(var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(readout,font,Brushes.Gold,new RectangleF(boxLeft+7,boxTop+5,boxWidth-14,font.Height+2),format);
                }
            }
            string text=Readout(p,index,h);var size=g.MeasureString(text,font);float width=Math.Min(Plot.Width, size.Width+14),left=Math.Min(Plot.Right-width,Math.Max(Plot.Left,x+12)),top=Plot.Top+5;
            if(GeoMath.IsFinite(h)&&Y(h)<Plot.Top+font.Height*3)top=Plot.Bottom-font.Height-14;
            using(var fill=new SolidBrush(Color.FromArgb(245,18,23,27)))g.FillRectangle(fill,left,top,width,font.Height+10);
            using(var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(text,font,Brushes.White,new RectangleF(left+7,top+5,width-14,font.Height+2),format);
        }
    }
}


