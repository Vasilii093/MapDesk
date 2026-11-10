using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
namespace AlpineQuestPort {
    internal static class BackgroundJobs {
        static readonly object gate=new object();static readonly Dictionary<Task,string> jobs=new Dictionary<Task,string>();
        internal static int Count {get{lock(gate)return jobs.Count;}}
        internal static Task<T> Run<T>(string owner,Func<T> work){return Track(Task.Run(work),owner);}
        internal static Task Run(string owner,Action work){return Track(Task.Run(work),owner);}
        internal static Task Run(string owner,Func<Task> work){return Track(Task.Run(work),owner);}
        static T Track<T>(T task,string owner) where T:Task {lock(gate)jobs.Add(task,owner);task.ContinueWith(done=>{if(done.IsFaulted)Diagnostics.Record(owner,done.Exception);lock(gate)jobs.Remove(done);},TaskScheduler.Default);return task;}
        internal static async Task Drain(){while(true){Task[] pending;lock(gate)pending=jobs.Keys.ToArray();if(pending.Length==0)return;try{await Task.WhenAll(pending);}catch{ /* Each owner records and handles its result. */ }}}
        internal static void DisposeAfter(Task task,IDisposable resource){if(task==null||task.IsCompleted)resource.Dispose();else task.ContinueWith(done=>resource.Dispose(),TaskScheduler.Default);}
    }
}