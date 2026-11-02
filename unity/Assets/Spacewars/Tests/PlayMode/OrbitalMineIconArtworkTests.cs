using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class OrbitalMineIconArtworkTests
{
    [UnityTest] public IEnumerator ApprovedArtworkKeepsAlphaAndRendersAtCardHudAndRadialSizes()
    {
        var texture=Resources.Load<Texture2D>("OrbitalIcons/mine-trap");Assert.NotNull(texture);
        var probe=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32);
        var previous=RenderTexture.active;var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);
        try{Graphics.Blit(texture,probe);RenderTexture.active=probe;pixels.ReadPixels(new Rect(0,0,probe.width,probe.height),0,0);pixels.Apply();Assert.Less(pixels.GetPixel(0,0).a,.01f,"transparent surroundings");Assert.Greater(pixels.GetPixel(probe.width/2,probe.height/2).a,.95f,"opaque metal plate");}
        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(probe);UnityEngine.Object.DestroyImmediate(pixels);}
        var host=new GameObject("Mine ability artwork QA");var panel=ScriptableObject.CreateInstance<PanelSettings>();var target=new RenderTexture(800,360,24);target.Create();Texture2D capture=null;
        try{
            panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.scaleMode=PanelScaleMode.ConstantPixelSize;panel.scale=1;panel.targetTexture=target;
            var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;root.style.width=800;root.style.height=360;root.style.backgroundColor=OrbitalTheme.Panel;OrbitalTheme.Install(root);
            void Caption(string text,float x){var label=new Label(text);label.style.position=Position.Absolute;label.style.left=x;label.style.top=25;label.style.fontSize=16;label.style.color=OrbitalTheme.Ink;root.Add(label);}
            RadialArtwork Icon(float x,float y,float size){var art=new RadialArtwork();art.style.position=Position.Absolute;art.style.left=x;art.style.top=y;art.style.width=art.style.height=size;art.Set("orbital:2");root.Add(art);Assert.AreSame(texture,art.Children().First().style.backgroundImage.value.texture);return art;}
            Caption("Карточка · 220",20);Caption("HUD · 36",300);Caption("Колесо · 70",430);Caption("Откат · 36",620);
            var card=Icon(20,65,220);Icon(300,130,36);Icon(430,110,70);
            var block=new VisualElement();block.style.position=Position.Absolute;block.style.left=620;block.style.top=130;block.style.width=block.style.height=36;root.Add(block);
            var sweep=new OrbitalCooldownSweep{Fraction=.5f};sweep.style.position=Position.Absolute;sweep.style.left=sweep.style.right=sweep.style.top=sweep.style.bottom=0;block.Add(sweep);
            var cooling=new RadialArtwork();cooling.style.position=Position.Absolute;cooling.style.left=cooling.style.right=cooling.style.top=cooling.style.bottom=0;cooling.style.opacity=.25f;cooling.Set("orbital:2");block.Add(cooling);
            var seconds=new Label("15");seconds.style.position=Position.Absolute;seconds.style.left=seconds.style.right=seconds.style.top=seconds.style.bottom=0;seconds.style.unityTextAlign=TextAnchor.MiddleCenter;seconds.style.fontSize=16;seconds.style.color=OrbitalTheme.Ink;seconds.style.marginLeft=seconds.style.marginRight=seconds.style.marginTop=seconds.style.marginBottom=0;block.Add(seconds);
            card.Set("orbital:1");Assert.AreSame(Resources.Load<Texture2D>("OrbitalIcons/scan"),card.Children().First().style.backgroundImage.value.texture);
            card.Set("orbital:8");Assert.AreSame(Resources.Load<Texture2D>("OrbitalIcons/control-hack"),card.Children().First().style.backgroundImage.value.texture);
            card.Set("orbital:2");Assert.AreSame(texture,card.Children().First().style.backgroundImage.value.texture);
            for(int i=0;i<8;i++)yield return null;
            RenderTexture.active=target;capture=new Texture2D(800,360,TextureFormat.RGB24,false);capture.ReadPixels(new Rect(0,0,800,360),0,0);capture.Apply();
            int visible=0;for(int x=300;x<336;x++)for(int y=194;y<230;y++)if(capture.GetPixel(x,y).maxColorComponent>.25f)visible++;Assert.Greater(visible,20,"small HUD silhouette remains visible");
            var output=Environment.GetEnvironmentVariable("ORDINARY_PAD_EVIDENCE");if(!string.IsNullOrEmpty(output)){Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,"mine-icon-native-sizes.png"),capture.EncodeToPNG());}
        }finally{RenderTexture.active=previous;panel.targetTexture=null;if(capture)UnityEngine.Object.DestroyImmediate(capture);target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);}
    }
}
