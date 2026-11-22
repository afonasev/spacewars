using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

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
        private int RelayRecipients(PlayableOwner owner)
        {
            if(offline==null||offline.CampaignOpening||DominationEnabled||eliminated.Contains(owner))return 0;
            int recipients=0;
            for(int i=0;i<offline.Roster.Count;i++)
            {
                var candidate=(PlayableOwner)i;
                if(candidate!=owner&&offline.Roster[i].Control!=OfflineControl.Scenario&&
                    !Hostile(owner,candidate)&&!eliminated.Contains(candidate))
                    recipients|=1<<i;
            }
            return recipients;
        }

        private (NavPoint Center, double Radius)? RelayArrivalArea(PlayableCommand command)
        {
            if (command.Origin != PlayableOrderOrigin.Ai || !sites.TryGetValue(command.SiteId, out var site) ||
                site.Site.Kind != PlayableBuildingKind.RelayTower ||
                Distance(command.Target, site.Site.Position) > profile.RelayCaptureRadius)
                return null;
            // A body may stop short of the assigned point within the arrival tolerance.
            return (site.Site.Position, Math.Max(0, profile.RelayCaptureRadius - profile.Navigation.ArrivalTolerance));
        }

        private bool InRelayZone(Unit unit,TerritorySite site)=>
            !eliminated.Contains(unit.Owner)&&TryActor(unit.Id,out var actor)&&
            Distance(actor.Position,site.Position)<=profile.RelayCaptureRadius;

        private PlayableCommandStatus TransferRelay(PlayableOwner owner,int siteId,int recipientIndex,out string message)
        {
            message="Передача вышки недоступна";
            if(!sites.TryGetValue(siteId,out var state)||state.Site.Kind!=PlayableBuildingKind.RelayTower||
                recipientIndex<0||recipientIndex>=(offline?.Roster.Count??0)||
                (RelayRecipients(owner)&(1<<recipientIndex))==0)
                return PlayableCommandStatus.InvalidTarget;
            if(state.Locked!=owner||state.Progress<1||
                units.Values.Any(unit=>Hostile(unit.Owner,owner)&&InRelayZone(unit,state.Site)))
                return PlayableCommandStatus.Rejected;

            var recipient=(PlayableOwner)recipientIndex;
            state.Locked=state.Claimant=recipient;
            incomeEvents.RemoveAll(e=>e.Kind==PlayableBuildingKind.RelayTower&&e.BuildingId==siteId);
            string name="Вышка связи №"+siteId;
            Fact(owner,MatchFactKind.RelayTransferredOut,0,(int)PlayableBuildingKind.RelayTower,name);
            Fact(recipient,MatchFactKind.RelayTransferredIn,0,(int)PlayableBuildingKind.RelayTower,name);
            message=null;
            return PlayableCommandStatus.Applied;
        }

        internal void ShareAiRelays(PlayableAiOwnerLoop donor,IReadOnlyList<PlayableAiOwnerLoop> bots)
        {
            if(Outcome!=PlayableMatchOutcome.Playing||offline==null||!TryOwner(donor.OwnerId,out var owner))return;
            var calculate=CaptureRelaySharing(donor,bots);
            if(calculate!=null)ApplyRelaySharing(owner,calculate());
        }

        internal System.Func<(int Site,int Recipient)[]> CaptureRelaySharing(PlayableAiOwnerLoop donor,IReadOnlyList<PlayableAiOwnerLoop> bots)
        {
            if(Outcome!=PlayableMatchOutcome.Playing||offline==null||!TryOwner(donor.OwnerId,out var owner))return null;
            int eligible=RelayRecipients(owner);
            if(eligible==0)return null;
            var owned=sites.Values.Where(s=>s.Site.Kind==PlayableBuildingKind.RelayTower&&s.Locked==owner&&
                s.Progress>=1&&!s.Contested).Select(s=>s.Site.Id).ToArray();
            if(owned.Length==0)return null;
            var recipients=Owners.Where(candidate=>(eligible&(1<<(int)candidate))!=0 &&
                    (offline.Roster[(int)candidate].Control==OfflineControl.Human||
                     bots.Any(bot=>bot.OwnerId==OwnerName(candidate)&&AiProfile.BehaviorDifficulty(bot.Difficulty)>=AiProfile.BehaviorDifficulty(donor.Difficulty))))
                .Select(candidate=>(Owner:candidate,Income:Income(candidate),Capacity:Population(candidate).Capacity,
                    Gain:profile.RelayIncomePerPeriod*IncomeMultiplier(candidate))).ToArray();
            double income=Income(owner),loss=profile.RelayIncomePerPeriod*IncomeMultiplier(owner);
            double gap=profile.RelayTransferIncomeGap*profile.IncomePeriodSeconds;
            return ()=>{
                var transfers=new List<(int,int)>();
                foreach(var siteId in owned.OrderBy(id=>id)){
                    int best=-1;
                    for(int i=0;i<recipients.Length;i++){
                        var candidate=recipients[i];
                        if(income-candidate.Income<=gap)continue;
                        if(best<0||candidate.Income<recipients[best].Income||
                            candidate.Income==recipients[best].Income&&(candidate.Capacity<recipients[best].Capacity||
                            candidate.Capacity==recipients[best].Capacity&&(int)candidate.Owner<(int)recipients[best].Owner))best=i;
                    }
                    if(best<0)break;
                    transfers.Add((siteId,(int)recipients[best].Owner));
                    income-=loss;
                    recipients[best].Income+=recipients[best].Gain;
                }
                return transfers.ToArray();
            };
        }
        internal void ApplyRelaySharing(PlayableOwner owner,(int Site,int Recipient)[] transfers)
        {
            foreach(var transfer in transfers){
                if(Income(owner)-Income((PlayableOwner)transfer.Recipient)<=profile.RelayTransferIncomeGap*profile.IncomePeriodSeconds)continue;
                TransferRelay(owner,transfer.Site,transfer.Recipient,out _);
            }
        }

        private void AdvanceRelayCapture(SiteState state,double dt)
        {
            if(DominationEnabled)
            {
                AdvanceTeamRelayCapture(state,dt);
                return;
            }
            if(state.Locked.HasValue)state.ControlledSeconds+=dt;
            var present=new HashSet<PlayableOwner>();
            foreach(var u in units.Values)
                if(u.Kind!=PlayableEntityKind.Skat&&!Materializing(u)&&!Frozen(u)&&InRelayZone(u,state.Site))
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
