using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
internal static class FormationSharedChecks
{
    static void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
    static NavLocation Location(JToken v)=>new NavLocation(Point(v["Position"]),(string)v["SurfaceId"]);
    static NavPoint Point(JToken v)=>new NavPoint((double)v["X"],(double)v["Z"]);
    static NavigationRetentionBudget.Scope Open(NavigationRetentionBudget b){Check(b.TryOpenScope(NavigationPreparationKind.Solve,805,0,out var s),"shared field scope refused");return s;}
    static void Drain(RecastAttachmentField field,PartitionNavigationProvider provider,NavGeometry geometry,double radius,NavigationRetentionBudget budget)
    {
        long start=field.Work,bound=checked(512L*field.NodeCapacity);
        while(field.Status==RecastAttachmentField.Result.Pending||field.RoutePending){
            var scope=Open(budget);try{long before=field.Work;field.Step(provider,geometry,radius,scope.OwnedReservation(),256);Check(field.Work-before<=256,"shared work quantum exceeded");}finally{scope.Discard();}
            Check(field.Work-start<=bound,"shared finite work bound exceeded");
        }
    }
    static bool Whole(PartitionNavigationProvider provider,NavGeometry geometry,double radius,NavLocation a,NavLocation b,INavStorageReservation r)
    {
        System.Collections.Generic.IReadOnlyList<NavTypedLeg> trace=null;
        try{return geometry.SegmentFree(a.Position,b.Position,radius)&&provider.TryTraceOwned(a,b.Position,new NavClearanceProfile("ground",1,"ground",0,radius),out var reached,out trace,r)&&reached.Equals(b);}
        finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
    }
    static void VerifyDenial(PartitionNavigationProvider provider,NavigationRetentionBudget budget,NavigationProfile profile,double radius,int seeds)
    {
        var scope=Open(budget);var owner=((INavStorageFacade)provider).StorageRoot;
        long physical=budget.PhysicalBytes,held=budget.HeldBytes,bytes=0;int references=owner.References,calls=0;bool refused=false;RecastAttachmentField unexpected=null;
        budget.AllocationAdmission=value=>{calls++;bytes=value;return false;};
        try{
            try{unexpected=RecastAttachmentField.Create(profile.LocalGridCell,radius,50,seeds,owner,scope.OwnedReservation());}
            catch(InvalidOperationException e) when(e.GetType()==typeof(InvalidOperationException)&&e.Message=="Physical storage exceeds prepared reservation."){refused=true;}
            Check(refused&&unexpected==null&&calls==1&&bytes==6297816,"exact capacity refusal/predicate boundary mismatch");
            Check(budget.PhysicalBytes==physical&&budget.HeldBytes==held&&owner.References==references,"denied field produced owner/credit/name-reference delta");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="shared-denial-only",Result="PASS",Calls=calls,Bytes=bytes,PhysicalBefore=physical,PhysicalAfter=budget.PhysicalBytes,HeldBefore=held,HeldAfter=budget.HeldBytes,ReferencesBefore=references,ReferencesAfter=owner.References,ExceptionType=typeof(InvalidOperationException).FullName,ExceptionMessage="Physical storage exceeds prepared reservation."}));
        }finally{unexpected?.Dispose();budget.AllocationAdmission=null;scope.Discard();}
    }
    internal static void Run(bool denialOnly=false)
    {
        var rows=File.ReadLines(".local/recast-formation/attachment-diagnostic1.log").Select(line=>{try{return JObject.Parse(line);}catch{return null;}}).Where(x=>x!=null).ToArray();
        var context=rows.Single(x=>(string)x["Case"]=="actual-paid-context"&&(int)x["Count"]==49);var spine=context["Spine"];
        var capture=File.ReadLines(".local/recast-formation/member24-connectivity1.log").Select(line=>{try{return JObject.Parse(line);}catch{return null;}}).First(x=>x!=null&&(string)x["Case"]=="exact-paid-member-input-captured");
        var bytes=File.ReadAllBytes(".local/recast-formation/member24-geometry.bin");Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()==(string)capture["GeometrySHA"],"exact geometry changed");
        var dto=WorldWire.Unpack(bytes,WorldWire.ReadNavigationGeometryState);var geometry=new NavGeometry(dto.HalfExtent,dto.Obstacles,dto.Revision);
        var profile=NavigationProfile.FromWireValues((string)capture["ProfileId"],(int)capture["ProfileRevision"],capture["ProfileValues"].ToObject<double[]>());
        var raw=new PartitionNavigationProvider(PlayableProfile.RecastGroupCorridorDefault.AuthoredMap,geometry,(string)capture["HeldIdentity"]);
        var all=context["Members"].ToArray();var origins=all.Select(m=>Location(m["StartLocation"])).ToArray();var center=new NavPoint(origins.Average(p=>p.Position.X),origins.Average(p=>p.Position.Z));
        double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
        double originRadius=origins.Max(p=>Distance(p.Position,center)+profile.Radius),fanRadius=(double)spine["FanRadius"];var fanCenter=Point(spine["FanCenter"]);
        NavLocation Node(int n)=>n==0?Location(spine["Legs"][0]["From"]):Location(spine["Legs"][n-1]["To"]);
        var stops=spine["Stops"].ToObject<int[]>();var budget=new NavigationRetentionBudget();PartitionNavigationProvider provider=null;RecastAttachmentField field=null;ulong native=RecastNative.sw_recast_allocation_count();
        try{
            var birth=Open(budget);try{var r=birth.OwnedReservation();provider=PartitionNavigationProvider.CopyOwned(raw,r);if(!denialOnly)field=RecastAttachmentField.Create(profile.LocalGridCell,Math.Max(originRadius,fanRadius),50,stops.Length,((INavStorageFacade)provider).StorageRoot,r);}finally{birth.Discard();}
            if(denialOnly){VerifyDenial(provider,budget,profile,originRadius,stops.Length);return;}
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="shared-field-layout",center,originRadius,fanCenter,fanRadius,field.NodeCapacity,field.PaidLayout,ProviderBytes=budget.PhysicalBytes-field.PaidLayout}));
            Check(field.NodeCapacity==54289&&field.PaidLayout==6952944&&profile.LocalDetourDistance==3.6,"frozen paid layout/profile mismatch");
            long providerBytes=budget.PhysicalBytes-field.PaidLayout;
            foreach(bool origin in new[]{false,true}){
                var failed=rows.Where(x=>(string)x["Case"]=="actual-null-attachment-trace"&&(string)x["OriginalFailure"]==(origin?"noOriginJoin":"noTerminalExit")).ToArray();
                Check(failed.Length==(origin?7:9),"actual failed membership changed");
                var seeds=stops.Where(n=>origin?n<=(int)spine["Fan"]:n>=(int)spine["Fan"]).Where(n=>Node(n).SurfaceId==(origin?"bank/east":"bank/west")).ToArray();
                var begin=Open(budget);try{field.Begin(origin?center:fanCenter,origin?originRadius:fanRadius,origin?"bank/east":"bank/west",origin,failed.Length,seeds.Length,begin.OwnedReservation());for(int i=0;i<failed.Length;i++)field.SetTarget(i,Location(failed[i][origin?"StartLocation":"GoalLocation"]));for(int i=0;i<seeds.Length;i++)field.SetSeed(i,seeds[i],Node(seeds[i]));}finally{begin.Discard();}
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="shared-side-name-overlap",Origin=origin,budget.PhysicalBytes,SurfaceCharge=budget.PhysicalBytes-providerBytes-field.PaidLayout}));
                long work=field.Work,traces=field.Traces,expansions=field.Expansions;var clock=System.Diagnostics.Stopwatch.StartNew();Drain(field,provider,geometry,profile.Radius,budget);
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="shared-field-solve",Origin=origin,Result=field.Status.ToString(),Resolved=Enumerable.Range(0,failed.Length).Where(field.Resolved).Select(i=>(int)failed[i]["Entity"]).ToArray(),Missing=Enumerable.Range(0,failed.Length).Where(i=>!field.Resolved(i)).Select(i=>(int)failed[i]["Entity"]).ToArray(),Work=field.Work-work,Traces=field.Traces-traces,Expansions=field.Expansions-expansions,Milliseconds=clock.Elapsed.TotalMilliseconds,budget.PeakRetainedBytes}));
                Check(field.Status==RecastAttachmentField.Result.Succeeded,"shared geometry-derived "+(origin?"origin":"terminal")+" field incomplete; STOP no envelope growth");
                for(int i=0;i<failed.Length;i++){
                    field.BeginRoute(i);Drain(field,provider,geometry,profile.Radius,budget);var route=Enumerable.Range(0,field.Count).Select(field.Location).ToArray();int stop=field.SourceStop(i);var endpoint=Location(failed[i][origin?"StartLocation":"GoalLocation"]);
                    Check(route.Length>2&&route[0].Equals(origin?endpoint:Node(stop))&&route[^1].Equals(origin?Node(stop):endpoint),"sourceStop/exact endpoint changed");
                    var verify=Open(budget);try{for(int j=1;j<route.Length;j++)Check(Whole(provider,geometry,profile.Radius,route[j-1],route[j],verify.OwnedReservation()),"unsafe/wrong-direction shared branch");}finally{verify.Discard();}
                    Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="shared-explicit-branch",Origin=origin,Entity=(int)failed[i]["Entity"],Stop=stop,Route=route,Result="PASS"}));
                }
            }
            // An obstacle-interior endpoint must not receive a best partial route.
            var unsafeBegin=Open(budget);try{field.Begin(fanCenter,fanRadius,"bank/west",false,1,1,unsafeBegin.OwnedReservation());field.SetTarget(0,new NavLocation(new NavPoint(-28,26),"bank/west"));field.SetSeed(0,(int)spine["Fan"],Node((int)spine["Fan"]));}finally{unsafeBegin.Discard();}
            Check(!geometry.IsFree(new NavPoint(-28,26),profile.Radius),"unsafe control is not blocked");Drain(field,provider,geometry,profile.Radius,budget);Check(field.Status==RecastAttachmentField.Result.Failed&&!field.Resolved(0),"blocked target received partial Ready");Console.WriteLine("SHARED unsafe/no partial PASS");
            var cancel=Open(budget);try{field.Begin(center,originRadius,"bank/east",true,1,1,cancel.OwnedReservation());field.SetTarget(0,origins[0]);field.SetSeed(0,0,Node(0));bool reuseRejected=false;try{field.Begin(center,originRadius,"bank/east",true,1,1,cancel.OwnedReservation());}catch(ArgumentException){reuseRejected=true;}Check(reuseRejected,"unfinished field reuse accepted");bool stopped=false;try{field.Step(provider,geometry,profile.Radius,cancel.OwnedReservation(),256,()=>true);}catch(OperationCanceledException){stopped=true;}Check(stopped&&field.Status==RecastAttachmentField.Result.Pending,"cancel ignored");}finally{cancel.Discard();}Console.WriteLine("SHARED cancel PASS");
            VerifyDenial(provider,budget,profile,originRadius,stops.Length);
        }finally{
            field?.Dispose();((INavStorageFacade)provider)?.StorageRoot.Release();Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="shared-field-final-census",budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes,budget.PeakRetainedBytes,NativeBefore=native,NativeAfter=RecastNative.sw_recast_allocation_count()}));
            Check(budget.PhysicalBytes==0&&budget.HeldBytes==0&&budget.ActiveScopes==0&&RecastNative.sw_recast_allocation_count()==native,"shared field cleanup leak");
        }
    }
}
