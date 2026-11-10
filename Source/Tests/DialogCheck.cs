using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AlpineQuestPort;
class DialogCheck {
 [STAThread]static int Main(string[] args){try{Application.EnableVisualStyles();using(var d=new MapsDialog(MapSource.BuiltIn(),MapSource.BuiltIn()[0],delegate{})){
 d.Show();Application.DoEvents();d.PerformLayout();
 foreach(Control panel in d.Controls)foreach(Control c in panel.Controls)if(c is Button&&!panel.ClientRectangle.Contains(c.Bounds))throw new Exception("Button clipped: "+c.Text+" "+c.Bounds+" parent "+panel.ClientRectangle);
 using(var b=new Bitmap(d.Width,d.Height)){d.DrawToBitmap(b,new Rectangle(0,0,d.Width,d.Height));b.Save(args[0]);}
 Console.WriteLine("PASS: map catalog buttons remain inside parent bounds");}return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}
