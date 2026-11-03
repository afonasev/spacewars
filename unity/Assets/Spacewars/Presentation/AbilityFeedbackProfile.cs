using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class AbilityFeedbackProfile
    {
        public string id="orbital-ability-feedback-v1";public int revision=1;
        [SurfaceSetting("Способности","Длительность","Время импульса после успешного применения.","с",.2f,3,.05f)] public float duration=1.15f;
        [SurfaceSetting("Способности","Радиус установки","Внешний размер визуального импульса установки; не радиус урона.","м",.2f,2,.05f)] public float mineRadius=.85f;
        [SurfaceSetting("Способности","Начальный масштаб","Доля внешнего радиуса в начале импульса.","доля",.1f,1,.05f)] public float startScale=.8f;
        [SurfaceSetting("Способности","Масштаб захвата","Размер импульса относительно корпуса захваченного объекта.","×",1,3,.1f)] public float hackRadiusScale=2.8f;
        [SurfaceSetting("Способности","Яркость","Прозрачность энергетической текстуры и искр.","доля",.1f,1,.05f)] public float opacity=.7f;
        [SurfaceSetting("Способности","Свечение","Яркость энергетического импульса и мягких световых дорожек.","×",.5f,3,.1f)] public float emissionGain=1.8f;
        [SurfaceSetting("Способности","Отступ от земли","Защита импульса от мерцания поверхности.","м",.01f,.15f,.01f)] public float groundLift=.04f;
        [SurfaceSetting("Способности","Высота искр","Высота коротких световых дорожек над корпусом.","м",.1f,2,.05f)] public float sparkHeight=.85f;
        [SurfaceSetting("Способности","Ширина искр","Толщина световых дорожек.","м",.005f,.08f,.005f)] public float sparkWidth=.035f;
        [SurfaceSetting("Способности","Длина искры","Длина одной световой дорожки.","м",.05f,.5f,.01f)] public float sparkLength=.2f;
        [SurfaceSetting("Способности","Число искр","Количество дорожек одного эффекта.","шт",3,16,1)] public float sparkCount=8;
        [SurfaceSetting("Способности","Лимит эффектов","Предел одновременных визуальных импульсов одного локального экрана.","шт",1,24,1)] public float maxEffects=12;
        public IEnumerable<SurfaceParameter> Parameters(){foreach(var f in GetType().GetFields()){var a=f.GetCustomAttribute<SurfaceSettingAttribute>();if(a!=null)yield return new SurfaceParameter("visual.abilityFeedback."+f.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)f.GetValue(this),v=>f.SetValue(this,v));}}
        public void Validate(){if(id!="orbital-ability-feedback-v1"||revision<1)throw new ArgumentException("Invalid ability feedback identity");foreach(var p in Parameters()){float v=p.Read();if(float.IsNaN(v)||float.IsInfinity(v)||v<p.Minimum||v>p.Maximum)throw new ArgumentException(p.Path);}if(sparkCount!=Mathf.Round(sparkCount)||maxEffects!=Mathf.Round(maxEffects))throw new ArgumentException("Effect counts must be integers");}
        public static AbilityFeedbackProfile Load(){var p=JsonUtility.FromJson<AbilityFeedbackProfile>(Resources.Load<TextAsset>("AbilityFeedbackProfile").text);p.Validate();return p;}
    }
}
