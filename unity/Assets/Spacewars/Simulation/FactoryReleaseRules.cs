using System;
namespace Spacewars.Simulation
{
    // Launch phases are protocol states, not balance parameters. The staged vehicle
    // remains a paid order until Released, never a target/weapon/navigation actor.
    public static class FactoryReleaseRules
    {
        public const int Closed=0,Opening=1,Driving=2,Closing=3;
        public static double ModelScale(PlayableProfile p)=>p.FactoryModelHeightMeters/2.06*p.FactoryModelScale;
        // Accepted F2 source geometry: front door plane and floor in authored metres.
        public static double GatePlane(PlayableProfile p)=>1.345*ModelScale(p);
        public static double Floor(PlayableProfile p)=>.125125*ModelScale(p);
        public static double ModelRadius(PlayableProfile p,PlayableEntityKind kind)=>kind==PlayableEntityKind.Tank?p.TankModelRadius*p.TankModelScale:kind==PlayableEntityKind.Explorer?p.ExplorerModelRadius*p.ExplorerModelScale:p.ShkvalModelRadius*p.ShkvalModelScale;
        public static double ExitDistance(PlayableProfile p,PlayableEntityKind kind)=>Math.Max(p.FactoryExitDistance,GatePlane(p)+Math.Max(ModelRadius(p,kind),PlayableUnitRules.Radius(p,kind)));
        public static NavPoint Exit(PlayableProfile p,NavPoint origin,double heading,PlayableEntityKind kind)
        {double d=ExitDistance(p,kind);return new NavPoint(origin.X+Math.Cos(heading)*d,origin.Z+Math.Sin(heading)*d);}
        public static double Duration(PlayableProfile p,PlayableEntityKind kind)=>ExitDistance(p,kind)/p.FactoryRolloutSpeed;
    }
}
