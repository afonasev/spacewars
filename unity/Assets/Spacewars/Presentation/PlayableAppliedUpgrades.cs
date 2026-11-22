using System;
using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private VisualElement appliedUpgradesRow, appliedUpgradeTip;
        private (int Id, int Weapon, int Defense, int Concrete, bool Upgraded, bool Rocket, int Revision) appliedUpgradeKey;

        private void ConfigureAppliedUpgrades(VisualElement identity)
        {
            appliedUpgradesRow=new VisualElement{name="applied-upgrades",pickingMode=PickingMode.Ignore};
            appliedUpgradesRow.style.flexDirection=FlexDirection.Row;
            appliedUpgradesRow.style.flexWrap=Wrap.Wrap;
            appliedUpgradesRow.style.marginTop=3;
            appliedUpgradesRow.style.display=DisplayStyle.None;
            identity.Add(appliedUpgradesRow);
        }

        internal static PlayableResearchKind? WeaponResearch(int level)=>level switch
        {
            1=>PlayableResearchKind.ImprovedWeaponSystems,
            2=>PlayableResearchKind.AdvancedWeaponSystems,
            3=>PlayableResearchKind.UltimateWeaponSystems,
            _=>null
        };

        private static PlayableResearchKind? DefenseResearch(int level)=>level switch
        {
            1=>PlayableResearchKind.ImprovedDefensiveSystems,
            2=>PlayableResearchKind.AdvancedDefensiveSystems,
            3=>PlayableResearchKind.UltimateDefensiveSystems,
            _=>null
        };

        private static PlayableResearchKind? ConcreteResearch(int level)=>level switch
        {
            1=>PlayableResearchKind.ReinforcedConcrete,
            2=>PlayableResearchKind.AdvancedReinforcedConcrete,
            _=>null
        };

        internal static IReadOnlyList<PlayableResearchOrderSnapshot> CompletedResearch(PlayableResearchKind? kind)
            =>kind.HasValue?new[]{new PlayableResearchOrderSnapshot(0,kind.Value,0,0,0,0,false,true)}:
                Array.Empty<PlayableResearchOrderSnapshot>();

        private void RefreshAppliedUpgrades(PlayableBuildingSnapshot building,PlayableEntitySnapshot unit)
        {
            var key=(Id:building?.Id??unit?.Id??0,
                Weapon:building?.WeaponSystemsLevel??(unit?.Kind==PlayableEntityKind.Skat?0:unit?.WeaponSystemsLevel??0),
                Defense:unit?.DefensiveSystemsLevel??0,Concrete:building?.ConcreteLevel??0,
                Upgraded:building?.ModelUpgraded??unit?.Upgraded??false,Rocket:unit?.RocketLauncher??false,Revision:profile.Revision);
            if(key==appliedUpgradeKey)return;
            appliedUpgradeKey=key;
            HideAppliedUpgradeTip();
            appliedUpgradesRow.Clear();
            var defense=DefenseResearch(key.Defense);
            AddResearchBadge(WeaponResearch(key.Weapon),null);
            AddResearchBadge(defense,null);
            AddResearchBadge(ConcreteResearch(key.Concrete),null);
            if(unit!=null&&unit.Kind!=PlayableEntityKind.Skat&&unit.Upgraded)
                AddResearchBadge(RadialActionPresentation.Specialization(unit.Kind),CompletedResearch(defense));
            if(key.Rocket)AddResearchBadge(PlayableResearchKind.ExplorerRocketLauncher,null);
            if(building?.ScientificCenterUpgraded==true)
                AddResearchBadge(PlayableResearchKind.ScientificCenterUpgrade,null);
            else if(building?.ModelUpgraded==true)
                AddUpgradeBadge(OrbitalPrecision.BuildingArtwork(building),true,
                    PlayableMenuDescriptions.LocalBuildingUpgrade(profile,building.Kind));
            appliedUpgradesRow.style.display=appliedUpgradesRow.childCount>0?DisplayStyle.Flex:DisplayStyle.None;
        }

        private void AddResearchBadge(PlayableResearchKind? kind,IEnumerable<PlayableResearchOrderSnapshot> research)
        {
            if(!kind.HasValue)return;
            var k=kind.Value;
            AddUpgradeBadge(RadialActionPresentation.ResearchArtwork(k),RadialActionPresentation.ResearchUsesUpgradeMark(k),
                (PlayableResearchRules.Name(k),PlayableResearchRules.Description(profile,k),
                    PlayableMenuDescriptions.ResearchCardStats(profile,k,research,provingGround!=null)));
        }

        private void AddUpgradeBadge(string artwork,bool upgraded,(string Name,string Description,string[] Stats) detail)
        {
            var badge=new VisualElement{name="applied-upgrade-"+artwork};
            badge.style.width=badge.style.height=32;
            badge.style.marginRight=4;
            badge.style.marginBottom=3;
            badge.style.backgroundColor=OrbitalTheme.Panel;
            var art=new RadialArtwork();
            art.style.flexGrow=1;
            art.Set(artwork,upgraded);
            badge.Add(art);
            badge.RegisterCallback<PointerEnterEvent>(e=>
            {
                if(e.pointerType==UnityEngine.UIElements.PointerType.mouse)ShowAppliedUpgradeTip(badge,detail);
            });
            badge.RegisterCallback<PointerLeaveEvent>(_=>HideAppliedUpgradeTip());
            appliedUpgradesRow.Add(badge);
        }

        private void ShowAppliedUpgradeTip(VisualElement badge,(string Name,string Description,string[] Stats) detail)
        {
            HideAppliedUpgradeTip();
            if(!(root.resolvedStyle.width>16))return;
            var tip=new VisualElement{name="applied-upgrade-tooltip",pickingMode=PickingMode.Ignore};
            appliedUpgradeTip=tip;
            tip.style.position=Position.Absolute;
            tip.style.backgroundColor=OrbitalTheme.Panel;
            tip.style.borderLeftWidth=tip.style.borderRightWidth=tip.style.borderTopWidth=tip.style.borderBottomWidth=1;
            tip.style.borderLeftColor=tip.style.borderRightColor=tip.style.borderTopColor=tip.style.borderBottomColor=OrbitalTheme.Line;
            tip.style.paddingTop=tip.style.paddingBottom=8;
            tip.style.paddingLeft=tip.style.paddingRight=10;
            float width=Mathf.Min(330,root.resolvedStyle.width-16);
            tip.style.width=width;
            var dock=root.WorldToLocal(armyRegion.worldBound.position);
            var anchor=root.WorldToLocal(badge.worldBound.position);
            tip.style.left=Mathf.Clamp(anchor.x,8,root.resolvedStyle.width-width-8);
            tip.style.top=Mathf.Max(8,dock.y-120);
            void AddLine(string text,bool strong)
            {
                var line=SelectionText("upgrade-tooltip-line");
                line.text=text;
                line.style.whiteSpace=WhiteSpace.Normal;
                line.style.marginBottom=4;
                line.style.color=strong?Ink:Muted;
                if(strong)line.style.unityFontStyleAndWeight=FontStyle.Bold;
                tip.Add(line);
            }
            AddLine(detail.Name,true);
            AddLine(detail.Description,false);
            foreach(var stat in detail.Stats)AddLine(stat,true);
            tip.RegisterCallback<GeometryChangedEvent>(e=>
            {
                float top=Mathf.Max(8,root.WorldToLocal(armyRegion.worldBound.position).y-e.newRect.height-6);
                if(Mathf.Abs(top-tip.resolvedStyle.top)>.5f)tip.style.top=top;
            });
            root.Add(tip);
        }

        private void HideAppliedUpgradeTip()
        {
            appliedUpgradeTip?.RemoveFromHierarchy();
            appliedUpgradeTip=null;
        }
    }
}
