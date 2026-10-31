#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        // Opt-in driver of the real sandbox, threaded runtime and ordinary renderer.
        // Only arrangement/removal use the sandbox editor. Aim/poses are never injected.
        private IEnumerator CaptureWeaponRestEvidence()
        {
            var facts = new List<string> { "stage,tick,id,kind,moving,target,hull,yaw,pitch,visual_yaw_error_deg,x,z" };
            long sequence = 0;
            float overallEnd = Time.realtimeSinceStartup + 180;
            double Angle(double a) => Math.Atan2(Math.Sin(a), Math.Cos(a));
            PlayableEntitySnapshot Entity(int id) => view?.Entities.FirstOrDefault(e => e.Id == id);
            void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException("Weapon rest: " + message); }
            void RecordPose(string stage, int id)
            {
                var e = Entity(id); var a = world.Actors[id];
                float visual = Vector3.Angle(a.Turret.forward, a.Hull.forward);
                facts.Add(string.Join(",", stage, view.Tick, id, e.Kind, e.Moving, e.TargetId,
                    e.HullHeading.ToString("R", CultureInfo.InvariantCulture), e.TurretHeading.ToString("R", CultureInfo.InvariantCulture),
                    e.WeaponPitch.ToString("R", CultureInfo.InvariantCulture), visual.ToString("R", CultureInfo.InvariantCulture),
                    e.Position.X.ToString("R", CultureInfo.InvariantCulture), e.Position.Z.ToString("R", CultureInfo.InvariantCulture)));
                File.WriteAllLines(Path.Combine(evidence, "weapon-rest-poses.csv"), facts);
            }
            IEnumerator Until(Func<bool> condition, string name, float seconds = 25)
            {
                float end = Mathf.Min(overallEnd, Time.realtimeSinceStartup + seconds);
                while (!condition() && Time.realtimeSinceStartup < end)
                {
                    Check(view?.Failure == null && !runtime.IsStopped, "runtime failed: " + view?.Failure);
                    yield return null;
                }
                Check(condition(), "timeout: " + name + "; tick=" + view?.Tick);
            }
            void Focus(NavPoint point, float zoom = 6)
            {
                var target = world.Point(point) + Vector3.up * .6f;
                cameraView.orthographicSize = zoom;
                cameraView.transform.position = target + new Vector3((float)profile.CameraOffsetX, (float)profile.CameraHeight, (float)profile.CameraOffsetZ);
                cameraView.transform.LookAt(target);
            }
            IEnumerator Frame(string name)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(evidence, name + ".png"));
                yield return new WaitForEndOfFrame(); yield return new WaitForEndOfFrame();
            }
            void Command(PlayableCommandKind kind, int id, NavPoint destination = default)
            {
                var result = runtime.TrySubmit(new PlayableCommand(runtime.Generation, ++sequence, "player-1", kind, new[] { id }, destination));
                Check(result.Accepted, "command rejected: " + kind + " " + result.Status);
            }
            IEnumerator Arrange(PlayableEntityKind kind)
            {
                provingGround.Units.Clear(); provingGround.Buildings.Clear();
                provingGround.Units.Add(new OfflineScenarioUnit(1, kind, new NavPoint(-5, -20)));
                RebuildProvingGround();
                yield return Until(() => !provingGroundBusy && view != null && view.Entities.Count == 1 && view.Entities[0].Kind == kind, "arrangement " + kind, 45);
                sequence = 0;
                provingLeft.style.display = provingRight.style.display = DisplayStyle.None;
                Pause(false);
            }

            Screen.SetResolution(1600, 1000, FullScreenMode.Windowed);
            yield return Until(() => !initializingMenu && startupLoadingHost == null && cameraView.isActiveAndEnabled, "presentation ready", 45);
            bool stationaryOnly = Array.IndexOf(Environment.GetCommandLineArgs(), "-weaponRestStationaryOnly") >= 0;
            foreach (var kind in stationaryOnly ? Array.Empty<PlayableEntityKind>() : new[] { PlayableEntityKind.Shkval, PlayableEntityKind.Tank, PlayableEntityKind.Explorer })
            {
                yield return Arrange(kind);
                int id = view.Entities.Single().Id;
                var start = Entity(id).Position;
                Command(PlayableCommandKind.Move, id, new NavPoint(-5, -4));
                yield return Until(() => Entity(id).Moving && Math.Abs(Entity(id).Position.Z - start.Z) > 2 && Math.Abs(Angle(Entity(id).TurretHeading - Entity(id).HullHeading)) < .015, kind + " moving and aligned");
                Check(Entity(id).TargetId == 0 && view.Projectiles.Count == 0, kind + " movement unexpectedly in combat");
                RecordPose("moving", id); Focus(Entity(id).Position);
                yield return Frame(kind + "-01-moving");
                Check(Vector3.Angle(world.Actors[id].Turret.forward, world.Actors[id].Hull.forward) < 2, kind + " rendered weapon is not forward");
                yield return Until(() => !Entity(id).Moving && Math.Abs(Entity(id).Position.Z + 4) < 1, kind + " arrived");
                Focus(new NavPoint(-2, -2));
                Check(runtime.PlaceProvingGroundUnit(1, PlayableEntityKind.Explorer, new NavPoint(2, 0)), "enemy placement refused");
                yield return Until(() => view.Projectiles.Any(p => p.OwnerId == id), kind + " actual shot", 20);
                Check(Entity(id).TargetId != 0 && Math.Abs(Angle(Entity(id).TurretHeading - Entity(id).HullHeading)) > .2, kind + " combat did not turn weapon");
                RecordPose("combat", id);
                yield return Frame(kind + "-02-combat");
                double departed = Entity(id).TurretHeading;
                long lostAt = view.Tick;
                Check(runtime.RemoveProvingGroundUnits(1, true), "sandbox target removal refused");
                yield return Until(() => !view.Entities.Any(e => e.Owner == PlayableOwner.Enemy), "target destroyed");
                yield return Until(() => Entity(id).TargetId == 0 && Math.Abs(Angle(Entity(id).TurretHeading - Entity(id).HullHeading)) < .00001 &&
                    (kind != PlayableEntityKind.Shkval || Math.Abs(Entity(id).WeaponPitch - ShkvalLaunchGeometry.NeutralPitch) < .00001), kind + " returns to rest", 12);
                Check(view.Tick > lostAt && Math.Abs(Angle(departed - Entity(id).HullHeading)) > .05, kind + " no observed return interval");
                RecordPose("rest", id);
                yield return Frame(kind + "-03-rest");
                Check(Vector3.Angle(world.Actors[id].Turret.forward, world.Actors[id].Hull.forward) < 1, kind + " rendered rest yaw mismatch");
                // Rotate the real view; authoritative and visible yaw must stay on the chassis.
                double yaw = Entity(id).TurretHeading;
                cameraView.transform.RotateAround(world.Point(Entity(id).Position), Vector3.up, 75);
                yield return Frame(kind + "-04-rest-camera-rotated");
                Check(Math.Abs(Angle(Entity(id).TurretHeading - yaw)) < .00001, kind + " camera changed authoritative aim");
                Check(Vector3.Angle(world.Actors[id].Turret.forward, world.Actors[id].Hull.forward) < 1, kind + " camera changed rendered rest");
                RecordPose("rest-camera-rotated", id);
            }

            // Real defensive plot and production turret; a deliberate sandbox scenario
            // places a target nearby because sandbox placement UI normally separates halves.
            foreach (var defense in new[] { PlayableBuildingKind.SentryTurret, PlayableBuildingKind.GaussTower })
            {
            provingGround.Units.Clear(); provingGround.Buildings.Clear();
            var site = profile.AuthoredMap.Sites(profile).First(s => s.Id == 1);
            var pad = site.DefenseSlots(profile).First();
            Check(provingGround.PlaceBuilding(0, defense, pad.Position) == null, "turret placement");
            provingGround.Units.Add(new OfflineScenarioUnit(2, PlayableEntityKind.Tank, new NavPoint(pad.Position.X + 7, pad.Position.Z + 3)));
            RebuildProvingGround();
            yield return Until(() => !provingGroundBusy && view.Buildings.Any(b => b.Kind == defense), "turret arrangement", 45);
            Pause(false);
            yield return Until(() => !view.Paused && view.Tick > 1, "stationary scenario unpaused");
            provingLeft.style.display = provingRight.style.display = DisplayStyle.None;
            var turret = view.Buildings.Single(b => b.Kind == defense);
            int turretId = turret.Id;
            Focus(pad.Position, 6);
            double desired = Math.Atan2(3, 7);
            yield return Until(() => { var aim = view.Buildings.Single(b => b.Id == turretId).Turret; return aim.ShotSequence > 0 && Math.Abs(Angle(aim.Heading - desired)) < .03; }, defense + " fires and aims", 15);
            turret = view.Buildings.Single(b => b.Id == turretId);
            Check(Math.Abs(Angle(turret.Turret.Heading - Math.PI / 2)) > .2, "turret never left resting yaw");
            yield return Frame(defense + "-02-combat");
            Check(runtime.RemoveProvingGroundUnits(1, true), "turret target removal");
            yield return Until(() => { var s = view.Buildings.Single(b => b.Id == turretId).Turret; return s.TargetId == 0 && Math.Abs(Angle(s.Heading - Math.PI / 2)) < .00001 && Math.Abs(s.Pitch) < .00001 && !s.Turning; }, "turret returns", 12);
            yield return Frame(defense + "-03-rest");
            var rest = view.Buildings.Single(b => b.Id == turretId).Turret;
            var rig = world.Actors[turretId].Hull.GetComponent<SentryTurretRig>();
            float renderedError = Vector3.Angle(rig.Forward, Vector3.forward);
            Check(renderedError < 1, defense + " rendered rest differs from original world forward");
            facts.Add("sentry-rest," + view.Tick + "," + turretId + "," + defense + ",false,0,0," + rest.Heading.ToString("R", CultureInfo.InvariantCulture) + "," + rest.Pitch.ToString("R", CultureInfo.InvariantCulture) + "," + renderedError.ToString("R", CultureInfo.InvariantCulture) + "," + pad.Position.X.ToString("R", CultureInfo.InvariantCulture) + "," + pad.Position.Z.ToString("R", CultureInfo.InvariantCulture));
            }
            File.WriteAllLines(Path.Combine(evidence, "weapon-rest-poses.csv"), facts);
            File.WriteAllText(Path.Combine(evidence, "weapon-rest-result.txt"), "PASS real Development Player sandbox; threaded host; seed=7; profile=" + profile.ProfileId + " revision=" + profile.Revision + "; mode=" + (stationaryOnly ? "stationary only" : "all weapons") + "; unit mode checks actual movement, combat projectile, target loss via sandbox removal, rest and camera rotation; Sentry/Gauss: actual shot, settled aim and rendered return. No aim/pose injection; no ordinary AI/device/human acceptance claim.");
            Debug.Log("WEAPON_REST_PLAYER_PASS");
            Quit();
        }
    }
}
#endif
