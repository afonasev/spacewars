using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Explicit ordinary-Player QA route for source log configurations. Inactive
    // in normal play; deadline is the fixed test budget, never a balance setting.
    public sealed class AiMatchScenarioEvidence : MonoBehaviour
    {
        sealed class ProducerCapture
        {
            readonly Dictionary<(int,long),long> readySince=new Dictionary<(int,long),long>();
            readonly string output;readonly int seed;readonly long generation;readonly bool armyPending,armyStart;
            Task<byte[]> pending;bool written;
            public readonly JObject Result=new JObject{["Status"]="No prolonged blocker observed"};
            public ProducerCapture(string directory,int expectedSeed,long expectedGeneration,bool captureArmyPending=false,bool captureArmyStart=false){output=directory;seed=expectedSeed;generation=expectedGeneration;armyPending=captureArmyPending;armyStart=captureArmyStart;if(armyPending)Result["Status"]="No prolonged pending army order observed";}
            public void Observe(PlayableSnapshot view,PlayableRuntime runtime,PlayableProfile gameplay=null)
            {
                if(pending!=null)return;
                if(armyPending){
                    if(armyStart){
                        var first=view.Entities.FirstOrDefault(u=>u.Owner==PlayableOwner.Enemy&&(u.Kind==PlayableEntityKind.Tank||u.Kind==PlayableEntityKind.Shkval));
                        if(first==null)return;
                        Result["Status"]="Army start capture pending";Result["TriggerTick"]=view.Tick;Result["Entity"]=first.Id;Result["Cause"]="first own heavy unit birth; current world for exact continuation";
                        Result["TriggerFrame"]=JObject.FromObject(new{view.Tick,Unit=first,Entities=view.Entities.ToArray()});
                        try{pending=runtime.RequestCaptureBytes();}catch(Exception error){pending=Task.FromException<byte[]>(error);}return;
                    }
                    var seen=new HashSet<(int,long)>();
                    foreach(var unit in view.Entities.Where(u=>u.Owner==PlayableOwner.Enemy&&(u.Kind==PlayableEntityKind.Tank||u.Kind==PlayableEntityKind.Shkval))){
                        var order=unit.Queue?.Pending;long identity=0;string cause=null;
                        if(order!=null&&order.Kind==PlayableCommandKind.AttackMove&&order.Anchor.HasValue){
                            var target=order.Anchor.Value.Position;
                            if(Math.Sqrt(Math.Pow(target.X-unit.Position.X,2)+Math.Pow(target.Z-unit.Position.Z,2))>26){identity=order.GroupId;cause="far pending AttackMove";}
                        }
                        // The observed failure can remain in defense before any offensive
                        // route is issued. Capture that actual accepted, stalled approach.
                        if(identity==0&&unit.Kind==PlayableEntityKind.Tank&&!unit.Moving&&unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack){
                            var target=view.Entities.FirstOrDefault(e=>e.Id==unit.CurrentOrder.TargetId&&e.Health>0);
                            if(target!=null&&Math.Sqrt(Math.Pow(target.Position.X-unit.Position.X,2)+Math.Pow(target.Position.Z-unit.Position.Z,2))>PlayableUnitRules.Range(gameplay,unit.Kind)){
                                identity=-(long)target.Id;cause="stationary out-of-range accepted Attack";
                            }
                        }
                        if(identity==0)continue;var key=(unit.Id,identity);seen.Add(key);
                        if(!readySince.TryGetValue(key,out long first)){readySince.Add(key,view.Tick);first=view.Tick;}
                        // Diagnostic delay only; no gameplay deadline or acceptance changes.
                        if(view.Tick-first<60)continue;
                        Result["Status"]="Army capture pending";Result["TriggerTick"]=view.Tick;Result["Entity"]=unit.Id;Result["OrderIdentity"]=identity;Result["Cause"]=cause;
                        Result["TriggerFrame"]=JObject.FromObject(new{view.Tick,Unit=unit,Entities=view.Entities.ToArray()});
                        try{pending=runtime.RequestCaptureBytes();}catch(Exception error){pending=Task.FromException<byte[]>(error);}return;
                    }
                    foreach(var key in readySince.Keys.Where(k=>!seen.Contains(k)).ToArray())readySince.Remove(key);
                    return;
                }
                foreach(var b in view.Buildings){
                    var orders=b.PrivateState?.Orders;
                    if(b.Kind!=PlayableBuildingKind.Factory||orders==null||orders.Count==0||!orders[0].Active||orders[0].Remaining>0)continue;
                    var key=(b.Id,orders[0].Id);if(!readySince.TryGetValue(key,out long first)){readySince.Add(key,view.Tick);first=view.Tick;}
                    // Diagnostic trigger only; gameplay and qualification thresholds are unchanged.
                    if(view.Tick-first<300)continue;
                    Result["Status"]="Capture pending";Result["TriggerTick"]=view.Tick;Result["Factory"]=b.Id;Result["Order"]=orders[0].Id;
                    Result["TriggerFrame"]=JObject.FromObject(new{view.Tick,Factory=b,Entities=view.Entities.ToArray()});
                    try{pending=runtime.RequestCaptureBytes();}catch(Exception error){pending=Task.FromException<byte[]>(error);}return;
                }
            }
            public void FlushCompleted()
            {
                if(pending==null||!pending.IsCompleted||written)return;written=true;
                try{
                    var bytes=pending.GetAwaiter().GetResult();using var state=PlayableWorldState.Decode(bytes);
                    if(state.Version!=PlayableWorldState.CurrentVersion||state.Seed!=seed||state.Generation!=generation)throw new InvalidOperationException("Match capture envelope binding mismatch");
                    string path=Path.Combine(output,armyPending?"army-pending.world":"producer-blocker.world");File.WriteAllBytes(path,bytes);
                    using var hash=SHA256.Create();Result["Sha256"]=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
                    Result["BindingSha256"]=BitConverter.ToString(hash.ComputeHash(state.Binding)).Replace("-","").ToLowerInvariant();
                    Result["Status"]="Captured current envelope; target and profile/map binding require restore preflight";Result["Path"]=path;Result["CapturedTick"]=state.Tick;
                    Result["Version"]=state.Version;Result["SourceIdentity"]=state.SourceIdentity;Result["Bytes"]=bytes.Length;
                }catch(Exception error){Result["Status"]="Capture NotRun";Result["Error"]=error.ToString();}
                File.WriteAllText(Path.Combine(output,armyPending?"army-capture.json":"producer-capture.json"),Result.ToString());
            }
        }
        static string Argument(string key){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install(){if(Argument("-aiMatchScenarioEvidence")!=null)new GameObject("AI match scenario evidence").AddComponent<AiMatchScenarioEvidence>();}
        IEnumerator Start()
        {
            if(!NativeSilentStartup.IsSilent)throw new InvalidOperationException("AI scenario evidence requires muted startup");
            var source=JObject.Parse(File.ReadAllText(Argument("-aiMatchScenarioEvidence")));
            int number=int.Parse(Argument("-aiMatchScenarioNumber"));
            string output=Argument("-aiMatchScenarioOutput");if(string.IsNullOrEmpty(output))throw new ArgumentException("Scenario output directory required");Directory.CreateDirectory(output);
            var entry=source["Scenarios"].Single(s=>(int)s["Number"]==number);
            IPlayableTerrain map=number==1||number==2?(IPlayableTerrain)new NullDockMap():number==3?new BasaltCrownMap():number==4?new MossHighlandMap():number==5?new WhiteMeridianMap():number==6?new FoundryMap(new FoundryProfileData()):number==7?new CrimsonGroveMap():throw new ArgumentOutOfRangeException(nameof(number));
            var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(source["Profile"].ToString()),map);
            var ai=new AiProfile(JsonUtility.FromJson<AiProfileData>(source["Ai"].ToString()));if(ai.Hash!=(string)source["AiHash"])throw new InvalidOperationException("Scenario AI profile hash mismatch");
            var lobby=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=(int)entry["Seed"],NullDock=number<=2,BasaltCrown=number==3,MossHighland=number==4,WhiteMeridian=number==5,Foundry=number==6,CrimsonGrove=number==7,
                Participants=entry["Participants"].Select(s=>new NativeLobbyParticipant{Name=(string)s["Id"],Team=(int)s["Team"],Color=(int)s["Color"],Difficulty=(AiDifficulty)Enum.Parse(typeof(AiDifficulty),(string)s["Difficulty"])}).ToList()};
            using var runtime=PlayableRuntime.CreateLobbyMatch(profile,number,lobby,aiProfile:ai);using var host=new UnityHostRouteService();
            var producerCapture=number==4&&Argument("-captureProducerBlocker")!=null?new ProducerCapture(output,(int)entry["Seed"],runtime.Generation):null;
            var armyCapture=number==2&&(Argument("-captureArmyPending")!=null||Argument("-captureArmyStart")!=null)?new ProducerCapture(output,(int)entry["Seed"],runtime.Generation,true,Argument("-captureArmyStart")!=null):null;
            runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);double deadline=Time.realtimeSinceStartupAsDouble+280,maxAir=0,maxHeavy=0;bool reached=false;string failure=null;
            var homes=new Dictionary<PlayableOwner,NavPoint>();
            while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline)
            {
                host.Service(runtime,profile.NavigationRequestsPerFrame);var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);var view=frame.Overview;
                producerCapture?.Observe(view,runtime);producerCapture?.FlushCompleted();armyCapture?.Observe(frame.Players.Single(p=>p.OwnerId=="foundry-2"),runtime,profile);armyCapture?.FlushCompleted();
                failure=runtime.Latest.Failure??host.LastProjectionFailure;if(failure!=null)break;
                foreach(var h in view.Buildings.Where(b=>b.Kind==PlayableBuildingKind.Headquarters&&!homes.ContainsKey(b.Owner)))homes.Add(h.Owner,h.Position);
                foreach(var u in view.Entities.Where(u=>u.Owner==PlayableOwner.Enemy&&homes.ContainsKey(u.Owner)))
                {
                    double distance=Math.Sqrt(Math.Pow(u.Position.X-homes[u.Owner].X,2)+Math.Pow(u.Position.Z-homes[u.Owner].Z,2));
                    if(u.Kind==PlayableEntityKind.Hawk)maxAir=Math.Max(maxAir,distance);
                    if(u.Kind==PlayableEntityKind.Tank||u.Kind==PlayableEntityKind.Shkval)maxHeavy=Math.Max(maxHeavy,distance);
                }
                if(view.Tick>=(int)entry["EndTick"]||view.Outcome!=PlayableMatchOutcome.Playing){reached=true;break;}
                yield return null;
            }
            failure=failure??runtime.Latest.Failure??host.LastProjectionFailure;
            producerCapture?.FlushCompleted();armyCapture?.FlushCompleted();
            bool passed=reached&&failure==null&&(number!=6||maxAir>7.3)&&(number!=2||maxHeavy>26);
            var report=new JObject{["Number"]=number,["Tick"]=runtime.Latest.Tick,["ReachedHorizonOrEnd"]=reached,["MaxFoundry2AirDistance"]=maxAir,["MaxFoundry2HeavyDistance"]=maxHeavy,["Failure"]=failure,["Passed"]=passed,["JournalSession"]=TestMatchLogSession.CurrentDirectory};
            runtime.RequestStop();double until=Time.realtimeSinceStartupAsDouble+5;while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<until)yield return null;
            producerCapture?.FlushCompleted();armyCapture?.FlushCompleted();report["ProducerCapture"]=producerCapture?.Result;report["ArmyCapture"]=armyCapture?.Result;
            if(runtime.Latest.Failure!=null){passed=false;report["Failure"]=runtime.Latest.Failure;report["Passed"]=false;}
            File.WriteAllText(Path.Combine(output,"result.json"),report.ToString());
            TestMatchLogSession.PrepareForQuit();Debug.Log((passed?"AI_MATCH_SCENARIO_PASS ":"AI_MATCH_SCENARIO_FAIL ")+output);Application.Quit(passed&&runtime.IsStopped?0:2);
        }
    }
}
