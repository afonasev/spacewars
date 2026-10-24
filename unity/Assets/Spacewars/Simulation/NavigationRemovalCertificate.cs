using System;
using System.Linq;
namespace Spacewars.Simulation
{
    public sealed partial class NavigationArrivalWork
    {
        // The same paid normal preparation serves ordinary work and the new
        // certificate. Full current source/pages are retained, never pruned.
        private bool RemovalActive=>removalMode&&historicalOracle!=null&&input.Source.Candidates.Count<historicalOracle.input.Source.Candidates.Count;
        private int ActivePreparationMember=>RemovalActive&&historicalOracle!=null&&historicalGate==4&&historicalState.PreparationMember<members.Length?historicalState.PreparationMember:memberIndex;
        private bool PrepareRegionsFor(int member)
        {
            string key=WorkingKey(members[member]);
            if(preparedClearances.ContainsKey(key))return true;
            if(preparingClearanceKey!=key)BeginPreparation(key);
            while(true){
                if(preparationPageIndex>=pageStarts.Length){AddPrepared(key,preparationCandidateIndices,preparationRegions);ClearPreparation();return true;}
                int end=pageStarts[preparationPageIndex]+pageCounts[preparationPageIndex];
                if(preparationCursor>=end){FinishPreparationPage(key);preparationPageIndex++;preparationValidCandidateIndices.Clear();preparationCursor=preparationPageIndex<pageStarts.Length?pageStarts[preparationPageIndex]:0;continue;}
                if(allocationRemaining==0)return false;
                SpendCandidate();int raw=preparationCursor++;
                if(input.WorkingFor(members[member]).Provider.IsValid(input.Source.Candidates[raw],members[member].Clearance))preparationValidCandidateIndices.Add(raw);
            }
        }
        private void ResetRemoval()
        {if(historicalState==null)return;historicalState.SourceOld=historicalState.PreparationMember=historicalState.RegionCursor=historicalState.RemovalStage=historicalState.EndpointCursor=0;historicalState.EndpointFound=false;}
        private int[] RemovalSnapshot()=>new[]{historicalState.SourceOld,historicalState.PreparationMember,historicalState.RegionCursor,historicalState.RemovalStage,historicalState.EndpointCursor,historicalState.EndpointFound?1:0};
        private void RestoreRemoval(int[] s)
        {
            if(s==null||s.Length!=6||s[5]<0||s[5]>1)throw new ArgumentException("Missing removal continuation.");
            historicalState.SourceOld=s[0];historicalState.PreparationMember=s[1];historicalState.RegionCursor=s[2];historicalState.RemovalStage=s[3];historicalState.EndpointCursor=s[4];historicalState.EndpointFound=s[5]!=0;
        }
        // Gate2: one old source row per already-paid Candidate operation.
        // Inspect the complete old sequence; insertion/reordering cannot match.
        private bool RemovalSubsequenceStep()
        {
            int old=historicalState.SourceOld;
            if(old==historicalOracle.input.Source.Candidates.Count){if(historicalCursor!=input.Source.Candidates.Count)return false;historicalGate=3;historicalCursor=0;return true;}
            if(historicalCursor<input.Source.Candidates.Count&&input.Source.Candidates[historicalCursor].Equals(historicalOracle.input.Source.Candidates[old]))historicalCursor++;
            historicalState.SourceOld++;return true;
        }
        private bool RemovalOraclePageZero()=>historicalOracle.pageIndex==0&&historicalOracle.memberOutcomes.Count==0;
        private NavTerminalRegion RemovalRegion(NavigationArrivalWork work,int member)
        {
            if(work.pageStarts.Length==0||work.pageStarts[0]!=0||!work.preparedClearances.TryGetValue(work.WorkingKey(work.members[member]),out var prepared))return null;
            return prepared.Regions[0];
        }
        private bool RemovalOldRecipe(int member,NavTerminalRegion old)
        {
            var oracle=historicalOracle;
            if(member<oracle.assignments.Count){var a=oracle.assignments[member];return a.Member.EntityId==members[member].EntityId&&a.Proof.PhysicalRecipe?.Region?.StableKey==old.StableKey;}
            if(member!=oracle.memberIndex)return true;
            // Minimal query case only. Multiple speculative page requests or
            // missing old preparation take the existing whole-group fallback.
            return oracle.pageRequests.Count<=1&&(oracle.pageRequests.Count==0||oracle.pageRequests[0].Request.Region?.StableKey==old.StableKey);
        }
        private bool PrepareRemovalCertificate()
        {
            while(historicalState.PreparationMember<members.Length){
                if(!RemovalOraclePageZero()){DropHistorical();return false;}
                int index=historicalState.PreparationMember;
                if(historicalState.RemovalStage==0){
                    // Header/control admission is itself paid, including reuse
                    // of a prepared clearance. No free member-sized traversal.
                    if(allocationRemaining==0)return false;SpendCandidate();
                    if(RemovalRegion(historicalOracle,index)==null){DropHistorical();return false;}
                    historicalState.RemovalStage=1;
                }
                if(historicalState.RemovalStage==1){
                    if(!PrepareRegionsFor(index))return false;
                    if(allocationRemaining==0)return false;SpendCandidate();
                    var old=RemovalRegion(historicalOracle,index);var current=RemovalRegion(this,index);
                    if(current==null||old.Candidates.Count!=current.Candidates.Count||!old.Requested.Equals(current.Requested)||!RemovalOldRecipe(index,old)){DropHistorical();return false;}
                    historicalState.RemovalStage=2;
                }
                var previous=RemovalRegion(historicalOracle,index);var now=RemovalRegion(this,index);
                while(historicalState.RegionCursor<now.Candidates.Count){
                    if(allocationRemaining==0)return false;SpendCandidate();int n=historicalState.RegionCursor;
                    if(!now.Candidates[n].Equals(previous.Candidates[n])){DropHistorical();return false;}
                    historicalState.RegionCursor++;
                }
                historicalState.RegionCursor=0;historicalState.RemovalStage=0;historicalState.PreparationMember++;
            }
            return true;
        }
        private bool RemovalEndpointMember(NavArrivalProposal old)
        {
            var region=RemovalRegion(this,historicalMember);
            while(!historicalState.EndpointFound&&historicalState.EndpointCursor<region.Candidates.Count){
                if(allocationRemaining==0)return false;SpendCandidate();int n=historicalState.EndpointCursor++;
                historicalState.EndpointFound=region.Candidates[n].Equals(old.Endpoint);
            }
            if(!historicalState.EndpointFound){DropHistorical();return false;}return true;
        }
        private void ValidateRemovalCertificate()
        {
            var s=historicalState;int count=historicalOracle.input.Source.Candidates.Count;
            if(s.SourceOld<0||s.SourceOld>count||s.PreparationMember<0||s.PreparationMember>members.Length||s.RemovalStage<0||s.RemovalStage>2||s.RegionCursor<0||s.RegionCursor>64||s.EndpointCursor<0||s.EndpointCursor>64)throw new ArgumentException("Removal cursor dimensions mismatch.");
            int matched=0;for(int i=0;i<s.SourceOld;i++)if(matched<input.Source.Candidates.Count&&input.Source.Candidates[matched].Equals(historicalOracle.input.Source.Candidates[i]))matched++;
            if(historicalGate<2&&s.SourceOld!=0||historicalGate==2&&historicalCursor!=matched||historicalGate>2&&(s.SourceOld!=count||matched!=input.Source.Candidates.Count))throw new ArgumentException("Removal source prefix mismatch.");
            if(historicalGate<4&&(s.PreparationMember!=0||s.RegionCursor!=0||s.RemovalStage!=0)||s.PreparationMember==members.Length&&(s.RegionCursor!=0||s.RemovalStage!=0))throw new ArgumentException("Removal preparation phase mismatch.");
            for(int m=0;m<s.PreparationMember+(s.RemovalStage==2?1:0);m++){
                var old=RemovalRegion(historicalOracle,m);var current=RemovalRegion(this,m);int checkedCount=m<s.PreparationMember?current?.Candidates.Count??-1:s.RegionCursor;
                if(old==null||current==null||old.Candidates.Count!=current.Candidates.Count||!old.Requested.Equals(current.Requested)||checkedCount<0||checkedCount>current.Candidates.Count||!RemovalOldRecipe(m,old))throw new ArgumentException("Removal prepared region mismatch.");
                for(int n=0;n<checkedCount;n++)if(!old.Candidates[n].Equals(current.Candidates[n]))throw new ArgumentException("Removal region prefix mismatch.");
            }
            if(s.EndpointCursor>0||s.EndpointFound){
                if(s.PreparationMember!=members.Length||historicalOracle.Status!=NavArrivalStatus.Ready)throw new ArgumentException("Removal endpoint check lacks completed source.");
                var region=RemovalRegion(this,historicalMember);if(s.EndpointCursor>region.Candidates.Count)throw new ArgumentException("Removal endpoint cursor overflow.");
                bool found=false;for(int n=0;n<s.EndpointCursor;n++)found|=region.Candidates[n].Equals(historicalOracle.assignments[historicalMember].Endpoint);
                if(found!=s.EndpointFound)throw new ArgumentException("Removal endpoint prefix mismatch.");
            }
        }
    }
}
