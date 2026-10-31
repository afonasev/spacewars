namespace Spacewars.Simulation
{
    // Authored pivots and centered exit of the maintained Panцирь model, in meters.
    public static class GaussTowerGeometry
    {
        public static double Recoil(PlayableProfile p,double remaining)
        {
            if(remaining<=0)return 0;
            double age=p.GaussBeamSeconds-remaining,t=System.Math.Max(0,age/p.GaussRecoilSeconds),peak=p.GaussRecoilPeakFraction;
            if(t>=1)return 0;
            double x=t<peak?t/peak:(1-t)/(1-peak);
            return p.GaussRecoilMeters*x*x*(3-2*x);
        }
        public const double PitchHeight=1.86,BarrelOffset=.98;
        public static BallisticPoint Muzzle(NavPoint at,double ground,double scale,double heading,double pitch)
        {
            double f=BarrelOffset*System.Math.Cos(pitch);
            return new BallisticPoint(at.X+System.Math.Cos(heading)*f*scale,ground+(PitchHeight+BarrelOffset*System.Math.Sin(pitch))*scale,at.Z+System.Math.Sin(heading)*f*scale);
        }
    }
    public sealed class GaussShotState
    {
        public BallisticPoint Start,End;
        public double VisibleSeconds;
        // Frozen contact list supports geometry-publication retry without double damage.
        public int[] Targets=System.Array.Empty<int>();
        public double[] Damage=System.Array.Empty<double>();
        public int Next;
    }
}
