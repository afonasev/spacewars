using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class CrossingsRockTextureTests
    {
        [UnityTest] public IEnumerator FoundryRetainsItsExistingSharedShaderPath()
        {
            var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try{Assert.AreEqual(0,material.GetVector("_CrossingsRockProjection").x);}
            finally{UnityEngine.Object.Destroy(material);}
            yield return new CrossingsSurfaceRenderingTests().SharedShaderRendersFoundryWithoutCrossingsPalette();
        }

        [UnityTest] public IEnumerator RockProjectionVariantsKeepOtherSurfacesAndFogUnchanged()
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;
            var root=new GameObject("Rock texture inspection");var ambient=RenderSettings.ambientLight;
            var target=new RenderTexture(1600,1000,24);target.Create();var pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            var output=Environment.GetEnvironmentVariable("SPACEWARS_ROCK_TEXTURE_EVIDENCE");
            if(string.IsNullOrEmpty(output))output=Path.GetFullPath(Path.Combine(Application.dataPath,"..","testResults","rock-texture"));
            Directory.CreateDirectory(output);
            using(var world=new PlayableWorld(root.transform,profile))
            try
            {
                world.InspectionReveal();RenderSettings.ambientLight=new Color(.65f,.65f,.66f);
                var sun=new GameObject("Sun").AddComponent<Light>();sun.transform.SetParent(root.transform);sun.type=LightType.Directional;sun.intensity=1.4f;sun.transform.rotation=Quaternion.Euler(48,-30,0);
                var camera=new GameObject("Gameplay camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);
                camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=700;camera.targetTexture=target;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.10f,.13f);
                var renderers=root.GetComponentsInChildren<MeshRenderer>();
                var materials=renderers.Select(r=>r.sharedMaterial).Distinct().Where(m=>m.HasProperty("_CrossingsRockProjection")).ToArray();
                Assert.IsNotEmpty(materials);
                void Mode(int mode){foreach(var m in materials)m.SetVector("_CrossingsRockProjection",new Vector4(mode==0?0:1,mode==2?.16f:.06f,0,0));}
                byte[] Capture(string file=null)
                {
                    camera.Render();var previous=RenderTexture.active;
                    try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();}
                    finally{RenderTexture.active=previous;}
                    int magenta=0;foreach(var c in pixels.GetPixels32())if(c.r>200&&c.b>200&&c.g<40)magenta++;
                    Assert.AreEqual(0,magenta,"Shared shader must compile on the real renderer");
                    if(file!=null)File.WriteAllBytes(Path.Combine(output,file+".png"),pixels.EncodeToPNG());
                    return pixels.GetRawTextureData<byte>().ToArray();
                }
                int nearSingular=0,triangles=0;float minimum=1;var block=new MaterialPropertyBlock();
                foreach(var renderer in renderers)
                {
                    renderer.GetPropertyBlock(block);if(block.GetFloat("_SurfaceRole")!=4||renderer.name.StartsWith("Detail —"))continue;
                    var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;var normals=mesh.normals;var indices=mesh.triangles;
                    for(int i=0;i<indices.Length;i+=3)
                    {
                        var n=normals[indices[i]].normalized;if(n.sqrMagnitude<.5f)continue;
                        float coverage=Mathf.Abs(n.x+n.y+n.z)/Mathf.Sqrt(3);minimum=Mathf.Min(minimum,coverage);if(coverage<.1f)nearSingular++;triangles++;
                    }
                }
                Assert.Greater(nearSingular,0,"The shipped rock mesh must reproduce the tangent projection failure");
                var focuses=new[]{new Vector3(-20,0,-18),new Vector3(-24,.04f,-52),new Vector3(-29,0,-29),new Vector3(20,0,18),new Vector3(-20,0,-18),new Vector3(-26,0,-18)};
                var names=new[]{"west-wall","south-wall","west-close","east-wall","roof-transition","west-pan"};
                var offset=new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);
                for(int view=0;view<focuses.Length;view++)
                {
                    camera.orthographicSize=(float)(view==2?profile.CameraMinZoom:profile.CameraOrthoSize);
                    camera.transform.position=focuses[view]+(view==4?new Vector3(0,36,-12):offset);camera.transform.LookAt(focuses[view]);
                    for(int frame=0;frame<3;frame++)yield return null;
                    for(int mode=0;mode<3;mode++){Mode(mode);Capture((mode==0?"before":mode==1?"crisp":"soft")+"-"+names[view]);}
                }
                // Freeze the view and remove animated water plus corrected rock pixels.
                // Other surface roles must remain byte-identical with the opt-in toggled.
                foreach(var renderer in renderers){renderer.GetPropertyBlock(block);float role=block.GetFloat("_SurfaceRole");if(role==4||role==5)renderer.enabled=false;}
                Mode(0);var unchanged=Capture();Mode(1);Assert.IsTrue(unchanged.SequenceEqual(Capture()),"Ground, steel and concrete must not change");
                foreach(var renderer in renderers)renderer.enabled=true;
                world.InspectionHide();Mode(0);var fog=Capture();Mode(1);Assert.IsTrue(fog.SequenceEqual(Capture("hidden-fog")),"Rock correction must preserve fully hidden fog output");
                var imports=new List<string>();
                foreach(var suffix in new[]{"","-b","-c"})
                {
                    var t=Resources.Load<Texture2D>("Environment/NaturalFrontier/Rock"+suffix);
                    imports.Add($"Rock{suffix}: {t.width}x{t.height}, {t.format}, mips={t.mipmapCount}, {t.wrapMode}, {t.filterMode}, aniso={t.anisoLevel}");
                }
                File.WriteAllLines(Path.Combine(output,"rock-imports.txt"),imports);
                File.WriteAllText(Path.Combine(output,"inspection.txt"),$"Production terrain {world.SurfaceRevision}; actual orthographic camera offset={offset}, size={profile.CameraOrthoSize}, close={profile.CameraMinZoom}; roof uses steep diagnostic camera. No simulation seed; revealed fog, diagnostic lighting, 1600x1000. Before=original oblique; crisp=.06, soft=.16. Existing oblique coverage minimum={minimum}, triangles below .1={nearSingular}/{triangles}. Negative controls: non-rock byte equality and fully hidden fog equality. Editor renders, not human/Player acceptance.\n");
                TestContext.WriteLine($"TANGENT_PROJECTION triangles={nearSingular}/{triangles}, minimum={minimum}");
            }
            finally{RenderSettings.ambientLight=ambient;UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(pixels);target.Release();UnityEngine.Object.Destroy(target);}
        }
    }
}
