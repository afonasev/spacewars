using System;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Spacewars.Editor
{
    public static class HawkWeaponPreviewProject
    {
        const string ScenePath = "Assets/Spacewars/Content/StyleA/Preview/HawkWeaponPreview.unity";
        static string Evidence => Path.GetFullPath("../docs/hawk-weapon-preview/evidence");
        [MenuItem("Spacewars/Preview/Hawk Weapon/Create Scene and Verify")]
        public static void PrepareAndVerify()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Evidence);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var controller = new GameObject("Hawk weapon laboratory").AddComponent<HawkWeaponPreview>();
            // Save only the controller. Start builds the isolated preview; no temporary render objects are serialized.
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            controller.Initialize();
            bool first = false, pair = false, impact = false;
            for (int frame = 0; frame < 360; frame++)
            {
                controller.Step(1d / 60);
                if (!first && controller.Launches.Count == 1) { Capture(controller, "01-first-launch"); first = true; }
                if (!pair && controller.Launches.Count == 2 && controller.LiveRockets == 2)
                {
                    // Give the second rocket a visible distance from its socket.
                    for (int i = 0; i < 8; i++) controller.Step(1d / 60);
                    Capture(controller, "02-paired-flight"); pair = true;
                }
                if (!impact && controller.Hits > 0) { Capture(controller, "03-impact"); impact = true; }
            }
            Require(first && pair && impact, "Missing visible launch/pair/impact phase");
            Require(controller.Launches.Count >= 4 && controller.Hits >= 4, "Repeated pairs must hit the target");
            var launches = controller.Launches.ToArray();
            for (int i = 0; i + 1 < launches.Length; i += 2)
            {
                Require(launches[i].side == 0 && launches[i+1].side == 1 && launches[i].target == launches[i+1].target, "Pair target/side mismatch");
                Require(Math.Abs(launches[i+1].time-launches[i].time-controller.Profile.shotIntervalSeconds) < 1e-8, "Pair interval mismatch");
                if (i+2 < launches.Length) Require(launches[i+2].time-launches[i+1].time+1e-8 >= controller.Profile.reloadSeconds, "Reload shortened");
            }
            int hits = controller.Hits;
            controller.Initialize(); controller.KillAfterFirst = true;
            for (int i = 0; i < 240; i++) controller.Step(1d/60);
            Require(controller.Launches.Count == 1 && controller.Health == 0 && !controller.Weapon.SecondPending, "Target death must cancel second launch");
            Capture(controller, "04-cancelled-second");
            controller.KillAfterFirst = false; controller.Initialize(); controller.HawkMoving = true;
            controller.Step(2); Require(controller.Launches.Count == 0, "Moving Hawk fired");
            controller.HawkMoving = false; controller.TargetMoving = true;
            for (int i = 0; i < 180 && controller.Launches.Count < 2; i++) controller.Step(1d/60);
            for (int i = 0; i < 8; i++) controller.Step(1d/60);
            Require(controller.Launches.Count >= 2, "Stationary Hawk did not resume");
            Capture(controller, "05-moving-target");
            var airborne = HawkWeaponPreviewProfile.Load(); airborne.targetHeight = 5;
            controller.TargetMoving = false; controller.Initialize(airborne);
            for (int i = 0; i < 150; i++) controller.Step(1d/60);
            Require(controller.Hits == 2, "Airborne target must receive both rockets");
            Capture(controller, "06-airborne-target");
            File.WriteAllText(Path.Combine(Evidence, "scene-witness.json"), JsonUtility.ToJson(new Report {
                result="PASS", unity=Application.unityVersion, shots=launches.Length, hits=hits, launches=launches,
                cancellation="one launch, dead target, no pending second", movement="no shots while moving; resumes after preparation", airborne="both rockets hit target at height 5",
                scope="Isolated Editor preview controller. Not ordinary match, Player or human acceptance."
            }, true));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Debug.Log("HAWK_WEAPON_SCENE_PASS");
        }
        [Serializable] sealed class Report
        {
            public string result, unity, cancellation, movement, airborne, scope;
            public int shots, hits;
            public HawkWeaponPreview.LaunchRecord[] launches;
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Capture(HawkWeaponPreview controller, string name)
        {
            var camera = controller.PreviewCamera;
            var render = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = render; camera.Render(); RenderTexture.active = render;
                image.ReadPixels(new Rect(0,0,1600,1000),0,0); image.Apply();
                File.WriteAllBytes(Path.Combine(Evidence, name+".png"),image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture=null; RenderTexture.active=previous;
                UnityEngine.Object.DestroyImmediate(image); render.Release(); UnityEngine.Object.DestroyImmediate(render);
            }
        }
    }
}
