using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    [Serializable] public sealed class GameplayAudioProfile
    {
        public string id="hybrid-gameplay-v2";
        public double near=8,far=70,mergeDistance=8,repeatSeconds=.08,movementCell=18,fadeSeconds=.15;
        public float effectsBudget=.38f,notificationGain=.12f,shotGain=.28f,impactGain=.14f,explosionGain=.30f,movementGain=.08f;
        public int voices=12,shots=6,impacts=4,explosions=3,movementGroups=3,freshTicks=6;
        public float baseGain=.16f,damageGain=.18f,motionAccentGain=.08f;
        public double warningSeconds=6,notificationSeconds=.25,baseRepeatSeconds=.4,motionRepeatSeconds=.8,heavyDamageRatio=.35;
        public int baseVoices=3;
        public void Validate()
        {
            foreach(var field in GameplayAudioMetadata.Fields)field.Validate(field.Read(this));
            if(far<=near||movementGroups>voices)throw new ArgumentException("Invalid gameplay audio profile.");
        }
    }
    public sealed class GameplayAudioScreen
    {
        public readonly PlayableSnapshot View;
        public readonly NavPoint Focus,Right;
        public readonly ICollection<int> Selected;
        public GameplayAudioScreen(PlayableSnapshot view,NavPoint focus,NavPoint right,ICollection<int> selected=null)
        {View=view;Focus=focus;Right=right;Selected=selected;}
    }
    public sealed class GameplayAudioCue
    {
        public string Key,Clip;
        public long EventId;
        public NavPoint Position;
        public float Gain,BaseGain,Pan,Priority,Pitch=1;
        public bool Loop,Notification;
        public PlayableSoundKind Kind;
        public readonly HashSet<string> Observers=new HashSet<string>();
    }
    // No Unity objects or world authority reads. All inputs are permitted projections.
    public sealed class GameplayAudioPlan
    {
        private readonly GameplayAudioProfile profile;
        private readonly Dictionary<long,long> seen=new Dictionary<long,long>();
        private readonly Dictionary<string,double> repeats=new Dictionary<string,double>();
        private long generation=long.MinValue,lastTick=-1;
        public GameplayAudioPlan(GameplayAudioProfile profile){profile.Validate();this.profile=profile;}
        public int SeenCount=>seen.Count;
        public void Reset(){seen.Clear();repeats.Clear();generation=long.MinValue;lastTick=-1;}
        public double Attenuation(double distance)
        {double t=Math.Max(0,Math.Min(1,(distance-profile.near)/(profile.far-profile.near)));return (1-t)*(1-t);}
        public bool Spatial(GameplayAudioScreen s,NavPoint position,out float gain,out float pan)
        {
            double x=position.X-s.Focus.X,z=position.Z-s.Focus.Z,d=Math.Sqrt(x*x+z*z);
            gain=(float)Attenuation(d);pan=(float)Math.Max(-.85,Math.Min(.85,(x*s.Right.X+z*s.Right.Z)/profile.far));
            return !double.IsNaN(gain)&&!double.IsNaN(pan)&&gain>.002;
        }
        public GameplayAudioCue[] Observe(IReadOnlyList<GameplayAudioScreen> screens,double now,bool active)
        {
            if(!active||screens.Count==0){Reset();return Array.Empty<GameplayAudioCue>();}
            var first=screens.FirstOrDefault(s=>s.View!=null);if(first==null){Reset();return Array.Empty<GameplayAudioCue>();}
            long tick=first.View.Tick;
            bool baseline=generation!=first.View.Generation||tick<lastTick;
            if(baseline){Reset();generation=first.View.Generation;}
            var cues=new Dictionary<long,GameplayAudioCue>();
            var movement=new Dictionary<int,Tuple<PlayableEntitySnapshot,GameplayAudioScreen,float,float>>();
            foreach(var screen in screens)
            {
                var view=screen.View;if(view==null||view.Generation!=generation||view.Tick!=tick)continue;
                foreach(var e in view.Sounds)
                {
                    if(baseline){seen[e.Id]=e.Tick;continue;}
                    if(e.Tick>tick||tick-e.Tick>profile.freshTicks||seen.ContainsKey(e.Id))continue;
                    bool notification=PlayableSoundPolicy.OwnerOnly(e.Kind);
                    if(e.Kind==PlayableSoundKind.HeavyDamage&&!HeavyDamage(view,e))continue;
                    float gain=1,pan=0;
                    if(!notification&&!Spatial(screen,e.Position,out gain,out pan))continue;
                    bool selected=screen.Selected!=null&&view.Entities.Any(u=>screen.Selected.Contains(u.Id)&&Distance(u.Position,e.Position)<3);
                    float baseGain=notification?profile.notificationGain:BaseGain(e);
                    var cue=new GameplayAudioCue{Key="event:"+e.Id,EventId=e.Id,Position=e.Position,Kind=e.Kind,Gain=gain*baseGain,BaseGain=baseGain,
                        Pan=pan,Priority=gain+(selected ? .5f : 0)+(e.Kind==PlayableSoundKind.BaseThreat||e.Kind==PlayableSoundKind.ProductionLimited?3f:e.Kind==PlayableSoundKind.Destroyed||e.Kind==PlayableSoundKind.HeavyDamage?1f:0),
                        Clip=Clip(e),Notification=notification,Pitch=e.Weapon==PlayableEntityKind.Shkval ? .85f : 1};
                    cue.Observers.Add(view.OwnerId);
                    if(cues.TryGetValue(e.Id,out var old))
                    {if(gain*baseGain>old.Gain){cue.Observers.UnionWith(old.Observers);cues[e.Id]=cue;}else old.Observers.Add(view.OwnerId);}
                    else cues[e.Id]=cue;
                }
                foreach(var unit in view.Entities)
                {
                    if(!unit.Moving||unit.Health<=0||!Spatial(screen,unit.Position,out var gain,out var pan))continue;
                    if(!movement.TryGetValue(unit.Id,out var old)||gain>old.Item3)
                        movement[unit.Id]=Tuple.Create(unit,screen,gain,pan);
                }
            }
            // Mark every currently exposed event, including inaudible/discarded ones: never catch up later.
            foreach(var screen in screens)if(screen.View?.Generation==generation)
                foreach(var e in screen.View.Sounds)seen[e.Id]=e.Tick;
            lastTick=tick;
            foreach(var id in seen.Where(p=>tick-p.Value>30).Select(p=>p.Key).ToArray())seen.Remove(id);
            foreach(var key in repeats.Where(p=>now-p.Value>Math.Max(profile.warningSeconds,Math.Max(profile.motionRepeatSeconds,profile.baseRepeatSeconds))+1).Select(p=>p.Key).ToArray())repeats.Remove(key);
            var result=new List<GameplayAudioCue>();var counts=new Dictionary<PlayableSoundKind,int>();
            foreach(var cue in cues.Values.OrderByDescending(c=>c.Priority).ThenBy(c=>c.EventId))
            {
                int cap=CategoryCap(cue.Kind);
                counts.TryGetValue(cue.Kind,out var count);
                string cell=cue.Notification?"notification:"+cue.Clip:cue.Clip+":"+Math.Floor(cue.Position.X/profile.mergeDistance)+":"+Math.Floor(cue.Position.Z/profile.mergeDistance);
                double interval=cue.Kind==PlayableSoundKind.HeavyDamage||cue.Kind==PlayableSoundKind.BaseThreat||cue.Kind==PlayableSoundKind.ProductionLimited?profile.warningSeconds:cue.Notification?profile.notificationSeconds:cue.Kind==PlayableSoundKind.MovementStarted||cue.Kind==PlayableSoundKind.MovementStopped?profile.motionRepeatSeconds:(int)cue.Kind>2?profile.baseRepeatSeconds:profile.repeatSeconds;
                if(count>=cap||!cue.Notification&&result.Count(c=>!c.Notification)>=profile.voices||cue.Notification&&result.Any(c=>c.Notification)||repeats.TryGetValue(cell,out var at)&&now-at<interval)continue;
                repeats[cell]=now;counts[cue.Kind]=count+1;result.Add(cue);
            }
            var groups=movement.Values.GroupBy(u=>u.Item1.Kind+":"+Math.Floor(u.Item1.Position.X/profile.movementCell)+":"+Math.Floor(u.Item1.Position.Z/profile.movementCell))
                .Select(g=>new {Key=g.Key,Best=g.OrderByDescending(u=>u.Item3).First()}).OrderByDescending(g=>g.Best.Item3).Take(profile.movementGroups);
            foreach(var group in groups)
            {
                if(result.Count(c=>!c.Notification)>=profile.voices)break;
                var best=group.Best;result.Add(new GameplayAudioCue{Key="movement:"+group.Key,Clip="movement-loop",Position=best.Item1.Position,
                    Gain=best.Item3*profile.movementGain,BaseGain=profile.movementGain,Pan=best.Item4,Priority=best.Item3*.1f,Loop=true,
                    Pitch=best.Item1.Kind==PlayableEntityKind.Explorer?1.2f:best.Item1.Kind==PlayableEntityKind.Shkval ? .85f : 1});
            }
            return result.ToArray();
        }
        public int CategoryCap(PlayableSoundKind kind)=>kind==PlayableSoundKind.Shot?profile.shots:kind==PlayableSoundKind.Impact?profile.impacts:kind==PlayableSoundKind.Destroyed?profile.explosions:profile.baseVoices;
        private bool HeavyDamage(PlayableSnapshot view,PlayableSoundEvent e)
        {
            var p=view.ActiveProfile??PlayableProfile.Default;
            return e.Building?view.Buildings.Any(b=>Distance(b.Position,e.Position)<1&&b.Health>0&&b.Health<=TerritoryRules.Health(p,b.Kind)*profile.heavyDamageRatio):view.Entities.Any(u=>Distance(u.Position,e.Position)<3&&u.Kind==e.Weapon&&u.Health>0&&u.Health<=PlayableUnitRules.Health(p,u.Kind)*profile.heavyDamageRatio);
        }
        private float BaseGain(PlayableSoundEvent e)=>e.Kind==PlayableSoundKind.HeavyDamage?profile.damageGain:e.Kind==PlayableSoundKind.MovementStarted||e.Kind==PlayableSoundKind.MovementStopped?profile.motionAccentGain:(int)e.Kind>2?profile.baseGain:e.Kind==PlayableSoundKind.Destroyed||e.Kind==PlayableSoundKind.Impact&&e.Weapon==PlayableEntityKind.Shkval?profile.explosionGain:e.Kind==PlayableSoundKind.Impact?profile.impactGain:profile.shotGain;
        private static string Clip(PlayableSoundEvent e)
        {
            switch(e.Kind)
            {
                case PlayableSoundKind.ProductionQueued:case PlayableSoundKind.ResearchQueued:return "queued";
                case PlayableSoundKind.ProductionStarted:case PlayableSoundKind.ResearchStarted:case PlayableSoundKind.UpgradeStarted:return "work-start";
                case PlayableSoundKind.ProductionComplete:case PlayableSoundKind.ResearchComplete:case PlayableSoundKind.UpgradeComplete:return "ready";
                case PlayableSoundKind.ProductionCancelled:case PlayableSoundKind.ResearchCancelled:case PlayableSoundKind.UpgradeCancelled:case PlayableSoundKind.ConstructionCancelled:case PlayableSoundKind.RepairCancelled:return "cancel";
                case PlayableSoundKind.BaseThreat:case PlayableSoundKind.ProductionLimited:return "warning";
                case PlayableSoundKind.HeavyDamage:return "damage";
                case PlayableSoundKind.DemolitionStarted:case PlayableSoundKind.Demolished:return "demolition";
                case PlayableSoundKind.ConstructionStarted:case PlayableSoundKind.ConstructionComplete:return "construction";
                case PlayableSoundKind.RepairStarted:case PlayableSoundKind.RepairComplete:return "repair";
                case PlayableSoundKind.MovementStarted:return "motion-start";
                case PlayableSoundKind.MovementStopped:return "motion-stop";
            }
            if(e.Kind==PlayableSoundKind.Destroyed||e.Kind==PlayableSoundKind.Impact&&e.Weapon==PlayableEntityKind.Shkval)return "explosion";
            if(e.Kind==PlayableSoundKind.Impact)return "impact";
            return e.Weapon==PlayableEntityKind.Explorer?"burst-shot":"tank-shot";
        }
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
    }
}
