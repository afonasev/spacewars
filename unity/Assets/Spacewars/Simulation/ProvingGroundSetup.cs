using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    // Experiment recipe. Every running authority receives its own immutable configuration copy.
    public sealed class ProvingGroundSetup
    {
        public const string Identity="balance-proving-ground-v1";
        public PlayableProfile Profile {get;}
        public List<OfflineScenarioUnit> Units {get;}=new List<OfflineScenarioUnit>();
        public List<OfflineScenarioBuilding> Buildings {get;}=new List<OfflineScenarioBuilding>();
        private readonly HashSet<string> upgradedBuildings=new HashSet<string>();
        public bool BuildingUpgraded(int site,int slot)=>upgradedBuildings.Contains(site+":"+slot);
        public string UpgradeBuilding(int site,int slot)
        {
            var building=Buildings.FirstOrDefault(b=>b.SiteId==site&&b.SlotId==slot);
            if(building==null||building.Kind!=PlayableBuildingKind.Refinery&&building.Kind!=PlayableBuildingKind.ScientificCenter&&building.Kind!=PlayableBuildingKind.Factory&&building.Kind!=PlayableBuildingKind.Hangar)return "У этого здания нет улучшения.";
            if(!upgradedBuildings.Add(site+":"+slot))return "Здание уже улучшено.";
            return null;
        }
        public void RemoveBuilding(int site,int slot){Buildings.RemoveAll(b=>b.SiteId==site&&b.SlotId==slot);upgradedBuildings.Remove(site+":"+slot);}
        private readonly HashSet<PlayableResearchKind>[] research={new HashSet<PlayableResearchKind>(),new HashSet<PlayableResearchKind>()};
        public ProvingGroundSetup(PlayableProfile source){Profile=PlayableProfile.Create(source.CopyData(),new ProvingGroundMap(),source.DisplayName);}
        public IReadOnlyCollection<PlayableResearchKind> Research(int side)=>research[side].OrderBy(x=>(int)x).ToArray();
        public void SetResearch(int side,PlayableResearchKind kind,bool enabled)
        {
            if(!Enum.IsDefined(typeof(PlayableResearchKind),kind))throw new ArgumentException("Unknown research");
            if(PlayableResearchRules.Local(kind))throw new ArgumentException("Upgrade the selected building instead",nameof(kind));
            if(enabled){for(PlayableResearchKind? next=kind;next.HasValue;next=PlayableResearchRules.Previous(next.Value))research[side].Add(next.Value);}
            else {research[side].Remove(kind);bool Depends(PlayableResearchKind k){for(var previous=PlayableResearchRules.Previous(k);previous.HasValue;previous=PlayableResearchRules.Previous(previous.Value))if(previous.Value==kind)return true;return false;}foreach(var next in research[side].Where(Depends).ToArray())research[side].Remove(next);}
        }
        public double ArmyCost(int side)=>Units.Where(u=>u.LogicalPlayer==side+1).Sum(u=>(double)PlayableUnitRules.Cost(Profile,u.Kind));
        public double ResearchCost(int side)=>research[side].Sum(k=>PlayableResearchRules.Cost(Profile,k));
        public string PlaceUnit(int side,PlayableEntityKind kind,NavPoint point)
        {
            if(side<0||side>1)return "Неизвестная сторона.";
            double radius=PlayableUnitRules.Radius(Profile,kind);
            if(!PlayableUnitRules.Supported(kind)||double.IsNaN(point.X)||double.IsNaN(point.Z)||Math.Abs(point.X)+radius>=Profile.ArenaHalfExtent||Math.Abs(point.Z)+radius>=Profile.ArenaHalfExtent||(side==0?point.X+radius>0:point.X-radius<0))return "Выберите свободную точку на своей половине.";
            if(Units.Any(u=>(u.Kind==PlayableEntityKind.Hawk)==(kind==PlayableEntityKind.Hawk)&&Distance(u.Position,point)<radius+PlayableUnitRules.Radius(Profile,u.Kind)))return "Здесь уже стоит юнит.";
            if(kind==PlayableEntityKind.Hawk){Units.Add(new OfflineScenarioUnit(side+1,kind,point,side==0?0:Math.PI));return null;}
            foreach(var b in AllBuildings()) {double br=TerritoryRules.Radius(Profile,b.Kind);if(Math.Abs(point.X-b.Position.X)<br+radius&&Math.Abs(point.Z-b.Position.Z)<br+radius)return "Место занято зданием.";}
            double slotRadius=Math.Max(Profile.ScienceFootprintRadius,Math.Max(Profile.FactoryFootprintRadius,Profile.RefineryFootprintRadius));
            if(Profile.AuthoredMap.Sites(Profile).Any(s=>s.Slots.Any(slot=>Distance(slot.Position,point)<slotRadius+radius)))return "Оставьте строительный слот свободным.";
            if(Profile.AuthoredMap.Sites(Profile).Any(s=>s.DefenseSlots(Profile).Any(slot=>Distance(slot.Position,point)<Profile.TurretFootprintRadius+radius)))return "Оставьте оборонительный слот свободным.";
            Units.Add(new OfflineScenarioUnit(side+1,kind,point,side==0?0:Math.PI));return null;
        }
        public string PlaceBuilding(int side,PlayableBuildingKind kind,NavPoint point)
        {
            if(side<0||side>1||double.IsNaN(point.X)||double.IsNaN(point.Z)||(side==0?point.X>0:point.X<0))return "Выберите слот на своей половине.";
            if(!Buildable.Contains(kind))return "Выберите здание.";
            var options=Profile.AuthoredMap.Sites(Profile).Where(s=>s.Id==side+1||s.Id==side+3).SelectMany(s=>(TerritoryRules.Defensive(kind)?s.DefenseSlots(Profile):s.Slots).Select(slot=>new {Site=s,Slot=slot})).OrderBy(x=>Distance(x.Slot.Position,point)).ToArray();
            var pick=options.FirstOrDefault();if(pick==null||Distance(pick.Slot.Position,point)>TerritoryRules.Radius(Profile,kind)+Profile.BuildingPickRadius)return "Выберите строительный слот своей базы.";
            if(Buildings.Any(b=>b.SiteId==pick.Site.Id&&b.SlotId==pick.Slot.Id))return "Слот занят.";
            double br=TerritoryRules.Radius(Profile,kind);
            if(Units.Any(u=>u.Kind!=PlayableEntityKind.Hawk&&Math.Abs(u.Position.X-pick.Slot.Position.X)<br+PlayableUnitRules.Radius(Profile,u.Kind)&&Math.Abs(u.Position.Z-pick.Slot.Position.Z)<br+PlayableUnitRules.Radius(Profile,u.Kind)))return "Освободите слот от юнитов.";
            Buildings.Add(new OfflineScenarioBuilding(side+1,kind,pick.Site.Id,pick.Slot.Id,pick.Slot.Position,pick.Slot.Heading));return null;
        }
        public static readonly PlayableBuildingKind[] Buildable={PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter,PlayableBuildingKind.Hangar,PlayableBuildingKind.SentryTurret,PlayableBuildingKind.RepairTower,PlayableBuildingKind.GaussTower};
        private IEnumerable<OfflineScenarioBuilding> AllBuildings()
        {
            foreach(var s in Profile.AuthoredMap.Sites(Profile))yield return new OfflineScenarioBuilding(s.Id%2==1?1:2,s.Kind,s.Id,0,s.Position);
            foreach(var b in Buildings)yield return b;
        }
        public ProvingGroundSetup Copy()
        {var c=new ProvingGroundSetup(Profile);c.Units.AddRange(Units);c.Buildings.AddRange(Buildings);c.upgradedBuildings.UnionWith(upgradedBuildings);for(int i=0;i<2;i++)c.research[i].UnionWith(research[i]);return c;}
        public OfflineMatchConfiguration Configuration()
        {
            var copy=Copy();var map=copy.Profile.AuthoredMap;
            var roster=new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("enemy-1",2,2,OfflineControl.Scenario)};
            var starts=Enumerable.Range(0,2).Select(i=>new OfflineStart("proving-"+i,i+1,map.Headquarters((PlayableOwner)i),new NavPoint(i==0?-25:25,0),i==0?0:Math.PI,i+1)).ToArray();
            var buildings=copy.AllBuildings().Where(b=>b.Kind!=PlayableBuildingKind.Headquarters).ToArray();
            return new OfflineMatchConfiguration(copy.Profile,Identity,map.Id,"proving-ground-open-v1",7,roster,starts,map.Sites(copy.Profile),Array.Empty<NavObstacle>(),new double[,]{{0,80},{80,0}},scenario:copy.Units.ToArray(),terrain:map.Id,scenarioBuildings:buildings,provingGround:copy);
        }
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
    }
}
