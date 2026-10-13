using System;
using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Inspection geometry, deliberately independent from live match actors/selection.
    public sealed class NereidaPreview : MonoBehaviour
    {
        public NereidaMap Map { get; private set; }
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private Material material;
        private Material iceMaterial,snowMaterial,rockMaterial,steelMaterial,waterMaterial;
        public NereidaArtProfile Art { get; private set; }
        private static readonly Color Snow=new Color(.78f,.86f,.89f), Ice=new Color(.19f,.55f,.65f),
            Road=new Color(.17f,.23f,.28f), Water=new Color(.025f,.10f,.17f), Amber=new Color(1,.62f,.22f);
        public static NereidaProfileData LoadProfile() => JsonUtility.FromJson<NereidaProfileData>(Resources.Load<TextAsset>("NereidaProfile").text);
        public void Build(NereidaProfileData data)
        {
            if(Map!=null)throw new InvalidOperationException("Create a fresh preview for each profile.");
            Map=new NereidaMap(data);
            Art=NereidaArtProfile.Load();
            iceMaterial=Art.Material("Environment/NaturalFrontier/Rock",new Vector3(.13677501f,.13289979f,.14225178f),0);
            snowMaterial=Art.Material("Environment/NaturalFrontier/Concrete",new Vector3(.38497328f,.37142344f,.37949659f),1);
            rockMaterial=Art.Material("Environment/NaturalFrontier/Rock",new Vector3(.13677501f,.13289979f,.14225178f),2);
            steelMaterial=Art.Material("Environment/NaturalFrontier/Steel",new Vector3(.13237418f,.12266814f,.12128175f),3);
            waterMaterial=Art.Material("Environment/NaturalFrontier/Rock",new Vector3(.13677501f,.13289979f,.14225178f),4);
            foreach(var m in new[]{iceMaterial,snowMaterial,rockMaterial,steelMaterial,waterMaterial})owned.Add(m);
            material=new Material(Shader.Find("Universal Render Pipeline/Lit"));owned.Add(material);
            material.SetFloat("_Smoothness",.18f);
            Prism("Open water (not traversable)",Map.Box(0,-144,-144,144,144),-Map.WaterDepth-.1,-Map.WaterDepth,Water);
            foreach(var s in Map.Supports) {
                bool ramp=System.Linq.Enumerable.Contains(Map.Ramps,s);
                Color color=s.IsBridge?Road:s.Id.StartsWith("home")?Snow:s.Id.StartsWith("terrace")?new Color(.42f,.56f,.60f):Ice;
                if(ramp)color=Road;
                // Bridge bottom is the deck underside; never extrude a wall through the underpass.
                double bottom=s.IsBridge?Map.MiddleHeight-Map.DeckThickness:-Map.WaterDepth;
                Prism(s.Id,s.Bounds,bottom,s.Height,color,s.HeightAt);
            }
            for(int q=0;q<4;q++) {
                foreach(double edge in new[]{29.0,55.0}) {
                    Bar("Bridge rail",q,0,edge,60,.4,.5,Map.MiddleHeight+.8,Road);
                }
                foreach(double x in new[]{-28.0,28.0})foreach(double z in new[]{29.0,55.0}) {
                    Bar("Bridge post",q,x,z,1.2,1.2,2.3,Map.MiddleHeight+1.15,Road);
                    Bar("Bridge amber beacon",q,x,z,.6,.6,.4,Map.MiddleHeight+2.5,Amber);
                }
                BuildStationComposition(q);
                BuildIceFractures(q);
                BuildSnowDrifts(q);
                foreach(double z in new[]{74.0,116.0}) {
                    Bar("Road lamp",q,32,z,.5,.5,4,Map.MiddleHeight+2,Road);
                    Bar("Road lamp head",q,32,z,1.3,1.3,.4,Map.MiddleHeight+4,Amber);
                }
            }
            var profile=PlayableProfile.Default;
            foreach(var site in Map.Sites(profile)) {
                double height=site.Kind==PlayableBuildingKind.Headquarters?Map.HomeHeight:site.Kind==PlayableBuildingKind.Outpost?Map.MiddleHeight:site.Id<=12?Map.HomeHeight:site.Id<=16?Map.MiddleHeight:0;
                Color color=site.Kind==PlayableBuildingKind.Mine?new Color(.93f,.74f,.32f):site.Kind==PlayableBuildingKind.Outpost?Amber:Snow;
                double radius=site.Kind==PlayableBuildingKind.Mine?profile.MineFootprintRadius:profile.SlotRingRadius+profile.FactoryFootprintRadius;
                Disc("Reserved site "+site.Id,site.Position,height+.15,radius,new Color(.22f,.29f,.33f));
                var prefix=site.Kind==PlayableBuildingKind.Headquarters?"S":site.Kind==PlayableBuildingKind.Outpost?"F":"M";
                int number=site.Kind==PlayableBuildingKind.Headquarters?site.Id:site.Kind==PlayableBuildingKind.Outpost?site.Id-4:site.Id-8;
                Label(prefix+number,site.Position,height+.4,color);
                foreach(var slot in site.Slots)Disc("Factory reserve "+site.Id+"/"+slot.Id,slot.Position,height+.22,profile.FactoryFootprintRadius,new Color(.41f,.51f,.54f));
            }
        }
        private void BuildStationComposition(int q)
        {
            // Distinct authored compounds; all modules stay inside the common home reserve
            // and off the shared terrace lanes, ramps, landing approach and construction envelope.
            double labX,labZ,labW,labD,serviceX,serviceZ,serviceW,serviceD,padX,padZ;
            switch(q) {
                case 0: labX=125;labZ=114;labW=8;labD=26;serviceX=80;serviceZ=125;serviceW=12;serviceD=7;padX=82;padZ=82;break;
                case 1: labX=117;labZ=124;labW=15;labD=14;serviceX=126;serviceZ=82;serviceW=7;serviceD=14;padX=83;padZ=85;break;
                case 2: labX=124;labZ=117;labW=10;labD=22;serviceX=83;serviceZ=126;serviceW=16;serviceD=6;padX=85;padZ=82;break;
                default:labX=116;labZ=123;labW=18;labD=12;serviceX=126;serviceZ=85;serviceW=6;serviceD=15;padX=83;padZ=85;break;
            }
            Bar("Station laboratory",q,labX,labZ,labW,labD,7,Map.HomeHeight+3.5,Road);
            // Authored architecture stays wholly above the existing laboratory footprint.
            // These shapes identify the four compounds without narrowing any ground route.
            switch(q) {
                case 0:
                    Bar("Antenna spine",q,labX,labZ,2,18,2,Map.HomeHeight+8,Snow);
                    Bar("Antenna mast",q,labX,labZ+6,.8,.8,7,Map.HomeHeight+11,Road);
                    Bar("Antenna crossbar",q,labX,labZ+6,6,.6,.6,Map.HomeHeight+13,Road);break;
                case 1:
                    Bar("Observatory plinth",q,labX,labZ,10,10,2,Map.HomeHeight+8,Snow);
                    var dome=GameObject.CreatePrimitive(PrimitiveType.Sphere);dome.name="Observatory dome";
                    dome.transform.SetParent(transform,false);var dp=Map.Point(q,labX,labZ);
                    dome.transform.localPosition=new Vector3((float)dp.X,(float)Map.HomeHeight+9,(float)dp.Z);
                    dome.transform.localScale=new Vector3((float)Map.Scale*8,6,(float)Map.Scale*8);
                    DestroyImmediate(dome.GetComponent<Collider>());Paint(dome.GetComponent<Renderer>(),Road);break;
                case 2:
                    foreach(double z in new[]{labZ-5,labZ+5}) {
                        Bar("Cooling stack",q,labX,z,6,5,4,Map.HomeHeight+9,Snow);
                        Bar("Cooling cap",q,labX,z,5,4,.3,Map.HomeHeight+11.2,Road);
                    }break;
                default:
                    Bar("Stepped roof annex",q,labX-4,labZ,8,8,4,Map.HomeHeight+9,Snow);
                    Bar("Roof array",q,labX+5,labZ,5,9,.5,Map.HomeHeight+7.5,Amber);break;
            }
            FinishLaboratory(q,labX,labZ,labW,labD);
            Bar("Station service module",q,serviceX,serviceZ,serviceW,serviceD,4,Map.HomeHeight+2,new Color(.62f,.32f,.15f));
            Bar("Landing pad",q,padX,padZ,15,15,.16,Map.HomeHeight+.1,Road);
            Label("H",Map.Point(q,padX,padZ),Map.HomeHeight+.35,Amber);
            Bar("Laboratory window",q,labX-labW*.51,labZ-labD*.24,.15,Math.Min(4,labD*.35),1.2,Map.HomeHeight+3,Amber);
            Bar("Laboratory window",q,labX-labW*.51,labZ+labD*.25,.15,Math.Min(4,labD*.35),1.2,Map.HomeHeight+3,Amber);
            // The gate cabin marks the outside shoulder of the same broad terrace approach.
            Bar("Checkpoint shelter",q,51,120,2,3,3,Map.MiddleHeight+1.5,Road);
        }
        private void FinishLaboratory(int q,double x,double z,double w,double d)
        {
            // Architectural coordinates stay on/inside the existing laboratory footprint.
            // Panel pitch and pipe radii are authored model dimensions, not gameplay tuning.
            for(double dz=-d*.4;dz<=d*.4;dz+=3) {
                Bar("Finish/facade rib",q,x-w*.48,z+dz,.24,.24,6,Map.HomeHeight+3,Road);
                Bar("Finish/facade rib",q,x+w*.48,z+dz,.24,.24,6,Map.HomeHeight+3,Road);
                Bar("Finish/window frame",q,x-w*.47,z+dz,.16,2.2,1.5,Map.HomeHeight+4.5,Snow);
                Bar("Laboratory glazed panel",q,x-w*.485,z+dz,.16,1.9,1.1,Map.HomeHeight+4.5,new Color(.23f,.69f,.72f));
            }
            Bar("Finish/roof snow cap",q,x,z,w*.96,d*.96,.18,Map.HomeHeight+7.15,Snow);
            for(double dx=-w*.4;dx<=w*.4;dx+=2.8)
                Bar("Finish/roof standing seam",q,x+dx,z,.16,d*.94,.16,Map.HomeHeight+7.3,Road);
            Bar("Finish/loading door",q,x,z-d*.49,w*.5,.18,3.6,Map.HomeHeight+1.8,new Color(.36f,.44f,.49f));
            Bar("Loading hazard header",q,x,z-d*.485,w*.55,.2,.35,Map.HomeHeight+3.85,Amber);
            for(int i=0;i<3;i++)Bar("Finish/utility conduit",q,x+w*.32,z-d*.20+i*.8,.32,.45,4.5,Map.HomeHeight+2.5,Road);
            // Different flat roof deposits avoid a repeated clean cuboid at every start.
            Bar("Finish/roof equipment",q,x-w*.18,z+(q%2==0?d*.25:-d*.25),w*.38,d*.22,.8,Map.HomeHeight+7.7,new Color(.42f,.50f,.54f));
        }
        private void BuildSnowDrifts(int q)
        {
            // Flush artwork: it neither raises terrain nor introduces colliders/blockers.
            // q-specific wind tongues interrupt the repeated lower-arm silhouette visually.
            double[][] shapes;
            switch(q) {
                case 0: shapes=new[]{new double[]{-15,64,-10,68,-6,77,-5,90,-9,86,-13,73},new double[]{8,106,15,101,15,118,11,132,7,127,10,116}};break;
                case 1: shapes=new[]{new double[]{-15,102,-8,109,1,115,9,126,3,127,-6,118,-15,111},new double[]{9,65,15,60,15,82,11,91,7,87,10,77}};break;
                case 2: shapes=new[]{new double[]{-15,62,-6,68,4,79,7,92,3,96,0,85,-8,77,-15,74},new double[]{-13,119,-6,126,1,136,-9,132,-15,124}};break;
                default:shapes=new[]{new double[]{-15,87,-9,80,-4,73,2,67,7,69,1,78,-4,82,-10,92},new double[]{6,102,15,107,15,128,10,135,8,124,10,113}};break;
            }
            for(int j=0;j<shapes.Length;j++) {
                var points=new List<NavPoint>();var xy=shapes[j];for(int i=0;i<xy.Length;i+=2)points.Add(Map.Point(q,xy[i],xy[i+1]));
                Prism("Snow drift/"+q+"/"+j,new NavObstacle(new NavPolygon(points)),.015,.025,Snow);
            }
        }
        private void BuildIceFractures(int q)
        {
            // Thin, authored ice seams: no collider, elevation barrier or dark-water cutout.
            // The irregular polygons are surface artwork coordinates, not a navigation mask.
            double[] seam;
            switch(q) {
                case 0: seam=new double[]{-13,57, -4,69, -7,83, 7,102, 12,122, 11,122, 6,102, -8,83, -5,69, -14,57};break;
                case 1: seam=new double[]{12,58, 4,73, 8,90, -5,108, -10,130, -11,130, -6,108, 7,90, 3,73, 11,58};break;
                case 2: seam=new double[]{-12,63, 5,79, 1,99, 13,116, 12,117, 0,99, 4,79, -13,64};break;
                default:seam=new double[]{10,63, -5,84, -3,101, 8,113, 3,132, 2,132, 7,113, -4,101, -6,84, 9,62};break;
            }
            var points=new List<NavPoint>();
            for(int i=0;i<seam.Length;i+=2)points.Add(Map.Point(q,seam[i],seam[i+1]));
            Prism("Ice fracture/"+q,new NavObstacle(new NavPolygon(points)),.06,.08,new Color(.30f,.66f,.73f));
        }
        private void Prism(string name,NavObstacle shape,double bottom,double top,Color color,Func<NavPoint,double> height=null)
        {
            var mesh=TerrainMesh.Prism(shape,bottom,top,height);owned.Add(mesh);
            var obj=new GameObject(name);obj.transform.SetParent(transform,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=obj.AddComponent<MeshRenderer>();Paint(r,color);
            if(name=="Open water (not traversable)")r.sharedMaterial=waterMaterial;
            else if(name.StartsWith("home/"))r.sharedMaterial=snowMaterial;
            else if(name.StartsWith("terrace/"))r.sharedMaterial=rockMaterial;
            else if(name.StartsWith("ice-")||name.StartsWith("Ice fracture/"))r.sharedMaterial=iceMaterial;
            else if(name.StartsWith("Snow drift/")) {
                // Exact same world albedo as underlying ice, fading only the authored snow mask.
                var drift=new Material(iceMaterial);owned.Add(drift);drift.SetFloat("_Role",5);
                var v=shape.Footprint.Vertices;var points=new Vector4[16];
                for(int i=0;i<v.Count;i++)points[i]=new Vector4((float)v[i].X,(float)v[i].Z,0,0);
                drift.SetVectorArray("_DriftPoints",points);drift.SetFloat("_DriftCount",v.Count);r.sharedMaterial=drift;
                var b=new MaterialPropertyBlock();b.SetColor("_BaseColor",Ice);r.SetPropertyBlock(b);
            }
            else r.sharedMaterial=steelMaterial;
        }
        private void Paint(Renderer renderer,Color color)
        {renderer.sharedMaterial=material;var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);renderer.SetPropertyBlock(block);}
        private void Bar(string name,int q,double x,double z,double width,double depth,double height,double y,Color color)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.transform.SetParent(transform,false);
            var p=Map.Point(q,x,z);obj.transform.localPosition=new Vector3((float)p.X,(float)y,(float)p.Z);
            obj.transform.localScale=new Vector3((float)(width*Map.Scale),(float)height,(float)(depth*Map.Scale));obj.transform.localRotation=Quaternion.Euler(0,-q*90,0);
            DestroyImmediate(obj.GetComponent<Collider>());Paint(obj.GetComponent<Renderer>(),color);
            if(name=="Station laboratory"||name=="Station service module"||name=="Landing pad"||name.StartsWith("Finish/"))obj.GetComponent<Renderer>().sharedMaterial=steelMaterial;
        }
        private void Disc(string name,NavPoint p,double y,double radius,Color color)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cylinder);obj.name=name;obj.transform.SetParent(transform,false);
            obj.transform.localPosition=new Vector3((float)p.X,(float)y,(float)p.Z);obj.transform.localScale=new Vector3((float)radius*2,.06f,(float)radius*2);
            DestroyImmediate(obj.GetComponent<Collider>());Paint(obj.GetComponent<Renderer>(),color);
        }
        private void Label(string text,NavPoint p,double y,Color color)
        {
            var obj=new GameObject(text);obj.transform.SetParent(transform,false);obj.transform.localPosition=new Vector3((float)p.X,(float)y,(float)p.Z);obj.transform.localRotation=Quaternion.Euler(90,0,0);
            var label=obj.AddComponent<TextMesh>();label.text=text;label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.fontSize=64;label.characterSize=.8f;label.color=color;
        }
        private void OnDestroy(){foreach(var obj in owned)if(obj)DestroyImmediate(obj);owned.Clear();}
    }
}
