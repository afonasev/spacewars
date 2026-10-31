using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Runtime;

namespace Spacewars.Presentation
{
    // Match-scoped presentation state. Never submitted to the host or serialized in a save.
    public sealed class NativeMatchChat
    {
        public sealed class Message
        {
            public readonly int Author, Team;
            public readonly string Name, Text;
            public readonly int Color;
            public readonly double Created;
            public readonly bool IsAi;
            public readonly NavPoint? Point;
            internal Message(int author,int team,string name,int color,string text,double now,NavPoint? point)
                :this(author,team,name,color,text,now){IsAi=true;Point=point;}

            internal double? Evicted;
            internal double EvictionOpacity;
            internal Message(int author, int team, string name, int color, string text, double now)
            { Author=author; Team=team; Name=name; Color=color; Text=text; Created=now; }
        }
        // Channel tags stay outside the positive lobby-team namespace.
        public const int Public = -1, Spectators = -2;
        readonly Dictionary<int,string> spectators = new Dictionary<int,string>();
        public const int TextLimit = 160; // Protocol/input invariant, not a designer parameter.
        readonly NativeLobbyConfiguration roster;
        readonly List<Message> messages = new List<Message>();
        long lastAiEventId;
        readonly Queue<double> ordinaryAiTimes=new Queue<double>();
        int observerAiLimit;
        int limit;
        double readable, fade, evictionFade;
        public NativeMatchChat(NativeLobbyConfiguration setup, PlayableProfile profile)
        {
            roster=(setup??new NativeLobbyConfiguration()).Copy();roster.InitializeParticipants();
            Rebind(profile);
        }
        public void Rebind(PlayableProfile profile)
        {
            var data=profile.CopyData();observerAiLimit=(int)data.aiChatObserverLimit;limit=(int)data.chatVisibleLimit;readable=data.chatReadableSeconds;fade=data.chatFadeSeconds;evictionFade=data.chatEvictionSeconds;
        }
        // Spectators use distinct identities; they never occupy a player/AI slot.
        public bool RegisterSpectator(int author,string name)
        {
            name=Normalize(name).Trim();
            if(author>=0||name.Length==0||name.Length>24||spectators.ContainsKey(author))return false;
            spectators.Add(author,name);return true;
        }
        public IEnumerable<string> AuthorNames=>roster.Participants.Select((p,i)=>new{p.Human,Name=roster.ParticipantName(i)}).Where(p=>p.Human).Select(p=>p.Name).Concat(spectators.Values);
        public static string AudienceLabel(int channel)=>channel==Public?"[All]":channel==Spectators?"[Obs]":"[Team]";
        public bool CanWrite(int author)=>spectators.ContainsKey(author)||author>=0&&author<roster.Participants.Count&&roster.Participants[author].Human;
        public int DefaultChannel(int author)=>spectators.ContainsKey(author)?Spectators:CanWrite(author)&&roster.Participants.Where((p,i)=>i!=author).Any(p=>p.Team==roster.Participants[author].Team)?roster.Participants[author].Team:Public;
        public static string Normalize(string text)
        {
            text=(text??"").Replace('\r',' ').Replace('\n',' ').Replace('\u2028',' ').Replace('\u2029',' ');
            // Do not split a pasted surrogate pair at the length boundary.
            int length=Math.Min(TextLimit,text.Length);if(length>0&&length<text.Length&&char.IsHighSurrogate(text[length-1]))length--;
            return text.Substring(0,length);
        }
        public bool Send(int author,int channel,string text,double now)
        {
            bool observer=spectators.TryGetValue(author,out var observerName);
            if(!CanWrite(author)||(observer?channel!=Spectators&&channel!=Public:channel!=Public&&channel!=roster.Participants[author].Team))return false;
            text=Normalize(text).Trim();if(text.Length==0)return false;
            Add(new Message(author,channel,observer?observerName:roster.ParticipantName(author),observer?-1:roster.ParticipantColor(author),text,now),now);
            return true;
        }
        public bool SendAi(int author,AiCommunication evt,double now)
        {
            if(evt==null||author<0||author>=roster.Participants.Count||roster.Participants[author].Human||roster.Participants[author].Team!=evt.Team)return false;
            string expected=author==0?"player-1":"foundry-"+(author+1);
            if(evt.Author!=expected&&!(author==1&&evt.Author=="enemy-1"))return false;
            string text=Normalize(evt.Text).Trim();if(text.Length==0||evt.Id<=lastAiEventId)return false;
            lastAiEventId=evt.Id; // Sorted authority IDs: rejected/replayed events never become a delayed queue.
            while(ordinaryAiTimes.Count>0&&now-ordinaryAiTimes.Peek()>=readable)ordinaryAiTimes.Dequeue();
            if(roster.Spectator&&!evt.Critical&&ordinaryAiTimes.Count>=observerAiLimit)return false;
            if(!evt.Critical)ordinaryAiTimes.Enqueue(now);
            Add(new Message(author,evt.Team,roster.ParticipantName(author),roster.ParticipantColor(author),text,now,evt.Point),now);
            return true;
        }
        private void Add(Message message,double now)
        {
            Advance(now);int channel=message.Team;
            var active=messages.Where(m=>m.Team==channel&&!m.Evicted.HasValue).ToArray();
            if(active.Length>=limit){var old=active[0];old.EvictionOpacity=Opacity(old,now);old.Evicted=now;}
            messages.Add(message);
            var outgoing=messages.Where(m=>m.Team==channel&&m.Evicted.HasValue).ToArray();
            foreach(var old in outgoing.Take(Math.Max(0,outgoing.Length-limit)))messages.Remove(old);
        }
        public double Opacity(Message message,double now)=>message.Evicted.HasValue?
            Math.Max(0,message.EvictionOpacity*(1-Math.Max(0,now-message.Evicted.Value)/evictionFade)):
            Math.Clamp(1-Math.Max(0,now-message.Created-readable)/fade,0,1);
        public void Advance(double now)=>messages.RemoveAll(m=>Opacity(m,now)<=0);
        public Message[] Visible(int viewer,bool spectator,double now)
        {
            Advance(now);
            bool watching=spectator||spectators.ContainsKey(viewer);
            bool Allowed(Message m)=>m.Team==Public||watching&&m.IsAi||(m.Team==Spectators?watching:
                !watching&&viewer>=0&&viewer<roster.Participants.Count&&m.Team==roster.Participants[viewer].Team);
            var selected=new HashSet<Message>(messages.Where(Allowed).Where(m=>!m.Evicted.HasValue).TakeLast(limit)
                .Concat(messages.Where(Allowed).Where(m=>m.Evicted.HasValue).TakeLast(limit)));
            return messages.Where(selected.Contains).ToArray();
        }
    }
}
