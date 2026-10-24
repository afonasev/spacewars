using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    // Anonymous ground impressions: no owner, entity ID, destination or hidden route.
    public sealed class PlayableGroundTrail
    {
        public const int DefaultLifetimeTicks=300; // Default profile: ten seconds at authoritative 30 Hz.
        public NavPoint From{get;} public NavPoint To{get;}
        public double Heading{get;} public double Distance{get;} public long Tick{get;}
        public PlayableEntityKind Kind{get;}
        public PlayableGroundTrail(NavPoint from,NavPoint to,double heading,double distance,long tick,PlayableEntityKind kind)
        {From=from;To=to;Heading=heading;Distance=distance;Tick=tick;Kind=kind;}
    }
    public sealed class GroundTrailJournal
    {
        // Defensive storage ceiling, not a gameplay/art control. Supports >2600 moving
        // vehicles at ten samples/s for the full lifetime; normal roster bounds are lower.
        public const int Capacity=262144;
        readonly Queue<PlayableGroundTrail> samples=new Queue<PlayableGroundTrail>();
        sealed class Cursor{public NavPoint Position;public double Distance;public long Tick;public bool WasMoving;}
        readonly Dictionary<int,Cursor> cursors=new Dictionary<int,Cursor>();
        public int Count=>samples.Count;
        public void Expire(long tick,int lifetimeTicks=PlayableGroundTrail.DefaultLifetimeTicks){if(lifetimeTicks<1)throw new ArgumentOutOfRangeException(nameof(lifetimeTicks));while(samples.Count>0&&tick-samples.Peek().Tick>=lifetimeTicks)samples.Dequeue();}
        public void Observe(int id,PlayableEntityKind kind,NavPoint from,NavPoint to,double heading,long tick)
        {
            if(!cursors.TryGetValue(id,out var c))cursors[id]=c=new Cursor{Position=from,Tick=tick-1};
            double dx=to.X-c.Position.X,dz=to.Z-c.Position.Z,length=Math.Sqrt(dx*dx+dz*dz);
            // First displacement is immediate; steady motion is sampled at 10 Hz.
            // A final stopped sample closes any remaining distance.
            bool stopped=from.Equals(to),starting=!stopped&&!c.WasMoving;
            c.WasMoving=!stopped;
            if(!starting&&tick-c.Tick<3&&!stopped)return;
            if(length>1e-7&&length<10){samples.Enqueue(new PlayableGroundTrail(c.Position,to,heading,c.Distance,tick,kind));c.Distance+=length;}
            c.Position=to;c.Tick=tick;
            while(samples.Count>Capacity)samples.Dequeue();
        }
        public void RemoveMissing(ISet<int> alive){foreach(var id in cursors.Keys.Where(id=>!alive.Contains(id)).ToArray())cursors.Remove(id);}
        public PlayableGroundTrail[] Visible(long tick,TeamVisionSnapshot vision=null,int lifetimeTicks=PlayableGroundTrail.DefaultLifetimeTicks)
        {
            Expire(tick,lifetimeTicks);
            // Endpoints + midpoint prevent disclosing an invisible segment. Pixel fog also
            // clips contact-strip edges in the renderer, separately for every local view.
            return samples.Where(s=>vision==null||vision.IsVisible(s.From)&&vision.IsVisible(s.To)&&vision.IsVisible(new NavPoint((s.From.X+s.To.X)*.5,(s.From.Z+s.To.Z)*.5))).ToArray();
        }
    }
}
