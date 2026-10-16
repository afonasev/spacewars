using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    // Root physical charge already includes its 16-byte intrusive capture
    // header: the conservative 136-byte managed bound fits Charge(128)=144.
    internal sealed partial class NavPersistentQueue<T> : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>IsRegistered&&frozen&&!faulted&&reserve==null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.PersistentQueue,value:NavPhysicalValue<T>.Kind);
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(128)||NavStorageLayout.ObjectBytesBound(GetType())>NavStorageLayout.Charge(128))throw new ArgumentException("Physical persistent queue callback/layout contradiction.");
            writer.Output.Write(nextId);writer.Output.Write(FirstId);writer.Output.Write(LastId);NavPhysicalMacro.WriteEdge(writer,this,records);
        }
        private NavPersistentQueue(bool physical):base(128){frozen=true;}
        internal static NavPersistentQueue<T> ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            long next=reader.Input.ReadInt64(),first=reader.Input.ReadInt64(),last=reader.Input.ReadInt64();var records=NavPhysicalMacro.ReadEdge<NavCowDictionary<long,Record>>(reader);
            if(records.HasPhysicalInsertionOrder||next<1||first<0||last<0||first>=next||last>=next||records.Count==0&&(first!=0||last!=0)||records.Count!=0&&(first==0||last==0)||NavStorageLayout.ObjectBytesBound(typeof(NavPersistentQueue<T>))>NavStorageLayout.Charge(128))throw new ArgumentException("Physical persistent queue dimensions/endpoints contradiction.");
            // A bounded walk proves complete coverage without a rebuilt ID map.
            int visited=0;long previous=0,at=first;while(at!=0){if(++visited>records.Count||!records.TryGetValue(at,out var record)||record==null||at<1||at>=next||record.Previous!=previous||record.Next<0||record.Next>=next)throw new ArgumentException("Physical persistent queue link/cycle contradiction.");previous=at;at=record.Next;}
            if(visited!=records.Count||previous!=last)throw new ArgumentException("Physical persistent queue coverage/tail contradiction.");
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(128);var result=allocation.Register(new NavPersistentQueue<T>(true){nextId=next,FirstId=first,LastId=last});result.records=NavPhysicalMacro.Acquire(result,records);return result;
            }
        }
        internal sealed partial class Record : INavPhysicalBody
        {
            internal override bool IsImmutableCaptureRoot=>IsRegistered&&acquired;
            public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.QueueRecord,value:NavPhysicalValue<T>.Kind);
            public void WritePhysicalBody(NavPhysicalBodyWriter writer)
            {
                if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(Record.PhysicalBodyBytes)||NavStorageLayout.ObjectBytesBound(GetType())>Record.PhysicalBodyBytes)throw new ArgumentException("Physical queue record ownership/layout contradiction.");
                writer.Output.Write(Previous);writer.Output.Write(Next);NavStoragePayload<T>.Validate(Value,this);NavPhysicalValue<T>.Write(writer,Value);
            }
            internal static Record ReadPhysicalBody(NavPhysicalBodyReader reader)
            {
                long previous=reader.Input.ReadInt64(),next=reader.Input.ReadInt64();var value=NavPhysicalValue<T>.Read(reader);if(previous<0||next<0||NavStorageLayout.ObjectBytesBound(typeof(Record))>Record.PhysicalBodyBytes)throw new ArgumentException("Invalid physical queue record links/layout.");
                using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){allocation.Reserve(Record.PhysicalBodyBytes);var result=allocation.Register(new Record(previous,next,value));result.Acquire();return result;}
            }
        }
    }
    internal sealed partial class NavPersistentIndex<TKey,TValue> : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>IsRegistered&&frozen&&!faulted&&reserve==null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Index,NavPhysicalValue<TKey>.Kind,NavPhysicalValue<TValue>.Kind);
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(128)||NavStorageLayout.ObjectBytesBound(GetType())>NavStorageLayout.Charge(128))throw new ArgumentException("Physical persistent index callback/layout contradiction.");
            writer.Output.Write(nextSlot);writer.Output.Write(freeHead);NavPhysicalMacro.WriteEdge(writer,this,lookup);NavPhysicalMacro.WriteEdge(writer,this,slots);
        }
        private NavPersistentIndex(bool physical):base(128){frozen=true;}
        private static bool SamePhysicalKey(TKey left,TKey right)
        {
            if(typeof(TKey)==typeof(NavNameKey)){var a=(NavNameKey)(object)left;var b=(NavNameKey)(object)right;return ReferenceEquals(a.Value,b.Value)&&ReferenceEquals(a.Reference.Owner,b.Reference.Owner)&&a.Reference.Role==b.Reference.Role&&a.Reference.Index==b.Reference.Index;}
            return EqualityComparer<TKey>.Default.Equals(left,right);
        }
        internal static NavPersistentIndex<TKey,TValue> ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            int next=reader.Count(NavPhysicalCaptureIndex.MaxDefinitions),free=reader.Input.ReadInt32();var lookup=NavPhysicalMacro.ReadEdge<NavCowDictionary<TKey,int>>(reader);var slots=NavPhysicalMacro.ReadEdge<NavCowDictionary<int,Slot>>(reader);
            if(lookup.HasPhysicalInsertionOrder||slots.HasPhysicalInsertionOrder||free<-1||free>=next||slots.Count!=next||lookup.Count>next||NavStorageLayout.ObjectBytesBound(typeof(NavPersistentIndex<TKey,TValue>))>NavStorageLayout.Charge(128))throw new ArgumentException("Physical persistent index slot dimensions/layout contradiction.");
            using(var scratch=NavStorageScratch.Reserve(reader.Reservation,checked(8192L+4L*next))){
                var seen=new int[next];int freeCount=0;for(int at=free;at!=-1;){if(at<0||at>=next||seen[at]!=0||!slots.TryGetValue(at,out var slot)||slot==null||slot.Live)throw new ArgumentException("Physical persistent index free-list/cycle contradiction.");seen[at]=1;freeCount++;at=slot.FreeNext;}
                int live=0;for(int n=0;n<next;n++){if(!slots.TryGetValue(n,out var slot)||slot==null)throw new ArgumentException("Missing physical persistent index allocated slot.");if(slot.Live){if(seen[n]!=0||!lookup.TryGetValue(slot.Key,out int id)||id!=n)throw new ArgumentException("Physical persistent index lookup/slot contradiction.");live++;}else if(seen[n]!=1)throw new ArgumentException("Physical persistent index orphan free slot.");}
                foreach(var pair in lookup)if(pair.Value<0||pair.Value>=next||!slots[pair.Value].Live||!SamePhysicalKey(pair.Key,slots[pair.Value].Key))throw new ArgumentException("Physical persistent index reverse lookup contradiction.");
                if(live!=lookup.Count||live+freeCount!=next)throw new ArgumentException("Physical persistent index coverage contradiction.");
            }
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){allocation.Reserve(128);var result=allocation.Register(new NavPersistentIndex<TKey,TValue>(true){nextSlot=next,freeHead=free});result.lookup=NavPhysicalMacro.Acquire(result,lookup);result.slots=NavPhysicalMacro.Acquire(result,slots);return result;}
        }
        internal sealed partial class Slot : INavPhysicalBody
        {
            internal override bool IsImmutableCaptureRoot=>IsRegistered&&(!Live||acquired);
            public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.IndexSlot,NavPhysicalValue<TKey>.Kind,NavPhysicalValue<TValue>.Kind);
            public void WritePhysicalBody(NavPhysicalBodyWriter writer)
            {
                if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(SlotBytes)||NavStorageLayout.ObjectBytesBound(GetType())>SlotBytes)throw new ArgumentException("Physical persistent slot ownership/layout contradiction.");
                writer.Output.Write(Live);writer.Output.Write(FreeNext);NavStoragePayload<TKey>.Validate(Key,this);NavStoragePayload<TValue>.Validate(Value,this);NavPhysicalValue<TKey>.Write(writer,Key);NavPhysicalValue<TValue>.Write(writer,Value);NavPhysicalMacro.WriteEdge(writer,this,metadata,true);
            }
            internal static Slot ReadPhysicalBody(NavPhysicalBodyReader reader)
            {
                bool live=NavPhysicalMacro.ReadBoolean(reader);int next=reader.Input.ReadInt32();var key=NavPhysicalValue<TKey>.Read(reader);var value=NavPhysicalValue<TValue>.Read(reader);var metadata=NavPhysicalMacro.ReadEdge<NavOwnedStorage>(reader,true);
                if(next<-1||live&&next!=-1||!live&&(!EqualityComparer<TKey>.Default.Equals(key,default)||!EqualityComparer<TValue>.Default.Equals(value,default)||metadata!=null)||NavStorageLayout.ObjectBytesBound(typeof(Slot))>SlotBytes)throw new ArgumentException("Physical persistent slot live/default/layout contradiction.");
                using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){allocation.Reserve(SlotBytes);var result=allocation.Register(live?new Slot(key,value,metadata):new Slot(next));if(live)result.Acquire();return result;}
            }
        }
    }
    internal static partial class NavPhysicalBodyFactory
    {
        private static bool TryReadPersistent(NavPhysicalTag tag,NavPhysicalBodyReader reader,out NavOwnedStorage value)
        {
            value=null;
            if(tag.Key==NavPhysicalValueKind.NameKey&&tag.Value==NavPhysicalValueKind.ArrivalPreparedClearance&&(tag.Storage==NavPhysicalStorageKind.Index||tag.Storage==NavPhysicalStorageKind.IndexSlot)){value=tag.Storage==NavPhysicalStorageKind.Index?(NavOwnedStorage)NavPersistentIndex<NavNameKey,NavigationArrivalWork.PreparedClearance>.ReadPhysicalBody(reader):NavPersistentIndex<NavNameKey,NavigationArrivalWork.PreparedClearance>.Slot.ReadPhysicalBody(reader);return true;}
            if((tag.Storage==NavPhysicalStorageKind.Map||tag.Storage==NavPhysicalStorageKind.AvlNode)&&tag.Key==NavPhysicalValueKind.Integer&&tag.Value==NavPhysicalValueKind.NamePreparedIndexSlot){value=ReadMap<int,NavPersistentIndex<NavNameKey,NavigationArrivalWork.PreparedClearance>.Slot>(tag,reader);return true;}
            if(tag.Key==NavPhysicalValueKind.Integer&&tag.Value==NavPhysicalValueKind.ArrivalRouteRow&&(tag.Storage==NavPhysicalStorageKind.Index||tag.Storage==NavPhysicalStorageKind.IndexSlot)){value=tag.Storage==NavPhysicalStorageKind.Index?(NavOwnedStorage)NavPersistentIndex<int,NavigationArrivalWork.RouteRow>.ReadPhysicalBody(reader):NavPersistentIndex<int,NavigationArrivalWork.RouteRow>.Slot.ReadPhysicalBody(reader);return true;}
            if((tag.Storage==NavPhysicalStorageKind.Map||tag.Storage==NavPhysicalStorageKind.AvlNode)&&tag.Key==NavPhysicalValueKind.Integer&&tag.Value==NavPhysicalValueKind.IntegerArrivalRowIndexSlot){value=ReadMap<int,NavPersistentIndex<int,NavigationArrivalWork.RouteRow>.Slot>(tag,reader);return true;}
            if(tag.Key==NavPhysicalValueKind.Long&&tag.Value==NavPhysicalValueKind.ArrivalStatus&&(tag.Storage==NavPhysicalStorageKind.Index||tag.Storage==NavPhysicalStorageKind.IndexSlot)){value=tag.Storage==NavPhysicalStorageKind.Index?(NavOwnedStorage)NavPersistentIndex<long,NavArrivalStatus>.ReadPhysicalBody(reader):NavPersistentIndex<long,NavArrivalStatus>.Slot.ReadPhysicalBody(reader);return true;}
            if((tag.Storage==NavPhysicalStorageKind.Map||tag.Storage==NavPhysicalStorageKind.AvlNode)&&tag.Key==NavPhysicalValueKind.Integer&&tag.Value==NavPhysicalValueKind.LongArrivalStatusIndexSlot){value=ReadMap<int,NavPersistentIndex<long,NavArrivalStatus>.Slot>(tag,reader);return true;}

            if(tag.Key==NavPhysicalValueKind.None&&(tag.Storage==NavPhysicalStorageKind.PersistentQueue||tag.Storage==NavPhysicalStorageKind.QueueRecord)){
                switch(tag.Value){case NavPhysicalValueKind.Job:value=tag.Storage==NavPhysicalStorageKind.PersistentQueue?(NavOwnedStorage)NavPersistentQueue<LayeredNavigationScheduler.Job>.ReadPhysicalBody(reader):NavPersistentQueue<LayeredNavigationScheduler.Job>.Record.ReadPhysicalBody(reader);return true;
                    case NavPhysicalValueKind.Completion:value=tag.Storage==NavPhysicalStorageKind.PersistentQueue?(NavOwnedStorage)NavPersistentQueue<NavScheduledCompletion>.ReadPhysicalBody(reader):NavPersistentQueue<NavScheduledCompletion>.Record.ReadPhysicalBody(reader);return true;
                    case NavPhysicalValueKind.CacheField:value=tag.Storage==NavPhysicalStorageKind.PersistentQueue?(NavOwnedStorage)NavPersistentQueue<LayeredNavigationScheduler.CacheField>.ReadPhysicalBody(reader):NavPersistentQueue<LayeredNavigationScheduler.CacheField>.Record.ReadPhysicalBody(reader);return true;
                    case NavPhysicalValueKind.ProviderReference:value=tag.Storage==NavPhysicalStorageKind.PersistentQueue?(NavOwnedStorage)NavPersistentQueue<NavProviderReferences.Entry>.ReadPhysicalBody(reader):NavPersistentQueue<NavProviderReferences.Entry>.Record.ReadPhysicalBody(reader);return true;
                    case NavPhysicalValueKind.Integer:value=tag.Storage==NavPhysicalStorageKind.PersistentQueue?(NavOwnedStorage)NavPersistentQueue<int>.ReadPhysicalBody(reader):NavPersistentQueue<int>.Record.ReadPhysicalBody(reader);return true;
                    case NavPhysicalValueKind.NameKey:value=tag.Storage==NavPhysicalStorageKind.PersistentQueue?(NavOwnedStorage)NavPersistentQueue<NavNameKey>.ReadPhysicalBody(reader):NavPersistentQueue<NavNameKey>.Record.ReadPhysicalBody(reader);return true;
                    case NavPhysicalValueKind.Request:value=tag.Storage==NavPhysicalStorageKind.PersistentQueue?(NavOwnedStorage)NavPersistentQueue<NavScheduledRequest>.ReadPhysicalBody(reader):NavPersistentQueue<NavScheduledRequest>.Record.ReadPhysicalBody(reader);return true;
                    case NavPhysicalValueKind.TypedLeg:value=tag.Storage==NavPhysicalStorageKind.PersistentQueue?(NavOwnedStorage)NavPersistentQueue<NavTypedLeg>.ReadPhysicalBody(reader):NavPersistentQueue<NavTypedLeg>.Record.ReadPhysicalBody(reader);return true;}
            }
            if(tag.Key==NavPhysicalValueKind.NameKey&&(tag.Storage==NavPhysicalStorageKind.Index||tag.Storage==NavPhysicalStorageKind.IndexSlot)){
                switch(tag.Value){case NavPhysicalValueKind.Long:value=tag.Storage==NavPhysicalStorageKind.Index?(NavOwnedStorage)NavPersistentIndex<NavNameKey,long>.ReadPhysicalBody(reader):NavPersistentIndex<NavNameKey,long>.Slot.ReadPhysicalBody(reader);return true;
                    case NavPhysicalValueKind.SubscriberLink:value=tag.Storage==NavPhysicalStorageKind.Index?(NavOwnedStorage)NavPersistentIndex<NavNameKey,NavSubscriberLink>.ReadPhysicalBody(reader):NavPersistentIndex<NavNameKey,NavSubscriberLink>.Slot.ReadPhysicalBody(reader);return true;}
            }
            if(tag.Storage==NavPhysicalStorageKind.Map||tag.Storage==NavPhysicalStorageKind.AvlNode){
                if(tag.Key==NavPhysicalValueKind.Unsigned&&tag.Value==NavPhysicalValueKind.ProviderReferenceQueue){value=ReadMap<uint,NavPersistentQueue<NavProviderReferences.Entry>>(tag,reader);return true;}
                if(tag.Key==NavPhysicalValueKind.Long){switch(tag.Value){case NavPhysicalValueKind.JobQueueRecord:value=ReadMap<long,NavPersistentQueue<LayeredNavigationScheduler.Job>.Record>(tag,reader);return true;case NavPhysicalValueKind.CompletionQueueRecord:value=ReadMap<long,NavPersistentQueue<NavScheduledCompletion>.Record>(tag,reader);return true;case NavPhysicalValueKind.CacheFieldQueueRecord:value=ReadMap<long,NavPersistentQueue<LayeredNavigationScheduler.CacheField>.Record>(tag,reader);return true;case NavPhysicalValueKind.ProviderReferenceQueueRecord:value=ReadMap<long,NavPersistentQueue<NavProviderReferences.Entry>.Record>(tag,reader);return true;case NavPhysicalValueKind.IntegerQueueRecord:value=ReadMap<long,NavPersistentQueue<int>.Record>(tag,reader);return true;case NavPhysicalValueKind.NameQueueRecord:value=ReadMap<long,NavPersistentQueue<NavNameKey>.Record>(tag,reader);return true;case NavPhysicalValueKind.RequestQueueRecord:value=ReadMap<long,NavPersistentQueue<NavScheduledRequest>.Record>(tag,reader);return true;case NavPhysicalValueKind.LegQueueRecord:value=ReadMap<long,NavPersistentQueue<NavTypedLeg>.Record>(tag,reader);return true;}}
                if(tag.Key==NavPhysicalValueKind.Integer){switch(tag.Value){case NavPhysicalValueKind.NameLongIndexSlot:value=ReadMap<int,NavPersistentIndex<NavNameKey,long>.Slot>(tag,reader);return true;case NavPhysicalValueKind.NameSubscriberIndexSlot:value=ReadMap<int,NavPersistentIndex<NavNameKey,NavSubscriberLink>.Slot>(tag,reader);return true;}}
            }
            return false;
        }
    }
}
