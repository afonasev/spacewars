using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Spacewars.Simulation.Ai
{
    public sealed class AiOwnerConfig
    {
        public string OwnerId {get;} public AiDifficulty Difficulty {get;} public uint PersonalitySeed {get;}
        public AiOwnerConfig(string ownerId,AiDifficulty difficulty,int matchSeed,AiProfile profile)
        {
            if(string.IsNullOrWhiteSpace(ownerId)||profile==null)throw new ArgumentException("Owner/profile required.");
            AiProfile.DifficultyId(difficulty);OwnerId=ownerId;Difficulty=difficulty;
            PersonalitySeed=AiRandom.Key(matchSeed,ownerId,profile.Id+"@"+profile.Revision+":"+profile.Hash,"personality",0);
        }
    }
    // Counter-based samples: callers persist the ordinal in F6; enumeration never advances a shared RNG.
    public static class AiRandom
    {
        public static uint Key(int seed,string owner,string revision,string decision,long ordinal)
        {
            if(string.IsNullOrWhiteSpace(owner)||string.IsNullOrWhiteSpace(revision)||string.IsNullOrWhiteSpace(decision)||ordinal<0)throw new ArgumentException("Invalid PRNG key.");
            var hash=AiProfile.Digest(seed.ToString(CultureInfo.InvariantCulture)+"\n"+owner.Length+":"+owner+"\n"+revision.Length+":"+revision+"\n"+decision.Length+":"+decision+"\n"+ordinal.ToString(CultureInfo.InvariantCulture));
            return uint.Parse(hash.Substring(0,8),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        }
    }
    public static class AiMatchSeed
    {
        // A randomly selected process origin plus atomic ordinal guarantees distinct ordinary starts in this process.
        // Technical entropy identity, not a tunable gameplay parameter. Explicit seeds bypass it unchanged.
        private static int next=BitConverter.ToInt32(Guid.NewGuid().ToByteArray(),0);
        public static int Resolve(int? explicitSeed=null)=>explicitSeed??Interlocked.Increment(ref next);
    }
    public sealed class AiMatchIdentity
    {
        public string CodeRevision {get;} public string MapId {get;} public string MapHash {get;}
        public string GameplayProfileId {get;} public int GameplayRevision {get;} public string GameplayHash {get;}
        public string AiProfileId {get;} public int AiRevision {get;} public string AiHash {get;} public string RosterHash {get;}
        public int MatchSeed {get;} public string OwnerId {get;} public string TeamId {get;} public AiDifficulty Difficulty {get;} public long Generation {get;}
        public AiMatchIdentity(string codeRevision,string mapId,string mapHash,PlayableProfile gameplay,AiProfile ai,AiRosterCatalog catalog,int seed,string ownerId,string teamId,AiDifficulty difficulty,long generation)
        {
            if(new[]{codeRevision,mapId,mapHash,ownerId,teamId}.Any(string.IsNullOrWhiteSpace)||gameplay==null||ai==null||catalog==null||generation<1)throw new ArgumentException("Incomplete match identity.");
            AiProfile.DifficultyId(difficulty);CodeRevision=codeRevision;MapId=mapId;MapHash=mapHash;GameplayProfileId=gameplay.ProfileId;GameplayRevision=gameplay.Revision;
            GameplayHash=GameplayDigest(gameplay);AiProfileId=ai.Id;AiRevision=ai.Revision;AiHash=ai.Hash;RosterHash=catalog.Hash;MatchSeed=seed;OwnerId=ownerId;TeamId=teamId;Difficulty=difficulty;Generation=generation;
        }
        private static string Format(object value)=>value is double number?number.ToString("R",CultureInfo.InvariantCulture):Convert.ToString(value,CultureInfo.InvariantCulture);
        public static string GameplayDigest(PlayableProfile profile)
        {
            var data=profile.CopyData();return AiProfile.Digest(string.Join("\n",typeof(PlayableProfileData).GetFields(BindingFlags.Public|BindingFlags.Instance).OrderBy(f=>f.Name,StringComparer.Ordinal).Select(f=>f.Name+"="+Format(f.GetValue(data)))));
        }
    }
}
