using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private readonly Dictionary<int,Transform> memories=new Dictionary<int,Transform>();
        public int MemoryCount=>memories.Count;
        private void ClearMemories(){foreach(var t in memories.Values)Object.Destroy(t.gameObject);memories.Clear();}
        public void RenderMemories(PlayableSnapshot view,Camera camera=null)
        {
            var retained=new HashSet<int>();
            if(view.Vision!=null)foreach(var known in view.Vision.KnownBuildings)
            {
                if(view.Vision.IsVisible(known.Position))continue;
                retained.Add(known.Id);
                if(!memories.TryGetValue(known.Id,out var anchor))
                {
                    anchor=new GameObject("Remembered building "+known.Id).transform;anchor.SetParent(root,false);
                    var key=BuildingKey(known.Kind.ToString(),known.RefineryUpgraded);var model=Model(key,anchor,known.Owner==PlayableOwner.Player);
                    model.localScale=Vector3.one*(float)BuildingScale(key);model.localRotation=BuildingFacing;
                    foreach(var renderer in model.GetComponentsInChildren<Renderer>())
                    {
                        for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)
                        {
                            var original=renderer.sharedMaterials[slot];var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);
                            var color=block.HasColor("_BaseColor")?block.GetColor("_BaseColor"):original.GetColor("_BaseColor");
                            color=Color.Lerp(color,new Color(color.grayscale,color.grayscale,color.grayscale,1),(float)profile.FogMemoryDesaturation)*(float)profile.FogMemoryBrightness;
                            block.SetColor("_BaseColor",color);block.SetColor("_EmissionColor",Color.black);renderer.SetPropertyBlock(block,slot);
                        }
                    }
                    memories.Add(known.Id,anchor);
                }
                anchor.position=Point(known.Position)+Vector3.up*ApronArt.apronHeight;anchor.rotation=Quaternion.identity;anchor.GetChild(0).rotation=Facing(camera);
            }
            foreach(int id in memories.Keys.ToArray())if(!retained.Contains(id)){Object.Destroy(memories[id].gameObject);memories.Remove(id);}
        }
    }
}
