using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class CombatMicroStableGuardRepairThreadedTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        const int Seed=7;const long Gen=905;
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile Ai=AiProfile.Initial;
        static object Field(object value,string name)=>value.GetType().GetField(name,F).GetValue(value);
        static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
        static string Output=>Environment.GetEnvironmentVariable("COMBAT_MICRO_STABLE_GUARD_OUTPUT")??Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,"../../.local/ai-combat-attention/repair-custody/stable-guard"));
        static Dictionary<string,object> Scalars(object value)=>value==null?null:value.GetType().GetFields(F).Where(f=>f.FieldType.IsPrimitive||f.FieldType.IsEnum||f.FieldType==typeof(string)||f.FieldType==typeof(NavPoint)).ToDictionary(f=>f.Name,f=>f.GetValue(value));
        sealed class Setup:IDisposable
        {
            internal readonly PlayableProfile Profile;
            internal readonly OfflineMatchConfiguration Config;
            internal readonly PlayableDomain D;
            internal readonly PlayableAiOwnerLoop Loop;
            internal readonly AiAuthorityScheduler Scheduler;
            internal readonly PlayableAuthorityTick Authority;
            internal readonly AiArmyRegistry Registry;
            internal readonly AiCombatMicroPolicy Micro;
            internal readonly AiArmyPlanner Planner;
            internal readonly int Patient,Explorer,Tower,Asset;
            internal readonly long Army;
            internal readonly NavPoint PatientOrigin;
            internal readonly PlayableAiAction Guard;
            internal readonly object Preflight;
            internal Setup(PlayableProfile selectedProfile=null)
            {
                var P=Profile=selectedProfile??CombatMicroStableGuardRepairThreadedTests.P;
                var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,10),new NavPoint(-18,-12),pin:1),new OfflineStart("east",2,new NavPoint(24,21),new NavPoint(25,27),pin:2)};
                var units=new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-16,15)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-15,25)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-11,25))};
                var map=P.AuthoredMap as ThreeCrossingsMap;OfflineMatchConfiguration basis=null;
                if(map!=null){
                    basis=NativeLobbyMatch.Create(P,new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=Seed,Participants=new List<NativeLobbyParticipant>{new NativeLobbyParticipant{Name="AI",Team=1,Color=0,Difficulty=AiDifficulty.Veteran},new NativeLobbyParticipant{Name="Human",Human=true,Team=2,Color=1}}});
                    starts=basis.Starts.ToArray();var home=starts.Single(start=>start.PinnedLogicalPlayer==1).Position;int sx=Math.Sign(home.X),sz=Math.Sign(home.Z);
                    units=new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(home.X,home.Z-sz*17)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(home.X-sx*3,home.Z+2)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(home.X-sx*3,home.Z-2))};
                }
                var site=map==null?new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,P):basis.Sites.Single(value=>value.Id==starts.Single(start=>start.PinnedLogicalPlayer==1).SiteId);
                var pad=map==null?site.DefenseSlots(P).Single(slot=>slot.Id==1003):site.DefenseSlots(P).OrderByDescending(slot=>Math.Sign(site.Position.Z)*slot.Position.Z).First();
                var costs=map==null?new double[,]{{0,100},{100,0}}:new double[starts.Length,starts.Length];if(basis!=null)for(int a=0;a<starts.Length;a++)for(int b=0;b<starts.Length;b++)costs[a,b]=basis.RouteCost(a,b);
                Config=new OfflineMatchConfiguration(P,basis?.SourceIdentity??"stable-guard-repair-v1",basis?.MapIdentity??"flat-seed7",basis?.RouteProvenance??"ordinary-certified-threaded-stable-guard",Seed,
                    basis?.Roster.ToArray()??new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)},starts,
                    basis?.Sites.ToArray()??new[]{site,new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>())},basis?.Obstacles.ToArray()??Array.Empty<NavObstacle>(),costs,
                    scenario:units,terrain:map?.Id??"flat-ground-v1",
                    scenarioBuildings:new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.RepairTower,site.Id,pad.Id,pad.Position,pad.Heading)});
                D=new PlayableDomain(P,Gen,Config);
                try{
                    Loop=new PlayableAiOwnerLoop(P,PlayableAiOpeningComposition.Initialize(Seed,"player-1",forcedOpening:PlayableAiOpening.GreedySafe,aiProfile:Ai),Gen,null,Ai,AiDifficulty.Veteran);
                    Scheduler=new AiAuthorityScheduler(new[]{Loop});Authority=new PlayableAuthorityTick(D,Scheduler,Seed,Config);
                    Registry=(AiArmyRegistry)Field(Loop,"armies");Planner=(AiArmyPlanner)Field(Loop,"armyPlanner");Micro=(AiCombatMicroPolicy)Field(Loop,"combatMicro");
                    Assert.False((bool)Field(Scheduler,"backgroundReviews"));
                    var initial=Authority.ParticipantView("player-1");var tanks=initial.Entities.Where(u=>u.Owner==initial.Owner&&u.Kind==PlayableEntityKind.Tank).OrderBy(u=>u.Id).ToArray();
                    Assert.AreEqual(3,tanks.Length);Patient=tanks[0].Id;PatientOrigin=tanks[0].Position;Explorer=initial.Entities.Single(u=>u.Owner==initial.Owner&&u.Kind==PlayableEntityKind.Explorer).Id;
                    foreach(var unit in initial.Entities.Where(u=>u.Kind!=PlayableEntityKind.Hawk))Assert.True(D.Geometry.IsFree(unit.Position,PlayableUnitRules.Radius(P,unit.Kind)),"Every initial spawn is legal");
                    typeof(PlayableDomain).GetMethod("Damage",F).Invoke(D,new object[]{Patient,72});
                    Assert.AreEqual(PlayableCommandStatus.Applied,Authority.Apply(new PlayableCommand(Gen,1,"player-1",PlayableCommandKind.Hold,new[]{Explorer},default,origin:PlayableOrderOrigin.Human)).Status);
                    var view=D.PlayerSnapshotForArmyAi(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,Seed,initial.Owner,true,Registry.Capture());var o=PlayableAiObservation.From(view);
                    var patient=o.Entities.Single(u=>u.Id==Patient);var tower=o.Buildings.Single(b=>b.Kind==PlayableBuildingKind.RepairTower&&b.Owner==o.Owner);Tower=tower.Id;
                    Assert.AreEqual(28,patient.Health);Assert.AreEqual(100,patient.MaxHealth);Assert.IsNull(patient.CurrentOrder);Assert.IsNull(patient.Queue?.Pending);
                    Assert.Greater(Distance(patient.Position,tower.Position),P.ExplorerRange*P.TurretRangeMultiplier,"Initial patient must approach before healing");
                    var admission=new AiCombatMicroPolicy();admission.Observe(o,P,Ai,AiDifficulty.Veteran,Registry);var repair=admission.Plan(o,P,Ai,AiDifficulty.Veteran,Registry);
                    Assert.NotNull(repair,"Actual initial idle observation admits a repair candidate");Assert.AreEqual(AiCombatMicroPhase.Repair,repair.Phase);Assert.AreEqual(Tower,repair.Action.ParentId);
                    Guard=Planner.ProposePositioning(o,P,Ai,Registry);Assert.NotNull(Guard,"Actual certified positioning proposal");CollectionAssert.AreEquivalent(tanks.Select(u=>u.Id),Guard.EntityIds);
                    Assert.True(Guard.EntityIds.All(id=>o.RouteProofs.Any(r=>r.UnitId==id&&r.TargetId==Guard.ParentId&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&AiTacticalExecutor.SafeRoute(o,r,P))),"Every Guard recipient has actual current certified safe HQ route");
                    Assert.IsNull(D.ArmyMoveRejection(Guard,false),"The actual common Guard command must validate every current recipient path");
                    Planner.Commit(o,Guard,Registry);Army=Registry.ArmyFor(Patient);Asset=Guard.ParentId;
                    Assert.AreEqual(PlayableCommandStatus.Applied,Authority.Apply(new PlayableCommand(Gen,2,o.OwnerId,Guard.Kind,Guard.CopyEntityIds(),Guard.Target,parentId:Guard.ParentId,origin:PlayableOrderOrigin.Ai,source:Guard.SourceIdentity,jobId:1,actionId:1)).Status);
                    Planner.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,Guard.ActionId,0,2,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"authored ordinary Guard",o.OwnerId,Guard.SourceIdentity,new AiReceiptIdentity(Gen,o.OwnerId,1,1),AiArmyPlanner.Policy,Guard.Kind));
                    typeof(AiAuthorityScheduler).GetField("nextActionId",F).SetValue(Scheduler,1L);
                    var accepted=Authority.ParticipantView("player-1");var actor=accepted.Entities.Single(u=>u.Id==Patient);Assert.AreEqual(1,actor.OrderStamp.ActionId);Assert.AreEqual(2,actor.OrderStamp.Sequence);Assert.AreEqual(PlayableOrderOrigin.Ai,actor.OrderStamp.Origin);
                    Assert.AreEqual(0,D.Tick);CheckCore(PlayableAiObservation.From(accepted),0);
                    Preflight=new{Tick=D.Tick,Seed,Generation=Gen,Profile=P.ProfileId,P.Revision,AiHash=Ai.Hash,Config.SourceIdentity,Config.MapIdentity,Patient,PatientOrigin,Health=patient.Health,Tower=tower,HealingRange=P.ExplorerRange*P.TurretRangeMultiplier,
                        Army,Asset,Guard=new{Guard.Kind,Guard.Target,Guard.ParentId,Entities=Guard.CopyEntityIds()},Registry=Registry.Capture(),ActualStamp=actor.OrderStamp,
                        InitialRepairCandidate=new{repair.Phase,repair.Army,repair.Action.Target,repair.Action.ParentId},Routes=o.RouteProofs.Where(r=>Guard.EntityIds.Contains(r.UnitId)&&r.TargetId==Guard.ParentId).Select(r=>new{r.UnitId,r.Kind,r.TargetId,r.Origin,r.Target,r.Goal,Points=r.Path.ToArray()}).ToArray(),
                        IdlePathValidity="Initial true idle observation admits Repair. Authored ordinary short Guard Move must naturally complete during match before idle Repair; no completion/health/order injected in loop."};
                }catch{Dispose();throw;}
            }
            internal void CheckCore(PlayableAiObservation o,int anchor)
            {
                var P=Profile;
                var group=Registry.CaptureArmy(Army).Single(a=>a.Id==Army);Assert.True(group.Major);Assert.AreEqual(o.OwnerId,group.OwnerId);Assert.AreEqual(AiArmyPlanner.Policy,group.TacticalOwner);
                Assert.False(new[]{AiArmyPhase.Disbanded,AiArmyPhase.Retreating,AiArmyPhase.Recovering,AiArmyPhase.Regrouping}.Contains(group.Phase));
                Assert.AreEqual(Army,Registry.ArmyFor(Patient));Assert.Contains(Patient,group.Members);Assert.False(group.Reinforcements.Contains(Patient));
                var core=o.Entities.Where(u=>u.Id!=Patient&&u.Owner==o.Owner&&u.Health>0&&!u.Held&&!u.Departing&&u.OrderStamp?.Origin!=PlayableOrderOrigin.Human&&group.Members.Contains(u.Id)&&!group.Reinforcements.Contains(u.Id)&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0&&PlayableUnitRules.HealthRatio(P,u)>=Ai.DifficultyValue(AiDifficulty.Veteran,"micro.returnCoreHealth")).ToArray();
                Assert.GreaterOrEqual(core.Length,Ai.DifficultyValue(AiDifficulty.Veteran,"micro.returnCoreUnits"));
                Assert.True(core.Any(a=>(anchor==0||a.Id==anchor)&&core.Count(b=>Distance(a.Position,b.Position)<=Ai.DifficultyValue(AiDifficulty.Veteran,"micro.supportRadius"))>=Ai.DifficultyValue(AiDifficulty.Veteran,"micro.returnCoreUnits")));
                Assert.True(o.Buildings.Any(b=>b.Id==Asset&&b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready));
            }
            public void Dispose(){D?.Navigation.ReleaseCommittedHostRoot();D?.DestroyOwnedNavigation();}
        }
        [Test,Timeout(90000)] public void ZeroTickPreflightForStableGuardRepair()
        {
            using var s=new Setup();Assert.Zero(s.D.Tick);Directory.CreateDirectory(Output);string path=Path.Combine(Output,"zero-tick-4.json");Assert.False(File.Exists(path));File.WriteAllText(path,Newtonsoft.Json.JsonConvert.SerializeObject(s.Preflight,Newtonsoft.Json.Formatting.Indented));TestContext.WriteLine("ZERO_TICK_PREFLIGHT "+path);
        }
        [Test,Timeout(90000)] public void ReadyPatientPhysicallyRejoinsPreexistingGuardCore()=>RunStableGuard();
        [Test,Timeout(90000)] public void RecastReadyPatientPhysicallyRejoinsPreexistingGuardCore()
        {
            var data=P.CopyData();data.groundRouteBackend=1;RunStableGuard(PlayableProfile.Create(data));
        }
        [Test,Timeout(90000)] public void ReducedRepairRangePhysicallyHealsRejoinsAndRestoresCurrent()
        {
            var data=P.CopyData();data.groundRouteBackend=1;data.turretRange=6;
            var profile=PlayableProfile.Create(data);
            Assert.AreEqual(10,profile.ExplorerRange*profile.TurretRangeMultiplier,"Keep legacy range distinct from the reduced ordinary repair radius.");
            Assert.AreEqual(6,profile.TurretRange);
            RunStableGuard(profile,true,Path.Combine(Output,"reduced-current-radius"));
        }
        [Test,Timeout(90000)] public void AuthoredRampRepairPhysicallyRejoinsPreexistingGuardCore()
        {
            var data=P.CopyData();data.groundRouteBackend=1;var map=new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
            RunStableGuard(PlayableProfile.Create(data,map),outputOverride:Path.Combine(Output,"authored-ramp"));
        }
        [Test,Timeout(90000)] public void AuthoredRampRepairCurrentContinuesExactly()
        {
            var data=P.CopyData();data.groundRouteBackend=1;var map=new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
            RunStableGuard(PlayableProfile.Create(data,map),true,Path.Combine(Output,"authored-ramp-current"));
        }
        [Test,Timeout(90000)] public void RecastRepairCurrentContinuesExactly()
        {
            var data=P.CopyData();data.groundRouteBackend=1;RunStableGuard(PlayableProfile.Create(data),true);
        }
        [UnityTest,Timeout(90000)] public IEnumerator RecastRepairCurrentAndRecordedFrames()
        {
            var data=P.CopyData();data.groundRouteBackend=1;var profile=PlayableProfile.Create(data);
            string output=Environment.GetEnvironmentVariable("COMBAT_MICRO_STABLE_GUARD_OUTPUT")??Path.Combine(Output,"recorded-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff"));
            RunStableGuard(profile,true,output);
            yield return RenderRecordedRepair(profile,output,output);
        }
        [UnityTest,Timeout(90000)] public IEnumerator AuthoredRecordedRepairFramesFromCurrentWitness()
        {
            var data=P.CopyData();data.groundRouteBackend=1;var profile=PlayableProfile.Create(data,new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
            string input=Environment.GetEnvironmentVariable("COMBAT_MICRO_AUTHORED_FRAMES_INPUT");Assert.False(string.IsNullOrWhiteSpace(input));
            string output=Environment.GetEnvironmentVariable("COMBAT_MICRO_AUTHORED_FRAMES_OUTPUT")??Path.Combine(Output,"authored-frames-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff"));
            foreach(string stage in new[]{"current","healing","joined"})Assert.True(File.Exists(Path.Combine(input,"repair-"+stage+".world")));
            yield return RenderRecordedRepair(profile,input,output);
        }
        IEnumerator RenderRecordedRepair(PlayableProfile profile,string input,string output)
        {
            Directory.CreateDirectory(output);
            using var authored=new Setup(profile);
            var ambient=RenderSettings.ambientLight;var ambientMode=RenderSettings.ambientMode;
            var pipeline=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;float shadows=pipeline?.shadowDistance??0;
            var root=new GameObject("Recorded actual stable guard repair");var target=new RenderTexture(1600,1000,24);target.Create();PlayableWorld world=null;
            try{
                world=new PlayableWorld(root.transform,profile,staged:true);yield return world.Preparation;world.InspectionReveal();
                var camera=new GameObject("Recorded match inspection camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.targetTexture=target;camera.orthographic=true;camera.orthographicSize=17;camera.nearClipPlane=.05f;camera.farClipPlane=200;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.1f,.13f);
                var focus=world.Point(new NavPoint(-22,17));camera.transform.position=focus+new Vector3(0,32,-24);camera.transform.LookAt(focus);
                camera.gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());
                var sun=new GameObject("Recorded match sun").AddComponent<Light>();sun.transform.SetParent(root.transform);IndustrialLightingProfile.Load(null).ApplyScene(sun);
                var renderer=new GameObject("Actual snapshot production renderer").AddComponent<PlayableBootstrap>();renderer.transform.SetParent(root.transform);renderer.ConfigureShkvalEvidence(world,profile);
                foreach(string stage in new[]{"current","healing","joined"}){
                    var bytes=File.ReadAllBytes(Path.Combine(input,"repair-"+stage+".world"));var restored=PlayableAuthorityTick.RestoreBytes(bytes,authored.Config);
                    try{
                        CollectionAssert.AreEqual(bytes,restored.CaptureBytes());foreach(var owner in restored.Scheduler.Owners)owner.BindExecutionDomain(restored.Domain);restored.Domain.BindCombatMotionAuthority(()=>restored.Scheduler.Owners);
                        var view=restored.ParticipantView("player-1");Assert.AreEqual(Gen,view.Generation);Assert.AreEqual(Seed,view.Seed);
                        if(profile.AuthoredMap!=null){var patient=view.Entities.Single(u=>u.Id==authored.Patient);var tower=view.Buildings.Single(b=>b.Id==authored.Tower);var center=world.Point(new NavPoint((patient.Position.X+tower.Position.X)/2,(patient.Position.Z+tower.Position.Z)/2));camera.transform.position=center+new Vector3(0,32,-24);camera.transform.LookAt(center);}
                        world.Clear();renderer.RenderShkvalEvidence(view,camera);
                        foreach(var b in view.Buildings)if(world.Actors.TryGetValue(b.Id,out var a))a.Healing?.Drive(b,view,profile);
                        foreach(var u in view.Entities)if(world.Actors.TryGetValue(u.Id,out var a))PlayableWorld.UpdateHealth(a,camera,(float)PlayableUnitRules.HealthRatio(profile,u),u.Id==authored.Patient);
                        for(int n=0;n<8;n++)yield return null;
                        string name="actual-"+stage+"-tick-"+view.Tick;string path=Path.Combine(output,name+".png");Assert.False(File.Exists(path));
                        var old=RenderTexture.active;var pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);
                        try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();Assert.Greater(pixels.GetPixels32().Count(c=>c.r>20||c.g>30||c.b>40),1000);File.WriteAllBytes(path,pixels.EncodeToPNG());}finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(pixels);}
                        File.WriteAllText(Path.Combine(output,name+".json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{State="Production rendering of actual recorded current match; inspection camera, no synthetic pose/health",view.Tick,view.Seed,view.Generation,profile.ProfileId,profile.Revision,authored.Patient,Entities=view.Entities.Select(u=>new{u.Id,u.Kind,u.Position,u.Health,u.ExactHealth,u.OrderStamp}).ToArray(),Buildings=view.Buildings.Select(b=>new{b.Id,b.Kind,b.Position,b.Healing}).ToArray()},Newtonsoft.Json.Formatting.Indented));
                        TestContext.WriteLine("RECORDED_ACTUAL_FRAME "+path);
                    }finally{restored.Stop();restored.Domain.Navigation.ReleaseCommittedHostRoot();restored.Domain.DestroyOwnedNavigation();Assert.Zero(restored.Domain.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(restored.Domain.Navigation.PhysicalHostBudget.HeldBytes);}
                }
            }finally{RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=ambientMode;if(pipeline!=null)pipeline.shadowDistance=shadows;world?.Dispose();UnityEngine.Object.DestroyImmediate(root);target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
        void RunStableGuard(PlayableProfile selectedProfile=null,bool currentControl=false,string outputOverride=null)
        {
            string output=outputOverride??Output;string path=Path.Combine(output,"ordinary-attempt-4.json");Assert.False(File.Exists(path));
            using var s=new Setup(selectedProfile);var P=s.Profile;CertifiedRouteWorker worker=null;var frames=new List<object>(2048);int dropped=0,errors=0;string traceError=null;
            PlayableAuthorityTick cold=null;CertifiedRouteWorker coldWorker=null;byte[] currentBytes=null,healingBytes=null,joinedBytes=null;bool currentExact=false;
            long repairScheduled=-1,repairApplied=-1,healed=-1,followScheduled=-1,followApplied=-1,followSequence=0,nativeJoin=-1,joined=-1;int anchor=0;bool repairMotion=false,towerLock=false,returnMotion=false;
            NavPoint repairOrigin=default,repairGoal=default,followOrigin=default;double minHealth=100,maxHealth=28,firstHealDistance=-1;long affected=0;var ownedSurfaces=new HashSet<string>();var pathSurfaces=new HashSet<string>();
            var records=new Dictionary<string,PlayableAiTraceRecord>();var thread=Thread.CurrentThread.ManagedThreadId;var nativeProbe=new StableGuardPendingReadback();
            Action<string,long,PlayableAiObservation,AiArmyRegistryState,PlayableAiAction[]> observer=null;bool installed=false;
            object MapAt(string name){var map=(IDictionary)Field(s.Micro,name);return map.Contains(s.Patient)?map[s.Patient]:null;}
            void Trace(string stage,PlayableAiObservation o,AiArmyRegistryState groups)
            {
                try{if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Background owner observation");if(frames.Count>=2048){dropped++;return;}
                    object native=null;
                    // Native root pins are forbidden inside the pre-decision callback.
                    if(stage=="post-frame"){
                        var client=MapAt("clients");
                        if(!nativeProbe.Bound&&client!=null&&(AiCombatMicroPhase)Field(client,"Phase")==AiCombatMicroPhase.Repair&&(long)Field(client,"Sequence")>0){
                            var actor=o.Entities.Single(u=>u.Id==s.Patient&&u.Owner==o.Owner);long seq=(long)Field(client,"Sequence"),actionId=(long)Field(client,"ActionId");
                            var receipt=s.Loop.Checkpoint.Records.FirstOrDefault(r=>r.Policy==AiCombatMicroPolicy.Policy&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied&&r.CommandSequence==seq&&r.ActionId==actionId);
                            nativeProbe.Bind(Gen,s.Patient,s.Tower,(int)Field(client,"Tower"),o.Owner,actor,receipt,seq,actionId);
                        }
                        native=nativeProbe.Capture(s.D,worker,o);
                    }
                    frames.Add(new{Stage=stage,NativeProbe=native,WorldTick=s.D.Tick,ObservationTick=o.Tick,OwnerDecisionTick=s.Loop.Checkpoint.DecisionTick,Army=s.Army,CurrentArmy=s.Registry.ArmyFor(s.Patient),Client=Scalars(MapAt("clients")),Certificate=Scalars(MapAt("certificates")),
                        Own=o.Entities.Where(u=>u.Owner==o.Owner).Select(u=>new{u.Id,u.Kind,u.Health,u.ExactHealth,u.MaxHealth,u.Position,u.Held,u.Departing,u.NavigationOutcome,u.CurrentOrder,u.OrderStamp,u.Queue}).ToArray(),
                        Armies=groups.Armies.Select(g=>g.Copy()).ToArray(),Towers=o.Buildings.Where(b=>b.Id==s.Tower).Select(b=>new{b.Id,b.Health,b.Phase,b.Position,b.Healing}).ToArray(),
                        Records=((IEnumerable)Field(s.Loop,"records")).Cast<PlayableAiTraceRecord>().ToArray(),
                        Pending=((IEnumerable)Field(s.Loop,"pendingItems")).Cast<object>().Select(p=>new{Values=Scalars(p),Action=((PlayableAiAction)Field(p,"Action")),Intent=Scalars(Field(p,"Intent"))}).ToArray()});
                }catch(Exception ex){errors++;if(traceError==null)traceError=ex.ToString();}
            }
            try{
                Assert.IsNull(PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests);Assert.IsNull(PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests);
                observer=(id,tick,o,groups,actions)=>Trace("pre-decision",o,groups);PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests="player-1";PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=observer;installed=true;
                worker=new CertifiedRouteWorker(s.Authority.NavigationBinding,P.NavigationRequestsPerFrame);var clock=Stopwatch.StartNew();
                while(s.D.Tick<900&&clock.Elapsed.TotalSeconds<60){
                    if(!s.Authority.TryAdvance()){Thread.Sleep(1);continue;}Assert.IsNull(worker.Publication.Failure);
                    if(P.AuthoredMap is ThreeCrossingsMap){Assert.True(s.D.Navigation.Crowd.TryGet(s.Patient,out var actual));pathSurfaces.Add(actual.Location.SurfaceId);if(s.D.CombatMotionAuthority.AffectedPublications>affected){affected=s.D.CombatMotionAuthority.AffectedPublications;ownedSurfaces.Add(actual.Location.SurfaceId);}}
                    if(currentControl&&s.D.Tick==90){
                        Assert.GreaterOrEqual(repairApplied,0,"Capture the real applied repair, not an authored surrogate.");
                        currentBytes=s.Authority.CaptureBytes();
                        using(var state=PlayableWorldState.Decode(currentBytes)){
                            Assert.AreEqual(PlayableWorldState.CurrentVersion,state.Version);Assert.AreEqual(Gen,state.Generation);Assert.AreEqual(Seed,state.Seed);Assert.AreEqual(90,state.Tick);Assert.AreEqual(s.Config.SourceIdentity,state.SourceIdentity);CollectionAssert.AreEqual(WorldWire.Binding(P),state.Binding);
                        }
                        cold=PlayableAuthorityTick.RestoreBytes(currentBytes,s.Config);CollectionAssert.AreEqual(currentBytes,cold.CaptureBytes(),"Exact current capture before source rebind.");
                        foreach(var owner in cold.Scheduler.Owners)owner.BindExecutionDomain(cold.Domain);
                        cold.Domain.BindCombatMotionAuthority(()=>cold.Scheduler.Owners);
                        CollectionAssert.AreEqual(currentBytes,cold.CaptureBytes(),"Paid source rebind preserves accepted repair execution.");
                        coldWorker=new CertifiedRouteWorker(cold.NavigationBinding,P.NavigationRequestsPerFrame);
                    }
                    if(coldWorker!=null&&s.D.Tick>90&&s.D.Tick<=135){
                        while(cold.Tick<s.D.Tick&&clock.Elapsed.TotalSeconds<60){if(!cold.TryAdvance())Thread.Sleep(1);Assert.IsNull(coldWorker.Publication.Failure);}
                        Assert.AreEqual(s.D.Tick,cold.Tick);
                        if(s.D.Tick==135){healingBytes=s.Authority.CaptureBytes();CollectionAssert.AreEqual(healingBytes,cold.CaptureBytes(),"Exact full current repair continuation for45 ticks.");currentExact=true;}
                    }
                    var view=s.Authority.ParticipantView("player-1");var o=PlayableAiObservation.From(view);var actor=o.Entities.Single(u=>u.Id==s.Patient&&u.Owner==o.Owner);Assert.Greater(actor.Health,0);
                    Trace("post-frame",o,s.Registry.Capture());
                    foreach(var r in s.Loop.Checkpoint.Records)records[r.ActionId+":"+r.Status]=r;
                    var client=MapAt("clients");var cert=MapAt("certificates");
                    if(client!=null&&(AiCombatMicroPhase)Field(client,"Phase")==AiCombatMicroPhase.Repair){
                        if(repairScheduled<0){repairScheduled=s.D.Tick;repairOrigin=actor.Position;repairGoal=(NavPoint)Field(client,"Goal");Assert.LessOrEqual(Distance(repairGoal,o.Buildings.Single(b=>b.Id==s.Tower).Position),P.TurretRange,"Repair goal must use the current ordinary healing radius.");Assert.Less(PlayableUnitRules.HealthRatio(P,actor),Ai.DifficultyValue(AiDifficulty.Veteran,"micro.combatRepairHealth"));}
                        s.CheckCore(o,0);Assert.AreEqual(s.Army,(long)Field(client,"Army"));
                        if((long)Field(client,"Sequence")>0&&repairApplied<0){repairApplied=s.D.Tick;Assert.NotNull(cert);Assert.AreEqual(s.Army,(long)Field(cert,"Army"));Assert.AreEqual(s.Tower,(int)Field(client,"Tower"));
                            var receipt=records.Values.Single(r=>r.Status==PlayableAiDeliveryStatus.Applied&&r.Policy==AiCombatMicroPolicy.Policy&&r.Kind==PlayableCommandKind.Move&&r.CommandSequence==(long)Field(client,"Sequence"));
                            Assert.AreEqual(PlayableCommandStatus.Applied,receipt.RuntimeStatus);Assert.AreEqual(actor.OrderStamp.ActionId,receipt.ActionId);Assert.AreEqual(actor.OrderStamp.Sequence,receipt.CommandSequence);Assert.AreEqual(PlayableOrderOrigin.Ai,actor.OrderStamp.Origin);Assert.AreEqual(PlayableCommandKind.Move,actor.OrderStamp.Kind);
                            Assert.AreEqual(Gen,receipt.ReceiptIdentity.Generation);Assert.AreEqual("player-1",receipt.ReceiptIdentity.OwnerId);Assert.AreEqual(PlayableAiOpeningComposition.SourceIdentity,receipt.SourceIdentity);
                        }
                    }
                    if(repairApplied>=0){
                        if(healed<0)repairMotion|=Distance(actor.Position,repairOrigin)>1&&Distance(actor.Position,repairGoal)<Distance(repairOrigin,repairGoal)-1;
                        var tower=o.Buildings.Single(b=>b.Id==s.Tower);towerLock|=tower.Healing?.TargetId==s.Patient;
                        minHealth=Math.Min(minHealth,actor.ExactHealth??actor.Health);maxHealth=Math.Max(maxHealth,actor.ExactHealth??actor.Health);
                        if(firstHealDistance<0&&(actor.ExactHealth??actor.Health)>28){firstHealDistance=Distance(actor.Position,tower.Position);Assert.LessOrEqual(firstHealDistance,P.TurretRange,"Actual HP growth must occur inside the current healing radius.");}
                        if(healed<0&&PlayableUnitRules.HealthRatio(P,actor)>=Ai.DifficultyValue(AiDifficulty.Veteran,"micro.repairReturnHealth")){healed=s.D.Tick;s.CheckCore(o,0);}
                        s.CheckCore(o,anchor);
                    }
                    foreach(var pending in ((IEnumerable)Field(s.Loop,"pendingItems")).Cast<object>().Where(p=>(string)Field(p,"PolicyName")==AiCombatMicroPolicy.Policy)){
                        var action=(PlayableAiAction)Field(pending,"Action");if(action.Kind!=PlayableCommandKind.Follow||!action.EntityIds.Contains(s.Patient))continue;
                        if(followScheduled<0){followScheduled=s.D.Tick;anchor=action.TargetId;Assert.GreaterOrEqual(healed,0);s.CheckCore(o,anchor);}
                    }
                    var applied=records.Values.FirstOrDefault(r=>r.Policy==AiCombatMicroPolicy.Policy&&r.Kind==PlayableCommandKind.Follow&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ActionId==actor.OrderStamp?.ActionId);
                    if(applied!=null&&followApplied<0){
                        Assert.GreaterOrEqual(followScheduled,0);Assert.GreaterOrEqual(healed,0);Assert.NotNull(cert);Assert.AreEqual(anchor,(int)Field(cert,"Target"));Assert.AreEqual(s.Army,(long)Field(cert,"Army"));
                        Assert.AreEqual("player-1",applied.ReceiptIdentity.OwnerId);Assert.AreEqual(Gen,applied.ReceiptIdentity.Generation);Assert.Greater(applied.ReceiptIdentity.DecisionOrdinal,0);Assert.Greater(applied.ReceiptIdentity.ActionOrdinal,0);
                        Assert.AreEqual(PlayableAiOpeningComposition.SourceIdentity,applied.SourceIdentity);Assert.AreEqual(applied.CommandSequence,actor.OrderStamp.Sequence);Assert.Greater(applied.CommandSequence,1);Assert.Greater(applied.ActionId,1);
                        Assert.AreEqual(PlayableCommandStatus.Applied,applied.RuntimeStatus);Assert.AreEqual(PlayableOrderOrigin.Ai,actor.OrderStamp.Origin);Assert.AreEqual(PlayableCommandKind.Follow,actor.OrderStamp.Kind);
                        followSequence=applied.CommandSequence;followApplied=s.D.Tick;followOrigin=actor.Position;s.CheckCore(o,anchor);
                    }
                    var leader=o.Entities.FirstOrDefault(u=>u.Id==anchor&&u.Owner==o.Owner&&u.Health>0);
                    if(followApplied>=0&&leader!=null){
                        var goal=PlayableUnitRules.FollowGoal(P,leader.Position,leader.HullHeading);returnMotion|=Distance(actor.Position,followOrigin)>1&&Distance(actor.Position,goal)<Distance(followOrigin,goal)-1;
                        bool reached=returnMotion&&actor.OrderStamp?.Sequence==followSequence&&actor.CurrentOrder?.CommandSequence==followSequence&&actor.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow&&actor.CurrentOrder.TargetId==anchor&&Distance(actor.Position,leader.Position)<=P.FollowDistance+P.FollowArrivalTolerance;
                        if(!reached)nativeJoin=-1;else if(nativeJoin<0)nativeJoin=s.D.Tick;
                        if(reached&&!s.Micro.Reserves(s.Patient)){joined=s.D.Tick;s.CheckCore(o,anchor);Assert.LessOrEqual(joined-nativeJoin,AiProfile.SecondsToTicks(Ai.DifficultyValue(AiDifficulty.Veteran,"decisionSeconds"),30));break;}
                    }
                    Thread.Sleep(1);
                }
                TestContext.WriteLine($"STABLE_GUARD seed7 gen905 tick={s.D.Tick} army={s.Army} repairScheduled={repairScheduled} repairApplied={repairApplied} repairMotion={repairMotion} towerLock={towerLock} heal={healed} minHP={minHealth:R} maxHP={maxHealth:R} followScheduled={followScheduled} followApplied={followApplied} returnMotion={returnMotion} nativeJoin={nativeJoin} joined={joined} pumps={worker.Publication.Pumps} repairRadius={P.TurretRange:R} legacyRange={P.ExplorerRange*P.TurretRangeMultiplier:R} firstHealDistance={firstHealDistance:R}");
                Assert.GreaterOrEqual(repairScheduled,0);Assert.GreaterOrEqual(repairApplied,repairScheduled);Assert.True(repairMotion);Assert.True(towerLock);Assert.GreaterOrEqual(firstHealDistance,0);Assert.GreaterOrEqual(healed,repairApplied);Assert.Greater(maxHealth,minHealth);
                Assert.GreaterOrEqual(followScheduled,healed);Assert.GreaterOrEqual(followApplied,followScheduled);Assert.True(returnMotion);Assert.GreaterOrEqual(joined,followApplied);
                var human=s.Authority.ParticipantView("player-1").Entities.Single(u=>u.Id==s.Explorer);Assert.True(human.Held);Assert.AreEqual(PlayableOrderOrigin.Human,human.OrderStamp.Origin);
                var appliedRecords=records.Values.Where(r=>r.Status==PlayableAiDeliveryStatus.Applied).ToArray();Assert.LessOrEqual(appliedRecords.Length,(int)Ai.DifficultyValue(AiDifficulty.Veteran,"control.burst")+Math.Floor(s.D.Tick*Ai.DifficultyValue(AiDifficulty.Veteran,"control.ordersPerMinute")/1800));
                Assert.Greater(worker.Publication.Pumps,0);Assert.Zero(dropped);Assert.Zero(errors);
                if(P.AuthoredMap is ThreeCrossingsMap){int sx=Math.Sign(s.PatientOrigin.X),sz=Math.Sign(s.PatientOrigin.Z);Assert.Greater(affected,0);Assert.Contains(sx<0?"bank/west":"bank/east",pathSurfaces.ToArray(),"The ordinary initial Guard approaches from bank before Micro takes over on the ramp.");Assert.Contains("ramp/"+sx+"/"+sz+"/z",ownedSurfaces.ToArray());Assert.Contains("deck/"+sx+"/"+sz,ownedSurfaces.ToArray());TestContext.WriteLine("AUTHORED_MICRO affected="+affected+" surfaces="+string.Join(",",ownedSurfaces));}
                Assert.True(nativeProbe.Bound);Assert.Zero(nativeProbe.Errors);Assert.Zero(nativeProbe.Missing);Assert.Zero(nativeProbe.Truncated);Assert.AreEqual(nativeProbe.PinsAcquired,nativeProbe.PinsReleased);
                if(currentControl){Assert.True(currentExact);joinedBytes=s.Authority.CaptureBytes();TestContext.WriteLine($"REPAIR_CURRENT tick90 bytes={currentBytes.Length} immediateExact=True reboundExact=True continued45Exact=True");}
            }finally{
                coldWorker?.Dispose();if(coldWorker!=null)Assert.True(SpinWait.SpinUntil(()=>coldWorker.IsStopped,5000));
                if(cold!=null){cold.Stop();cold.Domain.Navigation.ReleaseCommittedHostRoot();cold.Domain.DestroyOwnedNavigation();Assert.Zero(cold.Domain.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(cold.Domain.Navigation.PhysicalHostBudget.HeldBytes);}
                worker?.Dispose();bool stopped=worker==null||SpinWait.SpinUntil(()=>worker.IsStopped,5000);bool owned=!installed||ReferenceEquals(PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests,observer);
                if(installed&&owned){PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests=null;}
                Directory.CreateDirectory(output);File.WriteAllText(path,Newtonsoft.Json.JsonConvert.SerializeObject(new{Preflight=s.Preflight,Tick=s.D.Tick,PathSurfaces=pathSurfaces.ToArray(),OwnedSurfaces=ownedSurfaces.ToArray(),AffectedPublications=affected,RepairRadius=P.TurretRange,LegacyRange=P.ExplorerRange*P.TurretRangeMultiplier,firstHealDistance,repairScheduled,repairApplied,healed,followScheduled,followApplied,nativeJoin,joined,repairMotion,towerLock,returnMotion,minHealth,maxHealth,Frames=frames,Dropped=dropped,Errors=errors,Error=traceError,NativeProbe=new{nativeProbe.Bound,nativeProbe.Samples,nativeProbe.Errors,nativeProbe.Missing,nativeProbe.Truncated,nativeProbe.FirstError,nativeProbe.PinsAcquired,nativeProbe.PinsReleased,nativeProbe.MaxCopyTicks,nativeProbe.MaxReleaseTicks,StopwatchFrequency=Stopwatch.Frequency},WorkerStopped=stopped,ObserverOwned=owned},Newtonsoft.Json.Formatting.Indented));
                if(currentBytes!=null)File.WriteAllBytes(Path.Combine(output,"repair-current.world"),currentBytes);
                if(healingBytes!=null)File.WriteAllBytes(Path.Combine(output,"repair-healing.world"),healingBytes);
                if(joinedBytes!=null)File.WriteAllBytes(Path.Combine(output,"repair-joined.world"),joinedBytes);
                Assert.True(stopped);Assert.True(owned);
            }
        }
    }
}
