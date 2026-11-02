using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Spacewars.Presentation
{
    [Serializable] public sealed class RadialCardProfile
    {
        public string id="radial-card-a-v1";
        public int revision=14;
        [SurfaceSetting("Компактный HUD","Ширина консоли здания","Максимальная ширина нижней консоли здания.","px",300,520,10)] public float dockWidth=420;
        [SurfaceSetting("Компактный HUD","Портрет здания","Размер портрета в компактной шапке.","px",24,64,2)] public float dockPortraitSize=38;
        [SurfaceSetting("Компактный HUD","Высота заказа","Высота прямоугольных ячеек очереди.","px",32,64,2)] public float dockOrderHeight=44;
        [SurfaceSetting("Компактный HUD","Активный заказ","Ширина активного заказа с временем и прогрессом.","px",120,200,2)] public float dockActiveWidth=148;
        [SurfaceSetting("Компактный HUD","Карточка юнита","Максимальная ширина таблицы выбранного юнита.","px",280,420,10)] public float selectionWidth=320;
        [SurfaceSetting("Компактный HUD","Текст характеристик","Размер текста компактных характеристик и счётчиков.","px",9,14,1)] public float selectionFont=10;
        [SurfaceSetting("Компактный HUD","Строка характеристик","Минимальная высота строки характеристик.","px",16,28,1)] public float selectionRowHeight=19;
        [SurfaceSetting("Компактный HUD","Портрет состава","Размер портретов в составе группы.","px",20,40,2)] public float selectionIconSize=28;
        [SurfaceSetting("Компактный HUD","Ширина личного портрета","Ширина иконки отдельного юнита в раскрытом списке.","px",22,40,2)] public float selectionTileWidth=26;
        [SurfaceSetting("Компактный HUD","Высота личного портрета","Высота иконки вместе с личным здоровьем.","px",20,36,2)] public float selectionTileHeight=24;
        [SurfaceSetting("Компактный HUD","Колонки списка","Максимальное число юнитов в строке подробного состава.","шт.",5,12,1)] public float selectionColumns=10;
        [SurfaceSetting("Компактный HUD","Интервал элементов","Промежуток между компактными элементами.","px",2,8,1)] public float selectionGap=3;
        [SurfaceSetting("Компактный HUD","Отступ консоли","Внутренние отступы нижней консоли.","px",6,18,1)] public float dockPadding=10;

        [SurfaceSetting("Служебные иконки","Размер","Желаемый размер ремонта, продажи и точки сбора; ограничивается свободным местом лепестка.","px",24,48,2)] public float utilityIconSize=36;
        [SurfaceSetting("Служебные иконки","Яркость","Яркость предметных значков рядом с миниатюрами юнитов и зданий.","доля",.4f,1,.02f)] public float utilityIconBrightness=.76f;
        [SurfaceSetting("Улучшения","Миниатюры","Желаемая высота миниатюр улучшений; ограничивается свободным местом лепестка.","px",40,90,2)] public float upgradePortraitSize=70;
        [SurfaceSetting("Улучшения","Яркость значков","Яркость цветных иконок оружия, защиты и бетона рядом с миниатюрами.","доля",.4f,1,.02f)] public float researchIconBrightness=.76f;
        [SurfaceSetting("Улучшения","Значок улучшения","Размер двух шевронов относительно миниатюры улучшенного здания или юнита.","доля",.16f,.34f,.02f)] public float buildingUpgradeBadgeSize=.22f;
        [SurfaceSetting("Улучшения","Номер уровня","Размер номера уровня относительно высоты значка.","доля",.18f,.4f,.02f)] public float researchRankSize=.28f;
        [SurfaceSetting("Группы","Минимум секторов","Минимальное число секторов пустого или небольшого колеса групп.","шт.",3,9,1)] public float minimumGroupSlots=3;
        [SurfaceSetting("Группы","Свободные сектора","Сколько свободных номеров добавлять к занятым группам в пределах девяти.","шт.",1,3,1)] public float spareGroupSlots=2;
        [SurfaceSetting("Оптический круг B","Светлота поверхности","Подмешивание холодного оттенка рамки в непрозрачную поверхность лепестков.","доля",0,.4f,.01f)] public float opticalSurfaceLift=.18f;
        [SurfaceSetting("Оптический круг B","Заливка выбора","Подмешивание бирюзового оттенка в выбранный лепесток; янтарный автозаказ сохраняется.","доля",0,.25f,.01f)] public float opticalSelectionTint=.12f;
        [SurfaceSetting("Оптический круг B","Глубина поверхности","Мягкое осветление оптической поверхности к наружному краю; не окрашивает янтарные состояния.","доля",0,.12f,.01f)] public float opticalSheen=.06f;
        [SurfaceSetting("Оптический круг B","Сила блика","Прозрачность тонкого верхнего блика оптической поверхности.","доля",0,.4f,.01f)] public float opticalEdgeLight=.18f;
        [SurfaceSetting("Оптический круг B","Глубина края","Внутренний отступ блика от границы; не меняет геометрию выбора.","px",.5f,3,.1f)] public float opticalEdgeInset=1.5f;
        [SurfaceSetting("Оптический круг B","Сила свечения","Прозрачность мягкой каймы выбранного сектора; дуга производства остаётся поверх.","доля",0,.2f,.01f)] public float opticalGlowOpacity=.08f;
        [SurfaceSetting("Оптический круг B","Ширина свечения","Ширина мягкой каймы под основным контуром выбора.","px",2,6,.5f)] public float opticalGlowWidth=4;
        [SurfaceSetting("Колёса действий","Разделитель","Постоянная ширина промежутка между лепестками по всему радиусу.","px",2,6,1)] public float sectorGap=4;
        [SurfaceSetting("Колёса действий","Скругление лепестка","Плавность углов у внутренней и внешней дуги.","px",2,8,1)] public float petalCorner=5;
        [SurfaceSetting("Колёса действий","Значок центра","Размер значка над центральной подписью.","px",24,40,2)] public float centreIconSize=32;
        [SurfaceSetting("Колёса действий","Подпись центра","Размер центральной подписи.","px",10,14,1)] public float centreTextSize=11;
        [SurfaceSetting("Колёса действий","Интервал подписей","Расстояние между значком/номером и подписью.","px",2,8,1)] public float labelGap=4;
        [SurfaceSetting("Строительное колесо","Портрет","Высота портрета в композиции B при эталонном диаметре 360 px; масштабируется вместе с колесом.","px",60,90,2)] public float constructionPortraitSize=78;
        [SurfaceSetting("Строительное колесо","Подписи","Размер имени и цены в композиции B при эталонном диаметре 360 px.","px",12,16,1)] public float constructionTextSize=13;
        [SurfaceSetting("Карточки действий","Минимальный круг","Нижняя граница диаметра при нехватке ширины окна.","px",160,220,10)] public float minimumWheelSize=160;
        [SurfaceSetting("Карточки действий","Одиночная база","Диаметр диска единственной базы.","px",140,220,10)] public float singleBaseSize=180;
        [SurfaceSetting("Карточки действий","Номер группы","Размер номера в секторе группы.","px",18,28,1)] public float groupNumberSize=24;
        [SurfaceSetting("Карточки действий","Портрет состава","Высота портрета типа юнитов в составе группы.","px",40,64,2)] public float compositionSize=48;
        [SurfaceSetting("Карточка A","Светлота поверхности","Холодный оттенок непрозрачной подложки карточки.","доля",0,.3f,.01f)] public float cardSurfaceLift=.1f;
        [SurfaceSetting("Карточка A","Голубой контур","Сила голубого оттенка тонкой рамки карточки.","доля",0,.7f,.01f)] public float cardRimTint=.4f;
        [SurfaceSetting("Карточка A","Акцент заголовка","Ширина голубого штриха перед названием.","px",2,6,1)] public float titleAccentWidth=3;
        [SurfaceSetting("Карточка A","Пиктограммы","Размер значков здоровья, постройки и блокировки.","px",16,28,2)] public float statIconSize=22;
        [SurfaceSetting("Карточка A","Парные значения","Минимальная высота строки здоровья и времени постройки.","px",36,56,2)] public float featuredStatHeight=44;
        [SurfaceSetting("Карточка A","Отступ предупреждения","Внутренние отступы и верхний зазор плашки условий.","px",4,12,1)] public float notePadding=8;
        [SurfaceSetting("Карточка A","Заливка предупреждения","Примесь янтарного оттенка к непрозрачной плашке условий и последствий.","доля",.04f,.24f,.01f)] public float noteTint=.12f;
        [SurfaceSetting("Карточки действий","Ширина","Предпочтительная ширина; ограничена доступным местом рядом с колесом.","px",310,420,10)] public float width=380;
        [SurfaceSetting("Карточки действий","Внутренний отступ","Свободное пространство между рамкой и содержимым.","px",12,24,1)] public float padding=16;
        [SurfaceSetting("Карточки действий","Заголовок","Размер названия выбранного действия.","px",18,28,1)] public float titleSize=24;
        [SurfaceSetting("Карточки действий","Основной текст","Размер описания, подписей и примечаний.","px",12,16,1)] public float textSize=13;
        [SurfaceSetting("Карточки действий","Значения","Размер значений характеристик и цены.","px",14,20,1)] public float valueSize=16;
        [SurfaceSetting("Карточки действий","Отступ от разделителя","Отступ текста и значений от вертикального разделителя таблицы.","px",4,20,1)] public float statColumnPadding=10;
        [SurfaceSetting("Портреты башен","Поворот","Ракурс спереди в три четверти; применяется при Bake defense portraits.","°",0,60,1)] public float defensePortraitYaw=25;
        [SurfaceSetting("Портреты юнитов","Яркость","Умножение яркости при Bake unit portraits; не меняет модели на карте.","×",.5f,2,.05f)] public float unitPortraitBrightness=1.5f;
        [SurfaceSetting("Портреты юнитов","Яркость танка","Отдельное осветление всех вариантов танка при Bake unit portraits; сохраняет тёмный корпус.","×",.5f,2,.05f)] public float tankPortraitBrightness=1.2f;
        [SurfaceSetting("Портреты юнитов","Насыщенность","Насыщенность при Bake unit portraits; сохраняет прозрачность и контур.","×",0,1.5f,.01f)] public float unitPortraitSaturation=1.06f;
        [SurfaceSetting("Портреты башен","Высота ракурса","Наклон сверху; применяется при Bake defense portraits, не меняет камеру матча.","°",15,60,1)] public float defensePortraitElevation=35;
        [SurfaceSetting("Карточки действий","Строка характеристик","Минимальная высота строки; длинный текст переносится.","px",30,44,1)] public float statHeight=34;
        [SurfaceSetting("Карточки действий","Строка управления","Высота подсказки кнопки геймпада или мыши.","px",32,44,1)] public float actionHeight=36;
        public IEnumerable<SurfaceParameter> Parameters()
        {
            foreach(var field in GetType().GetFields())
            {
                var m=field.GetCustomAttribute<SurfaceSettingAttribute>();if(m==null)continue;
                yield return new SurfaceParameter("visual.radialCard."+field.Name,m.Group,m.Label,m.Description,m.Unit,m.Minimum,m.Maximum,m.Step,()=> (float)field.GetValue(this),v=>field.SetValue(this,v));
            }
        }
        public void Validate()
        {
            if(id!="radial-card-a-v1"||revision<1)throw new ArgumentException("Invalid radial card profile identity");
            foreach(var p in Parameters()){float v=p.Read();if(float.IsNaN(v)||float.IsInfinity(v)||v<p.Minimum||v>p.Maximum||Mathf.Abs(v/p.Step-Mathf.Round(v/p.Step))>.001f)throw new ArgumentException("Invalid radial card parameter: "+p.Path);}
        }
        public static RadialCardProfile Load()
        {
            var asset=Resources.Load<TextAsset>("RadialCardProfile");if(!asset)throw new InvalidOperationException("Missing radial card profile");
            var profile=JsonUtility.FromJson<RadialCardProfile>(asset.text);profile.Validate();return profile;
        }
    }
}
