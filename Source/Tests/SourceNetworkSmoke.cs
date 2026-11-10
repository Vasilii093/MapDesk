using System;
using System.IO;
using System.Threading;
using AlpineQuestPort;
class SourceNetworkSmoke {
 static int Main(string[] args){try{foreach(string id in new[]{"esri-imagery","google-local"}){
 var source=MapSource.BuiltIn().Find(m=>m.Id==id);string cache=Path.Combine(args[0],id+"-"+Guid.NewGuid());bool loaded=false;
 using(var tiles=new TileStore(cache,null,source)){tiles.OnlineEnabled=true;for(int i=0;i<150;i++){
 using(var img=tiles.GetTile(2,2,1))if(img!=null){if(img.Width!=256||img.Height!=256)throw new Exception("Unexpected tile dimensions");loaded=true;break;}
 if(!String.IsNullOrEmpty(tiles.LastError)&&tiles.PendingCount==0)throw new Exception(tiles.LastError);Thread.Sleep(200);
 }if(!loaded)throw new Exception("Timeout "+tiles.LastError);}
 if(!source.DiskCache&&Directory.Exists(cache)&&Directory.GetFiles(cache,"*",SearchOption.AllDirectories).Length>0)throw new Exception("Google disk cache unexpectedly written");
 Console.WriteLine("PASS: live "+id+" tile 2/2/1 256x256; cache policy verified");}return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}
