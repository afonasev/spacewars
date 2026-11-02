namespace Spacewars.Simulation
{
    public sealed class OrbitalMineSnapshot
    {
        public int Id{get;} public PlayableOwner Owner{get;} public NavPoint Point{get;} public double ModelRadius{get;} public double ModelHeight{get;} public double BlastRadius{get;}
        public OrbitalMineSnapshot(int id,PlayableOwner owner,NavPoint point,double modelRadius,double modelHeight,double blastRadius){Id=id;Owner=owner;Point=point;ModelRadius=modelRadius;ModelHeight=modelHeight;BlastRadius=blastRadius;}
    }
    public sealed class OrbitalMineBlastSnapshot
    {
        public int Id{get;} public PlayableOwner Owner{get;} public NavPoint Point{get;} public double Radius{get;} public long Tick{get;} public double Duration{get;}
        public OrbitalMineBlastSnapshot(int id,PlayableOwner owner,NavPoint point,double radius,long tick,double duration){Id=id;Owner=owner;Point=point;Radius=radius;Tick=tick;Duration=duration;}
    }
}
