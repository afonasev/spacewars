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
    // Transient observed facts only: never part of a world checkpoint or AI input.
    public sealed class PlayableSoundEvent
    {
        public readonly long Id,Tick;
        public readonly PlayableSoundKind Kind;
        public readonly NavPoint Position;
        public readonly PlayableEntityKind Weapon;
        public readonly bool Building;
        public PlayableSoundEvent(long id,long tick,PlayableSoundKind kind,NavPoint position,PlayableEntityKind weapon,bool building=false)
        {Id=id;Tick=tick;Kind=kind;Position=position;Weapon=weapon;Building=building;}
    }
}
