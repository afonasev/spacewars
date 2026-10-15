using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;
using Spacewars.Headless;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiCurrentSafe7109FundingWindowDiagnosticTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private const string Archive="docs/native-ai/q4/safe-financial-retarget-20261013/attempts/root-preservation/owned-raw-natural-focused.tar.zst";
        private const string ArchiveHash="84dd7ec4ec2bd73953f3d9fafbc822510b60ad8c448c150d4dbc97d1fb2e3385";
        private const string Input=".local/qa/q4-safe7109-funding-window-20261013/input/7109-checkpoint-6000.world";
        private const string InputHash="31e918f6966a365cffce2a4b0a0bda047818bda55dc01b5a8423d002cbc3b735";
        private const string Control=".local/qa/q4-safe7109-funding-window-20261013/input/7109-checkpoint-6300.world";
        private const string ControlHash="2f1ad9fadbe589d4245800c155b0639aa97aa38e5b034b921e27562fa65e8cf2";
        private const string Owner="ai-0";
        private const int StartTick=6000,DecisionTick=6278,EndTick=6300,Site=9;

        private static string Root(){var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;Assert.NotNull(d);return d.FullName;}
        private static object Get(object x,string name){var f=x.GetType().GetField(name,F);Assert.NotNull(f,name);return f.GetValue(x);}
        private static object Call(object x,string name)=>x.GetType().GetMethod(name,F).Invoke(x,null);
        private static object DecodeSiteFundingMemory(string payload)
        {
            if(string.IsNullOrWhiteSpace(payload))return null;
            using(var stream=new MemoryStream(Convert.FromBase64String(payload)))using(var reader=new BinaryReader(stream))
            {
                Func<object> fingerprint=()=>new{Site=reader.ReadInt32(),Kind=reader.ReadInt32(),Owner=reader.ReadInt32(),Claimant=reader.ReadInt32(),Center=reader.ReadInt32(),Contested=WorldWire.Boolean(reader),Progress=reader.ReadDouble()};
                int magic=reader.ReadInt32();string owner=reader.ReadString();long generation=reader.ReadInt64();string metadata=reader.ReadString();
                bool hasActive=WorldWire.Boolean(reader);object active=hasActive?fingerprint():null;
                int expiredCount=reader.ReadInt32();var expired=Enumerable.Range(0,expiredCount).Select(_=>fingerprint()).ToArray();
                int policyVersion=0,homeSite=0;bool firstIncomeReady=false;var dormant=Array.Empty<object>();
                if(stream.Position<stream.Length)
                {
                    policyVersion=reader.ReadInt32();homeSite=reader.ReadInt32();firstIncomeReady=WorldWire.Boolean(reader);
                    if(stream.Position<stream.Length)
                    {
                        int dormantMagic=reader.ReadInt32();int count=reader.ReadInt32();dormant=Enumerable.Range(0,count).Select(_=>
                        {var target=fingerprint();long created=reader.ReadInt64(),expires=reader.ReadInt64();return (object)new{Target=target,Created=created,Expires=expires};}).ToArray();
                        return new{magic,owner,generation,metadata,active,expired,policyVersion,homeSite,firstIncomeReady,dormantMagic,dormant,TrailingBytes=stream.Length-stream.Position};
                    }
                }
                return new{magic,owner,generation,metadata,active,expired,policyVersion,homeSite,firstIncomeReady,dormant,TrailingBytes=stream.Length-stream.Position};
            }
        }
        private static void Advance(PlayableAuthorityTick a,UnityHostRouteService host){for(int i=0;i<512;i++){if(a.TryAdvance())return;host.Service(a,64);}Assert.Fail("ordinary typed-route barrier timed out at "+a.Tick);}
        private static void AssertBytesEqual(byte[] expected,byte[] actual,string message){if(expected.Length!=actual.Length)Assert.Fail(message+" length "+expected.Length+" != "+actual.Length);for(int i=0;i<expected.Length;i++)if(expected[i]!=actual[i])Assert.Fail(message+" first difference "+i);}
        private static int FirstByteDifference(byte[] expected,byte[] actual){if(expected.Length!=actual.Length)return Math.Min(expected.Length,actual.Length);for(int i=0;i<expected.Length;i++)if(expected[i]!=actual[i])return i;return -1;}
        private static string AuthorityFacts(object domain,FieldInfo projection,FieldInfo requests,FieldInfo ownerFilter,FieldInfo capture)
        {
            var p=projection.GetValue(domain);var r=requests.GetValue(domain);var o=ownerFilter.GetValue(domain);var c=capture.GetValue(domain);
            try{projection.SetValue(domain,null);requests.SetValue(domain,null);ownerFilter.SetValue(domain,null);capture.SetValue(domain,false);return PlayableWorldRestoreTests.Facts(domain,true);}
            finally{projection.SetValue(domain,p);requests.SetValue(domain,r);ownerFilter.SetValue(domain,o);capture.SetValue(domain,c);}
        }
        private sealed class Tick{internal int Number,Finance;internal byte[] World;internal string Facts,Views,CheckpointsJson;}
        private sealed class Timings{internal long Advance,WorldCaptureComparison,IndependentFacts,DtoSerializationComparison,EvidenceWriting;}
        private sealed class Run{internal Tick[] Ticks;internal int TicksObserved;internal readonly Timings Timing=new Timings();internal readonly List<string> Trace=new List<string>();internal readonly List<string> Preselection6278=new List<string>();internal readonly List<string> ExpansionDecision6278=new List<string>();internal object ObserverBindingProof,FirstMismatch;}
        private sealed class ReadOnlyByteCollectionJsonConverter:JsonConverter
        {
            private static readonly ReadOnlyCollection<string> DecimalTokens=Array.AsReadOnly(Enumerable.Range(0,256).Select(x=>x.ToString(CultureInfo.InvariantCulture)).ToArray());
            public override bool CanConvert(Type objectType)=>objectType==typeof(ReadOnlyCollection<byte>);
            public override bool CanRead=>false;
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer)
            {
                if(value==null){writer.WriteNull();return;}
                if(value.GetType()!=typeof(ReadOnlyCollection<byte>))throw new JsonSerializationException("The coverage converter accepts only ReadOnlyCollection<byte> values.");
                var bytes=(ReadOnlyCollection<byte>)value;writer.WriteStartArray();
                for(int i=0;i<bytes.Count;i++)writer.WriteRawValue(DecimalTokens[bytes[i]]);
                writer.WriteEndArray();
            }
            public override object ReadJson(JsonReader reader,Type objectType,object existingValue,JsonSerializer serializer)
                =>throw new NotSupportedException("This diagnostic converter is write-only.");
        }
        private sealed class CoverageOnlyContractResolver:DefaultContractResolver
        {
            private readonly JsonConverter converter;
            internal CoverageOnlyContractResolver(JsonConverter converter){this.converter=converter;}
            protected override JsonProperty CreateProperty(MemberInfo member,MemberSerialization memberSerialization)
            {
                var property=base.CreateProperty(member,memberSerialization);
                if(member.DeclaringType==typeof(TeamVisionSnapshot)&&member.Name=="Coverage")
                {
                    if(property.PropertyType!=typeof(IReadOnlyList<byte>))throw new JsonSerializationException("TeamVisionSnapshot.Coverage changed its declared type.");
                    property.Converter=converter;
                }
                return property;
            }
        }
        private static JsonSerializer CreateCoverageSerializer()
        {
            var converter=new ReadOnlyByteCollectionJsonConverter();
            var settings=new JsonSerializerSettings{ContractResolver=new CoverageOnlyContractResolver(converter)};
            settings.Converters.Add(converter);
            return JsonSerializer.Create(settings);
        }
        private static string SerializeWithCoverageConverter(JsonSerializer serializer,object value)
        {
            var text=new StringBuilder();
            using(var output=new StringWriter(text,CultureInfo.InvariantCulture))
            using(var writer=new JsonTextWriter(output){Formatting=Formatting.None})
            {
                serializer.Serialize(writer,value);writer.Flush();return output.ToString();
            }
        }
        private static string FactsJson(string authorityFacts,string checkpointsJson,string budgetsJson)
            =>"{\"AuthorityFacts\":"+JsonConvert.SerializeObject(authorityFacts)+",\"Checkpoints\":"+checkpointsJson+",\"Budgets\":"+budgetsJson+"}";
        private static void WriteTimelineLine(StreamWriter writer,long tick,int finance,string authorityFacts,string checkpointsJson,string budgetsJson,
            string receiptsJson,string viewFactsJson,string viewsJson,string targetSiteJson,string explorersJson,string actualEventsJson)
        {
            writer.Write("{\"Tick\":");writer.Write(JsonConvert.SerializeObject(tick));
            writer.Write(",\"FinanceAfterCompleteTick\":");writer.Write(JsonConvert.SerializeObject(finance));
            writer.Write(",\"AuthorityFacts\":");writer.Write(JsonConvert.SerializeObject(authorityFacts));
            writer.Write(",\"AllOwnerCheckpoints\":");writer.Write(checkpointsJson);
            writer.Write(",\"AllOwnerLedgers\":");writer.Write(budgetsJson);
            writer.Write(",\"AllOwnerReceipts\":");writer.Write(receiptsJson);
            writer.Write(",\"ViewFacts\":");writer.Write(viewFactsJson);
            writer.Write(",\"Views\":");writer.Write(viewsJson);
            writer.Write(",\"TargetSite9\":");writer.Write(targetSiteJson);
            writer.Write(",\"Explorers\":");writer.Write(explorersJson);
            writer.Write(",\"ActualEvents\":");writer.Write(actualEventsJson);
            writer.WriteLine("}");writer.Flush();
        }
        private static double Milliseconds(long stopwatchTicks)=>stopwatchTicks*1000.0/Stopwatch.Frequency;
        private static object TimingSummary(Timings t)=>new{AdvanceMilliseconds=Milliseconds(t.Advance),WorldCaptureComparisonMilliseconds=Milliseconds(t.WorldCaptureComparison),
            IndependentFactsMilliseconds=Milliseconds(t.IndependentFacts),DtoSerializationComparisonMilliseconds=Milliseconds(t.DtoSerializationComparison),EvidenceWritingMilliseconds=Milliseconds(t.EvidenceWriting)};

        [Test]
        public void ReadOnlyCoverageJsonConverterIsByteExactAndReadNeutralOnRestoredCheckpoint()
        {
            var serializer=CreateCoverageSerializer();var converter=serializer.Converters.Single(x=>x.GetType()==typeof(ReadOnlyByteCollectionJsonConverter));
            Assert.IsTrue(converter.CanConvert(typeof(ReadOnlyCollection<byte>)));
            Assert.IsFalse(converter.CanConvert(typeof(byte[])));Assert.IsFalse(converter.CanConvert(typeof(IReadOnlyList<byte>)));
            Assert.IsFalse(converter.CanConvert(typeof(List<byte>)));
            var fullRange=Enumerable.Range(0,65536).Select(i=>(byte)(i&255)).ToArray();
            var fullMixed=Enumerable.Range(0,65536).Select(i=>(byte)((i*73+i/256+19)&255)).ToArray();
            var fullEdges=Enumerable.Range(0,65536).Select(i=>(byte)((i&1)==0?0:255)).ToArray();
            var cases=new[]{
                new{Name="empty",Value=Array.AsReadOnly(Array.Empty<byte>())},
                new{Name="all-256-byte-values",Value=Array.AsReadOnly(Enumerable.Range(0,256).Select(i=>(byte)i).ToArray())},
                new{Name="full-65536-range",Value=Array.AsReadOnly(fullRange)},
                new{Name="full-65536-mixed-pattern",Value=Array.AsReadOnly(fullMixed)},
                new{Name="full-65536-edge-pattern",Value=Array.AsReadOnly(fullEdges)}
            };
            foreach(var item in cases)Assert.AreEqual(JsonConvert.SerializeObject(item.Value),SerializeWithCoverageConverter(serializer,item.Value),item.Name+" JSON byte/order/value compatibility");
            var byteArray=(byte[])fullMixed.Clone();Assert.AreEqual(JsonConvert.SerializeObject(byteArray),SerializeWithCoverageConverter(serializer,byteArray),"byte[] remains on Json.NET defaults");

            var root=Root();var inputPath=Path.Combine(root,Input);
            Assert.AreEqual(ArchiveHash,Wire.Hash(File.ReadAllBytes(Path.Combine(root,Archive))),"immutable current primary archive");
            var input=File.ReadAllBytes(inputPath);Assert.AreEqual(InputHash,Wire.Hash(input),"exact original tick-6000 checkpoint");
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var participants=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,7109,participants);Assert.AreEqual(8,config.Roster.Count);
            var authority=PlayableAuthorityTick.RestoreBytes(input,config);
            try
            {
                Assert.AreEqual(StartTick,authority.Tick);AssertBytesEqual(input,authority.CaptureBytes(),"compatibility restore leaves exact tick-6000 world");
                var domain=Get(authority,"domain");var beforeWorld=authority.CaptureBytes();var beforeFacts=PlayableWorldRestoreTests.Facts(domain,true);
                var beforeCheckpointRecords=authority.CaptureDiagnosticCheckpoints();var beforeBudgetRecords=authority.CaptureDiagnosticBudgets();
                var beforeCheckpoints=JsonConvert.SerializeObject(beforeCheckpointRecords);var beforeBudgets=JsonConvert.SerializeObject(beforeBudgetRecords);
                Assert.AreEqual(8,beforeCheckpointRecords.Length);Assert.AreEqual(8,beforeBudgetRecords.Length);
                var beforeViews=Enumerable.Range(0,8).Select(n=>authority.ParticipantView("ai-"+n)).ToArray();
                foreach(var view in beforeViews)
                {
                    Assert.NotNull(view.Vision,"every restored participant view has a vision snapshot");
                    Assert.AreEqual(65536,view.Vision.Coverage.Count,"every restored view exposes full 64KiB coverage");
                    Assert.AreEqual(typeof(ReadOnlyCollection<byte>),view.Vision.Coverage.GetType(),"coverage converter runtime type remains exact");
                }
                var beforeLegacyJson=JsonConvert.SerializeObject(beforeViews);var beforeConvertedJson=SerializeWithCoverageConverter(serializer,beforeViews);
                Assert.AreEqual(beforeLegacyJson,beforeConvertedJson,"all eight real restored views, including coverage, match prior JsonConvert output");

                var afterWorld=authority.CaptureBytes();var afterFacts=PlayableWorldRestoreTests.Facts(domain,true);
                var afterCheckpointRecords=authority.CaptureDiagnosticCheckpoints();var afterBudgetRecords=authority.CaptureDiagnosticBudgets();
                var afterCheckpoints=JsonConvert.SerializeObject(afterCheckpointRecords);var afterBudgets=JsonConvert.SerializeObject(afterBudgetRecords);
                Assert.AreEqual(8,afterCheckpointRecords.Length);Assert.AreEqual(8,afterBudgetRecords.Length);
                var afterViews=Enumerable.Range(0,8).Select(n=>authority.ParticipantView("ai-"+n)).ToArray();
                var afterLegacyJson=JsonConvert.SerializeObject(afterViews);var afterConvertedJson=SerializeWithCoverageConverter(serializer,afterViews);
                AssertBytesEqual(beforeWorld,afterWorld,"converter serialization preserves fresh world bytes");
                Assert.AreEqual(beforeFacts,afterFacts,"converter serialization preserves fresh independent authority facts");
                Assert.AreEqual(beforeCheckpoints,afterCheckpoints,"converter serialization preserves fresh all-owner checkpoints and receipts");
                Assert.AreEqual(beforeBudgets,afterBudgets,"converter serialization preserves fresh all-owner budgets");
                Assert.AreEqual(beforeLegacyJson,afterLegacyJson,"fresh default serialization of all participant views is unchanged");
                Assert.AreEqual(beforeConvertedJson,afterConvertedJson,"fresh converter serialization of all participant views is deterministic");
                Assert.AreEqual(afterLegacyJson,afterConvertedJson,"fresh post-read default and converter serialization match directly");
            }
            finally{authority.Stop();}
        }

        [Test]
        public void CurrentSafe7109_6000To6300_FundingWindowDiagnosticIsByteAndFactNeutral()
        {
            var root=Root();var inputPath=Path.Combine(root,Input);var controlPath=Path.Combine(root,Control);
            Assert.AreEqual(ArchiveHash,Wire.Hash(File.ReadAllBytes(Path.Combine(root,Archive))),"immutable current primary archive");
            var input=File.ReadAllBytes(inputPath);var control=File.ReadAllBytes(controlPath);
            Assert.AreEqual(InputHash,Wire.Hash(input),"exact current checkpoint 6000");Assert.AreEqual(ControlHash,Wire.Hash(control),"exact current control checkpoint 6300");
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var participants=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,7109,participants);
            Assert.AreEqual(8,config.Roster.Count);Assert.IsEmpty(config.ScenarioUnits);Assert.IsEmpty(config.ScenarioBuildings);
            var evidence=Path.Combine(root,".local/qa/q4-safe7109-funding-window-20261013/attempt-06");Directory.CreateDirectory(evidence);
            var coverageSerializer=CreateCoverageSerializer();
            Run Execute(bool enabled,Run expected,StreamWriter timelineWriter)
            {
                var authority=PlayableAuthorityTick.RestoreBytes(input,config);var run=new Run{Ticks=new Tick[EndTick-StartTick]};
                object domain=Get(authority,"domain");var dt=domain.GetType();
                var routeProjection=dt.GetField("RouteProjectionReadbackForTests",F);var routeRequests=dt.GetField("RouteRequestsReadbackForTests",F);
                var routeOwner=dt.GetField("RouteProjectionOwnerFilterForTests",F);var routeCapture=dt.GetField("CaptureCurrentExpansionRoutesForTests",F);
                var scheduler=Get(authority,"scheduler");var loop=((AiAuthorityScheduler)scheduler).Owners.Single(x=>x.OwnerId==Owner);
                var expansion=(AiExpansionPlanner)Get(loop,"expansion");var arbiter=Get(loop,"arbiter");
                try
                {
                    Assert.AreEqual(StartTick,authority.Tick);AssertBytesEqual(input,authority.CaptureBytes(),"immediate restore");
                    var initial=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==Owner);
                    Assert.AreEqual(7109,initial.Seed);Assert.AreEqual(71,initial.Generation);Assert.AreEqual(AiDifficulty.Fighter,initial.Difficulty);Assert.AreEqual("Safe",initial.Opening.Opening.ToString());
                    void Trace(object row){long started=Stopwatch.GetTimestamp();run.Trace.Add(JsonConvert.SerializeObject(row));run.Timing.DtoSerializationComparison+=Stopwatch.GetTimestamp()-started;}
                    AiExpansionFinancial.ActualLifecycleReadbackOwnerFilterForTests=enabled?Owner:null;
                    AiExpansionFinancial.ActualLifecycleReadbackForTests=enabled?(Action<string,long,string,int,AiBudgetEntry,AiBudgetEntry,AiBudgetState>)((who,tick,stage,site,before,after,budget)=>Trace(new{Owner=who,Tick=tick,Stage="Lifecycle."+stage,Site=site,Before=before,After=after,Budget=budget})):null;
                    AiExpansionFinancial.ActualDecisionReadbackOwnerFilterForTests=enabled?Owner:null;AiExpansionFinancial.ActualDecisionReadbackForTests=enabled?(Action<object>)Trace:null;
                    AiExpansionFinancial.ActualWitnessStageOwnerFilterForTests=enabled?Owner:null;AiExpansionFinancial.ActualWitnessStageReadbackForTests=enabled?(Action<object>)Trace:null;
                    AiExpansionFinancial.ActualBestSearchOwnerFilterForTests=enabled?Owner:null;AiExpansionFinancial.ActualBestSearchReadbackForTests=enabled?(Action<object>)Trace:null;
                    Assert.NotNull(routeProjection);Assert.NotNull(routeRequests);routeOwner.SetValue(domain,enabled?Owner:null);routeCapture.SetValue(domain,false);
                    routeProjection.SetValue(domain,enabled?(object)new Action<PlayableRouteProjectionReadback>(r=>Trace(new{Stage="RouteProjection",Readback=r})):null);
                    routeRequests.SetValue(domain,enabled?(object)new Action<string,long,PlayableRouteRequest[]>((who,tick,requests)=>{if(who==Owner)Trace(new{Stage="RouteRequests",Owner=who,Tick=tick,Requests=requests});}):null);
                    AiExpansionPlannerGuardObserverForTests.OwnerFilter=enabled?Owner:null;
                    AiExpansionPlannerGuardObserverForTests.Observer=enabled?(Action<string,long,int,int,string,bool>)((who,tick,site,actor,stage,result)=>Trace(new{Stage="ExpansionGuard",Owner=who,Tick=tick,Site=site,Actor=actor,Guard=stage,Result=result})):null;
                    AiExpansionPlannerGuardObserverForTests.SafetyObserver=enabled?(Action<string,long,int,int,int,NavPoint,double,double,string>)((who,tick,site,actor,threat,point,distance,range,kind)=>Trace(new{Stage="ExpansionSafety",Owner=who,Tick=tick,Site=site,Actor=actor,Threat=threat,Point=point,Distance=distance,Range=range,Kind=kind})):null;
                    PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=enabled?Owner:null;
                    PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=enabled?new Action<CurrentExpansionDecisionReadback>(r=>
                    {
                        long serializationStarted=Stopwatch.GetTimestamp();
                        var readback=new{Stage="CurrentExpansionDecisionReadback",Tick=r.Observation.Tick,Owner=r.Observation.OwnerId,Observation=r.Observation,
                            r.Ledger,r.ProtectedFunds,r.FinanceSite,r.FinanceReady,r.Threatened,r.UrgentRepair,r.AllowsExpansion,r.ExpansionAction,r.ExpansionIntent,
                            r.Proposals,r.Selected,r.Rejections,r.BeforeFinanceDetach,r.AfterTechnologyAllocation,r.AfterFinancialAllocation,r.ThreatIds,
                            r.MissingScout,r.EssentialScale,r.ScalingReason,r.ScalingStartup,r.ArmyActors,r.PendingActors,r.ExpansionState};
                        var serialized=JsonConvert.SerializeObject(readback);run.Trace.Add(serialized);if(r.Observation.Tick==DecisionTick&&r.Observation.OwnerId==Owner)run.ExpansionDecision6278.Add(serialized);
                        run.Timing.DtoSerializationComparison+=Stopwatch.GetTimestamp()-serializationStarted;
                    }):null;
                    PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=enabled?Owner:null;
                    PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=enabled?new Action<string>(raw=>
                    {
                        Trace(JsonConvert.DeserializeObject(raw));
                        if(raw.Contains("\"Tick\":"+DecisionTick)&&raw.Contains("\"Stage\":\"ExpansionFinancialAdmissionInputs\""))
                        {
                            var dormantState=expansion.Capture();var failedSites=Get(expansion,"failed");var memo=Call(arbiter,"SiteFundingMemory");
                            long writing=Stopwatch.GetTimestamp();var row=JsonConvert.SerializeObject(new{Raw=raw,Expansion=dormantState,FailedSiteSuppressionFingerprints=failedSites,SiteFundingMemoryBase64=memo,SiteFundingMemory=DecodeSiteFundingMemory((string)memo)});
                            run.Timing.DtoSerializationComparison+=Stopwatch.GetTimestamp()-writing;run.Preselection6278.Add(row);
                            writing=Stopwatch.GetTimestamp();File.WriteAllText(Path.Combine(evidence,"preselection-6278.json"),row+"\n");run.Timing.EvidenceWriting+=Stopwatch.GetTimestamp()-writing;
                        }
                    }):null;
                    long timer;
                    using(var host=new UnityHostRouteService())for(int i=0;i<EndTick-StartTick;i++)
                    {
                        var traceOffset=run.Trace.Count;timer=Stopwatch.GetTimestamp();Advance(authority,host);run.Timing.Advance+=Stopwatch.GetTimestamp()-timer;Assert.AreEqual(StartTick+i+1,authority.Tick);
                        timer=Stopwatch.GetTimestamp();var world=authority.CaptureBytes();run.Timing.WorldCaptureComparison+=Stopwatch.GetTimestamp()-timer;
                        timer=Stopwatch.GetTimestamp();var authorityFacts=AuthorityFacts(domain,routeProjection,routeRequests,routeOwner,routeCapture);run.Timing.IndependentFacts+=Stopwatch.GetTimestamp()-timer;
                        if(enabled&&i==0)
                        {
                            var clone=PlayableAuthorityTick.RestoreBytes(world,config);try
                            {
                                timer=Stopwatch.GetTimestamp();var cloneDomain=Get(clone,"domain");var hookedFacts=PlayableWorldRestoreTests.Facts(domain,true);var cloneFacts=PlayableWorldRestoreTests.Facts(cloneDomain,true);run.Timing.IndependentFacts+=Stopwatch.GetTimestamp()-timer;
                                var authorityDiff=typeof(OfflineParticipantRestoreTests).GetMethod("AuthorityDiff",BindingFlags.Static|BindingFlags.NonPublic);
                                Assert.NotNull(authorityDiff,"existing full-field authority diff helper");var firstDifference=authorityDiff.Invoke(null,new object[]{domain,cloneDomain,"domain",0});
                                Assert.AreEqual(authorityFacts,cloneFacts,"detaching only test observer bindings yields exact restored authority facts");
                                run.ObserverBindingProof=new{Tick=authority.Tick,Compared="live hooked domain vs exact byte-restored twin",FirstRawAuthorityDifference=firstDifference,
                                    RawDifferenceClassification="transient nonpersisted audio-feed sequence; existing AuthorityFacts helper already excludes it",
                                    DetachedFactsEqualToTwin=true,Bindings=new object[]{new{Field="RouteProjectionReadbackForTests",Before=routeProjection.GetValue(domain)?.GetType().FullName,DuringNeutralization="null"},
                                    new{Field="RouteRequestsReadbackForTests",Before=routeRequests.GetValue(domain)?.GetType().FullName,DuringNeutralization="null"},
                                    new{Field="RouteProjectionOwnerFilterForTests",Before=routeOwner.GetValue(domain),DuringNeutralization="null"},
                                    new{Field="CaptureCurrentExpansionRoutesForTests",Before=routeCapture.GetValue(domain),DuringNeutralization=false}}};
                                Assert.AreNotEqual(hookedFacts,authorityFacts,"observer bindings, and no gameplay authority fields, explain the live-vs-twin hash delta");
                            }finally{clone.Stop();}
                        }
                        timer=Stopwatch.GetTimestamp();var checkpoints=authority.CaptureDiagnosticCheckpoints();var budgets=authority.CaptureDiagnosticBudgets();
                        var views=Enumerable.Range(0,8).Select(n=>authority.ParticipantView("ai-"+n)).ToArray();
                        var checkpointsJson=JsonConvert.SerializeObject(checkpoints);var budgetsJson=JsonConvert.SerializeObject(budgets);var viewsJson=SerializeWithCoverageConverter(coverageSerializer,views);
                        var facts=FactsJson(authorityFacts,checkpointsJson,budgetsJson);var allViews=viewsJson;
                        var ownerBudget=budgets.Single(x=>x.OwnerId==Owner);var site9=ownerBudget.Reserved.Concat(ownerBudget.Unpaid).Where(e=>e.Purpose==AiExpansionFinancial.Purpose&&e.Action.SiteId==Site).Sum(e=>e.Amount);
                        run.Timing.DtoSerializationComparison+=Stopwatch.GetTimestamp()-timer;
                        timer=Stopwatch.GetTimestamp();var postRead=authority.CaptureBytes();AssertBytesEqual(world,postRead,"complete diagnostic reads preserve bytes at "+authority.Tick);run.Timing.WorldCaptureComparison+=Stopwatch.GetTimestamp()-timer;
                        timer=Stopwatch.GetTimestamp();var postFacts=AuthorityFacts(domain,routeProjection,routeRequests,routeOwner,routeCapture);run.Timing.IndependentFacts+=Stopwatch.GetTimestamp()-timer;
                        timer=Stopwatch.GetTimestamp();var postCheckpoints=JsonConvert.SerializeObject(authority.CaptureDiagnosticCheckpoints());var postBudgets=JsonConvert.SerializeObject(authority.CaptureDiagnosticBudgets());
                        var postViews=SerializeWithCoverageConverter(coverageSerializer,Enumerable.Range(0,8).Select(n=>authority.ParticipantView("ai-"+n)).ToArray());run.Timing.DtoSerializationComparison+=Stopwatch.GetTimestamp()-timer;
                        timer=Stopwatch.GetTimestamp();Assert.AreEqual(authorityFacts,postFacts,"independent full-authority facts before/after reads at "+authority.Tick);
                        Assert.AreEqual(checkpointsJson,postCheckpoints,"all-owner checkpoints and receipts before/after reads at "+authority.Tick);
                        Assert.AreEqual(budgetsJson,postBudgets,"all-owner ledgers before/after reads at "+authority.Tick);
                        Assert.AreEqual(allViews,postViews,"all eight participant views before/after reads at "+authority.Tick);run.Timing.DtoSerializationComparison+=Stopwatch.GetTimestamp()-timer;
                        var current=new Tick{Number=(int)authority.Tick,Finance=site9,World=world,Facts=facts,Views=allViews,CheckpointsJson=authority.Tick==EndTick?checkpointsJson:null};
                        if(expected!=null)
                        {
                            var prior=expected.Ticks[i];timer=Stopwatch.GetTimestamp();int byteDifference=FirstByteDifference(prior.World,world);run.Timing.WorldCaptureComparison+=Stopwatch.GetTimestamp()-timer;
                            timer=Stopwatch.GetTimestamp();
                            bool factsEqual=prior.Facts==facts,viewsEqual=prior.Views==allViews;
                            run.Timing.DtoSerializationComparison+=Stopwatch.GetTimestamp()-timer;
                            if(byteDifference>=0||!factsEqual||!viewsEqual)
                            {
                                var mismatch=new{Result="FAIL",Qualification="NotQualified",Tick=authority.Tick,FirstWorldByteDifference=byteDifference,
                                    ExpectedWorldLength=prior.World.Length,ActualWorldLength=world.Length,ExpectedWorldHash=Wire.Hash(prior.World),ActualWorldHash=Wire.Hash(world),FactsEqual=factsEqual,ViewsEqual=viewsEqual,
                                    ExpectedFacts=prior.Facts,ActualFacts=facts,ExpectedViews=prior.Views,ActualViews=allViews,ActualEvents=run.Trace.Skip(traceOffset).ToArray()};
                                if(run.FirstMismatch==null){run.FirstMismatch=mismatch;File.WriteAllText(Path.Combine(evidence,"first-mismatch.json"),JsonConvert.SerializeObject(mismatch,Formatting.Indented));}
                            }
                        }
                        if(expected==null||i==EndTick-StartTick-1)run.Ticks[i]=current;
                        run.TicksObserved++;
                        if(enabled)
                        {
                            timer=Stopwatch.GetTimestamp();var receiptsJson=JsonConvert.SerializeObject(checkpoints.Select(c=>new{c.OwnerId,Records=c.Records.ToArray()}).ToArray());
                            var viewFactsJson=JsonConvert.SerializeObject(views.Select((v,n)=>new{Owner="ai-"+n,Facts=PlayableWorldRestoreTests.Facts(v)}).ToArray());
                            var targetSiteJson=JsonConvert.SerializeObject(views[0].Sites.FirstOrDefault(x=>x.Site.Id==Site));
                            var explorersJson=JsonConvert.SerializeObject(views[0].Entities.Where(x=>x.Owner==views[0].Owner&&x.Kind==PlayableEntityKind.Explorer).ToArray());
                            run.Timing.DtoSerializationComparison+=Stopwatch.GetTimestamp()-timer;
                            timer=Stopwatch.GetTimestamp();var actualEventsJson=JsonConvert.SerializeObject(run.Trace.Skip(traceOffset).ToArray());run.Timing.DtoSerializationComparison+=Stopwatch.GetTimestamp()-timer;
                            timer=Stopwatch.GetTimestamp();WriteTimelineLine(timelineWriter,authority.Tick,site9,authorityFacts,checkpointsJson,budgetsJson,receiptsJson,viewFactsJson,viewsJson,targetSiteJson,explorersJson,actualEventsJson);run.Timing.EvidenceWriting+=Stopwatch.GetTimestamp()-timer;
                        }
                        if(expected!=null&&i<EndTick-StartTick-1)expected.Ticks[i]=null;
                        checkpoints=null;budgets=null;views=null;checkpointsJson=null;budgetsJson=null;viewsJson=null;postCheckpoints=null;postBudgets=null;postViews=null;
                    }
                    timer=Stopwatch.GetTimestamp();AssertBytesEqual((expected==null?run.Ticks[run.Ticks.Length-1]:expected.Ticks[run.Ticks.Length-1]).World,authority.CaptureBytes(),"end diagnostic read preserves final bytes");run.Timing.WorldCaptureComparison+=Stopwatch.GetTimestamp()-timer;
                    return run;
                }
                finally
                {
                    AiExpansionFinancial.ActualLifecycleReadbackForTests=null;AiExpansionFinancial.ActualLifecycleReadbackOwnerFilterForTests=null;
                    AiExpansionFinancial.ActualDecisionReadbackForTests=null;AiExpansionFinancial.ActualDecisionReadbackOwnerFilterForTests=null;
                    AiExpansionFinancial.ActualWitnessStageReadbackForTests=null;AiExpansionFinancial.ActualWitnessStageOwnerFilterForTests=null;
                    AiExpansionFinancial.ActualBestSearchReadbackForTests=null;AiExpansionFinancial.ActualBestSearchOwnerFilterForTests=null;
                    PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=null;
                    PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=null;PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=null;
                    AiExpansionPlannerGuardObserverForTests.Observer=null;AiExpansionPlannerGuardObserverForTests.SafetyObserver=null;AiExpansionPlannerGuardObserverForTests.OwnerFilter=null;
                    routeProjection.SetValue(domain,null);routeRequests.SetValue(domain,null);routeOwner.SetValue(domain,null);routeCapture.SetValue(domain,false);authority.Stop();
                }
            }

            StreamWriter timelineWriter=null;
            try
            {
            timelineWriter=new StreamWriter(Path.Combine(evidence,"timeline-on.jsonl"),false,new UTF8Encoding(false));
            var off=Execute(false,null,timelineWriter);var on=Execute(true,off,timelineWriter);
            var offFinal=off.Ticks[EndTick-StartTick-1].World;var onFinal=on.Ticks[EndTick-StartTick-1].World;
            int offDifference=FirstByteDifference(control,offFinal),onDifference=FirstByteDifference(control,onFinal);
            bool has6278=on.Preselection6278.Count==1;
            string[] requiredStages={"ExpansionFinancialAdmissionInputs","TechnologyAllocation","ExpansionFinancialAllocation","ActualCandidatesBeforeSelection","ActualSelection","CurrentExpansionDecisionReadback"};
            var stageStatus=requiredStages.Select(stage=>new{Stage=stage,Status=on.Trace.Any(x=>x.Contains("\"Tick\":"+DecisionTick)&&x.Contains("\"Stage\":\""+stage+"\""))?"Reached":"NotReached",
                Reason=on.Trace.Any(x=>x.Contains("\"Tick\":"+DecisionTick)&&x.Contains("\"Stage\":\""+stage+"\""))?"Observed on the ordinary owner-loop path":"No actual callback emitted this stage at tick 6278; no predicate was invoked diagnostically"}).ToArray();
            bool stagesReached=stageStatus.All(x=>x.Status=="Reached");
            var qualification=new{Result=has6278&&on.ExpansionDecision6278.Count==1&&stagesReached&&offDifference<0&&onDifference<0&&on.FirstMismatch==null?"QualifiedEpisode":"NotQualified",Tick=EndTick,
                DecisionTickStatus=has6278?"Reached":"NotReached",DecisionTickReason=has6278?"Actual ExpansionFinancialAdmissionInputs callback observed":"No actual callback emitted at tick 6278; no predicate was invoked diagnostically",
                DecisionTickObserved=has6278,Stages=stageStatus,HookOffFinalControlEqual=offDifference<0,HookOffFirstByteDifference=offDifference,
                HookOnFinalControlEqual=onDifference<0,HookOnFirstByteDifference=onDifference,ControlLength=control.Length,HookOffFinalLength=offFinal.Length,HookOnFinalLength=onFinal.Length,
                ControlHash=Wire.Hash(control),HookOffFinalHash=Wire.Hash(offFinal),HookOnFinalHash=Wire.Hash(onFinal),HookParity=on.FirstMismatch==null?"Equal":"FAIL",FirstMismatch=on.FirstMismatch};
            bool finalCheckpointsEqual=off.Ticks[EndTick-StartTick-1].CheckpointsJson==on.Ticks[EndTick-StartTick-1].CheckpointsJson;
            File.WriteAllBytes(Path.Combine(evidence,"final-6300-hook-off.world"),offFinal);File.WriteAllBytes(Path.Combine(evidence,"final-6300-hook-on.world"),onFinal);
            File.WriteAllText(Path.Combine(evidence,"episode-qualification.json"),JsonConvert.SerializeObject(qualification,Formatting.Indented));
            File.WriteAllLines(Path.Combine(evidence,"trace-on.jsonl"),on.Trace);
            File.WriteAllText(Path.Combine(evidence,"expansion-decision-6278.json"),on.ExpansionDecision6278.Count==1?on.ExpansionDecision6278[0]:JsonConvert.SerializeObject(new{Status="NotReached",Reason="No actual CurrentExpansionDecisionReadback at tick 6278; no predicate was invoked diagnostically"},Formatting.Indented));
            File.WriteAllText(Path.Combine(evidence,"timings.json"),JsonConvert.SerializeObject(new{Scope="Cumulative stopwatch timings inside this required test only; no separate benchmark",EvidenceWritingScope="preselection writes and per-tick timeline writes/flushes",HookOff=new{TicksObserved=off.TicksObserved,Timings=TimingSummary(off.Timing)},HookOn=new{TicksObserved=on.TicksObserved,Timings=TimingSummary(on.Timing)}},Formatting.Indented));
            Assert.AreEqual(EndTick-StartTick,off.TicksObserved);Assert.AreEqual(EndTick-StartTick,on.TicksObserved);Assert.IsEmpty(off.Trace);Assert.IsNotEmpty(on.Trace,"existing actual-path readbacks observed");
            Assert.IsNull(on.FirstMismatch,"hook-off/on parity must hold for all 300 ticks");
            Assert.AreEqual(1,on.Preselection6278.Count,"the real ai-0 ExpansionFinancialAdmissionInputs stage must occur at tick 6278");
            Assert.AreEqual(1,on.ExpansionDecision6278.Count,"actual expansion decision readback must capture post-allocation ledgers at tick 6278");
            var decision6278=JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(on.ExpansionDecision6278[0]);
            Assert.NotNull(decision6278["AfterTechnologyAllocation"],"actual after-technology ledger at tick 6278");
            Assert.NotNull(decision6278["AfterFinancialAllocation"],"actual after-financial ledger at tick 6278");
            foreach(var stage in requiredStages)Assert.IsTrue(on.Trace.Any(x=>x.Contains("\"Tick\":"+DecisionTick)&&x.Contains("\"Stage\":\""+stage+"\"")),"actual tick 6278 stage: "+stage);
            AssertBytesEqual(control,offFinal,"actual hook-off final 6300 vs archived control");
            AssertBytesEqual(control,onFinal,"actual hook-on final 6300 vs archived control");
            Assert.IsTrue(finalCheckpointsEqual,"final all-owner receipt equality");off.Ticks[EndTick-StartTick-1]=null;on.Ticks[EndTick-StartTick-1]=null;
            Assert.IsNotEmpty(on.Preselection6278[0],"actual preselection state and serialized site funding lifecycle must be recorded");
            File.WriteAllText(Path.Combine(evidence,"summary.json"),JsonConvert.SerializeObject(new{Seed=7109,Generation=71,Difficulty=AiDifficulty.Fighter,Owners=8,
                StartTick,DecisionTick,EndTick,Archive,ArchiveHash,Input,InputHash,Control,ControlHash,HookOffOnAll300TicksEqual=true,
                AllOwnerFactsReceiptsLedgersAndViewsEqual=true,DiagnosticReadsNeutralEveryTick=true,ActualPreselection6278=true,
                Final6300MatchesCurrentArchiveControl=true,TraceRows=on.Trace.Count,ObserverBindingProof=on.ObserverBindingProof,
                FinalAllOwnerCheckpointAndReceiptEquality=finalCheckpointsEqual,HookOffTicksObserved=off.TicksObserved,HookOnTicksObserved=on.TicksObserved,
                CumulativeTimings=new{Scope="Inside this test run only",HookOff=TimingSummary(off.Timing),HookOn=TimingSummary(on.Timing)},
                Timeline=Path.Combine(evidence,"timeline-on.jsonl"),Trace=Path.Combine(evidence,"trace-on.jsonl"),Preselection=Path.Combine(evidence,"preselection-6278.json")},Formatting.Indented));
            TestContext.WriteLine("Q4_SAFE7109_FUNDING_WINDOW "+File.ReadAllText(Path.Combine(evidence,"summary.json")));
            }
            finally{if(timelineWriter!=null){timelineWriter.Flush();timelineWriter.Dispose();}}
        }
    }
}
