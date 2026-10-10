using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Input;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class OfflineTwoLocalBootstrap
    {
        private readonly KeyboardControlGroups offlineKeyboardGroups=new KeyboardControlGroups();
        private readonly Dictionary<int,Label> offlineGroupLabels=new Dictionary<int,Label>();
        private AngularCommandCursor offlineCommandCursor;
        private KeyboardSelectable[] OfflineKeyboardEntities()
        {
            var view=views[0].View;
            return view.Entities.Where(e=>e.Health>0).Select(e=>new KeyboardSelectable(e.Id,(int)e.Kind,e.Owner==view.Owner,false,e.Position.X,e.Position.Z))
                .Concat(view.Buildings.Where(b=>b.Health>0&&b.Phase!=ConstructionPhase.Pending).Select(b=>new KeyboardSelectable(b.Id,(int)b.Kind,b.Owner==view.Owner,true,b.Position.X,b.Position.Z))).ToArray();
        }
        private void BindOfflineKeyboardCommands()
        {
            keyboard.SelectionDragPixels=(float)profile.SelectionDragPixels;keyboard.DoubleClickMilliseconds=padProfile.doubleTapMs;
            keyboard.CanIssueAttack=()=>views[0].View.Entities.Any(e=>e.Owner==views[0].View.Owner&&e.Health>0&&views[0].Selection.Contains(e.Id));
            keyboard.SelectionKey=OfflineKeyboardSelection;keyboard.SelectSameType=OfflineSelectSameType;
            keyboard.EdgePanAxis=point=>AngularCommandCursor.EdgePan(point,views[0].Camera.pixelRect,(float)profile.CameraEdgePanZonePixels);
            keyboard.EdgePan=axis=>ScrollOfflineCamera(axis,NativeCameraScrollSettings.EdgeSpeed(profile.CameraEdgePanSpeed));
            keyboard.ResetCamera=()=>{views[0].CameraState.Reset();views[0].CameraState.Apply(views[0].Camera);};
            offlineCommandCursor=new AngularCommandCursor();offlineCommandCursor.Set(false);
        }
        private void ScrollOfflineCamera(Vector2 axis,float speed)
        {
            var delta=NativeCameraScrollSettings.Displacement(views[0].Camera.transform,axis,speed,Time.unscaledDeltaTime);
            Pan(0,new Vector2(delta.x,delta.z));
        }
        private void OfflineKeyboardSelection(int index,bool assign,double time)
        {
            if(session==null)return;var v=views[0];var entities=OfflineKeyboardEntities();session.RecordHumanAction(session.Seats[0].Id);
            var ids=offlineKeyboardGroups.Apply(index,assign,time,v.Selection,entities,padProfile.doubleTapMs,out bool focus);
            v.Selection.Clear();foreach(int id in ids)v.Selection.Add(id);v.Site=v.Slot=v.Parent=v.Anchor=0;
            SelectionMarkerEvent(0);
            if(focus){var center=Center(0);var point=KeyboardControlGroups.Focus(ids,entities,center.X,center.Z,padProfile.controlGroupFocusRadius);if(point.HasValue)Focus(0,new NavPoint(point.Value.X,point.Value.Z));}
        }
        private void OfflineSelectSameType(Vector2 point)
        {
            if(session==null)return;var v=views[0];var ground=Ground(0,point);
            var clicked=v.View.Entities.Where(e=>e.Owner==v.View.Owner&&e.Health>0).OrderBy(e=>Distance(e.Position,ground)).FirstOrDefault();
            if(clicked==null||Distance(clicked.Position,ground)>PlayableUnitRules.Radius(profile,clicked.Kind)*profile.TargetPickRadiusMultiplier){SelectMouse(point,point,false);return;}
            session.RecordHumanAction(session.Seats[0].Id);v.Selection.Clear();v.Site=v.Slot=v.Parent=v.Anchor=0;
            foreach(var entity in v.View.Entities.Where(e=>e.Owner==v.View.Owner&&e.Health>0&&e.Kind==clicked.Kind))if(OfflinePadSelection.InViewport(v.Camera,v.World.Point(entity.Position)))v.Selection.Add(entity.Id);
            offlineKeyboardGroups.Observe(v.Selection);SelectionMarkerEvent(0);
        }
        private (PlayableCommandKind? Kind,int Target) OfflineMouseCommand(NavPoint p,bool attack,bool map)
        {
            if(map)return (attack?PlayableCommandKind.AttackMove:PlayableCommandKind.Move,0);
            var v=views[0];
            var units=v.View.Entities.Where(e=>e.Health>0&&Distance(e.Position,p)<=PlayableUnitRules.Radius(profile,e.Kind)*profile.TargetPickRadiusMultiplier)
                .Select(e=>(e.Id,e.Owner,e.Position,Unit:true));
            var buildings=v.View.Buildings.Where(b=>b.Health>0&&b.Phase!=ConstructionPhase.Pending&&Distance(b.Position,p)<=profile.BuildingPickRadius)
                .Select(b=>(b.Id,b.Owner,b.Position,Unit:false));
            var target=units.Concat(buildings).OrderBy(e=>Distance(e.Position,p)).ThenBy(e=>e.Id).FirstOrDefault();
            if(target.Id==0)return (attack?PlayableCommandKind.AttackMove:PlayableCommandKind.Move,0);
            if(v.View.IsHostile(target.Owner))return (PlayableCommandKind.Attack,target.Id);
            // An allied building is a context hit, not an implicit ground order.
            if(target.Unit)return attack?(PlayableCommandKind.AttackMove,0):(PlayableCommandKind.Follow,target.Id);
            return (null,0);
        }
        private void UpdateOfflineKeyboardPresentation()
        {
            var v=views[0];var entities=OfflineKeyboardEntities();offlineKeyboardGroups.Prune(entities);offlineKeyboardGroups.Observe(v.Selection);
            foreach(var label in offlineGroupLabels.Values)label.style.display=DisplayStyle.None;
            if(offlineKeyboardGroups.Active.HasValue)
                foreach(var e in entities.Where(e=>e.Own&&v.Selection.Contains(e.Id)))
                {
                    var screen=OfflinePadCamera.ProjectScreen(v.Camera,v.World.Point(new NavPoint(e.X,e.Z))+Vector3.up*(e.Building?2:1));
                    if(screen.z<=0||!v.Camera.pixelRect.Contains(new Vector2(screen.x,screen.y)))continue;
                    if(!offlineGroupLabels.TryGetValue(e.Id,out var label)){label=new Label {pickingMode=PickingMode.Ignore};label.style.position=Position.Absolute;label.style.color=Color.white;label.style.backgroundColor=new Color(.03f,.06f,.1f,.85f);label.style.fontSize=12;label.style.paddingLeft=3;label.style.paddingRight=3;root.Add(label);offlineGroupLabels.Add(e.Id,label);}
                    var p=PanelPoint(new Vector2(screen.x,screen.y));label.text=offlineKeyboardGroups.Active.Value.ToString();label.style.left=p.x-18;label.style.top=p.y-16;label.style.display=DisplayStyle.Flex;
                }
            foreach(int id in offlineGroupLabels.Keys.Where(id=>!entities.Any(e=>e.Id==id&&e.Own)).ToArray()){offlineGroupLabels[id].RemoveFromHierarchy();offlineGroupLabels.Remove(id);}
            if(assignedMouse==null||!assignedMouse.added){offlineCommandCursor?.Set(false);return;}
            var pointer=assignedMouse.position.ReadValue();int map=MapAt(pointer);bool field=keyboard.WorldInputEnabled&&keyboard.HasFocus&&keyboard.CanReadWorld?.Invoke()!=false&&(map>0||!OverHud(pointer))&&(map>0||v.Camera.pixelRect.Contains(pointer));
            bool units=v.View.Entities.Any(e=>e.Owner==v.View.Owner&&e.Health>0&&v.Selection.Contains(e.Id));
            bool building=v.Selection.Any(id=>v.View.Buildings.Any(b=>b.Id==id));
            offlineCommandCursor?.Set(field&&!building&&(keyboard.AttackMode||units&&map==0&&OfflineMouseCommand(Ground(0,pointer),false,false).Kind==PlayableCommandKind.Attack));

        }
    }
}
