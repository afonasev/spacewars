using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Spacewars.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Spacewars.Editor
{
    // Every unspecified build is test by default; production must be selected explicitly.
    public sealed class TestMatchLoggingBuild : IPreprocessBuildWithReport
    {
        // Minimal QA route: verifies the same non-Development format used by test installers.
        public static void BuildNonDevelopmentSmoke()
        {
            Directory.CreateDirectory("Builds/test-log-channel");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{PlayableProject.ScenePath},locationPathName="Builds/test-log-channel/Spacewars-TestLogs.app",target=BuildTarget.StandaloneOSX,options=BuildOptions.DetailedBuildReport});
            if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(report.summary.result.ToString());
            UnityEngine.Debug.Log("TEST_LOG_CHANNEL_BUILD_PASS");
        }
        public int callbackOrder=>-1000;
        public void OnPreprocessBuild(BuildReport report)
        {
            bool development=(report.summary.options&BuildOptions.Development)!=0;
            string channel=Environment.GetEnvironmentVariable("SPACEWARS_RELEASE_CHANNEL");
            bool test=TestMatchLoggingProfile.IsTestBuild(development,channel);
            var resource=Resources.Load<TextAsset>("TestMatchLoggingProfile");
            if(!resource)throw new BuildFailedException("Test match logging resource missing");
            var profile=JsonUtility.FromJson<TestMatchLoggingProfile>(resource.text);profile.Validate();
            if(test&&!profile.enabled)throw new BuildFailedException("Test match logging must be enabled in test builds");
            var root=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
            string Run(string arguments){using(var p=Process.Start(new ProcessStartInfo("git","-C \""+root+"\" "+arguments){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true})){var value=p.StandardOutput.ReadToEnd();var err=p.StandardError.ReadToEnd();p.WaitForExit();if(p.ExitCode!=0)throw new BuildFailedException("Build provenance: "+err);return value.TrimEnd();}}
            var diff=Run("diff HEAD --binary");var status=Run("status --porcelain");
            string digest;using(var hash=SHA256.Create())digest=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(diff))).Replace("-","").ToLowerInvariant();
            var path="Assets/StreamingAssets/TestBuildIdentity.json";Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,JsonConvert.SerializeObject(new{Schema=1,SourceRevision=Run("rev-parse HEAD"),Dirty=status.Length>0,TrackedDiffSha256=digest,GitStatus=status,BuiltUtc=DateTime.UtcNow,Platform=report.summary.platform.ToString(),Development=development,TestBuild=test,Channel=channel??"test",LoggingProfile=test?profile.id:null},Formatting.Indented));
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
