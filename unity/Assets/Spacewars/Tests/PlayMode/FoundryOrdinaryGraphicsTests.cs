using System;
using System.Collections;
using System.Collections.Generic;
using Spacewars.Simulation.Ai;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Input;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Tests.PlayMode
{
    public sealed partial class FoundryOrdinaryGraphicsTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private static MaterialPropertyBlock Effective(Renderer renderer,int slot=0)
        {
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);
            if(block.isEmpty)renderer.GetPropertyBlock(block);
            return block;
        }

        [TestCase(false)] [TestCase(true)]
        public void GraphicsRefreshPreservesRendererAndMaterialOverrides(bool materialOverride)
        {
            var host=new GameObject("Material override regression");
            using(var world=new PlayableWorld(host.transform,PlayableProfile.Default))
            try
            {
                var obj=new GameObject("Mixed override scopes");obj.transform.SetParent(world.GraphicsRoot);
                var renderer=obj.AddComponent<MeshRenderer>();
                var material=new Material(Resources.Load<Shader>("TerritoryFog"));
                try
                {
                    renderer.sharedMaterials=new[]{material,material};
                    var general=new MaterialPropertyBlock();general.SetColor("_BaseColor",Color.red);general.SetFloat("_SurfaceRole",6);general.SetVector("_FoundryPadBounds",new Vector4(1,2,3,4));renderer.SetPropertyBlock(general);
                    if(materialOverride){var specific=new MaterialPropertyBlock();specific.SetColor("_BaseColor",Color.blue);specific.SetFloat("_SurfaceRole",8);renderer.SetPropertyBlock(specific,1);}
                    for(int refresh=0;refresh<2;refresh++)
                    {
                        world.ApplyGraphics(ActorMaterialProfile.Load(),IndustrialLightingProfile.Load());
                        var inherited=new MaterialPropertyBlock();renderer.GetPropertyBlock(inherited,0);
                        Assert.True(inherited.isEmpty,"Do not introduce a per-material block that hides renderer overrides");
                        var ground=Effective(renderer);Assert.AreEqual(Color.red,ground.GetColor("_BaseColor"));Assert.AreEqual(6,ground.GetFloat("_SurfaceRole"));Assert.AreEqual(new Vector4(1,2,3,4),ground.GetVector("_FoundryPadBounds"));
                        var second=Effective(renderer,1);Assert.AreEqual(materialOverride?Color.blue:Color.red,second.GetColor("_BaseColor"));Assert.AreEqual(materialOverride?8:6,second.GetFloat("_SurfaceRole"));
                        Assert.AreEqual(IndustrialLightingProfile.Load().concreteHighlight,ground.GetVector("_IndustrialFinish").z);
                        Assert.AreEqual(ground.GetVector("_IndustrialFinish"),second.GetVector("_IndustrialFinish"));
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(material);}
            }
            finally{UnityEngine.Object.DestroyImmediate(host);}
        }

        private static void AssertTerrain(PlayableWorld world)
        {
            var renderers=world.GraphicsRoot.GetComponentsInChildren<MeshRenderer>();
            var ground=renderers.Where(r=>Effective(r).GetFloat("_SurfaceRole")==6||Effective(r).GetFloat("_SurfaceRole")==7).ToArray();
            Assert.IsNotEmpty(ground,"Ordinary match must retain textured ash/gravel roles after binding lighting");
            Assert.True(renderers.Any(r=>Effective(r).GetFloat("_SurfaceRole")==8),"Concrete role retained");
            Assert.True(renderers.Any(r=>Effective(r).GetFloat("_SurfaceRole")==10),"Basalt role retained");
            // SetColor/GetColor perform Unity's color-space round trip. Compare
            // the stored representation exactly, rather than raw authored floats.
            var expected=new MaterialPropertyBlock();expected.SetColor("_BaseColor",PlayableWorld.FoundryGround);
            foreach(var renderer in ground)
            {
                Assert.AreEqual(expected.GetColor("_BaseColor"),Effective(renderer).GetColor("_BaseColor"),renderer.name);
                Assert.AreEqual("SlagAsh",renderer.sharedMaterial.GetTexture("_EarthTex").name);
                Assert.AreEqual("SlagAsh-b",renderer.sharedMaterial.GetTexture("_EarthTexB").name);
                Assert.AreEqual("SlagAsh-c",renderer.sharedMaterial.GetTexture("_EarthTexC").name);
            }
        }

        [UnityTest] public IEnumerator LobbyMatchAndLiveGraphicsResetRetainFoundryTextures()
        {
            Assert.Contains("-noaudio",Environment.GetCommandLineArgs());Assert.Contains("-muted",Environment.GetCommandLineArgs());
            var output=Environment.GetEnvironmentVariable("SPACEWARS_FOUNDRY_ORDINARY_EVIDENCE")??Path.GetFullPath("../.local/foundry-ordinary/captures");Directory.CreateDirectory(output);
            bool addKeyboard=Keyboard.current==null,addMouse=Mouse.current==null;
            var keyboard=addKeyboard?InputSystem.AddDevice<Keyboard>():Keyboard.current;var mouse=addMouse?InputSystem.AddDevice<Mouse>():Mouse.current;
            var host=new GameObject("Foundry ordinary lobby graphics witness");var app=host.AddComponent<PlayableBootstrap>();
            T Get<T>(string n)=>(T)typeof(PlayableBootstrap).GetField(n,Flags).GetValue(app);
            void Call(string n)=>typeof(PlayableBootstrap).GetMethod(n,Flags).Invoke(app,null);
            RenderTexture target=null;Camera camera=null;
            try
            {
                double until=Time.realtimeSinceStartupAsDouble+120;
                while(Get<VisualElement>("lobbyScreen")==null&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                Assert.NotNull(Get<VisualElement>("lobbyScreen"),"Menu startup");Call("ShowLobby");
                Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-map-choice").value="Огненный разлом (6)";
                var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.HasExplicitSeed=true;setup.ExplicitSeed=7;
                Assert.AreEqual(6,setup.Participants.Count);Call("LaunchLobbyMatch");until=Time.realtimeSinceStartupAsDouble+120;
                while((Get<bool>("inLobby")||Get<bool>("preparing"))&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                Assert.False(Get<bool>("inLobby"));Assert.False(Get<bool>("preparing"));
                var runtime=Get<PlayableRuntime>("runtime");Assert.NotNull(runtime);Assert.AreEqual(7,runtime.MatchSeed);
                var profile=Get<PlayableProfile>("profile");Assert.IsInstanceOf<FoundryMap>(profile.AuthoredMap);
                until=Time.realtimeSinceStartupAsDouble+30;
                while((Get<PlayableSnapshot>("view")==null||Get<PlayableSnapshot>("view").Tick<60)&&Time.realtimeSinceStartupAsDouble<until){Assert.False(runtime.IsStopped,runtime.Latest?.Failure);yield return null;}
                Assert.GreaterOrEqual(Get<PlayableSnapshot>("view").Tick,60,"Ordinary threaded match advances through app Update, without Pump or injected state");
                var world=Get<PlayableWorld>("world");AssertTerrain(world);
                camera=Get<Camera>("cameraView");target=new RenderTexture(1600,1000,24){antiAliasing=CameraAntialiasingProfile.Load().Samples};target.Create();camera.targetTexture=target;
                for(int frame=0;frame<8;frame++)yield return null;
                Save(target,Path.Combine(output,"ordinary-match.png"));
                var binding=camera.GetComponent<NativeGraphicsBinding>();
                typeof(NativeGraphicsBinding).GetMethod("Refresh",Flags).Invoke(binding,new object[]{"*"});
                for(int frame=0;frame<8;frame++)yield return null;
                AssertTerrain(world);Save(target,Path.Combine(output,"after-graphics-refresh.png"));
                var view=Get<PlayableSnapshot>("view");
                File.WriteAllText(Path.Combine(output,"capture.txt"),"Ordinary lobby → Foundry; default six participants; seed=7; tick="+view.Tick+"; paused="+view.Paused+"; map="+profile.AuthoredMap.Id+"@"+profile.AuthoredMap.Revision+"; Unity="+Application.unityVersion+"; GPU="+SystemInfo.graphicsDeviceName+"; real match camera, normal owner fog, no injected entities, no InspectionReveal, no synchronous Pump. Editor PlayMode; not a Player/device acceptance.");
            }
            finally
            {
                if(camera)camera.targetTexture=null;if(target){target.Release();UnityEngine.Object.Destroy(target);}UnityEngine.Object.Destroy(host);
                if(addKeyboard)InputSystem.RemoveDevice(keyboard);if(addMouse)InputSystem.RemoveDevice(mouse);
            }
            yield return null;
        }
        // One combined redesign witness: real menu/lobby, six AI, threaded app host.
        // Frozen seed/horizon/deadlines are recorded in the change QA plan before launch.
        [UnityTest,Timeout(540000)] public IEnumerator FracturedPlateauSixAiOrdinaryLobbyWitness()
        {
            Assert.Contains("-noaudio",Environment.GetCommandLineArgs());Assert.Contains("-muted",Environment.GetCommandLineArgs());
            var output=Environment.GetEnvironmentVariable("SPACEWARS_FOUNDRY_ORDINARY_EVIDENCE")??Path.GetFullPath("../.local/foundry-combined");Directory.CreateDirectory(output);
            bool addKeyboard=Keyboard.current==null,addMouse=Mouse.current==null;
            var keyboard=addKeyboard?InputSystem.AddDevice<Keyboard>():Keyboard.current;var mouse=addMouse?InputSystem.AddDevice<Mouse>():Mouse.current;
            var host=new GameObject("Fractured plateau combined ordinary witness");var app=host.AddComponent<PlayableBootstrap>();
            T Get<T>(string n)=>(T)typeof(PlayableBootstrap).GetField(n,Flags).GetValue(app);
            void Call(string n)=>typeof(PlayableBootstrap).GetMethod(n,Flags).Invoke(app,null);
            var origins=new Dictionary<int,NavPoint>();var movedScouts=new HashSet<int>();var developed=new HashSet<PlayableOwner>();var movedOwners=new HashSet<PlayableOwner>();var claimedMines=new HashSet<int>();
            PlayableRuntime runtime=null;RenderTexture target=null;Camera camera=null;MovementWitness movement=null;
            try
            {
                double until=Time.realtimeSinceStartupAsDouble+120;
                while(Get<VisualElement>("lobbyScreen")==null&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                Assert.NotNull(Get<VisualElement>("lobbyScreen"),"Menu startup");Call("ShowLobby");
                Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-map-choice").value="Огненный разлом (6)";
                var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.HasExplicitSeed=true;setup.ExplicitSeed=7;
                Assert.AreEqual(6,setup.Participants.Count);
                for(int i=0;i<6;i++){setup.Participants[i].Human=false;setup.Participants[i].Team=i<3?1:2;setup.Participants[i].Difficulty=AiDifficulty.Fighter;}
                Assert.True(setup.Spectator);Call("LaunchLobbyMatch");until=Time.realtimeSinceStartupAsDouble+120;
                while((Get<bool>("inLobby")||Get<bool>("preparing"))&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                Assert.False(Get<bool>("inLobby"));Assert.False(Get<bool>("preparing"));
                runtime=Get<PlayableRuntime>("runtime");Assert.NotNull(runtime);Assert.AreEqual(7,runtime.MatchSeed);
                var profile=Get<PlayableProfile>("profile");Assert.IsInstanceOf<FoundryMap>(profile.AuthoredMap);
                Assert.AreEqual(6,runtime.Latest.Participants.Count);Assert.True(runtime.Latest.Participants.All(p=>p.Control==OfflineControl.Ai));
                runtime.RequestPause(true);until=Time.realtimeSinceStartupAsDouble+120;
                while(!runtime.Latest.Paused&&!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                movement=new MovementWitness();movement.Attach(runtime,profile);runtime.RequestPause(false);
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
                var routes=Get<UnityHostRouteService>("routeService");var world=Get<PlayableWorld>("world");
                camera=Get<Camera>("cameraView");target=new RenderTexture(1600,1000,24){antiAliasing=CameraAntialiasingProfile.Load().Samples};target.Create();camera.targetTexture=target;
                until=Time.realtimeSinceStartupAsDouble+280;long report=-1;
                while(runtime.Latest.Tick<1800&&!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<until)
                {
                    var view=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview;
                    movement.Observe(view);
                    foreach(var u in view.Entities)
                    {
                        if(!origins.TryGetValue(u.Id,out var origin))origins.Add(u.Id,u.Position);
                        else if(u.Kind==PlayableEntityKind.Explorer&&Math.Sqrt(Math.Pow(u.Position.X-origin.X,2)+Math.Pow(u.Position.Z-origin.Z,2))>PlayableUnitRules.Radius(profile,u.Kind)){movedScouts.Add(u.Id);movedOwners.Add(u.Owner);}
                    }
                    foreach(var b in view.Buildings.Where(b=>b.Health>0&&b.Phase==ConstructionPhase.Ready&&(b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.Refinery)))developed.Add(b.Owner);
                    foreach(var site in view.Sites.Where(s=>s.Site.Kind==PlayableBuildingKind.Mine&&(s.Claimant.HasValue||s.Owner.HasValue)))
                    {
                        if(claimedMines.Add(site.Site.Id))File.AppendAllText(Path.Combine(output,"resource-events.txt"),"tick="+view.Tick+" site="+site.Site.Id+" claimant="+site.Claimant+" owner="+site.Owner+" progress="+site.Progress+" ready="+site.Ready+"\n");
                    }
                    if(view.Tick/300!=report)
                    {
                        report=view.Tick/300;
                        File.AppendAllText(Path.Combine(output,"progress.txt"),"tick="+view.Tick+" scouts="+movedScouts.Count+" movedOwners="+string.Join(",",movedOwners.OrderBy(o=>o))+" developed="+developed.Count+" sites="+string.Join(";",view.Sites.Where(s=>s.Progress>0||s.Ready).Select(s=>s.Site.Id+":"+s.Claimant+":"+s.Progress))+" failure="+view.Failure+" projection="+routes.LastProjectionFailure+"\n");
                    }
                    Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    yield return null;
                }
                runtime.RequestPause(true);
                while(!runtime.Latest.Paused&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                Assert.True(runtime.Latest.Paused,"Final evidence uses the paused existing match.");
                var final=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview;
                SaveReframedFinalState(runtime,camera,target,output);AssertTerrain(world);
                File.WriteAllText(Path.Combine(output,"result.txt"),"seed=7; six Fighter AI; native3v3; tick="+final.Tick+"; movedScouts="+movedScouts.Count+"; movedOwners="+string.Join(",",movedOwners.OrderBy(o=>o))+"; developedOwners="+developed.Count+"; claimedMines="+claimedMines.Count+"; failure="+final.Failure+"; projection="+routes.LastProjectionFailure+"; ordinary app Update threaded host; no Pump/injected state; Editor only.");
                Assert.IsNull(final.Failure);Assert.IsNull(routes.LastProjectionFailure);Assert.GreaterOrEqual(final.Tick,1800);
                Assert.AreEqual(6,developed.Count,"All owners complete early infrastructure");Assert.Greater(movedScouts.Count,0,"Actual movement, not command receipt");movement.Observe(final);movement.Verify(runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Players.ToDictionary(v=>v.OwnerId,v=>v.Owner),developed);Assert.Greater(claimedMines.Count,0,"Actual resource access on changed terrain");
            }
            finally
            {
                FinishMovementWitness(runtime,movement,output);
                if(camera)camera.targetTexture=null;if(target){target.Release();UnityEngine.Object.Destroy(target);}UnityEngine.Object.Destroy(host);
                if(addKeyboard)InputSystem.RemoveDevice(keyboard);if(addMouse)InputSystem.RemoveDevice(mouse);
            }
            yield return null;
        }
        // One combined redesign witness: real menu/lobby, six AI, threaded app host.
        // Frozen seed/horizon/deadlines are recorded in the change QA plan before launch.
        [UnityTest,Timeout(540000)] public IEnumerator FracturedPlateauSixAiDiagnosticOrdinaryWitness()
            => FracturedPlateauSixAiDiagnosticOrdinaryBody(4);

        [UnityTest,Timeout(540000)] public IEnumerator FracturedPlateauSixAiNormalSpeedOrdinaryWitness()
            => FracturedPlateauSixAiDiagnosticOrdinaryBody(1);

        [UnityTest,Timeout(540000)] public IEnumerator FracturedPlateauSixAiNormalSpeedFixedCohortWitness()
            => FracturedPlateauSixAiDiagnosticOrdinaryBody(1,true);

        private IEnumerator FracturedPlateauSixAiDiagnosticOrdinaryBody(double spectatorSpeed,bool fixedCohort=false)
        {
            Assert.Contains("-noaudio",Environment.GetCommandLineArgs());Assert.Contains("-muted",Environment.GetCommandLineArgs());
            var output=Environment.GetEnvironmentVariable("SPACEWARS_FOUNDRY_DIAGNOSTIC_EVIDENCE")??Path.GetFullPath("../.local/foundry-diagnostic");Directory.CreateDirectory(output);
            // Owned neutral devices: never allow shared physical/default edge input to pan this observer camera.
            var keyboard=InputSystem.AddDevice<Keyboard>();var mouse=InputSystem.AddDevice<Mouse>();
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(Screen.width*.5f,Screen.height*.5f)});
            var host=new GameObject("Fractured plateau combined ordinary witness");var app=host.AddComponent<PlayableBootstrap>();
            T Get<T>(string n)=>(T)typeof(PlayableBootstrap).GetField(n,Flags).GetValue(app);
            void Call(string n)=>typeof(PlayableBootstrap).GetMethod(n,Flags).Invoke(app,null);
            var origins=new Dictionary<int,NavPoint>();var movedScouts=new HashSet<int>();var developed=new HashSet<PlayableOwner>();var movedOwners=new HashSet<PlayableOwner>();var claimedMines=new HashSet<int>();
            PlayableRuntime runtime=null;RenderTexture target=null;Camera camera=null;MovementWitness movement=null;
            var evidence=new FoundryDiagnosticLog{spectatorSpeed=spectatorSpeed,wallFrequency=System.Diagnostics.Stopwatch.Frequency};
            long horizon=fixedCohort?3600:1800;int prefixDevelopedCount=-1,prefixMineCount=-1;var maxDisplacement=new Dictionary<int,double>();
            PlayableAuthorityTick authority=null;
            void Sample(PlayableSpectatorFrame frame)
            {
                if(evidence.samples.Count>=(fixedCohort?13:8))return;
                var checkpoints=authority.CaptureDiagnosticCheckpoints();
                var sample=new FoundryDiagnosticSample{tick=frame.Overview.Tick,wallTicks=System.Diagnostics.Stopwatch.GetTimestamp(),camera=CameraState(camera),owners=frame.Players.Select(view=>
                {
                    var cp=checkpoints.Single(c=>c.OwnerId==view.OwnerId);
                    var scouts=view.Entities.Where(u=>u.Owner==view.Owner&&u.Kind==PlayableEntityKind.Explorer).Select(u=>
                    {
                        origins.TryGetValue(u.Id,out var origin);maxDisplacement.TryGetValue(u.Id,out double maximum);
                        return "id="+u.Id+" origin="+origin.X+","+origin.Z+" current="+u.Position.X+","+u.Position.Z+" max="+maximum+" moving="+u.Moving+" outcome="+u.NavigationOutcome+" order="+(u.CurrentOrder==null?"none":u.CurrentOrder.Kind+":"+u.CurrentOrder.Destination.X+","+u.CurrentOrder.Destination.Z+"@"+u.CurrentOrder.IssuedTick)+" queue="+(u.Queue==null?"none":"present")+" ownProofs="+view.RouteProofs.Count(r=>r.UnitId==u.Id);
                    }).ToArray();
                    var opening=cp.OpeningExecution;var expansion=cp.Expansion;
                    return "owner="+view.OwnerId+" logical="+((int)view.Owner+1)+" home="+view.HomeSiteId+" team="+view.Team+" profile="+view.ProfileId+"@"+view.ProfileRevision+" scouts=["+string.Join(";",scouts)+"] decision="+cp.DecisionTick+" lastScoutOrder="+cp.LastScoutOrderTick+" pending="+cp.PendingActionId+" policy="+cp.LastPolicy+" action="+cp.LastActionKind+" opening="+cp.Opening?.Opening+" openingPhase="+opening?.Phase+" protected="+opening?.ProtectedCapturerId+" openingReason="+opening?.Reason+" expansion="+expansion?.SiteId+":"+expansion?.ActorId+":"+expansion?.Phase+":"+expansion?.Reason+" claims="+string.Join(";",(cp.Armies?.Armies??Array.Empty<AiArmyState>()).Where(c=>c.Phase!=AiArmyPhase.Disbanded).Take(8).Select(c=>c.Role+":"+c.TacticalOwner+":"+c.Objective+":"+c.Phase))+" receipts="+string.Join(";",cp.Records.Skip(Math.Max(0,cp.Records.Count-8)).Select(r=>r.ApplicationTick+":"+r.Policy+":"+r.Kind+":"+r.Status+":"+r.RuntimeStatus+":"+r.Message));
                }).ToArray(),sites=frame.Overview.Sites.Select(s=>s.Site.Id+":"+s.Claimant+":"+s.Owner+":"+s.Progress+":"+s.Ready).ToArray()};
                evidence.samples.Add(sample);
            }
            try
            {
                double until=Time.realtimeSinceStartupAsDouble+120;
                while(Get<VisualElement>("lobbyScreen")==null&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                Assert.NotNull(Get<VisualElement>("lobbyScreen"),"Menu startup");Call("ShowLobby");
                Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-map-choice").value="Огненный разлом (6)";
                var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.HasExplicitSeed=true;setup.ExplicitSeed=7;
                Assert.AreEqual(6,setup.Participants.Count);
                for(int i=0;i<6;i++){setup.Participants[i].Human=false;setup.Participants[i].Team=i<3?1:2;setup.Participants[i].Difficulty=AiDifficulty.Fighter;}
                Assert.True(setup.Spectator);Call("LaunchLobbyMatch");until=Time.realtimeSinceStartupAsDouble+120;
                while((Get<bool>("inLobby")||Get<bool>("preparing"))&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                Assert.False(Get<bool>("inLobby"));Assert.False(Get<bool>("preparing"));
                runtime=Get<PlayableRuntime>("runtime");Assert.NotNull(runtime);Assert.AreEqual(7,runtime.MatchSeed);
                var profile=Get<PlayableProfile>("profile");Assert.IsInstanceOf<FoundryMap>(profile.AuthoredMap);
                evidence.gameplayProfile=JsonUtility.ToJson(profile.CopyData());evidence.aiProfile=JsonUtility.ToJson(runtime.NativeAiProfile.CopyData());evidence.lobby=JsonUtility.ToJson(runtime.LobbyConfiguration);
                authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",Flags).GetValue(runtime);
                Assert.AreEqual(6,runtime.Latest.Participants.Count);Assert.True(runtime.Latest.Participants.All(p=>p.Control==OfflineControl.Ai));
                runtime.RequestPause(true);until=Time.realtimeSinceStartupAsDouble+120;
                while(!runtime.Latest.Paused&&!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                movement=new MovementWitness(fixedCohort?1800:long.MaxValue);movement.Attach(runtime,profile);runtime.RequestPause(false);
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,spectatorSpeed);
                var routes=Get<UnityHostRouteService>("routeService");var world=Get<PlayableWorld>("world");
                camera=Get<Camera>("cameraView");target=new RenderTexture(1600,1000,24){antiAliasing=CameraAntialiasingProfile.Load().Samples};target.Create();camera.targetTexture=target;
                var observerInput=host.GetComponent<PlayableInput>();observerInput.AssignedKeyboard=keyboard;observerInput.AssignedMouse=mouse;
                InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(target.width*.5f,target.height*.5f)});
                evidence.initialCamera=CameraState(camera);
                void ObserveFrame(PlayableSpectatorFrame observed)
                {
                    var view=observed.Overview;
                    movement.Observe(view);
                    foreach(var u in view.Entities)
                    {
                        if(!origins.TryGetValue(u.Id,out var origin)){origins.Add(u.Id,u.Position);origin=u.Position;}
                        double displacement=Math.Sqrt(Math.Pow(u.Position.X-origin.X,2)+Math.Pow(u.Position.Z-origin.Z,2));maxDisplacement.TryGetValue(u.Id,out double previousMaximum);maxDisplacement[u.Id]=Math.Max(previousMaximum,displacement);
                        if(u.Kind==PlayableEntityKind.Explorer&&Math.Sqrt(Math.Pow(u.Position.X-origin.X,2)+Math.Pow(u.Position.Z-origin.Z,2))>PlayableUnitRules.Radius(profile,u.Kind)){movedScouts.Add(u.Id);movedOwners.Add(u.Owner);}
                    }
                    foreach(var b in view.Buildings.Where(b=>b.Health>0&&b.Phase==ConstructionPhase.Ready&&(b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.Refinery)))developed.Add(b.Owner);
                    foreach(var site in view.Sites.Where(s=>s.Site.Kind==PlayableBuildingKind.Mine&&(s.Claimant.HasValue||s.Owner.HasValue)))
                    {
                        if(claimedMines.Add(site.Site.Id))evidence.resourceEvents.Add("tick="+view.Tick+" site="+site.Site.Id+" claimant="+site.Claimant+" owner="+site.Owner+" progress="+site.Progress+" ready="+site.Ready);
                    }
                    if(fixedCohort&&movement.PrefixReadbackTick<0&&view.Tick>=1800) {
                        movement.FreezePrefix(view.Tick,developed,claimedMines.Count);
                        prefixDevelopedCount=developed.Count;prefixMineCount=claimedMines.Count;
                        File.WriteAllText(Path.Combine(output,"prefix-facts.txt"),"cutoff=1800; firstReadbackTick="+view.Tick+
                            "; wallTicks="+System.Diagnostics.Stopwatch.GetTimestamp()+"; developedOwners="+string.Join(",",developed)+"; claimedMines="+string.Join(",",claimedMines)+"\n"+
                            movement.Report(observed.Players.ToDictionary(v=>v.OwnerId,v=>v.Owner)));
                    }
                }
                until=Time.realtimeSinceStartupAsDouble+280;long report=-1;
                while(runtime.Latest.Tick<horizon&&!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<until)
                {
                    var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);var view=frame.Overview;
                    ObserveFrame(frame);
                    if(view.Tick/300!=report)
                    {
                        report=view.Tick/300;
                        Sample(frame);
                    }
                    Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    yield return null;
                }
                var finalFrame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);var final=finalFrame.Overview;if(fixedCohort)ObserveFrame(finalFrame);Sample(finalFrame);
                runtime.RequestPause(true);
                while(!runtime.Latest.Paused&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                Assert.True(runtime.Latest.Paused,"Freeze capture on the existing runtime, without advancing a new match");
                var capture=runtime.RequestCaptureBytes();
                while(!capture.IsCompleted&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                Assert.True(capture.IsCompleted,"Current-format capture must finish under the same280s deadline");
                File.WriteAllBytes(Path.Combine(output,"current-format.capture"),capture.GetAwaiter().GetResult());
                evidence.captureTick=runtime.Latest.Tick;evidence.finalCamera=CameraState(camera);
                evidence.inFrustum=world.GraphicsRoot.GetComponentsInChildren<Renderer>().Count(r=>r.enabled&&r.gameObject.activeInHierarchy&&GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera),r.bounds));
                var reframed=SaveReframedFinalState(runtime,camera,target,output);AssertTerrain(world);
                evidence.originalFrameColors=reframed.originalColors;evidence.reframedCamera=reframed.reframedCamera;
                var active=RenderTexture.active;Texture2D pixels=null;
                try{RenderTexture.active=target;pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();evidence.frameColors=pixels.GetPixels32().Where((_,i)=>i%173==0).Select(c=>((int)c.r<<16)|((int)c.g<<8)|c.b).Distinct().Count();}
                finally{RenderTexture.active=active;if(pixels)UnityEngine.Object.Destroy(pixels);}
                File.WriteAllText(Path.Combine(output,"bounded-observation.json"),JsonUtility.ToJson(evidence,true));
                Assert.Greater(evidence.frameColors,16,"Reframed ordinary final state must contain rendered scene detail; original frame is preserved separately");
                File.WriteAllText(Path.Combine(output,"result.txt"),"seed=7; six Fighter AI; native3v3; tick="+final.Tick+"; movedScouts="+movedScouts.Count+"; movedOwners="+string.Join(",",movedOwners.OrderBy(o=>o))+"; developedOwners="+developed.Count+"; claimedMines="+claimedMines.Count+"; failure="+final.Failure+"; projection="+routes.LastProjectionFailure+"; ordinary app Update threaded host; no Pump/injected state; Editor only.");
                Assert.IsNull(final.Failure);Assert.IsNull(routes.LastProjectionFailure);Assert.GreaterOrEqual(final.Tick,horizon);
                if(fixedCohort) {
                    Assert.AreEqual(13,evidence.samples.Count,"Seven prefix samples and six tail samples must be retained.");
                    Assert.GreaterOrEqual(movement.PrefixReadbackTick,1800);
                    Assert.AreEqual(6,prefixDevelopedCount,"All six early infrastructures must be present in the frozen prefix.");
                    Assert.Greater(prefixMineCount,0,"Resource access must be present in the frozen prefix; tail cannot rescue it.");
                }
                Assert.AreEqual(6,developed.Count,"All owners complete early infrastructure");Assert.Greater(movedScouts.Count,0,"Actual movement, not command receipt");movement.Observe(final);movement.Verify(runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Players.ToDictionary(v=>v.OwnerId,v=>v.Owner),developed);Assert.Greater(claimedMines.Count,0,"Actual resource access on changed terrain");
            }
            finally
            {
                if(string.IsNullOrEmpty(evidence.finalCamera))evidence.finalCamera=CameraState(camera);
                File.WriteAllText(Path.Combine(output,"bounded-observation.json"),JsonUtility.ToJson(evidence,true));
                FinishMovementWitness(runtime,movement,output);
                if(camera)camera.targetTexture=null;if(target){target.Release();UnityEngine.Object.Destroy(target);}UnityEngine.Object.Destroy(host);
                InputSystem.RemoveDevice(keyboard);InputSystem.RemoveDevice(mouse);
            }
            yield return null;
        }
        [Serializable] private sealed class FoundryDiagnosticSample { public long tick,wallTicks;public string camera;public string[] owners,sites; }
        [Serializable] private sealed class FoundryDiagnosticLog
        {
            public string gameplayProfile,aiProfile,lobby,initialCamera,finalCamera,reframedCamera;
            public long captureTick,wallFrequency;public double spectatorSpeed;public int frameColors,originalFrameColors,inFrustum;
            public List<FoundryDiagnosticSample> samples=new List<FoundryDiagnosticSample>();
            public List<string> resourceEvents=new List<string>();
        }
        private static string CameraState(Camera camera)=>!camera?"null":"enabled="+camera.enabled+" active="+camera.gameObject.activeInHierarchy+" position="+camera.transform.position.ToString("R")+" rotation="+camera.transform.eulerAngles.ToString("R")+" ortho="+camera.orthographicSize+" near="+camera.nearClipPlane+" far="+camera.farClipPlane+" rect="+camera.pixelRect+" mask="+camera.cullingMask+" target="+(camera.targetTexture?camera.targetTexture.width+"x"+camera.targetTexture.height:"screen");
        [Serializable] private sealed class FoundryReframeEvidence
        {
            public string label,originalCamera,reframedCamera;
            public Vector3 originalPosition;public Quaternion originalRotation;public float originalOrthographicSize;
            public float originalNearClip,originalFarClip,reframedNearClip,reframedFarClip;
            public long tick;public int originalMask,originalColors;
        }
        private static FoundryReframeEvidence SaveReframedFinalState(PlayableRuntime runtime,Camera camera,RenderTexture target,string output)
        {
            Assert.True(runtime.Latest.Paused);Assert.GreaterOrEqual(runtime.Latest.Tick,1800);
            var evidence=new FoundryReframeEvidence{label="Reframed ordinary final state; original spectator framing is preserved; no movement acceptance from this PNG.",
                tick=runtime.Latest.Tick,originalCamera=CameraState(camera),originalPosition=camera.transform.position,originalRotation=camera.transform.rotation,
                originalOrthographicSize=camera.orthographicSize,originalMask=camera.cullingMask,originalNearClip=camera.nearClipPlane,originalFarClip=camera.farClipPlane};
            camera.Render();Save(target,Path.Combine(output,"ordinary-match.png"));
            var active=RenderTexture.active;Texture2D pixels=null;
            try{RenderTexture.active=target;pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();evidence.originalColors=pixels.GetPixels32().Where((_,i)=>i%173==0).Select(c=>((int)c.r<<16)|((int)c.g<<8)|c.b).Distinct().Count();}
            finally{RenderTexture.active=active;if(pixels)UnityEngine.Object.Destroy(pixels);}
            File.WriteAllText(Path.Combine(output,"camera-before-reframe.json"),JsonUtility.ToJson(evidence,true));
            // Test observer only, after horizon and acknowledged pause. Existing
            // live world, visibility and culling mask are used without changes.
            camera.transform.position=new Vector3(0,220,-130);camera.transform.LookAt(new Vector3(0,3,0));camera.orthographicSize=145;
            // This observer's north floor is ~327.5m deep; the runtime camera's
            // original 300m clip is preserved above before this test-only pose.
            camera.farClipPlane=Mathf.Max(400,evidence.originalFarClip);
            evidence.reframedNearClip=camera.nearClipPlane;evidence.reframedFarClip=camera.farClipPlane;
            camera.Render();Save(target,Path.Combine(output,"final-world-reframed.png"));
            Assert.AreEqual(evidence.originalMask,camera.cullingMask);
            evidence.reframedCamera=CameraState(camera);
            File.WriteAllText(Path.Combine(output,"reframe-result.json"),JsonUtility.ToJson(evidence,true));
            File.WriteAllText(Path.Combine(output,"final-world-reframed.txt"),evidence.label+" tick="+evidence.tick);
            return evidence;
        }
        private static void Save(RenderTexture target,string path)
        {
            var active=RenderTexture.active;Texture2D image=null;
            try
            {
                RenderTexture.active=target;image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                var pixels=image.GetPixels32();Assert.Less(pixels.Count(c=>c.r>200&&c.b>200&&c.g<40),16,"No shader error pink");
                File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally{RenderTexture.active=active;if(image)UnityEngine.Object.Destroy(image);}
        }
    }
}
