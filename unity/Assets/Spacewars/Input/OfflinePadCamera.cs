using System;
using UnityEngine;

namespace Spacewars.Input
{
    // Projection geometry is frozen from BattleRenderer.makeCamera, not a new
    // gameplay tuning profile. Zoom/clamp/home values use source Balance Lab metadata.
    public sealed class OfflinePadCamera
    {
        public double X{get;private set;} public double Z{get;private set;} public double Zoom{get;private set;}
        private readonly double homeX,homeZ,homeZoom;
        public OfflinePadCamera(double x,double z,NativeLocalInputProfile profile){X=homeX=x;Z=homeZ=z;Zoom=homeZoom=profile.cameraDefaultZoom;}
        public void Focus(double x,double z){X=x;Z=z;}
        public void AdjustZoom(double delta,NativeLocalInputProfile profile){Zoom=Math.Clamp(Zoom+delta,profile.cameraMinZoom,profile.cameraMaxZoom);}
        public void Reset(){X=homeX;Z=homeZ;Zoom=homeZoom;}
        public void Apply(Camera camera)
        {
            camera.orthographic=false;camera.fieldOfView=48;camera.nearClipPlane=.1f;camera.farClipPlane=700;
            camera.transform.position=new Vector3((float)X,(float)(Zoom*1.35),(float)(Z-Zoom*.72));
            camera.transform.rotation=Quaternion.LookRotation(new Vector3((float)X,0,(float)Z)-camera.transform.position,Vector3.up);
        }
        public static Vector3 ProjectViewport(Camera camera,Vector3 point){var p=camera.WorldToViewportPoint(point);return new Vector3(1-p.x,p.y,p.z);}
        public static Vector3 ProjectScreen(Camera camera,Vector3 point){var p=ProjectViewport(camera,point);var r=camera.pixelRect;return new Vector3(r.x+p.x*r.width,r.y+p.y*r.height,p.z);}
        public static Ray ScreenRay(Camera camera,Vector2 point){var r=camera.pixelRect;return camera.ViewportPointToRay(new Vector3(1-(point.x-r.x)/r.width,(point.y-r.y)/r.height,0));}
        public static float PixelsPerMeter(Camera camera,Vector3 point)
        {
            var a=camera.WorldToScreenPoint(point);var b=camera.WorldToScreenPoint(point+camera.transform.right);
            return Vector2.Distance(new Vector2(a.x,a.y),new Vector2(b.x,b.y));
        }
    }
}
