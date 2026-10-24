using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Spacewars.Presentation
{
    [Serializable] public sealed class HawkWeaponPreviewProfile
    {
        public string id = "hawk-weapon-preview-v1";
        public int revision = 1;
        [SurfaceSetting("Оружие", "Подготовка сверх танковой", "Добавляется к подготовке танка после остановки.", "с", .05f, 5, .05f)]
        public float preparationExtraSeconds = .8f;
        [SurfaceSetting("Оружие", "Интервал ракет", "Задержка между левым и правым пуском одного залпа.", "с", .05f, 1, .01f)]
        public float shotIntervalSeconds = .18f;
        [SurfaceSetting("Оружие", "Перезарядка", "Пауза после второго пуска либо отмены второй ракеты.", "с", .1f, 10, .1f)]
        public float reloadSeconds = 2.4f;
        [SurfaceSetting("Ракета", "Скорость", "Скорость полёта ракеты.", "м/с", 1, 60, .5f)]
        public float speed = 14;
        [SurfaceSetting("Ракета", "Доворачивание", "Максимальная скорость изменения направления.", "°/с", 0, 90, 1)]
        public float turnDegreesPerSecond = 18;
        [SurfaceSetting("Ракета", "Время жизни", "Ракета исчезает после этого времени полёта.", "с", .2f, 15, .1f)]
        public float lifetimeSeconds = 4;
        [SurfaceSetting("Ракета", "Урон", "Урон одного прямого попадания; без площади поражения.", "HP", 1, 200, 1)]
        public float damage = 20;
        [SurfaceSetting("Ракета", "Радиус столкновения", "Физический радиус проверки попадания.", "м", .01f, .5f, .01f)]
        public float collisionRadius = .07f;
        [SurfaceSetting("Вид", "Длина ракеты", "Размер видимой ракеты.", "м", .2f, 1.5f, .05f)]
        public float rocketLength = .7f;
        [SurfaceSetting("Вид", "Длина факела", "Видимая длина пламени двигателя.", "м", .1f, 2, .05f)]
        public float exhaustLength = .65f;
        [SurfaceSetting("Вид", "Ширина следа", "Толщина дымного следа.", "м", .01f, .3f, .01f)]
        public float trailWidth = .07f;
        [SurfaceSetting("Вид", "Длительность следа", "Время сохранения дымного следа.", "с", .1f, 2, .05f)]
        public float trailSeconds = .65f;
        [SurfaceSetting("Вид", "Радиус вспышки", "Размер эффекта прямого попадания, не радиус урона.", "м", .1f, 2, .05f)]
        public float impactRadius = .7f;
        [SurfaceSetting("Сцена", "Здоровье мишени", "Запас здоровья новой мишени.", "HP", 1, 2000, 1)]
        public float targetHealth = 240;
        [SurfaceSetting("Сцена", "Движение мишени", "Скорость бокового перемещения включённой движущейся мишени.", "м/с", .1f, 8, .1f)]
        public float targetSpeed = 1.6f;
        [SurfaceSetting("Сцена", "Высота зависания", "Высота модели над площадкой в проверочной сцене.", "м", 1, 6, .1f)]
        public float hoverHeight = 2;
        [SurfaceSetting("Сцена", "Дистанция мишени", "Продольная дистанция до центра мишени.", "м", 5, 30, .5f)]
        public float targetDistance = 12;
        [SurfaceSetting("Сцена", "Камера", "Вертикальный полуразмер ортографического обзора.", "м", 6, 24, .5f)]
        public float cameraSize = 9;
        [SurfaceSetting("Вид", "Основной винт", "Скорость вращения основного винта.", "°/с", 90, 2400, 30)]
        public float rotorDegreesPerSecond = 900;
        [SurfaceSetting("Вид", "Ядро факела", "Относительный размер светлого ядра факела.", "доля", 0.1f, 1f, 0.05f)]
        public float exhaustCore = 0.5f;
        [SurfaceSetting("Вид", "Яркость факела", "Прозрачность пламени двигателя.", "доля", 0.1f, 1f, 0.05f)]
        public float exhaustOpacity = 0.9f;
        [SurfaceSetting("Вид", "Длительность вспышки", "Время сохранения эффекта попадания.", "с", 0.1f, 2f, 0.05f)]
        public float impactSeconds = 0.6f;
        [SurfaceSetting("Вид", "Толщина искр", "Ширина искр попадания.", "м", 0.01f, 0.2f, 0.01f)]
        public float impactSparkWidth = 0.03f;
        [SurfaceSetting("Вид", "Свечение попадания", "Интенсивность свечения попадания.", "доля", 0f, 2f, 0.05f)]
        public float impactGlow = 1f;
        [SurfaceSetting("Вид", "Дым попадания", "Насыщенность дыма попадания.", "доля", 0f, 1f, 0.05f)]
        public float impactSmoke = 0.7f;
        [SurfaceSetting("Сцена", "Радиус мишени", "Видимый и физический радиус мишени.", "м", 0.2f, 2f, 0.1f)]
        public float targetRadius = 0.9f;
        [SurfaceSetting("Сцена", "Высота мишени", "Высота центра мишени; можно проверить воздушную цель.", "м", 1f, 6f, 0.1f)]
        public float targetHeight = 1f;
        [SurfaceSetting("Сцена", "Размах движения", "Максимальное боковое отклонение мишени.", "м", 1f, 6f, 0.5f)]
        public float targetTravel = 3f;
        public IEnumerable<SurfaceParameter> Parameters()
        {
            foreach (var field in GetType().GetFields())
            {
                var a = field.GetCustomAttribute<SurfaceSettingAttribute>();
                if (a != null) yield return new SurfaceParameter("preview.hawkWeapon." + field.Name, a.Group, a.Label, a.Description, a.Unit,
                    a.Minimum, a.Maximum, a.Step, () => (float)field.GetValue(this), v => field.SetValue(this, v));
            }
        }
        public void Validate()
        {
            if (id != "hawk-weapon-preview-v1" || revision < 1) throw new ArgumentException("Invalid Hawk preview profile identity");
            foreach (var p in Parameters())
                if (float.IsNaN(p.Read()) || float.IsInfinity(p.Read()) || p.Read() < p.Minimum || p.Read() > p.Maximum)
                    throw new ArgumentException("Invalid Hawk setting: " + p.Path);
        }
        public static HawkWeaponPreviewProfile Load()
        {
            var text = Resources.Load<TextAsset>("HawkWeaponPreviewProfile");
            if (!text) throw new InvalidOperationException("Missing HawkWeaponPreviewProfile");
            var profile = JsonUtility.FromJson<HawkWeaponPreviewProfile>(text.text); profile.Validate(); return profile;
        }
    }
}
