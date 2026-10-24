using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    public sealed partial class NavigationArrivalWork
    {
        private static readonly long LayoutBytes=Math.Max(768,NavStorageLayout.ObjectBytesBound(typeof(NavigationArrivalWork)));
        private Func<long,bool> rootReservation;
        private INavStorageReservation OwnedReservation=>rootReservation?.Target as INavStorageReservation??throw new InvalidOperationException("Missing arrival allocation scope.");
        private NavOwnedStorage ownedInput;
        internal NavArrivalInput PhysicalInput=>input;
        private NavRetainedArray<NavOwnedText> workingKeys;
        private NavOwnedText preparingKeyStorage;
        private readonly bool physicalLifetime;
        internal bool HasPhysicalLifetime=>physicalLifetime;
        private bool rootFrozen,rootFaulted,rootReleased,derivativesDiscarded;
        internal override bool IsImmutableCaptureRoot=>IsRegistered&&rootFrozen&&!rootFaulted;
        private void MutableRoot(){if(rootFrozen||rootFaulted||rootReleased)throw new InvalidOperationException("Committed or failed arrival work cannot be mutated.");}
        internal static NavigationArrivalWork CreateOwned(NavArrivalInput input,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,input?.StorageRoot);
            using(NavStorageScratch.Reserve(reservation,checked(8192+64L*input.Source.Candidates.Count+128L*input.Members.Count))){
                var work=new NavigationArrivalWork(input,reservation.TryReserve);
                try{work.InitializeWorkingKeys();return work;}catch{work.ReleaseOwner();throw;}
            }
        }
        private string RawWorkingKey(NavArrivalMember member)=>ClearanceKey(member.Clearance)+":"+input.WorkingFor(member).Provider.Binding+":"+input.WorkingFor(member).Profile.ExactProfileBinding;
        private void InitializeWorkingKeys()
        {
            var values=new NavOwnedText[members.Length];
            try{
                for(int n=0;n<values.Length;n++){
                    var member=members[n];long wire=checked(256+NavContract.StringWireBytes(member.Clearance.Id)+NavContract.StringWireBytes(member.Clearance.MobilityClass)+NavContract.StringWireBytes(input.WorkingFor(member).Provider.Binding)+NavContract.StringWireBytes(input.WorkingFor(member).Profile.ExactProfileBinding));
                    using(NavStorageScratch.Reserve(OwnedReservation,NavContract.DigestScratchBytes(wire)))values[n]=NavOwnedText.Copy(RawWorkingKey(member),OwnedReservation);
                }
                workingKeys=NavRetainedArray<NavOwnedText>.Copy(values,rootReservation);
            }finally{for(int n=0;n<values.Length;n++)values[n]?.Release();}
        }
        private NavOwnedText KeyOwner(string key)
        {if(workingKeys==null)return null;for(int n=0;n<workingKeys.Count;n++)if(ReferenceEquals(workingKeys[n].Value,key))return workingKeys[n];throw new ArgumentException("Arrival working key lost its exact owner.");}
        private void AddPrepared(string key,NavPagedList<NavRetainedArray<int>> indices,NavPagedList<NavTerminalRegion> regions)
        {
            indices.Seal();regions.Seal();var owner=KeyOwner(key);var prepared=PreparedClearance.Create(indices,regions,owner,rootReservation);
            try{preparedClearances.Add(owner==null?new NavNameKey(key):new NavNameKey(new NavNameReference(owner,NavNameRole.Text)),prepared);}finally{prepared.ReleaseOwner();}
        }
        private void AddRequestRow(int candidate,NavScheduledRequest request)
        {var row=RouteRow.Create(candidate,request,default,null,rootReservation);try{pageRequests.Add(row);}finally{row.ReleaseOwner();}}
        private void AcceptCompletion(RouteRow old,NavSolveStatus status,NavTypedRoute route)
        {
            int index=0;while(index<pageRequests.Count&&!ReferenceEquals(pageRequests[index],old))index++;
            if(index==pageRequests.Count||pageResults.ContainsKey(old.Candidate))throw new ArgumentException("Unknown or repeated arrival completion.");
            var row=RouteRow.Create(old.Candidate,old.Request,status,route,rootReservation);
            try{pageRequests.Set(index,row);pageResults.Add(row.Candidate,row);}finally{row.ReleaseOwner();}
        }
        private void KeepCertifiedAssignment(NavArrivalMember member,NavLocation endpoint,NavScheduledRequest request,NavTypedRoute route)
        {
            if(rootReservation==null){var legacyProof=new NavArrivalReachabilityProof(input,member,endpoint,request,route);KeepAssignment(new NavArrivalProposal(member,endpoint,route,legacyProof));return;}
            var proof=NavArrivalReachabilityProof.CreateOwned(input,member,endpoint,request,route,OwnedReservation);
            try{var proposal=NavArrivalProposal.CreateOwned(member,proof.Endpoint,route,proof,OwnedReservation);try{KeepAssignment(proposal);}finally{proposal.StorageRoot.Release();}}finally{proof.StorageRoot.Release();}
        }
        private NavPagedList<NavRetainedArray<int>> CopyCandidateSlots(int[][] source)
        {
            if(source==null)return null;var list=NavPagedList<NavRetainedArray<int>>.Create(rootReservation);
            try{foreach(var row in source){var values=row==null?null:NavRetainedArray<int>.Copy(row,rootReservation);try{list.Add(values);}finally{values?.Release();}}return list;}catch{list.ReleaseOwner();throw;}
        }
        private NavPagedList<NavTerminalRegion> CopyRegionSlots(NavTerminalRegion[] source)
        {if(source==null)return null;var list=NavPagedList<NavTerminalRegion>.Create(rootReservation);try{foreach(var region in source)list.Add(region);return list;}catch{list.ReleaseOwner();throw;}}
        private static int[][] ExportCandidates(NavPagedList<NavRetainedArray<int>> source)=>source?.Select(row=>row?.ToArray()).ToArray();
        private void ResetPreparationSlots()
        {
            preparationCandidateIndices?.ReleaseOwner();preparationCandidateIndices=null;preparationRegions?.ReleaseOwner();preparationRegions=null;
            preparationCandidateIndices=NavPagedList<NavRetainedArray<int>>.Create(rootReservation);preparationRegions=NavPagedList<NavTerminalRegion>.Create(rootReservation);
            for(int n=0;n<pageStarts.Length;n++){preparationCandidateIndices.Add(null);preparationRegions.Add(null);}
            preparingKeyStorage?.Release();preparingKeyStorage=KeyOwner(preparingClearanceKey);preparingKeyStorage?.Retain();
        }
        private void ClearPreparation()
        {preparingClearanceKey=null;preparingKeyStorage?.Release();preparingKeyStorage=null;preparationCandidateIndices?.ReleaseOwner();preparationCandidateIndices=null;preparationRegions?.ReleaseOwner();preparationRegions=null;}
        private void FinishPreparationPage(string key)
        {
            IDisposable scratch=rootReservation==null?null:NavStorageScratch.Reserve(OwnedReservation,checked(8192+128L*preparationValidCandidateIndices.Count+NavContract.DigestScratchBytes(256+preparationValidCandidateIndices.Sum(i=>NavContract.LocationWireBytes(input.Source.Candidates[i])))));
            try{
                var valid=NavRetainedArray<int>.Copy(preparationValidCandidateIndices,rootReservation);
                try{preparationCandidateIndices.Set(preparationPageIndex,valid);
                    if(valid.Count>0){var locations=valid.Select(i=>input.Source.Candidates[i]).ToArray();var region=new NavTerminalRegion("arrival-"+input.Source.StableKey.Substring(0,20)+"-"+key.Substring(0,20)+"-"+preparationPageIndex,input.Source.Revision,locations[0],locations);
                        if(rootReservation==null)preparationRegions.Set(preparationPageIndex,region);
                        else{var owned=NavTerminalRegion.CopyOwned(region,OwnedReservation);try{preparationRegions.Set(preparationPageIndex,owned);}finally{owned.StorageRoot.Release();}}
                    }
                }finally{valid.Release();}
            }finally{scratch?.Dispose();}
        }
        internal void SealPrepared()
        {
            MutableRoot();historicalState?.Seal();preparedClearances.Seal();preparationValidCandidateIndices.Seal();preparationCandidateIndices?.Seal();preparationRegions?.Seal();pageRequests.Seal();pageResults.Seal();memberOutcomes.Seal();assignments.Seal();scheduler.SealPrepared();rootReservation=null;rootFrozen=true;
        }
        internal NavigationArrivalWork ForkOwned(INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,this);if(!rootFrozen||rootFaulted)throw new InvalidOperationException("Seal arrival work before preparing a fork.");
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(LayoutBytes);var fork=allocation.Register(new NavigationArrivalWork(true));fork.Retain();fork.rootReservation=reservation.TryReserve;fork.queryMode=queryMode;fork.groupQueryMode=groupQueryMode;fork.demandQueryMode=demandQueryMode;fork.boundedCorridorMode=boundedCorridorMode;
                try{
                    ForkHistoricalInto(fork,reservation);
                    fork.input=input;fork.ownedInput=NavPhysicalMacro.Acquire(fork,ownedInput);fork.membersStorage=NavPhysicalMacro.Acquire(fork,membersStorage);fork.members=members;fork.pageStartsStorage=NavPhysicalMacro.Acquire(fork,pageStartsStorage);fork.pageCountsStorage=NavPhysicalMacro.Acquire(fork,pageCountsStorage);fork.pageStarts=pageStarts;fork.pageCounts=pageCounts;
                    fork.workingKeys=NavPhysicalMacro.Acquire(fork,workingKeys);fork.preparingKeyStorage=NavPhysicalMacro.Acquire(fork,preparingKeyStorage);fork.preparingClearanceKey=preparingClearanceKey;
                    fork.preparedClearances=preparedClearances.ForkOwned(reservation);fork.preparationValidCandidateIndices=preparationValidCandidateIndices.ForkOwned(reservation);fork.preparationCandidateIndices=preparationCandidateIndices?.ForkOwned(reservation);fork.preparationRegions=preparationRegions?.ForkOwned(reservation);
                    fork.pageRequests=pageRequests.ForkOwned(reservation);fork.pageResults=pageResults.ForkOwned(reservation);fork.memberOutcomes=memberOutcomes.ForkOwned(reservation);fork.assignments=assignments.ForkOwned(reservation);fork.scheduler=scheduler.ForkOwned(reservation);
                    fork.lastEpoch=lastEpoch;fork.authorityReservationBase=authorityReservationBase;fork.allocationRemaining=allocationRemaining;fork.memberIndex=memberIndex;fork.pageIndex=pageIndex;fork.candidateCursor=candidateCursor;fork.occupantCursor=occupantCursor;fork.preparationCursor=preparationCursor;fork.preparationPageIndex=preparationPageIndex;
                    fork.candidateValidated=candidateValidated;fork.candidateBlocked=candidateBlocked;fork.memberSawBlocked=memberSawBlocked;fork.memberAllUnreachable=memberAllUnreachable;fork.memberRouteCount=memberRouteCount;fork.memberFailure=memberFailure;fork.phase=phase;fork.Status=Status;
                    fork.AllocationWorkConsumed=AllocationWorkConsumed;fork.CandidateWorkConsumed=CandidateWorkConsumed;fork.OccupancyWorkConsumed=OccupancyWorkConsumed;fork.DirectWorkConsumed=DirectWorkConsumed;fork.LastAdvanceConsumed=LastAdvanceConsumed;fork.LastAdvanceRetentionBlocked=LastAdvanceRetentionBlocked;fork.derivativesDiscarded=derivativesDiscarded;
                    return fork;
                }catch{fork.ReleaseOwner();throw;}
            }
        }
        private NavigationArrivalWork(bool physical):base(LayoutBytes){physicalLifetime=physical;}
        internal override void ReleaseOwner(){if(rootReleased)return;rootReleased=true;Release();}
        protected override void ReleaseEdges()
        {
            // Legacy proposals have no declaration owner; keep their original route edge.
            if(!IsRegistered&&assignments!=null)foreach(var proposal in assignments)proposal.Route?.Release();
            historicalState?.Release();
            ownedInput?.Release();membersStorage?.Release();pageStartsStorage?.Release();pageCountsStorage?.Release();workingKeys?.Release();preparingKeyStorage?.Release();
            preparedClearances?.Release();preparationValidCandidateIndices?.Release();preparationCandidateIndices?.Release();preparationRegions?.Release();pageRequests?.Release();pageResults?.Release();memberOutcomes?.Release();assignments?.Release();scheduler?.Release();rootReservation=null;
        }
        internal sealed partial class PreparedClearance : NavOwnedStorage
        {
            internal readonly NavPagedList<NavRetainedArray<int>> CandidateIndices;internal readonly NavPagedList<NavTerminalRegion> Regions;internal readonly NavOwnedText Key;
            private PreparedClearance(NavPagedList<NavRetainedArray<int>> indices,NavPagedList<NavTerminalRegion> regions,NavOwnedText key):base(128){CandidateIndices=indices;Regions=regions;Key=key;}
            internal static PreparedClearance Create(NavPagedList<NavRetainedArray<int>> indices,NavPagedList<NavTerminalRegion> regions,NavOwnedText key,Func<long,bool> reserve)
            {using(var allocation=new NavStorageAllocation(reserve)){allocation.Reserve(128);var result=allocation.Register(new PreparedClearance(indices,regions,key));result.RequireEdge(indices);result.RequireEdge(regions);if(key!=null)result.RequireEdge(key);indices.Retain();regions.Retain();key?.Retain();result.Retain();return result;}}
            protected override void ReleaseEdges(){CandidateIndices?.Release();Regions?.Release();Key?.Release();}
        }
        internal sealed partial class RouteRow : NavOwnedStorage
        {
            internal readonly int Candidate;internal readonly NavScheduledRequest Request;internal readonly NavSolveStatus Status;internal readonly NavTypedRoute Route;
            private NavOwnedStorage recipe;
            private RouteRow(int candidate,NavScheduledRequest request,NavSolveStatus status,NavTypedRoute route):base(160){Candidate=candidate;Request=request;Status=status;Route=route;}
            internal static RouteRow Create(int candidate,NavScheduledRequest request,NavSolveStatus status,NavTypedRoute route,Func<long,bool> reserve)
            {if(reserve?.Target is INavStorageReservation r){NavStorageAdmission.RequireOwned(r,request?.StorageRoot);if(route!=null)NavStorageAdmission.RequireOwned(r,route);}
                using(var allocation=new NavStorageAllocation(reserve)){allocation.Reserve(160);var row=allocation.Register(new RouteRow(candidate,request,status,route));row.recipe=request?.StorageRoot;row.recipe?.Retain();route?.Retain();row.Retain();return row;}}
            protected override void ReleaseEdges(){recipe?.Release();Route?.Release();}
        }
    }
}
