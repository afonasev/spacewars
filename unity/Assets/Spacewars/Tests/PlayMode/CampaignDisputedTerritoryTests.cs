using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class CampaignDisputedTerritoryTests
{
    Action missionCleanup;
    [TearDown] public void StopOwnedMission(){missionCleanup?.Invoke();missionCleanup=null;}
    // Observe only retained immutable publication roots. Never traverse worker-owned work.
    static void ObserveDemandPolicy(NavigationRoutePort port,ref bool demandSeen,ref bool compilerSeen,ref bool certificateSeen)
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
        var session=(NavigationSession)typeof(NavigationRoutePort).GetField("session",flags).GetValue(port);
        var root=session.PinCommittedHostRoot();if(root==null)return;
        bool foundDemand=false,foundCompiler=false,foundCertificate=false,demandPreviouslySeen=demandSeen,certificatePreviouslySeen=certificateSeen;
        try
        {
            Assert.True(root.IsImmutableCaptureRoot,"policy observation requires a sealed host root");
            var visited=new System.Collections.Generic.HashSet<NavigationArrivalWork>();
            void Observe(NavigationArrivalWork work)
            {
                if(work==null||!visited.Add(work))return;
                Assert.True((bool)typeof(NavigationArrivalWork).GetProperty("IsImmutableCaptureRoot",flags).GetValue(work),"policy observation requires sealed arrival work");
                var scheduler=(LayeredNavigationScheduler)typeof(NavigationArrivalWork).GetField("scheduler",flags).GetValue(work);
                var jobs=typeof(LayeredNavigationScheduler).GetField("jobs",flags).GetValue(scheduler);
                var first=jobs.GetType().GetProperty("First",flags);Assert.IsNotNull(first,"unknown scheduler queue shape");
                var job=first.GetValue(jobs);
                if(job!=null)
                {
                    var request=(NavScheduledRequest)job.GetType().GetProperty("Request",flags).GetValue(job);
                    foundDemand|=request.DemandQuery;
                    foundCompiler|=job.GetType().GetProperty("Compiler",flags).GetValue(job)!=null;
                    if(request.CertifiedLazyPartition){Assert.True(request.RectangularOrigins,"fresh mission must select paid rectangle Origins");var demand=job.GetType().GetProperty("Demand",flags).GetValue(job);var store=demand?.GetType().GetField("lazyStore",flags).GetValue(demand);var proof=store?.GetType().GetField("Exclusion",flags).GetValue(store);if(proof!=null&&proof.GetType().GetProperty("Progress",flags).GetValue(proof).ToString()=="Certified"){foundCertificate=true;if(!certificatePreviouslySeen){var cursor=proof.GetType().GetField("Cursor",flags).GetValue(proof);var cert=cursor.GetType().GetField("TerminalCertificate",flags).GetValue(cursor);Debug.Log("CAMPAIGN_CAPACITY_CERTIFIED excluded="+cert.GetType().GetField("ExcludedCount",flags).GetValue(cert)+" remaining="+cert.GetType().GetField("RemainingCount",flags).GetValue(cert));}}}
                    if(request.DemandQuery&&!demandPreviouslySeen)Debug.Log("CAMPAIGN_DEMAND_POLICY DemandQuery="+request.DemandQuery+" V6="+request.DemandQueryV6+" certified="+request.CertifiedLazyPartition+" rectangularOrigins="+request.RectangularOrigins+" compiler="+(foundCompiler?"present":"none"));
                }
                var oracle=typeof(NavigationArrivalWork).GetProperty("historicalOracle",flags);Assert.IsNotNull(oracle,"unknown historical oracle shape");
                Observe((NavigationArrivalWork)oracle.GetValue(work));
            }
            foreach(var pair in root.ArrivalWork)if(pair.Value is NavigationArrivalWork work)Observe(work);
        }
        finally{typeof(NavOwnedStorage).GetMethod("Release",flags).Invoke(root,null);}
        if(foundCompiler&&!compilerSeen)Debug.Log("CAMPAIGN_EAGER_COMPILER_OBSERVED");
        demandSeen|=foundDemand;compilerSeen|=foundCompiler;certificateSeen|=foundCertificate;
    }
    static IEnumerator Until(Func<bool> condition,double seconds,string reason,PlayableRuntime r,UnityHostRouteService routes,PlayableProfile p,Action observe=null)
    {
        double start=Time.realtimeSinceStartupAsDouble;long startTick=r.Latest?.Tick??0;double end=start+seconds;
        long nextMovementReport=startTick;bool movementDiagnostic=Environment.GetEnvironmentVariable("CAMPAIGN_MOVEMENT_DIAGNOSTIC")=="1";
        void MovementReport(){if(!movementDiagnostic||r.Latest==null||r.Latest.Tick<nextMovementReport)return;nextMovementReport=r.Latest.Tick+300;
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;var port=r.NavigationBinding?.RoutePort;if(port==null)return;var session=(NavigationSession)typeof(NavigationRoutePort).GetField("session",flags).GetValue(port);
            object Read(string name)=>typeof(NavigationSession).GetProperty(name,flags).GetValue(session);
            Debug.Log("CAMPAIGN_MOVEMENT tick="+r.Latest.Tick+" phase="+reason+" publications="+Read("OwnedMovementPublications")+" refusals="+Read("OwnedMovementRefusals")+" lastReason="+Read("LastOwnedMovementRefusal")+" message="+Read("LastOwnedMovementRefusalMessage")+" physical="+port.PhysicalRetainedBytes+" held="+port.PhysicalHeldBytes+" players="+string.Join(";",r.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player).Select(e=>e.Id+":"+e.Position.X+","+e.Position.Z+":"+e.NavigationOutcome)));}

        while(!condition()&&Time.realtimeSinceStartupAsDouble<end){routes.Service(r,p.NavigationRequestsPerFrame);foreach(var receipt in r.DrainReceipts())Debug.Log("CAMPAIGN_RECEIPT sequence="+receipt.Sequence+" tick="+receipt.AppliedTick+" status="+receipt.Status+" message="+receipt.Message);Assert.False(r.IsStopped,"tick="+r.Latest?.Tick+" "+r.Latest?.Failure);observe?.Invoke();MovementReport();yield return null;}
        bool complete=condition();long ticks=(r.Latest?.Tick??startTick)-startTick;Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,"CAMPAIGN_WAIT {0}: wall_s={1:F3} ticks={2} simulated_s={3:F3} complete={4}",reason,Time.realtimeSinceStartupAsDouble-start,ticks,ticks/30d,complete));
        if(!complete)
        {
            Debug.Log("CAMPAIGN_NAV_STATE tick="+r.Latest.Tick+" runtimePending="+r.Latest.Metrics.NavigationPending+" epoch="+routes.SolverEpoch+" projection="+routes.LastProjectionFailure+" workerFailure="+routes.CertifiedPublication?.Failure+" pumps="+routes.CertifiedPublication?.Pumps+" "+string.Join(";",typeof(NavSchedulerCounters).GetProperties().Select(v=>v.Name+"="+v.GetValue(routes.CertifiedCounters)))+" actors="+string.Join(";",r.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player).Select(e=>e.Id+":order="+e.CurrentOrder?.Kind+":stamp="+e.OrderStamp?.Sequence+":active="+e.Queue?.Active?.CommandSequence+":pending="+e.Queue?.Pending?.CommandSequence+":completion="+e.Queue?.Completion?.Order?.Sequence+":outcome="+e.NavigationOutcome)));
            string evidence=Environment.GetEnvironmentVariable("CAMPAIGN_EVIDENCE");
            if(!string.IsNullOrEmpty(evidence)){var capture=r.RequestCaptureBytes();double limit=Time.realtimeSinceStartupAsDouble+3;while(!capture.IsCompleted&&Time.realtimeSinceStartupAsDouble<limit){routes.Service(r,p.NavigationRequestsPerFrame);yield return null;}if(capture.Status==System.Threading.Tasks.TaskStatus.RanToCompletion){System.IO.Directory.CreateDirectory(evidence);System.IO.File.WriteAllBytes(System.IO.Path.Combine(evidence,"failure-state.bin"),capture.Result);}else Debug.Log("CAMPAIGN_FAILURE_CAPTURE "+capture.Status+" "+capture.Exception);}
        }
        Assert.True(complete,reason+" receipts="+string.Join(";",r.ReceiptsAfter(0).Select(v=>v.Sequence+":"+v.Status+":"+v.Message))+" tick="+r.Latest?.Tick+" failure="+r.Latest?.Failure+" entities="+string.Join(";",r.Latest.Entities.Select(e=>e.Id+":"+e.Owner+":"+e.Health+":"+e.Position.X+","+e.Position.Z+":"+e.NavigationOutcome))+" defenders="+string.Join(";",r.ParticipantView("corporation").Entities.Where(e=>e.Owner==PlayableOwner.Enemy).Select(e=>e.Id+":"+e.Health+":"+e.Position.X+","+e.Position.Z+":"+e.Moving+":"+e.TargetId)));
    }
    [UnityTest] public IEnumerator OrdinaryThreadedGroupContact()
    {
        var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());var config=CampaignReconOpening.Configuration(p);
        var r=new PlayableRuntime(config,105);var routes=new UnityHostRouteService();long sequence=0;
        void Command(PlayableCommandKind kind,NavPoint point,int target=0){var ids=r.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player).Select(e=>e.Id).ToArray();var receipt=r.TrySubmit(new PlayableCommand(r.Generation,++sequence,"player-1",kind,ids,point,targetId:target));Assert.True(receipt.Accepted,"command="+kind+" target="+target+" sequence="+sequence+" status="+receipt.Status+" tick="+r.Latest.Tick+" visible="+r.Latest.Vision.IsVisible(point));}
        try
        {
            yield return Until(()=>r.IsPrepared,60,"ordinary preparation",r,routes,p);
            var first=r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player);Command(PlayableCommandKind.Move,CampaignReconOpening.Rendezvous);
            yield return Until(()=>r.Latest.Entities.Any(e=>e.Id==first.Id&&Math.Abs(e.Position.Z-first.Position.Z)>.1),30,"threaded first motion",r,routes,p);
            Assert.IsNotNull(routes.CertifiedPublication);Debug.Log("disputed witness: ordinary threaded first motion");
            yield return Until(()=>r.Latest.Mission.Reinforced,60,"first +3",r,routes,p);
            Command(PlayableCommandKind.Move,CampaignReconOpening.Explore);
            yield return Until(()=>r.Latest.Mission.MineDiscovered,90,"mine discovery",r,routes,p);
            var mine=r.Latest.Buildings.Single(b=>b.SiteId==1);Command(PlayableCommandKind.Attack,mine.Position,mine.Id);
            yield return Until(()=>r.Latest.Mission.MineDestroyed,120,"mine combat",r,routes,p);
            Command(PlayableCommandKind.AttackMove,CampaignReconOpening.Fork);
            yield return Until(()=>r.Latest.Entities.Any(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Explorer&&(e.Health<e.MaxHealth||e.Moving||e.TargetId!=0)),90,"actual local contact",r,routes,p);
            Assert.False(r.Latest.Mission.GateOpen);Assert.AreEqual(PlayableMatchOutcome.Playing,r.Latest.Outcome);Debug.Log("disputed witness: group movement -> local guard contact PASS");
        }
        finally{r.RequestStop();routes.Dispose();}
    }
    [UnityTest] public IEnumerator HorizontalBOrdinaryCommandStartsMoving()
    {
        var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());var config=CampaignReconOpening.Configuration(p);
        var r=new PlayableRuntime(config,105);var routes=new UnityHostRouteService();
        try
        {
            yield return Until(()=>r.IsPrepared,60,"B ordinary preparation",r,routes,p);
            var first=r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player);
            var receipt=r.TrySubmit(new PlayableCommand(r.Generation,1,"player-1",PlayableCommandKind.Move,new[]{first.Id},CampaignReconOpening.Rendezvous));Assert.True(receipt.Accepted);
            yield return Until(()=>r.Latest.Entities.Any(e=>e.Id==first.Id&&Math.Abs(e.Position.Z-first.Position.Z)>.1),30,"B threaded first motion",r,routes,p);
            Assert.IsNotNull(routes.CertifiedPublication);Assert.False(r.Latest.Mission.MineDiscovered);Assert.False(r.Latest.Mission.ForkRevealed);Assert.AreEqual(PlayableMatchOutcome.Playing,r.Latest.Outcome);
        }
        finally{r.RequestStop();routes.Dispose();}
    }
    [UnityTest, Timeout(600000)] public IEnumerator OrdinaryBranchesSecondCheckpointRetryAndFinalVictory()=>RunMission(false);
    [UnityTest, Timeout(240000)] public IEnumerator AuthenticSecondCheckpointFinalCombatDiagnostic()=>RunMission(true);
    [UnityTest, Timeout(240000)] public IEnumerator CapturedFinalCombatDiagnostic()=>RunMission(true);
    [UnityTest, Timeout(360000)] public IEnumerator OrdinaryEastThenWestReachesAuthenticSecondCheckpoint()=>RunMission(false,true,true);
    IEnumerator RunMission(bool finalOnly,bool eastFirst=false,bool branchesOnly=false)
    {
        var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());var config=CampaignReconOpening.Configuration(p);
        var r=new PlayableRuntime(config,106);var routes=new UnityHostRouteService();long sequence=0;
        System.Collections.Generic.Dictionary<int,int> guards=null;
        void Bounds()
        {
            if(guards==null)return;
            foreach(var e in r.ParticipantView("corporation").Entities.Where(e=>e.Owner==PlayableOwner.Enemy))Assert.True(CampaignReconOpening.Contains(CampaignReconOpening.GuardRegions[guards[e.Id]],e.Position,PlayableUnitRules.Radius(p,e.Kind)),"actual guard footprint left authored region: "+e.Id+" at "+e.Position.X+","+e.Position.Z);
        }
        IEnumerator Wait(Func<bool> done,double seconds,string reason)=>Until(()=>{Bounds();return done();},seconds,reason,r,routes,p);
        void Command(PlayableCommandKind kind,NavPoint point,int target=0)
        {
            var ids=r.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player).Select(e=>e.Id).ToArray();Assert.Greater(ids.Length,0,"army alive before command");
            var receipt=r.TrySubmit(new PlayableCommand(r.Generation,++sequence,"player-1",kind,ids,point,targetId:target));Assert.True(receipt.Accepted,"command="+kind+" target="+target+" sequence="+sequence+" status="+receipt.Status+" tick="+r.Latest.Tick+" visible="+r.Latest.Vision.IsVisible(point)+" outcome="+r.Latest.Outcome+" failure="+r.Latest.Failure);
        }
        IEnumerator Clear(Func<bool> done,int group,NavPoint[] scoutPoints)
        {
            double end=Time.realtimeSinceStartupAsDouble+180;int index=0;
            while(!done()&&Time.realtimeSinceStartupAsDouble<end)
            {
                Assert.AreEqual(PlayableMatchOutcome.Playing,r.Latest.Outcome,"army must survive actual authored combat; sector="+group+" tick="+r.Latest.Tick+" defenders="+string.Join(";",r.ParticipantView("corporation").Entities.Where(e=>e.Owner==PlayableOwner.Enemy).Select(e=>e.Id+":"+e.Health+":"+e.Position.X+","+e.Position.Z)));
                var enemy=r.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Enemy&&CampaignReconOpening.Contains(CampaignReconOpening.GuardRegions[group],e.Position)).OrderBy(e=>e.Kind==PlayableEntityKind.Tank?0:1).ThenBy(e=>e.Health).FirstOrDefault();
                var building=r.Latest.Buildings.Where(b=>b.Owner==PlayableOwner.Enemy&&CampaignReconOpening.GuardSites[group].Contains(b.SiteId)).OrderBy(b=>b.Kind==PlayableBuildingKind.Outpost?0:1).FirstOrDefault();
                if(enemy!=null||building!=null)
                {
                    int target=enemy?.Id??building.Id;
                    // One whole-squad approach, using ordinary Explorer moving fire. Explicit
                    // Attack stops at maximum range, where physical tracer spread wastes bursts.
                    // This is the already observed close approach, now with authored assault equipment.
                    bool approachTank=enemy?.Kind==PlayableEntityKind.Tank;
                    var aim=enemy?.Position??building.Position;
                    Command(approachTank?PlayableCommandKind.Move:PlayableCommandKind.Attack,
                        approachTank?new NavPoint(aim.X,aim.Z-2):aim,approachTank?0:target);
                    Debug.Log("CAMPAIGN_TACTIC approachTank="+approachTank+" sector="+group+" target="+target+" kind="+(enemy==null?"building":"unit")+" army="+string.Join(";",r.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player).Select(e=>e.Id+":"+e.Health+":"+e.Position.X+","+e.Position.Z)));
                    // Ordinary human tactic: interrupt a building attack when its visible guards engage.
                    // Explicit Attack otherwise keeps firing at the mine while defenders kill the army.
                    yield return Wait(()=>done()||!r.Latest.Entities.Any(e=>e.Id==target)&&!r.Latest.Buildings.Any(b=>b.Id==target)||enemy==null&&r.Latest.Entities.Any(e=>e.Owner==PlayableOwner.Enemy&&CampaignReconOpening.Contains(CampaignReconOpening.GuardRegions[group],e.Position)),60,"visible target destroyed/contact lost or guards require priority "+target);
                }
                else
                {
                    var point=scoutPoints[index++%scoutPoints.Length];Command(PlayableCommandKind.AttackMove,point);
                    yield return Wait(()=>done()||r.Latest.Entities.Any(e=>e.Owner==PlayableOwner.Enemy&&CampaignReconOpening.Contains(CampaignReconOpening.GuardRegions[group],e.Position))||r.Latest.Buildings.Any(b=>b.Owner==PlayableOwner.Enemy&&CampaignReconOpening.GuardSites[group].Contains(b.SiteId))||r.Latest.Entities.Any(e=>e.Owner==PlayableOwner.Player&&Math.Abs(e.Position.X-point.X)<3&&Math.Abs(e.Position.Z-point.Z)<3),60,"scouting authored branch");
                }
                Bounds();yield return null;
            }
            Assert.True(done(),"actual sector clearance "+group+" remaining="+string.Join(";",r.ParticipantView("corporation").Entities.Where(e=>e.Owner==PlayableOwner.Enemy).Select(e=>e.Id+":"+e.Health+":"+e.Position.X+","+e.Position.Z))+" buildings="+string.Join(";",r.ParticipantView("corporation").Buildings.Where(b=>b.Owner==PlayableOwner.Enemy).Select(b=>b.Id+":"+b.SiteId+":"+b.Health)));Debug.Log("disputed journey: sector "+group+" clear, army="+r.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player));
        }
        IEnumerator SaveVictory()
        {
            string proof=Environment.GetEnvironmentVariable("CAMPAIGN_EVIDENCE");if(string.IsNullOrEmpty(proof))yield break;
            r.RequestPause(true);yield return Wait(()=>r.Latest.Paused,30,"pause actual victorious world before capture");
            var capture=r.RequestCaptureBytes();yield return Wait(()=>capture.IsCompleted,20,"capture actual victorious world");
            Assert.AreEqual(System.Threading.Tasks.TaskStatus.RanToCompletion,capture.Status,capture.Exception?.ToString());
            System.IO.Directory.CreateDirectory(proof);System.IO.File.WriteAllBytes(System.IO.Path.Combine(proof,"victory-world.bin"),capture.Result);
        }
        missionCleanup=()=>
        {
            string evidence=Environment.GetEnvironmentVariable("CAMPAIGN_EVIDENCE");
            var last=r.Latest;string report="outcome="+last?.Outcome+" tick="+last?.Tick+" status="+last?.Status+" failure="+last?.Failure+"\nremaining="+string.Join(";",r.ParticipantView("corporation").Entities.Select(e=>e.Id+":"+e.Owner+":"+e.Health+":"+e.Position.X+","+e.Position.Z))+"\nbuildings="+string.Join(";",r.ParticipantView("corporation").Buildings.Select(b=>b.Id+":"+b.SiteId+":"+b.Health));
            Debug.Log("CAMPAIGN_TERMINAL "+report);
            if(!string.IsNullOrEmpty(evidence)){System.IO.Directory.CreateDirectory(evidence);System.IO.File.WriteAllText(System.IO.Path.Combine(evidence,(finalOnly?"captured-final":eastFirst?"east-first":"west-first")+"-terminal.txt"),report);}
            r.RequestStop();routes.Dispose();
        };
        try
        {
            if(finalOnly)
            {
                r.RequestStop();yield return Wait(()=>r.IsStopped,30,"unused genesis stops before authentic final restore");
                var bytes=System.IO.File.ReadAllBytes(Environment.GetEnvironmentVariable("CAMPAIGN_SECOND_CHECKPOINT"));
                r=PlayableRuntime.RestoreBytes(bytes,config);r.RequestPause(false);
                yield return Wait(()=>r.IsPrepared&&!r.Latest.Paused,60,"authentic final checkpoint prepared");sequence=r.HumanSequenceWatermark;
                Assert.True(r.Latest.Mission.FinalReinforced);Assert.False(r.Latest.Mission.Victory);
                guards=r.ParticipantView("corporation").Entities.Where(e=>e.Owner==PlayableOwner.Enemy).ToDictionary(e=>e.Id,e=>Enumerable.Range(0,4).Single(g=>CampaignReconOpening.Contains(CampaignReconOpening.GuardRegions[g],e.Position)));
                yield return Clear(()=>r.Latest.Mission.Victory,3,new[]{new NavPoint(0,184),new NavPoint(-35,211),new NavPoint(35,211)});
                Assert.AreEqual(PlayableMatchOutcome.PlayerWon,r.Latest.Outcome);Assert.IsNotNull(r.Result);yield return SaveVictory();yield break;
            }
            yield return Wait(()=>r.IsPrepared,60,"ordinary preparation");
            guards=r.ParticipantView("corporation").Entities.Where(e=>e.Owner==PlayableOwner.Enemy).ToDictionary(e=>e.Id,e=>Enumerable.Range(0,4).Single(g=>CampaignReconOpening.Contains(CampaignReconOpening.GuardRegions[g],e.Position)));
            Command(PlayableCommandKind.Move,CampaignReconOpening.Rendezvous);yield return Wait(()=>r.Latest.Mission.Reinforced,60,"first real reinforcement");
            yield return Wait(()=>r.HasCampaignCheckpoint,30,"first checkpoint");var firstCheckpoint=r.CampaignCheckpoint;
            Command(PlayableCommandKind.Move,CampaignReconOpening.Explore);yield return Wait(()=>r.Latest.Mission.MineDiscovered,90,"first mine revealed");
            var mine=r.Latest.Buildings.Single(b=>b.SiteId==1);Command(PlayableCommandKind.Attack,mine.Position,mine.Id);yield return Wait(()=>r.Latest.Mission.MineDestroyed,120,"first mine combat");
            Command(PlayableCommandKind.AttackMove,CampaignReconOpening.Fork);yield return Wait(()=>r.Latest.Mission.ForkRevealed,90,"fork revealed");
            yield return Clear(()=>!r.ParticipantView("corporation").Entities.Any(e=>e.Owner==PlayableOwner.Enemy&&guards[e.Id]==0),0,new[]{new NavPoint(0,38),CampaignReconOpening.Fork});
            IEnumerator WestBranch()=>Clear(()=>r.Latest.Mission.WestCleared,1,new[]{new NavPoint(-55,64),new NavPoint(-55,82),new NavPoint(-55,106),new NavPoint(-55,120)});
            IEnumerator EastBranch()=>Clear(()=>r.Latest.Mission.EastCleared,2,new[]{new NavPoint(68,64),new NavPoint(57,82),new NavPoint(70,90),new NavPoint(70,123),new NavPoint(84,105)});
            yield return eastFirst?EastBranch():WestBranch();
            Assert.False(r.Latest.Mission.GateOpen,eastFirst?"west still required":"east still required");
            yield return eastFirst?WestBranch():EastBranch();
            yield return Wait(()=>r.Latest.Mission.GateOpen,30,"gate opens after both actual sectors");int survivors=r.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player);
            Command(PlayableCommandKind.Move,CampaignReconOpening.Convergence);yield return Wait(()=>r.Latest.Mission.FinalReinforced,90,"four new allies join");
            Assert.AreEqual(survivors+4,r.Latest.Entities.Count(e=>e.Owner==PlayableOwner.Player));
            yield return Wait(()=>r.CampaignCheckpoint!=null&&!r.CampaignCheckpoint.SequenceEqual(firstCheckpoint),30,"authentic second checkpoint replaces first");
            var second=r.CampaignCheckpoint;
            string proof=Environment.GetEnvironmentVariable("CAMPAIGN_EVIDENCE");if(!string.IsNullOrEmpty(proof)){System.IO.Directory.CreateDirectory(proof);System.IO.File.WriteAllBytes(System.IO.Path.Combine(proof,"second-checkpoint.bin"),second);}
            var a=PlayableAuthorityTick.RestoreBytes(second,config);var z=PlayableAuthorityTick.RestoreBytes(second,config);
            using(var routesA=new UnityHostRouteService())using(var routesZ=new UnityHostRouteService())
            try
            {
                CollectionAssert.AreEqual(a.CaptureBytes(),z.CaptureBytes(),"second checkpoint exact initial full envelope");long target=a.Tick+30;double deadline=Time.realtimeSinceStartupAsDouble+30;
                while((a.Tick<target||z.Tick<target)&&Time.realtimeSinceStartupAsDouble<deadline){routesA.Service(a,p.NavigationRequestsPerFrame);routesZ.Service(z,p.NavigationRequestsPerFrame);if(a.Tick<target)a.TryAdvance();if(z.Tick<target)z.TryAdvance();yield return null;}
                Assert.AreEqual(target,a.Tick);Assert.AreEqual(target,z.Tick);CollectionAssert.AreEqual(a.CaptureBytes(),z.CaptureBytes(),"second checkpoint exact 30-tick full continuation");
            }
            finally{a.Stop();z.Stop();}
            if(branchesOnly){Assert.True(r.Latest.Mission.WestCleared&&r.Latest.Mission.EastCleared&&r.Latest.Mission.FinalReinforced);Assert.AreEqual(PlayableMatchOutcome.Playing,r.Latest.Outcome);Debug.Log("disputed reverse journey: east -> west -> four new allies -> authentic checkpoint PASS");yield break;}
            var expected=r.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player).OrderBy(e=>e.Id).Select(e=>e.Id+":"+e.Health).ToArray();
            r.RequestStop();yield return Wait(()=>r.IsStopped,30,"host stops before checkpoint retry");routes.Dispose();
            r=PlayableRuntime.RestoreBytes(second,config);r.RequestPause(true);routes=new UnityHostRouteService();
            yield return Wait(()=>r.IsPrepared&&r.Latest.Paused,60,"second checkpoint binds real restored host");sequence=r.HumanSequenceWatermark;
            Assert.True(r.Latest.Mission.FinalReinforced);Assert.True(r.Latest.Mission.GateOpen);Assert.False(r.Latest.Mission.Victory);
            CollectionAssert.AreEqual(expected,r.Latest.Entities.Where(e=>e.Owner==PlayableOwner.Player).OrderBy(e=>e.Id).Select(e=>e.Id+":"+e.Health).ToArray(),"IDs, losses, HP retained without duplicate or refill");CollectionAssert.AreEqual(second,r.CampaignCheckpoint);
            r.RequestPause(false);yield return Wait(()=>!r.Latest.Paused,30,"resume authentic retry");
            yield return Clear(()=>r.Latest.Mission.Victory,3,new[]{new NavPoint(0,184),new NavPoint(-35,211),new NavPoint(35,211)});
            Assert.AreEqual(PlayableMatchOutcome.PlayerWon,r.Latest.Outcome);Assert.IsNotNull(r.Result);Assert.True(r.Latest.Mission.Objectives.Single(g=>g.Id=="final").Complete);Debug.Log("disputed journey: authentic retry -> final combat -> victory PASS");
        }
        finally{StopOwnedMission();}
    }
    [UnityTest] public IEnumerator OrdinaryGuardPursuitLostContactAndReturn()
    {
        var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());var config=CampaignReconOpening.Configuration(p);var r=new PlayableRuntime(config,107);var routes=new UnityHostRouteService();long sequence=0;
        void Move(NavPoint point){int id=r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Id;Assert.True(r.TrySubmit(new PlayableCommand(r.Generation,++sequence,"player-1",PlayableCommandKind.Move,new[]{id},point)).Accepted);}
        try
        {
            yield return Until(()=>r.IsPrepared,60,"guard preparation",r,routes,p);
            var homes=r.ParticipantView("corporation").Entities.Where(e=>e.Owner==PlayableOwner.Enemy&&CampaignReconOpening.Contains(CampaignReconOpening.GuardRegions[0],e.Position)).ToDictionary(e=>e.Id,e=>e.Position);
            Move(new NavPoint(6,-30));yield return Until(()=>r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Position.Z>-32,60,"bypass exact rendezvous without transfer",r,routes,p);
            Move(new NavPoint(0,25));yield return Until(()=>r.ParticipantView("corporation").Entities.Any(e=>homes.ContainsKey(e.Id)&&Math.Abs(e.Position.Z-homes[e.Id].Z)>.1),90,"ordinary visible contact produces real guard movement",r,routes,p);
            Move(CampaignReconOpening.Explore);
            yield return Until(()=>r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Position.Z<16,30,"player retreats out of region",r,routes,p);
            yield return Until(()=>r.ParticipantView("corporation").Entities.Where(e=>homes.ContainsKey(e.Id)).All(e=>Math.Abs(e.Position.X-homes[e.Id].X)<=p.ExplorerCollisionRadius&&Math.Abs(e.Position.Z-homes[e.Id].Z)<=p.ExplorerCollisionRadius),60,"lost contact returns guards home",r,routes,p);
            Assert.AreEqual(2,r.ParticipantView("corporation").Entities.Count(e=>homes.ContainsKey(e.Id)),"return witness retains both guards alive");
            foreach(var e in r.ParticipantView("corporation").Entities.Where(e=>homes.ContainsKey(e.Id)))Assert.True(CampaignReconOpening.Contains(CampaignReconOpening.GuardRegions[0],e.Position,p.ExplorerCollisionRadius));
        }
        finally{r.RequestStop();routes.Dispose();}
    }

    [UnityTest] public IEnumerator VisibleOutsideLeashTargetCanBeAttackedThenGuardsReturn()
    {
        var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());var c=CampaignReconOpening.Configuration(p);var r=new PlayableRuntime(c,108);var routes=new UnityHostRouteService();long sequence=0;
        void Move(NavPoint point){int id=r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Id;Assert.True(r.TrySubmit(new PlayableCommand(r.Generation,++sequence,"player-1",PlayableCommandKind.Move,new[]{id},point)).Accepted);}
        IEnumerator Wait(Func<bool> done,double seconds,string reason)=>Until(done,seconds,reason,r,routes,p);
        try
        {
            yield return Wait(()=>r.IsPrepared,60,"outside contact preparation");
            var homes=r.ParticipantView("corporation").Entities.Where(e=>e.Owner==PlayableOwner.Enemy&&CampaignReconOpening.Contains(CampaignReconOpening.GuardRegions[1],e.Position)).ToDictionary(e=>e.Id,e=>e.Position);
            Move(new NavPoint(6,-30));yield return Wait(()=>r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Position.Z>-32,60,"bypass rendezvous");
            Move(new NavPoint(-25,20));yield return Wait(()=>r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Position.Z>18,60,"western approach");
            Move(new NavPoint(-55,60));yield return Wait(()=>r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Position.X< -52,60,"quarry approach");
            Move(new NavPoint(-55,81));yield return Wait(()=>r.ParticipantView("corporation").Entities.Any(e=>homes.ContainsKey(e.Id)&&Math.Abs(e.Position.Z-homes[e.Id].Z)>.1),60,"local guards pursue real contact");
            Move(new NavPoint(-55,54));yield return Wait(()=>r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Position.Z<56,30,"player crosses outside authored leash");
            int hp=r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Health;
            yield return Wait(()=>r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Health<hp,30,"outside target takes normal guard fire");
            foreach(var e in r.ParticipantView("corporation").Entities.Where(e=>homes.ContainsKey(e.Id)))Assert.True(CampaignReconOpening.Contains(CampaignReconOpening.GuardRegions[1],e.Position,p.ExplorerCollisionRadius));
            Move(new NavPoint(-55,42));yield return Wait(()=>r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Position.Z<45,30,"contact lost beyond normal vision");
            yield return Wait(()=>r.ParticipantView("corporation").Entities.Where(e=>homes.ContainsKey(e.Id)).All(e=>Math.Abs(e.Position.X-homes[e.Id].X)<=p.ExplorerCollisionRadius&&Math.Abs(e.Position.Z-homes[e.Id].Z)<=p.ExplorerCollisionRadius),60,"guards immediately replace chase with home return");
            Assert.AreEqual(2,r.ParticipantView("corporation").Entities.Count(e=>homes.ContainsKey(e.Id)));
        }
        finally{r.RequestStop();routes.Dispose();}
    }

    // Independent straight-corridor witness. The failed western movement witness remains unchanged/open.
    [UnityTest] public IEnumerator VisibleOutsideCorridorLeashCanBeAttackedAndReturn()
    {
        var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());var c=CampaignReconOpening.Configuration(p);var r=new PlayableRuntime(c,110);var routes=new UnityHostRouteService();long sequence=0;
        void Move(NavPoint point){int id=r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Id;Assert.True(r.TrySubmit(new PlayableCommand(r.Generation,++sequence,"player-1",PlayableCommandKind.Move,new[]{id},point)).Accepted);}
        IEnumerator Wait(Func<bool> done,double seconds,string reason)=>Until(done,seconds,reason,r,routes,p);
        try
        {
            yield return Wait(()=>r.IsPrepared,60,"corridor outside contact preparation");
            var homes=r.ParticipantView("corporation").Entities.Where(e=>e.Owner==PlayableOwner.Enemy&&CampaignReconOpening.Contains(CampaignReconOpening.GuardRegions[0],e.Position)).ToDictionary(e=>e.Id,e=>e.Position);
            Move(new NavPoint(6,-30));yield return Wait(()=>r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Position.Z>-32,60,"bypass rendezvous");
            Move(new NavPoint(0,25));yield return Wait(()=>r.ParticipantView("corporation").Entities.Any(e=>homes.ContainsKey(e.Id)&&Math.Abs(e.Position.Z-homes[e.Id].Z)>.1),90,"real local contact and pursuit");
            foreach(double z in new[]{25d,21d,17d})
            {
                Move(new NavPoint(0,z));
                yield return Wait(()=>{var player=r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player);return Math.Abs(player.Position.Z-z)<=p.ExplorerCollisionRadius&&r.ParticipantView("corporation").Entities.Any(e=>homes.ContainsKey(e.Id)&&Math.Sqrt(Math.Pow(e.Position.X-player.Position.X,2)+Math.Pow(e.Position.Z-player.Position.Z,2))<=p.ExplorerRange);},30,"settled inside contact and actual firing distance at z="+z);
            }
            yield return Wait(()=>r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Health<p.ExplorerHealth,30,"establish actual guard fire inside the leash before moving outside");
            Move(new NavPoint(0,14));yield return Wait(()=>r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Position.Z<16,30,"outside corridor leash");
            var outside=r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player);
            Assert.True(r.ParticipantView("corporation").Entities.Any(e=>e.Id==outside.Id),"outside target must remain legitimately visible");
            Assert.True(r.ParticipantView("corporation").Entities.Any(e=>homes.ContainsKey(e.Id)&&Math.Sqrt(Math.Pow(e.Position.X-outside.Position.X,2)+Math.Pow(e.Position.Z-outside.Position.Z,2))<=p.ExplorerVision),"outside target remains in local guard sight");
            int hp=outside.Health;
            yield return Wait(()=>r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Health<hp,30,"visible outside target takes normal guard fire");
            foreach(var e in r.ParticipantView("corporation").Entities.Where(e=>homes.ContainsKey(e.Id)))Assert.True(CampaignReconOpening.Contains(CampaignReconOpening.GuardRegions[0],e.Position,p.ExplorerCollisionRadius));
            Move(new NavPoint(6,-8));yield return Wait(()=>r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Position.Z< -5&&!r.ParticipantView("corporation").Entities.Any(e=>e.Owner==PlayableOwner.Player),30,"actual loss of sight beyond the guard region and normal vision");
            yield return Wait(()=>r.ParticipantView("corporation").Entities.Where(e=>homes.ContainsKey(e.Id)).All(e=>Math.Abs(e.Position.X-homes[e.Id].X)<=p.ExplorerCollisionRadius&&Math.Abs(e.Position.Z-homes[e.Id].Z)<=p.ExplorerCollisionRadius),60,"return after actual outside attack");
            Assert.AreEqual(2,r.ParticipantView("corporation").Entities.Count(e=>homes.ContainsKey(e.Id)));
        }
        finally{r.RequestStop();routes.Dispose();}
    }

    // Focused route isolation from the observed west-survivor position. One actor, cleared west
    // buildings, unchanged authored terrain/profile/host. This cannot qualify mission combat or completion.
    [UnityTest] public IEnumerator OrdinaryWestToEastAppliedRouteWitness()
    {
        var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());var authored=CampaignReconOpening.Configuration(p);
        var start=new NavPoint(-61.5089109845236,108.075765746673);var goal=new NavPoint(68,64);
        var config=new OfflineMatchConfiguration(p,authored.SourceIdentity+"-west-route-witness",authored.MapIdentity,authored.RouteProvenance,authored.Seed,authored.Roster.ToArray(),authored.Starts.ToArray(),authored.Sites.ToArray(),authored.Obstacles.ToArray(),new double[3,3],scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,start)},terrain:p.AuthoredMap.Id,scenarioBuildings:authored.ScenarioBuildings.Where(b=>b.SiteId>=4).ToArray(),missionId:CampaignReconOpening.Id);
        var r=new PlayableRuntime(config,113);var routes=new UnityHostRouteService();
        try
        {
            yield return Until(()=>r.IsPrepared,60,"minimal west-route preparation",r,routes,p);
            var save=r.RequestCaptureBytes();yield return Until(()=>save.IsCompleted,30,"minimal idle checkpoint",r,routes,p);Assert.AreEqual(System.Threading.Tasks.TaskStatus.RanToCompletion,save.Status,save.Exception?.ToString());
            string proof=Environment.GetEnvironmentVariable("CAMPAIGN_EVIDENCE");if(!string.IsNullOrEmpty(proof)){System.IO.Directory.CreateDirectory(proof);System.IO.File.WriteAllBytes(System.IO.Path.Combine(proof,"route-initial.bin"),save.Result);}
            int id=r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Id;
            Assert.True(r.TrySubmit(new PlayableCommand(r.Generation,1,"player-1",PlayableCommandKind.AttackMove,new[]{id},goal)).Accepted);
            yield return Until(()=>r.ReceiptsAfter(0).Any(v=>v.Sequence==1&&v.Status!=PlayableCommandStatus.Accepted),30,"actual west-east command application",r,routes,p);
            Assert.AreEqual(PlayableCommandStatus.Applied,r.ReceiptsAfter(0).Single(v=>v.Sequence==1&&v.Status!=PlayableCommandStatus.Accepted).Status);
            bool moved=false,demandSeen=false,compilerSeen=false,certificateSeen=false;
            double routeStart=Time.realtimeSinceStartupAsDouble;long routeTickStart=r.Latest.Tick;int observation=0;
            double[] observationOffsets={10,35};
            Func<bool> arrived=()=>{ObserveDemandPolicy(r.NavigationBinding.RoutePort,ref demandSeen,ref compilerSeen,ref certificateSeen);var e=r.Latest.Entities.Single(v=>v.Id==id);if(!moved&&Math.Abs(e.Position.X-start.X)+Math.Abs(e.Position.Z-start.Z)>.1){moved=true;Debug.Log("CAMPAIGN_ROUTE_FIRST_MOTION tick="+r.Latest.Tick);}return Math.Abs(e.Position.X-goal.X)<3&&Math.Abs(e.Position.Z-goal.Z)<3;};
            Action observe=()=>
            {
                double elapsed=Time.realtimeSinceStartupAsDouble-routeStart;
                if(observation<observationOffsets.Length&&elapsed>=observationOffsets[observation])
                {
                    var binding=r.NavigationBinding;var publication=routes.CertifiedPublication;
                    Debug.Log("CAMPAIGN_ARRIVAL_OBSERVATION index="+(observation+1)+" elapsed_s="+elapsed.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+
                        " ticks="+(r.Latest.Tick-routeTickStart)+" runtimePending="+r.Latest.Metrics.NavigationPending+" queuePending="+r.Latest.Entities.Single(v=>v.Id==id).Queue?.Pending?.CommandSequence+
                        " requestQueue="+binding?.RoutePort?.QueuedRequestCount+" workerPumps="+publication?.Pumps+" solverEpoch="+publication?.Epoch+
                        " authority="+binding?.RoutePort?.ArrivalAuthoritySnapshot+
                        " physicalRetained_nonAtomic="+binding?.RoutePort?.PhysicalRetainedBytes+" physicalHeld_nonAtomic="+binding?.RoutePort?.PhysicalHeldBytes+
                        " position="+r.Latest.Entities.Single(v=>v.Id==id).Position.X.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+","+r.Latest.Entities.Single(v=>v.Id==id).Position.Z.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
                    observation++;
                }
            };
            yield return Until(arrived,60,"minimal actual west-east arrival",r,routes,p,observe);
            Assert.True(certificateSeen,"fresh mission must obtain paid capacity certificate");Assert.True(moved);Assert.True(demandSeen,"fresh mission request must select DemandQuery");Assert.False(compilerSeen,"fresh demand query must avoid eager compiler");Debug.Log("CAMPAIGN_ROUTE_ARRIVAL tick="+r.Latest.Tick);
        }
        finally{r.RequestStop();routes.Dispose();}
    }

    // Separate short diagnostic: preserve the original source/config/command; stop at a pinned pending search.
    [UnityTest] public IEnumerator CapturePendingOrdinaryWestToEastSearch()
    {
        var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());var authored=CampaignReconOpening.Configuration(p);
        var start=new NavPoint(-61.5089109845236,108.075765746673);var goal=new NavPoint(68,64);
        var config=new OfflineMatchConfiguration(p,authored.SourceIdentity+"-west-route-witness",authored.MapIdentity,authored.RouteProvenance,authored.Seed,authored.Roster.ToArray(),authored.Starts.ToArray(),authored.Sites.ToArray(),authored.Obstacles.ToArray(),new double[3,3],scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,start)},terrain:p.AuthoredMap.Id,scenarioBuildings:authored.ScenarioBuildings.Where(b=>b.SiteId>=4).ToArray(),missionId:CampaignReconOpening.Id);
        var r=new PlayableRuntime(config,113);var routes=new UnityHostRouteService();
        try
        {
            yield return Until(()=>r.IsPrepared,60,"minimal west-route preparation",r,routes,p);
            var save=r.RequestCaptureBytes();yield return Until(()=>save.IsCompleted,30,"minimal idle checkpoint",r,routes,p);Assert.AreEqual(System.Threading.Tasks.TaskStatus.RanToCompletion,save.Status,save.Exception?.ToString());
            string proof=Environment.GetEnvironmentVariable("CAMPAIGN_EVIDENCE");if(!string.IsNullOrEmpty(proof)){System.IO.Directory.CreateDirectory(proof);System.IO.File.WriteAllBytes(System.IO.Path.Combine(proof,"route-initial.bin"),save.Result);}
            int id=r.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Id;
            Assert.True(r.TrySubmit(new PlayableCommand(r.Generation,1,"player-1",PlayableCommandKind.AttackMove,new[]{id},goal)).Accepted);
            yield return Until(()=>r.ReceiptsAfter(0).Any(v=>v.Sequence==1&&v.Status!=PlayableCommandStatus.Accepted),30,"actual west-east command application",r,routes,p);
            Assert.AreEqual(PlayableCommandStatus.Applied,r.ReceiptsAfter(0).Single(v=>v.Sequence==1&&v.Status!=PlayableCommandStatus.Accepted).Status);
            Assert.IsNotEmpty(proof,"Diagnostic output required");bool captured=false;
            bool Capture()
            {
                if(captured)return true;
                const System.Reflection.BindingFlags f=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
                const System.Reflection.BindingFlags sf=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
                var session=(NavigationSession)typeof(NavigationRoutePort).GetField("session",f).GetValue(r.NavigationBinding.RoutePort);
                var root=session.PinCommittedHostRoot();if(root==null)return false;
                try{foreach(var pair in root.ArrivalWork){if(!(pair.Value is NavigationArrivalWork work))continue;
                    var scheduler=(LayeredNavigationScheduler)typeof(NavigationArrivalWork).GetField("scheduler",f).GetValue(work);var jobs=typeof(LayeredNavigationScheduler).GetField("jobs",f).GetValue(scheduler);var job=jobs.GetType().GetProperty("First",f).GetValue(jobs);if(job==null)continue;var demand=job.GetType().GetProperty("Demand",f).GetValue(job);if(demand==null)continue;
                    object Field(string name)=>demand.GetType().GetField(name,f).GetValue(demand);
                    if(Field("phase").ToString()!="Candidates")continue;
                    Assert.True((bool)typeof(NavigationArrivalWork).GetProperty("IsImmutableCaptureRoot",f).GetValue(work));var request=(NavScheduledRequest)job.GetType().GetProperty("Request",f).GetValue(job);Assert.True(request.RectangularOrigins);Assert.AreEqual(64,request.Region.Candidates.Count);Assert.Null(job.GetType().GetProperty("Compiler",f).GetValue(job));
                    var text=new System.Text.StringBuilder("Pinned actual threaded ArrivalWork; diagnostic only, no arrival PASS.\n");
                    text.AppendLine("tick="+r.Latest.Tick+" epoch="+work.LogicalEpoch+" input="+work.InputBinding+" provider="+request.Provider.Binding+" graph="+request.GraphKey+" compile="+request.CompileKey+" artifact="+request.ArtifactKey);
                    foreach(string name in new[]{"phase","lazyStage","seed","current","candidate","chosen","chosenCandidate","hEligible"})text.AppendLine(name+"="+Field(name));
                    foreach(string name in new[]{"starts","ends","exactDistance","exactFrontier"}){var v=Field(name);text.AppendLine(name+".Count="+v.GetType().GetProperty("Count",f).GetValue(v));}
                    using(var ms=new System.IO.MemoryStream())using(var writer=new System.IO.BinaryWriter(ms)){var value=Field("exactBest");value.GetType().GetMethod("Write",f).Invoke(value,new object[]{writer});writer.Flush();text.AppendLine("exactBestLE="+BitConverter.ToString(ms.ToArray()).Replace("-",""));}
                    text.AppendLine("Graph="+work.RouteCounters.PhysicalGraphWork+" Connector="+work.RouteCounters.ConnectorWork);
                    var open=typeof(NavigationSession).GetMethod("TryOpenPhysicalHostScope",f);var args=new object[]{Enum.Parse(open.GetParameters()[0].ParameterType,"Capture"),work.LogicalEpoch,0L,null};Assert.True((bool)open.Invoke(session,args),"Pinned capture admission");var scope=args[3];object output=null;
                    try{var reservation=scope.GetType().GetMethod("OwnedReservation",f).Invoke(scope,new object[]{null});var wire=typeof(NavOwnedStorage).Assembly.GetType("Spacewars.Simulation.NavPhysicalBodyWire");var captureType=typeof(NavigationSession).Assembly.GetType("Spacewars.Runtime.NavigationCaptureBytes");
                        Action<System.IO.BinaryWriter> write=w=>wire.GetMethod("WriteTo",sf).Invoke(null,new object[]{w,work,reservation});output=captureType.GetMethod("Write",sf).Invoke(null,new object[]{reservation,write});scope.GetType().GetMethod("Attach",f).Invoke(scope,new[]{output});scope.GetType().GetMethods(f).Single(m=>m.Name=="Commit"&&m.GetParameters().Length==2).Invoke(scope,new object[]{output,(Action<object>)(_=>{})});
                        var bytes=(byte[])captureType.GetMethod("TransferPublic",f).Invoke(output,null);output=null;System.IO.File.WriteAllBytes(System.IO.Path.Combine(proof,"pending-arrival.body"),bytes);using(var hash=System.Security.Cryptography.SHA256.Create())text.AppendLine("BODY_SHA256="+BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant());text.AppendLine("BODY_BYTES="+bytes.Length);
                    }finally{(output as IDisposable)?.Dispose();(scope as IDisposable)?.Dispose();}
                    System.IO.File.WriteAllText(System.IO.Path.Combine(proof,"pending-arrival.txt"),text.ToString());Debug.Log("CAMPAIGN_PINNED_SEARCH "+text);captured=true;return true;
                }return false;}finally{typeof(NavOwnedStorage).GetMethod("Release",f).Invoke(root,null);}
            }
            yield return Until(Capture,60,"diagnostic pinned pending search, no arrival claim",r,routes,p);Assert.True(captured);
        }
        finally{r.RequestStop();routes.Dispose();}
    }

}
