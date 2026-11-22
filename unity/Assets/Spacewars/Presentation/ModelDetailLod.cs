using System;
using UnityEngine;

namespace Spacewars.Presentation
{
    internal readonly struct ActorCameraProjection
    {
        internal readonly Camera Camera;
        internal readonly Vector3 Position, Forward;
        internal readonly bool Orthographic;
        internal readonly float OrthographicSize, HalfFieldTangent, NearClip;

        internal ActorCameraProjection(Camera camera)
        {
            this = default;
            Camera = camera;
            if (!camera) return;
            var cameraTransform = camera.transform;
            Position = cameraTransform.position;
            Forward = cameraTransform.forward;
            Orthographic = camera.orthographic;
            if (Orthographic) OrthographicSize = camera.orthographicSize;
            else HalfFieldTangent = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
            NearClip = camera.nearClipPlane;
        }

        internal float Depth(Vector3 worldPosition) => Vector3.Dot(worldPosition - Position, Forward);
        internal float Height(float depth) => Orthographic ? 2 * OrthographicSize : 2 * depth * HalfFieldTangent;
    }

    // Three mesh representations, one original rig/material/renderer lifecycle.
    public sealed class ModelDetailLod : MonoBehaviour
    {
        [SerializeField] Renderer[] renderers=Array.Empty<Renderer>();
        [SerializeField] Mesh[] original=Array.Empty<Mesh>(),normal=Array.Empty<Mesh>(),far=Array.Empty<Mesh>();
        [SerializeField] Bounds referenceBounds;
        [SerializeField] Bounds[] selectionBounds=Array.Empty<Bounds>();
        private GameObject[] groups;
        internal void ConfigureGroups(GameObject[] levels, Bounds bounds)
        {
            if(levels.Length!=3)throw new ArgumentException("Expected three model tiers.");
            groups=levels;referenceBounds=bounds;SetDetail(0);
        }
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
        internal void Select(in ActorCameraProjection view, ModelDetailProfile profile)
            => SelectScreenRatio(ProjectedScreenRatio(view), profile);

        public float ProjectedScreenRatio(Camera camera) => ProjectedScreenRatio(new ActorCameraProjection(camera));

        private float ProjectedScreenRatio(in ActorCameraProjection view)
        {
            if (!view.Camera) return float.PositiveInfinity;
            var model = transform;
            var scale = model.lossyScale;
            if (selectionBounds.Length == 0)
            {
                float depth = view.Depth(model.TransformPoint(referenceBounds.center));
                return ProjectedBoundsRatio(view, referenceBounds, scale, depth);
            }

            float largest = 0;
            foreach (var bounds in selectionBounds)
            {
                float depth = view.Depth(model.TransformPoint(bounds.center));
                if (depth > view.NearClip) largest = Mathf.Max(largest, ProjectedBoundsRatio(view, bounds, scale, depth));
            }
            return largest;
        }

        private static float ProjectedBoundsRatio(in ActorCameraProjection view, Bounds bounds, Vector3 scale, float depth)
        {
            var boundsSize = bounds.size;
            float size = Mathf.Max(boundsSize.x * Mathf.Abs(scale.x), boundsSize.y * Mathf.Abs(scale.y), boundsSize.z * Mathf.Abs(scale.z));
            float height = view.Height(depth);
            return depth > view.NearClip && height > 0 ? size / height : float.PositiveInfinity;
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
            if(groups!=null){for(int i=0;i<groups.Length;i++)groups[i].SetActive(i==level);Level=level;return;}
            var meshes=level==0?original:level==1?normal:far;
            for(int i=0;i<renderers.Length;i++){
                if(renderers[i] is SkinnedMeshRenderer skin){var bounds=skin.localBounds;skin.sharedMesh=meshes[i];skin.localBounds=bounds;}
                else {var bounds=renderers[i].localBounds;renderers[i].GetComponent<MeshFilter>().sharedMesh=meshes[i];renderers[i].localBounds=bounds;}
            }
            Level=level;
        }
    }
}
