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
        ParticleSystem dust;
        Material dustMaterial;
        long previousTick=-1;
        bool initialized;
        public ArmoredMineProfile Profile {get;private set;}
        public float Phase {get;private set;}
        public int DustCount=>dust?dust.particleCount:0;
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
            var glowObject=new GameObject("Recessed red shaft light");glowObject.transform.SetParent(Node("modelRoot"),false);glowObject.layer=gameObject.layer;
            glowObject.transform.localPosition=new Vector3(0,.17f,.22f);
            var glow=glowObject.AddComponent<Light>();glow.type=LightType.Point;glow.color=new Color(1,.025f,.012f);
            glow.intensity=Profile.wellLightIntensity;glow.range=Profile.wellLightRange*transform.lossyScale.x;
            glow.shadows=LightShadows.None;glow.cullingMask=1<<gameObject.layer;
            // Recessed heat is deliberately red for every owner, unlike the exterior lamps.
            var block=new MaterialPropertyBlock();
            foreach(var heat in GetComponentsInChildren<Renderer>(true))
                for(int slot=0;slot<heat.sharedMaterials.Length;slot++)
                    if(heat.sharedMaterials[slot].name.EndsWith("well-hot-emissive",StringComparison.Ordinal))
                    {heat.GetPropertyBlock(block,slot);block.SetColor("_EmissionColor",new Color(1,.015f,.008f)*Profile.wellEmission*ActorMaterialProfile.Load().emissionGain);heat.SetPropertyBlock(block,slot);}
            initialized=true;Sample(0,0,false,0);
        }
        public void Drive(PlayableBuildingSnapshot building,long tick,int id)
        {
            Initialize();
            if(previousTick>tick)dust.Clear();
            float delta=previousTick<0?0:Mathf.Max(0,(tick-previousTick)/30f);previousTick=tick;
            bool working=building.Progress>=1&&building.Phase==ConstructionPhase.Ready&&building.PrivateState?.Lifecycle?.Selling!=true;
            if(!working){dust.Clear();return;}
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
            var emission=dust.emission;emission.rateOverTime=working&&Phase>.20f&&Phase<.30f?Profile.dustRate:0;
            if(delta>0){if(dust.isStopped)dust.Play();dust.Simulate(delta,true,false,true);}
        }
        static void Segment(Transform mesh,Vector3 a,Vector3 b)
        {
            var parent=mesh.parent;var start=parent.InverseTransformPoint(a);var end=parent.InverseTransformPoint(b);var direction=end-start;
            mesh.localPosition=(start+end)*.5f;mesh.localRotation=Quaternion.FromToRotation(Vector3.up,direction);
            mesh.localScale=new Vector3(1,direction.magnitude,1);
        }
        void OnDestroy(){if(dustMaterial){if(Application.isPlaying)Destroy(dustMaterial);else DestroyImmediate(dustMaterial);}}
    }
}
