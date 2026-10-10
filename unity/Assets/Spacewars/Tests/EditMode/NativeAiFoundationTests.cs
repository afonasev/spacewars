using System;
using System.Linq;
using System.IO;
using System.Collections;
using System.Globalization;
using Spacewars.Simulation.Ai;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiFoundationTests
    {
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private static object Create(PlayableProfile profile)=>Activator.CreateInstance(Domain,Flags,null,new object[]{profile,71L,false},null);
        private static PlayableSnapshot View(object domain,PlayableOwner owner)=>(PlayableSnapshot)Domain.GetMethod("PlayerSnapshot",Flags).Invoke(domain,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,owner});
        private static PlayableCommandStatus Apply(object domain,PlayableCommand command)
        {object[] args={command,null};return (PlayableCommandStatus)Domain.GetMethod("Apply",Flags).Invoke(domain,args);}

        [TestCase(false)][TestCase(true)] public void EqualGenesis(bool authoredMap)
        {
            var profile=authoredMap?PlayableProfile.ThreeCrossingsDefault:PlayableProfile.Default;
            var domain=Create(profile);
            foreach(var owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy})
            {
                var view=View(domain,owner);var units=view.Entities.Where(e=>e.Owner==owner).ToArray();
                Assert.AreEqual(1,units.Length);Assert.AreEqual(PlayableEntityKind.Explorer,units[0].Kind);
                var buildings=view.Buildings.Where(b=>b.Owner==owner).ToArray();Assert.AreEqual(1,buildings.Length);
                Assert.AreEqual(PlayableBuildingKind.Headquarters,buildings[0].Kind);Assert.AreEqual(1,buildings[0].Progress);
                Assert.AreEqual(profile.StartingCredits,view.Credits);Assert.AreEqual(profile.ExplorerPopulationCost,view.Population.Living);
            }
        }

        [TestCase(false,false)][TestCase(false,true)][TestCase(true,false)][TestCase(true,true)]
        public void EqualGenesisWithPermutedStartsAndControls(bool aiDuel,bool swap)
        {
            var baseline=OfflineParticipantAuthorityTests.Config(2,false);
            var roster=baseline.Roster.Select((p,i)=>new OfflineParticipant(p.Id,swap?2-i:i+1,p.Team,i==0&&!aiDuel?OfflineControl.Human:OfflineControl.Ai)).ToArray();
            var costs=new double[baseline.Starts.Count,baseline.Starts.Count];
            for(int i=0;i<baseline.Starts.Count;i++)for(int j=0;j<baseline.Starts.Count;j++)costs[i,j]=baseline.RouteCost(i,j);
            var config=new OfflineMatchConfiguration(baseline.Profile,baseline.SourceIdentity,baseline.MapIdentity,baseline.RouteProvenance,baseline.Seed,roster,baseline.Starts.ToArray(),baseline.Sites.ToArray(),baseline.Obstacles.ToArray(),costs);
            var authority=new OfflineParticipantAuthority(config,71);
            for(int i=0;i<roster.Length;i++)
            {
                var view=authority.View(roster[i].Id);var owner=(PlayableOwner)i;
                Assert.AreEqual(1,view.Entities.Count(e=>e.Owner==owner));Assert.AreEqual(PlayableEntityKind.Explorer,view.Entities.Single(e=>e.Owner==owner).Kind);
                Assert.AreEqual(PlayableBuildingKind.Headquarters,view.Buildings.Single(e=>e.Owner==owner).Kind);
                Assert.AreEqual(config.Starts[config.Assignments[i]].Position,view.Buildings.Single(e=>e.Owner==owner).Position);
                Assert.AreEqual(config.Profile.StartingCredits,view.Credits);
            }
        }

        [TestCase(PlayableOrderOrigin.Ai)][TestCase(PlayableOrderOrigin.Human)] public void SharedAdmission(PlayableOrderOrigin origin)
        {
            var data=PlayableProfile.Default.CopyData();data.startingCredits=1;var profile=PlayableProfile.Create(data);
            var domain=Create(profile);var before=View(domain,PlayableOwner.Player);
            var unit=before.Entities.Single(e=>e.Owner==PlayableOwner.Player);
            PlayableCommand Command(long sequence,PlayableCommandKind kind,int[] ids)=>new PlayableCommand(71,sequence,"enemy-1",kind,ids,origin:origin,source:origin==PlayableOrderOrigin.Ai?"foundation-test":null,jobId:origin==PlayableOrderOrigin.Ai?1:0,actionId:origin==PlayableOrderOrigin.Ai?sequence:0);
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Apply(domain,Command(1,PlayableCommandKind.Hold,new[]{unit.Id})));
            Assert.AreEqual(PlayableCommandStatus.InsufficientCredits,Apply(domain,Command(2,PlayableCommandKind.BuildFactory,Array.Empty<int>())));
            Assert.AreEqual(before.Credits,View(domain,PlayableOwner.Player).Credits);Assert.AreEqual(1,View(domain,PlayableOwner.Enemy).Credits);
            Assert.False(View(domain,PlayableOwner.Player).Entities.Single(e=>e.Id==unit.Id).Held);
            Assert.AreEqual(1,View(domain,PlayableOwner.Enemy).Buildings.Count(b=>b.Owner==PlayableOwner.Enemy));
        }

        private static OfflineMatchConfiguration IdentityFixture()
        {
            var baseline=OfflineParticipantAuthorityTests.Config(3,false);
            var names=new[]{"owner-a","owner-z","owner-17"};
            var roster=names.Select((name,i)=>new OfflineParticipant(name,i+1,i<2?3:7,OfflineControl.Ai)).ToArray();
            var starts=baseline.Starts.Select(x=>new OfflineStart(x.Id,x.SiteId+40,x.Position,x.ExplorerAnchor,x.Heading,x.PinnedLogicalPlayer)).Reverse().ToArray();
            var sites=baseline.Sites.Select(x=>new TerritorySite(x.Id+40,x.Kind,x.Position,x.Slots.ToArray())).ToArray();
            var costs=new double[starts.Length,starts.Length];
            for(int i=0;i<starts.Length;i++)for(int j=0;j<starts.Length;j++)costs[i,j]=baseline.RouteCost(starts.Length-1-i,starts.Length-1-j);
            return new OfflineMatchConfiguration(baseline.Profile,baseline.SourceIdentity,baseline.MapIdentity,baseline.RouteProvenance,baseline.Seed,roster,starts,sites,baseline.Obstacles.ToArray(),costs);
        }

        [Test] public void OwnerPermutation()
        {
            var config=IdentityFixture();var authority=new OfflineParticipantAuthority(config,71);
            for(int i=0;i<config.Roster.Count;i++)
            {
                var observation=PlayableAiObservation.From(authority.View(config.Roster[i].Id));
                Assert.AreEqual(config.Starts[config.Assignments[i]].SiteId,observation.HomeSiteId);Assert.Greater(observation.HomeSiteId,40);
                var policy=new PlayableAiEconomicLivenessPolicy(ownerId:observation.OwnerId);var action=policy.TryPlan(observation);
                Assert.NotNull(action);Assert.AreEqual(observation.OwnerId,action.PlayerId);Assert.AreEqual(observation.HomeSiteId,action.SiteId);
                var command=new PlayableCommand(71,1,action.PlayerId,action.Kind,Array.Empty<int>(),siteId:action.SiteId,slotId:action.SlotId,parentId:action.ParentId,buildingKind:action.BuildingKind,origin:PlayableOrderOrigin.Ai,source:action.SourceIdentity,jobId:1,actionId:1);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(command).Status);
                Assert.AreEqual((PlayableOwner)i,authority.View(action.PlayerId).Buildings.Single(b=>b.SiteId==action.SiteId&&b.Kind==PlayableBuildingKind.Factory).Owner);
            }
        }

        [Test] public void AllyNotEnemy()
        {
            var config=IdentityFixture();var authority=new OfflineParticipantAuthority(config,71);var view=authority.View("owner-a");
            var own=view.Entities.Single(e=>e.Owner==PlayableOwner.Player);
            var snapshot=new PlayableSnapshot(view.ProfileId,view.ProfileRevision,view.Generation,view.Seed,view.Sequence,view.Tick,view.Status,false,view.Outcome,view.Credits,view.Geometry,
                new[]{own,new PlayableEntitySnapshot(901,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-10,0),100,false,0,0,0),new PlayableEntitySnapshot(902,PlayableOwner.Third,PlayableEntityKind.Tank,new NavPoint(-9,0),100,false,0,0,0)},
                view.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),view.Metrics,null,sites:view.Sites.ToArray(),owner:view.Owner,ownerId:view.OwnerId,team:view.Team,participants:config.Roster.ToArray(),homeSiteId:view.HomeSiteId);
            var observation=PlayableAiObservation.From(snapshot);Assert.False(observation.IsHostile(PlayableOwner.Enemy));Assert.True(observation.IsHostile(PlayableOwner.Third));
            var readiness=new PlayableAiResearchReadinessTracker().Observe(observation,new PlayableAiResearchStrategicIntent(71,0,false,1));
            Assert.AreEqual(1,readiness.EnemyForce,"Readiness counts the third owner but excludes the allied second slot.");
            var method=typeof(PlayableAiMissionDefensePolicy).GetMethod("VisibleEnemies",BindingFlags.Static|BindingFlags.NonPublic);
            var targets=(System.Collections.Generic.IEnumerable<PlayableEntitySnapshot>)method.Invoke(null,new object[]{observation});CollectionAssert.AreEqual(new[]{902},targets.Select(x=>x.Id));
            Assert.False(view.PublicScoutObjectives.Any(x=>x.SiteId==config.Starts[config.Assignments[1]].SiteId));
        }

        [Test] public void IndependentControl()
        {
            var config=IdentityFixture();var authority=new OfflineParticipantAuthority(config,71);
            var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop",true);
            var loops=config.Roster.Select(p=>Activator.CreateInstance(type,Flags,null,new object[]{config.Profile,PlayableAiOpeningComposition.Initialize(config.Seed,p.Id),71L,null},null)).ToArray();
            PlayableAiOwnerCheckpoint State(int i)=>(PlayableAiOwnerCheckpoint)type.GetProperty("Checkpoint",Flags).GetValue(loops[i]);
            void Deliver(int i,long human)=>type.GetMethod("Deliver",Flags).Invoke(loops[i],new object[]{OfflineParticipantAuthorityTests.Domain(authority),human,false});
            void Review(int i)=>type.GetMethod("Review",Flags).Invoke(loops[i],new object[]{authority.View(config.Roster[i].Id),0L});
            // OwnerLoop first reviews at its existing 45-tick decision cadence.
            OfflineParticipantAuthorityTests.Step(authority,45);
            for(int i=0;i<loops.Length;i++){Review(i);Assert.Greater(State(i).PendingActionId,0);}
            var unaffected=State(1).PendingActionId;Deliver(0,1);Assert.Zero(State(0).PendingActionId);Assert.AreEqual(unaffected,State(1).PendingActionId);
            int hq=authority.View("owner-a").Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters).Id;
            OfflineParticipantAuthorityTests.Call(authority,"Damage",hq,10000);OfflineParticipantAuthorityTests.Step(authority,60);
            Deliver(0,1);Review(0);Assert.Zero(State(0).PendingActionId);
            Deliver(1,0);Deliver(2,0);
            Assert.True(State(1).Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Applied));Assert.True(State(2).Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Applied));
        }

        // Canonical encoding of every public DTO property/field, including nested collections.
        // Do not compare only Identity: it deliberately excludes gameplay facts.
        private static byte[] ProjectionBytes(object value)
        {
            using(var stream=new MemoryStream())
            {
                using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))Encode(w,value);
                return stream.ToArray();
            }
        }
        private static void Encode(BinaryWriter w,object value)
        {
            w.Write(value!=null);if(value==null)return;var type=value.GetType();w.Write(type.FullName);
            if(value is double number){w.Write(number);return;}
            if(value is float single){w.Write(single);return;}
            if(type.IsPrimitive||type.IsEnum||value is string||value is decimal)
            {w.Write(Convert.ToString(value,CultureInfo.InvariantCulture));return;}
            if(value is IEnumerable sequence){var items=sequence.Cast<object>().ToArray();w.Write(items.Length);foreach(var item in items)Encode(w,item);return;}
            foreach(var p in type.GetProperties(BindingFlags.Public|BindingFlags.Instance).Where(p=>p.GetIndexParameters().Length==0).OrderBy(p=>p.Name))
            {w.Write(p.Name);Encode(w,p.GetValue(value));}
            foreach(var f in type.GetFields(BindingFlags.Public|BindingFlags.Instance).OrderBy(f=>f.Name))
            {w.Write(f.Name);Encode(w,f.GetValue(value));}
        }
        private static object[] Values(object domain,string name)
        {
            var registry=Domain.GetField(name,Flags).GetValue(domain);
            return ((IEnumerable)registry.GetType().GetProperty("Values").GetValue(registry)).Cast<object>().ToArray();
        }
        private static byte[] LoopBytes(object loop)
        {
            using(var stream=new MemoryStream())
            {using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))loop.GetType().GetMethod("WriteState",Flags).Invoke(loop,new object[]{w});return stream.ToArray();}
        }
        [TestCase(AiDifficulty.Recruit,false)][TestCase(AiDifficulty.Fighter,false)][TestCase(AiDifficulty.Veteran,false)]
        [TestCase(AiDifficulty.Recruit,true)][TestCase(AiDifficulty.Fighter,true)][TestCase(AiDifficulty.Veteran,true)]
        public void HiddenCounterfactual(AiDifficulty difficulty,bool enemyObserver)
        {
            var profile=PlayableProfile.Default;var a=Create(profile);var b=Create(profile);
            int cadence=(int)AiProfile.SecondsToTicks(AiProfile.Initial.DifficultyValue(difficulty,"decisionSeconds"),30);
            for(int i=0;i<cadence;i++){Domain.GetMethod("Step",Flags).Invoke(a,new object[]{1d/30});Domain.GetMethod("Step",Flags).Invoke(b,new object[]{1d/30});}
            var observer=enemyObserver?PlayableOwner.Enemy:PlayableOwner.Player;var hidden=enemyObserver?PlayableOwner.Player:PlayableOwner.Enemy;
            var before=PlayableAiObservation.From(View(a,observer));var hiddenView=View(b,hidden);
            Assert.False(View(b,observer).Entities.Any(e=>e.Owner==hidden),"Fixture must actually hide the enemy.");
            Domain.GetField(enemyObserver?"credits":"enemyCredits",Flags).SetValue(b,98765d);
            foreach(var u in Values(b,"units").Where(u=>(PlayableOwner)u.GetType().GetField("Owner").GetValue(u)==hidden))u.GetType().GetField("Health").SetValue(u,17);
            foreach(var h in Values(b,"buildings").Where(h=>(PlayableOwner)h.GetType().GetField("Owner").GetValue(h)==hidden))h.GetType().GetField("Health").SetValue(h,123d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Apply(b,new PlayableCommand(71,1,hiddenView.OwnerId,PlayableCommandKind.BuildFactory,Array.Empty<int>())));
            var nav=(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(b);var vision=View(b,observer).Vision;
            var placement=(from x in Enumerable.Range(-28,57) from z in Enumerable.Range(-28,57)
                let point=new NavPoint(x,z) where !vision.IsVisible(point)&&nav.Crowd.CanPlace(point,PlayableUnitRules.Radius(profile,PlayableEntityKind.Tank)) select point).First();
            Domain.GetMethod("SpawnUnit",Flags).Invoke(b,new object[]{placement,hidden,PlayableEntityKind.Tank});
            var after=PlayableAiObservation.From(View(b,observer));
            Assert.AreNotEqual(ProjectionBytes(PlayableAiObservation.From(hiddenView)),ProjectionBytes(PlayableAiObservation.From(View(b,hidden))),"Positive control: hidden owner's facts changed.");
            CollectionAssert.AreEqual(ProjectionBytes(before),ProjectionBytes(after),"Every allowed owner fact must be byte-equivalent.");
            var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop",true);
            object Loop()=>Activator.CreateInstance(type,Flags,null,new object[]{profile,PlayableAiOpeningComposition.Initialize(before.Seed,before.OwnerId),71L,null,AiProfile.Initial,difficulty},null);
            var first=Loop();var second=Loop();CollectionAssert.AreEqual(LoopBytes(first),LoopBytes(second));
            type.GetMethod("Review",Flags).Invoke(first,new object[]{View(a,observer),0L});type.GetMethod("Review",Flags).Invoke(second,new object[]{View(b,observer),0L});
            var checkpoint=(PlayableAiOwnerCheckpoint)type.GetProperty("Checkpoint",Flags).GetValue(first);
            Assert.Greater(checkpoint.PendingActionId,0,"A real plan must be selected.");
            CollectionAssert.AreEqual(LoopBytes(first),LoopBytes(second),"Plans, RNG/memory, pending claims and receipts cannot depend on hidden truth.");
        }
        [Test] public void DetachedOwnerProjectionIsolation()
        {
            var config=IdentityFixture();var authority=new OfflineParticipantAuthority(config,71);
            var saved=config.Roster.Select(p=>PlayableAiObservation.From(authority.View(p.Id))).ToArray();var bytes=saved.Select(ProjectionBytes).ToArray();
            var owner=config.Roster[1].Id;var view=authority.View(owner);var explorer=view.Entities.Single(e=>e.Owner==view.Owner);
            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(71,1,owner,PlayableCommandKind.BuildFactory,Array.Empty<int>())).Status);
            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(71,2,owner,PlayableCommandKind.Hold,new[]{explorer.Id})).Status);
            OfflineParticipantAuthorityTests.Step(authority,600);
            var factory=authority.View(owner).Buildings.Single(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Factory);
            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(71,3,owner,PlayableCommandKind.QueueTank,new[]{factory.Id})).Status);
            var now=PlayableAiObservation.From(authority.View(owner));Assert.AreEqual(1,now.Buildings.Single(b=>b.Id==factory.Id).PrivateState.Orders.Count);
            Assert.AreNotEqual(bytes[1],ProjectionBytes(now),"Positive control: live private queue changed.");
            for(int i=0;i<saved.Length;i++)
            {
                CollectionAssert.AreEqual(bytes[i],ProjectionBytes(saved[i]),"Published DTO must remain detached for "+config.Roster[i].Id);
                var other=PlayableAiObservation.From(authority.View(config.Roster[i].Id));
                Assert.True(other.Buildings.Where(b=>b.Owner!=other.Owner).All(b=>b.PrivateState==null&&b.ExactHealth==null));
                Assert.True(other.Entities.Where(e=>e.Owner!=other.Owner).All(e=>e.CurrentOrder==null&&e.OrderStamp==null));
                Assert.AreEqual(config.Roster[i].Id,other.OwnerId);Assert.AreEqual(config.Roster[i].Team,other.Team);
            }
            Assert.False(typeof(PlayableAiObservation).GetProperties().Any(p=>p.PropertyType==Domain||typeof(Delegate).IsAssignableFrom(p.PropertyType)||p.Name=="AiAuthority"));
        }

        [Test] public void ExplicitCombatFixturesKeepAuthoredUnits()
        {
            var view=View(NativeCombatFixture.WithTwoEnemyDefenders(PlayableProfile.Default,71,false),PlayableOwner.Enemy);
            Assert.AreEqual(2,view.Entities.Count(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Tank));
        }
    }
}
