using System;
using System.Linq;
using Spacewars.Input;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class OfflineTwoLocalBootstrap
    {
        private OfflinePadBase[] PadBases(Viewport v)=>v.View.Buildings.Where(b=>b.Owner==v.View.Owner&&b.Phase==ConstructionPhase.Ready&&TerritoryRules.Center(b.Kind)).Select(b=>new OfflinePadBase(b.Id,b.Position.X,b.Position.Z,b.Id==v.OriginalHq)).ToArray();
        private OfflinePadAction[] PadMenuActions(Viewport v,out int pages)
        {
            pages=1;
            if(gestures.Mode=="groupWheel"||gestures.Mode=="groupAssign"){
                var units=PadUnits(1);v.GroupSlots??=v.Groups.SlotCount(units);bool assign=gestures.Mode=="groupAssign";
                return v.Groups.Actions(units,v.Selection,assign,v.GroupSlots.Value).Select(a=>new OfflinePadAction(a.Id,"Группа "+a.Id,a.Enabled,assign?"RT: отпустить — заменить; держать — добавить":"Отпустите RB")).ToArray();
            }
            v.GroupSlots=null;
            if(gestures.Mode=="baseWheel"){
                var bases=PadBases(v);var sectors=OfflinePadGroups.Bases(bases,v.OriginalHq,v.BasePage,(int)padProfile.basePageSize,out pages);
                return sectors.Select(a=>{var point=bases.FirstOrDefault(b=>b.Id.ToString()==a.Id);return new OfflinePadAction(a.Id,a.Id==v.OriginalHq.ToString()||a.Id=="original-hq"?"Первый штаб":OfflinePadWorldActions.Name(v.View.Buildings.First(b=>b.Id==point.Id).Kind),a.Enabled,a.Enabled?"Перейти камерой":"Штаб уничтожен"){Point=a.Enabled?new NavPoint(point.X,point.Z):(NavPoint?)null};}).ToArray();
            }
            if(gestures.Mode=="buildingWheel")return v.Anchor!=0?OfflinePadWorldActions.Building(v.View,profile,v.Anchor):OfflinePadWorldActions.Build(v.View,profile,v.Site,v.Slot);
            return Array.Empty<OfflinePadAction>();
        }
        private bool PadMenuIntent(OfflinePadIntent intent,OfflinePadAction[] actions,Viewport v)
        {
            if(intent.Kind=="assignGroup"||intent.Kind=="addGroup"){
                if(actions.Any(a=>a.Sector.Id==intent.Id&&a.Sector.Enabled)&&int.TryParse(intent.Id,out var slot)){v.Groups.Assign(slot,v.Selection,PadUnits(1),intent.Kind=="addGroup");session.RecordHumanAction(session.Seats[1].Id);}return true;
            }
            if(intent.Kind=="cycleGroup"||intent.Kind=="recallGroup"){
                var selected=intent.Kind=="cycleGroup"?v.Groups.Cycle(PadUnits(1),v.Selection):int.TryParse(intent.Id,out var slot)?v.Groups.Recall(slot,PadUnits(1)):null;
                if(selected!=null){session.RecordHumanAction(session.Seats[1].Id);v.Selection.Clear();foreach(var id in selected)v.Selection.Add(id);SelectionMarkerEvent(1);if(intent.Kind=="cycleGroup"){var point=OfflinePadGroups.Focus(selected,PadUnits(1),v.CameraState.X,v.CameraState.Z,padProfile.controlGroupFocusRadius);if(point.HasValue){Focus(1,new NavPoint(point.Value.X,point.Value.Z));v.Cursor=Center(1);}}}return true;
            }
            if(intent.Kind=="selectAllArmy"){session.RecordHumanAction(session.Seats[1].Id);v.Selection.Clear();v.Anchor=v.Site=v.Slot=v.Parent=0;v.Groups.ClearActive();foreach(var unit in v.View.Entities.Where(e=>e.Owner==v.View.Owner))v.Selection.Add(unit.Id);SelectionMarkerEvent(1);return true;}
            if(intent.Kind=="cycleBase"||intent.Kind=="selectBase"){
                var point=intent.Kind=="cycleBase"?v.Groups.CycleBase(PadBases(v),v.OriginalHq):actions.FirstOrDefault(a=>a.Sector.Id==intent.Id&&a.Sector.Enabled)?.Point is NavPoint p?new OfflinePadBase(0,p.X,p.Z):(OfflinePadBase?)null;
                if(point.HasValue){Focus(1,new NavPoint(point.Value.X,point.Value.Z));v.Cursor=new NavPoint(point.Value.X,point.Value.Z);}return true;
            }
            if(intent.Kind=="activate"||intent.Kind=="repeat"||intent.Kind=="sell"){
                var action=actions.FirstOrDefault(a=>a.Sector.Id==intent.Id&&a.Sector.Enabled);if(action?.Sector.Id=="rally"){gestures.SetMode("rallyTarget");return true;}
                var command=intent.Kind=="activate"?action?.Command:action?.HoldCommand;if(command!=null){v.Notice.text=command.Submit(session,session.Seats[1].Id).Status.ToString();if(command.Kind==PlayableCommandKind.BuildAt){v.Selection.Clear();v.Anchor=v.Site=v.Slot=v.Parent=0;gestures.SetMode("world");}}return true;
            }
            if(intent.Kind=="rally"&&v.Anchor!=0){new OfflinePadActionCommand{Kind=PlayableCommandKind.SetRally,Entities=new[]{v.Anchor},Point=v.Cursor}.Submit(session,session.Seats[1].Id);return true;}
            if(intent.Kind=="deselect"){v.Selection.Clear();v.Anchor=v.Site=v.Slot=v.Parent=0;SelectionMarkerEvent(1);return true;}
            return false;
        }
    }
}
