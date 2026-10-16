using System;
using System.IO;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal readonly struct NavigationArrivalLeaseValues
    {
        internal readonly long Token,Acquisition,Retirement,World,Attempt,Request,Entity,Incarnation,Order,Provider;
        internal readonly bool Retiring;
        internal NavigationArrivalLeaseValues(long token,long acquisition,long retirement,long world,long attempt,long request,long entity,long incarnation,long order,long provider,bool retiring)
        {Token=token;Acquisition=acquisition;Retirement=retirement;World=world;Attempt=attempt;Request=request;Entity=entity;Incarnation=incarnation;Order=order;Provider=provider;Retiring=retiring;}
        internal void Validate(){if(Token<1||Acquisition<1||World<1||Attempt<1||Request<1||Entity<0||Incarnation<1||Order<1||Provider<1||(Retiring?Retirement<1:Retirement!=0))throw new ArgumentException("Invalid physical arrival lease identity.");}
        internal void WriteBinding(BinaryWriter w){w.Write(Token);w.Write(Acquisition);w.Write(World);w.Write(Attempt);w.Write(Request);w.Write(Entity);w.Write(Incarnation);w.Write(Order);w.Write(Provider);}
        internal bool Same(NavigationArrivalLeaseValues value)=>Token==value.Token&&Acquisition==value.Acquisition&&Retirement==value.Retirement&&World==value.World&&Attempt==value.Attempt&&Request==value.Request&&Entity==value.Entity&&Incarnation==value.Incarnation&&Order==value.Order&&Provider==value.Provider&&Retiring==value.Retiring;
        internal static NavigationArrivalLeaseValues Read(ArrivalProviderLease lease)=>new NavigationArrivalLeaseValues(lease.Token,lease.Acquisition,lease.Retirement,lease.World,lease.Attempt,lease.Request,lease.Entity,lease.Incarnation,lease.Order,lease.ProviderId,lease.Retiring);
    }
    public sealed partial class NavigationSession
    {
        // Physical projection payloads belong to NavigationHostRow. This owner
        // contains the actual authority lease facade, without legacy buffers.
        internal sealed class NavigationLiveArrivalLease : NavOwnedStorage
        {
            private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationLiveArrivalLease))+NavStorageLayout.ObjectBytesBound(typeof(ArrivalProviderLease));
            internal ArrivalProviderLease Record;
            internal NavDeclarationStorage ProviderRoot;
            internal NavOwnedText Binding,InitialReason,RemovedReason;
            internal NavigationLiveArrivalLease RetirementPrevious,RetirementNext,RetiredNext;
            internal NavigationCommandSlots<long,ArrivalProviderLease> RetiredTokenMap;
            internal NavigationLiveArrivalLeaseTable RetiredLeaseTable;
            internal int Revision;
            private bool released;
            internal override bool IsImmutableCaptureRoot=>false;
            private NavigationLiveArrivalLease():base(Layout){Retain();}
            internal static NavigationLiveArrivalLease CreateOwned(NavigationArrivalLeaseValues data,NavigationSession session,ILayeredNavigationProvider provider,INavStorageReservation r,NavigationArrivalLeaseSnapshot snapshot=null)
            {
                data.Validate();var root=(provider as INavStorageFacade)?.StorageRoot as NavDeclarationStorage;
                if(session==null||session.Generation!=data.World||root==null||!root.IsImmutableCaptureRoot||!ReferenceEquals(root.Facade,provider))throw new ArgumentException("Physical arrival lease requires its exact provider owner.");NavStorageAdmission.RequireOwned(r,root);
                using(var allocation=new NavStorageAllocation(r.TryReserve)){
                    allocation.Reserve(Layout);var owner=allocation.Register(new NavigationLiveArrivalLease());
                    try{
                        owner.ProviderRoot=NavPhysicalMacro.Acquire(owner,root);
                        if(snapshot==null){using(NavStorageScratch.Reserve(r,512))owner.Binding=NavOwnedText.Digest(data.WriteBinding,72,r);owner.InitialReason=NavOwnedText.Copy("",r);owner.RemovedReason=NavOwnedText.Copy("gameplay-owner-removed",r);}
                        else{if(!snapshot.IsImmutableCaptureRoot||!snapshot.Data.Same(data)||!ReferenceEquals(snapshot.ProviderRoot,root))throw new ArgumentException("Cold physical lease differs from frozen roles.");owner.Binding=NavPhysicalMacro.Acquire(owner,snapshot.Binding);owner.InitialReason=NavPhysicalMacro.Acquire(owner,snapshot.InitialReason);owner.RemovedReason=NavPhysicalMacro.Acquire(owner,snapshot.RemovedReason);}
                        owner.Record=new ArrivalProviderLease{StorageOwner=owner,Session=session,Token=data.Token,Acquisition=data.Acquisition,Retirement=data.Retirement,World=data.World,Attempt=data.Attempt,Request=data.Request,Entity=data.Entity,Incarnation=data.Incarnation,Order=data.Order,ProviderId=data.Provider,Provider=provider,Retiring=data.Retiring,Attached=true,Binding=owner.Binding.Value,Reason=data.Retiring?owner.RemovedReason.Value:owner.InitialReason.Value};return owner;
                    }catch{owner.ReleaseStorage();throw;}
                }
            }
            internal void MarkRetiringNoThrow(long sequence){Record.Retiring=true;Record.Retirement=sequence;Record.Reason=RemovedReason.Value;Revision++;}
            internal void DetachNoThrow(){Record.Attached=false;Revision++;}
            internal void ReleaseStorage(){if(released)return;released=true;Release();}
            internal override void ReleaseOwner()=>ReleaseStorage();
            protected override void ReleaseEdges(){RetiredLeaseTable?.Release();RetiredLeaseTable=null;RetiredTokenMap?.Release();RetiredTokenMap=null;ProviderRoot?.Release();Binding?.Release();InitialReason?.Release();RemovedReason?.Release();ProviderRoot=null;Binding=InitialReason=RemovedReason=null;Record=null;}
        }
        private NavigationLiveArrivalLease retiredArrivalLeases;
        private NavigationLiveArrivalLease DetachRetiredArrivalLeasesNoThrow(){var value=retiredArrivalLeases;retiredArrivalLeases=null;return value;}
        private void RetireArrivalLeaseNoThrow(NavigationLiveArrivalLease owner){owner.RetiredNext=retiredArrivalLeases;retiredArrivalLeases=owner;}
        internal static void ReleaseRetiredArrivalLeases(NavigationLiveArrivalLease owner)
        {while(owner!=null){var next=owner.RetiredNext;owner.RetiredNext=null;var map=owner.RetiredTokenMap;owner.RetiredTokenMap=null;map?.Release();var table=owner.RetiredLeaseTable;owner.RetiredLeaseTable=null;table?.Release();owner.Release();owner=next;}}
    }
}
