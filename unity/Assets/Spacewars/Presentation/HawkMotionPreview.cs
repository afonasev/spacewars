using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
namespace Spacewars.Presentation
{
 public sealed class HawkMotionPreview : MonoBehaviour
 {
  public HawkMotion Motion {get;private set;}
  public HawkMotionPreviewProfile Profile {get;private set;}
  public Camera PreviewCamera {get;private set;}
  public double Clock {get;private set;}
  public bool Demo=true;
  public string Phase {get;private set;}="Зависание";
  public const double DemoSeconds=22; // Recorded demonstration timeline, not motion tuning.
  GameObject world,craft;Transform rotor,tail;readonly List<Material> materials=new List<Material>();
  HawkMotionPreviewProfile active;Vector3 command;bool keyboardWasActive,tuning;Vector2 scroll;
  static void Release(UnityEngine.Object o){if(!o)return;if(Application.isPlaying)Destroy(o);else DestroyImmediate(o);}
  void Start(){if(!world)Initialize();}
  void OnDestroy(){Cleanup();}
  void Cleanup(){if(world){world.SetActive(false);Release(world);}world=null;foreach(var m in materials)Release(m);materials.Clear();}
  Material Mat(Color c){var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.SetColor("_BaseColor",c);materials.Add(m);return m;}
  void Box(string name,Vector3 pos,Vector3 scale,Material mat){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(world.transform,false);g.transform.position=pos;g.transform.localScale=scale;Release(g.GetComponent<Collider>());g.GetComponent<Renderer>().sharedMaterial=mat;}
  public void Initialize(){
   Cleanup();Profile=Profile??HawkMotionPreviewProfile.Load();Profile.Validate();active=JsonUtility.FromJson<HawkMotionPreviewProfile>(JsonUtility.ToJson(Profile));
   Motion=new HawkMotion(active.Settings());Clock=0;command=Vector3.zero;Phase="Зависание";keyboardWasActive=false;
   world=new GameObject("Hawk motion laboratory");world.transform.SetParent(transform,false);
   craft=Instantiate(Resources.Load<GameObject>("StyleA/hawk"),world.transform);
   var nodes=craft.GetComponentsInChildren<Transform>();rotor=nodes.Single(x=>x.name=="mainRotor");tail=nodes.Single(x=>x.name=="tailRotor");
   // Authored inspection-floor geometry and fixed camera angle; the overview scale is tunable.
   Box("Площадка",new Vector3(0,-.2f,0),new Vector3(60,.3f,60),Mat(new Color(.18f,.22f,.24f)));
   var lines=Mat(new Color(.3f,.36f,.38f));for(int i=-28;i<=28;i+=2){Box("Линия",new Vector3(i,-.04f,0),new Vector3(.025f,.01f,60),lines);Box("Линия",new Vector3(0,-.04f,i),new Vector3(60,.01f,.025f),lines);}
   Box("Начало",new Vector3(0,-.02f,0),new Vector3(1,.01f,1),Mat(new Color(.65f,.45f,.1f)));
   RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.54f,.58f);
   var light=new GameObject("Light").AddComponent<Light>();light.transform.SetParent(world.transform,false);light.type=LightType.Directional;light.intensity=1.8f;light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(50,-30,0);
   PreviewCamera=new GameObject("Motion camera").AddComponent<Camera>();PreviewCamera.transform.SetParent(world.transform,false);PreviewCamera.orthographic=true;PreviewCamera.orthographicSize=active.cameraSize;PreviewCamera.clearFlags=CameraClearFlags.SolidColor;PreviewCamera.backgroundColor=new Color(.07f,.095f,.12f);
   Present();
  }
  public void SetCommand(float forward,float strafe,float turn){Demo=false;command=new Vector3(strafe,turn,forward);Phase="Ручное управление";}
  public void Step(double dt){
   if(Motion==null)throw new InvalidOperationException("Initialize first");
   if(dt<=0||double.IsNaN(dt)||double.IsInfinity(dt))throw new ArgumentOutOfRangeException(nameof(dt));
   // Keep command transitions and motion on bounded substeps, independent of render frame rate.
   for(double remaining=dt;remaining>1e-12;){double step=Math.Min(remaining,1d/120);remaining-=step;
    if(Demo)DemoCommand(Math.Min(Clock,DemoSeconds));Motion.Advance(step,command.z,command.x,command.y);Clock+=step;
   }
   rotor.Rotate(Vector3.up,active.rotorSpeed*(float)dt,Space.Self);tail.Rotate(Vector3.right,active.rotorSpeed*(float)dt,Space.Self);Present();
  }
  void DemoCommand(double t){command=Vector3.zero;
   if(t<.8)Phase="Зависание";
   else if(t<3){Phase="Разгон вперёд · нос вниз";command.z=1;}
   else if(t<4.8)Phase="Торможение · нос вверх";
   else if(t<7){Phase=Motion.ReverseExhausted?"Отход завершён · автоторможение":"Короткий отход назад";command.z=-1;}
   else if(t<7.5)Phase="Зависание";
   else if(t<9.3){Phase="Стрейф вправо · курс сохранён";command.x=1;}
   else if(t<10.5)Phase="Торможение бокового движения";
   else if(t<12.3){Phase="Стрейф влево · курс сохранён";command.x=-1;}
   else if(t<13.5)Phase="Возврат к зависанию";
   else if(t<15.5){Phase="Плавный разворот на месте";command.y=1;}
   else if(t<16.5)Phase="Остановка разворота";
   else if(t<19){Phase="Поворот в полёте · крен";command.z=1;command.y=-.5f;}
   else if(t<21)Phase="Торможение и выравнивание";
   else Phase="Зависание";
  }
  void Present(){
   var pos=new Vector3((float)Motion.X,active.hoverHeight,(float)Motion.Z);craft.transform.position=pos;
   craft.transform.rotation=Quaternion.Euler(0,(float)Motion.Yaw,0)*Quaternion.Euler((float)Motion.Pitch,0,(float)Motion.Roll);
   PreviewCamera.transform.position=pos+new Vector3(10,8,-10);PreviewCamera.transform.LookAt(pos);
  }
  void Update(){
   if(!world)return;
   if(!Demo&&Keyboard.current!=null){var k=Keyboard.current;var v=new Vector3((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),(k.eKey.isPressed?1:0)-(k.qKey.isPressed?1:0),(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0));
    bool pressed=v.sqrMagnitude>0;if(pressed||keyboardWasActive)command=v;keyboardWasActive=pressed;
   }
   if(Time.deltaTime>0)Step(Time.deltaTime);
  }
  void OnGUI(){if(!world)return;GUILayout.BeginArea(new Rect(14,14,310,Screen.height-28),GUI.skin.box);
   GUILayout.Label("ЯСТРЕБ · МАНЁВРЫ");GUILayout.Label(Phase);GUILayout.Label($"Скорость {Motion.Speed:0.0} м/с · курс {Motion.Yaw:0}°");
   if(GUILayout.Button("Демонстрация заново")){Demo=true;Initialize();}
   if(GUILayout.Button("Вперёд / W"))SetCommand(1,0,0);
   if(GUILayout.Button("Короткий отход / S"))SetCommand(-1,0,0);
   GUILayout.BeginHorizontal();if(GUILayout.Button("Влево / A"))SetCommand(0,-1,0);if(GUILayout.Button("Вправо / D"))SetCommand(0,1,0);GUILayout.EndHorizontal();
   GUILayout.BeginHorizontal();if(GUILayout.Button("Разворот / Q"))SetCommand(0,0,-1);if(GUILayout.Button("Разворот / E"))SetCommand(0,0,1);GUILayout.EndHorizontal();
   if(GUILayout.Button("Тормозить / ручной режим"))SetCommand(0,0,0);
   tuning=GUILayout.Toggle(tuning,"Настройки манёвров");if(tuning){GUILayout.Label("Применяются при новом старте.");scroll=GUILayout.BeginScrollView(scroll);foreach(var p in Profile.Parameters()){GUILayout.Label(new GUIContent($"{p.Label}: {p.Read():0.##} {p.Unit}",p.Description));float v=GUILayout.HorizontalSlider(p.Read(),p.Minimum,p.Maximum);p.Write(Mathf.Clamp(Mathf.Round(v/p.Step)*p.Step,p.Minimum,p.Maximum));}GUILayout.EndScrollView();}
   GUILayout.EndArea();
  }
 }
}
