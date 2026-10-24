using System;
using System.IO;
using Spacewars.Simulation;
using static Spacewars.Runtime.WorldWire;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private static void WriteAir(BinaryWriter w,AirState a)
        {
            w.Write(a!=null);if(a==null)return;
            var n=a.Actor;w.Write(n.Id);WorldWire.Write(w,n.Position);WorldWire.Write(w,n.Goal);Number(w,n.Heading);w.Write(n.Team);w.Write(n.Moving);w.Write(n.Held);w.Write((int)n.Outcome);Number(w,n.Radius);Number(w,n.Speed);Number(w,n.TurnSpeed);
            foreach(double v in new[]{a.Height,a.PreviousHeight,a.Pitch,a.Roll,a.YawVelocity,a.ShortRemaining})Number(w,v);
            w.Write(a.Short);w.Write(a.Escape);w.Write(a.Departing);w.Write(a.FacingTarget);WorldWire.Write(w,a.Velocity);WorldWire.Write(w,a.YieldGoal);w.Write(a.Yielding);
            w.Write(a.Deferred.Count);foreach(var c in a.Deferred){w.Write(c.Generation);w.Write(c.Sequence);String(w,c.PlayerId);w.Write((int)c.Kind);WorldWire.Write(w,c.Target);w.Write(c.TargetId);w.Write(c.EntityIds[0]);w.Write((int)c.Origin);String(w,c.Source);w.Write(c.JobId);w.Write(c.ActionId);}
        }
        private static AirState ReadAir(BinaryReader r)
        {
            if(!Boolean(r))return null;
            int id=r.ReadInt32();var p=ReadPoint(r);var goal=ReadPoint(r);double heading=Number(r);int team=r.ReadInt32();bool moving=Boolean(r),held=Boolean(r);var outcome=EnumValue<NavigationOutcome>(r);double radius=Number(r),speed=Number(r),turn=Number(r);
            var a=new AirState{Actor=new NavUnit(id,p,radius,speed,turn){Goal=goal,Heading=heading,Team=team,Moving=moving,Held=held,Outcome=outcome}};
            a.Height=Number(r);a.PreviousHeight=Number(r);a.Pitch=Number(r);a.Roll=Number(r);a.YawVelocity=Number(r);a.ShortRemaining=Number(r);a.Short=Boolean(r);a.Escape=Boolean(r);a.Departing=Boolean(r);a.FacingTarget=r.ReadInt32();a.Velocity=ReadPoint(r);a.YieldGoal=ReadPoint(r);a.Yielding=Boolean(r);
            int count=r.ReadInt32();if(count<0||count>4096)throw new ArgumentException("Invalid air queue count.");
            for(int i=0;i<count;i++){long generation=r.ReadInt64(),sequence=r.ReadInt64();string owner=String(r);var kind=EnumValue<PlayableCommandKind>(r);var target=ReadPoint(r);int targetId=r.ReadInt32(),entity=r.ReadInt32();var origin=EnumValue<PlayableOrderOrigin>(r);string source=String(r);long job=r.ReadInt64(),action=r.ReadInt64();a.Deferred.Enqueue(new PlayableCommand(generation,sequence,owner,kind,new[]{entity},target,targetId:targetId,origin:origin,source:source,jobId:job,actionId:action));}
            return a;
        }
    }
}
