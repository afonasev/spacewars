using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private IEnumerable<NavObstacle> RelayObstacles()
        {
            foreach(var s in offline==null?TerritoryRules.Sites(profile):offline.Sites)
                if(s.Kind==PlayableBuildingKind.RelayTower)
                {
                    double r=profile.RelayFootprintRadius;
                    yield return new NavObstacle(s.Position.X-r,s.Position.Z-r,s.Position.X+r,s.Position.Z+r);
                }
        }
        private void AdvanceRelayCapture(SiteState state,double dt)
        {
            if(state.Locked.HasValue)state.ControlledSeconds+=dt;
            var present=new HashSet<PlayableOwner>();
            foreach(var u in units.Values)
                if(!eliminated.Contains(u.Owner)&&TryActor(u.Id,out var n)&&Distance(n.Position,state.Site.Position)<=profile.RelayCaptureRadius)
                    present.Add(u.Owner);
            state.Contested=present.Select(TeamOf).Distinct().Count()>1;
            if(state.Contested)return;
            double delta=dt/profile.RelayCaptureSeconds;
            if(state.Locked.HasValue)
            {
                var owner=state.Locked.Value;
                bool hostile=present.Any(o=>TeamOf(o)!=TeamOf(owner));
                state.Progress=hostile?System.Math.Max(0,state.Progress-delta):System.Math.Min(1,state.Progress+delta);
                if(state.Progress<=1e-9)
                {
                    MatchEvent(MatchEventKind.RelayControlLost,owner,state.Site.Position);
                    Fact(owner,MatchFactKind.RelayNeutralized,0,(int)PlayableBuildingKind.RelayTower,"Вышка связи №"+state.Site.Id);
                    AddLingeringVision(owner,state.Site.Position,profile.RelayVisionRange);
                    state.Progress=0;state.Locked=null;state.Claimant=null;
                }
                return;
            }
            // Allies never replace a partial friendly claim. Incomplete claims
            // decay if the actual claimant leaves; complete control is latched.
            if(state.Claimant.HasValue&&present.Contains(state.Claimant.Value))
            {
                state.Progress=System.Math.Min(1,state.Progress+delta);
                if(state.Progress>=1)
                {
                    state.Locked=state.Claimant;
                    Fact(state.Locked.Value,MatchFactKind.RelayCaptured,0,(int)PlayableBuildingKind.RelayTower,"Вышка связи №"+state.Site.Id);
                }
                return;
            }
            if(state.Progress>0)
            {
                state.Progress=System.Math.Max(0,state.Progress-delta);
                if(state.Progress<=1e-9){state.Progress=0;state.Claimant=null;}
                return;
            }
            var claimant=Owners.Where(present.Contains).Select(o=>(PlayableOwner?)o).FirstOrDefault();
            if(claimant.HasValue){state.Claimant=claimant;state.Progress=System.Math.Min(1,delta);}
        }
    }
}
