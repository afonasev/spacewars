#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using Spacewars.Runtime;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private string qaDeadlinePath;
        private PlayerQaDeadline qaDeadline;
        private bool qaFinishing,qaFailed,qaLateCaptured,qaQ10Requested;
        private string qaStopReason;
        private void InitializeQaDeadline()
        {
            var args=Environment.GetCommandLineArgs();qaQ10Requested=Array.IndexOf(args,"-q10Readouts")>=0;
            if(Array.IndexOf(args,"-routePerformanceEvidence")<0)return;
            int index=Array.IndexOf(args,"-qaDeadlineFile");
            if(index<0||index+1>=args.Length)return;
            qaDeadlinePath=args[index+1];
            index=Array.IndexOf(args,"-playableEvidence");
            if(index<0||index+1>=args.Length)throw new ArgumentException("QA deadline requires evidence directory.");
            evidence=args[index+1];Directory.CreateDirectory(evidence);
        }
        private void BeginQaFinish(string reason,bool failed=false)
        {
            qaFailed|=failed||!string.IsNullOrEmpty(view?.Failure);
            if(qaFinishing)return;
            qaFinishing=true;qaStopReason=reason;quitting=true;routePerformanceEvidence=false;
            StopAllCoroutines();runtime?.RequestStop();Record("QA STOP "+reason);
            try{
                if(sixPlayerMotion!=null){if(view!=null)CaptureSixPlayerMotion(Time.realtimeSinceStartup);FlushSixPlayerMotion();}
                Flush();
            }catch(Exception error){qaFailed=true;qaStopReason+="; evidence flush "+error;}
            // No later producer samples: Update returns before RecordRouteFrame.
            // Wait across frames for the actual IO consumer to write its final footer.
            routeFrames?.Dispose();
        }
        private void FlushPerformanceReceipts()
        {
            if(runtime==null){if(qaQ10Requested){qaFailed=true;qaStopReason+="; Q10 runtime/readouts absent";}return;}var rows=runtime.CopyPerformanceReceipts(out int expected,out int dropped);
            if(expected==0&&!qaQ10Requested)return;
            using(var writer=new StreamWriter(Path.Combine(evidence,"eight-player-receipts.csv"))){
                writer.WriteLine(QaPerformanceReceipts.CsvHeader);
                foreach(var row in rows)writer.WriteLine(QaPerformanceReceipts.CsvRow(row));
                writer.WriteLine("# rows="+rows.Length+",dropped="+dropped+",expected="+expected);
            }
            if(qaQ10Requested&&!runtime.WritePerformanceNavigationSteps(Path.Combine(evidence,"eight-player-navigation-steps.csv"))){qaFailed=true;qaStopReason+="; navigation step readout missing";}
            var queues=runtime.PerformanceQueueFrame;
            if(queues==null){qaFailed=true;qaStopReason+="; group readout missing";}else{queues.WriteCsv(Path.Combine(evidence,"eight-player-group-final.csv"));if(queues.Dropped>0||queues.Count!=400){qaFailed=true;qaStopReason+="; group readout incomplete";}}
            if(dropped>0||expected!=8){qaFailed=true;qaStopReason+="; scripted receipt lane incomplete";}
        }
        private bool ServiceQaCompletion()
        {
            if(qaDeadlinePath==null)return false;
            try{
                if(qaDeadline==null&&File.Exists(qaDeadlinePath))qaDeadline=PlayerQaDeadline.Parse(File.ReadAllText(qaDeadlinePath));
                long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if(!qaFinishing&&qaDeadline!=null){
                    if(!qaLateCaptured&&routePerformanceStarted>=0&&qaDeadline.ShouldCapture(now)){
                        qaLateCaptured=true;Record("QA SCREENSHOT late");
                        ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"qa-late.png"));
                    }
                    if(qaDeadline.ShouldFinish(now))BeginQaFinish(routePerformanceStarted<0?"FAIL entry did not reach match":"admitted lifetime cutoff",routePerformanceStarted<0);
                }
            }catch(Exception error){BeginQaFinish("FAIL QA clock "+error,true);}
            if(!qaFinishing)return false;
            bool authorityStopped=runtime==null||runtime.IsStopped;
            bool workerStopped=authorityStopped&&(routeService==null||routeService.StopDiagnosticWorker());
            if(!PlayerQaDeadline.ReadyToQuit(authorityStopped,workerStopped,routeFrames))return true;
            if(routeFrames?.Failure!=null){qaFailed=true;qaStopReason+="; frame sink "+routeFrames.Failure;}
            if((routeFrames?.Dropped??0)>0){qaFailed=true;qaStopReason+="; frame samples dropped";}
            if(Runtime.OriginalGroupDiagnostics.Active){Runtime.OriginalGroupDiagnostics.Save(Path.Combine(evidence,"original-group-disposition.bin"));Runtime.OriginalGroupDiagnostics.Stop();}
            if(Runtime.NavigationArrivalDiagnostics.Active){Runtime.NavigationArrivalDiagnostics.Save(Path.Combine(evidence,"arrival-diagnostic.bin"));Runtime.NavigationArrivalDiagnostics.Stop();}
            Flush();FlushPerformanceReceipts();
            if(QaAiRouteAudit.WriteCsv(Path.Combine(evidence,"ai-route-review-audit.csv"))>0){qaFailed=true;qaStopReason+="; AI audit owner overflow";}
            if(QaArrivalLifecycle.WriteCsv(Path.Combine(evidence,"arrival-lifecycle.csv"))>0){qaFailed=true;qaStopReason+="; arrival lifecycle incomplete";}
            File.WriteAllText(Path.Combine(evidence,"qa-completion.txt"),(qaFailed?"FAIL":"COMPLETE")+"\n"+qaStopReason+"\nframe_dropped="+(routeFrames?.Dropped??0)+"\n");
            Application.Quit(qaFailed?1:0);return true;
        }
    }
}
#endif
