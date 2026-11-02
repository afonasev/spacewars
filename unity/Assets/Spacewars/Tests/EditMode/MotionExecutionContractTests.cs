using System;
using System.IO;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed class MotionExecutionContractTests
    {
        static NavLocation At(double x,string surface="flat")=>new NavLocation(new NavPoint(x,0),surface);
        static MotionExecutionBinding Binding(long entity,long group=11,long request=7,MotionOwnIntentKind kind=MotionOwnIntentKind.Grouped)=>new MotionExecutionBinding(905,group,group==0?0:1,kind==MotionOwnIntentKind.Grouped?1:0,kind==MotionOwnIntentKind.Grouped?22:0,entity,3,1,request,request==0?0:2,kind==MotionOwnIntentKind.Grouped?1:0,"player-1",request==0?null:"route","profile","geometry",null,"owner-policy",kind);
        static MotionExecutionTracePiece Piece(double a,double b,double lo=0,double hi=1,double height=0)=>new MotionExecutionTracePiece(MotionExecutionTraceKind.Surface,MotionExecutionLaw.AffineSupportChord,At(a),At(b),"floor",0,0,height,lo,hi);
        static bool Create(MotionExecutionBinding b,long id,MotionExecutionTracePiece[] pieces,ref long grant,out MotionExecutionPrimitive p,Func<long,bool> reserve=null)=>MotionExecutionPrimitive.TryCreate(b,id,MotionExecutionKind.CooperativeYield,30,.033,At(0),At(1),b.HasOwnIntent?(NavLocation?)At(3):null,.6,"tank",0,.1,1,77,b.Request,pieces,ref grant,reserve??(_=>true),out p);
        [Test]public void CooperativeBundleRetainsForeignOwnGroupAndIdleReceiverWithoutInventedIntent()
        {
            long grant=1000;Assert.True(Create(Binding(1),1,new[]{Piece(0,1)},ref grant,out var own));Assert.True(Create(Binding(2,22,8),2,new[]{Piece(0,1)},ref grant,out var peer));Assert.True(Create(Binding(3,0,0,MotionOwnIntentKind.None),3,new[]{Piece(0,1)},ref grant,out var idle));
            Assert.True(MotionExecutionProposal.TryCreate(905,11,1,77,30,new[]{own,peer,idle},ref grant,_=>true,out var proposal));Assert.AreEqual(22,proposal.Primitive(1).Binding.Group);Assert.AreEqual(MotionOwnIntentKind.None,proposal.Primitive(2).Binding.OwnIntent);Assert.Zero(proposal.Primitive(2).OriginalOwnRequest);Assert.Null(proposal.Primitive(2).AssignedEndpoint);
            Assert.Throws<ArgumentException>(()=>MotionExecutionProposal.TryCreate(905,11,1,77,30,new[]{own,own},ref grant,_=>true,out _));
        }
        [Test]public void StandaloneOwnRequestDoesNotNeedFakeGroupOrUnavailableRoot()
        {long grant=100;Assert.True(Create(Binding(1,0,9,MotionOwnIntentKind.Standalone),1,new[]{Piece(0,1)},ref grant,out var p));Assert.Zero(p.Binding.Group);Assert.Zero(p.Binding.RootOrder);Assert.AreEqual(9,p.OriginalOwnRequest);Assert.True(p.AssignedEndpoint.HasValue);}
        [Test]public void ExactTraceRejectsGapsOverlapAndNonzeroPortalCuts()
        {
            long grant=1000;var b=Binding(1);
            Assert.Throws<ArgumentException>(()=>Create(b,1,new[]{Piece(0,.5,0,.4),Piece(.5,1,.5,1)},ref grant,out _));
            Assert.Throws<ArgumentException>(()=>Create(b,1,new[]{Piece(0,.5,0,.6),Piece(.5,1,.5,1)},ref grant,out _));
            var portal=new MotionExecutionTracePiece(MotionExecutionTraceKind.Portal,MotionExecutionLaw.SemanticPortalCrossing,At(.5),At(.5),null,0,0,0,.5,.6,"portal");
            Assert.Throws<ArgumentException>(()=>Create(b,1,new[]{Piece(0,.5,0,.5),portal,Piece(.5,1,.6,1)},ref grant,out _));
        }
        [Test]public void SameHeaderDoesNotAuthorizeChangedSupportTranscript()
        {long grant=100;Assert.True(Create(Binding(1),1,new[]{Piece(0,1)},ref grant,out var a));Assert.True(Create(Binding(1),1,new[]{Piece(0,1,height:3)},ref grant,out var b));Assert.True(a.SameHeader(b));Assert.False(a.SameTracePiece(b,0));}
        [Test]public void SameActualChordAndRemainingHeadersCannotHideChangedRemainingLegs()
        {
            var binding=Binding(1);long grant=1000;
            var north=new NavLocation(new NavPoint(1,1),"flat");var south=new NavLocation(new NavPoint(1,-1),"flat");
            var a=new[]{new NavTypedLeg(At(0),north,Math.Sqrt(2),NavTypedLegKind.Surface,null),new NavTypedLeg(north,At(3),Math.Sqrt(5),NavTypedLegKind.Surface,null)};
            var b=new[]{new NavTypedLeg(At(0),south,Math.Sqrt(2),NavTypedLegKind.Surface,null),new NavTypedLeg(south,At(3),Math.Sqrt(5),NavTypedLegKind.Surface,null)};
            Assert.True(MotionExecutionRemainingBranch.TryCreate(binding,At(0),At(3),0,0,a,ref grant,_=>true,out var ra));Assert.True(MotionExecutionRemainingBranch.TryCreate(binding,At(0),At(3),0,0,b,ref grant,_=>true,out var rb));
            Assert.True(MotionExecutionPrimitive.TryCreate(binding,1,MotionExecutionKind.CooperativeYield,30,.033,At(0),At(1),At(3),.6,"tank",0,.1,1,77,7,new[]{Piece(0,1)},ref grant,_=>true,out var pa,ra));
            Assert.True(MotionExecutionPrimitive.TryCreate(binding,1,MotionExecutionKind.CooperativeYield,30,.033,At(0),At(1),At(3),.6,"tank",0,.1,1,77,7,new[]{Piece(0,1)},ref grant,_=>true,out var pb,rb));
            Assert.True(pa.SameHeader(pb));Assert.True(pa.SameTracePiece(pb,0));Assert.True(ra.SameHeader(rb));Assert.False(ra.SameLeg(rb,0));Assert.False(ra.SameLeg(rb,1));
        }
        [Test]public void CopyWorkAndMemoryRefusalLeaveRealGrantUnspent()
        {
            long grant=1;Assert.False(Create(Binding(1),1,new[]{Piece(0,1)},ref grant,out _,_=>throw new Exception("unpaid copy")));Assert.AreEqual(1,grant);
            grant=2;Assert.False(Create(Binding(1),1,new[]{Piece(0,1)},ref grant,out _,_=>false));Assert.AreEqual(2,grant);Assert.True(Create(Binding(1),1,new[]{Piece(0,1)},ref grant,out _));Assert.Zero(grant);
        }
        [Test]public void RealNewAndRestoredOwnerConstructorPreparesProductionPredicateModels()
        {
            var p=PlayableProfile.Default;bool alreadyReady=BuildingCombatGeometry.TryPreparedPose(p,PlayableBuildingKind.GaussTower,false,default,0,out _);TestContext.WriteLine("Models ready before real owner construction: "+alreadyReady+" (a warm runner is not a cold-process claim)");
            var ai=AiProfile.Initial;var owner=new PlayableAiOwnerLoop(p,PlayableAiOpeningComposition.Initialize(7,"player-1",aiProfile:ai),905,null,ai,AiDifficulty.Veteran);
            foreach(PlayableBuildingKind kind in Enum.GetValues(typeof(PlayableBuildingKind))){Assert.True(BuildingCombatGeometry.TryPreparedPose(p,kind,false,default,0,out var pose));Assert.True(pose.TryCreateSegmentWork(new BallisticPoint(1000,10,1000),new BallisticPoint(1001,10,1001),.06,_=>true,out var work));while(work.Status==CombatPredicateStatus.Pending)work.Advance(1);Assert.False(work.Hit);}
            Assert.True(BuildingCombatGeometry.TryPreparedPose(p,PlayableBuildingKind.ScientificCenter,true,default,0,out _),"Real owner startup prepares upgraded scanner before safety work");
            byte[] bytes=WorldWire.Pack(owner.WriteState);var cold=WorldWire.Unpack(bytes,r=>PlayableAiOwnerLoop.ReadState(r,p,ai,905,7,0));CollectionAssert.AreEqual(bytes,WorldWire.Pack(cold.WriteState));owner.Stop(0);cold.Stop(0);
        }
    }
}
