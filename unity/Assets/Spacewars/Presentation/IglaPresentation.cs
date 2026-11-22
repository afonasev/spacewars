using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed class IglaPresentation : MonoBehaviour
    {
        private static readonly Dictionary<string,Material> fleetMaterials=new();
        private Transform[] mounts, wheels;
        private Transform muzzle;
        private LineRenderer beam;
        private Material beamMaterial;
        private Vector3 previous;
        private bool positioned;
        private double beamStartSeconds;

        public static GameObject Create(Transform parent)
        {
            var root = new GameObject("Igla");
            root.transform.SetParent(parent,false);
            var rig = root.AddComponent<IglaPresentation>();
            var tiers = new[]{"close","normal","far"}.Select(t =>
                Instantiate(Resources.Load<GameObject>("StyleA/Igla/"+t),root.transform,false)).ToArray();
            foreach(var tier in tiers)BindFleetMaterials(tier);
            rig.mounts = tiers.Select(t=>t.GetComponentsInChildren<Transform>(true).Single(n=>n.name=="turretYaw")).ToArray();
            rig.muzzle = rig.mounts[0].GetComponentsInChildren<Transform>(true).Single(t=>t.name=="muzzle");
            rig.wheels = root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("wheel")).ToArray();
            root.AddComponent<ModelDetailLod>().ConfigureGroups(tiers,new Bounds(new Vector3(0,.60f,.66f),new Vector3(1.78f,1.22f,3.46f)));
            rig.beam = new GameObject("Gauss beam").AddComponent<LineRenderer>();
            rig.beam.transform.SetParent(root.transform,false);
            rig.beamMaterial = new Material(Shader.Find("Sprites/Default"));
            rig.beam.sharedMaterial = rig.beamMaterial;
            rig.beam.startColor = rig.beam.endColor = Color.white;
            rig.beam.widthCurve = AnimationCurve.Linear(0f, .16f, 1f, .12f);
            rig.beam.positionCount=2; rig.beam.enabled=false;
            return root;
        }

        public static void BindFleetMaterials(GameObject tier)
        {
            foreach(var renderer in tier.GetComponentsInChildren<MeshRenderer>(true))
            {
                var slots=renderer.sharedMaterials;
                for(int i=0;i<slots.Length;i++)
                {
                    var role=slots[i].name;
                    if(role=="rubber"||role=="emissive")continue;
                    if(!fleetMaterials.TryGetValue(role,out var material)||!material)
                    {
                        material=Resources.Load<Material>("StyleA/tankHybrid-"+role);
                        if(!material)throw new System.InvalidOperationException("Missing Igla fleet material: "+role);
                        fleetMaterials[role]=material;
                    }
                    slots[i]=material;
                }
                renderer.sharedMaterials=slots;
            }
        }

        public void Display(PlayableEntitySnapshot unit, double matchSeconds)
        {
            var turn=Quaternion.Euler(0,(float)(unit.HullHeading-unit.TurretHeading)*Mathf.Rad2Deg,0);
            foreach(var mount in mounts)mount.localRotation=turn;
            Vector3 location=transform.position;
            if(positioned)
            {
                float travel=Vector3.Dot(location-previous,transform.forward);
                foreach(var wheel in wheels)wheel.Rotate(Vector3.right,travel*Mathf.Rad2Deg/(.221f*transform.lossyScale.x),Space.Self);
            }
            positioned=true; previous=location;
            if (!unit.IglaBeam.HasValue)
            {
                beam.enabled = false;
                return;
            }
            if (!beam.enabled) beamStartSeconds = matchSeconds;
            beam.enabled = true;
            const float crestSeconds = .06f;
            float elapsed = (float)(matchSeconds - beamStartSeconds);
            float fade = Mathf.InverseLerp(crestSeconds, PlayableUnitRules.IglaBeamVisibleSeconds, elapsed);
            float pulse = 1f - Mathf.SmoothStep(0f, 1f, fade);
            beam.widthMultiplier = Mathf.Lerp(.15f, 1f, pulse);
            beam.startColor = beam.endColor = new Color(1f, 1f, 1f, pulse);
            var end=unit.IglaBeam.Value;
            beam.SetPosition(0,muzzle.position);
            beam.SetPosition(1,new Vector3((float)end.X,(float)end.Y,(float)end.Z));
        }

        private void OnDestroy(){if(beamMaterial)Destroy(beamMaterial);}
    }
}
