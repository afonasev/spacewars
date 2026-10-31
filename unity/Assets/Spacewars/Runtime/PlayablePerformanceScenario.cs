#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    public sealed partial class PlayableRuntime
    {
        internal const string MovingPerformanceSource="qa:eight-player-400-moving:v1";
        internal static long EightPlayerMovingPerformanceSequence(PlayableOwner owner,long previous)
        {
            if((int)owner<0||(int)owner>=8||previous<0)throw new ArgumentException("Moving performance sequence binding.");
            // Native AI starts at 1L<<61. Keep diagnostic blocks above that range
            // and far apart, while the unchanged authority allocator advances normally.
            long floor=(1L<<62)+((long)(int)owner<<50);
            return checked(Math.Max(floor,previous)+1);
        }
        internal static PlayableCommand CreateEightPlayerMovingPerformanceCommand(PlayableSnapshot view,long sequence)
        {
            if(view==null||sequence<1||!(view.ActiveProfile?.AuthoredMap is AiTestMap)||view.Participants.Count!=8||
                view.Participants.Any(p=>p.Control!=OfflineControl.Ai)||(int)view.Owner<0||(int)view.Owner>=view.Participants.Count||view.Participants[(int)view.Owner].Id!=view.OwnerId)
                throw new ArgumentException("Moving performance command requires the bound eight-AI field perspective.");
            var own=view.Entities.Where(e=>e.Owner==view.Owner&&e.Health>0).OrderBy(e=>e.Id).ToArray();
            if(own.Length!=50)throw new ArgumentException("Moving performance command requires exactly fifty own living units.");
            double x=own.Average(e=>e.Position.X),z=own.Average(e=>e.Position.Z),length=Math.Sqrt(x*x+z*z);
            if(!(length>0)||double.IsInfinity(length))throw new ArgumentException("Moving performance start must have a finite radial anchor.");
            // Tangential command keeps cohorts apart and uses the ordinary common-goal
            // formation/route path. This is scripted QA load, never a native AI decision.
            var target=new NavPoint(x-z/length*30,z+x/length*30);
            long action=1000000+(int)view.Owner; // Separate diagnostic action namespace.
            return new PlayableCommand(view.Generation,sequence,view.OwnerId,PlayableCommandKind.Move,own.Select(e=>e.Id).ToArray(),target,
                origin:PlayableOrderOrigin.Ai,source:MovingPerformanceSource,jobId:action,actionId:action);
        }
        public PlayableCommandSubmitResult SubmitEightPlayerMovingPerformanceEvidence(PlayableSnapshot view)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-eightPlayerMovingPerformanceEvidence")<0||!localSpectator||
                offlineConfiguration==null||offlineConfiguration.Roster.Count!=8||!(offlineConfiguration.Profile.AuthoredMap is AiTestMap))
                return new PlayableCommandSubmitResult(PlayableCommandStatus.Rejected);
            lock(gate){
                try{
                    long sequence=EightPlayerMovingPerformanceSequence(view?.Owner??PlayableOwner.Player,
                        view!=null&&acceptedByOwner.TryGetValue(view.OwnerId??"",out var last)?last:0);
                    var command=CreateEightPlayerMovingPerformanceCommand(view,sequence);
                    if(QaReadoutsRequested){domain.EnableNavigationStepsForQa();if(qaPerformanceReceipts==null)qaPerformanceReceipts=new QaPerformanceReceipts();qaPerformanceReceipts.Register(command);QaArrivalLifecycle.Register(command.Generation,command.Sequence);}
                    var result=TrySubmitAi(command);qaPerformanceReceipts?.Submitted(command,result.Status,view.Tick);
                    if(result.Accepted&&qaPerformanceReceipts?.Expected==8)QaAiRouteAudit.Arm(command.Generation,offlineConfiguration.Roster.Select(p=>p.Id).ToArray(),offlineConfiguration.Profile);return result;
                }catch(ArgumentException){return new PlayableCommandSubmitResult(PlayableCommandStatus.InvalidEntity);}
            }
        }

        public static PlayableRuntime CreateSmallFivePerformanceMatch(PlayableProfile profile,long generation,NativeLobbyConfiguration setup,bool startPaused=false,AiProfile aiProfile=null)
        {
            if(setup.Participants.Count!=2||setup.Spectator)throw new ArgumentException("Small performance QA requires two participant seats.");
            setup=setup.Copy();setup.PrepareMatchNames();var basis=NativeLobbyMatch.Create(profile,setup);
            var costs=new double[basis.Starts.Count,basis.Starts.Count];
            for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)costs[a,b]=basis.RouteCost(a,b);
            OfflineMatchConfiguration WithScenario(OfflineScenarioUnit[] units)=>new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,
                basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,basis.Spectators.ToArray(),units,profile.AuthoredMap.Id,basis.ScenarioBuildings.ToArray());
            var placed=new System.Collections.Generic.List<OfflineScenarioUnit>();
            var configuration=basis;
            double radius=PlayableUnitRules.Radius(profile,PlayableEntityKind.Tank),spacing=2*radius+.25;
            foreach(var seat in Enumerable.Range(0,basis.Roster.Count))for(int unit=0;unit<4;unit++){
                var home=basis.Starts[basis.Assignments[seat]].ExplorerAnchor;bool found=false;
                for(int ring=1;ring<=12&&!found;ring++)for(int z=-ring;z<=ring&&!found;z++)for(int x=-ring;x<=ring&&!found;x++){
                    if(Math.Max(Math.Abs(x),Math.Abs(z))!=ring)continue;
                    var position=new NavPoint(home.X+x*spacing,home.Z+z*spacing);
                    if(!profile.AuthoredMap.SupportsFootprint(position,radius))continue;
                    var candidate=new OfflineScenarioUnit(basis.Roster[seat].LogicalPlayer,PlayableEntityKind.Tank,position,0);
                    // The actual configuration constructor enforces geometry, every building
                    // envelope, all automatic explorers and every already placed unit.
                    try{configuration=WithScenario(placed.Concat(new[]{candidate}).ToArray());}
                    catch(ArgumentException){continue;}
                    placed.Add(candidate);found=true;
                }
                if(!found)throw new InvalidOperationException("No valid small-five QA placement near the assigned explorer.");
            }
            var runtime=new PlayableRuntime(configuration,generation,startPaused,setup.Difficulty,setup.Participants.Select(p=>p.Difficulty).ToArray(),aiProfile:aiProfile);
            runtime.lobbyConfiguration=setup;return runtime;
        }

        // Opt-in QA genesis. Runtime scheduling, AI, physics and commands remain ordinary.
        public static PlayableRuntime CreateEightPlayerPerformanceMatch(PlayableProfile profile,long generation,NativeLobbyConfiguration setup,bool startPaused=false,AiProfile aiProfile=null)
        {
            if(!(profile.AuthoredMap is AiTestMap)||setup.Participants==null||setup.Participants.Count!=8||!setup.Spectator)
                throw new ArgumentException("Eight-player performance QA requires the eight-seat open-ground map and eight AI owners.");
            setup=setup.Copy();setup.PrepareMatchNames();
            var basis=NativeLobbyMatch.Create(profile,setup);
            var scenario=basis.Roster.SelectMany((owner,seat)=>Enumerable.Range(0,49).Select(i=>{
                double angle=-Math.PI/2+basis.Assignments[seat]*Math.PI/4;
                double radial=88+(i/7-3)*2.2,tangent=(i%7-3)*2.2;
                return new OfflineScenarioUnit(owner.LogicalPlayer,PlayableEntityKind.Tank,
                    new NavPoint(Math.Cos(angle)*radial-Math.Sin(angle)*tangent,Math.Sin(angle)*radial+Math.Cos(angle)*tangent),angle+Math.PI);
            })).ToArray();
            var costs=new double[basis.Starts.Count,basis.Starts.Count];
            for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)costs[a,b]=basis.RouteCost(a,b);
            var configuration=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,
                basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,basis.Spectators.ToArray(),scenario,profile.AuthoredMap.Id,basis.ScenarioBuildings.ToArray());
            // Ordinary genesis supplies the fiftieth unit (the starting explorer).
            var runtime=new PlayableRuntime(configuration,generation,startPaused,setup.Difficulty,
                setup.Participants.Select(p=>p.Difficulty).ToArray(),spectator:true,aiProfile:aiProfile);
            runtime.lobbyConfiguration=setup;
            return runtime;
        }
    }
}
#endif
