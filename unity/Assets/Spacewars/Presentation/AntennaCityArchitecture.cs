using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
namespace Spacewars.Presentation
{
    // Reproducible city kit. Roof equipment is contained by the original solid envelope;
    // no colliders or simulation objects are created by this builder.
    public static class AntennaCityArchitecture
    {
        public static readonly string[] Themes={"Logistics depot","Communications exchange","Power services","Transit depot","Maintenance works","Civil protection","City forum","Municipal works","Central station","Repair workshop"};
        public const int MunicipalHall = 7;

        public const int Station = 8, Workshop = 9;
        public static int ThemeForQuarter(AntennaCityMap map,int index,int district)
        {
            if(index==map.MunicipalHallQuarter)return MunicipalHall;
            if(index==map.StationQuarter)return Station;
            if(index==map.WorkshopQuarter)return Workshop;
            return district;
        }

        // Subtraction makes convex clipping fragments. Remove a shared seam only when
        // the hull is fully occupied by the two fragments; actual gaps remain open.
        public static IReadOnlyList<NavObstacle> JoinConvexNeighbours(IReadOnlyList<NavObstacle> pieces)
        {
            var shells=pieces.ToList();
            for(int i=0;i<shells.Count;i++){
                for(int j=i+1;j<shells.Count;j++){
                    var a=shells[i];var b=shells[j];
                    if(a.MaxX<b.MinX-1e-7 || b.MaxX<a.MinX-1e-7 || a.MaxZ<b.MinZ-1e-7 || b.MaxZ<a.MinZ-1e-7)continue;
                    var points=a.Footprint.Vertices.Concat(b.Footprint.Vertices).OrderBy(p=>p.X).ThenBy(p=>p.Z).ToArray();
                    var hull=new List<NavPoint>();
                    void Chain(IEnumerable<NavPoint> ordered){
                        int start=hull.Count;
                        foreach(var p in ordered){
                            while(hull.Count>=start+2){
                                var u=hull[hull.Count-2];var v=hull[hull.Count-1];
                                if((v.X-u.X)*(p.Z-u.Z)-(v.Z-u.Z)*(p.X-u.X)>1e-8)break;
                                hull.RemoveAt(hull.Count-1);
                            }
                            hull.Add(p);
                        }
                        hull.RemoveAt(hull.Count-1);
                    }
                    Chain(points); Chain(points.Reverse());
                    if(Math.Abs(Area(hull)-Area(a.Footprint.Vertices)-Area(b.Footprint.Vertices))>1e-6)continue;
                    shells[i]=new NavObstacle(new NavPolygon(hull),a.Bottom,a.Top);
                    shells.RemoveAt(j);
                    j=i;
                }
            }
            return shells;
        }

        static double Area(IReadOnlyList<NavPoint> points)
        {
            double area=0;
            for(int i=0;i<points.Count;i++){
                var a=points[i];var b=points[(i+1)%points.Count];area+=a.X*b.Z-b.X*a.Z;
            }
            return Math.Abs(area)*.5;
        }

