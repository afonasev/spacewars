using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Separate Native phase and source. The ground and Air receipts never form
    // a synthetic global transaction, even when one owner controls both kinds.
    internal sealed class AiCombatMicroAirMotionAuthority : IAirMotionAuthority
    {
        readonly PlayableDomain domain;
        readonly Func<IReadOnlyList<PlayableAiOwnerLoop>> owners;
        readonly AiCombatMicroExecutionStorage storage=new AiCombatMicroExecutionStorage();
        readonly Dictionary<AiCombatMicroExecutionOwner,Issuer> issuers=new Dictionary<AiCombatMicroExecutionOwner,Issuer>();
        readonly Dictionary<AiCombatMicroExecutionOwner,AiCombatMicroExecutionOwner.Source> sources=new Dictionary<AiCombatMicroExecutionOwner,AiCombatMicroExecutionOwner.Source>();
        readonly List<Adoption> cleanup=new List<Adoption>();Adoption pending;
        internal long Publications{get;private set;}
        internal long AffectedPublications{get;private set;}
        internal long DeparturePublications{get;private set;}
        internal string LastRefusal{get;private set;}
        internal bool HasPending=>pending!=null;
        [NonSerialized] internal Func<int,int> WorkGrantForTests;
        internal AiCombatMicroAirMotionAuthority(PlayableDomain domain,Func<IReadOnlyList<PlayableAiOwnerLoop>> owners){this.domain=domain;this.owners=owners;}
        sealed class Refused:InvalidOperationException{internal Refused(string message):base(message){}}
        readonly struct View
        {
            readonly PlayableDomain.AirMotionPublication publication;readonly PlayableDomain.AirMotionPin pin;
            internal View(PlayableDomain.AirMotionPublication value){publication=value;pin=null;}
            internal View(PlayableDomain.AirMotionPin value){pin=value;publication=null;}
            internal int Count=>publication?.Count??pin.Count;
            internal long Generation=>publication?.Generation??pin.Generation;
            internal double DeltaTime=>publication?.DeltaTime??pin.DeltaTime;
            internal PlayableDomain.AirMotionReceiver this[int index]=>publication!=null?publication[index]:pin[index];
        }
        static bool Same(PlayableDomain.AirMotionPin a,PlayableDomain.AirMotionPin b)=>a.Generation==b.Generation&&a.Operation==b.Operation&&a.InputValues.Span.SequenceEqual(b.InputValues.Span)&&a.OutputValues.Span.SequenceEqual(b.OutputValues.Span);
        sealed class PinLease:IDisposable
        {
            PlayableDomain.AirMotionPin pin;
            internal PinLease(PlayableDomain.AirMotionPin pin){this.pin=pin;pin.Retain();}
            public void Dispose(){var old=pin;pin=null;old?.Release();}
        }
        sealed class Input:IAiCombatMicroNativeInput
        {
            internal readonly PlayableDomain.AirMotionPin Pin;
            public IAiCombatMicroNativeExecutionSource NativeOwner{get;}
            public long OperationId=>Pin.Operation;
            public string SourceIdentity=>PlayableDomain.AirMotionSourceIdentity;
            public MotionExecutionProposal Proposal{get;}
            internal Input(Issuer issuer,PlayableDomain.AirMotionPin pin,MotionExecutionProposal proposal){NativeOwner=issuer;Pin=pin;Proposal=proposal;}
            public bool TryRetain(out IDisposable retention){retention=null;if(!Pin.IsLive)return false;retention=new PinLease(Pin);return true;}
        }
        sealed class Issuer:IAiCombatMicroNativeExecutionSource
        {
            readonly PlayableDomain domain;
            internal readonly Dictionary<long,Input> Accepted=new Dictionary<long,Input>();
            internal Issuer(PlayableDomain domain){this.domain=domain;}
            public bool TryRetainCommitted(long operation,out IAiCombatMicroNativeCommittedRecord record,out IDisposable retention)
            {
                record=null;retention=null;if(!Accepted.TryGetValue(operation,out var input)||!domain.TryRetainAirMotionSource(operation,true,out var pin))return false;
                if(!pin.Committed||!Same(input.Pin,pin)){pin.Dispose();throw new InvalidOperationException("Committed Air source differs from the accepted complete calculation.");}
                record=new Record(this,operation,input.Proposal);retention=pin;return true;
            }
            sealed class Record:IAiCombatMicroNativeCommittedRecord
            {
                public IAiCombatMicroNativeExecutionSource NativeOwner{get;}
                public long OperationId{get;}
                public MotionExecutionProposal Actual{get;}
                internal Record(Issuer issuer,long operation,MotionExecutionProposal actual){NativeOwner=issuer;OperationId=operation;Actual=actual;}
            }
        }
        sealed class Row
        {
            internal PlayableAiOwnerLoop Owner;internal AiCombatMicroExecutionOwner Registry;internal Issuer Issuer;
            internal AiCombatMicroExecutionOwner.Input Input,Actual;internal AiCombatMicroExecutionOwner.Submission Submission;
            internal AiCombatMicroExecutionOwner.PreparedInstall Install;
        }
        sealed class Adoption:NavigationMotionAdoption
        {
            internal readonly AiCombatMicroAirMotionAuthority Authority;
            internal PlayableDomain.AirMotionPublication Publication;internal PlayableDomain.AirMotionPin Pin;
            internal readonly List<Row> Rows=new List<Row>();internal readonly List<IDisposable> Resources=new List<IDisposable>();
            internal AiCombatMicroExecutionStorage.Lease Memory;internal bool KeepPending;internal long Tick;
            bool installed,released,disposed;internal bool Departure;
            internal Adoption(AiCombatMicroAirMotionAuthority authority){Authority=authority;Tick=authority.domain.Tick;}
            internal override void ValidateCurrent()
            {
                if(Publication==null||Pin==null||!Pin.IsLive||!ReferenceEquals(Pin.Domain,Authority.domain))throw new InvalidOperationException("Missing live Air publication custody.");
                if(!Publication.Matches(Pin))throw new PlayableDomain.AirMotionUnavailableException("Complete Air input/output changed before publication.");
                foreach(var owner in Authority.owners()){
                    if(!Authority.domain.Authorizes(owner.OwnerId))continue;var affected=Authority.Affected(owner,new View(Publication));var row=Rows.FirstOrDefault(r=>ReferenceEquals(r.Registry,owner.ExecutionOwner));
                    if(affected.Count!=(row?.Input.Proposal.Count??0))throw new PlayableDomain.AirMotionUnavailableException("Current whole Air classification changed.");
                    for(int i=0;i<affected.Count;i++)if(row.Input.Proposal.Primitive(i).Binding.Entity!=Publication[affected[i]].Entity)throw new PlayableDomain.AirMotionUnavailableException("Current Air receiver routing changed.");
                }
                long grant=long.MaxValue;foreach(var row in Rows)if(!row.Install.TryValidateCurrent(ref grant))throw new PlayableDomain.AirMotionUnavailableException("Current Air policy/guard admission changed before publication.");
            }
            internal override void ApplyCommittedNoThrow()
            {
                foreach(var row in Rows){row.Install.EnterNoThrow();row.Install.ApplyNoThrow();}
                installed=true;KeepPending=false;Authority.pending=null;Authority.Publications++;if(Rows.Count>0)Authority.AffectedPublications++;if(Departure)Authority.DeparturePublications++;
            }
            public override void Dispose()
            {
                Publication=null;if(KeepPending)return;if(!Authority.cleanup.Contains(this))Authority.cleanup.Add(this);if(TryCleanup())Authority.cleanup.Remove(this);
            }
            internal bool TryCleanup()
            {
                if(disposed)return true;
                if(!released){foreach(var row in Rows){row.Install?.Dispose();row.Install=null;if(!installed){if(row.Submission!=null)row.Registry.Cancel(row.Submission);if(row.Actual?.Live==true)row.Registry.CancelInput(row.Actual);if(row.Input?.Live==true)row.Registry.CancelInput(row.Input);}if(Pin!=null)row.Issuer?.Accepted.Remove(Pin.Operation);}foreach(var r in Resources)r.Dispose();Resources.Clear();Memory?.Dispose();Memory=null;released=true;}
                if(Pin!=null){if(!Authority.domain.TryRetireAirMotionSource(Pin))return false;Pin.Dispose();Pin=null;}disposed=true;return true;
            }
        }
        static bool Point(NavPoint a,NavPoint b)=>MotionExecutionBinding.Same(a.X,b.X)&&MotionExecutionBinding.Same(a.Z,b.Z);
        List<int> Affected(PlayableAiOwnerLoop owner,View view)
        {
            var result=new List<int>();
            for(int i=0;i<view.Count;i++){
                var r=view[i];var actor=domain.CombatExecutionActor(r.Entity);
                if(actor==null||actor.Kind!=PlayableEntityKind.Hawk||actor.Owner!=domain.OwnerFor(owner.OwnerId)||actor.Health<=0||actor.Held||actor.Departing||actor.OrderStamp?.Origin==PlayableOrderOrigin.Human||!owner.ReservesExecutionSubject(actor.Id)||!owner.RetainsCurrentExecutionStamp(domain,actor.Id))continue;
                if(Point(r.Before,r.Final)&&MotionExecutionBinding.Same(r.BeforeHeight,r.AssignedHeight)&&MotionExecutionBinding.Same(r.BeforeActor.Motion.Heading,r.AfterActor.Motion.Heading))continue;
                result.Add(i);
            }
            return result;
        }
        AiCombatMicroExecutionOwner.Source Source(Row row,ref long grant)
        {
            if(!issuers.TryGetValue(row.Registry,out var issuer)){issuer=new Issuer(domain);issuers.Add(row.Registry,issuer);}row.Issuer=issuer;
            if(!sources.TryGetValue(row.Registry,out var source)){source=row.Registry.RegisterSource(issuer,PlayableDomain.AirMotionSourceIdentity,ref grant)??throw new Refused("Air source admission refused.");sources.Add(row.Registry,source);}return source;
        }
        public NavigationMotionAdoption Prepare(PlayableDomain.AirMotionPublication publication)
        {
            if(pending!=null&&!publication.Matches(pending.Pin)){pending.KeepPending=false;cleanup.Add(pending);pending=null;LastRefusal="Accepted Air input became stale; retire outside publication before a new submission.";return null;}
            var adoption=pending??new Adoption(this);adoption.Publication=publication;
            try{
                if(adoption.Pin==null){
                    adoption.Pin=publication.AcceptSource();if(!storage.TryReserve(checked(8192L+4096L*publication.Count),out adoption.Memory))throw new Refused("Whole Air receiver admission refused.");
                    for(int i=0;i<publication.Count;i++)adoption.Departure|=!publication[i].HeightAssigned&&publication[i].BeforeActor.Departing;
                    foreach(var owner in owners()){
                        if(!domain.Authorizes(owner.OwnerId))continue;var affected=Affected(owner,new View(publication));if(affected.Count==0)continue;
                        var row=new Row{Owner=owner,Registry=owner.ExecutionOwner};adoption.Rows.Add(row);long grant=long.MaxValue;var source=Source(row,ref grant);
                        var proposal=BuildProposal(owner,row.Registry,new View(adoption.Pin),adoption.Pin.Operation,affected,adoption.Tick,adoption.Resources);
                        var input=new Input(row.Issuer,adoption.Pin,proposal);row.Issuer.Accepted.Add(adoption.Pin.Operation,input);
                        if(!row.Registry.TryRetainInput(source,input,ref grant,out row.Input)||!row.Registry.TrySubmit(row.Input,ref grant,out row.Submission))throw new Refused("Air micro proposal admission refused.");
                    }
                }else publication.SelectSource(adoption.Pin);
                int remaining=WorkGrantForTests?.Invoke(4096)??4096;if(remaining<0||remaining>4096)throw new InvalidOperationException("Invalid Air work grant.");bool ready=true;
                foreach(var row in adoption.Rows){
                    long grant=long.MaxValue;var registry=row.Registry;var state=registry.Status(row.Submission);
                    if(state==AiCombatMicroExecutionStatus.Pending&&remaining>0){remaining-=registry.Advance(row.Submission,remaining,ref grant);state=registry.Status(row.Submission);}
                    if(state==AiCombatMicroExecutionStatus.Pending){ready=false;continue;}
                    if(!registry.TryIssue(row.Submission,ref grant,out var issued))throw new Refused("Air path denied or current authority changed.");
                    if(state==AiCombatMicroExecutionStatus.Allowed){
                        var affected=Affected(row.Owner,new View(publication));var actual=BuildProposal(row.Owner,registry,new View(publication),adoption.Pin.Operation,affected,adoption.Tick,adoption.Resources);var source=Source(row,ref grant);
                        if(!registry.TryRetainInput(source,new Input(row.Issuer,adoption.Pin,actual),ref grant,out row.Actual)||!registry.TryPrepareActual(issued,row.Actual,ref grant))throw new Refused("Actual Air matching admission refused.");state=registry.Status(row.Submission);
                    }
                    if(state==AiCombatMicroExecutionStatus.Matching&&remaining>0){remaining-=registry.Advance(row.Submission,remaining,ref grant);state=registry.Status(row.Submission);}
                    if(state==AiCombatMicroExecutionStatus.Matching){ready=false;continue;}
                    if(state!=AiCombatMicroExecutionStatus.Ready)throw new Refused("Actual complete Air values differ.");
                    if(row.Install==null)row.Install=registry.PrepareInstall(issued,ref grant)??throw new Refused("Air factual install reservation refused.");
                    if(!row.Install.TryValidateCurrent(ref grant))throw new Refused("Current Air policy/guard admission unavailable.");
                }
                if(!ready){adoption.KeepPending=true;pending=adoption;LastRefusal="Paid Air policy work remains pending.";adoption.Publication=null;return null;}
                pending=null;adoption.KeepPending=false;LastRefusal=null;return adoption;
            }catch(PlayableDomain.AirMotionUnavailableException error){LastRefusal=error.Message;adoption.KeepPending=false;pending=null;adoption.Publication=null;cleanup.Add(adoption);return null;}
            catch(Refused error){LastRefusal=error.Message;adoption.KeepPending=false;pending=null;adoption.Publication=null;cleanup.Add(adoption);return null;}
            catch{adoption.KeepPending=false;pending=null;adoption.Publication=null;cleanup.Add(adoption);throw;}
        }
        internal void ServiceSources()
        {
            Drain();foreach(var owner in owners()){
                if(!domain.Authorizes(owner.OwnerId))continue;owner.BindExecutionDomain(domain);var registry=owner.ExecutionOwner;
                if(pending!=null)foreach(var row in pending.Rows)if(ReferenceEquals(row.Registry,registry))row.Owner=owner;
                while(registry.TryGetColdBinding(out var submission,out long operation,out long tick,out var state,PlayableDomain.AirMotionSourceIdentity)){
                    if(pending!=null&&pending.Pin.Operation!=operation)throw new InvalidOperationException("Current Air aggregate has distinct operations.");
                    if(pending==null){if(!domain.TryRetainAirMotionSource(operation,false,out var pin))throw new InvalidOperationException("Current Air source awaits owning rebind.");pending=new Adoption(this){Pin=pin,Tick=tick,KeepPending=true};if(!storage.TryReserve(checked(8192L+4096L*pin.Count),out pending.Memory))throw new InvalidOperationException("Current Air liability admission refused.");}
                    var row=new Row{Owner=owner,Registry=registry,Submission=submission};pending.Rows.Add(row);long grant=long.MaxValue;var source=Source(row,ref grant);
                    if(!storage.TryBegin(checked(8*pending.Pin.Count+8),out var values))throw new InvalidOperationException("Saved Air factory admission refused.");
                    MotionExecutionProposal proposal;
                    using(values){
                        proposal=registry.ReadAcceptedSnapshot(submission,ref grant,values,out var profile);var indices=new List<int>();
                        for(int i=0;i<proposal.Count;i++){int found=-1;for(int j=0;j<pending.Pin.Count;j++)if(pending.Pin[j].Entity==proposal.Primitive(i).Binding.Entity){found=j;break;}if(found<0)throw new ArgumentException("Saved Air receiver absent from owning source.");indices.Add(found);}
                        var roots=new List<IDisposable>();try{registry.CheckAcceptedSnapshot(submission,BuildProposal(owner,registry,new View(pending.Pin),operation,indices,tick,roots,proposal,profile));}finally{foreach(var root in roots)root.Dispose();}
                        pending.Resources.Add(values.Transfer());
                    }
                    var input=new Input(row.Issuer,pending.Pin,proposal);row.Issuer.Accepted.Add(operation,input);
                    if(!registry.TryRetainInput(source,input,ref grant,out row.Input))throw new InvalidOperationException("Current paid Air input rebind refused.");
                    if(state==AiCombatMicroExecutionStatus.Matching||state==AiCombatMicroExecutionStatus.Ready||state==AiCombatMicroExecutionStatus.Executing)if(!registry.TryRetainInput(source,new Input(row.Issuer,pending.Pin,proposal),ref grant,out row.Actual))throw new InvalidOperationException("Current actual Air input rebind refused.");
                    if(!registry.TryRebind(submission,row.Input,row.Actual,ref grant))throw new InvalidOperationException("Current Air Runtime liability rebind refused.");
                    if(state==AiCombatMicroExecutionStatus.Executing&&(!pending.Pin.Committed||!registry.TryResumeCommitted(source,submission,out var ticket)||!registry.TryConsumeRestoredCommitted(source,ticket,ref grant,ref grant)))throw new InvalidOperationException("Committed Air fact awaits consume-only adoption.");
                }
            }
            if(pending?.Pin.Committed==true){pending.KeepPending=false;cleanup.Add(pending);pending=null;Drain();}
            if(domain.AwaitingAirSources)throw new InvalidOperationException("Native Air paid sources remain unbound before clock.");
        }
        void Drain(){for(int i=cleanup.Count-1;i>=0;i--)if(cleanup[i].TryCleanup())cleanup.RemoveAt(i);}
        internal void BeforeCapture(){Drain();if(cleanup.Count!=0)throw new InvalidOperationException("Air retirement remains pending before capture.");}
        internal void Stop(){if(pending!=null){pending.KeepPending=false;cleanup.Add(pending);pending=null;}Drain();if(cleanup.Count!=0)throw new InvalidOperationException("Air release remains pending at stop.");}
        MotionExecutionProposal BuildProposal(PlayableAiOwnerLoop owner,AiCombatMicroExecutionOwner registry,View view,long operation,List<int> affected,long tick,List<IDisposable> resources,MotionExecutionProposal accepted=null,PlayableProfile acceptedProfile=null)
        {
            if(!storage.TryBegin(checked(3*affected.Count+1),out var allocation))throw new Refused("Air immutable value factory refused.");
            using(allocation){
                if(!allocation.TryReserve(checked(8192L+4096L*affected.Count)))throw new Refused("Air immutable scratch refused.");long grant=long.MaxValue;var profile=acceptedProfile??owner.ExecutionProfile;var values=new MotionExecutionPrimitive[affected.Count];
                for(int j=0;j<affected.Count;j++){
                    var r=view[affected[j]];var before=r.BeforeActor;var after=r.AfterActor;var stamp=before.CommandStamp;
                    if(stamp==null||stamp.Origin!=PlayableOrderOrigin.Ai||before.Departing)throw new Refused("Affected Air source has no current own micro command.");
                    AiCombatMicroPolicy.ExecutionDescriptor descriptor=null;if(accepted==null&&!registry.TryDescribe(r.Entity,ref grant,out descriptor))throw new Refused("Current Air micro descriptor unavailable.");
                    using(descriptor){
                        string route="native-air-command:"+stamp.Revision;
                        var binding=accepted?.Primitive(j).Binding??new MotionExecutionBinding(view.Generation,0,0,0,stamp.Sequence,r.Entity,before.Motion.Incarnation,before.Motion.Mobility,0,stamp.Revision,after.CommandStamp?.Revision??0,owner.OwnerId,route,profile.ProfileId,"geometry:"+domain.Geometry.Revision,null,descriptor.Identity,MotionOwnIntentKind.Air);
                        if(binding.OwnIntent!=MotionOwnIntentKind.Air||binding.World!=view.Generation||binding.Owner!=owner.OwnerId||binding.Entity!=r.Entity||binding.Incarnation!=before.Motion.Incarnation||binding.Mobility!=before.Motion.Mobility||binding.Command!=stamp.Sequence||binding.Order!=stamp.Revision||binding.Activation!=(after.CommandStamp?.Revision??0)||binding.Route!=route||binding.Profile!=profile.ProfileId)throw new ArgumentException("Saved Air identity differs from its owning source.");
                        var from=new NavLocation(r.Before,"native-air");var to=new NavLocation(r.Final,"native-air");var goal=new NavLocation(after.Motion.Goal,"native-air");
                        var law=new MotionExecutionAirStep(r.BeforeHeight,r.AssignedHeight,r.Nominal,r.NominalClearance,r.HeightAssigned,r.Final);
                        var trace=new MotionExecutionTracePiece(MotionExecutionTraceKind.Air,MotionExecutionLaw.NativeAssignedAltitude,from,to,null,0,0,0,0,1);
                        if(!MotionExecutionPrimitive.TryCreate(binding,affected[j]+1,MotionExecutionKind.AirDiscrete,tick,view.DeltaTime,from,to,goal,before.Motion.Radius,"hawk",before.Motion.Heading,after.Motion.Heading,0,0,0,new[]{trace},ref grant,allocation.TryReserve,out values[j],air:law))throw new Refused("Air primitive admission refused.");
                    }
                }
                if(!MotionExecutionProposal.TryCreate(view.Generation,0,0,operation,tick,values,ref grant,allocation.TryReserve,out var result))throw new Refused("Air owner bundle admission refused.");resources.Add(allocation.Transfer());return result;
            }
        }
    }
}
