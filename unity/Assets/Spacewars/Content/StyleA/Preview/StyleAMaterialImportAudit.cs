using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spacewars.StyleA.Preview
{
    [Serializable] public sealed class MaterialSourceRow
    {
        public string path, name, sourceSha256, priorImportedSha256, sourceYaml;
        public Color sourceLegacyColor, sourceBaseColor, sourceEmissionColor;
        public bool sourceEmission;
    }
    [Serializable] public sealed class MaterialSourceManifest
    {
        public string predecessor;
        public MaterialSourceRow[] rows;
    }
    [Serializable] public sealed class MaterialState
    {
        public string identity, shader, role, baseTexture, emissionTexture;
        public bool emission, validEmissionKeyword;
        public int giFlags;
        public string[] keywords;
        public Color baseColor, legacyColor, emissionColor;
    }
    public static class MaterialImportComparison
    {
        public static Material Clone(Material original, MaterialSourceRow source, bool emission, bool legacy)
        {
            if (!original || source == null) throw new ArgumentException("Material and source are required");
            var clone = new Material(original);
            if (emission) { if (source.sourceEmission) clone.EnableKeyword("_EMISSION"); else clone.DisableKeyword("_EMISSION"); }
            if (legacy && source.name.EndsWith("foundation-concrete", StringComparison.Ordinal))
                clone.SetColor("_Color", source.sourceLegacyColor);
            return clone;
        }
        public static MaterialState Read(Material material, string identity) => new MaterialState
        {
            identity = identity, role = material.name, shader = material.shader.name,
            emission = material.IsKeywordEnabled("_EMISSION"),
            validEmissionKeyword = material.shader.keywordSpace.FindKeyword("_EMISSION").isValid,
            keywords = material.shaderKeywords, giFlags = (int)material.globalIlluminationFlags,
            baseColor = material.GetColor("_BaseColor"), legacyColor = material.GetColor("_Color"),
            emissionColor = material.GetColor("_EmissionColor"),
            baseTexture = material.GetTexture("_BaseMap") ? material.GetTexture("_BaseMap").name : null,
            emissionTexture = material.GetTexture("_EmissionMap") ? material.GetTexture("_EmissionMap").name : null
        };
    }
    public sealed class StyleAMaterialImportAudit : MonoBehaviour
    {
        // Fixed seedless diagnostic conditions, not gameplay/Balance Lab tuning.
        public Material[] imported;
        public MaterialSourceRow[] sources;
        public GameObject[] prefabs;
        public Camera auditCamera;
        public ShaderVariantCollection diagnosticVariants;
        [Serializable] private sealed class Readback { public string mode; public MaterialState[] materials; public string[] bindings; }
        [Serializable] private sealed class Host
        {
            public string classification = "DIAGNOSTIC_NOT_ACCEPTANCE", profile = "style-a-material-import-fixture-v1", seed = "none";
            public string unity, gpu, os, pipeline, utc;
            public int width, height, quality, variantCount;
            public bool focused, muted;
        }
        private string output;
        private IEnumerator Start()
        {
            AudioListener.volume = 0; Application.runInBackground = true;
            QualitySettings.vSyncCount = 1; Application.targetFrameRate = 60;
            var args = Environment.GetCommandLineArgs();
            for (var i=0;i+1<args.Length;i++) if(args[i]=="-materialEvidence") output=args[i+1];
            if(string.IsNullOrEmpty(output)) { Debug.LogError("-materialEvidence required"); Application.Quit(2); yield break; }
            Directory.CreateDirectory(output);
            diagnosticVariants.WarmUp();
            // Read the packaged material assets before any diagnostic copies are created.
            File.WriteAllText(Path.Combine(output,"packaged-imported.json"),JsonUtility.ToJson(new Readback {mode="packaged-imported",
                materials=imported.Select((m,i)=>MaterialImportComparison.Read(m,sources[i].name)).ToArray(),
                bindings=Array.Empty<string>()},true));
            File.WriteAllText(Path.Combine(output,"host.json"),JsonUtility.ToJson(new Host {
                unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,os=SystemInfo.operatingSystem,
                pipeline=GraphicsSettings.currentRenderPipeline.name,utc=DateTime.UtcNow.ToString("O"),
                width=Screen.width,height=Screen.height,quality=QualitySettings.GetQualityLevel(),
                variantCount=diagnosticVariants.variantCount,focused=Application.isFocused,muted=AudioListener.volume==0 },true));
            foreach(var mode in new[]{"imported-before","source-properties","legacy-only","imported-after"})
            {
                // The first case renders the actual packaged assets. Other cases are controls.
                var copies=mode=="imported-before" ? imported : imported.Select((m,i)=>MaterialImportComparison.Clone(m,sources[i],mode=="source-properties",mode=="source-properties"||mode=="legacy-only")).ToArray();
                var grid=new GameObject("Diagnostic nine assets"); var bindings=new List<string>();
                for(var i=0;i<prefabs.Length;i++)
                {
                    var go=Instantiate(prefabs[i],grid.transform);go.transform.position=new Vector3((i%3-1)*6,0,(i/3-1)*6);
                    go.transform.rotation=Quaternion.Euler(0,18,0);
                    foreach(var renderer in go.GetComponentsInChildren<Renderer>(true))
                    {
                        var materials=renderer.sharedMaterials;
                        for(var slot=0;slot<materials.Length;slot++)
                        {
                            var index=Array.IndexOf(imported,materials[slot]);
                            if(index<0)continue;
                            bindings.Add(prefabs[i].name+"/"+renderer.name+"/"+slot+"="+sources[index].name);
                            materials[slot]=copies[index];
                        }
                        renderer.sharedMaterials=materials;
                    }
                }
                File.WriteAllText(Path.Combine(output,mode+".json"),JsonUtility.ToJson(new Readback {mode=mode,
                    materials=copies.Select((m,i)=>MaterialImportComparison.Read(m,sources[i].name)).ToArray(),bindings=bindings.ToArray()},true));
                auditCamera.transform.position=new Vector3(0,20,-24);auditCamera.transform.LookAt(Vector3.zero);auditCamera.orthographicSize=12;
                yield return Capture(mode+"-assets");
                grid.SetActive(false);
                var swatches=new GameObject("16 affected material swatches");
                for(var i=0;i<copies.Length;i++)
                {
                    var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);sphere.transform.SetParent(swatches.transform);
                    sphere.transform.position=new Vector3((i%4-1.5f)*2.5f,(1.5f-i/4)*2.5f,0);
                    sphere.transform.localScale=Vector3.one*1.8f;sphere.GetComponent<Renderer>().sharedMaterial=copies[i];
                }
                auditCamera.transform.position=new Vector3(0,0,-15);auditCamera.transform.rotation=Quaternion.identity;auditCamera.orthographicSize=5.5f;
                yield return Capture(mode+"-swatches");
                Destroy(grid);Destroy(swatches);if(mode!="imported-before")foreach(var m in copies)Destroy(m);yield return null;
                Debug.Log("MATERIAL_IMPORT_CASE_COMPLETE "+mode);
            }
            Debug.Log("MATERIAL_IMPORT_AUDIT_COMPLETE");Application.Quit();
        }
        private IEnumerator Capture(string name)
        {
            // Settle rendering only; no performance samples or timing claim.
            for(var frame=0;frame<30;frame++)yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));
            yield return new WaitForSeconds(.5f);
        }
    }
}