        public static Color Graphite(float value)=>new Color(value*.83f,value*.92f,value);
        public static Color Accent(int theme)=>theme%3==0?new Color(.96f,.61f,.25f):theme%3==1?new Color(.22f,.67f,.77f):new Color(.65f,.39f,.65f);
        public static Mesh Quarter(IReadOnlyList<NavObstacle> pieces,double height,int theme,int level,AntennaCityArtProfile art)
        {
            var mesh=new CityMesh();float h=(float)height,recess=Mathf.Min(art.roofRecess,h*.25f);
            var wall=Graphite(art.facadeValue*(1+(theme%3-1)*.06f));
            foreach(var piece in pieces){
                var points=piece.Footprint.Vertices.Select(p=>new Vector3((float)p.X,0,(float)p.Z)).ToArray();
                var center=points.Aggregate(Vector3.zero,(a,b)=>a+b)/points.Length;
                float clearance=float.MaxValue;
                for(int i=0;i<points.Length;i++)clearance=Mathf.Min(clearance,Distance(center,points[i],points[(i+1)%points.Length]));
                if(theme==MunicipalHall){
                    Hall(mesh,points,piece.Footprint.Triangles,center,h,level);
                    continue;
                }
                if(theme==Station || theme==Workshop){
                    TerminalRoof(mesh,points,center,h,theme,level);
                    continue;
                }
                // A thin tip reads as a low enclosed service wing, without rows of tiny windows.
                bool serviceWing=clearance < art.parapetWidth*3;
                // The street-facing plinth occupies the complete blocked footprint at ground level.
                float plinth = h * .36f;
                var curb=points.Select(p=>p+Vector3.up*.12f).ToArray();
                float sillInset=Mathf.Min(.15f,art.parapetWidth/Mathf.Max(clearance,art.parapetWidth));
                var sill=curb.Select(p=>Vector3.Lerp(p,center+Vector3.up*.12f,sillInset)).ToArray();
                Band(mesh,points,curb,center,Graphite(.52f),1,theme);
                Band(mesh,curb,sill,center,new Color(.64f,.62f,.56f),1,theme);
                var baseRing = sill.Select(p => new Vector3(p.x,plinth,p.z)).ToArray();
                var inward = Mathf.Min(.32f, art.parapetWidth / Mathf.Max(clearance, art.parapetWidth));
                var roof = baseRing.Select(p => Vector3.Lerp(p, center + Vector3.up * plinth, inward)).ToArray();
                Band(mesh, sill, baseRing, center, wall, serviceWing ? 2 : 3, theme);
                Band(mesh, baseRing, roof, center, Graphite(.52f), 2, theme);
                Cap(mesh, roof, Graphite(.34f), 2);
                if (serviceWing) continue;

                Vector3[] Terrace(float scale, float y) => roof.Select(p =>
                    Vector3.Lerp(center + Vector3.up * plinth, p, scale) + Vector3.up * (y - plinth)).ToArray();

                if (theme == 0 || theme == 1 || theme >= 5) {
                    // Cantilevered freight hall, tapered exchange, stepped civic halls.
                    float baseScale = theme == 0 ? .70f : .94f;
                    float crownScale, ledgeScale;
                    switch(theme) {
                        case 0: ledgeScale=.97f; crownScale=.97f; break;
                        case 1: ledgeScale=.48f; crownScale=.15f; break;
                        case 5: ledgeScale=.69f; crownScale=.58f; break;
                        default: ledgeScale=.69f; crownScale=.38f; break;
                    }
                    var lower = Terrace(baseScale, plinth);
                    var shoulder = Terrace(theme == 1 ? .56f : baseScale, h * .68f);
                    Band(mesh, lower, shoulder, center, wall, 3, theme);
                    var ledge = Terrace(ledgeScale, h * .68f);
                    Band(mesh, shoulder, ledge, center, Graphite(.59f), 2, theme);
                    var crown = Terrace(crownScale, h);
                    Band(mesh, ledge, crown, center, theme == 6 ? Graphite(.75f) : wall, 3, theme);
                    var capColor = theme == 6 ? new Color(.32f,.56f,.62f) : Graphite(.46f);
                    if (level < 2) {
                        var rim = Terrace(crownScale*.86f, h);
                        Band(mesh,crown,rim,center,Graphite(.62f),2,theme);
                        var well=rim.Select(p=>p-Vector3.up*recess*.35f).ToArray();
                        Band(mesh,rim,well,center,Graphite(.28f),2,theme);
                        Cap(mesh,well,capColor,theme==6?5:2);
                    } else Cap(mesh,crown,capColor,theme==6?5:2);
                    continue;
                }

                // Choose the longitudinal axis from the lot itself so halls follow their frontage.
                var axis = points.Select((p,i)=>points[(i+1)%points.Length]-p)
                    .OrderByDescending(edge=>edge.sqrMagnitude).First().normalized;
                var across = new Vector3(-axis.z,0,axis.x);
                float halfLength = points.Max(p=>Mathf.Abs(Vector3.Dot(p-center,axis)));
                float halfWidth = points.Max(p=>Mathf.Abs(Vector3.Dot(p-center,across)));
                // Fit an oriented rectangle analytically to every half-plane of the convex lot.
                float fit = 1;
                for(int i=0;i<points.Length;i++) {
                    var edge=points[(i+1)%points.Length]-points[i];
                    if(edge.sqrMagnitude < 1e-10f) continue;
                    var n=new Vector3(edge.z,0,-edge.x).normalized;
                    float reach=halfLength*Mathf.Abs(Vector3.Dot(n,axis))+halfWidth*Mathf.Abs(Vector3.Dot(n,across));
                    if(reach>0) fit=Mathf.Min(fit,Mathf.Abs(Vector3.Dot(n,center-points[i]))/reach);
                }
                halfLength *= fit*.94f; halfWidth *= fit*.94f;
                var pivot=center+Vector3.up*plinth;
                if(theme==2) {
                    int segments=level==2?8:level==1?12:16;
                    Vector3[] Drum(float scale,float y) => Enumerable.Range(0,segments).Select(i=>{
                        float angle=2*Mathf.PI*i/segments;
                        return center+axis*(halfLength*scale*Mathf.Cos(angle))+across*(halfWidth*scale*Mathf.Sin(angle))+Vector3.up*y;
                    }).ToArray();
                    var bottom=Drum(1,plinth);var neck=Drum(.77f,h-recess);var lip=Drum(.88f,h);
                    Band(mesh,bottom,neck,center,wall,3,theme);
                    Band(mesh,neck,lip,center,Graphite(.59f),2,theme);
                    var well=Drum(.68f,h-recess*.55f);
                    Band(mesh,lip,well,center,Graphite(.37f),2,theme);
                    Cap(mesh,well,Graphite(.22f),7);
                } else {
                    int spans=theme==3?(level==0?12:level==1?8:4):6;
                    var section=new List<Vector3>{pivot-across*halfWidth};
                    for(int i=0;i<=spans;i++){
                        float t=(float)i/spans;
                        float rise=theme==3?Mathf.Sin(t*Mathf.PI):i%2;
                        section.Add(pivot+across*((2*t-1)*halfWidth)+Vector3.up*((h-plinth)*(.40f+.60f*rise)));
                    }
                    section.Add(pivot+across*halfWidth);
                    var front=section.Select(p=>p+axis*halfLength).ToArray();
                    var back=section.Select(p=>p-axis*halfLength).ToArray();
                    for(int i=0;i<section.Count-1;i++){
                        var normal=Vector3.Cross(section[i+1]-section[i],axis).normalized;
                        mesh.Quad(back[i],front[i],front[i+1],back[i+1],normal,
                            theme==3?new Color(.24f,.46f,.52f):Graphite(i%2==0?.57f:.32f),2);
                        if(i>0 && i<section.Count-2){
                            var foot= Vector3.up*(section[i].y-plinth);
                            var nextFoot=Vector3.up*(section[i+1].y-plinth);
                            mesh.Quad(front[i]-foot,front[i+1]-nextFoot,front[i+1],front[i],axis,wall,3,theme,new Vector2(2*halfWidth/spans,h-plinth));
                            mesh.Quad(back[i]-foot,back[i+1]-nextFoot,back[i+1],back[i],-axis,wall,3,theme,new Vector2(2*halfWidth/spans,h-plinth));
                        }
                    }
                    if(level==0 && halfLength>art.equipmentSpacing) {
                        // Mullions sit on the gable, inside the plinth boundary.
                        for(int i=1;i<section.Count-1;i++){
                            float y=section[i].y-plinth;
                            if(y>recess)mesh.Box(front[i]-Vector3.up*(y*.5f),
                                new Vector3(.12f,y,.12f),Graphite(.55f),2);
                        }
                    }
                }
            }
            return mesh.Finish("Nova City "+Themes[theme]+" LOD"+level);
        }

