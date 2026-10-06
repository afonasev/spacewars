using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class TerritoryDomainTests
    {
        private static readonly Type T=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private object d;
        private PlayableProfile p;
        private long sequence;
        private object Call(string name,params object[] args)=>T.GetMethod(name,Flags).Invoke(d,args);
        private PlayableSnapshot View()=>(PlayableSnapshot)Call("Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        private NavigationSession Nav=>(NavigationSession)T.GetProperty("Navigation",Flags).GetValue(d);
        private void Frames(int count,bool route=false)
        {
            for(int i=0;i<count;i++)
            {
                if(route)while(Nav.Requests.TryDequeue(out var r))Nav.Answers.TryEnqueue(new NavigationAnswer(r,new[]{r.Start,r.Goal}));
                Call("Step",1d/30d);
            }
        }
        private PlayableCommandStatus Apply(PlayableCommandKind kind,int site=0,int slot=0,PlayableBuildingKind building=PlayableBuildingKind.Outpost,int parent=0,int id=0,string owner="player-1")
        {
            object[] args={new PlayableCommand(1,++sequence,owner,kind,id==0?new int[0]:new[]{id},siteId:site,slotId:slot,buildingKind:building,parentId:parent),null};
            return (PlayableCommandStatus)T.GetMethod("Apply",Flags).Invoke(d,args);
        }
        private int Spawn(NavPoint position,bool enemy=false)
        {
            if(enemy)Call("SpawnEnemy",position);else Call("SpawnPlayer",position,position);
            return View().Entities.Max(x=>x.Id);
        }
        private void RemoveUnit(int id){Call("Damage",id,100000);}
        private void Capture(int site)
        {
            var pos=View().Sites.Single(s=>s.Site.Id==site).Site.Position;
            int unit=Spawn(new NavPoint(pos.X-1,pos.Z));Frames(152);RemoveUnit(unit);
        }
        [SetUp] public void Setup()
        {
            var data=(PlayableProfileData)typeof(PlayableProfile).GetMethod("DefaultData",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            data.enemyAdvanceDelaySeconds=600;p=PlayableProfile.Create(data);
            d=Activator.CreateInstance(T,Flags,null,new object[]{p,1L},null);
            T.GetField("credits",Flags).SetValue(d,10000d);
        }
        [Test] public void CaptureDoesNotAccelerateAndContestFreezesThenDecays()
        {
            var pos=new NavPoint(p.OutpostX,p.OutpostZ);int first=Spawn(new NavPoint(pos.X-2,pos.Z));Frames(30);
            double one=View().Sites[2].Progress;
            int second=Spawn(new NavPoint(pos.X+2,pos.Z));Frames(30);
            Assert.AreEqual(one*2,View().Sites[2].Progress,1e-8);
            int enemy=Spawn(new NavPoint(pos.X,pos.Z+2),true);double before=View().Sites[2].Progress;Frames(1);
            Assert.IsTrue(View().Sites[2].Contested);Assert.AreEqual(before,View().Sites[2].Progress);
            RemoveUnit(first);RemoveUnit(second);Frames(1);Assert.Less(View().Sites[2].Progress,before);
            Frames(151);Assert.AreEqual(PlayableOwner.Enemy,View().Sites[2].Claimant);Assert.Greater(View().Sites[2].Progress,0);
        }
        [Test] public void CaptureUnlocksPaidRequestAndRejectsDuplicateWrongOwnerAndPoorBuyer()
        {
            Capture(4);Assert.AreEqual(2,View().Buildings.Count);
            T.GetField("credits",Flags).SetValue(d,0d);
            Assert.AreEqual(PlayableCommandStatus.InsufficientCredits,Apply(PlayableCommandKind.BuildAt,4,building:PlayableBuildingKind.Mine));Assert.IsNull(View().Sites[3].Owner);
            T.GetField("credits",Flags).SetValue(d,1000d);
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Apply(PlayableCommandKind.BuildAt,4,building:PlayableBuildingKind.Mine,owner:"enemy-1"));
            Assert.AreEqual(PlayableCommandStatus.Applied,Apply(PlayableCommandKind.BuildAt,4,building:PlayableBuildingKind.Mine));
            int after=View().Credits;Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Apply(PlayableCommandKind.BuildAt,4,building:PlayableBuildingKind.Mine));Assert.AreEqual(after,View().Credits);
            Assert.AreEqual(ConstructionPhase.Pending,View().Buildings.Last().Phase);
        }
        [Test] public void FriendlyHoldEvacuatesBeforeSolidAndDoesNotReplacePendingRouteEachTick()
        {
            Capture(3);var pos=View().Sites[2].Site.Position;int unit=Spawn(pos);Nav.Stop(unit,true);
            while(Nav.Requests.TryDequeue(out _)){}
            int revision=View().Geometry.Revision;
            Assert.AreEqual(PlayableCommandStatus.Applied,Apply(PlayableCommandKind.BuildAt,3));Frames(5);
            Assert.AreEqual(ConstructionPhase.Pending,View().Buildings.Last().Phase);Assert.AreEqual(revision,View().Geometry.Revision);Assert.AreEqual(1,Nav.Requests.Count);
            Assert.AreEqual(pos.X,View().Entities.Single(x=>x.Id==unit).Position.X);
            Frames(150,true);Assert.AreNotEqual(ConstructionPhase.Pending,View().Buildings.Last().Phase);
            Assert.Greater(View().Geometry.Revision,revision);Assert.IsFalse(Nav.Crowd.Units.Single(x=>x.Id==unit).Held);
        }
        [Test] public void HostileOccupancyBlocksAndCancelRefundsOnceWithClaimPreserved()
        {
            Capture(4);Spawn(View().Sites[3].Site.Position,true);
            int before=View().Credits;Apply(PlayableCommandKind.BuildAt,4,building:PlayableBuildingKind.Mine);Frames(1);
            var b=View().Buildings.Last();Assert.AreEqual(ConstructionPhase.Pending,b.Phase);Assert.AreEqual(0,b.Health);
            Assert.AreEqual(PlayableCommandStatus.Applied,Apply(PlayableCommandKind.CancelBuilding,id:b.Id));
            Assert.AreEqual(before-p.MineCreditCost*(1-p.BuildingCancellationRefundRatio),View().Credits);
            int refunded=View().Credits;Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Apply(PlayableCommandKind.CancelBuilding,id:b.Id));Assert.AreEqual(refunded,View().Credits);Assert.AreEqual(PlayableOwner.Player,View().Sites[3].Owner);
        }
        [Test] public void FoundationDamageSurvivesCompletionAndMinePaysOnlyOnceReady()
        {
            Capture(4);Apply(PlayableCommandKind.BuildAt,4,building:PlayableBuildingKind.Mine);Frames(30);
            var mine=View().Buildings.Last();double initial=View().IncomePerSecond;Call("Damage",mine.Id,5);Frames(450);
            var ready=View().Buildings.Single(x=>x.Id==mine.Id);Assert.AreEqual(ConstructionPhase.Ready,ready.Phase);Assert.Less(ready.Health,p.MineHealth);Assert.AreEqual(initial+p.MineIncomePerPeriod/p.IncomePeriodSeconds,View().IncomePerSecond);
            Call("Damage",mine.Id,100000);Assert.IsNull(View().Sites[3].Owner);Assert.AreEqual(initial,View().IncomePerSecond);
        }
        [TestCase(false)] [TestCase(true)] public void MaterializedOutpostSurvivesHqLossAndCascadeIsBaseLocal(bool ready)
        {
            Capture(3);Apply(PlayableCommandKind.BuildAt,3);Frames(ready?601:1);
            int outpost=View().Sites[2].CenterId,hq=View().Sites[0].CenterId;
            Apply(PlayableCommandKind.BuildAt,1,1,PlayableBuildingKind.Factory,hq);Frames(1);
            Call("Damage",hq,100000);Frames(1);
            Assert.AreEqual(PlayableMatchOutcome.Playing,View().Outcome);Assert.IsTrue(View().Buildings.Any(b=>b.Id==outpost));Assert.IsFalse(View().Buildings.Any(b=>b.SiteId==1));Assert.IsNull(View().Sites[0].Owner);
        }
        [Test] public void PaidPendingOutpostDoesNotPostponeDefeat()
        {
            Capture(3);Spawn(View().Sites[2].Site.Position,true);Apply(PlayableCommandKind.BuildAt,3);Frames(1);
            Call("Damage",View().Sites[0].CenterId,100000);Frames(1);Assert.AreEqual(PlayableMatchOutcome.PlayerLost,View().Outcome);Assert.IsFalse(View().Buildings.Any(b=>b.Phase==ConstructionPhase.Pending));
        }
        [Test] public void RebuiltFactoryHasNewIdAndStaleParentCannotSpend()
        {
            int parent=View().Sites[0].CenterId;
            Apply(PlayableCommandKind.BuildAt,1,1,PlayableBuildingKind.Factory,parent);Frames(451);
            int old=View().Buildings.Last().Id;Call("Damage",old,100000);
            int before=View().Credits;Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Apply(PlayableCommandKind.BuildAt,1,1,PlayableBuildingKind.Factory,parent+999));Assert.AreEqual(before,View().Credits);
            Assert.AreEqual(PlayableCommandStatus.Applied,Apply(PlayableCommandKind.BuildAt,1,1,PlayableBuildingKind.Factory,parent));Assert.Greater(View().Buildings.Last().Id,old);
        }
        [Test] public void TwoFactoriesProduceAtSeparateExitsAndCentralCancellationKeepsOtherSlots()
        {
            int parent=View().Sites[0].CenterId;
            Apply(PlayableCommandKind.BuildAt,1,1,PlayableBuildingKind.Factory,parent);
            Apply(PlayableCommandKind.BuildAt,1,2,PlayableBuildingKind.Factory,parent);Frames(451);
            var factories=View().Buildings.Where(b=>b.Kind==PlayableBuildingKind.Factory).ToArray();Assert.AreEqual(2,factories.Length);
            foreach(var b in factories)Assert.AreEqual(PlayableCommandStatus.Applied,Apply(PlayableCommandKind.QueueTank,id:b.Id));Frames(451);
            var tanks=View().Entities.Where(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank).ToArray();Assert.AreEqual(2,tanks.Length);Assert.AreNotEqual(tanks[0].Id,tanks[1].Id);
            Assert.Greater(Math.Abs(tanks[0].Position.Z-tanks[1].Position.Z),p.TankCollisionRadius*2);
            Capture(3);Apply(PlayableCommandKind.BuildAt,3);Frames(60);
            int id=View().Sites[2].CenterId;Assert.AreEqual(PlayableCommandStatus.Applied,Apply(PlayableCommandKind.CancelBuilding,id:id));Assert.AreEqual(2,View().Buildings.Count(b=>b.Kind==PlayableBuildingKind.Factory));Assert.AreEqual(PlayableOwner.Player,View().Sites[2].Owner);
        }
        [Test] public void CompletedMineSettlesOnBoundaryAndDestroyedMineDoesNotPayAgain()
        {
            Capture(4);Apply(PlayableCommandKind.BuildAt,4,building:PlayableBuildingKind.Mine);Frames(451);
            int mine=View().Sites[3].CenterId;Assert.AreEqual(ConstructionPhase.Ready,View().Buildings.Single(b=>b.Id==mine).Phase);
            int until=(int)(30-View().Tick%30);int before=View().Credits;Frames(until);
            Assert.AreEqual(before+(p.HeadquartersIncomePerPeriod+p.MineIncomePerPeriod)/p.IncomePeriodSeconds,View().Credits);
            Call("Damage",mine,100000);before=View().Credits;Frames(30);Assert.AreEqual(before+p.HeadquartersIncomePerPeriod/p.IncomePeriodSeconds,View().Credits);
        }
        [Test] public void ProfileRejectsMissingCaptureFractionalSlotsAndOutOfRangeValues()
        {
            var method=typeof(PlayableProfile).GetMethod("DefaultData",BindingFlags.Static|BindingFlags.NonPublic);
            var data=(PlayableProfileData)method.Invoke(null,null);data.outpostCaptureRadius=0;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
            data=(PlayableProfileData)method.Invoke(null,null);data.outpostSlots=2.5;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
        }
        [Test] public void InvalidLayoutAndForeignProducerCommandsAreRejected()
        {
            var method=typeof(PlayableProfile).GetMethod("DefaultData",BindingFlags.Static|BindingFlags.NonPublic);
            var data=(PlayableProfileData)method.Invoke(null,null);data.outpostX=0;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
            data=(PlayableProfileData)method.Invoke(null,null);data.slotRingRadius=12;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
            data=(PlayableProfileData)method.Invoke(null,null);data.mineX=data.outpostX;data.mineZ=data.outpostZ;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
            int parent=View().Sites[0].CenterId;Apply(PlayableCommandKind.BuildAt,1,1,PlayableBuildingKind.Factory,parent);Frames(451);
            var factory=View().Buildings.Last();int before=View().Credits;
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Apply(PlayableCommandKind.QueueTank,id:factory.Id,owner:"enemy-1"));
            Assert.AreEqual(before,View().Credits);Assert.AreEqual(0,View().Buildings.Last().QueueCount);
        }
        [Test] public void VisiblePadShapesDoNotUseCaptureRadius()
        {
            Assert.IsFalse(TerritoryRules.Contains(new NavPoint(2,0),new NavPoint(0,0),1.7,false,false));
            Assert.IsFalse(TerritoryRules.Contains(new NavPoint(1.6,1.6),new NavPoint(0,0),1.6,false,true));
            Assert.IsTrue(TerritoryRules.Contains(new NavPoint(1.5,1.5),new NavPoint(0,0),1.6,true,false));
        }
    }
}
