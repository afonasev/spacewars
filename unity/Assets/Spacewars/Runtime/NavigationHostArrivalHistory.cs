using System;
using System.Collections;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed class NavigationHostArrivalHistory : NavOwnedStorage,INavPhysicalBody,IReadOnlyList<NavigationSession.ArrivalAdvance>
    {
        private static readonly long LayoutBytes=NavStorageLayout.ObjectBytesBound(typeof(NavigationHostArrivalHistory));
        private NavPagedList<long> grants;
        internal override bool IsImmutableCaptureRoot=>grants!=null&&sealedHistory;
        private bool sealedHistory;
        public int Count=>grants.Count/8;
        public NavigationSession.ArrivalAdvance this[int n]
        {get{if(n<0||n>=Count)throw new ArgumentOutOfRangeException();int at=n*8;return new NavigationSession.ArrivalAdvance{Epoch=grants[at],External=grants[at+1],Allocation=checked((int)grants[at+2]),Graph=checked((int)grants[at+3]),Field=checked((int)grants[at+4]),Connectors=checked((int)grants[at+5]),Bookkeeping=checked((int)grants[at+6]),Maintenance=checked((int)grants[at+7])};}}
        private NavigationHostArrivalHistory(bool retained):base(LayoutBytes)
        {if(retained)Retain();}
        private static NavigationHostArrivalHistory Allocate(INavStorageReservation reservation,bool retained)
        {
            using(var allocation=new NavStorageAllocationContext(reservation.TryReserve)){
                allocation.Reserve(LayoutBytes);var value=new NavigationHostArrivalHistory(retained);allocation.Register(value);return value;
            }
        }

        internal static NavigationHostArrivalHistory CreateOwned(INavStorageReservation reservation)
        {
            var history=Allocate(reservation,true);
            try{history.grants=NavPhysicalMacro.Acquire(history,NavPagedList<long>.CreateOwned(reservation,4));history.grants.Release();history.grants.Seal();history.sealedHistory=true;return history;}
            catch{history.Release();throw;}
        }
        internal NavigationHostArrivalHistory AppendOwned(NavigationSession.ArrivalAdvance step,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,this);Validate(step,Count);
            var history=Allocate(reservation,true);
            try{history.grants=NavPhysicalMacro.Acquire(history,grants.ForkOwned(reservation));history.grants.Release();
                history.grants.Add(step.Epoch);history.grants.Add(step.External);history.grants.Add(step.Allocation);history.grants.Add(step.Graph);history.grants.Add(step.Field);history.grants.Add(step.Connectors);history.grants.Add(step.Bookkeeping);history.grants.Add(step.Maintenance);
                history.grants.Seal();history.sealedHistory=true;return history;
            }catch{history.Release();throw;}
        }
        private static void Validate(NavigationSession.ArrivalAdvance step,int epoch)
        {
            if(step.Epoch!=epoch||step.External<0||step.External>LayeredNavigationScheduler.MaxRetainedBytes||step.Allocation<1||step.Allocation>262144||step.Graph!=step.Allocation||step.Field!=step.Allocation||step.Connectors!=step.Allocation||step.Bookkeeping!=step.Allocation||step.Maintenance!=256)throw new ArgumentException("Invalid physical arrival grant history.");
        }
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.HostArrivalHistory);
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!IsImmutableCaptureRoot||grants.Count%8!=0||StorageBytes!=NavStorageLayout.Charge(LayoutBytes))throw new ArgumentException("Invalid physical history layout.");
            for(int n=0;n<Count;n++)Validate(this[n],n);NavPhysicalMacro.WriteEdge(writer,this,grants);
        }
        internal static NavigationHostArrivalHistory ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var grants=NavPhysicalMacro.ReadEdge<NavPagedList<long>>(reader);if(grants.Count%8!=0)throw new ArgumentException("Invalid physical grant history shape.");
            var owner=Allocate(reader.Reservation,false);owner.grants=NavPhysicalMacro.Acquire(owner,grants);owner.sealedHistory=true;
            for(int n=0;n<owner.Count;n++)Validate(owner[n],n);return owner;
        }
        public IEnumerator<NavigationSession.ArrivalAdvance> GetEnumerator(){for(int n=0;n<Count;n++)yield return this[n];}
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        protected override void ReleaseEdges(){grants?.Release();}
    }
}
