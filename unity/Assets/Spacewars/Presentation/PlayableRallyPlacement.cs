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
        private PlayableBuildingSnapshot SelectedRallyProducer()
            =>view==null||selection.Count!=1?null:view.Buildings.FirstOrDefault(b=>selection.Contains(b.Id)&&b.Owner==view.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState!=null&&b.PrivateState.Lifecycle?.Selling!=true&&(b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost||b.Kind==PlayableBuildingKind.Factory));
        private void BeginRallyPlacement(bool controller)
        {
            var producer=SelectedRallyProducer();if(producer==null)return;
            rallyPlacementBuilding=producer.Id;padRememberedBuilding=producer.Id;battleRally=true;battleDismissed=false;battleController=controller;notice="";
            if(controller){padCursorGround=producer.PrivateState.HasRally?producer.Rally:new NavPoint(producer.Position.X+profile.DefaultRallyDistance,producer.Position.Z);padCursorInitialized=true;FocusPadPoint(padCursorGround);battleGestures.SetMode(mapOpen?"rallyMap":"rallyTarget");}
            root.Focus();
        }
        private void CompleteRallyPlacement(NavPoint point,bool returnToProducer)
        {
            var producer=view?.Buildings.FirstOrDefault(b=>b.Id==rallyPlacementBuilding&&b.Owner==view.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true)??SelectedRallyProducer();
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
            var producer=view?.Buildings.FirstOrDefault(b=>b.Id==rallyPlacementBuilding&&b.Owner==view.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true);
            bool active=battleRally&&battleController&&!paused&&!restarting&&view?.Outcome==PlayableMatchOutcome.Playing&&producer!=null;
            if(battleRally&&producer==null)EndRallyPlacement(null,battleController);
            world?.RenderRallyPreview(active&&!mapOpen?(NavPoint?)padCursorGround:null,view?.Owner??PlayableOwner.Player);
        }
    }
}
