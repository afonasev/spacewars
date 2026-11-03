using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private long nextMatchEvent;
        private readonly Queue<PlayableMatchEvent> matchEvents=new Queue<PlayableMatchEvent>();
        private void MatchEvent(MatchEventKind kind,PlayableOwner owner,NavPoint point,long causeId=0)
        {
            // Safety bound limits presentation memory during pathological damage bursts.
            while(matchEvents.Count>0&&(matchEvents.Count>=4096||Tick-matchEvents.Peek().Tick>profile.NotificationHistorySeconds*30))matchEvents.Dequeue();
            matchEvents.Enqueue(new PlayableMatchEvent(++nextMatchEvent,Tick,owner,kind,point,causeId));
        }
        private PlayableMatchEvent[] MatchEvents(PlayableOwner? owner=null)
            =>matchEvents.Where(e=>Tick-e.Tick<=profile.NotificationHistorySeconds*30&&
                (owner.HasValue?e.Owner==owner.Value:e.Battle)).ToArray();
    }
}
