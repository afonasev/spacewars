using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class HawkVisualProfile
    {
        public string id="hawk-visual-v2"; public int revision=2;
        [SurfaceSetting("Ястреб", "Отклонение сопел", "Предел визуального отклонения сопел при крене и тангаже.", "°",0,35,1)] public float nozzleAngle=18;
        [SurfaceSetting("Ястреб", "Реакция сопел", "Связь направления сопел с существующим наклоном корпуса.", "ratio",0,3,.1f)] public float nozzleResponse=1.1f;
        [SurfaceSetting("Ястреб", "Ход крышки", "Визуальный ход крышки кассеты при подготовке следующей ракеты.", "м",0,.25f,.01f)] public float reloadTravel=.12f;
        [SurfaceSetting("Ястреб", "Цикл механизма", "Длительность реакции механизма на фактический пуск. Не меняет перезарядку оружия.", "с",.1f,1,.05f)] public float mechanismSeconds=.45f;
        [SurfaceSetting("Ястреб", "Пульс пуска", "Длительность короткой вспышки у сопла при фактическом пуске.", "с",.02f,.3f,.01f)] public float launchSeconds=.09f;
        [SurfaceSetting("Ястреб", "Размер вспышки", "Размер короткой вспышки, не закрывающей корпус.", "м",.01f,.4f,.01f)] public float launchSize=.15f;
        [SurfaceSetting("Ястреб", "Длина выхлопа", "Длина ракетного выхлопа относительно длины ракеты.", "ratio",.2f,2,.05f)] public float exhaustLength=.75f;
        [SurfaceSetting("Ястреб", "Толщина выхлопа", "Толщина ракетного выхлопа относительно радиуса ракеты.", "ratio",.2f,2,.05f)] public float exhaustWidth=.8f;
        [SurfaceSetting("Ястреб", "Непрозрачность выхлопа", "Яркость аддитивного выхлопа ракеты; меньшие значения сохраняют видимый контур корпуса.", "opacity",0,1,.05f)] public float exhaustOpacity=.3f;
        public IEnumerable<SurfaceParameter> Parameters(){foreach(var f in GetType().GetFields()){var a=f.GetCustomAttribute<SurfaceSettingAttribute>();if(a!=null)yield return new SurfaceParameter("visual.hawk."+f.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=>(float)f.GetValue(this),v=>f.SetValue(this,v));}}
        public void Validate(){if(id!="hawk-visual-v2"||revision<1)throw new ArgumentException("Hawk visual identity");foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentOutOfRangeException(p.Path);}
        public static HawkVisualProfile Load(){var a=Resources.Load<TextAsset>("HawkVisualProfile");if(!a)throw new InvalidOperationException("Missing Hawk visual profile");var p=JsonUtility.FromJson<HawkVisualProfile>(a.text);p.Validate();return p;}
    }
}
