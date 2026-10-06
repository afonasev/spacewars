using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Spacewars.StyleA.Preview
{
    // Dedicated diagnostic Player only. It never enters the Playable gameplay scene.
    public sealed class StyleAUnitPreviewCapture : MonoBehaviour
    {
        private string output;
        private bool captured;

        private IEnumerator Start()
        {
            AudioListener.volume = 0;
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-styleAUnitEvidence") output = args[i + 1];
            if (string.IsNullOrEmpty(output)) yield break;
            Directory.CreateDirectory(output);
            yield return new WaitForSeconds(2f);
            yield return new WaitForEndOfFrame();
            var path = Path.Combine(output, "style-a-tank-explorer-shkval-preview.png");
            ScreenCapture.CaptureScreenshot(path);
            captured = true;
            Debug.Log("STYLE_A_UNIT_PREVIEW_CAPTURE " + path);
            yield return new WaitForSeconds(2f);
            Application.Quit();
        }

        private void OnGUI()
        {
            var title = new GUIStyle(GUI.skin.label) { fontSize = 25, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = Color.white;
            GUI.Label(new Rect(0, 18, Screen.width, 36), "STYLE A UNIT IMPORT · DIAGNOSTIC", title);
            var label = new GUIStyle(title) { fontSize = 20 };
            var names = new[] { "TANK", "EXPLORER", "SHKVAL" };
            for (var i = 0; i < names.Length; i++)
                GUI.Label(new Rect(Screen.width * (i + .5f) / 3f - 110, Screen.height - 70, 220, 32), names[i], label);
            if (captured) GUI.Label(new Rect(0, Screen.height - 34, Screen.width, 28), "PNG saved · diagnostic asset preview", label);
        }
    }
}
