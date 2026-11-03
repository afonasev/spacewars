using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed class RelayTowerContractTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        readonly Type type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        PlayableDomain domain;
        PlayableProfile profile;
        object Call(string method,params object[] args)=>type.GetMethod(method,F).Invoke(domain,args);
        PlayableSnapshot View()=>domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,71);
        TerritorySiteSnapshot Relay()=>View().Sites.Single(s=>s.Site.Id==4);
        public static OfflineMatchConfiguration Configuration(PlayableProfile p,bool ai=false)
        {
            var points=new[]{new NavPoint(-16,-14),new NavPoint(16,14),new NavPoint(-16,14)};
            var sites=points.Select((v,i)=>new TerritorySite(i+1,PlayableBuildingKind.Headquarters,v,Array.Empty<TerritorySlot>())).Concat(new[]{new TerritorySite(4,PlayableBuildingKind.RelayTower,new NavPoint(0,0),Array.Empty<TerritorySlot>())}).ToArray();
            var starts=points.Select((v,i)=>new OfflineStart("start-"+i,i+1,v,new NavPoint(v.X+5,v.Z),pin:i+1)).ToArray();
            var roster=new[]{new OfflineParticipant("player-1",1,1,ai?OfflineControl.Ai:OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human),new OfflineParticipant("player-3",3,1,OfflineControl.Human)};
            var costs=new double[3,3];for(int i=0;i<3;i++)for(int j=0;j<3;j++)costs[i,j]=i==j?0:40;
            return new OfflineMatchConfiguration(p,"relay-contract-v1","relay-contract-flat","authored test geometry",71,roster,starts,sites,Array.Empty<NavObstacle>(),costs);
        }
        [SetUp] public void Setup(){profile=PlayableProfile.Default;domain=new PlayableDomain(profile,912,Configuration(profile));}
        [TearDown] public void Cleanup(){domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}
        int Spawn(PlayableOwner owner,NavPoint point)=>(int)Call("SpawnUnit",point,owner,PlayableEntityKind.Explorer);
        void Capture(double seconds){for(int i=0;i<(int)Math.Ceiling(seconds*30);i++){Call("AdvanceLingeringVision",1d/30);Call("AdvanceCapture",1d/30);Call("RefreshVision");}}
        void Remove(int id)=>Call("Damage",id,100000);
        void Place(int id,NavPoint point)
        {
            var actor=domain.Navigation.Crowd.Units.Single(u=>u.Id==id);
            Assert.True(domain.Navigation.Crowd.Locate(point,actor.Radius,out var location));
            actor.Position=point;actor.Location=location;
        }
        void KnowledgeScenario()
        {
            Cleanup();var data=PlayableProfile.Default.CopyData();data.headquartersVisionRange=6;data.explorerVision=8;
            profile=PlayableProfile.Create(data);domain=new PlayableDomain(profile,912,Configuration(profile));
        }
        PlayableSnapshot PlayerView(PlayableOwner owner=PlayableOwner.Player)=>domain.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,71,owner);
        [Test] public void RelayIncomeMarkersFollowExistingCadenceAndRestoreWithoutCreditingAgain()
        {
            Spawn(PlayableOwner.Player,new NavPoint(2.5,0));Capture(6);
            double bank=(double)Call("Balance",PlayableOwner.Player),rate=View().IncomePerSecond;
            for(int i=0;i<149;i++)domain.Step(1d/30);
            Assert.False(PlayerView().IncomeEvents.Any(e=>e.Kind==PlayableBuildingKind.RelayTower));
            domain.Step(1d/30);
            var marker=PlayerView().IncomeEvents.Single(e=>e.Kind==PlayableBuildingKind.RelayTower);
            Assert.AreEqual(4,marker.BuildingId);Assert.AreEqual(150,marker.Tick);
            Assert.AreEqual(profile.RelayIncomePerPeriod,marker.Amount);
            Assert.That((double)Call("Balance",PlayableOwner.Player),Is.EqualTo(bank+rate*5).Within(1e-8));
            Assert.False(PlayerView(PlayableOwner.Third).IncomeEvents.Any(e=>e.Kind==PlayableBuildingKind.RelayTower));
            var bytes=domain.CaptureWorldBytes(71,"relay-income");
            var restored=PlayableDomain.RestoreConfiguredWorldBytes(bytes,profile,71,"relay-income",Configuration(profile));
            try
            {
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(71,"relay-income"));
                Assert.AreEqual(marker.Amount,restored.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,71).IncomeEvents.Single(e=>e.Kind==PlayableBuildingKind.RelayTower).Amount);
                domain.Step(1d/30);restored.Step(1d/30);
                CollectionAssert.AreEqual(domain.CaptureWorldBytes(71,"relay-income"),restored.CaptureWorldBytes(71,"relay-income"));
            }
            finally{restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }
        [Test] public void RelayIncomeMarkersRequireFullControlAndSurviveOnlyPartialNeutralization()
        {
            int own=Spawn(PlayableOwner.Player,new NavPoint(2.5,0));Capture(2);Call("EmitIncomeMarkers",PlayableOwner.Player);
            Assert.False(PlayerView().IncomeEvents.Any(e=>e.Kind==PlayableBuildingKind.RelayTower));
            Capture(4);Call("EmitIncomeMarkers",PlayableOwner.Player);
            Assert.True(PlayerView().IncomeEvents.Any(e=>e.Kind==PlayableBuildingKind.RelayTower));
            Remove(own);Spawn(PlayableOwner.Enemy,new NavPoint(-2.5,0));Capture(2);
            Assert.True(PlayerView().IncomeEvents.Any(e=>e.Kind==PlayableBuildingKind.RelayTower));
            Call("EmitIncomeMarkers",PlayableOwner.Enemy);
            Assert.False(PlayerView(PlayableOwner.Enemy).IncomeEvents.Any(e=>e.Kind==PlayableBuildingKind.RelayTower));
            Capture(3.1);
            Assert.False(PlayerView().IncomeEvents.Any(e=>e.Kind==PlayableBuildingKind.RelayTower));
            Capture(6);Call("EmitIncomeMarkers",PlayableOwner.Enemy);
            Assert.True(PlayerView(PlayableOwner.Enemy).IncomeEvents.Any(e=>e.Kind==PlayableBuildingKind.RelayTower));
            Assert.False(PlayerView().IncomeEvents.Any(e=>e.Kind==PlayableBuildingKind.RelayTower));
        }
        [Test] public void PublicSitesDoNotExposeUnseenControlOrGrantAiLiveSites()
        {
            KnowledgeScenario();int enemy=Spawn(PlayableOwner.Enemy,new NavPoint(2.5,0));Capture(6);
            var view=PlayerView();Assert.IsFalse(view.Vision.IsVisible(default));
            Assert.AreEqual(Configuration(profile).Sites.Count,view.PublicSites.Count);
            Assert.False(view.Sites.Any(s=>s.Site.Id==4),"AI and commands still receive only current sight");
            var mark=PlayableMapView.Marks(view).Single(m=>m.Kind==PlayableBuildingKind.RelayTower);
            Assert.IsNull(mark.Owner);Assert.AreEqual(MapMarkState.Empty,mark.State);
            Assert.IsEmpty(view.Vision.KnownRelays);
        }
        [Test] public void RelayMemoryIsTeamScopedPersistsAcrossHiddenTakeoverAndCurrentRestore()
        {
            KnowledgeScenario();int own=Spawn(PlayableOwner.Player,new NavPoint(2.5,0));Capture(6);
            var seen=PlayerView();Assert.AreEqual(PlayableOwner.Player,seen.Vision.KnownRelays.Single().Owner);
            Assert.AreEqual(PlayableOwner.Player,PlayerView(PlayableOwner.Third).Vision.KnownRelays.Single().Owner);
            Remove(own);
            Spawn(PlayableOwner.Enemy,new NavPoint(-2.5,0));Capture(12);
            var hidden=PlayerView();Assert.False(hidden.Sites.Any(s=>s.Site.Id==4));
            Assert.AreEqual(PlayableOwner.Enemy,Relay().Owner);
            Assert.IsNull(hidden.Vision.KnownRelays.Single().Owner,"Residual sight confirms neutralization before the later hidden takeover");
            Assert.IsNull(PlayableMapView.Marks(hidden).Single(m=>m.Kind==PlayableBuildingKind.RelayTower).Owner);
            var bytes=domain.CaptureWorldBytes(71,"relay-memory");
            var restored=PlayableDomain.RestoreConfiguredWorldBytes(bytes,profile,71,"relay-memory",Configuration(profile));
            try
            {
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(71,"relay-memory"));
                var knowledge=restored.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,71).Vision;
                Assert.IsNull(knowledge.KnownRelays.Single().Owner);
            }
            finally{restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
            Spawn(PlayableOwner.Player,new NavPoint(6,0));
            var revisited=PlayerView();Assert.AreEqual(PlayableOwner.Enemy,revisited.Vision.KnownRelays.Single().Owner);
            Assert.AreEqual(PlayableOwner.Player,seen.Vision.KnownRelays.Single().Owner,"Published knowledge is immutable");
        }
        [Test] public void StationaryVisibleCaptureRefreshesRelayMemoryWithoutChangingVisionInputs()
        {
            KnowledgeScenario();Spawn(PlayableOwner.Player,new NavPoint(2.5,0));Assert.IsNull(PlayerView().Vision.KnownRelays.Single().Owner);
            Capture(6);Assert.AreEqual(PlayableOwner.Player,PlayerView().Vision.KnownRelays.Single().Owner);
        }
        [Test] public void CompletedOwnershipSurvivesDepartureAndAlliesWithRealIndependentIncome()
        {
            double baseline=View().IncomePerSecond;int actor=Spawn(PlayableOwner.Player,new NavPoint(2.5,0));Capture(6);
            Assert.AreEqual(PlayableOwner.Player,Relay().Owner);Assert.AreEqual(0,Relay().CenterId);Assert.AreEqual(1,Relay().Progress);
            Assert.That(View().IncomePerSecond-baseline,Is.EqualTo(profile.RelayIncomePerPeriod/profile.IncomePeriodSeconds).Within(1e-9));
            Assert.IsEmpty(OfflinePadWorldActions.Build(View(),profile,4,0));
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Call("BuildAt",4,0,PlayableBuildingKind.RelayTower,0,PlayableOwner.Player,null));
            Remove(actor);Spawn(PlayableOwner.Third,new NavPoint(-2.5,0));Capture(8);
            Assert.AreEqual(PlayableOwner.Player,Relay().Owner);Assert.AreEqual(1,Relay().Progress);
            double bank=(double)Call("Balance",PlayableOwner.Player);Call("SettleIncome",PlayableOwner.Player);
            Assert.That((double)Call("Balance",PlayableOwner.Player)-bank,Is.EqualTo(View().IncomePerSecond*profile.IncomeSettlementSeconds).Within(1e-9));
            var data=profile.CopyData();data.refineryIncomePerPeriod+=100;
            Assert.AreEqual(profile.RelayIncomePerPeriod,PlayableProfile.Create(data).RelayIncomePerPeriod);
        }
        [Test] public void ContestFreezesAndHostileControlMustPassThroughNeutralBeforeRecapture()
        {
            double baseline=View().IncomePerSecond;int actor=Spawn(PlayableOwner.Player,new NavPoint(2.5,0));Capture(6);Remove(actor);
            Spawn(PlayableOwner.Enemy,new NavPoint(-2.5,0));Capture(2);double progress=Relay().Progress;
            Assert.AreEqual(PlayableOwner.Player,Relay().Owner);Assert.Greater(View().IncomePerSecond,baseline);
            int ally=Spawn(PlayableOwner.Third,new NavPoint(0,2.5));Capture(6);
            Assert.True(Relay().Contested);Assert.AreEqual(progress,Relay().Progress);Assert.AreEqual(PlayableOwner.Player,Relay().Owner);
            Remove(ally);Capture(3.1);Assert.IsNull(Relay().Owner);Assert.That(View().IncomePerSecond,Is.EqualTo(baseline).Within(1e-9));
            Capture(6);Assert.AreEqual(PlayableOwner.Enemy,Relay().Owner);Assert.False(View().Buildings.Any(b=>b.Kind==PlayableBuildingKind.RelayTower));
            Call("FinishManually");var result=domain.Result;
            Assert.AreEqual(2,result.Facts.Count(f=>f.Kind==MatchFactKind.RelayCaptured));Assert.AreEqual(1,result.Facts.Count(f=>f.Kind==MatchFactKind.RelayNeutralized));
            var graph=typeof(PostMatchResultsView).GetMethod("GraphEvent",F);var ui=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PostMatchResultsView));
            foreach(var fact in result.Facts.Where(f=>f.Kind==MatchFactKind.RelayCaptured||f.Kind==MatchFactKind.RelayNeutralized))Assert.True((bool)graph.Invoke(ui,new object[]{0,fact}));
        }
        [Test] public void IncompleteCaptureDecaysWithoutCreatingOwnerOrIncome()
        {
            double baseline=View().IncomePerSecond;int actor=Spawn(PlayableOwner.Player,new NavPoint(2.5,0));Capture(2);
            Assert.Greater(Relay().Progress,0);Assert.IsNull(Relay().Owner);Remove(actor);Capture(3);
            Assert.AreEqual(0,Relay().Progress);Assert.IsNull(Relay().Claimant);Assert.AreEqual(baseline,View().IncomePerSecond);
        }
        [TestCase(false,false)] [TestCase(true,false)]
        [TestCase(false,true)] [TestCase(true,true)]
        public void AbandonedPartialCaptureNeverNotifiesControlLoss(bool enemyArrives,bool restore)
        {
            int actor=Spawn(PlayableOwner.Player,new NavPoint(2.5,0));Capture(2);
            Assert.That(Relay().Progress,Is.GreaterThan(0).And.LessThan(1));
            Assert.IsNull(Relay().Owner);
            if(restore)
            {
                var bytes=domain.CaptureWorldBytes(71,"relay-partial-notification");
                var restored=PlayableDomain.RestoreConfiguredWorldBytes(bytes,profile,71,"relay-partial-notification",Configuration(profile));
                Cleanup();domain=restored;
            }
            // Leave the capture radius without damage/combat notifications.
            domain.Navigation.Crowd.Units.Single(u=>u.Id==actor).Position=new NavPoint(-16,-14);
            if(enemyArrives)Spawn(PlayableOwner.Enemy,new NavPoint(-2.5,0));
            var notifications=new NativeMatchNotifications();
            for(int i=0;i<240;i++)
            {
                Call("AdvanceCapture",1d/30);
                var own=domain.PlayerSnapshot(i+1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,71,PlayableOwner.Player);
                Assert.False(own.MatchEvents.Any(e=>e.Kind==MatchEventKind.RelayControlLost),"An incomplete claim is not lost ownership");
                notifications.Update(own,false,i/30d,profile);
                Assert.Zero(notifications.Count,"No false loss may enter notification history");
            }
            Assert.AreEqual(enemyArrives?(PlayableOwner?)PlayableOwner.Enemy:null,Relay().Owner);
            Call("FinishManually");
            Assert.False(domain.Result.Facts.Any(f=>f.Kind==MatchFactKind.RelayNeutralized),"Partial decay is not neutralization");
        }
        [TestCase(false)] [TestCase(true)] public void CurrentCaptureRestoreContinuesPartialCaptureAndNeutralizationExactly(bool neutralizing)
        {
            int actor=Spawn(PlayableOwner.Player,new NavPoint(2.5,0));
            if(neutralizing){Capture(6);Remove(actor);Spawn(PlayableOwner.Enemy,new NavPoint(-2.5,0));}
            Capture(2);
            var bytes=domain.CaptureWorldBytes(71,"relay-save-test");
            var restored=PlayableDomain.RestoreConfiguredWorldBytes(bytes,profile,71,"relay-save-test",Configuration(profile));
            try
            {
                Assert.AreEqual(bytes,restored.CaptureWorldBytes(71,"relay-save-test"));
                for(int i=0;i<330;i++){Call("AdvanceCapture",1d/30);type.GetMethod("AdvanceCapture",F).Invoke(restored,new object[]{1d/30});}
                Assert.AreEqual(domain.CaptureWorldBytes(71,"relay-save-test"),restored.CaptureWorldBytes(71,"relay-save-test"));
            }
            finally{restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }
        [Test] public void RelayVisionStartsAtFullCaptureAndSurvivesDrainAndRecoveryUntilNeutralized()
        {
            KnowledgeScenario();var probe=new NavPoint(0,9.5);int own=Spawn(PlayableOwner.Player,new NavPoint(2.5,0));
            int target=Spawn(PlayableOwner.Enemy,probe);Capture(2);
            Assert.False(PlayerView().Vision.IsVisible(probe),"partial neutral capture cannot reveal beyond unit sight");
            Assert.False(PlayerView(PlayableOwner.Third).Vision.IsVisible(probe));
            Capture(3.1);var captured=PlayerView();Assert.True(captured.Vision.IsVisible(probe),"stationary completion must invalidate source cache");
            Assert.True(captured.Entities.Any(e=>e.Id==target));Assert.True(PlayerView(PlayableOwner.Third).Vision.IsVisible(probe),"ordinary allied team sight");
            Assert.True(captured.Vision.Sources.Any(v=>v.Position.Equals(default(NavPoint))&&v.Radius==profile.RelayVisionRange));
            Assert.False(captured.Vision.IsVisible(new NavPoint(0,10.1)),"relay radius is 10, not an unbounded reveal");
            Remove(own);Call("AdvanceLingeringVision",1d);int enemy=Spawn(PlayableOwner.Enemy,new NavPoint(-2.5,0));var beforeDrain=PlayerView().Vision;
            Capture(2);Assert.AreEqual(PlayableOwner.Player,Relay().Owner);Assert.That(Relay().Progress,Is.GreaterThan(0).And.LessThan(1));
            Assert.True(PlayerView().Vision.IsVisible(probe));Assert.AreEqual(beforeDrain.FogRevision,PlayerView().Vision.FogRevision,"partial drain does not change the source");
            Place(enemy,new NavPoint(-10,10));Capture(3);
            Assert.AreEqual(1,Relay().Progress);Assert.AreEqual(PlayableOwner.Player,Relay().Owner);Assert.True(PlayerView().Vision.IsVisible(probe));
            var ai=PlayableAiObservation.From(domain.PlayerSnapshotForAi(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,71,PlayableOwner.Player));
            Assert.True(ai.Entities.Any(e=>e.Id==target),"native AI reads the same newly visible enemy");
            Place(enemy,new NavPoint(-2.5,0));PlayerView();Capture(5.1);
            Assert.IsNull(Relay().Owner);Assert.False(PlayerView().Vision.IsVisible(probe),"shrinking residual radius must invalidate the cached source");
            Assert.False(PlayerView(PlayableOwner.Third).Vision.IsVisible(probe));Assert.False(PlayerView().Entities.Any(e=>e.Id==target));
            Capture(5.1);Assert.AreEqual(PlayableOwner.Enemy,Relay().Owner);
            Assert.True(PlayerView(PlayableOwner.Enemy).Vision.Sources.Any(v=>v.Position.Equals(default(NavPoint))&&v.Radius==profile.RelayVisionRange));
            Assert.False(PlayerView().Vision.IsVisible(probe));Assert.True(captured.Vision.IsVisible(probe),"published old vision stays immutable");
        }
        [TestCase(false)][TestCase(true)] public void RelayVisionAndExactSaveContinueDuringDrainOrRecovery(bool recovering)
        {
            KnowledgeScenario();var probe=new NavPoint(0,9.5);int own=Spawn(PlayableOwner.Player,new NavPoint(2.5,0));Capture(6);Remove(own);
            int enemy=Spawn(PlayableOwner.Enemy,new NavPoint(-2.5,0));Capture(2);Assert.True(PlayerView().Vision.IsVisible(probe));
            if(recovering)Place(enemy,new NavPoint(-10,10));
            var bytes=domain.CaptureWorldBytes(71,"relay-vision-save");
            var restored=PlayableDomain.RestoreConfiguredWorldBytes(bytes,profile,71,"relay-vision-save",Configuration(profile));
            try
            {
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(71,"relay-vision-save"));
                for(int i=0;i<180;i++)
                {
                    Call("AdvanceCapture",1d/30);type.GetMethod("AdvanceCapture",F).Invoke(restored,new object[]{1d/30});
                    var expected=PlayerView();var actual=restored.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,71,PlayableOwner.Player);
                    Assert.AreEqual(expected.Vision.IsVisible(probe),actual.Vision.IsVisible(probe));
                    if(recovering)Assert.True(actual.Vision.IsVisible(probe));
                }
                CollectionAssert.AreEqual(domain.CaptureWorldBytes(71,"relay-vision-save"),restored.CaptureWorldBytes(71,"relay-vision-save"));
                Assert.AreEqual(recovering?(PlayableOwner?)PlayableOwner.Player:null,Relay().Owner);
                if(recovering)Assert.AreEqual(1,Relay().Progress);
            }
            finally{restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }
        [Test] public void RelayVisionDefaultMetadataAndSaveBindingAreIndependentOfTankVision()
        {
            Assert.AreEqual(10,profile.RelayVisionRange);Assert.AreEqual(13,profile.TankVisionRange);
            var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="relay.visionRange");Assert.AreEqual(10,field.Read(profile.CopyData()));
            var data=profile.CopyData();data.tankVisionRange+=2;Assert.AreEqual(10,PlayableProfile.Create(data).RelayVisionRange);
            data.relayVisionRange=17;var changed=PlayableProfile.Create(data);Assert.AreEqual(17,changed.RelayVisionRange);Assert.AreEqual(15,changed.TankVisionRange);
            var bytes=domain.CaptureWorldBytes(71,"relay-vision-binding");
            Assert.Throws<ArgumentException>(()=>PlayableDomain.RestoreConfiguredWorldBytes(bytes,changed,71,"relay-vision-binding",Configuration(changed)));
            data.relayVisionRange=0;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
        }
        [Test] public void NativeExpansionPlansFreeRelayMovementAndNeverConstruction()
        {
            var relay=new TerritorySite(4,PlayableBuildingKind.RelayTower,new NavPoint(0,0),Array.Empty<TerritorySlot>());
            var unit=new PlayableEntitySnapshot(13,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(6,0),100,false,0,0,0);
            PlayableAiObservation Observation(PlayableOwner? owner,double progress=0,PlayableOwner? claimant=null)=>PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,71,71,0,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,new[]{unit},Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
                sites:new[]{new TerritorySiteSnapshot(relay,claimant,owner,progress,false,0,owner.HasValue)},
                publicScoutObjectives:new[]{new PlayablePublicScoutObjective(4,new NavPoint(2.5,0),PlayablePublicScoutObjectiveRole.ExpansionIncome,true)},
                routeProofs:new[]{new PlayableRouteProof(13,PlayableOwner.Player,71,0,1,unit.Position,profile.ExplorerCollisionRadius,PlayableRouteTargetKind.PublicObjective,4,new NavPoint(2.5,0),new NavPoint(2.5,0),new[]{new NavPoint(2.5,0)})},activeProfile:profile,ownerId:"player-1",participants:Configuration(profile).Roster.ToArray(),team:1));
            var planner=new AiExpansionPlanner("player-1");var action=planner.TryPlan(Observation(null),profile,AiProfile.Initial);
            Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);Assert.AreEqual(PlayableBuildingKind.RelayTower,action.BuildingKind);Assert.AreEqual(0,TerritoryRules.Cost(profile,action.BuildingKind));
            Assert.False(AiIncomeSiteSelection.Free(Observation(PlayableOwner.Third,1,PlayableOwner.Third).Sites[0],Observation(PlayableOwner.Third,1,PlayableOwner.Third),profile));
            Assert.True(AiIncomeSiteSelection.Free(Observation(PlayableOwner.Enemy,1,PlayableOwner.Enemy).Sites[0],Observation(PlayableOwner.Enemy,1,PlayableOwner.Enemy),profile));
        }
    }
}
