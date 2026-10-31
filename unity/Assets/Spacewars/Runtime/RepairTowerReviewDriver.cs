#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Linq;
using System.Threading;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Bounded native presentation review with actual authoritative healing. Setup/damage are scripted;
    // the separate ordinary-host test qualifies the command-to-construction path.
    public sealed class RepairTowerReviewDriver:IDisposable
    {
        readonly PlayableDomain domain;readonly PlayableAuthorityTick authority;readonly CertifiedRouteWorker worker;
        readonly int[] actors;bool airDamaged,underFire;
        public RepairTowerReviewDriver(PlayableProfile profile)
        {
            domain=new PlayableDomain(profile,813,false);actors=domain.PrepareRepairReview();
            authority=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);
            worker=new CertifiedRouteWorker(authority.NavigationBinding,profile.NavigationRequestsPerFrame);
        }
        public PlayableSnapshot View=>authority.Latest;
        public bool Step()
        {
            if(worker.Publication.Failure!=null)throw new InvalidOperationException(worker.Publication.Failure.ToString());
            if(!authority.TryAdvance())return false;
            if(!airDamaged&&domain.Tick>=60){domain.RepairReviewDamage(actors[1],40);airDamaged=true;}
            if(!underFire&&domain.Tick>=120){domain.RepairReviewDamage(actors[0],8);domain.RepairReviewDamage(actors[2],8);underFire=true;}
            return true;
        }
        public void Dispose(){worker.Dispose();if(!SpinWait.SpinUntil(()=>worker.IsStopped,5000))throw new InvalidOperationException("Review worker did not stop");domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}
    }
    internal sealed partial class PlayableDomain
    {
        internal void RepairReviewDamage(int id,int hp)=>Damage(id,hp);
        internal int[] PrepareRepairReview()
        {
            AddCredits(PlayableOwner.Player,10000);
            var home=sites.Values.Single(s=>s.Site.Id==1);
            var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter,PlayableBuildingKind.Hangar,PlayableBuildingKind.Factory};
            foreach(var slot in home.Site.Slots)BuildAt(1,slot.Id,kinds[(slot.Id-1)%kinds.Length],home.CenterId,PlayableOwner.Player,out _);
            BuildAt(1,1001,PlayableBuildingKind.RepairTower,home.CenterId,PlayableOwner.Player,out _);
            BuildAt(1,1002,PlayableBuildingKind.SentryTurret,home.CenterId,PlayableOwner.Player,out _);
            AdvanceFoundations();AdvanceBuildings(100);
            var tower=buildings.Values.Single(b=>b.Kind==PlayableBuildingKind.RepairTower);var at=tower.Position;
            int tank=SpawnUnit(new NavPoint(at.X+3,at.Z+2),PlayableOwner.Player,PlayableEntityKind.Tank);navigation.Crowd.Stop(tank,true);Damage(tank,40);
            int hawk=SpawnUnit(new NavPoint(at.X-2,at.Z-3),PlayableOwner.Player,PlayableEntityKind.Hawk);
            SpawnUnit(new NavPoint(at.X+5,at.Z),PlayableOwner.Player,PlayableEntityKind.Explorer);
            return new[]{tank,hawk,tower.Id};
        }
    }
}
#endif
