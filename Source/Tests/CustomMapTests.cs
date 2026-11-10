using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AlpineQuestPort;

class CustomMapTests {
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Field<T>(object value,string name){return (T)value.GetType().GetField(name,Flags).GetValue(value);}
    static object Call(object value,string name,params object[] args){return value.GetType().GetMethod(name,Flags).Invoke(value,args);}
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
    static MapSource Source(){return new MapSource{Id="custom-test",UserDefined=true,Group="Мои карты",Name="Проверочная карта",Template="https://tiles.example.invalid/{z}/{x}/{y}.png",MinZoom=0,MaxZoom=19,Language="ru",Attribution="Тестовый источник"};}
    static void Rejected(MapSource source,string name){bool bad=false;try{UserMapCatalog.Validate(source);}catch(InvalidDataException){bad=true;}Check(bad,name);}
    static void Probe(byte[] body,int status,bool success){
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;string request="";
        var server=Task.Run(delegate{using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream()){var header=new StringBuilder();while(!header.ToString().EndsWith("\r\n\r\n")){int b=stream.ReadByte();if(b<0)break;header.Append((char)b);}request=header.ToString();byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 "+status+" Test\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n\r\n");stream.Write(response,0,response.Length);stream.Write(body,0,body.Length);}});
        try{var source=Source();source.Template="http://127.0.0.1:"+port+"/{z}/{x}/{y}.png";source.Referer="https://example.invalid/";bool loaded=false;using(var token=new CancellationTokenSource(4000)){try{using(var image=MapSourceProbe.Load(source,new GeoPoint(0,0),2,token.Token).GetAwaiter().GetResult()){loaded=image.Width==256&&image.GetPixel(5,5).R==255;}}catch(InvalidDataException){}}Check(loaded==success,"probe response HTTP "+status+" success="+success);server.Wait(1000);Check(request.StartsWith("GET /2/2/2.png")&&request.Contains("Referer: https://example.invalid/"),"probe requests center tile and supplied Referer");}finally{listener.Stop();}
    }
    [STAThread]static int Main(string[] args){try{Application.EnableVisualStyles();string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);var source=Source();UserMapCatalog.Validate(source);Check(source.TileUrl(3,2,4).EndsWith("/3/2/4.png"),"XYZ URL");source.Template="https://{s}.example.invalid/{z}/{x}/{-y}.png?lang={lang}";source.Subdomains="a,b,c";UserMapCatalog.Validate(source);Check(source.TileUrl(3,2,4)=="https://a.example.invalid/3/2/3.png?lang=ru","TMS subdomains and language");string key=source.CacheKey;source.Subdomains="d,e";Check(source.CacheKey!=key,"custom subdomains isolate cache");source.Template="https://example.invalid/{q}.png";Check(source.TileUrl(3,2,4)=="https://example.invalid/210.png","quadkey URL");
        source=Source();source.Template="https://example.invalid/map";Rejected(source,"reject webpage URL");source=Source();source.Template="file:///C:/{z}/{x}/{y}.png";Rejected(source,"reject non-HTTP URL");source=Source();source.Template+="?key={unknown}";Rejected(source,"reject unknown placeholder");source=Source();source.MinZoom=20;source.MaxZoom=2;Rejected(source,"reject invalid zoom range");source=Source();source.Template="https://{s}.example.invalid/{z}/{x}/{y}";Rejected(source,"require subdomains when used");
        source=Source();source.Subdomains="a,b";source.Referer="https://example.invalid/";string file=Path.Combine(output,"custom.xml");UserMapCatalog.Save(file,new[]{source});var loaded=UserMapCatalog.Load(file);Check(loaded.Count==1&&loaded[0].Referer==source.Referer&&loaded[0].Subdomains==source.Subdomains&&loaded[0].UserDefined,"source settings persist");
        string old=File.ReadAllText(file);source.MinZoom=21;bool failed=false;try{UserMapCatalog.Save(file,new[]{source});}catch(InvalidDataException){failed=true;}Check(failed&&File.ReadAllText(file)==old,"invalid edit leaves saved catalogue unchanged");File.WriteAllText(file,"<!DOCTYPE maps [<!ENTITY x SYSTEM 'file:///secret'>]><maps>&x;</maps>");failed=false;try{UserMapCatalog.Load(file);}catch(System.Xml.XmlException){failed=true;}Check(failed,"custom catalogue rejects external XML entities");
        byte[] png;using(var bitmap=new Bitmap(256,256))using(var stream=new MemoryStream()){using(var g=Graphics.FromImage(bitmap))g.Clear(Color.Red);bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png);png=stream.ToArray();}Probe(png,200,true);Probe(Encoding.UTF8.GetBytes("<html>Not a tile</html>"),200,false);Probe(new byte[0],404,false);
        string folder=Path.Combine(output,"workspace-"+Guid.NewGuid().ToString("N"));using(var form=new MainForm(null,true,folder)){form.Show();Application.DoEvents();source=Source();Call(form,"SaveCustomMap",source);var sources=Field<List<MapSource>>(form,"sources");Check(sources.Any(s=>s.Id==source.Id),"new source enters shared base/layer catalog");
            var changed=source.Copy();changed.Name="Изменённая карта";Call(form,"SaveCustomMap",changed);Check(sources.Count(s=>s.Id==source.Id)==1&&sources.First(s=>s.Id==source.Id).Name==changed.Name,"editing replaces entry without duplicates");
            var duplicate=source.Copy();duplicate.Id="custom-duplicate";failed=false;try{Call(form,"SaveCustomMap",duplicate);}catch(TargetInvocationException e){failed=e.InnerException is InvalidDataException;}Check(failed,"duplicate URL rejected");
            using(var catalog=new MapsDialog(sources,changed,delegate{},s=>Call(form,"SaveCustomMap",s),s=>Call(form,"DeleteCustomMap",s),new GeoPoint(0,0),2)){catalog.Show();Application.DoEvents();using(var image=new Bitmap(catalog.Width,catalog.Height)){catalog.DrawToBitmap(image,new Rectangle(0,0,catalog.Width,catalog.Height));image.Save(Path.Combine(output,"catalog.png"));}catalog.Close();}
            using(var dialog=new CustomMapDialog(changed,new GeoPoint(0,0),2)){dialog.Show();Application.DoEvents();Check(dialog.ReadValue().Template==changed.Template,"editor round trip");using(var image=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new Rectangle(0,0,dialog.Width,dialog.Height));image.Save(Path.Combine(output,"custom-map.png"));}dialog.Close();}
            var status=form.Controls.OfType<StatusStrip>().Single();Check(!status.Items.Cast<ToolStripItem>().Any(item=>item.Text.Contains("EPSG:")),"duplicated footer coordinates removed");form.Close();}
        using(var form=new MainForm(null,true,folder)){var sources=Field<List<MapSource>>(form,"sources");Check(sources.Any(s=>s.Name=="Изменённая карта"),"custom source survives restart");Call(form,"DeleteCustomMap",sources.Single(s=>s.UserDefined));Check(UserMapCatalog.Load(Path.Combine(folder,"custom-maps.xml")).Count==0,"custom source deletion persists");}
        Console.WriteLine("PASS: custom sources complete");return 0;
    }catch(Exception e){Console.WriteLine(e);return 1;}}
}

