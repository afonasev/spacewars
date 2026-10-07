using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        [Serializable] private sealed class RefineryUpgrade
        { public int TermsRevision;
            public double PaidCost,Duration,Elapsed;
            public bool Complete;
        }
        private string UpgradeBlocked(Building b)
        {
            if(b.Kind!=PlayableBuildingKind.Refinery||!b.Ready||b.Health<=0||b.Sale!=null)return "Нужен готовый собственный завод";
            if(b.Upgrade!=null)return b.Upgrade.Complete?"Завод уже улучшен":"Улучшение уже начато";
            if(!buildings.Values.Any(s=>s.Owner==b.Owner&&s.Kind==PlayableBuildingKind.ScientificCenter&&s.Ready&&s.Health>0&&s.Sale==null))return "Нужен готовый собственный Научный центр";
            return Balance(b.Owner)<profile.RefineryUpgradeCost?"Недостаточно кредитов":null;
        }
        private PlayableCommandStatus StartRefineryUpgrade(int id,PlayableOwner owner,out string message)
        {
            message="Выберите собственный завод";
            if(!buildings.TryGetValue(id,out var b)||b.Owner!=owner)return PlayableCommandStatus.InvalidEntity;
            message=UpgradeBlocked(b);if(message!=null)return PlayableCommandStatus.Rejected;
            AddCredits(owner,-profile.RefineryUpgradeCost);
            b.Upgrade=new RefineryUpgrade{TermsRevision=profile.Revision,PaidCost=profile.RefineryUpgradeCost,Duration=profile.RefineryUpgradeSeconds};
            message="Улучшение завода начато";return PlayableCommandStatus.Applied;
        }
        private PlayableCommandStatus CancelRefineryUpgrade(int id,PlayableOwner owner,out string message)
        {
            message="Нет активного улучшения";
            if(!buildings.TryGetValue(id,out var b)||b.Owner!=owner||b.Sale!=null||b.Upgrade==null||b.Upgrade.Complete)return PlayableCommandStatus.InvalidEntity;
            AddCredits(owner,b.Upgrade.PaidCost);b.Upgrade=null;
            message="Улучшение отменено: полный возврат";return PlayableCommandStatus.Applied;
        }
        private void AdvanceRefineryUpgrades(double dt)
        {
            foreach(var b in buildings.Values)
            {
                var u=b.Upgrade;if(eliminated.Contains(b.Owner)||u==null||u.Complete||b.Sale!=null||!b.Ready)continue;
                u.Elapsed=Math.Min(u.Duration,u.Elapsed+dt);
                if(u.Elapsed+1e-9>=u.Duration){u.Elapsed=u.Duration;u.Complete=true;}
            }
        }
        private PlayableRefineryUpgradeSnapshot UpgradeSnapshot(Building b)
        {
            if(b.Kind!=PlayableBuildingKind.Refinery||!b.Ready)return null;
            var u=b.Upgrade;
            return new PlayableRefineryUpgradeSnapshot(u!=null&&!u.Complete,u?.Complete??false,u?.PaidCost??0,u?.Elapsed??0,u?.Duration??profile.RefineryUpgradeSeconds,UpgradeBlocked(b));
        }
    }
}
