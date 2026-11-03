using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class HqUtilityAlignmentTests
{
    [UnityTest] public IEnumerator HeadquartersUtilityRowsStayCenteredInsideCompactPetals()
    {
        var profile=PlayableProfile.Default;
        var hq=new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,default,400,1,0,0,default,lifecycle:new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,600,null,null,1,300,false));
        var view=new PlayableSnapshot(profile.ProfileId,profile.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,Array.Empty<PlayableEntitySnapshot>(),new[]{hq},Array.Empty<PlayableProjectileSnapshot>(),null,null);
        var actions=OfflinePadWorldActions.Building(view,profile,1);Assert.AreEqual(2,actions.Length);
        var host=new GameObject("HQ utility alignment");var panel=ScriptableObject.CreateInstance<PanelSettings>();var target=new RenderTexture(1040,440,24);target.Create();
        try
        {
            var doc=host.AddComponent<UIDocument>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.scaleMode=PanelScaleMode.ConstantPixelSize;panel.targetTexture=target;doc.panelSettings=panel;
            var root=doc.rootVisualElement;root.style.backgroundColor=new Color(.025f,.04f,.05f);root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var title=new Label("Штаб · центровка иконок служебных действий");title.style.color=Color.white;title.style.fontSize=22;title.style.marginTop=12;title.style.marginLeft=16;root.Add(title);
            var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;row.style.marginTop=14;root.Add(row);
            var rings=new List<OrbitalBattleRing>();var widths=new[]{160,210,280};
            foreach(int width in widths)
            {
                var column=new VisualElement();column.style.width=340;column.style.alignItems=Align.Center;row.Add(column);
                var label=new Label(width+" px");label.style.color=Color.white;label.style.fontSize=17;label.style.marginBottom=14;column.Add(label);
                var ring=new OrbitalBattleRing();ring.style.position=Position.Relative;ring.style.display=DisplayStyle.Flex;ring.Resize(width);ring.ConfigureInput(true);column.Add(ring);rings.Add(ring);
            }
            for(int frame=0;frame<8;frame++)
            {
                foreach(var ring in rings){ring.Set(actions,"Штаб","1:sale");ring.Q<RadialActionCard>().style.display=DisplayStyle.None;}
                yield return null;
            }
            var errors=new List<string>();
            for(int i=0;i<rings.Count;i++)
            {
                var ring=rings[i];Assert.AreEqual(widths[i],ring.worldBound.width,.1f,"Wheel must be rendered at the requested size");var buttons=ring.Query<Button>().ToList().Where(b=>b.GetType().Name=="SectorButton").ToArray();Assert.AreEqual(2,buttons.Length);
                for(int sector=0;sector<buttons.Length;sector++)
                {
                    var button=buttons[sector];var icon=button.Q<RadialArtwork>();var titleLabel=button.Q<Label>("radial-sector-title");var price=button.Q<Label>("radial-sector-count");
                    float offset=icon.worldBound.center.x-ring.worldBound.center.x;
                    if(Mathf.Abs(offset)>.75f)errors.Add(widths[i]+" px "+titleLabel.text+" icon x-offset="+offset.ToString("F2"));
                    foreach(var element in new VisualElement[]{icon,titleLabel,price})
                    {
                        if(element==price&&string.IsNullOrEmpty(price.text))continue;
                        var rect=element.worldBound;rect.position-=ring.worldBound.position;
                        foreach(var corner in new[]{rect.min,rect.max,new Vector2(rect.xMin,rect.yMax),new Vector2(rect.xMax,rect.yMin)})
                            if(!OrbitalBattleRing.SectorContains(corner,widths[i],sector,2))errors.Add(widths[i]+" "+titleLabel.text+" outside petal: "+element.GetType().Name+" "+rect);
                    }
                    if(icon.worldBound.Overlaps(titleLabel.worldBound)||titleLabel.worldBound.Overlaps(price.worldBound))errors.Add(widths[i]+" "+titleLabel.text+" rows overlap");
                }
            }
            string output=Environment.GetEnvironmentVariable("HQ_ICON_EVIDENCE")??Path.GetFullPath("../.local/center-hq-sale-icon/capture.png");Directory.CreateDirectory(Path.GetDirectoryName(output));
            var prior=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try{image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(output,image.EncodeToPNG());}
            finally{RenderTexture.active=prior;UnityEngine.Object.DestroyImmediate(image);}
            Assert.IsEmpty(errors,string.Join("\n",errors));
        }
        finally
        {panel.targetTexture=null;UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);target.Release();UnityEngine.Object.DestroyImmediate(target);}
    }
}
