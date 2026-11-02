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
        }
        readonly Dictionary<int,RelayView> relayViews=new Dictionary<int,RelayView>();
        void ClearRelayViews()
        {
            foreach(var r in relayViews.Values){ReleaseWorldObject(r.Progress);ReleaseWorldObject(r.Root);}
            relayViews.Clear();
        }
        void RenderRelayViews(PlayableSnapshot view,Camera camera)
        {
            foreach(var site in view.DiscoveredSites.Where(s=>s.Kind==PlayableBuildingKind.RelayTower))
            {
                if(!relayViews.TryGetValue(site.Id,out var relay))
                {
                    var model=Model("relay-tower",root,true);model.name="Relay site "+site.Id;model.localPosition=Point(site.Position);
                    var marker=new GameObject("Circular capture progress");marker.transform.SetParent(model,false);
                    var mesh=new Mesh{name="Relay capture progress"};marker.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=marker.AddComponent<MeshRenderer>();renderer.sharedMaterial=CapturePadMaterial;
                    Layer(model.gameObject);
                    relay=new RelayView{Root=model.gameObject,Motion=model.GetComponent<RelayTowerPresentation>(),Detail=model.GetComponent<ModelDetailLod>(),Progress=mesh,ProgressRenderer=renderer};
                    relayViews.Add(site.Id,relay);
                }
                var state=view.Sites.FirstOrDefault(s=>s.Site.Id==site.Id);
                // Only current visible site data drives paint/motion; no hidden
                // live ownership lookup. Physical model is hidden outside sight.
                relay.Root.SetActive(state!=null);
                if(state==null)continue;
                Color? owner=state.Owner.HasValue?(OwnerPaint!=null?OwnerPaint(state.Owner):state.Owner==PlayableOwner.Enemy?Enemy:Ally):(Color?)null;
                relay.Motion.Sample(owner,state.ControlledSeconds);
                if(camera)relay.Detail.Select(camera);
                if(relay.Fraction!=state.Progress)
                {
                    RelayProgressMesh(relay.Progress,(float)state.Progress,relay.Motion.Profile.progressRingWidth);relay.Fraction=state.Progress;
                }
                var color=state.Claimant.HasValue?(OwnerPaint!=null?OwnerPaint(state.Claimant):state.Claimant==PlayableOwner.Enemy?Enemy:Ally):Color.gray;
                if(state.Contested)color*=.65f+.35f*(float)(.5+.5*System.Math.Sin(view.Tick/30d*System.Math.PI*2*profile.CapturePulseHz));
                color.a=1;var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);relay.ProgressRenderer.SetPropertyBlock(block);
            }
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
