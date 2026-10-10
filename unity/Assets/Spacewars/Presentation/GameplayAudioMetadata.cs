using System;
using System.Reflection;

namespace Spacewars.Presentation
{
    public sealed class GameplayAudioField
    {
        public readonly string Path,Label,Description,Unit;
        public readonly double Minimum,Maximum,Step;
        private readonly FieldInfo field;
        public GameplayAudioField(string key,string label,string description,string unit,double minimum,double maximum,double step)
        {Path="audio."+key;Label=label;Description=description;Unit=unit;Minimum=minimum;Maximum=maximum;Step=step;field=typeof(GameplayAudioProfile).GetField(key);}
        public double Read(GameplayAudioProfile p)=>Convert.ToDouble(field.GetValue(p));
        public void Write(GameplayAudioProfile p,double value)
        {
            Validate(value);value=Minimum+Math.Round((Math.Max(Minimum,Math.Min(Maximum,value))-Minimum)/Step)*Step;
            field.SetValue(p,Convert.ChangeType(value,field.FieldType));
        }
        public void Validate(double v)
        {if(double.IsNaN(v)||double.IsInfinity(v)||v<Minimum-Step*1e-5||v>Maximum+Step*1e-5)throw new ArgumentOutOfRangeException(Path);}
    }
    // Separate named presentation profile in Balance Lab; never extends the world-wire field order.
    public static class GameplayAudioMetadata
    {
        public static readonly GameplayAudioField[] Fields={
            new GameplayAudioField("near","Ближняя зона","Расстояние полного уровня от точки обзора камеры.","м",0,30,1),
            new GameplayAudioField("far","Дальность слышимости","На этом расстоянии эффект плавно исчезает.","м",31,160,1),
            new GameplayAudioField("mergeDistance","Объединение повторов","Размер области отбора похожих звуков.","м",1,30,1),
            new GameplayAudioField("repeatSeconds","Интервал повторов","Ограничивает частые одинаковые эффекты в одной области.","с",.02,.5,.01),
            new GameplayAudioField("movementCell","Группировка движения","Размер области объединения ходовой техники.","м",4,40,1),
            new GameplayAudioField("fadeSeconds","Плавность микса","Скорость изменения громкости и панорамы.","с",.05,.5,.01),
            new GameplayAudioField("effectsBudget","Общий уровень эффектов","Предел суммы уровней всех эффектов для запаса музыке.","уровень",.05,.5,.01),
            new GameplayAudioField("shotGain","Уровень выстрелов","Ближний уровень выстрела до затухания и общего ограничения.","уровень",.01,.4,.01),
            new GameplayAudioField("impactGain","Уровень попаданий","Ближний уровень попадания по броне или земле.","уровень",.01,.4,.01),
            new GameplayAudioField("explosionGain","Уровень взрывов","Ближний уровень разрушения и ракетного попадания.","уровень",.01,.5,.01),
            new GameplayAudioField("movementGain","Уровень движения","Ближний уровень одной группы ходовой техники.","уровень",.01,.2,.01),
            new GameplayAudioField("notificationGain","Уровень подтверждений","Уровень короткого сигнала до личной настройки громкости.","уровень",.01,.2,.01),
            new GameplayAudioField("voices","Всего источников","Общий лимит игровых эффектов; подтверждение имеет отдельный источник.","шт",4,32,1),
            new GameplayAudioField("shots","Выстрелы","Лимит одновременно слышимых выстрелов.","шт",1,12,1),
            new GameplayAudioField("impacts","Попадания","Лимит одновременно слышимых попаданий.","шт",1,12,1),
            new GameplayAudioField("explosions","Уничтожения","Лимит одновременно слышимых разрушений.","шт",1,8,1),
            new GameplayAudioField("movementGroups","Группы движения","Лимит объединённых звуков ходовой.","шт",1,6,1),
            new GameplayAudioField("freshTicks","Свежесть событий","Старые события отбрасываются без догоняющей очереди.","тики",1,15,1),
            new GameplayAudioField("baseGain","Уровень базы","Стройка, ремонт и демонтаж до общего ограничения.","уровень",.01,.3,.01),
            new GameplayAudioField("damageGain","Тяжёлое повреждение","Уровень акцента повреждения.","уровень",.01,.3,.01),
            new GameplayAudioField("motionAccentGain","Запуск и остановка","Уровень коротких акцентов движения.","уровень",.01,.2,.01),
            new GameplayAudioField("baseVoices","Источники базы","Лимит одной категории действий базы.","шт",1,6,1),
            new GameplayAudioField("warningSeconds","Повтор тревоги","Минимальный интервал важных угроз и ограничений.","с",1,30,.5),
            new GameplayAudioField("notificationSeconds","Повтор уведомления","Минимальный интервал коротких сигналов одного вида.","с",.1,2,.05),
            new GameplayAudioField("baseRepeatSeconds","Повтор базы","Интервал пространственных действий базы в одной области.","с",.1,3,.1),
            new GameplayAudioField("motionRepeatSeconds","Повтор движения","Интервал акцентов движения колонны в одной области.","с",.1,3,.1),
            new GameplayAudioField("heavyDamageRatio","Порог повреждения","Доля оставшегося здоровья для акцента тяжёлого повреждения.","доля",.1,.6,.05)
        };
    }
}
