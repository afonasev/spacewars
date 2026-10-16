using System.Linq;
using Spacewars.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // Local presentation preferences; never mutate the match profile or authority.
    public sealed class NativeLocalControlSettings
    {
        private readonly NativeLocalInputProfile profile;
        private const string Prefix="spacewars.controls.";
        public float CursorSpeed {get=>Load("cursorSpeed",profile.cursorSpeed);private set=>Save("cursorSpeed",value);}
        public float CameraSpeed {get=>Load("cameraSpeed",profile.cameraSpeed);private set=>Save("cameraSpeed",value);}
        public float CursorRecenterIdleMs {get=>Load("cursorRecenterIdleMs",profile.cursorRecenterIdleMs);private set=>Save("cursorRecenterIdleMs",value);}
        public NativeLocalControlSettings()
        {profile=JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text);profile.Validate();}
        private float Load(string key,float fallback)
        {var field=profile.metadata.Single(m=>m.path.EndsWith("."+key));float value=PlayerPrefs.GetFloat(Prefix+key,fallback);return float.IsNaN(value)||float.IsInfinity(value)?fallback:Mathf.Clamp(value,field.min,field.max);}
        private static void Save(string key,float value){NativePreferenceWriter.SetFloat(Prefix+key,value);}
        public void AddSliders(VisualElement parent)
        {
            Add("cursorSpeed","Скорость курсора · RS",CursorSpeed,value=>{CursorSpeed=value;Save("cursorSpeed",value);});
            Add("cameraSpeed","Скорость камеры · LS",CameraSpeed,value=>{CameraSpeed=value;Save("cameraSpeed",value);});
            Add("cursorRecenterIdleMs","Пауза перед центрированием курсора",CursorRecenterIdleMs,value=>{CursorRecenterIdleMs=value;Save("cursorRecenterIdleMs",value);});
            void Add(string key,string title,float current,System.Action<float> changed)
            {
                var field=profile.metadata.Single(m=>m.path.EndsWith("."+key));
                var slider=new Slider(title,field.min,field.max){name="settings-pad-"+key,value=current};var label=OrbitalTheme.Text(current.ToString("0.##")+" "+field.unit,"orbital-muted");label.AddToClassList("settings-value");parent.Add(slider);parent.Add(label);
                slider.RegisterValueChangedCallback(evt=>{float value=field.min+Mathf.Round((evt.newValue-field.min)/field.step)*field.step;slider.SetValueWithoutNotify(value);changed(value);label.text=value.ToString("0.##")+" "+field.unit;});
                slider.userData=new System.Action<int>(direction=>slider.value=Mathf.Clamp(slider.value+direction*field.step,field.min,field.max));
            }
        }
    }
}
