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
            Plan("double-income",PlayableAiOpening.Safe,"doubleIncome"),
            Plan("greedy-ground",PlayableAiOpening.GreedySafe,"greedy"),
            Plan("aggressive",PlayableAiOpening.BlindRush,"aggressive"),
            Plan("all-in",PlayableAiOpening.ExplorerAllIn,"allIn"),
            Plan("air",PlayableAiOpening.Air,"air")});
        private static AiOpeningDefinition Plan(string id,PlayableAiOpening opening,string weight)=>new AiOpeningDefinition(id,opening,"openingsMidgame.opening."+weight,new[]{
            new AiOpeningMilestone("income",AiOpeningGoal.Income),new AiOpeningMilestone("factory",AiOpeningGoal.Factory,"income"),
            new AiOpeningMilestone("force",AiOpeningGoal.Force,"factory"),new AiOpeningMilestone("pressure",AiOpeningGoal.Pressure,"force")});
        // Historical enum names remain decodable, but cannot select retired expansion/scout behavior.
        public static AiOpeningDefinition For(PlayableAiOpening opening)=>All.Single(d=>d.Opening==
            (opening==PlayableAiOpening.GreedyMine||opening==PlayableAiOpening.DoubleMineExplorerRush?PlayableAiOpening.GreedySafe:opening));
    }
}
