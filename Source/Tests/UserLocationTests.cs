using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using AlpineQuestPort;

static class UserLocationTests {
 [STAThread] static int Main(string[] args){try{
  Application.EnableVisualStyles();Directory.CreateDirectory(args[0]);
  var a=new GeoPoint(48.664433,37.863681);
  foreach(int bearing in new[]{0,90})using(var engine=new RayProfileEngine(Path.Combine(args[0],"dem"))){
   var b=RangeRings.Destination(a,20000,bearing);var deadline=DateTime.UtcNow.AddSeconds(175);
   while(DateTime.UtcNow<deadline){engine.Step(a,b,a.Radio,true,DateTime.UtcNow);Application.DoEvents();if(engine.Radio!=null&&engine.Radio.Complete)break;Thread.Sleep(20);}
   Console.WriteLine("20 km bearing="+bearing+" status="+engine.Status);
   if(engine.Radio==null||!engine.Radio.Complete)throw new Exception("User location incomplete");
   Console.WriteLine("PASS user location: "+engine.Profile.Heights.Length+" samples; min="+engine.Profile.Heights.Min()+" max="+engine.Profile.Heights.Max());
   using(var image=RayProfilePainter.Render(engine,900,320,"20 км · "+bearing+"°",true))image.Save(Path.Combine(args[0],"profile-"+bearing+".png"));
  }
  return 0;
 }catch(Exception ex){Console.WriteLine(ex);return 1;}}
}
