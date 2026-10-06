using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    public enum PlayableAiOpening { Safe, GreedySafe, GreedyMine, BlindRush, ExplorerAllIn, DoubleMineExplorerRush }
    public enum PlayableAiOpeningPhase { Active, Complete, Aborted }

    [Serializable]
    public sealed class PlayableAiCompositionIntent
    {
        public PlayableAiCompositionIntent(int explorer,int tank,double explorerShare)
        {
            if(explorer<0||tank<0||explorerShare<0||explorerShare>1)throw new ArgumentOutOfRangeException(nameof(explorer));
            Explorer=explorer;Tank=tank;ExplorerShare=explorerShare;
        }
        public int Explorer{get;} public int Tank{get;} public double ExplorerShare{get;}
        public bool SemanticallyEquals(PlayableAiCompositionIntent other)=>other!=null&&Explorer==other.Explorer&&Tank==other.Tank&&ExplorerShare.Equals(other.ExplorerShare);
    }

    [Serializable]
    public sealed class PlayableAiOpeningCompositionState
    {
        private readonly string[] commitments;
        internal PlayableAiOpeningCompositionState(string ownerId,int matchSeed,string profileIdentity,string sourceIdentity,uint personalitySeed,PlayableAiOpening opening,PlayableAiOpeningPhase phase,PlayableAiCompositionIntent intent,IEnumerable<string> commitments)
        {
            if(String.IsNullOrEmpty(ownerId)||String.IsNullOrEmpty(profileIdentity)||String.IsNullOrEmpty(sourceIdentity)||intent==null)throw new ArgumentException("Opening composition provenance is required.");
            OwnerId=ownerId;MatchSeed=matchSeed;ProfileIdentity=profileIdentity;SourceIdentity=sourceIdentity;PersonalitySeed=personalitySeed;Opening=opening;InitialOpening=opening;Phase=phase;Intent=intent;this.commitments=(commitments??Enumerable.Empty<string>()).ToArray();
        }
        public string OwnerId{get;} public int MatchSeed{get;} public string ProfileIdentity{get;} public string SourceIdentity{get;} public uint PersonalitySeed{get;}
        public PlayableAiOpening Opening{get;} public PlayableAiOpening InitialOpening{get;} public PlayableAiOpeningPhase Phase{get;} public PlayableAiCompositionIntent Intent{get;}
        public IReadOnlyList<string> Commitments=>Array.AsReadOnly(commitments);
        public PlayableAiOpeningCompositionState Restore()=>new PlayableAiOpeningCompositionState(OwnerId,MatchSeed,ProfileIdentity,SourceIdentity,PersonalitySeed,Opening,Phase,new PlayableAiCompositionIntent(Intent.Explorer,Intent.Tank,Intent.ExplorerShare),commitments);
        public bool SemanticallyEquals(PlayableAiOpeningCompositionState other)=>other!=null&&OwnerId==other.OwnerId&&MatchSeed==other.MatchSeed&&ProfileIdentity==other.ProfileIdentity&&SourceIdentity==other.SourceIdentity&&PersonalitySeed==other.PersonalitySeed&&Opening==other.Opening&&InitialOpening==other.InitialOpening&&Phase==other.Phase&&Intent.SemanticallyEquals(other.Intent)&&commitments.SequenceEqual(other.commitments);
    }

    // Frozen source provenance, not a writable native balance profile. Values are audited from ai/release.json at 96a32f63.
    public static class PlayableAiOpeningComposition
    {
        public const string SourceProfileIdentity="adaptive-strategic-ai-v1@release:8";
        public const string SourceIdentity="source:96a32f63:ai-release-c569e3a03045";
        private static readonly PlayableAiOpening[] Ordered={PlayableAiOpening.Safe,PlayableAiOpening.GreedySafe,PlayableAiOpening.GreedyMine,PlayableAiOpening.BlindRush,PlayableAiOpening.ExplorerAllIn,PlayableAiOpening.DoubleMineExplorerRush};
        private static readonly string[][] Commitments={new[]{"factory","refinery"},new[]{"refinery","refinery","factory"},new[]{"refinery","capture-mine"},new[]{"factory","first-tank","committed-attack"},new[]{"factory","mass-explorer-production","first-wave","streaming-reinforcement"},new[]{"factory","capture-two-local-mines","build-two-mines","mass-explorer-production","early-attack"}};
        private static readonly double[] BaseWeights={.49,.22,.12,.16,.06,.12};
        public static IReadOnlyList<PlayableAiOpening> Supported=>Array.AsReadOnly(Ordered);
        public static PlayableAiOpeningCompositionState Initialize(int matchSeed,string ownerId,string sourceProfileIdentity=SourceProfileIdentity)
        {
            if(String.IsNullOrEmpty(ownerId))throw new ArgumentException("Owner identity is required.",nameof(ownerId));
            if(sourceProfileIdentity!=SourceProfileIdentity)throw new ArgumentException("Frozen source profile identity does not match this diagnostic slice.",nameof(sourceProfileIdentity));
            var personalitySeed=Hash($"{matchSeed}:{ownerId}:adaptive-strategic-ai-v1:8");
            var aggression=.15+(.95-.15)*Random(personalitySeed,"trait:aggression");
            var greed=.15+(.95-.15)*Random(personalitySeed,"trait:greed");
            var caution=.15+(.95-.15)*Random(personalitySeed,"trait:caution");
            var remoteExpansion=.05+(.65-.05)*Random(personalitySeed,"trait:remoteExpansion");
            var allIn=.05+(.65-.05)*Random(personalitySeed,"trait:allIn");
            var weights=new[]{BaseWeights[0]*(.5+caution),BaseWeights[1]*(.5+greed+caution*.25),BaseWeights[2]*(.5+greed+remoteExpansion),BaseWeights[3]*(.5+aggression+allIn*.5),BaseWeights[4]*(.35+aggression+allIn),BaseWeights[5]*(.45+aggression*.65+greed*.8)};
            var pick=Random(personalitySeed,"opening")*weights.Sum();var cursor=0d;var selected=0;
            for(var i=0;i<Ordered.Length;i++){cursor+=weights[i];if(pick<=cursor){selected=i;break;}}
            return new PlayableAiOpeningCompositionState(ownerId,matchSeed,sourceProfileIdentity,SourceIdentity,personalitySeed,Ordered[selected],PlayableAiOpeningPhase.Active,new PlayableAiCompositionIntent(1,1,.5),Commitments[selected]);
        }
        private static uint Hash(string text)
        {
            unchecked { uint value=2166136261;foreach(var character in text)value=(uint)((value^(ushort)character)*16777619);return value; }
        }
        private static double Random(uint seed,string key)=>Hash($"{seed}:{key}")/4294967296d;
    }

    // Owns diagnostic strategic state outside of live observations and command delivery.
    [Serializable]
    public sealed class PlayableAiOpeningCompositionAuthority
    {
        private readonly Dictionary<string,PlayableAiOpeningCompositionState> states=new Dictionary<string,PlayableAiOpeningCompositionState>();
        public PlayableAiOpeningCompositionState Initialize(int matchSeed,string ownerId,string sourceProfileIdentity=PlayableAiOpeningComposition.SourceProfileIdentity)
        {
            var state=PlayableAiOpeningComposition.Initialize(matchSeed,ownerId,sourceProfileIdentity);states[ownerId]=state;return state;
        }
        public PlayableAiOpeningCompositionState ForOwner(string ownerId)=>ownerId!=null&&states.TryGetValue(ownerId,out var state)?state:null;
        public IReadOnlyList<PlayableAiOpeningCompositionState> Capture()=>states.Values.OrderBy(x=>x.OwnerId,StringComparer.Ordinal).Select(x=>x.Restore()).ToArray();
        public void Restore(IEnumerable<PlayableAiOpeningCompositionState> snapshot)
        {
            states.Clear();foreach(var state in snapshot??Enumerable.Empty<PlayableAiOpeningCompositionState>()){if(state==null||states.ContainsKey(state.OwnerId))throw new ArgumentException("Snapshot must contain one valid state per owner.");states.Add(state.OwnerId,state.Restore());}
        }
    }
}
