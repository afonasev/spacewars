using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
public sealed class CampaignWestMotionHostDiagnosticTests
{
    [UnityTest] public IEnumerator ResumeActualWestScoutCheckpointAndObserveMovement()=>Observe("9df50d2c599235ec930c457fe3a52910f2fa58546de49a5a753035a03b67b62e");
    [UnityTest] public IEnumerator ResumeActualForkCheckpointAndObserveMovement()=>Observe("8ae2ba2b216f6280b45fc88b3fb189d0cfd67972bdc9b916dfe8b78d9807a27f");
    private IEnumerator Observe(string expectedHash)
    {
        string source=Environment.GetEnvironmentVariable("CAMPAIGN_WEST_BODY"),folder=Environment.GetEnvironmentVariable("CAMPAIGN_WEST_OUTPUT");Assert.IsNotEmpty(source);Assert.IsNotEmpty(folder);Directory.CreateDirectory(folder);
        var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());var config=CampaignReconOpening.Configuration(p,false);var bytes=File.ReadAllBytes(source);
        using(var h=System.Security.Cryptography.SHA256.Create())Assert.AreEqual(expectedHash,BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant());
        var r=PlayableRuntime.RestoreBytes(bytes,config);var routes=new UnityHostRouteService();var log=new StringBuilder();long first=-1,last=-1;double start=Time.realtimeSinceStartupAsDouble;
        try{
            while(Time.realtimeSinceStartupAsDouble-start<32){routes.Service(r,p.NavigationRequestsPerFrame);Assert.False(r.IsStopped,r.Latest?.Failure);var s=r.Latest;
                if(s!=null&&s.Tick!=last){last=s.Tick;if(first<0)first=last;if((last-first)%15==0){const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;var port=r.NavigationBinding?.RoutePort;var session=port==null?null:(NavigationSession)typeof(NavigationRoutePort).GetField("session",flags).GetValue(port);object Read(string name)=>session==null?null:typeof(NavigationSession).GetProperty(name,flags).GetValue(session);
                    log.AppendLine("path="+Read("LastNavigationStepPath")+" trafficStage="+Read("LastTrafficDispatchStage")+" namesFailure="+Read("LastAnswerNamesFailure")+" resultFailure="+Read("LastResultBatchFailure")+" batchPublications="+Read("OwnedResultBatchPublications")+" discardPublications="+Read("OwnedDiscardPublications")+" answers="+session?.Answers.Count+" tick="+last+" pending="+s.Metrics.NavigationPending+" outcome="+s.Outcome+" actors="+string.Join(";",s.Entities.Where(e=>e.Owner==PlayableOwner.Player).Select(e=>e.Id+":"+e.Position.X+","+e.Position.Z+":"+e.NavigationOutcome))+" authority="+r.NavigationBinding?.RoutePort?.ArrivalAuthoritySnapshot+" worker="+routes.CertifiedPublication?.Failure);}}
                if(first>=0&&last-first>=900)break;yield return null;
            }
            Assert.Greater(last,first);File.WriteAllText(Path.Combine(folder,"actual-host-resume.txt"),log.ToString());TestContext.WriteLine(log.ToString());
        }finally{r.RequestStop();routes.Dispose();}
    }
}
