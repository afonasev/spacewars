using System;
namespace Spacewars.Simulation
{
    // Serializable authority state; renderer never chooses targets or advances this clock.
    public sealed class SentryTurretState
    {
        public int Target,Remaining,BurstSequence,ShotSequence,BurstSize;
        public double Heading=Math.PI/2,Pitch,Reload,Delay,BurstSpread;
        public bool Turning;
    }
    public sealed class PlayableTurretSnapshot
    {
        public PlayableTurretSnapshot(int target,double heading,double pitch,bool turning,int shots,int weaponLevel,BallisticPoint beamStart=default,BallisticPoint beamEnd=default,double beamSeconds=0,double charge=0,double recoil=0)
        {Charge=charge;Recoil=recoil;BeamStart=beamStart;BeamEnd=beamEnd;BeamSeconds=beamSeconds;TargetId=target;Heading=heading;Pitch=pitch;Turning=turning;ShotSequence=shots;WeaponLevel=weaponLevel;}
        public double Charge{get;} public double Recoil{get;}
        public BallisticPoint BeamStart{get;} public BallisticPoint BeamEnd{get;} public double BeamSeconds{get;}
        public int TargetId{get;} public double Heading{get;} public double Pitch{get;} public bool Turning{get;}
        public int ShotSequence{get;} public int WeaponLevel{get;}
    }
    public static class SentryTurretGeometry
    {
        // Authored rig coordinates from the approved GLB; changing mesh pivots requires regeneration and focused geometry QA.
        public const double YawHeight=1.45,PitchHeight=1.86,BarrelOffset=1.055,HalfSpacing=.125;
        public static BallisticPoint Muzzle(NavPoint at,double ground,double scale,double heading,double pitch,int side)
        {
            double forward=BarrelOffset*Math.Cos(pitch),vertical=BarrelOffset*Math.Sin(pitch),lateral=(side==0?-1:1)*HalfSpacing;
            return new BallisticPoint(at.X+(Math.Cos(heading)*forward+Math.Sin(heading)*lateral)*scale,
                ground+(PitchHeight+vertical)*scale,at.Z+(Math.Sin(heading)*forward-Math.Cos(heading)*lateral)*scale);
        }
    }
}
