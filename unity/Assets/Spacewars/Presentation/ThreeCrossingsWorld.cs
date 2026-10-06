using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        internal static readonly Color BankColor=new Color(.55f,.56f,.49f),WaterColor=new Color(.08f,.36f,.49f),BridgeColor=new Color(.73f,.71f,.60f),RockColor=new Color(.30f,.34f,.36f);
        private ThreeCrossingsMaterials surfaceMaterials;
        public string SurfaceRevision=>surfaceMaterials==null?"none":surfaceMaterials.Profile.id+"@"+surfaceMaterials.Profile.revision;
        private void CreateThreeCrossings(ThreeCrossingsMap map)
        {
            surfaceMaterials=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,material);
            foreach(var support in map.Supports)
            {
                double depth=support.IsBridge?map.DeckThickness:map.WaterDepth;
                MapPrism(support.Id,support.Bounds,-depth,support.Height,support.IsBridge||support.Id.StartsWith("platform-")?BridgeColor:BankColor,support.HeightAt,support.IsBridge?3:support.Id.StartsWith("platform-")?2:1);
                if(support.IsBridge)
                {
                    MapPrism("River below bridge",support.Bounds,-map.WaterDepth-map.DeckThickness,-map.WaterDepth,WaterColor);
                    // Canonical bridge silhouette is presentation-only, outside the complete support corridor.
                    var b=support.Bounds;
                    foreach(int side in new[]{-1,1})
                    {
                        double edge=side<0?b.MinZ:b.MaxZ,z=edge+side*map.RailThickness/2;
                        MapBox("Bridge rail",new NavObstacle(b.MinX+map.PostWidth,z-map.RailThickness/2,b.MaxX-map.PostWidth,z+map.RailThickness/2),map.RailHeight/2,map.RailHeight,RockColor);
                        foreach(double x in new[]{b.MinX+map.PostWidth/2,b.MaxX-map.PostWidth/2})
                        {z=edge+side*map.PostWidth/2;MapBox("Bridge end post",new NavObstacle(x-map.PostWidth/2,z-map.PostWidth/2,x+map.PostWidth/2,z+map.PostWidth/2),map.PostHeight/2,map.PostHeight,BridgeColor);}
                    }
                }
            }
            foreach(var water in map.Water)MapPrism("River — no support",water,-map.WaterDepth-map.DeckThickness,-map.WaterDepth,WaterColor);
            foreach(var rock in map.Solids)MapPrism("Solid rock / platform edge",rock,rock.Bottom,rock.Top,double.IsNaN(rock.Top)?RockColor:rock.Top<map.WaterDepth?BridgeColor:rock.Bottom>0?Color.Lerp(RockColor,BridgeColor,.2f):RockColor,null,rock.Top<map.WaterDepth?2:4);
        }
        private readonly System.Collections.Generic.List<Mesh> terrainMeshes=new System.Collections.Generic.List<Mesh>();
        // Surface role IDs are a fixed shader contract: 0 unchanged, 1 earth, 2 concrete, 3 steel, 4 rock.
        private void MapPrism(string name,NavObstacle shape,double bottom,double top,Color color,System.Func<NavPoint,double> height=null,int surfaceRole=0)
        {
            var obj=new GameObject(name);obj.transform.SetParent(root,false);var mesh=TerrainMesh.Prism(shape,bottom,top,height);terrainMeshes.Add(mesh);obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=material;var properties=new MaterialPropertyBlock();properties.SetColor("_BaseColor",color);properties.SetFloat("_SurfaceRole",surfaceRole);obj.GetComponent<Renderer>().SetPropertyBlock(properties);
        }
        private void MapBox(string name,NavObstacle b,double y,double height,Color color)=>Part(name,root,new Vector3((float)((b.MinX+b.MaxX)/2),(float)y,(float)((b.MinZ+b.MaxZ)/2)),new Vector3((float)(b.MaxX-b.MinX),(float)height,(float)(b.MaxZ-b.MinZ)),color);
        public void InspectionSites()
        {
            // Diagnostic projection of authoritative sites, not additional gameplay geometry.
            foreach(var site in TerritoryRules.Sites(profile))
            {
                bool mine=site.Kind==PlayableBuildingKind.Mine;
                Color color=mine?new Color(.95f,.67f,.19f):site.Id==1?Ally:site.Id==2?Enemy:new Color(.86f,.77f,.39f);
                double radius=TerritoryRules.Radius(profile,site.Kind);
                var marker=Part(site.Kind+" "+site.Id,root,Point(site.Position)+Vector3.up*.02f,new Vector3((float)radius*2,.04f,(float)radius*2),color,mine?PrimitiveType.Cylinder:PrimitiveType.Cube);
                if(mine)marker.transform.localScale=new Vector3((float)radius*2,.02f,(float)radius*2);
                foreach(var slot in site.Slots){var pad=Part("Reserved slot",root,Point(slot.Position)+Vector3.up*.02f,new Vector3((float)profile.OrdinaryPadRadius*2,.04f,(float)profile.OrdinaryPadRadius*2),color);pad.transform.rotation=Quaternion.Euler(0,-(float)slot.Heading*Mathf.Rad2Deg,0);}
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
