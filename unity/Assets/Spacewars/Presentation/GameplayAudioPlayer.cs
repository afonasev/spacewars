using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // One pool for the whole local match, independent of viewport count.
    public sealed class GameplayAudioPlayer : MonoBehaviour
    {
        private sealed class Voice
        {internal AudioSource Source;internal string Key;internal bool Loop;internal float Gain,BaseGain,Pan,TargetGain,TargetPan,Priority;internal PlayableSoundKind Kind;internal NavPoint Position;internal HashSet<string> Observers;}
        private readonly Dictionary<string,AudioClip[]> clips=new Dictionary<string,AudioClip[]>();
        private readonly HashSet<string> pendingCommands=new HashSet<string>();
        private readonly Dictionary<string,double> commandKeys=new Dictionary<string,double>();
        private Voice[] voices;
        private AudioSource notification;
        private AudioListener ownedListener;
        private GameplayMixGuard mixGuard;
        private bool ownsGuard;
        private GameplayAudioProfile profile;
        private GameplayAudioPlan plan;
        private double lastSignal=-10;
        private float signalPriority;
        private readonly Dictionary<string,double> signalKeys=new Dictionary<string,double>();
        private readonly Dictionary<string,string> selections=new Dictionary<string,string>();
        private long stateGeneration=long.MinValue;
        private bool statePaused,stateFinished,stateActive;
        private long generation=long.MinValue,lastTick=-1;
        public static bool DiagnosticSuppressOutput;
        public int ActiveVoices=>voices==null?0:voices.Count(v=>v.Key!=null);
        public float EffectsGainSum{get;private set;}
        public int PlayedEvents{get;private set;}
        public int PlayedNotifications{get;private set;}
        public int PoolSize=>voices?.Length??0;

        public void Initialize()
        {
            if(voices!=null)return;
            profile=NativeGameplayAudioProfile.Current;profile.Validate();
            plan=new GameplayAudioPlan(profile);
            foreach(var key in new[]{"tank-shot","burst-shot","impact","explosion","order","reject","movement-loop","construction","demolition","repair","motion-start","motion-stop","damage","queued","work-start","ready","cancel","warning","select","ui","match-start","pause","result"})
            {
                int n=key=="tank-shot"||key=="burst-shot"||key=="impact"||key=="explosion"||key=="construction"||key=="demolition"||key=="repair"||key=="damage"?3:1;
                clips[key]=Enumerable.Range(1,n).Select(i=>Resources.Load<AudioClip>("Sfx/Hybrid/"+key+"-"+i)).ToArray();
                if(clips[key].Any(c=>!c))throw new InvalidOperationException("Missing hybrid sound: "+key);
                foreach(var c in clips[key])c.LoadAudioData();
            }
            voices=Enumerable.Range(0,profile.voices).Select(i=>new Voice{Source=Source("Effect "+i)}).ToArray();
            notification=Source("Human command");notification.priority=32;
            var listener=FindAnyObjectByType<AudioListener>();
            if(!listener)listener=ownedListener=gameObject.AddComponent<AudioListener>();
            mixGuard=listener.GetComponent<GameplayMixGuard>();
            if(!mixGuard){mixGuard=listener.gameObject.AddComponent<GameplayMixGuard>();ownsGuard=true;}
        }
        private AudioSource Source(string name)
        {
            var host=new GameObject(name);host.transform.SetParent(transform,false);
            var source=host.AddComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=0;source.dopplerLevel=0;source.volume=0;
            return source;
        }
        public void ResetAudio()
        {
            if(voices==null)return;
            foreach(var v in voices){v.Source.Stop();v.Source.volume=0;v.Key=null;v.Gain=0;v.TargetGain=0;}
            notification.Stop();commandKeys.Clear();pendingCommands.Clear();signalKeys.Clear();selections.Clear();plan.Reset();EffectsGainSum=0;generation=long.MinValue;lastTick=-1;lastSignal=-10;signalPriority=0;
        }
        public void Command(PlayableCommand command,bool accepted)
        {
            Initialize();if(command==null||command.Origin==PlayableOrderOrigin.Ai||command.Kind==PlayableCommandKind.Restart)return;
            string key=command.Generation+":"+command.PlayerId+":"+command.Sequence;
            double now=Time.unscaledTimeAsDouble;
            if(commandKeys.ContainsKey(key))return;commandKeys[key]=now;if(accepted)pendingCommands.Add(key);
            bool economic=command.Kind==PlayableCommandKind.QueueTank||command.Kind==PlayableCommandKind.QueueExplorer||command.Kind==PlayableCommandKind.QueueShkval||command.Kind==PlayableCommandKind.QueueResearch||command.Kind==PlayableCommandKind.CancelResearch||command.Kind==PlayableCommandKind.CancelProductionOrder||command.Kind==PlayableCommandKind.UpgradeRefinery||command.Kind==PlayableCommandKind.CancelRefineryUpgrade;
            if(!accepted||!economic)Signal(accepted,now);
        }
        public void Receipt(PlayableCommandReceipt receipt,long commandGeneration)
        {
            if(receipt==null||receipt.Status==PlayableCommandStatus.Accepted)return;
            string key=commandGeneration+":"+receipt.OwnerId+":"+receipt.Sequence;
            if(!pendingCommands.Remove(key))return;
            if(receipt.Status!=PlayableCommandStatus.Applied&&receipt.Status!=PlayableCommandStatus.Cancelled&&commandKeys.TryGetValue(key,out var at)&&Time.unscaledTimeAsDouble-at<2)
                Signal(false,Time.unscaledTimeAsDouble);
        }
        private void Signal(bool accepted,double now)
        {
            SignalClip(accepted?"order":"reject",now,1);
        }
        private void SignalClip(string clip,double now,float priority=0)
        {
            double interval=clip=="warning"?profile.warningSeconds:profile.notificationSeconds;
            if(signalKeys.TryGetValue(clip,out var previous)&&now-previous<interval)return;
            if((now-lastSignal<profile.notificationSeconds||notification.isPlaying)&&priority<=signalPriority)return;
            signalKeys[clip]=now;signalPriority=priority;lastSignal=now;notification.Stop();notification.clip=clips[clip][0];notification.volume=DiagnosticSuppressOutput?0:profile.notificationGain*NativeAudioSettings.Notifications;
            notification.Play();PlayedNotifications++;
        }
        public static void Ui()
        {
            var player=FindAnyObjectByType<GameplayAudioPlayer>();
            if(player&&player.isActiveAndEnabled){player.Initialize();player.SignalClip("ui",Time.unscaledTimeAsDouble);}
        }
        public void Selection(string owner,string signature)
        {
            Initialize();if(selections.TryGetValue(owner,out var previous)&&previous==signature)return;
            selections[owner]=signature;SignalClip("select",Time.unscaledTimeAsDouble);
        }
        public void PresentationState(long matchGeneration,bool active,bool paused,bool finished)
        {
            Initialize();string clip=null;
            if(finished&&!stateFinished)clip="result";
            else if(paused!=statePaused&&stateGeneration==matchGeneration)clip="pause";
            else if(active&&(!stateActive||stateGeneration!=matchGeneration)&&!statePaused)clip="match-start";
            stateGeneration=matchGeneration;stateActive=active;statePaused=paused;stateFinished=finished;
            if(clip!=null)SignalClip(clip,Time.unscaledTimeAsDouble,2);
        }
        public void Tick(IReadOnlyList<GameplayAudioScreen> screens,bool active,double seconds)
        {
            Initialize();
            if(!ReferenceEquals(profile,NativeGameplayAudioProfile.Current))
            {ResetAudio();foreach(var v in voices)Destroy(v.Source.gameObject);Destroy(notification.gameObject);voices=null;Initialize();}
            if(!active){if(generation!=long.MinValue)ResetAudio();notification.volume=DiagnosticSuppressOutput?0:profile.notificationGain*NativeAudioSettings.Notifications;return;}
            var first=screens.FirstOrDefault(s=>s.View!=null);
            if(first==null){ResetAudio();return;}
            if(generation!=first.View.Generation||first.View.Tick<lastTick){ResetAudio();generation=first.View.Generation;}
            lastTick=first.View.Tick;
            foreach(var v in voices)
            {if(v.Key!=null&&!v.Source.isPlaying&&!v.Loop){v.Key=null;v.Gain=0;}if(v.Loop)v.TargetGain=0;}
            double now=Time.unscaledTimeAsDouble;
            foreach(var cue in plan.Observe(screens,now,true))Play(cue);
            float blend=(float)(1-Math.Exp(-Math.Max(0,seconds)/profile.fadeSeconds));
            foreach(var v in voices)
            {
                if(v.Key!=null&&!v.Loop)
                {
                    v.TargetGain=0;
                    foreach(var screen in screens)if(screen.View!=null&&v.Observers.Contains(screen.View.OwnerId)&&plan.Spatial(screen,v.Position,out var gain,out var pan)&&gain*v.BaseGain>v.TargetGain)
                    {v.TargetGain=gain*v.BaseGain;v.TargetPan=pan;}
                }
                v.Gain=Mathf.Lerp(v.Gain,v.TargetGain,blend);v.Pan=Mathf.Lerp(v.Pan,v.TargetPan,blend);
                if(v.Loop&&v.TargetGain==0&&v.Gain<.001f){v.Source.Stop();v.Key=null;v.Gain=0;}
            }
            float sum=voices.Where(v=>v.Key!=null).Sum(v=>v.Gain);
            float scale=sum>profile.effectsBudget?profile.effectsBudget/sum:1;
            EffectsGainSum=sum*scale*NativeAudioSettings.Effects;
            foreach(var v in voices)
            {v.Source.volume=DiagnosticSuppressOutput?0:v.Gain*scale*NativeAudioSettings.Effects;v.Source.panStereo=v.Pan;}
            notification.volume=DiagnosticSuppressOutput?0:profile.notificationGain*NativeAudioSettings.Notifications;
            foreach(var key in commandKeys.Where(p=>now-p.Value>2).Select(p=>p.Key).ToArray()){commandKeys.Remove(key);pendingCommands.Remove(key);}
        }
        private void Play(GameplayAudioCue cue)
        {
            if(cue.Notification){SignalClip(cue.Clip,Time.unscaledTimeAsDouble,cue.Priority);return;}
            var existing=voices.FirstOrDefault(v=>v.Key==cue.Key);
            if(existing!=null){existing.TargetGain=cue.Gain;existing.TargetPan=cue.Pan;existing.Priority=cue.Priority;return;}
            int cap=plan.CategoryCap(cue.Kind);
            var category=voices.Where(v=>v.Key!=null&&(cue.Loop?v.Loop:!v.Loop&&v.Kind==cue.Kind)).ToArray();
            if(cue.Loop)cap=profile.movementGroups;
            Voice voice;
            if(category.Length>=cap)voice=category.OrderBy(v=>v.Priority).First();
            else voice=voices.FirstOrDefault(v=>v.Key==null)??voices.OrderBy(v=>v.Priority).First();
            if(voice.Key!=null&&voice.Priority>cue.Priority)return;
            voice.Source.Stop();voice.Source.volume=0;voice.Key=cue.Key;voice.Loop=cue.Loop;voice.Kind=cue.Kind;
            voice.BaseGain=cue.BaseGain;voice.TargetGain=cue.Gain;voice.TargetPan=cue.Pan;voice.Priority=cue.Priority;voice.Position=cue.Position;voice.Observers=cue.Observers;
            // Transients need their attack immediately; loops smoothly enter/leave.
            voice.Gain=cue.Loop?0:cue.Gain;voice.Pan=cue.Pan;
            var choices=clips[cue.Clip];voice.Source.clip=choices[(int)(Math.Abs(cue.EventId)%choices.Length)];
            voice.Source.loop=cue.Loop;voice.Source.pitch=cue.Pitch;voice.Source.Play();
            if(!cue.Loop)PlayedEvents++;
        }
        private void OnDisable()=>ResetAudio();
        private void OnDestroy()
        {
            if(voices!=null)foreach(var v in voices)if(v.Source)Destroy(v.Source.gameObject);
            if(notification)Destroy(notification.gameObject);if(ownsGuard&&mixGuard)Destroy(mixGuard);if(ownedListener)Destroy(ownedListener);
        }
    }
}
