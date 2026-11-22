namespace Spacewars.Simulation
{
    public enum MatchEventKind { BuildingAttacked, UnitsAttacked, BuildingCompleted, UnitReady, ResearchCompleted, MineCaptured, UnitCaptured, RelayControlLost, MineHit, MineTriggered, UpgradeCompleted, ProductionCapacity, ProductionCredits, ProductionExit, MineDepleted, NuclearLaunch, RelayCaptured, DominationGained, DominationLost, DominationNinety, DominationTen, DominationCountdown }
    // Transient presentation fact. No hidden target identity, AI input or checkpoint data.
    public sealed class PlayableMatchEvent
    {
        public long Id{get;} public long Tick{get;} public PlayableOwner Owner{get;}
        public long CauseId{get;} public MatchEventKind Kind{get;} public NavPoint Point{get;}
        public bool Shared=>Kind is MatchEventKind.DominationGained or MatchEventKind.DominationLost or MatchEventKind.DominationNinety
            or MatchEventKind.DominationTen or MatchEventKind.DominationCountdown;
        public bool Teamwide=>Kind==MatchEventKind.RelayCaptured||(Kind==MatchEventKind.RelayControlLost&&CauseId!=0);
        public bool Positional=>Kind is not (MatchEventKind.DominationNinety or MatchEventKind.DominationTen or MatchEventKind.DominationCountdown);
        public bool Battle=>Kind==MatchEventKind.BuildingAttacked||Kind==MatchEventKind.UnitsAttacked;
        public PlayableMatchEvent(long id,long tick,PlayableOwner owner,MatchEventKind kind,NavPoint point,long causeId=0)
        {CauseId=causeId;Id=id;Tick=tick;Owner=owner;Kind=kind;Point=point;}
    }
}
