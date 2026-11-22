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
        HeavyDamage, MovementStarted, MovementStopped, BaseThreat, ProductionLimited,
        OrbitalScanApplied, OrbitalMineInstalled, OrbitalControlHacked, OrbitalSystemsDisabled, OrbitalNuclearImpact,
        OrbitalReinforcementStarted, OrbitalReinforcementReady, OrbitalStasisApplied, OrbitalStasisReleased,
        OrbitalNanobotsDeployed, OrbitalProductionBoosted, OrbitalNuclearDescent
    }
    public static class PlayableSoundPolicy
    {
        public static bool Ability(PlayableSoundKind kind) =>
            kind == PlayableSoundKind.OrbitalScanApplied || kind == PlayableSoundKind.OrbitalMineInstalled ||
            kind == PlayableSoundKind.OrbitalControlHacked || kind == PlayableSoundKind.OrbitalSystemsDisabled ||
            kind == PlayableSoundKind.OrbitalReinforcementStarted || kind == PlayableSoundKind.OrbitalReinforcementReady ||
            kind == PlayableSoundKind.OrbitalStasisApplied || kind == PlayableSoundKind.OrbitalStasisReleased ||
            kind == PlayableSoundKind.OrbitalNanobotsDeployed || kind == PlayableSoundKind.OrbitalProductionBoosted ||
            kind == PlayableSoundKind.OrbitalNuclearDescent;
        // Private economics and alerts must never cross to another owner, even in shared vision.
        public static bool OwnerOnly(PlayableSoundKind kind)=>kind==PlayableSoundKind.ProductionQueued||kind==PlayableSoundKind.ProductionCancelled||kind==PlayableSoundKind.ProductionStarted||kind==PlayableSoundKind.ProductionComplete||kind==PlayableSoundKind.ResearchQueued||kind==PlayableSoundKind.ResearchCancelled||kind==PlayableSoundKind.ResearchStarted||kind==PlayableSoundKind.ResearchComplete||kind==PlayableSoundKind.UpgradeStarted||kind==PlayableSoundKind.UpgradeCancelled||kind==PlayableSoundKind.UpgradeComplete||kind==PlayableSoundKind.BaseThreat||kind==PlayableSoundKind.ProductionLimited;
    }
    // Visual-only terminal flight facts, projected with the existing observed sound event.
    // No renderer geometry or checkpoint state participates in damage resolution.
    public sealed class PlayableDirectContact
    {
        public readonly int ProjectileId,TargetId;
        public readonly BallisticPoint From; public readonly BallisticPoint? Impact; public readonly bool Rocket, DamagedUnit;
        public PlayableDirectContact(int projectileId,int targetId,BallisticPoint from,BallisticPoint? impact=null,bool rocket=false,bool damagedUnit=false)
        {ProjectileId=projectileId;TargetId=targetId;From=from;Impact=impact;Rocket=rocket;DamagedUnit=damagedUnit;}
    }
    // Transient observed facts only: never part of a world checkpoint or AI input.
    public sealed class PlayableSoundEvent
    {
        public readonly long Id,Tick;
        // Optional audio-only phase deadline at the fixed 30 Hz authority cadence.
        public readonly long PhaseEndTick;
        public readonly PlayableSoundKind Kind;
        public readonly NavPoint Position;
        public readonly PlayableEntityKind Weapon;
        public readonly bool Building; // Source on a shot, target on an impact.
        // Permitted transient unit-death pose. Not an authority entity or checkpoint.
        public readonly int UnitId;
        public readonly bool RocketLaunch;
        public readonly int ShotSide; // Transient launcher articulation; never authority state.
        public readonly double HullHeading; public readonly double Altitude;public readonly PlayableOwner? Owner;
        public readonly PlayableDirectContact Contact;
        public readonly int? ShkvalDamagedUnits;
        public PlayableSoundEvent(long id,long tick,PlayableSoundKind kind,NavPoint position,PlayableEntityKind weapon,bool building=false,int unitId=0,double hullHeading=0,PlayableDirectContact contact=null,double altitude=0,PlayableOwner? owner=null,int shotSide=-1,long phaseEndTick=0,bool rocketLaunch=false,int? shkvalDamagedUnits=null)
        {ShkvalDamagedUnits=shkvalDamagedUnits;RocketLaunch=rocketLaunch;PhaseEndTick=phaseEndTick;ShotSide=shotSide;Altitude=altitude;Owner=owner;Contact=contact;Id=id;Tick=tick;Kind=kind;Position=position;Weapon=weapon;Building=building;UnitId=unitId;HullHeading=hullHeading;}
    }
}
