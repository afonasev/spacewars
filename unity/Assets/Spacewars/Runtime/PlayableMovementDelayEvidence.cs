using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Opt-in, bounded, non-authoritative evidence. No per-tick formatting or I/O.
    // Flushed only after the authority tick loop has stopped.
    internal sealed class PlayableMovementDelayEvidence
    {
        internal struct Row
        {
            public long Tick,Timestamp,Order,InstalledRequest,InstalledOrder,Group,Refusals,SourceMismatch;
            public int ResultStage,ResultSourceReason;
            public string RefusalMessage;
            public int Actor,Path,NullReason,NullActor,Refusal,TrafficStage,RouteIndex,RouteLength,LocalIndex,LocalLength,NoProgress;
            public double Dt,StepMs,X,Z,Heading,GoalX,GoalZ,BlockedSeconds,Speed,TurnSpeed;
            public bool Moving,Held,Pending,Computed;
            public int Outcome,ComputePath;
            public double BodyX,BodyZ,BodyHeading,TargetX,TargetZ,BodySpeed,BodyTurn,Cap,ProposalX,ProposalZ,ProposalHeading,VelocityX,VelocityZ;
            public bool Active,Potential,Yielding,Refused,Blocked,Overflow,StaticNoWinner,IndependentTurn;
        }
        private readonly Row[] rows=new Row[4096];
        private readonly string directory;
        private int count,dropped;
        internal int Actor;
        private PlayableMovementDelayEvidence(string directory){this.directory=directory;}
        internal static PlayableMovementDelayEvidence Create()
        {
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"-movementDelayEvidence")<0)return null;
            int at=Array.IndexOf(args,"-playableEvidence");
            return at<0||at+1>=args.Length?null:new PlayableMovementDelayEvidence(args[at+1]);
        }
        internal void Record(NavigationSession nav,long tick,long order,double dt,long begin)
        {
            if(!nav.Crowd.TryGet(Actor,out var unit))return;
            if(count==rows.Length){dropped++;return;}
            var now=Stopwatch.GetTimestamp();var row=nav.MovementDelayComputation;
            row.Tick=tick;row.Timestamp=now;row.Order=order;row.Actor=Actor;row.Dt=dt;row.StepMs=(now-begin)*1000d/Stopwatch.Frequency;
            row.Path=nav.LastNavigationStepPath;row.NullReason=nav.LastOwnedMovementNullReason;row.NullActor=nav.LastOwnedMovementNullActor;
            row.Refusals=nav.OwnedMovementRefusals;row.Refusal=nav.LastOwnedMovementRefusal;row.TrafficStage=nav.LastTrafficDispatchStage;row.SourceMismatch=nav.MovementDelaySourceMismatch;row.RefusalMessage=nav.LastOwnedMovementRefusalMessage;row.ResultStage=nav.LastResultBatchStage;row.ResultSourceReason=nav.LastResultBatchSourceReason;
            row.X=unit.Position.X;row.Z=unit.Position.Z;row.Heading=unit.Heading;row.Moving=unit.Moving;row.Held=unit.Held;
            row.Pending=nav.IsPending(Actor);row.Outcome=(int)unit.Outcome;row.GoalX=unit.Goal.X;row.GoalZ=unit.Goal.Z;
            row.Speed=unit.Speed;row.TurnSpeed=unit.TurnSpeed;row.InstalledRequest=unit.InstalledRequest;row.InstalledOrder=unit.InstalledOrder;row.Group=unit.InstalledGroupId;
            row.RouteIndex=unit.routeIndex;row.RouteLength=unit.route.Length;row.LocalIndex=unit.localIndex;row.LocalLength=unit.localRoute.Length;
            row.NoProgress=unit.noProgressTicks;row.BlockedSeconds=unit.blockedSeconds;rows[count++]=row;
        }
        internal void Flush(long generation)
        {
            if(count==0)return;
            try{
                Directory.CreateDirectory(directory);string path=Path.Combine(directory,"movement-delay-"+generation+".csv");
                var fields=typeof(Row).GetFields(BindingFlags.Public|BindingFlags.Instance);
                using(var writer=new StreamWriter(path)){
                    for(int i=0;i<fields.Length;i++){if(i>0)writer.Write(',');writer.Write(fields[i].Name);}writer.WriteLine();
                    for(int i=0;i<count;i++){object row=rows[i];for(int j=0;j<fields.Length;j++){if(j>0)writer.Write(',');var value=fields[j].GetValue(row);string text=value is double d?d.ToString("R",CultureInfo.InvariantCulture):Convert.ToString(value,CultureInfo.InvariantCulture);writer.Write("\""+text.Replace("\"","\"\"")+"\"");}writer.WriteLine();}
                }
                Console.WriteLine("MOVEMENT_DELAY rows="+count+" dropped="+dropped+" frequency="+Stopwatch.Frequency+" file="+path);
            }catch(Exception error){Console.WriteLine("MOVEMENT_DELAY_WRITE_FAILED "+error.GetType().Name+": "+error.Message);}
            count=0;
        }
    }
    public sealed partial class NavigationSession
    {
        // Separate from the legacy string observer: no diagnostic string allocations.
        internal int MovementDelayActor;
        internal long MovementDelaySourceMismatch;
        internal PlayableMovementDelayEvidence.Row MovementDelayComputation;
        private void CaptureMovementDelaySources(PreparedMovement c,bool compatibleHost)
        {
            long mask=0;
            if(!compatibleHost)mask|=1L<<0;
            if(!ReferenceEquals(c.SourceTraffic,trafficAuthority))mask|=1L<<1;
            if(!ReferenceEquals(groupCommandBacking,c.Backing))mask|=1L<<2;
            if(!ReferenceEquals(admissionOwner,c.SourceAdmission))mask|=1L<<3;
            if(authorityOperationIdentity!=c.Operation)mask|=1L<<4;
            if(admissionPublicationIdentity!=c.Publication)mask|=1L<<5;
            if(requestSequence!=c.Sequence)mask|=1L<<6;
            if(!ReferenceEquals(certifiedHost,c.Worker)||certifiedHostAttachmentIdentity!=c.Attachment)mask|=1L<<7;
            if(!ReferenceEquals(Crowd,c.Frame.SourceCrowd)||!c.Frame.SourceCurrent())mask|=1L<<8;
            if(barrierAnswers.Count!=0||Answers.Count!=0||Answers.Revision!=c.AnswersRevision)mask|=1L<<9;
            if(Requests.Count!=c.RequestsCount||Requests.Revision!=c.RequestsRevision)mask|=1L<<10;
            if(!ReferenceEquals(groups,c.Groups)||c.Groups.Revision!=c.Revisions[0]||!ReferenceEquals(memberGroups,c.Members)||c.Members.Revision!=c.Revisions[1])mask|=1L<<11;
            if(!ReferenceEquals(pending,c.Pending)||c.Pending.Revision!=c.Revisions[2]||!ReferenceEquals(allocating,c.Allocating)||c.Allocating.Revision!=c.Revisions[3])mask|=1L<<12;
            if(!ReferenceEquals(arrivalLedger,c.Ledger)||c.Ledger.Revision!=c.Revisions[4])mask|=1L<<13;
            if(!ReferenceEquals(retainedGoals,c.Goals)||c.Goals.Revision!=c.Revisions[5]||!ReferenceEquals(reservations,c.Reservations)||c.Reservations.Revision!=c.Revisions[6]||!ReferenceEquals(movementIdentities,c.Identities)||c.Identities.Revision!=c.Revisions[7])mask|=1L<<14;
            if((c.Probes==null?probes.Count!=0:!ReferenceEquals(probes,c.Probes)||c.Probes.Revision!=c.Revisions[8])||probes.Count!=c.ProbeCount)mask|=1L<<15;
            if(!ReferenceEquals(liveArrivalTables,c.Arrival)||c.Arrival.Count!=c.Attempts.Length||arrivalRetirementSequence!=c.Retirement)mask|=1L<<16;
            for(int i=0;i<c.Attempts.Length;i++){var f=c.Attempts[i];var a=f.Owner.Record;if(!ReferenceEquals(c.Arrival.Attempts.OwnerFor(c.Arrival.SortedId(i)),f.Owner)||f.Owner.Revision!=f.Revision||a.Working!=f.Working||a.Reconstructing!=f.Reconstructing||a.Retiring!=f.Retiring||a.RestoredRetiringCompute!=f.Restored||a.Retirement!=f.Retirement)mask|=1L<<17;}
            for(int i=0;i<c.GroupSources.Length;i++)if(!c.GroupSources[i].Owner.MatchesObservation(c.GroupSources[i]))mask|=1L<<18;
            if(!ReferenceEquals(liveArrivalLeaseTable,c.LeaseTable)||(c.LeaseTable?.Revision??0)!=c.LeaseRevision||(c.LeaseTable?.Tokens.Revision??0)!=c.LeaseTokenRevision||arrivalProjectionLeases.Count!=c.Leases.Length||(liveArrivalLeaseTable?.Count??0)!=c.Leases.Length)mask|=1L<<19;
            if(installedExecutions.Count!=c.InstalledCount)mask|=1L<<20;
            for(int i=0;i<c.Installed.Length;i++)if(!ReferenceEquals(installedExecutions.OwnerFor(c.Frame.Source[i].Id),c.Installed[i])||c.Installed[i]!=null&&installedExecutions.IsDetached(c.Frame.Source[i].Id)!=c.Detached[i])mask|=1L<<20;
            MovementDelaySourceMismatch|=mask;
        }
        private void CaptureMovementDelay(int path,NavMotionBody[] bodies,NavMotionProposal[] proposals,double[] preferred=null)
        {
            if(MovementDelayActor==0)return;
            for(int i=0;i<bodies.Length;i++)if(bodies[i].Id==MovementDelayActor){
                var b=bodies[i];var p=proposals[i];
                MovementDelayComputation=new PlayableMovementDelayEvidence.Row{
                    Computed=true,ComputePath=path,BodyX=b.Position.X,BodyZ=b.Position.Z,BodyHeading=b.Heading,
                    TargetX=b.Target.X,TargetZ=b.Target.Z,BodySpeed=b.Speed,BodyTurn=b.TurnSpeed,Cap=preferred==null?double.NaN:preferred[i],
                    Active=b.Active,Potential=b.Potential,Yielding=b.Yielding,ProposalX=p.Position.X,ProposalZ=p.Position.Z,ProposalHeading=p.Heading,
                    VelocityX=p.Velocity.X,VelocityZ=p.Velocity.Z,Refused=p.Refuse,Blocked=p.Blocked,Overflow=p.Overflow,StaticNoWinner=p.StaticNoWinner,IndependentTurn=p.IndependentTurn};
                return;
            }
        }
    }
    internal sealed partial class PlayableDomain
    {
        private readonly PlayableMovementDelayEvidence movementDelayEvidence=PlayableMovementDelayEvidence.Create();
        internal void FlushMovementDelayEvidence()=>movementDelayEvidence?.Flush(navigation.Generation);
        private void StepNavigationCore(double dt)
        {
            var trace=movementDelayEvidence;if(trace==null){StepNavigationMeasured(dt);return;}
            if(trace.Actor==0)foreach(var unit in units.Values)if(unit.Owner==PlayableOwner.Player&&unit.Kind==PlayableEntityKind.Explorer){trace.Actor=unit.Id;break;}
            navigation.MovementDelayActor=trace.Actor;navigation.MovementDelayComputation=default;navigation.MovementDelaySourceMismatch=0;long begin=Stopwatch.GetTimestamp();
            try{StepNavigationMeasured(dt);}
            finally{long order=units.TryGetValue(trace.Actor,out var unit)?unit.CurrentOrder?.CommandSequence??0:0;trace.Record(navigation,Tick,order,dt,begin);}
        }
    }
}
