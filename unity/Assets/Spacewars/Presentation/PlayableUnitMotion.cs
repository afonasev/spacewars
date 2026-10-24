using System;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    public readonly struct PlayableDisplayPose
    {
        public PlayableDisplayPose(NavPoint position,double hull,double turret,double altitude=0,double pitch=0,double roll=0){Position=position;Hull=hull;Turret=turret;Altitude=altitude;Pitch=pitch;Roll=roll;}
        public double Altitude{get;} public double Pitch{get;} public double Roll{get;} public NavPoint Position{get;} public double Hull{get;} public double Turret{get;}
    }
    // Derived presentation only. Never modifies or extrapolates an authority view.
    public sealed class PlayableUnitMotion
    {
        private const double TickSeconds=1d/30d;
        private sealed class Track
        {
            internal PlayableDisplayPose From,To;
            internal double Started;
            internal PlayableOwner Owner;internal PlayableEntityKind Kind;internal string Surface;
        }
        private readonly Dictionary<int,Track> tracks=new Dictionary<int,Track>();
        private readonly List<int> removed=new List<int>();
        private long generation=-1,tick=-1;private int revision=-1,geometry=-1,team=-1;
        private string profile,ownerId;private PlayableOwner owner;private bool snap;
        private double interval=TickSeconds,age;
        private readonly Dictionary<int,PlayableEntitySnapshot> preceding=new Dictionary<int,PlayableEntitySnapshot>();
        public void BeginFrame(PlayableSnapshot view,bool forceSnap=false,double speed=1,PlayableSnapshot previous=null,double snapshotAge=0)
        {
            if(double.IsNaN(speed)||double.IsInfinity(speed)||speed<=0)throw new ArgumentOutOfRangeException(nameof(speed));
            interval=TickSeconds/speed;age=double.IsNaN(snapshotAge)||double.IsInfinity(snapshotAge)?0:Math.Max(0,snapshotAge);preceding.Clear();
            bool sameContext=generation==view.Generation&&profile==view.ProfileId&&revision==view.ProfileRevision&&
                owner==view.Owner&&ownerId==view.OwnerId&&team==view.Team&&geometry==(view.Geometry?.Revision??-1);
            // Accelerated rendering can skip publications. Use the last actual authority
            // segment, never a straight shortcut between the two rendered snapshots.
            if(sameContext&&view.Tick>tick+1&&previous!=null&&previous.Tick==view.Tick-1&&
                previous.Generation==view.Generation&&previous.ProfileId==view.ProfileId&&previous.ProfileRevision==view.ProfileRevision&&
                previous.Owner==view.Owner&&previous.OwnerId==view.OwnerId&&previous.Team==view.Team&&
                (previous.Geometry?.Revision??-1)==(view.Geometry?.Revision??-1)&&!previous.Paused&&previous.Status==RuntimeStatus.Running)
                foreach(var entity in previous.Entities)preceding[entity.Id]=entity;
            if(generation!=view.Generation||profile!=view.ProfileId||revision!=view.ProfileRevision||
                owner!=view.Owner||ownerId!=view.OwnerId||team!=view.Team||geometry!=(view.Geometry?.Revision??-1)||
                view.Tick<tick||view.Tick>tick+1)tracks.Clear();
            generation=view.Generation;tick=view.Tick;profile=view.ProfileId;revision=view.ProfileRevision;
            owner=view.Owner;ownerId=view.OwnerId;team=view.Team;geometry=view.Geometry?.Revision??-1;
            snap=forceSnap||view.Paused||view.Status!=RuntimeStatus.Running||view.Outcome!=PlayableMatchOutcome.Playing;
        }
        public PlayableDisplayPose Sample(PlayableEntitySnapshot entity,double now)
        {
            var target=new PlayableDisplayPose(entity.Position,entity.HullHeading,entity.TurretHeading,entity.Altitude,entity.Pitch,entity.Roll);
            string surface=entity.Location?.SurfaceId;
            if(!tracks.TryGetValue(entity.Id,out var track)){
                var start=target;
                if(preceding.TryGetValue(entity.Id,out var prior)&&prior.Owner==entity.Owner&&prior.Kind==entity.Kind&&
                    prior.Location?.SurfaceId==surface&&((!prior.Held&&!entity.Held)||entity.Kind==PlayableEntityKind.Hawk))
                    start=new PlayableDisplayPose(prior.Position,prior.HullHeading,prior.TurretHeading,prior.Altitude,prior.Pitch,prior.Roll);
                track=new Track{From=start,To=start,Started=now,Owner=entity.Owner,Kind=entity.Kind,Surface=surface};
                tracks.Add(entity.Id,track);
            }
            if(snap||(entity.Held&&entity.Kind!=PlayableEntityKind.Hawk)||track.Owner!=entity.Owner||track.Kind!=entity.Kind||track.Surface!=surface){
                track.From=track.To=target;track.Started=now;track.Owner=entity.Owner;track.Kind=entity.Kind;track.Surface=surface;
            }else if(!track.To.Position.Equals(target.Position)||track.To.Hull!=target.Hull||track.To.Turret!=target.Turret||track.To.Altitude!=target.Altitude||track.To.Pitch!=target.Pitch||track.To.Roll!=target.Roll){
                // Use consecutive authority endpoints, not a diagonal shortcut from
                // an unfinished display pose when the route turns a corner.
                track.From=track.To;track.To=target;track.Started=now-age;
            }
            return At(track,now,interval);
        }
        public void KeepVisible(HashSet<int> alive)
        {
            removed.Clear();foreach(var row in tracks)if(!alive.Contains(row.Key))removed.Add(row.Key);
            foreach(int id in removed)tracks.Remove(id);
        }
        private static PlayableDisplayPose At(Track track,double now,double duration)
        {
            double alpha=Math.Max(0,Math.Min(1,(now-track.Started)/duration));
            if(alpha>=1)return track.To;
            var a=track.From;var b=track.To;
            return new PlayableDisplayPose(new NavPoint(a.Position.X+(b.Position.X-a.Position.X)*alpha,
                a.Position.Z+(b.Position.Z-a.Position.Z)*alpha),Angle(a.Hull,b.Hull,alpha),Angle(a.Turret,b.Turret,alpha),a.Altitude+(b.Altitude-a.Altitude)*alpha,a.Pitch+(b.Pitch-a.Pitch)*alpha,a.Roll+(b.Roll-a.Roll)*alpha);
        }
        private static double Angle(double a,double b,double alpha)
        {
            double delta=b-a;while(delta>Math.PI)delta-=2*Math.PI;while(delta< -Math.PI)delta+=2*Math.PI;
            return a+delta*alpha;
        }
    }
}
