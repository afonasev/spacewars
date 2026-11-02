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

namespace Spacewars.Tests.PlayMode
{
    public sealed class AiRelayAuthoredOpeningTests
    {
        [UnityTest,Timeout(180000)] public IEnumerator OrdinaryCrossingsFirstUnitLeavesAndCaptures()
        {
            var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),
                new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
            var lobby=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=7112026,Participants=new List<NativeLobbyParticipant>{
                new NativeLobbyParticipant{Name="Relay AI",Team=1,Color=0,Difficulty=AiDifficulty.Fighter},
                new NativeLobbyParticipant{Name="Human",Team=2,Color=1,Human=true}}};
            var runtime=PlayableRuntime.CreateLobbyMatch(p,91,lobby,startPaused:true);var routes=new UnityHostRouteService();
            double deadline=Time.realtimeSinceStartupAsDouble+150;
            try
            {
                while(!runtime.IsPrepared&&Time.realtimeSinceStartupAsDouble<deadline){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.True(runtime.IsPrepared);
                var first=runtime.Latest.Entities.Where(u=>u.Owner==PlayableOwner.Player).OrderBy(u=>u.Id).First();
                runtime.RequestPause(false);long departure=-1;bool captured=false;
                while(runtime.Latest.Tick<1800&&Time.realtimeSinceStartupAsDouble<deadline)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);var v=runtime.Latest;Assert.IsNull(v.Failure);
                    var unit=v.Entities.FirstOrDefault(u=>u.Id==first.Id);
                    if(departure<0&&unit!=null&&AiOpponentSelector.Distance(first.Position,unit.Position)>PlayableUnitRules.Radius(p,first.Kind))departure=v.Tick;
                    captured|=v.Sites.Any(s=>s.Site.Kind==PlayableBuildingKind.RelayTower&&s.Owner==PlayableOwner.Player);
                    if(departure>=0&&captured)break;yield return null;
                }
                TestContext.WriteLine("AUTHORED_RELAY seed=7112026 tick="+runtime.Latest.Tick+" departure="+departure+" captured="+captured+" routeFailure="+routes.LastProjectionFailure);
                Assert.GreaterOrEqual(departure,0);Assert.Less(departure,300,"No economic/army wait in ordinary opening");Assert.True(captured,"First ordinary authored relay capture");Assert.IsNull(routes.LastProjectionFailure);
            }
            finally{runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000));runtime.Dispose();routes.Dispose();}
        }
    }
}
