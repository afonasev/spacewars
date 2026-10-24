using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Tests.PlayMode
{
    public sealed class ArmoredFactoryIntegrationTests
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        static PlayableBuildingSnapshot Building(bool working=false,bool visible=true,double progress=1,int stage=0,double time=0)=>new PlayableBuildingSnapshot(
            10,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(4,7),100,progress,working?1:0,0,default,
            launchPhase:stage,launchTime:time,launchKind:stage==1||stage==2?(PlayableEntityKind?)PlayableEntityKind.Tank:null,includePrivateState:visible,orders:working?new[]{new PlayableProductionOrderSnapshot(1,PlayableEntityKind.Tank,150,1,10,5,true)}:null);

        [UnityTest] public IEnumerator SnapshotLifecycleFreezesAndDoesNotMistakeCancellationForRelease()
        {
            var root=new GameObject("Factory lifecycle regression");
            using(var world=new PlayableWorld(root.transform,PlayableProfile.Default,staged:true))
            try
            {
                world.SetPresentationLayer(11);
                var actor=world.Building(10,"Factory",true);var model=actor.Factory;
                Assert.NotNull(model);Assert.AreEqual(0,actor.Root.GetComponentsInChildren<Collider>().Length);
                Assert.That(actor.Hull.name,Does.Contain("factory-armored"));
                var rules=PlayableProfile.Default;
                model.Drive(Building(true),0,rules);model.Drive(Building(true),30,rules);
                Assert.AreEqual(FactoryModelPresentation.Phase.Working,model.phase);Assert.Greater(model.SmokeParticles,0);
                Assert.True(model.GetComponentsInChildren<ParticleSystem>().All(p=>p.gameObject.layer==11));
                var fan=model.GetComponentsInChildren<Transform>().Single(t=>t.name=="fan0");var rotation=fan.localRotation;
                var particles=model.SmokeParticles;
                for(int frame=0;frame<5;frame++){yield return null;model.Drive(Building(true),30,rules);}
                Assert.AreEqual(rotation,fan.localRotation);Assert.AreEqual(particles,model.SmokeParticles,"Paused snapshots must not advance particles");
                model.Drive(Building(),31,rules);Assert.AreEqual(FactoryModelPresentation.Phase.Idle,model.phase);Assert.AreEqual(0,model.doorOpen);
                model.Drive(Building(),120,rules);Assert.AreEqual(0,model.SmokeParticles,"Cancelled work dissipates");
                model.Drive(Building(true,false),150,rules);Assert.AreEqual(FactoryModelPresentation.Phase.Idle,model.phase,"Private queue is not disclosed");
                model.Drive(Building(true,progress:.5),180,rules);Assert.AreEqual(FactoryModelPresentation.Phase.Idle,model.phase);
                model.Drive(Building(stage:FactoryReleaseRules.Opening,time:rules.FactoryGateSeconds),195,rules);Assert.AreEqual(FactoryModelPresentation.Phase.Rollout,model.phase);Assert.AreEqual(1,model.doorOpen);
                model.Drive(Building(stage:FactoryReleaseRules.Driving,time:.5),210,rules);Assert.AreEqual(1,model.doorOpen,"Gate stays open during protected drive-out");
                model.Drive(Building(stage:FactoryReleaseRules.Closing,time:rules.FactoryGateSeconds),240,rules);Assert.AreEqual(0,model.doorOpen);
                model.Drive(Building(),270,rules);Assert.AreEqual(FactoryModelPresentation.Phase.Idle,model.phase);
                var staged=Building(stage:FactoryReleaseRules.Driving,time:.5);
                var snapshot=new PlayableSnapshot(rules.ProfileId,rules.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),new[]{staged},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null);
                world.UpdateFactoryModel(actor,staged,snapshot);
                foreach(var color in new[]{Color.red,Color.green,Color.magenta})
                {
                    PlayableWorld.PaintOwner(actor,color);int painted=0;
                    foreach(var renderer in actor.FactoryStaged.GetComponentsInChildren<Renderer>())for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)
                    {
                        if(!renderer.sharedMaterials[slot].name.EndsWith("team-primary"))continue;
                        var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);Assert.AreEqual(color,block.GetColor("_BaseColor"));painted++;
                    }
                    Assert.Greater(painted,0);
                }
            }
            finally{UnityEngine.Object.Destroy(root);}
            yield return null;
        }

        [UnityTest] public IEnumerator OrdinaryLobbyProductionUsesArmoredFactory()
        {
            Assert.Contains("-noaudio",Environment.GetCommandLineArgs());Assert.Contains("-muted",Environment.GetCommandLineArgs());
            var output=Environment.GetEnvironmentVariable("SPACEWARS_FACTORY_EVIDENCE")??Path.GetFullPath("../.local/factory-integration");Directory.CreateDirectory(output);
            bool addKeyboard=Keyboard.current==null,addMouse=Mouse.current==null;
            var keyboard=addKeyboard?InputSystem.AddDevice<Keyboard>():Keyboard.current;var mouse=addMouse?InputSystem.AddDevice<Mouse>():Mouse.current;
            var host=new GameObject("Ordinary factory production witness");var app=host.AddComponent<PlayableBootstrap>();
            T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Flags).GetValue(app);
            void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(app,null);
            void Submit(PlayableCommandKind kind,int[] ids=null)=>typeof(PlayableBootstrap).GetMethod("Submit",Flags).Invoke(app,new object[]{kind,ids,default(NavPoint),0,0L,PlayableEntityKind.Tank,PlayableResearchKind.TankChassis,PlayableOrderMode.Replace});
            Camera inspection=null;RenderTexture target=null;
            var trace=new List<string>();long traceTick=-30;string traceState=null;
            void Trace(string stage)
            {
                var snapshot=Get<PlayableSnapshot>("view");if(snapshot==null)return;
                var factory=snapshot.Buildings.FirstOrDefault(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory);
                var events=snapshot.Sounds.Where(e=>e.Kind==PlayableSoundKind.ProductionComplete||e.Kind==PlayableSoundKind.ProductionLimited).ToArray();
                string state=stage+" orders="+string.Join(",",factory?.PrivateState?.Orders.Select(o=>o.Id+":"+o.Active+":"+Math.Ceiling(o.Remaining))??Array.Empty<string>())+" events="+string.Join(",",events.Select(e=>e.Id+":"+e.Kind+":"+e.Tick+":"+e.Building+":"+e.Position.X+","+e.Position.Z));
                if(state==traceState&&snapshot.Tick-traceTick<30)return;traceState=state;traceTick=snapshot.Tick;
                var world=Get<PlayableWorld>("world");FactoryModelPresentation model=null;if(factory!=null&&world!=null&&world.Actors.TryGetValue(factory.Id,out var actor))model=actor.Factory;
                trace.Add("wall="+Time.realtimeSinceStartupAsDouble+" tick="+snapshot.Tick+" paused="+snapshot.Paused+" units="+snapshot.Entities.Count(e=>e.Owner==PlayableOwner.Player)+" launch="+(factory==null?"absent":factory.LaunchPhase+"/"+factory.LaunchTime)+" model="+(model?model.phase+"/"+model.doorOpen:"absent")+" "+state);
            }
            try
            {
                double until=Time.realtimeSinceStartupAsDouble+120;
                while(Get<VisualElement>("lobbyScreen")==null&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                Assert.NotNull(Get<VisualElement>("lobbyScreen"));Call("ShowLobby");
                Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-map-choice").value="Огненный разлом (6)";
                var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.HasExplicitSeed=true;setup.ExplicitSeed=7;
                Call("LaunchLobbyMatch");until=Time.realtimeSinceStartupAsDouble+120;
                while((Get<bool>("inLobby")||Get<bool>("preparing"))&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                Assert.False(Get<bool>("inLobby"));Assert.False(Get<bool>("preparing"));
                var runtime=Get<PlayableRuntime>("runtime");Assert.NotNull(runtime);Assert.AreEqual(7,runtime.MatchSeed);
                PlayableSnapshot View()=>Get<PlayableSnapshot>("view");
                PlayableBuildingSnapshot Factory()=>View()?.Buildings.FirstOrDefault(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory);
                until=Time.realtimeSinceStartupAsDouble+90;
                while((View()==null||View().Tick<2)&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                Assert.NotNull(View());Submit(PlayableCommandKind.BuildFactory);
                until=Time.realtimeSinceStartupAsDouble+90;
                while((Factory()==null||Factory().Progress<1||Factory().Phase!=ConstructionPhase.Ready)&&Time.realtimeSinceStartupAsDouble<until){Trace("observe");Assert.False(runtime.IsStopped,runtime.Latest?.Failure);yield return null;}
                var factory=Factory();Assert.NotNull(factory,"Normal construction command created factory");Assert.AreEqual(1,factory.Progress,"Factory completed within fixed budget");
                var world=Get<PlayableWorld>("world");var actor=world.Actors[factory.Id];Assert.NotNull(actor.Factory);
                int initialUnits=View().Entities.Count(e=>e.Owner==PlayableOwner.Player);
                inspection=new GameObject("Factory game-scene inspection camera").AddComponent<Camera>();inspection.CopyFrom(Get<Camera>("cameraView"));inspection.enabled=false;
                var direction=new Vector3((float)Math.Cos(factory.Heading),0,(float)Math.Sin(factory.Heading));
                inspection.orthographicSize=7;inspection.transform.position=actor.Root.position+direction*12+Vector3.Cross(Vector3.up,direction)*9+Vector3.up*12;inspection.transform.LookAt(actor.Root.position+Vector3.up*.5f);
                target=new RenderTexture(1600,1000,24){antiAliasing=CameraAntialiasingProfile.Load().Samples};target.Create();inspection.targetTexture=target;
                Capture(inspection,target,Path.Combine(output,"idle.png"));
                Submit(PlayableCommandKind.QueueTank,new[]{factory.Id});until=Time.realtimeSinceStartupAsDouble+90;
                while(actor.Factory.SmokeParticles==0&&Time.realtimeSinceStartupAsDouble<until){Trace("observe");Assert.False(runtime.IsStopped,runtime.Latest?.Failure);yield return null;}
                Assert.AreEqual(FactoryModelPresentation.Phase.Working,actor.Factory.phase);Assert.Greater(actor.Factory.SmokeParticles,0);
                Capture(inspection,target,Path.Combine(output,"working.png"));
                until=Time.realtimeSinceStartupAsDouble+90;
                while(Factory().LaunchPhase!=FactoryReleaseRules.Opening&&Time.realtimeSinceStartupAsDouble<until){Trace("opening");Assert.False(runtime.IsStopped,runtime.Latest?.Failure);yield return null;}
                Assert.AreEqual(FactoryReleaseRules.Opening,Factory().LaunchPhase);Assert.AreEqual(initialUnits,View().Entities.Count(e=>e.Owner==PlayableOwner.Player));Capture(inspection,target,Path.Combine(output,"opening.png"));
                until=Time.realtimeSinceStartupAsDouble+90;
                while((Factory().LaunchPhase!=FactoryReleaseRules.Driving||Factory().LaunchTime<.4)&&Time.realtimeSinceStartupAsDouble<until){Trace("driving");Assert.False(runtime.IsStopped,runtime.Latest?.Failure);yield return null;}
                Assert.AreEqual(FactoryReleaseRules.Driving,Factory().LaunchPhase);Assert.AreEqual(1,actor.Factory.doorOpen);Assert.AreEqual(initialUnits,View().Entities.Count(e=>e.Owner==PlayableOwner.Player),"Staged vehicle is not yet an ordinary command or damage target");
                Assert.True(actor.FactoryStaged&&actor.FactoryStaged.gameObject.activeSelf);Assert.AreEqual(0,actor.FactoryStaged.GetComponentsInChildren<Collider>().Length);Capture(inspection,target,Path.Combine(output,"driving.png"));
                until=Time.realtimeSinceStartupAsDouble+90;
                while(View().Entities.Count(e=>e.Owner==PlayableOwner.Player)==initialUnits&&Time.realtimeSinceStartupAsDouble<until){Trace("activation");Assert.False(runtime.IsStopped,runtime.Latest?.Failure);yield return null;}
                Assert.Greater(View().Entities.Count(e=>e.Owner==PlayableOwner.Player),initialUnits,"Authority activated unit after full clearance");Assert.AreEqual(FactoryReleaseRules.Closing,Factory().LaunchPhase);Assert.False(actor.FactoryStaged.gameObject.activeSelf);
                var tank=View().Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank);var tankActor=world.Actors[tank.Id];
                float plane=(float)FactoryReleaseRules.GatePlane(Get<PlayableProfile>("profile"));
                foreach(var filter in tankActor.Hull.GetComponentsInChildren<MeshFilter>())foreach(var vertex in filter.sharedMesh.vertices)
                    Assert.GreaterOrEqual(Vector3.Dot(filter.transform.TransformPoint(vertex)-actor.Root.position,direction),plane-.001f,"Entire rendered tank clears gate before activation");
                Capture(inspection,target,Path.Combine(output,"release.png"));
                until=Time.realtimeSinceStartupAsDouble+90;
                while((actor.Factory.phase!=FactoryModelPresentation.Phase.Idle||actor.Factory.SmokeParticles!=0)&&Time.realtimeSinceStartupAsDouble<until){Trace("observe");Assert.False(runtime.IsStopped,runtime.Latest?.Failure);yield return null;}
                Assert.AreEqual(FactoryModelPresentation.Phase.Idle,actor.Factory.phase);Assert.AreEqual(0,actor.Factory.SmokeParticles);
                File.WriteAllText(Path.Combine(output,"capture.txt"),"Ordinary lobby Foundry six participants, seed 7, current profiles. BuildFactory and QueueTank through normal app Submit, threaded host advanced by app Update. Opening before motion; protected staged vehicle; full rendered hull clearance at activation; gate closure and smoke idle. Tick="+View().Tick+". Actual game scene, separate close inspection camera, normal owner fog, no injected entities, no synchronous Pump. Editor PlayMode, not Player/device or human acceptance.");
            }
            finally
            {
                Trace("final");File.WriteAllLines(Path.Combine(output,"production-trace.txt"),trace);
                if(inspection)UnityEngine.Object.Destroy(inspection.gameObject);if(target){target.Release();UnityEngine.Object.Destroy(target);}UnityEngine.Object.Destroy(host);
                if(addKeyboard)InputSystem.RemoveDevice(keyboard);if(addMouse)InputSystem.RemoveDevice(mouse);
            }
            yield return null;
        }
        static void Capture(Camera camera,RenderTexture target,string path)
        {
            camera.Render();var previous=RenderTexture.active;Texture2D image=null;
            try{RenderTexture.active=target;image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();Assert.Less(image.GetPixels32().Count(c=>c.r>200&&c.b>200&&c.g<40),16,"No error shader");File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{RenderTexture.active=previous;if(image)UnityEngine.Object.Destroy(image);}
        }
    }
}
