using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif
namespace Spacewars.Tests.PlayMode
{
    public sealed class BasaltCrownNativeTests
    {
        private static PlayableProfile Profile()=>PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new BasaltCrownMap());
        [Test] public void NativeWarmPaintAndImportsRetainAllSourceRoles(){
            var art=BasaltCrownArtProfile.Load();art.Validate();
            Assert.AreEqual(44,art.Parameters().Count());
            Assert.Throws<ArgumentException>(()=>BasaltCrownArtProfile.Role("unknown"));
            var invalid=BasaltCrownArtProfile.Load();invalid.detailTile=float.NaN;Assert.Throws<ArgumentException>(()=>invalid.Validate());
            var m=new BasaltCrownMap();CollectionAssert.AreEquivalent(BasaltCrownArtProfile.Roles,m.Materials.Select(p=>p.Material).Distinct());
            long memory=0;foreach(string family in new[]{"Dark","Stone","Sand"})foreach(string suffix in new[]{"","-b","-c"}){
                var path="BasaltCrownArt/"+family+suffix;var texture=Resources.Load<Texture2D>(path);Assert.IsNotNull(texture,path);
                Assert.AreEqual(1254,texture.width);Assert.AreEqual(1254,texture.height);Assert.Greater(texture.mipmapCount,1);
                Assert.AreEqual(TextureWrapMode.Repeat,texture.wrapMode);Assert.AreEqual(FilterMode.Trilinear,texture.filterMode);Assert.AreEqual(8,texture.anisoLevel);
                // Generated albedo is opaque RGB; no alpha channel or lossy compression.
                Assert.AreEqual(TextureFormat.RGB24,texture.format);memory+=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
#if UNITY_EDITOR
                var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));Assert.True(importer.sRGBTexture);Assert.AreEqual(TextureImporterCompression.Uncompressed,importer.textureCompression);
#endif
            }
            var masks=PlayableWorld.BuildBasaltPaintMasks(m,art);try{
                var a=masks[0].GetPixels();var b=masks[1].GetPixels();var maxima=new float[7];bool feather=false;
                for(int i=0;i<a.Length;i++){
                    float[] weights={a[i].r,a[i].g,a[i].b,a[i].a,b[i].r,b[i].g,b[i].b};Assert.That(weights.Sum(),Is.EqualTo(1).Within(4d/255));
                    for(int j=0;j<7;j++)maxima[j]=Math.Max(maxima[j],weights[j]);if(weights.Count(w=>w>.1f)>1)feather=true;
                }
                Assert.True(feather);Assert.True(maxima.All(v=>v>.5f),string.Join(",",maxima));
                string folder=Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,"docs/basalt-crown/site-aprons");Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,"native-import.json"),"{\"textures\":9,\"width\":1254,\"height\":1254,\"format\":\"RGB24\",\"mipmaps\":true,\"repeat\":true,\"filter\":\"Trilinear\",\"aniso\":8,\"runtime_bytes\":"+memory+"}\n");
            }finally{foreach(var mask in masks)UnityEngine.Object.DestroyImmediate(mask);}
        }
        [Test] public void CitadelCrestMaskMarksCliffsAndLeavesBothRampsClear()
        {
            var map = new BasaltCrownMap();
            var masks = PlayableWorld.BuildBasaltPaintMasks(map, BasaltCrownArtProfile.Load());
            try
            {
                float Crest(double x, double z) => masks[1].GetPixelBilinear(
                    (float)(x / (2 * map.HalfExtent) + .5), (float)(z / (2 * map.HalfExtent) + .5)).a;
                Assert.Greater(Crest(0, 24), .5f);
                foreach (int side in new[] { -1, 1 })
                {
                    Assert.Greater(Crest(side * 22, 10), .5f);
                    Assert.Zero(Crest(side * 22, 0));
                    Assert.Zero(Crest(side * 14, 0), "Adjacent plateau supports share an internal seam.");
                }
            }
            finally
            {
                foreach (var mask in masks) UnityEngine.Object.DestroyImmediate(mask);
            }
        }
        [Test] public void EngineeredRoadsAndErodedRidgesArePresentationOnly(){
            var p=Profile();var m=(BasaltCrownMap)p.AuthoredMap;var art=BasaltCrownArtProfile.Load();
            var host=new GameObject("Basalt presentation contract");
            var plaza=BasaltCrownRoads.Plaza(m,p);
            var blockers=m.MovementBlockers.ToArray();var supports=m.Supports.ToArray();var transitions=m.SurfaceTransitions.ToArray();
            var heights=new[]{new NavPoint(-22,0),new NavPoint(22,0),new NavPoint(0,0),new NavPoint(40,20)}.Select(m.SurfaceHeight).ToArray();
            using(var world=new PlayableWorld(host.transform,p)){
                Assert.IsEmpty(host.GetComponentsInChildren<Collider>(true));
                Assert.IsEmpty(host.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true));
                var rocks=host.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="Basalt ridge").ToArray();
                var solids=m.Solids.Where(b=>!double.IsNaN(b.Top)).ToArray();Assert.AreEqual(solids.Length,rocks.Length);
                for(int i=0;i<rocks.Length;i++){
                    var b=solids[i];var mesh=rocks[i].sharedMesh;
                    Assert.Greater(mesh.vertexCount,36,"Actual fractured geometry, not a renamed prism");
                    foreach(var v in mesh.vertices){
                        Assert.That(v.x,Is.InRange((float)b.MinX-.0001f,(float)b.MaxX+.0001f));
                        Assert.That(v.z,Is.InRange((float)b.MinZ-.0001f,(float)b.MaxZ+.0001f));
                        Assert.That(v.y,Is.InRange((float)b.Bottom-.0001f,(float)b.Top+.0001f));
                    }
                    var properties=new MaterialPropertyBlock();rocks[i].GetComponent<Renderer>().GetPropertyBlock(properties);
                    Assert.AreEqual(0,properties.GetFloat("_RoadSurface"),"Ridges cannot inherit road paint");
                }
                foreach(var point in new[]{new NavPoint(-22,0),new NavPoint(22,0),new NavPoint(0,0),new NavPoint(-40,18),new NavPoint(40,-18),new NavPoint(0,32),new NavPoint(0,-32)}){
                    Assert.Greater(BasaltCrownRoads.Coverage((float)point.X,(float)point.Z,art,plaza),.99f);
                    Assert.True(m.SupportsFootprint(point,p.TankCollisionRadius),"Paving follows existing routes: "+point);
                }
                Assert.AreEqual(0,BasaltCrownRoads.Coverage(0,14,art,plaza),"Central mine remains natural basalt");
                Assert.AreEqual(0,BasaltCrownRoads.Coverage(0,44,art,plaza),"No road painted through boundary ridge");
                CollectionAssert.AreEqual(blockers,m.MovementBlockers);CollectionAssert.AreEqual(supports,m.Supports);
                CollectionAssert.AreEqual(transitions,m.SurfaceTransitions);
                CollectionAssert.AreEqual(heights,new[]{new NavPoint(-22,0),new NavPoint(22,0),new NavPoint(0,0),new NavPoint(40,20)}.Select(m.SurfaceHeight));
            }
            UnityEngine.Object.DestroyImmediate(host);
        }
        [Test] public void BorderRocksStayOnStoneGroundAndKeepThreeCompactTiers()
        {
            var profile=Profile();
            var map=(BasaltCrownMap)profile.AuthoredMap;
            var playable=map.Supports.Single(support=>support.Id=="outer-ring").Bounds;
            var art=BasaltCrownArtProfile.Load();
            var pads=BasaltCrownDetails.SitePads(map,profile,art);
            var mines=BasaltCrownDetails.Mines(map,profile);
            var plaza=BasaltCrownRoads.Plaza(map,profile);
            var host=new GameObject("Basalt border rocks");
            try
            {
                using var world=new PlayableWorld(host.transform,profile);
                var groups=host.GetComponentsInChildren<ModelDetailLod>().Where(lod=>lod.name.StartsWith("Basalt border — ")).ToArray();
                Assert.AreEqual(8,groups.Length);
                Assert.IsEmpty(host.GetComponentsInChildren<Collider>());
                var totals=new long[3];
                foreach(var group in groups)
                {
                    var bounds=group.ReferenceBounds;
                    Assert.True(bounds.min.x>playable.MinX&&bounds.max.x<playable.MaxX&&
                        bounds.min.z>playable.MinZ&&bounds.max.z<playable.MaxZ);
                    NavPoint Closest(NavPoint point)=>new NavPoint(
                        Math.Max(bounds.min.x,Math.Min(bounds.max.x,point.X)),
                        Math.Max(bounds.min.z,Math.Min(bounds.max.z,point.Z)));
                    foreach(var pad in pads)
                        Assert.Greater(BasaltCrownDetails.PadDistance(Closest(pad.Center),pad),art.detailClearance);
                    foreach(var mine in mines)
                    {
                        var point=Closest(mine.Position);
                        double dx=point.X-mine.Position.X,dz=point.Z-mine.Position.Z;
                        double apronExtent=art.mineApronRadius+art.mineApronFeather+art.detailClearance;
                        Assert.Greater(Math.Max(Math.Max(Math.Abs(dx),Math.Abs(dz)),(Math.Abs(dx)+Math.Abs(dz))*.70710678),
                            apronExtent);
                    }
                    foreach(float x in new[]{bounds.min.x,bounds.center.x,bounds.max.x})
                    foreach(float z in new[]{bounds.min.z,bounds.center.z,bounds.max.z})
                    {
                        var point=new NavPoint(x,z);
                        Assert.True(map.SupportsFootprint(point,0));
                        Assert.Greater(BasaltCrownRoads.SignedDistance(x,z,art,plaza),art.detailClearance);
                        Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(point));
                    }
                    var tiers=new[]{group.OriginalMeshes.Single(),group.NormalMeshes.Single(),group.FarMeshes.Single()};
                    for(int i=0;i<tiers.Length;i++)
                    {
                        Assert.AreEqual(bounds,tiers[i].bounds);
                        CollectionAssert.AreEquivalent(new[]{new Color32(255,0,0,255),new Color32(0,255,0,255),new Color32(0,0,255,255)},
                            tiers[i].colors32.Distinct(), "Each detail level preserves all three basalt paint roles.");
                        group.SetDetail(i);
                        Assert.AreSame(tiers[i],group.GetComponent<MeshFilter>().sharedMesh);
                        totals[i]+=tiers[i].GetIndexCount(0)/3;
                    }
                    Assert.Greater(tiers[0].GetIndexCount(0),tiers[1].GetIndexCount(0));
                    Assert.Greater(tiers[1].GetIndexCount(0),tiers[2].GetIndexCount(0));
                }
                Debug.Log("BASALT_BORDER_RENDERERS "+groups.Length+" TRIANGLES "+string.Join("/",totals));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
        [Test] public void DetailedShouldersAndMineApronsPreserveFullClearance(){
            var p=Profile();var m=(BasaltCrownMap)p.AuthoredMap;var art=BasaltCrownArtProfile.Load();
            var mines=BasaltCrownDetails.HillMines(m,p);Assert.IsEmpty(mines,"Citadel mining was replaced by one relay");
            foreach(var mine in mines){Assert.IsEmpty(mine.Slots);Assert.True(m.SupportsFootprint(mine.Position,art.mineApronRadius+art.mineApronFeather));}
            var allMines=BasaltCrownDetails.Mines(m,p);Assert.AreEqual(4,allMines.Length);
            foreach(var mine in allMines){
                Assert.True(m.SupportsFootprint(mine.Position,art.mineApronRadius+art.mineApronFeather));
                if(mine.Id>=7)Assert.Greater(BasaltCrownRoads.SignedDistance((float)mine.Position.X,(float)mine.Position.Z,art),art.mineApronRadius);
            }
            var pads=BasaltCrownDetails.SitePads(m,p,art);
            var sites=m.Sites(p).Where(site=>TerritoryRules.Center(site.Kind)).ToArray();
            Assert.AreEqual(4,pads.Length);
            float Area(BasaltCrownDetails.SitePad pad)=>4*pad.HalfX*pad.HalfZ-2*pad.Corner*pad.Corner;
            var smallestHeadquarters=sites.Where(site=>site.Kind==PlayableBuildingKind.Headquarters)
                .Min(site=>Area(pads.Single(pad=>pad.Hub.Equals(site.Position))));
            foreach(var site in sites){
                var pad=pads.Single(item=>item.Hub.Equals(site.Position));
                if(site.Kind==PlayableBuildingKind.Outpost)Assert.Less(Area(pad),smallestHeadquarters);
                float Distance(NavPoint point)=>BasaltCrownDetails.PadDistance(point,pad);
                Assert.Greater(pad.Corner,0);
                Assert.Greater(Distance(new NavPoint(pad.Center.X+pad.HalfX,pad.Center.Z+pad.HalfZ)),0);
                Assert.LessOrEqual(Distance(site.Position)+TerritoryRules.Radius(p,site.Kind),-art.siteApronFeather);
                double slotRadius=Math.Max(p.ScienceFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius));
                foreach(var slot in site.Slots)
                    Assert.LessOrEqual(Distance(slot.Position)+slotRadius,-art.siteApronFeather);
                var defense=site.DefenseSlots(p);
                Assert.IsNotEmpty(defense);
                var markerHalf=ConstructionApronMesh.DefenseSupportHalf(p,EnvironmentArtProfile.Load());
                double defenseRadius=Math.Max(Math.Sqrt(2)*p.TurretFootprintRadius,markerHalf.magnitude);
                foreach(var slot in defense)
                    Assert.LessOrEqual(Distance(slot.Position)+defenseRadius,-art.siteApronFeather,
                        "HQ "+site.Id+" defense slot "+slot.Id+" must fit even with a rotated marker");
                foreach(var approach in new[]{new NavPoint(site.Position.X-Math.Sign(site.Position.X)*12,site.Position.Z),
                    new NavPoint(site.Position.X,site.Position.Z-Math.Sign(site.Position.Z)*12)}){
                    Assert.Greater(BasaltCrownRoads.Coverage((float)approach.X,(float)approach.Z,art,BasaltCrownRoads.Plaza(m,p)),.99f);
                    Assert.True(m.SupportsSweep(approach,site.Position,p.TankCollisionRadius));
                }
            }
            var plaza=BasaltCrownRoads.Plaza(m,p);
            var details=BasaltCrownDetails.Plan(m,p,art);
            Assert.Greater(details.Count(d=>d.Type==BasaltCrownDetails.Kind.Kerb),30);
            Assert.Greater(details.Count(d=>d.Type==BasaltCrownDetails.Kind.Rubble),30);
            Assert.GreaterOrEqual(details.Count(d=>d.Type==BasaltCrownDetails.Kind.Ore),8);
            foreach(var detail in details){
                Assert.True(BasaltCrownDetails.Clear(detail,m,p,art));
                if(detail.Type==BasaltCrownDetails.Kind.Rubble)
                    Assert.True(pads.Where(pad=>pad.IsOutpost).All(pad=>BasaltCrownDetails.PadDistance(detail.Center,pad)>=detail.Radius));
                if(detail.Type==BasaltCrownDetails.Kind.Kerb)
                    Assert.True(pads.All(pad=>BasaltCrownDetails.PadDistance(detail.Center,pad)>=detail.Radius));
                var mesh=BasaltCrownDetails.Build(new[]{detail},m);
                try{foreach(var v in mesh.vertices){
                    Assert.True(m.SupportsFootprint(new NavPoint(v.x,v.z),0));
                    if(detail.Type==BasaltCrownDetails.Kind.Kerb)Assert.GreaterOrEqual(BasaltCrownRoads.SignedDistance(v.x,v.z,art,plaza),-.001f);
                    Assert.LessOrEqual(v.y-m.SurfaceHeight(new NavPoint(v.x,v.z)),detail.Height+.001);
                }}finally{UnityEngine.Object.DestroyImmediate(mesh);}
            }
        }
        [Test] public void RelayPlazaUsesProfileRadiusAndLeavesBothRoadOpeningsClear()
        {
            var p=Profile();
            var map=(BasaltCrownMap)p.AuthoredMap;
            var art=BasaltCrownArtProfile.Load();
            var plaza=BasaltCrownRoads.Plaza(map,p);
            Assert.AreEqual((float)p.RelayCaptureRadius,plaza.Radius);
            Assert.That(BasaltCrownRoads.SignedDistance(plaza.Center.x,plaza.Center.y+plaza.Radius,art,plaza),Is.EqualTo(0).Within(1e-5));
            var data=p.CopyData();
            data.relayCaptureRadius-=1;
            Assert.AreEqual((float)data.relayCaptureRadius,BasaltCrownRoads.Plaza(map,PlayableProfile.Create(data,map)).Radius);

            float rim=plaza.Radius+art.kerbWidth*.5f+art.detailClearance;
            var ringKerbs=BasaltCrownDetails.Plan(map,p,art).Where(detail=>detail.Type==BasaltCrownDetails.Kind.Kerb
                && Math.Abs(plaza.SignedDistance((float)detail.Center.X,(float)detail.Center.Z)-(rim-plaza.Radius))<1e-4).ToArray();
            Assert.That(ringKerbs.Length,Is.GreaterThan(4));
            foreach(int side in new[]{-1,1})
            {
                Assert.True(ringKerbs.Any(kerb=>side*(kerb.Center.Z-plaza.Center.y)>BasaltCrownRoads.CausewayHalfWidth));
                Assert.Greater(BasaltCrownRoads.Coverage(plaza.Center.x+side*rim,plaza.Center.y,art,plaza),.99f);
                Assert.True(map.SupportsSweep(new NavPoint(plaza.Center.x+side*rim,plaza.Center.y),
                    new NavPoint(plaza.Center.x,plaza.Center.y),p.TankCollisionRadius));
            }
            Assert.True(ringKerbs.All(kerb=>Math.Abs(kerb.Center.Z-plaza.Center.y)>BasaltCrownRoads.CausewayHalfWidth));
        }
        [Test] public void EveryArtControlRejectsNonfiniteAndOutOfRangeValues(){
            var art=BasaltCrownArtProfile.Load();
            foreach(var parameter in art.Parameters()){
                float original=parameter.Read();
                foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,parameter.Minimum-1,parameter.Maximum+1}){
                    parameter.Write(invalid);Assert.Throws<ArgumentException>(()=>art.Validate(),parameter.Path);
                }
                parameter.Write(original);art.Validate();
            }
        }
        [UnityTest] public IEnumerator NativeAiDuelRunsWithSelectedLobbyAndNoRuntimeFailure(){
            var p=Profile();var setup=new NativeLobbyConfiguration{BasaltCrown=true,HasExplicitSeed=true,ExplicitSeed=7108};setup.InitializeParticipants();
            using(var runtime=Spacewars.Runtime.PlayableRuntime.CreateLobbyMatch(p,4,setup))using(var routes=new UnityHostRouteService()){
                float deadline=Time.realtimeSinceStartup+15;
                while(runtime.Latest.Tick<120&&runtime.Latest.Failure==null&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.GreaterOrEqual(runtime.Latest.Tick,120,"tick="+runtime.Latest.Tick+" projection="+routes.LastProjectionFailure);
                Assert.IsNull(runtime.Latest.Failure);Assert.Zero(runtime.Latest.Metrics.Errors);Assert.AreEqual(2,runtime.Latest.Participants.Count);
                runtime.RequestStop();float stopDeadline=Time.realtimeSinceStartup+3;while(!runtime.IsStopped&&Time.realtimeSinceStartup<stopDeadline)yield return null;
                Assert.True(runtime.IsStopped);
            }
        }
        [UnityTest] public IEnumerator NativeMixedUnitsTraverseBothRampsWithoutCrossingCliffs(){
            var p=Profile();var m=(BasaltCrownMap)p.AuthoredMap;TerritoryRules.ValidateArena(p);
            var geometry=new NavGeometry(p.ArenaHalfExtent,m.MovementBlockers.ToArray(),1);
            using(var router=new UnityNavigationRouter(geometry,p.Navigation))foreach(int side in new[]{-1,1})foreach(var kind in new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval}){
                double radius=PlayableUnitRules.Radius(p,kind);var origin=new NavPoint(side*30,0);var target=new NavPoint(side*14,0);
                var path=router.FindPath(origin,target);Assert.IsNotEmpty(path);
                var crowd=new NavCrowd(geometry,p.Navigation,m);crowd.Add(1,origin,radius,PlayableUnitRules.Speed(p,kind),PlayableUnitRules.Turn(p,kind));
                Assert.True(crowd.SetRoute(1,target,path));
                for(int tick=0;tick<1200&&crowd.Units[0].Outcome!=NavigationOutcome.Arrived;tick++){
                    var before=crowd.Units[0].Position;crowd.Step(1d/30);var after=crowd.Units[0].Position;
                    Assert.True(m.SupportsSweep(before,after,radius));
                    Assert.LessOrEqual(Math.Abs(m.SurfaceHeight(after)-m.SurfaceHeight(before)),Math.Tan(22*Math.PI/180)*Math.Sqrt(Math.Pow(after.X-before.X,2)+Math.Pow(after.Z-before.Z,2))+1e-7);
                }
                Assert.AreEqual(NavigationOutcome.Arrived,crowd.Units[0].Outcome);yield return null;
            }
        }
        [UnityTest] public IEnumerator NativeBasaltWorldAndMinimapRenderTheSameTerrain(){
            var p=Profile();var m=(BasaltCrownMap)p.AuthoredMap;var host=new GameObject("Basalt Crown rendered QA");
            var cameraObject=new GameObject("Basalt QA camera");var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;cameraObject.AddComponent<VisibleShadowCoverage>().Configure(host.transform,IndustrialLightingProfile.Load());
            var sunObject=new GameObject("Basalt QA sun");var sun=sunObject.AddComponent<Light>();sun.type=LightType.Directional;sun.transform.rotation=Quaternion.Euler(55,-30,0);sun.intensity=1.4f;
            var priorAmbient=RenderSettings.ambientLight;IndustrialLightingProfile.Load().ApplyScene(sun);
            var image=new RenderTexture(1280,960,24);image.Create();camera.targetTexture=image;
            var world=new PlayableWorld(host.transform,p);var minimap=new PlayableMapTerrain(p);try{
                world.Fog.ShowPublic();minimap.ShowPublicTerrain();
                Assert.Greater(minimap.Texture.GetPixels32().Distinct().Count(),2);
                var setup=new Spacewars.Runtime.OfflineParticipantAuthority(m.Configuration(p,7108,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("enemy-1",2,2,OfflineControl.Ai)}),1);
                var overview=setup.View("player-1");
                foreach(var b in overview.Buildings){var actor=world.Building(b.Id,"headquarters",b.Owner==PlayableOwner.Player);actor.Root.position=new Vector3((float)b.Position.X,(float)m.SurfaceHeight(b.Position),(float)b.Position.Z);}
                foreach(var u in overview.Entities){var actor=world.Tank(u.Id,u.Owner==PlayableOwner.Player,u.Kind);actor.Root.position=new Vector3((float)u.Position.X,(float)m.SurfaceHeight(u.Position),(float)u.Position.Z);}
                yield return null;
                string folder=Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,"artifacts/playtest/basalt-crown-site-aprons");Directory.CreateDirectory(folder);
                foreach(string pose in new[]{"presentation","mine-south","mine-north","mine-west","mine-east","base","outpost","relay-plaza","overview","oblique","ramp-close","east-ramp","ring-close","scree-close","cliff-close","road-corner","normal-camera","orbit"}){
                    if(pose=="overview")world.InspectionSites();
                    camera.orthographicSize=pose=="relay-plaza"?15:pose=="presentation"||pose=="overview"||pose=="oblique"||pose=="orbit"?52:pose=="normal-camera"?26:pose=="base"||pose=="outpost"?14:(float)p.CameraMinZoom;
                    var focus=pose=="mine-west"?new Vector3(-48,0,0):pose=="mine-east"?new Vector3(48,0,0):pose=="base"?new Vector3(-40,0,-32):pose=="outpost"?new Vector3(40,0,-32):pose=="mine-south"?new Vector3(0,1,-12):pose=="mine-north"?new Vector3(0,1,14):pose=="ramp-close"?new Vector3(-22,0,0):pose=="east-ramp"?new Vector3(22,0,0):pose=="ring-close"?new Vector3(-36,0,12):pose=="scree-close"?new Vector3(10,0,-24):pose=="cliff-close"?new Vector3(-16,1,-16):pose=="road-corner"?new Vector3(40,0,32):pose=="normal-camera"?new Vector3(-25,0,-4):Vector3.zero;
                    camera.transform.position=focus+(pose=="overview"?new Vector3(0,120,-.01f):pose=="orbit"?new Vector3(32,70,52):new Vector3(-32,70,-52));camera.transform.LookAt(focus);
                    if(pose=="relay-plaza")world.RenderSites(overview,camera);
                    camera.Render();var png=new Texture2D(image.width,image.height,TextureFormat.RGB24,false);var previous=RenderTexture.active;RenderTexture.active=image;png.ReadPixels(new Rect(0,0,image.width,image.height),0,0);png.Apply();RenderTexture.active=previous;
                    var pixels=png.GetPixels();Assert.Greater(pixels.Count(c=>c.maxColorComponent>.05f),pixels.Length/10);
                    Assert.Less(pixels.Count(c=>c.r>.8f&&c.b>.8f&&c.g<.1f),pixels.Length/100,"Error-shader pixels in "+pose);
                    File.WriteAllBytes(Path.Combine(folder,pose+".png"),png.EncodeToPNG());UnityEngine.Object.DestroyImmediate(png);
                }
                var edgeGroups=host.GetComponentsInChildren<ModelDetailLod>()
                    .Where(lod=>lod.name.StartsWith("Basalt border — ")).ToArray();
                if(edgeGroups.Length>0){
                    var edgeFocus=new Vector3(-34,0,48);
                    camera.orthographicSize=14;
                    camera.transform.position=edgeFocus+new Vector3(-32,45,-35);
                    camera.transform.LookAt(edgeFocus);
                    for(int tier=0;tier<3;tier++){
                        foreach(var group in edgeGroups)group.SetDetail(tier);
                        camera.Render();
                        var pixels=new Texture2D(image.width,image.height,TextureFormat.RGB24,false);
                        var previous=RenderTexture.active;
                        RenderTexture.active=image;
                        pixels.ReadPixels(new Rect(0,0,image.width,image.height),0,0);
                        pixels.Apply();
                        RenderTexture.active=previous;
                        File.WriteAllBytes(Path.Combine(folder,"border-lod-"+tier+".png"),pixels.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(pixels);
                    }
                    foreach(var group in edgeGroups)group.SetDetail(0);
                }
                File.WriteAllBytes(Path.Combine(folder,"minimap.png"),minimap.Texture.EncodeToPNG());
                camera.orthographicSize=52;camera.transform.position=new Vector3(-32,70,-52);camera.transform.LookAt(Vector3.zero);camera.Render();
                var publicRead=new Texture2D(image.width,image.height,TextureFormat.RGB24,false);var priorRead=RenderTexture.active;RenderTexture.active=image;publicRead.ReadPixels(new Rect(0,0,image.width,image.height),0,0);publicRead.Apply();RenderTexture.active=priorRead;
                float publicMean=publicRead.GetPixels().Average(c=>c.grayscale);UnityEngine.Object.DestroyImmediate(publicRead);
                world.Fog.Reset();Assert.True(world.Fog.Texture.GetPixels32().All(c=>c.r>0));
                camera.orthographicSize=52;camera.transform.position=new Vector3(-32,70,-52);camera.transform.LookAt(Vector3.zero);camera.Render();
                var fogPng=new Texture2D(image.width,image.height,TextureFormat.RGB24,false);var previousFog=RenderTexture.active;RenderTexture.active=image;fogPng.ReadPixels(new Rect(0,0,image.width,image.height),0,0);fogPng.Apply();RenderTexture.active=previousFog;
                Assert.Greater(fogPng.GetPixels().Count(c=>c.maxColorComponent>.001f),image.width*image.height/10);
                Assert.Less(fogPng.GetPixels().Average(c=>c.grayscale),publicMean*.7f,"Native fog must visibly darken the terrain shader.");
                File.WriteAllBytes(Path.Combine(folder,"fog.png"),fogPng.EncodeToPNG());UnityEngine.Object.DestroyImmediate(fogPng);
            }finally{world.Dispose();minimap.Dispose();camera.targetTexture=null;image.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(sunObject);RenderSettings.ambientLight=priorAmbient;}
        }
    }
}
