using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Spacewars.Tests.PlayMode
{
    public sealed class NereidaPreviewTests
    {
        [UnityTest] public IEnumerator ActualMeshPreviewPreservesUnderpassesAndCapturesReviewViews()
        {
            var root=new GameObject("Nereida render test");var ambient=RenderSettings.ambientLight;
            var ambientMode=RenderSettings.ambientMode;float volume=AudioListener.volume;
            try {
                AudioListener.volume=0;RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.6f,.66f,.72f);
                var preview=root.AddComponent<NereidaPreview>();preview.Build(NereidaPreview.LoadProfile());
                var sun=new GameObject("Sun").AddComponent<Light>();sun.transform.SetParent(root.transform);sun.type=LightType.Directional;sun.intensity=1.4f;sun.transform.rotation=Quaternion.Euler(55,-35,0);
                var camera=new GameObject("Camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=900;camera.backgroundColor=new Color(.035f,.07f,.11f);camera.clearFlags=CameraClearFlags.SolidColor;
                yield return null;
                foreach(var bridge in preview.Map.Supports.Where(s=>s.IsBridge)) {
                    var mesh=root.GetComponentsInChildren<Transform>().Single(t=>t.name==bridge.Id).GetComponent<MeshFilter>().sharedMesh;
                    Assert.AreEqual(preview.Map.MiddleHeight-preview.Map.DeckThickness,mesh.bounds.min.y,1e-5,bridge.Id);
                    Assert.AreEqual(preview.Map.MiddleHeight,mesh.bounds.max.y,1e-5);
                }
                foreach(var support in preview.Map.Supports) {
                    var mesh=root.GetComponentsInChildren<Transform>().Single(t=>t.name==support.Id).GetComponent<MeshFilter>().sharedMesh;
                    foreach(var point in support.Bounds.Footprint.Vertices) {
                        var expected=new Vector3((float)point.X,(float)support.HeightAt(point),(float)point.Z);
                        Assert.True(mesh.vertices.Any(vertex=>Vector3.Distance(vertex,expected)<.001f),"Rendered boundary must include the exact support vertex: "+support.Id);
                    }
                }
                Assert.AreEqual(20,root.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("Reserved site ")));
                var labs=root.GetComponentsInChildren<Transform>().Where(t=>t.name=="Station laboratory").ToArray();
                Assert.AreEqual(4,labs.Length);Assert.AreEqual(4,labs.Select(t=>t.localPosition).Distinct().Count(),"Station compounds must use distinct layouts.");
                Assert.AreEqual(4,labs.Select(t=>t.localScale).Distinct().Count(),"Station laboratories must have distinct authored footprints.");
                foreach(var fracture in root.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Ice fracture/"))) {
                    Assert.IsNull(fracture.GetComponent<Collider>());
                    foreach(var vertex in fracture.GetComponent<MeshFilter>().sharedMesh.vertices)
                        Assert.True(preview.Map.IsValidLocation(new Spacewars.Simulation.NavLocation(new Spacewars.Simulation.NavPoint(vertex.x,vertex.z),"ice"),0),"Ice artwork must not extend into water");
                }
                Assert.AreEqual(4,root.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("Ice fracture/")));
                foreach(string feature in new[]{"Antenna spine","Observatory dome","Cooling stack","Stepped roof annex"})
                    Assert.True(root.GetComponentsInChildren<Transform>().Any(t=>t.name==feature),feature);
                preview.Art.Validate();
                foreach(var parameter in preview.Art.Parameters()) {
                    float old=parameter.Read();parameter.Write(float.NaN);
                    Assert.Throws<ArgumentException>(()=>preview.Art.Validate(),parameter.Path);parameter.Write(old);
                }
                var drifts=root.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Snow drift/")).ToArray();
                Assert.AreEqual(8,drifts.Length);
                foreach(var drift in drifts) {
                    Assert.IsNull(drift.GetComponent<Collider>());
                    var mesh=drift.GetComponent<MeshFilter>().sharedMesh;
                    foreach(var vertex in mesh.vertices)Assert.True(preview.Map.IsValidLocation(new Spacewars.Simulation.NavLocation(new Spacewars.Simulation.NavPoint(vertex.x,vertex.z),"ice"),0),drift.name);
                    Assert.Less(mesh.bounds.max.y,.03f,"Flush art cannot close or raise a route");
                }
                var imports=new System.Collections.Generic.List<string>();
                foreach(var role in new[]{"Rock","Concrete","Steel"})foreach(var suffix in new[]{"","-b","-c"}) {
                    var tex=Resources.Load<Texture2D>("Environment/NaturalFrontier/"+role+suffix);
                    Assert.IsNotNull(tex);Assert.GreaterOrEqual(tex.width,1024);Assert.Greater(tex.mipmapCount,1);
                    Assert.AreEqual(TextureWrapMode.Repeat,tex.wrapMode);Assert.AreEqual(FilterMode.Trilinear,tex.filterMode);Assert.GreaterOrEqual(tex.anisoLevel,8);
                    Assert.AreNotEqual(TextureFormat.RGBA32,tex.format,"Imported family must remain compressed");
                    imports.Add(tex.name+": "+tex.width+"x"+tex.height+" "+tex.format+" mips="+tex.mipmapCount+" "+tex.wrapMode+" "+tex.filterMode+" aniso="+tex.anisoLevel);
                }
                foreach(var support in preview.Map.Supports)Assert.AreEqual("Spacewars/NereidaSurface",root.GetComponentsInChildren<Transform>().Single(t=>t.name==support.Id).GetComponent<Renderer>().sharedMaterial.shader.name);
                foreach(var finish in root.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Finish/"))) {
                    var point=new Spacewars.Simulation.NavPoint(finish.position.x,finish.position.z);
                    Assert.True(Enumerable.Range(0,4).Any(q=>preview.Map.IsValidLocation(new Spacewars.Simulation.NavLocation(point,"home/"+q),0)),finish.name);
                    var lab=labs.Single(l=>Math.Abs(l.position.x-finish.position.x)<20&&Math.Abs(l.position.z-finish.position.z)<20);
                    foreach(var vertex in finish.GetComponent<MeshFilter>().sharedMesh.vertices) {
                        var local=lab.InverseTransformPoint(finish.TransformPoint(vertex));
                        Assert.LessOrEqual(Math.Abs(local.x),.501f,finish.name+" laboratory X envelope");
                        Assert.LessOrEqual(Math.Abs(local.z),.501f,finish.name+" laboratory Z envelope");
                    }
                    Assert.Greater(finish.GetComponent<Renderer>().bounds.min.y,(float)preview.Map.HomeHeight-.01f);
                }
                Assert.False(root.GetComponentsInChildren<Renderer>().Any(r=>r.sharedMaterials.Any(m=>!m||!m.shader.isSupported)));
                var output=Environment.GetEnvironmentVariable("NEREIDA_EVIDENCE")??Path.GetFullPath("../.local/nereida-review");Directory.CreateDirectory(output);
                Shot("top-down",new Vector3(0,340,0),new Vector3(0,0,0),148);
                Shot("oblique",new Vector3(180,210,-220),new Vector3(0,0,0),205);
                File.WriteAllLines(Path.Combine(output,"texture-imports.txt"),imports);
                Shot("ice-close",new Vector3(25,45,106),new Vector3(0,0,94),30);
                Shot("station-close",new Vector3(140,70,147),new Vector3(108,12,109),36);
                Shot("vertical-cliff",new Vector3(53,5,124),new Vector3(80,5,115),23);
                Shot("underpass",new Vector3(0,8,95),new Vector3(0,3,42),34);
                File.WriteAllText(Path.Combine(output,"render-state.txt"),preview.Map.Id+"@"+preview.Map.Revision+"; actual Unity meshes; Editor PlayMode inspection; no live match; no seed-dependent geometry; profile Resources/NereidaProfile.json; art v4 preview only; ordinary match pending.\n");
                void Shot(string name,Vector3 position,Vector3 target,float size) {
                    camera.transform.position=position;camera.transform.LookAt(target,Math.Abs(position.x)<.01f&&Math.Abs(position.z)<.01f?Vector3.forward:Vector3.up);camera.orthographicSize=size;
                    // Fixed capture resolution is evidence quality, not gameplay tuning.
                    var rt=new RenderTexture(1600,1600,24);var old=RenderTexture.active;var png=new Texture2D(1600,1600,TextureFormat.RGB24,false);
                    try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;png.ReadPixels(new Rect(0,0,1600,1600),0,0);png.Apply();
                        var pixels=png.GetPixels32();if(name=="top-down"||name=="oblique"||name=="underpass")Assert.Greater(pixels.Count(c=>c.g>c.r+12&&c.b>c.r+12),pixels.Length/20,"Ice/water missing from actual render");
                        File.WriteAllBytes(Path.Combine(output,name+".png"),png.EncodeToPNG());}
                    finally{camera.targetTexture=null;RenderTexture.active=old;Object.Destroy(png);rt.Release();Object.Destroy(rt);}
                }
            }
            finally{AudioListener.volume=volume;RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=ambientMode;Object.Destroy(root);}
            yield return null;
        }
    }
}
