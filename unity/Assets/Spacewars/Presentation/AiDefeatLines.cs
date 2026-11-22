using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Runtime;

namespace Spacewars.Presentation
{
    public static class AiDefeatLines
    {
        public const double ReadingSeconds=4;

        public sealed class Line
        {
            public string Key { get; }
            public string Text { get; }
            internal bool Rare { get; }
            internal Func<AiDefeat,bool> Eligible { get; }

            internal Line(string key,string text,bool rare=false,Func<AiDefeat,bool> eligible=null)
            {
                Key="defeat-"+key;
                Text=text;
                Rare=rare;
                Eligible=eligible;
            }
        }

        public static IReadOnlyList<Line> All { get; }=Array.AsReadOnly(new[]{
            new Line("worthy","Это была достойная битва."),
            new Line("deserved","Хороший бой. Победа заслуженная."),
            new Line("last-base","Последняя база потеряна. Связь прекращаю."),
            new Line("plan","У меня был план, и я его придерживался."),
            new Line("close","Эх, а ведь почти получилось."),
            new Line("next-time","В следующий раз всё будет иначе."),
            new Line("lucky-one","Тебе просто повезло.",eligible:d=>d.Opponents==1),
            new Line("lucky-many","Вам просто повезло.",eligible:d=>d.Opponents>1),
            new Line("enjoy","Ну что ж. Наслаждайтесь, пока можете."),
            new Line("radio","Штаб, ответьте. Штаб?.. Ах да.",true),
            new Line("report","Запишите в отчёте: противник отказался проигрывать.",true),
            new Line("army","Войска остались, но командовать некому.",true,d=>d.LivingPopulation>10),
            new Line("backup-plan","У меня был запасной план. Он сгорел вместе со штабом.",true,d=>d.LostHeadquarters),
            new Line("formation","Зато строй держали красиво.",true),
            new Line("recon","Считайте это разведкой боем. Очень подробной разведкой.",true),
            new Line("rematch","Я требую матча-реванша.",true),
            new Line("save","Ничего. У меня ещё есть сохранение.",true)
        });

        public static IEnumerable<Line> Eligible(AiDefeat defeat)
            =>All.Where(line=>line.Eligible==null||line.Eligible(defeat));

        internal static Line Choose(AiDefeat defeat,Random random,HashSet<string> used)
        {
            var eligible=Eligible(defeat).ToArray();
            bool rare=random.Next(5)==0;
            var choices=eligible.Where(line=>line.Rare==rare&&!used.Contains(line.Key)).ToArray();
            if(choices.Length==0)choices=eligible.Where(line=>!used.Contains(line.Key)).ToArray();
            if(choices.Length==0)choices=eligible.Where(line=>line.Rare==rare).ToArray();
            return choices[random.Next(choices.Length)];
        }
    }
}
