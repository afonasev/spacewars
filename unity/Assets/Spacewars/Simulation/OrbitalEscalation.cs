using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Existing IDs remain stable; Control Hack is an additional basic ability at ID 8.
    public static class OrbitalAbilities
    {
        public const int CompactProgressionVersion=1;
        public const int ControlHack=8,SystemFailure=3,Reinforcement=4,Stasis=5,RepairNanobots=9,AcceleratedProduction=10,NuclearStrike=6;
        public static readonly IReadOnlyList<int> Implemented=Array.AsReadOnly(new[]{1,2,ControlHack,SystemFailure,Reinforcement,Stasis,RepairNanobots,AcceleratedProduction,NuclearStrike});
        public static int Tier(int id)=>id==ControlHack||id>=1&&id<=2?1:(id>=3&&id<=5||id==RepairNanobots||id==AcceleratedProduction)?2:id>=6&&id<=7?3:0;
        public static string Name(int id)=>id==NuclearStrike?"Ядерный удар":id==AcceleratedProduction?"Ускоренная выработка":id==RepairNanobots?"Ремонтные нанороботы":id==Stasis?"Стазис":id==Reinforcement?"Призыв подкрепления":id==SystemFailure?"Сбой систем":id==ControlHack?"Взлом управления":id==1?"Сканирование":id==2?"Мина-ловушка":"Поддержка "+(Tier(id)==1?"I":Tier(id)==2?"II":"III")+" · "+(id==1||id==3||id==6?"А":id==5?"В":"Б");
        public static double Duration(int id,PlayableProfileData p)=>id==ControlHack?p.orbitalHackSeconds:Tier(id)==1?p.orbitalBasicSeconds:Tier(id)==2?p.orbitalAdvancedSeconds:p.orbitalUltimateSeconds;
        public static double Threshold(int level,PlayableProfileData p)=>level switch
        {
            0=>p.orbitalThreshold1,
            1=>p.orbitalThreshold2,
            2=>p.orbitalThreshold3,
            3=>p.orbitalThreshold4,
            4=>p.orbitalProgressionVersion==CompactProgressionVersion?p.orbitalThresholdMax:p.orbitalThreshold5,
            5 when p.orbitalProgressionVersion!=CompactProgressionVersion=>p.orbitalThresholdMax,
            _=>0
        };
        internal static int ChoiceTier(int stage,bool compact)
        {
            if(compact&&stage>=2)stage++;
            return stage switch { 1 or 2=>1,3 or 4=>2,5=>3,_=>0 };
        }
    }
    public sealed class OrbitalScanSnapshot
    {
        public readonly PlayableOwner Owner;public readonly NavPoint Point;public readonly double Radius;
        public OrbitalScanSnapshot(PlayableOwner owner,NavPoint point,double radius){Owner=owner;Point=point;Radius=radius;}
    }
    public sealed class OrbitalAbilitySnapshot
    {
        public readonly int Id;public readonly double Remaining,Duration,Recovery;
        public OrbitalAbilitySnapshot(int id,double remaining,double duration,double speed){Id=id;Duration=duration;Recovery=remaining;Remaining=remaining/speed;}
    }
    public sealed class OrbitalEscalationSnapshot
    {
        public static readonly OrbitalEscalationSnapshot Empty=new OrbitalEscalationSnapshot(0,0,250,0,0,Array.Empty<OrbitalAbilitySnapshot>(),Array.Empty<int>());
        public bool IsMax=>Level>0&&Threshold==0;
        public readonly int Level,PendingLevel;public readonly double Progress,Threshold,CommonRemaining;
        public OrbitalReinforcementQuote ReinforcementQuote {get;}
        public IReadOnlyList<OrbitalRepairRim> RepairRims { get; }
        public IReadOnlyList<int> RepairedUnits { get; }
        public IReadOnlyList<OrbitalNuclearSnapshot> NuclearStrikes { get; }
        public readonly IReadOnlyList<OrbitalAbilitySnapshot> Abilities;public readonly IReadOnlyList<int> Options;
        public OrbitalEscalationSnapshot(int level,double progress,double threshold,int pending,double common,OrbitalAbilitySnapshot[] abilities,int[] options,OrbitalReinforcementQuote reinforcementQuote=default, OrbitalRepairRim[] repairRims=null, int[] repairedUnits=null, OrbitalNuclearSnapshot[] nuclearStrikes=null)
        {NuclearStrikes=Array.AsReadOnly(nuclearStrikes??Array.Empty<OrbitalNuclearSnapshot>());RepairRims=Array.AsReadOnly(repairRims??Array.Empty<OrbitalRepairRim>());RepairedUnits=Array.AsReadOnly(repairedUnits??Array.Empty<int>());ReinforcementQuote=reinforcementQuote;Level=level;Progress=progress;Threshold=threshold;PendingLevel=pending;CommonRemaining=common;Abilities=Array.AsReadOnly(abilities);Options=Array.AsReadOnly(options);}
    }
    public sealed class OrbitalEscalationState
    {
        public const int StoredChoiceCount=5;
        private int ChoiceCount=>compact?4:StoredChoiceCount;
        private int MaxLevel=>ChoiceCount+1;
        public int Level{get;private set;}public double Progress{get;private set;}public double CommonRemaining{get;private set;}
        private readonly bool provingGround,compact;
        public OrbitalEscalationState(bool provingGround=false,bool compact=false)
        {
            this.provingGround=provingGround;
            this.compact=compact;
            // Keep the fifth serialized slot empty in compact matches.
            choices=new int[provingGround?OrbitalAbilities.Implemented.Count:StoredChoiceCount];
            remaining=new double[choices.Length];
            if(provingGround){Level=MaxLevel;for(int i=0;i<choices.Length;i++)choices[i]=OrbitalAbilities.Implemented[i];}
        }
        private double Speed(PlayableProfileData p)=>!provingGround&&Level==MaxLevel?p.orbitalFinalSpeed:1;
        private double Duration(int id,PlayableProfileData p)=>provingGround?p.provingGroundOrbitalSeconds:OrbitalAbilities.Duration(id,p);
        private readonly int[] choices;private readonly double[] remaining;
        public double ProvingGroundNuclearRecovery=>provingGround?remaining[StoredChoiceCount+3]:0;
        public void RestoreProvingGroundNuclear(double seconds)
        {
            if(!Finite(seconds)||seconds<0||!provingGround&&seconds!=0)throw new ArgumentException("Invalid proving ground nuclear timer.");
            if(provingGround)remaining[StoredChoiceCount+3]=seconds;
        }
        public double ProvingGroundProductionRecovery=>provingGround?remaining[StoredChoiceCount+2]:0;
        public void RestoreProvingGroundProduction(double seconds)
        {
            if(!provingGround||!Finite(seconds)||seconds<0)throw new ArgumentException("Invalid proving ground production timer.");
            remaining[StoredChoiceCount+2]=seconds;
        }
        public double ProvingGroundStasisRecovery=>provingGround?remaining[StoredChoiceCount]:0;
        public void RestoreProvingGroundStasis(double seconds)
        {
            if(!Finite(seconds)||seconds<0||!provingGround&&seconds!=0)throw new ArgumentException("Invalid proving ground stasis timer.");
            if(provingGround)remaining[StoredChoiceCount]=seconds;
        }
        public double ProvingGroundRepairRecovery => provingGround ? remaining[StoredChoiceCount + 1] : 0;
        public void RestoreProvingGroundRepair(double seconds)
        {
            if(!Finite(seconds)||seconds<0||!provingGround&&seconds!=0)
                throw new ArgumentException("Invalid proving ground repair timer.");
            if(provingGround)remaining[StoredChoiceCount + 1]=seconds;
        }
        public int Choice(int slot)=>choices[slot];public double Recovery(int slot)=>remaining[slot];
        public int PendingLevel=>provingGround?0:Enumerable.Range(0,Math.Min(Level,ChoiceCount)).Where(i=>choices[i]==0).Select(i=>i+1).FirstOrDefault();
        public int[] Options()
        {
            int tier=OrbitalAbilities.ChoiceTier(PendingLevel,compact);
            return tier==0?Array.Empty<int>():OrbitalAbilities.Implemented.Where(id=>OrbitalAbilities.Tier(id)==tier&&!choices.Contains(id)).ToArray();
        }
        public void Damage(double amount,PlayableProfileData p)
        {
            if(provingGround||amount<=0||double.IsNaN(amount)||double.IsInfinity(amount)||Level==MaxLevel)return;
            Progress+=amount;
            while(Level<MaxLevel&&Progress>=OrbitalAbilities.Threshold(Level,p)){Progress-=OrbitalAbilities.Threshold(Level,p);Level++;}
            if(Level==MaxLevel)Progress=0;
        }
        public bool Choose(int id)
        {int pending=PendingLevel;if(pending==0||!Options().Contains(id))return false;choices[pending-1]=id;return true;}
        public bool CanUse(int id)=>id!=0&&Array.IndexOf(choices,id)>=0&&CommonRemaining<=1e-9&&remaining[Array.IndexOf(choices,id)]<=1e-9;
        public bool Use(int id,PlayableProfileData p)
        {
            int slot=Array.IndexOf(choices,id);
            if(id==0||slot<0||CommonRemaining>1e-9||remaining[slot]>1e-9)return false;
            remaining[slot]=Duration(id,p);CommonRemaining=provingGround?0:p.orbitalCommonSeconds;return true;
        }
        public void Advance(double dt,PlayableProfileData p)
        {if(dt<=0)return;CommonRemaining=Math.Max(0,CommonRemaining-dt);for(int i=0;i<remaining.Length;i++)remaining[i]=Math.Max(0,remaining[i]-dt*Speed(p));}
        public OrbitalEscalationSnapshot Snapshot(PlayableProfileData p,OrbitalReinforcementQuote reinforcementQuote=default, OrbitalRepairRim[] repairRims=null, int[] repairedUnits=null, OrbitalNuclearSnapshot[] nuclearStrikes=null)=>new OrbitalEscalationSnapshot(Level,Progress,OrbitalAbilities.Threshold(Level,p),PendingLevel,CommonRemaining,
            Enumerable.Range(0,choices.Length).Where(i=>choices[i]!=0).Select(i=>new OrbitalAbilitySnapshot(choices[i],remaining[i],Duration(choices[i],p),Speed(p))).ToArray(),Options(),reinforcementQuote,repairRims,repairedUnits,nuclearStrikes);
        public static OrbitalEscalationState Restore(int level,double progress,double common,int[] ids,double[] cooldowns,PlayableProfileData p,bool provingGround=false)
        {
            bool compact=p.orbitalProgressionVersion==OrbitalAbilities.CompactProgressionVersion;
            var state=new OrbitalEscalationState(provingGround,compact){Level=level,Progress=progress,CommonRemaining=common};
            int maximum=state.MaxLevel;
            if(level<0||level>maximum||!Finite(progress)||progress<0||level==maximum&&progress!=0||level<maximum&&progress>=OrbitalAbilities.Threshold(level,p)||!Finite(common)||common<0||ids.Length!=StoredChoiceCount||cooldowns.Length!=StoredChoiceCount)throw new ArgumentException("Invalid orbital state.");
            bool gap=false;
            if(provingGround){
                var expected=Enumerable.Range(0,StoredChoiceCount).Select(state.Choice).ToArray();
                bool legacy=ids[StoredChoiceCount-1]==0&&ids.Take(StoredChoiceCount-1).SequenceEqual(expected.Take(StoredChoiceCount-1));
                if(level!=maximum||progress!=0||common!=0||!legacy&&!ids.SequenceEqual(expected))throw new ArgumentException("Invalid proving ground abilities.");
                for(int i=0;i<StoredChoiceCount;i++){if(!Finite(cooldowns[i])||cooldowns[i]<0||ids[i]==0&&cooldowns[i]!=0)throw new ArgumentException("Invalid orbital timer.");state.remaining[i]=cooldowns[i];}
                return state;
            }
            for(int i=0;i<StoredChoiceCount;i++){
                int id=ids[i];if(!Finite(cooldowns[i])||cooldowns[i]<0||id==0&&cooldowns[i]!=0)throw new ArgumentException("Invalid orbital timer.");
                if(id==0){gap=true;continue;}
                if(!compact&&i==4&&id==7&&!gap&&i<level){state.choices[i]=OrbitalAbilities.NuclearStrike;state.remaining[i]=cooldowns[i];continue;}
                if(gap||i>=level||!state.Choose(id))throw new ArgumentException("Invalid orbital choice.");state.remaining[i]=cooldowns[i];
            }
            return state;
        }
        private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    }
    public static class OrbitalProductionBoost
    {
        public static bool Income(PlayableBuildingKind kind) =>
            kind == PlayableBuildingKind.Mine || kind == PlayableBuildingKind.Refinery;

        public static bool Production(PlayableBuildingKind kind) =>
            kind == PlayableBuildingKind.Factory || kind == PlayableBuildingKind.Hangar ||
            kind == PlayableBuildingKind.ScientificCenter;

        public static double HealthFloor(PlayableBuildingKind kind, PlayableProfileData data) =>
            (kind == PlayableBuildingKind.Mine ? data.orbitalBoostMineHealthPercent : data.orbitalBoostBuildingHealthPercent) / 100;

        public static double ExtraIncome(PlayableProfile profile, PlayableProfileData data, PlayableBuildingKind kind,
            bool upgraded, double? reserve)
        {
            double seconds = data.orbitalBoostSeconds;
            if (kind == PlayableBuildingKind.Mine)
                return TerritoryRules.MineIncome(profile, reserve, seconds * data.orbitalBoostIncomeMultiplier) -
                    TerritoryRules.MineIncome(profile, reserve, seconds);
            double rate = (upgraded ? profile.RefineryUpgradedIncome : profile.RefineryIncomePerPeriod) / profile.IncomePeriodSeconds;
            return rate * seconds * (data.orbitalBoostIncomeMultiplier - 1);
        }
    }
    public static class OrbitalMinePlacement
    {
        public const double DefaultTankBodies = 5;

        public static bool Blocks(PlayableProfile profile, NavPoint point, NavPoint enemy, NavLocation? enemyLocation)
        {
            double x = point.X - enemy.X, z = point.Z - enemy.Z;
            double radius = profile.MineEnemyClearanceRadius;
            if (x*x + z*z > radius*radius) return false;
            var map = profile.AuthoredMap;
            if (map == null) return true;
            if (!map.TryLocate(point, 0, out var mineLocation)) return false;
            if (enemyLocation.HasValue) return map.CompatibleCombatSurface(mineLocation, enemyLocation.Value);
            return map.TryLocate(enemy, 0, out var resolvedEnemy) && map.CompatibleCombatSurface(mineLocation, resolvedEnemy);
        }
    }

    public static class OrbitalProfileMetadata
    {
        private static PlayableProfileField F(string name,string label,string unit,double min,double max,double step,Func<PlayableProfileData,double> read)=>new PlayableProfileField("orbital."+name,"Орбитальная поддержка · orbital-escalation-v1",label,"Параметр эскалации и восстановления орбитальной поддержки.",unit,min,max,step,read,name);
        private static PlayableProfileField V(string name,string label,string description,string unit,double min,double max,double step,Func<PlayableProfileData,double> read)=>new PlayableProfileField("orbital."+name,"Орбитальная поддержка · orbital-escalation-v1",label,description,unit,min,max,step,read,name);
        // Optional binding/term extension keeps default world payloads byte-compatible.
        public static readonly PlayableProfileField MineEnemyClearanceField =
            V(nameof(PlayableProfileData.orbitalMineEnemyClearanceTankBodies), "Отступ от врага",
                "orbital-trap-mine-v1: Минимальный отступ от центра вражеского наземного юнита в диаметрах коллизии Вепря.",
                "корп.", 1, 20, .5, p => p.orbitalMineEnemyClearanceTankBodies == 0 ? OrbitalMinePlacement.DefaultTankBodies : p.orbitalMineEnemyClearanceTankBodies);
        public static readonly IReadOnlyList<PlayableProfileField> BoostFields=new[]{
            V(nameof(PlayableProfileData.orbitalBoostSeconds),"Длительность","Игровое время ускорения.","с",.1,120,.1,p=>p.orbitalBoostSeconds),
            V(nameof(PlayableProfileData.orbitalBoostIncomeMultiplier),"Множитель дохода","Множитель дохода и расхода шахтного запаса.","×",1,10,.1,p=>p.orbitalBoostIncomeMultiplier),
            V(nameof(PlayableProfileData.orbitalBoostProductionMultiplier),"Множитель работы очереди","Множитель прогресса оплаченного заказа.","×",1,10,.1,p=>p.orbitalBoostProductionMultiplier),
            V(nameof(PlayableProfileData.orbitalBoostMineHealthPercent),"Порог HP шахты","Минимальное здоровье от износа; боевой урон может опустить его ниже.","%",1,99,1,p=>p.orbitalBoostMineHealthPercent),
            V(nameof(PlayableProfileData.orbitalBoostBuildingHealthPercent),"Порог HP остальных зданий","Минимальное здоровье от износа; боевой урон может опустить его ниже.","%",1,99,1,p=>p.orbitalBoostBuildingHealthPercent)
        };
        public static readonly IReadOnlyList<PlayableProfileField> StasisFields=new[]{
            V(nameof(PlayableProfileData.orbitalStasisRadius),"Радиус","Горизонтальный радиус наземной цели и радиус сферы при наведении на Ястреба.","м",.1,20,.1,p=>p.orbitalStasisRadius),
            V(nameof(PlayableProfileData.orbitalStasisSeconds),"Длительность","Игровое время остановки и неуязвимости.","с",.1,60,.1,p=>p.orbitalStasisSeconds),
            V(nameof(PlayableProfileData.orbitalStasisTargets),"Лимит целей","Максимум ближайших юнитов за одно применение.","шт",1,4,1,p=>p.orbitalStasisTargets)
        };
        public static readonly IReadOnlyList<PlayableProfileField> RepairFields = new[]{
            V(nameof(PlayableProfileData.orbitalNanobotsRadius),"Радиус","Радиус зоны и воздушной полусферы.","м",.1,20,.1,p=>p.orbitalNanobotsRadius),
            V(nameof(PlayableProfileData.orbitalNanobotsSeconds),"Длительность","Игровое время действия зоны.","с",.1,60,.1,p=>p.orbitalNanobotsSeconds),
            V(nameof(PlayableProfileData.orbitalNanobotsHealthPerSecond),"Скорость ремонта","Восстановление здоровья каждому раненому юниту.","HP/с",.1,100,.1,p=>p.orbitalNanobotsHealthPerSecond)
        };
        public static readonly IReadOnlyList<PlayableProfileField> ReinforcementFields=new[]{
            V(nameof(PlayableProfileData.orbitalReinforcementCostMultiplier),"Коэффициент стоимости","Множитель цены производства всей волны до округления вверх до десятка.","×",1,5,.05,p=>p.orbitalReinforcementCostMultiplier),
            V(nameof(PlayableProfileData.orbitalReinforcementSeconds),"Время материализации","Время от подтверждения до готовности призванных юнитов.","с",.1,30,.1,p=>p.orbitalReinforcementSeconds),
            V(nameof(PlayableProfileData.orbitalReinforcementClearance),"Отступ от врага","Минимальный зазор от вражеского наземного юнита или здания до корпуса подкрепления.","м",0,50,.1,p=>p.orbitalReinforcementClearance)
        };
        public static readonly IReadOnlyList<PlayableProfileField> Fields=new[]{
            V("orbitalSystemFailureRadius","Сбой систем · радиус","Область поиска по центрам: два ближайших больших здания и все вражеские башни.","м",.1,20,.1,p=>p.orbitalSystemFailureRadius),
            V("orbitalSystemFailureSeconds","Сбой систем · длительность","Время заморозки действий и прогресса здания.","с",.1,120,.1,p=>p.orbitalSystemFailureSeconds),
            V("orbitalSystemFailureMaxTargets","Сбой систем · больших зданий","Максимум ближайших больших зданий; башни не входят в лимит.","шт",1,10,1,p=>p.orbitalSystemFailureMaxTargets),
            V("orbitalMineCapacity","Лимит мин","orbital-trap-mine-v1: Максимум одновременно установленных мин; новая заменяет старейшую.","шт",1,3,1,p=>p.orbitalMineCapacity),
            V("orbitalMineVision","Обзор мины","orbital-trap-mine-v1: Радиус постоянного обзора владельца и команды.","м",0.5,30,0.5,p=>p.orbitalMineVision),
            V("orbitalMineDirectDamage","Урон при наезде","orbital-trap-mine-v1: Полный урон сработавшему юниту; сплэш ему повторно не наносится.","HP",1,1000,1,p=>p.orbitalMineDirectDamage),
            V("orbitalMineSplashDamage","Площадной урон","orbital-trap-mine-v1: Урон остальным целям, включая своих и союзников.","HP",1,1000,1,p=>p.orbitalMineSplashDamage),
            V("orbitalMineBlastRadius","Радиус взрыва","orbital-trap-mine-v1: Общий радиус игрового урона, видимой границы и preview.","м",0.1,20,0.1,p=>p.orbitalMineBlastRadius),
            V("orbitalMineModelRadius","Размер мины","orbital-trap-mine-v1: Радиус низкого корпуса и физического контакта с юнитом.","м",0.1,1,0.05,p=>p.orbitalMineModelRadius),
            V("orbitalMineModelHeight","Высота корпуса","orbital-trap-mine-v1: Высота модели относительно местности.","м",0.05,0.5,0.01,p=>p.orbitalMineModelHeight),
            V("orbitalMineEffectSeconds","Длительность взрыва","orbital-trap-mine-v1: Время короткой вспышки, пыли и осколков.","с",0.1,2,0.05,p=>p.orbitalMineEffectSeconds),
            V("orbitalMineEffectHeight","Высота взрыва","orbital-trap-mine-v1: Высота низкой вспышки и подброса осколков.","м",0.1,2,0.05,p=>p.orbitalMineEffectHeight),
            V("orbitalMineRimWidth","Толщина границы взрыва","orbital-trap-mine-v1: Ширина хорошо видимой наземной границы внутри игрового радиуса.","м",0.01,0.2,0.005,p=>p.orbitalMineRimWidth),
            V("orbitalMinePreviewWidth","Толщина preview","orbital-trap-mine-v1: Экранная толщина линии потенциального взрыва.","px",1,6,0.5,p=>p.orbitalMinePreviewWidth),
            V("orbitalMineDustOpacity","Плотность пыли","orbital-trap-mine-v1: Яркость низкой затухающей пыли.","доля",0,1,0.05,p=>p.orbitalMineDustOpacity),
            V("orbitalMineFragmentCount","Число осколков","orbital-trap-mine-v1: Количество короткоживущих частиц взрыва.","шт",3,24,1,p=>p.orbitalMineFragmentCount),
            V("orbitalMineFragmentSize","Размер осколков","orbital-trap-mine-v1: Размер видимых фрагментов корпуса.","м",0.01,0.15,0.005,p=>p.orbitalMineFragmentSize),
            V("orbitalHackSeconds","Перезарядка взлома","Личная перезарядка Взлома управления; не меняет сканирование, мину и общий интервал.","с",1,600,1,p=>p.orbitalHackSeconds),
            V("orbitalHackHealthPercent","Порог здоровья для взлома","Захват разрешён строго ниже указанной доли максимального HP прежнего владельца.","%",1,100,1,p=>p.orbitalHackHealthPercent),
            F("orbitalScanRadius","Радиус сканирования","м",1,100,.5,p=>p.orbitalScanRadius),F("orbitalScanSeconds","Длительность сканирования","с",.1,30,.1,p=>p.orbitalScanSeconds),
            F("orbitalScanSweepSeconds","Оборот сектора","с",.1,10,.1,p=>p.orbitalScanSweepSeconds),F("orbitalScanSectorDegrees","Ширина сектора","град",5,180,1,p=>p.orbitalScanSectorDegrees),
            F("orbitalScanLineWidth","Толщина эффекта","px",.5,5,.5,p=>p.orbitalScanLineWidth),F("orbitalScanOpacity","Яркость эффекта","доля",.1,1,.05,p=>p.orbitalScanOpacity),
            V("orbitalScanCoreDegrees","Ядро луча","Угловая ширина мягкого яркого ядра сканирующего луча.","град",.5,10,.5,p=>p.orbitalScanCoreDegrees),
            V("orbitalScanTrailOpacity","Яркость шлейфа","Плотность плавно затухающего следа за вращающимся лучом.","доля",0,1,.01,p=>p.orbitalScanTrailOpacity),
            V("orbitalScanWaveOpacity","Яркость волн","Плотность расходящихся концентрических волн на поверхности.","доля",0,1,.01,p=>p.orbitalScanWaveOpacity),
            V("orbitalScanWaveSeconds","Период волн","Время прохода кольцевой волны от центра к границе сканирования.","с",.2,5,.05,p=>p.orbitalScanWaveSeconds),
            V("orbitalScanFadeSeconds","Появление и угасание","Длительность мягкого появления и исчезновения эффекта; не меняет время обзора.","с",.05,1,.05,p=>p.orbitalScanFadeSeconds),
            V("orbitalScanRimOpacity","Яркость границы","Плотность вспомогательной прерывистой границы области.","доля",0,1,.01,p=>p.orbitalScanRimOpacity),
            F("orbitalCardWidth","Ширина карточки выбора","px",180,420,1,p=>p.orbitalCardWidth),F("orbitalCardHeight","Высота карточки выбора","px",200,440,1,p=>p.orbitalCardHeight),F("orbitalCardArtworkSize","Иконка в карточке","px",90,280,1,p=>p.orbitalCardArtworkSize),
            F("orbitalUiRingWidth","Толщина шкалы уровня","px",1,8,.5,p=>p.orbitalUiRingWidth),F("orbitalUiCornerRadius","Скругление иконок","px",0,16,1,p=>p.orbitalUiCornerRadius),F("orbitalUiPulseSeconds","Период пульсации выбора","с",.3,5,.1,p=>p.orbitalUiPulseSeconds),F("orbitalUiCoolingArtworkOpacity","Яркость иконки при перезарядке","доля",0,1,.05,p=>p.orbitalUiCoolingArtworkOpacity),
            F("orbitalPanelWidth","Ширина панели","px",60,120,1,p=>p.orbitalPanelWidth),F("orbitalIconSize","Размер иконки","px",24,64,1,p=>p.orbitalIconSize),F("orbitalLevelFontSize","Размер уровня","px",24,56,1,p=>p.orbitalLevelFontSize),
            V("orbitalThresholdMax","Урон от V до MAX","Самостоятельное количество снятого здоровья для перехода V → MAX. Не зависит от порогов других уровней.","HP",1,1000000,.1,p=>p.orbitalThresholdMax),
            V("orbitalUiMaxFontScale","Размер надписи MAX","Размер MAX относительно обычной цифры; сохраняет текст внутри медальона.","доля",.3,.8,.05,p=>p.orbitalUiMaxFontScale),
            F("orbitalThreshold1","Урон до I","HP",1,100000,1,p=>p.orbitalThreshold1),F("orbitalThreshold2","Урон от I до II","HP",1,100000,1,p=>p.orbitalThreshold2),F("orbitalThreshold3","Урон от II до III","HP",1,100000,1,p=>p.orbitalThreshold3),F("orbitalThreshold4","Урон от III до IV","HP",1,100000,1,p=>p.orbitalThreshold4),F("orbitalThreshold5","Урон от IV до V","HP",1,100000,1,p=>p.orbitalThreshold5),
            F("orbitalBasicSeconds","Базовая перезарядка","с",1,600,1,p=>p.orbitalBasicSeconds),F("orbitalAdvancedSeconds","Продвинутая перезарядка","с",1,600,1,p=>p.orbitalAdvancedSeconds),F("orbitalUltimateSeconds","Ультимативная перезарядка","с",1,600,1,p=>p.orbitalUltimateSeconds),F("orbitalCommonSeconds","Общий интервал","с",0,60,1,p=>p.orbitalCommonSeconds),F("orbitalFinalSpeed","Скорость восстановления на MAX","×",1,10,.1,p=>p.orbitalFinalSpeed)
        };
    }
}
