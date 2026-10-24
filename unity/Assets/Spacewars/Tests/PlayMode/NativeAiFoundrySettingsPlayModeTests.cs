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

public sealed class NativeAiFoundrySettingsPlayModeTests
{
    // Clarified manual settings: lava Foundry, six middle-difficulty bots,
    // approximately one simulation minute. Team layout uses the ordinary3v3 default.
    // This is early-match smoke, not a late-production/research acceptance test.
    [UnityTest,Timeout(300000)] public IEnumerator HumanSettingsFoundrySixFightersCompleteOneMinute()
    {
        var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),
            new FoundryMap(JsonUtility.FromJson<FoundryProfileData>(Resources.Load<TextAsset>("FoundryProfile").text)));
        var lobby=new NativeLobbyConfiguration{Foundry=true,HasExplicitSeed=true,ExplicitSeed=-1915648643,
            Participants=Enumerable.Range(0,6).Select(i=>new NativeLobbyParticipant{Name="Fighter "+(i+1),Team=i<3?1:2,Color=i,Difficulty=AiDifficulty.Fighter}).ToList()};
        Assert.True(lobby.Spectator);Assert.AreEqual(6,lobby.Capacity(profile));Assert.IsNull(lobby.Validate(profile,true,true));
        var runtime=PlayableRuntime.CreateLobbyMatch(profile,1,lobby);var routes=new UnityHostRouteService();
        var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
        var origins=new Dictionary<int,NavPoint>();var movedScouts=new HashSet<int>();var movedCombat=new HashSet<int>();var progressed=new HashSet<PlayableOwner>();long lastReport=-1;
        try
        {
            runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);float deadline=Time.realtimeSinceStartup+280;
            while(runtime.Latest.Tick<1800&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)
            {
                routes.Service(runtime,profile.NavigationRequestsPerFrame);var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);var view=frame.Overview;
                foreach(var u in view.Entities)
                {
                    if(!origins.TryGetValue(u.Id,out var origin))origins.Add(u.Id,u.Position);
                    else if(Math.Sqrt(Math.Pow(u.Position.X-origin.X,2)+Math.Pow(u.Position.Z-origin.Z,2))>PlayableUnitRules.Radius(profile,u.Kind))
                    {if(u.Kind==PlayableEntityKind.Explorer)movedScouts.Add(u.Id);else movedCombat.Add(u.Id);}
                }
                foreach(var b in view.Buildings.Where(b=>b.Health>0&&b.Phase==ConstructionPhase.Ready&&(b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.Refinery)))progressed.Add(b.Owner);
                if(view.Tick/300!=lastReport)
                {
                    lastReport=view.Tick/300;
                    TestContext.WriteLine("FOUNDRY_SIX tick="+view.Tick+" developedOwners="+string.Join(",",progressed.OrderBy(x=>x))+" movedScouts="+string.Join(",",movedScouts.OrderBy(x=>x))+" movedCombat="+string.Join(",",movedCombat.OrderBy(x=>x))+" routeFailure="+routes.LastProjectionFailure);
                }
                yield return null;
            }
            var checkpoints=authority.CaptureDiagnosticCheckpoints();
            TestContext.WriteLine("FOUNDRY_SIX_RESULT seed=-1915648643 teams=3v3 difficulties=6Fighter tick="+runtime.Latest.Tick+" developedOwners="+progressed.Count+" movedScouts="+movedScouts.Count+" movedCombat="+movedCombat.Count+" failure="+runtime.Latest.Failure);
            Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);Assert.GreaterOrEqual(runtime.Latest.Tick,1800);
            Assert.AreEqual(6,runtime.Latest.Participants.Count);Assert.True(runtime.Latest.Participants.All(p=>p.Control==OfflineControl.Ai));
            Assert.AreEqual(6,checkpoints.Length);Assert.True(checkpoints.All(c=>c.Difficulty==AiDifficulty.Fighter&&c.DecisionTick>0));
            Assert.AreEqual(6,progressed.Count,"Every owner must actually complete initial infrastructure.");Assert.Greater(movedScouts.Count,0,"A receipt alone is not actual early reconnaissance.");
        }
        finally{runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,3000));routes.Dispose();runtime.Dispose();}
    }
}
