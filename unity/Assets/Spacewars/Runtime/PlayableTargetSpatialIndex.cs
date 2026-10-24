using System;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Derived broad phase only. Eligibility and exact distances remain live domain rules.
    internal sealed class PlayableTargetSpatialIndex
    {
        internal readonly struct Entry
        {
            internal readonly int Id;
            internal readonly bool Building;
            internal Entry(int id,bool building){Id=id;Building=building;}
        }
        private readonly struct Cell:IEquatable<Cell>
        {
            internal readonly long X,Z;
            internal Cell(long x,long z){X=x;Z=z;}
            public bool Equals(Cell other)=>X==other.X&&Z==other.Z;
            public override bool Equals(object obj)=>obj is Cell c&&Equals(c);
            public override int GetHashCode()=>unchecked((X.GetHashCode()*397)^Z.GetHashCode());
        }
        // This changes only broad-phase work, never weapon/vision range.
        private const double CellWidth=16;
        private const double ExactCellLimit=281474976710656d;
        private readonly Dictionary<Cell,int> heads=new Dictionary<Cell,int>();
        private Entry[] entries=new Entry[64];
        private int[] next=new int[64];
        private int count;
        private bool unbucketed;
        internal int VisitedEntries{get;private set;}
        internal bool CombatPhase;
        internal readonly List<Entry> Candidates=new List<Entry>();
        internal void Clear(){heads.Clear();count=0;unbucketed=false;}
        private static bool Coordinate(double value,out long cell)
        {
            double at=Math.Floor(value/CellWidth);
            if(double.IsNaN(at)||double.IsInfinity(at)||Math.Abs(at)>=ExactCellLimit){cell=0;return false;}
            cell=(long)at;return true;
        }
        internal void Add(int id,NavPoint position,bool building)
        {
            if(count==entries.Length){Array.Resize(ref entries,checked(count*2));Array.Resize(ref next,entries.Length);}
            int index=count++;entries[index]=new Entry(id,building);
            if(!Coordinate(position.X,out long x)||!Coordinate(position.Z,out long z)){unbucketed=true;return;}
            var cell=new Cell(x,z);next[index]=heads.TryGetValue(cell,out int head)?head:-1;heads[cell]=index;
        }
        internal void Query(NavPoint point,double range,List<Entry> output)
        {
            output.Clear();VisitedEntries=0;
            if(range<0||double.IsNaN(range))return;
            // One extra cell on every side makes rounded AABB arithmetic conservative
            // throughout the supported cell domain (coordinate ulp is far below a cell).
            if(unbucketed||!Coordinate(point.X-range,out long lowX)||!Coordinate(point.X+range,out long highX)||
                !Coordinate(point.Z-range,out long lowZ)||!Coordinate(point.Z+range,out long highZ)||
                (double)(highX-lowX+3)*(highZ-lowZ+3)>4096)
            {
                for(int i=0;i<count;i++)output.Add(entries[i]);VisitedEntries=count;return;
            }
            for(long x=lowX-1;x<=highX+1;x++)for(long z=lowZ-1;z<=highZ+1;z++)
                if(heads.TryGetValue(new Cell(x,z),out int head))
                    for(int i=head;i>=0;i=next[i]){output.Add(entries[i]);VisitedEntries++;}
        }
    }

    internal sealed partial class PlayableDomain
    {
        private readonly PlayableTargetSpatialIndex targetSpatialIndex=new PlayableTargetSpatialIndex();
        private void RebuildTargetSpatialIndex()
        {
            targetSpatialIndex.Clear();
            foreach(var u in units.Values)if(TryActor(u.Id,out var n))targetSpatialIndex.Add(u.Id,n.Position,false);
            foreach(var b in buildings.Values)if(b.Phase!=ConstructionPhase.Pending)targetSpatialIndex.Add(b.Id,b.Position,true);
        }
        private void AdvanceIndexedCombat(double dt)
        {
            // Called after movement/vision. Combat requests do not move bodies; projectile
            // deaths and economy spawns occur after this phase. Restore needs no wire state.
            RebuildTargetSpatialIndex();targetSpatialIndex.CombatPhase=true;
            try{AdvanceCombat(dt);}finally{targetSpatialIndex.CombatPhase=false;}
        }
        private int NearestSpatialTarget(NavPoint point,PlayableOwner observer,double range,bool unitsFirst,bool includeAir=false)
        {
            // Direct domain queries outside combat must also observe spawn/death/restore.
            if(!targetSpatialIndex.CombatPhase)RebuildTargetSpatialIndex();
            targetSpatialIndex.Query(point,range,targetSpatialIndex.Candidates);
            int best=0,bestClass=2;double distance=range;
            foreach(var candidate in targetSpatialIndex.Candidates)
            {
                int id=candidate.Id;
                if(!Target(id,out var position,out _))continue;
                PlayableOwner owner;
                if(units.TryGetValue(id,out var unit)){if(!includeAir&&unit.Air!=null)continue;owner=unit.Owner;}
                else if(buildings.TryGetValue(id,out var building)&&building.Phase!=ConstructionPhase.Pending)owner=building.Owner;
                else continue;
                if(!Hostile(owner,observer))continue;
                // Preserve the legacy lazy per-team vision-cache creation even when this
                // broad-phase candidate is rejected by exact range below.
                var vision=Vision(observer);
                double d=Distance(point,position);
                if(d<=range&&!vision.IsVisible(position))continue;
                int targetClass=unitsFirst&&candidate.Building?1:0;
                if(d<=range&&(best==0||targetClass<bestClass||targetClass==bestClass&&(d<distance||d==distance&&id<best)))
                {best=id;bestClass=targetClass;distance=d;}
            }
            return best;
        }
    }
}
