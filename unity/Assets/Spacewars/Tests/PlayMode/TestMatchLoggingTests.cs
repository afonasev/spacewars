using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class TestMatchLoggingTests
{
    string folder;TestMatchLogSession session;
    TestMatchLoggingProfile Settings()=>new TestMatchLoggingProfile{stateIntervalTicks=5,flushMilliseconds=20,queueCapacity=128,segmentBytes=4096};
    PlayableMatchJournalContext Context()=>new PlayableMatchJournalContext(PlayableProfile.Default,null,AiProfile.Initial,1,7,false,new[]{"player-1","enemy-1"},new[]{"enemy-1"});
    [SetUp] public void Setup(){folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/test-match-journals",TestContext.CurrentContext.Test.Name+"-"+Guid.NewGuid().ToString("N")));session=new TestMatchLogSession(folder,Settings(),new{Revision="fixture",Development=true});}
    [TearDown] public void Cleanup(){PlayableRuntime.JournalFactory=null;PlayableRuntime.JournalFailureReported=null;session.Dispose();}
    static string[] Events(string path)=>Directory.GetFiles(path,"events-*.jsonl").OrderBy(x=>x,StringComparer.Ordinal).SelectMany(File.ReadAllLines).ToArray();
    IEnumerator Drained(TestMatchJournal journal){double end=Time.realtimeSinceStartupAsDouble+5;while(!journal.WriterStopped&&Time.realtimeSinceStartupAsDouble<end)yield return null;Assert.True(journal.WriterStopped,"bounded writer must drain");}
    [Test] public void TestBuildDefaultsCannotDisableRequiredProfile()
    {
        var profile=JsonUtility.FromJson<TestMatchLoggingProfile>(Resources.Load<TextAsset>("TestMatchLoggingProfile").text);profile.Validate();Assert.True(profile.enabled);Assert.AreEqual(30,profile.stateIntervalTicks);
    }
    [TestCase(false,null,true)][TestCase(false,"test",true)][TestCase(false,"production",false)][TestCase(true,"production",true)]
    public void AllTestBuildRoutesRequireLogging(bool development,string channel,bool expected)
    {Assert.AreEqual(expected,TestMatchLoggingProfile.IsTestBuild(development,channel));}
    [TestCase(false,false,true,true)][TestCase(false,false,false,false)][TestCase(false,false,null,false)][TestCase(false,true,null,true)][TestCase(true,false,false,true)]
    public void RuntimeHonorsTestMarkerWithoutDevelopmentDefine(bool editor,bool development,bool? marker,bool expected)
    {Assert.AreEqual(expected,TestMatchLoggingProfile.ShouldRecord(editor,development,marker));}
    [UnityTest] public IEnumerator SessionsAndMatchesNeverOverwriteAndCompletedStreamHasReceipt()
    {
        var first=session.CreateMatch(Context());var second=session.CreateMatch(Context());Assert.AreNotEqual(first.DirectoryPath,second.DirectoryPath);
        first.Receipt(new PlayableCommandReceipt(12,34,PlayableCommandStatus.Applied,"applied fixture",0,"player-1"));first.Complete(null,null);second.Complete(null,null);
        yield return Drained(first);yield return Drained(second);
        var lines=Events(first.DirectoryPath).Select(JObject.Parse).ToArray();Assert.AreEqual("command-receipt",(string)lines[0]["Type"]);Assert.AreEqual("Applied",(string)lines[0]["Record"]["Status"]);
        Assert.AreEqual("complete",(string)JObject.Parse(File.ReadAllText(Path.Combine(first.DirectoryPath,"summary.json")))["Status"]);
        using(var next=new TestMatchLogSession(folder,Settings(),new{Revision="next"})){Assert.AreNotEqual(session.DirectoryPath,next.DirectoryPath);}
    }
    [Test] public void UnityErrorsAndRuntimeFailuresPreserveStacksAndCorrelation()
    {
        LogAssert.Expect(LogType.Error,"journal error fixture");UnityEngine.Debug.LogError("journal error fixture");
        var journal=session.CreateMatch(Context());journal.Failure(new InvalidOperationException("authority fixture"),123);
        var errors=File.ReadAllLines(Path.Combine(session.DirectoryPath,"errors.jsonl")).Select(JObject.Parse).ToArray();
        Assert.True(errors.Any(x=>(string)x["Message"]=="journal error fixture"));
        var runtime=errors.Single(x=>(string)x["Type"]=="RuntimeException");Assert.That((string)runtime["Stack"],Does.Contain("InvalidOperationException"));Assert.AreEqual(journal.DirectoryPath,(string)runtime["Match"]);
    }
    [UnityTest] public IEnumerator QueueOverflowIsBoundedAndReportedAsPartial()
    {
        var settings=Settings();settings.queueCapacity=16;
        using(var other=new TestMatchLogSession(folder,settings,new{Revision="overflow"})){
            var journal=other.CreateMatch(Context());var message=new string('x',8192);
            for(int i=0;i<2000;i++)journal.Receipt(new PlayableCommandReceipt(i,i,PlayableCommandStatus.Applied,message,0,"player-1"));
            journal.Complete(null,null);yield return Drained(journal);
            var summary=JObject.Parse(File.ReadAllText(Path.Combine(journal.DirectoryPath,"summary.json")));Assert.AreEqual("partial",(string)summary["Status"]);Assert.Greater((long)summary["Dropped"],0);
            Assert.Greater(Directory.GetFiles(journal.DirectoryPath,"events-*.jsonl").Length,1,"file segments rotate without overwriting");
        }
    }
    [UnityTest] public IEnumerator InterruptedWriterCannotClaimCompletedMatch()
    {
        var journal=session.CreateMatch(Context());journal.Receipt(new PlayableCommandReceipt(1,1,PlayableCommandStatus.Applied,"before exit",0,"player-1"));journal.Dispose();yield return Drained(journal);
        var summary=JObject.Parse(File.ReadAllText(Path.Combine(journal.DirectoryPath,"summary.json")));Assert.AreEqual("partial",(string)summary["Status"]);Assert.IsNotEmpty((string)summary["Interruption"]);
    }
    [UnityTest] public IEnumerator DetachedOwnerReviewsDoNotReplayHistoricalJournalReceipts()
    {
        TestMatchJournal journal=null;PlayableRuntime.JournalFactory=c=>journal=session.CreateMatch(c);
        var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
        var lobby=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7,Participants=new System.Collections.Generic.List<NativeLobbyParticipant>{new NativeLobbyParticipant{Name="AI",Team=1,Color=0},new NativeLobbyParticipant{Name="Human",Human=true,Team=2,Color=1}}};
        var runtime=new PlayableRuntime(NativeLobbyMatch.Create(profile,lobby),71);var routes=new UnityHostRouteService();
        var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(runtime);
        try{
            double end=Time.realtimeSinceStartupAsDouble+15;
            while(runtime.Latest.Tick<120&&!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<end){routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
            Assert.GreaterOrEqual(runtime.Latest.Tick,120);Assert.False(runtime.IsStopped,runtime.Latest.Failure);
            runtime.RequestStop();while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<end)yield return null;Assert.True(runtime.IsStopped);
            yield return Drained(journal);
            var summary=JObject.Parse(File.ReadAllText(Path.Combine(journal.DirectoryPath,"summary.json")));Assert.AreEqual("complete",(string)summary["Status"]);Assert.Zero((long)summary["Dropped"]);
            var receipts=Events(journal.DirectoryPath).Select(JObject.Parse).Where(x=>(string)x["Type"]=="ai-receipt").Select(x=>x["Record"].ToString(Newtonsoft.Json.Formatting.None)).ToArray();
            Assert.IsNotEmpty(receipts,"ordinary detached AI must produce actual trace receipts");
            Assert.AreEqual(receipts.Distinct(StringComparer.Ordinal).Count(),receipts.Length,"New immutable owner copies must not replay already-recorded historical receipts.");
        }finally{
            runtime.RequestStop();routes.Dispose();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped&&domain.ObservedPathWorkerStopped,5000));runtime.Dispose();domain.DestroyOwnedNavigation();
        }
    }
    [UnityTest] public IEnumerator RejectedIngressIsLoggedWithoutChangingPausedWorld()
    {
        TestMatchJournal journal=null;PlayableRuntime.JournalFactory=c=>journal=session.CreateMatch(c);
        using(var runtime=new PlayableRuntime(PlayableProfile.Default,1,7,autonomousOwnerAi:false,humanControlledPlayer:true,startPaused:true))
        {
            var own=runtime.Latest.Entities.First(x=>x.Owner==PlayableOwner.Player);
            Assert.AreEqual(PlayableCommandStatus.Rejected,runtime.TrySubmit(new PlayableCommand(1,1,"player-1",PlayableCommandKind.Hold,new[]{own.Id})).Status);
            Assert.Zero(runtime.Latest.Tick);runtime.RequestStop();double deadline=Time.realtimeSinceStartupAsDouble+5;while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;Assert.True(runtime.IsStopped);yield return Drained(journal);
            Assert.True(Events(journal.DirectoryPath).Select(JObject.Parse).Any(x=>(string)x["Type"]=="command-receipt"&&(string)x["Record"]["Status"]=="Rejected"));
        }
    }
    [UnityTest] public IEnumerator WriterStorageFailureIsPreservedAndNeverClaimsCompletion()
    {
        var path=Path.Combine(folder,"broken-writer");Directory.CreateDirectory(Path.Combine(path,"events-0001.jsonl"));
        using(var journal=new TestMatchJournal(path,Settings(),Context(),new{Revision="io-failure"},session.WriteError))
        {
            yield return Drained(journal);Assert.False(File.Exists(Path.Combine(path,"summary.json")));Assert.True(File.Exists(Path.Combine(path,"recorder-failure.txt")));
        }
        session.Dispose();var end=JObject.Parse(File.ReadAllText(Path.Combine(session.DirectoryPath,"session-end.json")));Assert.True((bool)end["RecorderFailure"]);
        Assert.True(File.ReadAllLines(Path.Combine(session.DirectoryPath,"errors.jsonl")).Select(JObject.Parse).Any(x=>(string)x["Type"]=="RecorderFailure"));
    }
    [UnityTest] public IEnumerator RecorderFactoryFailureCannotFailTheMatch()
    {
        PlayableRuntime.JournalFactory=c=>throw new IOException("recorder factory fixture");PlayableRuntime.JournalFailureReported=e=>session.WriteError("RecorderFactoryFailure",e.Message,e.ToString(),null);
        using(var runtime=new PlayableRuntime(PlayableProfile.Default,1,7,autonomousOwnerAi:false,startPaused:true))
        {
            Assert.That(runtime.JournalFailure,Does.Contain("recorder factory fixture"));runtime.RequestStop();double deadline=Time.realtimeSinceStartupAsDouble+5;while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;Assert.True(runtime.IsStopped);Assert.AreEqual(RuntimeStatus.Stopped,runtime.Latest.Status);Assert.IsNull(runtime.Latest.Failure);
        }
    }
    [UnityTest] public IEnumerator DefaultStartupCreatesSessionAndHooksOrdinaryRuntimeWithoutEvidenceFlags()
    {
        try
        {
            TestMatchLogSession.StartDefault(Path.Combine(folder,"default-startup"));var path=TestMatchLogSession.CurrentDirectory;Assert.IsNotEmpty(path);Assert.NotNull(PlayableRuntime.JournalFactory);
            LogAssert.Expect(LogType.Error,"default startup error fixture");UnityEngine.Debug.LogError("default startup error fixture");
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,1,7,autonomousOwnerAi:false,startPaused:true))
            {runtime.RequestStop();double deadline=Time.realtimeSinceStartupAsDouble+5;while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;Assert.True(runtime.IsStopped);Assert.IsNull(runtime.JournalFailure);}
            TestMatchLogSession.PrepareForQuit();LogAssert.Expect(LogType.Error,"late quit error fixture");UnityEngine.Debug.LogError("late quit error fixture");
            TestMatchLogSession.Shutdown();
            Assert.True(File.Exists(Path.Combine(path,"session-end.json")));Assert.AreEqual(1,Directory.GetDirectories(path,"match-*").Length);
            Assert.True(File.ReadAllLines(Path.Combine(path,"errors.jsonl")).Select(JObject.Parse).Any(x=>(string)x["Message"]=="default startup error fixture"));
            Assert.True(File.ReadAllLines(Path.Combine(path,"errors.jsonl")).Select(JObject.Parse).Any(x=>(string)x["Message"]=="late quit error fixture"));
        }
        finally{TestMatchLogSession.Shutdown();}
    }
    [UnityTest] public IEnumerator ConfiguredLocalMatchKeepsCustomOwnersAndRoster()
    {
        var profile=PlayableProfile.Default;
        var starts=new[]{new OfflineStart("a",1,new NavPoint(-24,-24),new NavPoint(-10,-4),pin:1),new OfflineStart("b",2,new NavPoint(24,24),new NavPoint(2,-4),pin:2)};
        var config=new OfflineMatchConfiguration(profile,"local-log-fixture","flat-log","initial",7,new[]{new OfflineParticipant("owner-11",1,1,OfflineControl.Human),new OfflineParticipant("owner-28",2,2,OfflineControl.Human)},starts,starts.Select(s=>new TerritorySite(s.SiteId,PlayableBuildingKind.Headquarters,s.Position,Array.Empty<TerritorySlot>())).ToArray(),Array.Empty<NavObstacle>(),new double[,]{{0,1},{1,0}});
        TestMatchJournal journal=null;PlayableRuntime.JournalFactory=c=>journal=session.CreateMatch(c);
        using(var runtime=new PlayableRuntime(config,1,startPaused:true))
        {runtime.RequestStop();double deadline=Time.realtimeSinceStartupAsDouble+5;while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;Assert.True(runtime.IsStopped);Assert.IsNull(runtime.JournalFailure);yield return Drained(journal);}
        var metadata=JObject.Parse(File.ReadAllText(Path.Combine(journal.DirectoryPath,"manifest.json")));Assert.AreEqual(2,((JArray)metadata["Roster"]).Count);Assert.AreEqual("flat-log",(string)metadata["Map"]);
        var state=Events(journal.DirectoryPath).Select(JObject.Parse).First(x=>(string)x["Type"]=="state");CollectionAssert.AreEquivalent(new[]{"owner-11","owner-28"},((JArray)state["Owners"]).Select(x=>(string)x["OwnerId"]).ToArray());
    }
    [UnityTest] public IEnumerator RestoredRuntimeStartsNewJournalWithoutChangingSaveFormat()
    {
        var journals=new System.Collections.Generic.List<TestMatchJournal>();PlayableRuntime.JournalFactory=c=>{var j=session.CreateMatch(c);journals.Add(j);return j;};byte[] bytes;
        using(var original=new PlayableRuntime(PlayableProfile.Default,1,7,autonomousOwnerAi:false,startPaused:true))
        {
            var capture=original.RequestCaptureBytes();double deadline=Time.realtimeSinceStartupAsDouble+5;while(!capture.IsCompleted&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;Assert.True(capture.IsCompleted);Assert.False(capture.IsFaulted,capture.Exception?.ToString());bytes=capture.Result;
            original.RequestStop();while(!original.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;Assert.True(original.IsStopped);
        }
        using(var restored=PlayableRuntime.RestoreBytes(bytes,PlayableProfile.Default,7,autonomousOwnerAi:false))
        {restored.RequestStop();double deadline=Time.realtimeSinceStartupAsDouble+5;while(!restored.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;Assert.True(restored.IsStopped);Assert.IsNull(restored.JournalFailure);}
        Assert.AreEqual(2,journals.Count);foreach(var j in journals)yield return Drained(j);
        var metadata=JObject.Parse(File.ReadAllText(Path.Combine(journals[1].DirectoryPath,"manifest.json")));Assert.True((bool)metadata["Restored"]);Assert.AreNotEqual(journals[0].DirectoryPath,journals[1].DirectoryPath);
    }
    [UnityTest] public IEnumerator OrdinaryHumanMatchRecordsActualAiDecisionsAndCommands()=>RunMatch(false);
    [UnityTest] public IEnumerator OrdinarySpectatorMatchRecordsBothAiOwners()=>RunMatch(true);
    IEnumerator RunMatch(bool spectator)
    {
        TestMatchJournal journal=null;PlayableRuntime.JournalFactory=c=>journal=session.CreateMatch(c);
        using(var runtime=new PlayableRuntime(PlayableProfile.Default,1,7,humanControlledPlayer:!spectator,spectator:spectator))
        using(var routes=new UnityHostRouteService()){
            if(!spectator){var own=runtime.Latest.Entities.First(x=>x.Owner==PlayableOwner.Player);Assert.True(runtime.TrySubmit(new PlayableCommand(1,1,"player-1",PlayableCommandKind.Hold,new[]{own.Id})).Accepted);}
            double end=Time.realtimeSinceStartupAsDouble+20;
            while(runtime.Latest.Tick<45&&Time.realtimeSinceStartupAsDouble<end){routes.Service(runtime,64);yield return null;}
            Assert.IsNull(runtime.Latest.Failure);Assert.GreaterOrEqual(runtime.Latest.Tick,45,"ordinary threaded match must advance");
            runtime.RequestStop();while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<end)yield return null;Assert.True(runtime.IsStopped);yield return Drained(journal);
            var rows=Events(journal.DirectoryPath).Select(JObject.Parse).ToArray();
            Assert.True(rows.Any(x=>(string)x["Type"]=="state"));Assert.True(rows.Any(x=>(string)x["Type"]=="ai-checkpoint"));Assert.True(rows.Any(x=>(string)x["Type"]=="ai-receipt"));
            Assert.True(rows.Any(x=>(string)x["Type"]=="ai-economy-decision"),"real policy decisions, including alternatives and reasons");
            var owners=rows.Where(x=>(string)x["Type"]=="ai-checkpoint").Select(x=>(string)x["OwnerId"]).Distinct().ToArray();
            CollectionAssert.AreEquivalent(spectator?new[]{"player-1","enemy-1"}:new[]{"enemy-1"},owners);
            if(!spectator){Assert.True(rows.Any(x=>(string)x["Type"]=="command"));Assert.True(rows.Any(x=>(string)x["Type"]=="command-receipt"&&(string)x["Record"]["Status"]=="Applied"));}
            Assert.AreEqual("complete",(string)JObject.Parse(File.ReadAllText(Path.Combine(journal.DirectoryPath,"summary.json")))["Status"]);
            var metadata=JObject.Parse(File.ReadAllText(Path.Combine(journal.DirectoryPath,"manifest.json")));Assert.AreEqual(spectator,(bool)metadata["Spectator"]);Assert.AreEqual(7,(int)metadata["Seed"]);
        }
    }
}
