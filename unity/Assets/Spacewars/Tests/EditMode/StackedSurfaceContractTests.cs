using System;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    // Contract fixture only; the initiative's actual stacked-map routing/crowd gate remains open.
    public sealed class StackedSurfaceContractTests
    {
        private sealed class Stacked : IPlayableTerrain
        {
            public string Id=>"stacked-contract";public int Revision=>1;public int SurfaceSemanticsVersion=>1;public double HalfExtent=>10;public double DirectFireHeight=>1;
            public IReadOnlyList<MapSupport> Supports{get;}=new[]{new MapSupport("repeated",new NavObstacle(-10,-10,10,10),false,surfaceId:"lower"),new MapSupport("repeated",new NavObstacle(-10,-10,10,10),false,5,surfaceId:"upper")};
            public IReadOnlyList<NavObstacle> Solids=>Array.Empty<NavObstacle>();public IReadOnlyList<NavObstacle> MovementBlockers=>Solids;public IReadOnlyList<NavSurfaceTransition> SurfaceTransitions=>Array.Empty<NavSurfaceTransition>();
            public MapSupport SupportAt(NavPoint p)=>null;public double SurfaceHeight(NavPoint p)=>throw new InvalidOperationException("Ambiguous XZ");public NavPoint SurfaceGradient(NavPoint p)=>default;
            public bool SupportsFootprint(NavPoint p,double radius)=>Math.Abs(p.X)+radius<=10&&Math.Abs(p.Z)+radius<=10;
            public bool SupportsSweep(NavPoint a,NavPoint b,double radius)=>SupportsFootprint(a,radius)&&SupportsFootprint(b,radius);
            public TerritorySite[] Sites(PlayableProfile p)=>Array.Empty<TerritorySite>();public NavPoint Headquarters(PlayableOwner owner)=>default;
            public bool TryLocate(NavPoint p,double radius,out NavLocation location){location=default;return false;} // Explicit layer required.
            public bool IsValidLocation(NavLocation location,double radius)=>(location.SurfaceId=="lower"||location.SurfaceId=="upper")&&SupportsFootprint(location.Position,radius);
            public bool TryTraverse(NavLocation from,NavPoint to,double radius,out NavLocation location){location=default;if(!IsValidLocation(from,radius)||!SupportsSweep(from.Position,to,radius))return false;location=new NavLocation(to,from.SurfaceId);return true;}
            public bool CompatibleCombatSurface(NavLocation a,NavLocation b)=>IsValidLocation(a,0)&&IsValidLocation(b,0)&&a.SurfaceId==b.SurfaceId;
        }
        [Test] public void SameXZLayersRemainDistinctAndWrongAssignedLayerCannotInstall()
        {
            var m=new Stacked();var p=NavigationProfile.Default;var g=new NavGeometry(10,Array.Empty<NavObstacle>(),1);var crowd=new NavCrowd(g,p,m);var start=new NavPoint(-3,0);var goal=new NavPoint(3,0);
            var lower=new NavLocation(goal,"lower");var upper=new NavLocation(goal,"upper");Assert.AreNotEqual(lower,upper);Assert.True(m.IsValidLocation(lower,1));Assert.True(m.IsValidLocation(upper,1));Assert.False(m.TryLocate(goal,1,out _));Assert.False(m.CompatibleCombatSurface(lower,upper));
            var actor=crowd.Add(1,start,1,2,3,new NavLocation(start,"lower"));Assert.False(crowd.SetRoute(1,goal,new[]{goal},upper));Assert.False(actor.Moving);Assert.AreEqual("lower",actor.Location.SurfaceId);
            Assert.True(crowd.SetRoute(1,goal,new[]{goal},lower));for(int i=0;i<600&&actor.Moving;i++)crowd.Step(1d/30);Assert.AreEqual(NavigationOutcome.Arrived,actor.Outcome);Assert.AreEqual("lower",actor.Location.SurfaceId);
        }
        [Test] public void SavedWrongLayerRouteAndUnboundReferencesRejectBeforePublication()
        {
            var m=new Stacked();var g=new NavGeometry(10,Array.Empty<NavObstacle>(),1);var p=NavigationProfile.Default;var nav=new NavigationSession(1,g,p,terrain:m);var from=new NavPoint(-3,0);var goal=new NavPoint(3,0);
            nav.Crowd.Add(1,from,1,2,3,new NavLocation(from,"lower"));Assert.True(nav.Crowd.SetRoute(1,goal,new[]{goal},new NavLocation(goal,"lower")));
            var state=nav.CaptureState();var restored=new NavigationSession(1,g,p,terrain:m);restored.RestoreState(state);Assert.AreEqual("lower",restored.Crowd.Units[0].Location.SurfaceId);
            state.Crowd.Units[0].Location=new NavLocation(from,"upper");Assert.Throws<ArgumentException>(()=>new NavigationSession(1,g,p,terrain:m).RestoreState(state));
            state.Crowd.Units[0].Location=null;Assert.Throws<ArgumentException>(()=>new NavigationSession(1,g,p,terrain:m).RestoreState(state));
        }
    }
}
