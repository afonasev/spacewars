using System;
using System.IO;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class AiCombatMicroExecutionMatchTests
    {
        static byte[] Bytes(Action<BinaryWriter> write){using(var m=new MemoryStream()){using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))write(w);return m.ToArray();}}
        static NavLocation At(double x,double z=0)=>new NavLocation(new NavPoint(x,z),"floor");
        static MotionExecutionProposal Proposal(double middle,double height=0)
        {
            var binding=new MotionExecutionBinding(905,11,1,2,3,1,1,1,7,8,9,"owner","route","profile","geometry",null,"policy");long grant=10000;
            var via=At(1,middle);var legs=new[]{new NavTypedLeg(At(0),via,1,NavTypedLegKind.Surface,null),new NavTypedLeg(via,At(3),2,NavTypedLegKind.Surface,null)};
            Assert.True(MotionExecutionRemainingBranch.TryCreate(binding,At(0),At(3),0,0,legs,ref grant,_=>true,out var branch));
            var trace=new[]{new MotionExecutionTracePiece(MotionExecutionTraceKind.Surface,MotionExecutionLaw.AffineSupportChord,At(0),At(.5),"support",0,0,height,0,.5),new MotionExecutionTracePiece(MotionExecutionTraceKind.Surface,MotionExecutionLaw.AffineSupportChord,At(.5),At(1),"support",0,0,0,.5,1)};
            Assert.True(MotionExecutionPrimitive.TryCreate(binding,1,MotionExecutionKind.AttachmentShortcutRejoin,30,.033,At(0),At(1),At(3),.6,"tank",0,0,1,10,7,trace,ref grant,_=>true,out var primitive,branch));Assert.True(MotionExecutionProposal.TryCreate(905,11,1,10,30,new[]{primitive},ref grant,_=>true,out var proposal));return proposal;
        }
        [TestCase(1,0,true)][TestCase(-1,0,false)][TestCase(1,3,false)]
        public void EveryActualTraceAndRemainingLegIsPaidEvenForMatchingHeader(double middle,double height,bool expectedEqual)
        {
            var expected=Proposal(1);var actual=Proposal(middle,height);var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;Assert.True(storage.TryReserve(5000,out var expectedRoot));Assert.True(storage.TryReserve(5000,out var actualRoot));
            using(expectedRoot)using(actualRoot){Assert.True(AiCombatMicroExecutionMatch.TryCreate(expected,actual,expectedRoot,actualRoot,storage,out var work));using(work){var initial=Bytes(work.Write);Assert.Zero(work.Advance(0,()=>throw new Exception("zero grant")));Assert.Zero(work.Advance(1,()=>false));CollectionAssert.AreEqual(initial,Bytes(work.Write));while(!work.Complete){Assert.AreEqual(1,work.Advance(1));var bytes=Bytes(work.Write);AiCombatMicroExecutionMatch cold;using(var r=new BinaryReader(new MemoryStream(bytes)))cold=AiCombatMicroExecutionMatch.Read(r,expected,actual,expectedRoot,actualRoot,storage);using(cold){CollectionAssert.AreEqual(bytes,Bytes(cold.Write));Assert.AreEqual(work.Advance(1),cold.Advance(1));CollectionAssert.AreEqual(Bytes(work.Write),Bytes(cold.Write));}}Assert.AreEqual(expectedEqual,work.Equal);Assert.Greater(work.Consumed,2);}}
            Assert.AreEqual(baseline,storage.Retained);
        }
        [Test]public void IdenticalReferenceStillNeedsAllComparisonStepsAndRefusedBirthKeepsLiability()
        {
            var proposal=Proposal(1);var storage=new AiCombatMicroExecutionStorage(5000);Assert.True(storage.TryReserve(1000,out var root));using(root){long before=storage.Retained;var tiny=new AiCombatMicroExecutionStorage(128);Assert.False(AiCombatMicroExecutionMatch.TryCreate(proposal,proposal,root,root,tiny,out _));Assert.AreEqual(before,storage.Retained);Assert.True(AiCombatMicroExecutionMatch.TryCreate(proposal,proposal,root,root,storage,out var work));using(work){Assert.AreEqual(1,work.Advance(1));Assert.False(work.Complete);Assert.Throws<InvalidOperationException>(()=>{var ignored=work.Equal;});while(!work.Complete)work.Advance(1);Assert.True(work.Equal);Assert.AreEqual(9,work.Consumed);}}
        }
    }
}
