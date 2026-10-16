using System;
using System.Linq;
namespace Spacewars.Simulation
{
    public sealed partial class NavigationArrivalWork : INavPhysicalBody
    {
        NavPhysicalTag INavPhysicalBody.PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.ArrivalWork);
        void INavPhysicalBody.WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!IsImmutableCaptureRoot||rootReservation!=null||restoredReady!=null||StorageBytes!=NavStorageLayout.Charge(LayoutBytes))throw new ArgumentException("Physical arrival Work callback/layout contradiction.");
            ValidatePhysicalShape();
            NavPhysicalMacro.WriteEdge(writer,this,ownedInput,true);
            NavPhysicalMacro.WriteEdge(writer,this,membersStorage);NavPhysicalMacro.WriteEdge(writer,this,pageStartsStorage);NavPhysicalMacro.WriteEdge(writer,this,pageCountsStorage);NavPhysicalMacro.WriteEdge(writer,this,workingKeys);
            NavPhysicalMacro.WriteEdge(writer,this,preparedClearances);NavPhysicalMacro.WriteEdge(writer,this,preparationValidCandidateIndices);NavPhysicalMacro.WriteEdge(writer,this,preparationCandidateIndices,true);NavPhysicalMacro.WriteEdge(writer,this,preparationRegions,true);NavPhysicalMacro.WriteEdge(writer,this,preparingKeyStorage,true);
            NavPhysicalMacro.WriteEdge(writer,this,pageRequests);NavPhysicalMacro.WriteEdge(writer,this,pageResults);NavPhysicalMacro.WriteEdge(writer,this,memberOutcomes);NavPhysicalMacro.WriteEdge(writer,this,assignments);NavPhysicalMacro.WriteEdge(writer,this,scheduler);
            WritePhysicalScalars(writer.Output);
        }
        internal static NavigationArrivalWork ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var input=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader,true);if(input!=null&&!(input.Facade is NavArrivalInput))throw new ArgumentException("Wrong physical Work input facade.");
            var members=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavArrivalMember>>(reader);var starts=NavPhysicalMacro.ReadEdge<NavRetainedArray<int>>(reader);var counts=NavPhysicalMacro.ReadEdge<NavRetainedArray<int>>(reader);var keys=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavOwnedText>>(reader);
            var clearances=NavPhysicalMacro.ReadEdge<NavPersistentIndex<NavNameKey,PreparedClearance>>(reader);var valid=NavPhysicalMacro.ReadEdge<NavPagedList<int>>(reader);var indices=NavPhysicalMacro.ReadEdge<NavPagedList<NavRetainedArray<int>>>(reader,true);var regions=NavPhysicalMacro.ReadEdge<NavPagedList<NavTerminalRegion>>(reader,true);var preparing=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader,true);
            var requests=NavPhysicalMacro.ReadEdge<NavPagedList<RouteRow>>(reader);var results=NavPhysicalMacro.ReadEdge<NavPersistentIndex<int,RouteRow>>(reader);var outcomes=NavPhysicalMacro.ReadEdge<NavPersistentIndex<long,NavArrivalStatus>>(reader);var proposals=NavPhysicalMacro.ReadEdge<NavPagedList<NavArrivalProposal>>(reader);var scheduler=NavPhysicalMacro.ReadEdge<LayeredNavigationScheduler>(reader);
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(LayoutBytes);var work=allocation.Register(new NavigationArrivalWork(true){rootFrozen=true});
                work.ownedInput=NavPhysicalMacro.Acquire(work,input);work.input=(NavArrivalInput)input?.Facade;
                work.membersStorage=NavPhysicalMacro.Acquire(work,members);work.members=members.ReadOnlyBacking;work.pageStartsStorage=NavPhysicalMacro.Acquire(work,starts);work.pageStarts=starts.ReadOnlyBacking;work.pageCountsStorage=NavPhysicalMacro.Acquire(work,counts);work.pageCounts=counts.ReadOnlyBacking;work.workingKeys=NavPhysicalMacro.Acquire(work,keys);
                work.preparedClearances=NavPhysicalMacro.Acquire(work,clearances);work.preparationValidCandidateIndices=NavPhysicalMacro.Acquire(work,valid);work.preparationCandidateIndices=NavPhysicalMacro.Acquire(work,indices);work.preparationRegions=NavPhysicalMacro.Acquire(work,regions);work.preparingKeyStorage=NavPhysicalMacro.Acquire(work,preparing);work.preparingClearanceKey=preparing?.Value;
                work.pageRequests=NavPhysicalMacro.Acquire(work,requests);work.pageResults=NavPhysicalMacro.Acquire(work,results);work.memberOutcomes=NavPhysicalMacro.Acquire(work,outcomes);work.assignments=NavPhysicalMacro.Acquire(work,proposals);work.scheduler=NavPhysicalMacro.Acquire(work,scheduler);
                work.ReadPhysicalScalars(reader);work.ValidatePhysicalShape();work.ValidatePhysicalPreparationMembership(reader.Reservation);
                if(work.input!=null)using(NavStorageScratch.Reserve(reader.Reservation,checked(8192+128L*members.Count))){for(int n=0;n<members.Count;n++){
                    var member=members[n];var binding=work.input.WorkingFor(member);long wire=checked(256+NavContract.StringWireBytes(member.Clearance.Id)+NavContract.StringWireBytes(member.Clearance.MobilityClass)+NavContract.StringWireBytes(binding.Provider.Binding)+NavContract.StringWireBytes(binding.Profile.ExactProfileBinding));
                    using(NavStorageScratch.Reserve(reader.Reservation,NavContract.DigestScratchBytes(wire)))if(keys[n].Value!=work.RawWorkingKey(member))throw new ArgumentException("Physical arrival working-key digest changed.");
                }}
                return work;
            }
        }
        private void WritePhysicalScalars(System.IO.BinaryWriter output)
        {
            output.Write(lastEpoch);
            output.Write(authorityReservationBase);
            output.Write(AllocationWorkConsumed);
            output.Write(CandidateWorkConsumed);
            output.Write(OccupancyWorkConsumed);
            output.Write(DirectWorkConsumed);
            output.Write(LastAdvanceConsumed);
            output.Write(allocationRemaining);
            output.Write(memberIndex);
            output.Write(pageIndex);
            output.Write(candidateCursor);
            output.Write(occupantCursor);
            output.Write(preparationCursor);
            output.Write(preparationPageIndex);
            output.Write(memberRouteCount);
            output.Write(candidateValidated);
            output.Write(candidateBlocked);
            output.Write(memberSawBlocked);
            output.Write(memberAllUnreachable);
            output.Write(LastAdvanceRetentionBlocked);
            output.Write(derivativesDiscarded);
            output.Write((int)memberFailure);
            output.Write((int)phase);
            output.Write((int)Status);
        }
        private void ReadPhysicalScalars(NavPhysicalBodyReader reader)
        {
            lastEpoch=reader.Input.ReadInt64();
            authorityReservationBase=reader.Input.ReadInt64();
            AllocationWorkConsumed=reader.Input.ReadInt64();
            CandidateWorkConsumed=reader.Input.ReadInt64();
            OccupancyWorkConsumed=reader.Input.ReadInt64();
            DirectWorkConsumed=reader.Input.ReadInt64();
            LastAdvanceConsumed=reader.Input.ReadInt64();
            allocationRemaining=reader.Input.ReadInt32();
            memberIndex=reader.Input.ReadInt32();
            pageIndex=reader.Input.ReadInt32();
            candidateCursor=reader.Input.ReadInt32();
            occupantCursor=reader.Input.ReadInt32();
            preparationCursor=reader.Input.ReadInt32();
            preparationPageIndex=reader.Input.ReadInt32();
            memberRouteCount=reader.Input.ReadInt32();
            candidateValidated=NavPhysicalMacro.ReadBoolean(reader);
            candidateBlocked=NavPhysicalMacro.ReadBoolean(reader);
            memberSawBlocked=NavPhysicalMacro.ReadBoolean(reader);
            memberAllUnreachable=NavPhysicalMacro.ReadBoolean(reader);
            LastAdvanceRetentionBlocked=NavPhysicalMacro.ReadBoolean(reader);
            derivativesDiscarded=NavPhysicalMacro.ReadBoolean(reader);
            memberFailure=(NavArrivalStatus)reader.Input.ReadInt32();
            phase=(Phase)reader.Input.ReadInt32();
            Status=(NavArrivalStatus)reader.Input.ReadInt32();
        }
        private void ValidatePhysicalShape()
        {
            if(membersStorage==null||pageStartsStorage==null||pageCountsStorage==null||workingKeys==null||preparedClearances==null||preparationValidCandidateIndices==null||pageRequests==null||pageResults==null||memberOutcomes==null||assignments==null||scheduler==null||!ReferenceEquals(members,membersStorage.ReadOnlyBacking)||!ReferenceEquals(pageStarts,pageStartsStorage.ReadOnlyBacking)||!ReferenceEquals(pageCounts,pageCountsStorage.ReadOnlyBacking)||workingKeys.Count!=members.Length||pageStarts.Length!=pageCounts.Length||members.Length<1||!Enum.IsDefined(typeof(Phase),phase)||!Enum.IsDefined(typeof(NavArrivalStatus),Status)||!Enum.IsDefined(typeof(NavArrivalStatus),memberFailure)||restoredReady!=null)throw new ArgumentException("Physical arrival Work storage/phase shape contradiction.");
            if(lastEpoch<-1||authorityReservationBase<0||allocationRemaining<0||memberIndex<0||memberIndex>members.Length||pageIndex<0||pageIndex>pageStarts.Length||candidateCursor<0||occupantCursor<0||preparationCursor<0||preparationPageIndex<0||preparationPageIndex>pageStarts.Length||memberRouteCount<0||AllocationWorkConsumed<0||CandidateWorkConsumed<0||OccupancyWorkConsumed<0||DirectWorkConsumed<0||LastAdvanceConsumed<0||LastAdvanceConsumed>AllocationWorkConsumed||AllocationWorkConsumed!=checked(CandidateWorkConsumed+OccupancyWorkConsumed)||lastEpoch==-1&&(allocationRemaining!=0||AllocationWorkConsumed!=0))throw new ArgumentException("Physical arrival Work cursor/accounting contradiction.");
            bool terminal=Status!=NavArrivalStatus.Pending&&Status!=NavArrivalStatus.BudgetExhausted;if(terminal!=(phase==Phase.Finished))throw new ArgumentException("Physical arrival Work terminal phase contradiction.");
            if((input==null)!=derivativesDiscarded)throw new ArgumentException("Physical discarded Work input contradiction.");
            if(input==null){if(ownedInput!=null||assignments.Count!=0||pageRequests.Count!=0||pageResults.Count!=0||preparedClearances.Count!=0||preparingKeyStorage!=null||preparationCandidateIndices!=null||preparationRegions!=null||scheduler.PendingCount!=0||scheduler.ReadyCount!=0)throw new ArgumentException("Cancelled Work retained live derivatives.");return;}
            if(!ReferenceEquals(input.StorageRoot,ownedInput)||members.Length!=input.Members.Count||ReferenceEquals(members,input.MembersBacking)||ReferenceEquals(membersStorage,((NavRetainedArray<NavOwnedStorage>)((NavDeclarationStorage)ownedInput).Seventh)[1])||scheduler.AttemptBinding!=(input.IndependentMembers?input.StableKey:null))throw new ArgumentException("Physical arrival Work input/member/attempt contradiction.");
            int covered=0;for(int n=0;n<pageStarts.Length;n++){
                if(pageStarts[n]!=covered||pageCounts[n]<1||pageCounts[n]>64||covered>input.Source.Candidates.Count-pageCounts[n])throw new ArgumentException("Physical arrival page coverage contradiction.");string surface=input.Source.Candidates[covered].SurfaceId;
                for(int i=0;i<pageCounts[n];i++)if(input.Source.Candidates[covered+i].SurfaceId!=surface)throw new ArgumentException("Physical arrival page spans surfaces.");covered+=pageCounts[n];
                if(n+1<pageStarts.Length&&pageCounts[n]<64&&input.Source.Candidates[covered].SurfaceId==surface)throw new ArgumentException("Physical arrival page was split before original boundary.");
            }
            if(covered!=input.Source.Candidates.Count)throw new ArgumentException("Physical arrival pages omit candidates.");for(int n=0;n<members.Length;n++)if(!ReferenceEquals(members[n],input.Members[n])||workingKeys[n]==null)throw new ArgumentException("Physical arrival member alias changed.");
            foreach(var entry in preparedClearances){var prepared=entry.Value;if(prepared==null||!ReferenceEquals(entry.Key.Reference.Owner,prepared.Key)||entry.Key.Reference.Role!=NavNameRole.Text||entry.Key.Reference.Index!=-1||!ReferenceEquals(entry.Key.Value,prepared.Key?.Value)||prepared.CandidateIndices.Count!=pageStarts.Length||prepared.Regions.Count!=pageStarts.Length)throw new ArgumentException("Physical prepared clearance owner/shape contradiction.");ValidateCandidateRegions(prepared.CandidateIndices,prepared.Regions,pageStarts.Length,prepared.Key.Value);}
            if(preparingKeyStorage==null){if(preparingClearanceKey!=null||preparationCandidateIndices!=null||preparationRegions!=null)throw new ArgumentException("Physical empty preparation has hidden roots.");}
            else{
                if(!ReferenceEquals(preparingClearanceKey,preparingKeyStorage.Value)||memberIndex>=members.Length||!ReferenceEquals(preparingKeyStorage,workingKeys[memberIndex])||phase!=Phase.PrepareRegions||preparationCandidateIndices==null||preparationRegions==null||preparationCandidateIndices.Count!=pageStarts.Length||preparationRegions.Count!=pageStarts.Length||preparedClearances.ContainsKey(preparingClearanceKey))throw new ArgumentException("Physical partial preparation key/shape contradiction.");
                ValidateCandidateRegions(preparationCandidateIndices,preparationRegions,preparationPageIndex,preparingClearanceKey);
                if(preparationPageIndex<pageStarts.Length){int start=pageStarts[preparationPageIndex],end=start+pageCounts[preparationPageIndex];if(preparationCursor<start||preparationCursor>end)throw new ArgumentException("Physical preparation cursor outside page.");int previous=start-1;foreach(int index in preparationValidCandidateIndices){if(index<=previous||index<start||index>=preparationCursor)throw new ArgumentException("Physical partial valid-index prefix contradiction.");previous=index;}}
                else if(preparationCursor!=0||preparationValidCandidateIndices.Count!=0)throw new ArgumentException("Physical completed preparation cursor contradiction.");
            }
            if((phase==Phase.ScanCandidates||phase==Phase.SolvePage)&&memberIndex<members.Length&&pageIndex<pageStarts.Length){
                if(!preparedClearances.TryGetValue(WorkingKey(members[memberIndex]),out var prepared))throw new ArgumentException("Physical active member lacks clearance preparation.");int count=prepared.CandidateIndices[pageIndex].Count;if(candidateCursor>count||phase==Phase.SolvePage&&candidateCursor!=count||occupantCursor>input.Occupancy.Occupants.Count+assignments.Count||!candidateValidated&&occupantCursor!=0)throw new ArgumentException("Physical candidate scan/solve cursor contradiction.");
            }
            foreach(var entry in pageResults){if(entry.Value==null||entry.Key!=entry.Value.Candidate)throw new ArgumentException("Physical result candidate mismatch.");int matches=0;foreach(var row in pageRequests)if(ReferenceEquals(row,entry.Value))matches++;if(matches!=1)throw new ArgumentException("Physical results must alias one exact request row.");}
            for(int n=0;n<pageRequests.Count;n++){
                var row=pageRequests[n];if(row==null||row.Candidate<0||row.Candidate>=input.Source.Candidates.Count||memberIndex>=members.Length||pageIndex>=pageStarts.Length||n>0&&row.Candidate<=pageRequests[n-1].Candidate)throw new ArgumentException("Physical request row order/candidate contradiction.");
                var member=members[memberIndex];var working=input.WorkingFor(member);var request=row.Request;
                if(!ReferenceEquals(request.Provider,working.Provider)||!ReferenceEquals(request.Clearance,member.Clearance)||!ReferenceEquals(request.Profile,working.Profile)||!ReferenceEquals(request.Limits,input.Limits)||!preparedClearances.TryGetValue(WorkingKey(member),out var clearance)||!ReferenceEquals(request.Region,clearance.Regions[pageIndex])||!request.Subscription.Origin.Equals(member.Origin)||!request.Subscription.AssignedEndpoint.Equals(input.Source.Candidates[row.Candidate])||request.Subscription.EntityId!=member.EntityId||request.Subscription.Incarnation!=member.Incarnation||request.Subscription.WorldGeneration!=member.WorldGeneration||request.Subscription.GroupId!=member.GroupId||request.Subscription.RootOrderRevision!=member.RootOrderRevision||request.Subscription.CommandSequence!=member.CommandSequence||request.Subscription.IssuedTick!=member.IssuedTick||request.Subscription.ActivationRevision!=member.ActivationRevision||request.Subscription.MemberOrderRevision!=member.MemberOrderRevision||request.Subscription.MobilityRevision!=member.MobilityRevision||request.Subscription.RequestSequence!=member.RequestSequence||request.Subscription.HoldBinding!=member.HoldBinding||request.Subscription.Provenance!=NavRouteProvenance.CommandMember)throw new ArgumentException("Physical request exact member/recipe/region selectors changed.");
                var candidates=clearance.CandidateIndices[pageIndex];int position=-1;for(int i=0;i<candidates.Count;i++)if(candidates[i]==row.Candidate){position=i;break;}
                if(position<0||position>=candidateCursor||request.MaxLabels!=input.MaxLabels||request.Singleton!=(working.Provider is PartitionNavigationProvider&&!input.IndependentMembers&&members.Length==1))throw new ArgumentException("Physical request candidate/cursor/limits/singleton differs from original CreateRequest.");
                if(pageResults.TryGetValue(row.Candidate,out var completed)){if(!ReferenceEquals(row,completed)||row.Status==NavSolveStatus.Pending||(row.Status==NavSolveStatus.Ready)!=(row.Route!=null))throw new ArgumentException("Physical completion status/alias contradiction.");}
                else if(row.Route!=null||row.Status!=default(NavSolveStatus))throw new ArgumentException("Physical unresolved row has hidden result.");
            }
            int processed=0;for(int n=0;n<members.Length;n++){
                int proposals=0;foreach(var proposal in assignments)if(ReferenceEquals(proposal.Member,members[n])){proposals++;if(!ReferenceEquals(proposal.Route,proposal.Proof.Route)||!proposal.Proof.ValidateAgainst(input,members[n],proposal.Endpoint))throw new ArgumentException("Physical proposal proof/member contradiction.");}
                bool outcome=memberOutcomes.TryGetValue(members[n].EntityId,out var status);if(proposals>1||proposals>0&&outcome||proposals>0&&n>=memberIndex||outcome&&n>=memberIndex||outcome&&(status!=NavArrivalStatus.NoAvailableSlot&&status!=NavArrivalStatus.UnreachableInGraph&&status!=NavArrivalStatus.CapacityExceeded&&status!=NavArrivalStatus.InvalidInput))throw new ArgumentException("Physical member outcome/proposal coverage contradiction.");if(proposals!=0||outcome)processed++;
            }
            if(processed!=assignments.Count+memberOutcomes.Count||input.IndependentMembers&&Status!=NavArrivalStatus.Stale&&processed!=memberIndex||Status==NavArrivalStatus.Ready&&(memberIndex!=members.Length||scheduler.PendingCount!=0||scheduler.ReadyCount!=0))throw new ArgumentException("Physical processed-member/Ready coverage contradiction.");
        }
        private void ValidatePhysicalPreparationMembership(INavStorageReservation reservation)
        {
            if(input==null)return;
            using(NavStorageScratch.Reserve(reservation,8192)){
                void Prefix(NavRetainedArray<int> indices,int first,int end,NavArrivalMember member){
                    int n=0;var provider=input.WorkingFor(member).Provider;
                    for(int candidate=first;candidate<end;candidate++)if(provider.IsValid(input.Source.Candidates[candidate],member.Clearance)){
                        if(n>=indices.Count||indices[n++]!=candidate)throw new ArgumentException("Physical prepared valid-prefix differs from original provider qualification.");
                    }
                    if(n!=indices.Count)throw new ArgumentException("Physical valid-prefix has an extra candidate.");
                }
                foreach(var entry in preparedClearances){int owner=-1;for(int n=0;n<workingKeys.Count;n++)if(ReferenceEquals(workingKeys[n],entry.Value.Key)){owner=n;break;}
                    if(owner<0)throw new ArgumentException("Prepared working key is outside exact Work key owners.");
                    for(int page=0;page<pageStarts.Length;page++)Prefix(entry.Value.CandidateIndices[page],pageStarts[page],pageStarts[page]+pageCounts[page],members[owner]);
                }
                if(preparingKeyStorage!=null){for(int page=0;page<preparationPageIndex;page++)Prefix(preparationCandidateIndices[page],pageStarts[page],pageStarts[page]+pageCounts[page],members[memberIndex]);
                    if(preparationPageIndex<pageStarts.Length){int n=0;var member=members[memberIndex];var provider=input.WorkingFor(member).Provider;
                        for(int candidate=pageStarts[preparationPageIndex];candidate<preparationCursor;candidate++)if(provider.IsValid(input.Source.Candidates[candidate],member.Clearance)){
                            if(n>=preparationValidCandidateIndices.Count||preparationValidCandidateIndices[n++]!=candidate)throw new ArgumentException("Partial prepared valid-prefix differs from original qualification.");
                        }
                        if(n!=preparationValidCandidateIndices.Count)throw new ArgumentException("Partial valid-prefix has an extra candidate.");
                    }
                }
            }
        }
        private void ValidateCandidateRegions(NavPagedList<NavRetainedArray<int>> indices,NavPagedList<NavTerminalRegion> regions,int complete,string key)
        {
            for(int page=0;page<pageStarts.Length;page++){
                var row=indices[page];var region=regions[page];if(page>=complete){if(row!=null||region!=null)throw new ArgumentException("Physical preparation future page is populated.");continue;}
                if(row==null||row.Count>pageCounts[page]||(row.Count==0)!=(region==null)||region!=null&&(region.Candidates.Count!=row.Count||region.Revision!=input.Source.Revision||region.Id!="arrival-"+input.Source.StableKey.Substring(0,20)+"-"+key.Substring(0,20)+"-"+page))throw new ArgumentException("Physical prepared region dimensions/identity contradiction.");
                int previous=pageStarts[page]-1;for(int n=0;n<row.Count;n++){int index=row[n];if(index<=previous||index<pageStarts[page]||index>=pageStarts[page]+pageCounts[page]||region!=null&&!region.Candidates[n].Equals(input.Source.Candidates[index]))throw new ArgumentException("Physical prepared region candidate correspondence contradiction.");previous=index;}
                if(region!=null&&!region.Requested.Equals(input.Source.Candidates[row[0]]))throw new ArgumentException("Physical prepared region requested candidate changed.");
            }
        }
        internal sealed partial class PreparedClearance : INavPhysicalBody
        {
            internal override bool IsImmutableCaptureRoot=>IsRegistered;
            public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.ArrivalPreparedClearance);
            public void WritePhysicalBody(NavPhysicalBodyWriter writer){if(Key==null||!CandidateIndices.IsFrozen||!Regions.IsFrozen||StorageBytes!=NavStorageLayout.Charge(128))throw new ArgumentException("Physical prepared clearance must be owned/immutable.");NavPhysicalMacro.WriteEdge(writer,this,Key);NavPhysicalMacro.WriteEdge(writer,this,CandidateIndices);NavPhysicalMacro.WriteEdge(writer,this,Regions);}
            internal static PreparedClearance ReadPhysicalBody(NavPhysicalBodyReader reader){var key=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader);var indices=NavPhysicalMacro.ReadEdge<NavPagedList<NavRetainedArray<int>>>(reader);var regions=NavPhysicalMacro.ReadEdge<NavPagedList<NavTerminalRegion>>(reader);if(indices.Count!=regions.Count)throw new ArgumentException("Physical clearance array shape contradiction.");using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){allocation.Reserve(128);var value=allocation.Register(new PreparedClearance(indices,regions,key));indices.Retain();regions.Retain();key.Retain();return value;}}
        }
        internal sealed partial class RouteRow : INavPhysicalBody
        {
            internal override bool IsImmutableCaptureRoot=>IsRegistered;
            public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.ArrivalRouteRow);
            public void WritePhysicalBody(NavPhysicalBodyWriter writer){if(Candidate<0||Request==null||!ReferenceEquals(recipe,Request.StorageRoot)||!Enum.IsDefined(typeof(NavSolveStatus),Status)||Route!=null&&!ReferenceEquals(Route.Subscription,Request.Subscription)||StorageBytes!=NavStorageLayout.Charge(160))throw new ArgumentException("Physical route row recipe/status/layout contradiction.");writer.Output.Write(Candidate);writer.Output.Write((int)Status);NavPhysicalMacro.WriteEdge(writer,this,recipe);NavPhysicalMacro.WriteEdge(writer,this,Route,true);}
            internal static RouteRow ReadPhysicalBody(NavPhysicalBodyReader reader){int candidate=reader.Count(int.MaxValue);int status=reader.Input.ReadInt32();var recipe=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);var route=NavPhysicalMacro.ReadEdge<NavTypedRoute>(reader,true);if(!(recipe.Facade is NavScheduledRequest request)||!Enum.IsDefined(typeof(NavSolveStatus),status)||route!=null&&!ReferenceEquals(route.Subscription,request.Subscription)||route!=null&&(NavSolveStatus)status!=NavSolveStatus.Ready)throw new ArgumentException("Physical row recipe/result mismatch.");using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){allocation.Reserve(160);var value=allocation.Register(new RouteRow(candidate,request,(NavSolveStatus)status,route){recipe=recipe});recipe.Retain();route?.Retain();return value;}}
        }
    }
}
