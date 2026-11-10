using System;
using System.IO;
using System.Linq;
using System.Text;
using AlpineQuestPort;
class LdkTests {
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Int(Stream s,int v){for(int i=3;i>=0;i--)s.WriteByte((byte)(v>>(8*i)));}
 static void Long(Stream s,long v){Int(s,(int)(v>>32));Int(s,(int)v);}
 static void Text(Stream s,string text){var b=Encoding.UTF8.GetBytes(text);Int(s,b.Length);s.Write(b,0,b.Length);}
 static void Reject(byte[] bytes,string file){File.WriteAllBytes(file,bytes);bool rejected=false;try{MapFiles.Load(file);}catch(InvalidDataException){rejected=true;}Check(rejected,"malformed LDK rejected");}
 static int Main(string[] args){try{
  string sample=args[0],folder=args[1];Directory.CreateDirectory(folder);var bytes=File.ReadAllBytes(sample);var doc=MapFiles.Load(sample);
  Check(doc.Waypoints.Count==2027,"all 2027 sample waypoints imported");Check(doc.Waypoints.All(p=>!String.IsNullOrWhiteSpace(p.Name)&&p.ColorRgb==0xffff00&&GeoMath.IsValidCoordinate(p.Lat,p.Lon)),"names inherited yellow color and valid coordinates");
  Check(doc.Waypoints.Select(p=>p.Group).Distinct().Count()==1&&doc.Waypoints[0].Group==Path.GetFileNameWithoutExtension(sample),"unnamed set receives source file group");
  Check(Math.Abs(doc.Waypoints[0].Lat-48.6778481)<1e-8&&Math.Abs(doc.Waypoints[0].Lon-37.8418932)<1e-8&&Math.Abs(doc.Waypoints[0].Elevation.Value-235.767)<.001,"coordinate order scale and millimetre elevation");
  string gpx=Path.Combine(folder,"imported.gpx");MapFiles.SaveGpx(doc,gpx);var restored=MapFiles.Load(gpx);Check(restored.Waypoints.Count==doc.Waypoints.Count&&restored.Waypoints.Zip(doc.Waypoints,(a,b)=>a.Name==b.Name&&a.Group==b.Group&&a.ColorRgb==b.ColorRgb&&Math.Abs(a.Lat-b.Lat)<1e-7).All(v=>v),"GPX retains names groups colors and coordinates");
  // Add a real nested container node pointing at the original sample's set, no user data alteration.
  using(var s=new MemoryStream()){s.Write(bytes,0,bytes.Length);long child=s.Position;s.Write(bytes,48,48+1200);long meta=s.Position;byte[] payload;using(var m=new MemoryStream()){Int(m,1);Text(m,"name");Text(m,"Nested group");payload=m.ToArray();}Int(s,0x105555);Int(s,0);Long(s,payload.Length);Long(s,payload.Length);Long(s,0);s.Write(payload,0,payload.Length);
   s.Position=child+8;Long(s,meta);Long(s,child+24);s.Position=80;Int(s,1);Int(s,0);s.Position=96;Long(s,child);Int(s,1);string nested=Path.Combine(folder,"nested.ldk");File.WriteAllBytes(nested,s.ToArray());var grouped=MapFiles.Load(nested);Check(grouped.Waypoints.Count==2027&&grouped.Waypoints.All(p=>p.Group.EndsWith(" / Nested group")),"nested folder mapped to group path");}
  string broken=Path.Combine(folder,"broken.ldk");Reject(bytes.Take(70).ToArray(),broken);var cycle=(byte[])bytes.Clone();using(var s=new MemoryStream(cycle)){s.Position=80;Int(s,1);Int(s,0);s.Position=96;Long(s,48);}Reject(cycle,broken);
  Console.WriteLine("LDK tests complete");return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}}
}
