using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Runtime
{
    using Spacewars.Simulation;
    // Pure C# bounded U3 match state. Rendering and route calculation are external.
    internal sealed partial class PlayableDomain
    {
        private long nextAiSequence=1L<<61;
        internal long AllocateAiSequence()=>checked(nextAiSequence++);

        internal const string PlayerId="player-1";
        private sealed class Unit { public FlameArm FlameLeft,FlameRight; public bool FlameSync; public Unit CopyForAir()=>(Unit)MemberwiseClone(); public DroneRepairState DroneRepair; public DefaultCombatState DefaultCombat; public int LastCombatShotTarget; public double MaterializationUntil,MaterializationDuration,StasisUntil; public double ShkvalPitch=ShkvalLaunchGeometry.NeutralPitch; public double[] ShkvalCells; public bool WreckWaiting; public int WreckObservedRevision=-1,WreckFailureGeometry=-1; public WreckConnectorFailure WreckConnector; public AirState Air; public int ScenarioIndex=-1; public bool? AuthoredUpgrade; public PlayableOrderStamp LastOrder; public PlayableEntityKind Kind; public int BurstRemaining,BurstTarget,BurstSequence,BurstSize; public double BurstSpread,HawkBurstReload; public double BurstDelay; public bool ExplicitTarget; public BallisticPoint? IglaBeamEnd; public NavPoint PreviousPosition,Velocity; public int Id,Target; public double Health; public PlayableOwner Owner; public double Turret,Reload,StoppedSeconds,Repath; public NavPoint AttackMove; public bool HasAttackMove,PatrolStarted; public PlayableTacticalOrderSnapshot CurrentOrder; }
        private sealed class Building { public GaussShotState Gauss; public RepairTowerState Healing; public SentryTurretState Weapon; public int LastCombatShotTarget; public int LaunchPhase,LaunchedId; public double LaunchTime; public int TermsRevision; public RefineryUpgrade Upgrade,ProducerUpgrade; public double? LastDamageTime; public SaleState Sale; public RepairState Repair; public int Id,SiteId,SlotId,ParentId,PaidCost; public double Health,Heading=TerritoryRules.BuildingHeading,RetryAt; public ConstructionPhase Phase; public string BlockedReason; public readonly HashSet<int> Evacuated=new HashSet<int>(); public PlayableOwner Owner; public PlayableBuildingKind Kind; public NavPoint Position,Rally; public bool HasRally; public double Build; public bool Ready; public readonly List<ProductionOrder> Orders=new List<ProductionOrder>(); public bool RepeatTank; public PlayableEntityKind RepeatKind; public int Queue=>Orders.Count; public double ProductionProgress=>Orders.Count>0&&Orders[0].Active?1-Orders[0].Remaining/Orders[0].Duration:0; }
        private sealed class Projectile { public long? DirectLaunchTick; public int DirectBarrel; public HawkRocketFlight Hawk;public bool Spatial;public double DirectionY; public int TermsRevision; public Rocket Rocket; public int Id,Owner,Target; public NavPoint Position; public PlayableEntityKind Kind; public double Damage; public double Speed,Radius,Remaining,DirectionX,DirectionZ,Height,VerticalSlope; public PlayableOwner Faction; }
        private readonly NavGeometry projectileGeometry;
        internal PlayableProfile RoutingProfile=>profile;
        private PlayableProfile profile; private readonly NavigationSession navigation; private readonly bool scriptedEnemyPatrol; private readonly AuthoritySlotMap<int,Unit> units=new AuthoritySlotMap<int,Unit>(); private readonly AuthoritySlotMap<int,Building> buildings=new AuthoritySlotMap<int,Building>(); private readonly List<Projectile> projectiles=new List<Projectile>();
        private long nextOrderRevision=1; private int nextId=1,nextProjectile=1; private double credits,incomeClock,elapsed; private double enemyCredits; private readonly AuthoritySlotMap<string,PlayableCenterDamageSnapshot> centerDamage=new AuthoritySlotMap<string,PlayableCenterDamageSnapshot>(); internal long Tick {get;private set;} internal PlayableMatchOutcome Outcome{get;private set;}
        internal PlayableDomain(PlayableProfile profile,long generation):this(profile,generation,true){}
        internal PlayableDomain(PlayableProfile profile,long generation,bool scriptedEnemyPatrol):this(profile,generation,scriptedEnemyPatrol,false,null){}
        private PlayableDomain(PlayableProfile profile,long generation,bool scriptedEnemyPatrol,bool empty,NavGeometry savedGeometry):this(profile,generation,scriptedEnemyPatrol,empty,savedGeometry,null){}
        private PlayableDomain(PlayableProfile profile,long generation,bool scriptedEnemyPatrol,bool empty,NavGeometry savedGeometry,OfflineMatchConfiguration configuration,NavigationRetentionBudget navigationBudget=null,NavigationAuthorityOwners decodedAuthority=null)
        { this.profile=profile;termProfiles[profile.Revision]=profile; offline=configuration;if(offline!=null&&!WorldWire.Binding(profile).SequenceEqual(WorldWire.Binding(offline.Profile)))throw new ArgumentException("Offline profile mismatch.");foreach(var owner in Owners)ownerCredits.Add(owner,profile.StartingCredits); this.scriptedEnemyPatrol=scriptedEnemyPatrol; projectileGeometry=new NavGeometry(profile.ArenaHalfExtent,SolidObstacles().ToArray(),1); Geometry=savedGeometry??new NavGeometry(profile.ArenaHalfExtent,StaticObstacles(),1); navigation=new NavigationSession(generation,Geometry,profile.Navigation,null,profile.AuthoredMap,navigationBudget,decodedAuthority,replayOnlyProjection:true);navigation.UsesRecastGroundRoutes=profile.UsesRecastGroundRoutes;Geometry=navigation.AuthorityGeometry;navigation.OwnerMovementGeometry=OwnerMovementGeometry;navigation.Crowd.ProductionEscape=GroundProductionEscape;navigation.Crowd.AllowsMovingTurn=AllowsExplorerMovingTurn;navigation.Crowd.AllowsExplorerYield=IsExplorer;navigation.Crowd.LeadsFormation=id=>units.TryGetValue(id,out var u)&&u.Kind==PlayableEntityKind.Cerberus;navigation.ConfigureMarch(profile.GroupMarchMaximumStretch,profile.Revision);navigation.PreserveInstalledExecution=RetainsInstalledExecution;navigation.RouteActivated+=ActivateSpatialExecution;navigation.EndpointAssigned+=ObserveEndpointAssignment;navigation.RouteRequested+=ObserveSpatialRequest;navigation.RouteFailed+=RecordTacticalRouteFailure;navigation.MovementCancelled+=CancelSpatialExecution; credits=profile.StartingCredits;enemyCredits=profile.StartingCredits;InitializeSites(); Outcome=PlayableMatchOutcome.Playing;
          if(empty)return;
          if(offline!=null){MaterializeOffline();return;}
          AddBuilding(PlayableOwner.Player,PlayableBuildingKind.Headquarters,profile.Headquarters(PlayableOwner.Player),true); AddBuilding(PlayableOwner.Enemy,PlayableBuildingKind.Headquarters,profile.Headquarters(PlayableOwner.Enemy),true); RebuildGeometry();
          SpawnUnit(new NavPoint(profile.Headquarters(PlayableOwner.Player).X+profile.DefenderOffsetX,profile.Headquarters(PlayableOwner.Player).Z-profile.DefenderOffsetZ),PlayableOwner.Player,PlayableEntityKind.Explorer);
          SpawnUnit(new NavPoint(profile.Headquarters(PlayableOwner.Enemy).X-profile.DefenderOffsetX,profile.Headquarters(PlayableOwner.Enemy).Z),PlayableOwner.Enemy,PlayableEntityKind.Explorer); }
        private bool IsExplorer(int id)
            => units.TryGetValue(id,out var unit)&&unit.Kind==PlayableEntityKind.Explorer;

        private bool AllowsExplorerMovingTurn(int id)
        {
            return units.TryGetValue(id,out var unit)&&unit.Kind==PlayableEntityKind.Explorer&&
                unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&navigation.IsSoloGroupMember(id);
        }
        internal NavGeometry Geometry {get;private set;} internal NavigationSession Navigation {get{return navigation;}}
        internal void DestroyOwnedNavigation()
        {
            if(navigation.HasCertifiedHost)throw new InvalidOperationException("Domain destruction requires route-worker ACK.");
            StopCombatMotionAuthority();ReleaseAirMotionSources();rallyWork.Clear();Geometry=null;navigation.DestroyNavigationAuthority();
        }
        internal PlayableTacticalObservationState CaptureTacticalObservationState()=>new PlayableTacticalObservationState(navigation.Generation,Tick,
            units.Values.Where(u=>u.CurrentOrder!=null).OrderBy(u=>u.Id).Select(u=>u.CurrentOrder).ToArray(),
            centerDamage.Values.OrderBy(d=>d.CenterId).ThenBy(d=>d.AttackerId).ToArray());
        internal void RestoreTacticalObservationState(PlayableTacticalObservationState state)
        {
            if(state==null||state.Generation!=navigation.Generation||state.Tick!=Tick)throw new ArgumentException("Tactical checkpoint generation/tick mismatch.",nameof(state));
            if(state.Orders.Select(o=>o.UnitId).Distinct().Count()!=state.Orders.Count||state.Orders.Any(o=>o.Generation!=navigation.Generation||o.IssuedTick>Tick||o.CommandSequence<1||!units.TryGetValue(o.UnitId,out var u)||u.Owner!=o.Owner))throw new ArgumentException("Invalid tactical order checkpoint.",nameof(state));
            if(state.Damage.Select(d=>d.CenterId+":"+d.AttackerId).Distinct().Count()!=state.Damage.Count||state.Damage.Any(d=>d.Generation!=navigation.Generation||d.Tick>Tick||d.Damage<=0||!buildings.TryGetValue(d.CenterId,out var b)||b.Owner!=d.Owner||(b.Kind!=PlayableBuildingKind.Headquarters&&b.Kind!=PlayableBuildingKind.Outpost)||!units.TryGetValue(d.AttackerId,out var attacker)||attacker.Owner==d.Owner))throw new ArgumentException("Invalid center damage checkpoint.",nameof(state));
            foreach(var unit in units.Values)unit.CurrentOrder=null;
            foreach(var order in state.Orders)units[order.UnitId].CurrentOrder=order.Copy();
            centerDamage.Clear();foreach(var damage in state.Damage)centerDamage.Add(damage.CenterId+":"+damage.AttackerId,damage.Copy());
        }
        internal PlayableCommandStatus Apply(PlayableCommand command,out string message)=>ApplyWithStatistics(command,out message,true);
        internal PlayableCommandStatus ApplyLocalCombatMove(PlayableCommand command,out string message)=>
            ApplyWithStatistics(command,out message,true,exactMove:true);
        // Runtime records admitted human intents before pause/route barriers; direct authority callers retain their ingress count.
        internal PlayableCommandStatus ApplyWithoutHumanStatistics(PlayableCommand command,out string message)=>ApplyWithStatistics(command,out message,false);
        private PlayableCommandStatus ApplyWithStatistics(PlayableCommand command,out string message,bool recordHuman,bool exactMove=false)
        {
            navigation.AssertMotionOperational();AssertAirOperational();AssertCombatExecutionOperational();
            message="Invalid command provenance.";
            if(command==null||!Enum.IsDefined(typeof(PlayableOrderMode),command.Mode)||!Enum.IsDefined(typeof(PlayableOrderOrigin),command.Origin)||command.Sequence<=0)return PlayableCommandStatus.InvalidSequence;
            if(command.Origin==PlayableOrderOrigin.Ai&&(string.IsNullOrWhiteSpace(command.Source)||command.ActionId<=0||command.JobId<=0))return PlayableCommandStatus.Rejected;
            if(command.Origin!=PlayableOrderOrigin.Ai&&(command.Source!=null||command.ActionId!=0||command.JobId!=0))return PlayableCommandStatus.Rejected;
            if(exactMove&&(command.Kind is not (PlayableCommandKind.Move or PlayableCommandKind.AttackMove)||
                command.Origin!=PlayableOrderOrigin.Ai||command.Mode!=PlayableOrderMode.Replace||command.EntityIds.Count!=1))return PlayableCommandStatus.Rejected;
            if(command.Generation!=navigation.Generation)return PlayableCommandStatus.StaleGeneration;
            if(!Authorizes(command.PlayerId))return PlayableCommandStatus.InvalidOwner;
            if(Outcome!=PlayableMatchOutcome.Playing&&command.Kind!=PlayableCommandKind.Restart){message="Match is finished.";return PlayableCommandStatus.Rejected;}
            if(offline!=null){var owner=OwnerFor(command.PlayerId);if(offlineSequences.TryGetValue(owner,out var previous)&&command.Sequence<=previous)return PlayableCommandStatus.InvalidSequence;offlineSequences[owner]=command.Sequence;}
            if(command.Sequence>=nextAiSequence)nextAiSequence=checked(command.Sequence+1);
            if(offline?.CampaignOpening==true&&command.Kind!=PlayableCommandKind.Move&&command.Kind!=PlayableCommandKind.AttackMove&&command.Kind!=PlayableCommandKind.Attack&&command.Kind!=PlayableCommandKind.Stop&&command.Kind!=PlayableCommandKind.Hold&&command.Kind!=PlayableCommandKind.Follow){message="Construction and production are unavailable in this mission.";return PlayableCommandStatus.Rejected;}
            if(command.Kind!=PlayableCommandKind.UseOrbital&&command.EntityIds.Any(id=>buildings.TryGetValue(id,out var disabled)&&disabled.Owner==OwnerFor(command.PlayerId)&&SystemsDisabled(id))){message="Сбой систем: действия здания недоступны";return PlayableCommandStatus.Rejected;}
            if(command.EntityIds.Count>0&&command.EntityIds.All(id=>buildings.TryGetValue(id,out var turret)&&TerritoryRules.ArmedDefense(turret.Kind))&&(command.Kind==PlayableCommandKind.Attack||command.Kind==PlayableCommandKind.Move||command.Kind==PlayableCommandKind.AttackMove||command.Kind==PlayableCommandKind.Stop||command.Kind==PlayableCommandKind.Hold))
            {if(recordHuman&&command.Origin==PlayableOrderOrigin.Human)RecordHumanAction(command.PlayerId);return TurretOrder(command,OwnerFor(command.PlayerId),out message);}
            bool tactical=command.Kind==PlayableCommandKind.Move||command.Kind==PlayableCommandKind.AttackMove||command.Kind==PlayableCommandKind.Attack||command.Kind==PlayableCommandKind.Stop||command.Kind==PlayableCommandKind.Hold||command.Kind==PlayableCommandKind.Follow;
            if(tactical&&command.Origin==PlayableOrderOrigin.Ai&&command.Source!=NuclearEvasionSource&&
                command.EntityIds.Any(id=>nuclearEvacuations.ContainsKey(id)))
            {message="Evacuating nuclear strike area.";return PlayableCommandStatus.Rejected;}
            // Navigation retains HOLD while an accepted replacement awaits its route.
            // Priority follows durable accepted origin, independently of transient route/kind.
            if(tactical&&command.EntityIds.Any(id=>units.TryGetValue(id,out var air)&&air.Air!=null)){if(recordHuman&&command.Origin==PlayableOrderOrigin.Human)RecordHumanAction(command.PlayerId);return ApplyAirCommand(command,out message,false);}
            if(tactical&&command.Origin==PlayableOrderOrigin.Ai&&command.EntityIds.Any(id=>units.TryGetValue(id,out var u)&&TryActor(id,out var n)&&n.Held&&(u.LastOrder==null||u.LastOrder.Origin!=PlayableOrderOrigin.Ai||(u.LastOrder.Source!=command.Source&&command.Source!=NuclearEvasionSource)||u.LastOrder.Owner!=u.Owner||u.LastOrder.Generation!=navigation.Generation)))
            {message="Human/unknown HOLD has priority.";return PlayableCommandStatus.Rejected;}
            PlayableCommandStatus? spatialRejection=null;
            if(command.Kind==PlayableCommandKind.Move||command.Kind==PlayableCommandKind.AttackMove){
                spatialRejection=MoveRecipientFailure(command.CopyEntityIds(),OwnerFor(command.PlayerId));
                if(spatialRejection.HasValue)message="Invalid movement recipients.";
                if(!spatialRejection.HasValue&&IsSingleHumanMove(command))
                    command=ResolveSingleHumanMove(command);
                else if(!spatialRejection.HasValue){
                    NavLocation original;bool valid;
                    if(command.TargetLocation.HasValue){original=command.TargetLocation.Value;valid=original.Position.Equals(command.Target)&&navigation.Crowd.ValidLocation(original,0);}
                    else valid=navigation.Crowd.Locate(command.Target,0,out original);
                    if(!valid){spatialRejection=PlayableCommandStatus.InvalidTarget;message="Invalid destination.";}else command=command.WithResolvedLocation(original);
                }
            }
            if(command.Mode==PlayableOrderMode.Append){if(recordHuman&&command.Origin==PlayableOrderOrigin.Human)RecordHumanAction(command.PlayerId);return spatialRejection??AppendTactical(command,out message);}
            if(tactical&&recordHuman&&command.Origin==PlayableOrderOrigin.Human)RecordHumanAction(command.PlayerId);
            bool committed=false;PlayableCommandStatus status;
            if(tactical)status=spatialRejection??ApplyTacticalPrepared(command,out message,out committed,exactMove);
            else status=ApplyCore(command,out message);
            try{if(!tactical&&recordHuman&&command.Origin==PlayableOrderOrigin.Human&&Enum.IsDefined(typeof(PlayableCommandKind),command.Kind)&&command.Kind!=PlayableCommandKind.Restart)RecordHumanAction(command.PlayerId);}
            catch(Exception error){if(committed)throw new TacticalCommitFailure(TacticalCommitPhase.Committed,error);throw;}
            return status;
        }
        private PlayableCommandStatus ApplyCore(PlayableCommand command,out string message,PreparedTacticalCommit preparation=null,bool exactMove=false,bool preserveFormation=false)
        { message=null; if(command.Generation!=navigation.Generation)return PlayableCommandStatus.StaleGeneration; if(!Authorizes(command.PlayerId))return PlayableCommandStatus.InvalidOwner; var issuer=OwnerFor(command.PlayerId); if(Outcome!=PlayableMatchOutcome.Playing && command.Kind!=PlayableCommandKind.Restart){message="Match is finished.";return PlayableCommandStatus.Rejected;}
          switch(command.Kind){
            case PlayableCommandKind.SignalAttack:case PlayableCommandKind.SignalDefend:case PlayableCommandKind.SignalRecon:return SendTeamSignal(command,issuer);
            case PlayableCommandKind.TransferRelay:return TransferRelay(issuer,command.SiteId,command.TargetId,out message);
            case PlayableCommandKind.BeginOrbitalReinforcement:return BeginReinforcement(issuer,out message);
            case PlayableCommandKind.CancelOrbitalReinforcement:return CancelReinforcement(issuer);
            case PlayableCommandKind.ChooseOrbital:return ChooseOrbital(issuer,command.TargetId);
            case PlayableCommandKind.UseOrbital:
                if(command.TargetId==OrbitalAbilities.Reinforcement)return ConfirmReinforcement(issuer,command,out message);
                CancelReinforcement(issuer);
                if(command.TargetId==OrbitalAbilities.NuclearStrike)
                    return UseNuclearStrike(issuer,command);
                if(command.TargetId==OrbitalAbilities.RepairNanobots)
                    return UseRepairNanobots(issuer,command);
                int orbitalTarget=command.EntityIds.Count==1?command.EntityIds[0]:0;
                if(command.TargetId==OrbitalAbilities.Stasis)
                    return UseStasis(issuer,command.Target,orbitalTarget,command.TargetLocation);
                if(command.TargetId==OrbitalAbilities.AcceleratedProduction)return StartBoost(issuer,orbitalTarget);
                return command.TargetId==OrbitalAbilities.ControlHack?HackControl(issuer,orbitalTarget,command):UseOrbital(issuer,command.TargetId,command.Target);
            case PlayableCommandKind.UpgradeProducer:return QueueProducerUpgrade(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,out message,
                command.Origin==PlayableOrderOrigin.Ai&&command.Source==PlayableAiOpeningComposition.SourceIdentity);
            case PlayableCommandKind.UpgradeRefinery:return StartRefineryUpgrade(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,out message);
            case PlayableCommandKind.CancelRefineryUpgrade:return CancelRefineryUpgrade(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,out message);
            case PlayableCommandKind.QueueResearch:return QueueResearch(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,command.ResearchKind,out message);
            case PlayableCommandKind.CancelResearch:return CancelResearch(command.EntityIds.Count>0?command.EntityIds[0]:0,command.ProductionOrderId,issuer,out message);
            case PlayableCommandKind.SellBuilding:
                var sale=SellBuilding(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,out message);
                if(sale==PlayableCommandStatus.Applied)InterruptUnreadyResearchCenters();
                return sale;
            case PlayableCommandKind.RepairTarget:return OrderDroneRepair(command,issuer,out message);
            case PlayableCommandKind.StartBuildingRepair:return StartRepair(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,out message);
            case PlayableCommandKind.CancelBuildingRepair:return CancelRepair(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,out message);
            case PlayableCommandKind.BuildAt:return BuildAt(command.SiteId,command.SlotId,command.BuildingKind,command.ParentId,issuer,out message);
            case PlayableCommandKind.CancelBuilding:return CancelBuilding(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,out message);
            case PlayableCommandKind.BuildFactory:return Build(PlayableBuildingKind.Factory,command.Pad,issuer,out message);
            case PlayableCommandKind.BuildRefinery:return Build(PlayableBuildingKind.Refinery,command.Pad,issuer,out message);
            case PlayableCommandKind.QueueSkat:return QueueUnit(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,PlayableEntityKind.Skat,out message);
            case PlayableCommandKind.QueueHawk:return QueueUnit(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,PlayableEntityKind.Hawk,out message);
            case PlayableCommandKind.QueueIgla:return QueueUnit(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,PlayableEntityKind.Igla,out message);
            case PlayableCommandKind.QueueCerberus:return QueueUnit(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,PlayableEntityKind.Cerberus,out message);
            case PlayableCommandKind.QueueShkval:return QueueUnit(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,PlayableEntityKind.Shkval,out message);
            case PlayableCommandKind.QueueExplorer:return QueueUnit(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,PlayableEntityKind.Explorer,out message);
            case PlayableCommandKind.QueueTank:return QueueTank(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,out message);
            case PlayableCommandKind.CancelProductionOrder:return CancelProduction(command.EntityIds.Count>0?command.EntityIds[0]:0,command.ProductionOrderId,issuer,out message);
            case PlayableCommandKind.ToggleRepeatProduction:return ToggleRepeat(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,out message,command.UnitKind);
            case PlayableCommandKind.SetRally when command.Origin == PlayableOrderOrigin.Ai && command.SlotId == ClearAiRallySlot:
                return ClearAiRally(command.EntityIds.Count > 0 ? command.EntityIds[0] : 0, issuer, out message);
            case PlayableCommandKind.SetRally:return Rally(command.EntityIds.Count>0?command.EntityIds[0]:0,command.Target,issuer,command.Sequence,out message);
            case PlayableCommandKind.Move: case PlayableCommandKind.AttackMove:
                return Move(command.CopyEntityIds(),command.Target,command.Kind==PlayableCommandKind.AttackMove,command.Sequence,issuer,out message,preparation,
                    exactMove||!preserveFormation&&IsSingleHumanMove(command),relay:RelayArrivalArea(command),preserveFormation:preserveFormation);
            case PlayableCommandKind.Attack:return Attack(command.CopyEntityIds(),command.TargetId,command.Sequence,issuer,out message,preparation);
            case PlayableCommandKind.Stop:return Stop(command.CopyEntityIds(),issuer,false,out message,preparation);
            case PlayableCommandKind.Hold:return Stop(command.CopyEntityIds(),issuer,true,out message,preparation);
            default:message="Unsupported command.";return PlayableCommandStatus.Rejected;
          }
        }
        // Compatibility for U3 command fixtures: immediately resolves into explicit home slots.
        private PlayableCommandStatus Build(PlayableBuildingKind kind,int pad,PlayableOwner issuer,out string message)
        {
            if(pad!=0){message="Invalid pad.";return PlayableCommandStatus.InvalidTarget;}
            int siteId=HomeSite(issuer);
            return BuildAt(siteId,kind==PlayableBuildingKind.Factory?1:2,kind,sites[siteId].CenterId,issuer,out message);
        }
        private PlayableCommandStatus? MoveRecipientFailure(int[] ids,PlayableOwner issuer)
        {
            if(ids.Length==0)return PlayableCommandStatus.InvalidTarget;var unique=new HashSet<int>();
            foreach(int id in ids)if(!unique.Add(id)||!units.TryGetValue(id,out var unit)||unit.Owner!=issuer)return PlayableCommandStatus.InvalidEntity;
            return null;
        }
        private static bool IsSingleHumanMove(PlayableCommand command)=>command.Kind==PlayableCommandKind.Move&&
            command.Origin==PlayableOrderOrigin.Human&&command.EntityIds.Count==1;
        private PlayableCommand ResolveSingleHumanMove(PlayableCommand command)
        {
            var destination=GroundReplay?.NextSlot(command.PlayerId,command.EntityIds[0]) ??
                navigation.ResolveSingleMoveEndpoint(profile,command.EntityIds[0],command.Target,command.TargetLocation);
            return command.WithMoveDestination(destination);
        }

        private PlayableCommandStatus Move(int[] ids,NavPoint target,bool attackMove,long commandSequence,PlayableOwner issuer,out string message,PreparedTacticalCommit preparation=null,bool exactMove=false,bool retainRallyIntent=false,(NavPoint Center,double Radius)? relay=null,bool preserveFormation=false)
        {
            message="Invalid destination.";
            var recipientFailure=MoveRecipientFailure(ids,issuer);if(recipientFailure.HasValue){message="Invalid movement recipients.";return recipientFailure.Value;}
            bool formation = (ids.Length > 1 || preserveFormation || relay.HasValue) && profile.UsesRecastGroundRoutes && !exactMove;
            // Attack-move retains its requested objective even inside a building;
            // the shared arrival allocator chooses collision-free approach slots.
            if (!formation && (!attackMove || exactMove) && ids.Any(id => !Geometry.IsFree(target, PlayableUnitRules.Radius(profile, units[id].Kind))))
                return PlayableCommandStatus.InvalidTarget;
            Array.Sort(ids);
            var slots = GroundReplay?.MoveSlots(FinancialOwnerId(issuer),ids) ?? (exactMove ? new[] { target } : formation ?
                navigation.AllocateFormationSlots(target, ids, id => units[id].Kind, relay) :
                navigation.AllocateArrivalSlots(target, ids));
            if(slots.Length!=ids.Length){message="No free arrival slots for movement recipients.";return PlayableCommandStatus.InvalidTarget;}
            // Produced actors are already committed. Keep their assigned rally
            // endpoint across admission refusal so ordinary attack-move retries
            // the free slot, rather than the potentially occupied flag itself.
            if(retainRallyIntent)for(int i=0;i<ids.Length;i++){
                var u=units[ids[i]];u.HasAttackMove=true;u.AttackMove=slots[i];
                u.CurrentOrder=new PlayableTacticalOrderSnapshot(u.Id,u.Owner,navigation.Generation,commandSequence,Tick,PlayableTacticalOrderKind.AttackMove,target,0);
            }
            if(!(preparation==null?navigation.MoveGroup(ids,slots):navigation.StageTacticalMove(ids,slots,preparation.NavigationInputs,fixedEndpoint:exactMove))){message="Navigation admission refused: "+(navigation.LastCommandAdmissionRefusal??"movement staging unavailable")+".";return PlayableCommandStatus.Overflow;}
            if(preparation!=null&&!preparation.BeginProposal()){message="Movement proposal inputs changed before commit.";return PlayableCommandStatus.Rejected;}
            for(int i=0;i<ids.Length;i++){var u=units[ids[i]];u.WreckWaiting=false;InterruptBurst(u);u.ExplicitTarget=false;u.Target=0;u.HasAttackMove=attackMove;u.AttackMove=slots[i];u.StoppedSeconds=0;u.CurrentOrder=new PlayableTacticalOrderSnapshot(u.Id,u.Owner,navigation.Generation,commandSequence,Tick,attackMove?PlayableTacticalOrderKind.AttackMove:PlayableTacticalOrderKind.Move,target,0);}
            message="Order accepted.";return PlayableCommandStatus.Applied;
        }
        private PlayableCommandStatus Attack(int[] ids,int target,long commandSequence,PlayableOwner issuer,out string message,PreparedTacticalCommit preparation=null)
        {
            RefreshVision();message="No visible enemy target.";
            if(!VisibleTarget(issuer,target))return PlayableCommandStatus.InvalidTarget;
            if(ids.Length==0||ids.Distinct().Count()!=ids.Length||ids.Any(id=>!units.TryGetValue(id,out var unit)||unit.Owner!=issuer))return PlayableCommandStatus.InvalidEntity;
            if(!(preparation==null?navigation.TryStopGroup(ids,false):navigation.StageTacticalStop(ids,false,preparation.NavigationInputs))){message="Attack order could not be prepared.";return PlayableCommandStatus.Overflow;}
            if(preparation!=null&&!preparation.BeginProposal())return PlayableCommandStatus.Rejected;
            foreach(int id in ids){var u=units[id];u.WreckWaiting=false;InterruptBurst(u);u.ExplicitTarget=true;u.Target=target;u.HasAttackMove=false;u.Repath=0;u.CurrentOrder=new PlayableTacticalOrderSnapshot(u.Id,u.Owner,navigation.Generation,commandSequence,Tick,PlayableTacticalOrderKind.Attack,default(NavPoint),target);}
            message="Attack ordered.";return PlayableCommandStatus.Applied;
        }
        private PlayableCommandStatus Stop(int[] ids,PlayableOwner issuer,bool hold,out string message,PreparedTacticalCommit preparation=null){if(ids.Length==0||ids.Distinct().Count()!=ids.Length||ids.Any(id=>!units.TryGetValue(id,out var unit)||unit.Owner!=issuer)){message="No selectable tanks.";return PlayableCommandStatus.InvalidEntity;}if(!(preparation==null?navigation.TryStopGroup(ids,hold):navigation.StageTacticalStop(ids,hold,preparation.NavigationInputs))){message="Navigation Stop admission refused.";return PlayableCommandStatus.Overflow;}if(preparation!=null&&!preparation.BeginProposal()){message="Stop domain source changed.";return PlayableCommandStatus.Rejected;}foreach(int id in ids){var u=units[id];InterruptBurst(u);u.ExplicitTarget=false;u.Target=0;u.WreckWaiting=false;u.HasAttackMove=false;u.CurrentOrder=null;}message=hold?"HOLD.":"Stopped.";return PlayableCommandStatus.Applied;}
        private readonly bool navigationPerformanceTrace=Array.IndexOf(Environment.GetCommandLineArgs(),"-routePerformanceEvidence")>=0;
        internal Func<bool> NavigationResultPreparationCancelled;
        internal bool NonblockingNavigationPreparation;
        private bool recoverMotionPreparation;
        private void StepNavigationWithStableAnswers(double dt)
        {
            GroundReplay?.Deliver();
            var port=navigation.RoutePort;
            if(!NonblockingNavigationPreparation){
                using(port.AcquireResultPreparation(NavigationResultPreparationCancelled)){
                    navigation.Step(dt);
                    GroundReplay?.CheckDelivered();
                    combatMotionAuthority?.RetireDeniedRepairBranches();
                }
                return;
            }
            bool recover=false;
            try{
                using var boundary=port.TryAcquireResultPreparation(NavigationResultPreparationCancelled,recoverMotionPreparation);
                navigation.Step(dt);
                GroundReplay?.CheckDelivered();
                combatMotionAuthority?.RetireDeniedRepairBranches();
                // A refused optimistic frame can ask for one stable turn. Do
                // not extend an acknowledged turn after another refusal.
                recover=boundary==null&&navigation.HasCertifiedHost&&navigation.MotionSourceContended;
            }finally{
                recoverMotionPreparation=recover;
                port.FinishResultPreparationTurn(recover);
            }
        }
        private void StepNavigationMeasured(double dt)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            long qaBegin=NavigationStepsForQa!=null?System.Diagnostics.Stopwatch.GetTimestamp():0;
#endif
            if(!navigationPerformanceTrace){StepNavigationWithStableAnswers(dt);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if(qaBegin!=0)RecordNavigationStepForQa(qaBegin);
#endif
                return;}
            long begin=System.Diagnostics.Stopwatch.GetTimestamp();
            double repair=navigation.Crowd.RepairCpuMs;int count=navigation.Crowd.RepairCount,failed=navigation.Crowd.RepairFailed,gc=GC.CollectionCount(0);
            StepNavigationWithStableAnswers(dt);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(qaBegin!=0)RecordNavigationStepForQa(qaBegin);
#endif
            double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-begin)*1000d/System.Diagnostics.Stopwatch.Frequency;
            int repairs=navigation.Crowd.RepairCount-count,failures=navigation.Crowd.RepairFailed-failed;
            if(ms>=15||repairs!=0||failures!=0)Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "NAV_STAGE tick={0} ms={1:F4} repair_ms={2:F4} repairs={3} failed={4} gc0={5}",
                Tick,ms,navigation.Crowd.RepairCpuMs-repair,repairs,failures,GC.CollectionCount(0)-gc));
        }
        internal void Step(double dt)
        {
            navigation.AssertMotionOperational();AssertAirOperational();AssertCombatExecutionOperational();
            using var measured=PerformanceProfile.Measure(ProfileStage.Domain);
            if(Outcome!=PlayableMatchOutcome.Playing)return;
            long phase= navigationPerformanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
            double movementMs=0,visionMs=0,contactsMs=0,combatMs=0,economyMs=0,firingMs=0,projectilesMs=0,queuesMs=0;
            double Phase(){if(!navigationPerformanceTrace)return 0;long now=System.Diagnostics.Stopwatch.GetTimestamp();double ms=(now-phase)*1000d/System.Diagnostics.Stopwatch.Frequency;phase=now;return ms;}
            using(PerformanceProfile.Measure(ProfileStage.DomainPrelude)){ObserveSoundMotion();
            Tick++;elapsed+=dt;AdvanceLingeringVision(dt);AdvanceOrbital(dt);AdvanceWrecks();ReconcileWreckRoutes();ServiceWreckRoutes();buildingDestructions.RemoveAll(d=>Tick>=d.Snapshot.EndTick);incomeEvents.RemoveAll(e=>(Tick-e.Tick)/30d>=profile.IncomeMarkerDurationSeconds);impacts.RemoveAll(p=>(Tick-p.Tick)/30d>profile.ImpactEffectSec);
            // Pinned web ordering: movement/projectiles precede capture, foundations, construction and settlement.
            foreach(var u in units.Values)if(TryActor(u.Id,out var before)){ObserveWreckConnectorInputs(u,before);u.PreviousPosition=before.Position;if(Frozen(u)){if(u.DefaultCombat?.Deadline>0)u.DefaultCombat.Deadline+=dt;}else{RetireLostTargetOrder(u);EnforceCombatResponseRadius(u,before,dt);}}
            AdvanceCampaignGuards(dt);AdvanceFollow();
            if(combatExecutionOwners!=null&&RoutingProfile.UsesRecastGroundRoutes)
                foreach(var owner in combatExecutionOwners())
                    if(Authorizes(owner.OwnerId))owner.RetireUnavailableGroundRepairs(this);
            }using(PerformanceProfile.Measure(ProfileStage.Movement))StepNavigation(dt);using(PerformanceProfile.Measure(ProfileStage.DomainMotionExtras)){AdvanceHawks(dt);RefreshSpatialCompletions();foreach(var u in units.Values)if(TryActor(u.Id,out var after)){u.Velocity=new NavPoint((after.Position.X-u.PreviousPosition.X)/dt,(after.Position.Z-u.PreviousPosition.Z)/dt);if(Frozen(u))continue;RetireExhaustedTacticalMove(u,after);if(after.Held&&!navigation.IsPending(u.Id)){u.CurrentOrder=null;u.HasAttackMove=false;}if(!u.DroneRepair.Detour&&!WaitingForWreck(u.Id)&&!HasQueueExecution(u.Id)&&u.CurrentOrder!=null&&u.CurrentOrder.Kind!=PlayableTacticalOrderKind.Repair&&u.CurrentOrder.Kind!=PlayableTacticalOrderKind.Attack&&u.CurrentOrder.Kind!=PlayableTacticalOrderKind.Follow&&!navigation.IsPending(u.Id)&&!after.Moving&&(after.Outcome==NavigationOutcome.Arrived||after.Outcome==NavigationOutcome.Unreachable||after.Outcome==NavigationOutcome.Blocked||after.Outcome==NavigationOutcome.Rejected))u.CurrentOrder=null;}AdvanceFollow(false);ObserveGroundTrails();}movementMs=Phase();RefreshVision();AdvanceNuclearStrikes();visionMs=Phase();RefreshFormationContacts();AdvanceMines();contactsMs=Phase();using(PerformanceProfile.Measure(ProfileStage.Firing)){AdvanceIndexedCombat(dt);AdvanceTurrets(dt);repairedUnits.Clear();AdvanceRepairTowers(dt);AdvanceRepairDrones(dt);AdvanceRepairNanobots(dt);}firingMs=Phase();using(PerformanceProfile.Measure(ProfileStage.Projectiles))AdvanceProjectiles(dt);AdvanceMines();projectilesMs=Phase();using(PerformanceProfile.Measure(ProfileStage.Queues)){
                foreach(var unit in units.Values)if(!Frozen(unit))RetireLostTargetOrder(unit);
                RefreshTacticalQueues();
            }queuesMs=Phase();combatMs=firingMs+projectilesMs+queuesMs;
            using(PerformanceProfile.Measure(ProfileStage.Economy)){AdvanceCapture(dt);AdvanceFoundations();AdvanceBuildings(dt);AdvanceProduction(dt);AdvanceBuildingLifecycle(dt);AdvanceRefineryUpgrades(dt);AdvanceResearch(dt);
            AdvanceBoostWear(dt);
            incomeClock+=dt;
            double settlement=profile.IncomeSettlementSeconds;
            while(incomeClock>=settlement-1e-9){incomeClock-=settlement;foreach(var owner in Owners)if(!eliminated.Contains(owner))SettleIncome(owner);}
            RemoveSettledBoosts();
            // Presentation remains tied to persisted match ticks, independently of settlement cadence.
            if(Tick%(30L*profile.IncomePeriodSeconds)==0)foreach(var owner in Owners)if(!eliminated.Contains(owner))EmitIncomeMarkers(owner);
            CheckOutcome();if(DominationEnabled&&Outcome==PlayableMatchOutcome.Playing)AdvanceDomination(dt);AdvanceRally();}economyMs=Phase();RefreshVision();ObserveSoundMotion();if(Outcome!=PlayableMatchOutcome.Playing)FreezeResult();
            double finalMs=Phase();
            if(navigationPerformanceTrace&&movementMs+visionMs+contactsMs+combatMs+economyMs+finalMs>=8)
                Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,"DOMAIN_STAGE tick={0} movement_ms={1:F4} vision_ms={2:F4} combat_ms={3:F4} economy_ms={4:F4} final_ms={5:F4} contacts_ms={6:F4} firing_ms={7:F4} projectiles_ms={8:F4} queues_ms={9:F4} fire_hits={10} fire_misses={11}",Tick,movementMs,visionMs,combatMs,economyMs,finalMs,contactsMs,firingMs,projectilesMs,queuesMs,GroundFireCache.Hits,GroundFireCache.Misses));
        }
        private readonly System.Collections.Generic.Dictionary<PlayableOwner,double> settledIncome=new System.Collections.Generic.Dictionary<PlayableOwner,double>();
        private Func<string,double> aiIncomeMultiplier;
        internal void ConfigureAiIncome(Func<string,double> multiplier)=>aiIncomeMultiplier=multiplier;
        private double IncomeMultiplier(PlayableOwner owner)=>aiIncomeMultiplier?.Invoke(FinancialOwnerId(owner))??1;
        private double SettledIncome(PlayableOwner owner)=>settledIncome.TryGetValue(owner,out var total)?total:0;
        private void SettleIncome(PlayableOwner owner)
        {
            if(FreeProvingGround)return;
            double seconds=profile.IncomeSettlementSeconds;
            var amount=BaseIncome(owner)/profile.IncomePeriodSeconds*seconds;
            foreach(var building in buildings.Values)
            {
                if(building.Owner!=owner||!IncomeSourceActive(building)||!OrbitalProductionBoost.Income(building.Kind))continue;
                double extraSeconds=ExtraBoostWork(building.Id,elapsed-incomeClock,seconds);
                if(building.Kind==PlayableBuildingKind.Refinery)
                {
                    amount+=IncomePerPeriod(building)/profile.IncomePeriodSeconds*extraSeconds;
                    continue;
                }
                var site=sites[building.SiteId];
                if(site.MineReserve<=0){amount+=IncomePerPeriod(building)/profile.IncomePeriodSeconds*extraSeconds;continue;}
                double rate=profile.MineIncomePerPeriod/profile.IncomePeriodSeconds;
                amount+=TerritoryRules.MineIncome(profile,site.MineReserve,seconds+extraSeconds)-rate*seconds;
                site.MineReserve=Math.Max(0,site.MineReserve-rate*(seconds+extraSeconds));
                if(site.MineReserve<1e-9)site.MineReserve=0;
                if(site.MineReserve==0)MatchEvent(MatchEventKind.MineDepleted,owner,building.Position);
            }
            amount*=IncomeMultiplier(owner);
            // Fractional installments must not leave a whole-credit affordability/HUD boundary
            // infinitesimally below its value through binary floating-point accumulation.
            double bank=Balance(owner),next=bank+amount,whole=Math.Round(next);
            if(Math.Abs(next-whole)<1e-9)amount=whole-bank;
            AddCredits(owner,amount);settledIncome[owner]=SettledIncome(owner)+amount;Fact(owner,MatchFactKind.Income,amount);
        }
        private void EmitIncomeMarkers(PlayableOwner owner)
        {
            if(FreeProvingGround)return;
            double multiplier=IncomeMultiplier(owner);
            foreach(var b in buildings.Values.OrderBy(b=>b.Id))if(b.Owner==owner&&b.Ready&&b.Sale==null&&!SystemsDisabled(b.Id))
            {
                double displayed=IncomePerPeriod(b)*BoostIncomeRate(b);
                if(displayed>0)incomeEvents.Add(new PlayableIncomeEvent(Tick,b.Id,owner,b.Kind,b.Position,displayed*multiplier));
            }
            foreach(var site in sites.Values.OrderBy(s=>s.Site.Id))
                if(!DominationEnabled&&site.Site.Kind==PlayableBuildingKind.RelayTower&&site.Locked==owner&&profile.RelayIncomePerPeriod>0)
                    incomeEvents.Add(new PlayableIncomeEvent(Tick,site.Site.Id,owner,PlayableBuildingKind.RelayTower,site.Site.Position,profile.RelayIncomePerPeriod*multiplier));
        }
        // Saved visual facts retain exact restore projections but never re-credit on restore.
        private readonly List<PlayableIncomeEvent> incomeEvents=new List<PlayableIncomeEvent>();
        private PlayableIncomeEvent[] IncomeEvents(PlayableOwner? owner)=>incomeEvents.Where(e=>(!owner.HasValue||e.Owner==owner.Value)&&IncomeSourceActive(e)&&!eliminated.Contains(e.Owner)).ToArray();
        private bool IncomeSourceActive(PlayableIncomeEvent e)=>e.Kind==PlayableBuildingKind.RelayTower
            ?sites.TryGetValue(e.BuildingId,out var site)&&site.Site.Kind==PlayableBuildingKind.RelayTower&&site.Locked==e.Owner
            :buildings.TryGetValue(e.BuildingId,out var building)&&building.Owner==e.Owner&&building.Ready&&building.Sale==null&&!SystemsDisabled(building.Id);
        private bool IncomeSourceActive(Building building)=>building.Ready&&building.Sale==null&&!SystemsDisabled(building.Id);

        private double IncomePerPeriod(Building building)
        {
            if(building.Upgrade?.Complete==true)return profile.RefineryUpgradedIncome;
            if(building.Kind==PlayableBuildingKind.Mine)
            {
                var site=sites[building.SiteId];
                if(site.MineReserveCapacity>0&&site.MineReserve==0)
                    return profile.MineIncomePerPeriod*profile.MineDepletedIncomePercent/100;
            }
            return TerritoryRules.Income(profile,building.Kind);
        }

        private double Income(PlayableOwner owner)
        {
            double amount=BaseIncome(owner);
            foreach(var b in buildings.Values)
                if(b.Owner==owner&&BoostRemaining(b.Id)>0&&IncomeSourceActive(b)&&OrbitalProductionBoost.Income(b.Kind))
                    amount+=IncomePerPeriod(b)*(BoostIncomeRate(b)-1);
            return amount*IncomeMultiplier(owner);
        }
        private double BaseIncome(PlayableOwner owner)
        {
            double amount=0;
            foreach(var b in buildings.Values)
                if(b.Owner==owner&&IncomeSourceActive(b))amount+=IncomePerPeriod(b);
            foreach(var s in sites.Values)
                if(!DominationEnabled&&s.Site.Kind==PlayableBuildingKind.RelayTower&&s.Locked==owner)amount+=profile.RelayIncomePerPeriod;
            return amount;
        }
        private void AdvanceBuildings(double dt)
        {
            foreach(var b in buildings.Values)
            {
                if(eliminated.Contains(b.Owner)||b.Sale!=null||b.Phase==ConstructionPhase.Pending)continue;
                if(!b.Ready)
                {
                    double duration=BuildingDuration(b),before=b.Build;
                    b.Build=Math.Min(duration,b.Build+dt);
                    b.Health=Math.Min(BuildingMaximumHealth(b.Owner,b.Kind),b.Health+BuildingMaximumHealth(b.Owner,b.Kind,Terms(b.TermsRevision))*(1-Terms(b.TermsRevision).ConstructionInitialHealthRatio)*(b.Build-before)/duration);
                    if(b.Build>=duration-1e-9){b.Build=duration;double maximum=BuildingMaximumHealth(b.Owner,b.Kind);if(Math.Abs(b.Health-maximum)<1e-9)b.Health=maximum;b.Ready=true;b.Phase=ConstructionPhase.Ready;Sound(PlayableSoundKind.ConstructionComplete,b.Position,PlayableEntityKind.Tank,b.Owner,true);RecordBuildingComplete(b);}
                    continue;
                }
            }
        }
        private void SpawnPlayer(NavPoint p,NavPoint rally)=>SpawnProduced(p,rally,PlayableOwner.Player);
        private void SpawnProduced(NavPoint p,NavPoint? rally,PlayableOwner owner,PlayableEntityKind kind=PlayableEntityKind.Tank)
            =>CompleteProduced(SpawnUnit(p,owner,kind),rally,owner,kind);
        private bool TrySpawnProduced(NavPoint p,NavPoint? rally,PlayableOwner owner,PlayableEntityKind kind)
        {
            int id;try{id=SpawnUnit(p,owner,kind);}catch(NavigationRetentionBudget.CapacityRefused){return false;}
            CompleteProduced(id,rally,owner,kind);return true;
        }
        private void CompleteProduced(int id,NavPoint? rally,PlayableOwner owner,PlayableEntityKind kind)
        {
            Fact(owner,MatchFactKind.UnitCompleted,PlayableUnitRules.Cost(profile,kind),(int)kind,kind.ToString());
            // AI reinforcements await their army's observed route admission. An inherited
            // factory rally must not dispatch a fresh singleton across an unsafe front.
            bool armyManaged=kind!=PlayableEntityKind.Hawk&&offline!=null&&offline.Roster[(int)owner].Control==OfflineControl.Ai&&
                (kind==PlayableEntityKind.Explorer||Spacewars.Simulation.Ai.AiRosterCatalog.Initial.For(kind).lineWeight+
                    Spacewars.Simulation.Ai.AiRosterCatalog.Initial.For(kind).supportWeight>0);
            if(PlayableUnitRules.Aircraft(kind)&&rally.HasValue){var u=units[id];u.HasAttackMove=true;u.AttackMove=rally.Value;u.CurrentOrder=new PlayableTacticalOrderSnapshot(id,owner,navigation.Generation,Math.Max(1,nextProductionSequence-1),Tick,PlayableTacticalOrderKind.AttackMove,rally.Value,0);u.Air.Actor.Goal=rally.Value;u.Air.Actor.Moving=true;u.Air.Actor.Outcome=NavigationOutcome.Moving;return;}
            if(!armyManaged&&rally.HasValue&&Geometry.IsFree(rally.Value,PlayableUnitRules.Radius(profile,kind)))
                Move(new[]{id},rally.Value,true,Math.Max(1,nextProductionSequence-1),owner,out _,retainRallyIntent:true);
        }
        private int SpawnUnit(NavPoint p,PlayableOwner owner,PlayableEntityKind kind)=>SpawnUnitCore(p,owner,kind,true);
        private int SpawnUnitCore(NavPoint p,PlayableOwner owner,PlayableEntityKind kind,bool recordArmy)
        {
            int id=nextId;int followingId=checked(id+1);var unit=new Unit{Id=id,Owner=owner,Kind=kind,Health=UnitMaximumHealth(owner,kind),PreviousPosition=p,StoppedSeconds=kind==PlayableEntityKind.Igla?profile.IglaPreparation:0};
            if(PlayableUnitRules.Aircraft(kind))InitializeHawk(unit,p);else navigation.Crowd.Add(id,p,PlayableUnitRules.Radius(profile,kind),PlayableUnitRules.Speed(profile,kind,kind==PlayableEntityKind.Tank&&Chassis(owner)),PlayableUnitRules.Turn(profile,kind),canYield:kind==PlayableEntityKind.Tank).Team=TeamOf(owner);
            if(kind==PlayableEntityKind.Tank&&navigation.Crowd.TryGet(id,out var tank)){tank.ReverseDistance=profile.TankReverseDistance;tank.ReverseSpeedRatio=profile.TankReverseSpeedRatio;tank.ReverseConeRadians=profile.TankReverseConeRadians;}
            nextId=followingId;units.Add(id,unit);soundMotion[id]=false;if(recordArmy)RecordArmy(owner);return id;
        }
        private void SpawnEnemy(NavPoint p)=>SpawnUnit(p,PlayableOwner.Enemy,PlayableEntityKind.Tank);
        private void InterruptBurst(Unit u){if(u.BurstRemaining>0&&u.Kind!=PlayableEntityKind.Hawk&&u.HawkBurstReload<=0)u.Reload=profile.ExplorerBurstPauseMs/1000d;if(u.Kind==PlayableEntityKind.Explorer)u.HawkBurstReload=0;u.BurstRemaining=0;u.BurstTarget=0;u.BurstDelay=0;}
        internal Action CombatStepForQa;
        private void ReturnWeaponToRest(Unit u,NavUnit self,double dt)
        {
            if(u.Kind==PlayableEntityKind.Igla){AimIgla(u,self,self.Heading,dt,false);return;}
            double speed=u.Kind==PlayableEntityKind.Shkval?profile.ShkvalTurretTurnSpeed:
                u.Kind==PlayableEntityKind.Explorer?profile.ExplorerTurretTurnSpeed:profile.TankTurretTurnSpeed;
            u.Turret=Turn(u.Turret,self.Heading,speed*dt);
            if(u.Kind==PlayableEntityKind.Shkval)u.ShkvalPitch=Turn(u.ShkvalPitch,ShkvalLaunchGeometry.NeutralPitch,speed*dt);
        }
        private void AdvanceCombat(double dt)
        {
            foreach(var u in units.Values)
            {
                if(!TryActor(u.Id,out var self))continue;
                if(eliminated.Contains(u.Owner)){if(u.Kind!=PlayableEntityKind.Hawk)ReturnWeaponToRest(u,self,dt);continue;}

                if(Materializing(u)||Frozen(u)||u.Kind==PlayableEntityKind.Skat)continue;
                if(u.Kind==PlayableEntityKind.Cerberus){AdvanceCerberus(u,self,dt);continue;}
                ReleaseOutOfRangeMicroFocus(u,self);
                bool defaultCombat=PrepareDefaultCombat(u,self);
                int respondingAttacker=u.HasAttackMove&&!u.ExplicitTarget?PreferredAttacker(u,self):0;
                if(!ExplorerRocketEquipped(u)&&respondingAttacker!=0&&respondingAttacker!=u.Target){InterruptBurst(u);u.Target=respondingAttacker;}
                if(u.Kind==PlayableEntityKind.Hawk){AdvanceHawkCombat(u,dt,respondingAttacker);continue;}
                if(u.Kind!=PlayableEntityKind.Explorer&&AirTarget(u.Target)){InterruptBurst(u);u.Target=0;u.ExplicitTarget=false;u.CurrentOrder=null;}
                if(u.Kind==PlayableEntityKind.Shkval){AdvanceShkval(u,self,dt,respondingAttacker);continue;}
                double range=PlayableUnitRules.Range(profile,u.Kind);bool explorer=u.Kind==PlayableEntityKind.Explorer;

                u.BurstDelay=Math.Max(0,u.BurstDelay-dt);u.Reload=Math.Max(0,u.Reload-dt);u.Repath=Math.Max(0,u.Repath-dt);
                // Physical displacement resets stationary preparation even when logical
                // Follow/navigation intent is stationary; no gameplay epsilon.
                bool physicallyMoving=!self.Position.Equals(u.PreviousPosition);
                u.StoppedSeconds=self.Moving||physicallyMoving?0:u.StoppedSeconds+dt;
                if(IsFollowing(u)){if(!ExplorerRocketEquipped(u))InterruptBurstIfFollowingCannotFire(u);if(!ExplorerRocketEquipped(u)&&!FollowCanFire(u)){u.Target=0;ReturnWeaponToRest(u,self,dt);continue;}int next=FollowDefenseTarget(u,self.Position,range);if(next==0)ClearFollowBurst(u);u.Target=next;}
                bool plainMove=!u.ExplicitTarget&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move;
                // Use the release weapon's existing moving-fire permission on
                // an owned rotation. Ordinary Tank Move keeps its prior rule.
                bool firingCombatMove=!explorer&&plainMove&&!self.Held&&profile.TankFiresWhileMoving&&
                    u.LastOrder?.Sequence==u.CurrentOrder.CommandSequence&&combatExecutionOwners!=null&&
                    combatExecutionOwners().Any(owner=>owner.IsAppliedCombatWithdrawal(u.LastOrder) ||
                        owner.IsAppliedCerberusManeuver(u.LastOrder));
                if(!explorer&&plainMove&&!self.Held&&!firingCombatMove){InterruptBurst(u);u.Target=0;}
                if(RetireLostTargetOrder(u)){ReturnWeaponToRest(u,self,dt);continue;}
                bool movingExplorer=explorer&&plainMove;
                if(u.Target!=0&&(movingExplorer&&!DefaultFireClear(u,self,u.Target)||firingCombatMove&&!KeepAutomaticTarget(u,self)))
                {InterruptBurst(u);u.Target=0;}
                if(!defaultCombat&&u.Target!=0&&(!u.ExplicitTarget&&!DefaultEnemyAlive(u.Target)||!VisibleTarget(u.Owner,u.Target)||!Target(u.Target,out var seen,out _)||(!u.ExplicitTarget&&Distance(self.Position,seen)>DefaultWeaponRange(u,u.Target)))){InterruptBurst(u);if((u.ExplicitTarget||!explorer)&&!self.Held)CombatStop(u.Id);if(u.ExplicitTarget&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack)u.CurrentOrder=null;u.Target=0;u.ExplicitTarget=false;}
                if(!defaultCombat&&ExplorerRocketEquipped(u)&&!u.ExplicitTarget&&!IsFollowing(u)&&
                    (plainMove||u.HasAttackMove||self.Held||u.CurrentOrder==null&&!self.Moving))
                {
                    int next=RocketAutomaticTarget(u,self);
                    if(next!=u.Target){InterruptBurst(u);u.Target=next;}
                }
                if(u.Target==0)
                {
                    // Source idle defense never replaces Tank movement. Explorer retains ordinary moving fire.
                    bool idle=self.Held||u.CurrentOrder==null&&!self.Moving&&!navigation.IsPending(u.Id);
                    if(movingExplorer||firingCombatMove)u.Target=DefaultDefenseTarget(u,self);
                    else if(!defaultCombat&&(idle||u.HasAttackMove)){
                        int nearby=NearestDefenseTargetWithAir(self.Position,u.Owner,range*(idle?profile.IdleAutoDefenseMultiplier:1),explorer);
                        if(DefaultFireClear(u,self,nearby))u.Target=nearby;
                    }
                    // A small delayed assault creates a loss condition while leaving time to build an army.
                    if(scriptedEnemyPatrol&&u.Owner==PlayableOwner.Enemy&&!u.PatrolStarted&&elapsed>=profile.EnemyAdvanceDelaySeconds&&!self.Moving&&!navigation.IsPending(u.Id)&&!self.Held)
                    {
                        // Scripted ground patrol uses a fixed map destination, never a hidden live entity.
                        // The known map start is not an observation of current ownership or occupancy.
                        var destination=new NavPoint(profile.Headquarters(PlayableOwner.Player).X+profile.DefenderOffsetX,profile.Headquarters(PlayableOwner.Player).Z+profile.DefenderOffsetZ);
                        if(CombatMove(u.Id,destination)){u.PatrolStarted=true;u.HasAttackMove=true;u.AttackMove=destination;}
                    }
                }
                if(!Target(u.Target,out var target,out _)){
                    // Target loss may coincide with route/geometry maintenance. Keep
                    // retrying the retained attack-move after that boundary, even
                    // when its transient combat target has already been cleared.
                    ContinueAttackMove(u,self);
                    ReturnWeaponToRest(u,self,dt);continue;
                }
                double dx=target.X-self.Position.X,dz=target.Z-self.Position.Z,d=Math.Sqrt(dx*dx+dz*dz),desired=Math.Atan2(dz,dx);
                if(u.Kind!=PlayableEntityKind.Igla)u.Turret=Turn(u.Turret,desired,(explorer?profile.ExplorerTurretTurnSpeed:profile.TankTurretTurnSpeed)*dt);
                // Static walls block line of fire; building footprints do not hide their own targets.
                bool clear=d<=DefaultWeaponRange(u,u.Target)&&DefaultFireClear(u,self,u.Target);
                if(!clear)
                {
                    InterruptBurst(u);
                    if(u.Kind==PlayableEntityKind.Igla)AimIgla(u,self,desired,dt,false);
                    if(defaultCombat)continue;
                    if(u.HasAttackMove){u.Target=0;ContinueAttackMove(u,self);continue;}
                    if(u.ExplicitTarget&&!self.Held&&u.Repath<=0&&!self.Moving&&!navigation.IsPending(u.Id)&&d>0)
                    {
                        if(TryAttackApproach(u.Id,self.Position,target,out var approach))CombatMove(u.Id,approach);
                        u.Repath=profile.AttackRepathSeconds;
                    }
                    continue;
                }
                if((!ExplorerRocketEquipped(u)||u.HasAttackMove)&&!self.Held&&!firingCombatMove&&!ResponseMovesWithoutStopping(u)&&(!explorer||u.ExplicitTarget||u.HasAttackMove||defaultCombat&&u.DefaultCombat?.Phase==DefaultCombatPhase.Pursuit)&&(self.Moving||navigation.IsPending(u.Id)))CombatStop(u.Id);
                if(u.Kind==PlayableEntityKind.Igla)
                {
                    AimIgla(u,self,desired,dt,!self.Moving&&!physicallyMoving);
                    if(self.Moving||physicallyMoving)continue;
                    if(u.Reload<=0 && u.StoppedSeconds>=profile.IglaPreparation &&
                        Math.Abs(Angle(desired-self.Heading))<=profile.IglaHalfAngle*Math.PI/180 &&
                        Math.Abs(Angle(desired-u.Turret))<=.001)iglaShooters.Add(u);
                    continue;
                }
                if(!explorer&&!profile.TankFiresWhileMoving&&(self.Moving||physicallyMoving||u.StoppedSeconds<profile.TankPreparationSeconds))continue;
                if(explorer)
                {
                    if(u.BurstRemaining>0&&u.BurstTarget!=u.Target)InterruptBurst(u);
                    if(ExplorerRocketEquipped(u))
                    {
                        bool ready=u.BurstRemaining>0?u.BurstDelay<=0:u.Reload<=0;
                        if(ready&&(d>0||AirTarget(u.Target))&&(d<1e-9||Math.Abs(Angle(desired-u.Turret))<=profile.ExplorerAimToleranceRad))
                            FireExplorerRocket(u,self.Position,target);
                        continue;
                    }
                    if((d>0||AirTarget(u.Target))&&u.Reload<=0&&(d<1e-9||Math.Abs(Angle(desired-u.Turret))<=profile.ExplorerAimToleranceRad))
                    {
                        if(u.BurstRemaining==0){u.BurstRemaining=u.BurstSize=UnitUpgraded(u)?profile.ExplorerAssaultBurstSize:profile.ExplorerBurstSize;u.BurstSpread=UnitUpgraded(u)?profile.ExplorerAssaultSpreadDeg:profile.ExplorerSpreadDeg;u.BurstTarget=u.Target;u.BurstSequence++;u.BurstDelay=0;}
                        if(u.BurstDelay<=0){FireTracer(u,self.Position,target);u.BurstRemaining--;u.BurstDelay=profile.ExplorerBurstShotIntervalMs/1000d;if(u.BurstRemaining==0)u.Reload=profile.ExplorerBurstPauseMs/1000d;}
                    }
                    continue;
                }
                if(d>0&&u.Reload<=0&&Math.Abs(Angle(desired-u.Turret))<=profile.TankAimToleranceRadians)
                {
                    projectiles.Add(new Projectile{TermsRevision=profile.Revision,Id=nextProjectile++,Owner=u.Id,Faction=u.Owner,Target=u.Target,DirectLaunchTick=Tick,Position=self.Position,Height=GroundHeight(self.Position)+DirectFireHeight,VerticalSlope=(GroundHeight(target)-GroundHeight(self.Position))/d,Kind=u.Kind,Speed=profile.TankProjectileSpeed,Radius=profile.TankProjectileCollisionRadius,Damage=WeaponDamage(u.Owner,profile.TankWeaponDamage),Remaining=profile.TankProjectileMaxTravel,DirectionX=dx/d,DirectionZ=dz/d});
                    RememberDefaultShot(u);Sound(PlayableSoundKind.Shot,self.Position,u.Kind,u.Owner,unitId:u.Id);
                    u.Reload=profile.TankWeaponReloadSeconds;
                }
            }
            ApplyIglaShots();
            ApplyFlameHits();
            CombatStepForQa?.Invoke();
        }
        internal bool TryAttackApproach(int id,NavPoint from,NavPoint target,out NavPoint result,Predicate<NavPoint> permit=null)
        {
            units.TryGetValue(id,out var actor);
            return TryTargetAttackApproach(id,from,target,actor?.Target??0,out result,permit);
        }

        private bool TryTargetAttackApproach(int id,NavPoint from,NavPoint target,int targetId,out NavPoint result,Predicate<NavPoint> permit)
        {
            result=default(NavPoint);double range=units.TryGetValue(id,out var actor)?DefaultWeaponRange(actor,targetId):profile.TankRange;double best=double.PositiveInfinity;
            double minimumRadius = 0;
            if (actor?.Kind == PlayableEntityKind.Cerberus && TryActor(targetId, out var defender))
            {
                range = profile.CerberusEngagementRange;
                minimumRadius = PlayableUnitRules.Radius(profile, actor.Kind) + defender.Radius
                    + profile.Navigation.ArrivalTolerance;
            }
            foreach(var point in PlayableUnitRules.AttackApproachCandidates(profile,from,target,range,minimumRadius))
            {
                var candidate=point;
                if(permit?.Invoke(candidate)==false||!CampaignMoveAllowed(id,candidate)||!Geometry.IsFree(candidate,actor==null?profile.TankCollisionRadius:PlayableUnitRules.Radius(profile,actor.Kind))||!FireClearAt(candidate))continue;
                // A short-range firing stance can be closer than the spacing reserved
                // for parked groups. Admit the exact footprint before routing to it.
                if (actor?.Kind == PlayableEntityKind.Cerberus)
                {
                    if (!navigation.CanOccupyCombatStance(id, candidate)) continue;
                    double travel = Distance(from, candidate);
                    if (travel < best) { best = travel; result = candidate; }
                    continue;
                }
                var slots=navigation.AllocateArrivalSlots(candidate,new[]{id});if(slots.Length!=1)continue;
                var slot=slots[0];
                // Slot allocation only reads geometry and occupancy. A caller-provided permit
                // may have side effects, so only reuse the fire check without that callback.
                bool alreadyClear=permit==null&&slot.Equals(candidate);
                candidate=slot;
                if(permit?.Invoke(candidate)==false||!CampaignMoveAllowed(id,candidate)||Distance(candidate,target)>range||(!alreadyClear&&!FireClearAt(candidate)))continue;
                double cost=Distance(from,candidate);if(cost<best){best=cost;result=candidate;}
            }
            return !double.IsPositiveInfinity(best);

            bool FireClearAt(NavPoint candidate)
            {
                if(actor?.Kind==PlayableEntityKind.Cerberus&&TryActor(id,out var hull))
                    return DualFlameStance(actor,hull,candidate,targetId,target);
                if(actor?.Kind==PlayableEntityKind.Shkval)return true;
                if(actor!=null&&targetId!=0&&ExplorerRocketEquipped(actor))
                    return SpatialFireClear(actor,candidate,GroundHeight(candidate)+DirectFireHeight,targetId,profile.HawkMissileRadius);
                if(actor!=null&&actor.Kind==PlayableEntityKind.Explorer&&AirTarget(targetId))
                    return SpatialFireClear(actor,candidate,GroundHeight(candidate)+DirectFireHeight,targetId,0);
                return actor!=null&&targetId!=0
                    ?GroundTargetFireClear(actor,candidate,target,targetId)
                    :GroundFireClear(candidate,target,actor?.Kind==PlayableEntityKind.Explorer?0:profile.TankProjectileCollisionRadius);
            }
        }
        private void AdvanceProjectiles(double dt){SalvoTrace.Record(SalvoPhase.BeginTick);for(int i=projectiles.Count-1;i>=0;i--){var p=projectiles[i];if(p.Spatial){if(AdvanceAirProjectile(p,dt))projectiles.RemoveAt(i);continue;}if(p.Rocket!=null){if(AdvanceRocket(p,dt))projectiles.RemoveAt(i);continue;}double move=Math.Min(p.Speed*dt,p.Remaining);NavPoint next=new NavPoint(p.Position.X+p.DirectionX*move,p.Position.Z+p.DirectionZ*move);double nextHeight=p.Height+p.VerticalSlope*move;if(!ProjectileClear(p.Position,next,p.Height,nextHeight,p.Radius)){Sound(PlayableSoundKind.Impact,next,p.Kind);projectiles.RemoveAt(i);continue;}int hit=HitContactAtHeight(p.Faction,p.Position,next,p.Radius,p.Height,nextHeight,out var actualContact,p.Target);if(hit!=0){Target(hit,out var hitPoint,out _);if(buildings.ContainsKey(hit))hitPoint=actualContact.Ground;if(!TryDamageWithOwner(hit,p.Damage,p.Owner,p.Faction,p,hitPoint,actualContact))continue;projectiles.RemoveAt(i);continue;}p.Position=next;p.Height=nextHeight;p.Remaining-=move;if(p.Remaining<=0)projectiles.RemoveAt(i);}SalvoTrace.Report(Tick);}
        private int FirstHit(PlayableOwner faction,NavPoint a,NavPoint b,double projectileRadius)=>FirstHitAtHeight(faction,a,b,projectileRadius,GroundHeight(a)+DirectFireHeight,GroundHeight(b)+DirectFireHeight);
        private int FirstHitAtHeight(PlayableOwner faction,NavPoint a,NavPoint b,double projectileRadius,double fromHeight,double toHeight)
            =>HitContactAtHeight(faction,a,b,projectileRadius,fromHeight,toHeight,out _);
        private int HitContactAtHeight(PlayableOwner faction,NavPoint a,NavPoint b,double projectileRadius,double fromHeight,double toHeight,out BallisticPoint contact,int explicitWreck=0,PlayableOwner? observer=null)
        {
            int hit=FirstContactAtHeight(faction,a,b,projectileRadius,fromHeight,toHeight,explicitWreck,observer,out double fraction);
            contact=double.IsPositiveInfinity(fraction)?new BallisticPoint(b.X,toHeight,b.Z):
                new BallisticPoint(a.X+(b.X-a.X)*fraction,fromHeight+(toHeight-fromHeight)*fraction,a.Z+(b.Z-a.Z)*fraction);
            if(buildings.TryGetValue(hit,out var contacted))contact=BuildingCombat(contacted).Closest(contact);
            return hit;
        }
        private int FirstContactAtHeight(PlayableOwner faction,NavPoint a,NavPoint b,double projectileRadius,double fromHeight,double toHeight,int explicitWreck,PlayableOwner? observer,out double fraction)
        {
            int hit=0;double best=double.PositiveInfinity;
            foreach(var u in units.Values)
            {
                if(!Hostile(u.Owner,faction)||!TryActor(u.Id,out var n))continue;
                double t=HitTime(n.Position,PlayableUnitRules.Radius(profile,u.Kind),u.Air!=null?HawkBodyHeight:2*PlayableUnitRules.Radius(profile,u.Kind),u.Air?.Height);
                if(t<best&&(!observer.HasValue||VisibleTarget(observer.Value,u.Id))){best=t;hit=u.Id;}
            }
            foreach(var building in buildings.Values)
            {
                if(!Hostile(building.Owner,faction)||building.Phase==ConstructionPhase.Pending)continue;
                BuildingCombat(building).Segment(new BallisticPoint(a.X,fromHeight,a.Z),new BallisticPoint(b.X,toHeight,b.Z),projectileRadius,out double t,out _);
                if(t<best&&(!observer.HasValue||VisibleTarget(observer.Value,building.Id))){best=t;hit=building.Id;}
            }
            if(wrecks.TryGetValue(explicitWreck,out var wreck)&&wreck.Blocks(Tick)){double t=HitTime(wreck.Position,wreck.Radius,Math.Max(DirectFireHeight*2,wreck.Radius*2));if(t<best){best=t;hit=wreck.Id;}}
            fraction=best;
            return hit;
            double HitTime(NavPoint p,double radius,double height,double? baseHeight=null)
            {
                if(profile.AuthoredMap==null&&!baseHeight.HasValue)return Intersection(a,b,p,radius+projectileRadius);
                double dx=b.X-a.X,dz=b.Z-a.Z,x=a.X-p.X,z=a.Z-p.Z,r=radius+projectileRadius,aa=dx*dx+dz*dz,bb=2*(x*dx+z*dz),cc=x*x+z*z-r*r;
                double enter=0,exit=1;if(aa<1e-12){if(cc>0)return double.PositiveInfinity;}else{double disc=bb*bb-4*aa*cc;if(disc<0)return double.PositiveInfinity;enter=Math.Max(0,(-bb-Math.Sqrt(disc))/(2*aa));exit=Math.Min(1,(-bb+Math.Sqrt(disc))/(2*aa));}
                double low=(baseHeight??GroundHeight(p))-projectileRadius,high=(baseHeight??GroundHeight(p))+height+projectileRadius,dy=toHeight-fromHeight;
                if(Math.Abs(dy)<1e-12){if(fromHeight<low||fromHeight>high)return double.PositiveInfinity;}else{double t1=(low-fromHeight)/dy,t2=(high-fromHeight)/dy;enter=Math.Max(enter,Math.Min(t1,t2));exit=Math.Min(exit,Math.Max(t1,t2));}
                return enter<=exit?enter:double.PositiveInfinity;
            }
        }
        private double DirectFireHeight=>profile.AuthoredMap?.DirectFireHeight??1; // Preserves the original flat fixture projectile plane.
        private double GroundHeight(NavPoint p)=>profile.AuthoredMap?.SurfaceHeight(p)??0;
        private Func<NavPoint,double> TerrainHeight=>profile.AuthoredMap==null?null:(Func<NavPoint,double>)GroundHeight;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PlayableDomain,PlayableGroundFireCache> groundFireCaches=new System.Runtime.CompilerServices.ConditionalWeakTable<PlayableDomain,PlayableGroundFireCache>();
        private PlayableGroundFireCache GroundFireCache=>groundFireCaches.GetValue(this,_=>new PlayableGroundFireCache());
        private bool GroundFireClear(NavPoint a,NavPoint b,double radius)
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.GroundFire);
            // This predicate sees immutable map solids/terrain, never live buildings,
            // vision or bodies. Exact directed endpoints and profile identity bind reuse.
            var cache=GroundFireCache;
            if(cache.TryGet(profile,a,b,radius,out bool clear)){if(navigationPerformanceTrace)cache.Hits++;return clear;}
            if(navigationPerformanceTrace)cache.Misses++;
            clear=ProjectileClear(a,b,GroundHeight(a)+DirectFireHeight,GroundHeight(b)+DirectFireHeight,radius);
            cache.Store(profile,a,b,radius,clear);return clear;
        }
        private bool GroundTargetFireClear(Unit unit,NavPoint from,NavPoint to,int target)
        {
            double radius=unit.Kind==PlayableEntityKind.Explorer?0:profile.TankProjectileCollisionRadius;
            return GroundFireClear(from,to,radius)&&
                GroundShotBodyClear(unit,from,to,GroundHeight(from)+DirectFireHeight,GroundHeight(to)+DirectFireHeight,radius,target);
        }

        private bool GroundShotBodyClear(Unit unit,NavPoint from,NavPoint to,double fromHeight,double toHeight,double radius,int target)
        {
            // Approach selection only consumes the first hit identity.
            int hit=FirstContactAtHeight(unit.Owner,from,to,radius,fromHeight,toHeight,target,unit.Owner,out _);
            return hit==0||hit==target;
        }

        private bool ProjectileClear(NavPoint a,NavPoint b,double ah,double bh,double radius)=>profile.AuthoredMap==null?projectileGeometry.SegmentFree(a,b,radius):PlayableBallistics.TerrainLineClear(new BallisticPoint(a.X,ah,a.Z),new BallisticPoint(b.X,bh,b.Z),radius,profile.AuthoredMap.Solids,profile.BallisticWallHeight,GroundHeight);
        private static double Intersection(NavPoint a,NavPoint b,NavPoint p,double radius)
        {
            double dx=b.X-a.X,dz=b.Z-a.Z,x=a.X-p.X,z=a.Z-p.Z,c=x*x+z*z-radius*radius;
            if(c<=0)return 0;double aa=dx*dx+dz*dz;if(aa==0)return double.PositiveInfinity;
            double bb=2*(x*dx+z*dz),disc=bb*bb-4*aa*c;
            if(disc<0)return double.PositiveInfinity;double t=(-bb-Math.Sqrt(disc))/(2*aa);
            return t>=0&&t<=1?t:double.PositiveInfinity;
        }
        private double BuildingRadius(PlayableBuildingKind kind)=>TerritoryRules.Radius(profile,kind);
        private static double Distance(NavPoint a,NavPoint b){double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
        private static double Turn(double current,double desired,double max){double delta=Angle(desired-current);return current+Math.Max(-max,Math.Min(max,delta));} private static double Angle(double value){while(value>Math.PI)value-=Math.PI*2;while(value<-Math.PI)value+=Math.PI*2;return value;}
        private void Damage(int id,int amount)=>DamageFromProjectile(id,amount,0);
        private void DamageFromProjectile(int id,double amount,int attackerId)=>DamageWithOwner(id,amount,attackerId,null);
        private void DamageWithOwner(int id,double amount,int attackerId,PlayableOwner? sourceOwner)
        {if(!TryDamageWithOwner(id,amount,attackerId,sourceOwner))throw new InvalidOperationException("Geometry publication preparation refused.");}
        private bool TryDamageWithOwner(int id,double amount,int attackerId,PlayableOwner? sourceOwner,Projectile impact=null,NavPoint impactPoint=default(NavPoint),BallisticPoint? impactContact=null,NavPoint incoming=default)
            =>TryDamagePrepared(id,amount,attackerId,sourceOwner,impact,impactPoint,impactContact,null,incoming);
        private bool TryDamagePrepared(int id,double amount,int attackerId,PlayableOwner? sourceOwner,Projectile impact,NavPoint impactPoint,BallisticPoint? impactContact,Dictionary<int,PlayableWreckSnapshot> preparedWrecks,NavPoint incoming=default,bool uniformDamage=false)
        {
            if(amount<=0)return true;if(wrecks.ContainsKey(id))return DestroyWreck(id,impact?.Kind??(units.TryGetValue(attackerId,out var shooter)?shooter.Kind:PlayableEntityKind.Shkval));
            var creditedOwner=sourceOwner??(units.TryGetValue(attackerId,out var source)?(PlayableOwner?)source.Owner:null);
            if(impact!=null)incoming=new NavPoint(impact.DirectionX,impact.DirectionZ);
            if(units.TryGetValue(id,out var u)){
                if(Frozen(u))return true;
                PlayableWreckSnapshot prospective=null;NavUnit destroyed=null;
                if(u.Health-amount<=0&&(preparedWrecks!=null&&preparedWrecks.ContainsKey(u.Id)||TryActor(u.Id,out destroyed))){
                    prospective=preparedWrecks==null?CreateUnitWreck(u,destroyed):preparedWrecks[u.Id];
                    // Publish the prospective blocking body before consuming the lethal hit.
                    // A busy host or refused reservation leaves all world facts unchanged,
                    // so the existing projectile retry keeps exactly this impact available.
                    if(preparedWrecks==null&&(u.Air==null||prospective.Blocks(Tick))&&!TryRebuildGeometry(additionalWreck:prospective,removedActors:u.Air!=null?null:new[]{u.Id}))return false;
                }
                if(prospective!=null||destroyed!=null||TryActor(id,out destroyed))MatchEvent(MatchEventKind.UnitsAttacked,u.Owner,impact!=null?impactPoint:prospective?.Position??destroyed.Position);
                if(impact!=null)DirectImpactSoundAt(impact,id,impactPoint,false,impactContact,damagedUnit:true);CampaignAggression(id,creditedOwner);CreditOrbital(creditedOwner,u.Owner,Math.Min(u.Health,amount));u.Health-=amount;
                if(destroyed!=null)ObserveCombatHit(id,u.Owner,destroyed.Position,attackerId,creditedOwner,incoming);
                else if(prospective!=null)ObserveCombatHit(id,u.Owner,prospective.Position,attackerId,creditedOwner,incoming);
                if(u.Health<=0){
                    if(prospective!=null){
                        if(WrecksActive)wrecks.Add(u.Id,prospective);
                        Sound(PlayableSoundKind.Destroyed,prospective.Position,u.Kind,u.Owner,unitId:u.Id,hullHeading:prospective.Heading,altitude:u.Air?.Height??0);}
                    RecordKill(creditedOwner,u.Owner,id,false,(int)u.Kind,PlayableUnitRules.Cost(profile,u.Kind));InvalidateSpatialCompletion(id);tacticalQueues.Remove(id);
                    if(!Materializing(u)&&(prospective!=null||destroyed!=null))AddLingeringVision(u.Owner,prospective?.Position??destroyed.Position,PlayableUnitRules.Vision(profile,u.Kind));
                    units.Remove(id);
                    if(preparedWrecks==null&&prospective?.Blocks(Tick)==true)ReconcileWreckRoutes();RecordArmy(u.Owner);
                    foreach(var key in centerDamage.Where(pair=>pair.Value.AttackerId==id).Select(pair=>pair.Key).ToArray())centerDamage.Remove(key);
                }else SoundUnitDamage(u);
                return true;
            }
            if(!buildings.TryGetValue(id,out var b)||b.Phase==ConstructionPhase.Pending)return true;
            // Geometry publication may refuse without committing. Keep the hit and the world
            // intact until the prospective removal can publish; the direct projectile retries.
            double damage=amount*(uniformDamage||b.Ready?1:profile.ConstructionDamageMultiplier);
            if(preparedWrecks==null&&b.Health-damage<=0&&!TryRebuildGeometry(removal:b))return false;
            MatchEvent(MatchEventKind.BuildingAttacked,b.Owner,impact!=null?impactPoint:b.Position);
            if(impact!=null)DirectImpactSoundAt(impact,id,impactPoint,true,impactContact);
            CampaignAggression(id,creditedOwner);
            b.LastDamageTime=elapsed;b.Repair=null;boostWear.Remove(id);
            double before=b.Health;CreditOrbital(creditedOwner,b.Owner,Math.Min(before,damage));b.Health-=damage;
            if(damage>0)ObserveCombatHit(id,b.Owner,b.Position,attackerId,creditedOwner,incoming);
            if(attackerId!=0&&(b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost)&&units.TryGetValue(attackerId,out var attacker)&&Hostile(attacker.Owner,b.Owner))
            {int actual=(int)Math.Ceiling(Math.Min(before,before-b.Health));if(actual>0)centerDamage[b.Id+":"+attackerId]=new PlayableCenterDamageSnapshot(b.Id,b.Owner,attackerId,navigation.Generation,Tick,actual);}
            if(b.Health<=0){foreach(var key in centerDamage.Where(pair=>pair.Value.CenterId==id).Select(pair=>pair.Key).ToArray())centerDamage.Remove(key);RemoveBuildingWithAttacker(b,false,creditedOwner,destroyed:true);}
            else {Sound(PlayableSoundKind.HeavyDamage,b.Position,PlayableEntityKind.Tank,b.Owner,true);Sound(PlayableSoundKind.BaseThreat,b.Position,PlayableEntityKind.Tank,b.Owner,true);}
            return true;
        }
        private bool Target(int id,out NavPoint p,out int hp){if(wrecks.TryGetValue(id,out var wreck)&&wreck.Blocks(Tick)){p=wreck.Position;hp=1;return true;}Unit u;if(units.TryGetValue(id,out u)){NavUnit n;if(TryActor(id,out n)){p=n.Position;hp=(int)Math.Ceiling(u.Health);return true;}}Building b;if(buildings.TryGetValue(id,out b)&&b.Phase!=ConstructionPhase.Pending){p=b.Position;hp=(int)Math.Ceiling(b.Health);return true;}p=default(NavPoint);hp=0;return false;}
        private int NearestDefenseTarget(NavPoint point,PlayableOwner observer,double range)
            =>NearestSpatialTarget(point,observer,range,true);
        private int NearestDefenseTargetWithAir(NavPoint point,PlayableOwner observer,double range,bool includeAir)
            =>NearestSpatialTarget(point,observer,range,true,includeAir);
        private int NearestVisible(NavPoint point,PlayableOwner observer)
        {
            int best=0;double distance=double.MaxValue;
            foreach(int id in units.Keys.Concat(buildings.Keys))if(VisibleTarget(observer,id)&&Target(id,out var position,out _))
            {double d=Distance(point,position);if(d<distance){distance=d;best=id;}}
            return best;
        }
        private void CheckOutcome()
        {
            if(offline!=null){CheckOfflineOutcome();return;}
            bool player=false,enemy=false;
            foreach(var b in buildings.Values)if(TerritoryRules.Center(b.Kind)&&b.Phase!=ConstructionPhase.Pending){if(b.Owner==PlayableOwner.Player)player=true;else enemy=true;}
            if(!enemy)Outcome=PlayableMatchOutcome.PlayerWon;else if(!player)Outcome=PlayableMatchOutcome.PlayerLost;
            if(Outcome!=PlayableMatchOutcome.Playing)
            {
                var loser=!enemy?PlayableOwner.Enemy:PlayableOwner.Player;RecordPlayerExit(loser,false);
                foreach(var u in units.Values)if(u.Owner==loser){InvalidateSpatialCompletion(u.Id);u.Target=0;u.HasAttackMove=false;navigation.Stop(u.Id,true);}
                var pending=new List<Building>();foreach(var b in buildings.Values)if(b.Owner==loser){b.Orders.Clear();b.RepeatTank=false;if(b.Phase==ConstructionPhase.Pending)pending.Add(b);}
                foreach(var b in pending)RemoveBuilding(b,false);
                foreach(var site in sites.Values)if(site.Claimant==loser&&!site.Locked.HasValue){site.Claimant=null;site.Progress=0;}
            }
        }
        private void AddBuilding(PlayableOwner owner,PlayableBuildingKind kind,NavPoint p,bool ready)
        {
            int siteId=HomeSite(owner);
            var b=new Building{TermsRevision=profile.Revision,Id=nextId++,Owner=owner,Kind=kind,Position=p,SiteId=siteId,PaidCost=TerritoryRules.Cost(profile,kind),Health=BuildingMaximumHealth(owner,kind)*(ready?1:profile.ConstructionInitialHealthRatio),Ready=ready,Phase=ready?ConstructionPhase.Ready:ConstructionPhase.Constructing};
            buildings.Add(b.Id,b);if(ready)RecordBuildingCount(owner,kind);sites[siteId].CenterId=b.Id;sites[siteId].Locked=owner;sites[siteId].Claimant=owner;sites[siteId].Progress=1;
        }
        private List<Building> geometryFoundations;
        internal readonly struct GeometryFoundationSource
        {
            private readonly object target;private readonly double initialHealth;private readonly double health;private readonly string blocked;
            internal GeometryFoundationSource(object value,PlayableDomain domain){var b=(Building)value;target=b;health=b.Health;blocked=b.BlockedReason;var terms=domain.Terms(b.TermsRevision);initialHealth=domain.BuildingMaximumHealth(b.Owner,b.Kind,terms)*terms.ConstructionInitialHealthRatio;}
            internal bool Matches(PlayableDomain d){var b=(Building)target;return d.buildings.TryGetValue(b.Id,out var current)&&ReferenceEquals(b,current)&&b.Phase==ConstructionPhase.Pending&&!b.Ready&&b.Sale==null&&b.Health.Equals(health)&&ReferenceEquals(b.BlockedReason,blocked);}
            internal NavObstacle Footprint(PlayableDomain d){var b=(Building)target;double r=d.BuildingRadius(b.Kind);return new NavObstacle(b.Position.X-r,b.Position.Z-r,b.Position.X+r,b.Position.Z+r);}
            internal void ApplyNoThrow(){var b=(Building)target;b.Phase=ConstructionPhase.Constructing;b.BlockedReason=null;b.Health=initialHealth;}
        }
        internal int PreparedGeometryFoundationCount=>geometryFoundations?.Count??0;
        internal void ObservePreparedGeometry(GeometryFoundationSource[] rows){if(rows.Length!=PreparedGeometryFoundationCount)throw new InvalidOperationException("Foundation source count changed.");for(int i=0;i<rows.Length;i++)rows[i]=new GeometryFoundationSource(geometryFoundations[i],this);}
        internal bool PreparedGeometryCurrent(NavGeometry source,GeometryFoundationSource[] rows){if(!ReferenceEquals(Geometry,source)||rows.Length!=PreparedGeometryFoundationCount)return false;for(int i=0;i<rows.Length;i++)if(!rows[i].Matches(this))return false;return true;}
        internal void ApplyPreparedGeometryNoThrow(NavGeometry next,GeometryFoundationSource[] rows){Geometry=next;for(int i=0;i<rows.Length;i++)rows[i].ApplyNoThrow();}
        private void RebuildGeometry(){if(!TryRebuildGeometry())throw new InvalidOperationException("Geometry publication preparation refused.",navigation.LastGeometryPreparationFailure);ReconcileWreckRoutes();}
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Keep the same atomic removal of dead actors in both arms of the benchmark.
        private static readonly bool BenchmarkWithoutWrecks=Array.IndexOf(Environment.GetCommandLineArgs(),"-battleBenchmark")>=0
            &&Array.IndexOf(Environment.GetCommandLineArgs(),"-battleNoWrecks")>=0;
#else
        private const bool BenchmarkWithoutWrecks=false;
#endif
        private bool TryRebuildGeometry(List<Building> foundations=null,Building removal=null,int[] removedWrecks=null,PlayableWreckSnapshot additionalWreck=null,ICollection<Building> removals=null,ICollection<PlayableWreckSnapshot> additionalWrecks=null,IEnumerable<PlayableWreckSnapshot> wreckState=null,int[] removedActors=null)
        {
            using var wreckGeometryTiming=PerformanceProfile.Measure(ProfileStage.WreckGeometry);
            var obstacles=new List<NavObstacle>(StaticObstacles());
            foreach(var b in buildings.Values)
            {
                bool removed=false;
                if(removal!=null||removals!=null)for(var ancestor=b;ancestor!=null;ancestor=buildings.TryGetValue(ancestor.ParentId,out var parent)?parent:null)if(ReferenceEquals(ancestor,removal)||removals!=null&&removals.Contains(ancestor)){removed=true;break;}
                if(!removed&&(b.Phase!=ConstructionPhase.Pending||foundations!=null&&foundations.Contains(b))){double r=BuildingRadius(b.Kind);obstacles.Add(new NavObstacle(b.Position.X-r,b.Position.Z-r,b.Position.X+r,b.Position.Z+r));}
            }
            // Prospective deaths can have lower IDs than existing wrecks. Publish
            // the same canonical ID order that current-format restoration validates.
            var retainedWrecks=wreckState??wrecks.Values.Where(w=>(!w.IsAirborneKind||WreckObstaclePublished(w))&&(removedWrecks==null||!removedWrecks.Contains(w.Id)));
            var combinedWrecks=retainedWrecks.Concat(additionalWreck==null?Array.Empty<PlayableWreckSnapshot>():new[]{additionalWreck})
                .Concat(additionalWrecks??Array.Empty<PlayableWreckSnapshot>());
            if(WrecksActive)obstacles.AddRange(combinedWrecks.Where(w=>w.Blocks(Tick)).OrderBy(w=>w.Id).Select(w=>new NavObstacle(w.Position,w.Radius,w.PhysicalLayer)));
            var next=NavGeometry.CreateRebindSource(profile.ArenaHalfExtent,obstacles.ToArray(),Geometry.Revision+1);geometryFoundations=foundations;
            try{bool changed;using(PerformanceProfile.Measure(ProfileStage.WreckRebind))changed=navigation.TryChangeGeometry(next,this,removedActors);if(!changed)return false;navigation.UsesRecastGroundRoutes=profile.UsesRecastGroundRoutes;Geometry=navigation.AuthorityGeometry;if(foundations!=null)foreach(var b in foundations)if(b.Phase==ConstructionPhase.Pending){b.Phase=ConstructionPhase.Constructing;b.BlockedReason=null;b.Health=BuildingMaximumHealth(b.Owner,b.Kind,Terms(b.TermsRevision))*Terms(b.TermsRevision).ConstructionInitialHealthRatio;}InvalidateTacticalArrivalAfterGeometryChange();return true;}finally{geometryFoundations=null;}
        }
        internal PlayableSnapshot Snapshot(long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,string failure,int seed){var es=new List<PlayableEntitySnapshot>();foreach(var u in units.Values){NavUnit n;if(TryActor(u.Id,out n))es.Add(new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,n.Position,(int)Math.Ceiling(u.Health),n.Moving&&!Frozen(u),u.Kind==PlayableEntityKind.Skat&&!Frozen(u)?u.DroneRepair.BeamTarget:u.Target,n.Heading,u.Turret,upgraded:u.Kind==PlayableEntityKind.Hawk?HawkSalvo(u.Owner):UnitUpgraded(u),held:n.Held,navigationOutcome:WaitingForWreck(u.Id)?NavigationOutcome.Waiting:n.Outcome,maxHealth:UnitMaximumHealth(u.Owner,u.Kind),exactHealth:u.Health,weaponSystemsLevel:ArmySystemsLevel(u.Owner,true),defensiveSystemsLevel:ArmySystemsLevel(u.Owner,false),altitude:u.Air?.Height??0,pitch:u.Air?.Pitch??0,roll:u.Air?.Roll??0,departing:u.Air?.Departing??false,weaponPitch:u.Kind==PlayableEntityKind.Shkval?u.ShkvalPitch:ExplorerPitch(u),weaponSequence:u.Kind==PlayableEntityKind.Shkval?u.BurstSequence%4:0,weaponReload:u.Kind==PlayableEntityKind.Shkval?u.Reload:0,shkvalCells:u.ShkvalCells==null?null:new ShkvalCellsSnapshot(u.ShkvalCells),materializationProgress:Materializing(u)?1-MaterializationRemaining(u)/u.MaterializationDuration:1,stasisRemaining:StasisRemaining(u),rocketLauncher:ExplorerRocketEquipped(u),iglaBeam:IglaBeam(u,null),flames:Frozen(u)?null:FlamePose(u)));}var bs=new List<PlayableBuildingSnapshot>();foreach(var b in buildings.Values){double duration=BuildingDuration(b);bs.Add(new PlayableBuildingSnapshot(b.Id,b.Owner,b.Kind,b.Position,(int)Math.Ceiling(b.Health),b.Ready?1:b.Build/duration,b.Queue,b.ProductionProgress,b.Rally,b.SiteId,b.SlotId,b.ParentId,b.Phase,b.BlockedReason,b.Heading,orders:ProductionSnapshot(b),repeat:b.RepeatTank,lifecycle:LifecycleSnapshot(b),upgrade:UpgradeSnapshot(b),refineryUpgraded:b.Upgrade?.Complete==true,repeatKind:b.RepeatKind,research:b.Kind==PlayableBuildingKind.ScientificCenter?ResearchSnapshot(b.Owner):null,hasRally:b.HasRally,pendingRally:PendingRally(b.Id),launchPhase:b.LaunchPhase,launchTime:b.LaunchTime,launchKind:StagedUnitKind(b),launchUpgraded:b.Kind==PlayableBuildingKind.Hangar?HawkSalvo(b.Owner):FactoryStagedUpgraded(b),maxHealth:BuildingMaximumHealth(b.Owner,b.Kind),turret:TurretSnapshot(b),healing:RepairTowerSnapshot(b),scientificCenterUpgraded:CenterUpgraded(b.Id),producerUpgraded:b.ProducerUpgrade?.Complete==true,systemFailureRemaining:SystemFailureRemaining(b.Id),boostRemaining:BoostRemaining(b.Id),concreteLevel:ConcreteLevel(b.Owner),weaponSystemsLevel:TerritoryRules.ArmedDefense(b.Kind)?ArmySystemsLevel(b.Owner,true):0));}var ps=new List<PlayableProjectileSnapshot>();foreach(var p in projectiles){double height=p.Height,pitch=p.Spatial?Math.Atan2(p.Hawk?.Direction.Y??p.DirectionY,Math.Sqrt(Math.Pow(p.Hawk?.Direction.X??p.DirectionX,2)+Math.Pow(p.Hawk?.Direction.Z??p.DirectionZ,2))):Math.Atan(p.VerticalSlope),heading=Math.Atan2(p.DirectionZ,p.DirectionX);if(p.Rocket!=null){double progress=p.Rocket.Elapsed/p.Rocket.Flight.Duration;var point=p.Rocket.Flight.Point(progress);var tangent=p.Rocket.Flight.Tangent(progress);height=point.Y;pitch=Math.Atan2(tangent.Y,Math.Sqrt(tangent.X*tangent.X+tangent.Z*tangent.Z));heading=Math.Atan2(tangent.Z,tangent.X);}ps.Add(new PlayableProjectileSnapshot(p.Id,p.Owner,p.Target,p.Position,p.Kind,heading,height:height,pitch:pitch,faction:p.Faction,age:p.Rocket?.Elapsed??0,launch:p.Rocket?.Launch,directLaunchTick:p.DirectLaunchTick,directBarrel:p.DirectBarrel,rocket:p.Hawk!=null));}return new PlayableSnapshot(profile.ProfileId,profile.Revision,navigation.Generation,seed,sequence,Tick,status,paused,Outcome,(int)Math.Floor(Balance(PlayableOwner.Player)),Geometry,es.ToArray(),bs.ToArray(),ps.ToArray(),metrics,failure,SiteSnapshots(),Income(PlayableOwner.Player)/profile.IncomePeriodSeconds,population:Population(PlayableOwner.Player),researchAvailability:ResearchAvailability(PlayableOwner.Player),ownerResearch:ResearchSnapshot(PlayableOwner.Player),participants:offline?.Roster.ToArray(),activeProfile:profile,domination:DominationSnapshot(),discoveredSites:sites.Values.Select(s=>s.Site).ToArray(),sounds:Sounds(),incomeEvents:IncomeEvents(null),groundTrails:groundTrails.Visible(Tick,lifetimeTicks:profile.GroundTrailLifetimeTicks),buildingDestructions:BuildingDestructions(),wrecks:WreckSnapshots()).WithMines(MineSnapshots(null),MineBlastSnapshots(null)).WithMatchEvents(MatchEvents(PlayableOwner.Player)).WithOrbital(OrbitalView(PlayableOwner.Player));}
    }
}
