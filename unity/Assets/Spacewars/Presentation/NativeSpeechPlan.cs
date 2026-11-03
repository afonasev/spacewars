using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Presentation
{
    public sealed class SpokenSignalCue
    {
        public string Key{get;} public string Identity{get;} public string MineCause{get;}
        public double Created{get;} public int Priority{get;}
        public SpokenSignalCue(string key,string identity,double created,int priority,string mineCause=null)
        {Key=key;Identity=identity;Created=created;Priority=priority;MineCause=mineCause;}
    }
    // One detached planner for the device. Reads permitted screen records only, never world authority.
    public sealed class NativeSpeechPlan
    {
        readonly GameplayAudioProfile profile;
        readonly Dictionary<string,NativeMatchNotifications> projectors=new Dictionary<string,NativeMatchNotifications>();
        readonly Dictionary<string,long> aiIds=new Dictionary<string,long>();
        readonly List<SpokenSignalCue> queue=new List<SpokenSignalCue>();
        readonly Dictionary<string,double> repeats=new Dictionary<string,double>(),victimCauses=new Dictionary<string,double>();
        string context;long tick=-1;
        public int Pending=>queue.Count;
        public int Epoch{get;private set;}
        public NativeSpeechPlan(GameplayAudioProfile profile){profile.Validate();this.profile=profile;}
        public void Reset(){Epoch++;projectors.Clear();aiIds.Clear();queue.Clear();repeats.Clear();victimCauses.Clear();context=null;tick=-1;}
        public void Discard()=>queue.Clear();
        public void Observe(IReadOnlyList<GameplayAudioScreen> screens,double now,bool active,bool enabled=true)
        {
            if(!active||screens.Count==0){Reset();return;}
            var valid=screens.Where(s=>s.View!=null&&!s.Overview).GroupBy(s=>s.View.OwnerId+":"+s.Overview).Select(g=>g.First()).ToArray();
            if(valid.Length==0){Reset();return;}
            string next=string.Join("|",valid.Select(s=>s.View.Generation+":"+s.View.OwnerId+":"+s.Overview).OrderBy(s=>s));
            long current=valid.Max(s=>s.View.Tick);bool baseline=context!=next||current<tick;
            if(baseline)Reset();context=next;tick=current;
            var fresh=new List<SpokenSignalCue>();
            foreach(var screen in valid)
            {
                var view=screen.View;string owner=view.OwnerId+":"+screen.Overview;
                if(!projectors.TryGetValue(owner,out var notifications))projectors.Add(owner,notifications=new NativeMatchNotifications());
                notifications.Update(view,screen.Overview,now,view.ActiveProfile??PlayableProfile.Default);
                if(!baseline)foreach(var entry in notifications.NewEntries)
                {
                    if(entry.VoiceKey==null||view.Tick-entry.Tick>profile.speechMaxAgeSeconds*30)continue;
                    string cause=entry.CauseId>0?view.Generation+":"+entry.Tick+":"+entry.CauseId:null;
                    fresh.Add(new SpokenSignalCue(entry.VoiceKey,entry.Identity,now,entry.Priority,cause));
                }
                aiIds.TryGetValue(owner,out long last);
                foreach(var message in screen.Communications.OrderBy(e=>e.Id))
                {
                    if(message.Generation!=view.Generation||message.Id<=last)continue;last=message.Id;
                    if(baseline||message.Author==view.OwnerId&&!screen.Overview||!screen.Overview&&message.Team!=view.Team||view.Tick-message.Tick>profile.speechMaxAgeSeconds*30||message.Tick>view.Tick||message.VoiceKey==null)continue;
                    fresh.Add(new SpokenSignalCue(message.VoiceKey,"ai:"+message.Id,now,message.Critical?3:2));
                }
                aiIds[owner]=last;
            }
            foreach(var cause in fresh.Where(c=>c.Key=="system-mine-hit"&&c.MineCause!=null).Select(c=>c.MineCause))victimCauses[cause]=now;
            foreach(var cue in fresh.GroupBy(c=>c.Identity).Select(g=>g.OrderByDescending(c=>c.Priority).First()))
            {
                if(!SpokenSignalCatalog.Phrases.ContainsKey(cue.Key)||cue.Key=="system-mine-triggered"&&cue.MineCause!=null&&victimCauses.ContainsKey(cue.MineCause))continue;
                if(queue.Any(c=>c.Identity==cue.Identity||c.Key==cue.Key))continue;
                if(repeats.TryGetValue(cue.Key,out double at)&&now-at<profile.speechRepeatSeconds)continue;
                queue.Add(cue);
            }
            queue.RemoveAll(c=>now-c.Created>=profile.speechMaxAgeSeconds||c.Key=="system-mine-triggered"&&c.MineCause!=null&&victimCauses.ContainsKey(c.MineCause));
            var ordered=queue.OrderByDescending(c=>c.Priority).ThenBy(c=>c.Created).Take(profile.speechQueueLimit).ToArray();queue.Clear();queue.AddRange(ordered);
            foreach(var key in repeats.Where(p=>now-p.Value>profile.speechRepeatSeconds+profile.speechMaxAgeSeconds).Select(p=>p.Key).ToArray())repeats.Remove(key);
            foreach(var key in victimCauses.Where(p=>now-p.Value>profile.speechMaxAgeSeconds).Select(p=>p.Key).ToArray())victimCauses.Remove(key);
            // Consume disabled/silent events; enabling voice later cannot replay them.
            if(!enabled)queue.Clear();
        }
        public SpokenSignalCue Next(double now,bool busy)
        {
            queue.RemoveAll(c=>now-c.Created>=profile.speechMaxAgeSeconds);
            // Allied action speech is immediate or discarded; it must not describe a
            // cancelled operation after waiting behind another spoken line. Text remains.
            if(busy){queue.RemoveAll(c=>c.Identity.StartsWith("ai:",StringComparison.Ordinal));return null;}
            if(queue.Count==0)return null;
            var cue=queue[0];queue.RemoveAt(0);repeats[cue.Key]=now;return cue;
        }
    }
}
