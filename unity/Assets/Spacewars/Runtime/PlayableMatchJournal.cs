using System;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Trusted local diagnostics only. Capture runs on authority; a sink must not retain
    // authority or query it from another thread. No journal data enters observations/saves.
    public interface IPlayableMatchJournal
    {
        void Capture(PlayableAuthorityTick authority,bool paused);
        void Command(PlayableCommand command);
        void Receipt(PlayableCommandReceipt receipt);
        void Failure(Exception error,long tick);
        void Complete(PlayableSnapshot final,MatchResult result);
    }
    public sealed class PlayableMatchJournalContext
    {
        public readonly PlayableProfile Profile;
        public readonly OfflineMatchConfiguration Configuration;
        public readonly AiProfile Ai;
        public readonly long Generation;
        public readonly int Seed;
        public readonly bool Spectator,Restored;
        public readonly long StartTick;
        public readonly string[] Owners,AiOwners;
        public PlayableMatchJournalContext(PlayableProfile profile,OfflineMatchConfiguration configuration,AiProfile ai,long generation,int seed,bool spectator,string[] owners,string[] aiOwners,long startTick=0,bool restored=false)
        {Profile=profile;Configuration=configuration;Ai=ai;Generation=generation;Seed=seed;Spectator=spectator;Owners=owners;AiOwners=aiOwners;StartTick=startTick;Restored=restored;}
    }
    public sealed partial class PlayableRuntime
    {
        // Presentation installs the factory only in test builds. Null is zero-I/O production.
        public static Func<PlayableMatchJournalContext,IPlayableMatchJournal> JournalFactory;
        public static Action<Exception> JournalFailureReported;
        IPlayableMatchJournal matchJournal;
        int journalFailed;
        public string JournalFailure{get;private set;}
        void InitializeJournal(PlayableProfile profile,bool restored=false)
        {
            if(JournalFactory==null)return;
            try{matchJournal=JournalFactory(new PlayableMatchJournalContext(profile,offlineConfiguration,NativeAiProfile,Generation,seed,localSpectator,authorityTick.CaptureDiagnosticOwnerIds(),System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(aiScheduler.Owners,o=>o.OwnerId)),authorityTick.Tick,restored));}
            catch(Exception error){JournalProblem(error);}
        }
        void JournalProblem(Exception error)
        {
            JournalFailure=error.ToString();
            if(System.Threading.Interlocked.Exchange(ref journalFailed,1)!=0)return;
            try{JournalFailureReported?.Invoke(error);}catch{/* Recorder failure cannot fail the match. */}
        }
        void JournalCapture(bool paused)
        {if(matchJournal==null)return;try{matchJournal.Capture(authorityTick,paused);}catch(Exception error){JournalProblem(error);}}
        void JournalCommand(PlayableCommand command)
        {if(matchJournal==null)return;try{matchJournal.Command(command);}catch(Exception error){JournalProblem(error);}}
        void JournalReceipt(PlayableCommandReceipt receipt)
        {if(matchJournal==null)return;try{matchJournal.Receipt(receipt);}catch(Exception error){JournalProblem(error);}}
        void JournalException(Exception error)
        {if(matchJournal==null)return;try{matchJournal.Failure(error,domain.Tick);}catch(Exception secondary){JournalProblem(secondary);}}
        void JournalComplete()
        {if(matchJournal==null)return;try{matchJournal.Capture(authorityTick,paused!=0);matchJournal.Complete(latest,domain.Result);}catch(Exception error){JournalProblem(error);}}
    }
}
