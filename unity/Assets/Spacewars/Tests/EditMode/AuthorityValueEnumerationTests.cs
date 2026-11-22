using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class AuthorityValueEnumerationTests
    {
        [Test] public void TypedAndInterfaceValuesKeepSlotOrderThroughChurnAndHydration()
        {
            var map=new AuthoritySlotMap<int,string>();var random=new Random(838049288);
            for(int step=0;step<600;step++){
                int key=random.Next(45);if(step%3==0)map.Remove(key);else map[key]="v"+step;
                if(step%31==0){var restored=new AuthoritySlotMap<int,string>();foreach(var pair in map)restored.Add(pair.Key,pair.Value);restored.RestoreLayout(map.CaptureLayout());map=restored;}
                var expected=map.Select(pair=>pair.Value).ToArray();var direct=new List<string>();foreach(var value in map.Values)direct.Add(value);
                CollectionAssert.AreEqual(expected,direct);CollectionAssert.AreEqual(expected,((IDictionary<int,string>)map).Values);
                var copy=new object[map.Count+2];((IDictionary)map).Values.CopyTo(copy,1);CollectionAssert.AreEqual(expected,copy.Skip(1).Take(map.Count));
                if(expected.Length>0)Assert.True(map.Values.Contains(expected[expected.Length-1]));
            }
        }
        [Test] public void ValuesCaptureVersionOnFirstMoveAndRejectLaterMutation()
        {
            var map=new AuthoritySlotMap<int,string>();map.Add(1,"first");var values=map.Values;var cursor=values.GetEnumerator();
            map.Add(2,"second");Assert.True(cursor.MoveNext());Assert.AreEqual("first",cursor.Current);
            map[2]="changed";Assert.Throws<InvalidOperationException>(()=>cursor.MoveNext());Assert.False(cursor.MoveNext());
            var last=values.GetEnumerator();Assert.True(last.MoveNext());Assert.True(last.MoveNext());map.Remove(1);Assert.Throws<InvalidOperationException>(()=>last.MoveNext());
            CollectionAssert.AreEqual(new[]{"changed"},values);Assert.AreSame(values,map.Values);
        }
        [Test] public void ExhaustedDisposedAndReadOnlyViewsPreserveExistingContract()
        {
            var map=new AuthoritySlotMap<int,string>();map.Add(1,"one");var cursor=map.Values.GetEnumerator();Assert.IsNull(cursor.Current);
            Assert.True(cursor.MoveNext());Assert.False(cursor.MoveNext());Assert.AreEqual("one",cursor.Current);map.Clear();Assert.False(cursor.MoveNext());
            cursor=map.Values.GetEnumerator();cursor.Dispose();map.Add(2,"two");Assert.False(cursor.MoveNext());Assert.Throws<NotSupportedException>(()=>cursor.Reset());
            ICollection<string> values=map.Values;Assert.True(values.IsReadOnly);Assert.Throws<NotSupportedException>(()=>values.Add("x"));Assert.Throws<NotSupportedException>(()=>values.Remove("two"));Assert.Throws<NotSupportedException>(()=>values.Clear());
            Assert.AreSame(map,((ICollection)map.Values).SyncRoot);Assert.False(((ICollection)map.Values).IsSynchronized);
        }
    }
}
