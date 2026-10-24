using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using Spacewars.Simulation;
using Spacewars.Runtime;
internal static class CowAudit {internal static long Comparisons;}
internal static class Program
{
    static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions{WriteIndented=true};
    static T Field<T>(object o,string key)=>(T)o.GetType().GetField(key,F).GetValue(o);
    static void Need(bool yes,string detail){if(!yes)throw new Exception(detail);}
    static string Hash(byte[] b)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)).ToLowerInvariant();
    static byte[] Bytes(Action<BinaryWriter> action){using var s=new MemoryStream();using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))action(w);return s.ToArray();}
    static object Entry(NavExactQueueEntry key)=>new{key.Node,Cost=Convert.ToHexString(Bytes(key.Cost.Write))};
    static bool Take(NavCowOrderedSet<NavExactQueueEntry> set,out NavExactQueueEntry minimum)
    {
#if CANDIDATE
        return set.TryTakeMin(out minimum);
#else
        minimum=default;var key=set.Min;if(!set.Remove(key))return false;minimum=key;return true;
#endif
    }
    sealed class Recorder:INavStorageReservation,INavStorageScratchAllocator,INavStorageLedgerIdentity,INavStorageLocalBirthAllocator
    {
        internal readonly NavigationRetentionBudget.Scope Scope;internal readonly List<long> Requests=new();internal int Refuse;internal Recorder(NavigationRetentionBudget.Scope s){Scope=s;}
        public bool TryReserve(long n){Requests.Add(n);return Requests.Count!=Refuse&&Scope.TryReserve(n);}
        public void Register(NavOwnedStorage root)=>((INavStorageAllocator)Scope).Register(root);
        public bool TryRegisterLocalBirth(NavOwnedStorage root)=>((INavStorageLocalBirthAllocator)Scope).TryRegisterLocalBirth(root);
        public IDisposable ClaimScratch(long n)=>Scope.ClaimScratch(n);public object Identity=>Scope.Identity;public bool Owns(NavOwnedStorage root)=>Scope.Owns(root);
    }
    static void Zero(NavigationRetentionBudget b)=>Need(b.PhysicalBytes==0&&b.HeldBytes==0&&b.ActiveScopes==0,"final ledger zero");
    static byte[] Physical(NavigationRetentionBudget b,NavOwnedStorage root)
    {
        Need(b.TryOpenScope(NavigationPreparationKind.Capture,1,1,out var s),"capture scope");using(s){NavigationCaptureBytes bytes=null;try{var r=s.OwnedReservation();bytes=NavigationCaptureBytes.Write(r,w=>NavPhysicalBodyWire.WriteTo(w,root,r));s.Attach(bytes);s.Commit(bytes,_=>{});return bytes.TransferPublic();}finally{bytes?.Dispose();}}
    }
    static T Decode<T>(NavigationRetentionBudget b,byte[] bytes)where T:NavOwnedStorage
    {Need(b.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var s),"decode scope");using(s){using var m=new MemoryStream(bytes,false);using var r=new BinaryReader(m);var root=(T)NavPhysicalBodyWire.ReadFrom(r,s.OwnedReservation());Need(m.Position==m.Length,"decode EOF");s.Attach(root);s.Commit(root,_=>{});return root;}}
    static NavCowDictionary<NavExactQueueEntry,bool>.Node Root(NavCowOrderedSet<NavExactQueueEntry> s)=>Field<NavCowDictionary<NavExactQueueEntry,bool>.Node>(Field<NavCowDictionary<NavExactQueueEntry,bool>>(s,"values"),"root");
    static Dictionary<int,object> Nodes(NavCowOrderedSet<NavExactQueueEntry> s){var d=new Dictionary<int,object>();void Walk(NavCowDictionary<NavExactQueueEntry,bool>.Node n){if(n==null)return;d.Add(n.Key.Node,n);Walk(n.Left);Walk(n.Right);}Walk(Root(s));return d;}
    static object Shape(NavCowOrderedSet<NavExactQueueEntry> s)
    {
        var x=s.SaveShape();Need(x.Keys!=null&&x.Left.Length==x.Keys.Length&&x.Right.Length==x.Keys.Length,"complete shape arrays");var dto=new{Root=x.Root,Keys=x.Keys.Select(Entry).ToArray(),Left=x.Left,Right=x.Right};using var doc=JsonDocument.Parse(JsonSerializer.Serialize(dto));Need(doc.RootElement.GetProperty("Keys").GetArrayLength()==s.Count&&doc.RootElement.EnumerateObject().Count()==4,"actual serialized shape fields, not empty object");return dto;
    }
    static object Container(string name,int[] keys,bool equalCost,int nth=0,bool frozen=false)
    {
        var b=new NavigationRetentionBudget();NavCowOrderedSet<NavExactQueueEntry> source=null,fork=null;object result=null;
        try{
            Need(b.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var birth),"container birth");using(birth){source=NavCowOrderedSet<NavExactQueueEntry>.Create(reserve:birth.TryReserve);foreach(int key in keys)Need(source.Add(new NavExactQueueEntry{Node=key,Cost=NavExactCost.FromDouble(equalCost?1:key)}),"insert");source.Seal();birth.Attach(source);birth.Commit(source,_=>{});}
            var pinned=Physical(b,source);var sourceShape=JsonSerializer.Serialize(Shape(source));var oldNodes=Nodes(source);long originalCharge=b.PhysicalBytes;var oldRoot=Root(source);
            Need(b.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope),"container mutation scope");using(scope){var reserve=new Recorder(scope);fork=source.ForkPrepared(reserve.TryReserve);if(frozen)fork.Seal();reserve.Requests.Clear();reserve.Refuse=nth;var forkRoot=Root(fork);NavExactQueueEntry extracted=new NavExactQueueEntry{Node=-999};bool success=false;string error=null;CowAudit.Comparisons=0;
                try{success=Take(fork,out extracted);}catch(InvalidOperationException e){error=e.GetType().Name+":"+e.Message;}long comparisons=CowAudit.Comparisons;
                if(error!=null){Need(extracted.Node==0&&extracted.Cost==default,"out default on refusal/frozen");Need(ReferenceEquals(Root(fork),forkRoot),"failed mutation preserves installed root");}
                if(nth>0)Need(error!=null&&reserve.Requests.Count==nth,"exact refused ordinal");
                if(frozen)Need(error!=null&&reserve.Requests.Count==0,"frozen checked before empty/allocations");
                if(keys.Length==0&&!frozen)Need(!success&&error==null&&reserve.Requests.Count==0&&extracted.Node==0,"mutable empty false/default/no allocation");
                Need(ReferenceEquals(Root(source),oldRoot)&&sourceShape==JsonSerializer.Serialize(Shape(source)),"source COW pin/root unchanged");
                fork.Seal();var afterNodes=Nodes(fork);var aliases=afterNodes.Where(p=>oldNodes.TryGetValue(p.Key,out var n)&&ReferenceEquals(n,p.Value)).Select(p=>p.Key).OrderBy(x=>x).ToArray();
                var bytes=Physical(b,fork);Need(pinned.SequenceEqual(Physical(b,source)),"pinned physical bytes unchanged");
                result=new{name,nth,frozen,success,error,extracted=Entry(extracted),Comparisons=comparisons,requests=reserve.Requests.ToArray(),shape=Shape(fork),physical=Convert.ToBase64String(bytes),sourcePhysical=Hash(pinned),charge=b.PhysicalBytes,originalCharge,aliases};
                fork.ReleaseStorage();fork=null;scope.Discard();Need(b.PhysicalBytes==originalCharge&&b.HeldBytes==0,"mutation disposal leaves exact original charge");
            }
        }finally{fork?.ReleaseStorage();source?.ReleaseStorage();Zero(b);}return result;
    }
    static void Canonical(BinaryWriter w,object value)
    {
        if(value==null){w.Write(false);return;}w.Write(true);var t=value.GetType();w.Write(t.FullName);
        if(value is string text){w.Write(text);return;}if(t.IsEnum){w.Write(Convert.ToInt64(value));return;}
        if(value is bool b){w.Write(b);return;}if(value is int i){w.Write(i);return;}if(value is long l){w.Write(l);return;}if(value is double d){w.Write(d);return;}if(value is byte by){w.Write(by);return;}if(value is ulong ul){w.Write(ul);return;}
        if(value is NavExactCost exact){exact.Write(w);return;}if(value is NavLocation at){NavContract.Location(w,at);return;}if(value is NavPoint point){NavContract.Point(w,point);return;}if(value is NavScheduledRequest request){w.Write(request.ArtifactKey);return;}
        if(value is NavTypedLeg leg){NavContract.Location(w,leg.From);NavContract.Location(w,leg.To);w.Write(leg.Cost);w.Write((int)leg.Kind);w.Write(leg.PortalId??"");return;}
        if(value is IEnumerable sequence){var rows=sequence.Cast<object>().ToArray();w.Write(rows.Length);foreach(var row in rows)Canonical(w,row);return;}
        var fields=t.GetFields(F).Where(f=>!f.IsStatic).OrderBy(f=>f.Name,StringComparer.Ordinal).ToArray();Need(fields.Length>0,"nonempty logical projection "+t.FullName);w.Write(fields.Length);foreach(var field in fields){w.Write(field.Name);Canonical(w,field.GetValue(value));}
    }
    static byte[] Logical(LayeredNavigationScheduler s)=>Bytes(w=>Canonical(w,s.Save()));
    static NavDemandQueryWork Demand(LayeredNavigationScheduler s)
    {var jobs=Field<NavPersistentQueue<LayeredNavigationScheduler.Job>>(s,"jobs");return jobs.First?.Demand;}
    static bool At(LayeredNavigationScheduler s,string target)
    {
        var w=Demand(s);if(w==null||w.Status!=NavSolveStatus.Pending)return false;string phase=Field<object>(w,"phase").ToString();var frontier=Field<NavCowOrderedSet<NavExactQueueEntry>>(w,"exactFrontier");int chosen=Field<int>(w,"chosen");bool stop=frontier.Count==0||chosen>=0&&frontier.Min.Cost>Field<NavExactCost>(w,"exactBest");
        if(target=="terminalcost")return phase=="Candidates"&&Field<int>(w,"terminalBlocker")==-2;
        var q=Field<NavScheduledRequest>(w,"request");bool normalized=phase=="Edges"&&!Field<bool>(w,"pending")&&(q.LazyPartition?Field<int>(w,"lazyMask")==0:Field<int>(w,"direction")==4);
        if(target=="stop")return (phase=="Pop"||normalized)&&chosen>=0&&frontier.Count>0&&stop;
        if(stop||frontier.Count==0)return false;if(target=="direct")return phase=="Pop";
        return normalized;
    }
    static LayeredNavigationScheduler Step(NavigationRetentionBudget b,LayeredNavigationScheduler source,long epoch,out int used)
    {Need(b.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var scope),"one-step scope");using(scope){var next=source.ForkOwned(scope.OwnedReservation());try{used=next.Advance(epoch,new NavEpochQuota(256,256,256,256,256),_=>true,1);Need(!next.RetentionBlocked,"one-step retention");next.SealPrepared();scope.Attach(next);scope.Commit(next,_=>{});return next;}catch{next.ReleaseStorage();throw;}}}
    static LayeredNavigationScheduler Birth(NavigationRetentionBudget b,string mode,out NavScheduledRequest raw)
    {
        if(mode=="terminalcost"){
            var terminalMap=new CampaignOpeningMap();var terminalObstacles=new[]{new NavObstacle(-240,-240,-80,240),new NavObstacle(110,-200,130,50)};var terminalProvider=new PartitionNavigationProvider(terminalMap,new NavGeometry(terminalMap.HalfExtent,terminalObstacles,terminalMap.Revision),null);var terminalClearance=new NavClearanceProfile("ground",1,"ground",1,.25);Need(terminalProvider.TryLocate(new NavPoint(80,-160),terminalClearance,out var terminalFrom),"terminal-cost fixture origin");Need(terminalProvider.TryLocate(new NavPoint(160,160),terminalClearance,out var terminalTo),"terminal-cost fixture endpoint");var terminalRegion=new NavTerminalRegion("certified-target",1,terminalTo,new[]{terminalTo,new NavLocation(new NavPoint(160.125,160),terminalTo.SurfaceId)});raw=new NavScheduledRequest(terminalProvider,terminalClearance,new NavGraphProfile(new string('b',64),"distance",1,80),new NavGraphCompileLimits(49,28,196),terminalRegion,new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,terminalFrom,terminalTo,null),49,query:true,demandQuery:true,lazyPartition:true,certifiedLazyPartition:true,rectangularOrigins:true,terminalRectangleProof:true,terminalCornerProof:true,originShadowProof:true,terminalCostProof:true);
            Need(b.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var terminalScope),"terminal-cost scheduler birth");using(terminalScope){var p=PartitionNavigationProvider.CopyOwned(terminalProvider,terminalScope.OwnedReservation());var q=NavScheduledRequest.CopyOwned(raw,p,terminalScope.OwnedReservation());var scheduler=LayeredNavigationScheduler.CreateOwned(terminalScope.OwnedReservation());try{Need(scheduler.Submit(q),"terminal-cost submit");scheduler.SealPrepared();terminalScope.Attach(scheduler);terminalScope.Commit(scheduler,_=>{});return scheduler;}catch{scheduler.ReleaseStorage();throw;}finally{p.StorageRoot.ReleaseOwner();q.StorageRoot.ReleaseOwner();}}
        }
        var map=ThreeCrossingsMap.Default;var provider=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.Concat(new[]{new NavObstacle(43,34.5,45,52)}).ToArray(),map.Revision),null);var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);Need(provider.TryLocate(new NavPoint(40,40),clearance,out var from),"existing detour authored origin");Need(provider.TryLocate(new NavPoint(48,40),clearance,out var to),"existing detour authored endpoint");
        var region=new NavTerminalRegion("target-detour",1,to,new[]{to,new NavLocation(new NavPoint(48.125,40),to.SurfaceId)});raw=new NavScheduledRequest(provider,clearance,new NavGraphProfile(new string('b',64),"distance",1,8),new NavGraphCompileLimits(2000,2000,10000),region,new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,from,to,null),1024,query:true,demandQuery:true,demandQueryV2:true,demandQueryV3:true,demandQueryV5:true,demandQueryV6:mode!="V5",lazyPartition:mode=="lazy");
        Need(b.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var scope),"scheduler birth");using(scope){var p=PartitionNavigationProvider.CopyOwned(provider,scope.OwnedReservation());var q=NavScheduledRequest.CopyOwned(raw,p,scope.OwnedReservation());var s=LayeredNavigationScheduler.CreateOwned(scope.OwnedReservation());try{Need(s.Submit(q),"detour submit");s.SealPrepared();scope.Attach(s);scope.Commit(s,_=>{});return s;}catch{s.ReleaseStorage();throw;}finally{p.StorageRoot.ReleaseOwner();q.StorageRoot.ReleaseOwner();}}
    }
    static object QueryPoint(string folder,string mode,string target,NavigationRetentionBudget hot,LayeredNavigationScheduler prefix,NavScheduledRequest raw,long epoch)
    {
        var cold=new NavigationRetentionBudget();LayeredNavigationScheduler restored=null,nextHot=null,nextCold=null,logical=null;var sourceBytes=Physical(hot,prefix);var sourceLogical=Logical(prefix);var before=Demand(prefix);var frontier=Field<NavCowOrderedSet<NavExactQueueEntry>>(before,"exactFrontier");var minimum=frontier.Count>0?frontier.Min:default;int count=frontier.Count;long sourceCharge=hot.PhysicalBytes;
        File.WriteAllBytes(Path.Combine(folder,mode+"-"+target+"-before.bin"),sourceBytes);
        try{
            restored=Decode<LayeredNavigationScheduler>(cold,sourceBytes);Need(sourceBytes.SequenceEqual(Physical(cold,restored)),"physical immediate bytes");Need(sourceCharge==cold.PhysicalBytes,"physical immediate charge");
            CowAudit.Comparisons=0;int steps=1,used=0;var hotStep=prefix;for(int i=0;i<steps;i++){var next=Step(hot,hotStep,epoch,out int paid);Need(paid==1,"selected actual paid one action");used+=paid;if(!ReferenceEquals(hotStep,prefix))hotStep.ReleaseStorage();hotStep=next;}nextHot=hotStep;long comparisons=CowAudit.Comparisons;var after=Demand(nextHot);var afterFrontier=Field<NavCowOrderedSet<NavExactQueueEntry>>(after,"exactFrontier");
            Need(nextHot.Counters.ConnectorWork==prefix.Counters.ConnectorWork+steps,"exact Connector paid increment");
            if(target=="terminalcost"){Need(afterFrontier.Count==count&&Field<int>(after,"current")==Field<int>(before,"current"),"TerminalCost candidate step preserves selected Pop/frontier");Need(Field<int>(after,"terminalBlocker")==-1,"TerminalCostRejects gate consumed after ResetTerminalRectangle");}else if(target!="stop"){Need(afterFrontier.Count==count-1&&Field<int>(after,"current")==minimum.Node,"actual selected step extracted minimum");}else{Need(count>0&&minimum.Cost>Field<NavExactCost>(before,"exactBest"),"incumbent stop actually reads nonempty minimum");Need(afterFrontier.Count==count&&ReferenceEquals(Root(frontier),Root(afterFrontier)),"incumbent stop preserves exact frontier root");}
            var coldStep=restored;for(int i=0;i<steps;i++){var next=Step(cold,coldStep,epoch,out int coldUsed);Need(coldUsed==1,"cold exact one-step quota");if(!ReferenceEquals(coldStep,restored))coldStep.ReleaseStorage();coldStep=next;}nextCold=coldStep;var expected=Physical(hot,nextHot);Need(expected.SequenceEqual(Physical(cold,nextCold)),"physical cold next full bytes");Need(hot.PhysicalBytes==cold.PhysicalBytes,"physical hot/cold retained charge with pins");Need(sourceBytes.SequenceEqual(Physical(hot,prefix)),"hot source pin bytes");
            var saved=prefix.Save();var canonical=((PartitionNavigationProvider)raw.Provider).DetachedSnapshot();var canonicalRaw=NavDemandQueryWork.DetachSharedRecipe(raw,canonical);Need(canonical.Binding==raw.Provider.Binding&&canonical.EstimatedRetainedBytes==saved.ProviderBytes,"logical exact canonical provider binding/charge");logical=LayeredNavigationScheduler.Restore(saved,new[]{canonicalRaw},new[]{canonical});Need(sourceLogical.SequenceEqual(Logical(logical)),"logical immediate complete checkpoint bytes");int logicalUsed=0;for(int i=0;i<steps;i++)logicalUsed+=logical.Advance(epoch,new NavEpochQuota(256,256,256,256,256),_=>true,1);Need(logicalUsed==used&&Logical(nextHot).SequenceEqual(Logical(logical)),"logical cold next complete checkpoint/counters/quota");
            File.WriteAllBytes(Path.Combine(folder,mode+"-"+target+"-after.bin"),expected);return new{mode,target,Comparisons=comparisons,beforeSHA=Hash(sourceBytes),afterSHA=Hash(expected),logicalBeforeSHA=Hash(sourceLogical),logicalAfterSHA=Hash(Logical(nextHot)),used,epoch,chosen=Field<int>(after,"current"),frontierBefore=count,frontierAfter=afterFrontier.Count,charge=hot.PhysicalBytes,sourceCharge,allColdChecks=true};
        }finally{logical?.ReleaseStorage();nextCold?.ReleaseStorage();restored?.ReleaseStorage();nextHot?.ReleaseStorage();Zero(cold);Need(hot.PhysicalBytes==sourceCharge&&hot.HeldBytes==0&&hot.ActiveScopes==0,"query pin charge after cleanup");}
    }
    static int Main(string[] args)
    {
        var containers=new List<object>();var queries=new List<object>();var report=new Dictionary<string,object>();try{
            var cases=new[]{("empty",Array.Empty<int>()),("singleton",new[]{1}),("min-right",new[]{1,2}),("no-rotation",new[]{4,2,6,1,3,5,7}),("single-rotation",new[]{2,1,4,3,5}),("double-rotation",new[]{2,1,4,3}),("equal-cost-node",new[]{9,2,5})};
            foreach(var item in cases){var result=Container(item.Item1,item.Item2,item.Item1=="equal-cost-node");containers.Add(result);if(item.Item1.Contains("rotation")&&item.Item1!="no-rotation"){using var doc=JsonDocument.Parse(JsonSerializer.Serialize(result));int n=doc.RootElement.GetProperty("requests").GetArrayLength();Need(n>0,"rotation has allocation/refusal points");for(int ordinal=1;ordinal<=n;ordinal++)containers.Add(Container(item.Item1,item.Item2,false,ordinal));}}
            containers.Add(Container("frozen-empty",Array.Empty<int>(),false,frozen:true));containers.Add(Container("frozen-nonempty",new[]{1},false,frozen:true));
            foreach(string mode in new[]{"V5","V6","lazy","terminalcost"}){var b=new NavigationRetentionBudget();LayeredNavigationScheduler root=null;long epoch=0;try{root=Birth(b,mode,out var raw);foreach(string target in (mode=="terminalcost"?new[]{"terminalcost"}:new[]{"direct","edges","stop"})){for(int n=0;!At(root,target);n++){Need(n<4096*256,"unchanged bounded prefix steps");Need(root.ReadyCount==0,"fixture missed prePop target "+mode+"/"+target);var next=Step(b,root,epoch,out int used);root.ReleaseStorage();root=next;if(used==0)epoch++;}queries.Add(QueryPoint(args[0],mode,target,b,root,raw,epoch));}}finally{root?.ReleaseStorage();Zero(b);}}
            report["classification"]="PASS";
        }catch(Exception e){report["classification"]="FAIL_OR_NOTRUN";report["error"]=e.ToString();Environment.ExitCode=2;}
        report["containers"]=containers;report["queries"]=queries;File.WriteAllText(Path.Combine(args[0],"result.json"),JsonSerializer.Serialize(report,Json));return Environment.ExitCode;
    }
}
