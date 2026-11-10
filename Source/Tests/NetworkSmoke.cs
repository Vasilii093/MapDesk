using System;
using System.Drawing;
using System.IO;
using System.Threading;
using AlpineQuestPort;
class NetworkSmoke
{
    static int Main(string[] args) {
        string cache=Path.GetFullPath(args[0]);
        using(var tiles=new TileStore(cache,null)) {
            tiles.OnlineEnabled=true;
            for(int i=0;i<110;i++) {
                using(var image=tiles.GetTile(1,1,0)) if(image!=null) { Console.WriteLine("PASS: live OSM tile 1/1/0, size="+image.Width+"x"+image.Height); return 0; }
                if(!String.IsNullOrEmpty(tiles.LastError) && tiles.PendingCount==0) { Console.WriteLine("FAIL: "+tiles.LastError); return 1; }
                Thread.Sleep(200);
            }
            Console.WriteLine("FAIL: timeout "+tiles.LastError); return 2;
        }
    }
}
