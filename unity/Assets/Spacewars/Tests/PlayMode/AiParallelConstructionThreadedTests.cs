using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class AiParallelConstructionThreadedTests
{
    [UnityTest,Timeout(110000)] public IEnumerator OrdinaryOpeningOverlapsBuildingsAndScoutActuallyMoves()
    {
        const int seed=7;
        // Reconstruct the supported profile observed immediately before the
        // approved All-In change. Seed 7 selects Safe under the current defaults.
        // The original Oct 29 hash remains historical evidence of the older schema.
        var terms=NativeBalanceFields.InitialAiOpeningProfile().CopyData();
        terms.fields.Single(f=>f.path=="openingsMidgame.opening.allIn").value=10;
        terms.fields.Single(f=>f.path=="openingsMidgame.allInMax").value=5;
        var ai=new AiProfile(terms);
        Assert.AreEqual("a312fbcd55ee40747860818b4f2f96154a2d945c5e3403bfd3ece4fd5a75e1d4",ai.Hash);
        Assert.AreEqual(PlayableAiOpening.BlindRush,PlayableAiOpeningComposition.Initialize(seed,"parallel-owner",aiProfile:ai).Opening);
        var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
        Assert.AreEqual(500,p.StartingCredits,"ordinary default bank, no extra funding");
        var lobby=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=seed,Participants=new List<NativeLobbyParticipant>{new NativeLobbyParticipant{Name="AI",Team=1,Color=0,Difficulty=AiDifficulty.Veteran},new NativeLobbyParticipant{Name="Human",Team=2,Color=1}}};
        Assert.IsNull(lobby.Validate(p,true,true));var basis=NativeLobbyMatch.Create(p,lobby);
        var config=new OfflineMatchConfiguration(p,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,seed,new[]{new OfflineParticipant("parallel-owner",1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),new double[basis.Starts.Count,basis.Starts.Count],spectators:new[]{PlayableRuntime.LocalSpectatorId},terrain:p.AuthoredMap.Id);
        string output=Environment.GetEnvironmentVariable("AI_PARALLEL_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
        using var logs=new TestMatchLogSession(output,new TestMatchLoggingProfile(),new{Fixture=TestContext.CurrentContext.Test.Name,Seed=seed,AiHash=ai.Hash});
        var previousJournalFactory=PlayableRuntime.JournalFactory;
        PlayableRuntime.JournalFactory=c=>logs.CreateMatch(c);
        PlayableRuntime runtime=null;
        UnityHostRouteService routes=null;
        var events=new List<object>();var scouts=new Dictionary<int,(NavPoint position,NavPoint goal,long action,long tick)>();long overlap=-1,motion=-1,producer=-1;
        try{
            runtime=new PlayableRuntime(config,1,participantDifficulties:new[]{AiDifficulty.Veteran,AiDifficulty.Fighter},spectator:true,aiProfile:ai);
            routes=new UnityHostRouteService();
            runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);float deadline=Time.realtimeSinceStartup+90;
            while(runtime.Latest.Tick<1800&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline){
                routes.Service(runtime,p.NavigationRequestsPerFrame);var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);var own=frame.Players.Single(v=>v.Owner==PlayableOwner.Player);Assert.IsNull(runtime.Latest.Failure);
                var buildings=own.Buildings.Where(b=>b.Owner==PlayableOwner.Player&&(b.Health>0||b.Phase!=ConstructionPhase.Ready)).ToArray();
                Assert.GreaterOrEqual(own.Credits,0);
                bool refinery=buildings.Any(b=>b.Kind==PlayableBuildingKind.Refinery&&b.Phase!=ConstructionPhase.Ready);
                bool factory=buildings.Any(b=>b.Kind==PlayableBuildingKind.Factory&&b.Phase!=ConstructionPhase.Ready);
                if(overlap<0&&refinery&&factory){
                    overlap=frame.Overview.Tick;
                    Assert.AreEqual(1,buildings.Count(b=>b.Kind==PlayableBuildingKind.Refinery),"aggressive opening must not duplicate its income target");
                    Assert.AreEqual(1,buildings.Count(b=>b.Kind==PlayableBuildingKind.Factory));
                    events.Add(new{kind="overlapping-paid-construction",tick=overlap,own.Credits,buildings=buildings.Select(b=>new{b.Id,b.Kind,b.Phase,b.Progress,b.SiteId,b.SlotId}).ToArray()});
                }
                if(producer<0&&buildings.Any(b=>b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready)){producer=frame.Overview.Tick;events.Add(new{kind="producer-ready",tick=producer});}
                foreach(var u in own.Entities.Where(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Explorer&&u.Health>0&&u.OrderStamp!=null&&u.OrderStamp.Origin==PlayableOrderOrigin.Ai&&u.OrderStamp.Source==PlayableAiOpeningComposition.SourceIdentity&&u.CurrentOrder!=null&&(u.OrderStamp.Kind==PlayableCommandKind.Move||u.OrderStamp.Kind==PlayableCommandKind.AttackMove))){
                    if(!scouts.TryGetValue(u.Id,out var start)||start.action!=u.OrderStamp.ActionId){scouts[u.Id]=(u.Position,u.CurrentOrder.Destination,u.OrderStamp.ActionId,frame.Overview.Tick);events.Add(new{kind="scout-command",tick=frame.Overview.Tick,u.Id,u.Position,goal=u.CurrentOrder.Destination,u.OrderStamp.ActionId});}
                    else if(motion<0&&frame.Overview.Tick>start.tick){
                        double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
                        if(Distance(start.position,u.Position)>p.ExplorerCollisionRadius&&Distance(u.Position,start.goal)<Distance(start.position,start.goal)-p.ExplorerCollisionRadius){motion=frame.Overview.Tick;events.Add(new{kind="scout-command-to-motion",tick=motion,u.Id,u.Position,start=start.position,target=start.goal,start.action});}
                    }
                }
                if(overlap>=0&&motion>=0&&producer>=0)break;
                yield return null;
            }
            File.WriteAllText(Path.Combine(output,"ordinary-result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{seed,ai.Id,ai.Hash,runtime.Latest.Tick,overlap,motion,producer,failure=runtime.Latest.Failure,projectionFailure=routes.LastProjectionFailure,events},Newtonsoft.Json.Formatting.Indented));
            Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);Assert.GreaterOrEqual(overlap,0,"two necessary buildings must overlap before first Refinery completes");Assert.GreaterOrEqual(motion,0,"AI scout must physically progress after its real command");Assert.GreaterOrEqual(producer,0,"ordinary factory construction completes");
        }finally{
            try{
                if(runtime!=null){runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,3000));}
            }finally{
                routes?.Dispose();
                runtime?.Dispose();
                PlayableRuntime.JournalFactory=previousJournalFactory;
            }
        }
    }
}
