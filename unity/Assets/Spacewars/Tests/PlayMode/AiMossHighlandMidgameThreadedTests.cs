using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class AiMossHighlandMidgameThreadedTests
    {
        [UnityTest, Timeout(625000)]
        public IEnumerator KnownOutpostProducesIncomeAndCapacityWhileArmiesAct()
        {
            var source=JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath,"../Tests/Fixtures/MossHighlandMidgameWitness.json")));
            var map=new MossHighlandMap(JsonUtility.FromJson<MossHighlandProfileData>(Resources.Load<TextAsset>("MossHighlandProfile").text));
            var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(source["Profile"].ToString()),map);
            var ai=new AiProfile(JsonUtility.FromJson<AiProfileData>(source["Ai"]["Data"].ToString()));
            Assert.AreEqual((string)source["Ai"]["Hash"],ai.Hash);
            var difficulties=source["Difficulties"].Values<string>().ToArray();
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=(int)source["Seed"],MossHighland=true,
                Participants=source["Roster"].Select((player,i)=>new NativeLobbyParticipant{Name=(string)player["Id"],
                    Team=(int)player["Team"],Color=i,Difficulty=(AiDifficulty)Enum.Parse(typeof(AiDifficulty),difficulties[i])}).ToList()};
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/midgame-witness/moss-"+Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(output);
            using var logs=new TestMatchLogSession(output,new TestMatchLoggingProfile(),new{Fixture=TestContext.CurrentContext.Test.Name,setup.ExplicitSeed,ai.Hash});
            TestMatchJournal journal=null;
            PlayableRuntime.JournalFactory=c=>journal=logs.CreateMatch(c);
            PlayableRuntime runtime=null;
            UnityHostRouteService host=null;
            var firstPoses=new Dictionary<(int Unit,long Sequence),NavPoint>();
            var motion=new HashSet<long>();
            var outposts=new HashSet<int>();
            var outpostIncome=new HashSet<int>();
            var outpostUse=new HashSet<int>();
            var matureEconomies=new HashSet<PlayableOwner>();
            var damage=new HashSet<int>();
            var invalid=new Dictionary<string,PlayableAiTraceRecord>();
            long tick=-1;
            bool reached=false;
            try
            {
                runtime=PlayableRuntime.CreateLobbyMatch(profile,1,setup,startPaused:true,aiProfile:ai);
                host=new UnityHostRouteService();
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
                runtime.RequestPause(false);
                double deadline=Time.realtimeSinceStartupAsDouble+600;
                while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline)
                {
                    host.Service(runtime,profile.NavigationRequestsPerFrame);
                    var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);
                    var view=frame.Overview;
                    Assert.IsNull(view.Failure);
                    Assert.IsNull(host.LastProjectionFailure);
                    if(view.Tick!=tick)
                    {
                        tick=view.Tick;
                        foreach(var b in view.Buildings.Where(b=>b.Kind==PlayableBuildingKind.Outpost&&b.Phase==ConstructionPhase.Ready&&b.Health>0))
                        {
                            outposts.Add(b.Id);
                            if(frame.Players.Any(p=>p.IncomeEvents.Any(i=>i.BuildingId==b.Id&&i.Amount>0)))outpostIncome.Add(b.Id);
                            if(view.Buildings.Any(x=>x.ParentId==b.Id&&x.Health>0&&x.Phase==ConstructionPhase.Ready&&
                                (x.Kind==PlayableBuildingKind.Refinery||x.Kind==PlayableBuildingKind.Factory||x.Kind==PlayableBuildingKind.Hangar||
                                 TerritoryRules.ArmedDefense(x.Kind))))outpostUse.Add(b.Id);
                        }
                        foreach(var p in frame.Players)
                        {
                            if(AiEconomicInvestmentPlanner.ReadyIncomeCount(PlayableAiObservation.From(p))>
                                ai.Value("technology.minimumIncomeBuildings"))matureEconomies.Add(p.Owner);
                        }
                        foreach(var b in view.Buildings.Where(b=>b.Health>0&&b.Phase==ConstructionPhase.Ready&&
                            (b.Kind==PlayableBuildingKind.Mine||b.Kind==PlayableBuildingKind.Refinery||b.Kind==PlayableBuildingKind.Headquarters)&&
                            (b.ExactHealth??b.Health)<TerritoryRules.MaximumHealth(profile,b)))damage.Add(b.Id);
                        foreach(var u in view.Entities.Where(u=>u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&
                            (u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move||u.CurrentOrder?.Kind==PlayableTacticalOrderKind.AttackMove||
                             u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack)))
                        {
                            var key=(u.Id,u.OrderStamp.Sequence);
                            if(!firstPoses.TryGetValue(key,out var start))firstPoses.Add(key,u.Position);
                            else if(AiOpponentSelector.Distance(start,u.Position)>PlayableUnitRules.Radius(profile,u.Kind))motion.Add(key.Sequence);
                        }
                    }
                    if(tick>=(long)source["StopTick"]||view.Outcome!=PlayableMatchOutcome.Playing){reached=true;break;}
                    yield return null;
                }
                runtime.RequestFinish();
                double finish=Time.realtimeSinceStartupAsDouble+8;
                while(runtime.Latest.Outcome==PlayableMatchOutcome.Playing&&Time.realtimeSinceStartupAsDouble<finish)
                {host.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
            }
            finally
            {
                runtime?.RequestStop();
                if(runtime!=null)System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000);
                host?.Dispose();
                runtime?.Dispose();
                PlayableRuntime.JournalFactory=null;
            }
            double drain=Time.realtimeSinceStartupAsDouble+10;
            while(journal!=null&&!journal.WriterStopped&&Time.realtimeSinceStartupAsDouble<drain)yield return null;
            Assert.NotNull(journal);
            Assert.True(journal.WriterStopped);
            var summary=JObject.Parse(File.ReadAllText(Path.Combine(journal.DirectoryPath,"summary.json")));
            Assert.AreEqual(0,(long)summary["Dropped"]);
            var applied=new HashSet<long>();
            foreach(var file in Directory.EnumerateFiles(journal.DirectoryPath,"events-*.jsonl"))
            foreach(var line in File.ReadLines(file).Where(l=>l.Contains("\"Type\":\"ai-receipt\"")))
            {
                var receipt=JObject.Parse(line)["Record"].ToObject<PlayableAiTraceRecord>();
                if(receipt.Policy!="army-mission"&&receipt.Policy!="army-flank")continue;
                if(receipt.Status==PlayableAiDeliveryStatus.Applied)applied.Add(receipt.CommandSequence);
                if(receipt.RuntimeStatus==PlayableCommandStatus.InvalidTarget&&
                    (receipt.Kind==PlayableCommandKind.Move||receipt.Kind==PlayableCommandKind.AttackMove))
                    invalid[receipt.ReceiptIdentity.Id]=receipt;
            }
            File.WriteAllText(Path.Combine(output,"readback.json"),JsonConvert.SerializeObject(new{
                tick,reached,sourceRevision=(string)source["SourceRevision"],seed=setup.ExplicitSeed,ai.Hash,
                outposts,outpostIncome,outpostUse,matureEconomies,damage,armyMotion=motion.Count(applied.Contains),
                invalid=invalid.Values,journal=journal.DirectoryPath
            },Formatting.Indented));
            TestContext.WriteLine("MOSS_MIDGAME_WITNESS "+output);
            Assert.True(reached,"Original horizon or genuine match termination.");
            Assert.True(outpostIncome.Overlaps(outpostUse),"A ready captured outpost pays and hosts useful infrastructure.");
            Assert.IsNotEmpty(matureEconomies,"At least one economy grows beyond the science prerequisite.");
            Assert.IsNotEmpty(damage,"An HQ or income target takes damage.");
            Assert.Greater(motion.Count(applied.Contains),0,"An applied army command leads to physical movement.");
            Assert.IsEmpty(invalid,"No army movement command fails destination admission.");
        }
    }
}
