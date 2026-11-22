using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private sealed class NuclearStrike
        {
            internal long Id;
            internal PlayableOwner Owner;
            internal NavLocation Center;
            internal double Start, WarningEnd, ImpactDue, ImpactTime = -1, EffectSeconds, Radius, Damage;
            internal bool Committed;
            internal OrbitalRepairRim[] Rim;
            internal readonly Dictionary<PlayableOwner,double> FirstObserved = new Dictionary<PlayableOwner,double>();
        }

        private readonly List<NuclearStrike> nuclearStrikes = new List<NuclearStrike>();

        private PlayableCommandStatus UseNuclearStrike(PlayableOwner owner, PlayableCommand command)
        {
            if (eliminated.Contains(owner) || !Orbital(owner).CanUse(OrbitalAbilities.NuclearStrike))
                return PlayableCommandStatus.Rejected;

            RefreshVision();
            if (!OrbitalRepairNanobots.TryLocate(profile, command.Target, command.TargetLocation, out var center) ||
                !Vision(owner).IsVisible(center.Position))
                return PlayableCommandStatus.InvalidTarget;

            var data = OrbitalData;
            var strike = new NuclearStrike
            {
                Id = (Tick << 8) | (byte)owner,
                Owner = owner, Center = center, Start = elapsed,
                WarningEnd = elapsed + data.orbitalNuclearWarningSeconds,
                ImpactDue = elapsed + data.orbitalNuclearWarningSeconds + data.orbitalNuclearFlightSeconds,
                Radius = data.orbitalNuclearRadius, Damage = data.orbitalNuclearDamage,
                EffectSeconds = data.orbitalNuclearEffectSeconds,
                Rim = OrbitalRepairNanobots.Rim(profile, center, data.orbitalNuclearRadius)
            };
            if (command.Origin == PlayableOrderOrigin.Ai && !CanAiConfirmNuclearStrike(strike))
                return PlayableCommandStatus.InvalidTarget;
            Orbital(owner).Use(OrbitalAbilities.NuclearStrike, data);
            nuclearStrikes.Add(strike);
            foreach(var recipient in Owners.Where(recipient=>recipient!=owner&&!eliminated.Contains(recipient)))
                MatchEvent(MatchEventKind.NuclearLaunch,recipient,default,strike.Id+1);
            return PlayableCommandStatus.Applied;
        }

        private bool CanAiConfirmNuclearStrike(NuclearStrike strike)
        {
            var observation = PlayableAiObservation.From(PlayerSnapshot(0,RuntimeStatus.Running,false,
                new PlayableRuntimeMetrics(0,0,0,0,0),null,0,strike.Owner));
            if (AiOrbitalPolicy.NuclearTargetCount(observation,profile,OrbitalData,strike.Center.Position) <
                OrbitalData.orbitalNuclearMinimumTargets) return false;
            double seconds = strike.ImpactDue - elapsed;
            foreach (var unit in units.Values)
            {
                if (Hostile(strike.Owner,unit.Owner) || !TryActor(unit.Id,out var actor)) continue;
                var predicted = new NavPoint(actor.Position.X + unit.Velocity.X * seconds,
                    actor.Position.Z + unit.Velocity.Z * seconds);
                double? height = unit.Air == null ? null : (double?)(unit.Air.Height + HawkBodyHeight / 2);
                if (InsideRepairField(unit,actor,strike.Center,strike.Radius) ||
                    OrbitalRepairNanobots.Contains(profile.AuthoredMap,strike.Center,strike.Radius,predicted,null,height))
                    return false;
            }
            return true;
        }

        private void AdvanceNuclearStrikes()
        {
            for (int i = 0; i < nuclearStrikes.Count;)
            {
                var strike = nuclearStrikes[i];
                if (strike.ImpactTime >= 0)
                {
                    if (elapsed >= strike.ImpactTime + strike.EffectSeconds) { nuclearStrikes.RemoveAt(i); continue; }
                }
                else
                {
                    if (!strike.Committed)
                    {
                        if (!Vision(strike.Owner).IsVisible(strike.Center.Position)) { nuclearStrikes.RemoveAt(i); continue; }
                        if (elapsed + 1e-9 >= strike.WarningEnd)
                        {
                            strike.Committed = true;
                            if (elapsed + 1e-9 < strike.ImpactDue)
                                Sound(PlayableSoundKind.OrbitalNuclearDescent, strike.Center.Position,
                                    PlayableEntityKind.Shkval, phaseEndTick: AudioPhaseEnd(strike.ImpactDue - elapsed));
                        }
                    }
                    if (strike.Committed && elapsed + 1e-9 >= strike.ImpactDue && ImpactNuclearStrike(strike))
                        strike.ImpactTime = elapsed;
                }
                i++;
            }
        }

        private bool NuclearBuildingInside(NuclearStrike strike, Building building) =>
            building.Phase != ConstructionPhase.Pending && building.Health > 0 &&
            OrbitalRepairNanobots.Contains(profile.AuthoredMap, strike.Center, strike.Radius, building.Position, null, null);

        private bool NuclearUnitInside(NuclearStrike strike, Unit unit, out NavUnit actor) =>
            TryActor(unit.Id, out actor) && !Frozen(unit) && InsideRepairField(unit, actor, strike.Center, strike.Radius);

        private bool ImpactNuclearStrike(NuclearStrike strike)
        {
            var victims = new List<int>();
            var prospective = new Dictionary<int, PlayableWreckSnapshot>();
            var removals = new List<Building>();
            foreach (var unit in units.Values.OrderBy(unit => unit.Id))
            {
                if (!NuclearUnitInside(strike, unit, out var actor)) continue;
                victims.Add(unit.Id);
                if (unit.Health <= strike.Damage) prospective.Add(unit.Id, CreateUnitWreck(unit, actor));
            }
            foreach (var building in buildings.Values.OrderBy(building => building.Id))
            {
                if (!NuclearBuildingInside(strike, building)) continue;
                victims.Add(building.Id);
                if (building.Health <= strike.Damage) removals.Add(building);
            }

            bool geometryChange = removals.Count != 0 || prospective.Values.Any(wreck => wreck.Blocks(Tick));
            if (geometryChange)
            {
                var removedActors = prospective.Values.Where(wreck => wreck.Kind != PlayableEntityKind.Hawk)
                    .Select(wreck => wreck.Id).ToArray();
                if (!TryRebuildGeometry(removals:removals, additionalWrecks:prospective.Values,
                    removedActors:removedActors.Length == 0 ? null : removedActors))
                    return false;
            }
            foreach (int id in victims)
                TryDamagePrepared(id, strike.Damage, 0, strike.Owner, null, default, null, prospective, uniformDamage:true);
            Sound(PlayableSoundKind.OrbitalNuclearImpact, strike.Center.Position, PlayableEntityKind.Shkval);
            if (geometryChange) { ReconcileWreckRoutes(); RetryWreckWaits(); }
            return true;
        }

        private OrbitalNuclearSnapshot[] ProjectNuclearStrikes(PlayableOwner owner, PlayableVision vision)
        {
            if (nuclearStrikes.Count == 0) return Array.Empty<OrbitalNuclearSnapshot>();
            return nuclearStrikes.Select(strike =>
            {
                bool impact = strike.ImpactTime >= 0;
                bool allied = owner != strike.Owner && !Hostile(owner,strike.Owner) && !eliminated.Contains(owner);
                bool seeCenter = vision.IsVisible(strike.Center.Position);
                var phase = impact ? OrbitalNuclearPhase.Impact :
                    strike.Committed ? OrbitalNuclearPhase.Descent : OrbitalNuclearPhase.Warning;
                double progress = impact ? (elapsed - strike.ImpactTime) / strike.EffectSeconds :
                    strike.Committed ? (elapsed - strike.WarningEnd) / (strike.ImpactDue - strike.WarningEnd) :
                    (elapsed - strike.Start) / (strike.WarningEnd - strike.Start);
                var rim = impact ? Array.Empty<OrbitalRepairRim>() : strike.Rim.Where(segment =>
                    vision.IsVisible(segment.From.Position) && vision.IsVisible(segment.To.Position) &&
                    vision.IsVisible(new NavPoint((segment.From.Position.X+segment.To.Position.X)/2,
                        (segment.From.Position.Z+segment.To.Position.Z)/2))).ToArray();
                return new OrbitalNuclearSnapshot(strike.Id, seeCenter || allied&&!impact ? strike.Center : (NavLocation?)null,
                    allied&&!impact, phase, strike.Radius, Math.Max(0,Math.Min(1,progress)),rim);
            }).ToArray();
        }

        private void WriteNuclearStrikes(BinaryWriter writer)
        {
            writer.Write(nuclearStrikes.Count);
            foreach (var strike in nuclearStrikes)
            {
                writer.Write(strike.Id);
                writer.Write((int)strike.Owner);
                WorldWire.Number(writer,strike.Center.Position.X);
                WorldWire.Number(writer,strike.Center.Position.Z);
                WorldWire.String(writer,strike.Center.SurfaceId);
                WorldWire.Number(writer,strike.Start);
                WorldWire.Number(writer,strike.WarningEnd);
                WorldWire.Number(writer,strike.ImpactDue);
                WorldWire.Number(writer,strike.ImpactTime);
                WorldWire.Number(writer,strike.EffectSeconds);
                WorldWire.Number(writer,strike.Radius);
                WorldWire.Number(writer,strike.Damage);
                writer.Write(strike.Committed);
                writer.Write(strike.FirstObserved.Count);
                foreach (var entry in strike.FirstObserved.OrderBy(entry => (int)entry.Key))
                {
                    writer.Write((int)entry.Key);
                    WorldWire.Number(writer,entry.Value);
                }
            }
            WriteNuclearEvacuations(writer);
            foreach (var pair in orbital.OrderBy(entry => (int)entry.Key))
                WorldWire.Number(writer,pair.Value.ProvingGroundNuclearRecovery);
        }

        private void ReadNuclearStrikes(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 10000) throw new ArgumentException("Invalid nuclear strike count.");
            for (int i = 0; i < count; i++)
            {
                long id = reader.ReadInt64();
                var owner = WorldWire.EnumValue<PlayableOwner>(reader);
                var point = new NavPoint(WorldWire.Number(reader),WorldWire.Number(reader));
                var center = new NavLocation(point,WorldWire.String(reader));
                var strike = new NuclearStrike
                {
                    Id=id, Owner=owner, Center=center, Start=WorldWire.Number(reader),
                    WarningEnd=WorldWire.Number(reader), ImpactDue=WorldWire.Number(reader),
                    ImpactTime=WorldWire.Number(reader), EffectSeconds=WorldWire.Number(reader),
                    Radius=WorldWire.Number(reader), Damage=WorldWire.Number(reader), Committed=WorldWire.Boolean(reader)
                };
                if (!HasOwner(owner) || id < 0 || nuclearStrikes.Any(other => other.Id == id) ||
                    strike.Start < 0 || strike.Start > elapsed || strike.WarningEnd <= strike.Start ||
                    strike.ImpactDue <= strike.WarningEnd || strike.ImpactTime > elapsed ||
                    strike.ImpactTime != -1 && (!strike.Committed || strike.ImpactTime+1e-9 < strike.ImpactDue) ||
                    strike.EffectSeconds <= 0 || strike.Radius <= 0 || strike.Damage <= 0 ||
                    !OrbitalRepairNanobots.TryLocate(profile,point,center,out _))
                    throw new ArgumentException("Invalid nuclear strike.");
                int observers=reader.ReadInt32();
                if (observers<0 || observers>Owners.Count()) throw new ArgumentException("Invalid strike observers.");
                for (int j=0;j<observers;j++)
                {
                    var observer=WorldWire.EnumValue<PlayableOwner>(reader);
                    double observed=WorldWire.Number(reader);
                    if (!HasOwner(observer) || observed<strike.Start || observed>elapsed)
                        throw new ArgumentException("Invalid strike observation.");
                    strike.FirstObserved.Add(observer,observed);
                }
                strike.Rim=OrbitalRepairNanobots.Rim(profile,center,strike.Radius);
                nuclearStrikes.Add(strike);
            }
            ReadNuclearEvacuations(reader);
            foreach (var pair in orbital.OrderBy(entry => (int)entry.Key))
                pair.Value.RestoreProvingGroundNuclear(WorldWire.Number(reader));
        }
    }
}
