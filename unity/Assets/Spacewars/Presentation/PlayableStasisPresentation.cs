using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private Material stasisMaterial;

        public void UpdateStasis(Actor actor, PlayableEntitySnapshot unit)
        {
            if (unit.StasisRemaining <= 0)
            {
                if (actor.StasisCylinder) ReleaseWorldObject(actor.StasisCylinder.gameObject);
                actor.StasisCylinder = null;
                return;
            }
            if (actor.StasisCylinder) return;

            if (!stasisMaterial)
            {
                var shader = Resources.Load<Shader>("OrbitalStasis");
                if (!shader) return;
                stasisMaterial = new Material(shader);
            }
            orbitalCylinderMesh ??= BuildReinforcementCylinder();

            var effect = new GameObject("Stasis Cylinder") { layer = presentationLayer };
            effect.transform.SetParent(actor.Root, false);
            var dimensions = AbilityFeedback.StasisDimensions(PlayableUnitRules.Radius(profile, unit.Kind));
            effect.transform.localScale = dimensions;
            effect.transform.localPosition = Vector3.up * AbilityFeedback.groundLift;
            effect.AddComponent<MeshFilter>().sharedMesh = orbitalCylinderMesh;
            var renderer = effect.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = stasisMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            actor.StasisCylinder = renderer;
        }
    }
}
