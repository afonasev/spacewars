using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeBalanceApplyTests
    {
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static object Call(object d,string method,params object[] args)=>Domain.GetMethod(method,F).Invoke(d,args);
        private static object New(PlayableProfile p)=>Activator.CreateInstance(Domain,F,null,new object[]{p,1L,false},null);
        private static PlayableSnapshot View(object d,PlayableOwner owner=PlayableOwner.Player)=>(PlayableSnapshot)Call(d,"PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        private static void Deliver(object d)
        {
            var nav=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(d);
            while(nav.Requests.TryDequeue(out var request))Assert.True(nav.Answers.TryEnqueue(new NavigationAnswer(request,new SharedFlowRouter(request.Geometry,request.Profile).FindPath(request.Start,request.Goal))));
        }
        private static int Build(object d,PlayableBuildingKind kind,int slot)
        {
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"BuildAt",1,slot,kind,1,PlayableOwner.Player,null));
            int id=View(d).Buildings.Single(b=>b.Kind==kind&&b.SlotId==slot).Id;
            for(int i=0;i<1800&&View(d).Buildings.Single(b=>b.Id==id).Phase!=ConstructionPhase.Ready;i++){Call(d,"AdvanceFoundations");Call(d,"AdvanceBuildings",100d);Deliver(d);Call(d,"Step",1d/30);}
            Assert.AreEqual(ConstructionPhase.Ready,View(d).Buildings.Single(b=>b.Id==id).Phase);return id;
        }
        private static void Send(object d,long sequence,PlayableCommandKind kind,int id)
            =>Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"Apply",new PlayableCommand(1,sequence,"player-1",kind,new[]{id}),null));
        private static object Restore(object d,PlayableProfile p)
            =>Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{(byte[])Call(d,"CaptureWorldBytes",7,"lab"),p,7,"lab"});
        private static void CloseCorridorHistoryDomain(object d)
        {
            if(d==null)return;
            var nav=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(d);var budget=nav.PhysicalHostBudget;
            nav.ReleaseCommittedHostRoot();Call(d,"DestroyOwnedNavigation");
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        private static void CheckCorridorHistoryRoundtrip(object d,PlayableProfile p,IDictionary<int,int> expected)
        {
            byte[] bytes=(byte[])Call(d,"CaptureWorldBytes",7,"lab");object restored=null;
            try{
                restored=Restore(d,p);
                CollectionAssert.AreEqual(bytes,(byte[])Call(restored,"CaptureWorldBytes",7,"lab"));
                var terms=(IDictionary<int,PlayableProfile>)Domain.GetField("termProfiles",F).GetValue(restored);
                CollectionAssert.AreEquivalent(expected.Keys,terms.Keys);
                foreach(var term in expected)Assert.AreEqual(term.Value,terms[term.Key].Navigation.GroupCorridorMaximumExcessPercent,"policy for paid term "+term.Key);
                Call(d,"AdvanceProduction",.25d);Call(restored,"AdvanceProduction",.25d);
                CollectionAssert.AreEqual((byte[])Call(d,"CaptureWorldBytes",7,"lab"),(byte[])Call(restored,"CaptureWorldBytes",7,"lab"),"same current continuation");
            }finally{CloseCorridorHistoryDomain(restored);}
        }
        [TestCase(0)][TestCase(15)]
        public void CurrentSingleCorridorTermRestoresExactly(int policy)
        {
            var data=PlayableProfile.Default.CopyData();data.groupCorridorMaximumExcessPercent=policy;var p=PlayableProfile.Create(data);object d=null;
            try{d=New(p);CheckCorridorHistoryRoundtrip(d,p,new Dictionary<int,int>{{p.Revision,policy}});}
            finally{CloseCorridorHistoryDomain(d);}
        }
        [TestCase(0,15)][TestCase(15,0)]
        public void CurrentPaidCorridorHistoryRestoresAcrossBothPolicyTransitions(int first,int second)
        {
            var data=PlayableProfile.Default.CopyData();data.groupCorridorMaximumExcessPercent=first;var p=PlayableProfile.Create(data);object d=null;
            try{
                d=New(p);Call(d,"AddCredits",PlayableOwner.Player,10000d);
                Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
                Call(d,"AdvanceFoundations");Call(d,"AdvanceBuildings",(double)p.FactoryBuildSeconds+1);
                int factory=View(d).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Id;
                Send(d,1,PlayableCommandKind.QueueTank,factory);
                var paid=View(d).Buildings.Single(b=>b.Id==factory).PrivateState.Orders[0];
                var history=new Dictionary<int,int>{{p.Revision,first}};
                CheckCorridorHistoryRoundtrip(d,p,history);
                foreach(int policy in new[]{second,first}){
                    data=p.CopyData();data.revision++;data.groupCorridorMaximumExcessPercent=policy;data.tankCreditCost+=10;data.tankProductionSeconds+=1;
                    var next=PlayableProfile.Create(data);Assert.IsNull(Call(d,"ValidateBalance",next));Call(d,"ApplyBalance",next);p=next;history.Add(p.Revision,policy);
                    CheckCorridorHistoryRoundtrip(d,p,history);
                    var old=View(d).Buildings.Single(b=>b.Id==factory).PrivateState.Orders[0];
                    Assert.AreEqual(paid.PaidCost,old.PaidCost);Assert.AreEqual(paid.Duration,old.Duration);
                }
                Send(d,2,PlayableCommandKind.QueueTank,factory);
                var fresh=View(d).Buildings.Single(b=>b.Id==factory).PrivateState.Orders[1];
                Assert.AreEqual(p.TankCreditCost,fresh.PaidCost);Assert.AreEqual(p.TankProductionSeconds,fresh.Duration);
                CheckCorridorHistoryRoundtrip(d,p,history);
            }finally{CloseCorridorHistoryDomain(d);}
        }
        [Test] public void CorridorTermDecodeDoesNotExpandPolicyOrLabRange()
        {
            var data=PlayableProfile.Default.CopyData();data.groupCorridorMaximumExcessPercent=4;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
            var field=PlayableProfileMetadata.Fields.Single(f=>f.Path==NavigationProfile.GroupCorridorMetadata.Path);
            Assert.AreEqual(15,field.Minimum);Assert.AreEqual(15,field.Maximum);Assert.AreEqual(1,field.Step);
            data.groupCorridorMaximumExcessPercent=0;field.Write(data,0);field.Write(data,15);
            Assert.Throws<ArgumentOutOfRangeException>(()=>field.Write(data,0));Assert.Throws<ArgumentOutOfRangeException>(()=>field.Write(data,4));
        }
        [Test]public void WebImpactRadiusIsPresentationMetadataWithHardBounds()
        {
            var data=PlayableProfile.Default.CopyData();Assert.That(PlayableProfile.Create(data).ImpactEffectRadius,Is.EqualTo(.22));
            data.impactEffectRadius=.5;Assert.That(PlayableProfile.Create(data).ImpactEffectRadius,Is.EqualTo(.5));
            data.impactEffectRadius=.01;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
            data.impactEffectRadius=1.01;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
        }
        [Test] public void ExpandedTransportRebindPreservesAll257IntentsAndRejectsOldAnswer()
        {
            var session=new NavigationSession(1,new NavGeometry(100,new NavObstacle[0],1),PlayableProfile.Default.Navigation);
            NavigationRequest inFlight=null;
            for(int id=1;id<=257;id++){session.Crowd.Add(id,new NavPoint(3*(id%20),3*(id/20)));Assert.True(session.Move(id,new NavPoint(50,50)));if(id==1)Assert.True(session.Requests.TryDequeue(out inFlight));}
            Assert.True(session.CanRebind);session.Rebind(PlayableProfile.Default.Navigation);
            Assert.AreEqual(257,session.PendingCount);for(int id=1;id<=257;id++)Assert.True(session.IsPending(id));
            session.Answers.TryEnqueue(new NavigationAnswer(inFlight,new[]{inFlight.Goal}));session.ApplyResults();
            Assert.AreEqual(1,session.RejectedResults);Assert.AreEqual(257,session.PendingCount);
        }
        [Test] public void CapacityRebindCountsActiveOrdersAndBlocksWaitingOrders()
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var d=New(p);foreach(var enemy in View(d,PlayableOwner.Enemy).Entities.Where(u=>u.Owner==PlayableOwner.Enemy).Skip(1))Call(d,"Damage",enemy.Id,100000);Call(d,"AddCredits",PlayableOwner.Player,10000d);int factory=Build(d,PlayableBuildingKind.Factory,1);
            Send(d,1,PlayableCommandKind.QueueTank,factory);Send(d,2,PlayableCommandKind.QueueTank,factory);Call(d,"AdvanceProduction",.01d);
            int tankCount=View(d).Entities.Count(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Tank);
            var data=p.CopyData();data.revision=2;data.tankPopulationCost+=1;
            int living=View(d).Entities.Where(u=>u.Owner==PlayableOwner.Player).Sum(u=>u.Kind==PlayableEntityKind.Tank?data.tankPopulationCost:PlayableUnitRules.Population(p,u.Kind));
            data.armyCapacity=living+data.tankPopulationCost-1;Assert.IsNotNull(Call(d,"ValidateBalance",PlayableProfile.Create(data,p.AuthoredMap)));
            data.armyCapacity++;var q=PlayableProfile.Create(data,p.AuthoredMap);Assert.IsNull(Call(d,"ValidateBalance",q));Call(d,"ApplyBalance",q);
            var restored=Restore(d,q);var population=(PlayablePopulationSnapshot)Call(restored,"Population",PlayableOwner.Player);Assert.AreEqual(q.ArmyCapacity,population.Living+population.Reserved);
            for(int i=0;i<1800&&View(restored).Buildings.Single(b=>b.Id==factory).PrivateState.Orders.Count==2;i++){Deliver(restored);Call(restored,"Step",1d/30);}
            var orders=View(restored).Buildings.Single(b=>b.Id==factory).PrivateState.Orders;Assert.AreEqual(1,orders.Count);Assert.False(orders[0].Active);
            population=(PlayablePopulationSnapshot)Call(restored,"Population",PlayableOwner.Player);Assert.AreEqual(q.ArmyCapacity,population.Living);Assert.Zero(population.Reserved);
        }
        [Test] public void RepairSaleAndUpgradeKeepTermsThroughTwoRevisionsAndRestore()
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var d=New(p);Call(d,"AddCredits",PlayableOwner.Player,100000d);
            int repair=Build(d,PlayableBuildingKind.Factory,1),sale=Build(d,PlayableBuildingKind.Factory,2),refinery=Build(d,PlayableBuildingKind.Refinery,3);Build(d,PlayableBuildingKind.ScientificCenter,4);
            Call(d,"Damage",repair,17);for(int i=0;i<(p.BuildingRepairCombatLockoutSec+1)*30;i++){Deliver(d);Call(d,"Step",1d/30);}
            Send(d,1,PlayableCommandKind.StartBuildingRepair,repair);Send(d,2,PlayableCommandKind.SellBuilding,sale);Send(d,3,PlayableCommandKind.UpgradeRefinery,refinery);
            double oldDuration=View(d).Buildings.Single(b=>b.Id==repair).PrivateState.Lifecycle.RepairDuration;
            var data=p.CopyData();data.revision=2;data.factoryHealth+=100;data.buildingRepairDurationSec+=10;data.buildingSaleDemolitionSec+=1;data.refineryUpgradeSeconds+=10;
            var q=PlayableProfile.Create(data,p.AuthoredMap);Assert.IsNull(Call(d,"ValidateBalance",q));Call(d,"ApplyBalance",q);d=Restore(d,q);
            Assert.AreEqual(oldDuration,View(d).Buildings.Single(b=>b.Id==repair).PrivateState.Lifecycle.RepairDuration);
            data=q.CopyData();data.revision=3;data.factoryHealth+=100;var r=PlayableProfile.Create(data,p.AuthoredMap);Assert.IsNull(Call(d,"ValidateBalance",r));Call(d,"ApplyBalance",r);d=Restore(d,r);
            Call(d,"AdvanceBuildingLifecycle",Math.Ceiling(Math.Max(oldDuration,p.BuildingSaleDemolitionSec))+1);
            Assert.False(View(d).Buildings.Any(b=>b.Id==sale));Assert.AreEqual(p.FactoryHealth,View(d).Buildings.Single(b=>b.Id==repair).Health,1e-6,"An old repair restores only its purchased health, never the larger new maximum.");
            Restore(d,r);
        }
        [Test] public void FieldsHaveWritersAndExcludeMapTopology()
        {
            var data=PlayableProfile.Default.CopyData();
            foreach(var field in PlayableProfileMetadata.Fields){Assert.NotNull(field.FieldName,field.Path);field.Write(data,field.Read(data));}
            Assert.False(PlayableProfileMetadata.Fields.Where(NativeBalanceFields.Editable).Any(f=>f.Group=="Arena"||f.Group=="Slots"||f.FieldName.Contains("Footprint")||f.FieldName.Contains("CollisionRadius")));
            PlayableProfile.Validate(data);
            var offset=PlayableProfileMetadata.Fields.Single(f=>f.Path=="camera.offsetX");
            offset.Write(data,-17);Assert.AreEqual(-17,PlayableProfile.Create(data).CameraOffsetX);
            Assert.Throws<ArgumentOutOfRangeException>(()=>offset.Write(data,101));
        }
        [Test] public void LiveApplyKeepsPaidProductionAndBuildTermsAcrossSaveRestore()
        {
            var p=PlayableProfile.Default;var d=New(p);Call(d,"AddCredits",PlayableOwner.Player,10000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
            Call(d,"AdvanceFoundations");Call(d,"AdvanceBuildings",(double)p.FactoryBuildSeconds+1);
            int factory=View(d).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Id;
            Call(d,"Apply",new PlayableCommand(1,1,"player-1",PlayableCommandKind.QueueTank,new[]{factory}),null);
            var paid=View(d).Buildings.Single(b=>b.Id==factory).PrivateState.Orders[0];
            var data=p.CopyData();data.revision=2;data.tankCreditCost+=10;data.tankProductionSeconds+=1;data.factoryBuildSeconds+=1;data.tankSpeed+=1;
            var next=PlayableProfile.Create(data);Assert.IsNull(Call(d,"ValidateBalance",next));Call(d,"ApplyBalance",next);
            var after=View(d);Assert.AreSame(next,after.ActiveProfile);Assert.AreEqual(0,after.Tick);Assert.AreEqual(paid.PaidCost,after.Buildings.Single(b=>b.Id==factory).PrivateState.Orders[0].PaidCost);
            Assert.AreEqual(paid.Duration,after.Buildings.Single(b=>b.Id==factory).PrivateState.Orders[0].Duration);
            byte[] bytes=(byte[])Call(d,"CaptureWorldBytes",7,"native-lab-test");
            var restored=Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,next,7,"native-lab-test"});
            Assert.AreEqual(after.Credits,View(restored).Credits);Assert.AreEqual(after.Entities.Count,View(restored).Entities.Count);
            Call(restored,"Apply",new PlayableCommand(1,2,"player-1",PlayableCommandKind.QueueTank,new[]{factory}),null);
            Assert.AreEqual(next.TankCreditCost,View(restored).Buildings.Single(b=>b.Id==factory).PrivateState.Orders[1].PaidCost);
        }
        [Test] public void ReusedRevisionCannotOverwriteFirstTransactionBinding()
        {
            var p=PlayableProfile.Default;var d=New(p);var data=p.CopyData();data.tankCreditCost+=10;
            Assert.IsNotNull(Call(d,"ValidateBalance",PlayableProfile.Create(data)));
            Assert.AreEqual(p.TankCreditCost,View(d).ActiveProfile.TankCreditCost);
        }
        [Test] public void CaptureIsReadOnlyAndIncompleteBuildingKeepsItsDuration()
        {
            var p=PlayableProfile.Default;var d=New(p);Call(d,"AddCredits",PlayableOwner.Player,10000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
            Call(d,"AdvanceFoundations");Call(d,"AdvanceBuildings",1d);
            var before=View(d).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory);
            var data=p.CopyData();data.revision=2;data.factoryBuildSeconds+=20;var q=PlayableProfile.Create(data);
            Call(d,"ApplyBalance",q);
            Assert.AreEqual(before.Progress,View(d).Buildings.Single(b=>b.Id==before.Id).Progress);
            byte[] one=(byte[])Call(d,"CaptureWorldBytes",7,"lab");byte[] two=(byte[])Call(d,"CaptureWorldBytes",7,"lab");CollectionAssert.AreEqual(one,two);
            var restored=Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{one,q,7,"lab"});
            Assert.AreEqual(before.Progress,View(restored).Buildings.Single(b=>b.Id==before.Id).Progress);
        }
        [Test] public void RejectedTopologyDoesNotMutateDomain()
        {
            var p=PlayableProfile.Default;var d=New(p);var data=p.CopyData();data.revision=2;data.tankCollisionRadius+=.1;
            Assert.IsNotNull(Call(d,"ValidateBalance",PlayableProfile.Create(data)));Assert.AreEqual(p.Revision,View(d).ProfileRevision);
        }
        [Test] public void PreparationRefusalPublishesRetryStatusAndKeepsActualRuntimeWorkerAlive()
        {
            var p=PlayableProfile.Default;var data=p.CopyData();data.revision++;data.tankSpeed*=1.1;var next=PlayableProfile.Create(data);var runtime=new PlayableRuntime(p,1,7,autonomousOwnerAi:false,startPaused:true);var kernel=new CertifiedRouteKernel();var actual=typeof(PlayableRuntime).GetField("domain",F).GetValue(runtime);var nav=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(actual);var budget=nav.PhysicalHostBudget;var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",F).GetValue(runtime);int denied=0;
            bool Until(Func<bool> done)=>SpinWait.SpinUntil(()=>{if(authority.AwaitingRoutes)kernel.Pump(runtime.NavigationBinding,4);return done();},3000);
            try{
                Assert.True(Until(()=>runtime.Latest?.Paused==true));Assert.Null(Call(actual,"ValidateBalance",next));
                budget.AllocationAdmission=_=>{var frames=new System.Diagnostics.StackTrace().GetFrames();if(frames.Any(f=>f.GetMethod().DeclaringType==Domain&&f.GetMethod().Name=="TryApplyBalance")&&frames.Any(f=>f.GetMethod().DeclaringType==typeof(NavigationAdmissionStorage))){Interlocked.Increment(ref denied);return false;}return true;};
                Assert.Null(runtime.RequestBalance(next,1,p.Revision));runtime.RequestPause(false);Assert.True(Until(()=>runtime.BalanceApplyStatus==PlayableDomain.BalancePreparationRefusedMessage));Assert.AreEqual(1,Volatile.Read(ref denied));Assert.False(runtime.IsStopped);Assert.AreEqual(RuntimeStatus.Running,runtime.Latest.Status);Assert.Null(runtime.Latest.Failure);Assert.AreEqual(p.Revision,runtime.Latest.ProfileRevision);Assert.AreEqual(p.Revision,nav.Admission.Profile.Revision);Assert.Zero(nav.OwnedRebindPublications);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                budget.AllocationAdmission=null;Assert.Null(runtime.RequestBalance(next,1,p.Revision));Assert.True(Until(()=>runtime.Latest?.ProfileRevision==next.Revision));Assert.AreSame(next,runtime.Latest.ActiveProfile);Assert.AreEqual(next.Revision,nav.Admission.Profile.Revision);Assert.AreEqual(1,nav.OwnedRebindPublications);Assert.Null(runtime.Latest.Failure);Assert.False(runtime.IsStopped);
            }finally{budget.AllocationAdmission=null;runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,3000));kernel.Clear();nav.ReleaseCommittedHostRoot();Call(actual,"DestroyOwnedNavigation");Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);runtime.Dispose();}
        }

        [Test] public void PausedRequestWaitsForResumeAndPublishesMatchingProfile()
        {
            var p=PlayableProfile.Default;var data=p.CopyData();data.revision=2;data.tankWeaponDamage+=1;var next=PlayableProfile.Create(data);
            using(var runtime=new PlayableRuntime(p,1,7,autonomousOwnerAi:false,startPaused:true))
            {
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest!=null,3000));long tick=runtime.Latest.Tick;
                Assert.IsNull(runtime.RequestBalance(next,1,p.Revision));Thread.Sleep(100);
                Assert.AreEqual(p.Revision,runtime.Latest.ProfileRevision);Assert.AreEqual(tick,runtime.Latest.Tick);
                runtime.RequestPause(false);Assert.True(SpinWait.SpinUntil(()=>runtime.Latest?.ProfileRevision==2,3000));
                Assert.AreSame(next,runtime.Latest.ActiveProfile);Assert.IsNull(runtime.Latest.Failure);runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,3000));
            }
        }
    }
}
