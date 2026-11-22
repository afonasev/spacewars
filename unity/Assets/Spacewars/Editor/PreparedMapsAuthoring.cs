using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace Spacewars.Editor
{
    [InitializeOnLoad] public static class PreparedMapsAuthoring
    {
        public const string Folder="Assets/Spacewars/Content/Resources/PreparedMaps";
        public static readonly string[] Maps={"ThreeCrossings","Foundry","MossHighland","BasaltCrown","WhiteMeridian","CrimsonGrove","AntennaCity","DeafMirror","NullDock","AiTest","Campaign","ProvingGround","Diagnostic","Nereida"};
        const string OverheadStamp = Folder + "/overhead-stamp.txt";
        static bool CanCaptureOverhead => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        static string stamp;static PreparedSchema schema;static PlayableProfileData gameplay;static bool baking;static readonly HashSet<string> written=new HashSet<string>();
        static PreparedMapsAuthoring(){PreparedMapCache.EditorStamp=CurrentStamp;PreparedMapCache.EditorResourcePath=ResourcePath;PreparedMapCache.EditorSchema=BuildSchema;}
        public static void Invalidate(){stamp=null;schema=null;gameplay=null;PreparedMapCache.CaptureSchema=null;}
        static string ResourcePath(Object obj){string path=AssetDatabase.GetAssetPath(obj);int marker=path.LastIndexOf("/Resources/",StringComparison.Ordinal);if(marker<0)return null;path=path.Substring(marker+11);int dot=path.LastIndexOf('.');return dot<0?path:path.Substring(0,dot);}
        public static string CurrentStamp()
        {
            if(stamp!=null)return stamp;var lines=new List<string>{"format="+PreparedMapCache.FormatVersion};
            foreach(string file in new[]{"Packages/manifest.json","Packages/packages-lock.json"})if(File.Exists(file))lines.Add(file+"="+PreparedMapCache.Hash(File.ReadAllBytes(file)));
            foreach(string folder in new[]{"Assets/Spacewars/Presentation","Assets/Spacewars/Simulation","Assets/Spacewars/Editor"})foreach(string file in Directory.GetFiles(folder,"*.cs",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.Ordinal))lines.Add(file+"="+PreparedMapCache.Hash(File.ReadAllBytes(file)));
            foreach(string path in AssetDatabase.GetAllAssetPaths().Where(p=>(p.StartsWith("Assets/Spacewars/Content/Resources/")||p.StartsWith("Assets/Spacewars/Resources/"))&&!p.StartsWith(Folder+"/")&&!AssetDatabase.IsValidFolder(p)).OrderBy(p=>p,StringComparer.Ordinal))lines.Add(path+"="+AssetDatabase.GetAssetDependencyHash(path));
            return stamp=PreparedMapCache.Hash(Encoding.UTF8.GetBytes(string.Join("\n",lines)));
        }
        public static PreparedSchema BuildSchema()
        {
            if(schema!=null)return schema;var names=new Dictionary<string,int>();
            foreach(string file in Directory.GetFiles("Assets/Spacewars/Presentation","*.cs",SearchOption.AllDirectories))foreach(Match m in Regex.Matches(File.ReadAllText(file),"\\.Set(Float|Int|Integer|VectorArray|Vector|Color|Texture)\\(\\s*\"(_[^\"]+)\"")){
                string type=m.Groups[1].Value,name=m.Groups[2].Value;int kind=type=="Texture"?2:type=="VectorArray"?3:type=="Vector"||type=="Color"?1:0;names[name]=kind;
            }
            var shaders=new List<PreparedShaderSchema>();
            foreach(string path in AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/Spacewars/")&&p.EndsWith(".shader"))){var shader=AssetDatabase.LoadAssetAtPath<Shader>(path);if(!shader)continue;string text=File.ReadAllText(path);shaders.Add(new PreparedShaderSchema{name=shader.name,properties=names.Where(p=>Regex.IsMatch(text,@"\b"+Regex.Escape(p.Key)+@"\b")).Select(p=>new PreparedSchemaProperty{name=p.Key,kind=p.Value}).ToArray()});}
            return schema=new PreparedSchema{properties=names.Select(p=>new PreparedSchemaProperty{name=p.Key,kind=p.Value}).ToArray(),shaders=shaders.ToArray()};
        }
        public static void BeginBake()
        {
            if(baking)throw new InvalidOperationException("Prepared map bake already active");baking=true;written.Clear();Directory.CreateDirectory(Folder);if(File.Exists(Folder+"/stamp.txt"))File.Delete(Folder+"/stamp.txt");PreparedMapCache.BypassReads=true;PreparedMapCache.CaptureSchema=BuildSchema();
            PreparedMapCache.BakeWriter=(group,bytes)=>{File.WriteAllBytes(Folder+"/"+group+".bytes",bytes);written.Add(group);};
        }
        public static void AbortBake(){PreparedMapCache.BakeWriter=null;PreparedMapCache.BypassReads=false;baking=false;}
        private static IEnumerable<string> ExpectedGroups(bool includeOverhead)
        {
            foreach (string key in Maps)
            {
                if (key == "Nereida")
                {
                    yield return "Preview-" + key;
                    continue;
                }

                yield return "World-" + key;
                yield return "Minimap-" + key;
                yield return PlayableTacticalMapTerrain.Group(key);
                if (includeOverhead) yield return PlayableTacticalMapTerrain.OverheadGroup(key);
            }
        }
        public static void EndBake()
        {
            if(!baking)return;PreparedMapCache.BakeWriter=null;PreparedMapCache.BypassReads=false;baking=false;
            if (!ExpectedGroups(CanCaptureOverhead).All(written.Contains))
                throw new InvalidOperationException("Incomplete prepared-map bake; source stamp was not published");
            if (CanCaptureOverhead) File.WriteAllText(OverheadStamp, CurrentStamp());
            File.WriteAllText(Folder+"/stamp.txt",CurrentStamp());File.WriteAllText(Folder+"/schema.json",JsonUtility.ToJson(BuildSchema()));AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        static T Data<T>(string name)=>JsonUtility.FromJson<T>(Resources.Load<TextAsset>(name).text);
        public static PlayableProfile Profile(string key)
        {
            if(gameplay==null){
                // Compile the immutable profile selected by the actual new-match
                // menu; do not read or modify local authoring/player saves.
                var baseline=PlayableProfile.Create(Data<PlayableProfileData>("PlayableProfile"));
                string absent=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".local/prepared-map-authoring",Guid.NewGuid().ToString("N")+".json"));
                var store=new NativeBalanceStore(baseline,absent,false);
                store.ResetRelease();
                gameplay=store.Active.CopyData();
            }
            return PlayableProfile.Create(gameplay,Map(key));
        }
        public static IPlayableTerrain Map(string key){switch(key){case "ThreeCrossings":return new ThreeCrossingsMap(Data<ThreeCrossingsProfileData>(key+"Profile"));case "Foundry":return new FoundryMap(Data<FoundryProfileData>(key+"Profile"));case "MossHighland":return new MossHighlandMap(Data<MossHighlandProfileData>(key+"Profile"));case "BasaltCrown":return new BasaltCrownMap();case "WhiteMeridian":return new WhiteMeridianMap(Data<WhiteMeridianProfileData>(key+"Profile"));case "CrimsonGrove":return new CrimsonGroveMap(Data<CrimsonGroveProfileData>(key+"Profile"));case "DeafMirror":return new DeafMirrorMap();case "AntennaCity":return new AntennaCityMap(Data<AntennaCityProfileData>(key+"Profile"));case "NullDock":return new NullDockMap(Data<NullDockProfileData>(key+"Profile"));case "AiTest":return new AiTestMap();case "Campaign":return new CampaignOpeningMap();case "ProvingGround":return new ProvingGroundMap();case "Nereida":return new NereidaMap(NereidaPreview.LoadProfile());default:return null;}}
        [MenuItem("Tools/Spacewars/Prepare map environments")] public static void BakeAll()
        {
            BeginBake();
            try
            {
                foreach (string key in Maps)
                {
                    var host = new GameObject("Prepare " + key);
                    try
                    {
                        if (key == "Nereida") host.AddComponent<NereidaPreview>().Build(NereidaPreview.LoadProfile());
                        else
                        {
                            var profile = Profile(key);
                            using var world = new PlayableWorld(host.transform, profile);
                            using var compact = new PlayableMapTerrain(profile);
                            using var tactical = new PlayableTacticalMapTerrain(profile);
                            if (CanCaptureOverhead)
                            {
                                if (profile.AuthoredMap == null)
                                    foreach (var obstacle in PlayableMap.SolidObstacles(profile)) world.Obstacle(obstacle);
                                using var overhead = new PlayableTacticalMapTerrain(profile, overheadSource: world);
                            }
                        }
                        Debug.Log("PREPARED_MAP_BAKED " + key);
                    }
                    finally { Object.DestroyImmediate(host); }
                }
                EndBake();
            }
            catch { AbortBake(); throw; }
        }

        public static bool Fresh() =>
            BaseFresh() &&
            File.Exists(OverheadStamp) && File.ReadAllText(OverheadStamp) == CurrentStamp() &&
            ExpectedGroups(true).All(group => File.Exists(Folder + "/" + group + ".bytes"));

        public static bool FreshForBuild() => CanCaptureOverhead ? Fresh() : BaseFresh();

        private static bool BaseFresh() =>
            File.Exists(Folder + "/stamp.txt") &&
            File.ReadAllText(Folder + "/stamp.txt") == CurrentStamp() &&
            ExpectedGroups(false).All(group => File.Exists(Folder + "/" + group + ".bytes"));

    }
    public sealed class PreparedMapsPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported,string[] deleted,string[] moved,string[] old){if(imported.Concat(deleted).Concat(moved).Any(p=>p.StartsWith("Assets/Spacewars/")&&!p.StartsWith(PreparedMapsAuthoring.Folder+"/")&&p!=PreparedMapsAuthoring.Folder))PreparedMapsAuthoring.Invalidate();}
    }
    // Only runs as part of an already requested build; does not initiate a Player build.
    public sealed class PreparedMapsBuildPreparation : IPreprocessBuildWithReport
    {
        public int callbackOrder=>-1000;
        public void OnPreprocessBuild(BuildReport report){if(!PreparedMapsAuthoring.FreshForBuild())PreparedMapsAuthoring.BakeAll();}
    }
}
