using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace AlpineQuestPort
{
    // GetTile returns a caller-owned image. Dispose it after painting.
    public sealed class TileStore : IDisposable
    {
        public readonly MapSource Source;
        private const int MemoryLimit = 256;
        private const int QueueLimit = 64;
        private readonly object gate = new object();
        private readonly string cacheDirectory;
        private readonly string offlineDirectory;
        
        private readonly Dictionary<string, CachedTile> memory = new Dictionary<string, CachedTile>();
        private readonly LinkedList<Request> queue = new LinkedList<Request>();
        private readonly HashSet<string> pending = new HashSet<string>();
        private readonly List<CancellationTokenSource> cancellationSources = new List<CancellationTokenSource>();
        private CancellationTokenSource networkCancellation;
        private bool onlineEnabled;
        private bool disposed;
        private bool insideFrame;
        private int frame;
        private int active;private int diskRevision;
        private long accessSequence;
        private string lastError = "";

        private sealed class CachedTile
        {
            public Image Image;
            public DateTime RefreshAfterUtc;
            public bool FromNetwork;
            public long Access;
        }

        private sealed class Request
        {
            public string Key;
            public int Z;
            public int X;
            public int Y;
            public int Frame;
            public DateTime RequestedUtc;
        }

        private sealed class Metadata
        {
            public DateTime ExpiresUtc;
            public string ETag;
            public DateTimeOffset? LastModified;
        }

        private sealed class LoadResult
        {
            public Image Image;
            public DateTime RefreshAfterUtc = DateTime.UtcNow.AddMinutes(2);
            public bool FromNetwork;
            public string Error;
            public bool NetworkSucceeded;
        }

        public TileStore(string cacheDirectory, string offlineDirectory) : this(cacheDirectory,offlineDirectory,MapSource.BuiltIn().Find(s=>s.Id=="osm")) { }
        public TileStore(string cacheDirectory, string offlineDirectory, MapSource source)
        {
            Source=source.Copy();
            if (String.IsNullOrWhiteSpace(cacheDirectory)) throw new ArgumentException("Не указан каталог кэша.", "cacheDirectory");
            this.cacheDirectory = Path.GetFullPath(cacheDirectory);
            this.offlineDirectory = String.IsNullOrWhiteSpace(offlineDirectory) ? null : Path.GetFullPath(offlineDirectory);
            // csc-built Framework apps can inherit the legacy TLS default.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            TileDisk.Published+=DiskPublished;
        }

        public event EventHandler Changed;

        public bool OnlineEnabled
        {
            get { lock (gate) return onlineEnabled; }
            set
            {
                CancellationTokenSource cancel = null;
                lock (gate)
                {
                    if (disposed || value == onlineEnabled) return;
                    onlineEnabled = value;
                    if (value)
                    {
                        networkCancellation = new CancellationTokenSource();
                        cancellationSources.RemoveAll(c=>c.IsCancellationRequested);
                        cancellationSources.Add(networkCancellation);
                        foreach (CachedTile tile in memory.Values)
                            if (!tile.FromNetwork) tile.RefreshAfterUtc = DateTime.MinValue;
                        lastError = "";
                    }
                    else cancel = networkCancellation;
                }
                if (cancel != null) cancel.Cancel();
                OnChanged();
            }
        }

        public string LastError { get { lock (gate) return lastError; } }
        public void DiscardQueued(){lock(gate){foreach(var item in queue)pending.Remove(item.Key);queue.Clear();}}
        public int PendingCount { get { lock (gate) return queue.Count + active; } }

        // Optional: bracket a complete viewport paint to discard obsolete queued tiles.
        public void BeginFrame()
        {
            lock (gate)
            {
                if (disposed) return;
                insideFrame = true;
                unchecked { frame++; }
            }
        }

        public void EndFrame()
        {
            lock (gate)
            {
                if (disposed) return;
                LinkedListNode<Request> node = queue.First;
                while (node != null)
                {
                    LinkedListNode<Request> next = node.Next;
                    if (node.Value.Frame != frame)
                    {
                        pending.Remove(node.Value.Key);
                        queue.Remove(node);
                    }
                    node = next;
                }
                insideFrame = false;
                PumpLocked();
            }
        }

        static Image CloneForRender(Image image){var copy=(Image)image.Clone();copy.Tag=image;return copy;}
        public Image GetCachedTile(int z,int x,int y){lock(gate){if(disposed)return null;CachedTile tile;string key=z+"/"+x+"/"+y;return memory.TryGetValue(key,out tile)&&tile.Image!=null?CloneForRender(tile.Image):null;}}
        public Image GetTile(int z, int x, int y)
        {
            if (z < Source.MinZoom || z > Source.MaxZoom) return null;
            int width = 1 << z;
            if (y < 0 || y >= width) return null;
            x = ((x % width) + width) % width;
            string key = z.ToString(CultureInfo.InvariantCulture) + "/" + x.ToString(CultureInfo.InvariantCulture) + "/" + y.ToString(CultureInfo.InvariantCulture);
            lock (gate)
            {
                if (disposed) return null;
                CachedTile tile;
                bool found = memory.TryGetValue(key, out tile);
                if (found) tile.Access = ++accessSequence;
                if (!found || ((tile.Image == null || onlineEnabled && (!tile.FromNetwork || RevalidateSaved)) && tile.RefreshAfterUtc <= DateTime.UtcNow))
                    EnqueueLocked(new Request { Key = key, Z = z, X = x, Y = y, Frame = frame, RequestedUtc = DateTime.UtcNow });
                else if (pending.Contains(key)) TouchQueuedLocked(key);
                return found && tile.Image != null ? CloneForRender(tile.Image) : null;
            }
        }

        private void EnqueueLocked(Request request)
        {
            if (pending.Contains(request.Key)) { TouchQueuedLocked(request.Key); return; }
            if (PreserveQueuedRequests && queue.Count >= QueueLimit) return;
            while (queue.Count >= QueueLimit)
            {
                pending.Remove(queue.First.Value.Key);
                queue.RemoveFirst();
            }
            pending.Add(request.Key);
            queue.AddLast(request);
            if (!insideFrame) PumpLocked();
        }

        private void TouchQueuedLocked(string key)
        {
            for (LinkedListNode<Request> node = queue.First; node != null; node = node.Next)
                if (node.Value.Key == key)
                {
                    node.Value.Frame = frame;
                    node.Value.RequestedUtc = DateTime.UtcNow;
                    return;
                }
        }

        private void PumpLocked()
        {
            while (!disposed && !insideFrame && active < 2 && queue.Count > 0)
            {
                Request request = queue.First.Value;
                queue.RemoveFirst();
                if ((DateTime.UtcNow - request.RequestedUtc).TotalSeconds > 15)
                {
                    pending.Remove(request.Key);
                    continue;
                }
                active++;
                BackgroundJobs.Run("tile.load",async delegate { await ProcessAsync(request).ConfigureAwait(false); });
            }
        }

        private async Task ProcessAsync(Request request)
        {
            LoadResult result = null;int revision;lock(gate)revision=diskRevision;
            try { result = await LoadAsync(request).ConfigureAwait(false); }
            catch (Exception ex)
            {
                Diagnostics.Record("tile.load",ex);result = new LoadResult { Error = "Не удалось загрузить карту: " + ex.Message };
            }
            lock (gate)
            {
                active--;
                if(revision!=diskRevision&&!result.FromNetwork)result.RefreshAfterUtc=DateTime.MinValue;
                pending.Remove(request.Key);
                if (!disposed)
                {
                    CachedTile previous;
                    memory.TryGetValue(request.Key, out previous);
                    if (result.Image == null && previous != null && previous.Image != null)
                    {
                        previous.RefreshAfterUtc = result.RefreshAfterUtc;
                        previous.Access = ++accessSequence;
                    }
                    else
                    {
                        if (previous != null && previous.Image != null) previous.Image.Dispose();
                        memory[request.Key] = new CachedTile { Image = result.Image, RefreshAfterUtc = result.RefreshAfterUtc, FromNetwork = result.FromNetwork, Access = ++accessSequence };
                        result.Image = null;
                    }
                    if (!String.IsNullOrEmpty(result.Error)) lastError = result.Error;
                    else if (result.NetworkSucceeded) lastError = "";
                    TrimMemoryLocked();
                    PumpLocked();
                }
            }
            if (result.Image != null) result.Image.Dispose();
            OnChanged();
        }

        public bool HasDiskTile(int z,int x,int y){return File.Exists(Path.Combine(cacheDirectory,z.ToString(CultureInfo.InvariantCulture),x.ToString(CultureInfo.InvariantCulture),y.ToString(CultureInfo.InvariantCulture)+".png"));}
        public volatile bool ForceDiskCache;
        public volatile bool RevalidateSaved; // Default: reuse valid source tiles, regardless of HTTP expiry.
        public bool PreserveQueuedRequests;
        public int FailureRetrySeconds=120;

        // Dedicated bulk-download stores call this sequentially, outside the visible-tile queue.
        internal async Task<bool> SaveOfflineTile(int z,int x,int y,CancellationToken cancellation) {
            cancellation.ThrowIfCancellationRequested();
            string path=Path.Combine(cacheDirectory,z.ToString(CultureInfo.InvariantCulture),x.ToString(CultureInfo.InvariantCulture),y.ToString(CultureInfo.InvariantCulture)+".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // Mark before I/O so quota cleanup cannot race with an explicit offline save.
            if(!File.Exists(path+".keep")){DiskSpace.Check(path,0);File.WriteAllText(path+".keep","");}
            using(cancellation.Register(delegate{OnlineEnabled=false;})) {
                var result=await LoadAsync(new Request{Z=z,X=x,Y=y,Key=z+"/"+x+"/"+y,RequestedUtc=DateTime.UtcNow}).ConfigureAwait(false);
                try {
                    cancellation.ThrowIfCancellationRequested();
                    if(result.Image==null) {lock(gate)lastError=result.Error??"Нет изображения";return false;}
                    // Language-independent legacy tiles may live in a sibling cache.
                    if(!File.Exists(path))using(var buffer=new MemoryStream()){result.Image.Save(buffer,System.Drawing.Imaging.ImageFormat.Png);WriteAtomic(path,buffer.ToArray());}
                    using(var check=LoadImage(path))return true;
                } finally {if(result.Image!=null)result.Image.Dispose();}
            }
        }

        private async Task<LoadResult> LoadAsync(Request request)
        {
            var result = new LoadResult();
            string diskPath = Path.Combine(cacheDirectory, request.Z.ToString(CultureInfo.InvariantCulture), request.X.ToString(CultureInfo.InvariantCulture), request.Y.ToString(CultureInfo.InvariantCulture) + ".png");
            Metadata metadata = null;
            // Older versions split language-independent sources into language-specific folders.
            if(!File.Exists(diskPath)&&!Source.Template.Contains("{lang}")){foreach(string language in new[]{"x-local","ru","en","uk","de","fr"}){var alias=Source.Copy();alias.Language=language;string parent=Path.GetDirectoryName(cacheDirectory);if(parent==null)break;string alternate=Path.Combine(parent,alias.CacheKey,request.Z.ToString(CultureInfo.InvariantCulture),request.X.ToString(CultureInfo.InvariantCulture),request.Y.ToString(CultureInfo.InvariantCulture)+".png");if(File.Exists(alternate)){diskPath=alternate;break;}}}
            if (File.Exists(diskPath))
            {
                try
                {
                    result.Image = LoadImage(diskPath);
                    result.FromNetwork = true;
                    metadata = ReadMetadata(diskPath);
                    result.RefreshAfterUtc = metadata.ExpiresUtc;
                }
                catch (Exception ex) {Diagnostics.Record("Tiles.failure",ex); result.Error = "Не удалось прочитать кэш карты: " + ex.Message; }
            }
            if (result.Image == null && offlineDirectory != null)
            {
                string offlinePath = Path.Combine(offlineDirectory, request.Z.ToString("00", CultureInfo.InvariantCulture), request.X.ToString(CultureInfo.InvariantCulture) + "_" + request.Y.ToString(CultureInfo.InvariantCulture) + ".jpg");
                if (File.Exists(offlinePath))
                    try { result.Image = LoadImage(offlinePath); result.RefreshAfterUtc = DateTime.MinValue; }
                    catch (Exception ex) {Diagnostics.Record("Tiles.failure",ex); result.Error = "Не удалось прочитать базовую карту: " + ex.Message; }
            }
            bool fetch;
            CancellationToken token;
            lock (gate)
            {
                fetch = !disposed && onlineEnabled && (result.Image == null || !result.FromNetwork || RevalidateSaved && result.RefreshAfterUtc <= DateTime.UtcNow);
                token = networkCancellation == null ? CancellationToken.None : networkCancellation.Token;
            }
            if (!fetch) return result;
            // Publish a cached/packaged tile before waiting for a network refresh.
            if (result.Image != null) PublishInterim(request.Key, result);
            try
            {
                string uri = Source.TileUrl(request.Z,request.X,request.Y);
                using (var message = new HttpRequestMessage(HttpMethod.Get, uri))
                {
                    Uri referer;if(Uri.TryCreate(Source.Referer,UriKind.Absolute,out referer))message.Headers.Referrer=referer;
                    if (metadata != null && result.FromNetwork)
                    {
                        EntityTagHeaderValue tag;
                        if (!String.IsNullOrEmpty(metadata.ETag) && EntityTagHeaderValue.TryParse(metadata.ETag, out tag)) message.Headers.IfNoneMatch.Add(tag);
                        if (metadata.LastModified.HasValue) message.Headers.IfModifiedSince = metadata.LastModified;
                    }
                    using (HttpResponseMessage response = await TileNetwork.SendAsync(message, token).ConfigureAwait(false))
                    {
                        if (response.StatusCode != HttpStatusCode.NotModified) response.EnsureSuccessStatusCode();
                        Metadata updated = MetadataFromResponse(response, metadata);
                        bool persist = ForceDiskCache || Source.DiskCache && (response.Headers.CacheControl == null || !response.Headers.CacheControl.NoStore);
                        if (response.StatusCode == HttpStatusCode.NotModified)
                        {
                            if (result.Image == null || !result.FromNetwork) throw new InvalidDataException("Сервер не вернул изображение тайла.");
                            if (persist) SaveMetadata(diskPath, updated);
                        }
                        else
                        {
                            byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            Image downloaded;
                            using (var stream = new MemoryStream(bytes, false)) downloaded = ReadDetachedImage(stream);
                            if (result.Image != null) result.Image.Dispose();
                            result.Image = downloaded;
                            result.FromNetwork = true;
                            if (persist)
                            {
                                try
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(diskPath));
                                    WriteAtomic(diskPath, bytes);
                                    SaveMetadata(diskPath, updated);
                                }
                                catch (Exception ex) {Diagnostics.Record("Tiles.failure",ex); result.Error = "Карта загружена, но кэш не сохранён: " + ex.Message; }
                            }
                        }
                        result.RefreshAfterUtc = updated.ExpiresUtc;
                        result.NetworkSucceeded = true;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (!token.IsCancellationRequested) result.Error = "Сервер карты не ответил за 20 секунд. Проверьте подключение к интернету.";
                result.RefreshAfterUtc = DateTime.UtcNow.AddSeconds(30);
            }
            catch (Exception ex)
            {Diagnostics.Record("Tiles.failure",ex);
                result.Error = "Онлайн-карта недоступна: " + ex.Message;
                result.RefreshAfterUtc = DateTime.UtcNow.AddSeconds(FailureRetrySeconds);
            }
            return result;
        }

        private void PublishInterim(string key, LoadResult result)
        {
            lock (gate)
            {
                if (disposed || memory.ContainsKey(key)) return;
                memory[key] = new CachedTile { Image = (Image)result.Image.Clone(), FromNetwork = result.FromNetwork, RefreshAfterUtc = result.RefreshAfterUtc, Access = ++accessSequence };
                TrimMemoryLocked();
            }
            OnChanged();
        }

        private static Image LoadImage(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) return ReadDetachedImage(stream);
        }

        private static Image ReadDetachedImage(Stream stream)
        {
            using (Image image = Image.FromStream(stream, false, true))
            {
                if (image.Width > 1024 || image.Height > 1024 || image.Width < 1 || image.Height < 1) throw new InvalidDataException("Неверный размер изображения карты.");
                return new Bitmap(image);
            }
        }

        private static Metadata ReadMetadata(string diskPath)
        {
            var result = new Metadata { ExpiresUtc = File.GetLastWriteTimeUtc(diskPath).AddDays(7) };
            string path = diskPath + ".meta";
            if (!File.Exists(path)) return result;
            try
            {
                string[] lines = File.ReadAllLines(path);
                long ticks;
                if (lines.Length > 0 && Int64.TryParse(lines[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks) && ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks) result.ExpiresUtc = new DateTime(ticks, DateTimeKind.Utc);
                if (lines.Length > 1) result.ETag = lines[1];
                DateTimeOffset modified;
                if (lines.Length > 2 && DateTimeOffset.TryParse(lines[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out modified)) result.LastModified = modified;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return result;
        }

        private static Metadata MetadataFromResponse(HttpResponseMessage response, Metadata previous)
        {
            DateTime now = DateTime.UtcNow;
            DateTime expiry = now.AddDays(7);
            CacheControlHeaderValue control = response.Headers.CacheControl;
            if (control != null && control.MaxAge.HasValue)
            {
                TimeSpan remaining = control.MaxAge.Value - (response.Headers.Age ?? TimeSpan.Zero);
                expiry = now.AddSeconds(Math.Max(0, Math.Min(remaining.TotalSeconds, 31536000)));
            }
            else if (response.Content.Headers.Expires.HasValue) expiry = response.Content.Headers.Expires.Value.UtcDateTime;
            if (control != null && (control.NoCache || control.NoStore)) expiry = now;
            return new Metadata
            {
                ExpiresUtc = expiry,
                ETag = response.Headers.ETag != null ? response.Headers.ETag.ToString() : previous == null ? null : previous.ETag,
                LastModified = response.Content.Headers.LastModified ?? (previous == null ? null : previous.LastModified)
            };
        }

        private static void SaveMetadata(string diskPath, Metadata metadata)
        {
            string text = metadata.ExpiresUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "\n" + (metadata.ETag ?? "") + "\n" + (metadata.LastModified.HasValue ? metadata.LastModified.Value.ToString("o", CultureInfo.InvariantCulture) : "");
            WriteAtomic(diskPath + ".meta", System.Text.Encoding.UTF8.GetBytes(text));
        }

        private static void WriteAtomic(string path, byte[] data){TileDisk.Write(path,data);}
        private void DiskPublished(string path){
            if(!path.StartsWith(cacheDirectory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!path.EndsWith(".png",StringComparison.OrdinalIgnoreCase))return;
            lock(gate){if(disposed)return;diskRevision++;foreach(var tile in memory.Values)if(!tile.FromNetwork)tile.RefreshAfterUtc=DateTime.MinValue;}
            OnChanged();
        }

        private void TrimMemoryLocked()
        {
            while (memory.Count > MemoryLimit)
            {
                string oldest = null;
                long sequence = Int64.MaxValue;
                foreach (KeyValuePair<string, CachedTile> item in memory)
                    if (item.Value.Access < sequence) { oldest = item.Key; sequence = item.Value.Access; }
                if (oldest == null) break;
                CachedTile tile = memory[oldest];
                memory.Remove(oldest);
                if (tile.Image != null) tile.Image.Dispose();
            }
        }

        private void OnChanged()
        {
            EventHandler handler;
            lock (gate) { if (disposed) return; handler = Changed; }
            // Events may originate on a worker thread. UI subscribers marshal to the UI thread.
            if (handler != null)
                try { handler(this, EventArgs.Empty); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
        }

        public void Dispose()
        {
            List<CancellationTokenSource> sources;
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                onlineEnabled = false;
                sources = new List<CancellationTokenSource>(cancellationSources);
                queue.Clear();
                foreach (CachedTile tile in memory.Values) if (tile.Image != null) tile.Image.Dispose();
                memory.Clear();
                Changed = null;
            }
            foreach (CancellationTokenSource source in sources) source.Cancel();
            TileDisk.Published-=DiskPublished;
            // Keep token sources valid until in-flight request continuations have observed cancellation.
            // They contain no wait handles, so normal collection releases them after the store is released.
        }
    }
}

