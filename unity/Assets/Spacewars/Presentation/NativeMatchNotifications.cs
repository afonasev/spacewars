using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Presentation
{
    // One instance per viewport. Receives only that viewport's authorized facts.
    public sealed class NativeMatchNotifications
    {
        public sealed class Entry
        {
            public string VoiceKey{get;} public string Identity{get;} public long Tick{get;} public long CauseId{get;} public int Priority{get;}
            public string Text{get;} public NavPoint Point{get;} public double ShownAt{get;} public bool Battle{get;}
            internal bool Focusable=true;
            internal long NuclearId=-1;
            internal Entry(string text,NavPoint point,double now,bool battle,string voiceKey=null,string identity=null,long tick=0,long causeId=0,int priority=0){VoiceKey=voiceKey;Identity=identity;Tick=tick;CauseId=causeId;Priority=priority;Text=text;Point=point;ShownAt=now;Battle=battle;}
        }
        private sealed class Front{internal PlayableOwner Owner;internal NavPoint Point;internal long LastTick;}
        private readonly List<Front> fronts=new List<Front>();
        private readonly List<Entry> fresh=new List<Entry>();
        public IReadOnlyList<Entry> NewEntries=>fresh;
        private readonly List<Entry> history=new List<Entry>();
        private const int GamepadHistorySize = 5;
        private readonly List<Entry> gamepadHistory = new List<Entry>(GamepadHistorySize);
        private Entry gamepadCursor;
        private readonly HashSet<string> signals=new HashSet<string>();
        private long generation=-1,lastTick=-1,lastEvent;
        private string context;
        private Entry cursor;
        private PlayableProfile cachedProfile;
        private PlayableProfileData settings;
        public PlayableProfileData Settings(PlayableProfile profile){if(!ReferenceEquals(profile,cachedProfile)){cachedProfile=profile;settings=profile.CopyData();}return settings;}
        public Entry Latest=>history.LastOrDefault();
        public int Count=>history.Count;
        public void Reset(PlayableSnapshot baseline=null)
        {
            ClearHistory();fresh.Clear();fronts.Clear();signals.Clear();
            generation=baseline?.Generation??-1;lastTick=baseline?.Tick??-1;context=baseline?.OwnerId;
            lastEvent=baseline?.MatchEvents.Select(e=>e.Id).DefaultIfEmpty(0).Max()??0;
            if(baseline!=null)foreach(var signal in baseline.TeamSignals)signals.Add(SignalKey(signal));
        }
        private void ClearHistory()
        {
            history.Clear();
            gamepadHistory.Clear();
            cursor = null;
            gamepadCursor = null;
        }
        public static string Label(MatchEventKind kind){var key=SpokenSignalCatalog.Key(kind);return key==null?"":SpokenSignalCatalog.Phrases[key];}

        public void Update(PlayableSnapshot view,bool overview,double now,PlayableProfile profile)
        {
            fresh.Clear();if(overview&&!view.Domination.HasValue){Reset(view);context="overview";return;}var data=Settings(profile);string nextContext=overview?"overview":view.OwnerId;
            bool newGeneration=generation!=view.Generation||view.Tick<lastTick;
            bool switchView=context!=null&&context!=nextContext&&!newGeneration;
            if(newGeneration||switchView){ClearHistory();fronts.Clear();signals.Clear();lastEvent=0;}
            generation=view.Generation;context=nextContext;lastTick=view.Tick;
            foreach (var entry in history.Concat(gamepadHistory))
                if (entry.Focusable && entry.NuclearId >= 0 &&
                    !view.Orbital.NuclearStrikes.Any(strike => strike.Id == entry.NuclearId && strike.AlliedSignal))
                    entry.Focusable = false;
            gamepadHistory.RemoveAll(entry => !entry.Focusable);
            Prune(now,data.notificationHistorySeconds);
            if(switchView){lastEvent=view.MatchEvents.Select(e=>e.Id).DefaultIfEmpty(0).Max();foreach(var s in view.TeamSignals)signals.Add(SignalKey(s));return;}
            var pending=new List<(long tick,long id,int source,PlayableMatchEvent fact,PlayableTeamSignal signal)>();
            foreach(var e in view.MatchEvents.Where(e=>e.Id>lastEvent).OrderBy(e=>e.Id)){
                lastEvent=Math.Max(lastEvent,e.Id);
                if(!e.Shared&&(overview||e.Owner!=view.Owner&&(!e.Teamwide||view.IsHostile(e.Owner))))continue;
                if(SpokenSignalCatalog.Key(e.Kind)==null)continue;
                // The specific mine alert replaces generic damage from that same nearby blast.
                if(e.Battle&&view.MatchEvents.Any(m=>m.Kind==MatchEventKind.MineHit&&m.Tick==e.Tick&&Distance(m.Point,e.Point)<=data.notificationFrontRadius*data.notificationFrontRadius))continue;
                pending.Add((e.Tick,e.Id,0,e,null));
            }
            var currentSignals=new HashSet<string>();
            foreach(var s in view.TeamSignals){string key=SignalKey(s);currentSignals.Add(key);
                if(!signals.Add(key)||overview||s.Author==view.Owner||s.ExpiresTick<=view.Tick)continue;
                pending.Add((s.CreatedTick,s.Id,1,null,s));
            }
            signals.IntersectWith(currentSignals);
            foreach(var p in pending.OrderBy(e=>e.tick).ThenBy(e=>e.source).ThenBy(e=>e.id)){
                if(p.fact!=null){var e=p.fact;
                    if(e.Kind==MatchEventKind.NuclearLaunch)
                    {
                        long id=e.CauseId-1;
                        var strike=view.Orbital.NuclearStrikes.FirstOrDefault(candidate=>candidate.Id==id && candidate.AlliedSignal);
                        var point=strike?.Center;
                        Admit(Label(e.Kind),point?.Position??default,now,false,SpokenSignalCatalog.NuclearLaunchKey,
                            "nuclear:"+id,e.Tick,0,SpokenSignalCatalog.Priority(e.Kind),point.HasValue,id);
                        cursor=Latest;
                        continue;
                    }
                    if(e.Battle){fronts.RemoveAll(f=>e.Tick-f.LastTick>data.notificationFrontIdleSeconds*30);
                        var front=fronts.Where(f=>f.Owner==e.Owner&&Distance(f.Point,e.Point)<=data.notificationFrontRadius*data.notificationFrontRadius).OrderBy(f=>Distance(f.Point,e.Point)).FirstOrDefault();
                        if(front!=null){front.Point=e.Point;front.LastTick=e.Tick;continue;}
                        fronts.Add(new Front{Owner=e.Owner,Point=e.Point,LastTick=e.Tick});
                    }
                    Admit(Label(e.Kind),e.Point,now,e.Battle,SpokenSignalCatalog.Key(e.Kind),"fact:"+e.Id,e.Tick,e.CauseId,SpokenSignalCatalog.Priority(e.Kind),e.Positional);
                }else{
                    var s=p.signal;var key=SpokenSignalCatalog.Key(s.Kind);Admit(SpokenSignalCatalog.Phrases[key],s.Point,now,false,key,"signal:"+s.Id,s.CreatedTick,0,2);
                }
            }
            // A restored cast has a live private marker even if the transient launch voice has already played.
            foreach(var strike in view.Orbital.NuclearStrikes)
                if(strike.AlliedSignal && strike.Center.HasValue && !history.Any(entry=>entry.NuclearId==strike.Id))
                {
                    var entry=new Entry(SpokenSignalCatalog.Phrases[SpokenSignalCatalog.NuclearLaunchKey],
                        strike.Center.Value.Position,now-data.notificationSeconds,false);
                    entry.NuclearId=strike.Id;
                    history.Add(entry);
                }
        }
        private static string SignalKey(PlayableTeamSignal s)=>s.Id+":"+s.CreatedTick;
        private static double Distance(NavPoint a,NavPoint b)=>Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2);
        // Shared admission point for authorized future positional AI communications.
        public void Admit(string text,NavPoint point,double now,bool battle=false,string voiceKey=null,string identity=null,long tick=0,long causeId=0,int priority=0,bool focusable=true,long nuclearId=-1)
        {
            var entry = new Entry(text,point,now,battle,voiceKey,identity,tick,causeId,priority)
                { Focusable = focusable, NuclearId = nuclearId };
            history.Add(entry);
            fresh.Add(entry);
            if (!focusable) return;

            cursor = entry;
            if (gamepadHistory.Count == GamepadHistorySize) gamepadHistory.RemoveAt(0);
            gamepadHistory.Add(entry);
            gamepadCursor = null;
        }
        public NavPoint? FocusNextGamepadEvent()
        {
            if (gamepadHistory.Count == 0) return null;
            int index = gamepadHistory.IndexOf(gamepadCursor);
            gamepadCursor = gamepadHistory[index > 0 ? index - 1 : gamepadHistory.Count - 1];
            return gamepadCursor.Point;
        }
        public void Prune(double now,double seconds){history.RemoveAll(e=>now>=e.ShownAt+seconds && !(e.NuclearId>=0 && e.Focusable));if(cursor!=null&&!history.Contains(cursor))cursor=null;}
        public Entry Visible(double now,PlayableProfile profile){Prune(now,Settings(profile).notificationHistorySeconds);return Latest!=null&&now<Latest.ShownAt+Settings(profile).notificationSeconds?Latest:null;}
        public NavPoint? Focus(bool previous,double now,PlayableProfile profile)
        {
            Prune(now,Settings(profile).notificationHistorySeconds);if(history.Count==0)return null;
            var eligible=history.Where(entry=>entry.Focusable).ToArray();
            if(eligible.Length==0)return null;
            int index=Array.IndexOf(eligible,cursor);
            if(index<0)index=cursor==null?eligible.Length-1:eligible.Length;
            cursor=eligible[previous?Math.Clamp(index-1,0,eligible.Length-1):eligible.Length-1];return cursor.Point;
        }
    }
}
