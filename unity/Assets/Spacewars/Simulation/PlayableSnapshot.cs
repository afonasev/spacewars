using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Spacewars.Simulation
{
    public enum PlayableOwner { Player, Enemy, Third, Fourth, Fifth, Sixth, Seventh, Eighth }
    public enum PlayableEntityKind { Tank, Explorer, Shkval }
    public enum PlayableBuildingKind { Headquarters, Factory, Refinery, Outpost, Mine, ScientificCenter }
    public enum PlayableMatchOutcome { Playing, PlayerWon, PlayerLost, TeamWon }
    public enum PlayableTacticalOrderKind { Move, AttackMove, Attack, Follow }
    public enum PlayableRouteTargetKind { VisibleEnemy, PublicObjective, FriendlyAnchor }
    // Computed by authority from the current owner projection; no hidden target or path is published.
    public sealed class PlayableArtillerySupportSnapshot
    {
        public PlayableArtillerySupportSnapshot(int unitId,long generation,long tick,bool supported,bool threatened,bool usefulShot,bool positionAllowsShot,NavPoint? position)
        {UnitId=unitId;Generation=generation;Tick=tick;Supported=supported;Threatened=threatened;UsefulShot=usefulShot;PositionAllowsShot=positionAllowsShot;Position=position;}
        public int UnitId{get;} public long Generation{get;} public long Tick{get;}
        public bool Supported{get;} public bool Threatened{get;} public bool UsefulShot{get;} public bool PositionAllowsShot{get;} public NavPoint? Position{get;}
    }
    // A route is a current navigation result for one semantic leg, never a command or a cached mission path.
    public sealed class PlayableRouteRequest
    {
        public PlayableRouteRequest(int unitId,PlayableRouteTargetKind kind,int targetId,long generation,long tick,NavPoint origin,double radius,NavPoint target)
        {UnitId=unitId;Kind=kind;TargetId=targetId;Generation=generation;Tick=tick;Origin=origin;Radius=radius;Target=target;}
        public int UnitId{get;} public PlayableRouteTargetKind Kind{get;} public int TargetId{get;} public long Generation{get;} public long Tick{get;}
        public NavPoint Origin{get;} public double Radius{get;} public NavPoint Target{get;}
    }
    public sealed class PlayableRouteProof
    {
        private readonly NavPoint[] path;
        public PlayableRouteProof(int unitId,PlayableOwner owner,long generation,long tick,int geometryRevision,NavPoint origin,double radius,PlayableRouteTargetKind kind,int targetId,NavPoint target,NavPoint goal,NavPoint[] path)
        {UnitId=unitId;Owner=owner;Generation=generation;Tick=tick;GeometryRevision=geometryRevision;Origin=origin;Radius=radius;Kind=kind;TargetId=targetId;Target=target;Goal=goal;this.path=(NavPoint[])(path??Array.Empty<NavPoint>()).Clone();}
        public int UnitId{get;} public PlayableOwner Owner{get;} public long Generation{get;} public long Tick{get;} public int GeometryRevision{get;}
        public NavPoint Origin{get;} public double Radius{get;} public PlayableRouteTargetKind Kind{get;} public int TargetId{get;} public NavPoint Target{get;} public NavPoint Goal{get;}
        public IReadOnlyList<NavPoint> Path=>Array.AsReadOnly(path);
        public PlayableRouteProof Copy()=>new PlayableRouteProof(UnitId,Owner,Generation,Tick,GeometryRevision,Origin,Radius,Kind,TargetId,Target,Goal,path);
    }
    [Serializable]
    public sealed class PlayableTacticalOrderSnapshot
    {
        public PlayableTacticalOrderSnapshot(int unitId,PlayableOwner owner,long generation,long commandSequence,long issuedTick,PlayableTacticalOrderKind kind,NavPoint destination,int targetId)
        {UnitId=unitId;Owner=owner;Generation=generation;CommandSequence=commandSequence;IssuedTick=issuedTick;Kind=kind;Destination=destination;TargetId=targetId;}
        public int UnitId{get;} public PlayableOwner Owner{get;} public long Generation{get;} public long CommandSequence{get;} public long IssuedTick{get;}
        public PlayableTacticalOrderKind Kind{get;} public NavPoint Destination{get;} public int TargetId{get;}
        public PlayableTacticalOrderSnapshot Copy()=>new PlayableTacticalOrderSnapshot(UnitId,Owner,Generation,CommandSequence,IssuedTick,Kind,Destination,TargetId);
    }
    [Serializable]
    public sealed class PlayableCenterDamageSnapshot
    {
        public PlayableCenterDamageSnapshot(int centerId,PlayableOwner owner,int attackerId,long generation,long tick,int damage)
        {CenterId=centerId;Owner=owner;AttackerId=attackerId;Generation=generation;Tick=tick;Damage=damage;}
        public int CenterId{get;} public PlayableOwner Owner{get;} public int AttackerId{get;} public long Generation{get;} public long Tick{get;} public int Damage{get;}
        public PlayableCenterDamageSnapshot Copy()=>new PlayableCenterDamageSnapshot(CenterId,Owner,AttackerId,Generation,Tick,Damage);
    }
    // Authority checkpoint for these facts only; this is not a complete match save.
    [Serializable]
    public sealed class PlayableTacticalObservationState
    {
        private readonly PlayableTacticalOrderSnapshot[] orders;
        private readonly PlayableCenterDamageSnapshot[] damage;
        public PlayableTacticalObservationState(long generation,long tick,PlayableTacticalOrderSnapshot[] orders,PlayableCenterDamageSnapshot[] damage)
        {Generation=generation;Tick=tick;this.orders=Array.ConvertAll(orders??Array.Empty<PlayableTacticalOrderSnapshot>(),x=>x.Copy());this.damage=Array.ConvertAll(damage??Array.Empty<PlayableCenterDamageSnapshot>(),x=>x.Copy());}
        public long Generation{get;} public long Tick{get;}
        public IReadOnlyList<PlayableTacticalOrderSnapshot> Orders=>Array.AsReadOnly(orders);
        public IReadOnlyList<PlayableCenterDamageSnapshot> Damage=>Array.AsReadOnly(damage);
        public PlayableTacticalObservationState Copy()=>new PlayableTacticalObservationState(Generation,Tick,orders,damage);
        public string Serialize()
        {
            var culture=CultureInfo.InvariantCulture;
            var rows=new List<string>{"tactical-observation-v1|"+Generation.ToString(culture)+"|"+Tick.ToString(culture)};
            rows.AddRange(orders.OrderBy(o=>o.UnitId).Select(o=>string.Join("|",new[]{"O",o.UnitId.ToString(culture),((int)o.Owner).ToString(culture),o.Generation.ToString(culture),o.CommandSequence.ToString(culture),o.IssuedTick.ToString(culture),((int)o.Kind).ToString(culture),o.Destination.X.ToString("R",culture),o.Destination.Z.ToString("R",culture),o.TargetId.ToString(culture)})));
            rows.AddRange(damage.OrderBy(d=>d.CenterId).ThenBy(d=>d.AttackerId).Select(d=>string.Join("|",new[]{"D",d.CenterId.ToString(culture),((int)d.Owner).ToString(culture),d.AttackerId.ToString(culture),d.Generation.ToString(culture),d.Tick.ToString(culture),d.Damage.ToString(culture)})));
            return string.Join("\n",rows);
        }
        public static PlayableTacticalObservationState Deserialize(string value)
        {
            if(String.IsNullOrEmpty(value))throw new FormatException("Missing tactical checkpoint.");
            var rows=value.Split('\n');var header=rows[0].Split('|');
            if(header.Length!=3||header[0]!="tactical-observation-v1")throw new FormatException("Unsupported tactical checkpoint.");
            var culture=CultureInfo.InvariantCulture;long generation=long.Parse(header[1],culture),tick=long.Parse(header[2],culture);
            var parsedOrders=new List<PlayableTacticalOrderSnapshot>();var parsedDamage=new List<PlayableCenterDamageSnapshot>();
            foreach(var row in rows.Skip(1))
            {
                var parts=row.Split('|');
                if(parts.Length==10&&parts[0]=="O")
                {
                    var owner=(PlayableOwner)int.Parse(parts[2],culture);var kind=(PlayableTacticalOrderKind)int.Parse(parts[6],culture);
                    if(!Enum.IsDefined(typeof(PlayableOwner),owner)||!Enum.IsDefined(typeof(PlayableTacticalOrderKind),kind))throw new FormatException("Invalid tactical order enum.");
                    parsedOrders.Add(new PlayableTacticalOrderSnapshot(int.Parse(parts[1],culture),owner,long.Parse(parts[3],culture),long.Parse(parts[4],culture),long.Parse(parts[5],culture),kind,new NavPoint(double.Parse(parts[7],culture),double.Parse(parts[8],culture)),int.Parse(parts[9],culture)));
                }
                else if(parts.Length==7&&parts[0]=="D")
                {
                    var owner=(PlayableOwner)int.Parse(parts[2],culture);if(!Enum.IsDefined(typeof(PlayableOwner),owner))throw new FormatException("Invalid damage owner.");
                    parsedDamage.Add(new PlayableCenterDamageSnapshot(int.Parse(parts[1],culture),owner,int.Parse(parts[3],culture),long.Parse(parts[4],culture),long.Parse(parts[5],culture),int.Parse(parts[6],culture)));
                }
                else throw new FormatException("Malformed tactical checkpoint row.");
            }
            return new PlayableTacticalObservationState(generation,tick,parsedOrders.ToArray(),parsedDamage.ToArray());
        }
    }
    public sealed class PlayableEntitySnapshot { public PlayableEntitySnapshot(int id,PlayableOwner owner,PlayableEntityKind kind,NavPoint position,int health,bool moving,int targetId,double hullHeading,double turretHeading,PlayableTacticalOrderSnapshot currentOrder=null,bool upgraded=false,bool held=false,NavigationOutcome navigationOutcome=NavigationOutcome.Idle,PlayableOrderStamp orderStamp=null){OrderStamp=orderStamp;NavigationOutcome=navigationOutcome;Held=held;Upgraded=upgraded;Id=id;Owner=owner;Kind=kind;Position=position;Health=health;Moving=moving;TargetId=targetId;HullHeading=hullHeading;TurretHeading=turretHeading;CurrentOrder=currentOrder?.Copy();} public PlayableOrderStamp OrderStamp{get;} public int Id{get;} public PlayableOwner Owner{get;} public PlayableEntityKind Kind{get;} public NavPoint Position{get;} public int Health{get;} public bool Moving{get;} public int TargetId{get;} public double HullHeading{get;} public double TurretHeading{get;} public PlayableTacticalOrderSnapshot CurrentOrder{get;} public bool Upgraded{get;} public bool Held{get;} public NavigationOutcome NavigationOutcome{get;} }
    public sealed class PlayableProductionOrderSnapshot
    {
        public PlayableProductionOrderSnapshot(long id,PlayableEntityKind kind,int paidCost,int populationCost,double duration,double remaining,bool active)
        {Id=id;Kind=kind;PaidCost=paidCost;PopulationCost=populationCost;Duration=duration;Remaining=remaining;Active=active;}
        public long Id{get;} public PlayableEntityKind Kind{get;} public int PaidCost{get;} public int PopulationCost{get;}
        public double Duration{get;} public double Remaining{get;} public bool Active{get;}
        public double Progress=>Active?1-Remaining/Duration:0;
    }
    public sealed class PlayablePopulationSnapshot
    {
        public PlayablePopulationSnapshot(int living,int reserved,int capacity){Living=living;Reserved=reserved;Capacity=capacity;}
        public int Living{get;} public int Reserved{get;} public int Capacity{get;}
    }
    public enum PlayablePublicScoutObjectiveRole { PossibleEnemyStart }
    // Immutable authored geography for an AI policy; it intentionally carries no live ownership or vision fact.
    public sealed class PlayablePublicScoutObjective
    {
        public PlayablePublicScoutObjective(int siteId,NavPoint approach,PlayablePublicScoutObjectiveRole role,bool reachable)
        {SiteId=siteId;Approach=approach;Role=role;Reachable=reachable;}
        public int SiteId{get;} public NavPoint Approach{get;} public PlayablePublicScoutObjectiveRole Role{get;} public bool Reachable{get;}
    }
    public sealed class PlayableBuildingLifecycleSnapshot
    {
        public PlayableBuildingLifecycleSnapshot(bool selling,double saleProgress,bool repairing,bool waiting,double paidSeconds,double repairDuration,string saleBlocked,string repairBlocked,int cascadeCount,int refund,bool lastCenter)
        { Selling=selling;SaleProgress=saleProgress;Repairing=repairing;WaitingForCredits=waiting;PaidSeconds=paidSeconds;RepairDuration=repairDuration;SaleBlockedReason=saleBlocked;RepairBlockedReason=repairBlocked;CascadeCount=cascadeCount;Refund=refund;LastCenter=lastCenter; }
        public bool Selling{get;} public double SaleProgress{get;} public bool Repairing{get;} public bool WaitingForCredits{get;}
        public double PaidSeconds{get;} public double RepairDuration{get;} public string SaleBlockedReason{get;} public string RepairBlockedReason{get;}
        public int CascadeCount{get;} public int Refund{get;} public bool LastCenter{get;}
    }
    public sealed class PlayableBuildingPrivateState
    {
        public PlayableRefineryUpgradeSnapshot Upgrade{get;}
        public PlayableBuildingLifecycleSnapshot Lifecycle{get;}
        public PlayableBuildingPrivateState(int queue,double progress,NavPoint rally,PlayableProductionOrderSnapshot[] orders=null,bool repeat=false,PlayableBuildingLifecycleSnapshot lifecycle=null,PlayableRefineryUpgradeSnapshot upgrade=null,PlayableEntityKind repeatKind=PlayableEntityKind.Tank,PlayableResearchOrderSnapshot[] research=null,bool hasRally=false,NavPoint? pendingRally=null){HasRally=hasRally;PendingRally=pendingRally;Research=Array.AsReadOnly((PlayableResearchOrderSnapshot[])(research??Array.Empty<PlayableResearchOrderSnapshot>()).Clone());RepeatKind=repeatKind;Upgrade=upgrade;Lifecycle=lifecycle;Orders=Array.AsReadOnly((PlayableProductionOrderSnapshot[])(orders??Array.Empty<PlayableProductionOrderSnapshot>()).Clone());Repeat=repeat;QueueCount=queue;ProductionProgress=progress;Rally=rally;}
        public IReadOnlyList<PlayableResearchOrderSnapshot> Research{get;}
        public IReadOnlyList<PlayableProductionOrderSnapshot> Orders{get;} public bool Repeat{get;} public PlayableEntityKind RepeatKind{get;}
        public int QueueCount{get;} public double ProductionProgress{get;} public NavPoint Rally{get;} public bool HasRally{get;} public NavPoint? PendingRally{get;}
    }
    public sealed class PlayableBuildingSnapshot { public PlayableBuildingSnapshot(int id,PlayableOwner owner,PlayableBuildingKind kind,NavPoint position,int health,double progress,int queueCount,double productionProgress,NavPoint rally,int siteId=0,int slotId=0,int parentId=0,ConstructionPhase phase=ConstructionPhase.Ready,string blockedReason=null,double heading=0,bool includePrivateState=true,PlayableProductionOrderSnapshot[] orders=null,bool repeat=false,PlayableBuildingLifecycleSnapshot lifecycle=null,PlayableRefineryUpgradeSnapshot upgrade=null,bool refineryUpgraded=false,PlayableEntityKind repeatKind=PlayableEntityKind.Tank,PlayableResearchOrderSnapshot[] research=null,bool hasRally=false,NavPoint? pendingRally=null,double? exactHealth=null){ExactHealth=exactHealth;RefineryUpgraded=refineryUpgraded;PrivateState=includePrivateState?new PlayableBuildingPrivateState(queueCount,productionProgress,rally,orders,repeat,lifecycle,upgrade,repeatKind,research,hasRally,pendingRally):null;SiteId=siteId;SlotId=slotId;ParentId=parentId;Phase=phase;BlockedReason=blockedReason;Heading=heading;Id=id;Owner=owner;Kind=kind;Position=position;Health=health;Progress=progress;QueueCount=PrivateState?.QueueCount??0;ProductionProgress=PrivateState?.ProductionProgress??0;Rally=PrivateState?.Rally??default(NavPoint);} public int Id{get;} public PlayableOwner Owner{get;} public PlayableBuildingKind Kind{get;} public NavPoint Position{get;} public int Health{get;} public double? ExactHealth{get;} public double Progress{get;} public bool RefineryUpgraded{get;} public PlayableBuildingPrivateState PrivateState{get;} public int QueueCount{get;} public double ProductionProgress{get;} public NavPoint Rally{get;} public int SiteId{get;} public int SlotId{get;} public int ParentId{get;} public ConstructionPhase Phase{get;} public string BlockedReason{get;} public double Heading{get;} }
    public sealed class PlayableProjectileSnapshot
    {
        public PlayableProjectileSnapshot(int id,int ownerId,int targetId,NavPoint position,PlayableEntityKind kind=PlayableEntityKind.Tank,double heading=0,double height=1,double pitch=0,PlayableOwner faction=PlayableOwner.Player,double age=0,bool visible=true,PlayableImpactMarker marker=null)
        {Id=id;OwnerId=ownerId;TargetId=targetId;Position=position;Kind=kind;Heading=heading;Height=height;Pitch=pitch;Faction=faction;Age=age;Visible=visible;Marker=marker;}
        public int Id{get;}public int OwnerId{get;}public int TargetId{get;}public NavPoint Position{get;}public PlayableEntityKind Kind{get;}public double Heading{get;}public double Height{get;}public double Pitch{get;}public PlayableOwner Faction{get;}public double Age{get;}public bool Visible{get;}public PlayableImpactMarker Marker{get;}
    }
    public sealed class PlayableImpactMarker
    {
        public PlayableImpactMarker(NavPoint position,double radius,double opacity){Position=position;Radius=radius;Opacity=opacity;}
        public NavPoint Position{get;}public double Radius{get;}public double Opacity{get;}
    }
    public sealed class PlayableImpactSnapshot
    {
        public PlayableImpactSnapshot(int id,PlayableOwner owner,BallisticPoint point,double radius,long tick,int visibleMask){Id=id;Owner=owner;Point=point;Radius=radius;Tick=tick;VisibleMask=visibleMask;}
        public int Id{get;}public PlayableOwner Owner{get;}public BallisticPoint Point{get;}public double Radius{get;}public long Tick{get;}public int VisibleMask{get;}
    }
    public sealed class PlayableRuntimeMetrics { public PlayableRuntimeMetrics(double tickCpuMilliseconds,double tickIntervalMilliseconds,double commandLatencyMilliseconds,int commandBacklog,int errors,int navigationPending=0,int missedDeadlines=0,double maximumTickCpu=0){NavigationPending=navigationPending;MissedDeadlines=missedDeadlines;MaximumTickCpu=maximumTickCpu;TickCpuMilliseconds=tickCpuMilliseconds;TickIntervalMilliseconds=tickIntervalMilliseconds;CommandLatencyMilliseconds=commandLatencyMilliseconds;CommandBacklog=commandBacklog;Errors=errors;} public int NavigationPending{get;} public int MissedDeadlines{get;} public double MaximumTickCpu{get;} public double TickCpuMilliseconds{get;} public double TickIntervalMilliseconds{get;} public double CommandLatencyMilliseconds{get;} public int CommandBacklog{get;} public int Errors{get;} }
    public sealed class PlayableSnapshot
    {
        public PlayableSnapshot(string profileId,int profileRevision,long generation,int seed,long sequence,long tick,RuntimeStatus status,bool paused,PlayableMatchOutcome outcome,int credits,NavGeometry geometry,PlayableEntitySnapshot[] entities,PlayableBuildingSnapshot[] buildings,PlayableProjectileSnapshot[] projectiles,PlayableRuntimeMetrics metrics,string failure,TerritorySiteSnapshot[] sites=null,double incomePerSecond=0,TeamVisionSnapshot vision=null,TerritorySite[] discoveredSites=null,PlayablePopulationSnapshot population=null,PlayableImpactSnapshot[] impacts=null,PlayableResearchAvailabilitySnapshot[] researchAvailability=null,PlayableResearchOrderSnapshot[] ownerResearch=null,PlayablePublicScoutObjective[] publicScoutObjectives=null,PlayableCenterDamageSnapshot[] ownCenterDamage=null,PlayableRouteProof[] routeProofs=null,PlayableArtillerySupportSnapshot[] artillerySupport=null,PlayableOwner owner=PlayableOwner.Player,double? exactCredits=null,string ownerId=null,int team=-1,OfflineParticipant[] participants=null,PlayableProfile activeProfile=null)
        {ActiveProfile=activeProfile;OwnerId=ownerId??(owner==PlayableOwner.Player?"player-1":"enemy-1");Team=team<0?(int)owner:team;Participants=Array.AsReadOnly((OfflineParticipant[])(participants??Array.Empty<OfflineParticipant>()).Clone());ExactCredits=exactCredits??credits;Owner=owner;ArtillerySupport=Array.AsReadOnly((PlayableArtillerySupportSnapshot[])(artillerySupport??Array.Empty<PlayableArtillerySupportSnapshot>()).Clone());RouteProofs=Array.AsReadOnly(Array.ConvertAll(routeProofs??Array.Empty<PlayableRouteProof>(),x=>x.Copy()));OwnCenterDamage=Array.AsReadOnly(Array.ConvertAll(ownCenterDamage??Array.Empty<PlayableCenterDamageSnapshot>(),x=>x.Copy()));PublicScoutObjectives=Array.AsReadOnly((PlayablePublicScoutObjective[])(publicScoutObjectives??new PlayablePublicScoutObjective[0]).Clone());ResearchAvailability=Array.AsReadOnly((PlayableResearchAvailabilitySnapshot[])(researchAvailability??new PlayableResearchAvailabilitySnapshot[0]).Clone());OwnerResearch=Array.AsReadOnly((PlayableResearchOrderSnapshot[])(ownerResearch??Array.Empty<PlayableResearchOrderSnapshot>()).Clone());Impacts=Array.AsReadOnly((PlayableImpactSnapshot[])(impacts??new PlayableImpactSnapshot[0]).Clone());Population=population;Vision=vision;DiscoveredSites=Array.AsReadOnly((TerritorySite[])(discoveredSites??new TerritorySite[0]).Clone());Sites=Array.AsReadOnly((TerritorySiteSnapshot[])(sites??new TerritorySiteSnapshot[0]).Clone());IncomePerSecond=incomePerSecond;ProfileId=profileId;ProfileRevision=profileRevision;Generation=generation;Seed=seed;Sequence=sequence;Tick=tick;Status=status;Paused=paused;Outcome=outcome;Credits=credits;Geometry=geometry;Entities=Array.AsReadOnly((PlayableEntitySnapshot[])entities.Clone());Buildings=Array.AsReadOnly((PlayableBuildingSnapshot[])buildings.Clone());Projectiles=Array.AsReadOnly((PlayableProjectileSnapshot[])projectiles.Clone());Metrics=metrics;Failure=failure;}
        public string OwnerId{get;} public int Team{get;} public IReadOnlyList<OfflineParticipant> Participants{get;}
        public bool IsHostile(PlayableOwner other)=>Participants.Count==0?other!=Owner:Participants[(int)other].Team!=Team;
        public PlayablePopulationSnapshot Population{get;} public TeamVisionSnapshot Vision{get;} public IReadOnlyList<TerritorySite> DiscoveredSites{get;} public IReadOnlyList<TerritorySiteSnapshot> Sites{get;} public IReadOnlyList<PlayableResearchAvailabilitySnapshot> ResearchAvailability{get;} public IReadOnlyList<PlayableResearchOrderSnapshot> OwnerResearch{get;} public IReadOnlyList<PlayablePublicScoutObjective> PublicScoutObjectives{get;} public double IncomePerSecond{get;}
        public IReadOnlyList<PlayableImpactSnapshot> Impacts{get;}
        public IReadOnlyList<PlayableCenterDamageSnapshot> OwnCenterDamage{get;}
        public IReadOnlyList<PlayableRouteProof> RouteProofs{get;}
        public IReadOnlyList<PlayableArtillerySupportSnapshot> ArtillerySupport{get;}
        public PlayableProfile ActiveProfile {get;}
        public PlayableOwner Owner{get;} public string ProfileId{get;} public int ProfileRevision{get;} public long Generation{get;} public int Seed{get;} public long Sequence{get;} public long Tick{get;} public RuntimeStatus Status{get;} public bool Paused{get;} public PlayableMatchOutcome Outcome{get;} public int Credits{get;} public double ExactCredits{get;} public NavGeometry Geometry{get;} public IReadOnlyList<PlayableEntitySnapshot> Entities{get;} public IReadOnlyList<PlayableBuildingSnapshot> Buildings{get;} public IReadOnlyList<PlayableProjectileSnapshot> Projectiles{get;} public PlayableRuntimeMetrics Metrics{get;} public string Failure{get;}
    }
}
