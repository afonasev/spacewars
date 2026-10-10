using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private void ValidateGroupOrders()
        {
            foreach(var group in navigation.GroupOrders){
                if(group.MarchVersion==1&&(!termProfiles.TryGetValue(group.MarchProfileRevision,out var marchTerms)||
                    group.MarchStretch!=marchTerms.GroupMarchMaximumStretch))
                    throw new System.ArgumentException("Invalid group march balance revision.");
                if(group.OwnerId==null)continue; // Explicit unknown-origin legacy singleton/group.
                foreach(var member in group.Members){
                    if(!units.TryGetValue(member.Entity,out var u)||OwnerName(u.Owner)!=group.OwnerId||TeamOf(u.Owner)!=group.TeamId||group.IssuedTick>Tick||member.ContactTick>Tick)
                        throw new System.ArgumentException("Invalid group/domain command binding.");
                    bool bound=u.LastOrder!=null&&u.LastOrder.Revision==member.OrderRevision&&u.LastOrder.Sequence==group.CommandSequence&&u.LastOrder.Kind==group.Kind&&u.LastOrder.Origin==group.Origin&&
                        u.LastOrder.Source==group.Source&&u.LastOrder.JobId==group.JobId&&u.LastOrder.ActionId==group.ActionId;
                    if(!bound&&!PreservedAttackAfterFailedReplacement(u,group))throw new System.ArgumentException("Invalid group/domain command binding.");
                    if(u.CurrentOrder!=null&&u.CurrentOrder.CommandSequence==group.CommandSequence&&group.OriginalLocation.HasValue){var o=u.CurrentOrder;if(!o.Destination.Equals(group.OriginalGoal)||o.CommandSequence!=group.CommandSequence||o.Owner!=u.Owner)throw new System.ArgumentException("Invalid tactical surface anchor binding.");u.CurrentOrder=new PlayableTacticalOrderSnapshot(o.UnitId,o.Owner,o.Generation,o.CommandSequence,o.IssuedTick,o.Kind,o.Destination,o.TargetId,group.OriginalLocation);}
                }
            }
        }
        private void RefreshFormationContacts()
        {
            // Observation only: same post-movement vision and existing local weapon/terrain limits.
            // In particular ordinary Tank Move still does not acquire a target or stop to fire.
            foreach(var u in units.Values){
                if(!navigation.HasGroupMember(u.Id)||!navigation.Crowd.TryGet(u.Id,out var self))continue;
                double range=PlayableUnitRules.Range(profile,u.Kind,u.Kind==PlayableEntityKind.Shkval&&UnitUpgraded(u));bool contact=false;
                foreach(var other in units.Values){
                    if(VisibleTarget(u.Owner,other.Id)&&navigation.Crowd.TryGet(other.Id,out var enemy)&&Distance(self.Position,enemy.Position)<=range&&
                        GroundFireClear(self.Position,enemy.Position,u.Kind==PlayableEntityKind.Explorer?0:profile.TankProjectileCollisionRadius)){contact=true;break;}
                }
                if(!contact)foreach(var other in buildings.Values){
                    if(VisibleTarget(u.Owner,other.Id)&&Distance(self.Position,other.Position)<=range&&
                        GroundFireClear(self.Position,other.Position,u.Kind==PlayableEntityKind.Explorer?0:profile.TankProjectileCollisionRadius)){contact=true;break;}
                }
                navigation.SetFormationContact(u.Id,contact,Tick);
            }
        }
    }
}
