using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        internal void ValidateAiKnowledge(AiKnowledgeState state)
        {
            var owner=OwnerFor(state.OwnerId);
            AiStateWire.Require(state.Generation==navigation.Generation&&state.ObservationTick<=Tick&&state.Contacts.All(c=>c.Id<nextId&&Owners.Contains(c.Owner)&&Hostile(c.Owner,owner)&&Math.Abs(c.Position.X)<=profile.ArenaHalfExtent&&Math.Abs(c.Position.Z)<=profile.ArenaHalfExtent)&&state.Areas.All(a=>sites.ContainsKey(a.AreaId)),"knowledge owner/area binding");
        }
    }
    public sealed class AiKnowledgeTracker
    {
        private AiKnowledgeState state;
        public AiKnowledgeTracker(string ownerId,long generation)
        {if(string.IsNullOrWhiteSpace(ownerId)||generation<=0)throw new ArgumentException("Invalid knowledge identity.");state=new AiKnowledgeState(ownerId,generation,-1,Array.Empty<AiKnownContact>(),Array.Empty<AiVisitedArea>());}
        public AiKnowledgeState Capture()=>state;
        public IReadOnlyList<int> Observe(PlayableAiObservation observation,AiProfile profile)
        {
            if(observation.OwnerId!=state.OwnerId||observation.Generation!=state.Generation||observation.Tick<state.ObservationTick)throw new ArgumentException("Foreign or stale knowledge observation.");
            var contacts=state.Contacts.ToDictionary(c=>c.Id);
            var seen=new HashSet<int>();
            foreach(var e in observation.Entities.Where(e=>observation.IsHostile(e.Owner)&&e.Health>0&&observation.Vision!=null&&observation.Vision.IsVisible(e.Position)))
            {seen.Add(e.Id);contacts[e.Id]=new AiKnownContact(e.Id,e.Owner,false,(int)e.Kind,e.Position,e.Health,observation.Tick,1,true);}
            foreach(var b in observation.Buildings.Where(b=>observation.IsHostile(b.Owner)&&b.Health>0&&observation.Vision!=null&&observation.Vision.IsVisible(b.Position)))
            {seen.Add(b.Id);contacts[b.Id]=new AiKnownContact(b.Id,b.Owner,true,(int)b.Kind,b.Position,b.Health,observation.Tick,1,true);}
            foreach(var c in contacts.Values.ToArray())
            {
                if(seen.Contains(c.Id))continue;
                // Mobile contacts may have moved elsewhere. Only a static building's
                // visible former anchor can confirm its absence; hidden death cannot.
                if(c.Building&&observation.Vision!=null&&observation.Vision.IsVisible(c.Position)){contacts.Remove(c.Id);continue;}
                double age=(observation.Tick-c.LastSeenTick)/30d;
                contacts[c.Id]=new AiKnownContact(c.Id,c.Owner,c.Building,c.Kind,c.Position,c.HealthAtLastSeen,c.LastSeenTick,Math.Pow(2,-age/profile.Value("scouting.contactHalfLifeSeconds")),false);
            }
            var areas=state.Areas.ToDictionary(a=>a.AreaId);var newlyCovered=new List<int>();
            long revisit=AiProfile.SecondsToTicks(profile.Value("scouting.revisitSeconds"),30);
            foreach(var delta in observation.Intel.Areas)
            {
                var envelope=delta.Envelope;areas.TryGetValue(envelope.AreaId,out var prior);
                bool occupied=observation.Buildings.Any(b=>b.Health>0&&observation.Vision!=null&&observation.Vision.IsVisible(b.Position)&&envelope.Contains(b.Position));
                if(delta.Full){if(prior==null||prior.ActuallyCoveredTick<state.ObservationTick)newlyCovered.Add(envelope.AreaId);areas[envelope.AreaId]=new AiVisitedArea(envelope.AreaId,observation.Tick,!occupied,false);}
                else if(prior!=null)areas[envelope.AreaId]=new AiVisitedArea(prior.AreaId,prior.ActuallyCoveredTick,prior.EmptyAtLastCoverage&&!occupied,observation.Tick-prior.ActuallyCoveredTick>=revisit);
            }
            state=new AiKnowledgeState(state.OwnerId,state.Generation,observation.Tick,contacts.Values,areas.Values);
            return Array.AsReadOnly(newlyCovered.ToArray());
        }
        internal void Rebind(AiProfile profile)
        {
            state=new AiKnowledgeState(state.OwnerId,state.Generation,state.ObservationTick,
                state.Contacts.Select(c=>new AiKnownContact(c.Id,c.Owner,c.Building,c.Kind,c.Position,c.HealthAtLastSeen,c.LastSeenTick,c.Visible?1:Math.Pow(2,-(state.ObservationTick-c.LastSeenTick)/30d/profile.Value("scouting.contactHalfLifeSeconds")),c.Visible)),
                state.Areas.Select(a=>new AiVisitedArea(a.AreaId,a.ActuallyCoveredTick,a.EmptyAtLastCoverage,state.ObservationTick-a.ActuallyCoveredTick>=AiProfile.SecondsToTicks(profile.Value("scouting.revisitSeconds"),30))));
        }
        internal void WriteState(BinaryWriter w)
        {
            WorldWire.String(w,state.OwnerId);w.Write(state.Generation);w.Write(state.ObservationTick);
            WorldWire.Array(w,state.Contacts.ToArray(),c=>{w.Write(c.Id);w.Write((int)c.Owner);w.Write(c.Building);w.Write(c.Kind);WorldWire.Write(w,c.Position);w.Write(c.HealthAtLastSeen);w.Write(c.LastSeenTick);WorldWire.Number(w,c.Confidence);w.Write(c.Visible);});
            WorldWire.Array(w,state.Areas.ToArray(),a=>{w.Write(a.AreaId);w.Write(a.ActuallyCoveredTick);w.Write(a.EmptyAtLastCoverage);w.Write(a.RevisitDue);});
        }
        internal static AiKnowledgeTracker ReadState(BinaryReader r,string owner,long generation,long tick,AiProfile profile)
        {
            var id=WorldWire.String(r);long gen=r.ReadInt64(),observed=r.ReadInt64();
            AiStateWire.Require(id==owner&&gen==generation&&observed>=-1&&observed<=tick,"knowledge identity/clock");
            var cs=WorldWire.Array(r,()=>new AiKnownContact(r.ReadInt32(),WorldWire.EnumValue<PlayableOwner>(r),WorldWire.Boolean(r),r.ReadInt32(),WorldWire.ReadPoint(r),r.ReadInt32(),r.ReadInt64(),WorldWire.Number(r),WorldWire.Boolean(r)));
            var areas=WorldWire.Array(r,()=>new AiVisitedArea(r.ReadInt32(),r.ReadInt64(),WorldWire.Boolean(r),WorldWire.Boolean(r)));
            AiStateWire.Require(cs.All(c=>c.Id>0&&c.HealthAtLastSeen>0&&c.LastSeenTick>=0&&c.LastSeenTick<=observed&&c.Confidence>=0&&c.Confidence<=1&&(!c.Visible||c.LastSeenTick==observed&&c.Confidence==1)&&Enum.IsDefined(c.Building?typeof(PlayableBuildingKind):typeof(PlayableEntityKind),c.Kind))&&cs.Select(c=>c.Id).Distinct().Count()==cs.Length,"knowledge contacts");
            AiStateWire.Require(areas.All(a=>a.AreaId>0&&a.ActuallyCoveredTick>=0&&a.ActuallyCoveredTick<=observed)&&areas.Select(a=>a.AreaId).Distinct().Count()==areas.Length,"knowledge areas");
            AiStateWire.Require(cs.Select(c=>c.Id).SequenceEqual(cs.Select(c=>c.Id).OrderBy(x=>x))&&areas.Select(a=>a.AreaId).SequenceEqual(areas.Select(a=>a.AreaId).OrderBy(x=>x)),"knowledge canonical ordering");
            AiStateWire.Require(cs.All(c=>c.Confidence==(c.Visible?1:Math.Pow(2,-(observed-c.LastSeenTick)/30d/profile.Value("scouting.contactHalfLifeSeconds"))))&&areas.All(a=>a.RevisitDue==(observed-a.ActuallyCoveredTick>=AiProfile.SecondsToTicks(profile.Value("scouting.revisitSeconds"),30))),"knowledge derived freshness");
            AiStateWire.Require(observed>=0||cs.Length==0&&areas.Length==0,"unobserved knowledge");
            var result=new AiKnowledgeTracker(owner,generation);result.state=new AiKnowledgeState(id,gen,observed,cs,areas);return result;
        }
    }
}
