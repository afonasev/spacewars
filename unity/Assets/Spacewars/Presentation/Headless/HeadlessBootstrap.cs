using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
namespace Spacewars.Headless
{
    public sealed class HeadlessBootstrap : MonoBehaviour
    {
        private MatchManifest manifest;
        private HeadlessEconomyMetrics economyMetrics;
        private PlayableAuthorityTick authority;
        private UnityHostRouteServiceProxy routes;
        private string output,mode;
        private int commandIndex,traceCount;
        private long commandSequence;
        private bool finished;
        private readonly List<StateHash> hashes=new List<StateHash>();
        private readonly HashSet<string> seen=new HashSet<string>();
        private StreamWriter decisions,states;
        private readonly Stopwatch timer=new Stopwatch();
        private readonly List<double> tickCpu=new List<double>();
        private string initialState;
        private readonly HashSet<int> genesisUnits=new HashSet<int>();
        private readonly Dictionary<int,int> previousHealth=new Dictionary<int,int>();
        private bool captured,produced,researched,damage,arrived;
        private int routeFailures;
        private readonly HashSet<int> failedRoutes=new HashSet<int>();
        private static readonly JsonSerializerSettings Strict=new JsonSerializerSettings{MissingMemberHandling=MissingMemberHandling.Error,NullValueHandling=NullValueHandling.Include};
        private static string Serialize(object value)=>JsonConvert.SerializeObject(value,Formatting.None);
        public static T Parse<T>(string json)
        {using(var reader=new JsonTextReader(new StringReader(json))){var token=JToken.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});if(reader.Read())throw new ArgumentException("Trailing JSON input.");CheckShape(token,typeof(T));return token.ToObject<T>(JsonSerializer.Create(Strict));}}
        private static void CheckShape(JToken token,Type type)
        {
            if(token.Type==JTokenType.Null){if(type==typeof(ReplayExpectation))return;throw new ArgumentException("Null input: "+type.Name);}
            if(type.IsArray){Wire.Require(token.Type==JTokenType.Array,"Expected array: "+type.Name);foreach(var item in token)CheckShape(item,type.GetElementType());return;}
            if(type==typeof(string)){Wire.Require(token.Type==JTokenType.String,"Expected string");return;}
            if(type==typeof(int)||type==typeof(long)){Wire.Require(token.Type==JTokenType.Integer,"Expected integer");return;}
            if(type==typeof(double)){Wire.Require(token.Type==JTokenType.Integer||token.Type==JTokenType.Float,"Expected number");return;}
            Wire.Require(token.Type==JTokenType.Object,"Expected object: "+type.Name);
            var properties=type.GetProperties();
            Wire.Require(((JObject)token).Properties().Count()==properties.Length,"Missing/unknown input in "+type.Name);
            foreach(var property in properties){var child=token[property.Name];Wire.Require(child!=null,"Missing input: "+property.Name);CheckShape(child,property.PropertyType);}
        }
        void Start()
        {
            try
            {
                var args=Environment.GetCommandLineArgs();
                string Arg(string key){int i=Array.IndexOf(args,key);if(i<0||i+1>=args.Length)throw new ArgumentException("Missing "+key);return args[i+1];}
                output=Arg("--ai-output");mode=Arg("--ai-mode");Wire.Require(mode=="run"||mode=="replay","Unknown worker mode.");
                var input=File.ReadAllText(Arg("--ai-manifest"));Wire.Require(input.Length<=1024*1024,"Manifest too large.");manifest=Parse<MatchManifest>(input);Wire.Validate(manifest,mode=="replay");
                var embedded=Resources.Load<TextAsset>("HeadlessBuildIdentity");Wire.Require(embedded!=null,"Missing embedded build identity.");
                var engine=HeadlessFixtures.EngineIdentity(embedded.text.Trim());var expected=manifest.Worker;
                engine.BundleHash=expected.BundleHash;engine.ExecutableHash=expected.ExecutableHash;
                Wire.Require(Serialize(engine)==Serialize(expected),"Worker engine/code/profile/navigation identity mismatch.");
                Wire.Require(Wire.FileHash(manifest.Executable)==expected.ExecutableHash&&Wire.BundleHash(manifest.WorkerRoot)==expected.BundleHash,"Worker bundle hash mismatch.");
                var config=HeadlessFixtures.Create(manifest);
                Wire.Require(manifest.MapId==config.MapIdentity&&manifest.MapHash==HeadlessFixtures.MapHash(config)&&manifest.GeometryHash==HeadlessFixtures.GeometryHash(config),"Map/geometry binding mismatch.");
                authority=new PlayableAuthorityTick(config,manifest.Generation,AiProfile.Initial,(AiDifficulty)Array.IndexOf(new[]{"recruit","fighter","veteran"},manifest.Roster[0].Difficulty));
                routes=new UnityHostRouteServiceProxy();
                economyMetrics=new HeadlessEconomyMetrics(authority,AiProfile.Initial);
                Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"failures.jsonl"),"");
                decisions=new StreamWriter(Path.Combine(output,"decisions.jsonl"));states=new StreamWriter(Path.Combine(output,"state-hashes.jsonl"));
                foreach(var e in authority.Latest.Entities)genesisUnits.Add(e.Id);
                Observe();timer.Start();initialState=RecordState();
            }
            catch(Exception e){Fail(e,e is ArgumentException||e is JsonException?2:3);}
        }
        void Update()
        {
            if(finished||authority==null)return;
            try
            {
                for(int i=0;i<manifest.TickBatch;i++)
                {
                    if(authority.Tick>=manifest.TicksLimit||authority.Latest.Outcome!=PlayableMatchOutcome.Playing){Complete();return;}
                    while(commandIndex<manifest.Commands.Length&&manifest.Commands[commandIndex].Tick==authority.Tick)Apply(manifest.Commands[commandIndex++]);
                    var start=Stopwatch.GetTimestamp();
                    bool advanced=false;
                    for(int attempts=0;attempts<4096;attempts++)
                    {if(authority.TryAdvance()){advanced=true;break;}routes.Service(authority,manifest.RouteBudget);}
                    if(!advanced)throw new InvalidOperationException("Fixed navigation barrier failed to drain.");
                    tickCpu.Add((Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency);
                    economyMetrics.Observe(authority);Observe();Trace();
                    if(manifest.Checkpoints.Contains((int)authority.Tick))RecordState();
                }
            }
            catch(Exception e){Fail(e,3);}
        }
        private void Apply(CommandInput input)
        {
            var view=authority.ParticipantView(input.OwnerId);
            var command=HeadlessFixtures.Command(authority,input,manifest.Generation,++commandSequence);
            var entities=command.CopyEntityIds();var receipt=authority.Apply(command);
            decisions.WriteLine(Serialize(new {Type="diagnostic-command",Tick=authority.Tick,Reason=receipt.Message,ObservedFacts=new {view.Credits,view.HomeSiteId,SelectedEntities=entities},Input=input,Receipt=receipt}));
            if(receipt.Status!=PlayableCommandStatus.Applied&&receipt.Status!=PlayableCommandStatus.Accepted)throw new InvalidOperationException("Diagnostic command rejected: "+receipt.Status+" "+receipt.Message);
        }
        private void Observe()
        {
            var view=authority.Latest;
            captured|=view.Sites.Any(site=>site.Site.Id==3&&site.Progress>=1);
            produced|=view.Entities.Any(e=>e.Owner==view.Owner&&!genesisUnits.Contains(e.Id));
            researched|=view.OwnerResearch.Any(r=>r.Complete);
            foreach(var e in view.Entities)
            {
                if(previousHealth.TryGetValue(e.Id,out var health)&&e.Health<health)damage=true;
                previousHealth[e.Id]=e.Health;
                if(e.Owner==view.Owner&&e.Kind==PlayableEntityKind.Tank&&e.NavigationOutcome==NavigationOutcome.Arrived&&view.Tick>30)arrived=true;
                if((e.NavigationOutcome==NavigationOutcome.Unreachable||e.NavigationOutcome==NavigationOutcome.Blocked)&&failedRoutes.Add(e.Id))routeFailures++;
                if(manifest.Fixture=="obstacle-v1"&&e.Owner==view.Owner&&e.Kind==PlayableEntityKind.Tank&&!view.Geometry.IsFree(e.Position,view.ActiveProfile.TankCollisionRadius))throw new InvalidOperationException("Tank crossed obstacle clearance.");
            }
            foreach(var b in view.Buildings){if(previousHealth.TryGetValue(b.Id,out var health)&&b.Health<health)damage=true;previousHealth[b.Id]=b.Health;}
        }
        private void Trace()
        {
            if(traceCount>=manifest.TraceLimit)return;
            foreach(var checkpoint in authority.CaptureDiagnosticCheckpoints())
            foreach(var record in checkpoint.Records)
            {
                string key=Serialize(record);if(!seen.Add(key))continue;
                if(traceCount>=manifest.TraceLimit)continue;
                var view=authority.ParticipantView(checkpoint.OwnerId);
                decisions.WriteLine(Serialize(new {Type="ai-receipt",TraceTick=authority.Tick,Reason=string.IsNullOrEmpty(record.Message)?record.Policy+":"+record.Status:record.Message,ObservedFacts=new {ObservedTick=view.Tick,view.OwnerId,view.Credits,view.IncomePerSecond,view.HomeSiteId,VisibleEntities=view.Entities.Select(e=>new {e.Id,e.Kind,e.Health,e.Position}),VisibleBuildings=view.Buildings.Select(b=>new {b.Id,b.Kind,b.Health})},Commitment=new {checkpoint.PendingActionId,checkpoint.PendingDueTick},Record=record}));traceCount++;
            }
            traceCount+=AiEconomyDecisionJournal.Write(decisions,authority.CaptureDiagnosticCheckpoints(),seen,Math.Max(0,manifest.TraceLimit-traceCount));
        }
        private string RecordState()
        {
            var bytes=authority.CaptureBytes();var hash=Wire.Hash(bytes);
            if(hashes.Count==0||hashes.Last().Tick!=authority.Tick){var state=new StateHash{Tick=authority.Tick,Hash=hash};hashes.Add(state);states.WriteLine(Serialize(state));File.WriteAllBytes(Path.Combine(output,"checkpoint-"+authority.Tick+".world"),bytes);}
            return hash;
        }
        private void Complete()
        {
            RecordState();decisions.Flush();states.Flush();timer.Stop();
            string outcome=authority.Latest.Outcome.ToString(),termination=outcome=="Playing"?"timeout":"win";
            string traceHash=Wire.FileHash(Path.Combine(output,"decisions.jsonl"));
            var actual=new ReplayExpectation{States=hashes.ToArray(),DecisionsHash=traceHash,Termination=termination,Outcome=outcome,FinalTick=authority.Tick};
            if(mode=="replay"&&Serialize(actual)!=Serialize(manifest.Expected))throw new InvalidOperationException("Replay state/strategic trace/termination hash mismatch.");
            File.WriteAllText(Path.Combine(output,"metrics.json"),Serialize(new {SimulationTicks=authority.Tick,TickRate=30,ExecutionWallMilliseconds=timer.Elapsed.TotalMilliseconds,TickCpuP50=Percentile(.5),TickCpuP95=Percentile(.95),TickCpuP99=Percentile(.99),PeakWorkingSetBytes=Process.GetCurrentProcess().PeakWorkingSet64,TraceRecords=traceCount,TraceLimit=manifest.TraceLimit,Economy=economyMetrics.Reports}));
            File.WriteAllText(Path.Combine(output,"summary.json"),Serialize(new {TechnicalResult="valid",ExitCode=0,Termination=termination,Outcome=outcome,authority.WinnerTeam,FinalTick=authority.Tick,InitialStateHash=initialState,FinalStateHash=hashes.Last().Hash,DecisionsHash=traceHash,ReplayVerified=mode=="replay",StrengthQualification=false,Coverage=new {Captured=captured,Produced=produced,ResearchCompleted=researched,DamageObserved=damage,ObstacleArrived=arrived,RouteFailureEntities=routeFailures}}));
            manifest.Expected=actual;File.WriteAllText(Path.Combine(output,"replay-inputs.json"),JsonConvert.SerializeObject(manifest,Formatting.Indented));
            Close();Application.Quit(0);
        }
        private double Percentile(double q){if(tickCpu.Count==0)return 0;var values=tickCpu.OrderBy(x=>x).ToArray();return values[(int)Math.Floor((values.Length-1)*q)];}
        private void Fail(Exception e,int code)
        {
            UnityEngine.Debug.LogException(e);
            if(output!=null){Directory.CreateDirectory(output);File.AppendAllText(Path.Combine(output,"failures.jsonl"),Serialize(new {ExitCode=code,Error=e.ToString(),Tick=authority?.Tick})+"\n");File.WriteAllText(Path.Combine(output,"summary.json"),Serialize(new {TechnicalResult="failed",ExitCode=code,Termination=code==2?"invalid":"crash",Error=e.Message,Tick=authority?.Tick}));}
            Close();Application.Quit(code);
        }
        private void Close(){finished=true;decisions?.Dispose();states?.Dispose();routes?.Dispose();authority?.Stop();}
        void OnApplicationQuit(){if(!finished){if(output!=null)File.WriteAllText(Path.Combine(output,"summary.json"),Serialize(new {TechnicalResult="incomplete",ExitCode=4,Termination="interrupted",Tick=authority?.Tick}));Close();}}
        private sealed class UnityHostRouteServiceProxy : IDisposable
        {private readonly Spacewars.Presentation.UnityHostRouteService service=new Spacewars.Presentation.UnityHostRouteService();public void Service(PlayableAuthorityTick a,int budget)=>service.Service(a,budget);public void Dispose()=>service.Dispose();}
    }
    public static class AiEconomyDecisionJournal
    {
        // The same writer is exercised in Editor against real owner-loop checkpoints.
        // These are decision diagnostics, never fabricated command/receipt records.
        public static int Write(TextWriter sink,IEnumerable<PlayableAiOwnerCheckpoint> checkpoints,HashSet<string> seen,int remaining)
        {
            if(sink==null||checkpoints==null||seen==null||remaining<0)throw new ArgumentException("Invalid journal sink.");
            int written=0;
            foreach(var checkpoint in checkpoints.OrderBy(c=>c.OwnerId,StringComparer.Ordinal))
            {
                var record=checkpoint.EconomyDecision;if(record==null||written==remaining)continue;
                if(!seen.Add("economy-decision:"+record.Identity))continue;
                sink.WriteLine(JsonConvert.SerializeObject(new {Type="ai-economy-decision",TraceTick=record.Tick,Reason=record.Reason,Record=record},Formatting.None));written++;
            }
            return written;
        }
    }

}
