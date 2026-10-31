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
public sealed class AiAirOpeningThreadedTests
{
    [UnityTest,Timeout(320000)] public IEnumerator OrdinarySoloAirBuildsLaunchesAndMovesArmy()
    {
        const int seed=771205;
        var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
        var lobby=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=seed,Participants=new List<NativeLobbyParticipant>{new NativeLobbyParticipant{Name="Air",Team=1,Color=0,Difficulty=AiDifficulty.Fighter},new NativeLobbyParticipant{Name="Human",Human=true,Team=2,Color=1}}};
        Assert.IsNull(lobby.Validate(p,true,true));
        var basis=NativeLobbyMatch.Create(p,lobby);
        var config=new OfflineMatchConfiguration(p,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,seed,new[]{new OfflineParticipant("air-owner",1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),new double[basis.Starts.Count,basis.Starts.Count],spectators:new[]{PlayableRuntime.LocalSpectatorId},terrain:p.AuthoredMap.Id);
        var pick=PlayableAiOpeningComposition.Initialize(seed,"air-owner");
        TestContext.WriteLine("O_WITNESS_PRECONDITION seed="+seed+" ai="+pick.ProfileIdentity+" opening="+pick.Opening);
        Assert.AreEqual(PlayableAiOpening.Air,pick.Opening,"fixed witness seed must exercise ordinary air selection");
        var runtime=new PlayableRuntime(config,1,spectator:true);var routes=new UnityHostRouteService();
        var births=new Dictionary<int,NavPoint>();var moved=new HashSet<int>();bool hangar=false,factory=false,army=false;int maxHawks=0;long report=-1;
        try
        {
            runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);float deadline=Time.realtimeSinceStartup+300;
            while(runtime.Latest.Tick<12000&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)
            {
                routes.Service(runtime,p.NavigationRequestsPerFrame);var f=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);
                var own=f.Overview.Entities.Where(u=>u.Owner==PlayableOwner.Player&&u.Health>0).ToArray();
                var bs=f.Overview.Buildings.Where(b=>b.Owner==PlayableOwner.Player&&b.Health>0&&b.Phase==ConstructionPhase.Ready).ToArray();
                hangar|=bs.Any(b=>b.Kind==PlayableBuildingKind.Hangar);factory|=bs.Any(b=>b.Kind==PlayableBuildingKind.Factory);
                var hawks=own.Where(u=>u.Kind==PlayableEntityKind.Hawk).ToArray();maxHawks=Math.Max(maxHawks,hawks.Length);
                foreach(var u in hawks){if(!births.ContainsKey(u.Id))births.Add(u.Id,u.Position);else if(Math.Sqrt(Math.Pow(u.Position.X-births[u.Id].X,2)+Math.Pow(u.Position.Z-births[u.Id].Z,2))>p.HawkRadius)moved.Add(u.Id);}
                // Published mission and stamped owner commands, beyond release/rally motion.
                army|=f.Players.Any(v=>v.Owner==PlayableOwner.Player&&v.Entities.Any(u=>u.Kind==PlayableEntityKind.Hawk&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&(u.OrderStamp.Kind==PlayableCommandKind.Attack||u.OrderStamp.Kind==PlayableCommandKind.AttackMove)));
                if(runtime.Latest.Tick/900!=report){report=runtime.Latest.Tick/900;TestContext.WriteLine("O_WITNESS tick="+runtime.Latest.Tick+" ready="+string.Join(",",bs.Select(b=>b.Kind))+" hawks="+hawks.Length+" moved="+moved.Count+" army="+army+" credits="+f.Players.First(v=>v.Owner==PlayableOwner.Player).Credits);}
                if(hangar&&maxHawks>=3&&moved.Count>=3&&army&&factory)break;
                yield return null;
            }
            TestContext.WriteLine("O_WITNESS_RESULT tick="+runtime.Latest.Tick+" hangar="+hangar+" maxHawks="+maxHawks+" moved="+moved.Count+" army="+army+" factory="+factory+" failure="+runtime.Latest.Failure);
            Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
            Assert.True(hangar,"ordinary AI built Hangar");Assert.GreaterOrEqual(maxHawks,3,"ordinary QueueHawk released first wave");Assert.GreaterOrEqual(moved.Count,3,"actual airborne motion");Assert.True(army,"real army attack command, not spawn/rally");Assert.True(factory,"solo air transitions to Factory");
        }
        finally{runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,3000));routes.Dispose();runtime.Dispose();}
    }
}
