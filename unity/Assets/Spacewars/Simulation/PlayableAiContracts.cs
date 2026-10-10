using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Presentation-safe owner policy input, never a view of PlayableDomain.
    public sealed class PlayableAiObservation
    {
        public const int CurrentSchemaVersion=16;
        public const int ParticipantSchemaVersion=17;
        public int SchemaVersion{get;}
        private readonly PlayableEntitySnapshot[] entities;
        private readonly PlayableBuildingSnapshot[] buildings;
        private readonly TerritorySiteSnapshot[] sites;
        private readonly PlayableImpactSnapshot[] impacts;
        private readonly PlayableResearchAvailabilitySnapshot[] researchAvailability;
        private readonly PlayableResearchOrderSnapshot[] ownerResearch;
        private readonly PlayablePublicScoutObjective[] publicScoutObjectives;
        private readonly PlayableCenterDamageSnapshot[] ownCenterDamage;
        private readonly PlayableRouteProof[] routeProofs;
        private readonly PlayableArtillerySupportSnapshot[] artillerySupport;
        private PlayableAiObservation(PlayableSnapshot snapshot)
        {
            Intel=new Spacewars.Simulation.Ai.AiIntelDelta(snapshot.Tick,snapshot.IntelEnvelopes,snapshot.Vision);Vision=snapshot.Vision;SchemaVersion=snapshot.Participants.Count>0?ParticipantSchemaVersion:CurrentSchemaVersion;HomeSiteId=snapshot.HomeSiteId;OwnerId=snapshot.OwnerId;Team=snapshot.Team;Participants=snapshot.Participants;Owner=snapshot.Owner;ProfileId=snapshot.ProfileId; ProfileRevision=snapshot.ProfileRevision; Generation=snapshot.Generation; Seed=snapshot.Seed;
            var owner=Owner;
            SnapshotSequence=snapshot.Sequence; Tick=snapshot.Tick; Credits=snapshot.Credits; SettledIncome=snapshot.SettledIncome; IncomePerSecond=snapshot.IncomePerSecond;
            Population=snapshot.Population;ownerResearch=snapshot.OwnerResearch.ToArray(); entities=snapshot.Entities.Select(x=>new PlayableEntitySnapshot(x.Id,x.Owner,x.Kind,x.Position,x.Health,x.Moving,x.TargetId,x.HullHeading,x.TurretHeading,
                x.Owner==owner&&x.CurrentOrder?.Owner==owner&&x.CurrentOrder.UnitId==x.Id&&x.CurrentOrder.Generation==snapshot.Generation&&x.CurrentOrder.IssuedTick<=snapshot.Tick?x.CurrentOrder:null,
                completion:x.Owner==owner&&x.Completion?.Order.Owner==owner&&x.Completion.Order.Generation==snapshot.Generation?x.Completion:null,location:x.Owner==owner?x.Location:null,upgraded:x.Upgraded,held:x.Owner==owner&&x.Held,navigationOutcome:x.Owner==owner?x.NavigationOutcome:NavigationOutcome.Idle,orderStamp:x.Owner==owner?x.OrderStamp:null,queue:x.Owner==owner?x.Queue:null)).ToArray(); buildings=snapshot.Buildings.ToArray();
            ownCenterDamage=snapshot.OwnCenterDamage.Where(x=>x.Owner==owner&&x.Generation==snapshot.Generation&&x.Tick<=snapshot.Tick&&x.Damage>0&&snapshot.Buildings.Any(b=>b.Id==x.CenterId&&b.Owner==owner&&b.Health>0&&b.Phase!=ConstructionPhase.Pending&&(b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost))&&entities.Any(e=>e.Id==x.AttackerId&&snapshot.IsHostile(e.Owner)&&e.Health>0)).Select(x=>x.Copy()).ToArray();
            routeProofs=snapshot.RouteProofs.Where(p=>p.Owner==owner&&p.Generation==snapshot.Generation&&p.Tick==snapshot.Tick&&p.GeometryRevision>0&&p.Radius>0&&p.Path.Count>0&&
                entities.Any(e=>e.Id==p.UnitId&&e.Owner==owner&&e.Position.X==p.Origin.X&&e.Position.Z==p.Origin.Z)&&
                (p.Kind==PlayableRouteTargetKind.VisibleEnemy&&entities.Any(e=>e.Id==p.TargetId&&snapshot.IsHostile(e.Owner)&&e.Position.X==p.Target.X&&e.Position.Z==p.Target.Z)||
                 p.Kind==PlayableRouteTargetKind.VisibleEnemy&&buildings.Any(b=>b.Id==p.TargetId&&snapshot.IsHostile(b.Owner)&&b.Position.X==p.Target.X&&b.Position.Z==p.Target.Z)||
                 p.Kind==PlayableRouteTargetKind.FriendlyAnchor&&entities.Any(e=>e.Id==p.TargetId&&e.Owner==owner&&e.Position.X==p.Target.X&&e.Position.Z==p.Target.Z)||
                 p.Kind==PlayableRouteTargetKind.FriendlyAnchor&&snapshot.ActiveProfile!=null&&snapshot.ActiveProfile.ProfileId==snapshot.ProfileId&&snapshot.ActiveProfile.Revision==snapshot.ProfileRevision&&entities.Any(e=>e.Id==p.TargetId&&e.Id!=p.UnitId&&e.Owner==owner&&PlayableUnitRules.FollowGoal(snapshot.ActiveProfile,e.Position,e.HullHeading).Equals(p.Target))||
                 p.Kind==PlayableRouteTargetKind.FriendlyAnchor&&entities.Any(e=>e.Id==p.TargetId&&e.Id==p.UnitId&&e.Owner==owner&&e.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&e.CurrentOrder.Destination.Equals(p.Target))||
                 p.Kind==PlayableRouteTargetKind.FriendlyAnchor&&buildings.Any(b=>b.Id==p.TargetId&&b.Owner==owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.Position.Equals(p.Target))||
                 p.Kind==PlayableRouteTargetKind.PublicObjective&&snapshot.PublicScoutObjectives.Any(o=>o.SiteId==p.TargetId&&o.Reachable&&o.Approach.X==p.Target.X&&o.Approach.Z==p.Target.Z))&&
                (p.Kind==PlayableRouteTargetKind.PublicObjective||p.Path.All(point=>snapshot.Vision!=null&&snapshot.Vision.IsVisible(point)))).Select(p=>p.Copy()).ToArray();
            artillerySupport=snapshot.ArtillerySupport.Where(a=>a.Generation==snapshot.Generation&&a.Tick==snapshot.Tick&&entities.Any(e=>e.Id==a.UnitId&&e.Owner==owner&&e.Kind==PlayableEntityKind.Shkval&&e.Health>0)&&(!a.Position.HasValue||snapshot.Vision!=null&&snapshot.Vision.IsVisible(a.Position.Value))).ToArray();
            sites=snapshot.Sites.ToArray(); impacts=snapshot.Impacts.ToArray();researchAvailability=snapshot.ResearchAvailability.ToArray();publicScoutObjectives=snapshot.PublicScoutObjectives.ToArray();
        }
        public static PlayableAiObservation From(PlayableSnapshot snapshot)
        {
            if(snapshot==null)throw new ArgumentNullException(nameof(snapshot));
            return new PlayableAiObservation(snapshot);
        }
        public Spacewars.Simulation.Ai.AiIntelDelta Intel{get;}
        public TeamVisionSnapshot Vision{get;}
        public int HomeSiteId{get;}
        public bool IsHostile(PlayableOwner other)=>Participants.Count==0?other!=Owner:Participants[(int)other].Team!=Team;
        public PlayableOwner Owner{get;} public string OwnerId{get;} public int Team{get;} public IReadOnlyList<OfflineParticipant> Participants{get;} public string ProfileId{get;} public int ProfileRevision{get;} public long Generation{get;} public int Seed{get;}
        public long SnapshotSequence{get;} public long Tick{get;} public int Credits{get;} public double IncomePerSecond{get;} public double SettledIncome{get;}
        public PlayablePopulationSnapshot Population{get;}
        public IReadOnlyList<PlayableResearchAvailabilitySnapshot> ResearchAvailability=>Array.AsReadOnly(researchAvailability);
        public IReadOnlyList<PlayableResearchOrderSnapshot> OwnerResearch=>Array.AsReadOnly(ownerResearch);
        public IReadOnlyList<PlayablePublicScoutObjective> PublicScoutObjectives=>Array.AsReadOnly(publicScoutObjectives);
        public IReadOnlyList<PlayableEntitySnapshot> Entities=>Array.AsReadOnly(entities);
        public IReadOnlyList<PlayableBuildingSnapshot> Buildings=>Array.AsReadOnly(buildings);
        public IReadOnlyList<TerritorySiteSnapshot> Sites=>Array.AsReadOnly(sites);
        public IReadOnlyList<PlayableImpactSnapshot> Impacts=>Array.AsReadOnly(impacts);
        public IReadOnlyList<PlayableCenterDamageSnapshot> OwnCenterDamage=>Array.AsReadOnly(ownCenterDamage);
        public IReadOnlyList<PlayableRouteProof> RouteProofs=>Array.AsReadOnly(routeProofs);
        public IReadOnlyList<PlayableArtillerySupportSnapshot> ArtillerySupport=>Array.AsReadOnly(artillerySupport);
        public string Identity=>$"observation-{SchemaVersion}:{ProfileId}@{ProfileRevision}:{OwnerId}:{Generation}:{SnapshotSequence}:{Tick}:{Seed}";
    }

    public sealed class PlayableAiAction
    {
        private readonly int[] entityIds;
        public PlayableAiAction(long actionId,string playerId,string profileId,int profileRevision,long generation,long snapshotSequence,PlayableCommandKind kind,int[] entityIds=null,NavPoint target=default(NavPoint),int siteId=0,int slotId=0,PlayableBuildingKind buildingKind=PlayableBuildingKind.Headquarters,int parentId=0,int targetId=0,long productionOrderId=0,PlayableEntityKind unitKind=PlayableEntityKind.Tank,PlayableResearchKind researchKind=PlayableResearchKind.TankChassis,int seed=0,string sourceIdentity=null)
        {
            ActionId=actionId;PlayerId=playerId;ProfileId=profileId;ProfileRevision=profileRevision;Generation=generation;SnapshotSequence=snapshotSequence;Seed=seed;SourceIdentity=sourceIdentity;Kind=kind;
            this.entityIds=entityIds==null?Array.Empty<int>():entityIds.ToArray();Target=target;SiteId=siteId;SlotId=slotId;BuildingKind=buildingKind;ParentId=parentId;TargetId=targetId;ProductionOrderId=productionOrderId;UnitKind=unitKind;ResearchKind=researchKind;
        }
        public long ActionId{get;} public string PlayerId{get;} public string ProfileId{get;} public int ProfileRevision{get;} public long Generation{get;} public long SnapshotSequence{get;} public int Seed{get;} public string SourceIdentity{get;}
        public PlayableCommandKind Kind{get;} public NavPoint Target{get;} public int SiteId{get;} public int SlotId{get;} public PlayableBuildingKind BuildingKind{get;} public int ParentId{get;} public int TargetId{get;} public long ProductionOrderId{get;} public PlayableEntityKind UnitKind{get;} public PlayableResearchKind ResearchKind{get;}
        public IReadOnlyList<int> EntityIds=>Array.AsReadOnly(entityIds);
        public int[] CopyEntityIds()=>entityIds.ToArray();
    }

    public enum PlayableAiDeliveryStatus { Scheduled, Accepted, Applied, Rejected, Stale, Cancelled, Stopped, InvalidOwner, InvalidAction }

    [Serializable]
    public sealed class PlayableAiTraceRecord
    {
        public PlayableAiTraceRecord(string observationIdentity,long actionId,long dueTick,long commandSequence,long applicationTick,PlayableAiDeliveryStatus status,PlayableCommandStatus? runtimeStatus,string message,string ownerId=null,string sourceIdentity=null,Spacewars.Simulation.Ai.AiReceiptIdentity receiptIdentity=null,string policy=null,PlayableCommandKind? kind=null)
        {Policy=policy;Kind=kind;ReceiptIdentity=receiptIdentity;ObservationIdentity=observationIdentity;ActionId=actionId;DueTick=dueTick;CommandSequence=commandSequence;ApplicationTick=applicationTick;Status=status;RuntimeStatus=runtimeStatus;Message=message??"";
            var parts=(observationIdentity??"").Split(':');var parsed=parts.Length>=7&&(parts[0]=="observation-13"||parts[0]=="observation-14"||parts[0]=="observation-"+PlayableAiObservation.CurrentSchemaVersion||parts[0]=="observation-"+PlayableAiObservation.ParticipantSchemaVersion)?parts[parts.Length-5]:null;
            OwnerId=ownerId??(parsed=="player-1"||parsed=="enemy-1"?parsed:null);SourceIdentity=sourceIdentity;}
        public string Policy{get;} public PlayableCommandKind? Kind{get;}
        public Spacewars.Simulation.Ai.AiReceiptIdentity ReceiptIdentity{get;}
        public string ObservationIdentity{get;} public string OwnerId{get;} public string SourceIdentity{get;} public long ActionId{get;} public long DueTick{get;} public long CommandSequence{get;} public long ApplicationTick{get;} public PlayableAiDeliveryStatus Status{get;} public PlayableCommandStatus? RuntimeStatus{get;} public string Message{get;}
    }

    [Serializable]
    public sealed class PlayableAiDiagnosticIdentity
    {
        public PlayableAiDiagnosticIdentity(string scenarioId,string sourceIdentity,PlayableAiObservation observation):this(scenarioId,sourceIdentity,"adapter-u6-fixtures-v1",observation){}
        public PlayableAiDiagnosticIdentity(string scenarioId,string sourceIdentity,string fixtureBankIdentity,PlayableAiObservation observation)
        {
            if(String.IsNullOrEmpty(scenarioId)||String.IsNullOrEmpty(sourceIdentity)||String.IsNullOrEmpty(fixtureBankIdentity))throw new ArgumentException("Diagnostic provenance is required.");
            if(observation==null)throw new ArgumentNullException(nameof(observation));
            ScenarioId=scenarioId;SourceIdentity=sourceIdentity;FixtureBankIdentity=fixtureBankIdentity;OwnerId=observation.OwnerId;ProfileId=observation.ProfileId;ProfileRevision=observation.ProfileRevision;SchemaVersion=PlayableCommand.CurrentSchemaVersion;ObservationSchemaVersion=observation.SchemaVersion;ObservationIdentity=observation.Identity;
        }
        public string ScenarioId{get;} public string SourceIdentity{get;} public string FixtureBankIdentity{get;} public string OwnerId{get;} public string ProfileId{get;} public int ProfileRevision{get;} public int SchemaVersion{get;} public int ObservationSchemaVersion{get;} public string Policy{get;} public PlayableCommandKind? Kind{get;}
        public Spacewars.Simulation.Ai.AiReceiptIdentity ReceiptIdentity{get;}
        public string ObservationIdentity{get;}
        public bool Matches(PlayableAiObservation observation)=>observation!=null&&observation.OwnerId==OwnerId&&observation.ProfileId==ProfileId&&observation.ProfileRevision==ProfileRevision&&SchemaVersion==PlayableCommand.CurrentSchemaVersion&&ObservationSchemaVersion==observation.SchemaVersion;
    }

    // This is a diagnostic checkpoint, not player save/replay state. It is intentionally limited to published trace semantics.
    [Serializable]
    public sealed class PlayableAiDiagnosticTimeline
    {
        private readonly PlayableAiTraceRecord[] records;
        public PlayableAiDiagnosticTimeline(PlayableAiDiagnosticIdentity identity,IEnumerable<PlayableAiTraceRecord> entries)
        {Identity=identity??throw new ArgumentNullException(nameof(identity));records=(entries??Enumerable.Empty<PlayableAiTraceRecord>()).ToArray();}
        public PlayableAiDiagnosticIdentity Identity{get;} public IReadOnlyList<PlayableAiTraceRecord> Records=>Array.AsReadOnly(records);
        public bool SemanticallyEquals(PlayableAiDiagnosticTimeline other)
        {
            if(other==null||Identity.ScenarioId!=other.Identity.ScenarioId||Identity.SourceIdentity!=other.Identity.SourceIdentity||Identity.FixtureBankIdentity!=other.Identity.FixtureBankIdentity||Identity.OwnerId!=other.Identity.OwnerId||Identity.ProfileId!=other.Identity.ProfileId||Identity.ProfileRevision!=other.Identity.ProfileRevision||Identity.SchemaVersion!=other.Identity.SchemaVersion||Identity.ObservationSchemaVersion!=other.Identity.ObservationSchemaVersion||records.Length!=other.records.Length)return false;
            return records.Zip(other.records,(a,b)=>a.ObservationIdentity==b.ObservationIdentity&&a.OwnerId==b.OwnerId&&a.SourceIdentity==b.SourceIdentity&&a.ActionId==b.ActionId&&a.DueTick==b.DueTick&&a.CommandSequence==b.CommandSequence&&a.ApplicationTick==b.ApplicationTick&&a.Status==b.Status&&a.RuntimeStatus==b.RuntimeStatus).All(x=>x);
        }
    }
}
