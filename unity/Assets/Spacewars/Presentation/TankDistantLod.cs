using System;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Both representations share the original articulation transforms and bones.
    // Presentation cameras select detail; authority and animation always use one rig.
    public sealed class TankDistantLod : MonoBehaviour
    {
        [SerializeField] Renderer[] renderers=Array.Empty<Renderer>();
        [SerializeField] Mesh[] full=Array.Empty<Mesh>(),distant=Array.Empty<Mesh>();
        [SerializeField] Bounds referenceBounds;
        TankMechanismProfile art;
        public bool IsDistant {get;private set;}
        public Renderer[] Renderers=>renderers;
        public Mesh[] FullMeshes=>full;
        public Mesh[] DistantMeshes=>distant;
        public Bounds ReferenceBounds=>referenceBounds;
        public void Configure(Renderer[] sources,Mesh[] near,Mesh[] far,Bounds bounds)
        {if(sources.Length!=near.Length||near.Length!=far.Length)throw new ArgumentException("Tank detail representation");renderers=sources;full=near;distant=far;referenceBounds=bounds;SetDistant(false);}
        public void Select(Camera camera)
        {
            if(!camera){SetDistant(false);return;}
            art??=TankMechanismProfile.Load();
            var scale=transform.lossyScale;
            float size=Mathf.Max(referenceBounds.size.x*Mathf.Abs(scale.x),referenceBounds.size.y*Mathf.Abs(scale.y),referenceBounds.size.z*Mathf.Abs(scale.z));
            float depth=Vector3.Dot(transform.TransformPoint(referenceBounds.center)-camera.transform.position,camera.transform.forward);
            float height=camera.orthographic?2*camera.orthographicSize:2*depth*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f);
            SelectScreenRatio(depth>camera.nearClipPlane&&height>0?size/height:float.PositiveInfinity,art);
        }
        public void SelectScreenRatio(float ratio,TankMechanismProfile profile)
        {
            // Invalid/behind-camera projections fail closed to the original full model.
            bool next=!float.IsNaN(ratio)&&!float.IsInfinity(ratio)&&ratio>=0&&
                (IsDistant?ratio<profile.distantExitScreenHeight:ratio<=profile.distantEnterScreenHeight);
            if(next!=IsDistant)SetDistant(next);
        }
        internal void SetDistant(bool value)
        {
            IsDistant=value;var meshes=value?distant:full;
            for(int n=0;n<renderers.Length;n++){
                if(renderers[n] is SkinnedMeshRenderer skin){var bounds=skin.localBounds;skin.sharedMesh=meshes[n];skin.localBounds=bounds;}
                else renderers[n].GetComponent<MeshFilter>().sharedMesh=meshes[n];
            }
        }
    }
}
