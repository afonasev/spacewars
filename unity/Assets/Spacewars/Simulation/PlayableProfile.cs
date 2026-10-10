using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    [Serializable]
    public sealed class PlayableProfileData
    {
        public int schemaVersion;
        public double scienceHealth;
        public double scienceBuildSeconds;
        public double scienceVisionRange;
        public double scienceFootprintRadius;
        public double scienceCreditCost;
        public double scienceModelHeightMeters;
        public double scienceModelScale;
        public double refineryUpgradeCost;
        public double refineryUpgradeSeconds;
        public double refineryUpgradedIncome;
        public double refineryTurbineSpeed;
        public double tankChassisGlowIntensity;
        public double tankChassisCost, tankChassisSeconds, tankChassisSpeed;
        public double explorerAssaultCost, explorerAssaultSeconds, explorerAssaultBurstSize, explorerAssaultSpreadDeg;
        public double shkvalGuidanceCost, shkvalGuidanceSeconds, shkvalGuidanceRange;

        public double lifecycleMarkerPixels;
        public double lifecycleMarkerPulseHz;
        public double lifecycleMarkerMinOpacity;
        public double lifecycleMarkerOffsetMeters;

        public double buildingSaleRefundRatio;
        public double buildingSaleCombatLockoutSec;
        public double buildingSaleDemolitionSec;
        public double buildingRepairDurationSec;
        public double buildingRepairCostRatio;
        public double buildingRepairCombatLockoutSec;
        public double saleMarkerScale;

        public double minimapCompactSize;
        public double minimapTacticalSize;
        public double minimapTerrainSaturation;
        public double minimapTerrainBrightness;
        public double minimapTerrainTierContrast;
        public double minimapMarkerSize;
        public double minimapMarkerStroke;
        public double minimapTerrainStroke;
        public double minimapCameraStroke;

        public double fogTintR;
        public double fogTintG;
        public double fogTintB;
        public double fogMemoryBrightness;
        public double fogMemoryDesaturation;

        public double headquartersVisionRange;
        public double outpostVisionRange;
        public double factoryVisionRange;
        public double refineryVisionRange;
        public double mineVisionRange;
        public double constructionVisionMultiplier;
        public double visionCellSize;
        public double fogRevealMs;
        public double fogConcealMs;
        public double fogEdgeFeather;
        public double fogExploredOpacity;
        public double fogUnseenOpacity;
        public double minimapLandmarkSize;
        public double minimapLandmarkOutline;
        public double minimapUnitMarkerSize;

        public double outpostHealth;
        public double outpostBuildSeconds;
        public double outpostCreditCost;
        public double outpostIncomePerPeriod;
        public double outpostFootprintRadius;
        public double outpostModelHeightMeters;
        public double outpostModelScale;
        public double mineHealth;
        public double mineBuildSeconds;
        public double mineCreditCost;
        public double mineIncomePerPeriod;
        public double mineFootprintRadius;
        public double mineModelHeightMeters;
        public double mineModelScale;
        public double headquartersCaptureRadius;
        public double headquartersCaptureSeconds;
        public double outpostCaptureRadius;
        public double outpostCaptureSeconds;
        public double mineCaptureRadius;
        public double mineCaptureSeconds;
        public double headquartersSlots;
        public double outpostSlots;
        public double slotRingRadius;
        public double ordinaryPadRadius;
        public double padBorderRatio;
        public double capturePulseHz;
        public double constructionDamageMultiplier;
        public double evacuationClearance;
        public double evacuationRetrySeconds;
        public double outpostX;
        public double outpostZ;
        public double mineX;
        public double mineZ;

        public double shkvalCollisionRadius;
        public double shkvalModelRadius;
        public double shkvalModelScale;
        public double shkvalSpeed;
        public double shkvalTurnSpeed;
        public double shkvalTurretTurnSpeed;
        public double shkvalAimToleranceRad;
        public double shkvalRange;
        public double shkvalVision;
        public int shkvalHealth;
        public double shkvalStopForMs;
        public int shkvalPopulationCost;
        public int shkvalCreditCost;
        public int shkvalProductionMenuOrder;
        public double shkvalProductionDurationSec;
        public int shkvalDamage;
        public double shkvalFireIntervalMs;
        public double shkvalProjectileSpeed;
        public double shkvalArcHeight;
        public double shkvalBlastRadius;
        public double shkvalMarkerStartRadius;
        public double shkvalMarkerOpacity;
        public double shkvalLeadSpeedThreshold;
        public double shkvalLeadFalloff;
        public double shkvalLaunchHeight;
        public double shkvalProjectileRadius;
        public double shkvalProjectileLength;
        public double shkvalBuildingCollisionHeight;
        public double shkvalMaxLeadTimeSec;
        public double shkvalFriendlyFirePenalty;
        public double shkvalBaseRange;
        public double shkvalExhaustStartupSec;
        public double shkvalExhaustRadius;
        public double shkvalExhaustLength;
        public double shkvalExhaustStartBoost;
        public double shkvalExhaustOpacity;
        public double weaponMuzzleCoreRatio;
        public double impactEffectSec;
        public double ballisticWallHeight;
        public double explorerCollisionRadius;
        public double explorerModelRadius;
        public double explorerModelScale;
        public double explorerSpeed;
        public double explorerTurnSpeed;
        public double explorerTurretTurnSpeed;
        public double explorerAimToleranceRad;
        public double explorerRange;
        public double explorerVision;
        public int explorerHealth;
        public int explorerStopForMs;
        public int explorerPopulationCost;
        public int explorerCreditCost;
        public int explorerProductionMenuOrder;
        public int explorerProductionDurationSec;
        public int explorerDamage;
        public int explorerBurstSize;
        public int explorerBurstShotIntervalMs;
        public int explorerBurstPauseMs;
        public double explorerProjectileSpeed;
        public double explorerSpreadDeg;
        public double explorerTracerLength;
        public double explorerTracerThickness;
        public double explorerMuzzleOffset;
        public string profileId;
        public int revision;
        public string sourceCommit;
        public string sourceProfileId;
        public int sourceProfileRevision;
        public string sourceManifestSha256;

        public double unitOrderQueueLimit=64;
        public double groupMarchMaximumStretch=4;
        public double idleAutoDefenseMultiplier;
        public double followDistance, followArrivalTolerance;
        public double tankCollisionRadius, tankModelRadius, tankModelScale, tankSpeed, tankTurnSpeed, tankTurretTurnSpeed, tankAimToleranceRadians, tankRange, tankVisionRange;
        public int tankHealth, tankStopForMilliseconds, tankPopulationCost, tankCreditCost, tankProductionMenuOrder, tankProductionSeconds, tankWeaponDamage, tankWeaponReloadMilliseconds;
        public bool tankFiresWhileMoving;
        public string tankProjectileType;
        public double tankProjectileSpeed, tankProjectileCollisionRadius, projectileExtraRangePercent;

        public int startingCredits, incomePeriodSeconds;
        public int armyCapacity;
        public int matchScoreEarnedCreditsDivisor=2;
        public double buildingCancellationRefundRatio, unitCancellationRefundRatio;
        public int headquartersHealth, headquartersBuildSeconds, headquartersCreditCost, headquartersIncomePerPeriod;
        public double headquartersFootprintRadius, headquartersModelHeightMeters, headquartersModelScale;
        public int factoryHealth, factoryBuildSeconds, factoryCreditCost;
        public double factoryFootprintRadius, factoryModelHeightMeters, factoryModelScale;
        public int refineryHealth, refineryBuildSeconds, refineryCreditCost, refineryIncomePerPeriod;
        public double refineryFootprintRadius, refineryModelHeightMeters, refineryModelScale;

        public double arenaHalfExtent, playerHeadquartersX, enemyHeadquartersX, headquartersZ;
        public double playerFactoryPadX, playerFactoryPadZ, playerRefineryPadX, playerRefineryPadZ;
        public double enemyFactoryPadX, enemyFactoryPadZ, enemyRefineryPadX, enemyRefineryPadZ;
        public double centralObstacleHalfWidth, centralObstacleHalfDepth, passageHalfWidth;
        public double enemyAdvanceDelaySeconds, attackApproachRangeRatio, attackRepathSeconds, factoryExitDistance, defaultRallyDistance, targetPickRadiusMultiplier, buildingPickRadius;
        public double defenderOffsetX, defenderOffsetZ;
        public int navigationRequestsPerFrame, renderTargetFramesPerSecond;
        public double cameraEdgePanSpeed=8, cameraEdgePanZonePixels=28;
        public double cameraOrthoSize, cameraHeight, cameraOffsetX, cameraOffsetZ, cameraPanSpeed, cameraMinZoom, cameraMaxZoom, cameraZoomSpeed, selectionDragPixels;
    }

    public sealed class PlayableProfile
    {
        public double MinimapCompactSize {get; private set;}
        public double MinimapTacticalSize {get; private set;}
        public double MinimapTerrainSaturation {get; private set;}
        public double MinimapTerrainBrightness {get; private set;}
        public double MinimapTerrainTierContrast {get; private set;}
        public double MinimapMarkerSize {get; private set;}
        public double MinimapMarkerStroke {get; private set;}
        public double MinimapTerrainStroke {get; private set;}
        public double MinimapCameraStroke {get; private set;}
        public double HeadquartersVisionRange {get; private set;}
        public double OutpostVisionRange {get; private set;}
        public double FactoryVisionRange {get; private set;}
        public double RefineryVisionRange {get; private set;}
        public double MineVisionRange {get; private set;}
        public double ConstructionVisionMultiplier {get; private set;}
        public double VisionCellSize {get; private set;}
        public double FogRevealMs {get; private set;}
        public double FogConcealMs {get; private set;}
        public double FogEdgeFeather {get; private set;}
        public double FogExploredOpacity {get; private set;}
        public double FogUnseenOpacity {get; private set;}
        public double MinimapLandmarkSize {get; private set;}
        public double MinimapLandmarkOutline {get; private set;}
        public double MinimapUnitMarkerSize {get; private set;}
        public double FogTintR {get; private set;}
        public double FogTintG {get; private set;}
        public double FogTintB {get; private set;}
        public double FogMemoryBrightness {get; private set;}
        public double FogMemoryDesaturation {get; private set;}
        public double ScienceHealth {get; private set;}
        public double ScienceBuildSeconds {get; private set;}
        public double ScienceVisionRange {get; private set;}
        public double ScienceFootprintRadius {get; private set;}
        public double ScienceCreditCost {get; private set;}
        public double ScienceModelHeightMeters {get; private set;}
        public double ScienceModelScale {get; private set;}
        public double RefineryUpgradeCost {get; private set;}
        public double RefineryUpgradeSeconds {get; private set;}
        public double RefineryUpgradedIncome {get; private set;}
        public double RefineryTurbineSpeed {get; private set;}
        public double TankChassisGlowIntensity {get;private set;}
        public double TankChassisCost {get; private set;} public double TankChassisSeconds {get; private set;} public double TankChassisSpeed {get; private set;}
        public double ExplorerAssaultCost {get; private set;} public double ExplorerAssaultSeconds {get; private set;} public int ExplorerAssaultBurstSize {get; private set;} public double ExplorerAssaultSpreadDeg {get; private set;}
        public double ShkvalGuidanceCost {get; private set;} public double ShkvalGuidanceSeconds {get; private set;} public double ShkvalGuidanceRange {get; private set;}
        public double ShkvalCollisionRadius {get; private set;}
        public double ShkvalModelRadius {get; private set;}
        public double ShkvalModelScale {get; private set;}
        public double ShkvalSpeed {get; private set;}
        public double ShkvalTurnSpeed {get; private set;}
        public double ShkvalTurretTurnSpeed {get; private set;}
        public double ShkvalAimToleranceRad {get; private set;}
        public double ShkvalRange {get; private set;}
        public double ShkvalVision {get; private set;}
        public int ShkvalHealth {get; private set;}
        public double ShkvalStopForMs {get; private set;}
        public int ShkvalPopulationCost {get; private set;}
        public int ShkvalCreditCost {get; private set;}
        public int ShkvalProductionMenuOrder {get; private set;}
        public double ShkvalProductionDurationSec {get; private set;}
        public int ShkvalDamage {get; private set;}
        public double ShkvalFireIntervalMs {get; private set;}
        public double ShkvalProjectileSpeed {get; private set;}
        public double ShkvalArcHeight {get; private set;}
        public double ShkvalBlastRadius {get; private set;}
        public double ShkvalMarkerStartRadius {get; private set;}
        public double ShkvalMarkerOpacity {get; private set;}
        public double ShkvalLeadSpeedThreshold {get; private set;}
        public double ShkvalLeadFalloff {get; private set;}
        public double ShkvalLaunchHeight {get; private set;}
        public double ShkvalProjectileRadius {get; private set;}
        public double ShkvalProjectileLength {get; private set;}
        public double ShkvalBuildingCollisionHeight {get; private set;}
        public double ShkvalMaxLeadTimeSec {get; private set;}
        public double ShkvalFriendlyFirePenalty {get; private set;}
        public double ShkvalBaseRange {get; private set;}
        public double ShkvalExhaustStartupSec {get; private set;}
        public double ShkvalExhaustRadius {get; private set;}
        public double ShkvalExhaustLength {get; private set;}
        public double ShkvalExhaustStartBoost {get; private set;}
        public double ShkvalExhaustOpacity {get; private set;}
        public double WeaponMuzzleCoreRatio {get; private set;}
        public double ImpactEffectSec {get; private set;}
        public double BallisticWallHeight {get; private set;}
        public double ExplorerCollisionRadius {get; private set;}
        public double ExplorerModelRadius {get; private set;}
        public double ExplorerModelScale {get; private set;}
        public double ExplorerSpeed {get; private set;}
        public double ExplorerTurnSpeed {get; private set;}
        public double ExplorerTurretTurnSpeed {get; private set;}
        public double ExplorerAimToleranceRad {get; private set;}
        public double ExplorerRange {get; private set;}
        public double ExplorerVision {get; private set;}
        public int ExplorerHealth {get; private set;}
        public int ExplorerStopForMs {get; private set;}
        public int ExplorerPopulationCost {get; private set;}
        public int ExplorerCreditCost {get; private set;}
        public int ExplorerProductionMenuOrder {get; private set;}
        public int ExplorerProductionDurationSec {get; private set;}
        public int ExplorerDamage {get; private set;}
        public int ExplorerBurstSize {get; private set;}
        public int ExplorerBurstShotIntervalMs {get; private set;}
        public int ExplorerBurstPauseMs {get; private set;}
        public double ExplorerProjectileSpeed {get; private set;}
        public double ExplorerSpreadDeg {get; private set;}
        public double ExplorerTracerLength {get; private set;}
        public double ExplorerTracerThickness {get; private set;}
        public double ExplorerMuzzleOffset {get; private set;}
        public const string RequiredProfileId = "unity-owner-research-queue-u6-v1";
        public const int RequiredSchemaVersion = 9;

        public double BuildingSaleRefundRatio {get; private set;}
        public double BuildingSaleCombatLockoutSec {get; private set;}
        public double BuildingSaleDemolitionSec {get; private set;}
        public double BuildingRepairDurationSec {get; private set;}
        public double BuildingRepairCostRatio {get; private set;}
        public double BuildingRepairCombatLockoutSec {get; private set;}
        public double SaleMarkerScale {get; private set;}
        public double LifecycleMarkerPixels {get; private set;}
        public double LifecycleMarkerPulseHz {get; private set;}
        public double LifecycleMarkerMinOpacity {get; private set;}
        public double LifecycleMarkerOffsetMeters {get; private set;}
        private readonly PlayableProfileData sourceData;
        public string DisplayName { get; private set; }
        public PlayableProfileData CopyData() => NativeBalanceFields.Copy(sourceData);
        private PlayableProfile(PlayableProfileData data)
        {
            sourceData=NativeBalanceFields.Copy(data);DisplayName="Основной";
            ShkvalCollisionRadius=data.shkvalCollisionRadius;
            ShkvalModelRadius=data.shkvalModelRadius;
            ShkvalModelScale=data.shkvalModelScale;
            ShkvalSpeed=data.shkvalSpeed;
            ShkvalTurnSpeed=data.shkvalTurnSpeed;
            ShkvalTurretTurnSpeed=data.shkvalTurretTurnSpeed;
            ShkvalAimToleranceRad=data.shkvalAimToleranceRad;
            ShkvalRange=data.shkvalRange;
            ShkvalVision=data.shkvalVision;
            ShkvalHealth=data.shkvalHealth;
            ShkvalStopForMs=data.shkvalStopForMs;
            ShkvalPopulationCost=data.shkvalPopulationCost;
            ShkvalCreditCost=data.shkvalCreditCost;
            ShkvalProductionMenuOrder=data.shkvalProductionMenuOrder;
            ShkvalProductionDurationSec=data.shkvalProductionDurationSec;
            ShkvalDamage=data.shkvalDamage;
            ShkvalFireIntervalMs=data.shkvalFireIntervalMs;
            ShkvalProjectileSpeed=data.shkvalProjectileSpeed;
            ShkvalArcHeight=data.shkvalArcHeight;
            ShkvalBlastRadius=data.shkvalBlastRadius;
            ShkvalMarkerStartRadius=data.shkvalMarkerStartRadius;
            ShkvalMarkerOpacity=data.shkvalMarkerOpacity;
            ShkvalLeadSpeedThreshold=data.shkvalLeadSpeedThreshold;
            ShkvalLeadFalloff=data.shkvalLeadFalloff;
            ShkvalLaunchHeight=data.shkvalLaunchHeight;
            ShkvalProjectileRadius=data.shkvalProjectileRadius;
            ShkvalProjectileLength=data.shkvalProjectileLength;
            ShkvalBuildingCollisionHeight=data.shkvalBuildingCollisionHeight;
            ShkvalMaxLeadTimeSec=data.shkvalMaxLeadTimeSec;
            ShkvalFriendlyFirePenalty=data.shkvalFriendlyFirePenalty;
            ShkvalBaseRange=data.shkvalBaseRange;
            ShkvalExhaustStartupSec=data.shkvalExhaustStartupSec;
            ShkvalExhaustRadius=data.shkvalExhaustRadius;
            ShkvalExhaustLength=data.shkvalExhaustLength;
            ShkvalExhaustStartBoost=data.shkvalExhaustStartBoost;
            ShkvalExhaustOpacity=data.shkvalExhaustOpacity;
            WeaponMuzzleCoreRatio=data.weaponMuzzleCoreRatio;
            ImpactEffectSec=data.impactEffectSec;
            BallisticWallHeight=data.ballisticWallHeight;
            ExplorerCollisionRadius=data.explorerCollisionRadius;
            ExplorerModelRadius=data.explorerModelRadius;
            ExplorerModelScale=data.explorerModelScale;
            ExplorerSpeed=data.explorerSpeed;
            ExplorerTurnSpeed=data.explorerTurnSpeed;
            ExplorerTurretTurnSpeed=data.explorerTurretTurnSpeed;
            ExplorerAimToleranceRad=data.explorerAimToleranceRad;
            ExplorerRange=data.explorerRange;
            ExplorerVision=data.explorerVision;
            ExplorerHealth=data.explorerHealth;
            ExplorerStopForMs=data.explorerStopForMs;
            ExplorerPopulationCost=data.explorerPopulationCost;
            ExplorerCreditCost=data.explorerCreditCost;
            ExplorerProductionMenuOrder=data.explorerProductionMenuOrder;
            ExplorerProductionDurationSec=data.explorerProductionDurationSec;
            ExplorerDamage=data.explorerDamage;
            ExplorerBurstSize=data.explorerBurstSize;
            ExplorerBurstShotIntervalMs=data.explorerBurstShotIntervalMs;
            ExplorerBurstPauseMs=data.explorerBurstPauseMs;
            ExplorerProjectileSpeed=data.explorerProjectileSpeed;
            ExplorerSpreadDeg=data.explorerSpreadDeg;
            ExplorerTracerLength=data.explorerTracerLength;
            ExplorerTracerThickness=data.explorerTracerThickness;
            ExplorerMuzzleOffset=data.explorerMuzzleOffset;
            ScienceHealth=data.scienceHealth;
            ScienceBuildSeconds=data.scienceBuildSeconds;
            ScienceVisionRange=data.scienceVisionRange;
            ScienceFootprintRadius=data.scienceFootprintRadius;
            ScienceCreditCost=data.scienceCreditCost;
            ScienceModelHeightMeters=data.scienceModelHeightMeters;
            ScienceModelScale=data.scienceModelScale;
            RefineryUpgradeCost=data.refineryUpgradeCost;
            RefineryUpgradeSeconds=data.refineryUpgradeSeconds;
            RefineryUpgradedIncome=data.refineryUpgradedIncome;
            RefineryTurbineSpeed=data.refineryTurbineSpeed;
            TankChassisGlowIntensity=data.tankChassisGlowIntensity;TankChassisCost=data.tankChassisCost;TankChassisSeconds=data.tankChassisSeconds;TankChassisSpeed=data.tankChassisSpeed;
            ExplorerAssaultCost=data.explorerAssaultCost;ExplorerAssaultSeconds=data.explorerAssaultSeconds;ExplorerAssaultBurstSize=(int)data.explorerAssaultBurstSize;ExplorerAssaultSpreadDeg=data.explorerAssaultSpreadDeg;
            ShkvalGuidanceCost=data.shkvalGuidanceCost;ShkvalGuidanceSeconds=data.shkvalGuidanceSeconds;ShkvalGuidanceRange=data.shkvalGuidanceRange;
            BuildingSaleRefundRatio=data.buildingSaleRefundRatio;
            BuildingSaleCombatLockoutSec=data.buildingSaleCombatLockoutSec;
            BuildingSaleDemolitionSec=data.buildingSaleDemolitionSec;
            BuildingRepairDurationSec=data.buildingRepairDurationSec;
            BuildingRepairCostRatio=data.buildingRepairCostRatio;
            BuildingRepairCombatLockoutSec=data.buildingRepairCombatLockoutSec;
            SaleMarkerScale=data.saleMarkerScale;
            LifecycleMarkerPixels=data.lifecycleMarkerPixels;
            LifecycleMarkerPulseHz=data.lifecycleMarkerPulseHz;
            LifecycleMarkerMinOpacity=data.lifecycleMarkerMinOpacity;
            LifecycleMarkerOffsetMeters=data.lifecycleMarkerOffsetMeters;
            FogTintR=data.fogTintR;
            FogTintG=data.fogTintG;
            FogTintB=data.fogTintB;
            FogMemoryBrightness=data.fogMemoryBrightness;
            MinimapCompactSize=data.minimapCompactSize;
            MinimapTacticalSize=data.minimapTacticalSize;
            MinimapTerrainSaturation=data.minimapTerrainSaturation;
            MinimapTerrainBrightness=data.minimapTerrainBrightness;
            MinimapTerrainTierContrast=data.minimapTerrainTierContrast;
            MinimapMarkerSize=data.minimapMarkerSize;
            MinimapMarkerStroke=data.minimapMarkerStroke;
            MinimapTerrainStroke=data.minimapTerrainStroke;
            MinimapCameraStroke=data.minimapCameraStroke;

            FogMemoryDesaturation=data.fogMemoryDesaturation;

            HeadquartersVisionRange=data.headquartersVisionRange;
            OutpostVisionRange=data.outpostVisionRange;
            FactoryVisionRange=data.factoryVisionRange;
            RefineryVisionRange=data.refineryVisionRange;
            MineVisionRange=data.mineVisionRange;
            ConstructionVisionMultiplier=data.constructionVisionMultiplier;
            VisionCellSize=data.visionCellSize;
            FogRevealMs=data.fogRevealMs;
            FogConcealMs=data.fogConcealMs;
            FogEdgeFeather=data.fogEdgeFeather;
            FogExploredOpacity=data.fogExploredOpacity;
            FogUnseenOpacity=data.fogUnseenOpacity;
            MinimapLandmarkSize=data.minimapLandmarkSize;
            MinimapLandmarkOutline=data.minimapLandmarkOutline;
            MinimapUnitMarkerSize=data.minimapUnitMarkerSize;

            OutpostHealth=data.outpostHealth;OutpostBuildSeconds=data.outpostBuildSeconds;OutpostCreditCost=data.outpostCreditCost;OutpostIncomePerPeriod=data.outpostIncomePerPeriod;OutpostFootprintRadius=data.outpostFootprintRadius;OutpostModelHeightMeters=data.outpostModelHeightMeters;OutpostModelScale=data.outpostModelScale;MineHealth=data.mineHealth;MineBuildSeconds=data.mineBuildSeconds;MineCreditCost=data.mineCreditCost;MineIncomePerPeriod=data.mineIncomePerPeriod;MineFootprintRadius=data.mineFootprintRadius;MineModelHeightMeters=data.mineModelHeightMeters;MineModelScale=data.mineModelScale;HeadquartersCaptureRadius=data.headquartersCaptureRadius;HeadquartersCaptureSeconds=data.headquartersCaptureSeconds;OutpostCaptureRadius=data.outpostCaptureRadius;OutpostCaptureSeconds=data.outpostCaptureSeconds;MineCaptureRadius=data.mineCaptureRadius;MineCaptureSeconds=data.mineCaptureSeconds;HeadquartersSlots=data.headquartersSlots;OutpostSlots=data.outpostSlots;SlotRingRadius=data.slotRingRadius;OrdinaryPadRadius=data.ordinaryPadRadius;PadBorderRatio=data.padBorderRatio;CapturePulseHz=data.capturePulseHz;ConstructionDamageMultiplier=data.constructionDamageMultiplier;EvacuationClearance=data.evacuationClearance;EvacuationRetrySeconds=data.evacuationRetrySeconds;OutpostX=data.outpostX;OutpostZ=data.outpostZ;MineX=data.mineX;MineZ=data.mineZ;
            DefenderOffsetX=data.defenderOffsetX;DefenderOffsetZ=data.defenderOffsetZ;
            SchemaVersion = data.schemaVersion; ProfileId = data.profileId; Revision = data.revision;
            SourceCommit = data.sourceCommit; SourceProfileId = data.sourceProfileId; SourceProfileRevision = data.sourceProfileRevision; SourceManifestSha256 = data.sourceManifestSha256;
            UnitOrderQueueLimit=(int)data.unitOrderQueueLimit;GroupMarchMaximumStretch=data.groupMarchMaximumStretch;FollowDistance=data.followDistance;FollowArrivalTolerance=data.followArrivalTolerance;IdleAutoDefenseMultiplier=data.idleAutoDefenseMultiplier;TankCollisionRadius = data.tankCollisionRadius; TankModelRadius = data.tankModelRadius; TankModelScale = data.tankModelScale; TankSpeed = data.tankSpeed; TankTurnSpeed = data.tankTurnSpeed; TankTurretTurnSpeed = data.tankTurretTurnSpeed; TankAimToleranceRadians = data.tankAimToleranceRadians; TankRange = data.tankRange; TankVisionRange = data.tankVisionRange;
            TankHealth = data.tankHealth; TankFiresWhileMoving = data.tankFiresWhileMoving; TankStopForMilliseconds = data.tankStopForMilliseconds; TankPopulationCost = data.tankPopulationCost; TankCreditCost = data.tankCreditCost; TankProductionMenuOrder = data.tankProductionMenuOrder; TankProductionSeconds = data.tankProductionSeconds; TankWeaponDamage = data.tankWeaponDamage; TankWeaponReloadMilliseconds = data.tankWeaponReloadMilliseconds; TankProjectileType = data.tankProjectileType; TankProjectileSpeed = data.tankProjectileSpeed; TankProjectileCollisionRadius = data.tankProjectileCollisionRadius; ProjectileExtraRangePercent = data.projectileExtraRangePercent;
            MatchScoreEarnedCreditsDivisor=data.matchScoreEarnedCreditsDivisor; ArmyCapacity = data.armyCapacity; StartingCredits = data.startingCredits; IncomePeriodSeconds = data.incomePeriodSeconds; BuildingCancellationRefundRatio = data.buildingCancellationRefundRatio; UnitCancellationRefundRatio = data.unitCancellationRefundRatio;
            HeadquartersHealth = data.headquartersHealth; HeadquartersBuildSeconds = data.headquartersBuildSeconds; HeadquartersCreditCost = data.headquartersCreditCost; HeadquartersIncomePerPeriod = data.headquartersIncomePerPeriod; HeadquartersFootprintRadius = data.headquartersFootprintRadius; HeadquartersModelHeightMeters = data.headquartersModelHeightMeters; HeadquartersModelScale = data.headquartersModelScale;
            FactoryHealth = data.factoryHealth; FactoryBuildSeconds = data.factoryBuildSeconds; FactoryCreditCost = data.factoryCreditCost; FactoryFootprintRadius = data.factoryFootprintRadius; FactoryModelHeightMeters = data.factoryModelHeightMeters; FactoryModelScale = data.factoryModelScale;
            RefineryHealth = data.refineryHealth; RefineryBuildSeconds = data.refineryBuildSeconds; RefineryCreditCost = data.refineryCreditCost; RefineryIncomePerPeriod = data.refineryIncomePerPeriod; RefineryFootprintRadius = data.refineryFootprintRadius; RefineryModelHeightMeters = data.refineryModelHeightMeters; RefineryModelScale = data.refineryModelScale;
            ArenaHalfExtent = data.arenaHalfExtent; PlayerHeadquartersX = data.playerHeadquartersX; EnemyHeadquartersX = data.enemyHeadquartersX; HeadquartersZ = data.headquartersZ;
            PlayerFactoryPadX = data.playerFactoryPadX; PlayerFactoryPadZ = data.playerFactoryPadZ; PlayerRefineryPadX = data.playerRefineryPadX; PlayerRefineryPadZ = data.playerRefineryPadZ;
            EnemyFactoryPadX = data.enemyFactoryPadX; EnemyFactoryPadZ = data.enemyFactoryPadZ; EnemyRefineryPadX = data.enemyRefineryPadX; EnemyRefineryPadZ = data.enemyRefineryPadZ;
            CentralObstacleHalfWidth = data.centralObstacleHalfWidth; CentralObstacleHalfDepth = data.centralObstacleHalfDepth; PassageHalfWidth = data.passageHalfWidth;
            EnemyAdvanceDelaySeconds = data.enemyAdvanceDelaySeconds; AttackApproachRangeRatio = data.attackApproachRangeRatio; AttackRepathSeconds = data.attackRepathSeconds; FactoryExitDistance = data.factoryExitDistance; DefaultRallyDistance = data.defaultRallyDistance; TargetPickRadiusMultiplier = data.targetPickRadiusMultiplier; BuildingPickRadius = data.buildingPickRadius; NavigationRequestsPerFrame = data.navigationRequestsPerFrame; RenderTargetFramesPerSecond = data.renderTargetFramesPerSecond;
            CameraOrthoSize = data.cameraOrthoSize; CameraHeight = data.cameraHeight; CameraOffsetX = data.cameraOffsetX; CameraOffsetZ = data.cameraOffsetZ; CameraPanSpeed = data.cameraPanSpeed; CameraEdgePanSpeed=data.cameraEdgePanSpeed; CameraEdgePanZonePixels=data.cameraEdgePanZonePixels; CameraMinZoom = data.cameraMinZoom; CameraMaxZoom = data.cameraMaxZoom; CameraZoomSpeed = data.cameraZoomSpeed; SelectionDragPixels = data.selectionDragPixels;
            Navigation = new NavigationProfile(ProfileId, Revision, TankCollisionRadius, TankSpeed, TankTurnSpeed, .8d, .15d, 3.6d, .08d);
        }

        public double OutpostHealth {get; private set;}
        public double OutpostBuildSeconds {get; private set;}
        public double OutpostCreditCost {get; private set;}
        public double OutpostIncomePerPeriod {get; private set;}
        public double OutpostFootprintRadius {get; private set;}
        public double OutpostModelHeightMeters {get; private set;}
        public double OutpostModelScale {get; private set;}
        public double MineHealth {get; private set;}
        public double MineBuildSeconds {get; private set;}
        public double MineCreditCost {get; private set;}
        public double MineIncomePerPeriod {get; private set;}
        public double MineFootprintRadius {get; private set;}
        public double MineModelHeightMeters {get; private set;}
        public double MineModelScale {get; private set;}
        public double HeadquartersCaptureRadius {get; private set;}
        public double HeadquartersCaptureSeconds {get; private set;}
        public double OutpostCaptureRadius {get; private set;}
        public double OutpostCaptureSeconds {get; private set;}
        public double MineCaptureRadius {get; private set;}
        public double MineCaptureSeconds {get; private set;}
        public double HeadquartersSlots {get; private set;}
        public double OutpostSlots {get; private set;}
        public double SlotRingRadius {get; private set;}
        public double OrdinaryPadRadius {get; private set;}
        public double PadBorderRatio {get; private set;}
        public double CapturePulseHz {get; private set;}
        public double ConstructionDamageMultiplier {get; private set;}
        public double EvacuationClearance {get; private set;}
        public double EvacuationRetrySeconds {get; private set;}
        public double OutpostX {get; private set;}
        public double OutpostZ {get; private set;}
        public double MineX {get; private set;}
        public double MineZ {get; private set;}
        public double DefenderOffsetX{get;private set;} public double DefenderOffsetZ{get;private set;}
        public int SchemaVersion { get; private set; } public string ProfileId { get; private set; } public int Revision { get; private set; }
        public string SourceCommit { get; private set; } public string SourceProfileId { get; private set; } public int SourceProfileRevision { get; private set; } public string SourceManifestSha256 { get; private set; }
        public double TankCollisionRadius { get; private set; } public double TankModelRadius { get; private set; } public double TankModelScale { get; private set; } public double TankSpeed { get; private set; } public double TankTurnSpeed { get; private set; } public double TankTurretTurnSpeed { get; private set; } public double TankAimToleranceRadians { get; private set; } public double TankRange { get; private set; } public double TankVisionRange { get; private set; }
        public int TankHealth { get; private set; } public bool TankFiresWhileMoving { get; private set; } public int TankStopForMilliseconds { get; private set; } public int TankPopulationCost { get; private set; } public int TankCreditCost { get; private set; } public int TankProductionMenuOrder { get; private set; } public int TankProductionSeconds { get; private set; } public int TankWeaponDamage { get; private set; } public int TankWeaponReloadMilliseconds { get; private set; } public string TankProjectileType { get; private set; } public double TankProjectileSpeed { get; private set; } public double TankProjectileCollisionRadius { get; private set; } public double ProjectileExtraRangePercent { get; private set; }
        public int UnitOrderQueueLimit{get;private set;}
        public double GroupMarchMaximumStretch{get;private set;}
        public double FollowDistance{get;private set;} public double FollowArrivalTolerance{get;private set;}
        public double IdleAutoDefenseMultiplier{get;private set;}
        public double TankPreparationSeconds { get { return TankStopForMilliseconds / 1000d; } } public double TankWeaponReloadSeconds { get { return TankWeaponReloadMilliseconds / 1000d; } } public double TankProjectileMaxTravel { get { return TankRange * (1d + ProjectileExtraRangePercent / 100d); } }
        public int MatchScoreEarnedCreditsDivisor {get;private set;}
        public int ArmyCapacity { get; private set; } public int StartingCredits { get; private set; } public int IncomePeriodSeconds { get; private set; } public double BuildingCancellationRefundRatio { get; private set; } public double UnitCancellationRefundRatio { get; private set; }
        public int HeadquartersHealth { get; private set; } public int HeadquartersBuildSeconds { get; private set; } public int HeadquartersCreditCost { get; private set; } public int HeadquartersIncomePerPeriod { get; private set; } public double HeadquartersFootprintRadius { get; private set; } public double HeadquartersModelHeightMeters { get; private set; } public double HeadquartersModelScale { get; private set; }
        public int FactoryHealth { get; private set; } public int FactoryBuildSeconds { get; private set; } public int FactoryCreditCost { get; private set; } public double FactoryFootprintRadius { get; private set; } public double FactoryModelHeightMeters { get; private set; } public double FactoryModelScale { get; private set; }
        public int RefineryHealth { get; private set; } public int RefineryBuildSeconds { get; private set; } public int RefineryCreditCost { get; private set; } public int RefineryIncomePerPeriod { get; private set; } public double RefineryFootprintRadius { get; private set; } public double RefineryModelHeightMeters { get; private set; } public double RefineryModelScale { get; private set; }
        public double HeadquartersIncomePerSecond { get { return HeadquartersIncomePerPeriod / (double)IncomePeriodSeconds; } } public double RefineryIncomePerSecond { get { return RefineryIncomePerPeriod / (double)IncomePeriodSeconds; } }
        public double ArenaHalfExtent { get; private set; } public double GroundHalfExtent { get { return ArenaHalfExtent; } } public double PlayerHeadquartersX { get; private set; } public double EnemyHeadquartersX { get; private set; } public double HeadquartersZ { get; private set; }
        public double PlayerFactoryPadX { get; private set; } public double PlayerFactoryPadZ { get; private set; } public double PlayerRefineryPadX { get; private set; } public double PlayerRefineryPadZ { get; private set; }
        public double EnemyFactoryPadX { get; private set; } public double EnemyFactoryPadZ { get; private set; } public double EnemyRefineryPadX { get; private set; } public double EnemyRefineryPadZ { get; private set; }
        public double CentralObstacleHalfWidth { get; private set; } public double CentralObstacleHalfDepth { get; private set; } public double PassageHalfWidth { get; private set; }
        public double EnemyAdvanceDelaySeconds { get; private set; } public double AttackApproachRangeRatio { get; private set; } public double AttackRepathSeconds { get; private set; } public double FactoryExitDistance { get; private set; } public double DefaultRallyDistance { get; private set; } public double TargetPickRadiusMultiplier { get; private set; } public double BuildingPickRadius { get; private set; } public int NavigationRequestsPerFrame { get; private set; } public int RenderTargetFramesPerSecond { get; private set; }
        public double CameraOrthoSize { get; private set; } public double CameraHeight { get; private set; } public double CameraOffsetX { get; private set; } public double CameraOffsetZ { get; private set; } public double CameraEdgePanSpeed { get; private set; } public double CameraEdgePanZonePixels { get; private set; } public double CameraPanSpeed { get; private set; } public double CameraMinZoom { get; private set; } public double CameraMaxZoom { get; private set; } public double CameraZoomSpeed { get; private set; } public double SelectionDragPixels { get; private set; }
        public NavigationProfile Navigation { get; private set; }

        public IPlayableTerrain AuthoredMap { get; private set; }
        public NavPoint Headquarters(PlayableOwner owner)=>AuthoredMap!=null?AuthoredMap.Headquarters(owner):new NavPoint(owner==PlayableOwner.Player?PlayerHeadquartersX:EnemyHeadquartersX,HeadquartersZ);
        public static PlayableProfile Create(PlayableProfileData data,IPlayableTerrain map=null,string displayName=null) { Validate(data); var result=new PlayableProfile(data); result.DisplayName=displayName??"Основной";result.AuthoredMap=map;if(map!=null)result.ArenaHalfExtent=map.HalfExtent; TerritoryRules.ValidateArena(result); return result; }
        public static PlayableProfile ThreeCrossingsDefault=>Create(DefaultData(),ThreeCrossingsMap.Default);
        public static PlayableProfile Default { get { return Create(DefaultData()); } }

        public static void Validate(PlayableProfileData data)
        {
            if (data != null && data.matchScoreEarnedCreditsDivisor<1) throw new ArgumentException("Score divisor must be positive.");
            if (data == null) throw new ArgumentNullException("data");
            if (data.schemaVersion != RequiredSchemaVersion || data.profileId != RequiredProfileId || data.revision < 1) throw new ArgumentException("Unsupported playable profile identity.", "data");
            if (String.IsNullOrWhiteSpace(data.sourceCommit) || String.IsNullOrWhiteSpace(data.sourceProfileId) || data.sourceProfileRevision < 1 || String.IsNullOrWhiteSpace(data.sourceManifestSha256)) throw new ArgumentException("Playable profile provenance is incomplete.", "data");
            if (data.tankProjectileType != "kinetic-shell") throw new ArgumentException("Playable profile requires the release tank projectile type.", "data");
            if(data.shkvalMarkerStartRadius<data.shkvalBlastRadius)throw new ArgumentException("Artillery marker must cover blast radius.");
            foreach (PlayableProfileField field in PlayableProfileMetadata.Fields) { double value = field.Read(data); if (Double.IsNaN(value) || Double.IsInfinity(value) || value < field.Minimum || value > field.Maximum) throw new ArgumentException("Invalid playable profile field: " + field.Path, "data"); }
            foreach(var field in PlayableProfileMetadata.Fields)if(field.Path.StartsWith("system.follow.")){double steps=(field.Read(data)-field.Minimum)/field.Step;if(Math.Abs(steps-Math.Round(steps))>1e-8)throw new ArgumentException("Follow value must align to source metadata step: "+field.Path);}
            foreach(var field in PlayableProfileMetadata.Fields)if(field.Path.StartsWith("groupMarch.")){double steps=(field.Read(data)-field.Minimum)/field.Step;if(Math.Abs(steps-Math.Round(steps))>1e-8)throw new ArgumentException("March value must align to metadata step: "+field.Path);}
            if(data.unitOrderQueueLimit!=Math.Truncate(data.unitOrderQueueLimit))throw new ArgumentException("Queue limit must be an integer.");
            if(data.fogExploredOpacity>=data.fogUnseenOpacity)throw new ArgumentException("Explored terrain must be clearer than unseen terrain.");
            if (data.headquartersSlots != Math.Floor(data.headquartersSlots) || data.outpostSlots != Math.Floor(data.outpostSlots)) throw new ArgumentException("Slot counts must be integers.");
            if (data.playerHeadquartersX >= data.enemyHeadquartersX || data.centralObstacleHalfWidth >= data.arenaHalfExtent || data.centralObstacleHalfDepth >= data.arenaHalfExtent || data.passageHalfWidth >= data.centralObstacleHalfDepth || data.cameraMinZoom > data.cameraMaxZoom) throw new ArgumentException("Playable arena layout or camera range is invalid.", "data");
        }

        private static PlayableProfileData DefaultData()
        {
            return new PlayableProfileData {
                shkvalCollisionRadius=0.72,
                shkvalModelRadius=0.6,
                shkvalModelScale=1.4,
                shkvalSpeed=4,
                shkvalTurnSpeed=4.5,
                shkvalTurretTurnSpeed=6,
                shkvalAimToleranceRad=0.035,
                shkvalRange=16,
                shkvalVision=12,
                shkvalHealth=70,
                shkvalStopForMs=1800,
                shkvalPopulationCost=4,
                shkvalCreditCost=200,
                shkvalProductionMenuOrder=30,
                shkvalProductionDurationSec=20,
                shkvalDamage=10,
                shkvalFireIntervalMs=1800,
                shkvalProjectileSpeed=12,
                shkvalArcHeight=4,
                shkvalBlastRadius=1.4,
                shkvalMarkerStartRadius=1.9,
                shkvalMarkerOpacity=0.22,
                shkvalLeadSpeedThreshold=7,
                shkvalLeadFalloff=1,
                shkvalLaunchHeight=1.15,
                shkvalProjectileRadius=0.09,
                shkvalProjectileLength=0.65,
                shkvalBuildingCollisionHeight=2.5,
                shkvalMaxLeadTimeSec=30,
                shkvalFriendlyFirePenalty=2,
                shkvalBaseRange=13,
                shkvalExhaustStartupSec=0.16,
                shkvalExhaustRadius=0.1,
                shkvalExhaustLength=0.75,
                shkvalExhaustStartBoost=2.2,
                shkvalExhaustOpacity=0.85,
                weaponMuzzleCoreRatio=0.6,
                impactEffectSec=0.16,
                ballisticWallHeight=1.74,
                explorerCollisionRadius=0.58,
                explorerModelRadius=0.5,
                explorerModelScale=1,
                explorerSpeed=7,
                explorerTurnSpeed=7.5,
                explorerTurretTurnSpeed=10,
                explorerAimToleranceRad=0.034906585,
                explorerRange=10,
                explorerVision=15,
                explorerHealth=70,
                explorerStopForMs=0,
                explorerPopulationCost=2,
                explorerCreditCost=100,
                explorerProductionMenuOrder=10,
                explorerProductionDurationSec=15,
                explorerDamage=2,
                explorerBurstSize=4,
                explorerBurstShotIntervalMs=90,
                explorerBurstPauseMs=1100,
                explorerProjectileSpeed=32,
                explorerSpreadDeg=7,
                explorerTracerLength=0.45,
                explorerTracerThickness=0.025,
                explorerMuzzleOffset=0.7,

                minimapCompactSize=180,
                minimapTacticalSize=600,
                minimapTerrainSaturation=0.7,
                minimapTerrainBrightness=1.15,
                minimapTerrainTierContrast=0.12,
                minimapMarkerSize=1.1,
                minimapMarkerStroke=0.35,
                minimapTerrainStroke=0.32,
                minimapCameraStroke=1.5,

                fogTintR=0.035,
                fogTintG=0.05,
                fogTintB=0.06,
                fogMemoryBrightness=0.45,
                fogMemoryDesaturation=0.85,

                headquartersVisionRange=22,
                outpostVisionRange=18,
                factoryVisionRange=14,
                refineryVisionRange=14,
                mineVisionRange=16,
                constructionVisionMultiplier=0.5,
                visionCellSize=4,
                fogRevealMs=180,
                fogConcealMs=450,
                fogEdgeFeather=1.5,
                fogExploredOpacity=0.46,
                fogUnseenOpacity=0.94,
                minimapLandmarkSize=14,
                minimapLandmarkOutline=2,
                minimapUnitMarkerSize=0.9,

                outpostHealth=300,outpostBuildSeconds=20,outpostCreditCost=400,outpostIncomePerPeriod=5,outpostFootprintRadius=1.7,outpostModelHeightMeters=4.6,outpostModelScale=0.8,mineHealth=250,mineBuildSeconds=15,mineCreditCost=200,mineIncomePerPeriod=15,mineFootprintRadius=1.4,mineModelHeightMeters=2.4,mineModelScale=0.7,headquartersCaptureRadius=4.5,headquartersCaptureSeconds=5,outpostCaptureRadius=3.5,outpostCaptureSeconds=5,mineCaptureRadius=3,mineCaptureSeconds=5,headquartersSlots=5,outpostSlots=3,slotRingRadius=6.5,ordinaryPadRadius=1.6,padBorderRatio=0.06,capturePulseHz=2,constructionDamageMultiplier=1.5,evacuationClearance=1,evacuationRetrySeconds=1,outpostX=-16,outpostZ=20,mineX=16,mineZ=-20,
                defenderOffsetX=5,defenderOffsetZ=4, schemaVersion=RequiredSchemaVersion, armyCapacity=100, profileId=RequiredProfileId, revision=1, sourceCommit="ab1d42d5b1e8af104509efb6d204a4c310be00f1", sourceProfileId="local:main:1788708872201:1", sourceProfileRevision=5, sourceManifestSha256="af6c3085e1a487c827ea104b86242c3931b46ac1f6756fa74d6c1cddbdaf09fb",
                followDistance=2, followArrivalTolerance=.25, idleAutoDefenseMultiplier=1.1, tankCollisionRadius=.72d, tankModelRadius=.65d, tankModelScale=1d, tankSpeed=4d, tankTurnSpeed=4.5d, tankTurretTurnSpeed=6d, tankAimToleranceRadians=.034906585d, tankRange=10d, tankVisionRange=13d, tankHealth=100, tankFiresWhileMoving=true, tankStopForMilliseconds=0, tankPopulationCost=3, tankCreditCost=150, tankProductionMenuOrder=20, tankProductionSeconds=15, tankWeaponDamage=8, tankWeaponReloadMilliseconds=900, tankProjectileType="kinetic-shell", tankProjectileSpeed=15d, tankProjectileCollisionRadius=.06d, projectileExtraRangePercent=20d,
                buildingSaleRefundRatio=0.75d,buildingSaleCombatLockoutSec=5d,buildingSaleDemolitionSec=2d,buildingRepairDurationSec=30d,buildingRepairCostRatio=0.5d,buildingRepairCombatLockoutSec=5d,saleMarkerScale=0.7d,
                lifecycleMarkerPixels=30d,lifecycleMarkerPulseHz=1d,lifecycleMarkerMinOpacity=0.4d,lifecycleMarkerOffsetMeters=0.8d,
                scienceHealth=250, scienceBuildSeconds=20, scienceVisionRange=14, scienceFootprintRadius=1.45, scienceCreditCost=300, scienceModelHeightMeters=1.6407, scienceModelScale=0.8, refineryUpgradeCost=200, refineryUpgradeSeconds=20, refineryUpgradedIncome=15, refineryTurbineSpeed=90,tankChassisGlowIntensity=1.2,tankChassisCost=300,tankChassisSeconds=30,tankChassisSpeed=6,explorerAssaultCost=300,explorerAssaultSeconds=30,explorerAssaultBurstSize=6,explorerAssaultSpreadDeg=5,shkvalGuidanceCost=300,shkvalGuidanceSeconds=30,shkvalGuidanceRange=16, startingCredits=500, incomePeriodSeconds=5, buildingCancellationRefundRatio=.75d, unitCancellationRefundRatio=1d, headquartersHealth=400, headquartersBuildSeconds=25, headquartersCreditCost=600, headquartersIncomePerPeriod=20, headquartersFootprintRadius=2.05d, headquartersModelHeightMeters=4.6d, headquartersModelScale=.7d, factoryHealth=250, factoryBuildSeconds=15, factoryCreditCost=300, factoryFootprintRadius=1.45d, factoryModelHeightMeters=2.5d, factoryModelScale=.7d, refineryHealth=200, refineryBuildSeconds=10, refineryCreditCost=200, refineryIncomePerPeriod=10, refineryFootprintRadius=1.35d, refineryModelHeightMeters=2.6d, refineryModelScale=.8d,
                arenaHalfExtent=32d, playerHeadquartersX=-21d, enemyHeadquartersX=21d, headquartersZ=0d, playerFactoryPadX=-15d, playerFactoryPadZ=-9d, playerRefineryPadX=-15d, playerRefineryPadZ=9d, enemyFactoryPadX=15d, enemyFactoryPadZ=9d, enemyRefineryPadX=15d, enemyRefineryPadZ=-9d, centralObstacleHalfWidth=3d, centralObstacleHalfDepth=24d, passageHalfWidth=4d, enemyAdvanceDelaySeconds=180d, attackApproachRangeRatio=.85d, attackRepathSeconds=.5d, factoryExitDistance=3d, defaultRallyDistance=7d, targetPickRadiusMultiplier=1.8d, buildingPickRadius=2.3d, navigationRequestsPerFrame=4, renderTargetFramesPerSecond=60, cameraOrthoSize=9.6d, cameraHeight=26d, cameraOffsetX=0d, cameraOffsetZ=-37d, cameraPanSpeed=12d, cameraMinZoom=4.27d, cameraMaxZoom=12.8d, cameraZoomSpeed=.013d, selectionDragPixels=6d
            };
        }
    }

    public sealed class PlayableProfileField
    {
        private readonly Func<PlayableProfileData, double> read;
        public PlayableProfileField(string path, string group, string label, string description, string unit, double minimum, double maximum, double step, Func<PlayableProfileData, double> read, string fieldName=null) { FieldName=fieldName; Path=path; Group=group; Label=label; Description=description; Unit=unit; Minimum=minimum; Maximum=maximum; Step=step; this.read=read; }
        public string Path { get; private set; } public string Group { get; private set; } public string Label { get; private set; } public string Description { get; private set; } public string Unit { get; private set; } public double Minimum { get; private set; } public double Maximum { get; private set; } public double Step { get; private set; }
        public string FieldName {get;}
        public void Write(PlayableProfileData data,double value) { NativeBalanceFields.Write(this,data,value); }
        public double Read(PlayableProfileData data) { return read(data); }
    }

    public static class PlayableProfileMetadata
    {
        public static readonly IReadOnlyList<PlayableProfileField> Fields = new[] {
            new PlayableProfileField("science.scienceHealth","Science and refinery","Science health","Maximum science building health","HP",1,10000,10,p=>p.scienceHealth,nameof(PlayableProfileData.scienceHealth)),
            new PlayableProfileField("science.scienceBuildSeconds","Science and refinery","Science construction time","Time to complete science construction","s",0.1,300,0.5,p=>p.scienceBuildSeconds,nameof(PlayableProfileData.scienceBuildSeconds)),
            new PlayableProfileField("science.scienceVisionRange","Science and refinery","Science vision","Radius revealing nearby terrain and entities","m",1,100,0.5,p=>p.scienceVisionRange,nameof(PlayableProfileData.scienceVisionRange)),
            new PlayableProfileField("science.scienceFootprintRadius","Science and refinery","Science footprint","Solid construction and navigation radius","m",0.1,10,0.05,p=>p.scienceFootprintRadius,nameof(PlayableProfileData.scienceFootprintRadius)),
            new PlayableProfileField("science.scienceCreditCost","Science and refinery","Science price","Credits paid on accepted science construction","credits",0,5000,10,p=>p.scienceCreditCost,nameof(PlayableProfileData.scienceCreditCost)),
            new PlayableProfileField("science.scienceModelHeightMeters","Science and refinery","Science model height","Visual height before relative scale","m",0.1,20,0.1,p=>p.scienceModelHeightMeters,nameof(PlayableProfileData.scienceModelHeightMeters)),
            new PlayableProfileField("science.scienceModelScale","Science and refinery","Science model scale","Visual scale; does not change collision","ratio",0.25,3,0.05,p=>p.scienceModelScale,nameof(PlayableProfileData.scienceModelScale)),
            new PlayableProfileField("science.refineryUpgradeCost","Science and refinery","Upgrade price","Credits paid once per refinery upgrade","credits",0,5000,10,p=>p.refineryUpgradeCost,nameof(PlayableProfileData.refineryUpgradeCost)),
            new PlayableProfileField("science.refineryUpgradeSeconds","Science and refinery","Upgrade duration","Active simulation time until improved income","s",0.5,300,0.5,p=>p.refineryUpgradeSeconds,nameof(PlayableProfileData.refineryUpgradeSeconds)),
            new PlayableProfileField("science.refineryUpgradedIncome","Science and refinery","Upgraded income","Credits per shared income period after completion","credits",0,500,1,p=>p.refineryUpgradedIncome,nameof(PlayableProfileData.refineryUpgradedIncome)),
            new PlayableProfileField("science.refineryTurbineSpeed","Science and refinery","Turbine rotation","Visible completed refinery turbine speed","degrees/s",1,720,1,p=>p.refineryTurbineSpeed,nameof(PlayableProfileData.refineryTurbineSpeed)),
            new PlayableProfileField("unitUpgrades.tankChassis.glowIntensity","Танк · Улучшение шасси","Улучшение: яркость подсветки","Яркость тонких полос на улучшенных гусеничных обтекателях. Ноль выключает свечение, сохраняя геометрию.","×",0.0,5.0,0.1,p=>p.tankChassisGlowIntensity,nameof(PlayableProfileData.tankChassisGlowIntensity)),
            new PlayableProfileField("research.tankChassisCost","Research / tank chassis","Chassis price","Credits charged when chassis research starts","credits",0,5000,10,p=>p.tankChassisCost,nameof(PlayableProfileData.tankChassisCost)),
            new PlayableProfileField("research.tankChassisSeconds","Research / tank chassis","Chassis duration","Active simulation time for chassis research","s",0.5,300,0.5,p=>p.tankChassisSeconds,nameof(PlayableProfileData.tankChassisSeconds)),
            new PlayableProfileField("research.tankChassisSpeed","Research / tank chassis","Chassis tank speed","Owner tank speed after completed chassis research","m/s",0.1,50,0.1,p=>p.tankChassisSpeed,nameof(PlayableProfileData.tankChassisSpeed)),
            new PlayableProfileField("research.explorerAssaultCost","Research / Explorer","Assault gun price","Credits charged when Explorer assault research starts","credits",0,5000,10,p=>p.explorerAssaultCost,nameof(PlayableProfileData.explorerAssaultCost)),
            new PlayableProfileField("research.explorerAssaultSeconds","Research / Explorer","Assault gun duration","Active simulation time for Explorer assault research","s",0.5,300,0.5,p=>p.explorerAssaultSeconds,nameof(PlayableProfileData.explorerAssaultSeconds)),
            new PlayableProfileField("research.explorerAssaultBurstSize","Research / Explorer","Assault burst size","Tracers in an upgraded Explorer burst","shots",1,20,1,p=>p.explorerAssaultBurstSize,nameof(PlayableProfileData.explorerAssaultBurstSize)),
            new PlayableProfileField("research.explorerAssaultSpreadDeg","Research / Explorer","Assault spread","Maximum upgraded Explorer tracer spread","degrees",0,45,0.5,p=>p.explorerAssaultSpreadDeg,nameof(PlayableProfileData.explorerAssaultSpreadDeg)),
            new PlayableProfileField("research.shkvalGuidanceCost","Research / Shkval","Guidance price","Credits charged when guidance research starts","credits",0,5000,10,p=>p.shkvalGuidanceCost,nameof(PlayableProfileData.shkvalGuidanceCost)),
            new PlayableProfileField("research.shkvalGuidanceSeconds","Research / Shkval","Guidance duration","Active simulation time for Shkval guidance research","s",0.5,300,0.5,p=>p.shkvalGuidanceSeconds,nameof(PlayableProfileData.shkvalGuidanceSeconds)),
            new PlayableProfileField("research.shkvalGuidanceRange","Research / Shkval","Guidance range","Owner Shkval range after completed guidance research","m",0.1,100,0.1,p=>p.shkvalGuidanceRange,nameof(PlayableProfileData.shkvalGuidanceRange)),

            new PlayableProfileField("map.minimapCompactSize","Minimap","Compact map side","Compact map side; controls map readability and layout.","px",120,280,1,p=>p.minimapCompactSize,nameof(PlayableProfileData.minimapCompactSize)),
            new PlayableProfileField("map.minimapTacticalSize","Minimap","Tactical map maximum side","Tactical map maximum side; controls map readability and layout.","px",320,800,1,p=>p.minimapTacticalSize,nameof(PlayableProfileData.minimapTacticalSize)),
            new PlayableProfileField("map.minimapTerrainSaturation","Minimap","Terrain saturation","Terrain saturation; controls map readability and layout.","ratio",0,2,0.01,p=>p.minimapTerrainSaturation,nameof(PlayableProfileData.minimapTerrainSaturation)),
            new PlayableProfileField("map.minimapTerrainBrightness","Minimap","Terrain brightness","Terrain brightness; controls map readability and layout.","ratio",0.1,3,0.01,p=>p.minimapTerrainBrightness,nameof(PlayableProfileData.minimapTerrainBrightness)),
            new PlayableProfileField("map.minimapTerrainTierContrast","Minimap","Obstacle terrain contrast","Obstacle terrain contrast; controls map readability and layout.","ratio",0,1,0.01,p=>p.minimapTerrainTierContrast,nameof(PlayableProfileData.minimapTerrainTierContrast)),
            new PlayableProfileField("map.minimapMarkerSize","Minimap","Building marker scale","Building marker scale; controls map readability and layout.","ratio",0.1,5,0.01,p=>p.minimapMarkerSize,nameof(PlayableProfileData.minimapMarkerSize)),
            new PlayableProfileField("map.minimapMarkerStroke","Minimap","Building marker stroke","Building marker stroke; controls map readability and layout.","px",0.1,4,0.01,p=>p.minimapMarkerStroke,nameof(PlayableProfileData.minimapMarkerStroke)),
            new PlayableProfileField("map.minimapTerrainStroke","Minimap","Terrain outline","Terrain outline; controls map readability and layout.","px",0.1,4,0.01,p=>p.minimapTerrainStroke,nameof(PlayableProfileData.minimapTerrainStroke)),
            new PlayableProfileField("map.minimapCameraStroke","Minimap","Camera footprint stroke","Camera footprint stroke; controls map readability and layout.","px",0.25,6,0.25,p=>p.minimapCameraStroke,nameof(PlayableProfileData.minimapCameraStroke)),

            new PlayableProfileField("vision.fogTintR","Fog presentation","Fog red","Red component of unexplored terrain tint.","ratio",0,1,0.005,p=>p.fogTintR,nameof(PlayableProfileData.fogTintR)),
            new PlayableProfileField("vision.fogTintG","Fog presentation","Fog green","Green component of unexplored terrain tint.","ratio",0,1,0.005,p=>p.fogTintG,nameof(PlayableProfileData.fogTintG)),
            new PlayableProfileField("vision.fogTintB","Fog presentation","Fog blue","Blue component of unexplored terrain tint.","ratio",0,1,0.005,p=>p.fogTintB,nameof(PlayableProfileData.fogTintB)),
            new PlayableProfileField("vision.fogMemoryBrightness","Fog presentation","Memory brightness","Relative brightness of confirmed building memory.","ratio",0.05,1,0.01,p=>p.fogMemoryBrightness,nameof(PlayableProfileData.fogMemoryBrightness)),
            new PlayableProfileField("vision.fogMemoryDesaturation","Fog presentation","Memory desaturation","Desaturation distinguishing static building memories from live actors.","ratio",0,1,0.01,p=>p.fogMemoryDesaturation,nameof(PlayableProfileData.fogMemoryDesaturation)),

            new PlayableProfileField("vision.headquartersVisionRange","Vision and map","HeadquartersVisionRange","Imported buildings.headquarters.vision; controls native team vision or fog/map readability.","m",1,128,0.1,p=>p.headquartersVisionRange,nameof(PlayableProfileData.headquartersVisionRange)),
            new PlayableProfileField("vision.outpostVisionRange","Vision and map","OutpostVisionRange","Imported buildings.outpost.vision; controls native team vision or fog/map readability.","m",1,128,0.1,p=>p.outpostVisionRange,nameof(PlayableProfileData.outpostVisionRange)),
            new PlayableProfileField("vision.factoryVisionRange","Vision and map","FactoryVisionRange","Imported buildings.factory.vision; controls native team vision or fog/map readability.","m",1,128,0.1,p=>p.factoryVisionRange,nameof(PlayableProfileData.factoryVisionRange)),
            new PlayableProfileField("vision.refineryVisionRange","Vision and map","RefineryVisionRange","Imported buildings.refinery.vision; controls native team vision or fog/map readability.","m",1,128,0.1,p=>p.refineryVisionRange,nameof(PlayableProfileData.refineryVisionRange)),
            new PlayableProfileField("vision.mineVisionRange","Vision and map","MineVisionRange","Imported buildings.mine.vision; controls native team vision or fog/map readability.","m",1,128,0.1,p=>p.mineVisionRange,nameof(PlayableProfileData.mineVisionRange)),
            new PlayableProfileField("vision.constructionVisionMultiplier","Vision and map","ConstructionVisionMultiplier","Imported construction.visionMultiplier; controls native team vision or fog/map readability.","ratio",0.01,1,0.01,p=>p.constructionVisionMultiplier,nameof(PlayableProfileData.constructionVisionMultiplier)),
            new PlayableProfileField("vision.visionCellSize","Vision and map","VisionCellSize","Imported map.visionCellSize; controls native team vision or fog/map readability.","m",0.25,32,0.25,p=>p.visionCellSize,nameof(PlayableProfileData.visionCellSize)),
            new PlayableProfileField("vision.fogRevealMs","Vision and map","FogRevealMs","Imported system.fogRevealMs; controls native team vision or fog/map readability.","ms",1,10000,1,p=>p.fogRevealMs,nameof(PlayableProfileData.fogRevealMs)),
            new PlayableProfileField("vision.fogConcealMs","Vision and map","FogConcealMs","Imported system.fogConcealMs; controls native team vision or fog/map readability.","ms",1,10000,1,p=>p.fogConcealMs,nameof(PlayableProfileData.fogConcealMs)),
            new PlayableProfileField("vision.fogEdgeFeather","Vision and map","FogEdgeFeather","Imported system.fogEdgeFeather; controls native team vision or fog/map readability.","m",0.01,16,0.01,p=>p.fogEdgeFeather,nameof(PlayableProfileData.fogEdgeFeather)),
            new PlayableProfileField("vision.fogExploredOpacity","Vision and map","FogExploredOpacity","Imported system.fogExploredOpacity; controls native team vision or fog/map readability.","ratio",0.01,0.99,0.01,p=>p.fogExploredOpacity,nameof(PlayableProfileData.fogExploredOpacity)),
            new PlayableProfileField("vision.fogUnseenOpacity","Vision and map","FogUnseenOpacity","Imported system.fogUnseenOpacity; controls native team vision or fog/map readability.","ratio",0.01,1,0.01,p=>p.fogUnseenOpacity,nameof(PlayableProfileData.fogUnseenOpacity)),
            new PlayableProfileField("vision.minimapLandmarkSize","Vision and map","MinimapLandmarkSize","Imported system.minimap.landmarkSizePx; controls native team vision or fog/map readability.","px",1,64,1,p=>p.minimapLandmarkSize,nameof(PlayableProfileData.minimapLandmarkSize)),
            new PlayableProfileField("vision.minimapLandmarkOutline","Vision and map","MinimapLandmarkOutline","Imported system.minimap.landmarkOutlinePx; controls native team vision or fog/map readability.","px",0.1,8,0.1,p=>p.minimapLandmarkOutline,nameof(PlayableProfileData.minimapLandmarkOutline)),
            new PlayableProfileField("vision.minimapUnitMarkerSize","Vision and map","MinimapUnitMarkerSize","Imported system.minimap.unitMarkerSize; controls native team vision or fog/map readability.","m",0.1,8,0.1,p=>p.minimapUnitMarkerSize,nameof(PlayableProfileData.minimapUnitMarkerSize)),

            new PlayableProfileField("territory.outpostHealth","Territory","Outpost health","outpost Health; imported release rule.","HP",1,10000,1,p=>p.outpostHealth,nameof(PlayableProfileData.outpostHealth)),
            new PlayableProfileField("territory.outpostBuildSeconds","Territory","Outpost build seconds","outpost BuildSeconds; imported release rule.","s",1,300,1,p=>p.outpostBuildSeconds,nameof(PlayableProfileData.outpostBuildSeconds)),
            new PlayableProfileField("territory.outpostCreditCost","Territory","Outpost credit cost","outpost CreditCost; imported release rule.","credits",0,100000,1,p=>p.outpostCreditCost,nameof(PlayableProfileData.outpostCreditCost)),
            new PlayableProfileField("territory.outpostIncomePerPeriod","Territory","Outpost income per period","outpost IncomePerPeriod; imported release rule.","credits",0,100000,1,p=>p.outpostIncomePerPeriod,nameof(PlayableProfileData.outpostIncomePerPeriod)),
            new PlayableProfileField("territory.outpostFootprintRadius","Territory","Outpost footprint radius","outpost FootprintRadius; imported release rule.","m",0.1,20,0.01,p=>p.outpostFootprintRadius,nameof(PlayableProfileData.outpostFootprintRadius)),
            new PlayableProfileField("territory.outpostModelHeightMeters","Territory","Outpost model height meters","outpost ModelHeightMeters; imported release rule.","m",0.1,100,0.1,p=>p.outpostModelHeightMeters,nameof(PlayableProfileData.outpostModelHeightMeters)),
            new PlayableProfileField("territory.outpostModelScale","Territory","Outpost model scale","outpost ModelScale; imported release rule.","ratio",0.1,5,0.01,p=>p.outpostModelScale,nameof(PlayableProfileData.outpostModelScale)),
            new PlayableProfileField("territory.mineHealth","Territory","Mine health","mine Health; imported release rule.","HP",1,10000,1,p=>p.mineHealth,nameof(PlayableProfileData.mineHealth)),
            new PlayableProfileField("territory.mineBuildSeconds","Territory","Mine build seconds","mine BuildSeconds; imported release rule.","s",1,300,1,p=>p.mineBuildSeconds,nameof(PlayableProfileData.mineBuildSeconds)),
            new PlayableProfileField("territory.mineCreditCost","Territory","Mine credit cost","mine CreditCost; imported release rule.","credits",0,100000,1,p=>p.mineCreditCost,nameof(PlayableProfileData.mineCreditCost)),
            new PlayableProfileField("territory.mineIncomePerPeriod","Territory","Mine income per period","mine IncomePerPeriod; imported release rule.","credits",0,100000,1,p=>p.mineIncomePerPeriod,nameof(PlayableProfileData.mineIncomePerPeriod)),
            new PlayableProfileField("territory.mineFootprintRadius","Territory","Mine footprint radius","mine FootprintRadius; imported release rule.","m",0.1,20,0.01,p=>p.mineFootprintRadius,nameof(PlayableProfileData.mineFootprintRadius)),
            new PlayableProfileField("territory.mineModelHeightMeters","Territory","Mine model height meters","mine ModelHeightMeters; imported release rule.","m",0.1,100,0.1,p=>p.mineModelHeightMeters,nameof(PlayableProfileData.mineModelHeightMeters)),
            new PlayableProfileField("territory.mineModelScale","Territory","Mine model scale","mine ModelScale; imported release rule.","ratio",0.1,5,0.01,p=>p.mineModelScale,nameof(PlayableProfileData.mineModelScale)),
            new PlayableProfileField("territory.headquartersCaptureRadius","Capture","Headquarters capture radius","Eligible ground-unit distance from site.","m",0.1,20,0.1,p=>p.headquartersCaptureRadius,nameof(PlayableProfileData.headquartersCaptureRadius)),
            new PlayableProfileField("territory.headquartersCaptureSeconds","Capture","Headquarters capture seconds","Time to capture or remove full progress; unit count does not accelerate.","s",0.1,60,0.1,p=>p.headquartersCaptureSeconds,nameof(PlayableProfileData.headquartersCaptureSeconds)),
            new PlayableProfileField("territory.outpostCaptureRadius","Capture","Outpost capture radius","Eligible ground-unit distance from site.","m",0.1,20,0.1,p=>p.outpostCaptureRadius,nameof(PlayableProfileData.outpostCaptureRadius)),
            new PlayableProfileField("territory.outpostCaptureSeconds","Capture","Outpost capture seconds","Time to capture or remove full progress; unit count does not accelerate.","s",0.1,60,0.1,p=>p.outpostCaptureSeconds,nameof(PlayableProfileData.outpostCaptureSeconds)),
            new PlayableProfileField("territory.mineCaptureRadius","Capture","Mine capture radius","Eligible ground-unit distance from site.","m",0.1,20,0.1,p=>p.mineCaptureRadius,nameof(PlayableProfileData.mineCaptureRadius)),
            new PlayableProfileField("territory.mineCaptureSeconds","Capture","Mine capture seconds","Time to capture or remove full progress; unit count does not accelerate.","s",0.1,60,0.1,p=>p.mineCaptureSeconds,nameof(PlayableProfileData.mineCaptureSeconds)),
            new PlayableProfileField("territory.headquartersSlots","Slots","Headquarters slots","Ordinary slots around ready HQ.","slots",1,8,1,p=>p.headquartersSlots,nameof(PlayableProfileData.headquartersSlots)),
            new PlayableProfileField("territory.outpostSlots","Slots","Outpost slots","Ordinary slots around ready outpost.","slots",1,8,1,p=>p.outpostSlots,nameof(PlayableProfileData.outpostSlots)),
            new PlayableProfileField("territory.slotRingRadius","Slots","Slot ring radius","Center to ordinary slot distance.","m",4,12,0.1,p=>p.slotRingRadius,nameof(PlayableProfileData.slotRingRadius)),
            new PlayableProfileField("territory.ordinaryPadRadius","Presentation","Ordinary pad radius","Square pad half extent; same visible and pick shape.","m",0.5,3,0.1,p=>p.ordinaryPadRadius,nameof(PlayableProfileData.ordinaryPadRadius)),
            new PlayableProfileField("territory.padBorderRatio","Presentation","Pad border ratio","Pad border relative width.","ratio",0.01,0.3,0.01,p=>p.padBorderRatio,nameof(PlayableProfileData.padBorderRatio)),
            new PlayableProfileField("territory.capturePulseHz","Presentation","Capture pulse hz","Contested capture pulse frequency.","Hz",0.1,5,0.1,p=>p.capturePulseHz,nameof(PlayableProfileData.capturePulseHz)),
            new PlayableProfileField("territory.constructionDamageMultiplier","Construction","Construction damage multiplier","Incoming damage multiplier on unfinished foundations.","ratio",0.1,5,0.1,p=>p.constructionDamageMultiplier,nameof(PlayableProfileData.constructionDamageMultiplier)),
            new PlayableProfileField("territory.evacuationClearance","Construction","Evacuation clearance","Exit clearance beyond combined footprints.","m",0.1,4,0.1,p=>p.evacuationClearance,nameof(PlayableProfileData.evacuationClearance)),
            new PlayableProfileField("territory.evacuationRetrySeconds","Construction","Evacuation retry seconds","Bounded retry cadence for blocked exits.","s",0.1,10,0.1,p=>p.evacuationRetrySeconds,nameof(PlayableProfileData.evacuationRetrySeconds)),
            new PlayableProfileField("territory.outpostX","Arena","Outpost x","Neutral outpost X.","m",-28,28,1,p=>p.outpostX,nameof(PlayableProfileData.outpostX)),
            new PlayableProfileField("territory.outpostZ","Arena","Outpost z","Neutral outpost Z.","m",-28,28,1,p=>p.outpostZ,nameof(PlayableProfileData.outpostZ)),
            new PlayableProfileField("territory.mineX","Arena","Mine x","Neutral mine X.","m",-28,28,1,p=>p.mineX,nameof(PlayableProfileData.mineX)),
            new PlayableProfileField("territory.mineZ","Arena","Mine z","Neutral mine Z.","m",-28,28,1,p=>p.mineZ,nameof(PlayableProfileData.mineZ)),

            new PlayableProfileField("arena.defenderOffsetX","Arena","Defender X offset","Distance defenders start ahead of hostile HQ.","m",1,20,.5,p=>p.defenderOffsetX,nameof(PlayableProfileData.defenderOffsetX)),
            new PlayableProfileField("arena.defenderOffsetZ","Arena","Defender Z offset","Defender spacing either side of headquarters row.","m",1,20,.5,p=>p.defenderOffsetZ,nameof(PlayableProfileData.defenderOffsetZ)),
            new PlayableProfileField("artillery.collisionRadius", "Юниты · Шквал", "Радиус коллизии", "Полный опорный footprint юнита для движения, размещения и столкновений.", "м", 0.1, 5, 0.01, p=>p.shkvalCollisionRadius,nameof(PlayableProfileData.shkvalCollisionRadius)),
            new PlayableProfileField("artillery.modelRadius", "Юниты · Шквал", "Радиус модели", "Базовый визуальный радиус модели до применения относительного масштаба.", "м", 0.05, 5, 0.01, p=>p.shkvalModelRadius,nameof(PlayableProfileData.shkvalModelRadius)),
            new PlayableProfileField("artillery.modelScale", "Юниты · Шквал", "Масштаб модели", "Меняет только визуальный размер модели и экранные якоря, не коллизию.", "×", 0.25, 3, 0.05, p=>p.shkvalModelScale,nameof(PlayableProfileData.shkvalModelScale)),
            new PlayableProfileField("artillery.speed", "Юниты · Шквал", "Скорость", "Максимальная линейная скорость движения юнита.", "м/с", 0.1, 50, 0.1, p=>p.shkvalSpeed,nameof(PlayableProfileData.shkvalSpeed)),
            new PlayableProfileField("artillery.turnSpeed", "Юниты · Шквал", "Скорость поворота корпуса", "Максимальная скорость разворота корпуса во время движения.", "рад/с", 0.1, 30, 0.1, p=>p.shkvalTurnSpeed,nameof(PlayableProfileData.shkvalTurnSpeed)),
            new PlayableProfileField("artillery.turretTurnSpeed", "Юниты · Шквал", "Скорость башни", "Максимальная скорость независимого поворота башни к цели.", "рад/с", 0.1, 40, 0.1, p=>p.shkvalTurretTurnSpeed,nameof(PlayableProfileData.shkvalTurretTurnSpeed)),
            new PlayableProfileField("artillery.aimToleranceRad", "Юниты · Шквал", "Допуск наведения", "Максимальная угловая ошибка башни, при которой разрешён выстрел.", "рад", 0.001, 0.5, 0.001, p=>p.shkvalAimToleranceRad,nameof(PlayableProfileData.shkvalAimToleranceRad)),
            new PlayableProfileField("artillery.range", "Юниты · Шквал", "Дальность огня", "Максимальная дистанция начала и продолжения атаки.", "м", 0.5, 50, 0.5, p=>p.shkvalRange,nameof(PlayableProfileData.shkvalRange)),
            new PlayableProfileField("artillery.vision", "Юниты · Шквал", "Обзор", "Радиус мгновенной игровой видимости юнита.", "м", 0.5, 80, 0.5, p=>p.shkvalVision,nameof(PlayableProfileData.shkvalVision)),
            new PlayableProfileField("artillery.health", "Юниты · Шквал", "Здоровье", "Максимальный запас прочности юнита.", "HP", 1, 10000, 1, p=>p.shkvalHealth,nameof(PlayableProfileData.shkvalHealth)),
            new PlayableProfileField("artillery.stopForMs", "Юниты · Шквал", "Остановка перед огнём", "Минимальное время неподвижности перед разрешённым выстрелом, если стрельба на ходу отключена.", "мс", 0, 10000, 10, p=>p.shkvalStopForMs,nameof(PlayableProfileData.shkvalStopForMs)),
            new PlayableProfileField("artillery.populationCost", "Юниты · Шквал", "Стоимость армии", "Сколько единиц общего лимита армии занимает готовый или зарезервированный юнит.", "ед.", 1, 100, 1, p=>p.shkvalPopulationCost,nameof(PlayableProfileData.shkvalPopulationCost)),
            new PlayableProfileField("artillery.creditCost", "Юниты · Шквал", "Цена производства", "Сколько кредитов списывается при добавлении юнита в очередь совместимого здания.", "кредиты", 0, 5000, 10, p=>p.shkvalCreditCost,nameof(PlayableProfileData.shkvalCreditCost)),
            new PlayableProfileField("artillery.productionMenuOrder", "Юниты · Шквал", "Порядок в меню производства", "Определяет позицию юнита в меню производства: меньшее число показывается раньше.", "индекс", 1, 100, 1, p=>p.shkvalProductionMenuOrder,nameof(PlayableProfileData.shkvalProductionMenuOrder)),
            new PlayableProfileField("artillery.productionDurationSec", "Юниты · Шквал", "Время производства", "Сколько времени совместимое здание производит один юнит.", "с", 0.1, 300, 0.5, p=>p.shkvalProductionDurationSec,nameof(PlayableProfileData.shkvalProductionDurationSec)),
            new PlayableProfileField("artillery.damage", "Юниты · Шквал", "Урон снаряда", "Сколько здоровья снимает одно фактическое попадание снаряда.", "HP", 0, 1000, 1, p=>p.shkvalDamage,nameof(PlayableProfileData.shkvalDamage)),
            new PlayableProfileField("artillery.fireIntervalMs", "Ракета и взрыв", "Интервал огня", "Пауза между одиночными ракетами.", "мс", 10, 10000, 10, p=>p.shkvalFireIntervalMs,nameof(PlayableProfileData.shkvalFireIntervalMs)),
            new PlayableProfileField("artillery.projectileSpeed", "Юниты · Шквал", "Скорость снаряда", "Скорость физического снаряда; влияет на упреждение и время до попадания.", "м/с", 0.1, 200, 0.5, p=>p.shkvalProjectileSpeed,nameof(PlayableProfileData.shkvalProjectileSpeed)),
            new PlayableProfileField("artillery.arcHeight", "Ракета и взрыв", "Высота дуги", "Подъём над прямой между запуском и приземлением; меняет время полёта и упреждение.", "м", 0.1, 30, 0.1, p=>p.shkvalArcHeight,nameof(PlayableProfileData.shkvalArcHeight)),
            new PlayableProfileField("artillery.blastRadius", "Ракета и взрыв", "Радиус взрыва", "Область полного урона, включая союзников.", "м", 0.1, 10, 0.1, p=>p.shkvalBlastRadius,nameof(PlayableProfileData.shkvalBlastRadius)),
            new PlayableProfileField("artillery.markerStartRadius", "Ракета и взрыв", "Начальный радиус маркера", "Круг сужается от этого радиуса до радиуса взрыва.", "м", 0.1, 15, 0.1, p=>p.shkvalMarkerStartRadius,nameof(PlayableProfileData.shkvalMarkerStartRadius)),
            new PlayableProfileField("artillery.markerOpacity", "Ракета и взрыв", "Прозрачность заливки", "Видимость полупрозрачной зоны попадания.", "доля", 0.01, 0.8, 0.01, p=>p.shkvalMarkerOpacity,nameof(PlayableProfileData.shkvalMarkerOpacity)),
            new PlayableProfileField("artillery.leadSpeedThreshold", "Ракета и взрыв", "Порог быстрого движения", "Скорость, к которой упреждение существенно ослабевает.", "м/с", 0.1, 50, 0.1, p=>p.shkvalLeadSpeedThreshold,nameof(PlayableProfileData.shkvalLeadSpeedThreshold)),
            new PlayableProfileField("artillery.leadFalloff", "Ракета и взрыв", "Плавность потери упреждения", "Ширина плавного перехода к неполному упреждению около порога.", "м/с", 0.1, 10, 0.1, p=>p.shkvalLeadFalloff,nameof(PlayableProfileData.shkvalLeadFalloff)),
            new PlayableProfileField("artillery.launchHeight", "Ракета и взрыв", "Высота запуска", "Высота ракеты над опорной поверхностью при старте.", "м", 0.1, 5, 0.05, p=>p.shkvalLaunchHeight,nameof(PlayableProfileData.shkvalLaunchHeight)),
            new PlayableProfileField("artillery.projectileRadius", "Ракета и взрыв", "Радиус ракеты", "Физическая и видимая толщина ракеты.", "м", 0.01, 0.5, 0.01, p=>p.shkvalProjectileRadius,nameof(PlayableProfileData.shkvalProjectileRadius)),
            new PlayableProfileField("artillery.projectileLength", "Ракета и взрыв", "Длина ракеты", "Видимая длина одиночного снаряда.", "м", 0.1, 3, 0.05, p=>p.shkvalProjectileLength,nameof(PlayableProfileData.shkvalProjectileLength)),
            new PlayableProfileField("artillery.buildingCollisionHeight", "Ракета и взрыв", "Высота перехвата зданием", "Авторитетная высота объёма здания для ракет и укрытия от взрыва.", "м", 0.1, 10, 0.1, p=>p.shkvalBuildingCollisionHeight,nameof(PlayableProfileData.shkvalBuildingCollisionHeight)),
            new PlayableProfileField("artillery.maxLeadTimeSec", "Ракета и взрыв", "Горизонт упреждения", "Максимальное время прогнозирования; недостижимый перехват заменяется огнём по текущей позиции.", "с", 0.1, 300, 0.1, p=>p.shkvalMaxLeadTimeSec,nameof(PlayableProfileData.shkvalMaxLeadTimeSec)),
            new PlayableProfileField("artillery.friendlyFirePenalty", "Ракета и взрыв", "Штраф за урон своим", "Вес ожидаемого урона союзникам при автоматическом выборе группы; невыгодный выстрел откладывается.", "×", 0, 10, 0.1, p=>p.shkvalFriendlyFirePenalty,nameof(PlayableProfileData.shkvalFriendlyFirePenalty)),
            new PlayableProfileField("artillery.baseRange", "Системы наведения", "Базовая дальность", "Дальность Шквала до исследования; ровно на 3 м меньше улучшенной.", "м", 1, 100, 0.5, p=>p.shkvalBaseRange,nameof(PlayableProfileData.shkvalBaseRange)),
            new PlayableProfileField("artillery.exhaustStartupSec", "Бой · Дульные вспышки", "Шквал · стартовая вспышка", "За это время сильный стартовый огонь переходит в ровный хвост.", "с", 0.033, 1, 0.01, p=>p.shkvalExhaustStartupSec,nameof(PlayableProfileData.shkvalExhaustStartupSec)),
            new PlayableProfileField("artillery.exhaustRadius", "Бой · Дульные вспышки", "Шквал · ширина огня", "Радиус огня у заднего сопла ракеты в полёте.", "м", 0.01, 0.5, 0.01, p=>p.shkvalExhaustRadius,nameof(PlayableProfileData.shkvalExhaustRadius)),
            new PlayableProfileField("artillery.exhaustLength", "Бой · Дульные вспышки", "Шквал · длина хвоста", "Длина постоянного огненного хвоста позади летящей ракеты.", "м", 0.05, 3, 0.05, p=>p.shkvalExhaustLength,nameof(PlayableProfileData.shkvalExhaustLength)),
            new PlayableProfileField("artillery.exhaustStartBoost", "Бой · Дульные вспышки", "Шквал · усиление старта", "Увеличение размера огня при запуске по сравнению с ровным полётом.", "×", 1, 5, 0.1, p=>p.shkvalExhaustStartBoost,nameof(PlayableProfileData.shkvalExhaustStartBoost)),
            new PlayableProfileField("artillery.exhaustOpacity", "Бой · Дульные вспышки", "Шквал · яркость огня", "Непрозрачность светящегося огня у сопла в течение полёта.", "доля", 0.1, 1, 0.05, p=>p.shkvalExhaustOpacity,nameof(PlayableProfileData.shkvalExhaustOpacity)),
            new PlayableProfileField("artillery.weaponMuzzleCoreRatio", "Бой · Дульные вспышки", "Сердцевина вспышки", "Размер жёлто-белой сердцевины внутри оранжевого края.", "доля", 0.1, 0.9, 0.05, p=>p.weaponMuzzleCoreRatio,nameof(PlayableProfileData.weaponMuzzleCoreRatio)),
            new PlayableProfileField("artillery.impactEffectSec", "Бой · Остовы и эффекты", "Эффект попадания", "Продолжительность локального эффекта контакта.", "с", 0.033, 1, 0.01, p=>p.impactEffectSec,nameof(PlayableProfileData.impactEffectSec)),
            new PlayableProfileField("artillery.ballisticWallHeight", "Artillery", "Ballistic wall height", "Ballistic wall height for baseline artillery flight, production or readability; imported release behavior unless marked native geometry.", "m", 0.001, 300, 0.01, p=>p.ballisticWallHeight,nameof(PlayableProfileData.ballisticWallHeight)),
            new PlayableProfileField("explorer.collisionRadius", "Explorer", "collisionRadius", "Explorer collisionRadius: imported main@5 behavior or physical tracer catalog.", "m", 0.001, 100, 0.01, p=>p.explorerCollisionRadius,nameof(PlayableProfileData.explorerCollisionRadius)),
new PlayableProfileField("explorer.modelRadius", "Explorer", "modelRadius", "Explorer modelRadius: imported main@5 behavior or physical tracer catalog.", "m", 0.001, 100, 0.01, p=>p.explorerModelRadius,nameof(PlayableProfileData.explorerModelRadius)),
new PlayableProfileField("explorer.modelScale", "Explorer", "modelScale", "Explorer modelScale: imported main@5 behavior or physical tracer catalog.", "ratio", 0.001, 100, 0.01, p=>p.explorerModelScale,nameof(PlayableProfileData.explorerModelScale)),
new PlayableProfileField("explorer.speed", "Explorer", "speed", "Explorer speed: imported main@5 behavior or physical tracer catalog.", "m/s", 0.001, 100, 0.01, p=>p.explorerSpeed,nameof(PlayableProfileData.explorerSpeed)),
new PlayableProfileField("explorer.turnSpeed", "Explorer", "turnSpeed", "Explorer turnSpeed: imported main@5 behavior or physical tracer catalog.", "rad/s", 0.001, 100, 0.01, p=>p.explorerTurnSpeed,nameof(PlayableProfileData.explorerTurnSpeed)),
new PlayableProfileField("explorer.turretTurnSpeed", "Explorer", "turretTurnSpeed", "Explorer turretTurnSpeed: imported main@5 behavior or physical tracer catalog.", "rad/s", 0.001, 100, 0.01, p=>p.explorerTurretTurnSpeed,nameof(PlayableProfileData.explorerTurretTurnSpeed)),
new PlayableProfileField("explorer.aimToleranceRad", "Explorer", "aimToleranceRad", "Explorer aimToleranceRad: imported main@5 behavior or physical tracer catalog.", "ratio", 0.001, 100, 0.01, p=>p.explorerAimToleranceRad,nameof(PlayableProfileData.explorerAimToleranceRad)),
new PlayableProfileField("explorer.range", "Explorer", "range", "Explorer range: imported main@5 behavior or physical tracer catalog.", "m", 0.001, 100, 0.01, p=>p.explorerRange,nameof(PlayableProfileData.explorerRange)),
new PlayableProfileField("explorer.vision", "Explorer", "vision", "Explorer vision: imported main@5 behavior or physical tracer catalog.", "m", 0.001, 100, 0.01, p=>p.explorerVision,nameof(PlayableProfileData.explorerVision)),
new PlayableProfileField("explorer.health", "Explorer", "health", "Explorer health: imported main@5 behavior or physical tracer catalog.", "count", 1, 100000, 1, p=>p.explorerHealth,nameof(PlayableProfileData.explorerHealth)),
new PlayableProfileField("explorer.stopForMs", "Explorer", "stopForMs", "Explorer stopForMs: imported main@5 behavior or physical tracer catalog.", "ms", 0, 100000, 1, p=>p.explorerStopForMs,nameof(PlayableProfileData.explorerStopForMs)),
new PlayableProfileField("explorer.populationCost", "Explorer", "populationCost", "Explorer populationCost: imported main@5 behavior or physical tracer catalog.", "count", 0, 100000, 1, p=>p.explorerPopulationCost,nameof(PlayableProfileData.explorerPopulationCost)),
new PlayableProfileField("explorer.creditCost", "Explorer", "creditCost", "Explorer creditCost: imported main@5 behavior or physical tracer catalog.", "count", 0, 100000, 1, p=>p.explorerCreditCost,nameof(PlayableProfileData.explorerCreditCost)),
new PlayableProfileField("explorer.productionMenuOrder", "Explorer", "productionMenuOrder", "Explorer productionMenuOrder: imported main@5 behavior or physical tracer catalog.", "count", 0, 100000, 1, p=>p.explorerProductionMenuOrder,nameof(PlayableProfileData.explorerProductionMenuOrder)),
new PlayableProfileField("explorer.productionDurationSec", "Explorer", "productionDurationSec", "Explorer productionDurationSec: imported main@5 behavior or physical tracer catalog.", "s", 1, 100000, 1, p=>p.explorerProductionDurationSec,nameof(PlayableProfileData.explorerProductionDurationSec)),
new PlayableProfileField("explorer.damage", "Explorer", "damage", "Explorer damage: imported main@5 behavior or physical tracer catalog.", "count", 1, 100000, 1, p=>p.explorerDamage,nameof(PlayableProfileData.explorerDamage)),
new PlayableProfileField("explorer.burstSize", "Explorer", "burstSize", "Explorer burstSize: imported main@5 behavior or physical tracer catalog.", "count", 1, 100000, 1, p=>p.explorerBurstSize,nameof(PlayableProfileData.explorerBurstSize)),
new PlayableProfileField("explorer.burstShotIntervalMs", "Explorer", "burstShotIntervalMs", "Explorer burstShotIntervalMs: imported main@5 behavior or physical tracer catalog.", "ms", 1, 100000, 1, p=>p.explorerBurstShotIntervalMs,nameof(PlayableProfileData.explorerBurstShotIntervalMs)),
new PlayableProfileField("explorer.burstPauseMs", "Explorer", "burstPauseMs", "Explorer burstPauseMs: imported main@5 behavior or physical tracer catalog.", "ms", 1, 100000, 1, p=>p.explorerBurstPauseMs,nameof(PlayableProfileData.explorerBurstPauseMs)),
new PlayableProfileField("explorer.projectileSpeed", "Explorer", "projectileSpeed", "Explorer projectileSpeed: imported main@5 behavior or physical tracer catalog.", "m/s", 0.001, 100, 0.01, p=>p.explorerProjectileSpeed,nameof(PlayableProfileData.explorerProjectileSpeed)),
new PlayableProfileField("explorer.spreadDeg", "Explorer", "spreadDeg", "Explorer spreadDeg: imported main@5 behavior or physical tracer catalog.", "deg", 0, 100, 0.01, p=>p.explorerSpreadDeg,nameof(PlayableProfileData.explorerSpreadDeg)),
new PlayableProfileField("explorer.tracerLength", "Explorer", "tracerLength", "Explorer tracerLength: imported main@5 behavior or physical tracer catalog.", "m", 0.001, 100, 0.01, p=>p.explorerTracerLength,nameof(PlayableProfileData.explorerTracerLength)),
new PlayableProfileField("explorer.tracerThickness", "Explorer", "tracerThickness", "Explorer tracerThickness: imported main@5 behavior or physical tracer catalog.", "m", 0.001, 100, 0.01, p=>p.explorerTracerThickness,nameof(PlayableProfileData.explorerTracerThickness)),
new PlayableProfileField("explorer.muzzleOffset", "Explorer", "Muzzle distance", "Distance from Explorer center to the physical tracer spawn point.", "m", 0.001, 100, 0.01, p=>p.explorerMuzzleOffset,nameof(PlayableProfileData.explorerMuzzleOffset)),
            new PlayableProfileField("tank.collisionRadius", "Tank", "Collision radius", "Authoritative release tank collision radius.", "m", .1, 5, .01, p=>p.tankCollisionRadius,nameof(PlayableProfileData.tankCollisionRadius)), new PlayableProfileField("tank.modelRadius", "Tank", "Model radius", "Release tank model radius.", "m", .1, 5, .01, p=>p.tankModelRadius,nameof(PlayableProfileData.tankModelRadius)), new PlayableProfileField("tank.modelScale", "Tank", "Model scale", "Release tank model scale.", "ratio", .1, 5, .01, p=>p.tankModelScale,nameof(PlayableProfileData.tankModelScale)), new PlayableProfileField("tank.speed", "Tank", "Speed", "Release tank movement speed.", "m/s", .1, 50, .1, p=>p.tankSpeed,nameof(PlayableProfileData.tankSpeed)), new PlayableProfileField("tank.turnSpeed", "Tank", "Hull turn", "Release tank hull turn speed.", "rad/s", .1, 30, .1, p=>p.tankTurnSpeed,nameof(PlayableProfileData.tankTurnSpeed)), new PlayableProfileField("tank.turretTurnSpeed", "Tank", "Turret turn", "Release tank turret turn speed.", "rad/s", .1, 30, .1, p=>p.tankTurretTurnSpeed,nameof(PlayableProfileData.tankTurretTurnSpeed)), new PlayableProfileField("tank.aimToleranceRadians", "Combat", "Aim tolerance", "Release tank aim tolerance.", "rad", .001, 1, .001, p=>p.tankAimToleranceRadians,nameof(PlayableProfileData.tankAimToleranceRadians)), new PlayableProfileField("tank.range", "Combat", "Attack range", "Release tank target-acquisition range.", "m", 1, 100, .1, p=>p.tankRange,nameof(PlayableProfileData.tankRange)), new PlayableProfileField("tank.visionRange", "Tank", "Vision", "Release tank vision range.", "m", 1, 100, .1, p=>p.tankVisionRange,nameof(PlayableProfileData.tankVisionRange)), new PlayableProfileField("tank.health", "Tank", "Health", "Release tank health.", "HP", 1, 10000, 1, p=>p.tankHealth,nameof(PlayableProfileData.tankHealth)), new PlayableProfileField("tank.stopForMilliseconds", "Combat", "Preparation", "Release stationary-fire preparation delay.", "ms", 0, 10000, 1, p=>p.tankStopForMilliseconds,nameof(PlayableProfileData.tankStopForMilliseconds)), new PlayableProfileField("tank.populationCost", "Economy", "Population cost", "Release tank population cost.", "population", 0, 100, 1, p=>p.tankPopulationCost,nameof(PlayableProfileData.tankPopulationCost)), new PlayableProfileField("tank.creditCost", "Economy", "Credit cost", "Release tank credit cost.", "credits", 0, 100000, 1, p=>p.tankCreditCost,nameof(PlayableProfileData.tankCreditCost)), new PlayableProfileField("tank.productionMenuOrder", "Economy", "Production order", "Release tank production menu order.", "index", 0, 100, 1, p=>p.tankProductionMenuOrder,nameof(PlayableProfileData.tankProductionMenuOrder)), new PlayableProfileField("tank.productionSeconds", "Economy", "Production time", "Release factory tank production duration.", "s", 1, 300, 1, p=>p.tankProductionSeconds,nameof(PlayableProfileData.tankProductionSeconds)), new PlayableProfileField("tank.weaponDamage", "Combat", "Shell damage", "Release kinetic-shell damage.", "HP", 1, 1000, 1, p=>p.tankWeaponDamage,nameof(PlayableProfileData.tankWeaponDamage)), new PlayableProfileField("tank.weaponReloadMilliseconds", "Combat", "Reload", "Release kinetic-shell reload interval.", "ms", 1, 10000, 1, p=>p.tankWeaponReloadMilliseconds,nameof(PlayableProfileData.tankWeaponReloadMilliseconds)), new PlayableProfileField("tank.projectileSpeed", "Combat", "Shell speed", "Release kinetic-shell speed.", "m/s", .1, 100, .1, p=>p.tankProjectileSpeed,nameof(PlayableProfileData.tankProjectileSpeed)), new PlayableProfileField("tank.projectileCollisionRadius", "Combat", "Shell footprint", "Kinetic-shell collision footprint from its projectile catalog.", "m", .01, 2, .01, p=>p.tankProjectileCollisionRadius,nameof(PlayableProfileData.tankProjectileCollisionRadius)), new PlayableProfileField("combat.projectileExtraRangePercent", "Combat", "Projectile overtravel", "Release extension beyond target-acquisition range.", "%", 0, 100, 1, p=>p.projectileExtraRangePercent,nameof(PlayableProfileData.projectileExtraRangePercent)),
            new PlayableProfileField("economy.buildingSaleRefundRatio", "Здания · Жизненный цикл", "Возврат продажи", "Возврат продажи; значение релизного профиля.", "доля", 0, 1, 0.05, p=>p.buildingSaleRefundRatio,nameof(PlayableProfileData.buildingSaleRefundRatio)),
            new PlayableProfileField("economy.buildingSaleCombatLockoutSec", "Здания · Жизненный цикл", "Запрет продажи после урона", "Запрет продажи после урона; значение релизного профиля.", "с", 0, 30, 0.5, p=>p.buildingSaleCombatLockoutSec,nameof(PlayableProfileData.buildingSaleCombatLockoutSec)),
            new PlayableProfileField("economy.buildingSaleDemolitionSec", "Здания · Жизненный цикл", "Длительность демонтажа", "Длительность демонтажа; значение релизного профиля.", "с", 0.5, 10, 0.5, p=>p.buildingSaleDemolitionSec,nameof(PlayableProfileData.buildingSaleDemolitionSec)),
            new PlayableProfileField("economy.buildingRepairDurationSec", "Здания · Жизненный цикл", "Время полного ремонта", "Время полного ремонта; значение релизного профиля.", "с", 1, 120, 1, p=>p.buildingRepairDurationSec,nameof(PlayableProfileData.buildingRepairDurationSec)),
            new PlayableProfileField("economy.buildingRepairCostRatio", "Здания · Жизненный цикл", "Цена полного ремонта", "Цена полного ремонта; значение релизного профиля.", "доля", 0, 1, 0.05, p=>p.buildingRepairCostRatio,nameof(PlayableProfileData.buildingRepairCostRatio)),
            new PlayableProfileField("economy.buildingRepairCombatLockoutSec", "Здания · Жизненный цикл", "Запрет ремонта после урона", "Запрет ремонта после урона; значение релизного профиля.", "с", 0, 30, 0.5, p=>p.buildingRepairCombatLockoutSec,nameof(PlayableProfileData.buildingRepairCombatLockoutSec)),
            new PlayableProfileField("system.saleMarkerScale", "Здания · Жизненный цикл", "Масштаб знака продажи", "Масштаб знака продажи; значение релизного профиля.", "×", 0.25, 2, 0.05, p=>p.saleMarkerScale,nameof(PlayableProfileData.saleMarkerScale)),
            new PlayableProfileField("presentation.lifecycleMarkerPixels", "Здания · Маркеры", "Размер маркера ремонта", "Размер маркера ремонта; не влияет на симуляцию.", "px", 12, 80, 1, p=>p.lifecycleMarkerPixels,nameof(PlayableProfileData.lifecycleMarkerPixels)),
            new PlayableProfileField("presentation.lifecycleMarkerPulseHz", "Здания · Маркеры", "Пульсация маркера", "Пульсация маркера; не влияет на симуляцию.", "Гц", 0.1, 5, 0.1, p=>p.lifecycleMarkerPulseHz,nameof(PlayableProfileData.lifecycleMarkerPulseHz)),
            new PlayableProfileField("presentation.lifecycleMarkerMinOpacity", "Здания · Маркеры", "Минимальная непрозрачность", "Минимальная непрозрачность; не влияет на симуляцию.", "доля", 0.1, 1, 0.05, p=>p.lifecycleMarkerMinOpacity,nameof(PlayableProfileData.lifecycleMarkerMinOpacity)),
            new PlayableProfileField("presentation.lifecycleMarkerOffsetMeters", "Здания · Маркеры", "Отступ маркера над моделью", "Отступ маркера над моделью; не влияет на симуляцию.", "м", 0, 5, 0.1, p=>p.lifecycleMarkerOffsetMeters,nameof(PlayableProfileData.lifecycleMarkerOffsetMeters)),
            new PlayableProfileField("economy.startingCredits", "Economy", "Starting credits", "Release starting credits.", "credits", 0, 100000, 1, p=>p.startingCredits,nameof(PlayableProfileData.startingCredits)), new PlayableProfileField("economy.incomePeriodSeconds", "Economy", "Income period", "Release interval for income settlements.", "s", 1, 60, 1, p=>p.incomePeriodSeconds,nameof(PlayableProfileData.incomePeriodSeconds)), new PlayableProfileField("economy.buildingCancellationRefundRatio", "Economy", "Building refund", "Release building cancellation refund ratio.", "ratio", 0, 1, .01, p=>p.buildingCancellationRefundRatio,nameof(PlayableProfileData.buildingCancellationRefundRatio)), new PlayableProfileField("economy.unitCancellationRefundRatio", "Economy", "Unit refund", "Release unit cancellation refund ratio.", "ratio", 0, 1, .01, p=>p.unitCancellationRefundRatio,nameof(PlayableProfileData.unitCancellationRefundRatio)), new PlayableProfileField("economy.headquartersHealth", "Economy", "HQ health", "Release headquarters health.", "HP", 1, 10000, 1, p=>p.headquartersHealth,nameof(PlayableProfileData.headquartersHealth)), new PlayableProfileField("economy.headquartersBuildSeconds", "Economy", "HQ build time", "Release headquarters build duration.", "s", 1, 300, 1, p=>p.headquartersBuildSeconds,nameof(PlayableProfileData.headquartersBuildSeconds)), new PlayableProfileField("economy.headquartersCreditCost", "Economy", "HQ cost", "Release headquarters cost; initial HQ is free by scenario rule.", "credits", 0, 100000, 1, p=>p.headquartersCreditCost,nameof(PlayableProfileData.headquartersCreditCost)), new PlayableProfileField("economy.headquartersIncomePerPeriod", "Economy", "HQ income", "Release HQ credits awarded per income period.", "credits", 0, 100000, 1, p=>p.headquartersIncomePerPeriod,nameof(PlayableProfileData.headquartersIncomePerPeriod)), new PlayableProfileField("economy.headquartersFootprintRadius", "Economy", "HQ radius", "Release headquarters footprint radius.", "m", .1, 20, .01, p=>p.headquartersFootprintRadius,nameof(PlayableProfileData.headquartersFootprintRadius)), new PlayableProfileField("economy.factoryHealth", "Economy", "Factory health", "Release factory health.", "HP", 1, 10000, 1, p=>p.factoryHealth,nameof(PlayableProfileData.factoryHealth)), new PlayableProfileField("economy.factoryBuildSeconds", "Economy", "Factory build time", "Release factory build duration.", "s", 1, 300, 1, p=>p.factoryBuildSeconds,nameof(PlayableProfileData.factoryBuildSeconds)), new PlayableProfileField("economy.factoryCreditCost", "Economy", "Factory cost", "Release factory cost.", "credits", 0, 100000, 1, p=>p.factoryCreditCost,nameof(PlayableProfileData.factoryCreditCost)), new PlayableProfileField("economy.factoryFootprintRadius", "Economy", "Factory radius", "Release factory footprint radius.", "m", .1, 20, .01, p=>p.factoryFootprintRadius,nameof(PlayableProfileData.factoryFootprintRadius)), new PlayableProfileField("economy.refineryHealth", "Economy", "Refinery health", "Release refinery health.", "HP", 1, 10000, 1, p=>p.refineryHealth,nameof(PlayableProfileData.refineryHealth)), new PlayableProfileField("economy.refineryBuildSeconds", "Economy", "Refinery build time", "Release refinery build duration.", "s", 1, 300, 1, p=>p.refineryBuildSeconds,nameof(PlayableProfileData.refineryBuildSeconds)), new PlayableProfileField("economy.refineryCreditCost", "Economy", "Refinery cost", "Release refinery cost.", "credits", 0, 100000, 1, p=>p.refineryCreditCost,nameof(PlayableProfileData.refineryCreditCost)), new PlayableProfileField("economy.refineryIncomePerPeriod", "Economy", "Refinery income", "Release refinery credits awarded per income period.", "credits", 0, 100000, 1, p=>p.refineryIncomePerPeriod,nameof(PlayableProfileData.refineryIncomePerPeriod)), new PlayableProfileField("economy.refineryFootprintRadius", "Economy", "Refinery radius", "Release refinery footprint radius.", "m", .1, 20, .01, p=>p.refineryFootprintRadius,nameof(PlayableProfileData.refineryFootprintRadius)),
            new PlayableProfileField("building.headquartersModelHeightMeters", "Buildings", "HQ model height", "Release headquarters model height.", "m", .1, 100, .1, p=>p.headquartersModelHeightMeters,nameof(PlayableProfileData.headquartersModelHeightMeters)), new PlayableProfileField("building.headquartersModelScale", "Buildings", "HQ model scale", "Release headquarters model scale.", "ratio", .1, 5, .01, p=>p.headquartersModelScale,nameof(PlayableProfileData.headquartersModelScale)), new PlayableProfileField("building.factoryModelHeightMeters", "Buildings", "Factory model height", "Release factory model height.", "m", .1, 100, .1, p=>p.factoryModelHeightMeters,nameof(PlayableProfileData.factoryModelHeightMeters)), new PlayableProfileField("building.factoryModelScale", "Buildings", "Factory model scale", "Release factory model scale.", "ratio", .1, 5, .01, p=>p.factoryModelScale,nameof(PlayableProfileData.factoryModelScale)), new PlayableProfileField("building.refineryModelHeightMeters", "Buildings", "Refinery model height", "Release refinery model height.", "m", .1, 100, .1, p=>p.refineryModelHeightMeters,nameof(PlayableProfileData.refineryModelHeightMeters)), new PlayableProfileField("building.refineryModelScale", "Buildings", "Refinery model scale", "Release refinery model scale.", "ratio", .1, 5, .01, p=>p.refineryModelScale,nameof(PlayableProfileData.refineryModelScale)),
            new PlayableProfileField("arena.halfExtent", "Arena", "Half extent", "Fixed playable ground arena half extent.", "m", 16, 128, 1, p=>p.arenaHalfExtent,nameof(PlayableProfileData.arenaHalfExtent)), new PlayableProfileField("arena.playerHeadquartersX", "Arena", "Player HQ X", "Player base on the left side of the arena.", "m", -120, -1, 1, p=>p.playerHeadquartersX,nameof(PlayableProfileData.playerHeadquartersX)), new PlayableProfileField("arena.enemyHeadquartersX", "Arena", "Enemy HQ X", "Enemy base on the right side of the arena.", "m", 1, 120, 1, p=>p.enemyHeadquartersX,nameof(PlayableProfileData.enemyHeadquartersX)), new PlayableProfileField("arena.headquartersZ", "Arena", "HQ Z", "Shared headquarters row.", "m", -120, 120, 1, p=>p.headquartersZ,nameof(PlayableProfileData.headquartersZ)), new PlayableProfileField("arena.playerFactoryPadX", "Arena", "Player factory pad X", "Player factory construction pad.", "m", -120, -1, 1, p=>p.playerFactoryPadX,nameof(PlayableProfileData.playerFactoryPadX)), new PlayableProfileField("arena.playerFactoryPadZ", "Arena", "Player factory pad Z", "Player factory construction pad.", "m", -120, 120, 1, p=>p.playerFactoryPadZ,nameof(PlayableProfileData.playerFactoryPadZ)), new PlayableProfileField("arena.playerRefineryPadX", "Arena", "Player refinery pad X", "Player refinery construction pad.", "m", -120, -1, 1, p=>p.playerRefineryPadX,nameof(PlayableProfileData.playerRefineryPadX)), new PlayableProfileField("arena.playerRefineryPadZ", "Arena", "Player refinery pad Z", "Player refinery construction pad.", "m", -120, 120, 1, p=>p.playerRefineryPadZ,nameof(PlayableProfileData.playerRefineryPadZ)), new PlayableProfileField("arena.enemyFactoryPadX", "Arena", "Enemy factory pad X", "Enemy factory construction pad.", "m", 1, 120, 1, p=>p.enemyFactoryPadX,nameof(PlayableProfileData.enemyFactoryPadX)), new PlayableProfileField("arena.enemyFactoryPadZ", "Arena", "Enemy factory pad Z", "Enemy factory construction pad.", "m", -120, 120, 1, p=>p.enemyFactoryPadZ,nameof(PlayableProfileData.enemyFactoryPadZ)), new PlayableProfileField("arena.enemyRefineryPadX", "Arena", "Enemy refinery pad X", "Enemy refinery construction pad.", "m", 1, 120, 1, p=>p.enemyRefineryPadX,nameof(PlayableProfileData.enemyRefineryPadX)), new PlayableProfileField("arena.enemyRefineryPadZ", "Arena", "Enemy refinery pad Z", "Enemy refinery construction pad.", "m", -120, 120, 1, p=>p.enemyRefineryPadZ,nameof(PlayableProfileData.enemyRefineryPadZ)), new PlayableProfileField("arena.centralObstacleHalfWidth", "Arena", "Obstacle half width", "Central obstacle leaves top and bottom passages.", "m", .5, 20, .5, p=>p.centralObstacleHalfWidth,nameof(PlayableProfileData.centralObstacleHalfWidth)), new PlayableProfileField("arena.centralObstacleHalfDepth", "Arena", "Obstacle half depth", "Central obstacle half depth.", "m", .5, 28, .5, p=>p.centralObstacleHalfDepth,nameof(PlayableProfileData.centralObstacleHalfDepth)),
            new PlayableProfileField("arena.passageHalfWidth", "Arena", "Passage half width", "Half width of the central passage.", "m", .5, 16, .5, p=>p.passageHalfWidth,nameof(PlayableProfileData.passageHalfWidth)),
            new PlayableProfileField("system.matchScoreEarnedCreditsDivisor", "System", "Score credit divisor", "Earned credits contribution to match score.", "credits", 1, 1000, 1, p=>p.matchScoreEarnedCreditsDivisor,nameof(PlayableProfileData.matchScoreEarnedCreditsDivisor)),
            new PlayableProfileField("army.capacity", "Army", "Army capacity", "Living and active reserved population limit; paid waiting orders do not reserve capacity.", "population", 1, 1000, 1, p=>p.armyCapacity,nameof(PlayableProfileData.armyCapacity)),
            new PlayableProfileField("system.follow.distance", "Follow", "Following distance", "Source distance behind the leader", "m", 1, 10, .1, p=>p.followDistance,nameof(PlayableProfileData.followDistance)),
            new PlayableProfileField("system.follow.arrivalTolerance", "Follow", "Following arrival tolerance", "Source stationary follow tolerance", "m", .05, 1, .05, p=>p.followArrivalTolerance,nameof(PlayableProfileData.followArrivalTolerance)),
            new PlayableProfileField("system.idleAutoDefenseMultiplier", "System", "Idle auto-defense range", "Source range multiplier for idle target acquisition; does not authorize movement or extend weapon range.", "ratio", 1, 3, .05, p=>p.idleAutoDefenseMultiplier,nameof(PlayableProfileData.idleAutoDefenseMultiplier)),
            new PlayableProfileField("groupMarch.maximumStretch", "Group march", "Maximum stretch", "Maximum centerline lead before a moving member yields to regroup.", "m", .5, 40, .1, p=>p.groupMarchMaximumStretch,nameof(PlayableProfileData.groupMarchMaximumStretch)),
            new PlayableProfileField("system.unitOrderQueueLimit", "System", "Deferred unit orders", "Maximum deferred tactical orders per unit; overflow silently preserves the existing FIFO.", "orders", 1, 4096, 1, p=>p.unitOrderQueueLimit,nameof(PlayableProfileData.unitOrderQueueLimit)),
            new PlayableProfileField("behavior.enemyAdvanceDelaySeconds", "Behavior", "Enemy advance delay", "Enemy waits to assemble before its first advance.", "s", 1, 600, 1, p=>p.enemyAdvanceDelaySeconds,nameof(PlayableProfileData.enemyAdvanceDelaySeconds)), new PlayableProfileField("behavior.attackApproachRangeRatio", "Behavior", "Attack approach", "Fraction of attack range retained while approaching a target.", "ratio", .1, 1, .01, p=>p.attackApproachRangeRatio,nameof(PlayableProfileData.attackApproachRangeRatio)), new PlayableProfileField("behavior.attackRepathSeconds", "Behavior", "Attack repath", "Minimum interval between attack path requests.", "s", .05, 10, .05, p=>p.attackRepathSeconds,nameof(PlayableProfileData.attackRepathSeconds)), new PlayableProfileField("production.factoryExitDistance", "Production", "Factory exit", "Distance a new unit clears from its factory.", "m", .5, 20, .5, p=>p.factoryExitDistance,nameof(PlayableProfileData.factoryExitDistance)), new PlayableProfileField("production.defaultRallyDistance", "Production", "Default rally", "Factory default rally distance.", "m", 1, 32, .5, p=>p.defaultRallyDistance,nameof(PlayableProfileData.defaultRallyDistance)), new PlayableProfileField("input.targetPickRadiusMultiplier", "Input", "Target pick multiplier", "Multiplier applied to a unit footprint for pointer target picking.", "ratio", .5, 5, .1, p=>p.targetPickRadiusMultiplier,nameof(PlayableProfileData.targetPickRadiusMultiplier)), new PlayableProfileField("input.buildingPickRadius", "Input", "Building pick radius", "Pointer pick radius for buildings.", "m", .5, 20, .1, p=>p.buildingPickRadius,nameof(PlayableProfileData.buildingPickRadius)), new PlayableProfileField("navigation.requestsPerFrame", "Navigation", "Requests per frame", "Maximum navigation requests admitted per frame.", "requests", 1, 64, 1, p=>p.navigationRequestsPerFrame,nameof(PlayableProfileData.navigationRequestsPerFrame)),
            new PlayableProfileField("presentation.targetFramesPerSecond", "Rendering", "Target frame rate", "Native rendering cadence independent of the 30Hz domain.", "fps", 30, 240, 1, p=>p.renderTargetFramesPerSecond,nameof(PlayableProfileData.renderTargetFramesPerSecond)),
            new PlayableProfileField("camera.orthoSize", "Camera", "View size", "Playable camera orthographic half height.", "m", 5, 100, .01, p=>p.cameraOrthoSize,nameof(PlayableProfileData.cameraOrthoSize)), new PlayableProfileField("camera.height", "Camera", "Height", "Playable camera height above ground.", "m", 5, 200, 1, p=>p.cameraHeight,nameof(PlayableProfileData.cameraHeight)), new PlayableProfileField("camera.offsetX", "Camera", "X offset", "Diagonal camera lateral offset; also determines the direction building fronts face.", "m", -100, 100, 1, p=>p.cameraOffsetX,nameof(PlayableProfileData.cameraOffsetX)), new PlayableProfileField("camera.offsetZ", "Camera", "Z offset", "Playable camera longitudinal offset.", "m", -100, 100, 1, p=>p.cameraOffsetZ,nameof(PlayableProfileData.cameraOffsetZ)), new PlayableProfileField("camera.edgePanSpeed", "Camera", "Edge pan speed", "Pointer edge pan speed in focused gameplay; overridden by the local edge-scroll setting.", "m/s", .1, 100, .1, p=>p.cameraEdgePanSpeed,nameof(PlayableProfileData.cameraEdgePanSpeed)), new PlayableProfileField("camera.edgePanZonePixels", "Camera", "Edge pan zone", "Pointer edge acceleration band inside the active camera viewport.", "px", 8, 120, 1, p=>p.cameraEdgePanZonePixels,nameof(PlayableProfileData.cameraEdgePanZonePixels)), new PlayableProfileField("camera.panSpeed", "Camera", "Pan speed", "Arrow-key camera ground-pan speed; overridden by the local arrow-scroll setting.", "m/s", 1, 50, 1, p=>p.cameraPanSpeed,nameof(PlayableProfileData.cameraPanSpeed)), new PlayableProfileField("camera.minZoom", "Camera", "Minimum zoom", "Closest playable camera zoom.", "m", 4, 100, .01, p=>p.cameraMinZoom,nameof(PlayableProfileData.cameraMinZoom)), new PlayableProfileField("camera.maxZoom", "Camera", "Maximum zoom", "Farthest playable camera zoom.", "m", 5, 100, .01, p=>p.cameraMaxZoom,nameof(PlayableProfileData.cameraMaxZoom)), new PlayableProfileField("camera.zoomSpeed", "Camera", "Zoom speed", "Mouse-wheel zoom multiplier.", "ratio", .001, 1, .001, p=>p.cameraZoomSpeed,nameof(PlayableProfileData.cameraZoomSpeed)), new PlayableProfileField("input.selectionDragPixels", "Input", "Selection drag", "Pointer travel required to start box selection.", "px", 1, 100, 1, p=>p.selectionDragPixels,nameof(PlayableProfileData.selectionDragPixels))
        };
    }

    public static class PlayableMap
    {
        public static NavObstacle[] StaticObstacles(PlayableProfile profile)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if(profile.AuthoredMap!=null)return System.Linq.Enumerable.ToArray(profile.AuthoredMap.MovementBlockers);
            return new[] {
                new NavObstacle(-profile.CentralObstacleHalfWidth, -profile.CentralObstacleHalfDepth, profile.CentralObstacleHalfWidth, -profile.PassageHalfWidth),
                new NavObstacle(-profile.CentralObstacleHalfWidth, profile.PassageHalfWidth, profile.CentralObstacleHalfWidth, profile.CentralObstacleHalfDepth)
            };
        }

        public static NavObstacle[] SolidObstacles(PlayableProfile p)=>p.AuthoredMap==null?StaticObstacles(p):System.Linq.Enumerable.ToArray(p.AuthoredMap.Solids);

        public static NavPoint PlayerFactoryPad(PlayableProfile profile) { return new NavPoint(profile.PlayerFactoryPadX, profile.PlayerFactoryPadZ); }
        public static NavPoint PlayerRefineryPad(PlayableProfile profile) { return new NavPoint(profile.PlayerRefineryPadX, profile.PlayerRefineryPadZ); }
        public static NavPoint EnemyFactoryPad(PlayableProfile profile) { return new NavPoint(profile.EnemyFactoryPadX, profile.EnemyFactoryPadZ); }
        public static NavPoint EnemyRefineryPad(PlayableProfile profile) { return new NavPoint(profile.EnemyRefineryPadX, profile.EnemyRefineryPadZ); }
    }
}
