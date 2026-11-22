using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        GameObject mirrorRuins;
        void ClearVacantMirrorSites(){
            if(mirrorRuins)ReleaseWorldObject(mirrorRuins);
            mirrorRuins=null;
            if(mirrorRuinMeshes==null)return;
            foreach(var mesh in mirrorRuinMeshes)ReleaseWorldObject(mesh);
            mirrorRuinMeshes=null;
        }
        void CreateVacantMirrorSites(DeafMirrorMap map,PlayableSnapshot view){
            if(view.Participants.Count<2||view.PublicSites.Count==0)return;
            var occupied=view.PublicSites.Where(s=>s.Kind==PlayableBuildingKind.Headquarters).Select(s=>s.Position).ToArray();
            if(occupied.Length==8)return;
            mirrorRuins=new GameObject("Abandoned observatory stations");
            mirrorRuins.transform.SetParent(root,false);
            var meshes=mirrorRuinMeshes??(mirrorRuinMeshes=Enumerable.Range(0,3).Select(RuinMesh).ToArray());
            for(int i=0;i<8;i++){
                var position=map.Headquarters((PlayableOwner)i);
                if(occupied.Contains(position))continue;
                var station=new GameObject("Ruined station "+(i+1)).transform;
                station.SetParent(mirrorRuins.transform,false);station.localPosition=Point(position);
                station.localRotation=Quaternion.Euler(0,i*73,0);
                station.gameObject.layer=presentationLayer;
                station.gameObject.AddComponent<MeshFilter>().sharedMesh=meshes[0];
                var renderer=station.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial=cityMaterial;
                var detail=station.gameObject.AddComponent<ModelDetailLod>();
                detail.Configure(new Renderer[]{renderer},new[]{meshes[0]},new[]{meshes[1]},new[]{meshes[2]},meshes[0].bounds);
                station.gameObject.AddComponent<CrossingSceneryDetail>().Bind(detail);
            }
        }
        Mesh[] mirrorRuinMeshes;
        Mesh RuinMesh(int level){
            var batch=new CityMesh();
            var concrete=new Color(.37f,.36f,.35f);
            var steel=new Color(.26f,.28f,.29f);
            batch.Box(new Vector3(-2,1.2f,0),new Vector3(.65f,2.4f,4),concrete,2);
            batch.Quad(new Vector3(-1.8f,1.7f,-2),new Vector3(2,.25f,-1.4f),new Vector3(1.6f,.25f,1.8f),new Vector3(-1.8f,1.7f,1.4f),new Vector3(.4f,1,0).normalized,steel,2);
            if(level<2){
                batch.Box(new Vector3(1.7f,.3f,-1.55f),new Vector3(1.65f,.6f,.48f),concrete,2);
                batch.Box(new Vector3(2.15f,.16f,-.9f),new Vector3(.64f,.32f,1.1f),steel,2);
            }
            if(level<2)batch.Box(new Vector3(-.9f,.6f,1.7f),new Vector3(2.4f,1.2f,.65f),concrete,2);
            if(level==0)batch.Box(new Vector3(2.5f,.16f,.8f),new Vector3(.33f,.32f,3.3f),steel,2);
            return batch.Finish("Collapsed observatory station");
        }

        static Texture2D mirrorDistricts;
        static Texture2D MirrorDistricts => mirrorDistricts ? mirrorDistricts :
            (mirrorDistricts=Resources.Load<Texture2D>("Environment/MirrorDistricts"));

        // The mask covers 448 by 224 metres, north up, including a narrow margin
        // beyond the north and south banks. Alpha is the authored hard-cover mask.
        internal static Color MirrorDistrictAt(DeafMirrorMap map,NavPoint point)
        {
            return MirrorDistricts.GetPixelBilinear((float)(point.X/(2*map.HalfExtent)+.5),
                (float)(point.Z/map.HalfExtent+.5));
        }

        void CreateDeafMirror(DeafMirrorMap map)
        {
            // The existing three-variant surface shader also binds the shared fog mask.
            cityMaterial=new Material(Resources.Load<Shader>("AntennaCitySurface")){name="Lunar regolith and structural steel"};
            cityMaterial.SetTexture("_FogMask",Fog.Texture);
            float bankExtent=(float)map.Ground.Max(g=>g.MaxZ);
            cityMaterial.SetVector("_FogBounds",new Vector4((float)map.HalfExtent,(float)map.HalfExtent,bankExtent,0));
            cityMaterial.SetColor("_FogColor",new Color((float)profile.FogTintR,(float)profile.FogTintG,(float)profile.FogTintB));
            cityMaterial.SetVector("_Finish",new Vector4(8,.95f,27,4));
            foreach(var suffix in new[]{"","B","C"}){
                var variant=suffix==""?"":"-"+suffix.ToLowerInvariant();
                cityMaterial.SetTexture("_Concrete"+suffix,Resources.Load<Texture2D>("Environment/NaturalFrontier/Rock"+variant));
                cityMaterial.SetTexture("_Steel"+suffix,Resources.Load<Texture2D>("Environment/NullDock/Steel"+variant));
            }
            cityMaterial.SetTexture("_MirrorDistricts",MirrorDistricts);
            cityMaterial.SetTexture("_LunarPad",Resources.Load<Texture2D>("Environment/NaturalFrontier/Concrete"));
            cityMaterial.SetTexture("_LunarPadB",Resources.Load<Texture2D>("Environment/NaturalFrontier/Concrete-b"));
            cityMaterial.SetTexture("_LunarPadC",Resources.Load<Texture2D>("Environment/NaturalFrontier/Concrete-c"));
            var field=new CityMesh();
            var dust=new Color(.63f,.65f,.70f);
            var metal=new Color(.53f,.57f,.60f);
            foreach(var tile in map.Ground)field.Add(TerrainMesh.Prism(tile,-8,0),dust,-1);
            field.Add(MirrorRelief(map),dust,-1);
            MirrorHorizon(field,(float)map.HalfExtent,bankExtent,dust);
            MirrorAprons(field,map,profile);
            foreach(var relay in map.Sites(profile).Where(site=>site.Kind==PlayableBuildingKind.RelayTower)){
                // The relay overlay is raised above the soil. Stop the conduit just
                // inside its radius so the capture markings remain continuous.
                var origin=new Vector3((float)relay.Position.X,.078f,(float)relay.Position.Z);
                var heading=origin.z<0?Vector3.right:Vector3.left;
                var end=origin+heading*((float)profile.RelayCaptureRadius-.5f);
                field.Strip(origin,end,.72f,new Color(.31f,.31f,.33f),2);
                field.Strip(origin+Vector3.up*.009f,end+Vector3.up*.009f,.34f,new Color(.58f,.53f,.45f),2);
            }
            foreach(var trench in map.Cuts){
                field.Add(TerrainMesh.Prism(trench,-8.2,-8),new Color(.23f,.22f,.25f),0);
                MirrorTalus(field,trench);
            }
            foreach(var bridge in map.Bridges) MirrorBridge(field,bridge);
            EmitMirrorMesh(field,"Реголит и мосты");
            foreach(var trench in map.Cuts)
                EmitMirrorDetail("Слоистые борта разлома",level=>MirrorBanks(trench,level));
            for(int i=0;i<map.Rocks.Count;i++){
                var rock=map.Rocks[i];
                int shape=(i/2+i%2)%3;
                EmitMirrorDetail("Обнажение породы",level=>MirrorRock(rock,shape,level));
            }
            foreach(var site in map.Facilities)
                EmitMirrorDetail("Оборудование обсерватории",level=>MirrorFacility(site,metal,level));
        }

        // Scenery only. The inner loop meets the four actual edges of the playable ground.
        static void MirrorHorizon(CityMesh mesh,float x,float z,Color dust){
            var corners=new[]{new Vector3(-x,0,-z),new Vector3(x,0,-z),
                new Vector3(x,0,z),new Vector3(-x,0,z)};
            float[] rings={0,2,4,7,10,14,18,23,29,36,45,57,72,90,112};
            var positions=new List<Vector3>();
            var normals=new List<Vector3>();
            var indices=new List<int>();
            float Height(float px,float pz){
                float dx=Mathf.Max(0,Mathf.Abs(px)-x),dz=Mathf.Max(0,Mathf.Abs(pz)-z);
                float distance=Mathf.Sqrt(dx*dx+dz*dz);
                float near=Mathf.SmoothStep(0,1,Mathf.Clamp01(distance/9));
                float warp=Mathf.PerlinNoise(78+px*.038f,156+pz*.045f);
                float wave=Mathf.PerlinNoise(83+px*.021f,43+pz*.032f);
                float ribs=Mathf.PerlinNoise(35+px*.041f,19+pz*.096f+warp);
                float crest=(pz-125f+.16f*(px+210f)+3f*Mathf.Sin(px*.074f))/6.4f;
                float west=(px+201)/40f;
                float ridge=4.6f*Mathf.Exp(-crest*crest-west*west)*(.62f+.76f*warp);
                return -.015f+near*(ridge+5.3f*(wave-.50f)+2.2f*(ribs-.52f)-distance*.056f);
            }
            // Continuous world-space normals carry the lighting across the grid
            // and across all four corners of the perimeter.
            for(int side=0;side<corners.Length;side++){
                var start=corners[side];var end=corners[(side+1)%4];
                int spans=Mathf.CeilToInt(Vector3.Distance(start,end)/3.5f);
                int first=positions.Count;
                for(int i=0;i<=spans;i++){
                    var edge=Vector3.Lerp(start,end,(float)i/spans);
                    var outward=new Vector3(edge.x/x,0,edge.z/z);
                    foreach(float distance in rings){
                        var v=edge+outward*distance;
                        v.y=Height(v.x,v.z);
                        positions.Add(v);
                        normals.Add(new Vector3(Height(v.x-.5f,v.z)-Height(v.x+.5f,v.z),1,
                            Height(v.x,v.z-.5f)-Height(v.x,v.z+.5f)).normalized);
                    }
                    if(i==0)continue;
                    for(int j=1;j<rings.Length;j++){
                        int a=first+(i-1)*rings.Length+j-1,b=a+rings.Length;
                        indices.Add(a);indices.Add(b);indices.Add(b+1);
                        indices.Add(a);indices.Add(b+1);indices.Add(a+1);
                    }
                }
            }
            var skirt=new Mesh{name="Lunar terrain beyond the observatory"};
            skirt.SetVertices(positions);skirt.SetNormals(normals);skirt.SetTriangles(indices,0);
            mesh.Add(skirt,dust,-1);
        }

        static void MirrorTalus(CityMesh field,NavObstacle trench)
        {
            var rim=trench.Footprint.Vertices;
            var middle=new Vector3((float)rim.Average(p=>p.X),-7.93f,(float)rim.Average(p=>p.Z));
            var shale=new Color(.37f,.35f,.37f);
            for(int i=0;i<rim.Count;i++){
                var a=rim[i];var b=rim[(i+1)%rim.Count];
                // The vertical cross-sections between adjacent slices are inside the fissure.
                if(Math.Abs(a.X-b.X)<.01)continue;
                var start=new Vector3((float)a.X,middle.y,(float)a.Z);
                var end=new Vector3((float)b.X,middle.y,(float)b.Z);
                float length=Vector3.Distance(start,end);
                var tangent=(end-start).normalized;
                var inward=Vector3.Cross(Vector3.up,tangent);
                if(Vector3.Dot(inward,middle-(start+end)*.5f)<0)inward=-inward;
                // A fall originates at a single break in a bank. Leave entire
                // reaches bare between falls; the largest slab sits at the source.
                int seed=Mathf.RoundToInt(start.x*31+start.z*139+end.x*83);
                var scatter=new System.Random(seed);
                if(length<4 || scatter.NextDouble()<.52)continue;
                float source=.26f+.48f*(float)scatter.NextDouble();
                int count=scatter.Next(4,11);
                for(int k=0;k<count;k++){
                    float along=(float)(scatter.NextDouble()-scatter.NextDouble());
                    float t=source+(k==0?0:along)*.19f;
                    var bank=Vector3.Lerp(start,end,t);
                    float runout=k==0?.23f:.16f+.53f*(float)scatter.NextDouble();
                    var center=Vector3.Lerp(bank,middle,runout);
                    float size=k==0?2.2f:.34f+.57f*(float)scatter.NextDouble();
                    float radius=Mathf.Min(size,length*.16f,Vector3.Distance(center,bank)*.78f);
                    var left=center-tangent*radius*(k==0?1.32f:1);
                    var right=center+tangent*radius*.83f;
                    var toe=center+inward*radius*.86f;
                    var back=center-inward*radius*.66f;
                    var peak=center+tangent*radius*.22f+Vector3.up*radius*.74f;
                    void Face(Vector3 a,Vector3 b){
                        var normal=Vector3.Cross(b-a,peak-a).normalized;
                        field.Triangle(a,b,peak,normal.y<0?-normal:normal,shale,0);
                    }
                    Face(left,toe);Face(toe,right);Face(right,back);Face(back,left);
                }
            }
        }

        static CityMesh MirrorBanks(NavObstacle trench,int level)
        {
            var mesh=new CityMesh();
            var points=trench.Footprint.Vertices;
            var west=points.Where(p=>p.X==trench.MinX).OrderBy(p=>p.Z).ToArray();
            var east=points.Where(p=>p.X==trench.MaxX).OrderBy(p=>p.Z).ToArray();
            var shale=new Color(.32f,.32f,.37f);
            var dust=new Color(.51f,.49f,.49f);
            int spans=Mathf.Max(1,Mathf.CeilToInt((float)(trench.MaxX-trench.MinX)/(level==0?2.8f:level==1?4.8f:8f)));
            foreach(int side in new[]{-1,1}){
                var left=side<0?west[0]:west[west.Length-1];
                var right=side<0?east[0]:east[east.Length-1];
                float widthWest=(float)(west[west.Length-1].Z-west[0].Z);
                float widthEast=(float)(east[east.Length-1].Z-east[0].Z);
                Vector3[] Section(float t){
                    float x=Mathf.Lerp((float)left.X,(float)right.X,t);
                    float z=Mathf.Lerp((float)left.Z,(float)right.Z,t);
                    float width=Mathf.Lerp(widthWest,widthEast,t);
                    float grain=Mathf.PerlinNoise(95+x*.23f,55+z*.17f);
                    float chip=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.46f,.74f,Mathf.PerlinNoise(72+x*.14f,38+z*.1f)));
                    Vector3 Layer(float depth,float reach)=>new Vector3(x,depth,z-side*Mathf.Min(width*.29f,reach));
                    var lip=Layer(-.12f-.95f*chip,.23f+grain*.65f);
                    var shelf=Layer(-2.1f-.96f*grain,1.0f+.89f*chip);
                    var foot=Layer(-7.99f,.35f+grain*.6f);
                    var top=Layer(.012f,0);
                    if(level==2)return new[]{top,lip,shelf,foot};
                    var seam=Layer(shelf.y-.27f,.35f+.56f*chip);
                    var baseLedge=Layer(-5.1f+grain*.73f,1.35f+.53f*grain);
                    if(level==1)return new[]{top,lip,shelf,seam,baseLedge,foot};
                    return new[]{top,lip,shelf,seam,baseLedge,Layer(baseLedge.y-.36f,.58f),foot};
                }
                var a=Section(0);
                for(int i=1;i<=spans;i++){
                    var b=Section((float)i/spans);
                    for(int j=1;j<a.Length;j++){
                        var normal=Vector3.Cross(a[j]-a[j-1],b[j-1]-a[j-1]).normalized;
                        if(normal.z*side>0)normal=-normal;
                        var tint=j==1?dust:Color.Lerp(shale,dust,j%2==0?.28f:.08f);
                        mesh.Quad(a[j-1],a[j],b[j],b[j-1],normal,tint,0);
                    }
                    a=b;
                }
                float bankLength=(float)(right.X-left.X);
                var breaks=new System.Random(Mathf.RoundToInt((float)(left.X*41+left.Z*89))+side);
                float cursor=2f+(float)breaks.NextDouble()*12f;
                while(cursor<bankLength-2f){
                    int count=breaks.Next(2,4);
                    for(int k=0;k<count && cursor<bankLength-2f;k++){
                        float width=(k==0?3.5f:1.2f)+(float)breaks.NextDouble()*2.8f;
                        float t0=cursor/bankLength;
                        float t1=Mathf.Min(1,(cursor+width)/bankLength);
                        a=Section(t0);
                        var b=Section(t1);
                        float clearance=Mathf.Min(Mathf.Lerp(widthWest,widthEast,t0),
                            Mathf.Lerp(widthWest,widthEast,t1));
                        var outcrop=Vector3.forward*(-side*Mathf.Min(1.55f,clearance*.13f));
                        var upperA=Vector3.Lerp(a[1],b[1],.07f);
                        var upperB=Vector3.Lerp(a[1],b[1],.86f);
                        var crestA=upperA+outcrop*.36f+Vector3.down*.17f;
                        var crestB=upperB+outcrop+Vector3.down*.32f;
                        var toeA=Vector3.Lerp(a[2],b[2],.25f);
                        var toeB=Vector3.Lerp(a[2],b[2],.79f)+Vector3.down*.58f;
                        var normal=Vector3.forward*(-side);
                        mesh.Quad(upperA,upperB,crestB,crestA,Vector3.up,dust,0);
                        mesh.Quad(crestA,crestB,toeB,toeA,normal,shale*.84f,0);
                        mesh.Triangle(upperB,toeB,crestB,(normal+Vector3.right).normalized,shale,0);
                        mesh.Triangle(upperA,crestA,toeA,(normal-Vector3.right).normalized,shale,0);
                        if(k==0 && clearance>6f){
                            var slab=Vector3.Lerp(a[0],b[0],.55f)+normal*(clearance*.29f);
                            slab.y=-7.98f;
                            var tip=slab+Vector3.right*(width*.39f);
                            var tail=slab-Vector3.right*(width*.44f);
                            var front=slab+normal*.9f;
                            var back=slab-normal*.57f;
                            var spine=Vector3.Lerp(tail,tip,.62f)+Vector3.up*.72f;
                            foreach(var pair in new[]{(tail,front),(front,tip),(tip,back),(back,tail)}){
                                var n=Vector3.Cross(pair.Item2-pair.Item1,spine-pair.Item1).normalized;
                                mesh.Triangle(pair.Item1,pair.Item2,spine,n.y<0?-n:n,shale,0);
                            }
                        }
                        cursor+=width+.4f+(float)breaks.NextDouble()*1.8f;
                    }
                    cursor+=7f+(float)breaks.NextDouble()*12f;
                }
            }
            return mesh;
        }

        static void MirrorBridge(CityMesh field,NavObstacle bridge)
        {
            var deck=new Color(.42f,.46f,.49f);
            var edge=new Color(.28f,.31f,.33f);
            var collar=new Color(.59f,.50f,.38f);
            field.Add(TerrainMesh.Prism(bridge,-1.2,.02),deck,2);
            // Interpolate along the actual skewed banks, including on the rotated bridge.
            var corners=bridge.Footprint.Vertices.OrderBy(p=>p.X).ThenBy(p=>p.Z).ToArray();
            Vector3 DeckCorner(NavPoint point,float height)=>new Vector3((float)point.X,height,(float)point.Z);
            Vector3 DeckPoint(float across,float along,float height){
                var south=Vector3.Lerp(DeckCorner(corners[0],height),DeckCorner(corners[2],height),across);
                var north=Vector3.Lerp(DeckCorner(corners[1],height),DeckCorner(corners[3],height),across);
                return Vector3.Lerp(south,north,along);
            }
            foreach(float side in new[]{.027f,.973f}){
                field.Strip(DeckPoint(side,0,.06f),DeckPoint(side,1,.06f),1.25f,edge,4);
                field.Strip(DeckPoint(side,.04f,.09f),DeckPoint(side,.96f,.09f),.32f,collar,4);
                foreach(float along in new[]{.12f,.5f,.88f})
                    field.Box(DeckPoint(side,along,.12f),new Vector3(.83f,.18f,.44f),collar,2);
            }
            foreach(float along in new[]{.045f,.955f})
                field.Strip(DeckPoint(.052f,along,.055f),DeckPoint(.948f,along,.055f),.66f,collar,4);
            foreach(float across in new[]{.22f,.78f}){
                var hatch=DeckPoint(across,.26f,.065f);
                field.FlatRect(hatch,2.45f,1.68f,edge,4);
                field.FlatRect(hatch+Vector3.up*.016f,2.23f,1.46f,deck,2);
                field.FlatRect(hatch+new Vector3(.67f,.036f,0),.14f,.50f,collar,4);
            }
        }

        static Mesh MirrorRelief(DeafMirrorMap map){
            var vertices=new List<Vector3>();
            var indices=new List<int>();
            var shared=new Dictionary<Vector3,int>();
            foreach(var support in map.Supports.Where(s=>s.Id=="regolith-rise")){
                var polygon=support.Bounds.Footprint;
                var face=new int[polygon.Vertices.Count];
                for(int i=0;i<face.Length;i++){
                    var p=polygon.Vertices[i];
                    float h=(float)support.HeightAt(p);
                    var v=new Vector3((float)p.X,Mathf.Abs(h)<.00001f?0:h,(float)p.Z);
                    if(!shared.TryGetValue(v,out int index)){
                        index=vertices.Count;vertices.Add(v);shared.Add(v,index);
                    }
                    face[i]=index;
                }
                for(int i=0;i<polygon.Triangles.Count;i+=3){
                    indices.Add(face[polygon.Triangles[i]]);
                    indices.Add(face[polygon.Triangles[i+2]]);
                    indices.Add(face[polygon.Triangles[i+1]]);
                }
            }
            var mesh=new Mesh{name="Continuous regolith rises"};
            mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();
            var normals=mesh.normals;
            for(int i=0;i<normals.Length;i++)if(vertices[i].y==0)normals[i]=Vector3.up;
            mesh.normals=normals;
            return mesh;
        }

        internal static AntennaCityPads.Pad[] MirrorPads(DeafMirrorMap map,PlayableProfile profile){
            return map.Sites(profile).Where(s=>s.Kind!=PlayableBuildingKind.RelayTower).Select(site=>{
                bool mine=site.Kind==PlayableBuildingKind.Mine;
                int corners=mine?6:8;
                double radius=mine?profile.MineFootprintRadius+2.8:profile.SlotRingRadius+
                    (site.Kind==PlayableBuildingKind.Headquarters?4.8:3.2);
                NavObstacle Outline(double r)=>new NavObstacle(new NavPolygon(Enumerable.Range(0,corners).Select(i=>{
                    // The mine apron presents a straight edge toward world south.
                    double angle=(i+(mine?0:.5))*2*Math.PI/corners;
                    return new NavPoint(site.Position.X+r*Math.Cos(angle),site.Position.Z+r*Math.Sin(angle));
                })));
                return new AntennaCityPads.Pad{SiteId=site.Id,Kind=site.Kind,Center=site.Position,
                    Height=map.SurfaceHeight(site.Position),Outer=Outline(radius),Inner=Outline(radius-(mine?.6:.85))};
            }).ToArray();
        }
        static void MirrorAprons(CityMesh field,DeafMirrorMap map,PlayableProfile profile){
            foreach(var pad in MirrorPads(map,profile)){
                bool mine=pad.Kind==PlayableBuildingKind.Mine;
                var fill=mine?new Color(.60f,.54f,.44f):new Color(.48f,.54f,.60f);
                field.Add(AntennaCityPads.Mesh(new[]{pad},true),mine?new Color(.46f,.43f,.39f):new Color(.42f,.44f,.46f),-2,origin:pad.Center);
                field.Add(AntennaCityPads.Mesh(new[]{pad},false),fill,-2,origin:pad.Center);
                var vertices=pad.Inner.Footprint.Vertices;
                // Flush anchors sit just inside the concrete, clear of the build sockets.
                foreach(var vertex in vertices){
                    var bolt=new Vector3((float)(vertex.X*.92+pad.Center.X*.08),.055f,(float)(vertex.Z*.92+pad.Center.Z*.08));
                    field.FlatRect(bolt,mine?.24f:.38f,mine?.24f:.38f,new Color(.34f,.35f,.35f),4);
                }
                if(mine){
                    var sill=new Vector3((float)pad.Center.X,.062f,(float)vertices.Min(v=>v.Z)+.42f);
                    field.FlatRect(sill,2.1f,.48f,new Color(.38f,.36f,.33f),4);
                    foreach(float dx in new[]{-.92f,.92f}){
                        var at=sill+Vector3.right*dx+Vector3.up*.018f;
                        field.Strip(at,at+Vector3.forward*2.5f,.20f,new Color(.67f,.64f,.56f),2);
                    }
                }else if(pad.Kind==PlayableBuildingKind.Headquarters){
                    // The corner yards enter from the map centre, along the north–south service road.
                    float side=pad.Center.Z>=0?-1:1;
                    float approach=(float)(pad.Inner.MaxZ-pad.Center.Z);
                    foreach(float dx in new[]{-2.35f,2.35f}){
                        var start=new Vector3((float)pad.Center.X+dx,.066f,(float)pad.Center.Z+side*(approach+2.0f));
                        var end=start-Vector3.forward*side*5.1f;
                        field.Strip(start,end,.40f,new Color(.83f,.83f,.73f),4);
                    }
                }
                for(int i=0;i<vertices.Count;i+=2){
                    var a=vertices[i];var b=vertices[(i+1)%vertices.Count];
                    var start=new Vector3((float)a.X,.045f,(float)a.Z);
                    var end=new Vector3((float)b.X,.045f,(float)b.Z);
                    field.Strip(Vector3.Lerp(start,end,.18f),Vector3.Lerp(start,end,.60f),mine?.18f:.22f,
                        mine?new Color(.88f,.67f,.34f):new Color(.73f,.78f,.81f),4);
                }
            }
        }

        static CityMesh MirrorRock(NavObstacle rock,int shape,int level){
            var mesh=new CityMesh();
            var points=rock.Footprint.Vertices;
            int sides=level==0?points.Count:level==1?7:6;
            var outline=Enumerable.Range(0,sides).Select(i=>{
                var p=points[i*points.Count/sides];
                return new Vector3((float)p.X,0,(float)p.Z);
            }).ToArray();
            var center=outline.Aggregate(Vector3.zero,(sum,p)=>sum+p)/sides;
            var axis=new Vector3(shape==1?-.22f:.59f,0,1).normalized;
            float Across(Vector3 p)=>Vector3.Dot(p-center,axis);
            // Clip the same footprint into two plates. A narrow seam divides the
            // low slab; the ridge meets along a single inclined crest.
            int halves=shape==2?1:2;
            for(int h=0;h<halves;h++){
                var ring=new List<Vector3>();
                float sign=h==0?-1:1, gap=shape==0?.16f:0;
                for(int i=0;i<sides;i++){
                    var p=outline[i];var q=outline[(i+1)%sides];
                    if(halves==1){ring.Add(p);continue;}
                    float dp=sign*Across(p)-gap,dq=sign*Across(q)-gap;
                    if(dp>=0)ring.Add(p);
                    if((dp<0)!=(dq<0))ring.Add(Vector3.Lerp(p,q,dp/(dp-dq)));
                }
                var core=ring.Aggregate(Vector3.zero,(sum,p)=>sum+p)/ring.Count;
                Vector3 Shoulder(Vector3 p){
                    float along=(p.x-center.x)*.07f;
                    float height=shape==0?1.03f+.34f*h+along:shape==1?
                        Mathf.Max(.56f,2.95f-.64f*Mathf.Abs(Across(p))+along):1.92f+along;
                    var v=Vector3.Lerp(p,core,shape==2?.21f:.10f);
                    if(shape==1 && Mathf.Abs(Across(p))<.001f)v=p;
                    return v+Vector3.up*height;
                }
                var crown=Shoulder(core)+Vector3.up*(shape==2?1.9f:shape==1?.05f:.10f);
                var stone=shape==0?new Color(.48f,.44f,.43f):shape==1?new Color(.40f,.39f,.43f):new Color(.46f,.44f,.45f);
                for(int i=0;i<ring.Count;i++){
                    var p=ring[i];var q=ring[(i+1)%ring.Count];
                    var u=Shoulder(p);var v=Shoulder(q);
                    var wall=Vector3.Cross(q-p,u-p).normalized;
                    if(Vector3.Dot(wall,p-core)<0)wall=-wall;
                    if(shape!=1 || Mathf.Abs(Across(p))+Mathf.Abs(Across(q))>.001f)
                        mesh.Quad(p,q,v,u,wall,stone*.78f,0);
                    var n=Vector3.Cross(v-u,crown-u).normalized;
                    mesh.Triangle(u,v,crown,n.y<0?-n:n,stone,0);
                }
            }
            return mesh;
        }

        static CityMesh MirrorFacility(NavObstacle site,Color metal,int level){
            var batch=new CityMesh();
            var at=new Vector3((float)site.CircleCenter.X,0,(float)site.CircleCenter.Z);
            if(Mathf.Abs(at.z)<80){
                if(level==0)batch.Box(at+Vector3.up*.4f,new Vector3(12.8f,.8f,9.2f),metal,2);
                else batch.FlatRect(at+Vector3.up*.1f,12.8f,9.2f,metal,2);
                var roof=new Color(.45f,.52f,.56f);
                if(level<2)batch.Box(at+new Vector3(2,3.6f,0),new Vector3(2.5f,.5f,2),metal,2);
                if(level==2)batch.FlatRect(at+Vector3.up*3.5f,10,6,roof,2);
                else batch.Box(at+Vector3.up*2,new Vector3(10,3,6),roof,2);
            }else if(at.x>0){
                MirrorReflectors(batch,at,metal,level);
            }else{
                MirrorDish(batch,at,metal,level);
            }
            return batch;
        }
        static void MirrorBeam(CityMesh mesh,Vector3 a,Vector3 b,float width,Color steel){
            var axis=(b-a).normalized;
            var u=Vector3.Cross(axis,Mathf.Abs(axis.y)>.9f?Vector3.right:Vector3.up).normalized*(width*.5f);
            var v=Vector3.Cross(axis,u);
            var corners=new[]{u+v,v-u,-u-v,u-v};
            for(int i=0;i<4;i++){
                var p=corners[i];var q=corners[(i+1)%4];
                mesh.Quad(a+p,a+q,b+q,b+p,(p+q).normalized,steel,2);
            }
        }

        static void MirrorDish(CityMesh mesh,Vector3 at,Color metal,int level){
            var steel=new Color(.30f,.33f,.35f);
            var hub=at+new Vector3(.65f,4.3f,0);
            var tilt=Quaternion.Euler(-12,0,-18);
            Vector3 Bowl(float radius,float angle){
                return hub+tilt*new Vector3(radius*Mathf.Cos(angle),.072f*radius*radius,radius*Mathf.Sin(angle));
            }
            var receiver=hub+tilt*(Vector3.up*4.3f);
            var concrete=new Color(.60f,.56f,.51f);
            mesh.Box(at+new Vector3(-4.5f,.38f,0),new Vector3(3.15f,.76f,3.72f),concrete,1);
            mesh.Box(at+new Vector3(-4.5f,1.65f,0),new Vector3(2.65f,1.8f,3.25f),new Color(.40f,.43f,.45f),2);
            mesh.Box(hub-Vector3.up*.16f,new Vector3(1.55f,.58f,1.55f),steel,2);
            foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1}){
                var foot=at+new Vector3(.65f+x*2.5f,.8f,z*2.15f);
                mesh.Box(foot-Vector3.up*.43f,new Vector3(1.68f,.86f,1.52f),concrete,1);
                MirrorBeam(mesh,foot,hub+new Vector3(x*.42f,0,z*.45f),level==2?.46f:.35f,steel);
                if(level==0)mesh.Box(foot,new Vector3(.87f,.34f,.86f),metal,2);
            }
            int count=level==0?24:level==1?12:8;
            for(int i=0;i<count;i++){
                float angle=i*2*Mathf.PI/count,next=(i+1)*2*Mathf.PI/count;
                var a=Bowl(5,angle);var b=Bowl(5,next);
                var ceramic=Color.Lerp(new Color(.78f,.76f,.72f),new Color(.91f,.89f,.84f),i%2*.48f);
                var mid=Bowl(2.7f,angle);var midNext=Bowl(2.7f,next);
                if(level==2)mesh.Triangle(hub,b,a,Vector3.Cross(b-hub,a-hub).normalized,ceramic,2);
                else{
                    mesh.Triangle(hub,midNext,mid,Vector3.Cross(midNext-hub,mid-hub).normalized,ceramic,2);
                    mesh.Quad(mid,midNext,b,a,Vector3.Cross(midNext-mid,a-mid).normalized,ceramic,2);
                }
                var back=hub-Vector3.up*.3f;
                mesh.Triangle(back,a,b,Vector3.Cross(a-back,b-back).normalized,metal,2);
            }
            for(int leg=0;leg<3;leg++)
                MirrorBeam(mesh,Bowl(4.4f,leg*2*Mathf.PI/3),receiver,.15f,steel);
            mesh.Box(receiver,new Vector3(.55f,.52f,.64f),new Color(.66f,.55f,.40f),2);
        }

        static void MirrorReflectors(CityMesh mesh,Vector3 at,Color metal,int level){
            var steel=new Color(.28f,.31f,.34f);
            var glass=new Color(.21f,.26f,.34f);
            mesh.Box(at+new Vector3(0,.40f,-3.57f),new Vector3(11.95f,.8f,1.66f),metal,1);
            mesh.Box(at+new Vector3(0,1.05f,-3.57f),new Vector3(11.6f,.50f,1.37f),new Color(.36f,.40f,.43f),2);
            if(level<2){
                foreach(float x in new[]{-5.55f,0,5.55f})
                    MirrorBeam(mesh,at+new Vector3(x,1.3f,-4.16f),at+new Vector3(x,2.12f,-4.16f),.12f,steel);
                MirrorBeam(mesh,at+new Vector3(-5.65f,2.12f,-4.16f),at+new Vector3(5.65f,2.12f,-4.16f),.14f,new Color(.68f,.55f,.32f));
            }
            for(int i=-1;i<=1;i++){
                var baseAt=at+Vector3.right*(i*3.63f);
                mesh.Box(baseAt+new Vector3(0,.40f,-1.48f),new Vector3(3.14f,.8f,3.85f),metal,1);
                mesh.Box(baseAt+new Vector3(0,1.52f,-1.48f),new Vector3(2.85f,1.44f,3.55f),new Color(.45f,.49f,.52f),2);
                var pivot=baseAt+new Vector3(0,4.43f,-.19f);
                var pin=Vector3.right*1.35f;
                mesh.Box(pivot,new Vector3(2.7f,.40f,.45f),metal,2);
                foreach(int sign in new[]{-1,1})
                    mesh.Box(pivot+sign*pin,new Vector3(.24f,.54f,.54f),new Color(.78f,.72f,.60f),2);
                MirrorBeam(mesh,baseAt+new Vector3(0,2.24f,-2.62f),pivot,.59f,steel);
                if(level<2)MirrorBeam(mesh,baseAt+new Vector3(0,2.24f,.26f),pivot,.48f,steel);
                var low=baseAt+new Vector3(0,2.43f,-2.58f);
                var high=baseAt+new Vector3(0,6.42f,2.2f);
                var edge=Vector3.right*1.16f;
                var face=Vector3.Cross(high-low,edge).normalized;
                mesh.Quad(low-edge,low+edge,high+edge,high-edge,face,glass,2);
                mesh.Quad(low-edge,high-edge,high+edge,low+edge,-face,steel,2);
                if(level<2)foreach(int side in new[]{-1,1})
                    MirrorBeam(mesh,low+edge*side,high+edge*side,.12f,metal);
                if(level==0)for(int j=1;j<4;j++){
                    var row=Vector3.Lerp(low,high,j*.25f)+face*.023f;
                    MirrorBeam(mesh,row-edge,row+edge,.044f,metal);
                }
            }
        }
        void EmitMirrorDetail(string label,System.Func<int,CityMesh> build){
            var renderer=EmitMirrorMesh(build(0),label);
            var close=renderer.GetComponent<MeshFilter>().sharedMesh;
            var normal=build(1).Finish(label);
            var far=build(2).Finish(label);
            terrainMeshes.Add(normal);terrainMeshes.Add(far);
            var detail=renderer.gameObject.AddComponent<ModelDetailLod>();
            detail.Configure(new Renderer[]{renderer},new[]{close},new[]{normal},new[]{far},close.bounds);
            renderer.gameObject.AddComponent<CrossingSceneryDetail>().Bind(detail);
            sceneryDetail.Add(detail);
        }
        MeshRenderer EmitMirrorMesh(CityMesh batch,string label){
            var mesh=batch.Finish(label);
            terrainMeshes.Add(mesh);
            var node=new GameObject(label);
            node.transform.SetParent(root,false);
            node.layer=presentationLayer;
            node.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=node.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=cityMaterial;
            return renderer;
        }
    }
}
