using System;

namespace Spacewars.Simulation
{
    // Paid actor headers and derived working index. Immutable route arrays are
    // borrowed through explicit pins, never copied with the crowd frame.
    internal sealed class NavPreparedCrowdStep : NavOwnedStorage
    {
        private static readonly long HeaderBytes=NavStorageLayout.ObjectBytesBound(typeof(NavPreparedCrowdStep)),UnitBytes=NavStorageLayout.ObjectBytesBound(typeof(NavUnit)),CrowdBytes=NavStorageLayout.ObjectBytesBound(typeof(NavCrowd));
        private static long Align(long n)=>checked((n+7)/8*8);
        private static long Layout(int count)=>checked(HeaderBytes+16*32L+104L*count+Align(4L*count)+2*Align(count)+2L*count*UnitBytes+CrowdBytes+NavMotionWorkspace.ArrayBytes(count));
        internal readonly NavUnit[] Source,Before,After;
        internal readonly NavLiveUnitStorage[] ActorPins;
        internal readonly NavRetainedArray<NavPoint>[] Globals,Locals,NewLocals,RetiredGlobals,RetiredLocals;
        internal readonly NavOwnedStorage[] LocationPins,GoalPins,FailurePins;
        internal readonly int[] InitialRows;internal readonly bool[] TerminalSeeds;
        internal readonly double[] Preferred;
        // Final destination disposition, not an event history. At commit a
        // cleared route also makes any preserved installed owner historical.
        internal readonly bool[] Cleared;
        internal readonly NavMotionBody[] Motion;internal readonly NavMotionProposal[] Proposals;internal readonly int[] Neighbors,NeighborCounts,ComponentLabels,YieldLabels,YieldQueue;internal readonly double[] OriginalSpeeds;
        internal readonly NavCrowd SourceCrowd,Working;
        internal INavStorageReservation Reservation;
        internal NavCrowdSpatialStorage SourceSpatial,NextSpatial,RetiredSpatial;
        internal NavOwnedStorage GeometryPin,ProfilePin,NamesPin;
        internal NavGeometry Geometry;internal NavigationProfile Profile;
        internal long NextIncarnation;internal double MaximumRadius;
        internal int SourceNeighbor,SourceRepair,SourceFailed,SourceNoProgress;internal double SourceRepairCpu;
        private bool transferred;
        private NavPreparedCrowdStep(NavCrowd crowd,int count,INavStorageReservation reservation):base(Layout(count))
        {
            SourceCrowd=crowd;Reservation=reservation;Source=new NavUnit[count];Before=new NavUnit[count];After=new NavUnit[count];ActorPins=new NavLiveUnitStorage[count];
            Globals=new NavRetainedArray<NavPoint>[count];Locals=new NavRetainedArray<NavPoint>[count];NewLocals=new NavRetainedArray<NavPoint>[count];RetiredGlobals=new NavRetainedArray<NavPoint>[count];RetiredLocals=new NavRetainedArray<NavPoint>[count];
            LocationPins=new NavOwnedStorage[count];GoalPins=new NavOwnedStorage[count];FailurePins=new NavOwnedStorage[count];InitialRows=new int[count];Preferred=new double[count];Cleared=new bool[count];TerminalSeeds=new bool[count];for(int i=0;i<count;i++)Preferred[i]=double.NaN;
            Motion=new NavMotionBody[count];Proposals=new NavMotionProposal[count];Neighbors=new int[checked(count*NavCrowdMotion.NeighborCapacity)];NeighborCounts=new int[count];ComponentLabels=new int[count];OriginalSpeeds=new double[count];YieldLabels=new int[count];YieldQueue=new int[count];
            Working=new NavCrowd(this,crowd);Retain();
        }
        internal static NavPreparedCrowdStep Create(NavCrowd crowd,int count,INavStorageReservation r)
        {
            crowd.ValidatePreparationLedger(r);
            using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout(count));var result=a.Register(new NavPreparedCrowdStep(crowd,count,r));
                try{result.NextSpatial=count==0?null:NavCrowdSpatialStorage.Create(count,r.TryReserve);return result;}catch{result.Release();throw;}}
        }
        internal int Find(int id){int lo=0,hi=After.Length-1;while(lo<=hi){int mid=(lo+hi)/2,at=After[mid].Id;if(at==id)return mid;if(at<id)lo=mid+1;else hi=mid-1;}for(int i=0;i<After.Length;i++)if(After[i].Id==id)return i;return -1;}
        internal void ReplaceLocal(NavUnit unit,NavLocalRepairWorkspace route)
        {int row=Find(unit.Id);var next=NavRetainedArray<NavPoint>.Copy(route,Reservation.TryReserve);NewLocals[row]?.Release();NewLocals[row]=next;unit.localRoute=next.ReadOnlyBacking;}
        internal void ClearRoutes(NavUnit unit){int row=Find(unit.Id);Cleared[row]=true;NewLocals[row]?.Release();NewLocals[row]=null;}
        // A Ready projected after an earlier stop replaces that disposition.
        // A later movement rejection may clear this new route again.
        internal void ReplacedRoute(NavUnit unit){int row=Find(unit.Id);Cleared[row]=false;NewLocals[row]?.Release();NewLocals[row]=null;}
        internal void Observe()=>SourceCrowd.ObservePreparedStep(this);
        internal bool SourceCurrent()=>SourceCrowd.PreparedStepSourceCurrent(this);
        internal void Compute(double dt)=>Working.Step(dt);
        internal void ApplyNoThrow()
        {
            for(int i=0;i<Source.Length;i++){
                var actor=Source[i];if(Cleared[i])actor.StorageOwner.ClearNoThrow(out RetiredGlobals[i],out RetiredLocals[i]);
                else if(NewLocals[i]!=null){actor.StorageOwner.SetLocalNoThrow(NewLocals[i],out RetiredLocals[i]);NewLocals[i]=null;}
                actor.CopyMovementValuesFrom(After[i]);
            }
            SourceCrowd.ApplyPreparedStepNoThrow(this);transferred=true;
        }
        protected override void ReleaseEdges()
        {
            foreach(var p in LocationPins)p?.Release();foreach(var p in GoalPins)p?.Release();foreach(var p in FailurePins)p?.Release();foreach(var p in ActorPins)p?.Release();foreach(var p in Globals)p?.Release();foreach(var p in Locals)p?.Release();foreach(var p in NewLocals)p?.Release();foreach(var p in RetiredGlobals)p?.Release();foreach(var p in RetiredLocals)p?.Release();
            SourceSpatial?.Release();if(!transferred)NextSpatial?.Release();RetiredSpatial?.Release();GeometryPin?.Release();ProfilePin?.Release();NamesPin?.Release();Reservation=null;
        }
    }
    public sealed partial class NavCrowd
    {
        internal NavCrowd(NavPreparedCrowdStep frame,NavCrowd source)
        {preparedStep=frame;terrain=source.terrain;unitView=frame.After;}
        internal void ValidatePreparationLedger(INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,geometry.StorageRoot);NavStorageAdmission.RequireOwned(reservation,profile.StorageRoot);
            if(ownedSurfaceNames is NavOwnedStorage names)NavStorageAdmission.RequireOwned(reservation,names);
            if(spatial!=null)NavStorageAdmission.RequireOwned(reservation,spatial);
            for(int i=0;i<unitView.Count;i++){var actor=unitView[i];
                if(actor.Location.NameReference.Owner!=null)NavStorageAdmission.RequireOwned(reservation,actor.Location.NameReference.Owner);if(actor.GoalLocation?.NameReference.Owner!=null)NavStorageAdmission.RequireOwned(reservation,actor.GoalLocation.Value.NameReference.Owner);if(actor.FailedGoal?.NameReference.Owner!=null)NavStorageAdmission.RequireOwned(reservation,actor.FailedGoal.Value.NameReference.Owner);
                NavStorageAdmission.RequireOwned(reservation,actor.StorageOwner);var global=actor.StorageOwner.Global;var local=actor.StorageOwner.Local;
                if(global!=null)NavStorageAdmission.RequireOwned(reservation,global);if(local!=null)NavStorageAdmission.RequireOwned(reservation,local);
                if((global?.Count??0)!=actor.route.Length||(local?.Count??0)!=actor.localRoute.Length||global!=null&&!ReferenceEquals(global.ReadOnlyBacking,actor.route)||local!=null&&!ReferenceEquals(local.ReadOnlyBacking,actor.localRoute))throw new InvalidOperationException("Prepared movement route has no exact owner.");
            }
        }
        internal void ObservePreparedStep(NavPreparedCrowdStep c)
        {
            ValidatePreparationLedger(c.Reservation);
            if(unitView.Count!=c.Source.Length)throw new InvalidOperationException("Crowd membership changed.");
            c.Geometry=geometry;c.Profile=profile;c.NextIncarnation=nextIncarnation;c.MaximumRadius=maximumRadius;
            c.GeometryPin=geometry.StorageRoot;c.GeometryPin?.Retain();c.ProfilePin=profile.StorageRoot;c.ProfilePin?.Retain();c.NamesPin=ownedSurfaceNames as NavOwnedStorage;c.NamesPin?.Retain();c.SourceSpatial=spatial;spatial?.Retain();
            c.SourceNeighbor=NeighborCandidates;c.SourceRepair=RepairCount;c.SourceFailed=RepairFailed;c.SourceNoProgress=MaxNoProgressTicks;c.SourceRepairCpu=RepairCpuMs;
            for(int i=0;i<c.Source.Length;i++){
                var actor=unitView[i];if(actor.StorageOwner==null)throw new InvalidOperationException("Prepared motion requires owned actors.");c.Source[i]=actor;c.Before[i]=actor.CopyMovementHeader();c.After[i]=actor.CopyMovementHeader();
                c.LocationPins[i]=actor.Location.NameReference.Owner;c.LocationPins[i]?.Retain();c.GoalPins[i]=actor.GoalLocation?.NameReference.Owner;c.GoalPins[i]?.Retain();c.FailurePins[i]=actor.FailedGoal?.NameReference.Owner;c.FailurePins[i]?.Retain();c.ActorPins[i]=actor.StorageOwner;c.ActorPins[i].Retain();c.Globals[i]=actor.StorageOwner.Global;c.Globals[i]?.Retain();c.Locals[i]=actor.StorageOwner.Local;c.Locals[i]?.Retain();
            }
            int at=0;foreach(var actor in units.Values)c.InitialRows[at++]=ActorIndex(actor.Id);
            var work=c.Working;work.geometry=geometry;work.profile=profile;work.ownedSurfaceNames=ownedSurfaceNames;work.maximumRadius=maximumRadius;work.nextIncarnation=nextIncarnation;work.spatial=c.NextSpatial;
            work.RepairCount=RepairCount;work.RepairFailed=RepairFailed;work.MaxNoProgressTicks=MaxNoProgressTicks;work.RepairCpuMs=RepairCpuMs;
        }
        internal bool PreparedStepSourceCurrent(NavPreparedCrowdStep c)
        {
            if(preferredMaxSpeed.Count!=0||!ReferenceEquals(c.SourceCrowd,this)||!ReferenceEquals(geometry,c.Geometry)||!ReferenceEquals(profile,c.Profile)||!ReferenceEquals(ownedSurfaceNames,c.NamesPin)||!ReferenceEquals(spatial,c.SourceSpatial)||unitView.Count!=c.Source.Length||nextIncarnation!=c.NextIncarnation||maximumRadius!=c.MaximumRadius||NeighborCandidates!=c.SourceNeighbor||RepairCount!=c.SourceRepair||RepairFailed!=c.SourceFailed||MaxNoProgressTicks!=c.SourceNoProgress||RepairCpuMs!=c.SourceRepairCpu)return false;
            for(int i=0;i<c.Source.Length;i++)if(!ReferenceEquals(unitView[i],c.Source[i])||!c.Before[i].SameMovementValues(unitView[i])||!ReferenceEquals(unitView[i].StorageOwner,c.ActorPins[i])||!ReferenceEquals(c.Globals[i],unitView[i].StorageOwner.Global)||!ReferenceEquals(c.Locals[i],unitView[i].StorageOwner.Local))return false;
            int at=0;foreach(var actor in units.Values)if(!ReferenceEquals(actor,c.Source[c.InitialRows[at++]]))return false;return true;
        }
        internal void ApplyPreparedStepNoThrow(NavPreparedCrowdStep c)
        {c.RetiredSpatial=spatial;spatial=c.NextSpatial;NeighborCandidates=c.Working.NeighborCandidates;RepairCount=c.Working.RepairCount;RepairFailed=c.Working.RepairFailed;MaxNoProgressTicks=c.Working.MaxNoProgressTicks;RepairCpuMs=c.Working.RepairCpuMs;}
    }
}
