using Spacewars.Simulation;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        internal static readonly Color BankColor=new Color(.55f,.56f,.49f),WaterColor=new Color(.08f,.36f,.49f),BridgeColor=new Color(.73f,.71f,.60f),RockColor=new Color(.30f,.34f,.36f);
        private ThreeCrossingsMaterials surfaceMaterials;
        public string SurfaceRevision=>surfaceMaterials==null?"none":surfaceMaterials.Profile.id+"@"+surfaceMaterials.Profile.revision;
        public string EnvironmentRevision=>surfaceMaterials==null?"none":surfaceMaterials.Art.id+"@"+surfaceMaterials.Art.revision;
        private System.Collections.IEnumerator CreateThreeCrossings(ThreeCrossingsMap map)
        {
            var budget=new LoadingFrameBudget();
            surfaceMaterials=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,material,profile,staged:true);
            yield return surfaceMaterials.Preparation;
            foreach(var support in map.Supports)
            {
                if(budget.Expired){yield return null;budget.Restart();}
                double depth=support.IsBridge?map.DeckThickness*.18:map.WaterDepth;
                MapPrism(support.Id,support.Bounds,-depth,support.Height,support.IsBridge||support.Id.StartsWith("platform-")?BridgeColor:BankColor,support.HeightAt,support.IsBridge||support.Id.StartsWith("platform-")?3:1);
                if(support.IsBridge)
                {
                    MapPrism("River below bridge",support.Bounds,-map.WaterDepth-map.DeckThickness,-map.WaterDepth,WaterColor,null,5);
                    foreach(int side in new[]{-1,1})
                    {
                    var steel=CorporateOutpostModules.BridgeSteelwork(support,map,side,surfaceMaterials.Profile);terrainMeshes.Add(steel);
                    var railing=new GameObject("Bridge metal lattice");railing.transform.SetParent(root,false);
                    railing.AddComponent<MeshFilter>().sharedMesh=steel;var renderer=railing.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                    var block=new MaterialPropertyBlock();block.SetFloat("_SurfaceRole",3);block.SetFloat("_Dressing",4);block.SetColor("_BaseColor",Color.white);renderer.SetPropertyBlock(block);
                    }
                }
            }
            foreach(var water in map.Water)MapPrism("River — no support",water,-map.WaterDepth-map.DeckThickness,-map.WaterDepth,WaterColor,null,5);
            foreach(var rock in map.Solids)
            {
                if(budget.Expired){yield return null;budget.Restart();}
                if(IsPlatformFence(rock))
                {
                    float deck=(float)map.Supports.First(s=>s.Id.StartsWith("platform-")&&!s.Id.EndsWith("-ramp")).Height;
                    MapPrism("Platform steel foundation",rock,rock.Bottom,deck,BridgeColor,null,3);
                    var mesh=PlatformFence(rock,deck,surfaceMaterials.Profile);terrainMeshes.Add(mesh);
                    var fence=new GameObject("Platform steel fence");fence.transform.SetParent(root,false);
                    fence.AddComponent<MeshFilter>().sharedMesh=mesh;fence.AddComponent<MeshRenderer>().sharedMaterial=material;
                    var block=new MaterialPropertyBlock();block.SetFloat("_SurfaceRole",3);block.SetFloat("_Dressing",4);block.SetColor("_BaseColor",Color.white);
                    fence.GetComponent<Renderer>().SetPropertyBlock(block);
                    continue;
                }
                MapPrism("Solid rock / platform edge",rock,rock.Bottom,rock.Top,double.IsNaN(rock.Top)?RockColor:rock.Top<map.WaterDepth?BridgeColor:rock.Bottom>0?Color.Lerp(RockColor,BridgeColor,.2f):RockColor,null,rock.Top<map.WaterDepth?2:4);
            }
            var details=ThreeCrossingsDetails.Plan(map,profile,surfaceMaterials);
            var serviceMesh=ServiceEquipmentKit.Build();terrainMeshes.Add(serviceMesh);
            var servicePlacements=ThreeCrossingsServiceScenery.Plan(map,profile,surfaceMaterials,details.FindAll(p=>p.Kind!="Boulder"),serviceMesh.bounds);
            details=ThreeCrossingsServiceScenery.ReserveServicePockets(details,servicePlacements,serviceMesh.bounds,profile,surfaceMaterials);
            foreach(var placement in servicePlacements)
            {
                if(budget.Expired){yield return null;budget.Restart();}
                var obj=new GameObject("Detail — parked service equipment");obj.transform.SetParent(root,false);obj.transform.localPosition=placement.Position;obj.transform.localRotation=Quaternion.Euler(0,placement.Rotation,0);obj.transform.localScale=Vector3.one*placement.Scale;
                obj.AddComponent<MeshFilter>().sharedMesh=serviceMesh;var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                var block=new MaterialPropertyBlock();block.SetFloat("_SurfaceRole",3);block.SetFloat("_Dressing",2);block.SetColor("_BaseColor",Color.white);renderer.SetPropertyBlock(block);
            }
            yield return surfaceMaterials.PrepareHangarAccesses(map,material);
            yield return surfaceMaterials.PrepareWeathering(map,details,material);
            foreach(int group in new[]{0,1,2,3,4})
            {
                if(budget.Expired){yield return null;budget.Restart();}
                bool stones=group==0;
                var mesh=ThreeCrossingsDetails.Build(details,surfaceMaterials.Art,stones,group==2,surfaceMaterials.Profile.tractorScale,group==3,surfaceMaterials.Profile.industrialScale,group==4,surfaceMaterials.Profile.mineUtilityScale);terrainMeshes.Add(mesh);
                var obj=new GameObject(stones?"Detail — geological debris":group==2?"Detail — heavy tractor wrecks":group==3?"Detail — corporate extraction scenery":group==4?"Detail — perimeter building compounds":"Detail — service infrastructure");obj.transform.SetParent(root,false);
                obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=material;
                var properties=new MaterialPropertyBlock();properties.SetFloat("_SurfaceRole",stones?4:3);properties.SetFloat("_Dressing",group+1);
                properties.SetColor("_BaseColor",Color.white);obj.GetComponent<Renderer>().SetPropertyBlock(properties);
            }
        }
        // The authored contour builder uses ground-based quads for platform/ramp walls;
        // geological volumes begin below ground or above their parent mass.
        public static bool IsPlatformFence(NavObstacle wall)=>wall.Bottom==0&&!double.IsNaN(wall.Top)&&wall.Polygon!=null&&wall.Polygon.Vertices.Count==4;
        public static Mesh PlatformFence(NavObstacle wall,float deck,ThreeCrossingsSurfaceProfile profile)
        {
            var points=wall.Polygon.Vertices.Select(p=>new Vector3((float)p.X,0,(float)p.Z)).ToArray();
            int edge=0;
            for(int i=1;i<4;i++)if((points[(i+1)%4]-points[i]).sqrMagnitude<(points[(edge+1)%4]-points[edge]).sqrMagnitude)edge=i;
            var a=(points[edge]+points[(edge+1)%4])*.5f;
            var z=(points[(edge+2)%4]+points[(edge+3)%4])*.5f;
            float width=Mathf.Min(profile.platformFenceWidth,(points[(edge+1)%4]-points[edge]).magnitude*.5f);
            var direction=(z-a).normalized;a+=direction*width;z-=direction*width;
            float length=(z-a).magnitude,height=(float)wall.Top-deck;
            int panels=Mathf.Max(1,Mathf.CeilToInt(length/profile.platformFenceSpacing));
            var local=new EnvironmentDetails.Builder();var tint=new Color(.43f,.50f,.55f);
            foreach(float y in new[]{width*.5f,height-width*.5f})local.Box(new Vector3(0,y,0),new Vector3(length,width,width),tint);
            for(int i=0;i<=panels;i++)local.Box(new Vector3(-length*.5f+length*i/panels,height*.5f,0),new Vector3(width,height,width),tint);
            // Closely spaced vertical bars read as a fence at gameplay zoom, with open gaps.
            int bars=Mathf.Max(1,Mathf.CeilToInt(length/(profile.platformFenceSpacing*.2f)));
            for(int i=1;i<bars;i++)local.Box(new Vector3(-length*.5f+length*i/bars,height*.5f,0),new Vector3(width*.5f,height-width,width*.5f),tint);
            var source=local.Mesh("Platform fence section");var world=new EnvironmentDetails.Builder();
            world.Append(source,Matrix4x4.TRS((a+z)*.5f+Vector3.up*deck,Quaternion.FromToRotation(Vector3.right,direction),Vector3.one));
            Object.Destroy(source);return world.Mesh("Platform steel fence");
        }
        private readonly System.Collections.Generic.List<Mesh> terrainMeshes=new System.Collections.Generic.List<Mesh>();
        // Surface role IDs are a fixed shader contract: 0 unchanged, 1 earth, 2 concrete, 3 steel, 4 rock, 5 water.
        private void MapPrism(string name,NavObstacle shape,double bottom,double top,Color color,System.Func<NavPoint,double> height=null,int surfaceRole=0)
        {
            var obj=new GameObject(name);obj.transform.SetParent(root,false);var mesh=surfaceRole==4?ReadableRock(shape,bottom,top):TerrainMesh.Prism(shape,bottom,top,height);terrainMeshes.Add(mesh);obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=material;var properties=new MaterialPropertyBlock();properties.SetColor("_BaseColor",color);properties.SetFloat("_SurfaceRole",surfaceRole);
            // Hard-normal rock facets separate under URP normal bias. Include their
            // reverse faces in the shadow map to keep the solid opaque across seams;
            // this changes neither visible-face culling nor terrain/navigation geometry.
            if(surfaceRole==4)obj.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.TwoSided;
            if(name.StartsWith("platform-")){var p=surfaceMaterials.Profile;properties.SetVector("_PlatformSteel",new Vector4(1,p.platformSteelLanePanels,p.platformSteelContrast,p.platformSteelTread));properties.SetVector("_PlatformBounds",new Vector4((float)shape.MinX,(float)shape.MinZ,(float)shape.MaxX,(float)shape.MaxZ));}
            obj.GetComponent<Renderer>().SetPropertyBlock(properties);
        }
        private Mesh ReadableRock(NavObstacle shape,double bottom,double top)
        {
            var art=surfaceMaterials.Art;var settings=surfaceMaterials.Profile;
            var mesh=TerrainMesh.ErodedRock(shape,bottom>0?bottom-art.erosionDepth:bottom,top,art.erosionDepth,out _,true,settings.rockLedgeWavelength);
            var map=(ThreeCrossingsMap)profile.AuthoredMap;
            var masses=map.Solids.Where(s=>s.Bottom<=0&&s.Top>=map.WaterDepth&&!IsPlatformFence(s)).ToArray();
            float fullHeight=(float)map.Solids.Where(s=>s.Top>=map.WaterDepth).Max(s=>s.Top);
            var direction=new Vector2(-(float)(profile.CameraOffsetX/profile.CameraHeight),-(float)(profile.CameraOffsetZ/profile.CameraHeight));
            float projection=direction.magnitude;if(projection<.001f)return mesh;direction/=projection;
            float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
            float Scale(Vector3 p)
            {
                var point=new NavPoint(p.x,p.z);float distance=0;
                foreach(var mass in masses)
                {
                    if(!mass.Contains(point)&&mass.BoundaryDistanceSquared(point)>.00001)continue;
                    float exit=float.PositiveInfinity;var ring=mass.Footprint.Vertices;
                    for(int i=0;i<ring.Count;i++)
                    {
                        var a=new Vector2((float)ring[i].X-p.x,(float)ring[i].Z-p.z);
                        var b=new Vector2((float)ring[(i+1)%ring.Count].X-p.x,(float)ring[(i+1)%ring.Count].Z-p.z);var edge=b-a;
                        float denominator=Cross(direction,edge);if(Mathf.Abs(denominator)<.00001f)continue;
                        float t=Cross(a,edge)/denominator,u=Cross(a,direction)/denominator;
                        // Ignore the entry edge; zero-distance rear edges must still taper.
                        if(t>=-.0001f&&u>=0&&u<=1&&Cross(edge,direction)<0)exit=Mathf.Min(exit,Mathf.Max(0,t));
                    }
                    if(!float.IsPositiveInfinity(exit))distance=Mathf.Max(distance,exit);
                }
                return Mathf.Lerp(1,Mathf.Clamp01((distance-settings.rockRevealMargin)/(projection*fullHeight)),settings.rockPassageReveal);
            }
            var vertices=mesh.vertices;
            for(int i=0;i<vertices.Length;i++)if(vertices[i].y>0)vertices[i].y*=Scale(vertices[i]);
            mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.name="Broad passage-readable rock ledges";return mesh;
        }
        private void MapBox(string name,NavObstacle b,double y,double height,Color color,int role)
        {
            // Build presentation-only geometry directly; primitive cubes create a transient collider.
            MapPrism(name,b,y-height/2,y+height/2,color,null,role);
        }
        public void InspectionSites()
        {
            // Diagnostic projection of authoritative sites, not additional gameplay geometry.
            foreach(var site in TerritoryRules.Sites(profile))
            {
                bool mine=site.Kind==PlayableBuildingKind.Mine;
                Color color=mine?new Color(.95f,.67f,.19f):profile.AuthoredMap is FoundryMap?(site.Id<=3?Ally:site.Id<=6?Enemy:new Color(.64f,.48f,.83f)):site.Id==1?Ally:site.Id==2?Enemy:new Color(.86f,.77f,.39f);
                double radius=TerritoryRules.Radius(profile,site.Kind);
                var marker=Part(site.Kind+" "+site.Id,root,Point(site.Position)+Vector3.up*(profile.AuthoredMap is FoundryMap?.14f:.02f),new Vector3((float)radius*2,.04f,(float)radius*2),color,mine?PrimitiveType.Cylinder:PrimitiveType.Cube);
                if(mine)marker.transform.localScale=new Vector3((float)radius*2,.02f,(float)radius*2);
                if(profile.AuthoredMap is FoundryMap)
                {
                    string text=site.Id<=6?(site.Id<=3?"A":"B")+((site.Id-1)%3+1):site.Id<=10?"F"+(site.Id-6):"M"+(site.Id-10);
                    var label=new GameObject(text+" diagnostic label");label.transform.SetParent(root,false);label.transform.position=Point(site.Position)+new Vector3(0,.2f,3.5f);label.transform.rotation=Quaternion.Euler(90,0,0);var letters=label.AddComponent<TextMesh>();letters.text=text;letters.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.GetComponent<MeshRenderer>().sharedMaterial=letters.font.material;letters.fontSize=48;letters.characterSize=.7f;letters.anchor=TextAnchor.MiddleCenter;letters.color=Color.white;
                }
                foreach(var slot in site.Slots){var pad=Part("Reserved slot",root,Point(slot.Position)+Vector3.up*(profile.AuthoredMap is FoundryMap?.14f:.02f),new Vector3((float)profile.OrdinaryPadRadius*2,.04f,(float)profile.OrdinaryPadRadius*2),color);pad.transform.rotation=Quaternion.Euler(0,-(float)slot.Heading*Mathf.Rad2Deg,0);}
            }
        }
        public void InspectionHide(){if(groveMaterial)groveMaterial.SetTexture("_FogMask",Texture2D.whiteTexture);material.SetTexture("_FogMask",Texture2D.whiteTexture);if(meridianMaterial)meridianMaterial.SetTexture("_FogMask",Texture2D.whiteTexture);}
        public void InspectionReveal()
        {
            // Explicit diagnostic material override; gameplay fog/domain are unchanged.
            material.SetTexture("_FogMask",Texture2D.blackTexture);
            if(groveMaterial)groveMaterial.SetTexture("_FogMask",Texture2D.blackTexture);
            if(meridianMaterial)meridianMaterial.SetTexture("_FogMask",Texture2D.blackTexture);
        }
    }
}
