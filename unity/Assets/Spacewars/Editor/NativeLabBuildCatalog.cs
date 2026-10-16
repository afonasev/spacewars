using System.IO;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Spacewars.Editor
{
    // Build-time derivation only; marking a release never invokes a build.
    public sealed class NativeLabBuildCatalog : IPreprocessBuildWithReport
    {
        public int callbackOrder=>0;
        public void OnPreprocessBuild(BuildReport report)=>Prepare();
        public static void Prepare()
        {
            var source=Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,"../..",NativeBalanceStore.ProjectRelativePath));
            if(!File.Exists(source))throw new BuildFailedException("Missing Git-owned laboratory profiles: "+source);
            var store=new NativeBalanceStore(PlayableProfile.Default,source,true);
            var catalog=store.ExportPackage();
            const string path="Assets/Spacewars/Resources/NativeLabCatalog.json";
            File.WriteAllText(path,catalog.ToString()+"\n");AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
