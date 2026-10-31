using System;
using System.Linq;
using System.IO;
using System.Globalization;
using Spacewars.Simulation;
using Spacewars.Runtime;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private float routePerformanceStarted=-1;
        private bool fourPlayerPerformance,sixPlayerPerformance,eightPlayerPerformance,eightPlayerMovingPerformance,eightPlayerMovingSubmitted;
        private string MotionScenario=>eightPlayerPerformance?"eight-player":"six-player";
        private struct SixPlayerMotionRow
        {
            internal double Elapsed,X,Z,Heading,GoalX,GoalZ;
            internal long Tick,Sequence,Issued;
            internal int Actor,Owner,Kind,Health,Moving,Navigation,Order,Target;

        }
        private struct QaMotionExtra{internal long Tick,Generation;internal int Dropped;internal double CaptureMs;internal QaPerformanceQueueRow Row;}
        private QaMotionExtra[] qaMotionExtra;
        private SixPlayerMotionRow[] sixPlayerMotion;
        private bool sixPlayerMotionFlushed;
        private int sixPlayerMotionCount,sixPlayerMotionDropped,sixPlayerScreenshots;
        private float sixPlayerNextMotion,sixPlayerNextEconomy;
        private void CaptureSixPlayerMotion(float now)
        {
            if(now<sixPlayerNextMotion)return;
            sixPlayerNextMotion=now+.2f;
            var qa=qaMotionExtra!=null?runtime.PerformanceQueueFrame:null;
            foreach(var perspective in spectatorFrame.Players)foreach(var e in perspective.Entities){
                if(e.Owner!=perspective.Owner)continue;
                if(sixPlayerMotionCount>=sixPlayerMotion.Length){sixPlayerMotionDropped++;continue;}
                var o=e.CurrentOrder;var qr=QaPerformanceQueueRow.Unknown;if(qa!=null)qa.TryActor(e.Id,out qr);
                if(qaMotionExtra!=null)qaMotionExtra[sixPlayerMotionCount]=new QaMotionExtra{Row=qr,Tick=qa?.Tick??-1,Generation=qa?.Generation??-1,CaptureMs=qa?.CaptureMilliseconds??double.NaN,Dropped=qa?.Dropped??0};
                sixPlayerMotion[sixPlayerMotionCount++]=new SixPlayerMotionRow{Elapsed=now-started,Tick=view.Tick,
                    Actor=e.Id,Owner=(int)e.Owner,Kind=(int)e.Kind,Health=e.Health,X=e.Position.X,Z=e.Position.Z,Heading=e.HullHeading,
                    Moving=e.Moving?1:0,Navigation=(int)e.NavigationOutcome,Order=o==null?-1:(int)o.Kind,
                    Sequence=o?.CommandSequence??-1,Issued=o?.IssuedTick??-1,GoalX=o?.Destination.X??double.NaN,GoalZ=o?.Destination.Z??double.NaN,Target=e.TargetId};
            }
            if(now<sixPlayerNextEconomy)return;
            sixPlayerNextEconomy=now+5;
            if(spectatorFrame!=null)foreach(var p in spectatorFrame.Players)
                Record(string.Format(CultureInfo.InvariantCulture,MotionScenario+" ECON tick={0} owner={1} credits={2:R} income={3:R} units={4} buildings={5} eliminated={6}",
                    p.Tick,p.OwnerId,p.ExactCredits,p.IncomePerSecond,p.Entities.Count(e=>e.Owner==p.Owner),p.Buildings.Count(b=>b.Owner==p.Owner),p.OwnerEliminated));
        }
        private void FlushSixPlayerMotion()
        {
            if(sixPlayerMotionFlushed)return;
            using(var writer=new StreamWriter(Path.Combine(evidence,MotionScenario+"-motion.csv"))){
                writer.WriteLine("elapsed,tick,actor,owner,kind,health,x,z,heading,moving,navigation,order,sequence,issued_tick,goal_x,goal_z,target"+(qaMotionExtra==null?"":",meta_tick,meta_generation,meta_capture_ms,expected_sequence,pending_sequence,active_sequence,current_sequence,pending_group,active_group,nav_group,nav_member_phase,actual_admission_requests,attempt_request,pending_arrived,pending_failed,active_arrived,active_failed,actor_present,admission_publication,authority_operation,completion_sequence,completion_group,completion_tick,completion_spatial,meta_dropped"));
                for(int i=0;i<sixPlayerMotionCount;i++){var r=sixPlayerMotion[i];writer.Write(string.Format(CultureInfo.InvariantCulture,
                    "{0:R},{1},{2},{3},{4},{5},{6:R},{7:R},{8:R},{9},{10},{11},{12},{13},{14:R},{15:R},{16}",
                    r.Elapsed,r.Tick,r.Actor,r.Owner,r.Kind,r.Health,r.X,r.Z,r.Heading,r.Moving,r.Navigation,r.Order,r.Sequence,r.Issued,r.GoalX,r.GoalZ,r.Target));
                    if(qaMotionExtra!=null){var extra=qaMotionExtra[i];var q=extra.Row;writer.Write(string.Format(CultureInfo.InvariantCulture,",{0},{1},{2:R},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}",extra.Tick,extra.Generation,extra.CaptureMs,q.ExpectedSequence,q.PendingSequence,q.ActiveSequence,q.CurrentSequence,q.PendingGroup,q.ActiveGroup,q.NavGroup,q.NavMemberPhase,q.AdmissionRequests,q.AttemptRequest,q.PendingArrived?1:0,q.PendingFailed?1:0,q.ActiveArrived?1:0,q.ActiveFailed?1:0));writer.WriteLine(string.Format(CultureInfo.InvariantCulture,",{0},{1},{2},{3},{4},{5},{6},{7}",q.ActorPresent?1:0,q.AdmissionPublication,q.AuthorityOperation,q.CompletionSequence,q.CompletionGroup,q.CompletionTick,q.CompletionSpatial?1:0,extra.Dropped));}else writer.WriteLine();}
                writer.WriteLine("# samples="+sixPlayerMotionCount+",dropped="+sixPlayerMotionDropped);
            }
            sixPlayerMotionFlushed=true;
        }
        private int routePerformanceActor,routePerformanceOrder;
        private NavPoint routePerformanceHome,routePerformanceAway;
        private long routePerformanceMotionTick=-1;

        // Opt-in development harness: ordinary visible commands, unchanged match funding
        // and AI. Its wall deadline also closes a stalled Player without human input.
        private void DriveRoutePerformanceEvidence()
        {
            if(!routePerformanceEvidence||quitting||runtime==null||view==null||inLobby||preparing)return;
            if(SplitScreenPerformanceRequested){DriveSplitScreenPerformance();return;}
            float now=Time.realtimeSinceStartup;
            if(routePerformanceStarted<0){
                routePerformanceStarted=now;
                eightPlayerPerformance=Array.IndexOf(Environment.GetCommandLineArgs(),"-eightPlayerPerformanceEvidence")>=0;
                eightPlayerMovingPerformance=Array.IndexOf(Environment.GetCommandLineArgs(),"-eightPlayerMovingPerformanceEvidence")>=0;
                sixPlayerPerformance=Array.IndexOf(Environment.GetCommandLineArgs(),"-sixPlayerPerformanceEvidence")>=0;
                if(eightPlayerPerformance){
                    if(!spectatorMode||view.Participants.Count!=8||runtime.DiagnosticGlobalUnits!=400||!matchSetup.AiTestMap||spectatorRate!=1){Record("eight-player FAIL invalid roster/map/count/speed");Quit();return;}
                    sixPlayerMotion=new SixPlayerMotionRow[262144];if(runtime.PerformanceReadoutsEnabled)qaMotionExtra=new QaMotionExtra[sixPlayerMotion.Length];
                    Record("eight-player START seconds=120 speed=1 units=400 seed="+view.Seed+" map="+view.ActiveProfile.AuthoredMap.Id+" scriptedMovement="+eightPlayerMovingPerformance);
                    cameraView.orthographicSize=(float)profile.ArenaHalfExtent;
                    cameraView.transform.position=new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ)*4;
                    cameraView.farClipPlane=(float)profile.ArenaHalfExtent*12;
                    cameraView.transform.LookAt(Vector3.zero);RememberKeyboardCamera();
                    Record("eight-player CAMERA policy=fixed-full-map-qa profile="+profile.Revision+" orthographic="+cameraView.orthographic+" size="+cameraView.orthographicSize.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" position="+cameraView.transform.position.ToString("F6")+" rotation="+cameraView.transform.rotation.ToString("F6")+" near="+cameraView.nearClipPlane+" far="+cameraView.farClipPlane+" aspect="+cameraView.aspect);
                    routePerformanceActor=view.Entities.FirstOrDefault(e=>e.Kind==PlayableEntityKind.Explorer)?.Id??0;
                }
                else if(sixPlayerPerformance){
                    if(!spectatorMode||view.Participants.Count!=6||view.Participants.Any(p=>p.Control!=OfflineControl.Ai)||!matchSetup.Foundry||spectatorRate!=1){Record("six-player FAIL invalid roster/map/speed");Quit();return;}
                    sixPlayerMotion=new SixPlayerMotionRow[262144];if(runtime.PerformanceReadoutsEnabled)qaMotionExtra=new QaMotionExtra[sixPlayerMotion.Length];
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
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-smallFivePerformanceEvidence")>=0){
                    if(view.Participants.Count!=2||runtime.DiagnosticGlobalUnits!=10||view.Entities.Count(e=>e.Owner==LocalOwner)!=5){Record("small-five FAIL invalid roster/count");Quit();return;}
                    Record("small-five START participants=2 globalUnits=10 ownUnits=5 seed="+view.Seed);
                }
                Record("route-performance START actor="+actor.Id+" seconds=120 seed="+view.Seed);
                }
            }
            // QA-only framing is fixed after normal camera/input work every frame.
            if(eightPlayerPerformance)ResetKeyboardCamera();
            if(Runtime.OriginalGroupDiagnostics.ShouldStop||Runtime.NavigationArrivalDiagnostics.ShouldStop||now-routePerformanceStarted>=(sixPlayerPerformance?600:120)||view.Outcome!=PlayableMatchOutcome.Playing||!string.IsNullOrEmpty(view.Failure)){
                if(Runtime.NavigationArrivalDiagnostics.Active)Record("arrival-diagnostic STOP event="+Runtime.NavigationArrivalDiagnostics.ShouldStop+" dropped="+Runtime.NavigationArrivalDiagnostics.Dropped);
                Record("route-performance END orders="+routePerformanceOrder+" outcome="+view.Outcome+" failure="+view.Failure);
                routePerformanceEvidence=false;
                if(sixPlayerPerformance||eightPlayerPerformance){CaptureSixPlayerMotion(now);Record(MotionScenario+" END rows="+sixPlayerMotionCount+" dropped="+sixPlayerMotionDropped);FlushSixPlayerMotion();}
                Quit();return;
            }
            if(paused){Pause(false);return;}
            if(sixPlayerPerformance||eightPlayerPerformance){
                if(eightPlayerMovingPerformance&&!eightPlayerMovingSubmitted&&now-routePerformanceStarted>=5){
                    eightPlayerMovingSubmitted=true;
                    foreach(var ownerView in spectatorFrame.Players.OrderBy(v=>(int)v.Owner)){
                        var result=runtime.SubmitEightPlayerMovingPerformanceEvidence(ownerView);
                        Record("eight-player SCRIPTED-MOVE owner="+ownerView.OwnerId+" actors="+ownerView.Entities.Count(e=>e.Owner==ownerView.Owner&&e.Health>0)+" admission="+result.Status);
                        if(!result.Accepted){Record("eight-player FAIL scripted command admission "+result.Status);Quit();return;}
                        routePerformanceOrder++;
                    }
                    if(Runtime.NavigationArrivalDiagnostics.Active)Runtime.NavigationArrivalDiagnostics.Arm(runtime.RequestStop);
                    if(Runtime.OriginalGroupDiagnostics.Active)Runtime.OriginalGroupDiagnostics.Arm(runtime.RequestStop);
                }
                CaptureSixPlayerMotion(now);
                if(sixPlayerScreenshots<2&&now-routePerformanceStarted>=(sixPlayerScreenshots==0?6:eightPlayerPerformance?119:590)){
                    Record(MotionScenario+" SCREENSHOT index="+sixPlayerScreenshots);
                    ScreenCapture.CaptureScreenshot(Path.Combine(evidence,MotionScenario+"-"+(sixPlayerScreenshots++)+".png"));
                }
                return;
            }
            if(fourPlayerPerformance){
                if(now-routePerformanceStarted>=90&&spectatorRate!=1){spectatorRate=1;runtime.SetSpectatorSpeed(Runtime.PlayableRuntime.LocalSpectatorId,1);UpdateSpectatorSpeed();Record("four-player SPEED 1");}
                return; // Observe ordinary AI decisions; do not inject human commands.
            }
            if(sixPlayerScreenshots<2&&now-routePerformanceStarted>=(sixPlayerScreenshots==0?6:115)){
                Record("small SCREENSHOT index="+sixPlayerScreenshots);
                ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"small-"+(sixPlayerScreenshots++)+".png"));
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
