using System;
using System.Linq;

namespace Spacewars.Simulation
{
    // Current authored mode: two opposing seats, one human and the native Fighter AI.
    // Names/paint are presentation identity; teams must preserve the two-owner hostility contract.
    [Serializable]
    public sealed class NativeLobbyConfiguration
    {
        public string HumanName = "Игрок 1";
        public string AiName = "ИИ";
        public int HumanTeam = 1, AiTeam = 2;
        public int HumanColor = 0, AiColor = 1;
        public bool HumanPresent = true, AiPresent = true;
        public const int Seed = 19092026;
        public static readonly string[] ColorNames = { "Голубой", "Красный", "Лайм", "Жёлтый", "Фиолетовый", "Оранжевый", "Мятный", "Белый" };
        public static readonly string[] ColorHex = { "#59d8ff", "#ff5a73", "#a8ff53", "#ffd34e", "#c682ff", "#ff9e58", "#54f0c4", "#f1f1f1" };
        public int Capacity(PlayableProfile profile) => profile.AuthoredMap == null ? 0 : profile.AuthoredMap.Sites(profile).Count(s => s.Kind == PlayableBuildingKind.Headquarters);
        public string Validate(PlayableProfile profile, bool keyboardConnected, bool mouseConnected)
        {
            if (Capacity(profile) != 2) return "Для этого режима нужна карта с двумя стартами.";
            if (!HumanPresent || !AiPresent) return "Заполните оба игровых места.";
            if (string.IsNullOrWhiteSpace(HumanName) || string.IsNullOrWhiteSpace(AiName)) return "Укажите имена обоих участников.";
            if (HumanName.Trim().Length > 24 || AiName.Trim().Length > 24) return "Имя: не более 24 символов.";
            if (HumanTeam < 1 || HumanTeam > 8 || AiTeam < 1 || AiTeam > 8) return "Команда: от 1 до 8.";
            if (HumanTeam == AiTeam) return "На карте нужны две противоборствующие команды.";
            if (HumanColor < 0 || HumanColor >= ColorHex.Length || AiColor < 0 || AiColor >= ColorHex.Length) return "Выберите цвет из палитры.";
            if (HumanColor == AiColor) return "Каждому участнику нужен уникальный цвет.";
            if (!keyboardConnected || !mouseConnected) return "Ожидание мыши и клавиатуры Игрока 1.";
            return null;
        }
        public NativeLobbyConfiguration Copy() => (NativeLobbyConfiguration)MemberwiseClone();
        public string MatchHumanName => HumanName.Trim();
        public string MatchAiName => AiName.Trim() == "ИИ" ? "ИИ Боец 1" : AiName.Trim();
    }
}
