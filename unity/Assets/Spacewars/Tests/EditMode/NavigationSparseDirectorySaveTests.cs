using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationSparseDirectorySaveTests
    {
        // Position pages 32 and 1024 make the first write grow one/two levels
        // above the already allocated empty leaf block on the zero prefix.
        [TestCase(0)][TestCase(8191)][TestCase(8192)][TestCase(262144)]
        public void FirstSparseFrontierWriteRoundtripsPhysicalShapeAndNextCow(int node)
        {
            var hot=new NavigationRetentionBudget();
            Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var birth));
            var frontier=NavFieldFrontier.Create(300000,1,1024,true,birth.TryReserve);
            var first=new NavQueueEntry{Node=node,Cost=2};frontier.Add(first);frontier.SealPrepared();
            birth.Attach(frontier);birth.Commit(frontier,_=>{});
            long charge=hot.PhysicalBytes;byte[] bytes=Capture(hot,frontier);
            var cold=new NavigationRetentionBudget();
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));
            NavFieldFrontier restored=null;
            try{
                restored=(NavFieldFrontier)Read(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});
                Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(1,restored.Count);Assert.AreEqual(first.Node,restored.Min.Node);
                CollectionAssert.AreEqual(bytes,Capture(cold,restored));
                CollectionAssert.AreEqual(Continue(hot,frontier),Continue(cold,restored));
                CollectionAssert.AreEqual(bytes,Capture(hot,frontier));CollectionAssert.AreEqual(bytes,Capture(cold,restored));
            }finally{
                decode.Discard();restored?.ReleaseStorage();frontier.ReleaseStorage();
                Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);Assert.Zero(hot.ActiveScopes);
                Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
            }
        }
        [TestCase(8192)][TestCase(262144)]
        public void SparseDecodeEveryAllocationRefusalReleasesPhysicalOwnership(int node)
        {
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var birth));
            var source=NavFieldFrontier.Create(300000,1,1024,true,birth.TryReserve);
            source.Add(new NavQueueEntry{Node=node,Cost=1});source.SealPrepared();birth.Attach(source);birth.Commit(source,_=>{});
            byte[] bytes=Capture(hot,source);source.ReleaseStorage();Assert.Zero(hot.PhysicalBytes);
            int calls=0;var probe=new NavigationRetentionBudget();Assert.True(probe.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var discovery));
            probe.AllocationAdmission=_=>{calls++;return true;};
            var trial=Read(bytes,discovery.OwnedReservation());discovery.Attach(trial);discovery.Discard();Assert.Zero(probe.PhysicalBytes);Assert.Greater(calls,0);
            for(int nth=1;nth<=calls;nth++){
                var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));int seen=0;
                budget.AllocationAdmission=_=>++seen!=nth;
                try{Assert.Throws<InvalidOperationException>(()=>Read(bytes,scope.OwnedReservation()),"Refusal "+nth);Assert.AreEqual(nth,seen);}
                finally{budget.AllocationAdmission=null;scope.Discard();}
                Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.False(budget.CaptureActive);
            }
        }
        private static byte[] Continue(NavigationRetentionBudget budget,NavFieldFrontier source)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,3,out var scope));
            var next=source.ForkPackedPrepared(scope.TryReserve);scope.Attach(next);
            try{
                Assert.True(next.Remove(next.Min));Assert.Zero(next.Count);
                next.Add(new NavQueueEntry{Node=100,Cost=3});next.Add(new NavQueueEntry{Node=262145,Cost=1});
                Assert.AreEqual(262145,next.Min.Node);Assert.True(next.Remove(next.Min));
                next.SealPrepared();return Capture(budget,next,4);
            }finally{scope.Discard();}
        }
        [TestCase("root-empty")][TestCase("nonzero-empty")][TestCase("branch-empty")]
        [TestCase("excess-depth")][TestCase("page-count")][TestCase("alias")]
        public void MalformedDirectoryStillFailsBeforePublicationAndReleasesCharges(string kind)
        {
            byte[] bytes=Malformed(kind);var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));
            try{
                var error=Assert.Throws<ArgumentException>(()=>Read(bytes,scope.OwnedReservation()));
                StringAssert.Contains(kind.EndsWith("empty")?"Empty allocated physical directory block":kind=="alias"?"Repeated physical child":"Physical directory sparse shape contradiction",error.Message);
            }finally{scope.Discard();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.False(budget.CaptureActive);
        }
        private static byte[] Malformed(string kind)
        {
            using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream,Encoding.UTF8,true)){
                int id=0;void Def(NavPhysicalStorageKind storage,NavPhysicalValueKind value){w.Write((byte)2);w.Write(id++);w.Write(new NavPhysicalTag(storage,value:value).Encoded);}
                void Empty(){Def(NavPhysicalStorageKind.DirectoryBlock,NavPhysicalValueKind.Integer);for(int n=0;n<32;n++)w.Write((byte)0);}
                void Page(){Def(NavPhysicalStorageKind.Page,NavPhysicalValueKind.Integer);w.Write(1);w.Write(4);w.Write(7);}
                void Leaf(){Def(NavPhysicalStorageKind.DirectoryBlock,NavPhysicalValueKind.Integer);Page();for(int n=1;n<32;n++)w.Write((byte)0);}
                w.Write(NavPhysicalBodyWire.Magic);w.Write(0);
                Def(NavPhysicalStorageKind.Array,NavPhysicalValueKind.Storage);w.Write(1);
                Def(NavPhysicalStorageKind.Directory,NavPhysicalValueKind.Integer);
                int depth=kind=="root-empty"?0:kind=="branch-empty"?2:1;
                w.Write(2048);w.Write(2048);w.Write(depth);w.Write(kind=="root-empty"?0:kind=="page-count"?2:1);
                w.Write(kind=="root-empty"?1:kind=="branch-empty"?4:kind=="excess-depth"?2:3);
                Def(NavPhysicalStorageKind.DirectoryBlock,NavPhysicalValueKind.Integer);
                if(kind=="root-empty"){for(int n=0;n<32;n++)w.Write((byte)0);}
                else if(kind=="nonzero-empty"){Leaf();Empty();for(int n=2;n<32;n++)w.Write((byte)0);}
                else if(kind=="branch-empty"){
                    Empty();Def(NavPhysicalStorageKind.DirectoryBlock,NavPhysicalValueKind.Integer);Leaf();for(int n=1;n<32;n++)w.Write((byte)0);
                    for(int n=2;n<32;n++)w.Write((byte)0);
                }else if(kind=="excess-depth"){Leaf();for(int n=1;n<32;n++)w.Write((byte)0);}
                else if(kind=="alias"){
                    int shared=id;Leaf();w.Write((byte)1);w.Write(shared);w.Write(new NavPhysicalTag(NavPhysicalStorageKind.DirectoryBlock,value:NavPhysicalValueKind.Integer).Encoded);
                    for(int n=2;n<32;n++)w.Write((byte)0);
                }else{Empty();Leaf();for(int n=2;n<32;n++)w.Write((byte)0);}
                long end=stream.Position;stream.Position=4;w.Write(id);stream.Position=end;return stream.ToArray();
            }
        }
        private static byte[] Capture(NavigationRetentionBudget budget,NavOwnedStorage root,int token=2)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,token,out var scope));
            try{using(var transport=NavStorageScratch.Reserve(scope.OwnedReservation(),2*131072))using(var stream=new MemoryStream(131072)){
                using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))NavPhysicalBodyWire.WriteTo(writer,root,scope.OwnedReservation());
                Assert.Less(stream.Length,131072);return stream.ToArray();
            }}finally{scope.Discard();}
        }
        private static NavOwnedStorage Read(byte[] bytes,INavStorageReservation reservation)
        {
            using(var stream=new MemoryStream(bytes,false))using(var reader=new BinaryReader(stream)){
                var root=NavPhysicalBodyWire.ReadFrom(reader,reservation);
                Assert.AreEqual(stream.Length,stream.Position);return root;
            }
        }
    }
}
