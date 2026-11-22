using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed class RadialArtwork : VisualElement
    {
        readonly OrbitalPlaceholderImage orbital;readonly ModelPortrait portrait;readonly OrbitalGlyph glyph;readonly Label check;readonly ResearchIcon research;readonly PortraitUpgradeMark upgradeMark;
        readonly RadialCardProfile profile=RadialCardProfile.Load();
        string kind;bool emblem,initialized,lastUpgrade,lastSelected,lastAvailable,lastFitProductionPortrait;
        public RadialArtwork()
        {
            pickingMode=PickingMode.Ignore;
            orbital=new OrbitalPlaceholderImage(0);orbital.style.position=Position.Absolute;orbital.style.left=orbital.style.right=orbital.style.top=orbital.style.bottom=0;orbital.style.display=DisplayStyle.None;Add(orbital);
            portrait=new ModelPortrait();glyph=new OrbitalGlyph("building");research=new ResearchIcon();
            foreach(var item in new VisualElement[]{portrait,glyph,research}){item.style.width=item.style.height=StyleKeyword.Auto;item.style.position=Position.Absolute;item.style.left=item.style.right=item.style.top=item.style.bottom=0;Add(item);}
            check=new Label("✓"){name="upgrade-mark",pickingMode=PickingMode.Ignore};check.style.position=Position.Absolute;check.style.right=1;check.style.bottom=1;check.style.fontSize=13;check.style.unityFontStyleAndWeight=FontStyle.Bold;check.style.color=OrbitalTheme.Cyan;check.style.backgroundColor=OrbitalTheme.Panel;check.style.borderTopLeftRadius=check.style.borderTopRightRadius=check.style.borderBottomLeftRadius=check.style.borderBottomRightRadius=8;Add(check);
            upgradeMark=new PortraitUpgradeMark{name="portrait-upgrade-mark",pickingMode=PickingMode.Ignore};Add(upgradeMark);
            // The approval mark belongs to the artwork and scales with it. Its
            // original 13/70 reference proportions must not obscure a fitted model.
            RegisterCallback<GeometryChangedEvent>(_=>{
                check.style.fontSize=13*Mathf.Min(1,contentRect.height/70f);
                SizeUpgradeMark();
            });
        }
        private static bool IsResearchPortrait(string key)=>key is "scientificCenterUpgraded" or "tankHybridUpgraded"
            or "explorerUpgraded" or "shkval-guidance-3" or "hawkUpgraded";

        private void SizeUpgradeMark()
        {
            float size=Mathf.Min(contentRect.width,contentRect.height);
            if(IsResearchPortrait(kind))
                ResearchIcon.LayoutBadge(upgradeMark,size,profile.researchRankSize);
            else
                upgradeMark.style.width=upgradeMark.style.height=size*profile.buildingUpgradeBadgeSize;
        }

        public void Set(string key,bool upgraded=false,bool selected=false,bool available=true,bool fitProductionPortrait=false)
        {
            if(initialized&&kind==key&&lastUpgrade==upgraded&&lastSelected==selected&&lastAvailable==available&&lastFitProductionPortrait==fitProductionPortrait)return;
            initialized=true;lastUpgrade=upgraded;lastSelected=selected;lastAvailable=available;lastFitProductionPortrait=fitProductionPortrait;
            kind=key;
            bool reinforcementTanks=key==OrbitalPlaceholderImage.ReinforcementTanksRadialKey;
            int abilityId=0;
            bool isOrbital=reinforcementTanks||key!=null&&key.StartsWith("orbital:",System.StringComparison.Ordinal)&&int.TryParse(key.Substring(8),out abilityId);
            orbital.style.display=isOrbital?DisplayStyle.Flex:DisplayStyle.None;
            if(isOrbital){orbital.SetId(reinforcementTanks?OrbitalAbilities.Reinforcement:abilityId,reinforcementTanks?PlayableEntityKind.Tank:PlayableEntityKind.Explorer);orbital.style.opacity=available?1:.4f;portrait.style.display=glyph.style.display=research.style.display=check.style.display=upgradeMark.style.display=DisplayStyle.None;return;}
            emblem=ResearchEmblem.Supports(key);research.Set(key,available);
            portrait.Set(emblem?null:key,selected,available,upgraded&&!fitProductionPortrait);
            if(fitProductionPortrait)portrait.FitSquareContent(0);
            var texture=portrait.Texture;
            portrait.style.display=texture==null?DisplayStyle.None:DisplayStyle.Flex;
            bool showGlyph=texture==null&&!emblem;
            glyph.style.display=showGlyph?DisplayStyle.Flex:DisplayStyle.None;
            if(showGlyph)
            {
                glyph.Set(key,available?OrbitalTheme.Ink:OrbitalTheme.Muted);
                glyph.SetFallbackFrame(fitProductionPortrait?PortraitFraming.SquareUvFor(key):new Rect(0,0,1,1));
            }
            bool portraitUpgrade=IsResearchPortrait(key)||key is "factoryUpgraded" or "hangarUpgraded" or "refineryUpgraded"
                ||key==ModelPortrait.RocketLauncherArtwork;
            SizeUpgradeMark();
            check.style.display=upgraded&&!portraitUpgrade?DisplayStyle.Flex:DisplayStyle.None;
            upgradeMark.style.display=upgraded&&portraitUpgrade&&!portrait.HasUpgradeMark?DisplayStyle.Flex:DisplayStyle.None;upgradeMark.Available=available;upgradeMark.MarkDirtyRepaint();MarkDirtyRepaint();
        }
    }
    // Normalized vector paths keep chevrons crisp at petal and card sizes.
    public sealed class PortraitUpgradeMark : VisualElement
    {
        public bool Available=true;
        public int ChevronCount { get; }
        public PortraitUpgradeMark(int chevronCount=2)
        {
            ChevronCount=chevronCount;
            style.position=Position.Absolute;style.right=style.bottom=1;style.display=DisplayStyle.None;
            style.backgroundColor=OrbitalTheme.Panel;
            style.borderLeftWidth=style.borderRightWidth=style.borderTopWidth=style.borderBottomWidth=1;
            style.borderLeftColor=style.borderRightColor=style.borderTopColor=style.borderBottomColor=OrbitalTheme.Muted;
            style.borderTopLeftRadius=style.borderTopRightRadius=style.borderBottomLeftRadius=style.borderBottomRightRadius=2;
            generateVisualContent+=Draw;
        }
        void Draw(MeshGenerationContext context)
        {
            var r=contentRect;var p=context.painter2D;
            p.strokeColor=Available?OrbitalTheme.Ink:OrbitalTheme.Muted;p.lineWidth=Mathf.Max(1,Mathf.Min(r.width,r.height)/10);
            bool triple=ChevronCount==3;
            float top=triple?.12f:.18f, spacing=triple?.25f:.3f, arm=triple?.22f:.3f;
            for(int i=0;i<ChevronCount;i++)
            {
                float y=top+i*spacing;
                p.BeginPath();p.MoveTo(new Vector2(r.x+r.width*.2f,r.y+r.height*(y+arm)));
                p.LineTo(new Vector2(r.x+r.width*.5f,r.y+r.height*y));p.LineTo(new Vector2(r.x+r.width*.8f,r.y+r.height*(y+arm)));p.Stroke();
            }
        }
    }
    // Shared textured research artwork: the rank is UI text, never baked into the asset.
    public sealed class ResearchIcon : VisualElement
    {
        readonly Image image;
        readonly Label rank;
        readonly RadialCardProfile profile=RadialCardProfile.Load();
        public ResearchIcon()
        {
            pickingMode=PickingMode.Ignore;
            image=new Image{name="research-image",scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};
            image.style.position=Position.Absolute;image.style.left=image.style.right=image.style.top=image.style.bottom=0;Add(image);
            rank=new Label{name="research-level",pickingMode=PickingMode.Ignore};
            rank.style.position=Position.Absolute;rank.style.right=0;rank.style.bottom=0;
            rank.style.marginLeft=rank.style.marginRight=rank.style.marginTop=rank.style.marginBottom=0;
            rank.style.paddingLeft=rank.style.paddingRight=rank.style.paddingTop=rank.style.paddingBottom=0;
            rank.style.unityTextAlign=TextAnchor.MiddleCenter;rank.style.unityFontStyleAndWeight=FontStyle.Bold;
            rank.style.backgroundColor=OrbitalTheme.Panel;
            rank.style.borderTopWidth=rank.style.borderBottomWidth=rank.style.borderLeftWidth=rank.style.borderRightWidth=1;
            Add(rank);RegisterCallback<GeometryChangedEvent>(_=>LayoutRank());
        }
        internal static void LayoutBadge(VisualElement badge,float iconSize,float rankScale)
        {
            float size=iconSize*rankScale;
            if(badge is Label label)label.style.fontSize=size;
            badge.style.height=size*1.3f;
            badge.style.width=size*1.7f;
        }

        void LayoutRank()=>LayoutBadge(rank,Mathf.Min(contentRect.width,contentRect.height),profile.researchRankSize);
        public void Set(string key,bool available=true)
        {
            bool supported=ResearchEmblem.Supports(key);style.display=supported?DisplayStyle.Flex:DisplayStyle.None;
            image.image=supported?OrbitalGlyph.TextureFor(key):null;
            float brightness=profile.researchIconBrightness;var tone=available?Color.white:OrbitalTheme.Muted;
            image.tintColor=new Color(tone.r*brightness,tone.g*brightness,tone.b*brightness,1);
            rank.text=supported?ResearchEmblem.Level(key):"";
            var color=key!=null&&key.StartsWith("weapon")?OrbitalTheme.Amber:OrbitalTheme.Ink;
            rank.style.color=available?color:OrbitalTheme.Muted;
            rank.style.borderTopColor=rank.style.borderBottomColor=rank.style.borderLeftColor=rank.style.borderRightColor=OrbitalTheme.Muted;
            LayoutRank();
        }
    }
    public static class ResearchEmblem
    {
        public static bool Supports(string key)=>key!=null&&(key.Contains("weapon-systems")||key.Contains("defensive-systems")||key.StartsWith("reinforced-concrete"));
        public static string Level(string key)=>key.EndsWith("3")?"III":key.EndsWith("2")?"II":"I";
    }
}
