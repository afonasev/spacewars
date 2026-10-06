using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;
namespace Spacewars.Tests.EditMode.BalanceLab
{
    public static class BuildCompatibilityProbe
    {
        public static void Build()
        {
            var target = Environment.GetEnvironmentVariable("U9_BUILD_TARGET") == "Windows" ? BuildTarget.StandaloneWindows64 : BuildTarget.StandaloneOSX;
            var output = Environment.GetEnvironmentVariable("U9_BUILD_OUTPUT");
            if (string.IsNullOrEmpty(output) || !Path.IsPathRooted(output)) throw new BuildFailedException("Injected absolute output required");
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player);
            if (!assemblies.Any(a => a.name == "Spacewars.BalanceLab")) throw new BuildFailedException("Pure Lab assembly absent from Player compilation");
            var marker = Path.GetFullPath(Path.Combine("..", ".local", "u9-build-exclusion-probe.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(marker)); File.WriteAllText(marker, "U9_UNPUBLISHED_HISTORY_MUST_NOT_SHIP");
            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Spacewars/Content/Scenes/Playable.unity" },
                    locationPathName = output, target = target,
                    options = BuildOptions.Development | BuildOptions.DetailedBuildReport
                });
                if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException(report.summary.result.ToString());
                var sources = report.packedAssets.SelectMany(p => p.contents).Select(c => c.sourceAssetPath).Distinct().OrderBy(p => p).ToArray();
                if (sources.Any(p => p.Contains(".local") || p.Contains("normalizer-parity") || p.Contains("canonical-goldens"))) throw new BuildFailedException("Unpublished/test data in Player");
                File.WriteAllLines(output + ".packed-sources.txt", sources);
                Debug.Log("U9_BUILD_COMPATIBILITY_PASS target=" + target + " bytes=" + report.summary.totalSize + " packed=" + sources.Length);
            }
            finally { if (File.Exists(marker)) File.Delete(marker); }
        }
    }
}
