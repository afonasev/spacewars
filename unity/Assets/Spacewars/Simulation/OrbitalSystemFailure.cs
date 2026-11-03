using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    public readonly struct OrbitalBuildingTarget
    {
        public readonly int Id;public readonly PlayableBuildingKind Kind;public readonly NavPoint Position;
        public OrbitalBuildingTarget(int id,PlayableBuildingKind kind,NavPoint position){Id=id;Kind=kind;Position=position;}
    }
    public static class OrbitalSystemFailure
    {
        // Callers filter allegiance and existing target eligibility before selection.
        public static int[] Select(IEnumerable<OrbitalBuildingTarget> candidates,NavPoint center,double radius,int maximum)
        {
            double Distance(OrbitalBuildingTarget b){double x=b.Position.X-center.X,z=b.Position.Z-center.Z;return x*x+z*z;}
            var inside=candidates.Where(b=>Distance(b)<=radius*radius).ToArray();
            return inside.Where(b=>!TerritoryRules.Defensive(b.Kind)).OrderBy(Distance).ThenBy(b=>b.Id).Take(maximum).Select(b=>b.Id)
                .Concat(inside.Where(b=>TerritoryRules.Defensive(b.Kind)).OrderBy(b=>b.Id).Select(b=>b.Id)).ToArray();
        }
    }
}
