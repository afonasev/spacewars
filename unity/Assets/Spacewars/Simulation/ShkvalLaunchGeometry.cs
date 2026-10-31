using System;
namespace Spacewars.Simulation
{
    // Authored Unity-local pivots/sockets from design/shkval-monolith/build_model.py.
    // No renderer transforms or live/hidden observation queries in simulation.
    public static class ShkvalLaunchGeometry
    {
        public const double NeutralPitch=32*Math.PI/180;
        public const double RocketScale=.6, LoadedProtrusion=.04;
        public readonly struct Frame
        {
            public readonly BallisticPoint Mouth,Center,Direction;
            public Frame(BallisticPoint mouth,BallisticPoint center,BallisticPoint direction){Mouth=mouth;Center=center;Direction=direction;}
        }
        private static BallisticPoint Add(BallisticPoint a,BallisticPoint b)=>new BallisticPoint(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        private static BallisticPoint Mul(BallisticPoint a,double s)=>new BallisticPoint(a.X*s,a.Y*s,a.Z*s);
        private static BallisticPoint Cross(BallisticPoint a,BallisticPoint b)=>new BallisticPoint(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
        private static BallisticPoint Yaw(BallisticPoint v,double heading)=>new BallisticPoint(v.X*Math.Sin(heading)+v.Z*Math.Cos(heading),v.Y,-v.X*Math.Cos(heading)+v.Z*Math.Sin(heading));
        private static BallisticPoint Pitch(BallisticPoint v,double pitch)=>new BallisticPoint(v.X,v.Y*Math.Cos(pitch)+v.Z*Math.Sin(pitch),-v.Y*Math.Sin(pitch)+v.Z*Math.Cos(pitch));
        private static BallisticPoint Tilt(BallisticPoint v,NavPoint gradient,bool inverse=false)
        {
            double length=Math.Sqrt(1+gradient.X*gradient.X+gradient.Z*gradient.Z);
            var axis=new BallisticPoint(-gradient.Z/length,0,gradient.X/length);if(inverse)axis=Mul(axis,-1);
            return Add(Add(v,Cross(axis,v)),Mul(Cross(axis,Cross(axis,v)),1/(1+1/length)));
        }
        public static BallisticPoint LauncherPosition(double pitch)
        {
            // Rear bearing authored in build_model.py; neutral artwork is unchanged.
            var hinge=new BallisticPoint(0,.2,-.9);
            return Add(new BallisticPoint(0,.22,0),Add(Pitch(hinge,NeutralPitch),Mul(Pitch(hinge,pitch),-1)));
        }
        public static Frame At(BallisticPoint ground,double hull,double turret,double pitch,bool upgraded,int barrel,double modelScale,double projectileLength,NavPoint gradient=default)
        {
            if(barrel<0||barrel>3)throw new ArgumentOutOfRangeException(nameof(barrel));
            double x=(barrel%2==0?1:-1)*(upgraded?.46:.245),y=barrel<2?(upgraded?.13:.12):(upgraded?.56:.59);
            var local=Add(LauncherPosition(pitch),Pitch(new BallisticPoint(x,y,.94),pitch));
            var mouth=Add(ground,Tilt(Mul(Add(Yaw(new BallisticPoint(0,.8675,-.65),hull),Yaw(local,turret)),modelScale),gradient));
            var direction=Tilt(Yaw(Pitch(new BallisticPoint(0,0,1),pitch),turret),gradient);
            double inset=projectileLength*.64*RocketScale-LoadedProtrusion;
            return new Frame(mouth,Add(mouth,Mul(direction,-inset)),direction);
        }
        public static void Aim(BallisticPoint ground,double hull,bool upgraded,int barrel,double modelScale,double length,NavPoint gradient,BallisticPoint end,double arc,out double yaw,out double pitch)
        {
            yaw=Math.Atan2(end.Z-ground.Z,end.X-ground.X);pitch=NeutralPitch;
            // A fixed point solve accounts for socket offset, elevation and sloped ground.
            // Numerical convergence bound, not a gameplay parameter.
            for(int i=0;i<32;i++){
                var start=At(ground,hull,yaw,pitch,upgraded,barrel,modelScale,length,gradient).Center;
                var tangent=Tilt(new BallisticPoint(end.X-start.X,end.Y-start.Y+4*arc,end.Z-start.Z),gradient,true);
                double nextYaw=Math.Atan2(tangent.Z,tangent.X),nextPitch=Math.Atan2(tangent.Y,Math.Sqrt(tangent.X*tangent.X+tangent.Z*tangent.Z));
                bool converged=Math.Abs(nextYaw-yaw)<1e-13&&Math.Abs(nextPitch-pitch)<1e-13;
                yaw=nextYaw;pitch=nextPitch;if(converged)break;
            }
        }
    }
}
