using System;
using System.IO;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class AiCombatMicroSupportSpanWorkTests
    {
        static AiCombatSupportCatalog Catalog(params MapSupport[] values)=>new AiCombatSupportCatalog(AiCombatSupportSelector.FirstDeclared,"retained-native-partition",values);
        static NavLocation At(double x,double z,string surface="ground")=>new NavLocation(new NavPoint(x,z),surface);
        static byte[] Bytes(AiCombatMicroSupportSpanWork work){using(var stream=new MemoryStream()){using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))work.Write(w);return stream.ToArray();}}
        static bool Prove(AiCombatSupportCatalog catalog,NavLocation from,NavLocation to,string support)
        {
            var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained,grant=100000;Assert.True(AiCombatMicroSupportSpanWork.TryCreate(catalog,storage,from,to,support,ref grant,out var work));
            bool valid;using(work){int cap=20000;Assert.Zero(work.Advance(0));while(!work.Complete&&cap-->0)Assert.AreEqual(1,work.Advance(1));Assert.Greater(cap,0);valid=work.Valid;}Assert.AreEqual(baseline,storage.Retained);return valid;
        }
        [Test]public void CompleteConcaveSpanCannotApproveEndpointsAcrossMissingSupport()
        {
            var polygon=new NavPolygon(new[]{new NavPoint(-4,-4),new NavPoint(4,-4),new NavPoint(4,4),new NavPoint(2,4),new NavPoint(2,-2),new NavPoint(-2,-2),new NavPoint(-2,4),new NavPoint(-4,4)});
            var law=new MapSupport("concave",new NavObstacle(polygon),false,gradient:new NavPoint(.25,0),surfaceId:"ground");var c=Catalog(law);
            Assert.True(law.Contains(new NavPoint(-3,3)));Assert.True(law.Contains(new NavPoint(3,3)));Assert.False(law.Contains(new NavPoint(0,3)));
            Assert.False(Prove(c,At(-3,3),At(3,3),"concave"));Assert.True(Prove(c,At(-3,-3),At(3,-3),"concave"));
        }
        [Test]public void EarlierOverlapRequiresExplicitTypedRejoinInsteadOfSkippingThinRegion()
        {
            var c=Catalog(new MapSupport("first",new NavObstacle(-.01,-2,.01,2),true,4,surfaceId:"bridge"),new MapSupport("ground-law",new NavObstacle(-5,-2,5,2),false,surfaceId:"ground"));
            Assert.False(Prove(c,At(-4,0),At(4,0),"ground-law"),"Endpoint-only and radius sampling would miss the thin earlier support.");
            Assert.True(Prove(c,At(-4,0),At(-.01,0),"ground-law"));
            Assert.True(Prove(c,At(-.01,0,"bridge"),At(.01,0,"bridge"),"first"));
            Assert.True(Prove(c,At(.01,0),At(4,0),"ground-law"));
            Assert.True(Prove(c,At(-.01,0),At(-.01,0),null),"The explicit portal's from-side closure remains distinct.");
            Assert.True(Prove(c,At(-.01,0,"bridge"),At(-.01,0,"bridge"),null));
        }
        [Test]public void EveryPaidPolygonSortAndPointCursorContinuesExactlyWithoutReplay()
        {
            var p=new NavPolygon(new[]{new NavPoint(-5,-3),new NavPoint(5,-3),new NavPoint(5,3),new NavPoint(-5,3)});
            var catalog=Catalog(new MapSupport("polygon",new NavObstacle(p),false,surfaceId:"ground"));var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained,grant=100000;
            var from=At(-4,0);var to=At(4,0);Assert.True(AiCombatMicroSupportSpanWork.TryCreate(catalog,storage,from,to,"polygon",ref grant,out var hot));
            using(hot){int cap=2000;while(!hot.Complete&&cap-->0){var bytes=Bytes(hot);var coldStorage=new AiCombatMicroExecutionStorage();long coldBaseline=coldStorage.Retained;
                using(var reader=new BinaryReader(new MemoryStream(bytes)))using(var cold=AiCombatMicroSupportSpanWork.Read(reader,catalog,coldStorage,from,to,"polygon",ref grant)){
                    CollectionAssert.AreEqual(bytes,Bytes(cold));Assert.Zero(cold.Advance(0));hot.Advance(1);cold.Advance(1);CollectionAssert.AreEqual(Bytes(hot),Bytes(cold));
                }Assert.AreEqual(coldBaseline,coldStorage.Retained,"Cold proof releases its complete private liability.");
            }Assert.Greater(cap,0);Assert.True(hot.Valid);}Assert.AreEqual(baseline,storage.Retained);
        }
    }
}