        static void TerminalRoof(CityMesh mesh,Vector3[] footprint,Vector3 center,float height,int theme,int level)
        {
            bool station=theme==Station;
            var axis=center.normalized;
            var across=new Vector3(-axis.z,0,axis.x);
            var left=footprint.Where(p=>Vector3.Dot(p-center,across)<0).OrderBy(p=>Vector3.Dot(p,axis)).ToArray();
            var right=footprint.Where(p=>Vector3.Dot(p-center,across)>0).OrderBy(p=>Vector3.Dot(p,axis)).ToArray();
            float eave=height*.54f;
            var sill=footprint.Select(p=>p+Vector3.up*(height*.12f)).ToArray();
            var top=footprint.Select(p=>p+Vector3.up*eave).ToArray();
            Band(mesh,footprint,sill,center,Graphite(.54f),1,theme);
            Band(mesh,sill,top,center,station?Graphite(.56f):new Color(.53f,.43f,.33f),3,theme);
            var section=new List<Vector2>();
            if(station){
                int spans=level==0?12:level==1?8:4;
                for(int i=0;i<=spans;i++){
                    float t=(float)i/spans;
                    section.Add(new Vector2(t,Mathf.Sin(t*Mathf.PI)));
                }
            }else{
                section.AddRange(new[]{new Vector2(0,0),new Vector2(.36f,1),new Vector2(.36f,.18f),
                    new Vector2(.5f,0),new Vector2(.86f,1),new Vector2(.86f,.18f),new Vector2(1,0)});
            }
            // The entrance gallery carries a shallower version of the main roof.
            float fullSpan=Vector3.Distance(left.Last(),right.Last());
            Vector3 RoofPoint(Vector3 l,Vector3 r,Vector2 point)
            {
                float spanFraction=Mathf.Clamp01(Vector3.Distance(l,r)/fullSpan);
                return Vector3.Lerp(l,r,point.x)+Vector3.up*(eave+(height-eave)*spanFraction*point.y);
            }
            for(int j=1;j<section.Count;j++){
                var a=section[j-1];var b=section[j];
                bool glazing=station || a.x==b.x;
                var panel=glazing?new Color(.36f,.60f,.67f):new Color(.62f,.57f,.48f);
                for(int k=1;k<left.Length;k++){
                    int bays=level==2?1:level==1?2:4;
                    for(int bay=0;bay<bays;bay++){
                        float begin=(float)bay/bays,end=(float)(bay+1)/bays;
                        float ribEnd=level==2?begin:Mathf.Lerp(begin,end,.10f);
                        void Panel(float from,float to,bool rib){
                            var l=Vector3.Lerp(left[k-1],left[k],from);
                            var r=Vector3.Lerp(right[k-1],right[k],from);
                            var nextL=Vector3.Lerp(left[k-1],left[k],to);
                            var nextR=Vector3.Lerp(right[k-1],right[k],to);
                            var p=RoofPoint(l,r,a);var q=RoofPoint(l,r,b);
                            var s=RoofPoint(nextL,nextR,a);var u=RoofPoint(nextL,nextR,b);
                            var normal=Vector3.Cross(q-p,s-p).normalized;
                            mesh.Quad(p,s,u,q,normal,rib?Graphite(.69f):panel,rib || !glazing?2:8,theme,
                                new Vector2(Vector3.Distance(p,s),Vector3.Distance(p,q)));
                        }
                        if(ribEnd>begin)Panel(begin,ribEnd,true);
                        Panel(ribEnd,end,false);
                    }
                }
                if(a.x==b.x)continue;
                foreach(int end in new[]{0,left.Length-1}){
                    var l=left[end];var r=right[end];
                    var p=RoofPoint(l,r,a);var q=RoofPoint(l,r,b);
                    var normal=end==0?-axis:axis;
                    var bottomP=new Vector3(p.x,eave,p.z);var bottomQ=new Vector3(q.x,eave,q.z);
                    if(a.y>.0001f)mesh.Triangle(bottomP,p,q,normal,panel,glazing?8:2);
                    if(b.y>.0001f)mesh.Triangle(bottomP,q,bottomQ,normal,panel,glazing?8:2);
                }
            }
        }

