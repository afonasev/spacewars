using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private sealed class PadView { public GameObject Root,Fill; public Mesh Mesh; public double Progress=double.NaN; public Color Color=Color.clear; }
        private readonly Dictionary<string,PadView> territoryPads=new Dictionary<string,PadView>();
        private long padTick=-1;
        private static void DestroyPad(PadView p){foreach(var filter in p.Root.GetComponentsInChildren<MeshFilter>())UnityEngine.Object.Destroy(filter.sharedMesh);UnityEngine.Object.Destroy(p.Root);}
        public void ClearPads(){foreach(var p in territoryPads.Values){DestroyPad(p);}territoryPads.Clear();padTick=-1;}
        public void RenderSites(PlayableSnapshot view)
        {
            if(padTick==view.Tick)return;padTick=view.Tick;
            var live=new HashSet<string>();
            foreach(var state in view.Sites)
            {
                var center=view.Buildings.FirstOrDefault(b=>b.Id==state.CenterId);
                if(center==null||center.Phase==ConstructionPhase.Pending)
                    UpdatePad(state.Site.Id+":0",state.Site.Position,TerritoryRules.Radius(profile,state.Site.Kind),false,state.Site.Kind==PlayableBuildingKind.Mine,0,state.Claimant,state.Progress,state.Contested,view.Tick,live);
                if(!state.Ready)continue;
                foreach(var slot in state.Site.Slots)
                {
                    var b=view.Buildings.FirstOrDefault(x=>x.SiteId==state.Site.Id&&x.SlotId==slot.Id);
                    if(b!=null&&b.Phase!=ConstructionPhase.Pending)continue;
                    UpdatePad(state.Site.Id+":"+slot.Id,slot.Position,profile.OrdinaryPadRadius,true,false,slot.Heading,state.Owner,0,false,view.Tick,live);
                }
            }
            foreach(var site in view.DiscoveredSites)
            {
                if(view.Sites.Any(s=>s.Site.Id==site.Id))continue;
                bool rememberedCenter=view.Vision!=null&&view.Vision.KnownBuildings.Any(b=>b.Position.X==site.Position.X&&b.Position.Z==site.Position.Z);
                if(rememberedCenter||view.Buildings.Any(b=>b.SiteId==site.Id&&b.SlotId==0&&b.Phase!=ConstructionPhase.Pending))continue;
                UpdatePad(site.Id+":0",site.Position,TerritoryRules.Radius(profile,site.Kind),false,site.Kind==PlayableBuildingKind.Mine,0,null,0,false,view.Tick,live);
            }
            foreach(var key in territoryPads.Keys.ToArray())if(!live.Contains(key)){var pad=territoryPads[key];DestroyPad(pad);territoryPads.Remove(key);}
        }
        private void UpdatePad(string key,NavPoint point,double radius,bool square,bool circle,double heading,PlayableOwner? owner,double progress,bool contested,long tick,HashSet<string> live)
        {
            live.Add(key);
            if(!territoryPads.TryGetValue(key,out var pad))
            {
                var obj=new GameObject("Site pad "+key);obj.transform.SetParent(root,false);obj.transform.localPosition=Point(point);obj.transform.localRotation=Quaternion.Euler(0,-(float)heading*Mathf.Rad2Deg,0);
                PadMesh("Border",obj.transform,Polygon(radius,square,circle),.012f,new Color(.04f,.055f,.06f));
                PadMesh("Surface",obj.transform,Polygon(radius*(1-profile.PadBorderRatio),square,circle),.015f,square?new Color(.30f,.36f,.37f):circle?new Color(.34f,.30f,.24f):new Color(.20f,.24f,.26f));
                var fill=PadMesh("Capture",obj.transform,new List<Vector3>(),.018f,Ally);
                pad=new PadView{Root=obj,Fill=fill,Mesh=fill.GetComponent<MeshFilter>().sharedMesh};territoryPads.Add(key,pad);
            }
            if(pad.Progress!=progress)
            {
                var polygon=Polygon(radius*(1-profile.PadBorderRatio),square,circle);
                // Clip only when capture actually changes; stationary discovered pads allocate no mesh each tick.
                var clipped=new List<Vector3>();float edge=(float)(-radius+2*radius*progress);
                if(progress>0)for(int i=0;i<polygon.Count;i++)
                {
                    var a=polygon[i];var b=polygon[(i+1)%polygon.Count];bool ai=a.x<=edge,bi=b.x<=edge;
                    if(ai)clipped.Add(a);
                    if(ai!=bi)clipped.Add(Vector3.Lerp(a,b,(edge-a.x)/(b.x-a.x)));
                }
                SetMesh(pad.Mesh,clipped);pad.Progress=progress;
            }
            var color=owner==PlayableOwner.Enemy?Enemy:Ally;
            if(contested)color*=.65f+.35f*(float)(.5+.5*Math.Sin(tick/30d*Math.PI*2*profile.CapturePulseHz));
            if(color!=pad.Color){var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);pad.Fill.GetComponent<Renderer>().SetPropertyBlock(block);pad.Color=color;}
        }
        private static List<Vector3> Polygon(double r,bool square,bool circle)
        {
            if(square)return new List<Vector3>{new Vector3((float)-r,0,(float)-r),new Vector3((float)r,0,(float)-r),new Vector3((float)r,0,(float)r),new Vector3((float)-r,0,(float)r)};
            // 48 segments approximate the canonical circle; tessellation is a rendering invariant.
            int count=circle?48:6;var p=new List<Vector3>();for(int i=0;i<count;i++)p.Add(new Vector3((float)(r*Math.Cos(i*2*Math.PI/count)),0,(float)(r*Math.Sin(i*2*Math.PI/count))));return p;
        }
        private GameObject PadMesh(string name,Transform parent,List<Vector3> polygon,float y,Color color)
        {
            var g=new GameObject(name);g.transform.SetParent(parent,false);g.transform.localPosition=Vector3.up*y;
            var mesh=new Mesh{name=name};SetMesh(mesh,polygon);g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=material;
            var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);g.GetComponent<Renderer>().SetPropertyBlock(block);return g;
        }
        private static void SetMesh(Mesh mesh,List<Vector3> polygon)
        {
            mesh.Clear();if(polygon.Count<3)return;mesh.SetVertices(polygon);var indices=new List<int>();for(int i=1;i<polygon.Count-1;i++){indices.Add(0);indices.Add(i+1);indices.Add(i);}mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        }
    }
}
