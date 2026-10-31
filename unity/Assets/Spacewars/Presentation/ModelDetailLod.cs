using System;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Three mesh representations, one original rig/material/renderer lifecycle.
    public sealed class ModelDetailLod : MonoBehaviour
    {
        [SerializeField] Renderer[] renderers=Array.Empty<Renderer>();
        [SerializeField] Mesh[] original=Array.Empty<Mesh>(),normal=Array.Empty<Mesh>(),far=Array.Empty<Mesh>();
        [SerializeField] Bounds referenceBounds;
        [SerializeField] Bounds[] selectionBounds=Array.Empty<Bounds>();
        public void ConfigureSelectionBounds(Bounds[] bounds)=>selectionBounds=bounds??Array.Empty<Bounds>();
        public int Level {get;private set;}
        // Runtime provenance lets the authoring exporter distinguish its own
        // bindings from independently authored tiers during regeneration.
        public bool IsCatalogBound {get;private set;}
        public void MarkCatalogBound()=>IsCatalogBound=true;
        public Renderer[] Renderers=>renderers;
        public Mesh[] OriginalMeshes=>original;
        public Mesh[] NormalMeshes=>normal;
        public Mesh[] FarMeshes=>far;
        public Bounds ReferenceBounds=>referenceBounds;
        public Bounds[] SelectionBounds=>selectionBounds;
        public void Configure(Renderer[] sources,Mesh[] full,Mesh[] medium,Mesh[] coarse,Bounds bounds)
        {
            if(sources.Length==0||sources.Length!=full.Length||full.Length!=medium.Length||medium.Length!=coarse.Length)throw new ArgumentException("Model detail bindings");
            for(int i=0;i<sources.Length;i++)if(!sources[i]||!full[i]||!medium[i]||!coarse[i]||full[i].subMeshCount!=medium[i].subMeshCount||full[i].subMeshCount!=coarse[i].subMeshCount)throw new ArgumentException("Model detail mesh layout");
            renderers=sources;original=full;normal=medium;far=coarse;referenceBounds=bounds;SetDetail(0);
        }
        public void Select(Camera camera)
        {
            SelectScreenRatio(ProjectedScreenRatio(camera),ModelDetailProfile.Current);
        }
        public float ProjectedScreenRatio(Camera camera)
        {
            if(!camera)return float.PositiveInfinity;
            if(selectionBounds.Length>0){
                float largest=0;
                foreach(var bounds in selectionBounds){if(Vector3.Dot(transform.TransformPoint(bounds.center)-camera.transform.position,camera.transform.forward)>camera.nearClipPlane)largest=Mathf.Max(largest,ProjectedBoundsRatio(camera,bounds));}
                return largest;
            }
            return ProjectedBoundsRatio(camera,referenceBounds);
        }
        float ProjectedBoundsRatio(Camera camera,Bounds bounds)
        {
            var scale=transform.lossyScale;
            float size=Mathf.Max(bounds.size.x*Mathf.Abs(scale.x),bounds.size.y*Mathf.Abs(scale.y),bounds.size.z*Mathf.Abs(scale.z));
            float depth=Vector3.Dot(transform.TransformPoint(bounds.center)-camera.transform.position,camera.transform.forward);
            float height=camera.orthographic?2*camera.orthographicSize:2*depth*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f);
            return depth>camera.nearClipPlane&&height>0?size/height:float.PositiveInfinity;
        }
        public void SelectScreenRatio(float ratio,ModelDetailProfile profile)
        {
            int next=0;
            if(!float.IsNaN(ratio)&&!float.IsInfinity(ratio)&&ratio>=0){
                if(Level==2)next=ratio>=profile.normalExit?0:ratio>=profile.farExit?1:2;
                else if(Level==1)next=ratio>=profile.normalExit?0:ratio<=profile.farEnter?2:1;
                else next=ratio<=profile.farEnter?2:ratio<=profile.normalEnter?1:0;
            }
            if(next!=Level)SetDetail(next);
        }
        public void SetDetail(int level)
        {
            if(level<0||level>2)throw new ArgumentOutOfRangeException(nameof(level));
            var meshes=level==0?original:level==1?normal:far;
            for(int i=0;i<renderers.Length;i++){
                if(renderers[i] is SkinnedMeshRenderer skin){var bounds=skin.localBounds;skin.sharedMesh=meshes[i];skin.localBounds=bounds;}
                else {var bounds=renderers[i].localBounds;renderers[i].GetComponent<MeshFilter>().sharedMesh=meshes[i];renderers[i].localBounds=bounds;}
            }
            Level=level;
        }
    }
}
