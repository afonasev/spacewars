using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class TeamSitePrefilterTests
    {
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile A=AiProfile.Initial;
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static PlayableAiOwnerLoop Owner()=>new PlayableAiOwnerLoop(P,PlayableAiOpeningComposition.Initialize(41,"owner-a"),71);
        static Func<PlayableAiObservation,AiKnowledgeState,bool,int[]> Query(PlayableAiOwnerLoop owner)=>(Func<PlayableAiObservation,AiKnowledgeState,bool,int[]>)Delegate.CreateDelegate(typeof(Func<PlayableAiObservation,AiKnowledgeState,bool,int[]>),owner,typeof(PlayableAiOwnerLoop).GetMethod("RequestedTeamSites",F));
        static TerritorySiteSnapshot Site(int id,int variant)
        {
            var kind=variant==5?PlayableBuildingKind.Headquarters:variant==6?PlayableBuildingKind.Outpost:PlayableBuildingKind.Mine;
            var site=new TerritorySite(id,kind,new NavPoint(10+(id%8)*7,10+(id/8)*7),Array.Empty<TerritorySlot>());
            return new TerritorySiteSnapshot(site,variant==3?(PlayableOwner?)PlayableOwner.Enemy:null,variant==2?(PlayableOwner?)PlayableOwner.Enemy:null,0,variant==4,variant==1?id+100:0,false);
        }
        static PlayableAiObservation Observation(int variant,int credits=1000,bool moving=false,bool routes=true,bool enemy=false)
        {
            var sites=Enumerable.Range(1,32).Select(i=>Site(i,i%8==0?0:variant)).ToArray();
            var own=new PlayableEntitySnapshot(1001,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(0,0),100,moving,0,0,0);
            var units=new[]{own}.Concat(enemy?new[]{new PlayableEntitySnapshot(1002,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(17,10),100,false,0,0,0)}:Array.Empty<PlayableEntitySnapshot>()).ToArray();
            var home=new[]{new PlayableBuildingSnapshot(101,PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(-20,-20),100,1,0,0,default,siteId:50)};
            var objectives=sites.Select(s=>new PlayablePublicScoutObjective(s.Site.Id,s.Site.Position,PlayablePublicScoutObjectiveRole.ExpansionIncome,true)).ToArray();
            var proofs=routes?sites.Select(s=>new PlayableRouteProof(1001,PlayableOwner.Player,71,0,1,own.Position,P.ExplorerCollisionRadius,PlayableRouteTargetKind.PublicObjective,s.Site.Id,s.Site.Position,s.Site.Position,new[]{s.Site.Position})).ToArray():Array.Empty<PlayableRouteProof>();
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,41,0,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,credits,null,units,home,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:sites,publicScoutObjectives:objectives,routeProofs:proofs,activeProfile:P,homeSiteId:50,ownerId:"owner-a"));
        }
        // This owner has no assigned armies or pending actions; retain the original
        // site predicates and ordering to detect any result change in the optimized query.
        static int[] Original(PlayableAiObservation o,AiKnowledgeState intel,bool canExpand)
            =>o.Sites.Where(s=>canExpand&&AiIncomeSiteSelection.TerritoryAllowed(o,P,A,intel,s)&&AiIncomeSiteSelection.SiteSafe(o,P,intel,s)&&s.CenterId==0&&!s.Contested&&(!s.Owner.HasValue||s.Owner==o.Owner)&&(!s.Claimant.HasValue||s.Claimant==o.Owner)&&
                (s.Site.Kind==PlayableBuildingKind.Mine||s.Site.Kind==PlayableBuildingKind.Outpost)&&TerritoryRules.Income(P,s.Site.Kind)>0&&o.Credits>=TerritoryRules.Cost(P,s.Site.Kind))
                .Where(s=>o.Entities.Any(u=>u.Owner==o.Owner&&u.Kind==PlayableEntityKind.Explorer&&u.Health>0&&!u.Moving&&u.CurrentOrder==null&&o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.TargetId==s.Site.Id&&r.Kind==PlayableRouteTargetKind.PublicObjective)))
                .OrderBy(s=>o.Entities.Where(u=>u.Owner==o.Owner&&u.Kind==PlayableEntityKind.Explorer&&u.Health>0).Select(u=>AiOpponentSelector.Distance(u.Position,s.Site.Position)).DefaultIfEmpty(double.MaxValue).Min()).ThenBy(s=>s.Site.Id).Select(s=>s.Site.Id).ToArray();
        [Test] public void ExistingPredicateOrderAndRequestedSiteOrderMatchAcrossRejectionsAndMemory()
        {
            var owner=Owner();var query=Query(owner);var checkpoint=owner.Checkpoint;int eligible=0,rejected=0;
            for(int variant=0;variant<=6;variant++)foreach(int credits in new[]{0,1000})foreach(bool moving in new[]{false,true})foreach(bool routes in new[]{false,true})foreach(bool canExpand in new[]{false,true})foreach(bool remembered in new[]{false,true}){
                var o=Observation(variant,credits,moving,routes,enemy:variant==6);
                var intel=new AiKnowledgeState(o.OwnerId,o.Generation,o.Tick,remembered?new[]{new AiKnownContact(3000,PlayableOwner.Enemy,true,(int)PlayableBuildingKind.Headquarters,new NavPoint(50,10),100,0,1,false)}:Array.Empty<AiKnownContact>(),Array.Empty<AiVisitedArea>());
                var expected=Original(o,intel,canExpand);CollectionAssert.AreEqual(expected,query(o,intel,canExpand),$"variant={variant} credit={credits} moving={moving} routes={routes} expand={canExpand} memory={remembered}");if(expected.Length>0)eligible++;else rejected++;
            }
            Assert.Greater(eligible,0);Assert.Greater(rejected,0);Assert.AreSame(checkpoint,owner.Checkpoint);
        }
        [Test] public void ReportTeamSiteCostWithoutTimingThreshold()
        {
            var owner=Owner();var query=Query(owner);var o=Observation(1);var intel=new AiKnowledgeState(o.OwnerId,o.Generation,o.Tick,Array.Empty<AiKnownContact>(),Array.Empty<AiVisitedArea>());var expected=Original(o,intel,true);
            CollectionAssert.AreEqual(expected,query(o,intel,true));const int count=1000;int total=0;var watch=Stopwatch.StartNew();for(int i=0;i<count;i++)total+=query(o,intel,true).Length;watch.Stop();
            Assert.AreEqual(count*expected.Length,total);TestContext.WriteLine($"TEAM_SITES calls={count} sites={o.Sites.Count} selected={total} wall_ms={watch.Elapsed.TotalMilliseconds:F6} profile={PerformanceProfile.Enabled}");
        }
    }
}
