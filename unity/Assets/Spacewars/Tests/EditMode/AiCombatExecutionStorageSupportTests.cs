using System;
using System.IO;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed class AiCombatExecutionStorageSupportTests
    {
        static byte[] Bytes(Action<BinaryWriter> action){using(var m=new MemoryStream()){using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))action(w);return m.ToArray();}}
        static AiCombatSupportCatalog Catalog(IPlayableTerrain terrain){long grant=WorldWire.MaxItems;Assert.True(AiCombatSupportCatalog.TryBegin(terrain,"test-geometry",ref grant,_=>true,out var builder));while(!builder.Ready)Assert.AreEqual(1,builder.Advance(1));var result=builder.Finish();Assert.Throws<InvalidOperationException>(()=>builder.Finish());return result;}
        static void Run(AiCombatMicroSupportWork work){int cap=100000;while(!work.Complete&&cap-->0)Assert.AreEqual(1,work.Advance(1));Assert.Greater(cap,0);}
        [Test]public void SharedRootRetainsPayloadUntilLastOwnerAndRefusalChangesNothing()
        {
            var storage=new AiCombatMicroExecutionStorage(1024);long baseline=storage.Retained;
            Assert.True(storage.TryReserve(400,out var root));long retained=storage.Retained;Assert.False(storage.TryReserve(400,out _));Assert.AreEqual(retained,storage.Retained);
            Assert.True(root.TryShare(out var fork));Assert.AreEqual(retained+AiCombatMicroExecutionStorage.HandleBytes,storage.Retained);root.Dispose();root.Dispose();Assert.AreEqual(retained,storage.Retained);fork.Dispose();Assert.AreEqual(baseline,storage.Retained);Assert.Throws<ObjectDisposedException>(()=>root.TryShare(out _));
        }
        [Test]public void RetainedPayloadCanForkAfterOriginalOwnerReleaseWithoutResurrectingDeadRoot()
        {
            var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;Assert.True(storage.TryReserve(500,out var original));Assert.True(original.TryShare(out var pin));original.Dispose();Assert.Throws<ObjectDisposedException>(()=>original.TryShare(out _));Assert.True(original.TryShareRetainedRoot(out var child));pin.Dispose();Assert.Greater(storage.Retained,baseline);child.Dispose();Assert.AreEqual(baseline,storage.Retained);Assert.False(original.TryShareRetainedRoot(out _));Assert.AreEqual(baseline,storage.Retained);
        }
        [Test]public void TransactionCancelRollsBackAndTransferKeepsAdmittedRoots()
        {
            var storage=new AiCombatMicroExecutionStorage(4096);long baseline=storage.Retained;
            Assert.True(storage.TryBegin(2,out var tx));Assert.True(tx.TryReserve(500));Assert.False(tx.TryReserve(4000));tx.Dispose();Assert.AreEqual(baseline,storage.Retained);
            Assert.True(storage.TryBegin(1,out tx));Assert.True(tx.TryReserve(500));Assert.False(tx.TryReserve(1));long retained=storage.Retained;var owned=tx.Transfer();tx.Dispose();Assert.AreEqual(retained,storage.Retained);Assert.Throws<InvalidOperationException>(()=>tx.Transfer());owned.Dispose();owned.Dispose();Assert.AreEqual(baseline,storage.Retained);
            Assert.False(new AiCombatMicroExecutionStorage(128).TryBegin(1,out _));
        }
        [Test]public void CatalogAdmissionRefusalDoesNotSpendPreparationCredit()
        {
            var map=new AiTestMap();long grant=map.Supports.Count;Assert.False(AiCombatSupportCatalog.TryBegin(map,"identity",ref grant,_=>false,out _));Assert.AreEqual(map.Supports.Count,grant);grant=0;Assert.False(AiCombatSupportCatalog.TryBegin(map,"identity",ref grant,_=>throw new Exception("unpaid allocation"),out _));
        }
        [Test]public void SupportCursorGrantsRefusalForkAndColdStateMatchNativeFirstSelector()
        {
            var terrain=new AiTestMap();var catalog=Catalog(terrain);
            foreach(var point in new[]{new NavPoint(0,0),new NavPoint(144,144),new NavPoint(145,0)}){
                Assert.True(AiCombatMicroSupportWork.TryCreate(catalog,point,_=>true,out var work));var initial=Bytes(work.Write);Assert.Zero(work.Advance(0,()=>throw new Exception("zero grant")));Assert.Zero(work.Advance(1,()=>false));Assert.False(work.TryFork(_=>false,out _));CollectionAssert.AreEqual(initial,Bytes(work.Write));
                work.Advance(1);var bytes=Bytes(work.Write);AiCombatMicroSupportWork cold;using(var r=new BinaryReader(new MemoryStream(bytes)))cold=AiCombatMicroSupportWork.Read(r,catalog,_=>true);Assert.True(work.TryFork(_=>true,out var fork));Run(work);Run(cold);Run(fork);Assert.AreEqual(terrain.SupportAt(point)?.HeightAt(point)??0,work.Height);CollectionAssert.AreEqual(Bytes(work.Write),Bytes(cold.Write));CollectionAssert.AreEqual(Bytes(work.Write),Bytes(fork.Write));
            }
        }
        [Test]public void AntennaCityCatalogKeepsItsNativeFirstDeclaredPolygonAndSlopeSelector()
        {
            var terrain=new AntennaCityMap();var catalog=Catalog(terrain);int raised=0;
            for(int x=-150;x<=150;x+=25)for(int z=-150;z<=150;z+=25){
                var point=new NavPoint(x,z);Assert.True(AiCombatMicroSupportWork.TryCreate(catalog,point,_=>true,out var work));
                work.Advance(3);AiCombatMicroSupportWork cold;using(var reader=new BinaryReader(new MemoryStream(Bytes(work.Write))))cold=AiCombatMicroSupportWork.Read(reader,catalog,_=>true);
                Run(work);Run(cold);Assert.True(work.Supported);Assert.AreEqual(terrain.SurfaceHeight(point),work.Height);Assert.AreEqual(work.Height,cold.Height);CollectionAssert.AreEqual(Bytes(work.Write),Bytes(cold.Write));if(work.Height!=0)raised++;
            }
            Assert.Greater(raised,0,"Exercise the real elevated city supports as well as street/outside polygon points.");
        }
        [Test]public void PolygonSlopeAndAmbiguousSemanticSurfacePreserveNativeHeightArithmetic()
        {
            var terrain=new NereidaMap(new NereidaProfileData());var catalog=Catalog(terrain);int ambiguous=0,nonzero=0;
            for(int x=-144;x<=144;x+=8)for(int z=-144;z<=144;z+=8){var point=new NavPoint(x,z);Assert.True(AiCombatMicroSupportWork.TryCreate(catalog,point,_=>true,out var work));work.Advance(3);var bytes=Bytes(work.Write);AiCombatMicroSupportWork cold;using(var r=new BinaryReader(new MemoryStream(bytes)))cold=AiCombatMicroSupportWork.Read(r,catalog,_=>true);Run(work);Run(cold);double expected=terrain.SupportAt(point)?.HeightAt(point)??0;Assert.AreEqual(expected,work.Height,$"({x},{z})");Assert.AreEqual(expected,cold.Height);if(expected!=0)nonzero++;string first=null;foreach(var support in terrain.Supports)if(support.Contains(point)){if(first!=null&&first!=support.SurfaceId)ambiguous++;first=support.SurfaceId;}}
            Assert.Greater(nonzero,0);Assert.Greater(ambiguous,0,"stacked ambiguity must be exercised");
        }
        [Test]public void TypedStackedSupportUsesExactSourceHeightAndCannotFlattenWrongSurface()
        {
            var terrain=new NereidaMap(new NereidaProfileData());var catalog=Catalog(terrain);int nonzero=0;
            foreach(var source in terrain.Supports){var point=source.Bounds.Polygon!=null?source.Bounds.Polygon.Vertices[0]:new NavPoint((source.Bounds.MinX+source.Bounds.MaxX)/2,(source.Bounds.MinZ+source.Bounds.MaxZ)/2);Assert.True(source.Contains(point));var location=new NavLocation(point,source.SurfaceId);
                Assert.True(AiCombatMicroSupportWork.TryCreateTyped(catalog,location,source.Id,_=>true,out var work));byte[] initial=Bytes(work.Write);Assert.Zero(work.Advance(0));Assert.Zero(work.Advance(1,()=>false));CollectionAssert.AreEqual(initial,Bytes(work.Write));work.Advance(2);AiCombatMicroSupportWork cold;using(var r=new BinaryReader(new MemoryStream(Bytes(work.Write))))cold=AiCombatMicroSupportWork.Read(r,catalog,_=>true);Run(work);Run(cold);Assert.True(work.Supported);Assert.False(work.MatchesPoint(point),"A native ballistic query cannot accept a typed-height cursor");Assert.True(work.MatchesPoint(point,source.Id,source.SurfaceId));Assert.True(cold.MatchesPoint(point,source.Id,source.SurfaceId));Assert.AreEqual(source.HeightAt(point),work.Height);CollectionAssert.AreEqual(Bytes(work.Write),Bytes(cold.Write));if(work.Height!=0)nonzero++;
                Assert.True(AiCombatMicroSupportWork.TryCreateTyped(catalog,new NavLocation(point,"wrong-surface"),source.Id,_=>true,out var wrong));Run(wrong);Assert.False(wrong.Supported);Assert.Throws<InvalidOperationException>(()=>{var ignored=wrong.Height;});
            }Assert.Greater(nonzero,0);
        }
        [Test]public void SemanticRemainingLegHeightRequiresOneExactPhysicalHeightAcrossAllMatchingPieces()
        {
            var bounds=new NavObstacle(-2,-2,2,2);var point=new NavLocation(default,"deck");
            foreach(bool conflict in new[]{false,true}){var supports=new[]{new MapSupport("part-a",bounds,false,3,surfaceId:"deck"),new MapSupport("part-b",bounds,false,conflict?4:3,surfaceId:"deck")};var catalog=new AiCombatSupportCatalog(AiCombatSupportSelector.FirstDeclared,"semantic-test",supports);
                Assert.True(AiCombatMicroSupportWork.TryCreateSemantic(catalog,point,_=>true,out var work));work.Advance(2);AiCombatMicroSupportWork cold;using(var r=new BinaryReader(new MemoryStream(Bytes(work.Write))))cold=AiCombatMicroSupportWork.Read(r,catalog,_=>true);Run(work);Run(cold);Assert.AreEqual(!conflict,work.Supported);CollectionAssert.AreEqual(Bytes(work.Write),Bytes(cold.Write));if(conflict)Assert.Throws<InvalidOperationException>(()=>{var ignored=work.Height;});else{Assert.AreEqual(3,work.Height);Assert.AreEqual("part-a",work.ResolvedSupport);}Assert.True(work.MatchesPoint(default,null,"deck"));Assert.False(work.MatchesPoint(default));
            }
        }
        [Test]public void TypedUnknownSupportAndBareFlatHaveNoImplicitZeroHeightPermit()
        {
            var catalog=Catalog(new AiTestMap());Assert.True(AiCombatMicroSupportWork.TryCreateTyped(catalog,new NavLocation(default,"open-ground"),"missing-support",_=>true,out var work));Run(work);Assert.False(work.Supported);
            Assert.True(AiCombatMicroSupportWork.TryCreateTyped(Catalog(null),new NavLocation(default,"flat"),"flat",_=>true,out work));Run(work);Assert.False(work.Supported,"A typed source pin needs a concrete adapter catalog; null ballistic terrain is not that pin");
        }
        [Test]public void FlatAndUnsupportedAreDistinctAndWrongColdBindingIsRejected()
        {
            var flat=Catalog(null);Assert.True(AiCombatMicroSupportWork.TryCreate(flat,default,_=>true,out var work));Run(work);Assert.True(work.Supported);Assert.Zero(work.Height);
            var unsupported=new AiCombatSupportCatalog(AiCombatSupportSelector.Unsupported,"unverified",Array.Empty<MapSupport>());Assert.True(AiCombatMicroSupportWork.TryCreate(unsupported,default,_=>true,out var pending));Run(pending);Assert.False(pending.Supported);Assert.Throws<InvalidOperationException>(()=>{var ignored=pending.Height;});
            var bytes=Bytes(work.Write);using(var r=new BinaryReader(new MemoryStream(bytes)))Assert.Throws<ArgumentException>(()=>AiCombatMicroSupportWork.Read(r,unsupported,_=>true));
        }
    }
}
