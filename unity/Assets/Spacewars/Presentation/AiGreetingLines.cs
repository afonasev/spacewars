using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    public static class AiGreetingLines
    {
        public const double StartDelaySeconds = 2;
        public const double GapSeconds = 1.25;

        public enum Tone { Friendly, Respectful, Bold, Formal, Humorous }

        public sealed class Line
        {
            public string Key { get; }
            public string Text { get; }
            public Tone Mood { get; }

            internal Line(string key, string text, Tone mood)
            {
                Key = "greeting-" + key;
                Text = text;
                Mood = mood;
            }
        }

        public readonly struct Greeting
        {
            public int Slot { get; }
            public Line Phrase { get; }

            internal Greeting(int slot, Line phrase)
            {
                Slot = slot;
                Phrase = phrase;
            }
        }

        public static IReadOnlyList<Line> All { get; } = Array.AsReadOnly(new[] {
            new Line("good-luck","Всем удачи. Хорошего боя!",Tone.Friendly),
            new Line("heated","Удачи. Надеюсь, будет жарко.",Tone.Friendly),
            new Line("meeting","Рад встретиться с вами на поле боя.",Tone.Respectful),
            new Line("opponents","Хороших соперников всегда приятно встретить.",Tone.Respectful),
            new Line("trick","Если есть хитрый план, самое время его достать.",Tone.Bold),
            new Line("head-start","Даю вам фору. Пока я здороваюсь.",Tone.Bold),
            new Line("ready","Связь проверил. К бою готов.",Tone.Formal),
            new Line("commanders","Командиры, приветствую. Начинаем.",Tone.Formal),
            new Line("base","Всем привет. Штаб пока на месте.",Tone.Humorous),
            new Line("plan","Связь есть. План тоже есть. Пока всё идёт по плану.",Tone.Humorous)
        });

        public static Greeting[] Select(IReadOnlyList<NativeLobbyParticipant> participants, Random random)
        {
            var teams = Enumerable.Range(0, participants.Count)
                .Where(slot => !participants[slot].Human)
                .GroupBy(slot => participants[slot].Team)
                .Select(team => team.ToArray()).ToList();
            var selected = new Greeting[Math.Min(2, teams.Count)];
            for (int i = 0; i < selected.Length; i++)
            {
                int teamIndex = random.Next(teams.Count);
                var slots = teams[teamIndex];
                teams.RemoveAt(teamIndex);
                var phrases = i == 0 ? All : All.Where(line => line.Mood != selected[0].Phrase.Mood).ToArray();
                selected[i] = new Greeting(slots[random.Next(slots.Length)], phrases[random.Next(phrases.Count)]);
            }
            return selected;
        }
    }
}
