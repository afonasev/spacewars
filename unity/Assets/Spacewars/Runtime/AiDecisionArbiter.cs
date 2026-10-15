using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
#if UNITY_INCLUDE_TESTS
    internal sealed class AiArbiterSelectionReadback
    {
        internal AiArbiterSelectionReadback(string owner,IEnumerable<string> ordered,IReadOnlyDictionary<string,int> bands)
        {OwnerId=owner;Ordered=ordered.ToArray();Bands=new Dictionary<string,int>(bands);}
        internal string OwnerId{get;} internal string[] Ordered{get;} internal IReadOnlyDictionary<string,int> Bands{get;}
    }
#endif
    // A detached proposal. Commit/feedback hooks are confined to the legacy transaction adapter.
    public sealed class AiIntent
    {
        private readonly string[] claims;
        public AiIntent(string id,string policy,PlayableAiAction action,long expiresTick,int credits,int population,
            IEnumerable<string> claims,int priority=0,double utility=1,string conditions="",string reason="useful",long startupFund=0,string reservationId=null,bool overdue=false)
        {
            if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(policy)||action==null||credits<0||startupFund<0||startupFund>int.MaxValue||population<0||double.IsNaN(utility)||double.IsInfinity(utility))throw new ArgumentException("Invalid proposal.");
            Overdue=overdue;ReservationId=reservationId;StartupFund=startupFund;Id=id;Policy=policy;Action=action;ExpiresTick=expiresTick;Credits=credits;Population=population;
            this.claims=(claims??Array.Empty<string>()).Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            Priority=priority;Utility=utility;Conditions=conditions;Reason=reason;
        }
        public string Id{get;} public string Policy{get;} public PlayableAiAction Action{get;} public long ExpiresTick{get;}
        public bool Overdue{get;} public string ReservationId{get;} public long StartupFund{get;} public int Credits{get;} public int Population{get;} public IReadOnlyList<string> Claims=>Array.AsReadOnly(claims);
        public int Priority{get;} public double Utility{get;} public string Conditions{get;} public string Reason{get;}
        internal Action Commit; internal Action<PlayableAiTraceRecord> Feedback;
    }

    // One ledger per owner: resources are never pooled between allies/owners.
    public sealed partial class AiDecisionArbiter
    {
#if UNITY_INCLUDE_TESTS
        internal static Action<AiArbiterSelectionReadback> SelectionReadbackForTests;
        internal static string SelectionReadbackOwnerFilterForTests;
#endif
        private sealed class History { public long FirstTick,NextRetry;public int Failures;public string Conditions; }
        private readonly Dictionary<string,History> history=new Dictionary<string,History>(StringComparer.Ordinal);
        private readonly Dictionary<string,string> rejections=new Dictionary<string,string>(StringComparer.Ordinal);
        public IReadOnlyDictionary<string,string> Rejections=>new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(rejections);
        internal const string FundingMemoKey="technology-funding-lifecycle-v1";
        internal string FundingMemory(out long created,out long expires)
        {if(history.TryGetValue(FundingMemoKey,out var h)){created=h.FirstTick;expires=h.NextRetry;return h.Conditions;}created=expires=0;return null;}
        internal void FundingMemory(string payload,long created,long expires)
        {history[FundingMemoKey]=new History{Conditions=payload,FirstTick=created,NextRetry=expires};}
        internal void ClearFundingMemory()=>history.Remove(FundingMemoKey);
        internal string SiteFundingMemory(){if(!history.TryGetValue(AiExpansionFinancial.MemoKey,out var h))return null;if(string.IsNullOrWhiteSpace(h.Conditions))throw new ArgumentException("Present site finance memo requires a payload.");return h.Conditions;}
        internal void SaveSiteFundingMemory(string payload){history[AiExpansionFinancial.MemoKey]=new History{Conditions=payload};}
        internal void ClearSiteFundingMemory()=>history.Remove(AiExpansionFinancial.MemoKey);
        private long aging,retry;
        private int retryLimit;
        public AiDecisionArbiter(AiProfile profile)
        {Rebind(profile);}
        internal void Rebind(AiProfile profile)
        {if(profile==null)throw new ArgumentNullException(nameof(profile));aging=AiProfile.SecondsToTicks(profile.Value("decision.intentAgingSeconds"),30);retry=AiProfile.SecondsToTicks(profile.Value("decision.retrySeconds"),30);retryLimit=(int)profile.Value("decision.retryLimit");}
        public IReadOnlyList<AiIntent> Select(IEnumerable<AiIntent> proposals,long tick,int credits,int freePopulation,int actionLimit,AiBudgetLedger budget=null,AiArmyRegistry armies=null,PlayableProfile gameplayProfile=null)
        {
            if(proposals==null||credits<0||freePopulation<0||actionLimit<1)throw new ArgumentException("Invalid ledger.");
            rejections.Clear();
            var all=proposals.ToArray();
            if(all.Any(x=>x.Id==FundingMemoKey||x.Id==AiExpansionFinancial.MemoKey))throw new ArgumentException("Funding memo is not a gameplay intent.");
            if(all.Select(x=>x.Id).Distinct(StringComparer.Ordinal).Count()!=all.Length)throw new ArgumentException("Duplicate intent identity.");
            var candidates=new List<AiIntent>();
            foreach(var p in all.OrderBy(x=>x.Id,StringComparer.Ordinal))
            {
                if(!history.TryGetValue(p.Id,out var h)||h.Conditions!=p.Conditions)history[p.Id]=h=new History{FirstTick=tick,Conditions=p.Conditions};
                string reason=armies?.Reject(p)??(p.ExpiresTick<tick?"intent expired":p.Utility<=0?"no useful action":h.Failures>=retryLimit?"unchanged rejection retry limit":tick<h.NextRetry?"rejection cooldown":null);
                if(reason!=null){rejections[p.Id]=reason;continue;}
                candidates.Add(p);
            }
            // C3 semantic bands: survival, accepted startup, overdue/aged, ordinary.
            // Ordinary priority1 is a utility preference, not an exemption from aging.
#if UNITY_INCLUDE_TESTS
            Dictionary<string,int> actualBands=null;List<string> actualOrder=null;
            bool captureSelection=SelectionReadbackForTests!=null&&(SelectionReadbackOwnerFilterForTests==null||SelectionReadbackOwnerFilterForTests==budget?.OwnerId);
            if(captureSelection){actualBands=new Dictionary<string,int>(StringComparer.Ordinal);actualOrder=new List<string>();}
#endif
            int Band(AiIntent p)
            {
                int band=p.Priority>=2?4:p.ReservationId!=null&&!AiExpansionFinancial.IsFunding(budget?.ReservationEntry(p.ReservationId))?(AiTechnologyFunding.IsFunding(budget?.ReservationEntry(p.ReservationId))?2:3):p.Overdue||tick-history[p.Id].FirstTick>=aging?1:0;
#if UNITY_INCLUDE_TESTS
                if(actualBands!=null)actualBands[p.Id]=band;
#endif
                return band;
            }
            var ordered=candidates.OrderByDescending(Band)
                .ThenByDescending(p=>p.Priority).ThenByDescending(p=>p.Utility).ThenBy(p=>history[p.Id].FirstTick).ThenBy(p=>p.Id,StringComparer.Ordinal);
            // Keep the deficit until this intent's own held amount is added back. Clamping
            // before that would let it spend money still promised to other reservations.
            long available=budget==null?credits:(long)budget.Liquid-budget.SafetyReserve-budget.Reserved-budget.Unpaid;
            var chosen=new List<AiIntent>();var claims=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in ordered)
            {
#if UNITY_INCLUDE_TESTS
                actualOrder?.Add(p.Id);
#endif
                if(chosen.Count==actionLimit){rejections[p.Id]="decision attention limit";continue;}
                long held=budget?.ReservationAmount(p.ReservationId??p.Id)??0;
                var earmarked=p.ReservationId==null?null:budget?.ReservationEntry(p.ReservationId);
                bool factoryHold=earmarked!=null&&earmarked.Purpose=="factory-startup"&&p.Policy=="production"&&p.Action.Kind==PlayableCommandKind.QueueTank&&earmarked.Action.Kind==p.Action.Kind&&earmarked.Action.UnitKind==p.Action.UnitKind&&earmarked.Action.EntityIds.SequenceEqual(p.Action.EntityIds);
                bool expansionHold=earmarked!=null&&AiExpansionPlanner.IsFunding(earmarked)&&p.Policy=="expansion"&&p.Action.Kind==PlayableCommandKind.BuildAt&&earmarked.Action.Kind==p.Action.Kind&&
                    p.Action.SlotId==0&&p.Action.ParentId==0&&p.Action.EntityIds.Count==1&&(p.Action.BuildingKind==PlayableBuildingKind.Mine||p.Action.BuildingKind==PlayableBuildingKind.Outpost)&&p.Action.BuildingKind==earmarked.Action.BuildingKind&&p.Action.SiteId==earmarked.Action.SiteId&&
                    p.Action.PlayerId==earmarked.Action.PlayerId&&p.Action.Generation==earmarked.Action.Generation&&p.Action.EntityIds.SequenceEqual(earmarked.Action.EntityIds)&&earmarked.Amount==p.Credits&&
                    earmarked.Id==AiExpansionPlanner.FundingPurpose+":"+p.Action.SiteId+":"+string.Join(",",p.Action.EntityIds);
                bool conversionHold=earmarked!=null&&earmarked.Purpose==AiInfrastructurePlanner.ConversionPurpose&&p.Policy=="infrastructure"&&p.Action.Kind==PlayableCommandKind.BuildAt&&
                    p.Action.PlayerId==earmarked.Action.PlayerId&&p.Action.Generation==earmarked.Action.Generation&&p.Action.SiteId==earmarked.Action.SiteId&&p.Action.SlotId==earmarked.Action.SlotId&&p.Action.ParentId==earmarked.Action.ParentId&&p.Action.BuildingKind==earmarked.Action.BuildingKind;
                bool technologyHold=earmarked!=null&&AiTechnologyFunding.Matches(p,earmarked,this);
                bool financialHold=AiExpansionFinancial.Matches(p,earmarked,gameplayProfile);
                bool wrongStartup=p.ReservationId!=null&&!(factoryHold||expansionHold||conversionHold||technologyHold||financialHold);
                string reason=wrongStartup?"startup reservation target mismatch":p.Credits+p.StartupFund>Math.Min(credits,Math.Max(0,available+held))?"ledger budget unavailable":p.Population>freePopulation?"population capacity":
                    p.Claims.Any(claims.Contains)?"selected action claim conflict":budget!=null&&budget.HasUnpaid(p.Id)?"accepted unpaid obligation":budget!=null&&budget.HasConflictingClaims(p.Id,p.Claims,p.ReservationId)?"reserved claim conflict":null;
                if(reason!=null){rejections[p.Id]=reason;continue;}
                chosen.Add(p);credits-=p.Credits;available+=(p.ReservationId==null||financialHold?held:Math.Min(held,p.Credits))-p.Credits-p.StartupFund;freePopulation-=p.Population;foreach(var c in p.Claims)claims.Add(c);
            }
            // Bound stale diagnostic history; pending entries are fed back before next decision.
            var present=new HashSet<string>(all.Select(p=>p.Id),StringComparer.Ordinal);
            foreach(var key in history.Keys.Where(k=>k!=FundingMemoKey&&k!=AiExpansionFinancial.MemoKey&&!present.Contains(k)).OrderBy(k=>history[k].FirstTick).ThenBy(k=>k,StringComparer.Ordinal).Take(Math.Max(0,history.Count-(history.ContainsKey(FundingMemoKey)?1:0)-(history.ContainsKey(AiExpansionFinancial.MemoKey)?1:0)-128)).ToArray())history.Remove(key);
#if UNITY_INCLUDE_TESTS
            if(captureSelection)SelectionReadbackForTests(new AiArbiterSelectionReadback(budget?.OwnerId,actualOrder,actualBands));
#endif
            return chosen.AsReadOnly();
        }
        public void Terminal(AiIntent intent,long tick,PlayableAiDeliveryStatus status)
        {
            if(!history.TryGetValue(intent.Id,out var h))return;
            if(status==PlayableAiDeliveryStatus.Rejected||status==PlayableAiDeliveryStatus.InvalidAction)
            {h.Failures++;h.NextRetry=checked(tick+retry);}
            else history.Remove(intent.Id);
        }
    }
}
