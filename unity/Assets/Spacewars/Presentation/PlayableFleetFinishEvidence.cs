#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        // Opt-in photograph of the real sandbox, not an ordinary-match/navigation witness.
        private IEnumerator CaptureFleetFinishEvidence()
        {
            IEnumerator Until(Func<bool> ready, string description)
            {
                float end = Time.realtimeSinceStartup + 90;
                while (!ready() && Time.realtimeSinceStartup < end) yield return null;
                if (!ready()) throw new InvalidOperationException("Fleet photograph timeout: " + description);
            }
            yield return Until(() => !initializingMenu && root != null, "menu");
            // Own neutral devices, as in the existing sandbox evidence driver. Do
            // not consume the user's keys or rely on foreground device readiness.
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard.MakeCurrent(); mouse.MakeCurrent();
            lobbyKeyboard = keyboard; lobbyMouse = mouse;
            input.AssignedKeyboard = keyboard; input.AssignedMouse = mouse;
            localInputFocused = true; input.SetFocus(true);
            InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState { position = cameraView.pixelRect.center });
            InputSystem.Update(); input.Poll();
            OpenLaboratory();
            yield return null;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-modelDetailLabEvidence")>=0)yield return EditModelDetailLabEvidence();
            var button = laboratoryPage?.Page.Q<Button>("lab-proving-ground");
            if (button == null) throw new InvalidOperationException("Missing sandbox entry");
            menuNavigation.ActivateElement(button);
            yield return Until(() => provingGround != null && !provingGroundBusy && view != null && view.Tick > 0, "sandbox");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-weaponRestEvidence") >= 0) { yield return CaptureWeaponRestEvidence(); yield break; }
            Pause(false);
            // Authored photograph positions only; never used by shipping gameplay or camera tuning.
            var kinds = new[] { PlayableEntityKind.Tank, PlayableEntityKind.Explorer, PlayableEntityKind.Shkval, PlayableEntityKind.Hawk };
            var points = new[] { new NavPoint(-24, -6), new NavPoint(-18, -6), new NavPoint(-12, -6), new NavPoint(-5, -6) };
            for (int i = 0; i < kinds.Length; i++)
                if (!runtime.PlaceProvingGroundUnit(0, kinds[i], points[i])) throw new InvalidOperationException("Sandbox placement rejected " + kinds[i]);
            yield return Until(() => kinds.All(k => view.Entities.Any(e => e.Owner == PlayableOwner.Player && e.Kind == k && world.Actors.ContainsKey(e.Id))), "four visible actors");
            var entities = kinds.Select(k => view.Entities.Single(e => e.Owner == PlayableOwner.Player && e.Kind == k)).ToArray();
            if (!world.Actors[entities[0].Id].Hull.name.StartsWith("tankHybrid", StringComparison.Ordinal)) throw new InvalidOperationException("Expected current Spearhead");
            runtime.RequestPause(true);
            paused = true;
            input.enabled = false;
            ResetKeyboardCamera();
            provingLeft.style.display = provingRight.style.display = DisplayStyle.None;
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.5f);
            // Stop presentation updates too: rotor pose, camera, fog and lighting stay fixed for A/B.
            enabled = false;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-modelDetailLabEvidence")>=0){yield return CaptureModelDetailLabSelection(world.Actors[entities[0].Id]);yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-modelDetailCalibration")>=0){yield return CaptureModelDetailCalibration(entities.Select(e=>world.Actors[e.Id]).ToArray());yield break;}
            var hawk = world.Actors[entities[3].Id];
            var original = hawk.Hull.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r, r => r.sharedMaterials);
            var baseline = new Dictionary<string, Material>();
            foreach (string role in new[] { "team-primary", "neutral-metal" })
            {
                var mat = Resources.Load<Material>("FleetFinishBaseline/hawk-" + role);
                if (!mat) throw new InvalidOperationException("QA build is missing baseline " + role);
                baseline.Add(role, mat);
            }
            void Materials(bool before)
            {
                foreach (var pair in original)
                    pair.Key.sharedMaterials = pair.Value.Select(m => before ? baseline.FirstOrDefault(b => m.name.EndsWith(b.Key, StringComparison.Ordinal)).Value ?? m : m).ToArray();
            }
            void Focus(NavPoint point, float height, float zoom)
            {
                var target = world.Point(point) + Vector3.up * height;
                cameraView.orthographicSize = zoom;
                cameraView.transform.position = target + new Vector3((float)profile.CameraOffsetX, (float)profile.CameraHeight, (float)profile.CameraOffsetZ);
                cameraView.transform.LookAt(target);
                float fieldCenter = Screen.height - (top.worldBound.yMax + bottom.worldBound.yMin) * .5f;
                float projected = cameraView.WorldToScreenPoint(target).y;
                cameraView.transform.position -= cameraView.transform.up * (fieldCenter - projected) * (2 * zoom / Screen.height);
            }
            IEnumerator Frame(string name)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(evidence, name + ".png"));
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-hawkSelectionEvidence") >= 0)
            {
                for (int state = 0; state < 3; state++)
                {
                    bool selected = state == 1;
                    selection.Clear();
                    if (selected) selection.Add(entities[3].Id);
                    // Exercise the ordinary presentation pass with actual local selection.
                    enabled = true;
                    yield return null; yield return null;
                    enabled = false;
                    Focus(entities[3].Position, (float)profile.HawkClearance / 2, (float)profile.CameraMinZoom);
                    if (hawk.Selection.activeSelf != selected) throw new InvalidOperationException("Hawk selection projection state mismatch");
                    float radius = hawk.Selection.transform.localScale.x;
                    if (Mathf.Abs(radius - (float)(profile.HawkSelectionRadius + profile.SelectionRingPadding + profile.SelectionRingWidth)) > .00001f)
                        throw new InvalidOperationException("Hawk visual radius mismatch");
                    yield return Frame(state == 0 ? "hawk-unselected" : state == 1 ? "hawk-selected" : "hawk-selection-cleared");
                }
                File.WriteAllText(Path.Combine(evidence, "hawk-selection-result.txt"), "PASS real Player sandbox; selected-only projection; radius=" + hawk.Selection.transform.localScale.x + "; physicalRadius=" + profile.HawkRadius + "; unselected/selected/cleared=true; deviceAcceptance=false");
                Debug.Log("HAWK_SELECTION_PLAYER_PASS");
                enabled = true; Quit(); yield break;
            }
            foreach (bool before in new[] { true, false })
            {
                Materials(before);
                string suffix = before ? "before" : "after";
                Focus(new NavPoint(-14.5, -6), 2, (float)profile.CameraOrthoSize);
                yield return Frame("fleet-" + suffix);
                Focus(new NavPoint(-21, -6), .5f, (float)profile.CameraMinZoom);
                yield return Frame("tank-explorer-" + suffix);
                Focus(entities[3].Position, hawk.Root.position.y - world.Point(entities[3].Position).y, (float)profile.CameraMinZoom);
                yield return Frame("hawk-" + suffix);
            }
            var lines = entities.Select(e => e.Kind + " owner=" + e.Owner + " model=" + world.Actors[e.Id].Hull.name + " position=" + e.Position.X + "," + e.Position.Z).ToArray();
            File.WriteAllLines(Path.Combine(evidence, "fleet-actors.txt"), lines);
            File.WriteAllText(Path.Combine(evidence, "fleet-result.txt"), "PASS real Development Player sandbox; seed=" + view.Seed + "; same camera/light/owner/pose; physicalDevices=false; ordinaryMatch=false");
            Debug.Log("FLEET_FINISH_PLAYER_PASS");
            enabled = true;
            Quit();
        }
    }
}
#endif
