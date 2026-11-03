using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Input.Tests
{
    public sealed class AbilityFeedbackEffectsTests
    {
        [Test] public void EffectProfileAndMeshUseBoundedTunableSettings()
        {
            var p=AbilityFeedbackProfile.Load();Assert.AreEqual(12,p.Parameters().Count());foreach(var f in p.Parameters()){Assert.IsNotEmpty(f.Description);Assert.Greater(f.Step,0);Assert.That(f.Read(),Is.InRange(f.Minimum,f.Maximum));}
            var mesh=new Mesh();try{foreach(bool hack in new[]{false,true})foreach(float phase in new[]{0f,.25f,.5f,1f}){PlayableWorld.BuildAbilityPulse(mesh,phase,p.mineRadius,hack,p,17,Vector3.right);Assert.Greater(mesh.vertexCount,0);Assert.Less(mesh.vertexCount,128);foreach(var v in mesh.vertices){Assert.False(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z));Assert.LessOrEqual(Mathf.Abs(v.x),p.mineRadius+p.sparkWidth);Assert.LessOrEqual(Mathf.Abs(v.z),p.mineRadius+p.sparkWidth);}Assert.True(mesh.colors.All(c=>c.a>=0&&c.a<=p.opacity+.001f));}}finally{UnityEngine.Object.DestroyImmediate(mesh);}
        }
        [Test] public void AuthoredAssetsArePresentAndScanRemainsItsExistingEffect()
        {
            foreach(string name in new[]{"scan","mine","hack"}){var clip=Resources.Load<AudioClip>("Sfx/Hybrid/ability-"+name+"-1");Assert.NotNull(clip);Assert.AreEqual(44100,clip.frequency);Assert.AreEqual(2,clip.channels);Assert.That(clip.length,Is.InRange(.1f,3f));}
            Debug.Log("ABILITY_RESOURCE_DIAG texture="+Resources.Load("OrbitalEffects/energy-pulse")+" shader="+Resources.Load("AbilityPulse")+" names="+string.Join(",",Resources.LoadAll<UnityEngine.Object>("OrbitalEffects").Select(o=>o.name+":"+o.GetType().Name)));
            Assert.NotNull(Resources.Load<Texture2D>("OrbitalEffects/energy-pulse"),"generated energy texture must load as Texture2D");Assert.NotNull(Resources.Load<Shader>("AbilityPulse"),"ability pulse shader must load");
            Assert.False(PlayableWorld.IsAbilityEffect(PlayableSoundKind.OrbitalScanApplied));Assert.True(PlayableWorld.IsAbilityEffect(PlayableSoundKind.OrbitalMineInstalled));Assert.True(PlayableWorld.IsAbilityEffect(PlayableSoundKind.OrbitalControlHacked));
        }
    }
}
