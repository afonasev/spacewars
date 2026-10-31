using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        // Controlled presentation evidence, deliberately separate from the failed natural-action witness.
        [UnityTest] public IEnumerator ControlledAiSignalsAndPointerClickKeepSpectatorRole()
        {
            yield return StartOrdinary(0,configureRoster:d=>{foreach(var participant in d.Participants)participant.Human=false;});
            Call(app,"CreateSpectatorHud");Call(app,"UpdateFrame");
            Call(app,"SetSpectatorPerspective",PlayableOwner.Player);Call(app,"UpdateFrame");
            var view=Get<PlayableSnapshot>(app,"view");var camera=Get<Camera>(app,"cameraView");
            var home=view.Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters).Position;
            var world=Get<PlayableWorld>(app,"world");camera.orthographicSize=20;camera.transform.position=world.Point(home)+new Vector3(0,30,-24);camera.transform.LookAt(world.Point(home));
            var center=(NavPoint)Call(app,"Ground",camera.pixelRect.center);var goal=new NavPoint(center.X+10,center.Z+4);
            var setup=Get<NativeLobbyConfiguration>(app,"matchSetup");
            var a=new AiCommunication(10001,view.Generation,view.Tick,"player-1",setup.Participants[0].Team,"Выдвигаюсь в наступление к отмеченной позиции.",goal,TeamSignalKind.Attack,true);
            var b=new AiCommunication(10002,view.Generation,view.Tick,"foundry-4",setup.Participants[3].Team,"У базы противник. Нужна защита.",new NavPoint(center.X-5,center.Z+5),TeamSignalKind.Defend,true);
            Assert.True((bool)Call(app,"PresentAiCommunication",a));Assert.True((bool)Call(app,"PresentAiCommunication",b));Call(app,"UpdateFrame");yield return null;
            var chat=Get<NativeMatchChat>(app,"matchChat");Assert.True(chat.Visible(-1,true,Time.unscaledTimeAsDouble).Any(m=>m.Text==a.Text));Assert.True(chat.Visible(-1,true,Time.unscaledTimeAsDouble).Any(m=>m.Text==b.Text));
            Assert.AreEqual(setup.ParticipantName(0),chat.Visible(-1,true,Time.unscaledTimeAsDouble).Single(m=>m.Text==a.Text).Name);
            var feed=Get<VisualElement>(app,"chatFeed");var row=feed.Query<Label>("chat-message").ToList().Single(l=>l.text.Contains(a.Text));
            Assert.AreEqual(PickingMode.Position,row.pickingMode);Assert.True((bool)Call(app,"ChatMessageAtPanel",row.worldBound.center));
            var perspective=Get<PlayableOwner?>(app,"spectatorPerspective");var selection=Selection(app).ToArray();
            using(var click=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=row.worldBound.center})){click.target=row;row.SendEvent(click);}
            var focus=(NavPoint)Call(app,"Ground",camera.pixelRect.center);Assert.AreEqual(goal.X,focus.X,.1);Assert.AreEqual(goal.Z,focus.Z,.1);
            Assert.AreEqual(perspective,Get<PlayableOwner?>(app,"spectatorPerspective"));CollectionAssert.AreEqual(selection,Selection(app));
            Assert.False(Get<Spacewars.Input.PlayableInput>(app,"input").CommandInputEnabled);
            yield return Capture("controlled-ai-chat-signals",()=>{Call(app,"PresentAiCommunication",new AiCommunication(10003,view.Generation,view.Tick,"foundry-2",setup.Participants[1].Team,"Разведываю отмеченный район.",new NavPoint(goal.X+4,goal.Z),TeamSignalKind.Recon,true));});
            Call(app,"SetSpectatorPerspective",PlayableOwner.Enemy);Call(app,"UpdateFrame");
            Assert.True(chat.Visible(-1,true,Time.unscaledTimeAsDouble).Any(m=>m.Text==a.Text));Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(app,"signalWorldLayer").resolvedStyle.display);
            Call(app,"ToggleMap");Call(app,"UpdateFrame");Assert.Greater(Get<PlayableMapSurface>(app,"tacticalMap").TeamSignalCount,0);
            yield return Capture("controlled-ai-chat-tactical");
            Call(app,"ResetChat");Assert.IsEmpty(Get<NativeMatchChat>(app,"matchChat").Visible(-1,true,Time.unscaledTimeAsDouble));Assert.IsEmpty(Get<NativeTeamSignals>(app,"teamSignals").Visible(0,true,Get<PlayableSnapshot>(app,"view").Tick));
        }

        [UnityTest] public IEnumerator AiChatPointerFocusPreservesHeightAndCentersDestinationTerrace()
        {
            yield return StartOrdinary(0,configureRoster:d=>{foreach(var participant in d.Participants)participant.Human=false;});
            Call(app,"CreateSpectatorHud");Call(app,"SetSpectatorPerspective",PlayableOwner.Player);Call(app,"UpdateFrame");
            var view=Get<PlayableSnapshot>(app,"view");var camera=Get<Camera>(app,"cameraView");var world=Get<PlayableWorld>(app,"world");
            var home=view.Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters).Position;
            camera.orthographicSize=20;camera.transform.position=world.Point(home)+new Vector3(0,30,-24);camera.transform.LookAt(world.Point(home));
            var goal=new NavPoint(0,-80);float height=camera.transform.position.y;var rotation=camera.transform.rotation;float zoom=camera.orthographicSize;
            var setup=Get<NativeLobbyConfiguration>(app,"matchSetup");
            Assert.True((bool)Call(app,"PresentAiCommunication",new AiCommunication(20001,view.Generation,view.Tick,"foundry-5",setup.Participants[4].Team,"Выдвигаюсь к отмеченной позиции.",goal,TeamSignalKind.Attack,true)));
            Call(app,"UpdateFrame");yield return null;
            var row=Get<VisualElement>(app,"chatFeed").Query<Label>("chat-message").ToList().Single(l=>l.text.Contains("Выдвигаюсь к отмеченной позиции."));
            var perspective=Get<PlayableOwner?>(app,"spectatorPerspective");var selection=Selection(app).ToArray();
            using(var click=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=row.worldBound.center})){click.target=row;row.SendEvent(click);}
            var focus=(NavPoint)Call(app,"Ground",camera.pixelRect.center);Assert.AreEqual(goal.X,focus.X,.1);Assert.AreEqual(goal.Z,focus.Z,.1);
            Assert.AreEqual(height,camera.transform.position.y);Assert.AreEqual(rotation,camera.transform.rotation);Assert.AreEqual(zoom,camera.orthographicSize);
            var viewport=camera.WorldToViewportPoint(world.Point(goal));Assert.AreEqual(.5,viewport.x,.001);Assert.AreEqual(.5,viewport.y,.001);
            Assert.AreEqual(perspective,Get<PlayableOwner?>(app,"spectatorPerspective"));CollectionAssert.AreEqual(selection,Selection(app));
            yield return Capture("ai-chat-terrace-focus");
        }

        [UnityTest,Timeout(360000)] public IEnumerator AiMessagesAndSignalsReachLocalSpectatorThroughOrdinaryHost()
        {
            yield return StartOrdinary(0,configureRoster:d=>{foreach(var participant in d.Participants)participant.Human=false;});
            Call(app,"CreateSpectatorHud");Call(app,"UpdateFrame");
            var runtime=Runtime;var chat=Get<NativeMatchChat>(app,"matchChat");
            Assert.True(Get<bool>(app,"spectatorMode"));
            yield return Wait(()=>runtime.AiCommunications(PlayableRuntime.LocalSpectatorId).Select(e=>e.Team).Distinct().Count()>=2,"AI openings");
            Call(app,"UpdateFrame");
            var initial=chat.Visible(-1,true,Time.unscaledTimeAsDouble).Where(m=>m.IsAi).ToArray();
            Assert.GreaterOrEqual(initial.Select(m=>m.Team).Distinct().Count(),2,"Both teams must reach the spectator");
            Call(app,"SetSpectatorPerspective",PlayableOwner.Player);Call(app,"UpdateFrame");
            Assert.GreaterOrEqual(chat.Visible(-1,true,Time.unscaledTimeAsDouble).Where(m=>m.IsAi).Select(m=>m.Team).Distinct().Count(),2);
            runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
            float end=Time.realtimeSinceStartup+300;const long endTick=6000;NativeMatchChat.Message target=null;
            long startTick=runtime.Latest.Tick;
            try
            {
            while(runtime.Latest.Tick<endTick&&Time.realtimeSinceStartup<end)
            {
                yield return null;Call(app,"UpdateFrame");
                target=chat.Visible(-1,true,Time.unscaledTimeAsDouble).LastOrDefault(m=>m.IsAi&&m.Point.HasValue);
                if(target!=null)break;
                Assert.IsNull(runtime.Latest.Failure,"Ordinary host failure is a STOP, never an invitation to retry");
            }
            }
            finally
            {
                string output=Environment.GetEnvironmentVariable("ORDINARY_PAD_EVIDENCE");
                if(!string.IsNullOrEmpty(output))
                {
                    System.IO.Directory.CreateDirectory(output);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(output,"ai-chat-diagnostic.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new {
                        Seed=19092026,HorizonTick=endTick,WallCap=300,Rate=4,StartTick=startTick,EndTick=runtime.Latest.Tick,
                        Elapsed=Time.realtimeSinceStartup-(end-300),runtime.Latest.Paused,runtime.Latest.Failure,runtime.Latest.Outcome,
                        HostPaused=Get<bool>(app,"paused"),Preparing=Get<bool>(app,"preparing"),runtime.IsStopped,
                        Narrator=runtime.AiCommunicationDiagnostics(PlayableRuntime.LocalSpectatorId).Select(Newtonsoft.Json.Linq.JObject.Parse),
                        Transport=runtime.AiCommunications(PlayableRuntime.LocalSpectatorId),
                        Chat=chat.Visible(-1,true,Time.unscaledTimeAsDouble).Where(m=>m.IsAi).Select(m=>new{m.Text,m.Team,m.Point}),
                        TargetFound=target!=null
                    },Newtonsoft.Json.Formatting.Indented));
                }
            }
            Assert.NotNull(target,"Ordinary AI must publish a positional action/defense within the explicitly authorized tick6000/300s witness");
            var signals=Get<NativeTeamSignals>(app,"teamSignals");
            Assert.True(signals.Visible(0,true,Get<PlayableSnapshot>(app,"view").Tick).Any(s=>s.Point.Equals(target.Point.Value)),"Message and mark share the same goal");
            Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(app,"signalWorldLayer").resolvedStyle.display);
            var compact=Get<PlayableMapSurface>(app,"compactMap");Assert.Greater(compact.TeamSignalCount,0);
            var selection=Selection(app).ToArray();var perspective=Get<PlayableOwner?>(app,"spectatorPerspective");
            var camera=Get<Camera>(app,"cameraView");
            yield return null;
            var row=Get<VisualElement>(app,"chatFeed").Query<Label>("chat-message").ToList().Single(l=>l.text.Contains(target.Text));
            Assert.AreEqual(PickingMode.Position,row.pickingMode);Assert.True((bool)Call(app,"ChatMessageAtPanel",row.worldBound.center));
            using(var click=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=row.worldBound.center})){click.target=row;row.SendEvent(click);}
            Assert.AreEqual(perspective,Get<PlayableOwner?>(app,"spectatorPerspective"));CollectionAssert.AreEqual(selection,Selection(app));
            var focus=(NavPoint)Call(app,"Ground",camera.pixelRect.center);
            Assert.AreEqual(target.Point.Value.X,focus.X,.1);Assert.AreEqual(target.Point.Value.Z,focus.Z,.1);
            yield return Capture("ai-chat-signal-spectator");
            Call(app,"SetSpectatorPerspective",PlayableOwner.Enemy);Call(app,"UpdateFrame");
            Assert.True(chat.Visible(-1,true,Time.unscaledTimeAsDouble).Any(m=>m.IsAi&&m.Team==target.Team),"Perspective must not remove another team's AI line");
            Call(app,"ToggleMap");Call(app,"UpdateFrame");Assert.Greater(Get<PlayableMapSurface>(app,"tacticalMap").TeamSignalCount,0);
            yield return Capture("ai-chat-signal-tactical");
            Call(app,"ResetChat");Assert.IsEmpty(Get<NativeMatchChat>(app,"matchChat").Visible(-1,true,Time.unscaledTimeAsDouble));Assert.IsEmpty(Get<NativeTeamSignals>(app,"teamSignals").Visible(0,true,Get<PlayableSnapshot>(app,"view").Tick));
        }
    }
}
