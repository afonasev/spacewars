namespace Spacewars.Simulation
{
    public sealed class PlayableIncomeEvent
    {
        public PlayableIncomeEvent(long tick,int buildingId,PlayableOwner owner,PlayableBuildingKind kind,NavPoint position,double amount)
        {Tick=tick;BuildingId=buildingId;Owner=owner;Kind=kind;Position=position;Amount=amount;}
        public long Tick{get;} public int BuildingId{get;} public PlayableOwner Owner{get;}
        public PlayableBuildingKind Kind{get;} public NavPoint Position{get;} public double Amount{get;}
    }
}
