using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
public sealed class NullDockArtTests
{
    [TestCase(1f)] [TestCase(2f)] [TestCase(4f)]
    public void EveryRampHasSingleCoverageAndPreservesSurface(float height)
    {
        var map=new NullDockMap(new NullDockProfileData{terraceHeight=height});var art=NullDockArtProfile.Load();
        foreach(var ramp in map.Supports.Where(s=>s.Id.StartsWith("R"))){
            var mesh=NullDockRampMesh.Create(ramp,-art.hullDepth,art.Upper);
            try{
                var v=mesh.vertices;var t=mesh.triangles;var roles=new System.Collections.Generic.List<Vector2>();mesh.GetUVs(1,roles);
                double top=0,sidePaint=0;var b=ramp.Bounds;
                for(int i=0;i<t.Length;i+=3){
                    var a=v[t[i]];var c=v[t[i+1]];var d=v[t[i+2]];var cross=Vector3.Cross(c-a,d-a);double area=cross.magnitude*.5;
                    Assert.Greater(area,1e-7,ramp.Id+" nondegenerate face");
                    if(cross.y>0)top+=area;
                    if(Math.Abs(cross.y)<1e-6&&roles[t[i]].x==6){sidePaint+=area;Assert.GreaterOrEqual(Mathf.Min(a.y,c.y,d.y),0);}
                    foreach(var q in new[]{a,c,d}){Assert.True(b.Contains(new NavPoint(q.x,q.z)),ramp.Id+" footprint");Assert.LessOrEqual(q.y,ramp.HeightAt(new NavPoint(q.x,q.z))+1e-5);}
                }
                double expectedTop=(b.MaxX-b.MinX)*(b.MaxZ-b.MinZ)*Math.Sqrt(1+ramp.Gradient.X*ramp.Gradient.X+ramp.Gradient.Z*ramp.Gradient.Z);
                double length=Math.Abs(ramp.Gradient.X)>0?b.MaxX-b.MinX:b.MaxZ-b.MinZ;
                Assert.AreEqual(expectedTop,top,1e-3,ramp.Id+" exactly one top surface");
                Assert.AreEqual(length*height,sidePaint,1e-3,ramp.Id+" exactly two triangular painted sides");
                Assert.AreEqual(18,t.Length/3,ramp.Id+" removes hidden/duplicate paint faces; baseline 36");
                // Probe interiors of each paint band and neutral center; never a shared triangle edge.
                foreach(double fraction in new[]{.01,.37,.99}){
                    double x=b.MinX+(b.MaxX-b.MinX)*(Math.Abs(ramp.Gradient.X)>0?.37:fraction);
                    double z=b.MinZ+(b.MaxZ-b.MinZ)*(Math.Abs(ramp.Gradient.X)>0?fraction:.37);
                    var ray=new Ray(new Vector3((float)x,20,(float)z),Vector3.down);int hits=0;
                    for(int i=0;i<t.Length;i+=3){var a=v[t[i]];var c=v[t[i+1]];var d=v[t[i+2]];if(Vector3.Cross(c-a,d-a).y<=0)continue;
                        var plane=new Plane(a,c,d);if(!plane.Raycast(ray,out float distance))continue;var q=ray.GetPoint(distance);
                        bool Inside(Vector3 u,Vector3 w)=>Vector3.Dot(Vector3.Cross(w-u,q-u),plane.normal)>=-1e-5;
                        if(Inside(a,c)&&Inside(c,d)&&Inside(d,a))hits++;
                    }
                    Assert.AreEqual(1,hits,ramp.Id+" top ray coverage");
                }
            }finally{UnityEngine.Object.DestroyImmediate(mesh);}
        }
    }
    [UnityTest] public IEnumerator CaptureEveryRampSide()
    {
        string output=Environment.GetEnvironmentVariable("NULL_DOCK_ART_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
        var map=new NullDockMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
        var root=new GameObject("Ramp side inspection");PlayableWorld world=null;
        try{
            world=new PlayableWorld(root.transform,p,staged:true);yield return world.Preparation;world.InspectionReveal();
            var light=new GameObject("Ramp sun");light.transform.SetParent(root.transform);IndustrialLightingProfile.Load(map.Id).ApplyScene(light.AddComponent<Light>());
            var camera=new GameObject("Ramp camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.orthographic=true;camera.farClipPlane=900;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            ConfigureInspectionCamera(camera,root.transform,map.Id);yield return null;
            foreach(var ramp in map.Supports.Where(s=>s.Id.StartsWith("R"))){
                var b=ramp.Bounds;bool alongX=Math.Abs(ramp.Gradient.X)>0;
                foreach(int side in new[]{-1,1}){
                    var target=new Vector3((float)((b.MinX+b.MaxX)*.5),.8f,(float)((b.MinZ+b.MaxZ)*.5));
                    target+=alongX?new Vector3(0,0,side*(float)(b.MaxZ-b.MinZ)*.5f):new Vector3(side*(float)(b.MaxX-b.MinX)*.5f,0,0);
                    var offset=alongX?new Vector3(-3,8,side*14):new Vector3(side*14,8,-3);
                    SaveFrame(camera,output,ramp.Id+"-side-"+side,target,7,offset);
                    if(ramp.Id=="R3"&&side==-1)for(int step=1;step<=2;step++)SaveFrame(camera,output,"R3-moving-"+step,target+new Vector3(step*.15f,0,0),7,offset);
                    yield return null;
                }
            }
            File.WriteAllText(Path.Combine(output,"mesh-metrics.json"),JsonUtility.ToJson(world.DockMetrics,true));
        }finally{world?.Dispose();UnityEngine.Object.Destroy(root);}
    }
    [Test] public void MinimapUsesWorldZonePaletteAndKeepsFog()
    {
        var map=new NullDockMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
        var art=NullDockArtProfile.Load();
        using(var terrain=new PlayableMapTerrain(p))using(var fog=new PlayableFogMask(p)){
            terrain.ShowPublicTerrain();
            Color Sample(double x,double z)=>terrain.Texture.GetPixel((int)((x/p.ArenaHalfExtent+1)*.5*terrain.Texture.width),(int)((z/p.ArenaHalfExtent+1)*.5*terrain.Texture.height));
            void Same(Color expected,double x,double z){var actual=Sample(x,z);Assert.AreEqual(expected.r,actual.r,.005f);Assert.AreEqual(expected.g,actual.g,.005f);Assert.AreEqual(expected.b,actual.b,.005f);}
            foreach(int sign in new[]{-1,1}){
                Same(art.Battle,sign*39,-sign*27);Same(art.Contested,sign*39,-sign*30);
                Same(art.Battle,sign*47,-sign*24);Same(art.Contested,sign*50,-sign*24);
                Same(art.Rear,sign*80,sign*50);
            }
            Same(art.Headquarters,-52,3);Same(Color.Lerp(art.Headquarters,Color.white,art.siteDiskLift),-68,3);
            Same(Color.Lerp(art.Outpost,Color.white,art.siteDiskLift),28,50);
            Same(art.Upper,-44,0);Assert.Zero(Sample(-95,-95).a,"Space stays transparent");
            string output=Environment.GetEnvironmentVariable("NULL_DOCK_ART_EVIDENCE");
            if(!string.IsNullOrEmpty(output)){Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,"minimap-zones.png"),terrain.Texture.EncodeToPNG());}
            var at=new NavPoint(39,-24);var vision=new PlayableVision(0,p.ArenaHalfExtent,p.ArenaHalfExtent,4,p.FogEdgeFeather);
            vision.Refresh(Array.Empty<VisionSource>(),Array.Empty<KnownBuilding>());fog.Update(vision.Snapshot(),1);terrain.Update(fog);var unknown=Sample(at.X,at.Z);
            vision.Refresh(new[]{new VisionSource(at,12)},Array.Empty<KnownBuilding>());fog.Update(vision.Snapshot(),1);terrain.Update(fog);var visible=Sample(at.X,at.Z);
            vision.Refresh(Array.Empty<VisionSource>(),Array.Empty<KnownBuilding>());fog.Update(vision.Snapshot(),1);terrain.Update(fog);var explored=Sample(at.X,at.Z);
            Assert.Greater(visible.grayscale,explored.grayscale);Assert.Greater(explored.grayscale,unknown.grayscale);
        }
    }
    [UnityTest] public IEnumerator StationAndMinimapRespectVisibleExploredAndUnknownFog()
    {
        string output=Environment.GetEnvironmentVariable("NULL_DOCK_ART_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
        var map=new NullDockMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
        var root=new GameObject("Null Dock fog inspection");PlayableWorld world=null;PlayableMapTerrain minimap=null;
        try{
            world=new PlayableWorld(root.transform,p,staged:true);yield return world.Preparation;
            minimap=new PlayableMapTerrain(p,staged:true);yield return minimap.Preparation;minimap.ShowPublicTerrain();
            File.WriteAllBytes(Path.Combine(output,"minimap-public.png"),minimap.Texture.EncodeToPNG());
            var light=new GameObject("Fog sun");light.transform.SetParent(root.transform);IndustrialLightingProfile.Load(map.Id).ApplyScene(light.AddComponent<Light>());
            var camera=new GameObject("Fog camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.orthographic=true;camera.farClipPlane=900;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            var station=root.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="Station hull, decks and equipment");Assert.AreSame(world.Fog.Texture,station.sharedMaterial.GetTexture("_FogMask"));
            var at=map.Point(62,80);var vision=new PlayableVision(0,map.HalfExtent,map.HalfExtent,4,p.FogEdgeFeather);
            var rendered=new Color[3];var mapColors=new Color[3];string[] names={"unknown","visible","explored"};
            for(int state=0;state<3;state++){
                vision.Refresh(state==1?new[]{new VisionSource(at,15)}:Array.Empty<VisionSource>(),Array.Empty<KnownBuilding>());
                world.Fog.Update(vision.Snapshot(),1);minimap.Update(world.Fog);
                rendered[state]=SaveFrame(camera,output,"fog-"+names[state],new Vector3((float)at.X,0,(float)at.Z),8,new Vector3(0,50,0));
                mapColors[state]=minimap.Texture.GetPixel((int)((at.X/(map.HalfExtent*2)+.5)*minimap.Texture.width),(int)((at.Z/(map.HalfExtent*2)+.5)*minimap.Texture.height));
                File.WriteAllBytes(Path.Combine(output,"minimap-"+names[state]+".png"),minimap.Texture.EncodeToPNG());
            }
            File.WriteAllLines(Path.Combine(output,"fog-samples.csv"),new[]{"state,stationLuminance,minimapLuminance"}.Concat(Enumerable.Range(0,3).Select(i=>$"{names[i]},{rendered[i].grayscale},{mapColors[i].grayscale}")));
            Assert.Greater(rendered[1].grayscale,rendered[2].grayscale,"Visible station is brighter than explored station");
            Assert.Greater(rendered[2].grayscale,rendered[0].grayscale,"Explored station is brighter than unknown station");
            Assert.Greater(mapColors[1].grayscale,mapColors[2].grayscale);Assert.Greater(mapColors[2].grayscale,mapColors[0].grayscale);
        }finally{minimap?.Dispose();world?.Dispose();UnityEngine.Object.Destroy(root);}
    }
    [UnityTest] public IEnumerator SupportedProfilesKeepEquipmentInsideModuleHeight()
    {
        string output=Environment.GetEnvironmentVariable("NULL_DOCK_ART_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
        var violations=new System.Collections.Generic.List<string>();
        var rows=new System.Collections.Generic.List<string>{"moduleHeight,profile,vertices,triangles,maxModuleY,excessVertices"};
        foreach(float height in new[]{4f,6f,10f})for(int variant=0;variant<3;variant++){
            var art=NullDockArtProfile.Load();if(variant!=1)foreach(var parameter in art.Parameters())parameter.Write(variant==0?parameter.Minimum:parameter.Maximum);art.Validate();
            var map=new NullDockMap(new NullDockProfileData{moduleHeight=height,terraceHeight=variant==0?1:variant==2?4:2});
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);var root=new GameObject("Null Dock supported profile");PlayableWorld world=null;
            try{
                world=new PlayableWorld(root.transform,p,staged:true,dockArt:art);yield return world.Preparation;world.InspectionReveal();
                var meshes=root.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="Station hull, decks and equipment"||f.name.StartsWith("Null field ")).Select(f=>f.sharedMesh).ToArray();
                // Inspect generated interior roof vertices, excluding boundary trim and adjacent deck rails.
                var roof=meshes.SelectMany(m=>m.vertices).Where(v=>map.Modules.Any(m=>m.Contains(new NavPoint(v.x,v.z))&&m.BoundaryDistanceSquared(new NavPoint(v.x,v.z))>.25)).ToArray();
                Assert.IsNotEmpty(roof);var excess=roof.Where(v=>v.y>height+.001f).ToArray();
                rows.Add($"{height},{variant},{meshes.Sum(m=>m.vertexCount)},{meshes.Sum(m=>m.triangles.Length/3)},{roof.Max(v=>v.y)},{excess.Length}");
                if(excess.Length>0)violations.Add($"height={height} variant={variant}: {excess.Length} vertices above solid, max={excess.Max(v=>v.y)}");
                if(height==4&&variant==2){
                    var light=new GameObject("Profile sun");light.transform.SetParent(root.transform);IndustrialLightingProfile.Load(map.Id).ApplyScene(light.AddComponent<Light>());
                    var camera=new GameObject("Profile camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.orthographic=true;camera.farClipPlane=900;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.003f,.006f,.016f);
                    SaveFrame(camera,output,"minimum-height-maximum-art",new Vector3(-24,3,32),17,new Vector3(-22,25,-28));
                }
            }finally{world?.Dispose();UnityEngine.Object.Destroy(root);}yield return null;
        }
        File.WriteAllLines(Path.Combine(output,"profile-envelope.csv"),rows);Assert.IsEmpty(violations,string.Join("\n",violations));
    }
    [UnityTest] public IEnumerator ZeroFieldTiersPreserveEnvelopesFogAndSourceReturn()
    {
        string output=Environment.GetEnvironmentVariable("NULL_DOCK_ART_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
        var map=new NullDockMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
        var root=new GameObject("Zero Field tier inspection");PlayableWorld world=null;
        try{
            world=new PlayableWorld(root.transform,p,staged:true);yield return world.Preparation;
            var details=root.GetComponentsInChildren<ModelDetailLod>();Assert.AreEqual(3,details.Length);
            var station=root.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Station hull, decks and equipment");var stationSource=station.sharedMesh;
            var light=new GameObject("Tier sun");light.transform.SetParent(root.transform);IndustrialLightingProfile.Load(map.Id).ApplyScene(light.AddComponent<Light>());
            var camera=new GameObject("Tier camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.orthographic=true;camera.farClipPlane=900;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.003f,.006f,.016f);
            ConfigureInspectionCamera(camera,root.transform,map.Id);yield return null;
            var rows=new System.Collections.Generic.List<string>{"name,tier,vertices,triangles,meshBytes"};
            foreach(var detail in details){
                int index=detail.name.EndsWith("1")?0:detail.name.EndsWith("2")?1:2;var solid=map.Modules[index];
                var meshes=new[]{detail.OriginalMeshes[0],detail.NormalMeshes[0],detail.FarMeshes[0]};
                Assert.Less(meshes[1].triangles.Length,meshes[0].triangles.Length);Assert.Less(meshes[2].triangles.Length,meshes[1].triangles.Length);
                Assert.AreSame(world.Fog.Texture,detail.Renderers[0].sharedMaterial.GetTexture("_FogMask"));
                for(int tier=0;tier<3;tier++){
                    var mesh=meshes[tier];Assert.AreEqual(meshes[0].bounds,mesh.bounds);Assert.AreEqual(mesh.vertexCount,mesh.normals.Length);Assert.AreEqual(mesh.vertexCount,mesh.uv2.Length);
                    Assert.True(mesh.normals.All(n=>!float.IsNaN(n.x)&&!float.IsNaN(n.y)&&!float.IsNaN(n.z)&&n.sqrMagnitude>.9f));
                    Assert.True(mesh.uv2.All(v=>v.x>=0&&v.x<=8&&v.x==Mathf.Round(v.x)));
                    Assert.True(mesh.vertices.All(v=>v.y>=-.001f&&v.y<=map.ModuleHeight+.001f&&(solid.Contains(new NavPoint(v.x,v.z))||solid.BoundaryDistanceSquared(new NavPoint(v.x,v.z))<.00001)),detail.name+" tier "+tier+" must fit original solid");
                    rows.Add($"{detail.name},{tier},{mesh.vertexCount},{mesh.triangles.Length/3},{UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mesh)}");
                }
            }
            File.WriteAllLines(Path.Combine(output,"machine-lod-costs.csv"),rows);
            // Verify live fog binding BEFORE the screenshot-only reveal substitutes a black mask.
            world.InspectionReveal();
            for(int tier=0;tier<3;tier++){
                foreach(var detail in details)detail.SetDetail(tier);
                SaveFrame(camera,output,"lod-"+tier+"-stand",Vector3.zero,22,new Vector3(0,45,-35));
                SaveFrame(camera,output,"lod-"+tier+"-cryo",new Vector3(-24,3,32),17,new Vector3(-22,25,-28));
                SaveFrame(camera,output,"lod-"+tier+"-overview",Vector3.zero,105,new Vector3(0,180,-130));
                Assert.AreSame(stationSource,station.sharedMesh,"Existing station must never switch tiers");
            }
            camera.transform.position=new Vector3(0,180,-130);camera.transform.LookAt(Vector3.zero);camera.orthographicSize=10000;
            world.SelectEnvironmentDetail(camera);foreach(var detail in details)Assert.AreEqual(2,detail.Level,"Shared projected-size selector reaches far");
            world.Clear();foreach(var detail in details){Assert.AreEqual(0,detail.Level);Assert.AreSame(detail.OriginalMeshes[0],detail.GetComponent<MeshFilter>().sharedMesh);}
        }finally{world?.Dispose();UnityEngine.Object.Destroy(root);}
    }
    [UnityTest] public IEnumerator ThreatPaletteHasStraightBoundariesAndReadableBaseDisks()
    {
        string output=Environment.GetEnvironmentVariable("NULL_DOCK_ART_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
        var map=new NullDockMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
        var root=new GameObject("Threat-zone inspection");PlayableWorld world=null;
        try{
            var art=NullDockArtProfile.Load();art.Validate();
            foreach(string field in new[]{"baseHue","baseSaturation","siteDiskLift","battleHue","contestedHue","rearHue"})
                Assert.True(art.Parameters().Any(v=>v.Path=="visual.nullDock."+field),field+" must remain editable and validated");
            world=new PlayableWorld(root.transform,p,staged:true);yield return world.Preparation;world.InspectionReveal();
            var light=new GameObject("Zone sun");light.transform.SetParent(root.transform);IndustrialLightingProfile.Load(map.Id).ApplyScene(light.AddComponent<Light>());
            var camera=new GameObject("Zone camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.orthographic=true;camera.farClipPlane=900;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            ConfigureInspectionCamera(camera,root.transform,map.Id);yield return null;
            var rows=new System.Collections.Generic.List<string>{"sample,r,g,b"};
            SaveFrame(camera,output,"threat-zones-topdown",Vector3.zero,100,new Vector3(0,220,0),frame=>{
                Color Sample(string name,float x,float z){
                    var at=new NavPoint(x,z);var screen=camera.WorldToScreenPoint(new Vector3(x,(float)map.SurfaceHeight(at),z));
                    var sum=Color.black;for(int dx=-2;dx<=2;dx++)for(int dy=-2;dy<=2;dy++)sum+=frame.GetPixel((int)screen.x+dx,(int)screen.y+dy);
                    var color=sum/25;rows.Add($"{name},{color.r},{color.g},{color.b}");return color;
                }
                void Red(string name,float x,float z){var color=Sample(name,x,z);Assert.Greater(color.r,color.g+.04f,name);Assert.Greater(color.r,color.b+.04f,name);}
                void Yellow(string name,float x,float z){var color=Sample(name,x,z);Assert.Greater(color.g,color.b+.04f,name);Assert.Greater(color.r,color.b+.04f,name);}
                // Opposite sides of each boundary catch diagonal cutoffs and stray red extensions.
                foreach(int sign in new[]{-1,1}){
                    Red("center Z edge "+sign,sign*39,-sign*27);Yellow("outside Z edge "+sign,sign*39,-sign*30);
                    Red("center X edge "+sign,sign*47,-sign*24);Yellow("outside X edge "+sign,sign*50,-sign*24);
                    var rear=Sample("rear "+sign,sign*80,sign*50);Assert.Greater(rear.g,rear.r+.025f,"rear green");
                }
                var disk=Sample("HQ disk",-68,3);var outside=Sample("HQ surrounding deck",-52,3);
                Assert.Greater(outside.b,outside.r+.04f,"HQ retains blue floor");Assert.Greater(disk.grayscale,outside.grayscale+.025f,"disk is visibly lighter");
                var ramp=Sample("neutral HQ ramp",-44,0);Assert.Less(Mathf.Max(ramp.r,ramp.g,ramp.b)-Mathf.Min(ramp.r,ramp.g,ramp.b),.08f,"ramp is not zone-colored");
            });
            File.WriteAllLines(Path.Combine(output,"threat-zone-samples.csv"),rows);
        }finally{world?.Dispose();UnityEngine.Object.Destroy(root);}
    }
    private static void ConfigureInspectionCamera(Camera camera,Transform root,string mapId)
    {
        camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root,IndustrialLightingProfile.Load(mapId));
        camera.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());
        camera.gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());
        camera.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());
    }
    private static Color SaveFrame(Camera camera,string output,string name,Vector3 target,float size,Vector3 offset,Action<Texture2D> inspect=null)
    {
        camera.transform.position=target+offset;camera.transform.LookAt(target);camera.orthographicSize=size;
        var rt=new RenderTexture(1600,1200,24);rt.Create();var previous=RenderTexture.active;camera.targetTexture=rt;
        Color sample=Color.clear;try{camera.Render();RenderTexture.active=rt;var frame=new Texture2D(1600,1200,TextureFormat.RGB24,false);try{frame.ReadPixels(new Rect(0,0,1600,1200),0,0);frame.Apply();inspect?.Invoke(frame);sample=frame.GetPixel(800,600);File.WriteAllBytes(Path.Combine(output,name+".png"),frame.EncodeToPNG());}finally{UnityEngine.Object.Destroy(frame);}}
        finally{camera.targetTexture=null;RenderTexture.active=previous;UnityEngine.Object.Destroy(rt);}return sample;
    }
    [UnityTest] public IEnumerator StationPresentationAndLosslessBatching()
    {
        string output=Environment.GetEnvironmentVariable("NULL_DOCK_ART_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
        var root=new GameObject("Null Dock visual inspection");PlayableWorld world=null;
        try{
            var map=new NullDockMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var art=NullDockArtProfile.Load();art.Validate();Assert.IsNotEmpty(art.Parameters());
            world=new PlayableWorld(root.transform,p,staged:true);yield return world.Preparation;world.InspectionReveal();
            var light=new GameObject("Inspection sun");light.transform.SetParent(root.transform);IndustrialLightingProfile.Load(map.Id).ApplyScene(light.AddComponent<Light>());
            var camera=new GameObject("Inspection camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.orthographic=true;camera.farClipPlane=900;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.003f,.006f,.016f);
            ConfigureInspectionCamera(camera,root.transform,map.Id);
            yield return null;
            var metrics=world.DockMetrics;Assert.Greater(metrics.primitiveMeshes,100);Assert.Less(metrics.combinedMeshes,metrics.primitiveMeshes);Assert.AreEqual(metrics.sourceTriangles,metrics.activeTriangles,"batching preserves every authored triangle");Assert.Less(metrics.activeVertices,metrics.sourceVertices,"exact vertex indexing removes duplicates");
            File.WriteAllText(Path.Combine(output,"mesh-metrics.json"),JsonUtility.ToJson(metrics,true));
            var textures=new[]{"Steel","Steel-b","Steel-c"}.Select(name=>Resources.Load<Texture2D>("Environment/NullDock/"+name)).ToArray();
            foreach(var texture in textures){Assert.NotNull(texture);Assert.Greater(texture.mipmapCount,1);Assert.AreEqual(TextureWrapMode.Repeat,texture.wrapMode);}
            File.WriteAllLines(Path.Combine(output,"texture-costs.csv"),new[]{"name,width,height,format,mipmaps,wrap,filter,aniso,runtimeBytes"}.Concat(textures.Select(t=>$"{t.name},{t.width},{t.height},{t.format},{t.mipmapCount},{t.wrapMode},{t.filterMode},{t.anisoLevel},{UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t)}")));
            foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())foreach(var material in renderer.sharedMaterials)Assert.True(material.shader.isSupported,material.shader.name);
            Shot("overview",Vector3.zero,105,new Vector3(0,180,-130));Shot("topology",Vector3.zero,100,new Vector3(0,220,0));
            Shot("home",new Vector3(-66,2,0),32,new Vector3(0,65,-45));Shot("central-spine",Vector3.zero,26,new Vector3(0,48,-35));Shot("pocket-barrier",new Vector3(-60,2,-30),24,new Vector3(0,45,-35));
            Shot("corner-mine",new Vector3(-84,0,-66),20,new Vector3(0,45,-32));
            Shot("machinery-low-angle",new Vector3(-24,3,32),17,new Vector3(-22,25,-28));
            // Neutral mine models at the six actual sites make the requested pocket relocation visible.
            int mineDisplay=900;foreach(var site in map.Sites(p).Where(s=>s.Kind==PlayableBuildingKind.Mine)){
                var mine=world.Building(mineDisplay++,"Mine",false);mine.Root.position=world.Point(site.Position);world.FaceBuilding(mine);
            }
            world.InspectionReveal();yield return null;Shot("overview-mines",Vector3.zero,105,new Vector3(0,180,-130));Shot("corner-mine-model",new Vector3(-84,0,-66),20,new Vector3(0,45,-32));
            // Authored model scale inspection only; these display actors are not simulated match state.
            int actorId=1000;foreach(var site in map.Sites(p).Where(s=>s.Kind!=PlayableBuildingKind.Mine)){
                var actor=world.Building(actorId++,site.Kind.ToString(),site.Position.X<0);actor.Root.position=world.Point(site.Position);world.FaceBuilding(actor);
                if(site.Kind==PlayableBuildingKind.Headquarters)foreach(var slot in site.Slots){var building=world.Building(actorId++,slot.Id%2==0?"Refinery":"Factory",site.Position.X<0);building.Root.position=world.Point(slot.Position);world.FaceBuilding(building);}
            }
            for(int i=0;i<5;i++){var actor=world.Tank(actorId++,true);actor.Root.position=world.Point(map.Point(42+i*2,78+i%2*3));}
            world.InspectionReveal();yield return null;
            Shot("models-scale-overview",Vector3.zero,105,new Vector3(0,180,-130));Shot("models-scale-home",new Vector3(-66,2,0),22,new Vector3(0,45,-35));
            void Shot(string name,Vector3 target,float size,Vector3 offset){
                camera.transform.position=target+offset;camera.transform.LookAt(target);camera.orthographicSize=size;
                var rt=new RenderTexture(1600,1200,24);rt.Create();var previous=RenderTexture.active;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var image=new Texture2D(1600,1200,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,1200),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());camera.targetTexture=null;RenderTexture.active=previous;UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(rt);
            }
        }finally{world?.Dispose();UnityEngine.Object.Destroy(root);}
    }
}
