using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        // Stable execution header. Capture uses separate immutable leaves;
        // retaining this live header never freezes its progress fields.
        internal sealed class NavigationLiveArrivalAttempt : NavOwnedStorage
        {
            private static readonly long Header=NavStorageLayout.ObjectBytesBound(typeof(NavigationLiveArrivalAttempt))+NavStorageLayout.ObjectBytesBound(typeof(ArrivalAttempt));
            private static long Layout(int count)=>checked(Header+32L+8L*count);
            internal ArrivalAttempt Record;
            internal NavigationArrivalWork Work;
            internal NavigationHostArrivalIntents Intents;
            internal NavigationHostArrivalHistory History;
            internal NavOwnedText Frontier,Phase,Reason,RemovedReason;
            internal NavRetainedArray<ILayeredNavigationProvider> Providers;
            private readonly long[] workingIds;
            internal int Revision;
            internal NavigationLiveArrivalAttempt RetiredNext;
            internal void ChangedNoThrow(){Revision++;}
            private bool released;
            internal override bool IsImmutableCaptureRoot=>false;
            private NavigationLiveArrivalAttempt(int count):base(Layout(count)){workingIds=new long[count];Retain();}
            internal static NavigationLiveArrivalAttempt CreateOwned(ArrivalAttempt source,NavigationArrivalWork work,NavigationHostArrivalIntents intents,NavigationHostArrivalHistory history,INavStorageReservation reservation,NavigationArrivalAttemptSnapshot snapshot=null)
            {
                if(source==null||source.StorageOwner!=null||source.Input==null||source.Projections?.Count>0||source.WorkingProviderIds!=null&&source.WorkingProviderIds.Length!=source.Input.WorkingBindings.Count)throw new ArgumentException("Invalid physical attempt header input.");
                NavStorageAdmission.RequireOwned(reservation,work);NavStorageAdmission.RequireOwned(reservation,intents);NavStorageAdmission.RequireOwned(reservation,history);
                if(history.Count!=source.Epoch||!ReferenceEquals(work.PhysicalInput,source.Input)||!ReferenceEquals(intents.Requests,source.Intents))throw new ArgumentException("Physical attempt role differs from original owner.");
                int count=source.Input.WorkingBindings.Count;
                using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                    allocation.Reserve(Layout(count));var owner=allocation.Register(new NavigationLiveArrivalAttempt(count));
                    try{
                        owner.Work=NavPhysicalMacro.Acquire(owner,work);owner.Intents=NavPhysicalMacro.Acquire(owner,intents);owner.History=NavPhysicalMacro.Acquire(owner,history);
                        if(snapshot==null){owner.Frontier=NavOwnedText.Copy(source.Frontier,reservation);owner.Phase=NavOwnedText.Copy(source.SnapshotPhase,reservation);
                            if(!string.IsNullOrEmpty(source.RetirementReason))owner.Reason=NavOwnedText.Copy(source.RetirementReason,reservation);owner.RemovedReason=NavOwnedText.Copy("gameplay-owner-removed",reservation);
                        }else{owner.Frontier=NavPhysicalMacro.Acquire(owner,snapshot.Frontier);owner.Phase=NavPhysicalMacro.Acquire(owner,snapshot.Phase);owner.Reason=NavPhysicalMacro.Acquire(owner,snapshot.InitialReason);owner.RemovedReason=NavPhysicalMacro.Acquire(owner,snapshot.RemovedReason);}
                        using(NavStorageScratch.Reserve(reservation,checked(512L+64L*(count+1)))){
                            var providers=source.OwnedProviders??new[]{source.Input.Provider}.Concat(source.Input.WorkingBindings.Select(b=>b.Provider)).Distinct(ProviderIdentityComparer.Instance).ToArray();
                            for(int i=0;i<providers.Length;i++)if(!(providers[i] is INavStorageFacade facade)||facade.StorageRoot==null)throw new ArgumentException("Physical attempt needs exact owned provider facades.");
                            owner.Providers=NavRetainedArray<ILayeredNavigationProvider>.Copy(providers,reservation.TryReserve);
                        }
                        if(source.WorkingProviderIds!=null)System.Array.Copy(source.WorkingProviderIds,owner.workingIds,count);
                        var record=source.CopyHeader();record.StorageOwner=owner;record.Work=record.OwnedWork=work;record.OwnedIntents=intents;record.History=record.OwnedHistory=history;
                        record.Intents=intents.Requests;record.LegacyJournal=null;record.Projections=null;record.OwnedProviders=owner.Providers.ReadOnlyBacking;record.WorkingProviderIds=owner.workingIds;
                        record.Frontier=owner.Frontier.Value;record.SnapshotPhase=owner.Phase.Value;record.RetirementReason=snapshot==null?owner.Reason?.Value:snapshot.CurrentReason;owner.Record=record;return owner;
                    }catch{owner.ReleaseStorage();throw;}
                }
            }
            internal void ReleaseStorage(){if(released)return;released=true;Release();}
            internal override void ReleaseOwner()=>ReleaseStorage();
            protected override void ReleaseEdges(){Work?.Release();Intents?.Release();History?.Release();Frontier?.Release();Phase?.Release();Reason?.Release();RemovedReason?.Release();Providers?.Release();Work=null;Intents=null;History=null;Frontier=Phase=Reason=RemovedReason=null;Providers=null;Record=null;}
        }
    }
}
