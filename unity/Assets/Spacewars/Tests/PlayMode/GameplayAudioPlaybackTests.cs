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

public sealed class GameplayAudioPlaybackTests
{
    private GameObject host;
    private float listenerVolume;
    private string savedAudioProfile;
    private readonly Dictionary<string,float> saved=new Dictionary<string,float>();
    [SetUp] public void Setup()
    {
        savedAudioProfile=PlayerPrefs.HasKey(NativeGameplayAudioProfile.Key)?PlayerPrefs.GetString(NativeGameplayAudioProfile.Key):null;PlayerPrefs.DeleteKey(NativeGameplayAudioProfile.Key);NativeGameplayAudioProfile.Reload();
        saved.Clear();foreach(var key in new[]{"music","effects","notifications"})
        {var full=NativeAudioSettings.Prefix+key;if(PlayerPrefs.HasKey(full))saved[full]=PlayerPrefs.GetFloat(full);PlayerPrefs.DeleteKey(full);}
        listenerVolume=AudioListener.volume;AudioListener.volume=0;GameplayAudioPlayer.DiagnosticSuppressOutput=true;
        host=new GameObject("Gameplay audio test");
    }
    [TearDown] public void Cleanup()
    {
        if(host)UnityEngine.Object.DestroyImmediate(host);
        foreach(var key in new[]{"music","effects","notifications"})
        {var full=NativeAudioSettings.Prefix+key;if(saved.TryGetValue(full,out var v))PlayerPrefs.SetFloat(full,v);else PlayerPrefs.DeleteKey(full);}
        if(savedAudioProfile==null)PlayerPrefs.DeleteKey(NativeGameplayAudioProfile.Key);else PlayerPrefs.SetString(NativeGameplayAudioProfile.Key,savedAudioProfile);NativeGameplayAudioProfile.Reload();
        PlayerPrefs.Save();AudioListener.volume=listenerVolume;GameplayAudioPlayer.DiagnosticSuppressOutput=false;
    }
    private static PlayableSnapshot Frame(long tick,PlayableSoundEvent[] sounds=null,PlayableEntitySnapshot[] units=null,long generation=1)
        =>new PlayableSnapshot("test",1,generation,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,
            units??Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sounds:sounds);
    private static GameplayAudioScreen Screen(PlayableSnapshot v,double x=0)=>new GameplayAudioScreen(v,new NavPoint(x,0),new NavPoint(1,0));
    private static PlayableSoundEvent Shot(long id,long tick,double x=0)=>new PlayableSoundEvent(id,tick,PlayableSoundKind.Shot,new NavPoint(x,0),PlayableEntityKind.Tank);
    private GameplayAudioPlayer Player(){var p=host.AddComponent<GameplayAudioPlayer>();p.Initialize();p.Tick(new[]{Screen(Frame(0))},true,.1);return p;}

