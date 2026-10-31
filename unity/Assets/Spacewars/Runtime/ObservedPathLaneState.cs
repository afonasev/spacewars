using System;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using static Spacewars.Runtime.WorldWire;

namespace Spacewars.Runtime
{
    internal sealed partial class ObservedPathLane
    {
        internal void WriteState(BinaryWriter w)
        {
            lock(gate){
                if(stopping||failure!=null||jobs.Any(j=>!j.Done))throw new InvalidOperationException("Observed capture requires a settled worker.");
                var geometries=jobs.Select(j=>j.Work.Geometry).Distinct().ToArray();
                WorldWire.Array(w,geometries,g=>WorldWire.Write(w,new NavigationGeometryState{HalfExtent=g.HalfExtent,Revision=g.Revision,Obstacles=g.Obstacles.ToArray()}));
                w.Write(use);w.Write(jobs.Count);
                foreach(var j in jobs){var q=j.Work;
                    w.Write((int)q.Owner);w.Write(j.Actor);w.Write(j.Target);w.Write((int)j.Kind);w.Write(j.Candidate);w.Write(j.Used);
                    w.Write(System.Array.IndexOf(geometries,q.Geometry));String(w,q.Profile.ProfileId);w.Write(q.Profile.Revision);WorldWire.Array(w,q.Profile.CaptureWireValues(),v=>Number(w,v));
                    Number(w,q.Radius);Number(w,q.Speed);Number(w,q.Turn);WorldWire.Write(w,q.Origin);WorldWire.Write(w,q.Goal);WorldWire.Array(w,j.Path,v=>WorldWire.Write(w,v));
                }
            }
        }
        internal static ObservedPathLane ReadState(BinaryReader r)
        {
            var geometries=WorldWire.Array(r,()=>{var g=ReadNavigationGeometryState(r);if(g==null)throw new ArgumentException("Missing observed geometry.");return new NavGeometry(g.HalfExtent,g.Obstacles,g.Revision);});
            if(geometries.Length>WorldWire.MaxItems)throw new ArgumentException("Observed geometry capacity.");
            long use=r.ReadInt64();int count=r.ReadInt32();if(use<0||count<0||count>WorldWire.MaxItems)throw new ArgumentException("Observed query capacity/clock.");
            var entries=new Job[count];
            for(int i=0;i<count;i++){
                var owner=(PlayableOwner)r.ReadInt32();int actor=r.ReadInt32(),target=r.ReadInt32();var kind=(PlayableRouteTargetKind)r.ReadInt32();int candidate=r.ReadInt32();long used=r.ReadInt64();int geometry=r.ReadInt32();
                string profileId=String(r);int revision=r.ReadInt32();var profile=NavigationProfile.FromWireValues(profileId,revision,WorldWire.Array(r,()=>Number(r)));
                double radius=Number(r),speed=Number(r),turn=Number(r);var origin=ReadPoint(r);var goal=ReadPoint(r);var path=WorldWire.Array(r,()=>ReadPoint(r));
                if(!Enum.IsDefined(typeof(PlayableOwner),owner)||!Enum.IsDefined(typeof(PlayableRouteTargetKind),kind)||actor<=0||target<=0||candidate<0||used<0||used>use||geometry<0||geometry>=geometries.Length)throw new ArgumentException("Observed query binding.");
                profile.ForUnit(radius,speed,turn); // Same mobility validation as execution.
                var work=new ObservedPathWork(owner,geometries[geometry],profile,radius,speed,turn,origin,goal);
                var from=origin;foreach(var point in path){if(!work.Geometry.SegmentFree(from,point,radius))throw new ArgumentException("Unsafe observed saved path.");from=point;}
                if(path.Length>0&&!path[path.Length-1].Equals(goal))throw new ArgumentException("Observed saved goal mismatch.");
                if(entries.Take(i).Any(j=>j.Work.Owner==owner&&j.Actor==actor&&j.Target==target&&j.Kind==kind&&j.Candidate==candidate))throw new ArgumentException("Duplicate observed query.");
                entries[i]=new Job{Work=work,Actor=actor,Target=target,Kind=kind,Candidate=candidate,Used=used,Path=path,Done=true};
            }
            var lane=new ObservedPathLane(false);lock(lane.gate){lane.use=use;lane.jobs.AddRange(entries);foreach(var job in entries)lane.lookup.Add(Key(job),job);}return lane;
        }
    }
}
