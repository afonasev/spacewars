using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed class HawkWeaponTests
    {
        [Test] public void PairIsSeparatedAndReloadStartsAfterSecond()
        {
            var w = new HawkWeapon(.8, .18, 2.4);
            Assert.That(w.Advance(.79, true, 7, id => true), Is.Empty);
            var first = w.Advance(.01, true, 7, id => true).Single();
            Assert.That(first.Side, Is.Zero); Assert.That(first.Target, Is.EqualTo(7));
            Assert.That(w.ReloadRemaining, Is.Zero);
            Assert.That(w.Advance(.17, true, 7, id => true), Is.Empty);
            var second = w.Advance(.01, true, 99, id => true).Single();
            Assert.That(second.Side, Is.EqualTo(1)); Assert.That(second.Target, Is.EqualTo(7));
            Assert.That(w.ReloadRemaining, Is.EqualTo(2.4).Within(1e-12));
            Assert.That(w.Advance(2.39, true, 99, id => true), Is.Empty);
            Assert.That(w.Advance(.01, true, 99, id => true).Single().Target, Is.EqualTo(99));
        }
        [Test] public void TimingDoesNotDependOnFramePartition()
        {
            List<double> Simulate(double dt)
            {
                var w = new HawkWeapon(.8, .18, 2.4); var times = new List<double>();
                for (double t = 0; t < 10 - 1e-10; t += dt)
                    foreach (var e in w.Advance(Math.Min(dt, 10 - t), true, 7, id => true)) times.Add(t + e.Offset);
                return times;
            }
            var whole = Simulate(10); var ticks = Simulate(1d / 60);
            Assert.That(whole.Count, Is.EqualTo(8)); Assert.That(ticks.Count, Is.EqualTo(whole.Count));
            for (int i = 0; i < whole.Count; i++) Assert.That(ticks[i], Is.EqualTo(whole[i]).Within(1e-10));
        }
        [Test] public void DeathBetweenLaunchesCancelsWithoutRetarget()
        {
            var w = new HawkWeapon(.8, .18, 2.4); w.Advance(.8, true, 7, id => true);
            Assert.That(w.Advance(.18, true, 99, id => id == 99), Is.Empty);
            Assert.That(w.SecondPending, Is.False); Assert.That(w.ReloadRemaining, Is.EqualTo(2.22).Within(1e-10));
            Assert.That(w.Advance(2.22, true, 99, id => id == 99).Single().Side, Is.Zero);
        }
        [Test] public void MovementResetsPreparationAndCancelsPartialVolley()
        {
            var w = new HawkWeapon(.8, .18, 2.4); w.Advance(.7, true, 1, id => true);
            Assert.That(w.Advance(.2, false, 1, id => true), Is.Empty);
            Assert.That(w.Advance(.7, true, 1, id => true), Is.Empty);
            Assert.That(w.Advance(.1, true, 1, id => true).Count, Is.EqualTo(1));
            Assert.That(w.Advance(.1, false, 1, id => true), Is.Empty);
            Assert.That(w.SecondPending, Is.False); Assert.That(w.StoppedFor, Is.Zero);
            Assert.That(w.ReloadRemaining, Is.EqualTo(2.3).Within(1e-10));
        }
        [Test] public void InvalidTargetNeverFires()
        {
            var w = new HawkWeapon(.8, .18, 2.4);
            Assert.That(w.Advance(20, true, 1, id => false), Is.Empty);
            Assert.That(w.Advance(.1, true, 0, id => true), Is.Empty);
        }
        [Test] public void RocketTurnIsBoundedAndDoesNotChangeTarget()
        {
            var r = new HawkRocketFlight(7, new BallisticPoint(0, 0, 0), new BallisticPoint(0, 0, 1), 10, .3, 5);
            r.Advance(.2, new BallisticPoint(10, 0, 0));
            Assert.That(Math.Acos(r.Direction.Z), Is.EqualTo(.06).Within(1e-10));
            Assert.That(HawkRocketFlight.Length(r.Direction), Is.EqualTo(1).Within(1e-12));
            Assert.That(HawkRocketFlight.Length(r.Position), Is.EqualTo(2).Within(1e-12));
            Assert.That(r.Target, Is.EqualTo(7));
        }
        [Test] public void LostTargetKeepsHeadingAndLifetimeClampsTravel()
        {
            var r = new HawkRocketFlight(7, new BallisticPoint(0, 0, 0), new BallisticPoint(0, 0, 1), 10, .3, .5);
            r.Advance(1, null); Assert.That(r.Position.Z, Is.EqualTo(5)); Assert.That(r.RemainingLife, Is.Zero);
            r.Advance(1, new BallisticPoint(10, 0, 0)); Assert.That(r.Position.Z, Is.EqualTo(5));
        }
        [Test] public void OppositeTargetHasFiniteBoundedTurn()
        {
            var r = new HawkRocketFlight(7, new BallisticPoint(0, 0, 0), new BallisticPoint(0, 0, 1), 10, .3, 5);
            r.Advance(.2, new BallisticPoint(0, 0, -10));
            Assert.That(Math.Acos(r.Direction.Z), Is.EqualTo(.06).Within(1e-10));
        }
        [Test] public void SweptContactCatchesTargetBetweenSamples()
        {
            Assert.That(HawkRocketFlight.Contact(new BallisticPoint(-5,0,0), new BallisticPoint(5,0,0),
                new BallisticPoint(0,0,0), new BallisticPoint(0,0,0), .5, out var hit), Is.True);
            Assert.That(hit, Is.EqualTo(.45).Within(1e-12));
            Assert.That(HawkRocketFlight.Contact(new BallisticPoint(-5,2,0), new BallisticPoint(5,2,0),
                new BallisticPoint(0,0,0), new BallisticPoint(0,0,0), .5, out _), Is.False);
        }
        [Test] public void MovingTargetSweepUsesRelativeMotion()
        {
            Assert.That(HawkRocketFlight.Contact(new BallisticPoint(0,0,0), new BallisticPoint(0,0,0),
                new BallisticPoint(-2,0,0), new BallisticPoint(2,0,0), .5, out var hit), Is.True);
            Assert.That(hit, Is.EqualTo(.375).Within(1e-12));
        }
        [Test] public void ProfileMetadataAndRangesAreComplete()
        {
            var p = HawkWeaponPreviewProfile.Load(); p.Validate();
            var fields = p.Parameters().ToArray();
            Assert.That(fields.Select(f => f.Path).Distinct().Count(), Is.EqualTo(fields.Length));
            Assert.That(fields.Length, Is.EqualTo(typeof(HawkWeaponPreviewProfile).GetFields().Count(f => f.FieldType == typeof(float))));
            foreach (var field in fields)
            {
                Assert.That(field.Step, Is.GreaterThan(0)); Assert.That(field.Description, Is.Not.Empty);
                float before = field.Read(); field.Write(float.NaN); Assert.Throws<ArgumentException>(p.Validate); field.Write(before);
                field.Write(field.Maximum + field.Step); Assert.Throws<ArgumentException>(p.Validate); field.Write(before);
            }
            Assert.That(p.preparationExtraSeconds, Is.GreaterThan(0));
        }
        [Test] public void InvalidInputsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new HawkWeapon(.8, 0, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new HawkWeapon(.8,.18,2).Advance(double.NaN,true,1,_=>true));
            Assert.Throws<ArgumentException>(() => new HawkRocketFlight(1, new BallisticPoint(0,0,0), new BallisticPoint(0,0,0), 10,.3,5));
        }
    }
}
