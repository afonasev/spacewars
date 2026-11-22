using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Detached relay policy: public geography and owner-visible facts only. Mission
    // identity/lifetime lives in the existing serialized scout/army registry.
    internal static class AiRelayObjectives
    {
#if UNITY_INCLUDE_TESTS
        internal static Action<object> AssaultReadbackForTests;
#endif
        internal const string DominationPolicy="domination";
        internal static bool Urgent(PlayableAiObservation o)=>o.Domination is PlayableDominationSnapshot state&&state.WarningActive;
        internal static int Team(PlayableAiObservation o,int site)
        {
            var owner=o.MapRelayControl.FirstOrDefault(item=>item.SiteId==site)?.Owner;
            return owner.HasValue?o.Participants[(int)owner.Value].Team:0;
        }
        internal static double DominationPreference(PlayableAiObservation o,PlayableProfile p,int site)
        {
            if(o.Domination is not PlayableDominationSnapshot state||state.ScoringTeam==0||Team(o,site)!=state.ScoringTeam)return 0;
            return Math.Clamp(state.Score/p.DominationTarget,0,1);
        }

        internal static int[] RelayCounts(IEnumerable<int> relayTeams)
        {
            var counts=new int[9];
            foreach(int team in relayTeams)if(team!=0)counts[team]++;
            return counts;
        }

        private static int Margin(int[] counts,int leader,int from=0,int to=0)
        {
            int Count(int team)=>counts[team]-(team==from?1:0)+(team==to?1:0);
            return Count(leader)-Enumerable.Range(1,8).Where(team=>team!=leader).Max(Count);
        }

        internal static int AttackImpact(int[] counts,int leader,int team,int owner)=>
            Margin(counts,leader)-Math.Min(Margin(counts,leader,owner,0),Margin(counts,leader,owner,team));

        private static (double seconds,bool breaks) CaptureTime(PlayableAiObservation o,PlayableProfile p,int[] counts,
            int leader,int site,int owner)
        {
            var visible=o.Sites.FirstOrDefault(s=>s.Site.Id==site);
            double progress=visible?.Progress??(owner==0?0:1);
            double neutralize=owner==0?0:progress*p.RelayCaptureSeconds;
            if(owner!=0&&Margin(counts,leader,owner,0)<=0)return (neutralize,true);
            double capture=1;
            if(owner==0&&visible?.Claimant is PlayableOwner claimant)
                capture+=o.Participants[(int)claimant].Team==o.Team?-progress:progress;
            return (neutralize+capture*p.RelayCaptureSeconds,Margin(counts,leader,owner,o.Team)<=0);
        }

        internal static bool QueuedApproach(PlayableQueuedOrderSnapshot order,int site,NavPoint? goal=null)=>
            order!=null&&order.AiActionId>0&&order.Kind==PlayableCommandKind.AttackMove&&
            order.PublicObjectiveId==site&&order.Anchor.HasValue&&(!goal.HasValue||order.Anchor.Value.Position.Equals(goal.Value));

        internal static bool Advancing(PlayableEntitySnapshot unit,int site,Func<int,NavPoint,bool> rejected=null,NavPoint? goal=null)
        {
            var queue=unit.Queue;
            var order=queue?.Pending??queue?.Active;
            if(!QueuedApproach(order,site,goal)||rejected?.Invoke(site,order.Anchor.Value.Position)==true)
                return false;
            return queue.Pending!=null?queue.PendingAiRouteViable:
                unit.Moving||unit.NavigationOutcome is NavigationOutcome.Moving or NavigationOutcome.Waiting;
        }

        // Neutralizing a hostile relay stops its domination clock before ownership
        // transfers. Keep one present fighter through the remaining capture ticks.
        internal static IEnumerable<int> CaptureGuards(PlayableAiObservation o,PlayableProfile p)
        {
            foreach(var relay in o.Sites.Where(s=>!s.Owner.HasValue&&s.Claimant==o.Owner&&
                s.Site.Kind==PlayableBuildingKind.RelayTower))
            {
                var guard=o.Entities.Where(u=>Available(o,u)&&!u.Moving&&
                    Distance(u.Position,relay.Site.Position)<=p.RelayCaptureRadius)
                    .OrderBy(u=>u.Id).FirstOrDefault();
                if(guard!=null)yield return guard.Id;
            }
        }

        internal static HashSet<int> CommittedRecipients(PlayableAiObservation o,AiDecisionArbiter arbiter)
        {
            var members=new HashSet<int>();
            if(o.Domination is not PlayableDominationSnapshot state||!state.Rising||arbiter.RelayAssault(o.Tick).Site==0)
                return members;
            bool defending=state.LeadingTeam==o.Team;
            if(defending&&!state.WarningActive)return members;
            var counts=RelayCounts(o.MapRelayControl.Where(r=>r.Owner.HasValue).Select(r=>o.Participants[(int)r.Owner.Value].Team));
            var sites=o.PublicScoutObjectives.Where(site=>site.Role==PlayablePublicScoutObjectiveRole.Relay&&site.Reachable)
                .Where(site=>{
                    int owner=Team(o,site.SiteId);
                    return defending?owner==o.Team:owner!=o.Team&&AttackImpact(counts,state.LeadingTeam,o.Team,owner)>0;
                }).Select(site=>site.SiteId).ToHashSet();
            var rejected=arbiter.RejectedDominationApproaches(o.Tick);
            foreach(var unit in o.Entities.Where(u=>Available(o,u)))
            {
                int site=(unit.Queue?.Pending??unit.Queue?.Active)?.PublicObjectiveId??0;
                if(sites.Contains(site)&&Advancing(unit,site,rejected))members.Add(unit.Id);
            }
            return members;
        }

        internal static PlayableAiAction PlanDomination(PlayableAiObservation o,PlayableProfile p,AiArmyRegistry registry,
            Func<int,NavPoint,bool> rejected=null,AiProfile ai=null,AiDecisionArbiter arbiter=null,AiTeamAssignment assignment=null)
        {
            ai??=AiProfile.Initial;
            if(o.Domination is not PlayableDominationSnapshot state||!state.Rising)return null;
            bool defending=state.LeadingTeam==o.Team;
            if(defending&&!state.WarningActive)return null;
            var assault=arbiter?.RelayAssault(o.Tick);
            bool stalled=assault?.Observe(o,p,ai,arbiter)==true;
#if UNITY_INCLUDE_TESTS
            if(stalled)AssaultReadbackForTests?.Invoke(new{Type="relay-retry",Owner=o.OwnerId,o.Tick,assault.Site,assault.Goal});
#endif
            if(arbiter!=null)rejected=arbiter.RejectedDominationApproaches(o.Tick);
            var units=o.Entities.Where(unit=>Available(o,unit)).OrderBy(unit=>unit.Id).ToArray();
            if(defending&&AiProfile.BehaviorDifficulty(o.Difficulty)>=AiDifficulty.Veteran&&units.Length>=8)
            {
                var raids=registry.Capture().Armies.Where(army=>army.TacticalOwner==AiArmyPlanner.FlankPolicy)
                    .SelectMany(army=>army.Members).ToHashSet();
                var reserve=units.Where(unit=>raids.Contains(unit.Id)).Take(units.Length/8).Select(unit=>unit.Id).ToHashSet();
                units=units.Where(unit=>!reserve.Contains(unit.Id)).ToArray();
            }
            int leader=state.LeadingTeam;
            var counts=RelayCounts(o.MapRelayControl.Where(r=>r.Owner.HasValue).Select(r=>o.Participants[(int)r.Owner.Value].Team));
            var options=(from site in o.PublicScoutObjectives
                where site.Role==PlayablePublicScoutObjectiveRole.Relay&&site.Reachable
                let owner=Team(o,site.SiteId)
                let impact=defending?(owner==leader?1:0):(owner==o.Team?0:AttackImpact(counts,leader,o.Team,owner))
                where impact>0
                let center=o.PublicRelaySites.FirstOrDefault(r=>r.Id==site.SiteId)?.Position??
                    o.Sites.FirstOrDefault(r=>r.Site.Id==site.SiteId)?.Site.Position??site.Approach
                select (site.SiteId,Owner:owner,Center:center,Impact:impact)).ToArray();
            if(options.Length==0||units.Length==0)return null;

            bool Assigned(PlayableEntitySnapshot unit,int site,NavPoint center)
            {
                if(Advancing(unit,site,rejected))return true;
                if(unit.Queue?.Pending!=null)return false;
                if(stalled&&assault.Site==site)return false;
                bool Inside(NavPoint point)=>Distance(point,center)<=p.RelayCaptureRadius;
                return Inside(unit.Position)||
                    unit.CurrentOrder is PlayableTacticalOrderSnapshot order&&
                    (unit.Moving&&(order.Kind is PlayableTacticalOrderKind.Move or PlayableTacticalOrderKind.AttackMove)&&Inside(order.Destination)||
                     order.TargetId>0&&o.Entities.Any(enemy=>enemy.Id==order.TargetId&&o.IsHostile(enemy.Owner)&&Inside(enemy.Position)));
            }

            // A held circle cannot change hands until its visible defenders are
            // removed. Use the ordinary certified attack path for the clearing force.
            var fight=options.Where(site=>!defending||Enemies(o,p,site.Center).Length>0)
                .Where(site=>assignment==null||assignment.RelaySite<=0||assignment.RelaySite==site.SiteId)
                .Select(site=>new{Site=site,Members=units.Where(u=>Distance(u.Position,site.Center)<=p.ExplorerVision).ToArray()})
                .Where(x=>x.Members.Length>0&&Enemies(o,p,x.Site.Center).Length>0&&
                    AiActiveStrategyPolicy.Force(x.Members,p,ai)*ai.Value("activeStrategy.maximumEncounterRatio")>=
                    AiActiveStrategyPolicy.Force(Enemies(o,p,x.Site.Center),p,ai))
                .OrderByDescending(x=>x.Site.Impact).ThenBy(x=>x.Site.SiteId)
                .Select(x=>Combat(o,p,x.Members,x.Site.SiteId,1)).FirstOrDefault(action=>action!=null);
            if(fight!=null)
            {
#if UNITY_INCLUDE_TESTS
                AssaultReadbackForTests?.Invoke(new{Type="relay-clear",Owner=o.OwnerId,o.Tick,Site=fight.SiteId,Enemy=fight.TargetId,Units=fight.EntityIds});
#endif
                if(assault!=null)arbiter.SaveRelayAssault(assault);
                return fight;
            }
            var available=units.Where(unit=>!options.Any(site=>(assignment==null||assignment.RelaySite<=0||assignment.RelaySite==site.SiteId)&&Assigned(unit,site.SiteId,site.Center))).ToDictionary(unit=>unit.Id);
            var routes=o.RouteProofs.Where(route=>route.Kind==PlayableRouteTargetKind.PublicObjective&&available.ContainsKey(route.UnitId)&&
                rejected?.Invoke(route.TargetId,route.Goal)!=true).ToLookup(route=>route.TargetId);
            var targets=from site in options
                let at=defending?units.Count(unit=>Assigned(unit,site.SiteId,site.Center)):0
                let capture=defending?(seconds:0d,breaks:false):CaptureTime(o,p,counts,leader,site.SiteId,site.Owner)
                from cohort in routes[site.SiteId].GroupBy(route=>(route.Goal,Air:PlayableUnitRules.Aircraft(available[route.UnitId].Kind)))
                let sorted=cohort.OrderBy(route=>AiExpansionPlanner.RouteLength(route)/PlayableUnitRules.Speed(p,available[route.UnitId].Kind,available[route.UnitId].Upgraded)).ThenBy(route=>route.UnitId).ToArray()
                let arrival=AiExpansionPlanner.RouteLength(sorted[sorted.Length-1])/PlayableUnitRules.Speed(p,available[sorted[sorted.Length-1].UnitId].Kind,available[sorted[sorted.Length-1].UnitId].Upgraded)
                let members=sorted.Select(route=>available[route.UnitId]).ToArray()
                let enemies=Enemies(o,p,site.Center)
                let force=AiActiveStrategyPolicy.Force(members.Concat(units.Where(u=>Assigned(u,site.SiteId,site.Center))).Distinct().ToArray(),p,ai)
                let resistance=AiActiveStrategyPolicy.Force(enemies,p,ai)
                let support=AiActiveStrategyPolicy.Force(o.Entities.Where(u=>u.Health>0&&u.Owner!=o.Owner&&!o.IsHostile(u.Owner)&&
                    Distance(u.Position,site.Center)<=p.ExplorerVision).ToArray(),p,ai)
                let clearance=enemies.Sum(enemy=>(double)enemy.Health)/Math.Max(1,force/ai.Value("utility.forceHorizonSeconds"))
                let duration=arrival+clearance+capture.seconds
                let onTime=duration<=state.SecondsToVictory+1e-9
                where state.WarningActive||!defending&&state.SecondsToVictory<=PlayableDominationSnapshot.WarningSeconds+duration
                orderby defending?at:0, assignment?.RelaySite==site.SiteId descending,
                    (force+support)*ai.Value("activeStrategy.maximumEncounterRatio")>=resistance descending,
                    onTime descending, capture.breaks descending,
                    capture.breaks?0:site.Impact descending, cohort.Key.Air, duration, sorted.Length descending, site.SiteId
                select new{site.SiteId,Guards=at,Goal=cohort.Key.Goal,Routes=sorted,Arrival=arrival,Capture=capture.seconds,
                    Force=force,Support=support,Resistance=resistance,Clearance=clearance,Breaks=capture.breaks};
            var candidates=targets.ToArray();
            if(assault!=null)
            {
                var bids=candidates.GroupBy(c=>c.SiteId).Select(g=>g.First()).Select(c=>new AiRelayBid{
                    Site=c.SiteId,Tick=o.Tick,Started=assault.Bids.FirstOrDefault(b=>b.Site==c.SiteId)?.Started??o.Tick,
                    Arrival=c.Arrival,Capture=c.Capture,Force=c.Force,
                    Resistance=c.Resistance,Clearance=c.Clearance,Breaks=c.Breaks}).ToList();
                var active=assault.Bids.FirstOrDefault(b=>b.Site==assault.Site);
                if(!stalled&&active!=null&&!bids.Any(b=>b.Site==active.Site)&&options.Any(s=>s.SiteId==active.Site&&units.Any(u=>Assigned(u,s.SiteId,s.Center))))
                    bids.Add(new AiRelayBid{Site=active.Site,Tick=o.Tick,Started=active.Started,
                        Arrival=Math.Max(0,active.Arrival-(o.Tick-active.Tick)/30d),Capture=active.Capture,
                        Force=active.Force,Resistance=active.Resistance,Clearance=active.Clearance,Breaks=active.Breaks});
                assault.Bids=bids.ToArray();
                arbiter.SaveRelayAssault(assault);
            }
            var chosen=candidates.FirstOrDefault();
            if(chosen==null)return null;
            if(!defending&&(chosen.Force+chosen.Support)*ai.Value("activeStrategy.maximumEncounterRatio")<chosen.Resistance&&assignment?.RelaySite!=chosen.SiteId)
                return null;
            if(assignment?.RelaySite==chosen.SiteId&&o.Tick<assignment.RelayDeparture)
            {
#if UNITY_INCLUDE_TESTS
                AssaultReadbackForTests?.Invoke(new{Type="relay-assemble",Owner=o.OwnerId,o.Tick,Site=chosen.SiteId,assignment.RelayDeparture});
#endif
                return null;
            }
            int need=defending&&options.Length>1?Math.Max(1,(units.Length+options.Length-1)/options.Length-chosen.Guards):units.Length;
            var ids=chosen.Routes.Take(need).Select(route=>route.UnitId).Distinct().ToArray();
            // Reposition one stopped member at a time, so formation arrival slots
            // cannot put the entire corrective move outside the capture circle.
            if(stalled&&assault.Site==chosen.SiteId||ids.Any(id=>available[id].Queue?.Active?.PublicObjectiveId==chosen.SiteId&&
                !available[id].Moving&&available[id].NavigationOutcome==NavigationOutcome.Arrived&&
                Distance(available[id].Position,options.First(s=>s.SiteId==chosen.SiteId).Center)>p.RelayCaptureRadius))
                ids=ids.Take(1).ToArray();
            if(assault!=null){assault.Begin(chosen.SiteId,chosen.Goal,o.Tick);arbiter.SaveRelayAssault(assault);}
#if UNITY_INCLUDE_TESTS
            AssaultReadbackForTests?.Invoke(new{Type="relay-assault",Owner=o.OwnerId,o.Tick,Site=chosen.SiteId,chosen.Goal,Units=ids,
                chosen.Arrival,chosen.Clearance,chosen.Capture,chosen.Force,chosen.Resistance,Shared=assignment?.RelaySite==chosen.SiteId});
#endif
            return new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,
                PlayableCommandKind.AttackMove,ids,chosen.Goal,siteId:chosen.SiteId,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }

        internal static bool Available(PlayableAiObservation o,PlayableEntitySnapshot unit)=>unit.Owner==o.Owner&&unit.Health>0&&!unit.Held&&
            unit.OrderStamp?.Origin!=PlayableOrderOrigin.Human&&unit.Kind!=PlayableEntityKind.Skat&&
            AiRosterCatalog.Initial.LineWeight(unit.Kind)+AiRosterCatalog.Initial.SupportWeight(unit.Kind)>0;

        internal static bool NeedsGroundSupport(PlayableAiObservation o,PlayableProfile p,int site)
        {
            if(!o.Domination.HasValue||!o.PublicScoutObjectives.Any(t=>t.SiteId==site&&t.Reachable&&
                t.Role==PlayablePublicScoutObjectiveRole.Relay))return false;
            int owner=Team(o,site);
            if(owner!=0&&owner!=o.Team)return true;
            var relay=o.Sites.FirstOrDefault(s=>s.Site.Id==site);
            return relay!=null&&(relay.Contested||Enemies(o,p,relay.Site.Position).Any(e=>
                !PlayableUnitRules.Aircraft(e.Kind)&&Distance(e.Position,relay.Site.Position)<=p.RelayCaptureRadius));
        }

        internal static bool HasRelays(PlayableAiObservation o)=>o.PublicScoutObjectives.Any(t=>t.Role==PlayablePublicScoutObjectiveRole.Relay);
        internal static bool IsRelay(PlayableAiObservation o,int site)=>o.PublicScoutObjectives.Any(t=>t.SiteId==site&&t.Role==PlayablePublicScoutObjectiveRole.Relay);
        internal static string Claim(int site)=>"relay:"+site.ToString(System.Globalization.CultureInfo.InvariantCulture);
        internal static bool IsClaim(AiArmyState a)=>a.Objective!=null&&a.Objective.StartsWith("relay:",StringComparison.Ordinal);
        static double Distance(NavPoint a,NavPoint b)=>AiOpponentSelector.Distance(a,b);
        internal static PlayableEntitySnapshot[] Enemies(PlayableAiObservation o,PlayableProfile p,NavPoint point)=>
            o.Entities.Where(u=>u.Health>0&&o.IsHostile(u.Owner)&&Distance(u.Position,point)<=p.ExplorerVision).ToArray();
        internal static bool Needed(PlayableAiObservation o,PlayableProfile p,int site)
        {
            var s=o.Sites.FirstOrDefault(x=>x.Site.Id==site);
            if(o.Domination.HasValue&&Team(o,site)==o.Team)
                return o.Domination.Value.ScoringTeam==o.Team&&o.Domination.Value.Score>0;
            if(s==null)return true; // Inspect public geography; hidden control is never assumed.
            if(Enemies(o,p,s.Site.Position).Length>0||s.Contested)return true;
            if(s.Owner.HasValue&&!o.IsHostile(s.Owner.Value))return false;
            return o.Domination.HasValue||!s.Claimant.HasValue||s.Claimant==o.Owner||o.IsHostile(s.Claimant.Value);
        }
        internal static bool Viable(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge,
            IEnumerable<PlayableEntitySnapshot> members,int site,NavPoint target)
        {
            var ours=members.ToArray();
            var point=o.Sites.FirstOrDefault(s=>s.Site.Id==site)?.Site.Position??target;
            // Include only allies actually present at this fight, never an unseen team army.
            var allies=o.Entities.Where(u=>u.Health>0&&u.Owner!=o.Owner&&!o.IsHostile(u.Owner)&&Distance(u.Position,point)<=p.RelayCaptureRadius);
            var force=ours.Concat(allies).GroupBy(u=>u.Id).Select(g=>g.First()).ToArray();
            return AiActiveStrategyPolicy.Force(force,p,ai)*ai.Value("activeStrategy.maximumEncounterRatio")>=AiActiveStrategyPolicy.Force(Enemies(o,p,point),p,ai)&&
                AiActiveStrategyPolicy.ViableApproach(o,p,ai,force,site,target,PlayableRouteTargetKind.PublicObjective,knowledge);
        }
        internal static bool Holding(PlayableAiObservation o,PlayableProfile p,int site,IEnumerable<PlayableEntitySnapshot> members)=>
            o.Sites.Any(s=>s.Site.Id==site&&members.Any(u=>Distance(u.Position,s.Site.Position)<=p.RelayCaptureRadius));
        internal static void ObserveScout(PlayableAiObservation o,AiArmyRegistry registry,AiProfile ai,PlayableProfile p,AiArmyState a)
        {
            var target=o.PublicScoutObjectives.FirstOrDefault(t=>t.Role==PlayablePublicScoutObjectiveRole.Relay&&Claim(t.SiteId)==a.Objective);
            var unit=o.Entities.FirstOrDefault(u=>u.Id==a.LeaderId&&u.Owner==o.Owner&&u.Health>0);
            bool holding=target!=null&&unit!=null&&Holding(o,p,target.SiteId,new[]{unit});
            if(target==null||!target.Reachable||unit==null||unit.Held||unit.OrderStamp?.Origin==PlayableOrderOrigin.Human||
                !Needed(o,p,target.SiteId)&&!AiScoutPlanner.CombatInProgress(a)||
                !holding&&!AiScoutPlanner.CombatInProgress(a)&&o.Tick-a.ProgressTick>=AiProfile.SecondsToTicks(ai.Value("armies.stallSeconds"),30))
            {registry.Disband(a.Id);return;}
            registry.UpdateMission(a.Id,AiScoutPlanner.Policy,holding?AiArmyPhase.Engaging:AiArmyPhase.Advancing,o.Tick,a.Objective,unit.Position,target.Approach,
                progressed:holding&&!o.Sites.Any(s=>s.Site.Id==target.SiteId&&s.Contested)||a.Anchor.HasValue&&!unit.Position.Equals(a.Anchor.Value));
        }
        internal static PlayableAiAction Combat(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot[] units,int site,long actionId)
        {
            var relay=o.Sites.FirstOrDefault(s=>s.Site.Id==site);if(relay==null)return null;
            var enemy=Enemies(o,p,relay.Site.Position).OrderByDescending(e=>Distance(e.Position,relay.Site.Position)<=p.RelayCaptureRadius)
                .ThenBy(e=>units.Select(u=>Distance(u.Position,e.Position)).DefaultIfEmpty(double.MaxValue).Min()).ThenBy(e=>e.Id)
                .FirstOrDefault(e=>units.Any(u=>o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.VisibleEnemy&&r.TargetId==e.Id)));
            if(enemy==null)return null;
            var ids=units.Where(u=>o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.VisibleEnemy&&r.TargetId==enemy.Id)).Select(u=>u.Id).ToArray();
            return new PlayableAiAction(actionId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Attack,ids,enemy.Position,
                siteId:site,targetId:enemy.Id,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }
        internal static bool CombatOrder(PlayableAiObservation o,PlayableProfile p,int site,PlayableEntitySnapshot unit)
        {
            var s=o.Sites.FirstOrDefault(x=>x.Site.Id==site);var stamp=unit.OrderStamp;
            return s!=null&&unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack&&stamp?.Origin==PlayableOrderOrigin.Ai&&stamp.Owner==o.Owner&&
                stamp.Source==PlayableAiOpeningComposition.SourceIdentity&&stamp.Generation==o.Generation&&
                Enemies(o,p,s.Site.Position).Any(e=>e.Id==unit.CurrentOrder.TargetId);
        }
        internal static PlayableAiAction PlanScout(PlayableAiObservation o,AiKnowledgeState knowledge,AiArmyRegistry registry,PlayableProfile p,AiProfile ai,int budget,int reservedActor,int openingScout=0,ISet<int> reservedActors=null)
        {
            if(!HasRelays(o)||budget<=0)return null;
            var claims=registry.Capture().Armies.Where(a=>a.Role==AiArmyRole.Scout&&a.Phase!=AiArmyPhase.Disbanded).ToArray();
            if(claims.Any(a=>a.TacticalOwner==AiScoutPlanner.Policy&&IsClaim(a))||claims.Length>=budget)return null;
            var units=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&u.Kind==PlayableEntityKind.Explorer&&u.Id!=reservedActor&&reservedActors?.Contains(u.Id)!=true&&(openingScout==0||u.Id==openingScout)&&!u.Held&&
                u.OrderStamp?.Origin!=PlayableOrderOrigin.Human&&registry.ArmyFor(u.Id)==0).ToArray();
            var choices=from t in o.PublicScoutObjectives
                where t.Role==PlayablePublicScoutObjectiveRole.Relay&&t.Reachable&&Needed(o,p,t.SiteId)
                from u in units
                where Viable(o,p,ai,knowledge,new[]{u},t.SiteId,t.Approach)
                from route in o.RouteProofs.Where(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.PublicObjective&&r.TargetId==t.SiteId&&r.Target.Equals(t.Approach))
                where AiStaticDefenseSafety.Withdrawal(o,p,u,route.Path,knowledge)
                orderby Enemies(o,p,o.Sites.FirstOrDefault(s=>s.Site.Id==t.SiteId)?.Site.Position??t.Approach).Length>0 descending,
                    AiExpansionPlanner.RouteLength(route),t.SiteId,u.Id
                select new{t,u,route};
            var choice=choices.FirstOrDefault();if(choice==null)return null;
            return new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,
                PlayableCommandKind.Move,new[]{choice.u.Id},choice.route.Goal,siteId:choice.t.SiteId,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }
    }
}
