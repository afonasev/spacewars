using System;
using System.IO;
using System.Linq;
namespace Spacewars.Simulation
{
    public sealed partial class NavigationArrivalWork
    {
        // Hash collisions are conservative: every visited row still uses TooClose.
        // One existing occupancy operation pays each bounded initialization,
        // insertion, cell opening or row visit. No source reordering or surface filter.
        private const int SpatialBuckets=256;
        private const double SpatialCell=16, SpatialLimit=1000000;
        private bool spatialPolicy,spatialSafe=true,spatialFallback;
        private NavPagedList<int> spatialHeads,spatialLinks;
        private double spatialRadius;
        private int spatialBuilt,spatialPhase,spatialX,spatialZ,spatialMinZ,spatialMaxX,spatialMaxZ,spatialNext=-1;
        internal sealed class SpatialCheckpoint
        {
            internal int[] Heads,Links,Scalars;
            internal double Radius;
        }
        private static int SpatialBucket(int x,int z)=>unchecked((int)((uint)x*2654435761u^(uint)z*2246822519u))&(SpatialBuckets-1);
        private static bool SpatialPoint(NavPoint p,double r)=>!double.IsNaN(p.X)&&!double.IsNaN(p.Z)&&!double.IsNaN(r)&&Math.Abs(p.X)<=SpatialLimit&&Math.Abs(p.Z)<=SpatialLimit&&r>=0&&r<=SpatialLimit;
        private static int SpatialCoordinate(double p)=>(int)Math.Floor(p/SpatialCell);
        private void InitializeSpatial()
        {
            spatialHeads=NavPagedList<int>.Create(rootReservation,32);
            spatialLinks=NavPagedList<int>.Create(rootReservation,32);
        }
        private void SpatialRow(int row,out NavPoint point,out double radius,out bool blocks)
        {
            if(row<input.Occupancy.Occupants.Count){var o=input.Occupancy.Occupants[row];point=o.Location.Position;radius=o.Radius;blocks=o.BlocksPlacement;}
            else{var a=assignments[row-input.Occupancy.Occupants.Count];point=a.Endpoint.Position;radius=a.Member.Radius;blocks=true;}
        }
        private bool BuildSpatial()
        {
            if(spatialHeads==null)InitializeSpatial();
            while(spatialHeads.Count<SpatialBuckets){if(allocationRemaining==0)return false;SpendOccupancy();spatialHeads.Add(-1);}
            int total=input.Occupancy.Occupants.Count+assignments.Count;
            while(spatialBuilt<total){
                if(allocationRemaining==0)return false;SpendOccupancy();
                SpatialRow(spatialBuilt,out var p,out double r,out bool blocks);
                int head=-1,bucket=-1;
                if(blocks){
                    if(!SpatialPoint(p,r))spatialSafe=false;
                    else{spatialRadius=Math.Max(spatialRadius,r);bucket=SpatialBucket(SpatialCoordinate(p.X),SpatialCoordinate(p.Z));head=spatialHeads[bucket];}
                }
                spatialLinks.Add(head);if(bucket>=0)spatialHeads.Set(bucket,spatialBuilt);spatialBuilt++;
            }
            return true;
        }
        private void ResetSpatialQuery(){spatialPhase=0;spatialNext=-1;spatialFallback=false;spatialX=spatialZ=spatialMinZ=spatialMaxX=spatialMaxZ=0;}
        private double SpatialExtent(double memberRadius)=>Math.Max(input.ArrivalProfile.ArrivalSlotSpacing,4*Math.Max(memberRadius,spatialRadius)+2*input.ArrivalProfile.ArrivalTolerance+2*input.ArrivalProfile.LocalGridCell)+1e-6;
        private bool ScanSpatial(NavLocation candidate)
        {
            var member=members[memberIndex];var point=candidate.Position;
            if(spatialPhase==0){
                // This arithmetic is bounded; opening the first cell is paid below.
                // Outward padding avoids floating-point boundary exclusions.
                double extent=SpatialExtent(member.Radius);
                spatialFallback=!spatialSafe||!SpatialPoint(point,member.Radius)||!SpatialPoint(new NavPoint(extent,extent),extent);
                if(!spatialFallback){
                    spatialX=SpatialCoordinate(point.X-extent);spatialMaxX=SpatialCoordinate(point.X+extent);
                    spatialZ=spatialMinZ=SpatialCoordinate(point.Z-extent);spatialMaxZ=SpatialCoordinate(point.Z+extent);
                    spatialFallback=(long)(spatialMaxX-spatialX+1)*(spatialMaxZ-spatialZ+1)>64;
                }
                spatialPhase=1;
            }
            if(spatialFallback){
                int total=input.Occupancy.Occupants.Count+assignments.Count;
                while(occupantCursor<total){
                    if(allocationRemaining==0)return false;SpendOccupancy();SpatialRow(occupantCursor++,out var p,out double r,out bool blocks);
                    if(blocks&&TooClose(point,member.Radius,p,r)){candidateBlocked=true;return true;}
                }
                spatialPhase=3;return true;
            }
            while(spatialPhase!=3){
                if(allocationRemaining==0)return false;SpendOccupancy();
                if(spatialPhase==1){spatialNext=spatialHeads[SpatialBucket(spatialX,spatialZ)];spatialPhase=2;}
                else{
                    int row=spatialNext;SpatialRow(row,out var p,out double r,out bool blocks);spatialNext=spatialLinks[row];
                    if(blocks&&TooClose(point,member.Radius,p,r)){candidateBlocked=true;return true;}
                }
                if(spatialNext<0){
                    if(spatialZ<spatialMaxZ){spatialZ++;spatialPhase=1;}
                    else if(spatialX<spatialMaxX){spatialX++;spatialZ=spatialMinZ;spatialPhase=1;}
                    else spatialPhase=3;
                }
            }
            return true;
        }
        private int[] SpatialScalars()=>new[]{spatialSafe?1:0,spatialFallback?1:0,spatialBuilt,spatialPhase,spatialX,spatialZ,spatialMinZ,spatialMaxX,spatialMaxZ,spatialNext};
        private void SetSpatialScalars(int[] v,double radius)
        {
            if(v==null||v.Length!=10||v[0]<0||v[0]>1||v[1]<0||v[1]>1)throw new ArgumentException("Invalid spatial scalar shape.");
            spatialSafe=v[0]!=0;spatialFallback=v[1]!=0;spatialBuilt=v[2];spatialPhase=v[3];spatialX=v[4];spatialZ=v[5];spatialMinZ=v[6];spatialMaxX=v[7];spatialMaxZ=v[8];spatialNext=v[9];spatialRadius=radius;
        }
        private void WriteSpatialScalars(BinaryWriter writer){writer.Write(spatialSafe);writer.Write(spatialFallback);writer.Write(spatialBuilt);writer.Write(spatialPhase);writer.Write(spatialX);writer.Write(spatialZ);writer.Write(spatialMinZ);writer.Write(spatialMaxX);writer.Write(spatialMaxZ);writer.Write(spatialNext);writer.Write(spatialRadius);}
        private void ReadSpatialScalars(BinaryReader reader)
        {var v=new int[10];for(int i=0;i<10;i++)v[i]=i<2?reader.ReadByte():reader.ReadInt32();SetSpatialScalars(v,reader.ReadDouble());}
        private SpatialCheckpoint SaveSpatial()=>!spatialPolicy?null:new SpatialCheckpoint{Heads=spatialHeads?.ToArray(),Links=spatialLinks?.ToArray(),Scalars=SpatialScalars(),Radius=spatialRadius};
        private void RestoreSpatial(SpatialCheckpoint saved)
        {
            if(!spatialPolicy){if(saved!=null)throw new ArgumentException("Unexpected spatial checkpoint.");return;}
            if(saved==null||saved.Heads==null||saved.Links==null)throw new ArgumentException("Missing spatial checkpoint.");
            ReleaseSpatial();InitializeSpatial();foreach(int v in saved.Heads)spatialHeads.Add(v);foreach(int v in saved.Links)spatialLinks.Add(v);SetSpatialScalars(saved.Scalars,saved.Radius);ValidateSpatial();
        }
        private void ForkSpatial(NavigationArrivalWork fork,INavStorageReservation reservation)
        {
            fork.spatialPolicy=spatialPolicy;if(!spatialPolicy)return;
            fork.spatialHeads=spatialHeads?.ForkOwned(reservation);fork.spatialLinks=spatialLinks?.ForkOwned(reservation);
            fork.SetSpatialScalars(SpatialScalars(),spatialRadius);
        }
        private void ResetSpatialStorage(){if(spatialHeads==null)return;spatialHeads.Clear();spatialLinks.Clear();spatialBuilt=0;spatialRadius=0;spatialSafe=true;ResetSpatialQuery();}
        private void ReleaseSpatial(){spatialHeads?.Release();spatialHeads=null;spatialLinks?.Release();spatialLinks=null;}
        private void ValidateSpatial()
        {
            if(!spatialPolicy){if(spatialHeads!=null||spatialLinks!=null)throw new ArgumentException("Unversioned spatial storage.");return;}
            if(derivativesDiscarded){if(spatialHeads!=null||spatialLinks!=null)throw new ArgumentException("Cancelled spatial storage retained.");return;}
            if(spatialHeads==null||spatialLinks==null||spatialHeads.Count>SpatialBuckets||spatialLinks.Count!=spatialBuilt||spatialBuilt<0||spatialBuilt>input.Occupancy.Occupants.Count+assignments.Count||spatialPhase<0||spatialPhase>3||spatialNext< -1||spatialNext>=spatialBuilt||double.IsNaN(spatialRadius)||spatialRadius<0||spatialRadius>SpatialLimit||spatialHeads.Count<SpatialBuckets&&spatialBuilt!=0)throw new ArgumentException("Invalid spatial cursor/backing.");
            // Decode validation walks each row once, never rebuilds state. A chain
            // links only earlier rows in the same hash bucket; heads are latest.
            double radius=0;bool safe=true;int eligible=0,visited=0;
            for(int row=0;row<spatialBuilt;row++){
                int previous=spatialLinks[row];if(previous< -1||previous>=row)throw new ArgumentException("Spatial chain order changed.");
                SpatialRow(row,out var p,out double r,out bool blocks);
                if(!blocks||!SpatialPoint(p,r)){if(blocks)safe=false;if(previous!=-1)throw new ArgumentException("Unindexed row has link.");continue;}
                eligible++;radius=Math.Max(radius,r);int bucket=SpatialBucket(SpatialCoordinate(p.X),SpatialCoordinate(p.Z));
                if(spatialHeads[bucket]<row)throw new ArgumentException("Spatial bucket omits row.");
                if(previous>=0){SpatialRow(previous,out var q,out double qr,out bool qb);if(!qb||!SpatialPoint(q,qr)||SpatialBucket(SpatialCoordinate(q.X),SpatialCoordinate(q.Z))!=bucket)throw new ArgumentException("Spatial link crossed bucket.");}
            }
            for(int bucket=0;bucket<spatialHeads.Count;bucket++){
                int row=spatialHeads[bucket];if(row< -1||row>=spatialBuilt)throw new ArgumentException("Spatial head outside built prefix.");
                if(row>=0){SpatialRow(row,out var p,out double r,out bool blocks);if(!blocks||!SpatialPoint(p,r)||SpatialBucket(SpatialCoordinate(p.X),SpatialCoordinate(p.Z))!=bucket)throw new ArgumentException("Spatial head bucket changed.");}
            }
            for(int bucket=0;bucket<spatialHeads.Count;bucket++)for(int row=spatialHeads[bucket];row>=0;row=spatialLinks[row]){if(++visited>spatialBuilt)throw new ArgumentException("Spatial duplicate chain.");}
            if(visited!=eligible)throw new ArgumentException("Spatial chain omitted row.");
            if(candidateValidated&&phase==Phase.ScanCandidates){
                var member=members[memberIndex];var region=preparedClearances[WorkingKey(member)].CandidateIndices[pageIndex];
                if(candidateCursor>=region.Count||spatialPhase==0)throw new ArgumentException("Spatial query lacks candidate.");
                var p=input.Source.Candidates[region[candidateCursor]].Position;double extent=SpatialExtent(member.Radius);
                bool fallback=!spatialSafe||!SpatialPoint(p,member.Radius)||!SpatialPoint(new NavPoint(extent,extent),extent);
                if(!fallback){int minX=SpatialCoordinate(p.X-extent),maxX=SpatialCoordinate(p.X+extent),minZ=SpatialCoordinate(p.Z-extent),maxZ=SpatialCoordinate(p.Z+extent);
                    fallback=(long)(maxX-minX+1)*(maxZ-minZ+1)>64;
                    if(!fallback&&(spatialX<minX||spatialX>maxX||spatialMinZ!=minZ||spatialMaxZ!=maxZ||spatialMaxX!=maxX||spatialZ<minZ||spatialZ>maxZ||spatialPhase==2&&spatialNext<0||spatialPhase==3&&(spatialX!=maxX||spatialZ!=maxZ||spatialNext!=-1)))throw new ArgumentException("Spatial query bounds/cursor changed.");
                    if(!fallback&&spatialNext>=0){SpatialRow(spatialNext,out var nextPoint,out double nr,out bool nb);if(!nb||!SpatialPoint(nextPoint,nr)||SpatialBucket(SpatialCoordinate(nextPoint.X),SpatialCoordinate(nextPoint.Z))!=SpatialBucket(spatialX,spatialZ))throw new ArgumentException("Spatial query link is outside current bucket.");}
                }
                if(fallback!=spatialFallback||spatialBuilt!=input.Occupancy.Occupants.Count+assignments.Count)throw new ArgumentException("Spatial query safety/build changed.");
            }
            if(radius!=spatialRadius||safe!=spatialSafe)throw new ArgumentException("Spatial radius/safety certificate changed.");
        }
    }
}
