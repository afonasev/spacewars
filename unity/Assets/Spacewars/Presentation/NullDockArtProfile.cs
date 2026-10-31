using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class NullDockArtProfile
    {
        public string id="null-dock-art-v1";public int revision=4;
        [SurfaceSetting("Deck","Panel size","Constructive panel spacing on station decks.","m",2,8,.25f)] public float panelSize=6;
        [SurfaceSetting("Deck","Panel joint","Dark recessed panel seams.","m",.025f,.15f,.005f)] public float panelJoint=.055f;
        [SurfaceSetting("Deck","Lower deck value","Brightness below units on the main deck.","ratio",.1f,.65f,.01f)] public float deckValue=.64f;
        [SurfaceSetting("Deck","Raised deck value","Home and outpost floor brightness.","ratio",.35f,.8f,.01f)] public float upperValue=.62f;
        [SurfaceSetting("Deck","Paint saturation","Desaturated station blue on deck panels.","ratio",0,.5f,.01f)] public float paintSaturation=.06f;
        [SurfaceSetting("Deck","Hull depth","Visible station structure below the playable deck.","m",1,6,.25f)] public float hullDepth=4;
        [SurfaceSetting("Textures","Texture tile","World-space scale of crisp steel albedo and siblings.","m",1,8,.25f)] public float textureTile=4;
        [SurfaceSetting("Textures","Variant patch size","Irregular continuous regions selecting sibling textures.","m",8,40,1)] public float patchSize=19;
        [SurfaceSetting("Textures","Texture contrast","Fine steel wear contrast.","ratio",0,.8f,.025f)] public float textureContrast=.30f;
        [SurfaceSetting("Details","Edge bay spacing","Distance between structural buttresses and edge beacons.","m",4,12,.5f)] public float baySpacing=6;
        [SurfaceSetting("Details","Hull rib width","Width of external structural supports.","m",.2f,1.2f,.05f)] public float ribWidth=.55f;
        [SurfaceSetting("Details","Module equipment spacing","Spacing of in-envelope roof service equipment.","m",2,7,.25f)] public float equipmentSpacing=3.5f;
        [SurfaceSetting("Details","Pipe radius","Roof and external service pipe radius.","m",.1f,.6f,.025f)] public float pipeRadius=.25f;
        [SurfaceSetting("Details","Beacon emission","Small navigation light brightness.","ratio",0,3,.1f)] public float emission=.4f;
        [SurfaceSetting("Space","Planet radius","Distant planet disc radius behind station.","m",80,220,5)] public float planetRadius=140;
        [SurfaceSetting("Space","Planet offset X","Planet placement beyond the deck.","m",-300,300,5)] public float planetX=-190;
        [SurfaceSetting("Space","Planet offset Z","Planet placement beyond the deck.","m",-300,300,5)] public float planetZ=110;
        [SurfaceSetting("Deck","Route stripe width","Width of flush transit lanes along approved central approaches.","m",4,14,.5f)] public float routeWidth=9;
        [SurfaceSetting("Details","Service crane height","Height of the two exterior maintenance cranes, entirely over space.","m",6,16,.5f)] public float craneHeight=10;
        [SurfaceSetting("Zero Field","HQ floor value","Quiet slate deck behind units and building slots.","ratio",.3f,.65f,.01f)] public float headquartersValue=.54f;
        [SurfaceSetting("Zero Field","Outpost floor value","Brightness of the blue outpost construction deck.","ratio",.35f,.75f,.01f)] public float outpostValue=.54f;
        [SurfaceSetting("Zero Field","Ceramic value","Ivory armor on the three new machines.","ratio",.5f,.9f,.01f)] public float ceramicValue=.80f;
        [SurfaceSetting("Zero Field","Core emission","Recessed core glow independent of subdued border beacons.","ratio",0,2,.05f)] public float coreEmission=.8f;
        [SurfaceSetting("Threat zones","Base hue","Blue family shared by HQ and outpost floors.","turn",0f,1f,0.01f)] public float baseHue=0.59f;
        [SurfaceSetting("Threat zones","Base saturation","Color strength below base buildings.","ratio",0f,0.65f,0.01f)] public float baseSaturation=0.38f;
        [SurfaceSetting("Threat zones","Site circle lift","Lightens metal inside existing HQ and outpost circles.","ratio",0f,0.3f,0.01f)] public float siteDiskLift=0.13f;
        [SurfaceSetting("Threat zones","Battle center hue","Static floor-zone hue; does not describe current enemy presence.","turn",0f,1f,0.01f)] public float battleHue=0.025f;
        [SurfaceSetting("Threat zones","Battle center saturation","Strength of the static painted-metal floor color.","ratio",0f,0.65f,0.01f)] public float battleSaturation=0.43f;
        [SurfaceSetting("Threat zones","Battle center value","Brightness beneath units in this floor zone.","ratio",0.3f,0.85f,0.01f)] public float battleValue=0.68f;
        [SurfaceSetting("Threat zones","Contested wings hue","Static floor-zone hue; does not describe current enemy presence.","turn",0f,1f,0.01f)] public float contestedHue=0.13f;
        [SurfaceSetting("Threat zones","Contested wings saturation","Strength of the static painted-metal floor color.","ratio",0f,0.65f,0.01f)] public float contestedSaturation=0.43f;
        [SurfaceSetting("Threat zones","Contested wings value","Brightness beneath units in this floor zone.","ratio",0.3f,0.85f,0.01f)] public float contestedValue=0.7f;
        [SurfaceSetting("Threat zones","Rear pockets hue","Static floor-zone hue; does not describe current enemy presence.","turn",0f,1f,0.01f)] public float rearHue=0.39f;
        [SurfaceSetting("Threat zones","Rear pockets saturation","Strength of the static painted-metal floor color.","ratio",0f,0.65f,0.01f)] public float rearSaturation=0.25f;
        [SurfaceSetting("Threat zones","Rear pockets value","Brightness beneath units in this floor zone.","ratio",0.3f,0.85f,0.01f)] public float rearValue=0.6f;
        public Color Ceramic=>new Color(ceramicValue,ceramicValue*.985f,ceramicValue*.95f);
        public Color Core=>new Color(.10f,.58f,.72f);
        public Color Headquarters=>Color.HSVToRGB(baseHue,baseSaturation,headquartersValue);
        public Color Outpost=>Color.HSVToRGB(baseHue,baseSaturation,outpostValue);
        public Color Battle=>Color.HSVToRGB(battleHue,battleSaturation,battleValue);
        public Color Contested=>Color.HSVToRGB(contestedHue,contestedSaturation,contestedValue);
        public Color Rear=>Color.HSVToRGB(rearHue,rearSaturation,rearValue);
        public Color Deck=>Color.HSVToRGB(.57f,paintSaturation,deckValue);
        public Color Upper=>Color.HSVToRGB(.55f,paintSaturation*.35f,upperValue);
        public IEnumerable<SurfaceParameter> Parameters(){foreach(var f in GetType().GetFields()){var a=f.GetCustomAttribute<SurfaceSettingAttribute>();if(a!=null)yield return new SurfaceParameter("visual.nullDock."+f.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=>(float)f.GetValue(this),v=>f.SetValue(this,v));}}
        public void Validate(){if(id!="null-dock-art-v1"||revision<1)throw new ArgumentException("Invalid Null Dock art");foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException(p.Path);}
        public static NullDockArtProfile Load(){var asset=Resources.Load<TextAsset>("NullDockArtProfile");if(!asset)throw new InvalidOperationException("Missing Null Dock art profile");var p=JsonUtility.FromJson<NullDockArtProfile>(asset.text);p.Validate();return p;}
    }
}
