using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
namespace AlpineQuestPort {
    public sealed class OfflineArea {
        public string Id=Guid.NewGuid().ToString("N"),Name=DateTime.Now.ToString("yyyy-MM-dd HH:mm"),State="Не загружено";
        public MapSource Source;
        public double Lat1,Lon1,Lat2,Lon2;
        public int Min,Max,Done,Total,Failed;
        public bool Dem=true;
        public DateTime Updated=DateTime.UtcNow;
        internal void Validate(){Guid id;if(!Guid.TryParseExact(Id,"N",out id)||Source==null||!GeoMath.IsValidCoordinate(Lat1,Lon1)||!GeoMath.IsValidCoordinate(Lat2,Lon2)||Min<0||Max>20||Min>Max||Done<0||Total<0||Done>Total)throw new InvalidDataException("Повреждена запись сохранённой области.");UserMapCatalog.Validate(Source);OfflinePlan.Create(new GeoPoint(Lat1,Lon1),new GeoPoint(Lat2,Lon2),Min,Max);}
    }
    public sealed class OfflineAreaCatalog {
        public List<OfflineArea> Areas=new List<OfflineArea>();
        static readonly object gate=new object();
        internal static OfflineAreaCatalog Load(string folder){string path=Path.Combine(folder,"offline-areas.xml");if(!File.Exists(path)&&!File.Exists(path+".bak"))return new OfflineAreaCatalog();return AtomicSettings.Load(path,stream=>{using(var r=XmlReader.Create(stream,AtomicSettings.ReaderSettings())){var value=(OfflineAreaCatalog)new XmlSerializer(typeof(OfflineAreaCatalog)).Deserialize(r);if(value.Areas.Count>1000)throw new InvalidDataException("Слишком много областей.");foreach(var area in value.Areas)area.Validate();return value;}});}
        internal static void Update(string folder,OfflineArea area){lock(gate){area.Validate();var catalog=Load(folder);catalog.Areas.RemoveAll(a=>a.Id==area.Id);area.Updated=DateTime.UtcNow;catalog.Areas.Add(area);AtomicSettings.Save(Path.Combine(folder,"offline-areas.xml"),stream=>new XmlSerializer(typeof(OfflineAreaCatalog)).Serialize(stream,catalog));}}
    }
    internal sealed class OfflineAreasForm:Form {
        readonly string folder;readonly ListView list=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,MultiSelect=false};
        internal OfflineAreasForm(string data){folder=data;Text="Сохранённые области";Width=850;Height=480;StartPosition=FormStartPosition.CenterParent;list.Columns.Add("Область",145);list.Columns.Add("Карта",190);list.Columns.Add("Уровни / DEM",125);list.Columns.Add("Состояние",180);list.Columns.Add("Обработано",130);Controls.Add(list);var resume=new Button{Text="Проверить / дозагрузить выбранную область",Dock=DockStyle.Bottom,Height=40};resume.Click+=delegate{if(list.SelectedItems.Count==0)return;var area=(OfflineArea)list.SelectedItems[0].Tag;using(var dialog=new OfflineDownloadForm(folder,area.Source,area.Max,new GeoPoint(area.Lat1,area.Lon1),new GeoPoint(area.Lat2,area.Lon2),area))dialog.ShowDialog(this);RefreshAreas();};Controls.Add(resume);Controls.Add(new Label{Text="После перезапуска выберите область и нажмите «Дозагрузить». Готовые файлы проверяются и используются повторно. Защищённые области не удаляются автоматически.",Dock=DockStyle.Bottom,Height=48});RefreshAreas();}
        void RefreshAreas(){list.Items.Clear();try{foreach(var area in OfflineAreaCatalog.Load(folder).Areas.OrderByDescending(a=>a.Updated)){var row=new ListViewItem(new[]{area.Name,area.Source.Name,"Z"+area.Min+"–"+area.Max+(area.Dem?" + DEM":""),area.State=="Загрузка"?"Ожидает продолжения":area.State,area.Done+" / "+area.Total+"; ошибок "+area.Failed}){Tag=area};list.Items.Add(row);}}catch(Exception ex){Diagnostics.Show(this,"offline.catalog.load",ex);}}
    }
}
