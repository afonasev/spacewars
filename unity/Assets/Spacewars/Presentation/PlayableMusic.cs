using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private AdaptiveMusicPlayer music;
        private readonly PlayableMusicObservation musicObservation=new PlayableMusicObservation();
        private readonly List<PlayableSnapshot> musicScreens=new List<PlayableSnapshot>();
        private long musicGeneration=long.MinValue;

        private void InitializeMusic()
        {
            if(localCoordinator!=null||music)return;
            music=gameObject.AddComponent<AdaptiveMusicPlayer>();music.Initialize();
        }

        private void UpdateMusic()
        {
            if(localCoordinator!=null||!music)return;
            bool match=!inLobby&&!preparing&&!returningToLobby&&!restarting&&!quitting&&
                runtime!=null&&!runtime.IsStopped&&runtime.Result==null&&view!=null&&string.IsNullOrEmpty(view.Failure);
            int events=0;
            if(match)
            {
                if(musicGeneration!=view.Generation){musicObservation.Reset();musicGeneration=view.Generation;}
                musicScreens.Clear();
                if(matchSetup?.Participants!=null&&!matchSetup.Spectator)
                {
                    // The same immutable frame used to display the human screens. No AI view.
                    foreach(var seat in localPresentations)if(seat.view!=null)musicScreens.Add(seat.view);
                    if(musicScreens.Count==0)musicScreens.Add(view);
                }
                else musicScreens.Add(view); // Spectator's existing full presentation is allowed.
                if(!paused&&!view.Paused)events=musicObservation.Observe(musicScreens);
            }
            music.Tick(match?GameMusicMode.Match:GameMusicMode.Menu,view?.Generation??0,view?.Seed??0,
                events,match&&(paused||view.Paused),Time.unscaledDeltaTime);
        }
    }
}
