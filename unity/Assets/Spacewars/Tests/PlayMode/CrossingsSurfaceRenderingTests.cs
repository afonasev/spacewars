using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class CrossingsSurfaceRenderingTests
    {
        [UnityTest] public IEnumerator SharedShaderRendersFoundryWithoutCrossingsPalette()
        {
            var map=new FoundryMap(JsonUtility.FromJson<FoundryProfileData>(Resources.Load<TextAsset>("FoundryProfile").text));
            var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            var root=new GameObject("Foundry shared shader inspection");
            var ambient=RenderSettings.ambientLight;var volume=AudioListener.volume;AudioListener.volume=0;
            var target=new RenderTexture(800,500,24);target.Create();Texture2D pixels=null;
            var world=new PlayableWorld(root.transform,profile);world.InspectionReveal();
            try
            {
                RenderSettings.ambientLight=new Color(.65f,.65f,.66f);
                var sun=new GameObject("Sun").AddComponent<Light>();sun.transform.SetParent(root.transform);
                sun.type=LightType.Directional;sun.intensity=1.4f;sun.transform.rotation=Quaternion.Euler(48,-30,0);
                var camera=new GameObject("Camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);
                camera.orthographic=true;camera.orthographicSize=120;camera.farClipPlane=700;camera.targetTexture=target;
                camera.transform.position=new Vector3(0,200,-100);camera.transform.LookAt(Vector3.zero);
                foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())
                    if(renderer.sharedMaterial.HasProperty("_CrossingsPalette"))Assert.AreEqual(0,renderer.sharedMaterial.GetVector("_CrossingsPalette").x);
                for(int i=0;i<5;i++)yield return null;
                var previous=RenderTexture.active;
                try{RenderTexture.active=target;pixels=new Texture2D(800,500,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,800,500),0,0);pixels.Apply();}
                finally{RenderTexture.active=previous;}
                int visible=0,magenta=0;
                foreach(var c in pixels.GetPixels32())
                {
                    if(Mathf.Max(c.r,Mathf.Max(c.g,c.b))>45)visible++;
                    if(c.r>200&&c.b>200&&c.g<40)magenta++;
                }
                Assert.Greater(visible,100000,"Foundry terrain must render through the shared shader");
                Assert.AreEqual(0,magenta,"Shared shader must compile for Foundry surface roles");
                var output=Environment.GetEnvironmentVariable("SPACEWARS_SURFACE_EVIDENCE");
                if(!string.IsNullOrEmpty(output)){Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,"foundry-shared-shader.png"),pixels.EncodeToPNG());}
            }
            finally
            {
                RenderSettings.ambientLight=ambient;AudioListener.volume=volume;
                if(pixels)UnityEngine.Object.Destroy(pixels);target.Release();UnityEngine.Object.Destroy(target);
                world.Dispose();UnityEngine.Object.Destroy(root);
            }
        }
        // Optional durable frames use the production world, shader and profile.
        // RenderTexture capture does not need desktop focus or a Player build.
        [UnityTest] public IEnumerator ProductionSurfacesRenderAtGameplayAndOverviewScales()
        {
            var map=new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
            var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            var root=new GameObject("Crossings surface inspection");
            var world=new PlayableWorld(root.transform,profile);world.InspectionReveal();
            var ambient=RenderSettings.ambientLight;var volume=AudioListener.volume;AudioListener.volume=0;
            var target=new RenderTexture(1600,1000,24);target.Create();Texture2D pixels=null;
            var output=Environment.GetEnvironmentVariable("SPACEWARS_SURFACE_EVIDENCE");
            if(!string.IsNullOrEmpty(output))Directory.CreateDirectory(output);
            try
            {
                RenderSettings.ambientLight=new Color(.65f,.65f,.66f);
                var sun=new GameObject("Inspection sun").AddComponent<Light>();sun.transform.SetParent(root.transform);
                sun.type=LightType.Directional;sun.intensity=1.4f;sun.transform.rotation=Quaternion.Euler(48,-30,0);
                var camera=new GameObject("Inspection camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);
                camera.fieldOfView=48;camera.nearClipPlane=.1f;camera.farClipPlane=700;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.10f,.13f);camera.targetTexture=target;
                pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);
                var points=new List<Vector2>{Vector2.zero,new Vector2(-46,-46),new Vector2(-46,-46),new Vector2(46,46),new Vector2(-19,-18),Vector2.zero};
                var zooms=new List<float>{100f,18f,8f,18f,12f,8f};
                var names=new List<string>{"overview","base-a-normal","base-a-close","base-b-normal","road-and-cliff","bridge-close"};
                var actors=new List<PlayableWorld.Actor>();
                var serviceViews=new Dictionary<string,Vector3>();
                foreach(var site in TerritoryRules.Sites(profile).Where(s=>s.Kind==PlayableBuildingKind.Mine))
                {
                    var actor=world.Building(1000+site.Id,"Mine",false);actor.Root.position=new Vector3((float)site.Position.X,(float)map.SurfaceHeight(site.Position),(float)site.Position.Z);
                    actor.Health.parent.gameObject.SetActive(false);actor.Selection.SetActive(false);PlayableWorld.PaintOwner(actor,new Color(.55f,.57f,.53f));actors.Add(actor);
                    points.Add(new Vector2((float)site.Position.X,(float)site.Position.Z));zooms.Add(12);names.Add("mine-"+site.Id);
                }
                var diagnostic=new Material(Resources.Load<Shader>("TerritoryFog"));
                try{using(var surfaces=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,diagnostic,profile))
                {
                    var placements=ThreeCrossingsDetails.Plan(map,profile,surfaces);
                    foreach(var site in TerritoryRules.Sites(profile).Where(s=>s.Kind==PlayableBuildingKind.Mine))
                    {
                        var modules=placements.Where(p=>p.SiteId==site.Id).ToArray();
                        var center=(new Vector2((float)site.Position.X,(float)site.Position.Z)+modules.Select(p=>new Vector2(p.Position.x,p.Position.z)).Aggregate(Vector2.zero,(a,b)=>a+b))/3;
                        points.Add(center);zooms.Add(18);names.Add("industry-"+site.Id);
                    }
                    foreach(var compound in placements.Where(p=>p.Kind=="PerimeterHangar"))
                    {
                        points.Add(new Vector2(compound.Position.x,compound.Position.z));zooms.Add(20);names.Add("compound-"+(-compound.SiteId));
                        points.Add(new Vector2(compound.Position.x+5,compound.Position.z-5));zooms.Add(24);names.Add("compound-pan-"+(-compound.SiteId));
                    }
                    foreach(var module in placements.Where(p=>CorporateOutpostModules.IsModule(p.Kind)))
                    {
                        string name="mine-service-"+module.SiteId+"-"+module.Kind;
                        points.Add(new Vector2(module.Position.x,module.Position.z));zooms.Add(5.5f);names.Add(name);
                        var preferred=Quaternion.Euler(0,module.Rotation,0)*new Vector3(module.Kind=="CargoModule"?-5:5,5,-4.4f);
                        var selected=preferred;bool clearView=false;
                        // Inspect from a real open corridor, never through an outer cliff.
                        // Orbit samples are diagnostic camera poses, not gameplay/art tuning.
                        foreach(float turn in new[]{0f,45f,-45f,90f,-90f,135f,-135f,180f})
                        {
                            var candidate=Quaternion.Euler(0,turn,0)*preferred;
                            if(!map.SupportsSweep(new NavPoint(module.Position.x,module.Position.z),new NavPoint(module.Position.x+candidate.x,module.Position.z+candidate.z),camera.nearClipPlane))continue;
                            selected=candidate;clearView=true;break;
                        }
                        Assert.IsTrue(clearView,"Service inspection needs an unobstructed terrain corridor: "+name);
                        serviceViews[name]=selected;
                    }
                    foreach(var access in surfaces.HangarAccesses)
                    {points.Add(access.Gate+access.Direction*surfaces.Profile.hangarApronLength);zooms.Add(14);names.Add("hangar-access-"+(-access.Group));}
                    foreach(var utility in placements.Where(p=>p.Kind=="VentilationBlock"||p.Kind=="CableCabinet"||p.Kind=="PumpNode"))
                    {points.Add(new Vector2(utility.Position.x,utility.Position.z));zooms.Add(7);names.Add("utility-"+utility.Kind);}
                    foreach(var wreck in placements.Where(p=>p.Kind=="HeavyTractorWreck"))
                    {points.Add(new Vector2(wreck.Position.x,wreck.Position.z));zooms.Add(14);names.Add("tractor-"+names.Count);}
                }}finally{UnityEngine.Object.Destroy(diagnostic);}
                points.Add(Vector2.zero);zooms.Add(14);names.Add("bridge-structure");
                points.Add(new Vector2(0,-(float)map.CentralBridgeWidth/2));zooms.Add(8);names.Add("bridge-fittings");
                points.Add(new Vector2(-46,-46));zooms.Add(12);names.Add("base-a-vertical");
                points.Add(Vector2.zero);zooms.Add(100);names.Add("terrain-fog");
                for(int frame=0;frame<points.Count;frame++)
                {
                    var p=points[frame];float height=(float)map.SurfaceHeight(new NavPoint(p.x,p.y));
                    var focus=new Vector3(p.x,height,p.y);float zoom=zooms[frame];
                    if(names[frame].StartsWith("compound"))focus.y=0; // Ground-level service compounds.

                    bool hidden=names[frame]=="terrain-fog";
                    if(hidden){world.InspectionHide();foreach(var actor in actors)actor.Root.gameObject.SetActive(false);}
                    foreach(var actor in actors)world.FaceBuilding(actor,camera);
                    bool vertical=names[frame]=="base-a-vertical";
                    camera.orthographic=frame==0||hidden||vertical;camera.orthographicSize=vertical?8:72;
                    camera.transform.position=focus+(vertical?Vector3.up*30:names[frame].StartsWith("compound")?new Vector3((float)profile.CameraOffsetX, (float)profile.CameraHeight,(float)profile.CameraOffsetZ)*zoom/26:new Vector3(0,zoom*1.35f,-zoom*.72f));camera.transform.LookAt(focus,vertical?Vector3.forward:Vector3.up);
                    if(serviceViews.TryGetValue(names[frame],out var closeOffset)){camera.transform.position=focus+closeOffset;camera.transform.LookAt(focus+Vector3.up*.4f);}
                    if(names[frame]=="bridge-fittings"){camera.transform.position=focus+new Vector3(3,3,-7);camera.transform.LookAt(focus-Vector3.up*.15f);}
                    for(int i=0;i<5;i++)yield return null;
                    var previous=RenderTexture.active;
                    try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();}
                    finally{RenderTexture.active=previous;}
                    int visible=0,magenta=0;
                    foreach(var c in pixels.GetPixels32())
                    {
                        if(Mathf.Max(c.r,Mathf.Max(c.g,c.b))>45)visible++;
                        if(c.r>200&&c.b>200&&c.g<40)magenta++;
                    }
                    if(!hidden)Assert.Greater(visible,100000,"Terrain must actually render: "+names[frame]);
                    Assert.AreEqual(0,magenta,"Shader error material: "+names[frame]);
                    if(!string.IsNullOrEmpty(output))File.WriteAllBytes(Path.Combine(output,names[frame]+".png"),pixels.EncodeToPNG());
                }
                if(!string.IsNullOrEmpty(output))
                {
                    var imports=new List<string>();
                    foreach(var suffix in new[]{"","-b","-c"})
                    {
                        var tex=Resources.Load<Texture2D>("Environment/NaturalFrontier/Concrete"+suffix);
                        imports.Add($"Concrete{suffix}: {tex.width}x{tex.height} {tex.format}; mips={tex.mipmapCount}; filter={tex.filterMode}; aniso={tex.anisoLevel}; wrap={tex.wrapMode}");
                    }
                    File.WriteAllLines(Path.Combine(output,"concrete-import.txt"),imports);
                }
                if(!string.IsNullOrEmpty(output))File.WriteAllText(Path.Combine(output,"profiles.txt"),
                    map.Id+"@"+map.Revision+"\n"+world.SurfaceRevision+"\n"+world.EnvironmentRevision+"\nProduction terrain and diagnostic real mine models, no simulation seed; FOV48, 1600x1000; original cameras plus mine/wreck/fog views. Not human acceptance.\n");
            }
            finally
            {
                RenderSettings.ambientLight=ambient;AudioListener.volume=volume;
                if(pixels)UnityEngine.Object.Destroy(pixels);target.Release();UnityEngine.Object.Destroy(target);
                world.Dispose();UnityEngine.Object.Destroy(root);
            }
        }
    }
}
