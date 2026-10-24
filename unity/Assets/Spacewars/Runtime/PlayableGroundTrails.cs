using System.Collections.Generic;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        readonly GroundTrailJournal groundTrails=new GroundTrailJournal();
        readonly HashSet<int> groundTrailAlive=new HashSet<int>();
        void ObserveGroundTrails()
        {
            groundTrails.Expire(Tick,profile.GroundTrailLifetimeTicks);groundTrailAlive.Clear();
            foreach(var u in units.Values)if(navigation.Crowd.TryGet(u.Id,out var n))
            {groundTrailAlive.Add(u.Id);groundTrails.Observe(u.Id,u.Kind,u.PreviousPosition,n.Position,n.Heading,Tick);}
            groundTrails.RemoveMissing(groundTrailAlive);
        }
    }
}
