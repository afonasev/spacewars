using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private Transform rallyPreview;
        public Transform RallyPreview=>rallyPreview;
        private readonly Dictionary<int,Transform> rallyFlags=new Dictionary<int,Transform>();
        public IReadOnlyDictionary<int,Transform> RallyFlags=>rallyFlags;
        public void RenderRallyFlags(PlayableSnapshot view,ISet<int> selected,bool spectator=false)
        {
            var visible=new HashSet<int>();
            if(!spectator&&view!=null)
            foreach(var building in view.Buildings)
            {
                if(!selected.Contains(building.Id)||building.Owner!=view.Owner||building.Health<=0||
                   building.Phase!=ConstructionPhase.Ready||building.PrivateState?.HasRally!=true||
                   building.PrivateState.Lifecycle?.Selling==true)continue;
                visible.Add(building.Id);
                if(!rallyFlags.TryGetValue(building.Id,out var flag))
                {
                    flag=Model("rallyFlag",root,true);flag.name="Rally flag "+building.Id;
                    rallyFlags.Add(building.Id,flag);
                }
                flag.localScale=Vector3.one*(float)(profile.RallyFlagHeight/2.1077218055725098); // Authored GLB height in metres.
                flag.position=Point(building.Rally)+Vector3.up*.03f; // Avoid ground z-fighting, as in the approved web model.
                var slope=profile.AuthoredMap?.SurfaceGradient(building.Rally)??default(NavPoint);
                flag.rotation=Quaternion.FromToRotation(Vector3.up,new Vector3((float)-slope.X,1,(float)-slope.Z).normalized);
                var owner=OwnerPaint?.Invoke(building.Owner)??Ally;
                foreach(var renderer in flag.GetComponentsInChildren<Renderer>(true))
                for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)
                {
                    var role=renderer.sharedMaterials[slot].name;
                    if(!role.EndsWith("team-primary")&&!role.EndsWith("team-emissive"))continue;
                    var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",owner);
                    if(role.EndsWith("team-emissive"))block.SetColor("_EmissionColor",owner*2.2f);
                    renderer.SetPropertyBlock(block,slot);
                }
            }
            foreach(var id in rallyFlags.Keys.ToArray())if(!visible.Contains(id))RemoveRallyFlag(id);
        }
        private void RemoveRallyFlag(int id)
        {
            if(!rallyFlags.TryGetValue(id,out var flag))return;
            flag.gameObject.SetActive(false);ReleaseWorldObject(flag.gameObject);rallyFlags.Remove(id);
        }
        public void RenderRallyPreview(NavPoint? point,PlayableOwner owner)
        {
            if(!point.HasValue){if(rallyPreview)rallyPreview.gameObject.SetActive(false);return;}
            if(!rallyPreview){rallyPreview=Model("rallyFlag",root,true);rallyPreview.name="Rally placement preview";}
            rallyPreview.gameObject.SetActive(true);rallyPreview.localScale=Vector3.one*(float)(profile.RallyFlagHeight/2.1077218055725098);
            rallyPreview.position=Point(point.Value)+Vector3.up*.03f;
            var slope=profile.AuthoredMap?.SurfaceGradient(point.Value)??default(NavPoint);
            rallyPreview.rotation=Quaternion.FromToRotation(Vector3.up,new Vector3((float)-slope.X,1,(float)-slope.Z).normalized);
            var color=OwnerPaint?.Invoke(owner)??Ally;
            foreach(var renderer in rallyPreview.GetComponentsInChildren<Renderer>(true))for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)
            {
                var role=renderer.sharedMaterials[slot].name;if(!role.EndsWith("team-primary")&&!role.EndsWith("team-emissive"))continue;
                var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);if(role.EndsWith("team-emissive"))block.SetColor("_EmissionColor",color*2.2f);renderer.SetPropertyBlock(block,slot);
            }
        }
        private void ClearRallyFlags(){if(rallyPreview){rallyPreview.gameObject.SetActive(false);ReleaseWorldObject(rallyPreview.gameObject);rallyPreview=null;}foreach(var id in rallyFlags.Keys.ToArray())RemoveRallyFlag(id);}
    }
}
