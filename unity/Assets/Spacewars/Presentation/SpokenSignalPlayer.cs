using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Spacewars.Presentation
{
    // A single nonspatial voice channel for all local viewports, separate from command beeps/SFX.
    public sealed class SpokenSignalPlayer : MonoBehaviour
    {
        readonly Dictionary<string,AudioClip> clips=new Dictionary<string,AudioClip>();
        NativeSpeechPlan plan;GameplayAudioProfile profile;AudioSource source;GameObject sourceHost;
        private enum PublicLine { None, Greeting, Farewell }
        bool diagnosticSilent;
        PublicLine publicLine;
        private float activeSignalGain=1;
        double clock,speakingUntil=double.NegativeInfinity;
#if UNITY_INCLUDE_TESTS
        // Exercises the real playback path only in a process already muted before startup.
        // NativeQuietStartup keeps the source muted; no preference or system-volume changes.
        public void InitializeMutedForTests(GameplayAudioProfile settings)
        {
            if(!NativeQuietStartup.Enabled)throw new InvalidOperationException("Muted diagnostics require -muted or -noaudio from process startup.");
            diagnosticSilent=true;Initialize(settings);
        }
#endif
        public int Played{get;private set;} public string LastKey{get;private set;}
        public float OutputGain=>source?source.volume:0;
        public bool Busy=>clock<speakingUntil;
        public NativeSpeechPlan Plan=>plan;
        public void Initialize(GameplayAudioProfile settings)
        {
            if(ReferenceEquals(profile,settings)&&plan!=null)return;
            ResetSpeech();profile=settings;plan=new NativeSpeechPlan(settings);
            if(NativeSilentStartup.IsSilent&&!diagnosticSilent)return;
            foreach(var key in SpokenSignalCatalog.Phrases.Keys.Concat(AiDefeatLines.All.Select(line=>line.Key))
                .Concat(AiGreetingLines.All.Select(line=>line.Key)))
            {
                var clip=Resources.Load<AudioClip>("Sfx/Voice/"+key);
                if(!clip)throw new InvalidOperationException("Missing spoken signal: "+key);
                clip.LoadAudioData();clips[key]=clip;
            }
            if(!sourceHost){sourceHost=new GameObject("Spoken signals");sourceHost.transform.SetParent(transform,false);}
            if(!source){source=NativeQuietStartup.AddSource(sourceHost);source.playOnAwake=false;source.spatialBlend=0;source.dopplerLevel=0;source.priority=16;}
        }
        private bool VoiceEnabled=>NativeAudioSettings.VoiceEnabled&&NativeAudioSettings.Voice>0&&
            (!NativeSilentStartup.IsSilent||diagnosticSilent);

        public bool FarewellPlaying(double now)=>publicLine==PublicLine.Farewell&&now<speakingUntil&&VoiceEnabled;
        internal bool CanPlayGreeting(double now)=>!VoiceEnabled||now>=speakingUntil;

        public void PlayGreeting(string key,double now)
        {
            if(CanPlayGreeting(now))PlayPublicLine(key,now,PublicLine.Greeting);
        }

        public void PlayFarewell(string key,double now)=>PlayPublicLine(key,now,PublicLine.Farewell);

        private void PlayPublicLine(string key,double now,PublicLine line)
        {
            if(!VoiceEnabled||!source||!clips.TryGetValue(key,out var clip))return;
            if(line==PublicLine.Farewell)ResetSpeech();
            source.volume=GameplayAudioPlayer.DiagnosticSuppressOutput?0:profile.speechGain*NativeAudioSettings.Voice;
            clock=now;
            publicLine=line;
            Play(key,clip,now);
        }

        public void Tick(IReadOnlyList<GameplayAudioScreen> screens,bool active,double now)
        {
            if(plan==null)Initialize(NativeGameplayAudioProfile.Current);
            clock=now;
            bool enabled=VoiceEnabled;
            if(FarewellPlaying(now)||publicLine==PublicLine.Greeting&&active&&enabled&&Busy)
            {
                source.volume=GameplayAudioPlayer.DiagnosticSuppressOutput?0:profile.speechGain*NativeAudioSettings.Voice;
                return;
            }
            if(publicLine==PublicLine.Farewell)ResetSpeech();
            else if(publicLine==PublicLine.Greeting)publicLine=PublicLine.None;
            int epoch=plan.Epoch;plan.Observe(screens,now,active,enabled);
            if(plan.Epoch!=epoch){if(source){source.Stop();source.clip=null;}speakingUntil=double.NegativeInfinity;}
            if(!active||!enabled){if(source){source.Stop();source.volume=0;}speakingUntil=double.NegativeInfinity;return;}
            source.volume=GameplayAudioPlayer.DiagnosticSuppressOutput?0:profile.speechGain*NativeAudioSettings.Voice*activeSignalGain;
            // Derive channel occupancy from the scheduled clip duration. Some muted
            // Editor backends report isPlaying before a clip has even been assigned.
            var cue=plan.Next(now,Busy);if(cue==null)return;
            activeSignalGain=cue.Key==SpokenSignalCatalog.NuclearLaunchKey
                ? (float)(screens.FirstOrDefault(screen=>screen.View?.ActiveProfile!=null)?.View.ActiveProfile.CopyData().orbitalNuclearVoiceGain??1)
                :1;
            source.volume=GameplayAudioPlayer.DiagnosticSuppressOutput?0:profile.speechGain*NativeAudioSettings.Voice*activeSignalGain;
            Play(cue.Key,clips[cue.Key],now);
        }
        private void Play(string key,AudioClip clip,double now)
        {
            source.clip=clip;
            source.Play();
            speakingUntil=now+clip.length;
            LastKey=key;
            Played++;
        }

        public void ResetSpeech(){publicLine=PublicLine.None;speakingUntil=double.NegativeInfinity;plan?.Reset();if(source){source.Stop();source.clip=null;source.volume=0;}}
        void OnDisable()=>ResetSpeech();
        void OnDestroy(){if(sourceHost)Destroy(sourceHost);else if(source)Destroy(source);}
    }
}
