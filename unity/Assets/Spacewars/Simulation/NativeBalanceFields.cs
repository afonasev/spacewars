using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Spacewars.Simulation.Ai;

namespace Spacewars.Simulation
{
    // The laboratory edits only governed current-native rules, never map/topology.
    public static class NativeBalanceFields
    {
        // AI has an independent revision and numeric DTO; gameplay writers never mutate it.
        public static IReadOnlyList<AiProfileField> AiFields=>AiProfileMetadata.Fields;
        public static IReadOnlyList<AiProfileField> AiOpeningFields=>AiProfileMetadata.Fields
            .Where(f=>AiProfile.IsDefaultExtensionField(f.Path)||f.Path.StartsWith("openingsMidgame.",StringComparison.Ordinal)||f.Path.StartsWith("positionDevelopment.",StringComparison.Ordinal)||f.Path.StartsWith("activeStrategy.",StringComparison.Ordinal)||f.Path.StartsWith("safeReinforcements.",StringComparison.Ordinal)||
                f.Path.StartsWith("difficulty.",StringComparison.Ordinal)&&(f.Path.Contains(".control.")||f.Path.Contains(".micro.")||f.Path.EndsWith(".reactionSeconds",StringComparison.Ordinal)))
            .ToArray();
        public static AiProfile InitialAiOpeningProfile()
        {var data=AiProfile.Initial.CopyData();data.id="ai-openings-midgame-v1";return new AiProfile(data);}
        public static IReadOnlyList<AiProfileField> AiRosterFields=>AiRosterCatalog.Metadata;
        public static void Write(AiProfileField descriptor,AiProfileData data,double value)
        {
            if(descriptor==null||data?.fields==null||!ReferenceEquals(AiProfileMetadata.Field(descriptor.Path),descriptor))throw new ArgumentException("Unregistered AI field.");
            descriptor.Validate(value);var field=data.fields.Single(f=>f.path==descriptor.Path);field.value=value;
            // Cross-field validation occurs when the immutable candidate snapshot is constructed.
        }
        public static void Write(AiProfileField descriptor,AiRosterDescriptorData[] data,double value)
        {
            if(descriptor==null||data==null||!AiRosterFields.Contains(descriptor))throw new ArgumentException("Unregistered roster field.");
            descriptor.Validate(value);var parts=descriptor.Path.Split('.');
            var target=data.Single(d=>d.kind.ToString()==parts[1]);
            typeof(AiRosterDescriptorData).GetField(parts[2]).SetValue(target,value);
        }
        private static readonly Dictionary<string,FieldInfo> Fields=typeof(PlayableProfileData).GetFields().ToDictionary(f=>f.Name);
        public static PlayableProfileData Copy(PlayableProfileData source)
        =>source.CopyFields();
        public static void Write(PlayableProfileField descriptor,PlayableProfileData data,double value)
        {
            // Zero is absence of the new extension in an explicit exact profile.
            // Preserve writing that unchanged old field; it is not a quality setting.
            if(descriptor.FieldName==nameof(PlayableProfileData.groupCorridorMaximumExcessPercent)&&data.groupCorridorMaximumExcessPercent==0&&value==0)return;
            if(double.IsNaN(value)||double.IsInfinity(value)||value<descriptor.Minimum||value>descriptor.Maximum)throw new ArgumentOutOfRangeException(descriptor.Path);
            if(!Fields.TryGetValue(descriptor.FieldName??"",out var field))throw new InvalidOperationException("Missing native field writer: "+descriptor.Path);
            if(field.FieldType==typeof(int)){if(value!=Math.Truncate(value))throw new ArgumentException("Введите целое число.");field.SetValue(data,(int)value);}
            else field.SetValue(data,value);
        }
        public static bool Editable(PlayableProfileField field)
        {
            if(ReferenceEquals(field,PlayableProfileMetadata.AccruedCreditField))return true;
            string name=field.FieldName??"";
            // Retain retired fields in profile bindings and saves, but omit their controls from the lab.
            if(name=="shkvalLeadSpeedThreshold"||name=="shkvalLeadFalloff"||name=="shkvalMaxLeadTimeSec"||name=="shkvalFriendlyFirePenalty"||name=="provingGroundAttackRearLaneOffset")return false;
            if(name.StartsWith("orbitalMine",StringComparison.Ordinal))return true; // New placements capture independent mine terms.
            // Maneuver capability is bound at genesis/current capture; changes require a new match.
            if(name.StartsWith("tankReverse",StringComparison.Ordinal))return false;
            if(field.Group=="Arena"||field.Group=="Slots"||field.Group=="Minimap"||field.Group=="Fog presentation"||field.Group=="Camera"||field.Group=="Navigation"||field.Group=="Rendering"||field.Group=="Input"||field.Group=="Presentation")return false;
            if(name.StartsWith("minimap")||name.StartsWith("fog")||name=="visionCellSize"||name=="startingCredits"||name=="matchScoreEarnedCreditsDivisor"||name=="ballisticWallHeight"||name=="factoryExitDistance"||name=="defaultRallyDistance"||name=="evacuationClearance"||name=="shkvalBuildingCollisionHeight"||name=="shkvalProjectileRadius")return false;
            if(name=="shkvalMarkerStartRadius"||name=="shkvalMarkerOpacity")return true;
            if(name.Contains("Marker")||name.Contains("Tracer")||name.Contains("Muzzle")||name=="shkvalArcHeight"||name=="shkvalLaunchHeight")return false;
            if(name=="turretPadOffset"||name=="turretPassageWidth")return false; // Plot topology is fixed at genesis, like SlotRingRadius.
            if(name=="hawkRadius")return false; // Immutable physical envelope, like the other unit collision radii.
            if(name.Contains("CollisionRadius")||name.Contains("FootprintRadius")||name.Contains("ModelRadius")||name.Contains("ModelScale")||name.Contains("ModelHeight"))return false;
            return true;
        }
        // Controls are narrower than profile compatibility and live-apply bindings.
        // Historical revisions keep their authored values when a control is retired.
        public static bool LabEditable(PlayableProfileField field)
        {
            if(!Editable(field))return false;
            string path=field.Path;
            if(path.StartsWith("presentation.",StringComparison.Ordinal))
                return path=="presentation.unitWreck.lifetimeSeconds";
            if(path.StartsWith("wrecks.",StringComparison.Ordinal))
                return path is "wrecks.spawnProtectionSeconds" or "wrecks.wreckSinkSeconds" or "wrecks.hawkWreckChancePercent" or "wrecks.skatWreckChancePercent" or "wrecks.hawkWreckFallSeconds";
            if(path.StartsWith("crowdMotion.",StringComparison.Ordinal)||path.StartsWith("navigation.",StringComparison.Ordinal)||
                path.StartsWith("combatRounding.",StringComparison.Ordinal)||path.StartsWith("unitUpgrades.",StringComparison.Ordinal))
                return false;
            if(path.StartsWith("teamSignals.",StringComparison.Ordinal))
                return path is "teamSignals.lifetimeSeconds" or "teamSignals.maximumPerAuthor" or "teamSignals.repeatRadius";
            string name=field.FieldName??"";
            if(name.StartsWith("orbital",StringComparison.Ordinal))
            {
                if(name.StartsWith("orbitalMine",StringComparison.Ordinal))
                    return name is "orbitalMineCapacity" or "orbitalMineVision" or "orbitalMineDirectDamage" or "orbitalMineSplashDamage" or "orbitalMineBlastRadius" or "orbitalMineModelRadius" or "orbitalMineEnemyClearanceMeters";
                if(name.StartsWith("orbitalScan",StringComparison.Ordinal))
                    return name is "orbitalScanRadius" or "orbitalScanSeconds";
                return !name.StartsWith("orbitalUi",StringComparison.Ordinal)&&!name.StartsWith("orbitalCard",StringComparison.Ordinal)&&
                    name is not ("orbitalPanelWidth" or "orbitalIconSize" or "orbitalLevelFontSize" or "orbitalFinalSpeed");
            }
            if(name.StartsWith("hawkDebris",StringComparison.Ordinal)||name.StartsWith("shkvalExhaust",StringComparison.Ordinal)||
                name.StartsWith("aiArmySystems",StringComparison.Ordinal)&&name.EndsWith("UtilityScale",StringComparison.Ordinal))
                return false;
            return name is not (
                "gaussCostMultiplier" or "gaussHealthMultiplier" or "shkvalBaseBlastRadiusMultiplier" or "shkvalRange" or
                "gaussChargeSeconds" or "gaussChargeIntensity" or "gaussRecoilMeters" or "gaussRecoilSeconds" or
                "gaussRecoilPeakFraction" or "gaussBeamGlowWidth" or "gaussBeamRiseFraction" or "gaussBeamSeconds" or "gaussBeamIntensity" or
                "turretLaserWidth" or "turretLaserIntensity" or "turretTurningSpread" or
                "hawkMissileTurn" or "hawkUpgradedReloadSeconds" or "hawkSelectionRadius" or "hawkRotorSpeed" or "hawkCruisePitch" or "hawkAccelerationPitch" or "hawkBank" or
                "hawkAttitudeSpeed" or "hawkMissileLength" or "hawkMissileVisualScale" or "tankChassisGlowIntensity" or
                "refineryTurbineSpeed" or "rallyFlagHeight" or "saleMarkerScale" or "padBorderRatio" or "capturePulseHz" or
                "provingGroundDividerWidth" or "shkvalMarkerStartRadius" or "shkvalMarkerOpacity" or "shkvalImpactEffectRadius" or
                "incomeSettlementSeconds" or "attackRepathSeconds" or
                "constructionInitialHealthRatio" or "constructionDamageMultiplier" or "constructionVisionMultiplier" or
                "buildingRepairCostRatio" or "idleAutoDefenseMultiplier" or "attackApproachRangeRatio" or
                "projectileExtraRangePercent" or "upgradedProducerSpeedMultiplier");
        }

