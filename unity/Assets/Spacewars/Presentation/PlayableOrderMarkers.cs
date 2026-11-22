using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Presentation
{
    // Local input only. It never supplies a projected or confirmed route target.
    public sealed class PlayableOrderInputPulse
    {
        private readonly PlayableCommand intent;
        private readonly double start;
        public long Generation=>intent.Generation;
        public double Ends {get;}
        public PlayableCommandKind Kind=>intent.Kind;
        private PlayableOrderInputPulse(PlayableCommand intent,double now,double duration)
        {this.intent=intent;start=now;Ends=now+duration;}
        internal static PlayableOrderInputPulse ForIntent(PlayableSnapshot view,PlayableCommand intent,double now,Func<double> duration)
        {
            if(view==null||intent.Generation!=view.Generation||intent.PlayerId!=view.OwnerId||intent.EntityIds.Count==0)return null;
            switch(intent.Kind){
                case PlayableCommandKind.Move:case PlayableCommandKind.AttackMove:case PlayableCommandKind.Attack:
                case PlayableCommandKind.RepairTarget:case PlayableCommandKind.Follow:case PlayableCommandKind.Stop:case PlayableCommandKind.Hold:
                    return new PlayableOrderInputPulse(intent,now,duration());
                default:return null;
            }
        }
        public IEnumerable<NavLocation> Locations(PlayableSnapshot view)
        {
            if(view==null||view.Generation!=Generation||view.OwnerId!=intent.PlayerId)yield break;
            if(Kind==PlayableCommandKind.Stop||Kind==PlayableCommandKind.Hold){
                foreach(var unit in view.Entities)
                    if(unit.Owner==view.Owner&&unit.Health>0&&intent.EntityIds.Contains(unit.Id))
                        yield return unit.Location??new NavLocation(unit.Position,NavLocation.FlatSurface);
                yield break;
            }
            if(intent.TargetId!=0){
                var entity=view.Entities.FirstOrDefault(e=>e.Id==intent.TargetId&&e.Health>0);
                if(entity!=null){yield return entity.Location??new NavLocation(entity.Position,NavLocation.FlatSurface);yield break;}
                var building=view.Buildings.FirstOrDefault(b=>b.Id==intent.TargetId&&b.Health>0);
                if(building!=null){yield return new NavLocation(building.Position,NavLocation.FlatSurface);yield break;}
                var wreck=view.Wrecks.FirstOrDefault(w=>w.Id==intent.TargetId);
                if(wreck!=null)yield return new NavLocation(wreck.Position,NavLocation.FlatSurface);
                yield break;
            }
            yield return intent.TargetLocation??new NavLocation(intent.Target,NavLocation.FlatSurface);
        }
        public double Progress(double now)=>Math.Max(0,Math.Min(1,(now-start)/(Ends-start)));
    }

    // Event windows are presentation only. A transport admission is never a visible order proof.
    public sealed class PlayableOrderMarkers
    {
        public const double Duration=1;
        sealed class Candidate { public PlayableCommand Command;public PlayableCommandReceipt Receipt;public long SeenSnapshot; }
        sealed class Flash { public long Issuance;public int[] Units;public double Until;public PlayableQueueMarker Marker; }
        readonly Dictionary<long,Candidate> candidates=new Dictionary<long,Candidate>();
        readonly List<Flash> recent=new List<Flash>();
        EnvironmentArtProfile art;
        public PlayableOrderMarkers(EnvironmentArtProfile art=null){this.art=art;}
        public PlayableOrderInputPulse InputPulse(PlayableSnapshot view,PlayableCommand intent,double now)
            =>PlayableOrderInputPulse.ForIntent(view,intent,now,()=> (art??=EnvironmentArtProfile.Load()).orderInputPulseDuration);
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
                // Stop/Hold have no spatial issuance to await; their Applied receipt cancels feedback immediately.
                if(orders.Length==0&&view.Sequence<=c.SeenSnapshot&&c.Command.Kind!=PlayableCommandKind.Stop&&c.Command.Kind!=PlayableCommandKind.Hold)continue;
                // An Applied silent no-op has no new issuance in the typed observation.
                if(orders.Length>0||c.Command.Mode==PlayableOrderMode.Replace){
                    selectionEvent=selectionEvent.Except(ids).ToArray();
                    foreach(var f in recent)f.Units=f.Units.Except(ids).ToArray();
                }
                foreach(var order in orders)
                {
                    if(art==null)art=EnvironmentArtProfile.Load();
                    if(order.Location.HasValue)recent.RemoveAll(f=>Near(f.Marker.Location,order.Location.Value,art.orderConfirmationMergeRadius));
                    recent.Add(new Flash{Issuance=order.IssuanceId,Units=ids,Until=now+Duration,Marker=order});
                }
                candidates.Remove(pair.Key);
            }
            recent.RemoveAll(f=>f.Until<=now||f.Units.Length==0);
        }
        static bool Near(NavLocation? first,NavLocation second,double radius)
        {
            if(!first.HasValue||first.Value.SurfaceId!=second.SurfaceId)return false;
            var a=first.Value.Position;var b=second.Position;double dx=a.X-b.X,dz=a.Z-b.Z;
            return dx*dx+dz*dz<=radius*radius;
        }
        public IReadOnlyList<PlayableQueueMarker> Visible(PlayableSnapshot view,IEnumerable<int> currentSelection,double now)
        {
            Bind(view);if(view==null)return Array.Empty<PlayableQueueMarker>();
            var markers=new List<PlayableQueueMarker>();
            if(now<selectionUntil)markers.AddRange(PlayableQueueMarkers.ForSelection(view,selectionEvent.Intersect(currentSelection??Array.Empty<int>()),true));
            foreach(var f in recent.Where(f=>now<f.Until&&f.Units.Length>0))
            {
                var current=PlayableQueueMarkers.ForSelection(view,f.Units).FirstOrDefault(m=>m.IssuanceId==f.Issuance);
                // A short ground order may finish before its confirmation fades. Keep its accepted
                // point, but never use a cached position for a hidden/dead direct-attack target.
                if(current==null&&(f.Marker.Kind==PlayableCommandKind.Move||f.Marker.Kind==PlayableCommandKind.AttackMove)
                    &&view.Entities.Any(e=>e.Owner==view.Owner&&e.Health>0&&f.Units.Contains(e.Id)))current=f.Marker;
                if(current!=null)markers.Add(new PlayableQueueMarker(current.IssuanceId,current.CommandSequence,current.Number,current.Kind,current.Location,current.Phase,now-(f.Until-Duration)));
            }
            return Array.AsReadOnly(markers.Where(m=>m.Location.HasValue).GroupBy(m=>m.IssuanceId).Select(g=>g.OrderByDescending(m=>(m.Phase&QueueMarkerPhase.Active)!=0).First()).OrderBy(m=>m.CommandSequence).ToArray());
        }
    }
}
