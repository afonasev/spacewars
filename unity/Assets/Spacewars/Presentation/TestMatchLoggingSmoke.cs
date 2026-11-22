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
            var store=NativeBalanceStore.ForApplication(PlayableBootstrap.LoadNormalProfile(),
                Path.Combine(TestMatchLogSession.CurrentDirectory,"smoke-balance.json"));
            if(!store.HasActive){UnityEngine.Debug.LogError("TEST_MATCH_LOG_SMOKE_FAIL "+store.Problem);Application.Quit(2);yield break;}
            UnityEngine.Debug.Log("TEST_MATCH_LOG_SMOKE_PROFILE "+store.ActiveIdentity);
            for(int mode=0;mode<2;mode++){
                var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=7};
                setup.InitializeParticipants();
                if(mode==1)foreach(var participant in setup.Participants)participant.Human=false;
                var profile=PlayableBootstrap.ComposeLobbyProfile(store.Active,setup);
                using(var runtime=PlayableRuntime.CreateLobbyMatch(profile,mode+1,setup,aiProfile:store.ActiveAiProfile))
                using(var routes=new UnityHostRouteService()){
                    if(mode==0){var view=runtime.ParticipantView("player-1");var id=view.Entities.First(e=>e.Owner==view.Owner).Id;runtime.TrySubmit(new PlayableCommand(mode+1,1,"player-1",PlayableCommandKind.Hold,new[]{id}));}
                    double until=Time.realtimeSinceStartupAsDouble+20;
                    while(runtime.Latest.Tick<45&&Time.realtimeSinceStartupAsDouble<until){routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
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
