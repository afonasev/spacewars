namespace Spacewars.Runtime
{
    internal sealed partial class PlayableAiOwnerLoop
    {
        // Derived from the active difficulty, not a new gameplay delay.
        internal long StartupPendingDeliveryTick
        {get{long last=StartupReviewTick;foreach(var item in pendingItems)last=System.Math.Max(last,item.DueTick);return last;}}
    }
}
