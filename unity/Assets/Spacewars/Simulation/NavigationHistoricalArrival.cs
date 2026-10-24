using System;
using System.Linq;
namespace Spacewars.Simulation
{
    public sealed partial class NavigationArrivalWork
    {
        private NavigationArrivalWork historicalOracle {get=>historicalState?.Oracle;set=>historicalState.Oracle=value;}
        private NavPagedList<NavTypedLeg> historicalLegs {get=>historicalState?.Legs;set=>historicalState.Legs=value;}
        private NavScheduledRequest historicalRecipe {get=>historicalState?.Recipe;set=>historicalState.Recipe=value;}
        private long historicalEpochBase {get=>historicalState?.EpochBase??0;set=>historicalState.EpochBase=value;}
        private int historicalGate {get=>historicalState?.Gate??0;set=>historicalState.Gate=value;}
        private int historicalCursor {get=>historicalState?.Cursor??0;set=>historicalState.Cursor=value;}
        private int historicalMember {get=>historicalState?.Member??0;set=>historicalState.Member=value;}
        private int historicalLeg {get=>historicalState?.Leg??0;set=>historicalState.Leg=value;}
        private void SetContinuationPolicy(bool enabled,bool v3=true,bool prefix=true)
        {
            if(!enabled){historicalState?.ReleaseOwner();historicalState=null;return;}
            if(historicalState!=null){historicalState.Page0Occupancy=false;ResetPage0Occupancy();historicalState.TerminalRectangle=false;historicalState.TerminalCorner=false;historicalState.OriginShadow=false;historicalState.OwnedArrivalAnchor=false;historicalState.TerminalCost=false;historicalState.RectangleOrigins=false;historicalState.Certified=false;historicalState.Lazy=false;historicalState.Astar=false;historicalState.Exact=false;historicalState.Removal=false;historicalState.SeamBound=false;historicalState.DemandV3=v3;historicalState.OccupancyPrefix=prefix;return;}historicalState=new NavHistoricalArrivalState(rootReservation,v3:v3,prefix:prefix);if(IsRegistered)RequireEdge(historicalState);
        }
        internal bool IsHistoricalCandidate=>demandContinuationMode&&input.IndependentMembers&&!derivativesDiscarded&&(Status==NavArrivalStatus.Pending||Status==NavArrivalStatus.BudgetExhausted||Status==NavArrivalStatus.Ready);
        internal NavigationArrivalWork HistoricalSource=>historicalOracle?.HistoricalSource??this;
        internal void AttachHistorical(NavigationArrivalWork source,INavStorageReservation r)
        {
            MutableRoot();source=source.HistoricalSource;NavStorageAdmission.RequireOwned(r,source);
            if(terminalCostMode!=source.terminalCostMode||ownedArrivalAnchorMode!=source.ownedArrivalAnchorMode||originShadowMode!=source.originShadowMode||terminalCornerMode!=source.terminalCornerMode||terminalRectangleMode!=source.terminalRectangleMode||rectangleOriginsMode!=source.rectangleOriginsMode||certifiedMode!=source.certifiedMode||lazyMode!=source.lazyMode||astarMode!=source.astarMode||exactCostMode!=source.exactCostMode||!demandContinuationMode||!source.IsHistoricalCandidate||source.input.Members.Count!=input.Members.Count)return;
            historicalOracle=NavPhysicalMacro.Acquire(historicalState,source);historicalEpochBase=checked(source.lastEpoch+1);historicalGate=0;historicalCursor=0;
            historicalLegs=NavPagedList<NavTypedLeg>.Create(r.TryReserve);
        }
        internal void SealPreparedIfMutable(){if(!rootFrozen)SealPrepared();}
        private void ForkHistoricalInto(NavigationArrivalWork fork,INavStorageReservation r)
        {
            if(historicalState!=null){fork.historicalState=historicalState.Fork(r);if(fork.IsRegistered)fork.RequireEdge(fork.historicalState);}
        }
        private void ClearHistoricalStorage()
        {
            if(historicalState==null)return;
            historicalOracle?.Release();historicalOracle=null;historicalLegs?.ReleaseOwner();historicalLegs=null;historicalRecipe?.StorageRoot?.Release();historicalRecipe=null;
            historicalEpochBase=0;historicalGate=historicalCursor=historicalMember=historicalLeg=0;ResetRemoval();ResetPage0Occupancy();
        }
        private void DropHistorical()
        {
            if(historicalOracle!=null)scheduler.AbsorbHistoricalCounters(historicalOracle.RouteCounters);ClearHistoricalStorage();ClearAssignments();memberIndex=0;pageIndex=0;phase=Phase.PrepareRegions;Status=NavArrivalStatus.BudgetExhausted;
        }
        private bool HistoricalHeader()
        {
            var old=historicalOracle.input;
            return terminalRectangleMode==historicalOracle.terminalRectangleMode&&rectangleOriginsMode==historicalOracle.rectangleOriginsMode&&certifiedMode==historicalOracle.certifiedMode&&lazyMode==historicalOracle.lazyMode&&astarMode==historicalOracle.astarMode&&exactCostMode==historicalOracle.exactCostMode&&input.IndependentMembers&&old.IndependentMembers&&input.Members.Count==old.Members.Count&&input.ProfileIdentity==old.ProfileIdentity&&(removalMode?input.Source.Candidates.Count<=old.Source.Candidates.Count:input.Source.Candidates.Count==old.Source.Candidates.Count)&&input.Source.Requested.Equals(old.Source.Requested)&&input.Source.Id==old.Source.Id&&input.Source.Revision==old.Source.Revision&&input.Occupancy.Generation==old.Occupancy.Generation&&(occupancyPrefixMode?old.Occupancy.Occupants.Count<=input.Occupancy.Occupants.Count:input.Occupancy.Occupants.Count==old.Occupancy.Occupants.Count)&&(!occupancyPrefixMode||input.ArrivalProfile.ArrivalSlotSpacing==old.ArrivalProfile.ArrivalSlotSpacing&&input.ArrivalProfile.ArrivalTolerance==old.ArrivalProfile.ArrivalTolerance&&input.ArrivalProfile.LocalGridCell==old.ArrivalProfile.LocalGridCell)&&input.MaxLabels==old.MaxLabels&&input.Limits.MaxSamples==old.Limits.MaxSamples&&input.Limits.MaxNodes==old.Limits.MaxNodes&&input.Limits.MaxArcs==old.Limits.MaxArcs&&input.Provider is PartitionNavigationProvider now&&old.Provider is PartitionNavigationProvider previous&&previous.MonotoneHeaderTo(now);
        }
        private bool HistoricalMemberSame(int index)
        {
            var a=historicalOracle.members[index];var b=members[index];var old=historicalOracle.input.WorkingFor(a);var now=input.WorkingFor(b);
            return a.Order==b.Order&&a.Clearance.MobilityRevision==b.Clearance.MobilityRevision&&a.EntityId==b.EntityId&&a.WorldGeneration==b.WorldGeneration&&a.GroupId==b.GroupId&&a.RootOrderRevision==b.RootOrderRevision&&a.CommandSequence==b.CommandSequence&&a.IssuedTick==b.IssuedTick&&a.ActivationRevision==b.ActivationRevision&&a.MemberOrderRevision==b.MemberOrderRevision&&a.Incarnation==b.Incarnation&&a.MobilityRevision==b.MobilityRevision&&a.HoldBinding==b.HoldBinding&&a.Origin.Equals(b.Origin)&&a.Clearance.Id==b.Clearance.Id&&a.Clearance.Revision==b.Clearance.Revision&&a.Clearance.MobilityClass==b.Clearance.MobilityClass&&a.Clearance.Radius==b.Clearance.Radius&&old.Provider.Binding==historicalOracle.input.Provider.Binding&&now.Provider.Binding==input.Provider.Binding&&old.Profile.ExactProfileBinding==now.Profile.ExactProfileBinding&&old.Profile.GridCell==now.Profile.GridCell&&old.Profile.CostId==now.Profile.CostId&&old.Profile.CostRevision==now.Profile.CostRevision;
        }
        private int HistoricalPartCount=>((PartitionNavigationProvider)historicalOracle.input.Provider).MonotonePartCount+(page0OccupancyMode?0:historicalOracle.input.Occupancy.Occupants.Count);
        private bool HistoricalPartSame(int index)
        {
            var old=(PartitionNavigationProvider)historicalOracle.input.Provider;int count=old.MonotonePartCount;if(index<count)return old.MonotonePartTo((PartitionNavigationProvider)input.Provider,index);
            // The allocation predicate consumes only blocking XZ footprints.
            // Rebind request/proof text may change without changing that domain.
            int row=index-count;var a=historicalOracle.input.Occupancy.Occupants[row];var b=input.Occupancy.Occupants[row];return a.BlocksPlacement==b.BlocksPlacement&&(!a.BlocksPlacement||a.Location.Position.Equals(b.Location.Position)&&a.Radius==b.Radius);
        }
        private NavArrivalStatus AdvanceHistorical(long epoch,int allocationQuota,NavEpochQuota quota)
        {
            long start=AllocationWorkConsumed;
            while(historicalGate<4&&allocationRemaining>0){
                SpendCandidate();bool valid=true;
                if(historicalGate==0){valid=HistoricalHeader();historicalGate=1;historicalCursor=0;}
                else if(historicalGate==1){if(historicalCursor<members.Length)valid=HistoricalMemberSame(historicalCursor++);else{historicalGate=2;historicalCursor=0;}}
                else if(historicalGate==2&&RemovalActive){valid=RemovalSubsequenceStep();}
                else if(historicalGate==2){if(historicalCursor<input.Source.Candidates.Count){valid=input.Source.Candidates[historicalCursor].Equals(historicalOracle.input.Source.Candidates[historicalCursor]);historicalCursor++;}else{historicalGate=3;historicalCursor=0;}}
                else{var previous=(PartitionNavigationProvider)historicalOracle.input.Provider;var current=(PartitionNavigationProvider)input.Provider;if(historicalCursor<HistoricalPartCount)valid=HistoricalPartSame(historicalCursor++);else{historicalGate=4;historicalCursor=0;}}
                if(!valid){DropHistorical();LastAdvanceConsumed=AllocationWorkConsumed-start;return Status;}
            }
            if(historicalGate<4){Status=NavArrivalStatus.BudgetExhausted;LastAdvanceConsumed=AllocationWorkConsumed-start;return Status;}
            if(page0OccupancyMode&&!PreparePage0OccupancyCertificate()){if(historicalOracle!=null)Status=NavArrivalStatus.BudgetExhausted;LastAdvanceConsumed=AllocationWorkConsumed-start;return Status;}
            if(!page0OccupancyMode&&RemovalActive&&!PrepareRemovalCertificate()){if(historicalOracle!=null)Status=NavArrivalStatus.BudgetExhausted;LastAdvanceConsumed=AllocationWorkConsumed-start;return Status;}
            if(historicalOracle.Status!=NavArrivalStatus.Ready){
                if(historicalOracle.rootFrozen){var next=historicalOracle.ForkOwned(OwnedReservation);historicalOracle.Release();historicalOracle=next;}
                if(!historicalOracle.TryReserveAuthorityBytes(authorityReservationBase+OutputRetainedBytes+input.Provider.EstimatedRetainedBytes)){LastAdvanceRetentionBlocked=true;Status=NavArrivalStatus.BudgetExhausted;LastAdvanceConsumed=AllocationWorkConsumed-start;return Status;}
                long prior=historicalOracle.AllocationWorkConsumed,priorCandidate=historicalOracle.CandidateWorkConsumed,priorOccupancy=historicalOracle.OccupancyWorkConsumed,priorDirect=historicalOracle.DirectWorkConsumed;bool page0Boundary=false;if(page0OccupancyMode)historicalOracle.AdvancePage0(checked(historicalEpochBase+epoch),allocationRemaining,quota,out page0Boundary);else historicalOracle.Advance(checked(historicalEpochBase+epoch),allocationRemaining,quota);
                long used=historicalOracle.AllocationWorkConsumed-prior;allocationRemaining=checked(allocationRemaining-(int)used);AllocationWorkConsumed+=used;CandidateWorkConsumed+=historicalOracle.CandidateWorkConsumed-priorCandidate;OccupancyWorkConsumed+=historicalOracle.OccupancyWorkConsumed-priorOccupancy;DirectWorkConsumed+=historicalOracle.DirectWorkConsumed-priorDirect;
                LastAdvanceRetentionBlocked=historicalOracle.LastAdvanceRetentionBlocked;
                if(page0Boundary){DropHistorical();LastAdvanceConsumed=AllocationWorkConsumed-start;return Status;}
                if((page0OccupancyMode||RemovalActive)&&!RemovalOraclePageZero()){DropHistorical();LastAdvanceConsumed=AllocationWorkConsumed-start;return Status;}
                if(historicalOracle.Status!=NavArrivalStatus.Ready){Status=NavArrivalStatus.BudgetExhausted;LastAdvanceConsumed=AllocationWorkConsumed-start;return Status;}
            }
            if(historicalOracle.memberOutcomes.Count!=0||historicalOracle.assignments.Count!=members.Length){DropHistorical();LastAdvanceConsumed=AllocationWorkConsumed-start;return Status;}
            while(historicalMember<historicalOracle.assignments.Count&&allocationRemaining>0){
                var old=historicalOracle.assignments[historicalMember];var member=members.Single(m=>m.EntityId==old.Member.EntityId);var binding=input.WorkingFor(member);var provider=(PartitionNavigationProvider)binding.Provider;var endpoint=provider.OwnedCheckpointLocation(old.Endpoint);
                if(RemovalActive&&!RemovalEndpointMember(old)){if(historicalOracle!=null)Status=NavArrivalStatus.BudgetExhausted;break;}
                if(allocationRemaining==0)break;
                if(occupancyPrefixMode&&historicalCursor<input.Occupancy.Occupants.Count){
                    SpendOccupancy();var occupant=input.Occupancy.Occupants[historicalCursor++];
                    if(HistoricalOccupantBlocks(member,endpoint,occupant)){DropHistorical();break;}
                    continue;
                }
                SpendCandidate();
                if(historicalRecipe==null){
                    if(!provider.IsValid(member.Origin,member.Clearance)||!provider.IsValid(endpoint,member.Clearance)){DropHistorical();break;}
                    var sub=new NavRouteSubscription(member.WorldGeneration,member.GroupId,member.RootOrderRevision,member.CommandSequence,member.IssuedTick,member.ActivationRevision,member.EntityId,member.Incarnation,member.MemberOrderRevision,member.MobilityRevision,member.RequestSequence,member.Origin,endpoint,member.HoldBinding,NavRouteProvenance.CommandMember);
                    historicalRecipe=rootReservation==null?new NavScheduledRequest(provider,member.Clearance,binding.Profile,input.Limits,null,sub,input.MaxLabels,singleton:true):NavScheduledRequest.CreateOwned(provider,member.Clearance,binding.Profile,input.Limits,null,sub,input.MaxLabels,OwnedReservation,singleton:true);continue;
                }
                if(historicalLeg<old.Route.Legs.Count){
                    var leg=old.Route.Legs[historicalLeg];if(!provider.CertifyHistoricalLeg(leg,member.Clearance,occupancyPrefixMode)){DropHistorical();break;}
                    var copy=rootReservation==null?new NavTypedLeg(provider.OwnedCheckpointLocation(leg.From),provider.OwnedCheckpointLocation(leg.To),leg.Cost,leg.Kind,leg.PortalId):NavTypedLeg.CreateOwned(provider.OwnedCheckpointLocation(leg.From),provider.OwnedCheckpointLocation(leg.To),leg.Cost,leg.Kind,leg.PortalId,OwnedReservation,provider);
                    try{historicalLegs.Add(copy);}finally{copy.ReleaseOwner();}historicalLeg++;continue;
                }
                var route=new NavTypedRoute(historicalRecipe.Subscription,historicalLegs,old.Route.Cost,NavStorageLayout.Before(rootReservation,128),true);
                try{
                    KeepCertifiedAssignment(member,endpoint,historicalRecipe,route);
                }finally{route.ReleaseOwner();}
                historicalRecipe.StorageRoot?.ReleaseOwner();historicalRecipe=null;historicalLegs.ReleaseOwner();historicalLegs=NavPagedList<NavTypedLeg>.Create(rootReservation);historicalLeg=0;historicalCursor=0;historicalMember++;memberIndex=historicalMember;if(removalMode){historicalState.EndpointCursor=0;historicalState.EndpointFound=false;}
            }
            if(historicalOracle!=null&&historicalMember==members.Length){scheduler.AbsorbHistoricalCounters(historicalOracle.RouteCounters);historicalOracle.Release();historicalOracle=null;historicalLegs.ReleaseOwner();historicalLegs=null;historicalEpochBase=0;historicalGate=historicalCursor=historicalMember=historicalLeg=0;ResetRemoval();ResetPage0Occupancy();Status=NavArrivalStatus.Ready;memberIndex=members.Length;phase=Phase.Finished;}
            else if(historicalOracle!=null)Status=NavArrivalStatus.BudgetExhausted;
            LastAdvanceConsumed=AllocationWorkConsumed-start;return Status;
        }
        private void WriteHistoricalBody(NavPhysicalBodyWriter w)
        {
            NavPhysicalMacro.WriteEdge(w,this,historicalState);
        }
        private void ReadHistoricalBody(NavPhysicalBodyReader r)
        {
            historicalState=NavPhysicalMacro.Acquire(this,NavPhysicalMacro.ReadEdge<NavHistoricalArrivalState>(r));
            ValidateHistoricalContinuation(true);
        }
        private bool HistoricalOccupantBlocks(NavArrivalMember member,NavLocation endpoint,NavArrivalOccupant occupant)
            =>occupant.BlocksPlacement&&TooClose(endpoint.Position,member.Radius,occupant.Location.Position,occupant.Radius);
        private void ValidateHistoricalContinuation(bool physical)
        {
            if(historicalOracle==null){ValidateEmptyPage0Occupancy();if(historicalLegs!=null||historicalRecipe!=null||historicalEpochBase!=0||historicalGate!=0||historicalCursor!=0||historicalMember!=0||historicalLeg!=0)throw new ArgumentException("Historical empty continuation payload.");return;}
            if(terminalCostMode!=historicalOracle.terminalCostMode||ownedArrivalAnchorMode!=historicalOracle.ownedArrivalAnchorMode||originShadowMode!=historicalOracle.originShadowMode||terminalCornerMode!=historicalOracle.terminalCornerMode||terminalRectangleMode!=historicalOracle.terminalRectangleMode||rectangleOriginsMode!=historicalOracle.rectangleOriginsMode||certifiedMode!=historicalOracle.certifiedMode||lazyMode!=historicalOracle.lazyMode||astarMode!=historicalOracle.astarMode||exactCostMode!=historicalOracle.exactCostMode||!historicalOracle.IsHistoricalCandidate||historicalOracle.historicalOracle!=null||historicalLegs==null||historicalGate<0||historicalGate>4||historicalCursor<0||historicalMember<0||historicalMember>=members.Length||historicalLeg<0||historicalLegs.Count!=historicalLeg||historicalEpochBase<0||historicalGate<4&&historicalMember!=0)throw new ArgumentException("Historical continuation dimensions mismatch.");
            int bound=historicalGate==1?members.Length:historicalGate==2?input.Source.Candidates.Count:historicalGate==3?HistoricalPartCount:historicalGate==4&&occupancyPrefixMode?input.Occupancy.Occupants.Count:0;
            if(historicalCursor>bound||historicalGate==0&&historicalCursor!=0||historicalGate<4&&(historicalRecipe!=null||historicalLeg!=0||historicalLegs.Count!=0||assignments.Count!=0)||memberIndex!=historicalMember||assignments.Count!=historicalMember)throw new ArgumentException("Historical cursor/prefix mismatch.");
            if(historicalGate>0&&!HistoricalHeader())throw new ArgumentException("Historical certified header changed.");
            for(int i=0;i<(historicalGate>1?members.Length:historicalGate==1?historicalCursor:0);i++)if(!HistoricalMemberSame(i))throw new ArgumentException("Historical request mapping changed.");
            if(page0OccupancyMode)ValidatePage0OccupancyCertificate();else if(RemovalActive)ValidateRemovalCertificate();
            if(!RemovalActive)for(int i=0;i<(historicalGate>2?input.Source.Candidates.Count:historicalGate==2?historicalCursor:0);i++)if(!input.Source.Candidates[i].Equals(historicalOracle.input.Source.Candidates[i]))throw new ArgumentException("Historical terminal prefix changed.");
            if(historicalGate>=3){var previous=(PartitionNavigationProvider)historicalOracle.input.Provider;var current=(PartitionNavigationProvider)input.Provider;for(int i=0;i<(historicalGate==4?HistoricalPartCount:historicalCursor);i++)if(!HistoricalPartSame(i))throw new ArgumentException("Historical geometry prefix changed.");}
            if(occupancyPrefixMode&&historicalGate==4){
                if(historicalOracle.Status!=NavArrivalStatus.Ready&&historicalCursor!=0||historicalRecipe!=null&&historicalCursor!=input.Occupancy.Occupants.Count)throw new ArgumentException("Historical occupancy qualification lifecycle mismatch.");
                for(int member=0;member<=historicalMember&&historicalOracle.Status==NavArrivalStatus.Ready;member++){
                    if(historicalOracle.assignments.Count!=members.Length)throw new ArgumentException("Historical occupancy qualification lacks completed assignments.");
                    var old=historicalOracle.assignments[member];var current=members.Single(m=>m.EntityId==old.Member.EntityId);
                    int checkedRows=member<historicalMember?input.Occupancy.Occupants.Count:historicalCursor;
                    for(int row=0;row<checkedRows;row++)if(HistoricalOccupantBlocks(current,old.Endpoint,input.Occupancy.Occupants[row]))throw new ArgumentException("Historical checked occupancy prefix blocks endpoint.");
                    if(member<historicalMember&&(!assignments[member].Endpoint.Equals(old.Endpoint)||assignments[member].Member.EntityId!=old.Member.EntityId||assignments[member].Route.Cost!=old.Route.Cost))throw new ArgumentException("Historical certified assignment prefix changed.");
                }
            }
            if(historicalRecipe!=null){
                if(historicalGate!=4||historicalOracle.Status!=NavArrivalStatus.Ready||historicalOracle.assignments.Count!=members.Length)throw new ArgumentException("Historical recipe lacks completed source.");
                var old=historicalOracle.assignments[historicalMember];var m=members.Single(x=>x.EntityId==old.Member.EntityId);var binding=input.WorkingFor(m);var q=historicalRecipe.Subscription;
                if(!ReferenceEquals(historicalRecipe.Provider,binding.Provider)||historicalRecipe.GraphKey!=new NavGraphBinding(binding.Provider.Binding,m.Clearance,binding.Profile).StableKey||!historicalRecipe.Singleton||historicalRecipe.Query||historicalRecipe.Region!=null||historicalRecipe.MaxLabels!=input.MaxLabels||historicalRecipe.Limits.MaxSamples!=input.Limits.MaxSamples||historicalRecipe.Limits.MaxNodes!=input.Limits.MaxNodes||historicalRecipe.Limits.MaxArcs!=input.Limits.MaxArcs||q.StableKey!=new NavRouteSubscription(m.WorldGeneration,m.GroupId,m.RootOrderRevision,m.CommandSequence,m.IssuedTick,m.ActivationRevision,m.EntityId,m.Incarnation,m.MemberOrderRevision,m.MobilityRevision,m.RequestSequence,m.Origin,old.Endpoint,m.HoldBinding,NavRouteProvenance.CommandMember).StableKey||historicalLeg>old.Route.Legs.Count)throw new ArgumentException("Historical current recipe identity mismatch.");
                for(int i=0;i<historicalLeg;i++){var actual=historicalLegs[i];var expected=old.Route.Legs[i];if(!actual.From.Equals(expected.From)||!actual.To.Equals(expected.To)||actual.Cost!=expected.Cost||actual.Kind!=expected.Kind||actual.PortalId!=expected.PortalId||physical&&(!NavTypedLeg.SamePhysicalLocation(actual.From,((PartitionNavigationProvider)binding.Provider).OwnedCheckpointLocation(actual.From))||!NavTypedLeg.SamePhysicalLocation(actual.To,((PartitionNavigationProvider)binding.Provider).OwnedCheckpointLocation(actual.To))))throw new ArgumentException("Historical route prefix mismatch.");}
            }else if(historicalLeg!=0)throw new ArgumentException("Historical route prefix lacks recipe.");
        }
    }
}
