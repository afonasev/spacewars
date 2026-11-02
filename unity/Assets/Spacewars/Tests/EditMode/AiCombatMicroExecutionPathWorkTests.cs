using System;
using System.IO;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class AiCombatMicroExecutionPathWorkTests
    {
        static PlayableProfile Profile()=>PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
        static PlayableBuildingSnapshot Tower()=>new PlayableBuildingSnapshot(100,PlayableOwner.Enemy,PlayableBuildingKind.SentryTurret,default,100,1,0,0,default);
        static PlayableEntitySnapshot Unit(double x)=>new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(x,0),100,false,0,0,0);
        static PlayableAiObservation View(PlayableProfile p,PlayableEntitySnapshot unit,PlayableBuildingSnapshot tower=null)=>PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,905,7,40,40,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,new[]{unit},new[]{tower??Tower()},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
        static NavLocation At(double x)=>new NavLocation(new NavPoint(x,0),"open-ground");
        static MotionExecutionPrimitive Primitive(PlayableProfile p,double from,double to,double end,double? via=null,double claimedHeight=0)
        {
            var binding=new MotionExecutionBinding(905,11,1,2,3,1,1,1,7,8,9,"player-1","route","profile","geometry",null,"owner-intent");long grant=10000;
            var legs=via.HasValue?new[]{new NavTypedLeg(At(from),At(to),Math.Abs(to-from),NavTypedLegKind.Surface,null),new NavTypedLeg(At(to),At(via.Value),Math.Abs(to-via.Value),NavTypedLegKind.Surface,null),new NavTypedLeg(At(via.Value),At(end),Math.Abs(end-via.Value),NavTypedLegKind.Surface,null)}:new[]{new NavTypedLeg(At(from),At(to),Math.Abs(to-from),NavTypedLegKind.Surface,null),new NavTypedLeg(At(to),At(end),Math.Abs(end-to),NavTypedLegKind.Surface,null)};
            Assert.True(MotionExecutionRemainingBranch.TryCreate(binding,At(from),At(end),0,0,legs,ref grant,_=>true,out var branch));var piece=new MotionExecutionTracePiece(MotionExecutionTraceKind.Surface,MotionExecutionLaw.AffineSupportChord,At(from),At(to),"open-ground",0,0,claimedHeight,0,1);
            Assert.True(MotionExecutionPrimitive.TryCreate(binding,1,MotionExecutionKind.BoundedLocalRepair,40,1d/30,At(from),At(to),At(end),PlayableUnitRules.Radius(p,PlayableEntityKind.Tank),"tank",0,0,1,11,7,new[]{piece},ref grant,_=>true,out var primitive,branch));return primitive;
        }
        static AiCombatMicroCoverScene Scene(PlayableProfile p,AiCombatMicroExecutionStorage storage,PlayableBuildingSnapshot tower=null,PlayableBuildingSnapshot blocker=null)
        {
            long grant=10000;AiCombatMicroExecutionStorage.Lease catalogRoot=null;
            Assert.True(AiCombatSupportCatalog.TryBegin(p.AuthoredMap,"exact-test-source",ref grant,bytes=>storage.TryReserve(bytes,out catalogRoot),out var builder));while(!builder.Ready)builder.Advance(1);var catalog=builder.Finish();
            try{Assert.True(AiCombatMicroCoverScene.TryCreate(p,catalog,catalogRoot,blocker==null?new[]{tower??Tower()}:new[]{tower??Tower(),blocker},Array.Empty<NavObstacle>(),ref grant,storage,out var scene));return scene;}finally{catalogRoot.Dispose();}
        }
        static AiCombatMicroExecutionPathWork Work(PlayableProfile p,AiCombatMicroCoverScene scene,AiCombatMicroExecutionStorage storage,double from,double to,double end,AiCombatMicroExecutionPathWork.Cursor cursor=null,double? via=null,double claimedHeight=0,PlayableBuildingSnapshot tower=null)
        {long grant=10000;var unit=Unit(from);Assert.True(AiCombatMicroExecutionPathWork.TryCreate(scene,storage,Primitive(p,from,to,end,via,claimedHeight),View(p,unit,tower),null,unit,true,"same-owned-job",cursor,ref grant,out var work));return work;}
        static byte[] Bytes(Action<BinaryWriter> write){using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))write(writer);return stream.ToArray();}}
        static bool Acquire(AiCombatMicroExecutionPathWork work,out AiCombatMicroExecutionPathWork.Cursor cursor){long grant=10000;return work.TryAcquireActualCursor(ref grant,out cursor);}
        static void Run(AiCombatMicroExecutionPathWork work){int cap=4000000;while(!work.Complete&&cap-->0)Assert.AreEqual(1,work.Advance(1));Assert.Greater(cap,0);}
        [TestCase(1d)]
        [TestCase(100d)]
        public void AirUsesActualAssignedHeightAndPreservesItsPaidColdCursor(double assignedHeight)
        {
            var p=Profile();AiCombatMicroSafety.PrepareExecutionAssets(p);var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;
            var from=new NavLocation(new NavPoint(8,0),"native-air");var goal=new NavLocation(new NavPoint(12,0),"native-air");
            var binding=new MotionExecutionBinding(905,0,0,0,3,1,0,0,0,8,8,"player-1","native-air-order","profile","geometry",null,"same-owned-job",MotionOwnIntentKind.Air);
            var law=new MotionExecutionAirStep(1,assignedHeight,new NavPoint(8.1,0),19,true,from.Position);long grant=10000;
            var piece=new MotionExecutionTracePiece(MotionExecutionTraceKind.Air,MotionExecutionLaw.NativeAssignedAltitude,from,from,null,0,0,0,0,1);
            Assert.True(MotionExecutionPrimitive.TryCreate(binding,1,MotionExecutionKind.AirDiscrete,40,1d/30,from,from,goal,p.HawkRadius,"hawk",0,0,0,0,0,new[]{piece},ref grant,_=>true,out var primitive,air:law));
            var patient=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Hawk,from.Position,100,false,0,0,0,altitude:1);
            var actual=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Hawk,from.Position,100,false,0,0,0,altitude:assignedHeight);
            var blocker=new PlayableBuildingSnapshot(101,PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(4,0),100,1,0,0,default);
            PlayableAiObservation Observed(PlayableEntitySnapshot unit)=>PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,905,7,40,40,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,new[]{unit},new[]{Tower(),blocker},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
            var o=Observed(patient);Assert.False(AiCombatMicroSafety.Covers(o,p,Tower(),patient,patient.Position),"The real building occludes the low source altitude.");
            bool covered=AiCombatMicroSafety.Covers(Observed(actual),p,Tower(),actual,actual.Position);
            Assert.AreEqual(assignedHeight==100,covered,"The assigned Native altitude changes actual AA coverage while XZ stays fixed.");
            using(var scene=Scene(p,storage,blocker:blocker)){
                Assert.True(AiCombatMicroExecutionPathWork.TryCreate(scene,storage,primitive,o,null,patient,true,"same-owned-job",null,ref grant,out var work));
                using(work){
                    work.Advance(3);var saved=Bytes(work.Write);AiCombatMicroExecutionPathWork cold;
                    using(var reader=new BinaryReader(new MemoryStream(saved)))cold=AiCombatMicroExecutionPathWork.Read(reader,scene,storage,primitive,ref grant);
                    using(cold){CollectionAssert.AreEqual(saved,Bytes(cold.Write));Run(work);Run(cold);Assert.True(work.Supported);Assert.AreEqual(!covered,work.Allowed);CollectionAssert.AreEqual(Bytes(work.Write),Bytes(cold.Write));}
                    if(work.Allowed){Assert.True(Acquire(work,out var cursor));using(cursor){Assert.AreEqual(assignedHeight,cursor.AirHeight);var bytes=Bytes(cursor.Write);using(var reader=new BinaryReader(new MemoryStream(bytes)))using(var restored=AiCombatMicroExecutionPathWork.Cursor.Read(reader,storage,ref grant))CollectionAssert.AreEqual(bytes,Bytes(restored.Write));}}
                }
            }
            Assert.AreEqual(baseline,storage.Retained);
        }
        [Test]public void ForecastExitDoesNotOverwriteStagedPartialEscapeAndVerifiedExitCannotReenter()
        {
            var p=Profile();AiCombatMicroSafety.PrepareExecutionAssets(p);double range=p.ExplorerRange*p.TurretRangeMultiplier;Assert.True(AiCombatMicroSafety.Covers(View(p,Unit(.8*range)),p,Tower(),Unit(.8*range),new NavPoint(.8*range,0)),"fixture begins in actual native fire coverage");
            var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;AiCombatMicroExecutionPathWork.Cursor partial=null,exited=null;
            using(var scene=Scene(p,storage))try{
                using(var first=Work(p,scene,storage,.8*range,.9*range,range+2)){Run(first);Assert.True(first.Supported);Assert.True(first.Allowed);Assert.AreEqual(range+2-.8*range,first.RemainingLength,1e-10);Assert.True(Acquire(first,out partial));Assert.True(partial.Entry(0).Escaping,"only forecast exited; planned actual partial step remains covered");Assert.Throws<InvalidOperationException>(()=>Acquire(first,out _));}
                using(var second=Work(p,scene,storage,.9*range,range+2,range+3,partial)){Run(second);Assert.True(second.Allowed);Assert.True(Acquire(second,out exited));Assert.False(exited.Entry(0).Escaping);}
                using(var reentry=Work(p,scene,storage,range+2,.8*range,range+3,exited)){Run(reentry);Assert.True(reentry.Supported);Assert.False(reentry.Allowed);Assert.Throws<InvalidOperationException>(()=>Acquire(reentry,out _));}
            }finally{partial?.Dispose();exited?.Dispose();}Assert.AreEqual(baseline,storage.Retained);
        }
        [Test]public void CoveredOutwardEndpointCannotBecomeAPhantomBackstepAfterInterpolation()
        {
            double from=-.5,to=.01;var tower=new PlayableBuildingSnapshot(100,PlayableOwner.Enemy,PlayableBuildingKind.SentryTurret,new NavPoint(-3.001,0),100,1,0,0,default);var p=Profile();AiCombatMicroSafety.PrepareExecutionAssets(p);
            double rounded=from+(to-from),roundedDistance=AiCombatMicroSafety.Distance(tower.Position,new NavPoint(rounded,0)),actualDistance=AiCombatMicroSafety.Distance(tower.Position,new NavPoint(to,0));Assert.Greater(roundedDistance,actualDistance,"source IEEE case must expose phantom outward interpolation");Assert.True(AiCombatMicroSafety.Covers(View(p,Unit(from),tower),p,tower,Unit(from),new NavPoint(from,0)),"actual source origin is covered");Assert.True(AiCombatMicroSafety.Covers(View(p,Unit(to),tower),p,tower,Unit(to),new NavPoint(to,0)),"actual outward endpoint remains covered");
            var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;using(var scene=Scene(p,storage,tower))using(var work=Work(p,scene,storage,from,to,10,tower:tower)){Run(work);Assert.True(work.Supported);Assert.True(work.Allowed);Assert.True(Acquire(work,out var cursor));using(cursor){Assert.True(cursor.Entry(0).Escaping);Assert.AreEqual(actualDistance,cursor.Entry(0).Distance);}}Assert.AreEqual(baseline,storage.Retained);
        }
        [Test]public void StagedDistanceUsesExactExecutionEndpointInsteadOfRoundedInterpolation()
        {
            const double from=120,to=.3;Assert.AreNotEqual(to,from+(to-from),"fixture exposes a real IEEE cancellation difference");var tower=new PlayableBuildingSnapshot(100,PlayableOwner.Enemy,PlayableBuildingKind.SentryTurret,new NavPoint(-20,0),100,1,0,0,default);var p=Profile();var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;
            using(var scene=Scene(p,storage,tower))using(var work=Work(p,scene,storage,from,to,1,tower:tower)){Run(work);Assert.True(work.Allowed);Assert.True(Acquire(work,out var cursor));using(cursor){Assert.AreEqual(to,cursor.At.Position.X);Assert.AreEqual(AiCombatMicroSafety.Distance(tower.Position,new NavPoint(to,0)),cursor.Entry(0).Distance);}}Assert.AreEqual(baseline,storage.Retained);
        }
        [Test]public void StagedCursorCopyIsPaidAndRetainsOnlyItsValidatedDataAfterWorkRelease()
        {
            var p=Profile();AiCombatMicroSafety.PrepareExecutionAssets(p);var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;AiCombatMicroExecutionPathWork.Cursor cursor;
            using(var scene=Scene(p,storage)){
                using(var work=Work(p,scene,storage,12,13,14)){Run(work);long before=storage.Retained,grant=0;Assert.False(work.TryAcquireActualCursor(ref grant,out _));Assert.Zero(grant);Assert.AreEqual(before,storage.Retained);grant=100;Assert.True(work.TryAcquireActualCursor(ref grant,out cursor));Assert.AreEqual(100-work.CursorPreparationWork,grant);}
                Assert.False(cursor.Disposed);Assert.AreEqual(13,cursor.At.Position.X);Assert.AreEqual(1,cursor.Count);
            }
            Assert.AreEqual(baseline+4096+64*cursor.Count+AiCombatMicroExecutionStorage.RootBytes+AiCombatMicroExecutionStorage.HandleBytes,storage.Retained,"cursor liability derives only from validated retained data");cursor.Dispose();Assert.AreEqual(baseline,storage.Retained);
        }
        [Test]public void ColdCommittedCursorDataHasExactBytesAndStateDerivedLiability()
        {
            var p=Profile();AiCombatMicroSafety.PrepareExecutionAssets(p);var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;AiCombatMicroExecutionPathWork.Cursor cursor;
            using(var scene=Scene(p,storage))using(var work=Work(p,scene,storage,12,13,14)){Run(work);Assert.True(Acquire(work,out cursor));}
            using(cursor){long live=storage.Retained;byte[] bytes=Bytes(cursor.Write);long grant=10000;AiCombatMicroExecutionPathWork.Cursor cold;using(var r=new BinaryReader(new MemoryStream(bytes)))cold=AiCombatMicroExecutionPathWork.Cursor.Read(r,storage,ref grant);using(cold){CollectionAssert.AreEqual(bytes,Bytes(cold.Write));Assert.AreEqual(live-baseline,storage.Retained-live,"cold root liability matches retained hot cursor data");Assert.AreEqual(cursor.Entry(0).Escaping,cold.Entry(0).Escaping);}Assert.AreEqual(live,storage.Retained);
                grant=10000;using(var r=new BinaryReader(new MemoryStream(bytes)))Assert.Throws<InvalidOperationException>(()=>AiCombatMicroExecutionPathWork.Cursor.Read(r,new AiCombatMicroExecutionStorage(128),ref grant));Assert.AreEqual(10000,grant);byte[] bad=(byte[])bytes.Clone();bad[bad.Length-24]^=1;grant=10000;using(var r=new BinaryReader(new MemoryStream(bad)))Assert.Throws<ArgumentException>(()=>AiCombatMicroExecutionPathWork.Cursor.Read(r,storage,ref grant));Assert.AreEqual(live,storage.Retained,"invalid cold cursor must release admitted private roots");
            }Assert.AreEqual(baseline,storage.Retained);
        }
        [Test]public void FullRemainingBranchRejectsFutureFireEvenWhenActualChordIsOutside()
        {
            var p=Profile();AiCombatMicroSafety.PrepareExecutionAssets(p);double range=p.ExplorerRange*p.TurretRangeMultiplier;var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;
            using(var scene=Scene(p,storage))using(var work=Work(p,scene,storage,range+3,range+2,range+4,via:.8*range)){Run(work);Assert.True(work.Supported);Assert.False(work.Allowed);}Assert.AreEqual(baseline,storage.Retained);
        }
        [Test]public void TypedPortalChordAndWholeRejoinSuffixKeepTheirPaidColdProofWithoutThreats()
        {
            var p=Profile();var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained,grant=100000;
            var catalog=new AiCombatSupportCatalog(AiCombatSupportSelector.FirstDeclared,"actual-retained-test-laws",new[]{new MapSupport("bridge",new NavObstacle(-1,-2,1,2),true,4,surfaceId:"bridge"),new MapSupport("ground",new NavObstacle(-20,-20,20,20),false,surfaceId:"ground")});
            Assert.True(storage.TryReserve(4096,out var root));
            AiCombatMicroCoverScene scene;try{Assert.True(AiCombatMicroCoverScene.TryCreate(p,catalog,root,Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<NavObstacle>(),ref grant,storage,out scene));}finally{root.Dispose();}
            NavLocation L(double x,string surface)=>new NavLocation(new NavPoint(x,0),surface);
            var a=L(-4,"ground");var b=L(-1,"ground");var c=L(-1,"bridge");var at=L(0,"bridge");var d=L(1,"bridge");var e=L(1,"ground");var end=L(4,"ground");
            var binding=new MotionExecutionBinding(905,11,1,2,3,1,1,1,7,8,9,"player-1","route","profile","geometry",null,"same-owned-job");
            var legs=new[]{new NavTypedLeg(a,b,3,NavTypedLegKind.Surface,null),new NavTypedLeg(b,c,0,NavTypedLegKind.Portal,"first-portal"),new NavTypedLeg(c,at,1,NavTypedLegKind.Surface,null),new NavTypedLeg(at,d,1,NavTypedLegKind.Surface,null),new NavTypedLeg(d,e,0,NavTypedLegKind.Portal,"second-portal"),new NavTypedLeg(e,end,3,NavTypedLegKind.Surface,null)};
            Assert.True(MotionExecutionRemainingBranch.TryCreate(binding,a,end,0,0,legs,ref grant,_=>true,out var branch));
            var trace=new[]{new MotionExecutionTracePiece(MotionExecutionTraceKind.Surface,MotionExecutionLaw.AffineSupportChord,a,b,"ground",0,0,0,0,.5),new MotionExecutionTracePiece(MotionExecutionTraceKind.Portal,MotionExecutionLaw.SemanticPortalCrossing,b,c,null,0,0,0,.5,.5,"first-portal"),new MotionExecutionTracePiece(MotionExecutionTraceKind.Surface,MotionExecutionLaw.AffineSupportChord,c,at,"bridge",0,0,4,.5,1)};
            Assert.True(MotionExecutionPrimitive.TryCreate(binding,1,MotionExecutionKind.AttachmentShortcutRejoin,40,1d/30,a,at,end,PlayableUnitRules.Radius(p,PlayableEntityKind.Tank),"tank",0,0,0,0,7,trace,ref grant,_=>true,out var primitive,branch));
            var unit=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,a.Position,100,false,0,0,0);
            var observation=PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,905,7,40,40,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,new[]{unit},Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
            using(scene){Assert.True(AiCombatMicroExecutionPathWork.TryCreate(scene,storage,primitive,observation,null,unit,false,"same-owned-job",null,ref grant,out var hot));using(hot){int cap=10000;while(!hot.Complete&&cap-->0){
                var bytes=Bytes(hot.Write);using(var r=new BinaryReader(new MemoryStream(bytes)))using(var cold=AiCombatMicroExecutionPathWork.Read(r,scene,storage,primitive,ref grant)){CollectionAssert.AreEqual(bytes,Bytes(cold.Write));hot.Advance(1);cold.Advance(1);CollectionAssert.AreEqual(Bytes(hot.Write),Bytes(cold.Write));}
            }Assert.Greater(cap,0);Assert.True(hot.Supported);Assert.True(hot.Allowed);Assert.AreEqual(8,hot.RemainingLength);}}
            Assert.AreEqual(baseline,storage.Retained);
        }
        [Test]public void MissingExactHeightLawCannotBecomeAnApproval()
        {
            var p=Profile();var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;using(var scene=Scene(p,storage))using(var work=Work(p,scene,storage,12,13,14,claimedHeight:1)){Run(work);Assert.False(work.Supported);Assert.Throws<InvalidOperationException>(()=>{var ignored=work.Allowed;});}Assert.AreEqual(baseline,storage.Retained);
        }
        [Test]public void RetiredSceneOwnerStaysUsableThroughLiveWorkPinsAndLastReleaseClearsAllLiability()
        {
            var p=Profile();AiCombatMicroSafety.PrepareExecutionAssets(p);var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;var scene=Scene(p,storage);using(var work=Work(p,scene,storage,12,13,14)){scene.Dispose();Run(work);Assert.True(work.Allowed);}Assert.AreEqual(baseline,storage.Retained);
        }
        [Test]public void RefusedNestedBirthCannotSkipThePreparedSample()
        {
            var p=Profile();AiCombatMicroSafety.PrepareExecutionAssets(p);var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;using(var scene=Scene(p,storage))using(var work=Work(p,scene,storage,12,13,14)){
                Assert.Zero(work.Advance(0,()=>throw new Exception("zero grant")));Assert.Zero(work.Advance(1,()=>false));long bytes=storage.Capacity-storage.Retained-AiCombatMicroExecutionStorage.RootBytes-AiCombatMicroExecutionStorage.HandleBytes;Assert.True(storage.TryReserve(bytes,out var pressure));using(pressure){int cap=100;while(work.Advance(1)>0&&cap-->0){}Assert.Greater(cap,0);long consumed=work.Consumed;Assert.Zero(work.Advance(1));Assert.AreEqual(consumed,work.Consumed);}Run(work);Assert.True(work.Allowed);
            }Assert.AreEqual(baseline,storage.Retained);
        }
    }
}
