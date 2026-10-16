using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Presentation;
using Spacewars.Headless;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiTechnologyFundingMechanismTests
    {
        private static byte[] BudgetWire(AiBudgetState state,long tick)
        {
            var ledger=AiBudgetLedger.Restore(state,tick);
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
            {
                AiBudgetStateWire.Write(writer,ledger);return stream.ToArray();
            }
        }

        [Test] public void DiagnosticBudgetCaptureIsOwnerBoundEquivalentAndReadOnly()
        {
            var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;Assert.NotNull(d);
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();var config=((AiTestMap)p.AuthoredMap).Configuration(p,7109,roster);
            string fixture=Path.Combine(d.FullName,"unity/Tests/Fixtures/o6-tech-funding/checkpoint-3900-nav-v8.world");var a=PlayableAuthorityTick.RestoreBytes(File.ReadAllBytes(fixture),config);
            var before=a.CaptureBytes();
            var targeted=a.CaptureDiagnosticBudget("ai-4");
            var plural=a.CaptureDiagnosticBudgets().Single(x=>x.OwnerId=="ai-4");
            Assert.AreEqual("ai-4",targeted.OwnerId,"The requested owner identity must bind the returned budget.");
            CollectionAssert.AreEqual(BudgetWire(plural,a.Tick),BudgetWire(targeted,a.Tick),"Targeted and plural APIs must serialize the whole budget identically.");
            Assert.Throws<ArgumentException>(()=>a.CaptureDiagnosticBudget("missing-owner"));
            Assert.Throws<ArgumentException>(()=>a.CaptureDiagnosticBudget(" "));
            CollectionAssert.AreEqual(before,a.CaptureBytes(),"Diagnostic capture must not mutate authority state.");
            a.Stop();
        }

        [Test] public void ActualPaidBlockedScienceFoundation_RetainsResearchAndRestoresTypedMemo()
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());while(root!=null&&!File.Exists(Path.Combine(root.FullName,"workflow/project.json")))root=root.Parent;Assert.NotNull(root);
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();var config=((AiTestMap)p.AuthoredMap).Configuration(p,7109,roster);
            var a=PlayableAuthorityTick.RestoreBytes(File.ReadAllBytes(Path.Combine(root.FullName,"unity/Tests/Fixtures/o6-tech-funding/checkpoint-3900.world")),config);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static;
            var domain=typeof(PlayableAuthorityTick).GetField("domain",flags).GetValue(a);object Call(string name,params object[] args)=>domain.GetType().GetMethod(name,flags).Invoke(domain,args);
            var owner=a.ParticipantView("ai-4").Owner;Call("AddCredits",owner,424d); // explicit core fixture funding, separate from natural continuation
            var o=PlayableAiObservation.From(a.ParticipantView("ai-4"));Assert.AreEqual(600,o.Credits);
            var b=new AiBudgetLedger(o.OwnerId,71);b.ObserveBank(o.Credits);var arbiter=new AiDecisionArbiter(AiProfile.Initial);var f=new AiTechnologyFunding(o.OwnerId,71);var planner=new AiTechnologyPlanner();
            f.Observe(o,p,AiProfile.Initial,b,arbiter,planner.Plan(o,p,AiProfile.Initial,b.Capture()),true);
            var hold=b.Capture().Reserved.Single();var action=hold.Action;var intent=new AiIntent("blocked-science","economy",action,o.Tick+30,(int)Math.Ceiling(p.ScienceCreditCost),0,Array.Empty<string>(),reservationId:hold.Id);var receipt=new AiReceiptIdentity(71,o.OwnerId,1,1);
            Assert.True(f.Accept(intent,receipt,o,p,b,arbiter));
            // Real opposing unit on the paid footprint keeps AdvanceFoundations Pending,
            // with original Health0; no invented HP or skipped construction lifecycle.
            var slot=o.Sites.Single(x=>x.Site.Id==action.SiteId).Site.Slots.Single(x=>x.Id==action.SlotId);
            Call("SpawnUnit",slot.Position,owner==PlayableOwner.Player?PlayableOwner.Enemy:PlayableOwner.Player,PlayableEntityKind.Tank);
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,(long)Call("AllocateAiSequence"),o.OwnerId,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:action.SiteId,slotId:action.SlotId,parentId:action.ParentId,buildingKind:PlayableBuildingKind.ScientificCenter)).Status);
            b.Reconcile(intent.Id,receipt,PlayableAiDeliveryStatus.Applied);Call("AdvanceFoundations");
            o=PlayableAiObservation.From(a.ParticipantView("ai-4"));var center=o.Buildings.Single(x=>x.SiteId==action.SiteId&&x.SlotId==action.SlotId);Assert.AreEqual(ConstructionPhase.Pending,center.Phase);Assert.Zero(center.Health);Assert.NotNull(center.BlockedReason);
            b.ObserveBank(o.Credits);f.Observe(o,p,AiProfile.Initial,b,arbiter,planner.Plan(o,p,AiProfile.Initial,b.Capture(),preferredFunding:f.Preferred(arbiter)),true);
            Assert.AreEqual((int)Math.Ceiling(AiResearchCatalog.For(PlayableResearchKind.TankChassis).Cost(p)),b.Reserved);Assert.AreEqual(p.ScienceCreditCost,b.PaidTotal);Assert.AreEqual(PlayableCommandKind.QueueResearch,b.Capture().Reserved.Single().Action.Kind);
            var before=a.CaptureBytes();var validation=Call("TechnologyFundingValidationView",o.OwnerId,7109);typeof(AiTechnologyFunding).GetMethod("Validate",flags).Invoke(f,new object[]{validation,p,b,arbiter,Array.Empty<AiIntent>()});CollectionAssert.AreEqual(before,a.CaptureBytes(),"validation must be pure");
            using(var stream=new MemoryStream())
            {
                using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))typeof(AiDecisionArbiter).GetMethod("WriteState",flags).Invoke(arbiter,new object[]{writer});var data=stream.ToArray();stream.Position=0;var restored=new AiDecisionArbiter(AiProfile.Initial);
                using(var reader=new BinaryReader(stream,System.Text.Encoding.UTF8,true))typeof(AiDecisionArbiter).GetMethod("ReadState",flags).Invoke(restored,new object[]{reader,o.Tick,o.OwnerId,71L,p,AiProfile.Initial});typeof(AiTechnologyFunding).GetMethod("Validate",flags).Invoke(f,new object[]{validation,p,b,restored,Array.Empty<AiIntent>()});
                using(var output=new MemoryStream()){using(var writer=new BinaryWriter(output,System.Text.Encoding.UTF8,true))typeof(AiDecisionArbiter).GetMethod("WriteState",flags).Invoke(restored,new object[]{writer});CollectionAssert.AreEqual(data,output.ToArray());}
            }
            a.Stop();
        }
        [Test] public void ActualMalformedMemoParent_RejectBeforeRestoredAuthorityPublication()
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());while(root!=null&&!File.Exists(Path.Combine(root.FullName,"workflow/project.json")))root=root.Parent;var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();var config=((AiTestMap)p.AuthoredMap).Configuration(p,7109,roster);
            var a=PlayableAuthorityTick.RestoreBytes(File.ReadAllBytes(Path.Combine(root.FullName,"unity/Tests/Fixtures/o6-tech-funding/checkpoint-3900.world")),config);const BindingFlags f=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;object Get(object v,string key)=>v.GetType().GetField(key,f).GetValue(v);
            var owner=((System.Collections.IEnumerable)Get(Get(a,"scheduler"),"owners")).Cast<object>().Single(x=>(string)x.GetType().GetProperty("OwnerId",f).GetValue(x)=="ai-4");var b=(AiBudgetLedger)Get(owner,"budget");var arbiter=(AiDecisionArbiter)Get(owner,"arbiter");var funding=(AiTechnologyFunding)Get(owner,"technologyFunding");var o=PlayableAiObservation.From(a.ParticipantView("ai-4"));b.ObserveBank(o.Credits);
            funding.Observe(o,p,AiProfile.Initial,b,arbiter,new AiTechnologyPlanner().Plan(o,p,AiProfile.Initial,b.Capture()),true);var valid=a.CaptureBytes();CollectionAssert.AreEqual(valid,PlayableAuthorityTick.RestoreBytes(valid,config).CaptureBytes(),"legitimate partial lowcash target restores");
            var held=b.Capture().Reserved.Single(AiTechnologyFunding.IsFunding);int factory=o.Buildings.First(x=>x.Owner==o.Owner&&x.Kind==PlayableBuildingKind.Factory).Id;var memo=typeof(AiTechnologyFunding).GetMethod("Load",f).Invoke(null,new object[]{arbiter});memo.GetType().GetField("Parent",f).SetValue(memo,factory);typeof(AiTechnologyFunding).GetMethod("Save",f).Invoke(null,new[]{(object)arbiter,memo});var id=(string)typeof(AiTechnologyFunding).GetMethod("Id",f).Invoke(null,new[]{memo});var old=held.Action;var bad=new PlayableAiAction(old.ActionId,old.PlayerId,old.ProfileId,old.ProfileRevision,old.Generation,old.SnapshotSequence,old.Kind,old.CopyEntityIds(),old.Target,old.SiteId,old.SlotId,old.BuildingKind,factory,seed:old.Seed,sourceIdentity:old.SourceIdentity);
            b.Release(held.Id);Assert.True(b.Reserve(new AiBudgetEntry(id,held.Purpose,bad,held.Amount,held.Priority,held.CreatedTick,held.ExpiresTick,held.ReleaseCondition,held.Claims,held.Terms)));var malformed=a.CaptureBytes();Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(malformed,config),"forged memo AND ledger ownFactory parent reject before restored authority can publish");CollectionAssert.AreEqual(malformed,a.CaptureBytes(),"rejected restore cannot mutate existing authority");a.Stop();
        }
        [Test] public void ActualSafeWoundedForce_FundsThenUncoveredThreatReleasesWithoutChangingCoverRules()
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());while(root!=null&&!File.Exists(Path.Combine(root.FullName,"workflow/project.json")))root=root.Parent;var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();var config=((AiTestMap)p.AuthoredMap).Configuration(p,7109,roster);var bytes=File.ReadAllBytes(Path.Combine(root.FullName,"unity/Tests/Fixtures/o6-tech-funding/checkpoint-3900-nav-v8.world"));var a=PlayableAuthorityTick.RestoreBytes(bytes,config);CollectionAssert.AreEqual(bytes,a.CaptureBytes());const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;object Get(object v,string name)=>v.GetType().GetField(name,f).GetValue(v);
            var owner=((System.Collections.IEnumerable)Get(Get(a,"scheduler"),"owners")).Cast<object>().Single(x=>(string)x.GetType().GetProperty("OwnerId",f).GetValue(x)=="ai-4");var o=PlayableAiObservation.From(a.ParticipantView("ai-4"));var b=(AiBudgetLedger)Get(owner,"budget");var arbiter=(AiDecisionArbiter)Get(owner,"arbiter");var funding=(AiTechnologyFunding)Get(owner,"technologyFunding");var composition=(AiPhaseCompositionPlanner)Get(owner,"composition");var opening=(AiOpeningExecutor)Get(owner,"openingExecutor");
            Assert.False(composition.ViableCover(o,p,AiProfile.Initial),"shared O4 cover remains 2.98/3 healthy-equivalents, never relaxed");Assert.False(opening.MissingEssentialEconomy(o,AiProfile.Initial));Assert.False((bool)owner.GetType().GetMethod("TechnologyEmergency",f).Invoke(owner,new object[]{o}));Assert.AreEqual(176,o.Credits);
            var capacity=((AiProductionDemand)Get(owner,"demand")).Assess(o,p,AiProfile.Initial,b.Available);Assert.False(capacity.CanScale);Assert.False(((AiInfrastructurePlanner)Get(owner,"infrastructure")).Plan(o,p,AiProfile.Initial,capacity).Any(c=>c.Legal&&c.Priority>0&&c.Action.Kind==PlayableCommandKind.StartBuildingRepair));
            bool allowed=(bool)owner.GetType().GetMethod("CanFundTechnology",f).Invoke(owner,new object[]{o,false,false});Assert.True(allowed,"safe useful tech funding is not blocked by support/latephase force threshold");var plan=new AiTechnologyPlanner().Plan(o,p,AiProfile.Initial,b.Capture());Assert.NotNull(plan.FundingGoal);
            funding.Observe(o,p,AiProfile.Initial,b,arbiter,plan,allowed,PlayableUnitRules.Cost(p,PlayableEntityKind.Explorer));Assert.Greater(b.Reserved,0);Assert.AreEqual(PlayableUnitRules.Cost(p,PlayableEntityKind.Explorer),b.Available);Assert.AreEqual(176,a.ParticipantView("ai-4").Credits);var held=a.CaptureBytes();CollectionAssert.AreEqual(held,PlayableAuthorityTick.RestoreBytes(held,config).CaptureBytes());
            var domain=Get(a,"domain");var hq=o.Buildings.Single(x=>x.Owner==o.Owner&&x.Kind==PlayableBuildingKind.Headquarters);var nav=(NavigationSession)Get(domain,"navigation");var geometry=(NavGeometry)domain.GetType().GetProperty("Geometry",f).GetValue(domain);double radius=PlayableUnitRules.Radius(p,PlayableEntityKind.Tank);var point=Enumerable.Range(0,16).Select(i=>new NavPoint(hq.Position.X+Math.Cos(i*Math.PI/8)*p.HeadquartersVisionRange/2,hq.Position.Z+Math.Sin(i*Math.PI/8)*p.HeadquartersVisionRange/2)).First(v=>geometry.IsFree(v,radius)&&nav.Crowd.CanPlace(v,radius));domain.GetType().GetMethod("SpawnUnit",f).Invoke(domain,new object[]{point,PlayableOwner.Player,PlayableEntityKind.Tank});var threat=PlayableAiObservation.From(a.ParticipantView("ai-4"));Assert.True((bool)owner.GetType().GetMethod("TechnologyEmergency",f).Invoke(owner,new object[]{threat}));long paid=b.PaidTotal;owner.GetType().GetMethod("GuardDefenseDelivery",f).Invoke(owner,new[]{domain});Assert.IsEmpty(b.Capture().Reserved.Where(AiTechnologyFunding.IsFunding));Assert.AreEqual(paid,b.PaidTotal,"uncovered actual visible threat releases savings before any technology spend");a.Stop();
        }
        [Test] public void Legacy3900NavigationSectionsMigrateIndependentlyOfIncompatibleAiBinding()
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());while(root!=null&&!File.Exists(Path.Combine(root.FullName,"workflow/project.json")))root=root.Parent;Assert.NotNull(root);
            var legacy=File.ReadAllBytes(Path.Combine(root.FullName,"unity/Tests/Fixtures/o6-tech-funding/checkpoint-3900.world"));var current=File.ReadAllBytes(Path.Combine(root.FullName,"unity/Tests/Fixtures/o6-tech-funding/checkpoint-3900-nav-v8.world"));
            Assert.AreEqual("adb5a83b5fc184ddd1dd74869c139fbef3a21aaba00a6c115f3f52be2c0e7f1c",Wire.Hash(legacy));Assert.AreEqual("689431f1a9f57cf971f8ffee84a51f4431978432262e509e0d2f225d3cffcc78",Wire.Hash(current));
            var x=PlayableWorldState.Decode(legacy);var y=PlayableWorldState.Decode(current);CollectionAssert.AreEqual(x.Domain,y.Domain);CollectionAssert.AreEqual(x.AiAuthority,y.AiAuthority);CollectionAssert.AreEqual(x.Binding,y.Binding);CollectionAssert.AreEqual(x.Participants,y.Participants);CollectionAssert.AreEqual(x.RuntimeTransport,y.RuntimeTransport);
            Assert.Throws<ArgumentException>(()=>WorldWire.Unpack(y.Navigation,WorldWire.ReadNavigationSessionState),"Historical v8 requires an explicit grammar");
            var terrain=new AiTestMap();var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),terrain);NavigationSession a=null,b=null;
            NavigationSession Import(byte[] bytes,NavigationV8Format format){
                using(var state=WorldWire.Unpack(bytes,r=>WorldWire.ReadNavigationSessionState(r,format))){
                    var geometry=new NavGeometry(state.Geometry.HalfExtent,state.Geometry.Obstacles,state.Geometry.Revision);
                    var navigation=state.NavigationGeometryIndex==0?geometry:new NavGeometry(state.NavigationGeometry.HalfExtent,state.NavigationGeometry.Obstacles,state.NavigationGeometry.Revision);
                    var result=new NavigationSession(state.Generation,geometry,profile.Navigation,navigation,terrain);result.ConfigureMarch(profile.GroupMarchMaximumStretch,profile.Revision);try{result.RestoreState(state);return result;}catch{result.DestroyNavigationAuthority();throw;}
                }
            }
            byte[] Capture(NavigationSession session){using(var state=session.CaptureState())return WorldWire.Pack(w=>WorldWire.Write(w,state));}
            try{
                a=Import(x.Navigation,NavigationV8Format.Unspecified);b=Import(y.Navigation,NavigationV8Format.MainInstalledLayout);Assert.AreNotSame(a.RoutePort,b.RoutePort);
                CollectionAssert.AreEqual(PlayableWorldRestoreTests.FactBytes(a,true),PlayableWorldRestoreTests.FactBytes(b,true),"Independent migrated navigation authority agrees without AI import or simulation");
                CollectionAssert.AreEqual(Capture(a),Capture(b));Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(a),a.PhysicalHostBudget.PhysicalBytes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(b),b.PhysicalHostBudget.PhysicalBytes);
            }finally{a?.DestroyNavigationAuthority();b?.DestroyNavigationAuthority();if(a!=null)Assert.Zero(a.PhysicalHostBudget.PhysicalBytes);if(b!=null)Assert.Zero(b.PhysicalHostBudget.PhysicalBytes);}
        }
        [Test] public void Legacy3900UpgradeAndCurrentV8FixtureKeepExactCompleteWorldState()
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());while(root!=null&&!File.Exists(Path.Combine(root.FullName,"workflow/project.json")))root=root.Parent;Assert.NotNull(root);
            var legacy=File.ReadAllBytes(Path.Combine(root.FullName,"unity/Tests/Fixtures/o6-tech-funding/checkpoint-3900.world"));var current=File.ReadAllBytes(Path.Combine(root.FullName,"unity/Tests/Fixtures/o6-tech-funding/checkpoint-3900-nav-v8.world"));
            Assert.AreEqual("adb5a83b5fc184ddd1dd74869c139fbef3a21aaba00a6c115f3f52be2c0e7f1c",Wire.Hash(legacy));Assert.AreEqual("689431f1a9f57cf971f8ffee84a51f4431978432262e509e0d2f225d3cffcc78",Wire.Hash(current));
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();var config=((AiTestMap)p.AuthoredMap).Configuration(p,7109,roster);Assert.AreEqual(500,p.StartingCredits);Assert.IsEmpty(config.ScenarioUnits);Assert.IsEmpty(config.ScenarioBuildings);
            Assert.AreEqual(254,LegacyTransactionProfileCodec.Fields.Length);var a=PlayableAuthorityTick.ImportLegacyCheckpoint(legacy,config,NavigationV8Format.Unspecified,LegacyTransactionProfileFormat.Original254,AiProfile.Initial,LegacyAiProfileFormat.BeforeExpansionSelection);var b=PlayableAuthorityTick.ImportLegacyCheckpoint(current,config,NavigationV8Format.MainInstalledLayout,LegacyTransactionProfileFormat.Original254,AiProfile.Initial,LegacyAiProfileFormat.BeforeExpansionSelection);PlayableAuthorityTick clone=null;
            try{
                Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(current,config),"ambiguousv8 cannot pick a grammar");var migrated=a.CaptureBytes();CollectionAssert.AreEqual(migrated,b.CaptureBytes(),"v7 and explicitmainv8 converge to the same actor-owned authority");clone=PlayableAuthorityTick.RestoreBytes(migrated,config);CollectionAssert.AreEqual(migrated,clone.CaptureBytes(),"New physical format recapture remains exact after accepted migration");
                Assert.AreEqual(3900,a.Tick);Assert.AreEqual(a.Tick,b.Tick);Assert.AreEqual(176,a.ParticipantView("ai-4").Credits);Assert.AreEqual(176,b.ParticipantView("ai-4").Credits);Assert.AreEqual(PlayableWorldRestoreTests.Facts(a.Domain,true),PlayableWorldRestoreTests.Facts(b.Domain,true),"All independentlyread presentauthority fields remainexact; no normalization/exclusion");
                var x=PlayableWorldState.Decode(legacy);var y=PlayableWorldState.Decode(current);Assert.AreEqual(x.Version,y.Version);Assert.AreEqual(x.Seed,y.Seed);Assert.AreEqual(x.Generation,y.Generation);Assert.AreEqual(x.Tick,y.Tick);Assert.AreEqual(x.SourceIdentity,y.SourceIdentity);
                CollectionAssert.AreEqual(x.Domain,y.Domain);CollectionAssert.AreEqual(x.AiAuthority,y.AiAuthority);CollectionAssert.AreEqual(x.Binding,y.Binding);CollectionAssert.AreEqual(x.Participants,y.Participants);CollectionAssert.AreEqual(x.RuntimeTransport,y.RuntimeTransport);Assert.AreEqual(8,x.Vision.Length);for(int i=0;i<8;i++)CollectionAssert.AreEqual(x.Vision[i],y.Vision[i]);
                Assert.AreEqual(26154,x.Navigation.Length);Assert.AreEqual(x.Navigation.Length+16,y.Navigation.Length);Assert.AreEqual(7,BitConverter.ToInt32(x.Navigation,13631));Assert.AreEqual(8,BitConverter.ToInt32(y.Navigation,13631));
                // Pin the sole version-word difference and exact new metadata; compare every other byte.
                for(int i=0;i<x.Navigation.Length;i++)if(i<13631||i>=13635)Assert.AreEqual(x.Navigation[i],y.Navigation[i],"existingNavbyte "+i);
                CollectionAssert.AreEqual(new byte[]{1,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0},y.Navigation.Skip(x.Navigation.Length).ToArray());
                var oldNav=WorldWire.Unpack(x.Navigation,WorldWire.ReadNavigationSessionState);var newNav=WorldWire.Unpack(y.Navigation,r=>WorldWire.ReadNavigationSessionState(r,NavigationV8Format.MainInstalledLayout));Assert.Zero(oldNav.InstalledExecutionLayoutVersion);Assert.IsEmpty(oldNav.InstalledExecutions);Assert.AreEqual(1,newNav.InstalledExecutionLayoutVersion);Assert.IsEmpty(newNav.InstalledExecutions);Assert.IsEmpty(newNav.InstalledExecutionKeys);CollectionAssert.AreEqual(new[]{0},newNav.InstalledExecutionLayout,"Honest empty legacy-dense reconstruction; oldwire contains no allocationhistory");
            }finally{foreach(var t in new[]{a,b,clone})if(t!=null){t.Stop();t.Domain.DestroyOwnedNavigation();Assert.Zero(t.Domain.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(t.Domain.Navigation.PhysicalHostBudget.HeldBytes);Assert.Zero(t.Domain.Navigation.PhysicalHostBudget.ActiveScopes);}}
        }
        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)]
        public void Legacy3900AiMigrationUsesSuppliedExpansionValuesAndKeepsOtherBindingsStrict(bool changedExpansion,bool changedOther)
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());while(root!=null&&!File.Exists(Path.Combine(root.FullName,"workflow/project.json")))root=root.Parent;Assert.NotNull(root);
            var bytes=File.ReadAllBytes(Path.Combine(root.FullName,"unity/Tests/Fixtures/o6-tech-funding/checkpoint-3900.world"));var original=(byte[])bytes.Clone();
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();var config=((AiTestMap)p.AuthoredMap).Configuration(p,7109,roster);
            var data=AiProfile.Initial.CopyData();
            if(changedExpansion){data.fields.Single(f=>f.path=="economy.remoteExpansionShare").value=.37;data.fields.Single(f=>f.path=="economy.enemyMainProtectedIncomeSites").value=4;}
            if(changedOther)data.fields.Single(f=>f.path=="economy.reserveSeconds").value=20;
            var target=new AiProfile(data);
            Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.ImportLegacyCheckpoint(bytes,config,NavigationV8Format.Unspecified,LegacyTransactionProfileFormat.Original254,target),"Default import must still reject an incompatible AI hash");
            Assert.Throws<ArgumentNullException>(()=>PlayableAuthorityTick.ImportLegacyCheckpoint(bytes,config,NavigationV8Format.Unspecified,LegacyTransactionProfileFormat.Original254,aiFormat:LegacyAiProfileFormat.BeforeExpansionSelection));
            PlayableAuthorityTick Import()=>PlayableAuthorityTick.ImportLegacyCheckpoint(bytes,config,NavigationV8Format.Unspecified,LegacyTransactionProfileFormat.Original254,target,LegacyAiProfileFormat.BeforeExpansionSelection);
            if(changedOther){Assert.Throws<ArgumentException>(()=>Import(),"Migration cannot ignore another changed historical field");CollectionAssert.AreEqual(original,bytes);return;}
            PlayableAuthorityTick a=null,b=null;
            try{
                a=Import();Assert.AreEqual(3900,a.Tick);Assert.AreEqual(176,a.ParticipantView("ai-4").Credits);
                foreach(var owner in a.Scheduler.Owners){var actual=(AiProfile)owner.GetType().GetField("nativeProfile",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);Assert.AreSame(target,actual);Assert.AreEqual(changedExpansion ? .37 : .1,actual.Value("economy.remoteExpansionShare"));Assert.AreEqual(changedExpansion?4:2,actual.Value("economy.enemyMainProtectedIncomeSites"));}
                var migrated=a.CaptureBytes();b=PlayableAuthorityTick.RestoreBytes(migrated,config,target);CollectionAssert.AreEqual(migrated,b.CaptureBytes(),"Migrated current binding restores through the strict current reader");
                CollectionAssert.AreEqual(original,bytes);
            }finally{foreach(var t in new[]{a,b})if(t!=null){t.Stop();t.Domain.DestroyOwnedNavigation();Assert.Zero(t.Domain.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(t.Domain.Navigation.PhysicalHostBudget.HeldBytes);Assert.Zero(t.Domain.Navigation.PhysicalHostBudget.ActiveScopes);}}
        }
        // Fixed before execution: the original3900 state advances at most3600 ticks,
        // to7500, within its original18000 horizon. No bank/AI/world/selection injections.
        [Test] public void ActualNormalCheckpoint_AccumulatesPaysResearchWithExactC9()
        {
            var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;Assert.NotNull(d);
            string folder=Path.Combine(d.FullName,".local/o6-funding-mechanism");Directory.CreateDirectory(folder);
            string fixture=Path.Combine(d.FullName,"unity/Tests/Fixtures/o6-tech-funding/checkpoint-3900-nav-v8.world");var bytes=File.ReadAllBytes(fixture);
            Assert.AreEqual("689431f1a9f57cf971f8ffee84a51f4431978432262e509e0d2f225d3cffcc78",Wire.Hash(bytes));
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();var config=((AiTestMap)p.AuthoredMap).Configuration(p,7109,roster);
            var a=PlayableAuthorityTick.RestoreBytes(bytes,config);CollectionAssert.AreEqual(bytes,a.CaptureBytes(),"current C9 v8 fixture remains exact; no AI/world/RNG/timer/allocator reset");
            var initial=a.ParticipantView("ai-4");Assert.AreEqual(176,initial.Credits);Assert.AreEqual(3900,a.Tick);Assert.AreEqual(500,p.StartingCredits);Assert.IsEmpty(config.ScenarioUnits);Assert.IsEmpty(config.ScenarioBuildings);
            var initialAuthority=a.CaptureBytes();var initialTargeted=a.CaptureDiagnosticBudget(initial.OwnerId);var initialPlural=a.CaptureDiagnosticBudgets().Single(x=>x.OwnerId==initial.OwnerId);
            CollectionAssert.AreEqual(BudgetWire(initialPlural,a.Tick),BudgetWire(initialTargeted,a.Tick),"Initial-state targeted readback is byte-exact with the selected plural result.");
            CollectionAssert.AreEqual(initialAuthority,a.CaptureBytes(),"Initial diagnostic readback must be pure.");
            bool partial=false,sciencePaid=false,researchPaid=false;var stages=new System.Collections.Generic.HashSet<string>();
            bool checkedPeriodicParity=false,checkedCompletionParity=false;
            using(var sink=new StreamWriter(Path.Combine(folder,"facts.jsonl")))using(var host=new UnityHostRouteService())
            {
                for(int t=0;t<3600&&!a.WinnerTeam.HasValue;t++)
                {
                    bool advanced=false;for(int attempt=0;attempt<512;attempt++){if(a.TryAdvance()){advanced=true;break;}host.Service(a,64);}Assert.True(advanced,"actual route barrier "+a.Tick);
                    var v=a.ParticipantView("ai-4");var b=a.CaptureDiagnosticBudget(v.OwnerId);var holds=b.Reserved.Where(AiTechnologyFunding.IsFunding).ToArray();
                    partial|=holds.Any(e=>e.Amount>0&&e.Amount<600);sciencePaid|=b.Paid.Any(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.ScientificCenter);researchPaid|=b.Paid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueResearch);
                    string stage=holds.FirstOrDefault()?.Id??"none";
                    if(a.Tick%150==0||holds.Length>0&&stages.Add(stage)||v.OwnerResearch.Any(r=>r.Complete))
                    {
                        bool completed=v.OwnerResearch.Any(r=>r.Complete);
                        if(!checkedPeriodicParity&&a.Tick%150==0||completed&&!checkedCompletionParity)
                        {
                            var authorityBeforeDiagnostic=a.CaptureBytes();var selected=a.CaptureDiagnosticBudgets().Single(x=>x.OwnerId==v.OwnerId);
                            CollectionAssert.AreEqual(BudgetWire(selected,a.Tick),BudgetWire(b,a.Tick),"Representative sampled/completed state must match plural selected-budget bytes.");
                            CollectionAssert.AreEqual(authorityBeforeDiagnostic,a.CaptureBytes(),"Sampled/completed diagnostic comparison must remain read-only.");
                            if(a.Tick%150==0)checkedPeriodicParity=true;
                            if(completed)checkedCompletionParity=true;
                        }
                        sink.WriteLine(JsonConvert.SerializeObject(new{a.Tick,v.Credits,v.IncomePerSecond,v.SettledIncome,v.OwnerEliminated,b.PaidTotal,Held=holds,Unpaid=b.Unpaid,Research=v.OwnerResearch,Buildings=v.Buildings.Where(x=>x.Owner==v.Owner).Select(x=>new{x.Id,x.Kind,x.Phase,x.SiteId,x.SlotId,x.ParentId}),Units=v.Entities.Where(x=>x.Owner==v.Owner).Select(x=>new{x.Id,x.Kind,x.Health,x.Upgraded})}));sink.Flush();
                        var checkpoint=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(checkpoint,config);CollectionAssert.AreEqual(checkpoint,restored.CaptureBytes(),"current partial/stage C9 exact at "+a.Tick);restored.Stop();File.WriteAllBytes(Path.Combine(folder,"checkpoint-"+a.Tick+".world"),checkpoint);
                        var world=PlayableWorldState.Decode(checkpoint);Assert.GreaterOrEqual(world.AiAuthority.Length,1);
                    }
                    if(v.OwnerResearch.Any(r=>r.Complete))break;
                }
                var final=a.ParticipantView("ai-4");File.WriteAllText(Path.Combine(folder,"result.json"),JsonConvert.SerializeObject(new{a.Tick,Bound=7500,partial,sciencePaid,researchPaid,Research=final.OwnerResearch,final.OwnerEliminated,final.Credits,final.IncomePerSecond}));
                Assert.True(checkedPeriodicParity,"A periodic sample must verify targeted/plural whole-budget byte parity.");
                Assert.True(checkedCompletionParity,"The completed research state must verify targeted/plural whole-budget byte parity.");
                Assert.True(partial,"actual current-money partial accumulation");Assert.True(sciencePaid,"ordinary actual paid science, no gift");Assert.True(researchPaid,"ordinary actual paid research, not merely queued/emitted");Assert.True(final.OwnerResearch.Any(r=>r.Complete),"bounded continuation real blocker: "+JsonConvert.SerializeObject(final.OwnerResearch));
            }
            a.Stop();
        }
    }
}
