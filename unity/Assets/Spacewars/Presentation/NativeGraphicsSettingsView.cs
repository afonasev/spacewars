using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    public static class NativeGraphicsSettingsView
    {
        public static void AddBasic(VisualElement content)
        {
            var parameter=NativeGraphicsSettings.Parameters.Single(p=>p.Path==NativeGraphicsSettings.AaPath);
            var aa=new DropdownField("Сглаживание",new List<string>{"Выключено","MSAA 4×","SMAA · высокое качество"},(int)NativeGraphicsSettings.Read(parameter)){name="settings-graphics-aa"};
            aa.RegisterValueChangedCallback(_=>NativeGraphicsSettings.Set(parameter.Path,aa.index));content.Add(aa);
            var ao=new Toggle("Контактные тени (SSAO)"){name="settings-graphics-ssao",value=NativeGraphicsSettings.OcclusionEnabled};ao.RegisterValueChangedCallback(e=>NativeGraphicsSettings.OcclusionEnabled=e.newValue);content.Add(ao);
            var bloom=new Toggle("Свечение огней (Bloom)"){name="settings-graphics-bloom",value=NativeGraphicsSettings.BloomEnabled};bloom.RegisterValueChangedCallback(e=>NativeGraphicsSettings.BloomEnabled=e.newValue);content.Add(bloom);
            var trails=new Toggle("Следы техники"){name="settings-graphics-trails",tooltip="Полностью скрывает существующие и новые следы всех юнитов. Выбор сохраняется.",value=NativeGraphicsSettings.GroundTrailsEnabled};trails.RegisterValueChangedCallback(e=>NativeGraphicsSettings.GroundTrailsEnabled=e.newValue);content.Add(trails);
            void Refresh(string path){trails.SetValueWithoutNotify(NativeGraphicsSettings.GroundTrailsEnabled);aa.SetValueWithoutNotify(aa.choices[(int)NativeGraphicsSettings.Read(parameter)]);ao.SetValueWithoutNotify(NativeGraphicsSettings.OcclusionEnabled);bloom.SetValueWithoutNotify(NativeGraphicsSettings.BloomEnabled);}
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
