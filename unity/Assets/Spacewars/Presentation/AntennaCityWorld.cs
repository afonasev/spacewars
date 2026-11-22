using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        // Public minimap swatches remain neutral, never current ownership colors.
        public static readonly Color CityGround=new Color(.36f,.40f,.43f),CityBlock=new Color(.36f,.40f,.44f),CityPlaza=new Color(.44f,.48f,.52f),CityRamp=new Color(.46f,.49f,.52f),CityRoad=new Color(.29f,.33f,.36f);
        Material cityMaterial;
        IEnumerator CreateAntennaCity(AntennaCityMap map)
        {
            var art=AntennaCityArtProfile.Load();var budget=new LoadingFrameBudget();
            cityMaterial=new Material(Resources.Load<Shader>("AntennaCitySurface")){name="Nova City Night Line"};
            cityMaterial.SetTexture("_FogMask",Fog.Texture);cityMaterial.SetVector("_FogBounds",new Vector4((float)map.HalfExtent,(float)map.HalfExtent,0,0));
            cityMaterial.SetColor("_FogColor",new Color((float)profile.FogTintR,(float)profile.FogTintG,(float)profile.FogTintB));
            cityMaterial.SetVector("_Finish",new Vector4(art.textureTile,art.textureContrast,art.patchSize,art.bayWidth));cityMaterial.SetVector("_CityLights",new Vector4(art.windowEmission,art.signEmission,1,0));
            foreach(var suffix in new[]{"","B","C"}){
                string sibling=suffix==""?"":"-"+suffix.ToLowerInvariant();
                cityMaterial.SetTexture("_Concrete"+suffix,Resources.Load<Texture2D>("Environment/NaturalFrontier/Concrete"+sibling));
                cityMaterial.SetTexture("_Steel"+suffix,Resources.Load<Texture2D>("Environment/NullDock/Steel"+sibling));
            }
            MeshRenderer Emit(string name,Mesh mesh,bool flat=false){
                terrainMeshes.Add(mesh);var obj=new GameObject(name);obj.transform.SetParent(root,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=cityMaterial;
                if(flat)renderer.shadowCastingMode=ShadowCastingMode.Off;return renderer;
            }
            var ground=new CityMesh();
            foreach(var support in map.Supports.Reverse()){
                bool street=support.Id=="city/street";var vertices=support.Bounds.Footprint.Vertices;var a=vertices[0];var b=vertices[1];
                ground.Add(TerrainMesh.Prism(support.Bounds,-.4,support.Height,support.HeightAt),AntennaCityArchitecture.Graphite(street?art.roadValue:art.apronValue),street?0:1,Math.Atan2(b.Z-a.Z,b.X-a.X),a);
            }
            var pads=AntennaCityPads.Create(map,profile);
            var outposts=pads.Where(p=>p.Kind==PlayableBuildingKind.Outpost).ToArray();
            foreach(var yard in map.Yards.Where(yard=>!outposts.Any(p=>yard.Contains(p.Center)))){
                var v=yard.Footprint.Vertices;var a=v[0];var b=v[1];ground.Add(TerrainMesh.Prism(yard,0,.01),AntennaCityArchitecture.Graphite(art.yardValue),1,Math.Atan2(b.Z-a.Z,b.X-a.X),new NavPoint(v.Average(p=>p.X),v.Average(p=>p.Z)));}
            foreach(var road in map.Streets)ground.Add(TerrainMesh.Prism(road,0,.012),AntennaCityArchitecture.Graphite(art.roadValue),0);
            Emit("City textured streets and ground",ground.Finish("Nova City surfaces"));
            var markings=new CityMesh();
            AddCityRoadMarkings(markings,map);
            // Paired amber inlays frame each existing incline to the three central relay plazas.
            foreach (var ramp in map.Supports.Where(s=>s.Id.StartsWith("city/ramp/",StringComparison.Ordinal))) {
                var corners=ramp.Bounds.Footprint.Vertices;
                for(int i=0;i<corners.Count;i++){
                    var p=corners[i];var q=corners[(i+1)%corners.Count];
                    float low=(float)ramp.HeightAt(p), high=(float)ramp.HeightAt(q);
                    if(Mathf.Abs(high-low)<.01f)continue;
                    var a=new Vector3((float)p.X,low+.04f,(float)p.Z);
                    var b=new Vector3((float)q.X,high+.04f,(float)q.Z);
                    var opposite=corners[(i+2)%corners.Count];
                    var inset=new Vector3((float)(opposite.X-q.X),0,(float)(opposite.Z-q.Z)).normalized*art.parapetWidth;
                    markings.Strip(Vector3.Lerp(a,b,.06f)+inset,Vector3.Lerp(a,b,.94f)+inset,
                        art.parapetWidth*.7f,new Color(.76f,.60f,.33f),4);
                }
            }
            foreach(var pad in pads.Where(p=>p.Kind!=PlayableBuildingKind.Outpost)){
                markings.Add(AntennaCityPads.Mesh(new[]{pad},true),AntennaCityArchitecture.Graphite(.23f),2,pad.Heading,pad.Center);
                markings.Add(AntennaCityPads.Mesh(new[]{pad},false),AntennaCityArchitecture.Graphite(pad.Kind==PlayableBuildingKind.Headquarters?art.apronValue:art.apronValue*.84f),1,pad.Heading,pad.Center);
            }
            foreach(var pad in pads)CityApron(markings,pad);
            Emit("City flat markings and six themed aprons",markings.Finish("Nova City aprons"),true);
            // Keep each landmark separate and batch neighbouring quarters within each district.
            var quarters=map.Quarters.Concat(new[]{(IReadOnlyList<NavObstacle>)new[]{map.Blocks.Last()}});
            var batches=quarters.Select((quarter,index)=>{
                int district=index==map.Quarters.Count?6:District(map,quarter);
                int theme=AntennaCityArchitecture.ThemeForQuarter(map,index,district);
                var min=new Vector3((float)quarter.Min(p=>p.MinX),0,(float)quarter.Min(p=>p.MinZ));
                var max=new Vector3((float)quarter.Max(p=>p.MaxX),(float)map.BlockHeight,(float)quarter.Max(p=>p.MaxZ));
                var bounds=new Bounds((min+max)*.5f,max-min);
                float cell=64*(float)map.Scale;
                return new {theme, bounds, quarter, x=Mathf.FloorToInt(bounds.center.x/cell),z=Mathf.FloorToInt(bounds.center.z/cell)};
            }).GroupBy(q=>(q.theme,q.x,q.z));
            foreach(var batch in batches){
                int theme=batch.Key.theme;
                var shells=batch.SelectMany(q=>AntennaCityArchitecture.JoinConvexNeighbours(q.quarter)).ToArray();
                var tiers=new Mesh[3];
                for(int level=0;level<3;level++){tiers[level]=AntennaCityArchitecture.Quarter(shells,map.BlockHeight,theme,level,art);if(level>0)terrainMeshes.Add(tiers[level]);}
                var renderer=Emit($"City {AntennaCityArchitecture.Themes[theme]} {batch.Key.x}/{batch.Key.z}",tiers[0]);
                var detail=renderer.gameObject.AddComponent<ModelDetailLod>();detail.Configure(new Renderer[]{renderer},new[]{tiers[0]},new[]{tiers[1]},new[]{tiers[2]},tiers[0].bounds);
                detail.ConfigureSelectionBounds(batch.Select(q=>q.bounds).ToArray());
                renderer.gameObject.AddComponent<CrossingSceneryDetail>().Bind(detail);sceneryDetail.Add(detail);
                if(budget.Expired){yield return null;budget.Restart();}
            }
        }
        static int District(AntennaCityMap map, IReadOnlyList<NavObstacle> quarter)
        {
            // Area-weighted centroid stays stable when clipping splits a lot into fragments.
            double x=0, z=0, area=0;
            foreach(var piece in quarter) {
                var points=piece.Footprint.Vertices;
                var a=points[0];
                for(int i=1;i<points.Count-1;i++){
                    var b=points[i];var c=points[i+1];
                    double weight=Math.Abs((b.X-a.X)*(c.Z-a.Z)-(b.Z-a.Z)*(c.X-a.X));
                    x+=weight*(a.X+b.X+c.X)/3; z+=weight*(a.Z+b.Z+c.Z)/3; area+=weight;
                }
            }
            x/=area; z/=area;
            return Enumerable.Range(0,6).OrderBy(i=>{
                var home=map.Headquarters((PlayableOwner)i);
                return (x-home.X)*(x-home.X)+(z-home.Z)*(z-home.Z);
            }).First();
        }

        // Authoritative road rectangles define every painted route. Cut exact intervals at
        // intersecting streets and yards instead of drawing continuous lines across junctions.
        public static Mesh CityRoadMarkings(AntennaCityMap map)
        {
            var batch=new CityMesh();
            AddCityRoadMarkings(batch,map);
            return batch.Finish("Nova City lanes and roadside utilities");
        }
        static void AddCityRoadMarkings(CityMesh batch,AntennaCityMap map)
        {
            var paint=new Color(.84f,.87f,.88f);
            var supports=map.Supports.Where(s=>s.Id!="city/street").Select(s=>s.Bounds).ToArray();
            for(int index=0;index<map.Streets.Count;index++){
                var road=map.Streets[index];var v=road.Footprint.Vertices;
                var a=new Vector3((float)((v[0].X+v[3].X)/2),.019f,(float)((v[0].Z+v[3].Z)/2));
                var b=new Vector3((float)((v[1].X+v[2].X)/2),.019f,(float)((v[1].Z+v[2].Z)/2));
                float width=(float)Math.Sqrt(Math.Pow(v[0].X-v[3].X,2)+Math.Pow(v[0].Z-v[3].Z,2));
                var d=(b-a).normalized;var n=new Vector3(d.z,0,-d.x);a+=d*width*.5f;b-=d*width*.5f;
                float length=Vector3.Distance(a,b);
                var cuts=map.Yards.Concat(supports).Concat(map.Streets.Where((_,i)=>i!=index)).ToArray();
                void Segment(Vector3 lo,Vector3 hi,float thickness,int role=4){
                    var intervals=new List<Vector2>{new Vector2(0,1)};
                    foreach(var cut in cuts){
                        double enter=0,leave=1;bool hit=true;var points=cut.Footprint.Vertices;
                        // Half paint width expands the exclusion so no strip corner leaks across a boundary.
                        for(int k=0;k<points.Count;k++){
                            var p=points[k];var q=points[(k+1)%points.Count];double ex=q.X-p.X,ez=q.Z-p.Z;
                            double offset=thickness*.5*Math.Sqrt(ex*ex+ez*ez);
                            double f0=ex*(lo.z-p.Z)-ez*(lo.x-p.X)+offset;
                            double f1=ex*(hi.z-p.Z)-ez*(hi.x-p.X)+offset;
                            if(f0<0&&f1<0){hit=false;break;}
                            if(f0<0)enter=Math.Max(enter,-f0/(f1-f0));else if(f1<0)leave=Math.Min(leave,f0/(f0-f1));
                        }
                        if(!hit||enter>=leave)continue;
                        var next=new List<Vector2>();foreach(var span in intervals){if(leave<=span.x||enter>=span.y){next.Add(span);continue;}if(enter>span.x)next.Add(new Vector2(span.x,(float)enter));if(leave<span.y)next.Add(new Vector2((float)leave,span.y));}intervals=next;
                    }
                    foreach(var span in intervals){
                        var start=Vector3.Lerp(lo,hi,span.x);
                        var end=Vector3.Lerp(lo,hi,span.y);
                        float run=Vector3.Distance(start,end);
                        if(run<.001f)continue;
                        var half=n*(thickness*.5f);
                        var color=role==4?paint:AntennaCityArchitecture.Graphite(.43f);
                        batch.Quad(start-half,end-half,end+half,start+half,Vector3.up,color,role,0,new Vector2(run,1));
                    }
                }
                // Existing authored paint proportions; route angles and cutoffs are derived from geometry.
                if(width >= (map.MainStreetWidth+map.SideStreetWidth)*map.Scale*.5)
                    for(float t=3;t<length-3;t+=7)Segment(a+d*t,a+d*Mathf.Min(t+2.8f,length-2),.23f);
                foreach(int side in new[]{-1,1}){
                    var curb=n*(side*width*.46f);
                    Segment(a+curb,b+curb,Mathf.Min(.78f,width*.075f),side<0?9:10);
                }
                // A single painted edge matches the existing lane plan.
                Segment(a+n*width*.40f,b+n*width*.40f,.19f);
            }
        }
        static void CityApron(CityMesh batch,AntennaCityPads.Pad pad)
        {
            var points=pad.Inner.Footprint.Vertices;var center=new Vector3((float)points.Average(p=>p.X),(float)pad.Height+.038f,(float)points.Average(p=>p.Z));
            if(pad.Kind==PlayableBuildingKind.Outpost){
                var chalk=new Color(.63f,.68f,.69f);
                var amber=new Color(.86f,.58f,.29f);
                for(int i=0;i<points.Count;i++){
                    Vector3 Vertex(int k)=>new Vector3((float)points[k].X,center.y,(float)points[k].Z);
                    var corner=Vertex(i);
                    var along=(Vertex((i+1)%4)-corner).normalized;
                    var across=(Vertex((i+3)%4)-corner).normalized;
                    float arm=(float)pad.InnerRadius*.18f;
                    batch.Strip(corner,corner+along*arm,.17f,chalk,4);
                    batch.Strip(corner,corner+across*arm,.17f,chalk,4);
                    if(i%2==0)batch.FlatRect(corner+(along+across)*.43f,.25f,.25f,amber,5);
                }
                return;
            }
            int theme=(pad.SiteId-1)%6;var accent=AntennaCityArchitecture.Accent(theme);var paint=new Color(.69f,.72f,.74f);
            for(int i=0;i<points.Count;i++){
                var a=new Vector3((float)points[i].X,center.y,(float)points[i].Z);var b=new Vector3((float)points[(i+1)%points.Count].X,center.y,(float)points[(i+1)%points.Count].Z);
                a=Vector3.Lerp(a,center,.04f);b=Vector3.Lerp(b,center,.04f);batch.Strip(Vector3.Lerp(a,b,.08f),Vector3.Lerp(a,b,.28f),.12f,accent,5);
                batch.Strip(Vector3.Lerp(a,b,.72f),Vector3.Lerp(a,b,.92f),.12f,accent,5);
            }
            if(pad.Kind!=PlayableBuildingKind.Headquarters)return;
            float r=(float)pad.InnerRadius;
            var right=new Vector3((float)Math.Cos(pad.Heading),0,(float)Math.Sin(pad.Heading));
            var forward=new Vector3(-right.z,0,right.x);
            Vector3 Offset(float x,float z)=>right*x+forward*z;
            void Rect(Vector3 at,float width,float depth){var x=right*width*.5f;var z=forward*depth*.5f;batch.Quad(at-x-z,at+x-z,at+x+z,at-x+z,Vector3.up,accent,4,0,new Vector2(width,depth));}
            // Every theme stays flush and keeps the middle of the construction yard calm.
            var icon=center+Offset(-r+3,-r+3);
            int bays=theme==0?5:theme==3?4:3;
            for(int i=0;i<bays;i++){
                var p=center+Offset(-r+7+i*3,-r+2);
                batch.Strip(p,p+forward*2,.12f,paint,4);
                batch.Strip(p,p+right*2,.12f,paint,4);
            }
            if(theme==0){foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})Rect(icon+Offset(x*.6f,y*.6f),.8f,.8f);}
            else if(theme==1){for(int k=1;k<=3;k++){float s=k*.5f;var a=icon+forward*s;var b=icon+right*s;var c=icon-forward*s;var d=icon-right*s;batch.Strip(a,b,.12f,accent,4);batch.Strip(b,c,.12f,accent,4);batch.Strip(c,d,.12f,accent,4);batch.Strip(d,a,.12f,accent,4);}}
            else if(theme==2){batch.Strip(icon+Offset(.8f,1.5f),icon+Offset(-.4f,.1f),.3f,accent,4);batch.Strip(icon+Offset(-.4f,.1f),icon+Offset(.4f,-.1f),.3f,accent,4);batch.Strip(icon+Offset(.4f,-.1f),icon+Offset(-.8f,-1.5f),.3f,accent,4);}
            else if(theme==3){foreach(int side in new[]{-1,1})Rect(icon+right*side*.7f,.18f,3);for(int k=-1;k<=1;k++)Rect(icon+forward*k,1.8f,.16f);}
            else if(theme==4){batch.Strip(icon-forward*1.3f,icon+forward*.7f,.35f,accent,4);batch.Strip(icon+forward*.7f,icon+Offset(-.8f,1.4f),.3f,accent,4);batch.Strip(icon+forward*.7f,icon+Offset(.8f,1.4f),.3f,accent,4);}
            else{Rect(icon,.65f,2.8f);Rect(icon,2.8f,.65f);}
        }
    }
}
