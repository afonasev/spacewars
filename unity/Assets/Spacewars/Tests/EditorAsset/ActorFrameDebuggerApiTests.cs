using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.EditorAsset
{
    // Preflight only: never enables FrameDebugger or creates any scene/window.
    public sealed class ActorFrameDebuggerApiTests
    {
        [Serializable] private sealed class ApiType
        { public string name, assembly, location;public string[] methods,properties,fields; }
        [Serializable] private sealed class Report
        {
            public string unity,device,editorAssembly;
            public bool locallySupported,debuggerEnabled;
            public string connectedProfiler,remotePlayerGuid;
            public string[] batchBreakCauseStrings;
            public List<ApiType> types=new(); public List<string> loadErrors=new();
        }
        private const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private const BindingFlags Declared=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
        [Serializable] private sealed class NativeEnableState66
        {
            public string stage,shaderTag;
            public bool locallySupported,enabled,localEnabled,remoteEnabled,connectionIsEditor,isPlaying,isPaused;
            public int connectedProfiler,remoteGuid,count,limit,eventsHash,eventArrayLength;
            public uint eventDataHash;
        }
        [Serializable] private sealed class Connection66 {public int guid;public string identifier;}
        [Serializable] private sealed class LocalEnableReport66
        {
            public string unity,device,constantSource;
            public int editorGuid,invalidGuid,loadingGuid;
            public List<Connection66> available=new();public List<NativeEnableState66> states=new();
        }
        [UnityTest] public IEnumerator SourceProvenLocalEnableReportsNativeState()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_SHADER_METADATA_OUTPUT");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            Type Find(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).FirstOrDefault(t=>t!=null);
            var utility=Find("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility");var runtime=Find("UnityEngine.FrameDebugger");var profiler=Find("UnityEditorInternal.ProfilerDriver");var connection=Find("UnityEditor.Networking.PlayerConnection.GeneralConnectionState");
            Assert.NotNull(utility);Assert.NotNull(runtime);Assert.NotNull(profiler);Assert.NotNull(connection);
            object U(string name)=>utility.GetProperty(name,Static).GetValue(null);
            object P(string name)=>profiler.GetProperty(name,Static).GetValue(null);
            object CallU(string name,params object[] args)=>utility.GetMethod(name,Static).Invoke(null,args);
            object CallP(string name,params object[] args)=>profiler.GetMethod(name,Static).Invoke(null,args);
            bool RuntimeFlag(string name)=>(bool)runtime.GetMethod(name,Static).Invoke(null,null);
            var report=new LocalEnableReport66 {unity=Application.unityVersion,device=SystemInfo.graphicsDeviceType+":"+SystemInfo.graphicsDeviceName,
                constantSource=connection.Assembly.FullName+":"+connection.FullName,
                editorGuid=(int)connection.GetField("kEditorGuid",Static).GetRawConstantValue(),invalidGuid=(int)connection.GetField("kInvalidGuid",Static).GetRawConstantValue(),loadingGuid=(int)connection.GetField("kLoadingGuid",Static).GetRawConstantValue()};
            foreach(int guid in (int[])CallP("GetAvailableProfilers"))report.available.Add(new Connection66 {guid=guid,identifier=(string)CallP("GetConnectionIdentifier",guid)});
            void Snapshot(string stage)
            {
                var events=CallU("GetFrameEvents") as Array;
                report.states.Add(new NativeEnableState66 {stage=stage,locallySupported=(bool)U("locallySupported"),enabled=(bool)runtime.GetProperty("enabled",Static).GetValue(null),localEnabled=RuntimeFlag("IsLocalEnabled"),remoteEnabled=RuntimeFlag("IsRemoteEnabled"),connectionIsEditor=(bool)CallP("IsConnectionEditor"),connectedProfiler=(int)P("connectedProfiler"),remoteGuid=(int)CallU("GetRemotePlayerGUID"),count=(int)U("count"),limit=(int)U("limit"),eventsHash=(int)U("eventsHash"),eventDataHash=(uint)U("eventDataHash"),eventArrayLength=events?.Length??-1,isPlaying=EditorApplication.isPlaying,isPaused=EditorApplication.isPaused,shaderTag=Shader.globalRenderPipeline});
                File.WriteAllText(Path.Combine(output,"native-local-state.json"),JsonUtility.ToJson(report,true));
            }
            Snapshot("before");var before=report.states[0];
            Assert.True(before.locallySupported);Assert.False(before.enabled,"Preserve a foreign native debugger session");
            Assert.True(before.connectionIsEditor,"No profiler connection changes admitted by this probe");Assert.AreEqual(report.editorGuid,before.connectedProfiler,"Select only source-proven current LOCAL mapping");Assert.AreNotEqual(report.invalidGuid,report.editorGuid);
            bool owned=false;
            try
            {
                owned=true;CallU("SetEnabled",true,report.editorGuid);Snapshot("immediately-after-enable");
                EditorApplication.QueuePlayerLoopUpdate();yield return null;Snapshot("after-queued-frame");
            }
            finally
            {
                if(owned&&(bool)runtime.GetProperty("enabled",Static).GetValue(null))CallU("SetEnabled",false,(int)CallU("GetRemotePlayerGUID"));
                Snapshot("finally-restored");
            }
            var immediate=report.states.Single(x=>x.stage=="immediately-after-enable");var queued=report.states.Single(x=>x.stage=="after-queued-frame");var restored=report.states.Last();
            Assert.True(immediate.enabled&&immediate.localEnabled&&!immediate.remoteEnabled,"Source-proven local enable unavailable; no scene admitted");
            Assert.True(queued.enabled&&queued.localEnabled&&!queued.remoteEnabled,"Local native state did not remain enabled");
            Assert.False(restored.enabled||restored.localEnabled||restored.remoteEnabled,"Owned native debugger must disable exactly");
            Assert.AreEqual(before.connectedProfiler,restored.connectedProfiler);Assert.AreEqual(before.remoteGuid,restored.remoteGuid);Assert.AreEqual(before.isPlaying,restored.isPlaying);Assert.AreEqual(before.isPaused,restored.isPaused);Assert.AreEqual(before.shaderTag,restored.shaderTag);
        }
        [Test] public void LoadedEditorApisReportNativeFrameDebuggerSupport()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_SHADER_METADATA_OUTPUT");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            var report=new Report {unity=Application.unityVersion,device=SystemInfo.graphicsDeviceType+":"+SystemInfo.graphicsDeviceName,editorAssembly=typeof(UnityEditor.Editor).Assembly.FullName};
            var found=new List<Type>();
            try
            {
                foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try{types=assembly.GetTypes();}
                    catch(ReflectionTypeLoadException e){types=e.Types.Where(t=>t!=null).ToArray();report.loadErrors.Add(assembly.FullName+":"+string.Join(";",e.LoaderExceptions.Select(x=>x.Message)));}
                    foreach(var type in types.Where(t=>t.FullName!=null&&(t.Name=="FrameDebuggerUtility"||t.Name=="FrameDebuggerEventData"||t.Name=="FrameDebuggerEvent"||t.Name=="FrameEventType"||t.Name=="FrameDebugger"||t.FullName=="UnityEditorInternal.ProfilerDriver")))
                    {
                        found.Add(type);report.types.Add(new ApiType {name=type.FullName,assembly=assembly.FullName,location=assembly.Location,
                            methods=type.GetMethods(Declared).Select(m=>m.ToString()).OrderBy(s=>s).ToArray(),
                            properties=type.GetProperties(Declared).Select(p=>p.ToString()).OrderBy(s=>s).ToArray(),
                            fields=type.GetFields(Declared).Select(f=>f.ToString()+(f.IsLiteral?"="+f.GetRawConstantValue():"")).OrderBy(s=>s).ToArray()});
                    }
                }
                var utility=found.Single(t=>t.FullName=="UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility");
                report.locallySupported=(bool)utility.GetProperty("locallySupported",Static).GetValue(null);
                report.remotePlayerGuid=utility.GetMethod("GetRemotePlayerGUID",Static).Invoke(null,null).ToString();
                report.batchBreakCauseStrings=(string[])utility.GetMethod("GetBatchBreakCauseStrings",Static).Invoke(null,null);
                var runtime=found.Single(t=>t.Name=="FrameDebugger"&&t.Namespace=="UnityEngine");
                report.debuggerEnabled=(bool)runtime.GetProperty("enabled",Static).GetValue(null);
                var profiler=found.Single(t=>t.FullName=="UnityEditorInternal.ProfilerDriver");
                report.connectedProfiler=profiler.GetProperty("connectedProfiler",Static).GetValue(null).ToString();
            }
            finally{File.WriteAllText(Path.Combine(output,"frame-debugger-api.json"),JsonUtility.ToJson(report,true));}
            Assert.True(report.locallySupported,"Native FrameDebugger unsupported; no scene/capture is admitted");
            Assert.False(report.debuggerEnabled,"Preserve an existing debugger session; never replace it");
            Assert.Greater(report.batchBreakCauseStrings.Length,0,"Missing native batch-break lookup");
            Assert.True(report.types.Any(t=>t.name.EndsWith("FrameDebuggerEventData")),"Missing actual event schema");
        }
    }
}
