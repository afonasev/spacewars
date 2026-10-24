using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class NativeAiLongGrowthPlayModeTests
{
    // Optional test-instance observer, called once after the ordinary loop; never installed in runtime.
    internal Action<PlayableAuthorityTick,PlayableRuntime> TerminalReadbackForTests;
    [UnityTest,Timeout(300000)] public IEnumerator OrdinaryCrossingsDevelopsUsefulAdditionalProductionAndScience()
        =>CrossingsGrowth(AiDifficulty.Veteran);
    [UnityTest,Timeout(300000)] public IEnumerator HumanSettingsCrossingsTwoFightersDevelopUsefulProductionAndScience()
        =>CrossingsGrowth(AiDifficulty.Fighter);
    private IEnumerator CrossingsGrowth(AiDifficulty secondDifficulty)
    {
        var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),
            new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
        var lobby=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=-515260688,Participants=new List<NativeLobbyParticipant>{
            new NativeLobbyParticipant{Name="Fighter",Team=1,Color=0,Difficulty=AiDifficulty.Fighter},
            new NativeLobbyParticipant{Name=secondDifficulty.ToString(),Team=2,Color=1,Difficulty=secondDifficulty}}};
        TestContext.WriteLine("AI_GROWTH_CONFIG map=ThreeCrossings seed="+lobby.ExplicitSeed+" difficulties=Fighter,"+secondDifficulty);
        Assert.IsNull(lobby.Validate(profile,true,true));var runtime=PlayableRuntime.CreateLobbyMatch(profile,1,lobby);var routes=new UnityHostRouteService();
        var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
        long reached=-1,lastReport=-1;var births=new Dictionary<int,NavPoint>();var moved=new HashSet<int>();bool earlyPassed=false;long firstIncome=-1;
        try
        {
            runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);float deadline=Time.realtimeSinceStartup+280;
            while(runtime.Latest.Tick<12000&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)
            {
                routes.Service(runtime,profile.NavigationRequestsPerFrame);var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);
                foreach(var unit in frame.Overview.Entities.Where(u=>u.Kind==PlayableEntityKind.Tank||u.Kind==PlayableEntityKind.Shkval))
                {
                    if(!births.TryGetValue(unit.Id,out var origin))births.Add(unit.Id,unit.Position);
                    else if(Math.Sqrt(Math.Pow(unit.Position.X-origin.X,2)+Math.Pow(unit.Position.Z-origin.Z,2))>PlayableUnitRules.Radius(profile,unit.Kind))moved.Add(unit.Id);
                }
                if(firstIncome<0&&frame.Overview.Buildings.Any(b=>(b.Kind==PlayableBuildingKind.Mine||b.Kind==PlayableBuildingKind.Outpost)&&b.Health>0&&b.Phase==ConstructionPhase.Ready))firstIncome=frame.Overview.Tick;
                if(!earlyPassed&&frame.Overview.Tick>=6000)
                {
                    TestContext.WriteLine("AI_GROWTH_EARLY tick="+frame.Overview.Tick+" moved="+string.Join(",",moved.OrderBy(x=>x))+" firstIncome="+firstIncome);
                    Assert.GreaterOrEqual(moved.Count,3);Assert.GreaterOrEqual(firstIncome,0);earlyPassed=true;
                }
                foreach(var view in frame.Players)
                {
                    var owned=frame.Overview.Buildings.Where(b=>b.Owner==view.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready).ToArray();
                    if(owned.Count(b=>b.Kind==PlayableBuildingKind.Factory)>=2&&owned.Any(b=>b.Kind==PlayableBuildingKind.ScientificCenter)&&
                        view.OwnerResearch.Any(r=>r.Active||r.Complete))reached=frame.Overview.Tick;
                }
                if(frame.Overview.Tick/600!=lastReport)
                {
                    lastReport=frame.Overview.Tick/600;
                    TestContext.WriteLine("AI_GROWTH tick="+frame.Overview.Tick+" owners="+string.Join(";",frame.Players.Select(p=>p.OwnerId+" bank="+p.Credits+" buildings="+
                        string.Join(",",frame.Overview.Buildings.Where(b=>b.Owner==p.Owner).GroupBy(b=>b.Kind).Select(g=>g.Key+"="+g.Count())))));
                    foreach(var c in authority.CaptureDiagnosticCheckpoints())
                        TestContext.WriteLine("AI_GROWTH_DECISION owner="+c.OwnerId+" tick="+c.DecisionTick+" opening="+c.Opening?.Opening+" phase="+c.OpeningExecution?.Phase+" bank="+c.EconomyDecision?.Liquid+" available="+c.EconomyDecision?.Available+" scaling="+c.EconomyDecision?.ProductionCapacity?.ScalingReason+" blockers="+string.Join(" | ",c.EconomyDecision?.Blockers??Array.Empty<string>()));
                }
                if(reached>=0&&earlyPassed)break;
                yield return null;
            }
            TestContext.WriteLine("AI_GROWTH_RESULT tick="+runtime.Latest.Tick+" reached="+reached+" failure="+runtime.Latest.Failure);
            TerminalReadbackForTests?.Invoke(authority,runtime);
            Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
            Assert.True(earlyPassed,"The current profile must also complete the early movement/capture interval.");
            Assert.GreaterOrEqual(reached,0,"An ordinary owner must develop additional live production, a Science center and real research by the bounded long-game horizon.");
        }
        finally{runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,3000));routes.Dispose();runtime.Dispose();}
    }
}
