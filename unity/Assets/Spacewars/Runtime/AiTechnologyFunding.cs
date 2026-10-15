using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // State lives in one typed, bounded existing arbiter-history record and the shared
    // ledger. No new world/wire fields, speculative credit or ordinary-command metadata.
    public sealed class AiTechnologyFunding
    {
        public const string Purpose="technology-funding";
        private const int Magic=0x54474632;
        private readonly string owner;private readonly long generation;
        private enum Stage { Science, AwaitingScience, Research }
        private sealed class Memo
        {
            internal string Owner,Metadata,BuildIntent,BuildTerms;internal AiReceiptIdentity BuildReceipt;internal int BuildAmount;internal long Generation,Created,Expires;
            internal PlayableResearchKind Branch;internal Stage Phase;internal int Site,Slot,Parent,Center,Amount,Cash,ScienceCost,ResearchCost;
            internal double Income,Progress;internal bool Expired,MetadataChanged;
        }
        public AiTechnologyFunding(string owner,long generation){this.owner=owner??throw new ArgumentNullException(nameof(owner));this.generation=generation;}
        public static bool IsFunding(AiBudgetEntry e)=>e!=null&&e.Purpose==Purpose;
        private static string Terms(PlayableProfile p)=>p.ProfileId+"@"+p.Revision;
        private static string Metadata(PlayableProfile p,AiProfile ai)=>Terms(p)+":"+ai.Hash;
        private static string Id(Memo m)=>Purpose+":"+m.Phase+":"+m.Branch+":"+m.Site+":"+m.Slot+":"+m.Parent;
        private static bool ParseId(string id,out Stage stage,out PlayableResearchKind branch,out int site,out int slot,out int parent)
        {
            stage=default;branch=default;site=slot=parent=0;var x=(id??"").Split(':');
            return x.Length==6&&x[0]==Purpose&&Enum.TryParse(x[1],out stage)&&stage.ToString()==x[1]&&Enum.IsDefined(typeof(Stage),stage)&&Enum.TryParse(x[2],out branch)&&branch.ToString()==x[2]&&Enum.IsDefined(typeof(PlayableResearchKind),branch)&&
                int.TryParse(x[3],out site)&&site>0&&int.TryParse(x[4],out slot)&&slot>0&&int.TryParse(x[5],out parent)&&parent>0;
        }
        private static Memo Load(AiDecisionArbiter arbiter)
        {
            string payload=arbiter.FundingMemory(out var created,out var expires);return payload==null?null:Decode(payload,created,expires);
        }
        private static Memo Decode(string payload,long created,long expires)
        {
            if(payload.Length>2048)throw new ArgumentException("Unbounded technology funding memo.");
            using(var stream=new MemoryStream(Convert.FromBase64String(payload)))using(var r=new BinaryReader(stream))
            {
                if(r.ReadInt32()!=Magic)throw new ArgumentException("Unsupported technology funding memo.");
                var m=new Memo{Owner=r.ReadString(),Generation=r.ReadInt64(),Branch=(PlayableResearchKind)r.ReadInt32(),Phase=(Stage)r.ReadInt32(),Site=r.ReadInt32(),Slot=r.ReadInt32(),Parent=r.ReadInt32(),Center=r.ReadInt32(),Amount=r.ReadInt32(),Cash=r.ReadInt32(),ScienceCost=r.ReadInt32(),ResearchCost=r.ReadInt32(),Income=r.ReadDouble(),Progress=r.ReadDouble(),Expired=WorldWire.Boolean(r),MetadataChanged=WorldWire.Boolean(r),Metadata=r.ReadString(),Created=created,Expires=expires};
                m.BuildIntent=WorldWire.String(r);m.BuildTerms=WorldWire.String(r);m.BuildAmount=r.ReadInt32();m.BuildReceipt=AiStateWire.ReadAiReceiptIdentity(r);
                if(stream.Position!=stream.Length||!Enum.IsDefined(typeof(Stage),m.Phase)||!Enum.IsDefined(typeof(PlayableResearchKind),m.Branch)||string.IsNullOrWhiteSpace(m.Owner)||string.IsNullOrWhiteSpace(m.Metadata)||m.Site<1||m.Slot<1||m.Parent<1||m.Center<0||m.Amount<0||m.Cash<0||m.ScienceCost<0||m.ResearchCost<0||double.IsNaN(m.Income)||double.IsInfinity(m.Income)||m.Income<0||double.IsNaN(m.Progress)||double.IsInfinity(m.Progress)||m.Progress<0||m.Progress>1||m.Created<0||m.Expires<m.Created||m.Expired&&m.Amount!=0)throw new ArgumentException("Invalid technology funding memo.");
                AiStateWire.Require(m.BuildAmount>=0&&(m.BuildReceipt==null?m.BuildIntent==null&&m.BuildTerms==null&&m.BuildAmount==0:!string.IsNullOrWhiteSpace(m.BuildIntent)&&!string.IsNullOrWhiteSpace(m.BuildTerms)&&m.BuildReceipt.OwnerId==m.Owner&&m.BuildReceipt.Generation==m.Generation),"technology science receipt identity");
                AiStateWire.Require(m.Phase!=Stage.AwaitingScience||m.BuildReceipt!=null,"awaiting science without accepted receipt");
                AiStateWire.Require(m.Phase==Stage.Science?m.Center==0&&m.Progress==0&&m.BuildReceipt==null:m.Phase==Stage.AwaitingScience?m.Center==0&&m.Progress==0:m.Center>0,"technology stage/center binding");
                return m;
            }
        }
        internal static void ValidateMemory(string payload,long created,long expires,long tick,string owner,long generation,PlayableProfile p,AiProfile ai)
        {
            try{var m=Decode(payload,created,expires);AiStateWire.Require(m.Owner==owner&&m.Generation==generation&&m.Created<=tick&&m.Expires-m.Created<=AiProfile.SecondsToTicks(ai.Value("economy.reservationExpirySeconds"),30)&&m.Metadata==Metadata(p,ai)&&m.ScienceCost==TerritoryRules.Cost(p,PlayableBuildingKind.ScientificCenter)&&m.ResearchCost==(int)Math.Ceiling(AiResearchCatalog.For(m.Branch).Cost(p)),"technology memo context");}
            catch(Exception e) when(e is FormatException||e is EndOfStreamException||e is IOException){throw new ArgumentException("Invalid technology funding memo.",e);}
        }
        private static void Save(AiDecisionArbiter arbiter,Memo m)
        {
            using(var stream=new MemoryStream()){using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
                {w.Write(Magic);w.Write(m.Owner);w.Write(m.Generation);w.Write((int)m.Branch);w.Write((int)m.Phase);w.Write(m.Site);w.Write(m.Slot);w.Write(m.Parent);w.Write(m.Center);w.Write(m.Amount);w.Write(m.Cash);w.Write(m.ScienceCost);w.Write(m.ResearchCost);w.Write(m.Income);w.Write(m.Progress);w.Write(m.Expired);w.Write(m.MetadataChanged);w.Write(m.Metadata);WorldWire.String(w,m.BuildIntent);WorldWire.String(w,m.BuildTerms);w.Write(m.BuildAmount);AiStateWire.Write(w,m.BuildReceipt);}
                arbiter.FundingMemory(Convert.ToBase64String(stream.ToArray()),m.Created,m.Expires);}
        }
        private static bool ScienceEntry(Memo m,AiBudgetEntry e)=>m.BuildReceipt!=null&&e.Id==m.BuildIntent&&e.Receipt?.Id==m.BuildReceipt.Id&&e.Purpose=="economy"&&e.Amount==m.BuildAmount&&e.Terms==m.BuildTerms&&!e.AwaitingPayment&&e.Action.PlayerId==m.Owner&&e.Action.Generation==m.Generation&&e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.ScientificCenter&&e.Action.SiteId==m.Site&&e.Action.SlotId==m.Slot&&e.Action.ParentId==m.Parent;
#if UNITY_INCLUDE_TESTS
        internal static Action<string,long,string,string,string> CurrentSafeFundingObserveForTests;
        internal static string CurrentSafeFundingObserveOwnerForTests;
        internal static Action<string,long,string,string,string> CurrentSafeFundingMutationReadbackForTests;
        internal static string CurrentSafeFundingMutationOwnerForTests;
        private static string DiagnosticMemo(Memo m)=>m==null?null:$"{m.Phase}/{m.Branch}/site={m.Site}/slot={m.Slot}/parent={m.Parent}/center={m.Center}/amount={m.Amount}/created={m.Created}/expires={m.Expires}/cash={m.Cash}/income={m.Income:R}/progress={m.Progress:R}/expired={m.Expired}";
        private bool MutationReadbacksEnabled=>CurrentSafeFundingMutationReadbackForTests!=null&&(CurrentSafeFundingMutationOwnerForTests==null||CurrentSafeFundingMutationOwnerForTests==owner);
        private void FundingMutation(string stage,long tick,string beforeMemo,string afterMemo,string detail)
        {if(CurrentSafeFundingMutationReadbackForTests!=null&&(CurrentSafeFundingMutationOwnerForTests==null||CurrentSafeFundingMutationOwnerForTests==owner))CurrentSafeFundingMutationReadbackForTests(owner,tick,beforeMemo,afterMemo,stage+";"+detail);}
#endif
        private static bool AcceptedScience(Memo m,AiBudgetLedger b)=>b.Capture().Unpaid.Concat(b.Capture().Paid).Any(e=>ScienceEntry(m,e));
        private static bool PaidScience(Memo m,AiBudgetLedger b)=>b.Capture().Paid.Any(e=>ScienceEntry(m,e));
        public PlayableResearchKind? Preferred(AiDecisionArbiter a)=>Load(a)?.Branch;
        public bool HasHold(AiBudgetLedger b)=>b.HasReservationPurpose(Purpose);
        private static PlayableBuildingSnapshot Center(Memo m,PlayableAiObservation o)=>o.Buildings.FirstOrDefault(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.ScientificCenter&&b.SiteId==m.Site&&b.SlotId==m.Slot&&b.ParentId==m.Parent&&(b.Health>0||b.Phase==ConstructionPhase.Pending||b.Phase==ConstructionPhase.Constructing&&b.Progress==0)&&b.PrivateState?.Lifecycle?.Selling!=true&&(m.Center==0||b.Id==m.Center));
        private PlayableAiAction Desired(Memo m,PlayableAiObservation o,PlayableProfile p)=>new PlayableAiAction(1,owner,p.ProfileId,p.Revision,generation,o.SnapshotSequence,
            m.Phase==Stage.Research?PlayableCommandKind.QueueResearch:PlayableCommandKind.BuildAt,m.Phase==Stage.Research?new[]{m.Center}:Array.Empty<int>(),
            siteId:m.Phase==Stage.Research?0:m.Site,slotId:m.Phase==Stage.Research?0:m.Slot,parentId:m.Phase==Stage.Research?0:m.Parent,buildingKind:m.Phase==Stage.Research?PlayableBuildingKind.Headquarters:PlayableBuildingKind.ScientificCenter,
            researchKind:m.Phase==Stage.Research?m.Branch:PlayableResearchKind.TankChassis,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        private static int Required(Memo m)=>checked(m.ResearchCost+(m.Phase==Stage.Science?m.ScienceCost:0));
        private AiBudgetEntry Entry(Memo m,PlayableAiAction action,PlayableProfile p)=>new AiBudgetEntry(Id(m),Purpose,action,m.Amount,1,m.Created,m.Expires,"actual technology payment / target loss / no-progress expiry",Array.Empty<string>(),Terms(p));
        public static long CoveredAmount(AiBudgetState b,PlayableResearchKind kind,PlayableProfile p)=>b.Reserved.Where(e=>IsFunding(e)&&ParseId(e.Id,out _,out var branch,out _,out _,out _)&&branch==kind&&e.Action.PlayerId==b.OwnerId&&e.Action.Generation==b.Generation&&e.Action.ProfileId==p.ProfileId&&e.Action.ProfileRevision==p.Revision&&e.Terms==Terms(p)).Sum(e=>(long)e.Amount);
        private static bool Target(Memo m,PlayableAiAction a)=>a.PlayerId==m.Owner&&a.Generation==m.Generation&&
            (m.Phase==Stage.Science?a.Kind==PlayableCommandKind.BuildAt&&a.BuildingKind==PlayableBuildingKind.ScientificCenter&&a.SiteId==m.Site&&a.SlotId==m.Slot&&a.ParentId==m.Parent&&a.EntityIds.Count==0:
                m.Phase==Stage.Research&&a.Kind==PlayableCommandKind.QueueResearch&&a.ResearchKind==m.Branch&&a.EntityIds.SequenceEqual(new[]{m.Center}));
        public static bool Matches(AiIntent i,AiBudgetEntry e,AiDecisionArbiter arbiter)
        {
            if(!IsFunding(e))return false;var m=Load(arbiter);
            return m!=null&&!m.Expired&&m.Phase!=Stage.AwaitingScience&&i.ReservationId==e.Id&&e.Id==Id(m)&&e.Amount==m.Amount&&m.Amount==Required(m)&&i.StartupFund==0&&
                i.Policy==(m.Phase==Stage.Science?"economy":"research")&&i.Credits==(m.Phase==Stage.Science?m.ScienceCost:m.ResearchCost)&&Target(m,i.Action)&&Target(m,e.Action)&&
                i.Action.ProfileId==e.Action.ProfileId&&i.Action.ProfileRevision==e.Action.ProfileRevision&&i.Action.Seed==e.Action.Seed&&i.Action.SourceIdentity==e.Action.SourceIdentity&&e.Terms==i.Action.ProfileId+"@"+i.Action.ProfileRevision;
        }
        public string ReservationFor(PlayableAiAction a,AiBudgetLedger b,AiDecisionArbiter arbiter)
        {var m=Load(arbiter);return m!=null&&!m.Expired&&m.Amount==Required(m)&&Target(m,a)&&b.ReservationEntry(Id(m))!=null?Id(m):null;}
        public void Reconcile(PlayableAiObservation o,AiBudgetLedger b,AiDecisionArbiter a)
        {
#if UNITY_INCLUDE_TESTS
            var m=Load(a);
            if(MutationReadbacksEnabled)
            {
                bool hasHold=false;bool holdEvaluated=false;bool reconcile=false;
                if(m!=null&&!m.Expired&&m.Amount>0){hasHold=HasHold(b);holdEvaluated=true;reconcile=!hasHold;}
                string before=DiagnosticMemo(m);string ledgerBefore=string.Join(";",b.Capture().Reserved.Where(IsFunding).Select(e=>$"{e.Id}:{e.Amount}@{e.CreatedTick}-{e.ExpiresTick}"));
                if(reconcile)Suppress(o,b,a,false,"reconcile-missing-hold");
                FundingMutation("Reconcile",o.Tick,before,DiagnosticMemo(Load(a)),$"condition={reconcile};hasHold={(holdEvaluated?hasHold.ToString():"NotReached")};beforeLedger={ledgerBefore};afterLedger={string.Join(";",b.Capture().Reserved.Where(IsFunding).Select(e=>$"{e.Id}:{e.Amount}@{e.CreatedTick}-{e.ExpiresTick}"))}");
            }
            else if(m!=null&&!m.Expired&&m.Amount>0&&!HasHold(b))Suppress(o,b,a,false);
#else
            var m=Load(a);if(m!=null&&!m.Expired&&m.Amount>0&&!HasHold(b))Suppress(o,b,a,false);
#endif
        }
        public void Suppress(PlayableAiObservation o,AiBudgetLedger b,AiDecisionArbiter arbiter,bool consumeProgress=true)
        {
#if UNITY_INCLUDE_TESTS
            SuppressObserved(o,b,arbiter,consumeProgress,"explicit-suppress");
#else
            var m=Load(arbiter);foreach(var e in b.Capture().Reserved.Where(IsFunding))b.Release(e.Id);
            if(m==null)return;m.Expired=true;m.Amount=0;if(consumeProgress){m.Cash=o.Credits;m.Income=o.SettledIncome;}Save(arbiter,m);
#endif
        }
#if UNITY_INCLUDE_TESTS
        internal void Suppress(PlayableAiObservation o,AiBudgetLedger b,AiDecisionArbiter arbiter,bool consumeProgress,string reason)
        {SuppressObserved(o,b,arbiter,consumeProgress,reason);}
        private void SuppressObserved(PlayableAiObservation o,AiBudgetLedger b,AiDecisionArbiter arbiter,bool consumeProgress,string reason)
        {
            bool diagnosticEnabled=MutationReadbacksEnabled;
            string beforeMemo=diagnosticEnabled?DiagnosticMemo(Load(arbiter)):null;
            string ledgerBefore=diagnosticEnabled?string.Join(";",b.Capture().Reserved.Where(IsFunding).Select(e=>$"{e.Id}:{e.Amount}@{e.CreatedTick}-{e.ExpiresTick}")):null;
            var m=Load(arbiter);foreach(var e in b.Capture().Reserved.Where(IsFunding))b.Release(e.Id);
            if(m!=null){m.Expired=true;m.Amount=0;if(consumeProgress){m.Cash=o.Credits;m.Income=o.SettledIncome;}Save(arbiter,m);}
            if(diagnosticEnabled)FundingMutation("Suppress",o.Tick,beforeMemo,DiagnosticMemo(Load(arbiter)),"reason="+reason+";consumeProgress="+consumeProgress+";beforeLedger="+ledgerBefore+";afterLedger="+string.Join(";",b.Capture().Reserved.Where(IsFunding).Select(e=>$"{e.Id}:{e.Amount}@{e.CreatedTick}-{e.ExpiresTick}")));
        }
#endif
        // Called only at a real owner decision. Existing cash released by our own expiry
        // changes Available, never raw Liquid/settled income, and cannot revive this memo.
        public void Observe(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiBudgetLedger b,AiDecisionArbiter arbiter,AiTechnologyPlan plan,bool allowed,int protectedFunds=0,int discretionaryPriorityFunds=0)
        {
            if(o.OwnerId!=owner||o.Generation!=generation||b.OwnerId!=owner||b.Generation!=generation)throw new ArgumentException("Foreign technology funding context.");
            var m=Load(arbiter);
#if UNITY_INCLUDE_TESTS
            bool diagnosticEnabled=CurrentSafeFundingObserveForTests!=null&&(CurrentSafeFundingObserveOwnerForTests==null||CurrentSafeFundingObserveOwnerForTests==owner);
            var diagnosticBefore=diagnosticEnabled?DiagnosticMemo(m):null;var ledgerBefore=diagnosticEnabled?b.Capture().Reserved.Where(IsFunding).Select(e=>$"{e.Id}:{e.Amount}@{e.CreatedTick}-{e.ExpiresTick}").ToArray():null;
            var diagnosticOutcome="reached";string diagnosticFreshCash="not-evaluated",diagnosticWorldProgress="not-evaluated",diagnosticPending="not-evaluated",diagnosticLostTarget="not-evaluated",diagnosticReplace="not-evaluated",diagnosticAmount="not-evaluated",diagnosticProgress="not-evaluated";
            try
            {
#endif
            if(m!=null&&o.OwnerResearch.Any(r=>r.Kind==m.Branch))
            {
#if UNITY_INCLUDE_TESTS
                diagnosticOutcome="completed-research-cleared-funding";
#endif
                foreach(var e in b.Capture().Reserved.Where(IsFunding))b.Release(e.Id);arbiter.ClearFundingMemory();m=null;}
            if(!allowed||plan.FundingGoal==null){
#if UNITY_INCLUDE_TESTS
                diagnosticOutcome=!allowed?"suppressed-funding-guard":"suppressed-no-funding-goal";
#endif
 #if UNITY_INCLUDE_TESTS
                Suppress(o,b,arbiter,true,!allowed?"can-fund-guard":"no-funding-goal");
 #else
                Suppress(o,b,arbiter);
 #endif
                return;}
            if(m!=null&&(m.Owner!=owner||m.Generation!=generation))throw new ArgumentException("Foreign technology funding memo.");
            bool freshCash=m!=null&&(o.Credits>m.Cash||o.SettledIncome>m.Income);
            var center=m==null?null:Center(m,o);bool worldProgress=center!=null&&(m.BuildReceipt==null||PaidScience(m,b))&&(m.Center==0||center.Progress>m.Progress);
#if UNITY_INCLUDE_TESTS
            diagnosticFreshCash=freshCash.ToString();diagnosticWorldProgress=worldProgress.ToString();
#endif
            if(m!=null&&(o.Tick>m.Expires&&!HasHold(b)||m.Expired)&&!freshCash&&!worldProgress)
            {
#if UNITY_INCLUDE_TESTS
                diagnosticOutcome="suppressed-expired-without-progress";
#endif
 #if UNITY_INCLUDE_TESTS
                Suppress(o,b,arbiter,true,"expired-without-cash-or-world-progress");
 #else
                Suppress(o,b,arbiter);
 #endif
                return;}
            // An accepted invoice owns its cost until delivery; new income cannot
            // create a second promise or replace its physical target/branch.
            bool pendingScience=m!=null&&b.Capture().Unpaid.Any(e=>ScienceEntry(m,e));
            bool pendingResearch=m!=null&&b.Capture().Unpaid.Any(e=>e.Purpose=="research"&&e.Action.ResearchKind==m.Branch&&e.Action.EntityIds.SequenceEqual(new[]{m.Center}));
            if(pendingResearch){
#if UNITY_INCLUDE_TESTS
                diagnosticPending="accepted-research-invoice";diagnosticOutcome="pending-research-return";
#endif
                return;}
            bool lostTarget=m!=null&&(m.Phase==Stage.Science?
                AiEconomyAdmission.Reject(o,p,Desired(m,o,p)) is string reason&&reason!="insufficient credits":
                center==null&&!pendingScience);
            bool replace=m!=null&&(m.Branch!=plan.FundingGoal.Research||lostTarget);
 #if UNITY_INCLUDE_TESTS
            diagnosticPending=pendingScience?"accepted-science-invoice":"none";diagnosticLostTarget=lostTarget.ToString();diagnosticReplace=replace.ToString();
 #endif
            if(replace&&!freshCash&&!worldProgress){
#if UNITY_INCLUDE_TESTS
                diagnosticOutcome="suppressed-replacement-without-progress";
#endif
 #if UNITY_INCLUDE_TESTS
                Suppress(o,b,arbiter,true,"replacement-without-cash-or-world-progress");
 #else
                Suppress(o,b,arbiter);
 #endif
                return;}
            if(replace&&pendingScience){
#if UNITY_INCLUDE_TESTS
                diagnosticOutcome="suppressed-replacement-with-pending-science";
#endif
 #if UNITY_INCLUDE_TESTS
                Suppress(o,b,arbiter,true,"replacement-blocked-by-pending-science-invoice");
 #else
                Suppress(o,b,arbiter);
 #endif
                return;}
            if(m==null||replace)
            {
                center=o.Buildings.Where(x=>x.Owner==o.Owner&&x.Kind==PlayableBuildingKind.ScientificCenter&&(x.Health>0||x.Phase==ConstructionPhase.Pending||x.Phase==ConstructionPhase.Constructing&&x.Progress==0)&&x.PrivateState?.Lifecycle?.Selling!=true).OrderBy(x=>x.Id).FirstOrDefault();
                var replacement=new Memo{Owner=owner,Generation=generation,Branch=plan.FundingGoal.Research,Created=o.Tick,Expires=checked(o.Tick+b.ReservationLifetimeTicks),Cash=o.Credits,Income=o.SettledIncome,Metadata=Metadata(p,ai)};
                if(center!=null){replacement.Site=center.SiteId;replacement.Slot=center.SlotId;replacement.Parent=center.ParentId;replacement.Center=center.Id;replacement.Phase=Stage.Research;replacement.Progress=center.Progress;}
                else
                {
                    var target=new AiEconomyPlanner().InfrastructureRequests(o,p,plan.FundingGoal).FirstOrDefault(c=>c.Reason==null||c.Reason=="insufficient credits");if(target==null){
#if UNITY_INCLUDE_TESTS
                        diagnosticOutcome="suppressed-no-science-target";
#endif
 #if UNITY_INCLUDE_TESTS
                        Suppress(o,b,arbiter,true,"no-admissible-science-target");
 #else
                        Suppress(o,b,arbiter);
 #endif
                        return;}
                    replacement.Site=target.Action.SiteId;replacement.Slot=target.Action.SlotId;replacement.Parent=target.Action.ParentId;replacement.Phase=Stage.Science;
                }
                if(replace)foreach(var e in b.Capture().Reserved.Where(IsFunding))b.Release(e.Id);
                m=replacement;
            }
            string previousId=Id(m);var previous=b.ReservationEntry(previousId);
            center=Center(m,o);
            if(m.Phase==Stage.Science)
            {
                var reject=AiEconomyAdmission.Reject(o,p,Desired(m,o,p));if(reject!=null&&reject!="insufficient credits"){
#if UNITY_INCLUDE_TESTS
                    diagnosticOutcome="suppressed-science-admission:"+reject;
#endif
 #if UNITY_INCLUDE_TESTS
                    Suppress(o,b,arbiter,true,"science-target-admission:"+reject);
 #else
                    Suppress(o,b,arbiter);
 #endif
                    return;}
            }
            else if(center==null)
            {
                bool pending=b.Capture().Unpaid.Any(e=>ScienceEntry(m,e));
                if(m.Phase!=Stage.AwaitingScience||!pending){
#if UNITY_INCLUDE_TESTS
                    diagnosticOutcome="suppressed-lost-science-center";
#endif
 #if UNITY_INCLUDE_TESTS
                    Suppress(o,b,arbiter,true,"lost-science-center");
 #else
                    Suppress(o,b,arbiter);
 #endif
                    return;}
            }
            if(center!=null){if(m.BuildReceipt!=null&&!PaidScience(m,b)){
#if UNITY_INCLUDE_TESTS
                diagnosticOutcome="suppressed-unpaid-science-build";
#endif
 #if UNITY_INCLUDE_TESTS
                Suppress(o,b,arbiter,true,"science-build-not-paid");
 #else
                Suppress(o,b,arbiter);
 #endif
                return;}worldProgress|=m.Center==0||center.Progress>m.Progress;m.Phase=Stage.Research;m.Center=center.Id;m.Progress=center.Progress;}
            m.ScienceCost=TerritoryRules.Cost(p,PlayableBuildingKind.ScientificCenter);m.ResearchCost=(int)Math.Ceiling(AiResearchCatalog.For(m.Branch).Cost(p));
            // Metadata/availability changes alone never renew actual-money progress.
            if(previous!=null)b.Release(previousId);
            // Accepted science owns its continuation to research. Q4 can shrink only
            // an unaccepted discretionary hold, in this same memo/ledger transition;
            // amount reduction is not progress and retains all target/receipt clocks.
            int priorityFunds=m.BuildReceipt==null?discretionaryPriorityFunds:0;
            int amount=(int)Math.Min(Required(m),Math.Max(0,(long)b.Available-protectedFunds-priorityFunds));
            bool progress=worldProgress||freshCash&&amount>m.Amount;
 #if UNITY_INCLUDE_TESTS
            diagnosticAmount=amount.ToString();diagnosticProgress=progress.ToString();
 #endif
            if(m.Expired&&!freshCash&&!worldProgress){
#if UNITY_INCLUDE_TESTS
                diagnosticOutcome="suppressed-expired-memo";
#endif
 #if UNITY_INCLUDE_TESTS
                Suppress(o,b,arbiter,true,"expired-memo-without-progress");
 #else
                Suppress(o,b,arbiter);
 #endif
                return;}
            if(progress){m.Created=o.Tick;m.Expires=checked(o.Tick+b.ReservationLifetimeTicks);}
            else m.Expires=Math.Min(m.Expires,checked(m.Created+b.ReservationLifetimeTicks));
            m.Expired=false;m.Amount=amount;m.Cash=o.Credits;m.Income=o.SettledIncome;m.Metadata=Metadata(p,ai);m.MetadataChanged=false;
            if(o.Tick>m.Expires){
#if UNITY_INCLUDE_TESTS
                diagnosticOutcome="memo-expired-no-reserve";
#endif
                m.Expired=true;m.Amount=0;Save(arbiter,m);return;}
            if(!b.Reserve(Entry(m,Desired(m,o,p),p)))throw new InvalidOperationException("Technology goal funds do not fit current ledger.");Save(arbiter,m);
#if UNITY_INCLUDE_TESTS
            diagnosticOutcome=previous!=null?"reallocated-existing-hold":"reserved-new-or-replaced-hold";
#endif
#if UNITY_INCLUDE_TESTS
            }
            finally
            {
                if(diagnosticEnabled)
                    CurrentSafeFundingObserveForTests(owner,o.Tick,diagnosticBefore,DiagnosticMemo(Load(arbiter)), $"outcome={diagnosticOutcome};freshCash={diagnosticFreshCash};worldProgress={diagnosticWorldProgress};pending={diagnosticPending};lostTarget={diagnosticLostTarget};replace={diagnosticReplace};amount={diagnosticAmount};progress={diagnosticProgress};beforeLedger={string.Join(";",ledgerBefore)};afterLedger={string.Join(";",b.Capture().Reserved.Where(IsFunding).Select(e=>$"{e.Id}:{e.Amount}@{e.CreatedTick}-{e.ExpiresTick}"))};cash={o.Credits};income={o.SettledIncome};allowed={allowed};goal={plan.FundingGoal?.Research.ToString()??"none"}");
            }
#endif
        }
        public bool Accept(AiIntent i,AiReceiptIdentity receipt,PlayableAiObservation o,PlayableProfile p,AiBudgetLedger b,AiDecisionArbiter arbiter)
        {
#if UNITY_INCLUDE_TESTS
            bool diagnosticEnabled=MutationReadbacksEnabled;
            string beforeMemo=diagnosticEnabled?DiagnosticMemo(Load(arbiter)):null;
            string beforeLedger=diagnosticEnabled?string.Join(";",b.Capture().Reserved.Where(IsFunding).Select(e=>$"{e.Id}:{e.Amount}@{e.CreatedTick}-{e.ExpiresTick}")):null;
#endif
            var hold=b.ReservationEntry(i.ReservationId);if(!Matches(i,hold,arbiter))
            {
#if UNITY_INCLUDE_TESTS
                if(diagnosticEnabled)FundingMutation("Accept",o.Tick,beforeMemo,DiagnosticMemo(Load(arbiter)),$"accepted=false;reason=intent-or-hold-mismatch;intent={i.Id};reservation={i.ReservationId};receipt={receipt?.Id};beforeLedger={beforeLedger};afterLedger={string.Join(";",b.Capture().Reserved.Where(IsFunding).Select(e=>$"{e.Id}:{e.Amount}@{e.CreatedTick}-{e.ExpiresTick}"))}");
#endif
                return false;
            }
            var m=Load(arbiter);
            var accepted=new AiBudgetEntry(i.Id,i.Policy,i.Action,i.Credits,i.Priority,o.Tick,i.ExpiresTick,"terminal receipt / invalid target / expiry",i.Claims,Terms(p),receipt);
            AiBudgetEntry remainder=null;
            if(m.Phase==Stage.Science)
            {m.BuildIntent=accepted.Id;m.BuildReceipt=receipt;m.BuildAmount=accepted.Amount;m.BuildTerms=accepted.Terms;m.Phase=Stage.AwaitingScience;m.Amount=m.ResearchCost;remainder=Entry(m,Desired(m,o,p),p);}
            if(!b.AcceptReserved(accepted,hold.Id,remainder))
            {
#if UNITY_INCLUDE_TESTS
                if(diagnosticEnabled)FundingMutation("Accept",o.Tick,beforeMemo,DiagnosticMemo(Load(arbiter)),$"accepted=false;reason=ledger-rejected;intent={i.Id};amount={accepted.Amount};receipt={receipt?.Id};beforeLedger={beforeLedger};afterLedger={string.Join(";",b.Capture().Reserved.Where(IsFunding).Select(e=>$"{e.Id}:{e.Amount}@{e.CreatedTick}-{e.ExpiresTick}"))}");
#endif
                return false;
            }
            if(remainder==null){m.Amount=0;m.Expired=true;m.Cash=o.Credits;m.Income=o.SettledIncome;}Save(arbiter,m);
#if UNITY_INCLUDE_TESTS
            if(diagnosticEnabled)FundingMutation("Accept",o.Tick,beforeMemo,DiagnosticMemo(Load(arbiter)),$"accepted=true;intent={i.Id};purpose={accepted.Purpose};amount={accepted.Amount};remainder={remainder?.Amount.ToString()??"none"};receipt={receipt?.Id};beforeLedger={beforeLedger};afterLedger={string.Join(";",b.Capture().Reserved.Where(IsFunding).Select(e=>$"{e.Id}:{e.Amount}@{e.CreatedTick}-{e.ExpiresTick}"))}");
#endif
            return true;
        }
        public void Terminal(AiIntent i,PlayableAiDeliveryStatus status,AiBudgetLedger b,AiDecisionArbiter a)
        {
            if(i.ReservationId==null||!i.ReservationId.StartsWith(Purpose+":",StringComparison.Ordinal)||status==PlayableAiDeliveryStatus.Applied)return;
            var m=Load(a);if(m==null)return;foreach(var e in b.Capture().Reserved.Where(IsFunding))b.Release(e.Id);m.Expired=true;m.Amount=0;m.Cash=b.Liquid;Save(a,m);
        }
        public int Reprice(AiBudgetEntry e,PlayableProfile p)
        {if(!ParseId(e.Id,out var stage,out var branch,out _,out _,out _))throw new ArgumentException("Invalid technology reservation ID.");return Math.Min(e.Amount,checked((int)Math.Ceiling(AiResearchCatalog.For(branch).Cost(p))+(stage==Stage.Science?TerritoryRules.Cost(p,PlayableBuildingKind.ScientificCenter):0)));}
        public void Rebind(PlayableProfile p,AiProfile ai,AiBudgetLedger b,AiDecisionArbiter arbiter)
        {
            var m=Load(arbiter);if(m==null)return;m.MetadataChanged=true;m.Metadata=Metadata(p,ai);m.Expires=Math.Min(m.Expires,checked(m.Created+b.ReservationLifetimeTicks));
            m.ScienceCost=TerritoryRules.Cost(p,PlayableBuildingKind.ScientificCenter);m.ResearchCost=(int)Math.Ceiling(AiResearchCatalog.For(m.Branch).Cost(p));m.Amount=checked((int)b.ReservationAmount(Id(m)));
            var held=b.ReservationEntry(Id(m));if(held!=null)
            {
                var old=held.Action;var action=new PlayableAiAction(old.ActionId,old.PlayerId,p.ProfileId,p.Revision,old.Generation,old.SnapshotSequence,old.Kind,old.CopyEntityIds(),old.Target,old.SiteId,old.SlotId,old.BuildingKind,old.ParentId,old.TargetId,old.ProductionOrderId,old.UnitKind,old.ResearchKind,old.Seed,old.SourceIdentity);
                b.Release(held.Id);if(!b.Reserve(Entry(m,action,p)))throw new InvalidOperationException("Repriced technology funding no longer fits.");
            }
            Save(arbiter,m);
        }
        public bool Valid(AiBudgetEntry e,PlayableAiObservation o,PlayableProfile p,AiBudgetLedger b,AiDecisionArbiter a)
        {
            var m=Load(a);if(m==null||m.Expired||e.Id!=Id(m)||m.Owner!=owner||m.Generation!=generation)return false;
            if(o.OwnerResearch.Any(r=>r.Kind==m.Branch)||!AiTechnologyPlanner.ProductivePrerequisiteReady(o,AiResearchCatalog.For(m.Branch)))return false;
            if(m.Phase==Stage.Science){var reject=AiEconomyAdmission.Reject(o,p,e.Action);return reject==null||reject=="insufficient credits";}
            return m.Phase==Stage.AwaitingScience?AcceptedScience(m,b)&&(Center(m,o)!=null&&PaidScience(m,b)||b.Capture().Unpaid.Any(e=>ScienceEntry(m,e))):Center(m,o)!=null&&(m.BuildReceipt==null||PaidScience(m,b));
        }
        internal void Validate(PlayableAiObservation o,PlayableProfile p,AiBudgetLedger b,AiDecisionArbiter a,IReadOnlyList<AiIntent> pending)
        {
            var m=Load(a);var held=b.Capture().Reserved.Where(IsFunding).ToArray();
            if(m==null){AiStateWire.Require(held.Length==0,"orphan technology reservation");return;}
            AiStateWire.Require(m.Owner==owner&&m.Generation==generation&&m.Created<=o.Tick&&m.Expires-m.Created<=b.ReservationLifetimeTicks&&m.Income<=o.SettledIncome&&m.ScienceCost==TerritoryRules.Cost(p,PlayableBuildingKind.ScientificCenter)&&m.ResearchCost==(int)Math.Ceiling(AiResearchCatalog.For(m.Branch).Cost(p)),"technology memo owner/terms/clock");
            AiStateWire.Require(held.Length<=1&&held.All(e=>e.Id==Id(m)&&e.Amount==m.Amount&&e.Amount<=Required(m)&&e.CreatedTick==m.Created&&e.ExpiresTick==m.Expires&&e.Terms==Terms(p)&&e.Claims.Count==0&&e.Action.PlayerId==owner&&e.Action.Generation==generation&&e.Action.ProfileId==p.ProfileId&&e.Action.ProfileRevision==p.Revision&&
                (m.Phase==Stage.Research?Target(m,e.Action):e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.ScientificCenter&&e.Action.SiteId==m.Site&&e.Action.SlotId==m.Slot&&e.Action.ParentId==m.Parent&&e.Action.EntityIds.Count==0)),"technology reservation binding");
            AiStateWire.Require(!m.Expired||held.Length==0&&m.Amount==0,"expired technology hold");
            AiStateWire.Require(m.Expired||m.Phase!=Stage.AwaitingScience||AcceptedScience(m,b),"awaiting science exact accepted receipt");
            AiStateWire.Require(m.Expired||m.BuildReceipt==null||m.Phase!=Stage.Research||PaidScience(m,b)&&Center(m,o)!=null,"science paid receipt/center binding");
            AiStateWire.Require(m.Expired||m.Amount==0||held.Length==1,"missing technology hold");
            AiStateWire.Require(held.All(e=>Valid(e,o,p,b,a)),"technology physical target/receipt binding");
            foreach(var i in pending.Where(i=>i.ReservationId!=null&&i.ReservationId.StartsWith(Purpose+":",StringComparison.Ordinal)))
                AiStateWire.Require(ParseId(i.ReservationId,out var stage,out var branch,out var site,out var slot,out var parent)&&branch==m.Branch&&site==m.Site&&slot==m.Slot&&parent==m.Parent&&i.Action.PlayerId==owner&&i.Action.Generation==generation&&i.StartupFund==0&&
                    (stage==Stage.Science?i.Policy=="economy"&&i.Action.Kind==PlayableCommandKind.BuildAt&&i.Action.BuildingKind==PlayableBuildingKind.ScientificCenter&&i.Action.SiteId==site&&i.Action.SlotId==slot&&i.Action.ParentId==parent&&i.Credits==m.ScienceCost:i.Policy=="research"&&i.Action.Kind==PlayableCommandKind.QueueResearch&&i.Action.ResearchKind==branch&&i.Action.EntityIds.SequenceEqual(new[]{m.Center})&&i.Credits==m.ResearchCost)&&b.Capture().Unpaid.Any(e=>e.Id==i.Id&&e.Purpose==i.Policy&&e.Amount==i.Credits),"technology pending payment binding");
        }
    }
    internal sealed partial class PlayableDomain
    {
        internal PlayableAiObservation TechnologyFundingValidationView(string id,int seed)
        {
            var owner=OwnerFor(id);
            var rs=research.TryGetValue(owner,out var orders)?orders.Select(r=>new PlayableResearchOrderSnapshot(r.Id,r.Kind,r.CenterId,r.PaidCost,r.Elapsed,r.Duration,r.Active,r.Complete)).ToArray():Array.Empty<PlayableResearchOrderSnapshot>();
            var bs=buildings.Values.Where(b=>b.Owner==owner).Select(b=>new PlayableBuildingSnapshot(b.Id,b.Owner,b.Kind,b.Position,(int)Math.Ceiling(b.Health),b.Ready?1:b.Build/TerritoryRules.Duration(Terms(b.TermsRevision),b.Kind),b.Queue,b.ProductionProgress,b.Rally,b.SiteId,b.SlotId,b.ParentId,b.Phase,b.BlockedReason,b.Heading,orders:ProductionSnapshot(b),lifecycle:LifecycleSnapshot(b),research:b.Kind==PlayableBuildingKind.ScientificCenter?rs:null,exactHealth:b.Health)).ToArray();
            return PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,navigation.Generation,seed,0,Tick,RuntimeStatus.Running,false,Outcome,(int)Math.Floor(Balance(owner)),null,Array.Empty<PlayableEntitySnapshot>(),bs,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:SiteSnapshots().Where(s=>s.Owner==owner).ToArray(),ownerResearch:rs,activeProfile:profile,owner:owner,ownerId:id,homeSiteId:HomeSite(owner),settledIncome:SettledIncome(owner)));
        }
    }

}
