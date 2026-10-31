using System;using System.Linq;using NUnit.Framework;using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class Oct28ArrivalSlotOrderingTests
    {
        static NavLocation At(double x,double z,string surface="ground")=>NavigationArrivalTestFactory.At(x,z,surface);
        static double Cost(NavArrivalMember[] members,System.Collections.Generic.IReadOnlyList<NavLocation> slots){double result=0;for(int i=0;i<members.Length;i++){var a=members[i].Origin.Position;var b=slots[i].Position;result+=Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));}return result;}
        [Test] public void PermutationHasMinimumTravelAndPreservesFallbackTailAndMemberBindings()
        {
            var members=new[]{NavigationArrivalTestFactory.Member(1,0,At(-2,-3)),NavigationArrivalTestFactory.Member(2,0,At(0,-3)),NavigationArrivalTestFactory.Member(3,0,At(2,-3)),NavigationArrivalTestFactory.Member(4,0,At(-2,-5))};
            var slots=new[]{At(0,24),At(-3.6,20.4),At(0,20.4),At(3.6,20.4),At(-3.6,24),At(3.6,24)};
            var bindings=members.Select(m=>m.StableKey).ToArray();var ordered=NavigationArrivalSlotOrdering.Align(slots,members.Reverse());
            double best=double.PositiveInfinity;var prefix=slots.Take(4).ToArray();
            void Permute(int index){if(index==prefix.Length){best=Math.Min(best,Cost(members,prefix));return;}for(int i=index;i<prefix.Length;i++){var v=prefix[index];prefix[index]=prefix[i];prefix[i]=v;Permute(index+1);v=prefix[index];prefix[index]=prefix[i];prefix[i]=v;}}
            Permute(0);Assert.AreEqual(best,Cost(members,ordered),1e-10);
            CollectionAssert.AreEquivalent(slots.Take(4),ordered.Take(4));CollectionAssert.AreEqual(slots.Skip(4),ordered.Skip(4));CollectionAssert.AreEqual(bindings,members.Select(m=>m.StableKey));
            CollectionAssert.AreEqual(ordered,NavigationArrivalSlotOrdering.Align(slots,members));
        }
        [Test] public void CapturedDenseGroupRemovesProperStraightAssignmentCrossings()
        {
            var members=Enumerable.Range(0,18).Select(i=>NavigationArrivalTestFactory.Member(5+i,0,At((i%3-1)*2,-3-(i/3)*2),i%2==0?.72:.58)).ToArray();
            var slots=new System.Collections.Generic.List<NavLocation>();for(int ring=0;slots.Count<18;ring++)for(int z=-ring;z<=ring&&slots.Count<18;z++)for(int x=-ring;x<=ring&&slots.Count<18;x++)if(ring==0||Math.Abs(x)==ring||Math.Abs(z)==ring)slots.Add(At(x*3.6,24+z*3.6));
            var result=NavigationArrivalSlotOrdering.Align(slots,members);
            double Side(NavPoint a,NavPoint b,NavPoint c)=>(b.X-a.X)*(c.Z-a.Z)-(b.Z-a.Z)*(c.X-a.X);
            for(int i=0;i<members.Length;i++)for(int j=i+1;j<members.Length;j++){
                var a=members[i].Origin.Position;var b=result[i].Position;var c=members[j].Origin.Position;var d=result[j].Position;
                Assert.False(Side(a,b,c)*Side(a,b,d)<0&&Side(c,d,a)*Side(c,d,b)<0,"proper assignment crossing "+i+"/"+j);
            }
        }
        [Test] public void MixedSurfacesAndInsufficientSlotsKeepOriginalCandidateOrder()
        {
            var slots=new[]{At(0,24),At(-3.6,20.4)};
            var members=new[]{NavigationArrivalTestFactory.Member(1,0,At(0,0)),NavigationArrivalTestFactory.Member(2,0,At(1,0,"upper"))};
            Assert.AreSame(slots,NavigationArrivalSlotOrdering.Align(slots,members));
            Assert.AreSame(slots,NavigationArrivalSlotOrdering.Align(slots,members.Concat(new[]{NavigationArrivalTestFactory.Member(3,0,At(2,0))})));
        }
    }
}
