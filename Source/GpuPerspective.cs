using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace AlpineQuestPort {
 // One UI-thread-owned WGL context. FBO rendering is independent of window visibility.
 internal sealed class GpuPerspective : IDisposable {
  NativeWindow window;IntPtr dc,context;uint output,framebuffer;Bitmap bitmap;int ow,oh;
  internal double ReadbackMilliseconds {get;private set;}
  internal double FrameMilliseconds {get;private set;}
  internal string Renderer {get;private set;}
  [StructLayout(LayoutKind.Sequential)] struct Pfd {
   public ushort size,version;public uint flags;public byte type,color,red,redShift,green,greenShift,blue,blueShift,alpha,alphaShift,accum,accumRed,accumGreen,accumBlue,accumAlpha,depth,stencil,aux,layer,reserved;public uint layerMask,visibleMask,damageMask;
  }
  [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window,IntPtr dc);
  [DllImport("gdi32.dll")] static extern int ChoosePixelFormat(IntPtr dc,ref Pfd pfd);
  [DllImport("gdi32.dll")] static extern bool SetPixelFormat(IntPtr dc,int format,ref Pfd pfd);
  [DllImport("opengl32.dll")] static extern IntPtr wglCreateContext(IntPtr dc);
  [DllImport("opengl32.dll")] static extern bool wglMakeCurrent(IntPtr dc,IntPtr rc);
  [DllImport("opengl32.dll")] static extern bool wglDeleteContext(IntPtr rc);
  [DllImport("opengl32.dll",CharSet=CharSet.Ansi)] static extern IntPtr wglGetProcAddress(string name);
  [DllImport("opengl32.dll")] static extern IntPtr glGetString(uint name);
  [DllImport("opengl32.dll")] static extern uint glGetError();
  [DllImport("opengl32.dll")] static extern void glGetIntegerv(uint name,out int value);
  [DllImport("opengl32.dll")] static extern void glGenTextures(int count,out uint texture);
  [DllImport("opengl32.dll")] static extern void glDeleteTextures(int count,ref uint texture);
  [DllImport("opengl32.dll")] static extern void glBindTexture(uint target,uint texture);
  [DllImport("opengl32.dll")] static extern void glTexParameteri(uint target,uint name,int value);
  [DllImport("opengl32.dll")] static extern void glTexImage2D(uint target,int level,int internalFormat,int width,int height,int border,uint format,uint type,IntPtr data);
  [DllImport("opengl32.dll")] static extern void glTexSubImage2D(uint target,int level,int x,int y,int width,int height,uint format,uint type,IntPtr data);
  [DllImport("opengl32.dll")] static extern void glViewport(int x,int y,int width,int height);
  [DllImport("opengl32.dll")] static extern void glEnable(uint cap);
  [DllImport("opengl32.dll")] static extern void glDisable(uint cap);
  [DllImport("opengl32.dll")] static extern void glClear(uint mask);
  [DllImport("opengl32.dll")] static extern void glClearColor(float r,float g,float b,float a);
  [DllImport("opengl32.dll")] static extern void glBegin(uint mode);
  [DllImport("opengl32.dll")] static extern void glEnd();
  [DllImport("opengl32.dll")] static extern void glTexCoord2d(double s,double t);
  [DllImport("opengl32.dll")] static extern void glVertex4d(double x,double y,double z,double w);
  [DllImport("opengl32.dll")] static extern void glReadPixels(int x,int y,int width,int height,uint format,uint type,IntPtr data);
  [DllImport("opengl32.dll")] static extern void glHint(uint target,uint mode);
  [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Gen(int count,out uint id);
  [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Delete(int count,ref uint id);
  [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Bind(uint target,uint id);
  [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Attach(uint target,uint attachment,uint textureTarget,uint texture,int level);
  [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint Status(uint target);
  Gen gen;Delete delete;Bind bind;Attach attach;Status status;
  T Function<T>(string name) where T:class {IntPtr p=wglGetProcAddress(name);if(p==IntPtr.Zero||p.ToInt64()==-1||(p.ToInt64()>=1&&p.ToInt64()<=3))p=wglGetProcAddress(name+"EXT");if(p==IntPtr.Zero||p.ToInt64()==-1||(p.ToInt64()>=1&&p.ToInt64()<=3))throw new NotSupportedException("Нет "+name);return Marshal.GetDelegateForFunctionPointer(p,typeof(T)) as T;}
  internal GpuPerspective(){try{
   window=new NativeWindow();window.CreateHandle(new CreateParams{Caption="MapDesk GPU",Width=1,Height=1,Style=unchecked((int)0x80000000),ClassStyle=0x20});dc=GetDC(window.Handle);
   var p=new Pfd{size=(ushort)Marshal.SizeOf(typeof(Pfd)),version=1,flags=0x24,color=32,alpha=8};int format=ChoosePixelFormat(dc,ref p);
   if(format==0||!SetPixelFormat(dc,format,ref p))throw new NotSupportedException("Нет формата GPU");context=wglCreateContext(dc);if(context==IntPtr.Zero||!wglMakeCurrent(dc,context))throw new NotSupportedException("Не создан контекст GPU");
   Renderer=Marshal.PtrToStringAnsi(glGetString(0x1F01))??"";if(Renderer.Length==0||Renderer.IndexOf("GDI Generic",StringComparison.OrdinalIgnoreCase)>=0||Renderer.IndexOf("llvmpipe",StringComparison.OrdinalIgnoreCase)>=0||Renderer.IndexOf("Software",StringComparison.OrdinalIgnoreCase)>=0)throw new NotSupportedException("Аппаратный OpenGL недоступен: "+Renderer);
   gen=Function<Gen>("glGenFramebuffers");delete=Function<Delete>("glDeleteFramebuffers");bind=Function<Bind>("glBindFramebuffer");attach=Function<Attach>("glFramebufferTexture2D");status=Function<Status>("glCheckFramebufferStatus");gen(1,out framebuffer);glGenTextures(1,out output);glDisable(0x0BD0);glEnable(0x0DE1);glHint(0x0C50,0x1102);
  }catch{Dispose();throw;}finally{wglMakeCurrent(IntPtr.Zero,IntPtr.Zero);}}
  void Configure(uint id,int width,int height){glBindTexture(0x0DE1,id);glTexParameteri(0x0DE1,0x2801,0x2601);glTexParameteri(0x0DE1,0x2800,0x2601);glTexParameteri(0x0DE1,0x2802,0x812F);glTexParameteri(0x0DE1,0x2803,0x812F);glTexImage2D(0x0DE1,0,0x8058,width,height,0,0x80E1,0x1401,IntPtr.Zero);}
  sealed class Texture {public uint Id;public long Bytes,Used;}
  readonly System.Collections.Generic.Dictionary<Image,Texture> textures=new System.Collections.Generic.Dictionary<Image,Texture>();
  long textureBytes,frameNumber;
  internal int TextureUploads {get;private set;}
  internal long TextureBytes {get{return textureBytes;}}
  void TrimTextures(){if(textureBytes<=64L*1024*1024&&frameNumber%60!=0)return;var keys=new System.Collections.Generic.List<Image>(textures.Keys);keys.Sort((a,b)=>textures[a].Used.CompareTo(textures[b].Used));foreach(var key in keys){var t=textures[key];if(textureBytes<=64L*1024*1024&&frameNumber-t.Used<=60)break;glDeleteTextures(1,ref t.Id);textureBytes-=t.Bytes;textures.Remove(key);}}

  int sceneWidth,sceneHeight;double sceneC,sceneK,rotationCos=1,rotationSin;
  internal void DrawScene(Graphics g,int width,int height,double degrees,Action<GpuPerspective> draw,double rotation=0){
   var frameWatch=System.Diagnostics.Stopwatch.StartNew();
   if(!wglMakeCurrent(dc,context))throw new InvalidOperationException("GPU-контекст недоступен");
   try{
    int limit;glGetIntegerv(0x0D33,out limit);if(width>limit||height>limit)throw new NotSupportedException("Размер карты превышает лимит GPU");
    bind(0x8D40,framebuffer);
    if(ow!=width||oh!=height){Configure(output,width,height);attach(0x8D40,0x8CE0,0x0DE1,output,0);if(status(0x8D40)!=0x8CD5)throw new NotSupportedException("Неполный буфер GPU");if(bitmap!=null)bitmap.Dispose();bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb);ow=width;oh=height;}
    rotationCos=Math.Cos(rotation*Math.PI/180);rotationSin=Math.Sin(rotation*Math.PI/180);sceneWidth=width;sceneHeight=height;sceneC=Math.Cos(degrees*Math.PI/180);sceneK=Math.Sin(degrees*Math.PI/180)*0.6;
    glViewport(0,0,width,height);glClearColor(0.9f,0.92f,0.9f,1);glColorMask(1,1,1,1);glClear(0x4000);glColorMask(1,1,1,0);
    frameNumber++;draw(this);
    var readWatch=System.Diagnostics.Stopwatch.StartNew();
    var dest=bitmap.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);try{glReadPixels(0,0,width,height,0x80E1,0x1401,dest.Scan0);}finally{bitmap.UnlockBits(dest);}
    ReadbackMilliseconds=readWatch.Elapsed.TotalMilliseconds;
    uint error=glGetError();if(error!=0)throw new InvalidOperationException("Ошибка GPU: "+error);g.DrawImageUnscaled(bitmap,0,0);FrameMilliseconds=frameWatch.Elapsed.TotalMilliseconds;
   }finally{TrimTextures();wglMakeCurrent(IntPtr.Zero,IntPtr.Zero);}
  }
  [DllImport("opengl32.dll")] static extern void glBlendFunc(uint source,uint dest);
  [DllImport("opengl32.dll")] static extern void glColor4f(float r,float g,float b,float a);
  [DllImport("opengl32.dll")] static extern void glColorMask(byte r,byte g,byte b,byte a);
  internal void DrawTile(Image image,RectangleF rect,RectangleF source,float opacity=1){
   glEnable(0x0BE2);glBlendFunc(0x0302,0x0303);glColor4f(1,1,1,opacity);
   var key=image.Tag as Image??image;Texture texture;uint id;if(!textures.TryGetValue(key,out texture)){
    glGenTextures(1,out id);texture=new Texture{Id=id,Bytes=(long)image.Width*image.Height*4,Used=frameNumber};textures.Add(key,texture);textureBytes+=texture.Bytes;TextureUploads++;Configure(id,image.Width,image.Height);
    var b=image as Bitmap;if(b==null)throw new NotSupportedException("Формат тайла GPU");var data=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);try{if(data.Stride!=b.Width*4)throw new NotSupportedException("Шаг тайла GPU");glTexSubImage2D(0x0DE1,0,0,0,b.Width,b.Height,0x80E1,0x1401,data.Scan0);}finally{b.UnlockBits(data);}
   }else {id=texture.Id;glBindTexture(0x0DE1,id);}texture.Used=frameNumber;
   double x=rect.Left-sceneWidth/2.0,y=rect.Top-sceneHeight/2.0,u=source.Left/image.Width,v=source.Top/image.Height,ur=source.Right/image.Width,vb=source.Bottom/image.Height;
   glBegin(0x0007);Vertex(u,v,x,y,sceneWidth,sceneHeight,sceneC,sceneK);Vertex(ur,v,x+rect.Width,y,sceneWidth,sceneHeight,sceneC,sceneK);Vertex(ur,vb,x+rect.Width,y+rect.Height,sceneWidth,sceneHeight,sceneC,sceneK);Vertex(u,vb,x,y+rect.Height,sceneWidth,sceneHeight,sceneC,sceneK);glEnd();
  }
  void Vertex(double u,double v,double x,double y,int width,int height,double c,double k){double rx=x*rotationCos-y*rotationSin,ry=x*rotationSin+y*rotationCos;x=rx;y=ry;glTexCoord2d(u,v);glVertex4d(2*x/width,2*c*y/height,0,1-k*y/height);}
  public void Dispose(){if(context!=IntPtr.Zero){if(wglMakeCurrent(dc,context)){foreach(var t in textures.Values){uint id=t.Id;glDeleteTextures(1,ref id);}textures.Clear();textureBytes=0;if(output!=0)glDeleteTextures(1,ref output);if(framebuffer!=0&&delete!=null)delete(1,ref framebuffer);}wglMakeCurrent(IntPtr.Zero,IntPtr.Zero);wglDeleteContext(context);context=IntPtr.Zero;}if(dc!=IntPtr.Zero&&window!=null){ReleaseDC(window.Handle,dc);dc=IntPtr.Zero;}if(window!=null){window.DestroyHandle();window=null;}if(bitmap!=null){bitmap.Dispose();bitmap=null;}}
 }
}
