using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    internal interface INavCrowdStorageSource
    {
        NavMotionWorkspace CreateMotionWorkspace(NavCrowd source);
        NavUnit CreateUnit(int id,NavPoint point,double radius,double speed,double turn);
        NavCrowdSpatialStorage CreateSpatialStorage(int actors);
        NavLocalRepairWorkspace CreateRepairWorkspace(int radius,NavOwnedStorage surfaceNames);
        NavRetainedArray<NavPoint> CopyRoute(IReadOnlyList<NavPoint> points,NavPoint? tail);
    }
    // Actual actor header and its current immutable routes. Empty route roles
    // use the process-wide immutable empty constant and have no private array.
    internal sealed class NavLiveUnitStorage : NavOwnedStorage
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavLiveUnitStorage))+NavStorageLayout.ObjectBytesBound(typeof(NavUnit));
        internal NavUnit Unit{get;private set;}
        internal NavRetainedArray<NavPoint> Global,Local;
        internal NavOwnedStorage InstalledExecution;
        internal bool InstalledDetached;
        // Compatibility hydration only. Current BODY installations use the
        // owned historical edge above; this slot has no independent index.
        internal object LegacyInstalledExecution;
        private NavLiveUnitStorage():base(Layout){Retain();}
        internal static NavLiveUnitStorage CreateOwned(int id,NavPoint position,double radius,double speed,double turn,INavStorageReservation reservation)
        {
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(Layout);var root=allocation.Register(new NavLiveUnitStorage());
                try{root.Unit=new NavUnit(id,position,radius,speed,turn);root.Unit.BindStorage(root);return root;}catch{root.Release();throw;}
            }
        }
        internal void SetGlobalNoThrow(NavRetainedArray<NavPoint> next,out NavRetainedArray<NavPoint> previous,out NavRetainedArray<NavPoint> local)
        {previous=Global;local=Local;Global=next;Local=null;Unit.BindRouteBackingNoThrow(next,null);}
        internal void SetLocalNoThrow(NavRetainedArray<NavPoint> next,out NavRetainedArray<NavPoint> previous)
        {previous=Local;Local=next;Unit.localRoute=next?.ReadOnlyBacking??Array.Empty<NavPoint>();}
        internal void ClearNoThrow(out NavRetainedArray<NavPoint> global,out NavRetainedArray<NavPoint> local)
        {global=Global;local=Local;Global=Local=null;Unit.BindRouteBackingNoThrow(null,null);}
        internal void SetInstalledNoThrow(NavOwnedStorage next,bool detached,object legacy,out NavOwnedStorage retired)
        {retired=InstalledExecution;InstalledExecution=next;InstalledDetached=(next!=null||legacy!=null)&&detached;LegacyInstalledExecution=legacy;}
        internal void Hydrate(NavRetainedArray<NavPoint> global,NavRetainedArray<NavPoint> local)
        {
            if(Global!=null||Local!=null)throw new InvalidOperationException("Actor route destination is not empty.");
            if(global!=null){RequireEdge(global);global.Retain();}if(local!=null){RequireEdge(local);local.Retain();}Global=global;Local=local;Unit.BindRouteBackingNoThrow(global,local);
        }
        protected override void ReleaseEdges(){Global?.Release();Local?.Release();InstalledExecution?.Release();Global=Local=null;InstalledExecution=null;LegacyInstalledExecution=null;Unit=null;}
    }
    // Prepared only for a Ready answer that can install. The source actor and
    // both retired route edges survive until publication has left its gate.
    internal sealed class NavPreparedUnitRoute : NavOwnedStorage,IDisposable
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavPreparedUnitRoute));
        internal NavLiveUnitStorage Actor;internal NavRetainedArray<NavPoint> Route;
        private NavRetainedArray<NavPoint> expectedGlobal,expectedLocal,retiredGlobal,retiredLocal;
        private NavNameReference terminalName;
        private NavPoint position,goal;private NavLocation location;private long incarnation,mobility;private bool applied;
        private NavPreparedUnitRoute():base(Layout){Retain();}
        internal static NavPreparedUnitRoute PrepareOwned(NavCrowd crowd,NavUnit actor,NavPoint goal,IReadOnlyList<NavPoint> points,NavLocation? assigned,INavStorageReservation reservation)
        {
            if(actor?.StorageOwner==null)throw new ArgumentException("Ready actor has no admitted header.");NavStorageAdmission.RequireOwned(reservation,actor.StorageOwner);
            var actorRoot=actor.StorageOwner;actorRoot.Retain();var oldGlobal=actorRoot.Global;oldGlobal?.Retain();var oldLocal=actorRoot.Local;oldLocal?.Retain();NavNameReference terminalName=default;
            try{
                var position=actor.Position;var start=actor.Location;
                if(!crowd.ValidateRoute(actor,position,start,goal,points,assigned,out var terminal))return null;
                var location=assigned??terminal;var name=location.NameReference;
                if(name.Owner!=null){NavStorageAdmission.RequireOwned(reservation,name.Owner);name.Owner.Retain();terminalName=name;}
                using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                    allocation.Reserve(Layout);var value=allocation.Register(new NavPreparedUnitRoute());
                    try{
                        value.Actor=actorRoot;actorRoot.Retain();value.position=position;value.location=location;value.terminalName=terminalName;terminalName.Owner?.Retain();value.goal=goal;value.incarnation=actor.Incarnation;value.mobility=actor.MobilityRevision;
                        value.expectedGlobal=oldGlobal;oldGlobal?.Retain();value.expectedLocal=oldLocal;oldLocal?.Retain();
                        value.Route=NavRetainedArray<NavPoint>.CopyAppending(points,goal,reservation.TryReserve);return value;
                    }catch{value.Dispose();throw;}
                }
            }finally{actorRoot.Release();oldGlobal?.Release();oldLocal?.Release();terminalName.Owner?.Release();}
        }
        internal void Validate(NavCrowd crowd)
        {var actor=Actor.Unit;if(applied||!crowd.TryGet(actor.Id,out var current)||!ReferenceEquals(actor,current)||!actor.Position.Equals(position)||actor.Incarnation!=incarnation||actor.MobilityRevision!=mobility||!ReferenceEquals(Actor.Global,expectedGlobal)||!ReferenceEquals(Actor.Local,expectedLocal))throw new InvalidOperationException("Prepared actor route source changed.");}
        private void ProjectMotionNoThrow(NavUnit actor){actor.Goal=goal;actor.GoalLocation=location;actor.Held=false;actor.Moving=true;actor.Outcome=NavigationOutcome.Moving;}
        internal void ProjectInto(NavUnit shadow)
        {if(applied||Route==null)throw new InvalidOperationException("Prepared route is no longer private.");shadow.BindRouteBackingNoThrow(Route,null);ProjectMotionNoThrow(shadow);}
        internal bool ApplyNoThrow()
        {Actor.SetGlobalNoThrow(Route,out retiredGlobal,out retiredLocal);Route=null;ProjectMotionNoThrow(Actor.Unit);applied=true;return true;}
        public void Dispose()=>Release();
        protected override void ReleaseEdges(){terminalName.Owner?.Release();Route?.Release();retiredGlobal?.Release();retiredLocal?.Release();Actor?.Release();expectedGlobal?.Release();expectedLocal?.Release();}
    }
}
