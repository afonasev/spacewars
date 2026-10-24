using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace Spacewars.Editor
{
    public static class HawkMatchProject
    {
        public static void Prepare()
        {
            RingHangarAssets.Build();
            // Technical rasterization of the established pale/cyan HUD silhouette language.
            const int size=256;var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);var pixels=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float u=(x-127.5f)/112,v=(y-127.5f)/112;
                bool body=Mathf.Abs(u)<.2f*(1-.35f*Mathf.Max(0,v))&&v>-.25f&&v<.72f;
                bool tail=Mathf.Abs(u)<.055f&&v>-.85f&&v<-.1f;
                bool rotor=Mathf.Abs(v-.15f)<.035f&&Mathf.Abs(u)<.94f||Mathf.Abs(u)<.035f&&v>-.55f&&v<.94f;
                bool pods=Mathf.Abs(u)>.25f&&Mathf.Abs(u)<.36f&&v>-.18f&&v<.3f;
                bool tailplane=Mathf.Abs(u)<.36f&&v>-.8f&&v<-.73f;
                pixels[y*size+x]=body||tail||rotor||pods||tailplane?new Color(.89f,.95f,.97f,1):Color.clear;
                if(body&&v>.35f&&v<.55f)pixels[y*size+x]=new Color(.22f,.82f,.9f,1);
            }
            texture.SetPixels(pixels);texture.Apply();string path="Assets/Spacewars/Content/Resources/OrbitalIcons/hawk.png";File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();AssetDatabase.SaveAssets();
        }
        public static void BuildQA()
        {
            // Minimal local visual QA Player; uses existing scene/assets and never invokes release/install/deploy.
            string output=Path.GetFullPath("../.local/hawk-player/Spacewars.app");Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{PlayableProject.ScenePath},locationPathName=output,target=BuildTarget.StandaloneOSX,options=BuildOptions.Development|BuildOptions.DetailedBuildReport});
            if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(report.summary.result.ToString());Debug.Log("HAWK_QA_BUILD_PASS bytes="+report.summary.totalSize);
        }
    }
}
