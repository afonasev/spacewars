using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private readonly OfflineMatchConfiguration offline;
        private readonly Dictionary<PlayableOwner,double> ownerCredits=new Dictionary<PlayableOwner,double>();
        private readonly Dictionary<PlayableOwner,long> offlineSequences=new Dictionary<PlayableOwner,long>();
        private readonly HashSet<PlayableOwner> eliminated=new HashSet<PlayableOwner>();
        internal int? WinnerTeam{get;private set;}
        private IEnumerable<PlayableOwner> Owners=>offline==null?new[]{PlayableOwner.Player,PlayableOwner.Enemy}:Enumerable.Range(0,offline.Roster.Count).Select(i=>(PlayableOwner)i);
        private bool HasOwner(PlayableOwner owner)=>Owners.Contains(owner);
        private bool TryOwner(string id,out PlayableOwner owner){owner=PlayableOwner.Player;if(offline==null){if(id==PlayerId)return true;owner=PlayableOwner.Enemy;return id=="enemy-1";}for(int i=0;i<offline.Roster.Count;i++)if(offline.Roster[i].Id==id){owner=(PlayableOwner)i;return true;}return false;}
        internal bool Authorizes(string id)=>TryOwner(id,out var owner)&&!eliminated.Contains(owner);
        internal PlayableOwner OwnerFor(string id){if(!TryOwner(id,out var owner))throw new ArgumentException("Not a gameplay owner.");return owner;}
        private string OwnerName(PlayableOwner owner)=>offline==null?(owner==PlayableOwner.Player?PlayerId:"enemy-1"):offline.Roster[(int)owner].Id;
        private int TeamOf(PlayableOwner owner)=>offline==null?(int)owner:offline.Roster[(int)owner].Team;
        private bool Hostile(PlayableOwner a,PlayableOwner b)=>TeamOf(a)!=TeamOf(b);
        private int HomeSite(PlayableOwner owner)=>offline==null?(owner==PlayableOwner.Player?1:2):offline.Starts[offline.Assignments[(int)owner]].SiteId;
        private PlayablePublicScoutObjective[] PublicObjectives(PlayableOwner owner)=>offline==null?TerritoryRules.PublicScoutObjectives(profile,owner):offline.Starts.Where(s=>s.SiteId!=HomeSite(owner)).Select(s=>new PlayablePublicScoutObjective(s.SiteId,s.ExplorerAnchor,PlayablePublicScoutObjectiveRole.PossibleEnemyStart,true)).ToArray();
        private NavObstacle[] StaticObstacles()=>offline==null?PlayableMap.StaticObstacles(profile):offline.Obstacles.ToArray();
        private NavObstacle[] SolidObstacles()=>offline==null?PlayableMap.SolidObstacles(profile):offline.Obstacles.ToArray();
        internal PlayableDomain(PlayableProfile profile,long generation,OfflineMatchConfiguration configuration):this(profile,generation,false,false,null,configuration){}
        internal IReadOnlyList<string> EliminatedOwners=>eliminated.OrderBy(x=>(int)x).Select(x=>offline==null?(x==PlayableOwner.Player?PlayerId:"enemy-1"):offline.Roster[(int)x].Id).ToArray();
        private void MaterializeOffline()
        {
            foreach(var owner in Owners){var start=offline.Starts[offline.Assignments[(int)owner]];AddBuilding(owner,PlayableBuildingKind.Headquarters,start.Position,true);var b=buildings[sites[start.SiteId].CenterId];b.Heading=start.Heading;}
            foreach(var entity in offline.ScenarioBuildings.OrderBy(b=>b.SlotId==0?0:1)){
                int index=Enumerable.Range(0,offline.Roster.Count).Where(i=>offline.Roster[i].LogicalPlayer==entity.LogicalPlayer).DefaultIfEmpty(-1).First();if(index<0)continue;
                var owner=(PlayableOwner)index;var state=sites[entity.SiteId];int id=nextId++;int parent=entity.SlotId==0?0:state.CenterId;if(entity.SlotId!=0&&(parent==0||buildings[parent].Owner!=owner))throw new ArgumentException("Scenario parent binding mismatch.");
                var b=new Building{TermsRevision=profile.Revision,Id=id,Owner=owner,Kind=entity.Kind,SiteId=entity.SiteId,SlotId=entity.SlotId,ParentId=parent,Position=entity.Position,Heading=entity.Heading,PaidCost=TerritoryRules.Cost(profile,entity.Kind),Health=TerritoryRules.Health(profile,entity.Kind),Build=TerritoryRules.Duration(profile,entity.Kind),Ready=true,Phase=ConstructionPhase.Ready};buildings.Add(id,b);if(entity.SlotId==0){state.CenterId=id;state.Locked=owner;state.Claimant=owner;state.Progress=1;}
            }
            RebuildGeometry();
            foreach(var owner in Owners){var start=offline.Starts[offline.Assignments[(int)owner]];int id=SpawnUnit(start.ExplorerAnchor,owner,PlayableEntityKind.Explorer);navigation.Crowd.TryGet(id,out var n);n.Heading=start.Heading+Math.PI;units[id].Turret=n.Heading;}
            for(int scenarioIndex=0;scenarioIndex<offline.ScenarioUnits.Count;scenarioIndex++){var e=offline.ScenarioUnits[scenarioIndex];int index=Enumerable.Range(0,offline.Roster.Count).Where(i=>offline.Roster[i].LogicalPlayer==e.LogicalPlayer).DefaultIfEmpty(-1).First();if(index<0)continue;int id=SpawnUnit(e.Position,(PlayableOwner)index,e.Kind);navigation.Crowd.TryGet(id,out var n);n.Heading=e.Heading;units[id].Turret=e.Heading;units[id].ScenarioIndex=scenarioIndex;units[id].AuthoredUpgrade=e.Upgraded;if(e.Kind==PlayableEntityKind.Tank)navigation.Crowd.SetSpeed(id,PlayableUnitRules.Speed(profile,e.Kind,e.Upgraded));}
            RefreshVision();
        }
        private void CheckOfflineOutcome()
        {
            foreach(var owner in Owners.Where(x=>!eliminated.Contains(x)).ToArray())if(!buildings.Values.Any(b=>b.Owner==owner&&TerritoryRules.Center(b.Kind)&&b.Phase!=ConstructionPhase.Pending)){
                eliminated.Add(owner);
                foreach(var u in units.Values.Where(u=>u.Owner==owner)){InterruptBurst(u);u.Target=0;u.ExplicitTarget=false;u.HasAttackMove=false;u.CurrentOrder=null;navigation.Stop(u.Id,true);}
                foreach(var b in buildings.Values.Where(b=>b.Owner==owner).ToArray()){b.Repair=null;b.Orders.Clear();b.RepeatTank=false;if(b.Phase==ConstructionPhase.Pending)RemoveBuilding(b,false);}
                research.Remove(owner);foreach(var key in centerDamage.Keys.ToArray())if(centerDamage[key].Owner==owner||!units.ContainsKey(centerDamage[key].AttackerId))centerDamage.Remove(key);
                foreach(var site in sites.Values)if(site.Claimant==owner){site.Claimant=null;site.Progress=0;if(site.Locked==owner)site.Locked=null;}
            }
            var teams=Owners.Where(x=>!eliminated.Contains(x)).Select(TeamOf).Distinct().ToArray();
            if(teams.Length==1&&offline.Roster.Select(x=>x.Team).Distinct().Count()>1){WinnerTeam=teams[0];Outcome=PlayableMatchOutcome.TeamWon;}
        }
    }
    // Explicit compatibility boundary: historical two-owner diagnostic genesis,
    // defenders and caller-controlled AI activation are confined to this adapter.
    internal static class TwoOwnerDiagnosticAdapter
    {
        internal static PlayableDomain Create(PlayableProfile p,long generation,bool patrol)=>new PlayableDomain(p,generation,patrol);
    }
    public sealed class OfflineParticipantAuthority
    {
        private readonly PlayableDomain domain;public OfflineMatchConfiguration Configuration{get;} public long Tick=>domain.Tick;
        public int? WinnerTeam=>domain.WinnerTeam;public IReadOnlyList<string> EliminatedOwners=>domain.EliminatedOwners;
        public NavigationSession Navigation=>domain.Navigation;
        public OfflineParticipantAuthority(OfflineMatchConfiguration config,long generation){Configuration=config??throw new ArgumentNullException(nameof(config));domain=new PlayableDomain(config.Profile,generation,config);}
        private OfflineParticipantAuthority(OfflineMatchConfiguration config,PlayableDomain restored){Configuration=config;domain=restored;}
        public PlayableCommandReceipt Apply(PlayableCommand command){var status=domain.Apply(command,out var message);return new PlayableCommandReceipt(command?.Sequence??0,Tick,status,message,0,command?.PlayerId);}
        public void Step(double seconds){if(seconds<=0||double.IsNaN(seconds)||double.IsInfinity(seconds))throw new ArgumentOutOfRangeException(nameof(seconds));domain.Step(seconds);}
        public PlayableSnapshot View(string id)=>domain.PlayerSnapshot(Tick,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,Configuration.Seed,domain.OwnerFor(id));
        public byte[] CaptureBytes()=>domain.CaptureWorldBytes(Configuration.Seed,Configuration.SourceIdentity);
        public static OfflineParticipantAuthority Restore(byte[] bytes,OfflineMatchConfiguration config)=>new OfflineParticipantAuthority(config,PlayableDomain.RestoreConfiguredWorldBytes(bytes,config.Profile,config.Seed,config.SourceIdentity,config));
        internal PlayableDomain Domain=>domain;
    }
}