        public static IReadOnlyList<AiProfileField> AiLabFields {get;}=AiOpeningFields.Where(field=>
            field.Unit!="weight"&&!field.Path.EndsWith("Multiplier",StringComparison.Ordinal)&&
            !field.Path.EndsWith("Scale",StringComparison.Ordinal)&&
            field.Path is not ("positionDevelopment.towerValue" or "positionDevelopment.concreteValue")&&
            field.Path!="difficulty.expert.incomeBonusPercent"&&
            !field.Path.EndsWith(".control.fairRun",StringComparison.Ordinal)&&
            !field.Path.EndsWith(".micro.focusAttackers",StringComparison.Ordinal)&&
            !field.Path.EndsWith(".micro.lossPerSecond",StringComparison.Ordinal)).ToArray();

        public static string ValidateLiveDifference(PlayableProfile before,PlayableProfile after)
        {
            var a=before.CopyData();var b=after.CopyData();
            if(a.groundRouteBackend!=b.groundRouteBackend)return "Навигационный backend задаётся при создании матча.";
            foreach(var field in PlayableProfileMetadata.Fields)
                if(!Editable(field)&&field.Read(a)!=field.Read(b))return "Параметр не доступен в лаборатории: "+field.Path;
            if(!ReferenceEquals(before.AuthoredMap,after.AuthoredMap))return "Карта не может изменяться в лаборатории.";
            return null;
        }
    }
}
