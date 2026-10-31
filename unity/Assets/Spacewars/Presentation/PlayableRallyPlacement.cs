using System.Linq;
using Spacewars.Input;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private int rallyPlacementBuilding;
        private VisualElement rallyHints;
        private PlayableBuildingSnapshot SelectedRallyProducer()
            =>view==null||selection.Count!=1?null:view.Buildings.FirstOrDefault(b=>selection.Contains(b.Id)&&b.Owner==view.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState!=null&&b.PrivateState.Lifecycle?.Selling!=true&&PlayableMenuDescriptions.RallyProducer(b.Kind));
        private void BeginRallyPlacement(bool controller)
        {
            var producer=SelectedRallyProducer();if(producer==null)return;
            rallyPlacementBuilding=producer.Id;padRememberedBuilding=producer.Id;battleRally=true;battleDismissed=false;battleController=controller;notice="";
            if(controller){padCursorGround=producer.PrivateState.HasRally?producer.Rally:new NavPoint(producer.Position.X+profile.DefaultRallyDistance,producer.Position.Z);padCursorInitialized=true;FocusPadPoint(padCursorGround);battleGestures.SetMode(mapOpen?"rallyMap":"rallyTarget");}
            root.Focus();
        }
        private void CompleteRallyPlacement(NavPoint point,bool returnToProducer)
        {
            var producer=view?.Buildings.FirstOrDefault(b=>b.Id==rallyPlacementBuilding&&PlayableMenuDescriptions.RallyProducer(b.Kind)&&b.Owner==view.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true)??SelectedRallyProducer();
            if(producer!=null)Submit(PlayableCommandKind.SetRally,new[]{producer.Id},point);
            EndRallyPlacement(producer,returnToProducer);
        }
        private void CancelRallyPlacement(bool returnToProducer)
        {
            var producer=view?.Buildings.FirstOrDefault(b=>b.Id==rallyPlacementBuilding&&b.Owner==view.Owner&&b.Health>0);
            EndRallyPlacement(producer,returnToProducer);
        }
        private void EndRallyPlacement(PlayableBuildingSnapshot producer,bool returnToProducer)
        {
            battleRally=false;rallyPlacementBuilding=0;battleDismissed=false;world?.RenderRallyPreview(null,view?.Owner??PlayableOwner.Player);
            if(returnToProducer)
            {
                CloseMap();
                if(producer!=null){SetPadSelection(new[]{producer.Id});FocusPadPoint(producer.Position);padRememberedBuilding=producer.Id;}
                battleGestures.SetMode(producer!=null?"buildingWheel":"world");
            }
            root?.Focus();
        }
        private bool MouseBuildingMenuVisible(Vector2 building,Rect menu)
        {
            if(battleController||!SeatUsesKeyboard||input?.AssignedMouse==null)return true;
            var pointer=PanelPoint(input.AssignedMouse.position.ReadValue());
            // Reuse the authored radial interaction radius; rendered controls/details stay reachable.
            return Vector2.Distance(pointer,building)<=battleInputProfile.radialRadius||menu.Contains(pointer)||battleRing.ContainsPanelPoint(pointer+root.worldBound.position);
        }
        private void UpdateRallyPreview()
        {
            var producer=view?.Buildings.FirstOrDefault(b=>b.Id==rallyPlacementBuilding&&PlayableMenuDescriptions.RallyProducer(b.Kind)&&b.Owner==view.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true);
            bool active=battleRally&&battleController&&!paused&&!restarting&&view?.Outcome==PlayableMatchOutcome.Playing&&producer!=null;
            if(battleRally&&producer==null)EndRallyPlacement(null,battleController);
            if(rallyHints==null&&root!=null){rallyHints=new VisualElement{name="rally-placement-hints",pickingMode=PickingMode.Ignore};rallyHints.style.position=Position.Absolute;rallyHints.style.top=54;rallyHints.style.left=Length.Percent(50);rallyHints.style.marginLeft=-130;rallyHints.style.width=260;rallyHints.style.flexDirection=FlexDirection.Row;rallyHints.style.paddingLeft=rallyHints.style.paddingRight=8;OrbitalPrecision.Frame(rallyHints);
                foreach(var item in new[]{("A","Поставить"),("B","Отменить")}){rallyHints.Add(new NativeControllerGlyph(item.Item1,()=>SeatPad,false));var label=new Label(item.Item2){pickingMode=PickingMode.Ignore};label.style.fontSize=12;label.style.color=OrbitalTheme.Ink;label.style.marginRight=12;rallyHints.Add(label);}root.Add(rallyHints);}
            if(rallyHints!=null){rallyHints.style.display=active?DisplayStyle.Flex:DisplayStyle.None;if(active)rallyHints.BringToFront();}
            world?.RenderRallyPreview(active&&!mapOpen?(NavPoint?)padCursorGround:null,view?.Owner??PlayableOwner.Player);
        }
    }
}
