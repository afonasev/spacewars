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
        public NavPoint AttackDestination(PlayableOwner owner,PlayableProfile balance=null)
        {
            var enemy=Headquarters(owner==PlayableOwner.Player?PlayableOwner.Enemy:PlayableOwner.Player);
            // A collinear rear goal forces every ground unit through the solid HQ footprint.
            // Keep the rear objective, using the open lane beside it for the initial encounter.
            double flank=(balance??PlayableProfile.Default).ProvingGroundAttackRearLaneOffset;
            return new NavPoint(Math.Sign(enemy.X)*(Math.Abs(enemy.X)+HalfExtent)/2,enemy.Z+Math.Sign(enemy.X)*flank);
        }
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
        // The entire map is one rectangular support with the same limits as geometry.
        // A disk that clears both endpoints stays on this support along the segment.
        public bool TryLocate(NavPoint point, double radius, out NavLocation location)
        {
            location = default;
            if (!geometry.IsFree(point, radius)) return false;
            location = Supports[0].OwnedLocation(point);
            return true;
        }
        public bool IsValidLocation(NavLocation location, double radius)
            => location.SurfaceId == Supports[0].SurfaceId && geometry.IsFree(location.Position, radius);
        public bool TryTraverse(NavLocation from, NavPoint to, double radius, out NavLocation location)
        {
            location = default;
            if (from.SurfaceId != Supports[0].SurfaceId || !geometry.SegmentFree(from.Position, to, radius)) return false;
            location = Supports[0].OwnedLocation(to);
            return true;
        }
        public bool CompatibleCombatSurface(NavLocation anchor, NavLocation other) => locations.Combat(anchor, other);
    }
}
