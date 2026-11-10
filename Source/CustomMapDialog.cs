using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AlpineQuestPort {
    internal static class MapSourceProbe {
        public static async Task<Bitmap> Load(MapSource source,GeoPoint center,int zoom,CancellationToken cancellation){
            UserMapCatalog.Validate(source);int z=Math.Max(source.MinZoom,Math.Min(source.MaxZoom,zoom));int side=1<<z;
            int x=Math.Max(0,Math.Min(side-1,(int)(GeoMath.LonToPixel(center.Lon,z)/256))),y=Math.Max(0,Math.Min(side-1,(int)(GeoMath.LatToPixel(center.Lat,z)/256)));
            using(var client=new HttpClient{Timeout=TimeSpan.FromSeconds(10)})using(var request=new HttpRequestMessage(HttpMethod.Get,source.TileUrl(z,x,y))){
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MapDesk/0.31 (Windows desktop; personal map viewer)");
                if(!String.IsNullOrWhiteSpace(source.Referer))request.Headers.Referrer=new Uri(source.Referer);
                using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation).ConfigureAwait(false)){
                    if(!response.IsSuccessStatusCode)throw new InvalidDataException("Сервер вернул HTTP "+(int)response.StatusCode+". Проверьте адрес и доступ к источнику.");
                    if(response.Content.Headers.ContentLength>2*1024*1024)throw new InvalidDataException("Ответ больше 2 МиБ; ожидается изображение тайла.");
                    using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var data=new MemoryStream()){
                        var buffer=new byte[16384];int n;while((n=await stream.ReadAsync(buffer,0,buffer.Length,cancellation).ConfigureAwait(false))>0){if(data.Length+n>2*1024*1024)throw new InvalidDataException("Ответ больше 2 МиБ.");data.Write(buffer,0,n);}data.Position=0;
                        try{using(var image=Image.FromStream(data,true,true)){if(image.Width<1||image.Height<1||image.Width>1024||image.Height>1024)throw new InvalidDataException("Размер тайла должен быть не больше 1024 × 1024.");return new Bitmap(image);}}catch(ArgumentException){throw new InvalidDataException("Сервер не вернул растровое изображение. Ссылка на страницу сайта не подходит.");}
                    }
                }
            }
        }
    }
    internal sealed class CustomMapDialog:Form {
        readonly TextBox name=new TextBox(),url=new TextBox(),credits=new TextBox(),referer=new TextBox(),subdomains=new TextBox();
        readonly NumericUpDown min=new NumericUpDown{Minimum=0,Maximum=20},max=new NumericUpDown{Minimum=0,Maximum=20,Value=19};
        readonly Label result=new Label{AutoEllipsis=true};
        readonly PictureBox preview=new PictureBox{SizeMode=PictureBoxSizeMode.Zoom,BorderStyle=BorderStyle.FixedSingle};
        readonly Button check=new Button{Text="Проверить загрузку"};
        readonly MapSource original;readonly GeoPoint center;readonly int zoom;
        readonly CancellationTokenSource cancellation=new CancellationTokenSource();
        int inputRevision;bool released;
        public MapSource Value {get;private set;}
        void Field(string title,Control control,int y){Controls.Add(new Label{Text=title,Left=16,Top=y+4,Width=170,Height=26});control.SetBounds(190,y,490,28);control.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;Controls.Add(control);}
        public CustomMapDialog(MapSource source,GeoPoint center,int zoom){
            this.original=source;this.center=center??new GeoPoint(0,0);this.zoom=zoom;
            Text=source==null?"Добавить онлайн-карту":"Изменить онлайн-карту";ClientSize=new Size(700,550);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);
            Controls.Add(new Label{Text="Растровые карты Web Mercator (EPSG:3857). Нужен URL тайлов, а не адрес сайта.",Left=16,Top=14,Width=665,Height=42});
            Field("Название",name,62);Field("URL-шаблон",url,102);url.MaxLength=4096;
            Controls.Add(new Label{Text="XYZ: https://server/{z}/{x}/{y}.png · TMS: вместо {y} используйте {-y}\nТакже: {q} — quadkey, {s} — поддомен, {lang} — язык.",Left=190,Top=136,Width=490,Height=45});
            Field("Поддомены (через ,)",subdomains,190);Field("Авторство / подпись",credits,230);Field("Referer (если нужен)",referer,270);
            Controls.Add(new Label{Text="Уровни масштаба",Left=16,Top=314,Width=170});min.SetBounds(190,310,75,28);max.SetBounds(315,310,75,28);Controls.Add(min);Controls.Add(max);Controls.Add(new Label{Text="—",Left=283,Top=314,Width=24});
            check.SetBounds(16,360,170,34);check.Click+=async delegate{await Probe();};Controls.Add(check);
            result.SetBounds(16,406,480,75);result.Text="Проверка загрузит один тайл в районе текущего центра карты.";Controls.Add(result);preview.SetBounds(520,352,160,130);Controls.Add(preview);
            var save=new Button{Text="Сохранить",Left=425,Top=502,Width=125,Height=32};save.Click+=delegate{try{Value=ReadValue();DialogResult=DialogResult.OK;Close();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Проверьте источник");}};
            var cancel=new Button{Text="Отмена",Left=560,Top=502,Width=120,Height=32,DialogResult=DialogResult.Cancel};Controls.Add(save);Controls.Add(cancel);AcceptButton=save;CancelButton=cancel;
            if(source!=null){name.Text=source.Name;url.Text=source.Template;credits.Text=source.Attribution;referer.Text=source.Referer;subdomains.Text=source.Subdomains;min.Value=source.MinZoom;max.Value=source.MaxZoom;}
            EventHandler changed=delegate{inputRevision++;if(preview.Image!=null){preview.Image.Dispose();preview.Image=null;}result.Text="Параметры изменены. Можно проверить загрузку.";};
            url.TextChanged+=changed;referer.TextChanged+=changed;subdomains.TextChanged+=changed;min.ValueChanged+=changed;max.ValueChanged+=changed;
        }
        internal MapSource ReadValue(){var source=new MapSource{Id=original==null?"custom-"+Guid.NewGuid().ToString("N"):original.Id,UserDefined=true,Group="Мои карты",Name=name.Text.Trim(),Template=url.Text.Trim(),Attribution=credits.Text.Trim(),Referer=referer.Text.Trim(),Subdomains=subdomains.Text.Trim(),MinZoom=(int)min.Value,MaxZoom=(int)max.Value,Language=original==null?"ru":original.Language};UserMapCatalog.Validate(source);return source;}
        async Task Probe(){MapSource source;try{source=ReadValue();}catch(Exception ex){result.Text=ex.Message;return;}int revision=inputRevision;check.Enabled=false;if(preview.Image!=null){preview.Image.Dispose();preview.Image=null;}result.Text="Загрузка…";
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token)){timeout.CancelAfter(12000);try{var image=await MapSourceProbe.Load(source,center,zoom,timeout.Token);if(IsDisposed||Disposing||revision!=inputRevision){image.Dispose();return;}preview.Image=image;result.Text="Тайл доступен: "+image.Width+" × "+image.Height+". Проверено только текущее место и масштаб.";}catch(OperationCanceledException){if(!IsDisposed&&!Disposing&&revision==inputRevision)result.Text="Проверка отменена или сервер не ответил за 12 секунд.";}catch(Exception ex){if(!IsDisposed&&!Disposing&&revision==inputRevision)result.Text=ex is InvalidDataException?ex.Message:"Не удалось загрузить тайл. Проверьте сеть, URL и параметры доступа.";}finally{if(!IsDisposed&&!Disposing)check.Enabled=true;}}
        }
        protected override void Dispose(bool disposing){if(disposing&&!released){released=true;cancellation.Cancel();cancellation.Dispose();if(preview.Image!=null){preview.Image.Dispose();preview.Image=null;}}base.Dispose(disposing);}
    }
}
