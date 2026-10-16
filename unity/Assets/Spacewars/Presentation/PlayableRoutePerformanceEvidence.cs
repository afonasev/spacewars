using System;
using System.Linq;
using System.Globalization;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private float routePerformanceStarted=-1;
        private bool fourPlayerPerformance;
        private int routePerformanceActor,routePerformanceOrder;
        private NavPoint routePerformanceHome,routePerformanceAway;
        private long routePerformanceMotionTick=-1;

        // Opt-in development harness: ordinary visible commands, unchanged match funding
        // and AI. Its wall deadline also closes a stalled Player without human input.
        private void DriveRoutePerformanceEvidence()
        {
            if(!routePerformanceEvidence||quitting||runtime==null||view==null||inLobby||preparing)return;
            float now=Time.realtimeSinceStartup;
            if(routePerformanceStarted<0){
                routePerformanceStarted=now;
                fourPlayerPerformance=Array.IndexOf(Environment.GetCommandLineArgs(),"-fourPlayerPerformanceEvidence")>=0;
                if(fourPlayerPerformance){
                    if(!spectatorMode||view.Participants.Count!=4){Record("four-player FAIL invalid roster");Quit();return;}
                    spectatorRate=4;runtime.SetSpectatorSpeed(Runtime.PlayableRuntime.LocalSpectatorId,4);UpdateSpectatorSpeed();
                    Record("four-player SPEED 4 seed="+view.Seed);
                }
                var actor=view.Entities.FirstOrDefault(e=>e.Owner==LocalOwner&&e.Kind==PlayableEntityKind.Explorer&&e.Health>0);
                if(actor==null){Record("route-performance FAIL no own explorer");Quit();return;}
                routePerformanceActor=actor.Id;routePerformanceHome=actor.Position;
                var goal=view.PublicScoutObjectives.Where(o=>o.Reachable&&o.SiteId!=view.HomeSiteId&&!o.Approach.Equals(actor.Position))
                    .OrderBy(o=>Math.Pow(o.Approach.X-actor.Position.X,2)+Math.Pow(o.Approach.Z-actor.Position.Z,2)).FirstOrDefault();
                if(goal==null){Record("route-performance FAIL no public goal");Quit();return;}
                routePerformanceAway=goal.Approach;
                Record("route-performance START actor="+actor.Id+" seconds=120 seed="+view.Seed);
            }
            if(now-routePerformanceStarted>=120||view.Outcome!=PlayableMatchOutcome.Playing||!string.IsNullOrEmpty(view.Failure)){
                Record("route-performance END orders="+routePerformanceOrder+" outcome="+view.Outcome+" failure="+view.Failure);
                routePerformanceEvidence=false;Quit();return;
            }
            if(paused){Pause(false);return;}
            if(fourPlayerPerformance){
                if(now-routePerformanceStarted>=90&&spectatorRate!=1){spectatorRate=1;runtime.SetSpectatorSpeed(Runtime.PlayableRuntime.LocalSpectatorId,1);UpdateSpectatorSpeed();Record("four-player SPEED 1");}
                return; // Observe ordinary AI decisions; do not inject human commands.
            }
            var current=view.Entities.FirstOrDefault(e=>e.Id==routePerformanceActor&&e.Owner==LocalOwner);
            if(current==null)return; // Normal combat may kill the actor; keep measuring the world.
            if(now-routePerformanceStarted>=routePerformanceOrder*15){
                Submit(PlayableCommandKind.Move,new[]{current.Id},routePerformanceOrder%2==0?routePerformanceAway:routePerformanceHome);
                routePerformanceOrder++;
            }
            if(view.Tick-routePerformanceMotionTick>=3){
                routePerformanceMotionTick=view.Tick;
                Record("route-performance MOTION actor="+current.Id+" x="+current.Position.X.ToString("R",CultureInfo.InvariantCulture)+
                    " z="+current.Position.Z.ToString("R",CultureInfo.InvariantCulture)+" moving="+current.Moving+" navigation="+current.NavigationOutcome);
            }
        }
#endif
    }
}
