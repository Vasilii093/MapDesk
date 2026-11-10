using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AlpineQuestPort {
    sealed partial class MainForm {
        Panel objectSidebar,sidebarRail,mainContent;
        readonly Button exitMapFullscreen=new Button{Text="Вернуться · Esc / F11",Width=174,Height=32,BackColor=Color.White,ForeColor=Color.FromArgb(23,67,59),FlatStyle=FlatStyle.Flat,AccessibleName="Выйти из полноэкранной карты"};
        readonly Dictionary<Control,bool> chromeVisibility=new Dictionary<Control,bool>();
        bool mapFullscreen;
        Rectangle savedWindowBounds;
        FormWindowState savedWindowState;
        FormBorderStyle savedBorderStyle;
        void InitializeDisplayLayout(Panel sidebar,Panel content,ToolStrip sourceTools){
            objectSidebar=sidebar;mainContent=content;
            var collapse=new Button{Text="‹  Свернуть панель",Dock=DockStyle.Top,Height=30,FlatStyle=FlatStyle.Flat,AccessibleName="Свернуть объекты"};
            collapse.Click+=delegate{SetSidebarCollapsed(true);};sidebar.Controls.Add(collapse);collapse.SendToBack();
            sidebarRail=new Panel{Dock=DockStyle.Left,Width=36,BackColor=sidebar.BackColor,Visible=false};
            var expand=new Button{Text="☰",Dock=DockStyle.Top,Height=38,FlatStyle=FlatStyle.Flat,AccessibleName="Развернуть объекты"};
            expand.Click+=delegate{SetSidebarCollapsed(false);};sidebarRail.Controls.Add(expand);content.Controls.Add(sidebarRail);
            var fullscreenButton=new ToolStripButton("⛶ Карта · F11"){Alignment=ToolStripItemAlignment.Right,Overflow=ToolStripItemOverflow.Never,ToolTipText="Полноэкранная карта · выход по Esc или F11"};
            fullscreenButton.Click+=delegate{ToggleMapFullscreen();};sourceTools.Items.Insert(0,fullscreenButton);
            exitMapFullscreen.Visible=false;exitMapFullscreen.Anchor=AnchorStyles.Top|AnchorStyles.Right;exitMapFullscreen.Click+=delegate{ToggleMapFullscreen();};map.Controls.Add(exitMapFullscreen);
            map.Resize+=delegate{exitMapFullscreen.Location=new Point(Math.Max(0,map.ClientSize.Width-exitMapFullscreen.Width-12),12);};
            ApplySidebarLayout();
        }
        internal void SetSidebarCollapsed(bool collapsed){settings.SidebarCollapsed=collapsed;ApplySidebarLayout();map.Focus();}
        void ApplySidebarLayout(){if(objectSidebar==null)return;mainContent.SuspendLayout();objectSidebar.Visible=!mapFullscreen&&!settings.SidebarCollapsed;sidebarRail.Visible=!mapFullscreen&&settings.SidebarCollapsed;mainContent.ResumeLayout(true);map.RequestFrame();}
        internal void ToggleMapFullscreen(){
            SuspendLayout();
            if(!mapFullscreen){
                savedWindowState=WindowState;savedWindowBounds=WindowState==FormWindowState.Normal?Bounds:RestoreBounds;savedBorderStyle=FormBorderStyle;
                var screenBounds=Screen.FromControl(this).Bounds;chromeVisibility.Clear();foreach(Control control in Controls)if(control!=mainContent){chromeVisibility[control]=control.Visible;control.Visible=false;}
                mapFullscreen=true;WindowState=FormWindowState.Normal;FormBorderStyle=FormBorderStyle.None;Bounds=screenBounds;
            }else{
                mapFullscreen=false;WindowState=FormWindowState.Normal;FormBorderStyle=savedBorderStyle;Bounds=savedWindowBounds;WindowState=savedWindowState;
                foreach(var item in chromeVisibility)item.Key.Visible=item.Value;chromeVisibility.Clear();
            }
            ApplySidebarLayout();exitMapFullscreen.Visible=mapFullscreen;exitMapFullscreen.BringToFront();ResumeLayout(true);map.Focus();map.RequestFrame();
        }
    }
}
