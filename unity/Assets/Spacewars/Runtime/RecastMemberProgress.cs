using System;
using System.IO;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public enum RecastMovementPhase { Origin, Shared, Terminal, UnmappedProduction }
    // Scalar sidecar inside the paid immutable authority member. Version two is
    // selected by a distinct BODY tag; legacy MarchVersion2 bytes stay intact.
    public readonly struct RecastMemberProgress : IEquatable<RecastMemberProgress>
    {
        public readonly long Request;
        public readonly RecastMovementPhase Phase;
        public readonly int Cursor;
        public readonly double Station,HighWater;
        public RecastMemberProgress(long request,RecastMovementPhase phase,int cursor,double station,double highWater)
        {Request=request;Phase=phase;Cursor=cursor;Station=station;HighWater=highWater;Validate();}
        internal void Validate(){if(Request<0||(uint)Phase>3||Cursor<0||!NavRecastSpine.Finite(Station)||!NavRecastSpine.Finite(HighWater)||Station<0||HighWater<Station||Request==0&&(Phase!=0||Cursor!=0||Station!=0||HighWater!=0))throw new ArgumentException("Invalid Recast progress v2.");}
        public bool Equals(RecastMemberProgress p)=>Request==p.Request&&Phase==p.Phase&&Cursor==p.Cursor&&BitConverter.DoubleToInt64Bits(Station)==BitConverter.DoubleToInt64Bits(p.Station)&&BitConverter.DoubleToInt64Bits(HighWater)==BitConverter.DoubleToInt64Bits(p.HighWater);
        internal void Write(BinaryWriter w){Validate();w.Write(Request);w.Write((int)Phase);w.Write(Cursor);w.Write(Station);w.Write(HighWater);}
        internal static RecastMemberProgress Read(BinaryReader r)=>new RecastMemberProgress(r.ReadInt64(),(RecastMovementPhase)r.ReadInt32(),r.ReadInt32(),r.ReadDouble(),r.ReadDouble());
        internal NavRecastStepState ToStep()=>new NavRecastStepState((int)Phase,Cursor,Station,HighWater);
        internal static RecastMemberProgress FromStep(long request,NavRecastStepState state)=>new RecastMemberProgress(request,(RecastMovementPhase)state.Phase,state.Cursor,state.Station,state.HighWater);
    }
}
