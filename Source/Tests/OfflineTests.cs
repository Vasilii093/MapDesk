using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Windows.Forms;
using AlpineQuestPort;
class OfflineTests {
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS: "+name);}
 sealed class Server:IDisposable {
  readonly TcpListener listener=new TcpListener(IPAddress.Loopback,0);readonly byte[] png;volatile bool stopped;public int Count,Delay;public string Url;
  public Server(){using(var bmp=new Bitmap(256,256)){using(var g=Graphics.FromImage(bmp))g.Clear(Color.FromArgb(128,120,0));using(var memory=new MemoryStream()){bmp.Save(memory,System.Drawing.Imaging.ImageFormat.Png);png=memory.ToArray();}}listener.Start();Url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/{z}/{x}/{y}.png";Task.Run(delegate{while(!stopped){try{var c=listener.AcceptTcpClient();Task.Run(()=>Reply(c));}catch(SocketException){break;}}});}
  void Reply(TcpClient client){try{using(client)using(var stream=client.GetStream()){var header=new StringBuilder();while(!header.ToString().EndsWith("\r\n\r\n")){int value=stream.ReadByte();if(value<0)return;header.Append((char)value);}Interlocked.Increment(ref Count);Thread.Sleep(Delay);byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: "+png.Length+"\r\nCache-Control: max-age=0\r\nConnection: close\r\n\r\n");stream.Write(response,0,response.Length);stream.Write(png,0,png.Length);}}catch(IOException){}catch(ObjectDisposedException){}}
  public void Dispose(){stopped=true;listener.Stop();}
 }
 [STAThread]static int Main(string[] args){try{Application.EnableVisualStyles();var root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
  var a=new GeoPoint(55.751,37.621);var b=RangeRings.Destination(a,300,90);var p=TerrainProfile.Create(a,b,false,0,30);p.X[p.X.Length-1]=p.Length+1e-12;for(int i=0;i<p.Heights.Length;i++)p.Heights[i]=120;var result=RadioResult.Calculate(p,new[]{900.0},10,10,4.0/3);Check(result.Complete&&result.Radii[0].Last()==0,"roundoff beyond final distance produces zero endpoint radius");p.X[1]=-2;bool rejected=false;try{RadioResult.Calculate(p,new[]{900.0},10,10,4.0/3);}catch(ArgumentException){rejected=true;}Check(rejected,"materially invalid profile distances rejected");
  bool reproduced=false;for(int i=1;i<10000;i++){double length=10+i*.123456789;int count=(int)Math.Ceiling(length/30);if(length*count/count>length){reproduced=true;break;}}Check(reproduced,"old endpoint formula reproduces negative remaining distance");
  var plan=OfflinePlan.Create(a,b,12,14);Check(plan.Any(t=>t.Z==12)&&plan.Any(t=>t.Z==14),"all requested detail levels included");rejected=false;try{OfflinePlan.Create(new GeoPoint(-80,-170),new GeoPoint(80,-10),10,20);}catch(ArgumentException){rejected=true;}Check(rejected,"oversized area rejected before allocation");
  using(var server=new Server()){
   var source=new MapSource{Id="test",Name="Проверочный источник",Group="Карты",Template=server.Url,MinZoom=0,MaxZoom=20};string cache=Path.Combine(root,"tile-cache-v2",source.CacheKey);
   using(var store=new TileStore(cache,null,source)){store.OnlineEnabled=true;store.ForceDiskCache=true;Check(store.SaveOfflineTile(3,4,5,CancellationToken.None).Result,"tile downloaded and persisted");int requests=server.Count;Check(store.SaveOfflineTile(3,4,5,CancellationToken.None).Result&&server.Count==requests,"expired cached tile not re-fetched");}
   string file=Path.Combine(cache,"3","4","5.png");File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddDays(-3));Check(TileCacheInventory.Trim(Path.Combine(root,"tile-cache-v2"),1)==0&&File.Exists(file),"explicit offline tile survives quota cleanup");
   server.Delay=1200;using(var store=new TileStore(cache,null,source)){store.OnlineEnabled=true;store.ForceDiskCache=true;using(var cancel=new CancellationTokenSource(150)){bool cancelled=false;try{store.SaveOfflineTile(3,4,6,cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}Check(cancelled&&!File.Exists(Path.Combine(cache,"3","4","6.png")),"cancellation interrupts pending network without bogus tile");}}
   using(var store=new TileStore(cache,null,source)){store.OnlineEnabled=true;store.ForceDiskCache=true;server.Delay=0;Check(store.SaveOfflineTile(3,4,6,CancellationToken.None).Result,"cancelled tile resumes in next download");}
   server.Delay=1000;using(var engine=new RayProfileEngine(Path.Combine(root,"delayed-dem"))){var field=typeof(RayProfileEngine).GetField("elevation",BindingFlags.NonPublic|BindingFlags.Instance);var elevation=(ElevationStore)field.GetValue(engine);elevation.Tiles.Source.Template=server.Url;var options=new PointRadioOptions{Fresnel=true};engine.Step(a,b,options,true,DateTime.UtcNow);Check(engine.Radio==null,"Fresnel waits while DEM is missing");var until=DateTime.UtcNow.AddSeconds(12);while(DateTime.UtcNow<until&&engine.Radio==null){Thread.Sleep(25);engine.Step(a,b,options,true,DateTime.UtcNow);}Check(engine.Radio!=null&&engine.Radio.Complete,"delayed DEM automatically completes Fresnel without error");}
   using(var form=new OfflineDownloadForm(root,source,16,a,b)){form.Show();Application.DoEvents();using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,form.Width,form.Height));image.Save(Path.Combine(root,"download-dialog.png"));}form.Close();}
  }
  using(var tiles=new TileStore(Path.Combine(root,"selection"),null))using(var map=new MapCanvas(tiles){Dock=DockStyle.Fill})using(var window=new Form{Size=new Size(800,600)}){window.Controls.Add(map);window.Show();Application.DoEvents();map.GoTo(a,14);GeoPoint first=null,second=null;map.SelectOfflineRegion((x,y)=>{first=x;second=y;});var flags=BindingFlags.NonPublic|BindingFlags.Instance;typeof(MapCanvas).GetMethod("OnMouseDown",flags).Invoke(map,new object[]{new MouseEventArgs(MouseButtons.Left,1,200,180,0)});typeof(MapCanvas).GetMethod("OnMouseMove",flags).Invoke(map,new object[]{new MouseEventArgs(MouseButtons.Left,1,550,400,0)});using(var image=new Bitmap(map.Width,map.Height)){map.DrawToBitmap(image,map.ClientRectangle);image.Save(Path.Combine(root,"region-selection.png"));}typeof(MapCanvas).GetMethod("OnMouseUp",flags).Invoke(map,new object[]{new MouseEventArgs(MouseButtons.Left,1,550,400,0)});Application.DoEvents();Check(first!=null&&second!=null&&first.Lat<second.Lat&&first.Lon<second.Lon,"drag selection returns geographic rectangle");Check(map.Document.Zones.Count==0&&!map.HasDraft,"offline selection does not create map objects");map.SelectOfflineRegion((x,y)=>{throw new Exception("Cancelled selection fired");});map.CancelDraft();Check(map.Cursor==Cursors.Hand,"Escape cancels offline selection");window.Close();}
  var captured=WorkspaceArchive.Capture(root,DocumentSnapshot.Capture(new MapDocument()));string archive=Path.Combine(root,"offline.mapdesk");WorkspaceArchive.Export(archive,root,captured,true);string restored=WorkspaceArchive.Restore(archive,Path.Combine(root,"restored"));Check(Directory.GetFiles(restored,"*.keep",SearchOption.AllDirectories).Length>0,"backup preserves offline protection markers");
  Console.WriteLine("ALL 0.34 CHECKS PASSED");return 0;
 }catch(Exception ex){Console.WriteLine(ex);return 1;}}
}

