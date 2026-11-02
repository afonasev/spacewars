using System;
using System.Threading;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Runtime implements this boundary using its current receiver ownership.
    // Prepare must complete the full value/path match and reserve all final
    // cursor/receipt/retirement storage. There is no caller allow/commit flag.
    internal interface INavigationMotionAuthority
    {
        NavigationMotionAdoption Prepare(NavigationSession.MotionPublication publication);
    }
    internal abstract class NavigationMotionAdoption : IDisposable
    {
        // Last fallible current-policy/owner check, before the first Nav write.
        internal abstract void ValidateCurrent();
        // Only preadmitted factual cursor installs; no allocation, policy
        // callbacks, matching or external releases are permitted here.
        internal abstract void ApplyCommittedNoThrow();
        public abstract void Dispose();
    }
    public sealed partial class NavigationSession
    {
        internal sealed class MotionPublication : NavOwnedStorage
        {
            private static readonly long Bytes=NavStorageLayout.ObjectBytesBound(typeof(MotionPublication));
            private NavOwnedStorage carrier;
            private NavigationSession session;
            internal bool SourcesDestination;
            internal NavigationMotionSource SelectedSource;internal NavigationMotionSources SourceBefore,SourceAfter,RetiredSources;
            internal MotionSourcePin AcceptSource()=>session.AcceptMotionSource(this);
            internal void SelectSource(MotionSourcePin pin)
            {
                if(session.motionPhase!=1||!ReferenceEquals(session.preparingMotion,this)||pin==null||!pin.IsLive||!ReferenceEquals(pin.Session,session)||SelectedSource!=null)throw new InvalidOperationException("Invalid Native source selection.");
                var row=session.motionSources?.Find(pin.Operation);if(row==null||row.Committed||!ReferenceEquals(row.Source,pin.Record.Source)||row.Source.IncludesResults!=IncludesResults)throw new InvalidOperationException("Native source is stale or consume-only.");
                SelectedSource=NavPhysicalMacro.Acquire(this,row.Source);
            }
            internal NavMotionTranscript Transcript{get;private set;}
            internal readonly bool IncludesResults;
            internal bool Committed{get;private set;}
            internal bool Aborted{get;private set;}
            private MotionPublication(bool results):base(Bytes)
            {IncludesResults=results;}
            internal static MotionPublication Create(NavigationSession session,NavOwnedStorage carrier,NavPreparedCrowdStep frame,long generation,long operation,bool results,INavStorageReservation r)
            {
                var transcript=NavMotionTranscript.Create(frame,generation,operation,r);
                try{using(var a=new NavStorageAllocation(r.TryReserve)){
                    a.Reserve(Bytes);var result=a.Register(new MotionPublication(results));result.Retain();
                    try{result.session=session;result.carrier=NavPhysicalMacro.Acquire(result,carrier);result.Transcript=NavPhysicalMacro.Acquire(result,transcript);return result;}catch{result.Release();throw;}
                }}finally{transcript.Release();}
            }
            internal void MarkCommittedNoThrow(){Committed=true;}
            internal void MarkAbortedNoThrow(){Aborted=true;}
            protected override void ReleaseEdges(){carrier?.Release();Transcript?.Release();SelectedSource?.Release();SourceBefore?.Release();if(SourcesDestination)SourceAfter?.Release();SourceAfter?.ReleaseOwner();RetiredSources?.Release();}
        }
        private INavigationMotionAuthority motionAuthority;
        private int motionAuthorityThread,motionPhase;
        private MotionPublication unresolvedMotionPublication;
        private NavigationMotionAdoption unresolvedMotionAdoption;
        private Exception motionPublicationFailure;
        internal void BindMotionAuthority(INavigationMotionAuthority authority)
        {
            AssertMotionQuiescent();if(motionAuthority!=null)throw new InvalidOperationException("Motion authority is already bound.");
            if(authority==null)throw new ArgumentNullException(nameof(authority));
            PrepareFreshMotionCommandBacking();
            motionAuthority=authority;motionAuthorityThread=Thread.CurrentThread.ManagedThreadId;
        }
        internal void AssertMotionQuiescent()
        {if(motionPhase!=0)throw new InvalidOperationException("Navigation motion publication is not quiescent.");}
        private MotionPublication PrepareMotionPublication(NavOwnedStorage carrier,NavPreparedCrowdStep frame,long operation,bool results,NavigationRetentionBudget.Scope scope,out NavigationMotionAdoption adoption)
        {
            adoption=null;if(motionAuthority==null)return null;
            AssertMotionQuiescent();if(Thread.CurrentThread.ManagedThreadId!=motionAuthorityThread)throw new InvalidOperationException("Motion policy requires its owning Domain authority thread.");
            var publication=MotionPublication.Create(this,carrier,frame,Generation,operation,results,scope.OwnedReservation());
            motionPhase=1;preparingMotion=publication;
            try{
                adoption=motionAuthority.Prepare(publication)??throw new InvalidOperationException("Complete motion preadmission is unavailable.");
                if(publication.SelectedSource==null)throw new InvalidOperationException("Motion adoption has no accepted Native input.");
                publication.SourceBefore=NavPhysicalMacro.Acquire(publication,motionSources);
                var record=NavigationMotionRecord.Create(publication.SelectedSource,publication.Transcript,scope.OwnedReservation());
                try{publication.SourceAfter=NavigationMotionSources.Change(motionSources,Generation,motionSources.Watermark,publication.SelectedSource.Operation,record,scope.OwnedReservation());}finally{record.Release();}
                publication.SourceAfter.Retain();publication.SourcesDestination=true;
                return publication;
            }
            catch{preparingMotion=null;motionPhase=0;publication.MarkAbortedNoThrow();try{adoption?.Dispose();adoption=null;}finally{publication.Release();}throw;}
            finally{preparingMotion=null;motionPhase=0;}
        }
        private void EnterMotionPublication(MotionPublication publication,NavigationMotionAdoption adoption)
        {
            if(publication==null)return;AssertMotionQuiescent();
            if(Thread.CurrentThread.ManagedThreadId!=motionAuthorityThread||publication.Committed||publication.Aborted)throw new InvalidOperationException("Motion publication source/thread is stale.");
            motionPhase=1;try{if(!ReferenceEquals(publication.SourceBefore,motionSources)||publication.SelectedSource.LivePins<1)throw new InvalidOperationException("Accepted Native source changed before publication.");adoption.ValidateCurrent();}catch{motionPhase=0;throw;}
        }
        private void BeginMotionWrite(MotionPublication publication)
        {if(publication!=null)motionPhase=2;}
        private void CompleteMotionPublication(MotionPublication publication,NavigationMotionAdoption adoption)
        {
            if(publication==null)return;
            publication.RetiredSources=motionSources;motionSources=publication.SourceAfter;publication.SourcesDestination=false;
            publication.MarkCommittedNoThrow();
            try{adoption.ApplyCommittedNoThrow();motionPhase=0;}
            catch(Exception error){unresolvedMotionPublication=publication;unresolvedMotionAdoption=adoption;motionPublicationFailure=error;}
        }
        private void ThrowMotionPublicationFailure()
        {if(motionPublicationFailure!=null)throw new InvalidOperationException("Committed motion has an unresolved Runtime adoption.",motionPublicationFailure);}
        private void EndMotionPreparation(MotionPublication publication,NavigationMotionAdoption adoption)
        {
            // External releases happen after the entire Native+Runtime install.
            // A post-write contradiction remains a blocked owning obligation.
            if(publication==null)return;
            if(motionPhase==2){if(unresolvedMotionPublication==null){unresolvedMotionPublication=publication;unresolvedMotionAdoption=adoption;}return;}
            motionPhase=0;if(!publication.Committed)publication.MarkAbortedNoThrow();
            try{adoption?.Dispose();}finally{publication.Release();}
        }
    }
}
