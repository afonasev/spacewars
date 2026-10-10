using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        internal static readonly Color BankColor=new Color(.55f,.56f,.49f),WaterColor=new Color(.08f,.36f,.49f),BridgeColor=new Color(.73f,.71f,.60f),RockColor=new Color(.30f,.34f,.36f);
        private ThreeCrossingsMaterials surfaceMaterials;
        public string SurfaceRevision=>surfaceMaterials==null?"none":surfaceMaterials.Profile.id+"@"+surfaceMaterials.Profile.revision;
        public string EnvironmentRevision=>surfaceMaterials==null?"none":surfaceMaterials.Art.id+"@"+surfaceMaterials.Art.revision;
        private void CreateThreeCrossings(ThreeCrossingsMap map)
        {
            surfaceMaterials=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,material,profile);
            foreach(var support in map.Supports)
            {
                double depth=support.IsBridge?map.DeckThickness:map.WaterDepth;
                MapPrism(support.Id,support.Bounds,-depth,support.Height,support.IsBridge||support.Id.StartsWith("platform-")?BridgeColor:BankColor,support.HeightAt,support.IsBridge?3:support.Id.StartsWith("platform-")?2:1);
                if(support.IsBridge)
                {
                    MapPrism("River below bridge",support.Bounds,-map.WaterDepth-map.DeckThickness,-map.WaterDepth,WaterColor,null,5);
                    // Canonical bridge silhouette is presentation-only, outside the complete support corridor.
                    var b=support.Bounds;
                    foreach(int side in new[]{-1,1})
                    {
                        double edge=side<0?b.MinZ:b.MaxZ,z=edge+side*map.RailThickness/2;
                        MapBox("Bridge rail",new NavObstacle(b.MinX+map.PostWidth,z-map.RailThickness/2,b.MaxX-map.PostWidth,z+map.RailThickness/2),map.RailHeight/2,map.RailHeight,RockColor,3);
                        foreach(double x in new[]{b.MinX+map.PostWidth/2,b.MaxX-map.PostWidth/2})
                        {z=edge+side*map.PostWidth/2;MapBox("Bridge end post",new NavObstacle(x-map.PostWidth/2,z-map.PostWidth/2,x+map.PostWidth/2,z+map.PostWidth/2),map.PostHeight/2,map.PostHeight,BridgeColor,2);}
                    }
                }
            }
            foreach(var water in map.Water)MapPrism("River — no support",water,-map.WaterDepth-map.DeckThickness,-map.WaterDepth,WaterColor,null,5);
            foreach(var rock in map.Solids)MapPrism("Solid rock / platform edge",rock,rock.Bottom,rock.Top,double.IsNaN(rock.Top)?RockColor:rock.Top<map.WaterDepth?BridgeColor:rock.Bottom>0?Color.Lerp(RockColor,BridgeColor,.2f):RockColor,null,rock.Top<map.WaterDepth?2:4);
            var details=ThreeCrossingsDetails.Plan(map,profile,surfaceMaterials);
            surfaceMaterials.BuildWeathering(map,details,material);
            foreach(bool stones in new[]{true,false})
            {
                var mesh=ThreeCrossingsDetails.Build(details,surfaceMaterials.Art,stones);terrainMeshes.Add(mesh);
                var obj=new GameObject(stones?"Detail — geological debris":"Detail — service infrastructure");obj.transform.SetParent(root,false);
                obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=material;
                var properties=new MaterialPropertyBlock();properties.SetFloat("_SurfaceRole",stones?4:3);properties.SetFloat("_Dressing",stones?1:2);
                properties.SetColor("_BaseColor",Color.white);obj.GetComponent<Renderer>().SetPropertyBlock(properties);
            }
        }
        private readonly System.Collections.Generic.List<Mesh> terrainMeshes=new System.Collections.Generic.List<Mesh>();
        // Surface role IDs are a fixed shader contract: 0 unchanged, 1 earth, 2 concrete, 3 steel, 4 rock, 5 water.
        private void MapPrism(string name,NavObstacle shape,double bottom,double top,Color color,System.Func<NavPoint,double> height=null,int surfaceRole=0)
        {
            var obj=new GameObject(name);obj.transform.SetParent(root,false);var mesh=surfaceRole==4?TerrainMesh.ErodedRock(shape,bottom>0?bottom-surfaceMaterials.Art.erosionDepth:bottom,top,surfaceMaterials.Art.erosionDepth):TerrainMesh.Prism(shape,bottom,top,height);terrainMeshes.Add(mesh);obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=material;var properties=new MaterialPropertyBlock();properties.SetColor("_BaseColor",color);properties.SetFloat("_SurfaceRole",surfaceRole);obj.GetComponent<Renderer>().SetPropertyBlock(properties);
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
        public void InspectionHide()=>material.SetTexture("_FogMask",Texture2D.whiteTexture);
        public void InspectionReveal()
        {
            // Explicit diagnostic material override; gameplay fog/domain are unchanged.
            material.SetTexture("_FogMask",Texture2D.blackTexture);
        }
    }
}
