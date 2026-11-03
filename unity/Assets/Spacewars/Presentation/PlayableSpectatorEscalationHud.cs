using System;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private PlayableProfile spectatorEscalationDataProfile;
        private PlayableProfileData spectatorEscalationData;
        private void UpdateSpectatorTableViewport()
        {
            if(spectatorTableScroll==null||root.resolvedStyle.width<=0)return;
            float available=root.resolvedStyle.width-16;
            if(spectatorPerspective.HasValue)available-=Mathf.Max(0,top.resolvedStyle.width)+8;
            spectatorPanel.style.maxWidth=Mathf.Max(220,available);
            float viewport=Mathf.Min(spectatorTable.style.width.value.value,Mathf.Max(220,available)-2);
            spectatorTableScroll.style.height=26+spectatorRows.childCount*32+(spectatorTable.style.width.value.value>viewport?18:0);
        }
        private void RegisterSpectatorTooltip(VisualElement element)
        {
            element.focusable=true;
            element.RegisterCallback<PointerEnterEvent>(_=>ShowSpectatorTooltip(element));
            element.RegisterCallback<PointerLeaveEvent>(_=>spectatorTooltip.style.display=DisplayStyle.None);
            element.RegisterCallback<FocusInEvent>(_=>ShowSpectatorTooltip(element));
            element.RegisterCallback<FocusOutEvent>(_=>spectatorTooltip.style.display=DisplayStyle.None);
        }
        private VisualElement CreateSpectatorEscalation()
        {
            var cell=SpectatorIcons(SpectatorEscalationWidth);cell.name="spectator-escalation";cell.style.paddingLeft=8;
            var level=new VisualElement{name="spectator-escalation-level"};level.style.width=level.style.height=28;level.style.flexShrink=0;level.style.marginRight=6;RegisterSpectatorTooltip(level);cell.Add(level);
            var ring=new OrbitalProgressRing{name="spectator-escalation-progress",LineWidth=2};AbsoluteFill(ring);level.Add(ring);
            var text=new Label{name="spectator-escalation-number",pickingMode=PickingMode.Ignore};AbsoluteFill(text);text.style.unityTextAlign=TextAnchor.MiddleCenter;text.style.unityFontStyleAndWeight=FontStyle.Bold;text.style.marginLeft=text.style.marginRight=text.style.marginTop=text.style.marginBottom=0;text.style.paddingLeft=text.style.paddingRight=0;level.Add(text);
            var slots=new VisualElement{name="spectator-escalation-abilities"};slots.style.flexDirection=FlexDirection.Row;slots.style.flexShrink=0;cell.Add(slots);return cell;
        }
        private void UpdateSpectatorEscalation(VisualElement cell,OrbitalEscalationSnapshot state)
        {
            state=state??OrbitalEscalationSnapshot.Empty;
            bool max=state.Level==OrbitalEscalationState.MaxLevel;
            var level=cell[0];var ring=level.Q<OrbitalProgressRing>();ring.Fraction=max?1:(float)(state.Progress/Math.Max(1,state.Threshold));ring.Pending=state.PendingLevel>0;ring.Color=ring.Pending?Amber:Cyan;
            var number=level.Q<Label>();number.text=max?"MAX":state.Level.ToString();number.style.fontSize=max?8:12;number.style.color=ring.Pending?Amber:Ink;
            level.tooltip=(max?"MAX · максимальный уровень":"Эскалация · уровень "+state.Level+"\n"+state.Progress.ToString("0.##")+" / "+state.Threshold.ToString("0.##")+" до следующего уровня")+(state.PendingLevel>0?"\nОжидается выбор умения уровня "+state.PendingLevel:"");
            var slots=cell[1];string signature=string.Join(",",state.Abilities.Select(a=>a.Id));
            if(slots.userData as string!=signature)
            {
                slots.Clear();slots.userData=signature;
                foreach(var ability in state.Abilities)
                {
                    var tile=new VisualElement{name="spectator-ability-"+ability.Id};tile.style.width=tile.style.height=24;tile.style.flexShrink=0;tile.style.marginRight=3;tile.style.backgroundColor=PanelColor;tile.style.overflow=Overflow.Hidden;
                    tile.style.borderLeftWidth=tile.style.borderRightWidth=tile.style.borderTopWidth=tile.style.borderBottomWidth=1;RegisterSpectatorTooltip(tile);
                    var sweep=new OrbitalCooldownSweep{name="cooldown"};AbsoluteFill(sweep);tile.Add(sweep);
                    var art=new OrbitalPlaceholderImage(ability.Id){name="ability-artwork",pickingMode=PickingMode.Ignore};AbsoluteFill(art);tile.Add(art);
                    var seconds=new Label{name="seconds",pickingMode=PickingMode.Ignore};AbsoluteFill(seconds);seconds.style.unityTextAlign=TextAnchor.MiddleCenter;seconds.style.unityFontStyleAndWeight=FontStyle.Bold;seconds.style.fontSize=11;seconds.style.color=Ink;seconds.style.marginLeft=seconds.style.marginRight=seconds.style.marginTop=seconds.style.marginBottom=0;seconds.style.paddingLeft=seconds.style.paddingRight=0;tile.Add(seconds);slots.Add(tile);
                }
            }
            if(!ReferenceEquals(spectatorEscalationDataProfile,profile)){spectatorEscalationDataProfile=profile;spectatorEscalationData=profile.CopyData();}
            var data=spectatorEscalationData;
            for(int i=0;i<state.Abilities.Count;i++)
            {
                var ability=state.Abilities[i];var tile=slots[i];double wait=Math.Max(ability.Remaining,state.CommonRemaining);bool cooling=wait>1e-9;
                var sweep=tile.Q<OrbitalCooldownSweep>();sweep.Fraction=(float)(ability.Remaining>=state.CommonRemaining?1-ability.Recovery/Math.Max(.001,ability.Duration):1-state.CommonRemaining/Math.Max(.001,data.orbitalCommonSeconds));sweep.style.display=cooling?DisplayStyle.Flex:DisplayStyle.None;
                tile.Q("ability-artwork").style.opacity=cooling?(float)data.orbitalUiCoolingArtworkOpacity:1;
                var seconds=tile.Q<Label>("seconds");seconds.text=cooling?Math.Ceiling(wait).ToString():"✓";seconds.style.unityTextAlign=cooling?TextAnchor.MiddleCenter:TextAnchor.LowerRight;seconds.style.fontSize=cooling?11:8;seconds.style.color=cooling?Ink:Cyan;
                var edge=cooling?OrbitalTheme.Line:Cyan;tile.style.borderLeftColor=tile.style.borderRightColor=tile.style.borderTopColor=tile.style.borderBottomColor=edge;
                tile.tooltip=OrbitalAbilities.Name(ability.Id)+(cooling?" · перезарядка · "+Math.Ceiling(wait)+" с":" · готово");
            }
            if(spectatorTooltipTarget!=null&&spectatorTooltip.style.display.value==DisplayStyle.Flex&&cell.Contains(spectatorTooltipTarget))spectatorTooltip.text=spectatorTooltipTarget.tooltip;
        }
    }
}
