using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Spacewars.Presentation
{
    [Serializable] public sealed class ModelDetailProfile
    {
        private static ModelDetailProfile session;
        public static ModelDetailProfile Current=>session??=Load();
        public ModelDetailProfile Copy(){var copy=JsonUtility.FromJson<ModelDetailProfile>(JsonUtility.ToJson(this));copy.Validate();return copy;}
        public static void UseSession(ModelDetailProfile profile){session=profile?.Copy();}
        public string id="model-detail-v1"; public int revision=5;
        [SurfaceSetting("Детализация (LOD)","Исходная → обычная","При уменьшении модели ниже этого размера включается обычный LOD.","screen height",.02f,.3f,.005f)] public float normalEnter=.10f;
        [SurfaceSetting("Детализация (LOD)","Обычная → исходная","При увеличении модели выше этого размера возвращается исходная геометрия; порог выше перехода в обычную.","screen height",.025f,.4f,.005f)] public float normalExit=.12f;
        [SurfaceSetting("Детализация (LOD)","Обычная → дальняя","При уменьшении модели ниже этого размера включается дальний LOD.","screen height",.005f,.1f,.005f)] public float farEnter=.055f;
        [SurfaceSetting("Детализация (LOD)","Дальняя → обычная","При увеличении модели выше этого размера возвращается обычный LOD; порог ниже перехода в обычную из исходной.","screen height",.01f,.15f,.005f)] public float farExit=.060f;
        [SurfaceSetting("Следы техники","Средняя группа","Число видимых движущихся наземных машин, с которого следы оставляет только часть техники.","юн.",1,512,1)] public float trailCrowdThreshold=20;
        [SurfaceSetting("Следы техники","Большая группа","Порог большой группы с меньшим числом машин, оставляющих следы.","юн.",1,512,1)] public float trailLargeCrowdThreshold=40;
        [SurfaceSetting("Следы техники","Каждая N-я машина","Примерная доля машин со сплошными следами в средней группе: 2 — половина, 1 — все.","раз",1,16,1)] public float trailCrowdDivisor=2;
        [SurfaceSetting("Следы техники","Большая группа: каждая N-я","Примерная доля машин со сплошными следами в большой группе: 4 — четверть, 1 — все.","раз",1,16,1)] public float trailLargeCrowdDivisor=4;
        [SurfaceSetting("Следы техники","Скрывать при отдалении","Все следы скрываются, когда полувысота обзора ортографической камеры достигает порога; при приближении возвращаются только неистёкшие следы.","м",5,100,1)] public float trailHideZoom=18;
        [SurfaceSetting("Полоски здоровья","Скрывать при отдалении","При включённой пользовательской опции полоски скрываются, когда полувысота обзора достигает порога. Для перспективы используется полувысота на глубине объекта.","м",5,100,1)] public float healthHideZoom=20;
        public IEnumerable<SurfaceParameter> Parameters(){foreach(var f in GetType().GetFields()){var a=f.GetCustomAttribute<SurfaceSettingAttribute>();if(a!=null)yield return new SurfaceParameter("visual.modelDetail."+f.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)f.GetValue(this),v=>f.SetValue(this,v));}}
        public void Validate(){if(id!="model-detail-v1"||revision<1)throw new ArgumentException("Model detail identity");foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum||p.Unit!="screen height"&&p.Read()!=Math.Floor(p.Read()))throw new ArgumentException(p.Path);if(trailLargeCrowdThreshold<trailCrowdThreshold)throw new ArgumentException("Порог большой группы не может быть меньше средней.");if(!(farEnter<farExit&&farExit<normalEnter&&normalEnter<normalExit))throw new ArgumentException("Порядок LOD: обычная→дальняя < дальняя→обычная < исходная→обычная < обычная→исходная.");}
        public static ModelDetailProfile Load(){var p=JsonUtility.FromJson<ModelDetailProfile>(Resources.Load<TextAsset>("ModelDetailProfile").text);p.Validate();return p;}
    }
}