        static void Hall(CityMesh mesh,Vector3[] footprint,IReadOnlyList<int> triangles,Vector3 center,float height,int level)
        {
            Vector3[] Ring(float scale,float elevation) => footprint.Select(p=>
                Vector3.Lerp(center,p,scale)+Vector3.up*elevation).ToArray();
            var footing=Ring(1,height*.16f);
            var galleryRoof=Ring(1,height*.42f);
            Band(mesh,footprint,footing,center,new Color(.64f,.60f,.52f),2,MunicipalHall);
            Band(mesh,footing,galleryRoof,center,new Color(.49f,.47f,.42f),3,MunicipalHall);
            for(int i=0;i<triangles.Count;i+=3)
                mesh.Triangle(galleryRoof[triangles[i]],galleryRoof[triangles[i+1]],galleryRoof[triangles[i+2]],Vector3.up,Graphite(.50f),2);

            // Keep the entrance gallery low. Only the broad service hall rises to the cornice.
            var radial=new Vector3(-.8660254f,0,.5f);
            var cross=new Vector3(.5f,0,.8660254f);
            float rear=footprint.Max(p=>Vector3.Dot(p,radial));
            float front=footprint.Min(p=>Vector3.Dot(p,radial));
            var body=galleryRoof.Where(p=>Vector3.Dot(p,radial)>Mathf.Lerp(front,rear,.66f)).ToArray();
            var bodyCenter=body.Aggregate(Vector3.zero,(total,p)=>total+p)/body.Length;
            var cornice=body.Select(p=>Vector3.Lerp(bodyCenter,p,.97f)+Vector3.up*(height*.36f)).ToArray();
            var top=cornice.Select(p=>p+Vector3.up*(height*.06f)).ToArray();
            Band(mesh,body,cornice,bodyCenter,new Color(.45f,.48f,.50f),3,MunicipalHall);
            Band(mesh,cornice,top,bodyCenter,new Color(.62f,.55f,.43f),2,MunicipalHall);
            Cap(mesh,top,new Color(.52f,.58f,.61f),2);
            var span=footprint.Max(p=>Vector3.Dot(p-center,cross));
            var roof=center+radial*(footprint.Max(p=>Vector3.Dot(p-center,radial))-span*.55f)+Vector3.up*(height*.84f);
            // Low longitudinal glazed monitor, standing seams and paired air intakes.
            var lamp=roof+radial*(span*.1f);
            var x=cross*(span*.56f);var z=radial*(span*.12f);
            var curb=new[]{lamp-x-z,lamp+x-z,lamp+x+z,lamp-x+z};
            var crest=curb.Select(p=>lamp+(p-lamp)*.9f+Vector3.up*(height*.16f)).ToArray();
            Band(mesh,curb,crest,lamp,new Color(.32f,.61f,.65f),8,MunicipalHall);
            Cap(mesh,crest,Graphite(.60f),2);
            if(level==2)return;
            foreach(int side in new[]{-1,1}){
                var at=roof+cross*(side*span*.38f)+radial*(span*.28f);
                float size=span*.24f;
                mesh.Box(at+Vector3.up*.15f,new Vector3(size,.30f,size),Graphite(.43f),2);
                mesh.FlatRect(at+Vector3.up*.305f,size*.85f,size*.85f,Graphite(.24f),7);
                if(level==0)for(int k=-1;k<=1;k++)
                    mesh.Box(at+new Vector3(size*k*.24f,.34f,0),new Vector3(.08f,.07f,size*.84f),Graphite(.65f),2);
            }
        }

