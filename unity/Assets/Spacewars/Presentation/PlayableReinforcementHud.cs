using System;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private long reinforcementBeginSequence = -1;
        private bool reinforcementBeginPad;
        private bool reinforcementCancelling;
        private bool ReinforcementTargeting => scanTargeting && scanTargetAbility == OrbitalAbilities.Reinforcement;

        private string ReinforcementQuoteText()
        {
            var quote = view.Orbital.ReinforcementQuote;
            return (quote.Kind == PlayableEntityKind.Tank ? "2 Вепря" : "3 исследователя") + " · " + quote.Credits + " кр.";
        }

        private bool ReinforcementAffordable()
        {
            var quote = view.Orbital.ReinforcementQuote;
            var population = view.Population;
            return quote.Reserved || view.Credits >= quote.Credits &&
                (population == null || population.Living + population.Reserved + quote.Population <= population.Capacity);
        }

        private void BeginReinforcementTarget(bool pad)
        {
            if (ReinforcementTargeting)
            {
                if (orbitalOpen) CloseOrbital();
                return;
            }
            if (reinforcementBeginSequence >= 0 || reinforcementCancelling) return;
            if (!ReinforcementAffordable())
            {
                notice = view.Credits < view.Orbital.ReinforcementQuote.Credits ? "Недостаточно кредитов" : "Не хватает мест в армии";
                return;
            }
            CancelScanTarget();
            var intent = new PlayableCommand(generation, 0, LocalOwnerId, PlayableCommandKind.BeginOrbitalReinforcement, null);
            QueueHumanIntent(intent, (sent, result) =>
            {
                if (!result.Accepted) { notice = Friendly(result.Status); return; }
                reinforcementBeginPad = pad;
                reinforcementBeginSequence = sent.Sequence;
            });
        }

        private void ObserveReinforcementReceipts(System.Collections.Generic.IEnumerable<PlayableCommandReceipt> receipts)
        {
            foreach (var receipt in receipts)
            {
                if (reinforcementBeginSequence < 0 || receipt.Sequence != reinforcementBeginSequence || receipt.OwnerId != LocalOwnerId) continue;
                reinforcementBeginSequence = -1;
                if (receipt.Status == PlayableCommandStatus.Applied) BeginOrbitalTarget(reinforcementBeginPad, OrbitalAbilities.Reinforcement);
                else notice = receipt.Message ?? Friendly(receipt.Status);
                break;
            }
        }

        private void ReturnReinforcementPayment()
        {
            if (reinforcementCancelling || !ReinforcementTargeting || scanGeneration != generation || view == null ||
                view.Outcome != PlayableMatchOutcome.Playing) return;
            reinforcementCancelling = true;
            var intent = new PlayableCommand(generation, 0, LocalOwnerId, PlayableCommandKind.CancelOrbitalReinforcement, null);
            QueueHumanIntent(intent, (sent, result) =>
            {
                if (!result.Accepted) { reinforcementCancelling = false; notice = Friendly(result.Status); }
            });
        }

        private void RestoreReinforcementTargeting()
        {
            bool reserved = view.Orbital.ReinforcementQuote.Reserved;
            if (reinforcementCancelling)
            {
                if (!reserved) reinforcementCancelling = false;
                return;
            }
            if (CanOrbital && reserved && !scanTargeting && !orbitalOpen && reinforcementBeginSequence < 0)
                BeginOrbitalTarget(!SeatUsesKeyboard, OrbitalAbilities.Reinforcement);
        }

        private NavPoint[] ReinforcementPreview(NavPoint center, out bool valid)
        {
            var quote = view.Orbital.ReinforcementQuote;
            double radius = PlayableUnitRules.Radius(profile, quote.Kind);
            var map = profile.AuthoredMap;
            string surface = null;
            if (map != null && map.TryLocate(center, 0, out var location)) surface = location.SurfaceId;
            double clearance = ScanData.orbitalReinforcementClearance;
            bool SameSurface(NavPoint p) => map == null || map.TryLocate(p, 0, out var at) && at.SurfaceId == surface;
            bool Fits(NavPoint point)
            {
                if (!view.Vision.IsVisible(center) || !view.Vision.IsVisible(point) ||
                    map != null && (!map.TryLocate(point, radius, out var at) || at.SurfaceId != surface)) return false;

                foreach (var unit in view.Entities)
                {
                    if (unit.Kind == PlayableEntityKind.Hawk || unit.Health <= 0 || !SameSurface(unit.Position)) continue;
                    double minimum = radius + PlayableUnitRules.Radius(profile, unit.Kind) + (IsOpponent(unit.Owner) ? clearance : 0);
                    double dx = point.X - unit.Position.X, dz = point.Z - unit.Position.Z;
                    if (dx * dx + dz * dz < minimum * minimum) return false;
                }
                foreach (var building in view.Buildings)
                {
                    if (building.Health <= 0 || !SameSurface(building.Position)) continue;
                    double margin = radius + (IsOpponent(building.Owner) ? clearance : 0);
                    if (OrbitalReinforcement.OverlapsBuilding(point, building.Position, TerritoryRules.Radius(profile, building.Kind), margin)) return false;
                }
                return view.Geometry == null || view.Geometry.IsFree(point, radius);
            }
            return OrbitalReinforcement.Layout(center, quote, radius, Fits, out valid);
        }

        private static readonly Vector2[] ExplorerSilhouette =
        {
            new Vector2(-.95f,-.28f), new Vector2(-.70f,-.60f), new Vector2(-.38f,-.62f),
            new Vector2(-.22f,-.42f), new Vector2(.28f,-.42f), new Vector2(.47f,-.58f),
            new Vector2(.75f,-.56f), new Vector2(.96f,-.26f), new Vector2(.96f,.26f),
            new Vector2(.75f,.56f), new Vector2(.47f,.58f), new Vector2(.28f,.42f),
            new Vector2(-.22f,.42f), new Vector2(-.38f,.62f), new Vector2(-.70f,.60f),
            new Vector2(-.95f,.28f)
        };
        private static readonly Vector2[] ExplorerCabin =
        {
            new Vector2(-.49f,-.27f), new Vector2(.31f,-.27f), new Vector2(.53f,-.15f),
            new Vector2(.53f,.15f), new Vector2(.31f,.27f), new Vector2(-.49f,.27f)
        };
        private static readonly Vector2[] TankSilhouette =
        {
            new Vector2(-.76f,-.63f), new Vector2(.58f,-.63f), new Vector2(.75f,-.43f),
            new Vector2(.75f,-.13f), new Vector2(1,-.13f), new Vector2(1,.13f),
            new Vector2(.75f,.13f), new Vector2(.75f,.43f), new Vector2(.58f,.63f),
            new Vector2(-.76f,.63f), new Vector2(-.91f,.43f), new Vector2(-.91f,-.43f)
        };
        private static readonly Vector2[] TankTurret =
        {
            new Vector2(-.60f,-.35f), new Vector2(.04f,-.35f), new Vector2(.31f,-.20f),
            new Vector2(.31f,.20f), new Vector2(.04f,.35f), new Vector2(-.60f,.35f)
        };

        internal static void DrawReinforcementSilhouettes(Painter2D painter, NavPoint[] points, PlayableEntityKind kind,
            double radius, bool valid, Func<NavPoint, Vector2> project, float minScreenRadius = 0)
        {
            Color color = valid ? OrbitalTheme.Cyan : InvalidOrbitalPreview;
            bool tank = kind == PlayableEntityKind.Tank;
            var hull = tank ? TankSilhouette : ExplorerSilhouette;
            var detail = tank ? TankTurret : ExplorerCabin;
            painter.lineWidth = 1.25f;
            painter.strokeColor = new Color(color.r, color.g, color.b, .78f);
            foreach (var point in points)
            {
                Vector2 center = project(point);
                float scale = 1;
                if (minScreenRadius > 0)
                {
                    float extent = Vector2.Distance(center, project(new NavPoint(point.X + radius, point.Z)));
                    scale = Mathf.Max(1, minScreenRadius / Mathf.Max(.01f, extent));
                }

                void Shape(Vector2[] outline, float opacity, bool stroke)
                {
                    painter.fillColor = new Color(color.r, color.g, color.b, opacity);
                    painter.BeginPath();
                    for (int i = 0; i < outline.Length; i++)
                    {
                        Vector2 corner = outline[i];
                        var pixel = project(new NavPoint(point.X + corner.x * radius, point.Z + corner.y * radius));
                        pixel = center + (pixel - center) * scale;
                        if (i == 0) painter.MoveTo(pixel);
                        else painter.LineTo(pixel);
                    }
                    painter.ClosePath();
                    painter.Fill();
                    if (stroke) painter.Stroke();
                }

                Shape(hull, .34f, true);
                Shape(detail, .32f, false);
            }
        }
    }
}
