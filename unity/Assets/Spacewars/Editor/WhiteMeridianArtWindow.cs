using System;
using System.IO;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEditor;
using UnityEngine;
namespace Spacewars.Editor
{
    public sealed class WhiteMeridianArtWindow:EditorWindow
    {
        private const string Root="Assets/Spacewars/Content/Resources/";
        private IndustrialLightingProfile lighting;private WhiteMeridianLightingVariants lightingVariants;private int lightingIndex;private WhiteMeridianProfileData map;private WhiteMeridianArtProfile art;private Vector2 scroll;private string error;
        [MenuItem("Spacewars/Balance Lab/White Meridian")]
        public static void Open()=>GetWindow<WhiteMeridianArtWindow>("White Meridian");
        private void OnEnable(){map=JsonUtility.FromJson<WhiteMeridianProfileData>(File.ReadAllText(Root+"WhiteMeridianProfile.json"));art=WhiteMeridianArtProfile.Load();lighting=IndustrialLightingProfile.Load("white-meridian-astra-v1");lightingVariants=WhiteMeridianLightingVariants.Load();}
        private void OnGUI(){if(map==null||art==null)return;scroll=EditorGUILayout.BeginScrollView(scroll);
            foreach(var f in WhiteMeridianMap.Fields){double value=EditorGUILayout.Slider(new GUIContent(f.Label+" ("+f.Unit+")",f.Path+"\n"+f.Description),(float)f.Read(map),(float)f.Minimum,(float)f.Maximum);f.Write(map,Math.Round(value/f.Step)*f.Step);}
            foreach(var f in art.Parameters()){float value=EditorGUILayout.Slider(new GUIContent(f.Label+" ("+f.Unit+")",f.Path+"\n"+f.Description),f.Read(),f.Minimum,f.Maximum);f.Write(Mathf.Clamp(Mathf.Round(value/f.Step)*f.Step,f.Minimum,f.Maximum));}
            lightingIndex=EditorGUILayout.Popup("Match lighting variant",lightingIndex,Array.ConvertAll(lightingVariants.variants,v=>v.label));
            foreach(var f in lightingVariants.Parameters(lightingIndex)){float value=EditorGUILayout.Slider(new GUIContent(f.Label+" ("+f.Unit+")",f.Path+"\n"+f.Description),f.Read(),f.Minimum,f.Maximum);f.Write(Mathf.Clamp(Mathf.Round(value/f.Step)*f.Step,f.Minimum,f.Maximum));}
            EditorGUILayout.EndScrollView();if(error!=null)EditorGUILayout.HelpBox(error,MessageType.Error);
            if(GUILayout.Button("Validate and save next-match profiles"))try{art.Validate();lighting.Validate();lightingVariants.Validate();PlayableProfile.Create(PlayableProfile.Default.CopyData(),new WhiteMeridianMap(map));map.revision++;art.revision++;File.WriteAllText(Root+"WhiteMeridianProfile.json",JsonUtility.ToJson(map,true)+"\n");File.WriteAllText(Root+"WhiteMeridianArtProfile.json",JsonUtility.ToJson(art,true)+"\n");lightingVariants.revision++;File.WriteAllText(Root+"WhiteMeridianLightingVariants.json",JsonUtility.ToJson(lightingVariants,true)+"\n");AssetDatabase.Refresh();error=null;}catch(Exception e){error=e.Message;}
        }
    }
}
