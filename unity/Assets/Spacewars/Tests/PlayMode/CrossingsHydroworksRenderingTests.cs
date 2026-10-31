using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class CrossingsHydroworksRenderingTests
    {
        static string Costs(GameObject root)
        {
            var renderers=root.GetComponentsInChildren<MeshRenderer>();
            var meshes=root.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).Where(m=>m).Distinct().ToArray();
            var materials=renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m).Distinct().ToArray();
            var textures=materials.SelectMany(m=>m.GetTexturePropertyNames().Select(n=>m.GetTexture(n))).Where(t=>t).Distinct().ToArray();
            return JsonUtility.ToJson(new Cost{renderers=renderers.Length,meshes=meshes.Length,vertices=meshes.Sum(m=>m.vertexCount),triangles=meshes.Sum(m=>m.triangles.Length/3),submeshes=meshes.Sum(m=>m.subMeshCount),materials=materials.Length,meshBytes=meshes.Sum(m=>Profiler.GetRuntimeMemorySizeLong(m)),bufferBytes=meshes.Sum(BufferBytes),textureBytes=textures.Sum(t=>Profiler.GetRuntimeMemorySizeLong(t)),textures=textures.Length},true);
        }
        [Serializable] class Cost {public int renderers,meshes,vertices,triangles,submeshes,materials,textures;public long meshBytes,bufferBytes,textureBytes;}
        static long BufferBytes(Mesh mesh)=>mesh.vertexCount*Enumerable.Range(0,mesh.vertexBufferCount).Sum(i=>mesh.GetVertexBufferStride(i))+(long)mesh.triangles.Length*(mesh.indexFormat==UnityEngine.Rendering.IndexFormat.UInt16?2:4);
        [Test] public void ProductionWorldCostsOnly()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_HYDRO_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            var map=new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
            var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            var root=new GameObject("Hydroworks cost-only QA");var world=new PlayableWorld(root.transform,profile);
            try{world.InspectionReveal();File.WriteAllText(Path.Combine(output,"world-costs.json"),Costs(root));}
            finally{world.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
        [UnityTest] public IEnumerator ProductionHydroworksMatchedViewsAndCosts()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_HYDRO_EVIDENCE");
            Assert.IsNotEmpty(output,"Explicit owned evidence directory required");Directory.CreateDirectory(output);
            var map=new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
            var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            var root=new GameObject("Hydroworks production QA");var world=new PlayableWorld(root.transform,profile);world.InspectionReveal();
            var ambient=RenderSettings.ambientLight;var target=new RenderTexture(1600,1000,24);target.Create();var pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            try
            {
                RenderSettings.ambientLight=new Color(.65f,.65f,.66f);
                var sun=new GameObject("Matched sun").AddComponent<Light>();sun.transform.SetParent(root.transform);sun.type=LightType.Directional;sun.intensity=1.4f;sun.transform.rotation=Quaternion.Euler(48,-30,0);
                var camera=new GameObject("Matched camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.fieldOfView=48;camera.nearClipPlane=.1f;camera.farClipPlane=700;camera.targetTexture=target;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.10f,.13f);
                var views=new[]{"overview","ordinary","hero-close","north-bridge","south-bridge","water-bank","vertical","transition","water-motion-a","water-motion-b","fog","gameplay-camera","gameplay-close","quarry-gameplay","north-gameplay","south-gameplay"};
                var focus=new[]{Vector3.zero,Vector3.zero,new Vector3(0,-1,-(float)map.CentralBridgeWidth/2),new Vector3(0,0,(float)map.CrossingZ),new Vector3(0,0,-(float)map.CrossingZ),new Vector3(7,-2,23),new Vector3(-29,2,25),new Vector3(-19,0,-18),new Vector3(0,-3,-18),new Vector3(0,-3,-18),Vector3.zero,new Vector3(0,0,-10),new Vector3(10,0,-12),new Vector3(9,-1,23),new Vector3(0,0,38),new Vector3(0,0,-38)};
                var zoom=new[]{100f,40,15,20,20,12,12,14,14,14,100,12.8f,6,8,12.8f,12.8f};
                for(int v=0;v<views.Length;v++)
                {
                    bool overview=v==0||v==10;camera.orthographic=overview;camera.orthographicSize=72;
                    camera.transform.position=focus[v]+new Vector3(0,zoom[v]*1.35f,-zoom[v]*.72f);camera.transform.LookAt(focus[v]);
                    if(v==10)world.InspectionHide();
                    if(v>=11)
                    {
                        world.InspectionReveal();var lighting=IndustrialLightingProfile.Load(map.Id);
                        sun.color=Color.HSVToRGB(lighting.sunHue,lighting.sunSaturation,1);sun.intensity=lighting.sunIntensity;sun.transform.rotation=Quaternion.Euler(lighting.sunElevation,lighting.sunAzimuth,0);
                        RenderSettings.ambientLight=Color.HSVToRGB(lighting.ambientHue,lighting.ambientSaturation,lighting.ambientValue);
                        camera.orthographic=true;camera.orthographicSize=zoom[v];camera.transform.position=focus[v]+new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);camera.transform.LookAt(focus[v]);
                    }
                    if(v==9)yield return new WaitForSecondsRealtime(.8f);
                    for(int j=0;j<5;j++)yield return null;
                    var old=RenderTexture.active;try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();}finally{RenderTexture.active=old;}
                    int bright=0,magenta=0;foreach(var c in pixels.GetPixels32()){if(Mathf.Max(c.r,Mathf.Max(c.g,c.b))>45)bright++;if(c.r>200&&c.b>200&&c.g<40)magenta++;}
                    Assert.AreEqual(0,magenta,"Shader compilation: "+views[v]);if(v!=10)Assert.Greater(bright,100000,"Actual production rendering: "+views[v]);
                    File.WriteAllBytes(Path.Combine(output,views[v]+".png"),pixels.EncodeToPNG());
                }
                world.InspectionReveal();
                var hydroRenderers=root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Hydroworks ")).ToArray();
                if(hydroRenderers.Length>0)
                {
                    var shared=hydroRenderers[0].sharedMaterial;
                    var extent=shared.GetVector("_CrossingsHydroExtent");
                    // Diagnostic freeze only for controlled same-candidate geometry A/B; production clock stays live.
                    shared.SetVector("_CrossingsHydroExtent",new Vector4(extent.x,extent.y,1,7));
                    var unbatched=new GameObject("Unbatched same detailed candidate");unbatched.transform.SetParent(root.transform);
                    var parts=ThreeCrossingsHydroworks.Build(map,ThreeCrossingsSurfaceProfile.Load(),false);
                    foreach(var part in parts)
                    {
                        var obj=new GameObject(part.Name);obj.transform.SetParent(unbatched.transform);obj.AddComponent<MeshFilter>().sharedMesh=part.Mesh;var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=shared;
                        var block=new MaterialPropertyBlock();block.SetFloat("_SurfaceRole",part.Role);block.SetFloat("_Dressing",part.Role==3?3:part.Role==5?2:1);block.SetColor("_BaseColor",Color.white);renderer.SetPropertyBlock(block);
                    }
                    try
                    {
                        camera.orthographic=false;camera.transform.position=focus[2]+new Vector3(0,zoom[2]*1.35f,-zoom[2]*.72f);camera.transform.LookAt(focus[2]);
                        byte[] reference=null;int different=0;
                        for(int mode=0;mode<2;mode++)
                        {
                            unbatched.SetActive(mode==0);foreach(var r in hydroRenderers)r.enabled=mode==1;
                            for(int j=0;j<5;j++)yield return null;
                            var old=RenderTexture.active;try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();}finally{RenderTexture.active=old;}
                            File.WriteAllBytes(Path.Combine(output,mode==0?"same-candidate-unbatched.png":"same-candidate-optimized.png"),pixels.EncodeToPNG());
                            var bytes=pixels.GetRawTextureData<byte>().ToArray();if(mode==0)reference=bytes;else for(int j=0;j<bytes.Length;j++)if(bytes[j]!=reference[j])different++;
                        }
                        File.WriteAllText(Path.Combine(output,"same-candidate-pixels.txt"),"1600x1000 RGB24, camera/light/material/fog identical, water shader clock frozen at7 only for A/B. Different RGB channels="+different+" / "+reference.Length+". Exact triangle attribute test independently verifies full geometry.\n");
                    }
                    finally{foreach(var r in hydroRenderers)r.enabled=true;foreach(var part in parts)UnityEngine.Object.Destroy(part.Mesh);UnityEngine.Object.Destroy(unbatched);shared.SetVector("_CrossingsHydroExtent",extent);}
                }
                if(hydroRenderers.Length>0)
                {
                    var shared=hydroRenderers[0].sharedMaterial;var deep=shared.GetColor("_WaterDeep");var shallow=shared.GetColor("_WaterShallow");var extent=shared.GetVector("_CrossingsHydroExtent");
                    var means=new Color[2];
                    try
                    {
                        shared.SetVector("_CrossingsHydroExtent",new Vector4(extent.x,extent.y,1,7));camera.orthographic=true;camera.orthographicSize=4;
                        var at=new Vector3(0,-3,22);camera.transform.position=at+new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);camera.transform.LookAt(at);
                        for(int mode=0;mode<2;mode++)
                        {
                            var color=mode==0?new Color(.5f,.03f,.03f):new Color(.03f,.03f,.5f);shared.SetColor("_WaterDeep",color);shared.SetColor("_WaterShallow",color);
                            for(int j=0;j<5;j++)yield return null;
                            var old=RenderTexture.active;try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();}finally{RenderTexture.active=old;}
                            var colors=pixels.GetPixels();Color sum=Color.black;int count=0;for(int y=400;y<600;y++)for(int x=650;x<950;x++){sum+=colors[y*1600+x];count++;}means[mode]=sum/count;
                            File.WriteAllBytes(Path.Combine(output,mode==0?"diagnostic-palette-red.png":"diagnostic-palette-blue.png"),pixels.EncodeToPNG());
                        }
                        Assert.Greater(means[0].r,means[1].r+.02f,"Named water palette must drive hydro red channel");Assert.Greater(means[1].b,means[0].b+.02f,"Named water palette must drive hydro blue channel");
                        File.WriteAllText(Path.Combine(output,"palette-control.txt"),"Diagnostic override only, not shipped colors. Red mean="+means[0]+" Blue mean="+means[1]+"; frozen time7 same camera, ROI650:950x400:600.\n");
                    }
                    finally{shared.SetColor("_WaterDeep",deep);shared.SetColor("_WaterShallow",shallow);shared.SetVector("_CrossingsHydroExtent",extent);}
                }
                File.WriteAllText(Path.Combine(output,"world-costs.json"),Costs(root));
                File.WriteAllText(Path.Combine(output,"identity.txt"),map.Id+"@"+map.Revision+"\n"+world.SurfaceRevision+"\n1600x1000 FOV48; fixed authored layout, no match seed; same sun/ambient/cameras. Production renderer in Editor; no Player/device/human acceptance.\nRenderers/submeshes are potential base-pass submissions, not measured driver draw calls or FPS. Mesh/texture bytes are Unity runtime object memory.\n");
                Assert.IsEmpty(root.GetComponentsInChildren<Collider>(),"Presentation must not introduce collision authority");
            }
            finally{RenderSettings.ambientLight=ambient;world.Dispose();UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(pixels);target.Release();UnityEngine.Object.Destroy(target);}
        }
    }
}
