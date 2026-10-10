using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    // A physical boundary segment in the authored precedence partition. These
    // segments come from the same arrangement used by TryTraverse; transition
    // IDs alone cannot supply their geometry or clearance.
    public sealed class NavPartitionSeam
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
    internal sealed class TerrainLocations
    {
        private sealed class Seam { internal string A,B; internal NavPoint From,To; internal bool Allowed; }
        private readonly NavGeometry geometry; private readonly Tuple<NavPoint,NavPoint>[] edges; private readonly MapSupport[] regions;
        private readonly Dictionary<string,int> byId; private readonly List<Seam> seams=new List<Seam>();
        private readonly List<Seam> exterior=new List<Seam>();
        internal IReadOnlyList<NavSurfaceTransition> Transitions{get;}
        internal IReadOnlyList<NavPartitionSeam> Seams{get;}
        internal TerrainLocations(NavGeometry geometry,IEnumerable<MapSupport> authoredPrecedence,IEnumerable<NavSurfaceTransition> declared)
        {
            this.geometry=geometry;var supports=authoredPrecedence.ToArray();
            if(supports.Any(s=>string.IsNullOrWhiteSpace(s.SurfaceId)||s.SurfaceId==NavLocation.FlatSurface)||supports.Select(s=>s.SurfaceId).Distinct().Count()!=supports.Length)throw new ArgumentException("Provider must author unique surface keys.");
            regions=supports;
            byId=regions.Select((s,i)=>new {s.SurfaceId,Index=i}).ToDictionary(r=>r.SurfaceId,r=>r.Index);
            var declarations=declared.ToArray();if(declarations.Select(t=>t.Id).Distinct().Count()!=declarations.Length||declarations.Any(t=>!byId.ContainsKey(t.From)||!byId.ContainsKey(t.To)))throw new ArgumentException("Invalid provider transitions.");
            edges=supports.SelectMany(s=>Edges(s.Bounds)).ToArray();
            // Arrangement vertices split every ownership boundary, including overlaps.
            foreach(var edge in edges){
                var cuts=Cuts(edge.Item1,edge.Item2,edges);
                for(int i=1;i<cuts.Count;i++){
                    if(cuts[i]-cuts[i-1]<1e-10)continue;
                    var a=Lerp(edge.Item1,edge.Item2,cuts[i-1]);var b=Lerp(edge.Item1,edge.Item2,cuts[i]);var mid=Lerp(a,b,.5);
                    double dx=b.X-a.X,dz=b.Z-a.Z,len=Math.Sqrt(dx*dx+dz*dz),epsilon=Math.Min(1e-7,len/8);
                    var left=Owner(new NavPoint(mid.X-dz/len*epsilon,mid.Z+dx/len*epsilon));var right=Owner(new NavPoint(mid.X+dz/len*epsilon,mid.Z-dx/len*epsilon));
                    if(left==right)continue;
                    var seam=new Seam{From=a,To=b,A=left?.SurfaceId,B=right?.SurfaceId};
                    if(left==null||right==null){exterior.Add(seam);continue;}
                    bool declaredPair=declarations.Any(t=>t.From==seam.A&&t.To==seam.B||t.From==seam.B&&t.To==seam.A);
                    seam.Allowed=declaredPair&&Math.Abs(left.HeightAt(a)-right.HeightAt(a))<=1e-7&&Math.Abs(left.HeightAt(b)-right.HeightAt(b))<=1e-7;
                    seams.Add(seam);
                }
            }
            Transitions=Array.AsReadOnly(declarations);
            Seams=Array.AsReadOnly(seams.Select(s=>new NavPartitionSeam(s.A,s.B,s.From,s.To,s.Allowed)).ToArray());
        }
        // Canonical ownership is the same declared precedence partition: one
        // support containing p with no earlier bounds containing p. Keep only
        // the source support array, rather than duplicate every prefix graph.
        private MapSupport Owner(NavPoint p)
        {
            for(int i=0;i<regions.Length;i++){
                if(!regions[i].Contains(p))continue;
                bool excluded=false;for(int j=0;j<i;j++)if(regions[j].Bounds.Contains(p)){excluded=true;break;}
                if(!excluded)return regions[i];
            }
            return null;
        }
        private bool Footprint(NavPoint point,double radius)
        {
            if(radius<0||double.IsNaN(radius)||double.IsInfinity(radius)||Owner(point)==null||!geometry.IsFree(point,radius))return false;
            double r2=radius*radius;
            return !exterior.Any(e=>Distance2(point,e.From,e.To)<r2-1e-9)&&!seams.Any(e=>!e.Allowed&&Distance2(point,e.From,e.To)<r2-1e-9);
        }
        internal bool TryLocate(NavPoint point,double radius,out NavLocation location)
        {location=default;var region=Owner(point);if(region==null||!Footprint(point,radius))return false;location=new NavLocation(point,region.SurfaceId);return true;}
        internal bool Valid(NavLocation location,double radius)
        {
            if(location.SurfaceId==null||!byId.TryGetValue(location.SurfaceId,out var regionIndex)||!Footprint(location.Position,radius))return false;
            // At shared seams an explicitly bound incident region remains valid. A
            // hidden background interior can never be selected by a saved reference.
            var canonical=Owner(location.Position);return canonical==regions[regionIndex]||seams.Any(s=>s.Allowed&&(s.A==location.SurfaceId||s.B==location.SurfaceId)&&Distance2(location.Position,s.From,s.To)<=1e-16);
        }
        internal bool Traverse(NavLocation from,NavPoint to,double radius,out NavLocation location)
        {
            location=default;if(!Valid(from,radius)||!Footprint(to,radius)||!geometry.SegmentFree(from.Position,to,radius))return false;
            double r2=radius*radius;
            if(exterior.Any(s=>SegmentDistance2(from.Position,to,s.From,s.To)<r2-1e-9)||seams.Any(s=>!s.Allowed&&SegmentDistance2(from.Position,to,s.From,s.To)<r2-1e-9))return false;
            var cuts=Cuts(from.Position,to,edges);string current=from.SurfaceId;
            for(int i=1;i<cuts.Count;i++){
                if(cuts[i]-cuts[i-1]<1e-10)continue;
                var owner=Owner(Lerp(from.Position,to,(cuts[i]+cuts[i-1])/2));if(owner==null)return false;
                if(owner.SurfaceId!=current){if(!Cross(current,owner.SurfaceId,Lerp(from.Position,to,cuts[i-1])))return false;current=owner.SurfaceId;}
            }
            var end=Owner(to);if(end==null)return false;
            if(current!=end.SurfaceId&&!Cross(current,end.SurfaceId,to))return false;
            location=new NavLocation(to,end.SurfaceId);return true;
        }
        internal bool TryTrace(NavLocation from,NavPoint to,double radius,out NavLocation endpoint,out IReadOnlyList<NavTypedLeg> trace)
        {
            endpoint=default;trace=Array.Empty<NavTypedLeg>();
            if(!Traverse(from,to,radius,out endpoint))return false;
            var result=new List<NavTypedLeg>();var cuts=Cuts(from.Position,to,edges);
            string current=from.SurfaceId;var last=from.Position;
            for(int i=1;i<cuts.Count;i++){
                if(cuts[i]-cuts[i-1]<1e-10)continue;
                var owner=Owner(Lerp(from.Position,to,(cuts[i]+cuts[i-1])/2));
                if(owner==null)return false;
                if(owner.SurfaceId!=current&&!AppendCrossing(owner.SurfaceId,Lerp(from.Position,to,cuts[i-1])))return false;
            }
            if(current!=endpoint.SurfaceId&&!AppendCrossing(endpoint.SurfaceId,to))return false;
            if(!last.Equals(to))result.Add(new NavTypedLeg(new NavLocation(last,current),endpoint,
                LayeredNavigationProvider.Distance(last,to),NavTypedLegKind.Surface,null));
            trace=Array.AsReadOnly(result.ToArray());return true;

            bool AppendCrossing(string target,NavPoint point)
            {
                if(!last.Equals(point))result.Add(new NavTypedLeg(new NavLocation(last,current),new NavLocation(point,current),
                    LayeredNavigationProvider.Distance(last,point),NavTypedLegKind.Surface,null));
                var path=IncidentPath(current,target,point);if(path==null)return false;
                foreach(var seam in path){
                    string next=seam.FromSurface==current?seam.ToSurface:seam.FromSurface;
                    result.Add(new NavTypedLeg(new NavLocation(point,current),new NavLocation(point,next),0,NavTypedLegKind.Portal,seam.Id));
                    current=next;
                }
                last=point;return true;
            }
        }
        private IReadOnlyList<NavPartitionSeam> IncidentPath(string from,string to,NavPoint point)
        {
            var incident=Seams.Where(s=>s.Allowed&&Distance2(point,s.From,s.To)<=1e-14)
                .OrderBy(s=>s.Id,StringComparer.Ordinal).ToArray();
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
            var incident=seams.Where(s=>s.Allowed&&Distance2(point,s.From,s.To)<=1e-14).ToArray();
            var reached=new HashSet<string>{a};bool changed=true;
            while(changed){changed=false;foreach(var s in incident){if(reached.Contains(s.A)&&reached.Add(s.B))changed=true;if(reached.Contains(s.B)&&reached.Add(s.A))changed=true;}}
            return reached.Contains(b);
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
            foreach(var edge in edges){var c=edge.Item1;var d=edge.Item2;double ex=d.X-c.X,ez=d.Z-c.Z,den=Cross2(dx,dz,ex,ez);
                if(Math.Abs(den)>1e-12){double t=Cross2(c.X-a.X,c.Z-a.Z,ex,ez)/den,u=Cross2(c.X-a.X,c.Z-a.Z,dx,dz)/den;if(t>=0&&t<=1&&u>=0&&u<=1)cuts.Add(t);}
                else if(Math.Abs(Cross2(c.X-a.X,c.Z-a.Z,dx,dz))<=1e-10){foreach(var p in new[]{c,d}){double t=((p.X-a.X)*dx+(p.Z-a.Z)*dz)/length;if(t>0&&t<1)cuts.Add(t);}}
            }
            cuts.Sort();return cuts.Distinct().ToList();
        }
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
