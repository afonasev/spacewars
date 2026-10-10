using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    public enum OfflineControl { Human, Ai }
    public sealed class OfflineParticipant
    {
        public string Id{get;} public int LogicalPlayer{get;} public int Team{get;} public OfflineControl Control{get;}
        public OfflineParticipant(string id,int logicalPlayer,int team,OfflineControl control){Id=id;LogicalPlayer=logicalPlayer;Team=team;Control=control;}
    }
    public sealed class OfflineStart
    {
        public string Id{get;} public int SiteId{get;} public NavPoint Position{get;} public NavPoint ExplorerAnchor{get;} public double Heading{get;} public int? PinnedLogicalPlayer{get;}
        public OfflineStart(string id,int siteId,NavPoint position,NavPoint anchor,double heading=0,int? pin=null){Id=id;SiteId=siteId;Position=position;ExplorerAnchor=anchor;Heading=heading;PinnedLogicalPlayer=pin;}
    }
    public sealed class OfflineScenarioUnit
    {
        public int LogicalPlayer{get;} public PlayableEntityKind Kind{get;} public NavPoint Position{get;} public double Heading{get;} public bool Upgraded{get;}
        public OfflineScenarioUnit(int logicalPlayer,PlayableEntityKind kind,NavPoint position,double heading=0,bool upgraded=false){LogicalPlayer=logicalPlayer;Kind=kind;Position=position;Heading=heading;Upgraded=upgraded;}
    }
    public sealed class OfflineScenarioBuilding
    {
        public int LogicalPlayer{get;} public PlayableBuildingKind Kind{get;} public int SiteId{get;} public int SlotId{get;} public NavPoint Position{get;} public double Heading{get;}
        public OfflineScenarioBuilding(int logicalPlayer,PlayableBuildingKind kind,int site,int slot,NavPoint position,double heading=0){LogicalPlayer=logicalPlayer;Kind=kind;SiteId=site;SlotId=slot;Position=position;Heading=heading;}
    }
    // Flat SOURCE fixtures retain their explicit flat identity; native authored terrain
    // opts in with matching sites, solids, support geometry and map identity.
    public sealed class OfflineMatchConfiguration
    {
        public string SourceIdentity{get;} public string MapIdentity{get;} public string RouteProvenance{get;} public int Seed{get;}
        public IReadOnlyList<OfflineParticipant> Roster{get;} public IReadOnlyList<string> Spectators{get;} public IReadOnlyList<OfflineStart> Starts{get;}
        public IReadOnlyList<TerritorySite> Sites{get;} public IReadOnlyList<NavObstacle> Obstacles{get;} public IReadOnlyList<OfflineScenarioUnit> ScenarioUnits{get;} public IReadOnlyList<int> Assignments{get;} public IReadOnlyList<OfflineScenarioBuilding> ScenarioBuildings{get;}
        private readonly double[,] routeCosts;
        public OfflineMatchConfiguration(PlayableProfile profile,string source,string map,string routeProvenance,int seed,OfflineParticipant[] roster,OfflineStart[] starts,TerritorySite[] sites,NavObstacle[] obstacles,double[,] costs,string[] spectators=null,OfflineScenarioUnit[] scenario=null,string terrain="flat-ground-v1",OfflineScenarioBuilding[] scenarioBuildings=null)
        {
            if(profile==null||(profile.AuthoredMap==null?terrain!="flat-ground-v1":terrain!=profile.AuthoredMap.Id)||string.IsNullOrWhiteSpace(source)||string.IsNullOrWhiteSpace(map)||string.IsNullOrWhiteSpace(routeProvenance))throw new ArgumentException("Unsupported source/map/terrain binding.");
            if(roster==null||starts==null||sites==null||obstacles==null||costs==null)throw new ArgumentException("Missing authored binding.");
            if(roster.Any(x=>x==null||string.IsNullOrWhiteSpace(x.Id)||x.LogicalPlayer<1||x.LogicalPlayer>starts.Length||x.Team<1||x.Team>8||!Enum.IsDefined(typeof(OfflineControl),x.Control))||roster.Count(x=>x.Control==OfflineControl.Human)>4)throw new ArgumentException("Invalid offline roster.");
            spectators=spectators??Array.Empty<string>();scenario=scenario??Array.Empty<OfflineScenarioUnit>();scenarioBuildings=scenarioBuildings??Array.Empty<OfflineScenarioBuilding>();
            if(spectators.Any(string.IsNullOrWhiteSpace)||spectators.Distinct().Count()!=spectators.Length||spectators.Intersect(roster.Select(x=>x.Id)).Any())throw new ArgumentException("Invalid spectator roster.");
            if(starts.Any(x=>x==null||string.IsNullOrWhiteSpace(x.Id))||starts.Length>8||starts.Select(x=>x.Id).Distinct().Count()!=starts.Length||starts.Select(x=>x.SiteId).Distinct().Count()!=starts.Length||sites.Select(x=>x.Id).Distinct().Count()!=sites.Length||costs.GetLength(0)!=starts.Length||costs.GetLength(1)!=starts.Length)throw new ArgumentException("Invalid authored starts/sites/routes.");
            if(profile.AuthoredMap!=null)
            {
                TerritoryRules.ValidateArena(profile);var expected=profile.AuthoredMap.Sites(profile);
                if(sites.Length!=expected.Length||!sites.Zip(expected,SameSite).All(equal=>equal)||!obstacles.SequenceEqual(profile.AuthoredMap.MovementBlockers))throw new ArgumentException("Authored terrain/site binding mismatch.");
            }
            var geometry=new NavGeometry(profile.ArenaHalfExtent,obstacles,1);var envelopes=new List<Tuple<NavPoint,double,int,bool>>();
            foreach(var site in sites){if(site==null||site.Id<1||(site.Kind!=PlayableBuildingKind.Headquarters&&site.Kind!=PlayableBuildingKind.Outpost&&site.Kind!=PlayableBuildingKind.Mine)||!Finite(site.Position.X)||!Finite(site.Position.Z)||site.Slots.Select(x=>x.Id).Distinct().Count()!=site.Slots.Count||site.Slots.Select((x,i)=>x.Id!=i+1||!Finite(x.Position.X)||!Finite(x.Position.Z)||!Finite(x.Heading)).Any(x=>x))throw new ArgumentException("Null site.");envelopes.Add(Tuple.Create(site.Position,TerritoryRules.Radius(profile,site.Kind),site.Id,true));foreach(var slot in site.Slots)envelopes.Add(Tuple.Create(slot.Position,Math.Max(profile.ScienceFootprintRadius,Math.Max(profile.FactoryFootprintRadius,profile.RefineryFootprintRadius)),site.Id,false));}
            for(int i=0;i<envelopes.Count;i++){var e=envelopes[i];if(!geometry.IsFree(e.Item1,e.Item2*Math.Sqrt(2)+profile.ExplorerCollisionRadius))throw new ArgumentException("Unsupported site envelope.");for(int j=0;j<i;j++)if(!(e.Item3==envelopes[j].Item3&&(e.Item4||envelopes[j].Item4))&&Distance(e.Item1,envelopes[j].Item1)<e.Item2+envelopes[j].Item2)throw new ArgumentException("Overlapping authored envelopes.");}
            foreach(var start in starts){var site=sites.SingleOrDefault(x=>x.Id==start.SiteId);if(!Finite(start.Position.X)||!Finite(start.Position.Z)||!Finite(start.ExplorerAnchor.X)||!Finite(start.ExplorerAnchor.Z)||site==null||site.Kind!=PlayableBuildingKind.Headquarters||!site.Position.Equals(start.Position)||!Finite(start.Heading)||(start.PinnedLogicalPlayer.HasValue&&(start.PinnedLogicalPlayer<1||start.PinnedLogicalPlayer>starts.Length))||!geometry.IsFree(start.ExplorerAnchor,profile.ExplorerCollisionRadius)||envelopes.Any(e=>Distance(e.Item1,start.ExplorerAnchor)<e.Item2+profile.ExplorerCollisionRadius))throw new ArgumentException("Invalid start/anchor/pin.");}
            foreach(var unit in scenario)if(unit==null||!Finite(unit.Position.X)||!Finite(unit.Position.Z)||unit.LogicalPlayer<1||unit.LogicalPlayer>starts.Length||!Enum.IsDefined(typeof(PlayableEntityKind),unit.Kind)||!Finite(unit.Heading)||!geometry.IsFree(unit.Position,PlayableUnitRules.Radius(profile,unit.Kind))||envelopes.Any(e=>Distance(e.Item1,unit.Position)<e.Item2+PlayableUnitRules.Radius(profile,unit.Kind)))throw new ArgumentException("Unsupported scenario unit.");
            for(int a=0;a<starts.Length;a++)for(int b=0;b<starts.Length;b++)if(costs[a,b]<0||!Finite(costs[a,b])||costs[a,b]!=costs[b,a]||(a==b&&costs[a,b]!=0))throw new ArgumentException("Invalid route-cost binding.");
            SourceIdentity=source;MapIdentity=map;RouteProvenance=routeProvenance;Seed=seed;Roster=Array.AsReadOnly((OfflineParticipant[])roster.Clone());Spectators=Array.AsReadOnly((string[])spectators.Clone());Starts=Array.AsReadOnly((OfflineStart[])starts.Clone());Sites=Array.AsReadOnly((TerritorySite[])sites.Clone());Obstacles=Array.AsReadOnly((NavObstacle[])obstacles.Clone());ScenarioUnits=Array.AsReadOnly((OfflineScenarioUnit[])scenario.Clone());routeCosts=(double[,])costs.Clone();Assignments=Array.AsReadOnly(OfflineStartAssignment.Resolve(Starts,Roster,seed,routeCosts));Profile=profile;ScenarioBuildings=Array.AsReadOnly((OfflineScenarioBuilding[])scenarioBuildings.Clone());
            var occupied=new HashSet<string>();foreach(var b in ScenarioBuildings){
                if(b==null||b.LogicalPlayer<1||b.LogicalPlayer>starts.Length||!Enum.IsDefined(typeof(PlayableBuildingKind),b.Kind)||!Finite(b.Heading)||!occupied.Add(b.SiteId+":"+b.SlotId))throw new ArgumentException("Invalid authored building.");
                var site=Sites.SingleOrDefault(s=>s.Id==b.SiteId);if(site==null||b.SlotId<0||b.SlotId>site.Slots.Count)throw new ArgumentException("Missing authored site/slot.");
                if(b.SlotId==0){if(b.Kind!=site.Kind||Starts.Any(s=>s.SiteId==b.SiteId)||!b.Position.Equals(site.Position))throw new ArgumentException("Invalid authored center.");}
                else{
                    if(TerritoryRules.Center(b.Kind)||b.Kind==PlayableBuildingKind.Mine||!b.Position.Equals(site.Slots[b.SlotId-1].Position)||b.Heading!=site.Slots[b.SlotId-1].Heading)throw new ArgumentException("Invalid authored ordinary placement.");
                    int owner=Enumerable.Range(0,Roster.Count).Where(i=>Roster[i].LogicalPlayer==b.LogicalPlayer).DefaultIfEmpty(-1).First();bool automatic=owner>=0&&Starts[Assignments[owner]].SiteId==b.SiteId;
                    if(!automatic&&!ScenarioBuildings.Any(p=>p.LogicalPlayer==b.LogicalPlayer&&p.SiteId==b.SiteId&&p.SlotId==0)){
                        // An absent logical player's pinned automatic parent is source-valid.
                        if(!Starts.Any(s=>s.SiteId==b.SiteId&&s.PinnedLogicalPlayer==b.LogicalPlayer))throw new ArgumentException("Missing authored parent owner.");
                    }
                }
            }
            var placements=scenario.Select(u=>Tuple.Create(u.Position,PlayableUnitRules.Radius(profile,u.Kind))).ToArray();
            for(int i=0;i<placements.Length;i++){for(int j=0;j<i;j++)if(Distance(placements[i].Item1,placements[j].Item1)<placements[i].Item2+placements[j].Item2)throw new ArgumentException("Overlapping scenario units.");foreach(var start in starts)if(Distance(placements[i].Item1,start.ExplorerAnchor)<placements[i].Item2+profile.ExplorerCollisionRadius)throw new ArgumentException("Scenario unit overlaps automatic Explorer.");}

        }
        private static bool SameSite(TerritorySite a,TerritorySite b)=>a!=null&&a.Id==b.Id&&a.Kind==b.Kind&&a.Position.Equals(b.Position)&&a.Slots.Count==b.Slots.Count&&a.Slots.Zip(b.Slots,(x,y)=>x.Id==y.Id&&x.Position.Equals(y.Position)&&x.Heading==y.Heading).All(equal=>equal);
        private static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        public PlayableProfile Profile{get;}
        public double RouteCost(int from,int to)=>routeCosts[from,to];
    }
}
