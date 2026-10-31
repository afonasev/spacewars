using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private bool signalWheelOpen,signalWheelPad,signalStickReady;
        private long signalGeneration=-1,signalObservedTick=-1;private double signalObservedAt;
        private NavPoint signalPoint;private Vector2 signalWheelCentre;
        private VisualElement signalWorldLayer,signalModalPanel,signalHints;
        private TeamSignalWheel signalWheel;
        private readonly HashSet<long> heardTeamSignals=new HashSet<long>();
        private bool CanSignal=>view!=null&&!view.OwnerEliminated&&!spectatorMode&&matchSetup?.Spectator!=true&&!paused&&!chatOpen&&!MenuOwnsInput&&!ProvingPreparation&&input?.CommandInputEnabled==true&&view.Outcome==PlayableMatchOutcome.Playing&&localInputFocused;
        private void EnsureTeamSignalUi()
        {
            if(signalWorldLayer!=null)return;
            signalWorldLayer=new VisualElement{name="team-signal-world",pickingMode=PickingMode.Ignore};signalWorldLayer.style.position=Position.Absolute;signalWorldLayer.style.left=signalWorldLayer.style.right=signalWorldLayer.style.top=signalWorldLayer.style.bottom=0;
            signalWorldLayer.generateVisualContent+=DrawTeamSignals;root.Add(signalWorldLayer);
            signalModalPanel=new VisualElement{name="team-signal-modal"};signalModalPanel.style.position=Position.Absolute;signalModalPanel.style.left=signalModalPanel.style.right=signalModalPanel.style.top=signalModalPanel.style.bottom=0;signalModalPanel.style.display=DisplayStyle.None;
            signalModalPanel.RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());signalModalPanel.RegisterCallback<PointerUpEvent>(e=>e.StopPropagation());signalModalPanel.RegisterCallback<KeyDownEvent>(e=>e.StopPropagation());
            signalWheel=new TeamSignalWheel();signalModalPanel.Add(signalWheel);
            signalHints=new VisualElement{name="team-signal-hints",pickingMode=PickingMode.Ignore};signalHints.style.position=Position.Absolute;signalHints.style.flexDirection=FlexDirection.Row;signalHints.style.justifyContent=Justify.SpaceBetween;
            var surface=OrbitalTheme.Panel;surface.a=1;signalHints.style.backgroundColor=surface;
            signalHints.style.paddingLeft=signalHints.style.paddingRight=RadialCardProfile.Load().padding;
            signalHints.style.borderTopLeftRadius=signalHints.style.borderTopRightRadius=signalHints.style.borderBottomLeftRadius=signalHints.style.borderBottomRightRadius=8;
            signalHints.style.borderTopWidth=signalHints.style.borderBottomWidth=signalHints.style.borderLeftWidth=signalHints.style.borderRightWidth=1;
            signalHints.style.borderTopColor=signalHints.style.borderBottomColor=signalHints.style.borderLeftColor=signalHints.style.borderRightColor=OrbitalTheme.Line;
            signalModalPanel.Add(signalHints);root.Add(signalModalPanel);
        }
        private bool OpenTeamSignal(Vector2 screen,bool pad)
        {
            if(!CanSignal||!cameraView.pixelRect.Contains(screen))return false;
            int map=MapAt(screen);if(map==3||map==0&&!pad&&OverUi(screen))return false;
            EnsureTeamSignalUi();signalPoint=map>0?MapGround(map,screen):Ground(screen);signalWheelPad=pad;signalStickReady=false;
            float size=(float)profile.TeamSignalWheelPixels;var at=PanelPoint(screen);var bounds=root.contentRect;
            signalHints.Clear();signalHints.Add(new RadialControlHint(pad?"LS":"Мышь","Выбор",pad,SeatPad));signalHints.Add(new RadialControlHint(pad?"A":"ЛКМ","Сигнал",pad,SeatPad));signalHints.Add(new RadialControlHint(pad?"B":"Esc","Отмена",pad,SeatPad));
            float hintHeight=RadialCardProfile.Load().actionHeight+8,hintWidth=Mathf.Min(RadialCardProfile.Load().width+40,bounds.width-24);
            signalHints.style.width=hintWidth;signalHints.style.height=hintHeight;
            var placed=RadialViewportLayout.Fit(new Rect(at.x-hintWidth/2,at.y-size/2,hintWidth,size+hintHeight+RadialViewportLayout.Gap),bounds.size,AuxiliaryMapObstacle());
            signalWheelCentre=new Vector2(placed.center.x,placed.y+size/2);
            signalWheel.style.width=signalWheel.style.height=size;signalWheel.style.left=signalWheelCentre.x-size/2;signalWheel.style.top=placed.y;
            signalHints.style.left=placed.x;signalHints.style.top=placed.y+size+RadialViewportLayout.Gap;
            battleGestures.SetMode(battleGestures.Mode); // Drop pending holds; restoring input requires neutral release.
            signalWheel.Choose(Vector2.zero,1);signalWheelOpen=true;signalModalPanel.style.display=DisplayStyle.Flex;signalModalPanel.BringToFront();root.Focus();input.ClearMode();return true;
        }
        private bool CancelTeamSignal()
        {
            if(!signalWheelOpen)return false;signalWheelOpen=false;signalModalPanel.style.display=DisplayStyle.None;root.Focus();return true;
        }
        private void SendTeamSignal()
        {
            if(!signalWheelOpen||signalWheel.Selected<0||!CanSignal)return;
            var kind=TeamSignalArtwork.Sectors[signalWheel.Selected];
            var command=kind==TeamSignalKind.Attack?PlayableCommandKind.SignalAttack:kind==TeamSignalKind.Defend?PlayableCommandKind.SignalDefend:PlayableCommandKind.SignalRecon;
            // Positional communication carries no selected entity IDs.
            Submit(command,Array.Empty<int>(),signalPoint);CancelTeamSignal();
        }
        private bool SignalPointer(Vector2 at,bool left,bool down,bool alt)
        {
            if(signalWheelOpen){if(!signalWheelPad){signalWheel.Choose(PanelPoint(at)-signalWheelCentre,(float)profile.TeamSignalWheelPixels*.20f);if(down){if(left)SendTeamSignal();else CancelTeamSignal();}}return true;}
            if(!alt||!left||!down)return false;
            if(CanSignal){battleController=false;OpenTeamSignal(at,false);return true;}return false;
        }
        private bool PollTeamSignalPad(Gamepad pad)
        {
            if(signalWheelOpen){
                if(pad.startButton.wasPressedThisFrame){CancelTeamSignal();(localCoordinator??this).PauseFrom(this,true);return true;}
                if(!signalWheelPad)return true;
                var stick=pad.leftStick.ReadValue();if(stick.magnitude<=NativeDevicePreferences.Deadzone)signalStickReady=true;
                signalWheel.Choose(signalStickReady?new Vector2(stick.x,-stick.y):Vector2.zero,NativeDevicePreferences.Deadzone);
                if(pad.buttonEast.wasPressedThisFrame)CancelTeamSignal();else if(pad.buttonSouth.wasPressedThisFrame)SendTeamSignal();
                return true;
            }
            if(!pad.rightStickButton.wasPressedThisFrame||!CanSignal)return false;
            battleController=true;
            Vector2 at=padCursorInitialized?battleCursor:SeatScreenCenter;
            if(OpenTeamSignal(at,true))return true;return false;
        }
        private double TeamSignalTick=>view.Tick+(paused?0:Math.Min(1,Math.Max(0,Time.unscaledTimeAsDouble-signalObservedAt)*30));
        private void UpdateTeamSignalPresentation()
        {
            if(root==null||view==null)return;EnsureTeamSignalUi();
            if(signalGeneration!=view.Generation){signalGeneration=view.Generation;heardTeamSignals.Clear();CancelTeamSignal();}
            if(signalObservedTick!=view.Tick){signalObservedTick=view.Tick;signalObservedAt=Time.unscaledTimeAsDouble;}
            if(!CanSignal||signalWheelPad&&(SeatPad==null||!SeatPad.added))CancelTeamSignal();
            if(signalWheelOpen&&!signalWheelPad){var mouse=input.AssignedMouse??Mouse.current;if(mouse!=null)signalWheel.Choose(PanelPoint(mouse.position.ReadValue())-signalWheelCentre,(float)profile.TeamSignalWheelPixels*.20f);}
            var manual=spectatorMode||view.OwnerEliminated?Array.Empty<PlayableTeamSignal>():view.TeamSignals;
            var signals=manual.Concat(VisibleAiSignals).ToArray();
            // Union minimaps have no private layer: only each seat's own compact/tactical surfaces are updated.
            compactMap?.SetTeamSignals(signals,TeamSignalTick);tacticalMap?.SetTeamSignals(signals,TeamSignalTick);
            signalWorldLayer.style.display=!mapOpen&&!MenuOwnsInput&&!inLobby&&!preparing&&!ResultsVisible?DisplayStyle.Flex:DisplayStyle.None;signalWorldLayer.MarkDirtyRepaint();
            bool sound=false;foreach(var signal in signals)if(heardTeamSignals.Add(signal.Id))sound=true;
            heardTeamSignals.RemoveWhere(id=>!signals.Any(s=>s.Id==id));
            if(sound&&!paused)(localCoordinator??this).gameplayAudio?.TeamSignal();
        }
        private void DrawTeamSignals(MeshGenerationContext ctx)
        {
            if(view==null||world==null||cameraView==null)return;
            var manual=spectatorMode||view.OwnerEliminated?Array.Empty<PlayableTeamSignal>():view.TeamSignals;
            foreach(var signal in manual.Concat(VisibleAiSignals))
            {
                // Viewport-relative drawing keeps local seats and RenderTexture captures aligned.
                var viewport=cameraView.WorldToViewportPoint(world.Point(signal.Point));
                if(viewport.z<=0||viewport.x<0||viewport.x>1||viewport.y<0||viewport.y>1)continue;
                var at=new Vector2(viewport.x*root.contentRect.width,(1-viewport.y)*root.contentRect.height);
                TeamSignalArtwork.Mark(ctx.painter2D,at,signal.Kind,(float)profile.TeamSignalGlyphPixels,(float)signal.Alpha(TeamSignalTick,profile),false);
            }
        }
    }
}
