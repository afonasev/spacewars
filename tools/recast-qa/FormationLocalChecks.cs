using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
internal static class FormationLocalChecks
{
    static void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
    static NavLocation Location(JToken value)=>new NavLocation(new NavPoint((double)value["Position"]["X"],(double)value["Position"]["Z"]),(string)value["SurfaceId"]);
    static NavigationRetentionBudget.Scope Open(NavigationRetentionBudget budget){Check(budget.TryOpenScope(NavigationPreparationKind.Solve,805,0,out var scope),"local component scope refused");return scope;}
    static bool Whole(PartitionNavigationProvider provider,NavGeometry geometry,double radius,NavLocation from,NavLocation to,INavStorageReservation reservation)
    {
        System.Collections.Generic.IReadOnlyList<NavTypedLeg> trace=null;
        try{return geometry.SegmentFree(from.Position,to.Position,radius)&&provider.TryTraceOwned(from,to.Position,new NavClearanceProfile("ground",1,"ground",0,radius),out var reached,out trace,reservation)&&reached.Equals(to);}
        finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
    }
    static RecastLocalAttachment.Result Drain(RecastLocalAttachment helper,PartitionNavigationProvider provider,NavGeometry geometry,NavigationProfile profile,NavigationRetentionBudget budget)
    {
        long before=helper.Work,bound=checked(2L*helper.NodeCapacity*helper.NodeCapacity+32L*helper.NodeCapacity);
        while(helper.Status==RecastLocalAttachment.Result.Pending){
            var scope=Open(budget);
            try{long work=helper.Work;helper.Step(provider,geometry,profile,scope.OwnedReservation(),256);Check(helper.Work-work<=256,"local work quantum exceeded");}
            finally{scope.Discard();}
            Check(helper.Work-before<=bound,"finite local lattice work bound exceeded");
        }
        return helper.Status;
    }
    internal static void Run()
    {
        var rows=File.ReadLines(".local/recast-formation/attachment-diagnostic1.log").Select(line=>{try{return JObject.Parse(line);}catch{return null;}}).Where(x=>x!=null).ToArray();
        var context=rows.Single(x=>(string)x["Case"]=="actual-paid-context"&&(int)x["Count"]==49);var spine=context["Spine"];
        var capture=File.ReadLines(".local/recast-formation/member24-connectivity1.log").Select(line=>{try{return JObject.Parse(line);}catch{return null;}}).First(x=>x!=null&&(string)x["Case"]=="exact-paid-member-input-captured");
        var bytes=File.ReadAllBytes(".local/recast-formation/member24-geometry.bin");Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()==(string)capture["GeometrySHA"],"copied actual geometry changed");
        var dto=WorldWire.Unpack(bytes,WorldWire.ReadNavigationGeometryState);var geometry=new NavGeometry(dto.HalfExtent,dto.Obstacles,dto.Revision);
        var profile=NavigationProfile.FromWireValues((string)capture["ProfileId"],(int)capture["ProfileRevision"],capture["ProfileValues"].ToObject<double[]>());
        var raw=new PartitionNavigationProvider(PlayableProfile.RecastGroupCorridorDefault.AuthoredMap,geometry,(string)capture["HeldIdentity"]);
        var budget=new NavigationRetentionBudget();PartitionNavigationProvider provider=null;RecastLocalAttachment helper=null;ulong native=RecastNative.sw_recast_allocation_count();
        try{
            var create=Open(budget);try{var r=create.OwnedReservation();provider=PartitionNavigationProvider.CopyOwned(raw,r);helper=RecastLocalAttachment.Create(profile,((INavStorageFacade)provider).StorageRoot,r);}finally{create.Discard();}
            Check(helper.NodeCapacity==1369&&profile.LocalGridCell==.2&&profile.LocalDetourDistance==3.6,"existing local profile/bound changed");
            NavLocation Node(int n)=>n==0?Location(spine["Legs"][0]["From"]):Location(spine["Legs"][n-1]["To"]);
            foreach(var selected in new[]{(Entity:24,Reverse:false),(Entity:21,Reverse:true)}){
                var member=context["Members"].Single(m=>(int)m["Entity"]==selected.Entity);var start=Location(member["StartLocation"]);var goal=Location(member["GoalLocation"]);int fan=(int)spine["Fan"];bool found=false;int attempts=0;
                foreach(int stop in spine["Stops"].ToObject<int[]>().Where(n=>selected.Reverse?n>=fan:n<=fan)){
                    var root=selected.Reverse?goal:start;var target=Node(stop);if(root.SurfaceId!=target.SurfaceId)continue;attempts++;
                    var begin=Open(budget);try{Check(!Whole(provider,geometry,profile.Radius,selected.Reverse?target:root,selected.Reverse?root:target,begin.OwnedReservation()),"recorded failed connector became straight-clear");helper.Begin(root,target,selected.Reverse,begin.OwnedReservation());}finally{begin.Discard();}
                    var result=Drain(helper,provider,geometry,profile,budget);
                    Console.WriteLine("LOCAL attempt member="+selected.Entity+" reverse="+selected.Reverse+" stop="+stop+" result="+result+" work="+helper.Work+" traces="+helper.Traces);
                    if(result!=RecastLocalAttachment.Result.Succeeded)continue;
                    var route=Enumerable.Range(0,helper.Count).Select(helper.Location).ToArray();Check(route.Length>1,"failed direct connector has no explicit bend");
                    var verify=Open(budget);try{var at=selected.Reverse?target:root;foreach(var point in route){Check(Whole(provider,geometry,profile.Radius,at,point,verify.OwnedReservation()),"local route has unsafe/incorrect-direction edge");at=point;}Check(at.Equals(selected.Reverse?root:target),"local connector does not end exactly");}finally{verify.Discard();}
                    Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="actual-failed-local-connector",selected.Entity,selected.Reverse,Stop=stop,Attempts=attempts,Route=route,helper.Work,helper.Traces,helper.Expansions,helper.NodeCapacity,Result="PASS"}));found=true;break;
                }
                Check(found,"existing3.6 bound cannot complete actual member"+selected.Entity+"; STOP, do not enlarge bound");
            }
            // A blocked endpoint cannot become a best-partial Ready connector.
            var actualStart=Location(context["Members"].Single(m=>(int)m["Entity"]==24)["StartLocation"]);
            var blocked=geometry.Obstacles.Select(o=>new NavPoint((o.MinX+o.MaxX)/2,(o.MinZ+o.MaxZ)/2)).First(p=>!geometry.IsFree(p,profile.Radius));
            var unsafeBegin=Open(budget);try{helper.Begin(actualStart,new NavLocation(blocked,actualStart.SurfaceId),false,unsafeBegin.OwnedReservation());}finally{unsafeBegin.Discard();}
            Check(Drain(helper,provider,geometry,profile,budget)==RecastLocalAttachment.Result.Failed&&helper.Count==0,"blocked endpoint promoted best partial to Ready");Console.WriteLine("LOCAL unsafe endpoint remains Failed/no partial route PASS");
            var cancel=Open(budget);try{helper.Begin(actualStart,Location(spine["Legs"][0]["From"]),false,cancel.OwnedReservation());bool stopped=false;try{helper.Step(provider,geometry,profile,cancel.OwnedReservation(),256,()=>true);}catch(OperationCanceledException){stopped=true;}Check(stopped&&helper.Status!=RecastLocalAttachment.Result.Succeeded,"local cancellation ignored");}finally{cancel.Discard();}Console.WriteLine("LOCAL cancellation before work PASS");
            var denied=Open(budget);budget.AllocationAdmission=_=>false;bool refused=false;
            try{RecastLocalAttachment unexpected=null;try{unexpected=RecastLocalAttachment.Create(profile,((INavStorageFacade)provider).StorageRoot,denied.OwnedReservation());}catch(InvalidOperationException){refused=true;}finally{unexpected?.Dispose();}}
            finally{budget.AllocationAdmission=null;denied.Discard();}
            Check(refused,"local workspace admission denial ignored");Console.WriteLine("LOCAL workspace real allocation refusal PASS");
        }finally{helper?.Dispose();((INavStorageFacade)provider)?.StorageRoot.Release();Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Case="local-components-final-census",budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes,NativeBefore=native,NativeAfter=RecastNative.sw_recast_allocation_count()}));Check(budget.PhysicalBytes==0&&budget.HeldBytes==0&&budget.ActiveScopes==0&&RecastNative.sw_recast_allocation_count()==native,"local component owner leak");}
    }
}
