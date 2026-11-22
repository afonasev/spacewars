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
        {internal long PhaseEndTick;internal AudioSource Source;internal string Key;internal bool Loop;internal float Gain,BaseGain,Pan,TargetGain,TargetPan,Priority;internal PlayableSoundKind Kind;internal NavPoint Position;internal HashSet<string> Observers;}
        private readonly Dictionary<string,AudioClip[]> clips=new Dictionary<string,AudioClip[]>();
        private readonly HashSet<string> pendingCommands=new HashSet<string>();
        private readonly Dictionary<string,double> commandKeys=new Dictionary<string,double>();
        private Voice[] voices;
        private UnitRadio radio;
        private AudioSource radioSource;
        private bool radioActive;
        // One human click can produce separate destinations. Only applied recipients
        // participate in the single acknowledgement for that click.
        internal sealed class RadioOrder
        {
            internal int Pending;
            internal bool Relay;
            internal readonly Dictionary<int,long> FlightSequences = new Dictionary<int,long>();
            internal readonly List<int> Recipients = new List<int>();
            internal PlayableCommand Command;
            internal void Applied(PlayableCommand command)
            {
                Command = command;
                Recipients.AddRange(command.EntityIds);
                if (command.Kind == PlayableCommandKind.Move || command.Kind == PlayableCommandKind.AttackMove)
                    foreach (var id in command.EntityIds) FlightSequences[id] = command.Sequence;
            }
        }
        private readonly Dictionary<string,(PlayableCommand command, RadioOrder order)> radioPending =
            new Dictionary<string,(PlayableCommand, RadioOrder)>();
        private readonly List<RadioOrder> radioApplied = new List<RadioOrder>();
        // One arrival report per applied click, even when multiple aircraft reach it.
        private readonly List<RadioOrder> flights = new List<RadioOrder>();
        internal static int ObserveFlight(RadioOrder flight, PlayableSnapshot view)
        {
            bool pending = false;
            foreach (var unit in view.Entities)
            {
                if (unit.Owner != view.Owner || unit.Kind != PlayableEntityKind.Hawk ||
                    !flight.FlightSequences.TryGetValue(unit.Id, out var sequence)) continue;
                var stamp = unit.OrderStamp;
                if (stamp == null || stamp.Sequence < sequence) { pending = true; continue; }
                if (stamp.Sequence != sequence || stamp.Origin != PlayableOrderOrigin.Human ||
                    stamp.Kind != flight.Command.Kind) continue;
                if (!unit.Moving && unit.NavigationOutcome == NavigationOutcome.Arrived && unit.CurrentOrder == null) return unit.Id;
                if (unit.Moving || unit.CurrentOrder != null) pending = true;
            }
            return pending ? 0 : -1;
        }
        private AudioSource notification;
        private AudioSource dominationTimer;
        private long dominationAudioEvent;
        private AudioClip dominationTick;
        private SpokenSignalPlayer speech;
        public SpokenSignalPlayer Speech=>speech;
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
            if(NativeSilentStartup.IsSilent)return;
            if(voices!=null)return;
            profile=NativeGameplayAudioProfile.Current;profile.Validate();
            plan=new GameplayAudioPlan(profile);
            var shkvalImpacts=Enumerable.Range(0,GameplayAudioPlan.MaxShkvalImpactUnits+1).Select(GameplayAudioPlan.ShkvalImpactClip);
            var weaponClips=new[]{GameplayAudioPlan.IglaShotClip,GameplayAudioPlan.HawkRocketLaunchClip,GameplayAudioPlan.HawkRocketImpactClip,
                GameplayAudioPlan.VeprBuildingImpactClip,GameplayAudioPlan.VeprShotClip,
                GameplayAudioPlan.VeprUnitImpactClip,GameplayAudioPlan.ExplorerBurstShotClip,
                GameplayAudioPlan.SentryTurretShotClip,GameplayAudioPlan.GaussTowerShotClip};
            foreach(var key in new[]{"ability-reinforcement","ability-reinforcement-ready","ability-stasis","ability-stasis-release","ability-nanobots","ability-production-boost","ability-nuclear-descent","ability-nuclear-impact","ability-system-failure","ability-scan","ability-mine","ability-hack","tank-shot","burst-shot","impact","explosion","building-collapse","order","reject","movement-loop","construction","demolition","repair","motion-start","motion-stop","damage","queued","work-start","ready","cancel","warning","select","ui","match-start","pause","result"}.Concat(shkvalImpacts).Concat(weaponClips))
            {
                int n=key switch
                {
                    GameplayAudioPlan.HawkRocketLaunchClip or GameplayAudioPlan.VeprShotClip or GameplayAudioPlan.ExplorerBurstShotClip or GameplayAudioPlan.SentryTurretShotClip => 4,
                    GameplayAudioPlan.VeprUnitImpactClip => 8,
                    "tank-shot" or "burst-shot" or "impact" or "explosion" or "construction" or "demolition" or "repair" or "damage" => 3,
                    _ => 1,
                };
                clips[key]=Enumerable.Range(1,n).Select(i=>Resources.Load<AudioClip>("Sfx/Hybrid/"+key+"-"+i)).ToArray();
                if(clips[key].Any(c=>!c))throw new InvalidOperationException("Missing hybrid sound: "+key);
                foreach(var c in clips[key])c.LoadAudioData();
            }
            voices=Enumerable.Range(0,profile.voices).Select(i=>new Voice{Source=Source("Effect "+i)}).ToArray();
            if (!radioSource) radioSource = Source("Unit acknowledgements");
            radioSource.priority = 24;
            radio = new UnitRadio(radioSource);
            notification=Source("Human command");notification.priority=32;
            dominationTimer=Source("Domination countdown");
            dominationTick=Resources.Load<AudioClip>("Sfx/Hybrid/domination-tick");
            speech=GetComponent<SpokenSignalPlayer>()??gameObject.AddComponent<SpokenSignalPlayer>();speech.Initialize(profile);
            var listener=FindAnyObjectByType<AudioListener>();
            if(!listener)listener=ownedListener=gameObject.AddComponent<AudioListener>();
            mixGuard=listener.GetComponent<GameplayMixGuard>();
            if(!mixGuard){mixGuard=listener.gameObject.AddComponent<GameplayMixGuard>();ownsGuard=true;}
        }
        private AudioSource Source(string name)
        {
            var host=new GameObject(name);host.transform.SetParent(transform,false);
            var source=NativeQuietStartup.AddSource(host);source.playOnAwake=false;source.spatialBlend=0;source.dopplerLevel=0;source.volume=0;
            return source;
        }
        public void ResetAudio()=>ResetAudio(false);

        private void ResetAudio(bool preserveFarewell)
        {
            radio?.Reset(); radioActive = false; radioPending.Clear(); radioApplied.Clear(); flights.Clear();
            if(!preserveFarewell||speech?.FarewellPlaying(Time.unscaledTimeAsDouble)!=true)speech?.ResetSpeech();
            if(voices==null)return;
            foreach(var v in voices){v.Source.Stop();v.Source.volume=0;v.Key=null;v.Gain=0;v.TargetGain=0;}
            dominationTimer.Stop();dominationAudioEvent=0;notification.Stop();commandKeys.Clear();pendingCommands.Clear();signalKeys.Clear();selections.Clear();plan.Reset();EffectsGainSum=0;generation=long.MinValue;lastTick=-1;lastSignal=-10;signalPriority=0;
        }
        public void Command(PlayableCommand command,bool accepted) => Command(command, accepted, null);
        internal void Command(PlayableCommand command,bool accepted,RadioOrder order)
        {
            if(NativeSilentStartup.IsSilent)return;
            Initialize();if(command==null||command.Origin==PlayableOrderOrigin.Ai||command.Kind==PlayableCommandKind.Restart||command.Kind==PlayableCommandKind.SignalAttack||command.Kind==PlayableCommandKind.SignalDefend||command.Kind==PlayableCommandKind.SignalRecon)return;
            string key=command.Generation+":"+command.PlayerId+":"+command.Sequence;
            double now=Time.unscaledTimeAsDouble;
            if(commandKeys.ContainsKey(key))return;commandKeys[key]=now;if(accepted)pendingCommands.Add(key);
            if (accepted && UnitRadio.IsOrder(command.Kind))
            {
                order ??= new RadioOrder();
                order.Pending++;
                radioPending[key] = (command, order);
            }
            bool economic=command.Kind==PlayableCommandKind.QueueTank||command.Kind==PlayableCommandKind.QueueExplorer||command.Kind==PlayableCommandKind.QueueShkval||command.Kind==PlayableCommandKind.QueueResearch||command.Kind==PlayableCommandKind.CancelResearch||command.Kind==PlayableCommandKind.CancelProductionOrder||command.Kind==PlayableCommandKind.UpgradeRefinery||command.Kind==PlayableCommandKind.CancelRefineryUpgrade;
            if(!accepted||!economic)Signal(accepted,now);
        }
        public void Receipt(PlayableCommandReceipt receipt,long commandGeneration)
        {
            if(NativeSilentStartup.IsSilent)return;
            if(receipt==null||receipt.Status==PlayableCommandStatus.Accepted)return;
            string key=commandGeneration+":"+receipt.OwnerId+":"+receipt.Sequence;
            if(!pendingCommands.Remove(key))return;
            if (radioPending.TryGetValue(key, out var pending))
            {
                radioPending.Remove(key);
                if (receipt.Status == PlayableCommandStatus.Applied) pending.order.Applied(pending.command);
                if (--pending.order.Pending == 0 && pending.order.Command != null) radioApplied.Add(pending.order);
            }
            if(receipt.Status!=PlayableCommandStatus.Applied&&receipt.Status!=PlayableCommandStatus.Cancelled&&commandKeys.TryGetValue(key,out var at)&&Time.unscaledTimeAsDouble-at<2)
                Signal(false,Time.unscaledTimeAsDouble);
        }
        public void TeamSignal(){if(NativeSilentStartup.IsSilent)return;Initialize();SignalClip("order",Time.unscaledTimeAsDouble,1);}
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
            if(NativeSilentStartup.IsSilent)return;
            var player=FindAnyObjectByType<GameplayAudioPlayer>();
            if(player&&player.isActiveAndEnabled){player.Initialize();player.SignalClip("ui",Time.unscaledTimeAsDouble);}
        }
        public void Selection(string owner,string signature,PlayableSnapshot view=null,ICollection<int> selected=null)
        {
            if(NativeSilentStartup.IsSilent)return;
            Initialize();radio.Select(view,selected,Time.unscaledTimeAsDouble);if(selections.TryGetValue(owner,out var previous)&&previous==signature)return;
            selections[owner]=signature;SignalClip("select",Time.unscaledTimeAsDouble);
        }
        public void PresentationState(long matchGeneration,bool active,bool paused,bool finished)
        {
            if(NativeSilentStartup.IsSilent)return;
            Initialize();string clip=null;
            if(finished&&!stateFinished)clip="result";
            else if(paused!=statePaused&&stateGeneration==matchGeneration)clip="pause";
            else if(active&&(!stateActive||stateGeneration!=matchGeneration)&&!statePaused)clip="match-start";
            stateGeneration=matchGeneration;stateActive=active;statePaused=paused;stateFinished=finished;
            if(clip!=null)SignalClip(clip,Time.unscaledTimeAsDouble,2);
        }
        public void Tick(IReadOnlyList<GameplayAudioScreen> screens,bool active,double seconds)
        {
            if(NativeSilentStartup.IsSilent)return;
            Initialize();
            if(!ReferenceEquals(profile,NativeGameplayAudioProfile.Current))
            {ResetAudio();foreach(var v in voices)Destroy(v.Source.gameObject);Destroy(notification.gameObject);Destroy(dominationTimer.gameObject);voices=null;Initialize();}
            radioActive = active;
            if(!active){speech?.Tick(screens,false,Time.unscaledTimeAsDouble);if(generation!=long.MinValue)ResetAudio(true);notification.volume=DiagnosticSuppressOutput?0:profile.notificationGain*NativeAudioSettings.Notifications;return;}
            var first=screens.FirstOrDefault(s=>s.View!=null);
            if(first==null){ResetAudio();return;}
            if(generation!=first.View.Generation||first.View.Tick<lastTick){ResetAudio(true);generation=first.View.Generation;}
            radioActive = true;
            foreach (var order in radioApplied)
            {
                var screen = screens.FirstOrDefault(s => s.View?.OwnerId == order.Command.PlayerId);
                radio.Order(order.Command, screen?.View, order.Recipients, order.Relay);
                if (screen != null && order.FlightSequences.Count > 0) flights.Add(order);
            }
            radioApplied.Clear();
            if (first.View.Tick != lastTick)
            {
                for (int i = flights.Count - 1; i >= 0; i--)
                {
                    var flight = flights[i];
                    var view = screens.FirstOrDefault(s => s.View?.OwnerId == flight.Command.PlayerId)?.View;
                    int pilot = view == null ? -1 : ObserveFlight(flight, view);
                    if (pilot == 0) continue;
                    flights.RemoveAt(i);
                    if (pilot > 0) radio.Arrived(view, pilot);
                }
            }
            long previousTick=lastTick;
            lastTick=first.View.Tick;
            if(first.View.Domination.HasValue)
            {
                var timer=first.View.MatchEvents.LastOrDefault(item=>item.Kind==MatchEventKind.DominationCountdown&&item.Id>dominationAudioEvent);
                if(timer!=null)
                {
                    dominationAudioEvent=timer.Id;
                    if(previousTick>=0&&first.View.Domination.Value.Rising&&dominationTick)
                    {
                        dominationTimer.volume=DiagnosticSuppressOutput?0:profile.notificationGain*NativeAudioSettings.Notifications;
                        dominationTimer.PlayOneShot(dominationTick);
                    }
                }
            }
            foreach(var v in voices)
            {if(v.Key!=null&&v.PhaseEndTick>0&&lastTick>=v.PhaseEndTick){v.Source.Stop();v.Key=null;v.Gain=0;v.TargetGain=0;}if(v.Key!=null&&!v.Source.isPlaying&&!v.Loop){v.Key=null;v.Gain=0;}if(v.Loop)v.TargetGain=0;}
            double now=Time.unscaledTimeAsDouble;
            foreach(var cue in plan.Observe(screens,now,true))Play(cue);
            speech.Tick(screens,true,now);
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
            foreach(var key in commandKeys.Where(p=>now-p.Value>2).Select(p=>p.Key).ToArray()){commandKeys.Remove(key);pendingCommands.Remove(key);radioPending.Remove(key);}
        }
        private void LateUpdate()
        {
            radio?.Flush(radioActive, speech != null && speech.Busy, profile.speechGain, Time.unscaledTimeAsDouble);
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
            voice.Source.Stop();voice.Source.volume=0;voice.PhaseEndTick=cue.PhaseEndTick;voice.Key=cue.Key;voice.Loop=cue.Loop;voice.Kind=cue.Kind;
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
            if (radioSource) Destroy(radioSource.gameObject);
            if(speech)Destroy(speech);
            if(notification)Destroy(notification.gameObject);if(ownsGuard&&mixGuard)Destroy(mixGuard);if(ownedListener)Destroy(ownedListener);
        }
    }
}
