using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class AdaptiveMusicPlaybackTests
{
    private GameObject host;
    private float listenerVolume;
    [SetUp] public void Setup()
    {
        listenerVolume=AudioListener.volume;AdaptiveMusicPlayer.DiagnosticSuppressOutput=true;
        AudioListener.volume=0;host=new GameObject("music playback contract");
    }
    [TearDown] public void Cleanup()
    {
        if(host)UnityEngine.Object.DestroyImmediate(host);
        AudioListener.volume=listenerVolume;AdaptiveMusicPlayer.DiagnosticSuppressOutput=false;
    }

    [Test] public void FiveAlignedLongThemesAndMenuAreImported()
    {
        Assert.AreEqual(5,AdaptiveMusicPlayer.ThemeIds.Distinct().Count());
        foreach(var id in AdaptiveMusicPlayer.ThemeIds)
        {
            var bed=Resources.Load<AudioClip>("Music/"+id+"-bed");var pulse=Resources.Load<AudioClip>("Music/"+id+"-pulse");
            Assert.NotNull(bed,id);Assert.NotNull(pulse,id);Assert.GreaterOrEqual(bed.length,120);
            Assert.AreEqual(bed.samples,pulse.samples,id);Assert.AreEqual(bed.frequency,pulse.frequency,id);Assert.AreEqual(2,bed.channels);
        }
        Assert.GreaterOrEqual(Resources.Load<AudioClip>("Music/menu").length,120);
    }

    [UnityTest] public IEnumerator ScheduledLayersPlayOnOneTimelineAndPauseResumes()
    {
        var music=host.AddComponent<AdaptiveMusicPlayer>();music.Initialize();
        music.Tick(GameMusicMode.Match,1,7,0,false,.1);
        yield return new WaitForSecondsRealtime(.3f);
        var sources=host.GetComponentsInChildren<AudioSource>();Assert.AreEqual(4,sources.Length);
        var playing=sources.Where(s=>s.clip&&s.clip.name.EndsWith("-bed")||s.clip&&s.clip.name.EndsWith("-pulse")).ToArray();
        Assert.AreEqual(2,playing.Length);Assert.IsTrue(playing.All(s=>s.isPlaying));
        Assert.LessOrEqual(Math.Abs(playing[0].timeSamples-playing[1].timeSamples),1024,"Shared DSP timestamp aligns music layers.");
        Assert.Greater(playing[0].timeSamples,0,"The native audio transport advances, even while output is muted.");
        double age=music.State.ThemeElapsed;music.Tick(GameMusicMode.Match,1,7,6,true,10);
        int stopped=playing[0].timeSamples;yield return new WaitForSecondsRealtime(.15f);
        Assert.AreEqual(age,music.State.ThemeElapsed);Assert.LessOrEqual(Math.Abs(playing[0].timeSamples-stopped),1024);
        music.Tick(GameMusicMode.Match,1,7,6,false,.1);yield return new WaitForSecondsRealtime(.15f);
        Assert.IsTrue(playing.All(s=>s.isPlaying));Assert.Greater(playing[0].timeSamples,stopped);
        Assert.AreEqual(0,AudioListener.volume,"Saved/output mute remains intact.");
    }

    [Test] public void MenuMatchResultAndRestartReuseSourcesAndResetCombat()
    {
        var music=host.AddComponent<AdaptiveMusicPlayer>();music.Initialize();music.Initialize();
        music.Tick(GameMusicMode.Menu,0,0,0,false,3);Assert.AreEqual(GameMusicMode.Menu,music.Mode);
        music.Tick(GameMusicMode.Match,1,7,0,false,3);Assert.AreEqual(0,music.State.Intensity);
        music.Tick(GameMusicMode.Match,1,7,12,false,3);Assert.AreEqual(1,music.State.Intensity);
        music.Tick(GameMusicMode.Menu,1,7,0,false,3);Assert.IsNull(music.State);
        music.Tick(GameMusicMode.Match,2,7,0,false,3);Assert.AreEqual(0,music.State.Intensity);Assert.AreEqual(0,music.State.Serial);
        Assert.AreEqual(4,host.GetComponentsInChildren<AudioSource>().Length);Assert.AreEqual(0,AudioListener.volume);
    }

    [UnityTest] public IEnumerator ScheduledNextThemeBoundsDigitalSilenceWithoutRenderTicks()
    {
        var music=host.AddComponent<AdaptiveMusicPlayer>();music.Initialize();
        foreach(var envelope in host.GetComponentsInChildren<MusicGainEnvelope>())envelope.SuppressOutput=true;
        AudioListener.volume=1; // Global zero can bypass Unity DSP callbacks; each rendered block is zeroed after measurement.
        music.Tick(GameMusicMode.Match,1,7,0,false,.1);
        yield return new WaitForSecondsRealtime(.5f);
        music.Tick(GameMusicMode.Match,1,7,0,false,176);music.Tick(GameMusicMode.Match,1,7,0,false,.1);
        Assert.AreEqual(MusicPhase.FadingOut,music.State.Phase);
        var fields=BindingFlags.Instance|BindingFlags.NonPublic;
        int active=(int)typeof(AdaptiveMusicPlayer).GetField("activeBank",fields).GetValue(music);
        var envelopes=(MusicGainEnvelope[,])typeof(AdaptiveMusicPlayer).GetField("envelopes",fields).GetValue(music);
        var old=envelopes[active,0];var next=envelopes[1-active,0];
        // Audio transport and fade must progress independently of the render/controller clock.
        yield return new WaitForSecondsRealtime(7.5f);
        Assert.Greater(old.LastAudibleDsp,0);Assert.Greater(next.FirstAudibleDsp,old.LastAudibleDsp);
        double gap=next.FirstAudibleDsp-old.LastAudibleDsp;
        Assert.LessOrEqual(gap,10,"Measured pre-master digital output satisfies the audible silence contract.");
        Assert.LessOrEqual(gap,4,"The configured three-second gap leaves scheduling/fade headroom.");
        Assert.AreEqual(MusicPhase.FadingOut,music.State.Phase,"No render ticks were used to start the new audio.");
        music.Tick(GameMusicMode.Match,1,7,0,false,7.5);
        Assert.AreEqual(1,music.State.Serial);
    }

    [UnityTest] public IEnumerator OrdinaryGameOwnsOnePlayerThroughMenuAndLobby()
    {
        var game=host.AddComponent<PlayableBootstrap>();
        float startupDeadline=Time.realtimeSinceStartup+10;
        while(host.GetComponent<AdaptiveMusicPlayer>()==null&&Time.realtimeSinceStartup<startupDeadline)yield return null;
        Assert.IsTrue(game.enabled,"Startup including audio must succeed.");
        Assert.AreEqual(1,host.GetComponents<AdaptiveMusicPlayer>().Length);
        var music=host.GetComponent<AdaptiveMusicPlayer>();Assert.AreEqual(GameMusicMode.Menu,music.Mode);
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        typeof(PlayableBootstrap).GetMethod("ShowLobby",flags).Invoke(game,null);
        yield return null;
        Assert.AreEqual(GameMusicMode.Menu,music.Mode);Assert.AreEqual(4,music.SourceCount);
        Assert.AreEqual(12,host.GetComponent<GameplayAudioPlayer>().PoolSize);Assert.AreEqual(17,host.GetComponentsInChildren<AudioSource>().Length);
        typeof(PlayableBootstrap).GetMethod("LaunchLobbyMatch",flags).Invoke(game,null);
        float deadline=Time.realtimeSinceStartup+30;
        while(music.Mode!=GameMusicMode.Match&&Time.realtimeSinceStartup<deadline)yield return null;
        Assert.AreEqual(GameMusicMode.Match,music.Mode,"The ordinary lobby must launch a music-enabled match.");
        var runtime=(PlayableRuntime)typeof(PlayableBootstrap).GetField("runtime",flags).GetValue(game);
        Assert.NotNull(runtime);var state=music.State;
        typeof(PlayableBootstrap).GetMethod("Pause",flags).Invoke(game,new object[]{true});yield return null;
        double age=state.ThemeElapsed;yield return new WaitForSecondsRealtime(.15f);Assert.AreEqual(age,state.ThemeElapsed);
        typeof(PlayableBootstrap).GetMethod("Pause",flags).Invoke(game,new object[]{false});yield return null;
        runtime.RequestFinish();deadline=Time.realtimeSinceStartup+10;
        while(music.Mode!=GameMusicMode.Menu&&Time.realtimeSinceStartup<deadline)yield return null;
        Assert.AreEqual(GameMusicMode.Menu,music.Mode,"Final result returns music to menu mood.");
        typeof(PlayableBootstrap).GetMethod("Restart",flags).Invoke(game,null);deadline=Time.realtimeSinceStartup+30;
        while((music.Mode!=GameMusicMode.Match||ReferenceEquals(state,music.State))&&Time.realtimeSinceStartup<deadline)yield return null;
        Assert.AreEqual(GameMusicMode.Match,music.Mode);Assert.AreNotSame(state,music.State);Assert.AreEqual(0,music.State.Intensity);
        typeof(PlayableBootstrap).GetMethod("ReturnToLobby",flags).Invoke(game,null);deadline=Time.realtimeSinceStartup+10;
        while(music.Mode!=GameMusicMode.Menu&&Time.realtimeSinceStartup<deadline)yield return null;
        Assert.AreEqual(GameMusicMode.Menu,music.Mode);Assert.AreEqual(1,host.GetComponents<AdaptiveMusicPlayer>().Length);
        UnityEngine.Object.Destroy(host);yield return null;yield return null;
        Assert.IsFalse(music,"Destroying the game releases its music component.");
    }
}
