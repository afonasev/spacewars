using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Exact source polygons/height planes, with the SAME precedence as SupportAt.
    // Renderer meshes, decorative SurfaceProfile and observed/hidden units are absent.
    public sealed class RecastAuthorityGeometry
    {
        public const int Version=2;
        public const string Upstream="6dc1667f580357e8a2154c28b7867bea7e8ad3a7";
        internal bool IsLayered=>terrain is ILayeredPlayableTerrain;
        internal NavLocation NativeEndpoint(NavLocation endpoint,NavLocation other)=>(terrain as IRecastPhysicalTerrain)?.NativeEndpoint(endpoint,other)??endpoint;
        internal IReadOnlyList<NavTypedLeg> NativeAttachment(NavLocation from,NavLocation to)=>(terrain as IRecastPhysicalTerrain)?.NativeAttachment(from,to);
        internal IReadOnlyList<NavTypedLeg> ExpandPortal(uint id,bool forward)=>(terrain as IRecastPhysicalTerrain)?.ExpandPortal(Portal(id).Id,forward);
        internal NavLayerPortal Portal(uint id){var p=(terrain as ILayeredPlayableTerrain)?.LayeredNavigation?.Portals;if(p==null||id<1||id>p.Count)throw new ArgumentException("Unknown native authored portal.");return p[(int)id-1];}
        internal readonly float[] Links; internal readonly byte[] LinkAreas,LinkDirections; internal readonly uint[] LinkIds;
        internal readonly float[] Vertices;internal readonly int[] Triangles;internal readonly byte[] Areas,Directed;
        private readonly string[] areaIds;
        private readonly IPlayableTerrain terrain;private readonly NavGeometry sourceGeometry;
        internal double SupportHalfExtent {get;}
        internal NavOwnedStorage SourceStorageRoot=>sourceGeometry?.StorageRoot;
        internal bool DirectPhysical(NavLocation from,NavLocation to)=>terrain is StackedRampQualificationMap physical&&from.SurfaceId=="ramp-support"&&to.SurfaceId=="ramp-support"&&physical.Provider.CanSweep(from,to,new NavClearanceProfile("ground",1,"ground",0,Radius))&&(sourceGeometry==null||sourceGeometry.SegmentFree(from.Position,to.Position,Radius));
        internal readonly float CellSize,CellHeight,Radius;
        public string Binding{get;}
        public int TriangleCount=>Areas.Length;
        private RecastAuthorityGeometry(IPlayableTerrain terrain,Dictionary<string,byte> ids,float[] vertices,int[] triangles,byte[] areas,byte[] directed,NavigationProfile profile,int visibleGeometryRevision,float[] links,byte[] linkAreas,byte[] linkDirections,uint[] linkIds,NavGeometry sourceGeometry)
        {
            this.sourceGeometry=sourceGeometry.Obstacles.Count==0?null:sourceGeometry;SupportHalfExtent=sourceGeometry.HalfExtent;
            Links=links;LinkAreas=linkAreas;LinkDirections=linkDirections;LinkIds=linkIds;
            this.terrain=terrain;areaIds=ids.OrderBy(x=>x.Value).Select(x=>x.Key).ToArray();Vertices=vertices;Triangles=triangles;Areas=areas;Directed=directed;
            // Raster precision derives from the existing local geometry resolution.
            CellSize=(float)profile.LocalGridCell;CellHeight=CellSize/4;Radius=(float)profile.Radius;
            Binding=NavContract.Digest(w=>{w.Write("recast-authority-v2");w.Write(Upstream);w.Write(terrain?.Id??NavLocation.FlatSurface);w.Write(terrain?.Revision??1);w.Write(terrain?.SurfaceSemanticsVersion??1);w.Write(visibleGeometryRevision);w.Write(profile.ProfileId);w.Write(profile.Revision);
                w.Write(CellSize);w.Write(CellHeight);w.Write(Radius);foreach(var pair in ids.OrderBy(x=>x.Value)){w.Write(pair.Key);w.Write(pair.Value);}
                w.Write(vertices.Length);foreach(float x in vertices)w.Write(x);w.Write(triangles.Length);foreach(int x in triangles)w.Write(x);foreach(byte x in areas)w.Write(x);foreach(byte x in directed)w.Write(x);w.Write(Links.Length);foreach(float x in Links)w.Write(x);foreach(byte x in LinkAreas)w.Write(x);foreach(byte x in LinkDirections)w.Write(x);foreach(uint x in LinkIds)w.Write(x);});
        }
        internal string Surface(byte area)=>area>0&&area<=areaIds.Length?areaIds[area-1]:throw new ArgumentException("Unknown authority area.");
        internal byte Area(string surface){int index=Array.IndexOf(areaIds,surface);return index>=0?(byte)(index+1):throw new ArgumentException("Unknown authority surface.");}
        internal float[] Point(NavLocation location)
        {
            if(terrain==null){
                if(location.SurfaceId!=NavLocation.FlatSurface||Math.Abs(location.Position.X)>SupportHalfExtent||Math.Abs(location.Position.Z)>SupportHalfExtent)throw new ArgumentException("Unknown flat support location.");
                return new[]{(float)location.Position.X,0f,(float)location.Position.Z};
            }
            MapSupport support=null;
            foreach(var piece in terrain.Supports)if(piece.SurfaceId==location.SurfaceId&&piece.Bounds.Footprint.Contains(location.Position)){
                if(support!=null&&Math.Abs(support.HeightAt(location.Position)-piece.HeightAt(location.Position))>1e-8)throw new ArgumentException("Conflicting physical support pieces.");support=piece;
            }
            if(support==null)throw new ArgumentException("Unknown physical support location.");
            return new[]{(float)location.Position.X,(float)support.HeightAt(location.Position),(float)location.Position.Z};
        }
        internal const int SourceTriangleCapacity=100000,ClipPartCapacity=10000;
        internal bool UsedFoundation {get;private set;}
        internal long PayloadBytes=>checked(NavStorageLayout.ObjectBytesBound(typeof(RecastAuthorityGeometry))+128+4L*Vertices.Length+4L*Triangles.Length+Areas.Length+Directed.Length+4L*Links.Length+LinkAreas.Length+LinkDirections.Length+4L*LinkIds.Length+8L*areaIds.Length+24+2L*Binding.Length);
        // The construction reservation covers live list capacities, copied output,
        // digest stream and both clipping generations under the existing limits.
        internal static long ScratchBound(IPlayableTerrain terrain,NavGeometry geometry,bool foundation){
            long sources=terrain==null?2:terrain.Supports.Sum(x=>(long)x.Bounds.Footprint.Triangles.Count/3);
            long points=checked(3+3*sources);
            long clipping=foundation?checked(4L*ClipPartCapacity*(64+16*points)+4L*65536*8+6L*(64+32*points)):0;
            long source=checked(64L*(SourceTriangleCapacity+2));
            long obstacle=geometry.Obstacles.Sum(o=>128L+256L*(o.CircleRadius>0?32:o.Polygon?.Vertices.Count??4));
            return checked(4*source+clipping+obstacle+8192);
        }
        public static RecastAuthorityGeometry Prepare(IPlayableTerrain terrain,NavGeometry visibleGeometry,NavigationProfile profile,Func<bool> cancelled=null)
            =>PrepareCore(terrain,visibleGeometry,profile,null,cancelled);
        internal static RecastAuthorityGeometry PrepareFromFoundation(IPlayableTerrain terrain,NavGeometry visibleGeometry,NavigationProfile profile,RecastAuthorityGeometry foundation,Func<bool> cancelled=null)
            =>PrepareCore(terrain,visibleGeometry,profile,foundation,cancelled);
        private static RecastAuthorityGeometry PrepareCore(IPlayableTerrain terrain,NavGeometry visibleGeometry,NavigationProfile profile,RecastAuthorityGeometry foundation,Func<bool> cancelled)
        {
            if(visibleGeometry==null||profile==null)throw new ArgumentNullException();
            if(cancelled?.Invoke()==true)throw new OperationCanceledException();
            if(foundation!=null&&(!ReferenceEquals(foundation.terrain,terrain)||foundation.SupportHalfExtent!=visibleGeometry.HalfExtent))throw new ArgumentException("Foundation terrain owner changed.");
            var layered=(terrain as ILayeredPlayableTerrain)?.LayeredNavigation;
            // Flat gameplay has no authored map. Its support is the actual
            // arena plane; this temporary compiler input never becomes a map
            // or changes the gameplay provider/binding.
            IReadOnlyList<MapSupport> supports=terrain?.Supports??new[]{new MapSupport("flat-plane",new NavObstacle(-visibleGeometry.HalfExtent,-visibleGeometry.HalfExtent,visibleGeometry.HalfExtent,visibleGeometry.HalfExtent),false,surfaceId:NavLocation.FlatSurface)};
            if(supports.Count<1||supports.Select(s=>s.SurfaceId).Distinct(StringComparer.Ordinal).Count()>63)throw new InvalidOperationException("Recast semantic area capacity exceeded.");
            var ids=new Dictionary<string,byte>(StringComparer.Ordinal);foreach(var s in supports){if(string.IsNullOrEmpty(s.SurfaceId))throw new ArgumentException("Missing support surface.");if(!ids.ContainsKey(s.SurfaceId))ids.Add(s.SurfaceId,(byte)(ids.Count+1));}
            var directed=new byte[4096];foreach(byte a in ids.Values)directed[a*64+a]=1;
            // Non-layered authority TerrainLocations treats these named seams as bidirectional.
            if(layered==null&&terrain!=null)foreach(var link in terrain.SurfaceTransitions){byte a=ids[link.From],b=ids[link.To];directed[a*64+b]=directed[b*64+a]=1;}
            var links=new List<float>();var linkAreas=new List<byte>();var linkDirections=new List<byte>();var linkIds=new List<uint>();
            if(layered!=null){
                if(layered.Surfaces.Count!=ids.Count||layered.Surfaces.Any(s=>!ids.ContainsKey(s.Id)))throw new ArgumentException("Physical and semantic layers differ.");
                var clearance=new NavClearanceProfile("ground",1,"ground",0,profile.Radius);
                foreach(var portal in layered.Portals){
                    if(terrain is IRecastPhysicalTerrain physical&&!physical.NativePortal(portal.Id))continue;
                    if(layered.QualifyPortal(portal.Id,clearance).Status!=NavConnectorStatus.Ready||!visibleGeometry.SegmentFree(portal.Entry.Position,portal.Exit.Position,profile.Radius))continue;
                    foreach(var endpoint in new[]{portal.Entry,portal.Exit}){
                        MapSupport piece=null;foreach(var candidate in supports)if(candidate.SurfaceId==endpoint.SurfaceId&&candidate.Bounds.Footprint.Contains(endpoint.Position)){if(piece!=null&&Math.Abs(piece.HeightAt(endpoint.Position)-candidate.HeightAt(endpoint.Position))>1e-8)throw new ArgumentException("Conflicting portal support.");piece=candidate;}
                        if(piece==null)throw new ArgumentException("Portal has no physical support.");links.Add((float)endpoint.Position.X);links.Add((float)piece.HeightAt(endpoint.Position));links.Add((float)endpoint.Position.Z);
                    }
                    linkAreas.Add(ids[portal.Entry.SurfaceId]);linkAreas.Add(ids[portal.Exit.SurfaceId]);linkDirections.Add((byte)(portal.Direction==NavPortalDirection.Both?1:0));linkIds.Add((uint)(Enumerable.Range(0,layered.Portals.Count).First(i=>layered.Portals[i].Id==portal.Id)+1));
                }
            }
            var vertices=new List<float>();var triangles=new List<int>();var areas=new List<byte>();
            void Check(){if(cancelled?.Invoke()==true)throw new OperationCanceledException();if(areas.Count>SourceTriangleCapacity)throw new InvalidOperationException("Recast source triangle capacity exceeded.");}
            void Triangle(NavPoint a,NavPoint b,NavPoint c,Func<NavPoint,double> height,byte area){
                Check();int i=vertices.Count/3;foreach(var p in new[]{a,b,c}){vertices.Add((float)p.X);vertices.Add((float)height(p));vertices.Add((float)p.Z);}
                // NavPolygon is CCW in XZ; Recast needs upward Y winding.
                triangles.Add(i);triangles.Add(i+2);triangles.Add(i+1);areas.Add(area);
            }
            if(foundation!=null){vertices.AddRange(foundation.Vertices);triangles.AddRange(foundation.Triangles);areas.AddRange(foundation.Areas);}
            else {
            var earlier=new List<NavPoint[]>();
            string priorSurface=null;
            foreach(var support in (layered==null?supports:supports.OrderBy(s=>s.SurfaceId,StringComparer.Ordinal).ToArray())){
                if(terrain is IRecastPhysicalTerrain physical&&!physical.NativeSupport(support))continue;
                if(layered!=null&&support.SurfaceId!=priorSurface)earlier.Clear();priorSurface=support.SurfaceId;
                var poly=support.Bounds.Footprint;
                for(int i=0;i<poly.Triangles.Count;i+=3){
                    var parts=new List<NavPoint[]>{new[]{poly.Vertices[poly.Triangles[i]],poly.Vertices[poly.Triangles[i+1]],poly.Vertices[poly.Triangles[i+2]]}};
                    foreach(var cutter in earlier){var next=new List<NavPoint[]>();foreach(var part in parts)Subtract(part,cutter,next);parts=next;Check();if(parts.Count==0)break;if(parts.Count>ClipPartCapacity)throw new InvalidOperationException("Recast clipping capacity exceeded.");}
                    if(layered!=null){
                        var declaration=layered.Surfaces.Single(s=>s.Id==support.SurfaceId);
                        foreach(var obstacle in declaration.Area.Blockers){var boundary=obstacle.Footprint;for(int k=0;k<boundary.Triangles.Count;k+=3){var cutter=new[]{boundary.Vertices[boundary.Triangles[k]],boundary.Vertices[boundary.Triangles[k+1]],boundary.Vertices[boundary.Triangles[k+2]]};var next=new List<NavPoint[]>();foreach(var part in parts)Subtract(part,cutter,next);parts=next;Check();if(parts.Count>ClipPartCapacity)throw new InvalidOperationException("Recast clipping capacity exceeded.");}}
                    }
                    foreach(var part in parts)for(int j=1;j<part.Length-1;j++)if(Math.Abs(Cross(part[0],part[j],part[j+1]))>1e-10)Triangle(part[0],part[j],part[j+1],support.HeightAt,ids[support.SurfaceId]);
                }
                for(int i=0;i<poly.Triangles.Count;i+=3)earlier.Add(new[]{poly.Vertices[poly.Triangles[i]],poly.Vertices[poly.Triangles[i+1]],poly.Vertices[poly.Triangles[i+2]]});
            }
            }
            // Every obstacle is from the published knowledge-scoped authority snapshot.
            // A null-area prism removes its footprint on all supported gameplay layers,
            // matching current NavGeometry's XZ obstacle semantics.
            var heights=supports.SelectMany(s=>s.Bounds.Footprint.Vertices.Select(s.HeightAt)).ToArray();
            double top=heights.Max()+2,bottom=heights.Min()-2;
            foreach(var obstacle in visibleGeometry.Obstacles){
                NavPolygon p;
                if(obstacle.CircleRadius>0){var pts=new List<NavPoint>();for(int i=0;i<32;i++){double angle=2*Math.PI*i/32;double r=obstacle.CircleRadius/Math.Cos(Math.PI/32);pts.Add(new NavPoint(obstacle.CircleCenter.X+Math.Cos(angle)*r,obstacle.CircleCenter.Z+Math.Sin(angle)*r));}p=new NavPolygon(pts);}
                else p=obstacle.Footprint;
                for(int i=0;i<p.Triangles.Count;i+=3)Triangle(p.Vertices[p.Triangles[i]],p.Vertices[p.Triangles[i+1]],p.Vertices[p.Triangles[i+2]],_=>top,0);
                for(int i=0;i<p.Vertices.Count;i++){var a=p.Vertices[i];var b=p.Vertices[(i+1)%p.Vertices.Count];
                    int n=vertices.Count/3;foreach(var pair in new[]{Tuple.Create(a,bottom),Tuple.Create(b,bottom),Tuple.Create(b,top),Tuple.Create(a,top)}){vertices.Add((float)pair.Item1.X);vertices.Add((float)pair.Item2);vertices.Add((float)pair.Item1.Z);}triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});areas.Add(0);areas.Add(0);Check();}
            }
            return new RecastAuthorityGeometry(terrain,ids,vertices.ToArray(),triangles.ToArray(),areas.ToArray(),directed,profile,visibleGeometry.Revision,links.ToArray(),linkAreas.ToArray(),linkDirections.ToArray(),linkIds.ToArray(),visibleGeometry){UsedFoundation=foundation!=null};
        }
        private static double Cross(NavPoint a,NavPoint b,NavPoint p)=>(b.X-a.X)*(p.Z-a.Z)-(b.Z-a.Z)*(p.X-a.X);
        private static NavPoint[] Clip(NavPoint[] polygon,NavPoint a,NavPoint b,bool inside)
        {
            var output=new List<NavPoint>();if(polygon.Length==0)return output.ToArray();var last=polygon[polygon.Length-1];double dl=Cross(a,b,last);bool il=inside?dl>=0:dl<=0;
            foreach(var p in polygon){double d=Cross(a,b,p);bool ip=inside?d>=0:d<=0;if(ip!=il){double t=dl/(dl-d);output.Add(new NavPoint(last.X+(p.X-last.X)*t,last.Z+(p.Z-last.Z)*t));}if(ip)output.Add(p);last=p;dl=d;il=ip;}
            return output.ToArray();
        }
        private static void Subtract(NavPoint[] polygon,NavPoint[] cutter,List<NavPoint[]> output)
        {
            // Split by each convex cutter edge; emitted outside pieces are disjoint.
            var remaining=polygon;
            for(int i=0;i<3&&remaining.Length>=3;i++){var a=cutter[i];var b=cutter[(i+1)%3];var outside=Clip(remaining,a,b,false);if(outside.Length>=3&&Enumerable.Range(1,outside.Length-2).Any(j=>Math.Abs(Cross(outside[0],outside[j],outside[j+1]))>1e-10))output.Add(outside);remaining=Clip(remaining,a,b,true);}
        }
    }
}
