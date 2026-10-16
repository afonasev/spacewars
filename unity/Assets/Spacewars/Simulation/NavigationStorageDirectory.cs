using System;

namespace Spacewars.Simulation
{
    // Fixed fanout paths make fork constant-work and mutation bounded by depth.
    // Logical capacity is preserved separately for the primitive checkpoint contract.
    internal sealed partial class NavStorageDirectory<T> : NavOwnedStorage, IDisposable
    {
        internal const int Fanout=32;
        internal static NavStorageDirectory<T> Create(Func<long,bool> reserve=null)
        {using(var allocation=new NavStorageAllocation(reserve)){allocation.Reserve(128);return new NavStorageDirectory<T>(reserve,true);}}
        internal NavStorageDirectory(Func<long,bool> reserve=null,bool alreadyReserved=false):base(128){using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(128);allocation.Register(this);Retain();}}
        internal sealed partial class Page : NavOwnedStorage
        {
            internal readonly T[] Values;
            internal readonly long Owner;
            internal Page(int length,int width,long owner):base(96L+(long)length*width){Values=new T[length];Owner=owner;physicalWidth=width;}
            protected override void ReleaseEdges(){if(NavStoragePayload<T>.HasOwnership)foreach(var value in Values)NavStoragePayload<T>.Release(value);}
        }
        internal sealed partial class Block : NavOwnedStorage
        {
            internal readonly NavOwnedStorage[] Children=new NavOwnedStorage[Fanout];
            internal readonly long Owner;
            internal Block(long owner):base(64+32+8*Fanout){Owner=owner;}
            protected override void ReleaseEdges(){foreach(var child in Children)child?.Release();}
        }
        private Block root;
        private int depth;
        private static long sequence;
        private readonly long owner=System.Threading.Interlocked.Increment(ref sequence);
        internal int Count {get;private set;}
        internal int AllocatedPages {get;private set;}
        internal int Capacity {get;private set;}
        internal int Blocks {get;private set;}
        internal long DirectoryBytes=>NavStorageLayout.Charge(352)*Blocks;
        internal NavStorageDirectory<T> Fork(Func<long,bool> reserve=null)
        {var fork=NavStorageDirectory<T>.Create(reserve);try{root?.Retain();fork.root=root;fork.depth=depth;fork.Count=Count;fork.Capacity=Capacity;fork.Blocks=Blocks;fork.AllocatedPages=AllocatedPages;return fork;}catch{fork.Dispose();throw;}}
        internal void InitializeSlots(int slots){if(root!=null||slots<0)throw new ArgumentException("Invalid sparse directory initialization.");Count=Capacity=slots;}
        internal void RestoreCapacity(int value){if(value<Count)throw new ArgumentException("Directory capacity below count.");Capacity=value;}
        internal Page Get(int index)
        {
            if(index<0||index>=Count)throw new ArgumentOutOfRangeException(nameof(index));
            var block=root;if(block==null)return null;
            if((long)index>=(1L<<((depth+1)*5)))return null;
            for(int level=depth;level>0;level--){block=(Block)block.Children[(index>>(level*5))&31];if(block==null)return null;}
            return (Page)block.Children[index&31];
        }
        private Block Copy(Block source,NavStorageAllocationContext allocation)
        {
            if(source!=null&&source.Owner==owner)return source;
            allocation.Reserve(352);var result=new Block(owner);allocation.Register(result);
            if(source==null)Blocks++;
            else for(int i=0;i<Fanout;i++){var child=source.Children[i];child?.Retain();result.Children[i]=child;}
            return result;
        }
        private void SetChild(Block block,int index,NavOwnedStorage value)
        {value?.Retain();var old=block.Children[index];block.Children[index]=value;old?.Release();}
        internal Page Writable(int index,int length,int width,Func<long,bool> reserve,bool append=false)
        {
            if(index<0||index>(append?Count:Count-1))throw new ArgumentOutOfRangeException(nameof(index));
            using(var allocation=new NavStorageAllocationContext(reserve)){
                if(root==null){var initial=Copy(null,allocation);initial.Retain();root=initial;}
                while((long)index>=(1L<<((depth+1)*5))){
                    var expanded=Copy(null,allocation);SetChild(expanded,0,root);expanded.Retain();var old=root;root=expanded;old.Release();depth++;
                }
                var writable=Copy(root,allocation);if(!ReferenceEquals(writable,root)){writable.Retain();var old=root;root=writable;old.Release();}
                var block=root;
                for(int level=depth;level>0;level--){int slot=(index>>(level*5))&31;var source=(Block)block.Children[slot];var next=Copy(source,allocation);if(!ReferenceEquals(source,next))SetChild(block,slot,next);block=next;}
                int within=index&31;var page=(Page)block.Children[within];
                if(page==null||page.Owner!=owner){
                    allocation.Reserve(96L+(long)length*width);var copy=new Page(length,width,owner);allocation.Register(copy);
                    if(page!=null){for(int i=0;i<length;i++){var value=page.Values[i];NavStoragePayload<T>.Validate(value,copy);NavStoragePayload<T>.Retain(value);copy.Values[i]=value;}}else AllocatedPages++;SetChild(block,within,copy);page=copy;
                }
                if(append){Count++;if(Count>Capacity)Capacity=Capacity==0?4:Capacity*2;}
                return page;
            }
        }
        // Growth forecasts include worst-case touched path plus page; allocation
        // callbacks remain the authority and may refuse before each allocation.
        internal long WriteGrowthBytes(int length,int width)=>NavStorageLayout.Charge(352)*(depth+2)+NavStorageLayout.Charge(96L+(long)length*width);
        private bool released;
        public void Dispose(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>Dispose();
        protected override void ReleaseEdges(){var old=root;root=null;Count=Capacity=Blocks=AllocatedPages=depth=0;old?.Release();}
        internal NavOwnedStorage PhysicalRoot=>this;
    }
}
