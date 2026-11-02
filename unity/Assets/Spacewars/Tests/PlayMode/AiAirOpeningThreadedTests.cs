using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;
public sealed class AiAirOpeningThreadedTests
{
    [UnityTest,Timeout(320000)] public IEnumerator OrdinarySoloAirBuildsLaunchesAndMovesArmy()=>RunOrdinary(771205);
    [UnityTest,Timeout(320000)] public IEnumerator CurrentProfileSoloAirBuildsLaunchesAndMovesArmy()=>RunCurrentProfile(300);
    [UnityTest,Timeout(470000)] public IEnumerator CurrentProfileSoloAirBuildsLaunchesAndMovesArmyApproved450Seconds()=>RunCurrentProfile(450);
    IEnumerator RunCurrentProfile(float wallSeconds)
    {
        // Select only the opening precondition, before constructing any world.
        // The current Unity profile binding participates in the ordinary RNG.
        // Never retry a match or choose a seed from a gameplay outcome.
        int seed=-1;var ai=AiProfile.Initial;
        for(int candidate=0;candidate<10000;candidate++)
            if(PlayableAiOpeningComposition.Initialize(candidate,"air-owner",aiProfile:ai).Opening==PlayableAiOpening.Air){seed=candidate;break;}
        Assert.GreaterOrEqual(seed,0,"Current ordinary catalogue must contain an Air opening.");
        Assert.AreEqual(4,seed,"Freeze the current-profile ordinary witness precondition; profile drift requires review.");
        TestContext.WriteLine("CURRENT_AIR_FROZEN seed="+seed+" owner=air-owner profile="+PlayableAiOpeningComposition.ProfileBinding(ai));
        return RunOrdinary(seed,true,false,wallSeconds);
    }
    [UnityTest,Timeout(320000)] public IEnumerator CurrentProfileAdmissionDiagnostic()=>RunOrdinary(4,true,true);
    IEnumerator RunOrdinary(int seed,bool currentLobbyRoutes=false,bool admissionDiagnostics=false,float wallSeconds=300)
    {
        var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
        var lobby=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=seed,Participants=new List<NativeLobbyParticipant>{new NativeLobbyParticipant{Name="Air",Team=1,Color=0,Difficulty=AiDifficulty.Fighter},new NativeLobbyParticipant{Name="Human",Human=true,Team=2,Color=1}}};
        Assert.IsNull(lobby.Validate(p,true,true));
        var basis=NativeLobbyMatch.Create(p,lobby);
        var routeCosts=new double[basis.Starts.Count,basis.Starts.Count];
        if(currentLobbyRoutes)for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)routeCosts[a,b]=basis.RouteCost(a,b);
        var config=new OfflineMatchConfiguration(p,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,seed,new[]{new OfflineParticipant("air-owner",1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),routeCosts,spectators:new[]{PlayableRuntime.LocalSpectatorId},terrain:p.AuthoredMap.Id);
        if(currentLobbyRoutes)Assert.IsEmpty(config.ScenarioUnits,"Ordinary birth must not import or pre-spawn a Hawk.");
        var pick=PlayableAiOpeningComposition.Initialize(seed,"air-owner");
        TestContext.WriteLine("O_WITNESS_PRECONDITION seed="+seed+" ai="+pick.ProfileIdentity+" opening="+pick.Opening+" map="+config.MapIdentity+" backend="+p.UsesRecastGroundRoutes+" currentLobbyRoutes="+currentLobbyRoutes);
        Assert.AreEqual(PlayableAiOpening.Air,pick.Opening,"fixed witness seed must exercise ordinary air selection");
        PlayableRuntime runtime=null;UnityHostRouteService routes=null;
        string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/hawk-current/recorded-"+Guid.NewGuid().ToString("N")));
        var diagnostic=new List<object>();var financeSeen=new HashSet<string>();int diagnosticDropped=0;
        var previousPreselection=PlayableAiOwnerLoop.PreselectionReadbackForTests;
        var previousPreselectionFilter=PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests;
        var previousFinance=AiExpansionFinancial.ActualLifecycleReadbackForTests;
        var previousFinanceFilter=AiExpansionFinancial.ActualLifecycleReadbackOwnerFilterForTests;
        void Record(object row){if(diagnostic.Count<512)diagnostic.Add(row);else diagnosticDropped++;}
        System.Threading.Tasks.Task<byte[]> capture=null;
        var births=new Dictionary<int,NavPoint>();var moved=new HashSet<int>();var released=new HashSet<int>();bool departing=false;bool hangar=false,factory=false,army=false;int maxHawks=0;long report=-1;
        try
        {
            Assert.IsNull(TestMatchLogSession.CurrentDirectory,"This witness owns its isolated recorder.");
            TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));
            Assert.IsNotNull(TestMatchLogSession.CurrentDirectory,"Capture ordinary owner checkpoints from Runtime construction.");
            TestContext.WriteLine("O_WITNESS_LOGS "+TestMatchLogSession.CurrentDirectory);
            TestContext.WriteLine("O_WITNESS_OBSERVER standardRecorder=true admissionHooks="+admissionDiagnostics+" diagnosticCapture="+admissionDiagnostics);
            if(currentLobbyRoutes&&admissionDiagnostics)
            {
                PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests="air-owner";
                PlayableAiOwnerLoop.PreselectionReadbackForTests=r=>
                {
                    var o=r.Observation;
                    Record(new{Stage="preselection",o.Tick,o.Credits,r.ProtectedFunds,r.FinanceSite,r.FinanceReady,r.Threatened,r.UrgentRepair,r.AllowsExpansion,
                        Sites=o.Sites.ToArray(),Objectives=o.PublicScoutObjectives.ToArray(),Routes=o.RouteProofs.ToArray(),
                        Units=o.Entities.Where(u=>u.Owner==o.Owner).ToArray(),r.ArmyActors,r.PendingActors,r.Ledger,
                        r.ActualExpansionAction,r.ActualExpansionProposal,r.CounterfactualAlternative,r.CounterfactualProposal,r.ExpansionState});
                };
                AiExpansionFinancial.ActualLifecycleReadbackOwnerFilterForTests="air-owner";
                AiExpansionFinancial.ActualLifecycleReadbackForTests=(owner,tick,stage,site,previous,current,ledger)=>
                {
                    string key=stage+":"+site+":"+(previous?.Amount??-1)+":"+(current?.Amount??-1)+":"+tick/900;
                    if(financeSeen.Add(key))Record(new{Stage=stage,Tick=tick,Site=site,Previous=previous,Current=current,Ledger=ledger});
                };
            }
            runtime=new PlayableRuntime(config,1,spectator:true);routes=new UnityHostRouteService();
            runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);float deadline=Time.realtimeSinceStartup+wallSeconds;
            while(runtime.Latest.Tick<12000&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)
            {
                routes.Service(runtime,p.NavigationRequestsPerFrame);
                if(currentLobbyRoutes&&admissionDiagnostics&&capture==null&&runtime.Latest.Tick>=6000)capture=runtime.RequestCaptureBytes();
                var f=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);
                var own=f.Overview.Entities.Where(u=>u.Owner==PlayableOwner.Player&&u.Health>0).ToArray();
                var bs=f.Overview.Buildings.Where(b=>b.Owner==PlayableOwner.Player&&b.Health>0&&b.Phase==ConstructionPhase.Ready).ToArray();
                hangar|=bs.Any(b=>b.Kind==PlayableBuildingKind.Hangar);factory|=bs.Any(b=>b.Kind==PlayableBuildingKind.Factory);
                var hawks=own.Where(u=>u.Kind==PlayableEntityKind.Hawk).ToArray();maxHawks=Math.Max(maxHawks,hawks.Length);departing|=hawks.Any(u=>u.Departing);foreach(var u in hawks.Where(u=>!u.Departing))released.Add(u.Id);
                foreach(var u in hawks){if(!births.ContainsKey(u.Id))births.Add(u.Id,u.Position);else if(Math.Sqrt(Math.Pow(u.Position.X-births[u.Id].X,2)+Math.Pow(u.Position.Z-births[u.Id].Z,2))>p.HawkRadius)moved.Add(u.Id);}
                // Published mission and stamped owner commands, beyond release/rally motion.
                army|=f.Players.Any(v=>v.Owner==PlayableOwner.Player&&v.Entities.Any(u=>u.Kind==PlayableEntityKind.Hawk&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&(u.OrderStamp.Kind==PlayableCommandKind.Attack||u.OrderStamp.Kind==PlayableCommandKind.AttackMove)));
                if(runtime.Latest.Tick/900!=report){report=runtime.Latest.Tick/900;TestContext.WriteLine("O_WITNESS tick="+runtime.Latest.Tick+" ready="+string.Join(",",bs.Select(b=>b.Kind))+" hawks="+hawks.Length+" moved="+moved.Count+" army="+army+" credits="+f.Players.First(v=>v.Owner==PlayableOwner.Player).Credits);}
                if(hangar&&maxHawks>=3&&moved.Count>=3&&army&&factory)break;
                yield return null;
            }
            TestContext.WriteLine("O_WITNESS_RESULT tick="+runtime.Latest.Tick+" hangar="+hangar+" maxHawks="+maxHawks+" moved="+moved.Count+" army="+army+" factory="+factory+" departingObserved="+departing+" released="+released.Count+" failure="+runtime.Latest.Failure);
            Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
            Assert.True(hangar,"ordinary AI built Hangar");Assert.GreaterOrEqual(maxHawks,3,"ordinary QueueHawk released first wave");Assert.GreaterOrEqual(moved.Count,3,"actual airborne motion");Assert.True(army,"real army attack command, not spawn/rally");Assert.True(factory,"solo air transitions to Factory");
            if(currentLobbyRoutes){Assert.True(departing,"Observe actual Hangar flight departure after production.");Assert.GreaterOrEqual(released.Count,3,"Produced Hawks complete ordinary departure before army service.");}
        }
        finally
        {
            try
            {
                if(runtime!=null)
                {
                    runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,3000));
                    routes?.Dispose();runtime.Dispose();
                    // The authority is stopped before accessing its owned native resources.
                    var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
                    authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();
                }
            }
            finally
            {
                routes?.Dispose();TestMatchLogSession.Shutdown();
                PlayableAiOwnerLoop.PreselectionReadbackForTests=previousPreselection;
                PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=previousPreselectionFilter;
                AiExpansionFinancial.ActualLifecycleReadbackForTests=previousFinance;
                AiExpansionFinancial.ActualLifecycleReadbackOwnerFilterForTests=previousFinanceFilter;
                if(currentLobbyRoutes&&admissionDiagnostics)
                {
                    File.WriteAllText(Path.Combine(output,"admission.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Dropped=diagnosticDropped,Rows=diagnostic},Newtonsoft.Json.Formatting.Indented));
                    if(capture!=null&&capture.Status==System.Threading.Tasks.TaskStatus.RanToCompletion)File.WriteAllBytes(Path.Combine(output,"current.bin"),capture.Result);
                    TestContext.WriteLine("O_WITNESS_ADMISSION "+output+" rows="+diagnostic.Count+" dropped="+diagnosticDropped+" current="+capture?.Status);
                }
            }
        }
    }
}
