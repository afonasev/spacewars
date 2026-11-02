using System;
using System.Threading;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Immutable accepted input. LivePins is a lifetime observation only: it is
    // deliberately cold at decode and is never serialized as paid live work.
    internal sealed class NavigationMotionSource : NavOwnedStorage, INavPhysicalBody
    {
        private static readonly long Bytes=NavStorageLayout.ObjectBytesBound(typeof(NavigationMotionSource));
        internal long Generation,Operation;internal bool IncludesResults;internal NavMotionTranscript Proposal;
        internal int LivePins;
        private NavigationMotionSource():base(Bytes){}
        internal override bool IsImmutableCaptureRoot=>Proposal?.IsImmutableCaptureRoot==true;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.HostEnvelopePublication,value:NavPhysicalValueKind.MotionSourceV1);
        internal static NavigationMotionSource Create(long generation,long operation,bool results,NavMotionTranscript proposal,INavStorageReservation r,bool owner=true)
        {
            if(generation<1||operation<1||proposal==null||proposal.Generation!=generation)throw new ArgumentException("Invalid Native motion input.");
            using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Bytes);var s=a.Register(new NavigationMotionSource());if(owner)s.Retain();s.Generation=generation;s.Operation=operation;s.IncludesResults=results;s.Proposal=NavPhysicalMacro.Acquire(s,proposal);return s;}
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter w){w.Output.Write(Generation);w.Output.Write(Operation);w.Output.Write(IncludesResults);NavPhysicalMacro.WriteEdge(w,this,Proposal);}
        internal static NavigationMotionSource Read(NavPhysicalBodyReader r){long g=r.Input.ReadInt64(),op=r.Input.ReadInt64();bool results=NavPhysicalMacro.ReadBoolean(r);return Create(g,op,results,NavPhysicalMacro.ReadEdge<NavMotionTranscript>(r),r.Reservation,false);}
        protected override void ReleaseEdges(){Proposal?.Release();}
    }
    internal sealed class NavigationMotionRecord : NavOwnedStorage, INavPhysicalBody
    {
        private static readonly long Bytes=NavStorageLayout.ObjectBytesBound(typeof(NavigationMotionRecord));
        internal NavigationMotionSource Source;internal NavMotionTranscript Actual;
        internal bool Committed=>Actual!=null;
        private NavigationMotionRecord():base(Bytes){}
        internal override bool IsImmutableCaptureRoot=>Source?.IsImmutableCaptureRoot==true&&(Actual==null||Actual.IsImmutableCaptureRoot);
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.HostEnvelopePublication,value:NavPhysicalValueKind.MotionRecordV1);
        internal static NavigationMotionRecord Create(NavigationMotionSource source,NavMotionTranscript actual,INavStorageReservation r,bool owner=true)
        {
            if(source==null||actual!=null&&actual.Generation!=source.Generation)throw new ArgumentException("Invalid Native committed source.");
            using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Bytes);var s=a.Register(new NavigationMotionRecord());if(owner)s.Retain();s.Source=NavPhysicalMacro.Acquire(s,source);s.Actual=NavPhysicalMacro.Acquire(s,actual);return s;}
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter w){w.Output.Write(Committed);NavPhysicalMacro.WriteEdge(w,this,Source);if(Committed)NavPhysicalMacro.WriteEdge(w,this,Actual);}
        internal static NavigationMotionRecord Read(NavPhysicalBodyReader r){bool committed=NavPhysicalMacro.ReadBoolean(r);var source=NavPhysicalMacro.ReadEdge<NavigationMotionSource>(r);return Create(source,committed?NavPhysicalMacro.ReadEdge<NavMotionTranscript>(r):null,r.Reservation,false);}
        protected override void ReleaseEdges(){Source?.Release();Actual?.Release();}
    }
    internal sealed class NavigationMotionSources : NavOwnedStorage, INavPhysicalBody
    {
        private static readonly long Bytes=NavStorageLayout.ObjectBytesBound(typeof(NavigationMotionSources));
        internal long Generation,Watermark;internal NavPagedList<NavOwnedStorage> Records;
        private NavigationMotionSources():base(Bytes){}
        private bool ownerReleased;internal override void ReleaseOwner(){if(ownerReleased)return;ownerReleased=true;Release();}
        internal override bool IsImmutableCaptureRoot=>Records?.IsImmutableCaptureRoot==true;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.HostEnvelopePublication,value:NavPhysicalValueKind.MotionSourcesV1);
        internal NavigationMotionRecord Find(long operation){foreach(var item in Records){var row=(NavigationMotionRecord)item;if(row.Source.Operation==operation)return row;}return null;}
        internal static NavigationMotionSources Change(NavigationMotionSources previous,long generation,long watermark,long replace,NavigationMotionRecord row,INavStorageReservation r)
        {
            NavPagedList<NavOwnedStorage> rows=null;
            try{rows=NavPagedList<NavOwnedStorage>.CreateOwned(r,16);if(previous!=null)foreach(var item in previous.Records)if(((NavigationMotionRecord)item).Source.Operation!=replace)rows.Add(item);if(row!=null)rows.Add(row);rows.Seal();return Create(generation,watermark,rows,r,true);}
            finally{rows?.ReleaseStorage();}
        }
        private static NavigationMotionSources Create(long generation,long watermark,NavPagedList<NavOwnedStorage> rows,INavStorageReservation r,bool owner)
        {
            if(generation<1||watermark<1||rows==null||!rows.IsFrozen)throw new ArgumentException("Invalid Native source registry.");
            for(int i=0;i<rows.Count;i++){if(!(rows[i] is NavigationMotionRecord row)||row.Source.Generation!=generation||row.Source.Operation>watermark)throw new ArgumentException("Invalid Native source registry row.");for(int j=0;j<i;j++)if(((NavigationMotionRecord)rows[j]).Source.Operation==row.Source.Operation)throw new ArgumentException("Duplicate Native source operation.");}
            using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Bytes);var s=a.Register(new NavigationMotionSources());if(owner)s.Retain();s.Generation=generation;s.Watermark=watermark;s.Records=NavPhysicalMacro.Acquire(s,rows);return s;}
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter w){w.Output.Write(Generation);w.Output.Write(Watermark);NavPhysicalMacro.WriteEdge(w,this,Records);}
        internal static NavigationMotionSources Read(NavPhysicalBodyReader r){long g=r.Input.ReadInt64(),watermark=r.Input.ReadInt64();return Create(g,watermark,NavPhysicalMacro.ReadEdge<NavPagedList<NavOwnedStorage>>(r),r.Reservation,false);}
        protected override void ReleaseEdges(){Records?.Release();}
    }
    // Same BODY graph as Host/authority: accepted route/provider aliases are
    // shared with the installed owners, never independently decoded copies.
    internal sealed class NavigationMotionEnvelope : NavOwnedStorage, INavPhysicalBody
    {
        private static readonly long Bytes=NavStorageLayout.ObjectBytesBound(typeof(NavigationMotionEnvelope));
        internal NavOwnedStorage Host;internal NavigationMotionSources Sources;
        private NavigationMotionEnvelope():base(Bytes){}
        private bool ownerReleased;internal override void ReleaseOwner(){if(ownerReleased)return;ownerReleased=true;Release();}
        internal override bool IsImmutableCaptureRoot=>Host?.IsImmutableCaptureRoot==true&&Sources?.IsImmutableCaptureRoot==true;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.HostEnvelopePublication,value:NavPhysicalValueKind.MotionEnvelopeV1);
        internal static NavigationMotionEnvelope Create(NavOwnedStorage host,NavigationMotionSources sources,INavStorageReservation r,bool owner=true)
        {
            if(!(host is NavigationHostEnvelope)&&!(host is NavigationAccruedHostEnvelope)||sources==null)throw new ArgumentException("Invalid motion Session envelope.");
            using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Bytes);var s=a.Register(new NavigationMotionEnvelope());if(owner)s.Retain();s.Host=NavPhysicalMacro.Acquire(s,host);s.Sources=NavPhysicalMacro.Acquire(s,sources);return s;}
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter w){NavPhysicalMacro.WriteEdge(w,this,Host);NavPhysicalMacro.WriteEdge(w,this,Sources);}
        internal static NavigationMotionEnvelope Read(NavPhysicalBodyReader r)=>Create(NavPhysicalMacro.ReadEdge<NavOwnedStorage>(r),NavPhysicalMacro.ReadEdge<NavigationMotionSources>(r),r.Reservation,false);
        protected override void ReleaseEdges(){Host?.Release();Sources?.Release();}
    }
    public sealed partial class NavigationSession
    {
        internal const string GroundMotionSourceIdentity="native-ground-motion-v1";
        private NavigationMotionSources motionSources;
        private MotionPublication preparingMotion;
        internal long MotionOperationWatermark=>motionSources?.Watermark??0;
        internal bool AwaitingMotionSources{get{using(EnterTransport()){if(motionSources==null)return false;foreach(var item in motionSources.Records)if(Volatile.Read(ref ((NavigationMotionRecord)item).Source.LivePins)==0)return true;return false;}}}
        internal void AssertMotionOperational(){AssertMotionQuiescent();if(AwaitingMotionSources||motionSources?.Records.Count>0&&motionAuthority==null)throw new InvalidOperationException("Native motion sources await live rebind before clock/commands.");}
        // A paid owning pin is required both hot and cold. An ID or caller bool
        // cannot authenticate a source or clear its preclock obligation.
        internal sealed class MotionSourcePin : NavOwnedStorage, IDisposable
        {
            private static readonly long Bytes=NavStorageLayout.ObjectBytesBound(typeof(MotionSourcePin));
            internal NavigationSession Session;internal NavigationMotionRecord Record;private bool disposed;
            internal long Operation=>Record.Source.Operation;internal NavMotionTranscript Proposal=>Record.Source.Proposal;
            internal NavMotionTranscript CommittedTranscript=>Record.Actual;
            internal bool IsLive=>!disposed;
            private MotionSourcePin():base(Bytes){}
            internal static MotionSourcePin Create(NavigationSession session,NavigationMotionRecord record,INavStorageReservation r)
            {using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Bytes);var pin=a.Register(new MotionSourcePin());pin.Retain();pin.Session=session;pin.Record=NavPhysicalMacro.Acquire(pin,record);Interlocked.Increment(ref record.Source.LivePins);return pin;}}
            public void Dispose(){if(disposed)return;disposed=true;Release();}
            internal override void ReleaseOwner()=>Dispose();
            protected override void ReleaseEdges(){Interlocked.Decrement(ref Record.Source.LivePins);Record.Release();Record=null;Session=null;}
        }
        internal bool TryRetainMotionSource(long operation,bool committedOnly,out MotionSourcePin pin)
        {
            pin=null;NavigationMotionRecord row;
            using(EnterTransport()){AssertMotionQuiescent();row=motionSources?.Find(operation);if(row==null||committedOnly&&!row.Committed)return false;row.Retain();}
            try{if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,0,out var scope))return false;
                using(scope){pin=MotionSourcePin.Create(this,row,scope.OwnedReservation());scope.Attach(pin);try{scope.Commit(pin,_=>{});}catch{pin.Dispose();pin=null;throw;}}return true;
            }finally{row.Release();}
        }
        private MotionSourcePin AcceptMotionSource(MotionPublication publication)
        {
            if(motionPhase!=1||!ReferenceEquals(preparingMotion,publication)||publication.SelectedSource!=null||AwaitingMotionSources)throw new InvalidOperationException("Native source acceptance is outside current preparation.");
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,0,out var scope))throw new InvalidOperationException("Native source retention refused.");
            NavigationMotionSource input=null;NavigationMotionRecord record=null;NavigationMotionSources next=null,old=null;MotionSourcePin pin=null;
            using(scope){try{var r=scope.OwnedReservation();long op=checked(MotionOperationWatermark+1);input=NavigationMotionSource.Create(Generation,op,publication.IncludesResults,publication.Transcript,r);record=NavigationMotionRecord.Create(input,null,r);next=NavigationMotionSources.Change(motionSources,Generation,op,0,record,r);pin=MotionSourcePin.Create(this,record,r);scope.Attach(next);
                scope.Commit(next,_=>{using(EnterTransport()){old=motionSources;next.Retain();motionSources=next;}});publication.SelectSource(pin);return pin;
            }catch{pin?.Dispose();throw;}finally{old?.Release();next?.ReleaseOwner();record?.Release();input?.Release();}}
        }
        internal bool TryRetireMotionSource(MotionSourcePin pin)
        {
            AssertMotionQuiescent();if(Thread.CurrentThread.ManagedThreadId!=motionAuthorityThread||pin==null||!pin.IsLive||!ReferenceEquals(pin.Session,this))throw new ArgumentException("Missing owning thread/live Native source pin.");
            NavigationMotionSources before;
            using(EnterTransport()){
                var row=motionSources?.Find(pin.Operation);if(row==null||!ReferenceEquals(row.Source,pin.Record.Source))throw new InvalidOperationException("Native source was already retired.");
                before=motionSources;before.Retain();
            }
            try{
                if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,0,out var scope))return false;
                using(scope){var next=NavigationMotionSources.Change(before,Generation,before.Watermark,pin.Operation,null,scope.OwnedReservation());bool installed=false;
                    try{scope.Attach(next);using(EnterTransport())scope.Publish(next,_=>{if(!ReferenceEquals(before,motionSources))throw new InvalidOperationException("Source registry changed during retirement.");next.Retain();motionSources=next;installed=true;});scope.ResolveCommitted();return true;}
                    finally{next.ReleaseOwner();if(installed)before.Release();}}
            }finally{before.Release();}
        }
    }
}
