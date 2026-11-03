using System;
using System.Collections.Generic;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal static class RecastFormationGuide
    {
        internal readonly struct Layout
        {
            internal readonly NavPoint Entry,Exit,Direction,Normal;
            internal readonly double Length,Low,High,Pitch,Radius,BankReach;
            internal readonly int Lanes;
            internal readonly bool Directional;
            internal Layout(NavPoint a,NavPoint b,NavPoint d,double length,double low,double high,double pitch,double radius,bool preferDirectional=true,double bankReach=0)
            {Entry=a;Exit=b;Direction=d;Normal=new NavPoint(-d.Z,d.X);Length=length;Low=low;High=high;Pitch=pitch;Radius=radius;Lanes=Math.Max(1,1+(int)Math.Floor((high-low)/pitch));Directional=preferDirectional&&Math.Min(pitch,((low+high)/2-radius-low)/Lanes)>=2*radius;BankReach=Directional?bankReach:0;}
            internal NavPoint Point(double station,int lane)
            {
                double low=Low,high=High,lateralPitch=Pitch;
                // Opposite accepted axes must not steer into the same lanes.
                // Reserve one body radius at the physical middle for each
                // direction. The diameter is a geometric invariant, not tuning.
                // Preserve frozen lane count and longitudinal role pitch; use
                // a directional half only when every lane still fits safely.
                double middle=(Low+High)/2,rightHigh=middle-Radius;
                // Give each physical column one cell, with its center inside
                // the cell rather than exactly on the eroded support boundary.
                // A full diameter per cell proves the unchanged lane capacity.
                double directionalPitch=Math.Min(Pitch,(rightHigh-low)/Lanes);
                if(Directional){
                    high=rightHigh;lateralPitch=directionalPitch;
                }
                double offset=(low+high)/2+(lane-(Lanes-1)/2.0)*lateralPitch;
                return new NavPoint(Entry.X+Direction.X*station+Normal.X*offset,Entry.Z+Direction.Z*station+Normal.Z*offset);
            }
            internal double Station(NavPoint p)=>RecastFormationMotion.Station(p,Entry,Direction);
            internal NavPoint BankPoint(double station,NavPoint current)
            {
                double high=Directional?(Low+High)/2-Radius:High;
                double lateral=Math.Max(Low,Math.Min(high,RecastFormationMotion.Station(current,Entry,Normal)));
                return new NavPoint(Entry.X+Direction.X*station+Normal.X*lateral,Entry.Z+Direction.Z*station+Normal.Z*lateral);
            }
        }
        internal static int Portal(NavTypedRoute route,string id){for(int i=0;i<route.Legs.Count;i++)if(route.Legs[i].Kind==NavTypedLegKind.Portal&&route.Legs[i].PortalId==id)return i;return -1;}
        // Seam endpoints provide asymmetric clearance. Every lane is then
        // certified by the SAME full provider trace, including exclusions.
        internal static double AvailableBankReach(NavTypedRoute route,string entry,string exit,double requested)
        {
            int first=Portal(route,entry);if(requested<=0||!NavRecastSpine.Finite(requested)||!RecastFormationMotion.Axis(route,first,out var a,out _,out var d,out double length))return 0;
            double radius=route.RecastPlan.Spine.Radius;
            // Two footprint radii separate the corridor's last body center
            // from the immutable origin/terminal fan's nearest body center.
            // This is clearance geometry, not a new configurable distance.
            double origin=-RecastFormationMotion.Station(route.Subscription.Origin.Position,a,d)-2*radius;
            double goal=RecastFormationMotion.Station(route.Subscription.AssignedEndpoint.Position,a,d)-length-2*radius;
            return Math.Max(0,Math.Min(requested,Math.Min(origin,goal)));
        }
        internal static bool TryLayout(NavTypedRoute route,string entry,string exit,double pitch,INavStorageReservation r,out Layout layout,double bankReach=0)
        {
            layout=default;int first=Portal(route,entry),last=Portal(route,exit);
            if(first<0||last<=first||!(route.Legs[first].RecastAuthorityProvider is PartitionNavigationProvider provider)||pitch<2*route.RecastPlan.Spine.Radius)return false;
            NavStorageAdmission.RequireOwned(r,route);NavStorageAdmission.RequireOwned(r,((INavStorageFacade)provider).StorageRoot);
            if(provider.Binding!=route.RecastPlan.Spine.ProviderBinding||(provider.HoldBinding??"")!=route.RecastPlan.Spine.KnowledgeBinding)throw new ArgumentException("Formation provider/knowledge binding changed.");
            foreach(var leg in route.Legs)leg.RequireCanonicalProviderBinding(provider,r);
            int next=-1;for(int i=first+1;i<route.Legs.Count;i++)if(route.Legs[i].Kind==NavTypedLegKind.Portal){next=i;break;}if(next!=last)return false;
            if(!RecastFormationMotion.Axis(route,first,out var a,out var b,out var d,out double length))return false;
            if(!RecastFormationMotion.Bounds(route,first,a,d,out double low,out double high,out double radius))return false;
            bankReach=AvailableBankReach(route,entry,exit,bankReach);
            var candidate=new Layout(a,b,d,length,low,high,pitch,radius,true,bankReach);
            bool Qualify(Layout value){
                for(int lane=0;lane<value.Lanes;lane++){
                    var from=new NavLocation(value.Point(-radius-value.BankReach,lane),route.Legs[first].From.SurfaceId);
                    if(!Trace(provider,route,first,last,from,value.Point(length+radius+value.BankReach,lane),r))return false;
                }
                return true;
            }
            // A preferred half cannot remove a valid full-width/HOLD route.
            bool qualified=Qualify(candidate);
            if(!qualified&&candidate.BankReach>0){
                // An unavailable bank connector cannot remove a valid bridge or
                // HOLD-aware around path. Retain the certified original fallback.
                candidate=new Layout(a,b,d,length,low,high,pitch,radius);
                qualified=Qualify(candidate);
            }
            if(!qualified){
                if(!candidate.Directional)return false;
                candidate=new Layout(a,b,d,length,low,high,pitch,radius,false);
                if(!Qualify(candidate))return false;
            }
            layout=candidate;return true;
        }
        private static bool Trace(PartitionNavigationProvider provider,NavTypedRoute route,int first,int last,NavLocation from,NavPoint to,INavStorageReservation r)
        {
            var clearance=new NavClearanceProfile("ground",1,"ground",(int)route.Subscription.MobilityRevision,route.RecastPlan.Spine.Radius);
            if(!provider.TryTraceOwned(from,to,clearance,out var reached,out var trace,r))return false;
            try{int next=first;foreach(var leg in trace)if(leg.Kind==NavTypedLegKind.Portal){if(next>last||route.Legs[next].PortalId!=leg.PortalId||route.Legs[next].From.SurfaceId!=leg.From.SurfaceId||route.Legs[next].To.SurfaceId!=leg.To.SurfaceId)return false;do{next++;}while(next<=last&&route.Legs[next].Kind!=NavTypedLegKind.Portal);}return next>last&&reached.SurfaceId==route.Legs[last].To.SurfaceId;}
            finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
        }
        internal static NavFormationFrameHint BankApproach(NavLocation actual,string bank,int portal,Layout layout,double frontier,int role=0,int firstRow=0,int futureRows=1,double arrivalTolerance=0,bool acceptedPreEntry=false)
        {
            frontier-=(role-firstRow)*layout.Pitch;
            if(role<firstRow||role>=firstRow+futureRows||(actual.SurfaceId!=bank&&!acceptedPreEntry)||layout.Station(actual.Position)>=frontier-arrivalTolerance)return new NavFormationFrameHint{Flags=NavFormationFrameHint.Wait};
            // The motor retains its accepted route target. Only its actual own
            // forward sweep is capped; no temporary future lane point is parked.
            return new NavFormationFrameHint{Flags=(byte)(NavFormationFrameHint.Bound|(actual.SurfaceId!=bank?NavFormationFrameHint.PreEntry:0)),TargetLeg=portal,ForwardLimit=frontier};
        }
        internal static bool AcceptedPreEntry(NavTypedRoute route,NavRecastStepState state,NavLocation actual,int portal)
        {return route!=null&&route.RecastPlan!=null&&state.Phase>=0&&state.Phase<2&&state.Cursor>=0&&state.Cursor<portal&&portal<route.Legs.Count&&route.Legs[portal].Kind==NavTypedLegKind.Portal&&route.Legs[state.Cursor].From.SurfaceId==actual.SurfaceId;}
        internal static double BankFrontier(Layout layout,double rear)=>Math.Min(-layout.Radius,rear-layout.Pitch);
        internal static NavFormationFrameHint StagingApproach(NavPreparedCrowdStep frame,int row,NavTypedRoute route,int portal,Layout layout,double frontier,int role,int lane,int firstRow,int futureRows,double pace,double dt)
        {
            var actor=frame.After[row];var state=frame.RecastStates[row];
            if(!ReferenceEquals(frame.RecastRoutes[row],route)||!AcceptedPreEntry(route,state,actor.Location,portal))return new NavFormationFrameHint{Flags=NavFormationFrameHint.Wait};
            string bank=route.Legs[portal].From.SurfaceId;
            var hint=BankApproach(actor.Location,bank,portal,layout,frontier,role,firstRow,futureRows,frame.Profile.ArrivalTolerance,true);
            if((hint.Flags&NavFormationFrameHint.Wait)!=0)return hint;
            hint.Flags|=NavFormationFrameHint.PreEntry;
            if(DirectionalAt(layout,route,portal,actor.Location))hint.Flags|=NavFormationFrameHint.Directional;
            if(actor.Location.SurfaceId==bank){
                double station=layout.Station(actor.Position);
                double targetStation=Math.Min(hint.ForwardLimit,station+pace*Math.Max(dt,frame.Profile.CrowdLookaheadSeconds));
                double lateral=RecastFormationMotion.Station(actor.Position,layout.Entry,layout.Normal);
                var target=new NavPoint(layout.Entry.X+layout.Direction.X*targetStation+layout.Normal.X*lateral,layout.Entry.Z+layout.Direction.Z*targetStation+layout.Normal.Z*lateral);
                // Outside the physical mouth on our own side, an axial
                // forecast heads for the shoreline. Follow a point already on
                // this actor's installed bank route when it approaches the
                // assigned lane within this staged row's existing frontier.
                if(layout.Directional && lateral<layout.Low){
                    double laneSide=RecastFormationMotion.Station(layout.Point(station,lane),layout.Entry,layout.Normal);
                    double upper=(layout.Low+layout.High)/2-layout.Radius;
                    int chosen=-1;
                    double error=Math.Abs(laneSide-lateral);
                    int first=NavRecastIntervals.FirstTargetSurface(route,state);
                    for(int i=first;i<actor.route.Length;i++){
                        var point=actor.route[i];
                        double ahead=layout.Station(point);
                        double side=RecastFormationMotion.Station(point,layout.Entry,layout.Normal);
                        if(ahead<station || ahead>hint.ForwardLimit || side>upper || side<=lateral+frame.Profile.ArrivalTolerance)continue;
                        double delta=Math.Abs(laneSide-side);
                        if(delta<error){chosen=i;error=delta;}
                    }
                    if(chosen>=0){
                        var point=actor.route[chosen];
                        var guarded=hint;guarded.DirectionalBankReach=layout.BankReach;
                        if(RecastFormationMotion.DirectionalAllows(guarded,route,actor.Position,point) &&
                            NavRecastIntervals.CanTarget(route,state,actor.Location,chosen,frame.Reservation,frame.Working,actor.Id) &&
                            NavRecastIntervals.Advance(route,state,actor.Location,point,frame.Reservation,out var leg,frame.Working,actor.Id,portal) &&
                            leg.PortalCount==0 && leg.ToSurface==bank &&
                            frame.Working.ContinuationTraverse(route,state,actor.Location,point,actor.Radius,frame.Reservation,out var reached) &&
                            reached.SurfaceId==bank) target=point;
                    }
                }
                // The aim is a forecast, not a parked endpoint or a published
                // cursor. Actual own sweeps retain the exclusive entry guard.
                if(NavRecastIntervals.Advance(route,state,actor.Location,target,frame.Reservation,out var preparation,frame.Working,actor.Id)&&preparation.ToSurface==bank){hint.Target=target;hint.DirectionalBankReach=layout.BankReach;hint.Flags|=NavFormationFrameHint.Guide;}
            }
            return hint;
        }
        private static bool DirectionalAt(Layout layout,NavTypedRoute route,int portal,NavLocation actual)
        {
            if(!layout.Directional)return false;
            var entry=route.Legs[portal];if(actual.SurfaceId==entry.From.SurfaceId||actual.SurfaceId==entry.To.SurfaceId)return true;
            for(int i=portal+1;i<route.Legs.Count;i++)if(route.Legs[i].Kind==NavTypedLegKind.Portal)return actual.SurfaceId==route.Legs[i].To.SurfaceId;
            return false;
        }
        internal static NavPoint Aim(Layout layout,NavPoint actual,double station,int lane)
        {
            double current=layout.Station(actual);
            if(layout.Directional&&current<-layout.Radius){
                double side=RecastFormationMotion.Station(actual,layout.Entry,layout.Normal);
                double high=(layout.Low+layout.High)/2-layout.Radius;
                if(side<layout.Low||side>high){
                    double edge=side<layout.Low?layout.Low:high;
                    double chosen=RecastFormationMotion.Station(layout.Point(current,lane),layout.Entry,layout.Normal);
                    // A diagonal bank approach may advance only as far as its
                    // line can enter the directional half before the original
                    // full-footprint passage boundary. This geometric bound
                    // reaches the normal lookahead before the body itself
                    // crosses the band edge, avoiding a discontinuous target.
                    // Bodies already inside retain their full accepted forecast.
                    double crossing=(edge-side)/(chosen-side);
                    station=Math.Min(station,current+(-layout.Radius-current)/crossing);
                }
            }
            return layout.Point(station,lane);
        }
        internal static NavFormationFrameHint RecoveringApproach(NavPreparedCrowdStep frame,int row,NavTypedRoute route,int portal,Layout layout,int lane,double pace,double dt)
        {
            var actor=frame.After[row];var state=frame.RecastStates[row];
            if(!ReferenceEquals(frame.RecastRoutes[row],route)||!DirectionalAt(layout,route,portal,actor.Location)||state.Phase<0||state.Phase>2)return default;
            // No admission, role/frontier cap, or saved phase is reacquired.
            var hint=new NavFormationFrameHint{Flags=NavFormationFrameHint.Directional,TargetLeg=portal,DirectionalBankReach=layout.BankReach};
            var core=new NavFormationFrameHint{Flags=NavFormationFrameHint.Directional,TargetLeg=portal};
            // A native seam-adjacent motor corner may turn back into counterflow
            // after full-body exit. A temporary BANK connector is allowed only
            // with the same full owned trace and complete typed/HOLD goal suffix.
            double station=layout.Length+layout.Radius+(layout.BankReach>0?layout.BankReach:pace*Math.Max(dt,frame.Profile.CrowdLookaheadSeconds));
            int exit=portal+1;while(exit<route.Legs.Count&&route.Legs[exit].Kind!=NavTypedLegKind.Portal)exit++;if(exit>=route.Legs.Count||!actor.GoalLocation.HasValue)return core;
            bool Qualify(NavPoint target,bool physical){
                if(!RecastFormationMotion.DirectionalAllows(hint,route,actor.Position,target)||
                    !NavRecastIntervals.Advance(route,state,actor.Location,target,frame.Reservation,out var receipt,frame.Working,actor.Id)||receipt.ToSurface!=route.Legs[exit].To.SurfaceId)return false;
                var at=new NavLocation(receipt.To,receipt.ToSurface);
                if(physical&&(!frame.Working.ContinuationTraverse(route,state,actor.Location,target,layout.Radius,frame.Reservation,out var reached)||!reached.Equals(at)))return false;
                // The new bank aim needs the same complete current-owned motor
                // suffix, including the unchanged directional domain and HOLD.
                for(int candidate=actor.route.Length-1;candidate>=NavRecastIntervals.FirstTargetSurface(route,receipt.After);candidate--){
                    if(!RecastFormationMotion.DirectionalTailAllows(hint,route,at.Position,actor.route,candidate,actor.Goal)||
                        !NavRecastIntervals.TryContinuationRejoin(route,receipt.After,at,Array.Empty<NavPoint>(),0,actor.route,candidate,actor.GoalLocation.Value,frame.Reservation,out int same,frame.Working,actor.Id,true)||same!=candidate)continue;
                    hint.Target=target;hint.Flags|=NavFormationFrameHint.Guide;return true;
                }
                return false;
            }
            // On the exit bank the shared route still has to reach its
            // terminal fan. Its sampled common corner need not be every
            // member's turn across the incoming stream. Try the longitudinal
            // station of this member's own slot first. Both legs must be real
            // same-bank typed/HOLD sweeps with an accepted continuation.
            double actualStation=layout.Station(actor.Position);
            if(layout.BankReach>0 && actualStation>=layout.Length+layout.Radius &&
                actor.Location.SurfaceId==route.Legs[exit].To.SurfaceId && actor.GoalLocation.Value.SurfaceId==actor.Location.SurfaceId &&
                state.Phase==1 && !NavRecastIntervals.InTerminalFan(route,actor.Location) &&
                actor.localIndex>=actor.localRoute.Length)
            {
                var goal=actor.GoalLocation.Value;
                double goalStation=layout.Station(goal.Position);
                bool ReachesSlot(NavLocation from, NavRecastStepState before)
                {
                    if(!RecastFormationMotion.DirectionalAllows(hint,route,from.Position,goal.Position) ||
                        !NavRecastIntervals.Advance(route,before,from,goal.Position,frame.Reservation,out var end,frame.Working,actor.Id) ||
                        end.PortalCount!=0 || end.After.Phase!=2 || end.ToSurface!=goal.SurfaceId ||
                        !frame.Working.ContinuationTraverse(route,before,from,goal.Position,actor.Radius,frame.Reservation,out var reached) ||
                        !reached.Equals(goal)) return false;
                    int last=actor.route.Length-1;
                    return last>=0 && actor.route[last].Equals(goal.Position) &&
                        NavRecastIntervals.TryContinuationRejoin(route,end.After,goal,Array.Empty<NavPoint>(),0,actor.route,last,goal,
                            frame.Reservation,out int same,frame.Working,actor.Id,true) && same==last;
                }
                if(goalStation>=layout.Length+layout.Radius+layout.BankReach)
                {
                    if(goalStation>actualStation+frame.Profile.ArrivalTolerance)
                    {
                        var elbow=layout.BankPoint(goalStation,actor.Position);
                        if(RecastFormationMotion.DirectionalAllows(hint,route,actor.Position,elbow) &&
                            NavRecastIntervals.Advance(route,state,actor.Location,elbow,frame.Reservation,out var link,frame.Working,actor.Id) &&
                            link.PortalCount==0 && link.ToSurface==goal.SurfaceId &&
                            ReachesSlot(new NavLocation(link.To,link.ToSurface),link.After) && Qualify(elbow,true)) return hint;
                    }
                    else if(ReachesSlot(actor.Location,state) && Qualify(goal.Position,true)) return hint;
                }
            }
            if((layout.BankReach==0||layout.Station(actor.Position)>=layout.Length+layout.Radius+layout.BankReach)&&actor.routeIndex<actor.route.Length&&RecastFormationMotion.DirectionalTailAllows(hint,route,actor.Position,actor.route,actor.routeIndex,actor.Goal)&&
                NavRecastIntervals.CanTarget(route,state,actor.Location,actor.routeIndex,frame.Reservation,frame.Working,actor.Id))return hint;
            var laneTarget=layout.Point(station,lane);double current=layout.Station(actor.Position);
            // Shared passage and assigned lanes are unchanged. After full-body
            // exit, avoid pulling displaced bodies into one collar rendezvous.
            // A projected aim is a candidate, never an interpolated certificate.
            if(layout.BankReach>0&&current>=layout.Length+layout.Radius&&current<station&&actor.Location.SurfaceId==route.Legs[exit].To.SurfaceId&&(state.Phase==1||state.Phase==2)){
                var projected=layout.BankPoint(station,actor.Position);
                if(!projected.Equals(laneTarget)&&Qualify(projected,true))return hint;
            }
            if(Qualify(laneTarget,false))return hint;
            return core;
        }
        private static double Approach(NavTypedRoute route,NavUnit actor,int entry)
        {double total=0;var at=actor.Position;for(int i=0;i<=entry;i++){var p=route.Legs[i].To.Position;double x=p.X-at.X,z=p.Z-at.Z;total+=Math.Sqrt(x*x+z*z);at=p;}return total;}
        // Initial priority uses only the still-needed accepted prefix. This is a
        // distance estimate, never permission to change a motor cursor or sweep.
        private static double RemainingApproach(NavPreparedCrowdStep frame,int row,NavTypedRoute route,NavigationInstalledExecutionOwner owner,int entry,INavStorageReservation r)
        {
            var actor=frame.After[row];var state=frame.RecastStates[row];var request=owner?.Request?.Request;var subscription=route.Subscription;
            if(!(r is INavStorageLedgerIdentity ledger)||!(frame.Reservation is INavStorageLedgerIdentity frameLedger)||!ReferenceEquals(ledger.Identity,frameLedger.Identity)||route.RecastPlan==null||subscription.Provenance!=NavRouteProvenance.CommandMember||!ReferenceEquals(frame.RecastRoutes[row],route)||!ReferenceEquals(owner?.Corridor?.TypedRoute,route)||request==null||owner.Entity!=actor.Id||request.Entity!=actor.Id||request.Request!=actor.InstalledRequest||request.Order!=actor.InstalledOrder||subscription.EntityId!=actor.Id||subscription.RequestSequence!=actor.InstalledRequest||subscription.Incarnation!=actor.Incarnation||subscription.MobilityRevision!=actor.MobilityRevision||subscription.GroupId!=actor.InstalledGroupId||subscription.ActivationRevision!=actor.InstalledRevision||!actor.Moving||actor.Held||!actor.Location.Position.Equals(actor.Position)||state.Phase!=(int)RecastMovementPhase.Shared||state.Cursor<=0||state.Cursor<route.RecastPlan.OriginLegs||state.Cursor>=entry||state.Cursor>=route.Legs.Count||!NavRecastSpine.Finite(state.Station)||!NavRecastSpine.Finite(state.HighWater))return Approach(route,actor,entry);
            var leg=route.Legs[state.Cursor];if(leg.Kind!=NavTypedLegKind.Surface||leg.From.SurfaceId!=actor.Location.SurfaceId||leg.To.SurfaceId!=actor.Location.SurfaceId)return Approach(route,actor,entry);
            double total=0;var at=actor.Position;for(int i=state.Cursor;i<=entry;i++){var point=route.Legs[i].To.Position;double x=point.X-at.X,z=point.Z-at.Z;total+=Math.Sqrt(x*x+z*z);at=point;}
            if(!NavRecastSpine.Finite(total))return Approach(route,actor,entry);
            int surface=0;for(int i=0;i<state.Cursor;i++)if(route.Legs[i].Kind==NavTypedLegKind.Surface)surface++;
            // Use the same full typed, physical and HOLD guard as the motor. A
            // capacity exception must reach the existing atomic refusal path.
            return NavRecastIntervals.CanTarget(route,state,actor.Location,surface,r,frame.Working,actor.Id)?total:Approach(route,actor,entry);
        }
        internal static RecastFormationPlan Prepare(NavPreparedCrowdStep frame,NavigationAuthorityGroup group,NavigationInstalledExecutionOwner[] installed,bool[] detached,NavigationCommandSlots<int,long> membership,double dt,INavStorageReservation r,out bool clearCollecting)
        {
            using var profileGuide=PerformanceProfile.Measure(ProfileStage.FormationGuide);
            clearCollecting=false;
            var plan=group.Formation;if(plan==null)return null;int n=plan.Entries.Count;
            bool Eligible(int row,NavigationAuthorityMember.Values m)=>row>=0&&GroupMarch.InstalledActive(frame.After[row],group,m,installed[row],detached[row],membership);
            bool MemberEligible(int row,int entity){foreach(var value in group.Members){var m=((NavigationAuthorityMember)value).Data;if(m.Entity==entity)return Eligible(row,m);}return false;}
            void ResetCaps(){foreach(var value in group.Members){var m=((NavigationAuthorityMember)value).Data;int row=frame.Find(m.Entity);if(Eligible(row,m)){frame.Preferred[row]=frame.RecastReceiver[row]=double.NaN;frame.Formation[row]=default;}}}
            double minimumPace=double.PositiveInfinity;foreach(var value in group.Members){var m=((NavigationAuthorityMember)value).Data;int row=frame.Find(m.Entity);if(Eligible(row,m))minimumPace=Math.Min(minimumPace,frame.After[row].Speed);}
            foreach(var value in group.Members){var m=((NavigationAuthorityMember)value).Data;int row=frame.Find(m.Entity);if(Eligible(row,m))frame.Preferred[row]=frame.RecastReceiver[row]=Math.Min(frame.After[row].Speed,minimumPace);}
            using(NavStorageScratch.Reserve(r,checked(256+32*5L+160L*n))){
                var routes=new NavTypedRoute[n];var rows=new int[n];var entries=new RecastFormationEntry[n];var distances=new double[n];var order=new int[n];var paths=new NavRecastSpine[n];int pathCount=0;
                bool pending=false;int valid=0,eligibleCount=0;
                for(int i=0;i<n;i++){
                    var e=plan.Entries[i];entries[i]=e;int row=frame.Find(e.Entity);rows[i]=row;
                    if(e.Stage==RecastPassageStage.Tombstone){rows[i]=-1;continue;}
                    if(row<0||!membership.TryGetValue(e.Entity,out long g)||g!=plan.Group||detached[row]||frame.After[row].Incarnation!=e.Incarnation||frame.After[row].MobilityRevision!=e.Mobility){rows[i]=-1;entries[i]=e.WithStage(RecastPassageStage.Tombstone);continue;}
                    if(frame.After[row].Held||Released(group,e.Entity)){rows[i]=-1;continue;}
                    var owner=installed[row];if(owner==null||owner.Corridor.TypedRoute?.Subscription.RootOrderRevision!=plan.RootOrder||owner.Corridor.TypedRoute.Subscription.GroupId!=plan.Group){
                        var memberPhase=FindPhase(group,e.Entity);if(memberPhase==GroupMemberPhase.PendingRoute||memberPhase==GroupMemberPhase.Allocating)pending=true;else entries[i]=e.WithStage(RecastPassageStage.Tombstone);continue;
                    }
                    var route=owner.Corridor.TypedRoute;if(route.RecastPlan==null){entries[i]=e.WithStage(RecastPassageStage.Tombstone);continue;}
                    if(plan.Phase!=RecastFormationPhase.Collecting&&(e.Request!=route.Subscription.RequestSequence||e.SpineIndex<0||e.SpineIndex>=plan.Spines.Count||plan.Spines[e.SpineIndex].Binding!=route.RecastPlan.Spine.Binding))throw new ArgumentException("Formation accepted route binding changed.");
                    var recipe=owner.Corridor.Recipe;route.RecastPlan.ValidateBindings(recipe.GraphKey,owner.Corridor.ProviderOwner.Provider.Binding,recipe.Profile.ExactProfileBinding,owner.Request.Request.HeldIdentity,recipe.Clearance.Radius,owner.Request.Request.Geometry.Revision);NavContract.Sha256(route.RecastPlan.Spine.MapBinding);
                    routes[i]=route;valid++;if(MemberEligible(row,e.Entity))eligibleCount++;order[i]=i;
                }
                if(plan.Phase==RecastFormationPhase.Collecting&&pending){for(int i=0;i<n;i++)if(MemberEligible(rows[i],entries[i].Entity)){int row=rows[i];frame.Formation[row]=new NavFormationFrameHint{Flags=NavFormationFrameHint.Wait};frame.Preferred[row]=0;}return null;}
                string entry=plan.EntryPortal,exit=plan.ExitPortal;Layout layout=default;NavTypedRoute representative=null;
                double bankReach=NavRecastSpine.Finite(minimumPace)?minimumPace*Math.Max(dt,frame.Profile.CrowdLookaheadSeconds):0;
                bool GroupLayout(string ea,string eb,out Layout result){
                    double reach=bankReach;
                    foreach(var accepted in routes)if(accepted!=null)reach=Math.Min(reach,AvailableBankReach(accepted,ea,eb,reach));
                    int portal=Portal(representative,ea);
                    if(!RecastFormationMotion.Axis(representative,portal,out var origin,out _,out var direction,out double extent)){result=default;return false;}
                    // Retired/tombstoned members keep their assigned fan places.
                    // A vacated origin may change, but its still-occupied terminal
                    // must never be swallowed by a later derived bank collar.
                    foreach(var item in group.Members){var member=((NavigationAuthorityMember)item).Data;if(member.HasGoal)reach=Math.Min(reach,RecastFormationMotion.Station(member.Goal,origin,direction)-extent-2*representative.RecastPlan.Spine.Radius);}
                    return TryLayout(representative,ea,eb,frame.Profile.ArrivalSlotSpacing,r,out result,Math.Max(0,reach));
                }
                if(plan.Phase==RecastFormationPhase.Collecting){
                    // Find a consecutive physical passage common to every
                    // accepted route; no kind, map, or population inference.
                    for(int i=0;i<n&&representative==null;i++)if(routes[i]!=null)representative=routes[i];
                    if(valid<2||eligibleCount<2||representative==null){ResetCaps();clearCollecting=true;return null;}
                    for(int i=0;i<n;i++)if(routes[i]!=null&&(routes[i].RecastPlan.Spine.MapBinding!=representative.RecastPlan.Spine.MapBinding||routes[i].RecastPlan.Spine.ProviderBinding!=representative.RecastPlan.Spine.ProviderBinding||routes[i].RecastPlan.Spine.ProfileBinding!=representative.RecastPlan.Spine.ProfileBinding||routes[i].RecastPlan.Spine.KnowledgeBinding!=representative.RecastPlan.Spine.KnowledgeBinding)){ResetCaps();clearCollecting=true;return null;}
                    bool found=false;for(int a=0;a<representative.Legs.Count&&!found;a++)if(representative.Legs[a].Kind==NavTypedLegKind.Portal){int b=a+1;while(b<representative.Legs.Count&&representative.Legs[b].Kind!=NavTypedLegKind.Portal)b++;if(b>=representative.Legs.Count)continue;string ea=representative.Legs[a].PortalId,eb=representative.Legs[b].PortalId;bool common=true;for(int i=0;i<n;i++)if(routes[i]!=null&&(Portal(routes[i],ea)<0||Portal(routes[i],eb)<=Portal(routes[i],ea)))common=false;if(common&&GroupLayout(ea,eb,out layout)){entry=ea;exit=eb;found=true;}}
                    if(!found){ResetCaps();clearCollecting=true;return null;}
                    int k=0;for(int i=0;i<n;i++)if(routes[i]!=null){order[k++]=i;distances[i]=RemainingApproach(frame,rows[i],routes[i],installed[rows[i]],Portal(routes[i],entry),r);}
                    Array.Sort(order,0,k,Comparer<int>.Create((a,b)=>{int c=distances[a].CompareTo(distances[b]);return c!=0?c:entries[a].Slot.CompareTo(entries[b].Slot);}));
                    for(int j=0;j<k;j++){int i=order[j],spine=-1;for(int p=0;p<pathCount;p++)if(paths[p].Binding==routes[i].RecastPlan.Spine.Binding)spine=p;if(spine<0){spine=pathCount;paths[pathCount++]=routes[i].RecastPlan.Spine;}var e=entries[i];entries[i]=new RecastFormationEntry(e.Entity,e.Slot,spine,j,j%layout.Lanes,RecastPassageStage.Staging,e.Incarnation,e.Order,e.Mobility,routes[i].Subscription.RequestSequence);}
                }else{for(int i=0;i<n;i++)if(routes[i]!=null){representative=routes[i];break;}if(representative==null)return null;if(!GroupLayout(entry,exit,out layout)||layout.Lanes!=plan.LaneCount)throw new ArgumentException("Formation passage geometry changed.");for(int i=0;i<plan.Spines.Count;i++)paths[pathCount++]=plan.Spines[i];}
                // Only a finite physical window receives narrow roles. Vacated
                // rows release capacity immediately; survivor ranks never reset.
                // Reprojecting the reached lane endpoint can round one ULP below its station.
                // Use the same geometric epsilon as the forward sweep at this boundary.
                double exitStation=layout.Length+layout.Radius-1e-9;
                int baseRow=int.MaxValue;for(int i=0;i<n;i++)if(routes[i]!=null&&entries[i].Stage!=RecastPassageStage.Recovering&&entries[i].Stage!=RecastPassageStage.Tombstone){var e=entries[i];if(layout.Station(frame.After[rows[i]].Position)>=exitStation||!frame.After[rows[i]].Moving||Unmapped(group,e.Entity)){entries[i]=e.WithStage(RecastPassageStage.Recovering);continue;}baseRow=Math.Min(baseRow,e.EntryOrdinal/layout.Lanes);}
                int window=Math.Max(1,(int)Math.Floor(layout.Length/layout.Pitch));double pace=double.PositiveInfinity,minimum=double.PositiveInfinity;
                for(int i=0;i<n;i++)if(routes[i]!=null&&MemberEligible(rows[i],entries[i].Entity))pace=Math.Min(pace,frame.After[rows[i]].Speed);
                bool changed=plan.Phase==RecastFormationPhase.Collecting;for(int i=0;i<n;i++)if(routes[i]!=null){var e=entries[i];int role=e.EntryOrdinal/layout.Lanes;if(e.Stage==RecastPassageStage.Staging&&role<baseRow+window){entries[i]=e=e.WithStage(RecastPassageStage.Admitted);changed=true;}if(e.Stage==RecastPassageStage.Admitted)minimum=Math.Min(minimum,layout.Station(frame.After[rows[i]].Position)+role*layout.Pitch);}
                bool initialized=!double.IsPositiveInfinity(minimum);double anchor=initialized?(plan.AnchorInitialized?Math.Min(minimum,plan.FrontAnchor+pace*dt):minimum):0;
                double rear=double.PositiveInfinity;for(int i=0;i<n;i++)if(routes[i]!=null&&entries[i].Stage==RecastPassageStage.Admitted&&MemberEligible(rows[i],entries[i].Entity))rear=Math.Min(rear,layout.Station(frame.After[rows[i]].Position));
                double frontier=BankFrontier(layout,rear);int firstStagingRow=0,lastStagingRowExclusive=0;
                for(int i=0;i<n;i++)if(routes[i]!=null&&entries[i].Stage==RecastPassageStage.Staging){int role=entries[i].EntryOrdinal/layout.Lanes;firstStagingRow=lastStagingRowExclusive==0?role:Math.Min(firstStagingRow,role);lastStagingRowExclusive=Math.Max(lastStagingRowExclusive,role+1);}
                // Preparation spans only actual accepted future roles. Physical
                // own-row caps select a subset; the admitted window stays fixed.
                int futureRows=lastStagingRowExclusive>firstStagingRow?lastStagingRowExclusive-firstStagingRow:0;
                for(int i=0;i<n;i++)if(routes[i]!=null&&MemberEligible(rows[i],entries[i].Entity)){int row=rows[i];var e=entries[i];double bound=Math.Min(frame.After[row].Speed,pace);frame.Preferred[row]=frame.RecastReceiver[row]=bound;
                    if(e.Stage==RecastPassageStage.Staging){int portal=Portal(routes[i],entry);frame.Formation[row]=StagingApproach(frame,row,routes[i],portal,layout,frontier,e.EntryOrdinal/layout.Lanes,e.Lane,firstStagingRow,futureRows,pace,dt);if((frame.Formation[row].Flags&NavFormationFrameHint.Wait)!=0)frame.Preferred[row]=0;continue;}
                    if(e.Stage==RecastPassageStage.Recovering){frame.Formation[row]=RecoveringApproach(frame,row,routes[i],Portal(routes[i],entry),layout,e.Lane,pace,dt);continue;}
                    if(e.Stage!=RecastPassageStage.Admitted)continue;double nominal=(e.EntryOrdinal/layout.Lanes)*layout.Pitch;double limit=anchor+group.Data.MarchStretch-nominal;double station=layout.Station(frame.After[row].Position);double lookahead=pace*Math.Max(dt,frame.Profile.CrowdLookaheadSeconds);double targetStation=Math.Min(layout.Length+layout.Radius+layout.BankReach,Math.Max(station,limit)+lookahead);var target=Aim(layout,frame.After[row].Position,targetStation,e.Lane);var state=frame.RecastRoutes[row]!=null?frame.RecastStates[row]:NavRecastIntervals.Initial(routes[i]);
                    // One lane objective continues through the already certified exit
                    // collar in both Admitted and Recovering. The seam lip is not
                    // a parked destination; stopping all rows there blocks drainage.
                    // A role bound is a physical limit, not a stopping target. Keep
                    // lane seeking aimed over the existing forecast horizon; the
                    // owner filter still enforces the unchanged forward limit.
                    // A guide is temporary. The installed route and assigned
                    // endpoint remain the motor's authoritative intent.
                    bool can=NavRecastIntervals.Advance(routes[i],state,frame.After[row].Location,target,r,out _,frame.Working,frame.After[row].Id);
                    frame.Formation[row]=new NavFormationFrameHint{Target=target,TargetLeg=Portal(routes[i],entry),ForwardLimit=limit,DirectionalBankReach=can?layout.BankReach:0,Flags=(byte)(NavFormationFrameHint.Bound|(can?NavFormationFrameHint.Guide:0)|(DirectionalAt(layout,routes[i],Portal(routes[i],entry),frame.After[row].Location)?NavFormationFrameHint.Directional:0))};
                    // Keep accepted origin bends and avoidance eligible. The physical
                    // filter caps only own forward translation at the role limit.
                    frame.Preferred[row]=bound;
                }
                for(int i=0;i<n;i++)if(entries[i].Stage!=plan.Entries[i].Stage)changed=true;
                var phase=baseRow==int.MaxValue?RecastFormationPhase.Recovering:RecastFormationPhase.Feeding;
                if(!changed&&phase==plan.Phase&&anchor==plan.FrontAnchor&&initialized==plan.AnchorInitialized)return null;
                // Array slicing is paid by the staging reservation above.
                var distinct=new NavRecastSpine[pathCount];Array.Copy(paths,distinct,pathCount);
                return RecastFormationPlan.Create(plan.World,plan.Group,plan.RootOrder,plan.Command,plan.BirthMembership,layout.Lanes,phase,anchor,initialized,entry,exit,entries,distinct,r);
            }
        }
        private static bool Released(NavigationAuthorityGroup group,int entity){foreach(var m in group.Members){var d=((NavigationAuthorityMember)m).Data;if(d.Entity==entity)return d.FormationReleased;}return false;}
        private static bool Unmapped(NavigationAuthorityGroup group,int entity){foreach(var m in group.Members){var d=((NavigationAuthorityMember)m).Data;if(d.Entity==entity)return d.Recast.Phase==RecastMovementPhase.UnmappedProduction;}return false;}
        private static GroupMemberPhase FindPhase(NavigationAuthorityGroup group,int entity){foreach(var m in group.Members){var d=((NavigationAuthorityMember)m).Data;if(d.Entity==entity)return d.Phase;}return GroupMemberPhase.Detached;}
    }
}
