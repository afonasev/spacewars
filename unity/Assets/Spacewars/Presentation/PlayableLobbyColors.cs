using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private VisualElement lobbyColorOverlay;
        private Button lobbyColorAnchor;
        private void OpenLobbyColorPicker(LobbyColorField field,NativeLobbyParticipant participant)
        {
            CloseLobbyColorPicker(false);lobbyColorAnchor=field.Trigger;
            lobbyColorOverlay=new VisualElement{name="lobby-color-overlay"};
            lobbyColorOverlay.style.position=Position.Absolute;
            lobbyColorOverlay.style.left=lobbyColorOverlay.style.right=lobbyColorOverlay.style.top=lobbyColorOverlay.style.bottom=0;
            root.Add(lobbyColorOverlay);
            var popup=new VisualElement{name="lobby-color-palette"};popup.style.position=Position.Absolute;
            // Finite palette menu packing, independent of gameplay and camera profiles.
            const float width=130,optionHeight=32,padding=6;
            float height=NativeLobbyConfiguration.ColorHex.Length*optionHeight+padding*2;
            var anchor=root.WorldToLocal(field.worldBound);
            popup.style.left=Mathf.Clamp(anchor.xMin,0,Mathf.Max(0,root.resolvedStyle.width-width));
            popup.style.top=Mathf.Clamp(anchor.yMax+height<=root.resolvedStyle.height?anchor.yMax:anchor.yMin-height,0,Mathf.Max(0,root.resolvedStyle.height-height));
            popup.style.width=width;popup.style.paddingLeft=popup.style.paddingRight=popup.style.paddingTop=popup.style.paddingBottom=padding;
            popup.style.backgroundColor=PanelColor;OrbitalPrecision.Frame(popup);lobbyColorOverlay.Add(popup);
            Button selected=null;
            for(int i=0;i<NativeLobbyConfiguration.ColorHex.Length;i++)
            {
                int color=i;bool taken=lobbySetup.Participants.Any(p=>p!=participant&&p.Color==color);
                var option=new Button(()=>{field.value=color;CloseLobbyColorPicker();}){name="lobby-color-option-"+i,tooltip=NativeLobbyConfiguration.ColorNames[i]+(taken?" · занят":"")};
                option.style.height=optionHeight;option.style.minHeight=optionHeight;option.style.flexShrink=0;
                option.style.marginLeft=option.style.marginRight=option.style.marginTop=option.style.marginBottom=0;
                option.style.paddingLeft=option.style.paddingRight=6;option.style.paddingTop=option.style.paddingBottom=5;
                option.style.borderLeftWidth=option.style.borderRightWidth=option.style.borderTopWidth=option.style.borderBottomWidth=i==field.value?2:1;
                option.style.borderLeftColor=option.style.borderRightColor=option.style.borderTopColor=option.style.borderBottomColor=i==field.value?LobbyCyan:Muted;
                option.style.backgroundColor=Color.clear;
                option.RegisterCallback<FocusInEvent>(_=>option.style.backgroundColor=LobbyCyan);
                option.RegisterCallback<FocusOutEvent>(_=>option.style.backgroundColor=Color.clear);
                ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[i],out var paint);
                var swatch=new VisualElement{name="lobby-color-option-swatch",pickingMode=PickingMode.Ignore};swatch.style.flexGrow=1;swatch.style.height=18;swatch.style.backgroundColor=paint;option.Add(swatch);
                option.SetEnabled(!taken);option.style.opacity=taken?.3f:1;popup.Add(option);if(i==field.value)selected=option;
            }
            lobbyColorOverlay.RegisterCallback<PointerDownEvent>(evt=>{if(evt.target==lobbyColorOverlay){CloseLobbyColorPicker();evt.StopPropagation();}});
            menuNavigation?.SetScope(lobbyColorOverlay,()=>CloseLobbyColorPicker(),selected);
            selected?.Focus();
        }
        private void CloseLobbyColorPicker(bool restoreFocus=true)
        {
            if(lobbyColorOverlay==null)return;
            var overlay=lobbyColorOverlay;var anchor=lobbyColorAnchor;lobbyColorOverlay=null;lobbyColorAnchor=null;overlay.RemoveFromHierarchy();
            if(menuNavigation?.Scope==overlay)
            {
                if(restoreFocus)menuNavigation.SetScope(lobbyScreen,ShowMainMenu,anchor);
                else menuNavigation.SetScope(null);
            }
            if(restoreFocus)anchor?.Focus();
        }
    }
}
