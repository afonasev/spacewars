#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Spacewars.Presentation
{
    // Fixed graphical control only. Frozen seed7 genesis never advances the match.
    public sealed class TankDistantEvidence:MonoBehaviour
    {
        string output;GameObject scene;PlayableWorld world;Camera cameraView;RenderTexture target;
        NativeFrameTimingProbe probe;float started;bool done;
        readonly List<string> rows=new List<string>();
        void Start(){started=Time.realtimeSinceStartup;output=Environment.GetEnvironmentVariable("SPACEWARS_TANK_LOD_OUTPUT");if(string.IsNullOrEmpty(output)){Application.Quit(1);return;}Directory.CreateDirectory(output);StartCoroutine(Guard());}
        void Update(){if(!done&&Time.realtimeSinceStartup-started>90)Fail("90s fixed graphical control wall guard");}
        void Fail(string text){done=true;File.WriteAllText(Path.Combine(output,"FAIL.txt"),text);Debug.LogError(text);Application.Quit(1);}
        IEnumerator Guard(){var run=Run();while(true){object next;bool more;try{more=run.MoveNext();next=more?run.Current:null;}catch(Exception e){Fail(e.ToString());yield break;}if(!more)yield break;yield return next;}}
        void OnGUI(){if(target)GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),target,ScaleMode.ScaleToFit,false);}
        void Capture(string name)
        {
            cameraView.Render();var previous=RenderTexture.active;RenderTexture.active=target;
            var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try{pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());}
            finally{RenderTexture.active=previous;Object.Destroy(pixels);}
        }
        IEnumerator Run()
        {
            QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;Screen.SetResolution(1024,768,false);
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var setup=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
            PlayableSnapshot view;using(var runtime=PlayableRuntime.CreateEightPlayerPerformanceMatch(profile,1,setup,startPaused:true))view=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview;
            if(view.Tick!=0||view.Entities.Count!=400)throw new InvalidOperationException("Frozen400 genesis binding");
            scene=new GameObject("Fixed tank LOD presentation");world=new PlayableWorld(scene.transform,profile,staged:true);yield return world.Preparation;world.Fog.ShowPublic();world.RenderSites(view);
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-tankDistantLodCleanupOnly")>=0){
                var actor=world.Tank(view.Entities.First(e=>e.Kind==PlayableEntityKind.Tank).Id,true);
                actor.TankLod.SelectScreenRatio(0,TankMechanismProfile.Load());yield return null;
                DisposeScene();done=true;File.WriteAllText(Path.Combine(output,"cleanup.txt"),"Owned world/pads and LOD disposed before engine scene teardown; no graphical benchmark or match advancement.\n");
                Debug.Log("TANK_DISTANT_CLEANUP_PASS");Application.Quit(0);yield break;
            }
            var light=new GameObject("Fixed game lighting").AddComponent<Light>();light.transform.SetParent(scene.transform);var lighting=IndustrialLightingProfile.Load();lighting.ApplyScene(light);
            cameraView=new GameObject("Fixed game camera").AddComponent<Camera>();cameraView.transform.SetParent(scene.transform);cameraView.orthographic=true;cameraView.nearClipPlane=.1f;cameraView.farClipPlane=700;cameraView.clearFlags=CameraClearFlags.SolidColor;cameraView.backgroundColor=new Color(.13f,.16f,.17f);
            cameraView.gameObject.AddComponent<VisibleShadowCoverage>().Configure(scene.transform,lighting);
            cameraView.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());cameraView.gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());cameraView.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());
            target=new RenderTexture(1024,768,24){antiAliasing=4};target.Create();cameraView.targetTexture=target;
            foreach(var entity in view.Entities){var actor=world.Tank(entity.Id,true,entity.Kind);actor.Root.position=world.Point(entity.Position);actor.Hull.localRotation=Quaternion.Euler(0,90-(float)entity.HullHeading*Mathf.Rad2Deg,0);actor.Turret.localRotation=Quaternion.Euler(0,(float)(entity.HullHeading-entity.TurretHeading)*Mathf.Rad2Deg,0);PlayableWorld.PaintOwner(actor,Color.HSVToRGB((int)entity.Owner/8f,.6f,.8f));}
            var tanks=world.Actors.Values.Where(a=>a.TankLod).ToArray();if(tanks.Length!=392)throw new InvalidOperationException("Expected392 base tanks and8 explorers");
            var focus=tanks[0];int focusId=world.Actors.Single(p=>p.Value==focus).Key;foreach(var a in world.Actors.Values)a.Root.gameObject.SetActive(a==focus);
            var center=focus.Hull.TransformPoint(focus.TankLod.ReferenceBounds.center);
            cameraView.transform.position=center+new Vector3(8,11,-13);cameraView.transform.LookAt(center);
            foreach(var scale in new[]{("near",2.3f),("distant",22f)}){
                cameraView.orthographicSize=scale.Item2;
                focus.TankLod.SelectScreenRatio(float.PositiveInfinity,TankMechanismProfile.Load());
                for(int i=0;i<4;i++)yield return null;Capture(scale.Item1+"-original");
                focus.TankLod.Select(cameraView);
                if(focus.TankLod.IsDistant!=(scale.Item1=="distant"))throw new InvalidOperationException("Automatic detail threshold "+scale.Item1);
                for(int i=0;i<4;i++)yield return null;Capture(scale.Item1+"-automatic");
                File.WriteAllText(Path.Combine(output,scale.Item1+"-selection.txt"),"automaticDistant="+focus.TankLod.IsDistant+" ortho="+scale.Item2+" referenceBounds="+focus.TankLod.ReferenceBounds+"\n");
            }
            // One fixed observed-mechanism pose, shared by both visual passes.
            // It exercises the actual presentation rig, never a domain command/tick.
            PlayableSnapshot PoseFrame(long tick,bool shot)=>new PlayableSnapshot(profile.ProfileId,profile.Revision,1,7,tick+1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,view.Geometry,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sounds:shot?new[]{new PlayableSoundEvent(1,tick,PlayableSoundKind.Shot,new NavPoint(0,0),PlayableEntityKind.Tank,unitId:focusId)}:Array.Empty<PlayableSoundEvent>());
            focus.TankMechanism.Drive(new PlayableDisplayPose(new NavPoint(0,0),0,0),PoseFrame(0,false),focusId);
            focus.TankMechanism.Drive(new PlayableDisplayPose(new NavPoint(.3,.2),.4,.2),PoseFrame(1,true),focusId);
            focus.Turret.localRotation=Quaternion.Euler(0,25,0);
            foreach(var scale in new[]{("near",2.3f),("distant",22f)}){
                cameraView.orthographicSize=scale.Item2;
                focus.TankLod.SelectScreenRatio(float.PositiveInfinity,TankMechanismProfile.Load());
                for(int i=0;i<4;i++)yield return null;Capture(scale.Item1+"-animated-original");
                focus.TankLod.Select(cameraView);
                for(int i=0;i<4;i++)yield return null;Capture(scale.Item1+"-animated-automatic");
            }
            foreach(var a in world.Actors.Values)a.Root.gameObject.SetActive(true);
            cameraView.orthographicSize=144;cameraView.transform.position=new Vector3(0,230,-270);cameraView.transform.LookAt(Vector3.zero);
            yield return null;probe=new NativeFrameTimingProbe(output);
            rows.Add("pass,frame,wall_ms,gpu_stamp,gpu_ms,cpu_ms,main_ns,render_ns,triangles,draw_calls,batches,setpass,gc_bytes");
            foreach(var pass in new[]{"A1-full","B-distant","A2-full"}){
                foreach(var a in tanks){if(pass=="B-distant")a.TankLod.Select(cameraView);else a.TankLod.SelectScreenRatio(float.PositiveInfinity,TankMechanismProfile.Load());}
                if(tanks.Any(a=>a.TankLod.IsDistant!=(pass=="B-distant")))throw new InvalidOperationException("Fixed400 selector");
                for(int i=0;i<20;i++){yield return null;var warm=new NativeRouteFrameSample();probe.Read(ref warm);}
                for(int i=0;i<90;i++){
                    yield return null;var sample=new NativeRouteFrameSample();probe.Read(ref sample);
                    rows.Add(string.Format(CultureInfo.InvariantCulture,"{0},{1},{2:R},{3},{4:R},{5:R},{6},{7},{8},{9},{10},{11},{12}",pass,i,sample.WallFrameMs,sample.TimingStamp,sample.GpuMs,sample.CpuFrameMs,sample.MainThreadNs,sample.RenderThreadNs,sample.Triangles,sample.DrawCalls,sample.Batches,sample.SetPass,sample.GcFrameBytes));
                }
                Capture(pass);
            }
            File.WriteAllLines(Path.Combine(output,"fixed-graphical-aba.csv"),rows);
            File.WriteAllText(Path.Combine(output,"scope.txt"),"Fixed graphical A/B/A: seed7 paused400 genesis,392 tanks+8 explorers, authority tick0 throughout; no match simulation, movement/AI qualification or whole-game FPS claim. One fixed observed-mechanism presentation pose exercises track/rotor/recoil/turret/muzzle, not an authority tick. Shared original rig/materials,1024x768 MSAA4 render target,orthographic144,90 frames/pass after20warmup,vsync0. Near2.3 and distant22 captures in same camera pose. Numeric profile entry=.04 exit=.05. Upgraded geometry retains full source.\n");
            DisposeScene();done=true;Debug.Log("TANK_DISTANT_GRAPHICAL_PASS");Application.Quit(0);
        }
        void DisposeScene()
        {
            probe?.Dispose();probe=null;
            // Explicitly dispose while pad objects are still alive. Engine shutdown
            // may destroy children before this behaviour's OnDestroy callback.
            var owned=world;world=null;if(owned!=null&&owned.GraphicsRoot)owned.Dispose();
            if(cameraView)cameraView.targetTexture=null;
            if(scene)Object.Destroy(scene);scene=null;cameraView=null;
            if(target){target.Release();Object.Destroy(target);}target=null;
        }
        void OnDestroy()=>DisposeScene();
    }
}
#endif
