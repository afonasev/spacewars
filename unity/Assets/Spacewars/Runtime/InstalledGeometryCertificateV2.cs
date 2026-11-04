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
        internal int RemainingIndex=-1,RemainingLocalIndex;
        private InstalledGeometryMemberV2(NavPoint position,bool owned):base(Layout){Position=position;if(owned)Retain();}
        internal override bool IsImmutableCaptureRoot=>Request!=null&&Corridor!=null&&Global!=null&&Local!=null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.InstalledExecution,value:RemainingIndex<0?NavPhysicalValueKind.InstalledGeometryMemberV2:NavPhysicalValueKind.WreckGeometryMemberV1);
        internal static InstalledGeometryMemberV2 Create(NavigationHostRequest request,NavigationHostCorridor corridor,NavRetainedArray<NavPoint> global,NavRetainedArray<NavPoint> local,NavPoint position,INavStorageReservation r,bool owned=true)
        {
            if(request==null||corridor==null||global==null||local==null||!ReferenceEquals(corridor.RequestOwner,request)||!Finite(position.X)||!Finite(position.Z))throw new ArgumentException("Invalid geometry certificate member.");
            NavStorageAdmission.RequireOwned(r,request);NavStorageAdmission.RequireOwned(r,corridor);NavStorageAdmission.RequireOwned(r,global);NavStorageAdmission.RequireOwned(r,local);
            using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);var v=a.Register(new InstalledGeometryMemberV2(position,owned));try{v.Request=NavPhysicalMacro.Acquire(v,request);v.Corridor=NavPhysicalMacro.Acquire(v,corridor);v.Global=NavPhysicalMacro.Acquire(v,global);v.Local=NavPhysicalMacro.Acquire(v,local);return v;}catch{if(owned)v.Release();throw;}}
        }
        internal bool RemainingDisjoint(NavObstacle o)
        {
            if(o.PhysicalLayer>=0){
                var surfaces=Corridor.ProviderOwner.Provider.Surfaces;
                if(o.PhysicalLayer>=surfaces.Count)return false;
                string layer=surfaces[o.PhysicalLayer].Id;bool relevant=false;
                foreach(var leg in Corridor.TypedRoute.Legs)if(leg.From.SurfaceId==layer||leg.To.SurfaceId==layer)relevant=true;
                if(!relevant&&Request.Request.StartLocation?.SurfaceId!=layer&&Request.Request.GoalLocation?.SurfaceId!=layer)return true;
            }
            var from=Position;double radius=Corridor.Corridor.Radius;
            bool Segment(NavPoint to){
                double x=o.CircleCenter.X,z=o.CircleCenter.Z,dx=to.X-from.X,dz=to.Z-from.Z;
                double length=dx*dx+dz*dz,t=length==0?0:Math.Max(0,Math.Min(1,((x-from.X)*dx+(z-from.Z)*dz)/length));
                double ex=from.X+t*dx-x,ez=from.Z+t*dz-z;
                double clearance=radius+o.CircleRadius;
                bool safe=ex*ex+ez*ez>clearance*clearance;from=to;return safe;
            }
            if(o.CircleRadius<=0)return Disjoint(o);
            for(int i=RemainingLocalIndex;i<Local.Count;i++)if(!Segment(Local[i]))return false;
            for(int i=RemainingIndex;i<Global.Count;i++)if(!Segment(Global[i]))return false;
            return Segment(Corridor.Corridor.Endpoint.Position);
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
        public void WritePhysicalBody(NavPhysicalBodyWriter w){WorldWire.Write(w.Output,Position);NavPhysicalMacro.WriteEdge(w,this,Request);NavPhysicalMacro.WriteEdge(w,this,Corridor);NavPhysicalMacro.WriteEdge(w,this,Global);NavPhysicalMacro.WriteEdge(w,this,Local);if(RemainingIndex>=0){w.Output.Write(RemainingIndex);w.Output.Write(RemainingLocalIndex);}}
        internal static InstalledGeometryMemberV2 Read(NavPhysicalBodyReader r,bool remaining=false){var p=WorldWire.ReadPoint(r.Input);var request=NavPhysicalMacro.ReadEdge<NavigationHostRequest>(r);var corridor=NavPhysicalMacro.ReadEdge<NavigationHostCorridor>(r);var global=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavPoint>>(r);var local=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavPoint>>(r);var result=Create(request,corridor,global,local,p,r.Reservation,false);if(remaining){result.RemainingIndex=r.Input.ReadInt32();result.RemainingLocalIndex=r.Input.ReadInt32();if(result.RemainingIndex<0||result.RemainingIndex>global.Count||result.RemainingLocalIndex<0||result.RemainingLocalIndex>local.Count)throw new ArgumentException("Invalid remaining route proof.");}return result;}
        protected override void ReleaseEdges(){Request?.Release();Corridor?.Release();Global?.Release();Local?.Release();Request=null;Corridor=null;Global=Local=null;}
    }
    internal sealed class InstalledGeometryCertificateV2 : NavOwnedStorage,INavPhysicalBody
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(InstalledGeometryCertificateV2));
        internal bool WreckChanges;
        internal NavigationAuthorityGroup Group;internal NavGeometry Before,Current;private NavGeometry.OwnedBuffers beforeOwner,currentOwner;internal NavRetainedArray<NavOwnedStorage> Members;
        private InstalledGeometryCertificateV2(bool owned):base(Layout){if(owned)Retain();}
        internal override bool IsImmutableCaptureRoot=>Group!=null&&Members!=null&&Before!=null&&Current!=null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.InstalledExecution,value:WreckChanges?NavPhysicalValueKind.WreckGeometryCertificateV1:NavPhysicalValueKind.InstalledGeometryCertificateV2);
        internal static bool AddOnly(NavGeometry before,NavGeometry current)
        {
            if(before==null||current==null||before.HalfExtent!=current.HalfExtent||current.Revision<=before.Revision||current.Obstacles.Count<=before.Obstacles.Count)return false;
            // Exact ordered subsequence, plus uniqueness: no unnoticed replace/remove.
            int at=0;for(int n=0;n<current.Obstacles.Count;n++){var o=current.Obstacles[n];for(int j=0;j<n;j++)if(o.GeometryEquals(current.Obstacles[j]))return false;
                if(at<before.Obstacles.Count&&o.GeometryEquals(before.Obstacles[at]))at++;else if(o.Polygon!=null||o.CircleRadius!=0||o.Bottom!=0||!double.IsNaN(o.Top))return false;
            }return at==before.Obstacles.Count;
        }
        // An exact common prefix is already proved present in both immutable
        // inputs. Skip its quadratic set search; suffixes still use full exact
        // membership so reordered/duplicate obstacles keep the same semantics.
        private static int CommonPrefix(NavGeometry before,NavGeometry current)
        {int at=0;while(at<before.Obstacles.Count&&at<current.Obstacles.Count&&before.Obstacles[at].GeometryEquals(current.Obstacles[at]))at++;return at;}
        // Only transient circular footprints may differ; static topology is identical.
        internal static bool WreckDelta(NavGeometry before,NavGeometry current)
        {
            if(before==null||current==null||before.HalfExtent!=current.HalfExtent||current.Revision<=before.Revision)return false;
            bool Present(NavGeometry g,NavObstacle o){foreach(var b in g.Obstacles)if(b.GeometryEquals(o))return true;return false;}
            int prefix=CommonPrefix(before,current);
            for(int i=prefix;i<before.Obstacles.Count;i++){var o=before.Obstacles[i];if(o.CircleRadius<=0&&!Present(current,o))return false;}
            for(int i=prefix;i<current.Obstacles.Count;i++){var o=current.Obstacles[i];if(o.CircleRadius<=0&&!Present(before,o))return false;}
            return true;
        }
        internal static bool SafeWreck(NavigationAuthorityGroup group,NavGeometry before,NavGeometry current,NavRetainedArray<NavOwnedStorage> members)
        {
            if(group==null||group.Data.MarchVersion!=2||members==null||members.Count<1||!WreckDelta(before,current))return false;
            for(int i=0;i<members.Count;i++){
                if(!(members[i] is InstalledGeometryMemberV2 row)||row.RemainingIndex<0||row.RemainingIndex>row.Global.Count||row.RemainingLocalIndex<0||row.RemainingLocalIndex>row.Local.Count)return false;
                var d=row.Corridor.Corridor;var q=row.Request.Request;NavigationAuthorityMember member=null;
                foreach(var value in group.Members)if(((NavigationAuthorityMember)value).Data.Entity==d.Entity)member=(NavigationAuthorityMember)value;
                if(member==null)return false;var m=member.Data;
                if(d.Group!=group.Data.GroupId||d.RootRevision!=group.Data.OrderRevision||d.CommandSequence!=group.Data.CommandSequence||d.IssuedTick!=group.Data.IssuedTick||d.Incarnation!=m.Incarnation||d.Mobility!=m.MobilityRevision||d.ActivationRevision!=m.OrderRevision||q.ProfileRevision!=group.Data.MarchProfileRevision||d.HoldBinding!=q.HeldIdentity||!m.HasGoal||!m.Goal.Equals(d.Endpoint.Position)||!m.HasLocation||!member.GoalLocation.Value.Equals(d.Endpoint))return false;
                if(!WreckDelta(q.BaseGeometry,current))return false;
                // The proof covers all additions since original installation, including
                // additions already carried across earlier geometry publications.
                for(int n=CommonPrefix(q.BaseGeometry,current);n<current.Obstacles.Count;n++){var o=current.Obstacles[n];bool old=false;foreach(var b in q.BaseGeometry.Obstacles)if(b.GeometryEquals(o)){old=true;break;}if(!old&&!row.RemainingDisjoint(o))return false;}
                for(int j=0;j<i;j++)if(((InstalledGeometryMemberV2)members[j]).Request.Request.Entity==q.Entity)return false;
            }return true;
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
        internal static InstalledGeometryCertificateV2 Create(NavigationAuthorityGroup group,NavGeometry before,NavGeometry current,NavRetainedArray<NavOwnedStorage> members,INavStorageReservation r,bool owned=true,bool wreckChanges=false)
        {
            var value=TryCreate(group,before,current,members,r,owned,wreckChanges);
            if(value==null)throw new ArgumentException("Insufficient installed geometry certificate.");
            return value;
        }
        // The proof is pure over pinned immutable inputs. Test and allocate in one
        // entry point so ordinary preparation does not compute it twice. Capture
        // and restoration still validate the complete certificate independently.
        internal static InstalledGeometryCertificateV2 TryCreate(NavigationAuthorityGroup group,NavGeometry before,NavGeometry current,NavRetainedArray<NavOwnedStorage> members,INavStorageReservation r,bool owned=true,bool wreckChanges=false)
        {
            if(!(wreckChanges?SafeWreck(group,before,current,members):Safe(group,before,current,members)))return null;
            NavStorageAdmission.RequireOwned(r,group);NavStorageAdmission.RequireOwned(r,before.StorageRoot);NavStorageAdmission.RequireOwned(r,current.StorageRoot);NavStorageAdmission.RequireOwned(r,members);
            using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);var v=a.Register(new InstalledGeometryCertificateV2(owned));try{v.WreckChanges=wreckChanges;v.Group=NavPhysicalMacro.Acquire(v,group);v.beforeOwner=NavPhysicalMacro.Acquire(v,(NavGeometry.OwnedBuffers)before.StorageRoot);v.currentOwner=NavPhysicalMacro.Acquire(v,(NavGeometry.OwnedBuffers)current.StorageRoot);v.Before=(NavGeometry)v.beforeOwner.Facade;v.Current=(NavGeometry)v.currentOwner.Facade;v.Members=NavPhysicalMacro.Acquire(v,members);return v;}catch{if(owned)v.Release();throw;}}
        }
        internal bool Binds(NavigationHostRequest request,NavigationHostCorridor corridor,NavRetainedArray<NavPoint> route)
        {foreach(var value in Members){var row=(InstalledGeometryMemberV2)value;if(ReferenceEquals(row.Request,request)&&ReferenceEquals(row.Corridor,corridor)&&ReferenceEquals(row.Global,route))return true;}return false;}
        public void WritePhysicalBody(NavPhysicalBodyWriter w){if(!(WreckChanges?SafeWreck(Group,Before,Current,Members):Safe(Group,Before,Current,Members)))throw new ArgumentException("Invalid geometry certificate.");NavPhysicalMacro.WriteEdge(w,this,Group);NavPhysicalMacro.WriteEdge(w,this,beforeOwner);NavPhysicalMacro.WriteEdge(w,this,currentOwner);NavPhysicalMacro.WriteEdge(w,this,Members);}
        internal static InstalledGeometryCertificateV2 Read(NavPhysicalBodyReader r,bool wreckChanges=false){var group=NavPhysicalMacro.ReadEdge<NavigationAuthorityGroup>(r);var before=NavPhysicalMacro.ReadEdge<NavGeometry.OwnedBuffers>(r);var current=NavPhysicalMacro.ReadEdge<NavGeometry.OwnedBuffers>(r);var members=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavOwnedStorage>>(r);if(!(before.Facade is NavGeometry b)||!(current.Facade is NavGeometry c))throw new ArgumentException("Invalid geometry certificate domains.");return Create(group,b,c,members,r.Reservation,false,wreckChanges);}
        protected override void ReleaseEdges(){Group?.Release();beforeOwner?.Release();currentOwner?.Release();Members?.Release();Group=null;Before=Current=null;beforeOwner=currentOwner=null;Members=null;}
    }
}
