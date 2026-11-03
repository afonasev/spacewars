using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private sealed class AbilityEffect{internal PlayableSoundEvent Fact;internal GameObject Object;internal Mesh Mesh;}
        private readonly Dictionary<long,AbilityEffect> abilityEffects=new Dictionary<long,AbilityEffect>();
        private readonly Dictionary<long,long> abilityEffectsSeen=new Dictionary<long,long>();
        private long abilityEffectGeneration=long.MinValue,abilityEffectTick=-1;
        private Material abilityEffectMaterial;
        private PlayableProfile abilityFailureProfile;private float abilityFailureRadius;
        public AbilityFeedbackProfile AbilityFeedback{get;}=AbilityFeedbackProfile.Load();
        public int AbilityEffectCount=>abilityEffects.Count;
        public static bool IsAbilityEffect(PlayableSoundKind kind)=>kind==PlayableSoundKind.OrbitalSystemsDisabled||kind==PlayableSoundKind.OrbitalMineInstalled||kind==PlayableSoundKind.OrbitalControlHacked;
        private void ClearAbilityEffects()
        {
            foreach(var e in abilityEffects.Values){ReleaseWorldObject(e.Mesh);ReleaseWorldObject(e.Object);}abilityEffects.Clear();abilityEffectsSeen.Clear();
            ReleaseWorldObject(abilityEffectMaterial);abilityEffectMaterial=null;abilityEffectGeneration=long.MinValue;abilityEffectTick=-1;
        }
        public void RenderAbilityEffects(PlayableSnapshot view,Camera camera)
        {
            if(!ReferenceEquals(abilityFailureProfile,profile)){abilityFailureProfile=profile;abilityFailureRadius=(float)profile.CopyData().orbitalSystemFailureRadius;}
            bool baseline=abilityEffectGeneration!=view.Generation||view.Tick<abilityEffectTick;
            if(baseline){ClearAbilityEffects();abilityEffectGeneration=view.Generation;}
            // First binding/restore is a baseline, not a new successful application.
            if(baseline){foreach(var f in view.Sounds.Where(f=>IsAbilityEffect(f.Kind)))abilityEffectsSeen[f.Id]=f.Tick;abilityEffectTick=view.Tick;return;}
            foreach(var f in view.Sounds.Where(f=>IsAbilityEffect(f.Kind))){
                if(abilityEffectsSeen.ContainsKey(f.Id))continue;abilityEffectsSeen[f.Id]=f.Tick;
                if(view.Tick<f.Tick||(view.Tick-f.Tick)/30d>=AbilityFeedback.duration||!view.Vision.IsVisible(f.Position))continue;
                if(abilityEffects.Count>=(int)AbilityFeedback.maxEffects)continue;
                if(!abilityEffectMaterial){
                    var shader=Resources.Load<Shader>("AbilityPulse");var texture=Resources.Load<Texture2D>("OrbitalEffects/energy-pulse");
                    if(!shader||!texture)throw new InvalidOperationException("Missing 2D ability pulse texture or shader");
                    abilityEffectMaterial=new Material(shader){mainTexture=texture};abilityEffectMaterial.SetFloat("_EmissionGain",AbilityFeedback.emissionGain);
                }
                var go=new GameObject("Ability feedback "+f.Id);go.transform.SetParent(root,false);go.AddComponent<MeshRenderer>().sharedMaterial=abilityEffectMaterial;
                var mesh=new Mesh{name="Ability pulse "+f.Id};mesh.MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=mesh;Layer(go);
                abilityEffects.Add(f.Id,new AbilityEffect{Fact=f,Object=go,Mesh=mesh});
            }
            foreach(var pair in abilityEffects.ToArray()){
                var e=pair.Value;float phase=(float)((view.Tick-e.Fact.Tick)/30d/AbilityFeedback.duration);
                var entity=e.Fact.UnitId==0?null:view.Entities.FirstOrDefault(u=>u.Id==e.Fact.UnitId);
                var building=e.Fact.UnitId==0?null:view.Buildings.FirstOrDefault(b=>b.Id==e.Fact.UnitId);
                var point=entity!=null?entity.Position:building!=null?building.Position:e.Fact.Position;
                bool lost=e.Fact.Kind==PlayableSoundKind.OrbitalControlHacked&&entity==null&&building==null;
                if(phase>=1||lost||!view.Vision.IsVisible(point)){ReleaseWorldObject(e.Mesh);ReleaseWorldObject(e.Object);abilityEffects.Remove(pair.Key);continue;}
                e.Object.transform.position=entity!=null?EntityPoint(entity):Point(point);
                float radius=e.Fact.Kind==PlayableSoundKind.OrbitalSystemsDisabled?abilityFailureRadius:e.Fact.Kind==PlayableSoundKind.OrbitalMineInstalled?AbilityFeedback.mineRadius:
                    (float)(entity!=null?PlayableUnitRules.Radius(profile,entity.Kind):TerritoryRules.Radius(profile,building.Kind))*AbilityFeedback.hackRadiusScale;
                BuildAbilityPulse(e.Mesh,Mathf.Clamp01(phase),radius,e.Fact.Kind==PlayableSoundKind.OrbitalControlHacked,AbilityFeedback,(int)e.Fact.Id,camera.transform.right);
                // Ground rings conform to the actual surface; airborne captures keep their altitude.
                if(entity==null||entity.Kind!=PlayableEntityKind.Hawk){var v=e.Mesh.vertices;float y=Point(point).y;for(int i=0;i<v.Length;i++)v[i].y+=Point(new NavPoint(point.X+v[i].x,point.Z+v[i].z)).y-y;e.Mesh.vertices=v;e.Mesh.RecalculateBounds();}
            }
            foreach(var id in abilityEffectsSeen.Where(p=>view.Tick-p.Value>30).Select(p=>p.Key).ToArray())abilityEffectsSeen.Remove(id);
            abilityEffectTick=view.Tick;
        }
        public static void BuildAbilityPulse(Mesh mesh,float phase,float radius,bool hack,AbilityFeedbackProfile p,int seed,Vector3 cameraRight)
        {
            var vertices=new List<Vector3>();var colors=new List<Color>();var uv=new List<Vector2>();var indices=new List<int>();
            float envelope=Mathf.Max(0,Mathf.Sin(Mathf.PI*phase))*p.opacity;Color color=hack?OrbitalTheme.Cyan:OrbitalTheme.Amber;color.a=envelope;
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color tint,bool textured){int n=vertices.Count;vertices.AddRange(new[]{a,b,c,d});colors.AddRange(new[]{tint,tint,tint,tint});uv.AddRange(textured?new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)}:new[]{new Vector2(-1,0),new Vector2(-2,0),new Vector2(-2,1),new Vector2(-1,1)});indices.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
            // Half extents and phase interpolation are geometric invariants, not gameplay radii.
            float r=radius*Mathf.Lerp(p.startScale,1,phase);float y=p.groundLift;
            Quad(new Vector3(-r,y,-r),new Vector3(r,y,-r),new Vector3(r,y,r),new Vector3(-r,y,r),color,true);
            Color core=OrbitalTheme.Cyan;core.a=envelope*(1-phase);float inner=r/2;
            Quad(new Vector3(-inner,y*2,-inner),new Vector3(inner,y*2,-inner),new Vector3(inner,y*2,inner),new Vector3(-inner,y*2,inner),core,true);
            int count=hack?(int)p.sparkCount:(int)p.sparkCount/2;
            for(int i=0;i<count;i++){
                float angle=(i+(seed%17)/17f)*Mathf.PI*2/Mathf.Max(1,count);float offset=Mathf.Repeat(phase+i/(float)Mathf.Max(1,count),1);
                var at=new Vector3(Mathf.Cos(angle)*r/2,y+offset*p.sparkHeight,Mathf.Sin(angle)*r/2);var right=cameraRight*p.sparkWidth;
                var end=at+Vector3.up*p.sparkLength;Color spark=hack?OrbitalTheme.Cyan:Color.Lerp(OrbitalTheme.Amber,OrbitalTheme.Cyan,offset);spark.a=envelope*(1-offset);
                Quad(at-right,at+right,end+right,end-right,spark,false);
            }
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
        }
    }
}
