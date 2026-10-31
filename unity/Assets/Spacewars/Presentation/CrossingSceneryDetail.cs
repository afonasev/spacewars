using UnityEngine;
namespace Spacewars.Presentation
{
    // Each compact prop owns its bounds; a map-wide batch cannot pin it to close LOD.
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class CrossingSceneryDetail : MonoBehaviour
    {
        ModelDetailLod detail;
        public void Bind(ModelDetailLod value)=>detail=value;
        void OnWillRenderObject(){if(detail&&Camera.current)detail.Select(Camera.current);}
    }
}
