using System;
using System.Drawing;
using System.Linq;

namespace AlpineQuestPort {
    internal sealed class TerrainProfile {
        public GeoPoint[] Points;
        public double[] X, Heights;
        public double Length;
        public bool Circle;
        public static TerrainProfile Create(GeoPoint a,GeoPoint b,bool circle,double radius,double step) {
            if(!GeoMath.IsValidCoordinate(a.Lat,a.Lon)||(!circle&&!GeoMath.IsValidCoordinate(b.Lat,b.Lon))||step<10||!GeoMath.IsFinite(step))throw new ArgumentException("Неверные координаты или шаг выборки.");
            double length=circle?2*Math.PI*GeoMath.EarthRadiusMeters*Math.Sin(radius/GeoMath.EarthRadiusMeters):GeoMath.DistanceMeters(a,b);
            if(circle?(radius<10||radius>100000||!GeoMath.IsFinite(radius)):(length<10||length>300000))throw new ArgumentException(circle?"Радиус: 10 м – 100 км.":"Расстояние A–B должно быть от 10 м до 300 км.");
            int count=Math.Max(circle?72:2,Math.Min(4096,(int)Math.Ceiling(length/step)));
            var p=new TerrainProfile{Points=new GeoPoint[count+1],X=new double[count+1],Heights=Enumerable.Repeat(double.NaN,count+1).ToArray(),Length=length,Circle=circle};
            double azimuth=Bearing(a,b);
            for(int i=0;i<=count;i++){
                p.X[i]=circle?360.0*i/count:length*(i/(double)count);
                p.Points[i]=circle?RangeRings.Destination(a,radius,p.X[i]):RangeRings.Destination(a,p.X[i],azimuth);
                if(Math.Abs(p.Points[i].Lat)>GeoMath.MaxMercatorLatitude)throw new ArgumentException("Профиль выходит за область матрицы высот (±85°).");
            }
            if(circle)p.Points[count]=new GeoPoint(p.Points[0].Lat,p.Points[0].Lon);else{p.Points[0]=new GeoPoint(a.Lat,a.Lon);p.Points[count]=new GeoPoint(b.Lat,b.Lon);}return p;
        }
        internal static double Bearing(GeoPoint a,GeoPoint b){double lat=a.Lat*Math.PI/180,lat2=b.Lat*Math.PI/180,lon=(b.Lon-a.Lon)*Math.PI/180;return Math.Atan2(Math.Sin(lon)*Math.Cos(lat2),Math.Cos(lat)*Math.Sin(lat2)-Math.Sin(lat)*Math.Cos(lat2)*Math.Cos(lon))*180/Math.PI;}
    }
    internal sealed class RadioResult {
        public double[] Frequencies, Ground, Sight;
        public double[][] Radii;
        public double[] Margin60, Margin100, WorstDistance;
        public bool Complete, Endpoints, ApproximationValid;
        public static double Fresnel(double frequencyMHz,double d1,double d2){if(!GeoMath.IsFinite(frequencyMHz)||frequencyMHz<=0||d1<0||d2<0||!GeoMath.IsFinite(d1+d2)||d1+d2<=0)throw new ArgumentException("Неверные параметры зоны Френеля.");return Math.Sqrt((299792458.0/(frequencyMHz*1000000))*d1*d2/(d1+d2));}
        public static double Bulge(double d1,double d2,double k){if(!GeoMath.IsFinite(k)||k<=0)throw new ArgumentException("Коэффициент k должен быть положительным.");return d1*d2/(2*k*GeoMath.EarthRadiusMeters);}
        public static RadioResult Calculate(TerrainProfile profile,double[] frequencies,double antennaA,double antennaB,double k,bool curvature=true) {
            if(profile.Circle||frequencies.Length<1||frequencies.Length>3||frequencies.Any(f=>!GeoMath.IsFinite(f)||f<1||f>300000)||!GeoMath.IsFinite(antennaA)||!GeoMath.IsFinite(antennaB)||antennaA<0||antennaB<0)throw new ArgumentException("Проверьте частоты (1–300000 МГц) и высоты антенн.");
            int n=profile.X.Length;var r=new RadioResult{Frequencies=(double[])frequencies.Clone(),Ground=new double[n],Sight=new double[n],Radii=new double[frequencies.Length][],Margin60=Enumerable.Repeat(double.NaN,frequencies.Length).ToArray(),Margin100=Enumerable.Repeat(double.NaN,frequencies.Length).ToArray(),WorstDistance=new double[frequencies.Length],Complete=profile.Heights.All(GeoMath.IsFinite),Endpoints=GeoMath.IsFinite(profile.Heights[0])&&GeoMath.IsFinite(profile.Heights[n-1])};
            r.ApproximationValid=r.Endpoints&&Math.Abs(profile.Heights[n-1]+antennaB-profile.Heights[0]-antennaA)<=profile.Length*.1&&299792458.0/(frequencies.Min()*1e6)<=profile.Length*.1;
            for(int j=0;j<frequencies.Length;j++)r.Radii[j]=new double[n];
            for(int i=0;i<n;i++){
                double total=profile.Length,d=profile.X[i]; if(!GeoMath.IsFinite(total)||total<=0||!GeoMath.IsFinite(d)||d < -1e-7||d > total+1e-7)throw new ArgumentException("Неверные расстояния профиля."); d=Math.Max(0,Math.Min(total,d));r.Ground[i]=profile.Heights[i]+(curvature?Bulge(d,total-d,k):0);
                r.Sight[i]=r.Endpoints?profile.Heights[0]+antennaA+(profile.Heights[n-1]+antennaB-profile.Heights[0]-antennaA)*d/total:double.NaN;
                for(int j=0;j<frequencies.Length;j++){
                    // Paraxial first-zone approximation along the horizontal terrain path.
                    double radius=Fresnel(frequencies[j],d,total-d);r.Radii[j][i]=radius;
                    double m=r.Sight[i]-r.Ground[i]-.6*radius,full=r.Sight[i]-r.Ground[i]-radius;
                    if(GeoMath.IsFinite(m)&&(double.IsNaN(r.Margin60[j])||m<r.Margin60[j])){r.Margin60[j]=m;r.WorstDistance[j]=d;}
                    if(GeoMath.IsFinite(full)&&(double.IsNaN(r.Margin100[j])||full<r.Margin100[j]))r.Margin100[j]=full;
                }
            }return r;
        }
    }
    sealed partial class MapCanvas {
        public Action<GeoPoint> ProfilePick;
        void DrawPointPick(Graphics g){if(ProfilePick!=null)DrawLabel(g,"Выберите точку щелчком · Esc — отмена",20,Height-80);}
    }
}

