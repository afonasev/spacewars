using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private void ValidateGroupOrders()
        {
            foreach(var group in navigation.GroupOrders){
                if(group.MarchVersion>0&&(!termProfiles.TryGetValue(group.MarchProfileRevision,out var marchTerms)||
                    group.MarchStretch!=marchTerms.GroupMarchMaximumStretch))
                    throw new System.ArgumentException("Invalid group march balance revision.");
                if(group.OwnerId==null)continue; // Explicit unknown-origin legacy singleton/group.
                foreach(var member in group.Members){
                    if(!units.TryGetValue(member.Entity,out var u)||OwnerName(u.Owner)!=group.OwnerId||TeamOf(u.Owner)!=group.TeamId||group.IssuedTick>Tick||member.ContactTick>Tick)
                        throw new System.ArgumentException("Invalid group/domain command binding.");
                    bool bound=u.LastOrder!=null&&u.LastOrder.Revision==member.OrderRevision&&u.LastOrder.Sequence==group.CommandSequence&&u.LastOrder.Kind==group.Kind&&u.LastOrder.Origin==group.Origin&&
                        u.LastOrder.Source==group.Source&&u.LastOrder.JobId==group.JobId&&u.LastOrder.ActionId==group.ActionId;
                    if(!bound&&!PreservedAttackAfterFailedReplacement(u,group))throw new System.ArgumentException("Invalid group/domain command binding.");
                    if(u.CurrentOrder!=null&&u.CurrentOrder.CommandSequence==group.CommandSequence&&group.OriginalLocation.HasValue){var o=u.CurrentOrder;if(!o.Destination.Equals(group.OriginalGoal)||o.CommandSequence!=group.CommandSequence||o.Owner!=u.Owner)throw new System.ArgumentException("Invalid tactical surface anchor binding.");u.CurrentOrder=new PlayableTacticalOrderSnapshot(o.UnitId,o.Owner,o.Generation,o.CommandSequence,o.IssuedTick,o.Kind,o.Destination,o.TargetId,DomainLocation(group.OriginalLocation));}
                }
            }
        }
        private readonly PlayableTargetSpatialIndex contactSpatialIndex=new PlayableTargetSpatialIndex();
        private bool contactSpatialPhase;
        private void RebuildContactSpatialIndex()
        {
            contactSpatialIndex.Clear();
            foreach(var u in units.Values)if(TryActor(u.Id,out var actor))contactSpatialIndex.Add(u.Id,actor.Position,false);
            // FormationContact historically considers ALL buildings, including pending.
            foreach(var building in buildings.Values)contactSpatialIndex.Add(building.Id,building.Position,true);
        }
        private void RefreshFormationContacts()
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.Contacts);
#if UNITY_INCLUDE_TESTS
            if(!ContactPhaseQa.FullScanDetection)
#endif
            RebuildContactSpatialIndex();
            contactSpatialPhase=true;
            try{
                navigation.RefreshFormationGroupContacts(units.Keys,units.Count,FormationGroupContact,Tick
#if UNITY_INCLUDE_TESTS
                    ,FormationGroupPublished
#endif
                );
            }finally{contactSpatialPhase=false;}
        }
        private bool? FormationGroupContact(int entity)
        {
            if(!units.TryGetValue(entity,out var u)||!TryActor(entity,out var self))return null;
            double range=PlayableUnitRules.Range(profile,u.Kind,u.Kind==PlayableEntityKind.Shkval&&UnitUpgraded(u));bool contact;
            using(PerformanceProfile.Measure(ProfileStage.ContactDetect))contact=FormationContact(u.Owner,self.Position,range,u.Kind==PlayableEntityKind.Explorer?0:profile.TankProjectileCollisionRadius);
            PerformanceProfile.Add(ProfileWork.ContactMembers,1);
            return contact;
        }
        private void FormationGroupPublished(int entity,bool contact)
        {
#if UNITY_INCLUDE_TESTS
            ContactPhaseQa.AfterPublish?.Invoke(entity,contact);
#endif
        }
        private bool FormationContact(PlayableOwner owner,NavPoint position,double range,double radius)
        {
#if UNITY_INCLUDE_TESTS
            if(ContactPhaseQa.FullScanDetection)return FormationContactFullScanForQa(owner,position,range,radius);
#endif
            // A direct query outside the phase also observes spawn/death/body changes.
            if(!contactSpatialPhase)RebuildContactSpatialIndex();
            contactSpatialIndex.Query(position,range,contactSpatialIndex.Candidates);
            contactSpatialIndex.Candidates.Sort(); // original unit order, then building order
#if UNITY_INCLUDE_TESTS
            ContactPhaseQa.QueryVisited?.Invoke(contactSpatialIndex.VisitedEntries);
#endif
            foreach(var candidate in contactSpatialIndex.Candidates){
                if(!candidate.Building){
                    if(!units.TryGetValue(candidate.Id,out var other))continue;
                    NavUnit enemy;
                    if(other.Air!=null)enemy=other.Air.Actor;
                    else if(!navigation.Crowd.TryGet(other.Id,out enemy))continue;
                    if(Distance(position,enemy.Position)<=range&&
#if UNITY_INCLUDE_TESTS
                        ContactPhaseQa.Range(other.Id)&&
#endif
                        VisibleTarget(owner,other.Id)&&
#if UNITY_INCLUDE_TESTS
                        ContactPhaseQa.Terrain(other.Id)&&
#endif
                        GroundFireClear(position,enemy.Position,radius))return true;
                }else{
                    if(!buildings.TryGetValue(candidate.Id,out var other))continue;
                    if(Distance(position,other.Position)<=range&&
#if UNITY_INCLUDE_TESTS
                        ContactPhaseQa.Range(other.Id)&&
#endif
                        VisibleTarget(owner,other.Id)&&
#if UNITY_INCLUDE_TESTS
                        ContactPhaseQa.Terrain(other.Id)&&
#endif
                        GroundFireClear(position,other.Position,radius))return true;
                }
            }
            return false;
        }
