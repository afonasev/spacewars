using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class AiOrbitalThreadedTests
    {
        [UnityTest,Timeout(90000)] public IEnumerator OrdinaryBotEarnsChoosesHacksAndCommandsCapturedUnit()
        {
            var data=PlayableProfile.Default.CopyData();data.orbitalThreshold1=1;data.tankWeaponDamage=60;data.tankWeaponReloadMilliseconds=10000;data.explorerHealth=100;
            var map=new AiTestMap();var profile=PlayableProfile.Create(data,map);
            var basis=map.Configuration(profile,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            var costs=new double[basis.Starts.Count,basis.Starts.Count];for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)costs[a,b]=basis.RouteCost(a,b);
            var config=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,7,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,
                scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-10,0)),new OfflineScenarioUnit(2,PlayableEntityKind.Explorer,new NavPoint(-3,0))},terrain:map.Id);
            var ad=AiProfile.Initial.CopyData();
            foreach(var f in ad.fields){if(f.path=="orbital.hackValueScale")f.value=5;if(f.path=="orbital.scanUtility"||f.path=="orbital.mineUtility")f.value=1;if(f.path=="orbital.mineContactBonus"||f.path=="orbital.personalityBonus"||f.path=="orbital.choiceVariation")f.value=0;}
            var ai=new AiProfile(ad);var runtime=new PlayableRuntime(config,906,participantDifficulties:new[]{AiDifficulty.Veteran,AiDifficulty.Fighter},aiProfile:ai,startPaused:true);var routes=new UnityHostRouteService();
            try
            {
                float deadline=Time.realtimeSinceStartup+60;
                while(!runtime.IsPrepared&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,profile.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);yield return null;}
                Assert.True(runtime.IsPrepared);int target=runtime.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Explorer&&e.Position.X<0).Id;
                runtime.RequestPause(false);bool captured=false,moved=false;NavPoint capturePoint=default(NavPoint);
                while(Time.realtimeSinceStartup<deadline)
                {
                    routes.Service(runtime,profile.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    var unit=runtime.Latest.Entities.FirstOrDefault(e=>e.Id==target&&e.Owner==PlayableOwner.Player);
                    if(unit!=null){if(!captured){capturePoint=unit.Position;captured=true;}if(unit.CurrentOrder!=null&&unit.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&AiOpponentSelector.Distance(capturePoint,unit.Position)>.1){moved=true;break;}}
                    yield return null;
                }
                TestContext.WriteLine("ORBITAL_RESULT tick="+runtime.Latest.Tick+" captured="+captured+" moved="+moved+" abilities="+string.Join(",",runtime.Latest.Orbital.Abilities.Select(a=>a.Id)));
                Assert.True(runtime.Latest.Orbital.Abilities.Any(a=>a.Id==OrbitalAbilities.ControlHack),"ordinary bot chooses hack");Assert.True(captured,"ordinary AI hack transfers ownership");Assert.True(moved,"captured actor receives and physically executes an AI order");
                var capture=runtime.RequestCaptureBytes();while(!capture.IsCompleted&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
                Assert.True(capture.IsCompleted);Assert.False(capture.IsFaulted,capture.Exception?.ToString());
                using(var state=PlayableWorldState.Decode(capture.Result))Assert.Greater(state.AiAuthority.Length,0,"current authority persists orbital decisions");
            }
            finally{runtime.RequestStop();routes.Dispose();runtime.Dispose();}
        }
        [UnityTest,Timeout(90000)] public IEnumerator OrdinaryBotScansAndLearnsPublicEnemyArea()=>Support(1);
        [UnityTest,Timeout(90000)] public IEnumerator OrdinaryBotPlacesMineAndDamagesGroundEnemy()=>Support(2);
        private IEnumerator Support(int ability)
        {
            var data=PlayableProfile.Default.CopyData();data.orbitalThreshold1=1;data.tankWeaponDamage=60;data.tankWeaponReloadMilliseconds=10000;data.explorerHealth=100;
            var map=new AiTestMap();var profile=PlayableProfile.Create(data,map);
            var basis=map.Configuration(profile,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            // A bounded causal scan witness has two PUBLIC possible starts. On the full
            // eight-start map finding a particular enemy in one scan cannot be required fairly.
            var starts=ability==1?basis.Starts.Take(2).ToArray():basis.Starts.ToArray();
            var costs=new double[starts.Length,starts.Length];for(int a=0;a<starts.Length;a++)for(int b=0;b<starts.Length;b++)costs[a,b]=basis.RouteCost(a,b);
            var config=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,7,basis.Roster.ToArray(),starts,basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,
                scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-10,0)),new OfflineScenarioUnit(2,PlayableEntityKind.Explorer,new NavPoint(-3,0))},terrain:map.Id);
            var ad=AiProfile.Initial.CopyData();foreach(var f in ad.fields){if(f.path=="orbital.hackValueScale")f.value=.01;if(f.path=="orbital.scanUtility")f.value=ability==1?1000:1;if(f.path=="orbital.mineUtility")f.value=ability==2?1000:1;if(f.path=="orbital.mineContactBonus")f.value=ability==2?1000:0;if(f.path=="orbital.personalityBonus"||f.path=="orbital.choiceVariation")f.value=0;}
            var runtime=new PlayableRuntime(config,908+ability,participantDifficulties:new[]{AiDifficulty.Veteran,AiDifficulty.Fighter},aiProfile:new AiProfile(ad),startPaused:true);var routes=new UnityHostRouteService();
            try
            {
                float deadline=Time.realtimeSinceStartup+60;
                while(!runtime.IsPrepared&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,profile.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);yield return null;}
                Assert.True(runtime.IsPrepared);var initial=PlayableAiObservation.From(runtime.Latest);var hidden=initial.PublicScoutObjectives.Where(t=>initial.Vision?.IsVisible(t.Approach)!=true).ToArray();
                // Roster runtime owns participant loops; legacy AiCheckpoint is intentionally null.
                var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
                PlayableAiOwnerCheckpoint Checkpoint()=>authority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="player-1");
                runtime.RequestPause(false);bool effect=false,learned=false;
                while(Time.realtimeSinceStartup<deadline)
                {
                    routes.Service(runtime,profile.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    if(ability==1)
                    {
                        var view=runtime.Latest;bool scanning=view.ScanLines.Count>0;effect|=scanning;
                        var checkpoint=Checkpoint();long applied=checkpoint.Records.Where(r=>r.Policy==AiOrbitalPolicy.Policy&&r.Kind==PlayableCommandKind.UseOrbital&&r.Status==PlayableAiDeliveryStatus.Applied).Select(r=>r.ApplicationTick).DefaultIfEmpty(-1).Max();
                        // Knowledge must arrive during the scan, before a regular scout can substitute.
                        learned|=scanning&&applied>=0&&checkpoint.Knowledge.Contacts.Any(c=>c.Visible&&c.LastSeenTick>=applied&&initial.Vision?.IsVisible(c.Position)!=true&&
                            !view.Entities.Any(u=>u.Owner==view.Owner&&AiOpponentSelector.Distance(u.Position,c.Position)<=PlayableUnitRules.Vision(profile,u.Kind)));
                    }
                    else {effect|=runtime.Latest.MineBlasts.Count>0;learned=effect;}
                    if(effect&&learned)break;yield return null;
                }
                TestContext.WriteLine("ORBITAL_SUPPORT ability="+ability+" tick="+runtime.Latest.Tick+" effect="+effect+" learned="+learned+" casts="+string.Join(",",Checkpoint().Records.Where(r=>r.Policy==AiOrbitalPolicy.Policy&&r.Kind==PlayableCommandKind.UseOrbital).Select(r=>r.Status+"@"+r.ApplicationTick)));
                Assert.True(runtime.Latest.Orbital.Abilities.Any(a=>a.Id==ability),"ordinary bot chose requested opportunity class");Assert.True(effect,"autonomous ability has world effect");Assert.True(learned,ability==1?"scan updates real owner knowledge":"mine contacts ground enemy");
                Assert.True(Checkpoint().Records.Any(r=>r.Policy==AiOrbitalPolicy.Policy&&r.Kind==PlayableCommandKind.UseOrbital&&r.Status==PlayableAiDeliveryStatus.Applied),"ordinary authority applied the ability");
            }
            finally{runtime.RequestStop();routes.Dispose();runtime.Dispose();}
        }
    }
}
