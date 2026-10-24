using System;
using System.IO;

namespace Spacewars.Simulation
{
    // Cursor strings borrow their pinned recipe provider's binding. No detached
    // obstacle list, geometry replay, or unaccounted identity allocation.
    internal sealed class NavOwnedExclusionState : NavOwnedStorage, INavPhysicalBody
    {
        internal readonly NavScheduledRequest Recipe;
        internal NavExclusionCursor Cursor;
        private bool frozen;
        internal NavExclusionInput Input
        {
            get {
                var p=(PartitionNavigationProvider)Recipe.Provider;
                double b=p.HalfExtent/Recipe.Profile.GridCell;
                long lo=(long)Math.Ceiling(-b),hi=(long)Math.Floor(b),width=hi-lo+1;
                return new NavExclusionInput(p.ExclusionObstacles,p.Binding,p.Binding,Recipe.Profile.GridCell,
                    lo,hi,lo,hi,checked(width*width),Recipe.Limits.MaxSamples,Recipe.Limits.MaxNodes,Recipe.Limits.MaxArcs,Recipe.MaxLabels);
            }
        }
        internal NavExclusionProgress Progress=>Cursor.Stage==NavExclusionStage.Complete?Cursor.TerminalProgress:NavExclusionProgress.Pending;
        internal NavOwnedExclusionState(NavScheduledRequest recipe,Func<long,bool> reserve=null,bool initialize=true,bool birthOnly=false):base(1024)
        {
            if(!recipe.CertifiedLazyPartition)throw new ArgumentException("Certificate requires its own policy.");
            Recipe=recipe;
            using(var a=new NavStorageAllocation(reserve)){a.Reserve(1024);a.Register(this);if(!birthOnly)Retain();}
            if(IsRegistered)RequireEdge(recipe.StorageRoot);recipe.StorageRoot?.Retain();
            if(initialize)Cursor=NavigationRectangularExclusionCertificate.Start(Input);
        }
        internal void Step()
        {
            if(frozen||Progress!=NavExclusionProgress.Pending)throw new InvalidOperationException("Unpaid or immutable capacity work.");
            NavigationRectangularExclusionCertificate.Step(Input,ref Cursor,1,out _,out _);
        }
        internal bool Excludes(int id)
        {
            if(Progress!=NavExclusionProgress.Certified)return false;
            var input=Input;long width=input.MaxZIndex-input.MinZIndex+1;
            long x=input.MinXIndex+id/width,z=input.MinZIndex+id%width;
            var c=Cursor.TerminalCertificate;
            return x>=c.MinXIndex&&x<=c.MaxXIndex&&z>=c.MinZIndex&&z<=c.MaxZIndex;
        }
        internal int SkipRow(int x,int z,int rowMax)
        {
            if(Progress!=NavExclusionProgress.Certified)return z;
            var input=Input;var c=Cursor.TerminalCertificate;
            long gx=input.MinXIndex+x,gz=input.MinZIndex+z;
            return gx>=c.MinXIndex&&gx<=c.MaxXIndex&&gz>=c.MinZIndex&&gz<=c.MaxZIndex
                ?checked((int)Math.Min(rowMax+1L,c.MaxZIndex-input.MinZIndex+1)):z;
        }
        internal void Validate()
        {
            if(!NavigationRectangularExclusionCertificate.Validate(Input,Cursor,Cursor.TerminalCertificate))throw new ArgumentException("Invalid owned capacity proof.");
        }
        internal void RestoreCursor(NavExclusionCursor cursor)
        {
            cursor.ProviderIdentity=cursor.GeometryIdentity=Recipe.Provider.Binding;
            if(cursor.TerminalProgress==NavExclusionProgress.Certified){var c=cursor.TerminalCertificate;c.ProviderIdentity=c.GeometryIdentity=Recipe.Provider.Binding;cursor.TerminalCertificate=c;}Cursor=cursor;Validate();
        }
        internal void Seal(){frozen=true;}
        internal NavOwnedExclusionState Fork(Func<long,bool> reserve)
        {
            if(!frozen)throw new InvalidOperationException("Seal capacity state before fork.");
            return new NavOwnedExclusionState(Recipe,reserve,false){Cursor=Cursor};
        }
        protected override void ReleaseEdges(){Recipe.StorageRoot?.Release();}
        internal override bool IsImmutableCaptureRoot=>IsRegistered&&frozen;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.ExclusionStateV1);
        // Provider identities and fixed domain/caps derive from the retained
        // recipe. Every mutable scalar is explicit; decoding never runs Step.
        internal void Write(BinaryWriter w)=>WriteCursor(w,Cursor);
        internal static void WriteCursor(BinaryWriter w,NavExclusionCursor c)
        {
            w.Write(c.ObstacleIndex);w.Write((int)c.Stage);w.Write(c.Low);w.Write(c.High);
            w.Write(c.CandidateMinX);w.Write(c.CandidateMaxX);w.Write(c.CandidateMinZ);w.Write(c.CandidateMaxZ);
            w.Write(c.XMin);w.Write(c.XMax);w.Write(c.ZMin);w.Write(c.ZMax);w.Write(c.PaidOperations);w.Write((int)c.TerminalProgress);
            var p=c.TerminalCertificate;w.Write(p.ObstacleIndex);w.Write(p.GridCell);w.Write(p.MinX);w.Write(p.MaxX);w.Write(p.MinZ);w.Write(p.MaxZ);
            w.Write(p.MinXIndex);w.Write(p.MaxXIndex);w.Write(p.MinZIndex);w.Write(p.MaxZIndex);w.Write(p.Samples);w.Write(p.ExcludedCount);w.Write(p.RemainingCount);
            w.Write(p.MaxSamples);w.Write(p.MaxNodes);w.Write(p.MaxArcs);w.Write(p.MaxLabels);
        }
        internal void Read(BinaryReader r){Cursor=ReadCursor(r,Input);Validate();}
        internal static NavExclusionCursor ReadCursor(BinaryReader r,NavExclusionInput input)
        {
            var c=NavigationRectangularExclusionCertificate.Start(input);
            c.ObstacleIndex=r.ReadInt32();c.Stage=(NavExclusionStage)r.ReadInt32();c.Low=r.ReadInt64();c.High=r.ReadInt64();
            c.CandidateMinX=r.ReadDouble();c.CandidateMaxX=r.ReadDouble();c.CandidateMinZ=r.ReadDouble();c.CandidateMaxZ=r.ReadDouble();
            c.XMin=r.ReadInt64();c.XMax=r.ReadInt64();c.ZMin=r.ReadInt64();c.ZMax=r.ReadInt64();c.PaidOperations=r.ReadInt64();c.TerminalProgress=(NavExclusionProgress)r.ReadInt32();
            var p=new NavExclusionCertificate{ObstacleIndex=r.ReadInt32(),GridCell=r.ReadDouble(),MinX=r.ReadDouble(),MaxX=r.ReadDouble(),MinZ=r.ReadDouble(),MaxZ=r.ReadDouble(),
                MinXIndex=r.ReadInt64(),MaxXIndex=r.ReadInt64(),MinZIndex=r.ReadInt64(),MaxZIndex=r.ReadInt64(),Samples=r.ReadInt64(),ExcludedCount=r.ReadInt64(),RemainingCount=r.ReadInt64(),
                MaxSamples=r.ReadInt64(),MaxNodes=r.ReadInt64(),MaxArcs=r.ReadInt64(),MaxLabels=r.ReadInt64()};
            if(c.TerminalProgress==NavExclusionProgress.Certified){p.ProviderIdentity=input.ProviderIdentity;p.GeometryIdentity=input.GeometryIdentity;}
            c.TerminalCertificate=p;return c;
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter w)
        {
            if(!IsImmutableCaptureRoot||NavStorageLayout.ObjectBytesBound(GetType())>1024)throw new ArgumentException("Capacity state ownership/layout.");
            NavPhysicalMacro.WriteEdge(w,this,Recipe.StorageRoot);Write(w.Output);
        }
        internal static NavOwnedExclusionState ReadPhysicalBody(NavPhysicalBodyReader r)
        {
            var root=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(r);
            if(!(root.Facade is NavScheduledRequest q)||!q.CertifiedLazyPartition)throw new ArgumentException("Capacity recipe.");
            var n=new NavOwnedExclusionState(q,r.Reservation.TryReserve,false,true);
            try{n.Read(r.Input);n.frozen=true;return n;}catch{n.ReleaseOwner();throw;}
        }
    }
}
