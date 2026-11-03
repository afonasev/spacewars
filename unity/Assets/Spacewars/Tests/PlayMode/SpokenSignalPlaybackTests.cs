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
namespace Spacewars.Tests.PlayMode
{
    public sealed class SpokenSignalPlaybackTests
    {
        [Test] public void EveryApprovedVoiceClipLoadsAndDecodesWithHeadroom()
        {
            foreach(var key in SpokenSignalCatalog.Phrases.Keys)
            {
                var clip=Resources.Load<AudioClip>("Sfx/Voice/"+key);Assert.NotNull(clip,key);Assert.AreEqual(1,clip.channels,key);Assert.AreEqual(48000,clip.frequency,key);
                Assert.That(clip.length,Is.InRange(.2f,4f),key);Assert.True(clip.LoadAudioData(),key);
                var pcm=new float[clip.samples];Assert.True(clip.GetData(pcm,0),key);
                Assert.Greater(pcm.Count(v=>Math.Abs(v)>.005),100,key);Assert.Less(pcm.Max(v=>Math.Abs(v)),.75,key);
            }
            Assert.AreEqual(22,SpokenSignalCatalog.Phrases.Count);
        }
        [Test] public void SilentStartupNeverCreatesVoiceOutputOrReplaysDiscardedFacts()
        {
            var host=new GameObject("Silent speech verification");
            try
            {
                var player=host.AddComponent<SpokenSignalPlayer>();player.Initialize(new GameplayAudioProfile());
                if(NativeSilentStartup.IsSilent)Assert.IsEmpty(host.GetComponents<AudioSource>());
                player.Tick(Array.Empty<GameplayAudioScreen>(),false,0);Assert.AreEqual(0,player.Played);Assert.Zero(player.OutputGain);
            }finally{UnityEngine.Object.DestroyImmediate(host);}
        }
        [UnityTest] public IEnumerator MutedRealSpeechChannelRoutesVictimAndKeepsIndependentEnableAndGain()
        {
            string gainKey=NativeAudioSettings.Prefix+"voice",enabledKey=NativeAudioSettings.Prefix+"voiceEnabled";
            bool hasGain=PlayerPrefs.HasKey(gainKey),hasEnabled=PlayerPrefs.HasKey(enabledKey);float oldGain=PlayerPrefs.GetFloat(gainKey);int oldEnabled=PlayerPrefs.GetInt(enabledKey);
            bool suppressed=GameplayAudioPlayer.DiagnosticSuppressOutput;var host=new GameObject("Muted real speech channel");
            PlayableSnapshot View(long tick,PlayableOwner owner,params PlayableMatchEvent[] facts)=>new PlayableSnapshot("test",1,1,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,
                Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),null,null,owner:owner,ownerId:owner==PlayableOwner.Player?"player-1":"enemy-1",team:(int)owner).WithMatchEvents(facts);
            GameplayAudioScreen[] Screens(long tick,params PlayableMatchEvent[] facts)=>new[]{new GameplayAudioScreen(View(tick,PlayableOwner.Player,facts.Where(f=>f.Owner==PlayableOwner.Player).ToArray()),default,new NavPoint(1,0)),new GameplayAudioScreen(View(tick,PlayableOwner.Enemy,facts.Where(f=>f.Owner==PlayableOwner.Enemy).ToArray()),default,new NavPoint(1,0))};
            try
            {
                PlayerPrefs.SetFloat(gainKey,.7f);PlayerPrefs.SetInt(enabledKey,1);GameplayAudioPlayer.DiagnosticSuppressOutput=true;
                var speech=host.AddComponent<SpokenSignalPlayer>();speech.InitializeMutedForTests(new GameplayAudioProfile());
                speech.Tick(Screens(0),true,0);
                speech.Tick(Screens(1,new PlayableMatchEvent(1,1,PlayableOwner.Player,MatchEventKind.UnitCaptured,default)),true,1);
                var source=host.GetComponent<AudioSource>();Assert.NotNull(source,"diagnostic source");Assert.NotNull(source.clip,"selected="+speech.LastKey+" played="+speech.Played+" pending="+speech.Plan.Pending+" enabled="+NativeAudioSettings.VoiceEnabled+" gain="+NativeAudioSettings.Voice);Assert.True(source.mute);Assert.Zero(source.volume);Assert.AreEqual("system-unit-captured",source.clip.name);Assert.AreEqual(1,speech.Played);
                double afterFirst=1+source.clip.length+.01;source.Stop();
                speech.Tick(Screens(2,new PlayableMatchEvent(2,2,PlayableOwner.Player,MatchEventKind.MineHit,default,99),new PlayableMatchEvent(3,2,PlayableOwner.Enemy,MatchEventKind.MineTriggered,default,99)),true,afterFirst);
                Assert.AreEqual("system-mine-hit",speech.LastKey);Assert.AreEqual(2,speech.Played);Assert.Zero(speech.Plan.Pending);
                PlayerPrefs.SetInt(enabledKey,0);speech.Tick(Screens(3,new PlayableMatchEvent(4,3,PlayableOwner.Player,MatchEventKind.ResearchCompleted,default)),true,afterFirst+1);Assert.Zero(source.volume);Assert.False(source.isPlaying);
                PlayerPrefs.SetInt(enabledKey,1);speech.Tick(Screens(3,new PlayableMatchEvent(4,3,PlayableOwner.Player,MatchEventKind.ResearchCompleted,default)),true,afterFirst+1.1);Assert.AreEqual(2,speech.Played,"Enable never replays consumed events");
                speech.Tick(Screens(3),false,afterFirst+2);Assert.Zero(speech.Plan.Pending);Assert.Zero(source.volume);
                speech.Tick(Screens(10),true,10);
                speech.Tick(Screens(11,new PlayableMatchEvent(5,11,PlayableOwner.Player,MatchEventKind.UnitReady,default)),true,11);
                Assert.True(speech.Busy);Assert.NotNull(source.clip);
                var overview=new GameplayAudioScreen(View(11,PlayableOwner.Player),default,new NavPoint(1,0),overview:true);
                speech.Tick(new[]{overview},true,11.1);Assert.False(speech.Busy);Assert.Null(source.clip,"Overview immediately stops the previous perspective's clip");Assert.Zero(speech.Plan.Pending);
                speech.Tick(new[]{new GameplayAudioScreen(View(12,PlayableOwner.Enemy,new PlayableMatchEvent(6,12,PlayableOwner.Enemy,MatchEventKind.UnitCaptured,default)),default,new NavPoint(1,0))},true,12);
                Assert.False(speech.Busy);Assert.Zero(speech.Plan.Pending,"Retained events of a newly selected perspective are baselined, not replayed");
                yield return null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);GameplayAudioPlayer.DiagnosticSuppressOutput=suppressed;
                if(hasGain)PlayerPrefs.SetFloat(gainKey,oldGain);else PlayerPrefs.DeleteKey(gainKey);if(hasEnabled)PlayerPrefs.SetInt(enabledKey,oldEnabled);else PlayerPrefs.DeleteKey(enabledKey);PlayerPrefs.Save();
            }
        }
        [UnityTest] public IEnumerator NativeSettingsExposeIndependentVoiceToggleAndGainWithSilentWriteGuard()
        {
            string gainKey=NativeAudioSettings.Prefix+"voice",enabledKey=NativeAudioSettings.Prefix+"voiceEnabled";
            bool hasGain=PlayerPrefs.HasKey(gainKey),hasEnabled=PlayerPrefs.HasKey(enabledKey);float oldGain=PlayerPrefs.GetFloat(gainKey);int oldEnabled=PlayerPrefs.GetInt(enabledKey);
            var host=new GameObject("Speech settings capture");var panel=ScriptableObject.CreateInstance<PanelSettings>();var target=new RenderTexture(1280,900,0);target.Create();NativeMenuNavigation navigation=null;
            try
            {
                // Test setup restores exact prior keys; app handlers themselves must respect -muted.
                NativePreferenceWriter.SetFloat(gainKey,.45f);NativePreferenceWriter.SetInt(enabledKey,0);
                Assert.AreEqual(.45f,NativeAudioSettings.Voice,.001);Assert.False(NativeAudioSettings.VoiceEnabled);
                panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.targetTexture=target;
                var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;OrbitalTheme.ConfigurePanel(panel,root);navigation=new NativeMenuNavigation(root);
                var page=NativeSettingsView.Open(root,navigation,()=>{});for(int i=0;i<5;i++)yield return null;
                var gain=page.Q<Slider>("settings-audio-voice");var enabled=page.Q<Toggle>("settings-audio-voice-enabled");
                Assert.NotNull(gain);Assert.NotNull(enabled);Assert.AreEqual(.45f,gain.value,.001);Assert.False(enabled.value);
                float effects=NativeAudioSettings.Effects,signals=NativeAudioSettings.Notifications;
                gain.value=.7f;enabled.value=true;
                if(NativeSilentStartup.IsSilent){Assert.AreEqual(.45f,NativeAudioSettings.Voice,.001);Assert.False(NativeAudioSettings.VoiceEnabled);}
                else{Assert.AreEqual(.7f,NativeAudioSettings.Voice,.001);Assert.True(NativeAudioSettings.VoiceEnabled);}
                Assert.AreEqual(effects,NativeAudioSettings.Effects);Assert.AreEqual(signals,NativeAudioSettings.Notifications);
                gain.value=.45f;enabled.value=false;
                page.Q<Button>("settings-tab-Звук").Focus();navigation.Activate();
                var scroll=page.Q<ScrollView>("settings-scroll");scroll.scrollOffset=Vector2.zero;
                for(int i=0;i<6;i++)yield return null;
                string folder=Environment.GetEnvironmentVariable("SPOKEN_SIGNAL_EVIDENCE")??Path.GetFullPath("../.local/spoken-match-signals/captures");Directory.CreateDirectory(folder);
                var prior=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                try{image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(folder,"voice-settings.png"),image.EncodeToPNG());}
                finally{UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=prior;}
            }
            finally
            {
                navigation?.Dispose();panel.targetTexture=null;UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);target.Release();UnityEngine.Object.DestroyImmediate(target);
                if(hasGain)PlayerPrefs.SetFloat(gainKey,oldGain);else PlayerPrefs.DeleteKey(gainKey);if(hasEnabled)PlayerPrefs.SetInt(enabledKey,oldEnabled);else PlayerPrefs.DeleteKey(enabledKey);PlayerPrefs.Save();
            }
        }
    }
}
