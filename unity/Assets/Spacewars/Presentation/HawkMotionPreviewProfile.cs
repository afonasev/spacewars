using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Spacewars.Simulation;
namespace Spacewars.Presentation {
[Serializable] public sealed class HawkMotionPreviewProfile {
 public string id="hawk-motion-preview-v1";
 public int revision=1;
 [SurfaceSetting("Манёвры", "Скорость вперёд", "Предельная скорость полёта вперёд.", "м/с", 1f, 8f, 0.1f)] public float forwardSpeed=2.8f;
 [SurfaceSetting("Манёвры", "Скорость отхода", "Скорость короткого отхода назад.", "м/с", 0.1f, 0.9f, 0.1f)] public float reverseSpeed=0.7f;
 [SurfaceSetting("Манёвры", "Скорость стрейфа", "Боковая скорость без смены курса.", "м/с", 0.1f, 4f, 0.1f)] public float strafeSpeed=1.6f;
 [SurfaceSetting("Манёвры", "Разгон", "Плавность набора скорости.", "м/с²", 0.2f, 8f, 0.1f)] public float acceleration=2f;
 [SurfaceSetting("Манёвры", "Торможение", "Скорость гашения движения.", "м/с²", 0.2f, 10f, 0.1f)] public float braking=2.8f;
 [SurfaceSetting("Манёвры", "Длительность отхода", "После этого времени удержания задний ход сам тормозит; отпусти команду для нового отхода.", "с", 0.2f, 2f, 0.1f)] public float reverseSeconds=1f;
 [SurfaceSetting("Манёвры", "Скорость разворота", "Предельная угловая скорость курса.", "°/с", 10f, 90f, 1f)] public float yawSpeed=55f;
 [SurfaceSetting("Манёвры", "Разгон разворота", "Плавность начала и остановки разворота.", "°/с²", 10f, 180f, 5f)] public float yawAcceleration=80f;
 [SurfaceSetting("Манёвры", "Наклон вперёд", "Наклон носа в установившемся полёте вперёд.", "°", 1f, 12f, 1f)] public float cruisePitch=5f;
 [SurfaceSetting("Манёвры", "Наклон при разгоне", "Дополнительное опускание носа при разгоне и подъём при торможении.", "°", 1f, 20f, 1f)] public float accelerationPitch=10f;
 [SurfaceSetting("Манёвры", "Крен стрейфа", "Наклон корпуса при боковом разгоне.", "°", 1f, 20f, 1f)] public float strafeBank=12f;
 [SurfaceSetting("Манёвры", "Крен поворота", "Крен при повороте в движении.", "°", 1f, 20f, 1f)] public float turnBank=9f;
 [SurfaceSetting("Манёвры", "Плавность наклона", "Максимальная скорость изменения наклона корпуса.", "°/с", 5f, 90f, 1f)] public float attitudeSpeed=32f;
 [SurfaceSetting("Манёвры", "Высота", "Высота зависания в проверочной сцене.", "м", 1f, 6f, 0.1f)] public float hoverHeight=2.5f;
 [SurfaceSetting("Манёвры", "Камера", "Вертикальный полуразмер обзора.", "м", 5f, 15f, 0.5f)] public float cameraSize=6.5f;
 [SurfaceSetting("Манёвры", "Винт", "Скорость основного и хвостового винтов.", "°/с", 90f, 2400f, 30f)] public float rotorSpeed=900f;
 public IEnumerable<SurfaceParameter> Parameters() {
 foreach(var f in GetType().GetFields()) { var a=f.GetCustomAttribute<SurfaceSettingAttribute>();
 if(a!=null) yield return new SurfaceParameter("preview.hawkMotion."+f.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)f.GetValue(this),v=>f.SetValue(this,v)); }
 }
 public void Validate() { if(id!="hawk-motion-preview-v1"||revision<1) throw new ArgumentException("Invalid motion profile identity");
 foreach(var p in Parameters()) if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum) throw new ArgumentException(p.Path);
 Settings().Validate(); }
 public HawkMotionSettings Settings() => new HawkMotionSettings {ForwardSpeed=forwardSpeed,ReverseSpeed=reverseSpeed,StrafeSpeed=strafeSpeed,Acceleration=acceleration,Braking=braking,ReverseSeconds=reverseSeconds,YawSpeed=yawSpeed,YawAcceleration=yawAcceleration,CruisePitch=cruisePitch,AccelerationPitch=accelerationPitch,StrafeBank=strafeBank,TurnBank=turnBank,AttitudeSpeed=attitudeSpeed};
 public static HawkMotionPreviewProfile Load() { var text=Resources.Load<TextAsset>("HawkMotionPreviewProfile");
 if(!text) throw new InvalidOperationException("Missing motion profile"); var p=JsonUtility.FromJson<HawkMotionPreviewProfile>(text.text);p.Validate();return p; }
}}
