using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Presentation
{
    // Event windows are presentation only. A transport admission is never a visible order proof.
    public sealed class PlayableOrderMarkers
    {
        public const double Duration=1;
        sealed class Candidate { public PlayableCommand Command;public PlayableCommandReceipt Receipt;public long SeenSnapshot; }
        sealed class Flash { public long Issuance;public int[] Units;public double Until; }
        readonly Dictionary<long,Candidate> candidates=new Dictionary<long,Candidate>();
        readonly List<Flash> recent=new List<Flash>();
        int[] selectionEvent=Array.Empty<int>();double selectionUntil;
        long generation=-1,tick=-1,snapshot=-1;string owner;
        void Bind(PlayableSnapshot view)
        {
            if(view==null)return;
            if(generation!=view.Generation||owner!=view.OwnerId||view.Tick<tick||view.Sequence<snapshot||view.Outcome!=PlayableMatchOutcome.Playing){candidates.Clear();recent.Clear();selectionEvent=Array.Empty<int>();selectionUntil=0;}
            generation=view.Generation;owner=view.OwnerId;tick=view.Tick;snapshot=view.Sequence;
        }
        public void Selected(PlayableSnapshot view,IEnumerable<int> ids,double now)
        {Bind(view);selectionEvent=(ids??Array.Empty<int>()).ToArray();foreach(var f in recent)f.Units=f.Units.Except(selectionEvent).ToArray();selectionUntil=now+Duration;}
        public void Submitted(PlayableSnapshot view,PlayableCommand command)
        {
            Bind(view);if(view==null||command==null||command.PlayerId!=view.OwnerId||command.Generation!=view.Generation)return;
            if(command.Kind==PlayableCommandKind.Move||command.Kind==PlayableCommandKind.AttackMove||command.Kind==PlayableCommandKind.Attack||command.Kind==PlayableCommandKind.Stop||command.Kind==PlayableCommandKind.Hold||command.Kind==PlayableCommandKind.Follow)
                candidates[command.Sequence]=new Candidate{Command=command};
        }
        public void Observe(PlayableSnapshot view,IEnumerable<PlayableCommandReceipt> receipts,double now)
        {
            Bind(view);if(view==null)return;
            foreach(var receipt in receipts??Array.Empty<PlayableCommandReceipt>())
            {
                if(receipt.OwnerId!=view.OwnerId||!candidates.TryGetValue(receipt.Sequence,out var c))continue;
                if(receipt.Status!=PlayableCommandStatus.Applied){candidates.Remove(receipt.Sequence);continue;}
                c.Receipt=receipt;c.SeenSnapshot=view.Sequence;
            }
            foreach(var pair in candidates.ToArray())
            {
                var c=pair.Value;if(c.Receipt==null||view.Tick<c.Receipt.AppliedTick)continue;
                var ids=c.Command.CopyEntityIds();var orders=PlayableQueueMarkers.ForSelection(view,ids).Where(m=>m.CommandSequence==pair.Key).ToArray();
                if(orders.Length==0&&view.Sequence<=c.SeenSnapshot)continue;
                // An Applied silent no-op has no new issuance in the typed observation.
                if(orders.Length>0||c.Command.Mode==PlayableOrderMode.Replace){
                    selectionEvent=selectionEvent.Except(ids).ToArray();
                    foreach(var f in recent)f.Units=f.Units.Except(ids).ToArray();
                }
                foreach(var order in orders)recent.Add(new Flash{Issuance=order.IssuanceId,Units=ids,Until=now+Duration});
                candidates.Remove(pair.Key);
            }
            recent.RemoveAll(f=>f.Until<=now||f.Units.Length==0);
        }
        public IReadOnlyList<PlayableQueueMarker> Visible(PlayableSnapshot view,IEnumerable<int> currentSelection,double now)
        {
            Bind(view);if(view==null)return Array.Empty<PlayableQueueMarker>();
            var markers=new List<PlayableQueueMarker>();
            if(now<selectionUntil)markers.AddRange(PlayableQueueMarkers.ForSelection(view,selectionEvent.Intersect(currentSelection??Array.Empty<int>()),true));
            foreach(var f in recent.Where(f=>now<f.Until))markers.AddRange(PlayableQueueMarkers.ForSelection(view,f.Units).Where(m=>m.IssuanceId==f.Issuance));
            return Array.AsReadOnly(markers.Where(m=>m.Location.HasValue).GroupBy(m=>m.IssuanceId).Select(g=>g.OrderByDescending(m=>(m.Phase&QueueMarkerPhase.Active)!=0).First()).OrderBy(m=>m.CommandSequence).ToArray());
        }
    }
}
