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
    public sealed class AiVictoryConversionThreadedTests
    {
        private const int DuelSeed=817042, TeamSeed=817044;
        private const long Horizon=7200;
        private const double WallSeconds=300;
        private const long ContactWindow=150;

        [UnityTest, Timeout(325000)]
        public IEnumerator DefensiveOwnerConvertsAdvantageIntoVictory()=>Run(false);

        [UnityTest, Timeout(325000)]
        public IEnumerator AlliesJoinWithinWindowThenDestroyRemainingCenters()=>Run(true);

        [UnityTest, Timeout(120000)]
        public IEnumerator CurrentAuthoritySaveRestoresAnActiveConversion()=>Run(false,true);

        private sealed class Movement
        {
            public int Unit,Owner;
            public long Action,Sequence,FirstTick,MotionTick;
            public NavPoint Origin;
        }

        private static IEnumerator Run(bool team,bool captureOnly=false)
        {
            int seed=team?TeamSeed:DuelSeed;
            var data=PlayableProfile.Default.CopyData();
            data.arenaHalfExtent=64;
            data.tankVisionRange=25;
            var profile=PlayableProfile.Create(data,displayName:"Victory conversion witness");
            var aiData=AiProfile.Initial.CopyData();
            aiData.id="victory-conversion-witness-v1";
            void Set(string key,double value)=>aiData.fields.Single(f=>f.path==key).value=value;
            Set("openingsMidgame.personality.aggressive",0);
            Set("openingsMidgame.personality.harassing",0);
            Set("victoryConversion.earlyDefensive",0);
            var ai=new AiProfile(aiData);
            var positions=team?new[]{new NavPoint(-35,-19),new NavPoint(-35,23),new NavPoint(20,0),new NavPoint(43,21)}:
                new[]{new NavPoint(-35,-19),new NavPoint(20,0)};
            int allies=team?2:1;
            var starts=positions.Select((p,i)=>new OfflineStart("site-"+(i+1),i+1,p,new NavPoint(p.X+4.5,p.Z+3),pin:i+1)).ToArray();
            var sites=starts.Select(s=>new TerritorySite(s.SiteId,PlayableBuildingKind.Headquarters,s.Position,Array.Empty<TerritorySlot>())).ToList();
            if(team)sites.Add(new TerritorySite(5,PlayableBuildingKind.Outpost,new NavPoint(40,-20),Array.Empty<TerritorySlot>()));
            var roster=starts.Select((s,i)=>new OfflineParticipant("ai-"+(i+1),i+1,i<allies?1:2,OfflineControl.Ai)).ToArray();
            var units=new List<OfflineScenarioUnit>();
            for(int player=0;player<allies;player++)
            for(int i=0;i<6;i++)
            {
                // Two opposing arrival lengths, outside idle weapon range, inside
                // observed vision. Authoring sets genesis only.
                double x=(player==0?0:5)-(i/3)*2;
                double z=(player==0?-6:4)+(i%3)*2;
                units.Add(new OfflineScenarioUnit(player+1,PlayableEntityKind.Tank,new NavPoint(x,z)));
            }
            var scenarioBuildings=team?new[]{new OfflineScenarioBuilding(4,PlayableBuildingKind.Outpost,5,0,sites.Last().Position,0)}:
                Array.Empty<OfflineScenarioBuilding>();
            var config=new OfflineMatchConfiguration(profile,"victory-conversion-witness-v1",
                team?"victory-conversion-flat-team":"victory-conversion-flat-duel","authored-flat",
                seed,roster,starts,sites.ToArray(),Array.Empty<NavObstacle>(),new double[starts.Length,starts.Length],
                new[]{PlayableRuntime.LocalSpectatorId},units.ToArray(),scenarioBuildings:scenarioBuildings);
            var difficulties=roster.Select(_=>AiDifficulty.Veteran).ToArray();
            foreach(var p in roster.Take(allies))
                Assert.AreEqual(AiPersonality.Defensive,AiDoctrinePolicy.InitialPersonality(PlayableAiOpeningComposition.Initialize(seed,p.Id,aiProfile:ai),ai,AiDifficulty.Veteran));
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/ai-victory-conversion/matches",
                (captureOnly?"restore-":team?"team-":"duel-")+Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(output);
            using var logSession=new TestMatchLogSession(output,new TestMatchLoggingProfile(),new{
                config.MapIdentity,config.Seed,Profile=profile.ProfileId,profile.Revision,Gameplay=profile.CopyData(),Ai=ai.CopyData(),ai.Hash,
                roster,starts,units,scenarioBuildings,difficulties,ContactWindow,Horizon,WallSeconds});
            TestMatchJournal journal=null;
            PlayableRuntime.JournalFactory=c=>journal=logSession.CreateMatch(c);
            var motion=new Dictionary<(int Unit,long Sequence),Movement>();
            var damage=new Dictionary<(int Center,int Attacker,long Action),PlayableCenterDamageSnapshot>();
            var losses=new Dictionary<int,PlayableBuildingDestruction>();
            var targets=new Dictionary<int,PlayableBuildingSnapshot>();
            long tick=-1;
            MatchResult result=null;
            string failure=null;
            try
            {
                using var runtime=new PlayableRuntime(config,1,startPaused:true,participantDifficulties:difficulties,spectator:true,aiProfile:ai);
                using var routes=new UnityHostRouteService();
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
                runtime.RequestPause(false);
                double deadline=Time.realtimeSinceStartupAsDouble+WallSeconds;
                while(tick<Horizon&&Time.realtimeSinceStartupAsDouble<deadline)
                {
                    routes.Service(runtime,profile.NavigationRequestsPerFrame);
                    var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);
                    if(frame!=null&&frame.Overview.Tick!=tick)
                    {
                        var view=frame.Overview;
                        tick=view.Tick;
                        foreach(var b in view.Buildings.Where(b=>(int)b.Owner>=allies&&TerritoryRules.Center(b.Kind)))
                            targets[b.Id]=b;
                        var ownUnits=frame.Players.Where(p=>(int)p.Owner<allies)
                            .SelectMany(p=>p.Entities.Where(u=>u.Owner==p.Owner)).ToArray();
                        foreach(var u in ownUnits.Where(u=>u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&
                            (u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack||u.CurrentOrder?.Kind==PlayableTacticalOrderKind.AttackMove)))
                        {
                            var stamp=u.OrderStamp;
                            var key=(u.Id,stamp.Sequence);
                            if(!motion.TryGetValue(key,out var row))
                                motion[key]=new Movement{Unit=u.Id,Owner=(int)u.Owner,Sequence=stamp.Sequence,Action=stamp.ActionId,Origin=u.Position,FirstTick=tick};
                            else if(row.MotionTick==0&&AiOpponentSelector.Distance(row.Origin,u.Position)>PlayableUnitRules.Radius(profile,u.Kind))
                                row.MotionTick=tick;
                        }
                        foreach(var fact in frame.Players.SelectMany(p=>p.OwnCenterDamage).Where(d=>targets.ContainsKey(d.CenterId)))
                        {
                            var attacker=ownUnits.FirstOrDefault(u=>u.Id==fact.AttackerId);
                            if(attacker?.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&fact.Tick>=attacker.OrderStamp.Tick)
                                damage.TryAdd((fact.CenterId,fact.AttackerId,attacker.OrderStamp.ActionId),fact);
                        }
                        foreach(var b in view.BuildingDestructions.Where(b=>targets.ContainsKey(b.Id)))losses.TryAdd(b.Id,b);
                    }
                    if(captureOnly&&motion.Values.Any(row=>row.MotionTick>0))
                    {
                        var capture=runtime.RequestCaptureBytes();
                        while(!capture.IsCompleted&&Time.realtimeSinceStartupAsDouble<deadline)
                        {
                            routes.Service(runtime,profile.NavigationRequestsPerFrame);
                            yield return null;
                        }
                        Assert.True(capture.IsCompletedSuccessfully,capture.Exception?.ToString());
                        using var world=PlayableWorldState.Decode(capture.Result);
                        Assert.AreEqual(PlayableWorldState.CurrentVersion,world.Version);
                        Assert.AreEqual(config.Seed,world.Seed);
                        Assert.AreEqual(config.SourceIdentity,world.SourceIdentity);
                        CollectionAssert.AreEqual(WorldWire.Binding(profile),world.Binding);
                        Assert.IsNotEmpty(world.RuntimeTransport,"Capture came from the threaded runtime.");
                        var restored=PlayableAuthorityTick.RestoreBytes(capture.Result,config,ai);
                        try
                        {
                            var mission=restored.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId==roster[0].Id).ArmyMission;
                            StringAssert.StartsWith("victory conversion ",mission.Reason);
                            Assert.Greater(mission.ArmyId,0);
                            world.RuntimeTransport=Array.Empty<byte>();
                            var exact=world.Encode();
                            CollectionAssert.AreEqual(exact,restored.CaptureBytes(),"Exact current simulation, navigation and AI authority state");
                            File.WriteAllBytes(Path.Combine(output,"authority.bin"),exact);
                            File.WriteAllText(Path.Combine(output,"restore.json"),JsonConvert.SerializeObject(new{
                                world.Version,world.Tick,world.Generation,world.Seed,world.SourceIdentity,config.MapIdentity,
                                profile.ProfileId,profile.Revision,ai.Hash,AiSchema=AiStateWire.Version,Mission=mission.Reason,
                                Assembly=typeof(PlayableAuthorityTick).Assembly.ManifestModule.ModuleVersionId,Exact=true}));
                        }
                        finally{restored.Stop();restored.Domain.Navigation.DestroyNavigationAuthority();}
                        runtime.RequestStop();
                        Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000));
                        TestContext.WriteLine("VICTORY_RESTORE "+output);
                        yield break;
                    }
                    failure=runtime.Latest.Failure??routes.LastProjectionFailure?.ToString();
                    result=runtime.Result;
                    if(result!=null&&tick>=result.Tick||failure!=null||runtime.IsStopped)break;
                    yield return null;
                }
                runtime.RequestStop();
                Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000));
            }
            finally{PlayableRuntime.JournalFactory=null;}
            Assert.False(captureOnly,"The diagnostic must capture an active conversion after physical motion.");
            double drain=Time.realtimeSinceStartupAsDouble+10;
            while(journal!=null&&!journal.WriterStopped&&Time.realtimeSinceStartupAsDouble<drain)yield return null;
            Assert.NotNull(journal);
            Assert.True(journal.WriterStopped);
            var summary=JObject.Parse(File.ReadAllText(Path.Combine(journal.DirectoryPath,"summary.json")));
            var trace=Directory.EnumerateFiles(journal.DirectoryPath,"events-*.jsonl").OrderBy(x=>x)
                .SelectMany(File.ReadLines).Where(l=>l.Contains("\"Type\":\"ai-receipt\""))
                .Select(l=>JObject.Parse(l)["Record"].ToObject<PlayableAiTraceRecord>()).ToArray();
            var decisions=trace.Where(r=>r.Status==PlayableAiDeliveryStatus.Scheduled&&r.Message.StartsWith("victory conversion ",StringComparison.Ordinal)).ToArray();
            var admitted=trace.Where(r=>r.Status==PlayableAiDeliveryStatus.Applied&&decisions.Any(d=>d.ActionId==r.ActionId&&d.OwnerId==r.OwnerId)).ToArray();
            var moved=motion.Values.Where(m=>m.MotionTick>0&&admitted.Any(r=>r.ActionId==m.Action&&r.CommandSequence==m.Sequence&&r.OwnerId==roster[m.Owner].Id)).ToArray();
            var contacts=damage.Where(row=>moved.Any(m=>m.Unit==row.Key.Attacker&&m.Action==row.Key.Action&&m.MotionTick<=row.Value.Tick))
                .Select(row=>row.Value).OrderBy(d=>d.Tick).ToArray();
            int first=targets.Values.Single(b=>b.SiteId==allies+1).Id;
            var joined=Enumerable.Range(0,allies).Select(i=>contacts.FirstOrDefault(d=>d.CenterId==first&&moved.Any(m=>m.Unit==d.AttackerId&&m.Owner==i))).ToArray();
            File.WriteAllText(Path.Combine(output,"readback.json"),JsonConvert.SerializeObject(new{
                seed,config.MapIdentity,profile.ProfileId,profile.Revision,ai.Hash,Result=result==null?null:new{result.WinnerTeam,result.Tick,result.Manual},
                Tick=tick,Failure=failure,Decisions=decisions,Applied=admitted,Motion=moved,Contacts=contacts,FirstContacts=joined,
                Losses=losses.Values.OrderBy(l=>l.StartTick),Remaining=targets.Values.Where(b=>!losses.ContainsKey(b.Id)).Select(b=>new{b.Id,b.SiteId,b.Health}),
                RawMotion=motion.Values,RawDamage=damage.Select(d=>new{d.Key.Action,Fact=d.Value}),Journal=journal.DirectoryPath
            },Formatting.Indented));
            TestContext.WriteLine("VICTORY_WITNESS "+output);
            Assert.IsNull(failure);
            Assert.AreEqual(0,(long)summary["Dropped"]);
            Assert.IsNotEmpty(decisions,"Victory conversion must schedule a real order.");
            Assert.IsNotEmpty(admitted,"The scheduled conversion must have an applied receipt.");
            Assert.IsNotEmpty(moved,"Applied conversion commands must physically move units.");
            Assert.True(joined.All(x=>x!=null),"Each ally must move under an applied conversion order and damage the first center.");
            Assert.LessOrEqual(joined.Max(x=>x.Tick)-joined.Min(x=>x.Tick),ContactWindow);
            Assert.True(losses.ContainsKey(first));
            if(team)
            {
                Assert.GreaterOrEqual(losses.Count,3,"Both headquarters and the surviving outpost must actually be destroyed.");
                Assert.True(contacts.Any(c=>c.CenterId!=first&&c.Tick>losses[first].StartTick),"A conversion order must physically reach a subsequent center.");
            }
            Assert.NotNull(result,"Ordinary match must terminate before the tick and wall horizon.");
            Assert.False(result.Manual);
            Assert.AreEqual(1,result.WinnerTeam);
        }
    }
}
