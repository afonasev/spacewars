#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.IO;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Deliberately local to one opt-in QA scenario, never saved or authoritative.
    public static class QaArrivalLifecycle
    {
        public const int RecordLimit=8192;
        public static readonly bool Requested=Array.IndexOf(Environment.GetCommandLineArgs(),"-arrivalLifecycleReadouts")>=0;
        private static volatile Observer current;
        public static bool Enabled=>current!=null;
        internal struct Ticket { internal Observer Owner;internal long Generation,Sequence,Group,Attempt,Epoch,Quantum;internal int Grant;internal bool SelectionKnown;internal NavArrivalLifecycleState Before; }
        internal struct Row { internal Ticket Id;internal string Kind,Reason;internal bool Committed,Known;internal long Entity,Request,Order,Incarnation,Revision;internal int Geometry,Profile;internal NavArrivalLifecycleState Proposed; }
        internal sealed class Observer
        {
            readonly object gate=new object();readonly Row[] rows;readonly long[] sequences=new long[8],groups=new long[8];long generation;int expected,count,dropped,filtered,preselection,preselectionRefused;long observerTicks;
            internal Observer(int capacity){if(capacity<1||capacity>RecordLimit)throw new ArgumentOutOfRangeException(nameof(capacity));rows=new Row[capacity];}
            internal void Register(long gen,long sequence){lock(gate){if(gen<1||sequence<1||expected>0&&generation!=gen){dropped++;return;}for(int i=0;i<expected;i++)if(sequences[i]==sequence)return;if(expected==8){dropped++;return;}generation=gen;sequences[expected++]=sequence;Add(new Row{Id=new Ticket{Owner=this,Generation=gen,Sequence=sequence},Kind="registration",Reason="registered"});}}
            internal Ticket Match(long gen,long sequence,long group,long attempt){lock(gate){for(int i=0;i<expected;i++)if(generation==gen&&sequences[i]==sequence){if(groups[i]==0)groups[i]=group;if(group<=0||groups[i]!=group){filtered++;return default;}return new Ticket{Owner=this,Generation=gen,Sequence=sequence,Group=group,Attempt=attempt};}filtered++;return default;}}
            internal void Add(Row row){lock(gate){if(count==rows.Length){dropped++;return;}rows[count++]=row;}}
            internal void Preselection(bool refused){lock(gate){preselection++;if(refused)preselectionRefused++;}}
            internal void ObserveTicks(long ticks){lock(gate)observerTicks+=ticks;}
            internal int Write(string path){lock(gate){using(var w=new StreamWriter(path)){
                w.WriteLine("kind,generation,sequence,group,attempt,entity,request,order,incarnation,revision,geometry,profile,epoch,quantum,grant,committed,reason,state_known,before_epoch,before_total,before_candidate_work,before_occupancy_work,before_direct_work,before_member,before_page,before_raw,before_candidate_cursor,before_occupancy_cursor,before_preparation_page,before_preparation_member,before_historical_gate,before_historical_cursor,before_phase,before_status,proposed_epoch,proposed_total,proposed_candidate_work,proposed_occupancy_work,proposed_direct_work,proposed_member,proposed_page,proposed_raw,proposed_candidate_cursor,proposed_occupancy_cursor,proposed_preparation_page,proposed_preparation_member,proposed_historical_gate,proposed_historical_cursor,proposed_phase,proposed_status");
                for(int i=0;i<count;i++){var r=rows[i];w.Write(string.Join(",",r.Kind,r.Id.Generation,r.Id.Sequence,r.Id.Group,r.Id.Attempt,r.Entity,r.Request,r.Order,r.Incarnation,r.Revision,r.Geometry,r.Profile,r.Id.Epoch,r.Id.Quantum,r.Id.Grant,r.Kind=="advance"&&r.Reason=="exception"?-1:r.Committed?1:0,r.Reason,r.Known?1:0));State(w,r.Id.Before);State(w,r.Proposed);w.WriteLine();}
                w.WriteLine("# schema=1,rows="+count+",dropped="+dropped+",expected="+expected+",filtered="+filtered+",preselection_unattributed="+preselection+",preselection_refused="+preselectionRefused+",observer_ticks="+observerTicks+",frequency="+System.Diagnostics.Stopwatch.Frequency+",limit="+rows.Length);
            }return dropped+(expected==8?0:1);}}
#if UNITY_INCLUDE_TESTS
            internal Row[] CopyForTests(){lock(gate){var result=new Row[count];Array.Copy(rows,result,count);return result;}}
#endif
            static void State(StreamWriter w,NavArrivalLifecycleState s){w.Write(","+string.Join(",",s.Epoch,s.Total,s.CandidateWork,s.OccupancyWork,s.DirectWork,s.Member,s.Page,s.Raw,s.CandidateCursor,s.OccupancyCursor,s.PreparationPage,s.PreparationMember,s.HistoricalGate,s.HistoricalCursor,s.Phase,s.Status));}
        }
        internal static void Register(long generation,long sequence){if(!Requested&&current==null)return;if(current==null)current=new Observer(RecordLimit);current.Register(generation,sequence);}
        internal static Ticket BeginBirth(long generation,long attempt,long sequence,long group,NavigationRequest[] requests)
        {
            var o=current;if(o==null)return default;long start=System.Diagnostics.Stopwatch.GetTimestamp();var t=o.Match(generation,sequence,group,attempt);t.SelectionKnown=true;
            if(t.Owner!=null)foreach(var r in requests)o.Add(new Row{Id=t,Kind="binding",Reason="observed_input",Entity=r.Entity,Request=r.Request,Order=r.Order,Incarnation=r.MemberIdentity?.Incarnation??0,Revision=r.MemberIdentity?.OrderRevision??0,Geometry=r.BaseGeometry.Revision,Profile=r.ProfileRevision});
            o.ObserveTicks(System.Diagnostics.Stopwatch.GetTimestamp()-start);return t;
        }
        internal static void FinishBirth(Ticket t,NavigationArrivalWork work,bool committed,string reason,bool refused=false){var o=current;if(o==null)return;long start=System.Diagnostics.Stopwatch.GetTimestamp();if(t.Owner==null){if(!t.SelectionKnown)o.Preselection(refused);}else o.Add(new Row{Id=t,Kind="birth",Committed=committed,Reason=reason,Known=work!=null,Proposed=work?.LifecycleState??default});o.ObserveTicks(System.Diagnostics.Stopwatch.GetTimestamp()-start);}
        internal static Ticket BeginAdvance(long generation,long attempt,long epoch,long quantum,int grant,NavigationArrivalWork work)
        {
            var o=current;if(o==null||work?.PhysicalInput==null)return default;long start=System.Diagnostics.Stopwatch.GetTimestamp();var m=work.PhysicalInput.Members[0];var t=o.Match(generation,m.CommandSequence,m.GroupId,attempt);if(t.Owner!=null){t.Epoch=epoch;t.Quantum=quantum;t.Grant=grant;t.Before=work.LifecycleState;}o.ObserveTicks(System.Diagnostics.Stopwatch.GetTimestamp()-start);return t;
        }
        internal static void FinishAdvance(Ticket t,NavigationArrivalWork proposed,string outcome){if(t.Owner==null)return;long start=System.Diagnostics.Stopwatch.GetTimestamp();t.Owner.Add(new Row{Id=t,Kind="advance",Committed=outcome=="committed",Reason=outcome,Known=proposed!=null,Proposed=proposed?.LifecycleState??default});t.Owner.ObserveTicks(System.Diagnostics.Stopwatch.GetTimestamp()-start);}
        internal static void Retire(long generation,long attempt,NavigationArrivalWork work,string reason){var t=BeginAdvance(generation,attempt,0,0,0,work);if(t.Owner!=null)t.Owner.Add(new Row{Id=t,Kind="retire",Committed=true,Reason=reason=="gameplay-owner-removed"?"gameplay-owner-removed":"other-owner-retired",Known=true,Proposed=work.LifecycleState});}
        public static int WriteCsv(string path){var o=current;if(o==null)return Requested?1:0;return o.Write(path);}
#if UNITY_INCLUDE_TESTS
        internal static void ResetForTests(int capacity=0){current=capacity==0?null:new Observer(capacity);}
        internal static Observer ObserverForTests=>current;
        internal static Row[] RowsForTests(){var o=current;if(o==null)return Array.Empty<Row>();lock(o){return o.CopyForTests();}}
#endif
    }
}
#endif
