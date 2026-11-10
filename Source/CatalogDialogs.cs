using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AlpineQuestPort
{
    sealed class MarkerDialog:Form {
        readonly TextBox name=new TextBox();readonly ComboBox group=new ComboBox();readonly Button color=new Button();
        public PointRadioOptions RadioOptions {get;private set;}
        public bool StartRay {get;private set;}
        PointRadioEditor radioEditor;
        public string MarkerName {get;private set;}
        public int ColorRgb {get;private set;}
        public string GroupName {get;private set;}
        public MarkerDialog(GeoPoint point,IEnumerable<string> groups=null,MapCanvas map=null,string cache=null){
            Text="Точка · свойства";ClientSize=new Size(640,290);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);
            Controls.Add(new Label{Text="Название метки",AutoSize=true,Location=new Point(16,15)});name.SetBounds(16,43,608,28);name.Text=point.Name;MarkerName=point.Name;name.MaxLength=512;Controls.Add(name);
            ColorRgb=point.ColorRgb;Action preview=delegate{color.BackColor=Color.FromArgb(unchecked((int)0xFF000000)|ColorRgb);color.ForeColor=color.BackColor.GetBrightness()<0.5?Color.White:Color.Black;color.Text="Цвет · #"+ColorRgb.ToString("X6");};color.SetBounds(16,85,225,34);color.UseVisualStyleBackColor=false;preview();color.Click+=delegate{using(var d=new ColorDialog{Color=color.BackColor,FullOpen=true})if(d.ShowDialog(this)==DialogResult.OK){ColorRgb=d.Color.ToArgb()&0xFFFFFF;preview();}};Controls.Add(color);
            Controls.Add(new Label{Text="Группа (пусто — без группы)",AutoSize=true,Location=new Point(16,128)});group.SetBounds(16,155,608,28);if(groups!=null)foreach(string entry in groups.Where(v=>!String.IsNullOrWhiteSpace(v)).Distinct().OrderBy(v=>v))group.Items.Add(entry);group.Text=point.Group;GroupName=point.Group;Controls.Add(group);
            RadioOptions=point.Radio.Clone();
            var radioToggle=new Button{Text="Профиль и параметры радиолинии ▾",Location=new Point(16,197),Size=new Size(608,32)};Controls.Add(radioToggle);
            var radioPanel=new Panel{Location=new Point(0,235),Size=new Size(640,420),Visible=false};Controls.Add(radioPanel);
            radioToggle.Click+=delegate{if(radioEditor==null){radioEditor=new PointRadioEditor(RadioOptions){Left=16,Top=0};radioPanel.Controls.Add(radioEditor);if(map!=null&&cache!=null){var rayPreview=new RayPreview(map,point,radioEditor,cache){Left=16,Top=175,Width=608,Height=240};radioPanel.Controls.Add(rayPreview);}}radioPanel.Visible=!radioPanel.Visible;ClientSize=new Size(640,radioPanel.Visible?710:290);radioToggle.Text=radioPanel.Visible?"Профиль и параметры радиолинии ▴":"Профиль и параметры радиолинии ▾";};
            var save=new Button{Text="Сохранить",Location=new Point(283,245),Size=new Size(112,32),Anchor=AnchorStyles.Bottom|AnchorStyles.Right};save.Click+=delegate{string nextName=name.Text.Trim();if(nextName.Length==0){MessageBox.Show(this,"Введите название метки.");name.Focus();return;}try{if(radioEditor!=null)RadioOptions=radioEditor.Value();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Параметры Френеля");return;}MarkerName=nextName;GroupName=group.Text.Trim();DialogResult=DialogResult.OK;Close();};var cancel=new Button{Text="Отмена",Location=new Point(512,245),Size=new Size(112,32),Anchor=AnchorStyles.Bottom|AnchorStyles.Right,DialogResult=DialogResult.Cancel};var lead=new Button{Text="Сохранить и вести луч",Location=new Point(16,245),Size=new Size(247,32),Anchor=AnchorStyles.Bottom|AnchorStyles.Left};lead.Click+=delegate{StartRay=true;save.PerformClick();if(DialogResult!=DialogResult.OK)StartRay=false;};Controls.Add(lead);Controls.Add(save);Controls.Add(cancel);AcceptButton=save;CancelButton=cancel;
        }
    }

    sealed class MarkerGroupsDialog:Form {
        readonly CheckedListBox list=new CheckedListBox();readonly ComboBox group=new ComboBox();readonly List<GeoPoint> points;
        public List<GeoPoint> Chosen=new List<GeoPoint>();public string GroupName="";
        public MarkerGroupsDialog(IEnumerable<GeoPoint> source,GeoPoint selected){points=source.ToList();Text="Группы меток";ClientSize=new Size(580,540);MinimumSize=new Size(500,450);StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);
            var top=new Panel{Dock=DockStyle.Top,Height=95};top.Controls.Add(new Label{Text="Выберите группу или введите новую. Пусто — убрать из группы.",Location=new Point(12,10),AutoSize=true});group.SetBounds(12,38,550,28);group.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top;foreach(string entry in points.Select(v=>v.Group).Where(v=>!String.IsNullOrWhiteSpace(v)).Distinct().OrderBy(v=>v))group.Items.Add(entry);if(selected!=null)group.Text=selected.Group;top.Controls.Add(group);top.Controls.Add(new Label{Text="Отметьте метки для переноса:",Location=new Point(12,72),AutoSize=true});
            list.Dock=DockStyle.Fill;list.CheckOnClick=true;foreach(var point in points)list.Items.Add((String.IsNullOrEmpty(point.Name)?"Без названия":point.Name)+"  ["+(String.IsNullOrEmpty(point.Group)?"Без группы":point.Group)+"]",Object.ReferenceEquals(point,selected));
            var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=90,Padding=new Padding(10)};var all=new Button{Text="Выбрать все",Width=125};all.Click+=delegate{for(int i=0;i<list.Items.Count;i++)list.SetItemChecked(i,true);};var none=new Button{Text="Снять выбор",Width=125};none.Click+=delegate{for(int i=0;i<list.Items.Count;i++)list.SetItemChecked(i,false);};var save=new Button{Text="Перенести",Width=125};save.Click+=delegate{if(list.CheckedIndices.Count==0)return;Chosen=list.CheckedIndices.Cast<int>().Select(i=>points[i]).ToList();GroupName=group.Text.Trim();DialogResult=DialogResult.OK;Close();};var cancel=new Button{Text="Отмена",Width=125,DialogResult=DialogResult.Cancel};bottom.Controls.AddRange(new Control[]{all,none,save,cancel});AcceptButton=save;CancelButton=cancel;Controls.Add(list);Controls.Add(top);Controls.Add(bottom);
        }
    }

    sealed class MapsDialog:Form
    {
        readonly List<MapSource> sources;
        readonly ListBox list=new ListBox();
        readonly TextBox search=new TextBox();
        readonly ComboBox language=new ComboBox();
        readonly Label info=new Label();
        public MapSource Chosen;
        public MapsDialog(List<MapSource> all,MapSource current,Action<string> import,Action<MapSource> saveCustom=null,Action<MapSource> deleteCustom=null,GeoPoint center=null,int zoom=3){
            sources=all;Text="Каталог онлайн-карт";ClientSize=new Size(740,520);MinimumSize=new Size(756,559);StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);
            var top=new Panel{Dock=DockStyle.Top,Height=45,Padding=new Padding(12)};search.Dock=DockStyle.Fill;top.Controls.Add(search);
            list.Dock=DockStyle.Fill;list.IntegralHeight=false;list.BorderStyle=BorderStyle.FixedSingle;list.DisplayMember="";
            var bottom=new Panel{Dock=DockStyle.Bottom,Width=740,Height=145,Padding=new Padding(12)};
            info.SetBounds(12,4,710,55);info.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
            bottom.Controls.Add(info);
            bottom.Controls.Add(new Label{Text="Язык карты:",Location=new Point(12,64),AutoSize=true});
            language.SetBounds(132,60,220,28);language.DropDownStyle=ComboBoxStyle.DropDownList;language.Items.AddRange(new object[]{"x-local — местные названия","ru — русский","en — английский","uk — украинский","de — немецкий","fr — французский"});language.SelectedIndex=0;
            bottom.Controls.Add(language);
            var load=new Button{Text="Импорт AQX…",Location=new Point(12,102),Size=new Size(140,30)};
            load.Click+=delegate {using(var d=new OpenFileDialog{Filter="Каталог AlpineQuest|*.aqx"})if(d.ShowDialog(this)==DialogResult.OK){try{import(d.FileName);Fill();}catch(Exception e){MessageBox.Show(this,e.Message,"Ошибка импорта");}}};bottom.Controls.Add(load);
            var add=new Button{Text="Добавить URL…",Location=new Point(165,102),Size=new Size(140,30),Enabled=saveCustom!=null};
            var edit=new Button{Text="Изменить…",Location=new Point(400,60),Size=new Size(110,30)};
            var remove=new Button{Text="Удалить",Location=new Point(520,60),Size=new Size(100,30)};
            Action<MapSource> openEditor=delegate(MapSource source){using(var dialog=new CustomMapDialog(source,center,zoom))if(dialog.ShowDialog(this)==DialogResult.OK){try{saveCustom(dialog.Value);search.Clear();Fill();list.SelectedItem=dialog.Value;}catch(Exception ex){MessageBox.Show(this,ex.Message,"Источник не сохранён");}}};
            add.Click+=delegate{openEditor(null);};edit.Click+=delegate{var source=list.SelectedItem as MapSource;if(source!=null&&source.UserDefined&&saveCustom!=null)openEditor(source);};
            remove.Click+=delegate{var source=list.SelectedItem as MapSource;if(source==null||!source.UserDefined||deleteCustom==null)return;if(MessageBox.Show(this,"Удалить «"+source.Name+"» из каталога? Сохранённые слои и кэш останутся.","Удаление источника",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;try{deleteCustom(source);Fill();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Источник не удалён");}};
            bottom.Controls.Add(add);bottom.Controls.Add(edit);bottom.Controls.Add(remove);
            var select=new Button{Text="Показать карту",Location=new Point(400,102),Size=new Size(175,30),Anchor=AnchorStyles.Right|AnchorStyles.Bottom};
            select.Click+=delegate {Chosen=list.SelectedItem as MapSource;if(Chosen==null)return;if(Chosen.Template.Contains("{lang}")){Chosen=Chosen.Copy();Chosen.Language=((string)language.SelectedItem).Split(' ')[0];if(Chosen.UserDefined&&saveCustom!=null){try{saveCustom(Chosen);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Источник не сохранён");return;}}}DialogResult=DialogResult.OK;Close();};bottom.Controls.Add(select);
            var cancel=new Button{Text="Отмена",Location=new Point(590,102),Size=new Size(130,30),DialogResult=DialogResult.Cancel,Anchor=AnchorStyles.Right|AnchorStyles.Bottom};bottom.Controls.Add(cancel);CancelButton=cancel;AcceptButton=select;
            Controls.Add(list);Controls.Add(bottom);Controls.Add(top);search.TextChanged+=delegate{Fill();};
            list.SelectedIndexChanged+=delegate{var s=list.SelectedItem as MapSource;edit.Enabled=s!=null&&s.UserDefined&&saveCustom!=null;remove.Enabled=s!=null&&s.UserDefined&&deleteCustom!=null;if(s==null)return;for(int li=0;li<language.Items.Count;li++)if(((string)language.Items[li]).StartsWith(s.Language+" "))language.SelectedIndex=li;info.Text=s.Attribution+"\nМасштабы: "+s.MinZoom+"–"+s.MaxZoom+" · "+"дисковый кэш управляется в окне «Слои…»";language.Enabled=s.Template.Contains("{lang}");};
            Fill();list.SelectedItem=sources.FirstOrDefault(s=>s.CacheKey==current.CacheKey)??sources.FirstOrDefault(s=>s.Id==current.Id)??sources.FirstOrDefault();
            for(int i=0;i<language.Items.Count;i++)if(((string)language.Items[i]).StartsWith(current.Language+" "))language.SelectedIndex=i;
        }
        void Fill(){var selected=list.SelectedItem;list.BeginUpdate();list.Items.Clear();foreach(var s in sources.Where(s=>(s.Group+" "+s.Name).IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0))list.Items.Add(s);if(selected!=null&&list.Items.Contains(selected))list.SelectedItem=selected;else if(list.Items.Count>0)list.SelectedIndex=0;list.EndUpdate();}
    }

    sealed class CoordinatesDialog:Form
    {
        readonly ProjectionEngine engine;
        readonly List<CrsEntry> catalog;
        readonly GeoPoint anchor;
        readonly ListBox list=new ListBox();
        readonly TextBox definition=new TextBox();readonly TextBox first=new TextBox();readonly TextBox second=new TextBox();
        readonly Label firstLabel=new Label();readonly Label secondLabel=new Label();readonly Label info=new Label();
        public AngleFormat SelectedFormat;
        public CrsEntry Chosen;
        public GeoPoint Target;
        public CoordinatesDialog(ProjectionEngine p,List<CrsEntry> all,CrsEntry current,GeoPoint point,AngleFormat format=AngleFormat.Десятичные_градусы){
            engine=p;catalog=all;anchor=point;SelectedFormat=format;
            Text="Координаты · СК-42 / WGS84";Size=new Size(850,690);MinimumSize=new Size(730,610);StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);
            var top=new Panel{Dock=DockStyle.Top,Height=75,Padding=new Padding(12)};
            top.Controls.Add(new Label{Text="Выберите систему и формат координат",AutoSize=true,Location=new Point(12,15)});
            var bottom=new Panel{Dock=DockStyle.Bottom,Height=255};
            firstLabel.SetBounds(12,70,320,24);secondLabel.SetBounds(410,70,340,24);bottom.Controls.Add(firstLabel);bottom.Controls.Add(secondLabel);
            first.SetBounds(12,96,370,28);second.SetBounds(410,96,390,28);bottom.Controls.Add(first);bottom.Controls.Add(second);
            info.SetBounds(12,132,790,70);info.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top;info.ForeColor=Color.DarkSlateGray;bottom.Controls.Add(info);
            var apply=new Button{Text="Применить к карте",Location=new Point(12,211),Size=new Size(180,32)};
            apply.Click+=delegate{try{Chosen=Resolve();engine.Validate(Chosen.Id);DialogResult=DialogResult.OK;Close();}catch(Exception e){info.Text=e.Message;}};
            var go=new Button{Text="Перейти к координатам",Location=new Point(210,211),Size=new Size(235,32)};
            go.Click+=delegate{try{Chosen=Resolve();double a=Chosen.Geographic?ParseAngle(first.Text):Parse(first.Text),b=Chosen.Geographic?ParseAngle(second.Text):Parse(second.Text);if(Chosen.Sk42&&!Chosen.Geographic&&Chosen.Id.StartsWith("EPSG:284",StringComparison.Ordinal)&&b>=4000000&&b<33000000){int zone=(int)(b/1000000);var zoned=catalog.Find(c=>c.Id=="EPSG:"+(28400+zone));if(zoned!=null){Chosen=zoned;definition.Text=Chosen.Id;}}Target=engine.ToWgs(Chosen.Id,Chosen.NorthFirst?b:a,Chosen.NorthFirst?a:b);DialogResult=DialogResult.OK;Close();}catch(Exception e){info.Text=e.Message;}};
            var recalc=new Button{Text="Из центра карты",Location=new Point(463,211),Size=new Size(170,32)};recalc.Click+=delegate{RefreshCoordinates();};
            var close=new Button{Text="Отмена",DialogResult=DialogResult.Cancel,Location=new Point(655,211),Size=new Size(145,32)};bottom.Controls.AddRange(new Control[]{apply,go,recalc,close});CancelButton=close;AcceptButton=go;
            list.Dock=DockStyle.Fill;list.IntegralHeight=false;Controls.Add(list);Controls.Add(bottom);Controls.Add(top);
            list.Items.AddRange(new object[]{"СК-42 · X/Y · зона по точке","WGS84 · десятичные градусы","WGS84 · градусы и минуты","WGS84 · градусы, минуты и секунды"});
            list.SelectedIndexChanged+=delegate{try{if(list.SelectedIndex==0)definition.Text=CrsEntry.Sk42Zone(catalog,anchor).Id;else{definition.Text="EPSG:4326";SelectedFormat=(AngleFormat)(list.SelectedIndex-1);}RefreshCoordinates();}catch(Exception e){info.Text=e.Message;}};
            list.SelectedIndex=current.Id=="EPSG:4326"?1+(int)format:0;
        }
        internal static double ParseAngle(string s){
            var parts=System.Text.RegularExpressions.Regex.Split(s.Trim().Replace('−','-').Replace(',','.'),@"[°º′'″""\s]+").Where(v=>v.Length>0).ToArray();
            if(parts.Length<1||parts.Length>3)throw new ArgumentException("Введите градусы, минуты и секунды числами.");
            double d=Parse(parts[0]),m=parts.Length>1?Parse(parts[1]):0,t=parts.Length>2?Parse(parts[2]):0;
            if(m<0||m>=60||t<0||t>=60)throw new ArgumentException("Минуты и секунды должны быть от 0 до 60.");
            return (parts[0].StartsWith("-")?-1:1)*(Math.Abs(d)+m/60+t/3600);
        }
        internal static bool Matches(CrsEntry c,string query){
            string q=System.Text.RegularExpressions.Regex.Replace(query.Trim().ToLowerInvariant(),@"[\s\-‐‑–—_]","");
            if(q.StartsWith("ск42")||q.StartsWith("sk42")){string tail=q.Substring(4).Replace("зона","").Replace("zone","");if(!c.Sk42)return false;if(tail.Length==0)return true;int zone;if(Int32.TryParse(tail,out zone))return c.Id=="EPSG:"+(28400+zone);}
            string text=System.Text.RegularExpressions.Regex.Replace((c.Id+" "+c.Name).ToLowerInvariant(),@"[\s\-‐‑–—_]","");return text.Contains(q);
        }
        CrsEntry Resolve(){var c=catalog.Find(v=>v.Id==definition.Text&&(v.Id=="EPSG:4326"||(v.Sk42&&!v.Geographic&&v.Id.StartsWith("EPSG:284"))));if(c==null)throw new ArgumentException("Выберите СК-42 или WGS84.");return c;}
        void RefreshCoordinates(){try{var c=Resolve();firstLabel.Text=c.Geographic?"Широта (градусы)":c.NorthFirst?"X — север ("+c.Unit+")":"E — восток ("+c.Unit+")";secondLabel.Text=c.Geographic?"Долгота (градусы)":c.NorthFirst?"Y — восток ("+c.Unit+")":"N — север ("+c.Unit+")";var r=engine.FromWgs(anchor,c.Id);first.Text=(c.NorthFirst?r.Coordinate.Y:r.Coordinate.X).ToString(c.Geographic?"F8":c.Sk42?"F0":"F3",CultureInfo.InvariantCulture);second.Text=(c.NorthFirst?r.Coordinate.X:r.Coordinate.Y).ToString(c.Geographic?"F8":c.Sk42?"F0":"F3",CultureInfo.InvariantCulture);if(c.Geographic){var view=new ViewSettings{Format=SelectedFormat};first.Text=view.Angle(anchor.Lat);second.Text=view.Angle(anchor.Lon);}info.Text=(r.Accuracy>0?"Точность выбранного преобразования по базе: "+r.Accuracy+" м. ":"")+r.Operation;}catch(Exception e){info.Text=e.Message;first.Text="";second.Text="";}}
        internal static double Parse(string s){s=System.Text.RegularExpressions.Regex.Replace(s.Trim(),@"^[XYХУхуxy]\s*[=:]?\s*","");s=System.Text.RegularExpressions.Regex.Replace(s,@"\s","");double v;if(!Double.TryParse(s.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out v)||!GeoMath.IsFinite(v))throw new ArgumentException("Введите числовые координаты.");return v;}
    }
}

namespace AlpineQuestPort {
 internal sealed class CoordinateCopyDialog:System.Windows.Forms.Form {
  public CoordinateCopyDialog(GeoPoint point,System.Func<int,string> format){
   Text="Копировать координаты";ClientSize=new System.Drawing.Size(620,195);MinimumSize=Size;StartPosition=System.Windows.Forms.FormStartPosition.CenterParent;Font=new System.Drawing.Font("Segoe UI",10);
   var choices=new System.Windows.Forms.ComboBox{Left=15,Top=15,Width=590,DropDownStyle=System.Windows.Forms.ComboBoxStyle.DropDownList};
   choices.Items.AddRange(new object[]{"СК-42 · X/Y · зона по точке","WGS84 · десятичные градусы","WGS84 · градусы и минуты","WGS84 · градусы, минуты и секунды"});
   var text=new System.Windows.Forms.TextBox{Left=15,Top=55,Width=590,Height=65,Multiline=true,ReadOnly=true};
   var copy=new System.Windows.Forms.Button{Text="Копировать",Left=350,Top=145,Width=125,Height=32};var close=new System.Windows.Forms.Button{Text="Закрыть",Left=485,Top=145,Width=120,Height=32,DialogResult=System.Windows.Forms.DialogResult.Cancel};
   choices.SelectedIndexChanged+=delegate{try{text.Text=format(choices.SelectedIndex+1);copy.Enabled=true;}catch(System.Exception ex){text.Text="Преобразование недоступно: "+ex.Message;copy.Enabled=false;}};
   copy.Click+=delegate{try{System.Windows.Forms.Clipboard.SetText(text.Text);DialogResult=System.Windows.Forms.DialogResult.OK;}catch(System.Exception ex){System.Windows.Forms.MessageBox.Show(this,ex.Message,"Буфер обмена");}};
   Controls.AddRange(new System.Windows.Forms.Control[]{choices,text,copy,close});AcceptButton=copy;CancelButton=close;choices.SelectedIndex=0;
  }
 }
}
