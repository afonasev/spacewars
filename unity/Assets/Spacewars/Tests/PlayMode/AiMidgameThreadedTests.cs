using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;
public sealed class AiMidgameThreadedTests
{
    [Test] public void SelectOrdinaryCombinedCoverageSeedWithoutRunningGame()
    {
        var ai=NativeBalanceFields.InitialAiOpeningProfile();
        for(int seed=881000;seed<981000;seed++)
        {
            var o=PlayableAiOpeningComposition.Initialize(seed,"air-owner",aiProfile:ai);
            double Roll(string key)=>AiRandom.Key(seed,"air-owner",o.ProfileIdentity,key,0)/4294967296d;
            if(o.Opening!=PlayableAiOpening.Air||AiDoctrinePolicy.InitialPersonality(o,ai,AiDifficulty.Veteran)!=AiPersonality.Harassing||Roll("solo-air-income")<ai.Value("openingsMidgame.greedyAirShare")||Roll("first-doctrine")<.3||Roll("first-doctrine")>=.6)continue;
            if(PlayableAiOpeningComposition.Initialize(seed,"ground-owner",aiProfile:ai).Opening==PlayableAiOpening.Air)continue;
            TestContext.WriteLine("MP_PREFLIGHT "+Newtonsoft.Json.JsonConvert.SerializeObject(new{seed,hash=ai.Hash,personality="Harassing",doctrine="HawksExplorers",horizon=18000,timeoutSeconds=420,enemyOpening=PlayableAiOpeningComposition.Initialize(seed,"ground-owner",aiProfile:ai).Opening}));return;
        }
        Assert.Fail("No coverage seed in declared finite preflight search");
    }
    [UnityTest,Timeout(450000)] public IEnumerator OrdinaryMixedArmyMovesAfterCommandAndFires()
    {
        const int seed=881549;
        var ai=NativeBalanceFields.InitialAiOpeningProfile();var pick=PlayableAiOpeningComposition.Initialize(seed,"air-owner",aiProfile:ai);
        Assert.AreEqual(ai.Id+"@"+ai.Revision+":"+ai.Hash,pick.ProfileIdentity);Assert.AreEqual("bf9c04b0adf979abacea2e867c0133e5f7f602e3c6adec8f5ff8429bb8cd98cb",ai.Hash);Assert.AreEqual(PlayableAiOpening.Air,pick.Opening);Assert.AreEqual(AiPersonality.Harassing,AiDoctrinePolicy.InitialPersonality(pick,ai,AiDifficulty.Veteran));
        var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
        var lobby=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=seed,Participants=new List<NativeLobbyParticipant>{new NativeLobbyParticipant{Name="Air",Team=1,Color=0,Difficulty=AiDifficulty.Veteran},new NativeLobbyParticipant{Name="Ground",Team=2,Color=1,Difficulty=AiDifficulty.Fighter}}};
        Assert.IsNull(lobby.Validate(p,true,true));var basis=NativeLobbyMatch.Create(p,lobby);
        var config=new OfflineMatchConfiguration(p,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,seed,new[]{new OfflineParticipant("air-owner",1,1,OfflineControl.Ai),new OfflineParticipant("ground-owner",2,2,OfflineControl.Ai)},basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),new double[basis.Starts.Count,basis.Starts.Count],spectators:new[]{PlayableRuntime.LocalSpectatorId},terrain:p.AuthoredMap.Id);
        var runtime=new PlayableRuntime(config,1,participantDifficulties:new[]{AiDifficulty.Veteran,AiDifficulty.Fighter},spectator:true,aiProfile:ai);var routes=new UnityHostRouteService();
        var baselines=new Dictionary<int,(NavPoint position,NavPoint target,long tick,long action)>();var advanced=new HashSet<int>();var seenProjectiles=new HashSet<int>();bool effect=false,factory=false,hangar=false,mixed=false,groundArmy=false;long report=-1;PlayableAiOwnerCheckpoint last=null;
        try
        {
            runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);float deadline=Time.realtimeSinceStartup+420;
            while(runtime.Latest.Tick<18000&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)
            {
                routes.Service(runtime,p.NavigationRequestsPerFrame);var f=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);var own=f.Players.Single(v=>v.Owner==PlayableOwner.Player);var live=own.Entities.Where(u=>u.Owner==own.Owner&&u.Health>0).ToArray();
                factory|=own.Buildings.Any(b=>b.Owner==own.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready&&b.Health>0);hangar|=own.Buildings.Any(b=>b.Owner==own.Owner&&b.Kind==PlayableBuildingKind.Hangar&&b.Phase==ConstructionPhase.Ready&&b.Health>0);
                foreach(var u in live.Where(u=>u.Kind==PlayableEntityKind.Hawk&&!u.Departing&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&u.OrderStamp.Generation==runtime.Generation&&u.OrderStamp.Source==PlayableAiOpeningComposition.SourceIdentity&&(u.OrderStamp.Kind==PlayableCommandKind.Attack||u.OrderStamp.Kind==PlayableCommandKind.AttackMove)&&u.CurrentOrder!=null))
                {
                    NavPoint goal=u.CurrentOrder.Destination;
                    var target=f.Overview.Entities.FirstOrDefault(e=>e.Id==u.CurrentOrder.TargetId);var building=f.Overview.Buildings.FirstOrDefault(b=>b.Id==u.CurrentOrder.TargetId);if(target!=null)goal=target.Position;else if(building!=null)goal=building.Position;
                    if(!baselines.ContainsKey(u.Id))baselines[u.Id]=(u.Position,goal,f.Overview.Tick,u.OrderStamp.ActionId);
                    else {var start=baselines[u.Id];double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));if(f.Overview.Tick>start.tick&&Distance(start.position,u.Position)>p.HawkRadius&&Distance(u.Position,start.target)<Distance(start.position,start.target)-p.HawkRadius)advanced.Add(u.Id);}
                }
                effect|=f.Overview.Projectiles.Any(projectile=>projectile.Kind==PlayableEntityKind.Hawk&&advanced.Contains(projectile.OwnerId)&&!seenProjectiles.Contains(projectile.Id));foreach(var projectile in f.Overview.Projectiles)seenProjectiles.Add(projectile.Id);
                if(f.Overview.Tick/900!=report)
                {
                    report=f.Overview.Tick/900;var capture=runtime.RequestCaptureBytes();while(!capture.IsCompleted){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}Assert.False(capture.IsFaulted,capture.Exception?.ToString());
                    var captureFolder=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.local/ai-openings-midgame/mp-witness-captures"));System.IO.Directory.CreateDirectory(captureFolder);
                    using(var state=PlayableWorldState.Decode(capture.Result)){var name=state.Tick.ToString();System.IO.File.WriteAllBytes(System.IO.Path.Combine(captureFolder,name+".bin"),capture.Result);using var sha=System.Security.Cryptography.SHA256.Create();System.IO.File.WriteAllText(System.IO.Path.Combine(captureFolder,name+".sha256"),BitConverter.ToString(sha.ComputeHash(capture.Result)).Replace("-","").ToLowerInvariant());}
                    var snapshot=PlayableAuthorityTick.RestoreBytes(capture.Result,config,ai);last=snapshot.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="air-owner");snapshot.Stop();snapshot.Domain.Navigation.DestroyNavigationAuthority();
                    mixed|=last.Doctrine.Chosen&&last.Doctrine.Mixed&&last.Doctrine.Doctrine==AiDoctrine.HawksExplorers;
                    var military=last.Armies.Armies.Where(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded).SelectMany(a=>a.Members).ToHashSet();groundArmy|=live.Count(u=>u.Kind!=PlayableEntityKind.Hawk&&military.Contains(u.Id))>=2;
                    TestContext.WriteLine("MP_WITNESS "+Newtonsoft.Json.JsonConvert.SerializeObject(new{f.Overview.Tick,hangar,factory,mixed,groundArmy,advanced=advanced.ToArray(),effect,last.Doctrine,last.ArmyMission,Units=live.Select(u=>new{u.Id,u.Kind,u.Position,u.CurrentOrder,u.OrderStamp})}));
                }
                if(hangar&&factory&&mixed&&groundArmy&&advanced.Count>0&&effect)break;yield return null;
            }
            TestContext.WriteLine("MP_RESULT "+Newtonsoft.Json.JsonConvert.SerializeObject(new{runtime.Latest.Tick,hangar,factory,mixed,groundArmy,advanced=advanced.ToArray(),effect,runtime.Latest.Failure}));
            Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);Assert.True(hangar);Assert.True(factory);Assert.True(mixed);Assert.True(groundArmy,"At least two actual ground combat army members, excluding utility scouts");Assert.Greater(advanced.Count,0,"Physical progress toward military target after non-departing AI-command observation");Assert.True(effect,"A tracked, post-command advanced Hawk actually fired");
        }
        finally{runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,3000));routes.Dispose();runtime.Dispose();}
    }
    [UnityTest,Explicit("One bounded incident diagnostic; results are evidence, not a regression acceptance gate"),Timeout(90000)] public IEnumerator BoundedThreadedSpatialReceiptDiagnosis()
    {
        const int seed=881549;var ai=NativeBalanceFields.InitialAiOpeningProfile();
        var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
        var lobby=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=seed,Participants=new List<NativeLobbyParticipant>{new NativeLobbyParticipant{Name="Air",Team=1,Color=0,Difficulty=AiDifficulty.Veteran},new NativeLobbyParticipant{Name="Ground",Team=2,Color=1,Difficulty=AiDifficulty.Fighter}}};
        Assert.IsNull(lobby.Validate(p,true,true));var basis=NativeLobbyMatch.Create(p,lobby);
        var config=new OfflineMatchConfiguration(p,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,seed,new[]{new OfflineParticipant("air-owner",1,1,OfflineControl.Ai),new OfflineParticipant("ground-owner",2,2,OfflineControl.Ai)},basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),new double[basis.Starts.Count,basis.Starts.Count],spectators:new[]{PlayableRuntime.LocalSpectatorId},terrain:p.AuthoredMap.Id);
        var output=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.local/ai-openings-midgame/mp-threaded-state-diagnosis"));System.IO.Directory.CreateDirectory(output);
        System.IO.File.WriteAllText(System.IO.Path.Combine(output,"config.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{seed,RuntimeSource="1fef9d7f64a60b2f7f35372920f7a097a4f91962",p.ProfileId,p.Revision,ai.Id,ai.Hash,AiData=ai.CopyData(),config.SourceIdentity,config.MapIdentity,config.RouteProvenance,Starts=config.Starts,Sites=config.Sites,Obstacles=config.Obstacles,Costs=new double[basis.Starts.Count,basis.Starts.Count],Terrain=p.AuthoredMap.Id,Owners=config.Roster,Difficulties=new[]{AiDifficulty.Veteran,AiDifficulty.Fighter},ProfileResource=Resources.Load<TextAsset>("PlayableProfile").text,MapResource=Resources.Load<TextAsset>("ThreeCrossingsProfile").text},Newtonsoft.Json.Formatting.Indented));
        var runtime=new PlayableRuntime(config,1,participantDifficulties:new[]{AiDifficulty.Veteran,AiDifficulty.Fighter},spectator:true,aiProfile:ai);var routes=new UnityHostRouteService();string operands=null;
        runtime.BeforeCaptureSerialization=()=>{
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(runtime);var domain=authority.Domain;
            var receipts=(System.Collections.IDictionary)typeof(PlayableDomain).GetField("spatialCompletions",flags).GetValue(domain);var units=(System.Collections.IDictionary)typeof(PlayableDomain).GetField("units",flags).GetValue(domain);var rows=new List<object>();
            foreach(System.Collections.DictionaryEntry row in receipts){var receipt=(PlayableSpatialCompletion)row.Value;var group=domain.Navigation.GroupFor(receipt.Order.UnitId);var member=group?.Members.SingleOrDefault(m=>m.Entity==receipt.Order.UnitId);var last=(PlayableOrderStamp)units[receipt.Order.UnitId].GetType().GetField("LastOrder",flags).GetValue(units[receipt.Order.UnitId]);rows.Add(new{Receipt=receipt,LastOrder=last,Group=group,Member=member,OriginalValid=p.AuthoredMap.IsValidLocation(receipt.OriginalLocation,0),AssignedValid=p.AuthoredMap.IsValidLocation(receipt.AssignedLocation,0),OriginalEqual=group?.OriginalLocation.Equals(receipt.OriginalLocation),AssignedEqual=member?.GoalLocation.Equals(receipt.AssignedLocation),ClockValid=receipt.CompletionTick>=receipt.ActivationTick&&receipt.CompletionTick<=authority.Tick,authority.Tick});}
            operands=Newtonsoft.Json.JsonConvert.SerializeObject(rows,Newtonsoft.Json.Formatting.Indented);
        };
        try
        {
            runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);float deadline=Time.realtimeSinceStartup+60;long next=90;
            while(runtime.Latest.Tick<=950&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)
            {
                routes.Service(runtime,p.NavigationRequestsPerFrame);
                if(runtime.Latest.Tick>=next)
                {
                    var capture=runtime.RequestCaptureBytes();while(!capture.IsCompleted&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}Assert.True(capture.IsCompleted,"60s diagnostic cap");Assert.False(capture.IsFaulted,capture.Exception?.ToString());
                    using var state=PlayableWorldState.Decode(capture.Result);var name=state.Tick.ToString();System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,name+".bin"),capture.Result);System.IO.File.WriteAllText(System.IO.Path.Combine(output,name+"-operands.json"),operands??"[]");
                    using var hash=System.Security.Cryptography.SHA256.Create();System.IO.File.WriteAllText(System.IO.Path.Combine(output,name+".sha256"),BitConverter.ToString(hash.ComputeHash(capture.Result)).Replace("-","").ToLowerInvariant());TestContext.WriteLine("THREADED_RECEIPT_CAPTURE tick="+state.Tick+" path="+System.IO.Path.Combine(output,name+".bin")+" operands="+operands);
                    var restored=PlayableAuthorityTick.RestoreBytes(capture.Result,config,ai);restored.Stop();restored.Domain.Navigation.DestroyNavigationAuthority();next+=90;
                }
                yield return null;
            }
            TestContext.WriteLine("DIAGNOSTIC_MIXED no failing restore within tick="+runtime.Latest.Tick+" wall60cap; no repeat");Assert.Inconclusive("MIXED: original failure not reproduced in one bounded real-threaded diagnostic");
        }
        finally{runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,3000));routes.Dispose();runtime.Dispose();}
    }
}
