namespace Spacewars.Simulation.Ai
{
    public sealed class AiReceiptIdentity
    {
        public AiReceiptIdentity(long generation,string ownerId,long decisionOrdinal,int actionOrdinal)
        {if(generation<1||string.IsNullOrWhiteSpace(ownerId)||decisionOrdinal<1||actionOrdinal<1)throw new System.ArgumentException("Invalid receipt identity.");Generation=generation;OwnerId=ownerId;DecisionOrdinal=decisionOrdinal;ActionOrdinal=actionOrdinal;}
        public long Generation{get;} public string OwnerId{get;} public long DecisionOrdinal{get;} public int ActionOrdinal{get;}
        public string Id=>Generation+":"+OwnerId.Length+":"+OwnerId+":"+DecisionOrdinal+":"+ActionOrdinal;
    }

}
