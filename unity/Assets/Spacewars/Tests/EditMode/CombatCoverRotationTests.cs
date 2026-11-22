using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class CombatCoverRotationTests
    {
        const long Gen=901;
        static readonly PlayableProfile P=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new ProvingGroundMap(),"rotation diagnostic");
        static AiProfile Profile()
        {
            var data=AiProfile.Initial.CopyData();
            foreach(var f in data.fields)
            {
                if(f.path.EndsWith(".micro.withdrawHealth"))f.value=.6;
                if(f.path.EndsWith(".micro.airFocusEnabled")||f.path.EndsWith(".micro.focusEnabled")||f.path.EndsWith(".micro.repairEnabled"))f.value=0;
            }
            return new AiProfile(data);
        }
        static PlayableEntitySnapshot Unit(int id,PlayableEntityKind kind,NavPoint at,int hp=100,PlayableOrderStamp stamp=null)=>
            new PlayableEntitySnapshot(id,id>=100?PlayableOwner.Enemy:PlayableOwner.Player,kind,at,hp,false,0,0,0,
                orderStamp:stamp,maxHealth:100,navigationOutcome:stamp==null?NavigationOutcome.Idle:NavigationOutcome.Arrived);
        static PlayableAiObservation View(long tick,PlayableEntitySnapshot[] units,params PlayableProjectileSnapshot[] shots)
        {
            var vision=new PlayableVision(0,50,50,1,1);
            vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),30)},Array.Empty<KnownBuilding>());
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,Gen,7,tick,tick,
                RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,Array.Empty<PlayableBuildingSnapshot>(),shots,
                new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot()));
        }
        static AiArmyRegistry Army(PlayableAiObservation o,AiProfile ai,AiDifficulty difficulty,out long id)
        {
            var registry=new AiArmyRegistry(o.OwnerId,Gen,ai,difficulty);
            var genesis=o.Entities.Where(u=>u.Owner==o.Owner).All(u=>u.Kind==PlayableEntityKind.Shkval)?View(o.Tick,o.Entities.Append(Unit(3,PlayableEntityKind.Tank,new NavPoint(-20,20))).ToArray()):o;
            Assert.True(registry.TryCreate(genesis,AiArmyRole.Attack,AiArmyPlanner.Policy,genesis.Entities.Where(u=>u.Owner==o.Owner).Select(u=>u.Id).ToArray(),out id,requiredMinimum:1));
            registry.Observe(o);
            Assert.True(registry.UpdateMission(id,AiArmyPlanner.Policy,AiArmyPhase.Engaging,o.Tick,"contact"));
            return registry;
        }
        static double Distance(NavPoint a,NavPoint b)=>AiCombatMicroSafety.Distance(a,b);
        static PlayableProjectileSnapshot Shot(long tick,int victim=1,PlayableEntityKind kind=PlayableEntityKind.Explorer,PlayableImpactMarker marker=null)=>
            new PlayableProjectileSnapshot((int)tick,101,victim,new NavPoint(6,0),kind,faction:PlayableOwner.Enemy,directLaunchTick:tick,marker:marker);

        [TestCase(PlayableEntityKind.Tank)]
        [TestCase(PlayableEntityKind.Explorer)]
        [TestCase(PlayableEntityKind.Shkval)]
        [TestCase(PlayableEntityKind.Hawk)]
        public void FirstHitBelowSixtyAllowsEveryKindToRotateBehindCompatibleAirCover(PlayableEntityKind kind)
        {
            var ai=Profile();var micro=new AiCombatMicroPolicy();
            PlayableAiObservation At(long tick,int hp,bool shot)=>View(tick,new[]{Unit(1,kind,default,hp),Unit(2,PlayableEntityKind.Hawk,new NavPoint(-.5,2)),Unit(101,PlayableEntityKind.Explorer,new NavPoint(6,0))},shot?new[]{Shot(tick)}:Array.Empty<PlayableProjectileSnapshot>());
            var first=At(10,100,false);var registry=Army(first,ai,AiDifficulty.Veteran,out _);micro.Observe(first,P,ai,AiDifficulty.Veteran,registry);
            var boundary=At(20,60,true);micro.Observe(boundary,P,ai,AiDifficulty.Veteran,registry);
            Assert.IsNull(micro.Plan(boundary,P,ai,AiDifficulty.Veteran,registry));
            var wounded=At(30,59,true);micro.Observe(wounded,P,ai,AiDifficulty.Veteran,registry);
            var plan=micro.Plan(wounded,P,ai,AiDifficulty.Veteran,registry);Assert.NotNull(plan);Assert.AreEqual(AiCombatMicroPhase.Withdraw,plan.Phase);
            var single=new AiCombatMicroPolicy();single.Observe(first,P,ai,AiDifficulty.Veteran,registry);single.Observe(wounded,P,ai,AiDifficulty.Veteran,registry);
            Assert.AreEqual(AiCombatMicroPhase.Withdraw,single.Plan(wounded,P,ai,AiDifficulty.Veteran,registry)?.Phase,"One damaging hit is sufficient");
            // Shkval has no ground screen here and uses the ordinary wounded rotation.
            Assert.Less(Distance(default,plan.Action.Target),3);
        }

        [TestCase(PlayableEntityKind.Tank,false)]
        [TestCase(PlayableEntityKind.Shkval,false)]
        [TestCase(PlayableEntityKind.Explorer,true)]
        [TestCase(PlayableEntityKind.Hawk,true)]
        public void AirCoverMustBeAttackableByTheActualEnemy(PlayableEntityKind enemy,bool canCover)
        {
            var ai=Profile();var micro=new AiCombatMicroPolicy();
            PlayableAiObservation At(long tick,int hp)=>View(tick,new[]{Unit(1,PlayableEntityKind.Tank,default,hp),Unit(2,PlayableEntityKind.Hawk,new NavPoint(-.5,2)),Unit(101,enemy,new NavPoint(6,0))},Shot(tick,kind:enemy));
            var first=At(10,100);var registry=Army(first,ai,AiDifficulty.Veteran,out _);micro.Observe(first,P,ai,AiDifficulty.Veteran,registry);
            var wounded=At(40,50);micro.Observe(wounded,P,ai,AiDifficulty.Veteran,registry);var plan=micro.Plan(wounded,P,ai,AiDifficulty.Veteran,registry);
            if(canCover){Assert.NotNull(plan);Assert.Less(Distance(default,plan.Action.Target),3);}
            else if(plan!=null)Assert.Greater(Distance(new NavPoint(6,0),plan.Action.Target),PlayableUnitRules.Range(P,enemy,false),"An unattackable Hawk cannot justify a short stop inside enemy range");
        }

        [TestCase(PlayableEntityKind.Tank,true)]
        [TestCase(PlayableEntityKind.Explorer,true)]
        [TestCase(PlayableEntityKind.Shkval,false)]
        [TestCase(PlayableEntityKind.Hawk,false)]
        public void HealthyArtilleryImmediatelySeeksGroundScreen(PlayableEntityKind cover,bool expected)
        {
            var ai=Profile();var micro=new AiCombatMicroPolicy();
            var o=View(10,new[]{Unit(1,PlayableEntityKind.Shkval,default),Unit(2,cover,new NavPoint(-2,3)),Unit(101,PlayableEntityKind.Tank,new NavPoint(6,0))});
            var registry=Army(o,ai,AiDifficulty.Veteran,out _);micro.Observe(o,P,ai,AiDifficulty.Veteran,registry);
            var plan=micro.Plan(o,P,ai,AiDifficulty.Veteran,registry);
            Assert.AreEqual(expected,plan!=null);
            if(expected){Assert.AreEqual(AiCombatMicroPhase.Withdraw,plan.Phase);Assert.Greater(Distance(plan.Action.Target,new NavPoint(6,0)),Distance(new NavPoint(-2,3),new NavPoint(6,0))+P.Navigation.ArrivalTolerance);}
        }

        [Test]
        public void ArtilleryWithdrawalClearsCoverSplashAndVisibleImpact()
        {
            var ai=Profile();var micro=new AiCombatMicroPolicy();var cover=new NavPoint(-.5,2);
            PlayableAiObservation At(long tick,int hp)=>View(tick,new[]{Unit(1,PlayableEntityKind.Tank,default,hp),Unit(2,PlayableEntityKind.Tank,cover),Unit(101,PlayableEntityKind.Shkval,new NavPoint(6,0))},Shot(tick,kind:PlayableEntityKind.Shkval,marker:new PlayableImpactMarker(new NavPoint(-1,0),.1,1)));
            var first=At(10,100);var registry=Army(first,ai,AiDifficulty.Veteran,out _);micro.Observe(first,P,ai,AiDifficulty.Veteran,registry);
            var wounded=At(40,50);micro.Observe(wounded,P,ai,AiDifficulty.Veteran,registry);var plan=micro.Plan(wounded,P,ai,AiDifficulty.Veteran,registry);
            Assert.NotNull(plan);double safe=PlayableUnitRules.ShkvalBlastRadius(P,false)+P.TankCollisionRadius;
            Assert.Greater(Distance(plan.Action.Target,cover),safe);Assert.Greater(Distance(plan.Action.Target,new NavPoint(-1,0)),safe);
            Assert.False(micro.RotationReturnSafe(wounded,P,ai,AiDifficulty.Veteran,wounded.Entities[0],default));
        }

        [TestCase(PlayableEntityKind.Tank,AiDifficulty.Recruit)]
        [TestCase(PlayableEntityKind.Explorer,AiDifficulty.Fighter)]
        [TestCase(PlayableEntityKind.Shkval,AiDifficulty.Veteran)]
        [TestCase(PlayableEntityKind.Hawk,AiDifficulty.Expert)]
        public void SwitchedFireReturnsEveryKindToShootingWithoutExtraTravel(PlayableEntityKind kind,AiDifficulty difficulty)
        {
            var ai=Profile();var micro=new AiCombatMicroPolicy();var origin=View(30,new[]{Unit(1,kind,default,50),Unit(2,PlayableEntityKind.Explorer,new NavPoint(2,2)),Unit(101,PlayableEntityKind.Explorer,new NavPoint(6,0))});
            var registry=Army(origin,ai,difficulty,out long army);
            var action=new PlayableAiAction(1,origin.OwnerId,P.ProfileId,P.Revision,Gen,30,PlayableCommandKind.Move,new[]{1},default,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            micro.Commit(new AiCombatMicroPlan{Action=action,Phase=AiCombatMicroPhase.Withdraw,Army=army},Gen,30);
            var receipt=new AiReceiptIdentity(Gen,origin.OwnerId,1,1);micro.Applied(action,12,40,receipt);
            micro.Receipt(new PlayableAiTraceRecord(origin.Identity,1,40,12,40,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"withdrawal",origin.OwnerId,action.SourceIdentity,receipt,AiCombatMicroPolicy.Policy,action.Kind));
            var stamp=new PlayableOrderStamp(1,PlayableOwner.Player,Gen,1,12,40,PlayableOrderOrigin.Ai,action.Kind,action.SourceIdentity,1,1);
            var units=origin.Entities.Select(u=>u.Id==1?Unit(1,kind,default,50,stamp):u).ToArray();
            micro.Observe(View(45,units,Shot(45)),P,ai,difficulty,registry);
            Assert.IsNull(micro.PlanSwitchedReturn(View(49,units),P,ai,difficulty,registry));
            var current=View(51,units,Shot(50,2));var plan=micro.PlanSwitchedReturn(current,P,ai,difficulty,registry);
            Assert.NotNull(plan);Assert.AreEqual(AiCombatMicroPhase.Resume,plan.Phase);Assert.AreEqual(default(NavPoint),plan.Action.Target);
            Assert.True(micro.Authorizes(new AiIntent("rotation",AiCombatMicroPolicy.Policy,plan.Action,120,0,0,Array.Empty<string>()),current,P,ai,difficulty,registry));
        }

        [Test]
        public void LatestObservedVictimReplacesOldAttackerWithoutForgettingFireHistory()
        {
            var ai=Profile();var micro=new AiCombatMicroPolicy();
            var units=new[]{Unit(1,PlayableEntityKind.Tank,default,50),Unit(2,PlayableEntityKind.Tank,new NavPoint(-.5,2)),Unit(101,PlayableEntityKind.Tank,new NavPoint(6,0))};
            var first=View(40,units,Shot(40,kind:PlayableEntityKind.Tank));var registry=Army(first,ai,AiDifficulty.Veteran,out _);
            micro.Observe(first,P,ai,AiDifficulty.Veteran,registry);Assert.AreEqual(1,micro.ObservedAttackCount(first,units[0],P));
            // The old projectile may still be flying after a later shot at an ally.
            var switched=View(50,units,Shot(40,kind:PlayableEntityKind.Tank),Shot(50,2,PlayableEntityKind.Tank));
            micro.Observe(switched,P,ai,AiDifficulty.Veteran,registry);
            Assert.Zero(micro.ObservedAttackCount(switched,units[0],P));Assert.AreEqual(1,micro.ObservedAttackCount(switched,units[1],P));
            var again=View(60,units,Shot(60,kind:PlayableEntityKind.Tank));micro.Observe(again,P,ai,AiDifficulty.Veteran,registry);
            Assert.AreEqual(1,micro.ObservedAttackCount(again,units[0],P));Assert.Zero(micro.ObservedAttackCount(again,units[1],P));
        }

        [TestCase(PlayableEntityKind.Tank)]
        [TestCase(PlayableEntityKind.Explorer)]
        [TestCase(PlayableEntityKind.Shkval)]
        [TestCase(PlayableEntityKind.Hawk)]
        public void CompletedWithdrawalKeepsAutoFirePreparationAndReleasesOnActualShot(PlayableEntityKind kind)
        {
            var ai=Profile();var micro=new AiCombatMicroPolicy();var origin=View(30,new[]{Unit(1,kind,default,50),Unit(2,PlayableEntityKind.Explorer,new NavPoint(2,2)),Unit(101,PlayableEntityKind.Explorer,new NavPoint(6,0))});
            var registry=Army(origin,ai,AiDifficulty.Veteran,out long army);
            var action=new PlayableAiAction(1,origin.OwnerId,P.ProfileId,P.Revision,Gen,30,PlayableCommandKind.Move,new[]{1},default,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            micro.Commit(new AiCombatMicroPlan{Action=action,Phase=AiCombatMicroPhase.Withdraw,Army=army},Gen,30);
            var receipt=new AiReceiptIdentity(Gen,origin.OwnerId,1,1);micro.Applied(action,12,40,receipt);
            micro.Receipt(new PlayableAiTraceRecord(origin.Identity,1,40,12,40,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"withdrawal",origin.OwnerId,action.SourceIdentity,receipt,AiCombatMicroPolicy.Policy,action.Kind));
            var stamp=new PlayableOrderStamp(1,PlayableOwner.Player,Gen,1,12,40,PlayableOrderOrigin.Ai,action.Kind,action.SourceIdentity,1,1);
            Assert.True(micro.IsAppliedWithdrawal(stamp));
            var replaced=new PlayableOrderStamp(1,PlayableOwner.Player,Gen,2,13,41,PlayableOrderOrigin.Human,action.Kind,null,0,0);
            Assert.False(micro.IsAppliedWithdrawal(replaced));
            var stopped=new PlayableEntitySnapshot(1,PlayableOwner.Player,kind,default,50,false,101,0,0,orderStamp:stamp,maxHealth:100,navigationOutcome:NavigationOutcome.Arrived);
            var units=origin.Entities.Select(u=>u.Id==1?stopped:u).ToArray();
            micro.Observe(View(45,units,Shot(45)),P,ai,AiDifficulty.Veteran,registry);
            var current=View(51,units,Shot(50,2));micro.Observe(current,P,ai,AiDifficulty.Veteran,registry);
            Assert.IsNull(micro.PlanSwitchedReturn(current,P,ai,AiDifficulty.Veteran,registry),"Do not reset a weapon already preparing ordinary fire");
            Assert.IsNull(micro.Plan(current,P,ai,AiDifficulty.Veteran,registry));Assert.True(micro.Reserves(1));
            micro.ReturnShot(stamp,origin.OwnerId,52,ai,AiDifficulty.Veteran);Assert.True(micro.Reserves(1),"Moving fire does not complete withdrawal");
            AiCombatMicroPolicy detached;
            using(var reader=new System.IO.BinaryReader(new System.IO.MemoryStream(WorldWire.Pack(micro.Write))))detached=AiCombatMicroPolicy.Read(reader,52,Gen);
            Assert.False(detached.MissedRelease(micro,52,ai,AiDifficulty.Veteran));
            micro.ReturnShot(stamp,origin.OwnerId,53,ai,AiDifficulty.Veteran,completedWithdrawal:true);Assert.False(micro.Reserves(1));
            Assert.True(detached.MissedRelease(micro,52,ai,AiDifficulty.Veteran),"Detached review must not resurrect completed custody");
            Assert.False(micro.IsAppliedWithdrawal(stamp));
        }

        [Test]
        public void DamageRateDoesNotInflateWhenMicroReviewsBetweenHits()
        {
            var ai=Profile();
            PlayableAiObservation At(long tick,int hp)=>View(tick,new[]{Unit(1,PlayableEntityKind.Tank,default,hp),Unit(2,PlayableEntityKind.Tank,new NavPoint(-.5,2)),Unit(101,PlayableEntityKind.Tank,new NavPoint(6,0))},Shot(tick,kind:PlayableEntityKind.Tank));
            double Rate(bool frequent)
            {
                var micro=new AiCombatMicroPolicy();var first=At(10,100);var registry=Army(first,ai,AiDifficulty.Veteran,out _);
                micro.Observe(first,P,ai,AiDifficulty.Veteran,registry);micro.Observe(At(20,92),P,ai,AiDifficulty.Veteran,registry);
                if(frequent)for(int t=21;t<30;t++)micro.Observe(At(t,92),P,ai,AiDifficulty.Veteran,registry);
                micro.Observe(At(30,84),P,ai,AiDifficulty.Veteran,registry);
                const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
                var samples=(System.Collections.IDictionary)typeof(AiCombatMicroPolicy).GetField("samples",flags).GetValue(micro);
                return (double)samples[1].GetType().GetField("Loss",flags).GetValue(samples[1]);
            }
            Assert.AreEqual(48,Rate(false));Assert.AreEqual(Rate(false),Rate(true));
        }

        [Test]
        public void StaggeredVolleyDoesNotVetoAnAvailableShortWithdrawal()
        {
            var ai=Profile();
            var micro=new AiCombatMicroPolicy();
            PlayableAiObservation At(long tick,int hp)=>View(tick,new[]{
                Unit(1,PlayableEntityKind.Tank,default,hp),
                Unit(2,PlayableEntityKind.Tank,new NavPoint(-.5,2)),
                Unit(101,PlayableEntityKind.Tank,new NavPoint(6,0))},Shot(tick,kind:PlayableEntityKind.Tank));
            var first=At(39,100);
            var registry=Army(first,ai,AiDifficulty.Veteran,out _);
            foreach(var sample in new[]{(39,100),(50,92),(52,76),(78,68),(80,52)})
                micro.Observe(At(sample.Item1,sample.Item2),P,ai,AiDifficulty.Veteran,registry);
            var wounded=At(80,52);
            var plan=micro.Plan(wounded,P,ai,AiDifficulty.Veteran,registry);
            Assert.AreEqual(AiCombatMicroPhase.Withdraw,plan?.Phase);
        }

        [TestCase(4,false)]
        [TestCase(12,true)]
        public void WoundedTankMustHaveTimeForPaidReactionAndReverse(int remaining,bool escapes)
        {
            var ai=Profile();
            var micro=new AiCombatMicroPolicy();
            PlayableAiObservation At(long tick,int hp)=>View(tick,new[]{
                Unit(1,PlayableEntityKind.Tank,default,hp),
                Unit(2,PlayableEntityKind.Tank,new NavPoint(0,2)),
                Unit(101,PlayableEntityKind.Tank,new NavPoint(6,0))},Shot(tick,kind:PlayableEntityKind.Tank));
            var start=At(10,remaining+48);
            var registry=Army(start,ai,AiDifficulty.Veteran,out _);
            micro.Observe(start,P,ai,AiDifficulty.Veteran,registry);
            for(int hit=1;hit<=6;hit++)
                micro.Observe(At(10+28*hit,remaining+48-8*hit),P,ai,AiDifficulty.Veteran,registry);
            var plan=micro.Plan(At(178,remaining),P,ai,AiDifficulty.Veteran,registry);
            Assert.AreEqual(escapes?AiCombatMicroPhase.Withdraw:(AiCombatMicroPhase?)null,plan?.Phase);
        }

        [Test]
        public void EarlyWithdrawalSchedulesNormalPaidReactionWithoutWaitingForMacroDecision()
        {
            var ai=Profile();var first=View(10,new[]{Unit(1,PlayableEntityKind.Tank,default),Unit(2,PlayableEntityKind.Tank,new NavPoint(-.5,2)),Unit(101,PlayableEntityKind.Tank,new NavPoint(6,0))});
            var loop=new PlayableAiOwnerLoop(P,PlayableAiOpeningComposition.Initialize(7,first.OwnerId,aiProfile:ai),Gen,null,ai,AiDifficulty.Veteran);
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
            var registry=(AiArmyRegistry)typeof(PlayableAiOwnerLoop).GetField("armies",flags).GetValue(loop);
            Assert.True(registry.TryCreate(first,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2},out long army,requiredMinimum:1));
            Assert.True(registry.UpdateMission(army,AiArmyPlanner.Policy,AiArmyPhase.Engaging,10,"contact"));
            var micro=(AiCombatMicroPolicy)typeof(PlayableAiOwnerLoop).GetField("combatMicro",flags).GetValue(loop);
            micro.Observe(first,P,ai,AiDifficulty.Veteran,registry);
            var wounded=View(20,first.Entities.Select(u=>u.Id==1?Unit(1,PlayableEntityKind.Tank,default,52):u).ToArray(),Shot(20,kind:PlayableEntityKind.Tank));
            micro.Observe(wounded,P,ai,AiDifficulty.Veteran,registry);
            typeof(PlayableAiOwnerLoop).GetMethod("BindCombatMicro",flags).Invoke(loop,new object[]{wounded});
            typeof(PlayableAiOwnerLoop).GetMethod("TryEarlyCombatMicro",flags).Invoke(loop,new object[]{wounded,0L});
            var scheduled=loop.Checkpoint.Records.Single(r=>r.Status==PlayableAiDeliveryStatus.Scheduled);
            Assert.AreEqual(PlayableCommandKind.Move,scheduled.Kind);
            Assert.GreaterOrEqual(scheduled.DueTick-wounded.Tick,AiProfile.SecondsToTicks(ai.DifficultyValue(AiDifficulty.Veteran,"reactionSeconds"),30));
            Assert.LessOrEqual(scheduled.DueTick-wounded.Tick,AiProfile.SecondsToTicks(ai.DifficultyValue(AiDifficulty.Veteran,"control.reactionMaxSeconds"),30));
            Assert.Zero(loop.CombatAttention.Applied,"Scheduling does not grant or spend an applied order");
        }

        [Test]
        public void CoverBehindAVisibleSolidCannotReceiveDirectFire()
        {
            var ai=Profile();var micro=new AiCombatMicroPolicy();
            PlayableAiObservation At(long tick,int hp)
            {
                var source=View(tick,new[]{Unit(1,PlayableEntityKind.Tank,default,hp),Unit(2,PlayableEntityKind.Tank,new NavPoint(0,4)),Unit(101,PlayableEntityKind.Tank,new NavPoint(6,0))},Shot(tick,kind:PlayableEntityKind.Tank));
                var wall=new PlayableBuildingSnapshot(200,PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(3,2),400,1,0,0,default,phase:ConstructionPhase.Ready);
                return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,Gen,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,source.Entities.ToArray(),new[]{wall},new[]{Shot(tick,kind:PlayableEntityKind.Tank)},new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:source.Vision));
            }
            var first=At(10,100);var registry=Army(first,ai,AiDifficulty.Veteran,out _);micro.Observe(first,P,ai,AiDifficulty.Veteran,registry);
            var current=At(40,50);Assert.False(AiCombatMicroSafety.FireClear(current,P,current.Entities[2],current.Entities[1]));
            micro.Observe(current,P,ai,AiDifficulty.Veteran,registry);var plan=micro.Plan(current,P,ai,AiDifficulty.Veteran,registry);
            if(plan!=null)Assert.Greater(Distance(new NavPoint(6,0),plan.Action.Target),P.TankRange);
        }

        [Test]
        public void ImpactMarkersDoNotExposeHiddenShootersOrInvisibleGround()
        {
            var units=new[]{Unit(1,PlayableEntityKind.Tank,default)};
            var visible=new PlayableProjectileSnapshot(1,0,0,default,PlayableEntityKind.Shkval,faction:PlayableOwner.Enemy,visible:false,marker:new PlayableImpactMarker(default,1,1));
            var hidden=new PlayableProjectileSnapshot(2,0,0,default,PlayableEntityKind.Shkval,faction:PlayableOwner.Enemy,marker:new PlayableImpactMarker(new NavPoint(45,45),1,1));
            var o=View(10,units,visible,hidden);Assert.IsEmpty(o.ObservedHostileShots);Assert.AreEqual(1,o.ObservedHostileImpacts.Count);Assert.AreEqual(default(NavPoint),o.ObservedHostileImpacts[0].Position);
        }
    }
}
