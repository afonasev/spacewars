using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Keep the legacy envelope's exact fields, layout and bytes untouched.
    internal sealed class NavigationAccruedHostEnvelope : NavOwnedStorage, INavPhysicalBody
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationAccruedHostEnvelope));
        internal NavigationHostRoot Host;internal NavigationAuthorityOwners Authority;internal NavigationTrafficAuthority Traffic;internal NavPagedList<long> Bank;internal long PublicationIdentity;
        private bool released;
        private NavigationAccruedHostEnvelope(bool owner):base(Layout){if(owner)Retain();}
        internal override bool IsImmutableCaptureRoot=>Host?.IsImmutableCaptureRoot==true&&Authority?.IsImmutableCaptureRoot==true&&Bank?.IsImmutableCaptureRoot==true&&Host.AccruedCredits;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.HostEnvelopePublication,value:NavPhysicalValueKind.AccruedAuthorityCreditsV1);
        private static NavigationAccruedHostEnvelope Allocate(INavStorageReservation r,bool owner){using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);return a.Register(new NavigationAccruedHostEnvelope(owner));}}
        internal static NavigationAccruedHostEnvelope Create(NavigationHostRoot host,NavigationAuthorityOwners authority,NavigationTrafficAuthority traffic,NavPagedList<long> bank,long publication,INavStorageReservation r)
        {
            if(publication<0||host==null||authority==null||bank==null)throw new ArgumentException("Missing accrued envelope roots.");
            NavigationAccruedCredits.Validate(bank,host,bank[NavigationAccruedCredits.Generation],long.MaxValue,long.MaxValue-1);
            NavStorageAdmission.RequireOwned(r,host);NavStorageAdmission.RequireOwned(r,authority);NavStorageAdmission.RequireOwned(r,bank);if(traffic!=null)NavStorageAdmission.RequireOwned(r,traffic);
            var root=Allocate(r,true);try{root.Host=NavPhysicalMacro.Acquire(root,host);root.Authority=NavPhysicalMacro.Acquire(root,authority);root.Traffic=NavPhysicalMacro.Acquire(root,traffic);root.Bank=NavPhysicalMacro.Acquire(root,bank);root.PublicationIdentity=publication;if(!root.IsImmutableCaptureRoot)throw new ArgumentException("Mutable accrued envelope.");return root;}catch{root.ReleaseStorage();throw;}
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter w)
        {
            if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(Layout))throw new ArgumentException("Invalid accrued envelope.");
            w.Output.Write(PublicationIdentity);w.Output.Write(Traffic!=null);NavPhysicalMacro.WriteEdge(w,this,Host);NavPhysicalMacro.WriteEdge(w,this,Authority);if(Traffic!=null)NavPhysicalMacro.WriteEdge(w,this,Traffic);NavPhysicalMacro.WriteEdge(w,this,Bank);
        }
        internal static NavigationAccruedHostEnvelope Read(NavPhysicalBodyReader r)
        {
            long publication=r.Input.ReadInt64();bool traffic=NavPhysicalMacro.ReadBoolean(r);if(publication<0)throw new ArgumentException("Invalid accrued publication identity.");
            var host=NavPhysicalMacro.ReadEdge<NavigationHostRoot>(r);var authority=NavPhysicalMacro.ReadEdge<NavigationAuthorityOwners>(r);var queue=traffic?NavPhysicalMacro.ReadEdge<NavigationTrafficAuthority>(r):null;var bank=NavPhysicalMacro.ReadEdge<NavPagedList<long>>(r);
            NavigationAccruedCredits.Validate(bank,host,bank[NavigationAccruedCredits.Generation],long.MaxValue,long.MaxValue-1);
            var root=Allocate(r.Reservation,false);root.Host=NavPhysicalMacro.Acquire(root,host);root.Authority=NavPhysicalMacro.Acquire(root,authority);root.Traffic=NavPhysicalMacro.Acquire(root,queue);root.Bank=NavPhysicalMacro.Acquire(root,bank);root.PublicationIdentity=publication;if(!root.IsImmutableCaptureRoot)throw new ArgumentException("Mutable accrued envelope.");return root;
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){Host?.Release();Authority?.Release();Traffic?.Release();Bank?.Release();Host=null;Authority=null;Traffic=null;Bank=null;}
    }
}
