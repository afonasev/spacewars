using System;
using System.IO;
using System.Linq;

namespace Spacewars.Simulation
{
    // Only Origins uses this per-query index. Additional rectangles never
    // contribute to the capacity proof or to uncharged A* neighbor lookups.
    internal sealed class NavOriginRectangleWork : NavOwnedStorage,INavPhysicalBody
    {
        internal readonly NavScheduledRequest Recipe;
        private NavPagedList<long> ranges; // descriptor,xMin,xMax,zMin,zMax
        private NavCowDictionary<int,int> intervals; // relative Z start -> proof
        private NavRectangleCursor cursor;
        private int phase,descriptor,row=-1,rowMin,rowMax,scan,rowPhase;
        private bool frozen;
        private NavExclusionInput Input
        {
            get {var p=(PartitionNavigationProvider)Recipe.Provider;double b=p.HalfExtent/Recipe.Profile.GridCell;long lo=(long)Math.Ceiling(-b),hi=(long)Math.Floor(b),w=hi-lo+1;
                return new NavExclusionInput(p.ExclusionObstacles,p.Binding,p.Binding,Recipe.Profile.GridCell,lo,hi,lo,hi,w*w,Recipe.Limits.MaxSamples,Recipe.Limits.MaxNodes,Recipe.Limits.MaxArcs,Recipe.MaxLabels);}
        }
        internal bool Prepared=>phase==3;
        internal long NextGrowthBytes=>ranges.AppendGrowthBytes(8)+intervals.MutationGrowthBytes(4);
        internal long EstimatedRetainedBytes=>StorageBytes+ranges.CommittedStorageBytes(8)+intervals.CommittedStorageBytes(4);
        internal NavOriginRectangleWork(NavScheduledRequest recipe,Func<long,bool> reserve=null,bool initialize=true,bool birthOnly=false):base(1024)
        {
            Recipe=recipe;
            using(var a=new NavStorageAllocation(reserve)){a.Reserve(1024);a.Register(this);if(!birthOnly)Retain();}
            if(IsRegistered)RequireEdge(recipe.StorageRoot);recipe.StorageRoot?.Retain();
            if(initialize)try{ranges=NavPagedList<long>.Create(reserve);intervals=NavCowDictionary<int,int>.Create(insertionOrder:false,reserve:reserve);}catch{ReleaseOwner();throw;}
        }
        internal void StepPreparation()
        {
            if(frozen||Prepared)throw new InvalidOperationException("Unpaid rectangle preparation.");
            if(phase==0){if(descriptor==Input.Obstacles.Count){phase=3;return;}cursor=NavigationRectangleRangeProof.StartRectangle(Input,descriptor);phase=1;return;}
            if(phase==1){if(NavigationRectangleRangeProof.StepRectangle(Input,ref cursor,1,out _,out _)!=NavRectangleProgress.Pending)phase=2;return;}
            if(phase!=2)throw new InvalidOperationException("Rectangle phase.");
            if(NavigationRectangleRangeProof.Progress(cursor)==NavRectangleProgress.Ready){var c=cursor.Work.TerminalCertificate;if(c.ExcludedCount>0){ranges.Add(descriptor);ranges.Add(c.MinXIndex);ranges.Add(c.MaxXIndex);ranges.Add(c.MinZIndex);ranges.Add(c.MaxZIndex);}}
            descriptor++;phase=0;cursor=default;
        }
        private bool First(out System.Collections.Generic.KeyValuePair<int,int> interval)=>intervals.TrySuccessor(-1,out interval);
        internal bool NeedsRowStep(int x,int z)=>!Prepared||row!=x||rowPhase!=2||z>rowMax||First(out var first)&&first.Key<=z;
        internal void StepRow(int x,int z,int minZ,int maxZ,out int nextZ,out bool rowDone)
        {
            nextZ=z;rowDone=false;
            if(frozen||!Prepared)throw new InvalidOperationException("Unpaid row preparation.");
            if(row!=x){if(row!=-1||intervals.Count!=0)throw new InvalidOperationException("Undrained Origins row.");row=x;rowMin=minZ;rowMax=maxZ;scan=0;rowPhase=1;return;}
            if(rowMin!=minZ||rowMax!=maxZ)throw new ArgumentException("Origins support bounds changed.");
            if(rowPhase==1){
                if(scan==ranges.Count/5){rowPhase=2;return;}
                int proof=scan++;long offset=Input.MinXIndex;int i=proof*5;
                if(x+offset<ranges[i+1]||x+offset>ranges[i+2])return;
                int lo=checked((int)Math.Max(minZ,ranges[i+3]-offset)),hi=checked((int)Math.Min(maxZ,ranges[i+4]-offset));
                if(lo>hi)return;
                if(intervals.TryGetValue(lo,out int previous)){long end=ranges[previous*5+4];if(end<ranges[i+4]||end==ranges[i+4]&&ranges[previous*5]>ranges[i])intervals[lo]=proof;}
                else intervals.Add(lo,proof);
                return;
            }
            if(rowPhase!=2)throw new InvalidOperationException("Invalid row phase.");
            if(z>maxZ){if(First(out var drain)){intervals.Remove(drain.Key);return;}row=-1;rowPhase=0;scan=0;rowMin=rowMax=0;rowDone=true;return;}
            if(!First(out var first)||first.Key>z)throw new InvalidOperationException("No paid interval transition.");
            nextZ=Math.Max(z,checked((int)Math.Min(maxZ+1L,ranges[first.Value*5+4]-Input.MinZIndex+1)));intervals.Remove(first.Key);
        }
        internal void Seal(){if(frozen)return;ranges.Seal();intervals.Seal();frozen=true;}
        internal NavOriginRectangleWork Fork(Func<long,bool> reserve)
        {
            if(!frozen)throw new InvalidOperationException("Seal rectangle work before fork.");
            var n=new NavOriginRectangleWork(Recipe,reserve,false);
            try{n.ranges=ranges.Fork(reserve);n.intervals=intervals.ForkPrepared(reserve);n.cursor=cursor;n.phase=phase;n.descriptor=descriptor;n.row=row;n.rowMin=rowMin;n.rowMax=rowMax;n.scan=scan;n.rowPhase=rowPhase;return n;}catch{n.ReleaseOwner();throw;}
        }
        protected override void ReleaseEdges(){ranges?.Release();intervals?.Release();Recipe.StorageRoot?.Release();}
        internal override bool IsImmutableCaptureRoot=>IsRegistered&&frozen;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.ExclusionStateV1,value:NavPhysicalValueKind.OriginRectangleWorkV1);
        private void WriteScalars(BinaryWriter w)
        {w.Write(phase);w.Write(descriptor);w.Write(row);w.Write(rowMin);w.Write(rowMax);w.Write(scan);w.Write(rowPhase);if(phase==1||phase==2){w.Write(cursor.Obstacle);NavOwnedExclusionState.WriteCursor(w,cursor.Work);}}
        private void ReadScalars(BinaryReader r)
        {phase=r.ReadInt32();descriptor=r.ReadInt32();row=r.ReadInt32();rowMin=r.ReadInt32();rowMax=r.ReadInt32();scan=r.ReadInt32();rowPhase=r.ReadInt32();if(phase==1||phase==2){cursor.Obstacle=r.ReadInt32();cursor.Work=NavOwnedExclusionState.ReadCursor(r,NavigationRectangleRangeProof.Input(Input));}}
        internal void WriteTranscript(BinaryWriter w)
        {WriteScalars(w);w.Write(ranges.Count);foreach(long v in ranges)w.Write(v);w.Write(intervals.Count);foreach(var v in intervals){w.Write(v.Key);w.Write(v.Value);}}
        public void WritePhysicalBody(NavPhysicalBodyWriter w)
        {if(!IsImmutableCaptureRoot||NavStorageLayout.ObjectBytesBound(GetType())>1024)throw new ArgumentException("Origins rectangle ownership/layout.");NavPhysicalMacro.WriteEdge(w,this,Recipe.StorageRoot);WriteScalars(w.Output);NavPhysicalMacro.WriteEdge(w,this,ranges);NavPhysicalMacro.WriteEdge(w,this,intervals);}
        internal static NavOriginRectangleWork ReadPhysicalBody(NavPhysicalBodyReader r)
        {
            var root=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(r);if(!(root.Facade is NavScheduledRequest q))throw new ArgumentException("Origins rectangle recipe.");
            var n=new NavOriginRectangleWork(q,r.Reservation.TryReserve,false,true);
            try{n.ReadScalars(r.Input);n.ranges=NavPhysicalMacro.Acquire(n,NavPhysicalMacro.ReadEdge<NavPagedList<long>>(r));n.intervals=NavPhysicalMacro.Acquire(n,NavPhysicalMacro.ReadEdge<NavCowDictionary<int,int>>(r));n.Validate();n.frozen=true;return n;}catch{n.ReleaseOwner();throw;}
        }
        internal void Validate()
        {
            var input=NavigationRectangleRangeProof.Input(Input);int width=checked((int)(input.MaxXIndex-input.MinXIndex+1));
            if(phase<0||phase>3||descriptor<0||descriptor>input.Obstacles.Count||phase!=0&&phase!=3&&descriptor==input.Obstacles.Count||phase==3&&descriptor!=input.Obstacles.Count||ranges.Count%5!=0||ranges.Count/5>descriptor||row< -1||row>=width||rowPhase<0||rowPhase>2||scan<0||scan>ranges.Count/5||row==-1&&(rowPhase!=0||intervals.Count!=0||scan!=0)||row>=0&&(!Prepared||rowPhase==0||rowMin<0||rowMax>=width||rowMin>rowMax)||rowPhase==2&&scan!=ranges.Count/5)throw new ArgumentException("Origins rectangle cursor dimensions.");
            if(phase==1||phase==2){if(cursor.Obstacle!=descriptor||!NavigationRectangleRangeProof.ValidateRectangle(Input,cursor)||(phase==1)!=(NavigationRectangleRangeProof.Progress(cursor)==NavRectangleProgress.Pending))throw new ArgumentException("Origins rectangle paid prefix.");}
            long prior=-1;
            for(int i=0;i<ranges.Count;i+=5){long d=ranges[i];if(d<=prior||d>=descriptor)throw new ArgumentException("Origins proof descriptor prefix.");prior=d;
                var o=input.Obstacles[(int)d];long x0=ranges[i+1],x1=ranges[i+2],z0=ranges[i+3],z1=ranges[i+4];
                var c=NavigationRectangularExclusionCertificate.Start(input);c.ObstacleIndex=(int)d;c.Stage=NavExclusionStage.Complete;c.TerminalProgress=NavExclusionProgress.Certified;c.XMin=x0;c.XMax=x1;c.ZMin=z0;c.ZMax=z1;
                c.TerminalCertificate=new NavExclusionCertificate{ProviderIdentity=input.ProviderIdentity,GeometryIdentity=input.GeometryIdentity,GridCell=input.GridCell,MinX=o.MinX,MaxX=o.MaxX,MinZ=o.MinZ,MaxZ=o.MaxZ,MinXIndex=x0,MaxXIndex=x1,MinZIndex=z0,MaxZIndex=z1,Samples=input.Samples,ExcludedCount=checked((x1-x0+1)*(z1-z0+1)),MaxSamples=input.MaxSamples,MaxNodes=input.MaxNodes,MaxArcs=input.MaxArcs,MaxLabels=input.MaxLabels,ObstacleIndex=(int)d};var certificate=c.TerminalCertificate;certificate.RemainingCount=input.Samples-certificate.ExcludedCount;c.TerminalCertificate=certificate;
                if(x0>x1||z0>z1||!NavigationRectangularExclusionCertificate.Validate(input,c,certificate))throw new ArgumentException("Origins rectangle proof bounds.");
            }
            foreach(var pair in intervals){int i=pair.Value*5;if(pair.Value<0||pair.Value>=scan||pair.Key<rowMin||pair.Key>rowMax||row+input.MinXIndex<ranges[i+1]||row+input.MinXIndex>ranges[i+2]||pair.Key!=Math.Max(rowMin,ranges[i+3]-input.MinZIndex)||ranges[i+4]-input.MinZIndex<pair.Key)throw new ArgumentException("Origins row interval proof.");}
            if(rowPhase==1)ValidateCoverage(rowMin);
        }
        private void ValidateCoverage(int z)
        {
            long offset=Input.MinXIndex;
            for(int proof=0;proof<scan;proof++){
                int i=proof*5;if(row+offset<ranges[i+1]||row+offset>ranges[i+2])continue;
                int lo=checked((int)Math.Max(rowMin,ranges[i+3]-offset)),hi=checked((int)Math.Min(rowMax,ranges[i+4]-offset));
                if(lo>hi||hi<z)continue;
                if(!intervals.TryGetValue(lo,out int selected)||ranges[selected*5+4]<ranges[i+4]||ranges[selected*5+4]==ranges[i+4]&&ranges[selected*5]>ranges[i])throw new ArgumentException("Missing or noncanonical paid row interval.");
            }
        }
        internal void ValidateQueryCursor(int stage,int x,int z,int minZ,int maxZ)
        {
            Validate();
            if(stage!=4){if(row!=-1)throw new ArgumentException("Origins row outside traversal.");return;}
            if(row==-1){if(z!=minZ)throw new ArgumentException("Origins row has no paid beginning.");return;}
            if(row!=x||rowMin!=minZ||rowMax!=maxZ||z<minZ||z>maxZ+1)throw new ArgumentException("Origins query/row cursor mismatch.");
            ValidateCoverage(z);
        }
        internal sealed class Checkpoint {internal byte[] Scalars;internal long[] Ranges;internal System.Collections.Generic.KeyValuePair<int,int>[] Intervals;internal NavTreeShape<int> Shape;internal int Directory;}
        internal Checkpoint Save()
        {using(var stream=new MemoryStream()){using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))WriteScalars(w);return new Checkpoint{Scalars=stream.ToArray(),Ranges=ranges.ToArray(),Intervals=intervals.ToArray(),Shape=intervals.SaveShape(),Directory=ranges.DirectoryCapacity};}}
        internal static NavOriginRectangleWork Restore(NavScheduledRequest recipe,Checkpoint s)
        {
            var n=new NavOriginRectangleWork(recipe);
            try{using(var stream=new MemoryStream(s.Scalars,false))using(var r=new BinaryReader(stream)){n.ReadScalars(r);if(stream.Position!=stream.Length)throw new ArgumentException("Trailing Origins rectangle state.");}n.ranges.AddRange(s.Ranges);n.ranges.RestoreDirectoryCapacity(s.Directory,5*2048,n.ranges.Count);n.intervals.RestoreShape(s.Shape,0,2048);foreach(var pair in s.Intervals){if(!n.intervals.ContainsKey(pair.Key))throw new ArgumentException("Origins row tree shape.");n.intervals[pair.Key]=pair.Value;}if(n.intervals.Count!=s.Intervals.Length)throw new ArgumentException("Origins row tree values.");n.Validate();return n;}catch{n.ReleaseOwner();throw;}
        }
    }
}
