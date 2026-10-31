using System;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Runtime
{
    public enum AiPersonality { Defensive, Aggressive, Harassing }
    public sealed partial class AiDoctrineState
    {
        public AiPersonality Personality;
        public int PersonalityChanges,CompositionChanges;
        public long PersonalityChangedTick,NextCompositionChangeTick,LastCompositionChangeTick;
        public bool RandomScheduleChosen,SecondCompositionChange;
        public AiDoctrine PendingDoctrine;
    }
    public sealed partial class AiDoctrinePolicy
    {
        private bool personalityChosen,scheduleChosen,secondChange;
        private AiPersonality personality;
        private int personalityChanges,compositionChanges;
        private long personalityChanged,nextCompositionChange=-1,lastCompositionChange,lastThreat=-1;
        private AiDoctrine pendingDoctrine;
        internal long SpendableCredits{get;set;}
        public AiPersonality Personality=>personality;
        public bool AirOnly=>chosen&&doctrine==AiDoctrine.Air;
        internal bool HarassAllowed(AiDifficulty difficulty)=>difficulty!=AiDifficulty.Recruit&&(personality==AiPersonality.Harassing||AirOnly);
        public static AiPersonality InitialPersonality(PlayableAiOpeningCompositionState opening,AiProfile ai,AiDifficulty difficulty)
        {
            double[] weights={V(ai,"personality.defensive"),V(ai,"personality.aggressive"),difficulty==AiDifficulty.Recruit?0:V(ai,"personality.harassing")};
            double pick=AiRandom.Key(opening.MatchSeed,opening.OwnerId,opening.ProfileIdentity,"personality-kind",0)/4294967296d*weights.Sum(),sum=0;
            for(int i=0;i<weights.Length;i++){sum+=weights[i];if(pick<sum)return (AiPersonality)i;}
            return AiPersonality.Defensive;
        }
        private static bool CriticalDefense(PlayableAiObservation o,PlayableProfile p)=>o.OwnCenterDamage.Count>0||AiDefensePlanner.Threats(o,p).Length>0;
        private void ObservePersonality(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty difficulty)
        {
            if(!personalityChosen){personalityChosen=true;personality=InitialPersonality(opening,ai,difficulty);personalityChanged=o.Tick;}
            if(CriticalDefense(o,p)){lastThreat=o.Tick;return;} // Emergency defense is tactical and spends no random composition event.
            if(personalityChanges>=V(ai,"personality.maximumChanges")||o.Tick-personalityChanged<AiProfile.SecondsToTicks(V(ai,"personality.holdSeconds"),30))return;
            AiPersonality? next=null;
            if(lastThreat>=personalityChanged&&personality==AiPersonality.Defensive&&o.Entities.Count(u=>u.Owner==o.Owner&&u.Health>0)>=ai.Value("armies.minimumUnits"))next=AiPersonality.Aggressive;
            else if(o.Entities.Any(u=>o.IsHostile(u.Owner)&&u.Health>0)&&AiDefensePlanner.Force(o.Entities.Where(u=>o.IsHostile(u.Owner)&&u.Health>0).ToArray(),p,ai)>AiDefensePlanner.Force(o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0).ToArray(),p,ai)*ai.Value("armies.retreatThreatRatio"))
                next=difficulty==AiDifficulty.Recruit?AiPersonality.Defensive:AiPersonality.Harassing;
            if(next.HasValue&&next.Value!=personality){personality=next.Value;personalityChanges++;personalityChanged=o.Tick;}
        }
        private AiDoctrine DifferentDoctrine(AiProfile ai,string key,int ordinal)
        {
            bool combined=mixed||airOpening;
            var kinds=combined?new[]{AiDoctrine.HawksTanks,AiDoctrine.HawksExplorers,AiDoctrine.BalancedSmallAir,AiDoctrine.HeavySmallAir}:new[]{AiDoctrine.Balanced,AiDoctrine.Tanks,AiDoctrine.Heavy,AiDoctrine.Explorers};
            var keys=combined?new[]{"combined.hawksTanks","combined.hawksExplorers","combined.balancedSmallAir","combined.heavySmallAir"}:new[]{"ground.balanced","ground.tanks","ground.heavy","ground.explorers"};
            double total=Enumerable.Range(0,kinds.Length).Where(i=>kinds[i]!=doctrine).Sum(i=>V(ai,keys[i]));
            if(total<=0)return doctrine; // Disabled alternatives create no random transition.
            double pick=Roll(key,ordinal)*total,sum=0;
            for(int i=0;i<kinds.Length;i++){if(kinds[i]==doctrine)continue;sum+=V(ai,keys[i]);if(pick<sum)return kinds[i];}
            return Enumerable.Range(0,kinds.Length).Where(i=>kinds[i]!=doctrine&&V(ai,keys[i])>0).Select(i=>kinds[i]).Last();
        }
        private bool TransitionAvailable(PlayableAiObservation o,PlayableProfile p,AiDoctrine next)
        {
            bool combined=(int)next>=(int)AiDoctrine.HawksTanks;
            bool Factory(PlayableBuildingKind kind)=>o.Buildings.Any(b=>b.Owner==o.Owner&&Alive(b)&&b.Phase==ConstructionPhase.Ready&&b.Kind==kind);
            if(!Factory(PlayableBuildingKind.Factory)||combined&&!Factory(PlayableBuildingKind.Hangar))return false;
            var kinds=next==AiDoctrine.Explorers||next==AiDoctrine.HawksExplorers?new[]{PlayableEntityKind.Explorer}:new[]{PlayableEntityKind.Tank};
            return kinds.Any(kind=>SpendableCredits>=PlayableUnitRules.Cost(p,kind)&&AiEconomyAdmission.Producers(o,AiRosterCatalog.Initial.For(kind)).Any(b=>AiEconomyAdmission.Reject(o,p,new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableUnitRules.ProductionCommand(kind),new[]{b.Id},unitKind:kind,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity))==null));
        }
        private void ObserveAdaptation(PlayableAiObservation o,PlayableProfile p,AiProfile ai)
        {
            if(!chosen)return;
            if(!scheduleChosen)
            {
                scheduleChosen=true;secondChange=Roll("second-composition-change")<V(ai,"adaptation.secondProbability");
                if(Roll("composition-change-enabled")<V(ai,"adaptation.probability"))
                {
                    double delay=V(ai,"adaptation.firstMinSeconds")+Roll("first-composition-delay")*(V(ai,"adaptation.firstMaxSeconds")-V(ai,"adaptation.firstMinSeconds"));
                    pendingDoctrine=DifferentDoctrine(ai,"composition-change",0);nextCompositionChange=pendingDoctrine==doctrine?-1:checked(chosenTick+AiProfile.SecondsToTicks(delay,30));
                }
            }
            if(nextCompositionChange<0||o.Tick<nextCompositionChange||CriticalDefense(o,p)||!TransitionAvailable(o,p,pendingDoctrine))return;
            if(pendingDoctrine==doctrine){nextCompositionChange=-1;return;}
            doctrine=pendingDoctrine;mixed=(int)doctrine>=(int)AiDoctrine.HawksTanks;compositionChanges++;lastCompositionChange=o.Tick;
            // Two event slots are the agreed first event plus one conditional successor.
            if(compositionChanges==1&&secondChange)
            {pendingDoctrine=DifferentDoctrine(ai,"composition-change",1);nextCompositionChange=pendingDoctrine==doctrine?-1:checked(o.Tick+AiProfile.SecondsToTicks(V(ai,"adaptation.secondMinSeconds"),30));}
            else nextCompositionChange=-1;
        }
        private AiDoctrineState BindPersonality(AiDoctrineState s)
        {s.Personality=personality;s.PersonalityChanges=personalityChanges;s.CompositionChanges=compositionChanges;s.PersonalityChangedTick=personalityChanged;s.NextCompositionChangeTick=nextCompositionChange;s.LastCompositionChangeTick=lastCompositionChange;s.RandomScheduleChosen=scheduleChosen;s.SecondCompositionChange=secondChange;s.PendingDoctrine=pendingDoctrine;return s;}
        private void WritePersonality(BinaryWriter w)
        {w.Write(personalityChosen);w.Write(scheduleChosen);w.Write(secondChange);w.Write((int)personality);w.Write(personalityChanges);w.Write(compositionChanges);w.Write(personalityChanged);w.Write(nextCompositionChange);w.Write(lastCompositionChange);w.Write(lastThreat);w.Write((int)pendingDoctrine);}
        private void ReadPersonality(BinaryReader r,long tick)
        {
            personalityChosen=WorldWire.Boolean(r);scheduleChosen=WorldWire.Boolean(r);secondChange=WorldWire.Boolean(r);personality=WorldWire.EnumValue<AiPersonality>(r);personalityChanges=r.ReadInt32();compositionChanges=r.ReadInt32();personalityChanged=r.ReadInt64();nextCompositionChange=r.ReadInt64();lastCompositionChange=r.ReadInt64();lastThreat=r.ReadInt64();pendingDoctrine=WorldWire.EnumValue<AiDoctrine>(r);
            AiStateWire.Require(personalityChanges>=0&&personalityChanges<=AiProfileMetadata.Field("openingsMidgame.personality.maximumChanges").Maximum&&compositionChanges>=0&&compositionChanges<=2&&personalityChanged>=0&&personalityChanged<=tick&&nextCompositionChange>=-1&&lastCompositionChange>=0&&lastCompositionChange<=tick&&lastThreat>=-1&&lastThreat<=tick&&(!scheduleChosen||chosen)&&(!personalityChosen?personalityChanges==0:true),"personality/adaptation values and clocks");
        }
    }
}
