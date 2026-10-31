using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Detached, immutable presentation event. Never a gameplay command or save payload.
    public sealed class AiCommunication
    {
        public AiCommunication(long id,long generation,long tick,string author,int team,string text,NavPoint? point=null,TeamSignalKind kind=TeamSignalKind.Recon,bool critical=false)
        {Id=id;Generation=generation;Tick=tick;Author=author;Team=team;Text=text;Point=point;Kind=kind;Critical=critical;}
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
            bool Publish(string text,NavPoint? point=null,TeamSignalKind kind=TeamSignalKind.Recon,bool critical=false)
            {
                Count(point.HasValue?"positionalAttempts":"textAttempts");
                if(critical){if(criticalTeams.TryGetValue(o.Team,out var last)&&o.Tick-last<data.aiChatCriticalSeconds*30){Count("criticalBudget");return false;}criticalTeams[o.Team]=o.Tick;}
                else {if(o.Tick-state.Last<data.aiChatAuthorSeconds*30){Count("authorBudget");return false;}if(teams.TryGetValue(o.Team,out var last)&&o.Tick-last<data.aiChatTeamSeconds*30){Count("teamBudget");return false;}state.Last=o.Tick;teams[o.Team]=o.Tick;}
                Count(point.HasValue?"positionalEmitted":"textEmitted");
                events.Add(new AiCommunication(++next,o.Generation,o.Tick,o.OwnerId,o.Team,text,point,kind,critical));return true;
            }
            // Critical edges are processed before ordinary plans in the same review.
            bool defense=AiDefensePlanner.Threats(o,profile).Length>0;
            if(defense&&!state.Defense)
            {
                var center=o.Buildings.Where(b=>b.Owner==o.Owner&&b.Health>0).OrderBy(b=>b.Kind==PlayableBuildingKind.Headquarters?0:1).FirstOrDefault();
                if(center!=null)Publish("У базы противник. Нужна защита.",center.Position,TeamSignalKind.Defend,true);
            }
            else if(!defense&&state.Defense)Publish("Угроза базе миновала. Возвращаюсь к обычному плану.",critical:true);
            state.Defense=defense;
            if(!state.Opening&&ai.Opening!=null){state.Opening=true;Publish(OpeningText(ai.Opening.Opening));}
            if(ai.Strategy?.Strategy!=null&&ai.Strategy.Strategy!=state.Strategy)
            {state.Strategy=ai.Strategy.Strategy;Publish(StrategyText(state.Strategy.Value));}
            var live=new HashSet<string>();
            foreach(var army in ai.Armies?.Armies??Array.Empty<AiArmyState>())
            {
                var mission=ai.ArmyMission;bool main=mission!=null&&mission.ArmyId==army.Id;
                long operationTick=main?mission.StartedTick:army.CreatedTick;
                // Regrouping/reinforcement does not turn an existing operation into a new announcement.
                live.Add(army.Id+":advance:"+operationTick);live.Add(army.Id+":retreat:"+operationTick);
                bool retreat=army.Phase==AiArmyPhase.Retreating||army.Phase==AiArmyPhase.Recovering;
                if(!retreat&&army.Phase!=AiArmyPhase.Advancing&&army.Phase!=AiArmyPhase.Engaging){Count("armyPhase");continue;}
                if(army.Role!=AiArmyRole.Attack&&army.Role!=AiArmyRole.Raid&&!retreat){Count("armyRole");continue;}
                if(!retreat&&(main&&mission.TargetKind==PlayableRouteTargetKind.FriendlyAnchor||
                    army.Objective?.StartsWith(((int)PlayableRouteTargetKind.FriendlyAnchor)+":",StringComparison.Ordinal)==true)){Count("friendlyAnchor");continue;}
                string key=army.Id+":"+(retreat?"retreat":"advance")+":"+operationTick;
                if(state.Operations.Contains(key)){Count("alreadyAnnounced");continue;}
                var moving=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&EffectiveMoving(u,o)&&
                    state.Positions.TryGetValue(u.Id,out var previous)&&!previous.Equals(u.Position)).ToArray();
                if(moving.Length==0){Count("noEffectiveChangedMotion");continue;}
                var goal=main&&!retreat?mission.Target:!retreat&&army.Rally.HasValue?army.Rally.Value:moving[0].CurrentOrder.Destination;
                moving=moving.Where(u=>retreat||(main&&mission.TargetKind==PlayableRouteTargetKind.VisibleEnemy?
                    u.CurrentOrder.TargetId==mission.TargetId:u.CurrentOrder.Destination.Equals(goal))).ToArray();
                if(moving.Length==0){Count("goalMismatch");continue;}
                // Target comes from an owner-safe mission/order, never a spectator projection.
                bool raid=army.Role==AiArmyRole.Raid;
                string text=retreat?"Отвожу группу к оборонной позиции.":(raid?"Начинаю рейд":"Выдвигаюсь в наступление")+" "+Location(goal)+": "+moving.Length+" ед. техники.";
                state.Operations.Add(key); // Budget rejection is final for this live operation.
                Publish(text,goal,retreat?TeamSignalKind.Defend:TeamSignalKind.Attack,retreat);
            }
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
            var stamp=u.OrderStamp;var order=u.CurrentOrder;
            return u.Moving&&order!=null&&stamp!=null&&stamp.Origin==PlayableOrderOrigin.Ai&&stamp.Source==PlayableAiOpeningComposition.SourceIdentity&&
                stamp.Generation==o.Generation&&stamp.Owner==o.Owner&&stamp.UnitId==u.Id&&stamp.ActionId>0&&stamp.JobId>0&&stamp.Sequence==order.CommandSequence&&
                order.Generation==o.Generation&&order.Owner==o.Owner&&order.UnitId==u.Id&&order.IssuedTick==stamp.Tick&&
                (order.Kind==PlayableTacticalOrderKind.Move||order.Kind==PlayableTacticalOrderKind.AttackMove||order.Kind==PlayableTacticalOrderKind.Attack);
        }
        internal static string Location(NavPoint p)=>"в "+(p.Z<0?"северо":"юго")+"-"+(p.X<0?"западной":"восточной")+" части карты";
        internal static string OpeningText(PlayableAiOpening opening)=>opening switch
        {
            PlayableAiOpening.Safe=>"Начну с безопасного развития: экономика, оборона, затем армия.",
            PlayableAiOpening.GreedySafe=>"Вкладываюсь в экономику базы. Армию соберу позже.",
            PlayableAiOpening.GreedyMine=>"Начну с захвата шахты и развития дохода.",
            PlayableAiOpening.BlindRush=>"Готовлю раннюю танковую атаку.",
            PlayableAiOpening.ExplorerAllIn=>"Ставлю на раннюю атаку исследователями.",
            PlayableAiOpening.DoubleMineExplorerRush=>"Сначала займу две шахты, затем атакую исследователями.",
            PlayableAiOpening.Air=>"Готовлю авиацию и прикрытие базы.",
            _=>throw new ArgumentOutOfRangeException(nameof(opening))
        };
        internal static string StrategyText(PlayableAiMidgameStrategy strategy)=>strategy switch
        {
            PlayableAiMidgameStrategy.MassAssault=>"Собираю ударную армию для штурма.",
            PlayableAiMidgameStrategy.MapControl=>"Беру под контроль шахты и форпосты.",
            PlayableAiMidgameStrategy.Raids=>"Перехожу к рейдам по экономике.",
            _=>throw new ArgumentOutOfRangeException(nameof(strategy))
        };
    }
}
