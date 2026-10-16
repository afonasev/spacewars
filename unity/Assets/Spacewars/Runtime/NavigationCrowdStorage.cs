using System;
using System.Collections.Generic;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession : INavCrowdStorageSource
    {
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
        NavUnit INavCrowdStorageSource.CreateUnit(int id,NavPoint point,double radius,double speed,double turn)=>CreateOwnedCrowdUnit(id,point,radius,speed,turn);
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
        private NavUnit CreateOwnedCrowdUnit(int id,NavPoint position,double radius,double speed,double turn)
        {
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,admissionPublicationIdentity,out var scope))throw new InvalidOperationException("Actor birth admission refused.");
            using(scope){NavLiveUnitStorage owner=null;try{owner=NavLiveUnitStorage.CreateOwned(id,position,radius,speed,turn,scope.OwnedReservation());scope.Attach(owner);scope.Commit(owner,_=>{});return owner.Unit;}catch{owner?.Release();throw;}}
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
