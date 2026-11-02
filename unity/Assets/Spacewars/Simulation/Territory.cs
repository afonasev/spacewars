using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    public enum ConstructionPhase { Pending, Constructing, Ready }
    public sealed class TerritorySite
    {
        public TerritorySite(int id, PlayableBuildingKind kind, NavPoint position, PlayableProfile p,double phase=0)
        {
            Id=id; Kind=kind; Position=position;
            int count=kind==PlayableBuildingKind.Headquarters?(int)p.HeadquartersSlots:kind==PlayableBuildingKind.Outpost?(int)p.OutpostSlots:0;
            var slots=new TerritorySlot[count];
            for(int i=0;i<count;i++)
            {
                double angle=phase+2*Math.PI*i/count;
                slots[i]=new TerritorySlot(i+1,new NavPoint(position.X+Math.Cos(angle)*p.SlotRingRadius,position.Z+Math.Sin(angle)*p.SlotRingRadius),angle);
            }
            Slots=Array.AsReadOnly(slots);
        }
        public TerritorySite(int id,PlayableBuildingKind kind,NavPoint position,TerritorySlot[] slots){Id=id;Kind=kind;Position=position;Slots=Array.AsReadOnly((TerritorySlot[])slots.Clone());}
        public int Id{get;} public PlayableBuildingKind Kind{get;} public NavPoint Position{get;}
        public IReadOnlyList<TerritorySlot> Slots{get;}
        readonly System.Runtime.CompilerServices.ConditionalWeakTable<PlayableProfile,System.Collections.ObjectModel.ReadOnlyCollection<TerritorySlot>> defenseSlots=new System.Runtime.CompilerServices.ConditionalWeakTable<PlayableProfile,System.Collections.ObjectModel.ReadOnlyCollection<TerritorySlot>>();
        public IReadOnlyList<TerritorySlot> DefenseSlots(PlayableProfile profile)=>defenseSlots.GetValue(profile,p=>Array.AsReadOnly(SentryPadLayout.Create(this,p)));

    }
    public sealed class TerritorySlot
    {
        public TerritorySlot(int id,NavPoint position,double heading){Id=id;Position=position;Heading=heading;}
        public int Id{get;} public NavPoint Position{get;} public double Heading{get;}
    }
    public sealed class TerritorySiteSnapshot
    {
        public TerritorySiteSnapshot(TerritorySite site,PlayableOwner? claimant,PlayableOwner? owner,double progress,bool contested,int centerId,bool ready,double controlledSeconds=0)
        {Site=site;Claimant=claimant;Owner=owner;Progress=progress;Contested=contested;CenterId=centerId;Ready=ready;ControlledSeconds=controlledSeconds;}
        public TerritorySite Site{get;} public PlayableOwner? Claimant{get;} public PlayableOwner? Owner{get;}
        public double Progress{get;} public bool Contested{get;} public int CenterId{get;} public bool Ready{get;} public double ControlledSeconds{get;}
    }
    public static class TerritoryRules
    {
        // World south is a product invariant, independent of camera and radial pad layout.
        public const double BuildingHeading=-Math.PI/2;
        // A disjoint authority ID namespace preserves every existing ordinary slot ID.
        public const int DefenseSlotOffset=1000;
        public static bool ArmedDefense(PlayableBuildingKind kind)=>kind==PlayableBuildingKind.SentryTurret||kind==PlayableBuildingKind.GaussTower;
        public static bool Defensive(PlayableBuildingKind kind)=>kind==PlayableBuildingKind.GaussTower||kind==PlayableBuildingKind.SentryTurret||kind==PlayableBuildingKind.RepairTower;
        public static bool DefenseSlot(int slot)=>slot>DefenseSlotOffset;
        public static TerritorySite[] Sites(PlayableProfile p)=>p.AuthoredMap!=null?p.AuthoredMap.Sites(p):new[]{
            new TerritorySite(1,PlayableBuildingKind.Headquarters,new NavPoint(p.PlayerHeadquartersX,p.HeadquartersZ),p),
            new TerritorySite(2,PlayableBuildingKind.Headquarters,new NavPoint(p.EnemyHeadquartersX,p.HeadquartersZ),p),
            new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(p.OutpostX,p.OutpostZ),p),
            new TerritorySite(4,PlayableBuildingKind.Mine,new NavPoint(p.MineX,p.MineZ),p)};
        public static PlayablePublicScoutObjective[] PublicScoutObjectives(PlayableProfile p,PlayableOwner observer)
        {
            var own=p.Headquarters(observer);
            return Sites(p).Where(site=>site.Kind==PlayableBuildingKind.Headquarters&&Math.Abs(site.Position.X-own.X)>Double.Epsilon)
                .Select(site=>new PlayablePublicScoutObjective(site.Id,PublicScoutApproach(p,site,own),PlayablePublicScoutObjectiveRole.PossibleEnemyStart,true))
                .OrderBy(x=>x.SiteId).ToArray();
        }
        public static NavPoint PublicScoutApproach(PlayableProfile p,TerritorySite site,NavPoint observer)
        {
            // Public start geometry only: do not use occupancy or an actor spawn
            // anchor, which can be too far away to survey the placement envelope.
            double toward=Math.Atan2(observer.Z-site.Position.Z,observer.X-site.Position.X);
            if(toward<0)toward+=2*Math.PI;
            double angle=toward;
            var angles=site.Slots.Select(slot=>Math.Atan2(slot.Position.Z-site.Position.Z,slot.Position.X-site.Position.X))
                .Select(value=>value<0?value+2*Math.PI:value).Distinct().OrderBy(value=>value).ToArray();
            if(angles.Length>0)
                angle=angles.Select((value,index)=>(value+(index+1<angles.Length?angles[index+1]:angles[0]+2*Math.PI))/2)
                    .OrderBy(value=>Math.Abs(Math.Atan2(Math.Sin(value-toward),Math.Cos(value-toward)))).ThenBy(value=>value).First();
            // Every possible start uses HQ clearance, including vacant starts.
            // Current hidden occupancy must not select a different public point.
            double clearance=Radius(p,PlayableBuildingKind.Headquarters)+p.ExplorerCollisionRadius+p.Navigation.ArrivalSlotSpacing;
            return new NavPoint(site.Position.X+Math.Cos(angle)*clearance,site.Position.Z+Math.Sin(angle)*clearance);
        }
        public static NavPoint PublicStartScoutApproach(PlayableProfile p,OfflineStart start,NavPoint observer)
        {
            // Reserve both public layouts: vacant Outpost and occupied HQ.
            // Do not consult assignments, pins, ownership or actual building state.
            var slots=new TerritorySite(start.SiteId,PlayableBuildingKind.Headquarters,start.Position,p,start.Heading).Slots
                .Concat(new TerritorySite(start.SiteId,PlayableBuildingKind.Outpost,start.Position,p,start.Heading).Slots)
                .GroupBy(slot=>slot.Position).Select(group=>group.First()).ToArray();
            return PublicScoutApproach(p,new TerritorySite(start.SiteId,PlayableBuildingKind.Headquarters,start.Position,slots),observer);
        }
        public static void ValidateArena(PlayableProfile p)
        {
            var walls=new NavGeometry(p.ArenaHalfExtent,PlayableMap.StaticObstacles(p),1);
            var footprints=new List<Tuple<NavPoint,double>>();
            foreach(var site in Sites(p))
            {
                footprints.Add(Tuple.Create(site.Position,Radius(p,site.Kind)));
                foreach(var slot in site.Slots)footprints.Add(Tuple.Create(slot.Position,Math.Max(p.HangarFootprintRadius,Math.Max(p.ScienceFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius)))));
                foreach(var slot in site.DefenseSlots(p))footprints.Add(Tuple.Create(slot.Position,p.TurretFootprintRadius));
            }
            for(int i=0;i<footprints.Count;i++)
            {
                var current=footprints[i];double clearance=current.Item2*(p.AuthoredMap==null?1:Math.Sqrt(2))+p.TankCollisionRadius;
                if(!walls.IsFree(current.Item1,clearance))throw new ArgumentException("Territory envelope intersects terrain or boundary at "+current.Item1.X+","+current.Item1.Z+" clearance="+clearance);
                if(p.AuthoredMap!=null){double y=p.AuthoredMap.SurfaceHeight(current.Item1);foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1}){var corner=new NavPoint(current.Item1.X+sx*current.Item2,current.Item1.Z+sz*current.Item2);var support=p.AuthoredMap.SupportAt(corner);if(support==null||Math.Abs(support.HeightAt(corner)-y)>1e-9||support.Gradient.X!=0||support.Gradient.Z!=0)throw new ArgumentException("Building footprint requires one flat supported elevation.");}}
                for(int j=0;j<i;j++)
                {
                    var other=footprints[j];double gap=current.Item2+other.Item2+2*p.TankCollisionRadius;
                    if(Math.Abs(current.Item1.X-other.Item1.X)<gap&&Math.Abs(current.Item1.Z-other.Item1.Z)<gap)
                        throw new ArgumentException("Territory building envelopes leave no traversal clearance.");
                }
            }
            var obstacles=new List<NavObstacle>(PlayableMap.StaticObstacles(p));
            foreach(var f in footprints)obstacles.Add(new NavObstacle(f.Item1.X-f.Item2,f.Item1.Z-f.Item2,f.Item1.X+f.Item2,f.Item1.Z+f.Item2));
            var built=new NavGeometry(p.ArenaHalfExtent,obstacles.ToArray(),1);
            foreach(var site in Sites(p))foreach(var slot in site.Slots)
            {
                var exit=new NavPoint(slot.Position.X+Math.Cos(BuildingHeading)*p.FactoryExitDistance,slot.Position.Z+Math.Sin(BuildingHeading)*p.FactoryExitDistance);
                if(!built.IsFree(exit,p.TankCollisionRadius))throw new ArgumentException("Factory exit is blocked in the fully built arena.");
            }
        }
        public static bool ScienceProducer(PlayableBuildingKind kind)=>kind==PlayableBuildingKind.Factory||kind==PlayableBuildingKind.Hangar;
        public static bool HasScienceProducer(IEnumerable<PlayableBuildingSnapshot> buildings,PlayableOwner owner)=>buildings.Any(b=>b.Owner==owner&&ScienceProducer(b.Kind)&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true);
        public static bool Center(PlayableBuildingKind k)=>k==PlayableBuildingKind.Headquarters||k==PlayableBuildingKind.Outpost;
        public static double Radius(PlayableProfile p,PlayableBuildingKind k)=>k==PlayableBuildingKind.RelayTower?p.RelayFootprintRadius:Defensive(k)?p.TurretFootprintRadius:k==PlayableBuildingKind.Hangar?p.HangarFootprintRadius:k==PlayableBuildingKind.ScientificCenter?p.ScienceFootprintRadius:k==PlayableBuildingKind.Headquarters?p.HeadquartersFootprintRadius:k==PlayableBuildingKind.Factory?p.FactoryFootprintRadius:k==PlayableBuildingKind.Refinery?p.RefineryFootprintRadius:k==PlayableBuildingKind.Outpost?p.OutpostFootprintRadius:p.MineFootprintRadius;
        public static double MaximumHealth(PlayableProfile p,PlayableBuildingSnapshot building)=>building.MaxHealth>0?building.MaxHealth:PlayableStatRounding.MaximumHealth(p,Health(p,building.Kind));
        public static int Health(PlayableProfile p,PlayableBuildingKind k)=>(int)(k==PlayableBuildingKind.GaussTower?p.TurretHealth*p.GaussHealthMultiplier:Defensive(k)?p.TurretHealth:k==PlayableBuildingKind.Hangar?p.HangarHealth:k==PlayableBuildingKind.ScientificCenter?p.ScienceHealth:k==PlayableBuildingKind.Headquarters?p.HeadquartersHealth:k==PlayableBuildingKind.Factory?p.FactoryHealth:k==PlayableBuildingKind.Refinery?p.RefineryHealth:k==PlayableBuildingKind.Outpost?p.OutpostHealth:p.MineHealth);
        public static double Duration(PlayableProfile p,PlayableBuildingKind k)=>Defensive(k)?p.TurretBuildSeconds:k==PlayableBuildingKind.Hangar?p.HangarBuildSeconds:k==PlayableBuildingKind.ScientificCenter?p.ScienceBuildSeconds:k==PlayableBuildingKind.Headquarters?p.HeadquartersBuildSeconds:k==PlayableBuildingKind.Factory?p.FactoryBuildSeconds:k==PlayableBuildingKind.Refinery?p.RefineryBuildSeconds:k==PlayableBuildingKind.Outpost?p.OutpostBuildSeconds:p.MineBuildSeconds;
        public static int Cost(PlayableProfile p,PlayableBuildingKind k)=>(int)(k==PlayableBuildingKind.RelayTower?0:k==PlayableBuildingKind.GaussTower?p.TurretCreditCost*p.GaussCostMultiplier:Defensive(k)?p.TurretCreditCost:k==PlayableBuildingKind.Hangar?p.HangarCreditCost:k==PlayableBuildingKind.ScientificCenter?p.ScienceCreditCost:k==PlayableBuildingKind.Headquarters?p.HeadquartersCreditCost:k==PlayableBuildingKind.Factory?p.FactoryCreditCost:k==PlayableBuildingKind.Refinery?p.RefineryCreditCost:k==PlayableBuildingKind.Outpost?p.OutpostCreditCost:p.MineCreditCost);
        public static double Income(PlayableProfile p,PlayableBuildingKind k)=>k==PlayableBuildingKind.RelayTower?p.RelayIncomePerPeriod:k==PlayableBuildingKind.Headquarters?p.HeadquartersIncomePerPeriod:k==PlayableBuildingKind.Refinery?p.RefineryIncomePerPeriod:k==PlayableBuildingKind.Outpost?p.OutpostIncomePerPeriod:k==PlayableBuildingKind.Mine?p.MineIncomePerPeriod:0;
        public static double CaptureRadius(PlayableProfile p,PlayableBuildingKind k)=>k==PlayableBuildingKind.RelayTower?p.RelayCaptureRadius:k==PlayableBuildingKind.Headquarters?p.HeadquartersCaptureRadius:k==PlayableBuildingKind.Outpost?p.OutpostCaptureRadius:p.MineCaptureRadius;
        public static double CaptureSeconds(PlayableProfile p,PlayableBuildingKind k)=>k==PlayableBuildingKind.RelayTower?p.RelayCaptureSeconds:k==PlayableBuildingKind.Headquarters?p.HeadquartersCaptureSeconds:k==PlayableBuildingKind.Outpost?p.OutpostCaptureSeconds:p.MineCaptureSeconds;
        // Shared visible/pick polygon; six sides are the established typed-pad shape, not a balance parameter.
        public static bool Contains(NavPoint point,NavPoint center,double radius,bool square,bool circle,double heading=0)
        {
            double dx=point.X-center.X,dz=point.Z-center.Z,x=dx*Math.Cos(heading)+dz*Math.Sin(heading),z=-dx*Math.Sin(heading)+dz*Math.Cos(heading);
            if(square)return Math.Abs(x)<=radius&&Math.Abs(z)<=radius;
            if(circle)return x*x+z*z<=radius*radius;
            for(int i=0;i<6;i++){double a=(i+.5)*Math.PI/3;if(x*Math.Cos(a)+z*Math.Sin(a)>radius*Math.Cos(Math.PI/6))return false;}
            return true;
        }
    }
}
