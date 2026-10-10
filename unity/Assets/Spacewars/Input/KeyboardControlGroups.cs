using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Input
{
    public readonly struct KeyboardSelectable
    {
        public readonly int Id, Kind;
        public readonly bool Own, Building;
        public readonly double X, Z;
        public KeyboardSelectable(int id, int kind, bool own, bool building, double x, double z)
        { Id=id; Kind=kind; Own=own; Building=building; X=x; Z=z; }
    }

    // Local presentation state. Never serialized into a match or shared between seats.
    public sealed class KeyboardControlGroups
    {
        private readonly Dictionary<int,int[]> groups = new Dictionary<int,int[]>();
        private HashSet<int> observed = new HashSet<int>();
        private int lastIndex;
        private double lastTime = double.NegativeInfinity;
        public int? Active { get; private set; }
        public void Reset() { groups.Clear(); observed.Clear(); Active=null; lastIndex=0; lastTime=double.NegativeInfinity; }
        public static int[] Normalize(IEnumerable<int> ids, IReadOnlyList<KeyboardSelectable> entities)
        {
            var wanted = new HashSet<int>(ids);
            var own = entities.Where(e=>e.Own && wanted.Contains(e.Id)).OrderBy(e=>e.Id).ToArray();
            var units = own.Where(e=>!e.Building).Select(e=>e.Id).ToArray();
            return units.Length>0 ? units : own.Take(1).Select(e=>e.Id).ToArray();
        }
        public void Observe(IEnumerable<int> selection)
        { var next=new HashSet<int>(selection); if(!observed.SetEquals(next))Active=null; observed=next; }
        public void Prune(IReadOnlyList<KeyboardSelectable> entities)
        { foreach(var index in groups.Keys.ToArray())groups[index]=Normalize(groups[index],entities); }
        public int[] Apply(int index, bool assign, double time, IEnumerable<int> selection, IReadOnlyList<KeyboardSelectable> entities, double doubleTapMs, out bool focus)
        {
            focus=false;
            if(index==0) { Active=null; lastIndex=0; lastTime=double.NegativeInfinity; var army=entities.Where(e=>e.Own&&!e.Building).Select(e=>e.Id).ToArray(); observed=new HashSet<int>(army); return army; }
            if(index<1||index>9)throw new ArgumentOutOfRangeException(nameof(index));
            var result=assign?Normalize(selection,entities):groups.TryGetValue(index,out var ids)?Normalize(ids,entities):Array.Empty<int>();
            groups[index]=result;
            focus=!assign&&lastIndex==index&&time>=lastTime&&time-lastTime<=doubleTapMs/1000d;
            lastIndex=assign?0:index; lastTime=assign?double.NegativeInfinity:time;
            Active=index; observed=new HashSet<int>(result); return result;
        }
        public int SlotCount(IReadOnlyList<KeyboardSelectable> entities)
        { Prune(entities);return Math.Min(9,Math.Max(3,groups.Where(g=>g.Value.Length>0).Select(g=>g.Key).DefaultIfEmpty(0).Max()+1)); }
        public OfflinePadSector[] PadActions(IEnumerable<int> selection,IReadOnlyList<KeyboardSelectable> entities,bool assign,int slots)
        {
            Prune(entities);bool canAssign=Normalize(selection,entities).Any(id=>entities.Any(e=>e.Id==id&&!e.Building));
            return Enumerable.Range(1,slots).Select(index=>new OfflinePadSector(index.ToString(),assign?canAssign:groups.TryGetValue(index,out var ids)&&ids.Length>0)).ToArray();
        }
        public int[] RecallPad(int index,IReadOnlyList<KeyboardSelectable> entities)
        {
            if(index<1||index>9)return null;var result=groups.TryGetValue(index,out var stored)?Normalize(stored,entities):Array.Empty<int>();groups[index]=result;Active=index;observed=new HashSet<int>(result);return result;
        }
        public int[] Cycle(IEnumerable<int> selection,IReadOnlyList<KeyboardSelectable> entities)
        {
            Prune(entities);var occupied=groups.Where(g=>g.Value.Length>0).Select(g=>g.Key).OrderBy(i=>i).ToArray();if(occupied.Length==0)return null;
            var selected=new HashSet<int>(Normalize(selection,entities));bool Match(int index)=>selected.SetEquals(groups[index]);
            int current=Active.HasValue?Array.FindIndex(occupied,i=>i==Active.Value&&Match(i)):-1;if(current<0)current=Array.FindIndex(occupied,Match);
            return RecallPad(occupied[(current+1)%occupied.Length],entities);
        }
        public bool Add(int slot,IEnumerable<int> selection,IReadOnlyList<KeyboardSelectable> entities)
        {
            if(slot<1||slot>9)return false;var additions=Normalize(selection,entities);if(!additions.Any(id=>entities.Any(e=>e.Id==id&&!e.Building)))return false;
            groups.TryGetValue(slot,out var prior);groups[slot]=Normalize((prior??Array.Empty<int>()).Concat(additions),entities);
            if(!new HashSet<int>(additions).SetEquals(groups[slot]))Active=null;observed=new HashSet<int>(additions);return true;
        }
        public static OfflinePadBase? Focus(IEnumerable<int> selection,IReadOnlyList<KeyboardSelectable> entities,double cameraX,double cameraZ,double radius)
        {
            var ids=new HashSet<int>(selection); var building=entities.FirstOrDefault(e=>e.Own&&e.Building&&ids.Contains(e.Id));
            if(building.Id!=0)return new OfflinePadBase(building.Id,building.X,building.Z);
            return OfflinePadGroups.Focus(ids,entities.Where(e=>!e.Building).Select(e=>new OfflinePadSelectable(e.Id,e.Kind,e.Own,e.X,e.Z,0,true)).ToArray(),cameraX,cameraZ,radius);
        }
    }
}
