using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public enum GroupMemberPhase { PendingRoute, March, Arrived, Unreachable, Blocked, Stopped, Held, Combat, Detached, Dead }
    // Serializable authority records. Public observations/checkpoints are detached copies.
    [Serializable] public sealed class GroupMemberState
    {
        public int Entity, Slot; public long Incarnation, OrderRevision, MobilityRevision;
        public GroupMemberPhase Phase; public NavPoint Goal; public NavLocation? GoalLocation; public bool HasGoal, FormationReleased;
        public long Branch, Reservation, ContactTick, ProgressRequest; public string ContactPhase; public double Progress;
        internal GroupMemberState Copy()=>(GroupMemberState)MemberwiseClone();
    }
    [Serializable] public sealed class GroupOrderState
    {
        public long WorldGeneration, GroupId, OrderRevision, CommandSequence, IssuedTick, MembershipRevision;
        // Version zero is an explicit pre-march checkpoint. New issuances bind
        // their current Balance Lab revision and retain a geometric anchor.
        public int MarchVersion, MarchProfileRevision; public double MarchStretch, MarchAnchor;
        public bool MarchInitialized;
        public string OwnerId, Source; public int TeamId, TargetId;
        public PlayableOrderOrigin Origin; public PlayableCommandKind Kind; public long JobId, ActionId;
        public NavPoint OriginalGoal; public NavLocation? OriginalLocation; public GroupMemberState[] Members;
        internal GroupOrderState Copy(){var copy=(GroupOrderState)MemberwiseClone();copy.Members=Members.Select(m=>m.Copy()).ToArray();return copy;}
    }
    // Immutable application identity. Membership changes do not cancel unaffected member connectors.
    public sealed class NavigationMemberIdentity
    {
        public NavigationMemberIdentity(long incarnation,long mobility,long group=0,long order=0,long membership=0)
        {Incarnation=incarnation;MobilityRevision=mobility;GroupId=group;OrderRevision=order;MembershipRevision=membership;}
        public long Incarnation{get;} public long MobilityRevision{get;} public long GroupId{get;} public long OrderRevision{get;} public long MembershipRevision{get;}
    }
    public sealed partial class NavigationSession
    {
        private long groupSequence;
        private readonly AuthoritySlotMap<long,GroupOrderState> groups=new AuthoritySlotMap<long,GroupOrderState>();
        private readonly AuthoritySlotMap<int,long> memberGroups=new AuthoritySlotMap<int,long>();
        private GroupOrderState commandGroup;
        private long commandActivationRevision;
        private volatile NavigationAdmission admission;
        public NavigationAdmission Admission => admission;
        private bool AdmissionMatchesCurrent()
        {
            var previous=admission;
            if(previous==null||previous.PublishedThroughRequestSequence!=requestSequence||
                !ReferenceEquals(previous.Geometry,navigationGeometry)||!ReferenceEquals(previous.Profile,profile)||
                previous.Requests.Count!=pending.Count+probes.Count||previous.Groups.Count!=groups.Count)return false;
            foreach(var request in previous.Requests){
                var map=request.Entity<0?probes:pending;
                if(!map.TryGetValue(request.Entity,out var active)||!ReferenceEquals(active,request))return false;
            }
            foreach(var snapshot in previous.Groups){
                if(!groups.TryGetValue(snapshot.GroupId,out var group)||
                    snapshot.RootOrderRevision!=group.OrderRevision||snapshot.CommandSequence!=group.CommandSequence||
                    snapshot.IssuedTick!=group.IssuedTick||snapshot.MembershipRevision!=group.MembershipRevision||
                    snapshot.OwnerId!=group.OwnerId||snapshot.TeamId!=group.TeamId||snapshot.TargetId!=group.TargetId||
                    snapshot.Source!=group.Source||snapshot.JobId!=group.JobId||snapshot.ActionId!=group.ActionId||
                    snapshot.Origin!=group.Origin||snapshot.Kind!=group.Kind||
                    !snapshot.OriginalGoal.Equals(group.OriginalGoal)||!Nullable.Equals(snapshot.OriginalTerminal,group.OriginalLocation)||
                    snapshot.Members.Count!=group.Members.Length)return false;
                for(int i=0;i<group.Members.Length;i++){
                    var member=group.Members[i];var saved=snapshot.Members[i];
                    if(saved.Entity!=member.Entity||saved.Slot!=member.Slot||saved.Incarnation!=member.Incarnation||
                        saved.ActivationRevision!=member.OrderRevision||saved.MobilityRevision!=member.MobilityRevision||
                        saved.Phase!=member.Phase||saved.FormationReleased!=member.FormationReleased||
                        saved.HasGoal!=member.HasGoal||!Nullable.Equals(saved.AssignedEndpoint,member.GoalLocation)||
                        !ReferenceEquals(saved.PendingRequest,pending.TryGetValue(member.Entity,out var active)?active:null))return false;
                }
            }
            return true;
        }
        private void PublishAdmission()
        {
            if(commandGroup!=null||admissionBatchDepth!=0)return; // Publish complete accepted batches only.
            RefreshGroupMembers();
            if(AdmissionMatchesCurrent())return;
            var requests=pending.Values.Concat(probes.Values).OrderBy(r=>r.Request).ToArray();
            var snapshot=new NavigationAdmission(Generation,requestSequence,terrain,terrain?.Id??NavLocation.FlatSurface,
                terrain?.SurfaceSemanticsVersion??1,navigationGeometry,profile,
                groups.Values.OrderBy(g=>g.GroupId).Select(g=>new NavigationAdmissionGroup(g,
                    id=>pending.TryGetValue(id,out var request)?request:null)).ToArray(),requests);
            admission=snapshot;
        }
        public IReadOnlyList<GroupOrderState> GroupOrders=>groups.Values.OrderBy(g=>g.GroupId).Select(g=>g.Copy()).ToArray();
        internal bool HasGroupMember(int entity)=>memberGroups.ContainsKey(entity);
        public GroupOrderState GroupFor(int entity)=>memberGroups.TryGetValue(entity,out var id)?groups[id].Copy():null;
        internal void PrepareCommandGroup(PlayableCommand command,long revision,long tick,int team,long groupId=0,long issuanceRevision=0,long issuedTick=-1)
        {
            commandActivationRevision=revision;
            if(commandGroup!=null)throw new InvalidOperationException("Nested command group.");
            commandGroup=new GroupOrderState{WorldGeneration=Generation,GroupId=groupId==0?checked(groupSequence+1):groupId,OrderRevision=issuanceRevision==0?revision:issuanceRevision,CommandSequence=command.Sequence,IssuedTick=issuedTick<0?tick:issuedTick,
                OwnerId=command.PlayerId,TeamId=team,Origin=command.Origin,Kind=command.Kind,Source=command.Source,JobId=command.JobId,ActionId=command.ActionId,
                OriginalGoal=command.Target,OriginalLocation=command.TargetLocation,TargetId=command.TargetId,MembershipRevision=1};
        }
        internal void FinishCommandGroup(bool applied,IEnumerable<int> ids)
        {
            AdmissionBatch(()=>FinishCommandGroupLocked(applied,ids));
        }
        private void FinishCommandGroupLocked(bool applied,IEnumerable<int> ids)
        {
            var next=commandGroup;commandGroup=null;if(!applied){commandActivationRevision=0;return;}
            BindGroup(next,ids);
            PublishAdmission();
        }
        private void BindGroup(GroupOrderState next,IEnumerable<int> ids)
        {
            var members=new List<GroupMemberState>();
            foreach(int id in ids.Distinct().OrderBy(id=>id)){
                if(!Crowd.TryGet(id,out var actor))throw new InvalidOperationException("Accepted group member is absent.");
                DetachMember(id);
                retainedGoals.TryGetValue(id,out var goal);
                members.Add(new GroupMemberState{Entity=id,Incarnation=actor.Incarnation,OrderRevision=commandActivationRevision==0?next.OrderRevision:commandActivationRevision,MobilityRevision=actor.MobilityRevision,
                    Slot=id,Goal=goal,GoalLocation=retainedGoals.ContainsKey(id)&&Crowd.Locate(goal,actor.Radius,out var location)?(NavLocation?)location:null,HasGoal=retainedGoals.ContainsKey(id),Reservation=reservations.ContainsKey(id)?id:0,
                    Phase=actor.Held?GroupMemberPhase.Held:IsPending(id)?GroupMemberPhase.PendingRoute:actor.Moving?GroupMemberPhase.March:GroupMemberPhase.Stopped});
            }
            if(members.Count==0)return;
            if(groups.TryGetValue(next.GroupId,out var existing)){
                if(existing.CommandSequence!=next.CommandSequence||existing.OrderRevision!=next.OrderRevision)throw new InvalidOperationException("Issuance collision.");
                next.MarchVersion=existing.MarchVersion;next.MarchProfileRevision=existing.MarchProfileRevision;
                next.MarchStretch=existing.MarchStretch;next.MarchAnchor=0;next.MarchInitialized=false;
                int nextSlot=existing.Members.Max(m=>m.Slot)+1;
                foreach(var member in SpatialSlotOrder(members,next.OriginalGoal))member.Slot=nextSlot++;
                next.Members=existing.Members.Concat(members).OrderBy(m=>m.Entity).ToArray();next.MembershipRevision=existing.MembershipRevision+1;groups[next.GroupId]=next;
            }else{
                if(marchConfigured){next.MarchVersion=1;next.MarchProfileRevision=marchProfileRevision;next.MarchStretch=marchStretch;}
                int slot=0;foreach(var member in SpatialSlotOrder(members,next.OriginalGoal))member.Slot=slot++;
                next.Members=members.ToArray();groups.Add(next.GroupId,next);
            }
            groupSequence=Math.Max(groupSequence,next.GroupId);commandActivationRevision=0;
            foreach(var member in members)memberGroups.Add(member.Entity,next.GroupId);
            PublishAdmission();
        }
        private IEnumerable<GroupMemberState> SpatialSlotOrder(IEnumerable<GroupMemberState> members,NavPoint goal)
        {
            var rows=members.Select(m=>new { Member=m, Position=Crowd.TryGet(m.Entity,out var actor)?actor.Position:default(NavPoint) }).ToArray();
            double cx=rows.Average(x=>x.Position.X),cz=rows.Average(x=>x.Position.Z);
            double dx=goal.X-cx,dz=goal.Z-cz,length=Math.Sqrt(dx*dx+dz*dz);
            if(length==0)return rows.OrderBy(x=>x.Member.Entity).Select(x=>x.Member);
            // Sorting once by the transverse coordinate minimizes displacement
            // to neutral relative lanes; identity stays stable across ticks.
            return rows.OrderBy(x=>((x.Position.X-cx)*(-dz)+(x.Position.Z-cz)*dx)/length)
                .ThenBy(x=>x.Member.Entity).Select(x=>x.Member);
        }
        private void BindLegacyGroup(IEnumerable<int> ids,NavPoint goal,bool restoredLegacy=false)
        {
            long id=checked(groupSequence+1);
            BindGroup(new GroupOrderState{WorldGeneration=Generation,GroupId=id,OrderRevision=id,MembershipRevision=1,Kind=PlayableCommandKind.Move,
                Origin=PlayableOrderOrigin.Unknown,OriginalGoal=goal},ids);
            if(restoredLegacy&&groups.TryGetValue(id,out var group)){
                group.MarchVersion=0;group.MarchProfileRevision=0;group.MarchStretch=0;
                group.MarchAnchor=0;group.MarchInitialized=false;
            }
        }
        private void DetachMember(int id)
        {
            if(!memberGroups.TryGetValue(id,out var groupId))return;
            DetachInstalledExecution(id);
            var group=groups[groupId];group.Members=group.Members.Where(m=>m.Entity!=id).ToArray();group.MembershipRevision++;
            group.MarchAnchor=0;group.MarchInitialized=false;
            memberGroups.Remove(id);if(group.Members.Length==0)groups.Remove(groupId);
        }
        private static void RestoreGroupMap<TKey,TValue>(AuthoritySlotMap<TKey,TValue> map,TKey[] keys,int[] layout)
        {
            if(keys==null||keys.Length!=map.Count||keys.Distinct().Count()!=keys.Length||keys.Any(key=>!map.ContainsKey(key)))throw new ArgumentException("Invalid group map row order.");
            var rows=keys.Select(key=>new KeyValuePair<TKey,TValue>(key,map[key])).ToArray();map.Clear();foreach(var row in rows)map.Add(row.Key,row.Value);map.RestoreLayout(layout);
        }
        private GroupMemberState Member(int id)
        {return memberGroups.TryGetValue(id,out var groupId)?groups[groupId].Members.First(m=>m.Entity==id):null;}
        private void ResetMarchProgress(int id)
        {
            var member=Member(id);if(member==null||groups[memberGroups[id]].MarchVersion!=1)return;
            member.Progress=0;member.ProgressRequest=0;
            var group=groups[memberGroups[id]];group.MarchAnchor=0;group.MarchInitialized=false;
        }
        private NavigationMemberIdentity RequestIdentity(NavUnit unit)
        {
            var member=Member(unit.Id);var group=commandGroup??(member==null?null:groups[memberGroups[unit.Id]]);
            return new NavigationMemberIdentity(unit.Incarnation,unit.MobilityRevision,group?.GroupId??0,commandGroup!=null?commandActivationRevision:member?.OrderRevision??0,group?.MembershipRevision??0);
        }
        private bool CurrentMember(NavigationRequest request)
        {
            var identity=request.MemberIdentity;
            if(identity==null)return true; // Supported pre-A1 v9 request; its original order/reference checks still apply.
            if(!Crowd.TryGet(request.Entity,out var actor)||actor.Incarnation!=identity.Incarnation||actor.MobilityRevision!=identity.MobilityRevision)return false;
            if(identity.GroupId==0)return true;
            var member=Member(request.Entity);
            return member!=null&&memberGroups[request.Entity]==identity.GroupId&&member.Incarnation==identity.Incarnation&&member.OrderRevision==identity.OrderRevision;
        }
        private void RefreshGroupMembers()
        {
            foreach(int id in memberGroups.Keys.ToArray())RefreshGroupMember(id);
        }
        private void RefreshGroupMember(int id)
        {
                var member=Member(id);if(!Crowd.TryGet(id,out var actor)||actor.Incarnation!=member.Incarnation){DetachMember(id);return;}
                member.MobilityRevision=actor.MobilityRevision;member.Reservation=reservations.ContainsKey(id)?id:0;
                if(member.FormationReleased){member.Phase=GroupMemberPhase.Combat;return;}
                member.Phase=actor.Held?GroupMemberPhase.Held:IsPending(id)?GroupMemberPhase.PendingRoute:actor.Moving?GroupMemberPhase.March:
                    actor.Outcome==NavigationOutcome.Arrived?GroupMemberPhase.Arrived:actor.Outcome==NavigationOutcome.Unreachable?GroupMemberPhase.Unreachable:
                    actor.Outcome==NavigationOutcome.Blocked||actor.Outcome==NavigationOutcome.Rejected?GroupMemberPhase.Blocked:GroupMemberPhase.Stopped;
        }
        // Called at the post-movement visibility phase; never changes targeting/fire/movement intent.
        internal void SetFormationContact(int entity,bool visibleContact,long tick)
        {
            lock(transportGate){
                var member=Member(entity);if(member==null)return;
                if(member.FormationReleased!=visibleContact){
                    var group=groups[memberGroups[entity]];
                    group.MarchAnchor=0;group.MarchInitialized=false;
                }
                member.FormationReleased=visibleContact;member.ContactTick=tick;member.ContactPhase="PostMovementVisibility";
                PublishAdmission();
            }
        }
        // A3 consumes this guard to connect forward from the actor's current position, never the old anchor.
        public bool TryForwardResume(long generation,int entity,long groupId,long revision,long incarnation,out NavPoint from,out NavPoint originalGoal)
        {
            from=originalGoal=default(NavPoint);var member=Member(entity);
            if(generation!=Generation||!groups.TryGetValue(groupId,out var group)||(group.Kind!=PlayableCommandKind.Move&&group.Kind!=PlayableCommandKind.AttackMove))return false;
            if(member==null||memberGroups[entity]!=groupId||member.OrderRevision!=revision||member.Incarnation!=incarnation||member.FormationReleased||
                !Crowd.TryGet(entity,out var actor)||actor.Incarnation!=incarnation||actor.Held||member.Phase==GroupMemberPhase.Arrived||member.Phase==GroupMemberPhase.Unreachable)return false;
            from=actor.Position;originalGoal=groups[groupId].OriginalGoal;return true;
        }
        private void RestoreGroups(NavigationSessionState state)
        {
            if(state.GroupLayoutVersion!=0&&state.GroupLayoutVersion!=1||state.GroupLayoutVersion==1&&(state.Groups==null||state.GroupKeys==null||state.MemberGroupKeys==null||state.GroupLayouts==null||state.GroupLayouts.Length!=2)||state.GroupLayoutVersion==0&&(state.GroupKeys!=null||state.MemberGroupKeys!=null||state.GroupLayouts!=null))throw new ArgumentException("Invalid group layout metadata.");
            if(state.Groups==null){ // Current v9 legacy worlds have individual intents; never guess a common command.
                foreach(var unit in Crowd.Units)if(retainedGoals.TryGetValue(unit.Id,out var goal))BindLegacyGroup(new[]{unit.Id},goal,true);
                return;
            }
            if(state.GroupSequence<0)throw new ArgumentException("Invalid group allocator.");
            foreach(var saved in state.Groups){
                if(saved==null||saved.WorldGeneration!=Generation||saved.GroupId<1||saved.GroupId>state.GroupSequence||saved.OrderRevision<1||saved.MembershipRevision<1||saved.CommandSequence<0||saved.IssuedTick<0||
                    saved.MarchVersion<0||saved.MarchVersion>1||saved.MarchAnchor>0||double.IsNaN(saved.MarchAnchor)||double.IsInfinity(saved.MarchAnchor)||
                    (saved.MarchVersion==0?(saved.MarchInitialized||saved.MarchAnchor!=0||saved.MarchStretch!=0||saved.MarchProfileRevision!=0):
                        !marchConfigured||saved.MarchProfileRevision<1||saved.MarchStretch<=0||double.IsNaN(saved.MarchStretch)||double.IsInfinity(saved.MarchStretch)||
                        !saved.MarchInitialized&&saved.MarchAnchor!=0||
                        saved.MarchProfileRevision==marchProfileRevision&&saved.MarchStretch!=marchStretch)||
                    !Enum.IsDefined(typeof(PlayableOrderOrigin),saved.Origin)||!Enum.IsDefined(typeof(PlayableCommandKind),saved.Kind)||saved.Members==null||saved.Members.Length==0||groups.ContainsKey(saved.GroupId)||
                    (saved.Origin==PlayableOrderOrigin.Ai?(string.IsNullOrWhiteSpace(saved.Source)||saved.JobId<1||saved.ActionId<1):(saved.Source!=null||saved.JobId!=0||saved.ActionId!=0)))throw new ArgumentException("Invalid group checkpoint.");
                int previous=-1;var slots=new HashSet<int>();
                foreach(var member in saved.Members){
                    if(member==null||member.ContactTick<0||(saved.MarchVersion==0?member.Progress<0||member.ProgressRequest!=0:member.Progress>0)||double.IsNaN(member.Progress)||double.IsInfinity(member.Progress)||member.ProgressRequest<0||member.ProgressRequest>state.RequestSequence||member.Branch<0||member.Reservation<0||member.Entity<=previous||memberGroups.ContainsKey(member.Entity)||member.OrderRevision<saved.OrderRevision||member.Slot<0||!slots.Add(member.Slot)||
                        !Enum.IsDefined(typeof(GroupMemberPhase),member.Phase)||member.Phase==GroupMemberPhase.Dead||member.Phase==GroupMemberPhase.Detached||
                        !Crowd.TryGet(member.Entity,out var actor)||member.Incarnation!=actor.Incarnation||member.MobilityRevision!=actor.MobilityRevision)throw new ArgumentException("Invalid group member checkpoint.");
                    previous=member.Entity;memberGroups.Add(member.Entity,saved.GroupId);
                }
                groups.Add(saved.GroupId,saved.Copy());
            }
            if(state.GroupLayouts!=null){
                if(state.GroupLayouts.Length!=2)throw new ArgumentException("Invalid group layout count.");
                RestoreGroupMap(groups,state.GroupKeys,state.GroupLayouts[0]);RestoreGroupMap(memberGroups,state.MemberGroupKeys,state.GroupLayouts[1]);
            }
            groupSequence=state.GroupSequence;
            foreach(var request in pending.Values)if(!CurrentMember(request))throw new ArgumentException("Invalid pending member identity.");
        }
    }
}
