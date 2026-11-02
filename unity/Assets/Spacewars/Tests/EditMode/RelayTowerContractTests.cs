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
        void Capture(double seconds){for(int i=0;i<(int)Math.Ceiling(seconds*30);i++)Call("AdvanceCapture",1d/30);}
        void Remove(int id)=>Call("Damage",id,100000);
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
