using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace AlpineQuestPort {
 internal sealed partial class RouteDetailsForm {
  void InitializeHeightEditor(Action<GeoPoint> add){var controls=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=72,BackColor=Color.FromArgb(237,245,244),Padding=new Padding(8)};var mode=new CheckBox{Text="Поднять точку на профиле",AutoSize=true,Margin=new Padding(4,8,12,4)};var height=new NumericUpDown{Minimum=0,Maximum=10000,DecimalPlaces=1,Increment=1,Width=90,Enabled=false};var label=new Label{Text="Над рельефом, м",AutoSize=true,Margin=new Padding(4,8,4,4)};var save=new Button{Text="Сохранить точку на карте",AutoSize=true,Enabled=false};var readout=new Label{AutoSize=true,Text="Включите режим, выберите место на графике и тяните вверх или введите высоту.",Margin=new Padding(4,8,4,4)};controls.Controls.AddRange(new Control[]{mode,label,height,save,readout});Controls.Add(controls);plot.BringToFront();bool updating=false;
   mode.CheckedChanged+=delegate{plot.LiftMode=mode.Checked;plot.Cursor=mode.Checked?Cursors.Cross:Cursors.Default;};height.ValueChanged+=delegate{if(!updating)plot.SetLift((double)height.Value);};plot.HeightSelectionChanged+=delegate{updating=true;var point=plot.RaisedPoint();height.Enabled=save.Enabled=point!=null;height.Value=(decimal)plot.Lift;readout.Text=point==null?"Нет DEM в выбранном месте — дождитесь высот.":"DEM "+profile.Heights[plot.SelectedIndex].ToString("0.0")+" м → точка "+point.Elevation.Value.ToString("0.0")+" м";updating=false;};save.Click+=delegate{var point=plot.RaisedPoint();if(point!=null){add(point);status.Text="Точка с заданной высотой сохранена на карте. Рельеф DEM не изменён.";}};
  }
 }
 sealed partial class MainForm {
  Panel objectCard;Label cardTitle,cardStats;FlowLayoutPanel cardActions;RoutePlot cardPlot;object cardObject;ElevationStore cardElevation;TerrainProfile cardProfile;Timer cardTimer;int cardCursor;bool cardHidden;
  void InitializeObjectCards(){objectCard=new Panel{Dock=DockStyle.Bottom,Height=240,BackColor=Color.White,Visible=false,Padding=new Padding(12,5,12,5)};cardTitle=new Label{Dock=DockStyle.Top,Height=26,Font=new Font("Segoe UI",11,FontStyle.Bold),ForeColor=Color.FromArgb(25,65,64)};cardStats=new Label{Dock=DockStyle.Top,AutoSize=true,MaximumSize=new Size(1000,0),Font=new Font("Segoe UI",9)};cardActions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(0,2,0,3)};cardPlot=new RoutePlot{Dock=DockStyle.Fill,SummaryStyle=true,BackColor=Color.White};cardPlot.TemporaryPoint=()=>map.TemporaryPoint;cardPlot.RemoveTemporary=map.ClearTemporaryPoint;cardPlot.Pick+=map.AddProfilePoint;cardPlot.Locate+=point=>map.ShowTemporaryPoint(point,true);objectCard.Controls.Add(cardPlot);objectCard.Controls.Add(cardStats);InitializeCardCoordinates();objectCard.Controls.Add(cardCoordinates);objectCard.Controls.Add(cardActions);objectCard.Controls.Add(cardTitle);Controls.Add(objectCard);objectCard.BringToFront();cardTimer=new Timer{Interval=150};cardTimer.Tick+=delegate{if(closingInProgress)return;if(map.HasDraft){objectCard.Visible=false;cardObject=null;return;}if(!Object.ReferenceEquals(cardObject,map.Selected)){cardHidden=false;ShowObjectCard(map.Selected);}if(!objectCard.Visible||cardElevation==null)return;try{cardElevation.Tiles.OnlineEnabled=map.Tiles.OnlineEnabled;if(cardProfile!=null){bool changed=false;for(int i=0;i<48;i++){int index=cardCursor++%cardProfile.Points.Length;if(GeoMath.IsFinite(cardProfile.Heights[index]))continue;var h=cardElevation.At(cardProfile.Points[index],14);if(h.HasValue){cardProfile.Heights[index]=h.Value;changed=true;}}if(changed){cardStats.Text=RouteTerrain.Summary(cardProfile);cardPlot.Invalidate();}}else{var point=cardObject as GeoPoint;if(point!=null){var dem=cardElevation.At(point,14);UpdateCardCoordinates(point);cardStats.Text="Рельеф: "+(dem.HasValue?dem.Value.ToString("0.0")+" м":"ожидание DEM")+(point.Elevation.HasValue?" · высота точки: "+point.Elevation.Value.ToString("0.0")+" м"+(dem.HasValue?" · над землёй "+(point.Elevation.Value-dem.Value).ToString("+0.0;-0.0;0.0")+" м":""):"");}}}catch(Exception ex){Diagnostics.Record("object.card",ex);cardStats.Text="Не удалось прочитать высоты: "+ex.Message;}};objectCard.SizeChanged+=delegate{LayoutObjectCard();};cardStats.SizeChanged+=delegate{LayoutObjectCard();};cardActions.SizeChanged+=delegate{LayoutObjectCard();};cardTimer.Start();FormClosed+=delegate{cardTimer.Dispose();cardCopyTips.Dispose();if(cardMoreMenu!=null)cardMoreMenu.Dispose();if(cardElevation!=null)cardElevation.Dispose();};}
  FlowLayoutPanel cardCoordinates;Label cardWgs,cardSk;Button cardCopyWgs,cardCopySk;ToolTip cardCopyTips;
  void InitializeCardCoordinates(){
   cardCoordinates=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};
   cardWgs=new Label{AutoSize=true,Font=cardStats.Font,Margin=Padding.Empty};cardSk=new Label{AutoSize=true,Font=cardStats.Font,Margin=Padding.Empty};
   cardCopyTips=new ToolTip();cardCopyWgs=CardCopyButton("Копировать широту и долготу",2);cardCopySk=CardCopyButton("Копировать X и Y (СК-42)",1);
   cardCoordinates.Controls.AddRange(new Control[]{cardWgs,cardCopyWgs,cardSk,cardCopySk});cardCoordinates.SizeChanged+=delegate{LayoutObjectCard();};
  }
  internal string CardCopyValue(GeoPoint point,int format){if(format==2)return CopyText(point,2);return CopyText(point,1).Split(new[]{" · "},StringSplitOptions.None)[0];}
  Button CardCopyButton(string caption,int format){
   var button=new Button{Size=new Size(24,21),FlatStyle=FlatStyle.Flat,Margin=new Padding(0,0,0,2),AccessibleName=caption,Cursor=Cursors.Hand};button.FlatAppearance.BorderSize=0;cardCopyTips.SetToolTip(button,caption);
   button.Paint+=delegate(object sender,PaintEventArgs e){using(var pen=new Pen(Color.FromArgb(24,124,121),1.3f)){int x=(button.ClientSize.Width-12)/2,y=(button.ClientSize.Height-13)/2;e.Graphics.DrawRectangle(pen,x+3,y,8,9);using(var brush=new SolidBrush(button.BackColor))e.Graphics.FillRectangle(brush,x,y+3,8,9);e.Graphics.DrawRectangle(pen,x,y+3,8,9);}};
   button.Click+=delegate{var point=cardObject as GeoPoint;if(point==null)return;try{Clipboard.SetText(CardCopyValue(point,format));SetMessage("Координаты скопированы");}catch(Exception ex){Diagnostics.Show(this,"object.card.copy",ex);}};return button;
  }
  void UpdateCardCoordinates(GeoPoint point){cardWgs.Text="Широта "+point.Lat.ToString("0.000000")+" · долгота "+point.Lon.ToString("0.000000");cardSk.Text=CardSk42(point);cardCopySk.Enabled=!cardSk.Text.Contains("недоступно");}
  bool cardLayoutBusy;string cardCoordinateText;double cardLat=double.NaN,cardLon=double.NaN;
  string CardSk42(GeoPoint point){
   if(cardCoordinateText!=null&&point.Lat==cardLat&&point.Lon==cardLon)return cardCoordinateText;
   cardLat=point.Lat;cardLon=point.Lon;
   try{cardCoordinateText=CardCopyValue(point,1)+" · СК-42 · м";}
   catch(Exception ex){Diagnostics.Record("object.card.coordinates",ex);cardCoordinateText="СК-42: недоступно для этой точки";}
   return cardCoordinateText;
  }
  void LayoutObjectCard(){
   if(cardLayoutBusy||objectCard==null)return;cardLayoutBusy=true;
   try{cardStats.MaximumSize=new Size(Math.Max(100,objectCard.ClientSize.Width-objectCard.Padding.Horizontal),0);
    if(cardCoordinates!=null){cardWgs.MaximumSize=cardSk.MaximumSize=cardStats.MaximumSize;}
    if(cardObject is Track)objectCard.Height=objectCard.Padding.Vertical+cardTitle.Height+cardActions.Height+cardStats.Height+130;
    if(cardObject is GeoPoint)objectCard.Height=objectCard.Padding.Vertical+cardTitle.Height+cardActions.Height+cardStats.Height+(cardCoordinates==null?0:cardCoordinates.Height)+4;
   }finally{cardLayoutBusy=false;}
  }
  ContextMenuStrip cardMoreMenu;Button cardMoreButton;
  void CardAction(string title,Action action,bool destructive=false){
   if(title=="Переименовать"||title=="Экспорт GPX…"||destructive){if(cardMoreMenu==null){cardMoreMenu=new ContextMenuStrip();cardMoreButton=new Button{Text="Ещё ⋯",AutoSize=true,FlatStyle=FlatStyle.Flat,BackColor=Color.White,ForeColor=Color.DimGray,Margin=new Padding(6,0,0,2)};cardMoreButton.FlatAppearance.BorderSize=0;cardMoreButton.Click+=delegate{cardMoreMenu.Show(cardMoreButton,new Point(0,cardMoreButton.Height));};cardActions.Controls.Add(cardMoreButton);}var item=cardMoreMenu.Items.Add(title,null,delegate{action();});if(destructive){item.ForeColor=Color.Firebrick;}return;}
   if(title=="Показать на карте")title="На карте";if(title=="Скрыть карточку")title="Скрыть ×";
var button=new Button{Text=title,AutoSize=true,Height=26,FlatStyle=FlatStyle.Flat,ForeColor=destructive?Color.Firebrick:Color.FromArgb(24,124,121),BackColor=Color.White,Margin=new Padding(0,0,5,2)};button.FlatAppearance.BorderSize=0;button.FlatAppearance.MouseOverBackColor=Color.FromArgb(238,244,243);button.Click+=delegate{action();};cardActions.Controls.Add(button);}
  void ShowObjectCard(object value){if(objectCard==null)return;cardObject=value;if(cardElevation!=null){cardElevation.Dispose();cardElevation=null;}cardProfile=null;cardPlot.Profile=null;foreach(Control control in cardActions.Controls.Cast<Control>().ToArray())control.Dispose();cardActions.Controls.Clear();if(cardMoreMenu!=null){cardMoreMenu.Dispose();cardMoreMenu=null;cardMoreButton=null;}if(cardHidden||(!(value is Track)&&!(value is GeoPoint))||map.HasDraft){objectCard.Visible=false;return;}var route=value as Track;var point=value as GeoPoint;cardTitle.Text=(route!=null?"Маршрут  ·  ":"Точка  ·  ")+(route!=null?route.Name:point.Name);objectCard.Height=route!=null?240:120;cardPlot.Visible=route!=null;cardCoordinates.Visible=point!=null;if(point!=null)UpdateCardCoordinates(point);objectCard.Visible=true;cardElevation=new ElevationStore(Path.Combine(dataDir,"elevation-cache"));
   CardAction("Показать на карте",()=>{if(route!=null)map.FitPoints(route.Points);else map.GoTo(point,map.Zoom);});
   if(route!=null){CardAction("Профиль высот",()=>OpenRoute(route));CardAction("Редактировать",()=>{objectCard.Visible=false;map.Selected=route;map.BeginEdit();map.Focus();});try{cardProfile=RouteTerrain.Create(route);cardPlot.Profile=cardProfile;cardStats.Text=RouteTerrain.Summary(cardProfile);}catch(Exception ex){cardStats.Text=ex.Message;}}
   else{cardStats.Text="Загрузка сведений о точке…";CardAction("Изменить",()=>{map.Selected=point;EditSelectedMarker();cardTitle.Text="Точка  ·  "+point.Name;});CardAction("Луч / профиль",()=>PinRay(point));}
   CardAction("Переименовать",()=>{string name=TextPrompt.Ask(this,"Название",route!=null?route.Name:point.Name);if(name==null)return;if(route!=null)route.Name=name;else point.Name=name;MarkChanged();RefreshObjects();cardTitle.Text=(route!=null?"Маршрут  ·  ":"Точка  ·  ")+name;});
   CardAction("Экспорт GPX…",()=>{using(var dialog=new SaveFileDialog{Filter="GPX|*.gpx",FileName="Объект.gpx"})if(dialog.ShowDialog(this)==DialogResult.OK)try{var document=new MapDocument();if(route!=null)document.Tracks.Add(route);else document.Waypoints.Add(point);MapFiles.SaveGpx(document,dialog.FileName);}catch(Exception ex){Diagnostics.Show(this,"object.export",ex);}});
   CardAction("Скрыть карточку",()=>{cardHidden=true;objectCard.Visible=false;});CardAction("Удалить",()=>{if(MessageBox.Show(this,"Удалить выбранный объект? Действие можно отменить Ctrl+Z.","Удаление",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes){RemoveObject(value);ShowObjectCard(null);}},true);
   LayoutObjectCard();cardPlot.Invalidate();
  }
 }
}
