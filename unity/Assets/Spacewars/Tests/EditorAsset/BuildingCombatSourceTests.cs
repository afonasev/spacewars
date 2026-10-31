using NUnit.Framework;
namespace Spacewars.Tests.EditorAsset
{
 public sealed class BuildingCombatSourceTests
 {
  [Test] public void CombatSurfacesMatchEveryCurrentPrefabAndMotionProfile()=>Spacewars.Editor.BuildingCombatExport.Validate();
 }
}
