using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace Spacewars.Tests.EditorAsset
{
    // QA-only compiler inspection. Does not edit shaders, materials or render settings.
    public sealed class ActorInstancingMetadataTests
    {
        [Serializable] private sealed class Field
        {
            public string name, constantType, dataType;
            public int offset, rows, columns, arraySize, structSize;
            public Field[] fields;
        }
        [Serializable] private sealed class Buffer
        { public string name; public int size; public Field[] fields; }
        [Serializable] private sealed class Variant
        {
            public string shader, pass, stage; public string[] keywords;
            public bool preprocessSuccess, compileSuccess;
            public string[] preprocessMessages, compileMessages;
            public int compiledBytes;
            public Buffer[] buffers;
        }
        [Serializable] private sealed class ShaderInfo
        {
            public string name, path, hasInstancing, srpCompatibility, srpReason;
            public string[] passes, properties;
        }
        [Serializable] private sealed class Report
        {
            public string unity, platform="Metal", buildTarget="StandaloneOSX";
            public List<ShaderInfo> shaders=new(); public List<Variant> variants=new();
        }
        private static Field ConvertField(ShaderData.ConstantInfo f) => new Field {
            name=f.Name, constantType=f.ConstantType.ToString(), dataType=f.DataType.ToString(),
            offset=f.Index, rows=f.Rows, columns=f.Columns, arraySize=f.ArraySize, structSize=f.StructSize,
            fields=f.StructFields?.Select(ConvertField).ToArray()
        };
        private static string Query(string name, params object[] args)
        {
            var methods=typeof(ShaderUtil).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)
                .Where(m=>m.Name==name && m.GetParameters().Length==args.Length).ToArray();
            if(methods.Length!=1)return "NotRun: installed overload unavailable/ambiguous";
            try{return methods[0].Invoke(null,args)?.ToString()??"null";}
            catch(Exception e){return "NotRun: "+e.GetBaseException().Message;}
        }
        [Serializable] private sealed class SrpRow
        {
            public string shader, beforeCode, beforeReason, afterCode, afterReason, hasInstancing;
            public bool setPass, supported; public int activeSubshader, materialPassCount;
        }
        [Serializable] private sealed class SrpReport
        {
            public string unity, device, pipeline, tagBefore, assetTag, tagDuring, tagRestored; public List<SrpRow> rows=new();
        }
        [Test] public void ExistingLitPassInitializationReportsSrpCompatibility()
        {
            // Mirrors Unity6000.3 ShaderInspector.cs: transient Material.SetPass(0), then active subshader/status.
            Assert.AreNotEqual(UnityEngine.Rendering.GraphicsDeviceType.Null,SystemInfo.graphicsDeviceType);
            string output=Environment.GetEnvironmentVariable("SPACEWARS_SHADER_METADATA_OUTPUT");
            Assert.False(string.IsNullOrWhiteSpace(output)); Directory.CreateDirectory(output);
            var report=new SrpReport {unity=Application.unityVersion,device=SystemInfo.graphicsDeviceType+":"+SystemInfo.graphicsDeviceName,
                pipeline=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?.name??"null",tagBefore=Shader.globalRenderPipeline,
                assetTag=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?.renderPipelineShaderTag};
            Assert.False(string.IsNullOrEmpty(report.assetTag),"Actual SRP shader tag absent");
            try
            {
                // Mirrors RenderPipelineManager.TryPrepareRenderPipeline before shader selection.
                Shader.globalRenderPipeline=report.assetTag; report.tagDuring=Shader.globalRenderPipeline;
                foreach(string path in new[]{"Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader",
                    "Assets/Spacewars/Tests/Fixtures/InstancedActorLit/Cycle61Lit.shader"})
                {
                    var shader=AssetDatabase.LoadAssetAtPath<Shader>(path);Assert.NotNull(shader);
                    var row=new SrpRow {shader=shader.name,supported=shader.isSupported,
                        beforeCode=Query("GetSRPBatcherCompatibilityCode",shader,0)};
                    report.rows.Add(row);
                    if(int.TryParse(row.beforeCode,out int before))row.beforeReason=Query("GetSRPBatcherCompatibilityIssueReason",shader,0,before);
                    var material=new Material(shader);
                    try
                    {
                        row.materialPassCount=material.passCount;
                        row.setPass=material.SetPass(0);
                        row.activeSubshader=ShaderUtil.GetShaderData(shader).ActiveSubshaderIndex;
                        row.afterCode=Query("GetSRPBatcherCompatibilityCode",shader,row.activeSubshader);
                        if(int.TryParse(row.afterCode,out int after))row.afterReason=Query("GetSRPBatcherCompatibilityIssueReason",shader,row.activeSubshader,after);
                        row.hasInstancing=Query("HasInstancing",shader);
                    }
                    finally {UnityEngine.Object.DestroyImmediate(material);}
                }
            }
            finally {Shader.globalRenderPipeline=report.tagBefore;report.tagRestored=Shader.globalRenderPipeline;File.WriteAllText(Path.Combine(output,"srp-initialized.json"),JsonUtility.ToJson(report,true));}
            Assert.AreEqual(2,report.rows.Count);
            Assert.AreEqual(report.tagBefore,report.tagRestored,"Global SRP tag must restore exactly");
            Assert.True(report.rows.All(r=>r.supported&&r.setPass&&r.activeSubshader==0&&r.materialPassCount==9),"Actual URP subshader/pass contract unavailable; never accept fallback");
            Assert.True(report.rows.All(r=>r.afterCode!="1"&&!r.afterCode.StartsWith("NotRun")),"Compatibility still uninitialized; retain NotRun");
        }
        [Test] public void ExistingLitVariantsReportMetalConstantBuffers()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_SHADER_METADATA_OUTPUT");
            Assert.False(string.IsNullOrWhiteSpace(output)); Directory.CreateDirectory(output);
            var report=new Report {unity=Application.unityVersion};
            try
            {
                foreach(string path in new[]{"Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader",
                    "Assets/Spacewars/Tests/Fixtures/InstancedActorLit/Cycle61Lit.shader"})
                {
                    var shader=AssetDatabase.LoadAssetAtPath<Shader>(path); Assert.NotNull(shader,path);
                    var data=ShaderUtil.GetShaderData(shader); var sub=data.GetSubshader(0); Assert.NotNull(sub);
                    var info=new ShaderInfo {name=shader.name,path=path,hasInstancing=Query("HasInstancing",shader),
                        srpCompatibility=Query("GetSRPBatcherCompatibilityCode",shader,0),
                        srpReason="Pending compatibility code",
                        passes=Enumerable.Range(0,sub.PassCount).Select(i=>sub.GetPass(i).Name).ToArray(),
                        properties=Enumerable.Range(0,shader.GetPropertyCount()).Select(i=>shader.GetPropertyName(i)+":"+shader.GetPropertyType(i)+":"+shader.GetPropertyFlags(i)).ToArray()};
                    if(int.TryParse(info.srpCompatibility,out int code))info.srpReason=Query("GetSRPBatcherCompatibilityIssueReason",shader,0,code);
                    report.shaders.Add(info);
                    for(int p=0;p<sub.PassCount;p++)
                    {
                        var pass=sub.GetPass(p); if(pass.Name!="ForwardLit" && pass.Name!="ShadowCaster")continue;
                        foreach(bool instanced in new[]{false,true})foreach(var stage in new[]{ShaderType.Vertex,ShaderType.Fragment})
                        {
                            var keys=instanced?new[]{"INSTANCING_ON"}:Array.Empty<string>();
                            var row=new Variant {shader=shader.name,pass=pass.Name,stage=stage.ToString(),keywords=keys};
                            report.variants.Add(row);
                            string prefix=(shader.name.Contains("Spacewars")?"P":"A")+"-"+pass.Name+"-"+stage+"-"+(instanced?"on":"off");
                            var pre=pass.PreprocessVariant(stage,keys,ShaderCompilerPlatform.Metal,BuildTarget.StandaloneOSX,true);
                            row.preprocessSuccess=pre.Success; row.preprocessMessages=pre.Messages?.Select(m=>m.severity+":"+m.message+":"+m.messageDetails).ToArray();
                            File.WriteAllText(Path.Combine(output,prefix+".hlsl"),pre.PreprocessedCode??"");
                            var compiled=pass.CompileVariant(stage,keys,ShaderCompilerPlatform.Metal,BuildTarget.StandaloneOSX);
                            row.compileSuccess=compiled.Success; row.compileMessages=compiled.Messages?.Select(m=>m.severity+":"+m.message+":"+m.messageDetails).ToArray();
                            row.compiledBytes=compiled.ShaderData?.Length??0;
                            if(compiled.ShaderData!=null)File.WriteAllBytes(Path.Combine(output,prefix+".bin"),compiled.ShaderData);
                            row.buffers=compiled.ConstantBuffers?.Select(b=>new Buffer {name=b.Name,size=b.Size,fields=b.Fields?.Select(ConvertField).ToArray()}).ToArray();
                        }
                    }
                }
            }
            finally {File.WriteAllText(Path.Combine(output,"metadata.json"),JsonUtility.ToJson(report,true));}
            Assert.AreEqual(16,report.variants.Count,"Both stages of two passes/two variants/two existing shaders");
            Assert.True(report.variants.All(v=>v.compileSuccess&&v.preprocessSuccess),"Metadata collection incomplete; inspect saved rows");
            // A non-alpha ShadowCaster fragment returns a constant and legitimately has no buffers.
            Assert.True(report.variants.All(v=>v.compiledBytes>0),"Missing compiled stage data");
            Assert.True(report.variants.Where(v=>v.pass=="ForwardLit").All(v=>v.buffers!=null && v.buffers.Length>0),"Missing used ForwardLit buffers");
            var instance=report.variants.Single(v=>v.shader.StartsWith("Spacewars/")&&v.pass=="ForwardLit"&&v.stage=="Fragment"&&v.keywords.Length==1);
            Assert.True(instance.buffers.Any(b=>b.name=="UnityInstancing_SpacewarsActorProps"),"Prototype instance buffer not reflected");
        }
    }
}
