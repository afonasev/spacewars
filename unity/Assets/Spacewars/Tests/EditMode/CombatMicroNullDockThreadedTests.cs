using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class CombatMicroNullDockThreadedTests
    {
        [Test, Explicit("Bounded same-seed threaded witness using the original TestLogs manifest."), Timeout(700000)]
        public void SpectatorPassesOriginalFailureTick()
        {
            const int seed=-838049291;
            const long generation=2, horizon=16000;
            var output=Environment.GetEnvironmentVariable("SPACEWARS_MICRO_WITNESS");
            Assert.IsNotEmpty(output);
            var source=JObject.Parse(File.ReadAllText(Path.Combine(output,"manifest-input.json")));
            Assert.AreEqual(seed,(int)source["Seed"]);
            Assert.AreEqual(generation,(long)source["Generation"]);
            Assert.AreEqual(0L,(long)source["StartTick"]);
            Assert.True((bool)source["Spectator"]);
            Assert.False((bool)source["Restored"]);

            var mapJson=File.ReadAllText(Path.Combine(output,"map-profile.json"));
            var map=new NullDockMap(JsonConvert.DeserializeObject<NullDockProfileData>(mapJson));
            var profile=PlayableProfile.Create(source["Profile"].ToObject<PlayableProfileData>(),map);
            var ai=new AiProfile(source["Ai"]["Data"].ToObject<AiProfileData>());
            Assert.AreEqual((string)source["Ai"]["Hash"],ai.Hash);
            Assert.True(JToken.DeepEquals(source["Profile"],JObject.FromObject(profile.CopyData())));
            var setup=new NativeLobbyConfiguration{
                NullDock=true,HasExplicitSeed=true,ExplicitSeed=seed,Difficulty=AiDifficulty.Veteran,
                Participants=new List<NativeLobbyParticipant>{
                    new NativeLobbyParticipant{Name="ИИ",Team=2,Color=0,Difficulty=AiDifficulty.Veteran},
                    new NativeLobbyParticipant{Name="ИИ",Team=1,Color=1,Difficulty=AiDifficulty.Fighter}
                }
            };
            var config=NativeLobbyMatch.Create(profile,setup);
            var serializer=new JsonSerializer{Converters={new Newtonsoft.Json.Converters.StringEnumConverter()}};
            Assert.True(JToken.DeepEquals(source["Roster"],JArray.FromObject(config.Roster,serializer)));
            Assert.AreEqual("null-dock-a04-v1",config.MapIdentity);
            Assert.AreEqual((string)source["Map"],config.MapIdentity);
            CollectionAssert.AreEqual(new[]{2,1},config.Starts.OrderBy(s=>s.SiteId).Select(s=>s.PinnedLogicalPlayer.Value));

            using var logs=new TestMatchLogSession(Path.Combine(output,"TestLogs"),new TestMatchLoggingProfile(),
                new{config.MapIdentity,Seed=seed,SpectatorSpeed=4});
            TestMatchJournal journal=null;
            var previousFactory=PlayableRuntime.JournalFactory;
            PlayableRuntime.JournalFactory=c=>journal=logs.CreateMatch(c);
            using var host=new UnityHostRouteService();
            PlayableRuntime runtime=null;
            var clock=Stopwatch.StartNew();
            var initialPositions=new Dictionary<int,NavPoint>();
            long firstAiMotion=-1;
            long tick=0;
            string failure=null;
            try
            {
                runtime=PlayableRuntime.CreateLobbyMatch(profile,generation,setup,startPaused:true,aiProfile:ai);
                while(!(runtime.IsPrepared&&runtime.Latest.Paused)&&clock.Elapsed.TotalSeconds<60)
                {
                    host.Service(runtime,profile.NavigationRequestsPerFrame);
                    Assert.IsNull(runtime.Latest.Failure);
                    Thread.Sleep(1);
                }
                Assert.True(runtime.IsPrepared&&runtime.Latest.Paused);
                Assert.NotNull(host.CertifiedPublication,"Certified route worker must attach.");
                foreach(var entity in runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview.Entities)
                    initialPositions.Add(entity.Id,entity.Position);

                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
                runtime.RequestPause(false);
                clock.Restart();
                while(clock.Elapsed.TotalSeconds<600&&!runtime.IsStopped)
                {
                    host.Service(runtime,profile.NavigationRequestsPerFrame);
                    var view=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview;
                    tick=view.Tick;
                    failure=view.Failure??host.CertifiedPublication.Failure;
                    if(failure!=null||view.Outcome!=PlayableMatchOutcome.Playing)break;
                    if(firstAiMotion<0&&view.Entities.Any(u=>u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&
                        initialPositions.TryGetValue(u.Id,out var start)&&!start.Equals(u.Position)))firstAiMotion=tick;
                    if(tick>=horizon)break;
                    Thread.Sleep(1);
                }
                Assert.IsNull(failure);
                Assert.IsNull(runtime.Latest.Failure);
                Assert.GreaterOrEqual(tick,horizon,"Threaded match must pass the original failure tick 15890.");
                Assert.GreaterOrEqual(firstAiMotion,0,"An ordinary AI order must move a starting unit.");
            }
            finally
            {
                PlayableRuntime.JournalFactory=previousFactory;
                runtime?.Dispose();
                bool stopped=SpinWait.SpinUntil(()=>runtime==null||runtime.IsStopped,5000);
                bool drained=SpinWait.SpinUntil(()=>journal==null||journal.WriterStopped,10000);
                File.WriteAllText(Path.Combine(output,"threaded-result.json"),JsonConvert.SerializeObject(new{
                    Seed=seed,Generation=generation,Map=config.MapIdentity,AiHash=ai.Hash,Tick=tick,FirstAiMotion=firstAiMotion,
                    Failure=failure??runtime?.Latest.Failure,ProjectionDiagnostic=host.LastProjectionFailure,
                    Stopped=stopped,JournalDrained=drained,ElapsedSeconds=clock.Elapsed.TotalSeconds
                },Formatting.Indented));
                Assert.True(stopped);
                Assert.NotNull(journal);
                Assert.True(drained);
            }
        }
    }
}
