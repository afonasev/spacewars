using UnityEngine;
namespace Spacewars.Presentation
{
    [RequireComponent(typeof(Camera))]
    public sealed class ContactOcclusionCamera:MonoBehaviour
    {
        public ContactOcclusionProfile Profile {get;private set;}
        public void Configure(ContactOcclusionProfile profile){profile.Validate();Profile=profile;}
    }
}
