using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class PlayableDominationHudTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly Type FlagType=typeof(PlayableBootstrap).GetNestedType("DominationFlag",BindingFlags.NonPublic);
    static readonly Color Blue=new Color(.2f,.55f,1),Orange=new Color(1,.45f,.15f);
    static VisualElement Flag()=>(VisualElement)Activator.CreateInstance(FlagType,true);
    static void Update(VisualElement flag,PlayableDominationSnapshot state)=>(FlagType.GetMethod("UpdateValue",Flags)).Invoke(flag,new object[]{state,Blue,Orange,0L});

    [Test]
    public void CountdownFollowsAuthoritativeSecondsAcrossGainLossResetAndVictory()
    {
        var flag=Flag();
        void Check(int lead,int team,double points,double seconds,string expected)
        {
            Update(flag,new PlayableDominationSnapshot(lead,team,points,seconds));
            Assert.AreEqual(expected,flag.Q<Label>().text);
        }
        Assert.AreEqual("—",flag.Q<Label>().text);
        Check(0,0,0,200,"—");
        Check(1,1,0,200,"200");
        Check(1,1,.01,199.99,"200");
        Check(1,1,1,199,"199");
        Check(1,1,57,143,"143");
        Check(2,1,56,144,"144");
        Check(0,1,.1,199.9,"200");
        Check(0,0,0,200,"—");
        Check(2,2,0,200,"200");
        Check(2,2,110,90,"90");
        Assert.AreEqual(new Color(1,.70f,.35f),flag.Q<Label>().style.color.value);
        Check(2,2,190,10,"10");
        Assert.AreEqual(new Color(1,.35f,.42f),flag.Q<Label>().style.color.value);
        Check(2,2,199.99,.01,"1");
        Check(2,2,200,0,"0");
        // A custom profile's target/gain is already reflected in snapshot seconds.
        Check(1,1,0,150,"150");
        Check(1,1,1,149.5,"150");
        Check(1,1,2,149,"149");
    }

    [UnityTest]
    public IEnumerator SharedFlagRendersNeutralRisingAndDisplacedCountdowns()
    {
        var host=new GameObject("domination HUD rendering");
        var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        typeof(PlayableBootstrap).GetField("profile",Flags).SetValue(hud,PlayableProfile.Default);
        typeof(PlayableBootstrap).GetMethod("CreateHud",Flags).Invoke(hud,null);
        var root=(VisualElement)typeof(PlayableBootstrap).GetField("root",Flags).GetValue(hud);
        var panel=host.GetComponent<UIDocument>().panelSettings;
        var texture=new RenderTexture(800,450,0);texture.Create();panel.targetTexture=texture;
        var pixels=new Texture2D(800,450,TextureFormat.RGBA32,false);
        try
        {
            root.Clear();root.style.width=800;root.style.height=450;root.style.backgroundColor=new Color(.09f,.14f,.18f);
            var states=new[]{new PlayableDominationSnapshot(0,0,0,200),new PlayableDominationSnapshot(1,1,57,143),new PlayableDominationSnapshot(2,1,57,143),new PlayableDominationSnapshot(1,1,190,10)};
            var captions=new[]{"No score","To victory","Displaced","Final seconds"};
            for(int i=0;i<states.Length;i++)
            {
                var flag=Flag();flag.style.left=100+i*200;flag.style.top=100;root.Add(flag);Update(flag,states[i]);
                var label=new Label(captions[i]);label.style.position=Position.Absolute;label.style.left=35+i*200;label.style.top=190;label.style.color=Color.white;root.Add(label);
            }
            for(int frame=0;frame<10;frame++)yield return null;
            foreach(var flag in root.Query<VisualElement>(name:"domination-flag").ToList())
            {
                Assert.AreEqual(50,flag.resolvedStyle.width,.01);
                Assert.AreEqual(62,flag.resolvedStyle.height,.01);
                Assert.Greater(flag.Q<Label>().resolvedStyle.width,0);
            }
            var previous=RenderTexture.active;
            RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,800,450),0,0);pixels.Apply();RenderTexture.active=previous;
            var folder=Environment.GetEnvironmentVariable("SPACEWARS_DOMINATION_HUD_EVIDENCE");
            if(!string.IsNullOrEmpty(folder)){Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"domination-countdown.png"),pixels.EncodeToPNG());}
            Assert.AreEqual("—",root.Query<Label>().ToList()[0].text);
        }
        finally
        {
            panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(host);
        }
    }
}
