using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    [Serializable] public sealed class TestMatchLoggingProfile
    {
        public string id="test-match-logs-v1";
        public bool enabled=true;
        // Technical storage limits, not simulation/balance parameters.
        public int stateIntervalTicks=30,queueCapacity=4096,flushMilliseconds=1000,segmentBytes=16777216;
        public static bool IsTestBuild(bool development,string channel)
        {if(channel!=null&&channel!="test"&&channel!="production")throw new ArgumentException("Invalid build channel");return development||channel!="production";}
        public static bool ShouldRecord(bool editor,bool development,bool? testBuild)=>editor||development||testBuild==true;
        public void Validate(){if(id!="test-match-logs-v1"||stateIntervalTicks<1||queueCapacity<16||flushMilliseconds<10||segmentBytes<4096)throw new ArgumentException("Invalid test match logging profile");}
    }

    // One directory per process launch, independent of match/restart/viewport lifetime.
    public sealed class TestMatchLogSession : IDisposable
    {
        static TestMatchLogSession active;
        readonly object gate=new object();
        readonly List<TestMatchJournal> matches=new List<TestMatchJournal>();
        readonly TestMatchLoggingProfile profile;
        readonly object build;
        StreamWriter errors;
        bool closed,reportingFailure,hadRecorderFailure;
        public readonly string DirectoryPath;
        public static string CurrentDirectory=>active?.DirectoryPath;
        static readonly JsonSerializerSettings Json=new JsonSerializerSettings{ReferenceLoopHandling=ReferenceLoopHandling.Error,Converters={new StringEnumConverter()}};
        internal static string Serialize(object value)=>JsonConvert.SerializeObject(value,Formatting.None,Json);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoStart()
        {
            // Batch Editor tests explicitly create an isolated session. Interactive Play Mode
            // and test Players use the default automatically, with no launch flag.
            if(Application.isEditor&&Application.isBatchMode)return;
            StartDefault();
        }
        public static void StartDefault(string parentOverride=null)
        {
            if(active!=null)return;
            try{
                var identity=Path.Combine(Application.streamingAssetsPath,"TestBuildIdentity.json");
                var provenance=File.Exists(identity)?(object)JsonConvert.DeserializeObject(File.ReadAllText(identity)):new {SourceRevision="unknown-editor",Dirty=(bool?)null};
                bool? testBuild=(provenance as Newtonsoft.Json.Linq.JObject)?["TestBuild"]?.Value<bool>();
                if(!TestMatchLoggingProfile.ShouldRecord(Application.isEditor,UnityEngine.Debug.isDebugBuild,testBuild))return;
                if(!Application.isEditor)foreach(var type in new[]{LogType.Error,LogType.Exception,LogType.Assert,LogType.Warning})Application.SetStackTraceLogType(type,StackTraceLogType.Full);
                var asset=Resources.Load<TextAsset>("TestMatchLoggingProfile");
                if(!asset)throw new InvalidOperationException("Missing required TestMatchLoggingProfile");
                var settings=JsonUtility.FromJson<TestMatchLoggingProfile>(asset.text);settings.Validate();
                if(!settings.enabled)throw new InvalidOperationException("Test match recording must be enabled in test builds");
                active=new TestMatchLogSession(parentOverride??Path.Combine(Application.persistentDataPath,"TestLogs"),settings,new{Application.version,Application.unityVersion,Platform=Application.platform.ToString(),Development=UnityEngine.Debug.isDebugBuild,TestBuild=true,Source=provenance});
                PlayableRuntime.JournalFactory=active.CreateMatch;
                PlayableRuntime.JournalFailureReported=active.RecorderFailure;
                Application.quitting+=PrepareForQuit;AppDomain.CurrentDomain.ProcessExit+=ProcessExit;
                UnityEngine.Debug.Log("TEST_MATCH_LOGS "+active.DirectoryPath);
            }catch(Exception error){UnityEngine.Debug.LogError("TEST_MATCH_LOGS_UNAVAILABLE: "+error);}
        }
        static void ProcessExit(object sender,EventArgs args){try{Shutdown();}catch(Exception error){Console.Error.WriteLine("TEST_MATCH_LOGS_EXIT: "+error);}}
        public static void PrepareForQuit(){active?.DrainMatchesForQuit();}
        void DrainMatchesForQuit(){TestMatchJournal[] copy;lock(gate)copy=matches.ToArray();foreach(var match in copy)match.Dispose();}
        public static void Shutdown(){active?.Dispose();active=null;PlayableRuntime.JournalFactory=null;PlayableRuntime.JournalFailureReported=null;Application.quitting-=PrepareForQuit;AppDomain.CurrentDomain.ProcessExit-=ProcessExit;}
        public TestMatchLogSession(string parent,TestMatchLoggingProfile profile,object build)
        {
            this.profile=profile??throw new ArgumentNullException(nameof(profile));profile.Validate();this.build=build;
            DirectoryPath=Path.Combine(parent,DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")+"-"+Process.GetCurrentProcess().Id+"-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            errors=new StreamWriter(Path.Combine(DirectoryPath,"errors.jsonl"),false){AutoFlush=true};
            File.WriteAllText(Path.Combine(DirectoryPath,"session.json"),Serialize(new{Schema=1,StartedUtc=DateTime.UtcNow,Build=build,Logging=profile,Status="recording",Completion="Missing session-end.json means interrupted recording"}));
            Application.logMessageReceivedThreaded+=UnityMessage;
            AppDomain.CurrentDomain.UnhandledException+=Unhandled;
        }
        public TestMatchJournal CreateMatch(PlayableMatchJournalContext context)
        {
            lock(gate){if(closed)throw new ObjectDisposedException(nameof(TestMatchLogSession));
                var match=new TestMatchJournal(Path.Combine(DirectoryPath,"match-"+context.Generation+"-"+Guid.NewGuid().ToString("N")),profile,context,build,WriteError);
                matches.Add(match);return match;}
        }
        void UnityMessage(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Assert||type==LogType.Exception||type==LogType.Warning)WriteError(type.ToString(),message,stack,null);}
        void Unhandled(object sender,UnhandledExceptionEventArgs args)=>WriteError("UnhandledException",args.ExceptionObject?.ToString(),null,null);
        public void WriteError(string type,string message,string stack,string match)
        {
            lock(gate){if(closed)return;if(type.StartsWith("Recorder",StringComparison.Ordinal))hadRecorderFailure=true;
                try{errors.WriteLine(Serialize(new{Utc=DateTime.UtcNow,Thread=Thread.CurrentThread.ManagedThreadId,Type=type,Message=message,Stack=stack,Match=match,ActiveMatches=matches.Where(m=>!m.CompleteRequested).Select(m=>m.DirectoryPath).ToArray()}));}
                catch(Exception error){RecorderFailure(error);}}
        }
        void RecorderFailure(Exception error)
        {
            // Report once to the engine log even if this disk is full. Avoid recursive log callbacks.
            lock(gate){if(reportingFailure)return;reportingFailure=true;hadRecorderFailure=true;}
            UnityEngine.Debug.LogError("TEST_MATCH_LOGS_PARTIAL: "+error);
        }
        public void Dispose()
        {
            TestMatchJournal[] copy;
            lock(gate){if(closed)return;closed=true;copy=matches.ToArray();}
            Application.logMessageReceivedThreaded-=UnityMessage;AppDomain.CurrentDomain.UnhandledException-=Unhandled;
            foreach(var m in copy)m.Dispose();
            lock(gate){try{errors.Dispose();File.WriteAllText(Path.Combine(DirectoryPath,"session-end.json"),Serialize(new{EndedUtc=DateTime.UtcNow,Matches=copy.Length,RecorderFailure=hadRecorderFailure}));}catch(Exception error){RecorderFailure(error);}}
        }
    }

    public sealed class TestMatchJournal : IPlayableMatchJournal,IDisposable
    {
        sealed class Row{public object Value;public bool End;}
        readonly BlockingCollection<Row> queue;
        readonly Thread writer;
        readonly TestMatchLoggingProfile settings;
        readonly Action<string,string,string,string> error;
        // Detached owner adoption reconstructs immutable receipt objects. Object
        // identity is not event identity; compare all immutable terms without JSON
        // serialization or a match-long history on the authority thread.
        sealed class TraceComparer:IEqualityComparer<PlayableAiTraceRecord>
        {
            public bool Equals(PlayableAiTraceRecord a,PlayableAiTraceRecord b)
            {
                if(ReferenceEquals(a,b))return true;if(a==null||b==null)return false;
                var ar=a.ReceiptIdentity;var br=b.ReceiptIdentity;
                bool receipt=ReferenceEquals(ar,br)||ar!=null&&br!=null&&ar.Generation==br.Generation&&ar.OwnerId==br.OwnerId&&ar.DecisionOrdinal==br.DecisionOrdinal&&ar.ActionOrdinal==br.ActionOrdinal;
                return receipt&&a.ObservationIdentity==b.ObservationIdentity&&a.OwnerId==b.OwnerId&&a.SourceIdentity==b.SourceIdentity&&a.ActionId==b.ActionId&&a.DueTick==b.DueTick&&
                    a.CommandSequence==b.CommandSequence&&a.ApplicationTick==b.ApplicationTick&&a.Status==b.Status&&a.RuntimeStatus==b.RuntimeStatus&&a.Message==b.Message&&a.Policy==b.Policy&&a.Kind==b.Kind;
            }
            public int GetHashCode(PlayableAiTraceRecord value)=>value==null?0:unchecked(((int)value.ActionId^(int)(value.ActionId>>32))*397^(int)value.Status);
        }
        static readonly IEqualityComparer<PlayableAiTraceRecord> traceComparer=new TraceComparer();
        readonly Dictionary<string,HashSet<PlayableAiTraceRecord>> traceSeen=new Dictionary<string,HashSet<PlayableAiTraceRecord>>();
        readonly Dictionary<string,string> decisionSeen=new Dictionary<string,string>();
        readonly Dictionary<string,long> checkpointSeen=new Dictionary<string,long>();
        long lastState=-1,dropped,written;
        readonly long startTick;
        int complete,disposed;
        volatile string writerFailure;
        public readonly string DirectoryPath;
        public bool CompleteRequested=>Volatile.Read(ref complete)!=0;
        public bool WriterStopped=>!writer.IsAlive;
        public TestMatchJournal(string path,TestMatchLoggingProfile settings,PlayableMatchJournalContext context,object build,Action<string,string,string,string> error)
        {
            DirectoryPath=path;startTick=context.StartTick;this.settings=settings;settings.Validate();this.error=error;
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path,"manifest.json"),TestMatchLogSession.Serialize(new{Schema=1,StartedUtc=DateTime.UtcNow,Build=build,context.Generation,context.Seed,context.Spectator,context.Restored,context.StartTick,context.Owners,context.AiOwners,Map=context.Configuration?.MapIdentity??context.Profile.AuthoredMap?.GetType().Name??"flat-diagnostic",Roster=context.Configuration?.Roster,Profile=context.Profile.CopyData(),Ai=new{context.Ai.Id,context.Ai.Revision,context.Ai.Hash,Data=context.Ai.CopyData()},Logging=settings,Status="recording",Completion="Missing summary.json means incomplete; JSONL last partial line may be discarded"}));
            queue=new BlockingCollection<Row>(settings.queueCapacity);
            writer=new Thread(WriteLoop){IsBackground=true,Name="Spacewars.TestMatchLog"};writer.Start();
        }
        void Enqueue(object value,bool end=false)
        {
            if(Volatile.Read(ref disposed)!=0||queue.IsAddingCompleted||writerFailure!=null)return;
            try{if(!queue.TryAdd(new Row{Value=value,End=end}))Interlocked.Increment(ref dropped);}catch(InvalidOperationException){/* Close raced a final authority publication. */}
        }
        public void Command(PlayableCommand command)=>Enqueue(new{Type="command",Utc=DateTime.UtcNow,Record=command});
        public void Receipt(PlayableCommandReceipt receipt)=>Enqueue(new{Type="command-receipt",Utc=DateTime.UtcNow,Record=receipt});
        public void Failure(Exception failure,long tick){error?.Invoke("RuntimeException",failure.Message,failure.ToString(),DirectoryPath);Enqueue(new{Type="runtime-failure",Tick=tick,Exception=failure.ToString()});}
        public void Capture(PlayableAuthorityTick authority,bool paused)
        {
            if(CompleteRequested)return;
            foreach(var c in authority.CaptureDiagnosticCheckpoints())
            {
                if(!traceSeen.TryGetValue(c.OwnerId,out var previous))previous=new HashSet<PlayableAiTraceRecord>(traceComparer);
                foreach(var receipt in c.Records)if(!previous.Contains(receipt))Enqueue(new{Type="ai-receipt",Tick=authority.Tick,c.OwnerId,Historical=receipt.ApplicationTick>=0&&receipt.ApplicationTick<startTick,Record=receipt});
                // Only the bounded owner trace window is retained, not a match-long identity set.
                traceSeen[c.OwnerId]=new HashSet<PlayableAiTraceRecord>(c.Records,traceComparer);
                var decision=c.EconomyDecision;
                if(decision!=null&&(!decisionSeen.TryGetValue(c.OwnerId,out var identity)||identity!=decision.Identity.ToString()))
                {decisionSeen[c.OwnerId]=decision.Identity.ToString();Enqueue(new{Type="ai-economy-decision",Tick=authority.Tick,c.OwnerId,Record=decision});}
                if(!checkpointSeen.TryGetValue(c.OwnerId,out var seen)||seen!=c.DecisionTick)
                {checkpointSeen[c.OwnerId]=c.DecisionTick;Enqueue(new{Type="ai-checkpoint",Tick=authority.Tick,c.OwnerId,c.DecisionTick,c.LastPolicy,c.LastActionKind,c.LastScoutTick,c.LastScoutOrderTick,c.PendingActionId,c.PendingDueTick,c.LastCommandSequence,c.Knowledge,c.Mission,c.ArmyMission,c.Armies,c.Strategy,c.OpeningExecution,c.Infrastructure,c.Expansion,c.GamePhase,c.Composition,c.Doctrine,c.AiRevision,c.AiHash,c.Difficulty,c.PersonalitySeed});}
            }
            if(lastState>=0&&authority.Tick-lastState<settings.stateIntervalTicks)return;
            lastState=authority.Tick;
            // Full owner-visible snapshots are detached here; geometry/fog textures and complete
            // profile objects are intentionally not serialized repeatedly with each sample.
            var ids=authority.CaptureDiagnosticOwnerIds();
            var views=ids.Select(id=>authority.ParticipantView(id)).ToArray();
            Enqueue(new{Type="state",Tick=authority.Tick,Paused=paused,Outcome=views.FirstOrDefault()?.Outcome,authority.WinnerTeam,Owners=views.Select(v=>new{v.OwnerId,v.Team,v.Credits,v.ExactCredits,v.IncomePerSecond,v.SettledIncome,v.Population,v.OwnerEliminated,v.Entities,v.Buildings,v.OwnerResearch,v.DiscoveredSites,v.Metrics,v.Failure}).ToArray()});
        }
        public void Complete(PlayableSnapshot final,MatchResult result)
        {
            if(Interlocked.Exchange(ref complete,1)!=0)return;
            Enqueue(new{Type="match-end",Tick=final?.Tick,Outcome=final?.Outcome,Status=final?.Status,Failure=final?.Failure,Result=result},true);
            queue.CompleteAdding();
        }
        void WriteLoop()
        {
            StreamWriter stream=null;int segment=0;long bytes=0;object ending=null;var flush=Stopwatch.StartNew();
            try{
                stream=Open(++segment);
                while(!queue.IsCompleted){
                    if(queue.TryTake(out var row,settings.flushMilliseconds)){
                        var line=TestMatchLogSession.Serialize(row.Value);stream.WriteLine(line);bytes+=System.Text.Encoding.UTF8.GetByteCount(line)+1;Interlocked.Increment(ref written);if(row.End)ending=row.Value;
                        if(bytes>=settings.segmentBytes){stream.Dispose();stream=Open(++segment);bytes=0;}
                    }
                    if(flush.ElapsedMilliseconds>=settings.flushMilliseconds){stream.Flush();flush.Restart();}
                }
                stream.Flush();
                File.WriteAllText(Path.Combine(DirectoryPath,"summary.json"),TestMatchLogSession.Serialize(new{EndedUtc=DateTime.UtcNow,Status=ending!=null&&dropped==0?"complete":"partial",Dropped=Interlocked.Read(ref dropped),Written=Interlocked.Read(ref written),Segments=segment,End=ending,Interruption=ending==null?"Stopped without authority completion":null}));
            }catch(Exception ex){writerFailure=ex.ToString();try{error?.Invoke("RecorderFailure",ex.Message,ex.ToString(),DirectoryPath);File.WriteAllText(Path.Combine(DirectoryPath,"recorder-failure.txt"),ex.ToString());}catch{/* Engine log still records the storage error when available. */}}
            finally{try{stream?.Dispose();}catch(Exception ex){try{error?.Invoke("RecorderCloseFailure",ex.Message,ex.ToString(),DirectoryPath);}catch{}}}
        }
        StreamWriter Open(int segment)=>new StreamWriter(Path.Combine(DirectoryPath,"events-"+segment.ToString("D4")+".jsonl"),false);
        public void Dispose()
        {
            if(Interlocked.Exchange(ref disposed,1)!=0)return;
            queue.CompleteAdding();
            // Bounded process-exit wait; an unjoined writer retains the initial incomplete manifest.
            if(!writer.Join(2000))error?.Invoke("RecorderTimeout","Writer did not drain before shutdown",null,DirectoryPath);
        }
    }
}
