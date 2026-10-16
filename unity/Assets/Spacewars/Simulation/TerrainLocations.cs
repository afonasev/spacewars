using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    // A physical boundary segment in the authored precedence partition. These
    // segments come from the same arrangement used by TryTraverse; transition
    // IDs alone cannot supply their geometry or clearance.
    public sealed partial class NavPartitionSeam : INavStorageFacade
    {
        internal NavPartitionSeam(string fromSurface, string toSurface, NavPoint from, NavPoint to, bool allowed)
        {
            FromSurface=fromSurface; ToSurface=toSurface; From=from; To=to; Allowed=allowed;
            Id=NavContract.Digest(w=>{w.Write("partition-seam-v1");w.Write(fromSurface);w.Write(toSurface);
                NavContract.Point(w,from);NavContract.Point(w,to);w.Write(allowed);});
        }
        public string Id{get;}
        public string FromSurface{get;} public string ToSurface{get;}
        public NavPoint From{get;} public NavPoint To{get;} public bool Allowed{get;}
    }
    // Provider compiler over the SAME authored support objects. The provider declares
    // ownership precedence and region connections; height never selects an identity.
    internal interface INavArrivalPointTerrain
    {
        TerrainLocations ArrivalLocations {get;}
    }
    internal sealed partial class TerrainLocations : INavStorageFacade
    {
        internal sealed class Seam : INavStorageFacade {internal NavDeclarationStorage Storage;NavOwnedStorage INavStorageFacade.StorageRoot=>Storage; internal string A,B; internal NavPoint From,To; internal bool Allowed; }
        private readonly NavGeometry geometry; private Tuple<NavPoint,NavPoint>[] edges; private MapSupport[] regions;
        private IReadOnlyDictionary<NavNameKey,int> byId; private IReadOnlyList<Seam> seams;
        private IReadOnlyList<Seam> exterior;
        internal int TracePortalBound=>checked((2*edges.Length+2)*Math.Max(0,regions.Length-1));
        internal long TraceScratchBound
        {
            get{
                long cuts=checked(2L*edges.Length+2),portals=checked(cuts*Math.Max(0,regions.Length-1)),legs=checked(portals+cuts+1);
                long Rounded(long count){long cap=4;while(cap<count)cap=checked(cap*2);return cap;}
                // Both Traverse and TryTrace build cuts; raw/deduplicated Lists and
                // hash slots can overlap. Typed leg objects coexist with rounded
                // result backing storage, ToArray and wrapper. Incident sorting,
                // visited/prior dictionaries, queue and path lists use E/R/S.
                long cutScratch=checked(2*(16*Rounded(cuts)+64*(3*cuts+4)+32*edges.Length+256));
                long trace=checked(256*legs+8*Rounded(legs)+8*legs+256);
                long incident=checked(32*Seams.Count+128*(3L*regions.Length+4)+64L*regions.Length+8*Rounded(regions.Length)+512);
                return checked(cutScratch+trace+incident+16*Rounded(portals)+16*portals+256);
            }
        }
        internal IReadOnlyList<MapSupport> Supports=>regions; internal NavGeometry Geometry=>geometry;
        internal IReadOnlyDictionary<NavNameKey,int> SurfaceIndex=>byId;
        internal IReadOnlyList<NavSurfaceTransition> Transitions{get;private set;}
        internal IReadOnlyList<NavPartitionSeam> Seams{get;private set;}
        private sealed class SegmentIndex
        {
            private readonly double half;private readonly int[][] buckets;internal long RetainedBytes{get;}internal int Entries{get;}
            private int Bucket(double coordinate)=>(int)Math.Max(0,Math.Min(15,Math.Floor((coordinate+half)/(2*half)*16)));
            internal SegmentIndex(double half,int count,Func<int,NavPoint> from,Func<int,NavPoint> to){
                this.half=half;var lists=new List<int>[256];
                for(int i=0;i<count;i++){var a=from(i);var b=to(i);for(int x=Bucket(Math.Min(a.X,b.X));x<=Bucket(Math.Max(a.X,b.X));x++)for(int z=Bucket(Math.Min(a.Z,b.Z));z<=Bucket(Math.Max(a.Z,b.Z));z++){int at=x*16+z;if(lists[at]==null)lists[at]=new List<int>();lists[at].Add(i);}}
                buckets=new int[256][];long bytes=32+8L*256;int entries=0;
                for(int i=0;i<256;i++)if(lists[i]!=null){buckets[i]=lists[i].ToArray();entries+=buckets[i].Length;bytes+=32+4L*buckets[i].Length;}
                RetainedBytes=bytes;Entries=entries;
            }
            internal Query Candidates(double minX,double minZ,double maxX,double maxZ)=>new Query(this,Bucket(minX),Bucket(minZ),Bucket(maxX),Bucket(maxZ));
            internal readonly struct Query{
                private readonly SegmentIndex index;private readonly int left,bottom,right,top;
                internal Query(SegmentIndex index,int left,int bottom,int right,int top){this.index=index;this.left=left;this.bottom=bottom;this.right=right;this.top=top;}
                public Enumerator GetEnumerator()=>new Enumerator(index,left,bottom,right,top);
            }
            internal struct Enumerator{
                private readonly SegmentIndex index;private readonly int bottom,right,top;private int x,z,offset;
                internal Enumerator(SegmentIndex index,int left,int bottom,int right,int top){this.index=index;this.bottom=bottom;this.right=right;this.top=top;x=left;z=bottom;offset=0;Current=0;}
                public int Current{get;private set;}
                public bool MoveNext(){while(x<=right){var ids=index.buckets[x*16+z];if(ids!=null&&offset<ids.Length){Current=ids[offset++];return true;}offset=0;if(++z>top){z=bottom;x++;}}return false;}
            }
        }
        private readonly SegmentIndex edgeIndex,boundaryIndex,seamIndex;
        private readonly Seam[] blockedBoundaries;
        private readonly List<int> cutCandidates;
        private const int RegionGridWidth=16;
        private readonly int[][] regionGrid;
        internal long RegionIndexRetainedBytes{get;}
        private int RegionBucket(double coordinate)=>(int)Math.Max(0,Math.Min(RegionGridWidth-1,Math.Floor((coordinate+geometry.HalfExtent)/(2*geometry.HalfExtent)*RegionGridWidth)));
        private readonly struct FootprintKey : IEquatable<FootprintKey> {
            private readonly double x,z,r;
            internal FootprintKey(NavPoint point,double radius){x=point.X;z=point.Z;r=radius;}
            public bool Equals(FootprintKey other)=>x.Equals(other.x)&&z.Equals(other.z)&&r.Equals(other.r);
            public override bool Equals(object other)=>other is FootprintKey key&&Equals(key);
            public override int GetHashCode()=>unchecked((x.GetHashCode()*397^z.GetHashCode())*397^r.GetHashCode());
        }
        private const int FootprintCacheLimit=4096;
        // Owner-confined derived values, including failed footprints; every exact input
        // remains bound to this immutable geometry/support/transition arrangement.
        private readonly Dictionary<FootprintKey,bool> footprints;
        private readonly Queue<FootprintKey> footprintOrder;
        internal long FootprintCacheRetainedBytes=>footprints==null?0:128L*FootprintCacheLimit;
        internal TerrainLocations(NavGeometry geometry,IEnumerable<MapSupport> authoredPrecedence,IEnumerable<NavSurfaceTransition> declared)
        {
            cutCandidates=new List<int>();footprints=new Dictionary<FootprintKey,bool>();footprintOrder=new Queue<FootprintKey>();
            this.geometry=geometry;var buildingSeams=new List<Seam>();var buildingExterior=new List<Seam>();seams=buildingSeams;exterior=buildingExterior;var supports=authoredPrecedence.ToArray();
            if(supports.Any(s=>string.IsNullOrWhiteSpace(s.SurfaceId)||s.SurfaceId==NavLocation.FlatSurface)||supports.Select(s=>s.SurfaceId).Distinct().Count()!=supports.Length)throw new ArgumentException("Provider must author unique surface keys.");
            regions=supports;
            var buckets=new List<int>[RegionGridWidth*RegionGridWidth];
            for(int i=0;i<regions.Length;i++){
                var bounds=regions[i].Bounds;
                for(int x=RegionBucket(bounds.MinX);x<=RegionBucket(bounds.MaxX);x++)for(int z=RegionBucket(bounds.MinZ);z<=RegionBucket(bounds.MaxZ);z++){
                    int index=x*RegionGridWidth+z;if(buckets[index]==null)buckets[index]=new List<int>();buckets[index].Add(i);
                }
            }
            regionGrid=new int[buckets.Length][];long regionBytes=32+8L*regionGrid.Length;
            for(int i=0;i<buckets.Length;i++)if(buckets[i]!=null){regionGrid[i]=buckets[i].ToArray();regionBytes+=32+4L*regionGrid[i].Length;}

            byId=regions.Select((s,i)=>new {s.SurfaceId,Index=i}).ToDictionary(r=>new NavNameKey(r.SurfaceId),r=>r.Index);
            var declarations=declared.ToArray();if(declarations.Select(t=>t.Id).Distinct().Count()!=declarations.Length||declarations.Any(t=>!byId.ContainsKey(new NavNameKey(t.From))||!byId.ContainsKey(new NavNameKey(t.To))))throw new ArgumentException("Invalid provider transitions.");
            edges=supports.SelectMany(s=>Edges(s.Bounds)).ToArray();
            edgeIndex=new SegmentIndex(geometry.HalfExtent,edges.Length,i=>edges[i].Item1,i=>edges[i].Item2);
            // Arrangement vertices split every ownership boundary, including overlaps.
            foreach(var edge in edges){
                var cuts=IndexedCuts(edge.Item1,edge.Item2);
                for(int i=1;i<cuts.Count;i++){
                    if(cuts[i]-cuts[i-1]<1e-10)continue;
                    var a=Lerp(edge.Item1,edge.Item2,cuts[i-1]);var b=Lerp(edge.Item1,edge.Item2,cuts[i]);var mid=Lerp(a,b,.5);
                    double dx=b.X-a.X,dz=b.Z-a.Z,len=Math.Sqrt(dx*dx+dz*dz),epsilon=Math.Min(1e-7,len/8);
                    var left=Owner(new NavPoint(mid.X-dz/len*epsilon,mid.Z+dx/len*epsilon));var right=Owner(new NavPoint(mid.X+dz/len*epsilon,mid.Z-dx/len*epsilon));
                    if(left==right)continue;
                    var seam=new Seam{From=a,To=b,A=left?.SurfaceId,B=right?.SurfaceId};
                    if(left==null||right==null){buildingExterior.Add(seam);continue;}
                    bool declaredPair=declarations.Any(t=>t.From==seam.A&&t.To==seam.B||t.From==seam.B&&t.To==seam.A);
                    seam.Allowed=declaredPair&&Math.Abs(left.HeightAt(a)-right.HeightAt(a))<=1e-7&&Math.Abs(left.HeightAt(b)-right.HeightAt(b))<=1e-7;
                    buildingSeams.Add(seam);
                }
            }
            Transitions=Array.AsReadOnly(declarations);
            Seams=Array.AsReadOnly(seams.Select(s=>new NavPartitionSeam(s.A,s.B,s.From,s.To,s.Allowed)).ToArray());
            blockedBoundaries=exterior.Concat(seams.Where(x=>!x.Allowed)).ToArray();
            boundaryIndex=new SegmentIndex(geometry.HalfExtent,blockedBoundaries.Length,i=>blockedBoundaries[i].From,i=>blockedBoundaries[i].To);
            seamIndex=new SegmentIndex(geometry.HalfExtent,Seams.Count,i=>Seams[i].From,i=>Seams[i].To);
            RegionIndexRetainedBytes=regionBytes+edgeIndex.RetainedBytes+boundaryIndex.RetainedBytes+seamIndex.RetainedBytes+8L*blockedBoundaries.Length+64+8L*edgeIndex.Entries;
        }
        // Canonical ownership is the same declared precedence partition: one
        // support containing p with no earlier bounds containing p. Keep only
        // the source support array, rather than duplicate every prefix graph.
        private MapSupport Owner(NavPoint p)
        {
            if(double.IsNaN(p.X)||double.IsNaN(p.Z)||double.IsInfinity(p.X)||double.IsInfinity(p.Z))return null;
            if(regionGrid==null){foreach(var support in regions)if(support.Contains(p))return support;return null;}
            var candidates=regionGrid[RegionBucket(p.X)*RegionGridWidth+RegionBucket(p.Z)];
            // Buckets retain authored precedence order. MapSupport.Contains is exactly
            // Bounds.Contains, so the first containing support is the original owner.
            if(candidates!=null)for(int i=0;i<candidates.Length;i++){var support=regions[candidates[i]];if(support.Contains(p))return support;}
            return null;
        }
        private bool Footprint(NavPoint point,double radius)
        {
            if(footprints==null)return UncachedFootprint(point,radius);
            var key=new FootprintKey(point,radius);if(footprints.TryGetValue(key,out bool result))return result;
            result=UncachedFootprint(point,radius);
            if(footprints.Count==FootprintCacheLimit)footprints.Remove(footprintOrder.Dequeue());
            footprints.Add(key,result);footprintOrder.Enqueue(key);return result;
        }
        private bool UncachedFootprint(NavPoint point,double radius)
        {
            if(radius<0||double.IsNaN(radius)||double.IsInfinity(radius)||Owner(point)==null||!geometry.IsFree(point,radius))return false;
            double r2=radius*radius;
            double reach=radius+1e-9;
            if(boundaryIndex==null)return !exterior.Any(e=>Distance2(point,e.From,e.To)<r2-1e-9)&&!seams.Any(e=>!e.Allowed&&Distance2(point,e.From,e.To)<r2-1e-9);
            foreach(int i in boundaryIndex.Candidates(point.X-reach,point.Z-reach,point.X+reach,point.Z+reach)){var e=blockedBoundaries[i];if(NearPoint(point,radius,e.From,e.To)&&Distance2(point,e.From,e.To)<r2-1e-9)return false;}
            return true;
        }
        internal bool TryLocate(NavPoint point,double radius,out NavLocation location)
        {location=default;var region=Owner(point);if(region==null||!Footprint(point,radius))return false;location=region.OwnedLocation(point);return true;}
        // Radius zero cannot intersect a blocked seam: the footprint predicate
        // compares squared distance with -1e-9. Preserve authored precedence and
        // the map's own geometry, without touching its owner-confined memo.
        internal bool TryLocatePoint(NavPoint point,out NavLocation location)
        {location=default;var region=Owner(point);if(region==null||!geometry.IsFree(point,0))return false;location=region.OwnedLocation(point);return true;}
        internal NavLocation OwnedCheckpointLocation(NavLocation location)
        {if(location.SurfaceId==null)return location;if(!byId.TryGetValue(new NavNameKey(location.SurfaceId),out int index))throw new ArgumentException("Unknown partition checkpoint surface.");return regions[index].OwnedLocation(location.Position);}
        internal bool Valid(NavLocation location,double radius)
        {
            if(location.SurfaceId==null||!byId.TryGetValue(new NavNameKey(location.SurfaceId),out var regionIndex)||!Footprint(location.Position,radius))return false;
            // At shared seams an explicitly bound incident region remains valid. A
            // hidden background interior can never be selected by a saved reference.
            var canonical=Owner(location.Position);if(canonical==regions[regionIndex])return true;
            double reach=1e-8+1e-9;var p=location.Position;
            if(seamIndex==null)return Seams.Any(seam=>seam.Allowed&&(seam.FromSurface==location.SurfaceId||seam.ToSurface==location.SurfaceId)&&NearPoint(p,1e-8,seam.From,seam.To)&&Distance2(p,seam.From,seam.To)<=1e-16);
            foreach(int i in seamIndex.Candidates(p.X-reach,p.Z-reach,p.X+reach,p.Z+reach)){var seam=Seams[i];if(seam.Allowed&&(seam.FromSurface==location.SurfaceId||seam.ToSurface==location.SurfaceId)&&NearPoint(p,1e-8,seam.From,seam.To)&&Distance2(p,seam.From,seam.To)<=1e-16)return true;}
            return false;
        }
        internal bool Traverse(NavLocation from,NavPoint to,double radius,out NavLocation location)
            =>TraverseCore(from,to,radius,out location,out _,out _);
        private bool TraverseCore(NavLocation from,NavPoint to,double radius,out NavLocation location,out List<double> cuts,out bool crossed,bool certifiedEndpoints=false)
        {
            cuts=null;crossed=false;location=default;
            if(certifiedEndpoints){if(!geometry.SegmentFreeBetweenFreeEndpoints(from.Position,to,radius))return false;}
            else if(!Valid(from,radius)||!Footprint(to,radius)||!geometry.SegmentFree(from.Position,to,radius))return false;
            double r2=radius*radius;
            double reach=radius+1e-9;
            if(boundaryIndex==null){if(exterior.Any(e=>SegmentDistance2(from.Position,to,e.From,e.To)<r2-1e-9)||seams.Any(e=>!e.Allowed&&SegmentDistance2(from.Position,to,e.From,e.To)<r2-1e-9))return false;}
            else foreach(int i in boundaryIndex.Candidates(Math.Min(from.Position.X,to.X)-reach,Math.Min(from.Position.Z,to.Z)-reach,Math.Max(from.Position.X,to.X)+reach,Math.Max(from.Position.Z,to.Z)+reach)){var edge=blockedBoundaries[i];if(NearSegment(from.Position,to,radius,edge.From,edge.To)&&SegmentDistance2(from.Position,to,edge.From,edge.To)<r2-1e-9)return false;}
            cuts=IndexedCuts(from.Position,to);string current=from.SurfaceId;
            for(int i=1;i<cuts.Count;i++){
                if(cuts[i]-cuts[i-1]<1e-10)continue;
                var owner=Owner(Lerp(from.Position,to,(cuts[i]+cuts[i-1])/2));if(owner==null)return false;
                if(owner.SurfaceId!=current){crossed=true;if(!Cross(current,owner.SurfaceId,Lerp(from.Position,to,cuts[i-1])))return false;current=owner.SurfaceId;}
            }
            var end=Owner(to);if(end==null)return false;
            if(current!=end.SurfaceId){crossed=true;if(!Cross(current,end.SurfaceId,to))return false;}
            location=end.OwnedLocation(to);return true;
        }
        internal bool TryTrace(NavLocation from,NavPoint to,double radius,out NavLocation endpoint,out IReadOnlyList<NavTypedLeg> trace)
            =>TraceCore(from,to,radius,false,out endpoint,out trace);
        internal bool TryTrace(NavLocation from,NavPoint to,double radius,out NavLocation endpoint,out IReadOnlyList<NavTypedLeg> trace,INavStorageReservation reservation,ILayeredNavigationProvider provider=null)
            =>TraceCore(from,to,radius,false,out endpoint,out trace,false,reservation,provider);
        internal bool TryGraphTrace(NavLocation from,NavPoint to,double radius,out NavLocation endpoint,out IReadOnlyList<NavTypedLeg> trace)
            =>TraceCore(from,to,radius,true,out endpoint,out trace);
        internal bool TryGraphTraceValidated(NavLocation from,NavPoint to,double radius,out NavLocation endpoint,out IReadOnlyList<NavTypedLeg> trace,INavStorageReservation reservation=null,ILayeredNavigationProvider provider=null)
            =>TraceCore(from,to,radius,true,out endpoint,out trace,true,reservation,provider);
        private bool TraceCore(NavLocation from,NavPoint to,double radius,bool portalsOnly,out NavLocation endpoint,out IReadOnlyList<NavTypedLeg> trace,bool certifiedEndpoints=false,INavStorageReservation reservation=null,ILayeredNavigationProvider provider=null)
        {
            using(var scratch=reservation==null?null:NavStorageScratch.Reserve(reservation,TraceScratchBound)){
            endpoint=default;trace=Array.Empty<NavTypedLeg>();
            if(!TraverseCore(from,to,radius,out endpoint,out var cuts,out bool crossed,certifiedEndpoints))return false;
            if(portalsOnly&&!crossed)return true;
            var legacy=reservation==null?new List<NavTypedLeg>():null;
            var owned=reservation==null?null:NavPagedList<NavTypedLeg>.Create(reservation.TryReserve);bool escaped=false;
            try{
            if(!TryTraceSegments(from,to,endpoint,Append,cuts))return false;
            if(owned==null)trace=Array.AsReadOnly(legacy.ToArray());else{owned.Seal();trace=owned;escaped=true;}return true;
            void Append(NavLocation start,NavLocation end,double cost,NavTypedLegKind kind,string portal)
            {
                if(owned==null){legacy.Add(new NavTypedLeg(start,end,cost,kind,portal));return;}
                var leg=NavTypedLeg.CreateOwned(start,end,cost,kind,portal,reservation,provider);try{owned.Add(leg);}finally{leg.ReleaseStorage();}
            }
            }finally{if(!escaped)owned?.ReleaseStorage();}
            }
        }
        private bool TryTraceSegments(NavLocation from,NavPoint to,NavLocation endpoint,Action<NavLocation,NavLocation,double,NavTypedLegKind,string> append,List<double> cuts=null)
        {
            if(cuts==null)cuts=IndexedCuts(from.Position,to);string current=from.SurfaceId;var last=from.Position;
            for(int i=1;i<cuts.Count;i++){
                if(cuts[i]-cuts[i-1]<1e-10)continue;var owner=Owner(Lerp(from.Position,to,(cuts[i]+cuts[i-1])/2));
                if(owner==null)return false;
                if(owner.SurfaceId!=current&&!AppendCrossing(owner.SurfaceId,Lerp(from.Position,to,cuts[i-1])))return false;
            }
            if(current!=endpoint.SurfaceId&&!AppendCrossing(endpoint.SurfaceId,to))return false;
            if(!last.Equals(to))append(new NavLocation(last,current),endpoint,LayeredNavigationProvider.Distance(last,to),NavTypedLegKind.Surface,null);
            return true;
            bool AppendCrossing(string target,NavPoint point)
            {
                if(!last.Equals(point))append(new NavLocation(last,current),new NavLocation(point,current),LayeredNavigationProvider.Distance(last,point),NavTypedLegKind.Surface,null);
                var path=IncidentPath(current,target,point);if(path==null)return false;
                foreach(var seam in path){string next=seam.FromSurface==current?seam.ToSurface:seam.FromSurface;append(new NavLocation(point,current),new NavLocation(point,next),0,NavTypedLegKind.Portal,seam.Id);current=next;}
                last=point;return true;
            }
        }
        // Non-emitting BODY validation shares the actual traversal transcript.
        // No leg/container is created or registered in the restore transaction.
        internal void VerifyTrace(NavLocation from,NavLocation to,NavClearanceProfile clearance,IReadOnlyList<NavTypedLeg> expected,PartitionNavigationProvider provider,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,provider.StorageRoot);from.RequireName(reservation);to.RequireName(reservation);
            if(from.SurfaceId==null||to.SurfaceId==null||!NavTypedLeg.SamePhysicalLocation(from,OwnedCheckpointLocation(from))||!NavTypedLeg.SamePhysicalLocation(to,OwnedCheckpointLocation(to)))throw new ArgumentException("Physical Partition edge needs exact support selectors.");
            if(clearance?.MetadataOwner!=null)NavMetadataOwnership.Require(clearance,reservation);
            if(expected!=null){var root=expected as NavOwnedStorage??(expected as INavStorageFacade)?.StorageRoot;NavStorageAdmission.RequireOwned(reservation,root);if(!root.IsImmutableCaptureRoot)throw new ArgumentException("Mutable physical Partition trace.");}
            using(NavStorageScratch.Reserve(reservation,checked(TraceScratchBound+8192))){
                if(!Traverse(from,to.Position,clearance.Radius,out var endpoint)||!endpoint.Equals(to))throw new ArgumentException("Unqualified physical Partition edge.");
                int cursor=0;bool portalSeen=false;
                if(!TryTraceSegments(from,to.Position,endpoint,Compare)||expected!=null&&cursor!=expected.Count||expected==null&&portalSeen)throw new ArgumentException("Physical Partition trace transcript contradiction.");
                void Compare(NavLocation start,NavLocation end,double cost,NavTypedLegKind kind,string portal){
                    if(kind==NavTypedLegKind.Portal)portalSeen=true;if(expected==null)return;
                    if(cursor>=expected.Count)throw new ArgumentException("Missing physical Partition trace leg.");var saved=expected[cursor++];
                    var canonicalStart=LayeredNavigationCompileWork.CanonicalLocation(provider,start);var canonicalEnd=LayeredNavigationCompileWork.CanonicalLocation(provider,end);string canonicalPortal=null;
                    if(portal!=null){foreach(var seam in provider.Seams)if(seam.Id==portal){canonicalPortal=seam.Id;break;}}
                    if(saved==null||!saved.HasCanonicalProvider(provider.StorageRoot)||!NavTypedLeg.SamePhysicalLocation(saved.From,canonicalStart)||!NavTypedLeg.SamePhysicalLocation(saved.To,canonicalEnd)||saved.Cost!=cost||saved.Kind!=kind||!ReferenceEquals(saved.PortalId,canonicalPortal))throw new ArgumentException("Physical Partition trace leg/owner contradiction.");
                }
            }
        }
        private IReadOnlyList<NavPartitionSeam> IncidentPath(string from,string to,NavPoint point)
        {
            var incident=Incident(point).OrderBy(s=>s.Id,StringComparer.Ordinal).ToArray();
            var queue=new Queue<string>();var visited=new HashSet<string>{from};
            var prior=new Dictionary<string,Tuple<string,NavPartitionSeam>>();queue.Enqueue(from);
            while(queue.Count>0){
                var current=queue.Dequeue();if(current==to)break;
                foreach(var s in incident){
                    string next=s.FromSurface==current?s.ToSurface:s.ToSurface==current?s.FromSurface:null;
                    if(next==null||!visited.Add(next))continue;
                    prior[next]=Tuple.Create(current,s);queue.Enqueue(next);
                }
            }
            if(!visited.Contains(to))return null;
            var result=new List<NavPartitionSeam>();for(var current=to;current!=from;current=prior[current].Item1)result.Add(prior[current].Item2);
            result.Reverse();return result;
        }
        private bool Cross(string a,string b,NavPoint point)
        {
            // A vertex may join several authorized regions. Only incident portals
            // participate: a remote ramp cannot connect stacked layers here.
            var incident=Incident(point);
            var reached=new HashSet<string>{a};bool changed=true;
            while(changed){changed=false;foreach(var s in incident){if(reached.Contains(s.FromSurface)&&reached.Add(s.ToSurface))changed=true;if(reached.Contains(s.ToSurface)&&reached.Add(s.FromSurface))changed=true;}}
            return reached.Contains(b);
        }
        private List<NavPartitionSeam> Incident(NavPoint point){
            var result=new List<NavPartitionSeam>();double reach=1e-7+1e-9;
            if(seamIndex==null){foreach(var seam in Seams)if(seam.Allowed&&NearPoint(point,1e-7,seam.From,seam.To)&&Distance2(point,seam.From,seam.To)<=1e-14)result.Add(seam);return result;}
            foreach(int i in seamIndex.Candidates(point.X-reach,point.Z-reach,point.X+reach,point.Z+reach)){var seam=Seams[i];if(seam.Allowed&&NearPoint(point,1e-7,seam.From,seam.To)&&Distance2(point,seam.From,seam.To)<=1e-14)result.Add(seam);}
            return result;
        }
        private List<double> IndexedCuts(NavPoint a,NavPoint b){
            if(edgeIndex==null)return Cuts(a,b,edges);
            var cuts=new List<double>{0,1};double dx=b.X-a.X,dz=b.Z-a.Z,length=dx*dx+dz*dz;if(length==0)return cuts;
            double margin=1e-7+(1e-10+1e-12)/Math.Sqrt(length),reach=margin+1e-9;
            cutCandidates.Clear();foreach(int i in edgeIndex.Candidates(Math.Min(a.X,b.X)-reach,Math.Min(a.Z,b.Z)-reach,Math.Max(a.X,b.X)+reach,Math.Max(a.Z,b.Z)+reach))cutCandidates.Add(i);
            cutCandidates.Sort();int previous=-1;
            foreach(int i in cutCandidates){if(i==previous)continue;previous=i;AddCuts(cuts,a,b,dx,dz,length,margin,edges[i]);}
            cuts.Sort();return cuts.Distinct().ToList();
        }
        private static void AddCuts(List<double> cuts,NavPoint a,NavPoint b,double dx,double dz,double length,double margin,Tuple<NavPoint,NavPoint> edge){
            var c=edge.Item1;var d=edge.Item2;if(!NearSegment(a,b,margin,c,d))return;double ex=d.X-c.X,ez=d.Z-c.Z,den=Cross2(dx,dz,ex,ez);
            if(Math.Abs(den)>1e-12){double t=Cross2(c.X-a.X,c.Z-a.Z,ex,ez)/den,u=Cross2(c.X-a.X,c.Z-a.Z,dx,dz)/den;if(t>=0&&t<=1&&u>=0&&u<=1)cuts.Add(t);}
            else if(Math.Abs(Cross2(c.X-a.X,c.Z-a.Z,dx,dz))<=1e-10){foreach(var p in new[]{c,d}){double t=((p.X-a.X)*dx+(p.Z-a.Z)*dz)/length;if(t>0&&t<1)cuts.Add(t);}}
        }
        // Current providers author one combat family. This query does not replace
        // the existing GroundFireClear, ballistics or owner/team fog predicates.
        internal bool Combat(NavLocation a,NavLocation b)=>Valid(a,0)&&Valid(b,0);
        private static IEnumerable<Tuple<NavPoint,NavPoint>> Edges(NavObstacle bounds)
        {var vertices=bounds.Footprint.Vertices;for(int i=0;i<vertices.Count;i++)yield return Tuple.Create(vertices[i],vertices[(i+1)%vertices.Count]);}
        private static NavPoint Lerp(NavPoint a,NavPoint b,double t)=>new NavPoint(a.X+(b.X-a.X)*t,a.Z+(b.Z-a.Z)*t);
        private static double Cross2(double x,double z,double xx,double zz)=>x*zz-z*xx;
        private static List<double> Cuts(NavPoint a,NavPoint b,Tuple<NavPoint,NavPoint>[] edges)
        {
            var cuts=new List<double>{0,1};double dx=b.X-a.X,dz=b.Z-a.Z,length=dx*dx+dz*dz;if(length==0)return cuts;
            // Preserve the collinearity tolerance even for extremely short segments.
            double margin=1e-7+(1e-10+1e-12)/Math.Sqrt(length);
            foreach(var edge in edges){var c=edge.Item1;var d=edge.Item2;if(!NearSegment(a,b,margin,c,d))continue;double ex=d.X-c.X,ez=d.Z-c.Z,den=Cross2(dx,dz,ex,ez);
                if(Math.Abs(den)>1e-12){double t=Cross2(c.X-a.X,c.Z-a.Z,ex,ez)/den,u=Cross2(c.X-a.X,c.Z-a.Z,dx,dz)/den;if(t>=0&&t<=1&&u>=0&&u<=1)cuts.Add(t);}
                else if(Math.Abs(Cross2(c.X-a.X,c.Z-a.Z,dx,dz))<=1e-10){foreach(var p in new[]{c,d}){double t=((p.X-a.X)*dx+(p.Z-a.Z)*dz)/length;if(t>0&&t<1)cuts.Add(t);}}
            }
            cuts.Sort();return cuts.Distinct().ToList();
        }
        private static bool NearPoint(NavPoint point,double radius,NavPoint a,NavPoint b)
        {double reach=radius+1e-9;return point.X+reach>=Math.Min(a.X,b.X)&&point.X-reach<=Math.Max(a.X,b.X)&&point.Z+reach>=Math.Min(a.Z,b.Z)&&point.Z-reach<=Math.Max(a.Z,b.Z);}
        private static bool NearSegment(NavPoint a,NavPoint b,double radius,NavPoint c,NavPoint d)
        {double reach=radius+1e-9;return Math.Max(a.X,b.X)+reach>=Math.Min(c.X,d.X)&&Math.Min(a.X,b.X)-reach<=Math.Max(c.X,d.X)&&Math.Max(a.Z,b.Z)+reach>=Math.Min(c.Z,d.Z)&&Math.Min(a.Z,b.Z)-reach<=Math.Max(c.Z,d.Z);}
        private static double Distance2(NavPoint p,NavPoint a,NavPoint b)
        {double dx=b.X-a.X,dz=b.Z-a.Z,l=dx*dx+dz*dz,t=l==0?0:Math.Max(0,Math.Min(1,((p.X-a.X)*dx+(p.Z-a.Z)*dz)/l));dx=p.X-a.X-dx*t;dz=p.Z-a.Z-dz*t;return dx*dx+dz*dz;}
        private static double SegmentDistance2(NavPoint a,NavPoint b,NavPoint c,NavPoint d)
        {
            double dx=b.X-a.X,dz=b.Z-a.Z,ex=d.X-c.X,ez=d.Z-c.Z,den=Cross2(dx,dz,ex,ez);
            if(Math.Abs(den)>1e-12){double t=Cross2(c.X-a.X,c.Z-a.Z,ex,ez)/den,u=Cross2(c.X-a.X,c.Z-a.Z,dx,dz)/den;if(t>=0&&t<=1&&u>=0&&u<=1)return 0;}
            return Math.Min(Math.Min(Distance2(a,c,d),Distance2(b,c,d)),Math.Min(Distance2(c,a,b),Distance2(d,a,b)));
        }
    }
}
