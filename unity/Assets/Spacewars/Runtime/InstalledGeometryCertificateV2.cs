using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // A fresh, paid sufficient proof. Historical route proofs remain unchanged.
    // Full path bounds deliberately include consumed prefixes (conservative).
    internal sealed class InstalledGeometryMemberV2 : NavOwnedStorage,INavPhysicalBody
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(InstalledGeometryMemberV2));
        internal NavigationHostRequest Request;internal NavigationHostCorridor Corridor;internal NavRetainedArray<NavPoint> Global,Local;
        internal readonly NavPoint Position;
        private InstalledGeometryMemberV2(NavPoint position,bool owned):base(Layout){Position=position;if(owned)Retain();}
        internal override bool IsImmutableCaptureRoot=>Request!=null&&Corridor!=null&&Global!=null&&Local!=null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.InstalledExecution,value:NavPhysicalValueKind.InstalledGeometryMemberV2);
        internal static InstalledGeometryMemberV2 Create(NavigationHostRequest request,NavigationHostCorridor corridor,NavRetainedArray<NavPoint> global,NavRetainedArray<NavPoint> local,NavPoint position,INavStorageReservation r,bool owned=true)
        {
            if(request==null||corridor==null||global==null||local==null||!ReferenceEquals(corridor.RequestOwner,request)||!Finite(position.X)||!Finite(position.Z))throw new ArgumentException("Invalid geometry certificate member.");
            NavStorageAdmission.RequireOwned(r,request);NavStorageAdmission.RequireOwned(r,corridor);NavStorageAdmission.RequireOwned(r,global);NavStorageAdmission.RequireOwned(r,local);
            using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);var v=a.Register(new InstalledGeometryMemberV2(position,owned));try{v.Request=NavPhysicalMacro.Acquire(v,request);v.Corridor=NavPhysicalMacro.Acquire(v,corridor);v.Global=NavPhysicalMacro.Acquire(v,global);v.Local=NavPhysicalMacro.Acquire(v,local);return v;}catch{if(owned)v.Release();throw;}}
        }
        internal bool Disjoint(NavObstacle o)
        {
            double minX=Position.X,maxX=Position.X,minZ=Position.Z,maxZ=Position.Z;
            void Include(NavPoint p){minX=Math.Min(minX,p.X);maxX=Math.Max(maxX,p.X);minZ=Math.Min(minZ,p.Z);maxZ=Math.Max(maxZ,p.Z);}
            Include(Corridor.Corridor.Origin.Position);Include(Corridor.Corridor.Endpoint.Position);
            foreach(var leg in Corridor.Corridor.Legs){Include(leg.From.Position);Include(leg.To.Position);}foreach(var p in Global)Include(p);foreach(var p in Local)Include(p);
            double radius=Corridor.Corridor.Radius;minX=Outward(minX-radius,false);minZ=Outward(minZ-radius,false);maxX=Outward(maxX+radius,true);maxZ=Outward(maxZ+radius,true);
            return Finite(minX)&&Finite(minZ)&&Finite(maxX)&&Finite(maxZ)&&(maxX<o.MinX||minX>o.MaxX||maxZ<o.MinZ||minZ>o.MaxZ);
        }
        private static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
        private static double Outward(double x,bool up){if(x==0)return up?double.Epsilon:-double.Epsilon;long b=BitConverter.DoubleToInt64Bits(x);return BitConverter.Int64BitsToDouble(b+((x>0)==up?1:-1));}
        public void WritePhysicalBody(NavPhysicalBodyWriter w){WorldWire.Write(w.Output,Position);NavPhysicalMacro.WriteEdge(w,this,Request);NavPhysicalMacro.WriteEdge(w,this,Corridor);NavPhysicalMacro.WriteEdge(w,this,Global);NavPhysicalMacro.WriteEdge(w,this,Local);}
        internal static InstalledGeometryMemberV2 Read(NavPhysicalBodyReader r){var p=WorldWire.ReadPoint(r.Input);var request=NavPhysicalMacro.ReadEdge<NavigationHostRequest>(r);var corridor=NavPhysicalMacro.ReadEdge<NavigationHostCorridor>(r);var global=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavPoint>>(r);var local=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavPoint>>(r);return Create(request,corridor,global,local,p,r.Reservation,false);}
        protected override void ReleaseEdges(){Request?.Release();Corridor?.Release();Global?.Release();Local?.Release();Request=null;Corridor=null;Global=Local=null;}
    }
    internal sealed class InstalledGeometryCertificateV2 : NavOwnedStorage,INavPhysicalBody
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(InstalledGeometryCertificateV2));
        internal NavigationAuthorityGroup Group;internal NavGeometry Before,Current;private NavGeometry.OwnedBuffers beforeOwner,currentOwner;internal NavRetainedArray<NavOwnedStorage> Members;
        private InstalledGeometryCertificateV2(bool owned):base(Layout){if(owned)Retain();}
        internal override bool IsImmutableCaptureRoot=>Group!=null&&Members!=null&&Before!=null&&Current!=null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.InstalledExecution,value:NavPhysicalValueKind.InstalledGeometryCertificateV2);
        internal static bool AddOnly(NavGeometry before,NavGeometry current)
        {
            if(before==null||current==null||before.HalfExtent!=current.HalfExtent||current.Revision<=before.Revision||current.Obstacles.Count<=before.Obstacles.Count)return false;
            // Exact ordered subsequence, plus uniqueness: no unnoticed replace/remove.
            int at=0;for(int n=0;n<current.Obstacles.Count;n++){var o=current.Obstacles[n];for(int j=0;j<n;j++)if(o.GeometryEquals(current.Obstacles[j]))return false;
                if(at<before.Obstacles.Count&&o.GeometryEquals(before.Obstacles[at]))at++;else if(o.Polygon!=null||o.CircleRadius!=0||o.Bottom!=0||!double.IsNaN(o.Top))return false;
            }return at==before.Obstacles.Count;
        }
        internal static bool Safe(NavigationAuthorityGroup group,NavGeometry before,NavGeometry current,NavRetainedArray<NavOwnedStorage> members)
        {
            if(group==null||group.Data.MarchVersion!=2||members==null||members.Count!=group.Members.Count||!AddOnly(before,current))return false;
            for(int i=0;i<members.Count;i++){
                if(!(members[i] is InstalledGeometryMemberV2 row))return false;var d=row.Corridor.Corridor;var m=((NavigationAuthorityMember)group.Members[i]).Data;var q=row.Request.Request;
                if(d.Group!=group.Data.GroupId||d.RootRevision!=group.Data.OrderRevision||d.CommandSequence!=group.Data.CommandSequence||d.IssuedTick!=group.Data.IssuedTick||d.Entity!=m.Entity||d.Incarnation!=m.Incarnation||d.Mobility!=m.MobilityRevision||d.ActivationRevision!=m.OrderRevision||q.ProfileRevision!=group.Data.MarchProfileRevision||q.MemberIdentity==null||q.MemberIdentity.MembershipRevision!=group.Data.MembershipRevision||d.HoldBinding!=q.HeldIdentity||!m.HasGoal||!m.Goal.Equals(d.Endpoint.Position)||!m.HasLocation||!((NavigationAuthorityMember)group.Members[i]).GoalLocation.Value.Equals(d.Endpoint))return false;
                if(!ReferenceEquals(q.BaseGeometry,before)&&!AddOnly(q.BaseGeometry,current))return false;
                // Prove every footprint added since both original proof and last authority.
                for(int n=0;n<current.Obstacles.Count;n++){var o=current.Obstacles[n];bool old=false;foreach(var b in q.BaseGeometry.Obstacles)if(b.GeometryEquals(o)){old=true;break;}if(!old&&!row.Disjoint(o))return false;}
            }return true;
        }
        internal static InstalledGeometryCertificateV2 Create(NavigationAuthorityGroup group,NavGeometry before,NavGeometry current,NavRetainedArray<NavOwnedStorage> members,INavStorageReservation r,bool owned=true)
        {
            if(!Safe(group,before,current,members))throw new ArgumentException("Insufficient installed geometry certificate.");NavStorageAdmission.RequireOwned(r,group);NavStorageAdmission.RequireOwned(r,before.StorageRoot);NavStorageAdmission.RequireOwned(r,current.StorageRoot);NavStorageAdmission.RequireOwned(r,members);
            using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);var v=a.Register(new InstalledGeometryCertificateV2(owned));try{v.Group=NavPhysicalMacro.Acquire(v,group);v.beforeOwner=NavPhysicalMacro.Acquire(v,(NavGeometry.OwnedBuffers)before.StorageRoot);v.currentOwner=NavPhysicalMacro.Acquire(v,(NavGeometry.OwnedBuffers)current.StorageRoot);v.Before=(NavGeometry)v.beforeOwner.Facade;v.Current=(NavGeometry)v.currentOwner.Facade;v.Members=NavPhysicalMacro.Acquire(v,members);return v;}catch{if(owned)v.Release();throw;}}
        }
        internal bool Binds(NavigationHostRequest request,NavigationHostCorridor corridor,NavRetainedArray<NavPoint> route)
        {foreach(var value in Members){var row=(InstalledGeometryMemberV2)value;if(ReferenceEquals(row.Request,request)&&ReferenceEquals(row.Corridor,corridor)&&ReferenceEquals(row.Global,route))return true;}return false;}
        public void WritePhysicalBody(NavPhysicalBodyWriter w){if(!Safe(Group,Before,Current,Members))throw new ArgumentException("Invalid geometry certificate.");NavPhysicalMacro.WriteEdge(w,this,Group);NavPhysicalMacro.WriteEdge(w,this,beforeOwner);NavPhysicalMacro.WriteEdge(w,this,currentOwner);NavPhysicalMacro.WriteEdge(w,this,Members);}
        internal static InstalledGeometryCertificateV2 Read(NavPhysicalBodyReader r){var group=NavPhysicalMacro.ReadEdge<NavigationAuthorityGroup>(r);var before=NavPhysicalMacro.ReadEdge<NavGeometry.OwnedBuffers>(r);var current=NavPhysicalMacro.ReadEdge<NavGeometry.OwnedBuffers>(r);var members=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavOwnedStorage>>(r);if(!(before.Facade is NavGeometry b)||!(current.Facade is NavGeometry c))throw new ArgumentException("Invalid geometry certificate domains.");return Create(group,b,c,members,r.Reservation,false);}
        protected override void ReleaseEdges(){Group?.Release();beforeOwner?.Release();currentOwner?.Release();Members?.Release();Group=null;Before=Current=null;beforeOwner=currentOwner=null;Members=null;}
    }
}
