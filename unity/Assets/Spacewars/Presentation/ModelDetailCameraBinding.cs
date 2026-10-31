using UnityEngine;
namespace Spacewars.Presentation
{
    // Preview-only surfaces have no PlayableWorld camera update loop.
    public sealed class ModelDetailCameraBinding : MonoBehaviour
    {
        ModelDetailLod detail;
        public void Bind(ModelDetailLod value)=>detail=value;
        void OnWillRenderObject(){if(detail&&Camera.current)detail.Select(Camera.current);}
    }
}
