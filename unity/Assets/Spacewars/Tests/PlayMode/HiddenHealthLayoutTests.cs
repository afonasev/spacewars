using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Spacewars.Tests.PlayMode
{
    public sealed class HiddenHealthLayoutTests
    {
        static string Output=>Environment.GetEnvironmentVariable("SPACEWARS_HEALTH80_OUTPUT");
        static byte[] VisibleState(PlayableWorld.Actor actor)
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){
                foreach(var t in new[]{actor.Health.parent}.Concat(actor.Health.parent.Cast<Transform>())){
                    writer.Write(t.gameObject.activeSelf);var p=t.localPosition;var q=t.localRotation;var s=t.localScale;
                    writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);writer.Write(q.x);writer.Write(q.y);writer.Write(q.z);writer.Write(q.w);writer.Write(s.x);writer.Write(s.y);writer.Write(s.z);
                }return stream.ToArray();
            }
        }
        static void Layer(PlayableWorld.Actor actor,int layer)
        {
            foreach(var t in actor.Root.GetComponentsInChildren<Transform>(true).Concat(actor.Health.parent.GetComponentsInChildren<Transform>(true)))t.gameObject.layer=layer;
        }
        static byte[] Capture(Camera camera,RenderTexture rt,string path)
        {
            camera.Render();var old=RenderTexture.active;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
            try{RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();var data=image.EncodeToPNG();File.WriteAllBytes(path,data);return data;}
            finally{RenderTexture.active=old;Object.DestroyImmediate(image);}
        }
        static void Census(PlayableWorld.Actor actor,Camera camera)
        {
            var expected=actor.Root.GetComponentsInChildren<Renderer>().Concat(actor.Health.parent.GetComponentsInChildren<Renderer>()).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            var actual=Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&(camera.cullingMask&(1<<r.gameObject.layer))!=0).ToArray();
            CollectionAssert.AreEquivalent(expected,actual,"Capture layer contains only the selected actor and its own health/staged model");
        }
        static PlayableSnapshot Frame(PlayableProfile p,PlayableBuildingSnapshot[] buildings=null)=>new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,30,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),buildings??Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),null,null);
        [UnityTest] public IEnumerator HiddenUpdatesRevealExactCurrentGeometryAndKeepModelLodLive()
        {
            Assert.False(Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,FindObjectsSortMode.None).Any(r=>r.gameObject.layer==31),"Reserved capture layer is unused");
            var restore=NativePreferenceWriter.Capture();var prior=ModelDetailProfile.Current.Copy();float priorTime=Time.timeScale;
            var host=new GameObject("Hidden health80 reveal");var cameraObject=new GameObject("Hidden health80 reveal camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
            var rt=new RenderTexture(1280,800,24);rt.Create();camera.targetTexture=rt;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.05f,.07f);camera.nearClipPlane=.1f;camera.farClipPlane=2000;camera.cullingMask=1<<31;
            var sun=new GameObject("Hidden health80 sun").AddComponent<Light>();sun.transform.SetParent(host.transform);sun.type=LightType.Directional;sun.transform.rotation=Quaternion.Euler(48,-35,0);sun.cullingMask=1<<31;
            var world=new PlayableWorld(host.transform,PlayableProfile.Default,staged:true);var reference=new PlayableWorld(host.transform,PlayableProfile.Default,staged:true);
            var output=Output;Assert.IsNotEmpty(output,"Set owned output directory");Directory.CreateDirectory(output);var results=new List<string>();
            var kinds=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Hawk};
            var actual=new[]{world.Tank(801,true),world.Tank(802,true,PlayableEntityKind.Explorer),world.Tank(803,true,PlayableEntityKind.Hawk),world.Building(804,"Factory",true),world.Building(805,"Refinery",true)};
            var controls=new[]{reference.Tank(1801,true),reference.Tank(1802,true,PlayableEntityKind.Explorer),reference.Tank(1803,true,PlayableEntityKind.Hawk),reference.Building(1804,"Factory",true),reference.Building(1805,"Refinery",true)};
            try{
                ModelDetailProfile.UseSession(ModelDetailProfile.Load());
                world.InspectionReveal();reference.InspectionReveal();
                for(int step=0;step<8;step++){
                    Time.timeScale=step%2==0?0:1;bool perspective=step%2==1;
                    camera.orthographic=!perspective;camera.fieldOfView=53;camera.orthographicSize=144;
                    camera.transform.rotation=Quaternion.Euler(32+step,step*23,step%3*7);camera.transform.position=new Vector3(0,1,0)-camera.transform.forward*320;
                    for(int i=0;i<actual.Length;i++){
                        var a=actual[i];var b=controls[i];var position=new Vector3(i*4,step*.11f,step*.17f);
                        foreach(var actor in new[]{a,b}){actor.Root.position=position;actor.Root.rotation=Quaternion.Euler(step*2,step*31,0);actor.Hull.localRotation=Quaternion.Euler(0,step*19,0);actor.Selection.SetActive(step%3==0);}
                        if(step==2&&i<2){world.UpdateResearchModel(a,new PlayableEntitySnapshot(801+i,PlayableOwner.Player,kinds[i],default,100,false,0,0,0,upgraded:true));reference.UpdateResearchModel(b,new PlayableEntitySnapshot(1801+i,PlayableOwner.Player,kinds[i],default,100,false,0,0,0,upgraded:true));}
                        if(i==3){
                            var building=new PlayableBuildingSnapshot(804,PlayableOwner.Player,PlayableBuildingKind.Factory,default,100,1,0,0,default,launchPhase:step<6?FactoryReleaseRules.Driving:FactoryReleaseRules.Closing,launchTime:step*.1,launchKind:PlayableEntityKind.Tank,launchUpgraded:step>=2);
                            world.UpdateFactoryModel(a,building,Frame(PlayableProfile.Default,new[]{building}));reference.UpdateFactoryModel(b,building,Frame(PlayableProfile.Default,new[]{building}));a.Root.localScale=b.Root.localScale=new Vector3(1,.2f+step*.1f,1);
                        }
                        if(i==4&&step==2){var building=new PlayableBuildingSnapshot(805,PlayableOwner.Player,PlayableBuildingKind.Refinery,default,100,1,0,0,default,refineryUpgraded:true);world.UpdateRefineryModel(a,building,30);reference.UpdateRefineryModel(b,building,30);}
                        Layer(a,30);Layer(b,30);
                        a.DetailLod?.SetDetail(0);b.DetailLod?.SetDetail(0);a.FactoryStagedLod?.SetDetail(0);b.FactoryStagedLod?.SetDetail(0);
                        NativeUserSettings.AlwaysHealth=true;NativeUserSettings.HideDistantHealth=true;
                        PlayableWorld.UpdateHealth(a,camera,.27f,step%3==0);Assert.False(a.Health.parent.gameObject.activeSelf);
                        NativeUserSettings.HideDistantHealth=false;PlayableWorld.UpdateHealth(b,camera,.27f,step%3==0);
                        if(a.DetailLod){Assert.AreEqual(b.DetailLod.Level,a.DetailLod.Level);if(i<2)Assert.AreEqual(2,a.DetailLod.Level,"LOD still selects FAR while health hidden");}
                        if(a.FactoryStaged&&a.FactoryStaged.gameObject.activeSelf)Assert.AreEqual(b.FactoryStagedLod.Level,a.FactoryStagedLod.Level);
                    }
                    yield return null; // original model destruction and skin LOD frame boundary
                    // Reveal via threshold, preference, damage or selection in this call.
                    bool threshold=step%4==0;camera.orthographicSize=threshold?ModelDetailProfile.Current.healthHideZoom-.001f:30;
                    if(perspective)camera.transform.position=new Vector3(0,1,0)-camera.transform.forward*(threshold?20:60);
                    NativeUserSettings.HideDistantHealth=threshold;NativeUserSettings.AlwaysHealth=step%4<2;
                    float ratio=step%4==3?1:.63f;bool selected=step%4==3;
                    for(int i=0;i<actual.Length;i++){
                        PlayableWorld.UpdateHealth(actual[i],camera,ratio,selected);PlayableWorld.UpdateHealth(controls[i],camera,ratio,selected);
                        if(i==2){var e=new PlayableEntitySnapshot(803,PlayableOwner.Player,PlayableEntityKind.Hawk,new NavPoint(i*4,step*.17),100,false,0,.2,.3,altitude:4+step,pitch:step,roll:step*2);world.UpdateAircraft(actual[i],e,Frame(PlayableProfile.Default),selected);reference.UpdateAircraft(controls[i],e,Frame(PlayableProfile.Default),selected);}
                        Assert.True(actual[i].Health.parent.gameObject.activeSelf,"Reveal in same call");CollectionAssert.AreEqual(VisibleState(controls[i]),VisibleState(actual[i]),"Exact same-call reveal step="+step+" actor="+i);results.Add(step+":"+i+":exact");
                    }
                    yield return null;
                    if(step==0||step==2||step==3){
                        int selectedActor=step==3?3:0;var a=actual[selectedActor];var b=controls[selectedActor];
                        // Inspect readable health pixels as well as the boundary-state
                        // comparisons above. The hidden-health control must change pixels.
                        camera.orthographic=true;camera.orthographicSize=selectedActor==3?9.6f:2.135f;
                        camera.transform.position=a.Root.position-camera.transform.forward*40;
                        PlayableWorld.UpdateHealth(a,camera,ratio,selected);PlayableWorld.UpdateHealth(b,camera,ratio,selected);
                        CollectionAssert.AreEqual(VisibleState(b),VisibleState(a));
                        yield return null;
                        Layer(a,31);Census(a,camera);var first=Capture(camera,rt,Path.Combine(output,"reveal-"+step+"-actual.png"));
                        a.Health.parent.gameObject.SetActive(false);var hidden=Capture(camera,rt,Path.Combine(output,"reveal-"+step+"-health-hidden.png"));Assert.False(first.SequenceEqual(hidden),"Visible health must contribute actual pixels");a.Health.parent.gameObject.SetActive(true);
                        Layer(a,30);Layer(b,31);
                        yield return null;Census(b,camera);var second=Capture(camera,rt,Path.Combine(output,"reveal-"+step+"-reference.png"));CollectionAssert.AreEqual(second,first,"Same visible pixels after hidden history");Layer(b,30);
                    }
                }
                File.WriteAllLines(Path.Combine(output,"reveal-exact.txt"),results);
            }finally{Time.timeScale=priorTime;camera.targetTexture=null;world.Dispose();reference.Dispose();Object.DestroyImmediate(host);Object.DestroyImmediate(cameraObject);rt.Release();Object.DestroyImmediate(rt);ModelDetailProfile.UseSession(prior);restore();PlayerPrefs.Save();}
        }
    }
}
