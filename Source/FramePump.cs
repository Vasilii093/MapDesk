using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;
namespace AlpineQuestPort {
 // Sleeps until there is changed content. At most one UI callback is in flight.
 internal sealed class FramePump : IDisposable {
  readonly Control owner;readonly Action paint;readonly AutoResetEvent request=new AutoResetEvent(false),completed=new AutoResetEvent(false);readonly ManualResetEvent stop=new ManualResetEvent(false);readonly Thread worker;readonly object gate=new object();
  bool disposed;volatile bool failed;int fallbackQueued,limit=300;
  internal bool HighResolution {get;private set;}
  internal int Limit {get{return Volatile.Read(ref limit);}set{Volatile.Write(ref limit,Math.Max(15,Math.Min(300,value)));}}
  sealed class TimerHandle:WaitHandle {internal TimerHandle(IntPtr handle){SafeWaitHandle=new SafeWaitHandle(handle,true);}}
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateWaitableTimerEx(IntPtr attributes,string name,uint flags,uint access);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetWaitableTimer(IntPtr handle,ref long due,int period,IntPtr callback,IntPtr argument,bool resume);
  internal FramePump(Control owner,Action paint){this.owner=owner;this.paint=paint;worker=new Thread(Run){IsBackground=true,Name="MapDesk frame pacing"};worker.Start();}
  internal void Request(){lock(gate){if(disposed)return;if(!failed){request.Set();return;}}
   if(owner.IsHandleCreated&&Interlocked.Exchange(ref fallbackQueued,1)==0)try{owner.BeginInvoke((Action)delegate{Interlocked.Exchange(ref fallbackQueued,0);lock(gate){if(disposed)return;}if(!owner.IsDisposed)paint();});}catch(InvalidOperationException){Interlocked.Exchange(ref fallbackQueued,0);}
  }
  void Run(){TimerHandle timer=null;try{
   IntPtr handle=CreateWaitableTimerEx(IntPtr.Zero,null,2,0x1F0003);HighResolution=handle!=IntPtr.Zero;if(handle==IntPtr.Zero)handle=CreateWaitableTimerEx(IntPtr.Zero,null,0,0x1F0003);if(handle!=IntPtr.Zero)timer=new TimerHandle(handle);
   long last=0;var signal=new WaitHandle[]{stop,request};var done=new WaitHandle[]{stop,completed};
   while(WaitHandle.WaitAny(signal)==1){
    long remaining=last+(long)Math.Ceiling(Stopwatch.Frequency/(double)Limit)-Stopwatch.GetTimestamp();
    if(remaining>0){long due=-(long)Math.Ceiling(remaining*10000000.0/Stopwatch.Frequency);bool armed=timer!=null&&SetWaitableTimer(timer.SafeWaitHandle.DangerousGetHandle(),ref due,0,IntPtr.Zero,IntPtr.Zero,false);
     if(armed){if(WaitHandle.WaitAny(new WaitHandle[]{stop,timer})==0)break;}else if(stop.WaitOne((int)Math.Ceiling(remaining*1000.0/Stopwatch.Frequency)))break;
    }
    if(stop.WaitOne(0))break;
    // Consume redundant requests already represented by this frame.
    request.Reset();if(!owner.IsHandleCreated||owner.IsDisposed)continue;
    try{owner.BeginInvoke((Action)delegate{
     lock(gate){if(disposed)return;}
     try{var form=owner.FindForm();if(!owner.IsDisposed&&owner.Visible&&(form==null||form.WindowState!=FormWindowState.Minimized)){last=Stopwatch.GetTimestamp();paint();}}
     finally{lock(gate){if(!disposed)completed.Set();}}
    });}catch(InvalidOperationException){continue;}
    if(WaitHandle.WaitAny(done)==0)break;
   }
  }catch(Exception){failed=true;Request();}
  finally{if(timer!=null)timer.Dispose();}}
  public void Dispose(){lock(gate){if(disposed)return;disposed=true;stop.Set();}worker.Join();request.Dispose();completed.Dispose();stop.Dispose();}
 }
}
