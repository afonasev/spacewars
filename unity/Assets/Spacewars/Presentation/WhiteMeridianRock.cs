using System;
using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private static float MeridianEdgeDistance(NavPolygon polygon,float x,float z)
        {
            float nearest=float.MaxValue;
            for(int i=0;i<polygon.Vertices.Count;i++){
                var a=polygon.Vertices[i];var b=polygon.Vertices[(i+1)%polygon.Vertices.Count];var v=new Vector2((float)(b.X-a.X),(float)(b.Z-a.Z));var w=new Vector2(x-(float)a.X,z-(float)a.Z);
                float t=Mathf.Clamp01(Vector2.Dot(w,v)/v.sqrMagnitude);nearest=Mathf.Min(nearest,(w-v*t).magnitude);
            }
            return nearest;
        }
        private static IEnumerable<NavObstacle> MeridianRockGroups(WhiteMeridianMap map)
        {
            var groups=new List<NavObstacle>(map.Ridges);
            for(int i=0;i<groups.Count;i++)for(int j=i+1;j<groups.Count;j++){
                var a=groups[i];var b=groups[j];
                if(a.Top!=b.Top||a.MaxX<=b.MinX||b.MaxX<=a.MinX||a.MaxZ<=b.MinZ||b.MaxZ<=a.MinZ)continue;
                var joined=MeridianJoinedFootprint(a.Footprint,b.Footprint);
                if(joined==null)continue;
                groups[i]=new NavObstacle(joined,0,a.Top);groups.RemoveAt(j);j=i;
            }
            foreach(var group in groups)yield return group;
            foreach(var border in map.BorderRocks)yield return border;
        }
        private static NavPolygon MeridianJoinedFootprint(NavPolygon a,NavPolygon b)
        {
            // Extract the union perimeter from authored edges, splitting at crossings.
            // No convex hull: that would fill concave passage mouths outside the solid.
            const double epsilon=1e-7; // Geometric joining tolerance, in world metres.
            var edges=new List<(NavPoint from,NavPoint to)>();int crossings=0;
            double Cross(NavPoint p,NavPoint q)=>p.X*q.Z-p.Z*q.X;
            NavPoint Sub(NavPoint p,NavPoint q)=>new NavPoint(p.X-q.X,p.Z-q.Z);
            NavPoint Lerp(NavPoint p,NavPoint q,double t)=>new NavPoint(p.X+(q.X-p.X)*t,p.Z+(q.Z-p.Z)*t);
            bool Same(NavPoint p,NavPoint q)=>Math.Abs(p.X-q.X)<epsilon&&Math.Abs(p.Z-q.Z)<epsilon;
            void Boundary(NavPolygon source,NavPolygon other){
                for(int i=0;i<source.Vertices.Count;i++){
                    var p=source.Vertices[i];var q=source.Vertices[(i+1)%source.Vertices.Count];var direction=Sub(q,p);
                    var cuts=new List<double>{0,1};
                    for(int j=0;j<other.Vertices.Count;j++){
                        var c=other.Vertices[j];var d=other.Vertices[(j+1)%other.Vertices.Count];var delta=Sub(d,c);double denominator=Cross(direction,delta);
                        if(Math.Abs(denominator)<epsilon)continue;
                        double t=Cross(Sub(c,p),delta)/denominator,u=Cross(Sub(c,p),direction)/denominator;
                        if(t>epsilon&&t<1-epsilon&&u>=-epsilon&&u<=1+epsilon){cuts.Add(t);crossings++;}
                    }
                    cuts.Sort();
                    for(int k=1;k<cuts.Count;k++)if(cuts[k]-cuts[k-1]>epsilon&&!other.Contains(Lerp(p,q,(cuts[k-1]+cuts[k])*.5)))
                        edges.Add((Lerp(p,q,cuts[k-1]),Lerp(p,q,cuts[k])));
                }
            }
            Boundary(a,b);Boundary(b,a);if(crossings==0)return null;
            var perimeter=new List<NavPoint>();var first=edges[0].from;var next=first;
            while(edges.Count>0){
                int index=edges.FindIndex(e=>Same(e.from,next));
                if(index<0)throw new InvalidOperationException("Meridian rock union has an open perimeter");
                var edge=edges[index];perimeter.Add(edge.from);next=edge.to;edges.RemoveAt(index);
                if(Same(next,first))break;
            }
            if(edges.Count!=0)throw new InvalidOperationException("Meridian rock union has multiple perimeters");
            return new NavPolygon(perimeter);
        }
        private static Vector2 MeridianRockInset(NavPolygon polygon,Vector2 q,float amount)
        {
            // Inward boundary normals, including both edges at a corner. Clamping
            // the entire inset segment keeps narrow concave notches inside the solid.
            float nearest=float.MaxValue;var normal=Vector2.zero;
            for(int i=0;i<polygon.Vertices.Count;i++){
                var a=polygon.Vertices[i];var b=polygon.Vertices[(i+1)%polygon.Vertices.Count];
                var start=new Vector2((float)a.X,(float)a.Z);var delta=new Vector2((float)(b.X-a.X),(float)(b.Z-a.Z));
                var point=start+delta*Mathf.Clamp01(Vector2.Dot(q-start,delta)/delta.sqrMagnitude);
                float distance=(q-point).sqrMagnitude;var inward=new Vector2(-delta.y,delta.x).normalized;
                if(distance<nearest-1e-6f){nearest=distance;normal=inward;}
                else if(Mathf.Abs(distance-nearest)<1e-6f)normal+=inward;
            }
            normal.Normalize();var candidate=q+normal*amount;
            for(int attempt=0;attempt<12;attempt++){
                bool inside=true;
                for(int k=1;k<=4;k++){
                    var v=Vector2.Lerp(q,candidate,k*.25f);
                    if(!polygon.Contains(new NavPoint(v.x,v.y))){inside=false;break;}
                }
                if(inside)return candidate;
                candidate=Vector2.Lerp(q,candidate,.5f);
            }
            return q;
        }
        private static void MeridianRefineFacets(List<Vector3> vertices,List<int> triangles)
        {
            // Ear clipping preserves concave boundaries but creates long needle
            // triangles. Delaunay flips improve only interior roof diagonals; no
            // vertex, perimeter or physical footprint is moved by this operation.
            double Cross(Vector3 a,Vector3 b,Vector3 c)=>(double)(b.x-a.x)*(c.z-a.z)-(double)(b.z-a.z)*(c.x-a.x);
            void Tri(int offset,int a,int b,int c){
                if(Cross(vertices[a],vertices[b],vertices[c])>0){int swap=b;b=c;c=swap;}
                triangles[offset]=a;triangles[offset+1]=b;triangles[offset+2]=c;
            }
            for(int pass=0;pass<triangles.Count;pass++){
                var edges=new Dictionary<(int,int),(int triangle,int opposite)>();
                var used=new HashSet<int>();int flips=0;
                for(int t=0;t<triangles.Count;t+=3)for(int k=0;k<3;k++){
                    int a=triangles[t+k],b=triangles[t+(k+1)%3],c=triangles[t+(k+2)%3];var key=a<b?(a,b):(b,a);
                    if(!edges.TryGetValue(key,out var other)){edges.Add(key,(t,c));continue;}
                    if(used.Contains(t)||used.Contains(other.triangle))continue;
                    int d=other.opposite;var va=vertices[a];var vb=vertices[b];var vc=vertices[c];var vd=vertices[d];
                    if(Cross(va,vb,vc)*Cross(va,vb,vd)>=-1e-10||Cross(vc,vd,va)*Cross(vc,vd,vb)>=-1e-10)continue;
                    double ax=va.x-vd.x,az=va.z-vd.z,bx=vb.x-vd.x,bz=vb.z-vd.z,cx=vc.x-vd.x,cz=vc.z-vd.z;
                    double circle=(ax*ax+az*az)*(bx*cz-bz*cx)-(bx*bx+bz*bz)*(ax*cz-az*cx)+(cx*cx+cz*cz)*(ax*bz-az*bx);
                    if(circle*Cross(va,vb,vc)<=1e-8)continue;
                    Tri(t,c,d,a);Tri(other.triangle,d,c,b);used.Add(t);used.Add(other.triangle);flips++;
                }
                if(flips==0)return;
            }
            throw new InvalidOperationException("Meridian roof facet refinement did not converge");
        }
        private static Mesh MeridianRock(NavObstacle rock,WhiteMeridianMap map,WhiteMeridianArtProfile art)
        {
            // One continuous roof and only perimeter walls. Triangulate the authored
            // concave footprint itself: centroid insets can cross its narrow notches.
            // Rendering only; the physical solid, supports and passages stay intact.
            var poly=rock.Footprint;var vertices=new List<Vector3>();var triangles=new List<int>();
            var roofVertices=new Dictionary<Vector2,int>();
            NavPolygon crownPoly=null;
            float inset=art.rockFaceInset;
            for(int attempt=0;attempt<12&&crownPoly==null;attempt++){
                var points=new List<NavPoint>();
                foreach(var p in poly.Vertices){
                    var q=MeridianRockInset(poly,new Vector2((float)p.X,(float)p.Z),inset);
                    points.Add(new NavPoint(q.x,q.y));
                }
                try {
                    var candidate=new NavPolygon(points);bool inside=true;
                    for(int i=0;i<points.Count;i++)for(int k=0;k<=16;k++){
                        var a=points[i];var b=points[(i+1)%points.Count];float t=k/16f;
                        if(!poly.Contains(new NavPoint(a.X+(b.X-a.X)*t,a.Z+(b.Z-a.Z)*t)))inside=false;
                    }
                    if(inside)crownPoly=candidate;
                }catch(ArgumentException){ /* Narrow concavities require a smaller shared inset. */ }
                if(crownPoly==null)inset*=.5f;
            }
            if(crownPoly==null)crownPoly=poly;
            float baseHeight=float.MaxValue;
            foreach(var p in poly.Vertices)baseHeight=Mathf.Min(baseHeight,(float)map.SurfaceHeight(p));
            float ceiling=baseHeight+((float)rock.Top-baseHeight)*art.rockHeightRatio;
            Vector3 Roof(Vector2 q){
                float weather=Mathf.PerlinNoise(q.x*.17f+13,q.y*.17f+29);
                float edge=MeridianEdgeDistance(crownPoly,q.x,q.y);
                float bevel=1-Mathf.SmoothStep(0,1,edge/art.rockShoulder);
                float height=ceiling-art.rockCrownVariation*(.3f+.7f*weather)-art.rockRimDrop*bevel;
                // A lowered ridge must still emerge above any adjacent raised shelf.
                float floor=(float)map.SurfaceHeight(new NavPoint(q.x,q.y));
                return new Vector3(q.x,Mathf.Max(floor+.15f,height),q.y);
            }
            int RoofIndex(Vector2 q){
                if(roofVertices.TryGetValue(q,out int index))return index;
                index=vertices.Count;vertices.Add(Roof(q));roofVertices.Add(q,index);return index;
            }
            void RoofTri(Vector2 a,Vector2 b,Vector2 c,int depth){
                if(depth>0){
                    var ab=(a+b)*.5f;var bc=(b+c)*.5f;var ca=(c+a)*.5f;
                    RoofTri(a,ab,ca,depth-1);RoofTri(ab,b,bc,depth-1);
                    RoofTri(ca,bc,c,depth-1);RoofTri(ab,bc,ca,depth-1);return;
                }
                if(Vector2.SignedAngle(b-a,c-a)>0){var swap=b;b=c;c=swap;}
                triangles.Add(RoofIndex(a));triangles.Add(RoofIndex(b));triangles.Add(RoofIndex(c));
            }
            Vector2 Point(int i)=>new Vector2((float)crownPoly.Vertices[i].X,(float)crownPoly.Vertices[i].Z);
            // Shared subdivisions preserve the joined roof. Sparse, flat-shaded
            // crown facets follow the profile's broad mineral split scale.
            float longest=0;
            for(int i=0;i<crownPoly.Triangles.Count;i+=3)for(int k=0;k<3;k++)
                longest=Mathf.Max(longest,Vector2.Distance(Point(crownPoly.Triangles[i+k]),Point(crownPoly.Triangles[i+(k+1)%3])));
            int depth=0;while(longest>art.splitSpacing){longest*=.5f;depth++;}
            for(int i=0;i<crownPoly.Triangles.Count;i+=3)RoofTri(Point(crownPoly.Triangles[i]),Point(crownPoly.Triangles[i+1]),Point(crownPoly.Triangles[i+2]),depth);
            MeridianRefineFacets(vertices,triangles);
            void WallTri(Vector3 a,Vector3 b,Vector3 c){
                int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);
                triangles.Add(i);triangles.Add(i+1);triangles.Add(i+2);
            }
            for(int edge=0;edge<poly.Vertices.Count;edge++){
                var a=Point(edge);var b=Point((edge+1)%poly.Vertices.Count);
                var outerA=new Vector2((float)poly.Vertices[edge].X,(float)poly.Vertices[edge].Z);
                var outerB=new Vector2((float)poly.Vertices[(edge+1)%poly.Vertices.Count].X,(float)poly.Vertices[(edge+1)%poly.Vertices.Count].Z);
                // Match the roof boundary tessellation exactly; no perimeter cracks.
                int count=1<<depth;
                for(int k=0;k<count;k++){
                    var q=Vector2.Lerp(a,b,(float)k/count);var r=Vector2.Lerp(a,b,(float)(k+1)/count);
                    var qa=Roof(q);var rb=Roof(r);
                    var footA=Vector2.Lerp(outerA,outerB,(float)k/count);var footB=Vector2.Lerp(outerA,outerB,(float)(k+1)/count);
                    var lowA=new Vector3(footA.x,baseHeight-.12f,footA.y);var lowB=new Vector3(footB.x,baseHeight-.12f,footB.y);
                    float weatherA=Mathf.PerlinNoise(q.x*.31f+17,q.y*.31f+7);
                    float weatherB=Mathf.PerlinNoise(r.x*.31f+17,r.y*.31f+7);
                    var faceA=Vector2.Lerp(footA,q,weatherA*.45f);
                    var faceB=Vector2.Lerp(footB,r,weatherB*.45f);
                    var midA=new Vector3(faceA.x,Mathf.Lerp(baseHeight,qa.y,.55f+.12f*weatherA),faceA.y);
                    var midB=new Vector3(faceB.x,Mathf.Lerp(baseHeight,rb.y,.55f+.12f*weatherB),faceB.y);
                    WallTri(lowA,midA,midB);WallTri(lowA,midB,lowB);
                    WallTri(midA,qa,rb);WallTri(midA,rb,midB);
                }
            }
            var mesh=new Mesh{name="Meridian continuous low gypsum",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
            // Separate shading vertices reveal the broad mineral planes without
            // separating their geometry or cutting fissures through the massif.
            var faceted=new List<Vector3>(triangles.Count);
            for(int i=0;i<triangles.Count;i++){faceted.Add(vertices[triangles[i]]);triangles[i]=i;}
            mesh.SetVertices(faceted);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        // A single height envelope per allied side joins shelf, ramps and toes before
        // triangulation. Independent curtain walls cannot meet without knife edges.
        // This is presentation only: no MapSupport, blocker or collider is authored here.
        private static Mesh MeridianConnectedTerrain(WhiteMeridianMap map,WhiteMeridianArtProfile art,int side)
        {
            var supports=new List<MapSupport>();
            foreach(var support in map.Supports)
                if(support.Id!="salt-basin"&&(support.Bounds.MinX+support.Bounds.MaxX)*side>0)supports.Add(support);
            // Fixed mesh sampling is an implementation quality bound, not a gameplay control.
            const float step=.16f;
            float margin=art.cliffShoulder+art.footApron+art.connectionRound*3+art.rimWander;
            float minX=float.MaxValue,maxX=float.MinValue,minZ=float.MaxValue,maxZ=float.MinValue;
            foreach(var support in supports){minX=Mathf.Min(minX,(float)support.Bounds.MinX);maxX=Mathf.Max(maxX,(float)support.Bounds.MaxX);minZ=Mathf.Min(minZ,(float)support.Bounds.MinZ);maxZ=Mathf.Max(maxZ,(float)support.Bounds.MaxZ);}
            minX=Mathf.Floor((minX-margin)/step)*step;maxX=Mathf.Ceil((maxX+margin)/step)*step;
            minZ=Mathf.Floor((minZ-margin)/step)*step;maxZ=Mathf.Ceil((maxZ+margin)/step)*step;
            int nx=Mathf.RoundToInt((maxX-minX)/step)+1,nz=Mathf.RoundToInt((maxZ-minZ)/step)+1;
            var heights=new float[nx*nz];
            float Height(float x,float z){
                float height=0;
                foreach(var support in supports){
                    var b=support.Bounds;
                    if(x<b.MinX-margin||x>b.MaxX+margin||z<b.MinZ-margin||z>b.MaxZ+margin)continue;
                    var point=new NavPoint(x,z);
                    if(support.Contains(point)){
                        float h=(float)support.HeightAt(point);
                        if(support.Id.EndsWith("ramp")){
                            float u=Mathf.InverseLerp((float)b.MinX,(float)b.MaxX,x),v=Mathf.InverseLerp((float)b.MinZ,(float)b.MaxZ,z);
                            h+=art.rampRelief*Mathf.Sin(u*Mathf.PI)*Mathf.Sin(v*Mathf.PI)*(Mathf.PerlinNoise(x*.7f+9,z*.7f+31)*2-1);
                        }
                        height=Mathf.Max(height,h);continue;
                    }
                    // Euclidean distance rounds convex corners at every contour level.
                    // Use the nearest boundary height, so a ramp toe dies into the basin
                    // continuously instead of collapsing the width of a separate wall.
                    var polygon=b.Footprint;float distance=float.MaxValue;var nearest=Vector2.zero;
                    for(int i=0;i<polygon.Vertices.Count;i++){
                        var a=polygon.Vertices[i];var c=polygon.Vertices[(i+1)%polygon.Vertices.Count];
                        var start=new Vector2((float)a.X,(float)a.Z);var delta=new Vector2((float)(c.X-a.X),(float)(c.Z-a.Z));
                        var q=start+delta*Mathf.Clamp01(Vector2.Dot(new Vector2(x,z)-start,delta)/delta.sqrMagnitude);
                        float d=(new Vector2(x,z)-q).sqrMagnitude;if(d<distance){distance=d;nearest=q;}
                    }
                    distance=Mathf.Sqrt(distance);
                    float top=Mathf.Max(0,(float)support.HeightAt(new NavPoint(nearest.x,nearest.y)));
                    float weather=Mathf.PerlinNoise(x*.38f+43,z*.38f+19);
                    float width=Mathf.Max(.01f,art.cliffShoulder+art.rimWander*weather); // Numeric floor for the zero-width art preset.
                    if(!support.Id.EndsWith("ramp")){
                        // Narrow the RAW shelf collar beside a physical ramp, before
                        // filtering. Clipping the finished mesh instead would create a
                        // sharp trough/lighting scar where the constraint ends.
                        foreach(var ramp in supports){
                            if(!ramp.Id.EndsWith("ramp"))continue;
                            float qx=Mathf.Clamp(x,(float)ramp.Bounds.MinX,(float)ramp.Bounds.MaxX),qz=Mathf.Clamp(z,(float)ramp.Bounds.MinZ,(float)ramp.Bounds.MaxZ);
                            float proximity=1-Mathf.SmoothStep(0,1,new Vector2(x-qx,z-qz).magnitude/(art.cliffShoulder+art.connectionRound*2));
                            // Reserve the filter's full finite radius inside the side
                            // envelope. The additional grid-cell width is audited.
                            float narrow=Mathf.Max(.01f,art.rampEdgeInset-art.connectionRound*2);
                            width=Mathf.Lerp(width,Mathf.Min(width,narrow),proximity);
                        }
                    }
                    float t=Mathf.Clamp01(distance/width);
                    // Quintic section has horizontal tangency at both the lip and foot,
                    // with a steep gypsum face between them; it is not a drivable slope.
                    float section=1-t*t*t*(t*(t*6-15)+10);
                    float face=top*section+Mathf.Min(top,art.cliffErosion)*section*(1-section)*(weather-.5f);
                    float apronT=Mathf.Clamp01(distance/(width+art.footApron));
                    float apron=art.footRelief*Mathf.Min(1,width/Mathf.Max(.01f,art.cliffShoulder))*(.35f+.65f*weather)*Mathf.Min(1,top)*(1-apronT)*(1-apronT);
                    height=Mathf.Max(height,Mathf.Max(face,apron));
                }
                return height;
            }
            for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)heights[z*nx+x]=Height(minX+x*step,minZ+z*step);
            // Filter the UNION, not individual pieces. This rounds the ramp/plateau
            // saddle and removes max-envelope creases without overlapping seam meshes.
            // Linear ramp interiors and flat supports are invariant under this filter.
            int radius=Mathf.CeilToInt(art.connectionRound*2/step);var weights=new float[radius*2+1];float total=0;
            for(int k=-radius;k<=radius;k++){float v=k*step/art.connectionRound;weights[k+radius]=Mathf.Exp(-.5f*v*v);total+=weights[k+radius];}
            for(int k=0;k<weights.Length;k++)weights[k]/=total;
            var scratch=new float[heights.Length];
            for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)for(int k=-radius;k<=radius;k++)scratch[z*nx+x]+=heights[z*nx+Mathf.Clamp(x+k,0,nx-1)]*weights[k+radius];
            Array.Clear(heights,0,heights.Length);
            for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)for(int k=-radius;k<=radius;k++)heights[z*nx+x]+=scratch[Mathf.Clamp(z+k,0,nz-1)*nx+x]*weights[k+radius];
            var vertices=new List<Vector3>(heights.Length);var normals=new List<Vector3>(heights.Length);var triangles=new List<int>();
            for(int z=0;z<nz;z++)for(int x=0;x<nx;x++){
                float h=heights[z*nx+x];
                // Buried only at zero-height basin overlap; no coplanar double surface.
                vertices.Add(new Vector3(minX+x*step,h-.012f*Mathf.Clamp01(1-h/.02f),minZ+z*step));
                float dx=heights[z*nx+Mathf.Max(0,x-1)]-heights[z*nx+Mathf.Min(nx-1,x+1)];
                float dz=heights[Mathf.Max(0,z-1)*nx+x]-heights[Mathf.Min(nz-1,z+1)*nx+x];
                normals.Add(new Vector3(dx,2*step,dz).normalized);
            }
            for(int z=0;z<nz-1;z++)for(int x=0;x<nx-1;x++){
                int i=z*nx+x;if(Mathf.Max(Mathf.Max(heights[i],heights[i+1]),Mathf.Max(heights[i+nx],heights[i+nx+1]))<.001f)continue;
                triangles.Add(i);triangles.Add(i+nx);triangles.Add(i+1);triangles.Add(i+1);triangles.Add(i+nx);triangles.Add(i+nx+1);
            }
            var mesh=new Mesh{name="Meridian continuous geology "+side,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();return mesh;
        }
    }
}
