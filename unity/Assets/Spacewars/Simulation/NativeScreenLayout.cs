using System;
using System.Linq;
namespace Spacewars.Simulation
{
    public static class NativeScreenLayout
    {
        public static int Count(NativeLobbyConfiguration setup)=>setup.Spectator?0:setup.Participants.Count(p=>p.Human||p.ScreenSlot>=0);
        public static void Normalize(NativeLobbyConfiguration setup)
        {
            setup.InitializeParticipants();
            if(setup.Spectator){foreach(var p in setup.Participants)p.ScreenSlot=-1;return;}
            var humans=setup.Participants.Where(p=>p.Human).ToArray();
            var bots=setup.Participants.Where(p=>!p.Human&&p.ScreenSlot>=0).ToArray();
            foreach(var p in bots.Skip(Math.Max(0,4-humans.Length)))p.ScreenSlot=-1;
            var screens=humans.Concat(bots.Take(Math.Max(0,4-humans.Length))).ToArray();
            int capacity=screens.Length<3?screens.Length:4;var used=new bool[capacity];
            // Preserve valid explicit choices before assigning new mandatory seats.
            foreach(var p in screens){int slot=p.ScreenSlot;if(capacity==2&&slot>=2)slot%=2;
                if(slot>=0&&slot<capacity&&!used[slot]){p.ScreenSlot=slot;used[slot]=true;}else p.ScreenSlot=-1;}
            foreach(var p in screens.Where(p=>p.ScreenSlot<0)){int slot=Array.IndexOf(used,false);p.ScreenSlot=slot;used[slot]=true;}
        }
        public static string Assign(NativeLobbyConfiguration setup,NativeLobbyParticipant participant,int slot)
        {
            Normalize(setup);
            if(setup.Spectator)return "В режиме зрителя используется весь экран.";
            if(!setup.Participants.Contains(participant))return "Участник отсутствует.";
            if(slot<0){if(participant.Human)return "Игроку нужен экран.";participant.ScreenSlot=-1;Normalize(setup);return null;}
            int count=Count(setup)+(participant.ScreenSlot<0?1:0);
            if(count>4)return "Доступно до четырёх экранов.";
            int capacity=count<3?count:4;if(slot>=capacity)return "Недоступная позиция экрана.";
            if(participant.ScreenSlot<0){participant.ScreenSlot=Enumerable.Range(0,capacity).First(i=>setup.Participants.All(p=>p==participant||p.ScreenSlot!=i));Normalize(setup);}
            var other=setup.Participants.FirstOrDefault(p=>p!=participant&&p.ScreenSlot==slot);
            if(other!=null)other.ScreenSlot=participant.ScreenSlot;
            participant.ScreenSlot=slot;return null;
        }
        public static string Label(int slot,int count)=>slot<0?"Без экрана":count<=1?"Весь экран":count==2?(slot==0?"Слева":"Справа"):new[]{"Слева сверху","Справа сверху","Слева снизу","Справа снизу"}[slot];
    }
}
