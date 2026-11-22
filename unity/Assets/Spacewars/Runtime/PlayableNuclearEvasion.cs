using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private const string NuclearEvasionSource = "orbital-nuclear-evacuation";
        private sealed class NuclearEvacuation
        {
            internal long Strike;
            internal long Sequence;
            internal PlayableTacticalOrderKind? PreviousKind;
            internal NavPoint PreviousPoint;
            internal NavLocation? PreviousLocation;
            internal int PreviousTarget;
        }
        private readonly Dictionary<int,NuclearEvacuation> nuclearEvacuations = new Dictionary<int,NuclearEvacuation>();

        private static double Reaction(PlayableProfileData terms,AiDifficulty difficulty)=>difficulty switch
            {
                AiDifficulty.Recruit => terms.orbitalNuclearCadetReaction,
                AiDifficulty.Fighter => terms.orbitalNuclearFighterReaction,
                AiDifficulty.Veteran => terms.orbitalNuclearVeteranReaction,
                _ => terms.orbitalNuclearExpertReaction
            };

        internal void UpdateNuclearEvasion(string ownerId, AiDifficulty difficulty)
        {
            if (nuclearStrikes.Count==0 && nuclearEvacuations.Count==0 || !Authorizes(ownerId) ||
                Outcome!=PlayableMatchOutcome.Playing) return;
            var owner=OwnerFor(ownerId);
            var terms=OrbitalData;
            double reaction=Reaction(terms,difficulty);
            var vision=Vision(owner);
            foreach (var strike in nuclearStrikes)
            {
                if (reaction<0 || strike.ImpactTime>=0) continue;
                if (!strike.FirstObserved.ContainsKey(owner) &&
                    (!Hostile(owner,strike.Owner) || vision.IsVisible(strike.Center.Position) ||
                        strike.Rim.Any(segment => vision.IsVisible(segment.From.Position) && vision.IsVisible(segment.To.Position))))
                    strike.FirstObserved.Add(owner,elapsed);
            }
            foreach (int id in nuclearEvacuations.Keys.OrderBy(id=>id).ToArray())
            {
                if (!units.TryGetValue(id,out var unit)) { nuclearEvacuations.Remove(id); continue; }
                if (unit.Owner!=owner) continue;
                var evacuation=nuclearEvacuations[id];
                if (unit.LastOrder?.Sequence!=evacuation.Sequence) { nuclearEvacuations.Remove(id); continue; }
                if (nuclearStrikes.Any(strike=>strike.Id==evacuation.Strike && strike.ImpactTime<0)) continue;
                nuclearEvacuations.Remove(id);
                var kind=evacuation.PreviousKind switch
                {
                    PlayableTacticalOrderKind.Move => PlayableCommandKind.Move,
                    PlayableTacticalOrderKind.AttackMove => PlayableCommandKind.AttackMove,
                    PlayableTacticalOrderKind.Attack => PlayableCommandKind.Attack,
                    PlayableTacticalOrderKind.Follow => PlayableCommandKind.Follow,
                    _ => PlayableCommandKind.Stop
                };
                var status=IssueNuclearMovement(ownerId,id,kind,evacuation.PreviousPoint,evacuation.PreviousLocation,
                    evacuation.PreviousTarget,out _);
                if (status==PlayableCommandStatus.InvalidTarget && kind==PlayableCommandKind.Attack)
                    IssueNuclearMovement(ownerId,id,PlayableCommandKind.AttackMove,evacuation.PreviousPoint,null,0,out _);
            }
            if (reaction<0 || BackgroundDecisionProjections) return;
            foreach(var plan in NuclearPlans(owner, reaction))ApplyNuclearPlan(ownerId,plan);
        }

        internal sealed class NuclearEscapePlan
        {
            internal long Strike;
            internal int Unit;
            internal NavPoint[] Goals;
        }

        internal Func<NuclearEscapePlan[]> CaptureNuclearPlans(PlayableSnapshot view,AiDifficulty difficulty)
        {
            if(nuclearStrikes.Count==0)return null;
            var owner=view.Owner;
            var terms=OrbitalData;
            double reaction=Reaction(terms,difficulty);
            if(reaction<0)return null;
            var strikes=EligibleStrikes(owner,reaction);
            if(strikes.Length==0)return null;
            var termsProfile=profile;
            var excluded=nuclearEvacuations.Keys.ToHashSet();
            return ()=>PlanNuclearEscapes(termsProfile,strikes,view.Entities.Where(unit=>unit.Owner==owner &&
                unit.StasisRemaining<=0 && unit.OrderStamp?.Origin!=PlayableOrderOrigin.Human && !excluded.Contains(unit.Id)));
        }

        private (long Id,NavLocation Center,double Radius)[] EligibleStrikes(PlayableOwner owner,double reaction)
        {
            var vision=Vision(owner);
            return nuclearStrikes.Where(strike=>strike.ImpactTime<0 && strike.FirstObserved.TryGetValue(owner,out var observed) &&
                elapsed+1e-9>=observed+reaction && (!Hostile(owner,strike.Owner)||vision.IsVisible(strike.Center.Position)))
                .Select(s=>(s.Id,s.Center,s.Radius)).ToArray();
        }

        private NuclearEscapePlan[] NuclearPlans(PlayableOwner owner,double reaction)
        {
            var strikes=EligibleStrikes(owner,reaction);
            if(strikes.Length==0)return Array.Empty<NuclearEscapePlan>();
            var snapshot=PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,0,owner);
            return PlanNuclearEscapes(profile,strikes,snapshot.Entities.Where(unit=>unit.Owner==owner &&
                unit.StasisRemaining<=0 && unit.OrderStamp?.Origin!=PlayableOrderOrigin.Human && !nuclearEvacuations.ContainsKey(unit.Id)));
        }

        private static NuclearEscapePlan[] PlanNuclearEscapes(PlayableProfile profile,
            (long Id,NavLocation Center,double Radius)[] strikes,IEnumerable<PlayableEntitySnapshot> units)
        {
            var plans=new List<NuclearEscapePlan>();
            var actors=units.OrderBy(unit=>unit.Id).ToArray();
            foreach(var strike in strikes)
                foreach(var unit in actors){
                    bool air=PlayableUnitRules.Aircraft(unit.Kind);
                    double? height=air?unit.Altitude+PlayableUnitRules.AircraftBodyHeight(profile,unit.Kind)/2:(double?)null;
                    if(!OrbitalRepairNanobots.Contains(profile.AuthoredMap,strike.Center,strike.Radius,unit.Position,
                        air?null:unit.Location,height))continue;
                    double angle=Math.Atan2(unit.Position.Z-strike.Center.Position.Z,unit.Position.X-strike.Center.Position.X);
                    double radius=strike.Radius+PlayableUnitRules.Radius(profile,unit.Kind);
                    var goals=new NavPoint[8];
                    for(int i=0;i<goals.Length;i++){
                        double heading=angle+((i+1)/2)*(i%2==0?-1:1)*Math.PI/4;
                        goals[i]=new NavPoint(strike.Center.Position.X+Math.Cos(heading)*radius,
                            strike.Center.Position.Z+Math.Sin(heading)*radius);
                    }
                    plans.Add(new NuclearEscapePlan{Strike=strike.Id,Unit=unit.Id,Goals=goals});
                }
            return plans.ToArray();
        }

        internal void ApplyNuclearPlan(string ownerId,NuclearEscapePlan plan)
        {
            var owner=OwnerFor(ownerId);
            var strike=nuclearStrikes.FirstOrDefault(s=>s.Id==plan.Strike&&s.ImpactTime<0);
            if(strike==null || Hostile(owner,strike.Owner)&&!Vision(owner).IsVisible(strike.Center.Position) ||
                !units.TryGetValue(plan.Unit,out var unit)||unit.Owner!=owner||nuclearEvacuations.ContainsKey(unit.Id)||
                unit.LastOrder?.Origin==PlayableOrderOrigin.Human||!NuclearUnitInside(strike,unit,out var actor))return;
            var previous=unit.CurrentOrder;
            foreach(var goal in plan.Goals){
                NavLocation? location=unit.Air==null?new NavLocation(goal,actor.Location.SurfaceId):(NavLocation?)null;
                if(IssueNuclearMovement(ownerId,unit.Id,PlayableCommandKind.Move,goal,location,0,out var sequence)!=PlayableCommandStatus.Applied)continue;
                nuclearEvacuations.Add(unit.Id,new NuclearEvacuation{
                    Strike=strike.Id,Sequence=sequence,PreviousKind=previous?.Kind,PreviousPoint=previous?.Destination??actor.Position,
                    PreviousTarget=previous?.TargetId??0,PreviousLocation=previous?.DestinationLocation
                });
                break;
            }
        }

        private PlayableCommandStatus IssueNuclearMovement(string ownerId,int id,PlayableCommandKind kind,NavPoint point,
            NavLocation? location,int target,out long sequence)
        {
            sequence=AllocateAiSequence();
            var command=new PlayableCommand(navigation.Generation,sequence,ownerId,kind,new[]{id},target:point,
                targetId:target,origin:PlayableOrderOrigin.Ai,source:NuclearEvasionSource,jobId:sequence,actionId:sequence);
            if (location.HasValue) command=command.WithResolvedLocation(location.Value);
            return Apply(command,out _);
        }

        private void WriteNuclearEvacuations(BinaryWriter writer)
        {
            var active=nuclearEvacuations.Where(entry=>units.ContainsKey(entry.Key)).OrderBy(entry=>entry.Key).ToArray();
            writer.Write(active.Length);
            foreach (var entry in active)
            {
                var evacuation=entry.Value;
                writer.Write(entry.Key);
                writer.Write(evacuation.Strike);
                writer.Write(evacuation.Sequence);
                writer.Write(evacuation.PreviousKind.HasValue);
                if (evacuation.PreviousKind.HasValue) writer.Write((int)evacuation.PreviousKind.Value);
                WorldWire.Number(writer,evacuation.PreviousPoint.X);
                WorldWire.Number(writer,evacuation.PreviousPoint.Z);
                writer.Write(evacuation.PreviousTarget);
                writer.Write(evacuation.PreviousLocation.HasValue);
                if (evacuation.PreviousLocation.HasValue)
                    WorldWire.String(writer,evacuation.PreviousLocation.Value.SurfaceId);
            }
        }

        private void ReadNuclearEvacuations(BinaryReader reader)
        {
            int count=reader.ReadInt32();
            if (count<0 || count>units.Count) throw new ArgumentException("Invalid evacuation count.");
            for (int i=0;i<count;i++)
            {
                int unit=reader.ReadInt32();
                var evacuation=new NuclearEvacuation { Strike=reader.ReadInt64(),Sequence=reader.ReadInt64() };
                if (WorldWire.Boolean(reader)) evacuation.PreviousKind=WorldWire.EnumValue<PlayableTacticalOrderKind>(reader);
                evacuation.PreviousPoint=new NavPoint(WorldWire.Number(reader),WorldWire.Number(reader));
                evacuation.PreviousTarget=reader.ReadInt32();
                if (WorldWire.Boolean(reader)) evacuation.PreviousLocation=new NavLocation(evacuation.PreviousPoint,WorldWire.String(reader));
                if (!units.ContainsKey(unit) || evacuation.Sequence<=0 || evacuation.Strike<0)
                    throw new ArgumentException("Invalid nuclear evacuation.");
                nuclearEvacuations.Add(unit,evacuation);
            }
        }
    }
}
