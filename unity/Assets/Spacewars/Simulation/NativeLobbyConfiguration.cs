using System;
using System.Linq;
using System.Collections.Generic;
using Spacewars.Simulation.Ai;

namespace Spacewars.Simulation
{
    [Serializable]
    public sealed class NativeLobbyParticipant
    {
        public string Name;
        public int Team, Color;
        public bool Human;
        // Zero is the mixed mouse/keyboard seat; positive values are exact InputSystem device IDs.
        public int DeviceId;
        public AiDifficulty Difficulty = AiDifficulty.Fighter;
        public NativeLobbyParticipant Copy() => (NativeLobbyParticipant)MemberwiseClone();
    }
    // Current authored mode: two opposing seats, one human and a configured native AI.
    // Names/paint are presentation identity; teams must preserve the two-owner hostility contract.
    [Serializable]
    public sealed class NativeLobbyConfiguration
    {
        public List<NativeLobbyParticipant> Participants;
        public bool Spectator => Participants != null && Participants.All(p => !p.Human);
        public void InitializeParticipants()
        {
            if(Participants != null)return;
            Participants=new List<NativeLobbyParticipant>();
            if(HumanPresent)Participants.Add(new NativeLobbyParticipant{Name=HumanName,Human=true,Team=Foundry?1:HumanTeam,Color=Foundry?0:HumanColor});
            if(Foundry)for(int i=1;i<6;i++)Participants.Add(new NativeLobbyParticipant{Name="ИИ "+i,Team=i<3?1:2,Color=i,Difficulty=Difficulty});
            else if(AiPresent)Participants.Add(new NativeLobbyParticipant{Name=MatchAiName,Team=AiTeam,Color=AiColor,Difficulty=Difficulty});
        }
        public string AddParticipant(bool human,int deviceId,int capacity)
        {
            InitializeParticipants();
            if(human && Participants.Any(p=>p.Human&&p.DeviceId==deviceId))return "Этот контроллер уже назначен участнику.";
            if(Participants.Count>=capacity)return "Все места заняты. Удалите участника, чтобы освободить место.";
            if(human&&Participants.Count(p=>p.Human)>=4)return "Доступно до четырёх локальных игроков.";
            int color=Enumerable.Range(0,ColorHex.Length).First(c=>Participants.All(p=>p.Color!=c));
            Participants.Add(new NativeLobbyParticipant{Name=human?"Игрок "+(Participants.Count(p=>p.Human)+1):"ИИ "+(Participants.Count(p=>!p.Human)+1),Human=human,DeviceId=deviceId,Color=color,Team=Participants.Count==0?1:Participants.Count%2+1,Difficulty=Difficulty});
            return null;
        }
        public bool Foundry;
        public string HumanName = "Игрок 1";
        public string AiName = "ИИ";
        public int HumanTeam = 1, AiTeam = 2;
        public int HumanColor = 0, AiColor = 1;
        public bool HumanPresent = true, AiPresent = true;
        public const int Seed = 19092026; // Explicit legacy diagnostic fixture seed only.
        public AiDifficulty Difficulty = AiDifficulty.Fighter;
        public bool HasExplicitSeed;
        public int ExplicitSeed;
        public int ResolveSeed()=>AiMatchSeed.Resolve(HasExplicitSeed?(int?)ExplicitSeed:null);
        public string DifficultyLabel=>DifficultyNames[(int)Difficulty];
        public static readonly string[] DifficultyNames = { "Новобранец", "Боец", "Ветеран" };
        public static readonly string[] ColorNames = { "Голубой", "Красный", "Лайм", "Жёлтый", "Фиолетовый", "Оранжевый", "Мятный", "Белый" };
        public static readonly string[] ColorHex = { "#59d8ff", "#ff5a73", "#a8ff53", "#ffd34e", "#c682ff", "#ff9e58", "#54f0c4", "#f1f1f1" };
        public int Capacity(PlayableProfile profile) => profile.AuthoredMap == null ? 0 : profile.AuthoredMap.Sites(profile).Count(s => s.Kind == PlayableBuildingKind.Headquarters);
        public string Validate(PlayableProfile profile, bool keyboardConnected, bool mouseConnected)
        {
            if(Participants!=null)
            {
                if(Participants.Count<2)return "Добавьте не менее двух участников.";
                if(Participants.Count>Capacity(profile))return "Состав превышает вместимость карты. Освободите лишние места.";
                if(Participants.Any(p=>p==null||string.IsNullOrWhiteSpace(p.Name)||p.Name.Trim().Length>24))return "Укажите имена участников: не более 24 символов.";
                if(Participants.Any(p=>p.Team<1||p.Team>8))return "Команда: от 1 до 8.";
                if(Participants.Select(p=>p.Team).Distinct().Count()<2)return "На карте нужны две противоборствующие команды.";
                if(Participants.Any(p=>p.Color<0||p.Color>=ColorHex.Length)||Participants.Select(p=>p.Color).Distinct().Count()!=Participants.Count)return "Каждому участнику нужен уникальный цвет.";
                if(Participants.Any(p=>!Enum.IsDefined(typeof(AiDifficulty),p.Difficulty)))return "Выберите сложность каждого ИИ.";
                var humans=Participants.Where(p=>p.Human).ToArray();
                if(humans.Length>4||humans.Select(p=>p.DeviceId).Distinct().Count()!=humans.Length)return "Назначьте каждому игроку отдельное устройство.";
                if(humans.Any(p=>p.DeviceId==0)&&(!keyboardConnected||!mouseConnected))return "Ожидание мыши и клавиатуры Игрока 1.";
                return null;
            }
            if (!Enum.IsDefined(typeof(AiDifficulty),Difficulty)) return "Выберите сложность ИИ.";
            if (Capacity(profile) != (Foundry?6:2)) return "Карта не соответствует составу матча.";
            if (!HumanPresent || !Foundry&&!AiPresent) return "Заполните оба игровых места.";
            if (string.IsNullOrWhiteSpace(HumanName) || !Foundry&&string.IsNullOrWhiteSpace(AiName)) return "Укажите имена обоих участников.";
            if (HumanName.Trim().Length > 24 || !Foundry&&AiName.Trim().Length > 24) return "Имя: не более 24 символов.";
            if (HumanTeam < 1 || HumanTeam > 8 || AiTeam < 1 || AiTeam > 8) return "Команда: от 1 до 8.";
            if (!Foundry && HumanTeam == AiTeam) return "На карте нужны две противоборствующие команды.";
            if (HumanColor < 0 || HumanColor >= ColorHex.Length || AiColor < 0 || AiColor >= ColorHex.Length) return "Выберите цвет из палитры.";
            if (!Foundry && HumanColor == AiColor) return "Каждому участнику нужен уникальный цвет.";
            if (!keyboardConnected || !mouseConnected) return "Ожидание мыши и клавиатуры Игрока 1.";
            return null;
        }
        public string ParticipantName(int slot)=>Participants!=null&&slot<Participants.Count?Participants[slot].Name.Trim():slot==0?MatchHumanName:"ИИ "+DifficultyLabel+" "+slot;
        public int ParticipantColor(int slot)=>Participants!=null&&slot<Participants.Count?Participants[slot].Color:Foundry?slot:slot==0?HumanColor:AiColor;
        public NativeLobbyConfiguration Copy()
        {
            var copy=(NativeLobbyConfiguration)MemberwiseClone();
            copy.Participants=Participants?.Select(p=>p.Copy()).ToList();return copy;
        }
        public string MatchHumanName => HumanName.Trim();
        public string MatchAiName => AiName.Trim() == "ИИ" ? "ИИ "+DifficultyLabel+" 1" : AiName.Trim();
    }
}
