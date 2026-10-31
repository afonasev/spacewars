using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Simulation;

// Pinned baseline is compiled under a distinct type name by sparse_readiness_qa.py.
internal static class SparseReadinessChecks
{
    const BindingFlags P=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    sealed class Arm
    {
        internal readonly object Router,Field; internal readonly Type Type,Cell;
        internal readonly List<string> Dequeued=new List<string>();
        internal Arm(Type type,NavGeometry geometry,NavigationProfile profile,int[][] seeds)
        {
            Type=type;Cell=type.GetNestedType("Cell",BindingFlags.NonPublic);
            Router=Activator.CreateInstance(type,new object[]{geometry,profile,null,true});
            Type.GetField("testSparseFields",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,true);
            try{Field=Type.GetMethod("GetField",P,null,new[]{typeof(IReadOnlyList<>).MakeGenericType(Cell)},null).Invoke(Router,new object[]{Cells(seeds)});}
            finally{Type.GetField("testSparseFields",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,false);}
        }
        Array Cells(int[][] cells){var a=Array.CreateInstance(Cell,cells.Length);for(int i=0;i<cells.Length;i++)a.SetValue(Activator.CreateInstance(Cell,new object[]{cells[i][0],cells[i][1]}),i);return a;}
        internal void Expand(int[][] cells)
        {
            Type.GetField("testFieldDequeued",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,(Action<int,int>)((x,z)=>Dequeued.Add(x+","+z)));
            try{Type.GetMethod("ExpandUntil",P).Invoke(Router,new object[]{Field,Cells(cells)});}
            finally{Type.GetField("testFieldDequeued",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,null);}
        }
        string Key(object c)=>Cell.GetField("X").GetValue(c)+","+Cell.GetField("Z").GetValue(c);
        internal string[] Distances(){var d=Field.GetType().GetField("Distance",P).GetValue(Field);var sparse=(IDictionary)d.GetType().GetField("sparse",P).GetValue(d);return sparse.Keys.Cast<object>().Select(k=>Key(k)+":"+sparse[k]).ToArray();}
        internal string[] Frontier()=>((IEnumerable)Field.GetType().GetField("Frontier",P).GetValue(Field)).Cast<object>().Select(Key).ToArray();
    }
    internal static int RunSelected()
    {
        int cases=0;
        foreach(var obstacles in new[]{Array.Empty<NavObstacle>(),new[]{new NavObstacle(-1,-4,1,4)},new[]{new NavObstacle(-1,-12,1,12)}})
        foreach(var seeds in new[]{new[]{new[]{12,12}},new[]{new[]{12,12},new[]{12,12}},new[]{new[]{-2,-2},new[]{12,12}}}){
            var g=new NavGeometry(12,obstacles,1);var p=new NavigationProfile("sparse-readiness",1,.3,4,4.5,1,.15,3.6,.08);
            var a=new Arm(typeof(BaselineSharedFlowRouter),g,p,seeds);var b=new Arm(typeof(SharedFlowRouter),g,p,seeds);
            // Empty/already ready; duplicate missing; multi-target; unreachable; warm exhausted.
            foreach(var required in new[]{Array.Empty<int[]>(),seeds,new[]{new[]{10,13},new[]{10,13}},new[]{new[]{3,3},new[]{20,20}},new[]{new[]{int.MaxValue,int.MaxValue}},new[]{new[]{3,3}}}){
                a.Expand(required);b.Expand(required);CollectionAssert.AreEqual(a.Dequeued,b.Dequeued,"Complete FIFO dequeue sequence and invocation stopping boundary");CollectionAssert.AreEqual(a.Distances(),b.Distances(),"All distance values and insertion order");CollectionAssert.AreEqual(a.Frontier(),b.Frontier(),"Retained frontier for warm continuation");cases++;
            }
        }
        Console.WriteLine("SPARSE_READINESS_PASS cases="+cases+" exactFIFO=true distances=true frontier=true");
        return Spacewars.Tests.EditMode.ObserverGeometryTileTests.RunSelected();
    }
}
