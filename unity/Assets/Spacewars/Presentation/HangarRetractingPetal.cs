using UnityEngine;
namespace Spacewars.Presentation
{
    // Render-only aperture clipping: the moving metal slides under the opaque rim.
    // Dimensions match RingHangarAssets, not gameplay footprint/clearance tuning.
    public sealed class HangarRetractingPetal : MonoBehaviour
    {
        const float Aperture=.965f;
        Mesh mesh;Vector3[] authored,vertices;float last=-1;
        public void SetOpen(float open,int blade)
        {
            open=Mathf.Clamp01(open);
            if(!mesh){var filter=GetComponentInChildren<MeshFilter>();mesh=Instantiate(filter.sharedMesh);mesh.name="Retracting hangar petal";filter.sharedMesh=mesh;authored=mesh.vertices;vertices=new Vector3[authored.Length];}
            if(open==last)return;last=open;
            // A full diameter clears every point, including the curved tip.
            float angle=blade*Mathf.PI/4+Mathf.PI/8;
            var offset=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(2*Aperture*open);
            for(int i=0;i<vertices.Length;i++){
                var v=authored[i]+offset;float radius=new Vector2(v.x,v.z).magnitude;
                // Vertices past the rim collapse onto its hidden inner edge. Nothing
                // can protrude outside the body, even halfway through the animation.
                if(radius>Aperture){v.x*=Aperture/radius;v.z*=Aperture/radius;}
                vertices[i]=v;
            }
            mesh.vertices=vertices;mesh.RecalculateBounds();
            foreach(var r in GetComponentsInChildren<Renderer>())r.enabled=open<1;
        }
        void OnDestroy(){if(mesh){if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}}
    }
}
