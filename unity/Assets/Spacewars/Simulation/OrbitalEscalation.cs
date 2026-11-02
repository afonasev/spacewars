using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Existing IDs remain stable; Control Hack is an additional basic ability at ID 8.
    public static class OrbitalAbilities
    {
        public const int ControlHack=8;
        public static int Tier(int id)=>id==ControlHack||id>=1&&id<=2?1:id>=3&&id<=5?2:id>=6&&id<=7?3:0;
        public static string Name(int id)=>id==ControlHack?"Взлом управления":id==1?"Сканирование":id==2?"Мина-ловушка":"Поддержка "+(Tier(id)==1?"I":Tier(id)==2?"II":"III")+" · "+(id==1||id==3||id==6?"А":id==5?"В":"Б");
        public static double Duration(int id,PlayableProfileData p)=>id==ControlHack?p.orbitalHackSeconds:Tier(id)==1?p.orbitalBasicSeconds:Tier(id)==2?p.orbitalAdvancedSeconds:p.orbitalUltimateSeconds;
        public static double Threshold(int level,PlayableProfileData p)=>level==0?p.orbitalThreshold1:level==1?p.orbitalThreshold2:level==2?p.orbitalThreshold3:level==3?p.orbitalThreshold4:level==4?p.orbitalThreshold5:level==5?p.orbitalThreshold5*p.orbitalMaxThresholdMultiplier:0;
    }
    public sealed class OrbitalAbilitySnapshot
    {
        public readonly int Id;public readonly double Remaining,Duration,Recovery;
        public OrbitalAbilitySnapshot(int id,double remaining,double duration,double speed){Id=id;Duration=duration;Recovery=remaining;Remaining=remaining/speed;}
    }
    public sealed class OrbitalEscalationSnapshot
    {
        public static readonly OrbitalEscalationSnapshot Empty=new OrbitalEscalationSnapshot(0,0,250,0,0,Array.Empty<OrbitalAbilitySnapshot>(),Array.Empty<int>());
        public readonly int Level,PendingLevel;public readonly double Progress,Threshold,CommonRemaining;
        public readonly IReadOnlyList<OrbitalAbilitySnapshot> Abilities;public readonly IReadOnlyList<int> Options;
        public OrbitalEscalationSnapshot(int level,double progress,double threshold,int pending,double common,OrbitalAbilitySnapshot[] abilities,int[] options)
        {Level=level;Progress=progress;Threshold=threshold;PendingLevel=pending;CommonRemaining=common;Abilities=Array.AsReadOnly(abilities);Options=Array.AsReadOnly(options);}
    }
    public sealed class OrbitalEscalationState
    {
        public const int MaxLevel=6,ChoiceCount=5;
        public int Level{get;private set;}public double Progress{get;private set;}public double CommonRemaining{get;private set;}
        private readonly int[] choices=new int[ChoiceCount];private readonly double[] remaining=new double[ChoiceCount];
        public int Choice(int slot)=>choices[slot];public double Recovery(int slot)=>remaining[slot];
        public int PendingLevel=>Enumerable.Range(0,Math.Min(Level,ChoiceCount)).Where(i=>choices[i]==0).Select(i=>i+1).FirstOrDefault();
        public int[] Options()
        {
            int stage=PendingLevel;
            return stage==1||stage==2?new[]{1,2,OrbitalAbilities.ControlHack}.Where(id=>!choices.Contains(id)).ToArray():stage==3||stage==4?new[]{3,4,5}.Where(id=>!choices.Contains(id)).ToArray():stage==5?new[]{6,7}:Array.Empty<int>();
        }
        public void Damage(double amount,PlayableProfileData p)
        {
            if(amount<=0||double.IsNaN(amount)||double.IsInfinity(amount)||Level==MaxLevel)return;
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
            remaining[slot]=OrbitalAbilities.Duration(id,p);CommonRemaining=p.orbitalCommonSeconds;return true;
        }
        public void Advance(double dt,PlayableProfileData p)
        {if(dt<=0)return;CommonRemaining=Math.Max(0,CommonRemaining-dt);for(int i=0;i<ChoiceCount;i++)remaining[i]=Math.Max(0,remaining[i]-dt*(Level==MaxLevel?p.orbitalFinalSpeed:1));}
        public OrbitalEscalationSnapshot Snapshot(PlayableProfileData p)=>new OrbitalEscalationSnapshot(Level,Progress,OrbitalAbilities.Threshold(Level,p),PendingLevel,CommonRemaining,
            Enumerable.Range(0,ChoiceCount).Where(i=>choices[i]!=0).Select(i=>new OrbitalAbilitySnapshot(choices[i],remaining[i],OrbitalAbilities.Duration(choices[i],p),Level==MaxLevel?p.orbitalFinalSpeed:1)).ToArray(),Options());
        public static OrbitalEscalationState Restore(int level,double progress,double common,int[] ids,double[] cooldowns,PlayableProfileData p)
        {
            if(level<0||level>MaxLevel||!Finite(progress)||progress<0||level==MaxLevel&&progress!=0||level<MaxLevel&&progress>=OrbitalAbilities.Threshold(level,p)||!Finite(common)||common<0||ids.Length!=ChoiceCount||cooldowns.Length!=ChoiceCount)throw new ArgumentException("Invalid orbital state.");
            var state=new OrbitalEscalationState{Level=level,Progress=progress,CommonRemaining=common};bool gap=false;
            for(int i=0;i<ChoiceCount;i++){
                int id=ids[i];if(!Finite(cooldowns[i])||cooldowns[i]<0||id==0&&cooldowns[i]!=0)throw new ArgumentException("Invalid orbital timer.");
                if(id==0){gap=true;continue;}
                if(gap||i>=level||!state.Choose(id))throw new ArgumentException("Invalid orbital choice.");state.remaining[i]=cooldowns[i];
            }
            return state;
        }
        private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    }
    public static class OrbitalProfileMetadata
    {
        private static PlayableProfileField F(string name,string label,string unit,double min,double max,double step,Func<PlayableProfileData,double> read)=>new PlayableProfileField("orbital."+name,"Орбитальная поддержка · orbital-escalation-v1",label,"Параметр эскалации и восстановления орбитальной поддержки.",unit,min,max,step,read,name);
        private static PlayableProfileField V(string name,string label,string description,string unit,double min,double max,double step,Func<PlayableProfileData,double> read)=>new PlayableProfileField("orbital."+name,"Орбитальная поддержка · orbital-escalation-v1",label,description,unit,min,max,step,read,name);
        public static readonly IReadOnlyList<PlayableProfileField> Fields=new[]{
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
            V("orbitalMaxThresholdMultiplier","Порог MAX относительно V","Во сколько раз XP для V → MAX больше XP для IV → V.","×",1,10,.1,p=>p.orbitalMaxThresholdMultiplier),
            V("orbitalUiMaxFontScale","Размер надписи MAX","Размер MAX относительно обычной цифры; сохраняет текст внутри медальона.","доля",.3,.8,.05,p=>p.orbitalUiMaxFontScale),
            F("orbitalThreshold1","Урон до I","HP",1,100000,1,p=>p.orbitalThreshold1),F("orbitalThreshold2","Урон от I до II","HP",1,100000,1,p=>p.orbitalThreshold2),F("orbitalThreshold3","Урон от II до III","HP",1,100000,1,p=>p.orbitalThreshold3),F("orbitalThreshold4","Урон от III до IV","HP",1,100000,1,p=>p.orbitalThreshold4),F("orbitalThreshold5","Урон от IV до V","HP",1,100000,1,p=>p.orbitalThreshold5),
            F("orbitalBasicSeconds","Базовая перезарядка","с",1,600,1,p=>p.orbitalBasicSeconds),F("orbitalAdvancedSeconds","Продвинутая перезарядка","с",1,600,1,p=>p.orbitalAdvancedSeconds),F("orbitalUltimateSeconds","Ультимативная перезарядка","с",1,600,1,p=>p.orbitalUltimateSeconds),F("orbitalCommonSeconds","Общий интервал","с",0,60,1,p=>p.orbitalCommonSeconds),F("orbitalFinalSpeed","Скорость восстановления на MAX","×",1,10,.1,p=>p.orbitalFinalSpeed)
        };
    }
}
