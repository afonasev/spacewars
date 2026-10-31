using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        // Fixed dimensions below describe authored mechanical assemblies, not navigation or balance.
        // Everything is either inside an existing solid, below/along its edge, or flush to the deck.
        private static readonly Color DockArmor=new Color(.36f,.40f,.43f),DockEdge=new Color(.50f,.53f,.53f),DockOchre=new Color(.63f,.37f,.11f);

        private static void DockExteriorHull(DockBatch b,NullDockMap map,NullDockArtProfile art)
        {
            var vs=map.Supports.Single(s=>s.Id=="station-deck").Bounds.Footprint.Vertices;
            for(int i=0;i<vs.Count;i++){
                var a=V(vs[i],0);var z=V(vs[(i+1)%vs.Count],0);var along=(z-a).normalized;
                var outward=new Vector3(along.z,0,-along.x);var middle=(a+z)*.5f;
                if(map.SupportAt(new NavPoint(middle.x+outward.x,middle.z+outward.z))!=null)outward=-outward;
                float length=Vector3.Distance(a,z);var q=Quaternion.LookRotation(outward);
                // Repeating construction bays follow the outline, including recessed pockets.
                for(float d=0;d<length;d+=art.baySpacing){
                    float width=Mathf.Min(art.baySpacing,length-d);var at=a+along*(d+width*.5f);
                    float h=(float)map.SurfaceHeight(new NavPoint(at.x-outward.x*.12f,at.z-outward.z*.12f));
                    void Box(Vector3 p,Vector3 size,Color c,int role=4)=>b.Box(at+q*p,size,c,role,q);
                    // Deep layered chassis and segmented upper gunwale; no interior blocking decoration.
                    Box(new Vector3(0,(h-art.hullDepth)*.5f,1.25f),new Vector3(width-.12f,h+art.hullDepth,3.80f),DarkSteel,2);
                    Box(new Vector3(0,h-.12f,1.10f),new Vector3(width-.24f,1.0f,3.60f),DockArmor);
                    DockBeveledBox(b,at+q*new Vector3(0,h+.45f,1.05f),new Vector3(width-.32f,.70f,3.50f),.18f,DockEdge,q);
                    Box(new Vector3(0,h+.95f,2.45f),new Vector3(width-.5f,.20f,.3f),DarkSteel);
                    Box(new Vector3(0,h-.92f,1.04f),new Vector3(width*.66f,.60f,.20f),new Color(.055f,.075f,.09f));
                    Box(new Vector3(0,h-.91f,1.16f),new Vector3(width*.50f,.20f,.07f),Cyan,5);
                    Box(new Vector3(0,h-1.45f,.91f),new Vector3(width-.3f,.22f,.40f),DockOchre);
                    Box(new Vector3(0,-art.hullDepth+.28f,.55f),new Vector3(width-.15f,.42f,1.40f),DockArmor);
                    for(int side=-1;side<=1;side+=2){
                        float x=side*(width*.5f-.34f);
                        Box(new Vector3(x,(h-art.hullDepth)*.5f,1.02f),new Vector3(art.ribWidth,h+art.hullDepth+.6f,.65f),DockEdge);
                        DockBeveledBox(b,at+q*new Vector3(x,h+.55f,1.05f),new Vector3(1.15f,1.40f,4.05f),.20f,DockArmor,q);
                        Box(new Vector3(x,h+1.31f,1.05f),new Vector3(.50f,.10f,.65f),DockOchre);
                        Box(new Vector3(x,h+1.38f,1.05f),new Vector3(.23f,.08f,.25f),Cyan,5);
                        for(float yy=-art.hullDepth+.7f;yy<h-.8f;yy+=1.0f)
                            Box(new Vector3(x,yy,1.39f),new Vector3(.2f,.14f,.06f),new Color(.12f,.14f,.15f));
                    }
                    for(int pipe=0;pipe<2;pipe++){
                        var from=at+q*new Vector3(-width*.44f,h-.25f-pipe*.25f,1.25f);var to=at+q*new Vector3(width*.44f,h-.25f-pipe*.25f,1.25f);
                        b.Cylinder(from,to,.09f,pipe==0?Copper:Steel,4);
                        for(int clip=-1;clip<=1;clip++)Box(new Vector3(clip*width*.28f,h-.38f,1.29f),new Vector3(.18f,.56f,.22f),DarkSteel);
                    }
                    // Top service hardware gives depth in the normal RTS camera.
                    Box(new Vector3(0,h+.84f,1.6f),new Vector3(width*.53f,.12f,1.1f),DarkSteel);
                    Box(new Vector3(0,h+.85f,.55f),new Vector3(width*.60f,.08f,.48f),Cyan,5);
                    for(float x=-width*.22f;x<width*.23f;x+=.4f)Box(new Vector3(x,h+.92f,1.6f),new Vector3(.14f,.08f,1.05f),DockArmor);
                }
            }
        }

        private static void DockPlatformFascia(DockBatch b,NullDockMap map,MapSupport support,NullDockArtProfile art)
        {
            var vs=support.Bounds.Footprint.Vertices;float h=(float)support.Height;
            for(int i=0;i<vs.Count;i++){
                var a=V(vs[i],h);var end=V(vs[(i+1)%vs.Count],h);var along=(end-a).normalized;float len=Vector3.Distance(a,end);
                var outward=new Vector3(along.z,0,-along.x);var mid=(a+end)*.5f;
                if(support.Contains(new NavPoint(mid.x+outward.x,mid.z+outward.z)))outward=-outward;
                var q=Quaternion.LookRotation(outward);
                // One-metre sections fit the authored ramp boundaries exactly, never bridging entrances.
                for(float d=.5f;d<len;d+=1){var at=a+along*d;
                    var outside=map.SupportAt(new NavPoint(at.x+outward.x*.45f,at.z+outward.z*.45f));
                    if(outside==null||outside.Id.StartsWith("R")||outside.Height>=h||map.Modules.Any(m=>m.Contains(new NavPoint(at.x+outward.x*.7f,at.z+outward.z*.7f))))continue;
                    void Box(Vector3 p,Vector3 size,Color c,int role=4)=>b.Box(at+q*p,size,c,role,q);
                    Box(new Vector3(0,-h*.48f,.12f),new Vector3(.98f,h*.92f,.32f),DarkSteel);
                    Box(new Vector3(0,-.16f,-.05f),new Vector3(.98f,.24f,.70f),DockEdge);
                    Box(new Vector3(0,.10f,-.52f),new Vector3(.98f,.20f,1.2f),DockArmor);
                    if((int)d%6>0&&(int)d%6<5)Box(new Vector3(0,.22f,-.44f),new Vector3(.85f,.035f,.40f),Cyan,5);
                    Box(new Vector3(0,-h*.80f,.23f),new Vector3(.98f,.15f,.35f),DockOchre);
                    int phase=(int)d%6;
                    if(phase==0||phase==5){
                        Box(new Vector3(0,-h*.4f,.32f),new Vector3(.40f,h*.85f,.6f),DockEdge);
                        Box(new Vector3(0,.28f,-.05f),new Vector3(.55f,.40f,.75f),DockArmor);
                        Box(new Vector3(0,.50f,-.05f),new Vector3(.35f,.05f,.38f),DockOchre);
                    }else{
                        Box(new Vector3(0,-h*.44f,.32f),new Vector3(.94f,h*.36f,.08f),new Color(.035f,.06f,.075f));
                        Box(new Vector3(0,-h*.43f,.38f),new Vector3(.85f,Mathf.Min(.42f,h*.24f),.05f),Cyan,5);
                        Box(new Vector3(0,-h*.24f,.38f),new Vector3(.87f,.07f,.09f),Steel);
                    }
                    b.Cylinder(at+q*new Vector3(-.5f,-h*.65f,.4f),at+q*new Vector3(.5f,-h*.65f,.4f),.07f,Copper,4);
                }
            }
        }

        private static void DockDetailedModule(DockBatch b,NavObstacle solid,int index,float height,NullDockArtProfile art)
        {
            var points=solid.Footprint.Vertices;
            var center=new Vector3((float)(solid.MinX+solid.MaxX)*.5f,0,(float)(solid.MinZ+solid.MaxZ)*.5f);
            // Local assembly heights are authored against a unit module; profile scale keeps every part inside the solid.
            b.LocalTransform=Matrix4x4.Scale(new Vector3(1,height/6f,1));
            NavObstacle Inset(float k)=>new NavObstacle(new NavPolygon(points.Select(p=>new NavPoint(center.x+(p.X-center.x)*k,center.z+(p.Z-center.z)*k))));
            b.Add(TerrainMesh.Prism(solid,0,.38),new Color(.08f,.11f,.13f),4);
            b.Add(TerrainMesh.Prism(Inset(.97f),.38,3.85),DockArmor,4);
            DockModuleShoulder(b,points,center);
            b.Add(TerrainMesh.Prism(Inset(.99f),.52,.78),DockOchre,4);
            b.Add(TerrainMesh.Prism(Inset(.85f),4.48,4.58),new Color(.08f,.12f,.15f),4);
            b.Add(TerrainMesh.Prism(Inset(.84f),4.48,4.64),new Color(.29f,.33f,.35f),4);
            for(int e=0;e<points.Count;e++){
                Vector3 a=V(points[e],0),z=V(points[(e+1)%points.Count],0),along=(z-a).normalized;
                var inward=(center-(a+z)*.5f);inward.y=0;inward.Normalize();
                var faceNormal=new Vector3(along.z,0,-along.x);if(Vector3.Dot(faceNormal,inward)>0)faceNormal=-faceNormal;
                var q=Quaternion.LookRotation(faceNormal);float len=Vector3.Distance(a,z);
                // Bevelled sloping shoulders, bright lip and recessed horizontal conduits.
                b.Beam(a+inward*.26f+Vector3.up*3.78f,z+inward*.26f+Vector3.up*3.78f,.40f,.30f,DockEdge,4);
                b.Beam(a+inward*.28f+Vector3.up*1.02f,z+inward*.28f+Vector3.up*1.02f,.28f,.25f,DarkSteel,4);
                for(float d=1;d<len-1;d+=3.2f){var at=a+along*d+inward*.62f;
                    void Box(Vector3 p,Vector3 s,Color c,int role=4)=>b.Box(at+q*p,s,c,role,q);
                    Box(new Vector3(0,2.45f,-.05f),new Vector3(2.55f,2.65f,.18f),new Color(.075f,.11f,.14f));
                    Box(new Vector3(0,3.48f,.04f),new Vector3(2.7f,.16f,.33f),Steel);
                    Box(new Vector3(0,1.2f,.04f),new Vector3(2.7f,.18f,.4f),DockEdge);
                    Box(new Vector3(-1.3f,2.40f,.08f),new Vector3(.24f,2.65f,.42f),DockEdge);
                    if((int)(d/3.2f)%3==1){
                        for(int slat=0;slat<7;slat++)Box(new Vector3(0,1.6f+slat*.26f,.04f),new Vector3(2.15f,.12f,.22f),DockArmor);
                    }else{
                        Box(new Vector3(0,2.6f,.08f),new Vector3(2.10f,1.5f,.18f),DockArmor);
                        Box(new Vector3(0,1.65f,.17f),new Vector3(1.82f,.36f,.06f),Cyan,5);
                        Box(new Vector3(0,3.60f,-.24f),new Vector3(1.6f,.06f,.38f),Cyan,5);
                        Box(new Vector3(0,2.62f,.19f),new Vector3(1.5f,.08f,.03f),DarkSteel);
                        Box(new Vector3(.7f,2.9f,.28f),new Vector3(.11f,.35f,.09f),Steel);
                        Box(new Vector3(-.75f,3.0f,.22f),new Vector3(.30f,.13f,.03f),DockOchre);
                        for(int stud=-1;stud<=1;stud+=2)Box(new Vector3(stud*.84f,2.0f,.25f),new Vector3(.11f,.13f,.07f),Steel);
                    }
                    Box(new Vector3(-.96f,1.12f,.22f),new Vector3(.32f,.30f,.12f),DockOchre);
                    // Sloping armor knee inside the footprint.
                    b.Beam(at+q*new Vector3(1.2f,.6f,.1f),at+q*new Vector3(1.2f,3.9f,-.9f),.32f,.42f,DockArmor,4);
                }
            }
            if(index==2){
                var q=Quaternion.Euler(0,26.565f,0);
                for(int k=-2;k<=2;k++)DockRoofAssembly(b,center+q*new Vector3(0,4.64f,k*5.2f),q,k%2==0?0:1,4.1f,4.4f);
                for(int side=-1;side<=1;side+=2){
                    var a=center+q*new Vector3(side*2.65f,4.86f,-13.8f);var z=center+q*new Vector3(side*2.65f,4.86f,13.8f);
                    b.Cylinder(a,z,art.pipeRadius*.75f,side<0?Copper:Steel,4);
                    for(int k=-3;k<=3;k++)b.Box(center+q*new Vector3(side*2.65f,4.9f,k*4),new Vector3(.65f,.48f,.24f),DarkSteel,4,q);
                }
            }else if(index<2){
                // An axial heat-exchanger bank, twin fan units and conduits make a coherent machine,
                // instead of repeating a tiled assortment of unrelated roof boxes.
                var q=Quaternion.Euler(0,index==0?135:-45,0);
                DockRoofAssembly(b,center+q*new Vector3(-3.5f,4.64f,0),q,1,3.5f,14);
                for(int k=-1;k<=1;k++)DockRoofAssembly(b,center+q*new Vector3(1.1f,4.64f,k*5),q,0,4.6f,4.6f);
                for(int end=-1;end<=1;end+=2)DockRoofAssembly(b,center+q*new Vector3(0,4.64f,end*9),q,2,7.0f,2.8f);
                for(int pipe=0;pipe<3;pipe++){
                    float x=4.1f+pipe*.52f;
                    b.Cylinder(center+q*new Vector3(x,5.05f,-7),center+q*new Vector3(x,5.05f,7),.14f,pipe==1?Copper:Steel,4);
                    for(int k=-2;k<=2;k++)b.Box(center+q*new Vector3(x,5.07f,k*3),new Vector3(.38f,.42f,.16f),DarkSteel,4,q);
                }
            }else{
                int n=0;float step=Mathf.Max(4.8f,art.equipmentSpacing*1.5f);
                for(double x=solid.MinX+3.3;x<solid.MaxX-2.7;x+=step)for(double z=solid.MinZ+3.3;z<solid.MaxZ-2.7;z+=step){
                    // Test full box corners as well as center; decorative roof equipment never crosses a bevel.
                    var p=new NavPoint(x,z);if(!solid.Contains(p)||solid.BoundaryDistanceSquared(p)<3.0*3.0)continue;
                    bool fits=true;foreach(int xx in new[]{-1,1})foreach(int zz in new[]{-1,1})if(!solid.Contains(new NavPoint(x+xx*2.3,z+zz*2.3)))fits=false;
                    if(!fits)continue;
                    DockRoofAssembly(b,V(p,4.64f),Quaternion.identity,(n++ + index)%3,4.2f,4.2f);
                }
                if(n==0){
                    // Thin seam blockers use an axial bank, not a field of identical boxes.
                    float width=(float)(solid.MaxX-solid.MinX);float depth=(float)(solid.MaxZ-solid.MinZ);
                    DockRoofAssembly(b,center+Vector3.up*4.64f,Quaternion.identity,1,Mathf.Max(2,width-3),Mathf.Max(2,depth-2));
                }
            }
            b.LocalTransform=Matrix4x4.identity;
        }

        private static void DockModuleShoulder(DockBatch b,IReadOnlyList<NavPoint> points,Vector3 center)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int j=0;j<points.Count;j++){
                var p=V(points[j],0)-center;var q=V(points[(j+1)%points.Count],0)-center;int n=vertices.Count;
                vertices.Add(center+p*.97f+Vector3.up*3.85f);vertices.Add(center+p*.84f+Vector3.up*4.64f);
                vertices.Add(center+q*.84f+Vector3.up*4.64f);vertices.Add(center+q*.97f+Vector3.up*3.85f);
                // Polygon winding is authored clockwise in world XZ; each shoulder faces outward/up.
                triangles.AddRange(new[]{n,n+2,n+1,n,n+3,n+2});
                var along=(q-p).normalized;float length=Vector3.Distance(p,q);
                for(float d=1.6f;d<length-1;d+=3.2f){
                    var edge=p+along*d;
                    b.Beam(center+edge*.963f+Vector3.up*3.91f,center+edge*.85f+Vector3.up*4.65f,.15f,.10f,Steel,4);
                    var bolt=center+edge*.92f+Vector3.up*4.22f;
                    b.Box(bolt,new Vector3(.14f,.09f,.14f),DockOchre,4);
                }
            }
            var m=new Mesh();m.SetVertices(vertices);m.SetTriangles(triangles,0);m.RecalculateNormals();m.RecalculateBounds();b.Add(m,DockArmor,4);
        }

        private static void DockRoofAssembly(DockBatch b,Vector3 at,Quaternion q,int type,float width,float depth)
        {
            void Box(Vector3 p,Vector3 s,Color c,int role=4)=>b.Box(at+q*p,s,c,role,q);
            Box(Vector3.up*.12f,new Vector3(width,.24f,depth),DarkSteel);
            // Large chamfered armor rim supplies visible silhouette, not a painted rectangle.
            DockBeveledBox(b,at+Vector3.up*.49f,new Vector3(width,.68f,depth),.25f,DockArmor,q);
            Box(Vector3.up*.84f,new Vector3(width-.55f,.08f,depth-.55f),new Color(.065f,.09f,.115f));
            if(type==0){
                float r=Mathf.Min(width,depth)*.31f;
                var c=at+Vector3.up*.91f;
                b.Cylinder(c-Vector3.up*.1f,c+Vector3.up*.10f,r+.13f,DarkSteel,4);
                b.Ring(c+Vector3.up*.12f,r+.18f,r,Steel,4);
                b.Cylinder(c,c+Vector3.up*.20f,r*.19f,DockEdge,4);
                for(int k=0;k<10;k++){
                    var spin=q*Quaternion.Euler(0,k*36+18,0);var p=c+spin*new Vector3(0,.10f,r*.55f);
                    b.Box(p,new Vector3(r*.25f,.10f,r*.82f),DockArmor,4,spin*Quaternion.Euler(0,20,0));
                }
                // Cross braces in front of the fan; actual separate slats cast their own shadows.
                for(int k=-2;k<=2;k++)Box(new Vector3(k*r*.30f,1.11f,0),new Vector3(.07f,.06f,r*1.75f),Steel);
            }else if(type==1){
                for(float x=-width*.5f+.45f;x<width*.5f-.35f;x+=.34f)
                    Box(new Vector3(x,.96f,0),new Vector3(.13f,.28f,depth-.85f),Steel);
                Box(new Vector3(0,1.13f,0),new Vector3(width-.5f,.10f,.20f),DarkSteel);
            }else{
                for(int side=-1;side<=1;side+=2){
                    var from=at+q*new Vector3(side*width*.21f,1.03f,-depth*.30f);var to=at+q*new Vector3(side*width*.21f,1.03f,depth*.30f);
                    b.Cylinder(from,to,.22f,side<0?Copper:Steel,4);
                    for(int k=-1;k<=1;k++)Box(new Vector3(side*width*.21f,1.03f,k*depth*.23f),new Vector3(.64f,.43f,.15f),DarkSteel);
                }
            }
            for(int side=-1;side<=1;side+=2){
                Box(new Vector3(side*(width*.5f-.3f),.88f,depth*.5f-.38f),new Vector3(.20f,.09f,.25f),DockOchre);
                Box(new Vector3(side*(width*.5f-.3f),.88f,-depth*.5f+.38f),new Vector3(.20f,.09f,.25f),DockEdge);
            }
        }

        private static void DockBeveledBox(DockBatch b,Vector3 at,Vector3 size,float bevel,Color color,Quaternion q,int role=4)
        {
            float x=size.x*.5f,z=size.z*.5f,c=Mathf.Min(bevel,Mathf.Min(x,z)*.3f);
            var ring=new[]{new Vector2(-x+c,-z),new Vector2(x-c,-z),new Vector2(x,-z+c),new Vector2(x,z-c),new Vector2(x-c,z),new Vector2(-x+c,z),new Vector2(-x,z-c),new Vector2(-x,-z+c)};
            var verts=new List<Vector3>();var triangles=new List<int>();
            void Tri(Vector3 a,Vector3 bb,Vector3 cc){int n=verts.Count;verts.Add(a);verts.Add(bb);verts.Add(cc);triangles.Add(n);triangles.Add(n+1);triangles.Add(n+2);}
            for(int k=0;k<ring.Length;k++){
                var a=ring[k];var z2=ring[(k+1)%ring.Length];
                var lowA=new Vector3(a.x,-size.y*.5f,a.y);var lowB=new Vector3(z2.x,-size.y*.5f,z2.y);
                var midA=new Vector3(a.x,size.y*.5f-c,a.y);var midB=new Vector3(z2.x,size.y*.5f-c,z2.y);
                var topA=new Vector3(a.x*(1-c/x),size.y*.5f,a.y*(1-c/z));var topB=new Vector3(z2.x*(1-c/x),size.y*.5f,z2.y*(1-c/z));
                Tri(lowA,midA,midB);Tri(lowA,midB,lowB);Tri(midA,topA,topB);Tri(midA,topB,midB);Tri(Vector3.up*size.y*.5f,topB,topA);
            }
            var mesh=new Mesh();mesh.SetVertices(verts);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            b.Add(mesh,color,role,Matrix4x4.TRS(at,q,Vector3.one));
        }

        private static void DockDeckDetails(DockBatch b,NullDockMap map,NullDockArtProfile art)
        {
            // Flush maintenance hatches and access strips use existing walkable surfaces only.
            foreach(var support in map.Supports.Where(s=>s.Id=="station-deck"||s.Id.StartsWith("H-")||s.Id.StartsWith("F-"))){
                var bb=support.Bounds;
                for(double x=bb.MinX+5;x<bb.MaxX-4;x+=23)for(double z=bb.MinZ+5;z<bb.MaxZ-4;z+=29){
                    var p=new NavPoint(x,z);if(map.SupportAt(p)!=support||map.Modules.Any(m=>m.BoundaryDistanceSquared(p)<16||m.Contains(p)))continue;
                    if(map.Sites(PlayableProfile.Default).Any(s=>(s.Position.X-p.X)*(s.Position.X-p.X)+(s.Position.Z-p.Z)*(s.Position.Z-p.Z)<225))continue;
                    bool fits=true;foreach(int xx in new[]{-1,1})foreach(int zz in new[]{-1,1})if(map.SupportAt(new NavPoint(x+xx*2.5,z+zz*1.8))!=support)fits=false;
                    if(!fits)continue;var at=V(p,(float)support.HeightAt(p)+.035f);
                    b.Box(at,new Vector3(4.5f,.04f,3),DarkSteel,4);
                    b.Box(at+Vector3.up*.026f,new Vector3(4.15f,.015f,2.7f),DockArmor,3);
                    for(int side=-1;side<=1;side+=2){
                        b.Box(at+new Vector3(side*2.2f,.04f,0),new Vector3(.12f,.02f,3.1f),DockEdge,4);
                        b.Box(at+new Vector3(0,.04f,side*1.48f),new Vector3(4.5f,.02f,.12f),DockEdge,4);
                        b.Box(at+new Vector3(side*1.6f,.055f,1.05f),new Vector3(.45f,.02f,.15f),DockOchre,4);
                    }
                }
            }
            // Each ramp gets a structural frame and joints on its own slope, all flush.
            foreach(var ramp in map.Supports.Where(s=>s.Id.StartsWith("R"))){
                var bb=ramp.Bounds;bool east=Math.Abs(ramp.Gradient.X)>0;
                for(int k=1;k<8;k++){
                    double x=bb.MinX+(bb.MaxX-bb.MinX)*k/8,z=bb.MinZ+(bb.MaxZ-bb.MinZ)*k/8;
                    var box=east?new NavObstacle(x-.045,bb.MinZ+.5,x+.045,bb.MaxZ-.5):new NavObstacle(bb.MinX+.5,z-.045,bb.MaxX-.5,z+.045);
                    b.Add(TerrainMesh.Prism(box,0,0,p=>ramp.HeightAt(p)+.033),DarkSteel,4);
                }
            }
        }

        private static void DockCraneDetails(DockBatch b,NullDockArtProfile art)
        {
            // Two engineering towers stand outside the station footprint at opposite pocket corners.
            // Their footprints never become playable terrain and never occlude a mine approach.
            foreach(int sign in new[]{-1,1}){
                var p=new Vector3(sign*98,-1,sign*82);
                b.Cylinder(p,p+Vector3.up*3,2.1f,DarkSteel,4);
                b.Cylinder(p+Vector3.up*3,p+Vector3.up*5.3f,1.6f,DockArmor,4);
                b.Cylinder(p+Vector3.up*5.3f,p+Vector3.up*5.7f,1.85f,DockEdge,4);
                b.Cylinder(p+Vector3.up*5.7f,p+Vector3.up*8,1.1f,DarkSteel,4);
                b.Cylinder(p+Vector3.up*8,p+Vector3.up*8.5f,1.35f,DockEdge,4);
                b.Ring(p+Vector3.up*8.52f,1.2f,.9f,DockOchre,4);
                b.Cylinder(p+Vector3.up*8.5f,p+Vector3.up*11,.07f,Steel,4);
                for(int k=0;k<8;k++){
                    var q=Quaternion.Euler(0,k*45,0);var dir=q*Vector3.forward;
                    DockBeveledBox(b,p+dir*1.73f+Vector3.up*1.6f,new Vector3(.8f,2.5f,.65f),.15f,DockOchre,q);
                    b.Box(p+dir*1.68f+Vector3.up*4.2f,new Vector3(.60f,.30f,.1f),Cyan,5,q);
                    b.Cylinder(p+dir*1.35f+Vector3.up*5.8f,p+dir*1.35f+Vector3.up*7.8f,.11f,Copper,4);
                }
                b.Box(p+Vector3.up*11,new Vector3(.18f,.14f,.18f),Amber,5);
            }
            foreach(int side in new[]{-1,1}){
                var at=new Vector3(side*6,0,-side*74);var top=at+Vector3.up*art.craneHeight;
                for(int zside=-1;zside<=1;zside+=2){
                    for(int k=0;k<5;k++){
                        float a=k*4,zz=zside*.7f;
                        b.Beam(top+new Vector3(side*a,-.45f,zz),top+new Vector3(side*(a+4),.45f,zz),.14f,.14f,DockOchre,4);
                        b.Beam(top+new Vector3(side*a,.45f,zz),top+new Vector3(side*(a+4),-.45f,zz),.14f,.14f,DockOchre,4);
                    }
                    for(float y=1;y<art.craneHeight-1;y+=1.5f){
                        b.Beam(at+new Vector3(-.35f,y,zside*1.3f),at+new Vector3(.35f,y,zside*1.3f),.10f,.10f,Steel,4);
                    }
                }
                b.Cylinder(top+new Vector3(-side*2,-1,0),top+new Vector3(-side*2,-1,1.65f),.55f,Steel,4);
                for(int k=0;k<7;k++)b.Cylinder(top+new Vector3(-side*2,-1,.15f+k*.2f),top+new Vector3(-side*2,-1,.23f+k*.2f),.60f,DarkSteel,4);
                b.Box(at+new Vector3(-side*3,.4f,0),new Vector3(4,.6f,3),DockOchre,6);
            }
        }
    }
}
