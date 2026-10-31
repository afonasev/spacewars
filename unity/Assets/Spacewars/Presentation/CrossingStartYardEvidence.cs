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
    // Explicit development-only native review. Normal lobby, runtime and threaded
    // route service remain authoritative; this driver submits one human move.
    public sealed class CrossingStartYardEvidence : MonoBehaviour
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        PlayableBootstrap app;PlayableRuntime runtime;string output;StreamWriter log;
        double started,lastLog;bool finished,reveal;
        bool VisualOnly=>Environment.GetCommandLineArgs().Contains("-crossingYardVisualOnly");
        void LateUpdate(){if(reveal&&app)Get<PlayableWorld>("world")?.InspectionReveal();}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Install()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-crossingYardEvidence");
            if(i>=0&&i+1<args.Length){var driver=new GameObject("Crossing yard native review").AddComponent<CrossingStartYardEvidence>();driver.output=args[i+1];}
        }
        T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Flags).GetValue(app);
        void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(app,value);
        void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(app,null);
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        PlayableSnapshot View=>runtime.ParticipantView("player-1");
        IEnumerator Start()
        {
            Directory.CreateDirectory(output);log=new StreamWriter(Path.Combine(output,"ordinary.txt")){AutoFlush=true};started=Time.realtimeSinceStartupAsDouble;
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0)
            {
                bool next=false;object value=null;Exception failure=null;
                try{next=stack.Peek().MoveNext();if(next)value=stack.Peek().Current;}catch(Exception ex){failure=ex;}
                if(failure!=null){Finish("FAIL",failure.ToString());yield break;}
                if(!next){stack.Pop();continue;}
                if(value is IEnumerator nested)stack.Push(nested);else yield return value;
            }
            Finish("PASS",VisualOnly?"Visual capture only: ordinary lobby and real HUD; gameplay witness is not rerun.":"Native lobby, live HUD, moved mine arrival and AI displacement; no full/device/FPS acceptance.");
        }
        void Finish(string result,string message)
        {
            if(finished)return;finished=true;log.WriteLine(result+" "+message);log.Dispose();File.WriteAllText(Path.Combine(output,"result.txt"),result+"\n"+message);Application.Quit(result=="PASS"?0:2);
        }
        void Update(){if(!finished&&started>0&&Time.realtimeSinceStartupAsDouble-started>360)Finish("FAIL","360-second overall review deadline");}
        IEnumerator Wait(Func<bool> condition,double seconds,string label)
        {
            double end=Time.realtimeSinceStartupAsDouble+seconds;
            while(!condition()&&Time.realtimeSinceStartupAsDouble<end)yield return null;
            Require(condition(),label);log.WriteLine("PASS "+label);
        }
        IEnumerator Shot(string name,Vector3 point,float size)
        {
            var p=Get<PlayableProfile>("profile");var camera=Get<Camera>("cameraView");
            camera.transform.position=point+new Vector3((float)p.CameraOffsetX,(float)p.CameraHeight,(float)p.CameraOffsetZ);camera.transform.LookAt(point);camera.orthographicSize=size;
            for(int i=0;i<4;i++)yield return null;
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return null;
            log.WriteLine("FRAME "+name+" tick="+View.Tick+" camera="+size);
        }
        IEnumerator Run()
        {
            Require(Environment.GetCommandLineArgs().Contains("-noaudio")&&Environment.GetCommandLineArgs().Contains("-muted"),"quiet startup");
            yield return Wait(()=>{app=UnityEngine.Object.FindFirstObjectByType<PlayableBootstrap>();return app&&Get<VisualElement>("lobbyScreen")!=null;},120,"ordinary lobby ready");
            yield return Wait(()=>Get<NativeMainMenu>("nativeMainMenu")?.ScreenRoot!=null,10,"native main menu ready");
            Get<NativeMainMenu>("nativeMainMenu").ScreenRoot.Q<Button>("main-play").Focus();Get<NativeMenuNavigation>("menuNavigation").Activate();
            yield return Wait(()=>Get<VisualElement>("lobbyScreen").style.display.value==DisplayStyle.Flex,10,"play button enters lobby");
            var lobby=Get<VisualElement>("lobbyScreen");lobby.Q<DropdownField>("lobby-map-choice").value="Переправа (4)";
            var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.HasExplicitSeed=true;setup.ExplicitSeed=7;
            Set("battleHudUnattended",true);Call("LaunchLobbyMatch");
            yield return Wait(()=>!Get<bool>("inLobby")&&!Get<bool>("preparing"),120,"ordinary match launched");
            runtime=Get<PlayableRuntime>("runtime");Require(runtime!=null,"runtime");var profile=Get<PlayableProfile>("profile");Require(profile.AuthoredMap is ThreeCrossingsMap,"Crossing map");
            yield return Wait(()=>View.Tick>=5,10,"live simulation");
            var home=View.Buildings.Single(b=>b.Owner==View.Owner&&b.Kind==PlayableBuildingKind.Headquarters);
            var scout=View.Entities.Single(e=>e.Owner==View.Owner);var mine=View.Sites.Where(s=>s.Site.Kind==PlayableBuildingKind.Mine).OrderBy(s=>Distance(s.Site.Position,home.Position)).First().Site;
            var origins=View.Participants.Where(p=>p.Id!="player-1").Select(p=>new {p.Id,Position=runtime.ParticipantView(p.Id).Entities.First(e=>e.Owner==runtime.ParticipantView(p.Id).Owner).Position}).ToArray();
            log.WriteLine("seed=7 map="+profile.AuthoredMap.Id+"@"+profile.AuthoredMap.Revision+" home="+home.SiteId+" mine="+mine.Id+" target="+mine.Position.X+","+mine.Position.Z);
            yield return Shot("gameplay-start",new Vector3((float)home.Position.X,0,(float)home.Position.Z),(float)profile.CameraOrthoSize);
            yield return Shot("gameplay-start-close",new Vector3((float)(home.Position.X+mine.Position.X)*.5f,0,(float)home.Position.Z),(float)profile.CameraMinZoom);
            if(VisualOnly)
            {
                reveal=true;
                yield return Shot("gameplay-yard-revealed",new Vector3(-40,0,44),22);
                yield return Shot("gameplay-curb-revealed",new Vector3(-29,0,44),9);
                yield return Shot("gameplay-mine-revealed",new Vector3((float)mine.Position.X,0,(float)mine.Position.Z),8);
                yield return Shot("gameplay-tractor-revealed",new Vector3(-54,0,26),8);
                yield return Shot("gameplay-tractor-normal",new Vector3(-54,0,26),22);
                yield return Shot("gameplay-tractor-far",new Vector3(-54,0,26),44);
                foreach(var kind in CrossingIndustrialFleet.Kinds)
                {
                    var machine=UnityEngine.Object.FindObjectsByType<CrossingSceneryDetail>(FindObjectsSortMode.None).Where(o=>o.name=="Yard — "+kind).OrderBy(o=>Vector3.Dot(o.transform.forward,new Vector3((float)profile.CameraOffsetX,0,(float)profile.CameraOffsetZ).normalized)).Last();
                    var point=machine.transform.position;
                    foreach(var size in new[]{(float)profile.CameraMinZoom,(float)profile.CameraOrthoSize,32f,52f})
                    {
                        yield return Shot("fleet-"+kind+"-zoom"+size,point,size);
                        var lod=machine.GetComponent<ModelDetailLod>();log.WriteLine("FLEET "+kind+" position="+point+" zoom="+size+" LOD="+lod.Level+" triangles="+machine.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3);
                    }
                }
                yield break;
            }
            var target=new NavPoint(mine.Position.X,mine.Position.Z-Math.Sign(mine.Position.Z)*(profile.MineFootprintRadius+profile.Navigation.ArrivalSlotSpacing));
            Require(runtime.TrySubmit(new PlayableCommand(runtime.Generation,runtime.HumanSequenceWatermark+1,"player-1",PlayableCommandKind.Move,new[]{scout.Id},target).AsHuman()).Accepted,"human move accepted");
            long initialTick=View.Tick;double deadline=Time.realtimeSinceStartupAsDouble+110;bool arrived=false,aiMoved=false;
            while(View.Tick-initialTick<1800&&Time.realtimeSinceStartupAsDouble<deadline)
            {
                var v=View;Require(string.IsNullOrEmpty(v.Failure),"runtime failure: "+v.Failure);Require(!v.Paused,"unexpected pause");
                var unit=v.Entities.Single(e=>e.Id==scout.Id);arrived|=unit.NavigationOutcome==NavigationOutcome.Arrived&&Distance(unit.Position,target)<1.4;
                aiMoved|=origins.Any(o=>runtime.ParticipantView(o.Id).Entities.Any(e=>e.Owner==runtime.ParticipantView(o.Id).Owner&&Distance(e.Position,o.Position)>2));
                if(Time.realtimeSinceStartupAsDouble-lastLog>1){lastLog=Time.realtimeSinceStartupAsDouble;log.WriteLine("tick="+v.Tick+" scout="+unit.Position.X+","+unit.Position.Z+" outcome="+unit.NavigationOutcome+" aiMoved="+aiMoved);}
                if(arrived&&aiMoved)break;yield return null;
            }
            yield return Shot("gameplay-mine",new Vector3((float)mine.Position.X,0,(float)mine.Position.Z),(float)profile.CameraMinZoom);
            Require(arrived,"scout arrival at relocated mine within 1800 ticks/110 seconds");Require(aiMoved,"native AI leaves home");
        }
        static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
    }
}
#endif
