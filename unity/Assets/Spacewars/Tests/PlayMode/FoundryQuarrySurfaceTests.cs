using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Tests.PlayMode
{
    public sealed class FoundryQuarrySurfaceTests
    {
        [TestCase(1d)] [TestCase(1.5d)]
        public void ActualFoundationFootprintsAreHardAndOpenQuarryIsSoft(double scale)
        {
            var map=new FoundryMap(new FoundryProfileData{scale=scale});
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var art=FoundrySurfaceProfile.Load();var root=new GameObject("Foundry pad material boundary");
            var world=new PlayableWorld(root.transform,p);
            {
                try
                {
                    var sites=map.Sites(p);Assert.AreEqual(24,sites.Length);
                    foreach(var site in sites)
                    {
                        var pad=FoundryTerrainMesh.SitePad(site,p,art);
                        Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(site.Position));
                        if(site.Kind==PlayableBuildingKind.Mine)
                        {
                            Assert.Greater(pad.MaxZ-pad.MinZ,2*FoundryTerrainMesh.SiteEnvelope(site,p),"Yard extends beyond the former fitted square");
                            Assert.AreEqual(8,pad.Footprint.Vertices.Count);
                            Assert.That((pad.MinX+pad.MaxX)/2,Is.EqualTo(site.Position.X).Within(1e-9),"Mine is centered along X");
                            Assert.That((pad.MinZ+pad.MaxZ)/2,Is.EqualTo(site.Position.Z).Within(1e-9),"Mine is centered along Z");
                        }
                        foreach(var corner in pad.Footprint.Vertices)
                        {
                            var inside=new NavPoint(corner.X*.99+site.Position.X*.01,corner.Z*.99+site.Position.Z*.01);
                            if(site.Kind==PlayableBuildingKind.Mine)
                            {
                                Assert.That(map.SurfaceHeight(inside),Is.EqualTo(map.SurfaceHeight(site.Position)).Within(.0001),"Yard stays on its supported plane");
                                Assert.False(map.Solids.Any(rock=>rock.Contains(inside)),"Yard cannot enter blocked rocks");
                            }
                            Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(inside),"Every exact hex/square corner remains hard");
                        }
                    }
                    foreach(var q in new[]{map.Point(17,0),map.Point(65,80),map.Point(77,13),map.Point(0,116)})
                        Assert.AreEqual(GroundTrailSurfaceKind.Sand,world.TrailSurface(q),"Open supported quarry ground accepts impressions");
                    foreach(var rock in map.Solids)
                        Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(rock.Footprint.Vertices[0]));
                    var homePads=root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Hexagonal steel home ")).ToArray();
                    var yards=root.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("Steel mine work yard ")).ToArray();
                    Assert.AreEqual(14,yards.Length);
                    foreach(var yard in yards){Assert.AreEqual(8,yard.sharedMesh.vertexCount);Assert.AreEqual(6,yard.sharedMesh.triangles.Length/3);}
                    Assert.AreEqual(6,homePads.Length);Assert.IsEmpty(root.GetComponentsInChildren<Collider>());
                    foreach(var pad in homePads){var block=new MaterialPropertyBlock();pad.GetPropertyBlock(block);Assert.AreEqual(1,block.GetVector("_FoundryPadBounds").w);Assert.AreEqual(1,pad.sharedMaterial.GetVector("_FoundrySteel").x);}
                }
                finally{world.Dispose();UnityEngine.Object.DestroyImmediate(root);}
            }
        }
        [Test]
        public void MineYardReplacesFittedApronWithoutRemovingCaptureProgress()
        {
            var p=FoundryGreybox.LoadProfile();var root=new GameObject("Foundry mine yard lifecycle");var world=new PlayableWorld(root.transform,p);
            try
            {
                var site=((FoundryMap)p.AuthoredMap).Sites(p).First(s=>s.Kind==PlayableBuildingKind.Mine);
                PlayableSnapshot Frame(long tick,bool occupied)=>new PlayableSnapshot(p.ProfileId,p.Revision,1,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),
                    occupied?new[]{new PlayableBuildingSnapshot(site.Id,PlayableOwner.Player,site.Kind,site.Position,250,1,0,0,default,site.Id,0,phase:ConstructionPhase.Ready)}:Array.Empty<PlayableBuildingSnapshot>(),
                    Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
                    new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,occupied?1:.6,false,occupied?site.Id:0,occupied)});
                world.RenderSites(Frame(1,false));
                var pad=root.GetComponentsInChildren<Transform>().Single(t=>t.name=="Site pad "+site.Id+":0");
                var technical=pad.Find("Technical apron").GetComponent<MeshFilter>();var capture=pad.Find("Capture").GetComponent<MeshFilter>();
                Assert.Zero(technical.sharedMesh.vertexCount,"No model-sized decorative square over the broad yard");
                Assert.Greater(capture.sharedMesh.vertexCount,0,"Capture progress must remain visible");
                Assert.AreSame(world.Fog.Texture,capture.GetComponent<Renderer>().sharedMaterial.GetTexture("_FogMask"));
                world.RenderSites(Frame(2,true));Assert.Zero(capture.sharedMesh.vertexCount,"Completed mine has no permanent colored square");
                Assert.Zero(technical.sharedMesh.vertexCount);
            }
            finally{world.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test]
        public void CrossingAFoundationClipsImpressionsAndRetainsHeightAndLifetime()
        {
            bool old=NativeGraphicsSettings.GroundTrailsEnabled;NativeGraphicsSettings.GroundTrailsEnabled=true;
            var p=FoundryGreybox.LoadProfile();var root=new GameObject("Foundry actual trail clipping");var world=new PlayableWorld(root.transform,p);
            try
            {
                world.InspectionReveal();
                var facts=new[]{new PlayableGroundTrail(new NavPoint(-52,106),new NavPoint(-52,90),-Math.PI/2,0,3,PlayableEntityKind.Tank),new PlayableGroundTrail(new NavPoint(-37,106),new NavPoint(-37,90),-Math.PI/2,0,3,PlayableEntityKind.Explorer)};
                PlayableSnapshot Frame(long tick)=>new PlayableSnapshot(p.ProfileId,p.Revision,1,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,groundTrails:facts);
                world.RenderGroundTrails(Frame(5));Assert.Greater(world.GroundTrailQuadCount,0);
                var filter=root.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Ground vehicle trails");
                foreach(var v in filter.sharedMesh.vertices)
                {
                    var q=new NavPoint(v.x,v.z);Assert.AreEqual(GroundTrailSurfaceKind.Sand,world.TrailSurface(q),"No impression crosses steel, concrete or rock");
                    Assert.That(v.y,Is.EqualTo(p.AuthoredMap.SurfaceHeight(q)+.012).Within(.0001));
                }
                var channels=new System.Collections.Generic.List<Vector4>();filter.sharedMesh.GetUVs(1,channels);
                CollectionAssert.AreEquivalent(new[]{0f,1f},channels.Select(c=>c.y).Distinct());
                world.RenderGroundTrails(Frame(3+p.GroundTrailLifetimeTicks));Assert.Zero(world.GroundTrailQuadCount);
            }
            finally{world.Dispose();UnityEngine.Object.DestroyImmediate(root);NativeGraphicsSettings.GroundTrailsEnabled=old;}
        }
    }
}
