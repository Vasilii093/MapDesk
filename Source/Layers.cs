using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

namespace AlpineQuestPort {
    public sealed class LayerDefinition {
        public MapSource Source;
        public bool Visible=true,Dem;
        public int Opacity=100;
        public LayerDefinition Copy(){return new LayerDefinition{Source=Source.Copy(),Visible=Visible,Dem=Dem,Opacity=Opacity};}
        public override string ToString(){return (Visible?"● ":"○ ")+Source.Name+" · "+Opacity+"%";}
        public void Validate(){if(Source==null||Opacity<0||Opacity>100||Source.MinZoom<0||Source.MaxZoom>20||Source.MinZoom>Source.MaxZoom)throw new InvalidDataException("Некорректный слой карты.");Source.TileUrl(Source.MinZoom,0,0);}
    }
    public sealed class LayerConfiguration {
        public List<LayerDefinition> Layers=new List<LayerDefinition>();
        public void Save(string path){foreach(var layer in Layers)layer.Validate();AtomicSettings.Save(path,stream=>new XmlSerializer(typeof(LayerConfiguration)).Serialize(stream,this));}
        public static LayerConfiguration Load(string path){return AtomicSettings.Load(path,stream=>{using(var r=XmlReader.Create(stream,AtomicSettings.ReaderSettings())){var config=(LayerConfiguration)new XmlSerializer(typeof(LayerConfiguration)).Deserialize(r);if(config.Layers.Count>6)throw new InvalidDataException("Допускается до шести дополнительных слоёв.");foreach(var layer in config.Layers)layer.Validate();return config;}});}
    }
    internal sealed class RasterLayer:IDisposable {
        public readonly LayerDefinition Definition;
        public readonly TileStore Tiles;
        readonly ElevationStore dem;
        public RasterLayer(LayerDefinition definition,string data,Action changed){Definition=definition.Copy();if(definition.Dem){dem=new ElevationStore(Path.Combine(data,"elevation-cache"),true);Tiles=dem.Tiles;dem.Changed+=delegate{changed();};}else Tiles=new TileStore(Path.Combine(data,"tile-cache-v2",definition.Source.CacheKey),null,Definition.Source);Tiles.Changed+=delegate{changed();};}
        public Image Get(int z,int x,int y,bool request){if(dem!=null){var tile=dem.Get(z,x,y);if(tile==null||tile.Image==null)return null;var copy=(Image)tile.Image.Clone();copy.Tag=tile.Image;return copy;}return request?Tiles.GetTile(z,x,y):Tiles.GetCachedTile(z,x,y);}
        public void Dispose(){if(dem!=null)dem.Dispose();else Tiles.Dispose();}
    }
    internal sealed class LayersDialog:Form {
        readonly ListBox list=new ListBox{Dock=DockStyle.Fill,IntegralHeight=false};
        readonly ComboBox catalog=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=410};
        readonly TrackBar opacity=new TrackBar{Minimum=0,Maximum=100,TickFrequency=10,Width=280};
        readonly CheckBox visible=new CheckBox{Text="Показывать выбранный слой",AutoSize=true};
        readonly CheckBox cache=new CheckBox{Text="Принудительно сохранять просмотренные тайлы на диск",AutoSize=true};
        readonly Label detail=new Label{Width=690,Height=44};
        readonly MapCanvas map;
        readonly Action apply;
        bool refreshing;
        public LayersDialog(MapCanvas canvas,IEnumerable<MapSource> sources,Action save){
            map=canvas;apply=save;Text="Карты на экране · слои и кэш";ClientSize=new Size(730,620);MinimumSize=Size;Font=new Font("Segoe UI",10);StartPosition=FormStartPosition.CenterParent;
            var top=new FlowLayoutPanel{Dock=DockStyle.Top,Height=106,Padding=new Padding(10)};top.Controls.Add(new Label{Text="Список сверху вниз: верхний слой → нижний. Базовая карта — под ними.",Width=700,Height=24});foreach(var source in sources)catalog.Items.Add(source);catalog.SelectedIndex=0;catalog.DropDownWidth=560;top.Controls.Add(catalog);
            Add(top,"Добавить",delegate{var source=(MapSource)catalog.SelectedItem;AddLayer(new LayerDefinition{Source=source.Copy()});});Add(top,"Цветной DEM",delegate{AddLayer(new LayerDefinition{Dem=true,Opacity=45,Source=new MapSource{Id="colored-dem",Name="Цветные высоты DEM",Group="Высоты",Template="https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png",MaxZoom=14,Attribution="Terrain Tiles / Mapzen · AWS Open Data"}});});
            var lower=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=242,Padding=new Padding(10)};
            lower.Controls.Add(visible);lower.Controls.Add(opacity);var amount=new Label{Width=65,Margin=new Padding(3,10,3,3)};lower.Controls.Add(amount);lower.SetFlowBreak(amount,true);
            Add(lower,"Выше",delegate{MoveLayer(-1);});Add(lower,"Ниже",delegate{MoveLayer(1);});Add(lower,"Удалить слой",delegate{int i=SelectedIndex;if(i>=0){map.Layers.RemoveAt(i);Commit();}});Add(lower,"Закрыть",Close);lower.Controls.Add(detail);lower.SetFlowBreak(detail,true);lower.Controls.Add(cache);lower.SetFlowBreak(cache,true);
            lower.Controls.Add(new Label{Text="Кэш: "+Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MapDeskPreview","tile-cache-v2")+"\nСохраняются загруженные участки и уровни масштаба. Прозрачный слой показывает нижнюю карту; непрозрачный закрывает её.",Width=700,Height=64});
            Controls.Add(list);Controls.Add(lower);Controls.Add(top);
            list.SelectedIndexChanged+=delegate{RefreshControls();};visible.CheckedChanged+=delegate{if(refreshing)return;int i=SelectedIndex;if(i>=0)map.Layers[i].Visible=visible.Checked;else map.Settings.BaseVisible=visible.Checked;Commit();};opacity.ValueChanged+=delegate{amount.Text=opacity.Value+"%";if(refreshing)return;int i=SelectedIndex;if(i>=0)map.Layers[i].Opacity=opacity.Value;else map.Settings.BaseOpacity=opacity.Value;Commit();};cache.CheckedChanged+=delegate{if(refreshing)return;map.Settings.ForceTileCache=cache.Checked;Commit();};Fill(0);
        }
        static void Add(FlowLayoutPanel panel,string text,Action click){var button=new Button{Text=text,AutoSize=true,Height=30};button.Click+=delegate{click();};panel.Controls.Add(button);}
        int SelectedIndex {get{return list.SelectedIndex>=0&&list.SelectedIndex<map.Layers.Count?map.Layers.Count-1-list.SelectedIndex:-1;}}
        void Fill(int selected){refreshing=true;list.Items.Clear();foreach(var layer in map.Layers.AsEnumerable().Reverse())list.Items.Add(layer);list.Items.Add("Базовая · "+map.Tiles.Source.Name+" · "+map.Settings.BaseOpacity+"%");list.SelectedIndex=Math.Max(0,Math.Min(selected,list.Items.Count-1));refreshing=false;RefreshControls();}
        void RefreshControls(){refreshing=true;int i=SelectedIndex;visible.Checked=i>=0?map.Layers[i].Visible:map.Settings.BaseVisible;opacity.Value=i>=0?map.Layers[i].Opacity:map.Settings.BaseOpacity;cache.Checked=map.Settings.ForceTileCache;detail.Text=i>=0?map.Layers[i].Source.Attribution:"Базовый источник меняется кнопкой «Карты». Дополнительные слои остаются на месте.";refreshing=false;}
        void Commit(){int selected=list.SelectedIndex;apply();Fill(selected);}
        void AddLayer(LayerDefinition layer){if(map.Layers.Count>=6){MessageBox.Show(this,"Допускается до шести дополнительных слоёв.");return;}map.Layers.Add(layer);Commit();list.SelectedIndex=0;}
        void MoveLayer(int direction){int i=SelectedIndex,j=i-direction;if(i<0||j<0||j>=map.Layers.Count)return;var layer=map.Layers[i];map.Layers.RemoveAt(i);map.Layers.Insert(j,layer);int selected=map.Layers.Count-1-j;apply();Fill(selected);}
    }
    sealed partial class MapCanvas {
        public List<LayerDefinition> Layers=new List<LayerDefinition>();
        readonly List<RasterLayer> rasterLayers=new List<RasterLayer>();

