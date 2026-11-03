#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
namespace Spacewars.Presentation
{
    // Isolated visual demonstration, not an ordinary-match or physical-device witness.
    public sealed class HawkVisualShowcase : MonoBehaviour
    {
        public GameObject PreviousPrefab;
        public Camera[] Cameras {get;private set;}
        public double Clock {get;private set;}
        public string Phase {get;private set;}="Сравнение моделей";
        public int LaunchCount {get;private set;} public int HitCount {get;private set;}
        public bool Automatic=true;
        GameObject[] craft=new GameObject[3]; HawkModelPresentation[] art=new HawkModelPresentation[3];
        Transform[] rotors=new Transform[3],tails=new Transform[3];
        HawkMotion motion;HawkMotionPreviewProfile movement;HawkVisualProfile tuning;PlayableProfile gameplay;
        HawkWeapon[] weapons=new HawkWeapon[3];Vector3[] centers=new Vector3[3];
        WebProjectileVisuals visuals=new WebProjectileVisuals();List<Material> materials=new List<Material>();
        sealed class Shot{public GameObject model;public HawkRocketFlight flight;public int lane;}
        sealed class Hit{public GameObject model;public double at;}
        List<Shot> shots=new List<Shot>();List<Hit> hits=new List<Hit>();
        Material Mat(Color c){var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.SetColor("_BaseColor",c);m.SetFloat("_Smoothness",.2f);materials.Add(m);return m;}
        void Part(string name,Vector3 p,Vector3 scale,Material m,int layer,PrimitiveType type=PrimitiveType.Cube)
        {var g=GameObject.CreatePrimitive(type);g.name=name;g.transform.SetParent(transform,false);g.transform.position=p;g.transform.localScale=scale;g.layer=layer;var col=g.GetComponent<Collider>();col.enabled=false;if(Application.isPlaying)Destroy(col);else DestroyImmediate(col);g.GetComponent<Renderer>().sharedMaterial=m;}
        public void Initialize()
        {
            if(Cameras!=null)return;
            movement=HawkMotionPreviewProfile.Load();tuning=HawkVisualProfile.Load();gameplay=PlayableProfile.Default;motion=new HawkMotion(movement.Settings());
            Cameras=new Camera[3];RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.48f,.52f,.58f);
            var light=new GameObject("Showcase light").AddComponent<Light>();light.transform.SetParent(transform);light.type=LightType.Directional;light.intensity=1.8f;light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(48,-35,0);
            var fill=new GameObject("Showcase fill").AddComponent<Light>();fill.transform.SetParent(transform);fill.type=LightType.Directional;fill.intensity=.5f;fill.transform.rotation=Quaternion.Euler(30,140,0);
            var floor=Mat(new Color(.16f,.20f,.23f));var line=Mat(new Color(.24f,.30f,.34f));var target=Mat(new Color(.65f,.24f,.13f));
            for(int i=0;i<3;i++)
            {
                int layer=20+i;centers[i]=new Vector3(i*40,0,0);
                craft[i]=Instantiate(i==0?PreviousPrefab:Resources.Load<GameObject>("StyleA/hawk"),transform);craft[i].name="Hawk comparison "+i;
                if(i>0){art[i]=craft[i].AddComponent<HawkModelPresentation>();art[i].SetUpgrade(i==2);}
                foreach(var t in craft[i].GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;
                var nodes=craft[i].GetComponentsInChildren<Transform>(true);rotors[i]=nodes.Single(t=>t.name=="mainRotor");tails[i]=nodes.Single(t=>t.name=="tailRotor");
                Part("Grid floor",centers[i]+Vector3.down*.15f,new Vector3(50,.2f,50),floor,layer);
                for(int k=-24;k<=24;k+=2){Part("Grid",centers[i]+new Vector3(k,-.04f,0),new Vector3(.018f,.01f,50),line,layer);Part("Grid",centers[i]+new Vector3(0,-.04f,k),new Vector3(50,.01f,.018f),line,layer);}
                var camera=new GameObject("Showcase camera "+i).AddComponent<Camera>();camera.transform.SetParent(transform);camera.rect=new Rect(i/3f,0,1/3f,1);camera.cullingMask=1<<layer;camera.orthographic=true;camera.orthographicSize=3.7f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.16f,.20f);camera.nearClipPlane=.1f;camera.farClipPlane=100;Cameras[i]=camera;
                weapons[i]=new HawkWeapon(i==2?gameplay.HawkUpgradedPreparationSeconds:gameplay.HawkPreparationSeconds,i==2?gameplay.HawkUpgradedSalvoDelay:gameplay.HawkSalvoDelay,i==2?gameplay.HawkUpgradedReloadSeconds:gameplay.HawkReloadSeconds);
                Part("Target",centers[i]+new Vector3(0,2.8f,6),Vector3.one*.7f,target,layer,PrimitiveType.Sphere);
            }
            Present();
        }
        void Command(double t,out float forward,out float strafe,out float turn)
        {
            forward=strafe=turn=0;
            if(t<0){Phase="Сравнение моделей";return;}
            if(t<2.2){Phase="Вперёд · разгон";forward=1;}
            else if(t<4){Phase="Торможение";}
            else if(t<6.2){Phase="Назад · короткий отход";forward=-1;}
            else if(t<6.7)Phase="Зависание";
            else if(t<8.5){Phase="Вправо · стрейф";strafe=1;}
            else if(t<9.7)Phase="Боковое торможение";
            else if(t<11.5){Phase="Влево · стрейф";strafe=-1;}
            else if(t<12.7)Phase="Выравнивание";
            else if(t<14.7){Phase="Разворот на месте";turn=1;}
            else if(t<15.7)Phase="Остановка разворота";
            else if(t<18.2){Phase="Поворот в движении";forward=1;turn=-.5f;}
            else if(t<21)Phase="Торможение и зависание";
            else Phase="Пуск · полёт · попадание · перезарядка";
        }
        public void Step(double dt)
        {
            Initialize();if(dt<=0)return;
            while(dt>1e-9){double step=Math.Min(dt,1d/120);dt-=step;Command(Clock-2,out float f,out float s,out float r);if(Clock>=2&&Clock<23)motion.Advance(step,f,s,r);Clock+=step;
                if(Clock>=23){for(int i=0;i<3;i++)foreach(var launch in weapons[i].Advance(step,true,1,_=>true))Launch(i,launch.Side);}
                Present();
                foreach(var shot in shots.ToArray()){
                    var from=shot.flight.Position;var target=centers[shot.lane]+new Vector3(0,2.8f,6);shot.flight.Advance(step,new BallisticPoint(target.x,target.y,target.z));var to=shot.flight.Position;
                    if(HawkRocketFlight.Contact(from,to,new BallisticPoint(target.x,target.y,target.z),new BallisticPoint(target.x,target.y,target.z),.35+gameplay.HawkMissileRadius,out double fraction))
                    {HitCount++;var effect=visuals.DirectImpact(transform,HitCount);Layer(effect,20+shot.lane);effect.transform.position=Vector3.Lerp(new Vector3((float)from.X,(float)from.Y,(float)from.Z),new Vector3((float)to.X,(float)to.Y,(float)to.Z),(float)fraction);hits.Add(new Hit{model=effect,at=Clock});Release(shot.model);shots.Remove(shot);}
                    else{shot.model.transform.position=new Vector3((float)to.X,(float)to.Y,(float)to.Z);var d=shot.flight.Direction;shot.model.transform.rotation=Quaternion.FromToRotation(Vector3.right,new Vector3((float)d.X,(float)d.Y,(float)d.Z));}
                }
                foreach(var hit in hits.ToArray()){float age=(float)(Clock-hit.at)/.4f;if(age>=1){Release(hit.model);hits.Remove(hit);}else visuals.UpdateDirectImpact(hit.model,age,.4f,.6f,.03f,.7f,.4f);}
            }
        }
        void Launch(int lane,int side)
        {
            var socket=craft[lane].GetComponentsInChildren<Transform>(true).Single(t=>t.name==(side==0?"muzzleLeft":"muzzleRight"));var p=socket.position;var target=centers[lane]+new Vector3(0,2.8f,6);var d=(target-p).normalized;
            var rocket=lane==0?visuals.Rocket(transform,(float)gameplay.HawkMissileRadius,(float)gameplay.HawkMissileLength,Color.cyan):visuals.HawkRocket(transform,(float)gameplay.HawkMissileRadius,(float)gameplay.HawkMissileLength,new Color(.12f,.5f,.7f));
            Layer(rocket,20+lane);rocket.transform.localScale=Vector3.one*(float)gameplay.HawkMissileVisualScale;
            visuals.Exhaust(rocket,(float)gameplay.HawkMissileLength*(lane==0?1:tuning.exhaustLength),(float)gameplay.HawkMissileRadius*(lane==0?1:tuning.exhaustWidth),(float)gameplay.WeaponMuzzleCoreRatio,(float)(lane==0?gameplay.ShkvalExhaustOpacity:tuning.exhaustOpacity));
            shots.Add(new Shot{model=rocket,lane=lane,flight=new HawkRocketFlight(1,new BallisticPoint(p.x,p.y,p.z),new BallisticPoint(d.x,d.y,d.z),gameplay.HawkMissileSpeed,0,5)});LaunchCount++;if(art[lane])art[lane].Trigger(side,Clock);
        }
        void Present()
        {
            for(int i=0;i<3;i++){
                bool fire=Clock>=23;var pos=centers[i]+new Vector3(fire?0:(float)motion.X,2,fire?0:(float)motion.Z);
                craft[i].transform.position=pos;craft[i].transform.rotation=fire?Quaternion.identity:Quaternion.Euler(0,(float)motion.Yaw,0)*Quaternion.Euler((float)motion.Pitch,0,(float)motion.Roll);
                if(art[i])art[i].Render(Clock,Clock,fire?0:(float)motion.Pitch,fire?0:(float)motion.Roll,movement.rotorSpeed);
                else{rotors[i].localRotation=Quaternion.Euler(0,(float)Clock*movement.rotorSpeed,0);tails[i].localRotation=Quaternion.Euler((float)Clock*movement.rotorSpeed,0,0);}
                var focus=pos+Vector3.up*.7f+(fire?Vector3.forward*2:Vector3.zero);Cameras[i].orthographicSize=fire?4.7f:3.7f;Cameras[i].transform.position=focus+new Vector3(5,9,7);Cameras[i].transform.LookAt(focus);
            }
        }
        static void Layer(GameObject g,int layer){foreach(var t in g.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;}
        static void Release(UnityEngine.Object o){if(Application.isPlaying)Destroy(o);else DestroyImmediate(o);}
        void Start(){Initialize();if(Application.isPlaying)StartCoroutine(Evidence());}
        void Update(){if(Automatic&&Application.isPlaying)Step(Time.deltaTime);}
        IEnumerator Evidence()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-hawkVisualEvidence");if(index<0)yield break;string output=args[index+1];Directory.CreateDirectory(output);
            Screen.SetResolution(1800,960,FullScreenMode.Windowed);yield return null;
            var capture=gameObject.AddComponent<HawkVideoCapture>();capture.Begin(output);double[] times={.5,3,6.7,10,13,16,21,23.5,24,25,28};int next=0;
            while(Clock<31){yield return new WaitForEndOfFrame();if(next<times.Length&&Clock>=times[next]){ScreenCapture.CaptureScreenshot(Path.Combine(output,"frame-"+next.ToString("D2")+".png"));next++;}}
            yield return capture.Finish();File.WriteAllText(Path.Combine(output,"result.json"),"{\"scope\":\"isolated actual Unity Player presentation\",\"seconds\":"+Clock.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"launches\":"+LaunchCount+",\"hits\":"+HitCount+"}");
            if(LaunchCount<6||HitCount<6)throw new InvalidOperationException("Showcase did not demonstrate complete salvos");Debug.Log("HAWK_VISUAL_PLAYER_PASS");Application.Quit();
        }
        void OnGUI(){var style=new GUIStyle(GUI.skin.label){fontSize=24,alignment=TextAnchor.MiddleCenter};string[] labels={"ПРЕЖНЯЯ МОДЕЛЬ","НОВАЯ · БАЗОВЫЙ","НОВАЯ · УЛУЧШЕННЫЙ"};for(int i=0;i<3;i++)GUI.Label(new Rect(i*Screen.width/3f,12,Screen.width/3f,45),labels[i],style);GUI.Label(new Rect(0,Screen.height-72,Screen.width,34),Phase,style);style.fontSize=15;GUI.Label(new Rect(0,Screen.height-38,Screen.width,25),"Изолированная демонстрация Unity · одинаковый сценарий движения · не приёмка обычного матча",style);}
        void OnDestroy(){visuals.Dispose();foreach(var m in materials)Release(m);}
    }
}
#endif
