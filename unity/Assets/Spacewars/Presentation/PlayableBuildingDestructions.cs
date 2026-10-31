using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private sealed class CollapsingBuilding
        {internal GameObject Root,Effects;internal Transform Hull;internal Vector3 Origin;internal float Depth,Radius;}
        private readonly Dictionary<int,CollapsingBuilding> collapsingBuildings=new Dictionary<int,CollapsingBuilding>();
        private readonly BuildingCollapseVisuals collapseVisuals=new BuildingCollapseVisuals();
        private long collapseGeneration=long.MinValue,collapseTick=-1;
        public int BuildingDestructionCount=>collapsingBuildings.Count;
        public void RenderBuildingDestructions(PlayableSnapshot view)
        {
            if(view==null)return;
            if(collapseGeneration!=view.Generation||view.Tick<collapseTick){ClearBuildingDestructions();collapseGeneration=view.Generation;}
            collapseTick=view.Tick;
            var live=new HashSet<int>();
            foreach(var d in view.BuildingDestructions)
            {
                if(view.Tick<d.StartTick||view.Tick>=d.EndTick||view.Vision!=null&&!view.Vision.IsVisible(d.Position))continue;
                live.Add(d.Id);
                if(!collapsingBuildings.TryGetValue(d.Id,out var c))
                {
                    var holder=new GameObject("Building collapse "+d.Id);holder.transform.SetParent(root,false);
                    holder.transform.position=Point(d.Position)+Vector3.up*ApronArt.apronHeight;
                    string key=BuildingKey(d.Kind.ToString(),d.Upgraded);Transform hull;
                    if(actors.TryGetValue(d.Id,out var previous))
                    {
                        // Transfer the actual last-rendered hull before live actor cleanup:
                        // no one-frame turn, door reset, scale jump or model replacement.
                        holder.transform.position=previous.Root.position;hull=previous.Hull;hull.SetParent(holder.transform,true);
                    }
                    else
                    {
                        hull=Model(key,holder.transform,d.Owner==view.Owner);hull.localScale=Vector3.one*(float)BuildingScale(key);
                        hull.rotation=d.Kind==PlayableBuildingKind.Factory?Quaternion.Euler(0,90-(float)d.Heading*Mathf.Rad2Deg,0):Facing(null,d.Kind==PlayableBuildingKind.Mine);
                        holder.transform.localScale=new Vector3(1,Mathf.Lerp(.2f,1,(float)d.Progress),1);
                    }
                    // A corpse cannot continue production, light animation or staged vehicle release.
                    foreach(var component in holder.GetComponentsInChildren<MonoBehaviour>(true))component.enabled=false;
                    foreach(var collider in holder.GetComponentsInChildren<Collider>(true))collider.enabled=false;
                    foreach(var light in holder.GetComponentsInChildren<Light>(true))light.enabled=false;
                    foreach(var particles in holder.GetComponentsInChildren<ParticleSystem>(true))particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                    foreach(var particles in holder.GetComponentsInChildren<ParticleSystemRenderer>(true))particles.enabled=false;
                    var owner=OwnerPaint?.Invoke(d.Owner)??(d.Owner==view.Owner?Ally:Enemy);
                    var bounds=new Bounds(holder.transform.position,Vector3.zero);
                    // Service particle renderer bounds are deliberately conservative and do not describe the hull.
                    foreach(var renderer in hull.GetComponentsInChildren<MeshRenderer>(false))
                    {
                        bounds.Encapsulate(renderer.bounds);
                        for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)
                        {
                            var role=renderer.sharedMaterials[slot].name;var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);
                            if(role.EndsWith("team-primary")||role.EndsWith("team-emissive"))block.SetColor("_BaseColor",owner);
                            block.SetColor("_EmissionColor",Color.black);renderer.SetPropertyBlock(block,slot);
                        }
                    }
                    c=new CollapsingBuilding{Root=holder,Hull=hull,Origin=holder.transform.position,
                        Depth=Mathf.Max(.1f,bounds.max.y-holder.transform.position.y+.1f),Radius=Mathf.Max(bounds.extents.x,bounds.extents.z)};
                    c.Effects=collapseVisuals.Create(root,d.Id);c.Effects.transform.position=c.Origin;
                    Layer(holder);Layer(c.Effects);collapsingBuildings.Add(d.Id,c);
                }
                float phase=Mathf.Clamp01((float)(view.Tick-d.StartTick)/(d.EndTick-d.StartTick));
                // Shape/timing curves below are authored concept-A animation, like mesh keyframes.
                float sink=Mathf.SmoothStep(0,1,Mathf.Clamp01((phase-.10f)/.70f));
                float shake=Mathf.Sin(phase*95+d.Id)*Mathf.Sin(Mathf.PI*Mathf.Min(1,phase/.82f));
                float side=Mathf.Sin(phase*127+d.Id*2.3f);
                c.Root.transform.position=c.Origin+new Vector3(shake*(float)profile.BuildingDestructionShake,-c.Depth*sink,side*(float)profile.BuildingDestructionShake);
                c.Root.transform.localRotation=Quaternion.Euler(shake*(float)profile.BuildingDestructionTilt,0,side*(float)profile.BuildingDestructionTilt);
                c.Root.SetActive(phase<.82f);
                collapseVisuals.Update(c.Effects,phase,c.Radius,c.Depth*(1-sink),(float)profile.BuildingDestructionFireScale,(float)profile.BuildingDestructionSmokeOpacity);
            }
            foreach(var id in collapsingBuildings.Keys.ToArray())if(!live.Contains(id))
            {var c=collapsingBuildings[id];ReleaseWorldObject(c.Root);ReleaseWorldObject(c.Effects);collapsingBuildings.Remove(id);}
        }
        private void ClearBuildingDestructions()
        {
            foreach(var c in collapsingBuildings.Values){ReleaseWorldObject(c.Root);ReleaseWorldObject(c.Effects);}
            collapsingBuildings.Clear();collapseTick=-1;collapseGeneration=long.MinValue;
        }
    }
}
