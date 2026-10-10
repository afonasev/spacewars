using System;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // Device preferences override presentation speed only, never the match profile.
    public static class NativeCameraScrollSettings
    {
        public const string EdgeKey="spacewars.camera.edgePanSpeed",ArrowKey="spacewars.camera.arrowPanSpeed";
        private static readonly PlayableProfileField EdgeField=PlayableProfileMetadata.Fields.Single(f=>f.Path=="camera.edgePanSpeed");
        private static readonly PlayableProfileField ArrowField=PlayableProfileMetadata.Fields.Single(f=>f.Path=="camera.panSpeed");
        public static float EdgeSpeed(double fallback)=>Read(EdgeKey,EdgeField,fallback);
        public static float ArrowSpeed(double fallback)=>Read(ArrowKey,ArrowField,fallback);
        private static float Read(string key,PlayableProfileField field,double fallback)
        {
            float value=PlayerPrefs.GetFloat(key,(float)fallback);
            if(float.IsNaN(value)||float.IsInfinity(value))value=(float)fallback;
            return Mathf.Clamp(value,(float)field.Minimum,(float)field.Maximum);
        }
        public static Vector3 Displacement(Transform camera,Vector2 axis,float speed,float seconds)
        {
            var right=Vector3.ProjectOnPlane(camera.right,Vector3.up).normalized;
            var forward=Vector3.ProjectOnPlane(camera.forward,Vector3.up).normalized;
            return (right*axis.x+forward*axis.y)*speed*seconds;
        }
        public static void AddSliders(VisualElement parent)
        {
            var data=JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text);
            Add(EdgeKey,EdgeField,"Камера · край экрана","settings-camera-edge",EdgeField.Read(data));
            Add(ArrowKey,ArrowField,"Камера · стрелки","settings-camera-arrows",ArrowField.Read(data));
            void Add(string key,PlayableProfileField field,string title,string name,double fallback)
            {
                var slider=new Slider(title,(float)field.Minimum,(float)field.Maximum){name=name,value=Read(key,field,fallback),tooltip="Скорость прокрутки камеры, м/с"};
                var label=OrbitalTheme.Text(slider.value.ToString("0.#")+" м/с","orbital-muted");label.AddToClassList("settings-value");parent.Add(slider);parent.Add(label);
                slider.RegisterValueChangedCallback(e=>
                {
                    float value=Mathf.Clamp((float)(field.Minimum+Math.Round((e.newValue-field.Minimum)/field.Step)*field.Step),(float)field.Minimum,(float)field.Maximum);
                    slider.SetValueWithoutNotify(value);PlayerPrefs.SetFloat(key,value);PlayerPrefs.Save();label.text=value.ToString("0.#")+" м/с";
                });
                slider.userData=new Action<int>(direction=>slider.value=Mathf.Clamp(slider.value+direction*(float)field.Step,slider.lowValue,slider.highValue));
            }
        }
    }
}
