using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Headless;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiEliminationCrowdBindingTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private sealed class ResearchRow
        {
            public string Kind;public bool Active,Complete;public double PaidCost,Elapsed,Duration;
        }
        private sealed class UnitRow
        {
            public int Id;public string Owner,Kind;public int ActualTeam,ExpectedTeam;public double ActualRadius,ExpectedRadius,ActualTurnSpeed,ExpectedTurnSpeed,ActualSpeed,ExpectedSpeed;
            public bool OwnerEliminated,Upgraded;public bool? AuthoredUpgrade;public ResearchRow[] OwnerResearch;
            public bool HasTypedMismatch=>ActualTeam!=ExpectedTeam||ActualRadius!=ExpectedRadius||ActualTurnSpeed!=ExpectedTurnSpeed||ActualSpeed!=ExpectedSpeed;
        }
        private sealed class OwnerRow
        {
            public string Owner;public bool Eliminated;public ResearchRow[] Research;public UnitRow[] Units;
        }
        private static object Get(object value,string name)
        {
            var field=value.GetType().GetField(name,Flags);if(field!=null)return field.GetValue(value);
            return value.GetType().GetProperty(name,Flags).GetValue(value);
        }
        private static void Set(object value,string name,object fieldValue)=>value.GetType().GetField(name,Flags).SetValue(value,fieldValue);
        private static string Root()
        {var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;Assert.NotNull(d);return d.FullName;}
        private static byte[] ReadNaturalFixture(string member)
        {
            var archivePath=Path.Combine(Root(),"docs/native-ai/o6/o6-natural-topic-merge-source-compiled-raw.zip");
            using(var archive=ZipFile.OpenRead(archivePath))
            {
                var entry=archive.GetEntry(member);Assert.NotNull(entry,"natural fixture member is present in the committed source archive: "+member);
                using(var input=entry.Open())using(var output=new MemoryStream()){input.CopyTo(output);return output.ToArray();}
            }
        }
        private static OwnerRow[] Capture(PlayableAuthorityTick authority,PlayableProfile profile,OfflineParticipant[] roster)
        {
            var domain=Get(authority,"domain");var navigation=(NavigationSession)Get(domain,"navigation");var crowd=navigation.Crowd;
            var unitValues=((IEnumerable)Get(Get(domain,"units"),"Values")).Cast<object>().ToArray();
            var research=(IDictionary)Get(domain,"research");var eliminated=((IEnumerable)Get(domain,"eliminated")).Cast<PlayableOwner>().ToHashSet();
            var upgraded=domain.GetType().GetMethod("UnitUpgraded",Flags);
            return roster.Select(participant=>
            {
                var owner=(PlayableOwner)Array.IndexOf(roster,participant);bool isEliminated=eliminated.Contains(owner);
                var orders=research.Contains(owner)?((IEnumerable)research[owner]).Cast<object>().Select(order=>new ResearchRow
                {
                    Kind=Get(order,"Kind").ToString(),Active=(bool)Get(order,"Active"),Complete=(bool)Get(order,"Complete"),
                    PaidCost=Convert.ToDouble(Get(order,"PaidCost")),Elapsed=Convert.ToDouble(Get(order,"Elapsed")),Duration=Convert.ToDouble(Get(order,"Duration"))
                }).ToArray():Array.Empty<ResearchRow>();
                var units=unitValues.Where(unit=>(PlayableOwner)Get(unit,"Owner")==owner).Select(unit=>
                {
                    var kind=(PlayableEntityKind)Get(unit,"Kind");int id=(int)Get(unit,"Id");var actual=crowd.Units.Single(nav=>nav.Id==id);bool isUpgraded=(bool)upgraded.Invoke(domain,new[]{unit});
                    return new UnitRow
                    {
                        Id=id,Owner=participant.Id,Kind=kind.ToString(),ActualTeam=actual.Team,ExpectedTeam=participant.Team,
                        ActualRadius=actual.Radius,ExpectedRadius=PlayableUnitRules.Radius(profile,kind),
                        ActualTurnSpeed=actual.TurnSpeed,ExpectedTurnSpeed=PlayableUnitRules.Turn(profile,kind),
                        ActualSpeed=actual.Speed,ExpectedSpeed=PlayableUnitRules.Speed(profile,kind,kind==PlayableEntityKind.Tank&&isUpgraded),
                        OwnerEliminated=isEliminated,Upgraded=isUpgraded,AuthoredUpgrade=(bool?)Get(unit,"AuthoredUpgrade"),OwnerResearch=orders
                    };
                }).OrderBy(unit=>unit.Id).ToArray();
                return new OwnerRow{Owner=participant.Id,Eliminated=isEliminated,Research=orders,Units=units};
            }).ToArray();
        }
        private static object Frame(long tick,OwnerRow[] owners)=>new
        {
            Tick=tick,Owners=owners.Select(owner=>new{owner.Owner,owner.Eliminated,owner.Research,
                Units=owner.Units.Select(unit=>new{unit.Id,unit.Kind,unit.OwnerEliminated,unit.AuthoredUpgrade,unit.Upgraded,
                    Actual=new{Team=unit.ActualTeam,Radius=unit.ActualRadius,TurnSpeed=unit.ActualTurnSpeed,Speed=unit.ActualSpeed},
                    Expected=new{Team=unit.ExpectedTeam,Radius=unit.ExpectedRadius,TurnSpeed=unit.ExpectedTurnSpeed,Speed=unit.ExpectedSpeed},unit.HasTypedMismatch})})
        };

        [Test]
        public void Natural7108_LastValid15600ContinuesUninjectedThroughEliminationAndStrictRestore()
        {
            const int seed=7108,startTick=15600,tickLimit=15900;const string sourceCommit="9d35145f2cd34c4a075deefdeacba58492d82778";
            var root=Root();var output=Path.Combine(root,".local/qa/o6-elimination-crowd-green");Directory.CreateDirectory(output);
            var participants=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,seed,participants);
            byte[] start=ReadNaturalFixture(".local/o6-natural/7108-checkpoint-15600.world");
            Assert.AreEqual("b9b09dbb621b8333c6f50832c8589f5a4d7dbdda39acb42d8477e57b8cec99c7",Wire.Hash(start),"exact final valid source checkpoint");
            var authority=PlayableAuthorityTick.RestoreBytes(start,config);
            var trace=Path.Combine(output,"trace.jsonl");File.WriteAllText(trace,string.Empty);
            string[] previousEliminated=Array.Empty<string>();long firstMismatchTick=-1,ai7EliminationTick=-1;
            try
            {
                Assert.AreEqual(startTick,authority.Tick);CollectionAssert.AreEqual(start,authority.CaptureBytes(),"exact restoration of last-valid 15600 sample");
                File.WriteAllBytes(Path.Combine(output,"before-15600.world"),start);
                OwnerRow[] before=Capture(authority,profile,participants);previousEliminated=before.Where(owner=>owner.Eliminated).Select(owner=>owner.Owner).ToArray();
                File.AppendAllText(trace,JsonConvert.SerializeObject(new{SourceCommit=sourceCommit,Kind="before",Frame=Frame(authority.Tick,before)})+Environment.NewLine);
                var ai7Before=before.Single(owner=>owner.Owner=="ai-7");
                bool chassisResearchAtStart=ai7Before.Research.Any(order=>order.Kind==PlayableResearchKind.TankChassis.ToString()&&order.Complete);
                Assert.True(chassisResearchAtStart,"ai-7 starts with completed TankChassis research");
                CollectionAssert.AreEquivalent(new[]{145,150},ai7Before.Units.Where(unit=>unit.Kind==PlayableEntityKind.Tank.ToString()).Select(unit=>unit.Id));
                Assert.True(ai7Before.Units.Where(unit=>unit.Kind==PlayableEntityKind.Tank.ToString()).All(unit=>unit.ActualSpeed==profile.TankChassisSpeed&&unit.ExpectedSpeed==profile.TankChassisSpeed&&!unit.AuthoredUpgrade.HasValue&&unit.Upgraded));
                TestContext.WriteLine("O6_ELIMINATION_CROWD_BEFORE "+JsonConvert.SerializeObject(new{Seed=seed,Tick=authority.Tick,CheckpointHash=Wire.Hash(start),Ai7ChassisResearchComplete=chassisResearchAtStart,Eliminated=previousEliminated,Frame=Frame(authority.Tick,before)}));
                using(var host=new UnityHostRouteService())
                for(int i=0;i<tickLimit-startTick;i++)
                {
                    bool advanced=false;
                    for(int attempt=0;attempt<512;attempt++){if(authority.TryAdvance()){advanced=true;break;}host.Service(authority,64);}
                    Assert.True(advanced,"ordinary route barrier at tick "+authority.Tick);Assert.IsNull(authority.Latest.Failure);
                    var owners=Capture(authority,profile,participants);var nowEliminated=owners.Where(owner=>owner.Eliminated).Select(owner=>owner.Owner).OrderBy(owner=>owner,StringComparer.Ordinal).ToArray();
                    var ai7=owners.Single(owner=>owner.Owner=="ai-7");
                    if(ai7.Eliminated)
                    {
                        if(ai7EliminationTick<0){ai7EliminationTick=authority.Tick;Assert.AreEqual(15649,ai7EliminationTick,"ai-7 natural elimination tick remains the original event");}
                        Assert.IsEmpty(ai7.Research,"elimination removes ai-7 research");
                        foreach(var tank in ai7.Units.Where(unit=>unit.Kind==PlayableEntityKind.Tank.ToString()))
                            Assert.AreEqual(tank.ExpectedSpeed,tank.ActualSpeed,1e-9,"typed tank mobility remains synchronized after elimination at tick "+authority.Tick+" id="+tank.Id);
                    }
                    var mismatches=owners.SelectMany(owner=>owner.Units).Where(unit=>unit.HasTypedMismatch).ToArray();
                    bool eliminationChanged=!nowEliminated.SequenceEqual(previousEliminated);
                    bool firstMismatch=mismatches.Length>0&&firstMismatchTick<0;
                    if(firstMismatch)firstMismatchTick=authority.Tick;
                    if(eliminationChanged||firstMismatch||authority.Tick==tickLimit)
                    {
                        var frame=Frame(authority.Tick,owners);string label=eliminationChanged?"elimination-change":firstMismatch?"first-typed-mismatch":"target-15900";
                        File.AppendAllText(trace,JsonConvert.SerializeObject(new{SourceCommit=sourceCommit,Kind=label,Eliminated=nowEliminated,MismatchCount=mismatches.Length,Frame=frame})+Environment.NewLine);
                        TestContext.WriteLine("O6_ELIMINATION_CROWD_EVENT "+JsonConvert.SerializeObject(new{Kind=label,Eliminated=nowEliminated,MismatchCount=mismatches.Length,Frame=frame}));
                    }
                    previousEliminated=nowEliminated;
                }
                Assert.AreEqual(tickLimit,authority.Tick,"only 300 ordinary C9 ticks were advanced");
                Assert.AreEqual(15649,ai7EliminationTick,"natural ai-7 elimination was observed at the causal tick");
                var finalBytes=authority.CaptureBytes();File.WriteAllBytes(Path.Combine(output,"after-15900-corrected.world"),finalBytes);
                var finalOwners=Capture(authority,profile,participants);var ai7Final=finalOwners.Single(owner=>owner.Owner=="ai-7");
                Assert.True(ai7Final.Eliminated);Assert.IsEmpty(ai7Final.Research);Assert.IsEmpty(finalOwners.SelectMany(owner=>owner.Units).Where(unit=>unit.HasTypedMismatch));
                Assert.True(firstMismatchTick<0,"no typed crowd mismatch occurs during the natural continuation");
                var restored=PlayableAuthorityTick.RestoreBytes(finalBytes,config);
                try{CollectionAssert.AreEqual(finalBytes,restored.CaptureBytes(),"corrected strict target restore is byte exact");}finally{restored.Stop();}
                var originalInvalid=ReadNaturalFixture(".local/o6-natural/7108-checkpoint-15900.world");
                Assert.AreEqual("059ef3cfc9cba7cbf06cf69a74455ad098b0001974ad3dcdbbf1b7eac8b5eb8d",Wire.Hash(originalInvalid),"archived natural invalid sample remains unchanged");
                var invalidRestore=Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(originalInvalid,config));
                Assert.True(invalidRestore.Message.Contains("typed crowd binding"),"unchanged validator still rejects the archived invalid sample");
                Assert.AreNotEqual(Wire.Hash(originalInvalid),Wire.Hash(finalBytes),"corrected state differs from archived invalid sample");
                var result=new{SourceCommit=sourceCommit,Tick=authority.Tick,CorrectedSHA256=Wire.Hash(finalBytes),OriginalInvalidSHA256=Wire.Hash(originalInvalid),FirstMismatchTick=firstMismatchTick,Frame=Frame(authority.Tick,finalOwners)};
                File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(result,Formatting.Indented)+Environment.NewLine);
                TestContext.WriteLine("O6_ELIMINATION_CROWD_GREEN "+JsonConvert.SerializeObject(result));
            }
            finally{authority.Stop();}
        }

        [Test]
        public void EliminationPreservesAuthoredTankUpgradeTrueAndFalse()
        {
            var config=OfflineParticipantAuthorityTests.Config(8,allies:false,seed:7109,scenario:true);
            var authority=new OfflineParticipantAuthority(config,72);var owner=(PlayableOwner)7;var ownerId=config.Roster[7].Id;
            var domain=OfflineParticipantAuthorityTests.Domain(authority);
                var tanks=authority.View(ownerId).Entities.Where(entity=>entity.Owner==owner&&entity.Kind==PlayableEntityKind.Tank).OrderBy(entity=>entity.Upgraded).ToArray();
                Assert.AreEqual(2,tanks.Length);Assert.False(tanks[0].Upgraded);Assert.True(tanks[1].Upgraded);
                var unitStates=((IEnumerable)Get(Get(domain,"units"),"Values")).Cast<object>().ToArray();
                Assert.AreEqual((bool?)false,(bool?)Get(unitStates.Single(unit=>(int)Get(unit,"Id")==tanks[0].Id),"AuthoredUpgrade"));
                Assert.AreEqual((bool?)true,(bool?)Get(unitStates.Single(unit=>(int)Get(unit,"Id")==tanks[1].Id),"AuthoredUpgrade"));
                var researchMethod=domain.GetType().GetMethod("Research",Flags);var orders=(IList)researchMethod.Invoke(domain,new object[]{owner});
                var orderType=orders.GetType().GetGenericArguments()[0];var chassis=Activator.CreateInstance(orderType,true);
                Set(chassis,"Id",1L);Set(chassis,"Kind",PlayableResearchKind.TankChassis);Set(chassis,"CenterId",0);Set(chassis,"PaidCost",config.Profile.TankChassisCost);
                Set(chassis,"Elapsed",config.Profile.TankChassisSeconds);Set(chassis,"Duration",config.Profile.TankChassisSeconds);Set(chassis,"Active",false);Set(chassis,"Complete",true);orders.Add(chassis);
                int hq=authority.View(ownerId).Buildings.Single(building=>building.Owner==owner&&building.Kind==PlayableBuildingKind.Headquarters).Id;
                OfflineParticipantAuthorityTests.Call(authority,"Damage",hq,10000);OfflineParticipantAuthorityTests.Step(authority,1);
                CollectionAssert.Contains(authority.EliminatedOwners,ownerId);
                var after=authority.View(ownerId).Entities.Where(entity=>entity.Owner==owner&&entity.Kind==PlayableEntityKind.Tank).OrderBy(entity=>entity.Upgraded).ToArray();
                Assert.AreEqual(2,after.Length);Assert.False(after[0].Upgraded);Assert.True(after[1].Upgraded);
                Assert.IsEmpty((IList)OfflineParticipantAuthorityTests.Call(authority,"Research",owner),"elimination removes dynamic research while authored flags remain authoritative");
                foreach(var unit in after)
                {
                    Assert.True(authority.Navigation.Crowd.TryGet(unit.Id,out var nav));
                    Assert.AreEqual(unit.Upgraded?config.Profile.TankChassisSpeed:config.Profile.TankSpeed,nav.Speed,1e-9,"authored override mobility after elimination for id="+unit.Id);
                }
                var restored=OfflineParticipantAuthority.Restore(authority.CaptureBytes(),config);
                CollectionAssert.AreEqual(authority.CaptureBytes(),restored.CaptureBytes(),"authored elimination state remains strictly restorable");
        }
    }
}
