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
        private sealed class Unit { public int ScenarioIndex=-1; public bool? AuthoredUpgrade; public PlayableOrderStamp LastOrder; public PlayableEntityKind Kind; public int BurstRemaining,BurstTarget,BurstSequence,BurstSize; public double BurstSpread; public double BurstDelay; public bool ExplicitTarget; public NavPoint PreviousPosition,Velocity; public int Id,Health,Target; public PlayableOwner Owner; public double Turret,Reload,StoppedSeconds,Repath; public NavPoint AttackMove; public bool HasAttackMove,PatrolStarted; public PlayableTacticalOrderSnapshot CurrentOrder; }
        private sealed class Building { public int TermsRevision; public RefineryUpgrade Upgrade; public double? LastDamageTime; public SaleState Sale; public RepairState Repair; public int Id,SiteId,SlotId,ParentId,PaidCost; public double Health,Heading,RetryAt; public ConstructionPhase Phase; public string BlockedReason; public readonly HashSet<int> Evacuated=new HashSet<int>(); public PlayableOwner Owner; public PlayableBuildingKind Kind; public NavPoint Position,Rally; public bool HasRally; public double Build; public bool Ready; public readonly List<ProductionOrder> Orders=new List<ProductionOrder>(); public bool RepeatTank; public PlayableEntityKind RepeatKind; public int Queue=>Orders.Count; public double ProductionProgress=>Orders.Count>0&&Orders[0].Active?1-Orders[0].Remaining/Orders[0].Duration:0; }
        private sealed class Projectile { public int TermsRevision; public Rocket Rocket; public int Id,Owner,Target; public NavPoint Position; public PlayableEntityKind Kind; public int Damage; public double Speed,Radius,Remaining,DirectionX,DirectionZ,Height,VerticalSlope; public PlayableOwner Faction; }
        private readonly NavGeometry projectileGeometry;
        internal PlayableProfile RoutingProfile=>profile;
        private PlayableProfile profile; private readonly NavigationSession navigation; private readonly bool scriptedEnemyPatrol; private readonly AuthoritySlotMap<int,Unit> units=new AuthoritySlotMap<int,Unit>(); private readonly AuthoritySlotMap<int,Building> buildings=new AuthoritySlotMap<int,Building>(); private readonly List<Projectile> projectiles=new List<Projectile>();
        private long nextOrderRevision=1; private int nextId=1,nextProjectile=1; private double credits,incomeClock,elapsed; private double enemyCredits; private readonly AuthoritySlotMap<string,PlayableCenterDamageSnapshot> centerDamage=new AuthoritySlotMap<string,PlayableCenterDamageSnapshot>(); internal long Tick {get;private set;} internal PlayableMatchOutcome Outcome{get;private set;}
        internal PlayableDomain(PlayableProfile profile,long generation):this(profile,generation,true){}
        internal PlayableDomain(PlayableProfile profile,long generation,bool scriptedEnemyPatrol):this(profile,generation,scriptedEnemyPatrol,false,null){}
        private PlayableDomain(PlayableProfile profile,long generation,bool scriptedEnemyPatrol,bool empty,NavGeometry savedGeometry):this(profile,generation,scriptedEnemyPatrol,empty,savedGeometry,null){}
        private PlayableDomain(PlayableProfile profile,long generation,bool scriptedEnemyPatrol,bool empty,NavGeometry savedGeometry,OfflineMatchConfiguration configuration)
        { this.profile=profile;termProfiles[profile.Revision]=profile; offline=configuration;if(offline!=null&&!WorldWire.Binding(profile).SequenceEqual(WorldWire.Binding(offline.Profile)))throw new ArgumentException("Offline profile mismatch.");foreach(var owner in Owners)ownerCredits.Add(owner,profile.StartingCredits); this.scriptedEnemyPatrol=scriptedEnemyPatrol; projectileGeometry=new NavGeometry(profile.ArenaHalfExtent,SolidObstacles(),1); Geometry=savedGeometry??new NavGeometry(profile.ArenaHalfExtent,StaticObstacles(),1); navigation=new NavigationSession(generation,Geometry,profile.Navigation,terrain:profile.AuthoredMap);navigation.ConfigureMarch(profile.GroupMarchMaximumStretch,profile.Revision);navigation.PreserveInstalledExecution=RetainsInstalledExecution;navigation.RouteActivated+=ActivateSpatialExecution;navigation.RouteRequested+=ObserveSpatialRequest;navigation.RouteFailed+=RecordTacticalRouteFailure;navigation.MovementCancelled+=CancelSpatialExecution; credits=profile.StartingCredits;enemyCredits=profile.StartingCredits;InitializeSites(); Outcome=PlayableMatchOutcome.Playing;
          if(empty)return;
          if(offline!=null){MaterializeOffline();return;}
          AddBuilding(PlayableOwner.Player,PlayableBuildingKind.Headquarters,profile.Headquarters(PlayableOwner.Player),true); AddBuilding(PlayableOwner.Enemy,PlayableBuildingKind.Headquarters,profile.Headquarters(PlayableOwner.Enemy),true); RebuildGeometry();
          SpawnUnit(new NavPoint(profile.Headquarters(PlayableOwner.Player).X+profile.DefenderOffsetX,profile.Headquarters(PlayableOwner.Player).Z-profile.DefenderOffsetZ),PlayableOwner.Player,PlayableEntityKind.Explorer);
          SpawnUnit(new NavPoint(profile.Headquarters(PlayableOwner.Enemy).X-profile.DefenderOffsetX,profile.Headquarters(PlayableOwner.Enemy).Z),PlayableOwner.Enemy,PlayableEntityKind.Explorer); }
        internal NavGeometry Geometry {get;private set;} internal NavigationSession Navigation {get{return navigation;}}
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
        // Runtime records admitted human intents before pause/route barriers; direct authority callers retain their ingress count.
        internal PlayableCommandStatus ApplyWithoutHumanStatistics(PlayableCommand command,out string message)=>ApplyWithStatistics(command,out message,false);
        private PlayableCommandStatus ApplyWithStatistics(PlayableCommand command,out string message,bool recordHuman)
        {
            message="Invalid command provenance.";
            if(command==null||!Enum.IsDefined(typeof(PlayableOrderMode),command.Mode)||!Enum.IsDefined(typeof(PlayableOrderOrigin),command.Origin)||command.Sequence<=0)return PlayableCommandStatus.InvalidSequence;
            if(command.Origin==PlayableOrderOrigin.Ai&&(string.IsNullOrWhiteSpace(command.Source)||command.ActionId<=0||command.JobId<=0))return PlayableCommandStatus.Rejected;
            if(command.Origin!=PlayableOrderOrigin.Ai&&(command.Source!=null||command.ActionId!=0||command.JobId!=0))return PlayableCommandStatus.Rejected;
            if(command.Generation!=navigation.Generation)return PlayableCommandStatus.StaleGeneration;
            if(!Authorizes(command.PlayerId))return PlayableCommandStatus.InvalidOwner;
            if(Outcome!=PlayableMatchOutcome.Playing&&command.Kind!=PlayableCommandKind.Restart){message="Match is finished.";return PlayableCommandStatus.Rejected;}
            if(offline!=null){var owner=OwnerFor(command.PlayerId);if(offlineSequences.TryGetValue(owner,out var previous)&&command.Sequence<=previous)return PlayableCommandStatus.InvalidSequence;offlineSequences[owner]=command.Sequence;}
            if(command.Sequence>=nextAiSequence)nextAiSequence=checked(command.Sequence+1);
            bool tactical=command.Kind==PlayableCommandKind.Move||command.Kind==PlayableCommandKind.AttackMove||command.Kind==PlayableCommandKind.Attack||command.Kind==PlayableCommandKind.Stop||command.Kind==PlayableCommandKind.Hold||command.Kind==PlayableCommandKind.Follow;
            // Navigation retains HOLD while an accepted replacement awaits its route.
            // Priority follows durable accepted origin, independently of transient route/kind.
            if(tactical&&command.Origin==PlayableOrderOrigin.Ai&&command.EntityIds.Any(id=>units.TryGetValue(id,out var u)&&navigation.Crowd.TryGet(id,out var n)&&n.Held&&(u.LastOrder==null||u.LastOrder.Origin!=PlayableOrderOrigin.Ai||u.LastOrder.Source!=command.Source||u.LastOrder.Owner!=u.Owner||u.LastOrder.Generation!=navigation.Generation)))
            {message="Human/unknown HOLD has priority.";return PlayableCommandStatus.Rejected;}
            PlayableCommandStatus? spatialRejection=null;
            if(command.Kind==PlayableCommandKind.Move||command.Kind==PlayableCommandKind.AttackMove){
                spatialRejection=MoveRecipientFailure(command.CopyEntityIds(),OwnerFor(command.PlayerId));
                if(!spatialRejection.HasValue){if(!navigation.Crowd.Locate(command.Target,0,out var original)||command.TargetLocation.HasValue&&!command.TargetLocation.Value.Equals(original))spatialRejection=PlayableCommandStatus.InvalidTarget;else command=command.WithResolvedLocation(original);}
                if(spatialRejection.HasValue)message="Invalid destination.";
            }
            if(command.Mode==PlayableOrderMode.Append){var appended=spatialRejection??AppendTactical(command,out message);if(recordHuman&&command.Origin==PlayableOrderOrigin.Human)RecordHumanAction(command.PlayerId);return appended;}
            var previousBehaviors=tactical?CaptureTacticalBehaviors(command):null;
            bool prepared=tactical&&!spatialRejection.HasValue;
            if(prepared)navigation.PrepareCommandGroup(command,nextOrderRevision,Tick,TeamOf(OwnerFor(command.PlayerId)));
            HashSet<int> followApplied=null;PlayableCommandStatus status;try{status=spatialRejection??(command.Kind==PlayableCommandKind.Follow?Follow(command.CopyEntityIds(),command.TargetId,command.Sequence,OwnerFor(command.PlayerId),out message,out followApplied):ApplyCore(command,out message));}catch{if(prepared)navigation.FinishCommandGroup(false,Array.Empty<int>());throw;}
            if(prepared)navigation.FinishCommandGroup(status==PlayableCommandStatus.Applied,command.Kind==PlayableCommandKind.Follow?(IEnumerable<int>)followApplied:command.EntityIds);
            if(tactical&&status==PlayableCommandStatus.Applied){long revision=nextOrderRevision++;foreach(int id in command.EntityIds.Distinct()){if(command.Kind==PlayableCommandKind.Follow&&!followApplied.Contains(id))continue;var u=units[id];InvalidateSpatialCompletion(id);u.LastOrder=new PlayableOrderStamp(id,u.Owner,navigation.Generation,revision,command.Sequence,Tick,command.Origin,command.Kind,command.Source,command.JobId,command.ActionId);if(u.CurrentOrder!=null&&command.TargetLocation.HasValue){var o=u.CurrentOrder;u.CurrentOrder=new PlayableTacticalOrderSnapshot(o.UnitId,o.Owner,o.Generation,o.CommandSequence,o.IssuedTick,o.Kind,o.Destination,o.TargetId,command.TargetLocation);}AdmitSpatialExecution(u);}RegisterAcceptedQueue(command,revision,previousBehaviors,command.Kind==PlayableCommandKind.Follow?(IEnumerable<int>)followApplied:command.EntityIds);}
            if(recordHuman&&command.Origin==PlayableOrderOrigin.Human&&Enum.IsDefined(typeof(PlayableCommandKind),command.Kind)&&command.Kind!=PlayableCommandKind.Restart)RecordHumanAction(command.PlayerId);
            return status;
        }
        private PlayableCommandStatus ApplyCore(PlayableCommand command,out string message)
        { message=null; if(command.Generation!=navigation.Generation)return PlayableCommandStatus.StaleGeneration; if(!Authorizes(command.PlayerId))return PlayableCommandStatus.InvalidOwner; var issuer=OwnerFor(command.PlayerId); if(Outcome!=PlayableMatchOutcome.Playing && command.Kind!=PlayableCommandKind.Restart){message="Match is finished.";return PlayableCommandStatus.Rejected;}
          switch(command.Kind){
            case PlayableCommandKind.UpgradeRefinery:return StartRefineryUpgrade(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,out message);
            case PlayableCommandKind.CancelRefineryUpgrade:return CancelRefineryUpgrade(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,out message);
            case PlayableCommandKind.QueueResearch:return QueueResearch(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,command.ResearchKind,out message);
            case PlayableCommandKind.CancelResearch:return CancelResearch(command.EntityIds.Count>0?command.EntityIds[0]:0,command.ProductionOrderId,issuer,out message);
            case PlayableCommandKind.SellBuilding:
                var sale=SellBuilding(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,out message);
                if(sale==PlayableCommandStatus.Applied)InterruptUnreadyResearchCenters();
                return sale;
            case PlayableCommandKind.StartBuildingRepair:return StartRepair(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,out message);
            case PlayableCommandKind.CancelBuildingRepair:return CancelRepair(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,out message);
            case PlayableCommandKind.BuildAt:return BuildAt(command.SiteId,command.SlotId,command.BuildingKind,command.ParentId,issuer,out message);
            case PlayableCommandKind.CancelBuilding:return CancelBuilding(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,out message);
            case PlayableCommandKind.BuildFactory:return Build(PlayableBuildingKind.Factory,command.Pad,issuer,out message);
            case PlayableCommandKind.BuildRefinery:return Build(PlayableBuildingKind.Refinery,command.Pad,issuer,out message);
            case PlayableCommandKind.QueueShkval:return QueueUnit(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,PlayableEntityKind.Shkval,out message);
            case PlayableCommandKind.QueueExplorer:return QueueUnit(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,PlayableEntityKind.Explorer,out message);
            case PlayableCommandKind.QueueTank:return QueueTank(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,out message);
            case PlayableCommandKind.CancelProductionOrder:return CancelProduction(command.EntityIds.Count>0?command.EntityIds[0]:0,command.ProductionOrderId,issuer,out message);
            case PlayableCommandKind.ToggleRepeatProduction:return ToggleRepeat(command.EntityIds.Count>0?command.EntityIds[0]:0,issuer,out message,command.UnitKind);
            case PlayableCommandKind.SetRally:return Rally(command.EntityIds.Count>0?command.EntityIds[0]:0,command.Target,issuer,command.Sequence,out message);
            case PlayableCommandKind.Move: case PlayableCommandKind.AttackMove:return Move(command.CopyEntityIds(),command.Target,command.Kind==PlayableCommandKind.AttackMove,command.Sequence,issuer,out message);
            case PlayableCommandKind.Attack:return Attack(command.CopyEntityIds(),command.TargetId,command.Sequence,issuer,out message);
            case PlayableCommandKind.Stop:return Stop(command.CopyEntityIds(),issuer,false,out message);
            case PlayableCommandKind.Hold:return Stop(command.CopyEntityIds(),issuer,true,out message);
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
        private PlayableCommandStatus Move(int[] ids,NavPoint target,bool attackMove,long commandSequence,PlayableOwner issuer,out string message)
        {
            message="Invalid destination.";
            var recipientFailure=MoveRecipientFailure(ids,issuer);if(recipientFailure.HasValue)return recipientFailure.Value;
            if(ids.Any(id=>!Geometry.IsFree(target,PlayableUnitRules.Radius(profile,units[id].Kind))))return PlayableCommandStatus.InvalidTarget;
            Array.Sort(ids);
            var slots=navigation.AllocateArrivalSlots(target,ids);
            if(slots.Length!=ids.Length)return PlayableCommandStatus.InvalidTarget;
            if(!navigation.MoveGroup(ids,slots))return PlayableCommandStatus.Overflow;
            for(int i=0;i<ids.Length;i++){var u=units[ids[i]];InterruptBurst(u);u.ExplicitTarget=false;u.Target=0;u.HasAttackMove=attackMove;u.AttackMove=slots[i];u.StoppedSeconds=0;u.CurrentOrder=new PlayableTacticalOrderSnapshot(u.Id,u.Owner,navigation.Generation,commandSequence,Tick,attackMove?PlayableTacticalOrderKind.AttackMove:PlayableTacticalOrderKind.Move,target,0);}
            message="Order accepted.";return PlayableCommandStatus.Applied;
        }
        private PlayableCommandStatus Attack(int[] ids,int target,long commandSequence,PlayableOwner issuer,out string message)
        {
            RefreshVision();message="No visible enemy target.";
            if(!VisibleTarget(issuer,target))return PlayableCommandStatus.InvalidTarget;
            if(ids.Length==0||ids.Distinct().Count()!=ids.Length||ids.Any(id=>!units.TryGetValue(id,out var unit)||unit.Owner!=issuer))return PlayableCommandStatus.InvalidEntity;
            foreach(int id in ids){var u=units[id];InterruptBurst(u);u.ExplicitTarget=true;navigation.Stop(id,false);u.Target=target;u.HasAttackMove=false;u.Repath=0;u.CurrentOrder=new PlayableTacticalOrderSnapshot(u.Id,u.Owner,navigation.Generation,commandSequence,Tick,PlayableTacticalOrderKind.Attack,default(NavPoint),target);}
            message="Attack ordered.";return PlayableCommandStatus.Applied;
        }
        private PlayableCommandStatus Stop(int[] ids,PlayableOwner issuer,bool hold,out string message){if(ids.Length==0||ids.Distinct().Count()!=ids.Length||ids.Any(id=>!units.TryGetValue(id,out var unit)||unit.Owner!=issuer)){message="No selectable tanks.";return PlayableCommandStatus.InvalidEntity;}foreach(int id in ids){var u=units[id];InterruptBurst(u);u.ExplicitTarget=false;u.Target=0;u.HasAttackMove=false;u.CurrentOrder=null;navigation.Stop(id,hold);}message=hold?"HOLD.":"Stopped.";return PlayableCommandStatus.Applied;}
        internal void Step(double dt)
        {
            if(Outcome!=PlayableMatchOutcome.Playing)return;
            ObserveSoundMotion();
            Tick++;elapsed+=dt;incomeEvents.RemoveAll(e=>(Tick-e.Tick)/30d>=profile.IncomeMarkerDurationSeconds);impacts.RemoveAll(p=>(Tick-p.Tick)/30d>profile.ImpactEffectSec);
            // Pinned web ordering: movement/projectiles precede capture, foundations, construction and settlement.
            foreach(var u in units.Values)if(navigation.Crowd.TryGet(u.Id,out var before))u.PreviousPosition=before.Position;
            AdvanceFollow();navigation.Step(dt);RefreshSpatialCompletions();foreach(var u in units.Values)if(navigation.Crowd.TryGet(u.Id,out var after)){u.Velocity=new NavPoint((after.Position.X-u.PreviousPosition.X)/dt,(after.Position.Z-u.PreviousPosition.Z)/dt);if(after.Held&&!navigation.IsPending(u.Id)){u.CurrentOrder=null;u.HasAttackMove=false;}if(!HasQueueExecution(u.Id)&&u.CurrentOrder!=null&&u.CurrentOrder.Kind!=PlayableTacticalOrderKind.Attack&&u.CurrentOrder.Kind!=PlayableTacticalOrderKind.Follow&&!navigation.IsPending(u.Id)&&!after.Moving&&(after.Outcome==NavigationOutcome.Arrived||after.Outcome==NavigationOutcome.Unreachable||after.Outcome==NavigationOutcome.Blocked||after.Outcome==NavigationOutcome.Rejected))u.CurrentOrder=null;}AdvanceFollow(false);RefreshVision();RefreshFormationContacts();AdvanceCombat(dt);AdvanceProjectiles(dt);RefreshTacticalQueues();
            AdvanceCapture(dt);AdvanceFoundations();AdvanceBuildings(dt);AdvanceProduction(dt);AdvanceBuildingLifecycle(dt);AdvanceRefineryUpgrades(dt);AdvanceResearch(dt);
            incomeClock+=dt;
            while(incomeClock>=1d-1e-9){incomeClock-=1d;foreach(var owner in Owners)if(!eliminated.Contains(owner))SettleIncome(owner);}
            CheckOutcome();AdvanceRally();RefreshVision();ObserveSoundMotion();if(Outcome!=PlayableMatchOutcome.Playing)FreezeResult();
        }
        private readonly System.Collections.Generic.Dictionary<PlayableOwner,double> settledIncome=new System.Collections.Generic.Dictionary<PlayableOwner,double>();
        private double SettledIncome(PlayableOwner owner)=>settledIncome.TryGetValue(owner,out var total)?total:0;
        private void SettleIncome(PlayableOwner owner)
        {
            var amount=Income(owner)/profile.IncomePeriodSeconds;AddCredits(owner,amount);settledIncome[owner]=SettledIncome(owner)+amount;Fact(owner,MatchFactKind.Income,amount);
            // Tick is persisted; presentation shares the existing profile period, never the credit cadence.
            if(Tick%(30L*profile.IncomePeriodSeconds)!=0)return;
            foreach(var b in buildings.Values.OrderBy(b=>b.Id))if(b.Owner==owner&&b.Ready&&b.Sale==null)
            {
                double displayed=b.Upgrade?.Complete==true?profile.RefineryUpgradedIncome:TerritoryRules.Income(profile,b.Kind);
                if(displayed>0)incomeEvents.Add(new PlayableIncomeEvent(Tick,b.Id,owner,b.Kind,b.Position,displayed));
            }
        }
        // Saved visual facts retain exact restore projections but never re-credit on restore.
        private readonly List<PlayableIncomeEvent> incomeEvents=new List<PlayableIncomeEvent>();
        private PlayableIncomeEvent[] IncomeEvents(PlayableOwner? owner)=>incomeEvents.Where(e=>(!owner.HasValue||e.Owner==owner.Value)&&buildings.TryGetValue(e.BuildingId,out var b)&&b.Owner==e.Owner&&b.Ready&&b.Sale==null&&!eliminated.Contains(e.Owner)).ToArray();
        private double Income(PlayableOwner owner){double amount=0;foreach(var b in buildings.Values)if(b.Owner==owner&&b.Ready&&b.Sale==null)amount+=b.Upgrade?.Complete==true?profile.RefineryUpgradedIncome:TerritoryRules.Income(profile,b.Kind);return amount;}
        private void AdvanceBuildings(double dt)
        {
            foreach(var b in buildings.Values)
            {
                if(eliminated.Contains(b.Owner)||b.Sale!=null||b.Phase==ConstructionPhase.Pending)continue;
                if(!b.Ready)
                {
                    double duration=TerritoryRules.Duration(Terms(b.TermsRevision),b.Kind),before=b.Build;
                    b.Build=Math.Min(duration,b.Build+dt);
                    b.Health=Math.Min(TerritoryRules.Health(profile,b.Kind),b.Health+TerritoryRules.Health(Terms(b.TermsRevision),b.Kind)*(b.Build-before)/duration);
                    if(b.Build>=duration-1e-9){b.Build=duration;double maximum=TerritoryRules.Health(profile,b.Kind);if(Math.Abs(b.Health-maximum)<1e-9)b.Health=maximum;b.Ready=true;b.Phase=ConstructionPhase.Ready;Sound(PlayableSoundKind.ConstructionComplete,b.Position,PlayableEntityKind.Tank,b.Owner,true);RecordBuildingComplete(b);}
                    continue;
                }
            }
        }
        private void SpawnPlayer(NavPoint p,NavPoint rally)=>SpawnProduced(p,rally,PlayableOwner.Player);
        private void SpawnProduced(NavPoint p,NavPoint? rally,PlayableOwner owner,PlayableEntityKind kind=PlayableEntityKind.Tank)
        {
            int id=SpawnUnit(p,owner,kind);
            Fact(owner,MatchFactKind.UnitCompleted,PlayableUnitRules.Cost(profile,kind),(int)kind,kind.ToString());
            // AI reinforcements await their army's observed route admission. An inherited
            // factory rally must not dispatch a fresh singleton across an unsafe front.
            bool armyManaged=offline!=null&&offline.Roster[(int)owner].Control==OfflineControl.Ai&&
                (Spacewars.Simulation.Ai.AiRosterCatalog.Initial.For(kind).lineWeight+Spacewars.Simulation.Ai.AiRosterCatalog.Initial.For(kind).supportWeight>0);
            if(!armyManaged&&rally.HasValue&&Geometry.IsFree(rally.Value,PlayableUnitRules.Radius(profile,kind)))
                Move(new[]{id},rally.Value,false,Math.Max(1,nextProductionSequence-1),owner,out _);
        }
        private int SpawnUnit(NavPoint p,PlayableOwner owner,PlayableEntityKind kind)
        {
            int id=nextId++;units.Add(id,new Unit{Id=id,Owner=owner,Kind=kind,Health=PlayableUnitRules.Health(profile,kind),PreviousPosition=p});
            soundMotion[id]=false;
            navigation.Crowd.Add(id,p,PlayableUnitRules.Radius(profile,kind),PlayableUnitRules.Speed(profile,kind,kind==PlayableEntityKind.Tank&&Chassis(owner)),PlayableUnitRules.Turn(profile,kind)).Team=TeamOf(owner);RecordArmy(owner);return id;
        }
        private void SpawnEnemy(NavPoint p)=>SpawnUnit(p,PlayableOwner.Enemy,PlayableEntityKind.Tank);
        private void InterruptBurst(Unit u){if(u.BurstRemaining>0)u.Reload=profile.ExplorerBurstPauseMs/1000d;u.BurstRemaining=0;u.BurstTarget=0;u.BurstDelay=0;}
        private void AdvanceCombat(double dt)
        {
            foreach(var u in units.Values)
            {
                if(eliminated.Contains(u.Owner)||!navigation.Crowd.TryGet(u.Id,out var self))continue;
                if(u.Kind==PlayableEntityKind.Shkval){AdvanceShkval(u,self,dt);continue;}
                double range=PlayableUnitRules.Range(profile,u.Kind);bool explorer=u.Kind==PlayableEntityKind.Explorer;
                u.BurstDelay=Math.Max(0,u.BurstDelay-dt);u.Reload=Math.Max(0,u.Reload-dt);u.Repath=Math.Max(0,u.Repath-dt);
                u.StoppedSeconds=self.Moving?0:u.StoppedSeconds+dt;
                if(IsFollowing(u)){InterruptBurstIfFollowingCannotFire(u);if(!FollowCanFire(u)){u.Target=0;continue;}int next=FollowDefenseTarget(u,self.Position,range);if(next==0)ClearFollowBurst(u);u.Target=next;}
                // A held unit retains its prior idle defense until async activation. Once
                // ordinary Tank movement activates, that passive target cannot stop it.
                if(!explorer&&!self.Held&&!u.ExplicitTarget&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move){InterruptBurst(u);u.Target=0;}
                if(u.ExplicitTarget&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack&&!VisibleTarget(u.Owner,u.Target)&&TargetAlive(u.Target)){InterruptBurst(u);CombatStop(u.Id);continue;}
                if(u.Target!=0&&(!VisibleTarget(u.Owner,u.Target)||!Target(u.Target,out var seen,out _)||(!u.ExplicitTarget&&Distance(self.Position,seen)>range))){InterruptBurst(u);if((u.ExplicitTarget||!explorer)&&!self.Held)CombatStop(u.Id);if(u.ExplicitTarget&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack)u.CurrentOrder=null;u.Target=0;u.ExplicitTarget=false;if(u.HasAttackMove&&!self.Moving&&CanResumeCombat(u.Id))CombatMove(u.Id,u.AttackMove);}
                if(u.Target==0)
                {
                    // Source idle defense never replaces Tank movement. Explorer retains ordinary moving fire.
                    bool idle=self.Held||u.CurrentOrder==null&&!self.Moving&&!navigation.IsPending(u.Id);
                    if(idle||u.HasAttackMove||explorer&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move){
                        int nearby=NearestDefenseTarget(self.Position,u.Owner,range*(idle?profile.IdleAutoDefenseMultiplier:1));
                        if(Target(nearby,out var nearbyPosition,out _)&&GroundFireClear(self.Position,nearbyPosition,explorer?0:profile.TankProjectileCollisionRadius))u.Target=nearby;
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
                if(!Target(u.Target,out var target,out _))continue;
                double dx=target.X-self.Position.X,dz=target.Z-self.Position.Z,d=Math.Sqrt(dx*dx+dz*dz),desired=Math.Atan2(dz,dx);
                u.Turret=Turn(u.Turret,desired,(explorer?profile.ExplorerTurretTurnSpeed:profile.TankTurretTurnSpeed)*dt);
                // Static walls block line of fire; building footprints do not hide their own targets.
                bool clear=GroundFireClear(self.Position,target,explorer?0:profile.TankProjectileCollisionRadius);
                if(d>range||(!IsFollowing(u)&&!clear))
                {
                    InterruptBurst(u);
                    if(u.HasAttackMove){u.Target=0;if(!self.Moving&&CanResumeCombat(u.Id))CombatMove(u.Id,u.AttackMove);continue;}
                    if(u.ExplicitTarget&&!self.Held&&u.Repath<=0&&!self.Moving&&!navigation.IsPending(u.Id)&&d>0)
                    {
                        if(TryAttackApproach(u.Id,self.Position,target,out var approach))CombatMove(u.Id,approach);
                        u.Repath=profile.AttackRepathSeconds;
                    }
                    continue;
                }
                if(!self.Held&&(!explorer||u.ExplicitTarget||u.HasAttackMove)&&(self.Moving||navigation.IsPending(u.Id)))CombatStop(u.Id);
                if(!explorer&&!profile.TankFiresWhileMoving&&(self.Moving||u.StoppedSeconds<profile.TankPreparationSeconds))continue;
                if(explorer)
                {
                    if(u.BurstRemaining>0&&u.BurstTarget!=u.Target)InterruptBurst(u);
                    if(d>0&&u.Reload<=0&&Math.Abs(Angle(desired-u.Turret))<=profile.ExplorerAimToleranceRad)
                    {
                        if(u.BurstRemaining==0){u.BurstRemaining=u.BurstSize=UnitUpgraded(u)?profile.ExplorerAssaultBurstSize:profile.ExplorerBurstSize;u.BurstSpread=UnitUpgraded(u)?profile.ExplorerAssaultSpreadDeg:profile.ExplorerSpreadDeg;u.BurstTarget=u.Target;u.BurstSequence++;u.BurstDelay=0;}
                        if(u.BurstDelay<=0){FireTracer(u,self.Position,target);u.BurstRemaining--;u.BurstDelay=profile.ExplorerBurstShotIntervalMs/1000d;if(u.BurstRemaining==0)u.Reload=profile.ExplorerBurstPauseMs/1000d;}
                    }
                    continue;
                }
                if(d>0&&u.Reload<=0&&Math.Abs(Angle(desired-u.Turret))<=profile.TankAimToleranceRadians)
                {
                    projectiles.Add(new Projectile{TermsRevision=profile.Revision,Id=nextProjectile++,Owner=u.Id,Faction=u.Owner,Target=u.Target,Position=self.Position,Height=GroundHeight(self.Position)+DirectFireHeight,VerticalSlope=(GroundHeight(target)-GroundHeight(self.Position))/d,Kind=u.Kind,Speed=profile.TankProjectileSpeed,Radius=profile.TankProjectileCollisionRadius,Damage=profile.TankWeaponDamage,Remaining=profile.TankProjectileMaxTravel,DirectionX=dx/d,DirectionZ=dz/d});
                    Sound(PlayableSoundKind.Shot,self.Position,u.Kind,u.Owner);
                    u.Reload=profile.TankWeaponReloadSeconds;
                }
            }
        }
        internal bool TryAttackApproach(int id,NavPoint from,NavPoint target,out NavPoint result)
        {
            result=default(NavPoint);double range=units.TryGetValue(id,out var actor)?PlayableUnitRules.Range(profile,actor.Kind,actor.Kind==PlayableEntityKind.Shkval&&UnitUpgraded(actor)):profile.TankRange;double best=double.PositiveInfinity;
            foreach(var point in PlayableUnitRules.AttackApproachCandidates(profile,from,target,range))
            {
                var candidate=point;
                if(!Geometry.IsFree(candidate,actor==null?profile.TankCollisionRadius:PlayableUnitRules.Radius(profile,actor.Kind))||(actor?.Kind!=PlayableEntityKind.Shkval&&!GroundFireClear(candidate,target,profile.TankProjectileCollisionRadius)))continue;
                var slots=navigation.AllocateArrivalSlots(candidate,new[]{id});if(slots.Length!=1)continue;
                candidate=slots[0];
                if(Distance(candidate,target)>range||(actor?.Kind!=PlayableEntityKind.Shkval&&!GroundFireClear(candidate,target,profile.TankProjectileCollisionRadius)))continue;
                double cost=Distance(from,candidate);if(cost<best){best=cost;result=candidate;}
            }
            return !double.IsPositiveInfinity(best);
        }
        private void AdvanceProjectiles(double dt){for(int i=projectiles.Count-1;i>=0;i--){var p=projectiles[i];if(p.Rocket!=null){if(AdvanceRocket(p,dt))projectiles.RemoveAt(i);continue;}double move=Math.Min(p.Speed*dt,p.Remaining);NavPoint next=new NavPoint(p.Position.X+p.DirectionX*move,p.Position.Z+p.DirectionZ*move);double nextHeight=p.Height+p.VerticalSlope*move;if(!ProjectileClear(p.Position,next,p.Height,nextHeight,p.Radius)){Sound(PlayableSoundKind.Impact,next,p.Kind);projectiles.RemoveAt(i);continue;}int hit=FirstHitAtHeight(p.Faction,p.Position,next,p.Radius,p.Height,nextHeight);if(hit!=0){Target(hit,out var hitPoint,out _);Sound(PlayableSoundKind.Impact,hitPoint,p.Kind,building:buildings.ContainsKey(hit));DamageWithOwner(hit,p.Damage,p.Owner,p.Faction);projectiles.RemoveAt(i);continue;}p.Position=next;p.Height=nextHeight;p.Remaining-=move;if(p.Remaining<=0)projectiles.RemoveAt(i);}}
        private int FirstHit(PlayableOwner faction,NavPoint a,NavPoint b,double projectileRadius)=>FirstHitAtHeight(faction,a,b,projectileRadius,GroundHeight(a)+DirectFireHeight,GroundHeight(b)+DirectFireHeight);
        private int FirstHitAtHeight(PlayableOwner faction,NavPoint a,NavPoint b,double projectileRadius,double fromHeight,double toHeight)
        {
            int hit=0;double best=double.PositiveInfinity;
            foreach(var u in units.Values)
            {
                if(!Hostile(u.Owner,faction)||!navigation.Crowd.TryGet(u.Id,out var n))continue;
                double t=HitTime(n.Position,PlayableUnitRules.Radius(profile,u.Kind),2*PlayableUnitRules.Radius(profile,u.Kind));
                if(t<best){best=t;hit=u.Id;}
            }
            foreach(var building in buildings.Values)
            {
                if(!Hostile(building.Owner,faction)||building.Phase==ConstructionPhase.Pending)continue;
                double t=HitTime(building.Position,BuildingRadius(building.Kind),profile.ShkvalBuildingCollisionHeight);
                if(t<best){best=t;hit=building.Id;}
            }
            return hit;
            double HitTime(NavPoint p,double radius,double height)
            {
                if(profile.AuthoredMap==null)return Intersection(a,b,p,radius+projectileRadius);
                double dx=b.X-a.X,dz=b.Z-a.Z,x=a.X-p.X,z=a.Z-p.Z,r=radius+projectileRadius,aa=dx*dx+dz*dz,bb=2*(x*dx+z*dz),cc=x*x+z*z-r*r;
                double enter=0,exit=1;if(aa<1e-12){if(cc>0)return double.PositiveInfinity;}else{double disc=bb*bb-4*aa*cc;if(disc<0)return double.PositiveInfinity;enter=Math.Max(0,(-bb-Math.Sqrt(disc))/(2*aa));exit=Math.Min(1,(-bb+Math.Sqrt(disc))/(2*aa));}
                double low=GroundHeight(p)-projectileRadius,high=GroundHeight(p)+height+projectileRadius,dy=toHeight-fromHeight;
                if(Math.Abs(dy)<1e-12){if(fromHeight<low||fromHeight>high)return double.PositiveInfinity;}else{double t1=(low-fromHeight)/dy,t2=(high-fromHeight)/dy;enter=Math.Max(enter,Math.Min(t1,t2));exit=Math.Min(exit,Math.Max(t1,t2));}
                return enter<=exit?enter:double.PositiveInfinity;
            }
        }
        private double DirectFireHeight=>profile.AuthoredMap?.DirectFireHeight??1; // Preserves the original flat fixture projectile plane.
        private double GroundHeight(NavPoint p)=>profile.AuthoredMap?.SurfaceHeight(p)??0;
        private Func<NavPoint,double> TerrainHeight=>profile.AuthoredMap==null?null:(Func<NavPoint,double>)GroundHeight;
        private bool GroundFireClear(NavPoint a,NavPoint b,double radius)=>ProjectileClear(a,b,GroundHeight(a)+DirectFireHeight,GroundHeight(b)+DirectFireHeight,radius);
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
        private void DamageFromProjectile(int id,int amount,int attackerId)=>DamageWithOwner(id,amount,attackerId,null);
        private void DamageWithOwner(int id,int amount,int attackerId,PlayableOwner? sourceOwner)
        {
            if(amount<=0)return;
            var creditedOwner=sourceOwner??(units.TryGetValue(attackerId,out var source)?(PlayableOwner?)source.Owner:null);
            if(units.TryGetValue(id,out var u)){u.Health-=amount;if(u.Health<=0){if(navigation.Crowd.TryGet(u.Id,out var destroyed))Sound(PlayableSoundKind.Destroyed,destroyed.Position,u.Kind,u.Owner);RecordKill(creditedOwner,u.Owner,id,false,(int)u.Kind,PlayableUnitRules.Cost(profile,u.Kind));InvalidateSpatialCompletion(id);tacticalQueues.Remove(id);navigation.Remove(id);units.Remove(id);RecordArmy(u.Owner);foreach(var key in centerDamage.Where(pair=>pair.Value.AttackerId==id).Select(pair=>pair.Key).ToArray())centerDamage.Remove(key);}else SoundUnitDamage(u);return;}
            if(!buildings.TryGetValue(id,out var b)||b.Phase==ConstructionPhase.Pending)return;
            b.LastDamageTime=elapsed;b.Repair=null;
            double before=b.Health;b.Health-=amount*(b.Ready?1:profile.ConstructionDamageMultiplier);
            if(attackerId!=0&&(b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost)&&units.TryGetValue(attackerId,out var attacker)&&Hostile(attacker.Owner,b.Owner))
            {int actual=(int)Math.Ceiling(Math.Min(before,before-b.Health));if(actual>0)centerDamage[b.Id+":"+attackerId]=new PlayableCenterDamageSnapshot(b.Id,b.Owner,attackerId,navigation.Generation,Tick,actual);}
            if(b.Health<=0){Sound(PlayableSoundKind.Destroyed,b.Position,PlayableEntityKind.Tank,b.Owner,true);foreach(var key in centerDamage.Where(pair=>pair.Value.CenterId==id).Select(pair=>pair.Key).ToArray())centerDamage.Remove(key);RemoveBuildingWithAttacker(b,false,creditedOwner);RebuildGeometry();}
            else {Sound(PlayableSoundKind.HeavyDamage,b.Position,PlayableEntityKind.Tank,b.Owner,true);Sound(PlayableSoundKind.BaseThreat,b.Position,PlayableEntityKind.Tank,b.Owner,true);}
        }
        private bool Target(int id,out NavPoint p,out int hp){Unit u;if(units.TryGetValue(id,out u)){NavUnit n;if(navigation.Crowd.TryGet(id,out n)){p=n.Position;hp=u.Health;return true;}}Building b;if(buildings.TryGetValue(id,out b)&&b.Phase!=ConstructionPhase.Pending){p=b.Position;hp=(int)Math.Ceiling(b.Health);return true;}p=default(NavPoint);hp=0;return false;}
        private int NearestDefenseTarget(NavPoint point,PlayableOwner observer,double range)
        {
            // Source precedence: visible enemy units before visible buildings, deterministic nearest/ID.
            foreach(var candidates in new[]{units.Keys.OrderBy(id=>id),buildings.Keys.OrderBy(id=>id)}){
                int best=0;double distance=range;
                foreach(int id in candidates)if(VisibleTarget(observer,id)&&Target(id,out var position,out _)){
                    double d=Distance(point,position);if(d<=range&&(best==0||d<distance)){distance=d;best=id;}
                }
                if(best!=0)return best;
            }
            return 0;
        }
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
                var loser=!enemy?PlayableOwner.Enemy:PlayableOwner.Player;
                foreach(var u in units.Values)if(u.Owner==loser){InvalidateSpatialCompletion(u.Id);u.Target=0;u.HasAttackMove=false;navigation.Stop(u.Id,true);}
                var pending=new List<Building>();foreach(var b in buildings.Values)if(b.Owner==loser){b.Orders.Clear();b.RepeatTank=false;if(b.Phase==ConstructionPhase.Pending)pending.Add(b);}
                foreach(var b in pending)RemoveBuilding(b,false);
                foreach(var site in sites.Values)if(site.Claimant==loser&&!site.Locked.HasValue){site.Claimant=null;site.Progress=0;}
            }
        }
        private void AddBuilding(PlayableOwner owner,PlayableBuildingKind kind,NavPoint p,bool ready)
        {
            int siteId=HomeSite(owner);
            var b=new Building{TermsRevision=profile.Revision,Id=nextId++,Owner=owner,Kind=kind,Position=p,SiteId=siteId,PaidCost=TerritoryRules.Cost(profile,kind),Health=TerritoryRules.Health(profile,kind),Ready=ready,Phase=ready?ConstructionPhase.Ready:ConstructionPhase.Constructing};
            buildings.Add(b.Id,b);sites[siteId].CenterId=b.Id;sites[siteId].Locked=owner;sites[siteId].Claimant=owner;sites[siteId].Progress=1;
        }
        private void RebuildGeometry()
        {
            var obstacles=new List<NavObstacle>(StaticObstacles());
            foreach(var b in buildings.Values)if(b.Phase!=ConstructionPhase.Pending){double r=BuildingRadius(b.Kind);obstacles.Add(new NavObstacle(b.Position.X-r,b.Position.Z-r,b.Position.X+r,b.Position.Z+r));}
            Geometry=new NavGeometry(profile.ArenaHalfExtent,obstacles.ToArray(),Geometry.Revision+1);navigation.ChangeGeometry(Geometry);InvalidateTacticalArrivalAfterGeometryChange();
        }
        internal PlayableSnapshot Snapshot(long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,string failure,int seed){var es=new List<PlayableEntitySnapshot>();foreach(var u in units.Values){NavUnit n;if(navigation.Crowd.TryGet(u.Id,out n))es.Add(new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,n.Position,u.Health,n.Moving,u.Target,n.Heading,u.Turret,upgraded:UnitUpgraded(u),held:n.Held,navigationOutcome:n.Outcome));}var bs=new List<PlayableBuildingSnapshot>();foreach(var b in buildings.Values){double duration=TerritoryRules.Duration(Terms(b.TermsRevision),b.Kind);bs.Add(new PlayableBuildingSnapshot(b.Id,b.Owner,b.Kind,b.Position,(int)Math.Ceiling(b.Health),b.Ready?1:b.Build/duration,b.Queue,b.ProductionProgress,b.Rally,b.SiteId,b.SlotId,b.ParentId,b.Phase,b.BlockedReason,b.Heading,orders:ProductionSnapshot(b),repeat:b.RepeatTank,lifecycle:LifecycleSnapshot(b),upgrade:UpgradeSnapshot(b),refineryUpgraded:b.Upgrade?.Complete==true,repeatKind:b.RepeatKind,research:b.Kind==PlayableBuildingKind.ScientificCenter?ResearchSnapshot(b.Owner):null,hasRally:b.HasRally,pendingRally:PendingRally(b.Id)));}var ps=new List<PlayableProjectileSnapshot>();foreach(var p in projectiles){double height=p.Height,pitch=Math.Atan(p.VerticalSlope),heading=Math.Atan2(p.DirectionZ,p.DirectionX);if(p.Rocket!=null){double progress=p.Rocket.Elapsed/p.Rocket.Flight.Duration;var point=p.Rocket.Flight.Point(progress);var tangent=p.Rocket.Flight.Tangent(progress);height=point.Y;pitch=Math.Atan2(tangent.Y,Math.Sqrt(tangent.X*tangent.X+tangent.Z*tangent.Z));heading=Math.Atan2(tangent.Z,tangent.X);}ps.Add(new PlayableProjectileSnapshot(p.Id,p.Owner,p.Target,p.Position,p.Kind,heading,height:height,pitch:pitch,faction:p.Faction));}return new PlayableSnapshot(profile.ProfileId,profile.Revision,navigation.Generation,seed,sequence,Tick,status,paused,Outcome,(int)Math.Floor(Balance(PlayableOwner.Player)),Geometry,es.ToArray(),bs.ToArray(),ps.ToArray(),metrics,failure,SiteSnapshots(),Income(PlayableOwner.Player)/profile.IncomePeriodSeconds,population:Population(PlayableOwner.Player),researchAvailability:ResearchAvailability(PlayableOwner.Player),ownerResearch:ResearchSnapshot(PlayableOwner.Player),participants:offline?.Roster.ToArray(),activeProfile:profile,discoveredSites:sites.Values.Select(s=>s.Site).ToArray(),sounds:Sounds(),incomeEvents:IncomeEvents(null));}
    }
}
