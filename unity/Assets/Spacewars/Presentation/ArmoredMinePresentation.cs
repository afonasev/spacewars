using System;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Geometry-only animation. Snapshot time is the sole clock in ordinary matches.
    public sealed class ArmoredMinePresentation : MonoBehaviour
    {
        Transform rocker,slider,tip;
        Transform[] bases,tips,barrels,rods;
        Quaternion bindRotation;
        Vector3 sliderBind;
        float tipBindHeight;
        ParticleSystem dust,sparks;
        Light shaftLight;
        struct HeatSurface {public Renderer Renderer;public int Slot;}
        HeatSurface[] heatSurfaces;
        MaterialPropertyBlock heatBlock;
        float emissionGain,lastGlow=-1,lastHeat=-1,lastHue=-1,lastSparkAge=-1;
        int lastSparkId=-1;
        ParticleSystemRenderer sparkRenderer;
        Material dustMaterial;
        long previousTick=-1;
        bool initialized;
        public ArmoredMineProfile Profile {get;private set;}
        public float Phase {get;private set;}
        public int DustCount=>dust?dust.particleCount:0;
        public int SparkCount=>sparks?sparks.particleCount:0;
        public float ImpactGlow {get;private set;}=1;
        Transform Node(string name)=>GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);
        public void Initialize()
        {
            if(initialized)return;
            Profile=ArmoredMineProfile.Load();rocker=Node("rockerPivot");slider=Node("drillSlider");tip=Node("rodTip");
            bindRotation=rocker.localRotation;sliderBind=slider.localPosition;tipBindHeight=transform.InverseTransformPoint(tip.position).y;
            bases=Enumerable.Range(0,2).Select(i=>Node("hydraulicBase"+i)).ToArray();tips=Enumerable.Range(0,2).Select(i=>Node("hydraulicTip"+i)).ToArray();
            barrels=Enumerable.Range(0,2).Select(i=>Node("hydraulicBarrel"+i)).ToArray();rods=Enumerable.Range(0,2).Select(i=>Node("hydraulicRod"+i)).ToArray();
            var go=new GameObject("Bottom stroke dust");go.transform.SetParent(Node("dustSocket"),false);go.layer=gameObject.layer;go.transform.localRotation=Quaternion.Euler(-90,0,0);
            dust=go.AddComponent<ParticleSystem>();dust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);dust.useAutoRandomSeed=false;dust.randomSeed=271;
            var main=dust.main;main.playOnAwake=false;main.loop=true;main.startLifetime=Profile.dustLifetime;main.startSpeed=Profile.dustRise;main.startSize=Profile.dustSize;main.startColor=new Color(.45f,.41f,.35f,Profile.dustOpacity);main.maxParticles=24;main.simulationSpace=ParticleSystemSimulationSpace.Local;
            var shape=dust.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=30;shape.radius=.12f;
            var emission=dust.emission;emission.rateOverTime=0;
            var color=dust.colorOverLifetime;color.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.15f),new GradientAlphaKey(0,1)});color.color=gradient;
            dustMaterial=new Material(Resources.Load<Shader>("FactorySmoke"));var renderer=dust.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=dustMaterial;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            var sparkObject=new GameObject("Bottom stroke sparks");sparkObject.transform.SetParent(Node("modelRoot"),false);sparkObject.layer=gameObject.layer;
            // Authored bore-collar top is y=.35 and its centre is z=.22; do not move this to the mechanism.
            sparkObject.transform.localPosition=new Vector3(0,.35f-Profile.sparkOriginDepth,.22f);sparkObject.transform.localRotation=Quaternion.Euler(-90,0,0);
            sparks=sparkObject.AddComponent<ParticleSystem>();sparks.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);sparks.useAutoRandomSeed=false;
            var sparkMain=sparks.main;sparkMain.playOnAwake=false;sparkMain.loop=false;sparkMain.duration=Profile.sparkLifetime;
            sparkMain.startLifetime=Profile.sparkLifetime;sparkMain.startSpeed=Profile.sparkSpeed;sparkMain.startSize=Profile.sparkSize;
            sparkMain.startColor=new Color(1,.8f,.28f);sparkMain.gravityModifier=Profile.sparkGravity;sparkMain.maxParticles=24;sparkMain.simulationSpace=ParticleSystemSimulationSpace.Local;
            var sparkShape=sparks.shape;sparkShape.shapeType=ParticleSystemShapeType.Cone;sparkShape.angle=Profile.sparkSpread;sparkShape.radius=.075f;
            var sparkEmission=sparks.emission;sparkEmission.rateOverTime=0;sparkEmission.SetBursts(new[]{new ParticleSystem.Burst(0,(short)Mathf.RoundToInt(Profile.sparkCount))});
            var sparkFade=sparks.colorOverLifetime;sparkFade.enabled=true;var sparkGradient=new Gradient();sparkGradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(1,.65f,.15f),1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,.45f),new GradientAlphaKey(0,1)});sparkFade.color=sparkGradient;
            // Share the existing unlit particle material; one bounded burst adds no texture/material/light allocation.
            sparkRenderer=sparks.GetComponent<ParticleSystemRenderer>();sparkRenderer.sharedMaterial=dustMaterial;sparkRenderer.renderMode=ParticleSystemRenderMode.Stretch;sparkRenderer.lengthScale=2;sparkRenderer.velocityScale=.05f;
            sparkRenderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;sparkRenderer.receiveShadows=false;sparkRenderer.enabled=false;
            var glowObject=new GameObject("Recessed red shaft light");glowObject.transform.SetParent(Node("modelRoot"),false);glowObject.layer=gameObject.layer;
            glowObject.transform.localPosition=new Vector3(0,.17f,.22f);
            var glow=shaftLight=glowObject.AddComponent<Light>();glow.type=LightType.Point;glow.color=new Color(1,.025f,.012f);
            glow.intensity=Profile.wellLightIntensity;glow.range=Profile.wellLightRange*transform.lossyScale.x;
            glow.shadows=LightShadows.None;glow.cullingMask=1<<gameObject.layer;
            // Recessed heat is deliberately red for every owner, unlike the exterior lamps.
            heatBlock=new MaterialPropertyBlock();emissionGain=ActorMaterialProfile.Load().emissionGain;
            heatSurfaces=GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials.Select((m,i)=>new{Renderer=r,Material=m,Slot=i}))
                .Where(v=>v.Material.name.EndsWith("well-hot-emissive",StringComparison.Ordinal)).Select(v=>new HeatSurface{Renderer=v.Renderer,Slot=v.Slot}).ToArray();
            initialized=true;Sample(0,0,false,0);
        }
        public void Drive(PlayableBuildingSnapshot building,long tick,int id,ActorMaterialProfile finish=null)
        {
            Initialize();
            if(finish!=null&&emissionGain!=finish.emissionGain){emissionGain=finish.emissionGain;lastGlow=-1;}
            if(previousTick>tick){dust.Clear();ClearImpact();}
            float delta=previousTick<0?0:Mathf.Max(0,(tick-previousTick)/30f);previousTick=tick;
            bool working=building.Progress>=1&&building.Phase==ConstructionPhase.Ready&&building.PrivateState?.Lifecycle?.Selling!=true;
            if(!working){dust.Clear();ClearImpact();return;}
            Sample(tick/30f,id,true,delta);
        }
        public void Sample(float seconds,int id,bool working,float delta)
        {
            Initialize();
            // Stable per-entity decorrelation; no random or simulation state is modified.
            Phase=Mathf.Repeat(seconds/Profile.cycleSeconds+((uint)id%997)*.61803398875f,1);
            float angle=working?Mathf.Sin(Phase*Mathf.PI*2)*Profile.rockerDegrees:0;
            rocker.localRotation=bindRotation*Quaternion.AngleAxis(angle,Vector3.right);
            var p=sliderBind;p.y+=transform.InverseTransformPoint(tip.position).y-tipBindHeight;slider.localPosition=p;
            for(int i=0;i<2;i++)
            {
                var a=bases[i].position;var b=tips[i].position;
                Segment(barrels[i],a,Vector3.Lerp(a,b,.6f));Segment(rods[i],Vector3.Lerp(a,b,.45f),b);
            }
            UpdateImpact(id,working);
            var emission=dust.emission;emission.rateOverTime=working&&Phase>.20f&&Phase<.30f?Profile.dustRate:0;
            if(delta>0){if(dust.isStopped)dust.Play();dust.Simulate(delta,true,false,true);}
        }
        void UpdateImpact(int id,bool working)
        {
            // Positive quarter-cycle is the exact lowest point of the authored extraction stroke.
            float age=Mathf.Repeat(Phase-.25f,1)*Profile.cycleSeconds;
            float decay=working?1-Mathf.Clamp01(age/Profile.impactGlowDuration):0;
            float heat=Profile.impactGlowBoost>0?decay*decay:0;
            SetGlow(1+Profile.impactGlowBoost*heat,heat);
            bool active=working&&Profile.sparkCount>0&&age<Profile.sparkLifetime;
            sparkRenderer.enabled=active;
            if(!active){if(lastSparkAge>=0)sparks.Clear();lastSparkAge=-1;return;}
            if(age==lastSparkAge&&id==lastSparkId)return;
            if(id!=lastSparkId){sparks.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);sparks.randomSeed=(271u+(uint)id*997u)|1u;}
            var emission=sparks.emission;emission.SetBurst(0,new ParticleSystem.Burst(0,(short)Mathf.RoundToInt(Profile.sparkCount)));
            // Absolute match-age sampling is also correct for first observation, seek and paused ticks.
            // A tiny positive interval lets Unity instantiate the time-zero burst at exact contact.
            sparks.Simulate(Mathf.Max(age,.0001f),true,true,false);lastSparkAge=age;lastSparkId=id;
        }
        void SetGlow(float multiplier,float heat=0)
        {
            ImpactGlow=multiplier;if(lastGlow==multiplier&&lastHeat==heat&&lastHue==Profile.impactGlowHue)return;
            lastGlow=multiplier;lastHeat=heat;lastHue=Profile.impactGlowHue;
            float warmth=Profile.impactGlowHue>0?heat:0;
            var yellow=Color.HSVToRGB(Profile.impactGlowHue,1,1);
            var emission=Color.Lerp(new Color(1,.015f,.008f),yellow,warmth);
            shaftLight.color=Color.Lerp(new Color(1,.025f,.012f),yellow,warmth);
            shaftLight.intensity=Profile.wellLightIntensity*multiplier;
            foreach(var surface in heatSurfaces)
            {
                surface.Renderer.GetPropertyBlock(heatBlock,surface.Slot);
                heatBlock.SetColor("_EmissionColor",emission*Profile.wellEmission*emissionGain*multiplier);
                surface.Renderer.SetPropertyBlock(heatBlock,surface.Slot);
            }
        }
        void ClearImpact(){sparks.Clear();sparkRenderer.enabled=false;lastSparkAge=-1;SetGlow(1);}
        static void Segment(Transform mesh,Vector3 a,Vector3 b)
        {
            var parent=mesh.parent;var start=parent.InverseTransformPoint(a);var end=parent.InverseTransformPoint(b);var direction=end-start;
            mesh.localPosition=(start+end)*.5f;mesh.localRotation=Quaternion.FromToRotation(Vector3.up,direction);
            mesh.localScale=new Vector3(1,direction.magnitude,1);
        }
        void OnDestroy(){if(dustMaterial){if(Application.isPlaying)Destroy(dustMaterial);else DestroyImmediate(dustMaterial);}}
    }
}
