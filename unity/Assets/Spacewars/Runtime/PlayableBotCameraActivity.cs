using System;
using System.Linq;
using System.Threading;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Detached presentation hint only; not AI knowledge, policy state or persisted simulation.
    public sealed class BotCameraActivity
    {
        public readonly string OwnerId;public readonly long Generation,Sequence,Tick;public readonly NavPoint Position;
        public BotCameraActivity(string owner,long generation,long sequence,long tick,NavPoint position)
        {OwnerId=owner;Generation=generation;Sequence=sequence;Tick=tick;Position=position;}
    }
    internal sealed partial class PlayableDomain
    {
        private BotCameraActivity[] botCameraActivities=Array.Empty<BotCameraActivity>();
        internal BotCameraActivity[] BotCameraActivities=>botCameraActivities;
        internal void RecordBotCameraActivity(PlayableCommand command)
        {
            if(command.Origin!=PlayableOrderOrigin.Ai)return;
            var owner=OwnerFor(command.PlayerId);NavPoint? point=null;
            var actors=command.EntityIds.Where(id=>units.TryGetValue(id,out var u)&&u.Owner==owner&&TryActor(id,out _)).Select(id=>{TryActor(id,out var actor);return actor.Position;}).ToArray();
            if(actors.Length>0)point=new NavPoint(actors.Average(p=>p.X),actors.Average(p=>p.Z));
            else {
                // Buildings are authority positions belonging to the issuing owner only.
                var building=command.EntityIds.Select(id=>buildings.TryGetValue(id,out var b)?b:null).FirstOrDefault(b=>b!=null&&b.Owner==owner);
                if(command.Kind==PlayableCommandKind.BuildAt)building=buildings.Values.Where(b=>b.Owner==owner&&b.SiteId==command.SiteId&&b.Kind==command.BuildingKind).OrderByDescending(b=>b.Id).FirstOrDefault();
                if(building==null&&command.ParentId>0&&buildings.TryGetValue(command.ParentId,out var parent)&&parent.Owner==owner)building=parent;
                if(building!=null)point=building.Position;
            }
            if(!point.HasValue)return;
            var next=new BotCameraActivity(command.PlayerId,command.Generation,command.Sequence,Tick,point.Value);
            botCameraActivities=botCameraActivities.Where(a=>a.OwnerId!=command.PlayerId).Concat(new[]{next}).ToArray();
        }
    }
    public sealed partial class PlayableRuntime
    {
        private BotCameraActivity[] botCameraActivities=Array.Empty<BotCameraActivity>();
        public BotCameraActivity BotCameraFocus(string ownerId)=>Volatile.Read(ref botCameraActivities).FirstOrDefault(a=>a.OwnerId==ownerId);
    }
}
