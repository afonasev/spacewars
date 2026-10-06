#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private IEnumerator CaptureLobbyEvidence()
        {
            Directory.CreateDirectory(lobbyEvidence);
            yield return new WaitForSecondsRealtime(2);
            ScreenCapture.CaptureScreenshot(Path.Combine(lobbyEvidence,"01-main.png"));
            yield return new WaitForSecondsRealtime(.5f);
            ShowLobby();
            yield return new WaitForSecondsRealtime(1);
            if(runtime!=null||input.WorldInputEnabled)throw new InvalidOperationException("Lobby started authority/input.");
            ScreenCapture.CaptureScreenshot(Path.Combine(lobbyEvidence,"02-lobby.png"));
            yield return new WaitForSecondsRealtime(.5f);
            lobbyScreen.Q<TextField>("lobby-human-name").value="Командир";
            lobbyScreen.Q<TextField>("lobby-ai-name").value="ИИ";
            lobbySetup.HumanColor=4;lobbySetup.AiColor=4;RefreshLobbyValidation();
            if(launchButton.enabledSelf)throw new InvalidOperationException("Duplicate color accepted.");
            ScreenCapture.CaptureScreenshot(Path.Combine(lobbyEvidence,"03-invalid.png"));
            yield return new WaitForSecondsRealtime(.5f);
            lobbySetup.AiColor=3;lobbySetup.HumanTeam=3;lobbySetup.AiTeam=7;RebuildRoster();RefreshLobbyPreview();RefreshLobbyValidation();
            ScreenCapture.CaptureScreenshot(Path.Combine(lobbyEvidence,"04-configured.png"));
            yield return new WaitForSecondsRealtime(.5f);
            LaunchLobbyMatch();
            float deadline=Time.realtimeSinceStartup+20;
            while(preparing&&Time.realtimeSinceStartup<deadline)yield return null;
            if(preparing||inLobby||runtime==null)throw new InvalidOperationException("Lobby failed to start.");
            yield return new WaitForSecondsRealtime(3);
            if(runtime.LobbyConfiguration.HumanColor!=4||runtime.LobbyConfiguration.HumanTeam!=3||runtime.LobbyConfiguration.MatchHumanName!="Командир"||view.Tick<=0||view.Failure!=null)throw new InvalidOperationException("Match setup was not bound.");
            ScreenCapture.CaptureScreenshot(Path.Combine(lobbyEvidence,"05-match.png"));
            yield return new WaitForSecondsRealtime(.5f);
            Pause(true);yield return new WaitForSecondsRealtime(.5f);
            long tick=runtime.Latest.Tick;yield return new WaitForSecondsRealtime(.5f);
            if(runtime.Latest.Tick!=tick)throw new InvalidOperationException("Pause advanced ticks.");
            ScreenCapture.CaptureScreenshot(Path.Combine(lobbyEvidence,"06-pause.png"));
            yield return new WaitForSecondsRealtime(.5f);
            Restart();yield return new WaitForSecondsRealtime(2);
            if(runtime.Generation!=2||runtime.LobbyConfiguration.HumanColor!=4)throw new InvalidOperationException("Restart lost setup.");
            ReturnToLobby();yield return new WaitForSecondsRealtime(1);
            if(!inLobby||runtime!=null||lobbySetup.HumanName!="Командир")throw new InvalidOperationException("Return lost lobby state.");
            ScreenCapture.CaptureScreenshot(Path.Combine(lobbyEvidence,"07-return.png"));
            yield return new WaitForSecondsRealtime(.5f);
            LaunchLobbyMatch();yield return new WaitForSecondsRealtime(3);
            if(inLobby||runtime.Generation!=3)throw new InvalidOperationException("Relaunch failed.");
            File.WriteAllText(Path.Combine(lobbyEvidence,"smoke.json"),"{\"result\":\"passed\",\"scope\":\"Development Player auto route: menu/lobby/invalid colors/configure/launch/paused tick/restart/return/relaunch\",\"seed\":19092026,\"generation\":3,\"physical_device_acceptance\":false,\"natural_outcome_tested\":false}");
            Debug.Log("LOBBY_PLAYER_SMOKE_PASS");Quit();
        }
    }
}
#endif
