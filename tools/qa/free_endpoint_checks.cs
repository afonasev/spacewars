using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Simulation;

internal static class FreeEndpointChecks
{
    const BindingFlags P=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Cell(Type t,int x,int z)=>Activator.CreateInstance(t.GetNestedType("Cell",BindingFlags.NonPublic),new object[]{x,z});
    static string Point(object c)=>c.GetType().GetField("X").GetValue(c)+","+c.GetType().GetField("Z").GetValue(c);
    static string Entries(IDictionary d){var values=new List<string>();foreach(DictionaryEntry e in d)values.Add(Point(e.Key)+"="+e.Value);return string.Join(";",values.OrderBy(x=>x));}
    static string State(object r){
        var fields=(IDictionary)r.GetType().GetField("fields",P).GetValue(r);var result=new List<string>();
        foreach(DictionaryEntry e in fields){var f=e.Value;var d=f.GetType().GetField("Distance").GetValue(f);var sparse=(IDictionary)d.GetType().GetField("sparse",P).GetValue(d);
            result.Add(e.Key+":"+Entries(sparse)+"/fifo="+string.Join(";",((IEnumerable)f.GetType().GetField("Frontier").GetValue(f)).Cast<object>().Select(Point)));}
        return string.Join("|",result.OrderBy(x=>x));
    }
    static NavigationProfile Profile(double radius)=>new NavigationProfile("certified-edge",1,radius,4,4.5,1,.15,3.6,.08);
    static void ExactFifoAndRoutes(){
        foreach(double radius in new[]{.3,.72})foreach(var obstacle in new[]{new NavObstacle(-.8,-7,.8,7),new NavObstacle(new NavPoint(0,0),3),new NavObstacle(new NavPolygon(new[]{new NavPoint(-2,-5),new NavPoint(3,-2),new NavPoint(1,5)}))}){
            var g=new NavGeometry(24,new[]{obstacle},1);var p=Profile(radius);var actual=new SharedFlowRouter(g,p,null,true);var baseline=new BaselineSharedFlowRouter(g,p,null,true);
            var a=new List<string>();var b=new List<string>();
            typeof(SharedFlowRouter).GetField("testSparseFields",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,true);
            typeof(BaselineSharedFlowRouter).GetField("testSparseFields",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,true);
            typeof(SharedFlowRouter).GetField("testFieldDequeued",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,(Action<int,int>)((x,z)=>a.Add(x+","+z)));
            typeof(BaselineSharedFlowRouter).GetField("testFieldDequeued",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,(Action<int,int>)((x,z)=>b.Add(x+","+z)));
            try{for(int n=0;n<8;n++){
                a.Clear();b.Clear();var start=new NavPoint(-6-n*.13,-2+n*.11);var goal=new NavPoint(6+n*.17,2-n*.19);
                CollectionAssert.AreEqual(baseline.FindPath(start,goal),actual.FindPath(start,goal));CollectionAssert.AreEqual(b,a,"Exact dequeue order");Assert.AreEqual(State(baseline),State(actual),"Distances and remaining FIFO");Assert.AreEqual(baseline.ExpandedCells,actual.ExpandedCells);
                if(n%3==2){actual.TrimFields(1);baseline.TrimFields(1);}
            }}finally{foreach(var type in new[]{typeof(SharedFlowRouter),typeof(BaselineSharedFlowRouter)}){type.GetField("testSparseFields",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,false);type.GetField("testFieldDequeued",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,null);}}
        }
    }
    static void UntrustedRootsAndUnavailablePreparation(){
        var g=new NavGeometry(181,new[]{new NavObstacle(-1,-3,1,3)},1);var p=Profile(.3);var actual=new SharedFlowRouter(g,p,null,true);
        var cell=Cell(typeof(SharedFlowRouter),181,181);var field=typeof(SharedFlowRouter).GetMethod("GetField",P,null,new[]{cell.GetType()},null).Invoke(actual,new[]{cell});
        Assert.False((bool)field.GetType().GetField("FreeRoots").GetValue(field),"Private/synthetic roots are not certified");
        var prep=typeof(SharedFlowRouter).GetField("preparation",P).GetValue(actual);prep.GetType().GetField("allocationUnavailable",P).SetValue(prep,true);
        var baseline=new BaselineSharedFlowRouter(g,p,null,true);var before=new NavPoint(-5,0);var to=new NavPoint(5,0);
        CollectionAssert.AreEqual(baseline.FindPath(before,to),actual.FindPath(before,to));
        var changed=actual.ForGeometry(new NavGeometry(181,new[]{new NavObstacle(-1,-1,1,1)},2));Assert.Zero(changed.CachedFieldCount);
    }
    static void CertifiedTileBoundaryAndEviction(){
        var g=new NavGeometry(2100,new[]{new NavObstacle(-2099,-2099,-2097,-2097)},1);var p=Profile(.3);var r=new SharedFlowRouter(g,p,null,true);
        var prep=typeof(SharedFlowRouter).GetField("preparation",P).GetValue(r);var method=prep.GetType().GetMethod("EdgeFreeCertified",P|BindingFlags.Public);
        foreach(int x in Enumerable.Range(1,160).Select(i=>i*32-2))foreach(var delta in new[]{new[]{0,1},new[]{1,0},new[]{0,-1},new[]{-1,0}}){
            int z=33;var a=Cell(typeof(SharedFlowRouter),x,z);var b=Cell(typeof(SharedFlowRouter),x+delta[0],z+delta[1]);
            var pa=new NavPoint(-2100+x, -2100+z);var pb=new NavPoint(pa.X+delta[0],pa.Z+delta[1]);
            // Only invoke the certificate interface when both footprints are independently free.
            if(!g.IsFree(pa,.3)||!g.IsFree(pb,.3))continue;
            Assert.AreEqual(g.SegmentFree(pa,pb,.3),(bool)method.Invoke(prep,new[]{a,b}));Assert.AreEqual(g.SegmentFree(pa,pb,.3),(bool)method.Invoke(prep,new[]{a,b}));
        }
        Assert.LessOrEqual(r.PreparedGeometryBytes,SharedFlowRouter.ObserverPreparationPayloadLimit);
    }
    public static int RunSelected(){int code=Spacewars.Tests.EditMode.ObserverGeometryTileTests.RunSelected();int passed=0,failed=0;
        foreach(var check in new Action[]{ExactFifoAndRoutes,UntrustedRootsAndUnavailablePreparation,CertifiedTileBoundaryAndEviction})try{check();passed++;Console.WriteLine("CERT_CHECK_PASS "+check.Method.Name);}catch(Exception e){failed++;Console.WriteLine("CERT_CHECK_FAIL "+check.Method.Name+" "+e);}
        Console.WriteLine("CERT_CHECK_RESULT passed="+passed+" failed="+failed);return code==0&&failed==0?0:1;}
}
