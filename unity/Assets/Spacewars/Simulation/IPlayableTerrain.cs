using System.Collections.Generic;
namespace Spacewars.Simulation
{
    // Shared native support contract; geometry and ownership remain simulation authority.
    public interface IPlayableTerrain
    {
        string Id{get;} int Revision{get;} double HalfExtent{get;} double DirectFireHeight{get;}
        IReadOnlyList<MapSupport> Supports{get;} IReadOnlyList<NavObstacle> Solids{get;} IReadOnlyList<NavObstacle> MovementBlockers{get;}
        MapSupport SupportAt(NavPoint point); double SurfaceHeight(NavPoint point); NavPoint SurfaceGradient(NavPoint point);
        bool SupportsFootprint(NavPoint point,double radius); bool SupportsSweep(NavPoint from,NavPoint to,double radius);
        // Resolution is provider-owned, not a repeated support label or height heuristic.
        int SurfaceSemanticsVersion{get;} IReadOnlyList<NavSurfaceTransition> SurfaceTransitions{get;}
        bool TryLocate(NavPoint point,double radius,out NavLocation location);
        bool IsValidLocation(NavLocation location,double radius);
        bool TryTraverse(NavLocation from,NavPoint to,double radius,out NavLocation location);
        bool CompatibleCombatSurface(NavLocation anchor,NavLocation other);
        TerritorySite[] Sites(PlayableProfile profile); NavPoint Headquarters(PlayableOwner owner);
    }
}
