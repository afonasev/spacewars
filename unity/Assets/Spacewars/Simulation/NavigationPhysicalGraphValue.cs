using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    internal static class NavPhysicalGraphValue
    {
        internal static void WriteArc(NavPhysicalBodyWriter writer,NavGraphArc arc)
        {
            writer.Output.Write(arc.From);writer.Output.Write(arc.To);writer.Output.Write(arc.Cost);writer.Output.Write((int)arc.Kind);
            writer.Edge(arc.Trace as NavOwnedStorage);
            if(arc.Trace!=null&&!(arc.Trace is NavOwnedStorage))throw new ArgumentException("Unowned physical arc trace.");
            if(arc.NameReference.Owner!=null){if(arc.Trace!=null||arc.Kind!=NavGraphArcKind.Portal||!ReferenceEquals(arc.PortalId,arc.NameReference.Resolve()))throw new ArgumentException("Invalid direct arc name.");writer.Output.Write(-2);arc.NameReference.Write(writer);return;}
            if(arc.PortalId==null){if(arc.Kind!=NavGraphArcKind.Surface)throw new ArgumentException("Missing portal arc name.");writer.Output.Write(-1);return;}
            if(arc.Trace!=null)for(int n=0;n<arc.Trace.Count;n++)if(arc.Trace[n].Kind==NavTypedLegKind.Portal&&ReferenceEquals(arc.Trace[n].PortalId,arc.PortalId)){writer.Output.Write(n);return;}
            // A non-trace arc needs its own provider name ownership contract.
            // An ambient provider or an equal inline string supplies no owner.
            throw new ArgumentException("Physical arc needs its provider or owned trace portal binding.");
        }
        internal static NavGraphArc ReadArc(NavPhysicalBodyReader reader)
        {
            int from=reader.Input.ReadInt32(),to=reader.Input.ReadInt32();double cost=reader.Input.ReadDouble();var kind=(NavGraphArcKind)reader.Input.ReadInt32();
            if(from<0||to<0||double.IsNaN(cost)||double.IsInfinity(cost)||cost<0||!Enum.IsDefined(typeof(NavGraphArcKind),kind))throw new ArgumentException("Invalid physical graph arc.");
            var storage=reader.Edge();if(storage!=null&&!(storage is IReadOnlyList<NavTypedLeg>))throw new ArgumentException("Wrong physical graph trace type.");
            var trace=(IReadOnlyList<NavTypedLeg>)storage;int at=reader.Input.ReadInt32();string portal=null;NavNameReference name=default;
            if(at==-2){if(trace!=null)throw new ArgumentException("Contradictory direct arc trace.");name=NavNameReference.Read(reader);if(name.Owner==null)throw new ArgumentException("Missing direct arc owner.");portal=name.Resolve();}
            else if(at!=-1){if(trace==null||at<0||at>=trace.Count||trace[at]==null||trace[at].Kind!=NavTypedLegKind.Portal)throw new ArgumentException("Invalid physical arc portal binding.");portal=trace[at].PortalId;}
            if(kind==NavGraphArcKind.Surface&&portal!=null||kind==NavGraphArcKind.Portal&&portal==null)throw new ArgumentException("Physical graph arc kind/portal contradiction.");
            return new NavGraphArc(from,to,cost,kind,portal,trace,name);
        }
    }
    internal sealed partial class NavPackedFieldStorage : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>frozen;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.PackedField);
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {if(!frozen)throw new ArgumentException("Packed field must be sealed.");writer.Output.Write(Capacity);writer.Output.Write(Count);writer.Edge(pages);}
        private NavPackedFieldStorage(int capacity,bool physical):base(256){Capacity=capacity;frozen=true;}
        internal static NavPackedFieldStorage ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            int capacity=reader.Count(int.MaxValue),count=reader.Count(capacity);var directory=reader.Edge<NavStorageDirectory<NavPackedFieldCell>>();
            if(directory==null||directory.Count!=(int)(((long)capacity+LabelsPerPage-1)/LabelsPerPage)||directory.Capacity!=directory.Count)throw new ArgumentException("Packed field sparse capacity contradiction.");
            int populated=0;
            for(int p=0;p<directory.Count;p++){
                var page=directory.Get(p);if(page==null)continue;
                if(page.Values.Length!=LabelsPerPage||page.physicalWidth!=CellBytes)throw new ArgumentException("Packed field page shape contradiction.");
                bool nonempty=false;
                for(int n=0;n<LabelsPerPage;n++){
                    var cell=page.Values[n];long index=(long)p*LabelsPerPage+n;
                    if(cell.Flags>3||cell.Flags==2||index>=capacity&&cell.Flags!=0||(cell.Flags&2)==0&&!cell.Next.Equals(default(NavGraphArc))||
                        (cell.Flags&1)!=0&&(double.IsNaN(cell.Distance)||double.IsInfinity(cell.Distance)||cell.Distance<0))throw new ArgumentException("Invalid physical packed field cell.");
                    if((cell.Flags&1)!=0){populated++;nonempty=true;}
                }
                if(!nonempty)throw new ArgumentException("Empty allocated physical field page.");
            }
            if(populated!=count)throw new ArgumentException("Physical packed field count contradiction.");
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(256);var result=allocation.Register(new NavPackedFieldStorage(capacity,true){Count=count});result.RequireEdge(directory);directory.Retain();result.pages=directory;return result;
            }
        }
    }
}
