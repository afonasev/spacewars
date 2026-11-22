using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    public enum OrbitalNuclearPhase { Warning, Descent, Impact }

    public sealed class OrbitalNuclearSnapshot
    {
        public readonly long Id;
        public readonly NavLocation? Center;
        public readonly bool AlliedSignal;
        public readonly OrbitalNuclearPhase Phase;
        public readonly double Radius, Progress;
        public readonly IReadOnlyList<OrbitalRepairRim> VisibleRim;

        public OrbitalNuclearSnapshot(long id, NavLocation? center, bool alliedSignal, OrbitalNuclearPhase phase,
            double radius, double progress, OrbitalRepairRim[] visibleRim)
        {
            Id = id;
            Center = center;
            AlliedSignal = alliedSignal;
            Phase = phase;
            Radius = radius;
            Progress = progress;
            VisibleRim = Array.AsReadOnly(visibleRim);
        }
    }

    public static class OrbitalNuclearRules
    {
        public static readonly IReadOnlyList<PlayableProfileField> Fields = new[]
        {
            Field(nameof(PlayableProfileData.orbitalNuclearRadius), "Радиус полусферы", "м", 1, 30, .1, p => p.orbitalNuclearRadius),
            Field(nameof(PlayableProfileData.orbitalNuclearDamage), "Урон по юнитам и зданиям", "HP", 1, 1000, .1, p => p.orbitalNuclearDamage),
            Field(nameof(PlayableProfileData.orbitalNuclearWarningSeconds), "Время сведения мишени", "с", .1, 20, .1, p => p.orbitalNuclearWarningSeconds),
            Field(nameof(PlayableProfileData.orbitalNuclearFlightSeconds), "Время спуска ракеты", "с", .1, 10, .1, p => p.orbitalNuclearFlightSeconds),
            Field(nameof(PlayableProfileData.orbitalNuclearMinimumTargets), "Минимум целей для ИИ", "целей", 1, 30, 1, p => p.orbitalNuclearMinimumTargets),
            Field(nameof(PlayableProfileData.orbitalNuclearCadetReaction), "Реакция кадета (−1 — выключена)", "с", -1, 15, .1, p => p.orbitalNuclearCadetReaction),
            Field(nameof(PlayableProfileData.orbitalNuclearFighterReaction), "Реакция бойца", "с", -1, 15, .1, p => p.orbitalNuclearFighterReaction),
            Field(nameof(PlayableProfileData.orbitalNuclearVeteranReaction), "Реакция ветерана", "с", -1, 15, .1, p => p.orbitalNuclearVeteranReaction),
            Field(nameof(PlayableProfileData.orbitalNuclearExpertReaction), "Реакция эксперта", "с", -1, 15, .1, p => p.orbitalNuclearExpertReaction),
            Field(nameof(PlayableProfileData.orbitalNuclearRocketScale), "Масштаб ракеты", "×", .5, 10, .1, p => p.orbitalNuclearRocketScale),
            Field(nameof(PlayableProfileData.orbitalNuclearDropHeight), "Высота начала спуска", "м", 2, 80, 1, p => p.orbitalNuclearDropHeight),
            Field(nameof(PlayableProfileData.orbitalNuclearReticleWidth), "Толщина линии мишени", "пкс", 1, 10, .1, p => p.orbitalNuclearReticleWidth),
            Field(nameof(PlayableProfileData.orbitalNuclearEffectSeconds), "Длительность эффекта взрыва", "с", .2, 5, .05, p => p.orbitalNuclearEffectSeconds),
            Field(nameof(PlayableProfileData.orbitalNuclearFlashStrength), "Яркость вспышки", "×", 0, 3, .1, p => p.orbitalNuclearFlashStrength),
            Field(nameof(PlayableProfileData.orbitalNuclearWaveScale), "Масштаб ударной волны", "×", .25, 2, .05, p => p.orbitalNuclearWaveScale),
            Field(nameof(PlayableProfileData.orbitalNuclearSmokeOpacity), "Непрозрачность дыма", "доля", 0, 1, .05, p => p.orbitalNuclearSmokeOpacity),
            Field(nameof(PlayableProfileData.orbitalNuclearVoiceGain), "Громкость оповещения о запуске", "×", 0, 2, .05, p => p.orbitalNuclearVoiceGain)
        };

        private static PlayableProfileField Field(string key,string name,string unit,double min,double max,double step,
            Func<PlayableProfileData,double> read) =>
            new PlayableProfileField("orbital."+key, "Эскалация / Ядерный удар", name, name, unit, min, max, step, read, key);
    }
}
