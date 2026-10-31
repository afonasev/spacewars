using System;
namespace Spacewars.Simulation
{
    public sealed class RepairTowerState
    {
        public int Target;
        public double Heading=Math.PI/2,Pitch;
        public bool Initialized;
    }
    public sealed class PlayableRepairTowerSnapshot
    {
        public PlayableRepairTowerSnapshot(int target,double heading,double pitch)
        {TargetId=target;Heading=heading;Pitch=pitch;}
        public int TargetId{get;} public double Heading{get;} public double Pitch{get;}
    }
}
