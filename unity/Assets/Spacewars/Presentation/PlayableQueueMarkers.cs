using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    [Flags] public enum QueueMarkerPhase { Active=1, Pending=2, Deferred=4 }
    public sealed class PlayableQueueMarker
    {
        public readonly long IssuanceId,CommandSequence;
        public readonly int Number;
        public readonly PlayableCommandKind Kind;
        public readonly NavLocation? Location;
        public readonly QueueMarkerPhase Phase;
        public bool Attack=>Kind!=PlayableCommandKind.Move;
        public PlayableQueueMarker(long issuance,long sequence,int number,PlayableCommandKind kind,NavLocation? location,QueueMarkerPhase phase)
        {IssuanceId=issuance;CommandSequence=sequence;Number=number;Kind=kind;Location=location;Phase=phase;}
    }
    // Only typed owner observations enter this derived model. It has no authority or route service.
    public static class PlayableQueueMarkers
    {
        public static IReadOnlyList<PlayableQueueMarker> ForSelection(PlayableSnapshot view,IEnumerable<int> selection,bool activeOnly=false)
        {
            if(view==null)return Array.Empty<PlayableQueueMarker>();
            var selected=new HashSet<int>(selection??Array.Empty<int>());
            var orders=new List<(PlayableQueuedOrderSnapshot order,QueueMarkerPhase phase)>();
            foreach(var unit in view.Entities.Where(u=>u.Owner==view.Owner&&u.Health>0&&selected.Contains(u.Id)))
            {
                var queue=unit.Queue;if(queue==null)continue;
                if(queue.Active!=null)orders.Add((queue.Active,QueueMarkerPhase.Active));
                if(!activeOnly&&queue.Pending!=null)orders.Add((queue.Pending,QueueMarkerPhase.Pending));
                foreach(var order in activeOnly?Array.Empty<PlayableQueuedOrderSnapshot>():queue.Deferred)orders.Add((order,QueueMarkerPhase.Deferred));
            }
            int number=0;
            return Array.AsReadOnly(orders.GroupBy(x=>x.order.IssuanceId).OrderBy(g=>g.First().order.CommandSequence).ThenBy(g=>g.Key).Select(g=>
            {
                var order=g.First().order;NavLocation? location=order.Anchor;
                if(order.Kind==PlayableCommandKind.Attack)
                {
                    // The issuance anchor is never a fallback for a hidden/dead direct target.
                    var entity=view.Entities.FirstOrDefault(e=>e.Id==order.VisibleTargetId&&e.Health>0);
                    var building=view.Buildings.FirstOrDefault(b=>b.Id==order.VisibleTargetId&&b.Health>0);
                    location=entity!=null?new NavLocation(entity.Position,entity.Location?.SurfaceId??NavLocation.FlatSurface):building!=null?new NavLocation(building.Position,NavLocation.FlatSurface):(NavLocation?)null;
                }
                var phases=g.Aggregate((QueueMarkerPhase)0,(phase,x)=>phase|x.phase);
                return new PlayableQueueMarker(order.IssuanceId,order.CommandSequence,++number,order.Kind,location,phases);
            }).ToArray());
        }
    }
}
