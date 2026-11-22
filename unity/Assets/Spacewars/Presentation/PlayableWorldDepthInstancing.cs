using UnityEngine;
using UnityEngine.Rendering;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        internal void SetSplitScreenDepthInstancing(bool enabled)
        {
            // The world owns this material. Only its depth pass has an instancing variant.
            material.enableInstancing = enabled && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal &&
                SystemInfo.supportsInstancing;
        }
    }
}
