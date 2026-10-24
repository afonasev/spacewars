using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Reflection;using System.Text.Json;using System.Security.Cryptography;using Spacewars.Runtime;using Spacewars.Simulation;
internal static class EdgeAudit {
internal static T Field<T>(object o,string n)=>(T)o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(o);
internal static byte[] Bytes(Action<BinaryWriter> action){using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))action(w);return m.ToArray();}
internal static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
internal static byte[] Legs(IReadOnlyList<NavTypedLeg> legs)=>Bytes(w=>{w.Write(legs.Count);foreach(var l in legs){NavContract.Location(w,l.From);NavContract.Location(w,l.To);w.Write((int)l.Kind);w.Write(l.Cost);w.Write(l.PortalId!=null);if(l.PortalId!=null)w.Write(l.PortalId);}});
}
internal sealed class FixtureDeclarations:IDisposable {
readonly List<NavOwnedStorage> owners=new();internal readonly List<NavArrivalWorkingBinding> Bindings=new();
static double N(JsonElement x,string key){var v=x.GetProperty(key);return v.ValueKind==JsonValueKind.String?double.Parse(v.GetString(),System.Globalization.CultureInfo.InvariantCulture):v.GetDouble();}
internal FixtureDeclarations(NavigationRetentionBudget b,string path){foreach(var row in JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateArray()){
var obstacles=row.GetProperty("obstacles").EnumerateArray().Select(o=>{double radius=N(o,"CircleRadius");if(radius>0)return NavObstacle.RestoreCircle(N(o,"MinX"),N(o,"MinZ"),N(o,"MaxX"),N(o,"MaxZ"),radius);if(o.GetProperty("polygon").ValueKind!=JsonValueKind.Null)return new NavObstacle(new NavPolygon(o.GetProperty("polygon").EnumerateArray().Select(v=>new NavPoint(N(v,"X"),N(v,"Z"))).ToArray()),N(o,"Bottom"),N(o,"Top"));return new NavObstacle(N(o,"MinX"),N(o,"MinZ"),N(o,"MaxX"),N(o,"MaxZ"));}).ToArray();
var geometry=new NavGeometry(N(row,"HalfExtent"),obstacles,row.GetProperty("Revision").GetInt32());var plain=new PartitionNavigationProvider(ThreeCrossingsMap.Default,geometry,row.GetProperty("HoldBinding").GetString());if(plain.Binding!=row.GetProperty("Binding").GetString())throw new Exception("neutral declaration provider binding");
if(!b.TryOpenScope(NavigationPreparationKind.Admission,7,0,out var scope))throw new Exception("scope");using(scope){var provider=PartitionNavigationProvider.CopyOwned(plain,scope.OwnedReservation());var owner=((INavStorageFacade)provider).StorageRoot;scope.Attach(owner);scope.Commit(owner,_=>{});owners.Add(owner);var f=row.GetProperty("profile");Bindings.Add(new NavArrivalWorkingBinding(row.GetProperty("Entity").GetInt64(),provider,new NavGraphProfile(f.GetProperty("ExactProfileBinding").GetString(),f.GetProperty("CostId").GetString(),f.GetProperty("CostRevision").GetInt32(),N(f,"GridCell"))));}}
}
public void Dispose(){foreach(var owner in owners)owner.ReleaseOwner();}
}
internal static class ReplayInputs {
    static long L(JsonElement value,string name)=>value.GetProperty(name).GetInt64();
    static string S(JsonElement value,string name)=>value.GetProperty(name).GetString();
    static NavLocation Location(JsonElement value)=>new NavLocation(new NavPoint(value.GetProperty("x").GetDouble(),value.GetProperty("z").GetDouble()),S(value,"surface"));
    static void Require(bool condition,string detail){if(!condition)throw new ArgumentException("Exact preflight mismatch: "+detail);}
    static NavLocation[] Points(JsonElement rows)=>rows.EnumerateArray().Select((row,index)=>{Require(L(row,"order")==index,"ordered point "+index);return Location(row.GetProperty("point"));}).ToArray();
    static T Field<T>(object value,string name)=>EdgeAudit.Field<T>(value,name);
    internal static NavScheduledRequest[] Preflight(JsonElement data,IReadOnlyList<NavArrivalWorkingBinding> bindings,List<object> checks)
    {
            var full=data.GetProperty("fullSource");var rawSource=full.GetProperty("source");
            var source=new NavArrivalCandidateSource(S(rawSource,"Id"),(int)L(rawSource,"Revision"),Location(rawSource.GetProperty("requested")),Points(rawSource.GetProperty("points")));
            Require(source.StableKey==S(rawSource,"StableKey"),"full ordered source StableKey");checks.Add(new{role="full-source",source.StableKey,count=source.Candidates.Count});
            var prepared=data.GetProperty("prepared");var regions=new List<NavTerminalRegion>();
            foreach(var raw in prepared.GetProperty("regions").EnumerateArray()){
                if(!raw.GetProperty("present").GetBoolean()){regions.Add(null);continue;}
                var region=new NavTerminalRegion(S(raw,"Id"),(int)L(raw,"Revision"),Location(raw.GetProperty("requested")),Points(raw.GetProperty("candidates")));
                Require(region.StableKey==S(raw,"StableKey"),"ordered region page "+L(raw,"page"));regions.Add(region);
            }
            Require(regions.Count==L(prepared,"regionCount"),"all saved prepared regions");checks.Add(new{role="ordered-regions",count=regions.Count,keys=regions.Select(r=>r?.StableKey).ToArray(),page0=regions[0].StableKey});
            var work=data.GetProperty("work");
            var recipes=new List<NavScheduledRequest>();
            foreach(var rep in data.GetProperty("observed").EnumerateArray()){
                var subRaw=rep.GetProperty("subscription");long entity=L(subRaw,"EntityId");var c=rep.GetProperty("clearance");
                var clearance=new NavClearanceProfile(S(c,"Id"),(int)L(c,"Revision"),S(c,"MobilityClass"),(int)L(c,"MobilityRevision"),c.GetProperty("Radius").GetDouble());
                var rawMember=full.GetProperty("memberBindings").EnumerateArray().Single(row=>L(row,"EntityId")==entity);
                var original=work.GetProperty("members").EnumerateArray().Single(row=>L(row,"EntityId")==entity);
                var member=new NavArrivalMember(entity,L(subRaw,"Incarnation"),L(rawMember,"Order"),L(subRaw,"MobilityRevision"),Location(subRaw.GetProperty("origin")),clearance,L(subRaw,"WorldGeneration"),L(subRaw,"GroupId"),L(subRaw,"RootOrderRevision"),L(subRaw,"CommandSequence"),L(subRaw,"IssuedTick"),L(subRaw,"ActivationRevision"),L(subRaw,"MemberOrderRevision"),L(subRaw,"RequestSequence"),S(subRaw,"HoldBinding"));
                Require(member.StableKey==S(rawMember,"StableKey"),"member "+entity);
                Require(member.Origin.Equals(Location(original.GetProperty("origin")))&&member.RequestSequence==L(original,"RequestSequence"),"current work member roles "+entity);
                var subscription=new NavRouteSubscription(member.WorldGeneration,member.GroupId,member.RootOrderRevision,member.CommandSequence,member.IssuedTick,member.ActivationRevision,member.EntityId,member.Incarnation,member.MemberOrderRevision,member.MobilityRevision,member.RequestSequence,member.Origin,Location(subRaw.GetProperty("assigned")),member.HoldBinding,(NavRouteProvenance)L(subRaw,"Provenance"));
                Require(subscription.StableKey==S(subRaw,"StableKey"),"observed subscription "+entity);
                Require(S(rep.GetProperty("region"),"StableKey")==regions[0].StableKey,"observed recipe page0 "+entity);
                var provider=bindings.Select(x=>x.Provider).First(x=>x.Binding==S(rep,"binding"));
                var profile=bindings.Select(x=>x.Profile).First(x=>x.ExactProfileBinding==S(rep,"ExactProfileBinding"));
                Require(provider is PartitionNavigationProvider,"owned partition provider donor");
                Require(((INavStorageFacade)provider).StorageRoot!=null,"donor provider has exact owned root");
                Require(profile.ExactProfileBinding==S(rep,"ExactProfileBinding"),"current exact profile");
                var limit=data.GetProperty("header").GetProperty("limits");var limits=new NavGraphCompileLimits((int)L(limit,"MaxSamples"),(int)L(limit,"MaxNodes"),(int)L(limit,"MaxArcs"));
                var recipe=new NavScheduledRequest(provider,clearance,profile,limits,regions[0],subscription,(int)L(data.GetProperty("header"),"MaxLabels"),query:true,groupQuery:true,demandQuery:true,lazyPartition:true);
                foreach(string key in new[]{"GraphKey","CompileKey","ArtifactKey"})Require((string)typeof(NavScheduledRequest).GetProperty(key).GetValue(recipe)==S(rep,key),"observed current "+key+" entity"+entity);
                Require(recipe.MetricContract.ToString()==S(rep,"metricContract"),"metric contract "+entity);
                Require(recipe.Singleton==rep.GetProperty("Singleton").GetBoolean()&&recipe.Query&&recipe.GroupQuery&&recipe.DemandQuery&&recipe.DemandQueryV2&&recipe.DemandQueryV3&&!recipe.DemandQueryV4&&recipe.DemandQueryV5&&recipe.DemandQueryV6&&recipe.LazyPartition,"exact saved query policy");
                recipes.Add(recipe);
                checks.Add(new{role="observed-current-member-subscription",entity,member=member.StableKey,subscription=subscription.StableKey,region=regions[0].StableKey,
                    recordedProvider=S(rep,"binding"),recordedProfile=S(rep,"ExactProfileBinding"),recordedGraph=S(rep,"GraphKey"),recordedCompile=S(rep,"CompileKey"),recordedArtifact=S(rep,"ArtifactKey"),recordedRecipeHashesRecomputed=true});
            }
            return recipes.ToArray();
    }
}
