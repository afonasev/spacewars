#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Spacewars.Presentation;
using Spacewars.Simulation;

namespace Spacewars.Tests.PlayMode
{
    public sealed partial class ActorMaterialRenderingTests
    {
        private static Dictionary<string,object> InventoryBlock(MaterialPropertyBlock block,Material material)
        {
            var keys=Enumerable.Range(0,material.shader.GetPropertyCount()).Select(material.shader.GetPropertyName)
                .Concat(new[]{"_BaseColor","_EmissionColor","_Metallic","_Smoothness","_IndustrialFinish"}).Distinct();
            var values=new Dictionary<string,object>();
            foreach(string key in keys)
            {
                if(!block.HasProperty(key))continue;
                int property=material.shader.FindPropertyIndex(key);
                var type=property<0?UnityEngine.Rendering.ShaderPropertyType.Vector:material.shader.GetPropertyType(property);
                if(type==UnityEngine.Rendering.ShaderPropertyType.Float||type==UnityEngine.Rendering.ShaderPropertyType.Range)
                {float value=block.GetFloat(key);values[key]=new{value,bits=BitConverter.SingleToInt32Bits(value).ToString("x8")};}
                else if(type==UnityEngine.Rendering.ShaderPropertyType.Texture){var texture=block.GetTexture(key);values[key]=texture?texture.GetInstanceID():0;}
                else values[key]=MaterialInputBits(block.GetVector(key));
            }
            return values;
        }

        [Test,Explicit("Cycle62 readonly actual2+2 bindings; no rendering/performance or scope changes")]
        public void ReportOriginalActorBindingInventory()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_MATERIAL_BATCH_OUTPUT");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            var baseline=PlayableProfile.Create(Newtonsoft.Json.JsonConvert.DeserializeObject<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new AiTestMap());
            var store=new NativeBalanceStore(baseline,Path.Combine(output,"isolated-native-balance.json"),false);store.Select(NativeBalanceStore.NewMatchProfileId,NativeBalanceStore.NewMatchProfileVersion);
            var profile=store.Active;Assert.AreEqual(2,profile.Revision);var priorDetail=ModelDetailProfile.Current;ModelDetailProfile.UseSession(store.ActiveModelDetail);
            var root=new GameObject("Cycle62 readonly actor bindings");var world=new PlayableWorld(root.transform,profile);
            var camera=new GameObject("Inventory projection only").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.enabled=false;camera.orthographic=true;camera.aspect=3456f/2160;camera.transform.position=new Vector3(0,7,-10);camera.transform.LookAt(Vector3.zero);
            var kinds=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Explorer};
            var actors=kinds.Select((kind,i)=>world.Tank(i+1,true,kind)).ToArray();var phases=new List<object>();
            try
            {
                for(int i=0;i<actors.Length;i++)
                {actors[i].Root.position=new Vector3(i%2==0?-2:2,0,i<2?-1.5f:1.5f);ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[i<2?0:4],out var owner);PlayableWorld.PaintOwner(actors[i],owner);}
                foreach(bool upgraded in new[]{false,true})
                {
                    if(upgraded)for(int i=0;i<actors.Length;i++)world.UpdateResearchModel(actors[i],new PlayableEntitySnapshot(i+1,PlayableOwner.Player,kinds[i],default,100,false,0,0,0,upgraded:true));
                    foreach(float size in new[]{5f,(float)profile.ArenaHalfExtent})
                    {
                        camera.orthographicSize=size;foreach(var actor in actors)PlayableWorld.UpdateHealth(actor,camera,1,true);
                        var rows=new List<Dictionary<string,object>>();
                        for(int actorIndex=0;actorIndex<actors.Length;actorIndex++)foreach(var renderer in actors[actorIndex].Hull.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;var materials=renderer.sharedMaterials;var global=new MaterialPropertyBlock();renderer.GetPropertyBlock(global);
                            for(int slot=0;slot<materials.Length;slot++)
                            {
                                var material=materials[slot];var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);
                                rows.Add(new Dictionary<string,object>{["actor"]=actorIndex,["kind"]=kinds[actorIndex].ToString(),["renderer"]=renderer.name,["rendererId"]=renderer.GetInstanceID(),["active"]=renderer.enabled&&renderer.gameObject.activeInHierarchy,
                                    ["meshId"]=mesh.GetInstanceID(),["meshName"]=mesh.name,["meshAsset"]=AssetDatabase.GetAssetPath(mesh),["subMeshCount"]=mesh.subMeshCount,["vertexCount"]=mesh.vertexCount,["slot"]=slot,["slotCount"]=materials.Length,
                                    ["materialId"]=material.GetInstanceID(),["materialName"]=material.name,["materialAsset"]=AssetDatabase.GetAssetPath(material),["shader"]=material.shader.name,["instancing"]=material.enableInstancing,["rawQueue"]=material.rawRenderQueue,["passCount"]=material.passCount,["shadowPass"]=material.FindPass("ShadowCaster"),
                                    ["shadowCasting"]=renderer.shadowCastingMode.ToString(),["receiveShadows"]=renderer.receiveShadows,["globalEmpty"]=global.isEmpty,["globalKnownKeys"]=InventoryBlock(global,material),["slotEmpty"]=block.isEmpty,["slotKnownKeys"]=InventoryBlock(block,material)});
                            }
                        }
                        var eligible=rows.Where(row=>(bool)row["active"]&&(int)row["slotCount"]==1&&(bool)row["globalEmpty"]&&(string)row["shader"]=="Universal Render Pipeline/Lit").ToArray();
                        var groups=eligible.GroupBy(row=>row["meshId"]+"/"+row["materialId"]+"/"+row["slot"]+"/"+row["shadowCasting"]+"/"+row["receiveShadows"]+"/"+row["shadowPass"])
                            .Select(group=>new{key=group.Key,count=group.Count(),actors=group.Select(row=>(int)row["actor"]).Distinct().ToArray()}).ToArray();
                        phases.Add(new{upgraded,cameraSize=size,rows,eligibleRows=eligible.Length,sharedGroups=groups});File.WriteAllText(Path.Combine(output,"inventory.json"),Json(new{profile=profile.Revision,map=profile.AuthoredMap.Id,phases,
                            limits="Actual runtime IDs/subMesh/scope reads,2Tank+2Explorer; no GPU/performance/instanced submission proof. Known keys from actual source writers+shader metadata; MPB has no public arbitrary-key enumerator. No block/scope modifications by inventory. No per-index limitation inferred from scope docs."}));
                        Assert.Greater(groups.Count(g=>g.actors.Length>=2),0,"At least one actual shared mesh/material group must span repeated actors; no unique-mesh assumption");
                    }
                }
                File.WriteAllText(Path.Combine(output,"bound-profile.json"),Json(profile.CopyData()));File.WriteAllText(Path.Combine(output,"bound-model-detail.json"),Json(store.ActiveModelDetail));
            }
            finally{world.Dispose();UnityEngine.Object.DestroyImmediate(root);ModelDetailProfile.UseSession(priorDetail);}
        }
    }
}
#endif
