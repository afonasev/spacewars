using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Deterministic geometric policy. The authority owns all mutable progress;
    // the crowd still chooses and validates the actual step on its installed route.
    internal static class GroupMarch
    {
        internal static double Length(NavCorridorDescriptor corridor)
        {
            double length=0;foreach(var leg in corridor.Legs){
                double dx=leg.To.Position.X-leg.From.Position.X,dz=leg.To.Position.Z-leg.From.Position.Z;
                length+=Math.Sqrt(dx*dx+dz*dz);
            }
            return length;
        }
        internal static double Project(NavCorridorDescriptor corridor,NavLocation position,double previous)
            =>Project(corridor,position,previous,out _);
        private static double Project(NavCorridorDescriptor corridor,NavLocation position,double previous,out double traveled)
        {
            traveled=0;
            double bestTravel=0,bestDistance=double.PositiveInfinity;
            foreach(var leg in corridor.Legs){
                double dx=leg.To.Position.X-leg.From.Position.X,dz=leg.To.Position.Z-leg.From.Position.Z;
                double length=Math.Sqrt(dx*dx+dz*dz);
                if(length>0&&leg.From.SurfaceId==position.SurfaceId){
                    double fraction=Math.Max(0,Math.Min(1,((position.Position.X-leg.From.Position.X)*dx+
                        (position.Position.Z-leg.From.Position.Z)*dz)/(length*length)));
                    double x=leg.From.Position.X+fraction*dx,z=leg.From.Position.Z+fraction*dz;
                    double distance=(position.Position.X-x)*(position.Position.X-x)+(position.Position.Z-z)*(position.Position.Z-z);
                    if(distance<bestDistance){bestDistance=distance;bestTravel=traveled+fraction*length;}
                }
                if(leg.To.SurfaceId==position.SurfaceId){
                    double ex=position.Position.X-leg.To.Position.X,ez=position.Position.Z-leg.To.Position.Z;
                    double distance=ex*ex+ez*ez;
                    if(distance<bestDistance){bestDistance=distance;bestTravel=traveled+length;}
                }
                traveled+=length;
            }
            // All members share zero at their assigned terminal. This avoids
            // comparing distances traveled from unrelated starting positions.
            // Detours may be off the centerline; never move the cursor backwards.
            return bestDistance==double.PositiveInfinity?(double.IsNegativeInfinity(previous)?-traveled:previous):
                Math.Max(previous,Math.Min(0,bestTravel-traveled));
        }

        internal static double ProjectFromOrigin(NavCorridorDescriptor corridor,NavLocation position,double previous,double offset)
        {
            double remaining=Project(corridor,position,double.NegativeInfinity,out double length);
            return Math.Max(previous,offset+Math.Max(0,remaining+length));
        }

        // Shared by movement-only and projected result+movement preparation.
        // Actor rows and installed/group inputs are already private projections.
        internal static NavigationAuthorityGroup PrepareOwned(NavPreparedCrowdStep frame,NavigationAuthorityGroup source,NavigationInstalledExecutionOwner[] installed,bool[] detached,NavigationCommandSlots<int,long> memberGroups,double configuredStretch,int configuredRevision,double dt,INavStorageReservation reservation,bool recast=false,NavigationRetentionBudget projectionBudget=null)
        {
            using var phaseTiming=Spacewars.Simulation.PerformanceProfile.Measure(Spacewars.Simulation.ProfileStage.NavMarch);
            var data=source.Data;bool marching=data.Kind==PlayableCommandKind.Move||data.Kind==PlayableCommandKind.AttackMove;
            // An accepted Attack/Follow route still owns a physical Recast
            // cursor and sweep. Group pacing is a separate command policy.
            if(data.MarchVersion!=1&&data.MarchVersion!=2||!marching&&!recast)return null;
            int count=source.Members.Count;using(NavStorageScratch.Reserve(reservation,checked(512L+80L*count))){
                var sidecars=new RecastMemberProgress[count];var progress=new double[count];var offsets=new double[count];var requests=new long[count];var active=new bool[count];var rows=new int[count];
                double anchor=0,stretch=data.MarchProfileRevision==configuredRevision?data.MarchStretch:configuredStretch;bool initialized=false;
                for(int i=0;i<count;i++){
                    var member=((NavigationAuthorityMember)source.Members[i]).Data;sidecars[i]=member.Recast;progress[i]=member.Progress;offsets[i]=member.ProgressOffset;requests[i]=member.ProgressRequest;int row=frame.Find(member.Entity);
                    if(row<0||!InstalledActive(frame.After[row],source,member,installed[row],detached[row],memberGroups,includeReleased:recast))continue;
                    var actor=frame.After[row];var execution=installed[row].Corridor.Corridor;
                    if(requests[i]!=execution.Request){if(data.MarchVersion==2)offsets[i]=progress[i];else progress[i]=double.NegativeInfinity;requests[i]=execution.Request;initialized=false;}
                    progress[i]=data.MarchVersion==2?ProjectFromOrigin(execution,actor.Location,progress[i],offsets[i]):Project(execution,actor.Location,progress[i]);
                    // Contact releases only formation pacing. The still-owned
                    // installed route must keep its physical cursor and receipt.
                    if(!member.FormationReleased){active[i]=true;rows[i]=row;}
                    if(recast&&execution.RecastPlan!=null && !(sidecars[i].Request==execution.Request && sidecars[i].Phase==RecastMovementPhase.UnmappedProduction)){var typed=installed[row].Corridor.TypedRoute;var state=sidecars[i].Request==execution.Request?sidecars[i].ToStep():NavRecastIntervals.Initial(typed);
                        // Only the movement owner supplies this ledger. Hooked or other callers
                        // retain the original connection order, including projected Ready work.
                        bool nearestPrecheck=projectionBudget!=null&&reservation is INavStorageLedgerIdentity ledger&&ReferenceEquals(ledger.Identity,projectionBudget)&&frame.Reservation is INavStorageLedgerIdentity frameLedger&&ReferenceEquals(frameLedger.Identity,projectionBudget)&&projectionBudget.AllocationAdmission==null;
                        if(!NavRecastIntervals.Advance(typed,state,actor.Location,actor.Position,reservation,out var receipt,frame.Working,actor.Id,nearestPrecheck:nearestPrecheck))throw new ArgumentException("Recast current physical binding is invalid. actor="+actor.Id+" group="+data.GroupId+" request="+execution.Request+" sidecarRequest="+sidecars[i].Request+" phase="+state.Phase+" cursor="+state.Cursor+" legs="+typed.Legs.Count+" cursorSurface="+(state.Cursor>=0&&state.Cursor<typed.Legs.Count?typed.Legs[state.Cursor].From.SurfaceId:"invalid")+" origin="+typed.Legs[0].From.SurfaceId+":"+typed.Legs[0].From.Position.X+","+typed.Legs[0].From.Position.Z+" "+frame.Working.DescribeRecastBinding(actor.Id,actor.Location,actor.Position,typed.RecastPlan.Spine.Radius));
                        // Partition traces can cross a seam at the current XZ.
                        // The typed cursor and physical surface belong to the same receipt.
                        if(receipt.Verified&&receipt.PortalCount>0&&receipt.From.Equals(receipt.To)&&receipt.ToSurface!=actor.Location.SurfaceId)
                            actor.Location=frame.Working.OwnedLocation(new NavLocation(actor.Position,receipt.ToSurface));
                        sidecars[i]=RecastMemberProgress.FromStep(execution.Request,receipt.After);frame.BindRecast(row,typed,receipt.After);}
                }
                NavigationAuthorityGroup.Values MarchValues()=>new NavigationAuthorityGroup.Values(data.WorldGeneration,data.GroupId,data.OrderRevision,data.CommandSequence,data.IssuedTick,data.MembershipRevision,data.JobId,data.ActionId,data.MarchVersion,configuredRevision,data.TeamId,data.TargetId,stretch,anchor,initialized,data.Origin,data.Kind,data.OriginalGoal,data.HasLocation,data.Location);
                if (marching && count > 1 && frame.SourceCrowd.LeadsFormation != null)
                {
                    double leadSpeed = double.PositiveInfinity;
                    for (int i = 0; i < count; i++)
                        if (active[i] && frame.SourceCrowd.LeadsFormation(frame.After[rows[i]].Id))
                            leadSpeed = Math.Min(leadSpeed, frame.After[rows[i]].Speed);
                    if (!double.IsPositiveInfinity(leadSpeed))
                        for (int i = 0; i < count; i++)
                            if (active[i])
                                frame.Preferred[rows[i]] = Math.Min(frame.After[rows[i]].Speed, leadSpeed);
                }
                var values=MarchValues();
                var next=NavigationAuthorityGroup.CopyWithMarchBatchOwned(source,values,i=>{
                    var before=(NavigationAuthorityMember)source.Members[i];var d=before.Data;
                    var changed=new NavigationAuthorityMember.Values(d.Entity,d.Slot,d.Incarnation,d.OrderRevision,d.MobilityRevision,d.Phase,d.Goal,d.Location,d.HasLocation,d.HasGoal,d.FormationReleased,d.Branch,d.Reservation,d.ContactTick,requests[i],progress[i],d.ProgressVersion,offsets[i],sidecars[i]);
                    return changed.BitwiseSame(d)?null:NavigationAuthorityMember.CopyWithValuesOwned(before,changed,reservation);
                },reservation);
                if(recast&&marching)
                    for(int i=0;i<count;i++)
                        if(active[i]&&frame.RecastRoutes[rows[i]]!=null)frame.Formation[rows[i]].SingleMoveDirect=true;
                return next;
            }
        }
        // Apply actual accepted sweep receipts from the SAME private crowd frame.
        // Neither global routeIndex nor a worker-side cursor grants progress.
        internal static NavigationAuthorityGroup CompleteRecastOwned(NavPreparedCrowdStep frame,NavigationAuthorityGroup source,INavStorageReservation r)
        {
            using var phaseTiming=Spacewars.Simulation.PerformanceProfile.Measure(Spacewars.Simulation.ProfileStage.NavMarchComplete);
            return NavigationAuthorityGroup.CopyWithMarchBatchOwned(source,source.Data,i=>{
                var member=(NavigationAuthorityMember)source.Members[i];var d=member.Data;int row=frame.Find(d.Entity);
                if(row<0||frame.RecastRoutes[row]==null||(!frame.RecastReceipts[row].Verified&&!frame.RecastReceipts[row].ProductionUnmapped))return null;
                var route=frame.RecastRoutes[row];if(d.ProgressRequest!=route.Subscription.RequestSequence)throw new ArgumentException("Recast receipt request changed.");
                var progress=RecastMemberProgress.FromStep(d.ProgressRequest,frame.RecastReceipts[row].After);if(progress.Equals(d.Recast))return null;
                var values=new NavigationAuthorityMember.Values(d.Entity,d.Slot,d.Incarnation,d.OrderRevision,d.MobilityRevision,d.Phase,d.Goal,d.Location,d.HasLocation,d.HasGoal,d.FormationReleased,d.Branch,d.Reservation,d.ContactTick,d.ProgressRequest,d.Progress,d.ProgressVersion,d.ProgressOffset,progress);
                return NavigationAuthorityMember.CopyWithValuesOwned(member,values,r);
            },r);
        }
        // Recast members have distinct origin connectors and terminal branches.
        // Compare physical remaining geometry, never their unrelated traveled totals.
        // Include displacement from the current cursor: projection onto an endpoint
        // must not report zero remaining while the body is still far from its slot.
        internal static double RemainingCoordinate(NavUnit actor)
        {
            var at=actor.Position;double remaining=0;
            for(int i=actor.routeIndex;i<actor.route.Length;i++){
                var next=actor.route[i];double dx=next.X-at.X,dz=next.Z-at.Z;
                remaining+=Math.Sqrt(dx*dx+dz*dz);at=next;
            }
            return -remaining;
        }
        internal static bool InstalledActive(NavUnit actor,NavigationAuthorityGroup group,NavigationAuthorityMember.Values member,NavigationInstalledExecutionOwner owner,bool detached,NavigationCommandSlots<int,long> memberGroups,bool includeReleased=false)
        {
            if(owner==null||detached||member.FormationReleased&&!includeReleased||!actor.Moving||actor.Held)return false;var d=owner.Corridor.Corridor;
            if(actor.InstalledGroupId!=d.Group||actor.InstalledRevision!=d.ActivationRevision||actor.InstalledRequest!=d.Request||actor.InstalledOrder!=d.Order||actor.Incarnation!=d.Incarnation||actor.MobilityRevision!=d.Mobility||!memberGroups.TryGetValue(actor.Id,out var g)||g!=d.Group||group.Data.OrderRevision!=d.RootRevision||member.OrderRevision!=d.ActivationRevision||owner.Route.Count!=actor.route.Length)return false;
            if (ReferenceEquals(owner.Route.ReadOnlyBacking, actor.route)) return true;
            for(int i=0;i<actor.route.Length;i++)if(!owner.Route[i].Equals(actor.route[i]))return false;return true;
        }
        internal static bool ShareMarchSection(NavRecastMemberPlan first,RecastMemberProgress firstProgress,
            NavRecastMemberPlan second,RecastMemberProgress secondProgress)
        {
            if(firstProgress.Phase!=RecastMovementPhase.Shared||secondProgress.Phase!=RecastMovementPhase.Shared||
                first.Spine.Binding!=second.Spine.Binding)return false;
            int start=Math.Max(first.Join,second.Join),end=Math.Min(first.Exit,second.Exit);
            int firstLeg=first.Join+firstProgress.Cursor-first.OriginLegs;
            int secondLeg=second.Join+secondProgress.Cursor-second.OriginLegs;
            return firstLeg>=start&&firstLeg<end&&secondLeg>=start&&secondLeg<end;
        }

        internal static double Anchor(double previous,bool initialized,double slowest,double dt,double minimumProgress)
            =>initialized?Math.Min(minimumProgress,previous+slowest*dt):minimumProgress;

        internal static double MaxSpeed(double actorSpeed,double slowest,double progress,double anchor,double stretch,double dt)
            =>Math.Min(actorSpeed,Math.Min(slowest,Math.Max(0,(anchor+stretch-progress)/dt)));
    }

    public sealed partial class NavigationSession
    {
        private bool marchConfigured;
        private double marchStretch;
        private int marchProfileRevision;

        private static void ValidateMarchConfiguration(double maximumStretch,int profileRevision)
        {
            var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="groupMarch.maximumStretch");
            double steps=(maximumStretch-field.Minimum)/field.Step;
            if(double.IsNaN(maximumStretch)||double.IsInfinity(maximumStretch)||
                maximumStretch<field.Minimum||maximumStretch>field.Maximum||
                Math.Abs(steps-Math.Round(steps))>1e-8||profileRevision<1)
                throw new ArgumentException("Invalid group march profile.");
        }
        public void ConfigureMarch(double maximumStretch,int profileRevision)
        {
            ValidateMarchConfiguration(maximumStretch,profileRevision);
            lock(transportGate){authorityOperationIdentity=checked(authorityOperationIdentity+1);marchStretch=maximumStretch;marchProfileRevision=profileRevision;marchConfigured=true;}
        }

        private void ValidateMarchCheckpoint()
        {
            foreach(var group in groups.Values){
                if(group.MarchVersion!=1&&group.MarchVersion!=2)continue;
                double longestBoundRoute=0;
                foreach(var member in group.Members){
                    if(member.ProgressRequest==0){
                        if(group.MarchVersion==1?member.Progress!=0:member.Progress!=member.ProgressOffset)throw new ArgumentException("Unbound march progress.");
                        continue;
                    }
                    if(!installedExecutions.TryGetValue(member.Entity,out var installed)||
                        installed.Corridor.Group!=group.GroupId||
                        installed.Corridor.Request!=member.ProgressRequest||
                        installed.Corridor.Incarnation!=member.Incarnation||
                        installed.Corridor.ActivationRevision!=member.OrderRevision||
                        (group.MarchVersion==1?member.Progress < -GroupMarch.Length(installed.Corridor)-1e-6||member.Progress>0:member.Progress<member.ProgressOffset||member.Progress>member.ProgressOffset+GroupMarch.Length(installed.Corridor)+1e-6))
                        throw new ArgumentException("Invalid installed march progress binding.");
                    if(member.Recast.Request>0){var p=installed.Corridor.RecastPlan;if(p==null||member.Recast.Request!=installed.Corridor.Request||member.Recast.Cursor<0||member.Recast.Cursor>=installed.StorageOwner.Corridor.TypedRoute.Legs.Count||member.Recast.Station<p.Spine.Station(p.Join)||member.Recast.HighWater>p.Spine.Station(p.Exit)+1e-6)throw new ArgumentException("Invalid saved Recast phase/station binding.");}
                    longestBoundRoute=Math.Max(longestBoundRoute,GroupMarch.Length(installed.Corridor));
                }
                if(group.MarchVersion==1&&group.MarchInitialized&&(longestBoundRoute<=0||group.MarchAnchor < -longestBoundRoute-1e-6))
                    throw new ArgumentException("March anchor is outside installed route geometry.");
            }
        }

        private void PrepareMarch(double dt)
        {
            Crowd.ClearPreferredMotion();
            if(!marchConfigured)return;
            foreach(var group in groups.Values.OrderBy(g=>g.GroupId)){
                if(group.MarchVersion!=1&&group.MarchVersion!=2||(group.Kind!=PlayableCommandKind.Move&&group.Kind!=PlayableCommandKind.AttackMove))continue;
                if(group.MarchProfileRevision!=marchProfileRevision){
                    group.MarchProfileRevision=marchProfileRevision;group.MarchStretch=marchStretch;
                }
                foreach(var member in group.Members){
                    if(member.FormationReleased||!Crowd.TryGet(member.Entity,out var actor)||!actor.Moving||actor.Held||
                        !TryActiveInstalledExecution(member.Entity,out var execution))continue;
                    if(member.ProgressRequest!=execution.Corridor.Request){
                        if(group.MarchVersion==2)member.ProgressOffset=member.Progress;else member.Progress=double.NegativeInfinity;member.ProgressRequest=execution.Corridor.Request;
                        group.MarchInitialized=false;
                    }
                    member.Progress=group.MarchVersion==2?GroupMarch.ProjectFromOrigin(execution.Corridor,actor.Location,member.Progress,member.ProgressOffset):GroupMarch.Project(execution.Corridor,actor.Location,member.Progress);
                }
                group.MarchAnchor = 0;
                group.MarchInitialized = false;
            }
        }
    }
}
