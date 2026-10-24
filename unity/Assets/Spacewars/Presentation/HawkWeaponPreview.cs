using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Spacewars.Presentation
{
    // Standalone inspection adapter. The pure weapon/flight classes own timing and trajectories.
    public sealed class HawkWeaponPreview : MonoBehaviour
    {
        public bool TargetMoving, HawkMoving, KillAfterFirst;
        public HawkWeaponPreviewProfile Profile { get; private set; }
        public HawkWeapon Weapon { get; private set; }
        public Camera PreviewCamera { get; private set; }
        public double Clock { get; private set; }
        public float Health { get; private set; }
        public int Hits { get; private set; }
        public int LiveRockets => rockets.Count;
        public readonly List<LaunchRecord> Launches = new List<LaunchRecord>();
        [Serializable] public sealed class LaunchRecord { public double time; public int target, side; }
        sealed class RocketView { public HawkRocketFlight flight; public GameObject view; public double start; }
        sealed class Impact { public GameObject view; public float age; }
        readonly List<RocketView> rockets = new List<RocketView>();
        readonly List<Impact> impacts = new List<Impact>();
        readonly List<Material> ownedMaterials = new List<Material>();
        GameObject world, craft, target;
        Transform left, right, rotor, tailRotor;
        Material targetMaterial, trailMaterial;
        WebProjectileVisuals visuals;
        HawkWeaponPreviewProfile active;
        int targetId = 1;
        double accumulator;
        bool tuning, running = true;
        Vector2 scroll;
        const double TickSeconds = 1d / 60; // Inspection simulation frequency, not a weapon timing parameter.
        static BallisticPoint Point(Vector3 p) => new BallisticPoint(p.x, p.y, p.z);
        static Vector3 Vector(BallisticPoint p) => new Vector3((float)p.X, (float)p.Y, (float)p.Z);
        static void Release(UnityEngine.Object o) { if (!o) return; if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }
        void Start() { if (world == null) Initialize(); }
        void OnDestroy() { Cleanup(); }
        void Cleanup()
        {
            if (world) { world.SetActive(false); Release(world); }
            world = null; visuals?.Dispose(); visuals = null;
            foreach (var m in ownedMaterials) Release(m);
            ownedMaterials.Clear(); rockets.Clear(); impacts.Clear();
        }
        Material Material(Color color)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", .3f); ownedMaterials.Add(m); return m;
        }
        GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(world.transform, false);
            go.transform.position = position; go.transform.localScale = scale;
            Release(go.GetComponent<Collider>()); go.GetComponent<Renderer>().sharedMaterial = material; return go;
        }
        // Platform, marker and fixture colors/dimensions are authored scene geometry, not weapon tuning.
        public void Initialize(HawkWeaponPreviewProfile profile = null)
        {
            Cleanup(); Profile = profile ?? Profile ?? HawkWeaponPreviewProfile.Load(); Profile.Validate();
            active = JsonUtility.FromJson<HawkWeaponPreviewProfile>(JsonUtility.ToJson(Profile));
            Weapon = new HawkWeapon(PlayableProfile.Default.TankPreparationSeconds + active.preparationExtraSeconds,
                active.shotIntervalSeconds, active.reloadSeconds);
            Clock = 0; Health = active.targetHealth; Hits = 0; accumulator = 0; targetId = 1; Launches.Clear();
            world = new GameObject("Hawk weapon preview world"); world.transform.SetParent(transform, false);
            visuals = new WebProjectileVisuals();
            craft = Instantiate(Resources.Load<GameObject>("StyleA/hawk"), world.transform);
            craft.name = "Ястреб"; craft.transform.position = new Vector3(0, active.hoverHeight, -active.targetDistance / 2);
            var nodes = craft.GetComponentsInChildren<Transform>(true);
            left = nodes.Single(t => t.name == "muzzleLeft"); right = nodes.Single(t => t.name == "muzzleRight");
            rotor = nodes.Single(t => t.name == "mainRotor"); tailRotor = nodes.Single(t => t.name == "tailRotor");
            targetMaterial = Material(new Color(.65f, .15f, .08f));
            target = Primitive("Мишень", PrimitiveType.Sphere, new Vector3(0, active.targetHeight, active.targetDistance / 2), Vector3.one * (2 * active.targetRadius), targetMaterial);
            Primitive("Площадка", PrimitiveType.Cube, new Vector3(0, -.15f, 0), new Vector3(16, .25f, active.targetDistance + 10), Material(new Color(.18f, .22f, .24f)));
            var stripe = Material(new Color(.36f, .40f, .40f));
            for (int z = -(int)(active.targetDistance / 2 + 4); z <= active.targetDistance / 2 + 4; z += 2)
                Primitive("Разметка", PrimitiveType.Cube, new Vector3(0, -.018f, z), new Vector3(12, .012f, .025f), stripe);
            Primitive("Основание мишени", PrimitiveType.Cylinder, new Vector3(0, .08f, active.targetDistance / 2), new Vector3(2.8f, .1f, 2.8f), Material(new Color(.11f, .13f, .14f)));
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.5f, .54f, .58f);
            var light = new GameObject("Свет").AddComponent<Light>(); light.transform.SetParent(world.transform, false);
            light.type = LightType.Directional; light.intensity = 1.8f; light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            PreviewCamera = new GameObject("Камера оружия").AddComponent<Camera>(); PreviewCamera.transform.SetParent(world.transform, false);
            PreviewCamera.transform.position = new Vector3(15, 14, -16); PreviewCamera.transform.LookAt(new Vector3(0, 1, 0));
            PreviewCamera.orthographic = true; PreviewCamera.orthographicSize = active.cameraSize;
            PreviewCamera.clearFlags = CameraClearFlags.SolidColor; PreviewCamera.backgroundColor = new Color(.07f, .095f, .12f);
            PreviewCamera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            // No AudioSource/Listener or sounds are introduced, including interactive inspection.
            trailMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            trailMaterial.SetColor("_BaseColor", new Color(.65f, .69f, .72f, .65f)); ownedMaterials.Add(trailMaterial);
        }
        public void DestroyTarget() { Health = 0; if (targetMaterial) targetMaterial.SetColor("_BaseColor", new Color(.09f, .08f, .07f)); }
        public void RespawnTarget() { targetId++; Health = active.targetHealth; targetMaterial.SetColor("_BaseColor", new Color(.65f, .15f, .08f)); }
        void Update()
        {
            if (world == null || !running) return;
            // Keep all elapsed time; no dropped simulation ticks under low render FPS.
            accumulator += Time.deltaTime;
            while (accumulator >= TickSeconds) { Step(TickSeconds); accumulator -= TickSeconds; }
        }
        public void Step(double dt)
        {
            if (world == null) throw new InvalidOperationException("Initialize the preview before stepping");
            if (dt <= 0 || double.IsNaN(dt) || double.IsInfinity(dt)) throw new ArgumentOutOfRangeException(nameof(dt));
            if (dt > TickSeconds + 1e-12)
            {
                for (double remaining = dt; remaining > 1e-12; remaining -= Math.Min(remaining, TickSeconds)) Step(Math.Min(remaining, TickSeconds));
                return;
            }
            var before = target.transform.position;
            if (TargetMoving && Health > 0)
            {
                float x = Mathf.PingPong((float)(Clock + dt) * active.targetSpeed + active.targetTravel, 2 * active.targetTravel) - active.targetTravel;
                target.transform.position = new Vector3(x, active.targetHeight, active.targetDistance / 2);
            }
            if (HawkMoving) craft.transform.position += Vector3.right * (float)(Math.Cos(Clock) * dt);
            rotor.Rotate(Vector3.up, active.rotorDegreesPerSecond * (float)dt, Space.Self);
            tailRotor.Rotate(Vector3.right, active.rotorDegreesPerSecond * (float)dt, Space.Self);
            var launches = Weapon.Advance(dt, !HawkMoving, targetId, id => Health > 0 && id == targetId);
            foreach (var launch in launches)
            {
                var socket = launch.Side == 0 ? left : right;
                var start = Point(socket.position); var direction = Point(target.transform.position - socket.position);
                var flight = new HawkRocketFlight(launch.Target, start, direction, active.speed, active.turnDegreesPerSecond * Math.PI / 180, active.lifetimeSeconds);
                var view = visuals.Rocket(world.transform, active.collisionRadius, active.rocketLength, new Color(.35f, .42f, .45f));
                view.transform.position = socket.position; view.transform.rotation = Quaternion.FromToRotation(Vector3.right, Vector(flight.Direction));
                visuals.Exhaust(view, active.exhaustLength, active.collisionRadius, active.exhaustCore, active.exhaustOpacity);
                var trail = view.AddComponent<TrailRenderer>(); trail.sharedMaterial = trailMaterial;
                trail.time = active.trailSeconds; trail.startWidth = active.trailWidth; trail.endWidth = 0; trail.minVertexDistance = .04f;
                trail.startColor = new Color(.7f, .72f, .74f, .7f); trail.endColor = new Color(.4f, .45f, .5f, 0);
                rockets.Add(new RocketView { flight = flight, view = view, start = Clock + launch.Offset });
                Launches.Add(new LaunchRecord { time = Clock + launch.Offset, target = launch.Target, side = launch.Side });
                if (KillAfterFirst && launch.Side == 0) DestroyTarget();
            }
            foreach (var rocket in rockets.ToArray())
            {
                double duration = Math.Max(0, Clock + dt - Math.Max(Clock, rocket.start));
                var from = rocket.flight.Position;
                rocket.flight.Advance(duration, Health > 0 && rocket.flight.Target == targetId ? Point(target.transform.position) : (BallisticPoint?)null);
                var to = rocket.flight.Position;
                rocket.view.transform.position = Vector(to);
                rocket.view.transform.rotation = Quaternion.FromToRotation(Vector3.right, Vector(rocket.flight.Direction));
                // Analytic swept sphere contact includes target displacement within this fixed tick.
                var targetStart = Vector3.Lerp(before, target.transform.position, (float)((dt - duration) / dt));
                double fraction = 0;
                bool hit = Health > 0 && HawkRocketFlight.Contact(from, to, Point(targetStart), Point(target.transform.position), active.targetRadius + active.collisionRadius, out fraction);
                bool groundHit = to.Y <= active.collisionRadius;
                if (groundHit)
                {
                    double groundFraction = Math.Max(0, Math.Min(1, (from.Y - active.collisionRadius) / Math.Max(1e-12, from.Y - to.Y)));
                    if (!hit || groundFraction < fraction) { hit = false; fraction = groundFraction; }
                    else groundHit = false;
                }
                if (hit || groundHit)
                {
                    rocket.view.transform.position = Vector(HawkRocketFlight.Add(from, HawkRocketFlight.Scale(HawkRocketFlight.Sub(to, from), fraction)));
                    var flash = visuals.DirectImpact(world.transform, Hits + 1); flash.transform.position = rocket.view.transform.position;
                    impacts.Add(new Impact { view = flash });
                    if (hit) { Hits++; Health = Mathf.Max(0, Health - active.damage); if (Health == 0) DestroyTarget(); }
                }
                // The floor is a real obstruction for the preview flight, not a homing teleport.
                if (hit || rocket.flight.RemainingLife <= 0 || groundHit)
                { Release(rocket.view); rockets.Remove(rocket); }
            }
            foreach (var impact in impacts.ToArray())
            {
                impact.age += (float)dt;
                visuals.UpdateDirectImpact(impact.view, impact.age, active.impactRadius, active.impactRadius, active.impactSparkWidth, active.impactGlow, active.impactSmoke);
                if (impact.age > active.impactSeconds) { Release(impact.view); impacts.Remove(impact); }
            }
            Clock += dt;
        }
        void OnGUI()
        {
            if (world == null) return;
            GUILayout.BeginArea(new Rect(14, 14, 330, Screen.height - 28), GUI.skin.box);
            GUILayout.Label("ЯСТРЕБ · ПРОВЕРКА ОРУЖИЯ");
            GUILayout.Label($"Пуски: {Launches.Count}   Попадания: {Hits}   Мишень: {Health:0} HP");
            GUILayout.Label(Weapon.SecondPending ? "Вторая ракета…" : Weapon.ReloadRemaining > 0 ? $"Перезарядка: {Weapon.ReloadRemaining:0.0} с" : "Зависание / подготовка");
            if (GUILayout.Button(running ? "Пауза" : "Продолжить")) running = !running;
            if (GUILayout.Button("Начать заново")) Initialize();
            TargetMoving = GUILayout.Toggle(TargetMoving, "Движущаяся мишень");
            HawkMoving = GUILayout.Toggle(HawkMoving, "Движение Ястреба — огонь запрещён");
            KillAfterFirst = GUILayout.Toggle(KillAfterFirst, "Уничтожить мишень после первого пуска");
            if (GUILayout.Button("Уничтожить мишень")) DestroyTarget();
            if (GUILayout.Button("Новая мишень")) RespawnTarget();
            tuning = GUILayout.Toggle(tuning, "Лаборатория оружия");
            if (tuning)
            {
                GUILayout.Label("Изменения применяются при новом старте.");
                scroll = GUILayout.BeginScrollView(scroll);
                foreach (var p in Profile.Parameters())
                {
                    GUILayout.Label(new GUIContent($"{p.Label}: {p.Read():0.##} {p.Unit}", p.Description));
                    float value = GUILayout.HorizontalSlider(p.Read(), p.Minimum, p.Maximum);
                    p.Write(Mathf.Clamp(Mathf.Round(value / p.Step) * p.Step, p.Minimum, p.Maximum));
                }
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }
    }
}
