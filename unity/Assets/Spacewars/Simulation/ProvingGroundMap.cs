using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Symmetric open ground for two laboratory armies.
    public sealed class ProvingGroundMap : IPlayableTerrain, INavArrivalPointTerrain
    {
        public const int Capacity = 2;
        public string Id => "balance-proving-ground-map-v1";
        public int Revision => 1;
        public double HalfExtent => 64;
        public double DirectFireHeight => 1;
        public int SurfaceSemanticsVersion => 1;
        public IReadOnlyList<MapSupport> Supports { get; }
        public IReadOnlyList<NavObstacle> Solids => Array.Empty<NavObstacle>();
        public IReadOnlyList<NavObstacle> MovementBlockers => Solids;
        public IReadOnlyList<NavSurfaceTransition> SurfaceTransitions => Array.Empty<NavSurfaceTransition>();
        private readonly NavGeometry geometry;
        private readonly TerrainLocations locations;
        TerrainLocations INavArrivalPointTerrain.ArrivalLocations=>locations;

        public ProvingGroundMap()
        {
            Supports = Array.AsReadOnly(new[] { new MapSupport("open-ground",
                new NavObstacle(-HalfExtent, -HalfExtent, HalfExtent, HalfExtent), false, surfaceId: "open-ground") });
            geometry = new NavGeometry(HalfExtent, Array.Empty<NavObstacle>(), Revision);
            locations = new TerrainLocations(geometry, Supports, SurfaceTransitions);
        }
        // Authored map coordinates, not combat tuning. Mirror the same ordinary slots.
        public NavPoint Headquarters(PlayableOwner owner) => new NavPoint(owner==PlayableOwner.Player?-40:40,0);
        public NavPoint AttackDestination(PlayableOwner owner)
        {var enemy=Headquarters(owner==PlayableOwner.Player?PlayableOwner.Enemy:PlayableOwner.Player);return new NavPoint(Math.Sign(enemy.X)*(Math.Abs(enemy.X)+HalfExtent)/2,enemy.Z);}
        public TerritorySite[] Sites(PlayableProfile p) => new[] {
            new TerritorySite(1,PlayableBuildingKind.Headquarters,Headquarters(PlayableOwner.Player),p),
            new TerritorySite(2,PlayableBuildingKind.Headquarters,Headquarters(PlayableOwner.Enemy),p),
            new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(-40,30),p),
            new TerritorySite(4,PlayableBuildingKind.Outpost,new NavPoint(40,30),p),
            new TerritorySite(5,PlayableBuildingKind.Mine,new NavPoint(-40,-30),p),
            new TerritorySite(6,PlayableBuildingKind.Mine,new NavPoint(40,-30),p)};
        public MapSupport SupportAt(NavPoint point) => Supports[0].Contains(point) ? Supports[0] : null;
        public double SurfaceHeight(NavPoint point) => 0;
        public NavPoint SurfaceGradient(NavPoint point) => default(NavPoint);
        public bool SupportsFootprint(NavPoint point, double radius) => geometry.IsFree(point, radius);
        public bool SupportsSweep(NavPoint from, NavPoint to, double radius) => geometry.SegmentFree(from, to, radius);
        public bool TryLocate(NavPoint point, double radius, out NavLocation location) => locations.TryLocate(point, radius, out location);
        public bool IsValidLocation(NavLocation location, double radius) => locations.Valid(location, radius);
        public bool TryTraverse(NavLocation from, NavPoint to, double radius, out NavLocation location) => locations.Traverse(from, to, radius, out location);
        public bool CompatibleCombatSurface(NavLocation anchor, NavLocation other) => locations.Combat(anchor, other);
    }
}
