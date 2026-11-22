using System.Collections;
using NUnit.Framework;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
public sealed class OrbitalHudArtworkTests
{
    [Test] public void ApprovedHackChipHasAlphaAndIsUsedByRadialArtwork()
    {
        var icon=Resources.Load<Texture2D>("OrbitalIcons/control-hack");Assert.NotNull(icon);Assert.LessOrEqual(icon.width,256);Assert.LessOrEqual(icon.height,256);
        Assert.True(UnityEngine.Experimental.Rendering.GraphicsFormatUtility.HasAlphaChannel(icon.graphicsFormat));
        var art=new RadialArtwork();art.Set("orbital:8");Assert.True(System.Linq.Enumerable.Any(art.Children(),e=>e.style.display.value==DisplayStyle.Flex&&e.style.backgroundImage.value.texture==icon));
        art.Set("orbital:1");Assert.False(System.Linq.Enumerable.Any(art.Children(),e=>e.style.display.value==DisplayStyle.Flex&&e.style.backgroundImage.value.texture==icon),"reused radial slot must not retain another ability's icon");
    }
    [Test] public void AcceleratedProductionUsesTransparentSharedArtwork()
    {
        var icon = Resources.Load<Texture2D>("OrbitalIcons/accelerated-production");
        Assert.NotNull(icon);
        Assert.AreEqual(256, icon.width);
        Assert.AreEqual(256, icon.height);
        Assert.True(UnityEngine.Experimental.Rendering.GraphicsFormatUtility.HasAlphaChannel(icon.graphicsFormat));
        var radial = new RadialArtwork();
        radial.Set("orbital:" + Spacewars.Simulation.OrbitalAbilities.AcceleratedProduction);
        Assert.True(System.Linq.Enumerable.Any(radial.Children(), child =>
            child.style.display.value == DisplayStyle.Flex && child.style.backgroundImage.value.texture == icon));
    }
    [Test] public void RocketLauncherKeepsOneTripleMarkAcrossResearchArtwork()
    {
        var rocket=Spacewars.Simulation.PlayableResearchKind.ExplorerRocketLauncher;
        var assault=Spacewars.Simulation.PlayableResearchKind.ExplorerAssaultGuns;
        string key=RadialActionPresentation.ResearchArtwork(rocket);
        Assert.AreEqual(key,OrbitalPrecision.ResearchGlyph(rocket));
        Assert.AreEqual(key,OrbitalPrecision.Glyph("science:"+rocket));

        void Check(VisualElement root,int count)
        {
            var visible=root.Query<PortraitUpgradeMark>().ToList()
                .FindAll(mark=>mark.style.display.value==DisplayStyle.Flex);
            Assert.AreEqual(1,visible.Count);
            Assert.AreEqual(count,visible[0].ChevronCount);
        }

        var radial=new RadialArtwork();
        radial.Set(RadialActionPresentation.ResearchArtwork(assault),upgraded:true);
        Check(radial,2);
        radial.Set(key,upgraded:true);
        Check(radial,3);
        radial.Set(RadialActionPresentation.ResearchArtwork(assault),upgraded:true);
        Check(radial,2);

        var button=new Button();
        OrbitalPrecision.QueueCard(button,key,.4,true);
        Check(button.Q<ModelPortrait>("order-artwork"),3);

        var glyph=new OrbitalGlyph(key);
        Check(glyph,3);
        glyph.Kind=OrbitalPrecision.ResearchGlyph(assault);
        Assert.AreEqual(DisplayStyle.None,glyph.Q<PortraitUpgradeMark>().style.display.value);
    }
    [Test] public void HackCooldownGroupsWithThresholdAndSurvivesProfileJson()
    {
        var field=System.Linq.Enumerable.Single(Spacewars.Simulation.PlayableProfileMetadata.Fields,f=>f.FieldName=="orbitalHackSeconds");
        var labels=typeof(PlayableBootstrap).Assembly.GetType("Spacewars.Presentation.NativeBalanceLabels",true);var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
        Assert.AreEqual("Эскалация",labels.GetMethod("Section",flags).Invoke(null,new object[]{field}));Assert.AreEqual("Взлом управления",labels.GetMethod("Entity",flags).Invoke(null,new object[]{field}));
        var data=Spacewars.Simulation.PlayableProfile.Default.CopyData();field.Write(data,47);var restored=JsonUtility.FromJson<Spacewars.Simulation.PlayableProfileData>(JsonUtility.ToJson(data));Assert.AreEqual(47,restored.orbitalHackSeconds);Assert.AreEqual(data.orbitalBasicSeconds,restored.orbitalBasicSeconds);
    }
    [UnityTest] public IEnumerator RecoverySectorFillsClockwiseFromTwelve()
    {
        var host=new GameObject("orbital sweep render");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.scaleMode=PanelScaleMode.ConstantPixelSize;panel.scale=1;var texture=new RenderTexture(64,64,24);texture.Create();panel.targetTexture=texture;
        var document=host.AddComponent<UIDocument>();document.panelSettings=panel;var root=document.rootVisualElement;root.style.width=root.style.height=64;root.style.backgroundColor=Color.black;
        var sweep=new OrbitalCooldownSweep();sweep.style.position=Position.Absolute;sweep.style.left=sweep.style.right=sweep.style.top=sweep.style.bottom=0;sweep.style.width=sweep.style.height=64;root.Add(sweep);
        Texture2D pixels=null;var previous=RenderTexture.active;
        try{
            sweep.Fraction=.25f;for(int i=0;i<8;i++)yield return null;
            Assert.AreEqual(64,sweep.worldBound.width,.1f);Assert.AreEqual(64,sweep.worldBound.height,.1f);Assert.AreEqual(0,sweep.worldBound.xMin,.1f);Assert.AreEqual(0,sweep.worldBound.yMin,.1f);
            RenderTexture.active=texture;pixels=new Texture2D(64,64,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,64,64),0,0);pixels.Apply();
            var folder=System.Environment.GetEnvironmentVariable("ORDINARY_PAD_EVIDENCE");if(!string.IsNullOrEmpty(folder)){System.IO.Directory.CreateDirectory(folder);System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,"clockwise-quarter.png"),pixels.EncodeToPNG());}
            Assert.Greater(pixels.GetPixel(48,48).grayscale,.1f,"first quarter occupies upper-right");Assert.Less(pixels.GetPixel(16,48).grayscale,.05f);Assert.Less(pixels.GetPixel(48,16).grayscale,.05f);
            sweep.Fraction=.75f;for(int i=0;i<8;i++)yield return null;RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,64,64),0,0);pixels.Apply();
            if(!string.IsNullOrEmpty(folder))System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,"clockwise-three-quarters.png"),pixels.EncodeToPNG());
            Assert.Greater(pixels.GetPixel(48,16).grayscale,.1f);Assert.Greater(pixels.GetPixel(16,16).grayscale,.1f);Assert.Less(pixels.GetPixel(16,48).grayscale,.05f,"last quarter remains upper-left");
        }finally{RenderTexture.active=previous;if(pixels!=null)Object.DestroyImmediate(pixels);panel.targetTexture=null;texture.Release();Object.DestroyImmediate(texture);Object.DestroyImmediate(host);Object.DestroyImmediate(panel);}
    }
}
