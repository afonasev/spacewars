using System;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    public readonly struct PlayableDisplayPose
    {
        public PlayableDisplayPose(NavPoint position,double hull,double turret){Position=position;Hull=hull;Turret=turret;}
        public NavPoint Position{get;} public double Hull{get;} public double Turret{get;}
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
        public void BeginFrame(PlayableSnapshot view,bool forceSnap=false)
        {
            if(generation!=view.Generation||profile!=view.ProfileId||revision!=view.ProfileRevision||
                owner!=view.Owner||ownerId!=view.OwnerId||team!=view.Team||geometry!=(view.Geometry?.Revision??-1)||
                view.Tick<tick||view.Tick>tick+1)tracks.Clear();
            generation=view.Generation;tick=view.Tick;profile=view.ProfileId;revision=view.ProfileRevision;
            owner=view.Owner;ownerId=view.OwnerId;team=view.Team;geometry=view.Geometry?.Revision??-1;
            snap=forceSnap||view.Paused||view.Status!=RuntimeStatus.Running||view.Outcome!=PlayableMatchOutcome.Playing;
        }
        public PlayableDisplayPose Sample(PlayableEntitySnapshot entity,double now)
        {
            var target=new PlayableDisplayPose(entity.Position,entity.HullHeading,entity.TurretHeading);
            string surface=entity.Location?.SurfaceId;
            if(!tracks.TryGetValue(entity.Id,out var track)){
                track=new Track{From=target,To=target,Started=now,Owner=entity.Owner,Kind=entity.Kind,Surface=surface};
                tracks.Add(entity.Id,track);
            }
            if(snap||entity.Held||track.Owner!=entity.Owner||track.Kind!=entity.Kind||track.Surface!=surface){
                track.From=track.To=target;track.Started=now;track.Owner=entity.Owner;track.Kind=entity.Kind;track.Surface=surface;
            }else if(!track.To.Position.Equals(target.Position)||track.To.Hull!=target.Hull||track.To.Turret!=target.Turret){
                // Use consecutive authority endpoints, not a diagonal shortcut from
                // an unfinished display pose when the route turns a corner.
                track.From=track.To;track.To=target;track.Started=now;
            }
            return At(track,now);
        }
        public void KeepVisible(HashSet<int> alive)
        {
            removed.Clear();foreach(var row in tracks)if(!alive.Contains(row.Key))removed.Add(row.Key);
            foreach(int id in removed)tracks.Remove(id);
        }
        private static PlayableDisplayPose At(Track track,double now)
        {
            double alpha=Math.Max(0,Math.Min(1,(now-track.Started)/TickSeconds));
            if(alpha>=1)return track.To;
            var a=track.From;var b=track.To;
            return new PlayableDisplayPose(new NavPoint(a.Position.X+(b.Position.X-a.Position.X)*alpha,
                a.Position.Z+(b.Position.Z-a.Position.Z)*alpha),Angle(a.Hull,b.Hull,alpha),Angle(a.Turret,b.Turret,alpha));
        }
        private static double Angle(double a,double b,double alpha)
        {
            double delta=b-a;while(delta>Math.PI)delta-=2*Math.PI;while(delta< -Math.PI)delta+=2*Math.PI;
            return a+delta*alpha;
        }
    }
}
