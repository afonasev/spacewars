using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        public static readonly Color FoundryGround=new Color(.49f,.43f,.36f),FoundryRoad=new Color(.46f,.36f,.28f),FoundryRock=new Color(.15f,.14f,.14f),FoundryLava=new Color(1,.25f,.035f);
        private Texture2D foundryRoadMask,foundryLavaHeat;
        private void CreateFoundry(FoundryMap map)
        {
            var settings=FoundrySurfaceProfile.Load();settings.Apply(material);
            foreach(var support in map.Supports)MapPrism(support.Id,support.Bounds,-2,support.Height,FoundryGround,support.HeightAt,support.Height>=map.UpperHeight?7:6);
            foreach(var rock in FoundryTerrainMesh.Ridges(map))
            {
                var obj=new GameObject("Connected layered basalt / bank");obj.transform.SetParent(root,false);
                var mesh=FoundryTerrainMesh.Rock(rock,settings,map.Lava,map.SurfaceHeight);terrainMeshes.Add(mesh);obj.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",new Color(settings.basaltValue*.92f,settings.basaltValue*.95f,settings.basaltValue));block.SetFloat("_SurfaceRole",10);
                if(System.Linq.Enumerable.Any(map.Lava,c=>System.Linq.Enumerable.All(c.Footprint.Vertices,rock.Contains)))block.SetVector("_FoundryLavaRim",new Vector4((float)rock.Top-settings.lavaDepth,(float)rock.Top,1,0));
                renderer.SetPropertyBlock(block);
            }
            foreach(var rock in System.Linq.Enumerable.Skip(map.Solids,map.Solids.Count-4))
            {
                double x=(rock.MinX+rock.MaxX)/2,z=(rock.MinZ+rock.MaxZ)/2,w=(rock.MaxX-rock.MinX)*.33,d=(rock.MaxZ-rock.MinZ)*.35;
                var main=new NavObstacle(x-w,z-d,x+w*.55,z+d);
                MapPrism("Foundry grey industrial hall",main,rock.Top-settings.crownRelief,rock.Top+settings.industrialHeight,new Color(.43f,.45f,.46f),null,12);
                var annex=new NavObstacle(x+w*.55,z-d*.75,x+w,z+d*.4);
                MapPrism("Foundry grey industrial annex",annex,rock.Top-settings.crownRelief,rock.Top+settings.industrialHeight*.6f,new Color(.37f,.39f,.40f),null,12);
            }
            foreach(var lava in map.Lava)
            {
                float level=FoundryFissureMesh.Level(map,lava,settings);
                MapPrism("Lava river / fissure",lava,level-.04f,level,FoundryLava,null,11); // Thin presentation fluid below the exposed rim.
            }
            var sites=map.Sites(profile);
            // A single mask excludes complete pads before paving: no texture boundary bisects a site.
            double Envelope(TerritorySite site)=>site.Slots.Count==0?TerritoryRules.Radius(profile,site.Kind)+1:profile.SlotRingRadius+System.Math.Max(profile.ScienceFootprintRadius,System.Math.Max(profile.FactoryFootprintRadius,profile.RefineryFootprintRadius))+1;
            // Public presentation mask; sampled continuously in world space instead of tiled road meshes.
            const int resolution=512; // Fixed raster quality, no simulation or gameplay tuning.
            foundryRoadMask=new Texture2D(resolution,resolution,TextureFormat.RGBA32,false,true){name="Foundry compacted slag mask",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            var pixels=new Color[resolution*resolution];var lavaPixels=new Color[pixels.Length];
            for(int z=0;z<resolution;z++)for(int x=0;x<resolution;x++)
            {
                var q=new NavPoint(((x+.5)/resolution*2-1)*map.HalfExtent,((z+.5)/resolution*2-1)*map.HalfExtent);
                float irregular=.75f+.5f*Mathf.PerlinNoise((float)q.X/settings.depositScale,(float)q.Z/settings.depositScale);
                float amount=FoundryTerrainMesh.RoadCoverage(map,q,settings);
                if(System.Linq.Enumerable.Any(sites,site=>System.Math.Abs(q.X-site.Position.X)<Envelope(site)&&System.Math.Abs(q.Z-site.Position.Z)<Envelope(site)))amount=0;
                double rockDistance=double.MaxValue;foreach(var rock in map.Solids)rockDistance=System.Math.Min(rockDistance,System.Math.Sqrt(rock.Footprint.DistanceSquared(q)));
                float foot=1-Mathf.SmoothStep(0,1,(float)rockDistance/(settings.rockBlend*irregular));
                pixels[z*resolution+x]=new Color(amount,foot,0,1);
                double lavaDistance=0,glowDistance=double.MaxValue;foreach(var lava in map.Lava)
                {
                    if(lava.Contains(q))lavaDistance=System.Math.Max(lavaDistance,System.Math.Sqrt(lava.BoundaryDistanceSquared(q)));
                    glowDistance=System.Math.Min(glowDistance,System.Math.Sqrt(lava.Footprint.DistanceSquared(q)));
                }
                lavaPixels[z*resolution+x]=new Color(Mathf.SmoothStep(0,1,(float)lavaDistance/settings.lavaCrustWidth),1-Mathf.SmoothStep(0,1,(float)glowDistance/settings.lavaCrustWidth),0,1);
            }
            foundryRoadMask.SetPixels(pixels);foundryRoadMask.Apply();material.SetTexture("_RoadMask",foundryRoadMask);material.SetColor("_RoadTint",FoundryRoad);
            foundryLavaHeat=new Texture2D(resolution,resolution,TextureFormat.RGBA32,false,true){name="Foundry molten center / cooled rim",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};foundryLavaHeat.SetPixels(lavaPixels);foundryLavaHeat.Apply();material.SetTexture("_FoundryLavaHeat",foundryLavaHeat);
            foreach(var site in sites)
            {
                double extent=Envelope(site);var p=site.Position;
                var pad=site.Kind==PlayableBuildingKind.Headquarters?FoundryTerrainMesh.HomePad(p,extent,settings):new NavObstacle(p.X-extent,p.Z-extent,p.X+extent,p.Z+extent);
                // Fixed mesh-layer offset stays below the existing live build/capture pad surfaces.
                var obj=new GameObject((site.Kind==PlayableBuildingKind.Headquarters?"Hexagonal concrete home ":"Complete concrete site ")+site.Id);obj.transform.SetParent(root,false);
                var mesh=TerrainMesh.Prism(pad,-.1,.05,q=>map.SurfaceHeight(q)+.006);terrainMeshes.Add(mesh);obj.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",new Color(settings.concreteValue,settings.concreteValue,settings.concreteValue));block.SetFloat("_SurfaceRole",8);
                bool home=site.Kind==PlayableBuildingKind.Headquarters;
                block.SetVector("_FoundryPadBounds",new Vector4((float)p.X,(float)p.Z,(float)(home?extent*settings.homeHexExtent:extent),home?1:0));
                block.SetFloat("_FoundryPadCenter",site.Kind==PlayableBuildingKind.Mine?0:1);renderer.SetPropertyBlock(block);
            }
        }
    }
}
