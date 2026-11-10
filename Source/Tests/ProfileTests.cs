using System;
using System.Linq;
using AlpineQuestPort;
static class ProfileTests {
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);}
    public static void Run(string output){
        Check(Math.Abs(RadioResult.Fresnel(15000,10000,20000)-11.5430102)<.001,"ITU handbook Fresnel reference 15 GHz / 30 km");
        Check(Math.Abs(RadioResult.Bulge(10000,20000,4.0/3)-11.772)<.01,"effective earth curvature reference");
        Check(RadioResult.Fresnel(900,0,1000)==0,"Fresnel zero at endpoint");
        Check(Math.Abs(RadioResult.Fresnel(900,500,500)/RadioResult.Fresnel(3600,500,500)-2)<1e-10,"Fresnel inverse square root frequency scaling");
        bool rejected=false;try{RadioResult.Fresnel(0,100,100);}catch(ArgumentException){rejected=true;}Check(rejected,"zero frequency rejected");
        var a=new GeoPoint(55.75,37.62);var b=RangeRings.Destination(a,600,90);var p=TerrainProfile.Create(a,b,false,0,30);
        Check(GeoMath.DistanceMeters(p.Points[0],a)<.001&&GeoMath.DistanceMeters(p.Points.Last(),b)<.001&&Math.Abs(p.Length-600)<.001,"profile full extent and exact endpoints");
        for(int i=0;i<p.Heights.Length;i++)p.Heights[i]=120;
        var r=RadioResult.Calculate(p,new[]{900.0,2400,5800},20,20,4.0/3);Check(r.Complete&&r.Endpoints&&r.Margin60[0]<r.Margin60[1]&&r.Margin60[1]<r.Margin60[2]&&r.Margin60[0]>0,"three independent frequencies ordered clearances");
        p.Heights[p.Heights.Length/2]=160;r=RadioResult.Calculate(p,new[]{900.0},20,20,4.0/3);Check(r.Margin60[0]<0,"terrain ridge blocks first zone");
        p.Heights[3]=double.NaN;r=RadioResult.Calculate(p,new[]{900.0},20,20,4.0/3);Check(!r.Complete&&r.Endpoints,"missing interior height never yields complete-clear result");
        p.Heights[0]=double.NaN;r=RadioResult.Calculate(p,new[]{900.0},20,20,4.0/3);Check(!r.Endpoints&&double.IsNaN(r.Margin60[0]),"missing endpoint disables clearance calculation");
        var circle=TerrainProfile.Create(a,a,true,5000,30);Check(circle.X[0]==0&&circle.X.Last()==360&&GeoMath.DistanceMeters(circle.Points[0],circle.Points.Last())<.001&&circle.Points.All(v=>Math.Abs(GeoMath.DistanceMeters(a,v)-5000)<.001),"circle profile radius and 360 degree closure");
        var dateline=TerrainProfile.Create(new GeoPoint(40,179.99),new GeoPoint(40,-179.99),false,0,30);Check(dateline.Length<2000&&dateline.Points.All(v=>Math.Abs(v.Lon)>179.9),"profile follows short path across dateline");
        Check(TerrainProfile.Create(a,a,true,100000,10).Points.Length==4097,"profile sample memory bound");
    }
}

