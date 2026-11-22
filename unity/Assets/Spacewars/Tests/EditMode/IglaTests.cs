using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class IglaTests
    {
        private PlayableDomain domain;
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private const PlayableEntityKind Igla=PlayableEntityKind.Igla;
        private const PlayableOwner Player=PlayableOwner.Player;
        private object Call(string method, params object[] args)=>typeof(PlayableDomain).GetMethod(method,Private).Invoke(domain,args);
        private object Row(string table,int id)=>((IDictionary)typeof(PlayableDomain).GetField(table,Private).GetValue(domain))[id];
        private PlayableSnapshot View()=>domain.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19,Player);

        [SetUp] public void Setup()=>domain=new PlayableDomain(PlayableProfile.Default,71,false);
        [TearDown] public void Release(){domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}

        [Test] public void PaidOrderSurvivesLossOfOrdinaryCenter()
        {
            int Build(PlayableBuildingKind kind,int slot)
            {
                Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,slot,kind,1,Player,null));
                Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
                return View().Buildings.Single(b=>b.Kind==kind).Id;
            }
            long sequence=0;
            PlayableCommandStatus Queue(int id)=>domain.Apply(new PlayableCommand(71,++sequence,"player-1",PlayableCommandKind.QueueIgla,new[]{id}),out _);
            Call("AddCredits",Player,10000d);
            int factory=Build(PlayableBuildingKind.Factory,1);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Queue(factory));
            int center=Build(PlayableBuildingKind.ScientificCenter,2);
            Assert.AreEqual(PlayableCommandStatus.Applied,Queue(factory));
            var order=View().Buildings.Single(b=>b.Id==factory).PrivateState.Orders.Single();
            Assert.AreEqual(250,order.PaidCost); Assert.AreEqual(25,order.Duration); Assert.AreEqual(3,order.PopulationCost);
            Call("RemoveBuilding",Row("buildings",center),false);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Queue(factory));
            Call("AdvanceProduction",.1d); Call("AdvanceProduction",25d);
            for(int i=0;i<180&&!View().Entities.Any(e=>e.Kind==Igla);i++)domain.Step(1d/30);
            Assert.AreEqual(90,View().Entities.Single(e=>e.Kind==Igla).Health);
            Assert.AreEqual(1, AiRosterCatalog.Initial.Production(PlayableCommandKind.QueueIgla).supportWeight);
        }

        [TestCase(0, 5, 0, true, false, true)]
        [TestCase(0, 0, 5, true, false, false)]
        [TestCase(5, 0, 0, false, true, false)]
        [TestCase(5, 2, 0, false, true, true)]
        [TestCase(2, 5, 0, true, false, true)]
        [TestCase(3, 3, 0, false, false, true)]
        [TestCase(3, 3, 0, true, true, true)]
        public void ObservedCompositionSelectsPrimaryAndSecondarySupport(int armor, int softTargets, int air,
            bool preferIgla, bool iglaLeads, bool usesShkval)
        {
            var shares = AiDoctrinePolicy.GroundSupportShares(armor, softTargets, air, preferIgla);
            Assert.AreEqual(iglaLeads, shares.sniper > shares.artillery);
            Assert.AreEqual(usesShkval, shares.artillery > 0);
            if (armor >= 2 && softTargets >= 2) Assert.Greater(shares.sniper, 0);
        }

        [Test]
        public void OrderUsesOnlyVisibleArmorAndLiveCoverAfterOutstandingOrders()
        {
            var own = new[] { PlayableEntityKind.Tank, PlayableEntityKind.Explorer, PlayableEntityKind.Cerberus, Igla };
            PlayableAiObservation View(bool visible, bool extraCover)
            {
                var kinds = extraCover ? own.Concat(new[] { PlayableEntityKind.Explorer }) : own;
                var entities = kinds.Select((kind, index) => new PlayableEntitySnapshot(index+1, Player, kind,
                    new NavPoint(-15, index), 100, false, 0, 0, 0)).Append(new PlayableEntitySnapshot(42, PlayableOwner.Enemy,
                    PlayableEntityKind.Tank, new NavPoint(8, 0), 100, false, 0, 0, 0)).ToArray();
                var vision = new PlayableVision(0, 32, 32, 1, 1);
                vision.Refresh(visible ? new[] { new VisionSource(default, 15) } : System.Array.Empty<VisionSource>(),
                    System.Array.Empty<KnownBuilding>());
                var p = PlayableProfile.Default;
                return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId, p.Revision, 71, 1, 1, 1,
                    RuntimeStatus.Running, false, PlayableMatchOutcome.Playing, 500, null, entities,
                    System.Array.Empty<PlayableBuildingSnapshot>(), System.Array.Empty<PlayableProjectileSnapshot>(),
                    new PlayableRuntimeMetrics(0, 0, 0, 0, 0), null, vision: vision.Snapshot()));
            }
            Assert.Null(AiEconomyAdmission.IglaOrderRejection(View(true, true)));
            Assert.NotNull(AiEconomyAdmission.IglaOrderRejection(View(true, true), pendingOrders: 1));
            Assert.NotNull(AiEconomyAdmission.IglaOrderRejection(View(true, false)));
            Assert.NotNull(AiEconomyAdmission.IglaOrderRejection(View(false, true)));
        }

        [Test]
        public void IglaAndShkvalShareTheRowBehindVepr()
        {
            var p = PlayableProfile.Default;
            var kinds = new[] { PlayableEntityKind.Tank, Igla, PlayableEntityKind.Shkval };
            var members = kinds.Select((kind, index) => new NavFormationLayout.Member(index+1, kind,
                default, PlayableUnitRules.Radius(p, kind))).ToArray();
            var offsets = NavFormationLayout.Offsets(members, 5, new NavPoint(1, 0));
            Assert.Greater(offsets[0].X, offsets[1].X);
            Assert.AreEqual(offsets[1].X, offsets[2].X);
        }

        [Test] public void BothSidesCanPlaceIglaWithTheirOwnSystemsUpgrades()
        {
            var setup=new ProvingGroundSetup(PlayableProfile.Default);
            Assert.IsNull(setup.PlaceUnit(0,Igla,new NavPoint(-9,-9)));
            Assert.IsNull(setup.PlaceUnit(1,Igla,new NavPoint(9,-9)));
            setup.SetResearch(0,PlayableResearchKind.ImprovedDefensiveSystems,true);
            setup.SetResearch(0,PlayableResearchKind.ImprovedWeaponSystems,true);
            setup.SetResearch(0,PlayableResearchKind.TankChassis,true);
            Release();domain=new PlayableDomain(setup.Profile,71,setup.Configuration());
            Assert.AreEqual(250,setup.ArmyCost(0)); Assert.AreEqual(250,setup.ArmyCost(1));
            double upgraded=PlayableStatRounding.MaximumHealth(setup.Profile,90*(1+setup.Profile.DefensiveSystemsLevel1BonusPercent/100));
            Assert.AreEqual(upgraded,(double)Call("UnitMaximumHealth",Player,Igla));
            Assert.AreEqual(90,(double)Call("UnitMaximumHealth",PlayableOwner.Enemy,Igla));
            Assert.AreEqual(PlayableStatRounding.Damage(setup.Profile,16*(1+setup.Profile.WeaponSystemsLevel1BonusPercent/100)),
                (double)Call("WeaponDamage",Player,16d));
        }

        [Test] public void IndependentProfileFieldsSurviveWorldRestore()
        {
            var fields=PlayableProfileMetadata.IglaFields;
            Assert.True(fields.All(NativeBalanceFields.LabEditable));
            var data=PlayableProfile.Default.CopyData();
            fields.Single(f=>f.FieldName=="iglaDamage").Write(data,19);
            var profile=PlayableProfile.Create(data);
            Assert.AreEqual(19,profile.IglaDamage);
            Assert.AreEqual(PlayableProfile.Default.TankWeaponDamage,profile.TankWeaponDamage);
            Release();domain=new PlayableDomain(profile,71,false);
            var bytes=domain.CaptureWorldBytes(19,"igla-custom");
            var restored=PlayableDomain.RestoreWorldBytes(bytes,profile,19,"igla-custom");
            Release();domain=restored;
            CollectionAssert.AreEqual(bytes,domain.CaptureWorldBytes(19,"igla-custom"));
        }

        [Test] public void BeamCrossesFriendlyTankAndOnlyHitsChosenGroundTarget()
        {
            int Spawn(PlayableOwner owner,PlayableEntityKind kind,double x)=>(int)Call("SpawnUnit",new NavPoint(x,-20),owner,kind);
            int shooter=Spawn(Player,Igla,-14);
            int ally=Spawn(Player,PlayableEntityKind.Tank,-11);
            int enemy=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Explorer,-8);
            int behind=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-5.4);
            int plane=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,-4);
            var actor=domain.Navigation.Crowd.Units.Single(n=>n.Id==shooter);
            var unit=Row("units",shooter);
            double HP(int id)=>(double)Row("units",id).GetType().GetField("Health").GetValue(Row("units",id));
            double allyHp=HP(ally),enemyHp=HP(enemy),behindHp=HP(behind);
            Assert.False((bool)Call("IglaClear",unit,actor,ally,new NavPoint(-11,-20)));
            Assert.False((bool)Call("IglaClear",unit,actor,plane,new NavPoint(-4,-20)));
            Assert.True((bool)Call("IglaClear",unit,actor,enemy,new NavPoint(-8,-20)));
            unit.GetType().GetField("Target").SetValue(unit,enemy);
            ((IList)typeof(PlayableDomain).GetField("iglaShooters",Private).GetValue(domain)).Add(unit);
            Call("ApplyIglaShots");
            Assert.AreEqual(allyHp,HP(ally)); Assert.AreEqual(behindHp,HP(behind)); Assert.AreEqual(enemyHp-16,HP(enemy));
        }
    }
}
