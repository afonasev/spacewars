using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    internal static class RepairEffectStyle
    {
        internal static readonly Color Green = new Color(.44f, 1, .62f);
        internal const int EmissionTicks = 18, LifeTicks = 24;
        internal const float RisePixels = 18, SpreadPixels = 13, SignRadius = 4;
        internal const int LightGroups = 3, LightsPerGroup = 5, RimCycleTicks = 120;
        internal const float RimLightSpacing = .009f;

        internal static void DrawPlus(Painter2D painter, Vector2 point, float size, float width, float opacity)
        {
            var tint = Green;
            tint.a = opacity;
            painter.strokeColor = tint;
            painter.lineWidth = width;
            painter.BeginPath();
            painter.MoveTo(point - Vector2.right * size);
            painter.LineTo(point + Vector2.right * size);
            painter.MoveTo(point - Vector2.up * size);
            painter.LineTo(point + Vector2.up * size);
            painter.Stroke();
        }
    }

    public sealed partial class PlayableBootstrap
    {
        private bool RepairNanobotsTargeting => scanTargetAbility == OrbitalAbilities.RepairNanobots;

        private readonly List<(int Unit, long Birth, float Offset)> repairSigns =
            new List<(int Unit, long Birth, float Offset)>();
        private readonly Dictionary<int, long> lastRepairSign = new Dictionary<int, long>();
        private long repairSignGeneration = -1, repairSignTick = -1;
        private NavLocation? cachedRepairCenter;
        private PlayableProfile cachedRepairProfile;
        private OrbitalRepairRim[] cachedRepairRim;

        private bool TryRepairTarget(NavPoint point, out NavLocation center)
        {
            var map = profile.AuthoredMap;
            var mouse = input?.AssignedMouse ?? Mouse.current;
            Vector2 cursor = scanTargetPad ? battleCursor : mouse != null ? mouse.position.ReadValue() : SeatScreenCenter;
            if (map != null && !mapOpen && MapAt(cursor) == 0 && cameraView != null)
            {
                var ray = cameraView.ScreenPointToRay(cursor);
                var hit = GroundFromRay(ray, out double distance);
                if (hit.Equals(point) && !double.IsInfinity(distance))
                {
                    double y = ray.GetPoint((float)distance).y;
                    foreach (var support in map.Supports)
                    {
                        if (support.SurfaceId == null || !support.Contains(point) ||
                            System.Math.Abs(support.HeightAt(point) - y) > .01)
                            continue;
                        var location = new NavLocation(point, support.SurfaceId);
                        if (OrbitalRepairNanobots.TryLocate(profile,point,location,out center)) return true;
                    }
                }
            }
            return OrbitalRepairNanobots.TryLocate(profile,point,null,out center);
        }

        private IEnumerable<PlayableEntitySnapshot> RepairPreviewUnits(NavLocation center) =>
            view.Entities.Where(unit => !view.IsHostile(unit.Owner) && view.Vision.IsVisible(unit.Position) &&
                OrbitalRepairNanobots.Wounded(profile,unit) &&
                OrbitalRepairNanobots.Contains(profile,center,TargetPreviewRadius,unit));

        private IEnumerable<PlayableBuildingSnapshot> RepairPreviewBuildings(NavLocation center) =>
            view.Buildings.Where(building => !view.IsHostile(building.Owner) && view.Vision.IsVisible(building.Position) &&
                building.Phase == ConstructionPhase.Ready && building.Health > 0 &&
                building.Health < TerritoryRules.MaximumHealth(profile, building) &&
                building.PrivateState?.Lifecycle?.Selling != true &&
                OrbitalRepairNanobots.Contains(profile.AuthoredMap, center, TargetPreviewRadius, building.Position, null, null));

        private void UpdateRepairSigns()
        {
            if (generation != repairSignGeneration || view.Tick < repairSignTick)
            {
                repairSigns.Clear();
                lastRepairSign.Clear();
                repairSignGeneration = generation;
                repairSignTick = -1;
            }
            if (view.Tick == repairSignTick) return;
            repairSignTick = view.Tick;
            repairSigns.RemoveAll(sign => view.Tick - sign.Birth >= RepairEffectStyle.LifeTicks);
            foreach (int id in view.Orbital.RepairedUnits)
            {
                if (lastRepairSign.TryGetValue(id,out long last) && view.Tick - last < RepairEffectStyle.EmissionTicks)
                    continue;
                uint seed = unchecked((uint)id * 2654435761u + (uint)view.Tick * 747796405u);
                float offset = ((seed >> 16) / 65535f * 2 - 1) * RepairEffectStyle.SpreadPixels;
                repairSigns.Add((id,view.Tick,offset));
                lastRepairSign[id] = view.Tick;
            }
            if (lastRepairSign.Count > 0 && view.Tick % RepairEffectStyle.LifeTicks == 0)
            {
                foreach (int expired in lastRepairSign.Where(pair => view.Tick - pair.Value >= RepairEffectStyle.LifeTicks)
                    .Select(pair => pair.Key).ToArray())
                    lastRepairSign.Remove(expired);
            }
        }

        private Vector2 RepairPixel(Vector3 point)
        {
            var pixel = cameraView.WorldToViewportPoint(point);
            return new Vector2(pixel.x * root.contentRect.width,(1 - pixel.y) * root.contentRect.height);
        }

        private void DrawRepairRim(Painter2D painter, IEnumerable<OrbitalRepairRim> segments, Color color)
        {
            var map = profile.AuthoredMap;
            Vector2 Project(NavLocation at) => RepairPixel(new Vector3((float)at.Position.X,
                (float)OrbitalRepairNanobots.Height(map,at),(float)at.Position.Z));
            painter.strokeColor = color;
            painter.lineWidth = (float)ScanData.orbitalMinePreviewWidth;
            painter.BeginPath();
            foreach (var segment in segments)
            {
                painter.MoveTo(Project(segment.From));
                painter.LineTo(Project(segment.To));
            }
            painter.Stroke();
        }

        private void DrawMovingRepairLights(Painter2D painter)
        {
            var terrain = profile.AuthoredMap;
            float phase = (view.Tick % RepairEffectStyle.RimCycleTicks) / (float)RepairEffectStyle.RimCycleTicks;
            foreach (var segment in view.Orbital.RepairRims)
            {
                for (int group = 0; group < RepairEffectStyle.LightGroups; group++)
                for (int tail = 0; tail < RepairEffectStyle.LightsPerGroup; tail++)
                {
                    float turn = Mathf.Repeat(phase + group / (float)RepairEffectStyle.LightGroups -
                        tail * RepairEffectStyle.RimLightSpacing,1);
                    float step = turn * OrbitalRepairRim.SegmentCount - segment.Segment;
                    if (step < 0 || step >= 1) continue;
                    Vector2 Project(NavLocation at) => RepairPixel(new Vector3((float)at.Position.X,
                        (float)OrbitalRepairNanobots.Height(terrain,at),(float)at.Position.Z));
                    var at = Vector2.Lerp(Project(segment.From),Project(segment.To),step);
                    var tint = RepairEffectStyle.Green;
                    tint.a = 1 - tail * .16f;
                    painter.fillColor = tint;
                    painter.BeginPath();
                    painter.Arc(at,2.8f - tail * .35f,0,360);
                    painter.Fill();
                }
            }
        }

        private void DrawRepairEffects(Painter2D painter)
        {
            if (view.Orbital.RepairRims.Count > 0)
            {
                var tint = RepairEffectStyle.Green;
                tint.a = .62f + .13f * Mathf.Cos(view.Tick * (Mathf.PI * 2 / 48));
                DrawRepairRim(painter,view.Orbital.RepairRims,tint);
                DrawMovingRepairLights(painter);
            }
            foreach (var sign in repairSigns)
            {
                var unit = view.Entities.FirstOrDefault(entity => entity.Id == sign.Unit);
                var building = view.Buildings.FirstOrDefault(candidate => candidate.Id == sign.Unit);
                if ((unit == null && building == null) || !view.Vision.IsVisible(unit?.Position ?? building.Position) ||
                    !world.Actors.TryGetValue(sign.Unit,out var actor)) continue;
                float height = building != null ? (float)PlayableUnitRules.AirBuildingHeight(profile, building.Kind) :
                    PlayableUnitRules.Aircraft(unit.Kind) ? (float)PlayableUnitRules.AircraftBodyHeight(profile,unit.Kind) : .7f;
                var anchor = actor.Root.position + Vector3.up * height;
                var at = RepairPixel(anchor);
                float phase = (view.Tick - sign.Birth) / (float)RepairEffectStyle.LifeTicks;
                at += new Vector2(sign.Offset,-5 - RepairEffectStyle.RisePixels * phase);
                RepairEffectStyle.DrawPlus(painter,at,RepairEffectStyle.SignRadius,2,Mathf.Clamp01((1-phase)*2));
            }
        }

        private void DrawRepairPreview(Painter2D painter, NavPoint point)
        {
            if (!TryRepairTarget(point,out var center)) return;
            if (!cachedRepairCenter.HasValue || !cachedRepairCenter.Value.Equals(center) ||
                !ReferenceEquals(cachedRepairProfile,profile))
            {
                cachedRepairCenter = center;
                cachedRepairProfile = profile;
                cachedRepairRim = OrbitalRepairNanobots.Rim(profile,center,TargetPreviewRadius);
            }
            DrawRepairRim(painter,cachedRepairRim,abilityTargetValid ? RepairEffectStyle.Green : InvalidOrbitalPreview);
            if (!abilityTargetValid) return;
            foreach (var building in RepairPreviewBuildings(center))
            {
                if (!world.Actors.TryGetValue(building.Id, out var actor)) continue;
                var at = RepairPixel(actor.Root.position + Vector3.up * (float)PlayableUnitRules.AirBuildingHeight(profile, building.Kind));
                RepairEffectStyle.DrawPlus(painter, at, RepairEffectStyle.SignRadius, 2, .8f);
            }
            foreach (var unit in RepairPreviewUnits(center))
            {
                if (!world.Actors.TryGetValue(unit.Id,out var actor)) continue;
                var at = RepairPixel(actor.Root.position + Vector3.up * .6f);
                RepairEffectStyle.DrawPlus(painter,at,RepairEffectStyle.SignRadius,2,.8f);
            }
        }
    }
}
