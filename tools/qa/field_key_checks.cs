using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Spacewars.Simulation;
internal static class FieldKeyChecks
{
    const BindingFlags P=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    sealed class Arm
    {
        internal readonly object Router,Field;readonly Type Type,Cell;internal readonly List<string> Dequeued=new List<string>();
        internal Arm(Type type,NavGeometry geometry,NavigationProfile profile,int[][] seeds,string mode){
            Type=type;Cell=type.GetNestedType("Cell",BindingFlags.NonPublic);Router=Activator.CreateInstance(type,mode=="generic-dense"?new object[]{geometry,profile}:new object[]{geometry,profile,null,true});
            // Allocate bounded predicate storage before field creation to cover prepared dense.
            type.GetMethod("IsFree",P).Invoke(Router,new[]{NewCell(5,12)});
            type.GetField("testSparseFields",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,mode=="sparse");
            try{Field=type.GetMethod("GetField",P,null,new[]{typeof(IReadOnlyList<>).MakeGenericType(Cell)},null).Invoke(Router,new object[]{Cells(seeds)});}
            finally{type.GetField("testSparseFields",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,false);}
        }
        object NewCell(int x,int z)=>Activator.CreateInstance(Cell,new object[]{x,z});
        Array Cells(int[][] points){var a=Array.CreateInstance(Cell,points.Length);for(int i=0;i<points.Length;i++)a.SetValue(NewCell(points[i][0],points[i][1]),i);return a;}
        static int Coord(object c,string name)=>(int)c.GetType().GetField(name,P).GetValue(c);
        static string Key(object c)=>Coord(c,"X")+","+Coord(c,"Z");
        internal string Expand(int[][] points){Dequeued.Clear();Type.GetField("testFieldDequeued",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,(Action<int,int>)((x,z)=>Dequeued.Add(x+","+z)));
            try{if(Type==typeof(SharedFlowRouter)&&points.Length==1)Type.GetMethod("ExpandSingle",P).Invoke(Router,new object[]{Field,NewCell(points[0][0],points[0][1])});else Type.GetMethod("ExpandUntil",P).Invoke(Router,new object[]{Field,Cells(points)});return null;}catch(TargetInvocationException e){return e.InnerException.GetType().FullName+":"+e.InnerException.Message;}finally{Type.GetField("testFieldDequeued",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,null);}}
        internal string[] Frontier()=>((IEnumerable)Field.GetType().GetField("Frontier",P).GetValue(Field)).Cast<object>().Select(Key).ToArray();
        internal string Labels(){var d=Field.GetType().GetField("Distance",P).GetValue(Field);var sparse=(IDictionary)d.GetType().GetField("sparse",P).GetValue(d);var values=new List<string>();
            if(sparse!=null){foreach(DictionaryEntry e in sparse)values.Add(Key(e.Key)+":"+e.Value);return "sparse:"+string.Join(";",values.OrderBy(x=>x));}
            var array=(int[])d.GetType().GetField("dense",P).GetValue(d);for(int i=0;i<array.Length;i++)if(array[i]!=0)values.Add(i+":"+array[i]);return "dense:"+string.Join(";",values);}

    }
    static void ExactContinuationAndLabels(){int cases=0;
        foreach(string mode in new[]{"sparse","prepared-dense","generic-dense"})
        foreach(var obstacles in new[]{Array.Empty<NavObstacle>(),new[]{new NavObstacle(-1,-4,1,4)},new[]{new NavObstacle(-1,-12,1,12)}})
        foreach(var seeds in new[]{new[]{new[]{18,12}},new[]{new[]{18,12},new[]{18,12},new[]{18,14}},new[]{new[]{-2,-2},new[]{18,12}}}){
            var g=new NavGeometry(12,obstacles,1);var p=new NavigationProfile("queued-distance",1,.3,4,4.5,1,.15,3.6,.08);var a=new Arm(typeof(BaselineSharedFlowRouter),g,p,seeds,mode);var b=new Arm(typeof(SharedFlowRouter),g,p,seeds,mode);
            CollectionAssert.AreEqual(a.Frontier(),b.Frontier());Assert.AreEqual(a.Labels(),b.Labels());
            foreach(var required in new[]{Array.Empty<int[]>(),seeds,new[]{new[]{10,13},new[]{10,13}},new[]{new[]{3,3}},new[]{new[]{20,20}},new[]{new[]{int.MaxValue,int.MaxValue}},new[]{new[]{3,3}}}){
                Assert.AreEqual(a.Expand(required),b.Expand(required),"Exact exception contract");CollectionAssert.AreEqual(a.Dequeued,b.Dequeued,"Exact pop order and stopping boundary");CollectionAssert.AreEqual(a.Frontier(),b.Frontier(),"Remaining FIFO");Assert.AreEqual(a.Labels(),b.Labels(),"All distance labels");cases++;
            }
        }Console.WriteLine("QUEUE_CONTINUATION_PASS cases="+cases+" sparsePreparedDenseGenericDense=true");
    }
    public static string NormalizeKey(object key){
        if(key is string text)return string.Join(":",text.Split(':').Skip(2));
        var t=key.GetType();int count=(int)t.GetProperty("Count",P).GetValue(key);var parts=new List<string>();
        for(int i=0;i<count;i++){var c=t.GetMethod("At",P).Invoke(key,new object[]{i});parts.Add(c.GetType().GetField("X",P).GetValue(c)+":"+c.GetType().GetField("Z",P).GetValue(c));}return string.Join(":",parts);
    }
    static string KeySemantics(Type t){
        var g=new NavGeometry(12,Array.Empty<NavObstacle>(),1);var p=new NavigationProfile("key",1,.3,4,4.5,1,.15,3.6,.08);var r=Activator.CreateInstance(t,new object[]{g,p,null,true});var c=t.GetNestedType("Cell",BindingFlags.NonPublic);var listType=typeof(List<>).MakeGenericType(c);var get=t.GetMethod("GetField",P,null,new[]{typeof(IReadOnlyList<>).MakeGenericType(c)},null);
        Func<int,int,object> cell=(x,z)=>Activator.CreateInstance(c,new object[]{x,z});Func<object[],IList> list=v=>{var l=(IList)Activator.CreateInstance(listType);foreach(var x in v)l.Add(x);return l;};Func<IList,object> field=l=>get.Invoke(r,new object[]{l});
        var zero=cell(0,0);var a=cell(3,5);var b=cell(7,9);var empty=field(list(new object[0]));var single=field(list(new[]{zero}));Assert.AreNotSame(empty,single);
        Assert.AreSame(single,t.GetMethod("GetField",P,null,new[]{c},null).Invoke(r,new[]{zero}));
        var input=list(new[]{a,b});var original=field(input);Assert.AreSame(original,field(list(new[]{a,b})));
        Assert.AreNotSame(original,field(list(new[]{b,a})));Assert.AreNotSame(original,field(list(new[]{a,a})));
        input[0]=b;input[1]=a;Assert.AreNotSame(original,field(input));input[0]=a;input[1]=b;Assert.AreSame(original,field(input),"Stored key survives caller mutation");
        t.GetMethod("TrimFields").Invoke(r,new object[]{2});Assert.AreNotSame(original,field(input),"FIFO eviction preserves field identity contract");
        return t.GetProperty("RebuildCount").GetValue(r)+":"+t.GetProperty("FieldCacheHits").GetValue(r)+":"+t.GetProperty("CachedFieldCount").GetValue(r);
    }
    static void OrderedImmutableKeys(){Assert.AreEqual(KeySemantics(typeof(BaselineSharedFlowRouter)),KeySemantics(typeof(SharedFlowRouter)));}
    public static int RunSelected(){int code=FreeEndpointChecks.RunSelected(),passed=0,failed=0;foreach(var check in new Action[]{ExactContinuationAndLabels,OrderedImmutableKeys})try{check();passed++;Console.WriteLine("KEY_CHECK_PASS "+check.Method.Name);}catch(Exception e){failed++;Console.WriteLine("KEY_CHECK_FAIL "+check.Method.Name+" "+e);}Console.WriteLine("KEY_CHECK_RESULT passed="+passed+" failed="+failed);return code==0&&failed==0?0:1;}
}
