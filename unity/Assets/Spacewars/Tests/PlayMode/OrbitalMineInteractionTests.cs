using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        [UnityTest] public IEnumerator MineMousePreviewPlacementAndGroundBurstUseMatchingRadius()
        {
            yield return StartOrdinary(2,configureRoster:d=>d.Participants[0].DeviceId=0);
            var seat=Seats[0];var other=Seats[1];Runtime.RequestPause(true);yield return Wait(()=>Runtime.Latest.Paused,"seed mine unlock");
            var initial=Get<PlayableSnapshot>(seat,"view");var domain=typeof(PlayableRuntime).GetField("domain",F).GetValue(Runtime);var type=domain.GetType();
            type.GetMethod("CreditOrbital",F).Invoke(domain,new object[]{(PlayableOwner?)initial.Owner,Get<PlayableSnapshot>(other,"view").Owner,250d});
            Runtime.RequestPause(false);yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.PendingLevel==1,"mine choice");Assert.True((bool)Call(seat,"OpenOrbital",false));
            yield return Capture("mine-choice-grid",captureWidth:1600,captureHeight:1000,captureChecks:()=>{
                var stats=Get<VisualElement>(seat,"orbitalCards").Q("orbital-choice-2").Q("orbital-choice-stats");
                foreach(var cell in stats.Children())foreach(var label in cell.Query<Label>().ToList())Assert.LessOrEqual(label.MeasureTextSize(label.text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined).x,cell.worldBound.width+1,"mine stat must fit its cell: "+label.text);
                Inside(Get<VisualElement>(seat,"root").worldBound,stats.worldBound,"mine stats grid");
            });
            Call(seat,"SubmitOrbital",true,2);
            yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.Abilities.Count==1,"mine selected");
            var point=Get<PlayableSnapshot>(seat,"view").Entities.First(e=>e.Owner==initial.Owner).Position;point=new NavPoint(point.X,point.Z+3);
            Call(seat,"FocusScanPoint",point);var camera=Get<Camera>(seat,"cameraView");var world=Get<PlayableWorld>(seat,"world");
            camera.orthographicSize=6;camera.transform.position=world.Point(point)+new Vector3(0,10,-10);camera.transform.LookAt(world.Point(point));
            var screen=(Vector2)camera.WorldToScreenPoint(world.Point(point));Set(Mouse.current.position,screen);InputSystem.Update();Call(seat,"ApplyOrbitalFromHud",2);
            Assert.AreEqual(2,Get<int>(seat,"scanTargetAbility"));Assert.True(Get<bool>(seat,"scanTargeting"));
            var preview=(NavPoint?)Call(seat,"ScanPreviewPoint");Assert.True(preview.HasValue);Assert.AreEqual(point.X,preview.Value.X,.05);Assert.AreEqual(point.Z,preview.Value.Z,.05);
            Assert.AreEqual(1.4,(double)typeof(PlayableBootstrap).GetProperty("TargetPreviewRadius",F).GetValue(seat));
            yield return Capture("mine-preview-radius",captureWidth:1600,captureHeight:1000,captureChecks:()=>Assert.True(Get<bool>(seat,"scanTargeting")));
            Call(seat,"ScanPointer",screen,true,true,false);yield return Wait(()=>!Get<bool>(seat,"scanTargeting"),"mine placement receipt");
            Assert.AreEqual(1,Get<PlayableSnapshot>(seat,"view").Mines.Count);
            var effectTick=Get<PlayableSnapshot>(seat,"view").Sounds.Single(e=>e.Kind==PlayableSoundKind.OrbitalMineInstalled).Tick;
            yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Tick>=effectTick+10,"mine activation pulse midpoint");
            Assert.Greater(Get<PlayableWorld>(seat,"world").AbilityEffectCount,0,"successful installation creates pulse");Runtime.RequestPause(true);yield return Wait(()=>Runtime.Latest.Paused,"pause mine visual");
            yield return Capture("mine-b-normal",captureWidth:1600,captureHeight:1000,captureChecks:()=>Assert.AreEqual(1,Get<PlayableSnapshot>(seat,"view").Mines.Count));
            camera.orthographicSize=1.3f;camera.transform.position=world.Point(point)+new Vector3(0,1.8f,-1.8f);camera.transform.LookAt(world.Point(point));
            yield return Capture("mine-b-close",captureWidth:1600,captureHeight:1000,captureChecks:()=>Assert.AreEqual(1,Get<PlayableSnapshot>(seat,"view").Mines.Count));
            // Inspect each active runtime LOD at comparable scale without HUD occlusion.
            string output=Environment.GetEnvironmentVariable("ORDINARY_PAD_EVIDENCE");
            void WorldFrame(string name){
                if(string.IsNullOrEmpty(output))return;System.IO.Directory.CreateDirectory(output);
                var target=new RenderTexture(800,800,24);target.Create();var previous=camera.targetTexture;var rect=camera.rect;float aspect=camera.aspect;var active=RenderTexture.active;
                var texture=new Texture2D(800,800,TextureFormat.RGB24,false);
                try{camera.targetTexture=target;camera.rect=new Rect(0,0,1,1);camera.aspect=1;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,800,800),0,0);texture.Apply();System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,name+".png"),texture.EncodeToPNG());}
                finally{camera.targetTexture=previous;camera.rect=rect;camera.aspect=aspect;RenderTexture.active=active;target.Release();UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(texture);}
            }
            var mineLod=world.GraphicsRoot.GetComponentsInChildren<ModelDetailLod>().Single(l=>l.name.StartsWith("Trap mine B "));
            for(int tier=0;tier<3;tier++){mineLod.SetDetail(tier);Assert.AreEqual(tier,mineLod.Level);WorldFrame("mine-model-lod-"+tier);}mineLod.Select(camera);
            type.GetMethod("MarkMinesForAreaDamage",F).Invoke(domain,new object[]{new BallisticPoint(point.X,world.Point(point).y+.1,point.Z),.1d});
            type.GetMethod("AdvanceMines",F).Invoke(domain,null);
            Runtime.RequestPause(false);yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").MineBlasts.Count==1,"mine area detonation");Runtime.RequestPause(true);yield return Wait(()=>Runtime.Latest.Paused,"freeze burst");
            camera.orthographicSize=2.5f;camera.transform.position=world.Point(point)+new Vector3(0,4,-4);camera.transform.LookAt(world.Point(point));
            yield return Capture("mine-ground-burst",captureWidth:1600,captureHeight:1000,captureChecks:()=>Assert.AreEqual(1.4,Get<PlayableSnapshot>(seat,"view").MineBlasts.Single().Radius));
            WorldFrame("mine-burst-world");
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        [UnityTest] public IEnumerator MineGamepadCancelAndConfirmationStayOwnerBound()
        {
            yield return StartOrdinary(2);var seat=Seats[0];var other=Seats[1];var pad=pads[0];
            Runtime.RequestPause(true);yield return Wait(()=>Runtime.Latest.Paused,"seed gamepad mine");
            var owner=Get<PlayableSnapshot>(seat,"view").Owner;var domain=typeof(PlayableRuntime).GetField("domain",F).GetValue(Runtime);var type=domain.GetType();
            type.GetMethod("CreditOrbital",F).Invoke(domain,new object[]{(PlayableOwner?)owner,Get<PlayableSnapshot>(other,"view").Owner,250d});
            type.GetMethod("ChooseOrbital",F).Invoke(domain,new object[]{owner,2});Runtime.RequestPause(false);yield return Wait(()=>Get<PlayableSnapshot>(seat,"view").Orbital.Abilities.Count==1,"gamepad mine ready");
            Put(seat,"battleController",true);Assert.True((bool)Call(seat,"OpenOrbital",true));Call(seat,"SubmitOrbital",false,2);InputSystem.Update();Call(seat,"PollOrdinaryPad");Call(seat,"PollOrdinaryPad");
            ButtonFrame(pad,pad.buttonEast,true);ButtonFrame(pad,pad.buttonEast,false);Assert.False(Get<bool>(seat,"scanTargeting"));Assert.Zero(Get<PlayableSnapshot>(seat,"view").Orbital.Abilities.Single().Remaining);
            Assert.True((bool)Call(seat,"OpenOrbital",true));Call(seat,"SubmitOrbital",false,2);InputSystem.Update();Call(seat,"PollOrdinaryPad");Call(seat,"PollOrdinaryPad");
            var at=Get<PlayableSnapshot>(seat,"view").Entities.First(e=>e.Owner==owner).Position;PadCursor(seat,new NavPoint(at.X,at.Z+3));
            var otherCamera=Get<Camera>(other,"cameraView").transform.position;ButtonFrame(pad,pad.buttonSouth,true);ButtonFrame(pad,pad.buttonSouth,false);
            yield return Wait(()=>!Get<bool>(seat,"scanTargeting"),"gamepad mine receipt");Assert.AreEqual(1,Get<PlayableSnapshot>(seat,"view").Mines.Count);Assert.AreEqual(otherCamera,Get<Camera>(other,"cameraView").transform.position);
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
    }
}
