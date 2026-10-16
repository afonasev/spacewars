using System;

namespace Spacewars.Simulation
{
    internal sealed partial class NavCowOrderedSet<T> : INavPhysicalBody
    {
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Queue,value:NavPhysicalValue<T>.Kind);
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)=>writer.Edge(values);
        internal override bool IsImmutableCaptureRoot=>values.IsImmutableCaptureRoot;
        private NavCowOrderedSet(NavCowDictionary<T,bool> values):base(64){this.values=values;}
        internal static NavCowOrderedSet<T> ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var map=reader.Edge<NavCowDictionary<T,bool>>();if(map==null)throw new ArgumentException("Missing physical ordered-set map.");
            foreach(var pair in map)if(!pair.Value)throw new ArgumentException("Invalid physical ordered-set value.");
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(64);var result=allocation.Register(new NavCowOrderedSet<T>(map));result.RequireEdge(map);map.Retain();return result;
            }
        }
    }
    internal sealed partial class NavPackedSlots : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>frozen;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.PackedSlots);
        public void WritePhysicalBody(NavPhysicalBodyWriter writer){if(!frozen)throw new ArgumentException("Packed slots must be sealed.");writer.Edge(pages);}
        private NavPackedSlots(bool physical):base(128){frozen=true;}
        internal static NavPackedSlots ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var directory=reader.Edge<NavStorageDirectory<int>>();
            if(directory==null||directory.Count!=directory.AllocatedPages)throw new ArgumentException("Packed slots sparse count contradiction.");
            NavPhysicalDefaults.DirectoryCapacity(directory.Capacity,directory.Count);
            for(int n=0;n<directory.Count;n++)if(directory.Get(n)==null||directory.Get(n).Values.Length!=PageSize||directory.Get(n).physicalWidth!=4)throw new ArgumentException("Packed slots page shape contradiction.");
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(128);var result=allocation.Register(new NavPackedSlots(true));result.RequireEdge(directory);directory.Retain();result.pages=directory;return result;
            }
        }
    }
    internal sealed partial class NavFieldFrontier : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>frozen;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.FieldFrontier);
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!frozen)throw new ArgumentException("Field frontier must be sealed.");
            writer.Output.Write(nodes);writer.Output.Write(candidates);writer.Output.Write(maxLabels);writer.Output.Write(count);writer.Output.Write(heapPages);writer.Output.Write(positionPages);
            writer.Edge(legacy);writer.Edge(heap);writer.Edge(positions);
        }
        private NavFieldFrontier(int nodes,int candidates,int maxLabels):base(256)
        {this.nodes=nodes;this.candidates=candidates;this.maxLabels=maxLabels;frozen=true;}
        internal static NavFieldFrontier ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            int nodes=reader.Count(int.MaxValue),candidates=reader.Count(int.MaxValue),maximum=reader.Count(int.MaxValue),count=reader.Count(maximum),heapPages=reader.Count(NavPhysicalCaptureIndex.MaxDefinitions),positionPages=reader.Count(NavPhysicalCaptureIndex.MaxDefinitions);
            long labels=(long)nodes*candidates;if(labels>int.MaxValue)throw new ArgumentException("Field frontier label capacity overflow.");
            var legacy=reader.Edge<NavCowOrderedSet<NavQueueEntry>>();var heap=reader.Edge<NavStorageDirectory<NavQueueEntry>>();var positions=reader.Edge<NavStorageDirectory<int>>();
            if(legacy!=null){if(heap!=null||positions!=null||count!=0||heapPages!=0||positionPages!=0||legacy.Count>maximum)throw new ArgumentException("Legacy field frontier shape contradiction.");}
            else {
                if(heap==null||positions==null||heap.Count!=(int)(((long)maximum+PageSize-1)/PageSize)||positions.Count!=(int)((labels+PageSize-1)/PageSize)||heap.Capacity!=heap.Count||positions.Capacity!=positions.Count||heap.AllocatedPages!=heapPages||positions.AllocatedPages!=positionPages||count>(long)heapPages*PageSize)throw new ArgumentException("Packed field frontier sparse shape contradiction.");
                for(int p=0;p<heap.Count;p++){
                    var page=heap.Get(p);if(page==null){if((long)p*PageSize<count)throw new ArgumentException("Missing active raw heap page.");continue;}
                    if(page.Values.Length!=PageSize||page.physicalWidth!=16)throw new ArgumentException("Raw heap page shape contradiction.");
                }
                for(int p=0;p<positions.Count;p++){
                    var page=positions.Get(p);if(page==null)continue;
                    if(page.Values.Length!=PageSize||page.physicalWidth!=4)throw new ArgumentException("Raw heap position page shape contradiction.");
                    for(int n=0;n<PageSize;n++){
                        int position=page.Values[n];long label=(long)p*PageSize+n;
                        if(position<0||position>count||label>=labels&&position!=0)throw new ArgumentException("Invalid raw heap position.");
                        if(position!=0){var entry=heap.Get((position-1)/PageSize)?.Values[(position-1)%PageSize]??default;if((long)entry.Candidate*nodes+entry.Node!=label)throw new ArgumentException("Raw heap position/label contradiction.");}
                    }
                }
            }
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(256);var result=allocation.Register(new NavFieldFrontier(nodes,candidates,maximum){count=count,heapPages=heapPages,positionPages=positionPages});
                if(legacy!=null){result.RequireEdge(legacy);legacy.Retain();result.legacy=legacy;foreach(var entry in legacy)result.Key(entry);}
                else {
                    result.RequireEdge(heap);result.RequireEdge(positions);heap.Retain();result.heap=heap;positions.Retain();result.positions=positions;
                    for(int n=0;n<count;n++){var entry=result.Get(n);int key=result.Key(entry);if(result.Position(key)!=n+1||n>0&&result.Get((n-1)/2).CompareTo(entry)>0)throw new ArgumentException("Invalid raw heap order/duplicate label.");}
                }
                return result;
            }
        }
    }
}
