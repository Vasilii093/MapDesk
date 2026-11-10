using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace AlpineQuestPort
{
    public sealed class MapSource
    {
        public string Id,Name,Group,Attribution,Template,Referer;
        public string Subdomains="";
        public bool UserDefined;
        public int MinZoom=1,MaxZoom=19;
        public bool DiskCache=true;
        public string Language="x-local";
        public string CacheKey { get { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Template+"|"+Language+(Template.Contains("{s}")?"|"+Subdomains:"")))).Replace("-","").Substring(0,20); } }
        public override string ToString(){return Group+" · "+Name;}
        public string TileUrl(int z,int x,int y) {
            if(z<0||z>20)throw new InvalidDataException("Уровень источника должен быть от 0 до 20.");
            string quad="";for(int i=z;i>0;i--){int b=1<<(i-1),d=0;if((x&b)!=0)d++;if((y&b)!=0)d+=2;quad+=(char)('0'+d);}
            string s=Template.Replace("{z}",z.ToString(CultureInfo.InvariantCulture)).Replace("{x}",x.ToString(CultureInfo.InvariantCulture)).Replace("{y}",y.ToString(CultureInfo.InvariantCulture)).Replace("{-y}",((1<<z)-1-y).ToString(CultureInfo.InvariantCulture)).Replace("{q}",quad).Replace("{lang}",Uri.EscapeDataString(Language));
            if(s.Contains("{s}")){var domains=(Subdomains??"").Split(new[]{',',';',' '},StringSplitOptions.RemoveEmptyEntries);if(domains.Length==0)throw new InvalidDataException("Укажите поддомены для {s}, например a,b,c.");s=s.Replace("{s}",domains[(int)(((long)x+y)%domains.Length)]);}
            Uri uri;if(!Uri.TryCreate(s,UriKind.Absolute,out uri)||uri.Scheme!=Uri.UriSchemeHttps&&uri.Scheme!=Uri.UriSchemeHttp||s.Contains("{"))throw new InvalidDataException("Неподдерживаемый шаблон источника карты.");return s;
        }
        public MapSource Copy(){return (MapSource)MemberwiseClone();}
        public static List<MapSource> BuiltIn(){return new List<MapSource>{
            new MapSource{Id="esri-imagery",Group="Esri",Name="World Imagery",Attribution="Esri World Imagery · Esri, Maxar, Earthstar Geographics, and the GIS User Community",Template="https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",MaxZoom=19},
            new MapSource{Id="google-roads-ru",Group="Google",Name="Roads Layer (Russian)",Attribution="Google Maps · © Google",Template="https://mt1.google.com/vt/lyrs=h&hl={lang}&x={x}&y={y}&z={z}",Language="ru",DiskCache=false,MaxZoom=20},
            new MapSource{Id="esri-reference",Group="Esri",Name="Reference / Boundaries and Places",Attribution="Esri, HERE, Garmin and contributors",Template="https://services.arcgisonline.com/ArcGIS/rest/services/Reference/World_Boundaries_and_Places/MapServer/tile/{z}/{y}/{x}",MaxZoom=19},
            new MapSource{Id="google-local",Group="Google",Name="Maps (Localized)",Attribution="Google Maps · © Google",Template="https://mt1.google.com/vt/lyrs=m&hl={lang}&x={x}&y={y}&z={z}",DiskCache=false},
            new MapSource{Id="google-satellite",Group="Google",Name="Satellite",Attribution="Google · Image credits are supplied by Google",Template="https://mt1.google.com/vt/lyrs=s&x={x}&y={y}&z={z}",DiskCache=false,MaxZoom=20},
            new MapSource{Id="google-hybrid",Group="Google",Name="Hybrid",Attribution="Google Maps · © Google",Template="https://mt1.google.com/vt/lyrs=y&hl={lang}&x={x}&y={y}&z={z}",DiskCache=false,MaxZoom=20},
            new MapSource{Id="osm",Group="OpenStreetMap",Name="Standard",Attribution="© OpenStreetMap contributors · openstreetmap.org/copyright",Template="https://tile.openstreetmap.org/{z}/{x}/{y}.png"},
            new MapSource{Id="esri-topo",Group="Esri",Name="World Topographic",Attribution="Esri · HERE, Garmin, USGS, Intermap, INCREMENT P, NRCAN and contributors",Template="https://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}"}
        };}
        public static List<MapSource> LoadAqx(string path,out string report){
            var result=new List<MapSource>();var skipped=new List<string>();
            using(var reader=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4*1024*1024})){
                var doc=XDocument.Load(reader);if(doc.Root==null||doc.Root.Name.LocalName!="aqx")throw new InvalidDataException("Это не AQX-каталог.");
                Func<XElement,string,string> value=(e,n)=>{var v=e.Elements().FirstOrDefault(t=>t.Name.LocalName==n);return v==null?"":v.Value.Trim();};
                string group=value(doc.Root,"name");var parameters=doc.Root.Elements("param").Where(p=>p.Attribute("name")!=null).ToDictionary(p=>(string)p.Attribute("name"),p=>p.Value);
                foreach(var source in doc.Root.Elements().Where(s=>s.Name.LocalName=="source")){
                    string name=value(source,"name");if((string)source.Attribute("deprecated")=="true"){skipped.Add(name+": устаревший источник");continue;}

                    foreach(var level in source.Elements("zoom-levels")){
                        string url=value(level,"server");if(url.Length==0)continue;
                        foreach(var p in parameters)url=url.Replace("{$param:"+p.Key+"}",p.Value);
                        url=url.Replace("{$x}","{x}").Replace("{$y}","{y}").Replace("{$z}","{z}").Replace("{$q}","{q}").Replace("{$!y}","{-y}").Replace("{$s}","Galileo").Replace("{$ms}","");
                        if(url.Contains("{$")||url.Contains("{$!")){skipped.Add(name+": служебные параметры AlpineQuest");continue;}
                        var projectionNodes=new[]{doc.Root,source,level};
                        bool unsupportedProjection=projectionNodes.Any(node=>{
                            string projection=((string)node.Attribute("projection")??value(node,"projection")).Trim().ToUpperInvariant();
                            return projection.Length>0&&projection!="EPSG:3857"&&projection!="EPSG:900913"&&projection!="WEBMERCATOR"&&projection!="WEB MERCATOR"&&projection!="SPHERICAL MERCATOR";
                        });
                        if(unsupportedProjection){skipped.Add(name+": неподдерживаемая проекция тайлов");continue;}
                        var zooms=Regex.Matches((string)level.Attribute("z")??"1-19",@"\d+").Cast<Match>().Select(matchZoom=>Int32.Parse(matchZoom.Value)).ToList();
                        if(zooms.Count==0){skipped.Add(name+": неизвестные масштабы");continue;}
                        int min=zooms.Min(),max=zooms.Max();if(min<0||max>20){skipped.Add(name+": масштаб вне диапазона 0–20");continue;}
                        var m=new MapSource{Id=(string)source.Attribute("id")??name,Name=name,Group=group,Template=url,Referer=value(level,"referer"),Attribution=value(source,"copyright"),MinZoom=min,MaxZoom=max};
                        if(String.IsNullOrWhiteSpace(m.Attribution))m.Attribution=value(doc.Root,"description");
                        m.DiskCache=url.IndexOf("google",StringComparison.OrdinalIgnoreCase)<0;
                        try{m.TileUrl(min,0,0);result.Add(m);}catch(Exception){skipped.Add(name+": неподдерживаемый URL");}
                    }
                }
                foreach(var c in doc.Root.Elements("composite-source"))skipped.Add(value(c,"name")+": составной источник");
            }
            report="Импортировано вариантов: "+result.Count+(skipped.Count==0?"":"\nПропущено:\n"+String.Join("\n",skipped.Distinct().Take(18)));return result;
        }
    }
    public static class UserMapCatalog {
        public static void Validate(MapSource source){
            if(source==null||String.IsNullOrWhiteSpace(source.Name)||source.Name.Length>160)throw new InvalidDataException("Введите название карты (до 160 символов).");
            if(source.MinZoom<0||source.MaxZoom>20||source.MinZoom>source.MaxZoom)throw new InvalidDataException("Укажите диапазон масштабов от 0 до 20, минимум не больше максимума.");
            string template=source.Template??"";
            if(template.Length>4096||(!template.Contains("{q}")&&!(template.Contains("{z}")&&template.Contains("{x}")&&(template.Contains("{y}")||template.Contains("{-y}")))))throw new InvalidDataException("Нужен URL тайлов с {z}, {x}, {y} (или {-y}) либо {q}; адрес сайта не подходит.");
            foreach(string domain in (source.Subdomains??"").Split(new[]{',',';',' '},StringSplitOptions.RemoveEmptyEntries))if(!Regex.IsMatch(domain,@"^[a-zA-Z0-9-]{1,63}$"))throw new InvalidDataException("Поддомены: буквы, цифры и дефис; разделитель — запятая.");
            string url=source.TileUrl(source.MinZoom,0,0);var uri=new Uri(url);if(uri.UserInfo.Length>0||uri.Fragment.Length>0)throw new InvalidDataException("URL не должен содержать логин/пароль перед адресом или фрагмент #.");
            if(!String.IsNullOrWhiteSpace(source.Referer)){Uri reference;if(!Uri.TryCreate(source.Referer,UriKind.Absolute,out reference)||(reference.Scheme!="https"&&reference.Scheme!="http")||reference.UserInfo.Length>0)throw new InvalidDataException("Referer должен быть адресом HTTP или HTTPS.");}
        }
        public static List<MapSource> Load(string path){return AtomicSettings.Recover(path,LoadCore);}
        static List<MapSource> LoadCore(string path){
            if(!File.Exists(path))return new List<MapSource>();
            using(var reader=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2*1024*1024})){
                var root=XElement.Load(reader);if(root.Name!="maps")throw new InvalidDataException("Неизвестный формат пользовательских карт.");var result=new List<MapSource>();
                foreach(var e in root.Elements("map")){var source=new MapSource{Id=(string)e.Attribute("id"),Name=(string)e.Element("name"),Template=(string)e.Element("url"),Attribution=(string)e.Element("credits")??"",Referer=(string)e.Element("referer")??"",Subdomains=(string)e.Element("subdomains")??"",Language=(string)e.Element("language")??"ru",MinZoom=(int)e.Element("min"),MaxZoom=(int)e.Element("max"),UserDefined=true,Group="Мои карты"};Validate(source);if(String.IsNullOrEmpty(source.Id)||result.Any(s=>s.Id==source.Id))throw new InvalidDataException("Повторяющийся или пустой идентификатор карты.");result.Add(source);if(result.Count>200)throw new InvalidDataException("Допускается до 200 пользовательских карт.");}return result;
            }
        }
        public static void Save(string path,IEnumerable<MapSource> maps){var list=maps.ToList();if(list.Count>200)throw new InvalidDataException("Допускается до 200 пользовательских карт.");foreach(var s in list)Validate(s);
            var root=new XElement("maps",list.Select(s=>new XElement("map",new XAttribute("id",s.Id),new XElement("name",s.Name),new XElement("url",s.Template),new XElement("credits",s.Attribution??""),new XElement("referer",s.Referer??""),new XElement("subdomains",s.Subdomains??""),new XElement("language",s.Language??"ru"),new XElement("min",s.MinZoom),new XElement("max",s.MaxZoom))));
            AtomicSettings.SaveXml(path,root);
        }
    }
}
