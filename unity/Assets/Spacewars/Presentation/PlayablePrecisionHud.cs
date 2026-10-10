using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private OrbitalGlyph contextIcon;
        private VisualElement contextHealth;
        private Label contextHealthText,contextStats;
        private void ConfigurePrecisionHud()
        {
            // Fixed corner packing: matched row/glyph centres and bounded text slots; not a gameplay tuning parameter.
            top.style.backgroundColor=Color.clear;top.style.borderTopWidth=top.style.borderBottomWidth=top.style.borderLeftWidth=top.style.borderRightWidth=0;top.style.width=210;top.style.height=26;top.style.flexWrap=Wrap.NoWrap;top.style.paddingTop=2;top.style.paddingBottom=2;top.style.paddingLeft=6;top.style.paddingRight=6;top.style.minHeight=26;
            creditsLabel.style.height=24;creditsLabel.style.marginTop=0;creditsLabel.style.marginBottom=0;creditsLabel.style.paddingTop=0;creditsLabel.style.paddingBottom=0;creditsLabel.style.unityTextAlign=TextAnchor.MiddleLeft;creditsLabel.style.width=120;creditsLabel.style.flexShrink=0;creditsLabel.style.whiteSpace=WhiteSpace.NoWrap;creditsLabel.style.fontSize=12;creditsLabel.style.color=Ink;creditsLabel.style.unityTextOutlineWidth=.15f;creditsLabel.style.unityTextOutlineColor=PanelColor;creditsLabel.style.paddingLeft=20;creditsLabel.style.paddingRight=0;creditsLabel.style.marginLeft=0;creditsLabel.style.marginRight=4;
            var money=new OrbitalGlyph("credits"){name="credits-icon"};money.Tint=Cyan;money.style.position=Position.Absolute;money.style.left=0;money.style.top=5;money.style.width=16;money.style.height=14;creditsLabel.Add(money);
            hudFocusButton.style.width=72;hudFocusButton.style.flexShrink=0;hudFocusButton.style.whiteSpace=WhiteSpace.NoWrap;hudFocusButton.style.fontSize=12;hudFocusButton.style.unityTextOutlineWidth=.15f;hudFocusButton.style.unityTextOutlineColor=PanelColor;hudFocusButton.style.unityTextAlign=TextAnchor.MiddleLeft;hudFocusButton.style.paddingTop=0;hudFocusButton.style.paddingBottom=0;hudFocusButton.style.marginTop=0;hudFocusButton.style.marginRight=0;hudFocusButton.style.paddingLeft=24;hudFocusButton.style.paddingRight=4;hudFocusButton.style.height=24;hudFocusButton.style.minHeight=24;hudFocusButton.style.marginBottom=0;hudFocusButton.style.backgroundColor=Color.clear;
            hudFocusButton.style.borderTopWidth=hudFocusButton.style.borderBottomWidth=hudFocusButton.style.borderRightWidth=0;hudFocusButton.style.borderLeftWidth=1;
            var army=new OrbitalGlyph("army"){name="army-icon"};army.style.position=Position.Absolute;army.style.left=4;army.style.top=5;army.style.width=16;army.style.height=14;hudFocusButton.Add(army);
            OrbitalPrecision.Frame(armyRegion,flush:true);armyRegion.style.paddingTop=10;armyRegion.style.paddingBottom=10;armyRegion.style.paddingLeft=12;armyRegion.style.paddingRight=12;
            selectionLabel.style.fontSize=16;selectionLabel.style.paddingLeft=28;selectionLabel.style.minHeight=20;selectionLabel.style.marginBottom=3;
            selectionLabel.name="context-title";
            contextStats=new Label{name="context-stats"};contextStats.style.fontSize=12;contextStats.style.color=Muted;contextStats.style.whiteSpace=WhiteSpace.Normal;contextStats.style.marginLeft=28;contextStats.style.marginBottom=4;armyRegion.Insert(1,contextStats);
            contextIcon=new OrbitalGlyph("building");contextIcon.style.position=Position.Absolute;contextIcon.style.left=3;contextIcon.style.top=3;contextIcon.style.width=22;contextIcon.style.height=20;armyRegion.Add(contextIcon);
            var healthRow=new VisualElement{name="context-health",pickingMode=PickingMode.Ignore};healthRow.style.flexDirection=FlexDirection.Row;healthRow.style.alignItems=Align.Center;healthRow.style.marginBottom=1;healthRow.style.marginLeft=28;armyRegion.Insert(2,healthRow);
            var rail=new VisualElement();rail.style.flexGrow=1;rail.style.height=4;rail.style.backgroundColor=Line;healthRow.Add(rail);contextHealth=new VisualElement();contextHealth.style.height=4;contextHealth.style.backgroundColor=Cyan;rail.Add(contextHealth);
            contextHealthText=new Label();contextHealthText.style.fontSize=12;contextHealthText.style.marginLeft=8;contextHealthText.style.color=Muted;healthRow.Add(contextHealthText);
            queueLabel.style.fontSize=12;queueLabel.style.color=Muted;queueLabel.style.whiteSpace=WhiteSpace.Normal;queueLabel.style.marginBottom=6;
            var frame=root.Q("tactical-minimap-frame");OrbitalPrecision.Frame(frame,MinimapOutline,true);frame.style.paddingLeft=frame.style.paddingRight=frame.style.paddingTop=frame.style.paddingBottom=4;frame.style.width=(float)profile.MinimapCompactSize+8;frame.style.marginRight=0;
            mapClock.style.fontSize=12;mapClock.style.color=Ink;mapClock.style.marginTop=mapClock.style.marginBottom=0;mapClock.style.height=20;
        }
        private string packedCredits,packedCapacity;
        private float packedResourceFont;
        private void PackResourceRow()
        {
            float font=creditsLabel.resolvedStyle.fontSize;
            if(packedCredits==creditsLabel.text&&packedCapacity==hudFocusButton.text&&packedResourceFont==font)return;
            float money=creditsLabel.MeasureTextSize(creditsLabel.text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined).x;
            float army=hudFocusButton.MeasureTextSize(hudFocusButton.text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined).x;
            // Newly attached split-seat panels can have an unresolved font during their first layout.
            if(float.IsNaN(money)||float.IsNaN(army)||float.IsInfinity(money)||float.IsInfinity(army)||money<=0||army<=0)return;
            // Content width plus glyph/padding and the existing 7 px inter-control gap, plus 2 px for outline/raster rounding; no vacant numeric slots.
            creditsLabel.style.width=Mathf.Ceil(money)+22;hudFocusButton.style.width=Mathf.Ceil(army)+31;
            top.style.width=Mathf.Ceil(money)+Mathf.Ceil(army)+72;
            packedCredits=creditsLabel.text;packedCapacity=hudFocusButton.text;packedResourceFont=font;
        }
        private void UpdatePrecisionContext(PlayableBuildingSnapshot building)
        {
            bool visible=building!=null||selection.Count>0||selectedSite!=0||spectatorMode&&inspectionEntityId!=0;
            armyRegion.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            var ownSelected=view.Entities.Where(e=>spectatorMode?e.Id==inspectionEntityId:selection.Contains(e.Id)&&e.Owner==view.Owner).ToArray();
            var entity=ownSelected.FirstOrDefault(e=>e.Id==primarySelection)??ownSelected.FirstOrDefault();
            contextStats.style.display=building==null&&entity!=null?DisplayStyle.Flex:DisplayStyle.None;
            contextStats.text=entity==null?"":"Скорость "+PlayableUnitRules.Speed(profile,entity.Kind,entity.Upgraded).ToString("0.#")+" · Обзор "+PlayableUnitRules.Vision(profile,entity.Kind).ToString("0.#")+" · Дальность "+PlayableUnitRules.Range(profile,entity.Kind,entity.Upgraded).ToString("0.#")+(ownSelected.Length>1?" · Выбрано "+ownSelected.Length:"")+(entity.Held?" · HOLD":"");
            contextIcon.Set(building!=null?OrbitalPrecision.BuildingGlyph(building.Kind):entity==null?"building":entity.Kind==PlayableEntityKind.Explorer?"explorer":entity.Kind==PlayableEntityKind.Shkval?"shkval":"tank",Ink);
            var healthRow=armyRegion.Q("context-health");healthRow.style.display=building!=null||entity!=null?DisplayStyle.Flex:DisplayStyle.None;
            if(building!=null){contextHealth.style.width=Length.Percent((float)(100*building.Health/(double)TerritoryRules.Health(profile,building.Kind)));selectionLabel.text=BuildingName(building.Kind).ToUpperInvariant();contextHealthText.text=building.Health+" / "+TerritoryRules.Health(profile,building.Kind);}
            else if(entity!=null){selectionLabel.text=UnitName(entity.Kind);contextHealth.style.width=Length.Percent((float)(100*entity.Health/PlayableUnitRules.Health(profile,entity.Kind)));contextHealthText.text=entity.Health+" / "+PlayableUnitRules.Health(profile,entity.Kind);}
            // Production progress belongs to its active card; preserve construction/upgrade bars.
            if(building?.Kind==PlayableBuildingKind.Factory&&building.Progress>=1)progress.style.display=DisplayStyle.None;
            if(building?.PrivateState?.Lifecycle!=null){var s=building.PrivateState.Lifecycle;lifecycleLabel.style.display=s.Selling||s.Repairing?DisplayStyle.Flex:DisplayStyle.None;}
            armyRegion.MarkDirtyRepaint();
        }
    }
}
