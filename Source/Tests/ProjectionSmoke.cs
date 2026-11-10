using System;
using AlpineQuestPort;
class ProjectionSmoke {
 static int Main(){try{using(var p=new ProjectionEngine()){
 foreach(var id in new[]{"EPSG:4326","EPSG:3857","EPSG:4284","EPSG:28407","EPSG:32637"}){
 var r=p.Convert("EPSG:4326",id,37.62,55.75);Console.WriteLine(id+": "+r.Coordinate.X.ToString("R")+", "+r.Coordinate.Y.ToString("R")+"; accuracy="+r.Accuracy+"; "+r.Operation);
 var back=p.Convert(id,"EPSG:4326",r.Coordinate.X,r.Coordinate.Y);if(Math.Abs(back.Coordinate.X-37.62)>0.00001||Math.Abs(back.Coordinate.Y-55.75)>0.00001)throw new Exception("Roundtrip failed");
 }
 var sk=p.Convert("EPSG:4326","EPSG:28407",37.62,55.75);
 if(Math.Abs(sk.Coordinate.X-7413462.200356)>0.01||Math.Abs(sk.Coordinate.Y-6181691.999075)>0.01)throw new Exception("SK42 regression reference mismatch");
 bool rejected=false;try{p.Convert("EPSG:4326","EPSG:28407",10,55);}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Wrong zone accepted");
 rejected=false;try{p.Convert("EPSG:4326","EPSG:3857",Double.NaN,55);}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("NaN accepted");
 rejected=false;try{p.Validate("EPSG:999999999");}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Invalid CRS accepted");
 if(p.Describe("+proj=longlat +datum=WGS84").Kind!="geographic 2D")throw new Exception("Custom geographic CRS not recognized");
 rejected=false;try{p.Describe("EPSG:4978");}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Geocentric CRS accepted in 2D interface");
 Console.WriteLine("PASS references, roundtrips, wrong-zone, invalid inputs, custom CRS, 2D validation "+(Environment.Is64BitProcess?"x64":"x86"));}return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}
