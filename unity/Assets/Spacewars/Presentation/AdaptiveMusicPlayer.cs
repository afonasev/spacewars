using System;
using System.Collections.Generic;
using UnityEngine;

namespace Spacewars.Presentation
{
    public enum GameMusicMode { Menu, Match }

    // One coordinator-owned, two-dimensional output for the shared audio device.
    public sealed class AdaptiveMusicPlayer : MonoBehaviour
    {
        public static bool DiagnosticSuppressOutput;
        public static readonly string[] ThemeIds={"unknown-sector","steel-frontier","shadow-protocol","through-fire","last-bastion"};
        private AudioClip menu;
        private readonly AudioClip[] beds=new AudioClip[5],pulses=new AudioClip[5];
        private readonly AudioSource[,] sources=new AudioSource[2,2];
        private readonly MusicGainEnvelope[,] envelopes=new MusicGainEnvelope[2,2];
        private int pendingBank=-1,pendingTheme=-1;
        private double pendingStart,pauseAt;
        private bool pendingWasFuture;
        private AdaptiveMusicProfile profile;
        private AdaptiveMusicState state;
        private AudioListener ownedListener;
        private int activeBank,serial;
        private long generation=long.MinValue;
        private bool initialized,wasPaused;
        private GameMusicMode mode;
        public AdaptiveMusicState State=>state;
        public GameMusicMode Mode=>mode;
        public int ActiveTheme=>state?.Theme??-1;
        public int SourceCount=>4;

        public void Initialize()
        {
            if(initialized)return;
            var config=Resources.Load<TextAsset>("AdaptiveMusicProfile");
            if(!config)throw new InvalidOperationException("Missing AdaptiveMusicProfile.");
            profile=JsonUtility.FromJson<AdaptiveMusicProfile>(config.text);profile.Validate();
            menu=Load("menu");
            for(int i=0;i<ThemeIds.Length;i++)
            {
                beds[i]=Load(ThemeIds[i]+"-bed");pulses[i]=Load(ThemeIds[i]+"-pulse");
                if(beds[i].samples!=pulses[i].samples||beds[i].frequency!=pulses[i].frequency)
                    throw new InvalidOperationException("Unaligned music layers: "+ThemeIds[i]);
            }
            for(int bank=0;bank<2;bank++)for(int layer=0;layer<2;layer++)
            {
                var child=new GameObject("Music "+bank+" / "+layer);child.transform.SetParent(transform,false);
                var source=child.AddComponent<AudioSource>();source.playOnAwake=false;source.loop=true;
                source.spatialBlend=0;source.volume=1;source.pitch=1;source.priority=128;
                sources[bank,layer]=source;envelopes[bank,layer]=child.AddComponent<MusicGainEnvelope>();
                envelopes[bank,layer].SuppressOutput=DiagnosticSuppressOutput;
            }
            if(FindAnyObjectByType<AudioListener>()==null)ownedListener=gameObject.AddComponent<AudioListener>();
            initialized=true;mode=GameMusicMode.Menu;
            Schedule(0,menu,null,AudioSettings.dspTime+.05);
            SetGain(0,profile.calmBedGain,0);
        }

        private static AudioClip Load(string id)
        {
            var clip=Resources.Load<AudioClip>("Music/"+id);
            if(!clip)throw new InvalidOperationException("Missing music clip: "+id);
            if(clip.loadState!=AudioDataLoadState.Loaded&&!clip.LoadAudioData())
                throw new InvalidOperationException("Music did not preload: "+id);
            return clip;
        }

