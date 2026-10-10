using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private sealed class PadView { public GameObject Root,Fill; public Mesh Mesh; public MeshFilter Surface; public Vector2 Half; public double Progress=double.NaN; public Color Color=Color.clear; }
        private readonly Dictionary<string,PadView> territoryPads=new Dictionary<string,PadView>();
        private readonly Dictionary<string,Vector2> apronModelBounds=new Dictionary<string,Vector2>();
        private EnvironmentArtProfile apronArt;
        private Material apronMaterial;
        private long padTick=-1;
        private EnvironmentArtProfile ApronArt=>apronArt??(apronArt=EnvironmentArtProfile.Load());
        private static void DestroyPad(PadView p){foreach(var filter in p.Root.GetComponentsInChildren<MeshFilter>())UnityEngine.Object.Destroy(filter.sharedMesh);UnityEngine.Object.Destroy(p.Root);}
        public void ClearPads(){foreach(var p in territoryPads.Values)DestroyPad(p);territoryPads.Clear();apronModelBounds.Clear();padTick=-1;}
        private Material ApronMaterial
        {
            get
            {
                if(apronMaterial)return apronMaterial;
                var shader=Resources.Load<Shader>("ConstructionApron");if(!shader)throw new InvalidOperationException("Missing construction apron shader");
                apronMaterial=new Material(shader);apronMaterial.SetTexture("_ConcreteTex",Resources.Load<Texture2D>("Environment/NaturalFrontier/Concrete"));
                apronMaterial.SetTexture("_FogMask",Fog.Texture);apronMaterial.SetColor("_FogColor",material.GetColor("_FogColor"));apronMaterial.SetVector("_FogBounds",material.GetVector("_FogBounds"));
                return apronMaterial;
            }
        }
        // The lower platform is the support datum; balconies/equipment may overhang it.
        private Vector2 ApronModelHalf(string key)
        {
            if(apronModelBounds.TryGetValue(key,out var size))return size;
            var prefab=Resources.Load<GameObject>("StyleA/"+key);if(!prefab)throw new InvalidOperationException("Missing approved model: "+key);
            var inverse=prefab.transform.worldToLocalMatrix;
            var foundations=prefab.GetComponentsInChildren<Renderer>(true).Where(r=>r.name.EndsWith(":foundation-concrete",StringComparison.Ordinal)).ToArray();
            if(foundations.Length!=1)throw new InvalidOperationException("Expected one approved lower platform: "+key);
            foreach(var renderer in foundations)
            {
                var bounds=renderer.localBounds;var transform=inverse*renderer.transform.localToWorldMatrix;
                for(int i=0;i<8;i++)
                {
                    var corner=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    var p=transform.MultiplyPoint3x4(corner)*(float)BuildingScale(key);
                    size=Vector2.Max(size,new Vector2(Mathf.Abs(p.x),Mathf.Abs(p.z)));
                }
            }
            apronModelBounds.Add(key,size);return size;
        }
        private Vector2 ApronHalf(PlayableBuildingKind kind,bool upgraded=false)
            =>ApronModelHalf(BuildingKey(kind.ToString(),upgraded))+Vector2.one*ApronArt.apronMargin;
        private Vector2 ObservedSupportHalf(string key,NavPoint point,PlayableBuildingKind emptyKind,PlayableBuildingSnapshot observed,PlayableSnapshot view)
        {
            if(observed!=null)return ApronHalf(observed.Kind,observed.RefineryUpgraded);
            if(territoryPads.TryGetValue(key,out var retained))return retained.Half;
            var known=view.Vision?.KnownBuildings.FirstOrDefault(b=>b.Position.X==point.X&&b.Position.Z==point.Z);
            return known!=null?ApronHalf(known.Kind,known.RefineryUpgraded):ApronHalf(emptyKind);
        }
        public void RenderSites(PlayableSnapshot view,Camera camera=null)
        {
            var facing=Facing(camera);
            // Camera motion can occur while paused at the same tick; keep the support square to the facade.
            foreach(var pad in territoryPads.Values)pad.Root.transform.rotation=facing;
            if(padTick==view.Tick)return;padTick=view.Tick;
            var live=new HashSet<string>();
            foreach(var state in view.Sites)
            {
                var center=view.Buildings.FirstOrDefault(b=>b.Id==state.CenterId&&b.Phase!=ConstructionPhase.Pending);
                UpdatePad(state.Site.Id+":0",state.Site.Position,ObservedSupportHalf(state.Site.Id+":0",state.Site.Position,state.Site.Kind,center,view),state.Owner??state.Claimant,center==null?state.Progress:0,state.Contested,view.Tick,facing,live);
                foreach(var slot in state.Site.Slots)
                {
                    var observed=view.Buildings.FirstOrDefault(b=>b.SiteId==state.Site.Id&&b.SlotId==slot.Id&&b.Phase!=ConstructionPhase.Pending);
                    string key=state.Site.Id+":"+slot.Id;
                    UpdatePad(key,slot.Position,ObservedSupportHalf(key,slot.Position,PlayableBuildingKind.Factory,observed,view),state.Owner,0,false,view.Tick,facing,live);
                }
            }
            foreach(var site in view.DiscoveredSites)
            {
                if(view.Sites.Any(s=>s.Site.Id==site.Id))continue;
                // Static discovered ground is safe to remember; never inspect hidden live buildings here.
                UpdatePad(site.Id+":0",site.Position,ObservedSupportHalf(site.Id+":0",site.Position,site.Kind,null,view),null,0,false,view.Tick,facing,live);
                foreach(var slot in site.Slots)UpdatePad(site.Id+":"+slot.Id,slot.Position,ObservedSupportHalf(site.Id+":"+slot.Id,slot.Position,PlayableBuildingKind.Factory,null,view),null,0,false,view.Tick,facing,live);
            }
            foreach(var key in territoryPads.Keys.ToArray())if(!live.Contains(key)){DestroyPad(territoryPads[key]);territoryPads.Remove(key);}
        }
        private void UpdatePad(string key,NavPoint point,Vector2 half,PlayableOwner? owner,double progress,bool contested,long tick,Quaternion facing,HashSet<string> live)
        {
            live.Add(key);
            if(!territoryPads.TryGetValue(key,out var pad))
            {
                var obj=new GameObject("Site pad "+key);obj.transform.SetParent(root,false);obj.transform.localPosition=Point(point);obj.transform.rotation=facing;
                var surface=new GameObject("Technical apron");surface.transform.SetParent(obj.transform,false);var filter=surface.AddComponent<MeshFilter>();filter.sharedMesh=ConstructionApronMesh.Build(half,ApronArt);surface.AddComponent<MeshRenderer>().sharedMaterial=ApronMaterial;
                var fill=new GameObject("Capture");fill.transform.SetParent(obj.transform,false);var mesh=new Mesh{name="Capture edge strip"};fill.AddComponent<MeshFilter>().sharedMesh=mesh;fill.AddComponent<MeshRenderer>().sharedMaterial=ApronMaterial;
                Layer(obj);pad=new PadView{Root=obj,Fill=fill,Mesh=mesh,Surface=filter,Half=half};territoryPads.Add(key,pad);
            }
            if(pad.Half!=half)
            {
                // Resize the same physical support only from an observed model; keep its lifecycle identity.
                ReleaseWorldObject(pad.Surface.sharedMesh);pad.Surface.sharedMesh=ConstructionApronMesh.Build(half,ApronArt);
                pad.Half=half;pad.Progress=double.NaN;
            }
            if(pad.Progress!=progress)
            {
                // Capture advances along an exposed service-edge strip, leaving the concrete readable.
                var polygon=new List<Vector3>();float w=Mathf.Min(ApronArt.apronMarkWidth,Mathf.Min(pad.Half.x,pad.Half.y)*.09f),x=pad.Half.x*.65f,z=pad.Half.y-w*6;
                if(progress>0)polygon.AddRange(new[]{new Vector3(-x,0,z-w),new Vector3(Mathf.Lerp(-x,x,(float)progress),0,z-w),new Vector3(Mathf.Lerp(-x,x,(float)progress),0,z+w),new Vector3(-x,0,z+w)});
                SetMesh(pad.Mesh,polygon);pad.Fill.transform.localPosition=Vector3.up*(ApronArt.apronHeight+.006f);pad.Progress=progress;
            }
            var color=OwnerPaint!=null&&owner.HasValue?OwnerPaint(owner):owner==PlayableOwner.Enemy?Enemy:Ally;
            if(contested)color*=.65f+.35f*(float)(.5+.5*Math.Sin(tick/30d*Math.PI*2*profile.CapturePulseHz));
            if(color!=pad.Color){var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);pad.Fill.GetComponent<Renderer>().SetPropertyBlock(block);pad.Color=color;}
        }
        private static void SetMesh(Mesh mesh,List<Vector3> polygon)
        {
            mesh.Clear();if(polygon.Count<3)return;mesh.SetVertices(polygon);mesh.SetColors(Enumerable.Repeat(Color.white,polygon.Count).ToList());
            var indices=new List<int>();for(int i=1;i<polygon.Count-1;i++){indices.Add(0);indices.Add(i+1);indices.Add(i);}mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        }
    }
}
