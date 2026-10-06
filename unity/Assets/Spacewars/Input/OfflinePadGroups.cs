using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Input
{
    // UI-only, one instance per owner. Descriptors come from that owner's projection.
    public readonly struct OfflinePadBase
    {
        public readonly int Id;public readonly double X,Z;public readonly bool Original;
        public OfflinePadBase(int id,double x,double z,bool original=false){Id=id;X=x;Z=z;Original=original;}
    }
    public sealed class OfflinePadGroups
    {
        public const int MaximumSlots=9;
        private readonly Dictionary<int,int[]> groups=new Dictionary<int,int[]>();
        private int? remembered;private int baseIndex=-1;
        private static int[] Own(IEnumerable<int> ids,IReadOnlyList<OfflinePadSelectable> units)
        {var allowed=new HashSet<int>(units.Where(u=>u.Own).Select(u=>u.Id));return ids.Where(allowed.Contains).Distinct().ToArray();}
        public int[] Recall(int slot,IReadOnlyList<OfflinePadSelectable> units)
        {var ids=groups.TryGetValue(slot,out var value)?Own(value,units):Array.Empty<int>();if(ids.Length>0)groups[slot]=ids;else groups.Remove(slot);remembered=slot;return ids;}
        public bool Assign(int slot,IEnumerable<int> selection,IReadOnlyList<OfflinePadSelectable> units,bool add)
        {
            if(slot<1||slot>MaximumSlots)return false;var own=Own(selection,units);if(own.Length==0)return false;
            groups.TryGetValue(slot,out var previous);groups[slot]=add?Own((previous??Array.Empty<int>()).Concat(own),units):own;
            if(!add)remembered=slot;else if(!new HashSet<int>(own).SetEquals(groups[slot]))remembered=null;
            return true;
        }
        public void ClearActive()=>remembered=null;
        public int SlotCount(IReadOnlyList<OfflinePadSelectable> units)
        {int last=groups.Where(g=>Own(g.Value,units).Length>0).Select(g=>g.Key).DefaultIfEmpty(0).Max();return Math.Min(MaximumSlots,Math.Max(3,last+1));}
        public OfflinePadSector[] Actions(IReadOnlyList<OfflinePadSelectable> units,IEnumerable<int> selection,bool assign,int slots)
        {bool valid=Own(selection,units).Length>0;return Enumerable.Range(1,slots).Select(index=>new OfflinePadSector(index.ToString(),assign?valid:groups.TryGetValue(index,out var ids)&&Own(ids,units).Length>0)).ToArray();}
        public int[] Cycle(IReadOnlyList<OfflinePadSelectable> units,IEnumerable<int> selection)
        {
            var occupied=groups.Where(g=>Own(g.Value,units).Length>0).Select(g=>g.Key).OrderBy(index=>index).ToArray();if(occupied.Length==0)return null;
            var selected=Own(selection,units);bool Match(int index)=>new HashSet<int>(groups[index]).SetEquals(selected);
            int current=remembered.HasValue?Array.FindIndex(occupied,index=>index==remembered.Value&&Match(index)):-1;
            if(current<0)current=Array.FindIndex(occupied,Match);return Recall(occupied[(current+1)%occupied.Length],units);
        }
        public static OfflinePadBase? Focus(IEnumerable<int> selection,IReadOnlyList<OfflinePadSelectable> units,double cameraX,double cameraZ,double radius)
        {
            var ids=new HashSet<int>(Own(selection,units));var selected=units.Where(u=>ids.Contains(u.Id)).OrderBy(u=>u.Id.ToString(),StringComparer.Ordinal).ToArray();
            double Distance(double ax,double az,double bx,double bz)=>(ax-bx)*(ax-bx)+(az-bz)*(az-bz);
            int bestCount=0;double bestDistance=double.PositiveInfinity;OfflinePadBase? result=null;
            foreach(var anchor in selected){var nearby=selected.Where(u=>Distance(u.X,u.Z,anchor.X,anchor.Z)<=radius*radius).ToArray();if(nearby.Length<bestCount)continue;double x=nearby.Average(u=>u.X),z=nearby.Average(u=>u.Z),cameraDistance=Distance(x,z,cameraX,cameraZ);if(nearby.Length==bestCount&&cameraDistance>=bestDistance)continue;var nearest=nearby[0];foreach(var u in nearby)if(Distance(u.X,u.Z,x,z)<Distance(nearest.X,nearest.Z,x,z))nearest=u;bestCount=nearby.Length;bestDistance=cameraDistance;result=new OfflinePadBase(nearest.Id,nearest.X,nearest.Z);}
            return result;
        }
        public static OfflinePadSector[] Bases(IReadOnlyList<OfflinePadBase> bases,int originalId,int page,int pageSize,out int pages)
        {
            var others=bases.Where(b=>b.Id!=originalId).OrderBy(b=>b.Id.ToString(),StringComparer.Ordinal).ToArray();pages=Math.Max(1,(int)Math.Ceiling((double)others.Length/pageSize));
            return new[]{new OfflinePadSector(originalId==0?"original-hq":originalId.ToString(),bases.Any(b=>b.Id==originalId))}.Concat(others.Skip((page%pages)*pageSize).Take(pageSize).Select(b=>new OfflinePadSector(b.Id.ToString(),true))).ToArray();
        }
        public OfflinePadBase? CycleBase(IReadOnlyList<OfflinePadBase> bases,int originalId)
        {var ordered=bases.Where(b=>b.Id==originalId).Concat(bases.Where(b=>b.Id!=originalId).OrderBy(b=>b.Id.ToString(),StringComparer.Ordinal)).ToArray();return ordered.Length==0?(OfflinePadBase?)null:ordered[++baseIndex%ordered.Length];}
    }
}
