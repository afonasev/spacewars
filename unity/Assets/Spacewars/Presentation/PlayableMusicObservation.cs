using System;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    // Presentation projections only. The caller supplies human screens, never AI views.
    public sealed class PlayableMusicObservation
    {
        private readonly Dictionary<string,Dictionary<string,int>> health=new Dictionary<string,Dictionary<string,int>>();
        private readonly Dictionary<string,long> seen=new Dictionary<string,long>();
        private readonly HashSet<string> events=new HashSet<string>();
        private readonly List<string> expired=new List<string>();
        private long generation=long.MinValue,lastTick=-1;

        public void Reset(){health.Clear();seen.Clear();events.Clear();generation=long.MinValue;lastTick=-1;}

        public int Observe(IEnumerable<PlayableSnapshot> screens)
        {
            events.Clear();long tick=-1;
            foreach(var screen in screens)
            {
                if(screen==null)continue;
                if(generation!=screen.Generation){Reset();generation=screen.Generation;}
                tick=screen.Tick;
                if(tick<=lastTick)continue;
                foreach(var shot in screen.Projectiles)if(shot.Visible)NewEvent("shot:"+shot.Id,tick);
                foreach(var hit in screen.Impacts)NewEvent("hit:"+hit.Id,tick);
                health.TryGetValue(screen.OwnerId,out var previous);
                var current=new Dictionary<string,int>();
                foreach(var unit in screen.Entities)Health("unit:"+unit.Id,unit.Health,previous,current,tick);
                foreach(var building in screen.Buildings)Health("building:"+building.Id,building.Health,previous,current,tick);
                health[screen.OwnerId]=current;
            }
            if(tick>lastTick)
            {
                lastTick=tick;expired.Clear();
                // IDs cannot grow with match duration. 600 ticks is an observation cache horizon,
                // not a simulation rule or a music intensity threshold.
                foreach(var pair in seen)if(tick-pair.Value>600)expired.Add(pair.Key);
                foreach(var key in expired)seen.Remove(key);
            }
            return events.Count;
        }

        private void NewEvent(string key,long tick)
        {if(!seen.ContainsKey(key))events.Add(key);seen[key]=tick;}

        private void Health(string key,int value,Dictionary<string,int> previous,Dictionary<string,int> current,long tick)
        {
            current[key]=value;
            // Disappearance and re-discovery are not damage: only consecutive visible baselines.
            if(previous!=null&&previous.TryGetValue(key,out var old)&&value<old)
                events.Add("damage:"+key+":"+tick);
        }
    }
}
