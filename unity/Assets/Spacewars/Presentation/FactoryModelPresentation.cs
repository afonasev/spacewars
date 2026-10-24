using System;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Presentation only. Observed snapshots drive the ordinary model; preview remains available.
    public sealed class FactoryModelPresentation : MonoBehaviour
    {
        public enum Phase { Idle, Working, Rollout }
        public Phase phase;
        [Range(0, 1)] public float doorOpen;
        public bool animatePreview;
        public bool ExternallyDriven { get; set; }
        long lastTick=-1;
        FactoryModelProfile profile;
        Transform[] doors, fans;
        Transform rollerStore;
        Vector3[] closed;
        Quaternion[] fanBase;
        Renderer[] glow;
        ParticleSystem[] smoke;
        Material smokeMaterial;
        MaterialPropertyBlock block;
        float clock, lightSeconds;
        Color ownerColor;
        bool ready;
        public FactoryModelProfile Profile => profile;
        public int SmokeParticles => smoke == null ? 0 : smoke.Sum(p => p.particleCount);

        public void Initialize()
        {
            if (ready) return;
            profile = FactoryModelProfile.Load();
            block = new MaterialPropertyBlock();
            var nodes = GetComponentsInChildren<Transform>(true);
            doors = Enumerable.Range(0, 6).Select(i => nodes.Single(t => t.name == "doorSection" + i)).ToArray();
            closed = doors.Select(t => t.localPosition).ToArray();
            rollerStore = nodes.Single(t => t.name == "gateRollerStore");
            fans = Enumerable.Range(0, 2).Select(i => nodes.Single(t => t.name == "fan" + i)).ToArray();
            fanBase = fans.Select(t => t.localRotation).ToArray();
            glow = GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith("workGlow", StringComparison.Ordinal)).ToArray();
            if (glow.Length != 3) throw new InvalidOperationException("Three concealed work lights required");
            var shader = Resources.Load<Shader>("FactorySmoke");
            if (!shader) throw new InvalidOperationException("Missing factory smoke shader");
            smokeMaterial = new Material(shader) { name = "factory-preview-smoke" };
            smoke = new ParticleSystem[2];
            for (int i = 0; i < 2; i++)
            {
                var socket = nodes.Single(t => t.name == "smoke" + i);
                var go = new GameObject("Working smoke"); go.transform.SetParent(socket, false);go.layer=gameObject.layer;
                go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
                var p = go.AddComponent<ParticleSystem>(); p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                p.useAutoRandomSeed = false; p.randomSeed = (uint)(710 + i);
                var main = p.main; main.playOnAwake = false; main.loop = true;
                main.duration = 3; main.startLifetime = profile.smokeLifetime;
                main.startSpeed = profile.smokeRise; main.startSize = new ParticleSystem.MinMaxCurve(profile.smokeSize * .3f, profile.smokeSize * .6f);
                main.startColor = new Color(.78f, .80f, .82f, profile.smokeOpacity);
                main.maxParticles = 48; main.simulationSpace = ParticleSystemSimulationSpace.Local;
                var emission = p.emission; emission.rateOverTime = 0;
                var shape = p.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 12; shape.radius = .06f;
                var size = p.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .5f, 1, 2));
                var color = p.colorOverLifetime; color.enabled = true;
                var gradient = new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.2f),new GradientAlphaKey(0,1)}); color.color = gradient;
                var renderer = p.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = smokeMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
                smoke[i] = p;
            }
            ownerColor = GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials)
                .First(m=>m.name.EndsWith("team-primary",StringComparison.Ordinal)).GetColor("_BaseColor");
            ready = true;
            RefreshLights();
        }
        void Start() { Initialize(); }
        void Update()
        {
            if (!Application.isPlaying || ExternallyDriven) return;
            clock += Time.deltaTime;
            if (animatePreview)
            {
                float cycle = clock % 12;
                phase = cycle < 2 ? Phase.Idle : cycle < 8 ? Phase.Working : Phase.Rollout;
                doorOpen = cycle < 8 ? 0 : cycle < 9 ? cycle - 8 : cycle < 11 ? 1 : 12 - cycle;
            }
            Apply(phase, doorOpen, clock);
        }
        public void Drive(PlayableBuildingSnapshot building,long tick,PlayableProfile rules)
        {
            ExternallyDriven=true;Initialize();
            if(tick<lastTick)foreach(var p in smoke)p.Clear();
            float delta=lastTick<0?0:Mathf.Max(0,(tick-lastTick)/30f);lastTick=tick;
            bool available=building.Progress>=1&&building.Phase==ConstructionPhase.Ready&&building.PrivateState?.Lifecycle?.Selling!=true;
            int stage=available?building.LaunchPhase:0;
            float progress=Mathf.Clamp01((float)(building.LaunchTime/rules.FactoryGateSeconds));
            float opening=stage==FactoryReleaseRules.Opening?progress:stage==FactoryReleaseRules.Driving?1:stage==FactoryReleaseRules.Closing?1-progress:0;
            bool working=available&&stage==0&&building.PrivateState?.Orders.Any(o=>o.Active&&o.Remaining>0)==true;
            StepPreview(stage!=0?Phase.Rollout:working?Phase.Working:Phase.Idle,opening,tick/30f,delta);
        }
        public void Apply(Phase state, float opening, float seconds)
        {
            Initialize(); phase = state; doorOpen = Mathf.Clamp01(opening);
            // Only a rollout may expose the interior; roof never moves.
            float travel = state == Phase.Rollout ? doorOpen * profile.doorTravel : 0;
            for (int i = 0; i < doors.Length; i++)
            {
                var p = closed[i] + Vector3.up * travel;
                doors[i].localPosition = p;
                // Authored lintel lower edge: sections disappear into its roller housing.
                doors[i].gameObject.SetActive(p.y < rollerStore.localPosition.y);
            }
            for (int i = 0; i < fans.Length; i++) fans[i].localRotation = fanBase[i] * Quaternion.AngleAxis(seconds * profile.fanSpeed * (state == Phase.Working ? 1 : .15f), Vector3.forward);
            lightSeconds = seconds;
            RefreshLights();
            foreach (var p in smoke)
            {
                var emission = p.emission; emission.rateOverTime = state == Phase.Working ? profile.smokeRate : 0;
                if (Application.isPlaying && !ExternallyDriven && !p.isPlaying) p.Play();
            }
        }
        public void SetOwnerColor(Color color)
        {
            Initialize(); ownerColor = color; RefreshLights();
        }
        public void RefreshLights()
        {
            if (!ready) return;
            for (int i = 0; i < glow.Length; i++)
            {
                float wave = Mathf.Sin(lightSeconds * profile.flashRate * 6.283185f + i * 2.1f) * Mathf.Sin(lightSeconds * profile.flashRate * 4.7f + i);
                float flash = phase == Phase.Working ? Mathf.Max(0, wave) : 0;
                glow[i].GetPropertyBlock(block, 0);
                block.SetColor("_EmissionColor", ownerColor * flash * profile.flashIntensity);
                block.SetColor("_BaseColor", Color.Lerp(new Color(.025f,.025f,.025f), ownerColor,flash));
                glow[i].SetPropertyBlock(block, 0);
            }
        }
        // Deterministic sampling for saved native Editor frames, never gameplay evidence.
        public void Sample(Phase state, float opening, float seconds)
        {
            Apply(state, opening, seconds);
            foreach (var p in smoke) p.Simulate(seconds, true, true, true);
        }
        public void StepPreview(Phase state, float opening, float seconds, float delta)
        {
            Apply(state, opening, seconds);
            foreach (var p in smoke)
            {
                // Simulate pauses after stepping, but a stopped emitter must first be started.
                if (ExternallyDriven && p.isStopped) p.Play();
                p.Simulate(delta, true, false, true);
            }
        }
        void OnDestroy()
        {
            if (smokeMaterial) { if (Application.isPlaying) Destroy(smokeMaterial); else DestroyImmediate(smokeMaterial); }
        }
    }
}
