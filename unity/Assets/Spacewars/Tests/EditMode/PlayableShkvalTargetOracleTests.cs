using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableShkvalTargetOracleTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private static double Distance(NavPoint a,NavPoint b){double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
        private static BallisticBody Body(int id,double x,double z,PlayableOwner owner=PlayableOwner.Enemy,NavPoint velocity=default(NavPoint),bool building=false,double height=2,double baseHeight=0)
            =>new BallisticBody(id,owner,new NavPoint(x,z),velocity,1,height,100,building,baseHeight);
        private static bool Hostile(PlayableOwner a,PlayableOwner b)=>(a==PlayableOwner.Enemy)!=(b==PlayableOwner.Enemy);
        private sealed class ReferenceResult{internal int Target,Candidates,PredictionArrays,PredictionElements;}
        // Full current observed-set selector oracle. Geometry/flight primitives are shared;
        // candidate iteration, score accumulation and ranking are independently explicit.
        private static ReferenceResult Reference(IReadOnlyList<BallisticBody> bodies,bool guided=false,IReadOnlyList<NavObstacle> solids=null,Func<NavPoint,double> terrain=null)
        {
            solids=solids??Array.Empty<NavObstacle>();var result=new ReferenceResult();double bestScore=0,bestDistance=double.PositiveInfinity;
            foreach(var target in bodies){
                if(!Hostile(target.Owner,PlayableOwner.Player))continue;double d=Distance(default(NavPoint),target.Position);if(d>13)continue;result.Candidates++;
                var flight=guided?PlayableBallistics.Aim(default(NavPoint),target.Position,target.Velocity,1.15,4,12,true,2,1,2,terrain):new BallisticFlight(new BallisticPoint(0,(terrain?.Invoke(default(NavPoint))??0)+1.15,0),new BallisticPoint(target.Position.X,target.BaseHeight,target.Position.Z),4,12);
                var hit=PlayableBallistics.FirstContact(flight,.09,solids,1.74,bodies,0,terrain:terrain).Value;
                var predicted=new BallisticBody[bodies.Count];result.PredictionArrays++;result.PredictionElements+=predicted.Length;
                for(int i=0;i<predicted.Length;i++)predicted[i]=bodies[i].Predict(flight.Duration*hit.Progress,terrain);
                double score=0;foreach(var body in predicted)if(PlayableBallistics.BlastHits(hit.Point,1.4,body,solids,1.74,predicted,terrain))score+=Math.Min(body.Health,10)*(Hostile(body.Owner,PlayableOwner.Player)?1:-2);
                bool wins=score>bestScore||score>0&&score==bestScore&&(d<bestDistance||d==bestDistance&&String.CompareOrdinal(target.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),result.Target.ToString(System.Globalization.CultureInfo.InvariantCulture))<0);
                if(wins){result.Target=target.Id;bestScore=score;bestDistance=d;}
            }
            return result;
        }
        private static int Pick(IReadOnlyList<BallisticBody> bodies,bool guided=false,IReadOnlyList<NavObstacle> solids=null,Func<NavPoint,double> terrain=null)
            =>PlayableBallistics.BestTarget(default(NavPoint),PlayableOwner.Player,13,1.15,4,12,.09,1.4,10,2,solids??Array.Empty<NavObstacle>(),1.74,bodies,terrain,guided,2,1,2,Hostile);
        [TestCase(false)][TestCase(true)] public void FullObservedReferenceKeepsForecastInterceptionShieldingAndRanking(bool guided)
        {
            var bodies=new[]{Body(1,0,0,PlayableOwner.Player),Body(10,10,3,baseHeight:2),Body(2,10,-3,velocity:new NavPoint(.4,0),baseHeight:2),Body(7,13.5,0,PlayableOwner.Third,new NavPoint(-2,0),baseHeight:2),Body(9,7,0,building:true,height:6),Body(12,9,0,baseHeight:2)};
            var solids=new[]{new NavObstacle(4,-.2,4.2,.2)};Func<NavPoint,double> terrain=p=>p.X>=8?2:0;
            Assert.AreEqual(Reference(bodies,guided,solids,terrain).Target,Pick(bodies,guided,solids,terrain));
            var ties=new[]{Body(1,0,0,PlayableOwner.Player),Body(2,10,-3),Body(10,10,3)};
            Assert.AreEqual(10,Reference(ties,guided).Target);Assert.AreEqual(10,Pick(ties,guided));
            Assert.AreEqual(Reference(ties.Reverse().ToArray(),guided).Target,Pick(ties.Reverse().ToArray(),guided));
        }
        [Test] public void FriendlyOutsideCurrentRangeCanReverseAutomaticChoiceAtImpactTime()
        {
            var flight=new BallisticFlight(new BallisticPoint(0,1.15,0),new BallisticPoint(10,0,0),4,12);
            var baseBodies=new[]{Body(1,0,0,PlayableOwner.Player),Body(2,10,0)};
            double time=flight.Duration*PlayableBallistics.FirstContact(flight,.09,Array.Empty<NavObstacle>(),1.74,baseBodies,0).Value.Progress;
            var full=baseBodies.Concat(new[]{Body(7,13.5,0,PlayableOwner.Player,new NavPoint(-3.5/time,0))}).ToArray();
            Assert.Greater(Distance(default(NavPoint),full[2].Position),13);Assert.AreEqual(0,Pick(full));
            var clipped=full.Where(b=>Distance(default(NavPoint),b.Position)<=13).ToArray();Assert.AreEqual(2,Pick(clipped));
            Assert.AreEqual(Reference(full).Target,Pick(full));
        }
        // BestTarget's single Count access supplies the sole prediction-array newarr.
        // This list only exposes IReadOnlyList, so geometry enumerations do not count as constructions.
        private sealed class ConstructionProbe : IReadOnlyList<BallisticBody>
        {
            private readonly IReadOnlyList<BallisticBody> bodies;internal int CountReads;
            internal ConstructionProbe(IReadOnlyList<BallisticBody> bodies){this.bodies=bodies;}
            public int Count{get{CountReads++;return bodies.Count;}}
            public BallisticBody this[int i]=>bodies[i];
            public IEnumerator<BallisticBody> GetEnumerator()=>bodies.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        private static void VerifyConstructionProbeIl()
        {
            var method=typeof(PlayableBallistics).GetMethod("BestTarget");var bytes=method.GetMethodBody().GetILAsByteArray();
            var map=typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(System.Reflection.Emit.OpCode)).Select(f=>(System.Reflection.Emit.OpCode)f.GetValue(null)).ToDictionary(op=>op.Value);
            int at=0,arrays=0,counts=0;System.Reflection.Emit.OpCode previous=default(System.Reflection.Emit.OpCode);MethodBase previousMethod=null;
            while(at<bytes.Length){short value=bytes[at++]==0xfe?(short)(0xfe00|bytes[at++]):(short)bytes[at-1];var op=map[value];int size=0;MethodBase target=null;
                switch(op.OperandType){case System.Reflection.Emit.OperandType.InlineNone:break;
                    case System.Reflection.Emit.OperandType.ShortInlineBrTarget:case System.Reflection.Emit.OperandType.ShortInlineI:case System.Reflection.Emit.OperandType.ShortInlineVar:size=1;break;
                    case System.Reflection.Emit.OperandType.InlineVar:size=2;break;
                    case System.Reflection.Emit.OperandType.InlineI8:case System.Reflection.Emit.OperandType.InlineR:size=8;break;
                    case System.Reflection.Emit.OperandType.InlineSwitch:size=4+4*BitConverter.ToInt32(bytes,at);break;
                    default:size=4;break;}
                if(op.OperandType==System.Reflection.Emit.OperandType.InlineMethod){target=method.Module.ResolveMethod(BitConverter.ToInt32(bytes,at));if(target.DeclaringType==typeof(IReadOnlyCollection<BallisticBody>)&&target.Name=="get_Count")counts++;}
                if(op==System.Reflection.Emit.OpCodes.Newarr){Assert.AreEqual(typeof(BallisticBody),method.Module.ResolveType(BitConverter.ToInt32(bytes,at)));arrays++;Assert.AreEqual(System.Reflection.Emit.OpCodes.Callvirt,previous);Assert.AreEqual("get_Count",previousMethod?.Name);Assert.AreEqual(typeof(IReadOnlyCollection<BallisticBody>),previousMethod?.DeclaringType);}
                previous=op;previousMethod=target;at+=size;
            }
            Assert.AreEqual(1,arrays,"Exactly one prediction array construction site");Assert.AreEqual(1,counts,"Probe Count accesses must identify only that construction site");
        }
        private static object allocationEscape;
        private static long ArrayBytes(int count){long before=GC.GetAllocatedBytesForCurrentThread();allocationEscape=new BallisticBody[count];long bytes=GC.GetAllocatedBytesForCurrentThread()-before;GC.KeepAlive(allocationEscape);allocationEscape=null;return bytes;}
        private static int ManagedBodyBytes(){var method=new System.Reflection.Emit.DynamicMethod("BallisticBodyManagedBytes",typeof(int),Type.EmptyTypes);var il=method.GetILGenerator();il.Emit(System.Reflection.Emit.OpCodes.Sizeof,typeof(BallisticBody));il.Emit(System.Reflection.Emit.OpCodes.Ret);return ((Func<int>)method.CreateDelegate(typeof(Func<int>)))();}
        [TestCase(0)][TestCase(1)][TestCase(4)][TestCase(8)] public void RecordStructuralPredictionAllocationOnFixedInputs(int candidates)
        {
            var bodies=new List<BallisticBody>{Body(1,0,0,PlayableOwner.Player),Body(40,25,0,PlayableOwner.Player),Body(41,26,0,PlayableOwner.Player),Body(42,27,0,PlayableOwner.Player)};
            for(int i=0;i<candidates;i++)bodies.Add(Body(10+i,8+i*.35,(i%2==0?-1:1)*2));
            Pick(bodies);ArrayBytes(0);ArrayBytes(1);ArrayBytes(bodies.Count);
            var reference=Reference(bodies);long header=ArrayBytes(0),one=ArrayBytes(1),array=ArrayBytes(bodies.Count);
            Func<PlayableOwner,PlayableOwner,bool> hostility=Hostile;var solids=Array.Empty<NavObstacle>();
            int stride=ManagedBodyBytes();VerifyConstructionProbeIl();var probe=new ConstructionProbe(bodies);
            long before=GC.GetAllocatedBytesForCurrentThread();int actual=PlayableBallistics.BestTarget(default(NavPoint),PlayableOwner.Player,13,1.15,4,12,.09,1.4,10,2,solids,1.74,probe,hostile:hostility);long total=GC.GetAllocatedBytesForCurrentThread()-before;
            Assert.AreEqual(reference.Target,actual);Assert.AreEqual(candidates,reference.Candidates);Assert.AreEqual(candidates,reference.PredictionArrays);
            Assert.AreEqual(candidates*bodies.Count,reference.PredictionElements);Assert.Greater(stride,0);Assert.AreEqual(candidates==0?0:1,probe.CountReads,"Production prediction constructions measured through verified IL Count site");
            bool counter=one>0&&array>0;
            TestContext.WriteLine("STRUCTURAL observed="+bodies.Count+" candidates="+candidates+" sourcePredictionArrays="+reference.PredictionArrays+" sourcePredictionElements="+reference.PredictionElements+" managedElementBytes="+stride+" sourcePredictionPayloadBytes="+(reference.PredictionElements*stride)+" productionPredictionArrays="+probe.CountReads+" productionPredictionElements="+(probe.CountReads*bodies.Count)+" productionPredictionPayloadBytes="+(probe.CountReads*bodies.Count*stride)+" allocationCounterAvailable="+counter+" escapedOneElementArrayCounter="+one+" calibratedHeaderBytes="+(counter?header.ToString():"unknown")+" sourcePredictionArrayBytes="+(counter?(reference.PredictionArrays*array).ToString():"unknown")+" actualBestTargetAllocatedBytes="+(counter?total.ToString():"unavailable")+"; no CPU/performance qualification");
        }
        [TestCase(false)][TestCase(true)] public void ExplicitShotAndCurrentCapturePreserveBaselineOrGuidedFlight(bool guided)
        {
            var profile=PlayableProfile.Default;const int seed=19261019;const string source="native-shkval-target-oracle-v1";
            var c=OfflineParticipantAuthorityTests.Config(2,false,seed);var costs=new double[c.Starts.Count,c.Starts.Count];for(int i=0;i<c.Starts.Count;i++)for(int j=0;j<c.Starts.Count;j++)costs[i,j]=c.RouteCost(i,j);
            var config=new OfflineMatchConfiguration(profile,source,c.MapIdentity,"native-current-shkval-capture",seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),costs,
                scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Shkval,new NavPoint(-13,-5),upgraded:guided),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-4,-5)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-4,-3.5))});
            var world=new PlayableDomain(profile,91,config);PlayableDomain restored=null;
            object Call(PlayableDomain d,string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,Hidden).Invoke(d,args);
            IDictionary Units(PlayableDomain d)=>(IDictionary)typeof(PlayableDomain).GetField("units",Hidden).GetValue(d);
            IList Shots(PlayableDomain d)=>(IList)typeof(PlayableDomain).GetField("projectiles",Hidden).GetValue(d);
            try{
                int Id(int scenario){var unit=Units(world).Values.Cast<object>().Single(u=>(int)u.GetType().GetField("ScenarioIndex").GetValue(u)==scenario);return (int)unit.GetType().GetField("Id").GetValue(unit);}
                int gun=Id(0),enemy=Id(1);
                foreach(object unit in Units(world).Values)unit.GetType().GetField("Reload").SetValue(unit,999d);
                var actor=Units(world)[gun];actor.GetType().GetField("Reload").SetValue(actor,0d);
                var victim=Units(world)[enemy];victim.GetType().GetField("Velocity").SetValue(victim,new NavPoint(.5,0));Call(world,"RefreshVision");
                Assert.AreEqual(0,(int)Call(world,"ArtilleryTarget",actor,new NavPoint(-13,-5)),"Automatic friendly-fire penalty must apply before explicit Attack");
                Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(91,1,c.Roster[0].Id,PlayableCommandKind.Attack,new[]{gun},targetId:enemy),out _));
                for(int tick=0;tick<60&&Shots(world).Count==0;tick++)Call(world,"AdvanceIndexedCombat",1d/30);
                Assert.AreEqual(1,Shots(world).Count,"Explicit Attack must override the automatic penalty and launch");
                var rocket=Shots(world)[0].GetType().GetField("Rocket").GetValue(Shots(world)[0]);var flight=(BallisticFlight)rocket.GetType().GetField("Flight").GetValue(rocket);
                var expected=guided?PlayableBallistics.Aim(new NavPoint(-13,-5),new NavPoint(-4,-5),new NavPoint(.5,0),profile.ShkvalLaunchHeight,profile.ShkvalArcHeight,profile.ShkvalProjectileSpeed,true,profile.ShkvalLeadSpeedThreshold,profile.ShkvalLeadFalloff,profile.ShkvalMaxLeadTimeSec):new BallisticFlight(new BallisticPoint(-13,profile.ShkvalLaunchHeight,-5),new BallisticPoint(-4,0,-5),profile.ShkvalArcHeight,profile.ShkvalProjectileSpeed);
                Assert.AreEqual(expected.Start,flight.Start);Assert.AreEqual(expected.End,flight.End);Assert.AreEqual(expected.Duration,flight.Duration);
                byte[] bytes=world.CaptureWorldBytes(seed,source);restored=PlayableDomain.RestoreConfiguredWorldBytes(bytes,profile,seed,source,config);
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(seed,source));Assert.AreEqual(PlayableWorldRestoreTests.Facts(world,true),PlayableWorldRestoreTests.Facts(restored,true));
                for(int tick=0;tick<40;tick++){
                    foreach(var d in new[]{world,restored}){Call(d,"AdvanceIndexedCombat",1d/30);Call(d,"AdvanceProjectiles",1d/30);}
                    Assert.AreEqual(PlayableWorldRestoreTests.Facts(world,true),PlayableWorldRestoreTests.Facts(restored,true),"Current Shkval authority suffix "+tick);
                }
                CollectionAssert.AreEqual(world.CaptureWorldBytes(seed,source),restored.CaptureWorldBytes(seed,source));
            }finally{foreach(var d in new[]{world,restored})if(d!=null){d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();}}
        }
        [Test,Timeout(120000)] public void TwoOrdinaryShkvalsMoveChooseLaunchAndResumeAfterTargetLoss()
        {
            var c=OfflineParticipantAuthorityTests.Config(2,false,19261019);var costs=new double[c.Starts.Count,c.Starts.Count];for(int i=0;i<c.Starts.Count;i++)for(int j=0;j<c.Starts.Count;j++)costs[i,j]=c.RouteCost(i,j);
            var origins=new[]{new NavPoint(-18,6),new NavPoint(-18,10)};var goals=new[]{new NavPoint(5,6),new NavPoint(5,10)};
            var config=new OfflineMatchConfiguration(c.Profile,"native-two-shkval-target-oracle-v1","native-flat-eight-starts-v1","UnityHostRouteService",c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),costs,
                scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Shkval,origins[0]),new OfflineScenarioUnit(1,PlayableEntityKind.Shkval,origins[1],upgraded:true),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(3,8))});
            using(var host=new Spacewars.Presentation.UnityHostRouteService())using(var runtime=new PlayableRuntime(config,91,startPaused:true)){
                void Service(){host.Service(runtime,c.Profile.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(host.CertifiedPublication?.Failure);}
                bool Until(Func<bool> condition,double seconds){var timer=System.Diagnostics.Stopwatch.StartNew();while(timer.Elapsed.TotalSeconds<seconds){Service();if(condition())return true;System.Threading.Thread.Sleep(1);}return false;}
                Assert.True(Until(()=>runtime.IsPrepared&&runtime.Latest.Paused,30));
                var guns=runtime.ParticipantView(c.Roster[0].Id).Entities.Where(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Shkval).OrderBy(e=>e.Id).ToArray();Assert.AreEqual(2,guns.Length);Assert.False(guns[0].Upgraded);Assert.True(guns[1].Upgraded);
                int enemy=runtime.ParticipantView(c.Roster[1].Id).Entities.Single(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Tank).Id;
                runtime.RequestPause(false);Assert.True(Until(()=>!runtime.Latest.Paused,5));
                Assert.True(runtime.TrySubmit(new PlayableCommand(91,1,c.Roster[1].Id,PlayableCommandKind.Hold,new[]{enemy}).AsHuman()).Accepted);
                for(int i=0;i<2;i++)Assert.True(runtime.TrySubmit(new PlayableCommand(91,i+1,c.Roster[0].Id,PlayableCommandKind.AttackMove,new[]{guns[i].Id},goals[i]).AsHuman()).Accepted);
                var applied=new bool[2];var motion=new long[]{-1,-1};var choice=new long[]{-1,-1};var shot=new long[]{-1,-1};var resume=new long[]{-1,-1};var stops=new NavPoint[2];bool retreat=false;
                Assert.True(Until(()=>{
                    foreach(var receipt in runtime.DrainReceipts())if(receipt.OwnerId==c.Roster[0].Id&&receipt.Sequence>=1&&receipt.Sequence<=2&&receipt.Status==PlayableCommandStatus.Applied)applied[receipt.Sequence-1]=true;
                    var view=runtime.ParticipantView(c.Roster[0].Id);
                    for(int i=0;i<2;i++){
                        var gun=view.Entities.SingleOrDefault(e=>e.Id==guns[i].Id);Assert.NotNull(gun,"Both ordinary Shkvals must survive");
                        if(motion[i]<0&&Distance(gun.Position,origins[i])>.05){motion[i]=view.Tick;TestContext.WriteLine("motion gun="+i+" tick="+view.Tick);}
                        if(choice[i]<0&&gun.TargetId==enemy){choice[i]=view.Tick;TestContext.WriteLine("choice gun="+i+" tick="+view.Tick);}
                        if(shot[i]<0&&view.Projectiles.Any(p=>p.OwnerId==gun.Id&&p.TargetId==enemy)){shot[i]=view.Tick;stops[i]=gun.Position;TestContext.WriteLine("launch gun="+i+" tick="+view.Tick);}
                        if(shot[i]>=0&&resume[i]<0&&gun.TargetId==0&&gun.Moving&&Distance(gun.Position,stops[i])>.2){resume[i]=view.Tick;TestContext.WriteLine("resume gun="+i+" tick="+view.Tick);}
                    }
                    if(!retreat&&shot.All(t=>t>=0)){Assert.True(runtime.TrySubmit(new PlayableCommand(91,2,c.Roster[1].Id,PlayableCommandKind.Move,new[]{enemy},new NavPoint(3,27)).AsHuman()).Accepted);retreat=true;}
                    return applied.All(x=>x)&&resume.All(t=>t>=0);
                },60),"Two ordinary Shkvals must move, choose/launch and resume after target loss");
                var final=runtime.ParticipantView(c.Roster[0].Id);
                for(int i=0;i<2;i++){Assert.Greater(choice[i],motion[i]);Assert.Greater(shot[i],choice[i]);Assert.Greater(resume[i],shot[i]);var gun=final.Entities.Single(e=>e.Id==guns[i].Id);Assert.AreEqual(PlayableTacticalOrderKind.AttackMove,gun.CurrentOrder.Kind);Assert.AreEqual(goals[i],gun.CurrentOrder.Destination);}
            }
        }
    }
}
