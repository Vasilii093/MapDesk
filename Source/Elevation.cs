using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AlpineQuestPort {
 public sealed class ElevationTile:IDisposable {public Bitmap Image;public float[] Heights;public void Dispose(){if(Image!=null)Image.Dispose();}}
 public sealed class ElevationStore:IDisposable {
  public readonly TileStore Tiles;
  readonly Dictionary<string,ElevationTile> memory=new Dictionary<string,ElevationTile>();
  readonly Queue<string> order=new Queue<string>();
  readonly object gate=new object();
  readonly HashSet<string> pending=new HashSet<string>();
  readonly Queue<KeyValuePair<string,ElevationTile>> completed=new Queue<KeyValuePair<string,ElevationTile>>();
  bool disposed;readonly bool render;
  public event EventHandler Changed;
  public int PendingBuilds {get{lock(gate)return pending.Count;}}
  public ElevationStore(string cache,bool colored=false){render=colored;Tiles=new TileStore(cache,null,new MapSource{Id="terrain-dem",Name="Terrain Tiles",Group="Высоты",MinZoom=0,MaxZoom=14,Template="https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png",Attribution="Terrain Tiles / Mapzen · AWS Open Data"});Tiles.ForceDiskCache=true;Tiles.PreserveQueuedRequests=true;Tiles.FailureRetrySeconds=10;}
  public static double Decode(Color c){return c.R*256+c.G+c.B/256.0-32768;}
  static readonly double[] stops={-11000,0,200,500,1000,2000,4000,9000};
  static readonly Color[] colors={Color.FromArgb(21,60,115),Color.FromArgb(64,140,130),Color.FromArgb(114,173,91),Color.FromArgb(182,196,104),Color.FromArgb(222,202,127),Color.FromArgb(179,140,103),Color.FromArgb(160,148,144),Color.White};
  public static Color Tint(double h){int i=0;while(i<stops.Length-2&&h>stops[i+1])i++;double f=Math.Max(0,Math.Min(1,(h-stops[i])/(stops[i+1]-stops[i])));return Color.FromArgb((int)(colors[i].R+(colors[i+1].R-colors[i].R)*f),(int)(colors[i].G+(colors[i+1].G-colors[i].G)*f),(int)(colors[i].B+(colors[i+1].B-colors[i].B)*f));}
  // The UI owns decoded images. Workers publish completed images but never evict ones being painted.
  void Drain(){lock(gate){while(completed.Count>0){var item=completed.Dequeue();pending.Remove(item.Key);while(memory.Count>=128){string oldest=order.Dequeue();memory[oldest].Dispose();memory.Remove(oldest);}memory.Add(item.Key,item.Value);order.Enqueue(item.Key);}}}
  public ElevationTile Get(int z,int x,int y){if(disposed)return null;Drain();int n=1<<z;x=((x%n)+n)%n;string key=z+"/"+x+"/"+y;ElevationTile tile;if(memory.TryGetValue(key,out tile))return tile;
   lock(gate){if(disposed||pending.Contains(key)||pending.Count>=2)return null;}
   var raw=Tiles.GetTile(z,x,y);if(raw==null)return null;if(raw.Width!=256||raw.Height!=256){raw.Dispose();return null;}
   lock(gate){if(disposed){raw.Dispose();return null;}pending.Add(key);}
   BackgroundJobs.Run("dem.decode",delegate{ElevationTile built=null;try{using(raw)built=Build(raw,z,y,render);}catch(Exception ex){Diagnostics.Record("dem.decode",ex);}
    lock(gate){if(disposed){if(built!=null)built.Dispose();pending.Remove(key);return;}if(built!=null)completed.Enqueue(new KeyValuePair<string,ElevationTile>(key,built));else pending.Remove(key);}
    var changed=Changed;if(changed!=null)changed(this,EventArgs.Empty);
   });return null;
  }
  public static ElevationTile Build(Image raw,int z,int y,bool render=true){var result=new ElevationTile{Image=render?new Bitmap(256,256,PixelFormat.Format32bppArgb):null,Heights=new float[65536]};using(var b=new Bitmap(256,256,PixelFormat.Format32bppArgb)){using(var g=Graphics.FromImage(b))g.DrawImageUnscaled(raw,0,0);var data=b.LockBits(new Rectangle(0,0,256,256),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);var bytes=new byte[256*256*4];try{Marshal.Copy(data.Scan0,bytes,0,bytes.Length);}finally{b.UnlockBits(data);}for(int i=0;i<65536;i++){float h=bytes[i*4+2]*256+bytes[i*4+1]+bytes[i*4]/256f-32768;result.Heights[i]=h<=-32768?float.NaN:h;}
   if(render){double lat=GeoMath.PixelToLat(y*256+128,z),resolution=Math.Max(0.05,Math.Cos(lat*Math.PI/180)*40075016.686/(256*(1<<z)));
   for(int row=0;row<256;row++)for(int col=0;col<256;col++){int i=row*256+col;float h=result.Heights[i];if(float.IsNaN(h)){bytes[i*4+3]=0;continue;}double dx=(result.Heights[row*256+Math.Min(255,col+1)]-result.Heights[row*256+Math.Max(0,col-1)])/(2*resolution),dy=(result.Heights[Math.Min(255,row+1)*256+col]-result.Heights[Math.Max(0,row-1)*256+col])/(2*resolution);double light=0.65+0.35*(1-dx*0.5-dy*0.5)/Math.Sqrt(1+dx*dx+dy*dy);if(Double.IsNaN(light))light=1;light=Math.Max(0.35,Math.Min(1.15,light));Color c=Tint(h);bytes[i*4]=(byte)Math.Min(255,c.B*light);bytes[i*4+1]=(byte)Math.Min(255,c.G*light);bytes[i*4+2]=(byte)Math.Min(255,c.R*light);bytes[i*4+3]=255;}
   var dest=result.Image.LockBits(new Rectangle(0,0,256,256),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);try{Marshal.Copy(bytes,0,dest.Scan0,bytes.Length);}finally{result.Image.UnlockBits(dest);}}}
   return result;
  }
  public int LastSampleZoom {get;private set;}
  public double? AtAvailable(GeoPoint point,int zoom){int preferred=Math.Max(0,Math.Min(14,zoom));LastSampleZoom=preferred;var value=At(point,preferred);if(value.HasValue)return value;for(int level=14;level>=0;level--){if(level==preferred)continue;int x=(int)(GeoMath.LonToPixel(point.Lon,level)/256),y=(int)Math.Max(0,Math.Min((1<<level)-1,GeoMath.LatToPixel(point.Lat,level)/256));x=((x%(1<<level))+(1<<level))%(1<<level);if(!Tiles.HasDiskTile(level,x,y))continue;value=At(point,level);if(value.HasValue){LastSampleZoom=level;return value;}}return null;}
  public double? At(GeoPoint p,int zoom){int z=Math.Max(0,Math.Min(14,zoom));double px=GeoMath.LonToPixel(p.Lon,z),py=Math.Max(0,Math.Min(256*(1<<z)-0.001,GeoMath.LatToPixel(p.Lat,z)));var t=Get(z,(int)Math.Floor(px/256),(int)Math.Floor(py/256));if(t==null)return null;float h=t.Heights[((int)py%256)*256+((int)px%256+256)%256];return float.IsNaN(h)?(double?)null:h;}
  public void Dispose(){lock(gate){if(disposed)return;disposed=true;while(completed.Count>0)completed.Dequeue().Value.Dispose();}Tiles.Dispose();foreach(var t in memory.Values)t.Dispose();memory.Clear();}
 }
}


