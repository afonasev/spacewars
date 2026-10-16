using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Tests.PlayMode
{
    public sealed class ThreeCrossingsMaterialTests
    {
        private static ThreeCrossingsMap Map()=>new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
        private static PlayableProfile Gameplay(ThreeCrossingsMap map)=>PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
        [Test] public void ShippedRoadCorridorsStayOnAcceptedSupport()
        {
            var map=Map();var p=ThreeCrossingsSurfaceProfile.Load();
            Assert.AreEqual(3,map.Revision);
            ThreeCrossingsMaterials.ValidateRoads(p,map);
            ThreeCrossingsMaterials.ValidateRoads(new ThreeCrossingsSurfaceProfile(),map);
            var gameplay=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            foreach(var mine in TerritoryRules.Sites(gameplay).Where(site=>site.Kind==PlayableBuildingKind.Mine))
                foreach(var road in ThreeCrossingsMaterials.Segments(p))
                {
                    double dx=road.B.X-road.A.X,dz=road.B.Z-road.A.Z,len=dx*dx+dz*dz;
                    double t=len>0?Math.Max(0,Math.Min(1,((mine.Position.X-road.A.X)*dx+(mine.Position.Z-road.A.Z)*dz)/len)):0;
                    double x=mine.Position.X-road.A.X-t*dx,z=mine.Position.Z-road.A.Z-t*dz;
                    Assert.Greater(Math.Sqrt(x*x+z*z),gameplay.MineFootprintRadius+p.roadWidth/2,"Road overlaps mine reserve "+mine.Id);
                }
            // Every base and every bridge belongs to the same connected road network.
            var edges=ThreeCrossingsMaterials.Segments(p).ToArray();
            var reachable=new System.Collections.Generic.List<NavPoint>{new NavPoint(-46,-46)};
            bool changed;do{changed=false;foreach(var e in edges)
            {
                bool a=reachable.Any(v=>v.X==e.A.X&&v.Z==e.A.Z),b=reachable.Any(v=>v.X==e.B.X&&v.Z==e.B.Z);
                if(a&&!b){reachable.Add(e.B);changed=true;}if(b&&!a){reachable.Add(e.A);changed=true;}
            }}while(changed);
            foreach(var target in new[]{new NavPoint(-46,46),new NavPoint(46,-46),new NavPoint(46,46),new NavPoint(19,0),new NavPoint(18,38),new NavPoint(18,-38)})
                Assert.IsTrue(reachable.Any(v=>v.X==target.X&&v.Z==target.Z),"Disconnected destination "+target.X+","+target.Z);
        }
        [Test] public void MetadataRejectsOutOfRangeAndNonFiniteValuesIncludingRoadCoordinates()
        {
            var p=ThreeCrossingsSurfaceProfile.Load();var fields=p.Parameters().ToArray();
            Assert.AreEqual(fields.Length,fields.Select(f=>f.Path).Distinct().Count());
            foreach(var f in fields)
            {
                Assert.IsNotEmpty(f.Group);Assert.IsNotEmpty(f.Label);Assert.IsNotEmpty(f.Description);Assert.IsNotEmpty(f.Unit);Assert.Greater(f.Step,0);
                float value=f.Read();
                foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,f.Minimum-f.Step,f.Maximum+f.Step})
                {f.Write(invalid);Assert.Throws<ArgumentException>(()=>p.Validate(),f.Path);}
                f.Write(value);
            }
            p.Validate();
            p.roads[0].points[0].x=0;
            Assert.Throws<ArgumentException>(()=>ThreeCrossingsMaterials.ValidateRoads(p,Map()),"Road through water must fail");
        }
        [Test] public void SurfaceRolesUseExistingMeshesAndShareWorldSamplingAndFog()
        {
            var root=new GameObject("Material test");
            var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),Map());
            try{using(var world=new PlayableWorld(root.transform,profile))
            {
                Material shared=null;var block=new MaterialPropertyBlock();int ground=0,concrete=0,metal=0;
                foreach(var r in root.GetComponentsInChildren<MeshRenderer>())
                {
                    r.GetPropertyBlock(block);int role=(int)block.GetFloat("_SurfaceRole");if(role==0)continue;
                    if(shared==null)shared=r.sharedMaterial;Assert.AreSame(shared,r.sharedMaterial);
                    Assert.AreSame(world.Fog.Texture,r.sharedMaterial.GetTexture("_FogMask"));
                    if(r.name.StartsWith("platform-")){Assert.AreEqual(3,role);Assert.AreEqual(1,block.GetVector("_PlatformSteel").x);var bounds=block.GetVector("_PlatformBounds");Assert.Greater(bounds.z,bounds.x);Assert.Greater(bounds.w,bounds.y);}
                    else Assert.AreEqual(0,block.GetVector("_PlatformSteel").x,"Steel finish variation is platform-only");
                    if(role==1)ground++;if(role==2)concrete++;if(role==3&&!r.name.StartsWith("Detail —"))metal++;
                    Assert.IsNull(r.GetComponent<Collider>());
                }
                Assert.AreEqual(2,ground);Assert.AreEqual(0,concrete);Assert.AreEqual(9+Map().Supports.Count(s=>s.Id.StartsWith("platform-"))+2*Map().Solids.Count(PlayableWorld.IsPlatformFence),metal); // Decks, ramps, foundation strips and fences.
                var mask=(Texture2D)shared.GetTexture("_RoadMask");
                Assert.Greater(mask.GetPixelBilinear((float)(-19.0/128+.5),.5f).r,.9f);
                Assert.Less(mask.GetPixelBilinear(.5f,.65f).r,.01f);
                Assert.AreEqual("three-crossings-materials-v1@22",world.SurfaceRevision);
                var surfaceProfile=ThreeCrossingsSurfaceProfile.Load();
                Assert.AreEqual(new Vector4(surfaceProfile.roadCoreShade,surfaceProfile.roadShoulderTone,surfaceProfile.roadRutStrength,surfaceProfile.roadEdgeVariation),shared.GetVector("_CrossingsRoadFinish"));
                Assert.AreEqual(surfaceProfile.minePadContactStrength,shared.GetVector("_CrossingsMinePad").y);
                Assert.AreEqual(surfaceProfile.minePadContactWidth,shared.GetVector("_CrossingsMinePad").z);
                Assert.Less(surfaceProfile.roadFeather,1,"Road shoulders must preserve a compacted center.");
                Assert.AreEqual("natural-frontier-v1@11",world.EnvironmentRevision);
            }}finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void ExpandedRoadPaintStopsAtSolidsAndFillsBridgeContacts()
        {
            var map=Map();var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try{using(var surfaces=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,material,Gameplay(map)))
            {
                var pixels=surfaces.RoadMask.GetPixels();int size=ThreeCrossingsMaterials.MaskSize;
                var gameplay=Gameplay(map);var mines=TerritoryRules.Sites(gameplay).Where(s=>s.Kind==PlayableBuildingKind.Mine).ToArray();
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    var p=new NavPoint(((x+.5)/size*2-1)*map.HalfExtent,((y+.5)/size*2-1)*map.HalfExtent);
                    bool reserve=mines.Any(m=>Math.Pow(p.X-m.Position.X,2)+Math.Pow(p.Z-m.Position.Z,2)<=gameplay.MineFootprintRadius*gameplay.MineFootprintRadius);
                    if(reserve||!map.SupportsFootprint(p,0))Assert.Less(pixels[y*size+x].r,.01f,"Paint on blocked/unsupported terrain");
                }
                foreach(var bridge in map.Supports.Where(s=>s.IsBridge))foreach(int side in new[]{-1,1})
                {
                    double x=side*(bridge.Bounds.MaxX+1),z=(bridge.Bounds.MinZ+bridge.Bounds.MaxZ)/2;
                    double half=(bridge.Bounds.MaxZ-bridge.Bounds.MinZ)/2;
                    foreach(double offset in new[]{-half+1,0,half-1})
                        Assert.Greater(surfaces.RoadMask.GetPixelBilinear((float)(x/128+.5),(float)((z+offset)/128+.5)).r,.7f,"Approach narrower than deck");
                }
            }}finally{UnityEngine.Object.DestroyImmediate(material);}
        }
        [Test] public void TruckWearReachesEveryBasePlatformAndPreservesSeparatedSand()
        {
            var map=Map();var profile=ThreeCrossingsSurfaceProfile.Load();
            var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try{using(var surfaces=new ThreeCrossingsMaterials(profile,map,material,Gameplay(map)))
            {
                surfaces.BuildWeathering(map,Array.Empty<ThreeCrossingsDetails.Placement>(),material);
                foreach(var support in map.Supports.Where(s=>s.Id.StartsWith("platform-")&&!s.Id.EndsWith("-ramp")))
                {
                    int worn=0,dusty=0,clean=0,patterned=0;
                    var mask=surfaces.WeatheringMask;int size=mask.width;
                    for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                    {
                        var point=new NavPoint(((x+.5)/size*2-1)*map.HalfExtent,((y+.5)/size*2-1)*map.HalfExtent);
                        if(!support.Bounds.Contains(point))continue;
                        var c=mask.GetPixel(x,y);
                        if(c.b>.5f){worn++;if(c.a>.15f&&c.a<.85f)patterned++;}
                        if(c.r>.2f)dusty++;if(c.r<.04f)clean++;
                    }
                    Assert.Greater(worn,40,"Truck lanes must enter "+support.Id);
                    Assert.Greater(patterned,10,"Wear must retain a tread modulation");
                    Assert.Greater(dusty,40,"Separated sand must reach "+support.Id);
                    Assert.Greater(clean,40,"Construction must remain exposed on "+support.Id);
                }
                float Sample(float x,float z)=>surfaces.WeatheringMask.GetPixelBilinear(x/128+.5f,z/128+.5f).b;
                Assert.Greater(Sample(-19-profile.truckWheelSpacing*.5f,-10),.45f,"Paired wheel path on the arterial road");
                Assert.Less(Sample(-19,-10),.1f,"Truck lanes must not become one filled road stripe");
            }}finally{UnityEngine.Object.DestroyImmediate(material);}
        }
        [Test] public void FrontierTreatmentIsOptInAndBindsBothMutedPalettes()
        {
            var map=Map();var profile=ThreeCrossingsSurfaceProfile.Load();
            var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try
            {
                FoundrySurfaceProfile.Load().Apply(material);
                Assert.AreEqual(0,material.GetVector("_CrossingsPalette").x,"Other maps must keep their own finish");
                using(var surfaces=new ThreeCrossingsMaterials(profile,map,material,Gameplay(map)))
                {
                    Assert.AreEqual(1,material.GetVector("_CrossingsPalette").x);
                    Assert.AreEqual(profile.paletteWidth,material.GetVector("_CrossingsPalette").w);
                    Assert.AreEqual(new Vector4(1,profile.rockProjectionTransition,0,0),material.GetVector("_CrossingsRockProjection"));
                    Assert.AreEqual(new Vector4(1,profile.groundOpenDetail,profile.groundFootDetail,profile.groundGrainRetention),material.GetVector("_CrossingsGroundDetail"));
                    Assert.AreEqual(new Vector4(profile.rockFootContactWidth,profile.rockFootContactStrength,profile.rockFootDustReach,profile.rockFootDustStrength),material.GetVector("_CrossingsRockFoot"));
                    Assert.AreEqual(profile.rockFootPatchScale,material.GetFloat("_CrossingsFootPatch"));
                    Assert.AreEqual(profile.concreteDetailContrast,material.GetVector("_CrossingsInfrastructure").z);
                    Assert.AreEqual(new Vector4(QualitySettings.activeColorSpace==ColorSpace.Linear?Mathf.GammaToLinearSpace(profile.mineYardValue):profile.mineYardValue,profile.minePadContactStrength,profile.minePadContactWidth,0),material.GetVector("_CrossingsMinePad"));
                    Assert.AreEqual(new Vector4(profile.roadCoreShade,profile.roadShoulderTone,profile.roadRutStrength,profile.roadEdgeVariation),material.GetVector("_CrossingsRoadFinish"));
                    Assert.AreNotEqual(material.GetColor("_BasaltTint"),material.GetColor("_RustTint"));
                    Assert.Greater(material.GetVector("_CrossingsConstruction").w,.5f,"Wheels must expose construction under sand");
                    Color.RGBToHSV(material.GetColor("_RustTint"),out float h,out float s,out float v);
                    Assert.Less(s,.4f,"Quarry rock must remain muted");
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(material);}
        }
        [Test] public void MineYardsAndApproachesFollowActualSitesAndRemainSupported()
        {
            var map=Map();var gameplay=Gameplay(map);var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try{using(var surfaces=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,material,gameplay))
            {
                var sites=TerritoryRules.Sites(gameplay).Where(s=>s.Kind==PlayableBuildingKind.Mine).ToArray();
                Assert.AreEqual(sites.Length,surfaces.MineAccesses.Length);Assert.AreEqual(6,sites.Length);
                foreach(var access in surfaces.MineAccesses)
                {
                    Assert.IsTrue(sites.Any(s=>s.Id==access.Site.Id&&s.Position.X==access.Site.Position.X&&s.Position.Z==access.Site.Position.Z));
                    Assert.IsTrue(map.SupportsSweep(access.Connection,access.Entry,surfaces.Profile.mineAccessWidth/2));
                    Assert.Greater(surfaces.ConstructionMask.GetPixelBilinear((float)(access.Site.Position.X/128+.5),(float)(access.Site.Position.Z/128+.5)).g,.9f);
                    Assert.Less(surfaces.RoadMask.GetPixelBilinear((float)(access.Site.Position.X/128+.5),(float)(access.Site.Position.Z/128+.5)).r,.01f,"Mine core stays protected");
                    for(int i=0;i<=10;i++)
                    {
                        float t=i/10f,x=(float)(access.Connection.X+(access.Entry.X-access.Connection.X)*t),z=(float)(access.Connection.Z+(access.Entry.Z-access.Connection.Z)*t);
                        Assert.Greater(surfaces.RoadMask.GetPixelBilinear(x/128+.5f,z/128+.5f).r,.6f,"Access branch must actually join the road");
                    }
                }
            }}finally{UnityEngine.Object.DestroyImmediate(material);}
        }
        [Test] public void BridgeSandHasEntryProvenanceAndNoCentralIslands()
        {
            var map=Map();var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try{using(var surfaces=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,material,Gameplay(map)))
            {
                surfaces.BuildWeathering(map,Array.Empty<ThreeCrossingsDetails.Placement>(),material);
                foreach(var bridge in map.Supports.Where(s=>s.IsBridge))
                {
                    for(double x=bridge.Bounds.MinX+surfaces.Profile.bridgeSandReach+.5;x<bridge.Bounds.MaxX-surfaces.Profile.bridgeSandReach-.5;x+=.5)
                    for(double z=bridge.Bounds.MinZ+.5;z<bridge.Bounds.MaxZ-.5;z+=.5)
                        Assert.Less(surfaces.WeatheringMask.GetPixelBilinear((float)(x/128+.5),(float)(z/128+.5)).r,.01f,"No random sand island inside bridge deck");
                    double centerZ=(bridge.Bounds.MinZ+bridge.Bounds.MaxZ)/2;
                    Assert.Less(surfaces.WeatheringMask.GetPixelBilinear((float)((bridge.Bounds.MinX+.5)/128+.5),(float)(centerZ/128+.5)).r,.01f,"Entry traffic corridor remains clear");
                }
            }}finally{UnityEngine.Object.DestroyImmediate(material);}
        }
        [Test] public void TwoHeavyWrecksKeepTheirFullGeometryOutsideProtectedEnvelopes()
        {
            var map=Map();var gameplay=Gameplay(map);var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try{using(var surfaces=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,material,gameplay))
            {
                var plan=ThreeCrossingsDetails.Plan(map,gameplay,surfaces).Where(p=>p.Kind=="HeavyTractorWreck").ToArray();Assert.AreEqual(2,plan.Length);
                foreach(var wreck in plan)
                {
                    var mesh=ThreeCrossingsDetails.Build(new[]{wreck},surfaces.Art,false,true,surfaces.Profile.tractorScale);
                    try
                    {
                        Assert.Greater(mesh.vertexCount,4000,"Heavy machine must have real wheels and machinery");
                        Assert.Greater(mesh.bounds.size.z,8,"Industrial tractor is much larger than combat units");
                        foreach(var v in mesh.vertices)
                        {
                            Assert.IsTrue(map.SupportsFootprint(new NavPoint(v.x,v.z),surfaces.Art.detailClearance),"Full wreck footprint must be supported with clearance");
                            Assert.Less(surfaces.RoadMask.GetPixelBilinear(v.x/128+.5f,v.z/128+.5f).r,.015f);
                            Assert.Less(surfaces.ConstructionMask.GetPixelBilinear(v.x/128+.5f,v.z/128+.5f).g,.01f);
                            foreach(var site in TerritoryRules.Sites(gameplay))Assert.Greater(Math.Sqrt(Math.Pow(v.x-site.Position.X,2)+Math.Pow(v.z-site.Position.Z,2)),TerritoryRules.Radius(gameplay,site.Kind)+surfaces.Art.detailClearance);
                        }
                    }
                    finally{UnityEngine.Object.DestroyImmediate(mesh);}
                }
            }}finally{UnityEngine.Object.DestroyImmediate(material);}
        }
        [Test] public void GeologicalDressingRemainsInsideAuthoritativeRockVolumes()
        {
            var root=new GameObject("Rock envelope test");var map=Map();
            var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            try{using(var world=new PlayableWorld(root.transform,p))
            {
                var block=new MaterialPropertyBlock();int rocks=0;
                foreach(var r in root.GetComponentsInChildren<MeshRenderer>())
                {
                    r.GetPropertyBlock(block);if(block.GetFloat("_SurfaceRole")!=4||r.name.StartsWith("Detail —"))continue;rocks++;
                    var mesh=r.GetComponent<MeshFilter>().sharedMesh;Assert.Greater(mesh.vertexCount,100);
                    foreach(var v in mesh.vertices)
                        Assert.IsTrue(map.Solids.Any(s=>v.y>=s.Bottom-.001&&v.y<=s.Top+.001&&(s.Contains(new NavPoint(v.x,v.z))||s.BoundaryDistanceSquared(new NavPoint(v.x,v.z))<.000001)),"Dressing extends into a route or above cover");
                }
                Assert.Greater(rocks,0);
            }}finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void EdgeCompoundsHaveVariedScaleGroundSupportAndClearRoutes()
        {
            var map=Map();var gameplay=Gameplay(map);var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try{using(var surfaces=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,material,gameplay))
            {
                var compounds=ThreeCrossingsDetails.Plan(map,gameplay,surfaces).Where(p=>p.Kind.StartsWith("Perimeter")).ToArray();
                Assert.AreEqual(12,compounds.Length);Assert.AreEqual(4,compounds.Count(p=>p.Kind=="PerimeterHangar"));Assert.Greater(compounds.Max(p=>p.Scale),compounds.Min(p=>p.Scale));
                foreach(var p in compounds)
                {
                    Assert.Greater(Mathf.Max(Mathf.Abs(p.Position.x),Mathf.Abs(p.Position.z)),(float)map.HalfExtent*.65f);Assert.AreEqual(0,p.Position.y);
                    var local=CorporateOutpostModules.Compound(p.Kind);var mesh=ThreeCrossingsDetails.Build(new[]{p},surfaces.Art,false,false,1,false,1,true);
                    try
                    {
                        Assert.Greater(mesh.vertexCount,100);if(p.Kind=="PerimeterHangar"){Assert.Greater(local.bounds.size.z,5);Assert.Greater(Mathf.Max(mesh.bounds.size.x,mesh.bounds.size.z),5);Assert.Greater(mesh.bounds.size.y,2.8);}
                        Assert.IsTrue(ThreeCrossingsDetails.CompoundClear(map,gameplay,surfaces,local.bounds,p.Position,p.Scale,p.Rotation));
                        Assert.Less(mesh.bounds.min.y,.1);Assert.Greater(mesh.bounds.max.y,1);
                        foreach(var v in mesh.vertices)Assert.IsTrue(map.SupportsFootprint(new NavPoint(v.x,v.z),surfaces.Art.detailClearance),"Entire building and loading apron must be accessible bank ground");
                    }
                    finally{UnityEngine.Object.DestroyImmediate(local);UnityEngine.Object.DestroyImmediate(mesh);}
                }
            }}finally{UnityEngine.Object.DestroyImmediate(material);}
        }
        [Test] public void HangarSpursReachRoadsAndPreserveFullSupportedWidth()
        {
            var map=Map();var gameplay=Gameplay(map);var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try{using(var surfaces=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,material,gameplay))
            {
                var placements=ThreeCrossingsDetails.Plan(map,gameplay,surfaces);Assert.AreEqual(4,surfaces.HangarAccesses.Count);
                foreach(var access in surfaces.HangarAccesses)
                {
                    var hangar=placements.Single(p=>p.SiteId==access.Group&&p.Kind=="PerimeterHangar");
                    var local=CorporateOutpostModules.Compound(hangar.Kind);
                    try{var gate=hangar.Position+Quaternion.Euler(0,hangar.Rotation,0)*new Vector3(0,0,local.bounds.min.z*hangar.Scale);Assert.AreEqual(gate.x,access.Gate.x,.001f);Assert.AreEqual(gate.z,access.Gate.y,.001f);}finally{UnityEngine.Object.DestroyImmediate(local);}
                    var end=access.Points.Last();
                    Assert.IsTrue(ThreeCrossingsMaterials.Segments(surfaces.Profile).Any(s=>
                        Math.Abs((end.x-s.A.X)*(s.B.Z-s.A.Z)-(end.y-s.A.Z)*(s.B.X-s.A.X))<.001 && end.x>=Math.Min(s.A.X,s.B.X)-.001 && end.x<=Math.Max(s.A.X,s.B.X)+.001 && end.y>=Math.Min(s.A.Z,s.B.Z)-.001 && end.y<=Math.Max(s.A.Z,s.B.Z)+.001),"Spur joins existing network");
                    foreach(var segment in access.Segments)
                    {
                        Assert.IsTrue(map.SupportsSweep(segment.A,segment.B,surfaces.Profile.hangarAccessWidth/2+surfaces.Art.detailClearance),"Full width and unchanged clearance");
                        int n=Mathf.CeilToInt((float)Math.Sqrt(Math.Pow(segment.A.X-segment.B.X,2)+Math.Pow(segment.A.Z-segment.B.Z,2))/.25f);
                        for(int i=0;i<=n;i++)
                        {
                            var point=new Vector2(Mathf.Lerp((float)segment.A.X,(float)segment.B.X,i/(float)n),Mathf.Lerp((float)segment.A.Z,(float)segment.B.Z,i/(float)n));
                            Assert.IsTrue(surfaces.ProtectedGround(map,gameplay,point,surfaces.Profile.hangarAccessWidth/2));
                        }
                    }
                    foreach(var item in placements.Where(p=>p.Kind=="Boulder"))Assert.IsFalse(surfaces.AccessOccupied(new Vector2(item.Position.x,item.Position.z),item.Radius),"No new detail encroaches on a driveway");
                }
                surfaces.PaintHangarAccesses(map,material);surfaces.BuildWeathering(map,placements,material);
                foreach(var access in surfaces.HangarAccesses)
                {
                    var center=access.Gate+access.Direction*surfaces.Profile.hangarApronLength/2;
                    Assert.Greater(surfaces.ConstructionMask.GetPixelBilinear(center.x/128+.5f,center.y/128+.5f).g,.9f,"Actual gate loading apron");
                    var wheel=center+access.Right*surfaces.Profile.truckWheelSpacing/2;
                    Assert.Greater(surfaces.WeatheringMask.GetPixelBilinear(wheel.x/128+.5f,wheel.y/128+.5f).b,.4f,"Paired wheel wear at loading gate");
                }
            }}finally{UnityEngine.Object.DestroyImmediate(material);}
        }
        [Test] public void MineServiceHardwareAndBridgeFittingsProtectGameplayGeometry()
        {
            var map=Map();var gameplay=Gameplay(map);var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try{using(var surfaces=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,material,gameplay))
            {
                var plan=ThreeCrossingsDetails.Plan(map,gameplay,surfaces);var modules=plan.Where(p=>CorporateOutpostModules.IsModule(p.Kind)).ToArray();
                Assert.AreEqual(12,modules.Length);Assert.AreEqual(6,modules.Select(p=>p.SiteId).Distinct().Count());
                foreach(var module in modules)
                {
                    var original=CorporateOutpostModules.Build(module.Kind);var detailed=CorporateOutpostModules.Build(module.Kind,surfaces.Profile.mineUtilityScale);
                    var mesh=ThreeCrossingsDetails.Build(new[]{module},surfaces.Art,false,false,1,true,surfaces.Profile.industrialScale,false,surfaces.Profile.mineUtilityScale);
                    try{Assert.Greater(detailed.vertices.Skip(original.vertexCount).Count(v=>v.y>(module.Kind=="CargoModule"?.24f:.20f)+.05f&&v.z<-.685f),100,"Process pipes must sit visibly above the existing solid base, outside the casing");Assert.Greater(detailed.vertexCount,original.vertexCount+500);Assert.That(detailed.bounds.min.x,Is.GreaterThanOrEqualTo(original.bounds.min.x-.001f));Assert.That(detailed.bounds.max.x,Is.LessThanOrEqualTo(original.bounds.max.x+.001f));Assert.That(detailed.bounds.min.z,Is.GreaterThanOrEqualTo(original.bounds.min.z-.001f));Assert.That(detailed.bounds.max.z,Is.LessThanOrEqualTo(original.bounds.max.z+.001f));foreach(var v in mesh.vertices)
                    {
                        Assert.IsTrue(surfaces.ProtectedGround(map,gameplay,new Vector2(v.x,v.z),0));
                        Assert.Less(surfaces.RoadMask.GetPixelBilinear(v.x/128+.5f,v.z/128+.5f).r,.015f);
                        Assert.IsFalse(surfaces.AccessOccupied(new Vector2(v.x,v.z),0));
                    }}finally{UnityEngine.Object.DestroyImmediate(original);UnityEngine.Object.DestroyImmediate(detailed);UnityEngine.Object.DestroyImmediate(mesh);}
                }
                foreach(var bridge in map.Supports.Where(s=>s.IsBridge))
                {
                    var original=CorporateOutpostModules.BridgeSteelwork(bridge,map);var detailed=CorporateOutpostModules.BridgeSteelwork(bridge,map,0,surfaces.Profile);
                    try{Assert.Greater(detailed.vertexCount,original.vertexCount);Assert.Less(detailed.bounds.min.y,original.bounds.min.y);
                        foreach(var v in detailed.vertices)Assert.IsTrue(v.z<bridge.Bounds.MinZ||v.z>bridge.Bounds.MaxZ,"External fittings preserve full corridor");}
                    finally{UnityEngine.Object.DestroyImmediate(original);UnityEngine.Object.DestroyImmediate(detailed);}
                }
            }}finally{UnityEngine.Object.DestroyImmediate(material);}
        }
        [Test] public void PlatformFencesStayInsideExistingBlockersAndKeepOpenGaps()
        {
            var map=Map();var profile=ThreeCrossingsSurfaceProfile.Load();
            float deck=(float)map.Supports.First(s=>s.Id.StartsWith("platform-")&&!s.Id.EndsWith("-ramp")).Height;
            var walls=map.Solids.Where(PlayableWorld.IsPlatformFence).ToArray();Assert.AreEqual(56,walls.Length);
            foreach(var wall in walls)
            {
                var mesh=PlayableWorld.PlatformFence(wall,deck,profile);
                try
                {
                    Assert.Greater(mesh.vertexCount,100);
                    Assert.AreEqual(deck,mesh.bounds.min.y,.001f);Assert.AreEqual(wall.Top,mesh.bounds.max.y,.001);
                    foreach(var v in mesh.vertices)Assert.IsTrue(wall.Contains(new NavPoint(v.x,v.z))||wall.BoundaryDistanceSquared(new NavPoint(v.x,v.z))<.000001,"Fence must stay inside the existing blocker, including beside gates");
                    // Each face belongs to a narrow bar: no solid wall spanning a section.
                    var vertices=mesh.vertices;
                    for(int i=0;i<vertices.Length;i+=3)Assert.LessOrEqual(Mathf.Min((vertices[i+1]-vertices[i]).magnitude,Mathf.Min((vertices[i+2]-vertices[i]).magnitude,(vertices[i+2]-vertices[i+1]).magnitude)),profile.platformFenceWidth*1.5f);
                }
                finally{UnityEngine.Object.DestroyImmediate(mesh);}
            }
        }
        [Test] public void MetalBridgeLatticeIsOpenAndOutsideDriveableWidth()
        {
            var map=Map();
            foreach(var support in map.Supports.Where(s=>s.IsBridge))
            {
                var mesh=CorporateOutpostModules.BridgeSteelwork(support,map);
                try{Assert.Greater(mesh.vertexCount,300);Assert.Less(mesh.bounds.min.y,0);Assert.Greater(mesh.bounds.max.y,map.RailHeight*.95);
                    foreach(var v in mesh.vertices)Assert.IsTrue(v.z<support.Bounds.MinZ||v.z>support.Bounds.MaxZ,"Rails must leave entire driving corridor clear");
                    var vertices=mesh.vertices;foreach(var v in vertices)Assert.Less(Mathf.Min(Mathf.Abs(v.z-(float)support.Bounds.MinZ),Mathf.Abs(v.z-(float)support.Bounds.MaxZ)),map.RailThickness*.4,"Slim steel members replace concrete walls");
                }finally{UnityEngine.Object.DestroyImmediate(mesh);}
            }
        }
        [Test] public void CorporateModulesFollowEveryMineAndProtectWholeGeometry()
        {
            var map=Map();var gameplay=Gameplay(map);var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try{using(var surfaces=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,material,gameplay))
            {
                var modules=ThreeCrossingsDetails.Plan(map,gameplay,surfaces).Where(p=>CorporateOutpostModules.IsModule(p.Kind)).ToArray();
                Assert.AreEqual(12,modules.Length);
                foreach(var site in TerritoryRules.Sites(gameplay).Where(s=>s.Kind==PlayableBuildingKind.Mine))
                {
                    var pair=modules.Where(p=>p.SiteId==site.Id).ToArray();Assert.AreEqual(2,pair.Length);
                    CollectionAssert.AreEquivalent(new[]{"CargoModule","ExtractionSkid"},pair.Select(p=>p.Kind));
                    foreach(var p in pair)
                    {
                        Assert.LessOrEqual(Vector2.Distance(new Vector2(p.Position.x,p.Position.z),new Vector2((float)site.Position.X,(float)site.Position.Z)),surfaces.Profile.industrialReach+.001f);
                        var mesh=ThreeCrossingsDetails.Build(new[]{p},surfaces.Art,false,false,1,true,surfaces.Profile.industrialScale);
                        try{Assert.Greater(mesh.vertexCount,150);foreach(var v in mesh.vertices)
                        {
                            Assert.IsTrue(map.SupportsFootprint(new NavPoint(v.x,v.z),surfaces.Art.detailClearance));
                            Assert.Less(surfaces.RoadMask.GetPixelBilinear(v.x/128+.5f,v.z/128+.5f).r,.015f);
                            Assert.Less(surfaces.ConstructionMask.GetPixelBilinear(v.x/128+.5f,v.z/128+.5f).g,.01f);
                            foreach(var reserve in TerritoryRules.Sites(gameplay))
                            {
                                Assert.Greater(Vector2.Distance(new Vector2(v.x,v.z),new Vector2((float)reserve.Position.X,(float)reserve.Position.Z)),TerritoryRules.Radius(gameplay,reserve.Kind)+surfaces.Art.detailClearance);
                                foreach(var slot in reserve.Slots)Assert.Greater(Vector2.Distance(new Vector2(v.x,v.z),new Vector2((float)slot.Position.X,(float)slot.Position.Z)),gameplay.OrdinaryPadRadius+surfaces.Art.detailClearance);
                            }
                        }}finally{UnityEngine.Object.DestroyImmediate(mesh);}
                    }
                }
            }}finally{UnityEngine.Object.DestroyImmediate(material);}
        }
        [Test] public void DetailModulesProtectTravelBuildSitesAndShareFog()
        {
            var map=Map();var gameplay=Gameplay(map);var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try{using(var surfaces=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,material,gameplay))
            {
                var plan=ThreeCrossingsDetails.Plan(map,gameplay,surfaces);
                Assert.Greater(plan.Count(p=>p.Kind=="Boulder"),30,"Cliff clusters must actually populate");
                var stonesInPlan=plan.Where(p=>p.Kind=="Boulder").ToArray();
                Assert.Greater(stonesInPlan.Count(p=>p.Radius>=surfaces.Art.boulderRadius*.8f),5,"Selected feet need prominent fragments");
                Assert.Greater(stonesInPlan.Count(p=>stonesInPlan.Any(q=>q.Position!=p.Position&&Vector3.Distance(p.Position,q.Position)<p.Radius+q.Radius+1)),stonesInPlan.Length/2,"Most debris belongs to close groups");
                Assert.AreEqual(3,plan.Count(p=>p.Kind=="PumpNode"||p.Kind=="VentilationBlock"||p.Kind=="CableCabinet"),"All authored service anchors must resolve");
                foreach(var p in plan)
                {
                    if(p.Kind.StartsWith("Perimeter"))continue; // Full ground building footprints are checked separately.
                    var point=new NavPoint(p.Position.x,p.Position.z);
                    if(p.Kind!="HeavyTractorWreck")Assert.IsTrue(map.SupportsFootprint(point,p.Radius+surfaces.Art.detailClearance));
                    foreach(var site in TerritoryRules.Sites(gameplay))
                        Assert.Greater(Math.Sqrt(Math.Pow(point.X-site.Position.X,2)+Math.Pow(point.Z-site.Position.Z,2)),TerritoryRules.Radius(gameplay,site.Kind)+p.Radius);
                    foreach(bool stones in new[]{true,false})
                    {
                        if((p.Kind=="Boulder")!=stones)continue;
                        var mesh=ThreeCrossingsDetails.Build(new[]{p},surfaces.Art,stones,p.Kind=="HeavyTractorWreck",surfaces.Profile.tractorScale,CorporateOutpostModules.IsModule(p.Kind),surfaces.Profile.industrialScale);
                        foreach(var v in mesh.vertices)
                        {
                            Assert.IsTrue(map.SupportsFootprint(new NavPoint(v.x,v.z),0),"Module overhangs solid or water");
                            Assert.Less(surfaces.RoadMask.GetPixelBilinear(v.x/128+.5f,v.z/128+.5f).r,.02f,"Detail obscures route");
                        }
                        UnityEngine.Object.DestroyImmediate(mesh);
                    }
                }
                surfaces.BuildWeathering(map,plan,material);
                Assert.AreSame(surfaces.WeatheringMask,material.GetTexture("_WeatheringMask"));
                Assert.Greater(surfaces.WeatheringMask.GetPixels().Count(c=>c.r>.2f),100);
                Assert.Less(surfaces.WeatheringMask.GetPixelBilinear(.5f,.5f).r,.01f,"Bridge center remains exposed metal");
                int cleanEdge=0,dustyEdge=0;
                for(int i=0;i<100;i++)
                {
                    float x=Mathf.Lerp(-11.7f,11.7f,i/99f);
                    foreach(float z in new[]{-6.7f,6.7f})
                    {
                        float dust=surfaces.WeatheringMask.GetPixelBilinear(x/128+.5f,z/128+.5f).r;
                        if(dust<.03f)cleanEdge++;if(dust>.2f)dustyEdge++;
                    }
                }
                Assert.Greater(cleanEdge,40,"Deck perimeter must have substantial clean breaks");
                Assert.Greater(dustyEdge,4,"Separated deposits must still reach the deck");
            }}finally{UnityEngine.Object.DestroyImmediate(material);}
            var root=new GameObject("Detail fog test");
            try{using(var world=new PlayableWorld(root.transform,gameplay))
            {
                var details=root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Detail —")).ToArray();Assert.AreEqual(6,details.Length);Assert.AreEqual(1,details.Count(r=>r.name=="Detail — parked service equipment"));
                foreach(var r in details){Assert.IsNull(r.GetComponent<Collider>());Assert.AreSame(world.Fog.Texture,r.sharedMaterial.GetTexture("_FogMask"));}
                world.InspectionHide();foreach(var r in details)Assert.AreSame(Texture2D.whiteTexture,r.sharedMaterial.GetTexture("_FogMask"));
            }}finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void EnvironmentParametersRejectNonFiniteAndOutOfRangeValues()
        {
            var art=EnvironmentArtProfile.Load();var fields=art.Parameters().ToArray();
            Assert.AreEqual(fields.Length,fields.Select(f=>f.Path).Distinct().Count());
            foreach(var field in fields)
            {
                float original=field.Read();Assert.IsNotEmpty(field.Description);Assert.Greater(field.Step,0);
                foreach(float invalid in new[]{float.NaN,float.NegativeInfinity,field.Minimum-field.Step,field.Maximum+field.Step})
                {field.Write(invalid);Assert.Throws<ArgumentException>(()=>art.Validate(),field.Path);}
                field.Write(original);
            }
            art.Validate();
        }
        [Test] public void NaturalFootprintsExcludeConstructedPlatformsAndFadeOutsideRocks()
        {
            var map=Map();var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try{using(var surfaces=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,material,Gameplay(map)))
            {
                float Sample(double x,double z)=>surfaces.RockMask.GetPixelBilinear((float)(x/(map.HalfExtent*2)+.5),(float)(z/(map.HalfExtent*2)+.5)).r*EnvironmentArt.ShoreDistanceRange;
                var rock=map.Solids.First(r=>r.Top>=map.WaterDepth&&r.Bottom<=0&&r.MaxX-r.MinX<20);
                Assert.Less(Sample((rock.MinX+rock.MaxX)/2,(rock.MinZ+rock.MaxZ)/2),.3f);
                Assert.Greater(Sample(-46,-46),surfaces.Art.naturalBlendWidth,"Base platform must not paint a rock apron");
                bool intermediate=false;
                foreach(var c in surfaces.RockMask.GetPixels())if(c.r>.02f&&c.r<surfaces.Art.naturalBlendWidth/EnvironmentArt.ShoreDistanceRange) {intermediate=true;break;}
                Assert.IsTrue(intermediate,"Natural contact mask needs a graded apron, not a binary boundary");
                Assert.AreSame(surfaces.RockMask,material.GetTexture("_RockMask"));
            }}finally{UnityEngine.Object.DestroyImmediate(material);}
        }
        [Test] public void WaterSharesFogAndUsesBankDistanceInsteadOfBridgeEdges()
        {
            var root=new GameObject("Environment test");var map=Map();
            var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            try{using(var world=new PlayableWorld(root.transform,p))
            {
                var block=new MaterialPropertyBlock();int count=0;
                foreach(var r in root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("River")))
                {
                    r.GetPropertyBlock(block);Assert.AreEqual(5,block.GetFloat("_SurfaceRole"));count++;
                    Assert.AreSame(world.Fog.Texture,r.sharedMaterial.GetTexture("_FogMask"));
                    Assert.IsNull(r.GetComponent<Collider>());
                    foreach(string key in new[]{"Earth","Rock","Concrete","Steel"})
                    {
                        var texture=(Texture2D)r.sharedMaterial.GetTexture("_"+key+"Tex");
                        Assert.IsNotNull(texture);Assert.Greater(texture.mipmapCount,1);Assert.AreEqual(TextureWrapMode.Repeat,texture.wrapMode);
                    }
                    var shore=(Texture2D)r.sharedMaterial.GetTexture("_ShoreMask");
                    Assert.Greater(shore.GetPixelBilinear(.5f,.5f).r,.9f,"Bridge must not create a false riverbank at channel center");
                    Assert.Greater(shore.GetPixelBilinear((float)(-34.5/128+.5),(float)(-46.0/128+.5)).r,.9f,"Inland platform edge is not a riverbank");
                    Assert.IsFalse(float.IsNaN(r.sharedMaterial.GetVector("_WaterMotion").x));
                }
                Assert.Greater(count,0);world.InspectionHide();
                foreach(var r in root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("River")))
                    Assert.AreSame(Texture2D.whiteTexture,r.sharedMaterial.GetTexture("_FogMask"));
            }}finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
