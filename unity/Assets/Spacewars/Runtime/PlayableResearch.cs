using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private const int MaximumResearchOrders=6;
        private sealed class ResearchOrder { public long Id; public PlayableResearchKind Kind; public int CenterId; public double PaidCost,Elapsed,Duration; public bool Active,Complete; }
        private readonly Dictionary<PlayableOwner,List<ResearchOrder>> research=new Dictionary<PlayableOwner,List<ResearchOrder>>();
        private long nextResearchSequence=1;
        private List<ResearchOrder> Research(PlayableOwner owner)
        {
            if(!research.TryGetValue(owner,out var orders))research.Add(owner,orders=new List<ResearchOrder>());
            return orders;
        }
        private bool HasResearch(PlayableOwner owner,PlayableResearchKind kind)=>Research(owner).Any(o=>o.Kind==kind&&o.Complete);
        private bool HasActiveResearch(PlayableOwner owner,PlayableResearchKind kind)=>Research(owner).Any(o=>o.Kind==kind&&!o.Complete);
        private double ResearchCost(PlayableResearchKind kind)=>kind==PlayableResearchKind.TankChassis?profile.TankChassisCost:kind==PlayableResearchKind.ExplorerAssaultGuns?profile.ExplorerAssaultCost:profile.ShkvalGuidanceCost;
        private double ResearchDuration(PlayableResearchKind kind)=>kind==PlayableResearchKind.TankChassis?profile.TankChassisSeconds:kind==PlayableResearchKind.ExplorerAssaultGuns?profile.ExplorerAssaultSeconds:profile.ShkvalGuidanceSeconds;
        private PlayableCommandStatus QueueResearch(int centerId,PlayableOwner owner,PlayableResearchKind kind,out string message)
        {
            message="Нужен готовый собственный Научный центр";
            if(!buildings.TryGetValue(centerId,out var center)||center.Owner!=owner||center.Kind!=PlayableBuildingKind.ScientificCenter||!center.Ready||center.Sale!=null)return PlayableCommandStatus.InvalidEntity;
            var orders=Research(owner);if(HasResearch(owner,kind)||HasActiveResearch(owner,kind)){message="Исследование уже заказано";return PlayableCommandStatus.Rejected;}
            if(orders.Count(o=>!o.Complete)>=MaximumResearchOrders){message="Очередь исследований заполнена";return PlayableCommandStatus.Overflow;}
            if(Balance(owner)<ResearchCost(kind)){message="Недостаточно кредитов";return PlayableCommandStatus.InsufficientCredits;}
            orders.Add(new ResearchOrder{Id=nextResearchSequence++,Kind=kind,CenterId=centerId});
            Sound(PlayableSoundKind.ResearchQueued,center.Position,PlayableEntityKind.Tank,owner,true);
            StartResearchIfPossible(owner);
            message="Исследование добавлено в очередь";return PlayableCommandStatus.Applied;
        }
        private PlayableCommandStatus CancelResearch(int centerId,long id,PlayableOwner owner,out string message)
        {
            message="Нет активного исследования";var orders=Research(owner);var order=orders.FirstOrDefault(o=>o.Id==id&&o.Active&&!o.Complete);
            if(order==null||order.CenterId!=centerId||!ReadyResearchCenter(centerId,owner))return PlayableCommandStatus.InvalidTarget;
            AddCredits(owner,order.PaidCost);orders.Remove(order);Sound(PlayableSoundKind.ResearchCancelled,buildings[centerId].Position,PlayableEntityKind.Tank,owner,true);message="Исследование отменено: полный возврат";StartResearchIfPossible(owner);return PlayableCommandStatus.Applied;
        }
        private void StartResearchIfPossible(PlayableOwner owner)
        {
            var active=Research(owner).FirstOrDefault(o=>o.Active&&!o.Complete);if(active!=null)return;
            var next=Research(owner).FirstOrDefault(o=>!o.Active&&!o.Complete);if(next==null)return;
            var center=ReadyResearchCenter(next.CenterId,owner)?buildings[next.CenterId]:buildings.Values.Where(b=>ReadyResearchCenter(b.Id,owner)).OrderBy(b=>b.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),StringComparer.Ordinal).FirstOrDefault();
            if(center==null)return;
            double cost=ResearchCost(next.Kind);if(Balance(owner)<cost)return;
            AddCredits(owner,-cost);next.CenterId=center.Id;next.PaidCost=cost;next.Duration=ResearchDuration(next.Kind);next.Elapsed=0;next.Active=true;Sound(PlayableSoundKind.ResearchStarted,center.Position,PlayableEntityKind.Tank,owner,true);
        }
        private bool ReadyResearchCenter(int id,PlayableOwner owner)=>buildings.TryGetValue(id,out var center)&&center.Owner==owner&&center.Kind==PlayableBuildingKind.ScientificCenter&&center.Ready&&center.Health>0&&center.Sale==null;
        private void AdvanceResearch(double dt)
        {
            foreach(PlayableOwner owner in Owners)
            {
                if(eliminated.Contains(owner))continue;
                var active=Research(owner).FirstOrDefault(o=>o.Active&&!o.Complete);
                if(active!=null&&!ReadyResearchCenter(active.CenterId,owner)){Sound(PlayableSoundKind.ResearchCancelled,default(NavPoint),PlayableEntityKind.Tank,owner,true);Research(owner).Remove(active);active=null;}
                // Source Number.EPSILON tolerance is a fixed floating-point parity rule, not a Balance Lab value.
                if(active!=null){active.Elapsed=Math.Min(active.Duration,active.Elapsed+dt);if(active.Duration-active.Elapsed<=2.220446049250313e-16*active.Duration*128){active.Elapsed=active.Duration;active.Active=false;active.Complete=true;var center=buildings[active.CenterId];Sound(PlayableSoundKind.ResearchComplete,center.Position,PlayableEntityKind.Tank,owner,true);if(active.Kind==PlayableResearchKind.TankChassis)foreach(var u in units.Values)if(u.Owner==owner&&u.Kind==PlayableEntityKind.Tank&&!u.AuthoredUpgrade.HasValue)navigation.Crowd.SetSpeed(u.Id,profile.TankChassisSpeed);}}
                StartResearchIfPossible(owner);
            }
        }
        private void InterruptResearchCenter(int centerId)
        {
            foreach(PlayableOwner owner in Owners)
            {
                foreach(var order in Research(owner).Where(o=>o.Active&&!o.Complete&&o.CenterId==centerId))Sound(PlayableSoundKind.ResearchCancelled,default(NavPoint),PlayableEntityKind.Tank,owner,true);
                Research(owner).RemoveAll(o=>o.Active&&!o.Complete&&o.CenterId==centerId);
            }
        }
        private void InterruptUnreadyResearchCenters()
        {
            foreach(PlayableOwner owner in Owners)
            {
                foreach(var order in Research(owner).Where(o=>o.Active&&!o.Complete&&!ReadyResearchCenter(o.CenterId,owner)))Sound(PlayableSoundKind.ResearchCancelled,default(NavPoint),PlayableEntityKind.Tank,owner,true);
                Research(owner).RemoveAll(o=>o.Active&&!o.Complete&&!ReadyResearchCenter(o.CenterId,owner));
            }
        }
        private PlayableResearchSaveState CaptureResearchState()
        {
            return new PlayableResearchSaveState
            {
                NextSequence=nextResearchSequence,
                Orders=research.SelectMany(pair=>pair.Value.Select(order=>new PlayableResearchSaveOrder
                {
                    Owner=(int)pair.Key,Id=order.Id,Kind=order.Kind,CenterId=order.CenterId,PaidCost=order.PaidCost,
                    Elapsed=order.Elapsed,Duration=order.Duration,Active=order.Active,Complete=order.Complete
                })).OrderBy(order=>order.Id).ToArray()
            };
        }
        private void RestoreResearchState(PlayableResearchSaveState state)
        {
            if(state==null)throw new ArgumentNullException(nameof(state));
            research.Clear();
            nextResearchSequence=Math.Max(1,state.NextSequence);
            foreach(var saved in state.Orders??Array.Empty<PlayableResearchSaveOrder>())
            {
                if(!HasOwner((PlayableOwner)saved.Owner)||!Enum.IsDefined(typeof(PlayableResearchKind),saved.Kind))throw new ArgumentException("Unknown research save value",nameof(state));
                var owner=(PlayableOwner)saved.Owner;
                Research(owner).Add(new ResearchOrder{Id=saved.Id,Kind=saved.Kind,CenterId=saved.CenterId,PaidCost=saved.PaidCost,Elapsed=saved.Elapsed,Duration=saved.Duration,Active=saved.Active,Complete=saved.Complete});
                nextResearchSequence=Math.Max(nextResearchSequence,saved.Id+1);
            }
            foreach(var owner in research.Keys.ToArray())if(Chassis(owner))foreach(var unit in units.Values)if(unit.Owner==owner&&unit.Kind==PlayableEntityKind.Tank&&!unit.AuthoredUpgrade.HasValue)navigation.Crowd.SetSpeed(unit.Id,profile.TankChassisSpeed);
        }
        private PlayableResearchOrderSnapshot[] ResearchSnapshot(PlayableOwner owner)=>Research(owner).Select(o=>new PlayableResearchOrderSnapshot(o.Id,o.Kind,o.CenterId,o.PaidCost,o.Elapsed,o.Duration,o.Active,o.Complete)).ToArray();
        private PlayableResearchAvailabilitySnapshot[] ResearchAvailability(PlayableOwner owner)=>new[]{PlayableResearchKind.TankChassis,PlayableResearchKind.ExplorerAssaultGuns,PlayableResearchKind.ShkvalGuidance}.Select(kind=>new PlayableResearchAvailabilitySnapshot(kind,ResearchCost(kind),!HasResearch(owner,kind)&&!HasActiveResearch(owner,kind))).ToArray();
        private bool UnitUpgraded(Unit u)=>u.AuthoredUpgrade??(u.Kind==PlayableEntityKind.Tank?Chassis(u.Owner):u.Kind==PlayableEntityKind.Explorer?Assault(u.Owner):Guidance(u.Owner));
        private bool Chassis(PlayableOwner owner)=>HasResearch(owner,PlayableResearchKind.TankChassis);
        private bool Assault(PlayableOwner owner)=>HasResearch(owner,PlayableResearchKind.ExplorerAssaultGuns);
        private bool Guidance(PlayableOwner owner)=>HasResearch(owner,PlayableResearchKind.ShkvalGuidance);
    }
}
