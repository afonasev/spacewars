using System;
using System.IO;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class AiCombatMicroCoverWorkTests
    {
        static byte[] Bytes(Action<BinaryWriter> write){using(var m=new MemoryStream()){using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))write(w);return m.ToArray();}}
        static PlayableAiObservation View(PlayableProfile p,PlayableEntitySnapshot u,PlayableBuildingSnapshot[] buildings)=>PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,905,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,new[]{u},buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
        static AiCombatMicroCoverScene Scene(PlayableProfile p,PlayableBuildingSnapshot[] buildings,NavObstacle[] solids,AiCombatMicroExecutionStorage storage,out AiCombatMicroExecutionStorage.OwnedLeases roots)
        {
            Assert.True(storage.TryBegin(1,out var tx));AiCombatMicroExecutionStorage.Lease catalogMemory=null;
            // Catalog owns one root; scene retains its own independent handle.
            long grant=WorldWire.MaxItems;Assert.True(AiCombatSupportCatalog.TryBegin(p.AuthoredMap,"native-profile",ref grant,bytes=>{Assert.True(storage.TryReserve(bytes,out catalogMemory));return true;},out var builder));while(!builder.Ready)builder.Advance(1);var catalog=builder.Finish();Assert.True(AiCombatMicroCoverScene.TryCreate(p,catalog,catalogMemory,buildings,solids,ref grant,storage,out var scene));catalogMemory.Dispose();roots=tx.Transfer();tx.Dispose();return scene;
        }
        static void Run(AiCombatMicroCoverWork work){int limit=2000000;while(!work.Complete&&limit-->0)Assert.AreEqual(1,work.Advance(1));Assert.Greater(limit,0);}
        [TestCase(PlayableBuildingKind.SentryTurret,PlayableEntityKind.Tank,false)]
        [TestCase(PlayableBuildingKind.GaussTower,PlayableEntityKind.Tank,false)]
        [TestCase(PlayableBuildingKind.GaussTower,PlayableEntityKind.Hawk,false)]
        [TestCase(PlayableBuildingKind.SentryTurret,PlayableEntityKind.Hawk,false)]
        [TestCase(PlayableBuildingKind.SentryTurret,PlayableEntityKind.Tank,true)]
        public void PaidMuzzlePitchTerrainAndBuildingOcclusionMatchOrdinaryCovers(PlayableBuildingKind kind,PlayableEntityKind unitKind,bool blocker)
        {
            var p=PlayableProfile.Default;AiCombatMicroSafety.PrepareExecutionAssets(p);var tower=new PlayableBuildingSnapshot(100,PlayableOwner.Enemy,kind,new NavPoint(0,0),100,1,0,0,default);
            var unit=new PlayableEntitySnapshot(1,PlayableOwner.Player,unitKind,new NavPoint(8,0),100,false,0,0,0,altitude:8);
            var buildings=blocker?new[]{tower,new PlayableBuildingSnapshot(101,PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(4,0),100,1,0,0,default)}:new[]{tower};
            var observation=View(p,unit,buildings);var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;
            using(var scene=Scene(p,buildings,Array.Empty<NavObstacle>(),storage,out var roots))using(roots){
                foreach(var point in new[]{new NavPoint(8,0),new NavPoint(4,0),new NavPoint(100,0)}){
                    Assert.True(AiCombatMicroCoverWork.TryCreate(scene,storage,tower,unit,point,out var work));using(work){var initial=Bytes(work.Write);Assert.Zero(work.Advance(0,()=>throw new Exception("zero grant")));Assert.Zero(work.Advance(1,()=>false));CollectionAssert.AreEqual(initial,Bytes(work.Write));Run(work);Assert.True(work.Supported);Assert.AreEqual(AiCombatMicroSafety.Covers(observation,p,tower,unit,point),work.Covered,$"{kind}/{unitKind}/{blocker}/{point.X}");}
                }
            }
            Assert.AreEqual(baseline,storage.Retained);
        }
        [TestCase(PlayableBuildingKind.Factory)]
        [TestCase(PlayableBuildingKind.Hangar)]
        public void FrozenScenePreservesUpgradedProducerGeometry(PlayableBuildingKind kind)
        {
            var p=PlayableProfile.Default;AiCombatMicroSafety.PrepareExecutionAssets(p);var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;
            var building=new PlayableBuildingSnapshot(100,PlayableOwner.Player,kind,new NavPoint(4,8),100,1,0,0,default,producerUpgraded:true);
            using(var scene=Scene(p,new[]{building},Array.Empty<NavObstacle>(),storage,out var roots))using(roots){
                Assert.True(scene.At(0).Upgraded);var saved=Bytes(scene.WriteBinding);long grant=long.MaxValue;
                using(var reader=new BinaryReader(new MemoryStream(saved)))using(var cold=AiCombatMicroCoverScene.ReadFrozen(reader,scene,storage,ref grant)){
                    Assert.True(cold.At(0).Upgraded);CollectionAssert.AreEqual(saved,Bytes(cold.WriteBinding),"Cold geometry must keep the actual upgraded producer mesh selection.");
                }
            }
            Assert.AreEqual(baseline,storage.Retained);
        }
        [Test]public void SemanticRemainingBranchTargetKeepsFullCoverageAndColdBinding()
        {
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());AiCombatMicroSafety.PrepareExecutionAssets(p);var tower=new PlayableBuildingSnapshot(100,PlayableOwner.Enemy,PlayableBuildingKind.SentryTurret,default,100,1,0,0,default);var unit=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(8,0),100,false,0,0,0);var location=new NavLocation(unit.Position,"open-ground");var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;
            using(var scene=Scene(p,new[]{tower},Array.Empty<NavObstacle>(),storage,out var roots))using(roots){Assert.True(AiCombatMicroCoverWork.TryCreateSemanticTarget(scene,storage,tower,unit,location,out var work));using(work){work.Advance(3);AiCombatMicroCoverWork cold;using(var r=new BinaryReader(new MemoryStream(Bytes(work.Write))))cold=AiCombatMicroCoverWork.Read(r,scene,storage,location);using(cold){Run(work);Run(cold);Assert.True(work.Supported);Assert.AreEqual(AiCombatMicroSafety.Covers(View(p,unit,new[]{tower}),p,tower,unit,unit.Position),work.Covered);CollectionAssert.AreEqual(Bytes(work.Write),Bytes(cold.Write));}}}Assert.AreEqual(baseline,storage.Retained);
        }
        [Test]public void TypedTargetKeepsPinnedSourceHeightAndColdInputCannotSwitchModes()
        {
            var terrain=new NereidaMap(new NereidaProfileData());var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),terrain);AiCombatMicroSafety.PrepareExecutionAssets(p);MapSupport source=null;foreach(var support in terrain.Supports)if(support.Height>0){source=support;break;}Assert.NotNull(source);var point=source.Bounds.Polygon!=null?source.Bounds.Polygon.Vertices[0]:new NavPoint((source.Bounds.MinX+source.Bounds.MaxX)/2,(source.Bounds.MinZ+source.Bounds.MaxZ)/2);var location=new NavLocation(point,source.SurfaceId);
            var tower=new PlayableBuildingSnapshot(100,PlayableOwner.Enemy,PlayableBuildingKind.SentryTurret,point,100,1,0,0,default);var unit=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,point,100,false,0,0,0);var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;
            using(var scene=Scene(p,new[]{tower},Array.Empty<NavObstacle>(),storage,out var roots))using(roots){Assert.True(AiCombatMicroCoverWork.TryCreateTypedTarget(scene,storage,tower,unit,location,source.Id,out var work));using(work){work.Advance(3);var bytes=Bytes(work.Write);using(var r=new BinaryReader(new MemoryStream(bytes)))Assert.Throws<ArgumentException>(()=>AiCombatMicroCoverWork.Read(r,scene,storage));AiCombatMicroCoverWork cold;using(var r=new BinaryReader(new MemoryStream(bytes)))cold=AiCombatMicroCoverWork.Read(r,scene,storage,location,source.Id);using(cold){Run(work);Run(cold);CollectionAssert.AreEqual(Bytes(work.Write),Bytes(cold.Write));Assert.True(work.Supported);var target=(BallisticPoint)typeof(AiCombatMicroCoverWork).GetField("target",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(work);Assert.AreEqual(source.HeightAt(point)+terrain.DirectFireHeight,target.Y,"Actual target height uses pinned raw support arithmetic");}}
                Assert.True(AiCombatMicroCoverWork.TryCreateTypedTarget(scene,storage,tower,unit,new NavLocation(point,"wrong-surface"),source.Id,out var wrong));using(wrong){Run(wrong);Assert.False(wrong.Supported);Assert.Throws<InvalidOperationException>(()=>{var ignored=wrong.Covered;});}}
            Assert.AreEqual(baseline,storage.Retained);
        }
        [Test]public void PartialPreparationAndGeometryCursorColdRestoreAreExactAndReleaseLiability()
        {
            var p=PlayableProfile.Default;AiCombatMicroSafety.PrepareExecutionAssets(p);var tower=new PlayableBuildingSnapshot(100,PlayableOwner.Enemy,PlayableBuildingKind.SentryTurret,default,100,1,0,0,default);var unit=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(8,0),100,false,0,0,0);
            var buildings=new[]{tower,new PlayableBuildingSnapshot(101,PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(4,8),100,1,0,0,default)};
            var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;
            using(var scene=Scene(p,buildings,Array.Empty<NavObstacle>(),storage,out var roots))using(roots){
                Assert.True(AiCombatMicroCoverWork.TryCreate(scene,storage,tower,unit,unit.Position,out var work));using(work){int steps=0;while(!work.Complete){Assert.AreEqual(1,work.Advance(1));steps++;if(steps%197!=0&&!work.Complete)continue;var bytes=Bytes(work.Write);long retained=storage.Retained;AiCombatMicroCoverWork cold;using(var r=new BinaryReader(new MemoryStream(bytes)))cold=AiCombatMicroCoverWork.Read(r,scene,storage);using(cold)CollectionAssert.AreEqual(bytes,Bytes(cold.Write));Assert.AreEqual(retained,storage.Retained,"read/dispose must release exactly its own recomputed liability");using(var r=new BinaryReader(new MemoryStream(bytes)))cold=AiCombatMicroCoverWork.Read(r,scene,storage);using(cold){Assert.AreEqual(work.Advance(1),cold.Advance(1));CollectionAssert.AreEqual(Bytes(work.Write),Bytes(cold.Write));}}Assert.True(work.Supported);}
            }
            Assert.AreEqual(baseline,storage.Retained);
        }
    }
}
