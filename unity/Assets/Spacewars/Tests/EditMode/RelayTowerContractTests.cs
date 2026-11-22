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
        public static OfflineMatchConfiguration Configuration(PlayableProfile p,bool ai=false,bool includeMine=false,bool domination=false,bool freeForAll=false)
        {
            var points=new[]{new NavPoint(-16,-14),new NavPoint(16,14),new NavPoint(-16,14)};
            var sites=points.Select((v,i)=>new TerritorySite(i+1,PlayableBuildingKind.Headquarters,v,Array.Empty<TerritorySlot>())).Concat(new[]{new TerritorySite(4,PlayableBuildingKind.RelayTower,new NavPoint(0,0),Array.Empty<TerritorySlot>())}).ToArray();
            if(includeMine)sites=sites.Append(new TerritorySite(5,PlayableBuildingKind.Mine,new NavPoint(0,-7),Array.Empty<TerritorySlot>())).ToArray();
            var starts=points.Select((v,i)=>new OfflineStart("start-"+i,i+1,v,new NavPoint(v.X+5,v.Z),pin:i+1)).ToArray();
            var roster=new[]{new OfflineParticipant("player-1",1,1,ai?OfflineControl.Ai:OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human),new OfflineParticipant("player-3",3,freeForAll?3:1,OfflineControl.Human)};
            var costs=new double[3,3];for(int i=0;i<3;i++)for(int j=0;j<3;j++)costs[i,j]=i==j?0:40;
            return new OfflineMatchConfiguration(p,"relay-contract-v1","relay-contract-flat","authored test geometry",71,roster,starts,sites,Array.Empty<NavObstacle>(),costs,domination:domination);
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
        [Test] public void DominationCountsTeamRelayAndRestoresVictory()
        {
            Cleanup();
            var data=PlayableProfile.Default.CopyData();
            data.dominationTarget=3;
            data.relayCaptureSeconds=1;
            profile=PlayableProfile.Create(data);
            var config=Configuration(profile,domination:true);
            domain=new PlayableDomain(profile,912,config);
            int scout=Spawn(PlayableOwner.Player,new NavPoint(-3,0));
            double income=PlayerView().IncomePerSecond;
            for(int i=0;i<15;i++)domain.Step(1d/30);
            Place(scout,new NavPoint(-10,0));
            Spawn(PlayableOwner.Third,new NavPoint(-3,0));
            for(int i=0;i<45;i++)domain.Step(1d/30);
            var view=PlayerView();
            Assert.That(view.Domination.Value.Score,Is.EqualTo(31d/30).Within(1e-7));
            Assert.That(view.IncomePerSecond,Is.EqualTo(income));
            Assert.Zero(view.RelayTransferRecipients);
            Assert.AreEqual(PlayableOwner.Player,view.MapRelayControl.Single().Owner);
            Assert.That(PlayerView(PlayableOwner.Enemy).MatchEvents.Count(e=>e.Kind==MatchEventKind.DominationGained),Is.EqualTo(1));
            Assert.False(PlayerView(PlayableOwner.Enemy).MatchEvents.Any(e=>e.Kind==MatchEventKind.RelayCaptured));
            var bytes=domain.CaptureWorldBytes(71,"domination-test");
            var restored=PlayableDomain.RestoreConfiguredWorldBytes(bytes,profile,71,"domination-test",config);
            try
            {
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(71,"domination-test"));
                for(int i=0;i<60;i++){domain.Step(1d/30);restored.Step(1d/30);}
                Assert.AreEqual(MatchVictoryReason.Domination,domain.Result.VictoryReason);
                Assert.AreEqual(1,domain.Result.WinnerTeam);
                Assert.AreEqual(domain.Result.Tick,restored.Result.Tick);
            }
            finally {restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }

        [Test] public void WarningFiresOnceAtNinetyOnARisingScore()
        {
            Cleanup();
            var data=PlayableProfile.Default.CopyData();
            data.relayCaptureSeconds=1;
            data.dominationTarget=92;
            profile=PlayableProfile.Create(data);
            domain=new PlayableDomain(profile,912,Configuration(profile,domination:true));
            Spawn(PlayableOwner.Player,new NavPoint(-3,0));
            PlayableMatchEvent[] Warnings()=>PlayerView(PlayableOwner.Enemy).MatchEvents
                .Where(e=>e.Kind==MatchEventKind.DominationNinety).ToArray();
            for(int i=0;i<88;i++)domain.Step(1d/30);
            Assert.IsFalse(PlayerView().Domination.Value.WarningActive);
            Assert.IsEmpty(Warnings());
            for(int i=0;i<3;i++)domain.Step(1d/30);
            Assert.IsTrue(PlayerView().Domination.Value.WarningActive);
            Assert.AreEqual(89,Warnings().Single().Tick);
            for(int i=0;i<60;i++)domain.Step(1d/30);
            Assert.AreEqual(1,Warnings().Length);
        }

        [TestCase(.5,2)]
        [TestCase(10,8)]
        public void LossOfLeadPrecedesDisplacementAndAnnouncementsStayPositional(double decay,double displacement)
        {
            Cleanup();
            var data=PlayableProfile.Default.CopyData();
            data.relayCaptureSeconds=1;
            data.dominationTarget=100;
            data.dominationDecay=decay;
            data.dominationDisplacement=displacement;
            profile=PlayableProfile.Create(data);
            domain=new PlayableDomain(profile,912,Configuration(profile,domination:true));
            int scout=Spawn(PlayableOwner.Player,new NavPoint(-3,0));
            for(int i=0;i<360;i++)domain.Step(1d/30);
            Assert.That(PlayerView().Domination.Value.Score,Is.GreaterThan(10));
            Assert.That(PlayerView(PlayableOwner.Enemy).MatchEvents.Count(e=>e.Kind==MatchEventKind.DominationNinety),Is.EqualTo(1));
            Place(scout,new NavPoint(-10,0));
            Spawn(PlayableOwner.Enemy,new NavPoint(-3,0));
            for(int i=0;i<30;i++)domain.Step(1d/30);
            var interrupted=PlayerView().Domination.Value;
            Assert.Zero(interrupted.LeadingTeam);
            Assert.AreEqual(1,interrupted.ScoringTeam);
            Assert.That(interrupted.Score,Is.GreaterThan(0));
            var lost=PlayerView(PlayableOwner.Enemy).MatchEvents.Last(e=>e.Kind==MatchEventKind.DominationLost);
            Assert.That(lost.Point,Is.EqualTo(new NavPoint(0,0)));
            for(int i=0;i<30;i++)domain.Step(1d/30);
            var takeover=PlayerView().Domination.Value;
            Assert.AreEqual(2,takeover.LeadingTeam);
            Assert.AreEqual(1,takeover.ScoringTeam);
            Assert.That(takeover.Score,Is.EqualTo(interrupted.Score-(29*Math.Min(decay,2)+Math.Min(displacement,2))/30).Within(1e-7));
            Assert.That(PlayerView().MatchEvents.Last(e=>e.Kind==MatchEventKind.DominationGained).Tick,Is.GreaterThan(lost.Tick));
            for(int i=0;i<30;i++)domain.Step(1d/30);
            Assert.That(PlayerView().Domination.Value.Score,Is.EqualTo(takeover.Score-Math.Min(displacement,2)).Within(1e-7));
        }

        [Test] public void EliminatedTeamLosesRelaysImmediatelyAndItsScoreDecaysAcrossRestore()
        {
            Cleanup();
            var data=PlayableProfile.Default.CopyData();
            data.relayCaptureSeconds=1;
            profile=PlayableProfile.Create(data);
            var config=Configuration(profile,domination:true,freeForAll:true);
            domain=new PlayableDomain(profile,912,config);
            Spawn(PlayableOwner.Player,new NavPoint(-3,0));
            for(int i=0;i<360;i++)domain.Step(1d/30);
            var before=PlayerView().Domination.Value;
            Assert.AreEqual(1,before.ScoringTeam);
            Assert.That(before.Score,Is.GreaterThan(10));

            Assert.True(domain.Surrender("player-1"));
            Assert.AreEqual(PlayableMatchOutcome.Playing,domain.Outcome);
            Assert.IsNull(PlayerView(PlayableOwner.Enemy).MapRelayControl.Single().Owner);
            var remaining=PlayerView(PlayableOwner.Enemy).Domination.Value;
            Assert.AreEqual(0,remaining.LeadingTeam);
            Assert.AreEqual(1,remaining.ScoringTeam);
            Assert.AreEqual(before.Score,remaining.Score);
            var bytes=domain.CaptureWorldBytes(71,"domination-test");
            var restored=PlayableDomain.RestoreConfiguredWorldBytes(bytes,profile,71,"domination-test",config);
            try
            {
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(71,"domination-test"));
                for(int i=0;i<30;i++)
                {
                    domain.Step(1d/30);
                    restored.Step(1d/30);
                }
                Assert.That(PlayerView(PlayableOwner.Enemy).Domination.Value.Score,Is.EqualTo(before.Score-.5).Within(1e-7));
                CollectionAssert.AreEqual(domain.CaptureWorldBytes(71,"domination-test"),restored.CaptureWorldBytes(71,"domination-test"));
            }
            finally {restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }

        [Test] public void LoweringVictoryTargetDoesNotReduceExistingScore()
        {
            Cleanup();
            var data=PlayableProfile.Default.CopyData();
            data.relayCaptureSeconds=1;
            profile=PlayableProfile.Create(data);
            var config=Configuration(profile,domination:true);
            domain=new PlayableDomain(profile,912,config);
            Spawn(PlayableOwner.Player,new NavPoint(-3,0));
            for(int i=0;i<360;i++)domain.Step(1d/30);
            var score=PlayerView().Domination.Value.Score;
            Assert.That(score,Is.GreaterThan(10));

            data.revision++;
            data.dominationTarget=2;
            var lower=PlayableProfile.Create(data);
            Assert.IsNull(domain.ValidateAuthorityBalance(lower,true));
            domain.ApplyBalance(lower);
            domain.Step(1d/30);
            Assert.That(PlayerView().Domination.Value.Score,Is.EqualTo(score));
            Assert.AreEqual(MatchVictoryReason.Domination,domain.Result.VictoryReason);
            Assert.AreEqual(1,domain.Result.WinnerTeam);
        }

        [Test] public void SurrenderReportsTheLastOpposingTeamAndPreservesAlliedRelay()
        {
            Cleanup();
            domain=new PlayableDomain(profile,912,Configuration(profile,domination:true));
            Spawn(PlayableOwner.Third,new NavPoint(-3,0));
            for(int i=0;i<165;i++)domain.Step(1d/30);
            domain.Surrender("player-1");
            Assert.AreEqual(PlayableMatchOutcome.Playing,domain.Outcome);
            Assert.AreEqual(PlayableOwner.Player,PlayerView(PlayableOwner.Third).MapRelayControl.Single().Owner);
            domain.Surrender("player-2");
            Assert.AreEqual(1,domain.Result.WinnerTeam);
            Assert.AreEqual(MatchVictoryReason.Surrender,domain.Result.VictoryReason);
            var bytes=domain.CaptureWorldBytes(71,"domination-surrender");
            var restored=PlayableDomain.RestoreConfiguredWorldBytes(bytes,profile,71,"domination-surrender",Configuration(profile,domination:true));
            try{Assert.AreEqual(MatchVictoryReason.Surrender,restored.Result.VictoryReason);}
            finally{restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }
        void KnowledgeScenario()
        {
            Cleanup();var data=PlayableProfile.Default.CopyData();data.headquartersVisionRange=6;data.explorerVision=8;
            profile=PlayableProfile.Create(data);domain=new PlayableDomain(profile,912,Configuration(profile));
        }
        PlayableSnapshot PlayerView(PlayableOwner owner=PlayableOwner.Player)=>domain.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,71,owner);
        [Test] public void AlliedBoostUsesTripleReserveAndPaysOnlyTheMineOwner()
        {
            Cleanup();
            var data=PlayableProfile.Default.CopyData();
            data.mineBuildSeconds=1;
            data.mineReserveCredits=10;
            data.mineDepletedIncomePercent=20;
            profile=PlayableProfile.Create(data);
            domain=new PlayableDomain(profile,912,Configuration(profile,includeMine:true));
            Spawn(PlayableOwner.Player,new NavPoint(-2,-7));
            Capture(profile.MineCaptureSeconds+.1);
            Call("AddCredits",PlayableOwner.Player,1000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(912,1,"player-1",
                PlayableCommandKind.BuildAt,null,siteId:5,buildingKind:PlayableBuildingKind.Mine),out _));
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            var mine=PlayerView().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Mine);
            var site=((System.Collections.IDictionary)type.GetField("sites",F).GetValue(domain))[5];
            var reserve=site.GetType().GetField("MineReserve");
            double seconds=profile.IncomeSettlementSeconds;
            double work=profile.MineIncomePerPeriod/profile.IncomePeriodSeconds*seconds;
            reserve.SetValue(site,work*2);
            var ally=PlayableOwner.Third;
            Call("CreditOrbital",(PlayableOwner?)ally,PlayableOwner.Enemy,data.orbitalThreshold1+data.orbitalThreshold2+data.orbitalThreshold3);
            foreach(int id in new[]{1,2,OrbitalAbilities.AcceleratedProduction})
                Assert.AreEqual(PlayableCommandStatus.Applied,Call("ChooseOrbital",ally,id));
            Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(912,1,"player-3",
                PlayableCommandKind.UseOrbital,new[]{mine.Id},targetId:OrbitalAbilities.AcceleratedProduction),out _));
            double owner=(double)Call("Balance",PlayableOwner.Player), allyBalance=(double)Call("Balance",ally);
            type.GetField("elapsed",F).SetValue(domain,seconds);
            Call("SettleIncome",PlayableOwner.Player);
            Assert.AreEqual(0,PlayerView().Sites.Single(s=>s.Site.Id==5).MineReserve);
            double hq=TerritoryRules.Income(profile,PlayableBuildingKind.Headquarters)/profile.IncomePeriodSeconds*seconds;
            Assert.AreEqual(hq+work*2.2,(double)Call("Balance",PlayableOwner.Player)-owner,1e-7);
            Assert.AreEqual(allyBalance,Call("Balance",ally));
        }
        [TestCase(1d)][TestCase(1.5d)] public void MineReservePersistsAcrossDepletionRestoreAndRebuild(double incomeMultiplier)
        {
            Cleanup();
            var data=PlayableProfile.Default.CopyData();
            data.mineReserveCredits=2;
            data.mineBuildSeconds=1;
            profile=PlayableProfile.Create(data);
            var config=Configuration(profile,includeMine:true);
            domain=new PlayableDomain(profile,912,config);
            double OwnerMultiplier(string id)=>id=="player-1"?incomeMultiplier:1d;
            domain.ConfigureAiIncome(OwnerMultiplier);
            int scout=Spawn(PlayableOwner.Player,new NavPoint(-2,-7));
            int hostileScout=Spawn(PlayableOwner.Enemy,new NavPoint(5,-7));
            Capture(profile.MineCaptureSeconds+.1);
            TerritorySiteSnapshot Mine(PlayableOwner owner=PlayableOwner.Player)=>PlayerView(owner).Sites.Single(s=>s.Site.Id==5);
            Assert.AreEqual(2,Mine(PlayableOwner.Enemy).MineReserve,"Unoccupied, currently visible");
            Place(scout,new NavPoint(-5,-7));
            long sequence=0;
            PlayableCommandStatus Build()=>domain.Apply(new PlayableCommand(912,++sequence,"player-1",
                PlayableCommandKind.BuildAt,null,siteId:5,buildingKind:PlayableBuildingKind.Mine),out _);
            void Frames(int count){for(int i=0;i<count;i++)domain.Step(1d/30);}
            Call("AddCredits",PlayableOwner.Player,1000d);
            double otherIncome=PlayerView().IncomePerSecond;
            Assert.AreEqual(PlayableCommandStatus.Applied,Build());
            Frames(36);
            Assert.That(Mine().MineReserve,Is.GreaterThan(0).And.LessThan(2));
            Assert.That(Mine(PlayableOwner.Third).MineReserve,Is.EqualTo(Mine().MineReserve));
            Assert.IsNull(Mine(PlayableOwner.Enemy).MineReserve);
            Assert.False(PlayerView().MatchEvents.Any(e=>e.Kind==MatchEventKind.MineDepleted));
            var bytes=domain.CaptureWorldBytes(71,"mine-reserve");
            var restored=PlayableDomain.RestoreConfiguredWorldBytes(bytes,profile,71,"mine-reserve",config);
            restored.ConfigureAiIncome(OwnerMultiplier);
            try
            {
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(71,"mine-reserve"));
                for(int i=0;i<30;i++){domain.Step(1d/30);restored.Step(1d/30);}
                CollectionAssert.AreEqual(domain.CaptureWorldBytes(71,"mine-reserve"),restored.CaptureWorldBytes(71,"mine-reserve"));
                Assert.AreEqual(0,Mine().MineReserve);
                Assert.IsTrue(Mine(PlayableOwner.Enemy).MineDepleted);
                var depleted=PlayerView().MatchEvents.Single(e=>e.Kind==MatchEventKind.MineDepleted);
                Assert.AreEqual(Mine().Site.Position,depleted.Point);
                Assert.False(PlayerView(PlayableOwner.Enemy).MatchEvents.Any(e=>e.Kind==MatchEventKind.MineDepleted));
                Assert.False(PlayerView(PlayableOwner.Third).MatchEvents.Any(e=>e.Kind==MatchEventKind.MineDepleted));
                var resumed=restored.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,71,PlayableOwner.Player);
                Assert.AreEqual(depleted.Tick,resumed.MatchEvents.Single(e=>e.Kind==MatchEventKind.MineDepleted).Tick);
                var notifications=new NativeMatchNotifications();
                notifications.Update(PlayerView().WithMatchEvents(new[]{depleted}),false,0,profile);
                Assert.AreEqual("Шахта истощена",notifications.Latest.Text);
                Assert.AreEqual("system-mine-depleted",notifications.Latest.VoiceKey);
                Assert.AreEqual(depleted.Point,notifications.Focus(false,0,profile).Value);
                double depletedRate=profile.MineIncomePerPeriod/profile.IncomePeriodSeconds*.2*incomeMultiplier;
                Assert.That(PlayerView().IncomePerSecond,Is.EqualTo(otherIncome+depletedRate).Within(1e-8));
                double before=PlayerView().SettledIncome;
                Frames(30);
                Assert.That(PlayerView().SettledIncome-before,Is.EqualTo(otherIncome+depletedRate).Within(1e-8));
                Remove(Mine().CenterId);
                Assert.AreEqual(0,Mine(PlayableOwner.Enemy).MineReserve);
                Place(hostileScout,new NavPoint(7,-7));
                Place(scout,new NavPoint(-2,-7));
                Frames((int)Math.Ceiling((profile.BuildingDestructionSeconds+profile.MineCaptureSeconds+.2)*30));
                Assert.AreEqual(PlayableCommandStatus.Applied,Build());
                Place(scout,new NavPoint(-5,-7));
                Frames(36);
                Assert.IsTrue(Mine().Ready);
                Assert.IsTrue(Mine().MineDepleted);
                Assert.False(PlayerView().MatchEvents.Any(e=>e.Kind==MatchEventKind.MineDepleted&&e.Id>depleted.Id));
            }
            finally {restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }

        [Test] public void MineYieldProratesTheFinalSettlementAndLabFieldsAreEditable()
        {
            var data=PlayableProfile.Default.CopyData();
            data.mineIncomePerPeriod=15;
            var candidate=PlayableProfile.Create(data);
            Assert.That(TerritoryRules.MineIncome(candidate,.15,.1),Is.EqualTo(.18).Within(1e-10));
            Assert.That(TerritoryRules.MineIncome(candidate,0,.1),Is.EqualTo(.06).Within(1e-10));
            Assert.That(TerritoryRules.MineIncome(candidate,null,.1),Is.EqualTo(.3).Within(1e-10));
            foreach(string path in new[]{"territory.mineReserveCredits","territory.mineDepletedIncomePercent","science.refineryUpgradeCost","science.refineryUpgradedIncome"})
            {
                var field=PlayableProfileMetadata.Fields.Single(f=>f.Path==path);
                Assert.IsTrue(NativeBalanceFields.LabEditable(field),path);
            }
        }

        [Test] public void RelayTransferRevalidatesThreatAndPreservesCurrentContinuation()
        {
            int ownerUnit=Spawn(PlayableOwner.Player,new NavPoint(2.5,0));
            Capture(6);
            Call("EmitIncomeMarkers",PlayableOwner.Player);
            double relayRate=profile.RelayIncomePerPeriod/profile.IncomePeriodSeconds;
            double ownerRate=PlayerView().IncomePerSecond,allyRate=PlayerView(PlayableOwner.Third).IncomePerSecond;
            long sequence=0;
            PlayableCommandStatus Send(string owner,int recipient)=>domain.Apply(new PlayableCommand(912,++sequence,
                owner,PlayableCommandKind.TransferRelay,null,siteId:4,targetId:recipient),out _);
            Assert.AreEqual(1<<(int)PlayableOwner.Third,PlayerView().RelayTransferRecipients);
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send("player-1",1),"Hostile recipient");
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send("player-1",0),"Self recipient");
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send("player-3",0),"Ally cannot take control");
            Place(ownerUnit,new NavPoint(12,0));
            int defender=Spawn(PlayableOwner.Third,new NavPoint(2.5,0));
            int enemy=Spawn(PlayableOwner.Enemy,new NavPoint(-2.5,0));
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send("player-1",2),"Live presence invalidates a previously built menu");
            Capture(1);
            Assert.True(Relay().Contested);
            Assert.AreEqual(1,Relay().Progress,"Only a teammate is needed to stop neutralization");
            Place(defender,new NavPoint(0,12));Capture(1);
            Assert.Less(Relay().Progress,1);
            Place(enemy,new NavPoint(-12,0));
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send("player-1",2),"Recover full control before passing it");
            Capture(1.1);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send("player-1",2));
            Assert.AreEqual(PlayableOwner.Third,Relay().Claimant);
            Assert.AreEqual(PlayableOwner.Third,PlayerView().Vision.KnownRelays.Single().Owner);
            Assert.AreEqual(PlayableOwner.Third,PlayableMapView.Marks(PlayerView()).Single(m=>m.Kind==PlayableBuildingKind.RelayTower).Owner);
            Assert.That(PlayerView().IncomePerSecond,Is.EqualTo(ownerRate-relayRate).Within(1e-8));
            Assert.That(PlayerView(PlayableOwner.Third).IncomePerSecond,Is.EqualTo(allyRate+relayRate).Within(1e-8));
            Assert.False(PlayerView().IncomeEvents.Any(e=>e.Kind==PlayableBuildingKind.RelayTower));
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send("player-1",2),"Old owner cannot replay the action");

            var bytes=domain.CaptureWorldBytes(71,"relay-transfer");
            var restored=PlayableDomain.RestoreConfiguredWorldBytes(bytes,profile,71,"relay-transfer",Configuration(profile));
            try
            {
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(71,"relay-transfer"));
                double donorIncome=PlayerView().SettledIncome,recipientIncome=PlayerView(PlayableOwner.Third).SettledIncome;
                for(int i=0;i<30*profile.IncomePeriodSeconds;i++){domain.Step(1d/30);restored.Step(1d/30);}
                CollectionAssert.AreEqual(domain.CaptureWorldBytes(71,"relay-transfer"),restored.CaptureWorldBytes(71,"relay-transfer"));
                Assert.That(PlayerView().SettledIncome-donorIncome,Is.EqualTo((ownerRate-relayRate)*profile.IncomePeriodSeconds).Within(1e-8));
                Assert.That(PlayerView(PlayableOwner.Third).SettledIncome-recipientIncome,Is.EqualTo((allyRate+relayRate)*profile.IncomePeriodSeconds).Within(1e-8));
                Assert.IsTrue(PlayerView(PlayableOwner.Third).IncomeEvents.Any(e=>e.Kind==PlayableBuildingKind.RelayTower));
                domain.FinishManually();
                var outgoing=domain.Result.Facts.Single(f=>f.Kind==MatchFactKind.RelayTransferredOut);
                var incoming=domain.Result.Facts.Single(f=>f.Kind==MatchFactKind.RelayTransferredIn);
                Assert.AreEqual(PlayableOwner.Player,outgoing.Owner);
                Assert.AreEqual(PlayableOwner.Third,incoming.Owner);
                var graph=typeof(PostMatchResultsView).GetMethod("GraphEvent",F);
                var ui=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(PostMatchResultsView));
                Assert.False((bool)graph.Invoke(ui,new object[]{0,outgoing}));
                Assert.False((bool)graph.Invoke(ui,new object[]{0,incoming}));
            }
            finally{restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }

        [TestCase(false)][TestCase(true)]
        public void NonDefaultTransferGapRoundTripsVersionedProfileTerms(bool independentLabStats)
        {
            Cleanup();
            var data=PlayableProfile.Default.CopyData();
            if(independentLabStats)PlayableProfileMetadata.ProjectIndependentLabStats(data);
            data.relayTransferIncomeGap=6.5;
            profile=PlayableProfile.Create(data);
            var configuration=Configuration(profile);
            domain=new PlayableDomain(profile,912,configuration);
            var bytes=domain.CaptureWorldBytes(71,"custom-relay-gap");
            var restored=PlayableDomain.RestoreConfiguredWorldBytes(bytes,profile,71,"custom-relay-gap",configuration);
            try
            {
                Assert.AreEqual(6.5,restored.RoutingProfile.RelayTransferIncomeGap);
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(71,"custom-relay-gap"));
            }
            finally{restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }

        [TestCase(25,OfflineControl.Human,AiDifficulty.Recruit,false)]
        [TestCase(30,OfflineControl.Ai,AiDifficulty.Recruit,false)]
        [TestCase(30,OfflineControl.Ai,AiDifficulty.Fighter,true)]
        [TestCase(30,OfflineControl.Human,AiDifficulty.Recruit,true)]
        public void AiShareUsesStrictIncomeGapAndDifficulty(double relayIncome,OfflineControl allyControl,
            AiDifficulty allyDifficulty,bool expected)
        {
            Cleanup();
            var data=PlayableProfile.Default.CopyData();
            data.relayIncomePerPeriod=relayIncome;
            profile=PlayableProfile.Create(data);
            var original=Configuration(profile,ai:true);
            var roster=original.Roster.ToArray();
            roster[2]=new OfflineParticipant("player-3",3,1,allyControl);
            var costs=new double[,]{{0,40,40},{40,0,40},{40,40,0}};
            var config=new OfflineMatchConfiguration(profile,original.SourceIdentity,original.MapIdentity,original.RouteProvenance,
                original.Seed,roster,original.Starts.ToArray(),original.Sites.ToArray(),original.Obstacles.ToArray(),costs);
            domain=new PlayableDomain(profile,912,config);
            Spawn(PlayableOwner.Player,new NavPoint(2.5,0));Capture(6);
            var donor=new PlayableAiOwnerLoop(profile,PlayableAiOpeningComposition.Initialize(71,"player-1"),912);
            var ally=new PlayableAiOwnerLoop(profile,PlayableAiOpeningComposition.Initialize(71,"player-3"),912,null,AiProfile.Initial,allyDifficulty);
            domain.ShareAiRelays(donor,allyControl==OfflineControl.Ai?new[]{donor,ally}:new[]{donor});
            Assert.AreEqual(expected?PlayableOwner.Third:PlayableOwner.Player,Relay().Owner);
        }

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
        [Test] public void PublicRelayMarkerShowsUnseenControlWithoutGrantingAiLiveSites()
        {
            KnowledgeScenario();
            var neutral=PlayableMapView.Marks(PlayerView()).Single(m=>m.Kind==PlayableBuildingKind.RelayTower);
            Assert.IsNull(neutral.Owner);Assert.AreEqual(MapMarkState.Empty,neutral.State);
            int enemy=Spawn(PlayableOwner.Enemy,new NavPoint(2.5,0));Capture(6);
            var view=PlayerView();Assert.IsFalse(view.Vision.IsVisible(default));
            Assert.AreEqual(Configuration(profile).Sites.Count,view.PublicSites.Count);
            Assert.False(view.Sites.Any(s=>s.Site.Id==4),"AI and commands still receive only current sight");
            var mark=PlayableMapView.Marks(view).Single(m=>m.Kind==PlayableBuildingKind.RelayTower);
            Assert.AreEqual(PlayableOwner.Enemy,mark.Owner);Assert.AreEqual(MapMarkState.Ready,mark.State);
            Assert.False(view.Entities.Any(e=>e.Id==enemy),"Map control does not reveal the capturing unit");
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
            Assert.AreEqual(PlayableOwner.Enemy,PlayableMapView.Marks(hidden).Single(m=>m.Kind==PlayableBuildingKind.RelayTower).Owner);
            var bytes=domain.CaptureWorldBytes(71,"relay-memory");
            var restored=PlayableDomain.RestoreConfiguredWorldBytes(bytes,profile,71,"relay-memory",Configuration(profile));
            try
            {
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(71,"relay-memory"));
                var restoredView=restored.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,71);
                Assert.IsNull(restoredView.Vision.KnownRelays.Single().Owner);
                Assert.AreEqual(PlayableOwner.Enemy,PlayableMapView.Marks(restoredView).Single(m=>m.Kind==PlayableBuildingKind.RelayTower).Owner);
            }
            finally{restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
            Spawn(PlayableOwner.Player,new NavPoint(6,0));
            var revisited=PlayerView();Assert.AreEqual(PlayableOwner.Enemy,revisited.Vision.KnownRelays.Single().Owner);
            Assert.AreEqual(PlayableOwner.Player,seen.Vision.KnownRelays.Single().Owner,"Published knowledge is immutable");
        }
        [Test] public void LongerRelayCaptureAndNeutralizationUseTheSameProfileDuration()
        {
            const double duration=15, tick=1d/30;
            const double half=duration/2;
            Cleanup();
            var data=profile.CopyData();
            data.relayCaptureSeconds=duration;
            profile=PlayableProfile.Create(data);
            domain=new PlayableDomain(profile,912,Configuration(profile));
            int actor=Spawn(PlayableOwner.Player,new NavPoint(-2.5,0));

            Capture(half);
            Assert.IsNull(Relay().Owner);
            Assert.That(Relay().Progress,Is.EqualTo(.5).Within(1e-9));
            Capture(half-tick);
            Assert.IsNull(Relay().Owner,"Control is not awarded a tick early.");
            Capture(2*tick);
            Assert.AreEqual(PlayableOwner.Player,Relay().Owner,"Capture completes within one tick of its configured duration.");

            Remove(actor);
            Spawn(PlayableOwner.Enemy,new NavPoint(2.5,0));
            Capture(half);
            Assert.AreEqual(PlayableOwner.Player,Relay().Owner);
            Assert.That(Relay().Progress,Is.EqualTo(.5).Within(1e-9));
            Capture(half-tick);
            Assert.AreEqual(PlayableOwner.Player,Relay().Owner);
            Capture(tick);
            Assert.IsNull(Relay().Owner,"Full neutralization precedes the new capture phase.");
            Capture(duration+tick);
            Assert.AreEqual(PlayableOwner.Enemy,Relay().Owner);
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
            foreach(var fact in result.Facts.Where(f=>f.Kind==MatchFactKind.RelayCaptured))
                Assert.True((bool)graph.Invoke(ui,new object[]{0,fact}));
            var neutralization=result.Facts.Single(f=>f.Kind==MatchFactKind.RelayNeutralized);
            Assert.True((bool)graph.Invoke(ui,new object[]{0,neutralization}),"The income graph receives neutralization facts for player filtering.");
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
