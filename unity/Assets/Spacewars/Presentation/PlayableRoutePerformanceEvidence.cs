using System;
using System.Linq;
using System.IO;
using System.Globalization;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private float routePerformanceStarted=-1;
        private bool fourPlayerPerformance,sixPlayerPerformance;
        private struct SixPlayerMotionRow
        {
            internal double Elapsed,X,Z,Heading,GoalX,GoalZ;
            internal long Tick,Sequence,Issued;
            internal int Actor,Owner,Kind,Health,Moving,Navigation,Order,Target;
        }
        private SixPlayerMotionRow[] sixPlayerMotion;
        private int sixPlayerMotionCount,sixPlayerMotionDropped,sixPlayerScreenshots;
        private float sixPlayerNextMotion,sixPlayerNextEconomy;
        private void CaptureSixPlayerMotion(float now)
        {
            if(now<sixPlayerNextMotion)return;
            sixPlayerNextMotion=now+.2f;
            foreach(var perspective in spectatorFrame.Players)foreach(var e in perspective.Entities){
                if(e.Owner!=perspective.Owner)continue;
                if(sixPlayerMotionCount>=sixPlayerMotion.Length){sixPlayerMotionDropped++;continue;}
                var o=e.CurrentOrder;
                sixPlayerMotion[sixPlayerMotionCount++]=new SixPlayerMotionRow{Elapsed=now-started,Tick=view.Tick,
                    Actor=e.Id,Owner=(int)e.Owner,Kind=(int)e.Kind,Health=e.Health,X=e.Position.X,Z=e.Position.Z,Heading=e.HullHeading,
                    Moving=e.Moving?1:0,Navigation=(int)e.NavigationOutcome,Order=o==null?-1:(int)o.Kind,
                    Sequence=o?.CommandSequence??-1,Issued=o?.IssuedTick??-1,GoalX=o?.Destination.X??double.NaN,GoalZ=o?.Destination.Z??double.NaN,Target=e.TargetId};
            }
            if(now<sixPlayerNextEconomy)return;
            sixPlayerNextEconomy=now+5;
            if(spectatorFrame!=null)foreach(var p in spectatorFrame.Players)
                Record(string.Format(CultureInfo.InvariantCulture,"six-player ECON tick={0} owner={1} credits={2:R} income={3:R} units={4} buildings={5} eliminated={6}",
                    p.Tick,p.OwnerId,p.ExactCredits,p.IncomePerSecond,p.Entities.Count(e=>e.Owner==p.Owner),p.Buildings.Count(b=>b.Owner==p.Owner),p.OwnerEliminated));
        }
        private void FlushSixPlayerMotion()
        {
            using(var writer=new StreamWriter(Path.Combine(evidence,"six-player-motion.csv"))){
                writer.WriteLine("elapsed,tick,actor,owner,kind,health,x,z,heading,moving,navigation,order,sequence,issued_tick,goal_x,goal_z,target");
                for(int i=0;i<sixPlayerMotionCount;i++){var r=sixPlayerMotion[i];writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:R},{1},{2},{3},{4},{5},{6:R},{7:R},{8:R},{9},{10},{11},{12},{13},{14:R},{15:R},{16}",
                    r.Elapsed,r.Tick,r.Actor,r.Owner,r.Kind,r.Health,r.X,r.Z,r.Heading,r.Moving,r.Navigation,r.Order,r.Sequence,r.Issued,r.GoalX,r.GoalZ,r.Target));}
                writer.WriteLine("# samples="+sixPlayerMotionCount+",dropped="+sixPlayerMotionDropped);
            }
        }
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
                sixPlayerPerformance=Array.IndexOf(Environment.GetCommandLineArgs(),"-sixPlayerPerformanceEvidence")>=0;
                if(sixPlayerPerformance){
                    if(!spectatorMode||view.Participants.Count!=6||view.Participants.Any(p=>p.Control!=OfflineControl.Ai)||!matchSetup.Foundry||spectatorRate!=1){Record("six-player FAIL invalid roster/map/speed");Quit();return;}
                    sixPlayerMotion=new SixPlayerMotionRow[262144];
                    Record("six-player START seconds=600 speed=1 seed="+view.Seed+" map="+view.ActiveProfile.AuthoredMap.Id);
                    for(int i=0;i<view.Participants.Count;i++){var p=view.Participants[i];Record("six-player ROSTER slot="+i+" id="+p.Id+" team="+p.Team+" control="+p.Control+" difficulty="+matchSetup.Participants[i].Difficulty);}
                    routePerformanceActor=view.Entities.FirstOrDefault(e=>e.Kind==PlayableEntityKind.Explorer)?.Id??0;
                }
                else{
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
            }
            if(now-routePerformanceStarted>=(sixPlayerPerformance?600:120)||view.Outcome!=PlayableMatchOutcome.Playing||!string.IsNullOrEmpty(view.Failure)){
                Record("route-performance END orders="+routePerformanceOrder+" outcome="+view.Outcome+" failure="+view.Failure);
                routePerformanceEvidence=false;
                if(sixPlayerPerformance){CaptureSixPlayerMotion(now);Record("six-player END rows="+sixPlayerMotionCount+" dropped="+sixPlayerMotionDropped);FlushSixPlayerMotion();}
                Quit();return;
            }
            if(paused){Pause(false);return;}
            if(sixPlayerPerformance){
                CaptureSixPlayerMotion(now);
                if(sixPlayerScreenshots<2&&now-routePerformanceStarted>=(sixPlayerScreenshots==0?60:590)){
                    Record("six-player SCREENSHOT index="+sixPlayerScreenshots);
                    ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"six-player-"+(sixPlayerScreenshots++)+".png"));
                }
                return;
            }
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
