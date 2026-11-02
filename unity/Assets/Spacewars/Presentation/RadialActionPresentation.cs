using System;
using System.Linq;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    // Pure owner-projection presentation. Commands and gesture eligibility stay in WorldActions.
    public sealed class RadialActionPresentation
    {
        public string Title,Description,Note,Artwork;
        public string[] Stats=Array.Empty<string>();
        public bool Upgrade,AssignmentComplete;
        public int? Count;public int ReplaceCount,AddCount,ResultCount;
        public RadialCompositionItem[] Composition=Array.Empty<RadialCompositionItem>();
        static string N(double n)=>n.ToString("0.##");
        public static bool Upgraded(PlayableEntityKind kind,IEnumerable<PlayableResearchOrderSnapshot> research)=>research?.Any(r=>r.Complete&&r.Kind==Specialization(kind))==true;
        public static PlayableResearchKind Specialization(PlayableEntityKind kind)=>kind==PlayableEntityKind.Tank?PlayableResearchKind.TankChassis:kind==PlayableEntityKind.Explorer?PlayableResearchKind.ExplorerAssaultGuns:kind==PlayableEntityKind.Shkval?PlayableResearchKind.ShkvalGuidance:PlayableResearchKind.HawkSalvoSystems;
        public static string UnitArtwork(PlayableEntityKind kind,bool upgraded)=>kind==PlayableEntityKind.Tank?(upgraded?"tankHybridUpgraded":"tankHybrid"):kind==PlayableEntityKind.Explorer?(upgraded?"explorerUpgraded":"explorer"):kind==PlayableEntityKind.Shkval?(upgraded?"shkval-guidance-3":"shkval"):(upgraded?"hawkUpgraded":"hawk");
        public static string ResearchArtwork(PlayableResearchKind kind)
        {
            foreach(var unit in new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval,PlayableEntityKind.Hawk})if(Specialization(unit)==kind)return UnitArtwork(unit,true);
            return OrbitalPrecision.ResearchGlyph(kind);
        }
        static string[] Lines(string text)=>text.Split('\n').Where(s=>!string.IsNullOrWhiteSpace(s)).ToArray();
        static void Describe(RadialActionPresentation card,string text)
        {
            var lines=Lines(text);card.Title=lines[0];card.Description=lines.Length>1?lines[1]:"";
            card.Stats=lines.Skip(2).Where(s=>!s.StartsWith("Стоимость:")&&s!="Бесплатно").ToArray();
        }
        public static OfflinePadAction[] Decorate(OfflinePadAction[] actions,PlayableSnapshot view,PlayableProfile p,PlayableBuildingSnapshot building=null,bool free=false)
        {
            foreach(var a in actions)
            {
                var c=new RadialActionPresentation{Title=a.Label,Description=a.Detail};a.Presentation=c;
                string id=a.Sector.Id;
                if(a.Sector.Hold=="repeat")
                {
                    var kind=a.HoldCommand?.Unit??(PlayableEntityKind)Enum.Parse(typeof(PlayableEntityKind),id.Split(':').Last(),true);string text=PlayableMenuDescriptions.Unit(p,kind,view.OwnerResearch,freeEconomy:free);
                    Describe(c,text);c.Artwork=UnitArtwork(kind,Upgraded(kind,view.OwnerResearch));c.Upgrade=Upgraded(kind,view.OwnerResearch);
                    c.Note=a.Detail.StartsWith(text)?a.Detail.Substring(text.Length).Trim(' ','·','\n'):null;
                }
                else if(id.StartsWith("build:"))
                {
                    var kind=(PlayableBuildingKind)Enum.Parse(typeof(PlayableBuildingKind),id.Split(':').Last());
                    double hp=TerritoryRules.Health(p,kind)*(1+PlayableResearchRules.BuildingBonus(p,view.OwnerResearch)/100);
                    Describe(c,PlayableMenuDescriptions.Building(p,kind,freeEconomy:free,maxHealth:hp));c.Artwork=OrbitalPrecision.BuildingGlyph(kind);
                    var defense=PlayableMenuDescriptions.DefenseStats(p,kind,view.OwnerResearch);
                    if(defense.Length>0)
                    {
                        c.Stats=c.Stats.Concat(defense).ToArray();
                        c.Description=kind==PlayableBuildingKind.SentryTurret?"Спаренный пулемёт против наземных и воздушных целей.":
                            kind==PlayableBuildingKind.GaussTower?"Пробивающий луч по наземным целям. После каждой цели остаётся "+N(p.GaussPierceRetention*100)+"% урона.":
                            "Ремонтирует один свой наземный или воздушный юнит, в том числе в бою.";
                    }
                    if(!a.Sector.Enabled)c.Note=string.Join("\n",a.BlockedReasons);
                }
                else if(id.EndsWith(":repair")&&building!=null)
                {
                    c.Description=building.PrivateState?.Lifecycle?.Repairing==true?"Остановить восстановление здания.":"Восстановить повреждённое здание.";
                    c.Stats=new[]{"Здоровье: "+N(building.Health)+" / "+N(TerritoryRules.MaximumHealth(p,building))};c.Note=building.PrivateState?.Lifecycle?.Repairing==true||!a.Sector.Enabled?a.Detail:"Восстановление оплачивается по мере ремонта.";
                }
                else if((id.EndsWith(":sale")||id.EndsWith(":remove"))&&building!=null)
                {
                    bool center=TerritoryRules.Center(building.Kind);
                    c.Description=center?"Удалит этот центр и все зависимые здания.":"Удалит выбранное здание.";
                    if(center&&building.PrivateState?.Lifecycle?.LastCenter==true)c.Note="Последний центр: продажа приведёт к поражению.";
                    if(!a.Sector.Enabled)c.Note=building.PrivateState?.Lifecycle?.SaleBlockedReason??"Продажа недоступна: здание в бою.";
                    c.Stats=a.Price.HasValue?new[]{"Возврат: "+N(a.Price.Value)+" кредитов"}:Array.Empty<string>();
                }
                else if(id=="rally")c.Description="Новые юниты направятся к указанной точке после выпуска.";
                else if(id.EndsWith(":producer-upgrade"))
                {
                    c.Artwork=OrbitalPrecision.BuildingArtwork(building.Kind,true);c.Upgrade=true;c.Description="Улучшение в общей очереди производства.";
                    c.Stats=new[]{"Скорость: ×"+N(p.UpgradedProducerSpeedMultiplier),"Время: "+N(free?p.ProvingGroundActionSeconds:p.ProducerUpgradeSeconds(building.Kind))+" с"};c.Note=a.Detail.Split('\n').Last();
                }
                else if(id.EndsWith(":upgrade"))
                {
                    c.Artwork="refineryUpgraded";c.Upgrade=true;c.Description="Увеличивает доход завода переработки.";
                    c.Stats=new[]{"Доход: "+N(p.RefineryIncomePerPeriod)+" → "+N(p.RefineryUpgradedIncome),"Период: "+N(p.IncomePeriodSeconds)+" с","Исследование: "+N(free?p.ProvingGroundActionSeconds:p.RefineryUpgradeSeconds)+" с"};
                    c.Note=a.Detail.Split('\n').Last();
                }
                else if(building?.Kind==PlayableBuildingKind.ScientificCenter)
                {
                    var kind=id.EndsWith(":tank-chassis")?PlayableResearchKind.TankChassis:id.EndsWith(":explorer-assault-guns")?PlayableResearchKind.ExplorerAssaultGuns:id.EndsWith(":shkval-guidance")?PlayableResearchKind.ShkvalGuidance:(PlayableResearchKind)Enum.Parse(typeof(PlayableResearchKind),id.Split(':').Last());
                    c.Description=PlayableResearchRules.Description(p,kind);c.Artwork=OrbitalPrecision.ResearchGlyph(kind);c.Upgrade=PlayableResearchRules.Local(kind);
                    var specialized=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval,PlayableEntityKind.Hawk}.Where(k=>Specialization(k)==kind).ToArray();
                    var stats=new List<string>();
                    if(specialized.Length>0)
                    {
                        var unit=specialized[0];c.Artwork=UnitArtwork(unit,true);c.Upgrade=true;
                        if(unit==PlayableEntityKind.Tank)stats.Add("Скорость: "+N(PlayableUnitRules.Speed(p,unit,false))+" → "+N(PlayableUnitRules.Speed(p,unit,true))+" м/с");
                        if(unit==PlayableEntityKind.Explorer)stats.Add("Выстрелов в очереди: "+N(p.ExplorerBurstSize)+" → "+N(p.ExplorerAssaultBurstSize));
                        if(unit==PlayableEntityKind.Shkval)stats.Add("Дальность: "+N(PlayableUnitRules.Range(p,unit,false))+" → "+N(PlayableUnitRules.Range(p,unit,true))+" м");
                        if(unit==PlayableEntityKind.Hawk){stats.Add("Перезарядка: "+N(p.HawkReloadSeconds)+" → "+N(p.HawkUpgradedReloadSeconds)+" с");stats.Add("Интервал ракет: "+N(p.HawkSalvoDelay)+" → "+N(p.HawkUpgradedSalvoDelay)+" с");}
                    }
                    if(PlayableResearchRules.Global(kind)){var previous=PlayableResearchRules.Previous(kind);double before=previous.HasValue?PlayableResearchRules.Bonus(p,previous.Value):0;stats.Add((PlayableResearchRules.Weapon(kind)?"Урон":"Здоровье")+": +"+N(before)+"% → +"+N(PlayableResearchRules.Bonus(p,kind))+"%");}
                    if(PlayableResearchRules.Concrete(kind)){var previous=PlayableResearchRules.Previous(kind);stats.Add("Здоровье зданий: +"+N(previous.HasValue?PlayableResearchRules.Bonus(p,previous.Value):0)+"% → +"+N(PlayableResearchRules.Bonus(p,kind))+"%");}
                    stats.Add("Исследование: "+N(free?p.ProvingGroundActionSeconds:PlayableResearchRules.Duration(p,kind))+" с");c.Stats=stats.ToArray();
                    var availability=view.ResearchAvailability.FirstOrDefault(r=>r.Kind==kind);c.Note=a.Sector.Enabled?"Доступно к исследованию":availability?.BlockedReason??"Недостаточно кредитов";
                }
            }
            return actions;
        }
    }
}
