using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class EightPlayerPerformanceScenarioTests
    {
#if UNITY_INCLUDE_TESTS
        private static PlayableSnapshot CopyProjectionEnvelope(PlayableSnapshot s)
            =>new PlayableSnapshot(s.ProfileId,s.ProfileRevision,s.Generation,s.Seed,s.Sequence,s.Tick,s.Status,s.Paused,s.Outcome,s.Credits,s.Geometry,s.Entities.ToArray(),s.Buildings.ToArray(),s.Projectiles.ToArray(),s.Metrics,s.Failure,s.Sites.ToArray(),s.IncomePerSecond,s.Vision,s.DiscoveredSites.ToArray(),s.Population,s.Impacts.ToArray(),s.ResearchAvailability.ToArray(),s.OwnerResearch.ToArray(),s.PublicScoutObjectives.ToArray(),s.OwnCenterDamage.ToArray(),s.RouteProofs.ToArray(),s.ArtillerySupport.ToArray(),s.Owner,s.ExactCredits,s.OwnerId,s.Team,s.Participants.ToArray(),s.ActiveProfile,s.HomeSiteId,s.OwnerEliminated,s.SettledIncome,s.IntelEnvelopes.ToArray(),s.Sounds.ToArray(),s.IncomeEvents.ToArray(),s.Mission,s.GroundTrails.ToArray(),s.BuildingDestructions.ToArray(),s.Wrecks.ToArray()).WithTeamSignals(s.TeamSignals.ToArray()).WithMatchEvents(s.MatchEvents.ToArray());
        [Test] public void ClosedProofPathCopiesRetainDistinctDtosAndPublicInputDetachment()
        {
            foreach(int length in new[]{0,1,2,16,64}){
                var input=Enumerable.Range(0,length).Select(i=>new NavPoint(i+.25,-i-.5)).ToArray();var expected=input.ToArray();
                var proof=new PlayableRouteProof(1,PlayableOwner.Player,1,2,3,default,1,PlayableRouteTargetKind.FriendlyAnchor,4,default,default,input);
                if(length>0)input[0]=new NavPoint(1000,2000);
                var closed=proof.Copy();var again=closed.Copy();Assert.AreNotSame(proof,closed);Assert.AreNotSame(closed,again);Assert.AreNotSame(proof.Path,closed.Path);
                CollectionAssert.AreEqual(expected,proof.Path);CollectionAssert.AreEqual(expected,closed.Path);CollectionAssert.AreEqual(expected,again.Path);
                Assert.True(((IList)closed.Path).IsReadOnly);if(length>0)Assert.Throws<NotSupportedException>(()=>((IList)closed.Path)[0]=new NavPoint(5,6));
                PlayableRouteProof.ClonePathOnCopyForTests=true;PlayableRouteProof legacy;
                try{legacy=proof.Copy();}finally{PlayableRouteProof.ClonePathOnCopyForTests=false;}
                AssertSnapshotGraphEqual(legacy,closed,"proof"+length,0);
            }
            var empty=new PlayableRouteProof(1,PlayableOwner.Player,1,0,1,default,1,PlayableRouteTargetKind.FriendlyAnchor,2,default,default,null);Assert.IsEmpty(empty.Copy().Path);
        }
        [Test] public void ClosedProofPathCopyOnRealOwnerProjectionKeepsEveryCallerVisibleField()
        {
            const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
            var data=Newtonsoft.Json.JsonConvert.DeserializeObject<PlayableProfileData>(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),"Assets/Spacewars/Content/Resources/PlayableProfile.json")));
            var folder=Path.GetFullPath("../.local/three-cpu/proof");Directory.CreateDirectory(folder);var store=new NativeBalanceStore(PlayableProfile.Create(data,new AiTestMap()),Path.Combine(folder,"balance-control.json"),false);store.Select(NativeBalanceStore.NewMatchProfileId,NativeBalanceStore.NewMatchProfileVersion);
            var p=store.Active;var ai=store.ActiveAiProfile;var setup=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();OfflineMatchConfiguration config;
            using(var runtime=PlayableRuntime.CreateEightPlayerPerformanceMatch(p,1,setup,startPaused:true,aiProfile:ai)){Assert.AreEqual(400,runtime.DiagnosticGlobalUnits);config=(OfflineMatchConfiguration)typeof(PlayableRuntime).GetField("offlineConfiguration",hidden).GetValue(runtime);}
            var authority=new PlayableAuthorityTick(config,1,aiProfile:ai);
            try{
                var domain=(PlayableDomain)typeof(PlayableAuthorityTick).GetField("domain",hidden).GetValue(authority);var initial=PlayableAiObservation.From(authority.ParticipantView("player-1"));var registry=new AiArmyRegistry("player-1",1,ai,AiDifficulty.Fighter);var owned=initial.Entities.Where(u=>u.Owner==initial.Owner).Select(u=>u.Id).ToArray();Assert.AreEqual(50,owned.Length);
                Assert.True(registry.TryCreate(initial,AiArmyRole.Attack,AiArmyPlanner.Policy,owned.Take(25),out var id));Assert.True(registry.TryReinforce(initial,id,AiArmyPlanner.Policy,owned.Skip(25)));var state=registry.Capture();
                PlayableSnapshot Project()=>domain.PlayerSnapshotForArmyAi(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,initial.Owner,true,state);
                PlayableRouteProof.ClonePathOnCopyForTests=true;var legacy=Project();var legacyEnvelope=CopyProjectionEnvelope(legacy);var legacyObservation=PlayableAiObservation.From(legacy);PlayableRouteProof.ClonePathOnCopyForTests=false;
                var next=Project();Assert.Greater(legacy.RouteProofs.Count,1000);AssertSnapshotGraphEqual(legacy,next,"projection",0);AssertSnapshotGraphEqual(legacyEnvelope,CopyProjectionEnvelope(legacy),"constructor",0);AssertSnapshotGraphEqual(legacyObservation,PlayableAiObservation.From(legacy),"observation",0);
                var before=authority.CaptureBytes();var registryBefore=Newtonsoft.Json.JsonConvert.SerializeObject(registry.Capture());
                AssertSnapshotGraphEqual(legacy,Project(),"repeat-projection",0);
                CollectionAssert.AreEqual(before,authority.CaptureBytes());
                Assert.AreEqual(registryBefore,Newtonsoft.Json.JsonConvert.SerializeObject(registry.Capture()));
            }finally{PlayableRouteProof.ClonePathOnCopyForTests=false;authority.Stop();}
        }
#endif
    }
}
