using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Tests.EditMode
{
    public sealed class FoundryArtTests
    {
        [Test] public void ColdSlagPaletteSeparatesActualSupportLevelsAndContinuousRamps()
        {
            var art=FoundrySurfaceProfile.Load();art.Validate();
            Assert.AreEqual(1,art.heightPaletteStrength);
            foreach(double scale in new[]{1d,1.5d})
            {
                var map=new FoundryMap(new FoundryProfileData{scale=scale});
                Color At(double x,double z)=>art.GroundAtHeight(map.SurfaceHeight(map.Point(x,z)),map);
                var low=At(17,0);var rear=At(17,116);var upper=At(86,13);
                Assert.Greater(low.r,low.b,"Lowland remains warm");Assert.Greater(upper.b,upper.r,"Upper flank remains cool");
                Assert.Greater(rear.grayscale-low.grayscale,.06f);Assert.Greater(upper.grayscale-rear.grayscale,.06f);
                foreach(int side in new[]{-1,1})
                {
                    var previous=At(side*28,35);
                    for(int x=29;x<=60;x++)
                    {
                        var current=At(side*x,35);
                        Assert.Less(Mathf.Abs(current.grayscale-previous.grayscale),.02f,"No color step on a continuous flank ramp");
                        Assert.GreaterOrEqual(current.grayscale,previous.grayscale);previous=current;
                    }
                    Assert.Less(Vector3.Distance(new Vector3(At(side*60,35).r,At(side*60,35).g,At(side*60,35).b),new Vector3(upper.r,upper.g,upper.b)),.001f);
                }
            }
            art.upperValue=float.NaN;Assert.Throws<ArgumentException>(()=>art.Validate(),"Art controls retain hard validation");
        }
        [Test] public void CompactTerrainUsesHeightWithoutReplacingRoadRockOrLava()
        {
            var profile=FoundryGreybox.LoadProfile();var map=(FoundryMap)profile.AuthoredMap;
            var terrain=new PlayableMapTerrain(profile);
            try
            {
                terrain.ShowPublicTerrain();
                Color At(double x,double z){var q=map.Point(x,z);return terrain.Texture.GetPixel(Mathf.FloorToInt((float)(q.X/profile.ArenaHalfExtent*.5+.5)*terrain.Texture.width),Mathf.FloorToInt((float)(q.Z/profile.ArenaHalfExtent*.5+.5)*terrain.Texture.height));}
                var low=At(17,0);var rear=At(17,116);var upper=At(86,13);
                Assert.Greater(low.r,low.b);Assert.Greater(upper.b,upper.r);Assert.Greater(rear.grayscale-low.grayscale,.06f);Assert.Greater(upper.grayscale-rear.grayscale,.06f);
                void Same(Color expected,Color actual){Assert.AreEqual((Color32)expected,(Color32)actual);}
                Same(PlayableWorld.FoundryRoad,At(0,0));Same(PlayableWorld.FoundryRock,At(-130,0));Same(PlayableWorld.FoundryLava,At(123,0));
            }
            // Production Dispose schedules Destroy for PlayMode; this EditMode fixture owns immediate texture cleanup.
            finally{UnityEngine.Object.DestroyImmediate(terrain.Texture);}
        }
        [Test] public void RoleAccentsFollowRealClimbsAndOpenOutpostApproaches()
        {
            var settings=FoundrySurfaceProfile.Load();
            foreach(double scale in new[]{1d,1.5d})
            {
                var map=new FoundryMap(new FoundryProfileData{scale=scale});var paths=FoundryRouteFinish.Connections(map);
                Assert.AreEqual(4,paths.Length);
                // Maximum configurable accent width remains within real passage clearance.
                foreach(var path in paths)for(int i=1;i<path.Length;i++)
                    Assert.IsTrue(map.SupportsSweep(path[i-1],path[i],6),"Accent must not imply a route through a wall");
                foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1})
                {
                    Assert.Greater(FoundryRouteFinish.Coverage(map,map.Point(side*44,end*35),settings,paths).x,.99f);
                    Assert.Greater(FoundryRouteFinish.Coverage(map,map.Point(side*65,end*80),settings,paths).x,.99f);
                    Assert.Greater(FoundryRouteFinish.Coverage(map,map.Point(side<0?-44:58,end*65),settings,paths).y,.99f);
                }
                foreach(var point in new[]{map.Point(0,0),map.Point(0,80),map.Point(77,0)})
                    Assert.AreEqual(Vector2.zero,FoundryRouteFinish.Coverage(map,point,settings,paths),"Unrelated flat route remains unchanged");
                foreach(var point in new[]{map.Point(38,54),map.Point(38,-54),map.Point(-22,70),map.Point(-22,-70)})
                    Assert.AreEqual(Vector2.zero,FoundryRouteFinish.Coverage(map,point,settings,paths),"No accent through pocket walls");
            }
        }
        [Test] public void RouteMaskPreservesEveryPadAndBindsValidatedFinish()
        {
            var profile=FoundryGreybox.LoadProfile();var map=(FoundryMap)profile.AuthoredMap;var settings=FoundrySurfaceProfile.Load();
            var parent=new GameObject("Foundry role mask contract");
            try
            {
                using(var world=new PlayableWorld(parent.transform,profile))
                {
                    var material=parent.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Lava river / fissure").sharedMaterial;
                    var mask=(Texture2D)material.GetTexture("_FoundryRouteMask");Assert.NotNull(mask);
                    var pads=map.Sites(profile).Select(site=>FoundryTerrainMesh.SitePad(site,profile,settings)).ToArray();
                    var pixels=mask.GetPixels();int padSamples=0,climbSamples=0,connectionSamples=0;
                    for(int z=0;z<mask.height;z++)for(int x=0;x<mask.width;x++)
                    {
                        var q=new NavPoint(((x+.5)/mask.width*2-1)*map.HalfExtent,((z+.5)/mask.height*2-1)*map.HalfExtent);
                        var pixel=pixels[z*mask.width+x];
                        if(pads.Any(pad=>pad.Contains(q))){Assert.AreEqual(0,pixel.r);Assert.AreEqual(0,pixel.g);padSamples++;}
                        if(pixel.r>.9f)climbSamples++;if(pixel.g>.9f)connectionSamples++;
                    }
                    Assert.Greater(padSamples,1000);Assert.Greater(climbSamples,1000);Assert.Greater(connectionSamples,1000);
                    Assert.AreEqual(new Vector4(settings.padJointWidth,settings.padJointContrast,settings.padBayVariation,0),material.GetVector("_FoundryPadPours"));
                    Assert.AreEqual(new Vector4(settings.padAgeWidth,settings.padAgeStrength,0,0),material.GetVector("_FoundryPadAge"));
                    Assert.AreEqual(settings.climbValue,material.GetColor("_FoundryClimbTint").r,.001f);
                    Assert.AreEqual(settings.connectionValue,material.GetColor("_FoundryConnectionTint").r,.001f);
                    Assert.AreEqual(settings.climbWearSpacing,material.GetVector("_FoundryRouteFinish").x,.001f);
                    Assert.AreEqual(settings.climbWearContrast,material.GetVector("_FoundryRouteFinish").y,.001f);
                    Assert.IsEmpty(parent.GetComponentsInChildren<Collider>());
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(parent);}
        }
        [Test] public void RoadMaskRespectsHexCornersAndRetainsFlushApproachCoverage()
        {
            var profile=FoundryGreybox.LoadProfile();var map=(FoundryMap)profile.AuthoredMap;
            var settings=FoundrySurfaceProfile.Load();var parent=new GameObject("Foundry road connections");
            try
            {
                using(var world=new PlayableWorld(parent.transform,profile))
                {
                    var material=parent.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Lava river / fissure").sharedMaterial;
                    var mask=(Texture2D)material.GetTexture("_RoadMask");
                    // Bounded diagnosis before any assertion: preserve all four
                    // authored samples even when the first legacy point fails.
                    var connections=FoundryRouteFinish.Connections(map);
                    var audit=map.Sites(profile).Where(s=>s.Kind==PlayableBuildingKind.Headquarters&&Math.Abs(s.Position.X)>1).Select(home=>
                    {
                        double extent=FoundryTerrainMesh.SiteEnvelope(home,profile);
                        var legacy=new NavPoint(home.Position.X-Math.Sign(home.Position.X)*extent*1.2,Math.Sign(home.Position.Z)*106);
                        var road=TransverseRoadAt(map,legacy);var q=AuthoredRoadApproachPoint(map,home,profile,settings);
                        var uv=new Vector2((float)(q.X/map.HalfExtent*.5+.5),(float)(q.Z/map.HalfExtent*.5+.5));
                        var pixel=mask.GetPixelBilinear(uv.x,uv.y);
                        float dx=(Mathf.PerlinNoise((float)q.X/settings.roadBendScale+17,(float)q.Z/settings.roadBendScale+53)-.5f)*2*settings.roadMeander;
                        float dz=(Mathf.PerlinNoise((float)q.X/settings.roadBendScale+71,(float)q.Z/settings.roadBendScale+11)-.5f)*2*settings.roadMeander;
                        return new FoundryRoadAuditRow{homeId=home.Id,x=q.X,z=q.Z,legacyX=legacy.X,legacyZ=legacy.Z,roadCenterZ=(road.MinZ+road.MaxZ)*.5,
                            roadMinX=road.MinX,roadMaxX=road.MaxX,roadMinZ=road.MinZ,roadMaxZ=road.MaxZ,u=uv.x,v=uv.y,width=mask.width,height=mask.height,
                            warpX=dx,warpZ=dz,warpedX=q.X+dx,warpedZ=q.Z+dz,onPad=FoundryTerrainMesh.SitePad(home,profile,settings).Contains(q),
                            referenceRoadCoverage=FoundryTerrainMesh.RoadCoverage(map,q,settings),connectionCoverage=FoundryRouteFinish.Coverage(map,q,settings,connections).y,
                            sampledRed=pixel.r,sampledBlue=pixel.b};
                    }).ToArray();
                    var output=Environment.GetEnvironmentVariable("SPACEWARS_FOUNDRY_ROAD_EVIDENCE")??System.IO.Path.GetFullPath("../.local/foundry-road-diagnostic");
                    System.IO.Directory.CreateDirectory(output);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(output,"road-samples.json"),JsonUtility.ToJson(new FoundryRoadAudit{rows=audit},true));
                    int checkedCorners=0;
                    foreach(var home in map.Sites(profile).Where(s=>s.Kind==PlayableBuildingKind.Headquarters&&Math.Abs(s.Position.X)>1))
                    {
                        double extent=FoundryTerrainMesh.SiteEnvelope(home,profile);
                        // Inside the real hex but outside the former square exclusion,
                        // on the existing transverse allied road. Its concrete stays whole.
                        var q=AuthoredRoadApproachPoint(map,home,profile,settings);
                        var pad=FoundryTerrainMesh.SitePad(home,profile,settings);
                        Assert.IsTrue(pad.Contains(q));
                        var pixel=mask.GetPixelBilinear((float)(q.X/map.HalfExtent*.5+.5),(float)(q.Z/map.HalfExtent*.5+.5));
                        Assert.Less(pixel.r,.01f,"No road material cuts the concrete hex corner");
                        Assert.Greater(pixel.b,.9f,"Authored approach remains available to pad-edge finish");
                        checkedCorners++;
                    }
                    Assert.AreEqual(4,checkedCorners,"Both sides of each allied rear");
                    Assert.AreEqual(settings.roadValue,material.GetColor("_RoadTint").r,.001f);
                    Assert.AreEqual(settings.padEntryWear,material.GetVector("_FoundryReadability").w,.001f);
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(parent);}
        }
        [Serializable] private sealed class FoundryRoadAudit { public FoundryRoadAuditRow[] rows; }
        [Serializable] private sealed class FoundryRoadAuditRow
        {
            public int homeId,width,height;public double x,z,warpedX,warpedZ,legacyX,legacyZ,roadCenterZ,roadMinX,roadMaxX,roadMinZ,roadMaxZ;
            public float u,v,warpX,warpZ,referenceRoadCoverage,connectionCoverage,sampledRed,sampledBlue;public bool onPad;
        }
        private static NavObstacle TransverseRoadAt(FoundryMap map,NavPoint legacy)
            =>map.Roads.Single(r=>r.Contains(legacy)&&r.MaxX-r.MinX>r.MaxZ-r.MinZ);
        private static float NativeRoadWarpZ(double x,double z,FoundrySurfaceProfile settings)
            =>(Mathf.PerlinNoise((float)x/settings.roadBendScale+71,(float)z/settings.roadBendScale+11)-.5f)*2*settings.roadMeander;
        private static NavPoint AuthoredRoadApproachPoint(FoundryMap map,TerritorySite home,PlayableProfile profile,FoundrySurfaceProfile settings)
        {
            Assert.True(!float.IsNaN(settings.roadBendScale)&&!float.IsInfinity(settings.roadBendScale)&&settings.roadBendScale>0);
            Assert.True(!float.IsNaN(settings.roadMeander)&&!float.IsInfinity(settings.roadMeander)&&settings.roadMeander>=0);
            double extent=FoundryTerrainMesh.SiteEnvelope(home,profile),x=home.Position.X-Math.Sign(home.Position.X)*extent*1.2;
            var road=TransverseRoadAt(map,new NavPoint(x,Math.Sign(home.Position.Z)*106));
            double center=(road.MinZ+road.MaxZ)*.5,low=center-settings.roadMeander,high=center+settings.roadMeander;
            Assert.LessOrEqual(low+NativeRoadWarpZ(x,low,settings)-center,0,"Geometry-derived road centre must have a lower bracket");
            Assert.GreaterOrEqual(high+NativeRoadWarpZ(x,high,settings)-center,0,"Geometry-derived road centre must have an upper bracket");
            // Invert only the authored geometry/profile warp, never the mask or
            // coverage result. The old square remains excluded by the fixed X.
            for(int i=0;i<32;i++)
            {
                double middle=(low+high)*.5,error=middle+NativeRoadWarpZ(x,middle,settings)-center;
                Assert.True(!double.IsNaN(error)&&!double.IsInfinity(error));
                if(error<0)low=middle;else high=middle;
            }
            var point=new NavPoint(x,(low+high)*.5);
            float dx=(Mathf.PerlinNoise((float)point.X/settings.roadBendScale+17,(float)point.Z/settings.roadBendScale+53)-.5f)*2*settings.roadMeander;
            Assert.GreaterOrEqual(point.X+dx,road.MinX);Assert.LessOrEqual(point.X+dx,road.MaxX);
            Assert.Greater(Math.Abs(point.X-home.Position.X),extent,"Sample remains outside the former square exclusion");
            Assert.True(FoundryTerrainMesh.SitePad(home,profile,settings).Contains(point));
            return point;
        }
        [Test] public void SixHexagonalHomesContainCompleteSquareEnvelopesAndRemainSupported()
        {
            var profile=FoundryGreybox.LoadProfile();var map=(FoundryMap)profile.AuthoredMap;
            var settings=FoundrySurfaceProfile.Load();var homes=map.Sites(profile).Where(site=>site.Kind==PlayableBuildingKind.Headquarters).ToArray();
            Assert.AreEqual(6,homes.Length);
            double envelope=profile.SlotRingRadius+Math.Max(profile.ScienceFootprintRadius,Math.Max(profile.FactoryFootprintRadius,profile.RefineryFootprintRadius))+1;
            foreach(var home in homes)
            {
                var pad=FoundryTerrainMesh.HomePad(home.Position,envelope,settings);Assert.AreEqual(6,pad.Footprint.Vertices.Count);
                for(double x=-envelope;x<=envelope;x+=.5)for(double z=-envelope;z<=envelope;z+=.5)
                    Assert.IsTrue(pad.Contains(new NavPoint(home.Position.X+x,home.Position.Z+z)),"Full original construction envelope");
                foreach(var corner in pad.Footprint.Vertices)Assert.IsTrue(map.SupportsFootprint(corner,0),"Hex pad protrudes into a blocker or unsupported space");
            }
        }
        [Test] public void RoadMaskHasBroadConnectedCentersAndVisibleMeanders()
        {
            var map=new FoundryMap(new FoundryProfileData());var settings=FoundrySurfaceProfile.Load();
            foreach(var q in new[]{new NavPoint(0,0),new NavPoint(-75,0),new NavPoint(75,0),new NavPoint(0,35),new NavPoint(0,-35)})
                Assert.Greater(FoundryTerrainMesh.RoadCoverage(map,q,settings),.95,"Broad route center remains paved");
            var straight=new FoundrySurfaceProfile{roadMeander=0,roadFeather=settings.roadFeather,roadBendScale=settings.roadBendScale};
            float difference=0;for(int z=-80;z<=80;z++)difference+=Mathf.Abs(FoundryTerrainMesh.RoadCoverage(map,new NavPoint(8,z),settings)-FoundryTerrainMesh.RoadCoverage(map,new NavPoint(8,z),straight));
            Assert.Greater(difference,5,"Visible change along road edge, not only grain");
        }
        [Test] public void FourIndustrialDecksMatchFactoryBaseAndHaveNoSlabsAboveIt()
        {
            var map=new FoundryMap(new FoundryProfileData());var settings=FoundrySurfaceProfile.Load();
            foreach(var shape in map.Solids.Skip(map.Solids.Count-4))
            {
                var mesh=FoundryTerrainMesh.Rock(shape,settings,map.Lava,map.SurfaceHeight,true);
                try
                {
                    float deck=(float)shape.Top-Mathf.Max(.25f,settings.crownRelief*.55f);
                    Assert.AreEqual(deck,mesh.vertices.Max(v=>v.y),1e-4f);
                    Assert.Greater(mesh.vertices.Count(v=>Mathf.Abs(v.y-deck)<1e-4f),3);
                    foreach(var p in mesh.vertices)Assert.LessOrEqual(shape.Footprint.DistanceSquared(new NavPoint(p.x,p.z)),1e-8);
                }
                finally{UnityEngine.Object.DestroyImmediate(mesh);}
            }
        }
        [Test] public void AngularSlabsStayInsideSolidsAndLeaveEveryLavaRimOpen()
        {
            var map=new FoundryMap(new FoundryProfileData());var settings=FoundrySurfaceProfile.Load();
            for(int index=0;index<map.Solids.Count;index++)
            {
                var shape=map.Solids[index];var slabs=FoundryTerrainMesh.Blocks(shape,settings,map.Lava);
                if(index<3)Assert.IsNotEmpty(slabs,"Central masses and broad western ridge need actual angular forms");
                foreach(var slab in slabs)
                {
                    Assert.True(shape.Footprint.StrictlyContains(slab));
                    foreach(var channel in map.Lava)for(int i=0;i<slab.Vertices.Count;i++)Assert.GreaterOrEqual(channel.Footprint.SegmentDistanceSquared(slab.Vertices[i],slab.Vertices[(i+1)%slab.Vertices.Count]),settings.lavaCrustWidth*settings.lavaCrustWidth);
                }
            }
        }
        [Test] public void FracturedRidgesUseEveryAuthoritativePolygonExactly()
        {
            var map=new FoundryMap(new FoundryProfileData());var ridges=FoundryTerrainMesh.Ridges(map).ToArray();
            Assert.AreEqual(map.Solids.Count,ridges.Length);
            for(int i=0;i<ridges.Length;i++)
            {
                // NavObstacle is a value type: compare every geometric field and
                // retain identity of the actual polygon, rather than boxed copies.
                Assert.True(map.Solids[i].GeometryEquals(ridges[i]),"No box approximation or lost blocked corner");
                Assert.AreSame(map.Solids[i].Polygon,ridges[i].Polygon);
                CollectionAssert.AreEqual(map.Solids[i].Footprint.Vertices,ridges[i].Footprint.Vertices);
            }
            Assert.Greater(map.Solids[0].Footprint.Vertices.Count,4);
            Assert.Greater(map.Solids[2].Footprint.Vertices.Count,4);
        }
        [Test] public void SculptedVerticesStayInsideExistingBlockersWithIrregularCrowns()
        {
            var map=new FoundryMap(new FoundryProfileData());var settings=FoundrySurfaceProfile.Load();settings.Validate();
            foreach(var shape in FoundryTerrainMesh.Ridges(map))
            {
                var mesh=FoundryTerrainMesh.Rock(shape,settings);
                try
                {
                    Assert.Greater(mesh.vertexCount,100);
                    foreach(var v in mesh.vertices)
                    {
                        Assert.False(float.IsNaN(v.x)||float.IsInfinity(v.x)||float.IsNaN(v.y)||float.IsInfinity(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.z));
                        var q=new NavPoint(v.x,v.z);Assert.LessOrEqual(shape.Footprint.DistanceSquared(q),1e-8,"Visual rock protrudes into a route");
                        Assert.GreaterOrEqual(v.y,shape.Bottom-1e-4);Assert.LessOrEqual(v.y,shape.Top+1e-4);
                    }
                    var crown=mesh.vertices.Where(v=>v.y>shape.Bottom+settings.footHeight+1e-4).ToArray();
                    Assert.Greater(crown.Max(v=>v.y)-crown.Min(v=>v.y),.03f);
                    var vertices=mesh.vertices;var triangles=mesh.triangles;
                    for(int i=0;i<triangles.Length;i+=3)
                    {
                        var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];
                        if(a.y>settings.footHeight&&b.y>settings.footHeight&&c.y>settings.footHeight)
                            Assert.GreaterOrEqual(Vector3.Cross(b-a,c-a).y,-1e-4f,"No reversed crown triangles");
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(mesh);}
            }
        }
        [Test] public void DeterministicRockMeshAndProfileMetadataSurviveRepeatedLoads()
        {
            var map=new FoundryMap(new FoundryProfileData());var p=FoundrySurfaceProfile.Load();var shape=map.Solids[6];
            var a=FoundryTerrainMesh.Rock(shape,p);var b=FoundryTerrainMesh.Rock(shape,FoundrySurfaceProfile.Load());
            try{CollectionAssert.AreEqual(a.vertices,b.vertices);CollectionAssert.AreEqual(a.triangles,b.triangles);}finally{UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);}
            var parameters=p.Parameters().ToArray();Assert.AreEqual(parameters.Length,parameters.Select(v=>v.Path).Distinct().Count());
            foreach(var field in parameters){Assert.IsTrue(field.Path.StartsWith("visual.foundry."));Assert.Greater(field.Step,0);Assert.That(field.Read(),Is.InRange(field.Minimum,field.Maximum));}
            p.edgeErosion=float.NaN;Assert.Throws<ArgumentException>(()=>p.Validate());
        }
        [Test] public void MoltenChannelsHaveOpenCapsAndRecessedInwardWalls()
        {
            var map=new FoundryMap(new FoundryProfileData());var settings=FoundrySurfaceProfile.Load();
            foreach(var channel in map.Lava)
            {
                var ridge=FoundryFissureMesh.RidgeFor(map,channel);var mesh=FoundryTerrainMesh.Rock(ridge,settings,map.Lava);
                try
                {
                    var v=mesh.vertices;var t=mesh.triangles;int walls=0,caps=0;
                    for(int i=0;i<t.Length;i+=3)
                    {
                        var a=v[t[i]];var b=v[t[i+1]];var c=v[t[i+2]];var normal=Vector3.Cross(b-a,c-a);
                        var center=(a+b+c)/3;var q=new NavPoint(center.x,center.z);
                        if(normal.y>1e-5f&&center.y>FoundryFissureMesh.Level(map,channel,settings)){caps++;Assert.IsFalse(channel.Contains(q)&&channel.BoundaryDistanceSquared(q)>1e-7,"Stone cap covers molten channel: "+a+" / "+b+" / "+c+" normal="+normal+" channelX="+channel.MinX);}
                        if(center.y>FoundryFissureMesh.Level(map,channel,settings)&&Mathf.Abs(normal.y)<1e-5f&&Mathf.Max(a.y,Mathf.Max(b.y,c.y))-Mathf.Min(a.y,Mathf.Min(b.y,c.y))>.1f&&channel.BoundaryDistanceSquared(q)<1e-7)
                        {
                            walls++;var inward=center+normal.normalized*.01f;
                            Assert.IsTrue(channel.Contains(new NavPoint(inward.x,inward.z)),"Wall faces into the fissure");
                            Assert.GreaterOrEqual(center.y,FoundryFissureMesh.Level(map,channel,settings)-.006f);
                        }
                    }
                    Assert.Greater(caps,100);Assert.Greater(walls,40);
                    Assert.AreEqual((float)ridge.Top-settings.lavaDepth,FoundryFissureMesh.Level(map,channel,settings),1e-5f);
                }
                finally{UnityEngine.Object.DestroyImmediate(mesh);}
            }
        }
        [Test] public void RaisedDeckRockFeetSealTheirPerimeterAndKeepSupportedShoulders()
        {
            var map=new FoundryMap(new FoundryProfileData());var settings=FoundrySurfaceProfile.Load();
            foreach(int side in new[]{0,1})
            {
                var shape=map.Solids[side];
                var mesh=FoundryTerrainMesh.Rock(shape,settings,map.Lava,map.SurfaceHeight);
                try
                {
                    var vertices=mesh.vertices;var triangles=mesh.triangles;
                    Assert.Greater(vertices.Count(v=>shape.BoundaryDistanceSquared(new NavPoint(v.x,v.z))<1e-8&&Mathf.Abs(v.z)<24&&v.y>=map.SurfaceHeight(new NavPoint(v.x,v.z))-1e-4f),30,"Foot must reach the authored deck perimeter");
                    int checkedShoulders=0;
                    for(int i=0;i<triangles.Length;i+=3)
                    {
                        var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];
                        if(Vector3.Cross(b-a,c-a).normalized.y<.55f)continue;
                        foreach(var v in new[]{a,b,c})
                        {
                            if(Mathf.Abs(v.z)>=24||shape.BoundaryDistanceSquared(new NavPoint(v.x,v.z))>=settings.shoulderWidth*settings.shoulderWidth)continue;
                            checkedShoulders++;Assert.GreaterOrEqual(v.y,(float)map.SurfaceHeight(new NavPoint(v.x,v.z))-1e-4f,"Shoulder cannot sink behind the adjoining deck");
                        }
                    }
                    Assert.Greater(checkedShoulders,30);
                }
                finally{UnityEngine.Object.DestroyImmediate(mesh);}
            }
        }
        [Test] public void RealFoundryWorldHasNoPresentationCollidersOrMissingTextures()
        {
            var parent=new GameObject("Foundry art contract");
            try
            {
                var profile=FoundryGreybox.LoadProfile();var map=(FoundryMap)profile.AuthoredMap;
                using(var world=new PlayableWorld(parent.transform,profile))
                {
                    Assert.IsEmpty(parent.GetComponentsInChildren<Collider>());
                    Assert.AreEqual(18,map.Solids.Count,"Approved topology retains all eighteen authoritative rock volumes");
                    var rocks=parent.GetComponentsInChildren<MeshFilter>().Where(m=>m.sharedMesh.name=="Foundry fractured layered basalt").ToArray();Assert.AreEqual(map.Solids.Count,rocks.Length);
                    var groups=new[]{"Foundry industrial walls","Foundry oxidized roof","Foundry pipes and crane","Foundry weathered concrete","Foundry dark loading bay"};
                    var expectedFactories=Enumerable.Range(1,4).SelectMany(i=>groups.Select(g=>g+" factory "+i)).ToArray();
                    var actualFactories=parent.GetComponentsInChildren<MeshRenderer>().Where(r=>groups.Any(g=>r.name.StartsWith(g+" factory "))).Select(r=>r.name).ToArray();
                    CollectionAssert.AreEquivalent(expectedFactories,actualFactories,"Every factory retains all five exact consolidated material groups");
                    var material=rocks[0].GetComponent<Renderer>().sharedMaterial;
                    foreach(var name in new[]{"_EarthTex","_RockTex","_ConcreteTex","_SteelTex"})Assert.NotNull(material.GetTexture(name));
                    Assert.Greater(material.GetVector("_ArtDetail").x,0);
                    var settings=FoundrySurfaceProfile.Load();
                    Assert.Less(material.GetFloat("_FoundryCenterValue"),Mathf.GammaToLinearSpace(settings.concreteValue),"Central foundation must be darker in the shader color space");
                    var pads=parent.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Hexagonal steel home ")||r.name.StartsWith("Complete concrete site ")||r.name.StartsWith("Steel mine work yard ")).ToArray();
                    Assert.AreEqual(24,pads.Length);int centers=0;
                    foreach(var pad in pads){var block=new MaterialPropertyBlock();pad.GetPropertyBlock(block);if(block.GetFloat("_FoundryPadCenter")>0)centers++;}
                    Assert.AreEqual(10,centers,"Six headquarters and four outposts, no mine centers");
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(parent);}
        }
    }
}
