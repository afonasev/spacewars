using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    public static class FactoryProductionAnchors
    {
        public static NavPoint[] Candidates(NavPoint factory,double footprintRadius,double unitRadius,IReadOnlyList<NavPoint> authored)
        {
            // Frozen bases.ts spawnCandidates algorithm: 16 global radial directions,
            // distance footprint + two typed radii. These are source protocol invariants,
            // not independently adjustable native balance parameters.
            const int radialCount=16;
            var result=new NavPoint[authored.Count+radialCount];
            for(int i=0;i<authored.Count;i++)result[i]=authored[i];
            double distance=footprintRadius+unitRadius*2;
            for(int i=0;i<radialCount;i++)
            {
                double angle=i*Math.PI/8;
                result[authored.Count+i]=new NavPoint(factory.X+Math.Cos(angle)*distance,factory.Z+Math.Sin(angle)*distance);
            }
            return result;
        }
    }
}