        float rasterOpacity=1;
        public void ConfigureLayers(string data){var old=new List<RasterLayer>(rasterLayers);rasterLayers.Clear();foreach(var definition in Layers){definition.Validate();var match=old.FirstOrDefault(v=>v.Definition.Dem==definition.Dem&&v.Definition.Source.CacheKey==definition.Source.CacheKey);if(match!=null){old.Remove(match);match.Definition.Visible=definition.Visible;match.Definition.Opacity=definition.Opacity;rasterLayers.Add(match);}else rasterLayers.Add(new RasterLayer(definition,data,RequestFrame));}foreach(var layer in old)layer.Dispose();Tiles.ForceDiskCache=Settings.ForceTileCache;Tiles.RevalidateSaved=Settings.RevalidateSaved;RequestFrame();}
        void PaintTiles(Graphics g,double minX,double maxX,double minY,double maxY,double left,double top,long n){
            Tiles.ForceDiskCache=Settings.ForceTileCache;Tiles.RevalidateSaved=Settings.RevalidateSaved;
            try{rasterOpacity=Settings.BaseOpacity/100f;if(Settings.BaseVisible&&rasterOpacity>0)PaintBaseTiles(g,minX,maxX,minY,maxY,left,top,n);else Tiles.DiscardQueued();
                foreach(var layer in rasterLayers){var def=layer.Definition;layer.Tiles.ForceDiskCache=Settings.ForceTileCache;layer.Tiles.RevalidateSaved=Settings.RevalidateSaved;layer.Tiles.OnlineEnabled=Tiles.OnlineEnabled&&def.Visible&&def.Opacity>0;if(!def.Visible||def.Opacity==0||Zoom<layer.Tiles.Source.MinZoom){layer.Tiles.DiscardQueued();continue;}rasterOpacity=def.Opacity/100f;PaintExtraLayer(g,layer,minX,maxX,minY,maxY,left,top,n);}
            }finally{rasterOpacity=1;}
        }
        void PaintExtraLayer(Graphics g,RasterLayer layer,double minX,double maxX,double minY,double maxY,double left,double top,long n){
            int z=Math.Max(layer.Tiles.Source.MinZoom,Math.Min(Zoom,layer.Tiles.Source.MaxZoom)-Settings.SourceZoomOffset);layer.Tiles.BeginFrame();try{
                for(long y=(long)Math.Floor(minY/256);y<=(long)Math.Floor(maxY/256);y++){if(y<0||y>=n)continue;for(long x=(long)Math.Floor(minX/256);x<=(long)Math.Floor(maxX/256);x++){
                    long tx=((x%n)+n)%n;var rect=new RectangleF((float)(x*256-left),(float)(y*256-top),256,256);
                    for(int level=z;level>=Math.Max(layer.Tiles.Source.MinZoom,z-4);level--){long divisor=1L<<(Zoom-level);using(var tile=layer.Get(level,(int)(tx/divisor),(int)(y/divisor),level>=z-1)){if(tile==null)continue;DrawTileImage(g,tile,rect,new RectangleF((float)((tx%divisor)*tile.Width/(double)divisor),(float)((y%divisor)*tile.Height/(double)divisor),(float)(tile.Width/(double)divisor),(float)(tile.Height/(double)divisor)),null);break;}}
                }}
            }finally{layer.Tiles.EndFrame();}
        }
        void DrawLayerCredits(Graphics g){if(!Layers.Any(v=>v.Visible&&v.Opacity>0))return;string text=String.Join(" · ",new[]{Settings.BaseVisible?Tiles.Source.Attribution:""}.Concat(Layers.Where(v=>v.Visible&&v.Opacity>0).Select(v=>v.Source.Attribution)).Where(v=>!String.IsNullOrWhiteSpace(v)).Distinct());using(var font=new Font("Segoe UI",7)){var size=g.MeasureString(text,font,Math.Max(100,Width-20));int height=Math.Min(65,(int)Math.Ceiling(size.Height)+8);var box=new RectangleF(8,Height-height-2,Width-16,height);using(var brush=new SolidBrush(Color.FromArgb(230,Color.White)))g.FillRectangle(brush,box);g.DrawString(text,font,Brushes.DarkSlateGray,new RectangleF(box.X+4,box.Y+3,box.Width-8,box.Height-5));}}
    }
}
