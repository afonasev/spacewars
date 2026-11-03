using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class RelayTowerAiThreadedTests
    {
        [UnityTest,Timeout(180000)] public IEnumerator PreparedNativeAiChoosesRelayThroughOrdinaryHost()
        {
            const int seed=7112026;var p=PlayableProfile.RecastGroupCorridorDefault;var map=(ThreeCrossingsMap)p.AuthoredMap;
            var basis=map.Configuration(p,seed,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            var home=basis.Sites.Single(s=>s.Id==basis.Starts[basis.Assignments[0]].SiteId);
            // Own the income mines on the home bank, without creating vulnerable
            // assets beside the hostile base. Leave home building capacity free.
            var buildings=basis.Sites.Where(s=>s.Kind==PlayableBuildingKind.Mine&&Math.Sign(s.Position.X)==Math.Sign(home.Position.X)).Select(s=>new OfflineScenarioBuilding(1,PlayableBuildingKind.Mine,s.Id,0,s.Position)).ToList();
            var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.Hangar};
            for(int i=0;i<kinds.Length;i++)buildings.Add(new OfflineScenarioBuilding(1,kinds[i],home.Id,home.Slots[i].Id,home.Slots[i].Position,home.Slots[i].Heading));
            var units=new List<OfflineScenarioUnit>{new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-43,-4))};
            var walls=new NavGeometry(p.ArenaHalfExtent,map.MovementBlockers.ToArray(),1);
            var envelopes=basis.Sites.Select(s=>(s.Position,TerritoryRules.Radius(p,s.Kind))).Concat(basis.Sites.SelectMany(s=>s.Slots.Select(v=>(v.Position,Math.Max(p.HangarFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.ScienceFootprintRadius)))))).ToArray();
            double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
            var candidates=(from x in Enumerable.Range(-60,121).Where(v=>v%3==0) from z in Enumerable.Range(-60,121).Where(v=>v%3==0) let point=new NavPoint(x,z) where map.SurfaceHeight(point)==0&&walls.IsFree(point,1.2)&&Distance(point,home.Position)<30&&envelopes.All(e=>Distance(point,e.Item1)>e.Item2+1.2)&&basis.Starts.All(s=>Distance(point,s.ExplorerAnchor)>3) orderby Distance(point,home.Position) select point).ToArray();
            foreach(var kind in Enumerable.Repeat(PlayableEntityKind.Tank,8).Concat(new[]{PlayableEntityKind.Shkval,PlayableEntityKind.Hawk}))
            {var point=candidates.First(v=>units.All(u=>Distance(v,u.Position)>3));units.Add(new OfflineScenarioUnit(1,kind,point));}
            var costs=new double[4,4];for(int i=0;i<4;i++)for(int j=0;j<4;j++)costs[i,j]=basis.RouteCost(i,j);
            var config=new OfflineMatchConfiguration(p,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,seed,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,scenario:units.ToArray(),terrain:map.Id,scenarioBuildings:buildings.ToArray());
            var notes=new ConcurrentQueue<string>();var failures=new ConcurrentDictionary<string,int>();
            void Note(string text){if(notes.Count<256)notes.Enqueue(text);}
            var oldGuard=AiExpansionPlannerGuardObserverForTests.Observer;var oldGuardOwner=AiExpansionPlannerGuardObserverForTests.OwnerFilter;
            var oldDecision=PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests;var oldDecisionOwner=PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests;
            AiExpansionPlannerGuardObserverForTests.OwnerFilter="player-1";AiExpansionPlannerGuardObserverForTests.Observer=(owner,tick,site,actor,stage,ok)=>{if(site==7&&!ok){failures.AddOrUpdate(stage,1,(_,v)=>v+1);if(tick<180)Note(tick+" guard "+actor+" "+stage);}};
            PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests="player-1";PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=r=>{
                if(r.Observation.Tick<180)Note(r.Observation.Tick+" home="+home.Position.X+","+home.Position.Z+" threatened="+r.Threatened+" action="+r.ExpansionAction?.Kind+"/"+r.ExpansionAction?.SiteId+" state="+r.ExpansionState.SiteId+" selected="+string.Join(";",r.Selected)+" actors="+string.Join(";",r.Observation.Entities.Where(e=>e.Owner==r.Observation.Owner&&e.Kind==PlayableEntityKind.Explorer).Select(e=>e.Id+":"+e.Position+":"+e.Moving+":"+e.CurrentOrder?.Kind))+" sites="+string.Join(";",r.Observation.Sites.Select(v=>v.Site.Id+":"+v.Owner+":"+v.Progress))+" routes="+r.Observation.RouteProofs.Count);
            };
            PlayableRuntime runtime=null;UnityHostRouteService host=null;bool claimed=false,moved=false,applied=false;double end=Time.realtimeSinceStartupAsDouble+120;
            try
            {
                runtime=new PlayableRuntime(config,918,startPaused:true);host=new UnityHostRouteService();
                while((!runtime.IsPrepared||runtime.Latest==null)&&Time.realtimeSinceStartupAsDouble<end){host.Service(runtime,8);yield return null;}
                Assert.True(runtime.IsPrepared);var scheduler=(AiAuthorityScheduler)typeof(PlayableRuntime).GetField("aiScheduler",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(runtime);
                // Offline matches publish each roster owner through their scheduler;
                // Runtime.AiCheckpoint belongs to the two-owner diagnostic adapter.
                PlayableAiOwnerCheckpoint OwnerCheckpoint()=>scheduler.CheckpointFor("player-1");
                var first=runtime.Latest;var actor=first.Entities.Single(e=>e.Owner==PlayableOwner.Player&&Distance(e.Position,new NavPoint(-43,-4))<.01);
                runtime.RequestPause(false);
                while(Time.realtimeSinceStartupAsDouble<end&&runtime.Latest.Outcome==PlayableMatchOutcome.Playing)
                {
                    host.Service(runtime,8);Assert.IsNull(runtime.Latest.Failure);
                    var cp=OwnerCheckpoint();claimed|=cp?.Expansion?.SiteId==7;
                    moved|=runtime.Latest.Entities.Any(e=>e.Id==actor.Id&&Distance(e.Position,actor.Position)>.2);
                    applied|=cp?.Records.Any(r=>r.Policy=="expansion"&&r.Kind==PlayableCommandKind.Move&&r.RuntimeStatus==PlayableCommandStatus.Applied)==true;
                    if(runtime.Latest.Sites.Any(s=>s.Site.Id==7&&s.Owner==PlayableOwner.Player)&&claimed&&moved&&applied)break;
                    yield return null;
                }
                var checkpoint=OwnerCheckpoint();
                string diagnostics="tick="+runtime.Latest.Tick+" expansion="+checkpoint?.Expansion?.SiteId+"/"+checkpoint?.Expansion?.Reason+" last="+checkpoint?.LastPolicy+"/"+checkpoint?.LastActionKind+" records="+string.Join(";",checkpoint?.Records.Select(r=>r.Policy+":"+r.Kind+":"+r.RuntimeStatus)??Array.Empty<string>());
                Assert.True(claimed,"native owner loop actually selected site 7; "+diagnostics);Assert.True(moved,"eligible Explorer actually moved");Assert.True(applied,"ordinary expansion move was applied");Assert.True(runtime.Latest.Sites.Any(s=>s.Site.Id==7&&s.Owner==PlayableOwner.Player),"AI relay capture");
                Assert.False(runtime.Latest.Buildings.Any(b=>b.Kind==PlayableBuildingKind.RelayTower));
                Assert.True(runtime.Latest.Vision.Sources.Any(v=>v.Position.Equals(new NavPoint(-40,0))&&v.Radius==p.RelayVisionRange),"native AI receives its captured relay vision");
                string output=Path.GetFullPath("../.local/relay-integration/ai");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"native-ai.json"),"{\"result\":\"PASS\",\"seed\":"+seed+",\"tick\":"+runtime.Latest.Tick+",\"claimed\":true,\"moved\":true,\"applied\":true,\"captured\":true,\"scope\":\"prepared native midgame; current AI profile; real owner loop and route host\"}");
            }
            finally{AiExpansionPlannerGuardObserverForTests.Observer=oldGuard;AiExpansionPlannerGuardObserverForTests.OwnerFilter=oldGuardOwner;PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=oldDecision;PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=oldDecisionOwner;
                string diagnostics=Path.GetFullPath("../.local/relay-integration/ai");Directory.CreateDirectory(diagnostics);File.WriteAllLines(Path.Combine(diagnostics,"decision-notes.txt"),notes.Concat(failures.Select(v=>v.Key+"="+v.Value)));
                runtime?.Dispose();if(runtime!=null)Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000));host?.Dispose();}
        }
    }
}
