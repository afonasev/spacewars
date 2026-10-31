namespace Spacewars.Simulation
{
    // Immutable permitted projection; no renderer object or gameplay building remains alive.
    public sealed class PlayableBuildingDestruction
    {
        public int Id{get;} public int SiteId{get;} public int SlotId{get;}
        public PlayableOwner Owner{get;} public PlayableBuildingKind Kind{get;} public NavPoint Position{get;}
        public double Heading{get;} public double Progress{get;} public bool Upgraded{get;}
        public long StartTick{get;} public long EndTick{get;}
        public PlayableBuildingDestruction(int id,int siteId,int slotId,PlayableOwner owner,PlayableBuildingKind kind,NavPoint position,double heading,double progress,bool upgraded,long startTick,long endTick)
        {Id=id;SiteId=siteId;SlotId=slotId;Owner=owner;Kind=kind;Position=position;Heading=heading;Progress=progress;Upgraded=upgraded;StartTick=startTick;EndTick=endTick;}
    }
}
