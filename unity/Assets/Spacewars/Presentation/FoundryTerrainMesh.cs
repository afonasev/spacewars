using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Sculpted presentation only. The base is the complete authoritative footprint.
    public static class FoundryTerrainMesh
    {
        public static IEnumerable<NavObstacle> Ridges(FoundryMap map)
        {
            // Each authoritative polygon already is a complete connected ridge.
            // No index-based box union: oblique edges must remain exact.
            foreach(var solid in map.Solids)yield return solid;
        }
        // A six-sided circumscribed outline contains the entire square envelope,
        // rather than trimming its corners or changing any construction rules.
        public static NavObstacle HomePad(NavPoint center,double envelope,FoundrySurfaceProfile settings)
        {
            double x=envelope*settings.homeHexExtent,z=x;
            return new NavObstacle(new NavPolygon(new[]{
                new NavPoint(center.X-x,center.Z),new NavPoint(center.X-x/2,center.Z-z),
                new NavPoint(center.X+x/2,center.Z-z),new NavPoint(center.X+x,center.Z),
                new NavPoint(center.X+x/2,center.Z+z),new NavPoint(center.X-x/2,center.Z+z)}));
        }
        public static double SiteEnvelope(TerritorySite site,PlayableProfile profile)=>site.Slots.Count==0
            ?TerritoryRules.Radius(profile,site.Kind)+1
            :profile.SlotRingRadius+Math.Max(profile.ScienceFootprintRadius,Math.Max(profile.FactoryFootprintRadius,profile.RefineryFootprintRadius))+1;
        public static NavObstacle SitePad(TerritorySite site,PlayableProfile profile,FoundrySurfaceProfile settings)
        {
            double extent=SiteEnvelope(site,profile);var p=site.Position;
            if(site.Kind==PlayableBuildingKind.Mine)
            {
                extent+=settings.mineYardMargin;
                // Stay on the mine's existing supported plane: in particular the
                // rear yards must not bridge the adjoining flank ramp.
                var support=profile.AuthoredMap.SupportAt(p);
                // Limit paired edges together so the mine remains at the yard center.
                double halfX=Math.Min(extent,Math.Min(p.X-support.Bounds.MinX,support.Bounds.MaxX-p.X));
                double halfZ=Math.Min(extent,Math.Min(p.Z-support.Bounds.MinZ,support.Bounds.MaxZ-p.Z));
                double left=p.X-halfX,right=p.X+halfX,bottom=p.Z-halfZ,top=p.Z+halfZ;
                double cut=Math.Min(right-left,top-bottom)*.175;
                return new NavObstacle(new NavPolygon(new[]{new NavPoint(left+cut,bottom),new NavPoint(right-cut,bottom),
                    new NavPoint(right,bottom+cut),new NavPoint(right,top-cut),new NavPoint(right-cut,top),
                    new NavPoint(left+cut,top),new NavPoint(left,top-cut),new NavPoint(left,bottom+cut)}));
            }
            return site.Kind==PlayableBuildingKind.Headquarters?HomePad(p,extent,settings):new NavObstacle(p.X-extent,p.Z-extent,p.X+extent,p.Z+extent);
        }
        public static Mesh MineYardSurface(NavObstacle pad,FoundryMap map)
        {
            // The supported yard is flush: no hidden bottom or buried walls.
            // Share each contour vertex across cap triangles instead of a prism.
            var points=pad.Footprint.Vertices;
            var vertices=points.Select(p=>new Vector3((float)p.X,(float)map.SurfaceHeight(p)+.006f,(float)p.Z)).ToList();
            var triangles=new List<int>();var source=pad.Footprint.Triangles;
            for(int i=0;i<source.Count;i+=3){triangles.Add(source[i]);triangles.Add(source[i+2]);triangles.Add(source[i+1]);}
            var mesh=new Mesh{name="Shared-vertex mine work yard"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        public static float RoadCoverage(FoundryMap map,NavPoint point,FoundrySurfaceProfile settings)
        {
            double distance=0;
            // Warp both coordinates continuously: branch ends and shoulders blend
            // into the ash rather than exposing the rectangular authoring mask.
            float x=(float)point.X,z=(float)point.Z;
            float dx=(Mathf.PerlinNoise(x/settings.roadBendScale+17,z/settings.roadBendScale+53)-.5f)*2*settings.roadMeander;
            float dz=(Mathf.PerlinNoise(x/settings.roadBendScale+71,z/settings.roadBendScale+11)-.5f)*2*settings.roadMeander;
            var q=new NavPoint(point.X+dx,point.Z+dz);
            foreach(var road in map.Roads)if(road.Contains(q))distance=Math.Max(distance,Math.Sqrt(road.BoundaryDistanceSquared(q)));
            float deposit=Mathf.PerlinNoise(x/settings.depositScale+29,z/settings.depositScale+83);
            float width=settings.roadFeather*(.8f+1.6f*deposit);
            float brokenShoulder=settings.roadMeander*(.15f+.65f*deposit);
            return Mathf.SmoothStep(0,1,(float)(distance-brokenShoulder)/width);
        }
        public static float Roof(float x,float z,double bottom,double top,FoundrySurfaceProfile settings)
        {
            // Cellular geology creates broad fractured blocks and low joints. It is
            // sampled in world coordinates, independently of mesh triangles/albedo.
            float px=x/settings.crownScale,pz=z/settings.crownScale;
            int ix=Mathf.FloorToInt(px),iz=Mathf.FloorToInt(pz);float first=float.MaxValue,second=float.MaxValue,block=0;
            float Hash(int a,int b){float n=Mathf.Sin(a*127.1f+b*311.7f)*43758.5453f;return n-Mathf.Floor(n);}
            for(int a=ix-1;a<=ix+1;a++)for(int b=iz-1;b<=iz+1;b++)
            {
                float cx=a+.2f+.6f*Hash(a,b),cz=b+.2f+.6f*Hash(b+19,a+37);
                float d=(px-cx)*(px-cx)+(pz-cz)*(pz-cz);
                if(d<first){second=first;first=d;block=Hash(a+53,b+11);}else if(d<second)second=d;
            }
            float joint=1-Mathf.SmoothStep(0,settings.edgeErosion/settings.crownScale,second-first);
            float relief=Mathf.Min(settings.crownRelief,(float)(top-bottom)*.35f);
            float h=(float)top-relief*(.2f+.55f*block+.25f*joint);
            return h;
        }
        public static IReadOnlyList<NavPolygon> Blocks(NavObstacle shape,FoundrySurfaceProfile settings,IReadOnlyList<NavObstacle> lava=null)
        {
            var result=new List<NavPolygon>();double width=shape.MaxX-shape.MinX,depth=shape.MaxZ-shape.MinZ;
            if(Math.Min(width,depth)<14||Math.Max(width,depth)>Math.Min(width,depth)*15)return result;
            double span=Math.Min(settings.blockSpan,Math.Min(width,depth)*.45);
            int target=Math.Min(24,Math.Max(2,(int)Math.Ceiling(width*depth/(span*span)*settings.blockCoverage)));
            // Low-discrepancy dart admission gives irregular centers, sizes and gaps:
            // no rows, tiled cells or repeating carpet. Fixed bases are generator seeds.
            double Sequence(int n,int radix){double value=0,f=1d/radix;while(n>0){value+=f*(n%radix);n/=radix;f/=radix;}return value;}
            int seed=17+(int)Math.Abs(shape.MinX*3+shape.MinZ*7)%97;
            for(int attempt=1;attempt<=target*24&&result.Count<target;attempt++)
            {
                int key=seed+attempt;
                double x=shape.MinX+width*Sequence(key,2),z=shape.MinZ+depth*Sequence(key,3);
                double size=span*(.65+.35*Sequence(key,7)),turn=(Sequence(key,5)*2-1)*settings.blockRotation*Mathf.Deg2Rad,cs=Math.Cos(turn),sn=Math.Sin(turn);
                for(int fit=0;fit<3;fit++)
                {
                    double r=size*(.48-fit*.08),stretch=.75+.25*Sequence(key,11),cut=.2+.25*Sequence(key,13);
                    NavPoint P(double a,double b)=>new NavPoint(x+r*(a*cs-b*stretch*sn),z+r*(a*sn+b*stretch*cs));
                    var ring=new NavPolygon(new[]{P(-1,-1+cut),P(-1+cut,-1),P(1-cut*.5,-1),P(1,1-cut),P(1-cut*1.5,1),P(-1,1-cut*.25)});
                    if(!shape.Footprint.StrictlyContains(ring))continue;
                    bool blocked=false;
                    foreach(var other in result)
                    {
                        if(other.Vertices.Any(ring.Contains)){blocked=true;break;}
                        for(int i=0;i<ring.Vertices.Count;i++)if(other.SegmentDistanceSquared(ring.Vertices[i],ring.Vertices[(i+1)%ring.Vertices.Count])<settings.blockJoint*settings.blockJoint){blocked=true;break;}
                        if(blocked)break;
                    }
                    if(blocked)continue;
                    foreach(var channel in lava??Array.Empty<NavObstacle>())
                    {
                        if(channel.Footprint.Vertices.Any(ring.Contains)){blocked=true;break;}
                        for(int i=0;i<ring.Vertices.Count;i++)if(channel.Footprint.SegmentDistanceSquared(ring.Vertices[i],ring.Vertices[(i+1)%ring.Vertices.Count])<settings.lavaCrustWidth*settings.lavaCrustWidth){blocked=true;break;}
                        if(blocked)break;
                    }
                    if(blocked)continue;result.Add(ring);break;
                }
            }
            return result;
        }
        public static Mesh Rock(NavObstacle shape,FoundrySurfaceProfile settings,IReadOnlyList<NavObstacle> lava=null,Func<NavPoint,double> supportHeight=null,bool industrialDeck=false)
        {
            var polygon=shape.Footprint;var vertices=new List<Vector3>();var triangles=new List<int>();
            var channels=(lava??Array.Empty<NavObstacle>()).Where(c=>c.Footprint.Vertices.All(shape.Contains)).ToArray();
            var channelDistances=new TerrainPolygonDistances(channels);var topCache=new Dictionary<(double x,double z),Vector3>();
            void Tri(Vector3 a,Vector3 b,Vector3 c)
            {
                if(Vector3.Cross(b-a,c-a).sqrMagnitude<1e-14f)return;
                int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);triangles.Add(i);triangles.Add(i+1);triangles.Add(i+2);
            }
            Vector3 Top(NavPoint p)
            {
                if(industrialDeck)return new Vector3((float)p.X,(float)shape.Top-Mathf.Max(.25f,settings.crownRelief*.55f),(float)p.Z);
                var key=(p.X,p.Z);if(topCache.TryGetValue(key,out var cached))return cached;
                float boundary=(float)Math.Sqrt(polygon.BoundaryDistanceSquared(p));
                float fracture=Mathf.PerlinNoise((float)p.X/settings.crownScale+41,(float)p.Z/settings.crownScale+97);
                double lavaDistance=channelDistances.Minimum(p);
                float headroom=settings.blockHeight*Mathf.SmoothStep(0,1,(float)lavaDistance/(settings.lavaCrustWidth*3));
                float h=Roof((float)p.X,(float)p.Z,shape.Bottom,shape.Top,settings)-headroom;
                double support=supportHeight?.Invoke(p)??shape.Bottom;
                if(supportHeight!=null&&boundary<settings.shoulderWidth)
                {
                    double nearest=double.MaxValue;var contact=p;
                    for(int edge=0;edge<polygon.Vertices.Count;edge++)
                    {
                        var a=polygon.Vertices[edge];var b=polygon.Vertices[(edge+1)%polygon.Vertices.Count];
                        double dx=b.X-a.X,dz=b.Z-a.Z,t=Math.Max(0,Math.Min(1,((p.X-a.X)*dx+(p.Z-a.Z)*dz)/(dx*dx+dz*dz)));
                        var q=new NavPoint(a.X+dx*t,a.Z+dz*t);double distance=(p.X-q.X)*(p.X-q.X)+(p.Z-q.Z)*(p.Z-q.Z);
                        if(distance<nearest){nearest=distance;contact=q;}
                    }
                    support=Math.Max(support,supportHeight(contact));
                }
                float foot=Mathf.Min((float)support+settings.footHeight,(float)shape.Top-settings.crownRelief);
                float rise=Mathf.SmoothStep(0,1,boundary/(settings.shoulderWidth*(.45f+.75f*fracture)));
                h=Mathf.Lerp(foot,h,rise);
                float shelf=h/settings.strataSpacing,whole=Mathf.Floor(shelf),part=shelf-whole;
                float terraced=(whole+Mathf.SmoothStep(.55f,.95f,part))*settings.strataSpacing;
                h=Mathf.Max(foot,Mathf.Lerp(h,terraced,settings.terraceStrength));
                if(channels.Length>0)
                {
                    h=Mathf.Lerp((float)shape.Top,h,Mathf.SmoothStep(0,1,(float)lavaDistance/settings.lavaCrustWidth));
                }
                var result=new Vector3((float)p.X,Mathf.Clamp(h,(float)shape.Bottom+.001f,(float)shape.Top),(float)p.Z);
                topCache.Add(key,result);return result;
            }
            // Clip the validated polygon triangulation to a regular sampling lattice.
            // Testing cell centers would overhang every oblique authoritative edge.
            double step=settings.rockSampleSpacing;
            List<NavPoint> Clip(List<NavPoint> ring,bool xAxis,double bound,bool greater)
            {
                var result=new List<NavPoint>();if(ring.Count==0)return result;
                double Coordinate(NavPoint p)=>xAxis?p.X:p.Z;
                var previous=ring[ring.Count-1];double pv=Coordinate(previous);bool pin=greater?pv>=bound:pv<=bound;
                foreach(var current in ring)
                {
                    double cv=Coordinate(current);bool cin=greater?cv>=bound:cv<=bound;
                    if(cin!=pin){double t=(bound-pv)/(cv-pv);result.Add(new NavPoint(xAxis?bound:previous.X+(current.X-previous.X)*t,xAxis?previous.Z+(current.Z-previous.Z)*t:bound));}
                    if(cin)result.Add(current);previous=current;pv=cv;pin=cin;
                }
                return result;
            }
            for(int t=0;t<polygon.Triangles.Count;t+=3)
            {
                var source=new[]{polygon.Vertices[polygon.Triangles[t]],polygon.Vertices[polygon.Triangles[t+1]],polygon.Vertices[polygon.Triangles[t+2]]};
                int minX=(int)Math.Floor(source.Min(p=>p.X)/step),maxX=(int)Math.Ceiling(source.Max(p=>p.X)/step);
                int minZ=(int)Math.Floor(source.Min(p=>p.Z)/step),maxZ=(int)Math.Ceiling(source.Max(p=>p.Z)/step);
                for(int x=minX;x<maxX;x++)for(int z=minZ;z<maxZ;z++)
                {
                    var ring=Clip(Clip(Clip(Clip(source.ToList(),true,x*step,true),true,(x+1)*step,false),false,z*step,true),false,(z+1)*step,false);
                    for(int i=1;i<ring.Count-1;i++)
                    {
                        var a=Top(ring[0]);var b=Top(ring[i+1]);var c=Top(ring[i]);
                        if(Vector3.Cross(b-a,c-a).y<1e-7f)continue;
                        foreach(var clipped in FoundryFissureMesh.Exclude(a,b,c,channels))
                        {
                            for(int j=0;j<3;j++)if(channels.Any(channel=>channel.BoundaryDistanceSquared(new NavPoint(clipped[j].x,clipped[j].z))<1e-9))clipped[j].y=(float)shape.Top;
                            Tri(clipped[0],clipped[1],clipped[2]);
                        }
                    }
                }
            }
            foreach(var channel in channels)
            {
                var ring=channel.Footprint.Vertices;
                for(int edge=0;edge<ring.Count;edge++)
                {
                    var a=ring[edge];var b=ring[(edge+1)%ring.Count];int steps=Math.Max(1,(int)Math.Ceiling(Math.Sqrt((b.X-a.X)*(b.X-a.X)+(b.Z-a.Z)*(b.Z-a.Z))/step));
                    float floor=(float)shape.Top-settings.lavaDepth-.005f;
                    for(int j=0;j<steps;j++)for(int layer=0;layer<4;layer++)
                    {
                        Vector3 P(float along,float level)=>new Vector3((float)(a.X+(b.X-a.X)*along),Mathf.Lerp(floor,(float)shape.Top,level),(float)(a.Z+(b.Z-a.Z)*along));
                        float u=(float)j/steps,v=(float)(j+1)/steps,lo=layer/4f,hi=(layer+1)/4f;
                        Tri(P(u,lo),P(v,lo),P(v,hi));Tri(P(u,lo),P(v,hi),P(u,hi));
                    }
                }
            }
            for(int edge=0;edge<polygon.Vertices.Count;edge++)
            {
                var a=polygon.Vertices[edge];var b=polygon.Vertices[(edge+1)%polygon.Vertices.Count];
                var knots=new SortedSet<double>{0,1};
                void Crossings(double lo,double hi)
                {
                    if(Math.Abs(hi-lo)<1e-10)return;
                    for(int i=(int)Math.Ceiling(Math.Min(lo,hi)/step);i*step<Math.Max(lo,hi);i++){double t=(i*step-lo)/(hi-lo);if(t>0&&t<1)knots.Add(t);}
                }
                Crossings(a.X,b.X);Crossings(a.Z,b.Z);
                foreach(var q in channels.SelectMany(c=>c.Footprint.Vertices))
                {
                    double dx=b.X-a.X,dz=b.Z-a.Z,t=((q.X-a.X)*dx+(q.Z-a.Z)*dz)/(dx*dx+dz*dz);
                    if(t>0&&t<1&&Math.Abs((q.X-a.X)*dz-(q.Z-a.Z)*dx)<1e-8)knots.Add(t);
                }
                var values=knots.ToArray();int layers=Mathf.Max(4,Mathf.CeilToInt((float)(shape.Top-shape.Bottom)/settings.strataSpacing));
                NavPoint At(double t)=>new NavPoint(a.X+(b.X-a.X)*t,a.Z+(b.Z-a.Z)*t);
                for(int j=0;j<values.Length-1;j++)
                {
                    bool outlet=channels.Any(c=>c.Contains(At((values[j]+values[j+1])/2)));
                    Vector3 Wall(double along,float level)
                    {
                        var q=At(along);var crown=Top(q);if(outlet)crown.y=(float)shape.Top-settings.lavaDepth-.005f;
                        var floor=new Vector3((float)q.X,Mathf.Min(crown.y,(float)(supportHeight?.Invoke(q)??shape.Bottom)),(float)q.Z);
                        return Vector3.Lerp(floor,crown,level);
                    }
                    for(int k=0;k<layers;k++)
                    {
                        float lo=(float)k/layers,hi=(float)(k+1)/layers;
                        Tri(Wall(values[j],lo),Wall(values[j],hi),Wall(values[j+1],hi));Tri(Wall(values[j],lo),Wall(values[j+1],hi),Wall(values[j+1],lo));
                    }
                }
            }
            int slabStart=vertices.Count;
            foreach(var slab in industrialDeck?Array.Empty<NavPolygon>():Blocks(shape,settings,lava))
            {
                float bottom=slab.Vertices.Min(p=>Top(p).y)-.15f;
                float upper=slab.Vertices.Max(p=>Top(p).y);
                float variation=Mathf.PerlinNoise((float)slab.MinX/settings.crownScale+73,(float)slab.MinZ/settings.crownScale+41);
                float top=Mathf.Min((float)shape.Top-.05f,upper+settings.blockHeight*(.65f+.35f*variation));
                if(top-bottom<.2f)continue;
                Vector3 P(NavPoint p,bool roof)=>new Vector3((float)p.X,roof?top:bottom,(float)p.Z);
                for(int i=0;i<slab.Triangles.Count;i+=3)Tri(P(slab.Vertices[slab.Triangles[i]],true),P(slab.Vertices[slab.Triangles[i+2]],true),P(slab.Vertices[slab.Triangles[i+1]],true));
                for(int i=0;i<slab.Vertices.Count;i++)
                {
                    var a=slab.Vertices[i];var b=slab.Vertices[(i+1)%slab.Vertices.Count];
                    Tri(P(a,false),P(a,true),P(b,true));Tri(P(a,false),P(b,true),P(b,false));
                }
            }
            var mesh=new Mesh{name="Foundry fractured layered basalt",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();
            var normals=mesh.normals;var sums=new Dictionary<Vector3,Vector3>();
            Vector3 Key(Vector3 p)=>new Vector3(Mathf.Round(p.x*10000),Mathf.Round(p.y*10000),Mathf.Round(p.z*10000));
            for(int i=0;i<vertices.Count;i++){var key=Key(vertices[i]);sums.TryGetValue(key,out var n);sums[key]=n+normals[i];}
            // The old path already smoothed coincident normals. Share exact duplicate
            // positions with those same normals: lossless topology/memory optimization.
            var lookup=new Dictionary<(Vector3,Vector3),int>();var compact=new List<Vector3>();var smooth=new List<Vector3>();
            for(int i=0;i<vertices.Count;i++)
            {
                var p=vertices[i];var normal=i>=slabStart?normals[i]:sums[Key(p)].normalized;var attribute=(p,normal);if(!lookup.TryGetValue(attribute,out int index)){index=compact.Count;lookup.Add(attribute,index);compact.Add(p);smooth.Add(normal);}
                triangles[i]=index;
            }
            mesh.Clear();mesh.indexFormat=compact.Count<=65535?UnityEngine.Rendering.IndexFormat.UInt16:UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(compact);mesh.SetTriangles(triangles,0);mesh.SetNormals(smooth);mesh.RecalculateBounds();return mesh;
        }
    }
}
