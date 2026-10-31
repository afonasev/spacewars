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
            public string Text{get;} public NavPoint Point{get;} public double ShownAt{get;} public bool Battle{get;}
            internal Entry(string text,NavPoint point,double now,bool battle){Text=text;Point=point;ShownAt=now;Battle=battle;}
        }
        private sealed class Front{internal PlayableOwner Owner;internal NavPoint Point;internal long LastTick;}
        private readonly List<Front> fronts=new List<Front>();
        private readonly List<Entry> history=new List<Entry>();
        private readonly HashSet<string> signals=new HashSet<string>();
        private long generation=-1,lastTick=-1,lastEvent;
        private string context;
        private Entry cursor;
        private PlayableProfile cachedProfile;
        private PlayableProfileData settings;
        public PlayableProfileData Settings(PlayableProfile profile){if(!ReferenceEquals(profile,cachedProfile)){cachedProfile=profile;settings=profile.CopyData();}return settings;}
        public Entry Latest=>history.LastOrDefault();
        public int Count=>history.Count;
        public static string Label(MatchEventKind kind)=>kind==MatchEventKind.BuildingAttacked?"Здание атаковано":kind==MatchEventKind.UnitsAttacked?"Юниты атакованы":kind==MatchEventKind.BuildingCompleted?"Здание построено":kind==MatchEventKind.UnitReady?"Юнит готов":"Исследование завершено";
        public void Update(PlayableSnapshot view,bool overview,double now,PlayableProfile profile)
        {
            var data=Settings(profile);string nextContext=overview?"overview":view.OwnerId;
            bool newGeneration=generation!=view.Generation||view.Tick<lastTick;
            bool switchView=context!=null&&context!=nextContext&&!newGeneration;
            if(newGeneration||switchView){history.Clear();fronts.Clear();signals.Clear();cursor=null;lastEvent=0;}
            generation=view.Generation;context=nextContext;lastTick=view.Tick;
            Prune(now,data.notificationHistorySeconds);
            if(switchView){lastEvent=view.MatchEvents.Select(e=>e.Id).DefaultIfEmpty(0).Max();foreach(var s in view.TeamSignals)signals.Add(SignalKey(s));return;}
            var pending=new List<(long tick,long id,int source,PlayableMatchEvent fact,PlayableTeamSignal signal)>();
            foreach(var e in view.MatchEvents.Where(e=>e.Id>lastEvent).OrderBy(e=>e.Id)){
                lastEvent=Math.Max(lastEvent,e.Id);
                if(overview?!e.Battle:e.Owner!=view.Owner)continue;
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
                    if(e.Battle){fronts.RemoveAll(f=>e.Tick-f.LastTick>data.notificationFrontIdleSeconds*30);
                        var front=fronts.Where(f=>f.Owner==e.Owner&&Distance(f.Point,e.Point)<=data.notificationFrontRadius*data.notificationFrontRadius).OrderBy(f=>Distance(f.Point,e.Point)).FirstOrDefault();
                        if(front!=null){front.Point=e.Point;front.LastTick=e.Tick;continue;}
                        fronts.Add(new Front{Owner=e.Owner,Point=e.Point,LastTick=e.Tick});
                    }
                    Admit(Label(e.Kind),e.Point,now,e.Battle);
                }else{
                    var s=p.signal;Admit(s.Kind==TeamSignalKind.Attack?"Союзник: атаковать":s.Kind==TeamSignalKind.Defend?"Союзник: защищать":"Союзник: разведать",s.Point,now,false);
                }
            }
        }
        private static string SignalKey(PlayableTeamSignal s)=>s.Id+":"+s.CreatedTick;
        private static double Distance(NavPoint a,NavPoint b)=>Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2);
        // Shared admission point for authorized future positional AI communications.
        public void Admit(string text,NavPoint point,double now,bool battle=false){var entry=new Entry(text,point,now,battle);history.Add(entry);cursor=entry;}
        public void Prune(double now,double seconds){history.RemoveAll(e=>now>=e.ShownAt+seconds);if(cursor!=null&&!history.Contains(cursor))cursor=null;}
        public Entry Visible(double now,PlayableProfile profile){Prune(now,Settings(profile).notificationHistorySeconds);return Latest!=null&&now<Latest.ShownAt+Settings(profile).notificationSeconds?Latest:null;}
        public NavPoint? Focus(bool previous,double now,PlayableProfile profile)
        {
            Prune(now,Settings(profile).notificationHistorySeconds);if(history.Count==0)return null;
            int index=cursor==null?history.Count-1:history.IndexOf(cursor);
            cursor=history[previous?Math.Max(0,index-1):history.Count-1];return cursor.Point;
        }
    }
}
