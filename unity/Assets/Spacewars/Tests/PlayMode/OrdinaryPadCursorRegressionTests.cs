using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Input;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        private Vector2 PadMapScreen(PlayableBootstrap seat,PlayableMapSurface surface,NavPoint point)
        {
            var p=Get<PlayableProfile>(seat,"profile");var uv=new PlayableMapTransform(p.ArenaHalfExtent,p.ArenaHalfExtent).Project(point);
            var panel=surface.worldBound.position+new Vector2((float)uv.X*surface.worldBound.width,(float)uv.Z*surface.worldBound.height);
            var origin=(Vector2)Call(seat,"GlobalPanelPoint",Vector2.zero);
            var right=(Vector2)Call(seat,"GlobalPanelPoint",Vector2.right)-origin;
            var up=(Vector2)Call(seat,"GlobalPanelPoint",Vector2.up)-origin;
            return new Vector2((panel.x-origin.x)/right.x,(panel.y-origin.y)/up.y);
        }
        [UnityTest] public IEnumerator NeutralWorldCursorStaysOnWaterAndMovesInScreenAxes()
        {
            yield return StartOrdinary(1,mapName:"Переправа (4)");var camera=Get<Camera>(app,"cameraView");
            Call(app,"FocusPadPoint",new NavPoint(0,22));Put(app,"padCursorInitialized",true);Put(app,"battleController",true);
            var center=camera.pixelRect.center;Put(app,"battleCursor",center);
            var p=Get<PlayableProfile>(app,"profile");Assert.False(p.AuthoredMap.SupportsFootprint((NavPoint)Call(app,"Ground",center),0),"Fixture must really point at water");
            for(int i=0;i<4;i++){yield return null;Call(app,"UpdateFrame");Assert.Less(Vector2.Distance(center,Get<Vector2>(app,"battleCursor")),.01f,"A neutral screen cursor must not be reprojected by water/terrain");}
            Set(pads[0].rightStick,Vector2.right);InputSystem.Update();yield return null;Call(app,"UpdateFrame");var moved=Get<Vector2>(app,"battleCursor");Assert.Greater(moved.x,center.x);Assert.That(moved.y,Is.EqualTo(center.y).Within(.01f));
            Set(pads[0].rightStick,Vector2.zero);InputSystem.Update();yield return null;Call(app,"UpdateFrame");Assert.Less(Vector2.Distance(moved,Get<Vector2>(app,"battleCursor")),.01f);
        }
        [UnityTest] public IEnumerator CompactMapPadOrderUsesMapCoordinates()=>CompactMapPadOrder(1);
        [UnityTest] public IEnumerator SharedCompactMapPadOrderStaysWithItsSeat()=>CompactMapPadOrder(2);
        private IEnumerator CompactMapPadOrder(int humans)
        {
            yield return StartOrdinary(humans);var seat=Seats[humans-1];var v=Get<PlayableSnapshot>(seat,"view");var own=v.Entities.First(e=>e.Owner==v.Owner);
            Selection(seat).Add(own.Id);var map=(PlayableMapSurface)typeof(PlayableBootstrap).GetProperty("CompactInputMap",F).GetValue(seat);
            var screen=PadMapScreen(seat,map,own.Position);Assert.AreEqual(1,(int)Call(seat,"MapAt",screen),"The pad pointer must really hit this seat's compact/shared map");
            var expected=(NavPoint)Call(seat,"MapGround",1,screen);var below=(NavPoint)Call(seat,"Ground",screen);Assert.Greater(Math.Abs(expected.X-below.X)+Math.Abs(expected.Z-below.Z),1);
            Put(seat,"battleCursor",screen);Put(seat,"padCursorInitialized",true);Put(seat,"battleController",true);
            long before=Get<long>(seat,"sequence");Tap(pads[humans-1],pads[humans-1].buttonEast);long submitted=Get<long>(seat,"sequence");Assert.AreEqual(before+1,submitted);
            yield return Wait(()=>Runtime.OfflineFrame.Receipts.Any(r=>r.Receipt.OwnerId==v.OwnerId&&r.Receipt.Sequence==submitted&&r.Receipt.Status==PlayableCommandStatus.Applied),"compact map pad order");
            var entity=Runtime.ParticipantView(v.OwnerId).Entities.Single(e=>e.Id==own.Id);var target=entity.Queue?.Pending?.Anchor??entity.Queue?.Active?.Anchor??entity.Queue?.Completion?.OriginalLocation;
            Assert.True(target.HasValue,"The applied command must expose its authoritative target");Assert.That(target.Value.Position.X,Is.EqualTo(expected.X).Within(.01));Assert.That(target.Value.Position.Z,Is.EqualTo(expected.Z).Within(.01));
            Assert.Less(Vector2.Distance(screen,Get<Vector2>(seat,"battleCursor")),.01f,"Destination resolution must not move the pointer");
            if(humans>1){
                var frame=Get<VisualElement>(app,"sharedMapFrame");var cursor=Get<VisualElement>(seat,"battleCursorVisual");
                Assert.AreSame(frame,cursor.parent,"Shared-map cursor must remain visible outside the owning viewport clip");
                Assert.Greater(frame.IndexOf(cursor),frame.IndexOf(map),"Map rendering must not cover its controller cursor");
            }
        }
        [UnityTest] public IEnumerator TacticalMapCursorSpeedIsIndependentAndMatchesSettings()
        {
            yield return StartOrdinary(1);var p=Get<PlayableProfile>(app,"profile");
            var controls=(NativeLocalControlSettings)typeof(PlayableBootstrap).GetProperty("LocalControlSettings",F).GetValue(app);
            float worldSpeed=controls.CursorSpeed;var settings=new VisualElement();Get<VisualElement>(app,"root").Add(settings);controls.AddSliders(settings);
            Assert.AreEqual(.75f,controls.MapCursorSpeed);settings.Q<Slider>("settings-pad-mapCursorSpeed").value=1;
            Assert.AreEqual(1,controls.MapCursorSpeed);Assert.AreEqual(worldSpeed,controls.CursorSpeed);Assert.AreEqual(.75f,Get<NativeLocalInputProfile>(app,"battleInputProfile").mapCursorSpeed);
            Call(app,"ToggleMap");Call(app,"UpdateFrame");PadCursor(app,default);
            Set(pads[0].leftStick,Vector2.right);InputSystem.Update();yield return null;
            float dt=Time.unscaledDeltaTime;Call(app,"UpdateFrame");var moved=Get<NavPoint>(app,"padCursorGround");
            Assert.That(moved.X,Is.EqualTo(Math.Min(p.ArenaHalfExtent,2*p.ArenaHalfExtent*controls.MapCursorSpeed*dt)).Within(.01));Assert.That(moved.Z,Is.EqualTo(0).Within(.01));
            Set(pads[0].leftStick,Vector2.zero);InputSystem.Update();
        }
        [UnityTest] public IEnumerator WholeArmyFocusCentersLargestViewportClusterWithoutDroppingSelection()
        {
            yield return StartOrdinary(2);var seat=Seats[1];var otherCamera=Get<Camera>(Seats[0],"cameraView");var otherBefore=otherCamera.transform.position;
            var v=Get<PlayableSnapshot>(seat,"view");var anchor=v.Entities.First(e=>e.Owner==v.Owner).Position;
            PlayableEntitySnapshot Unit(int id,double x,double z,PlayableOwner owner)=>new PlayableEntitySnapshot(id,owner,PlayableEntityKind.Tank,new NavPoint(x,z),100,false,0,0,0);
            var own=new[]{Unit(701,anchor.X,anchor.Z,v.Owner),Unit(702,anchor.X+2,anchor.Z,v.Owner),Unit(703,anchor.X,anchor.Z+2,v.Owner),Unit(704,anchor.X+200,anchor.Z,v.Owner)};
            var all=own.Concat(Enumerable.Range(0,5).Select(i=>Unit(800+i,anchor.X+250+i,anchor.Z,v.Owner==PlayableOwner.Player?PlayableOwner.Enemy:PlayableOwner.Player))).ToArray();
            var fake=new PlayableSnapshot(v.ProfileId,v.ProfileRevision,v.Generation,v.Seed,v.Sequence,v.Tick,v.Status,false,PlayableMatchOutcome.Playing,v.Credits,v.Geometry,all,Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),v.Metrics,null,owner:v.Owner,ownerId:v.OwnerId,activeProfile:v.ActiveProfile);
            Call(seat,"FocusPadPoint",own[3].Position);Put(seat,"view",fake);
            Assert.True((bool)Call(seat,"OrdinaryPadMenuIntent",new OfflinePadIntent("selectAllArmy"),Array.Empty<OfflinePadAction>()));
            CollectionAssert.AreEquivalent(own.Select(e=>e.Id),Selection(seat));Assert.AreEqual(otherBefore,otherCamera.transform.position);
            var camera=Get<Camera>(seat,"cameraView");var world=Get<PlayableWorld>(seat,"world");var points=own.Take(3).Select(e=>camera.WorldToScreenPoint(world.Point(e.Position))).ToArray();
            var center=new Vector2((points.Min(p=>p.x)+points.Max(p=>p.x))/2,(points.Min(p=>p.y)+points.Max(p=>p.y))/2);
            Assert.Less(Vector2.Distance(center,camera.pixelRect.center),.1f);Assert.Less(Vector2.Distance(Get<Vector2>(seat,"battleCursor"),camera.pixelRect.center),.01f);
        }
    }
}
