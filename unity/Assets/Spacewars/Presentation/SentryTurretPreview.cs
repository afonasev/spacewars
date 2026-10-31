using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Spacewars.Presentation
{
    // Explicit inspection scene. Does not issue match commands or own gameplay HP/damage.
    public sealed class SentryTurretPreview : MonoBehaviour
    {
        public SentryTurretPreviewProfile Profile { get; private set; }
        public SentryTurretRig Rig { get; private set; }
        public Camera PreviewCamera { get; private set; }
        public string Phase { get; private set; }
        public double Clock { get; private set; }
        public int Shots { get; private set; }
        public int TurningShots { get; private set; }
        public int VerticalShots { get; private set; }
        public int BurstSize => PlayableProfile.Default.ExplorerAssaultBurstSize;
        public int ReferenceHealth => 3 * PlayableProfile.Default.TankHealth;
        public bool Automatic = true, Firing = true, ShowControls = true;
        public Vector3 ManualTarget;
        public float LastSpread { get; private set; }
        public readonly List<Shot> ShotLog = new List<Shot>();
        public struct Shot { public double time; public float spread; public bool turning; public Vector3 origin, direction; }
        sealed class Trace { public LineRenderer line; public Vector3 from, direction; public float age; }
        readonly List<Trace> traces = new List<Trace>();
        readonly List<Material> materials = new List<Material>();
        GameObject world, target;
        Material traceMaterial;
        GameObject[] flashes;
        float[] flashAges;
        int burstRemaining, sequence;
        double nextShot, accumulator;
        Vector2 scroll;
        bool tuning;
        const double FixedStep = 1d / 60; // Preview sampling frequency, not a gameplay tuning constant.
        const float Cycle = 12; // Authored demonstration choreography; no match timing is changed.
        void Start() { if (!world) Initialize(); }
        void OnDestroy() { Cleanup(); }
        static void Release(UnityEngine.Object o) { if (!o) return; if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }
        void Cleanup()
        {
            if (world) { world.SetActive(false); Release(world); } world = null;
            foreach (var m in materials) Release(m); materials.Clear(); traces.Clear();
        }
        Material Mat(Color color, bool unlit = false)
        {
            var m = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", color); if (!unlit) m.SetFloat("_Smoothness", .22f); materials.Add(m); return m;
        }
        GameObject Primitive(string name, PrimitiveType type, Vector3 location, Vector3 scale, Material material, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent ? parent : world.transform, false);
            go.transform.position = location; go.transform.localScale = scale; Release(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material; return go;
        }
        GameObject Asset(string resource, Vector3 position)
        {
            var prefab = Resources.Load<GameObject>("StyleA/" + resource);
            if (!prefab) throw new InvalidOperationException("Missing preview asset " + resource);
            var go = Instantiate(prefab, world.transform); go.transform.position = position;
            var p = PlayableProfile.Default;
            float scale = resource switch {
                "factory" => (float)(p.FactoryModelHeightMeters*p.FactoryModelScale/2.06),
                "headquarters" => (float)(p.HeadquartersModelHeightMeters*p.HeadquartersModelScale/4.2),
                "tank" => (float)(p.TankModelRadius*p.TankModelScale/1.585), _ => 1
            };
            go.transform.localScale = Vector3.one*scale;
            var finish = ActorMaterialProfile.Load();
            foreach(var renderer in go.GetComponentsInChildren<Renderer>(true))
                for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)finish.Apply(renderer,slot,new Color(.08f,.48f,.68f));
            return go;
        }
        public void Initialize()
        {
            Cleanup(); Profile = Profile ?? SentryTurretPreviewProfile.Load(); Profile.Validate();
            Clock = 0; nextShot = 0; accumulator = 0; burstRemaining = 0; sequence = 0; Shots = 0; TurningShots = 0; VerticalShots = 0; ShotLog.Clear();
            world = new GameObject("Stationary turret inspection world"); world.transform.SetParent(transform, false);
            Rig = Asset("sentry-turret", Vector3.zero).GetComponent<SentryTurretRig>(); Rig.Bind(); Rig.ResetAim();
            Asset("factory", new Vector3(-3.2f, 0, .7f));
            Asset("headquarters", new Vector3(3.2f, 0, 1.3f));
            Asset("tank", new Vector3(-2.8f, 0, -1.6f));
            Primitive("Technical base ground", PrimitiveType.Cube, new Vector3(0, -.13f, 0), new Vector3(17, .22f, 16), Mat(new Color(.26f,.255f,.22f)));
            var concrete = Mat(new Color(.44f,.44f,.40f));
            Primitive("Factory apron", PrimitiveType.Cube, new Vector3(-3.2f,-.005f,.7f), new Vector3(3.5f,.04f,3.3f), concrete);
            Primitive("Headquarters apron", PrimitiveType.Cube, new Vector3(3.2f,-.005f,1.3f), new Vector3(3.4f,.04f,3.4f), concrete);
            var paint = Mat(new Color(.70f,.62f,.38f));
            for (int i = -3; i <= 3; i++)
                Primitive("Neutral access marking", PrimitiveType.Cube, new Vector3(i*.42f,-.012f,-2.15f), new Vector3(.19f,.015f,.05f), paint);
            target = Primitive("Moving inspection target", PrimitiveType.Sphere, new Vector3(0,.8f,5), Vector3.one*.44f, Mat(new Color(.85f,.32f,.10f)));
            traceMaterial = Mat(new Color(1,.76f,.20f), true);
            flashes = new GameObject[2]; flashAges = new float[2];
            var sockets = new[] { Rig.Left, Rig.Right };
            for (int i=0;i<2;i++)
            {
                flashes[i] = Primitive("Muzzle flash", PrimitiveType.Sphere, sockets[i].position, new Vector3(.09f,.09f,.18f), traceMaterial, sockets[i]);
                flashes[i].transform.localPosition = Vector3.zero; flashes[i].SetActive(false);
            }
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.50f,.53f,.57f);
            var sun = new GameObject("Inspection sun").AddComponent<Light>(); sun.transform.SetParent(world.transform, false);
            sun.type = LightType.Directional; sun.intensity = 1.6f; sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(48,-32,0);
            PreviewCamera = new GameObject("Turret inspection camera").AddComponent<Camera>(); PreviewCamera.transform.SetParent(world.transform,false);
            PreviewCamera.transform.position = new Vector3(8,9,-13); PreviewCamera.transform.LookAt(new Vector3(0,1,0));
            PreviewCamera.orthographic = true; PreviewCamera.orthographicSize = Profile.cameraSize;
            PreviewCamera.clearFlags = CameraClearFlags.SolidColor; PreviewCamera.backgroundColor = new Color(.08f,.10f,.12f);
            PreviewCamera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            ManualTarget = new Vector3(0, .8f, 5);
            // No AudioListener or AudioSource is created by this inspection adapter.
        }
        public Vector3 ChoreographedTarget(double time)
        {
            float t = (float)(time % Cycle), r = Profile.targetDistance;
            if (t < 3) { Phase = "Наземная цель"; return new Vector3(0,.65f,r); }
            if (t < 7)
            {
                Phase = "Стрельба во время вращения";
                float a = (t-3)*Profile.targetDegreesPerSecond*Mathf.Deg2Rad + Mathf.PI*.6f;
                return new Vector3(Mathf.Sin(a)*r, Rig.Pitch.position.y+Profile.targetHeight, Mathf.Cos(a)*r);
            }
            if (t < 10) { Phase = "Стрельба вертикально вверх"; return Rig.Pitch.position + Vector3.up * Profile.targetHeight; }
            Phase = "Быстрый разворот к земле"; return new Vector3(-r,.65f,0);
        }
        public void Step(double seconds)
        {
            if (!world) throw new InvalidOperationException("Initialize turret preview first");
            if (!double.IsFinite(seconds) || seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            double remaining = seconds;
            while (remaining > 1e-10)
            {
                double step = Math.Min(remaining,FixedStep); StepTick(step); remaining -= step;
            }
        }
        void StepTick(double dt)
        {
            var point = Automatic ? ChoreographedTarget(Clock) : ManualTarget;
            target.transform.position = point; Rig.AimAt(point,(float)dt,Profile);
            for (int i=0;i<2;i++) { flashAges[i]-=(float)dt; flashes[i].SetActive(flashAges[i]>0); }
            if (Firing)
            {
                while (nextShot <= Clock + dt + 1e-10)
                {
                    if (burstRemaining==0) burstRemaining=BurstSize;
                    Emit(nextShot); burstRemaining--;
                    double interval=PlayableProfile.Default.ExplorerBurstShotIntervalMs/1000d;
                    nextShot += interval + (burstRemaining==0 ? PlayableProfile.Default.ExplorerBurstPauseMs/1000d : 0);
                }
            }
            else nextShot=Clock+dt;
            foreach (var trace in traces.ToArray())
            {
                trace.age+=(float)dt;
                float distance=trace.age*Profile.tracerSpeed;
                trace.line.SetPosition(0,trace.from+trace.direction*Mathf.Max(0,distance-Profile.tracerLength));
                trace.line.SetPosition(1,trace.from+trace.direction*distance);
                if (distance>PlayableProfile.Default.ExplorerRange) { Release(trace.line.gameObject); traces.Remove(trace); }
            }
            Clock+=dt;
        }
        void Emit(double time)
        {
            int side=sequence++%2; Transform socket=side==0?Rig.Left:Rig.Right;
            LastSpread=Rig.Spread((float)PlayableProfile.Default.ExplorerAssaultSpreadDeg,Profile);
            // Deterministic cone samples, independent of Unity's global RNG; directions follow the current barrel, never snap to target.
            float radius=Mathf.Sqrt((sequence*.61803398875f)%1)*Mathf.Tan(LastSpread*Mathf.Deg2Rad);
            float angle=sequence*2.39996323f;
            Vector3 forward=Rig.Forward, right=Vector3.Cross(forward,Mathf.Abs(forward.y)>.99f?Vector3.forward:Vector3.up).normalized;
            Vector3 up=Vector3.Cross(right,forward);
            Vector3 direction=(forward+right*(Mathf.Cos(angle)*radius)+up*(Mathf.Sin(angle)*radius)).normalized;
            var go=new GameObject("Inspection tracer");go.transform.SetParent(world.transform,false);
            var line=go.AddComponent<LineRenderer>();line.sharedMaterial=traceMaterial;line.useWorldSpace=true;line.positionCount=2;
            line.startWidth=line.endWidth=Profile.tracerWidth;line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;
            line.SetPosition(0,socket.position);line.SetPosition(1,socket.position+direction*.01f);
            traces.Add(new Trace{line=line,from=socket.position,direction=direction});
            flashAges[side]=Profile.flashSeconds;flashes[side].SetActive(true);
            Shots++;if(Rig.Turning)TurningShots++;if(Rig.ElevationDegrees>89.9f)VerticalShots++;
            ShotLog.Add(new Shot{time=time,spread=LastSpread,turning=Rig.Turning,origin=socket.position,direction=direction});
        }
        void Update()
        {
            if (!world) return; accumulator+=Time.deltaTime;
            while (accumulator>=FixedStep) { Step(FixedStep); accumulator-=FixedStep; }
        }
        void OnGUI()
        {
            if(!ShowControls||!world)return;
            GUILayout.BeginArea(new Rect(16,16,335,Screen.height-32),GUI.skin.box);
            GUILayout.Label("Пулемётная турель • модель 3");GUILayout.Label(Phase);
            GUILayout.Label($"HP ориентир: {ReferenceHealth}; очередь: {BurstSize}");
            GUILayout.Label($"Азимут {Rig.YawDegrees:0}° • подъём {Rig.ElevationDegrees:0}°");
            GUILayout.Label($"Разброс {LastSpread:0.0}° • выстрелов {Shots}");
            Firing=GUILayout.Toggle(Firing,"Стрельба");
            Automatic=GUILayout.Toggle(Automatic,"Автоматическая демонстрация");
            if(GUILayout.Button("Наземная цель")){Automatic=false;ManualTarget=new Vector3(0,.65f,Profile.targetDistance);}
            if(GUILayout.Button("Воздушная цель")){Automatic=false;ManualTarget=new Vector3(-Profile.targetDistance,Profile.targetHeight,0);}
            if(GUILayout.Button("Цель в зените")){Automatic=false;ManualTarget=Rig.Pitch.position+Vector3.up*Profile.targetHeight;}
            if(GUILayout.Button("Начать заново"))Initialize();
            tuning=GUILayout.Toggle(tuning,"Лаборатория превью");
            if(tuning)
            {
                scroll=GUILayout.BeginScrollView(scroll);
                foreach(var p in Profile.Parameters())
                {
                    GUILayout.Label(new GUIContent($"{p.Label}: {p.Read():0.##} {p.Unit}",p.Description));
                    float v=GUILayout.HorizontalSlider(p.Read(),p.Minimum,p.Maximum);
                    p.Write(Mathf.Clamp(Mathf.Round(v/p.Step)*p.Step,p.Minimum,p.Maximum));
                }
                GUILayout.EndScrollView();
            }
            GUILayout.Label("Проверочная сцена; не обычный матч.");GUILayout.EndArea();
        }
    }
}