        public void Tick(GameMusicMode nextMode,long nextGeneration,int seed,int events,bool paused,double seconds)
        {
            Initialize();double now=AudioSettings.dspTime;
            bool changed=mode!=nextMode||nextMode==GameMusicMode.Match&&generation!=nextGeneration;
            if(changed)
            {
                CancelPending();int outgoing=activeBank;activeBank=1-activeBank;
                Fade(outgoing,0,now);mode=nextMode;generation=nextGeneration;
                if(mode==GameMusicMode.Match)
                {
                    state=new AdaptiveMusicState(profile,ThemeIds.Length,unchecked(seed+(int)nextGeneration));serial=state.Serial;
                    Schedule(activeBank,beds[state.Theme],pulses[state.Theme],now+.05);
                }
                else{state=null;Schedule(activeBank,menu,null,now+.05);SetGain(activeBank,profile.calmBedGain,0);}
                wasPaused=false;
            }
            if(paused!=wasPaused)
            {
                if(paused)
                {
                    pauseAt=now;pendingWasFuture=pendingBank>=0&&pendingStart>now;
                    foreach(var source in sources)source.Pause();
                    if(pendingWasFuture)foreach(var source in Bank(pendingBank))source.Stop();
                }
                else
                {
                    foreach(var envelope in envelopes)envelope.Shift(now-pauseAt);
                    foreach(var source in sources)source.UnPause();
                    if(pendingWasFuture&&pendingBank>=0)
                    {pendingStart+=now-pauseAt;Schedule(pendingBank,beds[pendingTheme],pulses[pendingTheme],pendingStart);SetGain(pendingBank,profile.calmBedGain,0);}
                }
                wasPaused=paused;
            }
            if(paused)return;
            if(mode==GameMusicMode.Match)
            {
                var before=state.Phase;state.Advance(seconds,events,false,beds[state.Theme].length);
                if(before==MusicPhase.FadingOut&&state.Phase==MusicPhase.FadingIn&&state.Serial==serial)
                {CancelPending();Fade(activeBank,1,now);}
                if(before!=MusicPhase.FadingOut&&state.Phase==MusicPhase.FadingOut)
                {
                    Fade(activeBank,0,now);pendingBank=1-activeBank;pendingTheme=state.NextTheme;
                    pendingStart=now+profile.fadeSeconds+profile.silenceSeconds;
                    Schedule(pendingBank,beds[pendingTheme],pulses[pendingTheme],pendingStart);
                    SetGain(pendingBank,profile.calmBedGain,0);
                }
                if(state.Serial!=serial)
                {
                    serial=state.Serial;
                    if(pendingBank>=0&&pendingTheme==state.Theme)
                    {
                        if(events>0&&pendingStart>now+.05)
                        {pendingStart=now+.05;Schedule(pendingBank,beds[pendingTheme],pulses[pendingTheme],pendingStart);}
                        activeBank=pendingBank;state.AlignTransport(Math.Max(0,now-pendingStart));pendingBank=-1;
                    }
                    else{Schedule(activeBank,beds[state.Theme],pulses[state.Theme],now+.05);}
                }
                double bed=profile.calmBedGain+(profile.battleBedGain-profile.calmBedGain)*state.Intensity;
                SetGain(activeBank,bed,profile.battlePulseGain*state.Intensity);
            }
            else SetGain(activeBank,profile.calmBedGain,0);
            int inactive=1-activeBank;
            if(inactive!=pendingBank&&envelopes[inactive,0].FactorAt(now)<=0)
                foreach(var source in Bank(inactive))source.Stop();
        }

        private void CancelPending()
        {if(pendingBank>=0)foreach(var source in Bank(pendingBank))source.Stop();pendingBank=-1;}
        private void SetGain(int bank,double bed,double pulse)
        {envelopes[bank,0].SetGain((float)bed*NativeAudioSettings.Music);envelopes[bank,1].SetGain((float)pulse*NativeAudioSettings.Music);}
        private void Fade(int bank,float target,double now)
        {envelopes[bank,0].FadeTo(target,now,profile.fadeSeconds);envelopes[bank,1].FadeTo(target,now,profile.fadeSeconds);}
        private System.Collections.Generic.IEnumerable<AudioSource> Bank(int bank){yield return sources[bank,0];yield return sources[bank,1];}
        private void Schedule(int bank,AudioClip bed,AudioClip pulse,double start)
        {
            var a=sources[bank,0];var b=sources[bank,1];a.Stop();b.Stop();a.clip=bed;b.clip=pulse;a.timeSamples=0;
            envelopes[bank,0].Open(start,profile.fadeSeconds);envelopes[bank,1].Open(start,profile.fadeSeconds);
            a.PlayScheduled(start);if(pulse){b.timeSamples=0;b.PlayScheduled(start);}
        }
        private void OnDestroy()
        {
            foreach(var source in sources)if(source){source.Stop();Destroy(source.gameObject);}
            if(ownedListener)Destroy(ownedListener);
        }
    }
}
