namespace Spacewars.Simulation
{
    // Actual authority evidence, separate from accepted command and transient idle.
    // Exact original anchor and assigned arrival, independent of transient idle.
    public sealed class PlayableSpatialCompletion
    {
        public const string FlatSurface=NavLocation.FlatSurface;
        public PlayableSpatialCompletion(PlayableOrderStamp order,long groupId,long incarnation,NavPoint originalTarget,string surfaceId,long activationTick,long completionTick)
        :this(order,groupId,incarnation,new NavLocation(originalTarget,surfaceId),new NavLocation(originalTarget,surfaceId),activationTick,completionTick){}
        public PlayableSpatialCompletion(PlayableOrderStamp order,long groupId,long incarnation,NavLocation original,NavLocation assigned,long activationTick,long completionTick)
        {Order=order;GroupId=groupId;Incarnation=incarnation;OriginalLocation=original;AssignedLocation=assigned;ActivationTick=activationTick;CompletionTick=completionTick;}
        public PlayableOrderStamp Order{get;}
        public long GroupId{get;} public long Incarnation{get;}
        public NavLocation OriginalLocation{get;} public NavLocation AssignedLocation{get;}
        public NavPoint OriginalTarget=>OriginalLocation.Position; public string SurfaceId=>OriginalLocation.SurfaceId;
        public long ActivationTick{get;} public long CompletionTick{get;}
    }
}
