using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Immutable authored content, not a balance profile or a campaign scripting engine.
    public static class CampaignReconOpening
    {
        public const string Id="disputed-territory-opening-v1";
        public const string Revision="mission-4-horizontal-b";
        public static readonly string DefinitionHash=NavContract.Digest(w=>{w.Write(Id);w.Write(Revision);w.Write("desert-horizontal-b:quarry-outpost-gate-final:roster-18");});
        public const string Briefing="Вас, молодого боевого офицера корпорации, направили на удалённую планету Внешних миров. Разведка сообщает, что конкуренты продолжают незаконную добычу на недавно приобретённой территории: обследуйте район, остановите работы и при встрече с сопротивлением действуйте по обстоятельствам.";
        public static readonly NavPoint Landing=new NavPoint(-218,160),Rendezvous=new NavPoint(-176,112),Mine=new NavPoint(-115,130),Explore=new NavPoint(-124,122);
        public static readonly NavPoint Fork=new NavPoint(-57,16),West=new NavPoint(-35,-158),East=new NavPoint(68,122),Convergence=new NavPoint(129,-48),Final=new NavPoint(201,-165);
        public static readonly NavObstacle Gate=new NavObstacle(153,-78,205,-74);
        public static readonly NavObstacle[] GuardRegions={new NavObstacle(-116,42,-62,101),new NavObstacle(-69,-195,2,-119),new NavObstacle(28,82,111,163),new NavObstacle(160,-203,239,-123)};
        public static readonly int[][] GuardIndices={new[]{8,9},new[]{10,11},new[]{12},new[]{13,14,15,16,17}};
        public static readonly int[][] GuardSites={Array.Empty<int>(),new[]{2,3},new[]{4},new[]{5,6,7}};
        public static bool Contains(NavObstacle r,NavPoint p,double margin=0)=>p.X>=r.MinX+margin&&p.X<=r.MaxX-margin&&p.Z>=r.MinZ+margin&&p.Z<=r.MaxZ-margin;
        public static OfflineMatchConfiguration Configuration(PlayableProfile profile,bool assaultEquipment=true)
        {
            if(!(profile.AuthoredMap is CampaignOpeningMap))throw new ArgumentException("Campaign terrain required.");
            var starts=new[]{new OfflineStart("landing",-1,Landing,Landing,pin:1),new OfflineStart("mine-owner",-2,new NavPoint(-109,110),new NavPoint(-109,110),pin:2),new OfflineStart("waiting-squad",-3,Rendezvous,Rendezvous,pin:3)};
            return new OfflineMatchConfiguration(profile,Id,profile.AuthoredMap.Id,"authored-canyon-horizontal-b",19092026,
                new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("corporation",2,2,OfflineControl.Scenario),new OfflineParticipant("recon-squad",3,1,OfflineControl.Scenario)},
                starts,profile.AuthoredMap.Sites(profile),profile.AuthoredMap.MovementBlockers.ToArray(),new double[3,3],
                // Mission-local authored loadout: existing Balance Lab assault burst/spread.
                // The false option retains the pre-assault loadout on this same map; old mission-3
                // snapshots remain bound to their original content/source, never reinterpreted as B.
                scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,Landing,upgraded:assaultEquipment),new OfflineScenarioUnit(3,PlayableEntityKind.Explorer,new NavPoint(-183,106),upgraded:assaultEquipment),new OfflineScenarioUnit(3,PlayableEntityKind.Explorer,new NavPoint(-183,112),upgraded:assaultEquipment),new OfflineScenarioUnit(3,PlayableEntityKind.Explorer,new NavPoint(-183,118),upgraded:assaultEquipment),
                    new OfflineScenarioUnit(3,PlayableEntityKind.Explorer,new NavPoint(121,-54),upgraded:assaultEquipment),new OfflineScenarioUnit(3,PlayableEntityKind.Explorer,new NavPoint(121,-42),upgraded:assaultEquipment),new OfflineScenarioUnit(3,PlayableEntityKind.Explorer,new NavPoint(129,-54),upgraded:assaultEquipment),new OfflineScenarioUnit(3,PlayableEntityKind.Explorer,new NavPoint(129,-42),upgraded:assaultEquipment),
                    new OfflineScenarioUnit(2,PlayableEntityKind.Explorer,new NavPoint(-86,68)),new OfflineScenarioUnit(2,PlayableEntityKind.Explorer,new NavPoint(-86,82)),
                    new OfflineScenarioUnit(2,PlayableEntityKind.Explorer,new NavPoint(-25,-164.4)),new OfflineScenarioUnit(2,PlayableEntityKind.Explorer,new NavPoint(-25,-151.6)),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(59,105)),
                    new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(191,-176)),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(211,-176)),new OfflineScenarioUnit(2,PlayableEntityKind.Explorer,new NavPoint(192,-186)),new OfflineScenarioUnit(2,PlayableEntityKind.Explorer,new NavPoint(201,-186)),new OfflineScenarioUnit(2,PlayableEntityKind.Explorer,new NavPoint(210,-186))},
                terrain:profile.AuthoredMap.Id,scenarioBuildings:Buildings(profile),missionId:Id);
        }
        private static OfflineScenarioBuilding[] Buildings(PlayableProfile p)
        {
            var sites=p.AuthoredMap.Sites(p);var result=sites.Select(s=>new OfflineScenarioBuilding(2,s.Kind,s.Id,0,s.Position)).ToList();
            var post=sites.Single(s=>s.Id==4);result.Add(new OfflineScenarioBuilding(2,PlayableBuildingKind.Factory,4,1,post.Slots[0].Position));result.Add(new OfflineScenarioBuilding(2,PlayableBuildingKind.Refinery,4,2,post.Slots[1].Position));return result.ToArray();
        }
    }
    public sealed class CampaignObjective
    {
        public CampaignObjective(string id,string text,bool complete,NavPoint point){Id=id;Text=text;Complete=complete;Point=point;}
        public string Id{get;} public string Text{get;} public bool Complete{get;} public NavPoint Point{get;}
    }
    public sealed class CampaignMissionView
    {
        public CampaignMissionView(bool reinforced,bool discovered,bool destroyed,bool fork=false,bool west=false,bool east=false,bool gate=false,bool finalReinforced=false,bool victory=false,int tutorialSeen=0,int tutorialCompleted=0,int tutorialQueued=-1,bool tutorialPauseHeld=false,bool firstCombat=false,string[] radio=null)
        {
            Reinforced=reinforced;MineDiscovered=discovered;MineDestroyed=destroyed;ForkRevealed=fork;WestCleared=west;EastCleared=east;GateOpen=gate;FinalReinforced=finalReinforced;Victory=victory;
            var goals=new List<CampaignObjective>{new CampaignObjective("rendezvous","Добраться до сборного пункта",reinforced,CampaignReconOpening.Rendezvous)};
            if(reinforced)goals.Add(new CampaignObjective("recon","Продолжить разведку",discovered,CampaignReconOpening.Explore));
            if(discovered)goals.Add(new CampaignObjective("first-mine","Уничтожить незаконную шахту",destroyed,CampaignReconOpening.Mine));
            if(destroyed)goals.Add(new CampaignObjective("next-passage","Продолжить разведку прохода",fork,CampaignReconOpening.Fork));
            if(fork){goals.Add(new CampaignObjective("west","Зачистить южный карьер",west,CampaignReconOpening.West));goals.Add(new CampaignObjective("east","Зачистить северный форпост",east,CampaignReconOpening.East));}
            if(gate)goals.Add(new CampaignObjective("final",finalReinforced?"Остановить добычу в основном комплексе":"Продолжить разведку перевала",victory,finalReinforced?CampaignReconOpening.Final:CampaignReconOpening.Convergence));
            Objectives=goals.AsReadOnly();
            TutorialSeen=tutorialSeen;TutorialCompleted=tutorialCompleted;TutorialQueued=tutorialQueued;TutorialPauseHeld=tutorialPauseHeld;FirstCombat=firstCombat;Radio=Array.AsReadOnly(radio??Array.Empty<string>());
        }
        public string MissionId=>CampaignReconOpening.Id;
        public bool ForkRevealed{get;} public bool WestCleared{get;} public bool EastCleared{get;} public bool GateOpen{get;} public bool FinalReinforced{get;} public bool Victory{get;}
        public bool Reinforced{get;} public bool MineDiscovered{get;} public bool MineDestroyed{get;}
        public int TutorialSeen{get;} public int TutorialCompleted{get;} public int TutorialQueued{get;} public bool TutorialPauseHeld{get;} public bool FirstCombat{get;} public IReadOnlyList<string> Radio{get;}
        public IReadOnlyList<CampaignObjective> Objectives{get;}
    }
    public enum CampaignTutorialControlKind { Show, Dismiss, Complete, SuspendForDevice, ResumeForDevice }
    public sealed class CampaignTutorialControl
    { public CampaignTutorialControl(CampaignTutorialControlKind kind,int topic=-1){Kind=kind;Topic=topic;} public CampaignTutorialControlKind Kind{get;} public int Topic{get;} }
    public sealed class CampaignOpeningMap:IPlayableTerrain,INavArrivalPointTerrain
    {
        public string Id=>"disputed-territory-canyon-opening-v1";public int Revision=>3;
        public double HalfExtent=>240;public double DirectFireHeight=>1;public int SurfaceSemanticsVersion=>1;
        public IReadOnlyList<MapSupport> Supports{get;} public IReadOnlyList<NavObstacle> Solids{get;} public IReadOnlyList<NavObstacle> MovementBlockers=>Solids;
        public IReadOnlyList<NavSurfaceTransition> SurfaceTransitions=>Array.Empty<NavSurfaceTransition>();
        private readonly NavGeometry geometry;private readonly TerrainLocations locations;
        TerrainLocations INavArrivalPointTerrain.ArrivalLocations=>locations;
        public CampaignOpeningMap()
        {
            Supports=Array.AsReadOnly(new[]{new MapSupport("desert-floor",new NavObstacle(-240,-240,240,240),false,surfaceId:"desert-floor")});
            // B is approved immutable authored polygon content; no solver or quota change.
            Solids=Array.AsReadOnly(CampaignCanyonB.Rocks());
            geometry=new NavGeometry(HalfExtent,Solids.ToArray(),Revision);locations=new TerrainLocations(geometry,Supports,SurfaceTransitions);
        }
        public TerritorySite[] Sites(PlayableProfile p)=>new[]{new TerritorySite(1,PlayableBuildingKind.Mine,CampaignReconOpening.Mine,p),
            new TerritorySite(2,PlayableBuildingKind.Mine,new NavPoint(-39,-174),p),new TerritorySite(3,PlayableBuildingKind.Mine,new NavPoint(-39,-142),p),
            new TerritorySite(4,PlayableBuildingKind.Outpost,new NavPoint(68,122),new[]{new TerritorySlot(1,new NavPoint(56,134),0),new TerritorySlot(2,new NavPoint(80,134),0)}),
            new TerritorySite(5,PlayableBuildingKind.Mine,new NavPoint(187,-153),p),new TerritorySite(6,PlayableBuildingKind.Mine,new NavPoint(201,-153),p),new TerritorySite(7,PlayableBuildingKind.Mine,new NavPoint(215,-153),p)};
        public NavPoint Headquarters(PlayableOwner owner)=>CampaignReconOpening.Landing; // Camera anchor only; no headquarters entity/site exists.
        public MapSupport SupportAt(NavPoint p)=>Supports[0].Contains(p)?Supports[0]:null;
        public double SurfaceHeight(NavPoint p)=>0;public NavPoint SurfaceGradient(NavPoint p)=>default;
        public bool SupportsFootprint(NavPoint p,double r)=>geometry.IsFree(p,r);
        public bool SupportsSweep(NavPoint a,NavPoint b,double r)=>geometry.SegmentFree(a,b,r);
        public bool TryLocate(NavPoint p,double r,out NavLocation l)=>locations.TryLocate(p,r,out l);
        public bool IsValidLocation(NavLocation l,double r)=>locations.Valid(l,r);
        public bool TryTraverse(NavLocation a,NavPoint b,double r,out NavLocation l)=>locations.Traverse(a,b,r,out l);
        public bool CompatibleCombatSurface(NavLocation a,NavLocation b)=>locations.Combat(a,b);
    }
}
