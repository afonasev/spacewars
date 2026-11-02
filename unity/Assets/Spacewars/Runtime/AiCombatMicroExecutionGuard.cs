using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Runtime
{
    internal sealed partial class AiCombatMicroPolicy
    {
        // Minted only from the applied client/certificate, never supplied by Nav.
        internal sealed class ExecutionDescriptor:IDisposable
        {
            readonly AiCombatMicroExecutionStorage.Lease memory;internal bool Disposed{get;private set;}
            public void Dispose(){if(Disposed)return;Disposed=true;memory.Dispose();}
            internal readonly int Entity,Tower,Target;internal readonly long Army,Started,Sequence,Action,Generation;
            internal readonly AiCombatMicroPhase Phase;internal readonly string Identity;
            internal ExecutionDescriptor(int entity,int tower,int target,long army,long started,long sequence,long action,long generation,AiCombatMicroPhase phase,string identity,AiCombatMicroExecutionStorage.Lease memory)
            {this.memory=memory;Entity=entity;Tower=tower;Target=target;Army=army;Started=started;Sequence=sequence;Action=action;Generation=generation;Phase=phase;Identity=identity;}
            internal bool Withdrawal=>Phase==AiCombatMicroPhase.Withdraw||Phase==AiCombatMicroPhase.Repair;
        }
        // Retirement tests custody identity only. Health thresholds, live core
        // fitness and geometry eligibility belong to future authorization and
        // must not erase the history of an otherwise unchanged applied job.
        internal bool RetainsExecutionJob(int entity,long generation,string identity,string ownerId,PlayableOwner owner,PlayableOrderStamp stamp)
        {
            if(!clients.TryGetValue(entity,out var client)||!certificates.TryGetValue(entity,out var certificate)||client.Sequence<=0||certificate.Unit!=entity||certificate.Owner!=ownerId||certificate.Generation!=generation||certificate.Army!=client.Army||certificate.Sequence!=client.Sequence||certificate.ActionId!=client.ActionId||certificate.Source!=PlayableAiOpeningComposition.SourceIdentity||stamp==null||stamp.UnitId!=entity||stamp.Owner!=owner||stamp.Generation!=generation||stamp.Origin!=PlayableOrderOrigin.Ai||stamp.Sequence!=certificate.Sequence||stamp.ActionId!=certificate.ActionId||stamp.JobId!=certificate.ActionId||stamp.Source!=certificate.Source||stamp.Tick!=certificate.Tick||stamp.Kind!=certificate.Kind)return false;
            const string prefix="micro-execution:";if(identity==null||!identity.StartsWith(prefix,StringComparison.Ordinal))return false;int at=prefix.Length;
            return IdentityNumber(identity,ref at,generation)&&IdentityNumber(identity,ref at,entity)&&IdentityNumber(identity,ref at,client.Sequence)&&IdentityNumber(identity,ref at,client.ActionId)&&IdentityNumber(identity,ref at,client.Started)&&IdentityNumber(identity,ref at,(int)client.Phase,true);
        }
        static bool IdentityNumber(string value,ref int at,long expected,bool last=false)
        {
            int start=at;long number=0;while(at<value.Length&&value[at]>='0'&&value[at]<='9'){int digit=value[at++]-'0';if(number>(long.MaxValue-digit)/10)return false;number=number*10+digit;}
            if(at==start||at-start>1&&value[start]=='0'||number!=expected)return false;if(last)return at==value.Length;return at<value.Length&&value[at++]==':';
        }
        internal bool TryDescribeExecution(PlayableDomain domain,string owner,PlayableProfile p,AiProfile ai,AiDifficulty difficulty,AiArmyRegistry registry,int entity,AiCombatMicroExecutionStorage storage,ref long grant,out ExecutionDescriptor descriptor)
        {
            descriptor=null;
            if(!clients.TryGetValue(entity,out var client)||!certificates.TryGetValue(entity,out var certificate)||client.Sequence<=0||certificate.Sequence!=client.Sequence||certificate.ActionId!=client.ActionId||certificate.Army!=client.Army)return false;
            if(grant<64||!storage.TryReserve(8192,out var memory))return false;grant-=64;
            try{
            var identity=string.Join(":",new[]{"micro-execution",certificate.Generation.ToString(CultureInfo.InvariantCulture),entity.ToString(CultureInfo.InvariantCulture),client.Sequence.ToString(CultureInfo.InvariantCulture),client.ActionId.ToString(CultureInfo.InvariantCulture),client.Started.ToString(CultureInfo.InvariantCulture),((int)client.Phase).ToString(CultureInfo.InvariantCulture)});
            var value=new ExecutionDescriptor(entity,client.Tower,certificate.Target,client.Army,client.Started,client.Sequence,client.ActionId,certificate.Generation,client.Phase,identity,memory);
            if(!ExecutionCurrent(domain,owner,p,ai,difficulty,registry,value,null,false,storage,ref grant,out _,out _)){value.Dispose();return false;}descriptor=value;return true;
            }catch{memory.Dispose();throw;}
        }
        // All scans are explicitly paid before reading live scalars. This method
        // does no Covers/terrain/model work and never calls PlayerSnapshot.
        internal bool ExecutionCurrent(PlayableDomain domain,string owner,PlayableProfile p,AiProfile ai,AiDifficulty difficulty,AiArmyRegistry registry,ExecutionDescriptor d,MotionExecutionPrimitive primitive,bool committed,AiCombatMicroExecutionStorage storage,ref long grant,out PlayableEntitySnapshot patient,out bool admitted)
        {
            admitted=false;patient=null;int members=0,reinforcements=0;
            bool groupFound=registry.TryReadGuard(d.Army,out var group);if(groupFound){members=group.MemberCount;reinforcements=group.ReinforcementCount;}
            long cost=checked(128L+(primitive==null?0:PlayableDomain.CombatExecutionNativeGroupReadWork(domain.CombatExecutionUnitCount))+domain.CombatExecutionUnitCount*(16L+members+reinforcements)+domain.CombatExecutionBuildingCount*4L+2L*clients.Count+(long)members*(8L+members+reinforcements)+fire.Count*(16L+2L*(primitive?.RemainingBranch?.Count??0)));
            if(grant<cost||!storage.TryReserve(checked(8192L+2048L*(domain.CombatExecutionUnitCount+(long)fire.Count)),out var scratch))return false;grant-=cost;admitted=true;using(scratch){
            if(d.Disposed||!domain.Authorizes(owner)||domain.Navigation.Generation!=d.Generation||!clients.TryGetValue(d.Entity,out var client)||!certificates.TryGetValue(d.Entity,out var certificate)||client.Sequence!=d.Sequence||client.ActionId!=d.Action||client.Army!=d.Army||client.Tower!=d.Tower||client.Started!=d.Started||client.Phase!=d.Phase||domain.Tick<client.Started||domain.Tick-client.Started>T(ai,difficulty,"maxPhaseSeconds"))return false;
            patient=domain.CombatExecutionActor(d.Entity);var own=domain.OwnerFor(owner);
            if(patient==null||patient.Owner!=own||!Eligible(patient)||domain.CombatExecutionQueueBusy(d.Entity)||!Matches(certificate,owner,own,d.Generation,domain.Tick,patient))return false;
            if(primitive!=null){var b=primitive.Binding;if(b.World!=d.Generation||b.Owner!=owner||b.Entity!=d.Entity||b.OwnerPolicy!=d.Identity||b.Command!=d.Sequence||!domain.CombatExecutionBindingCurrent(primitive)||primitive.OwnerTick>domain.Tick||!MotionExecutionBinding.Same(patient.HullHeading,committed?primitive.HeadingAfter:primitive.HeadingBefore)||(primitive.Air.HasValue?patient.Kind!=PlayableEntityKind.Hawk||!MotionExecutionBinding.Same(patient.Position.X,(committed?primitive.To:primitive.From).Position.X)||!MotionExecutionBinding.Same(patient.Position.Z,(committed?primitive.To:primitive.From).Position.Z)||!MotionExecutionBinding.Same(patient.Altitude,committed?primitive.Air.Value.AssignedHeight:primitive.Air.Value.BeforeHeight):patient.Kind==PlayableEntityKind.Hawk||!MotionExecutionBinding.Same(patient.Location??new NavLocation(patient.Position,primitive.From.SurfaceId),committed?primitive.To:primitive.From)))return false;}
            // AppliedRepair intentionally survives loss of its former army; all
            // other phases need independently current major membership.
            if(d.Phase!=AiCombatMicroPhase.Repair&&(!groupFound||group.OwnerId!=owner||!group.Major||group.Phase==AiArmyPhase.Disbanded||!group.ContainsMember(d.Entity)||group.ContainsReinforcement(d.Entity)))return false;
            if(d.Phase==AiCombatMicroPhase.Return){
                if(!groupFound||!ReturnGroup(owner,d.Entity,group))return false;
                var anchor=domain.CombatExecutionActor(d.Target);if(anchor==null||!ReturnCoreCandidate(own,d.Entity,p,ai,difficulty,group,anchor))return false;
                int neighbors=0;foreach(var candidate in domain.CombatExecutionActors(own))if(ReturnCoreCandidate(own,d.Entity,p,ai,difficulty,group,candidate)&&ReturnCoreNeighbor(ai,difficulty,anchor.Position,candidate.Position))neighbors++;
                if(neighbors<V(ai,difficulty,"returnCoreUnits"))return false;
            }
            if(d.Phase==AiCombatMicroPhase.Repair){
                if(!domain.CombatExecutionRepairReady(d.Tower,own,d.Entity))return false;
                foreach(var pair in clients)if(pair.Key!=d.Entity&&pair.Value.Phase==AiCombatMicroPhase.Repair&&pair.Value.Tower==d.Tower)return false;
                var tower=domain.CombatExecutionBuildingPoint(d.Tower);double range=p.TurretRange;
                foreach(var other in domain.CombatExecutionActors(own))if(other.Id!=d.Entity&&other.Health>0&&PlayableUnitRules.HealthRatio(p,other)<1&&Distance(other.Position,tower)<=range)return false;
                if(primitive!=null){
                    var branch=primitive.RemainingBranch;if(!primitive.Air.HasValue&&branch==null||!primitive.AssignedEndpoint.HasValue||Distance(primitive.AssignedEndpoint.Value.Position,tower)>range)return false;
                    double length=primitive.Air.HasValue?Distance(primitive.From.Position,primitive.AssignedEndpoint.Value.Position):0;for(int i=0;i<(branch?.Count??0);i++)length+=Distance(branch.Leg(i).From.Position,branch.Leg(i).To.Position);
                    if(!RepairTimeAllowed(p,ai,difficulty,patient,length)||RepairSeconds(p,ai,difficulty,patient,length)>(T(ai,difficulty,"maxPhaseSeconds")-(domain.Tick-d.Started))/30d)return false;
                    if(samples.TryGetValue(d.Entity,out var sample)&&sample.Loss>0&&sample.DamageTick>=0&&domain.Tick-sample.DamageTick<=T(ai,difficulty,"safeSeconds")){
                        var currentPatient=patient;
                        IEnumerable<PlayableEntitySnapshot> Attackers(){foreach(var f in fire){if(f.Victim!=d.Entity)continue;var enemy=domain.CombatExecutionVisibleActor(f.Source,own);if(enemy!=null&&enemy.Health>0&&AiRosterCatalog.Initial.Targets(enemy.Kind,currentPatient.Kind==PlayableEntityKind.Hawk?"air":"ground")&&Distance(enemy.Position,currentPatient.Position)<=PlayableUnitRules.Range(p,enemy.Kind,enemy.Upgraded)+PlayableUnitRules.Radius(p,currentPatient.Kind)+PlayableUnitRules.Radius(p,enemy.Kind))yield return enemy;}}
                        IEnumerable<NavPoint> Ends(){if(primitive.Air.HasValue){yield return primitive.To.Position;yield return primitive.AssignedEndpoint.Value.Position;}else for(int i=0;i<branch.Count;i++)yield return branch.Leg(i).To.Position;}
                        if(!RepairEscapeAllowed(p,patient,sample.Loss,Attackers(),Ends()))return false;
                    }
                }
            }
            return d.Phase==AiCombatMicroPhase.Withdraw||d.Phase==AiCombatMicroPhase.Repair||d.Phase==AiCombatMicroPhase.Return;
            }
        }
    }
    internal sealed partial class PlayableDomain
    {
        internal bool TryReadCombatExecutionJobStamp(int id,PlayableOwner owner,out PlayableOrderStamp stamp)
        {stamp=null;if(!units.TryGetValue(id,out var unit)||unit.Health<=0||unit.Owner!=owner||!TryActor(id,out _))return false;stamp=unit.LastOrder;return true;}
        internal bool CombatExecutionHostile(PlayableOwner other,string owner)=>Hostile(other,OwnerFor(owner));
        internal int CombatExecutionUnitCount=>units.Count;
        internal int CombatExecutionBuildingCount=>buildings.Count;
        static readonly long CombatNativeGroupHeaderWords=64+NavStorageLayout.ObjectBytesBound(typeof(GroupOrderState))/8;
        static readonly long CombatNativeGroupMemberWords=8+NavStorageLayout.ObjectBytesBound(typeof(GroupMemberState))/8;
        internal static long CombatExecutionNativeGroupReadWork(int units)=>checked(CombatNativeGroupHeaderWords+units*CombatNativeGroupMemberWords);
        internal bool CombatExecutionBindingCurrent(MotionExecutionPrimitive primitive)
        {
            var b=primitive.Binding;if(!TryActor(checked((int)b.Entity),out var n)||n.Incarnation!=b.Incarnation||n.MobilityRevision!=b.Mobility||n.Held)return false;
            if(!b.HasOwnIntent)return false;
            if(b.OwnIntent==MotionOwnIntentKind.Air)return primitive.Air.HasValue&&units.TryGetValue(checked((int)b.Entity),out var aircraft)&&aircraft.Air!=null&&aircraft.LastOrder!=null&&aircraft.LastOrder.Revision==b.Order&&aircraft.LastOrder.Sequence==b.Command;
            if(b.OwnIntent==MotionOwnIntentKind.Grouped){var group=navigation.GroupFor(checked((int)b.Entity));if(group==null||group.WorldGeneration!=b.World||group.GroupId!=b.Group||group.OrderRevision!=b.RootOrder||group.MembershipRevision!=b.Membership||group.CommandSequence!=b.Command)return false;}
            if(tacticalQueues.TryGetValue(checked((int)b.Entity),out var q)&&q.Pending!=null){var e=q.Pending;return e.Issuance.GroupId==b.Group&&e.Incarnation==b.Incarnation&&e.Stamp.Revision==b.Activation&&e.AttemptRequest==b.Request&&e.AttemptOrder==b.Order&&e.Issuance.Command.Sequence==b.Command;}
            // Follow and continuation/repath requests use the ordinary Native
            // lane without a new tactical queue entry. Authenticate the actual
            // accepted request before it becomes installed; never infer it from
            // endpoint equality or from the last installed withdrawal route.
            // Native intentionally validates an unchanged member independently
            // of unrelated group membership changes. Current aggregate membership
            // is checked above; the accepted member keeps its own activation.
            var pending=navigation.PendingRequestFor(checked((int)b.Entity));var member=pending?.MemberIdentity;
            if(pending!=null&&member!=null&&pending.Session==b.World&&pending.Entity==b.Entity&&pending.Request==b.Request&&pending.Order==b.Order&&member.GroupId==b.Group&&member.OrderRevision==b.Activation&&member.Incarnation==b.Incarnation&&member.MobilityRevision==b.Mobility)return true;
            return n.InstalledGroupId==b.Group&&n.InstalledRevision==b.Activation&&n.InstalledRequest==b.Request&&n.InstalledOrder==b.Order;
        }
        internal bool CombatExecutionQueueBusy(int id)=>tacticalQueues.TryGetValue(id,out var q)&&q.Deferred.Count!=0||units.TryGetValue(id,out var unit)&&unit.Air!=null&&unit.Air.Deferred.Count!=0;
        // Targeted scalar projection: no queue viability solver, route projection,
        // renderer, private economy state or full PlayerSnapshot is materialized.
        internal PlayableEntitySnapshot CombatExecutionActor(int id)
        {
            if(!units.TryGetValue(id,out var u)||!TryActor(id,out var n))return null;
            PlayableUnitQueueSnapshot queue=null;if(tacticalQueues.TryGetValue(id,out var q)&&q.Pending!=null){var e=q.Pending;var i=e.Issuance;var pending=new PlayableQueuedOrderSnapshot(i.Id,i.GroupId,i.Command.Sequence,e.Stamp.Revision,i.Command.Kind,i.Command.TargetLocation,i.Command.TargetId,i.Command.SiteId,i.Command.Origin==PlayableOrderOrigin.Ai&&i.Command.Source==PlayableAiOpeningComposition.SourceIdentity?i.Command.ActionId:0);queue=new PlayableUnitQueueSnapshot(null,pending,Array.Empty<PlayableQueuedOrderSnapshot>(),null,false);}
            return new PlayableEntitySnapshot(id,u.Owner,u.Kind,n.Position,(int)Math.Ceiling(u.Health),n.Moving,u.Target,n.Heading,u.Turret,u.CurrentOrder,UnitUpgraded(u),n.Held,n.Outcome,u.LastOrder,location:n.Location.Detached(),queue:queue,maxHealth:UnitMaximumHealth(u.Owner,u.Kind),exactHealth:u.Health,altitude:u.Air?.Height??0,departing:u.Air?.Departing??false);
        }
        internal IEnumerable<PlayableEntitySnapshot> CombatExecutionActors(PlayableOwner owner){foreach(var u in units.Values)if(u.Owner==owner){var snapshot=CombatExecutionActor(u.Id);if(snapshot!=null)yield return snapshot;}}
        internal PlayableEntitySnapshot CombatExecutionVisibleActor(int id,PlayableOwner observer)=>units.TryGetValue(id,out var u)&&Hostile(u.Owner,observer)&&VisibleTarget(observer,id)?CombatExecutionActor(id):null;
        internal bool CombatExecutionRepairReady(int id,PlayableOwner owner,int patient)=>buildings.TryGetValue(id,out var b)&&b.Owner==owner&&b.Health>0&&b.Kind==PlayableBuildingKind.RepairTower&&b.Phase==ConstructionPhase.Ready&&b.Sale==null&&(b.Healing==null||b.Healing.Target==0||b.Healing.Target==patient);
        internal PlayableAiObservation CombatExecutionInput(string ownerId,int seed,PlayableEntitySnapshot patient)
        {
            var owner=OwnerFor(ownerId);var visible=Vision(owner);var rows=new List<PlayableBuildingSnapshot>(buildings.Count);
            foreach(var b in buildings.Values){if(b.Owner!=owner&&(Hostile(b.Owner,owner)&&b.Phase==ConstructionPhase.Pending||!visible.IsVisible(b.Position)))continue;
                rows.Add(new PlayableBuildingSnapshot(b.Id,b.Owner,b.Kind,b.Position,(int)Math.Ceiling(b.Health),b.Ready?1:b.Build/TerritoryRules.Duration(Terms(b.TermsRevision),b.Kind),0,0,default,phase:b.Phase,heading:b.Heading,includePrivateState:false,refineryUpgraded:b.Upgrade?.Complete==true,scientificCenterUpgraded:CenterUpgraded(b.Id),producerUpgraded:b.ProducerUpgrade?.Complete==true));}
            return PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,navigation.Generation,seed,Tick,Tick,RuntimeStatus.Running,false,Outcome,0,null,new[]{patient},rows.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,owner:owner,ownerId:ownerId,team:TeamOf(owner),participants:offline?.Roster.ToArray()));
        }
        internal bool CombatExecutionSceneCurrent(string ownerId,AiCombatMicroCoverScene scene)
        {
            var owner=OwnerFor(ownerId);var visible=Vision(owner);int count=0;
            foreach(var b in buildings.Values){if(b.Owner!=owner&&(Hostile(b.Owner,owner)&&b.Phase==ConstructionPhase.Pending||!visible.IsVisible(b.Position)))continue;
                if(count==scene.BuildingCount)return false;var row=scene.At(count++);double progress=b.Ready?1:b.Build/TerritoryRules.Duration(Terms(b.TermsRevision),b.Kind);
                bool upgraded=b.Kind==PlayableBuildingKind.Refinery?b.Upgrade?.Complete==true:b.Kind==PlayableBuildingKind.ScientificCenter?CenterUpgraded(b.Id):(b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.Hangar)&&b.ProducerUpgrade?.Complete==true;
                if(row.Id!=b.Id||row.Kind!=b.Kind||(row.Health>0)!=(b.Health>0)||row.Phase!=b.Phase||!MotionExecutionBinding.Same(row.Position.X,b.Position.X)||!MotionExecutionBinding.Same(row.Position.Z,b.Position.Z)||!MotionExecutionBinding.Same(row.Heading,b.Heading)||!MotionExecutionBinding.Same(row.Progress,progress)||row.Upgraded!=upgraded)return false;}
            return count==scene.BuildingCount;
        }
        internal NavPoint CombatExecutionBuildingPoint(int id)=>buildings.TryGetValue(id,out var b)?b.Position:throw new ArgumentException("Missing repair tower.");
    }
}
