using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace Spacewars.Editor
{
    public static class RecastPluginDiagnostic
    {
        public static void Probe()
        {
            const string path="Assets/Spacewars/Plugins/macOS/libspacewars_recast.dylib";
            var importer=AssetImporter.GetAtPath(path) as PluginImporter;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-configureRecastPlugin")>=0){
                if(importer==null)throw new InvalidOperationException("Missing native importer.");
                importer.SetCompatibleWithAnyPlatform(false);importer.SetCompatibleWithEditor(true);importer.SetEditorData("CPU","ARM64");importer.SetEditorData("OS","OSX");importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX,true);importer.SetPlatformData(BuildTarget.StandaloneOSX,"CPU","ARM64");importer.SaveAndReimport();
            }
            Debug.Log("RECAST_PLUGIN_PROBE "+JsonUtility.ToJson(new Facts{Path=path,Exists=System.IO.File.Exists(path),Importer=AssetImporter.GetAtPath(path)?.GetType().FullName,Editor=importer?.GetCompatibleWithEditor()??false,Any=importer?.GetCompatibleWithAnyPlatform()??false,Cpu=importer?.GetEditorData("CPU"),Os=importer?.GetEditorData("OS"),Native=importer?.isNativePlugin??false,MacPlayer=importer?.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX)??false,MacCpu=importer?.GetPlatformData(BuildTarget.StandaloneOSX,"CPU"),MacOs=importer?.GetPlatformData(BuildTarget.StandaloneOSX,"OS"),All=PluginImporter.GetAllImporters().Select(p=>p.assetPath).ToArray()}));
            var type=typeof(Spacewars.Simulation.RecastNavigationMap).Assembly.GetType("Spacewars.Simulation.RecastNative");
            try{Debug.Log("RECAST_ABI_LOADED "+type.GetMethod("sw_recast_abi",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null));}
            catch(Exception error){Debug.LogError("RECAST_ABI_LOAD_FAIL "+error);EditorApplication.Exit(2);return;}
            EditorApplication.Exit(0);
        }
        [Serializable]private sealed class Facts{public string Path,Importer,Cpu,Os,MacCpu,MacOs;public bool Exists,Editor,Any,Native,MacPlayer;public string[] All;}
    }
}
