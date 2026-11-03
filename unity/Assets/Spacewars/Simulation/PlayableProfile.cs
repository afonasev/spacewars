using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    [Serializable]
    public sealed class PlayableProfileData
    {
        // orbital-trap-mine-v1; independent of artillery and weapon research.
        public double orbitalMineCapacity=3,orbitalMineVision=5,orbitalMineDirectDamage=80,orbitalMineSplashDamage=50,orbitalMineBlastRadius=1.4;
        public double orbitalMineModelRadius=.35,orbitalMineModelHeight=.14,orbitalMineEffectSeconds=.55,orbitalMineEffectHeight=.45,orbitalMineRimWidth=.055,orbitalMinePreviewWidth=2;
        public double orbitalMineDustOpacity=.45,orbitalMineFragmentCount=12,orbitalMineFragmentSize=.045;
        public double orbitalScanCoreDegrees=2,orbitalScanTrailOpacity=.32,orbitalScanWaveOpacity=.13,orbitalScanWaveSeconds=.85,orbitalScanFadeSeconds=.2,orbitalScanRimOpacity=.08;
        public double orbitalHackHealthPercent=50,orbitalHackSeconds=30;
        public double orbitalScanRadius=10,orbitalScanSeconds=3,orbitalScanSweepSeconds=1.2,orbitalScanSectorDegrees=60,orbitalScanLineWidth=1.5,orbitalScanOpacity=.65;
        public double orbitalCardWidth=320,orbitalCardHeight=336,orbitalCardArtworkSize=220;
        public double orbitalUiRingWidth=3,orbitalUiCornerRadius=5,orbitalUiPulseSeconds=1.2,orbitalUiCoolingArtworkOpacity=.25;
        public double orbitalPanelWidth=76,orbitalIconSize=36,orbitalLevelFontSize=32;
        public double orbitalThresholdMax=8000,orbitalUiMaxFontScale=.5;
        public double orbitalThreshold1=250,orbitalThreshold2=500,orbitalThreshold3=1000,orbitalThreshold4=2000,orbitalThreshold5=4000;
        public double orbitalSystemFailureRadius=5,orbitalSystemFailureSeconds=20;
        public int orbitalSystemFailureMaxTargets=2;
        public double orbitalBasicSeconds=30,orbitalAdvancedSeconds=60,orbitalUltimateSeconds=90,orbitalCommonSeconds=10,orbitalFinalSpeed=2;

        // Immutable technical backend selection; zero preserves existing profile contracts.
        public int groundRouteBackend;
        // independent-defense-v1: mobile unit tuning never changes these terms.
        public double turretHealth=300;
        public double turretRange=10;
        public double turretDamage=2;
        public int turretBurstSize=6;
        public double turretBurstShotIntervalMs=90;
        public double turretBurstPauseMs=1100;
        public double turretSpreadDeg=5;
        public double turretProjectileSpeed=32;
        public double gaussDamage=50;
        public double gaussRange=11.5;
        // Legacy relative fields remain readable in stored profiles; current controls use absolute terms.
        // gauss-tower-v1
        public double gaussCostMultiplier=2;
        public double gaussHealthMultiplier=1.5;
        public double gaussTankDamageFraction=0.5;
        public double gaussReloadSeconds=4;
        public double gaussPierceRetention=0.7;
        public double gaussRangeBlend=0.5;
        public double gaussBeamRadius=0.04;
        public double gaussBeamSeconds=0.85;
        public double gaussBeamIntensity=3;
        public double gaussChargeSeconds=1.1;
        public double gaussChargeIntensity=5;
        public double gaussRecoilMeters=0.09;
        public double gaussRecoilSeconds=0.42;
        public double gaussRecoilPeakFraction=0.15;
        public double gaussBeamGlowWidth=5;
        public double gaussBeamRiseFraction=0.12;
        // repair-tower-v1; fixed healing, shared defensive economics/footprint.
        public double repairTowerHealthPerSecond=4;
        // sentry-turret-combat-v1
        public double turretCreditCost=200;
        public double turretBuildSeconds=15;
        public double turretHealthMultiplier=3;
        public double turretRangeMultiplier=1;
        public double turretFootprintRadius=0.55;
        public double turretPadOffset=2;
        public double turretPassageWidth=2.2;
        public double turretVision=15;
        public double turretModelScale=1;
        public double turretYawSpeed=360;
        public double turretPitchSpeed=180;
        public double turretDepression=20;
        public double turretTurningSpread=1.3;
        public double turretLaserWidth=0.015;
        public double turretLaserIntensity=1.8;

        // building-collapse-a-v1: authoritative timing/health and restrained visual controls.
        public double constructionInitialHealthRatio=0.25;
        public double buildingDestructionSeconds=2.4;
        public double buildingDestructionShake=0.045;
        public double buildingDestructionTilt=1.2;
        public double buildingDestructionFireScale=0.65;
        public double buildingDestructionSmokeOpacity=0.32;

        public int provingGroundStartingCredits=10000;
        public double provingGroundDividerWidth=.18;
        public double reinforcedConcreteCost=200, reinforcedConcreteSeconds=30, reinforcedConcreteBonusPercent=25;
        public double reinforcedConcreteLevel2Cost=200, reinforcedConcreteLevel2Seconds=30, reinforcedConcreteLevel2BonusPercent=50;
        public double provingGroundActionSeconds=5;
        public double provingGroundAttackRearLaneOffset=15;
        // match-events-v1: real-time readability/history and spatial battle admission.
        public double notificationBottomPixels=8;
        public double notificationSeconds=3,notificationHistorySeconds=5,notificationFrontRadius=16,notificationFrontIdleSeconds=5;
        // native-chat-v3: presentation timing, exposed through normal Balance Lab metadata.
        public double chatOutlineWidth=.2, chatShadowOffset=1, chatShadowBlur=2;
        // native-ai-communications-v1: presentation admission, never AI decision tuning.
        public double aiChatAuthorSeconds=12, aiChatTeamSeconds=5, aiChatCriticalSeconds=8, aiChatMaxAgeSeconds=6, aiChatObserverLimit=5;
        public double chatLeftPercent=20, chatVisibleLimit=5, chatReadableSeconds=4, chatFadeSeconds=2, chatEvictionSeconds=.25;
        // hawk-match-v1: all designer values participate in the normal Balance Lab metadata.
        public double hawkModelScale=.35;
        public double hawkAimTolerance=0.04;
        public double hawkPreparationYawSpeed=0.25;
        public double hawkMissileLength=0.55;
        public double hawkMissileVisualScale=0.8;
        public double hawkDebrisSeconds=0.8;
        public double hawkDebrisSpeed=3;
        public double hawkDebrisGravity=9.81;
        public double hawkDebrisCount=6;
        public double hawkHealth=60;
        public double hawkCreditCost=225;
        public double hawkPopulationCost=3;
        public double hawkProductionSeconds=20;
        public double hawkRadius=1;
        public double hawkSpeed=7.5;
        public double hawkReverseSpeed=2.5;
        public double hawkStrafeSpeed=8;
        public double hawkAcceleration=7;
        public double hawkBraking=12;
        public double hawkShortSeconds=0.8;
        public double hawkShortDistance=4;
        public double hawkYawSpeed=4;
        public double hawkYawAcceleration=10;
        public double hawkCruisePitch=12;
        public double hawkAccelerationPitch=10;
        public double hawkBank=20;
        public double hawkAttitudeSpeed=60;
        public double hawkClearance=3;
        public double hawkClimbSpeed=4;
        public double hawkDescentSpeed=2;
        public double hawkLookahead=12;
        public double hawkSeparation=0.25;
        public double factoryAssemblyGap=0.4;
        public double hawkRoofSeconds=1;
        public double hawkPlatformSeconds=1;
        public double hawkSpinSeconds=1;
        public double hawkRotorSpeed=2160;
        public double hawkRange=10;
        public double hawkPreparationSeconds=3;
        // hawk-salvo-research-v1: independent preparation, intra-salvo delay and reload.
        public double hawkSalvoResearchCost=300, hawkSalvoResearchSeconds=30;
        public double hawkUpgradedPreparationSeconds=1.5, hawkUpgradedSalvoDelay=0.18, hawkUpgradedReloadSeconds=2;
        public double hawkDamage=10;
        public double hawkSalvoDelay=0.5;
        public double hawkReloadSeconds=3;
        public double hawkMissileSpeed=16;
        public double hawkMissileTurn=0.8;
        public double hawkMissileRadius=0.07;
        public int groupCorridorMaximumExcessPercent;
        public int schemaVersion;
        public double crowdMotionHeadingIntervals=NavigationProfile.CrowdV1HeadingIntervals,crowdMotionSpeedBands=NavigationProfile.CrowdV1SpeedBands,crowdMotionLookaheadSeconds=NavigationProfile.CrowdV1LookaheadSeconds,crowdMotionNeighborCap=NavigationProfile.CrowdV1NeighborCap;
        internal void ResetCrowdMotionV1(){crowdMotionHeadingIntervals=NavigationProfile.CrowdV1HeadingIntervals;crowdMotionSpeedBands=NavigationProfile.CrowdV1SpeedBands;crowdMotionLookaheadSeconds=NavigationProfile.CrowdV1LookaheadSeconds;crowdMotionNeighborCap=NavigationProfile.CrowdV1NeighborCap;}

        // tower-range-zone-v1: purely visual defaults for profiles saved before the overlay existed.
        public double towerRangeFillOpacity=.08,towerRangeBorderOpacity=.75,towerRangeBorderWidth=.10,towerRangeEdgeFeather=.10,towerRangeLift=.045;
        public double towerRangeInnerFade=.6,towerRangeRepairHue=.36,towerRangeCombatHue=.12,towerRangeSaturation=.75,towerRangeBrightness=1;
        public double groundTrailLifetimeSeconds=10;
        // tactical-selection-a-v1: presentation only, not selection membership.
        public double selectionFrameRed=.39,selectionFrameGreen=.9,selectionFrameBlue=.65;
        public double selectionFrameLineWidth=1,selectionFrameCornerWidth=2,selectionFrameCornerLength=18;
        public double selectionFrameFillOpacity=.07,selectionFrameLineOpacity=.6;
        public double selectionRingPadding=.10, selectionRingWidth=.055, selectionRingLift=.045;
        public double hawkSelectionRadius=.65;
        public double healthBarWidth=1.4, healthBarHeight=.075, healthBarModelGap=.2, healthBarCellGap=.012;
        public int healthBarCells=10;
        public double healthBarOutlineWidth=.015, buildingSelectionPadding=.35, buildingSelectionWidth=.10;
        // ring-hangar-v1 defaults also initialize profiles saved before this building existed.
        public int hangarHealth = 250;
        public int hangarBuildSeconds = 15;
        public int hangarCreditCost = 300;
        // producer-upgrades-v1: independent of construction after initial defaults.
        public int factoryUpgradeCost=300,hangarUpgradeCost=300;
        public double factoryUpgradeSeconds=15,hangarUpgradeSeconds=15,upgradedProducerSpeedMultiplier=1.5;
        public double hangarVisionRange = 14;
        public double hangarFootprintRadius = 1.45;
        public double hangarModelHeightMeters = 1.25;
        public double hangarModelScale = 1;
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
        // tank-chassis-health-v1
        public double tankChassisHealthBonusPercent=20;
        // combat-stat-rounding-v1
        public double maximumHealthRoundingStep=5, damageRoundingStep=0.1;
        public double weaponSystemsLevel3Cost=400;
        public double weaponSystemsLevel3Seconds=60;
        public double weaponSystemsLevel3BonusPercent=50;
        public double defensiveSystemsLevel3Cost=400;
        public double defensiveSystemsLevel3Seconds=60;
        public double defensiveSystemsLevel3BonusPercent=50;
        public double scientificCenterUpgradeCost=300;
        public double scientificCenterUpgradeSeconds=30;
        public double weaponSystemsLevel1Cost=200;
        public double weaponSystemsLevel1Seconds=30;
        public double weaponSystemsLevel1BonusPercent=15;
        public double weaponSystemsLevel2Cost=300;
        public double weaponSystemsLevel2Seconds=45;
        public double weaponSystemsLevel2BonusPercent=30;
        public double defensiveSystemsLevel1Cost=200;
        public double defensiveSystemsLevel1Seconds=30;
        public double defensiveSystemsLevel1BonusPercent=15;
        public double defensiveSystemsLevel2Cost=300;
        public double defensiveSystemsLevel2Seconds=45;
        public double defensiveSystemsLevel2BonusPercent=30;
        public double armySystemsMinimumUnits=10;
        public double aiArmySystemsRecruitDelaySeconds=420;
        public double aiArmySystemsRecruitLevel2DelaySeconds=600;
        public double aiArmySystemsRecruitUtilityScale=0.15;
        public double aiArmySystemsFighterDelaySeconds=300;
        public double aiArmySystemsFighterLevel2DelaySeconds=480;
        public double aiArmySystemsFighterUtilityScale=1;
        public double aiArmySystemsVeteranDelaySeconds=180;
        public double aiArmySystemsVeteranLevel2DelaySeconds=360;
        public double aiArmySystemsVeteranUtilityScale=3;
        public double aiArmySystemsFighterCatchupSeconds=60;
        public double aiArmySystemsVeteranCatchupSeconds=0;

        public double explorerAssaultCost, explorerAssaultSeconds, explorerAssaultBurstSize, explorerAssaultSpreadDeg;
        public double shkvalGuidanceCost, shkvalGuidanceSeconds, shkvalGuidanceRange;

        public double incomeMarkerFontPixels=16, incomeMarkerRisePixels=24, incomeMarkerDurationSeconds=1;
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
        public double teamSignalLifetimeSeconds=5;
        public double teamSignalMaximumPerAuthor=3;
        public double teamSignalPulseSeconds=1.2;
        public double teamSignalFadeSeconds=0.6;
        public double teamSignalMinimumAlpha=0.3;
        public double teamSignalGlyphPixels=26;
        public double teamSignalMapPixels=16;
        public double teamSignalWheelPixels=240;
        public double teamSignalRepeatRadius=0.5;
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
        public double lingeringVisionSeconds=1;
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
        public double relayIncomePerPeriod=5,relayCaptureRadius=7,relayCaptureSeconds=5,relayFootprintRadius=.75;
        public double relayVisionRange=10; // Independent relay sight; tank sight keeps its own default.
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
        // Missing values in older Lab versions adopt this named profile default.
        public int shkvalDirectHitDamage=20;
        public double shkvalFireIntervalMs;
        public double shkvalProjectileSpeed;
        public double shkvalArcHeight;
        public double shkvalBlastRadius;
        public double shkvalBaseBlastRadiusMultiplier=0.8;
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
        public double directImpactSparkWidth=.035,directImpactGlow=1.5,directImpactSmokeOpacity=.22;
        public double directImpactRadius=0.18, directImpactSeconds=0.16, directImpactSparkLength=0.45, explorerImpactScale=0.4;
        public double unitWreckLifetimeSeconds=30;
        public double wreckSinkSeconds=2;
        public double wreckSpawnProtectionSeconds=1;
        public double wreckBurialMin=0.03;
        public double wreckBurialMax=0.16;
        public double wreckSinkDepth=2;
        public double hawkWreckChancePercent=30;
        public double hawkWreckFallSeconds=0.8;
        public double unitDeathSmokeSeconds=2;
        public double wreckHitSmokeSeconds=0.6;
        public double wreckSmokeSize=0.8;
        public double wreckSmokeOpacity=0.35;
        public double wreckSmokeRise=0.5;

        public double impactEffectRadius=0.22; // Web default-v2; presentation only.
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
        public double defaultCombatPursuitSeconds=2;
        public double followDistance, followArrivalTolerance;
        public double tankReverseDistance=0, tankReverseSpeedRatio=.45, tankReverseConeRadians=.4;
        public double tankCollisionRadius, tankModelRadius, tankModelScale, tankSpeed, tankTurnSpeed, tankTurretTurnSpeed, tankAimToleranceRadians, tankRange, tankVisionRange;
        public int tankHealth, tankStopForMilliseconds, tankPopulationCost, tankCreditCost, tankProductionMenuOrder, tankProductionSeconds, tankWeaponDamage, tankWeaponReloadMilliseconds;
        public bool tankFiresWhileMoving;
        public string tankProjectileType;
        public double tankProjectileSpeed, tankProjectileCollisionRadius, projectileExtraRangePercent;

        public int startingCredits, incomePeriodSeconds;
        public double incomeSettlementSeconds=.1d;
        public int armyCapacity;
        public int matchScoreEarnedCreditsDivisor=2;
        public double buildingCancellationRefundRatio, unitCancellationRefundRatio;
        public int headquartersHealth, headquartersBuildSeconds, headquartersCreditCost, headquartersIncomePerPeriod;
        public double rallyFlagHeight=1.8;
        public double headquartersFootprintRadius, headquartersModelHeightMeters, headquartersModelScale;
        public int factoryHealth, factoryBuildSeconds, factoryCreditCost;
        public double factoryFootprintRadius, factoryModelHeightMeters, factoryModelScale;
        public double factoryGateSeconds=.35, factoryRolloutSpeed=3.0;
        public int refineryHealth, refineryBuildSeconds, refineryCreditCost, refineryIncomePerPeriod;
        public double refineryFootprintRadius, refineryModelHeightMeters, refineryModelScale;

        public double arenaHalfExtent, playerHeadquartersX, enemyHeadquartersX, headquartersZ;
        public double playerFactoryPadX, playerFactoryPadZ, playerRefineryPadX, playerRefineryPadZ;
        public double enemyFactoryPadX, enemyFactoryPadZ, enemyRefineryPadX, enemyRefineryPadZ;
        public double centralObstacleHalfWidth, centralObstacleHalfDepth, passageHalfWidth;
        public double enemyAdvanceDelaySeconds, attackApproachRangeRatio, attackRepathSeconds, factoryExitDistance, defaultRallyDistance, targetPickRadiusMultiplier, buildingPickRadius;
        public double defenderOffsetX, defenderOffsetZ;
        // Zero means the optional extension is absent in legacy profiles. Do not
        // normalize it into old revisions or include it in their wire binding.
        public int navigationMaxAccruedCredits;
        public int navigationRequestsPerFrame, renderTargetFramesPerSecond;
        public double cameraEdgePanSpeed=8, cameraEdgePanZonePixels=28;
        public double cameraOrthoSize, cameraHeight, cameraOffsetX, cameraOffsetZ, cameraPanSpeed, cameraMinZoom, cameraMaxZoom, cameraZoomSpeed, selectionDragPixels;
    }

    public sealed class PlayableProfile
    {
        public bool UsesRecastGroundRoutes=>sourceData.groundRouteBackend==1;
        public double MinimapCompactSize {get; private set;}
        public double MinimapTacticalSize {get; private set;}
        public double MinimapTerrainSaturation {get; private set;}
        public double MinimapTerrainBrightness {get; private set;}
        public double MinimapTerrainTierContrast {get; private set;}
        public double NotificationHistorySeconds=>sourceData.notificationHistorySeconds;
        public double TeamSignalLifetimeSeconds => sourceData.teamSignalLifetimeSeconds;
        public double TeamSignalMaximumPerAuthor => sourceData.teamSignalMaximumPerAuthor;
        public double TeamSignalPulseSeconds => sourceData.teamSignalPulseSeconds;
        public double TeamSignalFadeSeconds => sourceData.teamSignalFadeSeconds;
        public double TeamSignalMinimumAlpha => sourceData.teamSignalMinimumAlpha;
        public double TeamSignalGlyphPixels => sourceData.teamSignalGlyphPixels;
        public double TeamSignalMapPixels => sourceData.teamSignalMapPixels;
        public double TeamSignalWheelPixels => sourceData.teamSignalWheelPixels;
        public double TeamSignalRepeatRadius => sourceData.teamSignalRepeatRadius;
        public double MinimapMarkerSize {get; private set;}
        public double MinimapMarkerStroke {get; private set;}
        public double MinimapTerrainStroke {get; private set;}
        public double MinimapCameraStroke {get; private set;}
        public double HeadquartersVisionRange {get; private set;}
        public double OutpostVisionRange {get; private set;}
        public double FactoryVisionRange {get; private set;}
        public double RefineryVisionRange {get; private set;}
        public double MineVisionRange {get; private set;}
        public double GaussCostMultiplier => sourceData.gaussCostMultiplier;
        public double GaussHealthMultiplier => sourceData.gaussHealthMultiplier;
        public double GaussTankDamageFraction => sourceData.gaussTankDamageFraction;
        public double GaussReloadSeconds => sourceData.gaussReloadSeconds;
        public double GaussPierceRetention => sourceData.gaussPierceRetention;
        public double GaussRangeBlend => sourceData.gaussRangeBlend;
        public double GaussBeamRadius => sourceData.gaussBeamRadius;
        public double GaussBeamSeconds => sourceData.gaussBeamSeconds;
        public double GaussBeamIntensity => sourceData.gaussBeamIntensity;
        public double GaussChargeSeconds => sourceData.gaussChargeSeconds;
        public double GaussChargeIntensity => sourceData.gaussChargeIntensity;
        public double GaussRecoilMeters => sourceData.gaussRecoilMeters;
        public double GaussRecoilSeconds => sourceData.gaussRecoilSeconds;
        public double GaussRecoilPeakFraction => sourceData.gaussRecoilPeakFraction;
        public double GaussBeamGlowWidth => sourceData.gaussBeamGlowWidth;
        public double GaussBeamRiseFraction => sourceData.gaussBeamRiseFraction;
        public double TowerRangeFillOpacity => sourceData.towerRangeFillOpacity;
        public double TowerRangeBorderOpacity => sourceData.towerRangeBorderOpacity;
        public double TowerRangeBorderWidth => sourceData.towerRangeBorderWidth;
        public double TowerRangeEdgeFeather => sourceData.towerRangeEdgeFeather;
        public double TowerRangeLift => sourceData.towerRangeLift;
        public double TowerRangeInnerFade => sourceData.towerRangeInnerFade;
        public double TowerRangeRepairHue => sourceData.towerRangeRepairHue;
        public double TowerRangeCombatHue => sourceData.towerRangeCombatHue;
        public double TowerRangeSaturation => sourceData.towerRangeSaturation;
        public double TowerRangeBrightness => sourceData.towerRangeBrightness;
        public double GaussRange => sourceData.gaussRange;
        public double TurretHealth => sourceData.turretHealth;
        public double TurretRange => sourceData.turretRange;
        public double TurretDamage => sourceData.turretDamage;
        public int TurretBurstSize => sourceData.turretBurstSize;
        public double TurretBurstShotIntervalMs => sourceData.turretBurstShotIntervalMs;
        public double TurretBurstPauseMs => sourceData.turretBurstPauseMs;
        public double TurretSpreadDeg => sourceData.turretSpreadDeg;
        public double TurretProjectileSpeed => sourceData.turretProjectileSpeed;
        public double GaussDamage => sourceData.gaussDamage;
        public double RepairTowerHealthPerSecond {get; private set;}
        public double TurretCreditCost {get; private set;}
        public double TurretBuildSeconds {get; private set;}
        public double TurretHealthMultiplier {get; private set;}
        public double TurretRangeMultiplier {get; private set;}
        public double TurretFootprintRadius {get; private set;}
        public double TurretPadOffset {get; private set;}
        public double TurretPassageWidth {get; private set;}
        public double TurretVision {get; private set;}
        public double TurretModelScale {get; private set;}
        public double TurretYawSpeed {get; private set;}
        public double TurretPitchSpeed {get; private set;}
        public double TurretDepression {get; private set;}
        public double TurretTurningSpread {get; private set;}
        public double TurretLaserWidth {get; private set;}
        public double TurretLaserIntensity {get; private set;}
        public double ConstructionVisionMultiplier {get; private set;}
        public double ConstructionInitialHealthRatio {get; private set;}
        public double BuildingDestructionSeconds {get; private set;}
        public double BuildingDestructionShake {get; private set;}
        public double BuildingDestructionTilt {get; private set;}
        public double BuildingDestructionFireScale {get; private set;}
        public double BuildingDestructionSmokeOpacity {get; private set;}

        public double VisionCellSize {get; private set;}
        public double FogRevealMs {get; private set;}
        public double FogConcealMs {get; private set;}
        public double LingeringVisionSeconds {get; private set;}
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
        public double HawkModelScale {get; private set;}
        public double HawkAimTolerance {get; private set;}
public double HawkPreparationYawSpeed {get; private set;}
public double HawkMissileLength {get; private set;}
public double HawkMissileVisualScale {get; private set;}
public double HawkDebrisSeconds {get; private set;}
public double HawkDebrisSpeed {get; private set;}
public double HawkDebrisGravity {get; private set;}
public double HawkDebrisCount {get; private set;}
        public double HawkHealth {get; private set;}
public double HawkCreditCost {get; private set;}
public double HawkPopulationCost {get; private set;}
public double HawkProductionSeconds {get; private set;}
public double HawkRadius {get; private set;}
public double HawkSpeed {get; private set;}
public double HawkReverseSpeed {get; private set;}
public double HawkStrafeSpeed {get; private set;}
public double HawkAcceleration {get; private set;}
public double HawkBraking {get; private set;}
public double HawkShortSeconds {get; private set;}
public double HawkShortDistance {get; private set;}
public double HawkYawSpeed {get; private set;}
public double HawkYawAcceleration {get; private set;}
public double HawkCruisePitch {get; private set;}
public double HawkAccelerationPitch {get; private set;}
public double HawkBank {get; private set;}
public double HawkAttitudeSpeed {get; private set;}
public double HawkClearance {get; private set;}
public double HawkClimbSpeed {get; private set;}
public double HawkDescentSpeed {get; private set;}
public double HawkLookahead {get; private set;}
public double HawkSeparation {get; private set;}
public double FactoryAssemblyGap {get; private set;}
public double HawkRoofSeconds {get; private set;}
public double HawkPlatformSeconds {get; private set;}
public double HawkSpinSeconds {get; private set;}
public double HawkRotorSpeed {get; private set;}
public double HawkRange {get; private set;}
public double HawkPreparationSeconds {get; private set;}
public double HawkDamage {get; private set;}
public double HawkSalvoDelay {get; private set;}
public double HawkReloadSeconds {get; private set;}
public double HawkSalvoResearchCost {get; private set;}
public double HawkSalvoResearchSeconds {get; private set;}
public double HawkUpgradedPreparationSeconds {get; private set;}
public double HawkUpgradedSalvoDelay {get; private set;}
public double HawkUpgradedReloadSeconds {get; private set;}
public double HawkMissileSpeed {get; private set;}
public double HawkMissileTurn {get; private set;}
public double HawkMissileRadius {get; private set;}
        public int HangarHealth {get; private set;}
        public int HangarBuildSeconds {get; private set;}
        public int HangarCreditCost {get; private set;}
        public double HangarVisionRange {get; private set;}
        public double HangarFootprintRadius {get; private set;}
        public double HangarModelHeightMeters {get; private set;}
        public double HangarModelScale {get; private set;}
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
        public double WeaponSystemsLevel3Cost => sourceData.weaponSystemsLevel3Cost;
        public double WeaponSystemsLevel3Seconds => sourceData.weaponSystemsLevel3Seconds;
        public double WeaponSystemsLevel3BonusPercent => sourceData.weaponSystemsLevel3BonusPercent;
        public double DefensiveSystemsLevel3Cost => sourceData.defensiveSystemsLevel3Cost;
        public double DefensiveSystemsLevel3Seconds => sourceData.defensiveSystemsLevel3Seconds;
        public double DefensiveSystemsLevel3BonusPercent => sourceData.defensiveSystemsLevel3BonusPercent;
        public int ProducerUpgradeCost(PlayableBuildingKind kind)=>kind==PlayableBuildingKind.Hangar?sourceData.hangarUpgradeCost:sourceData.factoryUpgradeCost;
        public double ProducerUpgradeSeconds(PlayableBuildingKind kind)=>kind==PlayableBuildingKind.Hangar?sourceData.hangarUpgradeSeconds:sourceData.factoryUpgradeSeconds;
        public double UpgradedProducerSpeedMultiplier=>sourceData.upgradedProducerSpeedMultiplier;
        public double ScientificCenterUpgradeCost => sourceData.scientificCenterUpgradeCost;
        public double ScientificCenterUpgradeSeconds => sourceData.scientificCenterUpgradeSeconds;
        public double WeaponSystemsLevel1Cost => sourceData.weaponSystemsLevel1Cost;
        public double WeaponSystemsLevel1Seconds => sourceData.weaponSystemsLevel1Seconds;
        public double WeaponSystemsLevel1BonusPercent => sourceData.weaponSystemsLevel1BonusPercent;
        public double WeaponSystemsLevel2Cost => sourceData.weaponSystemsLevel2Cost;
        public double WeaponSystemsLevel2Seconds => sourceData.weaponSystemsLevel2Seconds;
        public double WeaponSystemsLevel2BonusPercent => sourceData.weaponSystemsLevel2BonusPercent;
        public double DefensiveSystemsLevel1Cost => sourceData.defensiveSystemsLevel1Cost;
        public double DefensiveSystemsLevel1Seconds => sourceData.defensiveSystemsLevel1Seconds;
        public double DefensiveSystemsLevel1BonusPercent => sourceData.defensiveSystemsLevel1BonusPercent;
        public double DefensiveSystemsLevel2Cost => sourceData.defensiveSystemsLevel2Cost;
        public double DefensiveSystemsLevel2Seconds => sourceData.defensiveSystemsLevel2Seconds;
        public double DefensiveSystemsLevel2BonusPercent => sourceData.defensiveSystemsLevel2BonusPercent;
        public double ArmySystemsMinimumUnits => sourceData.armySystemsMinimumUnits;
        public double ArmySystemsAiDelaySeconds(Spacewars.Simulation.Ai.AiDifficulty difficulty,int level=1)=>difficulty switch {
            Spacewars.Simulation.Ai.AiDifficulty.Recruit=>level==1?sourceData.aiArmySystemsRecruitDelaySeconds:sourceData.aiArmySystemsRecruitLevel2DelaySeconds,
            Spacewars.Simulation.Ai.AiDifficulty.Fighter=>level==1?sourceData.aiArmySystemsFighterDelaySeconds:sourceData.aiArmySystemsFighterLevel2DelaySeconds,
            Spacewars.Simulation.Ai.AiDifficulty.Veteran=>level==1?sourceData.aiArmySystemsVeteranDelaySeconds:sourceData.aiArmySystemsVeteranLevel2DelaySeconds,
            _=>throw new ArgumentOutOfRangeException(nameof(difficulty))};
        public double ArmySystemsAiUtilityScale(Spacewars.Simulation.Ai.AiDifficulty difficulty)=>difficulty switch {
            Spacewars.Simulation.Ai.AiDifficulty.Recruit=>sourceData.aiArmySystemsRecruitUtilityScale,
            Spacewars.Simulation.Ai.AiDifficulty.Fighter=>sourceData.aiArmySystemsFighterUtilityScale,
            Spacewars.Simulation.Ai.AiDifficulty.Veteran=>sourceData.aiArmySystemsVeteranUtilityScale,
            _=>throw new ArgumentOutOfRangeException(nameof(difficulty))};
        public double ArmySystemsAiCatchupSeconds(Spacewars.Simulation.Ai.AiDifficulty difficulty)=>difficulty switch {
            Spacewars.Simulation.Ai.AiDifficulty.Recruit=>0,
            Spacewars.Simulation.Ai.AiDifficulty.Fighter=>sourceData.aiArmySystemsFighterCatchupSeconds,
            Spacewars.Simulation.Ai.AiDifficulty.Veteran=>sourceData.aiArmySystemsVeteranCatchupSeconds,
            _=>throw new ArgumentOutOfRangeException(nameof(difficulty))};

        public double MaximumHealthRoundingStep {get; private set;}
        public double DamageRoundingStep {get; private set;}
        public double TankChassisHealthBonusPercent {get; private set;}
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
        public int ShkvalDirectHitDamage=>sourceData.shkvalDirectHitDamage;
        public double ShkvalFireIntervalMs {get; private set;}
        public double ShkvalProjectileSpeed {get; private set;}
        public double ShkvalArcHeight {get; private set;}
        public double ShkvalBlastRadius {get; private set;}
        public double ShkvalBaseBlastRadiusMultiplier=>sourceData.shkvalBaseBlastRadiusMultiplier;
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
        public double DirectImpactSparkWidth {get; private set;}
        public double DirectImpactGlow {get; private set;}
        public double DirectImpactSmokeOpacity {get; private set;}
        public double DirectImpactRadius {get; private set;}
        public double DirectImpactSeconds {get; private set;}
        public double DirectImpactSparkLength {get; private set;}
        public double ExplorerImpactScale {get; private set;}
        public double UnitWreckLifetimeSeconds {get; private set;}
        public double WreckSinkSeconds {get; private set;}
        public double WreckSpawnProtectionSeconds {get; private set;}
        public double WreckBurialMin {get; private set;}
        public double WreckBurialMax {get; private set;}
        public double WreckSinkDepth {get; private set;}
        public double HawkWreckChancePercent {get; private set;}
        public double HawkWreckFallSeconds {get; private set;}
        public double UnitDeathSmokeSeconds {get; private set;}
        public double WreckHitSmokeSeconds {get; private set;}
        public double WreckSmokeSize {get; private set;}
        public double WreckSmokeOpacity {get; private set;}
        public double WreckSmokeRise {get; private set;}

        public double ImpactEffectRadius {get; private set;}
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
        public double IncomeMarkerFontPixels {get; private set;}
        public double IncomeMarkerRisePixels {get; private set;}
        public double IncomeMarkerDurationSeconds {get; private set;}
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
            DirectImpactSparkWidth=data.directImpactSparkWidth;DirectImpactGlow=data.directImpactGlow;DirectImpactSmokeOpacity=data.directImpactSmokeOpacity;
            DirectImpactRadius=data.directImpactRadius;DirectImpactSeconds=data.directImpactSeconds;
            DirectImpactSparkLength=data.directImpactSparkLength;ExplorerImpactScale=data.explorerImpactScale;
            UnitWreckLifetimeSeconds=data.unitWreckLifetimeSeconds;
            WreckSinkSeconds=data.wreckSinkSeconds;
            WreckSpawnProtectionSeconds=data.wreckSpawnProtectionSeconds;
            WreckBurialMin=data.wreckBurialMin;
            WreckBurialMax=data.wreckBurialMax;
            WreckSinkDepth=data.wreckSinkDepth;
            HawkWreckChancePercent=data.hawkWreckChancePercent;
            HawkWreckFallSeconds=data.hawkWreckFallSeconds;
            UnitDeathSmokeSeconds=data.unitDeathSmokeSeconds;
            WreckHitSmokeSeconds=data.wreckHitSmokeSeconds;
            WreckSmokeSize=data.wreckSmokeSize;
            WreckSmokeOpacity=data.wreckSmokeOpacity;
            WreckSmokeRise=data.wreckSmokeRise;

            ImpactEffectRadius=data.impactEffectRadius;
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
            HawkModelScale=data.hawkModelScale;HawkAimTolerance=data.hawkAimTolerance;HawkPreparationYawSpeed=data.hawkPreparationYawSpeed;HawkMissileLength=data.hawkMissileLength;HawkMissileVisualScale=data.hawkMissileVisualScale;HawkDebrisSeconds=data.hawkDebrisSeconds;HawkDebrisSpeed=data.hawkDebrisSpeed;HawkDebrisGravity=data.hawkDebrisGravity;HawkDebrisCount=data.hawkDebrisCount;HawkHealth=data.hawkHealth;
HawkCreditCost=data.hawkCreditCost;
HawkPopulationCost=data.hawkPopulationCost;
HawkProductionSeconds=data.hawkProductionSeconds;
HawkRadius=data.hawkRadius;
HawkSpeed=data.hawkSpeed;
HawkReverseSpeed=data.hawkReverseSpeed;
HawkStrafeSpeed=data.hawkStrafeSpeed;
HawkAcceleration=data.hawkAcceleration;
HawkBraking=data.hawkBraking;
HawkShortSeconds=data.hawkShortSeconds;
HawkShortDistance=data.hawkShortDistance;
HawkYawSpeed=data.hawkYawSpeed;
HawkYawAcceleration=data.hawkYawAcceleration;
HawkCruisePitch=data.hawkCruisePitch;
HawkAccelerationPitch=data.hawkAccelerationPitch;
HawkBank=data.hawkBank;
HawkAttitudeSpeed=data.hawkAttitudeSpeed;
HawkClearance=data.hawkClearance;
HawkClimbSpeed=data.hawkClimbSpeed;
HawkDescentSpeed=data.hawkDescentSpeed;
HawkLookahead=data.hawkLookahead;
HawkSeparation=data.hawkSeparation;
FactoryAssemblyGap=data.factoryAssemblyGap;
HawkRoofSeconds=data.hawkRoofSeconds;
HawkPlatformSeconds=data.hawkPlatformSeconds;
HawkSpinSeconds=data.hawkSpinSeconds;
HawkRotorSpeed=data.hawkRotorSpeed;
HawkRange=data.hawkRange;
HawkPreparationSeconds=data.hawkPreparationSeconds;
HawkDamage=data.hawkDamage;
HawkSalvoDelay=data.hawkSalvoDelay;
HawkReloadSeconds=data.hawkReloadSeconds;
HawkSalvoResearchCost=data.hawkSalvoResearchCost;
HawkSalvoResearchSeconds=data.hawkSalvoResearchSeconds;
HawkUpgradedPreparationSeconds=data.hawkUpgradedPreparationSeconds;
HawkUpgradedSalvoDelay=data.hawkUpgradedSalvoDelay;
HawkUpgradedReloadSeconds=data.hawkUpgradedReloadSeconds;
HawkMissileSpeed=data.hawkMissileSpeed;
HawkMissileTurn=data.hawkMissileTurn;
HawkMissileRadius=data.hawkMissileRadius;
            HangarHealth=data.hangarHealth;
            HangarBuildSeconds=data.hangarBuildSeconds;
            HangarCreditCost=data.hangarCreditCost;
            HangarVisionRange=data.hangarVisionRange;
            HangarFootprintRadius=data.hangarFootprintRadius;
            HangarModelHeightMeters=data.hangarModelHeightMeters;
            HangarModelScale=data.hangarModelScale;
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
            MaximumHealthRoundingStep=data.maximumHealthRoundingStep;DamageRoundingStep=data.damageRoundingStep;TankChassisHealthBonusPercent=data.tankChassisHealthBonusPercent;TankChassisGlowIntensity=data.tankChassisGlowIntensity;TankChassisCost=data.tankChassisCost;TankChassisSeconds=data.tankChassisSeconds;TankChassisSpeed=data.tankChassisSpeed;
            ExplorerAssaultCost=data.explorerAssaultCost;ExplorerAssaultSeconds=data.explorerAssaultSeconds;ExplorerAssaultBurstSize=(int)data.explorerAssaultBurstSize;ExplorerAssaultSpreadDeg=data.explorerAssaultSpreadDeg;
            ShkvalGuidanceCost=data.shkvalGuidanceCost;ShkvalGuidanceSeconds=data.shkvalGuidanceSeconds;ShkvalGuidanceRange=data.shkvalGuidanceRange;
            BuildingSaleRefundRatio=data.buildingSaleRefundRatio;
            BuildingSaleCombatLockoutSec=data.buildingSaleCombatLockoutSec;
            BuildingSaleDemolitionSec=data.buildingSaleDemolitionSec;
            BuildingRepairDurationSec=data.buildingRepairDurationSec;
            BuildingRepairCostRatio=data.buildingRepairCostRatio;
            BuildingRepairCombatLockoutSec=data.buildingRepairCombatLockoutSec;
            SaleMarkerScale=data.saleMarkerScale;
            IncomeMarkerFontPixels=data.incomeMarkerFontPixels;IncomeMarkerRisePixels=data.incomeMarkerRisePixels;IncomeMarkerDurationSeconds=data.incomeMarkerDurationSeconds;
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
            RepairTowerHealthPerSecond=data.repairTowerHealthPerSecond;
            TurretCreditCost=data.turretCreditCost;
            TurretBuildSeconds=data.turretBuildSeconds;
            TurretHealthMultiplier=data.turretHealthMultiplier;
            TurretRangeMultiplier=data.turretRangeMultiplier;
            TurretFootprintRadius=data.turretFootprintRadius;
            TurretPadOffset=data.turretPadOffset;
            TurretPassageWidth=data.turretPassageWidth;
            TurretVision=data.turretVision;
            TurretModelScale=data.turretModelScale;
            TurretYawSpeed=data.turretYawSpeed;
            TurretPitchSpeed=data.turretPitchSpeed;
            TurretDepression=data.turretDepression;
            TurretTurningSpread=data.turretTurningSpread;
            TurretLaserWidth=data.turretLaserWidth;
            TurretLaserIntensity=data.turretLaserIntensity;
            ConstructionInitialHealthRatio=data.constructionInitialHealthRatio;
            BuildingDestructionSeconds=data.buildingDestructionSeconds;
            BuildingDestructionShake=data.buildingDestructionShake;
            BuildingDestructionTilt=data.buildingDestructionTilt;
            BuildingDestructionFireScale=data.buildingDestructionFireScale;
            BuildingDestructionSmokeOpacity=data.buildingDestructionSmokeOpacity;

            VisionCellSize=data.visionCellSize;
            FogRevealMs=data.fogRevealMs;
            FogConcealMs=data.fogConcealMs;
            LingeringVisionSeconds=data.lingeringVisionSeconds;
            FogEdgeFeather=data.fogEdgeFeather;
            FogExploredOpacity=data.fogExploredOpacity;
            FogUnseenOpacity=data.fogUnseenOpacity;
            MinimapLandmarkSize=data.minimapLandmarkSize;
            MinimapLandmarkOutline=data.minimapLandmarkOutline;
            MinimapUnitMarkerSize=data.minimapUnitMarkerSize;

            OutpostHealth=data.outpostHealth;OutpostBuildSeconds=data.outpostBuildSeconds;OutpostCreditCost=data.outpostCreditCost;OutpostIncomePerPeriod=data.outpostIncomePerPeriod;OutpostFootprintRadius=data.outpostFootprintRadius;OutpostModelHeightMeters=data.outpostModelHeightMeters;OutpostModelScale=data.outpostModelScale;MineHealth=data.mineHealth;MineBuildSeconds=data.mineBuildSeconds;MineCreditCost=data.mineCreditCost;MineIncomePerPeriod=data.mineIncomePerPeriod;MineFootprintRadius=data.mineFootprintRadius;MineModelHeightMeters=data.mineModelHeightMeters;MineModelScale=data.mineModelScale;HeadquartersCaptureRadius=data.headquartersCaptureRadius;HeadquartersCaptureSeconds=data.headquartersCaptureSeconds;OutpostCaptureRadius=data.outpostCaptureRadius;OutpostCaptureSeconds=data.outpostCaptureSeconds;MineCaptureRadius=data.mineCaptureRadius;MineCaptureSeconds=data.mineCaptureSeconds;RelayIncomePerPeriod=data.relayIncomePerPeriod;RelayCaptureRadius=data.relayCaptureRadius;RelayCaptureSeconds=data.relayCaptureSeconds;RelayFootprintRadius=data.relayFootprintRadius;RelayVisionRange=data.relayVisionRange;HeadquartersSlots=data.headquartersSlots;OutpostSlots=data.outpostSlots;SlotRingRadius=data.slotRingRadius;OrdinaryPadRadius=data.ordinaryPadRadius;PadBorderRatio=data.padBorderRatio;CapturePulseHz=data.capturePulseHz;ConstructionDamageMultiplier=data.constructionDamageMultiplier;EvacuationClearance=data.evacuationClearance;EvacuationRetrySeconds=data.evacuationRetrySeconds;OutpostX=data.outpostX;OutpostZ=data.outpostZ;MineX=data.mineX;MineZ=data.mineZ;
            DefenderOffsetX=data.defenderOffsetX;DefenderOffsetZ=data.defenderOffsetZ;
            SchemaVersion = data.schemaVersion; ProfileId = data.profileId; Revision = data.revision;
            SourceCommit = data.sourceCommit; SourceProfileId = data.sourceProfileId; SourceProfileRevision = data.sourceProfileRevision; SourceManifestSha256 = data.sourceManifestSha256;
            HealthBarWidth=data.healthBarWidth;HealthBarHeight=data.healthBarHeight;HealthBarModelGap=data.healthBarModelGap;HealthBarCellGap=data.healthBarCellGap;HealthBarCells=data.healthBarCells;HealthBarOutlineWidth=data.healthBarOutlineWidth;BuildingSelectionPadding=data.buildingSelectionPadding;BuildingSelectionWidth=data.buildingSelectionWidth;
            GroundTrailLifetimeSeconds=data.groundTrailLifetimeSeconds;SelectionFrameRed=data.selectionFrameRed;SelectionFrameGreen=data.selectionFrameGreen;SelectionFrameBlue=data.selectionFrameBlue;SelectionFrameLineWidth=data.selectionFrameLineWidth;SelectionFrameCornerWidth=data.selectionFrameCornerWidth;SelectionFrameCornerLength=data.selectionFrameCornerLength;SelectionFrameFillOpacity=data.selectionFrameFillOpacity;SelectionFrameLineOpacity=data.selectionFrameLineOpacity;SelectionRingPadding=data.selectionRingPadding;SelectionRingWidth=data.selectionRingWidth;SelectionRingLift=data.selectionRingLift;
            HawkSelectionRadius=data.hawkSelectionRadius;
            UnitOrderQueueLimit=(int)data.unitOrderQueueLimit;GroupMarchMaximumStretch=data.groupMarchMaximumStretch;FollowDistance=data.followDistance;FollowArrivalTolerance=data.followArrivalTolerance;DefaultCombatPursuitSeconds=data.defaultCombatPursuitSeconds;IdleAutoDefenseMultiplier=data.idleAutoDefenseMultiplier;TankCollisionRadius = data.tankCollisionRadius; TankModelRadius = data.tankModelRadius; TankModelScale = data.tankModelScale; TankSpeed = data.tankSpeed;TankReverseDistance=data.tankReverseDistance;TankReverseSpeedRatio=data.tankReverseSpeedRatio;TankReverseConeRadians=data.tankReverseConeRadians; TankTurnSpeed = data.tankTurnSpeed; TankTurretTurnSpeed = data.tankTurretTurnSpeed; TankAimToleranceRadians = data.tankAimToleranceRadians; TankRange = data.tankRange; TankVisionRange = data.tankVisionRange;
            TankHealth = data.tankHealth; TankFiresWhileMoving = data.tankFiresWhileMoving; TankStopForMilliseconds = data.tankStopForMilliseconds; TankPopulationCost = data.tankPopulationCost; TankCreditCost = data.tankCreditCost; TankProductionMenuOrder = data.tankProductionMenuOrder; TankProductionSeconds = data.tankProductionSeconds; TankWeaponDamage = data.tankWeaponDamage; TankWeaponReloadMilliseconds = data.tankWeaponReloadMilliseconds; TankProjectileType = data.tankProjectileType; TankProjectileSpeed = data.tankProjectileSpeed; TankProjectileCollisionRadius = data.tankProjectileCollisionRadius; ProjectileExtraRangePercent = data.projectileExtraRangePercent;
            MatchScoreEarnedCreditsDivisor=data.matchScoreEarnedCreditsDivisor; ArmyCapacity = data.armyCapacity; StartingCredits = data.startingCredits; IncomePeriodSeconds = data.incomePeriodSeconds; IncomeSettlementSeconds=data.incomeSettlementSeconds; BuildingCancellationRefundRatio = data.buildingCancellationRefundRatio; UnitCancellationRefundRatio = data.unitCancellationRefundRatio;
            RallyFlagHeight=data.rallyFlagHeight; HeadquartersHealth = data.headquartersHealth; HeadquartersBuildSeconds = data.headquartersBuildSeconds; HeadquartersCreditCost = data.headquartersCreditCost; HeadquartersIncomePerPeriod = data.headquartersIncomePerPeriod; HeadquartersFootprintRadius = data.headquartersFootprintRadius; HeadquartersModelHeightMeters = data.headquartersModelHeightMeters; HeadquartersModelScale = data.headquartersModelScale;
            FactoryGateSeconds=data.factoryGateSeconds;FactoryRolloutSpeed=data.factoryRolloutSpeed;
            FactoryHealth = data.factoryHealth; FactoryBuildSeconds = data.factoryBuildSeconds; FactoryCreditCost = data.factoryCreditCost; FactoryFootprintRadius = data.factoryFootprintRadius; FactoryModelHeightMeters = data.factoryModelHeightMeters; FactoryModelScale = data.factoryModelScale;
            RefineryHealth = data.refineryHealth; RefineryBuildSeconds = data.refineryBuildSeconds; RefineryCreditCost = data.refineryCreditCost; RefineryIncomePerPeriod = data.refineryIncomePerPeriod; RefineryFootprintRadius = data.refineryFootprintRadius; RefineryModelHeightMeters = data.refineryModelHeightMeters; RefineryModelScale = data.refineryModelScale;
            ArenaHalfExtent = data.arenaHalfExtent; PlayerHeadquartersX = data.playerHeadquartersX; EnemyHeadquartersX = data.enemyHeadquartersX; HeadquartersZ = data.headquartersZ;
            PlayerFactoryPadX = data.playerFactoryPadX; PlayerFactoryPadZ = data.playerFactoryPadZ; PlayerRefineryPadX = data.playerRefineryPadX; PlayerRefineryPadZ = data.playerRefineryPadZ;
            EnemyFactoryPadX = data.enemyFactoryPadX; EnemyFactoryPadZ = data.enemyFactoryPadZ; EnemyRefineryPadX = data.enemyRefineryPadX; EnemyRefineryPadZ = data.enemyRefineryPadZ;
            CentralObstacleHalfWidth = data.centralObstacleHalfWidth; CentralObstacleHalfDepth = data.centralObstacleHalfDepth; PassageHalfWidth = data.passageHalfWidth;
            EnemyAdvanceDelaySeconds = data.enemyAdvanceDelaySeconds; AttackApproachRangeRatio = data.attackApproachRangeRatio; AttackRepathSeconds = data.attackRepathSeconds; FactoryExitDistance = data.factoryExitDistance; DefaultRallyDistance = data.defaultRallyDistance; TargetPickRadiusMultiplier = data.targetPickRadiusMultiplier; BuildingPickRadius = data.buildingPickRadius; NavigationRequestsPerFrame = data.navigationRequestsPerFrame; RenderTargetFramesPerSecond = data.renderTargetFramesPerSecond;
            CameraOrthoSize = data.cameraOrthoSize; CameraHeight = data.cameraHeight; CameraOffsetX = data.cameraOffsetX; CameraOffsetZ = data.cameraOffsetZ; CameraPanSpeed = data.cameraPanSpeed; CameraEdgePanSpeed=data.cameraEdgePanSpeed; CameraEdgePanZonePixels=data.cameraEdgePanZonePixels; CameraMinZoom = data.cameraMinZoom; CameraMaxZoom = data.cameraMaxZoom; CameraZoomSpeed = data.cameraZoomSpeed; SelectionDragPixels = data.selectionDragPixels;
            Navigation = new NavigationProfile(ProfileId, Revision, TankCollisionRadius, TankSpeed, TankTurnSpeed, .8d, .15d, 3.6d, .08d,crowdHeadingIntervals:data.crowdMotionHeadingIntervals,crowdSpeedBands:data.crowdMotionSpeedBands,crowdLookaheadSeconds:data.crowdMotionLookaheadSeconds,crowdNeighborCap:data.crowdMotionNeighborCap,groupCorridorMaximumExcessPercent:data.groupCorridorMaximumExcessPercent);
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
        public double RelayIncomePerPeriod {get;private set;}
        public double RelayCaptureRadius {get;private set;}
        public double RelayCaptureSeconds {get;private set;}
        public double RelayFootprintRadius {get;private set;}
        public double RelayVisionRange {get;private set;}
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
        public double HealthBarWidth {get;private set;} public double HealthBarHeight {get;private set;} public double HealthBarModelGap {get;private set;} public double HealthBarCellGap {get;private set;} public int HealthBarCells {get;private set;} public double HealthBarOutlineWidth {get;private set;} public double BuildingSelectionPadding {get;private set;} public double BuildingSelectionWidth {get;private set;}
        public double GroundTrailLifetimeSeconds {get;private set;}
        public int GroundTrailLifetimeTicks => (int)Math.Round(GroundTrailLifetimeSeconds*30);
        public double SelectionFrameRed {get;private set;}
        public double SelectionFrameGreen {get;private set;}
        public double SelectionFrameBlue {get;private set;}
        public double SelectionFrameLineWidth {get;private set;}
        public double SelectionFrameCornerWidth {get;private set;}
        public double SelectionFrameCornerLength {get;private set;}
        public double SelectionFrameFillOpacity {get;private set;}
        public double SelectionFrameLineOpacity {get;private set;}
        public double SelectionRingPadding {get;private set;} public double SelectionRingWidth {get;private set;} public double SelectionRingLift {get;private set;}
        public double HawkSelectionRadius {get;private set;}
        public double TankReverseDistance{get;private set;} public double TankReverseSpeedRatio{get;private set;} public double TankReverseConeRadians{get;private set;}
        public double TankCollisionRadius { get; private set; } public double TankModelRadius { get; private set; } public double TankModelScale { get; private set; } public double TankSpeed { get; private set; } public double TankTurnSpeed { get; private set; } public double TankTurretTurnSpeed { get; private set; } public double TankAimToleranceRadians { get; private set; } public double TankRange { get; private set; } public double TankVisionRange { get; private set; }
        public int TankHealth { get; private set; } public bool TankFiresWhileMoving { get; private set; } public int TankStopForMilliseconds { get; private set; } public int TankPopulationCost { get; private set; } public int TankCreditCost { get; private set; } public int TankProductionMenuOrder { get; private set; } public int TankProductionSeconds { get; private set; } public int TankWeaponDamage { get; private set; } public int TankWeaponReloadMilliseconds { get; private set; } public string TankProjectileType { get; private set; } public double TankProjectileSpeed { get; private set; } public double TankProjectileCollisionRadius { get; private set; } public double ProjectileExtraRangePercent { get; private set; }
        public int UnitOrderQueueLimit{get;private set;}
        public double GroupMarchMaximumStretch{get;private set;}
        public double FollowDistance{get;private set;} public double FollowArrivalTolerance{get;private set;}
        public double DefaultCombatPursuitSeconds {get;private set;}
        public double IdleAutoDefenseMultiplier{get;private set;}
        public double TankPreparationSeconds { get { return TankStopForMilliseconds / 1000d; } } public double TankWeaponReloadSeconds { get { return TankWeaponReloadMilliseconds / 1000d; } } public double TankProjectileMaxTravel { get { return TankRange * (1d + ProjectileExtraRangePercent / 100d); } }
        public int MatchScoreEarnedCreditsDivisor {get;private set;}
        public int ArmyCapacity { get; private set; } public int StartingCredits { get; private set; } public int IncomePeriodSeconds { get; private set; } public double IncomeSettlementSeconds { get; private set; } public double BuildingCancellationRefundRatio { get; private set; } public double UnitCancellationRefundRatio { get; private set; }
        public int HeadquartersHealth { get; private set; } public int HeadquartersBuildSeconds { get; private set; } public int HeadquartersCreditCost { get; private set; } public int HeadquartersIncomePerPeriod { get; private set; } public double HeadquartersFootprintRadius { get; private set; } public double HeadquartersModelHeightMeters { get; private set; } public double HeadquartersModelScale { get; private set; }
        public double RallyFlagHeight { get; private set; }
        public double FactoryGateSeconds {get;private set;} public double FactoryRolloutSpeed {get;private set;}
        public int FactoryHealth { get; private set; } public int FactoryBuildSeconds { get; private set; } public int FactoryCreditCost { get; private set; } public double FactoryFootprintRadius { get; private set; } public double FactoryModelHeightMeters { get; private set; } public double FactoryModelScale { get; private set; }
        public int RefineryHealth { get; private set; } public int RefineryBuildSeconds { get; private set; } public int RefineryCreditCost { get; private set; } public int RefineryIncomePerPeriod { get; private set; } public double RefineryFootprintRadius { get; private set; } public double RefineryModelHeightMeters { get; private set; } public double RefineryModelScale { get; private set; }
        public double HeadquartersIncomePerSecond { get { return HeadquartersIncomePerPeriod / (double)IncomePeriodSeconds; } } public double RefineryIncomePerSecond { get { return RefineryIncomePerPeriod / (double)IncomePeriodSeconds; } }
        public double ArenaHalfExtent { get; private set; } public double GroundHalfExtent { get { return ArenaHalfExtent; } } public double PlayerHeadquartersX { get; private set; } public double EnemyHeadquartersX { get; private set; } public double HeadquartersZ { get; private set; }
        public double PlayerFactoryPadX { get; private set; } public double PlayerFactoryPadZ { get; private set; } public double PlayerRefineryPadX { get; private set; } public double PlayerRefineryPadZ { get; private set; }
        public double EnemyFactoryPadX { get; private set; } public double EnemyFactoryPadZ { get; private set; } public double EnemyRefineryPadX { get; private set; } public double EnemyRefineryPadZ { get; private set; }
        public double CentralObstacleHalfWidth { get; private set; } public double CentralObstacleHalfDepth { get; private set; } public double PassageHalfWidth { get; private set; }
        public double EnemyAdvanceDelaySeconds { get; private set; } public double AttackApproachRangeRatio { get; private set; } public double AttackRepathSeconds { get; private set; } public double FactoryExitDistance { get; private set; } public double DefaultRallyDistance { get; private set; } public double TargetPickRadiusMultiplier { get; private set; } public double BuildingPickRadius { get; private set; } public int NavigationRequestsPerFrame { get; private set; } public int RenderTargetFramesPerSecond { get; private set; }
        public double CameraOrthoSize { get; private set; } public double CameraHeight { get; private set; } public double CameraOffsetX { get; private set; } public double CameraOffsetZ { get; private set; } public double CameraEdgePanSpeed { get; private set; } public double CameraEdgePanZonePixels { get; private set; } public double CameraPanSpeed { get; private set; } public double CameraMinZoom { get; private set; } public double CameraMaxZoom { get; private set; } public double CameraZoomSpeed { get; private set; } public double SelectionDragPixels { get; private set; }
        public int ProvingGroundStartingCredits=>sourceData.provingGroundStartingCredits;
        public double ProvingGroundDividerWidth=>sourceData.provingGroundDividerWidth;
        public double ReinforcedConcreteLevel2Cost=>sourceData.reinforcedConcreteLevel2Cost;
        public double ReinforcedConcreteLevel2Seconds=>sourceData.reinforcedConcreteLevel2Seconds;
        public double ReinforcedConcreteLevel2BonusPercent=>sourceData.reinforcedConcreteLevel2BonusPercent;
        public double ReinforcedConcreteCost=>sourceData.reinforcedConcreteCost;
        public double ReinforcedConcreteSeconds=>sourceData.reinforcedConcreteSeconds;
        public double ReinforcedConcreteBonusPercent=>sourceData.reinforcedConcreteBonusPercent;
        public double ProvingGroundActionSeconds=>sourceData.provingGroundActionSeconds;
        public double ProvingGroundAttackRearLaneOffset=>sourceData.provingGroundAttackRearLaneOffset;
        public int MaxAccruedCredits=>sourceData.navigationMaxAccruedCredits==0?600:sourceData.navigationMaxAccruedCredits;
        public bool HasAccruedCreditExtension=>sourceData.navigationMaxAccruedCredits!=0;
        public NavigationProfile Navigation { get; private set; }

        public IPlayableTerrain AuthoredMap { get; private set; }
        public NavPoint Headquarters(PlayableOwner owner)=>AuthoredMap!=null?AuthoredMap.Headquarters(owner):new NavPoint(owner==PlayableOwner.Player?PlayerHeadquartersX:EnemyHeadquartersX,HeadquartersZ);
        public static PlayableProfile Create(PlayableProfileData data,IPlayableTerrain map=null,string displayName=null) { Validate(data); var result=new PlayableProfile(data); result.DisplayName=displayName??"Основной";result.AuthoredMap=map;if(map!=null)result.ArenaHalfExtent=map.HalfExtent; TerritoryRules.ValidateArena(result); return result; }
        public static PlayableProfile RecastLayerQualification {get{var data=DefaultData();data.groundRouteBackend=1;return Create(data,new StackedRampQualificationMap(),"recast-layer-qualification-v1");}}
        public static PlayableProfile RecastGroupCorridorDefault {get{var data=DefaultData();data.groundRouteBackend=1;return Create(data,ThreeCrossingsMap.Default,"recast-group-corridor-v1");}}
        public static PlayableProfile ThreeCrossingsDefault=>Create(DefaultData(),ThreeCrossingsMap.Default);
        public static PlayableProfile CertifiedGroupCorridorDefault {get{var data=DefaultData();data.groupCorridorMaximumExcessPercent=15;var result=Create(data,ThreeCrossingsMap.Default);result.DisplayName="certified-group-corridor-v1";return result;}}
        public static PlayableProfile Default { get { return Create(DefaultData()); } }

        public static void Validate(PlayableProfileData data)
        {
            if (data != null && data.matchScoreEarnedCreditsDivisor<1) throw new ArgumentException("Score divisor must be positive.");
            if(data!=null&&data.navigationMaxAccruedCredits<0)throw new ArgumentException("Invalid accrued credit extension.");
            if (data == null) throw new ArgumentNullException("data");
            if(data.orbitalMineCapacity!=Math.Truncate(data.orbitalMineCapacity)||data.orbitalMineFragmentCount!=Math.Truncate(data.orbitalMineFragmentCount))throw new ArgumentException("Mine counts must be integral.");
            if(data.groundRouteBackend<0||data.groundRouteBackend>1)throw new ArgumentException("Unknown ground route backend.");
            if (data.schemaVersion != RequiredSchemaVersion || data.profileId != RequiredProfileId || data.revision < 1) throw new ArgumentException("Unsupported playable profile identity.", "data");
            if (String.IsNullOrWhiteSpace(data.sourceCommit) || String.IsNullOrWhiteSpace(data.sourceProfileId) || data.sourceProfileRevision < 1 || String.IsNullOrWhiteSpace(data.sourceManifestSha256)) throw new ArgumentException("Playable profile provenance is incomplete.", "data");
            if (data.tankProjectileType != "kinetic-shell") throw new ArgumentException("Playable profile requires the release tank projectile type.", "data");
            if(data.shkvalMarkerStartRadius<data.shkvalBlastRadius)throw new ArgumentException("Artillery marker must cover blast radius.");
            if(data.wreckBurialMin>data.wreckBurialMax)throw new ArgumentException("Minimum wreck burial exceeds maximum.",nameof(data));
            foreach (PlayableProfileField field in PlayableProfileMetadata.Fields) { double value = field.Read(data); if(field.FieldName==nameof(PlayableProfileData.groupCorridorMaximumExcessPercent)&&value==0)continue; if (Double.IsNaN(value) || Double.IsInfinity(value) || value < field.Minimum || value > field.Maximum) throw new ArgumentException("Invalid playable profile field: " + field.Path, "data"); }
            foreach(var field in PlayableProfileMetadata.Fields)if(field.Path.StartsWith("system.follow.")||field.Path.StartsWith("combat.default.")){double steps=(field.Read(data)-field.Minimum)/field.Step;if(Math.Abs(steps-Math.Round(steps))>1e-8)throw new ArgumentException("Follow value must align to source metadata step: "+field.Path);}
            foreach(var field in PlayableProfileMetadata.Fields)if(field.Path.StartsWith("groupMarch.")){double steps=(field.Read(data)-field.Minimum)/field.Step;if(Math.Abs(steps-Math.Round(steps))>1e-8)throw new ArgumentException("March value must align to metadata step: "+field.Path);}
            foreach(var field in PlayableProfileMetadata.CrowdFields){double steps=(field.Read(data)-field.Minimum)/field.Step;if(Math.Abs(steps-Math.Round(steps))>1e-8||field.Step==1&&field.Read(data)!=Math.Truncate(field.Read(data)))throw new ArgumentException("Crowd value must align to metadata: "+field.Path);}
            if(data.reinforcedConcreteLevel2BonusPercent<data.reinforcedConcreteBonusPercent)throw new ArgumentException("Concrete levels must be cumulative.");
            if(data.weaponSystemsLevel3BonusPercent<data.weaponSystemsLevel2BonusPercent||data.defensiveSystemsLevel3BonusPercent<data.defensiveSystemsLevel2BonusPercent||data.weaponSystemsLevel2BonusPercent<data.weaponSystemsLevel1BonusPercent||data.defensiveSystemsLevel2BonusPercent<data.defensiveSystemsLevel1BonusPercent)throw new ArgumentException("Army systems levels must be cumulative.");
            foreach(var field in PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("research.centerUpgrade.")||f.Path.StartsWith("research.weaponSystems.")||f.Path.StartsWith("research.defensiveSystems.")||f.Path=="ai.research.armySystemsMinimumUnits"||f.Path.StartsWith("ai.research.armySystems."))){double steps=(field.Read(data)-field.Minimum)/field.Step;if(Math.Abs(steps-Math.Round(steps))>1e-8)throw new ArgumentException("Army systems value must align to metadata: "+field.Path);}
            foreach(var field in PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("teamSignals."))){double steps=(field.Read(data)-field.Minimum)/field.Step;if(Math.Abs(steps-Math.Round(steps))>1e-8)throw new ArgumentException("Signal value must align to metadata: "+field.Path);}
            if(data.aiChatObserverLimit!=Math.Truncate(data.aiChatObserverLimit))throw new ArgumentException("Communication limits must be integers.");
            if(data.chatVisibleLimit!=Math.Truncate(data.chatVisibleLimit))throw new ArgumentException("Chat limit must be an integer.");
            if(data.unitOrderQueueLimit!=Math.Truncate(data.unitOrderQueueLimit))throw new ArgumentException("Queue limit must be an integer.");
            if(data.fogExploredOpacity>=data.fogUnseenOpacity)throw new ArgumentException("Explored terrain must be clearer than unseen terrain.");
            if (data.headquartersSlots != Math.Floor(data.headquartersSlots) || data.outpostSlots != Math.Floor(data.outpostSlots)) throw new ArgumentException("Slot counts must be integers.");
            if (data.playerHeadquartersX >= data.enemyHeadquartersX || data.centralObstacleHalfWidth >= data.arenaHalfExtent || data.centralObstacleHalfDepth >= data.arenaHalfExtent || data.passageHalfWidth >= data.centralObstacleHalfDepth || data.cameraMinZoom > data.cameraMaxZoom) throw new ArgumentException("Playable arena layout or camera range is invalid.", "data");
        }

        private static PlayableProfileData DefaultData()
        {
            return new PlayableProfileData {
                groundRouteBackend=1,
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

                outpostHealth=300,outpostBuildSeconds=20,outpostCreditCost=300,outpostIncomePerPeriod=5,outpostFootprintRadius=1.7,outpostModelHeightMeters=4.6,outpostModelScale=0.8,mineHealth=250,mineBuildSeconds=15,mineCreditCost=250,mineIncomePerPeriod=15,mineFootprintRadius=1.4,mineModelHeightMeters=2.4,mineModelScale=0.7,headquartersCaptureRadius=4.5,headquartersCaptureSeconds=5,outpostCaptureRadius=3.5,outpostCaptureSeconds=5,mineCaptureRadius=3,mineCaptureSeconds=5,headquartersSlots=5,outpostSlots=3,slotRingRadius=6.5,ordinaryPadRadius=1.6,padBorderRatio=0.06,capturePulseHz=2,constructionDamageMultiplier=1.5,evacuationClearance=1,evacuationRetrySeconds=1,outpostX=-16,outpostZ=20,mineX=16,mineZ=-20,
                defenderOffsetX=5,defenderOffsetZ=4, schemaVersion=RequiredSchemaVersion, armyCapacity=100, profileId=RequiredProfileId, revision=1, sourceCommit="7d0acb8d078451eaf0ed166a6f8b6d748925abf8", sourceProfileId="local:main:1788708872201:1", sourceProfileRevision=5, sourceManifestSha256="a9b40994adc17b96d9079279044b75cd79af2d6a3ab9c2c8ed576e6d1dad86b6",
                followDistance=2, followArrivalTolerance=.25, idleAutoDefenseMultiplier=1.1, tankCollisionRadius=.72d, tankModelRadius=.65d, tankModelScale=1d, tankSpeed=4d, tankTurnSpeed=4.5d, tankTurretTurnSpeed=6d, tankAimToleranceRadians=.034906585d, tankRange=10d, tankVisionRange=13d, tankHealth=100, tankFiresWhileMoving=true, tankStopForMilliseconds=0, tankPopulationCost=3, tankCreditCost=150, tankProductionMenuOrder=20, tankProductionSeconds=15, tankWeaponDamage=8, tankWeaponReloadMilliseconds=900, tankProjectileType="kinetic-shell", tankProjectileSpeed=15d, tankProjectileCollisionRadius=.06d, projectileExtraRangePercent=20d,
                buildingSaleRefundRatio=0.75d,buildingSaleCombatLockoutSec=5d,buildingSaleDemolitionSec=2d,buildingRepairDurationSec=30d,buildingRepairCostRatio=0.5d,buildingRepairCombatLockoutSec=5d,saleMarkerScale=0.7d,
                lifecycleMarkerPixels=30d,lifecycleMarkerPulseHz=1d,lifecycleMarkerMinOpacity=0.4d,lifecycleMarkerOffsetMeters=0.8d,
                scienceHealth=250, scienceBuildSeconds=20, scienceVisionRange=14, scienceFootprintRadius=1.45, scienceCreditCost=300, scienceModelHeightMeters=1.6407, scienceModelScale=0.8, refineryUpgradeCost=250, refineryUpgradeSeconds=20, refineryUpgradedIncome=20, refineryTurbineSpeed=90,tankChassisGlowIntensity=1.2,tankChassisCost=300,tankChassisSeconds=30,tankChassisSpeed=6,explorerAssaultCost=300,explorerAssaultSeconds=30,explorerAssaultBurstSize=6,explorerAssaultSpreadDeg=5,shkvalGuidanceCost=300,shkvalGuidanceSeconds=30,shkvalGuidanceRange=16, startingCredits=500, incomePeriodSeconds=5, buildingCancellationRefundRatio=.75d, unitCancellationRefundRatio=1d, headquartersHealth=400, headquartersBuildSeconds=25, headquartersCreditCost=500, headquartersIncomePerPeriod=20, headquartersFootprintRadius=2.05d, headquartersModelHeightMeters=4.6d, headquartersModelScale=.7d, factoryHealth=250, factoryBuildSeconds=15, factoryCreditCost=300, factoryFootprintRadius=1.45d, factoryModelHeightMeters=2.5d, factoryModelScale=.7d, refineryHealth=200, refineryBuildSeconds=10, refineryCreditCost=200, refineryIncomePerPeriod=10, refineryFootprintRadius=1.35d, refineryModelHeightMeters=2.6d, refineryModelScale=.8d,
                arenaHalfExtent=32d, playerHeadquartersX=-21d, enemyHeadquartersX=21d, headquartersZ=0d, playerFactoryPadX=-15d, playerFactoryPadZ=-9d, playerRefineryPadX=-15d, playerRefineryPadZ=9d, enemyFactoryPadX=15d, enemyFactoryPadZ=9d, enemyRefineryPadX=15d, enemyRefineryPadZ=-9d, centralObstacleHalfWidth=3d, centralObstacleHalfDepth=24d, passageHalfWidth=4d, enemyAdvanceDelaySeconds=180d, attackApproachRangeRatio=.85d, attackRepathSeconds=.5d, factoryExitDistance=3d, defaultRallyDistance=7d, targetPickRadiusMultiplier=1.8d, buildingPickRadius=2.3d, navigationRequestsPerFrame=4, renderTargetFramesPerSecond=60, cameraOrthoSize=9.6d, cameraHeight=26d, cameraOffsetX=0d, cameraOffsetZ=-37d, cameraPanSpeed=12d, cameraMinZoom=2.135d, cameraMaxZoom=25.6d, cameraZoomSpeed=.013d, selectionDragPixels=6d
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
        public static readonly IReadOnlyList<PlayableProfileField> PreCrowdFields = new[] {
            new PlayableProfileField("turret.health","Турель · independent-defense-v1","Здоровье","Базовое HP пулемётной и ремонтной башен, независимо от танка.","HP",1,5000,1,p=>p.turretHealth,nameof(PlayableProfileData.turretHealth)),
            new PlayableProfileField("turret.range","Турель · independent-defense-v1","Дальность","Дальность пулемётной и ремонтной башен, независимо от Исследователя.","м",1,50,0.5,p=>p.turretRange,nameof(PlayableProfileData.turretRange)),
            new PlayableProfileField("turret.damage","Турель · independent-defense-v1","Урон пули","Урон одного попадания до общих оружейных исследований.","HP",0.1,100,0.1,p=>p.turretDamage,nameof(PlayableProfileData.turretDamage)),
            new PlayableProfileField("turret.burstSize","Турель · independent-defense-v1","Выстрелов в очереди","Размер собственной пулемётной очереди башни.","шт",1,30,1,p=>p.turretBurstSize,nameof(PlayableProfileData.turretBurstSize)),
            new PlayableProfileField("turret.shotIntervalMs","Турель · independent-defense-v1","Интервал выстрелов","Интервал внутри очереди.","мс",10,2000,10,p=>p.turretBurstShotIntervalMs,nameof(PlayableProfileData.turretBurstShotIntervalMs)),
            new PlayableProfileField("turret.burstPauseMs","Турель · independent-defense-v1","Пауза очереди","Пауза после последней пули.","мс",10,10000,10,p=>p.turretBurstPauseMs,nameof(PlayableProfileData.turretBurstPauseMs)),
            new PlayableProfileField("turret.spreadDeg","Турель · independent-defense-v1","Разброс","Угол собственного разброса пулемётной башни.","°",0,45,1,p=>p.turretSpreadDeg,nameof(PlayableProfileData.turretSpreadDeg)),
            new PlayableProfileField("turret.projectileSpeed","Турель · independent-defense-v1","Скорость пули","Скорость физических пуль башни.","м/с",1,200,1,p=>p.turretProjectileSpeed,nameof(PlayableProfileData.turretProjectileSpeed)),
            new PlayableProfileField("gauss.damage","Гаусс-башня · independent-defense-v1","Урон первой цели","Абсолютный урон до оружейных исследований; не зависит от HP танка.","HP",1,1000,1,p=>p.gaussDamage,nameof(PlayableProfileData.gaussDamage)),
            new PlayableProfileField("gauss.range","Гаусс-башня · independent-defense-v1","Дальность","Собственная дальность Гаусс-башни, независимо от других юнитов.","м",1,50,0.5,p=>p.gaussRange,nameof(PlayableProfileData.gaussRange)),
            new PlayableProfileField("gauss.chargeSeconds","Гаусс-башня · gauss-tower-v1","Зарядка","Длительность свечения перед следующим выстрелом","с",0.1,3,0.1,p=>p.gaussChargeSeconds,nameof(PlayableProfileData.gaussChargeSeconds)),
            new PlayableProfileField("gauss.chargeIntensity","Гаусс-башня · gauss-tower-v1","Свечение зарядки","Яркость накопления энергии","×",0.1,20,0.1,p=>p.gaussChargeIntensity,nameof(PlayableProfileData.gaussChargeIntensity)),
            new PlayableProfileField("gauss.recoilMeters","Гаусс-башня · gauss-tower-v1","Ход отдачи","Откат оружейной головки в координатах модели","м",0.01,0.2,0.01,p=>p.gaussRecoilMeters,nameof(PlayableProfileData.gaussRecoilMeters)),
            new PlayableProfileField("gauss.recoilSeconds","Гаусс-башня · gauss-tower-v1","Возврат после отдачи","Длительность отката и возвращения","с",0.1,1,0.01,p=>p.gaussRecoilSeconds,nameof(PlayableProfileData.gaussRecoilSeconds)),
            new PlayableProfileField("gauss.recoilPeakFraction","Гаусс-башня · gauss-tower-v1","Резкость отдачи","Доля времени до максимального отката","доля",0.05,0.5,0.01,p=>p.gaussRecoilPeakFraction,nameof(PlayableProfileData.gaussRecoilPeakFraction)),
            new PlayableProfileField("gauss.beamGlowWidth","Гаусс-башня · gauss-tower-v1","Ореол луча","Ширина ореола относительно ядра","×",1,12,0.1,p=>p.gaussBeamGlowWidth,nameof(PlayableProfileData.gaussBeamGlowWidth)),
            new PlayableProfileField("gauss.beamRiseFraction","Гаусс-башня · gauss-tower-v1","Нарастание луча","Доля времени импульса для набора яркости","доля",0.02,0.5,0.01,p=>p.gaussBeamRiseFraction,nameof(PlayableProfileData.gaussBeamRiseFraction)),
            new PlayableProfileField("gauss.costMultiplier","Гаусс-башня · gauss-tower-v1","Цена относительно турели","Множитель цены обычной турели","×",0.1,10,0.1,p=>p.gaussCostMultiplier,nameof(PlayableProfileData.gaussCostMultiplier)),
            new PlayableProfileField("gauss.healthMultiplier","Гаусс-башня · gauss-tower-v1","Здоровье относительно турели","Множитель HP обычной турели","×",0.1,10,0.1,p=>p.gaussHealthMultiplier,nameof(PlayableProfileData.gaussHealthMultiplier)),
            new PlayableProfileField("gauss.reloadSeconds","Гаусс-башня · gauss-tower-v1","Перезарядка","Пауза между выстрелами","с",0.1,30,0.1,p=>p.gaussReloadSeconds,nameof(PlayableProfileData.gaussReloadSeconds)),
            new PlayableProfileField("gauss.pierceRetention","Гаусс-башня · gauss-tower-v1","Урон после пробития","Доля предыдущего урона для следующей цели","доля",0.01,1,0.01,p=>p.gaussPierceRetention,nameof(PlayableProfileData.gaussPierceRetention)),
            new PlayableProfileField("gauss.beamRadius","Гаусс-башня · gauss-tower-v1","Радиус луча","Толщина поражающего луча","м",0.001,0.25,0.001,p=>p.gaussBeamRadius,nameof(PlayableProfileData.gaussBeamRadius)),
            new PlayableProfileField("gauss.beamSeconds","Гаусс-башня · gauss-tower-v1","Видимость импульса","Длительность белого следа выстрела","с",0.02,1,0.01,p=>p.gaussBeamSeconds,nameof(PlayableProfileData.gaussBeamSeconds)),
            new PlayableProfileField("gauss.beamIntensity","Гаусс-башня · gauss-tower-v1","Яркость луча","Интенсивность белого импульса","×",0.1,10,0.1,p=>p.gaussBeamIntensity,nameof(PlayableProfileData.gaussBeamIntensity)),
            new PlayableProfileField("repairTower.healthPerSecond","Ремонтная вышка · repair-tower-v1","Скорость ремонта","Восстановление HP одной собственной наземной или воздушной цели, в том числе под огнём.","HP/с",0.1,100,0.1,p=>p.repairTowerHealthPerSecond,nameof(PlayableProfileData.repairTowerHealthPerSecond)),
            new PlayableProfileField("turret.creditCost","Турель · sentry-turret-combat-v1","Цена","Цена постройки пулемётной турели.","кред.",1,5000,25,p=>p.turretCreditCost,nameof(PlayableProfileData.turretCreditCost)),
            new PlayableProfileField("turret.buildSeconds","Турель · sentry-turret-combat-v1","Строительство","Время строительства турели.","с",1,120,1,p=>p.turretBuildSeconds,nameof(PlayableProfileData.turretBuildSeconds)),
            new PlayableProfileField("turret.passageWidth","Турель · sentry-turret-combat-v1","Ширина проездов","Кратчайший свободный зазор между квадратными основаниями турели и обычного здания; проверяется на полностью застроенной базе.","м",1.5,5,0.1,p=>p.turretPassageWidth,nameof(PlayableProfileData.turretPassageWidth)),
            new PlayableProfileField("turret.padOffset","Турель · sentry-turret-combat-v1","Вынос оборонительных площадок","Минимальный дополнительный радиус общего кольца относительно обычных зданий; при увеличенных зазорах всё кольцо расширяется совместно, сохраняя проходы и выходы заводов.","м",1,5,0.1,p=>p.turretPadOffset,nameof(PlayableProfileData.turretPadOffset)),
            new PlayableProfileField("turret.footprintRadius","Турель · sentry-turret-combat-v1","Радиус опоры","Половина ширины твёрдой опоры; бетонный настил декоративный.","м",0.4,1,0.01,p=>p.turretFootprintRadius,nameof(PlayableProfileData.turretFootprintRadius)),
            new PlayableProfileField("turret.vision","Турель · sentry-turret-combat-v1","Обзор","Радиус обзора готовой турели с обычными ограничениями fog.","м",1,40,1,p=>p.turretVision,nameof(PlayableProfileData.turretVision)),
            new PlayableProfileField("turret.modelScale","Турель · sentry-turret-combat-v1","Масштаб модели","Масштаб утверждённой модели 3 и авторских узлов.","×",0.5,1.5,0.05,p=>p.turretModelScale,nameof(PlayableProfileData.turretModelScale)),
            new PlayableProfileField("turret.yawSpeed","Турель · sentry-turret-combat-v1","Поворот","Предельная скорость поворота оружия по азимуту.","°/с",30,1440,30,p=>p.turretYawSpeed,nameof(PlayableProfileData.turretYawSpeed)),
            new PlayableProfileField("turret.pitchSpeed","Турель · sentry-turret-combat-v1","Подъём","Предельная скорость подъёма до вертикали.","°/с",30,720,15,p=>p.turretPitchSpeed,nameof(PlayableProfileData.turretPitchSpeed)),
            new PlayableProfileField("turret.depression","Турель · sentry-turret-combat-v1","Склонение","Максимальное склонение для близкой наземной цели.","°",0,45,1,p=>p.turretDepression,nameof(PlayableProfileData.turretDepression)),
            new PlayableProfileField("turret.turningSpread","Турель · sentry-turret-combat-v1","Разброс в повороте","Множитель разброса при движении оружия по любой оси.","×",1,2,0.05,p=>p.turretTurningSpread,nameof(PlayableProfileData.turretTurningSpread)),
            new PlayableProfileField("turret.laserWidth","Турель · sentry-turret-combat-v1","Толщина лазера","Видимая толщина постоянного красного луча наведения.","м",0.005,0.1,0.005,p=>p.turretLaserWidth,nameof(PlayableProfileData.turretLaserWidth)),
            new PlayableProfileField("turret.laserIntensity","Турель · sentry-turret-combat-v1","Яркость лазера","Яркость красного луча; луч не наносит урон.","×",0.1,4,0.1,p=>p.turretLaserIntensity,nameof(PlayableProfileData.turretLaserIntensity)),

            new PlayableProfileField("territory.constructionInitialHealthRatio","Здания · building-collapse-a-v1","Начальное здоровье фундамента","Доля максимального HP при материализации; остаток строится за прежнее время.","доля",0,1,0.01,p=>p.constructionInitialHealthRatio,nameof(PlayableProfileData.constructionInitialHealthRatio)),
            new PlayableProfileField("territory.buildingDestructionSeconds","Здания · building-collapse-a-v1","Длительность разрушения","Время проседания и затухания дыма; до конца площадка занята.","с",0.5,8,0.1,p=>p.buildingDestructionSeconds,nameof(PlayableProfileData.buildingDestructionSeconds)),
            new PlayableProfileField("presentation.buildingDestructionShake","Здания · building-collapse-a-v1","Дрожь здания","Амплитуда локальной дрожи корпуса без движения камеры.","м",0,0.2,0.005,p=>p.buildingDestructionShake,nameof(PlayableProfileData.buildingDestructionShake)),
            new PlayableProfileField("presentation.buildingDestructionTilt","Здания · building-collapse-a-v1","Наклон при разрушении","Небольшой колеблющийся наклон погибшего корпуса.","град",0,5,0.1,p=>p.buildingDestructionTilt,nameof(PlayableProfileData.buildingDestructionTilt)),
            new PlayableProfileField("presentation.buildingDestructionFireScale","Здания · building-collapse-a-v1","Размер пламени","Размер локального пламени относительно габаритов здания.","доля",0.1,1.5,0.05,p=>p.buildingDestructionFireScale,nameof(PlayableProfileData.buildingDestructionFireScale)),
            new PlayableProfileField("presentation.buildingDestructionSmokeOpacity","Здания · building-collapse-a-v1","Дым разрушения","Непрозрачность дыма и пыли вокруг оседающего корпуса.","доля",0,0.8,0.01,p=>p.buildingDestructionSmokeOpacity,nameof(PlayableProfileData.buildingDestructionSmokeOpacity)),

            new PlayableProfileField("presentation.notifications.bottomPixels","Уведомления · match-events-v1","Отступ от края","Горизонтальный отступ строки справа от рамки мини-карты.","px",0,40,1,p=>p.notificationBottomPixels,nameof(PlayableProfileData.notificationBottomPixels)),
            new PlayableProfileField("presentation.notifications.seconds","Уведомления · match-events-v1","Время строки","Срок единственной строки последнего события, максимум три секунды.","s",0.5,3,0.5,p=>p.notificationSeconds,nameof(PlayableProfileData.notificationSeconds)),
            new PlayableProfileField("presentation.notifications.historySeconds","Уведомления · match-events-v1","История переходов","Срок доступности события для Space и крестовины, максимум пять секунд.","s",1,5,0.5,p=>p.notificationHistorySeconds,nameof(PlayableProfileData.notificationHistorySeconds)),
            new PlayableProfileField("presentation.notifications.frontRadius","Уведомления · match-events-v1","Радиус боя","Близкие попадания продолжают один бой без новых предупреждений.","m",1,100,1,p=>p.notificationFrontRadius,nameof(PlayableProfileData.notificationFrontRadius)),
            new PlayableProfileField("presentation.notifications.frontIdleSeconds","Уведомления · match-events-v1","Затихание боя","Без урона дольше этого срока следующее попадание считается новым боем.","s",1,60,1,p=>p.notificationFrontIdleSeconds,nameof(PlayableProfileData.notificationFrontIdleSeconds)),
            new PlayableProfileField("presentation.aiChat.authorSeconds","Сообщения и сигналы ИИ","Интервал автора","Минимальный игровой интервал обычных сообщений одного ИИ.","s",0,120,1,p=>p.aiChatAuthorSeconds,nameof(PlayableProfileData.aiChatAuthorSeconds)),
            new PlayableProfileField("presentation.aiChat.teamSeconds","Сообщения и сигналы ИИ","Интервал команды","Минимальный игровой интервал обычных сообщений одной команды.","s",0,60,1,p=>p.aiChatTeamSeconds,nameof(PlayableProfileData.aiChatTeamSeconds)),
            new PlayableProfileField("presentation.aiChat.criticalSeconds","Сообщения и сигналы ИИ","Срочные сообщения","Минимальный игровой интервал срочных сообщений команды.","s",0,60,1,p=>p.aiChatCriticalSeconds,nameof(PlayableProfileData.aiChatCriticalSeconds)),
            new PlayableProfileField("presentation.aiChat.maxAgeSeconds","Сообщения и сигналы ИИ","Актуальность доставки","Слишком старые AI-события не появляются после задержки интерфейса.","s",1,30,1,p=>p.aiChatMaxAgeSeconds,nameof(PlayableProfileData.aiChatMaxAgeSeconds)),
            new PlayableProfileField("presentation.aiChat.observerLimit","Сообщения и сигналы ИИ","Поток зрителя","Максимум обычных AI-реплик за время чтения в общей зрительской ленте.","count",1,10,1,p=>p.aiChatObserverLimit,nameof(PlayableProfileData.aiChatObserverLimit)),
            new PlayableProfileField("presentation.chat.outlineWidth","Чат","Обводка текста","Толщина тёмной обводки ленты без фоновых плашек.","px",0,1,.05,p=>p.chatOutlineWidth,nameof(PlayableProfileData.chatOutlineWidth)),
            new PlayableProfileField("presentation.chat.shadowOffset","Чат","Смещение тени","Вертикальное смещение тени для чтения текста на карте.","px",0,4,.5,p=>p.chatShadowOffset,nameof(PlayableProfileData.chatShadowOffset)),
            new PlayableProfileField("presentation.chat.shadowBlur","Чат","Размытие тени","Радиус тёмной тени сообщений на фоне игры.","px",0,6,.5,p=>p.chatShadowBlur,nameof(PlayableProfileData.chatShadowBlur)),
            new PlayableProfileField("presentation.chat.leftPercent","Чат","Отступ слева","Начало блока чата в процентах ширины своего viewport.","%",0,35,1,p=>p.chatLeftPercent,nameof(PlayableProfileData.chatLeftPercent)),
            new PlayableProfileField("presentation.chat.visibleLimit","Чат","Видимых сообщений","Максимум читаемых сообщений в каждом локальном окне.","count",1,10,1,p=>p.chatVisibleLimit,nameof(PlayableProfileData.chatVisibleLimit)),
            new PlayableProfileField("presentation.chat.readableSeconds","Чат","Время чтения","Полная видимость до начала затухания; вне игрового времени.","s",1,60,1,p=>p.chatReadableSeconds,nameof(PlayableProfileData.chatReadableSeconds)),
            new PlayableProfileField("presentation.chat.fadeSeconds","Чат","Затухание","Продолжительность плавного исчезновения строки.","s",.1,10,.1,p=>p.chatFadeSeconds,nameof(PlayableProfileData.chatFadeSeconds)),
            new PlayableProfileField("presentation.chat.evictionSeconds","Чат","Вытеснение","Затухание старой строки при заполнении ленты.","s",.1,2,.05,p=>p.chatEvictionSeconds,nameof(PlayableProfileData.chatEvictionSeconds)),
            new PlayableProfileField("presentation.healthBar.width","Интерфейс · Здоровье","Длина","Общая длина полоски юнитов и зданий.","m",0.5,4,0.05,p=>p.healthBarWidth,nameof(PlayableProfileData.healthBarWidth)),
            new PlayableProfileField("presentation.healthBar.height","Интерфейс · Здоровье","Толщина","Толщина заполнения полоски.","m",0.025,0.3,0.005,p=>p.healthBarHeight,nameof(PlayableProfileData.healthBarHeight)),
            new PlayableProfileField("presentation.healthBar.modelGap","Интерфейс · Здоровье","Отступ над моделью","Расстояние от верха модели до нижнего края полоски.","m",0,2,0.05,p=>p.healthBarModelGap,nameof(PlayableProfileData.healthBarModelGap)),
            new PlayableProfileField("presentation.healthBar.cellGap","Интерфейс · Здоровье","Ширина разделителя","Тёмный промежуток между визуальными ячейками.","m",0.001,0.04,0.001,p=>p.healthBarCellGap,nameof(PlayableProfileData.healthBarCellGap)),
            new PlayableProfileField("presentation.healthBar.cells","Интерфейс · Здоровье","Число ячеек","Визуальные ячейки; здоровье заполняется непрерывно.","count",2,20,1,p=>p.healthBarCells,nameof(PlayableProfileData.healthBarCells)),
            new PlayableProfileField("presentation.healthBar.outlineWidth","Интерфейс · Здоровье","Толщина чёрного контура","Внешний чёрный контур полоски здоровья для чтения на светлом и тёмном фоне.","m",.005,.15,.005,p=>p.healthBarOutlineWidth,nameof(PlayableProfileData.healthBarOutlineWidth)),
            new PlayableProfileField("presentation.buildingSelection.padding","Интерфейс · Выбор здания","Запас рамки здания","Расстояние от углов нижнего основания до зелёной окружности выбора; не меняет коллизии.","m",.1,2,.05,p=>p.buildingSelectionPadding,nameof(PlayableProfileData.buildingSelectionPadding)),
            new PlayableProfileField("presentation.towerRange.fillOpacity","Интерфейс · Радиус башни","Непрозрачность заливки","Сила полупрозрачной зоны действия.","ratio",0,0.4,0.01,p=>p.towerRangeFillOpacity,nameof(PlayableProfileData.towerRangeFillOpacity)),
            new PlayableProfileField("presentation.towerRange.borderOpacity","Интерфейс · Радиус башни","Непрозрачность границы","Яркость тонкой внешней границы.","ratio",0,1,0.05,p=>p.towerRangeBorderOpacity,nameof(PlayableProfileData.towerRangeBorderOpacity)),
            new PlayableProfileField("presentation.towerRange.borderWidth","Интерфейс · Радиус башни","Толщина границы","Толщина границы на поверхности карты.","m",0.01,0.5,0.01,p=>p.towerRangeBorderWidth,nameof(PlayableProfileData.towerRangeBorderWidth)),
            new PlayableProfileField("presentation.towerRange.edgeFeather","Интерфейс · Радиус башни","Смягчение края","Ширина плавного затухания заливки у края.","m",0.01,1,0.01,p=>p.towerRangeEdgeFeather,nameof(PlayableProfileData.towerRangeEdgeFeather)),
            new PlayableProfileField("presentation.towerRange.lift","Интерфейс · Радиус башни","Отступ от земли","Высота визуальной зоны над поверхностью против мерцания.","m",0.005,0.2,0.005,p=>p.towerRangeLift,nameof(PlayableProfileData.towerRangeLift)),
            new PlayableProfileField("presentation.towerRange.innerFade","Интерфейс · Радиус башни","Заливка в центре","Доля непрозрачности заливки в центре относительно края.","ratio",0,1,0.05,p=>p.towerRangeInnerFade,nameof(PlayableProfileData.towerRangeInnerFade)),
            new PlayableProfileField("presentation.towerRange.repairHue","Интерфейс · Радиус башни","Оттенок ремонта","Цвет зоны ремонтной вышки.","hue",0,1,0.01,p=>p.towerRangeRepairHue,nameof(PlayableProfileData.towerRangeRepairHue)),
            new PlayableProfileField("presentation.towerRange.combatHue","Интерфейс · Радиус башни","Оттенок оружия","Цвет зоны боевой башни.","hue",0,1,0.01,p=>p.towerRangeCombatHue,nameof(PlayableProfileData.towerRangeCombatHue)),
            new PlayableProfileField("presentation.towerRange.saturation","Интерфейс · Радиус башни","Насыщенность","Насыщенность цвета зоны.","ratio",0,1,0.05,p=>p.towerRangeSaturation,nameof(PlayableProfileData.towerRangeSaturation)),
            new PlayableProfileField("presentation.towerRange.brightness","Интерфейс · Радиус башни","Яркость","Яркость цвета зоны.","ratio",0,1,0.05,p=>p.towerRangeBrightness,nameof(PlayableProfileData.towerRangeBrightness)),
            new PlayableProfileField("presentation.buildingSelection.width","Интерфейс · Выбор здания","Толщина рамки здания","Толщина зелёной окружности выбора здания на земле.","m",.03,.3,.01,p=>p.buildingSelectionWidth,nameof(PlayableProfileData.buildingSelectionWidth)),
            new PlayableProfileField("presentation.groundTrails.lifetimeSeconds","Следы техники","Время жизни следов","Время до исчезновения отпечатка в игровых секундах. Пауза останавливает отсчёт. Полное выключение — в настройках графики.","s",1,60,1,p=>p.groundTrailLifetimeSeconds,nameof(PlayableProfileData.groundTrailLifetimeSeconds)),
            new PlayableProfileField("presentation.selectionFrame.red","Интерфейс · Выделение","Красный","Красная компонента цвета рамки.","ratio",0,1,0.01,p=>p.selectionFrameRed,nameof(PlayableProfileData.selectionFrameRed)),
            new PlayableProfileField("presentation.selectionFrame.green","Интерфейс · Выделение","Зелёный","Зелёная компонента цвета рамки.","ratio",0,1,0.01,p=>p.selectionFrameGreen,nameof(PlayableProfileData.selectionFrameGreen)),
            new PlayableProfileField("presentation.selectionFrame.blue","Интерфейс · Выделение","Синий","Синяя компонента цвета рамки.","ratio",0,1,0.01,p=>p.selectionFrameBlue,nameof(PlayableProfileData.selectionFrameBlue)),
            new PlayableProfileField("presentation.selectionFrame.lineWidth","Интерфейс · Выделение","Толщина линии","Толщина тонкой рамки выделения.","px",0.5,4,0.5,p=>p.selectionFrameLineWidth,nameof(PlayableProfileData.selectionFrameLineWidth)),
            new PlayableProfileField("presentation.selectionFrame.cornerWidth","Интерфейс · Выделение","Толщина уголков","Толщина акцентированных уголков.","px",1,6,0.5,p=>p.selectionFrameCornerWidth,nameof(PlayableProfileData.selectionFrameCornerWidth)),
            new PlayableProfileField("presentation.selectionFrame.cornerLength","Интерфейс · Выделение","Длина уголков","Длина акцента вдоль края рамки.","px",4,40,1,p=>p.selectionFrameCornerLength,nameof(PlayableProfileData.selectionFrameCornerLength)),
            new PlayableProfileField("presentation.selectionFrame.fillOpacity","Интерфейс · Выделение","Заливка","Непрозрачность зелёной заливки внутри рамки.","ratio",0,0.3,0.01,p=>p.selectionFrameFillOpacity,nameof(PlayableProfileData.selectionFrameFillOpacity)),
            new PlayableProfileField("presentation.selectionFrame.lineOpacity","Интерфейс · Выделение","Контур","Непрозрачность тонкой линии между уголками.","ratio",0.1,1,0.05,p=>p.selectionFrameLineOpacity,nameof(PlayableProfileData.selectionFrameLineOpacity)),
            new PlayableProfileField("presentation.selectionRing.padding","Selection ring","Ring padding","Space between model footprint and selection circle.","m",0,1,.01,p=>p.selectionRingPadding,nameof(PlayableProfileData.selectionRingPadding)),
            new PlayableProfileField("presentation.selectionRing.width","Selection ring","Ring width","Width of the green ground circle.","m",.01,.2,.005,p=>p.selectionRingWidth,nameof(PlayableProfileData.selectionRingWidth)),
            new PlayableProfileField("presentation.selectionRing.lift","Selection ring","Surface lift","Avoids depth flicker against the terrain.","m",.01,.15,.005,p=>p.selectionRingLift,nameof(PlayableProfileData.selectionRingLift)),
            new PlayableProfileField("presentation.selectionRing.hawkRadius","Selection ring","Радиус круга Ястреба","Размер круга выбранного вертолёта без общей окантовки; не меняет габариты или выбор юнита.","m",.1,3,.05,p=>p.hawkSelectionRadius,nameof(PlayableProfileData.hawkSelectionRadius)),
            new PlayableProfileField("hangar.hangarHealth","Здания · Ангар","Здоровье","Максимальное здоровье ангара.","HP",1,10000,1,p=>p.hangarHealth,nameof(PlayableProfileData.hangarHealth)),
            new PlayableProfileField("hangar.hangarBuildSeconds","Здания · Ангар","Время строительства","Длительность строительства ангара.","s",1,300,1,p=>p.hangarBuildSeconds,nameof(PlayableProfileData.hangarBuildSeconds)),
            new PlayableProfileField("hangar.hangarCreditCost","Здания · Ангар","Цена","Стоимость заказа ангара.","credits",0,100000,1,p=>p.hangarCreditCost,nameof(PlayableProfileData.hangarCreditCost)),
            new PlayableProfileField("hangar.hangarVisionRange","Здания · Ангар","Обзор","Радиус обзора готового ангара.","m",1,50,1,p=>p.hangarVisionRange,nameof(PlayableProfileData.hangarVisionRange)),
            new PlayableProfileField("hangar.hangarFootprintRadius","Здания · Ангар","Радиус основания","Полуразмер непроходимого основания ангара.","m",0.1,20,0.01,p=>p.hangarFootprintRadius,nameof(PlayableProfileData.hangarFootprintRadius)),
            new PlayableProfileField("hangar.hangarModelHeightMeters","Здания · Ангар","Высота модели","Высота модели до относительного масштабирования.","m",0.1,20,0.1,p=>p.hangarModelHeightMeters,nameof(PlayableProfileData.hangarModelHeightMeters)),
            new PlayableProfileField("hangar.hangarModelScale","Здания · Ангар","Масштаб модели","Визуальный масштаб без изменения коллизии.","ratio",0.25,3,0.05,p=>p.hangarModelScale,nameof(PlayableProfileData.hangarModelScale)),
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
            new PlayableProfileField("research.weaponSystems.level3.cost","Исследования / Оружейные системы","Цена · уровень 3","Кредиты, списываемые при старте заказа.","кредиты",0,10000,1,p=>p.weaponSystemsLevel3Cost,nameof(PlayableProfileData.weaponSystemsLevel3Cost)),
            new PlayableProfileField("research.weaponSystems.level3.seconds","Исследования / Оружейные системы","Длительность · уровень 3","Время активного заказа в секундах симуляции.","с",0.5,300,0.5,p=>p.weaponSystemsLevel3Seconds,nameof(PlayableProfileData.weaponSystemsLevel3Seconds)),
            new PlayableProfileField("research.weaponSystems.level3.bonusPercent","Исследования / Оружейные системы","Суммарный бонус · уровень 3","Суммарное усиление относительно базы; заменяет предыдущий уровень.","%",0,300,1,p=>p.weaponSystemsLevel3BonusPercent,nameof(PlayableProfileData.weaponSystemsLevel3BonusPercent)),
            new PlayableProfileField("research.defensiveSystems.level3.cost","Исследования / Защитные системы","Цена · уровень 3","Кредиты, списываемые при старте заказа.","кредиты",0,10000,1,p=>p.defensiveSystemsLevel3Cost,nameof(PlayableProfileData.defensiveSystemsLevel3Cost)),
            new PlayableProfileField("research.defensiveSystems.level3.seconds","Исследования / Защитные системы","Длительность · уровень 3","Время активного заказа в секундах симуляции.","с",0.5,300,0.5,p=>p.defensiveSystemsLevel3Seconds,nameof(PlayableProfileData.defensiveSystemsLevel3Seconds)),
            new PlayableProfileField("research.defensiveSystems.level3.bonusPercent","Исследования / Защитные системы","Суммарный бонус · уровень 3","Суммарное усиление относительно базы; заменяет предыдущий уровень.","%",0,300,1,p=>p.defensiveSystemsLevel3BonusPercent,nameof(PlayableProfileData.defensiveSystemsLevel3BonusPercent)),
            new PlayableProfileField("factory.upgradeCost","Здания · Фабрика · Улучшение","Цена","Отдельный параметр улучшения; не меняет строительство.","credits",0,100000,1,p=>p.factoryUpgradeCost,nameof(PlayableProfileData.factoryUpgradeCost)),
            new PlayableProfileField("factory.upgradeSeconds","Здания · Фабрика · Улучшение","Время","Отдельный параметр улучшения; не меняет строительство.","s",1,300,1,p=>p.factoryUpgradeSeconds,nameof(PlayableProfileData.factoryUpgradeSeconds)),
            new PlayableProfileField("hangar.upgradeCost","Здания · Ангар · Улучшение","Цена","Отдельный параметр улучшения; не меняет строительство.","credits",0,100000,1,p=>p.hangarUpgradeCost,nameof(PlayableProfileData.hangarUpgradeCost)),
            new PlayableProfileField("hangar.upgradeSeconds","Здания · Ангар · Улучшение","Время","Отдельный параметр улучшения; не меняет строительство.","s",1,300,1,p=>p.hangarUpgradeSeconds,nameof(PlayableProfileData.hangarUpgradeSeconds)),
            new PlayableProfileField("production.upgradedProducerSpeedMultiplier","Производство · Улучшение","Множитель скорости","Время производства делится на множитель; выпуск не ускоряется.","×",1,10,0.1,p=>p.upgradedProducerSpeedMultiplier,nameof(PlayableProfileData.upgradedProducerSpeedMultiplier)),
            new PlayableProfileField("research.centerUpgrade.cost","Исследования / Улучшение научного центра","Цена","Кредиты, списываемые при старте заказа.","кредиты",0,10000,1,p=>p.scientificCenterUpgradeCost,nameof(PlayableProfileData.scientificCenterUpgradeCost)),
            new PlayableProfileField("research.centerUpgrade.seconds","Исследования / Улучшение научного центра","Длительность","Время активного заказа в секундах симуляции.","с",0.5,300,0.5,p=>p.scientificCenterUpgradeSeconds,nameof(PlayableProfileData.scientificCenterUpgradeSeconds)),
            new PlayableProfileField("research.weaponSystems.level1.cost","Исследования / Оружейные системы","Цена · уровень 1","Кредиты, списываемые при старте исследования.","кредиты",0,10000,1,p=>p.weaponSystemsLevel1Cost,nameof(PlayableProfileData.weaponSystemsLevel1Cost)),
            new PlayableProfileField("research.weaponSystems.level1.seconds","Исследования / Оружейные системы","Длительность · уровень 1","Длительность активного исследования в секундах симуляции.","с",0.5,300,0.5,p=>p.weaponSystemsLevel1Seconds,nameof(PlayableProfileData.weaponSystemsLevel1Seconds)),
            new PlayableProfileField("research.weaponSystems.level1.bonusPercent","Исследования / Оружейные системы","Суммарный бонус · уровень 1","Суммарное усиление относительно базового значения; второй уровень заменяет бонус первого.","%",0,300,1,p=>p.weaponSystemsLevel1BonusPercent,nameof(PlayableProfileData.weaponSystemsLevel1BonusPercent)),
            new PlayableProfileField("research.weaponSystems.level2.cost","Исследования / Оружейные системы","Цена · уровень 2","Кредиты, списываемые при старте исследования.","кредиты",0,10000,1,p=>p.weaponSystemsLevel2Cost,nameof(PlayableProfileData.weaponSystemsLevel2Cost)),
            new PlayableProfileField("research.weaponSystems.level2.seconds","Исследования / Оружейные системы","Длительность · уровень 2","Длительность активного исследования в секундах симуляции.","с",0.5,300,0.5,p=>p.weaponSystemsLevel2Seconds,nameof(PlayableProfileData.weaponSystemsLevel2Seconds)),
            new PlayableProfileField("research.weaponSystems.level2.bonusPercent","Исследования / Оружейные системы","Суммарный бонус · уровень 2","Суммарное усиление относительно базового значения; второй уровень заменяет бонус первого.","%",0,300,1,p=>p.weaponSystemsLevel2BonusPercent,nameof(PlayableProfileData.weaponSystemsLevel2BonusPercent)),
            new PlayableProfileField("research.defensiveSystems.level1.cost","Исследования / Защитные системы","Цена · уровень 1","Кредиты, списываемые при старте исследования.","кредиты",0,10000,1,p=>p.defensiveSystemsLevel1Cost,nameof(PlayableProfileData.defensiveSystemsLevel1Cost)),
            new PlayableProfileField("research.defensiveSystems.level1.seconds","Исследования / Защитные системы","Длительность · уровень 1","Длительность активного исследования в секундах симуляции.","с",0.5,300,0.5,p=>p.defensiveSystemsLevel1Seconds,nameof(PlayableProfileData.defensiveSystemsLevel1Seconds)),
            new PlayableProfileField("research.defensiveSystems.level1.bonusPercent","Исследования / Защитные системы","Суммарный бонус · уровень 1","Суммарное усиление относительно базового значения; второй уровень заменяет бонус первого.","%",0,300,1,p=>p.defensiveSystemsLevel1BonusPercent,nameof(PlayableProfileData.defensiveSystemsLevel1BonusPercent)),
            new PlayableProfileField("research.defensiveSystems.level2.cost","Исследования / Защитные системы","Цена · уровень 2","Кредиты, списываемые при старте исследования.","кредиты",0,10000,1,p=>p.defensiveSystemsLevel2Cost,nameof(PlayableProfileData.defensiveSystemsLevel2Cost)),
            new PlayableProfileField("research.defensiveSystems.level2.seconds","Исследования / Защитные системы","Длительность · уровень 2","Длительность активного исследования в секундах симуляции.","с",0.5,300,0.5,p=>p.defensiveSystemsLevel2Seconds,nameof(PlayableProfileData.defensiveSystemsLevel2Seconds)),
            new PlayableProfileField("research.defensiveSystems.level2.bonusPercent","Исследования / Защитные системы","Суммарный бонус · уровень 2","Суммарное усиление относительно базового значения; второй уровень заменяет бонус первого.","%",0,300,1,p=>p.defensiveSystemsLevel2BonusPercent,nameof(PlayableProfileData.defensiveSystemsLevel2BonusPercent)),
            new PlayableProfileField("ai.research.armySystems.recruit.delaySeconds","ИИ / Системы армии","Новичок · Уровень I: не раньше","Минимальное время матча для первого уровня обеих веток. Наблюдение у врага обходит срок с учётом задержки реакции; новичок не реагирует.","с",0,1800,30,p=>p.aiArmySystemsRecruitDelaySeconds,nameof(PlayableProfileData.aiArmySystemsRecruitDelaySeconds)),
            new PlayableProfileField("ai.research.armySystems.recruit.level2DelaySeconds","ИИ / Системы армии","Новичок · Уровень II: не раньше","Минимальное время матча для второго уровня обеих веток. Наблюдение у врага обходит срок с учётом задержки реакции; новичок не реагирует.","с",0,1800,30,p=>p.aiArmySystemsRecruitLevel2DelaySeconds,nameof(PlayableProfileData.aiArmySystemsRecruitLevel2DelaySeconds)),
            new PlayableProfileField("ai.research.armySystems.recruit.utilityScale","ИИ / Системы армии","Новичок · Ценность систем армии","Множитель полезности оружейной и защитной веток.","вес",0.05,10,0.05,p=>p.aiArmySystemsRecruitUtilityScale,nameof(PlayableProfileData.aiArmySystemsRecruitUtilityScale)),
            new PlayableProfileField("ai.research.armySystems.fighter.delaySeconds","ИИ / Системы армии","Боец · Уровень I: не раньше","Минимальное время матча для первого уровня обеих веток. Наблюдение у врага обходит срок с учётом задержки реакции; новичок не реагирует.","с",0,1800,30,p=>p.aiArmySystemsFighterDelaySeconds,nameof(PlayableProfileData.aiArmySystemsFighterDelaySeconds)),
            new PlayableProfileField("ai.research.armySystems.fighter.level2DelaySeconds","ИИ / Системы армии","Боец · Уровень II: не раньше","Минимальное время матча для второго уровня обеих веток. Наблюдение у врага обходит срок с учётом задержки реакции; новичок не реагирует.","с",0,1800,30,p=>p.aiArmySystemsFighterLevel2DelaySeconds,nameof(PlayableProfileData.aiArmySystemsFighterLevel2DelaySeconds)),
            new PlayableProfileField("ai.research.armySystems.fighter.utilityScale","ИИ / Системы армии","Боец · Ценность систем армии","Множитель полезности оружейной и защитной веток.","вес",0.05,10,0.05,p=>p.aiArmySystemsFighterUtilityScale,nameof(PlayableProfileData.aiArmySystemsFighterUtilityScale)),
            new PlayableProfileField("ai.research.armySystems.veteran.delaySeconds","ИИ / Системы армии","Ветеран · Уровень I: не раньше","Минимальное время матча для первого уровня обеих веток. Наблюдение у врага обходит срок с учётом задержки реакции; новичок не реагирует.","с",0,1800,30,p=>p.aiArmySystemsVeteranDelaySeconds,nameof(PlayableProfileData.aiArmySystemsVeteranDelaySeconds)),
            new PlayableProfileField("ai.research.armySystems.veteran.level2DelaySeconds","ИИ / Системы армии","Ветеран · Уровень II: не раньше","Минимальное время матча для второго уровня обеих веток. Наблюдение у врага обходит срок с учётом задержки реакции; новичок не реагирует.","с",0,1800,30,p=>p.aiArmySystemsVeteranLevel2DelaySeconds,nameof(PlayableProfileData.aiArmySystemsVeteranLevel2DelaySeconds)),
            new PlayableProfileField("ai.research.armySystems.veteran.utilityScale","ИИ / Системы армии","Ветеран · Ценность систем армии","Множитель полезности оружейной и защитной веток.","вес",0.05,10,0.05,p=>p.aiArmySystemsVeteranUtilityScale,nameof(PlayableProfileData.aiArmySystemsVeteranUtilityScale)),
            new PlayableProfileField("ai.research.armySystems.fighter.catchupSeconds","ИИ / Системы армии","Боец · Реакция на отставание","Игровых секунд с первого наблюдения вражеского уровня до приоритетного накопления. Новичок не учитывает вражеские апгрейды.","с",0,300,1,p=>p.aiArmySystemsFighterCatchupSeconds,nameof(PlayableProfileData.aiArmySystemsFighterCatchupSeconds)),
            new PlayableProfileField("ai.research.armySystems.veteran.catchupSeconds","ИИ / Системы армии","Ветеран · Реакция на отставание","Игровых секунд с первого наблюдения вражеского уровня до приоритетного накопления. Новичок не учитывает вражеские апгрейды.","с",0,300,1,p=>p.aiArmySystemsVeteranCatchupSeconds,nameof(PlayableProfileData.aiArmySystemsVeteranCatchupSeconds)),
            new PlayableProfileField("ai.research.armySystemsMinimumUnits","Исследования / ИИ / Исследования","Минимум юнитов для оружия и защиты","Живые собственные юниты поштучно, включая Исследователей. Ограничивает только новые исследования ИИ в двух глобальных ветках.","юниты",0,1000,1,p=>p.armySystemsMinimumUnits,nameof(PlayableProfileData.armySystemsMinimumUnits)),
            new PlayableProfileField("research.tankChassisCost","Research / tank chassis","Chassis price","Credits charged when chassis research starts","credits",0,5000,10,p=>p.tankChassisCost,nameof(PlayableProfileData.tankChassisCost)),
            new PlayableProfileField("research.tankChassisSeconds","Research / tank chassis","Chassis duration","Active simulation time for chassis research","s",0.5,300,0.5,p=>p.tankChassisSeconds,nameof(PlayableProfileData.tankChassisSeconds)),
            new PlayableProfileField("combatRounding.maximumHealthStep","Бой / Округление","Шаг максимального здоровья","Итоговое максимальное здоровье после всех улучшений округляется к ближайшему шагу; положительный минимум равен шагу.","HP",1,50,1,p=>p.maximumHealthRoundingStep,nameof(PlayableProfileData.maximumHealthRoundingStep)),
            new PlayableProfileField("combatRounding.damageStep","Бой / Округление","Шаг урона","Урон оружия после всех улучшений округляется к ближайшему шагу при выстреле.","HP",0.01,1,0.01,p=>p.damageRoundingStep,nameof(PlayableProfileData.damageRoundingStep)),
            new PlayableProfileField("research.tankChassisHealthBonusPercent","Research / tank chassis","Chassis health bonus","Maximum tank health bonus after chassis; multiplies defensive systems, preserves health fraction","%",0,200,1,p=>p.tankChassisHealthBonusPercent,nameof(PlayableProfileData.tankChassisHealthBonusPercent)),
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
            new PlayableProfileField("teamSignals.lifetimeSeconds","Командные сигналы (team-map-signals-v1)","Срок жизни","Срок жизни; параметры отображения и времени командных сигналов.","s",1,20,0.1,p=>p.teamSignalLifetimeSeconds,nameof(PlayableProfileData.teamSignalLifetimeSeconds)),
            new PlayableProfileField("teamSignals.maximumPerAuthor","Командные сигналы (team-map-signals-v1)","Сигналов на игрока","Сигналов на игрока; параметры отображения и времени командных сигналов.","count",1,8,1,p=>p.teamSignalMaximumPerAuthor,nameof(PlayableProfileData.teamSignalMaximumPerAuthor)),
            new PlayableProfileField("teamSignals.pulseSeconds","Командные сигналы (team-map-signals-v1)","Период мигания","Период мигания; параметры отображения и времени командных сигналов.","s",0.5,4,0.1,p=>p.teamSignalPulseSeconds,nameof(PlayableProfileData.teamSignalPulseSeconds)),
            new PlayableProfileField("teamSignals.fadeSeconds","Командные сигналы (team-map-signals-v1)","Затухание","Затухание; параметры отображения и времени командных сигналов.","s",0.1,1,0.1,p=>p.teamSignalFadeSeconds,nameof(PlayableProfileData.teamSignalFadeSeconds)),
            new PlayableProfileField("teamSignals.minimumAlpha","Командные сигналы (team-map-signals-v1)","Минимальная яркость","Минимальная яркость; параметры отображения и времени командных сигналов.","ratio",0.1,0.8,0.05,p=>p.teamSignalMinimumAlpha,nameof(PlayableProfileData.teamSignalMinimumAlpha)),
            new PlayableProfileField("teamSignals.glyphPixels","Командные сигналы (team-map-signals-v1)","Значок на поле","Значок на поле; параметры отображения и времени командных сигналов.","px",12,48,1,p=>p.teamSignalGlyphPixels,nameof(PlayableProfileData.teamSignalGlyphPixels)),
            new PlayableProfileField("teamSignals.mapPixels","Командные сигналы (team-map-signals-v1)","Значок на карте","Значок на карте; параметры отображения и времени командных сигналов.","px",10,28,1,p=>p.teamSignalMapPixels,nameof(PlayableProfileData.teamSignalMapPixels)),
            new PlayableProfileField("teamSignals.wheelPixels","Командные сигналы (team-map-signals-v1)","Круг выбора","Круг выбора; параметры отображения и времени командных сигналов.","px",160,360,10,p=>p.teamSignalWheelPixels,nameof(PlayableProfileData.teamSignalWheelPixels)),
            new PlayableProfileField("teamSignals.repeatRadius","Командные сигналы (team-map-signals-v1)","Радиус повтора","Радиус повтора; параметры отображения и времени командных сигналов.","m",0.1,3,0.1,p=>p.teamSignalRepeatRadius,nameof(PlayableProfileData.teamSignalRepeatRadius)),
            new PlayableProfileField("map.minimapMarkerSize","Minimap","Building marker scale","Building marker scale; controls map readability and layout.","ratio",0.1,5,0.01,p=>p.minimapMarkerSize,nameof(PlayableProfileData.minimapMarkerSize)),
            new PlayableProfileField("map.minimapMarkerStroke","Minimap","Building marker stroke","Building marker stroke; controls map readability and layout.","px",0.1,4,0.01,p=>p.minimapMarkerStroke,nameof(PlayableProfileData.minimapMarkerStroke)),
            new PlayableProfileField("map.minimapTerrainStroke","Minimap","Terrain outline","Terrain outline; controls map readability and layout.","px",0.1,4,0.01,p=>p.minimapTerrainStroke,nameof(PlayableProfileData.minimapTerrainStroke)),
            new PlayableProfileField("map.minimapCameraStroke","Minimap","Camera footprint stroke","Camera footprint stroke; controls map readability and layout.","px",0.25,6,0.25,p=>p.minimapCameraStroke,nameof(PlayableProfileData.minimapCameraStroke)),

            new PlayableProfileField("vision.fogTintR","Fog presentation","Fog red","Red component of unexplored terrain tint.","ratio",0,1,0.005,p=>p.fogTintR,nameof(PlayableProfileData.fogTintR)),
            new PlayableProfileField("vision.fogTintG","Fog presentation","Fog green","Green component of unexplored terrain tint.","ratio",0,1,0.005,p=>p.fogTintG,nameof(PlayableProfileData.fogTintG)),
            new PlayableProfileField("vision.fogTintB","Fog presentation","Fog blue","Blue component of unexplored terrain tint.","ratio",0,1,0.005,p=>p.fogTintB,nameof(PlayableProfileData.fogTintB)),
            new PlayableProfileField("vision.fogMemoryBrightness","Fog presentation","Memory brightness","Relative brightness of confirmed building memory.","ratio",0.05,1,0.01,p=>p.fogMemoryBrightness,nameof(PlayableProfileData.fogMemoryBrightness)),
            new PlayableProfileField("vision.fogMemoryDesaturation","Fog presentation","Memory desaturation","Desaturation of non-team memory surfaces; confirmed owner paint remains colored.","ratio",0,1,0.01,p=>p.fogMemoryDesaturation,nameof(PlayableProfileData.fogMemoryDesaturation)),

            new PlayableProfileField("vision.headquartersVisionRange","Vision and map","HeadquartersVisionRange","Imported buildings.headquarters.vision; controls native team vision or fog/map readability.","m",1,128,0.1,p=>p.headquartersVisionRange,nameof(PlayableProfileData.headquartersVisionRange)),
            new PlayableProfileField("vision.outpostVisionRange","Vision and map","OutpostVisionRange","Imported buildings.outpost.vision; controls native team vision or fog/map readability.","m",1,128,0.1,p=>p.outpostVisionRange,nameof(PlayableProfileData.outpostVisionRange)),
            new PlayableProfileField("vision.factoryVisionRange","Vision and map","FactoryVisionRange","Imported buildings.factory.vision; controls native team vision or fog/map readability.","m",1,128,0.1,p=>p.factoryVisionRange,nameof(PlayableProfileData.factoryVisionRange)),
            new PlayableProfileField("vision.refineryVisionRange","Vision and map","RefineryVisionRange","Imported buildings.refinery.vision; controls native team vision or fog/map readability.","m",1,128,0.1,p=>p.refineryVisionRange,nameof(PlayableProfileData.refineryVisionRange)),
            new PlayableProfileField("vision.mineVisionRange","Vision and map","MineVisionRange","Imported buildings.mine.vision; controls native team vision or fog/map readability.","m",1,128,0.1,p=>p.mineVisionRange,nameof(PlayableProfileData.mineVisionRange)),
            new PlayableProfileField("vision.constructionVisionMultiplier","Vision and map","ConstructionVisionMultiplier","Imported construction.visionMultiplier; controls native team vision or fog/map readability.","ratio",0.01,1,0.01,p=>p.constructionVisionMultiplier,nameof(PlayableProfileData.constructionVisionMultiplier)),
            new PlayableProfileField("vision.visionCellSize","Vision and map","VisionCellSize","Imported map.visionCellSize; controls native team vision or fog/map readability.","m",0.25,32,0.25,p=>p.visionCellSize,nameof(PlayableProfileData.visionCellSize)),
            new PlayableProfileField("vision.fogRevealMs","Vision and map","FogRevealMs","Imported system.fogRevealMs; controls native team vision or fog/map readability.","ms",1,10000,1,p=>p.fogRevealMs,nameof(PlayableProfileData.fogRevealMs)),
            new PlayableProfileField("vision.lingeringSeconds","Остаточный обзор · lingering-team-vision-v1","Время сужения обзора после потери источника","Линейное сужение прежнего радиуса до нуля после уничтожения или нейтрализации. Ноль отключает остаточный обзор.","с",0,10,.1,p=>p.lingeringVisionSeconds,nameof(PlayableProfileData.lingeringVisionSeconds)),
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
            new PlayableProfileField("relay.incomePerPeriod","Relay towers","Relay income","Independent credits per economy period; unrelated to refinery upgrades.","credits",0,1000,1,p=>p.relayIncomePerPeriod,nameof(PlayableProfileData.relayIncomePerPeriod)),
            new PlayableProfileField("relay.captureRadius","Relay towers","Nearby capture radius","Units may stand beside the circular platform.","m",2,8,.1,p=>p.relayCaptureRadius,nameof(PlayableProfileData.relayCaptureRadius)),
            new PlayableProfileField("relay.captureSeconds","Relay towers","Capture/neutralization time","Time for one full capture or neutralization phase.","s",.1,60,.1,p=>p.relayCaptureSeconds,nameof(PlayableProfileData.relayCaptureSeconds)),
            new PlayableProfileField("relay.visionRange","Relay towers","Обзор захваченной вышки","Радиус командного обзора после полного захвата и до полной нейтрализации. Независим от обзора танка.","m",1,128,.1,p=>p.relayVisionRange,nameof(PlayableProfileData.relayVisionRange)),
            new PlayableProfileField("relay.footprintRadius","Relay towers","Solid pedestal radius","Fixed central pole obstacle; surrounding platform remains traversable.","m",.5,1.2,.05,p=>p.relayFootprintRadius,nameof(PlayableProfileData.relayFootprintRadius)),
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
            new PlayableProfileField("artillery.damage", "Юниты · Шквал", "Урон по площади", "Базовый урон открытым жертвам взрыва, кроме физически задетого тела.", "HP", 0, 1000, 1, p=>p.shkvalDamage,nameof(PlayableProfileData.shkvalDamage)),
            new PlayableProfileField("artillery.fireIntervalMs", "Ракета и взрыв", "Интервал огня", "Пауза между одиночными ракетами.", "мс", 10, 10000, 10, p=>p.shkvalFireIntervalMs,nameof(PlayableProfileData.shkvalFireIntervalMs)),
            new PlayableProfileField("artillery.projectileSpeed", "Юниты · Шквал", "Скорость снаряда", "Скорость физического снаряда; определяет время до попадания.", "м/с", 0.1, 200, 0.5, p=>p.shkvalProjectileSpeed,nameof(PlayableProfileData.shkvalProjectileSpeed)),
            new PlayableProfileField("artillery.arcHeight", "Ракета и взрыв", "Высота дуги", "Подъём над прямой между запуском и приземлением; меняет время полёта.", "м", 0.1, 30, 0.1, p=>p.shkvalArcHeight,nameof(PlayableProfileData.shkvalArcHeight)),
            new PlayableProfileField("artillery.blastRadius", "Ракета и взрыв", "Радиус взрыва после Наведения", "Полный радиус после исследования; базовый Шквал использует профильный множитель. Урон включает союзников.", "м", 0.1, 10, 0.1, p=>p.shkvalBlastRadius,nameof(PlayableProfileData.shkvalBlastRadius)),
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
            new PlayableProfileField("presentation.directImpact.sparkWidth", "Бой · Попадания", "Толщина тонкой искры", "Ширина искры относительно радиуса эффекта; не меняет пули.", "доля", .01, .12, .005, p=>p.directImpactSparkWidth,nameof(PlayableProfileData.directImpactSparkWidth)),
            new PlayableProfileField("presentation.directImpact.glow", "Бой · Попадания", "Жар металла", "Яркость бело-горячей сердцевины и мягкого янтарного свечения.", "×", .25, 4, .05, p=>p.directImpactGlow,nameof(PlayableProfileData.directImpactGlow)),
            new PlayableProfileField("presentation.directImpact.smokeOpacity", "Бой · Попадания", "Лёгкий дым", "Прозрачность короткого дымка; у исследователя слабее.", "доля", 0, .5, .01, p=>p.directImpactSmokeOpacity,nameof(PlayableProfileData.directImpactSmokeOpacity)),
            new PlayableProfileField("presentation.directImpact.radius", "Бой · Попадания", "Размер попадания", "Радиус короткой вспышки прямого попадания; не влияет на урон.", "м", .03, .5, .01, p=>p.directImpactRadius,nameof(PlayableProfileData.directImpactRadius)),
            new PlayableProfileField("presentation.directImpact.seconds", "Бой · Попадания", "Длительность попадания", "Время вспышки и искр в игровом времени.", "с", .033, .5, .01, p=>p.directImpactSeconds,nameof(PlayableProfileData.directImpactSeconds)),
            new PlayableProfileField("presentation.directImpact.sparkLength", "Бой · Попадания", "Разлёт искр", "Длина небольших искр от точки попадания.", "м", .03, 1, .01, p=>p.directImpactSparkLength,nameof(PlayableProfileData.directImpactSparkLength)),
            new PlayableProfileField("presentation.directImpact.explorerScale", "Бой · Попадания", "Слабое попадание исследователя", "Размер вспышки и искр каждой попавшей пули относительно танка.", "доля", .1, 1, .05, p=>p.explorerImpactScale,nameof(PlayableProfileData.explorerImpactScale)),
            new PlayableProfileField("artillery.impactEffectRadius", "Бой · Остовы и эффекты", "Размер эффекта попадания", "Радиус локальной вспышки по правилам веб-версии; не радиус урона.", "м", .03, 1, .01, p=>p.impactEffectRadius,nameof(PlayableProfileData.impactEffectRadius)),
            new PlayableProfileField("presentation.unitWreck.lifetimeSeconds", "Бой · Остовы и эффекты", "Время жизни остова", "Выдержка после вспышки или приземления, затем начинается погружение. Остов блокирует движение.", "с", 0.1, 120, 0.1, p=>p.unitWreckLifetimeSeconds,nameof(PlayableProfileData.unitWreckLifetimeSeconds)),
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
            new PlayableProfileField("presentation.incomeMarkerFontPixels", "Здания · Доход", "Размер цифры дохода", "Размер всплывающего дохода над зданием.", "px", 10, 48, 1, p=>p.incomeMarkerFontPixels,nameof(PlayableProfileData.incomeMarkerFontPixels)),
            new PlayableProfileField("presentation.incomeMarkerRisePixels", "Здания · Доход", "Подъём цифры дохода", "Расстояние подъёма за время показа.", "px", 0, 100, 1, p=>p.incomeMarkerRisePixels,nameof(PlayableProfileData.incomeMarkerRisePixels)),
            new PlayableProfileField("presentation.incomeMarkerDurationSeconds", "Здания · Доход", "Время показа дохода", "Игровые секунды до исчезновения цифры; пауза останавливает анимацию.", "s", .1, 5, .1, p=>p.incomeMarkerDurationSeconds,nameof(PlayableProfileData.incomeMarkerDurationSeconds)),
            new PlayableProfileField("presentation.lifecycleMarkerPixels", "Здания · Маркеры", "Размер маркера ремонта", "Размер маркера ремонта; не влияет на симуляцию.", "px", 12, 80, 1, p=>p.lifecycleMarkerPixels,nameof(PlayableProfileData.lifecycleMarkerPixels)),
            new PlayableProfileField("presentation.lifecycleMarkerPulseHz", "Здания · Маркеры", "Пульсация маркера", "Пульсация маркера; не влияет на симуляцию.", "Гц", 0.1, 5, 0.1, p=>p.lifecycleMarkerPulseHz,nameof(PlayableProfileData.lifecycleMarkerPulseHz)),
            new PlayableProfileField("presentation.lifecycleMarkerMinOpacity", "Здания · Маркеры", "Минимальная непрозрачность", "Минимальная непрозрачность; не влияет на симуляцию.", "доля", 0.1, 1, 0.05, p=>p.lifecycleMarkerMinOpacity,nameof(PlayableProfileData.lifecycleMarkerMinOpacity)),
            new PlayableProfileField("presentation.lifecycleMarkerOffsetMeters", "Здания · Маркеры", "Отступ маркера над моделью", "Отступ маркера над моделью; не влияет на симуляцию.", "м", 0, 5, 0.1, p=>p.lifecycleMarkerOffsetMeters,nameof(PlayableProfileData.lifecycleMarkerOffsetMeters)),
            new PlayableProfileField("economy.startingCredits", "Economy", "Starting credits", "Release starting credits.", "credits", 0, 100000, 1, p=>p.startingCredits,nameof(PlayableProfileData.startingCredits)), new PlayableProfileField("economy.incomeSettlementSeconds", "Economy", "Income settlement interval", "Time between proportional credit settlements; total income rate is unchanged.", "s", .1, 1, .1, p=>p.incomeSettlementSeconds,nameof(PlayableProfileData.incomeSettlementSeconds)), new PlayableProfileField("economy.incomePeriodSeconds", "Economy", "Income period", "Period defining the income amount and marker cadence; credit cadence is configured separately.", "s", 1, 60, 1, p=>p.incomePeriodSeconds,nameof(PlayableProfileData.incomePeriodSeconds)), new PlayableProfileField("economy.buildingCancellationRefundRatio", "Economy", "Building refund", "Release building cancellation refund ratio.", "ratio", 0, 1, .01, p=>p.buildingCancellationRefundRatio,nameof(PlayableProfileData.buildingCancellationRefundRatio)), new PlayableProfileField("economy.unitCancellationRefundRatio", "Economy", "Unit refund", "Release unit cancellation refund ratio.", "ratio", 0, 1, .01, p=>p.unitCancellationRefundRatio,nameof(PlayableProfileData.unitCancellationRefundRatio)), new PlayableProfileField("economy.headquartersHealth", "Economy", "HQ health", "Release headquarters health.", "HP", 1, 10000, 1, p=>p.headquartersHealth,nameof(PlayableProfileData.headquartersHealth)), new PlayableProfileField("economy.headquartersBuildSeconds", "Economy", "HQ build time", "Release headquarters build duration.", "s", 1, 300, 1, p=>p.headquartersBuildSeconds,nameof(PlayableProfileData.headquartersBuildSeconds)), new PlayableProfileField("economy.headquartersCreditCost", "Economy", "HQ cost", "Release headquarters cost; initial HQ is free by scenario rule.", "credits", 0, 100000, 1, p=>p.headquartersCreditCost,nameof(PlayableProfileData.headquartersCreditCost)), new PlayableProfileField("economy.headquartersIncomePerPeriod", "Economy", "HQ income", "Release HQ credits awarded per income period.", "credits", 0, 100000, 1, p=>p.headquartersIncomePerPeriod,nameof(PlayableProfileData.headquartersIncomePerPeriod)), new PlayableProfileField("economy.headquartersFootprintRadius", "Economy", "HQ radius", "Release headquarters footprint radius.", "m", .1, 20, .01, p=>p.headquartersFootprintRadius,nameof(PlayableProfileData.headquartersFootprintRadius)), new PlayableProfileField("economy.factoryHealth", "Economy", "Factory health", "Release factory health.", "HP", 1, 10000, 1, p=>p.factoryHealth,nameof(PlayableProfileData.factoryHealth)), new PlayableProfileField("economy.factoryBuildSeconds", "Economy", "Factory build time", "Release factory build duration.", "s", 1, 300, 1, p=>p.factoryBuildSeconds,nameof(PlayableProfileData.factoryBuildSeconds)), new PlayableProfileField("economy.factoryCreditCost", "Economy", "Factory cost", "Release factory cost.", "credits", 0, 100000, 1, p=>p.factoryCreditCost,nameof(PlayableProfileData.factoryCreditCost)), new PlayableProfileField("economy.factoryFootprintRadius", "Economy", "Factory radius", "Release factory footprint radius.", "m", .1, 20, .01, p=>p.factoryFootprintRadius,nameof(PlayableProfileData.factoryFootprintRadius)), new PlayableProfileField("economy.refineryHealth", "Economy", "Refinery health", "Release refinery health.", "HP", 1, 10000, 1, p=>p.refineryHealth,nameof(PlayableProfileData.refineryHealth)), new PlayableProfileField("economy.refineryBuildSeconds", "Economy", "Refinery build time", "Release refinery build duration.", "s", 1, 300, 1, p=>p.refineryBuildSeconds,nameof(PlayableProfileData.refineryBuildSeconds)), new PlayableProfileField("economy.refineryCreditCost", "Economy", "Refinery cost", "Release refinery cost.", "credits", 0, 100000, 1, p=>p.refineryCreditCost,nameof(PlayableProfileData.refineryCreditCost)), new PlayableProfileField("economy.refineryIncomePerPeriod", "Economy", "Refinery income", "Release refinery credits awarded per income period.", "credits", 0, 100000, 1, p=>p.refineryIncomePerPeriod,nameof(PlayableProfileData.refineryIncomePerPeriod)), new PlayableProfileField("economy.refineryFootprintRadius", "Economy", "Refinery radius", "Release refinery footprint radius.", "m", .1, 20, .01, p=>p.refineryFootprintRadius,nameof(PlayableProfileData.refineryFootprintRadius)),
            new PlayableProfileField("system.rallyFlagHeight", "System", "Высота флажка точки сбора", "Визуальная высота флажка выбранного здания; не меняет достижимость или приказ юнита.", "m", .5, 4, .1, p=>p.rallyFlagHeight,nameof(PlayableProfileData.rallyFlagHeight)),
            new PlayableProfileField("building.headquartersModelHeightMeters", "Buildings", "HQ model height", "Release headquarters model height.", "m", .1, 100, .1, p=>p.headquartersModelHeightMeters,nameof(PlayableProfileData.headquartersModelHeightMeters)), new PlayableProfileField("building.headquartersModelScale", "Buildings", "HQ model scale", "Release headquarters model scale.", "ratio", .1, 5, .01, p=>p.headquartersModelScale,nameof(PlayableProfileData.headquartersModelScale)), new PlayableProfileField("building.factoryModelHeightMeters", "Buildings", "Factory model height", "Release factory model height.", "m", .1, 100, .1, p=>p.factoryModelHeightMeters,nameof(PlayableProfileData.factoryModelHeightMeters)), new PlayableProfileField("building.factoryModelScale", "Buildings", "Factory model scale", "Release factory model scale.", "ratio", .1, 5, .01, p=>p.factoryModelScale,nameof(PlayableProfileData.factoryModelScale)), new PlayableProfileField("building.refineryModelHeightMeters", "Buildings", "Refinery model height", "Release refinery model height.", "m", .1, 100, .1, p=>p.refineryModelHeightMeters,nameof(PlayableProfileData.refineryModelHeightMeters)), new PlayableProfileField("building.refineryModelScale", "Buildings", "Refinery model scale", "Release refinery model scale.", "ratio", .1, 5, .01, p=>p.refineryModelScale,nameof(PlayableProfileData.refineryModelScale)),
            new PlayableProfileField("arena.halfExtent", "Arena", "Half extent", "Fixed playable ground arena half extent.", "m", 16, 128, 1, p=>p.arenaHalfExtent,nameof(PlayableProfileData.arenaHalfExtent)), new PlayableProfileField("arena.playerHeadquartersX", "Arena", "Player HQ X", "Player base on the left side of the arena.", "m", -120, -1, 1, p=>p.playerHeadquartersX,nameof(PlayableProfileData.playerHeadquartersX)), new PlayableProfileField("arena.enemyHeadquartersX", "Arena", "Enemy HQ X", "Enemy base on the right side of the arena.", "m", 1, 120, 1, p=>p.enemyHeadquartersX,nameof(PlayableProfileData.enemyHeadquartersX)), new PlayableProfileField("arena.headquartersZ", "Arena", "HQ Z", "Shared headquarters row.", "m", -120, 120, 1, p=>p.headquartersZ,nameof(PlayableProfileData.headquartersZ)), new PlayableProfileField("arena.playerFactoryPadX", "Arena", "Player factory pad X", "Player factory construction pad.", "m", -120, -1, 1, p=>p.playerFactoryPadX,nameof(PlayableProfileData.playerFactoryPadX)), new PlayableProfileField("arena.playerFactoryPadZ", "Arena", "Player factory pad Z", "Player factory construction pad.", "m", -120, 120, 1, p=>p.playerFactoryPadZ,nameof(PlayableProfileData.playerFactoryPadZ)), new PlayableProfileField("arena.playerRefineryPadX", "Arena", "Player refinery pad X", "Player refinery construction pad.", "m", -120, -1, 1, p=>p.playerRefineryPadX,nameof(PlayableProfileData.playerRefineryPadX)), new PlayableProfileField("arena.playerRefineryPadZ", "Arena", "Player refinery pad Z", "Player refinery construction pad.", "m", -120, 120, 1, p=>p.playerRefineryPadZ,nameof(PlayableProfileData.playerRefineryPadZ)), new PlayableProfileField("arena.enemyFactoryPadX", "Arena", "Enemy factory pad X", "Enemy factory construction pad.", "m", 1, 120, 1, p=>p.enemyFactoryPadX,nameof(PlayableProfileData.enemyFactoryPadX)), new PlayableProfileField("arena.enemyFactoryPadZ", "Arena", "Enemy factory pad Z", "Enemy factory construction pad.", "m", -120, 120, 1, p=>p.enemyFactoryPadZ,nameof(PlayableProfileData.enemyFactoryPadZ)), new PlayableProfileField("arena.enemyRefineryPadX", "Arena", "Enemy refinery pad X", "Enemy refinery construction pad.", "m", 1, 120, 1, p=>p.enemyRefineryPadX,nameof(PlayableProfileData.enemyRefineryPadX)), new PlayableProfileField("arena.enemyRefineryPadZ", "Arena", "Enemy refinery pad Z", "Enemy refinery construction pad.", "m", -120, 120, 1, p=>p.enemyRefineryPadZ,nameof(PlayableProfileData.enemyRefineryPadZ)), new PlayableProfileField("arena.centralObstacleHalfWidth", "Arena", "Obstacle half width", "Central obstacle leaves top and bottom passages.", "m", .5, 20, .5, p=>p.centralObstacleHalfWidth,nameof(PlayableProfileData.centralObstacleHalfWidth)), new PlayableProfileField("arena.centralObstacleHalfDepth", "Arena", "Obstacle half depth", "Central obstacle half depth.", "m", .5, 28, .5, p=>p.centralObstacleHalfDepth,nameof(PlayableProfileData.centralObstacleHalfDepth)),
            new PlayableProfileField("arena.passageHalfWidth", "Arena", "Passage half width", "Half width of the central passage.", "m", .5, 16, .5, p=>p.passageHalfWidth,nameof(PlayableProfileData.passageHalfWidth)),
            new PlayableProfileField("system.matchScoreEarnedCreditsDivisor", "System", "Score credit divisor", "Earned credits contribution to match score.", "credits", 1, 1000, 1, p=>p.matchScoreEarnedCreditsDivisor,nameof(PlayableProfileData.matchScoreEarnedCreditsDivisor)),
            new PlayableProfileField("army.capacity", "Army", "Army capacity", "Living and active reserved population limit; paid waiting orders do not reserve capacity.", "population", 1, 1000, 1, p=>p.armyCapacity,nameof(PlayableProfileData.armyCapacity)),
            new PlayableProfileField("system.follow.distance", "Follow", "Following distance", "Source distance behind the leader", "m", 1, 10, .1, p=>p.followDistance,nameof(PlayableProfileData.followDistance)),
            new PlayableProfileField("system.follow.arrivalTolerance", "Follow", "Following arrival tolerance", "Source stationary follow tolerance", "m", .05, 1, .05, p=>p.followArrivalTolerance,nameof(PlayableProfileData.followArrivalTolerance)),
            new PlayableProfileField("system.idleAutoDefenseMultiplier", "System", "Idle auto-defense range", "Source range multiplier for idle target acquisition; does not authorize movement or extend weapon range.", "ratio", 1, 3, .05, p=>p.idleAutoDefenseMultiplier,nameof(PlayableProfileData.idleAutoDefenseMultiplier)),
            new PlayableProfileField("navigation.groupCorridor.maximumExcessPercent","Group navigation","Group route excess","Per-member certified weighted route excess above its own global optimum; missing certificate uses exact routing.","%",15,15,1,p=>p.groupCorridorMaximumExcessPercent,nameof(PlayableProfileData.groupCorridorMaximumExcessPercent)),
            new PlayableProfileField("groupMarch.maximumStretch", "Group march", "Maximum stretch", "Maximum centerline lead before a moving member yields to regroup.", "m", .5, 40, .1, p=>p.groupMarchMaximumStretch,nameof(PlayableProfileData.groupMarchMaximumStretch)),
            new PlayableProfileField("system.unitOrderQueueLimit", "System", "Deferred unit orders", "Maximum deferred tactical orders per unit; overflow silently preserves the existing FIFO.", "orders", 1, 4096, 1, p=>p.unitOrderQueueLimit,nameof(PlayableProfileData.unitOrderQueueLimit)),
            new PlayableProfileField("behavior.enemyAdvanceDelaySeconds", "Behavior", "Enemy advance delay", "Enemy waits to assemble before its first advance.", "s", 1, 600, 1, p=>p.enemyAdvanceDelaySeconds,nameof(PlayableProfileData.enemyAdvanceDelaySeconds)), new PlayableProfileField("behavior.attackApproachRangeRatio", "Behavior", "Attack approach", "Fraction of attack range retained while approaching a target.", "ratio", .1, 1, .01, p=>p.attackApproachRangeRatio,nameof(PlayableProfileData.attackApproachRangeRatio)), new PlayableProfileField("behavior.attackRepathSeconds", "Behavior", "Attack repath", "Minimum interval between attack path requests.", "s", .05, 10, .05, p=>p.attackRepathSeconds,nameof(PlayableProfileData.attackRepathSeconds)), new PlayableProfileField("production.factoryExitDistance", "Production", "Factory exit", "Distance a new unit clears from its factory.", "m", .5, 20, .5, p=>p.factoryExitDistance,nameof(PlayableProfileData.factoryExitDistance)), new PlayableProfileField("production.defaultRallyDistance", "Production", "Default rally", "Factory default rally distance.", "m", 1, 32, .5, p=>p.defaultRallyDistance,nameof(PlayableProfileData.defaultRallyDistance)), new PlayableProfileField("input.targetPickRadiusMultiplier", "Input", "Target pick multiplier", "Multiplier applied to a unit footprint for pointer target picking.", "ratio", .5, 5, .1, p=>p.targetPickRadiusMultiplier,nameof(PlayableProfileData.targetPickRadiusMultiplier)), new PlayableProfileField("input.buildingPickRadius", "Input", "Building pick radius", "Pointer pick radius for buildings.", "m", .5, 20, .1, p=>p.buildingPickRadius,nameof(PlayableProfileData.buildingPickRadius)), new PlayableProfileField("navigation.requestsPerFrame", "Navigation", "Requests per frame", "Maximum navigation requests admitted per frame.", "requests", 1, 64, 1, p=>p.navigationRequestsPerFrame,nameof(PlayableProfileData.navigationRequestsPerFrame)),
            new PlayableProfileField("presentation.targetFramesPerSecond", "Rendering", "Target frame rate", "Native rendering cadence independent of the 30Hz domain.", "fps", 30, 240, 1, p=>p.renderTargetFramesPerSecond,nameof(PlayableProfileData.renderTargetFramesPerSecond)),
            new PlayableProfileField("camera.orthoSize", "Camera", "View size", "Playable camera orthographic half height.", "m", 5, 100, .01, p=>p.cameraOrthoSize,nameof(PlayableProfileData.cameraOrthoSize)), new PlayableProfileField("camera.height", "Camera", "Height", "Playable camera height above ground.", "m", 5, 200, 1, p=>p.cameraHeight,nameof(PlayableProfileData.cameraHeight)), new PlayableProfileField("camera.offsetX", "Camera", "X offset", "Diagonal camera lateral offset; building fronts remain facing world south.", "m", -100, 100, 1, p=>p.cameraOffsetX,nameof(PlayableProfileData.cameraOffsetX)), new PlayableProfileField("camera.offsetZ", "Camera", "Z offset", "Playable camera longitudinal offset.", "m", -100, 100, 1, p=>p.cameraOffsetZ,nameof(PlayableProfileData.cameraOffsetZ)), new PlayableProfileField("camera.edgePanSpeed", "Camera", "Edge pan speed", "Pointer edge pan speed in focused gameplay; overridden by the local edge-scroll setting.", "m/s", .1, 100, .1, p=>p.cameraEdgePanSpeed,nameof(PlayableProfileData.cameraEdgePanSpeed)), new PlayableProfileField("camera.edgePanZonePixels", "Camera", "Edge pan zone", "Pointer edge acceleration band inside the active camera viewport.", "px", 8, 120, 1, p=>p.cameraEdgePanZonePixels,nameof(PlayableProfileData.cameraEdgePanZonePixels)), new PlayableProfileField("camera.panSpeed", "Camera", "Pan speed", "Arrow-key camera ground-pan speed; overridden by the local arrow-scroll setting.", "m/s", 1, 50, 1, p=>p.cameraPanSpeed,nameof(PlayableProfileData.cameraPanSpeed)), new PlayableProfileField("camera.minZoom", "Camera", "Minimum zoom", "Closest playable camera zoom.", "m", 1, 100, .005, p=>p.cameraMinZoom,nameof(PlayableProfileData.cameraMinZoom)), new PlayableProfileField("camera.maxZoom", "Camera", "Maximum zoom", "Farthest playable camera zoom.", "m", 5, 100, .01, p=>p.cameraMaxZoom,nameof(PlayableProfileData.cameraMaxZoom)), new PlayableProfileField("camera.zoomSpeed", "Camera", "Zoom speed", "Mouse-wheel zoom multiplier.", "ratio", .001, 1, .001, p=>p.cameraZoomSpeed,nameof(PlayableProfileData.cameraZoomSpeed)), new PlayableProfileField("input.selectionDragPixels", "Input", "Selection drag", "Pointer travel required to start box selection.", "px", 1, 100, 1, p=>p.selectionDragPixels,nameof(PlayableProfileData.selectionDragPixels))
        };
        private static PlayableProfileField CrowdField(int index,Func<PlayableProfileData,double> read,string dataField)
        {var f=NavigationProfile.CrowdMetadata[index];return new PlayableProfileField(f.Path,f.Group,f.Label,f.Description,f.Unit,f.Minimum,f.Maximum,f.Step,read,dataField);}
        public static readonly IReadOnlyList<PlayableProfileField> CrowdFields=new[]{
            CrowdField(0,p=>p.crowdMotionHeadingIntervals,nameof(PlayableProfileData.crowdMotionHeadingIntervals)),CrowdField(1,p=>p.crowdMotionSpeedBands,nameof(PlayableProfileData.crowdMotionSpeedBands)),CrowdField(2,p=>p.crowdMotionLookaheadSeconds,nameof(PlayableProfileData.crowdMotionLookaheadSeconds)),CrowdField(3,p=>p.crowdMotionNeighborCap,nameof(PlayableProfileData.crowdMotionNeighborCap))
        };
        public static readonly PlayableProfileField AccruedCreditField=new PlayableProfileField("navigation.maxAccruedCredits","Navigation","Лимит накопленного бюджета навигации","Максимум ожидающих квантов; при заполнении отбрасывается новый. Не меняет точность поиска и работу одного кванта. 600 = существующий горизонт сборки 20 секунд × 30 Гц.","квант",1,600,1,p=>p.navigationMaxAccruedCredits==0?600:p.navigationMaxAccruedCredits,nameof(PlayableProfileData.navigationMaxAccruedCredits));
        public static readonly IReadOnlyList<PlayableProfileField> HawkFields=new[]{
new PlayableProfileField("hawk.aimTolerance","Ястреб · hawk-match-v1","Точность наведения","Допустимая ошибка направления корпуса перед пуском.","rad",0.001,1,0.001,p=>p.hawkAimTolerance,nameof(PlayableProfileData.hawkAimTolerance)),
new PlayableProfileField("hawk.preparationYawSpeed","Ястреб · hawk-match-v1","Сброс подготовки поворотом","Вращение быстрее порога сбрасывает подготовку залпа.","rad/s",0.01,10,0.01,p=>p.hawkPreparationYawSpeed,nameof(PlayableProfileData.hawkPreparationYawSpeed)),
new PlayableProfileField("hawk.missileLength","Ястреб · hawk-match-v1","Длина ракеты","Размер корпуса ракеты в отображении.","m",0.1,3,0.01,p=>p.hawkMissileLength,nameof(PlayableProfileData.hawkMissileLength)),
new PlayableProfileField("hawk.debrisSeconds","Ястреб · hawk-match-v1","Время обломков","Максимальное время безвредных обломков; касание земли прекращает эффект раньше.","s",0.1,5,0.1,p=>p.hawkDebrisSeconds,nameof(PlayableProfileData.hawkDebrisSeconds)),
new PlayableProfileField("hawk.debrisSpeed","Ястреб · hawk-match-v1","Разлёт обломков","Начальная скорость разлёта визуальных обломков.","m/s",0.1,20,0.1,p=>p.hawkDebrisSpeed,nameof(PlayableProfileData.hawkDebrisSpeed)),
new PlayableProfileField("hawk.debrisGravity","Ястреб · hawk-match-v1","Падение обломков","Ускорение падения визуальных обломков, без физического урона.","m/s2",0.1,30,0.01,p=>p.hawkDebrisGravity,nameof(PlayableProfileData.hawkDebrisGravity)),
new PlayableProfileField("hawk.debrisCount","Ястреб · hawk-match-v1","Число обломков","Число видимых фрагментов при воздушном уничтожении.","count",1,16,1,p=>p.hawkDebrisCount,nameof(PlayableProfileData.hawkDebrisCount)),
new PlayableProfileField("hawk.modelScale","Ястреб · hawk-match-v1","Масштаб модели","Размер модели относительно её авторских метров.","ratio",.05,5,.01,p=>p.hawkModelScale,nameof(PlayableProfileData.hawkModelScale)),
            new PlayableProfileField("hawk.health","Ястреб · hawk-match-v1","Прочность","Полное здоровье базового Ястреба.","HP",1,10000,1,p=>p.hawkHealth,nameof(PlayableProfileData.hawkHealth)),
            new PlayableProfileField("hawk.creditCost","Ястреб · hawk-match-v1","Цена","Стоимость одного Ястреба в кредитах.","credits",1,100000,1,p=>p.hawkCreditCost,nameof(PlayableProfileData.hawkCreditCost)),
            new PlayableProfileField("hawk.populationCost","Ястреб · hawk-match-v1","Лимит армии","Место одного Ястреба в лимите армии.","population",1,100,1,p=>p.hawkPopulationCost,nameof(PlayableProfileData.hawkPopulationCost)),
            new PlayableProfileField("hawk.productionSeconds","Ястреб · hawk-match-v1","Производство","Время оплаченного производства до начала выпуска.","s",1,300,1,p=>p.hawkProductionSeconds,nameof(PlayableProfileData.hawkProductionSeconds)),
            new PlayableProfileField("hawk.radius","Ястреб · hawk-match-v1","Безопасный радиус","Горизонтальный габарит разделения; минимум покрывает корпус, хвост и вращающийся винт утверждённой модели.","m",1,5,0.01,p=>p.hawkRadius,nameof(PlayableProfileData.hawkRadius)),
            new PlayableProfileField("hawk.speed","Ястреб · hawk-match-v1","Полёт вперёд","Предельная скорость прямого полёта.","m/s",0.1,50,0.1,p=>p.hawkSpeed,nameof(PlayableProfileData.hawkSpeed)),
            new PlayableProfileField("hawk.reverseSpeed","Ястреб · hawk-match-v1","Отход назад","Скорость короткого отхода без разворота.","m/s",0.1,50,0.1,p=>p.hawkReverseSpeed,nameof(PlayableProfileData.hawkReverseSpeed)),
            new PlayableProfileField("hawk.strafeSpeed","Ястреб · hawk-match-v1","Боковое смещение","Предельная скорость короткого бокового манёвра.","m/s",0.1,50,0.1,p=>p.hawkStrafeSpeed,nameof(PlayableProfileData.hawkStrafeSpeed)),
            new PlayableProfileField("hawk.acceleration","Ястреб · hawk-match-v1","Разгон","Изменение горизонтальной скорости при разгоне.","m/s2",0.1,100,0.1,p=>p.hawkAcceleration,nameof(PlayableProfileData.hawkAcceleration)),
            new PlayableProfileField("hawk.braking","Ястреб · hawk-match-v1","Торможение","Изменение горизонтальной скорости при остановке.","m/s2",0.1,100,0.1,p=>p.hawkBraking,nameof(PlayableProfileData.hawkBraking)),
            new PlayableProfileField("hawk.shortSeconds","Ястреб · hawk-match-v1","Отход до разворота","Длительность дальнего отхода с удержанием оружейной цели перед обычным разворотом; ближнее смещение ограничено своей дистанцией.","s",0.1,5,0.05,p=>p.hawkShortSeconds,nameof(PlayableProfileData.hawkShortSeconds)),
            new PlayableProfileField("hawk.shortDistance","Ястреб · hawk-match-v1","Ближний манёвр","Максимальная дальность короткого смещения без удержания оружейной цели.","m",0.1,20,0.1,p=>p.hawkShortDistance,nameof(PlayableProfileData.hawkShortDistance)),
            new PlayableProfileField("hawk.yawSpeed","Ястреб · hawk-match-v1","Скорость разворота","Предельная угловая скорость корпуса.","rad/s",0.1,30,0.1,p=>p.hawkYawSpeed,nameof(PlayableProfileData.hawkYawSpeed)),
            new PlayableProfileField("hawk.yawAcceleration","Ястреб · hawk-match-v1","Разгон разворота","Плавность набора и снижения угловой скорости.","rad/s2",0.1,100,0.1,p=>p.hawkYawAcceleration,nameof(PlayableProfileData.hawkYawAcceleration)),
            new PlayableProfileField("hawk.cruisePitch","Ястреб · hawk-match-v1","Наклон вперёд","Наклон носа при установившемся полёте вперёд.","deg",0,45,1,p=>p.hawkCruisePitch,nameof(PlayableProfileData.hawkCruisePitch)),
            new PlayableProfileField("hawk.accelerationPitch","Ястреб · hawk-match-v1","Наклон на разгоне","Дополнительный наклон носа при разгоне и торможении.","deg",0,45,1,p=>p.hawkAccelerationPitch,nameof(PlayableProfileData.hawkAccelerationPitch)),
            new PlayableProfileField("hawk.bank","Ястреб · hawk-match-v1","Боковой крен","Предельный крен при боковом ускорении и повороте.","deg",0,60,1,p=>p.hawkBank,nameof(PlayableProfileData.hawkBank)),
            new PlayableProfileField("hawk.attitudeSpeed","Ястреб · hawk-match-v1","Плавность наклонов","Предельная скорость изменения pitch и roll.","deg/s",1,360,1,p=>p.hawkAttitudeSpeed,nameof(PlayableProfileData.hawkAttitudeSpeed)),
            new PlayableProfileField("hawk.clearance","Ястреб · hawk-match-v1","Просвет над геометрией","Безопасная высота над землёй, скалами и зданиями.","m",0.5,30,0.1,p=>p.hawkClearance,nameof(PlayableProfileData.hawkClearance)),
            new PlayableProfileField("hawk.climbSpeed","Ястреб · hawk-match-v1","Набор высоты","Скорость вертикального подъёма; горизонтальный полёт ждёт безопасного просвета.","m/s",0.1,30,0.1,p=>p.hawkClimbSpeed,nameof(PlayableProfileData.hawkClimbSpeed)),
            new PlayableProfileField("hawk.descentSpeed","Ястреб · hawk-match-v1","Снижение","Скорость плавного снижения после препятствия.","m/s",0.1,30,0.1,p=>p.hawkDescentSpeed,nameof(PlayableProfileData.hawkDescentSpeed)),
            new PlayableProfileField("hawk.lookahead","Ястреб · hawk-match-v1","Опережение рельефа","Дальность проверки высоты впереди по назначению.","m",1,100,0.5,p=>p.hawkLookahead,nameof(PlayableProfileData.hawkLookahead)),
            new PlayableProfileField("production.factoryAssemblyGap","Production · factory-assembly-v1","Зазор при расхождении у фабрики","Дополнительное расстояние между габаритами наземных юнитов при освобождении выезда; свободные места заполняются по обе стороны коридора.","m",0.05,3,0.05,p=>p.factoryAssemblyGap,nameof(PlayableProfileData.factoryAssemblyGap)),
            new PlayableProfileField("hawk.separation","Ястреб · hawk-match-v1","Зазор между вертолётами","Дополнительный зазор между безопасными габаритами любых воздушных юнитов.","m",0.05,5,0.05,p=>p.hawkSeparation,nameof(PlayableProfileData.hawkSeparation)),
            new PlayableProfileField("hawk.roofSeconds","Ястреб · hawk-match-v1","Лепестки крыши","Длительность открытия или закрытия люка.","s",0.1,20,0.1,p=>p.hawkRoofSeconds,nameof(PlayableProfileData.hawkRoofSeconds)),
            new PlayableProfileField("hawk.platformSeconds","Ястреб · hawk-match-v1","Подъёмная платформа","Длительность подъёма или опускания платформы.","s",0.1,20,0.1,p=>p.hawkPlatformSeconds,nameof(PlayableProfileData.hawkPlatformSeconds)),
            new PlayableProfileField("hawk.spinSeconds","Ястреб · hawk-match-v1","Раскрутка винта","Пауза раскрутки перед началом самостоятельного взлёта.","s",0.1,20,0.1,p=>p.hawkSpinSeconds,nameof(PlayableProfileData.hawkSpinSeconds)),
            new PlayableProfileField("hawk.rotorSpeed","Ястреб · hawk-match-v1","Основной винт","Установившаяся скорость вращения винта в отображении.","deg/s",10,3000,10,p=>p.hawkRotorSpeed,nameof(PlayableProfileData.hawkRotorSpeed)),
            new PlayableProfileField("hawk.range","Ястреб · hawk-match-v1","Дальность оружия","Дальность по горизонтальной проекции независимо от разницы высот.","m",1,100,0.1,p=>p.hawkRange,nameof(PlayableProfileData.hawkRange)),
            new PlayableProfileField("hawk.salvoResearchCost","Ястреб · hawk-salvo-research-v1","Цена исследования","Списание при старте исследования залпового огня.","credits",0,5000,10,p=>p.hawkSalvoResearchCost,nameof(PlayableProfileData.hawkSalvoResearchCost)),
            new PlayableProfileField("hawk.salvoResearchSeconds","Ястреб · hawk-salvo-research-v1","Время исследования","Длительность исследования залпового огня.","s",0.5,300,0.5,p=>p.hawkSalvoResearchSeconds,nameof(PlayableProfileData.hawkSalvoResearchSeconds)),
            new PlayableProfileField("hawk.upgradedPreparationSeconds","Ястреб · hawk-salvo-research-v1","Подготовка после улучшения","Непрерывная подготовка после остановки; перезарядка идёт независимо.","s",0.1,10,0.1,p=>p.hawkUpgradedPreparationSeconds,nameof(PlayableProfileData.hawkUpgradedPreparationSeconds)),
            new PlayableProfileField("hawk.upgradedSalvoDelay","Ястреб · hawk-salvo-research-v1","Интервал после улучшения","Пауза между двумя ракетами улучшенного Ястреба.","s",0.01,2,0.01,p=>p.hawkUpgradedSalvoDelay,nameof(PlayableProfileData.hawkUpgradedSalvoDelay)),
            new PlayableProfileField("hawk.upgradedReloadSeconds","Ястреб · hawk-salvo-research-v1","Перезарядка после улучшения","Перезарядка после полного или прерванного залпа; движение не сокращает её.","s",0.1,20,0.1,p=>p.hawkUpgradedReloadSeconds,nameof(PlayableProfileData.hawkUpgradedReloadSeconds)),
            new PlayableProfileField("hawk.preparationSeconds","Ястреб · hawk-match-v1","Подготовка залпа","Непрерывная подготовка после остановки и наведения; вертикальное движение её не сбрасывает.","s",0.1,10,0.1,p=>p.hawkPreparationSeconds,nameof(PlayableProfileData.hawkPreparationSeconds)),
            new PlayableProfileField("hawk.damage","Ястреб · hawk-match-v1","Урон ракеты","Прямой урон одной ракеты без взрыва по площади.","HP",1,1000,1,p=>p.hawkDamage,nameof(PlayableProfileData.hawkDamage)),
            new PlayableProfileField("hawk.salvoDelay","Ястреб · hawk-match-v1","Интервал двух ракет","Задержка между левым и правым пусками по одной зафиксированной цели.","s",0.01,2,0.01,p=>p.hawkSalvoDelay,nameof(PlayableProfileData.hawkSalvoDelay)),
            new PlayableProfileField("hawk.reloadSeconds","Ястреб · hawk-match-v1","Перезарядка","Перезарядка после полного или прерванного залпа.","s",0.1,20,0.1,p=>p.hawkReloadSeconds,nameof(PlayableProfileData.hawkReloadSeconds)),
            new PlayableProfileField("hawk.missileSpeed","Ястреб · hawk-match-v1","Скорость ракеты","Скорость трёхмерного полёта ракеты.","m/s",1,100,1,p=>p.hawkMissileSpeed,nameof(PlayableProfileData.hawkMissileSpeed)),
            new PlayableProfileField("hawk.missileTurn","Ястреб · hawk-match-v1","Доворот ракеты","Предельная угловая скорость ограниченного самонаведения.","rad/s",0.01,10,0.01,p=>p.hawkMissileTurn,nameof(PlayableProfileData.hawkMissileTurn)),
            new PlayableProfileField("hawk.missileRadius","Ястреб · hawk-match-v1","Габарит ракеты","Радиус физического swept-контакта ракеты.","m",0.01,1,0.01,p=>p.hawkMissileRadius,nameof(PlayableProfileData.hawkMissileRadius))};
        public static readonly IReadOnlyList<PlayableProfileField> TankReverseFields=new[]{
            new PlayableProfileField("tank.reverseDistance","Tank maneuvers","Short reverse distance","Maximum nearby final goal distance for reverse; zero disables the trial.","m",0,4,.1,p=>p.tankReverseDistance,nameof(PlayableProfileData.tankReverseDistance)),
            new PlayableProfileField("tank.reverseSpeedRatio","Tank maneuvers","Reverse speed","Fraction of current forward speed used by short reverse.","ratio",.1,1,.05,p=>p.tankReverseSpeedRatio,nameof(PlayableProfileData.tankReverseSpeedRatio)),
            new PlayableProfileField("tank.reverseConeRadians","Tank maneuvers","Rear cone","Maximum angle away from directly behind for short reverse.","rad",.05,1,.05,p=>p.tankReverseConeRadians,nameof(PlayableProfileData.tankReverseConeRadians))};
        public static readonly IReadOnlyList<PlayableProfileField> FactoryFields=new[]{
            new PlayableProfileField("factory.gateSeconds","Factory release","Gate motion","Time to fully open or close the factory gate.","s",.1,3,.05,p=>p.factoryGateSeconds,nameof(PlayableProfileData.factoryGateSeconds)),
            new PlayableProfileField("factory.rolloutSpeed","Factory release","Exit speed","Protected drive-out speed before control and vulnerability activate together.","m/s",.25,4,.05,p=>p.factoryRolloutSpeed,nameof(PlayableProfileData.factoryRolloutSpeed))};
        // Presentation-only controls stay out of the existing world/profile wire field lists.
        public static readonly IReadOnlyList<PlayableProfileField> HawkVisualFields=new[]{
new PlayableProfileField("hawk.missileVisualScale","Ястреб · hawk-match-v1","Масштаб модели ракеты","Масштаб корпуса и выхлопа ракеты только в отображении; физический радиус и боевые параметры не меняются.","×",0.1,2,0.05,p=>p.hawkMissileVisualScale,nameof(PlayableProfileData.hawkMissileVisualScale))};
        public static readonly IReadOnlyList<PlayableProfileField> WreckFields=new[]{
            new PlayableProfileField("wrecks.spawnProtectionSeconds","Бой · Остовы и эффекты","Неуязвимость нового остова","Защита после видимого появления или приземления; до появления остов также защищён. Попадания поглощаются без отложенного урона.","с",0,10,.1,p=>p.wreckSpawnProtectionSeconds,nameof(PlayableProfileData.wreckSpawnProtectionSeconds)),
            new PlayableProfileField("wrecks.wreckSinkSeconds","Бой · Остовы и эффекты","Погружение остова","Время плавного исчезновения после выдержки.","с",0.1,10,0.1,p=>p.wreckSinkSeconds,nameof(PlayableProfileData.wreckSinkSeconds)),
            new PlayableProfileField("wrecks.wreckBurialMin","Бой · Остовы и эффекты","Минимальная глубина посадки","Небольшая начальная посадка модели в поверхность.","м",0,1,0.01,p=>p.wreckBurialMin,nameof(PlayableProfileData.wreckBurialMin)),
            new PlayableProfileField("wrecks.wreckBurialMax","Бой · Остовы и эффекты","Максимальная глубина посадки","Случайная глубина фиксируется при создании.","м",0,1,0.01,p=>p.wreckBurialMax,nameof(PlayableProfileData.wreckBurialMax)),
            new PlayableProfileField("wrecks.wreckSinkDepth","Бой · Остовы и эффекты","Глубина исчезновения","Вертикальное погружение до удаления модели.","м",0.1,10,0.1,p=>p.wreckSinkDepth,nameof(PlayableProfileData.wreckSinkDepth)),
            new PlayableProfileField("wrecks.hawkWreckChancePercent","Бой · Остовы и эффекты","Вероятность падения Ястреба","Только при свободном месте непосредственно под вертолётом.","%",0,100,1,p=>p.hawkWreckChancePercent,nameof(PlayableProfileData.hawkWreckChancePercent)),
            new PlayableProfileField("wrecks.hawkWreckFallSeconds","Бой · Остовы и эффекты","Время падения Ястреба","Время падения до появления наземного препятствия.","с",0.1,5,0.1,p=>p.hawkWreckFallSeconds,nameof(PlayableProfileData.hawkWreckFallSeconds)),
            new PlayableProfileField("wrecks.unitDeathSmokeSeconds","Бой · Остовы и эффекты","Дым останков после гибели","Дым плавно затухает над останками после гибели.","с",0.1,10,0.1,p=>p.unitDeathSmokeSeconds,nameof(PlayableProfileData.unitDeathSmokeSeconds)),
            new PlayableProfileField("wrecks.wreckHitSmokeSeconds","Бой · Остовы и эффекты","Дым разрушенного остова","Короткий дым после попадания в остов.","с",0.1,5,0.1,p=>p.wreckHitSmokeSeconds,nameof(PlayableProfileData.wreckHitSmokeSeconds)),
            new PlayableProfileField("wrecks.wreckSmokeSize","Бой · Остовы и эффекты","Размер дыма","Диаметр клуба дыма.","м",0.1,5,0.1,p=>p.wreckSmokeSize,nameof(PlayableProfileData.wreckSmokeSize)),
            new PlayableProfileField("wrecks.wreckSmokeOpacity","Бой · Остовы и эффекты","Плотность дыма","Максимальная непрозрачность дыма.","доля",0,1,0.01,p=>p.wreckSmokeOpacity,nameof(PlayableProfileData.wreckSmokeOpacity)),
            new PlayableProfileField("wrecks.wreckSmokeRise","Бой · Остовы и эффекты","Подъём дыма","Скорость подъёма дыма над останками.","м/с",0,5,0.1,p=>p.wreckSmokeRise,nameof(PlayableProfileData.wreckSmokeRise)),
        };
        public static readonly IReadOnlyList<PlayableProfileField> Fields=PreCrowdFields.Concat(OrbitalProfileMetadata.Fields).Concat(WreckFields).Concat(TankReverseFields).Concat(HawkFields).Concat(HawkVisualFields).Concat(FactoryFields).Concat(CrowdFields).Concat(new[]{AccruedCreditField,
            new PlayableProfileField("artillery.baseBlastRadiusMultiplier","Ракета и взрыв · shkval-blast-upgrade-v1","Радиус базового Шквала · множитель","Доля полного радиуса взрыва до Наведения. После исследования используется полный радиус. Фиксируется при запуске ракеты.","×",0.1,1,0.01,p=>p.shkvalBaseBlastRadiusMultiplier,nameof(PlayableProfileData.shkvalBaseBlastRadiusMultiplier)),
            new PlayableProfileField("artillery.directHitDamage","Юниты · Шквал · shkval-direct-hit-v1","Урон прямого попадания","Итоговый базовый урон физически задетому телу вместо площадного; множитель оружия и округление применяются при запуске.","HP",0,1000,1,p=>p.shkvalDirectHitDamage,nameof(PlayableProfileData.shkvalDirectHitDamage)),
            new PlayableProfileField("combat.default.pursuitSeconds","Базовый бой · unit-default-combat-v1","Время преследования","Предел короткой погони перед возвратом на исходную позицию. Смена цели не продлевает время.","с",0,10,.1,p=>p.defaultCombatPursuitSeconds,nameof(PlayableProfileData.defaultCombatPursuitSeconds)),
            new PlayableProfileField("research.reinforcedConcrete.level2.cost","Усиленный бетон · reinforced-concrete-v2","Цена · уровень II","Параметр второго уровня бетона; бонус заменяет предыдущий суммарный эффект.","credits",0,10000,1,p=>p.reinforcedConcreteLevel2Cost,nameof(PlayableProfileData.reinforcedConcreteLevel2Cost)),
            new PlayableProfileField("research.reinforcedConcrete.level2.seconds","Усиленный бетон · reinforced-concrete-v2","Длительность · уровень II","Параметр второго уровня бетона; бонус заменяет предыдущий суммарный эффект.","с",0.5,300,0.5,p=>p.reinforcedConcreteLevel2Seconds,nameof(PlayableProfileData.reinforcedConcreteLevel2Seconds)),
            new PlayableProfileField("research.reinforcedConcrete.level2.bonusPercent","Усиленный бетон · reinforced-concrete-v2","Суммарный бонус · уровень II","Параметр второго уровня бетона; бонус заменяет предыдущий суммарный эффект.","%",0,300,1,p=>p.reinforcedConcreteLevel2BonusPercent,nameof(PlayableProfileData.reinforcedConcreteLevel2BonusPercent)),
            new PlayableProfileField("research.reinforcedConcrete.cost","Усиленный бетон · reinforced-concrete-v2","Цена · уровень I","Списание при старте исследования усиленного бетона I.","credits",0,5000,10,p=>p.reinforcedConcreteCost,nameof(PlayableProfileData.reinforcedConcreteCost)),
            new PlayableProfileField("research.reinforcedConcrete.seconds","Усиленный бетон · reinforced-concrete-v2","Время · уровень I","Длительность исследования бетона I в обычном матче.","с",0.5,300,0.5,p=>p.reinforcedConcreteSeconds,nameof(PlayableProfileData.reinforcedConcreteSeconds)),
            new PlayableProfileField("research.reinforcedConcrete.bonusPercent","Усиленный бетон · reinforced-concrete-v2","Суммарный бонус · уровень I","Увеличение максимального HP всех зданий владельца с сохранением процента здоровья.","%",0,500,1,p=>p.reinforcedConcreteBonusPercent,nameof(PlayableProfileData.reinforcedConcreteBonusPercent)),new PlayableProfileField("provingGround.actionSeconds","Полигон · balance-proving-ground-v1","Длительность действий","Время строительства, производства, исследований и улучшений только на полигоне. Выпуск юнитов сохраняет обычные физические ограничения.","с",.1,60,.1,p=>p.provingGroundActionSeconds,nameof(PlayableProfileData.provingGroundActionSeconds)),new PlayableProfileField("provingGround.attackRearLaneOffset","Полигон · balance-proving-ground-v1","Боковой отступ цели атаки за штабом","Цель остаётся позади вражеского штаба; боковой отступ открывает прямой подход между штабом и форпостом. Ноль требует обхода штаба обычной навигацией.","м",0,25,1,p=>p.provingGroundAttackRearLaneOffset,nameof(PlayableProfileData.provingGroundAttackRearLaneOffset)),new PlayableProfileField("provingGround.dividerWidth","Полигон · balance-proving-ground-v1","Ширина средней линии","Ширина визуальной границы половин полигона; не создаёт препятствий.","м",.05,1,.05,p=>p.provingGroundDividerWidth,nameof(PlayableProfileData.provingGroundDividerWidth))}).ToArray();
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
