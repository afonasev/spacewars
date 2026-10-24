namespace Spacewars.Simulation
{
    public enum PlayableSoundKind
    {
        Shot, Impact, Destroyed,
        ConstructionStarted, ConstructionCancelled, ConstructionComplete,
        DemolitionStarted, Demolished,
        RepairStarted, RepairCancelled, RepairComplete,
        ProductionQueued, ProductionCancelled, ProductionStarted, ProductionComplete,
        ResearchQueued, ResearchCancelled, ResearchStarted, ResearchComplete,
        UpgradeStarted, UpgradeCancelled, UpgradeComplete,
        HeavyDamage, MovementStarted, MovementStopped, BaseThreat, ProductionLimited
    }
    public static class PlayableSoundPolicy
    {
        // Private economics and alerts must never cross to another owner, even in shared vision.
        public static bool OwnerOnly(PlayableSoundKind kind)=>kind==PlayableSoundKind.ProductionQueued||kind==PlayableSoundKind.ProductionCancelled||kind==PlayableSoundKind.ProductionStarted||kind==PlayableSoundKind.ProductionComplete||kind==PlayableSoundKind.ResearchQueued||kind==PlayableSoundKind.ResearchCancelled||kind==PlayableSoundKind.ResearchStarted||kind==PlayableSoundKind.ResearchComplete||kind==PlayableSoundKind.UpgradeStarted||kind==PlayableSoundKind.UpgradeCancelled||kind==PlayableSoundKind.UpgradeComplete||kind==PlayableSoundKind.BaseThreat||kind==PlayableSoundKind.ProductionLimited;
    }
    // Visual-only terminal flight facts, projected with the existing observed sound event.
    // No renderer geometry or checkpoint state participates in damage resolution.
    public sealed class PlayableDirectContact
    {
        public readonly int ProjectileId,TargetId;
        public readonly BallisticPoint From; public readonly BallisticPoint? Impact;
        public PlayableDirectContact(int projectileId,int targetId,BallisticPoint from,BallisticPoint? impact=null)
        {ProjectileId=projectileId;TargetId=targetId;From=from;Impact=impact;}
    }
    // Transient observed facts only: never part of a world checkpoint or AI input.
    public sealed class PlayableSoundEvent
    {
        public readonly long Id,Tick;
        public readonly PlayableSoundKind Kind;
        public readonly NavPoint Position;
        public readonly PlayableEntityKind Weapon;
        public readonly bool Building;
        // Permitted transient unit-death pose. Not an authority entity or checkpoint.
        public readonly int UnitId;
        public readonly double HullHeading; public readonly double Altitude;public readonly PlayableOwner? Owner;
        public readonly PlayableDirectContact Contact;
        public PlayableSoundEvent(long id,long tick,PlayableSoundKind kind,NavPoint position,PlayableEntityKind weapon,bool building=false,int unitId=0,double hullHeading=0,PlayableDirectContact contact=null,double altitude=0,PlayableOwner? owner=null)
        {Altitude=altitude;Owner=owner;Contact=contact;Id=id;Tick=tick;Kind=kind;Position=position;Weapon=weapon;Building=building;UnitId=unitId;HullHeading=hullHeading;}
    }
}
