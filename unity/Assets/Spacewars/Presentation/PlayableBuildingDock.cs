using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private VisualElement dockBody,dockIdentity,dockContents;
        private Image dockPortrait;
        private Label dockQueueCount;
        private bool buildingDockVisible,buildingDockHasDetails;
        private void ConfigureBuildingDock()
        {
            dockBody=new VisualElement{name="building-dock-body"};
            dockIdentity=new VisualElement{name="building-dock-identity"};
            dockIdentity.style.flexDirection=FlexDirection.Row;dockIdentity.style.alignItems=Align.Center;dockIdentity.style.flexShrink=0;
            dockPortrait=new Image{name="building-dock-portrait",pickingMode=PickingMode.Ignore,scaleMode=ScaleMode.ScaleToFit};
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
            armyRegion.style.backgroundColor=new Color(.05f,.085f,.11f,.94f);
            armyRegion.style.borderTopColor=armyRegion.style.borderBottomColor=armyRegion.style.borderLeftColor=armyRegion.style.borderRightColor=new Color(.59f,.74f,.8f,.30f);
        }
        private void LayoutBuildingDock(float panelWidth)
        {
            if(dockBody==null)return;
            bool local=localHumanCount>1;
            // Small-seat packing invariant: leave room above the wrapped queue for the command ring.
            bool tinySeat=local&&panelWidth<460;
            float available=Mathf.Max(160,panelWidth-(local?16:(float)profile.MinimapCompactSize+24));
            bool wide=buildingDockVisible&&buildingDockHasDetails&&!local&&available>=620;
            armyRegion.style.width=buildingDockVisible?Mathf.Min(!buildingDockHasDetails?320:wide?840:480,available):Mathf.Min(local?270:340,available);
            armyRegion.style.paddingLeft=armyRegion.style.paddingRight=buildingDockVisible?16:10;
            armyRegion.style.paddingTop=armyRegion.style.paddingBottom=buildingDockVisible?(tinySeat?6:12):8;
            dockBody.style.flexDirection=wide?FlexDirection.Row:FlexDirection.Column;
            dockBody.style.alignItems=wide?Align.Center:Align.Stretch;
            dockIdentity.style.width=wide?228:new StyleLength(StyleKeyword.Auto);
            dockIdentity.style.marginRight=wide?18:0;dockIdentity.style.marginBottom=wide?0:tinySeat?2:6;
            dockPortrait.style.width=dockPortrait.style.height=buildingDockVisible?(wide?96:tinySeat?64:80):48;
            dockContents.style.borderLeftWidth=wide?1:0;dockContents.style.borderLeftColor=new Color(.5f,.64f,.7f,.16f);dockContents.style.paddingLeft=wide?16:0;
            bool showContents=buildingDockVisible?buildingDockHasDetails:dockContents.Children().Any(e=>e!=dockQueueCount&&e.style.display.value!=DisplayStyle.None);
            dockContents.style.display=showContents?DisplayStyle.Flex:DisplayStyle.None;
            selectionLabel.style.fontSize=14;contextStats.style.fontSize=11;contextHealthText.style.fontSize=11;dockQueueCount.style.fontSize=10;
        }
        private void UpdateBuildingDock(PlayableBuildingSnapshot building,PlayableEntitySnapshot entity)
        {
            buildingDockVisible=building!=null;
            string glyph=building!=null?OrbitalPrecision.BuildingGlyph(building.Kind):entity==null?null:entity.Kind==PlayableEntityKind.Hawk?"hawk":entity.Kind==PlayableEntityKind.Explorer?"explorer":entity.Kind==PlayableEntityKind.Shkval?"shkval":"tank";
            var portrait=DockArtwork.TextureFor(building?.RefineryUpgraded==true?"refineryUpgraded":glyph);
            dockPortrait.image=portrait;dockPortrait.style.display=portrait!=null?DisplayStyle.Flex:DisplayStyle.None;
            contextIcon.style.display=portrait==null?DisplayStyle.Flex:DisplayStyle.None;
            contextIcon.style.position=Position.Relative;contextIcon.style.left=contextIcon.style.top=0;
            int count=(building?.Kind==PlayableBuildingKind.Factory||building?.Kind==PlayableBuildingKind.Hangar)?building.PrivateState?.Orders?.Count??0:building?.Kind==PlayableBuildingKind.ScientificCenter?building.PrivateState?.Research?.Count(o=>!o.Complete)??0:-1;
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
            string key=glyph=="science"?"scientificCenter":glyph=="building"?"headquarters":glyph;
            if(!textures.TryGetValue(key,out var texture)){texture=Resources.Load<Texture2D>("DockPortraits/"+key);textures.Add(key,texture);}
            return texture;
        }
    }
}
