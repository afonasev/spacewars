using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using UnityEngine;
namespace Spacewars.Presentation
{
    public static class NativeGraphicsSettingsView
    {
        public static void AddBasic(VisualElement content,VisualElement effects)
        {
            var parameter=NativeGraphicsSettings.Parameters.Single(p=>p.Path==NativeGraphicsSettings.AaPath);
            var aa=new DropdownField("Сглаживание",new List<string>{"Выключено","MSAA 4×","SMAA · высокое качество"},(int)NativeGraphicsSettings.Read(parameter)){name="settings-graphics-aa"};
            aa.RegisterValueChangedCallback(_=>NativeGraphicsSettings.Set(parameter.Path,aa.index));content.Add(aa);
            var scaleParameter=NativeGraphicsSettings.Parameters.Single(p=>p.Path==NativeGraphicsSettings.RenderScalePath);
            var scale=new Slider("Масштаб рендера (Render Scale)",scaleParameter.Minimum*100,scaleParameter.Maximum*100){name="settings-graphics-render-scale",value=NativeGraphicsSettings.RenderScale*100};
            var scaleValue=OrbitalTheme.Text(Mathf.RoundToInt(scale.value)+"%","orbital-muted");scaleValue.AddToClassList("settings-value");
            scale.RegisterValueChangedCallback(e=>{float v=Mathf.Round(e.newValue/(scaleParameter.Step*100))*scaleParameter.Step;scale.SetValueWithoutNotify(v*100);NativeGraphicsSettings.Set(scaleParameter.Path,v);scaleValue.text=Mathf.RoundToInt(v*100)+"%";});
            scale.userData=new Action<int>(direction=>scale.value=Mathf.Clamp(scale.value+direction*scaleParameter.Step*100,scale.lowValue,scale.highValue));content.Add(scale);content.Add(scaleValue);
            var ao=new Toggle("Контактные тени (SSAO)"){name="settings-graphics-ssao",value=NativeGraphicsSettings.OcclusionEnabled};ao.RegisterValueChangedCallback(e=>NativeGraphicsSettings.OcclusionEnabled=e.newValue);content.Add(ao);
            var bloom=new Toggle("Свечение огней (Bloom)"){name="settings-graphics-bloom",value=NativeGraphicsSettings.BloomEnabled};bloom.RegisterValueChangedCallback(e=>NativeGraphicsSettings.BloomEnabled=e.newValue);content.Add(bloom);
            var trails=new Toggle("Следы техники"){name="settings-graphics-trails",tooltip="Полностью скрывает существующие и новые следы всех юнитов. Выбор сохраняется.",value=NativeGraphicsSettings.GroundTrailsEnabled};trails.RegisterValueChangedCallback(e=>NativeGraphicsSettings.GroundTrailsEnabled=e.newValue);effects.Add(trails);
            var wrecks=new Toggle("Остовы юнитов"){name="settings-graphics-wrecks",tooltip="Полностью удаляет существующие остовы и их препятствия, запрещает новые. Повторное включение — только для будущих смертей.",value=NativeGraphicsSettings.UnitWrecksEnabled};wrecks.RegisterValueChangedCallback(e=>NativeGraphicsSettings.UnitWrecksEnabled=e.newValue);effects.Add(wrecks);
            void Refresh(string path){scale.SetValueWithoutNotify(NativeGraphicsSettings.RenderScale*100);scaleValue.text=Mathf.RoundToInt(scale.value)+"%";wrecks.SetValueWithoutNotify(NativeGraphicsSettings.UnitWrecksEnabled);trails.SetValueWithoutNotify(NativeGraphicsSettings.GroundTrailsEnabled);aa.SetValueWithoutNotify(aa.choices[(int)NativeGraphicsSettings.Read(parameter)]);ao.SetValueWithoutNotify(NativeGraphicsSettings.OcclusionEnabled);bloom.SetValueWithoutNotify(NativeGraphicsSettings.BloomEnabled);}
            Bind(content,Refresh);
        }
        private static void Bind(VisualElement host,Action<string> refresh)
        {
            host.RegisterCallback<AttachToPanelEvent>(_=>NativeGraphicsSettings.Changed+=refresh);
            host.RegisterCallback<DetachFromPanelEvent>(_=>NativeGraphicsSettings.Changed-=refresh);
            if(host.panel!=null)NativeGraphicsSettings.Changed+=refresh;
        }
    }
}
