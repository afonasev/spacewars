namespace Spacewars.Simulation
{
    public enum MatchEventKind { BuildingAttacked, UnitsAttacked, BuildingCompleted, UnitReady, ResearchCompleted }
    // Transient presentation fact. No hidden target identity, AI input or checkpoint data.
    public sealed class PlayableMatchEvent
    {
        public long Id{get;} public long Tick{get;} public PlayableOwner Owner{get;}
        public MatchEventKind Kind{get;} public NavPoint Point{get;}
        public bool Battle=>Kind==MatchEventKind.BuildingAttacked||Kind==MatchEventKind.UnitsAttacked;
        public PlayableMatchEvent(long id,long tick,PlayableOwner owner,MatchEventKind kind,NavPoint point)
        {Id=id;Tick=tick;Owner=owner;Kind=kind;Point=point;}
    }
}
