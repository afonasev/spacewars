using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private sealed class TrailMeshLease
        {
            internal PlayableWorld Owner;
            internal int Epoch, Users, Quads;
            internal Mesh Mesh;
        }

        private MeshFilter trailFilter;
        private TrailMeshLease trailMeshLease, trailSpare;
        private int trailMeshEpoch;
        private TeamVisionSnapshot trailShareVision;
        private long trailShareGeneration, trailShareSequence, trailShareTick;
        private int trailShareTeam;
        private readonly List<PlayableGroundTrail> trailPicked = new();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal int TrailMeshBuildsForQa, TrailMeshReusesForQa;
#endif

        private bool KeepTrailSample(PlayableGroundTrail sample, long tick, int divisor, int lifetime) =>
            tick >= sample.Tick && tick - sample.Tick < lifetime && trailVehicles.Keep(sample, divisor);

        private void CollectTrailSamples(PlayableSnapshot view, int divisor, int lifetime)
        {
            trailPicked.Clear();
            var samples = view.GroundTrails;
            for (int i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                if (KeepTrailSample(sample, view.Tick, divisor, lifetime))
                    trailPicked.Add(sample);
            }
        }

        private bool CanReuseTrailMesh(PlayableWorld source, PlayableSnapshot view)
        {
            if (source?.trailShareVision == null || source.trailMeshLease == null ||
                !ReferenceEquals(profile, source.profile) ||
                !ReferenceEquals(view.Vision, source.trailShareVision) || view.Team != source.trailShareTeam ||
                view.Generation != source.trailShareGeneration || view.Sequence != source.trailShareSequence ||
                view.Tick != source.trailShareTick ||
                trailLifetimeTicks != source.trailLifetimeTicks ||
                trailPadQueries.Count != source.trailPadQueries.Count ||
                trailPicked.Count != source.trailPicked.Count)
                return false;

            for (int i = 0; i < trailPadQueries.Count; i++)
                if (!trailPadQueries[i].SameShape(source.trailPadQueries[i]))
                    return false;
            for (int i = 0; i < trailGeometryStyle.Length; i++)
                if (!trailGeometryStyle[i].Equals(source.trailGeometryStyle[i]))
                    return false;
            // Keep each player's historical density choices. Equal current vision
            // alone says nothing about choices made while one camera was zoomed out.
            for (int i = 0; i < trailPicked.Count; i++)
                if (!ReferenceEquals(trailPicked[i], source.trailPicked[i]))
                    return false;
            return SameMineTrailSurfaces(source, (ThreeCrossingsMap)profile.AuthoredMap);
        }

        private void RecordShareableTrailMesh(PlayableSnapshot view)
        {
            trailShareVision = view.Vision;
            trailShareGeneration = view.Generation;
            trailShareSequence = view.Sequence;
            trailShareTick = view.Tick;
            trailShareTeam = view.Team;
        }

        private bool SameMineTrailSurfaces(PlayableWorld source, ThreeCrossingsMap map)
        {
            EnsureTrailMineAccesses(map);
            source.EnsureTrailMineAccesses(map);
            var a = trailCrossingsArt;
            var b = source.trailCrossingsArt;
            if (!a.mineYardRadius.Equals(b.mineYardRadius) || !a.mineYardHalfWidth.Equals(b.mineYardHalfWidth) ||
                !a.minePadContactWidth.Equals(b.minePadContactWidth) || trailMineAccesses.Length != source.trailMineAccesses.Length)
                return false;
            for (int i = 0; i < trailMineAccesses.Length; i++)
                if (!trailMineAccesses[i].Site.Position.Equals(source.trailMineAccesses[i].Site.Position) ||
                    !trailMineAccesses[i].Connection.Equals(source.trailMineAccesses[i].Connection))
                    return false;
            return true;
        }

        private void HideTrailMesh()
        {
            if (trailObject) trailObject.SetActive(false);
            trailShareVision = null;
            trailSequence = -1;
            trailCount = -1;
        }

        private void PrepareWritableTrailMesh()
        {
            if (trailMeshLease?.Owner == this && trailMeshLease.Users == 1)
                return;
            var writable = trailSpare;
            trailSpare = null;
            if (writable == null)
            {
                var mesh = new Mesh { name = "Batched ground impressions", indexFormat = IndexFormat.UInt32 };
                mesh.MarkDynamic();
                writable = new TrailMeshLease { Owner = this, Epoch = trailMeshEpoch, Mesh = mesh };
            }
            SetTrailMesh(writable);
        }

        private void SetTrailMesh(TrailMeshLease next)
        {
            var previous = trailMeshLease;
            if (previous == next) return;
            if (next != null) next.Users++;
            trailMeshLease = next;
            trailMesh = next?.Mesh;
            if (trailFilter) trailFilter.sharedMesh = trailMesh;
            if (previous != null && --previous.Users == 0)
                previous.Owner.ReturnTrailMesh(previous);
        }

        private void ReturnTrailMesh(TrailMeshLease lease)
        {
            // Once all renderers have moved to another mesh, its owner can write
            // this one again. A clear or rebind retires older leases on return.
            if (lease.Epoch == trailMeshEpoch && trailSpare == null)
                trailSpare = lease;
            else
                ReleaseWorldObject(lease.Mesh);
        }

        private void ClearTrailMeshLeases()
        {
            trailMeshEpoch++;
            trailShareVision = null;
            trailPicked.Clear();
            SetTrailMesh(null);
            if (trailSpare != null) ReleaseWorldObject(trailSpare.Mesh);
            trailSpare = null;
            trailFilter = null;
        }
    }
}
