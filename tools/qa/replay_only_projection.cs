using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Spacewars.Simulation;
using Spacewars.Runtime;

// QA-only wrapper in the copied kernel source. Always executes the actual
// forward trace first; forced outcomes test the existing failure boundary.
internal static class ReplayFault
{
    internal static int Mode,Forced;internal static NavPoint Final;
    internal static bool TryTrace(PartitionNavigationProvider provider,NavLocation from,NavPoint to,
        NavClearanceProfile clearance,out NavLocation reached,out IReadOnlyList<NavTypedLeg> trace)
    {
        bool ok=provider.TryTrace(from,to,clearance,out reached,out trace);
        if(!ok||Mode==0)return ok;
        if(Mode==1){Mode=0;Forced++;return false;}
        if(Mode==2&&trace.Any(l=>l.Kind==NavTypedLegKind.Portal)){
            Mode=0;Forced++;trace=trace.Where(l=>l.Kind!=NavTypedLegKind.Portal).ToArray();
        }else if(Mode==3&&to.Equals(Final)){
            Mode=0;Forced++;string oldSurface=reached.SurfaceId;reached=provider.Surfaces.First(s=>s.Id!=oldSurface).OwnedLocation(to);
        }
        return ok;
    }
}

internal static class Program
{
    static string Folder;static long serial;static readonly List<object> Checks=new();
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    static void Require(bool c,string s){if(!c)throw new Exception(s);}
    static NavigationRetentionBudget.Scope Open(NavigationRetentionBudget b,NavigationPreparationKind k=NavigationPreparationKind.Projection)
    {if(!b.TryOpenScope(k,7,++serial,out var s))throw new InvalidOperationException("scope admission");return s;}
    static byte[] Capture(NavigationRetentionBudget b,NavOwnedStorage root)
    {using var s=Open(b,NavigationPreparationKind.Capture);using var bytes=NavigationCaptureBytes.Write(s.OwnedReservation(),w=>NavPhysicalBodyWire.WriteTo(w,root,s.OwnedReservation()));s.Attach(bytes);s.Commit(bytes,_=>{});return bytes.TransferPublic();}
    static T Cold<T>(NavigationRetentionBudget b,byte[] bytes) where T:NavOwnedStorage
    {using var s=Open(b,NavigationPreparationKind.Decode);using var stream=new MemoryStream(bytes,false);using var r=new BinaryReader(stream);var value=(T)NavPhysicalBodyWire.ReadFrom(r,s.OwnedReservation(),NavigationHostBodyExtension.Instance);Require(stream.Position==stream.Length,"BODY EOF");s.Attach(value);s.Commit(value,_=>{});return value;}
    static NavigationHostRow Row(NavigationHostRoot r)=>(NavigationHostRow)r.Rows[r.Projection.First];
    static NavigationHostRoot Step(NavigationRetentionBudget b,NavigationHostRoot root)
    {using var s=Open(b);NavigationHostRoot next=null;try{next=root.ForkOwned(s.OwnedReservation());Require(CertifiedRouteKernel.StageNextPhysicalProjection(next,s.OwnedReservation(),out var error),"paid step missing");Require(error==null,"actual route projection failed: "+error);next.SetFrontier(root.Frontier,root.Epoch,root.Progress+1);next.SealPrepared();s.Attach(next);s.Commit(next,_=>{});return next;}catch{next?.ReleaseStorage();throw;}}
    static NavigationHostRoot Birth(NavigationRetentionBudget b,NavScheduledRequest recipe,NavTypedLeg[] raw,double cost,bool compact)
    {using var s=Open(b);var r=s.OwnedReservation();var owned=NavScheduledRequest.CopyOwned(recipe,recipe.Provider,r);NavPagedList<NavTypedLeg> legs=null;NavTypedRoute route=null;NavigationHostRoot root=null;try{
        legs=NavPagedList<NavTypedLeg>.CreateOwned(r,4);foreach(var leg in raw){var copy=NavTypedLeg.CreateOwned(leg.From,leg.To,leg.Cost,leg.Kind,leg.PortalId,r,owned.Provider);try{legs.Add(copy);}finally{copy.Release();}}
        route=NavTypedRoute.CopySubscriptionAndCreateOwned(owned.Subscription,legs,cost,r);
        var locations=EdgeAudit.Field<TerrainLocations>(owned.Provider,"locations");var geometry=locations.Geometry;
        var sub=owned.Subscription;var request=new NavigationRequest(sub.WorldGeneration,sub.RequestSequence,(int)sub.EntityId,sub.MemberOrderRevision,PlayableProfile.ThreeCrossingsDefault.Navigation,geometry,sub.Origin.Position,sub.AssignedEndpoint.Position,geometry,sub.HoldBinding,null,sub.Origin,sub.AssignedEndpoint);
        root=compact?NavigationHostRoot.CreateReplayOnlyOwned(r):NavigationHostRoot.CreateOwned(r);
        root.RegisterArrival(request,owned,route,7,"actual-provider",r);root.SealPrepared();s.Attach(root);s.Commit(root,_=>{});return root;
    }catch{root?.ReleaseStorage();throw;}finally{owned.StorageRoot.Release();route?.ReleaseStorage();legs?.Release();}}
    static NavLocation Location(BinaryReader r)=>new(new NavPoint(0,0),"unused");
    static NavLocation ReadLocation(BinaryReader r){string surface=r.ReadString();return new NavLocation(new NavPoint(r.ReadDouble(),r.ReadDouble()),surface);}
    static NavTypedLeg[] ReadLegs(byte[] bytes)
    {using var m=new MemoryStream(bytes);using var r=new BinaryReader(m);var result=new NavTypedLeg[r.ReadInt32()];for(int i=0;i<result.Length;i++){var from=ReadLocation(r);var to=ReadLocation(r);var kind=(NavTypedLegKind)r.ReadInt32();double cost=r.ReadDouble();var portal=r.ReadBoolean()?r.ReadString():null;result[i]=new NavTypedLeg(from,to,cost,kind,portal);}Require(m.Position==m.Length,"typed bytes EOF");return result;}
    static void ColdNext(NavigationRetentionBudget b,NavigationHostRoot root,string label)
    {var before=Capture(b,root);var cold=Cold<NavigationHostRoot>(b,before);NavigationHostRoot hotNext=null,coldNext=null;try{
        Require(before.SequenceEqual(Capture(b,cold)),label+" immediate BODY");hotNext=Step(b,root);coldNext=Step(b,cold);
        var h=Capture(b,hotNext);var c=Capture(b,coldNext);Require(h.SequenceEqual(c),label+" exact next BODY");
        File.WriteAllBytes(Path.Combine(Folder,label+".before.body"),before);File.WriteAllBytes(Path.Combine(Folder,label+".next.body"),h);Checks.Add(new{label,coldNext=true,bytes=before.Length,sha256=EdgeAudit.Hash(before)});
    }finally{hotNext?.ReleaseStorage();coldNext?.ReleaseStorage();cold.ReleaseStorage();}}
    static void Refusals(NavigationRetentionBudget b,NavigationHostRoot root,string label,Func<NavigationHostRoot,INavStorageReservation,bool> action=null)
    {
        var original=Capture(b,root);long physical=b.PhysicalBytes;int sites=0;
        void Attempt(){using var scope=Open(b);NavigationHostRoot next=null;try{next=root.ForkOwned(scope.OwnedReservation());if(action==null)Require(CertifiedRouteKernel.StageNextPhysicalProjection(next,scope.OwnedReservation(),out _),"refusal step");else Require(action(next,scope.OwnedReservation()),"refusal action");next.SealPrepared();scope.Attach(next);/* deliberately discard candidate */}finally{next?.ReleaseStorage();}}
        b.AllocationAdmission=_=>{sites++;return true;};try{Attempt();}finally{b.AllocationAdmission=null;}
        Require(sites>0,label+" sites");Require(b.PhysicalBytes==physical,"successful discarded candidate leaked "+label);
        for(int fail=1;fail<=sites;fail++){int seen=0;bool refused=false;b.AllocationAdmission=_=>++seen!=fail;try{Attempt();}catch(InvalidOperationException){refused=true;}finally{b.AllocationAdmission=null;}
            Require(refused,label+" missing refusal "+fail);Require(b.PhysicalBytes==physical&&b.HeldBytes==0&&b.ActiveScopes==0,label+" ledger "+fail);
            Require(original.SequenceEqual(Capture(b,root)),label+" pinned bytes "+fail);
        }
        Checks.Add(new{label,allocationRefusals=sites,atomic=true});
    }
    static object Run(NavigationRetentionBudget b,NavigationHostRoot start,bool compact)
    {
        var root=start;root.Retain();int legs=0,replays=0,points=-1,portals=0;var coldDone=new HashSet<string>();var refusalDone=new HashSet<string>();var traceRows=new List<object>();long maxPhysical=0,maxHeld=0;
        try{
            while(root.Projection.Count>0){var row=Row(root);var p=row.ProjectionOwner;var route=row.Solved.Route;string tag=null;
                if(p.LegCursor<route.Legs.Count){var leg=route.Legs[p.LegCursor];legs++;tag=leg.Kind==NavTypedLegKind.Portal?"portal":p.LegCursor>0&&NavigationHostProjection.CanMerge(route.Legs[p.LegCursor-1],leg)?"merge":"append";
                    if(compact&&tag=="portal"&&coldDone.Add(tag))ColdNext(b,root,"compact-portal");
                    if(compact&&tag=="merge"&&coldDone.Add(tag))ColdNext(b,root,"compact-partial-merge");
                }else if(p.ReplayCursor<p.PointCount){replays++;points=p.PointCount;var actual=(PartitionNavigationProvider)row.Provider;
                    using(var scope=Open(b))using(NavStorageScratch.Reserve(scope.OwnedReservation(),512L+actual.TraceScratchBound+8L*actual.TracePortalBound)){Require(actual.TryTrace(p.At,p.Point(p.ReplayCursor),row.Scheduled.Clearance,out var at,out var trace),"actual compact forward trace");var crossed=trace.Where(l=>l.Kind==NavTypedLegKind.Portal).Select(l=>l.PortalId).ToArray();portals+=crossed.Length;traceRows.Add(new{cursor=p.ReplayCursor,from=new{surface=p.At.SurfaceId,x=p.At.Position.X,z=p.At.Position.Z},to=new{x=p.Point(p.ReplayCursor).X,z=p.Point(p.ReplayCursor).Z},surface=at.SurfaceId,portals=crossed});tag=crossed.Length>0?"replay-portals":"replay";}
                    if(compact&&p.ReplayCursor>0&&coldDone.Add("replay"))ColdNext(b,root,"compact-replay");
                }else tag="ready";
                if(compact&&refusalDone.Add(tag))Refusals(b,root,"compact-"+tag);
                var old=root;root=Step(b,root);old.Release();maxPhysical=Math.Max(maxPhysical,b.PhysicalBytes);maxHeld=Math.Max(maxHeld,b.HeldBytes);
            }
            var ready=(NavigationHostRow)root.Rows[root.Completion.First];Require(ready.Answer.Status==NavSolveStatus.Ready,"Ready");var expected=ready.Solved.Route.Legs.Where(l=>l.Kind==NavTypedLegKind.Surface).Select(l=>l.To.Position).ToArray();
            var bytes=EdgeAudit.Bytes(w=>{w.Write(ready.Answer.RouteLength);foreach(var p in ready.Answer.ReadOnlyRouteBacking)NavContract.Point(w,p);});Require(expected.SequenceEqual(ready.Answer.ReadOnlyRouteBacking),"byte-exact original points");
            Require(legs==214,"all original214 legs paid");File.WriteAllBytes(Path.Combine(Folder,(compact?"compact":"legacy")+".ready-points.bin"),bytes);
            Require(EdgeAudit.Legs(ready.Solved.Route.Legs).SequenceEqual(File.ReadAllBytes(Path.Combine(Folder,"14.typed-legs.bin"))),"all typed bytes preserved");
            var report=new{compact,legs,replays,points,portals,projectionSteps=legs+replays+1,ReadyPoints=expected.Length,readySHA256=EdgeAudit.Hash(bytes),typedSHA256=EdgeAudit.Hash(EdgeAudit.Legs(ready.Solved.Route.Legs)),costBits=BitConverter.DoubleToInt64Bits(ready.Solved.Route.Cost).ToString("x16"),maxPhysical,maxHeld,retained=b.RetainedBytes,peak=b.PeakRetainedBytes,traceRows};Checks.Add(report);return report;
        }finally{root.Release();}
    }
    static INavStorageReservation budgetReservation(NavigationRetentionBudget b,out NavigationRetentionBudget.Scope s){s=Open(b);return s.OwnedReservation();}
    static void Guards(NavigationRetentionBudget b,NavigationHostRoot source)
    {
        var origin=Row(source).Scheduled.Subscription.Origin;double x=origin.Position.X,z=origin.Position.Z;
        var raw=new List<NavTypedLeg>();NavTypedLeg Leg(double ax,double az,double bx,double bz,string surface=null,NavTypedLegKind kind=NavTypedLegKind.Surface)
        {var l=new NavTypedLeg(new NavLocation(new NavPoint(ax,az),surface??origin.SurfaceId),new NavLocation(new NavPoint(bx,bz),surface??origin.SurfaceId),0,kind,kind==NavTypedLegKind.Portal?"portal":null);raw.Add(l);return l;}
        try{
            var straight=Leg(x,z,x+1,z);var next=Leg(x+1,z,x+2,z);var zero=Leg(x+1,z,x+1,z);var corner=Leg(x+1,z,x+1,z+1);
            Require(NavigationHostProjection.CanMerge(straight,next),"straight merge");Require(!NavigationHostProjection.CanMerge(straight,zero)&&!NavigationHostProjection.CanMerge(zero,next),"zero boundary");
            Require(!NavigationHostProjection.CanMerge(straight,corner),"turn boundary");Require(!NavigationHostProjection.CanMerge(straight,Leg(x+1,z,x,z)),"reversal boundary");
            Require(!NavigationHostProjection.CanMerge(straight,Leg(x+1,z,x+2,z,"other")),"surface boundary");Require(!NavigationHostProjection.CanMerge(straight,Leg(x+1,z,x+1,z,null,NavTypedLegKind.Portal)),"portal boundary");
            Require(!NavigationHostProjection.CanMerge(straight,Leg(x+1.000000000001,z,x+2,z)),"exact joint");Require(!NavigationHostProjection.CanMerge(Leg(x,z,x+1,z+1),Leg(x+1,z+1,x+2,z+2)),"strict single axis only");
            foreach(var direction in new[]{1d,-1d}){Require(NavigationHostProjection.CanMerge(Leg(x,z,x+direction,z),Leg(x+direction,z,x+2*direction,z)),"both X directions");Require(NavigationHostProjection.CanMerge(Leg(x,z,x,z+direction),Leg(x,z+direction,x,z+2*direction)),"both Z directions");}
            using var scope=Open(b);var r=scope.OwnedReservation();var projection=NavigationHostProjection.CreateOwned(origin,r,true);
            try{
                // corner -> zero -> straight: zero must survive and prevent merge.
                var c=Leg(x,z,x,z+1);var n=Leg(x,z+1,x,z+1);var t=Leg(x,z+1,x,z+2);
                var ownedC=NavTypedLeg.CreateOwned(c.From,c.To,c.Cost,c.Kind,c.PortalId,r,Row(source).Provider);raw.Add(ownedC);
                var ownedN=NavTypedLeg.CreateOwned(n.From,n.To,n.Cost,n.Kind,n.PortalId,r,Row(source).Provider);raw.Add(ownedN);
                var ownedT=NavTypedLeg.CreateOwned(t.From,t.To,t.Cost,t.Kind,t.PortalId,r,Row(source).Provider);raw.Add(ownedT);
                projection.AdvanceLeg(ownedC,default);projection.AdvanceLeg(ownedN,default,ownedC);projection.AdvanceLeg(ownedT,default,ownedN);Require(projection.PointCount==3,"corner-zero-straight points");
            }finally{projection.ReleaseStorage();}
            Checks.Add(new{guardCases=13,cornerZeroStraight=3,exactNoEpsilon=true});
        }finally{foreach(var l in raw)l.ReleaseStorage();}
    }
    static NavigationHostRoot ReplayFailureStep(NavigationRetentionBudget b,NavigationHostRoot root)
    {using var s=Open(b);NavigationHostRoot next=null;try{next=root.ForkOwned(s.OwnedReservation());next.StageReplayFailure(next.Projection.First,s.OwnedReservation());next.SetFrontier(root.Frontier,root.Epoch,root.Progress+1);next.SealPrepared();s.Attach(next);s.Commit(next,_=>{});return next;}catch{next?.ReleaseStorage();throw;}}
    static void Fallback(NavigationRetentionBudget b,NavigationHostRoot source,int mode)
    {
        var root=source;root.Retain();int paid=0,legacyLegs=0,legacyReplay=0;bool reset=false;int forced=ReplayFault.Forced;var originalKey=root.Projection.First;
        try{
            while(Row(root).ProjectionOwner.LegCursor<Row(root).Solved.Route.Legs.Count){var old=root;root=Step(b,root);old.Release();paid++;}
            var initial=Row(root);var oldSolved=initial.Solved;var oldProvider=initial.ProviderOwner;var oldSubscription=initial.Scheduled.Subscription;
            ReplayFault.Final=initial.Scheduled.Subscription.AssignedEndpoint.Position;ReplayFault.Mode=mode;
            while(root.Projection.Count>0){var row=Row(root);var p=row.ProjectionOwner;
                if(!p.ReplayOnly){if(p.LegCursor<row.Solved.Route.Legs.Count)legacyLegs++;else if(p.ReplayCursor<p.PointCount)legacyReplay++;}
                using var s=Open(b);NavigationHostRoot next=null;
                try{next=root.ForkOwned(s.OwnedReservation());Require(CertifiedRouteKernel.StageNextPhysicalProjection(next,s.OwnedReservation(),out var failure),"forced step");
                    if(failure!=null){Require(p.ReplayOnly,"legacy failure not masked");Require(next.Projection.Count==root.Projection.Count&&next.Projection.First.Value==originalKey.Value,"reset queue identity");var saved=Row(next);var q=saved.ProjectionOwner;
                        Require(!q.ReplayOnly&&q.LegCursor==0&&q.ReplayCursor==0&&q.PointCount==0&&q.TypedCount==0&&q.ObservedCount==0&&q.At.Equals(oldSubscription.Origin)&&q.Expected.Equals(oldSubscription.Origin),"reset exact origin/cursors");
                        Require(ReferenceEquals(saved.Solved,oldSolved)&&ReferenceEquals(saved.ProviderOwner,oldProvider)&&ReferenceEquals(saved.Scheduled.Subscription,oldSubscription),"reset exact solved/provider/subscription");reset=true;
                    }
                    next.SetFrontier(root.Frontier,root.Epoch,root.Progress+1);next.SealPrepared();s.Attach(next);s.Commit(next,_=>{});var old=root;root=next;next=null;old.Release();paid++;
                }finally{next?.ReleaseStorage();}
                if(reset&&RowIfProjecting(root)?.ProjectionOwner.LegCursor==0){
                    ColdNext(b,root,"fallback-"+mode+"-cold");
                    Refusals(b,root,"fallback-"+mode+"-next-leg");
                }
            }
            Require(reset&&ReplayFault.Forced==forced+1&&ReplayFault.Mode==0,"one forced fallback");Require(legacyLegs==214&&legacyReplay==208,"legacy repayment full");var ready=(NavigationHostRow)root.Rows[root.Completion.First];
            var bytes=EdgeAudit.Bytes(w=>{w.Write(ready.Answer.RouteLength);foreach(var p in ready.Answer.ReadOnlyRouteBacking)NavContract.Point(w,p);});Require(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(Folder,"legacy.ready-points.bin"))),"fallback Ready bytes");Checks.Add(new{fallbackMode=mode,paid,legacyLegs,legacyReplay,oneReset=true,coldNext=true});
        }finally{ReplayFault.Mode=0;root.Release();}
    }
    static NavigationHostRow RowIfProjecting(NavigationHostRoot root)=>root.Projection.Count==0?null:Row(root);
    static void ResetRefusals(NavigationRetentionBudget b,NavigationHostRoot source)
    {
        var root=source;root.Retain();try{
            while(Row(root).ProjectionOwner.LegCursor<20){var old=root;root=Step(b,root);old.Release();}
            Refusals(b,root,"legacy-reset",(n,r)=>{n.StageReplayFailure(n.Projection.First,r);return true;});
            var next=ReplayFailureStep(b,root);try{ColdNext(b,next,"reset-direct");Require(!Row(next).ProjectionOwner.ReplayOnly,"plain reset projection");}finally{next.ReleaseStorage();}
        }finally{root.Release();}
    }
    static void PendingPolicy(NavigationRetentionBudget b,NavScheduledRequest recipe,NavTypedLeg[] raw,double cost,bool compact)
    {
        NavigationHostRow pending=null,cold=null,hotSolved=null,coldSolved=null;NavigationHostRequest input=null;NavigationHostProvider provider=null;
        using(var s=Open(b)){var r=s.OwnedReservation();var locations=EdgeAudit.Field<TerrainLocations>(recipe.Provider,"locations");var geometry=locations.Geometry;var sub=recipe.Subscription;
            input=NavigationHostRequest.CopyOwned(new NavigationRequest(sub.WorldGeneration,sub.RequestSequence,(int)sub.EntityId,sub.MemberOrderRevision,PlayableProfile.ThreeCrossingsDefault.Navigation,geometry,sub.Origin.Position,sub.AssignedEndpoint.Position,geometry,sub.HoldBinding,null,sub.Origin,sub.AssignedEndpoint),recipe.Provider.Binding,r);
            try{provider=NavigationHostProvider.CopyOwned(recipe.Provider,input.Request.Geometry,"actual-provider",1,r);pending=NavigationHostRow.CreateRegistered(input,provider,1,recipe,r,compact);s.Attach(pending);s.Commit(pending,_=>{});}finally{input.ReleaseStorage();provider?.ReleaseStorage();}}
        NavigationHostRow Solve(NavigationHostRow row){using var s=Open(b);var r=s.OwnedReservation();var legs=NavPagedList<NavTypedLeg>.CreateOwned(r,4);NavTypedRoute route=null;NavScheduledCompletion completion=null;NavigationHostRow result=null;
            try{foreach(var leg in raw){var l=NavTypedLeg.CreateOwned(leg.From,leg.To,leg.Cost,leg.Kind,leg.PortalId,r,row.Provider);try{legs.Add(l);}finally{l.Release();}}
                route=NavTypedRoute.CopySubscriptionAndCreateOwned(row.Scheduled.Subscription,legs,cost,r);completion=new NavScheduledCompletion(row.Scheduled,NavSolveStatus.Ready,route,0,0,r.TryReserve);result=row.WithSolved(completion,r);s.Attach(result);s.Commit(result,_=>{});return result;
            }catch{result?.ReleaseStorage();throw;}finally{legs.Release();route?.ReleaseStorage();completion?.Release();}}
        try{Require(pending.ProjectionOwner==null&&pending.Solved==null,"pending before projection birth");var before=Capture(b,pending);cold=Cold<NavigationHostRow>(b,before);Require(before.SequenceEqual(Capture(b,cold)),"pending immediate bytes");hotSolved=Solve(pending);coldSolved=Solve(cold);Require(hotSolved.ProjectionOwner.ReplayOnly==compact&&coldSolved.ProjectionOwner.ReplayOnly==compact,"saved pending factory policy");Require(Capture(b,hotSolved).SequenceEqual(Capture(b,coldSolved)),"pending birth exact BODY");File.WriteAllBytes(Path.Combine(Folder,(compact?"compact":"legacy")+"-pending.body"),before);Checks.Add(new{pendingBeforeProjection=true,compact,exactBirth=true});
        }finally{pending.ReleaseStorage();cold?.ReleaseStorage();hotSolved?.ReleaseStorage();coldSolved?.ReleaseStorage();}
    }
    static void CodecBoundaries(NavigationRetentionBudget b,NavigationHostRoot source)
    {
        var root=source;root.Retain();try{
            while(Row(root).ProjectionOwner.LegCursor<Row(root).Solved.Route.Legs.Count||Row(root).ProjectionOwner.ReplayCursor<2){var old=root;root=Step(b,root);old.Release();}
            var bytes=Capture(b,root);long physical=b.PhysicalBytes;int captureSites=0,decodeSites=0;
            b.AllocationAdmission=_=>{captureSites++;return true;};try{Capture(b,root);}finally{b.AllocationAdmission=null;}
            for(int fail=1;fail<=captureSites;fail++){int seen=0;bool rejected=false;b.AllocationAdmission=_=>++seen!=fail;try{Capture(b,root);}catch(InvalidOperationException){rejected=true;}finally{b.AllocationAdmission=null;}
                Require(rejected&&b.PhysicalBytes==physical&&b.HeldBytes==0&&b.ActiveScopes==0,"capture allocation refusal "+fail);Require(bytes.SequenceEqual(Capture(b,root)),"capture source unchanged");}
            b.AllocationAdmission=_=>{decodeSites++;return true;};try{var cold=Cold<NavigationHostRoot>(b,bytes);cold.ReleaseStorage();}finally{b.AllocationAdmission=null;}
            for(int fail=1;fail<=decodeSites;fail++){int seen=0;bool rejected=false;b.AllocationAdmission=_=>++seen!=fail;NavigationHostRoot cold=null;try{cold=Cold<NavigationHostRoot>(b,bytes);}catch(InvalidOperationException){rejected=true;}finally{b.AllocationAdmission=null;cold?.ReleaseStorage();}
                Require(rejected&&b.PhysicalBytes==physical&&b.HeldBytes==0&&b.ActiveScopes==0,"decode allocation refusal "+fail);
            }
            foreach(var storage in new[]{NavPhysicalStorageKind.HostRootFairArrivalV1,NavPhysicalStorageKind.HostRow,NavPhysicalStorageKind.HostProjection}){
                int tag=new NavPhysicalTag(storage,value:NavPhysicalValueKind.ReplayOnlyProjectionV1).Encoded,legacy=new NavPhysicalTag(storage).Encoded;var corrupt=(byte[])bytes.Clone();int found=0;
                for(int at=13;at+4<=corrupt.Length;at++)if((corrupt[at-5]==1||corrupt[at-5]==2)&&BitConverter.ToInt32(corrupt,at)==tag){Array.Copy(BitConverter.GetBytes(legacy),0,corrupt,at,4);found++;}
                Require(found>0,"tag present "+storage);bool rejected=false;NavigationHostRoot cold=null;try{cold=Cold<NavigationHostRoot>(b,corrupt);}catch(ArgumentException){rejected=true;}finally{cold?.ReleaseStorage();}
                Require(rejected&&b.PhysicalBytes==physical&&b.HeldBytes==0&&b.ActiveScopes==0,"policy downgrade rejected "+storage);File.WriteAllBytes(Path.Combine(Folder,"malformed-"+storage+".body"),corrupt);
            }
            // An externally constructed prepared prefix is fully replay-validated.
            using(var scope=Open(b)){var r=scope.OwnedReservation();var row=Row(root);var bad=row.ProjectionOwner.ForkOwned(r);NavigationHostRow next=null;
                try{bad.AdvanceReplay(row.Scheduled.Subscription.Origin,Array.Empty<NavNameReference>());bad.SealPrepared();bool rejected=false;try{next=row.WithProjection(bad,r);}catch(ArgumentException){rejected=true;}Require(rejected,"arbitrary prepared Replay cannot bypass validator");}finally{next?.ReleaseStorage();bad.ReleaseStorage();}}
            Require(bytes.SequenceEqual(Capture(b,root)),"malformed/untrusted source pin unchanged");
            var foreign=new NavigationRetentionBudget();int admissions=0;using(var scope=Open(foreign)){foreign.AllocationAdmission=_=>{admissions++;return true;};bool rejected=false;try{Row(root).AdvanceProjection(NavigationHostRow.ProjectionTransition.Leg,default,default,null,scope.OwnedReservation());}catch(ArgumentException){rejected=true;}finally{foreign.AllocationAdmission=null;}Require(rejected&&admissions==0,"foreign ownership fails before allocation");}
            Require(foreign.PhysicalBytes==0&&foreign.HeldBytes==0&&foreign.ActiveScopes==0,"foreign zero ledger");
            Checks.Add(new{captureSites,decodeSites,allRefusalsAtomic=true,tagDowngrades=3,untrustedPreparedRejected=true,foreignOwnerRejectedBeforeAllocation=true});
        }finally{root.Release();}
    }
    static void OriginalLegFailure(NavigationRetentionBudget b,NavScheduledRequest recipe,double cost)
    {
        var origin=recipe.Subscription.Origin;var bad=new NavTypedLeg(origin,new NavLocation(new NavPoint(10000,10000),origin.SurfaceId),cost,NavTypedLegKind.Surface,null);NavigationHostRoot root=null,next=null;
        try{root=Birth(b,recipe,new[]{bad},cost,true);using var scope=Open(b);next=root.ForkOwned(scope.OwnedReservation());Require(CertifiedRouteKernel.StageNextPhysicalProjection(next,scope.OwnedReservation(),out var failure)&&failure=="physical leg qualification","original bad leg exact failure");Require(next.Projection.Count==0&&next.Completion.Count==1&&((NavigationHostRow)next.Rows[next.Completion.First]).Answer.Status==NavSolveStatus.BlockedConnector,"original leg failure terminal, no fallback");Checks.Add(new{originalLegQualificationFailureTerminal=true});}
        finally{next?.ReleaseStorage();root?.ReleaseStorage();bad.ReleaseStorage();}
    }
    static byte[] SessionBytes(NavigationSession session)
    {using var capture=session.CaptureOwnedState();using var bytes=WorldWire.PackOwned(w=>WorldWire.Write(w,capture),capture.CaptureBudget);return bytes.TransferPublic();}
    static void SessionCold(bool legacy,bool reset)
    {
        var map=ThreeCrossingsMap.Default;var profile=PlayableProfile.ThreeCrossingsDefault;
        var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
        NavigationSession Make()=>new NavigationSession(1,geometry,profile.Navigation,null,map,null,null,replayOnlyProjection:!legacy);
        var session=Make();var kernel=new CertifiedRouteKernel();byte[] prefix=null;int observed=0;
        PlayableRouteBinding Binding()=>new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
        void Dispose(){kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Require(session.PhysicalHostBudget.PhysicalBytes==0&&session.PhysicalHostBudget.HeldBytes==0&&session.PhysicalHostBudget.ActiveScopes==0,"logical session zero ledger");}
        try{
            if(legacy){using var s=Open(session.PhysicalHostBudget);var root=NavigationHostRoot.CreateOwned(s.OwnedReservation());try{root.SealPrepared();s.Attach(root);session.PublishPhysicalHostRoot(s,null,root,()=>{});}finally{root.ReleaseStorage();}}
            session.Crowd.Add(1,new NavPoint(-18,0));Require(session.Move(1,new NavPoint(18,0)),"logical move");
            if(!reset)prefix=SessionBytes(session); // Before any row or projection birth.
            else{
                kernel.BeforePhysicalProjectionStep=()=>{
                    if(prefix!=null)return;var root=session.PinCommittedHostRoot();try{
                        if(root.Projection.Count==0)return;var p=Row(root).ProjectionOwner;
                        if(p.LegCursor!=Row(root).Solved.Route.Legs.Count||p.ReplayCursor>0)return;
                        using var scope=Open(session.PhysicalHostBudget);var next=root.ForkOwned(scope.OwnedReservation());try{next.StageReplayFailure(next.Projection.First,scope.OwnedReservation());next.SealPrepared();scope.Attach(next);session.PublishPhysicalHostRoot(scope,root,next,()=>{});}finally{next.ReleaseStorage();}
                        prefix=SessionBytes(session);observed++;
                    }finally{root.Release();}
                };
                for(int n=0;n<20000&&prefix==null;n++)kernel.Pump(Binding(),4);Require(prefix!=null&&observed==1,"logical after-reset witness");
            }
            kernel.BeforePhysicalProjectionStep=null;
            // For reset, finish exactly the partially consumed saved Pump budget.
            var suffix=SessionBytes(session);kernel.Pump(Binding(),1);var nextBytes=SessionBytes(session);
            Dispose();session=Make();session.RestoreState(WorldWire.Unpack(prefix,WorldWire.ReadNavigationSessionState));kernel=new CertifiedRouteKernel();Require(prefix.SequenceEqual(SessionBytes(session)),"logical immediate exact");
            var restored=session.PinCommittedHostRoot();try{Require(restored.ReplayOnlyProjection==!legacy,"logical saved prebirth root policy");if(reset)Require(!Row(restored).ProjectionOwner.ReplayOnly,"logical saved reset legacy");}finally{restored.Release();}
            if(reset)kernel.Pump(Binding(),7);
            Require(suffix.SequenceEqual(SessionBytes(session)),"logical saved Pump suffix exact");kernel.Pump(Binding(),1);Require(nextBytes.SequenceEqual(SessionBytes(session)),"logical next full budget exact");
            File.WriteAllBytes(Path.Combine(Folder,"logical-"+(legacy?"legacy":"compact")+(reset?"-reset":"-prebirth")+".bin"),prefix);
            Checks.Add(new{logicalWorldWire=true,legacy,reset,beforeRowBirth=!reset,immediateSuffixNext=true});
        }finally{Dispose();}
    }
    static int Main(string[] args)
    {
        Folder=args[0];var b=new NavigationRetentionBudget();FixtureDeclarations declarations=null;NavigationHostRoot compact=null,legacy=null;NavTypedLeg[] legs=null;var report=new Dictionary<string,object>();string stage="donor/provider/recipe and actual214 preflight";
        try{
            var data=JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder,"inputs.json"))).RootElement;
            declarations=new FixtureDeclarations(b,Path.Combine(Folder,"declarations.json"));
            var preflight=new List<object>();var recipes=ReplayInputs.Preflight(data,declarations.Bindings,preflight);var recipe=recipes.Single(r=>r.Subscription.EntityId==14);var actual=File.ReadAllBytes(Path.Combine(Folder,"14.typed-legs.bin"));legs=ReadLegs(actual);Require(legs.Length==214&&EdgeAudit.Legs(legs).SequenceEqual(actual),"214 exact raw typed bytes");
            var reference=JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder,"query-reference.json"))).RootElement.GetProperty("queries").EnumerateArray().Single(r=>r.GetProperty("entity").GetInt32()==14);double cost=reference.GetProperty("cost").GetDouble();
            Require(EdgeAudit.Hash(actual)==reference.GetProperty("typedSHA256").GetString(),"recorded route SHA");Checks.Add(new{preflight,typedSHA256=EdgeAudit.Hash(actual),referenceQuery=reference.Clone(),newSearchWork=new{grants=0,G=0,C=0,BK=0},policyValue=89,neutralProviderBindingsExact=true});
            stage="pending-before-projection and exact local guards";PendingPolicy(b,recipe,legs,cost,false);PendingPolicy(b,recipe,legs,cost,true);
            if(args.Length>1&&args[1]=="boundaries"){
                stage="all capture/decode refusals and malformed-policy boundaries";compact=Birth(b,recipe,legs,cost,true);CodecBoundaries(b,compact);OriginalLegFailure(b,recipe,cost);report["status"]="PASS_REPLAY_CODEC_BOUNDARIES";return 0;
            }
            stage="actual214 component";legacy=Birth(b,recipe,legs,cost,false);compact=Birth(b,recipe,legs,cost,true);ColdNext(b,legacy,"legacy-prebirth-projection");ColdNext(b,compact,"compact-prebirth-projection");Run(b,legacy,false);Run(b,compact,true);Guards(b,compact);
            stage="paid fallback/refusal/cold";ResetRefusals(b,compact);Fallback(b,compact,1);Fallback(b,compact,2);Fallback(b,compact,3);
            stage="logical WorldWire prebirth/reset cold-next";SessionCold(true,false);SessionCold(false,false);SessionCold(false,true);
            Require(File.ReadAllBytes(Path.Combine(Folder,"legacy.ready-points.bin")).SequenceEqual(File.ReadAllBytes(Path.Combine(Folder,"compact.ready-points.bin"))),"full Ready bytes legacy equality");
            report["status"]="PASS_ACTUAL214_REPLAY_ONLY_COMPONENT";
        }catch(Exception e){report["status"]="FAIL_OR_NOTRUN";report["error"]=e.ToString();Environment.ExitCode=2;}
        finally{compact?.ReleaseStorage();legacy?.ReleaseStorage();declarations?.Dispose();if(legs!=null)foreach(var l in legs)l.ReleaseStorage();report["stage"]=stage;report["checks"]=Checks;report["ledger"]=new{b.PhysicalBytes,b.HeldBytes,b.ActiveScopes,b.PeakRetainedBytes};File.WriteAllText(Path.Combine(Folder,"result.json"),JsonSerializer.Serialize(report,Json));Require(b.PhysicalBytes==0&&b.HeldBytes==0&&b.ActiveScopes==0,"final zero ledger");}
        return Environment.ExitCode;
    }
}
