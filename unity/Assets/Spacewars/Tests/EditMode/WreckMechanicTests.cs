using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class WreckMechanicTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        PlayableDomain domain;PlayableProfile profile;long sequence;UnityHostRouteService routeHost;
        object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(domain,args);
        PlayableSnapshot View()=>domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        int Spawn(NavPoint p,PlayableEntityKind kind=PlayableEntityKind.Tank,PlayableOwner owner=PlayableOwner.Player)=>(int)Call("SpawnUnit",p,owner,kind);
        [SetUp] public void Setup(){profile=PlayableProfile.Default;domain=new PlayableDomain(profile,1,false);sequence=0;}
        [TearDown] public void Cleanup(){routeHost?.Dispose();domain?.DestroyOwnedNavigation();}
        void Recreate(PlayableProfileData data){domain.DestroyOwnedNavigation();profile=PlayableProfile.Create(data);domain=new PlayableDomain(profile,1,false);}
        PlayableCommandStatus Send(int id,PlayableCommandKind kind,NavPoint goal=default,int target=0)=>domain.Apply(new PlayableCommand(1,++sequence,"player-1",kind,new[]{id},goal,targetId:target),out _);
        void Deliver(){var nav=domain.Navigation;if(routeHost!=null){for(int i=0;i<20000&&nav.PendingCount>0;i++){routeHost.Service(new PlayableRouteBinding(domain.Geometry,profile,nav.Admission,nav.RoutePort),4);nav.ApplyResults();}Assert.Zero(nav.PendingCount,"Actual route service must finish the component request: "+routeHost.LastProjectionFailure);return;}while(nav.Requests.TryDequeue(out var request))nav.Answers.TryEnqueue(new NavigationAnswer(request,new SharedFlowRouter(request.Geometry,request.Profile).FindPath(request.Start,request.Goal)));nav.ApplyResults();}
        void Steps(int count){for(int i=0;i<count;i++){Deliver();domain.Step(1d/30d);}}
        void AgeAllWrecksPastProtection()
        {long until=View().Wrecks.Select(w=>w.InvulnerableUntilTick).DefaultIfEmpty(domain.Tick).Max();if(until>domain.Tick)Steps((int)(until-domain.Tick));}
        [TestCase(PlayableEntityKind.Tank)] [TestCase(PlayableEntityKind.Explorer)]
        [TestCase(PlayableEntityKind.Shkval)] [TestCase(PlayableEntityKind.Hawk)]
        public void FreshWreckConsumesHitsUntilExactlyOneSecondAfterReveal(PlayableEntityKind weapon)
        {
            int dead=Spawn(default);Call("Damage",dead,int.MaxValue);var wreck=View().Wrecks.Single();
            Assert.AreEqual(1,profile.WreckSpawnProtectionSeconds);Assert.AreEqual(wreck.RevealTick+30,wreck.InvulnerableUntilTick);
            var geometry=domain.Geometry;var before=domain.CaptureWorldBytes(7,"fresh-hit");
            Assert.True((bool)Call("DestroyWreck",dead,weapon),"Protected impact is consumed, never retained for retry");
            Assert.AreSame(geometry,domain.Geometry);Assert.Less(View().Wrecks.Single().DestroyedTick,0);
            CollectionAssert.AreEqual(before,domain.CaptureWorldBytes(7,"fresh-hit"));
            Steps((int)(wreck.InvulnerableUntilTick-domain.Tick-1));
            Assert.True((bool)Call("DestroyWreck",dead,weapon));Assert.True(View().Wrecks.Single().Blocks(domain.Tick));
            Steps(1);Assert.True((bool)Call("DestroyWreck",dead,weapon));
            Assert.AreEqual(domain.Tick,View().Wrecks.Single().DestroyedTick);Assert.True(domain.Geometry.IsFree(default,0));
        }
        [Test] public void ProtectionDeadlineSurvivesCurrentCheckpointWithoutRestarting()
        {
            int dead=Spawn(default);Call("Damage",dead,int.MaxValue);Steps(10);
            var before=View().Wrecks.Single();var bytes=domain.CaptureWorldBytes(7,"wreck-protection");
            var restored=PlayableDomain.RestoreWorldBytes(bytes,profile,7,"wreck-protection");
            try
            {
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"wreck-protection"));
                var saved=restored.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7).Wrecks.Single();
                Assert.AreEqual(before.InvulnerableUntilTick,saved.InvulnerableUntilTick);
                Assert.False(saved.Damageable(restored.Tick));
                for(long i=restored.Tick;i<saved.InvulnerableUntilTick;i++)restored.Step(1d/30);
                Assert.True(saved.Damageable(restored.Tick));
                Assert.True((bool)typeof(PlayableDomain).GetMethod("DestroyWreck",F).Invoke(restored,new object[]{dead,PlayableEntityKind.Tank}));
                Assert.True(restored.Geometry.IsFree(default,0));
            }
            finally{restored.DestroyOwnedNavigation();}
        }
        [Test] public void AlreadyFlyingTankRoundsAreConsumedWithoutBreakingFreshWreck()
        {
            int victim=Spawn(default,PlayableEntityKind.Tank,PlayableOwner.Enemy);Call("Damage",victim,(int)View().Entities.Single(e=>e.Id==victim).Health-1);
            int first=Spawn(new NavPoint(-6,0)),second=Spawn(new NavPoint(-6,2));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(first,PlayableCommandKind.Attack,target:victim));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(second,PlayableCommandKind.Attack,target:victim));
            for(int i=0;i<300&&View().Projectiles.Count(p=>p.TargetId==victim)<2;i++)Steps(1);
            Assert.GreaterOrEqual(View().Projectiles.Count(p=>p.TargetId==victim),2,"Two real rounds must already be in flight");
            for(int i=0;i<300&&!View().Wrecks.Any(w=>w.Id==victim);i++)Steps(1);
            Assert.True(View().Wrecks.Any(w=>w.Id==victim));var wreck=View().Wrecks.Single(w=>w.Id==victim);
            Assert.Less(domain.Tick,wreck.InvulnerableUntilTick);
            for(int i=0;i<300&&View().Projectiles.Any(p=>p.TargetId==victim);i++)Call("AdvanceProjectiles",1d/30);
            Assert.False(View().Projectiles.Any(p=>p.TargetId==victim),"Old projectiles are spent, not parked until protection ends");
            Assert.Less(View().Wrecks.Single(w=>w.Id==victim).DestroyedTick,0);Assert.False(domain.Geometry.IsFree(default,0));
        }
        [Test] public void ShkvalBlastConsumesProtectedWreckHitWithoutDestruction()
        {
            int dead=Spawn(default);Call("Damage",dead,int.MaxValue);int neighbor=Spawn(new NavPoint(1.6,0));Call("Damage",neighbor,int.MaxValue);
            int gun=Spawn(new NavPoint(-6,0),PlayableEntityKind.Shkval);Assert.AreEqual(PlayableCommandStatus.Applied,Send(gun,PlayableCommandKind.Attack,target:dead));
            // Launch at fixed authority time to isolate an impact inside the protection interval.
            for(int i=0;i<300&&!View().Projectiles.Any(p=>p.Kind==PlayableEntityKind.Shkval);i++)Call("AdvanceCombat",1d/30);
            Assert.True(View().Projectiles.Any(p=>p.Kind==PlayableEntityKind.Shkval));
            for(int i=0;i<300&&View().Projectiles.Any(p=>p.Kind==PlayableEntityKind.Shkval);i++)Call("AdvanceProjectiles",1d/30);
            Assert.False(View().Projectiles.Any(p=>p.Kind==PlayableEntityKind.Shkval));
            foreach(var wreck in View().Wrecks){Assert.Less(wreck.DestroyedTick,0);Assert.True(wreck.Blocks(domain.Tick));}
        }
        [Test] public void ReverseIdDeathsPublishCanonicalWreckOrderAndExactRestore()
        {
            int lower=Spawn(new NavPoint(-8,0)),higher=Spawn(new NavPoint(8,0));
            Assert.Less(lower,higher);
            Call("Damage",higher,int.MaxValue);Call("Damage",lower,int.MaxValue);
            var wrecks=View().Wrecks.Where(w=>w.Blocks(domain.Tick)).OrderBy(w=>w.Id).ToArray();
            Assert.AreEqual(2,wrecks.Length);
            var circles=domain.Geometry.Obstacles.Where(o=>o.CircleRadius>0).ToArray();
            Assert.AreEqual(2,circles.Length);
            for(int i=0;i<wrecks.Length;i++)Assert.True(circles[i].GeometryEquals(new NavObstacle(wrecks[i].Position,wrecks[i].Radius)),"canonical ID order, independent of death order");
            AssertLifecycleRoundtrip();
        }

        [TestCase(true)][TestCase(false)] public void RefusedLethalUnitHitPreservesWorldAndRetries(bool activeHost)
        {
            int victim=Spawn(default);int mover=Spawn(new NavPoint(-10,0));Assert.AreEqual(PlayableCommandStatus.Applied,Send(mover,PlayableCommandKind.Move,new NavPoint(10,0)));
            var before=domain.CaptureWorldBytes(7,"lethal-refusal");var geometry=domain.Geometry;var budget=domain.Navigation.PhysicalHostBudget;
            long physical=budget.PhysicalBytes;int denied=0;System.Threading.Thread worker=null;Exception workerError=null;
            using var reached=new System.Threading.ManualResetEventSlim();using var resume=new System.Threading.ManualResetEventSlim();
            bool Hit()=>(bool)Call("TryDamageWithOwner",victim,(double)int.MaxValue,0,(PlayableOwner?)PlayableOwner.Enemy,null,default(NavPoint),null);
            try{
                if(activeHost){worker=new System.Threading.Thread(()=>{try{long floor=domain.Navigation.RoutePort.BeginHostAllocation();try{reached.Set();if(!resume.Wait(5000))throw new TimeoutException("Owned allocation release");}finally{domain.Navigation.RoutePort.EndHostAllocation(floor);}}catch(Exception e){workerError=e;reached.Set();}});worker.Start();Assert.True(reached.Wait(5000));Assert.IsNull(workerError);}
                else budget.AllocationAdmission=bytes=>{denied++;return false;};
                Assert.False(Hit(),"Lethal hit must defer without throwing while wreck geometry cannot publish");
            }finally{budget.AllocationAdmission=null;resume.Set();if(worker!=null)Assert.True(worker.Join(5000));}
            Assert.IsNull(workerError);if(!activeHost)Assert.Greater(denied,0);
            Assert.AreSame(geometry,domain.Geometry);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            CollectionAssert.AreEqual(before,domain.CaptureWorldBytes(7,"lethal-refusal"),"No health, kill, sound, intent or wreck fact changes on refusal");
            Assert.True(Hit());Assert.False(View().Entities.Any(e=>e.Id==victim));Assert.True(View().Wrecks.Single().Blocks(domain.Tick));Assert.False(domain.Geometry.IsFree(default,0));
            var saved=domain.CaptureWorldBytes(7,"lethal-refusal");var restored=PlayableDomain.RestoreWorldBytes(saved,profile,7,"lethal-refusal");try{CollectionAssert.AreEqual(saved,restored.CaptureWorldBytes(7,"lethal-refusal"));}finally{restored.DestroyOwnedNavigation();}
        }

        [TestCase("draft-batch",true)][TestCase("removal",true)][TestCase("late-geometry",true)][TestCase("removal",false)][TestCase("held",true)]
        public void LethalRemovalAndGeometryRefuseAsOnePreparedHit(string stage,bool commanded)
        {
            int victim=Spawn(default),survivor=Spawn(new NavPoint(-10,0));
            if(commanded)Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(1,++sequence,"player-1",PlayableCommandKind.Move,new[]{victim,survivor},new NavPoint(10,0)),out _));
            if(stage=="held")Assert.AreEqual(PlayableCommandStatus.Applied,Send(victim,PlayableCommandKind.Hold));
            var nav=domain.Navigation;var budget=nav.PhysicalHostBudget;var geometry=domain.Geometry;
            byte[] before=domain.CaptureWorldBytes(7,"lethal-combined");long physical=budget.PhysicalBytes;int denied=0;
            bool Hit()=>(bool)typeof(PlayableDomain).GetMethod("TryDamageWithOwner",F).Invoke(domain,new object[]{victim,(double)int.MaxValue,0,null,null,default(NavPoint),null});
            try{
                if(stage=="late-geometry")nav.AfterGeometryRemovalPreparationForQa=()=>budget.AllocationAdmission=bytes=>{denied++;return false;};
                else budget.AllocationAdmission=bytes=>{if(new System.Diagnostics.StackTrace(false).GetFrames().Any(f=>f.GetMethod().Name==(stage=="draft-batch"?"CopyWithRebindMembersOwned":"PrepareGeometryRemovals"))){denied++;return false;}return true;};
                Assert.False(Hit(),"A refused removal or subsequent geometry projection must defer the entire lethal hit");
            }finally{nav.AfterGeometryRemovalPreparationForQa=null;budget.AllocationAdmission=null;}
            Assert.Greater(denied,0);Assert.AreSame(geometry,domain.Geometry);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            CollectionAssert.AreEqual(before,domain.CaptureWorldBytes(7,"lethal-combined"),"Refusal preserves world, geometry, actor, group, intent, receipt and current Nav bytes");
            Assert.True(Hit(),nav.LastGeometryPreparationFailure?.ToString());Assert.False(nav.Crowd.TryGet(victim,out _));Assert.True(nav.Crowd.TryGet(survivor,out _));
            Assert.AreEqual(1,View().Wrecks.Count(w=>w.Id==victim));Assert.False(View().Entities.Any(u=>u.Id==victim));
            var committed=domain.CaptureWorldBytes(7,"lethal-combined");
            var restored=PlayableDomain.RestoreWorldBytes(committed,profile,7,"lethal-combined");try{CollectionAssert.AreEqual(committed,restored.CaptureWorldBytes(7,"lethal-combined"));}finally{restored.DestroyOwnedNavigation();}
            if(commanded){routeHost=new UnityHostRouteService();Deliver();Assert.Zero(nav.PendingCount,"Survivor retry must retain current group membership identity");}
            Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }

        [Test] public void LethalProjectileRetainsItsHitOnRefusalAndConsumesItOnceOnRetry()
        {
            int victim=Spawn(default,PlayableEntityKind.Tank,PlayableOwner.Enemy);
            Call("Damage",victim,(int)View().Entities.Single(e=>e.Id==victim).Health-1);
            int gun=Spawn(new NavPoint(-6,0));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(gun,PlayableCommandKind.Attack,target:victim));
            int refused=0;var budget=domain.Navigation.PhysicalHostBudget;
            try{
                budget.AllocationAdmission=bytes=>{
                    if(new System.Diagnostics.StackTrace(false).GetFrames().Any(f=>f.GetMethod().Name=="TryRebuildGeometry")){refused++;return false;}return true;
                };
                for(int i=0;i<600&&refused==0;i++)Steps(1);
                Assert.Greater(refused,0,"An ordinary lethal projectile must reach wreck publication admission");
                Assert.AreEqual(1,View().Entities.Single(e=>e.Id==victim).Health);
                Assert.IsEmpty(View().Wrecks);Assert.True(View().Projectiles.Any(p=>p.OwnerId==gun&&p.TargetId==victim));
            }finally{budget.AllocationAdmission=null;}
            // Advance the retained impact itself, without allowing another shot or time step.
            Call("AdvanceProjectiles",1d/30d);
            Assert.False(View().Entities.Any(e=>e.Id==victim));Assert.AreEqual(1,View().Wrecks.Count(w=>w.Id==victim));
            Assert.False(View().Projectiles.Any(p=>p.OwnerId==gun&&p.TargetId==victim));
            var committed=domain.CaptureWorldBytes(7,"projectile-retry");Call("AdvanceProjectiles",0d);
            CollectionAssert.AreEqual(committed,domain.CaptureWorldBytes(7,"projectile-retry"),"Consumed impact cannot apply damage or destruction events twice");
            Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }

        [Test] public void ShkvalBlastRefusalPreservesEveryVictimAndRetriesTheWholeImpactOnce()
        {
            var data=profile.CopyData();data.shkvalBlastRadius=3;data.shkvalMarkerStartRadius=3;Recreate(data);
            int neighbor=Spawn(new NavPoint(0,1.6),PlayableEntityKind.Explorer,PlayableOwner.Enemy);
            int victim=Spawn(default,PlayableEntityKind.Tank,PlayableOwner.Enemy);
            Call("Damage",victim,(int)View().Entities.Single(e=>e.Id==victim).Health-1);
            int gun=Spawn(new NavPoint(-6,0),PlayableEntityKind.Shkval);
            // An older wreck with a higher ID must not precede later lower-ID
            // prospective deaths when the whole blast publishes atomically.
            int older=Spawn(new NavPoint(12,12));Call("Damage",older,int.MaxValue);
            Assert.Greater(older,victim);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(gun,PlayableCommandKind.Attack,target:victim));
            for(int i=0;i<600&&!View().Projectiles.Any(p=>p.Kind==PlayableEntityKind.Shkval);i++)Steps(1);
            Assert.True(View().Projectiles.Any(p=>p.Kind==PlayableEntityKind.Shkval),"Actual weapon must launch the impact under test");
            int denied=0;byte[] before=null;var budget=domain.Navigation.PhysicalHostBudget;
            double health=View().Entities.Single(e=>e.Id==neighbor).ExactHealth.Value;
            try{
                budget.AllocationAdmission=bytes=>{
                    if(new System.Diagnostics.StackTrace(false).GetFrames().Any(f=>f.GetMethod().Name=="TryRebuildGeometry")){denied++;return false;}return true;
                };
                for(int i=0;i<600&&denied==0;i++){
                    before=domain.CaptureWorldBytes(7,"blast-refusal");
                    Assert.DoesNotThrow(()=>Call("AdvanceProjectiles",1d/30d),"A refused blast must retain its entire impact rather than throwing after partial damage");
                }
                Assert.Greater(denied,0);CollectionAssert.AreEqual(before,domain.CaptureWorldBytes(7,"blast-refusal"),"Refusal cannot consume flight, health, kill, sound, impact or wreck facts");
                Assert.AreEqual(health,View().Entities.Single(e=>e.Id==neighbor).ExactHealth);Assert.AreEqual(1,View().Entities.Single(e=>e.Id==victim).Health);
            }finally{budget.AllocationAdmission=null;}
            Call("AdvanceProjectiles",1d/30d);
            Assert.False(View().Entities.Any(e=>e.Id==victim));Assert.AreEqual(1,View().Wrecks.Count(w=>w.Id==victim));
            Assert.Less(View().Entities.Single(e=>e.Id==neighbor).ExactHealth,health,"The same physical blast must also hit the non-target neighbor");
            Assert.False(View().Projectiles.Any(p=>p.OwnerId==gun));var committed=domain.CaptureWorldBytes(7,"blast-refusal");
            Call("AdvanceProjectiles",0d);CollectionAssert.AreEqual(committed,domain.CaptureWorldBytes(7,"blast-refusal"));
            var restored=PlayableDomain.RestoreWorldBytes(committed,profile,7,"blast-refusal");
            try{CollectionAssert.AreEqual(committed,restored.CaptureWorldBytes(7,"blast-refusal"));}finally{restored.DestroyOwnedNavigation();}
            Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }

        [Test] public void RefusedWreckDestructionPreservesExactWorldAndRetriesWithoutLeaking()=>CheckRefusedWreck(false);
        [Test] public void ActiveHostAllocationDefersWreckDestructionUntilItsBoundary()=>CheckRefusedWreck(true);
        private void CheckRefusedWreck(bool activeHost)
        {
            int dead=Spawn(default);Call("Damage",dead,int.MaxValue);AgeAllWrecksPastProtection();
            int mover=Spawn(new NavPoint(-10,0));Assert.AreEqual(PlayableCommandStatus.Applied,Send(mover,PlayableCommandKind.Move,new NavPoint(10,0)));
            var before=domain.CaptureWorldBytes(7,"wreck-refusal");var geometry=domain.Geometry;var budget=domain.Navigation.PhysicalHostBudget;
            long physical=budget.PhysicalBytes;int denied=0;System.Threading.Thread worker=null;Exception workerError=null;
            using var reached=new System.Threading.ManualResetEventSlim();using var resume=new System.Threading.ManualResetEventSlim();
            try{
                if(activeHost){
                    worker=new System.Threading.Thread(()=>{try{long floor=domain.Navigation.RoutePort.BeginHostAllocation();try{reached.Set();if(!resume.Wait(5000))throw new TimeoutException("Owned allocation boundary release");}finally{domain.Navigation.RoutePort.EndHostAllocation(floor);}}catch(Exception e){workerError=e;reached.Set();}});
                    worker.Start();Assert.True(reached.Wait(5000));Assert.IsNull(workerError);
                }else budget.AllocationAdmission=bytes=>{denied++;return false;};
                Assert.False((bool)Call("DestroyWreck",dead,PlayableEntityKind.Tank));
                StringAssert.Contains(activeHost?"active navigation host allocation boundary":"scope capacity refused",domain.Navigation.LastGeometryPreparationFailure.Message);
            }finally{budget.AllocationAdmission=null;resume.Set();if(worker!=null)Assert.True(worker.Join(5000));}
            Assert.IsNull(workerError);if(!activeHost)Assert.Greater(denied,0);
            Assert.AreSame(geometry,domain.Geometry);Assert.True(View().Wrecks.Single().Blocks(domain.Tick));Assert.Less(View().Wrecks.Single().DestroyedTick,0);
            Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            CollectionAssert.AreEqual(before,domain.CaptureWorldBytes(7,"wreck-refusal"));
            Assert.True((bool)Call("DestroyWreck",dead,PlayableEntityKind.Tank));Assert.True(domain.Geometry.IsFree(default,0));Assert.False(View().Wrecks.Single().Blocks(domain.Tick));
            var saved=domain.CaptureWorldBytes(7,"wreck-refusal");var restored=PlayableDomain.RestoreWorldBytes(saved,profile,7,"wreck-refusal");
            try{CollectionAssert.AreEqual(saved,restored.CaptureWorldBytes(7,"wreck-refusal"));}finally{restored.DestroyOwnedNavigation();}
        }
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Hawk)][TestCase(PlayableEntityKind.Shkval)]
        public void ProjectileRetriesRefusedWreckHitWithoutConsumingTheHit(PlayableEntityKind weapon)
        {
            int dead=Spawn(default);Call("Damage",dead,int.MaxValue);
            int neighbor=0;if(weapon==PlayableEntityKind.Shkval){neighbor=Spawn(new NavPoint(1.6,0));Call("Damage",neighbor,int.MaxValue);}
            AgeAllWrecksPastProtection();int gun=Spawn(new NavPoint(-6,0),weapon);if(weapon==PlayableEntityKind.Hawk)Steps(20);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(gun,PlayableCommandKind.Attack,target:dead));
            using(domain.Navigation.CaptureState()){}var geometry=domain.Geometry;var budget=domain.Navigation.PhysicalHostBudget;int refused=0;
            try{
                budget.AllocationAdmission=bytes=>{
                    if(new System.Diagnostics.StackTrace(false).GetFrames().Any(f=>f.GetMethod().Name=="TryRebuildGeometry")){refused++;return false;}return true;
                };
                for(int i=0;i<600&&refused==0;i++)Steps(1);
                Assert.Greater(refused,0,"The real projectile must reach geometry admission.");
                Assert.AreSame(geometry,domain.Geometry);Assert.True(View().Wrecks.All(w=>w.Blocks(domain.Tick)&&w.DestroyedTick<0));
                Assert.IsNotEmpty(View().Projectiles,"A refused hit must retain its projectile.");Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            }finally{budget.AllocationAdmission=null;}
            for(int i=0;i<600&&View().Wrecks.Any(w=>w.Id==dead&&w.DestroyedTick<0);i++)Steps(1);
            Assert.False(View().Wrecks.Any(w=>w.Id==dead&&w.Blocks(domain.Tick)));Assert.True(domain.Geometry.IsFree(default,0));
            if(neighbor!=0){Assert.False(View().Wrecks.Any(w=>w.Id==neighbor&&w.Blocks(domain.Tick)));Assert.True(domain.Geometry.IsFree(new NavPoint(1.6,0),0));}
        }
        [TestCase(true)][TestCase(false)] public void NaturalExpiryRefusalPreservesWorldAndRetries(bool activeHost)
        {
            NavPoint RetainedGoal(int id){using(var state=domain.Navigation.CaptureState())return state.RetainedGoals.Single(g=>g.Entity==id).Point;}
            int dead=Spawn(default);Call("Damage",dead,int.MaxValue);AgeAllWrecksPastProtection();
            int mover=Spawn(new NavPoint(-10,0));Assert.AreEqual(PlayableCommandStatus.Applied,Send(mover,PlayableCommandKind.Move,new NavPoint(10,0)));
            Assert.AreEqual(new NavPoint(10,0),RetainedGoal(mover),"The accepted pending move must retain its authoritative goal");
            typeof(PlayableDomain).GetProperty("Tick",F).SetValue(domain,View().Wrecks.Single().ExpireTick);
            RefuseLifecycle(activeHost);
            Assert.AreEqual(dead,View().Wrecks.Single().Id);Assert.False(domain.Geometry.IsFree(default,0));
            Call("AdvanceWrecks");Assert.IsEmpty(View().Wrecks);Assert.True(domain.Geometry.IsFree(default,0));
            Assert.AreEqual(new NavPoint(10,0),RetainedGoal(mover));
            AssertLifecycleRoundtrip();
        }
        [TestCase(true)][TestCase(false)] public void HawkLandingRefusalRetriesAfterScheduledTick(bool activeHost)
        {
            var data=profile.CopyData();data.hawkWreckChancePercent=100;Recreate(data);
            int hawk=Spawn(default,PlayableEntityKind.Hawk);Call("Damage",hawk,int.MaxValue);
            int mover=Spawn(new NavPoint(-10,6));Assert.AreEqual(PlayableCommandStatus.Applied,Send(mover,PlayableCommandKind.Move,new NavPoint(10,6)));
            var wreck=View().Wrecks.Single();typeof(PlayableDomain).GetProperty("Tick",F).SetValue(domain,wreck.LandTick);
            RefuseLifecycle(activeHost);Assert.True(domain.Geometry.IsFree(default,0));AssertLifecycleRoundtrip();
            // An unrelated publication must not silently admit the deferred landing.
            int other=Spawn(new NavPoint(10,10));Call("Damage",other,int.MaxValue);Assert.True(domain.Geometry.IsFree(default,0));
            typeof(PlayableDomain).GetProperty("Tick",F).SetValue(domain,wreck.LandTick+1);
            Call("AdvanceWrecks");Assert.False(domain.Geometry.IsFree(default,0));
            var landed=View().Wrecks.Single(w=>w.Id==hawk);Assert.AreEqual(wreck.LandTick,landed.LandTick);Assert.AreEqual(wreck.ExpireTick,landed.ExpireTick);
            int revision=domain.Geometry.Revision;Call("AdvanceWrecks");Assert.AreEqual(revision,domain.Geometry.Revision,"A committed landing must not publish again");AssertLifecycleRoundtrip();
        }
        [Test] public void DeferredHawkLandingRechecksNewOccupant()
        {
            var data=profile.CopyData();data.hawkWreckChancePercent=100;Recreate(data);
            int hawk=Spawn(default,PlayableEntityKind.Hawk);Call("Damage",hawk,int.MaxValue);
            int mover=Spawn(new NavPoint(-10,6));Assert.AreEqual(PlayableCommandStatus.Applied,Send(mover,PlayableCommandKind.Move,new NavPoint(10,6)));
            long landing=View().Wrecks.Single().LandTick;typeof(PlayableDomain).GetProperty("Tick",F).SetValue(domain,landing);
            RefuseLifecycle(false);Spawn(default);typeof(PlayableDomain).GetProperty("Tick",F).SetValue(domain,landing+1);
            Call("AdvanceWrecks");Assert.False(View().Wrecks.Single().HasGround);Assert.True(domain.Geometry.IsFree(default,0));AssertLifecycleRoundtrip();
        }
        private void RefuseLifecycle(bool activeHost)
        {
            var before=domain.CaptureWorldBytes(7,"wreck-lifecycle");var geometry=domain.Geometry;var budget=domain.Navigation.PhysicalHostBudget;
            long physical=budget.PhysicalBytes;int denied=0;System.Threading.Thread worker=null;Exception workerError=null;
            using var reached=new System.Threading.ManualResetEventSlim();using var resume=new System.Threading.ManualResetEventSlim();
            try{
                if(activeHost){worker=new System.Threading.Thread(()=>{try{long floor=domain.Navigation.RoutePort.BeginHostAllocation();try{reached.Set();if(!resume.Wait(5000))throw new TimeoutException("Owned allocation release");}finally{domain.Navigation.RoutePort.EndHostAllocation(floor);}}catch(Exception e){workerError=e;reached.Set();}});worker.Start();Assert.True(reached.Wait(5000));Assert.IsNull(workerError);}
                else budget.AllocationAdmission=bytes=>{denied++;return false;};
                Assert.DoesNotThrow(()=>Call("AdvanceWrecks"),"Busy geometry preparation must defer lifecycle publication");
            }finally{budget.AllocationAdmission=null;resume.Set();if(worker!=null)Assert.True(worker.Join(5000));}
            Assert.IsNull(workerError);if(!activeHost)Assert.Greater(denied,0);
            Assert.AreSame(geometry,domain.Geometry);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            CollectionAssert.AreEqual(before,domain.CaptureWorldBytes(7,"wreck-lifecycle"),"A refused lifecycle publication cannot consume any world state");
        }
        private void AssertLifecycleRoundtrip()
        {
            var bytes=domain.CaptureWorldBytes(7,"wreck-lifecycle");var restored=PlayableDomain.RestoreWorldBytes(bytes,profile,7,"wreck-lifecycle");
            try{CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"wreck-lifecycle"));}finally{restored.DestroyOwnedNavigation();}
        }
        [Test] public void GroundDeathBlocksDuringFlashThenSinksAndExpiresOnCapturedTicks()
        {
            var data=profile.CopyData();data.unitWreckLifetimeSeconds=.2;data.wreckSinkSeconds=.2;Recreate(data);
            int id=Spawn(default);Call("Damage",id,int.MaxValue);var w=View().Wrecks.Single();
            Assert.True(w.Blocks(0));Assert.False(domain.Geometry.IsFree(default,0));Assert.Greater(w.RevealTick,w.DeathTick);
            Steps((int)w.SinkTick);Assert.True(View().Wrecks.Single().Blocks(domain.Tick));
            Steps((int)(w.ExpireTick-domain.Tick));Assert.IsEmpty(View().Wrecks);Assert.True(domain.Geometry.IsFree(default,0));
        }
        [TestCase(PlayableEntityKind.Tank)] [TestCase(PlayableEntityKind.Explorer)] [TestCase(PlayableEntityKind.Shkval)] [TestCase(PlayableEntityKind.Hawk)]
        public void NoAutomaticWreckTargetButExplicitAttackDestroysWithOneHit(PlayableEntityKind weapon)
        {
            int dead=Spawn(default);Call("Damage",dead,int.MaxValue);int attacker=Spawn(new NavPoint(-6,0),weapon);
            Steps(20);Assert.True(View().Wrecks.Single().Blocks(domain.Tick));Assert.Zero(View().Entities.Single(e=>e.Id==attacker).TargetId);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(attacker,PlayableCommandKind.Attack,target:dead));
            for(int i=0;i<600&&View().Wrecks.Any(w=>w.Id==dead&&w.DestroyedTick<0);i++)Steps(1);
            Assert.False(View().Wrecks.Any(w=>w.Id==dead&&w.Blocks(domain.Tick)),"An explicitly targeted wreck must be removed by the weapon hit.");Assert.True(domain.Geometry.IsFree(default,0));
        }
        [Test] public void ShkvalAreaDamageAlsoDestroysANearbyNonTargetWreck()
        {
            int first=Spawn(default);Call("Damage",first,int.MaxValue);int second=Spawn(new NavPoint(1.6,0));Call("Damage",second,int.MaxValue);int far=Spawn(new NavPoint(12,0));Call("Damage",far,int.MaxValue);
            int gun=Spawn(new NavPoint(-6,0),PlayableEntityKind.Shkval);Assert.AreEqual(PlayableCommandStatus.Applied,Send(gun,PlayableCommandKind.Attack,target:first));
            for(int i=0;i<600&&View().Wrecks.Any(w=>w.Id==first&&w.DestroyedTick<0);i++)Steps(1);
            Assert.False(View().Wrecks.Any(w=>w.Id==first&&w.Blocks(domain.Tick)));Assert.False(View().Wrecks.Any(w=>w.Id==second&&w.Blocks(domain.Tick)),"Splash must clear the neighboring wreck without a separate Attack.");Assert.True(View().Wrecks.Any(w=>w.Id==far&&w.Blocks(domain.Tick)));
        }
        [Test] public void FogHidesTheWreckProjectionButNotItsPhysicalObstacle()
        {
            var data=profile.CopyData();data.headquartersVisionRange=1;data.explorerVision=1;Recreate(data);
            int id=Spawn(default,PlayableEntityKind.Tank,PlayableOwner.Enemy);Call("Damage",id,int.MaxValue);
            var hidden=domain.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player);Assert.IsEmpty(hidden.Wrecks);Assert.False(domain.Geometry.IsFree(default,0));
            Spawn(new NavPoint(-6,0));var seen=domain.PlayerSnapshot(2,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player);Assert.AreEqual(id,seen.Wrecks.Single().Id);
        }
        [Test] public void CurrentFormatRestorePreservesWreckChoicesAndRemainingLifecycleExactly()
        {
            int dead=Spawn(default);Call("Damage",dead,int.MaxValue);Steps(7);
            var bytes=domain.CaptureWorldBytes(7,"wreck-mechanic");var restored=PlayableDomain.RestoreWorldBytes(bytes,profile,7,"wreck-mechanic");
            try{CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"wreck-mechanic"));var a=View().Wrecks.Single();var b=restored.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7).Wrecks.Single();Assert.AreEqual(a.Variant,b.Variant);Assert.AreEqual(a.Burial,b.Burial);Assert.AreEqual(a.ExpireTick,b.ExpireTick);Assert.False(restored.Geometry.IsFree(a.Position,0));}finally{restored.DestroyOwnedNavigation();}
        }
        [Test] public void WreckAndActiveShkvalLaunchRestoreTogetherExactly()
        {
            int dead=Spawn(default);Call("Damage",dead,int.MaxValue);int gun=Spawn(new NavPoint(-6,0),PlayableEntityKind.Shkval);int enemy=Spawn(new NavPoint(5,4),PlayableEntityKind.Tank,PlayableOwner.Enemy);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(gun,PlayableCommandKind.Attack,target:enemy));
            for(int i=0;i<180&&!View().Projectiles.Any(p=>p.Kind==PlayableEntityKind.Shkval);i++)Steps(1);
            Assert.True(View().Projectiles.Any(p=>p.Kind==PlayableEntityKind.Shkval));Assert.NotNull(View().Entities.Single(e=>e.Id==gun).ShkvalCells);
            var bytes=domain.CaptureWorldBytes(7,"wreck-shkval");var restored=PlayableDomain.RestoreWorldBytes(bytes,profile,7,"wreck-shkval");
            try{CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"wreck-shkval"));var snapshot=restored.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);Assert.True(snapshot.Wrecks.Any(w=>w.Id==dead));Assert.NotNull(snapshot.Projectiles.First(p=>p.Kind==PlayableEntityKind.Shkval).Launch);}finally{restored.DestroyOwnedNavigation();}
        }
        [TestCase("resume")] [TestCase("stop")] [TestCase("hold")] [TestCase("replace")] [TestCase("follow")] public void FullyClosedPassageRetainsOrderAcrossRestoreAndResumesUnlessStopped(string action)
        {
            var data=profile.CopyData();data.unitWreckLifetimeSeconds=3;data.wreckSinkSeconds=.2;Recreate(data);
            var home=new[]{new NavPoint(-20,-20),new NavPoint(20,-20)};
            var sites=home.Select((p,i)=>new TerritorySite(i+1,PlayableBuildingKind.Headquarters,p,profile)).ToArray();
            var starts=home.Select((p,i)=>new OfflineStart("wreck-start-"+i,i+1,p,new NavPoint(p.X,-10),pin:i+1)).ToArray();
            var walls=new[]{new NavObstacle(-2,1.1,2,profile.ArenaHalfExtent),new NavObstacle(-2,-profile.ArenaHalfExtent,2,-1.1)};
            var configuration=new OfflineMatchConfiguration(profile,"wreck-wait-fixture","wreck-corridor","flat-explicit-walls",7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)},starts,sites,walls,new double[,]{{0,40},{40,0}});
            domain.DestroyOwnedNavigation();domain=new PlayableDomain(profile,1,configuration);
            int dead=Spawn(default);Call("Damage",dead,int.MaxValue);var start=new NavPoint(-8,0);int mover=Spawn(start);routeHost=new UnityHostRouteService();
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(mover,PlayableCommandKind.Move,new NavPoint(8,0)));Steps(5);
            Assert.AreEqual(NavigationOutcome.Waiting,View().Entities.Single(e=>e.Id==mover).NavigationOutcome);Assert.AreEqual(1,sequence);
            var saved=domain.CaptureWorldBytes(7,"wreck-wait");var restored=PlayableDomain.RestoreConfiguredWorldBytes(saved,profile,7,"wreck-wait",configuration);routeHost.Dispose();domain.DestroyOwnedNavigation();domain=restored;routeHost=new UnityHostRouteService();
            Assert.AreEqual(NavigationOutcome.Waiting,View().Entities.Single(e=>e.Id==mover).NavigationOutcome);
            if(action=="follow"){int anchor=Spawn(new NavPoint(-16,0));Assert.AreEqual(PlayableCommandStatus.Applied,Send(mover,PlayableCommandKind.Follow,target:anchor));Assert.AreNotEqual(NavigationOutcome.Waiting,View().Entities.Single(e=>e.Id==mover).NavigationOutcome);Assert.False((bool)Call("WaitingForWreck",mover),"Follow must clear the authoritative wreck wait immediately.");}
            if(action=="stop")Assert.AreEqual(PlayableCommandStatus.Applied,Send(mover,PlayableCommandKind.Stop));if(action=="hold")Assert.AreEqual(PlayableCommandStatus.Applied,Send(mover,PlayableCommandKind.Hold));if(action=="replace"){
                Assert.AreEqual(PlayableCommandStatus.Applied,Send(mover,PlayableCommandKind.Move,new NavPoint(-12,0)));
                var replacementBytes=domain.CaptureWorldBytes(7,"wreck-wait");var replacementWorld=PlayableDomain.RestoreConfiguredWorldBytes(replacementBytes,profile,7,"wreck-wait",configuration);routeHost.Dispose();domain.DestroyOwnedNavigation();domain=replacementWorld;routeHost=new UnityHostRouteService();
            }Steps(350);
            var actor=View().Entities.Single(e=>e.Id==mover);var queue=domain.TacticalQueueProjection(mover,PlayableOwner.Player);TestContext.WriteLine($"action={action} tick={domain.Tick} position={actor.Position.X:R},{actor.Position.Z:R} outcome={actor.NavigationOutcome} active={queue?.Active?.Kind} pending={queue?.Pending?.Kind} completion={queue?.Completion?.Order?.Sequence} current={actor.CurrentOrder?.CommandSequence} last={actor.OrderStamp?.Sequence}");if(action=="stop"||action=="hold"){Assert.AreEqual(start,actor.Position);Assert.AreEqual(action=="hold"?NavigationOutcome.Held:NavigationOutcome.Stopped,actor.NavigationOutcome);}else if(action=="replace"){Assert.NotNull(queue.Completion,"The replacement must have its own authoritative arrival proof even after later geometry maintenance.");Assert.AreEqual(2,queue.Completion.Order.Sequence);Assert.AreEqual(new NavPoint(-12,0),queue.Completion.OriginalLocation.Value.Position);Assert.Null(queue.Active);Assert.Null(queue.Pending);Assert.Less(actor.Position.X,-10);}else if(action=="follow"){Assert.AreNotEqual(NavigationOutcome.Waiting,actor.NavigationOutcome);Assert.Less(actor.Position.X,-12);}else Assert.AreEqual(NavigationOutcome.Arrived,actor.NavigationOutcome);
        }
        [Test] public void HawkChanceAndLandingOccupancyAreAuthoritative()
        {
            var data=profile.CopyData();data.hawkWreckChancePercent=100;Recreate(data);int hawk=Spawn(default,PlayableEntityKind.Hawk);Call("Damage",hawk,int.MaxValue);var w=View().Wrecks.Single();Assert.True(w.HasGround);Assert.False(w.Blocks(domain.Tick));Steps((int)w.LandTick);Assert.True(View().Wrecks.Single().Blocks(domain.Tick));
            int second=Spawn(new NavPoint(5,0),PlayableEntityKind.Hawk);Spawn(new NavPoint(5,0));Call("Damage",second,int.MaxValue);Assert.False(View().Wrecks.Single(x=>x.Id==second).HasGround);
        }
        [Test] public void OccupyingAHawkLandingSiteDuringFallAbortsToAnAirBurst()
        {
            var data=profile.CopyData();data.hawkWreckChancePercent=100;Recreate(data);int hawk=Spawn(default,PlayableEntityKind.Hawk);Call("Damage",hawk,int.MaxValue);long landing=View().Wrecks.Single().LandTick;int neighbor=Spawn(default);
            Steps((int)landing);var w=View().Wrecks.Single();Assert.False(w.HasGround);Assert.False(w.Blocks(domain.Tick));Assert.AreEqual(landing,w.RevealTick);Assert.Greater(w.ExpireTick,landing);Assert.True(View().Entities.Any(e=>e.Id==neighbor));
        }
        [Test] public void LabControlsHaveMetadataAndValidateBurialRange()
        {
            Assert.AreEqual(1,profile.WreckSpawnProtectionSeconds);Assert.AreEqual(30,profile.UnitWreckLifetimeSeconds);Assert.AreEqual(2,profile.UnitDeathSmokeSeconds);Assert.AreEqual(30,profile.HawkWreckChancePercent);
            foreach(var field in PlayableProfileMetadata.WreckFields){Assert.True(NativeBalanceFields.Editable(field));Assert.IsNotEmpty(field.Label);Assert.IsNotEmpty(field.Description);Assert.IsNotEmpty(field.Unit);Assert.Greater(field.Step,0);var d=profile.CopyData();Assert.Throws<ArgumentOutOfRangeException>(()=>field.Write(d,field.Maximum+field.Step));}
            var data=profile.CopyData();data.wreckBurialMin=.5;data.wreckBurialMax=.1;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
        }
    }
}
