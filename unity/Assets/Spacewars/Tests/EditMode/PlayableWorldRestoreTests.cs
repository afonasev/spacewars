using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableWorldRestoreTests
    {
        private static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private const int Seed=19092026;
        private const string Source="native-domain-world-v1:c77962dd:source:96a32f63:ai-release-c569e3a03045";
        private static object Call(object d,string name,params object[] args)=>D.GetMethod(name,F).Invoke(d,args);
        private static object New(PlayableProfile p)=>Activator.CreateInstance(D,F,null,new object[]{p,71L,false},null);
        private static byte[] Bytes(object d)=>(byte[])Call(d,"CaptureWorldBytes",Seed,Source);
        private static object Restore(byte[] bytes,PlayableProfile p)=>(object)D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,Seed,Source});
        private static NavigationSession Nav(object d)=>(NavigationSession)D.GetProperty("Navigation",F).GetValue(d);
        private static IDictionary Buildings(object d)=>(IDictionary)D.GetField("buildings",F).GetValue(d);
        private static IDictionary Units(object d)=>(IDictionary)D.GetField("units",F).GetValue(d);
        private static PlayableSnapshot View(object d,PlayableOwner owner)=>(PlayableSnapshot)Call(d,"PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,Seed,owner);
        private static PlayableCommandStatus Send(object d,long sequence,PlayableCommandKind kind,int id,PlayableOwner owner=PlayableOwner.Player,NavPoint target=default(NavPoint),int victim=0,PlayableResearchKind research=PlayableResearchKind.TankChassis,long order=0)
            =>(PlayableCommandStatus)Call(d,"Apply",new PlayableCommand(71,sequence,owner==PlayableOwner.Player?"player-1":"enemy-1",kind,new[]{id},target,targetId:victim,researchKind:research,productionOrderId:order),null);
        private static int Build(object d,PlayableBuildingKind kind,int slot,PlayableOwner owner,bool ready=true)
        {
            int site=owner==PlayableOwner.Player?1:2;
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"BuildAt",site,slot,kind,site,owner,null));
            int id=Buildings(d).Values.Cast<object>().Select(b=>new{Id=(int)b.GetType().GetField("Id").GetValue(b),Site=(int)b.GetType().GetField("SiteId").GetValue(b),Slot=(int)b.GetType().GetField("SlotId").GetValue(b)}).Single(b=>b.Site==site&&b.Slot==slot).Id;
            if(ready){for(int tick=0;tick<1800&&!((bool)Buildings(d)[id].GetType().GetField("Ready").GetValue(Buildings(d)[id]));tick++){Call(d,"AdvanceFoundations");Call(d,"AdvanceBuildings",100d);Deliver(d,tick);Call(d,"Step",1d/30);}Assert.True((bool)Buildings(d)[id].GetType().GetField("Ready").GetValue(Buildings(d)[id]),"Seeded producer must actually finish evacuation and construction.");}
            return id;
        }
        private static void Deliver(object d,int tick)
        {
            // Explicit async-answer schedule: work completes only on labelled ticks.
            if(tick%3!=0)return;
            var nav=Nav(d);while(nav.Requests.TryDequeue(out var r))Assert.True(nav.Answers.TryEnqueue(new NavigationAnswer(r,new SharedFlowRouter(r.Geometry,r.Profile).FindPath(r.Start,r.Goal))));
        }
        // Independent oracle: reads actual private authority fields, never CaptureWorldBytes
        // or DTOs. New fields are included by default. Only explicitly classified cache,
        // synchronization, transient presentation and wall-clock diagnostic fields below are excluded.
        private static readonly Dictionary<string,string> Exclusions=new Dictionary<string,string>{
            {"PlayableDomain.soundJournal","Transient observer audio feed intentionally empty after cold restore; dedicated checkpoint test proves byte independence"},
            {"PlayableDomain.nextSoundEvent","Transient audio ID sequence belongs to the non-persisted feed, never gameplay/RNG"},
            {"PlayableDomain.soundMotion","Transient sound motion baseline re-seeded after restore; no navigation authority"},
            {"PlayableDomain.soundProductionLimits","Transient audio restriction edge baseline, never production authority"},
            {"PlayableDomain.lifecycleWork","Scratch list cleared before each lifecycle tick"},
            {"NavCrowd.spatial","Derived spatial index rebuilt from units"},
            {"NavigationSession.admission","Derived immutable publication rebuilt from groups, pending requests, geometry and profile"},
            {"NavCrowd.unitView","Read-only view of orderedUnits"},
            {"NavCrowd.<NeighborCandidates>k__BackingField","Per-step diagnostic metric"},
            {"NavCrowd.<RepairCpuMs>k__BackingField","Wall-clock solver CPU metric"},
            {"NavUnit.routeView","Read-only view of route"},
            {"NavGeometry.obstacleView","Read-only view of obstacles"},
            {"PlayableVision.cached","Derived immutable fog projection cache"}
        };
        // Cache immutable reflection metadata only. Every field value is still read
        // independently for each authority and tick; no state/digest is cached.
        private static readonly Dictionary<Type,FieldInfo[]> AuthorityFieldCache=new Dictionary<Type,FieldInfo[]>(),PublicFieldCache=new Dictionary<Type,FieldInfo[]>();
        private static readonly Dictionary<Type,PropertyInfo[]> PropertyCache=new Dictionary<Type,PropertyInfo[]>();
        private static FieldInfo[] Fields(Type type,bool authority){var cache=authority?AuthorityFieldCache:PublicFieldCache;lock(cache){if(!cache.TryGetValue(type,out var fields)){fields=type.GetFields(BindingFlags.Public|BindingFlags.Instance|(authority?BindingFlags.NonPublic:0)).OrderBy(f=>f.Name).ToArray();cache.Add(type,fields);}return fields;}}
        private static PropertyInfo[] Properties(Type type){lock(PropertyCache){if(!PropertyCache.TryGetValue(type,out var props)){props=type.GetProperties(BindingFlags.Public|BindingFlags.Instance).Where(x=>x.GetIndexParameters().Length==0).OrderBy(x=>x.Name).ToArray();PropertyCache.Add(type,props);}return props;}}
        internal static string Facts(object value,bool authority=false)
        {
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(FactBytes(value,authority)));
        }
        // Cached bytes are only BinaryWriter's immutable type/member name encoding.
        // Mutable field values, ordering and alias IDs are still visited every time.
        private static readonly Dictionary<string,byte[]> EncodedNames=new Dictionary<string,byte[]>();
        private static byte[] NameBytes(string name)
        {
            lock(EncodedNames){if(!EncodedNames.TryGetValue(name,out var bytes)){
                using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){writer.Write(name);writer.Flush();bytes=stream.ToArray();}
                EncodedNames.Add(name,bytes);
            }return bytes;}
        }
        private sealed class Reader
        {
            internal byte[] Name; internal Func<object,object> Read; internal bool Ordered;
        }
        private sealed class Description
        {
            internal byte[] Name; internal Reader[] Authority,Projection;
        }
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type,Description> Descriptions=new System.Collections.Concurrent.ConcurrentDictionary<Type,Description>();
        private static Description Describe(Type type)=>Descriptions.GetOrAdd(type,t=>{
            Func<MemberInfo,Reader> reader=member=>{
                var value=System.Linq.Expressions.Expression.Parameter(typeof(object),"value");
                var target=System.Linq.Expressions.Expression.Convert(value,t);
                var access=member is FieldInfo field?System.Linq.Expressions.Expression.Field(target,field):(System.Linq.Expressions.Expression)System.Linq.Expressions.Expression.Property(target,(PropertyInfo)member);
                return new Reader{Name=NameBytes(member.Name),Read=System.Linq.Expressions.Expression.Lambda<Func<object,object>>(System.Linq.Expressions.Expression.Convert(access,typeof(object)),value).Compile(),Ordered=t==typeof(PlayableVision)&&member.Name=="known"||t==typeof(NavCrowd)&&member.Name=="units"};
            };
            return new Description{Name=NameBytes(t.FullName),Authority=Fields(t,true).Where(field=>!Exclusions.ContainsKey(t.Name+"."+field.Name)&&!(t.IsGenericType&&t.GetGenericTypeDefinition()==typeof(NavMailbox<>)&&field.Name=="gate")).Select(field=>reader(field)).ToArray(),Projection=Properties(t).Where(prop=>!(t==typeof(PlayableSnapshot)&&prop.Name=="Sounds")).Select(prop=>reader(prop)).Concat(Fields(t,false).Select(field=>reader(field))).ToArray()};
        });
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type,byte[]> TypeNames=new System.Collections.Concurrent.ConcurrentDictionary<Type,byte[]>();
        private static byte[] TypeName(Type type)=>TypeNames.GetOrAdd(type,t=>NameBytes(t.FullName));
        internal static byte[] FactBytes(object value,bool authority=false,bool cachedNames=true)
        {
            var identities=new Dictionary<object,int>();
            using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory)){
                void Name(string name){if(cachedNames)writer.Write(NameBytes(name));else writer.Write(name);}
                void Visit(object v){
                    if(v==null){writer.Write("null");return;}var t=v.GetType();if(cachedNames)writer.Write(TypeName(t));else Name(t.FullName);
                    if(v is string str){writer.Write(str);return;}if(v is double number){writer.Write(number);return;}
                    if(v is int integer){writer.Write(integer);return;}if(v is long wide){writer.Write(wide);return;}if(v is bool flag){writer.Write(flag);return;}if(v is byte octet){writer.Write(octet);return;}
                    if(t.IsEnum){writer.Write(Convert.ToInt32(v));return;}
                    if(v is IEnumerable<byte> raw){var bytes=v as byte[]??raw.ToArray();writer.Write(bytes.Length);writer.Write(bytes);return;}
                    // Geometry/request reference aliases are operational authority. Others
                    // are values or views; profile object aliases do not change execution.
                    if(authority&&(v is NavGeometry||v is NavigationRequest)){if(identities.TryGetValue(v,out var identity)){writer.Write(identity);return;}writer.Write(identities.Count);identities.Add(v,identities.Count);}
                    if(authority&&t.IsGenericType&&t.GetGenericTypeDefinition()==typeof(AuthoritySlotMap<,>)){
                        // Independent reflection over actual allocation state, never its
                        // CaptureLayout helper. The key->slot index is derived lookup.
                        Visit(t.GetField("slots",F).GetValue(v));Visit(t.GetField("free",F).GetValue(v));
                        var lookup=(IDictionary)t.GetField("index",F).GetValue(v);writer.Write(lookup.Count);
                        foreach(var key in lookup.Keys.Cast<object>().OrderBy(x=>Convert.ToString(x,CultureInfo.InvariantCulture),StringComparer.Ordinal)){Visit(key);Visit(lookup[key]);}return;
                    }
                    if(cachedNames&&v is double[] doubles){writer.Write(doubles.Length);var name=TypeName(typeof(double));foreach(double item in doubles){writer.Write(name);writer.Write(item);}return;}
                    if(cachedNames&&v is int[] integers){writer.Write(integers.Length);var name=TypeName(typeof(int));foreach(int item in integers){writer.Write(name);writer.Write(item);}return;}
                    if(v is IEnumerable rows){var items=rows.Cast<object>().ToArray();if(t.IsGenericType&&t.GetGenericTypeDefinition()==typeof(HashSet<>))items=items.OrderBy(x=>Convert.ToString(x,CultureInfo.InvariantCulture)).ToArray();writer.Write(items.Length);foreach(var row in items)Visit(row);return;}
                    if(cachedNames){foreach(var reader in authority?Describe(t).Authority:Describe(t).Projection){
                        writer.Write(reader.Name);var item=reader.Read(v);
                        if(authority&&reader.Ordered){var dictionary=(IDictionary)item;foreach(object knownKey in dictionary.Keys.Cast<object>().OrderBy(x=>(int)x)){Visit(knownKey);Visit(dictionary[knownKey]);}}else Visit(item);
                    }return;}
                    if(authority){foreach(var f in Fields(t,true)){
                        string key=t.Name+"."+f.Name;if(Exclusions.ContainsKey(key)||t.IsGenericType&&t.GetGenericTypeDefinition()==typeof(NavMailbox<>)&&f.Name=="gate")continue;
                        Name(f.Name);var item=f.GetValue(v);
                        // Vision memory and crowd lookup tables do not use insertion order for decisions;
                        // all mutable facts/counters are still read independently.
                        if(t==typeof(PlayableVision)&&f.Name=="known"||t==typeof(NavCrowd)&&f.Name=="units"){var dictionary=(IDictionary)item;foreach(object knownKey in dictionary.Keys.Cast<object>().OrderBy(x=>(int)x)){Visit(knownKey);Visit(dictionary[knownKey]);}}else Visit(item);
                    }}else{
                        foreach(var prop in Properties(t)){if(t==typeof(PlayableSnapshot)&&prop.Name=="Sounds")continue;Name(prop.Name);Visit(prop.GetValue(v));}
                        foreach(var f in Fields(t,false)){Name(f.Name);Visit(f.GetValue(v));}
                    }
                }
                Visit(value);writer.Flush();return memory.ToArray();
            }
        }
        private static void Equal(object a,object b,int tick,bool projections=true)
        {
            var actual=Facts(a,true);var restored=Facts(b,true);
            if(actual!=restored){
                string Diff(object x,object y,string path,int depth){if(depth>4||x==null||y==null||x.GetType()!=y.GetType())return path;var type=x.GetType();if(x is IEnumerable)return path+" [ordered collection]";var changed=new List<string>();foreach(var f in Fields(type,true)){if(Exclusions.ContainsKey(type.Name+"."+f.Name))continue;var l=f.GetValue(x);var r=f.GetValue(y);if(Facts(l,true)!=Facts(r,true))changed.Add(Diff(l,r,path+"."+f.Name,depth+1));}return changed.Count==0?path+" [reference aliases]":string.Join("; ",changed);}
                Assert.Fail("Independent authority mismatch tick="+tick+" byteEqual="+Bytes(a).SequenceEqual(Bytes(b))+" fields="+Diff(a,b,"domain",0));
            }
            Assert.True(Bytes(a).SequenceEqual(Bytes(b)),"complete authority bytes at suffix tick "+tick);
            if(projections)foreach(PlayableOwner owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy})Assert.AreEqual(Facts(View(a,owner)),Facts(View(b,owner)),"fog projection "+owner+" tick "+tick);
        }
        private static void Suffix(object a,object b,int ticks=600)
        {
            Equal(a,b,-1);for(int i=0;i<ticks;i++){Deliver(a,i);Deliver(b,i);Call(a,"Step",1d/30);Call(b,"Step",1d/30);Equal(a,b,i);}
            var recaptured=Restore(Bytes(b),PlayableProfile.Default);Equal(b,recaptured,ticks);
        }
        [Test] public void SlotMapMatchesUnityDictionaryMutationOrderAndRepeatedHydration()
        {
            var expected=new Dictionary<int,string>();var actual=new AuthoritySlotMap<int,string>();var rng=new Random(19092026);
            for(int step=0;step<2400;step++){
                int key=rng.Next(120),operation=rng.Next(10);string value="value-"+step;
                if(operation<5){expected[key]=value;actual[key]=value;}
                else if(operation<8)Assert.AreEqual(expected.Remove(key),actual.Remove(key));
                else if(operation==8&&!expected.ContainsKey(key)){expected.Add(key,value);actual.Add(key,value);}
                else if(step%79==0){expected.Clear();actual.Clear();}
                CollectionAssert.AreEqual(expected.ToArray(),actual.ToArray(),"framework order step "+step);
                if(step%17==0){var b=new AuthoritySlotMap<int,string>();foreach(var row in actual)b.Add(row.Key,row.Value);var layout=actual.CaptureLayout();b.RestoreLayout(layout);Assert.AreEqual(Facts(actual,true),Facts(b,true));Array.Clear(layout,0,layout.Length);actual=b;}
            }
            IDictionary nongeneric=actual;nongeneric[999]="fixture";Assert.AreEqual("fixture",nongeneric[999]);nongeneric.Remove(999);
            foreach(int key in actual.Keys.ToArray()){actual.Remove(key);expected.Remove(key);}Assert.Zero(actual.Count);Assert.Greater(actual.CaptureLayout()[0],0);
            actual.Add(1000,"after-empty");expected.Add(1000,"after-empty");CollectionAssert.AreEqual(expected.ToArray(),actual.ToArray());
            actual.Clear();CollectionAssert.AreEqual(new[]{0},actual.CaptureLayout());
        }
        [Test] public void IndependentOracleDistinguishesFreeStackAndMalformedLayoutsFailBeforeMutation()
        {
            var a=new AuthoritySlotMap<int,string>();var b=new AuthoritySlotMap<int,string>();
            for(int i=0;i<3;i++){a.Add(i,"v");b.Add(i,"v");}a.Remove(0);a.Remove(1);b.Remove(1);b.Remove(0);
            CollectionAssert.AreEqual(a.ToArray(),b.ToArray());Assert.AreNotEqual(Facts(a,true),Facts(b,true),"same live enumeration must not hide future insertion order");
            string before=Facts(a,true);foreach(var layout in new[]{new[]{3,0,0},new[]{3,-1,0},new[]{3,0,3},new[]{262145},new[]{2,0,1},Array.Empty<int>()}){Assert.Throws<ArgumentException>(()=>a.RestoreLayout(layout));Assert.AreEqual(before,Facts(a,true));}
            a.Add(8,"a");b.Add(8,"a");a.Add(9,"b");b.Add(9,"b");CollectionAssert.AreNotEqual(a.ToArray(),b.ToArray());
        }
        [Test] public void EntityChurnAndMultipleRallyCancellationKeepFutureExecutionOrder()
        {
            var p=PlayableProfile.Default;var a=New(p);Call(a,"AddCredits",PlayableOwner.Player,100000d);
            int first=Build(a,PlayableBuildingKind.Factory,1,PlayableOwner.Player),second=Build(a,PlayableBuildingKind.Factory,2,PlayableOwner.Player);
            NavPoint Free(PlayableEntityKind kind)=>Enumerable.Range(-40,81).SelectMany(x=>Enumerable.Range(-40,81).Select(z=>new NavPoint(x,z))).First(point=>Nav(a).Crowd.CanPlace(point,PlayableUnitRules.Radius(p,kind)));
            int removed=(int)Call(a,"SpawnUnit",Free(PlayableEntityKind.Tank),PlayableOwner.Player,PlayableEntityKind.Tank);Call(a,"Damage",removed,100000);
            Call(a,"Damage",first,100000);int replacement=Build(a,PlayableBuildingKind.Factory,1,PlayableOwner.Player);
            Call(a,"SpawnUnit",Free(PlayableEntityKind.Explorer),PlayableOwner.Player,PlayableEntityKind.Explorer);
            Assert.AreEqual(PlayableCommandStatus.Accepted,Send(a,20,PlayableCommandKind.SetRally,second,target:new NavPoint(-35,-35)));
            Assert.AreEqual(PlayableCommandStatus.Accepted,Send(a,21,PlayableCommandKind.SetRally,replacement,target:new NavPoint(-34,-35)));
            var b=Restore(Bytes(a),p);Equal(a,b,-1);Call(a,"CancelAllRally");Call(b,"CancelAllRally");Equal(a,b,0);
            var receipts=(PlayableCommandReceipt[])Call(a,"DrainRallyReceipts");var restored=(PlayableCommandReceipt[])Call(b,"DrainRallyReceipts");Assert.AreEqual(Facts(receipts),Facts(restored));Assert.AreEqual(2,receipts.Length);
            for(int i=0;i<600;i++){Deliver(a,i);Deliver(b,i);Call(a,"Step",1d/30);Call(b,"Step",1d/30);Equal(a,b,i);}
        }
        [Test] public void DetachedEmptyConstructorHasNoStarterSpawnsOrGenesisTicks()
        {
            var p=PlayableProfile.Default;var empty=Activator.CreateInstance(D,F,null,new object[]{p,71L,false,true,new NavGeometry(p.ArenaHalfExtent,PlayableMap.StaticObstacles(p),1)},null);
            Assert.Zero(Units(empty).Count);Assert.Zero(Buildings(empty).Count);Assert.Zero(Nav(empty).Crowd.Units.Count);Assert.Zero((long)D.GetProperty("Tick",F).GetValue(empty));Assert.AreEqual(1,D.GetField("nextId",F).GetValue(empty));
            Assert.Zero(((ICollection)D.GetField("projectiles",F).GetValue(empty)).Count);Assert.Zero(((IDictionary)D.GetField("visions",F).GetValue(empty)).Count);Assert.Zero(Nav(empty).Requests.Count);Assert.Zero(Nav(empty).Answers.Count);
        }
        [TestCase(false)][TestCase(true)] public void IndependentBytesReconstructWithoutGenesisAndContinueEveryTick(bool authored)
        {
            var p=authored?PlayableProfile.ThreeCrossingsDefault:PlayableProfile.Default;var a=New(p);
            for(int i=0;i<47;i++){Deliver(a,i);Call(a,"Step",1d/30);}
            var bytes=Bytes(a);var b=Restore(bytes,p);Array.Clear(bytes,0,bytes.Length);Equal(a,b,-1);
            for(int i=0;i<600;i++){Deliver(a,i);Deliver(b,i);Call(a,"Step",1d/30);Call(b,"Step",1d/30);Equal(a,b,i);}
            Equal(b,Restore(Bytes(b),p),600);
        }
        // The finite 12000-tick proof includes expensive full-field every-tick oracles.
        // This is a test infrastructure watchdog, not a gameplay/performance budget.
        [Test,Timeout(1200000)] public void AuthoredPaidQueuesResearchFoundationUpgradeRepairSaleRallySettleAcrossBytes()
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var a=New(p);foreach(PlayableOwner owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy})Call(a,"AddCredits",owner,100000d);
            int factory=Build(a,PlayableBuildingKind.Factory,1,PlayableOwner.Player),science=Build(a,PlayableBuildingKind.ScientificCenter,2,PlayableOwner.Player),refinery=Build(a,PlayableBuildingKind.Refinery,3,PlayableOwner.Player),repair=Build(a,PlayableBuildingKind.Factory,4,PlayableOwner.Player),sale=Build(a,PlayableBuildingKind.Factory,5,PlayableOwner.Player);
            int enemyFactory=Build(a,PlayableBuildingKind.Factory,1,PlayableOwner.Enemy),enemyScience=Build(a,PlayableBuildingKind.ScientificCenter,2,PlayableOwner.Enemy);Build(a,PlayableBuildingKind.Refinery,3,PlayableOwner.Enemy);
            Call(a,"Damage",repair,17);for(int tick=0;tick<(p.BuildingRepairCombatLockoutSec+1)*30;tick++){Deliver(a,tick);Call(a,"Step",1d/30);}
            long seq=1;foreach(var f in new[]{factory,enemyFactory})foreach(var kind in new[]{PlayableCommandKind.QueueTank,PlayableCommandKind.QueueExplorer,PlayableCommandKind.QueueShkval})Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,seq++,kind,f,f==factory?PlayableOwner.Player:PlayableOwner.Enemy));
            foreach(PlayableOwner owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy})foreach(PlayableResearchKind kind in Enum.GetValues(typeof(PlayableResearchKind)))Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,seq++,PlayableCommandKind.QueueResearch,owner==PlayableOwner.Player?science:enemyScience,owner,research:kind));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,seq++,PlayableCommandKind.UpgradeRefinery,refinery));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,seq++,PlayableCommandKind.StartBuildingRepair,repair));Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,seq++,PlayableCommandKind.SellBuilding,sale));
            var site=TerritoryRules.Sites(p).Single(s=>s.Id==2);var blocker=(int)Call(a,"SpawnUnit",site.Slots[3].Position,PlayableOwner.Enemy,PlayableEntityKind.Explorer);
            int pending=Build(a,PlayableBuildingKind.Factory,4,PlayableOwner.Enemy,false);
            Assert.AreEqual(PlayableCommandStatus.Accepted,Send(a,seq++,PlayableCommandKind.SetRally,factory,target:new NavPoint(-35,-35)));
            Assert.AreEqual(PlayableCommandStatus.Accepted,Send(a,seq++,PlayableCommandKind.SetRally,enemyFactory,PlayableOwner.Enemy,new NavPoint(35,35)));
            Call(a,"Step",1d/30);Call(a,"Step",1d/30); // fractional clock, active production, blocked foundation, active probe
            var initial=View(a,PlayableOwner.Player);Assert.AreEqual(3,initial.Buildings.Single(x=>x.Id==factory).PrivateState.Orders.Count);Assert.True(initial.Buildings.Single(x=>x.Id==factory).PrivateState.Orders[0].Active);Assert.AreEqual(3,initial.Buildings.Single(x=>x.Id==science).PrivateState.Research.Count);Assert.NotNull(initial.Buildings.Single(x=>x.Id==factory).PrivateState.PendingRally);
            Assert.True(initial.Buildings.Single(x=>x.Id==refinery).PrivateState.Upgrade.Active);Assert.True(initial.Buildings.Single(x=>x.Id==repair).PrivateState.Lifecycle.Repairing);Assert.True(initial.Buildings.Single(x=>x.Id==sale).PrivateState.Lifecycle.Selling);Assert.AreEqual(ConstructionPhase.Pending,View(a,PlayableOwner.Enemy).Buildings.Single(x=>x.Id==pending).Phase);
            var b=Restore(Bytes(a),p);Equal(a,b,-1);
            int settledAt=-1;for(int tick=0;tick<12000;tick++){
                if(tick==5){foreach(var d in new[]{a,b})Assert.AreEqual(PlayableCommandStatus.Applied,Send(d,seq,PlayableCommandKind.Hold,blocker,PlayableOwner.Enemy));seq++;}
                if(tick==8){foreach(var d in new[]{a,b})Assert.AreEqual(PlayableCommandStatus.Applied,Send(d,seq,PlayableCommandKind.Move,blocker,PlayableOwner.Enemy,new NavPoint(35,35)));seq++;}
                Deliver(a,tick);Deliver(b,tick);Call(a,"Step",1d/30);Call(b,"Step",1d/30);Equal(a,b,tick);
                if(tick==0||tick==3||tick==600){b=Restore(Bytes(b),p);Equal(a,b,tick);}
                var own=View(a,PlayableOwner.Player);var enemy=View(a,PlayableOwner.Enemy);
                bool settled=own.Buildings.Where(x=>x.Owner==PlayableOwner.Player).All(x=>x.PrivateState.Orders.Count==0&&x.PrivateState.Research.All(r=>r.Complete)&&!x.PrivateState.Lifecycle.Repairing&&!x.PrivateState.Lifecycle.Selling&&(x.PrivateState.Upgrade?.Active!=true)&&!x.PrivateState.PendingRally.HasValue)&&enemy.Buildings.Where(x=>x.Owner==PlayableOwner.Enemy).All(x=>x.Phase==ConstructionPhase.Ready&&x.PrivateState.Orders.Count==0&&x.PrivateState.Research.All(r=>r.Complete))&&Nav(a).PendingCount==0;
                if(tick>=600&&settled){settledAt=tick;break;}
            }
            TestContext.WriteLine("seed="+Seed+" profile="+p.ProfileId+"@"+p.Revision+" map="+p.AuthoredMap.Id+"@"+p.AuthoredMap.Revision+" settledSuffixTick="+settledAt);
            Assert.GreaterOrEqual(settledAt,600,"Continue until every seeded pending lifecycle settles, not only 600 ticks.");
            Assert.False(Buildings(a).Contains(sale));Assert.True(View(a,PlayableOwner.Enemy).Buildings.Single(x=>x.Id==pending).Phase==ConstructionPhase.Ready);Assert.AreEqual(p.FactoryHealth,View(a,PlayableOwner.Player).Buildings.Single(x=>x.Id==repair).Health);
            Equal(a,Restore(Bytes(a),p),settledAt);
        }
        [Test] public void HeldBodyChangeBeforeCaptureReplansAtSameSuffixTick()
        {
            var p=PlayableProfile.Default;var a=New(p);int mover=(int)Call(a,"SpawnUnit",new NavPoint(-10,10),PlayableOwner.Player,PlayableEntityKind.Explorer),held=(int)Call(a,"SpawnUnit",new NavPoint(-7,10),PlayableOwner.Player,PlayableEntityKind.Tank);
            Nav(a).Move(mover,new NavPoint(-4,10));Deliver(a,0);Call(a,"Step",1d/30);Nav(a).Stop(held,true);
            var b=Restore(Bytes(a),p);long before=Nav(a).CaptureState().RequestSequence;Suffix(a,b);Assert.Greater(Nav(a).CaptureState().RequestSequence,before);
        }
        [Test] public void RegisteredRouteWorldWireRestoresAsOriginalPendingMailboxInput()
        {
            var p=PlayableProfile.Default;var a=New(p);
            int id=(int)Call(a,"SpawnUnit",new NavPoint(-10,10),PlayableOwner.Player,PlayableEntityKind.Explorer);
            var nav=Nav(a);Assert.True(nav.Move(id,new NavPoint(-5,10)));
            Assert.True(nav.TryTransferRoute(_=>true,out var registered));
            var bytes=Bytes(a);var b=Restore(bytes,p);var replayNav=Nav(b);
            Assert.AreEqual(1,replayNav.Requests.Count);
            Assert.AreEqual(0,replayNav.RegisteredRouteCount);
            Assert.True(replayNav.Requests.TryDequeue(out var replay));
            Assert.AreEqual(registered.Request,replay.Request);
            Assert.AreEqual(nav.Admission.Groups[0].GroupId,replayNav.Admission.Groups[0].GroupId);
            Assert.AreEqual(nav.Admission.Groups[0].RootOrderRevision,replayNav.Admission.Groups[0].RootOrderRevision);
            Assert.AreEqual(nav.Admission.Groups[0].Members[0].AssignedEndpoint,replayNav.Admission.Groups[0].Members[0].AssignedEndpoint);
            Assert.AreEqual(nav.Admission.Requests[0].Request,replayNav.Admission.Requests[0].Request);
            Assert.AreEqual(Facts(nav.Admission),Facts(replayNav.Admission),
                "Complete immutable admission including ordered members, typed endpoints, profile, geometry, HOLD and probes");
            Assert.False(replayNav.Requests.TryDequeue(out _));
            Assert.True(nav.TryCompleteRegisteredRoute(new NavigationAnswer(registered,new[]{registered.Goal})));
            Assert.True(replayNav.Answers.TryEnqueue(new NavigationAnswer(replay,new[]{replay.Goal})));
            Call(a,"Step",1d/30);Call(b,"Step",1d/30);
            Assert.AreEqual(nav.AppliedResults,replayNav.AppliedResults);
            Assert.True(Bytes(a).SequenceEqual(Bytes(b)),"world wire suffix after replay");
        }
        [Test] public void SellingCenterCascadesToUnfinishedChildrenWithoutNormalization()
        {
            var p=PlayableProfile.Default;var a=New(p);Call(a,"AddCredits",PlayableOwner.Player,10000d);int child=Build(a,PlayableBuildingKind.Factory,1,PlayableOwner.Player,false);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,1,PlayableCommandKind.SellBuilding,1));Call(a,"Step",1d/30);
            Assert.True(Buildings(a).Contains(child));Assert.False((bool)Buildings(a)[child].GetType().GetField("Ready").GetValue(Buildings(a)[child]));Suffix(a,Restore(Bytes(a),p));
        }
        [Test] public void RallyCandidateProgressProbeAnswersAndUnreadTerminalReceiptsRebind()
        {
            var p=PlayableProfile.Default;var a=New(p);Call(a,"AddCredits",PlayableOwner.Player,10000d);int f=Build(a,PlayableBuildingKind.Factory,1,PlayableOwner.Player);
            Assert.AreEqual(PlayableCommandStatus.Accepted,Send(a,1,PlayableCommandKind.SetRally,f,target:new NavPoint(-8,10)));Call(a,"AdvanceRally");Assert.True(Nav(a).Requests.TryDequeue(out var first));
            Nav(a).Answers.TryEnqueue(new NavigationAnswer(first,new NavPoint[0]));Nav(a).ApplyResults();
            var b=Restore(Bytes(a),p);Equal(a,b,-1);Call(a,"AdvanceRally");Call(b,"AdvanceRally");Equal(a,b,0);
            var work=((IDictionary)D.GetField("rallyWork",F).GetValue(a))[f];Assert.AreEqual(1,work.GetType().GetField("Candidate").GetValue(work));
            b=Restore(Bytes(b),p);for(int tick=0;tick<600;tick++){Deliver(a,tick);Deliver(b,tick);Call(a,"Step",1d/30);Call(b,"Step",1d/30);Equal(a,b,tick);}
            var receiptsA=(PlayableCommandReceipt[])Call(a,"DrainRallyReceipts");var receiptsB=(PlayableCommandReceipt[])Call(b,"DrainRallyReceipts");Assert.IsNotEmpty(receiptsA);Assert.AreEqual(Facts(receiptsA),Facts(receiptsB));Equal(a,Restore(Bytes(b),p),600);
        }
        [Test] public void StaleGeometryAnswerAndPendingReplacementKeepReferenceBindings()
        {
            var p=PlayableProfile.Default;var a=New(p);int mover=(int)Call(a,"SpawnUnit",new NavPoint(-10,10),PlayableOwner.Player,PlayableEntityKind.Explorer);
            Nav(a).Move(mover,new NavPoint(-4,10));Assert.True(Nav(a).Requests.TryDequeue(out var old));
            Call(a,"AddCredits",PlayableOwner.Player,10000d);Build(a,PlayableBuildingKind.Factory,1,PlayableOwner.Player);
            Assert.True(Nav(a).Answers.TryEnqueue(new NavigationAnswer(old,new[]{old.Goal})));
            var b=Restore(Bytes(a),p);Equal(a,b,-1);Suffix(a,b);Assert.GreaterOrEqual(Nav(a).RejectedResults,1);
        }
        [Test] public void ActiveBurstsAndRocketOutlivingSourceContinueExactly()
        {
            var p=PlayableProfile.Default;var a=New(p);foreach(int id in Units(a).Keys.Cast<int>().ToArray()){Nav(a).Remove(id);Units(a).Remove(id);}
            int rocket=(int)Call(a,"SpawnUnit",new NavPoint(-12,0),PlayableOwner.Player,PlayableEntityKind.Shkval);int enemy=(int)Call(a,"SpawnUnit",new NavPoint(-4,0),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            Units(a)[enemy].GetType().GetField("Reload").SetValue(Units(a)[enemy],999d);
            for(int i=0;i<55;i++)Call(a,"Step",1d/30);
            Assert.True(View(a,PlayableOwner.Player).Projectiles.Any(x=>x.Kind==PlayableEntityKind.Shkval));Call(a,"Damage",rocket,999);
            int explorer=(int)Call(a,"SpawnUnit",new NavPoint(-10,2),PlayableOwner.Player,PlayableEntityKind.Explorer);Call(a,"Step",1d/30);
            Assert.Greater((int)Units(a)[explorer].GetType().GetField("BurstRemaining").GetValue(Units(a)[explorer]),0);
            Suffix(a,Restore(Bytes(a),p));
        }
        [Test] public void RememberedUnseenBuildingAndRemovedCenterDamageFactSurvive()
        {
            var p=PlayableProfile.Default;var a=New(p);Call(a,"AddCredits",PlayableOwner.Enemy,10000d);int enemyFactory=Build(a,PlayableBuildingKind.Factory,1,PlayableOwner.Enemy);
            int scout=(int)Call(a,"SpawnUnit",new NavPoint(17,7),PlayableOwner.Player,PlayableEntityKind.Explorer);Call(a,"RefreshVision");Call(a,"Damage",scout,999);
            Call(a,"RefreshVision");Assert.True(View(a,PlayableOwner.Player).Vision.KnownBuildings.Any(x=>x.Id==enemyFactory));
            // Confirmed damage may outlive a sold center; this is raw authority, not a live target.
            int attacker=(int)Call(a,"SpawnUnit",new NavPoint(-15,-10),PlayableOwner.Enemy,PlayableEntityKind.Tank);Call(a,"DamageFromProjectile",1,3,attacker);Call(a,"Step",p.BuildingSaleCombatLockoutSec+1);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,99,PlayableCommandKind.SellBuilding,1));
            Call(a,"AdvanceBuildingLifecycle",p.BuildingSaleDemolitionSec+1);Call(a,"RefreshVision");
            Assert.False(Buildings(a).Contains(1));Assert.Greater(((IDictionary)D.GetField("centerDamage",F).GetValue(a)).Count,0);
            Suffix(a,Restore(Bytes(a),p));
        }
        private static int TransactionRegistryBytes(byte[] domain)
        {
            using(var stream=new MemoryStream(domain))using(var reader=new BinaryReader(stream))
            {
                int count=reader.ReadInt32();Assert.Greater(count,0);
                for(int i=0;i<count;i++){Assert.Greater(reader.ReadInt32(),0);int nameBytes=reader.ReadInt32();Assert.GreaterOrEqual(nameBytes,0);reader.ReadBytes(nameBytes);foreach(var field in PlayableProfileMetadata.Fields)reader.ReadDouble();}
                return (int)stream.Position;
            }
        }
        [Test] public void RejectsCorruptSchemaIdsPaymentCapacityAndBindingsWithoutLiveMutation()
        {
            var p=PlayableProfile.Default;var a=New(p);byte[] original=Bytes(a);
            foreach(int offset in new[]{0,4,8,12,20,original.Length-1}){var bad=(byte[])original.Clone();bad[offset]^=1;Assert.Throws<ArgumentException>(()=>PlayableWorldState.Decode(bad));}
            var corrupted=(byte[])original.Clone();corrupted[corrupted.Length/2]^=1;Assert.Throws<ArgumentException>(()=>PlayableWorldState.Decode(corrupted));
            var state=PlayableWorldState.Decode(original);state.Version=1;Assert.Throws<ArgumentException>(()=>PlayableWorldState.Decode(state.Encode()));
            state=PlayableWorldState.Decode(original);state.Generation++;AssertRestoreRejected(state.Encode(),p);
            state=PlayableWorldState.Decode(original);state.Tick++;AssertRestoreRejected(state.Encode(),p);
            state=PlayableWorldState.Decode(original);state.Seed++;AssertRestoreRejected(state.Encode(),p);
            state=PlayableWorldState.Decode(original);state.SourceIdentity+="changed";AssertRestoreRejected(state.Encode(),p);
            state=PlayableWorldState.Decode(original);state.Binding[5]^=1;AssertRestoreRejected(state.Encode(),p);
            state=PlayableWorldState.Decode(original);BitConverter.GetBytes(-1).CopyTo(state.Domain,state.Domain.Length-4);AssertRestoreRejected(state.Encode(),p); // malformed final rally slot layout
            state=PlayableWorldState.Decode(original);Assert.AreEqual(9,state.Version);int prefix=TransactionRegistryBytes(state.Domain);Assert.AreEqual((int)D.GetField("nextId",F).GetValue(a),BitConverter.ToInt32(state.Domain,prefix+54),"v9 allocator offset");BitConverter.GetBytes(1).CopyTo(state.Domain,prefix+54);AssertRestoreRejected(state.Encode(),p); // v9 next entity collides
            state=PlayableWorldState.Decode(original);Assert.AreEqual((long)D.GetField("nextAiSequence",F).GetValue(a),BitConverter.ToInt64(state.Domain,prefix+38),"v9 AI allocator offset");BitConverter.GetBytes(0L).CopyTo(state.Domain,prefix+38);AssertRestoreRejected(state.Encode(),p);
            state=PlayableWorldState.Decode(original);int incomeCount=BitConverter.ToInt32(state.Domain,prefix+86);Assert.Zero(incomeCount,"cold v9 settlement table");int creditOffset=prefix+90+incomeCount*12;Assert.AreEqual((double)D.GetField("credits",F).GetValue(a),BitConverter.ToDouble(state.Domain,creditOffset),"v9 diagnostic credit offset");BitConverter.GetBytes(-1d).CopyTo(state.Domain,creditOffset);AssertRestoreRejected(state.Encode(),p); // v9 negative diagnostic credit balance
            state=PlayableWorldState.Decode(original);Assert.AreEqual(p.StartingCredits,BitConverter.ToDouble(state.Domain,prefix+18),"v9 owner account offset");BitConverter.GetBytes(-1d).CopyTo(state.Domain,prefix+18);AssertRestoreRejected(state.Encode(),p); // v9 negative owner account
            state=PlayableWorldState.Decode(original);BitConverter.GetBytes(-1).CopyTo(state.Domain,prefix+86);AssertRestoreRejected(state.Encode(),p); // invalid settled-income count
            var income=(System.Collections.Generic.IDictionary<PlayableOwner,double>)D.GetField("settledIncome",F).GetValue(a);
            income.Add(PlayableOwner.Player,-1d);AssertRestoreRejected(Bytes(a),p);income[PlayableOwner.Player]=0d;
            state=PlayableWorldState.Decode(Bytes(a));Assert.AreEqual(1,BitConverter.ToInt32(state.Domain,prefix+86));Assert.AreEqual((int)PlayableOwner.Player,BitConverter.ToInt32(state.Domain,prefix+90));
            BitConverter.GetBytes((int)PlayableOwner.Player+100).CopyTo(state.Domain,prefix+90);AssertRestoreRejected(state.Encode(),p); // foreign income owner
            income.Clear();
            Call(a,"AddCredits",PlayableOwner.Player,10000d);int f=Build(a,PlayableBuildingKind.Factory,1,PlayableOwner.Player);Send(a,1,PlayableCommandKind.QueueTank,f);
            var order=((IList)Buildings(a)[f].GetType().GetField("Orders").GetValue(Buildings(a)[f]))[0];order.GetType().GetField("PaidCost").SetValue(order,1);AssertRestoreRejected(Bytes(a),p);
            order.GetType().GetField("PaidCost").SetValue(order,p.TankCreditCost);
            var orders=(IList)Buildings(a)[f].GetType().GetField("Orders").GetValue(Buildings(a)[f]);
            for(int i=0;i<6;i++){var extra=Activator.CreateInstance(order.GetType());foreach(var field in order.GetType().GetFields())field.SetValue(extra,field.GetValue(order));extra.GetType().GetField("Id").SetValue(extra,2L+i);orders.Add(extra);}D.GetField("nextProductionSequence",F).SetValue(a,8L);AssertRestoreRejected(Bytes(a),p);
            var decoded=PlayableWorldState.Decode(original);var independent=Restore(decoded.Encode(),p);Array.Clear(decoded.Domain,0,decoded.Domain.Length);Array.Clear(decoded.Navigation,0,decoded.Navigation.Length);foreach(var vision in decoded.Vision)Array.Clear(vision,0,vision.Length);ExactByteAssert.AreEqual(original,Bytes(independent),"Decoded arrays cannot mutate hydrated authority.");
            ExactByteAssert.AreEqual(original,Bytes(Restore(original,p)),"Valid source bytes remain reusable after failed candidates.");
        }
        [Test] public void ResearchWireRejectsOldVersionAndInvalidWaitingButAcceptsHistoricalCenter()
        {
            var p=PlayableProfile.Default;var a=New(p);Call(a,"AddCredits",PlayableOwner.Player,10000d);
            int center=Build(a,PlayableBuildingKind.ScientificCenter,1,PlayableOwner.Player);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,1,PlayableCommandKind.QueueResearch,center,research:PlayableResearchKind.TankChassis));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(a,2,PlayableCommandKind.QueueResearch,center,research:PlayableResearchKind.ExplorerAssaultGuns));
            var research=(IDictionary)D.GetField("research",F).GetValue(a);
            var queue=(IList)research[PlayableOwner.Player];var waiting=queue[1];var duration=waiting.GetType().GetField("Duration");var preferred=waiting.GetType().GetField("CenterId");
            duration.SetValue(waiting,1d);AssertRestoreRejected(Bytes(a),p);duration.SetValue(waiting,0d);
            preferred.SetValue(waiting,999999);AssertRestoreRejected(Bytes(a),p);preferred.SetValue(waiting,center);
            var old=PlayableWorldState.Decode(Bytes(a));old.Version=2;Assert.Throws<ArgumentException>(()=>PlayableWorldState.Decode(old.Encode()));
            Call(a,"Damage",center,(int)p.ScienceHealth+1);
            var sourceBytes=Bytes(a);var detached=Restore(sourceBytes,p);ExactByteAssert.AreEqual(sourceBytes,Bytes(detached));
            Assert.AreEqual(1,View(detached,PlayableOwner.Player).OwnerResearch.Count);
            Assert.False(View(detached,PlayableOwner.Player).OwnerResearch[0].Active);
            Assert.AreEqual(center,View(detached,PlayableOwner.Player).OwnerResearch[0].CenterId);
        }
        private static void AssertRestoreRejected(byte[] bytes,PlayableProfile p){var e=Assert.Throws<TargetInvocationException>(()=>Restore(bytes,p));Assert.IsInstanceOf<ArgumentException>(e.InnerException);}
        [Test] public void BindingCoversEveryEffectivePrimitiveProfileProperty()
        {
            var wire=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.WorldWire",true);var binding=wire.GetMethod("Binding",BindingFlags.Static|BindingFlags.NonPublic);
            foreach(var prop in typeof(PlayableProfile).GetProperties(BindingFlags.Public|BindingFlags.Instance).Where(x=>x.GetSetMethod(true)!=null&&(x.PropertyType==typeof(double)||x.PropertyType==typeof(int)||x.PropertyType==typeof(bool)||x.PropertyType==typeof(string)))){
                var profile=PlayableProfile.Default;var before=(byte[])binding.Invoke(null,new object[]{profile,false});var value=prop.GetValue(profile);object altered=prop.PropertyType==typeof(double)?(object)((double)value+.001):prop.PropertyType==typeof(int)?(object)((int)value+1):prop.PropertyType==typeof(bool)?(object)!(bool)value:(object)((string)value+":changed");
                prop.SetValue(profile,altered);Assert.False(before.SequenceEqual((byte[])binding.Invoke(null,new object[]{profile,false})),"Wire identity must cover effective profile field "+prop.Name);
            }
        }
        [Test] public void CircleWireRetainsExactBoundsAtNonRepresentableMidpoint()
        {
            var t=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.WorldWire",true);var write=t.GetMethod("Write",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(BinaryWriter),typeof(NavObstacle)},null);var read=t.GetMethod("ReadObstacle",BindingFlags.Static|BindingFlags.NonPublic);
            foreach(double x in new[]{.1,-.1,1e-10,12.3456789}){
                var o=new NavObstacle(new NavPoint(x,-.2),.3);using(var m=new MemoryStream()){using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))write.Invoke(null,new object[]{w,o});m.Position=0;using(var r=new BinaryReader(m)){var restored=(NavObstacle)read.Invoke(null,new object[]{r});Assert.True(o.GeometryEquals(restored));Assert.AreEqual(o.MinX,restored.MinX);Assert.AreEqual(o.CircleCenter,restored.CircleCenter);}}
            }
        }
    }
}
