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
    }
    public sealed class TerritorySlot
    {
        public TerritorySlot(int id,NavPoint position,double heading){Id=id;Position=position;Heading=heading;}
        public int Id{get;} public NavPoint Position{get;} public double Heading{get;}
    }
    public sealed class TerritorySiteSnapshot
    {
        public TerritorySiteSnapshot(TerritorySite site,PlayableOwner? claimant,PlayableOwner? owner,double progress,bool contested,int centerId,bool ready)
        {Site=site;Claimant=claimant;Owner=owner;Progress=progress;Contested=contested;CenterId=centerId;Ready=ready;}
        public TerritorySite Site{get;} public PlayableOwner? Claimant{get;} public PlayableOwner? Owner{get;}
        public double Progress{get;} public bool Contested{get;} public int CenterId{get;} public bool Ready{get;}
    }
    public static class TerritoryRules
    {
        public static TerritorySite[] Sites(PlayableProfile p)=>p.AuthoredMap!=null?p.AuthoredMap.Sites(p):new[]{
            new TerritorySite(1,PlayableBuildingKind.Headquarters,new NavPoint(p.PlayerHeadquartersX,p.HeadquartersZ),p),
            new TerritorySite(2,PlayableBuildingKind.Headquarters,new NavPoint(p.EnemyHeadquartersX,p.HeadquartersZ),p),
            new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(p.OutpostX,p.OutpostZ),p),
            new TerritorySite(4,PlayableBuildingKind.Mine,new NavPoint(p.MineX,p.MineZ),p)};
        public static PlayablePublicScoutObjective[] PublicScoutObjectives(PlayableProfile p,PlayableOwner observer)
        {
            var own=p.Headquarters(observer);
            return Sites(p).Where(site=>site.Kind==PlayableBuildingKind.Headquarters&&Math.Abs(site.Position.X-own.X)>Double.Epsilon)
                .Select(site=>
                {
                    var dx=own.X-site.Position.X;var dz=own.Z-site.Position.Z;var length=Math.Sqrt(dx*dx+dz*dz);
                    var clearance=Radius(p,site.Kind)+p.ExplorerCollisionRadius+p.Navigation.ArrivalSlotSpacing;
                    // Pick the nearest public gap between authored home slots. A direct radial
                    // approach can coincide with slot 1 and become illegal after a factory is built.
                    double spacing=2*Math.PI/site.Slots.Count;
                    double toward=Math.Atan2(dz,dx);
                    if(toward<0)toward+=2*Math.PI;
                    double angle=(Math.Floor(toward/spacing)+.5)*spacing;
                    return new PlayablePublicScoutObjective(site.Id,new NavPoint(site.Position.X+Math.Cos(angle)*clearance,site.Position.Z+Math.Sin(angle)*clearance),PlayablePublicScoutObjectiveRole.PossibleEnemyStart,true);
                }).OrderBy(x=>x.SiteId).ToArray();
        }
        public static void ValidateArena(PlayableProfile p)
        {
            var walls=new NavGeometry(p.ArenaHalfExtent,PlayableMap.StaticObstacles(p),1);
            var footprints=new List<Tuple<NavPoint,double>>();
            foreach(var site in Sites(p))
            {
                footprints.Add(Tuple.Create(site.Position,Radius(p,site.Kind)));
                foreach(var slot in site.Slots)footprints.Add(Tuple.Create(slot.Position,Math.Max(p.ScienceFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius))));
            }
            for(int i=0;i<footprints.Count;i++)
            {
                var current=footprints[i];double clearance=current.Item2*(p.AuthoredMap==null?1:Math.Sqrt(2))+p.TankCollisionRadius;
                if(!walls.IsFree(current.Item1,clearance))throw new ArgumentException("Territory envelope intersects terrain or boundary.");
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
                var exit=new NavPoint(slot.Position.X+Math.Cos(slot.Heading)*p.FactoryExitDistance,slot.Position.Z+Math.Sin(slot.Heading)*p.FactoryExitDistance);
                if(!built.IsFree(exit,p.TankCollisionRadius))throw new ArgumentException("Factory exit is blocked in the fully built arena.");
            }
        }
        public static bool Center(PlayableBuildingKind k)=>k==PlayableBuildingKind.Headquarters||k==PlayableBuildingKind.Outpost;
        public static double Radius(PlayableProfile p,PlayableBuildingKind k)=>k==PlayableBuildingKind.ScientificCenter?p.ScienceFootprintRadius:k==PlayableBuildingKind.Headquarters?p.HeadquartersFootprintRadius:k==PlayableBuildingKind.Factory?p.FactoryFootprintRadius:k==PlayableBuildingKind.Refinery?p.RefineryFootprintRadius:k==PlayableBuildingKind.Outpost?p.OutpostFootprintRadius:p.MineFootprintRadius;
        public static int Health(PlayableProfile p,PlayableBuildingKind k)=>(int)(k==PlayableBuildingKind.ScientificCenter?p.ScienceHealth:k==PlayableBuildingKind.Headquarters?p.HeadquartersHealth:k==PlayableBuildingKind.Factory?p.FactoryHealth:k==PlayableBuildingKind.Refinery?p.RefineryHealth:k==PlayableBuildingKind.Outpost?p.OutpostHealth:p.MineHealth);
        public static double Duration(PlayableProfile p,PlayableBuildingKind k)=>k==PlayableBuildingKind.ScientificCenter?p.ScienceBuildSeconds:k==PlayableBuildingKind.Headquarters?p.HeadquartersBuildSeconds:k==PlayableBuildingKind.Factory?p.FactoryBuildSeconds:k==PlayableBuildingKind.Refinery?p.RefineryBuildSeconds:k==PlayableBuildingKind.Outpost?p.OutpostBuildSeconds:p.MineBuildSeconds;
        public static int Cost(PlayableProfile p,PlayableBuildingKind k)=>(int)(k==PlayableBuildingKind.ScientificCenter?p.ScienceCreditCost:k==PlayableBuildingKind.Headquarters?p.HeadquartersCreditCost:k==PlayableBuildingKind.Factory?p.FactoryCreditCost:k==PlayableBuildingKind.Refinery?p.RefineryCreditCost:k==PlayableBuildingKind.Outpost?p.OutpostCreditCost:p.MineCreditCost);
        public static double Income(PlayableProfile p,PlayableBuildingKind k)=>k==PlayableBuildingKind.Headquarters?p.HeadquartersIncomePerPeriod:k==PlayableBuildingKind.Refinery?p.RefineryIncomePerPeriod:k==PlayableBuildingKind.Outpost?p.OutpostIncomePerPeriod:k==PlayableBuildingKind.Mine?p.MineIncomePerPeriod:0;
        public static double CaptureRadius(PlayableProfile p,PlayableBuildingKind k)=>k==PlayableBuildingKind.Headquarters?p.HeadquartersCaptureRadius:k==PlayableBuildingKind.Outpost?p.OutpostCaptureRadius:p.MineCaptureRadius;
        public static double CaptureSeconds(PlayableProfile p,PlayableBuildingKind k)=>k==PlayableBuildingKind.Headquarters?p.HeadquartersCaptureSeconds:k==PlayableBuildingKind.Outpost?p.OutpostCaptureSeconds:p.MineCaptureSeconds;
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
