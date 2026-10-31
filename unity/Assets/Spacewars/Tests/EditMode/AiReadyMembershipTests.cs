using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiArmyTests
    {
        private static double ReadyDistance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        private static double ReadyRouteLength(PlayableRouteProof route)
        {double length=0;var at=route.Origin;foreach(var point in route.Path){length+=ReadyDistance(at,point);at=point;}return length;}
        // Frozen pre-74 query. Preserve this reference independently of the runtime candidate.
        private static PlayableEntitySnapshot[] ReadyReference(PlayableAiObservation o,AiArmyState army,PlayableProfile p)
        {
            var core=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0).OrderBy(u=>u.Id).ToArray();
            return o.Entities.Where(u=>army.Reinforcements.Contains(u.Id)&&core.Any(c=>
                ReadyDistance(u.Position,c.Position)<=p.FollowDistance+p.FollowArrivalTolerance+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,c.Kind)&&
                o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&r.TargetId==c.Id&&AiTacticalExecutor.SafeRoute(o,r,p)&&
                    ReadyRouteLength(r)<=p.FollowDistance+p.FollowArrivalTolerance+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,c.Kind)))).ToArray();
        }
        private static PlayableEntitySnapshot[] ReadyVariant(PlayableAiObservation o,AiArmyState army,PlayableProfile p,bool membership,bool scalar)
        {
            var members=membership?new HashSet<int>(army.Members):null;var reinforcements=membership?new HashSet<int>(army.Reinforcements):null;
            bool Member(int id)=>membership?members.Contains(id):army.Members.Contains(id);
            bool Reinforcement(int id)=>membership?reinforcements.Contains(id):army.Reinforcements.Contains(id);
            var core=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&Member(u.Id)&&!Reinforcement(u.Id)&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0).OrderBy(u=>u.Id).ToArray();
            double spacing=p.FollowDistance+p.FollowArrivalTolerance;
            double tank=p.TankCollisionRadius,explorer=p.ExplorerCollisionRadius,shkval=p.ShkvalCollisionRadius,hawk=p.HawkRadius;
            double CachedRadius(PlayableEntityKind k)=>k==PlayableEntityKind.Hawk?hawk:k==PlayableEntityKind.Shkval?shkval:k==PlayableEntityKind.Explorer?explorer:tank;
            return o.Entities.Where(u=>{
                if(!Reinforcement(u.Id))return false;double radius=scalar?PlayableUnitRules.Radius(p,u.Kind):0;
                return core.Any(c=>ReadyDistance(u.Position,c.Position)<=(scalar?spacing+radius+CachedRadius(c.Kind):p.FollowDistance+p.FollowArrivalTolerance+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,c.Kind))&&
                    o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&r.TargetId==c.Id&&AiTacticalExecutor.SafeRoute(o,r,p)&&
                        ReadyRouteLength(r)<=(scalar?spacing+radius+CachedRadius(c.Kind):p.FollowDistance+p.FollowArrivalTolerance+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,c.Kind))));
            }).ToArray();
        }
        private static PlayableAiObservation ReadyCopy(PlayableAiObservation o,PlayableEntitySnapshot[] entities,PlayableRouteProof[] proofs,PlayableProfile p)
            =>PlayableAiObservation.From(new PlayableSnapshot(o.ProfileId,o.ProfileRevision,o.Generation,o.Seed,o.SnapshotSequence,o.Tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,o.Credits,null,entities,o.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:o.Vision,routeProofs:proofs,owner:o.Owner,ownerId:o.OwnerId,activeProfile:p));
        [Test] public void ReadyMembershipPreservesOrderedResultsAndAuthority()
        {
            const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
            var data=Newtonsoft.Json.JsonConvert.DeserializeObject<PlayableProfileData>(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),"Assets/Spacewars/Content/Resources/PlayableProfile.json")));
            var baseline=PlayableProfile.Create(data,new AiTestMap());var folder=Path.GetFullPath("../.local/three-cpu/ready");Directory.CreateDirectory(folder);
            var store=new NativeBalanceStore(baseline,Path.Combine(folder,"balance-control.json"),false);store.Select(NativeBalanceStore.NewMatchProfileId,NativeBalanceStore.NewMatchProfileVersion);
            var p=store.Active;var ai=store.ActiveAiProfile;var setup=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();OfflineMatchConfiguration config;
            using(var runtime=PlayableRuntime.CreateEightPlayerPerformanceMatch(p,1,setup,startPaused:true,aiProfile:ai)){Assert.AreEqual(400,runtime.DiagnosticGlobalUnits);config=(OfflineMatchConfiguration)typeof(PlayableRuntime).GetField("offlineConfiguration",hidden).GetValue(runtime);}
            var authority=new PlayableAuthorityTick(config,1,aiProfile:ai);
            try{
                var domain=(PlayableDomain)typeof(PlayableAuthorityTick).GetField("domain",hidden).GetValue(authority);var registry=new AiArmyRegistry("player-1",1,ai,AiDifficulty.Fighter);
                var initial=PlayableAiObservation.From(authority.ParticipantView("player-1"));var owned=initial.Entities.Where(u=>u.Owner==initial.Owner).Select(u=>u.Id).ToArray();Assert.AreEqual(50,owned.Length);
                Assert.True(registry.TryCreate(initial,AiArmyRole.Attack,AiArmyPlanner.Policy,owned.Take(25),out var id));Assert.True(registry.TryReinforce(initial,id,AiArmyPlanner.Policy,owned.Skip(25)));
                var real=PlayableAiObservation.From(domain.PlayerSnapshotForArmyAi(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,initial.Owner,true,registry.Capture()));var army=registry.Capture().Armies.Single();
                var registryBefore=Newtonsoft.Json.JsonConvert.SerializeObject(registry.Capture());
                var cases=new List<Tuple<string,PlayableAiObservation,AiArmyState>>{Tuple.Create("actual400-current-native-projection",real,army)};
                foreach(var flags in new[]{new[]{false,true,true},new[]{true,true,true},new[]{false,false,true},new[]{false,true,false}}){var o=A4Observation(100,wave:true,danger:flags[0],route:flags[1],near:flags[2]);var a=new AiArmyState{Members=new[]{1,2,3,4},Reinforcements=new[]{4}};cases.Add(Tuple.Create("near-danger-route-"+string.Join("-",flags),o,a));}
                var withdrawal=army.Copy();withdrawal.Phase=AiArmyPhase.Retreating;cases.Add(Tuple.Create("actual-withdrawal-state",real,withdrawal));
                cases.Add(Tuple.Create("empty",real,new AiArmyState{Members=Array.Empty<int>(),Reinforcements=Array.Empty<int>()}));
                var duplicate=ReadyCopy(real,real.Entities.Reverse().Concat(real.Entities.Where(u=>army.Reinforcements.Contains(u.Id)).Take(3)).ToArray(),real.RouteProofs.Concat(real.RouteProofs).ToArray(),p);var dupArmy=army.Copy();dupArmy.Members=dupArmy.Members.Concat(dupArmy.Members).ToArray();dupArmy.Reinforcements=dupArmy.Reinforcements.Concat(dupArmy.Reinforcements).ToArray();cases.Add(Tuple.Create("reversed-duplicate-current-entities-proofs-membership",duplicate,dupArmy));
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(1,1,"player-1",PlayableCommandKind.Hold,new[]{owned.Last()})).Status);
                var human=PlayableAiObservation.From(domain.PlayerSnapshotForArmyAi(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,initial.Owner,true,registry.Capture()));Assert.True(human.Entities.Single(u=>u.Id==owned.Last()).Held);cases.Add(Tuple.Create("actual-human-Hold-current-projection",human,army));
                var near=A4Observation(100,wave:true,near:true);var positiveDuplicate=ReadyCopy(near,near.Entities.Reverse().Concat(near.Entities.Where(u=>u.Id==4)).ToArray(),near.RouteProofs.Concat(near.RouteProofs).ToArray(),PlayableProfile.Default);var positiveArmy=new AiArmyState{Members=new[]{1,2,3,4,4},Reinforcements=new[]{4,4}};
                CollectionAssert.AreEqual(new[]{4,4},ReadyReference(positiveDuplicate,positiveArmy,PlayableProfile.Default).Select(u=>u.Id));cases.Add(Tuple.Create("positive-duplicate-order",positiveDuplicate,positiveArmy));
                var before=authority.CaptureBytes();
                foreach(var item in cases){var expected=ReadyReference(item.Item2,item.Item3,p);foreach(var m in new[]{false,true})foreach(var v in new[]{false,true})CollectionAssert.AreEqual(expected,ReadyVariant(item.Item2,item.Item3,p,m,v),item.Item1+"/"+m+"/"+v);CollectionAssert.AreEqual(expected,AiTacticalExecutor.Ready(item.Item2,item.Item3,p),item.Item1+" runtime");}
                CollectionAssert.AreEqual(before,authority.CaptureBytes(),"Ready variants never mutate current authority");
                Assert.AreEqual(registryBefore,Newtonsoft.Json.JsonConvert.SerializeObject(registry.Capture()));
            }finally{authority.Stop();}
        }
    }
}
