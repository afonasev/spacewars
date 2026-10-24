using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class NativeAiHumanReworkPlayModeTests
{
    // Ordinary threaded lobby/host routes. No fixture funds, injected orders,
    // forced opening, reduced navigation work or hidden observation authority.
    [UnityTest,Timeout(300000)] public IEnumerator OrdinaryCrossingsMovesCombatUnitsAndSettlesIncome()
    {
        var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),
            new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
        var lobby=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=-515260688,Participants=new List<NativeLobbyParticipant>{
            new NativeLobbyParticipant{Name="Fighter",Team=1,Color=0,Difficulty=AiDifficulty.Fighter},
            new NativeLobbyParticipant{Name="Veteran",Team=2,Color=1,Difficulty=AiDifficulty.Veteran}}};
        Assert.IsNull(lobby.Validate(profile,true,true));
        var runtime=PlayableRuntime.CreateLobbyMatch(profile,1,lobby);var routes=new UnityHostRouteService();
        var births=new Dictionary<int,NavPoint>();var moved=new HashSet<int>();long firstCombat=-1,firstIncome=-1,lastReport=-1;
        try
        {
            runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);float deadline=Time.realtimeSinceStartup+280;
            while(runtime.Latest.Tick<6000&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)
            {
                routes.Service(runtime,profile.NavigationRequestsPerFrame);var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);var view=frame.Overview;
                foreach(var unit in view.Entities.Where(u=>u.Kind==PlayableEntityKind.Tank||u.Kind==PlayableEntityKind.Shkval))
                {
                    if(!births.TryGetValue(unit.Id,out var origin)){births.Add(unit.Id,unit.Position);if(firstCombat<0)firstCombat=view.Tick;}
                    else if(Math.Sqrt(Math.Pow(unit.Position.X-origin.X,2)+Math.Pow(unit.Position.Z-origin.Z,2))>PlayableUnitRules.Radius(profile,unit.Kind))moved.Add(unit.Id);
                }
                if(firstIncome<0&&view.Buildings.Any(b=>(b.Kind==PlayableBuildingKind.Mine||b.Kind==PlayableBuildingKind.Outpost)&&b.Health>0&&b.Phase==ConstructionPhase.Ready))firstIncome=view.Tick;
                if(view.Tick/600!=lastReport)
                {
                    lastReport=view.Tick/600;
                    TestContext.WriteLine("AI_REWORK tick="+view.Tick+" combatBorn="+births.Count+" moved="+string.Join(",",moved.OrderBy(x=>x))+" firstIncome="+firstIncome+" buildings="+
                        string.Join(";",view.Buildings.GroupBy(b=>b.Owner).Select(g=>g.Key+":"+string.Join(",",g.GroupBy(b=>b.Kind).Select(k=>k.Key+"="+k.Count()))))+" routeFailure="+routes.LastProjectionFailure);
                }
                yield return null;
            }
            TestContext.WriteLine("AI_REWORK_RESULT tick="+runtime.Latest.Tick+" firstCombat="+firstCombat+" firstIncome="+firstIncome+" combatBorn="+births.Count+" moved="+string.Join(",",moved.OrderBy(x=>x))+" failure="+runtime.Latest.Failure);
            Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
            Assert.GreaterOrEqual(runtime.Latest.Tick,6000,"Complete the actual threaded interval, not only an accepted command.");
            Assert.GreaterOrEqual(moved.Count,3,"Actual combat bodies must move beyond their birth radius.");
            Assert.GreaterOrEqual(firstIncome,0,"At least one ordinary safe income center must be settled in the opening/early game.");
        }
        finally
        {
            runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,3000));routes.Dispose();runtime.Dispose();
        }
    }
}
