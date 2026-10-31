using System;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
internal static class StartupCacheChecks
{
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static NavigationRetentionBudget.Scope Open(NavigationRetentionBudget budget){Require(budget.TryOpenScope(NavigationPreparationKind.Admission,0,0,out var scope),"data scope refused");return scope;}
    internal static void RunCacheNegative(){var profile=PlayableProfile.RecastGroupCorridorDefault;var map=ThreeCrossingsMap.Default;RetiredMapAndDenial(map,profile,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision));}
    internal static void Run()
    {
        var map=ThreeCrossingsMap.Default;var profile=PlayableProfile.RecastGroupCorridorDefault;
        var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
        var budget=new NavigationRetentionBudget();var cache=new RecastMapCache();
        ulong before=RecastNative.sw_recast_allocation_count();long prepare;
        var scope=Open(budget);
        try{
            var r=scope.OwnedReservation();cache.PreparePublic(map,profile.Navigation,r);
            Require(cache.FoundationBuilds==1&&cache.PrepareCount==1&&cache.FoundationReuses==1,"startup did not prepare public native foundation");
            prepare=cache.PrepareCount;
            using var first=cache.Acquire(map,geometry,profile.Navigation,null,r);
            Require(first.Map.Geometry.UsedFoundation,"first request did not use startup foundation");
            using var warm=cache.Acquire(map,geometry,profile.Navigation,null,r);
            Require(ReferenceEquals(first.Map,warm.Map)&&cache.PrepareCount==prepare&&cache.FoundationBuilds==1,"warm request repeated Prepare/clipping/digest");
            var overlay=new NavGeometry(geometry.HalfExtent,geometry.Obstacles.Concat(new[]{new NavObstacle(-2,12,2,14)}).ToArray(),geometry.Revision+1);
            using var known=cache.Acquire(map,overlay,profile.Navigation,new string('a',64),r);
            using var knownWarm=cache.Acquire(map,overlay,profile.Navigation,new string('a',64),r);
            Require(known.Map.Geometry.UsedFoundation&&ReferenceEquals(known.Map,knownWarm.Map)&&cache.PrepareCount==prepare+1,"known overlay did not reuse authored clipping/exact variant");
            using var otherKnowledge=cache.Acquire(map,overlay,profile.Navigation,new string('b',64),r);
            Require(!ReferenceEquals(known.Map,otherKnowledge.Map),"knowledge identity aliased map variant");
            var revised=new NavGeometry(geometry.HalfExtent,geometry.Obstacles.ToArray(),geometry.Revision+1);
            using var version=cache.Acquire(map,revised,profile.Navigation,null,r);
            Require(!ReferenceEquals(first.Map,version.Map),"geometry version aliased public variant");
            long count=cache.PrepareCount;
            try{using var cancelled=cache.Acquire(map,geometry,profile.Navigation,null,r,()=>true);throw new Exception("warm cancellation ignored");}catch(OperationCanceledException){}
            Require(cache.PrepareCount==count,"cancelled lookup prepared geometry");
            Console.WriteLine($"STARTUP/CACHE foundation={cache.FoundationBuilds} prepare={cache.PrepareCount} digest={cache.GeometryDigests} native={cache.ColdBuilds} warm={cache.WarmHits} public+known+version+cancel PASS");
        }finally{scope.Discard();cache.Dispose();}
        Require(budget.PhysicalBytes==0&&budget.HeldBytes==0&&RecastNative.sw_recast_allocation_count()==before,"cache leaked physical/native owners");
        RetiredMapAndDenial(map,profile,geometry);
        Contexts(map,profile,geometry);
        SessionStartup(profile,geometry);
    }
    private static void RetiredMapAndDenial(ThreeCrossingsMap map,PlayableProfile profile,NavGeometry geometry)
    {
        ulong before=RecastNative.sw_recast_allocation_count();var budget=new NavigationRetentionBudget();var cache=new RecastMapCache();
        var create=Open(budget);RecastNavigationQuery query=null;
        try{
            var r=create.OwnedReservation();cache.PreparePublic(map,profile.Navigation,r);
            using var pin=cache.Acquire(map,geometry,profile.Navigation,null,r);
            Require(map.TryLocate(new NavPoint(-31,-46),profile.Navigation.Radius,out var from),"missing pin start");Require(map.TryLocate(new NavPoint(-46,-46),profile.Navigation.Radius,out var to),"missing pin goal");
            using var native=RecastNative.Memory(r);query=pin.Map.Query(from,to);
        }finally{create.Discard();}
        var denied=Open(budget);int allocations=0;long prepare=cache.PrepareCount;var prior=RecastNative.sw_recast_allocation_count();budget.AllocationAdmission=_=>++allocations!=2;
        try{
            var overlay=new NavGeometry(geometry.HalfExtent,geometry.Obstacles.Concat(new[]{new NavObstacle(-2,12,2,14)}).ToArray(),geometry.Revision+1);
            try{using var refuse=cache.Acquire(map,overlay,profile.Navigation,null,denied.OwnedReservation());throw new Exception("cache capacity denial ignored");}catch(InvalidOperationException error){Require(allocations==2&&error.Message=="Scratch exceeds prepared reservation.","expected actual second-allocation denial, got "+allocations+"/"+error.Message);}
        }finally{budget.AllocationAdmission=null;denied.Discard();}
        Require(cache.PrepareCount==prepare&&RecastNative.sw_recast_allocation_count()==prior,"denied entry partially published/rebuilt native state");
        var other=new NavigationRetentionBudget();var foreign=Open(other);
        try{try{using var illegal=cache.Acquire(map,geometry,profile.Navigation,null,foreign.OwnedReservation());throw new Exception("foreign ledger reused cache");}catch(ArgumentException){}}
        finally{foreign.Discard();}
        cache.RetireUnused();cache.Dispose();Require(budget.PhysicalBytes>0,"retired native map lost geometry/ledger while query pin was live");
        var step=Open(budget);
        try{using var native=RecastNative.Memory(step.OwnedReservation());while(query.Status==RecastQueryStatus.Pending)query.Step(32);Require(query.Status==RecastQueryStatus.Full,"retired pinned query invalidated");}
        finally{step.Discard();query.Dispose();}
        Require(budget.PhysicalBytes==0&&budget.HeldBytes==0&&other.PhysicalBytes==0&&RecastNative.sw_recast_allocation_count()==before,"retired map/query payload leaked budget");
        Console.WriteLine("CACHE deniedentry/foreignledger/retiredmap+geometry-payload/livequery lifetime PASS");
    }
    private static NavigationAdmissionStorage Admission(ThreeCrossingsMap map,NavigationProfile profile,NavGeometry geometry,NavigationRequest[] requests,INavStorageReservation r)
    {
        var p=NavigationProfile.CopyOwned(profile,r);var g=geometry.StorageRoot==null?NavGeometry.CopyOwned(geometry,r):geometry;
        var names=NavRetainedArray<NavNameReference>.Copy(Array.Empty<NavNameReference>(),r.TryReserve);
        var versions=NavRetainedArray<NavOwnedStorage>.Copy(Array.Empty<NavOwnedStorage>(),r.TryReserve);
        var rows=new NavigationHostRequest[requests.Length];NavRetainedArray<NavOwnedStorage> owned=null;
        try{
            for(int i=0;i<rows.Length;i++){var source=requests[i];var value=new NavigationRequest(source.Session,source.Request,source.Entity,source.Order,source.Profile,g,source.Start,source.Goal,g,source.HeldIdentity,source.MemberIdentity,source.StartLocation,source.GoalLocation);rows[i]=NavigationHostRequest.CopyOwned(value,r);}
            owned=NavRetainedArray<NavOwnedStorage>.Copy(rows.Cast<NavOwnedStorage>().ToArray(),r.TryReserve);
            return NavigationAdmissionStorage.PrepareOwned(1,requests.Length,map,map.Id,map.SurfaceSemanticsVersion,g,p,names,versions,owned,1,null,r);
        }finally{p.StorageRoot.Release();if(!ReferenceEquals(g,geometry))g.StorageRoot.ReleaseOwner();names.Release();versions.Release();owned?.Release();foreach(var row in rows)row?.Release();}
    }
    private static void Contexts(ThreeCrossingsMap map,PlayableProfile profile,NavGeometry geometry)
    {
        var budget=new NavigationRetentionBudget();var scope=Open(budget);RecastGroupPreparation prep=null;NavigationAdmissionStorage admission=null,empty=null;
        try{
            var r=scope.OwnedReservation();prep=RecastGroupPreparation.Create(r);prep.QueryStepWork=1;
            var small=profile.Navigation.ForUnit(profile.ExplorerCollisionRadius,profile.Navigation.TankSpeed,profile.Navigation.TankTurnSpeed);
            var from=new NavPoint(-31,-46);var to=new NavPoint(-46,-46);
            NavigationRequest Request(long sequence,long group,NavigationProfile p){Require(map.TryLocate(from,p.Radius,out var a),"invalid data start");Require(map.TryLocate(to,p.Radius,out var b),"invalid data goal");return new NavigationRequest(1,sequence,(int)sequence,1,p,geometry,from,to,memberIdentity:new NavigationMemberIdentity(sequence,0,group,1,1),startLocation:a,goalLocation:b);}
            // Same-class and different-class interleaving share the exact test
            // body. These are solver data inputs, not an overlapping-unit match.
            for(int classes=0;classes<2;classes++){
                var requests=new[]{Request(1,10,profile.Navigation),Request(2,10,profile.Navigation),Request(3,20,classes==0?profile.Navigation:small),Request(4,20,classes==0?profile.Navigation:small)};
                admission=Admission(map,profile.Navigation,geometry,requests,r);
                using var pin=new NavigationAdmissionPin(admission,admission.Value);
                var a=admission.Value.Requests[0];var b=admission.Value.Requests[2];
                var providerA=new PartitionNavigationProvider(map,a.Geometry,a.HeldIdentity);var providerB=new PartitionNavigationProvider(map,b.Geometry,b.HeldIdentity);
                long queries=prep.LongQueries;
                Require(!prep.TryGet(pin,a,providerA,r,null,out _,out _),"A did not leave a live sliced query");
                Require(!prep.TryGet(pin,b,providerB,r,null,out _,out _),"B did not leave a live sliced query");
                Require(prep.LongQueries==queries+2,"interleave replaced A query");
                RecastMemberRoute routeA=null,routeB=null;NavSolveStatus statusA=NavSolveStatus.Pending,statusB=NavSolveStatus.Pending;
                for(int i=0;i<128&&(statusA==NavSolveStatus.Pending||statusB==NavSolveStatus.Pending);i++){
                    prep.TryGet(pin,a,providerA,r,null,out routeA,out statusA);prep.TryGet(pin,b,providerB,r,null,out routeB,out statusB);
                }
                Require(statusA==NavSolveStatus.Ready&&statusB==NavSolveStatus.Ready&&routeA!=null&&routeB!=null,"interleaved queries did not finish");
                Require(prep.TryGet(pin,a,providerA,r,null,out var again,out _)&&ReferenceEquals(again,routeA)&&prep.LongQueries==queries+2,"A/B result was recomputed");
                try{prep.TryGet(pin,a,providerA,r,()=>true,out _,out _);throw new Exception("context cancel ignored");}catch(OperationCanceledException){}
                empty=Admission(map,profile.Navigation,geometry,Array.Empty<NavigationRequest>(),r);prep.Prune(empty.Value);Require(!prep.HasTransientWork,"stale subgroup pin survived prune");empty.Release();empty=null;
                admission.Release();admission=null;
            }
            Require(prep.LongQueries==4&&prep.Attachments==8,"one long query per unchanged subgroup violated");
            Console.WriteLine($"CONTEXT A/B/A/B same+different classes queries={prep.LongQueries} attachments={prep.Attachments} prepare={prep.PrepareCount} foundation={prep.FoundationBuilds} cancel/stale/results PASS");
        }finally{admission?.Release();empty?.Release();prep?.Dispose();scope.Discard();}
        Require(budget.PhysicalBytes==0&&budget.HeldBytes==0,"context/admission lifetime leaked physical owners");
    }
    private static void SessionStartup(PlayableProfile profile,NavGeometry geometry)
    {
        var session=new NavigationSession(2,geometry,profile.Navigation,null,profile.AuthoredMap){UsesRecastGroundRoutes=true};
        var before=RecastNative.sw_recast_allocation_count();
        try{
            session.PhysicalHostBudget.AllocationAdmission=_=>false;
            try{session.RoutePort.PrepareRecastStartup(profile,null);throw new Exception("startup capacity denial ignored");}catch(RecastNative.CapacityRefused){}
            Require(RecastNative.sw_recast_allocation_count()==before,"failed startup published native resources");session.PhysicalHostBudget.AllocationAdmission=null;
            var prep=session.RoutePort.PrepareRecastStartup(profile,null);
            Require(ReferenceEquals(prep,session.RoutePort.PrepareRecastStartup(profile,null))&&prep.FoundationBuilds==1&&prep.ColdBuilds>=1,"Session startup preparation was not reused");
            Console.WriteLine($"SESSION startup foundation={prep.FoundationBuilds} variants={prep.ColdBuilds} prepare={prep.PrepareCount} retained={session.PhysicalHostBudget.PhysicalBytes} PASS");
        }finally{session.DestroyNavigationAuthority();}
        Require(RecastNative.sw_recast_allocation_count()==before,"startup Session shutdown leaked native allocations");
    }
}
