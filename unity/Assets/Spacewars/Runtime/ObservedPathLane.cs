using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // A missing answer is a suspended observation, never an unreachable route.
    internal sealed class ObservedPathPending : Exception { }
    internal sealed partial class ObservedPathLane
    {
        // Technical in-flight transport window, independent of army/observation size.
        internal const int Capacity=64*8;
        private readonly object gate=new object();
        private readonly List<Job> jobs=new List<Job>();
        private readonly Dictionary<(PlayableOwner,int,int,PlayableRouteTargetKind,int),Job> lookup=new Dictionary<(PlayableOwner,int,int,PlayableRouteTargetKind,int),Job>();
        private static (PlayableOwner,int,int,PlayableRouteTargetKind,int) Key(Job j)=>(j.Work.Owner,j.Actor,j.Target,j.Kind,j.Candidate);
        private readonly Queue<Job> queue=new Queue<Job>();
        private readonly Dictionary<PlayableOwner,long> epochs=new Dictionary<PlayableOwner,long>();
        private int running;
        private readonly Thread thread;
        private bool stopping;private int stopped;private bool started;
        private Exception failure;private long use;
        private sealed class Job
        {
            internal ObservedPathWork Work;internal int Actor,Target,Candidate;internal PlayableRouteTargetKind Kind;
            internal NavPoint[] Path;internal bool Done,Enqueued,Running,Cancelled;internal long Used,Epoch;internal Action Before;
        }
        internal ObservedPathLane():this(true){}
        internal ObservedPathLane(bool start){thread=new Thread(Run){IsBackground=true,Name="Spacewars.Ai.ObservedPaths"};if(start)Start();}
        internal void Start(){lock(gate){if(stopping||started)return;started=true;thread.Start();}}
        internal bool IsStopped=>Volatile.Read(ref stopped)!=0;
        internal int Count {get{lock(gate)return queue.Count+running;}}
        internal int RetainedCount {get{lock(gate)return jobs.Count;}}
        internal bool HasPending {get{lock(gate)return jobs.Any(j=>!j.Done);}}
        internal void BeginProjection(PlayableOwner owner)
        {lock(gate){epochs.TryGetValue(owner,out var epoch);epochs[owner]=checked(epoch+1);}}
        internal void EndProjection(PlayableOwner owner)
        {
            lock(gate){
                epochs.TryGetValue(owner,out var epoch);
                foreach(var job in jobs.Where(j=>j.Work.Owner==owner&&j.Epoch!=epoch).ToArray()){job.Cancelled=true;jobs.Remove(job);lookup.Remove(Key(job));}
                var live=queue.Where(j=>!j.Cancelled).ToArray();queue.Clear();foreach(var job in live)queue.Enqueue(job);
                FillQueue();
            }
        }
        private void FillQueue()
        {
            // Retain one requested observation working set per owner, as the
            // synchronous snapshot already did. Stream it through the bounded
            // queue; never evict its early answers while later answers compute.
            foreach(var job in jobs){
                if(queue.Count+running>=Capacity)break;
                if(job.Done||job.Enqueued||job.Running||job.Cancelled)continue;
                job.Enqueued=true;queue.Enqueue(job);
            }
            Monitor.PulseAll(gate);
        }
        private static bool Same(Job j,ObservedPathWork w,int actor,int target,PlayableRouteTargetKind kind,int candidate)
            =>j.Actor==actor&&j.Target==target&&j.Kind==kind&&j.Candidate==candidate&&j.Work.Owner==w.Owner&&SameGeometry(j.Work.Geometry,w.Geometry)&&SameProfile(j.Work.Profile,w.Profile)&&j.Work.Radius==w.Radius&&j.Work.Speed==w.Speed&&j.Work.Turn==w.Turn;
        private static bool SameGeometry(NavGeometry a,NavGeometry b)=>ReferenceEquals(a,b)||a.HalfExtent==b.HalfExtent&&a.Obstacles.Count==b.Obstacles.Count&&a.Obstacles.Select((o,i)=>o.GeometryEquals(b.Obstacles[i])).All(x=>x);
        private static bool SameProfile(NavigationProfile a,NavigationProfile b){
            if(ReferenceEquals(a,b))return true;
            if(a.ProfileId!=b.ProfileId||a.Revision!=b.Revision||a.WireValueCount!=b.WireValueCount)return false;
            for(int i=0;i<a.WireValueCount;i++)if(BitConverter.DoubleToInt64Bits(a.WireValueAt(i))!=BitConverter.DoubleToInt64Bits(b.WireValueAt(i)))return false;return true;
        }
        internal bool TryGet(ObservedPathWork work,int actor,int target,PlayableRouteTargetKind kind,int candidate,out NavPoint[] path,Action before=null)
        {
            Job job;path=null;
            lock(gate){
                if(failure!=null)throw new InvalidOperationException("AI observed-path worker failed.",failure);
                if(stopping)throw new OperationCanceledException("AI observed-path worker stopped.");
                var key=(work.Owner,actor,target,kind,candidate);
                lookup.TryGetValue(key,out job);
                if(job!=null&&!Same(job,work,actor,target,kind,candidate)){job.Cancelled=true;jobs.Remove(job);lookup.Remove(key);job=null;}
                if(job==null){
                    if(jobs.Count==WorldWire.MaxItems)return false;
                    Start();epochs.TryGetValue(work.Owner,out var epoch);job=new Job{Work=work,Actor=actor,Target=target,Kind=kind,Candidate=candidate,Used=++use,Epoch=epoch,Before=before};jobs.Add(job);lookup.Add(key,job);FillQueue();return false;
                }
                job.Used=++use;epochs.TryGetValue(work.Owner,out job.Epoch);if(!job.Done)return false;
            }
            if(TryReadPath(job,work,out path))return true;
            // An old empty/unsafe answer cannot prove a changed request unreachable.
            lock(gate){job.Cancelled=true;jobs.Remove(job);lookup.Remove(Key(job));}
            return TryGet(work,actor,target,kind,candidate,out path,before);
        }
        // Authority delivery only reads settled work. It cannot enqueue, prune,
        // touch epochs/use clocks or execute the observed solver on the world thread.
        internal bool TryReadReady(ObservedPathWork work,int actor,int target,PlayableRouteTargetKind kind,int candidate,out NavPoint[] path)
        {
            path=null;Job job;
            lock(gate){
                if(failure!=null)throw new InvalidOperationException("AI observed-path worker failed.",failure);
                if(stopping)throw new OperationCanceledException("AI observed-path worker stopped.");
                if(!lookup.TryGetValue((work.Owner,actor,target,kind,candidate),out job)||!job.Done||
                    !Same(job,work,actor,target,kind,candidate))return false;
            }
            return TryReadPath(job,work,out path);
        }
        private static bool TryReadPath(Job job,ObservedPathWork work,out NavPoint[] path)
        {
            path=null;
            bool exact=job.Work.Origin.Equals(work.Origin)&&job.Work.Goal.Equals(work.Goal);
            if(exact){path=job.Path;return true;}
            // Rebind a completed nonempty geometric witness to current endpoints.
            // Every new/existing segment still passes current observed geometry;
            // the authority's existing proof checks additionally enforce live solids.
            if(job.Path.Length>0){
                var points=job.Path.Last().Equals(work.Goal)?job.Path:job.Path.Concat(new[]{work.Goal}).ToArray();
                var from=work.Origin;bool safe=true;
                foreach(var point in points){if(!work.Geometry.SegmentFree(from,point,work.Radius)){safe=false;break;}from=point;}
                if(safe){path=points;return true;}
            }
            return false;
        }
        internal void DrainForCapture(Func<bool> cancelled)
        {
            lock(gate){while(true){
                if(cancelled?.Invoke()==true||stopping)throw new OperationCanceledException("Observed capture stopped.");
                if(failure!=null)throw new InvalidOperationException("Observed capture worker failed.",failure);
                if(jobs.All(j=>j.Done))return;
                Monitor.Wait(gate,25);
            }}
        }
        internal void Stop(){lock(gate){stopping=true;queue.Clear();if(!started)Volatile.Write(ref stopped,1);Monitor.PulseAll(gate);}}
        private void Run()
        {
            var pool=new ObservedPathWorkPool();
            try{while(true){
                Job job;lock(gate){while(!stopping&&queue.Count==0)Monitor.Wait(gate);if(stopping)return;job=queue.Dequeue();job.Enqueued=false;if(job.Cancelled){FillQueue();continue;}job.Running=true;running++;}
                job.Before?.Invoke();var result=job.Work.Execute(pool);
                lock(gate){job.Path=result;job.Done=true;job.Before=null;job.Running=false;running--;FillQueue();Monitor.PulseAll(gate);}
            }}catch(Exception ex){lock(gate){failure=ex;Monitor.PulseAll(gate);}}
            finally{lock(gate){queue.Clear();jobs.Clear();lookup.Clear();running=0;}Volatile.Write(ref stopped,1);}
        }
    }
}
