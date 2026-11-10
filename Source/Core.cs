using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace AlpineQuestPort
{
    public sealed class PointRadioOptions {
        public bool Enabled=true, Fresnel=true, Curvature=true;
        public int Count=1;
        public double HeightA=20,HeightB=12,K=4.0/3;
        public double[] Frequencies={900,2400,5800};
        public PointRadioOptions Clone(){var copy=(PointRadioOptions)MemberwiseClone();copy.Frequencies=(double[])Frequencies.Clone();return copy;}
        public void Validate(){if(Count<1||Count>3||Frequencies==null||Frequencies.Length!=3||Frequencies.Any(v=>!GeoMath.IsFinite(v)||v<1||v>300000)||!GeoMath.IsFinite(HeightA)||!GeoMath.IsFinite(HeightB)||HeightA<0||HeightB<0||HeightA>1000||HeightB>1000||!GeoMath.IsFinite(K)||K<.1||K>10)throw new InvalidDataException("Некорректные параметры Френеля точки.");}
        public XElement ToXml(XNamespace ns){Validate();return new XElement(ns+"radio",new XAttribute("enabled",Enabled),new XAttribute("fresnel",Fresnel),new XAttribute("curvature",Curvature),new XAttribute("count",Count),new XAttribute("heightA",HeightA),new XAttribute("heightB",HeightB),new XAttribute("k",K),new XAttribute("f1",Frequencies[0]),new XAttribute("f2",Frequencies[1]),new XAttribute("f3",Frequencies[2]));}
        public static PointRadioOptions FromXml(XElement e){if(e==null)return new PointRadioOptions();try{var o=new PointRadioOptions{Enabled=(bool)e.Attribute("enabled"),Fresnel=(bool)e.Attribute("fresnel"),Curvature=(bool)e.Attribute("curvature"),Count=(int)e.Attribute("count"),HeightA=(double)e.Attribute("heightA"),HeightB=(double)e.Attribute("heightB"),K=(double)e.Attribute("k"),Frequencies=new[]{(double)e.Attribute("f1"),(double)e.Attribute("f2"),(double)e.Attribute("f3")}};o.Validate();return o;}catch(Exception ex){throw new InvalidDataException("Некорректные параметры Френеля точки.",ex);}}
        public string Key(){return ToXml("urn:mapdesk:waypoint:1").ToString(SaveOptions.DisableFormatting);}
    }

    public sealed class GeoPoint
    {
        public double Lat;
        public double Lon;
        public double? Elevation;
        public string Name;
        public DateTimeOffset? Time;
        public int ColorRgb = 0x176B50;
        public string Group = "";
        private PointRadioOptions radio;
        public PointRadioOptions Radio {get{return radio??(radio=new PointRadioOptions());}set{radio=value;}}

        public GeoPoint() { Name = String.Empty; }
        public GeoPoint(double lat, double lon) : this() { Lat = lat; Lon = lon; }
    }

    public sealed class Track
    {
        public string Name;
        public bool IsRoute;
        public List<GeoPoint> Points { get; private set; }
        public Track() { Name = String.Empty; Points = new List<GeoPoint>(); }
    }

    public sealed class Zone
    {
        public string Name = "Зона";
        public string Kind = "Polygon";
        public int ColorRgb = 0x307CC0;
        public List<GeoPoint> Points = new List<GeoPoint>();
        public List<GeoPoint> Outline()
        {
            if (Kind == "Rectangle" && Points.Count == 2) return new List<GeoPoint> { Points[0], new GeoPoint(Points[0].Lat, Points[1].Lon), Points[1], new GeoPoint(Points[1].Lat, Points[0].Lon) };
            if (Kind == "Circle" && Points.Count == 2) {
                var result = new List<GeoPoint>(); double radius = GeoMath.DistanceMeters(Points[0], Points[1]) / GeoMath.EarthRadiusMeters;
                double lat = Points[0].Lat * Math.PI / 180, lon = Points[0].Lon * Math.PI / 180;
                for (int i=0;i<120;i++) { double b=i*Math.PI/60, a=Math.Asin(Math.Sin(lat)*Math.Cos(radius)+Math.Cos(lat)*Math.Sin(radius)*Math.Cos(b)); double l=lon+Math.Atan2(Math.Sin(b)*Math.Sin(radius)*Math.Cos(lat),Math.Cos(radius)-Math.Sin(lat)*Math.Sin(a)); result.Add(new GeoPoint(a*180/Math.PI,((l*180/Math.PI+540)%360)-180)); }
                return result;
            }
            return new List<GeoPoint>(Points);
        }
    }

    public sealed class MapDocument
    {
        public List<GeoPoint> Waypoints { get; private set; }
        public List<Track> Tracks { get; private set; }
        public List<Zone> Zones = new List<Zone>();
        public MapDocument() { Waypoints = new List<GeoPoint>(); Tracks = new List<Track>(); }
    }

    public static class GeoMath
    {
        public const double EarthRadiusMeters = 6371008.8;
        public const double MaxMercatorLatitude = 85.0511287798066;

        public static bool IsFinite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        public static bool IsValidCoordinate(double lat, double lon)
        {
            return IsFinite(lat) && IsFinite(lon) && lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180;
        }

        public static double WorldSize(int zoom)
        {
            if (zoom < 0 || zoom > 40) throw new ArgumentOutOfRangeException("zoom");
            return 256.0 * Math.Pow(2, zoom);
        }

        public static double LonToPixel(double lon, int zoom)
        {
            if (!IsFinite(lon)) throw new ArgumentOutOfRangeException("lon");
            return (lon + 180.0) / 360.0 * WorldSize(zoom);
        }

        public static double LatToPixel(double lat, int zoom)
        {
            if (!IsFinite(lat)) throw new ArgumentOutOfRangeException("lat");
            lat = Math.Max(-MaxMercatorLatitude, Math.Min(MaxMercatorLatitude, lat));
            double sin = Math.Sin(lat * Math.PI / 180.0);
            return (0.5 - Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * WorldSize(zoom);
        }

        public static double PixelToLon(double x, int zoom)
        {
            if (!IsFinite(x)) throw new ArgumentOutOfRangeException("x");
            return x / WorldSize(zoom) * 360 - 180;
        }

        public static double PixelToLat(double y, int zoom)
        {
            if (!IsFinite(y)) throw new ArgumentOutOfRangeException("y");
            double world = WorldSize(zoom);
            y = Math.Max(0, Math.Min(world, y));
            double n = Math.PI - 2 * Math.PI * y / world;
            return 180.0 / Math.PI * Math.Atan(Math.Sinh(n));
        }

        public static double NormalizeLongitude(double lon)
        {
            if (!IsFinite(lon)) throw new ArgumentOutOfRangeException("lon");
            return ((lon + 180) % 360 + 360) % 360 - 180;
        }

        public static double DistanceMeters(GeoPoint a, GeoPoint b)
        {
            if (a == null || b == null) throw new ArgumentNullException(a == null ? "a" : "b");
            if (!IsValidCoordinate(a.Lat, a.Lon) || !IsValidCoordinate(b.Lat, b.Lon))
                throw new ArgumentException("Coordinates are outside their valid range.");
            double latA = a.Lat * Math.PI / 180, latB = b.Lat * Math.PI / 180;
            double dLat = latB - latA, dLon = (b.Lon - a.Lon) * Math.PI / 180;
            double h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(latA) * Math.Cos(latB) * Math.Pow(Math.Sin(dLon / 2), 2);
            return 2 * EarthRadiusMeters * Math.Asin(Math.Sqrt(Math.Max(0, Math.Min(1, h))));
        }

        public static double TrackLengthMeters(Track track)
        {
            if (track == null) throw new ArgumentNullException("track");
            double sum = 0;
            for (int i = 1; i < track.Points.Count; i++) sum += DistanceMeters(track.Points[i - 1], track.Points[i]);
            return sum;
        }
    }

    public static class MapFiles
    {
        public const long MaxXmlBytes = 32L * 1024 * 1024;
        public const long MaxArchiveBytes = 128L * 1024 * 1024;
        private const int MaxPoints = 500000;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly XNamespace MapDesk = "urn:mapdesk:waypoint:1";
        private static readonly XNamespace Gpx = "http://www.topografix.com/GPX/1/1";
        private static readonly XNamespace Kml = "http://www.opengis.net/kml/2.2";
        private static readonly XNamespace Gx = "http://www.google.com/kml/ext/2.2";

        public static MapDocument Load(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", "path");
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if(extension==".ldk")return LdkReader.Load(path);
            if (extension != ".gpx" && extension != ".kml" && extension != ".kmz")
                throw new NotSupportedException("Supported files: GPX, KML and KMZ.");
            FileInfo info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("The map data file was not found.", path);
            long limit = extension == ".kmz" ? MaxArchiveBytes : MaxXmlBytes;
            if (info.Length > limit) throw new InvalidDataException("The file is too large to import safely.");
            XDocument xml;
            using (FileStream input = File.OpenRead(path))
            {
                if (extension == ".kmz")
                {
                    using (ZipArchive archive = new ZipArchive(input, ZipArchiveMode.Read))
                    {
                        if (archive.Entries.Count > 4096) throw new InvalidDataException("The KMZ contains too many entries.");
                        List<ZipArchiveEntry> entries = archive.Entries.Where(e => e.FullName.EndsWith(".kml", StringComparison.OrdinalIgnoreCase)).ToList();
                        ZipArchiveEntry entry = entries.FirstOrDefault(e => e.FullName.Equals("doc.kml", StringComparison.OrdinalIgnoreCase)) ?? entries.FirstOrDefault();
                        if (entry == null) throw new InvalidDataException("The KMZ does not contain a KML document.");
                        if (entry.Length > MaxXmlBytes) throw new InvalidDataException("The KML inside the archive is too large.");
                        using (Stream content = entry.Open())
                        using (LimitedReadStream bounded = new LimitedReadStream(content, MaxXmlBytes)) xml = ReadXml(bounded);
                    }
                }
                else xml = ReadXml(input);
            }
            if (xml.Root == null) throw new InvalidDataException("The document is empty.");
            if (extension == ".gpx" && xml.Root.Name.LocalName == "gpx") return ReadGpx(xml.Root);
            if ((extension == ".kml" || extension == ".kmz") && xml.Root.Name.LocalName == "kml") return ReadKml(xml.Root);
            throw new InvalidDataException("The document does not match its GPX/KML file extension.");
        }

        private static XDocument ReadXml(Stream stream)
        {
            XmlReaderSettings settings = new XmlReaderSettings();
            settings.DtdProcessing = DtdProcessing.Prohibit;
            settings.XmlResolver = null;
            settings.MaxCharactersInDocument = MaxXmlBytes;
            settings.MaxCharactersFromEntities = 1024;
            settings.IgnoreComments = true;
            settings.IgnoreProcessingInstructions = true;
            using (XmlReader reader = XmlReader.Create(stream, settings)) return XDocument.Load(reader, LoadOptions.None);
        }

        private static IEnumerable<XElement> Children(XElement parent, string name)
        {
            return parent.Elements().Where(e => e.Name.LocalName == name);
        }

        private static string ChildText(XElement parent, string name)
        {
            XElement element = Children(parent, name).FirstOrDefault();
            return element == null ? String.Empty : element.Value.Trim();
        }

        private static double Number(string text, string label)
        {
            double value;
            if (!Double.TryParse(text, NumberStyles.Float, Invariant, out value) || !GeoMath.IsFinite(value))
                throw new InvalidDataException("Invalid " + label + ": a finite decimal number is required.");
            return value;
        }

        private static DateTimeOffset? Timestamp(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            DateTimeOffset time;
            if (!DateTimeOffset.TryParse(text, Invariant, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out time))
                throw new InvalidDataException("Invalid point timestamp.");
            return time;
        }

        private static void ValidatePoint(GeoPoint point)
        {
            if (point == null || !GeoMath.IsValidCoordinate(point.Lat, point.Lon))
                throw new InvalidDataException("Latitude must be between -90 and 90 and longitude between -180 and 180.");
            if (point.Elevation.HasValue && !GeoMath.IsFinite(point.Elevation.Value))
                throw new InvalidDataException("Elevation must be a finite number.");
        }

        private static void CountPoint(ref int count)
        {
            if (++count > MaxPoints) throw new InvalidDataException("The document contains more than 500,000 points.");
        }

        private static GeoPoint GpxPoint(XElement element, ref int count)
        {
            CountPoint(ref count);
            XAttribute lat = element.Attribute("lat"), lon = element.Attribute("lon");
            if (lat == null || lon == null) throw new InvalidDataException("A GPX point is missing latitude or longitude.");
            GeoPoint point = new GeoPoint(Number(lat.Value, "latitude"), Number(lon.Value, "longitude"));
            point.Name = ChildText(element, "name");
            var radioElement=element.Elements().Where(e=>e.Name.LocalName=="extensions").Elements(MapDesk+"radio").FirstOrDefault();if(radioElement!=null)point.Radio=PointRadioOptions.FromXml(radioElement);
            var group=element.Elements().Where(e=>e.Name.LocalName=="extensions").Elements(MapDesk+"group").FirstOrDefault();if(group!=null)point.Group=group.Value.Trim();
            var color=element.Elements().Where(e=>e.Name.LocalName=="extensions").Elements(MapDesk+"color").FirstOrDefault();int rgb;if(color!=null&&Int32.TryParse(color.Value.Trim().TrimStart('#'),NumberStyles.HexNumber,Invariant,out rgb)&&rgb>=0&&rgb<=0xFFFFFF)point.ColorRgb=rgb;
            string elevation = ChildText(element, "ele");
            if (elevation.Length > 0) point.Elevation = Number(elevation, "elevation");
            point.Time = Timestamp(ChildText(element, "time"));
            ValidatePoint(point);
            return point;
        }

        private static MapDocument ReadGpx(XElement root)
        {
            MapDocument result = new MapDocument();
            int count = 0;
            foreach (XElement point in Children(root, "wpt")) result.Waypoints.Add(GpxPoint(point, ref count));
            foreach (XElement route in Children(root, "rte"))
            {
                Track track = new Track { Name = ChildText(route, "name"), IsRoute = true };
                foreach (XElement point in Children(route, "rtept")) track.Points.Add(GpxPoint(point, ref count));
                if (track.Points.Count > 0) result.Tracks.Add(track);
            }
            foreach (XElement original in Children(root, "trk"))
            {
                string name = ChildText(original, "name");
                List<XElement> segments = Children(original, "trkseg").ToList();
                for (int i = 0; i < segments.Count; i++)
                {
                    Track track = new Track { Name = segments.Count > 1 ? name + " [" + (i + 1).ToString(Invariant) + "]" : name };
                    foreach (XElement point in Children(segments[i], "trkpt")) track.Points.Add(GpxPoint(point, ref count));
                    if (track.Points.Count > 0) result.Tracks.Add(track);
                }
            }
            foreach(var element in root.Elements().Where(e=>e.Name.LocalName=="extensions").Elements(MapDesk+"zone")) result.Zones.Add(ReadZone(element,ref count));
            return result;
        }

        private static GeoPoint KmlPoint(string token, bool spaceSeparated, ref int count)
        {
            CountPoint(ref count);
            string[] coordinates = spaceSeparated ? token.Split((char[])null, StringSplitOptions.RemoveEmptyEntries) : token.Split(',');
            if (coordinates.Length < 2 || coordinates.Length > 3) throw new InvalidDataException("A KML coordinate needs longitude, latitude and optional altitude.");
            GeoPoint point = new GeoPoint(Number(coordinates[1], "latitude"), Number(coordinates[0], "longitude"));
            if (coordinates.Length == 3 && coordinates[2].Length > 0) point.Elevation = Number(coordinates[2], "elevation");
            ValidatePoint(point);
            return point;
        }

        private static MapDocument ReadKml(XElement root)
        {
            MapDocument result = new MapDocument();
            int count = 0;
            foreach (XElement placemark in root.Descendants().Where(e => e.Name.LocalName == "Placemark"))
            {
                string name = ChildText(placemark, "name");
                var nativeZone=placemark.Elements().Where(e=>e.Name.LocalName=="ExtendedData").Elements(MapDesk+"zone").FirstOrDefault();
                if(nativeZone!=null){result.Zones.Add(ReadZone(nativeZone,ref count));continue;}
                foreach(var polygon in placemark.Descendants().Where(e=>e.Name.LocalName=="Polygon")){
                    if(polygon.Descendants().Any(e=>e.Name.LocalName=="innerBoundaryIs")) throw new InvalidDataException("Зоны KML с внутренними отверстиями пока не поддерживаются.");
                    var coordinates=polygon.Descendants().FirstOrDefault(e=>e.Name.LocalName=="coordinates");if(coordinates==null)continue;
                    var zone=new Zone{Name=name};foreach(string token in coordinates.Value.Split((char[])null,StringSplitOptions.RemoveEmptyEntries))zone.Points.Add(KmlPoint(token,false,ref count));
                    if(zone.Points.Count>1&&zone.Points[0].Lat==zone.Points.Last().Lat&&zone.Points[0].Lon==zone.Points.Last().Lon)zone.Points.RemoveAt(zone.Points.Count-1);
                    if(zone.Points.Count>=3)result.Zones.Add(zone);
                }
                foreach (XElement geometry in placemark.Descendants().Where(e => e.Name.LocalName == "Point" || e.Name.LocalName == "LineString" || e.Name == Gx + "Track"))
                {
                    if (geometry.Name == Gx + "Track")
                    {
                        Track gxTrack = new Track { Name = name };
                        List<XElement> coords = geometry.Elements(Gx + "coord").ToList();
                        List<XElement> times = Children(geometry, "when").ToList();
                        if (times.Count > 0 && times.Count != coords.Count) throw new InvalidDataException("A KML gx:Track has mismatched coordinates and timestamps.");
                        for (int i = 0; i < coords.Count; i++)
                        {
                            GeoPoint point = KmlPoint(coords[i].Value, true, ref count);
                            if (times.Count > 0) point.Time = Timestamp(times[i].Value);
                            gxTrack.Points.Add(point);
                        }
                        if (gxTrack.Points.Count > 0) result.Tracks.Add(gxTrack);
                    }
                    else
                    {
                        string[] coords = ChildText(geometry, "coordinates").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                        if (geometry.Name.LocalName == "Point")
                        {
                            if (coords.Length != 1) throw new InvalidDataException("A KML Point must contain one coordinate.");
                            GeoPoint point = KmlPoint(coords[0], false, ref count);
                            point.Name = name;
                            point.Radio=PointRadioOptions.FromXml(placemark.Elements().Where(e=>e.Name.LocalName=="ExtendedData").Elements(MapDesk+"radio").FirstOrDefault());
                            var group=placemark.Elements().Where(e=>e.Name.LocalName=="ExtendedData").Elements().FirstOrDefault(e=>e.Name.LocalName=="Data"&&(string)e.Attribute("name")=="mapdesk-group");if(group!=null)point.Group=ChildText(group,"value");
                            var style=Children(placemark,"Style").FirstOrDefault();string styleUrl=ChildText(placemark,"styleUrl");if(style==null&&styleUrl.StartsWith("#"))style=root.Descendants().FirstOrDefault(e=>e.Name.LocalName=="Style"&&(string)e.Attribute("id")==styleUrl.Substring(1));
                            var icon=style==null?null:Children(style,"IconStyle").FirstOrDefault();uint abgr;if(icon!=null&&UInt32.TryParse(ChildText(icon,"color"),NumberStyles.HexNumber,Invariant,out abgr))point.ColorRgb=(int)(((abgr&255)<<16)|(abgr&0xFF00)|((abgr>>16)&255));
                            XElement stamp = Children(placemark, "TimeStamp").FirstOrDefault();
                            if (stamp != null) point.Time = Timestamp(ChildText(stamp, "when"));
                            result.Waypoints.Add(point);
                        }
                        else
                        {
                            Track track = new Track { Name = name };
                            foreach (string coordinate in coords) track.Points.Add(KmlPoint(coordinate, false, ref count));
                            if (track.Points.Count > 0) result.Tracks.Add(track);
                        }
                    }
                }
            }
            return result;
        }

        private static string Decimal(double value) { return value.ToString("R", Invariant); }
        private static string TimeText(DateTimeOffset value) { return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", Invariant); }

        private static XElement GpxElement(string name, GeoPoint point)
        {
            ValidatePoint(point);
            XElement element = new XElement(Gpx + name, new XAttribute("lat", Decimal(point.Lat)), new XAttribute("lon", Decimal(point.Lon)));
            if (point.Elevation.HasValue) element.Add(new XElement(Gpx + "ele", Decimal(point.Elevation.Value)));
            if (point.Time.HasValue) element.Add(new XElement(Gpx + "time", TimeText(point.Time.Value)));
            if (!String.IsNullOrEmpty(point.Name)) element.Add(new XElement(Gpx + "name", point.Name));
            if(name=="wpt"){var extensions=new XElement(Gpx+"extensions",new XElement(MapDesk+"color","#"+(point.ColorRgb&0xFFFFFF).ToString("X6",Invariant)));if(!String.IsNullOrWhiteSpace(point.Group))extensions.Add(new XElement(MapDesk+"group",point.Group));extensions.Add(point.Radio.ToXml(MapDesk));element.Add(extensions);}
            return element;
        }

        private static Zone ReadZone(XElement element, ref int count) {
            var zone=new Zone{Name=(string)element.Attribute("name")??"Зона",Kind=(string)element.Attribute("kind")??"Polygon"};
            int rgb;if(Int32.TryParse((string)element.Attribute("color"),NumberStyles.HexNumber,Invariant,out rgb)&&rgb>=0&&rgb<=0xFFFFFF)zone.ColorRgb=rgb;
            foreach(var point in element.Elements(MapDesk+"point"))zone.Points.Add(GpxPoint(point,ref count));
            ValidateZone(zone);return zone;
        }
        private static void ValidateZone(Zone zone) {
            if(zone==null||(zone.Kind!="Polygon"&&zone.Kind!="Rectangle"&&zone.Kind!="Circle")||(zone.Kind=="Polygon"?zone.Points.Count<3:zone.Points.Count!=2))throw new InvalidDataException("Неверная геометрия зоны.");
            foreach(var point in zone.Points)ValidatePoint(point);
        }
        private static XElement ZoneElement(Zone zone) {
            ValidateZone(zone);var element=new XElement(MapDesk+"zone",new XAttribute("name",zone.Name??""),new XAttribute("kind",zone.Kind),new XAttribute("color",(zone.ColorRgb&0xFFFFFF).ToString("X6",Invariant)));
            foreach(var point in zone.Points)element.Add(new XElement(MapDesk+"point",new XAttribute("lat",Decimal(point.Lat)),new XAttribute("lon",Decimal(point.Lon))));return element;
        }

        public static void SaveGpx(MapDocument document, string path)
        {
            if (document == null) throw new ArgumentNullException("document");
            XElement root = new XElement(Gpx + "gpx", new XAttribute("version", "1.1"), new XAttribute("creator", "Native Map Workspace"));
            foreach (GeoPoint point in document.Waypoints) root.Add(GpxElement("wpt", point));
            foreach (Track track in document.Tracks)
            {
                if (track == null) throw new InvalidDataException("The document contains a null track.");
                XElement element = new XElement(Gpx + (track.IsRoute ? "rte" : "trk"));
                if (!String.IsNullOrEmpty(track.Name)) element.Add(new XElement(Gpx + "name", track.Name));
                if (track.IsRoute) {
                    foreach (GeoPoint point in track.Points) element.Add(GpxElement("rtept", point));
                } else {
                    XElement segment = new XElement(Gpx + "trkseg");
                    foreach (GeoPoint point in track.Points) segment.Add(GpxElement("trkpt", point));
                    element.Add(segment);
                }
                root.Add(element);
            }
            if(document.Zones.Count>0)root.Add(new XElement(Gpx+"extensions",document.Zones.Select(ZoneElement)));
            SaveXml(new XDocument(new XDeclaration("1.0", "utf-8", null), root), path);
        }

        public static void SaveKml(MapDocument document, string path)
        {
            if (document == null) throw new ArgumentNullException("document");
            XElement body = new XElement(Kml + "Document", new XElement(Kml + "name", "Native Map Workspace"));
            foreach (GeoPoint point in document.Waypoints)
            {
                ValidatePoint(point);
                XElement mark = new XElement(Kml + "Placemark");
                if (!String.IsNullOrEmpty(point.Name)) mark.Add(new XElement(Kml + "name", point.Name));
                if (point.Time.HasValue) mark.Add(new XElement(Kml + "TimeStamp", new XElement(Kml + "when", TimeText(point.Time.Value))));
                if(!String.IsNullOrWhiteSpace(point.Group))mark.Add(new XElement(Kml+"ExtendedData",new XElement(Kml+"Data",new XAttribute("name","mapdesk-group"),new XElement(Kml+"value",point.Group))));
                var radioData=mark.Element(Kml+"ExtendedData");if(radioData==null){radioData=new XElement(Kml+"ExtendedData");mark.Add(radioData);}radioData.Add(point.Radio.ToXml(MapDesk));
                int rgb=point.ColorRgb;string abgr="ff"+(rgb&255).ToString("x2")+((rgb>>8)&255).ToString("x2")+((rgb>>16)&255).ToString("x2");mark.Add(new XElement(Kml+"Style",new XElement(Kml+"IconStyle",new XElement(Kml+"color",abgr))));
                mark.Add(new XElement(Kml + "Point", new XElement(Kml + "coordinates", CoordinateText(point))));
                body.Add(mark);
            }
            foreach (Track track in document.Tracks)
            {
                if (track == null) throw new InvalidDataException("The document contains a null track.");
                XElement mark = new XElement(Kml + "Placemark");
                if (!String.IsNullOrEmpty(track.Name)) mark.Add(new XElement(Kml + "name", track.Name));
                foreach (GeoPoint point in track.Points) ValidatePoint(point);
                if (track.Points.Count > 0 && track.Points.All(p => p.Time.HasValue && p.Elevation.HasValue))
                {
                    XElement geometry = new XElement(Gx + "Track");
                    foreach (GeoPoint point in track.Points) geometry.Add(new XElement(Kml + "when", TimeText(point.Time.Value)));
                    foreach (GeoPoint point in track.Points) geometry.Add(new XElement(Gx + "coord", Decimal(point.Lon) + " " + Decimal(point.Lat) + " " + Decimal(point.Elevation.Value)));
                    mark.Add(geometry);
                }
                else mark.Add(new XElement(Kml + "LineString", new XElement(Kml + "tessellate", "1"), new XElement(Kml + "coordinates", String.Join(" ", track.Points.Select(CoordinateText)))));
                body.Add(mark);
            }
            foreach(var zone in document.Zones){
                var metadata=ZoneElement(zone);var outline=zone.Outline();outline.Add(outline[0]);int rgb=zone.ColorRgb;string abgr=(rgb&255).ToString("x2")+((rgb>>8)&255).ToString("x2")+((rgb>>16)&255).ToString("x2");
                body.Add(new XElement(Kml+"Placemark",new XElement(Kml+"name",zone.Name),new XElement(Kml+"Style",new XElement(Kml+"LineStyle",new XElement(Kml+"color","ff"+abgr),new XElement(Kml+"width",2)),new XElement(Kml+"PolyStyle",new XElement(Kml+"color","40"+abgr))),new XElement(Kml+"ExtendedData",metadata),new XElement(Kml+"Polygon",new XElement(Kml+"outerBoundaryIs",new XElement(Kml+"LinearRing",new XElement(Kml+"coordinates",String.Join(" ",outline.Select(CoordinateText))))))));
            }
            XElement root = new XElement(Kml + "kml", new XAttribute(XNamespace.Xmlns + "gx", Gx.NamespaceName), body);
            SaveXml(new XDocument(new XDeclaration("1.0", "utf-8", null), root), path);
        }

        private static string CoordinateText(GeoPoint point)
        {
            return Decimal(point.Lon) + "," + Decimal(point.Lat) + (point.Elevation.HasValue ? "," + Decimal(point.Elevation.Value) : String.Empty);
        }

        private static void SaveXml(XDocument document, string path)
        {
            string fullPath = Path.GetFullPath(path);
            string temporary = Path.Combine(Path.GetDirectoryName(fullPath), ".map-export-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                XmlWriterSettings settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, CloseOutput = true };
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (XmlWriter writer = XmlWriter.Create(stream, settings)) document.Save(writer);
                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
                else File.Move(temporary, fullPath);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private sealed class LimitedReadStream : Stream
        {
            private readonly Stream source;
            private readonly long limit;
            private long read;
            public LimitedReadStream(Stream source, long limit) { this.source = source; this.limit = limit; }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { return read; } set { throw new NotSupportedException(); } }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int received = source.Read(buffer, offset, count);
                read += received;
                if (read > limit) throw new InvalidDataException("The decompressed KML exceeds the import size limit.");
                return received;
            }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
    }
}
