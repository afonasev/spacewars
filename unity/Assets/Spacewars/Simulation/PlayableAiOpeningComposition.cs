using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Spacewars.Simulation.Ai;

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
        public PlayableAiOpeningCompositionState WithPhase(PlayableAiOpeningPhase phase)=>new PlayableAiOpeningCompositionState(OwnerId,MatchSeed,ProfileIdentity,SourceIdentity,PersonalitySeed,Opening,phase,Intent,commitments);
        public PlayableAiOpeningCompositionState Restore()=>new PlayableAiOpeningCompositionState(OwnerId,MatchSeed,ProfileIdentity,SourceIdentity,PersonalitySeed,Opening,Phase,new PlayableAiCompositionIntent(Intent.Explorer,Intent.Tank,Intent.ExplorerShare),commitments);
        public bool SemanticallyEquals(PlayableAiOpeningCompositionState other)=>other!=null&&OwnerId==other.OwnerId&&MatchSeed==other.MatchSeed&&ProfileIdentity==other.ProfileIdentity&&SourceIdentity==other.SourceIdentity&&PersonalitySeed==other.PersonalitySeed&&Opening==other.Opening&&InitialOpening==other.InitialOpening&&Phase==other.Phase&&Intent.SemanticallyEquals(other.Intent)&&commitments.SequenceEqual(other.commitments);
    }

    // Retained opening choices/weights; runtime identity and RNG use the native AI profile.
    public static class PlayableAiOpeningComposition
    {
        public static string SourceProfileIdentity=>ProfileBinding(AiProfile.Initial);
        public const string SourceIdentity="native-strategic-ai:opening-v1";
        public static string ProfileBinding(AiProfile profile)=>profile.Id+"@"+profile.Revision.ToString(CultureInfo.InvariantCulture)+":"+profile.Hash;
        // Genesis binding survives profile apply: existing personality/commitments are not rerolled.
        // The wire already stores this identity and seed, so no layout/schema change is needed.
        public static bool HasNativeBinding(PlayableAiOpeningCompositionState state)
        {
            if(state==null||state.SourceIdentity!=SourceIdentity||string.IsNullOrWhiteSpace(state.OwnerId))return false;
            string key=state.ProfileIdentity;int colon=key.LastIndexOf(':');int at=colon<0?-1:key.LastIndexOf('@',colon);
            if(at<1||colon<=at+1||!int.TryParse(key.Substring(at+1,colon-at-1),NumberStyles.None,CultureInfo.InvariantCulture,out int revision)||revision<1)return false;
            var hash=key.Substring(colon+1);if(hash.Length!=64||hash.Any(c=>!(c>='0'&&c<='9'||c>='a'&&c<='f')))return false;
            return state.PersonalitySeed==AiRandom.Key(state.MatchSeed,state.OwnerId,key,"personality",0);
        }
        public static IReadOnlyList<PlayableAiOpening> Supported=>Array.AsReadOnly(AiOpeningCatalog.All.Select(d=>d.Opening).ToArray());
        public static PlayableAiOpeningCompositionState Initialize(int matchSeed,string ownerId,string sourceProfileIdentity=null,AiProfile aiProfile=null,PlayableAiOpening? forcedOpening=null)
        {
            if(String.IsNullOrEmpty(ownerId))throw new ArgumentException("Owner identity is required.",nameof(ownerId));
            var ai=aiProfile??AiProfile.Initial;var binding=ProfileBinding(ai);
            if(sourceProfileIdentity!=null&&sourceProfileIdentity!=binding)throw new ArgumentException("Native AI profile identity mismatch.",nameof(sourceProfileIdentity));
            var personalitySeed=AiRandom.Key(matchSeed,ownerId,binding,"personality",0);
            var weights=AiOpeningCatalog.All.Select(d=>ai.Value(d.WeightPath)*(.5+Random(personalitySeed,"trait:"+d.Id))).ToArray();
            var pick=Random(personalitySeed,"opening")*weights.Sum();var cursor=0d;var selected=weights.Length-1;
            for(var i=0;i<weights.Length;i++){cursor+=weights[i];if(pick<cursor){selected=i;break;}}
            var definition=forcedOpening.HasValue?AiOpeningCatalog.For(forcedOpening.Value):AiOpeningCatalog.All[selected];
            return new PlayableAiOpeningCompositionState(ownerId,matchSeed,binding,SourceIdentity,personalitySeed,forcedOpening??definition.Opening,PlayableAiOpeningPhase.Active,new PlayableAiCompositionIntent(1,1,.5),definition.Milestones.Select(m=>m.Id));
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
        public PlayableAiOpeningCompositionState Initialize(int matchSeed,string ownerId,string sourceProfileIdentity=null,AiProfile aiProfile=null)
        {
            var state=PlayableAiOpeningComposition.Initialize(matchSeed,ownerId,sourceProfileIdentity,aiProfile);states[ownerId]=state;return state;
        }
        public PlayableAiOpeningCompositionState ForOwner(string ownerId)=>ownerId!=null&&states.TryGetValue(ownerId,out var state)?state:null;
        public IReadOnlyList<PlayableAiOpeningCompositionState> Capture()=>states.Values.OrderBy(x=>x.OwnerId,StringComparer.Ordinal).Select(x=>x.Restore()).ToArray();
        public void Restore(IEnumerable<PlayableAiOpeningCompositionState> snapshot)
        {
            states.Clear();foreach(var state in snapshot??Enumerable.Empty<PlayableAiOpeningCompositionState>()){if(state==null||states.ContainsKey(state.OwnerId))throw new ArgumentException("Snapshot must contain one valid state per owner.");states.Add(state.OwnerId,state.Restore());}
        }
    }
}
