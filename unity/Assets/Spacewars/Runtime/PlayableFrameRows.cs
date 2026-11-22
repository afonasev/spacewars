using System;
using System.Collections;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        // Single writer during capture. After capture, any reader can complete
        // the immutable rows once, including a reader of the latest authority tick.
        private class FrameRows<T> : IReadOnlyList<T>
        {
            private readonly List<T> ready;
            private List<Func<T>> pending;
            private readonly Lazy<IReadOnlyList<T>> completed;
            internal FrameRows(bool defer)
            {
                if (defer) {
                    completed = new Lazy<IReadOnlyList<T>>(Build);
                } else ready = new List<T>();
            }
            internal bool Deferred => completed != null;
            internal void Add(T row) => ready.Add(row);
            internal void Capture(Func<T> build) => (pending ??= new List<Func<T>>()).Add(build);
            internal virtual void AddShared((FrameRows<T> Rows, int Index) source)
            {
                if (Deferred) Capture(() => source.Rows[source.Index]);
                else Add(source.Rows[source.Index]);
            }
            protected virtual IReadOnlyList<T> Build()
            {
                var result = new T[pending?.Count ?? 0];
                for (int i = 0; i < result.Length; i++) result[i] = pending[i]();
                pending = null;
                return Array.AsReadOnly(result);
            }
            internal IReadOnlyList<T> Read() => Deferred ? completed.Value : ready.AsReadOnly();
            internal virtual int CapturedCount => Deferred ? (pending?.Count ?? 0) : ready.Count;
            public int Count => Deferred ? completed.Value.Count : ready.Count;
            public T this[int index] => Deferred ? completed.Value[index] : ready[index];
            public IEnumerator<T> GetEnumerator() => Deferred
                ? completed.Value.GetEnumerator() : ready.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private struct EntityFrameRow
        {
            internal FrameRows<PlayableEntitySnapshot> SharedRows;
            internal int SharedIndex;
            internal int Id;
            internal PlayableOwner Owner;
            internal PlayableEntityKind Kind;
            internal NavPoint Position;
            internal int Health;
            internal bool Moving;
            internal int TargetId;
            internal double HullHeading;
            internal double TurretHeading;
            internal PlayableTacticalOrderSnapshot CurrentOrder;
            internal bool Upgraded;
            internal bool Held;
            internal NavigationOutcome NavigationOutcome;
            internal PlayableOrderStamp OrderStamp;
            internal PlayableSpatialCompletion Completion;
            internal NavLocation? Location;
            internal UnitQueueFrame Queue;
            internal double MaxHealth;
            internal double? ExactHealth;
            internal int WeaponSystemsLevel;
            internal int DefensiveSystemsLevel;
            internal double Altitude;
            internal double Pitch;
            internal double Roll;
            internal bool Departing;
            internal double WeaponPitch;
            internal int WeaponSequence;
            internal double WeaponReload;
            internal ShkvalCellsSnapshot ShkvalCells;
            internal double MaterializationProgress;
            internal double StasisRemaining;
            internal bool RocketLauncher;
            internal CerberusWeaponPose? Flames;
            internal BallisticPoint? IglaBeam;

            internal PlayableEntitySnapshot Build()
            {
                if (SharedRows != null) return SharedRows[SharedIndex];
                return new PlayableEntitySnapshot(
                    Id, Owner, Kind, Position, Health, Moving, TargetId, HullHeading, TurretHeading,
                    CurrentOrder, Upgraded, Held, NavigationOutcome, OrderStamp, Completion, Location,
                    Queue.Build(), MaxHealth, ExactHealth, WeaponSystemsLevel, DefensiveSystemsLevel,
                    Altitude, Pitch, Roll, Departing, WeaponPitch, WeaponSequence, WeaponReload,
                    ShkvalCells, MaterializationProgress, StasisRemaining, RocketLauncher, Flames,
                    IglaBeam);
            }
        }

        private sealed class EntityFrameRows : FrameRows<PlayableEntitySnapshot>
        {
            // Only staging pages are pooled. Readers can retain the completed frame indefinitely.
            private const int PageSize = 32;
            private List<EntityFrameRow[]> pages;
            private int captured;

            internal EntityFrameRows(bool defer) : base(defer) { }
            internal override int CapturedCount => Deferred ? captured : Count;

            internal void CaptureEntity(in EntityFrameRow row)
            {
                if (!Deferred) { Add(row.Build()); return; }
                int slot = captured % PageSize;
                if (slot == 0)
                {
                    pages ??= new List<EntityFrameRow[]>();
                    pages.Add(System.Buffers.ArrayPool<EntityFrameRow>.Shared.Rent(PageSize));
                }
                pages[captured / PageSize][slot] = row;
                captured++;
            }

            internal override void AddShared((FrameRows<PlayableEntitySnapshot> Rows, int Index) source)
            {
                CaptureEntity(new EntityFrameRow { SharedRows = source.Rows, SharedIndex = source.Index });
            }

            protected override IReadOnlyList<PlayableEntitySnapshot> Build()
            {
                if (captured == 0) return Array.Empty<PlayableEntitySnapshot>();
                try
                {
                    var result = new PlayableEntitySnapshot[captured];
                    for (int i = 0; i < result.Length; i++)
                        result[i] = pages[i / PageSize][i % PageSize].Build();
                    return Array.AsReadOnly(result);
                }
                finally
                {
                    foreach (var page in pages)
                        System.Buffers.ArrayPool<EntityFrameRow>.Shared.Return(page, clearArray: true);
                    pages = null;
                }
            }
        }

        private static void AddEntity(EntityFrameRows rows,
            int id, PlayableOwner owner, PlayableEntityKind kind, NavPoint position, int health, bool moving,
            int targetId, double hullHeading, double turretHeading,
            PlayableTacticalOrderSnapshot currentOrder=null, bool upgraded=false, bool held=false,
            NavigationOutcome navigationOutcome=NavigationOutcome.Idle, PlayableOrderStamp orderStamp=null,
            PlayableSpatialCompletion completion=null, NavLocation? location=null, UnitQueueFrame queue=default,
            double maxHealth=0, double? exactHealth=null, int weaponSystemsLevel=0, int defensiveSystemsLevel=0,
            double altitude=0, double pitch=0, double roll=0, bool departing=false, double weaponPitch=0,
            int weaponSequence=0, double weaponReload=0, ShkvalCellsSnapshot shkvalCells=null,
            double materializationProgress=1, double stasisRemaining=0, bool rocketLauncher=false,
            CerberusWeaponPose? flames=null, BallisticPoint? iglaBeam=null)
        {
            rows.CaptureEntity(new EntityFrameRow
            {
                Id = id, Owner = owner, Kind = kind, Position = position, Health = health,
                Moving = moving, TargetId = targetId, HullHeading = hullHeading,
                TurretHeading = turretHeading, CurrentOrder = currentOrder, Upgraded = upgraded,
                Held = held, NavigationOutcome = navigationOutcome, OrderStamp = orderStamp,
                Completion = completion, Location = location, Queue = queue, MaxHealth = maxHealth,
                ExactHealth = exactHealth, WeaponSystemsLevel = weaponSystemsLevel,
                DefensiveSystemsLevel = defensiveSystemsLevel, Altitude = altitude, Pitch = pitch,
                Roll = roll, Departing = departing, WeaponPitch = weaponPitch,
                WeaponSequence = weaponSequence, WeaponReload = weaponReload, ShkvalCells = shkvalCells,
                MaterializationProgress = materializationProgress, StasisRemaining = stasisRemaining,
                RocketLauncher = rocketLauncher, Flames = flames, IglaBeam = iglaBeam
            });
        }

        private static void AddBuilding(FrameRows<PlayableBuildingSnapshot> rows,
            int id, PlayableOwner owner, PlayableBuildingKind kind, NavPoint position, int health,
            double progress, int queueCount, double productionProgress, NavPoint rally, int siteId=0,
            int slotId=0, int parentId=0, ConstructionPhase phase=ConstructionPhase.Ready,
            string blockedReason=null, double heading=0, bool includePrivateState=true,
            PlayableProductionOrderSnapshot[] orders=null, bool repeat=false,
            PlayableBuildingLifecycleSnapshot lifecycle=null, PlayableRefineryUpgradeSnapshot upgrade=null,
            bool refineryUpgraded=false, PlayableEntityKind repeatKind=PlayableEntityKind.Tank,
            PlayableResearchOrderSnapshot[] research=null, bool hasRally=false, NavPoint? pendingRally=null,
            double? exactHealth=null, int launchPhase=0, double launchTime=0,
            PlayableEntityKind? launchKind=null, bool launchUpgraded=false, double maxHealth=0,
            PlayableTurretSnapshot turret=null, PlayableRepairTowerSnapshot healing=null,
            bool scientificCenterUpgraded=false, bool producerUpgraded=false, double systemFailureRemaining=0,
            double boostRemaining=0, bool boostRepairing=false, double? boostWorkRemaining=null,
            bool boostWearDamage=false, bool boostSelling=false, int concreteLevel=0, int weaponSystemsLevel=0)
        {
            PlayableBuildingSnapshot Build() => new PlayableBuildingSnapshot(
                id, owner, kind, position, health, progress, queueCount, productionProgress, rally, siteId,
                slotId, parentId, phase, blockedReason, heading, includePrivateState, orders, repeat,
                lifecycle, upgrade, refineryUpgraded, repeatKind, research, hasRally, pendingRally,
                exactHealth, launchPhase, launchTime, launchKind, launchUpgraded, maxHealth, turret, healing,
                scientificCenterUpgraded, producerUpgraded, systemFailureRemaining, boostRemaining,
                boostRepairing, boostWorkRemaining, boostWearDamage, boostSelling, concreteLevel,
                weaponSystemsLevel);
            if (rows.Deferred) rows.Capture(Build);
            else rows.Add(Build());
        }
    }
}
