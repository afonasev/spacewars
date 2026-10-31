using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
 [Serializable] public sealed class ShkvalArtProfile
 {
  [SurfaceSetting("Модель", "Частота фактуры", "Частота UV окрашенного металла; применяется при экспорте исходных моделей.", "1/м",.5f,8,.25f)] public float sourceTextureRepeats=4;
  public string id="shkval-monolith-v1";public int revision=1;
  [SurfaceSetting("Пуск", "Ход установки", "Короткое движение крепления после пуска, только визуальное.", "м",0,.15f,.005f)] public float launchTravel=.045f;
  [SurfaceSetting("Пуск", "Возврат установки", "Время затухания движения крепления после пуска.", "с",.1f,1,.05f)] public float settleSeconds=.35f;
  [SurfaceSetting("Отдача корпуса", "Ход назад", "Смещение видимого корпуса против направления пуска; положение юнита и физика не меняются.", "м",0,.1f,.005f)] public float hullRecoilTravel=.025f;
  [SurfaceSetting("Отдача корпуса", "Рывок", "Время плавного короткого рывка корпуса после выстрела по игровым таймерам.", "с",.02f,.15f,.005f)] public float hullRecoilAttackSeconds=.045f;
  [SurfaceSetting("Отдача корпуса", "Возврат", "Время плавного возвращения корпуса в исходную позу после рывка.", "с",.1f,.6f,.01f)] public float hullRecoilReturnSeconds=.22f;
  [SurfaceSetting("Ракета", "Пульсация выхлопа", "Относительное изменение длины выхлопа.", "доля",0,.3f,.01f)] public float flamePulse=.14f;
  [SurfaceSetting("Ракета", "Частота выхлопа", "Частота пульсации по времени полёта ракеты.", "Гц",1,24,1)] public float flameFrequency=14;
  [SurfaceSetting("Пуск", "Сопровождение выхлопа", "Время сопровождения плоскости среза дула при отсечении выхлопа; полёт не изменяется.", "с",.1f,.6f,.025f)] public float exhaustMuzzleFollowSeconds=.3f;
  [SurfaceSetting("Ракета", "Масштаб ракеты", "Визуальный размер заряженной и летящей ракеты; контакт не меняется.", "доля",.3f,1,.05f)] public float rocketScale=.6f;
  [SurfaceSetting("Пуск", "Ход подачи", "Глубина появления следующей ракеты при заряжании.", "м",.02f,.3f,.01f)] public float loadingTravel=.12f;
  public IEnumerable<SurfaceParameter> Parameters(){foreach(var f in GetType().GetFields()){var a=f.GetCustomAttribute<SurfaceSettingAttribute>();if(a!=null)yield return new SurfaceParameter("visual.shkval."+f.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)f.GetValue(this),v=>f.SetValue(this,v));}}
  public void Validate(){if(id!="shkval-monolith-v1"||revision<1)throw new ArgumentException("Invalid Shkval art identity");foreach(var p in Parameters())if(!float.IsFinite(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException(p.Path);}
  public static ShkvalArtProfile Load(){var t=Resources.Load<TextAsset>("ShkvalArtProfile");if(!t)throw new InvalidOperationException("Missing Shkval art profile");var p=JsonUtility.FromJson<ShkvalArtProfile>(t.text);p.Validate();return p;}
 }
}
