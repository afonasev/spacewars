using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Explicit QA probe only. Default recording itself has no command-line dependency.
    public sealed class TestMatchLoggingSmoke : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"-testMatchLogSmoke")>=0)new GameObject("Test match logging smoke").AddComponent<TestMatchLoggingSmoke>();}
        IEnumerator Start()
        {
            if(!NativeSilentStartup.IsSilent)throw new InvalidOperationException("Automated logging smoke must start muted");
            if(TestMatchLogSession.CurrentDirectory==null){UnityEngine.Debug.LogError("TEST_MATCH_LOG_SMOKE_FAIL startup factory missing");Application.Quit(2);yield break;}
            for(int mode=0;mode<2;mode++){
                using(var runtime=new PlayableRuntime(PlayableProfile.Default,mode+1,7,humanControlledPlayer:mode==0,spectator:mode==1))
                using(var routes=new UnityHostRouteService()){
                    if(mode==0){var id=runtime.Latest.Entities.First(e=>e.Owner==PlayableOwner.Player).Id;runtime.TrySubmit(new PlayableCommand(mode+1,1,"player-1",PlayableCommandKind.Hold,new[]{id}));}
                    double until=Time.realtimeSinceStartupAsDouble+20;
                    while(runtime.Latest.Tick<45&&Time.realtimeSinceStartupAsDouble<until){routes.Service(runtime,64);yield return null;}
                    if(runtime.Latest.Tick<45||runtime.Latest.Failure!=null){UnityEngine.Debug.LogError("TEST_MATCH_LOG_SMOKE_FAIL runtime "+runtime.Latest.Failure);Application.Quit(2);yield break;}
                    runtime.RequestStop();while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                    if(!runtime.IsStopped){UnityEngine.Debug.LogError("TEST_MATCH_LOG_SMOKE_FAIL stop timeout");Application.Quit(2);yield break;}
                }
            }
            try{throw new InvalidOperationException("TEST_MATCH_LOG_EXCEPTION_PROBE");}catch(Exception error){UnityEngine.Debug.LogException(error);}
            var directory=TestMatchLogSession.CurrentDirectory;TestMatchLogSession.PrepareForQuit();
            var matches=Directory.GetDirectories(directory,"match-*");
            bool valid=matches.Length==2&&matches.All(p=>File.Exists(Path.Combine(p,"summary.json"))&&(string)JObject.Parse(File.ReadAllText(Path.Combine(p,"summary.json")))["Status"]=="complete");
            UnityEngine.Debug.Log((valid?"TEST_MATCH_LOG_SMOKE_PASS ":"TEST_MATCH_LOG_SMOKE_FAIL ")+directory);Application.Quit(valid?0:2);
        }
    }
}
