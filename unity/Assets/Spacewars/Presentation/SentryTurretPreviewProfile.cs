using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Spacewars.Presentation
{
    [Serializable] public sealed class SentryTurretPreviewProfile
    {
        public string id = "sentry-turret-preview-v1";
        public int revision = 1;
        [SurfaceSetting("Наведение", "Поворот башни", "Предельная скорость поворота по азимуту.", "°/с", 30, 1440, 30)]
        public float yawDegreesPerSecond = 360;
        [SurfaceSetting("Наведение", "Подъём оружия", "Предельная скорость наведения по высоте; верхний предел — вертикаль.", "°/с", 30, 720, 15)]
        public float pitchDegreesPerSecond = 180;
        [SurfaceSetting("Наведение", "Склонение", "Нижний предел для наземных целей рядом с установкой.", "°", 0, 45, 1)]
        public float depressionDegrees = 20;
        [SurfaceSetting("Оружие", "Разброс при повороте", "Множитель разброса улучшенного Исследователя при движении азимута или подъёма.", "×", 1, 2, .05f)]
        public float rotatingSpreadMultiplier = 1.3f;
        [SurfaceSetting("Вид", "Скорость трассера", "Скорость видимого трассера в проверочной сцене.", "м/с", 5, 200, 5)]
        public float tracerSpeed = 40;
        [SurfaceSetting("Вид", "Длина трассера", "Длина светящегося отрезка выстрела.", "м", .05f, 2, .05f)]
        public float tracerLength = .65f;
        [SurfaceSetting("Вид", "Толщина трассера", "Толщина видимого отрезка, не радиус урона.", "м", .005f, .1f, .005f)]
        public float tracerWidth = .025f;
        [SurfaceSetting("Вид", "Вспышка", "Длительность вспышки на текущем дульном срезе.", "с", .01f, .2f, .01f)]
        public float flashSeconds = .06f;
        [SurfaceSetting("Сцена", "Дистанция мишени", "Радиус движения мишени вокруг турели в превью.", "м", 2, 12, .5f)]
        public float targetDistance = 5;
        [SurfaceSetting("Сцена", "Скорость облёта", "Угловая скорость движущейся мишени в демонстрации.", "°/с", 5, 300, 5)]
        public float targetDegreesPerSecond = 65;
        [SurfaceSetting("Сцена", "Высота воздушной цели", "Высота над осью подъёма для воздушной и вертикальной мишени.", "м", 1, 8, .5f)]
        public float targetHeight = 3;
        [SurfaceSetting("Сцена", "Камера", "Вертикальный полуразмер общего вида базы.", "м", 4, 15, .5f)]
        public float cameraSize = 7;
        public IEnumerable<SurfaceParameter> Parameters()
        {
            foreach (var field in GetType().GetFields())
            {
                var a = field.GetCustomAttribute<SurfaceSettingAttribute>();
                if (a != null) yield return new SurfaceParameter("preview.sentryTurret." + field.Name, a.Group, a.Label, a.Description, a.Unit,
                    a.Minimum, a.Maximum, a.Step, () => (float)field.GetValue(this), v => field.SetValue(this, v));
            }
        }
        public void Validate()
        {
            if (id != "sentry-turret-preview-v1" || revision < 1) throw new ArgumentException("Invalid sentry preview profile identity");
            foreach (var p in Parameters())
                if (!float.IsFinite(p.Read()) || p.Read() < p.Minimum || p.Read() > p.Maximum)
                    throw new ArgumentException("Invalid turret setting: " + p.Path);
        }
        public static SentryTurretPreviewProfile Load()
        {
            var asset = Resources.Load<TextAsset>("SentryTurretPreviewProfile");
            if (!asset) throw new InvalidOperationException("Missing SentryTurretPreviewProfile");
            var result = JsonUtility.FromJson<SentryTurretPreviewProfile>(asset.text); result.Validate(); return result;
        }
    }
}
