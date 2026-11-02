using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private VisualElement lobbyScreenOverlay;
        private Button lobbyScreenAnchor;
        private Button LobbyScreenButton(NativeLobbyParticipant participant,int index)
        {
            int count=NativeScreenLayout.Count(lobbySetup);
            var button=new Button(()=>OpenLobbyScreenPicker(participant,index)){name="lobby-screen-"+index,tooltip=lobbySetup.Spectator?"В режиме зрителя используется весь экран":"Экран: "+NativeScreenLayout.Label(participant.ScreenSlot,count)};
            StyleAction(button);button.style.width=46;button.style.minWidth=46;button.style.height=38;button.style.marginRight=6;button.style.paddingLeft=button.style.paddingRight=0;
            button.Add(new LobbyScreenIcon(participant.ScreenSlot,count));button.SetEnabled(!lobbySetup.Spectator);return button;
        }
        private void OpenLobbyScreenPicker(NativeLobbyParticipant participant,int index)
        {
            if(lobbySetup.Spectator)return;
            CloseLobbyColorPicker(false);CloseLobbyScreenPicker(false);
            lobbyScreenAnchor=rosterPanel.Q<Button>("lobby-screen-"+index);
            lobbyScreenOverlay=new VisualElement{name="lobby-screen-overlay"};lobbyScreenOverlay.style.position=Position.Absolute;
            lobbyScreenOverlay.style.left=lobbyScreenOverlay.style.right=lobbyScreenOverlay.style.top=lobbyScreenOverlay.style.bottom=0;root.Add(lobbyScreenOverlay);
            var popup=new VisualElement{name="lobby-screen-picker"};popup.style.position=Position.Absolute;popup.style.width=260;popup.style.paddingLeft=popup.style.paddingRight=popup.style.paddingTop=popup.style.paddingBottom=10;popup.style.backgroundColor=new Color(PanelColor.r,PanelColor.g,PanelColor.b,1);OrbitalPrecision.Frame(popup);lobbyScreenOverlay.Add(popup);
            popup.Add(LobbyText("Экран",18));
            int current=NativeScreenLayout.Count(lobbySetup),count=current+(participant.ScreenSlot<0?1:0),capacity=count<=2?count:4;
            Button selected=null;
            if(!participant.Human)Add(-1,"Без экрана",popup);
            var choices=new VisualElement();choices.style.flexDirection=FlexDirection.Row;choices.style.flexWrap=Wrap.Wrap;popup.Add(choices);
            for(int slot=0;slot<capacity;slot++)Add(slot,NativeScreenLayout.Label(slot,count),choices);
            if(participant.ScreenSlot<0&&current<4)popup.Add(LobbyText("Добавит экран",12,Muted));
            if(count>4)popup.Add(LobbyText("Все четыре экрана заняты",12,Amber));
            void Add(int slot,string label,VisualElement parent){
                var option=new Button(()=>{lobbyActionError=NativeScreenLayout.Assign(lobbySetup,participant,slot);lobbyRosterEdited=true;CloseLobbyScreenPicker(false);RebuildRoster();menuNavigation?.SetScope(lobbyScreen,ShowMainMenu,rosterPanel.Q<Button>("lobby-screen-"+index));}){name="lobby-screen-option-"+slot,tooltip=label};
                StyleAction(option);option.style.height=slot<0?38:78;option.style.width=slot<0?238:116;option.style.marginLeft=option.style.marginRight=1;option.style.marginBottom=4;option.style.flexDirection=FlexDirection.Column;option.style.justifyContent=Justify.Center;option.style.alignItems=Align.Center;
                option.style.backgroundColor=participant.ScreenSlot==slot?new Color(.08f,.23f,.27f):Color.clear;
                if(slot>=0)option.Add(new LobbyScreenIcon(slot,count));
                var text=new Label(label){pickingMode=PickingMode.Ignore};text.style.fontSize=12;option.Add(text);option.SetEnabled(slot<0||count<=4);parent.Add(option);if(participant.ScreenSlot==slot)selected=option;
            }
            // Re-measure after layout and target/viewport resize; opening can precede first layout.
            void PositionPopup(){if(lobbyScreenAnchor==null||lobbyScreenOverlay==null)return;var anchor=lobbyScreenOverlay.WorldToLocal(lobbyScreenAnchor.worldBound);float width=lobbyScreenOverlay.resolvedStyle.width,height=lobbyScreenOverlay.resolvedStyle.height,h=popup.resolvedStyle.height;if(float.IsNaN(h))return;
                popup.style.left=Mathf.Clamp(anchor.xMax-260,0,Mathf.Max(0,width-260));
                popup.style.top=Mathf.Clamp(anchor.yMax+h<=height?anchor.yMax:anchor.yMin-h,0,Mathf.Max(0,height-h));}
            popup.RegisterCallback<GeometryChangedEvent>(_=>PositionPopup());lobbyScreenOverlay.RegisterCallback<GeometryChangedEvent>(_=>PositionPopup());PositionPopup();
            lobbyScreenOverlay.RegisterCallback<PointerDownEvent>(e=>{if(e.target==lobbyScreenOverlay){CloseLobbyScreenPicker();e.StopPropagation();}});
            menuNavigation?.SetScope(lobbyScreenOverlay,()=>CloseLobbyScreenPicker(),selected??choices.Q<Button>());(selected??choices.Q<Button>())?.Focus();
        }
        private void CloseLobbyScreenPicker(bool restoreFocus=true)
        {
            if(lobbyScreenOverlay==null)return;var overlay=lobbyScreenOverlay;var anchor=lobbyScreenAnchor;lobbyScreenOverlay=null;lobbyScreenAnchor=null;overlay.RemoveFromHierarchy();
            if(menuNavigation?.Scope==overlay){if(restoreFocus)menuNavigation.SetScope(lobbyScreen,ShowMainMenu,anchor);else menuNavigation.SetScope(null);}if(restoreFocus)anchor?.Focus();
        }
    }
}
