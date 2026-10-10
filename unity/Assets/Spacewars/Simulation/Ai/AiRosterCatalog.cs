using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Spacewars.Simulation.Ai
{
    public enum AiMobilityClass { GroundVehicle, SmallGroundVehicle }
    [Serializable] public sealed class AiRosterDescriptorData
    {
        public PlayableEntityKind kind;
        public double reconWeight,lineWeight,supportWeight,scoutingUtility;
        public string[] targetTags;
        public AiMobilityClass mobility;
        public PlayableBuildingKind producer;
        public PlayableCommandKind productionCommand;
        public PlayableResearchKind[] unlockDependencies;
        public string exclusionReason;
    }
    public sealed class AiRosterCatalog
    {
        private readonly AiRosterDescriptorData[] descriptors;
        public string Hash {get;}
        public static AiRosterCatalog Initial=>new AiRosterCatalog(new[]{
            Descriptor(PlayableEntityKind.Explorer,1,0,0,1,AiMobilityClass.SmallGroundVehicle,PlayableCommandKind.QueueExplorer),
            Descriptor(PlayableEntityKind.Tank,0,1,0,0,AiMobilityClass.GroundVehicle,PlayableCommandKind.QueueTank),
            Descriptor(PlayableEntityKind.Shkval,0,0,1,0,AiMobilityClass.GroundVehicle,PlayableCommandKind.QueueShkval)});
        private static AiRosterDescriptorData Descriptor(PlayableEntityKind kind,double recon,double line,double support,double scouting,AiMobilityClass mobility,PlayableCommandKind command)=>new AiRosterDescriptorData{
            kind=kind,reconWeight=recon,lineWeight=line,supportWeight=support,scoutingUtility=scouting,mobility=mobility,producer=PlayableBuildingKind.Factory,
            productionCommand=command,targetTags=new[]{"ground","building"},unlockDependencies=new PlayableResearchKind[0]};
        public static IReadOnlyList<AiProfileField> Metadata {get;}=Array.AsReadOnly(Enum.GetValues(typeof(PlayableEntityKind)).Cast<PlayableEntityKind>().SelectMany(kind=>
            new[]{"reconWeight","lineWeight","supportWeight","scoutingUtility"}.Select(role=>new AiProfileField("roster."+kind+"."+role,"ИИ / Роли",kind+" / "+role,
                "Относительный вес роли; игровые характеристики берутся из gameplay catalog.","ratio",0,1,.05,InitialWeight(kind,role),false))).ToArray());
        private static double InitialWeight(PlayableEntityKind kind,string role)=>kind==PlayableEntityKind.Explorer?(role=="reconWeight"||role=="scoutingUtility"?1:0):kind==PlayableEntityKind.Tank?(role=="lineWeight"?1:0):(role=="supportWeight"?1:0);
        public AiRosterCatalog(IEnumerable<AiRosterDescriptorData> input)
        {
            if(input==null)throw new ArgumentNullException(nameof(input));descriptors=input.Select(Copy).OrderBy(d=>(int)d.kind).ToArray();
            if(descriptors.Select(d=>d.kind).Distinct().Count()!=descriptors.Length||!descriptors.Select(d=>d.kind).SequenceEqual(Enum.GetValues(typeof(PlayableEntityKind)).Cast<PlayableEntityKind>().OrderBy(k=>(int)k)))throw new ArgumentException("Every playable kind needs one descriptor or documented exclusion.");
            foreach(var d in descriptors)
            {
                if(!Enum.IsDefined(typeof(AiMobilityClass),d.mobility)||d.targetTags==null||d.targetTags.Length==0||d.targetTags.Any(string.IsNullOrWhiteSpace)||d.targetTags.Distinct().Count()!=d.targetTags.Length||d.unlockDependencies==null||d.unlockDependencies.Distinct().Count()!=d.unlockDependencies.Length||d.unlockDependencies.Any(r=>!Enum.IsDefined(typeof(PlayableResearchKind),r)))throw new ArgumentException("Invalid roster metadata.");
                var weights=new[]{d.reconWeight,d.lineWeight,d.supportWeight,d.scoutingUtility};
                int i=0;foreach(var field in Metadata.Where(f=>f.Path.StartsWith("roster."+d.kind+".")))field.Validate(weights[i++]);
                if(string.IsNullOrWhiteSpace(d.exclusionReason)&&(!PlayableUnitRules.Supported(d.kind)||d.reconWeight+d.lineWeight+d.supportWeight<=0||d.producer!=PlayableBuildingKind.Factory||d.productionCommand!=Command(d.kind)))throw new ArgumentException("Missing role or production adapter.");
            }
            Hash=AiProfile.Digest(string.Join("\n",descriptors.Select(d=>d.kind+"|"+d.mobility+"|"+d.producer+"|"+d.productionCommand+"|"+string.Join(",",d.targetTags.OrderBy(t=>t,StringComparer.Ordinal))+"|"+string.Join(",",d.unlockDependencies.OrderBy(r=>(int)r))+"|"+(d.exclusionReason??"")+"|"+string.Join(",",new[]{d.reconWeight,d.lineWeight,d.supportWeight,d.scoutingUtility}.Select(x=>x.ToString("R",CultureInfo.InvariantCulture))))));
        }
        public AiRosterDescriptorData[] CopyData()=>descriptors.Select(Copy).ToArray();
        public AiRosterDescriptorData For(PlayableEntityKind kind)=>Copy(descriptors.Single(d=>d.kind==kind));
        public static PlayableCommandKind Command(PlayableEntityKind kind)
        {switch(kind){case PlayableEntityKind.Explorer:return PlayableCommandKind.QueueExplorer;case PlayableEntityKind.Tank:return PlayableCommandKind.QueueTank;case PlayableEntityKind.Shkval:return PlayableCommandKind.QueueShkval;default:throw new ArgumentException("Unknown kind.");}}
        // The adapter is registered once per supported gameplay mechanic. Strategies use
        // descriptors; they do not decode commands or duplicate gameplay terms.
        public AiRosterDescriptorData Production(PlayableCommandKind command)=>descriptors
            .Where(d=>string.IsNullOrWhiteSpace(d.exclusionReason)&&d.productionCommand==command).Select(Copy).SingleOrDefault();
        public double CreditCost(PlayableEntityKind kind,PlayableProfile gameplay)
        {For(kind);return PlayableUnitRules.Cost(gameplay,kind);}
        public int PopulationCost(PlayableEntityKind kind,PlayableProfile gameplay)
        {For(kind);return PlayableUnitRules.Population(gameplay,kind);}
        public double ProductionSeconds(PlayableEntityKind kind,PlayableProfile gameplay)
        {For(kind);return PlayableUnitRules.Duration(gameplay,kind);}
        private static AiRosterDescriptorData Copy(AiRosterDescriptorData d)
        {if(d==null)throw new ArgumentException("Null descriptor.");return new AiRosterDescriptorData{kind=d.kind,reconWeight=d.reconWeight,lineWeight=d.lineWeight,supportWeight=d.supportWeight,scoutingUtility=d.scoutingUtility,mobility=d.mobility,producer=d.producer,productionCommand=d.productionCommand,targetTags=d.targetTags?.ToArray(),unlockDependencies=d.unlockDependencies?.ToArray(),exclusionReason=d.exclusionReason};}
    }
}
