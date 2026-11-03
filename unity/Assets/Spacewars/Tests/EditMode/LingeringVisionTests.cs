using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class LingeringVisionTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private PlayableDomain domain;
        private PlayableProfile profile;
        private object Call(string method,params object[] args)=>typeof(PlayableDomain).GetMethod(method,Flags).Invoke(domain,args);
        private TeamVisionSnapshot Vision(PlayableOwner owner)
        {
            Call("RefreshVision");
            return ((PlayableVision)Call("Vision",owner)).Snapshot();
        }
        [SetUp] public void Setup()
        {
            profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            domain=new PlayableDomain(profile,1,false);
        }
        [TearDown] public void Cleanup()=>domain?.DestroyOwnedNavigation();

        [Test] public void LinearCirclesAreIndependentAndExpirationInvalidatesTheLastCircle()
        {
            var point=new NavPoint(0,25);
            Call("AddLingeringVision",PlayableOwner.Player,point,10d);
            Assert.True(Vision(PlayableOwner.Player).IsVisible(new NavPoint(9,25)));
            Assert.False(Vision(PlayableOwner.Enemy).IsVisible(new NavPoint(9,25)));
            Call("AdvanceLingeringVision",.5d);
            Assert.True(Vision(PlayableOwner.Player).IsVisible(new NavPoint(5,25)));
            Assert.False(Vision(PlayableOwner.Player).IsVisible(new NavPoint(5.01,25)));
            Call("AddLingeringVision",PlayableOwner.Player,point,4d);
            Call("AdvanceLingeringVision",.5d);
            Assert.True(Vision(PlayableOwner.Player).IsVisible(new NavPoint(1.9,25)));
            Assert.False(Vision(PlayableOwner.Player).IsVisible(new NavPoint(2.1,25)));
            Call("AdvanceLingeringVision",.5d);
            Assert.False(Vision(PlayableOwner.Player).IsVisible(point));
        }

        [Test] public void DestroyedUnitLeavesItsActualRadiusAndCurrentSaveContinuesExactly()
        {
            var point=new NavPoint(0,25);
            int id=(int)Call("SpawnUnit",point,PlayableOwner.Player,PlayableEntityKind.Tank);
            double radius=PlayableUnitRules.Vision(profile,PlayableEntityKind.Tank);
            Call("Damage",id,int.MaxValue);
            Assert.True(Vision(PlayableOwner.Player).IsVisible(new NavPoint(point.X+radius-.01,point.Z)));
            Call("AdvanceLingeringVision",.25d);Call("RefreshVision");
            byte[] bytes=domain.CaptureWorldBytes(7,"lingering-current");
            var restored=PlayableDomain.RestoreWorldBytes(bytes,profile,7,"lingering-current");
            try{
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"lingering-current"));
                for(int i=0;i<31;i++){
                    domain.Step(1d/30);restored.Step(1d/30);
                    CollectionAssert.AreEqual(domain.CaptureWorldBytes(7,"lingering-current"),restored.CaptureWorldBytes(7,"lingering-current"));
                }
            }finally{restored.DestroyOwnedNavigation();}
        }

        [Test] public void MineAndItsVictimLeaveTheirOwnDifferentCircles()
        {
            var point=new NavPoint(0,25);
            Call("PlaceMine",PlayableOwner.Player,point);
            int id=(int)Call("SpawnUnit",point,PlayableOwner.Enemy,PlayableEntityKind.Explorer);
            Call("Damage",id,profile.ExplorerHealth-1);
            Call("AdvanceMines");
            double mineRadius=profile.CopyData().orbitalMineVision;
            double victimRadius=PlayableUnitRules.Vision(profile,PlayableEntityKind.Explorer);
            Assert.AreNotEqual(mineRadius,victimRadius);
            Assert.True(Vision(PlayableOwner.Player).IsVisible(new NavPoint(point.X+mineRadius-.01,point.Z)));
            Assert.False(Vision(PlayableOwner.Player).IsVisible(new NavPoint(point.X+mineRadius+.01,point.Z)));
            Assert.True(Vision(PlayableOwner.Enemy).IsVisible(new NavPoint(point.X+victimRadius-.01,point.Z)));
            Call("AdvanceLingeringVision",1d);
            Assert.False(Vision(PlayableOwner.Player).IsVisible(point));
            Assert.False(Vision(PlayableOwner.Enemy).IsVisible(point));
        }

        [Test] public void RelayCircleStartsAtNeutralizationAndIsSharedWithAllies()
        {
            domain.DestroyOwnedNavigation();
            profile=PlayableProfile.Default;
            domain=new PlayableDomain(profile,1,RelayTowerContractTests.Configuration(profile));
            int scout=(int)Call("SpawnUnit",new NavPoint(2.5,0),PlayableOwner.Player,PlayableEntityKind.Explorer);
            Call("AdvanceCapture",100d);Call("AdvanceCapture",100d);
            Call("Damage",scout,int.MaxValue);Call("AdvanceLingeringVision",1d);
            Call("SpawnUnit",new NavPoint(-2.5,0),PlayableOwner.Enemy,PlayableEntityKind.Explorer);
            Call("AdvanceCapture",100d);
            Assert.True(Vision(PlayableOwner.Player).Sources.Any(s=>s.Position.Equals(default(NavPoint))&&s.Radius==profile.RelayVisionRange));
            Assert.True(Vision(PlayableOwner.Third).Sources.Any(s=>s.Position.Equals(default(NavPoint))&&s.Radius==profile.RelayVisionRange));
            Call("AdvanceLingeringVision",.5d);
            Assert.True(Vision(PlayableOwner.Player).Sources.Any(s=>s.Position.Equals(default(NavPoint))&&s.Radius==profile.RelayVisionRange/2));
            Call("AdvanceLingeringVision",.5d);
            Assert.False(Vision(PlayableOwner.Player).Sources.Any(s=>s.Position.Equals(default(NavPoint))));
        }

        [Test] public void ThirtyOrdinaryTicksExpireOneSecondWithoutAnExtraTick()
        {
            var point=new NavPoint(0,25);
            Call("AddLingeringVision",PlayableOwner.Player,point,10d);
            for(int i=0;i<30;i++)Call("AdvanceLingeringVision",1d/30);
            Assert.False(Vision(PlayableOwner.Player).IsVisible(point));
        }

        [Test] public void DestroyedBuildingLeavesItsLastPhaseRadius()
        {
            var buildings=(System.Collections.IDictionary)typeof(PlayableDomain).GetField("buildings",Flags).GetValue(domain);
            var building=buildings.Values.Cast<object>().First(b=>(PlayableOwner)b.GetType().GetField("Owner").GetValue(b)==PlayableOwner.Player);
            var type=building.GetType();
            int id=(int)type.GetField("Id").GetValue(building);
            var point=(NavPoint)type.GetField("Position").GetValue(building);
            var kind=(PlayableBuildingKind)type.GetField("Kind").GetValue(building);
            var phase=(ConstructionPhase)type.GetField("Phase").GetValue(building);
            double radius=PlayableVision.BuildingRadius(profile,kind,phase);
            Call("Damage",id,int.MaxValue);
            Assert.False(buildings.Contains(id));
            Assert.True(Vision(PlayableOwner.Player).Sources.Any(s=>s.Position.Equals(point)&&s.Radius==radius));
            Call("AdvanceLingeringVision",.5d);
            Assert.True(Vision(PlayableOwner.Player).Sources.Any(s=>s.Position.Equals(point)&&s.Radius==radius/2));
        }

        [Test] public void LabDurationHasValidatedBoundsAndZeroDisablesResidualSources()
        {
            var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="vision.lingeringSeconds");
            Assert.NotNull(field);
            var data=profile.CopyData();data.lingeringVisionSeconds=-1;
            Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data,new AiTestMap()));
            data.lingeringVisionSeconds=0;domain.DestroyOwnedNavigation();
            domain=new PlayableDomain(PlayableProfile.Create(data,new AiTestMap()),1,false);
            Call("AddLingeringVision",PlayableOwner.Player,new NavPoint(0,25),10d);
            Assert.False(Vision(PlayableOwner.Player).IsVisible(new NavPoint(0,25)));
        }
    }
}
