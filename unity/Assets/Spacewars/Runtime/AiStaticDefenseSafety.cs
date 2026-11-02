using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    internal static class AiStaticDefenseSafety
    {
        // Known positions are facts acquired by this owner, never a world query.
        internal static bool Withdrawal(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot unit,IEnumerable<NavPoint> path,AiKnowledgeState knowledge=null)
        {
            if(knowledge!=null&&(knowledge.OwnerId!=o.OwnerId||knowledge.Generation!=o.Generation||knowledge.ObservationTick>o.Tick))throw new ArgumentException("Foreign static-defense knowledge");
            var towers=o.Buildings.Where(b=>o.IsHostile(b.Owner)&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&TerritoryRules.ArmedDefense(b.Kind)).ToList();
            foreach(var c in knowledge?.Contacts??Array.Empty<AiKnownContact>())
                if(c.Building&&o.IsHostile(c.Owner)&&c.HealthAtLastSeen>0&&c.Confidence>0&&Enum.IsDefined(typeof(PlayableBuildingKind),c.Kind)&&TerritoryRules.ArmedDefense((PlayableBuildingKind)c.Kind)&&!towers.Any(b=>b.Id==c.Id))
                    towers.Add(new PlayableBuildingSnapshot(c.Id,c.Owner,(PlayableBuildingKind)c.Kind,c.Position,c.HealthAtLastSeen,1,0,0,default,includePrivateState:false));
            var points=path.ToArray();if(points.Length==0)return false;
            bool air=unit.Kind==PlayableEntityKind.Hawk;double radius=PlayableUnitRules.Radius(p,unit.Kind);
            foreach(var tower in towers)
            {
                if(air&&tower.Kind==PlayableBuildingKind.GaussTower)continue;
                bool Exposed(NavPoint q)=>AiPositionDevelopmentPlanner.Covers(o,p,tower.Position,tower.Kind,q,air,tower.Id,unit.Id);
                var at=unit.Position;bool escaping=Exposed(at);double previous=AiPositionDevelopmentPlanner.Distance(tower.Position,at);
                foreach(var end in points)
                {
                    double length=AiPositionDevelopmentPlanner.Distance(at,end);
                    // Physical-envelope resolution, not a gameplay/tuning distance.
                    // Include the exact closest point so a tangent crossing is not lost.
                    double dx=end.X-at.X,dz=end.Z-at.Z;
                    double closest=length==0?0:Math.Max(0,Math.Min(1,((tower.Position.X-at.X)*dx+(tower.Position.Z-at.Z)*dz)/(length*length)));
                    var nearest=new NavPoint(at.X+dx*closest,at.Z+dz*closest);
                    if(AiPositionDevelopmentPlanner.Distance(tower.Position,nearest)>AiPositionDevelopmentPlanner.Range(p,tower.Kind))
                    {escaping=false;previous=AiPositionDevelopmentPlanner.Distance(tower.Position,end);at=end;continue;}
                    int steps=Math.Max(1,(int)Math.Ceiling(length/radius));
                    var samples=Enumerable.Range(1,steps).Select(i=>i/(double)steps).Append(closest).Distinct().OrderBy(t=>t);
                    foreach(double t in samples)
                    {
                        var q=new NavPoint(at.X+dx*t,at.Z+dz*t);double distance=AiPositionDevelopmentPlanner.Distance(tower.Position,q);
                        if(Exposed(q)){if(!escaping||distance<previous)return false;}else escaping=false;
                        previous=distance;
                    }
                    at=end;
                }
                if(Exposed(points.Last()))return false;
            }
            return true;
        }
    }
}
