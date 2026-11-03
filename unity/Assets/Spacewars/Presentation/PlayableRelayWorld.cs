using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        sealed class RelayView
        {
            internal GameObject Root;
            internal RelayTowerPresentation Motion;
            internal ModelDetailLod Detail;
            internal Mesh Progress;
            internal MeshRenderer ProgressRenderer;
            internal double Fraction=-1;
            internal float Height;
        }
        readonly Dictionary<int,RelayView> relayViews=new Dictionary<int,RelayView>();
        void ClearRelayViews()
        {
            foreach(var r in relayViews.Values){ReleaseWorldObject(r.Progress);ReleaseWorldObject(r.Root);}
            relayViews.Clear();
        }
        void RenderRelayViews(PlayableSnapshot view,Camera camera)
        {
            foreach(var site in view.PublicSites.Where(s=>s.Kind==PlayableBuildingKind.RelayTower))
            {
                if(!relayViews.TryGetValue(site.Id,out var relay))
                {
                    var model=Model("relay-tower",root,true);model.name="Relay site "+site.Id;model.localPosition=Point(site.Position);
                    float height=0;
                    foreach(var part in model.GetComponentsInChildren<Renderer>(true))height=Mathf.Max(height,part.bounds.max.y-model.position.y);
                    var marker=new GameObject("Circular capture progress");marker.transform.SetParent(model,false);
                    var mesh=new Mesh{name="Relay capture progress"};marker.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=marker.AddComponent<MeshRenderer>();renderer.sharedMaterial=CapturePadMaterial;
                    Layer(model.gameObject);
                    relay=new RelayView{Root=model.gameObject,Height=height,Motion=model.GetComponent<RelayTowerPresentation>(),Detail=model.GetComponent<ModelDetailLod>(),Progress=mesh,ProgressRenderer=renderer};
                    relayViews.Add(site.Id,relay);
                }
                var state=view.Sites.FirstOrDefault(s=>s.Site.Id==site.Id);
                // Public landmark geometry persists; hidden control uses saved team knowledge.
                var known=view.Vision?.KnownRelays.FirstOrDefault(r=>r.SiteId==site.Id);
                var control=state!=null?state.Owner:known?.Owner;
                Color? owner=control.HasValue?(OwnerPaint!=null?OwnerPaint(control):control==PlayableOwner.Enemy?Enemy:Ally):(Color?)null;
                relay.Root.SetActive(true);
                if(state!=null)relay.Motion.Sample(owner,state.ControlledSeconds);
                else relay.Motion.Drive(owner,0);
                if(camera)relay.Detail.Select(camera);
                double progress=state?.Progress??0;
                if(relay.Fraction!=progress)
                {
                    RelayProgressMesh(relay.Progress,(float)progress,relay.Motion.Profile.progressRingWidth);relay.Fraction=progress;
                }
                var color=state?.Claimant.HasValue==true?(OwnerPaint!=null?OwnerPaint(state.Claimant):state.Claimant==PlayableOwner.Enemy?Enemy:Ally):Color.gray;
                if(state?.Contested==true)color*=.65f+.35f*(float)(.5+.5*System.Math.Sin(view.Tick/30d*System.Math.PI*2*profile.CapturePulseHz));
                color.a=1;var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);relay.ProgressRenderer.SetPropertyBlock(block);
            }
        }
        public bool TryRelayIncomeAnchor(int siteId,out Vector3 point)
        {
            if(relayViews.TryGetValue(siteId,out var relay)&&relay.Root.activeInHierarchy)
            {
                point=relay.Root.transform.position+Vector3.up*relay.Height;return true;
            }
            point=default;return false;
        }
        static void RelayProgressMesh(Mesh mesh,float progress,float width)
        {
            // Radii/height follow the authored 3.4-unit round support, rather than
            // capture range. Segment count is tessellation, not a gameplay knob.
            var vertices=new List<Vector3>();var triangles=new List<int>();
            int steps=Mathf.CeilToInt(Mathf.Clamp01(progress)*64);
            for(int i=0;i<=steps&&steps>0;i++)
            {
                float a=-Mathf.PI/2+Mathf.PI*2*progress*i/steps;
                vertices.Add(new Vector3(Mathf.Cos(a)*(1.83f-width),.22f,Mathf.Sin(a)*(1.83f-width)));
                vertices.Add(new Vector3(Mathf.Cos(a)*1.83f,.22f,Mathf.Sin(a)*1.83f));
                if(i==0)continue;int n=2*i;
                triangles.AddRange(new[]{n-2,n,n-1,n-1,n,n+1});
            }
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetColors(Enumerable.Repeat(Color.white,vertices.Count).ToArray());mesh.RecalculateNormals();mesh.RecalculateBounds();
        }
    }
}
