using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation.Ai
{
    public enum AiOpeningGoal { Factory, Income, Expansion, Force, Intel, Pressure }
    public sealed class AiOpeningMilestone
    {
        public AiOpeningMilestone(string id,AiOpeningGoal goal,params string[] dependencies)
        {Id=id;Goal=goal;Dependencies=Array.AsReadOnly(dependencies);}
        public string Id{get;} public AiOpeningGoal Goal{get;} public IReadOnlyList<string> Dependencies{get;}
    }
    public sealed class AiOpeningDefinition
    {
        public AiOpeningDefinition(string id,PlayableAiOpening opening,string weight,IEnumerable<AiOpeningMilestone> milestones)
        {
            Id=id;Opening=opening;WeightPath=weight;Milestones=Array.AsReadOnly(milestones.ToArray());
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var m in Milestones)if(m.Dependencies.Any(d=>!seen.Contains(d))||!seen.Add(m.Id))throw new ArgumentException("Opening DAG must be topologically ordered with unique IDs.");
        }
        public string Id{get;} public PlayableAiOpening Opening{get;} public string WeightPath{get;}
        public IReadOnlyList<AiOpeningMilestone> Milestones{get;}
        public string Fallback=>"ordinary-macro-army";
    }
    // Executable plan data. Shared gameplay prices, routes and commands stay in their existing catalogs.
    public static class AiOpeningCatalog
    {
        public static IReadOnlyList<AiOpeningDefinition> All{get;}=Array.AsReadOnly(new[]{
            new AiOpeningDefinition("safe",PlayableAiOpening.Safe,"opening.weights.safe",new[]{new AiOpeningMilestone("factory",AiOpeningGoal.Factory),new AiOpeningMilestone("income",AiOpeningGoal.Income),new AiOpeningMilestone("force",AiOpeningGoal.Force,"factory","income")}),
            new AiOpeningDefinition("greedy",PlayableAiOpening.GreedySafe,"opening.weights.greedy",new[]{new AiOpeningMilestone("income",AiOpeningGoal.Income),new AiOpeningMilestone("factory",AiOpeningGoal.Factory,"income"),new AiOpeningMilestone("force",AiOpeningGoal.Force,"factory")}),
            new AiOpeningDefinition("expansion",PlayableAiOpening.GreedyMine,"opening.weights.expansion",new[]{new AiOpeningMilestone("expansion",AiOpeningGoal.Expansion),new AiOpeningMilestone("factory",AiOpeningGoal.Factory),new AiOpeningMilestone("force",AiOpeningGoal.Force,"factory","expansion")}),
            new AiOpeningDefinition("blind-rush",PlayableAiOpening.BlindRush,"opening.weights.blindRush",new[]{new AiOpeningMilestone("factory",AiOpeningGoal.Factory),new AiOpeningMilestone("force",AiOpeningGoal.Force,"factory"),new AiOpeningMilestone("pressure",AiOpeningGoal.Pressure,"force")}),
            new AiOpeningDefinition("scout-led",PlayableAiOpening.ExplorerAllIn,"opening.weights.scoutPressure",new[]{new AiOpeningMilestone("factory",AiOpeningGoal.Factory),new AiOpeningMilestone("intel",AiOpeningGoal.Intel),new AiOpeningMilestone("force",AiOpeningGoal.Force,"factory"),new AiOpeningMilestone("pressure",AiOpeningGoal.Pressure,"force","intel")})});
        public static AiOpeningDefinition For(PlayableAiOpening opening)=>All.Single(d=>d.Opening==(opening==PlayableAiOpening.DoubleMineExplorerRush?PlayableAiOpening.GreedyMine:opening));
    }
}
