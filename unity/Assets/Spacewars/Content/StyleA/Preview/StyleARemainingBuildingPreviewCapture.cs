using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Spacewars.StyleA.Preview
{
    // Asset-only diagnostic Player; no Playable gameplay scene or profile scaling.
    public sealed class StyleARemainingBuildingPreviewCapture : MonoBehaviour
    {
        private string output;

        private IEnumerator Start()
        {
            AudioListener.volume = 0;
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-styleARemainingBuildingEvidence") output = args[i + 1];
            if (string.IsNullOrEmpty(output)) yield break;
            Directory.CreateDirectory(output);
            yield return new WaitForSeconds(2f);
            yield return new WaitForEndOfFrame();
            var path = Path.Combine(output, "style-a-outpost-mine-scientific-center-preview.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("STYLE_A_REMAINING_BUILDING_PREVIEW_CAPTURE " + path);
            yield return new WaitForSeconds(2f);
            Application.Quit();
        }

        private void OnGUI()
        {
            var title = new GUIStyle(GUI.skin.label) { fontSize = 25, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = Color.white;
            GUI.Label(new Rect(0, 18, Screen.width, 36), "STYLE A REMAINING BUILDING IMPORT · DIAGNOSTIC", title);
            var label = new GUIStyle(title) { fontSize = 20 };
            var names = new[] { "OUTPOST", "MINE", "SCIENTIFIC CENTER" };
            for (var i = 0; i < names.Length; i++)
                GUI.Label(new Rect(Screen.width * (i + .5f) / 3f - 120, Screen.height - 70, 240, 32), names[i], label);
        }
    }
}
