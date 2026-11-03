using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private readonly Dictionary<int,double> systemFailures=new Dictionary<int,double>();
        private double SystemFailureRemaining(int id)=>systemFailures.TryGetValue(id,out var until)&&buildings.ContainsKey(id)?System.Math.Max(0,until-elapsed):0;
        private bool SystemsDisabled(int id)=>SystemFailureRemaining(id)>0;
        private int[] SystemFailureTargets(PlayableOwner owner,NavPoint center)=>OrbitalSystemFailure.Select(
            buildings.Values.Where(b=>HasOwner(b.Owner)&&b.Health>0&&b.Phase!=ConstructionPhase.Pending&&Hostile(owner,b.Owner))
                .Select(b=>new OrbitalBuildingTarget(b.Id,b.Kind,b.Position)),center,OrbitalData.orbitalSystemFailureRadius,OrbitalData.orbitalSystemFailureMaxTargets);
    }
}
