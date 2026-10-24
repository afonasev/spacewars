using System;
using System.Linq;
namespace Spacewars.Simulation
{
    public sealed partial class NavigationArrivalWork
    {
        // Saved stages: 0 NotStarted, 1 OldRows, 2 CurrentRows, 3 Compare, 4 Complete.
        private int[] Page0OccupancySnapshot()=>new[]{historicalState.OccupancyStage,historicalState.OccupancyMember,historicalState.OccupancyCandidate,historicalState.OccupancyOldRow,historicalState.OccupancyCurrentRow,historicalState.OccupancyOldBlocked?1:0,historicalState.OccupancyCurrentBlocked?1:0};
        private void RestorePage0Occupancy(int[] values)
        {
            if(values==null||values.Length!=7||values[5]<0||values[5]>1||values[6]<0||values[6]>1)throw new ArgumentException("Missing page0 occupancy prefix.");
            var s=historicalState;s.OccupancyStage=values[0];s.OccupancyMember=values[1];s.OccupancyCandidate=values[2];s.OccupancyOldRow=values[3];s.OccupancyCurrentRow=values[4];s.OccupancyOldBlocked=values[5]!=0;s.OccupancyCurrentBlocked=values[6]!=0;
        }
        private void ResetPage0Occupancy()
        {if(historicalState==null)return;var s=historicalState;s.OccupancyStage=s.OccupancyMember=s.OccupancyCandidate=s.OccupancyOldRow=s.OccupancyCurrentRow=0;s.OccupancyOldBlocked=s.OccupancyCurrentBlocked=false;}
        private void ValidateEmptyPage0Occupancy()
        {
            if(historicalState==null)return;var s=historicalState;
            if(!page0OccupancyMode||historicalOracle==null){if(s.OccupancyStage!=0||s.OccupancyMember!=0||s.OccupancyCandidate!=0||s.OccupancyOldRow!=0||s.OccupancyCurrentRow!=0||s.OccupancyOldBlocked||s.OccupancyCurrentBlocked)throw new ArgumentException("Unexpected page0 occupancy progress.");}
        }
        private bool PreparePage0OccupancyRegions()
        {
            var s=historicalState;
            while(s.PreparationMember<members.Length){
                if(!RemovalOraclePageZero()){DropHistorical();return false;}
                int member=s.PreparationMember;
                if(s.RemovalStage==0){
                    if(allocationRemaining==0)return false;SpendCandidate();
                    if(RemovalRegion(historicalOracle,member)==null){DropHistorical();return false;}s.RemovalStage=1;
                }
                if(s.RemovalStage==1){
                    // Normal preparation scans the entire current ordered source, including later pages.
                    if(!PrepareRegionsFor(member))return false;
                    if(allocationRemaining==0)return false;SpendCandidate();
                    var old=RemovalRegion(historicalOracle,member);var current=RemovalRegion(this,member);
                    if(current==null||old.Candidates.Count!=current.Candidates.Count||!old.Requested.Equals(current.Requested)||!RemovalOldRecipe(member,old)){DropHistorical();return false;}s.RemovalStage=2;
                }
                var previous=RemovalRegion(historicalOracle,member);var now=RemovalRegion(this,member);
                while(s.RegionCursor<now.Candidates.Count){
                    if(allocationRemaining==0)return false;SpendCandidate();int n=s.RegionCursor;
                    if(!now.Candidates[n].Equals(previous.Candidates[n])){DropHistorical();return false;}s.RegionCursor++;
                }
                s.RegionCursor=0;s.RemovalStage=0;s.PreparationMember++;
            }
            return true;
        }
        private bool PreparePage0OccupancyCertificate()
        {
            if(!PreparePage0OccupancyRegions())return false;
            var s=historicalState;if(s.OccupancyStage==0)s.OccupancyStage=1;
            while(s.OccupancyStage!=4){
                var member=members[s.OccupancyMember];var region=RemovalRegion(this,s.OccupancyMember);var endpoint=region.Candidates[s.OccupancyCandidate];
                if(s.OccupancyStage==1){
                    if(s.OccupancyOldRow<historicalOracle.input.Occupancy.Occupants.Count){
                        if(allocationRemaining==0)return false;SpendOccupancy();var row=historicalOracle.input.Occupancy.Occupants[s.OccupancyOldRow++];s.OccupancyOldBlocked|=HistoricalOccupantBlocks(member,endpoint,row);continue;
                    }s.OccupancyStage=2;
                }
                if(s.OccupancyStage==2){
                    if(s.OccupancyCurrentRow<input.Occupancy.Occupants.Count){
                        if(allocationRemaining==0)return false;SpendOccupancy();var row=input.Occupancy.Occupants[s.OccupancyCurrentRow++];s.OccupancyCurrentBlocked|=HistoricalOccupantBlocks(member,endpoint,row);continue;
                    }s.OccupancyStage=3;
                }
                // Even empty row sets require one paid comparison/advance pair.
                if(allocationRemaining==0)return false;SpendCandidate();
                if(s.OccupancyOldBlocked!=s.OccupancyCurrentBlocked){DropHistorical();return false;}
                s.OccupancyOldRow=s.OccupancyCurrentRow=0;s.OccupancyOldBlocked=s.OccupancyCurrentBlocked=false;
                s.OccupancyCandidate++;
                if(s.OccupancyCandidate==region.Candidates.Count){s.OccupancyCandidate=0;s.OccupancyMember++;}
                s.OccupancyStage=s.OccupancyMember==members.Length?4:1;
            }
            return true;
        }
        private void ValidatePage0RegionPrefix()
        {
            var s=historicalState;
            if(s.PreparationMember<0||s.PreparationMember>members.Length||s.RemovalStage<0||s.RemovalStage>2||s.RegionCursor<0||s.RegionCursor>64||s.EndpointCursor<0||s.EndpointCursor>64)throw new ArgumentException("Page0 preparation cursor dimensions.");
            if(historicalGate<4&&(s.PreparationMember!=0||s.RemovalStage!=0||s.RegionCursor!=0)||s.PreparationMember==members.Length&&(s.RemovalStage!=0||s.RegionCursor!=0))throw new ArgumentException("Page0 preparation phase mismatch.");
            for(int m=0;m<s.PreparationMember+(s.RemovalStage==2?1:0);m++){
                var old=RemovalRegion(historicalOracle,m);var current=RemovalRegion(this,m);int checkedCount=m<s.PreparationMember?current?.Candidates.Count??-1:s.RegionCursor;
                if(old==null||current==null||old.Candidates.Count!=current.Candidates.Count||!old.Requested.Equals(current.Requested)||!RemovalOldRecipe(m,old)||checkedCount<0||checkedCount>current.Candidates.Count)throw new ArgumentException("Certified page0 region/recipe changed.");
                for(int n=0;n<checkedCount;n++)if(!current.Candidates[n].Equals(old.Candidates[n]))throw new ArgumentException("Certified page0 candidate changed.");
            }
            if(s.EndpointCursor>0||s.EndpointFound){
                if(s.PreparationMember!=members.Length||historicalOracle.Status!=NavArrivalStatus.Ready)throw new ArgumentException("Page0 endpoint lacks ready source.");
                var region=RemovalRegion(this,historicalMember);if(s.EndpointCursor>region.Candidates.Count)throw new ArgumentException("Page0 endpoint cursor overflow.");
                bool found=false;for(int n=0;n<s.EndpointCursor;n++)found|=region.Candidates[n].Equals(historicalOracle.assignments[historicalMember].Endpoint);
                if(found!=s.EndpointFound)throw new ArgumentException("Page0 endpoint represented prefix changed.");
            }
        }
        private void ValidatePage0OccupancyCertificate()
        {
            var s=historicalState;int oldCount=historicalOracle.input.Source.Candidates.Count;
            if(!terminalCostMode||!ownedArrivalAnchorMode||!originShadowMode||!terminalCornerMode||!terminalRectangleMode||!rectangleOriginsMode||!certifiedMode||!lazyMode||!astarMode||!exactCostMode||!removalMode||!occupancyPrefixMode||!demandV3Mode)throw new ArgumentException("Page0 wrapper underlying policy mismatch.");
            if(s.SourceOld<0||s.SourceOld>oldCount)throw new ArgumentException("Page0 source cursor overflow.");
            if(RemovalActive){int matched=0;for(int n=0;n<s.SourceOld;n++)if(matched<input.Source.Candidates.Count&&input.Source.Candidates[matched].Equals(historicalOracle.input.Source.Candidates[n]))matched++;
                if(historicalGate<2&&s.SourceOld!=0||historicalGate==2&&historicalCursor!=matched||historicalGate>2&&(s.SourceOld!=oldCount||matched!=input.Source.Candidates.Count))throw new ArgumentException("Page0 source subsequence prefix mismatch.");
            }else if(s.SourceOld!=0)throw new ArgumentException("Equal source has removal cursor.");
            ValidatePage0RegionPrefix();
            if(s.OccupancyStage<0||s.OccupancyStage>4||s.OccupancyMember<0||s.OccupancyMember>members.Length||s.OccupancyCandidate<0||s.OccupancyOldRow<0||s.OccupancyCurrentRow<0)throw new ArgumentException("Occupancy certificate dimensions.");
            if(s.OccupancyStage==0){if(s.OccupancyMember!=0||s.OccupancyCandidate!=0||s.OccupancyOldRow!=0||s.OccupancyCurrentRow!=0||s.OccupancyOldBlocked||s.OccupancyCurrentBlocked||s.PreparationMember==members.Length)throw new ArgumentException("Unstarted occupancy prefix mismatch.");return;}
            if(historicalGate!=4||s.PreparationMember!=members.Length)throw new ArgumentException("Occupancy prefix precedes complete preparation.");
            if(s.OccupancyStage==4){if(s.OccupancyMember!=members.Length||s.OccupancyCandidate!=0||s.OccupancyOldRow!=0||s.OccupancyCurrentRow!=0||s.OccupancyOldBlocked||s.OccupancyCurrentBlocked)throw new ArgumentException("Complete occupancy cursor mismatch.");}
            else if(s.OccupancyMember==members.Length)throw new ArgumentException("Incomplete occupancy member overflow.");
            // Validate only already paid rows/pairs. No remaining row is evaluated on restore.
            for(int m=0;m<members.Length;m++){
                if(m>s.OccupancyMember)break;var region=RemovalRegion(this,m);int completed=m<s.OccupancyMember?region.Candidates.Count:s.OccupancyCandidate;
                if(completed>region.Candidates.Count||m==s.OccupancyMember&&s.OccupancyStage!=4&&completed==region.Candidates.Count)throw new ArgumentException("Occupancy candidate cursor overflow.");
                for(int c=0;c<completed;c++)if(Page0BlockedPrefix(m,region.Candidates[c],true,historicalOracle.input.Occupancy.Occupants.Count)!=Page0BlockedPrefix(m,region.Candidates[c],false,input.Occupancy.Occupants.Count))throw new ArgumentException("Completed occupancy aggregate changed.");
                if(m!=s.OccupancyMember||s.OccupancyStage==4)continue;
                int oldRows=historicalOracle.input.Occupancy.Occupants.Count,currentRows=input.Occupancy.Occupants.Count;
                if(s.OccupancyOldRow>oldRows||s.OccupancyCurrentRow>currentRows||s.OccupancyStage==1&&(s.OccupancyCurrentRow!=0||s.OccupancyCurrentBlocked)||s.OccupancyStage>=2&&s.OccupancyOldRow!=oldRows||s.OccupancyStage==3&&s.OccupancyCurrentRow!=currentRows)throw new ArgumentException("Occupancy row/stage prefix mismatch.");
                if(s.OccupancyOldBlocked!=Page0BlockedPrefix(m,region.Candidates[completed],true,s.OccupancyOldRow)||s.OccupancyCurrentBlocked!=Page0BlockedPrefix(m,region.Candidates[completed],false,s.OccupancyCurrentRow))throw new ArgumentException("Occupancy partial OR prefix changed.");
            }
        }
        private bool Page0BlockedPrefix(int member,NavLocation endpoint,bool old,int count)
        {bool blocked=false;var rows=old?historicalOracle.input.Occupancy.Occupants:input.Occupancy.Occupants;for(int n=0;n<count;n++)blocked|=HistoricalOccupantBlocks(members[member],endpoint,rows[n]);return blocked;}
    }
}
