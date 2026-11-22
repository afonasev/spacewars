using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    internal static class ProductionBoostArtwork
    {
        internal static void Draw(Painter2D painter, Vector2 center, float radius)
        {
            painter.fillColor = new Color(1, .84f, .22f);
            painter.strokeColor = new Color(.34f, .19f, .035f, .9f);
            painter.lineWidth = Mathf.Max(1, radius * .12f);
            painter.BeginPath();
            painter.MoveTo(center + new Vector2(.27f, -1f) * radius);
            painter.LineTo(center + new Vector2(-.65f, .12f) * radius);
            painter.LineTo(center + new Vector2(-.07f, .12f) * radius);
            painter.LineTo(center + new Vector2(-.25f, 1f) * radius);
            painter.LineTo(center + new Vector2(.67f, -.14f) * radius);
            painter.LineTo(center + new Vector2(.1f, -.14f) * radius);
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }
    }

    public sealed partial class PlayableBootstrap
    {
        private readonly Dictionary<int, VisualElement> boostMarkers = new Dictionary<int, VisualElement>();

        private int BoostTargetAt(NavPoint point)
        {
            double Distance(NavPoint p) => (p.X - point.X) * (p.X - point.X) + (p.Z - point.Z) * (p.Z - point.Z);
            return view.Buildings.Where(b => b.Health > 0 && view.Vision.IsVisible(b.Position) &&
                    Distance(b.Position) <= Math.Pow(TerritoryRules.Radius(profile, b.Kind), 2))
                .OrderBy(b => Distance(b.Position)).ThenBy(b => b.Id).Select(b => b.Id).FirstOrDefault();
        }

        private string BoostTargetFeedback(int id, out bool valid)
        {
            valid = false;
            var b = view.Buildings.FirstOrDefault(candidate => candidate.Id == id);
            if (b == null) return "Наведитесь на доходное или производящее здание";
            if (!view.Vision.IsVisible(b.Position)) return "Цель должна быть в текущем обзоре";
            if (IsOpponent(b.Owner)) return "Выберите своё или союзное здание";
            bool income = OrbitalProductionBoost.Income(b.Kind);
            if (!income && !OrbitalProductionBoost.Production(b.Kind)) return "У этого здания нет подходящей выработки";
            if (b.BoostRemaining > 0) return $"Ускорение действует ещё {b.BoostRemaining:0.#} с";
            if (b.Phase != ConstructionPhase.Ready || b.BoostSelling) return "Здание недоступно";
            if (b.SystemsDisabled) return "Сбой систем";
            if (b.BoostRepairing) return "Здание уже ремонтируется";
            double floor = OrbitalProductionBoost.HealthFloor(b.Kind, ScanData);
            double health = (b.ExactHealth ?? b.Health) / TerritoryRules.MaximumHealth(profile, b);
            if (health <= floor + 1e-9) return $"Нужно больше {floor:P0} HP";
            valid = true;
            double repair = TerritoryRules.Cost(profile, b.Kind) * profile.BuildingRepairCostRatio * (1 - floor);
            string cost = $"до {floor:P0} HP · полный ремонт {repair:0.#} кр.";
            if (income)
            {
                double? reserve = b.Kind == PlayableBuildingKind.Mine
                    ? view.Sites.FirstOrDefault(s => s.Site.Id == b.SiteId)?.MineReserve : null;
                double extra = OrbitalProductionBoost.ExtraIncome(profile, ScanData, b.Kind, b.RefineryUpgraded, reserve);
                string recipient = b.Owner == view.Owner ? "вам" : "владельцу";
                return $"Около +{extra:0.#} кр. {recipient} · {cost}";
            }
            double scale = ScanData.orbitalBoostProductionMultiplier;
            double? work = b.BoostWorkRemaining;
            double eta = work.HasValue ? Math.Max(work.Value / scale, work.Value - ScanData.orbitalBoostSeconds * (scale - 1)) : 0;
            string order = work.HasValue ? $"текущая работа ≈ {eta:0.#} с" : "нет активной работы";
            return $"Работа очереди ×{scale:0.#} · {order} · {cost}";
        }

        private void UpdateProductionBoostMarkers()
        {
            if (boostMarkers.Count == 0 && !view.Buildings.Any(b => b.BoostRemaining > 0)) return;
            var visible = new HashSet<int>();
            foreach (var b in view.Buildings)
            {
                if (b.BoostRemaining <= 0 || !view.Vision.IsVisible(b.Position)) continue;
                visible.Add(b.Id);
                if (!boostMarkers.TryGetValue(b.Id, out var marker))
                {
                    marker = new VisualElement { name = "production-boost-lightning", pickingMode = PickingMode.Ignore };
                    marker.style.position = Position.Absolute;
                    var artwork = marker;
                    artwork.generateVisualContent += context =>
                        ProductionBoostArtwork.Draw(context.painter2D, artwork.contentRect.center, artwork.contentRect.height * .44f);
                    root.Insert(0, marker);
                    boostMarkers.Add(b.Id, marker);
                }
                float size = (float)profile.LifecycleMarkerPixels;
                Vector3 anchor = world.Actors.TryGetValue(b.Id, out var actor) && actor.Health
                    ? actor.Health.position
                    : world.Point(b.Position) + Vector3.up * (float)(BuildingVisualHeight(b.Kind) + profile.LifecycleMarkerOffsetMeters);
                var point = cameraView.WorldToViewportPoint(anchor);
                marker.style.width = marker.style.height = size;
                marker.style.left = point.x * root.contentRect.width - size / 2;
                marker.style.top = (1 - point.y) * root.contentRect.height - size * 1.35f;
                marker.style.display = point.z > 0 && !mapOpen ? DisplayStyle.Flex : DisplayStyle.None;
            }
            foreach (int id in boostMarkers.Keys.Where(id => !visible.Contains(id)).ToArray())
            {
                boostMarkers[id].RemoveFromHierarchy();
                boostMarkers.Remove(id);
            }
        }
    }
}
