using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        // Authority-thread scratch for a single spectator publication, not an
        // authority revision cache. Visibility is still evaluated per view.
        // DTOs are sealed immutable values. Snapshot constructors close arrays.
        private sealed class ProjectionBatch
        {
            internal readonly Dictionary<PlayableVision,TeamVisionSnapshot> Visions=new Dictionary<PlayableVision,TeamVisionSnapshot>();
            internal readonly Dictionary<int,(FrameRows<PlayableEntitySnapshot> Rows, int Index)> PublicUnits=new Dictionary<int,(FrameRows<PlayableEntitySnapshot> Rows, int Index)>();
            internal readonly Dictionary<int,(FrameRows<PlayableEntitySnapshot> Rows, int Index)> PlayerUnits;
            internal ProjectionBatch(bool includesOverview)
            {
                if (includesOverview) PlayerUnits = new Dictionary<int,(FrameRows<PlayableEntitySnapshot>, int)>();
            }
            internal readonly Dictionary<(int,bool),(FrameRows<PlayableBuildingSnapshot> Rows, int Index)> Buildings=new Dictionary<(int,bool),(FrameRows<PlayableBuildingSnapshot> Rows, int Index)>();
            internal readonly Dictionary<PlayableOwner,PlayablePopulationSnapshot> Populations=new Dictionary<PlayableOwner,PlayablePopulationSnapshot>();
            internal readonly HashSet<int> ProjectileEndpointIds=new HashSet<int>();
            internal readonly Dictionary<TeamVisionSnapshot,PlayableGroundTrail[]> Trails=new Dictionary<TeamVisionSnapshot,PlayableGroundTrail[]>();
            internal PlayableGroundTrail[] OverviewTrails;
            internal TerritorySite[] PublicSites;
            internal KnownRelay[] RelayControl;
            internal TerritorySiteSnapshot[] Sites;
            internal bool VisionRefreshed;
        }
        private ProjectionBatch projectionBatch;
        internal IDisposable BeginProjectionBatch(bool includesOverview=false)=>new ProjectionScope(this,includesOverview).Enter();

        // A single publication may open the same read cache first for the primary
        // view and then for spectator views, provided no world writer ran between them.
        internal sealed class ProjectionScope : IDisposable
        {
            private readonly PlayableDomain domain;
            private readonly ProjectionBatch batch;
            private ProjectionBatch previous;

            internal ProjectionScope(PlayableDomain domain, bool includesOverview)
            {
                this.domain = domain;
                batch = new ProjectionBatch(includesOverview);
            }

            internal ProjectionScope Enter()
            {
                previous = domain.projectionBatch;
#if UNITY_INCLUDE_TESTS
                if (domain.DisableProjectionBatchForTests) return this;
#endif
                domain.projectionBatch = batch;
                return this;
            }

            public void Dispose() => domain.projectionBatch = previous;
        }
#if UNITY_INCLUDE_TESTS
        internal bool DisableProjectionBatchForTests;
#endif
        private void RefreshProjectionVision()
        {if(projectionBatch==null||!projectionBatch.VisionRefreshed){RefreshVision();if(projectionBatch!=null)projectionBatch.VisionRefreshed=true;}}
        private TeamVisionSnapshot ProjectionVision(PlayableVision vision)
        {
            if(projectionBatch==null)return vision.Snapshot();
            if(!projectionBatch.Visions.TryGetValue(vision,out var snapshot)){snapshot=vision.Snapshot();projectionBatch.Visions.Add(vision,snapshot);}
            return snapshot;
        }
        // Owner-review adoption only checks the living own recipients. Preserve
        // the old projection's vision refresh at this exact authority boundary.
        internal HashSet<int> LivingReviewRecipients(PlayableOwner owner)
        {
            RefreshProjectionVision();
            var live=new HashSet<int>();
            foreach(var unit in units.Values)
                if(unit.Owner==owner&&TryActor(unit.Id,out _)&&(int)Math.Ceiling(unit.Health)>0)
                    live.Add(unit.Id);
            return live;
        }
        private PlayablePopulationSnapshot ProjectionPopulation(PlayableOwner owner)
        {
            if(projectionBatch==null)return Population(owner);
            if(!projectionBatch.Populations.TryGetValue(owner,out var population)){
                population=Population(owner);
                projectionBatch.Populations.Add(owner,population);
            }
            return population;
        }
        // Non-selected team members previously received PlayerSnapshot with no
        // requested routes. Their Targets and RequestedSites were therefore empty.
        // Only defense threats depend on that view; durable commitments still
        // come from the exact detached owner clone on the review worker.
        internal AiTeamPublicationObservation TeamPublicationObservation(long sequence,RuntimeStatus status,PlayableRuntimeMetrics metrics,int seed,PlayableOwner owner)
        {
            RefreshProjectionVision();var vision=Vision(owner);var enemies=new List<PlayableEntitySnapshot>();var centers=new List<PlayableBuildingSnapshot>();
            foreach(var unit in units.Values)if(Hostile(unit.Owner,owner)&&TryActor(unit.Id,out var actor)&&vision.IsVisible(actor.Position))
                enemies.Add(new PlayableEntitySnapshot(unit.Id,unit.Owner,unit.Kind,actor.Position,(int)Math.Ceiling(unit.Health),false,0,0,0));
            foreach(var b in buildings.Values)if(b.Owner==owner)
                centers.Add(new PlayableBuildingSnapshot(b.Id,b.Owner,b.Kind,b.Position,(int)Math.Ceiling(b.Health),0,0,0,default,phase:b.Phase,includePrivateState:false));
            var damage=centerDamage.Values.Where(d=>d.Owner==owner&&centers.Any(b=>b.Id==d.CenterId&&b.Health>0)&&enemies.Any(e=>e.Id==d.AttackerId)).OrderBy(d=>d.CenterId).ThenBy(d=>d.AttackerId).Select(d=>d.Copy()).ToArray();
            // This private temporary input cannot enter a participant/UI view or
            // selected-owner evaluation. Run the original defense rules unchanged.
            var snapshot=new PlayableSnapshot(profile.ProfileId,profile.Revision,navigation.Generation,seed,sequence,Tick,status,false,Outcome,0,Geometry,enemies.ToArray(),centers.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),metrics,null,
                vision:ProjectionVision(vision),ownCenterDamage:damage,owner:owner,ownerId:offline==null?null:offline.Roster[(int)owner].Id,team:TeamOf(owner),participants:offline?.Roster.ToArray(),domination:DominationSnapshot());
            return new AiTeamPublicationObservation(snapshot);
        }
        private PlayableGroundTrail[] ProjectionGroundTrails(TeamVisionSnapshot vision)
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.SnapshotTrails);
            if(projectionBatch==null)return groundTrails.Visible(Tick,vision,profile.GroundTrailLifetimeTicks);
            if(vision==null)return projectionBatch.OverviewTrails??(projectionBatch.OverviewTrails=groundTrails.Visible(Tick,null,profile.GroundTrailLifetimeTicks));
            if(!projectionBatch.Trails.TryGetValue(vision,out var trails)){
                trails=groundTrails.Visible(Tick,vision,profile.GroundTrailLifetimeTicks);
                projectionBatch.Trails.Add(vision,trails);
            }
            return trails;
        }
    }
}
