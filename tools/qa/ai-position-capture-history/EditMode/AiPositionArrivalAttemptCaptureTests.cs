using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using static Spacewars.Tests.EditMode.AiPositionLowerCaptureDiagnosticTests;
namespace Spacewars.Tests.EditMode
{
    public sealed class AiPositionArrivalAttemptCaptureTests
    {
        [Test,Timeout(180000)] public void Existing4b9CoveringArrivalAttemptBranch()
        {
            const string expectedHash="a07e7e94710a3dcb95e5c6495230bfce33d157e4c1268b3f03c0de7ac02c6706";
            var input=Environment.GetEnvironmentVariable("AI_POSITION_CAPTURE");var source=Environment.GetEnvironmentVariable("AI_POSITION_SOURCE_SHA");Assert.IsNotEmpty(input);Assert.IsNotEmpty(source);
            var bytes=File.ReadAllBytes(input);Assert.AreEqual(4620541,bytes.Length);using(var hash=SHA256.Create())Assert.AreEqual(expectedHash,BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant());
            long tick;int transportLength;using(var state=PlayableWorldState.Decode(bytes)){Assert.AreEqual(22,state.Version);Assert.AreEqual(7,state.Seed);Assert.AreEqual(107,state.Generation);Assert.AreEqual("ai-position-development-first-witness-v1",state.SourceIdentity);tick=state.Tick;transportLength=state.RuntimeTransport.Length;Assert.Greater(transportLength,0);}
            var output=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(input))),"arrival-4b9a-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);var clock=Stopwatch.StartNew();
            void Mark(string phase,object details=null)=>File.AppendAllText(Path.Combine(output,"phases.jsonl"),Newtonsoft.Json.JsonConvert.SerializeObject(new{phase,elapsedSeconds=clock.Elapsed.TotalSeconds,details})+"\n");
            const int seed=7;const long generation=107;
            var p=PlayableProfile.Default;var ai=NativeBalanceFields.InitialAiOpeningProfile();
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,-15),new NavPoint(-20,-27),pin:1),new OfflineStart("east",2,new NavPoint(20,15),new NavPoint(20,27),pin:2)};
            var home=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);
            var enemy=new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,p);
            var front=new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(-20,15),p);
            var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter};
            Assert.GreaterOrEqual(home.Slots.Count,kinds.Length);
            var buildings=kinds.Select((kind,i)=>new OfflineScenarioBuilding(1,kind,1,home.Slots[i].Id,home.Slots[i].Position,home.Slots[i].Heading)).Concat(new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Outpost,3,0,front.Position)}).ToArray();
            var units=Enumerable.Range(0,5).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-25+i*3,-1))).Concat(new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(20,-24))}).ToArray();
            var config=new OfflineMatchConfiguration(p,"ai-position-development-first-witness-v1","flat-public-position-v1","UnityHostRouteService",seed,new[]{new OfflineParticipant("position-owner",1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},starts,new[]{home,enemy,front},PlayableMap.StaticObstacles(p),new double[,]{{0,100},{100,0}},scenario:units,scenarioBuildings:buildings,spectators:new[]{PlayableRuntime.LocalSpectatorId});


            Assert.AreEqual("8d2ab24c1989b3fa52b21f175a4fbfe9b29e423eae5328ae55e606b9566c64ff",ai.Hash);Assert.AreEqual("unity-owner-research-queue-u6-v1",p.ProfileId);Assert.AreEqual(1,p.Revision);
            var ids=new HashSet<long>(Enumerable.Range(105,7).Select(i=>(long)i));
            PlayableRuntime clone=null;PlayableAuthorityTick authority=null;byte[] roundtrip=null;Exception failure=null;
            try
            {
                Mark("restore-before");clone=PlayableRuntime.RestoreBytesForTests(bytes,config,ai,r=>
                {
                    try
                    {
                        Mark("prestart-enter");authority=(PlayableAuthorityTick)Read(r,"authorityTick");var nav=authority.Domain.Navigation;Assert.AreEqual(tick,authority.Latest.Tick);
                        var allocating=Values(Read(nav,"allocating")).OfType<NavigationRequest>().Where(q=>ids.Contains(q.Request)).OrderBy(q=>q.Request).ToArray();Assert.AreEqual(7,allocating.Length);
                        var host=(NavigationHostRoot)Read(nav,"committedHostRoot");
                        var attempts=Values(Read(nav,"arrivalAttempts")).OfType<NavigationSession.ArrivalAttempt>().Where(a=>a.Intents.Any(q=>ids.Contains(q.Request))).OrderBy(a=>a.Id).ToArray();
                        var rows=attempts.Select(a=>
                        {
                            var work=a.OwnedWork;var history=a.Journal;NavOwnedStorage hostWork=null,hostIntents=null,hostHistory=null;
                            host?.ArrivalWork.TryGetValue(a.Id,out hostWork);host?.ArrivalIntentSets.TryGetValue(a.Id,out hostIntents);host?.ArrivalHistory.TryGetValue(a.Id,out hostHistory);
                            return new{a.Id,a.Group,a.Epoch,status=a.SnapshotStatus.ToString(),a.SnapshotPhase,a.SnapshotCandidate,a.SnapshotOccupancy,a.SnapshotMember,a.Frontier,
                                a.Published,a.Retiring,a.Retirement,a.RetirementReason,a.LegacyProviderAccounting,persistedRetiringCompute=a.RestoredRetiringCompute,
                                working="UNKNOWN: normal Working is not serialized; no original worker started",reconstruction=new{restoredValue=a.Reconstructing,basis=(bool)Read(nav,"physicalArrivalEnabled")?"arrival wire4 derives false from physical host state, not a historical runtime flag":"derived legacy reconstruction state; original transient flag UNKNOWN"},
                                selectedIntents=a.Intents.Where(q=>ids.Contains(q.Request)).Select(q=>new{request=Request(q),sameAllocatingReference=allocating.Any(current=>ReferenceEquals(current,q)),profileMatches=q.ProfileId==p.ProfileId&&q.ProfileRevision==p.Revision,
                                    baseGeometryAlias=ReferenceEquals(q.BaseGeometry,Read(nav,"navigationGeometry"))||host?.AuthorityGeometry!=null&&ReferenceEquals(q.BaseGeometry.StorageRoot,host.AuthorityGeometry)}).ToArray(),
                                work=new{present=a.Work!=null,ownedPresent=work!=null,ownedIntentsPresent=a.OwnedIntents!=null,ownedHistoryPresent=a.OwnedHistory!=null,hostWorkPresent=hostWork!=null,hostIntentsPresent=hostIntents!=null,hostHistoryPresent=hostHistory!=null,ownedRegistered=(bool?)Read(work,"IsRegistered"),workIsOwnedAlias=ReferenceEquals(a.Work,work),hostWorkAlias=ReferenceEquals(hostWork,work),hostIntentsAlias=ReferenceEquals(hostIntents,a.OwnedIntents),intentsArrayAlias=ReferenceEquals(a.Intents,a.OwnedIntents?.Requests),hostHistoryAlias=ReferenceEquals(hostHistory,a.OwnedHistory),currentHistoryAlias=ReferenceEquals(a.History,a.OwnedHistory),
                                    status=Read(work,"Status")?.ToString(),proposalCount=Read(Read(work,"Proposals"),"Count")},
                                history=new{count=history?.Count??0,firstEpoch=history!=null&&history.Count>0?(long?)history[0].Epoch:null,lastEpoch=history!=null&&history.Count>0?(long?)history[history.Count-1].Epoch:null}};
                        }).ToArray();
                        var uncovered=allocating.Where(q=>!attempts.Any(a=>!a.Retiring&&a.Intents.Any(i=>ReferenceEquals(i,q)))).ToArray();object eligibility=null;
                        if(uncovered.Length>0)
                        {
                            var bank=(NavPagedList<long>)Read(nav,"creditBank");object first=null,covering=null;long required=uncovered.Max(q=>q.Request);int count=bank==null?0:checked((int)bank[NavigationAccruedCredits.Count]);
                            if(bank!=null&&count>0){var head=NavigationAccruedCredits.First(bank);first=Scalars(head,"Tick","Prefix","Budget","Revision");for(int n=0;n<count;n++){int at=NavigationAccruedCredits.Header+4*(int)((bank[NavigationAccruedCredits.Head]+n)%NavigationAccruedCredits.Maximum);if(bank[at+1]>=required){covering=new{Tick=bank[at],Prefix=bank[at+1],Budget=bank[at+2],Revision=bank[at+3]};break;}}}
                            eligibility=new{uncovered=uncovered.Select(q=>q.Request).ToArray(),physicalArrivalEnabled=Read(nav,"physicalArrivalEnabled"),tickQuantaEnabled=nav.TickQuantaEnabled,
                                currentQuantum=Scalars(host,"AccruedCredits","TickQuantum","QuantumRunnable","QuantumGeneration","QuantumTick","QuantumSerial","ConsumedQuantum","QuantumCompleted","QuantumBudget","AdmissionPrefix","FairArrivals","LastServedArrival","ArrivalRoundEnd"),
                                ingress=uncovered.Select(q=>new{q.Request,allowedBySerializedQuantum=!nav.TickQuantaEnabled||host?.QuantumRunnable==true&&q.Request<=host.AdmissionPrefix}).ToArray(),
                                savedCreditBank=new{present=bank!=null,count,first,firstCoveringRequiredPrefix=covering,required},
                                boundary="Only persisted prefix/quantum/credit scalars read. No credit promotion, birth, Ensure or producer advance; transient scope/admission/working guards UNKNOWN."};
                        }
                        var facts=new{source,producer="4b9ab9e15dae939beea6d3330d767d1dcc9c3af2",inputHash=expectedHash,tick,transportLength,physicalArrivalEnabled=Read(nav,"physicalArrivalEnabled"),requests=allocating.Select(q=>q.Request).ToArray(),coveringAttemptCount=attempts.Length,attempts=rows,uncoveredEligibility=eligibility,
                            evidenceBoundary="Only covering allocation attempts and exact owned-role aliases from restored snapshot. No solver graph traversal, no new worker, tick, Ensure/Service/Pump/Quote or state change. CurrentArrivalIntent was not invoked: reported equality components are not all historical publication guards."};
                        File.WriteAllText(Path.Combine(output,"arrival.json"),Newtonsoft.Json.JsonConvert.SerializeObject(facts,Newtonsoft.Json.Formatting.Indented));Mark("readback-written");roundtrip=r.CaptureBeforeStartForTests();Mark("captured",new{bytes=roundtrip.Length});
                    }
                    catch(Exception error){failure=error;Mark("failure",error.ToString());}
                    finally{r.RequestStop();}
                });
                Assert.True(SpinWait.SpinUntil(()=>clone.IsStopped,5000));if(failure!=null)Assert.Fail(failure.ToString());using(var after=PlayableWorldState.Decode(roundtrip)){Assert.AreEqual(tick,after.Tick);Assert.AreEqual(transportLength,after.RuntimeTransport.Length);}
                ExactByteAssert.AreEqual(bytes,roundtrip,"arrival branch readback must preserve all Runtime bytes");Mark("exact-after");
            }
            finally{if(clone!=null){clone.RequestStop();Assert.True(SpinWait.SpinUntil(()=>clone.IsStopped,5000));clone.Dispose();}if(authority!=null){authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();}Mark("cleanup-after");}
        }
    }
}
