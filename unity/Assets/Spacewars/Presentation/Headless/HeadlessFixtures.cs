using System;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Headless
{
    public static class HeadlessFixtures
    {
        public static readonly string[] EconomyFixtures={"economy-rich-v1","economy-low-v1","economy-full-slots-v1","economy-lost-hq-v1","economy-blocked-exit-v1"};
        public static bool IsEconomy(string fixture)=>EconomyFixtures.Contains(fixture);
        private static OfflineMatchConfiguration Economy(MatchManifest m)
        {
            var p=PlayableProfile.Default;
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-24,0),new NavPoint(-24,7),pin:1),new OfflineStart("east",2,new NavPoint(24,24),new NavPoint(18,24),pin:2)};
            var homeSlots=new[]{new TerritorySlot(1,new NavPoint(-16,-8),0),new TerritorySlot(2,new NavPoint(-16,8),0)};
            bool rich=m.Fixture=="economy-rich-v1",lost=m.Fixture=="economy-lost-hq-v1",blocked=m.Fixture=="economy-blocked-exit-v1",full=m.Fixture=="economy-full-slots-v1";
            if(rich)homeSlots=homeSlots.Concat(new[]{new TerritorySlot(3,new NavPoint(-24,-12),0)}).ToArray();
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,lost?Array.Empty<TerritorySlot>():homeSlots),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>()),new TerritorySite(3,rich||lost?PlayableBuildingKind.Outpost:PlayableBuildingKind.Mine,new NavPoint(-7,0),p)};
            var buildings=new System.Collections.Generic.List<OfflineScenarioBuilding>();
            if(rich||blocked||full)foreach(var slot in homeSlots)
                buildings.Add(new OfflineScenarioBuilding(1,full?PlayableBuildingKind.ScientificCenter:slot.Id==1?PlayableBuildingKind.Factory:PlayableBuildingKind.Refinery,1,slot.Id,slot.Position,slot.Heading));
            if(rich||lost){buildings.Add(new OfflineScenarioBuilding(1,PlayableBuildingKind.Outpost,3,0,sites[2].Position));if(rich)foreach(var slot in sites[2].Slots.Take(sites[2].Slots.Count-1))buildings.Add(new OfflineScenarioBuilding(1,PlayableBuildingKind.Refinery,3,slot.Id,slot.Position,slot.Heading));}
            var obstacles=new System.Collections.Generic.List<NavObstacle>();
            if(blocked){var f=homeSlots[0].Position;double wall=p.FactoryFootprintRadius*Math.Sqrt(2)+p.ExplorerCollisionRadius+.1;double outer=wall+.2;
                obstacles.Add(new NavObstacle(f.X-wall,f.Z-outer,f.X+wall,f.Z-wall));obstacles.Add(new NavObstacle(f.X-wall,f.Z+wall,f.X+wall,f.Z+outer));obstacles.Add(new NavObstacle(f.X-outer,f.Z-outer,f.X-wall,f.Z+outer));obstacles.Add(new NavObstacle(f.X+wall,f.Z-outer,f.X+outer,f.Z+outer));}
            var units=lost?Enumerable.Range(0,6).Select(i=>new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-28+(i%3)*4,-12-(i/3)*4))).ToArray():Array.Empty<OfflineScenarioUnit>();
            return new OfflineMatchConfiguration(p,"native-economy-diagnostics-v1",m.Fixture,"UnityHostRouteService",m.Seed,m.Roster.Select(o=>new OfflineParticipant(o.Id,o.LogicalPlayer,o.Team,o.Control=="ai"?OfflineControl.Ai:OfflineControl.Human)).ToArray(),starts,sites,obstacles.ToArray(),new double[,]{{0,100},{100,0}},scenario:units,scenarioBuildings:buildings.ToArray());
        }
        public static OfflineMatchConfiguration Create(MatchManifest m)
        {
            if(IsEconomy(m.Fixture))return Economy(m);
            var p=PlayableProfile.Default;
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-24,-24),new NavPoint(-18,-28),pin:1),new OfflineStart("east",2,new NavPoint(24,24),new NavPoint(18,28),pin:2)};
            var slots=new[]{new TerritorySlot(1,new NavPoint(-24,-16),0),new TerritorySlot(2,new NavPoint(-16,-24),0)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,slots),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,new[]{new TerritorySlot(1,new NavPoint(24,16),0),new TerritorySlot(2,new NavPoint(16,24),0)}),new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(20,0),Array.Empty<TerritorySlot>())};
            var obstacle=m.Fixture=="obstacle-v1";
            var units=obstacle?new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-20,0))}:
                Enumerable.Range(0,6).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-12+(i%3)*4,-12+(i/3)*4))).ToArray();
            return new OfflineMatchConfiguration(p,"native-headless-fixtures-v1",m.Fixture,"UnityHostRouteService",m.Seed,
                m.Roster.Select(o=>new OfflineParticipant(o.Id,o.LogicalPlayer,o.Team,o.Control=="ai"?OfflineControl.Ai:OfflineControl.Human)).ToArray(),starts,sites,
                obstacle?new[]{new NavObstacle(-3,-12,3,12)}:Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:units);
        }
        public static PlayableCommand Command(PlayableAuthorityTick authority,CommandInput input,long generation,long sequence)
        {
            var view=authority.ParticipantView(input.OwnerId);var owner=view.Owner;
            var entities=input.Selector=="Explorer"?view.Entities.Where(e=>e.Owner==owner&&e.Kind==PlayableEntityKind.Explorer).Take(1).Select(e=>e.Id).ToArray():input.Selector=="Tanks"?view.Entities.Where(e=>e.Owner==owner&&e.Kind==PlayableEntityKind.Tank).Select(e=>e.Id).ToArray():input.Selector=="Factory"||input.Selector=="ScientificCenter"?view.Buildings.Where(b=>b.Owner==owner&&b.Kind.ToString()==input.Selector).Take(1).Select(b=>b.Id).ToArray():Array.Empty<int>();
            var parent=view.Buildings.FirstOrDefault(b=>b.Owner==owner&&b.Kind==PlayableBuildingKind.Headquarters)?.Id??0;
            return new PlayableCommand(generation,sequence,input.OwnerId,(PlayableCommandKind)Enum.Parse(typeof(PlayableCommandKind),input.Kind),entities,new NavPoint(input.X,input.Z),siteId:input.Site,slotId:input.Slot,parentId:parent,buildingKind:(PlayableBuildingKind)Enum.Parse(typeof(PlayableBuildingKind),input.Building),researchKind:(PlayableResearchKind)Enum.Parse(typeof(PlayableResearchKind),input.Research),origin:PlayableOrderOrigin.Human);
        }
        public static string MapHash(OfflineMatchConfiguration c)=>PlayableAiCanonical.Hash(PlayableAiCanonical.Encode(new {c.MapIdentity,c.Starts,c.Sites,c.Obstacles,c.ScenarioUnits,c.ScenarioBuildings}));
        public static string GeometryHash(OfflineMatchConfiguration c)=>PlayableAiCanonical.Hash(PlayableAiCanonical.Encode(new NavGeometry(c.Profile.ArenaHalfExtent,c.Obstacles.ToArray(),1)));
        public static WorkerIdentity EngineIdentity(string code)
        {
            var p=PlayableProfile.Default;var n=p.Navigation;
            return new WorkerIdentity {CodeRevision=code,UnityVersion=UnityEngine.Application.unityVersion,Platform="macOS",Architecture="arm64",GameplayId=p.ProfileId,GameplayRevision=p.Revision,GameplayHash=AiMatchIdentity.GameplayDigest(p),AiId=AiProfile.Initial.Id,AiRevision=AiProfile.Initial.Revision,AiHash=AiProfile.Initial.Hash,CatalogHash=AiRosterCatalog.Initial.Hash,
                NavMeshSettings=PlayableAiCanonical.Encode(new {Provider="UnityNavigationRouter",SettingsIndex=0,AgentRadius=n.Radius+n.GridCell/4,AgentHeight=1,AgentClimb=0,AgentSlope=0,VoxelSize=n.GridCell/4,HalfExtent=p.ArenaHalfExtent}),RouteDeliveryPolicy=PlayableAuthorityTick.RouteDeliveryPolicy};
        }
        public static MatchManifest Template(string fixture,WorkerIdentity identity,string root,string executable,string launcher)
        {
            var m=new MatchManifest{Schema="native-ai-run-v1",Worker=identity,WorkerRoot=root,Executable=executable,Launcher=launcher,Mode="duel",Fixture=fixture,MapId=fixture,Seed=19092026,Generation=71,TickRate=30,TicksLimit=fixture=="obstacle-v1"?1800:18000,TickBatch=64,RouteBudget=64,TraceLimit=2048,EvaluationPolicy="native-ai-behavior-v1@1",Openings="diagnostic-coverage",Roster=new[]{new OwnerInput{Id="west-owner",LogicalPlayer=1,Team=1,Control="human",Difficulty="fighter"},new OwnerInput{Id="east-owner",LogicalPlayer=2,Team=2,Control="ai",Difficulty="fighter"}}};
            CommandInput C(int tick,string kind,string selector,double x=0,double z=0,int site=0,int slot=0,string building="Headquarters")=>new CommandInput{Tick=tick,OwnerId="west-owner",Kind=kind,Selector=selector,X=x,Z=z,Site=site,Slot=slot,Building=building,Research="TankChassis"};
            var p=PlayableProfile.Default;
            // Coverage research before combat can destroy its producer; both readiness
            // and guaranteed ordinary HQ settlements fund this human diagnostic input.
            int researchTick=Math.Max(1800+(int)Math.Ceiling(TerritoryRules.Duration(p,PlayableBuildingKind.ScientificCenter)*30),
                (int)Math.Ceiling((p.FactoryCreditCost+p.ScienceCreditCost+p.TankChassisCost-p.StartingCredits)/(p.HeadquartersIncomePerPeriod/(double)p.IncomePeriodSeconds)*30));
            m.Commands=fixture=="obstacle-v1"?new[]{C(0,"Move","Tanks",20,-8)}:new[]{C(0,"BuildAt","None",site:1,slot:1,building:"Factory"),C(0,"Move","Explorer",20,0),C(1800,"BuildAt","None",site:1,slot:2,building:"ScientificCenter"),C(4500,"QueueTank","Factory"),C(researchTick,"QueueResearch","ScientificCenter"),C(9000,"AttackMove","Tanks",18,18),C(13500,"AttackMove","Tanks",12,0)}.OrderBy(c=>c.Tick).ToArray();
            if(IsEconomy(fixture))
            {
                m.TicksLimit=5400;m.TraceLimit=4096;
                m.Roster[0].Control="ai";m.Roster[1].Control="human";
                m.Commands=fixture=="economy-lost-hq-v1"?new[]{new CommandInput{Tick=0,OwnerId="east-owner",Kind="AttackMove",Selector="Tanks",X=-24,Z=-5,Site=0,Slot=0,Building="Headquarters",Research="TankChassis"}}:Array.Empty<CommandInput>();
            }
            m.Checkpoints=Enumerable.Range(0,m.TicksLimit/300+1).Select(i=>i*300).ToArray();
            var c=Create(m);m.MapHash=MapHash(c);m.GeometryHash=GeometryHash(c);m.RosterHash=Wire.RosterDigest(m.Roster);return m;
        }
    }
}