#if UNITY_INCLUDE_TESTS
        // Frozen pre-Q9 publication granularity with the SAME spatial detector.
        // Only for the one authorized whole-Contacts control; absent in Player.
        private void RefreshFormationContactsMemberControlForQa()
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.Contacts);
            RebuildContactSpatialIndex();contactSpatialPhase=true;
            try{foreach(var u in units.Values){
                if(!navigation.HasGroupMember(u.Id))continue;var contact=FormationGroupContact(u.Id);if(!contact.HasValue)continue;
                using(PerformanceProfile.Measure(ProfileStage.ContactPublish))navigation.SetFormationContact(u.Id,contact.Value,Tick);
            }}finally{contactSpatialPhase=false;}
        }
        // Baseline003e435c2 detector; only read-only predicate-observer hooks added.
        private bool FormationContactFullScanForQa(PlayableOwner owner,NavPoint position,double range,double radius)
        {
            // Range is a pure exact rejection before the source-scanning visibility test.
            // Keep the original distance expression, candidate order and terrain predicate;
            // publication still occurs for every member, including unchanged contact values.
            foreach(var other in units.Values){
                // This exact Unit is already in the enumeration. Preserve TryActor's
                // Air/Crowd branches without looking the same Unit up again by id.
                NavUnit enemy;
                if(other.Air!=null)enemy=other.Air.Actor;
                else if(!navigation.Crowd.TryGet(other.Id,out enemy))continue;
                if(Distance(position,enemy.Position)<=range&&
#if UNITY_INCLUDE_TESTS
                    ContactPhaseQa.Range(other.Id)&&
#endif
                    VisibleTarget(owner,other.Id)&&
#if UNITY_INCLUDE_TESTS
                    ContactPhaseQa.Terrain(other.Id)&&
#endif

                    GroundFireClear(position,enemy.Position,radius))return true;
            }
            foreach(var other in buildings.Values){
                if(Distance(position,other.Position)<=range&&
#if UNITY_INCLUDE_TESTS
                    ContactPhaseQa.Range(other.Id)&&
#endif
                    VisibleTarget(owner,other.Id)&&
#if UNITY_INCLUDE_TESTS
                    ContactPhaseQa.Terrain(other.Id)&&
#endif

                    GroundFireClear(position,other.Position,radius))return true;
            }
            return false;
        }
#endif
#if UNITY_INCLUDE_TESTS
        // Exact predecessor8c9040943 for the bounded fixed-input A/B/A control.
        private void RefreshFormationContactsPredecessorForQa()
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.Contacts);
            // Observation only: same post-movement vision and existing local weapon/terrain limits.
            // In particular ordinary Tank Move still does not acquire a target or stop to fire.
            foreach(var u in units.Values){
                if(!navigation.HasGroupMember(u.Id)||!TryActor(u.Id,out var self))continue;
                double range=PlayableUnitRules.Range(profile,u.Kind,u.Kind==PlayableEntityKind.Shkval&&UnitUpgraded(u));bool contact=false;
                using(PerformanceProfile.Measure(ProfileStage.ContactDetect)){
                contact=FormationContactPredecessorForQa(u.Owner,self.Position,range,u.Kind==PlayableEntityKind.Explorer?0:profile.TankProjectileCollisionRadius);
                }
                PerformanceProfile.Add(ProfileWork.ContactMembers,1);
                using(PerformanceProfile.Measure(ProfileStage.ContactPublish))navigation.SetFormationContact(u.Id,contact,Tick);
            }
        }
        private bool FormationContactPredecessorForQa(PlayableOwner owner,NavPoint position,double range,double radius)
        {
            // Range is a pure exact rejection before the source-scanning visibility test.
            // Keep the original distance expression, candidate order and terrain predicate;
            // publication still occurs for every member, including unchanged contact values.
            foreach(var other in units.Values){
                if(TryActor(other.Id,out var enemy)&&Distance(position,enemy.Position)<=range&&VisibleTarget(owner,other.Id)&&
                    GroundFireClear(position,enemy.Position,radius))return true;
            }
            foreach(var other in buildings.Values){
                if(Distance(position,other.Position)<=range&&VisibleTarget(owner,other.Id)&&
                    GroundFireClear(position,other.Position,radius))return true;
            }
            return false;
        }
#endif
    }
}
