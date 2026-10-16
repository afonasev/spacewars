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
                    bool movingMember = moving.Contains(actor.Id + ":" + actor.Incarnation) && !actor.Held;
                    string identity = "actor:" + Generation + ":" + actor.Id + ":" + actor.Incarnation + ":" +
                        actor.MobilityRevision + ":" + actor.Team + ":" + Bit(actor.Position.X) + ":" + Bit(actor.Position.Z) + ":" +
                        Bit(actor.Radius) + ":" + actor.Moving + ":" + actor.Held;
                    rows.Add(new NavArrivalOccupant(NavArrivalOccupantKind.Actor, actor.Id, actor.Incarnation,
                        location, actor.Radius, !movingMember, identity));

                    foreach(var row in arrivalLedger.Values.Where(r=>r.Entity==actor.Id))
                        rows.Add(new NavArrivalOccupant(row.State==NavArrivalReservationKind.PendingRequest?NavArrivalOccupantKind.PendingReservation:NavArrivalOccupantKind.InstalledReservation,
                            actor.Id,row.Incarnation,row.Endpoint,row.Radius,true,"ledger:"+row.World+":"+row.Id+":"+row.Proof,row.State,
                            row.State==NavArrivalReservationKind.PendingRequest?row.Request:0,
                            row.State==NavArrivalReservationKind.InstalledExecution?row.Request:0,
                            row.State==NavArrivalReservationKind.InstalledExecution?(NavLocation?)row.Endpoint:null));
                    if(pending.TryGetValue(actor.Id,out var request)&&!arrivalLedger.ContainsKey(request.Request)) {
                        if(reservations.TryGetValue(actor.Id,out var reserved))RequireReservationTarget(actor,reserved,request.Goal,request.GoalLocation,false,"pending request");
                        if(!request.GoalLocation.HasValue)throw new InvalidOperationException("Missing typed pending target.");
                        rows.Add(new NavArrivalOccupant(NavArrivalOccupantKind.PendingReservation,actor.Id,actor.Incarnation,
                            request.GoalLocation.Value,request.Profile.Radius,true,"pending:"+NavContract.Digest(w=>{
                                w.Write("arrival-pending-v1");w.Write(request.Session);w.Write(request.Request);w.Write(request.Entity);w.Write(request.Order);
                                w.Write(request.ProfileId);w.Write(request.ProfileRevision);w.Write(request.Geometry.Revision);w.Write(request.BaseGeometry.Revision);
                                NavContract.Point(w,request.Start);WriteLocation(w,request.StartLocation);NavContract.Point(w,request.Goal);WriteLocation(w,request.GoalLocation);
                                w.Write(request.HeldIdentity??"");var i=request.MemberIdentity;w.Write(i!=null);
                                if(i!=null){w.Write(i.Incarnation);w.Write(i.MobilityRevision);w.Write(i.GroupId);w.Write(i.OrderRevision);w.Write(i.MembershipRevision);}
                            }),
                            NavArrivalReservationKind.PendingRequest,request.Request));
                    }
                    if(actor.Moving&&!actor.Held&&actor.InstalledRequest>0&&!arrivalLedger.ContainsKey(actor.InstalledRequest)) {
                        if(!pending.ContainsKey(actor.Id)&&reservations.TryGetValue(actor.Id,out var retained))RequireReservationTarget(actor,retained,actor.Goal,actor.GoalLocation,true,"installed execution");
                        if(!actor.GoalLocation.HasValue)throw new InvalidOperationException("Missing typed installed target.");
                        rows.Add(new NavArrivalOccupant(NavArrivalOccupantKind.InstalledReservation,actor.Id,actor.Incarnation,
                            actor.GoalLocation.Value,actor.Radius,true,"installed:"+NavContract.Digest(w=>{
                                w.Write("arrival-installed-v1");w.Write(Generation);w.Write(actor.Id);w.Write(actor.Incarnation);w.Write(actor.InstalledGroupId);
                                w.Write(actor.InstalledRevision);w.Write(actor.InstalledRequest);w.Write(actor.InstalledOrder);
                                w.Write(installedExecutions.TryGetValue(actor.Id,out var e)?e.Corridor.StableKey:"retained-target-only");NavContract.Point(w,actor.Goal);WriteLocation(w,actor.GoalLocation);
                            }),
                            NavArrivalReservationKind.InstalledExecution,0,actor.InstalledRequest,actor.GoalLocation.Value));
                    }
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
