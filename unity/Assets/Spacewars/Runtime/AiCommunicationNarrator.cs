using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Detached, immutable presentation event. Never a gameplay command or save payload.
    public sealed class AiCommunication
    {
        public AiCommunication(long id,long generation,long tick,string author,int team,string text,NavPoint? point=null,TeamSignalKind kind=TeamSignalKind.Recon,bool critical=false,string voiceKey=null)
        {VoiceKey=voiceKey;Id=id;Generation=generation;Tick=tick;Author=author;Team=team;Text=text;Point=point;Kind=kind;Critical=critical;}
        public string VoiceKey{get;}
        public long Id{get;} public long Generation{get;} public long Tick{get;}
        public string Author{get;} public int Team{get;} public string Text{get;}
        public NavPoint? Point{get;} public TeamSignalKind Kind{get;} public bool Critical{get;}
    }

    // Authority-thread observer only. No world, planner, checkpoint or RNG mutations.
    internal sealed class AiCommunicationNarrator
    {
        private sealed class OwnerState
        {
            internal bool Opening,Defense;
            internal long ObservedTick=-1;
            internal readonly Dictionary<string,long> Counts=new Dictionary<string,long>();
            internal string Diagnostic;
            internal PlayableAiMidgameStrategy? Strategy;
            internal long Last=-1000000;
            internal readonly Dictionary<int,NavPoint> Positions=new Dictionary<int,NavPoint>();
            internal readonly HashSet<string> Operations=new HashSet<string>();
        }
        private readonly Dictionary<string,OwnerState> owners=new Dictionary<string,OwnerState>();
        private readonly Dictionary<int,long> teams=new Dictionary<int,long>(),criticalTeams=new Dictionary<int,long>();
        private readonly List<AiCommunication> events=new List<AiCommunication>();
        private long next;
        private readonly bool diagnostic=Environment.GetEnvironmentVariable("SPACEWARS_AI_CHAT_DIAGNOSTIC")=="1";
        internal string[] Diagnostics=>diagnostic?owners.Values.Select(s=>s.Diagnostic).Where(s=>s!=null).ToArray():Array.Empty<string>();
        private PlayableProfile boundProfile;
        private PlayableProfileData tuning;
        internal AiCommunication[] Snapshot=>events.ToArray();
        internal void Observe(PlayableSnapshot snapshot,PlayableAiOwnerCheckpoint ai,PlayableProfile profile)
        {
            if(snapshot.Paused||snapshot.Outcome!=PlayableMatchOutcome.Playing||ai==null||ai.OwnerId!=snapshot.OwnerId||ai.Generation!=snapshot.Generation)return;
            if(!ReferenceEquals(boundProfile,profile)){boundProfile=profile;tuning=profile.CopyData();}
            var o=PlayableAiObservation.From(snapshot);var data=tuning;
            if(!owners.TryGetValue(o.OwnerId,out var state))owners.Add(o.OwnerId,state=new OwnerState());
            void Count(string reason){if(diagnostic)state.Counts[reason]=state.Counts.TryGetValue(reason,out var count)?count+1:1;}
            Count("reviews");
            bool Publish(string text,NavPoint? point=null,TeamSignalKind kind=TeamSignalKind.Recon,bool critical=false,string voiceKey=null)
            {
                Count(point.HasValue?"positionalAttempts":"textAttempts");
                if(critical){if(criticalTeams.TryGetValue(o.Team,out var last)&&o.Tick-last<data.aiChatCriticalSeconds*30){Count("criticalBudget");return false;}criticalTeams[o.Team]=o.Tick;}
                else {if(o.Tick-state.Last<data.aiChatAuthorSeconds*30){Count("authorBudget");return false;}if(teams.TryGetValue(o.Team,out var last)&&o.Tick-last<data.aiChatTeamSeconds*30){Count("teamBudget");return false;}state.Last=o.Tick;teams[o.Team]=o.Tick;}
                Count(point.HasValue?"positionalEmitted":"textEmitted");
                events.Add(new AiCommunication(++next,o.Generation,o.Tick,o.OwnerId,o.Team,text,point,kind,critical,voiceKey));return true;
            }
            // Critical edges are processed before ordinary plans in the same review.
            var threats=AiDefensePlanner.Threats(o,profile);bool defense=threats.Length>0;
            if(defense&&!state.Defense)
                Publish("Нужна защита!",threats[0].Position,TeamSignalKind.Defend,true,"ally-need-defense");
            else if(!defense&&state.Defense)Publish("Угроза миновала",critical:true);
            state.Defense=defense;
            // A selected strategy is intent, never evidence of production or field execution.
            if(!state.Opening&&ai.Opening!=null){state.Opening=true;Publish(OpeningText(ai.Opening.Opening));}
            if(ai.Strategy?.Strategy!=null&&ai.Strategy.Strategy!=state.Strategy)
            {state.Strategy=ai.Strategy.Strategy;Publish(StrategyText(state.Strategy.Value));}
            var live=new HashSet<string>();
            foreach(var army in ai.Armies?.Armies??Array.Empty<AiArmyState>())
            {
                var mission=ai.ArmyMission;bool main=mission!=null&&mission.ArmyId==army.Id;
                long operationTick=main?mission.StartedTick:army.CreatedTick;
                // One announcement of each observed action per operation. Regrouping is not a new operation.
                foreach(var operationAction in new[]{"advance","engage","retreat"})live.Add(army.Id+":"+operationAction+":"+operationTick);
                bool retreat=army.Phase==AiArmyPhase.Retreating||army.Phase==AiArmyPhase.Recovering;
                if(!retreat&&army.Phase!=AiArmyPhase.Advancing&&army.Phase!=AiArmyPhase.Engaging){Count("armyPhase");continue;}
                if(army.Role!=AiArmyRole.Attack&&army.Role!=AiArmyRole.Raid&&!retreat){Count("armyRole");continue;}
                if(!retreat&&(main&&mission.TargetKind==PlayableRouteTargetKind.FriendlyAnchor||
                    army.Objective?.StartsWith(((int)PlayableRouteTargetKind.FriendlyAnchor)+":",StringComparison.Ordinal)==true)){Count("friendlyAnchor");continue;}
                var members=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&EffectiveOrder(u,o)).ToArray();
                var moving=members.Where(u=>u.Moving&&state.Positions.TryGetValue(u.Id,out var previous)&&!previous.Equals(u.Position)).ToArray();
                if(retreat)moving=moving.Where(u=>AiTacticalExecutor.EffectiveWithdrawal(o,army,u,ai.Records,profile,certified:false)).ToArray();
                var firing=state.ObservedTick<0?Array.Empty<PlayableEntitySnapshot>():members.Where(u=>snapshot.Sounds.Any(e=>e.Kind==PlayableSoundKind.Shot&&e.Owner==o.Owner&&e.UnitId==u.Id&&e.Tick>state.ObservedTick&&e.Tick<=o.Tick)).ToArray();
                if(moving.Length==0&&firing.Length==0){Count("noEffectiveChangedMotion");continue;}
                var goal=retreat?moving.FirstOrDefault()?.CurrentOrder.Destination:main?mission.Target:army.Rally;
                if(!goal.HasValue)goal=(firing.FirstOrDefault()??moving.First()).CurrentOrder.Destination;
                if(!retreat)
                {
                    bool Matches(PlayableEntitySnapshot u)=>main&&mission.TargetKind==PlayableRouteTargetKind.VisibleEnemy?
                        u.CurrentOrder.TargetId==mission.TargetId||u.TargetId==mission.TargetId:u.CurrentOrder.Destination.Equals(goal.Value);
                    moving=moving.Where(Matches).ToArray();firing=firing.Where(Matches).ToArray();
                }
                if(retreat&&moving.Length==0||!retreat&&moving.Length==0&&firing.Length==0){Count("goalMismatch");continue;}
                string action=retreat?"retreat":firing.Length>0?"engage":"advance";
                string key=army.Id+":"+action+":"+operationTick;
                if(state.Operations.Contains(key)){Count("alreadyAnnounced");continue;}
                bool raid=army.Role==AiArmyRole.Raid;string voiceKey=retreat?"ally-retreat":firing.Length>0?"ally-engage":raid?"ally-raid":"ally-advance";
                string phrase=retreat?"Отступаю":firing.Length>0?"Вступил в бой с противником!":raid?"Начинаю рейд":"Выдвигаюсь";
                int count=firing.Length>0?firing.Length:moving.Length;
                string text=phrase+" "+Location(goal.Value)+" · "+count+" ед.";
                state.Operations.Add(key); // A rejected budget is not replayed later as a stale action.
                Publish(text,goal.Value,retreat?TeamSignalKind.Defend:TeamSignalKind.Attack,retreat,voiceKey);
            }
            state.ObservedTick=o.Tick;

            if(diagnostic)state.Diagnostic=Newtonsoft.Json.JsonConvert.SerializeObject(new {
                o.OwnerId,o.Tick,o.Generation,o.Team,o.ProfileId,o.ProfileRevision,Counts=state.Counts,
                Opening=ai.Opening,Strategy=ai.Strategy,ai.ArmyMission,
                Armies=ai.Armies?.Armies,RecentMissionRecords=ai.Records.Where(r=>AiArmyPlanner.IsArmyPolicy(r.Policy)).Reverse().Take(12),
                Units=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0).Select(u=>new {
                    u.Id,u.Kind,u.Position,u.Moving,u.CurrentOrder,u.OrderStamp,u.Queue,u.NavigationOutcome,
                    Effective=EffectiveMoving(u,o),Changed=state.Positions.TryGetValue(u.Id,out var previous)&&!previous.Equals(u.Position)})
            });
            state.Operations.IntersectWith(live);
            state.Positions.Clear();foreach(var u in o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0))state.Positions[u.Id]=u.Position;
            events.RemoveAll(e=>o.Tick-e.Tick>data.aiChatMaxAgeSeconds*30);
            // Technical transport bound protects an unattended/slow presentation consumer.
            if(events.Count>128)events.RemoveRange(0,events.Count-128);
        }
        internal static bool EffectiveMoving(PlayableEntitySnapshot u,PlayableAiObservation o)
        {
            return EffectiveOrder(u,o)&&u.Moving;
        }
        internal static bool EffectiveOrder(PlayableEntitySnapshot u,PlayableAiObservation o)
        {
            var stamp=u.OrderStamp;var order=u.CurrentOrder;
            return order!=null&&stamp!=null&&stamp.Origin==PlayableOrderOrigin.Ai&&stamp.Source==PlayableAiOpeningComposition.SourceIdentity&&
                stamp.Generation==o.Generation&&stamp.Owner==o.Owner&&stamp.UnitId==u.Id&&stamp.ActionId>0&&stamp.JobId>0&&stamp.Sequence==order.CommandSequence&&
                order.Generation==o.Generation&&order.Owner==o.Owner&&order.UnitId==u.Id&&order.IssuedTick==stamp.Tick&&
                (order.Kind==PlayableTacticalOrderKind.Move||order.Kind==PlayableTacticalOrderKind.AttackMove||order.Kind==PlayableTacticalOrderKind.Attack);
        }
        internal static string Location(NavPoint p)=>"в "+(p.Z>=0?"северо":"юго")+"-"+(p.X<0?"западной":"восточной")+" части карты";
        internal static string OpeningText(PlayableAiOpening opening)=>opening switch
        {
            PlayableAiOpening.Safe=>"План: развитие и оборона",
            PlayableAiOpening.GreedySafe=>"План: экономика",
            PlayableAiOpening.GreedyMine=>"План: экономика",
            PlayableAiOpening.BlindRush=>"План: ранняя атака",
            PlayableAiOpening.ExplorerAllIn=>"План: ранняя атака",
            PlayableAiOpening.DoubleMineExplorerRush=>"План: экономика",
            PlayableAiOpening.Air=>"План: авиация",
            _=>throw new ArgumentOutOfRangeException(nameof(opening))
        };
        internal static string StrategyText(PlayableAiMidgameStrategy strategy)=>strategy switch
        {
            PlayableAiMidgameStrategy.MassAssault=>"План: штурм",
            PlayableAiMidgameStrategy.MapControl=>"План: контроль карты",
            PlayableAiMidgameStrategy.Raids=>"План: рейды",
            _=>throw new ArgumentOutOfRangeException(nameof(strategy))
        };
    }
}
