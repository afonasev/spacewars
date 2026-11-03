using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private sealed class ObservedSound
        {internal PlayableSoundEvent Event;internal int Mask;internal PlayableOwner? SubjectOwner;}
        private readonly Queue<ObservedSound> soundJournal=new Queue<ObservedSound>();
        private long nextSoundEvent;
        private void Sound(PlayableSoundKind kind,NavPoint position,PlayableEntityKind weapon,PlayableOwner? subjectOwner=null,bool building=false,int unitId=0,double hullHeading=0,double altitude=0,int shotSide=-1,NavPoint? notificationPoint=null)
        {
            if(subjectOwner.HasValue){
                if(kind==PlayableSoundKind.ConstructionComplete)MatchEvent(MatchEventKind.BuildingCompleted,subjectOwner.Value,position);
                else if(kind==PlayableSoundKind.ProductionComplete)MatchEvent(MatchEventKind.UnitReady,subjectOwner.Value,notificationPoint??position);
                else if(kind==PlayableSoundKind.UpgradeComplete)MatchEvent(MatchEventKind.UpgradeCompleted,subjectOwner.Value,position);
                else if(kind==PlayableSoundKind.ResearchComplete)MatchEvent(MatchEventKind.ResearchCompleted,subjectOwner.Value,position);
            }
            PublishSound(new PlayableSoundEvent(++nextSoundEvent,Tick,kind,position,weapon,building,unitId,hullHeading,altitude:altitude,owner:subjectOwner,shotSide:shotSide),subjectOwner);
        }
        private void DirectImpactSound(Projectile projectile,int targetId,NavPoint position,bool building=false)
            =>DirectImpactSoundAt(projectile,targetId,position,building,null);
        private void DirectImpactSoundAt(Projectile projectile,int targetId,NavPoint position,bool building,BallisticPoint? physicalPoint)
        {
            var contact=new PlayableDirectContact(projectile.Id,targetId,new BallisticPoint(projectile.Position.X,projectile.Height,projectile.Position.Z),physicalPoint);
            PublishSound(new PlayableSoundEvent(++nextSoundEvent,Tick,PlayableSoundKind.Impact,position,projectile.Kind,building,contact:contact),null);
        }
        private void PublishSound(PlayableSoundEvent fact,PlayableOwner? subjectOwner)
        {
            var kind=fact.Kind;var position=fact.Position;
            int mask=0;
            foreach(var owner in Owners)if(subjectOwner==owner||!PlayableSoundPolicy.OwnerOnly(kind)&&Vision(owner).IsVisible(position))mask|=1<<(int)owner;
            while(soundJournal.Count>0&&(soundJournal.Count>=256||Tick-soundJournal.Peek().Event.Tick>30))soundJournal.Dequeue();
            soundJournal.Enqueue(new ObservedSound{Event=fact,Mask=mask,SubjectOwner=subjectOwner});
        }
        private PlayableSoundEvent[] Sounds(PlayableOwner? observer=null)
            =>soundJournal.Where(e=>Tick-e.Event.Tick<=30&&(!observer.HasValue||
                (e.Mask&(1<<(int)observer.Value))!=0&&(e.SubjectOwner==observer.Value||Vision(observer.Value).IsVisible(e.Event.Position))))
                .Select(e=>e.Event).ToArray();
        private readonly Dictionary<int,bool> soundMotion=new Dictionary<int,bool>();
        private void ObserveSoundMotion()
        {
            foreach(var u in units.Values)if(TryActor(u.Id,out var n))
            {
                if(soundMotion.TryGetValue(u.Id,out var wasMoving)&&wasMoving!=n.Moving)
                    Sound(n.Moving?PlayableSoundKind.MovementStarted:PlayableSoundKind.MovementStopped,n.Position,u.Kind,u.Owner);
                soundMotion[u.Id]=n.Moving;
            }
            foreach(var id in soundMotion.Keys.Where(id=>!units.ContainsKey(id)).ToArray())soundMotion.Remove(id);
        }
        private void SoundUnitDamage(Unit u)
        {
            if(u.Health>0&&TryActor(u.Id,out var n))Sound(PlayableSoundKind.HeavyDamage,n.Position,u.Kind,u.Owner);
        }
    }
}