        static void Band(CityMesh mesh, Vector3[] lower, Vector3[] upper, Vector3 center, Color color, int role, int theme)
        {
            for(int i=0;i<lower.Length;i++) {
                int j=(i+1)%lower.Length;
                var edge=lower[j]-lower[i];
                var normal=Vector3.Cross(edge,upper[i]-lower[i]).normalized;
                if(Mathf.Abs(normal.y)>.999f) normal=Vector3.up*(Vector3.Distance(upper[i],center)>Vector3.Distance(lower[i],center)?-1:1);
                else if(Vector3.Dot(normal,new Vector3(lower[i].x-center.x,0,lower[i].z-center.z))<0) normal=-normal;
                mesh.Quad(lower[i],lower[j],upper[j],upper[i],normal,color,role,theme,new Vector2(edge.magnitude,Vector3.Distance(lower[i],upper[i])));
            }
        }
        static void Cap(CityMesh mesh, Vector3[] points, Color color, int role)
        {
            for(int i=1;i<points.Length-1;i++)mesh.Triangle(points[0],points[i],points[i+1],Vector3.up,color,role);
        }
        public static bool Fits(NavObstacle piece,Vector3 at,float width,float depth)
        {foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})if(!piece.Contains(new NavPoint(at.x+x*width*.5f,at.z+z*depth*.5f)))return false;return true;}
        static float Distance(Vector3 p,Vector3 a,Vector3 b){var d=b-a;return Vector3.Distance(p,a+d*Mathf.Clamp01(Vector3.Dot(p-a,d)/d.sqrMagnitude));}
    }
    // One material, indexed compact meshes, no hidden box bottoms. Vertex data stores surface role/theme.
    public sealed class CityMesh
    {
        readonly List<Vector3> vertices=new List<Vector3>(),normals=new List<Vector3>();
        readonly List<Color> colors=new List<Color>();readonly List<Vector2> uv=new List<Vector2>(),roles=new List<Vector2>();readonly List<int> triangles=new List<int>();
        void Vertex(Vector3 p,Vector3 normal,Color color,int role,int theme,Vector2 tex){vertices.Add(p);normals.Add(normal);colors.Add(color);uv.Add(tex);roles.Add(new Vector2(role,theme));}
        public void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 normal,Color color,int role,int theme=0){int s=vertices.Count;Vertex(a,normal,color,role,theme,new Vector2(a.x,a.z));Vertex(b,normal,color,role,theme,new Vector2(b.x,b.z));Vertex(c,normal,color,role,theme,new Vector2(c.x,c.z));triangles.Add(s);bool front=Vector3.Dot(Vector3.Cross(b-a,c-a),normal)>=0;triangles.Add(s+(front?1:2));triangles.Add(s+(front?2:1));}
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 normal,Color color,int role,int theme=0,Vector2? size=null){int s=vertices.Count;var q=size??Vector2.one;Vertex(a,normal,color,role,theme,Vector2.zero);Vertex(b,normal,color,role,theme,new Vector2(q.x,0));Vertex(c,normal,color,role,theme,q);Vertex(d,normal,color,role,theme,new Vector2(0,q.y));bool front=Vector3.Dot(Vector3.Cross(b-a,c-a),normal)>=0;triangles.AddRange(front?new[]{s,s+1,s+2,s,s+2,s+3}:new[]{s,s+2,s+1,s,s+3,s+2});}
        public void FlatRect(Vector3 at,float width,float depth,Color color,int role,int theme=0){var x=Vector3.right*width*.5f;var z=Vector3.forward*depth*.5f;Quad(at-x-z,at+x-z,at+x+z,at-x+z,Vector3.up,color,role,theme,new Vector2(width,depth));}
        public void Strip(Vector3 a,Vector3 b,float width,Color color,int role){var d=(b-a).normalized;var n=new Vector3(d.z,0,-d.x)*width*.5f;Quad(a-n,b-n,b+n,a+n,Vector3.up,color,role);}
        public void Box(Vector3 at,Vector3 size,Color color,int role){var a=at-size*.5f;var b=at+size*.5f;FlatRect(new Vector3(at.x,b.y,at.z),size.x,size.z,color,role);Quad(new Vector3(a.x,a.y,a.z),new Vector3(b.x,a.y,a.z),new Vector3(b.x,b.y,a.z),new Vector3(a.x,b.y,a.z),Vector3.back,color,role);Quad(new Vector3(b.x,a.y,b.z),new Vector3(a.x,a.y,b.z),new Vector3(a.x,b.y,b.z),new Vector3(b.x,b.y,b.z),Vector3.forward,color,role);Quad(new Vector3(a.x,a.y,b.z),new Vector3(a.x,a.y,a.z),new Vector3(a.x,b.y,a.z),new Vector3(a.x,b.y,b.z),Vector3.left,color,role);Quad(new Vector3(b.x,a.y,a.z),new Vector3(b.x,a.y,b.z),new Vector3(b.x,b.y,b.z),new Vector3(b.x,b.y,a.z),Vector3.right,color,role);}
        public void Add(Mesh source,Color color,int role,double heading=0,NavPoint origin=default){int start=vertices.Count;var v=source.vertices;var n=source.normals;float c=(float)Math.Cos(heading),s=(float)Math.Sin(heading);for(int i=0;i<v.Length;i++){float x=v[i].x-(float)origin.X,z=v[i].z-(float)origin.Z;Vertex(v[i],n[i],color,role,0,new Vector2(c*x+s*z,-s*x+c*z));}triangles.AddRange(source.triangles.Select(i=>i+start));if(Application.isPlaying)UnityEngine.Object.Destroy(source);else UnityEngine.Object.DestroyImmediate(source);}
        public Mesh Finish(string name){var m=new Mesh{name=name,indexFormat=IndexFormat.UInt32};m.SetVertices(vertices);m.SetNormals(normals);m.SetColors(colors);m.SetUVs(0,uv);m.SetUVs(1,roles);m.SetTriangles(triangles,0);m.RecalculateBounds();FinalProceduralMesh.IndexOwned(m);return m;}
    }
}
