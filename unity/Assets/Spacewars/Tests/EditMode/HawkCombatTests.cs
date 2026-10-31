using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class HawkCombatTests
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        PlayableDomain d;PlayableProfile p;long sequence;
        IDictionary Units=>(IDictionary)typeof(PlayableDomain).GetField("units",Hidden).GetValue(d);
        object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,Hidden).Invoke(d,args);
        object Unit(int id)=>Units[id];
        void Set(int id,string field,object value)=>Unit(id).GetType().GetField(field).SetValue(Unit(id),value);
        T Get<T>(int id,string field)=>(T)Unit(id).GetType().GetField(field).GetValue(Unit(id));
        PlayableSnapshot View=>d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        PlayableEntitySnapshot Entity(int id)=>View.Entities.Single(x=>x.Id==id);
        int Spawn(PlayableOwner owner,PlayableEntityKind kind,double x,double z)=>(int)Call("SpawnUnit",new NavPoint(x,z),owner,kind);
        void Send(PlayableCommandKind kind,int id,NavPoint point=default,int target=0)=>Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(1,++sequence,"player-1",kind,new[]{id},point,targetId:target),out _));
        void Tick(int count=1){for(int i=0;i<count;i++)d.Step(1d/30);}
        void Until(Func<bool> condition,int limit=900){for(int i=0;i<limit&&!condition();i++)Tick();Assert.True(condition(),"Fixed combat horizon exhausted");}
        [SetUp] public void Setup(){p=PlayableProfile.Default;d=new PlayableDomain(p,1,false);foreach(int id in Units.Keys.Cast<int>().ToArray()){d.Navigation.Remove(id);Units.Remove(id);}}
        [TearDown] public void Cleanup(){d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();}
        [Test] public void ResearchAppearanceAndLaunchFactsReachOwnerSnapshot()
        {
            int a=Spawn(PlayableOwner.Player,PlayableEntityKind.Hawk,-10,15),b=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,-3,15);
            Assert.False(Entity(a).Upgraded);CompleteSalvoResearch();Assert.True(Entity(a).Upgraded);
            var observed=d.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            Assert.True(observed.Entities.Single(e=>e.Id==a).Upgraded);
            int future=Spawn(PlayableOwner.Player,PlayableEntityKind.Hawk,-12,15);Assert.True(Entity(future).Upgraded);
            Call("LaunchHawk",Unit(a),b,0);Call("LaunchHawk",Unit(a),b,1);
            var facts=View.Sounds.Where(e=>e.Kind==PlayableSoundKind.Shot&&e.UnitId==a).ToArray();
            CollectionAssert.AreEqual(new[]{0,1},facts.Select(e=>e.ShotSide).ToArray());
            Assert.AreEqual(2,View.Projectiles.Count(e=>e.Kind==PlayableEntityKind.Hawk));
        }
        [Test] public void PairUsesFixedTargetAndSixHitsKillPeerWithoutGroundWreckAuthority()
        {
            int a=Spawn(PlayableOwner.Player,PlayableEntityKind.Hawk,-10,15),b=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,-3,15);Set(b,"Reload",1000d);
            Until(()=>Entity(b).Health<p.HawkHealth);Assert.AreEqual(p.HawkHealth-p.HawkDamage,Entity(b).Health);
            Until(()=>Entity(b).Health<p.HawkHealth-p.HawkDamage);Assert.AreEqual(p.HawkHealth-2*p.HawkDamage,Entity(b).Health);Assert.Greater(Get<double>(a,"Reload"),0);
            Until(()=>!View.Entities.Any(e=>e.Id==b));var death=View.Sounds.Single(e=>e.Kind==PlayableSoundKind.Destroyed&&e.UnitId==b);Assert.Greater(death.Altitude,0);Assert.False(d.Navigation.Crowd.Units.Any(n=>n.Id==b));Assert.AreEqual(PlayableOwner.Enemy,death.Owner);
        }
        [Test] public void MoveImmediatelyCancelsSecondRocketAndStartsReload()
        {
            int a=Spawn(PlayableOwner.Player,PlayableEntityKind.Hawk,-10,15),b=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,-3,15);Set(b,"Reload",1000d);Until(()=>Get<int>(a,"BurstRemaining")==1);
            Send(PlayableCommandKind.Move,a,new NavPoint(-10,20));Assert.Zero(Get<int>(a,"BurstRemaining"));Assert.Greater(Get<double>(a,"Reload"),0);Tick(15);Assert.LessOrEqual(p.HawkHealth-Entity(b).Health,p.HawkDamage);
        }
        [Test] public void LostFixedTargetCancelsPairRatherThanSwitchingSecondRocket()
        {
            int a=Spawn(PlayableOwner.Player,PlayableEntityKind.Hawk,-10,15),b=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,-3,15),c=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,-3,18);Set(b,"Reload",1000d);Set(c,"Reload",1000d);Until(()=>Get<int>(a,"BurstRemaining")==1);Assert.AreEqual(b,Get<int>(a,"BurstTarget"));Call("Damage",b,10000);Tick(6);Assert.Zero(Get<int>(a,"BurstRemaining"));Assert.AreEqual(p.HawkHealth,Entity(c).Health);Assert.Greater(Get<double>(a,"Reload"),0);
        }
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Shkval)] public void TankCannotAcquireAirAndItsGroundShellCannotHitHoveringAir(PlayableEntityKind ground)
        {
            int tank=Spawn(PlayableOwner.Player,ground,-10,15),air=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,-3,15);Set(air,"Reload",1000d);Send(PlayableCommandKind.Attack,tank,target:air);Tick(90);Assert.Zero(Get<int>(tank,"Target"));Assert.False(View.Projectiles.Any(x=>x.Kind==PlayableEntityKind.Tank));Assert.AreEqual(0,Call("FirstHitAtHeight",PlayableOwner.Player,new NavPoint(-10,15),new NavPoint(0,15),0d,.5d,.5d));
        }
        [TestCase(false)][TestCase(true)] public void BothExplorerVariantsFireExactlyUpwardWithFixedCabin(bool upgraded)
        {
            int scout=Spawn(PlayableOwner.Player,PlayableEntityKind.Explorer,-10,15),air=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,-10,15);Set(scout,"AuthoredUpgrade",upgraded);Set(air,"Reload",1000d);Tick();Assert.AreEqual(Math.PI/2,Entity(scout).WeaponPitch,1e-9);Assert.AreEqual(0,Entity(scout).HullHeading,1e-9);Assert.True(View.Projectiles.Any(x=>x.Kind==PlayableEntityKind.Explorer&&x.Pitch>1.5));Until(()=>Entity(air).Health<p.HawkHealth,120);
        }
        [Test] public void PendingPairAndHomingRocketRoundTripExactlyAndContinue()
        {
            int a=Spawn(PlayableOwner.Player,PlayableEntityKind.Hawk,-10,15),b=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,-3,15);Set(b,"Reload",1000d);Until(()=>Get<int>(a,"BurstRemaining")==1);Assert.True(View.Projectiles.Any(x=>x.Kind==PlayableEntityKind.Hawk));var original=d;var bytes=d.CaptureWorldBytes(7,"hawk-combat");var restored=PlayableDomain.RestoreWorldBytes(bytes,p,7,"hawk-combat");
            try{CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"hawk-combat"));for(int i=0;i<60;i++){original.Step(1d/30);restored.Step(1d/30);}CollectionAssert.AreEqual(original.CaptureWorldBytes(7,"hawk-combat"),restored.CaptureWorldBytes(7,"hawk-combat"));}
            finally{restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }
        [TestCase(false,false)] [TestCase(true,false)] [TestCase(false,true)] [TestCase(true,true)]
        public void OrdinaryRocketSteersOnlyWhenTargetMovesAway(bool receding,bool upgraded)
        {
            if(upgraded)CompleteSalvoResearch();
            int a=Spawn(PlayableOwner.Player,PlayableEntityKind.Hawk,-10,15),b=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,-3,15);
            Call("LaunchHawk",Unit(a),b,0);
            var shots=(IList)typeof(PlayableDomain).GetField("projectiles",Hidden).GetValue(d);
            var shot=shots[0];var rocket=(HawkRocketFlight)shot.GetType().GetField("Hawk").GetValue(shot);
            var heading=rocket.Direction;
            // A small lateral displacement exposes steering even before physical contact.
            var air=Get<object>(b,"Air");var actor=(NavUnit)air.GetType().GetField("Actor").GetValue(air);
            actor.Position=new NavPoint(-3,16);
            Set(b,"Velocity",receding?new NavPoint(1,0):default(NavPoint));
            Assert.False((bool)Call("AdvanceAirProjectile",shot,.01d));
            if(receding&&upgraded)Assert.AreNotEqual(heading,rocket.Direction);
            else Assert.AreEqual(heading,rocket.Direction,"Steering requires completed research and a receding target");
        }
        [Test] public void ResearchDoesNotUpgradeAnInFlightMissileAndLaunchTermsRestoreExactly()
        {
            int a=Spawn(PlayableOwner.Player,PlayableEntityKind.Hawk,-10,15),b=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,-3,15);
            Call("LaunchHawk",Unit(a),b,0);CompleteSalvoResearch();Call("LaunchHawk",Unit(a),b,1);
            var shots=(IList)typeof(PlayableDomain).GetField("projectiles",Hidden).GetValue(d);
            HawkRocketFlight Rocket(object shot)=>(HawkRocketFlight)shot.GetType().GetField("Hawk").GetValue(shot);
            Assert.Zero(Rocket(shots[0]).TurnSpeed);Assert.AreEqual(p.HawkMissileTurn,Rocket(shots[1]).TurnSpeed);
            var bytes=d.CaptureWorldBytes(7,"research-flight");var restored=PlayableDomain.RestoreWorldBytes(bytes,p,7,"research-flight");
            try{CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"research-flight"));for(int n=0;n<10;n++){d.Step(1d/30);restored.Step(1d/30);}CollectionAssert.AreEqual(d.CaptureWorldBytes(7,"research-flight"),restored.CaptureWorldBytes(7,"research-flight"));}
            finally{restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }
        static object Read(object value,string name)=>value.GetType().GetField(name).GetValue(value);
        static void Write(object value,string name,object data)=>value.GetType().GetField(name).SetValue(value,data);
        void PlaceAir(int id,BallisticPoint center)
        {
            var air=Get<object>(id,"Air");var actor=(NavUnit)Read(air,"Actor");
            actor.Position=center.Ground;actor.Goal=center.Ground;actor.Moving=false;
            double height=center.Y-1.8145018368959427*p.HawkModelScale/2;
            Write(air,"Height",height);Write(air,"PreviousHeight",height);Set(id,"PreviousPosition",center.Ground);Set(id,"Velocity",default(NavPoint));
        }
        [TestCase(false,false)] [TestCase(false,true)] [TestCase(true,false)] [TestCase(true,true)]
        public void FullWeaponRangeBudgetCanHitSecondaryAirTargetButNeverBeyondEnd(bool explorer,bool beyond)
        {
            int a=Spawn(PlayableOwner.Player,explorer?PlayableEntityKind.Explorer:PlayableEntityKind.Hawk,-10,15),initial=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,-6,15);
            if(explorer){Set(a,"Target",initial);Set(a,"BurstSize",4);Set(a,"BurstRemaining",4);Set(a,"BurstSpread",p.ExplorerSpreadDeg);Call("FireTracer",Unit(a),Entity(a).Position,Entity(initial).Position);}
            else Call("LaunchHawk",Unit(a),initial,0);
            var shots=(IList)typeof(PlayableDomain).GetField("projectiles",Hidden).GetValue(d);Assert.AreEqual(1,shots.Count);var shot=shots[0];
            var rocket=Read(shot,"Hawk") as HawkRocketFlight;
            var ground=(NavPoint)Read(shot,"Position");var start=new BallisticPoint(ground.X,(double)Read(shot,"Height"),ground.Z);
            var direction=rocket?.Direction??new BallisticPoint((double)Read(shot,"DirectionX"),(double)Read(shot,"DirectionY"),(double)Read(shot,"DirectionZ"));
            double remaining=rocket!=null?rocket.RemainingLife*rocket.Speed:(double)Read(shot,"Remaining");
            double offset=explorer?p.ExplorerMuzzleOffset:HawkRocketFlight.Length(HawkRocketFlight.Sub(start,new BallisticPoint(-10,Entity(a).Altitude+1.8145018368959427*p.HawkModelScale/2,15)));
            Assert.AreEqual(12,offset+remaining,1e-9,"Budget derives from range 10, not initial target distance 4");
            PlaceAir(initial,new BallisticPoint(-6,3.4,22));
            var next=HawkRocketFlight.Add(start,HawkRocketFlight.Scale(direction,remaining+(beyond?p.HawkRadius+.3:-.15)));
            int secondary=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,next.X,next.Z);PlaceAir(secondary,next);
            Assert.True((bool)Call("AdvanceAirProjectile",shot,2d),"Large step expires exactly at the travel boundary or a prior contact");
            Assert.AreEqual(p.HawkHealth-(beyond?0:explorer?p.ExplorerDamage:p.HawkDamage),Entity(secondary).ExactHealth.Value,1e-9);
            Assert.AreEqual(p.HawkHealth,Entity(initial).ExactHealth.Value);
        }
        [TestCase(false)] [TestCase(true)]
        public void FirstEffectiveSalvoReportsRealLaunchAndDamageTiming(bool upgraded)
        {
            if(upgraded)CompleteSalvoResearch();
            int a=Spawn(PlayableOwner.Player,PlayableEntityKind.Hawk,-10,15),b=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,-3,15);Set(b,"Reload",1000d);
            long start=d.Tick;double first=-1,second=-1,damage=-1;int launches=0;
            for(int tick=0;tick<300;tick++)
            {
                Tick();double seconds=(d.Tick-start)/30d;
                // Persistent burst state gives launch timestamps without depending on sound history retention.
                if(first<0&&Get<int>(a,"BurstSequence")>0){first=seconds;launches=1;}
                if(first>=0&&second<0&&Get<int>(a,"BurstRemaining")==0){second=seconds;launches=2;}
                if(damage<0&&Entity(b).Health<p.HawkHealth)damage=seconds;
                if(second>=0&&damage>=0)break;
            }
            TestContext.WriteLine($"HAWK_FIRST_SALVO upgraded={upgraded} distance=7 target=stationary-air dt=0.03333333333333333 firstLaunch={first:R} secondLaunch={second:R} firstDamage={damage:R} targetHp={Entity(b).ExactHealth:R}");
            Assert.AreEqual(2,launches);Assert.GreaterOrEqual(first,upgraded?p.HawkUpgradedPreparationSeconds:p.HawkPreparationSeconds);
            Assert.Greater(second,first);Assert.Greater(damage,first);Assert.Less(damage,10);
        }
        void CompleteSalvoResearch()
        {
            Call("AddCredits",PlayableOwner.Player,10000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,5,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,2,PlayableBuildingKind.ScientificCenter,1,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            int center=View.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.ScientificCenter).Id;
            Call("RestoreResearchState",new PlayableResearchSaveState{Orders=new[]{new PlayableResearchSaveOrder{Id=1,Owner=(int)PlayableOwner.Player,Kind=PlayableResearchKind.HawkSalvoSystems,CenterId=center,Complete=true,PaidCost=300,Duration=30,Elapsed=30}}});
        }
        [TestCase(false)][TestCase(true)] public void PairUsesProfileTimingAndIndependentPreparationAndReload(bool upgraded)
        {
            if(upgraded)CompleteSalvoResearch();
            int a=Spawn(PlayableOwner.Player,PlayableEntityKind.Hawk,-10,15),b=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,-3,15);Set(b,"Reload",1000d);
            Until(()=>Get<int>(a,"BurstRemaining")==1);
            double delay=upgraded?.18:.5,reload=upgraded?2:3,prepare=upgraded?1.5:3;
            Assert.AreEqual(delay,Get<double>(a,"BurstDelay"),1e-9);Assert.AreEqual(reload,Get<double>(a,"HawkBurstReload"),1e-9);
            Call("AdvanceHawkCombat",Unit(a),delay-.01);Assert.AreEqual(1,Get<int>(a,"BurstRemaining"));
            Call("AdvanceHawkCombat",Unit(a),.011d);Assert.Zero(Get<int>(a,"BurstRemaining"));Assert.AreEqual(reload,Get<double>(a,"Reload"),1e-9);
            int sequence=Get<int>(a,"BurstSequence");
            // A movement/stop cycle never alters the independent weapon reload.
            Send(PlayableCommandKind.Move,a,new NavPoint(-10,20));Send(PlayableCommandKind.Stop,a);
            Assert.AreEqual(reload,Get<double>(a,"Reload"),1e-9);
            Set(a,"StoppedSeconds",0d);
            Call("AdvanceHawkCombat",Unit(a),prepare-.001d);Assert.AreEqual(sequence,Get<int>(a,"BurstSequence"),"Both independent readiness timers must expire");
            double remaining=Get<double>(a,"Reload");
            if(remaining>0){Call("AdvanceHawkCombat",Unit(a),remaining-.001d);Assert.AreEqual(sequence,Get<int>(a,"BurstSequence"));Call("AdvanceHawkCombat",Unit(a),.002d);}
            // Baseline preparation and cooldown expire together; direct float thresholds may need one tick.
            else Call("AdvanceHawkCombat",Unit(a),.001d);
            Assert.AreEqual(sequence+1,Get<int>(a,"BurstSequence"));
        }
        [Test] public void ResearchDuringPairKeepsPendingDelayAndReloadThroughWorldRestore()
        {
            int a=Spawn(PlayableOwner.Player,PlayableEntityKind.Hawk,-10,15),b=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,-3,15);Set(b,"Reload",1000d);Until(()=>Get<int>(a,"BurstRemaining")==1);
            CompleteSalvoResearch();Assert.AreEqual(.5d,Get<double>(a,"BurstDelay"));Assert.AreEqual(3d,Get<double>(a,"HawkBurstReload"));
            var bytes=d.CaptureWorldBytes(7,"hawk-salvo-research");var restored=PlayableDomain.RestoreWorldBytes(bytes,p,7,"hawk-salvo-research");
            try{CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"hawk-salvo-research"));for(int n=0;n<18;n++){d.Step(1d/30);restored.Step(1d/30);}CollectionAssert.AreEqual(d.CaptureWorldBytes(7,"hawk-salvo-research"),restored.CaptureWorldBytes(7,"hawk-salvo-research"));Assert.Greater(Get<double>(a,"Reload"),2d);}
            finally{restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
            Send(PlayableCommandKind.Move,a,new NavPoint(-10,20));Send(PlayableCommandKind.Stop,a);
            Set(a,"StoppedSeconds",100d);Set(a,"Reload",0d);Call("AdvanceHawkCombat",Unit(a),.01d);
            Assert.AreEqual(.18d,Get<double>(a,"BurstDelay"));Assert.AreEqual(2d,Get<double>(a,"HawkBurstReload"));
        }
        [Test] public void SalvoResearchProfileHasMetadataAndRejectsInvalidValues()
        {
            Assert.AreEqual(3,p.HawkPreparationSeconds);Assert.AreEqual(.5,p.HawkSalvoDelay);Assert.AreEqual(3,p.HawkReloadSeconds);
            Assert.AreEqual(300,p.HawkSalvoResearchCost);Assert.AreEqual(30,p.HawkSalvoResearchSeconds);
            var fields=PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("hawk.salvoResearch")||f.Path.StartsWith("hawk.upgraded")).ToArray();Assert.AreEqual(5,fields.Length);
            foreach(var f in fields){Assert.IsNotEmpty(f.Label);Assert.IsNotEmpty(f.Description);Assert.IsNotEmpty(f.Unit);Assert.Greater(f.Step,0);
                var member=typeof(PlayableProfileData).GetField("hawk"+char.ToUpperInvariant(f.Path[5])+f.Path.Substring(6));Assert.NotNull(member);
                foreach(double value in new[]{f.Minimum-1,f.Maximum+1,double.NaN,double.PositiveInfinity}){var data=p.CopyData();member.SetValue(data,value);Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));}}
        }
        [Test] public void HoldDoesNotMoveForBlockedShotAndAttackFindsClearApproach()
        {
            int a=Spawn(PlayableOwner.Player,PlayableEntityKind.Hawk,-26,0),b=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-16,0);Send(PlayableCommandKind.Hold,a);var start=Entity(a).Position;Tick(90);Assert.AreEqual(start,Entity(a).Position);Assert.AreEqual(p.TankHealth,Entity(b).Health);Assert.False(View.Projectiles.Any(x=>x.Kind==PlayableEntityKind.Hawk));
            Send(PlayableCommandKind.Attack,a,target:b);bool moved=false;for(int tick=0;tick<300;tick++){Tick();moved|=!Entity(a).Position.Equals(start);if(Entity(b).Health<p.TankHealth)break;}Assert.True(moved,"Attack approaches a clear line around the blocking HQ");Assert.Less(Entity(b).Health,p.TankHealth);
        }
        [Test] public void SweptRocketMeetsRockAndEarliestBodyBeforeBuilding()
        {
            var rock=PlayableMap.SolidObstacles(p).First();double z=(rock.MinZ+rock.MaxZ)/2;
            var args=new object[]{new BallisticPoint(rock.MinX-1,.5,z),new BallisticPoint(rock.MaxX+1,.5,z),.07d,0,null,false,0,default(BallisticPoint),0d,0};
            Assert.True((bool)typeof(PlayableDomain).GetMethod("AirContact",Hidden).Invoke(d,args));Assert.Zero((int)args[6]);
            var hq=View.Buildings.First(b=>b.Kind==PlayableBuildingKind.Headquarters);double x=hq.Position.X,z2=hq.Position.Z;int near=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Explorer,x-8,z2);
            args=new object[]{new BallisticPoint(x-10,.5,z2),new BallisticPoint(x+5,.5,z2),.07d,0,null,false,0,default(BallisticPoint),0d,0};Assert.True((bool)typeof(PlayableDomain).GetMethod("AirContact",Hidden).Invoke(d,args));Assert.AreEqual(near,(int)args[6],"First unit in front of the building receives physical contact");
            Call("Damage",near,10000);args[6]=0;Assert.True((bool)typeof(PlayableDomain).GetMethod("AirContact",Hidden).Invoke(d,args));Assert.AreEqual(hq.Id,(int)args[6],"Building stops the same 3D ray");
        }
        [Test] public void VerticalMotionPreservesPreparationAndSubstantialYawResetsIt()
        {
            int a=Spawn(PlayableOwner.Player,PlayableEntityKind.Hawk,-10,15),b=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,-3,15);Set(b,"Reload",1000d);Set(a,"Target",b);Set(a,"StoppedSeconds",.7d);var air=Get<object>(a,"Air");var fields=air.GetType();fields.GetField("Height").SetValue(air,5d);
            Call("RefreshVision");Call("AdvanceHawkCombat",Unit(a),1d/30);Assert.Greater(Get<double>(a,"StoppedSeconds"),.7);
            fields.GetField("YawVelocity").SetValue(air,p.HawkPreparationYawSpeed*2);Call("AdvanceHawkCombat",Unit(a),1d/30);Assert.Zero(Get<double>(a,"StoppedSeconds"));Assert.Zero(Get<int>(a,"BurstRemaining"));
        }
        [TestCase(6d)][TestCase(8d)][TestCase(10d)] public void WorkingRangeExplorerDuelRecordsOutcomeAndTime(double distance)
        {
            int a=Spawn(PlayableOwner.Player,PlayableEntityKind.Hawk,-15,-20),b=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Explorer,-15+distance,-20);int tick=0;while(Units.Contains(a)&&Units.Contains(b)&&tick<900){Tick();tick++;}
            TestContext.WriteLine($"distance={distance:R} seconds={tick/30d:R} hawkAlive={Units.Contains(a)} explorerAlive={Units.Contains(b)} hawkHealth={(Units.Contains(a)?Entity(a).Health:0)} explorerHealth={(Units.Contains(b)?Entity(b).Health:0)}");Assert.Less(tick,900,"Stationary working-range duel must resolve within fixed 30 second observation");
        }
    }
}
