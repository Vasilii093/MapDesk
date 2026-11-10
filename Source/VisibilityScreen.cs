using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
namespace AlpineQuestPort {
    // UI captures values once; the worker never reads controls or mutable overlay state.
    internal sealed class VisibilityScreenInput {
        internal int Width,Height,Size,Zoom,Opacity;
        internal double Left,Top,Right,Bottom,CenterX,CenterY,Tilt,Rotation;
        internal byte[] Source;
        internal Bitmap Render(CancellationToken token){
            int width=Width,height=Height,sw=Size;var pixels=new byte[checked(width*height*4)];double scale=Math.Pow(2,14-Zoom),cx=CenterX*scale,cy=CenterY*scale,angle=Tilt*Math.PI/180,c=Math.Cos(angle),k=Math.Sin(angle)*.6,yaw=Rotation*Math.PI/180,rc=Math.Cos(yaw),rs=Math.Sin(yaw),world=GeoMath.WorldSize(14),sx=sw/(Right-Left),sy=sw/(Bottom-Top);int alpha=Opacity*255/100;
            Parallel.For(0,height,new ParallelOptions{CancellationToken=token,MaxDegreeOfParallelism=Math.Max(1,Math.Min(4,Environment.ProcessorCount-1))},row=>{
                double vy=row+.5-height/2.0,py=vy/(c+k*vy/height),factor=1-k*py/height;
                for(int col=0;col<width;col++){double vx=(col+.5-width/2.0)*factor,wx=cx+(vx*rc+py*rs)*scale,wy=cy+(-vx*rs+py*rc)*scale;wx=(wx%world+world)%world;double fx=(wx-Left)*sx,fy=(wy-Top)*sy;if(fx<0||fy<0||fx>=sw||fy>=sw)continue;int src=((int)fy*sw+(int)fx)*4;if(Source[src+3]==0)continue;int dst=(row*width+col)*4;pixels[dst]=Source[src];pixels[dst+1]=Source[src+1];pixels[dst+2]=Source[src+2];pixels[dst+3]=(byte)alpha;}
            });token.ThrowIfCancellationRequested();var image=new Bitmap(width,height,PixelFormat.Format32bppArgb);try{var bits=image.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);try{Marshal.Copy(pixels,0,bits.Scan0,pixels.Length);}finally{image.UnlockBits(bits);}return image;}catch{image.Dispose();throw;}
        }
    }
}
