using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private readonly object recastStartupGate=new object();
        private RecastGroupPreparation recastStartup;
        internal RecastGroupPreparation PrepareRecastStartup(PlayableProfile playable,Func<bool> cancelled)
        {
            if(!UsesRecastGroundRoutes)return null;
            lock(recastStartupGate){
                if(recastStartup!=null)return recastStartup;
                using var pin=PinAdmission();
                var input=pin.Value??throw new InvalidOperationException("Recast startup needs its admission source.");var terrain=input.AuthoredTerrain;
                if(terrain==null&&(input.SurfaceProviderId!=NavLocation.FlatSurface||input.SurfaceSemanticsVersion!=1))throw new InvalidOperationException("Recast startup needs a supported authored or flat terrain binding.");
                if(!TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,Generation,pin.Value.PublicationIdentity,out var scope))throw new RecastNative.CapacityRefused();
                RecastGroupPreparation candidate=null;
                try{
                    var r=scope.OwnedReservation();candidate=RecastGroupPreparation.Create(r);
                    // The existing ground roster has three public clearance
                    // sources. Equal classes hit the same variant; aircraft do
                    // not enter the ground backend. Future overlays stay scoped.
                    using(var scratch=NavStorageScratch.Reserve(r,4096)){
                        var profile=playable.Navigation;
                        candidate.PreparePublic(terrain,input.Geometry,profile,r,cancelled);
                        candidate.PreparePublic(terrain,input.Geometry,profile.ForUnit(playable.ExplorerCollisionRadius,profile.TankSpeed,profile.TankTurnSpeed),r,cancelled);
                        candidate.PreparePublic(terrain,input.Geometry,profile.ForUnit(playable.ShkvalCollisionRadius,profile.TankSpeed,profile.TankTurnSpeed),r,cancelled);
                    }
                    if(cancelled?.Invoke()==true)throw new OperationCanceledException();
                    recastStartup=candidate;candidate=null;return recastStartup;
                }finally{candidate?.Dispose();scope.Discard();}
            }
        }
        internal void ReleaseRecastPreparation(RecastGroupPreparation expected)
        {lock(recastStartupGate){if(expected!=null&&!ReferenceEquals(recastStartup,expected))throw new InvalidOperationException("Recast preparation owner changed.");var old=recastStartup;recastStartup=null;old?.Dispose();}}
    }
    public sealed partial class CertifiedRouteKernel
    {
        internal void PrepareRecastStartup(PlayableRouteBinding binding)
        {if(binding.RoutePort.UsesRecastGroundRoutes)recastPreparation=binding.RoutePort.PrepareRecastStartup(binding.Profile,()=>Continue?.Invoke()==false);}
    }
}
