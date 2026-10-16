using System;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        // Concrete observation of the existing short-circuit predicate. It is
        // carried only by a paid preparation, never by saved gameplay state.
        internal readonly struct NavigationRetentionSource
        {
            internal readonly object Queue,Active,Unit,Order;internal readonly bool HasQueue,HasUnit;
            internal NavigationRetentionSource(object queue,object active,object unit,object order,bool hasQueue,bool hasUnit){Queue=queue;Active=active;Unit=unit;Order=order;HasQueue=hasQueue;HasUnit=hasUnit;}
            internal bool Keep=>HasQueue&&(Active!=null||HasUnit&&Order!=null);
        }
        internal bool IsNavigationRetentionCallback(Func<int,bool> callback)=>callback==(Func<int,bool>)RetainsInstalledExecution;
        internal NavigationRetentionSource ObserveNavigationRetention(int id)
        {
            bool hasQueue=tacticalQueues.TryGetValue(id,out var queue);if(!hasQueue)return new NavigationRetentionSource(null,null,null,null,false,false);
            if(queue.Active!=null)return new NavigationRetentionSource(queue,queue.Active,null,null,true,false);
            bool hasUnit=units.TryGetValue(id,out var unit);return new NavigationRetentionSource(queue,null,unit,hasUnit?unit.CurrentOrder:null,true,hasUnit);
        }
        internal bool NavigationRetentionCurrent(int id,NavigationRetentionSource source)
        {
            bool hasQueue=tacticalQueues.TryGetValue(id,out var queue);if(hasQueue!=source.HasQueue||!ReferenceEquals(queue,source.Queue))return false;if(!hasQueue)return true;
            if(!ReferenceEquals(queue.Active,source.Active))return false;if(source.Active!=null)return true;
            bool hasUnit=units.TryGetValue(id,out var unit);return hasUnit==source.HasUnit&&ReferenceEquals(unit,source.Unit)&&(!hasUnit||ReferenceEquals(unit.CurrentOrder,source.Order));
        }
    }
}
