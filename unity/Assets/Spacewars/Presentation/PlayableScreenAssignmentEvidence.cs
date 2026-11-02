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
        private IEnumerator CaptureScreenAssignmentEvidence(string output)
        {
            Directory.CreateDirectory(output);while(initializingMenu)yield return null;
            battleHudUnattended=true;localInputFocused=true;
            float entryDeadline=Time.realtimeSinceStartup+15;
            while(nativeMainMenu?.ScreenRoot==null&&Time.realtimeSinceStartup<entryDeadline)yield return null;
            Require(nativeMainMenu?.ScreenRoot!=null,"native main menu ready");
            menuNavigation.UseKeyboardMouse();nativeMainMenu.ScreenRoot.Q<Button>("main-play").Focus();menuNavigation.Activate();
            while((lobbyScreen.style.display.value!=DisplayStyle.Flex||nativeMainMenu.ScreenRoot.style.display.value!=DisplayStyle.None)&&Time.realtimeSinceStartup<entryDeadline)yield return null;
            Require(lobbyScreen.style.display.value==DisplayStyle.Flex&&nativeMainMenu.ScreenRoot.style.display.value==DisplayStyle.None,"ordinary main-play entry hides menu");
            yield return new WaitForSecondsRealtime(1);
            OpenLobbyScreenPicker(lobbySetup.Participants[1],1);yield return Shot("lobby-picker-off");CloseLobbyScreenPicker();
            lobbyScreen.Q<DropdownField>("lobby-map-choice").value=MossHighlandMap.DisplayName;
            lobbySetup.Participants.RemoveRange(3,lobbySetup.Participants.Count-3);
            NativeScreenLayout.Normalize(lobbySetup);
            Require(NativeScreenLayout.Assign(lobbySetup,lobbySetup.Participants[1],1)==null,"second camera");
            Require(NativeScreenLayout.Assign(lobbySetup,lobbySetup.Participants[2],3)==null,"third camera");
            Require(NativeScreenLayout.Assign(lobbySetup,lobbySetup.Participants[0],2)==null,"free upper left");
            RebuildRoster();OpenLobbyScreenPicker(lobbySetup.Participants[2],2);yield return Shot("lobby-corners");CloseLobbyScreenPicker();
            LaunchLobbyMatch();yield return Ready();
            // Prevent physical pointer edge-pan from changing the unattended human screenshot.
            foreach(var seat in localPresentations.Where(s=>!s.BotSeat))seat.input.enabled=false;
            float until=Time.realtimeSinceStartup+20;
            while(runtime.BotCameraFocus("foundry-2")==null&&Time.realtimeSinceStartup<until)yield return null;
            Require(runtime.BotCameraFocus("foundry-2")!=null,"ordinary applied bot activity");
            Require(localPresentations.Count==3&&FreeScreenSlot==0,"assigned three-view layout");
            Require(sharedMapBackdrop!=null&&sharedMapBackdrop.resolvedStyle.backgroundColor.a==1,"opaque unused quadrant");
            foreach(var seat in localPresentations.Where(s=>s.BotSeat))Require(!seat.input.WorldInputEnabled&&!seat.input.CommandInputEnabled,"bot read-only");
            yield return new WaitForSecondsRealtime(2);yield return Shot("mixed-corners");
            var activity=runtime.BotCameraFocus("foundry-2");File.WriteAllText(Path.Combine(output,"activity.txt"),"owner="+activity.OwnerId+" generation="+activity.Generation+" tick="+activity.Tick+" sequence="+activity.Sequence+" position="+activity.Position.X+","+activity.Position.Z);
            ReturnToLobby();until=Time.realtimeSinceStartup+15;while((returningToLobby||runtime!=null)&&Time.realtimeSinceStartup<until)yield return null;Require(runtime==null,"return to lobby");
            foreach(var p in lobbySetup.Participants)p.Human=false;RebuildRoster();
            Require(lobbySetup.Participants.All(p=>p.ScreenSlot==-1),"spectator clears separate screens");yield return Shot("spectator-lobby");
            LaunchLobbyMatch();yield return Ready();SetSpectatorPerspective(PlayableOwner.Enemy);
            yield return new WaitForSecondsRealtime(2);Require(cameraView.rect==new Rect(0,0,1,1)&&localPresentations.Count==1,"spectator full screen");Require(BotCameraLocked,"spectator bot camera");yield return Shot("spectator-bot");
            SetSpectatorPerspective(PlayableOwner.Enemy);yield return new WaitForSecondsRealtime(.5f);Require(!BotCameraLocked,"free overview");yield return Shot("spectator-overview");
            File.WriteAllText(Path.Combine(output,"result.txt"),"PASS: ordinary roster launch, 3 assigned cameras, free top-left map, bot activity/read-only, exclusive spectator selection/deselection");
            Quit();
            IEnumerator Shot(string label){Require(nativeMainMenu.ScreenRoot.style.display.value==DisplayStyle.None,"main menu hidden for "+label);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,label+".png"));yield return new WaitForSecondsRealtime(.3f);Require(File.Exists(Path.Combine(output,label+".png")),"screenshot "+label);}
            IEnumerator Ready(){float end=Time.realtimeSinceStartup+60;while(preparing&&Time.realtimeSinceStartup<end)yield return null;Require(!preparing&&!inLobby&&runtime!=null,"ordinary lobby launch");Require(view!=null&&view.Failure==null,"runtime healthy");}
            void Require(bool condition,string label){if(!condition)throw new InvalidOperationException("Screen assignment QA: "+label);}
        }
        private void StartScreenAssignmentEvidence(string output)
        {
            StartCoroutine(LoadingPreparation.Run(CaptureScreenAssignmentEvidence(output),error=>{Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"result.txt"),"FAIL "+error);Debug.LogException(error);runtime?.RequestStop();Application.Quit(1);}));
        }
    }
}
#endif
