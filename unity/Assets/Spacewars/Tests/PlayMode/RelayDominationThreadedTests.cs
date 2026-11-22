using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class RelayDominationThreadedTests
    {
        [UnityTest,Timeout(120000)]
        public IEnumerator LobbyMatchMovesCapturesAndStartsDominationOnTheReleasedProfile()
        {
            Assert.True(NativeSilentStartup.IsSilent);
            var output=Environment.GetEnvironmentVariable("RELAY_MAPS_EVIDENCE");
            Assert.IsNotEmpty(output);
            Directory.CreateDirectory(output);
            var author=AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly=>assembly.GetType("Spacewars.Editor.PreparedMapsAuthoring")).First(type=>type!=null);
            var profile=(PlayableProfile)author.GetMethod("Profile").Invoke(null,new object[]{"BasaltCrown"});
            Assert.AreEqual(200,profile.DominationTarget);
            Assert.AreEqual(1,profile.DominationGain);
            Assert.AreEqual(2,profile.DominationDisplacement);
            Assert.AreEqual(.5,profile.DominationDecay);
            using var journal=new StreamWriter(Path.Combine(output,"domination-witness.txt")){AutoFlush=true};
            journal.WriteLine(profile.ProfileId+" @"+profile.Revision+" · relay capture "+profile.RelayCaptureSeconds+"s");

            var setup=new NativeLobbyConfiguration {
                BasaltCrown=true,HasExplicitSeed=true,ExplicitSeed=3112026,
                Participants=new List<NativeLobbyParticipant> {
                    new NativeLobbyParticipant{Name="Command witness",Human=true,Team=1,Color=0},
                    new NativeLobbyParticipant{Name="Idle opponent",Human=true,Team=2,Color=1,DeviceId=1}
                }
            };
            using var runtime=PlayableRuntime.CreateLobbyMatch(profile,94,setup,startPaused:true);
            using var routes=new UnityHostRouteService();
            double deadline=Time.realtimeSinceStartupAsDouble+90;
            IEnumerator Until(Func<bool> condition,string stage)
            {
                while(!condition())
                {
                    Assert.Less(Time.realtimeSinceStartupAsDouble,deadline,stage);
                    routes.Service(runtime,profile.NavigationRequestsPerFrame);
                    Assert.IsNull(runtime.Latest?.Failure,stage);
                    yield return null;
                }
            }
            try
            {
                yield return Until(()=>runtime.IsPrepared&&runtime.Latest?.Domination.HasValue==true,"lobby authority prepared");
                double baseline=runtime.Latest.IncomePerSecond;
                var scout=runtime.Latest.Entities.Single(unit=>unit.Owner==PlayableOwner.Player);
                var target=new NavPoint(profile.RelayCaptureRadius-profile.ExplorerCollisionRadius,0);
                runtime.RequestPause(false);
                Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,runtime.HumanSequenceWatermark+1,
                    "player-1",PlayableCommandKind.Move,new[]{scout.Id},target).AsHuman()).Accepted);
                yield return Until(()=>runtime.Latest.Entities.Any(unit=>unit.Id==scout.Id&&!unit.Position.Equals(scout.Position)),"first motion");
                journal.WriteLine("First motion at tick "+runtime.Latest.Tick);
                yield return Until(()=>runtime.Latest.Domination.Value.Score>=1,"relay captured and points awarded");
                Assert.AreEqual(1,runtime.Latest.Domination.Value.LeadingTeam);
                Assert.AreEqual(baseline,runtime.Latest.IncomePerSecond);
                Assert.IsFalse(runtime.Latest.IncomeEvents.Any(item=>item.Kind==PlayableBuildingKind.RelayTower));
                Assert.True(runtime.ParticipantView("foundry-2").MatchEvents.Any(item=>item.Kind==MatchEventKind.DominationGained));
                journal.WriteLine("Ownership and global event confirmed at tick "+runtime.Latest.Tick);
            }
            finally
            {
                runtime.RequestStop();
                Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000));
            }
        }
    }
}
