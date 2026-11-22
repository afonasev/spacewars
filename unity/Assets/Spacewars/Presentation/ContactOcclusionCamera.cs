using System;
using UnityEngine;
namespace Spacewars.Presentation
{
    [RequireComponent(typeof(Camera))]
    public sealed class ContactOcclusionCamera:MonoBehaviour
    {
        public ContactOcclusionProfile Profile {get;private set;}
        internal bool TemporalEnabled { get; private set; }
        internal event Action<Camera> ReleaseTemporalHistory;

        public void Configure(ContactOcclusionProfile profile)
        {
            profile.Validate();
            Profile = profile;
            if (profile.intensity <= 0) ReleaseHistory();
        }

        internal void SetTemporalEnabled(bool enabled)
        {
            TemporalEnabled = enabled;
            if (!enabled) ReleaseHistory();
        }

        private void OnDisable() => ReleaseHistory();

        private void ReleaseHistory()
        {
            if (ReleaseTemporalHistory != null)
                ReleaseTemporalHistory(GetComponent<Camera>());
        }
    }
}
