using UnityEngine;

namespace Spacewars.Presentation
{
    // Authored presentation anchors of the existing perimeter bypass and causeway.
    // They are not navigable surfaces and never participate in the route provider.
    public static class BasaltCrownRoads
    {
        public const float RingX=40, RingZ=32, CausewayHalfWidth=4;
        public static float SignedDistance(float x,float z,BasaltCrownArtProfile art)
        {
            float outer=Mathf.Max(Mathf.Abs(x)-RingX-art.bypassWidth*.5f,Mathf.Abs(z)-RingZ-art.bypassWidth*.5f);
            float inner=Mathf.Max(Mathf.Abs(x)-RingX+art.bypassWidth*.5f,Mathf.Abs(z)-RingZ+art.bypassWidth*.5f);
            float ring=Mathf.Max(outer,-inner);
            float central=Mathf.Max(Mathf.Abs(x)-RingX,Mathf.Abs(z)-CausewayHalfWidth);
            return Mathf.Min(ring,central);
        }
        public static float Coverage(float x,float z,BasaltCrownArtProfile art)
        {
            return 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(-art.roadFeather,art.roadFeather,SignedDistance(x,z,art)));
        }
    }
}
