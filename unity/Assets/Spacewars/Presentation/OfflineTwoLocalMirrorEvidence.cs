#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Spacewars.Input;

namespace Spacewars.Presentation
{
    public sealed partial class OfflineTwoLocalBootstrap
    {
        // Asymmetric GPU-only fixtures. Diagnostic geometry/colors/tolerances are
        // fixed evidence parameters, never gameplay balance or world authority.
        [Serializable] private sealed class MirrorPixelProof {public int seat,fixture;public float worldX,worldY,worldZ,screenX,screenY;public int matchingPixels;}
        [Serializable] private sealed class MirrorProof {public bool passed,syntheticDevices=true,physicalAcceptance=false,unregisteredCameraExcluded;public string profile;public int seed=19092026;public MirrorPixelProof[] probes;}
        private IEnumerator MirrorSyntheticEvidence()
        {
            var objects=new List<GameObject>();var colors=new[]{Color.green,Color.magenta,Color.cyan,Color.yellow};var points=new List<Vector3>();var proof=new List<MirrorPixelProof>();
            var material=new Material(Resources.Load<Material>("RuntimeLit"));
            var diagnostic=new GameObject("Unregistered disabled mirror probe camera");var other=diagnostic.AddComponent<Camera>();other.enabled=false;
            bool excluded=!OfflineCameraMirrorFeature.IsRegistered(other);
            try {
                for(int seat=0;seat<2;seat++)for(int fixture=0;fixture<2;fixture++){
                    var view=views[seat];var point=view.World.Point(new Spacewars.Simulation.NavPoint(view.CameraState.X+(fixture==0?3:-2),view.CameraState.Z+(fixture==0?1:-2)))+Vector3.up*.75f;
                    var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name="GPU mirror seat "+seat+" fixture "+fixture;cube.transform.SetParent(view.Root);cube.layer=8+seat;cube.transform.position=point;cube.transform.localScale=new Vector3(.55f,1.5f,.55f);Destroy(cube.GetComponent<Collider>());cube.GetComponent<Renderer>().sharedMaterial=material;
                    var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",colors[seat*2+fixture]);cube.GetComponent<Renderer>().SetPropertyBlock(block);objects.Add(cube);points.Add(point);
                }
                for(int frame=0;frame<4;frame++)yield return null;
                yield return new WaitForEndOfFrame();var texture=ScreenCapture.CaptureScreenshotAsTexture();
                try{
                    File.WriteAllBytes(Path.Combine(syntheticEvidence,"per-camera-asymmetric-gpu.png"),texture.EncodeToPNG());
                    for(int index=0;index<points.Count;index++){
                        int seat=index/2;var screen=OfflinePadCamera.ProjectScreen(views[seat].Camera,points[index]);var expected=colors[index];int matching=0;
                        for(int dy=-8;dy<=8;dy++)for(int dx=-8;dx<=8;dx++){
                            int x=Mathf.RoundToInt(screen.x)+dx,y=Mathf.RoundToInt(screen.y)+dy;if(x<0||y<0||x>=texture.width||y>=texture.height)continue;var pixel=texture.GetPixel(x,y);
                            bool Channel(float value,float wanted)=>wanted>.5f?value>.3f:value<.25f;
                            if(Channel(pixel.r,expected.r)&&Channel(pixel.g,expected.g)&&Channel(pixel.b,expected.b))matching++;
                        }
                        proof.Add(new MirrorPixelProof{seat=seat,fixture=index%2,worldX=points[index].x,worldY=points[index].y,worldZ=points[index].z,screenX=screen.x,screenY=screen.y,matchingPixels=matching});
                    }
                }finally{Destroy(texture);}
                bool passed=excluded&&proof.TrueForAll(p=>p.matchingPixels>=25);
                File.WriteAllText(Path.Combine(syntheticEvidence,"per-camera-asymmetric-gpu.json"),JsonUtility.ToJson(new MirrorProof{passed=passed,profile=profile.ProfileId+"@"+profile.Revision,unregisteredCameraExcluded=excluded,probes=proof.ToArray()},true));
                if(!passed)throw new InvalidOperationException("Per-camera mirrored world color differs from SOURCE logical projection; inspect asymmetric GPU evidence.");
            }finally{foreach(var obj in objects)Destroy(obj);Destroy(material);Destroy(diagnostic);}
            for(int frame=0;frame<3;frame++)yield return null;
        }
    }
}
#endif
