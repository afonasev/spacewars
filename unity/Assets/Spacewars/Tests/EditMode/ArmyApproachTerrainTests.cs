using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class ArmyApproachTerrainTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        // Exact observed positions from the current-profile ordinary Null Dock #2.
        // Target10 is the adjacent control; target12 is the repeated later defense target.
        [TestCase(-49.83809201294313,-1.896364217791661)]
        [TestCase(-45.880730390912376,-.8292005044928629)]
        public void ObservedNullDockTankApproachIsSupportedBeforeMoveAdmission(double targetX,double targetZ)
        {
            var source=JObject.Parse(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures/Oct29AiMatches.json")));
            var scenario=source["Scenarios"].Single(s=>(int)s["Number"]==2);
            var profile=PlayableProfile.Create(source["Profile"].ToObject<PlayableProfileData>(),new NullDockMap());
            var lobby=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=(int)scenario["Seed"],NullDock=true,
                Participants=scenario["Participants"].Select(p=>new NativeLobbyParticipant{Name=(string)p["Id"],Team=(int)p["Team"],Color=(int)p["Color"],Difficulty=(AiDifficulty)Enum.Parse(typeof(AiDifficulty),(string)p["Difficulty"])}).ToList()};
            var domain=new PlayableDomain(profile,2,NativeLobbyMatch.Create(profile,lobby));
            object Call(string method,params object[] args)=>typeof(PlayableDomain).GetMethod(method,F).Invoke(domain,args);
            try{
                foreach(var point in new[]{new NavPoint(-59.5,0),new NavPoint(-63.99138953656284,6.181867355918498),new NavPoint(-71.25861046343715,3.820604139901076)})
                    Call("AddBuilding",PlayableOwner.Enemy,PlayableBuildingKind.Refinery,point,true);
                Call("AddBuilding",PlayableOwner.Enemy,PlayableBuildingKind.Factory,new NavPoint(-71.25861046343715,-3.8206041399010746),true);Call("RebuildGeometry");
                var from=new NavPoint(-59.436112780099776,-4.330025149931526);var target=new NavPoint(targetX,targetZ);
                int id=(int)Call("SpawnUnit",from,PlayableOwner.Enemy,PlayableEntityKind.Tank);
                Assert.True(domain.Navigation.Crowd.Locate(from,profile.TankCollisionRadius,out _));
                Assert.True(domain.TryAttackApproach(id,from,target,out var approach),"Observed visible target has a legal in-range approach");
                TestContext.WriteLine($"NULL_DOCK_APPROACH from={from.X:R},{from.Z:R} target={target.X:R},{target.Z:R} goal={approach.X:R},{approach.Z:R}");
                Assert.True(domain.Geometry.IsFree(approach,profile.TankCollisionRadius));
                Assert.LessOrEqual(Math.Sqrt(Math.Pow(approach.X-target.X,2)+Math.Pow(approach.Z-target.Z,2)),PlayableUnitRules.Range(profile,PlayableEntityKind.Tank));
                Assert.True(domain.Navigation.Crowd.Locate(approach,profile.TankCollisionRadius,out _),"Attack approach must satisfy the same actual terrain admission as ordinary Move");
                Assert.True(domain.Navigation.Move(id,approach),"The selected point must be admissible by the real navigation session");
            }finally{domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}
        }
    }
}
