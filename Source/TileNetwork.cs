using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AlpineQuestPort {
    // Shared immutable response payload; every consumer owns its HttpResponseMessage and images.
    internal static class TileNetwork {
        sealed class Packet {internal int Status;internal byte[] Bytes;internal List<KeyValuePair<string,IEnumerable<string>>> Headers,ContentHeaders;}
        sealed class Entry {internal readonly CancellationTokenSource Cancellation=new CancellationTokenSource();internal Task<Packet> Task;internal int Consumers;}
        static readonly object gate=new object();
        static readonly Dictionary<string,Entry> pending=new Dictionary<string,Entry>();
        static readonly SemaphoreSlim slots=new SemaphoreSlim(4);
        static readonly HttpClient client=CreateClient();
        static HttpClient CreateClient(){var c=new HttpClient{Timeout=TimeSpan.FromSeconds(20),MaxResponseContentBufferSize=2*1024*1024};c.DefaultRequestHeaders.UserAgent.ParseAdd("MapDesk/0.38");return c;}
        internal static int PendingCount {get{lock(gate)return pending.Count;}}
        static async Task<Packet> Fetch(string url,List<KeyValuePair<string,IEnumerable<string>>> headers,CancellationToken token){
            await slots.WaitAsync(token).ConfigureAwait(false);
            try{using(var request=new HttpRequestMessage(HttpMethod.Get,url)){foreach(var h in headers)request.Headers.TryAddWithoutValidation(h.Key,h.Value);using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseContentRead,token).ConfigureAwait(false)){if((int)response.StatusCode>=400)Diagnostics.Record("tile.http."+(int)response.StatusCode,null);return new Packet{Status=(int)response.StatusCode,Bytes=await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false),Headers=response.Headers.ToList(),ContentHeaders=response.Content.Headers.ToList()};}}}
            catch(OperationCanceledException){throw;}catch(Exception ex){Diagnostics.Record("tile.network",ex);throw;}finally{slots.Release();}
        }
        static void Release(Entry entry){entry.Task.ContinueWith(task=>{if(task.IsFaulted){var observed=task.Exception;}entry.Cancellation.Dispose();},TaskScheduler.Default);}
        internal static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
            token.ThrowIfCancellationRequested();string url=request.RequestUri.AbsoluteUri;var headers=request.Headers.Select(h=>new KeyValuePair<string,IEnumerable<string>>(h.Key,h.Value.ToArray())).ToList();string key=url+"\n"+String.Join("\n",headers.Select(h=>h.Key+":"+String.Join(",",h.Value)));
            Entry entry;
            lock(gate){if(!pending.TryGetValue(key,out entry)){entry=new Entry();pending.Add(key,entry);entry.Task=Fetch(url,headers,entry.Cancellation.Token);}entry.Consumers++;}
            try{var canceled=new TaskCompletionSource<bool>();using(token.Register(()=>canceled.TrySetResult(true))){if(await Task.WhenAny(entry.Task,canceled.Task).ConfigureAwait(false)!=entry.Task)throw new OperationCanceledException(token);token.ThrowIfCancellationRequested();var packet=await entry.Task.ConfigureAwait(false);var result=new HttpResponseMessage((System.Net.HttpStatusCode)packet.Status){Content=new ByteArrayContent(packet.Bytes)};foreach(var h in packet.Headers)result.Headers.TryAddWithoutValidation(h.Key,h.Value);foreach(var h in packet.ContentHeaders)result.Content.Headers.TryAddWithoutValidation(h.Key,h.Value);return result;}}
            finally{lock(gate){if(--entry.Consumers==0){pending.Remove(key);if(!entry.Task.IsCompleted)entry.Cancellation.Cancel();Release(entry);}}}
        }
    }
    internal static class TileDisk {
        static readonly object[] locks=Enumerable.Range(0,64).Select(i=>new object()).ToArray();
        internal static event Action<string> Published;
        internal static void Write(string path,byte[] bytes){lock(locks[(path.ToUpperInvariant().GetHashCode()&Int32.MaxValue)%locks.Length]){DiskSpace.Check(path,bytes.Length);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}catch(Exception ex){Diagnostics.Record("tile.cache.write",ex);throw;}finally{try{if(File.Exists(temp))File.Delete(temp);}catch(Exception ex){Diagnostics.Record("tile.cache.cleanup",ex);}}}var handlers=Published;if(handlers!=null)foreach(Action<string> handler in handlers.GetInvocationList())try{handler(path);}catch(Exception ex){Diagnostics.Record("tile.cache.notify",ex);}}
    }
    internal static class DiskSpace {
        internal static void Check(string path,long bytes){var drive=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)));if(drive.IsReady&&drive.AvailableFreeSpace<bytes+64L*1024*1024)throw new IOException("Недостаточно свободного места. Освободите место; сохранённые области не удаляются автоматически.");}
    }
}
