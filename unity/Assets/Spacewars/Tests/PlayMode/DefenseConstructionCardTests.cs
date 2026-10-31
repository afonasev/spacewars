using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;

public sealed class DefenseConstructionCardTests
{
    [TestCase(0)] [TestCase(1)] [TestCase(2)]
    public void ActiveProfileAndCompletedOwnerResearchDriveDefenseFacts(int level)
    {
        var data=JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text);
        data.explorerDamage=7;data.tankHealth=200;data.repairTowerHealthPerSecond=6;
        var p=PlayableProfile.Create(data);
        var research=new[]{new PlayableResearchOrderSnapshot(1,PlayableResearchKind.ImprovedWeaponSystems,1,1,1,0,false,level>=1),new PlayableResearchOrderSnapshot(2,PlayableResearchKind.AdvancedWeaponSystems,1,1,1,0,false,level>=2)};
        double factor=1+(level==2?p.WeaponSystemsLevel2BonusPercent:level==1?p.WeaponSystemsLevel1BonusPercent:0)/100;
        string N(double n)=>n.ToString("0.##");
        var sentry=PlayableMenuDescriptions.DefenseStats(p,PlayableBuildingKind.SentryTurret,research);
        CollectionAssert.Contains(sentry,"Урон за попадание: "+N(7*factor));
        CollectionAssert.Contains(sentry,"Очередь: "+N(p.ExplorerAssaultBurstSize)+" выстр.");
        CollectionAssert.Contains(sentry,"Интервал: "+N(p.ExplorerBurstShotIntervalMs/1000d)+" с");
        CollectionAssert.Contains(sentry,"Пауза: "+N(p.ExplorerBurstPauseMs/1000d)+" с");
        CollectionAssert.Contains(sentry,"Дальность: "+N(p.ExplorerRange*p.TurretRangeMultiplier)+" м");
        var gauss=PlayableMenuDescriptions.DefenseStats(p,PlayableBuildingKind.GaussTower,research);
        CollectionAssert.Contains(gauss,"Урон за попадание: "+N(200*p.GaussTankDamageFraction*factor));
        CollectionAssert.Contains(gauss,"Перезарядка: "+N(p.GaussReloadSeconds)+" с");
        CollectionAssert.Contains(gauss,"Дальность: "+N(p.GaussRange)+" м");
        CollectionAssert.Contains(gauss,"Остаток урона: "+N(p.GaussPierceRetention*100)+"%");
        CollectionAssert.AreEqual(new[]{"Скорость ремонта: 6 HP/с"},PlayableMenuDescriptions.DefenseStats(p,PlayableBuildingKind.RepairTower,research));
        Assert.IsEmpty(PlayableMenuDescriptions.DefenseStats(p,PlayableBuildingKind.Factory,research));
    }
}
