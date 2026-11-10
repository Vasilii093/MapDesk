using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
class MakeIcon {
 static void Main(string[] args){Directory.CreateDirectory(args[0]);int[] sizes={16,24,32,48,64,128,256};var images=new byte[sizes.Length][];
 for(int i=0;i<sizes.Length;i++)using(var large=new Bitmap(1024,1024)){using(var g=Graphics.FromImage(large)){g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(4,4);
 using(var path=new GraphicsPath()){path.AddArc(8,8,64,64,180,90);path.AddArc(184,8,64,64,270,90);path.AddArc(184,184,64,64,0,90);path.AddArc(8,184,64,64,90,90);path.CloseFigure();using(var fill=new LinearGradientBrush(new Point(0,0),new Point(256,256),Color.FromArgb(23,67,79),Color.FromArgb(8,30,43)))g.FillPath(fill,path);}
 using(var contour=new Pen(Color.FromArgb(60,180,165),5)){g.DrawArc(contour,29,50,195,166,18,144);g.DrawArc(contour,53,75,148,116,14,151);}
 using(var ring=new Pen(Color.FromArgb(126,217,205),7))g.DrawArc(ring,44,38,168,168,204,132);
 g.FillPolygon(Brushes.WhiteSmoke,new[]{new PointF(137,49),new PointF(93,174),new PointF(131,151)});
 using(var needle=new SolidBrush(Color.FromArgb(247,176,67)))g.FillPolygon(needle,new[]{new PointF(137,49),new PointF(131,151),new PointF(163,181)});
 using(var point=new SolidBrush(Color.FromArgb(247,176,67)))g.FillEllipse(point,120,203,16,16);
 }
 using(var small=new Bitmap(sizes[i],sizes[i])){using(var g=Graphics.FromImage(small)){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(large,0,0,sizes[i],sizes[i]);}using(var mem=new MemoryStream()){small.Save(mem,ImageFormat.Png);images[i]=mem.ToArray();}if(sizes[i]==256)small.Save(Path.Combine(args[0],"MapDesk.png"),ImageFormat.Png);}}
 using(var file=File.Create(Path.Combine(args[0],"MapDesk.ico")))using(var w=new BinaryWriter(file)){w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;for(int i=0;i<sizes.Length;i++){w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)0);w.Write((byte)0);w.Write((ushort)1);w.Write((ushort)32);w.Write(images[i].Length);w.Write(offset);offset+=images[i].Length;}foreach(var bytes in images)w.Write(bytes);}
 }
}
