using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Input;
using Spacewars.Simulation;
using Spacewars.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        private string RallyDiagnostics()
        {
            if(Runtime==null)return "";
            var domain=typeof(PlayableRuntime).GetField("domain",F).GetValue(Runtime);
            var nav=(NavigationSession)domain.GetType().GetProperty("Navigation",F).GetValue(domain);
            var corridor=typeof(NavigationSession).GetProperty("LastCorridorFailure",F).GetValue(nav);
            return " tick="+Runtime.Latest.Tick+" pending="+nav.PendingCount+" requests="+nav.Requests.Count+" rejected="+nav.RejectedResults+" corridor="+corridor+" scheduler="+string.Join(",",typeof(NavSchedulerCounters).GetProperties().Select(prop=>prop.Name+":"+prop.GetValue(Get<UnityHostRouteService>(app,"routeService").CertifiedCounters)))+" projection="+Get<UnityHostRouteService>(app,"routeService")?.LastProjectionFailure+" points="+string.Join(";",Runtime.Latest.Buildings.Where(b=>b.PrivateState?.PendingRally!=null).Select(b=>b.Id+":"+b.PrivateState.PendingRally+":"+b.PrivateState.HasRally));
        }
        private Vector2 RallyMapScreen(PlayableBootstrap seat,int kind,NavPoint point)
        {
            var map=Get<PlayableMapSurface>(seat,kind==2?"tacticalMap":"compactMap");var profile=Get<PlayableProfile>(seat,"profile");
            var uv=new PlayableMapTransform(profile.ArenaHalfExtent,profile.ArenaHalfExtent).Project(point);
            var panel=map.worldBound.position+new Vector2((float)uv.X*map.worldBound.width,(float)uv.Z*map.worldBound.height);
            float scale=host.GetComponent<UIDocument>().panelSettings.scale;
            return new Vector2(panel.x*scale,Screen.height-panel.y*scale);
        }
        [UnityTest] public IEnumerator MouseKeepsProducerSelectionHidesDistantMenuAndUnitCircles()
        {
            yield return StartOrdinary(1);Put(app,"battleController",false);
            var view=Get<PlayableSnapshot>(app,"view");var building=view.Buildings.First(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Headquarters);
            Selection(app).Clear();Selection(app).Add(building.Id);Put(app,"battleRally",false);
            var camera=Get<Camera>(app,"cameraView");var world=Get<PlayableWorld>(app,"world");var screen=(Vector2)camera.WorldToScreenPoint(world.Point(building.Position));
            Set(Mouse.current.position,screen);InputSystem.Update();Call(app,"UpdateHud");yield return null;Call(app,"UpdateHud");
            Assert.AreEqual(DisplayStyle.Flex,Get<OrbitalBattleRing>(app,"battleRing").style.display.value);
            Set(Mouse.current.position,new Vector2(Screen.width-2,Screen.height-2));InputSystem.Update();Call(app,"UpdateHud");
            Assert.AreEqual(DisplayStyle.None,Get<OrbitalBattleRing>(app,"battleRing").style.display.value);CollectionAssert.AreEqual(new[]{building.Id},Selection(app));
            yield return Capture("mouse-distant-building",()=>{Put(app,"battleController",false);Set(Mouse.current.position,new Vector2(Screen.width-2,Screen.height-2));Call(app,"UpdateHud");});
            Set(Mouse.current.position,screen);InputSystem.Update();Call(app,"UpdateHud");Assert.AreEqual(DisplayStyle.Flex,Get<OrbitalBattleRing>(app,"battleRing").style.display.value);
            var unit=view.Entities.First(e=>e.Owner==view.Owner);Selection(app).Clear();Selection(app).Add(unit.Id);Call(app,"Render");
            Assert.IsFalse(world.Actors[unit.Id].Selection.activeSelf);CollectionAssert.AreEqual(new[]{unit.Id},Selection(app));
            Put(app,"battleController",true);Call(app,"Render");Assert.IsTrue(world.Actors[unit.Id].Selection.activeSelf);
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        [UnityTest] public IEnumerator MouseRallyUsesFieldCompactAndTacticalMapForProducerKinds()
        {
            yield return StartOrdinary(1);Put(app,"battleController",false);var original=Get<PlayableSnapshot>(app,"view");var own=original.Buildings.First(b=>b.Owner==original.Owner&&b.Kind==PlayableBuildingKind.Headquarters);
            foreach(var kind in new[]{PlayableBuildingKind.Headquarters,PlayableBuildingKind.Outpost,PlayableBuildingKind.Factory})
            foreach(int mapKind in new[]{0,1,2})
            {
                var current=Runtime.Latest;var producer=new PlayableBuildingSnapshot(own.Id,current.Owner,kind,own.Position,own.Health,1,0,0,default);
                // Presentation variants share a real authoritative HQ ID; authority/routing is executed, not copied.
                var view=new PlayableSnapshot(current.ProfileId,current.ProfileRevision,current.Generation,current.Seed,current.Sequence,current.Tick,current.Status,false,current.Outcome,current.Credits,current.Geometry,current.Entities.ToArray(),new[]{producer},current.Projectiles.ToArray(),current.Metrics,null,vision:current.Vision,owner:current.Owner,ownerId:current.OwnerId);
                Put(app,"view",view);Selection(app).Clear();Selection(app).Add(own.Id);var point=new NavPoint(own.Position.X+6,own.Position.Z);
                Call(app,"UpdateMaps");if(mapKind==2){Call(app,"ToggleMap");yield return null;yield return null;Call(app,"UpdateMaps");}
                if(mapKind==0){var camera=Get<Camera>(app,"cameraView");var screen=(Vector2)camera.WorldToScreenPoint(Get<PlayableWorld>(app,"world").Point(point));Call(app,"Order",screen,false,false);}
                else{var screen=RallyMapScreen(app,mapKind,point);var converted=(NavPoint)Call(app,"MapGround",mapKind,screen);Assert.AreEqual(point.X,converted.X,.01);Assert.AreEqual(point.Z,converted.Z,.01);Call(app,"MapOrder",mapKind,screen,false,false);}
                long sequence=Get<long>(app,"sequence");Assert.Greater(sequence,0);Call(app,"CloseMap");
                yield return Wait(()=>Runtime.OfflineFrame.Receipts.Any(r=>r.Receipt.Sequence==sequence&&r.Receipt.OwnerId==current.OwnerId&&r.Receipt.Status==PlayableCommandStatus.Applied),"mouse rally "+kind+" map "+mapKind);
                Call(app,"UpdateFrame");var applied=Get<PlayableSnapshot>(app,"view").Buildings.Single(b=>b.Id==own.Id);
                Assert.IsTrue(applied.PrivateState.HasRally);Assert.AreEqual(point.X,applied.Rally.X,.01);Assert.AreEqual(point.Z,applied.Rally.Z,.01);
                Assert.AreEqual(1,Get<PlayableMapSurface>(app,"compactMap").RallyFlagCount);
            }
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        [UnityTest] public IEnumerator PadFlagPlacementSurvivesMapTogglesReturnsToProducerAndCancelsSafely()
        {
            yield return StartOrdinary(2);var seat=Seats[1];var other=Seats[0];var view=Get<PlayableSnapshot>(seat,"view");var own=view.Buildings.First(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Headquarters);
            Selection(seat).Clear();Selection(seat).Add(own.Id);PadCursor(seat,own.Position);Call(app,"UpdateFrame");
            Call(seat,"ExecuteBattleAction","rally",false,true);Call(app,"UpdateFrame");
            Assert.IsTrue(Get<bool>(seat,"battleRally"));var world=Get<PlayableWorld>(seat,"world");Assert.IsNotNull(world.RallyPreview);Assert.IsTrue(world.RallyPreview.gameObject.activeSelf);
            AssertFlagColor(world.RallyPreview,(Color)Call(seat,"LobbyPaint",view.Owner));
            Assert.IsNull(Get<PlayableWorld>(other,"world").RallyPreview);Assert.AreEqual(Get<Camera>(seat,"cameraView").cullingMask,1<<world.RallyPreview.gameObject.layer);
            Assert.AreEqual(DisplayStyle.None,Get<VisualElement>(seat,"battleCursorVisual").style.display.value);
            yield return Capture("pad-world-flag-preview");
            Tap(pads[1],pads[1].selectButton);Assert.IsTrue(Get<bool>(seat,"mapOpen"));Assert.AreEqual("rallyMap",Get<OfflinePadGestures>(seat,"battleGestures").Mode);
            Assert.IsTrue(Get<bool>(seat,"battleRally"));Assert.IsFalse(world.RallyPreview.gameObject.activeSelf);
            Tap(pads[1],pads[1].selectButton);Assert.IsFalse(Get<bool>(seat,"mapOpen"));Assert.AreEqual("rallyTarget",Get<OfflinePadGestures>(seat,"battleGestures").Mode);
            Tap(pads[1],pads[1].selectButton);Assert.IsTrue(Get<bool>(seat,"mapOpen"));
            var point=new NavPoint(own.Position.X+6,own.Position.Z);PadCursor(seat,point);Call(app,"UpdateFrame");
            Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>(seat,"battleCursorVisual").style.display.value);
            Assert.AreSame(Resources.Load<Texture2D>("OrbitalIcons/rally"),Get<VisualElement>(seat,"battleCursorVisual").style.backgroundImage.value.texture);
            Assert.IsEmpty(Get<VisualElement>(seat,"tacticalOverlay").Query<Label>().ToList(),"Rally map keeps the requested hint-free layout.");
            yield return Capture("pad-tactical-flag-preview");AssertTacticalScreen(seat);Tap(pads[1],pads[1].buttonSouth);long sequence=Get<long>(seat,"sequence");
            Assert.IsFalse(Get<bool>(seat,"battleRally"));Assert.IsFalse(Get<bool>(seat,"mapOpen"));Assert.AreEqual("buildingWheel",Get<OfflinePadGestures>(seat,"battleGestures").Mode);CollectionAssert.AreEqual(new[]{own.Id},Selection(seat));
            var centered=(NavPoint)Call(seat,"Ground",Get<Camera>(seat,"cameraView").pixelRect.center);Assert.AreEqual(own.Position.X,centered.X,.05);Assert.AreEqual(own.Position.Z,centered.Z,.05);
            yield return Wait(()=>Runtime.OfflineFrame.Receipts.Any(r=>r.Receipt.Sequence==sequence&&r.Receipt.OwnerId==view.OwnerId&&r.Receipt.Status==PlayableCommandStatus.Applied),"pad map rally");Call(app,"UpdateFrame");
            var applied=Get<PlayableSnapshot>(seat,"view").Buildings.Single(b=>b.Id==own.Id);Assert.IsTrue(applied.PrivateState.HasRally);Assert.AreEqual(point.X,applied.Rally.X,.01);Assert.AreEqual(point.Z,applied.Rally.Z,.01);
            AssertFlagColor(world.RallyFlags[own.Id],(Color)Call(seat,"LobbyPaint",view.Owner));
            Assert.AreEqual(DisplayStyle.Flex,Get<OrbitalBattleRing>(seat,"battleRing").style.display.value);yield return Capture("pad-rally-confirmed-menu");
            Call(seat,"ExecuteBattleAction","rally",false,true);Call(app,"UpdateFrame");Tap(pads[1],pads[1].selectButton);Tap(pads[1],pads[1].buttonEast);Call(app,"UpdateFrame");
            Assert.IsFalse(Get<bool>(seat,"battleRally"));Assert.IsFalse(Get<bool>(seat,"mapOpen"));Assert.AreEqual(sequence,Get<long>(seat,"sequence"));
            var unchanged=Get<PlayableSnapshot>(seat,"view").Buildings.Single(b=>b.Id==own.Id);Assert.AreEqual(point.X,unchanged.Rally.X,.01);Assert.AreEqual(point.Z,unchanged.Rally.Z,.01);
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
        private static void AssertFlagColor(Transform flag,Color expected)
        {
            int checkedSlots=0;
            foreach(var renderer in flag.GetComponentsInChildren<Renderer>(true))for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)
            {
                if(!renderer.sharedMaterials[slot].name.EndsWith("team-primary"))continue;
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);Assert.IsTrue(block.GetColor("_BaseColor")==expected,"Material owner color must match the lobby color (Unity Color float comparison)");checkedSlots++;
            }
            Assert.Greater(checkedSlots,0);
        }
    }
}
