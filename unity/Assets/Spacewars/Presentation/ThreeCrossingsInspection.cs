using System;
using System.Collections;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Runtime;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Explicit native diagnostic mode. It shares production terrain/profile/router,
    // reveals layout only for inspection and never changes normal match fog or units.
    public sealed class ThreeCrossingsInspection:MonoBehaviour
    {
        private PlayableProfile profile;private PlayableWorld world;private Camera view;private string output;private string angle="TOP";
        private IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();output=args[Array.IndexOf(args,"-threeCrossingsEvidence")+1];Directory.CreateDirectory(output);
            profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
            var m=(ThreeCrossingsMap)profile.AuthoredMap;AudioListener.volume=0;Application.runInBackground=true;Application.targetFrameRate=profile.RenderTargetFramesPerSecond;
            RenderSettings.ambientLight=new Color(.63f,.69f,.73f);var sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.4f;sun.transform.rotation=Quaternion.Euler(48,-30,0);
            world=new PlayableWorld(transform,profile);world.InspectionReveal();
            bool artEvidence=Array.IndexOf(args,"-environmentArtEvidence")>=0;
            if(!artEvidence)world.InspectionSites();
            view=new GameObject("Inspection camera").AddComponent<Camera>();view.orthographic=true;view.orthographicSize=(float)m.OverviewSize;view.nearClipPlane=.1f;view.farClipPlane=400;view.backgroundColor=new Color(.075f,.10f,.13f);view.clearFlags=CameraClearFlags.SolidColor;
            view.transform.position=new Vector3(0,(float)m.OverviewHeight,0);view.transform.rotation=Quaternion.Euler(90,0,0);
            yield return null;
            var report=new System.Text.StringBuilder();report.AppendLine("NATIVE MATERIAL DIAGNOSTIC — NOT HUMAN ACCEPTANCE");report.AppendLine(m.Id+"@"+m.Revision+" / "+profile.ProfileId+"@"+profile.Revision+" / source "+profile.SourceProfileId+"@"+profile.SourceProfileRevision);report.AppendLine("Surface "+world.SurfaceRevision+" / Environment "+world.EnvironmentRevision);report.AppendLine("Fixed authored layout; no procedural seed. Native baseline start roster preserved in regular match.");
            var geometry=new NavGeometry(m.HalfExtent,PlayableMap.StaticObstacles(profile),m.Revision);
            using(var router=new UnityNavigationRouter(geometry,profile.Navigation))
            {
                report.AppendLine("Native NavMesh bake ms="+router.BuildMilliseconds);
                foreach(double z in new[]{-m.CrossingZ,0,m.CrossingZ})foreach(int direction in new[]{-1,1})Check(router,geometry,new NavPoint(direction*-18,z),new NavPoint(direction*18,z),report);
                foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1})Check(router,geometry,new NavPoint(side*m.PocketX,end*24),new NavPoint(side*m.PocketX,end*6),report);
            }
            File.WriteAllText(Path.Combine(output,"native-inspection.txt"),report.ToString());
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"native-top.png"));
            yield return new WaitForSecondsRealtime(1);angle="HEIGHT";view.transform.position=new Vector3(0,(float)m.OverviewHeight,(float)m.OverviewOffset);view.transform.LookAt(Vector3.zero);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"native-height.png"));
            yield return new WaitForSecondsRealtime(1);angle="BRIDGE SUPPORT";
            view.orthographicSize=(float)profile.CameraMinZoom;
            view.transform.position=new Vector3((float)profile.CameraOffsetZ,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);view.transform.LookAt(Vector3.zero);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"native-height-detail.png"));
            if(artEvidence)
            {
                yield return new WaitForSecondsRealtime(1);
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"native-water-later.png"));
            }
            yield return new WaitForSecondsRealtime(1);angle="PLATFORM / RAMPS";view.orthographicSize=(float)m.PlatformInspectionSize;
            var platform=new Vector3((float)-m.BaseCoordinate,0,(float)-m.BaseCoordinate);view.transform.position=platform+new Vector3((float)profile.CameraOffsetZ,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);view.transform.LookAt(platform);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"native-platform-detail.png"));
            if(artEvidence)
            {
                yield return new WaitForSecondsRealtime(1);angle="NATURAL TRANSITIONS";
                var rock=m.Solids.First(r=>r.Top>=m.WaterDepth&&r.Bottom<=0&&r.MaxX-r.MinX<20);
                var contact=new Vector3((float)((rock.MinX+rock.MaxX)/2),0,(float)rock.MinZ);
                view.orthographicSize=(float)profile.CameraMinZoom;
                view.transform.position=contact+new Vector3((float)profile.CameraOffsetZ,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);view.transform.LookAt(contact);
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"native-natural-detail.png"));
            }
            yield return new WaitForSecondsRealtime(1);angle="FULL FOG";world.InspectionHide();
            view.orthographicSize=(float)m.OverviewSize;view.transform.position=new Vector3(0,(float)m.OverviewHeight,0);view.transform.rotation=Quaternion.Euler(90,0,0);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"native-fog.png"));
            yield return new WaitForSecondsRealtime(1);Application.Quit();
        }
        private void Check(UnityNavigationRouter router,NavGeometry geometry,NavPoint a,NavPoint b,System.Text.StringBuilder report)
        {
            var path=router.FindPath(a,b);if(path.Length==0)throw new InvalidOperationException("Native route missing");var previous=a;foreach(var point in path){if(!geometry.SegmentFree(previous,point,profile.TankCollisionRadius))throw new InvalidOperationException("Native route violates authoritative support");previous=point;}
            report.AppendLine("PASS native route "+a.X+","+a.Z+" -> "+b.X+","+b.Z+" corners="+path.Length);
        }
        private void OnGUI()
        {
            if(profile==null||view==null)return;var title=new GUIStyle(GUI.skin.label){fontSize=24,alignment=TextAnchor.UpperCenter};title.normal.textColor=Color.white;
            GUI.Label(new Rect(0,10,Screen.width,40),"ТРИ ПЕРЕПРАВЫ · МАТЕРИАЛЫ · "+angle,title);
            var label=new GUIStyle(GUI.skin.label){fontSize=18,alignment=TextAnchor.MiddleCenter};label.normal.textColor=Color.white;
            if(angle!="FULL FOG")foreach(var site in TerritoryRules.Sites(profile)){var p=view.WorldToScreenPoint(new Vector3((float)site.Position.X,0,(float)site.Position.Z));string text=site.Id==1?"A":site.Id==2?"B":site.Kind==PlayableBuildingKind.Outpost?"Ф":"Ш";GUI.Label(new Rect(p.x-20,Screen.height-p.y-35,40,25),text,label);}
            label.fontSize=16;GUI.Label(new Rect(0,Screen.height-35,Screen.width,30),profile.AuthoredMap.Id+"@"+profile.AuthoredMap.Revision+" · "+world.SurfaceRevision+" · "+world.EnvironmentRevision,label);
        }
        private void OnDestroy(){world?.Dispose();}
    }
}
