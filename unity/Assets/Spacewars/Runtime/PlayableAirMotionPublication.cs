using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Spacewars.Simulation;
using static Spacewars.Runtime.WorldWire;

namespace Spacewars.Runtime
{
    internal interface IAirMotionAuthority
    {
        NavigationMotionAdoption Prepare(PlayableDomain.AirMotionPublication publication);
    }
    internal sealed partial class PlayableDomain
    {
        internal const string AirMotionSourceIdentity="spacewars/native-air-motion-v1";
        // Expected consumer unavailability before installation. Programming faults
        // and any exception after the first Air write remain fatal.
        internal sealed class AirMotionUnavailableException : InvalidOperationException
        {internal AirMotionUnavailableException(string message):base(message){}}
        internal readonly struct AirMotionActorState
        {
            internal readonly NavMotionActorState Motion;
            internal readonly double Height;
            internal readonly bool Departing;
            internal readonly PlayableTacticalOrderSnapshot CurrentOrder;
            internal readonly PlayableOrderStamp CommandStamp;
            internal AirMotionActorState(NavMotionActorState motion,double height,bool departing,PlayableTacticalOrderSnapshot order,PlayableOrderStamp stamp)
            {Motion=motion;Height=height;Departing=departing;CurrentOrder=order;CommandStamp=stamp;}
        }
        private static AirMotionActorState CaptureAirActor(Unit u)
        {return new AirMotionActorState(new NavMotionActorState(u.Air.Actor),u.Air.Height,u.Air.Departing,u.CurrentOrder?.Copy(),u.LastOrder);}
        // Only immutable scalar views cross the consumer boundary. Calculation
        // copies and queue backing remain private, including on retained sources.
        internal readonly struct AirMotionReceiver
        {
            internal readonly int Entity;
            internal readonly AirMotionActorState BeforeActor,AfterActor;
            internal readonly NavPoint Before,Nominal,Final;
            internal readonly double BeforeHeight,AssignedHeight,NominalClearance;
            internal readonly bool HeightAssigned;
            internal AirMotionReceiver(AirMotionActorState before,AirMotionActorState after,AirTrace trace)
            {Entity=before.Motion.Entity;BeforeActor=before;AfterActor=after;Before=before.Motion.Position;BeforeHeight=before.Height;Nominal=trace.Nominal;Final=trace.Final;AssignedHeight=trace.AssignedHeight;NominalClearance=trace.NominalClearance;HeightAssigned=trace.HeightAssigned;}
        }
        internal sealed class AirTrace
        {
            internal NavPoint Nominal,Final;
            internal double AssignedHeight,NominalClearance;
            internal bool HeightAssigned;
        }
        private readonly struct AirVisibility
        {internal readonly PlayableOwner Owner;internal readonly NavPoint Point;internal AirVisibility(PlayableOwner owner,NavPoint point){Owner=owner;Point=point;}}
        private sealed class AirCalculation
        {
            internal readonly PlayableDomain Domain;
            internal readonly Unit[] Units;
            internal readonly Dictionary<int,Unit> ById;
            internal readonly AirTrace[] Traces;
            internal readonly List<AirVisibility> Observations=new List<AirVisibility>();
            internal long NextRevision;
            internal AirCalculation(PlayableDomain domain)
            {
                Domain=domain;NextRevision=domain.nextOrderRevision;
                Units=domain.units.Values.Where(u=>u.Air!=null).OrderBy(u=>u.Id).Select(CloneAirUnit).ToArray();
                ById=Units.ToDictionary(u=>u.Id);Traces=Units.Select(u=>new AirTrace{Nominal=u.Air.Actor.Position,Final=u.Air.Actor.Position,AssignedHeight=u.Air.Height}).ToArray();
            }
            internal AirTrace Trace(int id)=>Traces[System.Array.FindIndex(Units,u=>u.Id==id)];
            internal bool TryActor(int id,out NavUnit actor)
            {if(ById.TryGetValue(id,out var u)){actor=u.Air.Actor;return true;}return Domain.TryActor(id,out actor);}
            internal bool Target(int id,out NavPoint p,out int hp)
            {if(ById.TryGetValue(id,out var u)){p=u.Air.Actor.Position;hp=(int)Math.Ceiling(u.Health);return true;}return Domain.Target(id,out p,out hp);}
            private bool Visible(PlayableOwner owner,NavPoint point){Observations.Add(new AirVisibility(owner,point));return Domain.AirVisible(owner,point);}
            internal bool VisibleTarget(PlayableOwner observer,int id)
            {
                if(Domain.wrecks.TryGetValue(id,out var wreck)&&wreck.Blocks(Domain.Tick)&&Visible(observer,wreck.Position))return true;
                if(!Target(id,out var pos,out _)||Domain.wrecks.ContainsKey(id))return false;
                var owner=Domain.units.TryGetValue(id,out var u)?u.Owner:Domain.buildings[id].Owner;
                return Domain.Hostile(owner,observer)&&Visible(observer,pos);
            }
        }
        private static Unit CloneAirUnit(Unit source)
        {
            var u=source.CopyForAir();var a=source.Air;var n=a.Actor;
            u.Air=new AirState{Actor=n.CopyMovementHeader(),Height=a.Height,PreviousHeight=a.PreviousHeight,Pitch=a.Pitch,Roll=a.Roll,YawVelocity=a.YawVelocity,ShortRemaining=a.ShortRemaining,Short=a.Short,Escape=a.Escape,Departing=a.Departing,FacingTarget=a.FacingTarget,Velocity=a.Velocity,YieldGoal=a.YieldGoal,Yielding=a.Yielding};
            foreach(var command in a.Deferred)u.Air.Deferred.Enqueue(command);
            if(source.DefaultCombat!=null){var d=source.DefaultCombat;u.DefaultCombat=new DefaultCombatState{Phase=d.Phase,Anchor=d.Anchor,Deadline=d.Deadline,Engaged=d.Engaged,ReturnRequested=d.ReturnRequested};}
            return u;
        }
        private static void WriteAirBytes(BinaryWriter w,byte[] bytes){w.Write(bytes.Length);w.Write(bytes);}
        private static NavigationCaptureBytes ReadAirBytes(BinaryReader r,INavStorageReservation reservation)
        {int length=r.ReadInt32();if(length<0||length>WorldWire.MaxBytes)throw new ArgumentException("Invalid air source byte length.");return NavigationCaptureBytes.Allocate(length,reservation,()=>{var bytes=r.ReadBytes(length);if(bytes.Length!=length)throw new EndOfStreamException();return bytes;});}
        private static void WriteAirDefault(BinaryWriter w,Unit u)
        {var d=u.DefaultCombat;w.Write(d!=null);if(d==null)return;w.Write((int)d.Phase);WorldWire.Write(w,d.Anchor);Number(w,d.Deadline);w.Write(d.Engaged);w.Write(d.ReturnRequested);}
        private static void ReadAirDefault(BinaryReader r,Unit u)
        {if(Boolean(r))u.DefaultCombat=new DefaultCombatState{Phase=EnumValue<DefaultCombatPhase>(r),Anchor=ReadPoint(r),Deadline=Number(r),Engaged=r.ReadInt32(),ReturnRequested=Boolean(r)};}
        // Capture every input read by the discrete law, including ground targets,
        // real solids/building tops and the existing visibility observations.
        private bool AirVisible(PlayableOwner owner,NavPoint point)=>visions.TryGetValue(TeamOf(owner),out var vision)&&vision.IsVisible(point);
        private void WriteAirInputs(BinaryWriter w,double dt,AirVisibility[] observations,INavStorageReservation reservation)
        {
            w.Write(1);w.Write(navigation.Generation);w.Write(Tick);Number(w,dt);w.Write(nextOrderRevision);using(var binding=WorldWire.AirMotionBindingOwned(profile,reservation))WriteAirBytes(w,binding.Bytes);
            w.Write(units.Count);foreach(var u in units.Values.OrderBy(u=>u.Id)){
                WriteRecord(w,u);WriteAirDefault(w,u);w.Write(TryActor(u.Id,out var n));if(n!=null){WorldWire.Write(w,n.Position);WorldWire.Write(w,n.Goal);Number(w,n.Heading);Number(w,n.Radius);Number(w,n.Speed);Number(w,n.TurnSpeed);w.Write(n.Moving);w.Write(n.Held);w.Write((int)n.Outcome);w.Write(n.Team);}}
            w.Write(buildings.Count);foreach(var b in buildings.Values.OrderBy(b=>b.Id)){w.Write(b.Id);w.Write((int)b.Owner);w.Write((int)b.Kind);w.Write((int)b.Phase);WorldWire.Write(w,b.Position);Number(w,b.Health);}
            var solids=SolidObstacles().ToArray();w.Write(solids.Length);foreach(var solid in solids)WorldWire.Write(w,solid);
            w.Write(wrecks.Count);foreach(var wreck in wrecks.Values.OrderBy(v=>v.Id)){w.Write(wreck.Id);WorldWire.Write(w,wreck.Position);w.Write(wreck.Blocks(Tick));}
            w.Write(observations.Length);foreach(var v in observations){w.Write((int)v.Owner);WorldWire.Write(w,v.Point);w.Write(AirVisible(v.Owner,v.Point));}
        }
        private sealed class AirSource : NavOwnedStorage
        {
            internal readonly long Generation,Operation,Tick;
            internal readonly double Dt;
            internal long NextRevision;
            internal Unit[] After;
            internal AirVisibility[] Observations;
            internal AirMotionReceiver[] Rows;
            internal NavigationCaptureBytes Inputs,Output;
            internal int LivePins;
            private NavOwnedStorage[] actorNames;
            private bool released;
            internal override bool IsImmutableCaptureRoot=>true;
            private AirSource(long bytes,long generation,long operation,long tick,double dt):base(bytes)
            {Generation=generation;Operation=operation;Tick=tick;Dt=dt;Retain();}
            internal static AirSource Calculate(PlayableDomain d,long operation,double dt,INavStorageReservation r)
            {
                // Includes clone/queue/trace/dictionary backing and its construction
                // peak. Serialization backing is admitted separately by Write.
                long bytes=checked(NavStorageLayout.ObjectBytesBound(typeof(AirSource))+4096L+4096L*d.units.Count+256L*d.units.Values.Sum(u=>u.Air?.Deferred.Count??0));
                using(var allocation=new NavStorageAllocation(r.TryReserve)){
                    allocation.Reserve(bytes);var s=allocation.Register(new AirSource(bytes,d.navigation.Generation,operation,d.Tick,dt));
                    try{
                        var frame=new AirCalculation(d);var before=frame.Units.Select(CaptureAirActor).ToArray();
                        d.CalculateHawks(frame,dt);s.After=frame.Units;s.NextRevision=frame.NextRevision;s.Observations=frame.Observations.ToArray();
                        if(d.airAuthority!=null){s.Inputs=NavigationCaptureBytes.Write(r,w=>d.WriteAirInputs(w,dt,s.Observations,r));s.RequireEdge(s.Inputs);}
                        for(int i=0;i<frame.Traces.Length;i++){frame.Traces[i].AssignedHeight=frame.Units[i].Air.Height;frame.Traces[i].Final=frame.Units[i].Air.Actor.Position;}
                        s.Rows=before.Select((b,i)=>new AirMotionReceiver(b,CaptureAirActor(frame.Units[i]),frame.Traces[i])).ToArray();s.PinActorNames();
                        if(d.airAuthority!=null){s.Output=NavigationCaptureBytes.Write(r,w=>s.WriteOutput(w));s.RequireEdge(s.Output);}return s;
                    }catch{s.ReleaseOwner();throw;}
                }
            }
            private void WriteOutput(BinaryWriter w)
            {
                w.Write(NextRevision);w.Write(After.Length);
                for(int i=0;i<After.Length;i++){WriteRecord(w,After[i]);WriteAirDefault(w,After[i]);var row=Rows[i];w.Write(row.Entity);WorldWire.Write(w,row.Before);Number(w,row.BeforeHeight);WorldWire.Write(w,row.Nominal);Number(w,row.NominalClearance);w.Write(row.HeightAssigned);Number(w,row.AssignedHeight);WorldWire.Write(w,row.Final);}
            }
            internal static AirSource Read(BinaryReader reader,INavStorageReservation r)
            {
                long generation=reader.ReadInt64(),operation=reader.ReadInt64(),tick=reader.ReadInt64();double dt=Number(reader);
                if(generation<1||operation<1||tick<0||dt<=0)throw new ArgumentException("Invalid air source identity.");
                using var input=ReadAirBytes(reader,r);using var output=ReadAirBytes(reader,r);
                long bytes=checked(NavStorageLayout.ObjectBytesBound(typeof(AirSource))+4096L+16L*(input.Bytes.Length+output.Bytes.Length));
                using(var allocation=new NavStorageAllocation(r.TryReserve)){
                    allocation.Reserve(bytes);var s=allocation.Register(new AirSource(bytes,generation,operation,tick,dt));
                    try{
                        s.Inputs=input;input.Retain();s.RequireEdge(s.Inputs);s.Output=output;output.Retain();s.RequireEdge(s.Output);
                        var beforeActors=s.ReadBeforeActors();
                        using(var stream=new MemoryStream(output.Bytes,false))using(var b=new BinaryReader(stream)){
                            s.NextRevision=b.ReadInt64();int count=b.ReadInt32();if(count<0||count>output.Bytes.Length/128||s.NextRevision<1)throw new ArgumentException("Invalid air source receivers.");s.After=new Unit[count];s.Rows=new AirMotionReceiver[count];
                            int previous=0;for(int i=0;i<count;i++){var u=ReadUnit(b);ReadAirDefault(b,u);int entity=b.ReadInt32();var before=ReadPoint(b);double height=Number(b);var trace=new AirTrace{Nominal=ReadPoint(b),NominalClearance=Number(b),HeightAssigned=Boolean(b),AssignedHeight=Number(b),Final=ReadPoint(b)};if(u.Air==null||entity!=u.Id||entity<=previous||!u.Air.Actor.Position.Equals(trace.Final)||u.Air.Height!=trace.AssignedHeight)throw new ArgumentException("Invalid air source row.");previous=entity;s.After[i]=u;if(!beforeActors.TryGetValue(entity,out var actor)||!actor.Motion.Position.Equals(before)||actor.Height!=height)throw new ArgumentException("Air before actor/source mismatch.");s.Rows[i]=new AirMotionReceiver(actor,CaptureAirActor(u),trace);}
                            if(stream.Position!=stream.Length)throw new ArgumentException("Trailing air source output.");
                        }
                        if(beforeActors.Count!=s.Rows.Length)throw new ArgumentException("Incomplete air actor projection.");s.PinActorNames();return s;
                    }catch{s.ReleaseOwner();throw;}
                }
            }
            // Decode the owning input record, never live Domain state. Opaque
            // input/output byte layouts remain unchanged for existing sources.
            private Dictionary<int,AirMotionActorState> ReadBeforeActors()
            {
                var result=new Dictionary<int,AirMotionActorState>();
                using(var stream=new MemoryStream(Inputs.Bytes,false))using(var b=new BinaryReader(stream)){
                    if(b.ReadInt32()!=1||b.ReadInt64()!=Generation||b.ReadInt64()!=Tick||Number(b)!=Dt||b.ReadInt64()<1)throw new ArgumentException("Invalid air input identity.");
                    int length=b.ReadInt32();if(length<0||length>stream.Length-stream.Position)throw new ArgumentException("Invalid air binding length.");stream.Position+=length;
                    int count=b.ReadInt32();if(count<0||count>Inputs.Bytes.Length/64)throw new ArgumentException("Invalid air input actor count.");int previous=0;
                    for(int i=0;i<count;i++){
                        var u=ReadUnit(b);if(u==null||u.Id<=previous)throw new ArgumentException("Invalid air input unit.");previous=u.Id;ReadAirDefault(b,u);
                        bool hasActor=Boolean(b);if(hasActor){var position=ReadPoint(b);var goal=ReadPoint(b);double heading=Number(b),radius=Number(b),speed=Number(b),turn=Number(b);bool moving=Boolean(b),held=Boolean(b);var outcome=EnumValue<NavigationOutcome>(b);int team=b.ReadInt32();
                            if(u.Air!=null){var a=u.Air.Actor;if(!a.Position.Equals(position)||!a.Goal.Equals(goal)||a.Heading!=heading||a.Radius!=radius||a.Speed!=speed||a.TurnSpeed!=turn||a.Moving!=moving||a.Held!=held||a.Outcome!=outcome||a.Team!=team)throw new ArgumentException("Air duplicate actor values differ.");}}
                        if(u.Air!=null){if(!hasActor||u.Air.Actor.Id!=u.Id)throw new ArgumentException("Missing air input actor.");result.Add(u.Id,CaptureAirActor(u));}
                    }
                }return result;
            }
            private void PinActorNames()
            {
                actorNames=new NavOwnedStorage[checked(Rows.Length*6)];int at=0;
                void Pin(NavLocation? location){var name=location?.NameReference.Owner;if(name!=null)actorNames[at++]=NavPhysicalMacro.Acquire(this,name);}
                foreach(var row in Rows){Pin(row.BeforeActor.Motion.Location);Pin(row.BeforeActor.Motion.Endpoint);Pin(row.BeforeActor.CurrentOrder?.DestinationLocation);Pin(row.AfterActor.Motion.Location);Pin(row.AfterActor.Motion.Endpoint);Pin(row.AfterActor.CurrentOrder?.DestinationLocation);}
            }
            internal void Write(BinaryWriter w){w.Write(Generation);w.Write(Operation);w.Write(Tick);Number(w,Dt);WriteAirBytes(w,Inputs.Bytes);WriteAirBytes(w,Output.Bytes);}
            internal override void ReleaseOwner(){if(released)return;released=true;Release();}
            protected override void ReleaseEdges(){Inputs?.Release();Output?.Release();if(actorNames!=null)foreach(var name in actorNames)name?.Release();actorNames=null;After=null;Rows=null;Observations=null;}
        }
        private sealed class AirRegistry : NavOwnedStorage
        {
            internal AirSource[] Sources;internal bool[] Committed;internal readonly long Watermark;
            private bool released;
            internal override bool IsImmutableCaptureRoot=>true;
            private AirRegistry(long bytes,long watermark):base(bytes){Watermark=watermark;Retain();}
            internal int Find(long op){for(int i=0;i<Sources.Length;i++)if(Sources[i].Operation==op)return i;return -1;}
            internal static AirRegistry Change(AirRegistry before,long watermark,AirSource source,bool committed,bool remove,INavStorageReservation r)
            {
                int old=source==null?-1:before?.Find(source.Operation)??-1;int count=(before?.Sources.Length??0)+(old<0&&!remove&&source!=null?1:0)-(old>=0&&remove?1:0);
                long bytes=checked(NavStorageLayout.ObjectBytesBound(typeof(AirRegistry))+128L+16L*count);
                using(var allocation=new NavStorageAllocation(r.TryReserve)){allocation.Reserve(bytes);var value=allocation.Register(new AirRegistry(bytes,watermark));
                    try{value.Sources=new AirSource[count];value.Committed=new bool[count];int n=0;if(before!=null)for(int i=0;i<before.Sources.Length;i++){if(i==old)continue;value.Sources[n]=NavPhysicalMacro.Acquire(value,before.Sources[i]);value.Committed[n++]=before.Committed[i];}if(source!=null&&!remove){value.Sources[n]=NavPhysicalMacro.Acquire(value,source);value.Committed[n]=committed;}return value;}catch{value.ReleaseOwner();throw;}}
            }
            internal override void ReleaseOwner(){if(released)return;released=true;Release();}
            protected override void ReleaseEdges(){if(Sources!=null)foreach(var s in Sources)s?.Release();Sources=null;Committed=null;}
        }
        internal sealed class AirMotionPin : NavOwnedStorage,IDisposable
        {
            internal NavOwnedStorage Root;internal PlayableDomain Domain;internal bool IsLive=true;
            internal readonly bool Committed;
            internal long Operation=>((AirSource)Root).Operation;
            internal long Generation=>((AirSource)Root).Generation;
            internal long Tick=>((AirSource)Root).Tick;
            internal double DeltaTime=>((AirSource)Root).Dt;
            internal string SourceIdentity=>AirMotionSourceIdentity;
            internal int Count=>((AirSource)Root).Rows.Length;
            internal AirMotionReceiver this[int index]=>((AirSource)Root).Rows[index];
            internal ReadOnlyMemory<byte> InputValues=>((AirSource)Root).Inputs.Bytes;
            internal ReadOnlyMemory<byte> OutputValues=>((AirSource)Root).Output.Bytes;
            private AirMotionPin(bool committed):base(NavStorageLayout.ObjectBytesBound(typeof(AirMotionPin))){Committed=committed;Retain();}
            internal static AirMotionPin Create(PlayableDomain d,NavOwnedStorage root,bool committed,INavStorageReservation r)
            {using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(NavStorageLayout.ObjectBytesBound(typeof(AirMotionPin)));var pin=a.Register(new AirMotionPin(committed));pin.Domain=d;pin.Root=NavPhysicalMacro.Acquire(pin,root);Interlocked.Increment(ref ((AirSource)root).LivePins);return pin;}}
            public void Dispose(){if(!IsLive)return;IsLive=false;Release();}
            internal override void ReleaseOwner()=>Dispose();
            protected override void ReleaseEdges(){Interlocked.Decrement(ref ((AirSource)Root).LivePins);Root.Release();Root=null;Domain=null;}
        }
        internal sealed class AirMotionPublication
        {
            internal readonly PlayableDomain Domain;internal readonly NavOwnedStorage Candidate;internal NavOwnedStorage Selected;
            internal bool Committed{get;private set;}
            internal long Generation=>((AirSource)Candidate).Generation;
            internal long Tick=>((AirSource)Candidate).Tick;
            internal double DeltaTime=>((AirSource)Candidate).Dt;
            internal string SourceIdentity=>AirMotionSourceIdentity;
            internal int Count=>((AirSource)Candidate).Rows.Length;
            internal AirMotionReceiver this[int i]=>((AirSource)Candidate).Rows[i];
            internal ReadOnlyMemory<byte> InputValues=>((AirSource)Candidate).Inputs.Bytes;
            internal ReadOnlyMemory<byte> OutputValues=>((AirSource)Candidate).Output.Bytes;
            internal AirMotionPublication(PlayableDomain d,NavOwnedStorage s){Domain=d;Candidate=s;}
            internal AirMotionPin AcceptSource()=>Domain.AcceptAirSource(this);
            internal bool Matches(AirMotionPin pin)=>pin!=null&&pin.IsLive&&ReferenceEquals(pin.Domain,Domain)&&InputValues.Span.SequenceEqual(pin.InputValues.Span)&&OutputValues.Span.SequenceEqual(pin.OutputValues.Span);
            internal void SelectSource(AirMotionPin pin)
            {
                if(!ReferenceEquals(Domain.preparingAir,this)||Domain.airPhase!=1||Selected!=null||pin==null||!pin.IsLive||!ReferenceEquals(pin.Domain,Domain))throw new InvalidOperationException("Invalid air source selection.");
                int i=Domain.airSources?.Find(pin.Operation)??-1;if(i<0||Domain.airSources.Committed[i]||!ReferenceEquals(Domain.airSources.Sources[i],pin.Root))throw new InvalidOperationException("Air source is stale or consume-only.");
                if(!Matches(pin))throw new InvalidOperationException("Complete air source inputs/receivers changed.");Selected=pin.Root;
            }
            internal void MarkCommittedNoThrow(){Committed=true;}
        }
        private readonly object airGate=new object();
        private AirRegistry airSources;
        private IAirMotionAuthority airAuthority;
        private int airThread,airPhase;
        private AirMotionPublication preparingAir;
        private NavOwnedStorage resumingAir;
        private Exception airAdoptionFailure;
        private NavOwnedStorage failedAirSource;
        private NavigationMotionAdoption failedAirAdoption;
        internal long AirOperationWatermark{get{lock(airGate)return airSources?.Watermark??0;}}
        internal bool AwaitingAirSources{get{lock(airGate)return airSources!=null&&airSources.Sources.Any(s=>Volatile.Read(ref s.LivePins)==0);}}
        private void AssertAirQuiescent(){if(airPhase!=0||airAdoptionFailure!=null)throw new InvalidOperationException("Air publication is active or has unresolved adoption.",airAdoptionFailure);}
        private void AssertAirOperational(){AssertAirQuiescent();if(AwaitingAirSources||airSources?.Sources.Length>0&&airAuthority==null)throw new InvalidOperationException("Air sources await paid live authority rebind before clock/commands.");}
        internal void BindAirMotionAuthority(IAirMotionAuthority authority){lock(airGate){AssertAirQuiescent();if(airAuthority!=null)throw new InvalidOperationException("Air authority already bound.");airAuthority=authority??throw new ArgumentNullException(nameof(authority));airThread=Thread.CurrentThread.ManagedThreadId;}}
        private void AssertAirThread(){if(airAuthority!=null&&Thread.CurrentThread.ManagedThreadId!=airThread)throw new InvalidOperationException("Air publication requires its Domain thread.");}
        private void AdvanceHawks(double dt)
        {
            AssertAirOperational();AssertAirThread();if(!units.Values.Any(u=>u.Air!=null))return;
            // Like ground movement, temporary host resolution/capacity refusal
            // leaves this complete Air step untouched while the world continues.
            if(!navigation.TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,navigation.Generation,0,out var scope))return;
            AirSource candidate=null;AirRegistry next=null,retired=null;NavigationMotionAdoption adoption=null;AirMotionPublication publication=null;
            using(scope){try{
                var r=scope.CapacityReservationOwned();candidate=AirSource.Calculate(this,checked(AirOperationWatermark+1),dt,r);scope.Attach(candidate);
                if(airAuthority!=null){publication=new AirMotionPublication(this,candidate);airPhase=1;preparingAir=publication;
                    try{adoption=airAuthority.Prepare(publication);if(adoption==null)return;if(publication.Selected==null||resumingAir!=null&&!ReferenceEquals(resumingAir,publication.Selected))throw new InvalidOperationException("Air adoption has no exact accepted source.");next=AirRegistry.Change(airSources,airSources.Watermark,(AirSource)publication.Selected,true,false,r);}finally{preparingAir=null;airPhase=0;}}
                Action<object> install=_=>{lock(airGate){airPhase=2;for(int i=0;i<candidate.After.Length;i++)InstallAirUnit(units[candidate.After[i].Id],candidate.After[i]);nextOrderRevision=candidate.NextRevision;
                    if(next!=null){next.Retain();retired=airSources;airSources=next;}publication?.MarkCommittedNoThrow();
                    try{adoption?.ApplyCommittedNoThrow();airPhase=0;}catch(Exception error){airAdoptionFailure=error;candidate.Retain();failedAirSource=candidate;failedAirAdoption=adoption;adoption=null;}}};
                // All fallible work, callback backing and final consumer validation
                // precedes ANY aircraft mutation. Only prepared stores follow.
                if(airAuthority!=null)using(var current=NavigationCaptureBytes.Write(r,w=>WriteAirInputs(w,dt,candidate.Observations,r)))if(!candidate.Inputs.Bytes.SequenceEqual(current.Bytes))throw new InvalidOperationException("Air source inputs changed.");
                airPhase=1;adoption?.ValidateCurrent();scope.Publish(candidate,install);
                scope.ResolveCommitted();if(airAdoptionFailure!=null)throw new InvalidOperationException("Committed air has unresolved consumer adoption.",airAdoptionFailure);
            }catch(AirMotionUnavailableException){if(airPhase==2||airAdoptionFailure!=null)throw;}catch(NavigationRetentionBudget.CapacityRefused){if(airPhase==2)throw;}finally{if(airAdoptionFailure==null)airPhase=0;adoption?.Dispose();retired?.Release();next?.ReleaseOwner();candidate?.ReleaseOwner();}}
        }
        private static void InstallAirUnit(Unit live,Unit after)
        {
            var a=live.Air;var b=after.Air;a.Actor.CopyMovementValuesFrom(b.Actor);a.Height=b.Height;a.PreviousHeight=b.PreviousHeight;a.Pitch=b.Pitch;a.Roll=b.Roll;a.YawVelocity=b.YawVelocity;a.ShortRemaining=b.ShortRemaining;a.Short=b.Short;a.Escape=b.Escape;a.Departing=b.Departing;a.FacingTarget=b.FacingTarget;a.Velocity=b.Velocity;a.YieldGoal=b.YieldGoal;a.Yielding=b.Yielding;if(a.Deferred.Count!=b.Deferred.Count)a.Deferred.Dequeue();
            // The law can only reset default combat and dequeue one command.
            // Keep existing live identities; no prepared mutable backing escapes.
            if(after.DefaultCombat==null)live.DefaultCombat=null;live.LastOrder=after.LastOrder;live.CurrentOrder=after.CurrentOrder;
            live.BurstRemaining=after.BurstRemaining;live.BurstTarget=after.BurstTarget;live.Reload=after.Reload;live.StoppedSeconds=after.StoppedSeconds;live.Target=after.Target;live.ExplicitTarget=after.ExplicitTarget;live.HasAttackMove=after.HasAttackMove;live.AttackMove=after.AttackMove;
        }
        private AirMotionPin AcceptAirSource(AirMotionPublication publication)
        {
            if(airPhase!=1||!ReferenceEquals(preparingAir,publication)||publication.Selected!=null||AwaitingAirSources)throw new InvalidOperationException("Air acceptance outside current preparation.");
            if(!navigation.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,navigation.Generation,0,out var scope))throw new AirMotionUnavailableException("Air source retention refused.");
            using(scope){var source=(AirSource)publication.Candidate;var next=AirRegistry.Change(airSources,source.Operation,source,false,false,scope.OwnedReservation());AirMotionPin pin=null;AirRegistry old=null;
                try{pin=AirMotionPin.Create(this,source,false,scope.OwnedReservation());scope.Attach(next);scope.Commit(next,_=>{lock(airGate){old=airSources;next.Retain();airSources=next;}});publication.SelectSource(pin);return pin;}catch{pin?.Dispose();throw;}finally{old?.Release();next.ReleaseOwner();}}
        }
        internal bool TryRetainAirMotionSource(long operation,bool committedOnly,out AirMotionPin pin)
        {
            pin=null;lock(airGate){AssertAirQuiescent();int i=airSources?.Find(operation)??-1;if(i<0||committedOnly&&!airSources.Committed[i])return false;
                if(!navigation.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,navigation.Generation,0,out var scope))return false;
                using(scope){pin=AirMotionPin.Create(this,airSources.Sources[i],airSources.Committed[i],scope.OwnedReservation());scope.Attach(pin);try{scope.Commit(pin,_=>{});return true;}catch{pin.Dispose();pin=null;throw;}}}
        }
        internal bool TryRetireAirMotionSource(AirMotionPin pin)
        {
            AssertAirThread();lock(airGate){AssertAirQuiescent();if(pin==null||!pin.IsLive||!ReferenceEquals(pin.Domain,this))throw new ArgumentException("Missing paid live air source pin.");int i=airSources?.Find(pin.Operation)??-1;if(i<0||!ReferenceEquals(airSources.Sources[i],pin.Root))throw new InvalidOperationException("Air source already retired.");
                if(!navigation.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,navigation.Generation,0,out var scope))return false;
                using(scope){var next=AirRegistry.Change(airSources,airSources.Watermark,(AirSource)pin.Root,false,true,scope.OwnedReservation());var old=airSources;try{scope.Attach(next);scope.Commit(next,_=>{next.Retain();airSources=next;});old.Release();return true;}finally{next.ReleaseOwner();}}}
        }
        // Re-enters the actual discrete calculation at the SAME Domain point;
        // the consumer still selects/matches the pending source during Prepare.
        internal void ResumeAirMotion(AirMotionPin pin)
        {
            AssertAirQuiescent();AssertAirThread();if(pin==null||!pin.IsLive||!ReferenceEquals(pin.Domain,this)||pin.Committed)throw new ArgumentException("Missing pending air source.");
            lock(airGate){int i=airSources?.Find(pin.Operation)??-1;if(i<0||airSources.Committed[i]||!ReferenceEquals(airSources.Sources[i],pin.Root))throw new InvalidOperationException("Air resume source is stale or consume-only.");}
            resumingAir=pin.Root;try{AdvanceHawks(((AirSource)pin.Root).Dt);}finally{resumingAir=null;}
        }
        private void WriteAirMotionSources(BinaryWriter w)
        {
            lock(airGate){AssertAirQuiescent();if(airSources==null)return;w.Write(0x41495231);w.Write(airSources.Watermark);w.Write(airSources.Sources.Length);for(int i=0;i<airSources.Sources.Length;i++){w.Write(airSources.Committed[i]);airSources.Sources[i].Write(w);}}
        }
        private void ReadAirMotionSources(BinaryReader r,NavigationDecodeTransaction transaction)
        {
            if(r.BaseStream.Position==r.BaseStream.Length)return;
            if(r.ReadInt32()!=0x41495231)throw new ArgumentException("Invalid air source extension.");long watermark=r.ReadInt64();int count=r.ReadInt32();if(watermark<1||count<0||count>100000)throw new ArgumentException("Invalid air source registry.");
            AirRegistry registry=null;
            try{registry=AirRegistry.Change(null,watermark,null,false,false,transaction.Reservation);for(int i=0;i<count;i++){bool committed=Boolean(r);var source=AirSource.Read(r,transaction.Reservation);try{if(source.Generation!=navigation.Generation||source.Operation>watermark||source.Tick>Tick||registry.Find(source.Operation)>=0)throw new ArgumentException("Invalid saved air source binding.");var next=AirRegistry.Change(registry,watermark,source,committed,false,transaction.Reservation);registry.ReleaseOwner();registry=next;}finally{source.ReleaseOwner();}}airSources=registry;registry=null;}finally{registry?.ReleaseOwner();}
        }
        private void ReleaseAirMotionSources()
        {lock(airGate){airSources?.Release();airSources=null;failedAirSource?.Release();failedAirSource=null;failedAirAdoption?.Dispose();failedAirAdoption=null;}}
    }
}
