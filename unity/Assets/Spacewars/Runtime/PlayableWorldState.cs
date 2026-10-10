using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Spacewars.Simulation;
using static Spacewars.Runtime.WorldWire;
namespace Spacewars.Runtime
{
    // Primitive envelope; each section has an explicit positional binary schema in
    // WorldWire/PlayableWorldRecords. There are no serialized engine objects/jobs.
    public sealed class PlayableWorldState
    {
        public const int CurrentVersion=9;
        public int Version=CurrentVersion,Seed;
        public long Generation,Tick;
        public string SourceIdentity;
        public byte[] Binding,Domain,Navigation,Participants,AiAuthority=System.Array.Empty<byte>(),RuntimeTransport=System.Array.Empty<byte>();
        public byte[][] Vision;
        public byte[] Encode()
        {
            var payload=WorldWire.Pack(w=>{
                w.Write(0x53575731);w.Write(Version);w.Write(Seed);w.Write(Generation);w.Write(Tick);String(w,SourceIdentity);
                Bytes(w,Binding);Bytes(w,Participants??System.Array.Empty<byte>());Bytes(w,Domain);Bytes(w,Navigation);Bytes(w,AiAuthority);Bytes(w,RuntimeTransport);Array(w,Vision,x=>Bytes(w,x));
            });
            if(payload.Length>MaxBytes-32)throw new ArgumentException("World exceeds byte limit.");
            var result=new byte[payload.Length+32];
            Buffer.BlockCopy(payload,0,result,0,payload.Length);
            using(var h=SHA256.Create())Buffer.BlockCopy(h.ComputeHash(payload),0,result,payload.Length,32);
            return result;
        }
        public static PlayableWorldState Decode(byte[] bytes)
        {
            if(bytes==null||bytes.Length<32||bytes.Length>MaxBytes)throw new ArgumentException("Invalid world envelope length.");
            var payload=bytes.Take(bytes.Length-32).ToArray();
            using(var h=SHA256.Create())if(!h.ComputeHash(payload).SequenceEqual(bytes.Skip(bytes.Length-32)))throw new ArgumentException("World envelope digest mismatch.");
            return WorldWire.Unpack(payload,r=>{
                if(r.ReadInt32()!=0x53575731)throw new ArgumentException("Invalid world magic.");
                var s=new PlayableWorldState{Version=r.ReadInt32(),Seed=r.ReadInt32(),Generation=r.ReadInt64(),Tick=r.ReadInt64(),SourceIdentity=String(r),Binding=Bytes(r),Participants=Bytes(r),Domain=Bytes(r),Navigation=Bytes(r),AiAuthority=Bytes(r),RuntimeTransport=Bytes(r),Vision=Array(r,()=>Bytes(r))};
                if(s.Version!=CurrentVersion||s.Generation<1||s.Tick<0||string.IsNullOrWhiteSpace(s.SourceIdentity)||s.Vision.Length>8)throw new ArgumentException("Unsupported world envelope.");return s;
            });
        }
        // Each section includes SHA256 to reject accidental corruption before hydration.
        private static void Bytes(BinaryWriter w,byte[] value){if(value==null||value.Length>MaxBytes)throw new ArgumentException("Invalid section.");w.Write(value.Length);w.Write(value);using(var h=SHA256.Create())w.Write(h.ComputeHash(value));}
        private static byte[] Bytes(BinaryReader r){int n=r.ReadInt32();if(n<0||n>MaxBytes||n>r.BaseStream.Length-r.BaseStream.Position-32)throw new ArgumentException("Invalid section size.");var b=r.ReadBytes(n);var hash=r.ReadBytes(32);using(var h=SHA256.Create())if(!h.ComputeHash(b).SequenceEqual(hash))throw new ArgumentException("World section digest mismatch.");return b;}
    }
    internal sealed partial class PlayableDomain
    {
        // Caller owns authority and has returned externally executing routes to the
        // mailboxes. This technical barrier does not invoke SetRallyPaused/reset.
        internal byte[] CaptureWorldBytes(int seed,string sourceIdentity)=>CaptureWorldState(seed,sourceIdentity).Encode();
        // Build detached sections once; callers append their sections before final encoding.
        internal PlayableWorldState CaptureWorldState(int seed,string sourceIdentity)
        {
            var nav=navigation.CaptureState();
            var domain=Pack(w=>{
                w.Write(termProfiles.Count);foreach(var entry in termProfiles.OrderBy(x=>x.Key)){w.Write(entry.Key);String(w,entry.Value.DisplayName);var data=entry.Value.CopyData();foreach(var descriptor in PlayableProfileMetadata.Fields)Number(w,descriptor.Read(data));}
                w.Write(scriptedEnemyPatrol);w.Write(WinnerTeam.HasValue);if(WinnerTeam.HasValue)w.Write(WinnerTeam.Value);Array(w,eliminated.OrderBy(x=>(int)x).ToArray(),x=>w.Write((int)x));w.Write(offlineSequences.Count);foreach(var pair in offlineSequences){w.Write((int)pair.Key);w.Write(pair.Value);}w.Write(ownerCredits.Count);foreach(var pair in ownerCredits){w.Write((int)pair.Key);Number(w,pair.Value);}
                w.Write(nextAiSequence);w.Write(nextOrderRevision);w.Write(nextId);w.Write(nextProjectile);w.Write(nextProductionSequence);w.Write(nextResearchSequence);w.Write(Tick);
                w.Write(settledIncome.Count);foreach(var row in settledIncome.OrderBy(x=>(int)x.Key)){w.Write((int)row.Key);Number(w,row.Value);}
                Number(w,credits);Number(w,enemyCredits);Number(w,incomeClock);Number(w,elapsed);w.Write((int)Outcome);
                Array(w,units.Values.ToArray(),x=>WriteRecord(w,x));Array(w,buildings.Values.ToArray(),x=>WriteRecord(w,x));Array(w,projectiles.ToArray(),x=>WriteRecord(w,x));
                w.Write(research.Count);foreach(var row in research){w.Write((int)row.Key);Array(w,row.Value.ToArray(),x=>WriteRecord(w,x));}
                w.Write(sites.Count);foreach(var row in sites){var s=row.Value;w.Write(row.Key);w.Write(s.Claimant.HasValue);if(s.Claimant.HasValue)w.Write((int)s.Claimant.Value);w.Write(s.Locked.HasValue);if(s.Locked.HasValue)w.Write((int)s.Locked.Value);Number(w,s.Progress);w.Write(s.Contested);w.Write(s.CenterId);}
                Array(w,centerDamage.Values.ToArray(),x=>WorldWire.Write(w,x));Array(w,impacts.ToArray(),x=>WorldWire.Write(w,x));
                w.Write(rallyPaused);w.Write(rallyWork.Count);
                foreach(var row in rallyWork){var work=row.Value;WorldWire.Write(w,work.Intent);w.Write(work.Kind);w.Write(work.Candidate);w.Write(work.Geometry!=null);if(work.Geometry!=null&& !ReferenceEquals(work.Geometry,Geometry))throw new InvalidOperationException("Rally geometry is outside current boundary.");w.Write(work.Request==null?0:work.Request.Request);}
                Array(w,rallyReceipts.ToArray(),x=>WorldWire.Write(w,x));
                foreach(var layout in new[]{units.CaptureLayout(),buildings.CaptureLayout(),centerDamage.CaptureLayout(),rallyWork.CaptureLayout()})Array(w,layout,i=>w.Write(i));
                WriteMatchHistory(w);WriteSpatialCompletionExtension(w);WriteTacticalQueueExtension(w);
                w.Write(0x494E4331);Array(w,incomeEvents.ToArray(),e=>{w.Write(e.Tick);w.Write(e.BuildingId);w.Write((int)e.Owner);w.Write((int)e.Kind);Number(w,e.Position.X);Number(w,e.Position.Z);Number(w,e.Amount);});
            });
            return new PlayableWorldState{Seed=seed,Generation=navigation.Generation,Tick=Tick,SourceIdentity=sourceIdentity,Binding=WorldWire.Binding(profile),Participants=offline==null?System.Array.Empty<byte>():OfflineConfigurationWire.Binding(offline,profile),Domain=domain,Navigation=Pack(w=>WorldWire.Write(w,nav)),Vision=visions.Values.Select(v=>Pack(w=>WorldWire.Write(w,v.CaptureState()))).ToArray()};
        }
        internal static PlayableDomain RestoreWorldBytes(byte[] bytes,PlayableProfile profile,int expectedSeed,string expectedSourceIdentity)=>RestoreConfiguredWorldBytes(bytes,profile,expectedSeed,expectedSourceIdentity,null);
        internal static PlayableDomain RestoreConfiguredWorldBytes(byte[] bytes,PlayableProfile profile,int expectedSeed,string expectedSourceIdentity,OfflineMatchConfiguration configuration,bool allowAiAuthority=false)
        {
            // Decode independently and hydrate only a detached candidate. No worker,
            // starter units, genesis ticks, route calls or policy callbacks are started.
            var state=PlayableWorldState.Decode(bytes);
            if(state.AiAuthority.Length>0&&!allowAiAuthority)throw new ArgumentException("Live AI world requires the authority restore API.");
            bool legacyQueue=state.Binding.SequenceEqual(WorldWire.Binding(profile,true));
            bool legacyMarch=legacyQueue||state.Binding.SequenceEqual(WorldWire.BindingLegacyMarch(profile));
            if(legacyQueue&&profile.UnitOrderQueueLimit!=64)throw new ArgumentException("Legacy world requires default absent queue metadata.");
            if(state.Seed!=expectedSeed||state.SourceIdentity!=expectedSourceIdentity||!legacyMarch&&!state.Binding.SequenceEqual(WorldWire.Binding(profile)))throw new ArgumentException("World source/profile/map binding mismatch.");
            if(!state.Participants.SequenceEqual(configuration==null?System.Array.Empty<byte>():OfflineConfigurationWire.Binding(configuration,profile,legacyQueue,legacyMarch)))throw new ArgumentException("World roster/map assignment binding mismatch.");
            var nav=Unpack(state.Navigation,ReadNavigationSessionState);
            if(nav==null||nav.Generation!=state.Generation||nav.Geometry==null)throw new ArgumentException("Invalid world navigation binding.");
            var geometry=new NavGeometry(nav.Geometry.HalfExtent,nav.Geometry.Obstacles,nav.Geometry.Revision);
            var candidate=Unpack(state.Domain,r=>{
                int termCount=r.ReadInt32();if(termCount<1||termCount>10000)throw new ArgumentException("Invalid term registry.");
                var termRows=new System.Collections.Generic.Dictionary<int,PlayableProfile>();
                for(int i=0;i<termCount;i++){int revision=r.ReadInt32();string name=String(r);var data=profile.CopyData();data.revision=revision;if(legacyQueue)data.unitOrderQueueLimit=64;foreach(var descriptor in legacyQueue?LegacyQueueCodec.Fields:legacyMarch?LegacyMarchCodec.Fields:PlayableProfileMetadata.Fields)descriptor.Write(data,Number(r));var bound=PlayableProfile.Create(data,profile.AuthoredMap,name);if(termRows.ContainsKey(revision)||NativeBalanceFields.ValidateLiveDifference(profile,bound)!=null)throw new ArgumentException("Invalid transaction profile.");termRows.Add(revision,bound);}
                if(!termRows.TryGetValue(profile.Revision,out var currentTerms)||!WorldWire.Binding(currentTerms).SequenceEqual(WorldWire.Binding(profile)))throw new ArgumentException("Current transaction profile mismatch.");
                bool patrol=Boolean(r);var d=new PlayableDomain(profile,state.Generation,patrol,true,geometry,configuration){Tick=state.Tick};foreach(var term in termRows)d.termProfiles[term.Key]=term.Key==profile.Revision?profile:term.Value;
                if(configuration!=null&&patrol)throw new ArgumentException("Offline world cannot enable diagnostic patrol.");
                d.WinnerTeam=Boolean(r)?(int?)r.ReadInt32():null;foreach(var owner in Array(r,()=>EnumValue<PlayableOwner>(r)))if(!d.HasOwner(owner)||!d.eliminated.Add(owner))throw new ArgumentException("Invalid eliminated roster.");int sequences=r.ReadInt32();if(sequences<0||sequences>d.ownerCredits.Count)throw new ArgumentException("Invalid command watermarks.");for(int i=0;i<sequences;i++){var owner=EnumValue<PlayableOwner>(r);long sequence=r.ReadInt64();if(configuration==null||!d.HasOwner(owner)||sequence<1||d.offlineSequences.ContainsKey(owner))throw new ArgumentException("Invalid owner command watermark.");d.offlineSequences.Add(owner,sequence);}int accounts=r.ReadInt32();if(accounts!=d.ownerCredits.Count)throw new ArgumentException("Invalid owner accounts.");d.ownerCredits.Clear();for(int i=0;i<accounts;i++){var owner=EnumValue<PlayableOwner>(r);if(!d.HasOwner(owner)||d.ownerCredits.ContainsKey(owner))throw new ArgumentException("Invalid account owner.");d.ownerCredits.Add(owner,Number(r));}
                d.nextAiSequence=r.ReadInt64();if(d.nextAiSequence<1)throw new ArgumentException("Invalid AI sequence allocator.");d.nextOrderRevision=r.ReadInt64();d.nextId=r.ReadInt32();d.nextProjectile=r.ReadInt32();d.nextProductionSequence=r.ReadInt64();d.nextResearchSequence=r.ReadInt64();if(r.ReadInt64()!=state.Tick)throw new ArgumentException("Domain/envelope tick binding mismatch.");
                int incomeCount=r.ReadInt32();if(incomeCount<0||incomeCount>d.Owners.Count())throw new ArgumentException("Invalid income owner count.");for(int i=0;i<incomeCount;i++){var owner=EnumValue<PlayableOwner>(r);var total=Number(r);if(!d.HasOwner(owner)||total<0||d.settledIncome.ContainsKey(owner))throw new ArgumentException("Invalid settled income owner/value.");d.settledIncome.Add(owner,total);}
                d.credits=Number(r);d.enemyCredits=Number(r);d.incomeClock=Number(r);d.elapsed=Number(r);d.Outcome=EnumValue<PlayableMatchOutcome>(r);
                foreach(var u in Array(r,()=>ReadUnit(r))){if(u==null||d.units.ContainsKey(u.Id))throw new ArgumentException("Duplicate/null world unit.");d.units.Add(u.Id,u);}
                foreach(var b in Array(r,()=>ReadBuilding(r))){if(b==null||d.buildings.ContainsKey(b.Id))throw new ArgumentException("Duplicate/null world building.");d.buildings.Add(b.Id,b);}
                d.projectiles.AddRange(Array(r,()=>ReadProjectile(r)));
                int owners=r.ReadInt32();if(owners<0||owners>d.ownerCredits.Count)throw new ArgumentException("Invalid research owners.");for(int i=0;i<owners;i++){var owner=EnumValue<PlayableOwner>(r);if(!d.HasOwner(owner)||d.research.ContainsKey(owner))throw new ArgumentException("Duplicate research owner.");d.research.Add(owner,Array(r,()=>ReadResearchOrder(r)).ToList());}
                int sites=r.ReadInt32();if(sites!=d.sites.Count)throw new ArgumentException("Invalid site count.");var seen=new System.Collections.Generic.HashSet<int>();for(int i=0;i<sites;i++){int id=r.ReadInt32();if(!seen.Add(id)||!d.sites.TryGetValue(id,out var s))throw new ArgumentException("Invalid territory identity.");s.Claimant=Boolean(r)?(PlayableOwner?)EnumValue<PlayableOwner>(r):null;s.Locked=Boolean(r)?(PlayableOwner?)EnumValue<PlayableOwner>(r):null;s.Progress=Number(r);s.Contested=Boolean(r);s.CenterId=r.ReadInt32();}
                foreach(var damage in Array(r,()=>ReadPlayableCenterDamageSnapshot(r))){if(damage==null)throw new ArgumentException("Null damage fact.");d.centerDamage.Add(damage.CenterId+":"+damage.AttackerId,damage);}
                d.impacts.AddRange(Array(r,()=>ReadPlayableImpactSnapshot(r)));
                d.rallyPaused=Boolean(r);int count=r.ReadInt32();if(count<0||count>d.buildings.Count)throw new ArgumentException("Invalid rally count.");
                var restoredRequests=d.navigation.RestoreWorldState(nav);
                for(int i=0;i<count;i++){
                    var intent=ReadPlayableRallyIntentState(r);int kind=r.ReadInt32(),exit=r.ReadInt32();bool hasGeometry=Boolean(r);long request=r.ReadInt64();
                    if(intent==null||intent.Generation!=state.Generation||intent.Sequence<1||d.RallyProducer(intent.Building,intent.Owner)==null||kind<0||kind>=RallyKinds.Length||exit<0||exit>d.FactoryExitCandidates(d.buildings[intent.Building],RallyKinds[kind]).Length||d.rallyWork.ContainsKey(intent.Building))throw new ArgumentException("Invalid rally progress.");
                    NavigationRequest bound=null;if(request!=0){bound=restoredRequests.SingleOrDefault(x=>x.Request==request&&x.Session==state.Generation);if(bound==null||bound.Entity!=-intent.Building||bound.Order!=intent.Sequence||!hasGeometry)throw new ArgumentException("Invalid rally request binding.");}
                    d.rallyWork.Add(intent.Building,new RallyWork{Intent=intent,Kind=kind,Candidate=exit,Geometry=hasGeometry?geometry:null,Request=bound});
                }
                d.rallyReceipts.AddRange(Array(r,()=>ReadPlayableCommandReceipt(r)));
                d.units.RestoreLayout(Array(r,()=>r.ReadInt32()));d.buildings.RestoreLayout(Array(r,()=>r.ReadInt32()));d.centerDamage.RestoreLayout(Array(r,()=>r.ReadInt32()));d.rallyWork.RestoreLayout(Array(r,()=>r.ReadInt32()));d.ReadMatchHistory(r);d.ReadSpatialCompletionExtension(r);d.ReadTacticalQueueExtension(r,legacyQueue);
                if(r.BaseStream.Position<r.BaseStream.Length)
                {
                    if(r.ReadInt32()!=0x494E4331)throw new ArgumentException("Invalid income presentation extension.");
                    d.incomeEvents.AddRange(Array(r,()=>new PlayableIncomeEvent(r.ReadInt64(),r.ReadInt32(),EnumValue<PlayableOwner>(r),EnumValue<PlayableBuildingKind>(r),new NavPoint(Number(r),Number(r)),Number(r))));
                    foreach(var e in d.incomeEvents)if(e.Tick<0||e.Tick>d.Tick||e.BuildingId<=0||!d.HasOwner(e.Owner)||e.Amount<=0)throw new ArgumentException("Invalid income event.");
                }
                return d;
            });
            foreach(var section in state.Vision){var v=Unpack(section,ReadPlayableVisionState);if(v==null||!candidate.Owners.Any(x=>candidate.TeamOf(x)==v.Team)||candidate.visions.ContainsKey(v.Team))throw new ArgumentException("Invalid world vision binding.");var next=new PlayableVision(v.Team,profile.ArenaHalfExtent,profile.ArenaHalfExtent,profile.VisionCellSize,profile.FogEdgeFeather);next.RestoreState(v);candidate.visions.Add(v.Team,next);}
            foreach(var v in candidate.visions.Values.Select(x=>x.CaptureState()))foreach(var known in v.KnownBuildings)if(!candidate.HasOwner(known.Owner)||known.Team!=candidate.TeamOf(known.Owner)||known.Team==v.Team||known.Id>=candidate.nextId)throw new ArgumentException("Invalid remembered building binding.");
            candidate.ValidateWorld();candidate.ValidateGroupOrders();candidate.ValidateSpatialCompletions();candidate.ValidateTacticalQueues();return candidate;
        }
        private void ValidateWorld()
        {
            void Require(bool condition,string reason){if(!condition)throw new ArgumentException("Invalid world: "+reason);}
            bool Ref(int id)=>id>=0&&id<nextId;
            Require(ownerCredits.Count==Owners.Count()&&ownerCredits.All(x=>HasOwner(x.Key)&&x.Value>=0),"owner economy");
            if(offline!=null){var alive=Owners.Where(x=>!eliminated.Contains(x)).Select(TeamOf).Distinct().ToArray();Require(Outcome==PlayableMatchOutcome.ManuallyFinished?(matchManual&&WinnerTeam==null):Outcome==PlayableMatchOutcome.Playing?(WinnerTeam==null&&(alive.Length!=1||offline.Roster.Select(x=>x.Team).Distinct().Count()==1)):Outcome==PlayableMatchOutcome.TeamWon&&offline.Roster.Select(x=>x.Team).Distinct().Count()>1&&alive.Length==1&&WinnerTeam==alive[0],"team result");Require(units.Values.Where(x=>eliminated.Contains(x.Owner)).All(x=>x.Target==0&&!x.HasAttackMove&&x.CurrentOrder==null)&&buildings.Values.Where(x=>eliminated.Contains(x.Owner)).All(x=>x.Orders.Count==0&&!x.RepeatTank&&x.Repair==null&&x.Phase!=ConstructionPhase.Pending),"eliminated inert actors");}

            Require(nextAiSequence>offlineSequences.Values.DefaultIfEmpty(0).Max(),"authority sequence watermark");
            Require(nextOrderRevision>0&&nextId>0&&nextProjectile>0&&nextProductionSequence>0&&nextResearchSequence>0&&credits>=0&&enemyCredits>=0&&incomeClock>=-1e-9&&incomeClock<1+1e-9&&elapsed>=0,"allocators/economy");
            var ids=new System.Collections.Generic.HashSet<int>();
            foreach(var u in units.Values){Require(u.Id>0&&u.Id<nextId&&ids.Add(u.Id)&&u.Health>0&&u.Health<=PlayableUnitRules.Health(profile,u.Kind)&&navigation.Crowd.TryGet(u.Id,out _),"unit identity/health/crowd");Require(Ref(u.Target)&&Ref(u.BurstTarget)&&u.Reload>=0&&u.Repath>=0&&u.StoppedSeconds>=0&&u.BurstDelay>=0&&u.BurstRemaining>=0&&u.BurstRemaining<=u.BurstSize&&u.BurstSequence>=0&&u.BurstSpread>=0,"combat continuation");
                Require(u.ScenarioIndex>=-1&&u.AuthoredUpgrade.HasValue==(u.ScenarioIndex>=0),"scenario lineage");if(u.ScenarioIndex>=0){Require(offline!=null&&u.ScenarioIndex<offline.ScenarioUnits.Count,"scenario source index");var authored=offline.ScenarioUnits[u.ScenarioIndex];Require(authored.LogicalPlayer==offline.Roster[(int)u.Owner].LogicalPlayer&&authored.Kind==u.Kind&&authored.Upgraded==u.AuthoredUpgrade.Value&&units.Values.Count(x=>x.ScenarioIndex==u.ScenarioIndex)==1,"authored variant binding");}
                var n=navigation.Crowd.Units.Single(x=>x.Id==u.Id);Require(HasOwner(u.Owner)&&n.Team==TeamOf(u.Owner)&&n.Radius==PlayableUnitRules.Radius(profile,u.Kind)&&n.TurnSpeed==PlayableUnitRules.Turn(profile,u.Kind)&&n.Speed==PlayableUnitRules.Speed(profile,u.Kind,u.Kind==PlayableEntityKind.Tank&&UnitUpgraded(u)),"typed crowd binding");
                if(u.LastOrder!=null){var o=u.LastOrder;Require(o.UnitId==u.Id&&o.Owner==u.Owner&&o.Generation==navigation.Generation&&o.Revision>0&&o.Revision<nextOrderRevision&&o.Sequence>0&&o.Tick>=0&&o.Tick<=Tick&&Enum.IsDefined(typeof(PlayableOrderOrigin),o.Origin)&&Enum.IsDefined(typeof(PlayableCommandKind),o.Kind),"order provenance binding");Require(o.Origin==PlayableOrderOrigin.Ai?!string.IsNullOrWhiteSpace(o.Source)&&o.JobId>0&&o.ActionId>0:o.Source==null&&o.JobId==0&&o.ActionId==0,"order origin correlation");Require(o.Kind==PlayableCommandKind.Hold||o.Kind==PlayableCommandKind.Stop||o.Kind==PlayableCommandKind.Move||o.Kind==PlayableCommandKind.AttackMove||o.Kind==PlayableCommandKind.Attack||o.Kind==PlayableCommandKind.Follow,"tactical order provenance");}
                if(u.CurrentOrder!=null){var o=u.CurrentOrder;Require(o.UnitId==u.Id&&o.Owner==u.Owner&&o.Generation==navigation.Generation&&o.CommandSequence>0&&o.IssuedTick>=0&&o.IssuedTick<=Tick&&Ref(o.TargetId),"tactical binding");if(o.Kind==PlayableTacticalOrderKind.Follow)Require(o.TargetId>0&&o.TargetId!=u.Id&&!buildings.ContainsKey(o.TargetId),"Follow leader identity");}}
            Require(navigation.Crowd.Units.Count==units.Count,"foreign crowd actor");
            var productionIds=new System.Collections.Generic.HashSet<long>();var slots=new System.Collections.Generic.HashSet<string>();
            foreach(var b in buildings.Values){
                Require(HasOwner(b.Owner)&&b.Id>0&&b.Id<nextId&&ids.Add(b.Id)&&sites.TryGetValue(b.SiteId,out _)&&b.TermsRevision>0&&b.PaidCost==TerritoryRules.Cost(Terms(b.TermsRevision),b.Kind)&&b.Health>=0&&b.Health<=TerritoryRules.Health(profile,b.Kind)+1e-9&&b.Build>=0&&b.Build<=TerritoryRules.Duration(Terms(b.TermsRevision),b.Kind)&&b.Ready==(b.Phase==ConstructionPhase.Ready)&&b.RetryAt>=0&&b.Orders.Count<=MaximumProductionOrders,"building identity/health/payment");
                var site=sites[b.SiteId];Require(slots.Add(b.SiteId+":"+b.SlotId)&&b.SlotId>=0&&b.SlotId<=site.Site.Slots.Count,"slot binding");
                if(b.SlotId==0)Require(b.ParentId==0&&b.Kind==site.Site.Kind&&site.CenterId==b.Id&&site.Locked==b.Owner&&b.Position.Equals(site.Site.Position),"center binding");
                else{Require(buildings.TryGetValue(b.ParentId,out var parent)&&parent.SiteId==b.SiteId&&parent.SlotId==0&&parent.Owner==b.Owner&&b.Position.Equals(site.Site.Slots[b.SlotId-1].Position)&&b.Heading==site.Site.Slots[b.SlotId-1].Heading,"parent/slot binding");}
                Require(b.LastDamageTime==null||(b.LastDamageTime>=0&&b.LastDamageTime<=elapsed),"damage clock");foreach(int id in b.Evacuated)Require(id>0&&id<nextId,"evacuation reference");
                for(int i=0;i<b.Orders.Count;i++){var o=b.Orders[i];Require(o!=null&&b.Kind==PlayableBuildingKind.Factory&&b.Ready&&b.Sale==null&&o.Id>0&&o.Id<nextProductionSequence&&productionIds.Add(o.Id)&&o.TermsRevision>0&&o.PaidCost==PlayableUnitRules.Cost(Terms(o.TermsRevision),o.Kind)&&o.PopulationCost==PlayableUnitRules.Population(Terms(o.TermsRevision),o.Kind)&&o.Duration==PlayableUnitRules.Duration(Terms(o.TermsRevision),o.Kind)&&o.Remaining>=0&&o.Remaining<=o.Duration&&(!o.Active||i==0),"production payment/order/capacity");}
                if(b.Upgrade!=null){var u=b.Upgrade;Require(b.Kind==PlayableBuildingKind.Refinery&&b.Ready&&u.TermsRevision>0&&u.PaidCost==Terms(u.TermsRevision).RefineryUpgradeCost&&u.Duration==Terms(u.TermsRevision).RefineryUpgradeSeconds&&u.Elapsed>=0&&u.Elapsed<=u.Duration&&u.Complete==(u.Elapsed==u.Duration),"refinery binding");}
                if(b.Sale!=null)Require(b.Repair==null&&b.Orders.Count==0&&b.Sale.Elapsed>=0&&b.Sale.Elapsed<b.Sale.Duration&&b.Sale.TermsRevision>0&&b.Sale.Duration==Terms(b.Sale.TermsRevision).BuildingSaleDemolitionSec,"sale settlement");
                if(b.Repair!=null){var r=b.Repair;var repairProfile=Terms(r.TermsRevision);double max=TerritoryRules.Health(repairProfile,b.Kind);Require(b.Ready&&r.MissingHealth>0&&r.MissingHealth<=max&&r.TermsRevision>0&&r.TotalCost==TerritoryRules.Cost(repairProfile,b.Kind)*repairProfile.BuildingRepairCostRatio*(r.MissingHealth/max)&&r.Duration==repairProfile.BuildingRepairDurationSec*(r.MissingHealth/max)&&r.PaidSeconds>=0&&r.PaidSeconds<r.Duration&&r.SettlementElapsed>=0&&r.SettlementElapsed<1+1e-9,"repair settlement");}
            }
            foreach(var s in sites.Values)Require((!s.Claimant.HasValue||HasOwner(s.Claimant.Value))&&(!s.Locked.HasValue||HasOwner(s.Locked.Value))&&s.Progress>=0&&s.Progress<=1&&(s.CenterId==0||(buildings.TryGetValue(s.CenterId,out var b)&&b.SlotId==0&&b.SiteId==s.Site.Id&&s.Locked==b.Owner)),"territory binding");
            var researchIds=new System.Collections.Generic.HashSet<long>();
            foreach(var pair in research)
            {
                Require(pair.Value.Count(o=>o!=null&&!o.Complete)<=MaximumResearchOrders&&pair.Value.Count(o=>o!=null&&o.Active)<=1&&pair.Value.All(o=>o!=null)&&pair.Value.Select(o=>o.Kind).Distinct().Count()==pair.Value.Count,"research capacity/kind");
                long previous=0;
                foreach(var o in pair.Value)
                {
                    Require(Enum.IsDefined(typeof(PlayableResearchKind),o.Kind)&&o.Id>previous&&o.Id<nextResearchSequence&&researchIds.Add(o.Id)&&o.CenterId>0&&o.CenterId<nextId,"research identity/order");previous=o.Id;
                    if(o.Complete)Require(!o.Active&&o.PaidCost>=0&&o.Duration>0&&o.Elapsed==o.Duration,"completed research terms");
                    else if(o.Active)Require(ReadyResearchCenter(o.CenterId,pair.Key)&&o.PaidCost>=0&&o.Duration>0&&o.Elapsed>=0&&o.Elapsed<o.Duration,"active research terms/center");
                    else Require(o.PaidCost==0&&o.Duration==0&&o.Elapsed==0,"unpaid waiting research");
                    if(!o.Complete&&buildings.TryGetValue(o.CenterId,out var center))Require(center.Owner==pair.Key&&center.Kind==PlayableBuildingKind.ScientificCenter,"research owner/history");
                }
            }
            foreach(PlayableOwner owner in Owners)Require(Population(owner).Living+Population(owner).Reserved<=profile.ArmyCapacity,"population capacity");
            var navState=navigation.CaptureState();
            foreach(var order in navState.Orders)Require(order.Entity>0&&Ref(order.Entity),"navigation order reference");
            foreach(var request in navState.Requests){Require(request.Entity!=0&&request.Entity!=int.MinValue&&Ref(Math.Abs(request.Entity)),"navigation request reference");for(int i=0;i<request.ProfileValues.Length;i++)if(i<7||i>9)Require(request.ProfileValues[i]==NavigationProfile.Metadata[i].Read(profile.Navigation),"navigation effective profile binding");}
            foreach(var point in navState.Reservations.Concat(navState.RetainedGoals))Require(units.ContainsKey(point.Entity),"navigation live intent binding");
            foreach(int index in navState.PendingRequestIndices)Require(units.ContainsKey(navState.Requests[index].Entity),"pending actor binding");
            foreach(int index in navState.ProbeRequestIndices){var req=navState.Requests[index];Require(rallyWork.TryGetValue(-req.Entity,out var work)&&work.Request!=null&&work.Request.Request==req.Request,"rally probe binding");}
            var shots=new System.Collections.Generic.HashSet<int>();foreach(var p in projectiles){Require(p!=null&&HasOwner(p.Faction)&&p.Id>0&&p.Id<nextProjectile&&shots.Add(p.Id)&&p.Owner>0&&Ref(p.Owner)&&Ref(p.Target)&&p.Damage>0,"projectile identity");if(p.Rocket!=null){var r=p.Rocket;Require(p.Kind==PlayableEntityKind.Shkval&&r.Flight!=null&&r.Elapsed>=0&&r.Elapsed<r.Flight.Duration&&r.Radius==Terms(p.TermsRevision).ShkvalProjectileRadius&&r.BlastRadius==Terms(p.TermsRevision).ShkvalBlastRadius&&r.MarkerStartRadius==Terms(p.TermsRevision).ShkvalMarkerStartRadius&&r.MarkerOpacity==Terms(p.TermsRevision).ShkvalMarkerOpacity&&r.BuildingHeight==Terms(p.TermsRevision).ShkvalBuildingCollisionHeight&&r.Predicted.Progress>=0&&r.Predicted.Progress<=1&&Ref(r.Predicted.BuildingId),"rocket flight");}else Require(p.Speed>0&&p.Radius>=0&&p.Remaining>0,"shell flight");}
            foreach(var d in centerDamage.Values)Require(d.Generation==navigation.Generation&&d.Tick>=0&&d.Tick<=Tick&&d.Damage>0&&d.AttackerId>0&&Ref(d.AttackerId)&&d.CenterId>0&&Ref(d.CenterId)&&(!buildings.TryGetValue(d.CenterId,out var b)||(b.Owner==d.Owner&&TerritoryRules.Center(b.Kind))),"center damage fact");
            foreach(var i in impacts)Require(i!=null&&HasOwner(i.Owner)&&i.Id>0&&i.Id<nextProjectile&&i.Tick>=0&&i.Tick<=Tick&&i.Radius>0&&i.VisibleMask>=0&&i.VisibleMask<=(1<<ownerCredits.Count)-1,"impact fact");
            foreach(var receipt in rallyReceipts)Require(receipt!=null&&receipt.Sequence>0&&receipt.AppliedTick>=0&&receipt.AppliedTick<=Tick&&receipt.Status!=PlayableCommandStatus.Accepted,"rally terminal receipt");
            var obstacles=new System.Collections.Generic.List<NavObstacle>(StaticObstacles());foreach(var b in buildings.Values)if(b.Phase!=ConstructionPhase.Pending){double r=BuildingRadius(b.Kind);obstacles.Add(new NavObstacle(b.Position.X-r,b.Position.Z-r,b.Position.X+r,b.Position.Z+r));}
            Require(Geometry.HalfExtent==profile.ArenaHalfExtent&&obstacles.Count==Geometry.Obstacles.Count&&obstacles.Select((o,i)=>o.GeometryEquals(Geometry.Obstacles[i])).All(x=>x),"saved building geometry");
        }
    }
}
