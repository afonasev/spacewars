using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Read-only main-thread snapshot adapter. Route work is owned by the caller
    // and must run after this method has released the transport gate.
    public sealed partial class NavigationSession
    {
        public NavArrivalOccupancySnapshot CaptureArrivalOccupancySnapshot(
            IEnumerable<NavArrivalMember> movingGroupMembers = null)
        {
            using (EnterTransport()) {
                var moving = new HashSet<string>((movingGroupMembers ?? Array.Empty<NavArrivalMember>())
                    .Where(x => x != null).Select(x => x.EntityId + ":" + x.Incarnation), StringComparer.Ordinal);
                var rows = new List<NavArrivalOccupant>();
                foreach (var actor in Crowd.Units.OrderBy(x => x.Id)) {
                    var location = actor.Location;
                    bool movingMember = moving.Contains(actor.Id + ":" + actor.Incarnation) && actor.Moving && !actor.Held;
                    string identity = "actor:" + Generation + ":" + actor.Id + ":" + actor.Incarnation + ":" +
                        actor.MobilityRevision + ":" + actor.Team + ":" + Bit(actor.Position.X) + ":" + Bit(actor.Position.Z) + ":" +
                        Bit(actor.Radius) + ":" + actor.Moving + ":" + actor.Held;
                    rows.Add(new NavArrivalOccupant(NavArrivalOccupantKind.Actor, actor.Id, actor.Incarnation,
                        location, actor.Radius, !movingMember, identity));

                    if (!reservations.TryGetValue(actor.Id, out var reserved)) continue;
                    if (pending.TryGetValue(actor.Id, out var request) && request.Request > 0) {
                        RequireReservationTarget(actor, reserved, request.Goal, request.GoalLocation, false, "pending request");
                        var memberIdentity = request.MemberIdentity;
                        string requestIdentity = "pending:" + NavContract.Digest(w => {
                            w.Write("arrival-pending-v1"); w.Write(request.Session); w.Write(request.Request); w.Write(request.Entity);
                            w.Write(request.Order); w.Write(request.ProfileId); w.Write(request.ProfileRevision);
                            w.Write(request.Geometry.Revision); w.Write(request.BaseGeometry.Revision);
                            NavContract.Point(w, request.Start); WriteLocation(w, request.StartLocation);
                            NavContract.Point(w, request.Goal); WriteLocation(w, request.GoalLocation);
                            w.Write(request.HeldIdentity ?? ""); w.Write(memberIdentity != null);
                            if (memberIdentity != null) {
                                w.Write(memberIdentity.Incarnation); w.Write(memberIdentity.MobilityRevision);
                                w.Write(memberIdentity.GroupId); w.Write(memberIdentity.OrderRevision);
                                w.Write(memberIdentity.MembershipRevision);
                            }
                        });
                        rows.Add(new NavArrivalOccupant(NavArrivalOccupantKind.PendingReservation, actor.Id,
                            actor.Incarnation, request.GoalLocation.Value, actor.Radius, true, requestIdentity,
                            NavArrivalReservationKind.PendingRequest, request.Request));
                        continue;
                    }
                    if (!actor.Moving || actor.Held || actor.InstalledRequest < 1) continue;
                    RequireReservationTarget(actor, reserved, actor.Goal, actor.GoalLocation, true, "installed execution");
                    installedExecutions.TryGetValue(actor.Id, out var execution);
                    string corridor = execution?.Corridor?.StableKey ?? "retained-target-only";
                    string installedIdentity = "installed:" + NavContract.Digest(w => {
                        w.Write("arrival-installed-v1"); w.Write(Generation); w.Write(actor.Id); w.Write(actor.Incarnation);
                        w.Write(actor.InstalledGroupId); w.Write(actor.InstalledRevision); w.Write(actor.InstalledRequest);
                        w.Write(actor.InstalledOrder); w.Write(corridor); NavContract.Point(w, actor.Goal);
                        WriteLocation(w, actor.GoalLocation);
                    });
                    rows.Add(new NavArrivalOccupant(NavArrivalOccupantKind.InstalledReservation, actor.Id,
                        actor.Incarnation, actor.GoalLocation.Value, actor.Radius, true, installedIdentity,
                        NavArrivalReservationKind.InstalledExecution, 0, actor.InstalledRequest,
                        actor.GoalLocation.Value));
                }
                return new NavArrivalOccupancySnapshot(Generation, rows);
            }
        }

        private static string Bit(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static void WriteLocation(BinaryWriter writer, NavLocation? location)
        {
            writer.Write(location.HasValue);
            if (location.HasValue) NavContract.Location(writer, location.Value);
        }
        private static void RequireReservationTarget(NavUnit actor, NavPoint reserved, NavPoint requestGoal,
            NavLocation? requestLocation, bool compareActorTarget, string context)
        {
            if (!requestLocation.HasValue || !requestGoal.Equals(reserved) ||
                !requestLocation.Value.Position.Equals(reserved) || compareActorTarget &&
                (!actor.GoalLocation.HasValue || !actor.GoalLocation.Value.Equals(requestLocation.Value) || !actor.Goal.Equals(reserved)))
                throw new InvalidOperationException("Inconsistent typed retained navigation reservation for " + context + ".");
        }
    }
}
