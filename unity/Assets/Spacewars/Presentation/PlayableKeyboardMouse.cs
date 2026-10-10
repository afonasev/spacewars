using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Input;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private readonly KeyboardControlGroups keyboardGroups = new KeyboardControlGroups();
        private readonly Dictionary<int,Label> keyboardGroupLabels = new Dictionary<int,Label>();
        private NativeLocalInputProfile keyboardProfile;
        private Vector3 keyboardHomePosition;
        private Quaternion keyboardHomeRotation;
        private float keyboardHomeZoom;
        private AngularCommandCursor commandCursor;
        private KeyboardSelectable[] KeyboardEntities() => view.Entities.Where(e=>e.Health>0).Select(e=>new KeyboardSelectable(e.Id,(int)e.Kind,e.Owner==view.Owner,false,e.Position.X,e.Position.Z))
            .Concat(view.Buildings.Where(b=>b.Health>0&&b.Phase!=ConstructionPhase.Pending).Select(b=>new KeyboardSelectable(b.Id,(int)b.Kind,b.Owner==view.Owner,true,b.Position.X,b.Position.Z))).ToArray();
        private NativeLocalInputProfile KeyboardProfile => keyboardProfile ?? (keyboardProfile=JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text));
        private void BindKeyboardCommands()
        {
            input.SelectionDragPixels=(float)profile.SelectionDragPixels;
            input.DoubleClickMilliseconds=KeyboardProfile.doubleTapMs;
            input.SelectionKey=KeyboardSelection;
            input.CanIssueAttack=()=>view!=null&&view.Entities.Any(e=>e.Owner==view.Owner&&e.Health>0&&selection.Contains(e.Id));
            input.SelectSameType=SelectSameType;
            input.ResetCamera=ResetKeyboardCamera;
            input.EdgePanAxis=point=>SeatUsesKeyboard?AngularCommandCursor.EdgePan(point,cameraView.pixelRect,(float)profile.CameraEdgePanZonePixels):Vector2.zero;
            input.EdgePan=axis=>cameraView.transform.position+=NativeCameraScrollSettings.Displacement(cameraView.transform,axis,NativeCameraScrollSettings.EdgeSpeed(profile.CameraEdgePanSpeed),Time.unscaledDeltaTime);
            commandCursor=new AngularCommandCursor();commandCursor.Set(false);
        }
        private void RememberKeyboardCamera()
        { keyboardHomePosition=cameraView.transform.position;keyboardHomeRotation=cameraView.transform.rotation;keyboardHomeZoom=cameraView.orthographicSize; }
        private void ResetKeyboardCamera()
        { cameraView.transform.SetPositionAndRotation(keyboardHomePosition,keyboardHomeRotation);cameraView.orthographicSize=keyboardHomeZoom; }
        private void KeyboardSelection(int index,bool assign,double time)
        {
            if(view==null)return;
            runtime?.RecordHumanAction(LocalOwnerId);
            var entities=KeyboardEntities();var ids=keyboardGroups.Apply(index,assign,time,selection,entities,KeyboardProfile.doubleTapMs,out bool focus);
            selection.Clear();foreach(int id in ids)selection.Add(id);selectedSite=selectedSlot=primarySelection=0;confirmSaleBuilding=0;battleDismissed=false;battleRally=false;
            if(focus)
            {
                var center=Ground(SeatScreenCenter);
                var point=KeyboardControlGroups.Focus(ids,entities,center.X,center.Z,KeyboardProfile.controlGroupFocusRadius);
                if(point.HasValue)cameraView.transform.position+=new Vector3((float)(point.Value.X-center.X),0,(float)(point.Value.Z-center.Z));
            }
            SelectionMarkerEvent();root.Focus();Record("keyboard group="+index+" assign="+assign+" focus="+focus+" ids="+string.Join(",",ids));
        }
        private void SelectSameType(Vector2 point)
        {
            if(view==null)return;int id=Pick(point,true);var clicked=view.Entities.FirstOrDefault(e=>e.Id==id&&e.Owner==view.Owner&&e.Health>0);
            if(clicked==null){Select(point,point,false);return;}
            runtime?.RecordHumanAction(LocalOwnerId);selection.Clear();
            foreach(var entity in view.Entities.Where(e=>e.Owner==view.Owner&&e.Health>0&&e.Kind==clicked.Kind))
            {
                var screen=cameraView.WorldToScreenPoint(world.Point(entity.Position));
                if(screen.z>0&&cameraView.pixelRect.Contains(new Vector2(screen.x,screen.y)))selection.Add(entity.Id);
            }
            selectedSite=selectedSlot=primarySelection=0;confirmSaleBuilding=0;keyboardGroups.Observe(selection);SelectionMarkerEvent();root.Focus();
        }
        private bool HostileAt(Vector2 point)
        { int id=Pick(point,false);return view.Entities.Any(e=>e.Id==id&&e.Health>0&&IsOpponent(e.Owner))||view.Buildings.Any(b=>b.Id==id&&b.Health>0&&IsOpponent(b.Owner)); }
        private void UpdateKeyboardPresentation()
        {
            var entities=KeyboardEntities();keyboardGroups.Prune(entities);keyboardGroups.Observe(selection);
            foreach(var label in keyboardGroupLabels.Values)label.style.display=DisplayStyle.None;
            if(keyboardGroups.Active.HasValue)
                foreach(var entity in entities.Where(e=>e.Own&&selection.Contains(e.Id)))
                {
                    var screen=cameraView.WorldToScreenPoint(world.Point(new NavPoint(entity.X,entity.Z))+Vector3.up*(entity.Building?2:1));
                    if(screen.z<=0||!cameraView.pixelRect.Contains(new Vector2(screen.x,screen.y)))continue;
                    if(!keyboardGroupLabels.TryGetValue(entity.Id,out var label))
                    {
                        label=new Label {name="keyboard-group-"+entity.Id,pickingMode=PickingMode.Ignore};label.style.position=Position.Absolute;label.style.color=Color.white;label.style.backgroundColor=new Color(.03f,.06f,.1f,.85f);label.style.fontSize=12;label.style.paddingLeft=3;label.style.paddingRight=3;root.Add(label);keyboardGroupLabels.Add(entity.Id,label);
                    }
                    var panel=PanelPoint(new Vector2(screen.x,screen.y));label.text=keyboardGroups.Active.Value.ToString();label.style.left=panel.x-18;label.style.top=panel.y-16;label.style.display=DisplayStyle.Flex;
                }
            foreach(int id in keyboardGroupLabels.Keys.Where(id=>!entities.Any(e=>e.Id==id&&e.Own)).ToArray()){keyboardGroupLabels[id].RemoveFromHierarchy();keyboardGroupLabels.Remove(id);}
            if(!SeatUsesKeyboard)return;
            var mouse=input.AssignedMouse??UnityEngine.InputSystem.Mouse.current;
            if(mouse==null||!mouse.added){commandCursor?.Set(false);return;}
            var point=mouse.position.ReadValue();bool field=input.WorldInputEnabled&&input.HasFocus&&!MenuOwnsInput&&input.IsKeyboardInUi?.Invoke()!=true&&(MapAt(point)>0||!OverUi(point))&&MapAt(point)!=3;
            bool units=view.Entities.Any(e=>e.Owner==view.Owner&&e.Health>0&&selection.Contains(e.Id));
            bool map=MapAt(point)>0;
            bool building=selection.Any(id=>view.Buildings.Any(b=>b.Id==id));
            commandCursor?.Set(field&&!building&&(input.AttackMode||units&&!map&&HostileAt(point))&&!battleRally);

        }
    }
}
