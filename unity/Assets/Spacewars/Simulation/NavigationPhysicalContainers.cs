using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    internal sealed partial class NavCowDictionary<TKey,TValue> : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>frozen;
        internal bool HasPhysicalInsertionOrder=>order!=null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Map,NavPhysicalValue<TKey>.Kind,NavPhysicalValue<TValue>.Kind);
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!frozen||!ReferenceEquals(comparer,NavPhysicalMapBinding<TKey,TValue>.Comparer))throw new ArgumentException("Physical map requires its supported immutable comparer.");
            writer.Output.Write(Count);writer.Edge(root);writer.Edge(order);
        }
        private NavCowDictionary(IComparer<TKey> comparer,bool physical):base(128){this.comparer=comparer;frozen=true;}
        internal static NavCowDictionary<TKey,TValue> ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            int count=reader.Count(NavPhysicalCaptureIndex.MaxDefinitions);
            var node=reader.Edge<Node>();var order=reader.Edge<NavPagedList<TKey>>();
            if(C(node)!=count||order!=null&&order.Count<count)throw new ArgumentException("Physical map root/count contradiction.");
            if(order!=null){
                const int mark=unchecked((int)0x80000000);int covered=0;
                void Clear(Node at){if(at==null)return;at.CaptureTag&=~mark;Clear(at.Left);Clear(at.Right);}
                try{
                    foreach(var key in order){
                        var at=node;while(at!=null){int comparison=NavPhysicalMapBinding<TKey,TValue>.Comparer.Compare(key,at.Key);if(comparison==0)break;at=comparison<0?at.Left:at.Right;}
                        // Removed insertion records are meaningful tombstones.
                        if(at!=null&&(at.CaptureTag&mark)==0){at.CaptureTag|=mark;covered++;}
                    }
                    if(covered!=count)throw new ArgumentException("Physical map insertion order omits a live key.");
                }finally{Clear(node);}
            }
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(128);var result=allocation.Register(new NavCowDictionary<TKey,TValue>(NavPhysicalMapBinding<TKey,TValue>.Comparer,true));
                if(node!=null){result.RequireEdge(node);node.Retain();result.root=node;}
                if(order!=null){result.RequireEdge(order);order.Retain();result.order=order;}
                return result;
            }
        }
        internal sealed partial class Node : INavPhysicalBody
        {
            public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.AvlNode,NavPhysicalValue<TKey>.Kind,NavPhysicalValue<TValue>.Kind);
            public void WritePhysicalBody(NavPhysicalBodyWriter writer)
            {NavPhysicalMapBinding<TKey,TValue>.Write(writer,Key,Value);writer.Edge(Left);writer.Edge(Right);}
            internal static Node ReadPhysicalBody(NavPhysicalBodyReader reader)
            {
                NavPhysicalMapBinding<TKey,TValue>.Read(reader,out var key,out var value);
                var left=reader.Edge<Node>();var right=reader.Edge<Node>();
                if(Math.Abs(H(left)-H(right))>1||Math.Max(H(left),H(right))>=64)throw new ArgumentException("Invalid physical AVL balance/depth.");
                var comparer=NavPhysicalMapBinding<TKey,TValue>.Comparer;
                if(left!=null){var max=left;while(max.Right!=null)max=max.Right;if(comparer.Compare(max.Key,key)>=0)throw new ArgumentException("Invalid physical AVL left ordering.");}
                if(right!=null){var min=right;while(min.Left!=null)min=min.Left;if(comparer.Compare(min.Key,key)<=0)throw new ArgumentException("Invalid physical AVL right ordering.");}
                using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                    allocation.Reserve(NodeBytes);var result=allocation.Register(new Node(key,value,left,right));result.AcquireEdges();return result;
                }
            }
        }
    }
    internal sealed partial class NavStorageDirectory<T> : INavPhysicalBody
    {
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Directory,value:NavPhysicalValue<T>.Kind);
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {writer.Output.Write(Count);writer.Output.Write(Capacity);writer.Output.Write(depth);writer.Output.Write(AllocatedPages);writer.Output.Write(Blocks);writer.Edge(root);}
        internal static NavStorageDirectory<T> ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            int count=reader.Count(int.MaxValue),capacity=reader.Count(int.MaxValue),depth=reader.Count(6),pageCount=reader.Count(NavPhysicalCaptureIndex.MaxDefinitions),blockCount=reader.Count(NavPhysicalCaptureIndex.MaxDefinitions);
            if(capacity<count)throw new ArgumentException("Physical directory capacity below count.");
            var root=reader.Edge<Block>();int actualPages=0,actualBlocks=0;long maximumPage=-1;
            const int mark=unchecked((int)0x80000000);
            void Mark(NavOwnedStorage storage){if((storage.CaptureTag&mark)!=0)throw new ArgumentException("Repeated physical child inside one directory tree.");storage.CaptureTag|=mark;}
            void Clear(NavOwnedStorage storage){if(storage==null||(storage.CaptureTag&mark)==0)return;storage.CaptureTag&=~mark;if(storage is Block block)for(int n=0;n<Fanout;n++)Clear(block.Children[n]);}
            void Validate(Block block,int level,long first){
                if(block==null)return;Mark(block);actualBlocks++;bool populated=false;
                if(actualBlocks>blockCount)throw new ArgumentException("Physical directory block count contradiction.");
                for(int n=0;n<Fanout;n++){
                    var child=block.Children[n];if(child==null)continue;populated=true;
                    long position=first+((long)n<<(level*5));
                    if(position>=count)throw new ArgumentException("Physical directory child outside logical shape.");
                    if(level==0){if(!(child is Page))throw new ArgumentException("Wrong physical directory leaf kind.");Mark(child);actualPages++;maximumPage=Math.Max(maximumPage,position);}
                    else {if(!(child is Block nested))throw new ArgumentException("Wrong physical directory branch kind.");Validate(nested,level-1,position);}
                }
                if(!populated)throw new ArgumentException("Empty allocated physical directory block.");
            }
            try{
                Validate(root,depth,0);
                if(actualPages!=pageCount||actualBlocks!=blockCount||root==null&&depth!=0||depth>0&&maximumPage<(1L<<(depth*5)))throw new ArgumentException("Physical directory sparse shape contradiction.");
            }finally{Clear(root);}
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(128);var result=allocation.Register(new NavStorageDirectory<T>(true));
                result.Count=count;result.Capacity=capacity;result.depth=depth;result.AllocatedPages=pageCount;result.Blocks=blockCount;
                if(root!=null){result.RequireEdge(root);root.Retain();result.root=root;}return result;
            }
        }
        private NavStorageDirectory(bool raw):base(128){}
        internal sealed partial class Block : INavPhysicalBody
        {
            public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.DirectoryBlock,value:NavPhysicalValue<T>.Kind);
            public void WritePhysicalBody(NavPhysicalBodyWriter writer){for(int n=0;n<Fanout;n++)writer.Edge(Children[n]);}
            internal static Block ReadPhysicalBody(NavPhysicalBodyReader reader)
            {
                using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                    allocation.Reserve(352);var result=allocation.Register(new Block(0));
                    for(int n=0;n<Fanout;n++){
                        var child=reader.Edge();if(child==null)continue;
                        if(!(child is Block)&&!(child is Page))throw new ArgumentException("Wrong physical directory child type.");
                        result.RequireEdge(child);child.Retain();result.Children[n]=child;
                    }
                    return result;
                }
            }
        }
        internal sealed partial class Page : INavPhysicalBody
        {
            internal readonly int physicalWidth;
            public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Page,value:NavPhysicalValue<T>.Kind);
            public void WritePhysicalBody(NavPhysicalBodyWriter writer)
            {writer.Output.Write(Values.Length);writer.Output.Write(physicalWidth);for(int n=0;n<Values.Length;n++)NavPhysicalValue<T>.Write(writer,Values[n]);}
            internal static Page ReadPhysicalBody(NavPhysicalBodyReader reader)
            {
                int count=reader.Count(4096),width=reader.Count(1024);
                if(count==0||width<NavRetainedArray<T>.ElementBytes)throw new ArgumentException("Invalid physical page width/length.");
                using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                    allocation.Reserve(96L+(long)count*width);var result=allocation.Register(new Page(count,width,0));
                    for(int n=0;n<count;n++){var value=NavPhysicalValue<T>.Read(reader);NavStoragePayload<T>.Validate(value,result);NavStoragePayload<T>.Retain(value);result.Values[n]=value;}return result;
                }
            }
        }
    }
    internal sealed partial class NavPagedList<T> : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>frozen&&!faulted;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.List,value:NavPhysicalValue<T>.Kind);
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {if(!frozen||faulted)throw new ArgumentException("Physical list must be sealed.");writer.Output.Write(pageSize);writer.Output.Write(Count);writer.Edge(pages);}
        private NavPagedList(int pageSize):base(128){this.pageSize=pageSize;frozen=true;}
        internal static NavPagedList<T> ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            int size=reader.Count(4096),count=reader.Count(int.MaxValue);
            if(size<1)throw new ArgumentException("Invalid physical list page size.");
            var directory=reader.Edge<NavStorageDirectory<T>>();
            if(directory==null||directory.Count<(int)(((long)count+size-1)/size)||(long)directory.Count*size>int.MaxValue||directory.AllocatedPages!=directory.Count)throw new ArgumentException("Physical list directory/count contradiction.");
            NavPhysicalDefaults.DirectoryCapacity(directory.Capacity,directory.Count);
            for(int n=0;n<directory.Count;n++)if(directory.Get(n)==null||directory.Get(n).Values.Length!=size||directory.Get(n).physicalWidth!=elementBytes)throw new ArgumentException("Physical list page shape contradiction.");
            for(int n=0;n<count;n++)NavNamedPayload.RequireActive(directory.Get(n/size).Values[n%size]);
            for(long n=count;n<(long)directory.Count*size;n++)if(!NavPhysicalDefaults.IsDefault(directory.Get((int)(n/size)).Values[(int)(n%size)]))throw new ArgumentException("Hidden non-default physical list tail payload.");
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(128);var result=allocation.Register(new NavPagedList<T>(size){Count=count});result.RequireEdge(directory);directory.Retain();result.pages=directory;return result;
            }
        }
    }
    internal sealed partial class NavRetainedArray<T> : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>true;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Array,value:NavPhysicalValue<T>.Kind);
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {writer.Output.Write(values.Length);for(int n=0;n<values.Length;n++)NavPhysicalValue<T>.Write(writer,values[n]);}
        internal static NavRetainedArray<T> ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            int count=reader.Count(int.MaxValue);
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(96L+(long)NavRetainedArray<T>.ElementBytes*count);var result=allocation.Register(new NavRetainedArray<T>(count));
                for(int n=0;n<count;n++){var value=NavPhysicalValue<T>.Read(reader);NavStoragePayload<T>.ValidateActive(value,result);NavStoragePayload<T>.Retain(value);result.values[n]=value;result.populated++;}return result;
            }
        }
    }
}
