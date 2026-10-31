using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        public static readonly Color FoundryGround=new Color(.49f,.43f,.36f),FoundryRoad=new Color(.46f,.36f,.28f),FoundryRock=new Color(.15f,.14f,.14f),FoundryLava=new Color(1,.25f,.035f);
        private Texture2D foundryRoadMask,foundryLavaHeat,foundryRouteMask;
        private System.Collections.IEnumerator CreateFoundry(FoundryMap map)
        {
            var budget=new LoadingFrameBudget();
            var settings=FoundrySurfaceProfile.Load();settings.Apply(material);
            material.SetVector("_FoundryHeightLevels",new Vector4((float)map.RearHeight,(float)map.UpperHeight,0,0));
            foreach(var support in map.Supports)MapPrism(support.Id,support.Bounds,-2,support.Height,FoundryGround,support.HeightAt,support.Height>=map.UpperHeight?7:6);
            foreach(var rock in FoundryTerrainMesh.Ridges(map))
            {
                if(budget.Expired){yield return null;budget.Restart();}
                var obj=new GameObject("Connected layered basalt / bank");obj.transform.SetParent(root,false);
                bool industrial=System.Linq.Enumerable.Contains(System.Linq.Enumerable.Skip(map.Solids,map.Solids.Count-4),rock);
                var mesh=FoundryTerrainMesh.Rock(rock,settings,map.Lava,map.SurfaceHeight,industrial);terrainMeshes.Add(mesh);obj.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",new Color(settings.basaltValue*.92f,settings.basaltValue*.95f,settings.basaltValue));block.SetFloat("_SurfaceRole",10);
                if(System.Linq.Enumerable.Any(map.Lava,c=>System.Linq.Enumerable.All(c.Footprint.Vertices,rock.Contains)))block.SetVector("_FoundryLavaRim",new Vector4((float)rock.Top-settings.lavaDepth,(float)rock.Top,1,0));
                renderer.SetPropertyBlock(block);
            }
            FoundryIndustrialLandmarks.Build(root,material,map,settings,terrainMeshes);
            foreach(var lava in map.Lava)
            {
                if(budget.Expired){yield return null;budget.Restart();}
                float level=FoundryFissureMesh.Level(map,lava,settings);
                MapPrism("Lava river / fissure",lava,level-.04f,level,FoundryLava,null,11); // Thin presentation fluid below the exposed rim.
            }
            var sites=map.Sites(profile);
            // The same actual outline drives the foundation mesh, road exclusion and hard trail coverage.
            // Blue retains the underlying road solely for flush wear at concrete entries.
            var pads=System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(sites,site=>FoundryTerrainMesh.SitePad(site,profile,settings)));
            // Public presentation mask; sampled continuously in world space instead of tiled road meshes.
            const int resolution=512; // Fixed raster quality, no simulation or gameplay tuning.
            foundryRoadMask=new Texture2D(resolution,resolution,TextureFormat.RGBA32,false,true){name="Foundry compacted slag mask",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            var pixels=new Color[resolution*resolution];var lavaPixels=new Color[pixels.Length];var routePixels=new Color[pixels.Length];
            var connections=FoundryRouteFinish.Connections(map);
            var rockDistances=new TerrainPolygonDistances(map.Solids);var lavaDistances=new TerrainPolygonDistances(map.Lava);
            for(int z=0;z<resolution;z++)for(int x=0;x<resolution;x++)
            {
                if(x==0&&budget.Expired){yield return null;budget.Restart();}
                var q=new NavPoint(((x+.5)/resolution*2-1)*map.HalfExtent,((z+.5)/resolution*2-1)*map.HalfExtent);
                float irregular=.75f+.5f*Mathf.PerlinNoise((float)q.X/settings.depositScale,(float)q.Z/settings.depositScale);
                float approach=FoundryTerrainMesh.RoadCoverage(map,q,settings),amount=approach;
                bool onPad=System.Linq.Enumerable.Any(pads,pad=>pad.Contains(q));
                var routes=FoundryRouteFinish.Coverage(map,q,settings,connections);
                // Road entry wear follows the true lateral outpost approach as well.
                approach=Mathf.Max(approach,routes.y);
                if(onPad)amount=0;
                routePixels[z*resolution+x]=onPad?Color.clear:new Color(routes.x,routes.y,0,1);
                double rockDistance=rockDistances.Minimum(q);
                float foot=1-Mathf.SmoothStep(0,1,(float)rockDistance/(settings.rockBlend*irregular));
                pixels[z*resolution+x]=new Color(amount,foot,approach,1);
                double lavaDistance=0,glowDistance=lavaDistances.Minimum(q);foreach(var lava in map.Lava)
                {
                    if(lava.Contains(q))lavaDistance=System.Math.Max(lavaDistance,System.Math.Sqrt(lava.BoundaryDistanceSquared(q)));
                }
                lavaPixels[z*resolution+x]=new Color(Mathf.SmoothStep(0,1,(float)lavaDistance/settings.lavaCrustWidth),1-Mathf.SmoothStep(0,1,(float)glowDistance/settings.lavaCrustWidth),0,1);
            }
            foundryRouteMask=new Texture2D(resolution,resolution,TextureFormat.RGBA32,false,true){name="Foundry climbs and outpost connections",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            foundryRouteMask.SetPixels(routePixels);foundryRouteMask.Apply();material.SetTexture("_FoundryRouteMask",foundryRouteMask);
            foundryRoadMask.SetPixels(pixels);foundryRoadMask.Apply();material.SetTexture("_RoadMask",foundryRoadMask);
            foundryLavaHeat=new Texture2D(resolution,resolution,TextureFormat.RGBA32,false,true){name="Foundry molten center / cooled rim",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};foundryLavaHeat.SetPixels(lavaPixels);foundryLavaHeat.Apply();material.SetTexture("_FoundryLavaHeat",foundryLavaHeat);
            foreach(var site in sites)
            {
                if(budget.Expired){yield return null;budget.Restart();}
                double extent=FoundryTerrainMesh.SiteEnvelope(site,profile);var p=site.Position;
                var pad=FoundryTerrainMesh.SitePad(site,profile,settings);
                // Fixed mesh-layer offset stays below the existing live build/capture pad surfaces.
                var obj=new GameObject((site.Kind==PlayableBuildingKind.Headquarters?"Hexagonal steel home ":site.Kind==PlayableBuildingKind.Mine?"Steel mine work yard ":"Complete concrete site ")+site.Id);obj.transform.SetParent(root,false);
                var mesh=site.Kind==PlayableBuildingKind.Mine?FoundryTerrainMesh.MineYardSurface(pad,map):TerrainMesh.Prism(pad,-.1,.05,q=>map.SurfaceHeight(q)+.006);terrainMeshes.Add(mesh);obj.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",new Color(settings.concreteValue,settings.concreteValue,settings.concreteValue));block.SetFloat("_SurfaceRole",8);
                bool home=site.Kind==PlayableBuildingKind.Headquarters,mine=site.Kind==PlayableBuildingKind.Mine;
                if(mine)
                {
                    p=new NavPoint((pad.MinX+pad.MaxX)/2,(pad.MinZ+pad.MaxZ)/2);
                    float halfX=(float)(pad.MaxX-pad.MinX)/2,halfZ=(float)(pad.MaxZ-pad.MinZ)/2;
                    block.SetVector("_FoundryYardShape",new Vector4(halfX,halfZ,Mathf.Min(halfX,halfZ)*.35f,0));
                }
                block.SetVector("_FoundryPadBounds",new Vector4((float)p.X,(float)p.Z,(float)(home?extent*settings.homeHexExtent:extent),home?1:mine?2:0));
                block.SetFloat("_FoundryPadCenter",site.Kind==PlayableBuildingKind.Mine?0:1);renderer.SetPropertyBlock(block);
            }
        }
    }
}
