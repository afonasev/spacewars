using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif
namespace Spacewars.Tests.PlayMode
{
    public sealed class MossHighlandNativeTests
    {
        private static PlayableProfile Profile()=>PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new MossHighlandMap(JsonUtility.FromJson<MossHighlandProfileData>(Resources.Load<TextAsset>("MossHighlandProfile").text)));
        [Test] public void AlbedoFamiliesImportsAndNaturalPaintAreValid(){
            var art=MossHighlandArtProfile.Load();Assert.AreEqual(64,art.Parameters().Count());art.Validate();var invalid=MossHighlandArtProfile.Load();invalid.detailTile=float.NaN;Assert.Throws<ArgumentException>(()=>invalid.Validate());
            foreach(var parameter in art.Parameters()){
                float original=parameter.Read();
                foreach(float value in new[]{parameter.Minimum,parameter.Maximum}){parameter.Write(value);Assert.DoesNotThrow(()=>art.Validate(),parameter.Path);}
                foreach(float value in new[]{float.NaN,float.PositiveInfinity,parameter.Maximum+parameter.Step,parameter.Minimum-parameter.Step}){parameter.Write(value);Assert.Throws<ArgumentException>(()=>art.Validate(),parameter.Path);}
                parameter.Write(original);
            }
            Assert.Throws<ArgumentException>(()=>MossHighlandArtProfile.Role("unknown"));
            foreach(string family in new[]{"MossHighlandArt/RockyMoss","Environment/NaturalFrontier/Rock","MossHighlandArt/DrySoil","Environment/NaturalFrontier/Concrete"})foreach(string suffix in new[]{"","-b","-c"}){
                var t=Resources.Load<Texture2D>(family+suffix);Assert.IsNotNull(t);Assert.GreaterOrEqual(t.width,1024);Assert.GreaterOrEqual(t.height,1024);Assert.Greater(t.mipmapCount,1);Assert.AreEqual(TextureWrapMode.Repeat,t.wrapMode);Assert.AreEqual(FilterMode.Trilinear,t.filterMode);Assert.AreEqual(8,t.anisoLevel);Assert.AreEqual(family.StartsWith("MossHighlandArt/")?TextureFormat.RGB24:TextureFormat.BC7,t.format,family+suffix);
#if UNITY_EDITOR
                var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t));Assert.True(importer.sRGBTexture);Assert.AreEqual(family.StartsWith("MossHighlandArt/")?TextureImporterCompression.Uncompressed:TextureImporterCompression.CompressedHQ,importer.textureCompression);
#endif
            }
            var material=art.Material(Texture2D.blackTexture,new Vector4(64,64,0,0),Color.black);
            try{Assert.AreSame(Resources.Load<Texture2D>("MossHighlandArt/DrySoil"),material.GetTexture("_SandTex"));Assert.AreSame(Resources.Load<Texture2D>("MossHighlandArt/RockyMoss"),material.GetTexture("_StoneTex"));Assert.AreSame(Resources.Load<Texture2D>("Environment/NaturalFrontier/Concrete"),material.GetTexture("_ConcreteTex"));Assert.AreEqual(art.rockTile,material.GetVector("_RoleDetail").x);Assert.AreEqual(art.cliffFaceContrast,material.GetVector("_CliffVariation").z);}finally{UnityEngine.Object.DestroyImmediate(material);}
            var map=new MossHighlandMap();CollectionAssert.AreEquivalent(new[]{"moss-ground","mountain.base-dark","mountain.base-mid","metal-deck"},map.Materials.Select(p=>p.Material).Distinct());
            var masks=PlayableWorld.BuildMossPaintMasks(map,art,Profile());try{
                var a=masks[0].GetPixels();var b=masks[1].GetPixels();bool feather=false;var maxima=new float[7];
                for(int i=0;i<a.Length;i++){float[] w={a[i].r,a[i].g,a[i].b,a[i].a,b[i].r,b[i].g,b[i].b};Assert.That(w.Sum(),Is.EqualTo(1).Within(4d/255));for(int j=0;j<7;j++)maxima[j]=Math.Max(maxima[j],w[j]);feather|=w.Count(x=>x>.1f)>1;}
                // Engineered footprint is crisp even though neighbouring natural masks feather.
                int centre=(int)((-38+map.HalfExtent)/(2*map.HalfExtent)*512);
                Assert.AreEqual(1,a[centre*512+centre].b);Assert.AreEqual(0,b[centre*512+centre].r);
                var transitions=masks[2].GetPixels();
                Assert.True(transitions.Any(t=>t.r>.5f));
                foreach(var point in new[]{new NavPoint(-12,0),new NavPoint(12,0),new NavPoint(0,-12),new NavPoint(0,12)})Assert.Greater(masks[2].GetPixelBilinear((float)(point.X/128+.5),(float)(point.Z/128+.5)).b,.9f);
                foreach(var site in map.Sites(Profile()).Where(t=>TerritoryRules.Center(t.Kind))){float role=masks[2].GetPixelBilinear((float)(site.Position.X/128+.5),(float)(site.Position.Z/128+.5)).a;Assert.That(role,Is.EqualTo(site.Kind==PlayableBuildingKind.Headquarters?1f/3:site.Id==13?1:2f/3).Within(.01));}
                Assert.Less(masks[0].GetPixelBilinear(.5f,(40-8)/128f+.5f).b,.01,"Vacated inner apron becomes natural ground");Assert.True(transitions.Any(t=>t.r>.05f&&t.r<.45f));
                Assert.True(transitions.Any(t=>t.g>.05f&&t.g<.9f));
                for(int z=0;z<512;z++)for(int x=0;x<512;x++){
                    var point=new NavPoint((x+.5)*.25-64,(z+.5)*.25-64);
                    if(map.Solids.Any(s=>s.Contains(point)))Assert.AreEqual(0,transitions[z*512+x].r,"No scree on solid roofs");
                }
                Assert.True(feather);foreach(int role in new[]{0,1,2,4})Assert.Greater(maxima[role],.5f);
            }finally{foreach(var t in masks)UnityEngine.Object.DestroyImmediate(t);}
        }
        [Test] public void EveryFullBaseAndOutpostFitsApronWithFactoryExitClearance(){
            var p=Profile();var map=(MossHighlandMap)p.AuthoredMap;var art=MossHighlandArtProfile.Load();
            TerritoryRules.ValidateArena(p);
            var masks=PlayableWorld.BuildMossPaintMasks(map,art,p);
            try{
                for(int z=0;z<masks[0].height;z++)for(int x=0;x<masks[0].width;x++){
                    var point=new NavPoint((x+.5)*2*map.HalfExtent/masks[0].width-map.HalfExtent,(z+.5)*2*map.HalfExtent/masks[0].height-map.HalfExtent);
                    if(map.Solids.Any(s=>s.Contains(point)))Assert.Less(masks[0].GetPixel(x,z).b,.01f,"Concrete must not climb a ridge");
                }
                var sites=map.Sites(p).Where(s=>TerritoryRules.Center(s.Kind)).ToArray();Assert.AreEqual(9,sites.Length);
                foreach(var site in sites){
                    var bounds=PlayableWorld.MossApronBounds(site,p,art);
                    Assert.That((bounds.MinX+bounds.MaxX)*.5,Is.EqualTo(site.Position.X).Within(1e-9));
                    Assert.That((bounds.MinZ+bounds.MaxZ)*.5,Is.EqualTo(site.Position.Z).Within(1e-9));
                    for(double dz=.125;dz<bounds.MaxZ-site.Position.Z;dz+=.25)for(double dx=.125;dx<bounds.MaxX-site.Position.X;dx+=.25){
                        float Paint(int sx,int sz)=>masks[0].GetPixel((int)((site.Position.X+sx*dx+map.HalfExtent)/.25),(int)((site.Position.Z+sz*dz+map.HalfExtent)/.25)).b;
                        foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1})Assert.That(Paint(sx,sz),Is.EqualTo(Paint(1,1)).Within(.01f),"Symmetric apron mask site "+site.Id);
                    }
                    void Covered(NavPoint point,double radius){
                        // Entire square, not only center or circumscribed approximation.
                        for(double z=-radius;z<=radius+.0001;z+=radius/4)for(double x=-radius;x<=radius+.0001;x+=radius/4){
                            var q=new NavPoint(point.X+x,point.Z+z);Assert.True(bounds.Contains(q),"apron site "+site.Id);
                            Assert.Greater(masks[0].GetPixelBilinear((float)(q.X/(2*map.HalfExtent)+.5),(float)(q.Z/(2*map.HalfExtent)+.5)).b,.99f,"concrete site "+site.Id);
                        }
                    }
                    Covered(site.Position,TerritoryRules.Radius(p,site.Kind));
                    foreach(var slot in site.Slots){
                        foreach(var kind in new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter})Covered(slot.Position,TerritoryRules.Radius(p,kind));
                        // Compact concrete ends near buildings; exits retain terrain support and native clearance.
                        for(int i=0;i<=8;i++){double distance=p.FactoryExitDistance*i/8;Assert.True(map.SupportsFootprint(new NavPoint(slot.Position.X+Math.Cos(slot.Heading)*distance,slot.Position.Z+Math.Sin(slot.Heading)*distance),p.TankCollisionRadius));}
                    }
                }
            }finally{foreach(var t in masks)UnityEngine.Object.DestroyImmediate(t);}
        }
        [Test] public void ReliefAndSmallObjectsPreserveAllGameplayGeometry(){
            var p=Profile();var m=(MossHighlandMap)p.AuthoredMap;var host=new GameObject("Highland visual contract");var supports=m.Supports.ToArray();var blockers=m.MovementBlockers.ToArray();var seams=m.SurfaceTransitions.ToArray();
            try{using(var world=new PlayableWorld(host.transform,p)){
                Assert.IsEmpty(host.GetComponentsInChildren<Collider>(true));Assert.IsEmpty(host.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true));
                var pebbles=host.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="Moss pebble").ToArray();Assert.IsNotEmpty(pebbles);
                foreach(var f in pebbles){var b=f.sharedMesh.bounds;Assert.True(m.VisualRidges.Any(s=>b.min.x>=s.MinX-.0001&&b.max.x<=s.MaxX+.0001&&b.min.z>=s.MinZ-.0001&&b.max.z<=s.MaxZ+.0001&&b.min.y>=s.Bottom-.0001&&b.max.y<=s.Top+.0001));}
                var mountains=host.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="Moss border mountain").ToArray();Assert.AreEqual(4,mountains.Length);
                var rivers=host.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="Moss border river").ToArray();Assert.AreEqual(2,rivers.Length);
                foreach(var f in mountains.Concat(rivers)){
                    bool water=f.name=="Moss border river";var shapes=water?m.BorderRivers:m.BorderMountains;
                    Assert.Greater(f.sharedMesh.vertexCount,100);
                    foreach(var v in f.sharedMesh.vertices){
                        Assert.True(shapes.Any(b=>b.Contains(new NavPoint(v.x,v.z))),"Border visual leaves its authoritative polygon: "+v);
                        Assert.That(v.y,Is.InRange(0,water?.5f:8f));
                    }
                }
                var rocks=host.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="Moss ridge").ToArray();Assert.AreEqual(1,rocks.Length,"One continuous source-height skin");Assert.True(rocks.All(f=>f.sharedMesh.vertexCount>36));
                foreach(var f in rocks.Concat(pebbles))foreach(var v in f.sharedMesh.vertices)Assert.True(m.Solids.Any(s=>s.Contains(new NavPoint(v.x,v.z))&&v.y>=s.Bottom-.0001&&v.y<=s.Top+.0001),"visual outside original solid "+v);
                var vertices=rocks[0].sharedMesh.vertices;
                var baselineHost=new GameObject("Unchipped cliff baseline");
                try{
                    var art=MossHighlandArtProfile.Load();art.cliffEdgeInset=0;art.cliffChipDepth=0;
                    using(var baseline=new PlayableWorld(baselineHost.transform,p,art)){
                        var before=baselineHost.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Moss ridge").sharedMesh.vertices;
                        var original=new System.Collections.Generic.HashSet<Vector3>(before);
                        foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1})
                            Assert.IsTrue(vertices.Any(v=>v.x*sx>10&&v.z*sz>10&&!original.Contains(v)),"Real chipped geometry in ridge sector "+sx+","+sz);
                        var unchanged=baselineHost.GetComponentsInChildren<MeshFilter>().Where(f=>f.name!="Moss ridge").ToArray();
                        var current=host.GetComponentsInChildren<MeshFilter>().Where(f=>f.name!="Moss ridge").ToArray();
                        Assert.AreEqual(unchanged.Length,current.Length);
                        for(int i=0;i<unchanged.Length;i++){
                            Assert.AreEqual(unchanged[i].name,current[i].name);
                            CollectionAssert.AreEqual(unchanged[i].sharedMesh.vertices,current[i].sharedMesh.vertices,"Non-ridge geometry stays exact");
                        }
                    }
                }finally{UnityEngine.Object.DestroyImmediate(baselineHost);}
                var edges=new System.Collections.Generic.Dictionary<(Vector3,Vector3),int>();
                int Order(Vector3 a,Vector3 b){int c=a.x.CompareTo(b.x);if(c!=0)return c;c=a.y.CompareTo(b.y);return c!=0?c:a.z.CompareTo(b.z);}
                void Edge(Vector3 a,Vector3 b){if(a==b)return;var key=Order(a,b)<0?(a,b):(b,a);edges.TryGetValue(key,out int count);edges[key]=count+1;}
                for(int i=0;i<vertices.Length;i+=3){Edge(vertices[i],vertices[i+1]);Edge(vertices[i+1],vertices[i+2]);Edge(vertices[i+2],vertices[i]);}
                // All walls now share height rings; no sample boundary exceptions remain.
                var unmatched=edges.Where(e=>e.Value==1&&e.Key.Item1.y>.001f&&e.Key.Item2.y>.001f).ToArray();
                Assert.IsEmpty(unmatched,"Open mesh edge across ridge skin");
                for(int i=0;i<vertices.Length;i+=3){
                    var center=(vertices[i]+vertices[i+1]+vertices[i+2])/3;
                    Assert.True(m.Solids.Any(s=>s.Contains(new NavPoint(center.x,center.z))&&center.y>=s.Bottom-.0001&&center.y<=s.Top+.0001),"ridge face outside original solid "+center);
                }
            }CollectionAssert.AreEqual(supports,m.Supports);CollectionAssert.AreEqual(blockers,m.MovementBlockers);CollectionAssert.AreEqual(seams,m.SurfaceTransitions);}finally{UnityEngine.Object.DestroyImmediate(host);}
        }
        [UnityTest] public IEnumerator NativeWorldNormalCameraCliffsRampsAndFogRender(){
            var p=Profile();var map=(MossHighlandMap)p.AuthoredMap;var host=new GameObject("Moss Highland render QA");var cameraObject=new GameObject("Highland QA camera");var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.03f,.045f,.04f);
            cameraObject.AddComponent<VisibleShadowCoverage>().Configure(host.transform,IndustrialLightingProfile.Load(map.Id));
            var sunObject=new GameObject("Highland QA sun");var sun=sunObject.AddComponent<Light>();sun.type=LightType.Directional;var lighting=IndustrialLightingProfile.Load(map.Id);var priorAmbient=RenderSettings.ambientLight;lighting.ApplyScene(sun);
            var image=new RenderTexture(1280,960,24);image.Create();camera.targetTexture=image;PlayableWorld world=null;var mini=new PlayableMapTerrain(p);
            string folder=Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,"artifacts/playtest/moss-highland-borders");Directory.CreateDirectory(folder);
            try{
                // Render only the current profile; historical baseline PNGs are preserved separately.
                {
                    world?.Dispose();var art=MossHighlandArtProfile.Load();
                    world=new PlayableWorld(host.transform,p,art);world.Fog.ShowPublic();mini.ShowPublicTerrain();
                    var roster=Enumerable.Range(0,4).Select(i=>new OfflineParticipant("seat-"+i,i+1,i+1,OfflineControl.Human)).ToArray();var config=map.Configuration(p,7108,roster);var authority=new OfflineParticipantAuthority(config,1);
                    foreach(var owner in roster){var v=authority.View(owner.Id);foreach(var b in v.Buildings.Where(b=>b.Owner==v.Owner)){var actor=world.Building(b.Id,"headquarters",b.Owner==PlayableOwner.Player);actor.Root.position=new Vector3((float)b.Position.X,(float)map.SurfaceHeight(b.Position),(float)b.Position.Z);ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[(int)b.Owner],out var color);PlayableWorld.PaintOwner(actor,color);}foreach(var e in v.Entities.Where(e=>e.Owner==v.Owner)){var actor=world.Tank(e.Id,e.Owner==PlayableOwner.Player,e.Kind);actor.Root.position=new Vector3((float)e.Position.X,(float)map.SurfaceHeight(e.Position),(float)e.Position.Z);ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[(int)e.Owner],out var color);PlayableWorld.PaintOwner(actor,color);}}
                    yield return null;
                    foreach(string pose in new[]{"overview","border-unshadowed","border-nw","border-se","river-west","river-east","border-normal","oblique","edge-decoration","centre-routes","normal-camera","base-close","ramp-close","cliff-close","moss-close","soil-close","orbit","rock-foot","apron-edge","cliff-side","cliff-unshadowed","ridge-se","ridge-ne","ridge-nw","ridge-east-transition"}){
                        var focus=pose=="rock-foot"?new Vector3(-24,0,-34):pose=="apron-edge"?new Vector3(-27,0,-38):pose=="cliff-side"?new Vector3(-20,2,-24):(pose=="normal-camera"||pose=="base-close")?new Vector3(-38,0,-38):pose=="soil-close"?new Vector3(-14,0,-40):pose=="ramp-close"?new Vector3(-12,0,0):(pose=="cliff-close"||pose=="cliff-unshadowed")?new Vector3(-20,0,-20):pose=="moss-close"?new Vector3(-28,0,-10):Vector3.zero;
                        if(pose=="ridge-se")focus=new Vector3(20,2,-24);
                        if(pose=="ridge-ne")focus=new Vector3(20,2,24);
                        if(pose=="ridge-nw")focus=new Vector3(-20,2,24);
                        if(pose=="ridge-east-transition")focus=new Vector3(12,0,0);
                        if(pose=="edge-decoration")focus=new Vector3(57,0,52);
                        if(pose=="centre-routes")focus=Vector3.zero;
                        if(pose=="border-nw"||pose=="border-unshadowed")focus=new Vector3(-53,0,53);
                        if(pose=="border-se")focus=new Vector3(53,0,-53);
                        if(pose=="river-west")focus=new Vector3(-57,0,30);
                        if(pose=="river-east")focus=new Vector3(57,0,-30);
                        if(pose=="border-normal")focus=new Vector3(-48,0,45);
                        camera.orthographicSize=pose=="centre-routes"?24:pose=="overview"||pose=="oblique"||pose=="orbit"?66:pose=="normal-camera"?(float)p.CameraOrthoSize:(float)p.CameraMinZoom;
                        camera.transform.position=focus+((pose=="cliff-side"||pose.StartsWith("ridge-"))?new Vector3(-20,16,-32):pose=="overview"?new Vector3(0,140,-.01f):pose=="normal-camera"?new Vector3((float)p.CameraOffsetX,(float)p.CameraHeight,(float)p.CameraOffsetZ):pose=="orbit"?new Vector3(45,80,55):new Vector3(-38,82,-58));camera.transform.LookAt(focus);
                        if(pose.StartsWith("border-")||pose.StartsWith("river-")){camera.orthographicSize=pose=="border-normal"?(float)p.CameraOrthoSize:17;camera.transform.position=focus+new Vector3((float)p.CameraOffsetX,(float)p.CameraHeight,(float)p.CameraOffsetZ);camera.transform.LookAt(focus);}
                        var shadows=sun.shadows;
                        if(pose=="cliff-unshadowed"||pose=="border-unshadowed")sun.shadows=LightShadows.None;
                        Capture(pose);
                        sun.shadows=shadows;
                    }
                }
                // Fully occupied visual fixture uses real site/slot positions and real models.
                // Economy progression is not claimed by this presentation fixture.
                int visualId=10000;
                foreach(var site in map.Sites(p).Where(s=>TerritoryRules.Center(s.Kind))){
                    var bounds=PlayableWorld.MossApronBounds(site,p,MossHighlandArtProfile.Load());
                    void Place(string kind,NavPoint point,bool upgraded=false){
                        var actor=world.Building(visualId++,kind,true,upgraded);actor.Root.position=new Vector3((float)point.X,(float)map.SurfaceHeight(point),(float)point.Z);
                        ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[site.Kind==PlayableBuildingKind.Headquarters?site.Id-1:0],out var ownerColor);PlayableWorld.PaintOwner(actor,ownerColor);
                        foreach(var renderer in actor.Root.GetComponentsInChildren<Renderer>()){
                            var b=renderer.bounds;
                            Assert.True(bounds.Contains(new NavPoint(b.min.x,b.min.z))&&bounds.Contains(new NavPoint(b.max.x,b.max.z)),"Visible model outside apron: "+site.Id+" "+kind);
                        }
                    }
                    if(site.Kind==PlayableBuildingKind.Outpost)Place("outpost",site.Position);
                    foreach(var slot in site.Slots)Place(slot.Id%3==1?"factory":slot.Id%3==2?"Refinery":"ScientificCenter",slot.Position,slot.Id%3==2);
                }
                yield return null;
                foreach(var site in map.Sites(p).Where(s=>s.Id==1||s.Kind==PlayableBuildingKind.Outpost)){
                    var focus=new Vector3((float)site.Position.X,(float)map.SurfaceHeight(site.Position),(float)site.Position.Z);
                    camera.orthographicSize=(float)p.CameraOrthoSize;camera.transform.position=focus+new Vector3((float)p.CameraOffsetX,(float)p.CameraHeight,(float)p.CameraOffsetZ);camera.transform.LookAt(focus);
                    Capture(site.Id==1?"full-base":"full-outpost-"+site.Id);
                }
                File.WriteAllBytes(Path.Combine(folder,"minimap.png"),mini.Texture.EncodeToPNG());Assert.Greater(mini.Texture.GetPixels32().Distinct().Count(),3);
                camera.orthographicSize=66;camera.transform.position=new Vector3(-38,82,-58);camera.transform.LookAt(Vector3.zero);camera.Render();float publicMean=Read().grayscale;
                world.Fog.Reset();camera.Render();Assert.Less(Read().grayscale,publicMean*.7f);Capture("fog");
                Color Read(){var png=Pixels();var result=new Color(png.GetPixels().Average(c=>c.r),png.GetPixels().Average(c=>c.g),png.GetPixels().Average(c=>c.b));UnityEngine.Object.DestroyImmediate(png);return result;}
                Texture2D Pixels(){var previous=RenderTexture.active;RenderTexture.active=image;var png=new Texture2D(image.width,image.height,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,image.width,image.height),0,0);png.Apply();RenderTexture.active=previous;return png;}
                void Capture(string name){camera.Render();var png=Pixels();try{var pixels=png.GetPixels();Assert.Greater(pixels.Count(c=>c.maxColorComponent>.05f),pixels.Length/10,name);Assert.Less(pixels.Count(c=>c.r>.8f&&c.b>.8f&&c.g<.1f),pixels.Length/100,"Error shader "+name);File.WriteAllBytes(Path.Combine(folder,name+".png"),png.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(png);}}
            }finally{world?.Dispose();mini.Dispose();camera.targetTexture=null;image.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(sunObject);RenderSettings.ambientLight=priorAmbient;}
        }
    }
}
