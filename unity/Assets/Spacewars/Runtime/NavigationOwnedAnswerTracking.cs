using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        // Bootstrapped jointly with the first qualified physical completion.
        // A drained table retains its exact spare slots and free-stack history.
        private NavigationCommandSlots<NavigationAnswer,NavigationHostAnswer> ownedAnswerTracking;
        private bool IsCorridorAnswerTracked(NavigationAnswer answer)
            =>ownedAnswerTracking?.ContainsKey(answer)==true||countedCorridorAnswers.Contains(answer);
        private int OwnedAnswerTrackingSlot(NavigationAnswer answer)
            =>answer?.StorageOwner==null||ownedAnswerTracking==null?-1:ownedAnswerTracking.FindOwnedSlot(answer,answer.StorageOwner,answer.StorageOwner);
        // The caller validated the source root/revision and extraction slot.
        // Transfer its owning edge into a paid carrier; never Release here.
        private NavOwnedStorage ExtractTrackedAnswerNoThrow(NavigationAnswer answer,int slot)
        {
            if(slot<0){countedCorridorAnswers.Remove(answer);return null;}
            ownedAnswerTracking.ExtractPreparedNoThrow(slot,out _,out var edge);return edge;
        }
        // The caller has guarded root identity/revision and owns a paid slot
        // for the transferred edge. No owner is released at this boundary.
        private NavOwnedStorage ExtractAnswerChargeNoThrow(NavigationAnswer answer)
        {
            bool tracked=IsCorridorAnswerTracked(answer);
            var retired=ExtractTrackedAnswerNoThrow(answer,OwnedAnswerTrackingSlot(answer));
            if(tracked)answerCorridorBytes-=answer.Corridor.EstimatedRetainedBytes;
            return retired;
        }
        private void ForgetCorridorChargeCompatibility(NavigationAnswer answer)
        {
            int slot=OwnedAnswerTrackingSlot(answer);
            if(slot>=0){var retired=ExtractTrackedAnswerNoThrow(answer,slot);answerCorridorBytes-=answer.Corridor.EstimatedRetainedBytes;retired?.Release();}
            else if(countedCorridorAnswers.Remove(answer))answerCorridorBytes-=answer.Corridor.EstimatedRetainedBytes;
        }
    }
}
