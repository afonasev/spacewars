using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Presentation-owned FIFO for ordinary intents. An admitted Stop/Hold can
    // remove still-unsent tactical recipients before their projection completes.
    public sealed class HumanMoveOrderQueue
    {
        private sealed class Pending
        {
            internal PlayableProfile Profile;internal PlayableSnapshot View;internal PlayableCommand Intent;internal int[] Recipients;
            internal HumanMoveDestination.Preparation Preparation;internal int RelaySiteId;
        }
        private readonly Queue<Pending> pending=new Queue<Pending>();
        private HumanMoveDestination.Preparation retiredProjection;
        public int Count=>pending.Count;
        public bool Enqueue(PlayableProfile profile,PlayableSnapshot view,PlayableCommand intent,int relaySiteId=0)
        {
            if(pending.Count>=PlayableRuntime.MaxOutstandingCommands)return false;
            pending.Enqueue(new Pending{Profile=profile,View=view,Intent=intent,Recipients=intent.CopyEntityIds(),RelaySiteId=relaySiteId});return true;
        }
        public IReadOnlyList<PlayableCommand> SupersedeWithStop(PlayableCommand stop)
        {
            if(stop.Kind!=PlayableCommandKind.Stop&&stop.Kind!=PlayableCommandKind.Hold)throw new ArgumentException("Expected Stop or Hold.",nameof(stop));
            var stopped=new HashSet<int>(stop.EntityIds);
            var retired=new List<PlayableCommand>();
            int count=pending.Count;
            for(int i=0;i<count;i++){
                var entry=pending.Dequeue();
                if(entry.Intent.Generation==stop.Generation&&entry.Intent.PlayerId==stop.PlayerId&&
                    IsCancellable(entry.Intent.Kind)&&entry.Recipients.Any(stopped.Contains)){
                    entry.Preparation?.Cancel();
                    if(entry.Preparation!=null)retiredProjection=entry.Preparation;
                    entry.Preparation=null;
                    entry.Recipients=entry.Recipients.Where(id=>!stopped.Contains(id)).ToArray();
                    if(entry.Recipients.Length==0){retired.Add(entry.Intent);continue;}
                }
                pending.Enqueue(entry);
            }
            return retired;
        }
        private static bool Projects(PlayableCommandKind kind)=>kind==PlayableCommandKind.Move||kind==PlayableCommandKind.AttackMove;
        private static bool IsCancellable(PlayableCommandKind kind)=>Projects(kind)||kind==PlayableCommandKind.Attack||kind==PlayableCommandKind.Follow;
        public bool TryTake(out PlayableCommand intent,out HumanMoveDestination.Group[] destinations)
            => TryTake(out intent, out destinations, out _);

        public bool TryTake(out PlayableCommand intent,out HumanMoveDestination.Group[] destinations,out int relaySiteId)
        {
            relaySiteId=0;intent=null;destinations=null;if(pending.Count==0)return false;var head=pending.Peek();
            relaySiteId=head.RelaySiteId;
            bool project=Projects(head.Intent.Kind);
            if(project&&retiredProjection!=null){
                if(!retiredProjection.IsCompleted)return false;
                retiredProjection=null;
            }
            if(project&&HumanMoveDestination.CanPrepareInBackground(head.Profile,head.RelaySiteId)){
                if(head.Preparation==null)head.Preparation=HumanMoveDestination.Prepare(head.Profile,head.View,head.Recipients,head.Intent.Target,head.RelaySiteId);
                if(!head.Preparation.IsCompleted)return false;
                // Always retire a failed preparation as well; it cannot wedge
                // the queue or silently turn into an unprojected destination.
                pending.Dequeue();intent=head.Intent;destinations=head.Preparation.Result;return true;
            }
            pending.Dequeue();intent=head.Intent;
            destinations=project?HumanMoveDestination.Resolve(head.Profile,head.View,head.Recipients,head.Intent.Target,relaySiteId:head.RelaySiteId):new[]{new HumanMoveDestination.Group(head.Recipients,head.Intent.Target)};
            return true;
        }
        public void Cancel()
        {foreach(var item in pending)item.Preparation?.Cancel();pending.Clear();retiredProjection=null;}
    }
}
