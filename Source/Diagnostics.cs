using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Windows.Forms;

namespace AlpineQuestPort {
    internal static class Diagnostics {
        static readonly object gate=new object();
        static string folder; static string latest="";
        internal const int MaxBytes=512*1024;
        internal static void Initialize(string data){lock(gate)folder=Path.Combine(data,"diagnostics");}
        internal static string Record(string operation,Exception error){
            string id=Guid.NewGuid().ToString("N");var text=new StringBuilder();
            text.AppendLine(DateTime.UtcNow.ToString("O")+" | MapDesk 0.40 experimental | "+(Environment.Is64BitProcess?"x64":"x86")+" | "+operation+" | "+id);
            // Do not include messages, file paths, URLs, coordinates, or exception.Data.
            for(Exception e=error;e!=null;e=e.InnerException){text.AppendLine(e.GetType().FullName+" HRESULT=0x"+e.HResult.ToString("X8"));var frames=new StackTrace(e,false).GetFrames();if(frames!=null)foreach(var frame in frames){var method=frame.GetMethod();if(method!=null)text.AppendLine("  "+(method.DeclaringType==null?"":method.DeclaringType.FullName)+"."+method.Name);}}
            lock(gate){latest=text.ToString();try{if(folder!=null){Directory.CreateDirectory(folder);string path=Path.Combine(folder,"events.log");if(File.Exists(path)&&new FileInfo(path).Length+Encoding.UTF8.GetByteCount(latest)>MaxBytes){string old=path+".1";if(File.Exists(old))File.Delete(old);File.Move(path,old);}File.AppendAllText(path,latest,Encoding.UTF8);}}catch{latest+="Diagnostic log unavailable (write failed).\r\n";}}
            return id;
        }
        internal static string Report(){lock(gate){var report=new StringBuilder("MapDesk 0.40 experimental\r\nRuntime: "+Environment.Version+"; "+(Environment.Is64BitProcess?"x64":"x86")+"\r\n");try{string path=folder==null?null:Path.Combine(folder,"events.log");if(path!=null&&File.Exists(path)){string value=File.ReadAllText(path);report.Append(value.Length>64000?value.Substring(value.Length-64000):value);}else report.Append(latest);}catch{report.Append(latest);}return report.ToString();}}
        internal static void Show(IWin32Window owner,string operation,Exception error){Record(operation,error);using(var dialog=new Form{Text="MapDesk — диагностика",Width=780,Height=480,StartPosition=FormStartPosition.CenterParent}){var body=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,Dock=DockStyle.Fill,Text=(error==null?"":error.Message+Environment.NewLine+Environment.NewLine)+Report()};var copy=new Button{Text="Скопировать диагностику",Dock=DockStyle.Bottom,Height=40};copy.Click+=delegate{try{Clipboard.SetText(Report());copy.Text="Диагностика скопирована";}catch(Exception ex){Record("clipboard.copy",ex);copy.Text="Буфер обмена занят — повторите";}};dialog.Controls.Add(body);dialog.Controls.Add(copy);dialog.ShowDialog(owner);}}
    }
    internal static class AtomicSettings {
        static readonly object gate=new object();
        static readonly HashSet<string> recovered=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal static void Save(string path,Action<Stream> writer){lock(gate){string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){writer(stream);stream.Flush(true);}using(var reader=XmlReader.Create(temp,ReaderSettings()))while(reader.Read()){}if(File.Exists(path)){bool valid=!recovered.Contains(Path.GetFullPath(path));if(valid)try{using(var reader=XmlReader.Create(path,ReaderSettings()))while(reader.Read()){} }catch{valid=false;}File.Replace(temp,path,valid?path+".bak":null);}else File.Move(temp,path);recovered.Remove(Path.GetFullPath(path));}catch(Exception ex){Diagnostics.Record("settings.save",ex);throw;}finally{try{if(File.Exists(temp))File.Delete(temp);}catch(Exception ex){Diagnostics.Record("settings.temp.cleanup",ex);}}}}
        internal static XmlReaderSettings ReaderSettings(){return new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8*1024*1024};}
        internal static T Load<T>(string path,Func<Stream,T> reader){lock(gate){try{using(var stream=File.OpenRead(path))return reader(stream);}catch(Exception ex){Diagnostics.Record("settings.load.primary",ex);if(!File.Exists(path+".bak"))throw;try{using(var stream=File.OpenRead(path+".bak")){T value=reader(stream);recovered.Add(Path.GetFullPath(path));Diagnostics.Record("settings.recovered.backup",null);return value;}}catch(Exception backup){Diagnostics.Record("settings.load.backup",backup);throw new InvalidDataException("Не удалось прочитать настройки и резервную копию.",backup);}}}}
        internal static T Recover<T>(string path,Func<string,T> reader){lock(gate){try{if(!File.Exists(path)&&File.Exists(path+".bak"))throw new FileNotFoundException();return reader(path);}catch(Exception ex){Diagnostics.Record("settings.load.primary",ex);if(!File.Exists(path+".bak"))throw;T value=reader(path+".bak");recovered.Add(Path.GetFullPath(path));Diagnostics.Record("settings.recovered.backup",null);return value;}}}
        internal static XElement LoadXml(string path){return Load(path,stream=>{using(var reader=XmlReader.Create(stream,ReaderSettings()))return XElement.Load(reader);});}
        internal static void SaveXml(string path,XElement value){Save(path,stream=>value.Save(stream));}
    }
}
