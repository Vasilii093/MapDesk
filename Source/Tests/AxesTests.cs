using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using AlpineQuestPort;
class AxesTests {
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
 static void Set(object value,string property,object data){value.GetType().GetProperty(property).GetSetMethod(true).Invoke(value,new[]{data});}
 [STAThread]static int Main(string[] args){try{Application.EnableVisualStyles();string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
 var axes=ProfileAxes.Create(-23,163,new[]{-20.0,10,160},1500,900,2,false);var ticks=axes.Ticks().ToArray();Check(Math.Abs(axes.HeightAtY(axes.Plot.Top)-axes.High)<1e-9&&Math.Abs(axes.HeightAtY(axes.Plot.Bottom)-axes.Low)<1e-9,"free cursor maps top and bottom to height axis");Check(Math.Abs(axes.HeightAtY(axes.Y(-10)) - (-10))<.0001,"free cursor height round trip supports negative altitudes");Check(axes.Low < -23&&axes.High>163&&ticks.Zip(ticks.Skip(1),(a,b)=>b-a).All(d=>d==10),"height ticks cover range at exactly 10 metres");Check(ticks.First()==axes.Low&&ticks.Last()==axes.High,"both scale endpoints included");Check(Math.Abs(axes.Y(axes.Low)-axes.Plot.Bottom)<.001&&Math.Abs(axes.Y(axes.High)-axes.Plot.Top)<.001,"axis endpoints use same graph height transform");Check(axes.Sample(new Point((int)axes.Plot.Left,(int)axes.Plot.Top+5),101)==0&&axes.Sample(new Point(0,0),101)==-1,"probe index and outside region");
 var origin=new GeoPoint(46.57,8.4);var end=RangeRings.Destination(origin,18900,90);var p=TerrainProfile.Create(origin,end,false,0,30);for(int i=0;i<p.Heights.Length;i++){double t=i/(double)(p.Heights.Length-1);p.Heights[i]=205-110*t+8*Math.Sin(t*30)+4*Math.Sin(t*85);}
 var options=new PointRadioOptions{Count=2,HeightA=11,HeightB=74,Frequencies=new[]{1550.0,1080.0,5800.0}};using(var engine=new RayProfileEngine(Path.Combine(root,"dem"))){Set(engine,"Profile",p);Set(engine,"Options",options);Set(engine,"Radio",RadioResult.Calculate(p,options.Frequencies.Take(2).ToArray(),11,74,options.K));engine.Status="Высоты готовы · тестовый рельеф";
 foreach(var size in new[]{new Size(1500,900),new Size(680,260)}){var handles=new EndpointHandles();using(var image=RayProfilePainter.Render(engine,size.Width,size.Height,"Профиль",size.Width<1000,handles)){
 Check(handles.Axes!=null&&handles.Axes.Plot.Right<size.Width-50,"right ruler reserved margin "+size.Width);var plot=handles.Axes.Plot;int index=p.Heights.Length/2;var mouse=new Point((int)(plot.Left+plot.Width*.5),(int)(plot.Top+plot.Height*.25));using(var g=Graphics.FromImage(image))using(var font=new Font("Segoe UI",9))handles.Axes.DrawProbe(g,engine,mouse,font);image.Save(Path.Combine(root,size.Width>1000?"profile-rulers.png":"profile-rulers-compact.png"));Check(handles.Axes.Readout(p,index,engine.Radio.Ground[index]).Contains("от min"),"probe includes height relative to terrain minimum");Check(Math.Abs(handles.Axes.MinimumDem-p.Heights.Min())<1e-9,"relative reference is actual minimum DEM not chart padding");}
 }
 int minimum=Array.IndexOf(p.Heights,p.Heights.Min());axes=ProfileAxes.Create(p.Heights.Min(),p.Heights.Max(),p.Heights,1500,900,2,false);Check(axes.Readout(p,minimum,p.Heights[minimum]).Contains("0.0".Replace('.',System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0])),"minimum point has zero relative height");p.Heights[minimum]=double.NaN;Check(axes.Readout(p,minimum,double.NaN).Contains("нет DEM"),"missing height does not invent a crosshair altitude");
 }
 Console.WriteLine("ALL AXES TESTS PASSED");return 0;}catch(Exception ex){Console.WriteLine(ex);return 1;}}
}

