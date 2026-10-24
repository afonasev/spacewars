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
        private void RefreshFormationContacts()
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.Contacts);
            // Observation only: same post-movement vision and existing local weapon/terrain limits.
            // In particular ordinary Tank Move still does not acquire a target or stop to fire.
            foreach(var u in units.Values){
                if(!navigation.HasGroupMember(u.Id)||!TryActor(u.Id,out var self))continue;
                double range=PlayableUnitRules.Range(profile,u.Kind,u.Kind==PlayableEntityKind.Shkval&&UnitUpgraded(u));bool contact=false;
                using(PerformanceProfile.Measure(ProfileStage.ContactDetect)){
                contact=FormationContact(u.Owner,self.Position,range,u.Kind==PlayableEntityKind.Explorer?0:profile.TankProjectileCollisionRadius);
                }
                PerformanceProfile.Add(ProfileWork.ContactMembers,1);
                using(PerformanceProfile.Measure(ProfileStage.ContactPublish))navigation.SetFormationContact(u.Id,contact,Tick);
            }
        }
        private bool FormationContact(PlayableOwner owner,NavPoint position,double range,double radius)
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
    }
}
