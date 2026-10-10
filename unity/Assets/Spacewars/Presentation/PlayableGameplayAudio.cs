using System.Collections.Generic;
using Spacewars.Simulation;
using Spacewars.Runtime;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private GameplayAudioPlayer gameplayAudio;
        private PlayableRuntime audioRuntime;
        private readonly List<GameplayAudioScreen> audioScreens=new List<GameplayAudioScreen>();
        private GameplayAudioPlayer SharedAudio=>(localCoordinator??this).gameplayAudio;
        private void InitializeGameplayAudio()
        {
            if(localCoordinator!=null||gameplayAudio)return;
            gameplayAudio=gameObject.AddComponent<GameplayAudioPlayer>();gameplayAudio.Initialize();
        }
        private void AudioCommand(PlayableCommand command,bool accepted)=>SharedAudio?.Command(command,accepted);
        private void UpdateGameplayAudio()
        {
            if(localCoordinator!=null||!gameplayAudio)return;
            if(!ReferenceEquals(audioRuntime,runtime)){gameplayAudio.ResetAudio();audioRuntime=runtime;}
            bool active=!inLobby&&!preparing&&!returningToLobby&&!restarting&&!quitting&&!paused&&runtime!=null&&!runtime.IsStopped&&runtime.Result==null&&view!=null&&!view.Paused&&string.IsNullOrEmpty(view.Failure);
            audioScreens.Clear();
            if(active)
            {
                if(localPresentations.Count==0)AddAudioScreen(this);
                else foreach(var seat in localPresentations)AddAudioScreen(seat);
            }
            gameplayAudio.Tick(audioScreens,active,Time.unscaledDeltaTime);
            gameplayAudio.PresentationState(view?.Generation??generation,active,paused||view?.Paused==true,runtime?.Result!=null);
        }
        private void AddAudioScreen(PlayableBootstrap seat)
        {
            if(seat.view==null||!seat.cameraView)return;
            var right=seat.cameraView.transform.right;
            audioScreens.Add(new GameplayAudioScreen(seat.view,seat.Ground(seat.SeatScreenCenter),new NavPoint(right.x,right.z),seat.selection));
        }
    }
    public sealed partial class OfflineTwoLocalBootstrap
    {
        private GameplayAudioPlayer gameplayAudio;
        private PlayableRuntime audioRuntime;
        private readonly List<GameplayAudioScreen> audioScreens=new List<GameplayAudioScreen>();
        private void UpdateGameplayAudio()
        {
            if(!gameplayAudio)return;
            if(!ReferenceEquals(audioRuntime,runtime)){gameplayAudio.ResetAudio();audioRuntime=runtime;}
            bool active=session!=null&&!session.Paused&&runtime!=null&&!runtime.IsStopped&&runtime.Result==null;
            audioScreens.Clear();
            if(active)for(int i=0;i<views.Length;i++)
            {
                var v=views[i];if(v?.View==null||!v.Camera)continue;
                var right=v.Camera.transform.right;
                audioScreens.Add(new GameplayAudioScreen(v.View,Ground(i,v.Camera.pixelRect.center),new NavPoint(right.x,right.z),v.Selection));
            }
            gameplayAudio.Tick(audioScreens,active,Time.unscaledDeltaTime);
            gameplayAudio.PresentationState(views.Length>0?views[0]?.View?.Generation??0:0,active,session?.Paused==true,runtime?.Result!=null);
        }
    }
}
