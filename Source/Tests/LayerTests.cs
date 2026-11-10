using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AlpineQuestPort;

static class LayerTests {
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
    static void Tile(string root,Color color){Directory.CreateDirectory(Path.Combine(root,"2","2"));using(var b=new Bitmap(256,256)){using(var g=Graphics.FromImage(b))g.Clear(color);b.Save(Path.Combine(root,"2","2","1.png"),System.Drawing.Imaging.ImageFormat.Png);}}
    static Image Load(TileStore store,int z,int x,int y){for(int i=0;i<400;i++){var image=store.GetTile(z,x,y);if(image!=null)return image;Thread.Sleep(10);Application.DoEvents();}throw new Exception("Tile timed out: "+store.LastError);}
    public static void Run(string output){
        byte[] png;using(var b=new Bitmap(256,256))using(var stream=new MemoryStream()){using(var g=Graphics.FromImage(b))g.Clear(Color.Red);b.Save(stream,System.Drawing.Imaging.ImageFormat.Png);png=stream.ToArray();}
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;var server=Task.Run(delegate{using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream()){int matched=0;while(matched<4){int c=stream.ReadByte();if(c<0)break;matched=c==new[]{13,10,13,10}[matched]?matched+1:0;}byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nCache-Control: no-store\r\nContent-Length: "+png.Length+"\r\nConnection: close\r\n\r\n");stream.Write(header,0,header.Length);stream.Write(png,0,png.Length);}});
        string cache=Path.Combine(output,"force-cache-"+Guid.NewGuid().ToString("N"));var source=new MapSource{Name="Forced cache",Template="http://127.0.0.1:"+port+"/{z}/{x}/{y}.png",DiskCache=false};
        try{using(var tiles=new TileStore(cache,null,source){ForceDiskCache=true}){tiles.OnlineEnabled=true;using(var image=Load(tiles,2,2,1))Check(image.Width==256,"forced cache receives image");}Check(File.Exists(Path.Combine(cache,"2","2","1.png")),"forced cache persists no-store source before display");}finally{listener.Stop();server.Wait(2000);}
        using(var reopened=new TileStore(cache,null,source){ForceDiskCache=true})using(var image=Load(reopened,2,2,1))Check(((Bitmap)image).GetPixel(0,0).R==255,"fresh offline store reads persisted tile with server stopped");
        var retryListener=new TcpListener(IPAddress.Loopback,0);retryListener.Start();int retryPort=((IPEndPoint)retryListener.LocalEndpoint).Port;
        var retryServer=Task.Run(delegate{for(int attempt=0;attempt<2;attempt++)using(var client=retryListener.AcceptTcpClient())using(var stream=client.GetStream()){int matched=0;while(matched<4){int c=stream.ReadByte();if(c<0)break;matched=c==new[]{13,10,13,10}[matched]?matched+1:0;}byte[] body=attempt==0?new byte[0]:png;byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 "+(attempt==0?"503 Service Unavailable":"200 OK")+"\r\nContent-Type: image/png\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n\r\n");stream.Write(header,0,header.Length);stream.Write(body,0,body.Length);}});
        try{using(var store=new TileStore(Path.Combine(output,"retry-"+Guid.NewGuid().ToString("N")),null,new MapSource{Name="Retry",Template="http://127.0.0.1:"+retryPort+"/{z}/{x}/{y}"}){FailureRetrySeconds=1,PreserveQueuedRequests=true}){store.OnlineEnabled=true;using(var image=Load(store,2,2,1))Check(image!=null,"transient HTTP failure retries and recovers without moving map");}}finally{retryListener.Stop();retryServer.Wait(2000);}
        string data=Path.Combine(output,"layer-test");var baseSource=new MapSource{Name="Blue base",Template="https://base.invalid/{z}/{x}/{y}",MinZoom=0,MaxZoom=2};var overlay=new MapSource{Name="Red transparent",Template="https://overlay.invalid/{z}/{x}/{y}",MinZoom=0,MaxZoom=2};
        string baseCache=Path.Combine(data,"tile-cache-v2",baseSource.CacheKey);Tile(baseCache,Color.Blue);Tile(Path.Combine(data,"tile-cache-v2",overlay.CacheKey),Color.FromArgb(128,255,0,0));
        using(var tiles=new TileStore(baseCache,null,baseSource))using(var map=new MapCanvas(tiles){Size=new Size(800,600)}){
            using(var image=Load(tiles,2,2,1)){}map.Settings.DataCards=false;map.Settings.ScaleBar=false;map.Settings.RangeRings=false;map.Settings.Crosshair=false;map.Settings.Elevation=false;map.GoTo(new GeoPoint(GeoMath.PixelToLat(384,2),GeoMath.PixelToLon(640,2)),2);
            map.Layers.Add(new LayerDefinition{Source=overlay});map.ConfigureLayers(data);
            using(var ready=new TileStore(Path.Combine(data,"tile-cache-v2",overlay.CacheKey),null,overlay))using(var image=Load(ready,2,2,1)){}
            for(int i=0;i<30;i++){using(var warm=new Bitmap(800,600))map.DrawToBitmap(warm,map.ClientRectangle);Application.DoEvents();Thread.Sleep(10);}
            foreach(bool gpu in new[]{false,true})foreach(int alpha in new[]{100,50,0}){
                map.Settings.Gpu=gpu;map.Settings.Perspective=true;map.Settings.Rotation=35;map.Layers[0].Opacity=alpha;map.ConfigureLayers(data);using(var image=new Bitmap(800,600)){map.DrawToBitmap(image,map.ClientRectangle);var c=image.GetPixel(400,300);int expected=(int)Math.Round(128*alpha/100.0);Check(Math.Abs(c.R-expected)<=3&&Math.Abs(c.B-(255-expected))<=3&&c.A==255,"layer alpha composition with rotation/perspective GPU="+gpu+" opacity="+alpha);}
            }
            map.Layers[0].Opacity=75;map.Layers[0].Visible=false;var config=new LayerConfiguration{Layers=map.Layers};string file=Path.Combine(data,"layers.xml");config.Save(file);var restored=LayerConfiguration.Load(file);Check(restored.Layers.Count==1&&!restored.Layers[0].Visible&&restored.Layers[0].Opacity==75&&restored.Layers[0].Source.CacheKey==overlay.CacheKey,"layer order settings and source persistence");
            using(var dialog=new LayersDialog(map,MapSource.BuiltIn(),delegate{map.ConfigureLayers(data);})){
                dialog.Show();using(var image=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new Rectangle(0,0,dialog.Width,dialog.Height));image.Save(Path.Combine(output,"layers-dialog.png"));}dialog.Close();
            }
        }
        Tile(Path.Combine(data,"elevation-cache"),Color.FromArgb(128,100,0));using(var dem=new ElevationStore(Path.Combine(data,"elevation-cache"),true)){ElevationTile tile=null;for(int i=0;i<300&&tile==null;i++){tile=dem.Get(2,2,1);Thread.Sleep(10);}Check(tile!=null&&tile.Image!=null&&tile.Heights[0]==100,"colored DEM built in background with retained height matrix");}
        var settings=new ViewSettings{ForceTileCache=true,BaseVisible=false,BaseOpacity=35};string settingPath=Path.Combine(data,"settings.xml");settings.Save(settingPath);var loaded=ViewSettings.Load(settingPath);Check(loaded.ForceTileCache&&!loaded.BaseVisible&&loaded.BaseOpacity==35,"cache and base layer settings persist");
    }
    public static void Live(string output){
        string data=Path.Combine(output,"layers-live-"+Guid.NewGuid().ToString("N"));var source=MapSource.BuiltIn().Find(s=>s.Id=="google-roads-ru");int z=14,x=(int)(GeoMath.LonToPixel(37.62,z)/256),y=(int)(GeoMath.LatToPixel(55.75,z)/256);string cache=Path.Combine(data,source.CacheKey);
        using(var store=new TileStore(cache,null,source){ForceDiskCache=true}){store.OnlineEnabled=true;using(var image=Load(store,z,x,y)){int clear=0,ink=0;var b=(Bitmap)image;for(int yy=0;yy<b.Height;yy++)for(int xx=0;xx<b.Width;xx++){if(b.GetPixel(xx,yy).A==0)clear++;else ink++;}Check(clear>0&&ink>0,"live Google Roads Russian has transparent background and labels");image.Save(Path.Combine(output,"google-roads-russian.png"),System.Drawing.Imaging.ImageFormat.Png);}}
        using(var store=new TileStore(cache,null,source){ForceDiskCache=true})using(var image=Load(store,z,x,y))Check(image.Width==256,"Google Roads offline after store restart");
    }
}
