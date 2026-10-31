using System;
namespace Spacewars.Simulation
{
    // Exact mesh queries transform into each articulated root's local frame; meshes/BVHs are shared once.
    internal readonly struct SentryCombatPose
    {
        static readonly CombatMesh RepairBody=CombatMesh.Decode(RepairTowerCombatData.modelRoot),RepairYaw=CombatMesh.Decode(RepairTowerCombatData.turretYaw),RepairPitch=CombatMesh.Decode(RepairTowerCombatData.weaponPitch);
        static readonly CombatMesh Body=CombatMesh.Decode(SentryCombatData.modelRoot),Yaw=CombatMesh.Decode(SentryCombatData.turretYaw),Pitch=CombatMesh.Decode(SentryCombatData.weaponPitch);
        static readonly CombatMesh GaussBody=CombatMesh.Decode(GaussCombatData.modelRoot),GaussYaw=CombatMesh.Decode(GaussCombatData.turretYaw),GaussPitch=CombatMesh.Decode(GaussCombatData.weaponPitch);
        readonly double recoil;readonly bool gauss;readonly bool repair;readonly double yawHeight,pitchHeight,pitchBack;
        readonly V at;readonly double scale,yaw,pitch,height;
        internal SentryCombatPose(NavPoint position,double ground,double scale,double heading,double elevation,double progress,bool repair=false,bool gauss=false,double recoil=0)
        {this.recoil=recoil;this.gauss=gauss;this.repair=repair;yawHeight=SentryTurretGeometry.YawHeight;pitchHeight=gauss?GaussTowerGeometry.PitchHeight:repair?RepairTowerGeometry.PitchHeight:SentryTurretGeometry.PitchHeight;pitchBack=repair?RepairTowerGeometry.PitchBack:0;at=new V(position.X,ground,position.Z);this.scale=scale;yaw=Math.PI/2-heading;pitch=elevation;height=BuildingCombatGeometry.ConstructionHeight(progress);}
        internal double Reach=>(Math.Max(Mesh(0).Reach,Math.Max(Mesh(1).Reach,Mesh(2).Reach))+pitchHeight+Math.Abs(pitchBack)+recoil)*scale;
        static V TurnYaw(V v,double a){double c=Math.Cos(a),s=Math.Sin(a);return new V(c*v.X+s*v.Z,v.Y,-s*v.X+c*v.Z);}
        static V TurnPitch(V v,double a){double c=Math.Cos(a),s=Math.Sin(a);return new V(v.X,c*v.Y+s*v.Z,-s*v.Y+c*v.Z);}
        V Local(BallisticPoint point,int part)
        {
            var p=(new V(point.X,point.Y,point.Z)-at)/scale;p=new V(p.X,p.Y/height,p.Z);
            // The fixed footing follows the approved south-facing base orientation.
            if(part==0)return TurnYaw(p,-Math.PI);
            p-=new V(0,yawHeight,0);p=TurnYaw(p,-yaw);
            if(part==2){p-=new V(0,pitchHeight-yawHeight,pitchBack);p=TurnPitch(p,-pitch)+new V(0,0,recoil);}
            return p;
        }
        BallisticPoint World(V p,int part)
        {
            if(part==0)p=TurnYaw(p,Math.PI);
            else{if(part==2)p=TurnPitch(p-new V(0,0,recoil),pitch)+new V(0,pitchHeight-yawHeight,pitchBack);p=TurnYaw(p,yaw)+new V(0,yawHeight,0);}
            p=new V(p.X,p.Y*height,p.Z)*scale+at;return new BallisticPoint(p.X,p.Y,p.Z);
        }
        CombatMesh Mesh(int part)=>gauss?(part==0?GaussBody:part==1?GaussYaw:GaussPitch):repair?(part==0?RepairBody:part==1?RepairYaw:RepairPitch):(part==0?Body:part==1?Yaw:Pitch);
        internal bool Segment(BallisticPoint a,BallisticPoint b,double radius,out double fraction,out BallisticPoint contact)
        {
            fraction=double.PositiveInfinity;
            for(int i=0;i<3;i++)
            {
                if(height==1||radius==0)fraction=Math.Min(fraction,Mesh(i).Segment(Local(a,i),Local(b,i),radius/scale,0,0,1));
                else foreach(var part in Mesh(i).Parts)foreach(var triangle in part.Triangles)
                    fraction=Math.Min(fraction,WorldTriangle(triangle,i).Sweep(new V(a.X,a.Y,a.Z),new V(b.X,b.Y,b.Z),radius));
            }
            contact=double.IsPositiveInfinity(fraction)?b:new BallisticPoint(a.X+(b.X-a.X)*fraction,a.Y+(b.Y-a.Y)*fraction,a.Z+(b.Z-a.Z)*fraction);return !double.IsPositiveInfinity(fraction);
        }
        CombatTriangle WorldTriangle(CombatTriangle t,int part)
        {
            var a=World(t.A,part);var b=World(t.B,part);var c=World(t.C,part);return new CombatTriangle(new V(a.X,a.Y,a.Z),new V(b.X,b.Y,b.Z),new V(c.X,c.Y,c.Z));
        }
        internal bool Overlaps(BallisticPoint p,double radius)
        {
            var at=new V(p.X,p.Y,p.Z);
            for(int i=0;i<3;i++)
            {
                if(height==1||radius==0){if(Mesh(i).Overlaps(Local(p,i),radius/scale,0,0,1))return true;}
                else {if(Mesh(i).Overlaps(Local(p,i),0,0,0,1))return true;foreach(var part in Mesh(i).Parts)foreach(var triangle in part.Triangles)if((WorldTriangle(triangle,i).Closest(at)-at).Length2<=radius*radius)return true;}
            }
            return false;
        }
        internal BallisticPoint Closest(BallisticPoint p)
        {
            double best=double.PositiveInfinity;BallisticPoint result=p;
            if(height!=1)
            {
                var at=new V(p.X,p.Y,p.Z);
                for(int i=0;i<3;i++){if(Mesh(i).Overlaps(Local(p,i),0,0,0,1))return p;foreach(var part in Mesh(i).Parts)foreach(var triangle in part.Triangles){var q=WorldTriangle(triangle,i).Closest(at);double distance=(q-at).Length2;if(distance<best){best=distance;result=new BallisticPoint(q.X,q.Y,q.Z);}}}return result;
            }
            for(int i=0;i<3;i++){var q=World(Mesh(i).Closest(Local(p,i),0,0,1),i);double d=(q.X-p.X)*(q.X-p.X)+(q.Y-p.Y)*(q.Y-p.Y)+(q.Z-p.Z)*(q.Z-p.Z);if(d<best){best=d;result=q;}}
            return result;
        }
    }
}
