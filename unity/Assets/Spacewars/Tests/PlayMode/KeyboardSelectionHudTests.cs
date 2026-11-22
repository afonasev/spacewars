using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Input;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed class KeyboardSelectionHudTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        GameObject go,cameraObject;PlayableBootstrap hud;Camera camera;
        void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,F).SetValue(hud,value);
        T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,F).GetValue(hud);
        object Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(hud,args);
        HashSet<int> Selection=>Get<HashSet<int>>("selection");
        PlayableSnapshot Snapshot(PlayableEntitySnapshot[] units,PlayableBuildingSnapshot[] buildings)=>new PlayableSnapshot("test",1,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,units,buildings,Array.Empty<PlayableProjectileSnapshot>(),null,null);
        static PlayableEntitySnapshot Unit(int id,PlayableEntityKind kind,double x,double z,PlayableOwner owner=PlayableOwner.Player)=>new PlayableEntitySnapshot(id,owner,kind,new NavPoint(x,z),100,false,0,0,0);
        static PlayableBuildingSnapshot Building(int id,double x,double z)=>new PlayableBuildingSnapshot(id,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(x,z),100,1,0,0,default);
        Vector2 Screen(NavPoint point,float up=.5f)=>camera.WorldToScreenPoint(new Vector3((float)point.X,up,(float)point.Z));
        [SetUp] public void Setup()
        {
            go=new GameObject("keyboard selection fixture");hud=go.AddComponent<PlayableBootstrap>();hud.enabled=false;Set("profile",PlayableProfile.Default);Call("CreateHud");
            cameraObject=new GameObject("selection camera");camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=15;camera.transform.position=new Vector3(0,40,-20);camera.transform.LookAt(Vector3.zero);Set("cameraView",camera);Set("world",new PlayableWorld(go.transform,PlayableProfile.Default));
            Set("view",Snapshot(new[]{Unit(1,PlayableEntityKind.Tank,-5,0),Unit(2,PlayableEntityKind.Tank,0,0),Unit(3,PlayableEntityKind.Explorer,5,0),Unit(4,PlayableEntityKind.Tank,200,0),Unit(5,PlayableEntityKind.Tank,8,0,PlayableOwner.Enemy)},new[]{Building(10,-8,-6),Building(11,8,-6)}));
        }
        [TearDown] public void Teardown(){UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(cameraObject);}
        [UnityEngine.TestTools.UnityTest] public System.Collections.IEnumerator WorldArrowNavigationKeepsHudFocusOnTheField()
        {
            var input=go.AddComponent<PlayableInput>();input.enabled=false;Set("input",input);
            input.IsKeyboardInUi=()=>Get<UnityEngine.UIElements.VisualElement>("root").focusController?.focusedElement is UnityEngine.UIElements.Button;
            yield return null;
            var root=Get<UnityEngine.UIElements.VisualElement>("root");root.Focus();
            foreach(var direction in new[]{UnityEngine.UIElements.NavigationMoveEvent.Direction.Left,UnityEngine.UIElements.NavigationMoveEvent.Direction.Right,UnityEngine.UIElements.NavigationMoveEvent.Direction.Up,UnityEngine.UIElements.NavigationMoveEvent.Direction.Down})
            {
                using(var evt=UnityEngine.UIElements.NavigationMoveEvent.GetPooled(direction)){evt.target=root;root.SendEvent(evt);}
                Assert.AreSame(root,root.focusController.focusedElement);
                Assert.AreEqual(UnityEngine.UIElements.DisplayStyle.None,Get<UnityEngine.UIElements.VisualElement>("armyComposition").style.display.value);
            }
            // Explicit F6 HUD focus and pause menus must retain their own navigation.
            Call("ToggleHudFocus");Assert.IsInstanceOf<UnityEngine.UIElements.Button>(root.focusController.focusedElement);
            Assert.False((bool)typeof(PlayableBootstrap).GetProperty("WorldKeyboardOwnsToolkit",F).GetValue(hud));
        }
        [Test] public void ModifierClicksOnlyAddUnitsAndBuildingsAlwaysReplace()
        {
            var view=Get<PlayableSnapshot>("view");var a=Screen(view.Entities[0].Position);var b=Screen(view.Entities[1].Position);var factory=Screen(view.Buildings[0].Position,1);
            Call("Select",a,a,false);Call("Select",b,b,true);Call("Select",a,a,true);CollectionAssert.AreEquivalent(new[]{1,2},Selection);
            Call("Select",factory,factory,true);CollectionAssert.AreEquivalent(new[]{10},Selection);
            Call("Select",a,a,true);CollectionAssert.AreEquivalent(new[]{1},Selection);
            var second=Screen(view.Buildings[1].Position,1);Call("Select",factory,factory,true);Call("Select",second,second,true);CollectionAssert.AreEquivalent(new[]{11},Selection);
        }
        [Test] public void ModifiedBoxReplacesBuildingAndDoubleClickUsesTypeViewportOwnership()
        {
            var view=Get<PlayableSnapshot>("view");Selection.Add(10);var a=Screen(view.Entities[0].Position,0);var b=Screen(view.Entities[2].Position,0);
            Call("Select",new Vector2(a.x-10,Math.Min(a.y,b.y)-10),new Vector2(b.x+10,Math.Max(a.y,b.y)+10),true);CollectionAssert.AreEquivalent(new[]{1,2,3},Selection);
            var point=Screen(view.Entities[0].Position);Call("SelectSameType",point);CollectionAssert.AreEquivalent(new[]{1,2},Selection);
        }
        [TestCase(10)]
        [TestCase(24)]
        public void NearMissSelectsOneUnitAndDoubleClickUsesThatUnitsVisibleType(float zoom)
        {
            camera.orthographicSize=zoom;
            var view=Get<PlayableSnapshot>("view");
            var profile=PlayableProfile.Default;
            var clicked=view.Entities[1];
            var center=Screen(clicked.Position);
            var radius=(float)(PlayableUnitRules.Radius(profile,clicked.Kind)*profile.TargetPickRadiusMultiplier);
            var rim=camera.WorldToScreenPoint(new Vector3(0,.5f,0)+camera.transform.right*radius);
            var point=center+Vector2.up*(Vector2.Distance(center,rim)+(float)profile.SelectionDragPixels);

            Assert.Zero((int)Call("Pick",point,false),"Command targeting still uses the exact zone.");
            Call("Select",point,point,false);
            CollectionAssert.AreEquivalent(new[]{2},Selection);
            Call("SelectSameType",point);
            CollectionAssert.AreEquivalent(new[]{1,2},Selection);

            var outside=point+Vector2.up*(float)(2*profile.SelectionDragPixels);
            Call("Select",outside,outside,false);
            Assert.IsEmpty(Selection);
        }

        [Test]
        public void OverlappingToleranceChoosesTheNearestOwnUnit()
        {
            var profile=PlayableProfile.Default;
            var body=new Vector3(0,.5f,0);
            float pixelsPerMeter=Vector2.Distance(camera.WorldToScreenPoint(body),
                camera.WorldToScreenPoint(body+Vector3.right));
            double radius=PlayableUnitRules.Radius(profile,PlayableEntityKind.Tank)*profile.TargetPickRadiusMultiplier;
            double margin=profile.SelectionDragPixels/pixelsPerMeter;
            Set("view",Snapshot(new[]{
                Unit(9,PlayableEntityKind.Tank,-radius-1.5*margin,0),
                Unit(10,PlayableEntityKind.Tank,radius+.5*margin,0)
            },Array.Empty<PlayableBuildingSnapshot>()));
            var point=Screen(default);
            Assert.Zero((int)Call("Pick",point,false));
            Call("Select",point,point,false);
            CollectionAssert.AreEquivalent(new[]{10},Selection);
        }

        [Test]
        public void AircraftTypeSelectionUsesItsVisibleBodyInsteadOfGroundProjection()
        {
            var units=new[]{
                new PlayableEntitySnapshot(20,PlayableOwner.Player,PlayableEntityKind.Hawk,
                    new NavPoint(0,0),100,false,0,0,0,altitude:3),
                new PlayableEntitySnapshot(21,PlayableOwner.Player,PlayableEntityKind.Hawk,
                    new NavPoint(4,0),100,false,0,0,0,altitude:3),
                new PlayableEntitySnapshot(22,PlayableOwner.Player,PlayableEntityKind.Hawk,
                    new NavPoint(0,0),0,false,0,0,0,altitude:3),
                new PlayableEntitySnapshot(23,PlayableOwner.Player,PlayableEntityKind.Hawk,
                    new NavPoint(0,0),100,false,0,0,0,altitude:60)};
            Set("view",Snapshot(units,Array.Empty<PlayableBuildingSnapshot>()));
            var world=Get<PlayableWorld>("world");
            var boundsArgs=new object[]{camera,units[0],null};
            Assert.True((bool)typeof(PlayableWorld).GetMethod("HawkScreenBounds",F).Invoke(world,boundsArgs));
            var bounds=(Rect)boundsArgs[2];
            var point=new Vector2(bounds.xMin-(float)PlayableProfile.Default.SelectionDragPixels,bounds.center.y);
            Call("Select",point,point,false);
            CollectionAssert.AreEquivalent(new[]{20},Selection);
            Call("SelectSameType",point);
            CollectionAssert.AreEquivalent(new[]{20,21},Selection);
        }

        [Test] public void InspectingForeignUnitClearsRecipientsAndUsesOnlyItsLiveUpgrades()
        {
            var enemy=new PlayableEntitySnapshot(5,PlayableOwner.Enemy,PlayableEntityKind.Tank,
                new NavPoint(8,0),100,false,0,0,0,upgraded:true,weaponSystemsLevel:3,defensiveSystemsLevel:2);
            Set("view",Snapshot(new[]{Unit(1,PlayableEntityKind.Tank,0,0),enemy},Array.Empty<PlayableBuildingSnapshot>()));
            Selection.Add(1);
            var point=Screen(enemy.Position);
            Call("Select",point,point,false);
            Assert.IsEmpty(Selection);
            Assert.AreEqual(enemy.Id,Get<int>("inspectionEntityId"));
            Call("UpdatePrecisionContext",new object[]{null});
            var row=Get<VisualElement>("root").Q("applied-upgrades");
            Assert.AreEqual(3,row.childCount);
            Assert.NotNull(row.Q("applied-upgrade-weapon-systems-3"));
            Assert.NotNull(row.Q("applied-upgrade-defensive-systems-2"));
            Assert.AreEqual(DisplayStyle.Flex,row.Q("applied-upgrade-tankHybridUpgraded").Q("portrait-upgrade-mark").style.display.value);
            Call("ToggleSelectionRoster",false);
            Assert.AreEqual(DisplayStyle.Flex,row.style.display.value);
            Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>("root").Q("context-health").style.display.value);

            Set("view",Snapshot(new[]{Unit(1,PlayableEntityKind.Tank,0,0)},Array.Empty<PlayableBuildingSnapshot>()));
            Call("ValidateInspection");
            Assert.Zero(Get<int>("inspectionEntityId"));
            Assert.IsEmpty(Selection);
        }

        [Test] public void InspectsBuildingUnderConstructionWithoutOpeningItsActions()
        {
            var enemy=new PlayableBuildingSnapshot(20,PlayableOwner.Enemy,PlayableBuildingKind.Factory,
                new NavPoint(8,0),50,.5,0,0,default,phase:ConstructionPhase.Constructing,
                includePrivateState:false,concreteLevel:2);
            Set("view",Snapshot(Array.Empty<PlayableEntitySnapshot>(),new[]{enemy}));
            var point=Screen(enemy.Position,1);
            Call("Select",point,point,false);
            Assert.AreEqual(enemy.Id,Get<int>("inspectionEntityId"));
            Assert.IsEmpty(Selection);
            Assert.IsEmpty((OfflinePadAction[])Call("CurrentBattleActions"));
            Call("UpdatePrecisionContext",enemy);
            Assert.AreEqual(1,Get<VisualElement>("root").Q("applied-upgrades").childCount);
            Call("ToggleSelectionRoster",false);
            Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>("root").Q("selection-stats").style.display.value);
        }

        [Test] public void HostileHoverAndHomeUseRealPresentationGeometry()
        {
            var view=Get<PlayableSnapshot>("view");Assert.True((bool)Call("HostileAt",Screen(view.Entities[4].Position)));Assert.False((bool)Call("HostileAt",Screen(view.Entities[0].Position)));
            Call("RememberKeyboardCamera");var home=camera.transform.position;var rotation=camera.transform.rotation;var size=camera.orthographicSize;camera.transform.position+=Vector3.right*10;camera.orthographicSize=3;
            Call("ResetKeyboardCamera");Assert.AreEqual(home,camera.transform.position);Assert.AreEqual(rotation,camera.transform.rotation);Assert.AreEqual(size,camera.orthographicSize);
        }
        [Test] public void GroupsAreConnectedToHudAndWholeArmyIncludesOffscreenOnlyOwnUnits()
        {
            Selection.Add(1);Selection.Add(2);Call("KeyboardSelection",3,true,1d);Selection.Clear();Call("KeyboardSelection",3,false,2d);CollectionAssert.AreEquivalent(new[]{1,2},Selection);
            Call("KeyboardSelection",0,false,3d);CollectionAssert.AreEquivalent(new[]{1,2,3,4},Selection);
            Selection.Clear();Selection.Add(10);Call("KeyboardSelection",5,true,4d);Selection.Clear();Call("KeyboardSelection",5,false,5d);CollectionAssert.AreEquivalent(new[]{10},Selection);
        }
        [Test] public void OfflineCameraOrbitKeepsItsTargetAndResetsThePose()
        {
            var config=JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text);
            var state=new OfflinePadCamera(4,7,config);state.Apply(camera);
            var homePosition=camera.transform.position;var homeRotation=camera.transform.rotation;
            var target=new Vector3(4,0,7);
            state.Rotate(55);state.Apply(camera);
            Assert.Greater(Quaternion.Angle(homeRotation,camera.transform.rotation),50);
            var at=camera.WorldToViewportPoint(target);
            Assert.That(at.x,Is.EqualTo(.5f).Within(.001f));Assert.That(at.y,Is.EqualTo(.5f).Within(.001f));
            state.Reset();state.Apply(camera);
            Assert.AreEqual(homePosition,camera.transform.position);Assert.AreEqual(homeRotation,camera.transform.rotation);
        }
        [Test] public void OfflineResolverUsesTheSameNearestContextTargetAndMirroredPan()
        {
            var offlineHost=new GameObject("offline keyboard routing fixture");var offline=offlineHost.AddComponent<OfflineTwoLocalBootstrap>();offline.enabled=false;
            var type=typeof(OfflineTwoLocalBootstrap);type.GetField("profile",F).SetValue(offline,PlayableProfile.Default);
            var viewType=type.GetNestedType("Viewport",BindingFlags.NonPublic);var viewport=Activator.CreateInstance(viewType,true);
            var data=Snapshot(new[]{Unit(1,PlayableEntityKind.Tank,0,0),Unit(2,PlayableEntityKind.Tank,1,0,PlayableOwner.Enemy)},new[]{Building(10,-6,0)});
            viewType.GetField("View").SetValue(viewport,data);viewType.GetField("Camera").SetValue(viewport,camera);viewType.GetField("World").SetValue(viewport,Get<PlayableWorld>("world"));
            var config=JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text);var cameraState=new OfflinePadCamera(0,0,config);viewType.GetField("CameraState").SetValue(viewport,cameraState);cameraState.Apply(camera);
            var slots=(Array)type.GetField("views",F).GetValue(offline);slots.SetValue(viewport,0);
            try
            {
                (PlayableCommandKind? Kind,int Target) Resolve(NavPoint point,bool attack)=>(ValueTuple<PlayableCommandKind?,int>)type.GetMethod("OfflineMouseCommand",F).Invoke(offline,new object[]{point,attack,false});
                Assert.AreEqual(PlayableCommandKind.Follow,Resolve(new NavPoint(.1,0),false).Kind);Assert.AreEqual(1,Resolve(new NavPoint(.1,0),false).Target);
                Assert.AreEqual(PlayableCommandKind.AttackMove,Resolve(new NavPoint(.1,0),true).Kind);Assert.Zero(Resolve(new NavPoint(.1,0),true).Target);
                Assert.AreEqual(PlayableCommandKind.Attack,Resolve(new NavPoint(.9,0),false).Kind);Assert.AreEqual(2,Resolve(new NavPoint(.9,0),false).Target);
                Assert.IsNull(Resolve(new NavPoint(-6,0),false).Kind);
                Assert.AreEqual(PlayableCommandKind.AttackMove,Resolve(new NavPoint(10,10),true).Kind);
                var selection=(HashSet<int>)viewType.GetField("Selection").GetValue(viewport);selection.Add(10);
                var at=OfflinePadCamera.ProjectScreen(camera,new Vector3(0,0,0));type.GetMethod("SelectMouse",F).Invoke(offline,new object[]{new Vector2(at.x,at.y),new Vector2(at.x,at.y),true});CollectionAssert.AreEquivalent(new[]{1},selection);
                foreach(float direction in new[]{-1f,1f})
                {
                    cameraState.Reset();cameraState.Apply(camera);var before=OfflinePadCamera.ProjectScreen(camera,Vector3.zero);
                    cameraState.Focus(-direction,0);cameraState.Apply(camera);var after=OfflinePadCamera.ProjectScreen(camera,Vector3.zero);
                    Assert.Less((after.x-before.x)*direction,0,"Field projection must move opposite the requested visual camera direction.");
                }
            }
            finally{slots.SetValue(null,0);UnityEngine.Object.DestroyImmediate(offlineHost);}
        }
    }
}
