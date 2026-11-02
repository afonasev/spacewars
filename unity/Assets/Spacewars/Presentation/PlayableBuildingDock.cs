using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private VisualElement dockBody,dockIdentity,dockContents;
        private ModelPortrait dockPortrait;
        private Label dockQueueCount;
        private bool buildingDockVisible,buildingDockHasDetails;
        private void ConfigureBuildingDock()
        {
            dockBody=new VisualElement{name="building-dock-body"};
            dockIdentity=new VisualElement{name="building-dock-identity"};
            dockIdentity.style.flexDirection=FlexDirection.Row;dockIdentity.style.alignItems=Align.Center;dockIdentity.style.flexShrink=0;
            dockPortrait=new ModelPortrait("building-dock-portrait"){name="building-dock-artwork"};
            dockPortrait.style.width=64;dockPortrait.style.height=64;dockPortrait.style.flexShrink=0;dockPortrait.style.marginRight=10;dockIdentity.Add(dockPortrait);
            var info=new VisualElement{name="building-dock-info"};info.style.flexGrow=1;info.style.minWidth=0;dockIdentity.Add(info);
            info.Add(selectionLabel);info.Add(contextStats);info.Add(armyRegion.Q("context-health"));
            selectionLabel.style.paddingLeft=0;selectionLabel.style.marginBottom=4;
            contextStats.style.marginLeft=0;
            var health=info.Q("context-health");health.style.marginLeft=0;health.style.flexWrap=Wrap.Wrap;
            contextHealthText.style.fontSize=11;contextHealthText.style.marginLeft=0;contextHealthText.style.marginTop=3;
            contextHealthText.style.width=Length.Percent(100);
            health[0].style.minWidth=40;health[0].style.borderTopLeftRadius=health[0].style.borderTopRightRadius=health[0].style.borderBottomLeftRadius=health[0].style.borderBottomRightRadius=2;
            dockContents=new VisualElement{name="building-dock-contents"};dockContents.style.minWidth=0;dockContents.style.flexGrow=1;
            foreach(var child in armyRegion.Children().ToArray())if(child!=contextIcon)dockContents.Add(child);
            contextIcon.RemoveFromHierarchy();dockIdentity.Add(contextIcon);
            dockQueueCount=new Label{name="building-dock-queue-count",pickingMode=PickingMode.Ignore};dockQueueCount.style.color=Muted;dockQueueCount.style.fontSize=10;dockQueueCount.style.marginBottom=3;dockContents.Insert(0,dockQueueCount);
            dockBody.Add(dockIdentity);dockBody.Add(dockContents);armyRegion.Add(dockBody);
            armyRegion.AddToClassList("soft-building-dock");
            OrbitalPrecision.Frame(armyRegion);
            ConfigureSelectionDock();
            armyRegion.style.backgroundColor=Color.clear;
            armyRegion.style.borderTopColor=armyRegion.style.borderBottomColor=armyRegion.style.borderLeftColor=armyRegion.style.borderRightColor=new Color(.59f,.74f,.8f,.30f);
        }
        private void LayoutBuildingDock(float panelWidth)
        {
            if(dockBody==null)return;
            bool local=localHumanCount>1;
            float available=Mathf.Max(160,panelWidth-(local?16:(float)profile.MinimapCompactSize+24));
            var p=selectionDockProfile;
            armyRegion.style.paddingLeft=armyRegion.style.paddingRight=p.dockPadding;
            armyRegion.style.paddingTop=armyRegion.style.paddingBottom=p.dockPadding;
            dockBody.style.flexDirection=FlexDirection.Column;dockBody.style.alignItems=Align.Stretch;
            dockIdentity.style.width=StyleKeyword.Auto;dockIdentity.style.marginRight=0;dockIdentity.style.marginBottom=p.selectionGap;
            dockContents.style.borderLeftWidth=0;dockContents.style.paddingLeft=0;
            selectionLabel.style.fontSize=p.selectionFont+2;contextStats.style.fontSize=p.selectionFont;
            contextHealthText.style.fontSize=p.selectionFont;dockQueueCount.style.fontSize=p.selectionFont;
            if(!buildingDockVisible){LayoutUnitSelectionDock(available);return;}
            armyRegion.style.width=Mathf.Min(buildingDockHasDetails?p.dockWidth:p.selectionWidth,available);
            dockPortrait.style.width=dockPortrait.style.height=p.dockPortraitSize;
            var health=armyRegion.Q("context-health");health.style.width=p.dockActiveWidth;
            health.style.flexWrap=Wrap.NoWrap;health[0].style.flexGrow=1;
            contextHealthText.style.width=StyleKeyword.Auto;contextHealthText.style.marginLeft=p.selectionGap;contextHealthText.style.marginTop=0;
            dockContents.style.display=buildingDockHasDetails?DisplayStyle.Flex:DisplayStyle.None;

        }
        private void UpdateBuildingDock(PlayableBuildingSnapshot building,PlayableEntitySnapshot entity)
        {
            buildingDockVisible=building!=null;
            dockIdentity.tooltip=building!=null?PlayableMenuDescriptions.Building(profile,building.Kind,building.RefineryUpgraded,freeEconomy:provingGround!=null,maxHealth:TerritoryRules.MaximumHealth(profile,building)):entity!=null?(selectionRosterExpanded?PlayableMenuDescriptions.Unit(profile,entity.Kind,entity.Owner==view.Owner?view.OwnerResearch:null,entity,freeEconomy:provingGround!=null):UnitName(entity.Kind)+" · "+entity.Health+" HP · "+"Развернуть подробности (RT)"):"";
            string glyph=building!=null?OrbitalPrecision.BuildingArtwork(building):entity==null?null:RadialActionPresentation.UnitArtwork(entity.Kind,entity.Upgraded);
            dockPortrait.Set(glyph);var portrait=dockPortrait.Texture;
            dockPortrait.style.display=portrait!=null?DisplayStyle.Flex:DisplayStyle.None;
            contextIcon.style.display=portrait==null?DisplayStyle.Flex:DisplayStyle.None;
            contextIcon.style.position=Position.Relative;contextIcon.style.left=contextIcon.style.top=0;
            int count=(building?.Kind==PlayableBuildingKind.Factory||building?.Kind==PlayableBuildingKind.Hangar)?building.PrivateState?.Orders?.Count??0:building?.Kind==PlayableBuildingKind.ScientificCenter?building.PrivateState?.Research?.Count(o=>!o.Complete&&o.CenterId==building.Id)??0:-1;
            buildingDockHasDetails=count>=0||building?.Progress<1||building?.PrivateState?.Upgrade?.Active==true||building?.PrivateState?.Lifecycle?.Selling==true||building?.PrivateState?.Lifecycle?.Repairing==true;
            bool showResearchCount=count>=0&&building?.Kind==PlayableBuildingKind.ScientificCenter;
            dockQueueCount.text=showResearchCount?"ИССЛЕДОВАНИЯ   "+count:"";
            dockQueueCount.style.display=showResearchCount?DisplayStyle.Flex:DisplayStyle.None;
            float width=root.resolvedStyle.width;if(width>0)LayoutBuildingDock(width);
        }
    }
    public static class DockArtwork
    {
        private static readonly System.Collections.Generic.Dictionary<string,Texture2D> textures=new();
        public static Texture2D TextureFor(string glyph)
        {
            if(string.IsNullOrEmpty(glyph))return null;
            bool contour=glyph.EndsWith("-outline");string stem=contour?glyph.Substring(0,glyph.Length-8):glyph;
            string key=(stem switch {
                "science" or "scientific-center"=>"scientificCenter", "building"=>"headquarters", "tank"=>"tankHybrid",
                "chassis"=>"tankHybridUpgraded", "assault"=>"explorerUpgraded", "guidance"=>"shkval-guidance-3",
                "hawk-salvo"=>"hawkUpgraded", "upgrade"=>"refineryUpgraded", _=>stem
            })+(contour?"-outline":"");
            if(!textures.TryGetValue(key,out var texture)){texture=Resources.Load<Texture2D>("DockPortraits/"+key);textures.Add(key,texture);}
            return texture;
        }
    }
}
