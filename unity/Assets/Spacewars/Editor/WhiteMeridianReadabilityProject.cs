using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Spacewars.Editor
{
    public static class WhiteMeridianReadabilityProject
    {
        private sealed class Solid {public Bounds bounds;public Vector3[] vertices;public int[] triangles;}
        private static bool Hits(Vector3 origin,Vector3 direction,float maximum,List<Solid> solids){
            var ray=new Ray(origin,direction);
            foreach(var solid in solids){
                if(!solid.bounds.IntersectRay(ray,out float distance)||distance>maximum)continue;
                for(int i=0;i<solid.triangles.Length;i+=3){
                    var a=solid.vertices[solid.triangles[i]];var b=solid.vertices[solid.triangles[i+1]];var c=solid.vertices[solid.triangles[i+2]];
                    var ab=b-a;var ac=c-a;var cross=Vector3.Cross(direction,ac);float det=Vector3.Dot(ab,cross);if(Mathf.Abs(det)<1e-7f)continue;
                    float inverse=1/det;var delta=origin-a;float u=Vector3.Dot(delta,cross)*inverse;if(u<0||u>1)continue;
                    var q=Vector3.Cross(delta,ab);float v=Vector3.Dot(direction,q)*inverse;if(v<0||u+v>1)continue;
                    float t=Vector3.Dot(ac,q)*inverse;if(t>.02f&&t<maximum)return true;
                }
            }
            return false;
        }
        private static Texture2D Read(RenderTexture rt){var old=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();RenderTexture.active=old;return t;}
        private static float Luma(Color c)=>c.r*.2126f+c.g*.7152f+c.b*.0722f;
        public static void Capture(){
            string output=Environment.GetEnvironmentVariable("WHITE_MERIDIAN_EVIDENCE");if(string.IsNullOrEmpty(output))throw new ArgumentException("WHITE_MERIDIAN_EVIDENCE required");Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var host=new GameObject("Meridian readability production scene");
            var map=new WhiteMeridianMap(JsonUtility.FromJson<WhiteMeridianProfileData>(Resources.Load<TextAsset>("WhiteMeridianProfile").text));
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);var set=WhiteMeridianLightingVariants.Load();
            var sun=new GameObject("Readability sun").AddComponent<Light>();
            var camera=new GameObject("Readability gameplay-angle camera").AddComponent<Camera>();camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=600;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            var coverage=camera.gameObject.AddComponent<VisibleShadowCoverage>();
            var probe=new RenderTexture(160,160,24){antiAliasing=4};probe.Create();
            // 160 px over 3.2m = 50 px/m: equivalent to a 960px-high default view
            // at orthographic size 9.6. Tiles crop the world; models are not enlarged.
            var atlas=new RenderTexture(1280,960,24){antiAliasing=4};atlas.Create();
            var report=new List<string>{"variant,condition,kind,colour,changedModelPixels,meanRGBDifference,groundShadowDelta"};
            using(var world=new PlayableWorld(host.transform,profile)){
                world.Fog.ShowPublic();
                var solids=host.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("Gypsum ridge")||f.name.StartsWith("Wreck ")).Select(f=>new Solid{bounds=f.GetComponent<Renderer>().bounds,vertices=f.sharedMesh.vertices.Select(f.transform.TransformPoint).ToArray(),triangles=f.sharedMesh.triangles}).ToList();
                var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),1);
                var kinds=new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval};
                var units=kinds.Select((k,i)=>world.Tank(9000+i,true,k)).ToArray();foreach(var unit in units){unit.Root.gameObject.SetActive(false);unit.Health.parent.gameObject.SetActive(false);}
                var offset=new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);
                var records=new List<string>();
                foreach(var variant in set.variants){
                    variant.lighting.ApplyScene(sun);coverage.Configure(host.transform,variant.lighting);world.ApplyGraphics(ActorMaterialProfile.Load(),variant.lighting);
                    camera.targetTexture=probe;camera.rect=new Rect(0,0,1,1);camera.aspect=1;camera.orthographicSize=1.6f;
                    void Aim(Vector3 point){camera.transform.position=point+offset;camera.transform.LookAt(point);}
                    float ShadowDelta(Vector3 point){
                        Aim(point);float strength=sun.shadowStrength;camera.Render();var shaded=Read(probe);sun.shadowStrength=0;camera.Render();var clear=Read(probe);sun.shadowStrength=strength;
                        float value=0;for(int y=76;y<84;y++)for(int x=76;x<84;x++)value+=Luma(clear.GetPixel(x,y))-Luma(shaded.GetPixel(x,y));
                        UnityEngine.Object.DestroyImmediate(shaded);UnityEngine.Object.DestroyImmediate(clear);return value/64;
                    }
                    Vector3 Find(bool shadow,out float delta){
                        var toLight=-sun.transform.forward;int blocked=0,visible=0;float maxDelta=0;
                        for(float z=-66;z<=66;z+=.75f)for(float x=-74;x<=74;x+=.75f){
                            var point=new NavPoint(x,z);double radius=kinds.Max(k=>PlayableUnitRules.Radius(profile,k));if(!map.SupportsFootprint(point,radius)||!geometry.IsFree(point,radius))continue;
                            var ground=new Vector3(x,(float)map.SurfaceHeight(point)+.03f,z);
                            if(Hits(ground,toLight,200,solids)!=shadow)continue;
                            if(shadow&&variant.id!="soft-daylight-v1"&&!Hits(ground+Vector3.up*.8f,toLight,200,solids))continue;blocked++;if(Hits(ground,offset.normalized,offset.magnitude,solids))continue;visible++;
                            float difference=ShadowDelta(ground);maxDelta=Mathf.Max(maxDelta,difference);
                            if(shadow?difference>.025f:Mathf.Abs(difference)<.005f){delta=difference;return ground;}
                        }
                        throw new InvalidOperationException("No verified visible "+(shadow?"shadow":"sun")+" anchor for "+variant.id+" cpuMatched="+blocked+" cameraVisible="+visible+" maxDelta="+maxDelta+" meshes="+solids.Count+" offset="+offset);
                    }
                    var anchors=new[]{Find(false,out float sunDelta),Find(true,out float shadeDelta)};var deltas=new[]{sunDelta,shadeDelta};
                    records.Add(variant.id+" sun="+anchors[0]+" shadow="+anchors[1]+" shadowLumaDelta="+shadeDelta);
                    for(int condition=0;condition<2;condition++)for(int kind=0;kind<3;kind++){
                        var point=anchors[condition];Aim(point);camera.targetTexture=probe;camera.rect=new Rect(0,0,1,1);camera.Render();var background=Read(probe);var bg=background.GetPixels32();
                        for(int colour=0;colour<8;colour++){
                            var unit=units[kind];unit.Root.position=point;unit.Root.rotation=Quaternion.Euler(0,25,0);ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[colour],out var paint);PlayableWorld.PaintOwner(unit,paint);unit.Root.gameObject.SetActive(true);
                            camera.targetTexture=probe;camera.rect=new Rect(0,0,1,1);camera.Render();var model=Read(probe);var px=model.GetPixels32();int changed=0;double total=0;
                            for(int i=0;i<px.Length;i++){int d=Math.Abs(px[i].r-bg[i].r)+Math.Abs(px[i].g-bg[i].g)+Math.Abs(px[i].b-bg[i].b);if(d>30){changed++;total+=d/3d;}}
                            report.Add(variant.id+","+(condition==0?"sun":"shadow")+","+kinds[kind]+","+NativeLobbyConfiguration.ColorNames[colour]+","+changed+","+(changed==0?0:total/changed).ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+","+deltas[condition].ToString("F4",System.Globalization.CultureInfo.InvariantCulture));
                            if(changed<30)throw new InvalidOperationException("Production model indistinguishable in render: "+variant.id+" "+condition+" "+kinds[kind]+" "+colour);
                            camera.targetTexture=atlas;camera.rect=new Rect(colour/8f,1-(condition*3+kind+1)/6f,1/8f,1/6f);camera.Render();
                            unit.Root.gameObject.SetActive(false);UnityEngine.Object.DestroyImmediate(model);
                        }
                        UnityEngine.Object.DestroyImmediate(background);
                    }
                    var board=Read(atlas);File.WriteAllBytes(Path.Combine(output,variant.id+"-readability.png"),board.EncodeToPNG());UnityEngine.Object.DestroyImmediate(board);
                }
                File.WriteAllLines(Path.Combine(output,"readability.csv"),report);File.WriteAllLines(Path.Combine(output,"anchors.txt"),records);
            }
            camera.targetTexture=null;probe.Release();atlas.Release();UnityEngine.Object.DestroyImmediate(probe);UnityEngine.Object.DestroyImmediate(atlas);UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(sun.gameObject);UnityEngine.Object.DestroyImmediate(host);
            Debug.Log("WHITE_MERIDIAN_READABILITY_CAPTURE_PASS 4 variants x 3 production unit kinds x 8 palette colours x sun/shadow = 192 samples; image differences are diagnostic, not human acceptance");
        }
    }
}
