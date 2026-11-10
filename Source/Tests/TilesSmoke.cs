using System;
using System.Drawing;
using System.IO;
using System.Threading;
using AlpineQuestPort;
class TilesSmoke
{
    static Image WaitTile(TileStore s, int z, int x, int y)
    {
        for (int i=0;i<150;i++) { Image im=s.GetTile(z,x,y); if(im!=null)return im; Thread.Sleep(20); }
        throw new Exception("Tile did not load");
    }
    static void Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"NativeMapTilesSmoke-"+Guid.NewGuid().ToString("N"));
        string cache=Path.Combine(root,"cache"), offline=Path.Combine(root,"offline");
        Directory.CreateDirectory(Path.Combine(offline,"01"));
        Directory.CreateDirectory(Path.Combine(cache,"2","1"));
        using(var bitmap=new Bitmap(256,256)) { using(var g=Graphics.FromImage(bitmap))g.Clear(Color.Navy);bitmap.Save(Path.Combine(offline,"01","0_0.jpg"),System.Drawing.Imaging.ImageFormat.Jpeg); bitmap.Save(Path.Combine(cache,"2","1","1.png"),System.Drawing.Imaging.ImageFormat.Png); }
        using(var store=new TileStore(cache,offline))
        {
            if(store.OnlineEnabled)throw new Exception("Network defaults to enabled");
            using(var tile=WaitTile(store,1,0,0))if(tile.Width!=256)throw new Exception("Bad offline tile");
            using(var tile=WaitTile(store,1,2,0))if(tile.Width!=256)throw new Exception("Longitude wrapping failed");
            using(var tile=WaitTile(store,1,0,0)){} // Disposing a returned clone must not dispose the cache.
            using(var tile=WaitTile(store,2,1,1)){}
            if(store.GetTile(20,0,0)!=null || store.GetTile(2,0,-1)!=null)throw new Exception("Invalid coordinate accepted");
            store.BeginFrame();
            for(int i=0;i<100;i++)store.GetTile(8,i,100);
            if(store.PendingCount>64)throw new Exception("Queue bound exceeded");
            store.EndFrame();
            for(int i=0;i<150&&store.PendingCount>0;i++)Thread.Sleep(20);
            if(store.PendingCount!=0)throw new Exception("Offline requests failed to drain");
            if(store.LastError!="")throw new Exception("Unexpected error: "+store.LastError);
        }
        using(var store=new TileStore(cache,offline)) { store.OnlineEnabled=true; using(var tile=WaitTile(store,2,1,1)){} store.OnlineEnabled=false; }
        Console.WriteLine("PASS: offline JPEG; disk PNG; clone ownership; X wrapping; coordinate validation; bounded queue; cancellation/dispose; fresh disk cache with online enabled.");
        // Test fixture stays under a uniquely named temporary folder; no external files are changed.
        Console.WriteLine(root);
    }
}
