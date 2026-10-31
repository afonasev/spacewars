using System;
namespace Spacewars.Simulation
{
    // Immutable authority projection. No HP/selection state; an explicit hit removes it.
    public sealed class PlayableWreckSnapshot
    {
        public int Id{get;} public PlayableOwner Owner{get;} public PlayableEntityKind Kind{get;}
        public NavPoint Position{get;} public double Heading{get;} public double Radius{get;}
        public double DeathHeight{get;} public double Burial{get;} public int Variant{get;} public bool Upgraded{get;}
        public long DeathTick{get;} public long LandTick{get;} public long RevealTick{get;}
        public long SinkTick{get;} public long ExpireTick{get;} public long DestroyedTick{get;}
        public PlayableEntityKind ImpactKind{get;} public bool HasGround=>LandTick>=0;
        public bool Blocks(long tick)=>HasGround&&tick>=LandTick&&tick<ExpireTick&&DestroyedTick<0;
        public PlayableWreckSnapshot(int id,PlayableOwner owner,PlayableEntityKind kind,NavPoint position,double heading,double radius,double deathHeight,double burial,int variant,long deathTick,long landTick,long revealTick,long sinkTick,long expireTick,long destroyedTick=-1,PlayableEntityKind impactKind=PlayableEntityKind.Tank,bool upgraded=false)
        {Upgraded=upgraded;Id=id;Owner=owner;Kind=kind;Position=position;Heading=heading;Radius=radius;DeathHeight=deathHeight;Burial=burial;Variant=variant;DeathTick=deathTick;LandTick=landTick;RevealTick=revealTick;SinkTick=sinkTick;ExpireTick=expireTick;DestroyedTick=destroyedTick;ImpactKind=impactKind;}
        public PlayableWreckSnapshot Destroy(long tick,PlayableEntityKind weapon)=>new PlayableWreckSnapshot(Id,Owner,Kind,Position,Heading,Radius,DeathHeight,Burial,Variant,DeathTick,LandTick,RevealTick,SinkTick,ExpireTick,tick,weapon,Upgraded);
        public PlayableWreckSnapshot WithoutLanding(long expire,double burstHeight)=>new PlayableWreckSnapshot(Id,Owner,Kind,Position,Heading,Radius,burstHeight,Burial,Variant,DeathTick,-1,LandTick,LandTick,expire,upgraded:Upgraded);
    }
}
