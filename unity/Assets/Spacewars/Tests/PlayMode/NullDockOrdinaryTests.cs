using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
public sealed class NullDockOrdinaryTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private GameObject host;private PlayableBootstrap app;private PlayableRuntime runtime;private StreamWriter log;private string output;
    private long lastReceipt;private long sequence;private double began,lastLog;private int factory,tank;private bool queued;private readonly Dictionary<int,int> health=new Dictionary<int,int>();private bool combat;
    private System.Threading.Tasks.Task<byte[]> diagnosticCapture;private bool captureWritten;private double pendingFortSince=-1;
    private T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Flags).GetValue(app);
    private void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(app,null);
    private PlayableSnapshot View=>runtime.ParticipantView("player-1");
    private void Observe(){
        Assert.False(runtime.IsStopped,runtime.Latest?.Failure);Assert.Less(Time.realtimeSinceStartupAsDouble-began,600,"overall witness horizon");
        var v=View;
        // Opt-in diagnostic only: the original witness commands, seed, horizons and assertions are unchanged.
        if(Environment.GetEnvironmentVariable("NULL_DOCK_CAPTURE_PENDING")=="1"){
            var fort=v.Buildings.FirstOrDefault(b=>b.Owner==v.Owner&&b.Kind==PlayableBuildingKind.Outpost&&b.Phase==ConstructionPhase.Pending);
            if(fort!=null&&pendingFortSince<0)pendingFortSince=Time.realtimeSinceStartupAsDouble;
            if(fort!=null&&diagnosticCapture==null&&Time.realtimeSinceStartupAsDouble-pendingFortSince>=10){log.WriteLine("CAPTURE pending fort id="+fort.Id+" reason="+fort.BlockedReason);diagnosticCapture=runtime.RequestCaptureBytes();}
            if(diagnosticCapture!=null&&diagnosticCapture.IsCompleted&&!captureWritten){captureWritten=true;if(diagnosticCapture.Status==System.Threading.Tasks.TaskStatus.RanToCompletion){File.WriteAllBytes(Path.Combine(output,"pending-fort.world"),diagnosticCapture.Result);log.WriteLine("CAPTURE saved pending-fort.world");}else log.WriteLine("CAPTURE failed "+diagnosticCapture.Exception);}
        }
        foreach(var receipt in runtime.ReceiptsAfter(lastReceipt)){log.WriteLine("RECEIPT "+receipt.Sequence+" "+receipt.Status+" "+receipt.Message);lastReceipt=Math.Max(lastReceipt,receipt.Sequence);}
        foreach(var e in v.Entities){if(health.TryGetValue(e.Id,out int before)&&e.Health<before)combat=true;health[e.Id]=e.Health;}
        if(v.Projectiles.Count>0)combat=true;
        var f=v.Buildings.FirstOrDefault(b=>b.Owner==v.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready);
        if(f!=null&&!queued){factory=f.Id;Send(PlayableCommandKind.QueueTank,new[]{factory});queued=true;}
        var t=v.Entities.FirstOrDefault(e=>e.Owner==v.Owner&&e.Kind==PlayableEntityKind.Tank);if(t!=null&&tank==0)tank=t.Id;
        if(Time.realtimeSinceStartupAsDouble-lastLog>=1){lastLog=Time.realtimeSinceStartupAsDouble;log.WriteLine("t="+(lastLog-began).ToString("F2")+" tick="+v.Tick+" paused="+v.Paused+" credits="+v.Credits+" buildings="+string.Join(";",v.Buildings.Select(b=>b.Id+":"+b.Kind+":"+b.Phase+":"+b.Progress+":"+b.BlockedReason))+" units="+string.Join(";",v.Entities.Select(e=>e.Id+":"+e.Owner+":"+e.Kind+":"+e.Position.X.ToString("F2")+","+e.Position.Z.ToString("F2")+":"+e.NavigationOutcome))+" combat="+combat);log.Flush();}
    }
    private void Send(PlayableCommandKind kind,int[] ids,NavPoint target=default(NavPoint),int site=0,int slot=0,PlayableBuildingKind building=PlayableBuildingKind.Headquarters,int parent=0){
        var command=new PlayableCommand(runtime.Generation,++sequence,"player-1",kind,ids,target,siteId:site,slotId:slot,buildingKind:building,parentId:parent).AsHuman();
        Assert.True(runtime.TrySubmit(command).Accepted,"submit "+kind);log.WriteLine("COMMAND "+sequence+" "+kind+" "+target.X+","+target.Z+" site="+site+" slot="+slot);log.Flush();
    }
    private IEnumerator Wait(Func<bool> condition,double seconds,string stage){double start=Time.realtimeSinceStartupAsDouble;while(!condition()&&Time.realtimeSinceStartupAsDouble-start<seconds){Observe();yield return null;}Observe();Assert.True(condition(),stage+" timeout");log.WriteLine("PASS "+stage);log.Flush();}
    private IEnumerator Move(int id,NavPoint target,string stage){var before=View.Entities.Single(e=>e.Id==id).Position;Send(PlayableCommandKind.Move,new[]{id},target);yield return Wait(()=>View.Entities.Any(e=>e.Id==id&&Distance(e.Position,before)>.25),60,stage+" first motion");yield return Wait(()=>View.Entities.Any(e=>e.Id==id&&Distance(e.Position,target)<1.4),120,stage+" arrival");}
    private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
    private void Shot(string name,Vector3 target,float size,Vector3 offset,bool reveal){
        var camera=Get<Camera>("cameraView");var old=camera.transform.position;var rotation=camera.transform.rotation;float zoom=camera.orthographicSize;float far=camera.farClipPlane;camera.farClipPlane=800;var previous=camera.targetTexture;var active=RenderTexture.active;
        var fogRenderers=host.GetComponentsInChildren<Renderer>(true);var materials=fogRenderers.SelectMany(r=>r.sharedMaterials).Where(m=>m&&m.HasProperty("_FogMask")).Distinct().ToArray();var fogTextures=materials.Select(m=>m.GetTexture("_FogMask")).ToArray();
        if(reveal)Get<PlayableWorld>("world").InspectionReveal();camera.transform.position=target+offset;camera.transform.LookAt(target);camera.orthographicSize=size;
        var rt=new RenderTexture(1600,1200,24);rt.Create();camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var png=new Texture2D(1600,1200,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,1600,1200),0,0);png.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),png.EncodeToPNG());
        for(int i=0;i<materials.Length;i++)materials[i].SetTexture("_FogMask",fogTextures[i]);
        camera.targetTexture=previous;RenderTexture.active=active;camera.transform.SetPositionAndRotation(old,rotation);camera.orthographicSize=zoom;camera.farClipPlane=far;UnityEngine.Object.Destroy(png);UnityEngine.Object.Destroy(rt);
    }
    [UnityTest] public IEnumerator LobbyToPocketRampCaptureProductionAndAiProgress(){
        output=Environment.GetEnvironmentVariable("NULL_DOCK_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);log=new StreamWriter(Path.Combine(output,"ordinary.txt"));
        bool keyboardAdded=Keyboard.current==null,mouseAdded=Mouse.current==null;var keyboard=keyboardAdded?InputSystem.AddDevice<Keyboard>():Keyboard.current;var mouse=mouseAdded?InputSystem.AddDevice<Mouse>():Mouse.current;
        try{
            Assert.Contains("-noaudio",Environment.GetCommandLineArgs());Assert.Contains("-muted",Environment.GetCommandLineArgs());
            host=new GameObject("Null Dock ordinary lobby witness");app=host.AddComponent<PlayableBootstrap>();double timeout=Time.realtimeSinceStartupAsDouble+120;
            while(Get<VisualElement>("lobbyScreen")==null&&Time.realtimeSinceStartupAsDouble<timeout)yield return null;
            Assert.NotNull(Get<VisualElement>("lobbyScreen"),"lobby startup");Call("ShowLobby");
            var lobby=Get<VisualElement>("lobbyScreen");lobby.Q<DropdownField>("lobby-map-choice").value=NullDockMap.DisplayName;
            var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.HasExplicitSeed=true;setup.ExplicitSeed=26102026;
            Assert.AreEqual(2,setup.Participants.Count);CollectionAssert.AreEqual(new[]{1,2},setup.Participants.Select(p=>p.Team).ToArray());
            log.WriteLine("LOBBY map="+lobby.Q<DropdownField>("lobby-map-choice").value+" seats=2 duel seed=26102026");
            Call("LaunchLobbyMatch");timeout=Time.realtimeSinceStartupAsDouble+120;
            while((Get<bool>("inLobby")||Get<bool>("preparing"))&&Time.realtimeSinceStartupAsDouble<timeout)yield return null;
            Assert.False(Get<bool>("inLobby"));Assert.False(Get<bool>("preparing"));runtime=Get<PlayableRuntime>("runtime");Assert.NotNull(runtime);began=Time.realtimeSinceStartupAsDouble;
            // App Update owns UnityHostRouteService.Service(runtime). No synchronous Pump,
            // direct authority mutation, time acceleration or injected unit state is used.
            var p=Get<PlayableProfile>("profile");Assert.IsInstanceOf<NullDockMap>(p.AuthoredMap);sequence=runtime.HumanSequenceWatermark;
            log.WriteLine("PROFILE "+p.ProfileId+"@"+p.Revision+" map="+p.AuthoredMap.Id+"@"+p.AuthoredMap.Revision);
            Assert.AreEqual(2,View.Participants.Count);var hq=View.Buildings.Single(b=>b.Owner==View.Owner&&b.Kind==PlayableBuildingKind.Headquarters);int scout=View.Entities.Single(e=>e.Owner==View.Owner).Id;
            var map=(NullDockMap)p.AuthoredMap;bool left=hq.SiteId==1;
            NavPoint Side(double x,double y)=>left?map.Point(x,y):map.Point(192-x,160-y);
            int fortId=left?4:3;var fort=map.Sites(p).Single(site=>site.Id==fortId);
            Shot("overview",Vector3.zero,105,new Vector3(0,180,-130),true);
            Shot("topology",Vector3.zero,100,new Vector3(0,220,0),true);
            Shot("home-pocket",new Vector3((float)hq.Position.X,2,(float)hq.Position.Z),38,new Vector3(0,65,-45),true);
            Send(PlayableCommandKind.BuildAt,Array.Empty<int>(),site:hq.SiteId,slot:1,building:PlayableBuildingKind.Factory,parent:hq.Id);
            yield return Move(scout,Side(62,80),"wide central home ramp descent");
            yield return Move(scout,Side(30,118),"home mine approach through R2 or R4");
            yield return Move(scout,fort.Position,"corner pocket and outpost ramp");
            yield return Wait(()=>View.Sites.Any(site=>site.Site.Id==fortId&&site.Claimant==View.Owner&&site.Progress>=1&&!site.Contested),30,"fort capture");
            yield return Wait(()=>View.Credits>=p.OutpostCreditCost,120,"outpost funding from ordinary income");
            Send(PlayableCommandKind.BuildAt,Array.Empty<int>(),site:fortId,building:PlayableBuildingKind.Outpost);
            yield return Wait(()=>View.Buildings.Any(b=>b.SiteId==fortId&&b.Kind==PlayableBuildingKind.Outpost&&b.Phase==ConstructionPhase.Ready),75,"outpost construction");
            yield return Move(scout,Side(110,134),"outpost inner ramp descent");
            yield return Wait(()=>factory>0&&tank>0,90,"factory production and safe spawn");
            yield return Move(tank,Side(62,80),"tank wide home ramp descent");
            yield return Wait(()=>View.Participants.Where(o=>o.Id!="player-1").All(o=>{var v=runtime.ParticipantView(o.Id);return v.Buildings.Count(b=>b.Owner==v.Owner)>1;}),120,"native AI economy progresses");
            Shot("ordinary-home",new Vector3((float)hq.Position.X,2,(float)hq.Position.Z),32,new Vector3(0,45,-40),false);
            log.WriteLine("PASS ordinary lobby / physical arrival / ramps / capture / build / production / native AI economy");
        }finally{
            if(runtime!=null){log?.WriteLine("END tick="+runtime.Latest?.Tick+" failure="+runtime.Latest?.Failure);runtime.RequestStop();}
            log?.Dispose();if(host)UnityEngine.Object.Destroy(host);if(keyboardAdded)InputSystem.RemoveDevice(keyboard);if(mouseAdded)InputSystem.RemoveDevice(mouse);
        }
    }
}
