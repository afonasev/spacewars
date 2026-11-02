using System;
using System.IO;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Runtime
{
    internal sealed partial class AiCombatMicroPolicy
    {
        // A detached proof of the existing ReturnCore predicate for one actual
        // target. This does not authorize an intent or consume a motion permit.
        internal sealed class ReturnCoreWork:IDisposable
        {
            readonly struct Candidate
            {
                internal readonly int Id;internal readonly NavPoint Point;internal readonly bool Eligible;
                internal Candidate(int id,NavPoint point,bool eligible){Id=id;Point=point;Eligible=eligible;}
            }
            readonly Candidate[] candidates;readonly bool groupValid;readonly int target;
            readonly double required,radius;readonly AiCombatMicroExecutionStorage.Lease memory;
            int cursor,anchor=-1,neighbors,phase;bool complete,allowed,disposed;
            internal bool Complete=>complete;
            internal bool Allowed{get{if(!complete)throw new InvalidOperationException("Return core pending.");return allowed;}}
            internal long Consumed{get;private set;}
            ReturnCoreWork(Candidate[] candidates,bool groupValid,int target,double required,double radius,AiCombatMicroExecutionStorage.Lease memory){this.candidates=candidates;this.groupValid=groupValid;this.target=target;this.required=required;this.radius=radius;this.memory=memory;}
            internal static bool TryCreate(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,AiArmyRegistry registry,int patient,long army,int target,ref long copyGrant,AiCombatMicroExecutionStorage storage,out ReturnCoreWork work)
            {
                work=null;if(o==null||p==null||ai==null||registry==null||storage==null||registry.OwnerId!=o.OwnerId||registry.Generation!=o.Generation||patient<=0||target<=0)throw new ArgumentException("Invalid return core owner input.");
                bool found=registry.TryReadGuard(army,out var group);var source=o.Entities;if(source.Count>WorldWire.MaxItems)throw new ArgumentException("Return input too large.");
                // Pay every membership/reinforcement scan plus row zeroing/fill.
                // The actual registry is only borrowed in this factory, never Work.
                long cost=checked(32L+(found?group.MemberCount+(long)group.ReinforcementCount:0)+2L*source.Count*(2L+(found?group.MemberCount+(long)group.ReinforcementCount:0)));
                if(copyGrant<cost||!storage.TryReserve(checked(256L+64L*source.Count),out var memory))return false;
                try{bool valid=found&&ReturnGroup(o.OwnerId,patient,group);var rows=new Candidate[source.Count];for(int i=0;i<rows.Length;i++){var candidate=source[i];rows[i]=new Candidate(candidate.Id,candidate.Position,valid&&ReturnCoreCandidate(o.Owner,patient,p,ai,d,group,candidate));}work=new ReturnCoreWork(rows,valid,target,V(ai,d,"returnCoreUnits"),V(ai,d,"supportRadius"),memory);copyGrant-=cost;return true;}catch{memory.Dispose();throw;}
            }
            internal int Advance(int grant,Func<bool> admit=null)
            {if(disposed)throw new ObjectDisposedException(nameof(ReturnCoreWork));if(grant<0)throw new ArgumentOutOfRangeException(nameof(grant));int used=0;while(used<grant&&!complete){if(admit!=null&&!admit())break;Step();Consumed++;used++;}return used;}
            void Step()
            {
                if(!groupValid){complete=true;return;}
                if(phase==0){if(cursor==candidates.Length){complete=true;return;}var candidate=candidates[cursor];if(candidate.Id==target&&candidate.Eligible){anchor=cursor;cursor=0;phase=1;return;}cursor++;return;}
                if(phase==1){if(cursor==candidates.Length){allowed=neighbors>=required;complete=true;return;}var candidate=candidates[cursor++];if(candidate.Eligible&&Distance(candidates[anchor].Point,candidate.Point)<=radius)neighbors++;return;}
                throw new InvalidOperationException("Invalid return core phase.");
            }
            void WriteBinding(BinaryWriter w){w.Write(groupValid);w.Write(target);w.Write(required);w.Write(radius);w.Write(candidates.Length);foreach(var c in candidates){w.Write(c.Id);WorldWire.Write(w,c.Point);w.Write(c.Eligible);}}
            internal void Write(BinaryWriter w){if(disposed)throw new ObjectDisposedException(nameof(ReturnCoreWork));w.Write(1);WriteBinding(w);w.Write(cursor);w.Write(anchor);w.Write(neighbors);w.Write(phase);w.Write(complete);w.Write(allowed);w.Write(Consumed);}
            // Cold input is rebuilt from the restored owning snapshot/registry.
            // Exact binding comparison is declared linear lifecycle work, never
            // one Advance credit. Allocation liability is recomputed by factory.
            internal static ReturnCoreWork Read(BinaryReader r,PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,AiArmyRegistry registry,int patient,long army,int target,ref long copyGrant,AiCombatMicroExecutionStorage storage)
            {
                if(r.ReadInt32()!=1)throw new ArgumentException("Unknown return core cursor.");if(!TryCreate(o,p,ai,d,registry,patient,army,target,ref copyGrant,storage,out var work))throw new InvalidOperationException("Return core admission refused.");
                try{using(var comparison=new PredicateBindingComparison(r))using(var w=new BinaryWriter(comparison,System.Text.Encoding.UTF8,true))work.WriteBinding(w);work.cursor=r.ReadInt32();work.anchor=r.ReadInt32();work.neighbors=r.ReadInt32();work.phase=r.ReadInt32();work.complete=WorldWire.Boolean(r);work.allowed=WorldWire.Boolean(r);work.Consumed=r.ReadInt64();
                    if(work.cursor<0||work.cursor>work.candidates.Length||work.anchor< -1||work.anchor>=work.candidates.Length||work.neighbors<0||work.neighbors>work.candidates.Length||work.phase<0||work.phase>1||work.Consumed<0||work.allowed&&!work.complete||work.phase==0&&(work.anchor!=-1||work.neighbors!=0||work.allowed)||work.phase==1&&(work.anchor<0||!work.candidates[work.anchor].Eligible||work.candidates[work.anchor].Id!=target||work.neighbors>work.cursor)||work.complete&&work.phase==1&&(work.cursor!=work.candidates.Length||work.allowed!=(work.neighbors>=work.required))||!work.groupValid&&(work.phase!=0||work.cursor!=0||work.neighbors!=0))throw new ArgumentException("Impossible return core cursor.");return work;
                }catch{work.Dispose();throw;}
            }
            public void Dispose(){if(disposed)return;disposed=true;memory.Dispose();}
        }
    }
}
