using System;
using System.Collections.Generic;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Mutation-local carrier; its edges are transferred together, never admitted
    // separately. Source pins keep mutable slots alive across admission callbacks.
    internal sealed class NavigationCrowdBirth : NavOwnedStorage
    {
        internal static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationCrowdBirth));
        internal NavLiveUnitStorage Actor;internal NavCrowdSpatialStorage Spatial;internal NavigationCrowdNameBacking Names;
        private bool released;
        private NavigationCrowdBirth():base(Layout){Retain();}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        internal static NavigationCrowdBirth CreateOwned(INavStorageReservation reservation)
        {using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(Layout);return allocation.Register(new NavigationCrowdBirth());}}
        protected override void ReleaseEdges(){Actor?.Release();Spatial?.Release();Names?.Release();}
    }
    public sealed partial class NavigationSession : INavCrowdStorageSource
    {
        internal static long BirthHeadroom(int actors)
        {
            int count=checked(actors+1);
            return checked(NavigationRetentionBudget.ScopeBytes+64+NavStorageLayout.Charge(NavigationCrowdBirth.Layout)+NavStorageLayout.Charge(NavLiveUnitStorage.Layout)+NavStorageLayout.Charge(NavCrowdSpatialStorage.Layout(count))+NavStorageLayout.Charge(NavigationCrowdNameBacking.Layout(count))+NavStorageScratch.LeaseBytes+64L+4L*count+3L*count*NavStorageLayout.ValueBytes(typeof(NavNameReference)));
        }
        bool INavCrowdStorageSource.Remove(int id)=>Remove(id);
        NavUnit INavCrowdStorageSource.Add(NavCrowd source,int id,NavPoint point,double radius,double speed,double turn,NavLocation? assigned,bool canYield)
        {
            NavigationRetentionBudget budget;NavigationCommandAuthority backing;NavigationCrowdNameBacking oldNames;NavigationAuthorityOwners authority;NavigationAdmissionStorage admission;
            NavigationProfile observedProfile;NavGeometry observedGeometry;NavCrowdSpatialStorage oldSpatial;NavLocation location;long generation,publication,operation,membership,attachment;CertifiedRouteKernel worker;
            using(EnterTransport()){
                RejectTransferCallbackReentry();if(!ReferenceEquals(source,Crowd))throw new InvalidOperationException("Actor crowd source changed.");
                location=source.PrepareBirthLocation(id,point,radius,speed,turn,assigned);
                budget=physicalHostBudget;generation=Generation;publication=admissionPublicationIdentity;operation=authorityOperationIdentity;membership=source.MotionMembership;observedProfile=source.Profile;observedGeometry=source.Geometry;oldSpatial=source.SpatialStorage;
                backing=groupCommandBacking;oldNames=backing?.CrowdNames;authority=authorityOwners;admission=admissionOwner;worker=certifiedHost;attachment=certifiedHostAttachmentIdentity;
                backing?.Retain();oldNames?.Retain();authority?.Retain();admission?.Retain();oldSpatial?.Retain();location.NameReference.Owner?.Retain();
            }
            try{
                if(!budget.TryOpenScope(NavigationPreparationKind.Admission,generation,publication,out var scope))throw new NavigationRetentionBudget.CapacityRefused();
                using(scope){NavigationCrowdBirth candidate=null;NavCrowdSpatialStorage retiredSpatial=null;NavigationCrowdNameBacking retiredNames=null;
                    try{
                        var reservation=scope.CapacityReservationOwned();
                        candidate=NavigationCrowdBirth.CreateOwned(reservation);
                        candidate.Actor=NavLiveUnitStorage.CreateOwned(id,point,radius,speed,turn,reservation,canYield);candidate.Actor.Unit.Location=location;
                        int count;using(EnterTransport()){ValidateSource();count=source.Units.Count;}
                        candidate.Spatial=NavCrowdSpatialStorage.Create(checked(count+1),reservation.TryReserve);
                        if(backing!=null)candidate.Names=CaptureCrowdNameReferences(reservation,prospective:candidate.Actor.Unit);
                        scope.Attach(candidate);var actor=candidate.Actor.Unit;
                        scope.Publish(candidate,_=>{using(EnterTransport()){
                            scope.AssertOwnerLive();ValidateSource();
                            if(scope.IsCancelled||scope.Owner!=Generation||scope.Publication!=publication||authorityOperationIdentity==long.MaxValue)throw new InvalidOperationException("Actor birth scope changed.");
                            NavLocation current;try{current=source.PrepareBirthLocation(id,point,radius,speed,turn,assigned);}catch(ArgumentException error){throw new InvalidOperationException("Actor placement changed during admission.",error);}
                            if(!SameName(current.NameReference,location.NameReference)||!current.Equals(location))throw new InvalidOperationException("Actor birth surface changed.");
                            if(candidate.Names!=null){
                                foreach(var unit in source.Units){int row=candidate.Names.Find(unit.Id);if(row<0||!SameName(candidate.Names.Names[row*3],OwnedOrFlatName(unit.Location))||!SameName(candidate.Names.Names[row*3+1],OwnedOrFlatName(unit.GoalLocation))||!SameName(candidate.Names.Names[row*3+2],OwnedOrFlatName(unit.FailedGoal)))throw new InvalidOperationException("Actor birth name selectors changed.");}
                            }
                            source.PrepareBirthIndex(actor,candidate.Spatial);
                            // No callbacks, admission or fallible preparation after this point.
                            retiredSpatial=source.AddPreparedNoThrow(actor,candidate.Spatial);candidate.Spatial=null;candidate.Actor=null;
                            if(backing!=null){retiredNames=backing.CrowdNames;backing.CrowdNames=candidate.Names;candidate.Names=null;}
                            authorityOperationIdentity=operation+1;
                        }});
                        scope.ResolveCommitted();return actor;
                    }finally{retiredSpatial?.Release();retiredNames?.Release();candidate?.ReleaseStorage();}
                    void ValidateSource(){if(!ReferenceEquals(budget,physicalHostBudget)||generation!=Generation||publication!=admissionPublicationIdentity||operation!=authorityOperationIdentity||!ReferenceEquals(source,Crowd)||membership!=source.MotionMembership||!ReferenceEquals(observedProfile,source.Profile)||!ReferenceEquals(observedGeometry,source.Geometry)||!ReferenceEquals(oldSpatial,source.SpatialStorage)||!ReferenceEquals(backing,groupCommandBacking)||!ReferenceEquals(oldNames,groupCommandBacking?.CrowdNames)||!ReferenceEquals(authority,authorityOwners)||!ReferenceEquals(admission,admissionOwner)||!ReferenceEquals(worker,certifiedHost)||attachment!=certifiedHostAttachmentIdentity||source.TryGet(id,out _))throw new InvalidOperationException("Actor birth authoritative source changed.");}
                }
            }finally{backing?.Release();oldNames?.Release();authority?.Release();admission?.Release();oldSpatial?.Release();location.NameReference.Owner?.Release();}
        }
        private static bool SameName(NavNameReference a,NavNameReference b)=>ReferenceEquals(a.Owner,b.Owner)&&a.Role==b.Role&&a.Index==b.Index;
        NavMotionWorkspace INavCrowdStorageSource.CreateMotionWorkspace(NavCrowd source)=>CreateCompatibilityMotionWorkspace(source);
        private NavMotionWorkspace CreateCompatibilityMotionWorkspace(NavCrowd source)
        {
            int count;long membership,publication;
            using(EnterTransport()){if(!ReferenceEquals(source,Crowd))throw new InvalidOperationException("Motion workspace source changed.");count=source.Units.Count;membership=source.MotionMembership;publication=admissionPublicationIdentity;}
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out var scope))throw new InvalidOperationException("Motion workspace admission refused.");
            IDisposable scratch=null;
            try{
                scratch=NavStorageScratch.Reserve(scope.OwnedReservation(),checked(NavMotionWorkspace.HeaderBytes+NavMotionWorkspace.ArrayBytes(count)));
                using(EnterTransport()){if(!ReferenceEquals(source,Crowd)||count!=source.Units.Count||membership!=source.MotionMembership)throw new InvalidOperationException("Motion workspace membership changed during admission.");}
                return new NavMotionWorkspace(source,count,membership,scratch,scope);
            }catch{scratch?.Dispose();scope.Dispose();throw;}
        }
        private void ValidateCompatibilityMotion(NavMotionWorkspace workspace)
        {if(workspace==null||!workspace.SourceCurrent(Crowd))throw new InvalidOperationException("Compatibility motion workspace lost its source.");}
        NavUnit INavCrowdStorageSource.CreateUnit(int id,NavPoint point,double radius,double speed,double turn,bool canYield)=>CreateOwnedCrowdUnit(id,point,radius,speed,turn,canYield);
        NavRetainedArray<NavPoint> INavCrowdStorageSource.CopyRoute(IReadOnlyList<NavPoint> points,NavPoint? tail)=>CopyOwnedCrowdRoute(points,tail);
        NavCrowdSpatialStorage INavCrowdStorageSource.CreateSpatialStorage(int actors)
        {
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,admissionPublicationIdentity,out var scope))throw new InvalidOperationException("Spatial index admission refused.");
            using(scope){NavCrowdSpatialStorage owner=null;try{owner=NavCrowdSpatialStorage.Create(actors,scope.TryReserve);scope.Attach(owner);scope.Commit(owner,_=>{});return owner;}catch{owner?.Release();throw;}}
        }
        NavLocalRepairWorkspace INavCrowdStorageSource.CreateRepairWorkspace(int radius,NavOwnedStorage surfaceNames)
        {
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,admissionPublicationIdentity,out var scope))throw new InvalidOperationException("Local repair workspace admission refused.");
            using(scope){NavLocalRepairWorkspace owner=null;try{owner=NavLocalRepairWorkspace.Create(radius,scope.TryReserve,surfaceNames);scope.Attach(owner);scope.Commit(owner,_=>{});return owner;}catch{owner?.Release();throw;}}
        }
        private NavUnit CreateOwnedCrowdUnit(int id,NavPoint position,double radius,double speed,double turn,bool canYield)
        {
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,admissionPublicationIdentity,out var scope))throw new NavigationRetentionBudget.CapacityRefused();
            using(scope){NavLiveUnitStorage owner=null;try{owner=NavLiveUnitStorage.CreateOwned(id,position,radius,speed,turn,scope.OwnedReservation(),canYield);scope.Attach(owner);scope.Commit(owner,_=>{});return owner.Unit;}catch{owner?.Release();throw;}}
        }
        private NavRetainedArray<NavPoint> CopyOwnedCrowdRoute(IReadOnlyList<NavPoint> points,NavPoint? tail)
        {
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,admissionPublicationIdentity,out var scope))throw new InvalidOperationException("Actor route admission refused.");
            using(scope){NavRetainedArray<NavPoint> owner=null;try{owner=tail.HasValue?NavRetainedArray<NavPoint>.CopyAppending(points,tail.Value,scope.TryReserve):NavRetainedArray<NavPoint>.Copy(points,scope.TryReserve);scope.Attach(owner);scope.Commit(owner,_=>{});return owner;}catch{owner?.Release();throw;}}
        }
        private void CaptureActorRouteCatalog(NavigationSessionState state)
        {
            bool physicalInstalled=physicalArrivalEnabled||committedHostRoot!=null;
            state.CaptureActorGlobal=new NavOwnedStorage[state.Crowd.Units.Length];state.CaptureActorLocal=new NavOwnedStorage[state.Crowd.Units.Length];if(physicalInstalled)state.CaptureInstalled=new NavOwnedStorage[state.InstalledExecutions.Length];
            for(int i=0;i<state.Crowd.Units.Length;i++){
                if(!Crowd.TryGet(state.Crowd.Units[i].Id,out var actor)||actor.StorageOwner==null)throw new InvalidOperationException("Captured actor header has no live owner.");
                var global=actor.StorageOwner.Global;var local=actor.StorageOwner.Local;
                if((global?.Count??0)!=actor.route.Length||(local?.Count??0)!=actor.localRoute.Length||global!=null&&!ReferenceEquals(global.ReadOnlyBacking,actor.route)||local!=null&&!ReferenceEquals(local.ReadOnlyBacking,actor.localRoute))throw new InvalidOperationException("Captured actor route has no exact owner.");
                global?.Retain();state.CaptureActorGlobal[i]=global;local?.Retain();state.CaptureActorLocal[i]=local;
            }
            if(!physicalInstalled)return; // Legacy v8 persists the ordinary installed DTO, without a physical BODY catalog.
            for(int i=0;i<state.InstalledExecutions.Length;i++){
                var owner=installedExecutions[state.InstalledExecutions[i].Entity].StorageOwner;if(owner==null)throw new InvalidOperationException("Captured installed execution has no historical owner.");owner.Retain();state.CaptureInstalled[i]=owner;
            }
        }
        private void BindOwnedCrowdFactories(){Crowd.StorageSource=this;}
    }
}