    [Test] public void ImportedHybridClipsHavePeakHeadroomAndMovementHasQuietSeam()
    {
        foreach(var kind in new[]{"order","reject","movement-loop","tank-shot","burst-shot","impact","explosion","construction","demolition","repair","damage","motion-start","motion-stop","queued","work-start","ready","cancel","warning","select","ui","match-start","pause","result"})
        {
            int n=kind=="tank-shot"||kind=="burst-shot"||kind=="impact"||kind=="explosion"||kind=="construction"||kind=="demolition"||kind=="repair"||kind=="damage"?3:1;
            for(int i=1;i<=n;i++)
            {
                var clip=Resources.Load<AudioClip>("Sfx/Hybrid/"+kind+"-"+i);Assert.NotNull(clip);
                Assert.AreEqual(1,clip.channels);var samples=new float[clip.samples];Assert.IsTrue(clip.GetData(samples,0));
                Assert.Less(samples.Max(x=>Math.Abs(x)),.70);
                if(kind=="movement-loop")Assert.Less(Math.Abs(samples[0]-samples[samples.Length-1]),.03);
            }
        }
    }
    [UnityTest] public IEnumerator SharedPoolPlaysOnceAdvancesAndResetsWithoutOldSounds()
    {
        var p=Player();var frame=Frame(1,new[]{Shot(1,1)});
        p.Tick(new[]{Screen(frame),Screen(frame)},true,.1);
        Assert.AreEqual(1,p.PlayedEvents);Assert.AreEqual(12,p.PoolSize);
        yield return new WaitForSecondsRealtime(.15f);
        var source=host.GetComponentsInChildren<AudioSource>().Single(s=>s.isPlaying);
        Assert.Greater(source.timeSamples,0);Assert.AreEqual(0,source.volume,"Diagnostic output remains silent.");
        p.Tick(new[]{Screen(frame)},false,.1);Assert.Zero(p.ActiveVoices);Assert.IsFalse(source.isPlaying);
        p.Tick(new[]{Screen(frame)},true,.1);Assert.AreEqual(1,p.PlayedEvents,"Resume baselines old event.");
        p.Tick(new[]{Screen(Frame(2,new[]{Shot(2,2)}))},true,.1);Assert.AreEqual(2,p.PlayedEvents);
        p.Tick(new[]{Screen(Frame(0,new[]{Shot(1,0)},generation:2))},true,.1);Assert.Zero(p.ActiveVoices);
        Assert.AreEqual(13,host.GetComponentsInChildren<AudioSource>().Length);
    }
    [Test] public void SingleSourceVolumeFollowsCameraDistanceWithoutBudgetFlattening()
    {
        var p=Player();GameplayAudioPlayer.DiagnosticSuppressOutput=false; // Master remains zero.
        var frame=Frame(1,new[]{Shot(1,1,8)});p.Tick(new[]{Screen(frame)},true,1);
        var source=host.GetComponentsInChildren<AudioSource>().Single(s=>s.isPlaying);
        float near=source.volume;Assert.AreEqual(NativeGameplayAudioProfile.Current.shotGain,near,.001);
        p.Tick(new[]{Screen(frame,-12)},true,1);float middle=source.volume;
        p.Tick(new[]{Screen(frame,-32)},true,1);float far=source.volume;
        Assert.Greater(near,middle);Assert.Greater(middle,far);Assert.Greater(far,0);
    }
    [Test] public void DenseMixNeverExceedsVoicesOrGainBudgetAndEffectsMuteIsIndependent()
    {
        var p=Player();GameplayAudioPlayer.DiagnosticSuppressOutput=false; // Master stays zero.
        for(int tick=1;tick<20;tick++)
        {
            var events=Enumerable.Range(1,80).Select(i=>Shot(tick*100+i,tick,(i%7)*9)).ToArray();
            p.Tick(new[]{Screen(Frame(tick,events))},true,.033);
            Assert.LessOrEqual(p.ActiveVoices,12);Assert.LessOrEqual(p.EffectsGainSum,.38001);
        }
        NativeAudioSettings.Effects=0;p.Tick(new[]{Screen(Frame(20))},true,.1);
        Assert.Zero(p.EffectsGainSum);Assert.IsTrue(host.GetComponentsInChildren<AudioSource>().Where(s=>s.name.StartsWith("Effect ")).All(s=>s.volume==0));
        Assert.AreEqual(1,NativeAudioSettings.Music);Assert.AreEqual(1,NativeAudioSettings.Notifications);Assert.Zero(AudioListener.volume);
    }
    [UnityTest] public IEnumerator MovingColumnUsesOneLoopAndStopsWhenNoLongerVisible()
    {
        var p=Player();var units=Enumerable.Range(1,30).Select(i=>new PlayableEntitySnapshot(i,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(i*.1,0),100,true,0,0,0)).ToArray();
        var frame=Frame(1,units:units);p.Tick(new[]{Screen(frame),Screen(frame)},true,.1);
        yield return new WaitForSecondsRealtime(.15f);
        Assert.AreEqual(1,host.GetComponentsInChildren<AudioSource>().Count(s=>s.isPlaying&&s.loop));
        for(int i=2;i<30;i++)p.Tick(new[]{Screen(Frame(i))},true,.1);
        Assert.Zero(p.ActiveVoices);
    }
    [UnityTest] public IEnumerator HumanGroupSignalIsOnceAndAiReceiptsAreSilent()
    {
        var p=Player();var command=new PlayableCommand(1,10,"player-1",PlayableCommandKind.Move,new[]{1,2,3,4});
        p.Command(command,true);p.Command(command,true);Assert.AreEqual(1,p.PlayedNotifications);
        p.Command(new PlayableCommand(1,12,"enemy-1",PlayableCommandKind.Attack,new[]{9},origin:PlayableOrderOrigin.Ai),true);
        p.Receipt(new PlayableCommandReceipt(12,1,PlayableCommandStatus.InvalidTarget,null,0,"enemy-1"),1);
        Assert.AreEqual(1,p.PlayedNotifications);
        yield return new WaitForSecondsRealtime(.4f);
        p.Receipt(new PlayableCommandReceipt(10,2,PlayableCommandStatus.InvalidTarget,null,0,"player-1"),1);
        p.Receipt(new PlayableCommandReceipt(10,2,PlayableCommandStatus.InvalidTarget,null,0,"player-1"),1);
        Assert.AreEqual(2,p.PlayedNotifications);
    }
    [UnityTest] public IEnumerator ListenerGuardRecordsActualUnityDspMixAndSuppressesPhysicalOutput()
    {
        var p=Player();var listener=UnityEngine.Object.FindAnyObjectByType<AudioListener>();var guard=listener.GetComponent<GameplayMixGuard>();
        Assert.NotNull(guard);guard.SuppressOutput=true;GameplayAudioPlayer.DiagnosticSuppressOutput=false;AudioListener.volume=1;
        guard.BeginCapture(AudioSettings.outputSampleRate*8*8);
        var music=host.AddComponent<AdaptiveMusicPlayer>();music.Initialize();
        float start=Time.realtimeSinceStartup;int ordinal=0;long tick=1;double next=.5;
        while(Time.realtimeSinceStartup-start<8)
        {
            double elapsed=Time.realtimeSinceStartup-start;var events=new List<PlayableSoundEvent>();
            if(elapsed>=next)
            {
                next+=elapsed<4?.65:.12;ordinal++;
                var catalogue=new[]{PlayableSoundKind.Shot,PlayableSoundKind.ConstructionStarted,PlayableSoundKind.RepairStarted,PlayableSoundKind.DemolitionStarted,PlayableSoundKind.ProductionComplete,PlayableSoundKind.ResearchComplete,PlayableSoundKind.BaseThreat,PlayableSoundKind.Destroyed,PlayableSoundKind.MovementStarted,PlayableSoundKind.MovementStopped,PlayableSoundKind.UpgradeComplete};
                events.Add(new PlayableSoundEvent(ordinal,tick,catalogue[(ordinal-1)%catalogue.Length],new NavPoint((ordinal%3-1)*12,0),(PlayableEntityKind)(ordinal%3)));
            }
            var moving=new[]{new PlayableEntitySnapshot(999,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(6,0),100,true,0,0,0)};
            p.Tick(new[]{Screen(Frame(tick,events.ToArray(),moving))},true,.033);music.Tick(GameMusicMode.Match,1,7,elapsed>4?6:0,false,.033);
            tick++;yield return new WaitForSecondsRealtime(.033f);
        }
        p.ResetAudio();var data=guard.EndCapture(out var channels);
        Assert.Greater(guard.Blocks,0);Assert.Greater(guard.InputPeak,.001);Assert.LessOrEqual(guard.OutputPeak,GameplayMixGuard.Ceiling+.00001);
        Assert.Greater(data.Length,AudioSettings.outputSampleRate*channels);
        string directory=Environment.GetEnvironmentVariable("SPACEWARS_GAMEPLAY_AUDIO_EVIDENCE");
        if(!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);using(var stream=new BinaryWriter(File.Create(Path.Combine(directory,"unity-dsp-authored-mix.wav"))))
            {
                int bytes=data.Length*2;stream.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));stream.Write(36+bytes);stream.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));stream.Write(16);stream.Write((short)1);stream.Write((short)channels);stream.Write(AudioSettings.outputSampleRate);stream.Write(AudioSettings.outputSampleRate*channels*2);stream.Write((short)(channels*2));stream.Write((short)16);stream.Write(System.Text.Encoding.ASCII.GetBytes("data"));stream.Write(bytes);foreach(var x in data)stream.Write((short)Math.Round(x*32767));
            }
            File.WriteAllText(Path.Combine(directory,"unity-dsp-metrics.json"),"{\"inputPeak\":"+guard.InputPeak.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"outputPeak\":"+guard.OutputPeak.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"blocks\":"+guard.Blocks+",\"authoredSnapshots\":true,\"physicalOutputSuppressed\":true}");
        }
        guard.SuppressOutput=false;AudioListener.volume=0;
    }
    [UnityTest] public IEnumerator BaseAndEconomyAssetsPlayInSharedChannelsAndWarningsPreemptLowSignals()
    {
        var p=Player();var ready=new PlayableSoundEvent(1,1,PlayableSoundKind.ProductionComplete,new NavPoint(200,0),PlayableEntityKind.Tank,true);
        p.Tick(new[]{Screen(Frame(1,new[]{ready})),Screen(Frame(1,new[]{ready}))},true,.1);
        Assert.AreEqual(1,p.PlayedNotifications);Assert.Zero(p.PlayedEvents);
        var signal=host.GetComponentsInChildren<AudioSource>().Single(s=>s.name=="Human command");Assert.AreEqual("ready-1",signal.clip.name);
        var warning=new PlayableSoundEvent(2,2,PlayableSoundKind.BaseThreat,new NavPoint(200,0),PlayableEntityKind.Tank,true);
        p.Tick(new[]{Screen(Frame(2,new[]{warning}))},true,.1);Assert.AreEqual("warning-1",signal.clip.name);Assert.AreEqual(2,p.PlayedNotifications);
        yield return new WaitForSecondsRealtime(.55f);
        p.Tick(new[]{Screen(Frame(3,new[]{new PlayableSoundEvent(3,3,PlayableSoundKind.BaseThreat,new NavPoint(200,0),PlayableEntityKind.Tank,true)}))},true,.1);
        Assert.AreEqual(2,p.PlayedNotifications,"A repeated warning cannot queue or restart.");
        var demolition=new PlayableSoundEvent(4,4,PlayableSoundKind.Demolished,new NavPoint(0,0),PlayableEntityKind.Tank,true);
        p.Tick(new[]{Screen(Frame(4,new[]{demolition})),Screen(Frame(4,new[]{demolition}))},true,.1);Assert.AreEqual(1,p.PlayedEvents);
        Assert.IsTrue(host.GetComponentsInChildren<AudioSource>().Any(s=>s.isPlaying&&s.clip.name.StartsWith("demolition-")));
        p.Tick(new[]{Screen(Frame(4))},false,.1);Assert.Zero(p.ActiveVoices);p.Tick(new[]{Screen(Frame(4,new[]{demolition}))},true,.1);Assert.AreEqual(1,p.PlayedEvents);
    }
    [UnityTest] public IEnumerator MatchPauseResultAndSelectionSignalsAreEdgesAndSurviveInactiveFrames()
    {
        var p=Player();p.PresentationState(1,true,false,false);Assert.AreEqual(1,p.PlayedNotifications);
        yield return new WaitForSecondsRealtime(.5f);
        p.PresentationState(1,true,false,false);Assert.AreEqual(1,p.PlayedNotifications);
        p.Selection("player-1","1:0:0");p.Selection("player-1","1:0:0");
        Assert.AreEqual(2,p.PlayedNotifications);Assert.AreEqual("select-1",host.GetComponentsInChildren<AudioSource>().Single(s=>s.name=="Human command").clip.name);
        yield return new WaitForSecondsRealtime(.3f);
        p.Tick(Array.Empty<GameplayAudioScreen>(),false,.1);p.PresentationState(1,false,true,false);Assert.AreEqual(3,p.PlayedNotifications);
        p.Tick(Array.Empty<GameplayAudioScreen>(),false,.1);p.PresentationState(1,false,true,false);
        Assert.IsTrue(host.GetComponentsInChildren<AudioSource>().Single(s=>s.name=="Human command").isPlaying);
        yield return new WaitForSecondsRealtime(.3f);
        p.PresentationState(1,false,false,true);Assert.AreEqual(4,p.PlayedNotifications);
        Assert.AreEqual("result-1",host.GetComponentsInChildren<AudioSource>().Single(s=>s.name=="Human command").clip.name);
        p.PresentationState(1,false,false,true);Assert.AreEqual(4,p.PlayedNotifications);
    }
    [Test] public void FinalGuardCapsCombinedPeaksAndPreservesQuietSignal()
    {
        var guard=host.AddComponent<GameplayMixGuard>();var quiet=new[]{.1f,-.2f,.1f};guard.Process(quiet,1);Assert.AreEqual(.1f,quiet[0]);
        var loud=new[]{1.4f,-1.2f,.8f};guard.Process(loud,1);Assert.LessOrEqual(loud.Max(x=>Math.Abs(x)),GameplayMixGuard.Ceiling+.00001);
    }
    [UnityTest] public IEnumerator SettingsExposeIndependentGainsAndRenderAudioControls()
    {
        var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        var target=new RenderTexture(1280,900,0);target.Create();panel.targetTexture=target;
        var document=host.AddComponent<UIDocument>();document.panelSettings=panel;var root=document.rootVisualElement;
        OrbitalTheme.ConfigurePanel(panel,root);var navigation=new NativeMenuNavigation(root);
        try
        {
            var page=NativeSettingsView.Open(root,navigation,()=>{});yield return null;yield return null;
            foreach(var key in new[]{"music","effects","notifications"})Assert.NotNull(page.Q<Slider>("settings-audio-"+key));
            page.Q<Slider>("settings-audio-effects").value=.35f;Assert.AreEqual(.35f,NativeAudioSettings.Effects,.001);
            page.Q<Slider>("settings-audio-notifications").value=.6f;Assert.AreEqual(.6f,NativeAudioSettings.Notifications,.001);
            Assert.AreEqual(1,NativeAudioSettings.Music);Assert.Zero(AudioListener.volume);
            string directory=Environment.GetEnvironmentVariable("SPACEWARS_GAMEPLAY_AUDIO_EVIDENCE");
            if(!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);page.Q<ScrollView>("settings-scroll").scrollOffset=new Vector2(0,150);yield return new WaitForSecondsRealtime(.2f);
                var old=RenderTexture.active;RenderTexture.active=target;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(directory,"audio-settings-1280x900.png"),pixels.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(pixels);RenderTexture.active=old;
                page.RemoveFromHierarchy();var audioLab=NativeGameplayAudioProfile.Open(root,navigation,()=>{});yield return null;yield return null;
                Assert.AreEqual(GameplayAudioMetadata.Fields.Length,audioLab.Query<Slider>().ToList().Count(s=>s.name.StartsWith("audio.")));
                audioLab.Q<Slider>("audio.near").value=12;
                audioLab.Q<Button>("audio-lab-save").Focus();navigation.Activate();
                Assert.AreEqual(12,NativeGameplayAudioProfile.Current.near);
                yield return new WaitForSecondsRealtime(.2f);
                old=RenderTexture.active;RenderTexture.active=target;pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(directory,"audio-laboratory-1280x900.png"),pixels.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(pixels);RenderTexture.active=old;
            }
        }
        finally{navigation.Dispose();panel.targetTexture=null;UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(panel);}
    }
}
