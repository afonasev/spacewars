using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        [UnityTest] public IEnumerator ProductionBoostMouseUsesOwnRefineryAndShowsLightning() => ProductionBoostInteraction(false);
        [UnityTest] public IEnumerator ProductionBoostPadUsesOwnRefineryAndShowsLightning() => ProductionBoostInteraction(true);

        private IEnumerator ProductionBoostInteraction(bool pad)
        {
            IEnumerator ServicePreparation()
            {
                yield return null;
                while (app != null && Get<bool>(app, "preparing")) { Call(app, "UpdateFrame"); yield return null; }
            }

            yield return StartOrdinary(2, configureRoster: roster =>
            {
                app.StartCoroutine(ServicePreparation());
                if (!pad) roster.Participants[0].DeviceId = 0;
            }, mapName: "Переправа (2)");
            var seat = Seats[0];
            Runtime.RequestPause(true);
            yield return Wait(() => Runtime.Latest.Paused, "arrange production boost");
            var domain = typeof(PlayableRuntime).GetField("domain", F).GetValue(Runtime);
            object Domain(string method, params object[] args) => domain.GetType().GetMethod(method, F).Invoke(domain, args);
            var initial = Get<PlayableSnapshot>(seat, "view");
            var owner = initial.Owner;
            var enemy = Get<PlayableSnapshot>(Seats[1], "view").Owner;
            var home = initial.Buildings.Single(b => b.Owner == owner && b.Kind == PlayableBuildingKind.Headquarters);
            var profile = Get<PlayableProfile>(seat, "profile");
            var data = profile.CopyData();
            var balance = Get<NativeBalanceStore>(app, "balanceStore");
            Assert.AreEqual(NativeBalanceStore.DefaultId, balance.SelectedId);
            Assert.AreEqual(balance.SelectedVersion, profile.Revision);
            TestContext.WriteLine($"Balance {balance.ActiveIdentity}; profile {profile.ProfileId}@{profile.Revision}, seed {initial.Seed}; income ×{data.orbitalBoostIncomeMultiplier}, production ×{data.orbitalBoostProductionMultiplier}, {data.orbitalBoostSeconds} seconds.");
            Domain("AddCredits", owner, 10000d);
            Assert.AreEqual(PlayableCommandStatus.Applied, Domain("BuildAt", home.SiteId, 1, PlayableBuildingKind.Refinery, home.Id, owner, null));
            Domain("AdvanceFoundations");
            Domain("AdvanceBuildings", 100d);
            var arranged = (PlayableSnapshot)Domain("PlayerSnapshot", 1L, RuntimeStatus.Running, true, new PlayableRuntimeMetrics(0,0,0,0,0), null, 7, owner);
            var refinery = arranged.Buildings.Single(b => b.Owner == owner && b.Kind == PlayableBuildingKind.Refinery);
            Domain("CreditOrbital", (PlayableOwner?)owner, enemy, data.orbitalThreshold1 + data.orbitalThreshold2 + data.orbitalThreshold3);
            foreach (int id in new[] { 1, 2, OrbitalAbilities.AcceleratedProduction })
                Assert.AreEqual(PlayableCommandStatus.Applied, Domain("ChooseOrbital", owner, id));

            Runtime.RequestPause(false);
            yield return Wait(() => !Runtime.Latest.Paused && !Get<bool>(seat, "paused") &&
                Get<PlayableSnapshot>(seat, "view").Orbital.Abilities.Any(a => a.Id == OrbitalAbilities.AcceleratedProduction), "boost becomes available");
            Call(seat, "FocusScanPoint", refinery.Position);
            if (pad)
            {
                Assert.True((bool)Call(seat, "OpenOrbital", true));
                Call(seat, "SubmitOrbital", false, OrbitalAbilities.AcceleratedProduction);
                PadCursor(seat, refinery.Position);
                Call(seat, "PollScanTargetPad", pads[0]);
                Press(pads[0].buttonSouth);
                InputSystem.Update();
                Call(seat, "PollScanTargetPad", pads[0]);
                Release(pads[0].buttonSouth);
                InputSystem.Update();
            }
            else
            {
                Call(seat, "ApplyOrbitalFromHud", OrbitalAbilities.AcceleratedProduction);
                var at = (Vector2)Get<Camera>(seat, "cameraView").WorldToScreenPoint(Get<PlayableWorld>(seat, "world").Point(refinery.Position));
                Set(Mouse.current.position, at);
                InputSystem.Update();
                Assert.True((bool)Call(seat, "ScanPointer", at, true, true, false));
            }

            var root = Get<VisualElement>(seat, "root");
            yield return Wait(() => Get<PlayableSnapshot>(seat, "view").Buildings.Any(b => b.Id == refinery.Id &&
                b.BoostRemaining > 0 && b.Health < refinery.Health) && root.Q("production-boost-lightning") != null, "threaded boost and lightning");
            Runtime.RequestPause(true);
            yield return Wait(() => Runtime.Latest.Paused, "capture active lightning");
            yield return Capture("production-boost-" + (pad ? "pad" : "mouse"),
                context: () => { Call(seat, "FocusScanPoint", refinery.Position); Call(seat, "UpdateLifecycleMarkers"); },
                captureWidth:1600, captureHeight:1000,
                captureChecks: () => Assert.AreEqual(DisplayStyle.Flex, root.Q("production-boost-lightning").style.display.value));
            Runtime.RequestStop();
            yield return Wait(() => Runtime.IsStopped, "stop production boost witness");
        }
    }
}
