#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    // Opt-in native match/render witness, not combat, AI, device or FPS acceptance.
    public sealed class MapModelDetailEvidence : MonoBehaviour
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        PlayableBootstrap app;string output;double started;bool finished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Install()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-mapModelDetailEvidence");
            if(i>=0&&i+1<args.Length){Application.runInBackground=true;new GameObject("Model detail native witness").AddComponent<MapModelDetailEvidence>().output=args[i+1];}
        }
        T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Flags).GetValue(app);
        void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(app,value);
        void Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(app,args);
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        void Finish(string result,string reason)
        {
            if(finished)return;finished=true;File.WriteAllText(Path.Combine(output,"result.txt"),result+"\n"+reason);Application.Quit(result=="PASS"?0:2);
        }
        void Update(){if(started>0&&!finished&&Time.realtimeSinceStartupAsDouble-started>360)Finish("FAIL","360-second native render witness deadline");}
        IEnumerator Start()
        {
            Directory.CreateDirectory(output);started=Time.realtimeSinceStartupAsDouble;var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0){bool next=false;object value=null;Exception failure=null;try{next=stack.Peek().MoveNext();if(next)value=stack.Peek().Current;}catch(Exception ex){failure=ex;}
                if(failure!=null){Finish("FAIL",failure.ToString());yield break;}if(!next){stack.Pop();continue;}if(value is IEnumerator nested)stack.Push(nested);else yield return value;}
            Finish("PASS","Ordinary lobby/threaded match scenery binding plus targeted paused wreck presentation; not combat birth, AI, device or FPS acceptance.");
        }
        IEnumerator Wait(Func<bool> condition,double seconds,string label)
        {double end=Time.realtimeSinceStartupAsDouble+seconds;while(!condition()&&Time.realtimeSinceStartupAsDouble<end)yield return null;Require(condition(),label);}
        IEnumerator Shot(string name)
        {
            if(Application.isBatchMode){
                yield return null;var camera=Get<Camera>("cameraView");var rt=new RenderTexture(1600,1200,24);rt.Create();var previous=RenderTexture.active;var target=camera.targetTexture;
                try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var frame=new Texture2D(1600,1200,TextureFormat.RGB24,false);try{frame.ReadPixels(new Rect(0,0,1600,1200),0,0);frame.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),frame.EncodeToPNG());}finally{Destroy(frame);}}
                finally{camera.targetTexture=target;RenderTexture.active=previous;rt.Release();Destroy(rt);}
            }else{yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();}
        }
        IEnumerator Run()
        {
            Require(NativeSilentStartup.IsSilent,"quiet startup");
            yield return Wait(()=>{app=UnityEngine.Object.FindFirstObjectByType<PlayableBootstrap>();return app&&Get<NativeMainMenu>("nativeMainMenu")?.ScreenRoot!=null;},120,"native menu");
            Get<NativeMainMenu>("nativeMainMenu").ScreenRoot.Q<Button>("main-play").Focus();Get<NativeMenuNavigation>("menuNavigation").Activate();
            yield return Wait(()=>Get<VisualElement>("lobbyScreen").style.display.value==DisplayStyle.Flex,10,"ordinary lobby");
            Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-map-choice").value=MossHighlandMap.DisplayName;
            var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.HasExplicitSeed=true;setup.ExplicitSeed=7;Set("battleHudUnattended",true);Call("LaunchLobbyMatch");
            yield return Wait(()=>!Get<bool>("inLobby")&&!Get<bool>("preparing"),120,"ordinary match start");var runtime=Get<PlayableRuntime>("runtime");
            yield return Wait(()=>runtime.ParticipantView("player-1")?.Tick>2,20,"threaded match ticks");
            Require(string.IsNullOrEmpty(runtime.ParticipantView("player-1").Failure),"ordinary match failure: "+runtime.ParticipantView("player-1").Failure);
            var world=Get<PlayableWorld>("world");var p=Get<PlayableProfile>("profile");var camera=Get<Camera>("cameraView");Require(p.AuthoredMap is MossHighlandMap,"Moss map");
            var scenery=world.GraphicsRoot.GetComponentsInChildren<ModelDetailLod>(true).Where(l=>l.transform.parent==world.GraphicsRoot).ToArray();Require(scenery.Length>0,"match scenery attached");
            yield return Wait(()=>scenery.Any(l=>l.Level>0),10,"ordinary match camera selected reduced scenery");yield return Shot("match-default");
            runtime.RequestPause(true);Set("paused",true);app.enabled=false;world.InspectionReveal();
            var subject=scenery.First(l=>l.name=="Moss pebble");Require(subject.IsCatalogBound,"generated catalog provenance");var renderers=subject.Renderers;var materials=renderers.Select(r=>r.sharedMaterials).ToArray();var bounds=renderers.Select(r=>r.localBounds).ToArray();
            var center=subject.transform.TransformPoint(subject.ReferenceBounds.center);camera.transform.position=center+new Vector3((float)p.CameraOffsetX,(float)p.CameraHeight,(float)p.CameraOffsetZ);camera.transform.LookAt(center);
            var rows=new List<object>();
            foreach(var step in new[]{(label:"close",ratio:.2f,level:0),(label:"normal",ratio:.08f,level:1),(label:"far",ratio:.03f,level:2),(label:"return",ratio:.08f,level:1)}){
                camera.orthographicSize=1;float size=subject.ProjectedScreenRatio(camera)*2;camera.orthographicSize=size/(2*step.ratio);var previous=scenery.Select(item=>item.Level).ToArray();foreach(var item in scenery)item.SetDetail(0);yield return Shot("scenery-"+step.label+"-source");for(int i=0;i<scenery.Length;i++)scenery[i].SetDetail(previous[i]);world.SelectEnvironmentDetail(camera);Require(subject.Level==step.level,"scenery "+step.label);
                for(int i=0;i<renderers.Length;i++){Require(materials[i].SequenceEqual(renderers[i].sharedMaterials),"material binding");Require(bounds[i]==renderers[i].localBounds,"culling bounds");}
                yield return Shot("scenery-"+step.label);rows.Add(new{kind="scenery",step.label,level=subject.Level,ratio=subject.ProjectedScreenRatio(camera),zoom=camera.orthographicSize});
            }
            // Exercise the existing authoritative-snapshot presentation boundary
            // in this same paused match world. Combat generation is not claimed.
            var wreck=new PlayableWreckSnapshot(900001,PlayableOwner.Player,PlayableEntityKind.Tank,default,.75,.72,0,.1,1,0,0,5,905,965,-1);
            var snapshot=new PlayableSnapshot(p.ProfileId,p.Revision,runtime.Generation,7,1,10,RuntimeStatus.Running,true,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,wrecks:new[]{wreck});
            world.RenderUnitWrecks(snapshot);var wreckModel=world.GraphicsRoot.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Unit wreck 900001");var detail=wreckModel.GetComponent<ModelDetailLod>();Require(detail,"wreck catalog attached");
            center=detail.transform.TransformPoint(detail.ReferenceBounds.center);camera.transform.position=center+new Vector3((float)p.CameraOffsetX,(float)p.CameraHeight,(float)p.CameraOffsetZ);camera.transform.LookAt(center);
            foreach(var step in new[]{(label:"close",ratio:.2f,level:0),(label:"normal",ratio:.08f,level:1),(label:"far",ratio:.03f,level:2),(label:"return",ratio:.08f,level:1)}){
                camera.orthographicSize=1;float size=detail.ProjectedScreenRatio(camera)*2;camera.orthographicSize=size/(2*step.ratio);int previous=detail.Level;detail.SetDetail(0);yield return Shot("wreck-"+step.label+"-source");detail.SetDetail(previous);world.SelectEnvironmentDetail(camera);Require(detail.Level==step.level,"wreck "+step.label);yield return Shot("wreck-"+step.label);
                rows.Add(new{kind="wreck",step.label,level=detail.Level,ratio=detail.ProjectedScreenRatio(camera),zoom=camera.orthographicSize});
            }
            object Costs(ModelDetailLod l)=>new{close=l.OriginalMeshes.Sum(m=>m.triangles.Length/3),normal=l.NormalMeshes.Sum(m=>m.triangles.Length/3),far=l.FarMeshes.Sum(m=>m.triangles.Length/3),renderers=l.Renderers.Length,materials=l.Renderers.Sum(r=>r.sharedMaterials.Length),meshBytes=new[]{l.OriginalMeshes.Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m)),l.NormalMeshes.Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m)),l.FarMeshes.Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m))}};
            File.WriteAllText(Path.Combine(output,"runtime-costs.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{scenery=Costs(subject),wreck=Costs(detail)},Newtonsoft.Json.Formatting.Indented));
            File.WriteAllText(Path.Combine(output,"selection.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{classification="ORDINARY_MATCH_SCENERY_AND_TARGETED_WRECK_RENDER_NOT_COMBAT_OR_FPS",muted=true,scenery=scenery.Length,rows},Newtonsoft.Json.Formatting.Indented));
        }
    }
}
#endif
