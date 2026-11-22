using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class SpectatorProjectionBatchTests
    {
        [Test] public void SlowPresentationKeepsOnlyLatestPendingAndCompletesBeforeShutdown()
        {
            using var entered = new System.Threading.ManualResetEventSlim();
            using var release = new System.Threading.ManualResetEventSlim();
            var published = new System.Collections.Generic.List<long>();
            var caller = System.Threading.Thread.CurrentThread.ManagedThreadId;
            int builder = caller;
            var worker = new PlayableRuntime.PresentationWorker(input => {
                builder = System.Threading.Thread.CurrentThread.ManagedThreadId;
                if (input.ReceiptOrdinal == 1) { entered.Set(); release.Wait(); }
                published.Add(input.ReceiptOrdinal);
            });
            PlayableRuntime.PresentationInput Input(int n) => new PlayableRuntime.PresentationInput(
                Array.Empty<PlayableSnapshot>(),null,Array.Empty<PlayableGroundTrail>(),null,Array.Empty<OfflineReceipt>(),n);
            try {
                worker.Submit(Input(1));
                Assert.True(entered.Wait(5000));
                for (int i=2; i<=10; i++) worker.Submit(Input(i));
            } finally { release.Set(); worker.Dispose(); }
            Assert.AreNotEqual(caller,builder);
            CollectionAssert.AreEqual(new long[]{1,10},published);
        }

        [TestCase(2),TestCase(8)] public void DetachedFramesMatchCurrentVisibilityAndReceiptOrder(int count)
        {
            var domain = Create(count);
            try {
                var journal = (GroundTrailJournal)typeof(PlayableDomain).GetField("groundTrails",Fields).GetValue(domain);
                var initial = Frame(domain,true);
                foreach (var view in initial.Players) {
                    var entity = view.Entities.First(e=>e.Owner==view.Owner);
                    journal.Observe(entity.Id,PlayableEntityKind.Explorer,entity.Position,
                        new NavPoint(entity.Position.X+.25,entity.Position.Z),0,domain.Tick);
                }
                var expected = Frame(domain,true);
                var receipts = expected.Players.Select((v,i)=>new OfflineReceipt(i+1,
                    new PlayableCommandReceipt(1,v.Tick,PlayableCommandStatus.Applied,"",0,v.OwnerId))).ToArray();
                PlayableSnapshot Capture(PlayableOwner owner,bool overview=false) =>
                    domain.CapturePresentationView(9,RuntimeStatus.Running,false,Metrics,null,838049288,owner,overview);
                PlayableRuntime.PresentationInput input;
                using (domain.BeginProjectionBatch())
                    input = new PlayableRuntime.PresentationInput(expected.Players.Select(v=>Capture(v.Owner)).ToArray(),
                        Capture(PlayableOwner.Player,true),domain.CapturePresentationTrails(),null,receipts,count);
                // Change live rows after capture, before allowing the worker to assemble them.
                foreach (var unit in Records(domain,"units").Values) Set(unit,"Health",1d);
                foreach (var building in Records(domain,"buildings").Values) Set(building,"Health",1d);
                journal.Expire(domain.Tick+10000);
                PlayableRuntime.PresentationFrames frames = null;
                using (var worker = new PlayableRuntime.PresentationWorker(next =>
                    frames = new PlayableRuntime.PresentationFrames(next,null))) worker.Submit(input);
                Assert.IsNotEmpty(frames.Spectator.Overview.GroundTrails);
                Assert.AreEqual(Values(expected),Values(frames.Spectator));
                foreach (var view in frames.Spectator.Players)
                    Assert.AreSame(view,frames.Offline.Views[view.OwnerId]);
                CollectionAssert.AreEqual(receipts,frames.Offline.Receipts);
            } finally { domain.DestroyOwnedNavigation(); }
        }

        [Test] public void OverviewReusesCompletedPrimaryRowsOnlyWithinTheirPublication()
        {
            var domain = Create(2);
            try {
                var projection = new PlayableDomain.ProjectionScope(domain, includesOverview:true);
                var primary = domain.PrimaryFrameSnapshot(9,RuntimeStatus.Running,false,Metrics,null,838049288,true,projection);
                var original = primary.Entities.First(unit => unit.Owner == PlayableOwner.Player &&
                    unit.Kind != PlayableEntityKind.Igla && unit.Kind != PlayableEntityKind.Skat);
                PlayableSnapshot overview;
                using (projection.Enter())
                    overview = domain.CapturePresentationView(9,RuntimeStatus.Running,false,Metrics,null,838049288,PlayableOwner.Player,true);
                Assert.AreSame(original,overview.Entities.Single(unit => unit.Id == original.Id));

                Set(Records(domain,"units")[original.Id],"Health",1d);
                using (domain.BeginProjectionBatch(includesOverview:true)) {
                    var next = domain.CapturePresentationView(10,RuntimeStatus.Running,false,Metrics,null,838049288,PlayableOwner.Player,true);
                    Assert.AreEqual(1,next.Entities.Single(unit => unit.Id == original.Id).Health);
                }
                Assert.Greater(original.Health,1);
            } finally { domain.DestroyOwnedNavigation(); }
        }

        [Test] public void CompletedEnvelopeMatchesFluentFieldsAndClosesInputsWithFewerAllocations()
        {
            var d=Create(2);
            try{
                var basis=Frame(d,true).Players.First();string original=JsonConvert.SerializeObject(basis);
                var mines=new[]{new OrbitalMineSnapshot(12,PlayableOwner.Player,new NavPoint(1,2),1,2,3)};
                var blasts=new[]{new OrbitalMineBlastSnapshot(13,PlayableOwner.Enemy,new NavPoint(3,4),5,6,7)};
                var scans=new[]{new OrbitalScanSnapshot(PlayableOwner.Player,new NavPoint(5,6),7)};
                var lines=new[]{new OrbitalScanLine(new NavPoint(1,2),new NavPoint(3,4),.5)};
                var triangles=new[]{new OrbitalScanTriangle(default,new NavPoint(1,2),new NavPoint(3,4),.5)};
                var signals=new[]{new PlayableTeamSignal(8,PlayableOwner.Player,TeamSignalKind.Defend,new NavPoint(8,9),10,11)};
                var events=new[]{new PlayableMatchEvent(12,13,PlayableOwner.Enemy,MatchEventKind.UnitsAttacked,new NavPoint(14,15),16)};
                PlayableSnapshot Fluent()=>basis.WithMines(mines,blasts).WithOrbital(basis.Orbital).WithOrbitalScans(scans).WithScanLines(lines,triangles).WithTeamSignals(signals).WithMatchEvents(events);
                PlayableSnapshot Complete()=>basis.WithPresentation(mines,blasts,basis.Orbital,scans,lines,triangles,signals,events);
                var old=Fluent();var next=Complete();
                Assert.AreEqual(JsonConvert.SerializeObject(old),JsonConvert.SerializeObject(next));
                string retained=JsonConvert.SerializeObject(next);signals[0]=null;events[0]=null;
                Assert.AreEqual(retained,JsonConvert.SerializeObject(next),"caller-owned signal/event arrays stay detached");
                Assert.AreEqual(original,JsonConvert.SerializeObject(basis),"source envelope is unchanged");
                Assert.Throws<NotSupportedException>(()=>((IList)next.TeamSignals)[0]=null);
                var empty=basis.WithPresentation(Array.Empty<OrbitalMineSnapshot>(),Array.Empty<OrbitalMineBlastSnapshot>(),basis.Orbital,Array.Empty<OrbitalScanSnapshot>(),Array.Empty<OrbitalScanLine>(),null,null,null);
                Assert.IsEmpty(empty.ScanTriangles);Assert.IsEmpty(empty.TeamSignals);Assert.IsEmpty(empty.MatchEvents);
                // Unity's Mono reports zero for GetAllocatedBytesForCurrentThread.
                // Count actual envelope allocations at their common clone boundary.
                PlayableSnapshot.EnvelopeCopiesForTests=0;Fluent();long legacy=PlayableSnapshot.EnvelopeCopiesForTests;
                PlayableSnapshot.EnvelopeCopiesForTests=0;Complete();long combined=PlayableSnapshot.EnvelopeCopiesForTests;
                Assert.AreEqual(6,legacy);Assert.AreEqual(1,combined);
                TestContext.WriteLine("ENVELOPE_COPIES fluent="+legacy+" combined="+combined);

            }finally{PlayableSnapshot.EnvelopeCopiesForTests=0;d.DestroyOwnedNavigation();}
        }
        const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static readonly PlayableRuntimeMetrics Metrics=new PlayableRuntimeMetrics(0,0,0,0,0);
        static PlayableDomain Create(int count)
        {
            var map=new CrimsonGroveMap();var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var roster=Enumerable.Range(0,count).Select(i=>new OfflineParticipant("owner-"+i,i+1,i%2+1,OfflineControl.Ai)).ToArray();
            return new PlayableDomain(profile,71,map.Configuration(profile,838049288,roster));
        }
        static PlayableSpectatorFrame Frame(PlayableDomain d,bool shared)
        {
            d.DisableProjectionBatchForTests=!shared;
            return d.SpectatorSnapshot(9,RuntimeStatus.Running,false,Metrics,null,838049288);
        }
        static string Values(PlayableSpectatorFrame f)=>JsonConvert.SerializeObject(new{f.Overview,f.Players});
        static IDictionary Records(PlayableDomain d,string name)=>(IDictionary)typeof(PlayableDomain).GetField(name,Fields).GetValue(d);
        static void Set(object value,string name,object next)=>value.GetType().GetField(name,Fields).SetValue(value,next);
        [TestCase(2)][TestCase(8)]
        public void OneRefreshPerTeamMatchesOwnerLoopThroughSameTickSightAndMemoryChanges(int count)
        {
            var old=Create(count);var next=Create(count);old.RepeatAlliedVisionRefreshForTests=true;
            try{
                PlayableSpectatorFrame retained=null;string retainedValues=null;
                int visible=0,hidden=0;
                for(int step=0;step<10;step++){
                    foreach(var d in new[]{old,next}){
                        var config=(OfflineMatchConfiguration)typeof(PlayableDomain).GetField("offline",Fields).GetValue(d);
                        var buildings=Records(d,"buildings").Values.Cast<object>().ToArray();
                        var enemy=buildings.First(b=>(PlayableOwner)b.GetType().GetField("Owner",Fields).GetValue(b)==PlayableOwner.Enemy);
                        var target=(NavPoint)enemy.GetType().GetField("Position",Fields).GetValue(enemy);
                        foreach(var b in buildings){
                            var owner=(PlayableOwner)b.GetType().GetField("Owner",Fields).GetValue(b);
                            if(config.Roster[(int)owner].Team==1){Set(b,"Phase",ConstructionPhase.Pending);Set(b,"Ready",false);}
                        }
                        foreach(var u in Records(d,"units").Values.Cast<object>()){
                            var owner=(PlayableOwner)u.GetType().GetField("Owner",Fields).GetValue(u);
                            if(config.Roster[(int)owner].Team!=1)continue;
                            Set(u,"Health",owner==PlayableOwner.Player?35d:0d);
                            int id=(int)u.GetType().GetField("Id",Fields).GetValue(u);
                            object[] args={id,null};Assert.True((bool)typeof(PlayableDomain).GetMethod("TryActor",Fields).Invoke(d,args));
                            ((NavUnit)args[1]).Position=step%2==0?target:new NavPoint(target.X>0?-120:120,target.Z>0?-120:120);
                        }
                        Set(enemy,"Heading",step*.2);Set(enemy,"Health",170d+step);
                        Set(enemy,"Phase",step==8?ConstructionPhase.Pending:ConstructionPhase.Ready);
                        Set(enemy,"Ready",step!=8);
                        foreach(var site in Records(d,"sites").Values.Cast<object>()){
                            var geometry=(TerritorySite)site.GetType().GetField("Site",Fields).GetValue(site);
                            if(geometry.Kind!=PlayableBuildingKind.RelayTower)continue;
                            Set(site,"Locked",step%3==0?(PlayableOwner?)PlayableOwner.Enemy:null);
                            typeof(PlayableDomain).GetMethod("AddLingeringVision",Fields).Invoke(d,new object[]{PlayableOwner.Player,geometry.Position,2d});
                        }
                    }
                    var expected=Frame(old,true);var actual=Frame(next,true);
                    Assert.AreEqual(Values(expected),Values(actual),"complete publication at same tick, step "+step);
                    string State(PlayableDomain d)=>JsonConvert.SerializeObject(Records(d,"visions").Values.Cast<PlayableVision>().Select(v=>v.CaptureState()).ToArray());
                    Assert.AreEqual(State(old),State(next),"all vision state including revisions and history");
                    var player=actual.Players.Single(v=>v.Owner==PlayableOwner.Player);
                    if(step%2==0){
                        Assert.True(player.Vision.Sources.Any());
                        if(step<8)Assert.True(player.Vision.KnownBuildings.Any(b=>player.Vision.IsVisible(b.Position)),"observe enemy before hiding, step "+step);
                        else Assert.False(player.Vision.KnownBuildings.Any(),"visible pending enemy is removed from memory");
                        visible++;
                    }
                    else if(step==9)Assert.False(player.Vision.KnownBuildings.Any(),"hidden reappearance cannot repopulate cleared memory");
                    else{
                        Assert.True(player.Vision.KnownBuildings.Any(),"previously seen enemy remains remembered, step "+step);
                        Assert.True(player.Vision.KnownBuildings.Any(b=>!player.Vision.IsVisible(b.Position)),"exercise hidden building memory");hidden++;
                    }
                    foreach(var view in actual.Players)Assert.True(view.Buildings.Where(b=>b.Owner!=view.Owner).All(b=>b.PrivateState==null));
                    if(retained!=null)Assert.AreEqual(retainedValues,Values(retained),"old frame remains immutable");
                    retained=actual;retainedValues=Values(actual);
                }
                Assert.Greater(visible,0);Assert.Greater(hidden,0);
            }finally{old.DestroyOwnedNavigation();next.DestroyOwnedNavigation();}
        }
        [TestCase(2)][TestCase(8)]
        public void BatchedPublicationMatchesIndependentProjectionsAndRetainsOldFrames(int count)
        {
            var d=Create(count);
            try{
                // Populate each view's lazy vision just as runtime startup does.
                Frame(d,false);PlayableSpectatorFrame prior=null;string priorValues=null;
                var buildings=Records(d,"buildings").Values.Cast<object>().ToArray();
                var units=Records(d,"units").Values.Cast<object>().ToArray();
                for(int step=0;step<16;step++){
                    var u=units[step%units.Length];var b=buildings[step%buildings.Length];
                    Set(u,"Health",35d+step*.25);Set(u,"Turret",step*.125);
                    Set(b,"Health",170d+step*.5);Set(b,"Build",step*1.25);Set(b,"Ready",step%3!=0);
                    Set(b,"Phase",step%3==0?ConstructionPhase.Pending:ConstructionPhase.Ready);
                    var independent=Frame(d,false);var shared=Frame(d,true);
                    Assert.AreEqual(Values(independent),Values(shared),"publication "+step);
                    foreach(var view in shared.Players)Assert.True(view.Buildings.Where(x=>x.Owner!=view.Owner).All(x=>x.PrivateState==null));
                    if(prior!=null)Assert.AreEqual(priorValues,Values(prior),"retained immutable frame");
                    prior=shared;priorValues=Values(shared);
                }
                if(count==8){
                    int reused=0;
                    foreach(var group in prior.Players.SelectMany(view=>view.Entities.Where(e=>e.Owner!=view.Owner)).GroupBy(e=>e.Id)){
                        var entries=group.ToArray();for(int i=1;i<entries.Length;i++){Assert.AreSame(entries[0],entries[i]);reused++;}
                    }
                    Assert.Greater(reused,0,"exercise shared public entities");
                }
            }finally{d.DestroyOwnedNavigation();}
        }
        [Test] public void AiTeamBatchMatchesIndependentViewsAndExpiresBeforeSameTickMutation()
        {
            var d=Create(8);
            try{
                string Read()=>JsonConvert.SerializeObject(Enumerable.Range(0,8).Select(i=>i==0?
                    d.PlayerSnapshotForAiReview(9,RuntimeStatus.Running,false,Metrics,null,838049288,(PlayableOwner)i,false,null,false):
                    d.PlayerSnapshot(9,RuntimeStatus.Running,false,Metrics,null,838049288,(PlayableOwner)i)).ToArray());
                string independent=Read();
                using(d.BeginProjectionBatch())Assert.AreEqual(independent,Read());
                var unit=Records(d,"units").Values.Cast<object>().First();Set(unit,"Health",27.25d);
                string changed=Read();Assert.AreNotEqual(independent,changed);
                using(d.BeginProjectionBatch())Assert.AreEqual(changed,Read());
            }finally{d.DestroyOwnedNavigation();}
        }
        [TestCase(2)][TestCase(8)]
        public void TeamPublicationObservationMatchesFullViewAndFreezesDefenseFacts(int count)
        {
            var d=Create(count);
            try{
                Frame(d,true);var config=(OfflineMatchConfiguration)typeof(PlayableDomain).GetField("offline",Fields).GetValue(d);
                var loops=Enumerable.Range(0,count).Select(i=>new PlayableAiOwnerLoop(config.Profile,PlayableAiOpeningComposition.Initialize(838049288,"owner-"+i,forcedOpening:i%2==0?PlayableAiOpening.GreedySafe:PlayableAiOpening.Safe),71)).ToArray();
                var homes=Records(d,"buildings").Values.Cast<object>().Where(b=>(PlayableBuildingKind)b.GetType().GetField("Kind",Fields).GetValue(b)==PlayableBuildingKind.Headquarters).ToArray();
                var home=homes.First(b=>(PlayableOwner)b.GetType().GetField("Owner",Fields).GetValue(b)==PlayableOwner.Player);
                int homeId=(int)home.GetType().GetField("Id",Fields).GetValue(home);var center=(NavPoint)home.GetType().GetField("Position",Fields).GetValue(home);
                var enemy=Records(d,"units").Values.Cast<object>().First(u=>(PlayableOwner)u.GetType().GetField("Owner",Fields).GetValue(u)==PlayableOwner.Enemy);int enemyId=(int)enemy.GetType().GetField("Id",Fields).GetValue(enemy);
                object[] args={enemyId,null};Assert.True((bool)typeof(PlayableDomain).GetMethod("TryActor",Fields).Invoke(d,args));var actor=(NavUnit)args[1];
                var damage=Records(d,"centerDamage");AiTeamPublicationObservation retained=null;string retainedValue=null;int threatened=0,quiet=0;
                for(int step=0;step<12;step++){
                    actor.Position=new NavPoint(center.X+(step%2==0?1:45),center.Z);Set(enemy,"Health",step%5==0?0d:35d);
                    Set(home,"Health",step%4==0?0d:200d);Set(home,"Phase",step%3==0?ConstructionPhase.Pending:ConstructionPhase.Ready);Set(home,"Ready",step%3!=0);
                    damage.Clear();if(step%2!=0)damage[homeId+":"+enemyId]=new PlayableCenterDamageSnapshot(homeId,PlayableOwner.Player,enemyId,71,0,5);
                    if(retained!=null)Assert.AreEqual(retainedValue,PlayableWorldRestoreTests.Facts(loops[0].PublishTeam(retained),true),"detached team input survives later same-tick mutations");
                    using(d.BeginProjectionBatch())for(int i=0;i<count;i++){
                        var full=d.PlayerSnapshot(9,RuntimeStatus.Running,false,Metrics,null,838049288,(PlayableOwner)i);Assert.Zero(full.RouteProofs.Count,"Non-selected team projection has no requested routes");
                        var expected=loops[i].PublishTeam(PlayableAiObservation.From(full));var compact=d.TeamPublicationObservation(9,RuntimeStatus.Running,Metrics,838049288,(PlayableOwner)i);var actual=loops[i].PublishTeam(compact);
                        Assert.AreEqual(PlayableWorldRestoreTests.Facts(expected,true),PlayableWorldRestoreTests.Facts(actual,true),"all team publication fields, owner "+i+" step "+step);
                        if(actual.HomeThreatened)threatened++;else quiet++;
                        if(i==0){retained=compact;retainedValue=PlayableWorldRestoreTests.Facts(actual,true);}
                    }
                }
                Assert.Greater(threatened,0);Assert.Greater(quiet,0);
                var frame=Frame(d,true);foreach(var team in frame.Players.GroupBy(v=>v.Team)){var first=team.First().Vision;foreach(var view in team)Assert.AreSame(first,view.Vision,"one closed team vision per synchronous publication");}
            }finally{d.DestroyOwnedNavigation();}
        }
        [Test] public void DerivedBatchDoesNotEnterCurrentFormatSaveOrColdRestore()
        {
            var d=Create(8);PlayableDomain restored=null;
            try{
                Frame(d,false);var expected=Frame(d,true);var before=d.CaptureWorldBytes(838049288,"projection-batch");
                Assert.AreEqual(Values(expected),Values(Frame(d,false)));
                CollectionAssert.AreEqual(before,d.CaptureWorldBytes(838049288,"projection-batch"));
                var config=(OfflineMatchConfiguration)typeof(PlayableDomain).GetField("offline",Fields).GetValue(d);
                restored=PlayableDomain.RestoreConfiguredWorldBytes(before,config.Profile,838049288,"projection-batch",config);
                CollectionAssert.AreEqual(before,restored.CaptureWorldBytes(838049288,"projection-batch"));
                Assert.AreEqual(Values(Frame(restored,false)),Values(Frame(restored,true)));
            }finally{restored?.DestroyOwnedNavigation();d.DestroyOwnedNavigation();}
        }
    }
}
