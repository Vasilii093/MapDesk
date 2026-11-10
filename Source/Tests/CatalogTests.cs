using System;
using System.IO;
using System.Linq;
using AlpineQuestPort;
class CatalogTests {
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS: "+label);}
 static int Main(){string temp=Path.Combine(Path.GetTempPath(),"MapDesk-catalog-"+Guid.NewGuid()+".aqx");try{
 var maps=MapSource.BuiltIn();var esri=maps.Find(m=>m.Id=="esri-imagery");var google=maps.Find(m=>m.Id=="google-local");
 Check(esri.TileUrl(3,4,2).EndsWith("/3/2/4"),"Esri y/x order");
 Check(google.TileUrl(3,4,2).Contains("hl=x-local"),"Google local language");
 Check(!google.DiskCache,"Google disk cache disabled");string key=google.CacheKey;google.Language="ru";Check(key!=google.CacheKey,"language cache isolation");Check(esri.CacheKey!=google.CacheKey,"provider cache isolation");
 File.WriteAllText(temp,"<aqx><name>Test</name><source id='valid'><name>XYZ</name><zoom-levels z='1-18'><server><![CDATA[https://example.com/{$z}/{$x}/{$y}]]></server></zoom-levels></source><source projection='EPSG:3395'><name>Wrong projection</name><zoom-levels z='1-18'><server>https://example.com/{$z}/{$x}/{$y}</server></zoom-levels></source><source><name>Unknown</name><zoom-levels z='1-18'><server>https://example.com/{$secret}</server></zoom-levels></source></aqx>");
 string report;var imported=MapSource.LoadAqx(temp,out report);Check(imported.Count==1,"reject unsupported projection and service variables");Check(imported[0].TileUrl(2,1,3)=="https://example.com/2/1/3","AQX XYZ translation");
 File.WriteAllText(temp,"<!DOCTYPE aqx [<!ENTITY x SYSTEM 'file:///secret'>]><aqx><name>&x;</name></aqx>");bool rejected=false;try{MapSource.LoadAqx(temp,out report);}catch(System.Xml.XmlException){rejected=true;}Check(rejected,"external XML entities rejected");return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}finally{if(File.Exists(temp))File.Delete(temp);}}
}
