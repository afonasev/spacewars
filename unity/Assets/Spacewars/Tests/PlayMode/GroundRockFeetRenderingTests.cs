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
    public sealed class GroundRockFeetRenderingTests
    {
        [UnityTest] public IEnumerator FoundryKeepsItsExistingGroundFinish()
        {
            var material=new Material(Resources.Load<Shader>("TerritoryFog"));
            try{Assert.AreEqual(0,material.GetVector("_CrossingsGroundDetail").x);}
            finally{UnityEngine.Object.Destroy(material);}
            yield return new CrossingsSurfaceRenderingTests().SharedShaderRendersFoundryWithoutCrossingsPalette();
        }
        [UnityTest] public IEnumerator QuieterGroundAndFootVariantsPreserveOtherRolesAndFog()
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;var root=new GameObject("Ground finish inspection");var ambient=RenderSettings.ambientLight;
            var target=new RenderTexture(1600,1000,24);target.Create();var pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            var output=Environment.GetEnvironmentVariable("SPACEWARS_GROUND_EVIDENCE");
            if(string.IsNullOrEmpty(output))output=Path.GetFullPath(Path.Combine(Application.dataPath,"..","testResults","ground-rock-feet"));
            Directory.CreateDirectory(output);
            using(var world=new PlayableWorld(root.transform,profile))
            try
            {
                world.InspectionReveal();RenderSettings.ambientLight=new Color(.65f,.65f,.66f);
                var sun=new GameObject("Sun").AddComponent<Light>();sun.transform.SetParent(root.transform);sun.type=LightType.Directional;sun.intensity=1.4f;sun.transform.rotation=Quaternion.Euler(48,-30,0);
                var camera=new GameObject("Gameplay camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=700;camera.targetTexture=target;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.10f,.13f);
                var renderers=root.GetComponentsInChildren<MeshRenderer>();var materials=renderers.Select(r=>r.sharedMaterial).Distinct().Where(m=>m.HasProperty("_CrossingsGroundDetail")).ToArray();
                var block=new MaterialPropertyBlock();float Role(MeshRenderer r){r.GetPropertyBlock(block);return block.GetFloat("_SurfaceRole");}
                Assert.IsNotEmpty(materials);
                void Mode(int mode,bool feet=true)
                {
                    foreach(var m in materials)
                    {
                        m.SetVector("_CrossingsGroundDetail",mode==2?new Vector4(1,1,1,.3f):new Vector4(mode==0?0:1,.8f,1,.3f));
                        var foot=new Vector4(.65f,.2f,2.2f,.2f);
                        if(!feet){foot.y=0;foot.w=0;}m.SetVector("_CrossingsRockFoot",foot);
                    }
                }
                Color32[] Capture(string file=null)
                {
                    camera.Render();var previous=RenderTexture.active;
                    try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();}
                    finally{RenderTexture.active=previous;}
                    var colors=pixels.GetPixels32();Assert.AreEqual(0,colors.Count(c=>c.r>200&&c.b>200&&c.g<40),"Shared shader error material");
                    if(file!=null)File.WriteAllBytes(Path.Combine(output,file+".png"),pixels.EncodeToPNG());return colors;
                }
                var focuses=new[]{new Vector3(-20,0,-18),new Vector3(-24,.04f,-52),new Vector3(-29,0,-34),new Vector3(20,0,18),new Vector3(-26,0,-18),new Vector3(-46,2,-46)};
                var names=new[]{"west-ground","south-foot","foot-close","east-ground","west-pan","metal-platform"};
                var offset=new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);
                void View(Vector3 focus,float size){camera.orthographicSize=size;camera.transform.position=focus+offset;camera.transform.LookAt(focus);}
                for(int view=0;view<focuses.Length;view++)
                {
                    View(focuses[view],(float)(view==2?profile.CameraMinZoom:profile.CameraOrthoSize));for(int frame=0;frame<3;frame++)yield return null;
                    for(int mode=0;mode<3;mode++){Mode(mode);Capture((mode==0?"before":mode==1?"crisp":"full-detail")+"-"+names[view]);}
                }
                View(focuses[0],(float)profile.CameraOrthoSize);
                foreach(var r in renderers)r.enabled=Role(r)==1;
                Mode(0);var original=Capture();Mode(1);var quiet=Capture();
                float L(Color32 c)=>(c.r+c.g+c.b)/3f;
                double noiseBefore=0,noiseAfter=0;int samples=0;
                for(int y=2;y<998;y+=2)for(int x=2;x<1598;x+=2)
                {
                    int i=y*1600+x;var offsets=new[]{0,-1,1,-1600,1600};
                    if(offsets.Any(o=>L(original[i+o])<50||L(quiet[i+o])<50))continue;
                    double N(Color32[] a)=>(Math.Abs(L(a[i-1])-2*L(a[i])+L(a[i+1]))+Math.Abs(L(a[i-1600])-2*L(a[i])+L(a[i+1600])))/(L(a[i])+1);
                    noiseBefore+=N(original);noiseAfter+=N(quiet);samples++;
                }
                Assert.Greater(samples,10000,"Measure actual ground pixels, excluding background/edges");
                Assert.Less(noiseAfter,noiseBefore*.8,"Rendered fine contrast must fall by at least 20%, normalised for brightness");
                Mode(1,false);var noFoot=Capture();Mode(1);var foot=Capture();int contactPixels=Enumerable.Range(0,foot.Length).Count(i=>!foot[i].Equals(noFoot[i]));
                Assert.Greater(contactPixels,100,"The contact/dust finish must actually render near cliff feet");
                foreach(var r in renderers)r.enabled=Role(r)!=1&&Role(r)!=5;
                Mode(0);var unchanged=Capture();Mode(1);Assert.IsTrue(unchanged.SequenceEqual(Capture()),"Rock, steel, concrete and existing props must remain byte-identical");
                foreach(var r in renderers)r.enabled=true;
                world.InspectionHide();Mode(0);var hidden=Capture();Mode(1);Assert.IsTrue(hidden.SequenceEqual(Capture("hidden-fog")),"Fully hidden fog output must stay identical");
                File.WriteAllText(Path.Combine(output,"inspection.txt"),$"{world.SurfaceRevision}; production camera offset={offset}, normal={profile.CameraOrthoSize}, close={profile.CameraMinZoom}; 1600x1000, terrain-only, no simulation seed, revealed fog and diagnostic lighting. Ground high-frequency contrast ratio={noiseAfter/noiseBefore}, samples={samples}; foot contact/dust changed pixels={contactPixels}. Non-ground and hidden-fog byte equality PASS. Before=feature disabled, crisp retention=.8/1/.3, full-detail retention=1/1/.3; both keep the .65m contact. Geometry and existing scree unchanged. Editor renders, not human/Player acceptance.\n");
                var imports=new List<string>();foreach(var suffix in new[]{"","-b","-c"}){var t=Resources.Load<Texture2D>("Environment/NaturalFrontier/Earth"+suffix);imports.Add($"Earth{suffix}: {t.width}x{t.height}, {t.format}, mips={t.mipmapCount}, {t.wrapMode}, {t.filterMode}, aniso={t.anisoLevel}");}File.WriteAllLines(Path.Combine(output,"earth-imports.txt"),imports);
                TestContext.WriteLine($"GROUND_CONTRAST ratio={noiseAfter/noiseBefore} samples={samples} footPixels={contactPixels}");
            }
            finally{RenderSettings.ambientLight=ambient;UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(pixels);target.Release();UnityEngine.Object.Destroy(target);}
        }
    }
}
