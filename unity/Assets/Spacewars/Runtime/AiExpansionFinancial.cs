using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Money belongs to a current site opportunity, never to its feasibility witness.
    // The ledger is the sole amount/progress-clock authority; this protected memo keeps
    // only target bindings and catalog-bounded expiry fingerprints through C9.
    internal sealed class AiExpansionFinancial
    {
        internal const string Purpose="expansion-financial",MemoKey="expansion-financial-lifecycle-v1";
        private const int Magic=0x45464d31;
        private const int DormantMagic=0x45464431;
        private const int SelectionMagic=0x45465331; // Optional memo tail; legacy clocks/payload remain readable.
        // Technical state versions, not balance knobs. Version 1 tracks Q4 first income;
        // version 2 was an interim home-only gate; version 3 keeps both facts independently.
        private const int LegacyFirstIncomeVersion=1,InterimHomeVersion=2,BootstrapVersion=3;
        private const string ReleaseCondition="current site safety / actual full admission / no-progress expiry";
#if UNITY_INCLUDE_TESTS
        internal static Action<string,long,string,int,AiBudgetEntry,AiBudgetEntry,AiBudgetState> ActualLifecycleReadbackForTests;
        internal static string ActualLifecycleReadbackOwnerFilterForTests;
        internal static Action<object> ActualDecisionReadbackForTests;
        internal static string ActualDecisionReadbackOwnerFilterForTests;
        internal static Action<object> ActualWitnessStageReadbackForTests;
        internal static string ActualWitnessStageOwnerFilterForTests;
        internal static Action<object> ActualBestSearchReadbackForTests;
        internal static string ActualBestSearchOwnerFilterForTests;
        private void Readback(string stage,long tick,Memo memo,AiBudgetEntry previous,AiBudgetLedger budget)
        {
            var observer=ActualLifecycleReadbackForTests;if(observer==null||ActualLifecycleReadbackOwnerFilterForTests!=null&&owner!=ActualLifecycleReadbackOwnerFilterForTests)return;
            int site=memo?.Active?.Site??0;var current=site==0?null:budget.ReservationEntry(Id(memo.Active));
            observer(owner,tick,stage,site,previous,current,budget.Capture());
        }
        private static void DecisionReadback(string ownerId,long tick,string stage,object data)
        {
            var observer=ActualDecisionReadbackForTests;if(observer==null||ActualDecisionReadbackOwnerFilterForTests!=null&&ownerId!=ActualDecisionReadbackOwnerFilterForTests)return;
            observer(new{Owner=ownerId,Tick=tick,Stage=stage,Data=data});
        }
        private static void WitnessStageReadback(PlayableAiObservation o,string caller,int requestedSiteId,TerritorySiteSnapshot site,PlayableEntitySnapshot actor,PlayableRouteProof route,string stage,bool result)
        {
            var observer=ActualWitnessStageReadbackForTests;if(observer==null||ActualWitnessStageOwnerFilterForTests!=null&&o.OwnerId!=ActualWitnessStageOwnerFilterForTests)return;
            int targetId=site?.Site.Id??requestedSiteId;if(targetId!=9)return;
            observer(new{Owner=o.OwnerId,Tick=o.Tick,Caller=caller,RequestedActiveSiteId=requestedSiteId,SiteId=targetId,ActorId=actor?.Id??0,Stage=stage,Result=result,
                Site=site,Actor=actor,Route=route,
                PublicObjectives=o.PublicScoutObjectives.Where(x=>x.SiteId==targetId).ToArray(),
                RouteProofs=o.RouteProofs.Where(x=>x.TargetId==targetId&&x.Kind==PlayableRouteTargetKind.PublicObjective).ToArray(),
                Explorers=o.Entities.Where(x=>x.Owner==o.Owner&&x.Kind==PlayableEntityKind.Explorer).ToArray()});
        }
#endif
        private sealed class Fingerprint
        {
            internal int Site,Kind,Owner,Claimant,Center;internal bool Contested;internal double Progress;
            internal bool Same(Fingerprint f)=>f!=null&&Site==f.Site&&Kind==f.Kind&&Owner==f.Owner&&Claimant==f.Claimant&&Center==f.Center&&Contested==f.Contested&&Progress==f.Progress;
        }
        private sealed class Memo
        {internal string Owner,Metadata;internal long Generation;internal int PolicyVersion,HomeSite;internal long NextSelectionOrdinal;internal bool HasSelection,PreferRemote;internal bool FirstIncomeHistoryKnown,FirstIncomeReady,HomeEconomyReady;internal Fingerprint Active;internal SortedDictionary<int,Fingerprint> Expired=new SortedDictionary<int,Fingerprint>();internal SortedDictionary<int,DormantTarget> Dormant=new SortedDictionary<int,DormantTarget>();}
        private sealed class DormantTarget
        {internal Fingerprint Target;internal long Created,Expires;}
        private readonly string owner;private readonly long generation;
        internal AiExpansionFinancial(string owner,long generation){this.owner=owner;this.generation=generation;}
        internal static bool IsFunding(AiBudgetEntry e)=>e!=null&&e.Purpose==Purpose;
        private static string Terms(PlayableProfile p)=>p.ProfileId+"@"+p.Revision;
        private static string Metadata(PlayableProfile p,AiProfile ai)=>Terms(p)+":"+ai.Hash;
        private static string Id(Fingerprint f)=>Purpose+":"+f.Site;
        private static bool IncomeKind(PlayableBuildingKind kind)=>kind==PlayableBuildingKind.Mine||kind==PlayableBuildingKind.Outpost;
        private static Fingerprint Finger(TerritorySiteSnapshot s)=>new Fingerprint{Site=s.Site.Id,Kind=(int)s.Site.Kind,Owner=s.Owner.HasValue?(int)s.Owner.Value:-1,Claimant=s.Claimant.HasValue?(int)s.Claimant.Value:-1,Center=s.CenterId,Contested=s.Contested,Progress=s.Progress};
        private static void Write(BinaryWriter w,Fingerprint f){w.Write(f.Site);w.Write(f.Kind);w.Write(f.Owner);w.Write(f.Claimant);w.Write(f.Center);w.Write(f.Contested);w.Write(f.Progress);}
        private static Fingerprint Read(BinaryReader r)=>new Fingerprint{Site=r.ReadInt32(),Kind=r.ReadInt32(),Owner=r.ReadInt32(),Claimant=r.ReadInt32(),Center=r.ReadInt32(),Contested=WorldWire.Boolean(r),Progress=r.ReadDouble()};
        private static void WriteDormant(BinaryWriter w,DormantTarget d){Write(w,d.Target);w.Write(d.Created);w.Write(d.Expires);}
        private static DormantTarget ReadDormant(BinaryReader r)=>new DormantTarget{Target=Read(r),Created=r.ReadInt64(),Expires=r.ReadInt64()};
        private static int PeekMarker(BinaryReader r){long position=r.BaseStream.Position;int marker=r.ReadInt32();r.BaseStream.Position=position;return marker;}
        private static Memo Decode(string payload)
        {
            try
            {
                using(var stream=new MemoryStream(Convert.FromBase64String(payload)))using(var r=new BinaryReader(stream))
                {
                    AiStateWire.Require(r.ReadInt32()==Magic,"site finance memo version");var m=new Memo{Owner=r.ReadString(),Generation=r.ReadInt64(),Metadata=r.ReadString()};
                    if(WorldWire.Boolean(r))m.Active=Read(r);int count=r.ReadInt32();
                    AiStateWire.Require(count>=0&&count<=(stream.Length-stream.Position)/29,"site finance fingerprint payload bound");
                    int previous=0;for(int i=0;i<count;i++){var f=Read(r);AiStateWire.Require(f.Site>previous,"sorted unique site expiry fingerprints");m.Expired.Add(f.Site,f);previous=f.Site;}
                    // Absent tail is honest legacy/unknown history. Keep its original bytes on Save.
                    if(stream.Position<stream.Length&&stream.Length-stream.Position>=4)
                    {
                        long tail=stream.Position;int marker=r.ReadInt32();stream.Position=tail;
                        if(marker!=DormantMagic&&marker!=SelectionMagic)
                        {
                            int version=r.ReadInt32();m.HomeSite=r.ReadInt32();AiStateWire.Require(m.HomeSite>=0,"site finance bootstrap home binding");m.PolicyVersion=version;
                            if(version==LegacyFirstIncomeVersion){m.FirstIncomeHistoryKnown=true;m.FirstIncomeReady=WorldWire.Boolean(r);}
                            else if(version==InterimHomeVersion){m.HomeEconomyReady=WorldWire.Boolean(r);}
                            else if(version==BootstrapVersion){m.FirstIncomeHistoryKnown=WorldWire.Boolean(r);m.FirstIncomeReady=WorldWire.Boolean(r);m.HomeEconomyReady=WorldWire.Boolean(r);}
                            else throw new ArgumentException("Unsupported site finance milestone version.");
                            AiStateWire.Require(!m.FirstIncomeReady||m.FirstIncomeHistoryKnown,"known sticky first-income milestone");
                        }
                    }
                    if(stream.Position<stream.Length&&PeekMarker(r)==DormantMagic)
                    {
                        AiStateWire.Require(r.ReadInt32()==DormantMagic,"site finance dormant extension version");int dormantCount=r.ReadInt32();
                        AiStateWire.Require(dormantCount>=0&&dormantCount<=(stream.Length-stream.Position)/45,"site finance dormant target payload bound");
                        previous=0;for(int i=0;i<dormantCount;i++){var d=ReadDormant(r);AiStateWire.Require(d.Target.Site>previous,"sorted unique dormant site targets");m.Dormant.Add(d.Target.Site,d);previous=d.Target.Site;}
                    }
                    if(stream.Position<stream.Length)
                    {
                        AiStateWire.Require(r.ReadInt32()==SelectionMagic,"site selection memo version");
                        m.NextSelectionOrdinal=r.ReadInt64();m.PreferRemote=WorldWire.Boolean(r);m.HasSelection=true;
                        AiStateWire.Require(m.NextSelectionOrdinal>0,"site selection ordinal");
                    }
                    AiStateWire.Require(stream.Position==stream.Length,"trailing site finance memo");return m;
                }
            }
            catch(Exception e) when(e is FormatException||e is EndOfStreamException||e is IOException){throw new ArgumentException("Invalid site finance memo.",e);}
        }
        private static Memo Load(AiDecisionArbiter a){var payload=a.SiteFundingMemory();return payload==null?null:Decode(payload);}
        private static void Save(AiDecisionArbiter a,Memo m)
        {
            using(var stream=new MemoryStream()){using(var w=new BinaryWriter(stream,Encoding.UTF8,true)){w.Write(Magic);w.Write(m.Owner);w.Write(m.Generation);w.Write(m.Metadata);w.Write(m.Active!=null);if(m.Active!=null)Write(w,m.Active);w.Write(m.Expired.Count);foreach(var f in m.Expired.Values)Write(w,f);if(m.PolicyVersion!=0){w.Write(m.PolicyVersion);w.Write(m.HomeSite);if(m.PolicyVersion==LegacyFirstIncomeVersion)w.Write(m.FirstIncomeReady);else if(m.PolicyVersion==InterimHomeVersion)w.Write(m.HomeEconomyReady);else{w.Write(m.FirstIncomeHistoryKnown);w.Write(m.FirstIncomeReady);w.Write(m.HomeEconomyReady);}}if(m.Dormant.Count>0){w.Write(DormantMagic);w.Write(m.Dormant.Count);foreach(var d in m.Dormant.Values)WriteDormant(w,d);}if(m.HasSelection){w.Write(SelectionMagic);w.Write(m.NextSelectionOrdinal);w.Write(m.PreferRemote);}}a.SaveSiteFundingMemory(Convert.ToBase64String(stream.ToArray()));}
        }
        internal void InitializeNewMatch(PlayableProfile p,AiProfile ai,AiDecisionArbiter a)
        {
            AiStateWire.Require(Load(a)==null,"bootstrap only at new-match genesis");
            Save(a,new Memo{Owner=owner,Generation=generation,Metadata=Metadata(p,ai),PolicyVersion=BootstrapVersion,FirstIncomeHistoryKnown=true});
        }
        internal bool BootstrapActive(AiDecisionArbiter a)
        {var m=Load(a);return m==null||!m.HomeEconomyReady;}
        internal bool FirstIncomePriorityActive(AiDecisionArbiter a)
        {var m=Load(a);return m!=null&&m.HomeEconomyReady&&m.FirstIncomeHistoryKnown&&!m.FirstIncomeReady;}
        private static void ObserveMilestone(Memo m,PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiOpeningExecutor opening,AiDecisionArbiter a)
        {
            if(m==null)return;
            bool changed=false;
            // Bind the stable home identity as soon as the observation knows it. This
            // does not imply Q7 readiness; accepted nonhome work can establish Q4 meanwhile.
            if(m.HomeSite==0&&o.HomeSiteId>0){m.HomeSite=o.HomeSiteId;changed=true;}
            if(!m.HomeEconomyReady&&opening!=null&&m.HomeSite>0&&m.HomeSite==o.HomeSiteId&&opening.HomeEconomyReady(o,ai))
            {m.HomeEconomyReady=true;changed=true;}
            if(m.FirstIncomeHistoryKnown&&!m.FirstIncomeReady&&m.HomeSite>0&&o.Sites.Any(s=>s.Site.Id!=m.HomeSite&&s.Owner==o.Owner&&s.Ready&&TerritoryRules.Income(p,s.Site.Kind)>0&&
                o.Buildings.Any(b=>b.Id==s.CenterId&&b.Owner==o.Owner&&b.SiteId==s.Site.Id&&b.SlotId==0&&b.Health>0&&b.Phase==ConstructionPhase.Ready)))
            {m.FirstIncomeReady=true;changed=true;}
            if(changed)m.PolicyVersion=BootstrapVersion;
            if(changed)Save(a,m);
        }
        private static TerritorySiteSnapshot Best(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiExpansionPlanner expansion,Memo m,string caller,Fingerprint justExpired=null,ISet<int> excludedSites=null)
        {
#if UNITY_INCLUDE_TESTS
            var selected=SelectBest(o,p,ai,expansion,m,caller,justExpired,excludedSites);
            var observer=ActualBestSearchReadbackForTests;
            if(observer!=null&&(ActualBestSearchOwnerFilterForTests==null||ActualBestSearchOwnerFilterForTests==o.OwnerId))
                observer(new{Owner=o.OwnerId,Tick=o.Tick,Caller=caller,RequestedJustExpiredSite=justExpired?.Site??0,
                    DynamicSites=o.Sites.ToArray(),DiscoveredObjectives=o.PublicScoutObjectives.ToArray(),SelectedSite=selected?.Site.Id??0});
            return selected;
#else
            return SelectBest(o,p,ai,expansion,m,caller,justExpired,excludedSites);
#endif
        }
        private static TerritorySiteSnapshot SelectBest(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiExpansionPlanner expansion,Memo m,string caller,Fingerprint justExpired,ISet<int> excludedSites)
            =>AiIncomeSiteSelection.Select(o,p,ai,expansion.Knowledge,
                o.Sites.Where(s=>(excludedSites==null||!excludedSites.Contains(s.Site.Id))&&(justExpired==null||!justExpired.Same(Finger(s)))&&
                    TerritoryRules.Cost(p,s.Site.Kind)>0&&Witness(o,p,ai,s,expansion,caller)&&(!m.Expired.TryGetValue(s.Site.Id,out var expired)||!expired.Same(Finger(s)))),
                AiIncomeSiteSelection.DrawRemote(o,ai,m.NextSelectionOrdinal));
        internal int LocalSite(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDecisionArbiter a,AiExpansionPlanner expansion)
        {
            var m=Load(a);
            return AiIncomeSiteSelection.Select(o,p,ai,expansion.Knowledge,o.Sites.Where(s=>Witness(o,p,ai,s,expansion,"LocalSite")&&
                (m==null||!m.Expired.TryGetValue(s.Site.Id,out var expired)||!expired.Same(Finger(s)))),false)?.Site.Id??0;
        }
        private static void RecordSelection(Memo m,PlayableAiObservation o,AiProfile ai)
        {m.PreferRemote=AiIncomeSiteSelection.DrawRemote(o,ai,m.NextSelectionOrdinal);m.NextSelectionOrdinal=checked(m.NextSelectionOrdinal+1);m.HasSelection=true;}
        internal bool RemotePreference(PlayableAiObservation o,AiProfile ai,AiDecisionArbiter a)
        {var m=Load(a);return m?.Active!=null&&m.HasSelection?m.PreferRemote:AiIncomeSiteSelection.DrawRemote(o,ai,m?.NextSelectionOrdinal??0);}
        private static ISet<int> UnexpiredDormantTargets(PlayableAiObservation o,Memo m)
        {
            var excluded=new HashSet<int>();
            foreach(var d in m.Dormant.Values)
            {var site=o.Sites.FirstOrDefault(s=>s.Site.Id==d.Target.Site);if(o.Tick>=d.Expires&&site!=null&&d.Target.Same(Finger(site)))excluded.Add(d.Target.Site);}
            return excluded;
        }
        private static bool PruneDormant(Memo m,PlayableAiObservation o)
        {
            if(m.Dormant.Count==0)return false;bool changed=false;
            foreach(var pair in m.Dormant.ToArray())
            {
                var d=pair.Value;var site=o.Sites.FirstOrDefault(s=>s.Site.Id==pair.Key);
                if(o.Tick>=d.Expires){m.Expired[pair.Key]=d.Target;m.Dormant.Remove(pair.Key);changed=true;}
                else if(site!=null&&!d.Target.Same(Finger(site))){m.Dormant.Remove(pair.Key);changed=true;}
            }
            return changed;
        }
        private static TerritorySiteSnapshot BestCurrent(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiExpansionPlanner expansion,Memo m,string caller,Fingerprint justExpired=null)
            =>Best(o,p,ai,expansion,m,caller,justExpired,UnexpiredDormantTargets(o,m));
        private static bool DormantClock(Memo m,TerritorySiteSnapshot site,long tick,out long created,out long expires)
        {
            created=tick;expires=0;
            if(!m.Dormant.TryGetValue(site.Site.Id,out var d)||!d.Target.Same(Finger(site)))return false;
            created=d.Created;expires=d.Expires;m.Dormant.Remove(site.Site.Id);return true;
        }
        // Pure preflight: a busy witness proves feasibility but acquires no actor/site claim.
        // Preserve the very same stable goal selection and expiry tombstones used by Observe.
        internal int PriorityCost(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDecisionArbiter a,AiExpansionPlanner expansion,AiBudgetEntry previous,bool allowed)
        {
            var m=Load(a);if(m==null||!FirstIncomePriorityActive(a)||!allowed||expansion.Capture().SiteId!=0)return 0;
            TerritorySiteSnapshot site=null;
            if(m.Active!=null&&previous!=null&&previous.Action.SiteId==m.Active.Site&&o.Tick<previous.ExpiresTick)
            {
                var current=o.Sites.FirstOrDefault(s=>s.Site.Id==m.Active.Site);
                if(current!=null&&Witness(o,p,ai,current,expansion,"PriorityCost")&&(!m.Expired.TryGetValue(current.Site.Id,out var currentExpired)||!currentExpired.Same(Finger(current))))site=current;
            }
            if(site==null)site=BestCurrent(o,p,ai,expansion,m,"PriorityCost",m.Active);
            return site!=null&&Witness(o,p,ai,site,expansion,"PriorityCost")&&(!m.Expired.TryGetValue(site.Site.Id,out var selectedExpired)||!selectedExpired.Same(Finger(site)))?TerritoryRules.Cost(p,site.Site.Kind):0;
        }
        private static bool Legal(TerritorySiteSnapshot s,PlayableAiObservation o,PlayableProfile p)=>s!=null&&IncomeKind(s.Site.Kind)&&TerritoryRules.Income(p,s.Site.Kind)>0&&s.CenterId==0&&!s.Contested&&(!s.Owner.HasValue||s.Owner==o.Owner)&&(!s.Claimant.HasValue||s.Claimant==o.Owner);
        private static bool Witness(PlayableAiObservation o,PlayableProfile p,AiProfile ai,TerritorySiteSnapshot s,AiExpansionPlanner expansion,string caller,int requestedSiteId=0)
        {
            if(s!=null&&!expansion.TeamAllowsSite(s.Site.Id))return false;
            bool legal=Legal(s,o,p);
#if UNITY_INCLUDE_TESTS
            WitnessStageReadback(o,caller,requestedSiteId,s,null,null,"legal-site",legal);
#endif
            if(!legal||!AiIncomeSiteSelection.TerritoryAllowed(o,p,ai,expansion.Knowledge,s))return false;
            bool objective=o.PublicScoutObjectives.Any(g=>g.SiteId==s.Site.Id&&g.Reachable&&g.Role!=PlayablePublicScoutObjectiveRole.PossibleEnemyStart);
#if UNITY_INCLUDE_TESTS
            WitnessStageReadback(o,caller,requestedSiteId,s,null,null,"reachable-public-objective",objective);
#endif
            if(!objective)return false;
#if UNITY_INCLUDE_TESTS
            bool actorReached=false;
#endif
            bool result=o.Entities.Where(u=>u.Owner==o.Owner&&u.Kind==PlayableEntityKind.Explorer&&u.Health>0).Any(u=>
            {
#if UNITY_INCLUDE_TESTS
                actorReached=true;
                bool routeReached=false;bool routeSafetyReached=false;bool routeSafety=false;PlayableRouteProof matchingRoute=null;
#endif
                bool routeSafe=o.RouteProofs.Any(r=>
                {
                    if(r.UnitId!=u.Id||r.TargetId!=s.Site.Id||r.Kind!=PlayableRouteTargetKind.PublicObjective)return false;
                    bool safe=AiExpansionPlanner.Safe(o,p,ai,s,u,r,expansion.Knowledge);
#if UNITY_INCLUDE_TESTS
                    routeReached=true;routeSafetyReached=true;routeSafety=safe;if(safe)matchingRoute=r;
                    WitnessStageReadback(o,caller,requestedSiteId,s,u,r,"typed-route-safety",safe);
#endif
                    return safe;
                });
#if UNITY_INCLUDE_TESTS
                if(!routeReached)WitnessStageReadback(o,caller,requestedSiteId,s,u,null,"typed-route-proof",false);else if(routeSafetyReached&&!routeSafety)WitnessStageReadback(o,caller,requestedSiteId,s,u,null,"typed-route-safety-blocked",false);
#endif
                if(!routeSafe)return false;
                bool unsuppressed=!expansion.Suppressed(s,u,o,p,ai);
#if UNITY_INCLUDE_TESTS
                WitnessStageReadback(o,caller,requestedSiteId,s,u,matchingRoute,"failed-site-suppression",unsuppressed);
#endif
                return unsuppressed;
            });
#if UNITY_INCLUDE_TESTS
            if(!actorReached)WitnessStageReadback(o,caller,requestedSiteId,s,null,null,"living-owned-explorer",false);
            WitnessStageReadback(o,caller,requestedSiteId,s,null,null,"witness-result",result);
#endif
            return result;
        }
        private PlayableAiAction Desired(Fingerprint f,PlayableAiObservation o,PlayableProfile p)=>new PlayableAiAction(1,owner,p.ProfileId,p.Revision,generation,o.SnapshotSequence,PlayableCommandKind.BuildAt,siteId:f.Site,buildingKind:(PlayableBuildingKind)f.Kind,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        private static AiBudgetEntry Entry(AiBudgetEntry old,PlayableAiAction action,int amount,long created,long expires,PlayableProfile p)=>new AiBudgetEntry(Purpose+":"+action.SiteId,Purpose,action,amount,1,created,expires,ReleaseCondition,Array.Empty<string>(),Terms(p));
        private static void Expired(Memo m,AiBudgetLedger b){if(m.Active==null)return;m.Expired[m.Active.Site]=m.Active;b.Release(Id(m.Active));m.Active=null;}
        internal void Reconcile(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiOpeningExecutor opening,AiBudgetLedger b,AiDecisionArbiter a,AiExpansionPlanner expansion)
        {
            var m=Load(a)??new Memo{Owner=owner,Generation=generation,Metadata=Metadata(p,ai)};ObserveMilestone(m,o,p,ai,opening,a);bool dormantChanged=PruneDormant(m,o);
#if UNITY_INCLUDE_TESTS
            Readback("Reconcile.entry",o.Tick,m,null,b);
#endif
            if(m?.Active==null)
            {
                if(dormantChanged)Save(a,m);
#if UNITY_INCLUDE_TESTS
                Readback("Reconcile.no-active",o.Tick,m,null,b);
#endif
                return;
            }
            var e=b.ReservationEntry(Id(m.Active));
            AiStateWire.Require(e!=null&&IsFunding(e),"active site finance requires ledger clock");
            if(o.Tick>=e.ExpiresTick){Expired(m,b);Save(a,m);
#if UNITY_INCLUDE_TESTS
                Readback("Reconcile.expired",o.Tick,m,null,b);
#endif
                return;
            }
            var site=o.Sites.FirstOrDefault(s=>s.Site.Id==m.Active.Site);
            if(site!=null)m.Active=Finger(site); // No time/cash/actor fields participate in this fingerprint.
            if(m.Expired.TryGetValue(m.Active.Site,out var oldExpiry)&&oldExpiry.Same(m.Active)){Expired(m,b);Save(a,m);
#if UNITY_INCLUDE_TESTS
                Readback("Reconcile.expired-fingerprint",o.Tick,m,null,b);
#endif
                return;
            }
            int existingExpansionSite=expansion.Capture().SiteId;bool witnessReached=existingExpansionSite==0;
            bool witness=witnessReached&&Witness(o,p,ai,site,expansion,"Reconcile",m.Active.Site);
            var threats=witness?AiDefensePlanner.Threats(o,p):null;bool threatCheckReached=witness;
            bool threatClear=threatCheckReached&&threats.Length==0;bool valid=witnessReached&&witness&&threatClear;
            long available=valid?Math.Max(0,(long)b.Liquid-b.SafetyReserve-(b.Reserved-e.Amount)-b.Unpaid):0;
            int cost=valid?TerritoryRules.Cost(p,(PlayableBuildingKind)m.Active.Kind):0;
            int amount=valid&&!BootstrapActive(a)?(int)Math.Min(e.Amount,Math.Min(cost,available)):0;
#if UNITY_INCLUDE_TESTS
            DecisionReadback(owner,o.Tick,"Reconcile.actual-evaluation",new{Site=site,ActiveSite=site==null?0:site.Site.Id,RequestedActiveSiteId=m.Active.Site,ExistingExpansionSite=existingExpansionSite,
                WitnessReached=witnessReached,WitnessStatus=witnessReached?(witness?"EvaluatedPass":"EvaluatedBlock"):"NotReached-existing-expansion",WitnessResult=witness,
                ThreatCheckReached=threatCheckReached,ThreatStatus=threatCheckReached?(threatClear?"EvaluatedPass":"EvaluatedBlock"):"NotReached-witness-blocked",ThreatIds=threats?.Select(x=>x.Id).ToArray(),ThreatClear=threatCheckReached?(bool?)threatClear:null,
                Before=e,Cost=cost,Liquid=b.Liquid,SafetyReserve=b.SafetyReserve,Reserved=b.Reserved,Unpaid=b.Unpaid,AvailableAfterOtherHolds=available,AmountBefore=e.Amount,AmountAfterEvaluation=amount});
#endif
            if(amount!=e.Amount){b.Release(e.Id);AiStateWire.Require(b.Reserve(Entry(e,e.Action,amount,e.CreatedTick,e.ExpiresTick,p)),"site finance shrink fits actual ledger");}
            Save(a,m);
#if UNITY_INCLUDE_TESTS
            Readback("Reconcile.exit",o.Tick,m,null,b);
#endif
        }
        internal bool IsExpiredFingerprint(TerritorySiteSnapshot site,AiDecisionArbiter a)
        {
            if(site==null)return false;
            var m=Load(a);
            return m!=null&&m.Expired.TryGetValue(site.Site.Id,out var expired)&&expired.Same(Finger(site));
        }
        internal static bool FullyFundable(AiBudgetLedger budget,int protectedFunds,AiIntent intent)=>
            budget!=null&&intent!=null&&protectedFunds>=0&&intent.Credits>=0&&intent.StartupFund>=0&&
            (long)intent.Credits+intent.StartupFund<=Math.Max(0,budget.Available-protectedFunds);
        // Release only our discretionary money before higher-band allocation. Preserve the
        // exact serialized previous entry locally until the same ordinary decision refills it.
        internal AiBudgetEntry Detach(AiBudgetLedger b){var e=b.Capture().Reserved.SingleOrDefault(IsFunding);if(e!=null)b.Release(e.Id);return e;}
        internal void Observe(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiOpeningExecutor opening,AiBudgetLedger b,AiDecisionArbiter a,AiExpansionPlanner expansion,AiBudgetEntry previous,int protectedFunds,bool allowed)
        {
            var m=Load(a)??new Memo{Owner=owner,Generation=generation,Metadata=Metadata(p,ai)};
            ObserveMilestone(m,o,p,ai,opening,a);
            bool bootstrapActive=BootstrapActive(a);
            PruneDormant(m,o);
#if UNITY_INCLUDE_TESTS
            Readback("Observe.entry",o.Tick,m,previous,b);
#endif
            if(m.Active!=null&&previous!=null&&o.Tick>=previous.ExpiresTick)Expired(m,b);
            if(expansion.Capture().SiteId!=0)
            {
                if(m.Active!=null)
                {
                    AiStateWire.Require(previous!=null,"active site finance preserves old ledger clock while another expansion is active");
                    var desired=Desired(m.Active,o,p);
                    AiStateWire.Require(b.Reserve(Entry(previous,desired,0,previous.CreatedTick,previous.ExpiresTick,p)),"inactive site finance retains zero amount and original expiry");
                }
                m.Metadata=Metadata(p,ai);Save(a,m);
#if UNITY_INCLUDE_TESTS
                Readback("Observe.expansion-active",o.Tick,m,previous,b);
#endif
                return;
            }
            bool targetChanged=false;
            if(m.Active!=null&&allowed&&previous!=null&&previous.Amount==0&&!bootstrapActive)
            {
                var current=o.Sites.FirstOrDefault(s=>s.Site.Id==m.Active.Site);
                bool currentWitness=current!=null&&Witness(o,p,ai,current,expansion,"Observe",m.Active.Site);
                if(!currentWitness)
                {
                    if(current!=null)m.Active=Finger(current);
                    var alternative=BestCurrent(o,p,ai,expansion,m,"Observe.Best",m.Active);
                    if(alternative!=null&&alternative.Site.Id!=m.Active.Site)
                    {
                        if(!m.Dormant.TryGetValue(m.Active.Site,out var oldDormant)||!oldDormant.Target.Same(m.Active))
                            m.Dormant[m.Active.Site]=new DormantTarget{Target=m.Active,Created=previous.CreatedTick,Expires=previous.ExpiresTick};
                        RecordSelection(m,o,ai);m.Active=Finger(alternative);targetChanged=true;
                    }
                }
            }
            if(m.Active==null&&allowed&&!bootstrapActive)
            {
                var best=BestCurrent(o,p,ai,expansion,m,"Observe.Best");
                if(best!=null){RecordSelection(m,o,ai);m.Active=Finger(best);targetChanged=true;}
            }
            if(m.Active!=null)
            {
                var site=o.Sites.FirstOrDefault(s=>s.Site.Id==m.Active.Site);if(site!=null)m.Active=Finger(site);
                bool witnessReached=allowed;bool witness=witnessReached&&Witness(o,p,ai,site,expansion,"Observe",m.Active.Site);
                bool valid=witnessReached&&witness;long available=valid?Math.Max(0,b.Available-protectedFunds):0;
                int cost=valid?TerritoryRules.Cost(p,(PlayableBuildingKind)m.Active.Kind):0;
                int amount=valid&&!bootstrapActive?(int)Math.Min(cost,available):0;
                long created=previous?.CreatedTick??o.Tick,expires=previous?.ExpiresTick??checked(o.Tick+b.ReservationLifetimeTicks);
                bool samePreviousTarget=previous!=null&&previous.Action.SiteId==m.Active.Site&&!targetChanged;
                if(targetChanged||!samePreviousTarget)
                {if(!DormantClock(m,site,o.Tick,out created,out expires)){created=o.Tick;expires=checked(o.Tick+b.ReservationLifetimeTicks);}}
                else if(amount>previous.Amount){created=o.Tick;expires=checked(o.Tick+b.ReservationLifetimeTicks);}
#if UNITY_INCLUDE_TESTS
                DecisionReadback(owner,o.Tick,"Observe.actual-allocation",new{Site=site,ActiveSite=site==null?0:site.Site.Id,RequestedActiveSiteId=m.Active.Site,Allowed=allowed,
                    WitnessReached=witnessReached,WitnessStatus=witnessReached?(witness?"EvaluatedPass":"EvaluatedBlock"):"NotReached-disallowed",WitnessResult=witness,Previous=previous,Cost=cost,Liquid=b.Liquid,Reserved=b.Reserved,Unpaid=b.Unpaid,
                    SafetyReserve=b.SafetyReserve,ProtectedFunds=protectedFunds,AvailableAfterProtection=available,AmountBefore=previous?.Amount,AmountAfterEvaluation=amount,
                    CreatedTickBefore=previous?.CreatedTick,ExpiresTickBefore=previous?.ExpiresTick,CreatedTickAfter=created,ExpiresTickAfter=expires});
#endif
                var desired=Desired(m.Active,o,p);AiStateWire.Require(b.Reserve(Entry(previous,desired,amount,created,expires,p)),"real site finance allocation fits ledger");
            }
            m.Metadata=Metadata(p,ai);Save(a,m);
#if UNITY_INCLUDE_TESTS
            Readback("Observe.exit",o.Tick,m,previous,b);
#endif
        }
        internal int ActiveSite(AiDecisionArbiter a)=>Load(a)?.Active?.Site??0;
        internal bool Ready(PlayableProfile p,AiBudgetLedger b,AiDecisionArbiter a,long tick)
        {var m=Load(a);if(m?.Active==null)return false;var e=b.ReservationEntry(Id(m.Active));return e!=null&&tick<e.ExpiresTick&&e.Amount==TerritoryRules.Cost(p,(PlayableBuildingKind)m.Active.Kind);}
        internal int PreparationSite(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiBudgetLedger b,AiDecisionArbiter a,AiExpansionPlanner expansion,bool openingAllows)
        {
            var m=Load(a);if(m?.Active==null||!openingAllows||BootstrapActive(a)||expansion.ClaimsSite(m.Active.Site)||AiDefensePlanner.Threats(o,p).Length>0)return 0;
            // Preparation consumes Explorer time only when the ordinary construction quote is fully protected.
            // An accepted expansion has its own obligation/fund and is retained by the owner loop.
            if(!Ready(p,b,a,o.Tick))return 0;
            var site=o.Sites.FirstOrDefault(s=>s.Site.Id==m.Active.Site);
            if(site==null||(m.Expired.TryGetValue(site.Site.Id,out var expired)&&expired.Same(Finger(site)))||!Witness(o,p,ai,site,expansion,"PreparationSite",m.Active.Site))return 0;
            return site.Site.Id;
        }
        internal string ReservationFor(PlayableAiAction action,PlayableAiObservation o,PlayableProfile p,AiBudgetLedger b,AiDecisionArbiter a)
        {
            var m=Load(a);if(m?.Active==null)return null;var e=b.ReservationEntry(Id(m.Active));
            return e!=null&&e.Amount>0&&e.Amount==TerritoryRules.Cost(p,(PlayableBuildingKind)m.Active.Kind)&&action.SiteId==m.Active.Site&&action.BuildingKind==(PlayableBuildingKind)m.Active.Kind&&(action.Kind==PlayableCommandKind.Move||action.Kind==PlayableCommandKind.BuildAt)&&o.Tick<e.ExpiresTick?e.Id:null;
        }
        internal static bool Matches(AiIntent i,AiBudgetEntry e,PlayableProfile p)=>p!=null&&IsFunding(e)&&e.Id==Purpose+":"+e.Action.SiteId&&e.Amount==TerritoryRules.Cost(p,e.Action.BuildingKind)&&e.Receipt==null&&!e.AwaitingPayment&&e.ExactSettled==0&&e.ReleaseCondition==ReleaseCondition&&e.Terms==Terms(p)&&e.Action.ProfileId==p.ProfileId&&e.Action.ProfileRevision==p.Revision&&i.ReservationId==e.Id&&i.Policy=="expansion"&&i.Action.EntityIds.Count==1&&((i.Action.Kind==PlayableCommandKind.Move&&i.Credits==0&&i.StartupFund==e.Amount)||(i.Action.Kind==PlayableCommandKind.BuildAt&&i.Credits==e.Amount&&i.StartupFund==0))&&e.Amount>0&&e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.EntityIds.Count==0&&e.Claims.Count==0&&e.Priority==1&&i.Priority==1&&
            e.Action.SiteId==i.Action.SiteId&&e.Action.BuildingKind==i.Action.BuildingKind&&IncomeKind(i.Action.BuildingKind)&&i.Action.SlotId==0&&i.Action.ParentId==0&&e.Action.SlotId==0&&e.Action.ParentId==0&&e.Action.PlayerId==i.Action.PlayerId&&e.Action.Generation==i.Action.Generation&&e.Action.ProfileId==i.Action.ProfileId&&e.Action.ProfileRevision==i.Action.ProfileRevision&&e.Action.Seed==i.Action.Seed&&e.Action.SourceIdentity==i.Action.SourceIdentity;
        private static bool FreeMatches(AiIntent i,AiBudgetEntry e,PlayableProfile p)=>p!=null&&IsFunding(e)&&e.Amount==0&&TerritoryRules.Cost(p,e.Action.BuildingKind)==0&&i.Policy=="expansion"&&i.Priority==1&&i.ReservationId==null&&i.Credits==0&&i.StartupFund==0&&i.Action.EntityIds.Count==1&&(i.Action.Kind==PlayableCommandKind.Move||i.Action.Kind==PlayableCommandKind.BuildAt)&&e.Id==Purpose+":"+i.Action.SiteId&&e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.EntityIds.Count==0&&e.Claims.Count==0&&e.Action.SiteId==i.Action.SiteId&&e.Action.BuildingKind==i.Action.BuildingKind&&IncomeKind(i.Action.BuildingKind)&&i.Action.SlotId==0&&i.Action.ParentId==0&&e.Action.PlayerId==i.Action.PlayerId&&e.Action.Generation==i.Action.Generation&&e.Action.ProfileId==p.ProfileId&&e.Action.ProfileRevision==p.Revision&&i.Action.ProfileId==p.ProfileId&&i.Action.ProfileRevision==p.Revision&&e.Action.Seed==i.Action.Seed&&e.Action.SourceIdentity==i.Action.SourceIdentity;
        internal bool ShouldConsume(AiIntent i,AiBudgetLedger b,AiDecisionArbiter a,PlayableProfile p)
        {var m=Load(a);if(m?.Active==null)return false;var e=b.ReservationEntry(Id(m.Active));return Matches(i,e,p)||FreeMatches(i,e,p);}
        internal AiIntent Consume(AiIntent i,AiBudgetLedger b,AiDecisionArbiter a,PlayableProfile p)
        {
            var m=Load(a);var e=b.ReservationEntry(i.ReservationId??(m?.Active==null?i.Id:Id(m.Active)));AiStateWire.Require(Matches(i,e,p)||FreeMatches(i,e,p),"full site finance admission target/amount");AiStateWire.Require(m?.Active!=null&&e.Id==Id(m.Active),"site finance active admission");b.Release(e.Id);m.Active=null;Save(a,m);
            // The selected financial reserve is consumed, not an accepted invoice or a
            // lasting pending reservation. Move creates the existing full fund; a captured
            // site goes directly to its exact ordinary BuildAt invoice and actual receipt.
            return new AiIntent(i.Id,i.Policy,i.Action,i.ExpiresTick,i.Credits,i.Population,i.Claims,i.Priority,i.Utility,i.Conditions,i.Reason,i.StartupFund,null,i.Overdue){Commit=i.Commit,Feedback=i.Feedback};
        }
        internal void Rebind(PlayableProfile p,AiProfile ai,long tick,AiBudgetLedger b,AiDecisionArbiter a,AiBudgetEntry previous)
        {
            var m=Load(a);if(m==null)return;m.Metadata=Metadata(p,ai);
            if(m.Active!=null)
            {
                AiStateWire.Require(previous!=null,"site finance rebind preserves ledger clock");long expires=Math.Min(previous.ExpiresTick,checked(previous.CreatedTick+b.ReservationLifetimeTicks));
                if(tick>=expires)Expired(m,b);
                else
                {
                    var old=previous.Action;var action=new PlayableAiAction(old.ActionId,owner,p.ProfileId,p.Revision,generation,old.SnapshotSequence,PlayableCommandKind.BuildAt,siteId:m.Active.Site,buildingKind:(PlayableBuildingKind)m.Active.Kind,seed:old.Seed,sourceIdentity:old.SourceIdentity);
                    int amount=BootstrapActive(a)?0:(int)Math.Min(previous.Amount,Math.Min(TerritoryRules.Cost(p,action.BuildingKind),b.Available));AiStateWire.Require(b.Reserve(Entry(previous,action,amount,previous.CreatedTick,expires,p)),"site finance rebind fits available money");
                }
            }
            foreach(var pair in m.Dormant.ToArray())
            {
                var d=pair.Value;long expires=Math.Min(d.Expires,checked(d.Created+b.ReservationLifetimeTicks));
                if(tick>=expires){m.Expired[pair.Key]=d.Target;m.Dormant.Remove(pair.Key);}
                else d.Expires=expires;
            }
            Save(a,m);
        }
        internal bool Valid(AiBudgetEntry e,AiDecisionArbiter a){var m=Load(a);return m?.Active!=null&&e.Id==Id(m.Active);}
        internal void ValidateHome(AiDecisionArbiter a,int actualHomeSite)
        {
            var m=Load(a);
            // The domain's immutable start assignment survives loss of the live HQ.
            // Home0 is genesis before the first observation, never an invented history.
            AiStateWire.Require(m==null||m.PolicyVersion==0||m.HomeSite==0||m.HomeSite==actualHomeSite,"bootstrap stable owner home binding");
        }
        internal void Validate(PlayableProfile p,AiProfile ai,AiBudgetLedger b,AiDecisionArbiter a,long tick,int seed,IReadOnlyList<TerritorySite> catalog,int owners)
        {
            var payload=a.SiteFundingMemory();var held=b.Capture().Reserved.Where(IsFunding).ToArray();
            if(payload==null){AiStateWire.Require(held.Length==0,"orphan site finance hold");return;}
            var income=catalog.Where(s=>IncomeKind(s.Kind)).ToDictionary(s=>s.Id,s=>s.Kind);
            int bound=checked(64+Encoding.UTF8.GetByteCount(owner)+Encoding.UTF8.GetByteCount(Metadata(p,ai))+(income.Count+1)*29+11+8+income.Count*45+13);
            AiStateWire.Require(payload.Length<=checked(4*((bound+2)/3)),"catalog-bounded site finance memo");var m=Decode(payload);
            AiStateWire.Require(m.HomeSite==0||catalog.Any(s=>s.Id==m.HomeSite),"bootstrap home catalog binding");
            AiStateWire.Require(m.PolicyVersion>=0&&m.PolicyVersion<=BootstrapVersion&&(!m.FirstIncomeReady||m.FirstIncomeHistoryKnown)&&(!m.FirstIncomeReady||m.HomeSite>0)&&(!m.HomeEconomyReady||m.HomeSite>0),"home and first-income milestone state");
            AiStateWire.Require(m.PolicyVersion!=LegacyFirstIncomeVersion||(m.FirstIncomeHistoryKnown&&(!m.FirstIncomeReady||m.HomeSite>0)&&!m.HomeEconomyReady),"legacy first-income milestone state");
            AiStateWire.Require(m.PolicyVersion!=InterimHomeVersion||(!m.FirstIncomeHistoryKnown&&!m.FirstIncomeReady),"interim home milestone state");
            AiStateWire.Require(m.PolicyVersion!=0||(!m.FirstIncomeHistoryKnown&&!m.FirstIncomeReady&&!m.HomeEconomyReady),"unknown legacy milestone state");
            AiStateWire.Require(m.Owner==owner&&m.Generation==generation&&m.Metadata==Metadata(p,ai)&&m.Expired.Count<=income.Count&&m.Dormant.Count<=income.Count,"site finance metadata/catalog context");
            foreach(var f in m.Expired.Values.Concat(m.Active==null?Array.Empty<Fingerprint>():new[]{m.Active}).Concat(m.Dormant.Values.Select(d=>d.Target)))
                AiStateWire.Require(income.TryGetValue(f.Site,out var kind)&&f.Kind==(int)kind&&f.Owner>=-1&&f.Owner<owners&&f.Claimant>=-1&&f.Claimant<owners&&f.Center>=0&&f.Progress>=0&&f.Progress<=1&&!double.IsNaN(f.Progress)&&!double.IsInfinity(f.Progress),"site finance fingerprint binding");
            foreach(var pair in m.Dormant)
            {
                var d=pair.Value;AiStateWire.Require(pair.Key==d.Target.Site&&d.Created>=0&&d.Created<=tick&&d.Created<d.Expires&&d.Expires-d.Created<=b.ReservationLifetimeTicks&&tick<d.Expires,"dormant site finance original clock");
                AiStateWire.Require(m.Active==null||m.Active.Site!=pair.Key||!m.Active.Same(d.Target),"active/dormant finance target uniqueness");
                AiStateWire.Require(!m.Expired.TryGetValue(pair.Key,out var dormantExpired)||!dormantExpired.Same(d.Target),"dormant/expired fingerprint uniqueness");
            }
            AiStateWire.Require(m.Active==null?held.Length==0:held.Length==1&&(!m.Expired.TryGetValue(m.Active.Site,out var activeExpired)||!activeExpired.Same(m.Active)),"site finance active/expired/ledger consistency");
            foreach(var e in held)
                AiStateWire.Require(e.Id==Id(m.Active)&&e.Amount<=TerritoryRules.Cost(p,(PlayableBuildingKind)m.Active.Kind)&&e.Priority==1&&e.Receipt==null&&!e.AwaitingPayment&&e.ExactSettled==0&&e.CreatedTick<=tick&&tick<e.ExpiresTick&&e.ExpiresTick-e.CreatedTick<=b.ReservationLifetimeTicks&&e.ReleaseCondition==ReleaseCondition&&e.Terms==Terms(p)&&e.Claims.Count==0&&e.Action.ActionId==1&&e.Action.PlayerId==owner&&e.Action.Generation==generation&&e.Action.ProfileId==p.ProfileId&&e.Action.ProfileRevision==p.Revision&&e.Action.Seed==seed&&e.Action.SourceIdentity==PlayableAiOpeningComposition.SourceIdentity&&e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.SiteId==m.Active.Site&&e.Action.BuildingKind==(PlayableBuildingKind)m.Active.Kind&&e.Action.EntityIds.Count==0&&e.Action.SlotId==0&&e.Action.ParentId==0&&e.Action.TargetId==0&&e.Action.ProductionOrderId==0&&e.Action.Target.Equals(default(NavPoint))&&e.Action.SnapshotSequence>=0,"site financial ledger desired target/terms/real clocks");
            AiStateWire.Require(!b.Capture().Unpaid.Concat(b.Capture().Paid).Any(IsFunding),"site finance memo is never accepted gameplay/payment");
        }
    }
    internal sealed partial class PlayableDomain
    {internal IReadOnlyList<TerritorySite> FinancialPublicCatalog=>sites.Values.Select(s=>s.Site).OrderBy(s=>s.Id).ToArray();
     internal int FinancialOwnerHome(string id)=>HomeSite(OwnerFor(id));}
}
