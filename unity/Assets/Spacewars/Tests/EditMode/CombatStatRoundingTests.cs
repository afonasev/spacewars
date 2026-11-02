using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Presentation;
namespace Spacewars.Tests.EditMode
{
    public sealed class CombatStatRoundingTests
    {
        [TestCase(138,140)] [TestCase(156,155)] [TestCase(80.5,80)] [TestCase(312.5,315)] [TestCase(437.5,440)]
        public void MaximumRoundsToFiveWithHalfUp(double input,double expected)=>Assert.AreEqual(expected,PlayableStatRounding.MaximumHealth(PlayableProfile.Default,input));
        [TestCase(2.34,2.3)] [TestCase(2.35,2.4)] [TestCase(2.3,2.3)] [TestCase(9.25,9.3)]
        public void WeaponRoundsToTenths(double input,double expected)=>Assert.AreEqual(expected,PlayableStatRounding.Damage(PlayableProfile.Default,input));
        [Test] public void TooltipsKeepCurrentHealthIntegerAndFinalStatsMatchTheProfile()
        {
            var p=PlayableProfile.Default;
            var technology=new[]{new PlayableResearchOrderSnapshot(1,PlayableResearchKind.TankChassis,0,0,0,0,false,true),new PlayableResearchOrderSnapshot(2,PlayableResearchKind.ImprovedDefensiveSystems,0,0,0,0,false,true)};
            StringAssert.Contains("140",PlayableMenuDescriptions.Unit(p,PlayableEntityKind.Tank,technology));
            var u=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(0,0),10,false,0,0,0,maxHealth:140,exactHealth:9.2);
            var text=PlayableMenuDescriptions.Unit(p,PlayableEntityKind.Tank,entity:u);
            StringAssert.Contains("10 / 140",text);StringAssert.DoesNotContain("9.2 /",text);StringAssert.DoesNotContain("9,2 /",text);
            Assert.AreEqual(1,PlayableStatRounding.DisplayHealth(.01));Assert.AreEqual(10,PlayableStatRounding.DisplayHealth(10));
        }
        [Test] public void RoundingControlsAreBoundedAndSerializedWithProfileCopies()
        {
            var p=PlayableProfile.Default;Assert.AreEqual(5,p.MaximumHealthRoundingStep);Assert.AreEqual(.1,p.DamageRoundingStep);
            var fields=PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("combatRounding.")).ToArray();Assert.AreEqual(2,fields.Length);
            foreach(var f in fields){Assert.IsNotEmpty(f.Description);Assert.IsNotEmpty(f.Unit);Assert.Greater(f.Step,0);foreach(var value in new[]{f.Minimum-f.Step,f.Maximum+f.Step,double.NaN}){var data=p.CopyData();typeof(PlayableProfileData).GetField(f.FieldName).SetValue(data,value);Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));}}
            var copy=PlayableProfile.Create(p.CopyData());Assert.AreEqual(p.MaximumHealthRoundingStep,copy.MaximumHealthRoundingStep);Assert.AreEqual(p.DamageRoundingStep,copy.DamageRoundingStep);
        }
    }
}
