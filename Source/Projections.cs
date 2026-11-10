using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace AlpineQuestPort
{
    public sealed class CrsEntry
    {
        public string Id, Name, Kind, Unit;
        public bool Geographic { get { return Kind.StartsWith("geographic"); } }
        public bool Sk42 { get { return Name.IndexOf("Pulkovo 1942", StringComparison.OrdinalIgnoreCase) >= 0; } }
        public bool NorthFirst { get { return Geographic || (Sk42 && Kind == "projected"); } }
        public override string ToString() { return Id + " · " + Name + (Sk42 ? (Geographic?" · СК-42: широта/долгота (не X/Y)":" · СК-42: X/Y") : ""); }
        public static CrsEntry Sk42Zone(List<CrsEntry> catalog,GeoPoint point){int zone=(int)Math.Floor(((point.Lon%360+360)%360)/6)+1;string id="EPSG:"+(28400+zone);var result=catalog.Find(c=>c.Id==id&&c.Sk42&&!c.Geographic);if(result==null)throw new ArgumentException("Для этой долготы нет стандартной зоны СК-42 в каталоге. Выберите систему вручную.");return result;}
        public static List<CrsEntry> Load() {
            var list = new List<CrsEntry>();
            foreach (string line in File.ReadAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "proj-data", "crs-catalog.tsv"), Encoding.UTF8)) {
                var p = line.Split('\t'); if (p.Length >= 4) list.Add(new CrsEntry { Id = p[0], Name = p[1], Kind = p[2], Unit = p[3] });
            }
            return list;
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct ProjectedCoordinate { public double X,Y,Z,T; }
    public sealed class ProjectionResult { public ProjectedCoordinate Coordinate; public string Operation; public double Accuracy; }

    public sealed class ProjectionEngine : IDisposable
    {
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet=CharSet.Ansi, SetLastError=true)] static extern IntPtr GetProcAddress(IntPtr module, string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr NoArgs();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DestroyContext(IntPtr context);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr OnePtr(IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr TwoPtr(IntPtr a,IntPtr b);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SearchPaths(IntPtr c,int count,IntPtr paths);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetNetwork(IntPtr c,int enabled);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr CreateTransform(IntPtr c,IntPtr source,IntPtr target,IntPtr area,IntPtr options);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TransArray(IntPtr op,int direction,UIntPtr count,[In,Out] ProjectedCoordinate[] points);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetInt(IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate double Accuracy(IntPtr c,IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr ErrorString(IntPtr c,int error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Area(IntPtr c,IntPtr p,out double w,out double s,out double e,out double n,out IntPtr name);
        static IntPtr module;
        readonly IntPtr context;
        readonly NoArgs createContext;
        readonly DestroyContext destroyContext;
        readonly OnePtr destroy,getName,getLast;
        readonly TwoPtr create,normalize;
        readonly CreateTransform makeTransform;
        readonly TransArray transform;
        readonly GetInt errno,resetError,contextError;
        readonly Accuracy accuracy;
        readonly ErrorString errorString;
        readonly Area getArea;
        readonly Dictionary<string,IntPtr> operations = new Dictionary<string,IntPtr>();
        readonly Dictionary<string,IntPtr> systems = new Dictionary<string,IntPtr>();
        bool disposed;

        T Bind<T>(string name) where T:class { var p=GetProcAddress(module,name); if(p==IntPtr.Zero) throw new InvalidOperationException("Нет функции PROJ: "+name); return Marshal.GetDelegateForFunctionPointer(p,typeof(T)) as T; }
        public ProjectionEngine() {
            if(module==IntPtr.Zero) {
                string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"native",Environment.Is64BitProcess?"x64":"x86");
                string dll=Directory.GetFiles(folder,"proj_*.dll").Single();
                module=LoadLibraryEx(dll,IntPtr.Zero,0x1100);
                if(module==IntPtr.Zero) throw new InvalidOperationException("Не загружена библиотека координат PROJ (код "+Marshal.GetLastWin32Error()+"). Проверьте папку native и Visual C++ Runtime соответствующей архитектуры.");
            }
            createContext=Bind<NoArgs>("proj_context_create");destroyContext=Bind<DestroyContext>("proj_context_destroy");
            destroy=Bind<OnePtr>("proj_destroy");getName=Bind<OnePtr>("proj_get_name");getLast=Bind<OnePtr>("proj_trans_get_last_used_operation");
            create=Bind<TwoPtr>("proj_create");normalize=Bind<TwoPtr>("proj_normalize_for_visualization");
            makeTransform=Bind<CreateTransform>("proj_create_crs_to_crs_from_pj");transform=Bind<TransArray>("proj_trans_array");
            errno=Bind<GetInt>("proj_errno");resetError=Bind<GetInt>("proj_errno_reset");contextError=Bind<GetInt>("proj_context_errno");
            accuracy=Bind<Accuracy>("proj_coordoperation_get_accuracy");errorString=Bind<ErrorString>("proj_context_errno_string");getArea=Bind<Area>("proj_get_area_of_use");
            context=createContext();
            using(var paths=new NativeStrings(new[]{Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"proj-data")})) Bind<SearchPaths>("proj_context_set_search_paths")(context,1,paths.Pointer);
            Bind<SetNetwork>("proj_context_set_enable_network")(context,0);
        }
        IntPtr System(string definition) {
            IntPtr crs; if(systems.TryGetValue(definition,out crs)) return crs;
            string text=definition.Trim(); if(text.StartsWith("+")&&!text.Contains("+type=crs")) text+=" +type=crs";
            using(var u=new NativeStrings(new[]{text})) crs=create(context,u.First);
            if(crs==IntPtr.Zero) throw new ArgumentException("Неизвестная система координат: "+Read(errorString(context,contextError(context))));
            systems[definition]=crs; return crs;
        }
        public void Validate(string definition){System(definition);}
        public CrsEntry Describe(string definition){
            IntPtr crs=System(definition),inspect=crs;bool temporary=false;
            int type=Bind<GetInt>("proj_get_type")(inspect);
            if(type==19){inspect=Bind<TwoPtr>("proj_get_source_crs")(context,crs);temporary=inspect!=IntPtr.Zero;if(temporary)type=Bind<GetInt>("proj_get_type")(inspect);}
            try{if(type!=12&&type!=15)throw new ArgumentException("В режиме карты нужна горизонтальная географическая или проецированная 2D-система.");return new CrsEntry{Id=definition,Name=Read(getName(crs)),Kind=type==12?"geographic 2D":"projected",Unit=type==12?"degree":"единицы определения"};}finally{if(temporary)destroy(inspect);}
        }
        void CheckArea(string id,double lon,double lat) {
            double w,s,e,n;IntPtr label;
            if(getArea(context,System(id),out w,out s,out e,out n,out label)!=0) {
                bool longitude=w<=e ? lon>=w-0.005&&lon<=e+0.005 : lon>=w-0.005||lon<=e+0.005;
                if(!longitude||lat<s-0.005||lat>n+0.005) throw new ArgumentException("Точка вне области применения "+id+". Выберите подходящую зону.");
            }
        }
        public ProjectionResult Convert(string source,string target,double x,double y,double z=0,double epoch=Double.PositiveInfinity) {
            if(disposed)throw new ObjectDisposedException("ProjectionEngine");
            if(!GeoMath.IsFinite(x)||!GeoMath.IsFinite(y)||!GeoMath.IsFinite(z))throw new ArgumentException("Координаты должны быть конечными числами.");
            if(source=="EPSG:4326" || source=="EPSG:4979") { if(!GeoMath.IsValidCoordinate(y,x))throw new ArgumentException("Недопустимая широта или долгота."); CheckArea(target,x,y); }
            string key=source+"\n"+target;IntPtr op;
            if(!operations.TryGetValue(key,out op)) {
                using(var options=new NativeStrings(new[]{"ALLOW_BALLPARK=NO","ONLY_BEST=YES"})) {
                    var raw=makeTransform(context,System(source),System(target),IntPtr.Zero,options.Pointer);
                    if(raw==IntPtr.Zero)throw new InvalidOperationException("Нет доступного точного преобразования. Возможно, нужны файлы сеток поправок. "+Read(errorString(context,contextError(context))));
                    try { op=normalize(context,raw); } finally { destroy(raw); }
                    if(op==IntPtr.Zero)throw new InvalidOperationException("Не удалось определить порядок осей.");
                    operations[key]=op;
                }
            }
            resetError(op);
            var p=new[]{new ProjectedCoordinate{X=x,Y=y,Z=z,T=epoch}};
            int error=transform(op,1,new UIntPtr(1),p);
            if(error!=0||!GeoMath.IsFinite(p[0].X)||!GeoMath.IsFinite(p[0].Y)||!GeoMath.IsFinite(p[0].Z))throw new InvalidOperationException("Преобразование недоступно: "+Read(errorString(context,error!=0?error:errno(op)))+". Проверьте зону, высоту и наличие сеток поправок.");
            if(target=="EPSG:4326"||target=="EPSG:4979") CheckArea(source,p[0].X,p[0].Y);
            IntPtr used=getLast(op);var result=new ProjectionResult{Coordinate=p[0],Accuracy=-1,Operation=""};
            try { if(used!=IntPtr.Zero) { result.Operation=Read(getName(used));result.Accuracy=accuracy(context,used); } else { result.Operation=Read(getName(op));result.Accuracy=accuracy(context,op); } } finally { if(used!=IntPtr.Zero)destroy(used); }
            return result;
        }
        public ProjectionResult FromWgs(GeoPoint point,string target) { return Convert("EPSG:4326",target,point.Lon,point.Lat,point.Elevation??0); }
        public GeoPoint ToWgs(string source,double x,double y) { var p=Convert(source,"EPSG:4326",x,y).Coordinate; return new GeoPoint(p.Y,p.X); }
        public static string Read(IntPtr pointer) { if(pointer==IntPtr.Zero)return "";int n=0;while(Marshal.ReadByte(pointer,n)!=0&&n<100000)n++;var bytes=new byte[n];Marshal.Copy(pointer,bytes,0,n);return Encoding.UTF8.GetString(bytes); }
        public void Dispose() { if(disposed)return;disposed=true;foreach(var p in operations.Values)destroy(p);foreach(var p in systems.Values)destroy(p);destroyContext(context); }
        sealed class NativeStrings:IDisposable {
            public IntPtr Pointer; public IntPtr First {get{return Marshal.ReadIntPtr(Pointer);}}
            readonly List<IntPtr> items=new List<IntPtr>();
            public NativeStrings(string[] values) { Pointer=Marshal.AllocHGlobal((values.Length+1)*IntPtr.Size);for(int i=0;i<values.Length;i++){var b=Encoding.UTF8.GetBytes(values[i]+"\0");var p=Marshal.AllocHGlobal(b.Length);Marshal.Copy(b,0,p,b.Length);items.Add(p);Marshal.WriteIntPtr(Pointer,i*IntPtr.Size,p);}Marshal.WriteIntPtr(Pointer,values.Length*IntPtr.Size,IntPtr.Zero); }
            public void Dispose(){foreach(var p in items)Marshal.FreeHGlobal(p);Marshal.FreeHGlobal(Pointer);}
        }
    }
}
