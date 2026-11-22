using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        internal bool UnitOnSelectionScreen(Camera camera, PlayableEntitySnapshot entity)
        {
            if (PlayableUnitRules.Aircraft(entity.Kind))
                return HawkScreenBounds(camera, entity, out _);

            var center = camera.WorldToScreenPoint(EntityPoint(entity));
            return center.z >= camera.nearClipPlane && center.z <= camera.farClipPlane &&
                camera.pixelRect.Contains(new Vector2(center.x, center.y));
        }

        internal IEnumerable<int> VisibleOwnUnitsOfType(Camera camera, PlayableSnapshot view,
            PlayableEntityKind kind)
        {
            foreach (var entity in view.Entities)
                if (entity.Owner == view.Owner && entity.Health > 0 && entity.Kind == kind &&
                    UnitOnSelectionScreen(camera, entity))
                    yield return entity.Id;
        }

        internal int PickNearbyOwnUnit(Camera camera, PlayableSnapshot view, Vector2 pointer,
            bool mirrored = false)
        {
            var viewport = camera.pixelRect;
            if (!viewport.Contains(pointer)) return 0;
            // Match the legacy local camera's horizontal reflection before using native projections.
            if (mirrored) pointer.x = viewport.xMin + viewport.xMax - pointer.x;

            // Give point selection a wider margin than the pointer drift allowed before a drag.
            float padding = 2 * (float)profile.SelectionDragPixels;
            int picked = 0;
            float best = float.PositiveInfinity;
            foreach (var entity in view.Entities)
            {
                if (entity.Owner != view.Owner || entity.Health <= 0) continue;

                Vector2 center;
                if (PlayableUnitRules.Aircraft(entity.Kind))
                {
                    if (!HawkScreenBounds(camera, entity, out var bounds)) continue;
                    center = bounds.center;
                    var nearest = new Vector2(Mathf.Clamp(pointer.x, bounds.xMin, bounds.xMax),
                        Mathf.Clamp(pointer.y, bounds.yMin, bounds.yMax));
                    if ((pointer - nearest).sqrMagnitude > padding * padding) continue;
                }
                else
                {
                    var position = EntityPoint(entity) + Vector3.up * .5f;
                    var screen = camera.WorldToScreenPoint(position);
                    center = new Vector2(screen.x, screen.y);
                    if (screen.z < camera.nearClipPlane || screen.z > camera.farClipPlane ||
                        !viewport.Contains(center)) continue;
                    float radius = (float)(PlayableUnitRules.Radius(profile, entity.Kind) *
                        profile.TargetPickRadiusMultiplier);
                    var rim = camera.WorldToScreenPoint(position + camera.transform.right * radius);
                    float limit = Vector2.Distance(center, rim) + padding;
                    if ((pointer - center).sqrMagnitude > limit * limit) continue;
                }
                float distance = (pointer - center).sqrMagnitude;
                if (distance < best || (distance == best && entity.Id < picked))
                {
                    picked = entity.Id;
                    best = distance;
                }
            }
            return picked;
        }
    }
}
