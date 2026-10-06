using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Intent-only checkpoint; it deliberately contains no route worker/job handle.
    [Serializable] public sealed class PlayableRallyIntentState
    {
        public long Generation,Sequence; public int Building; public PlayableOwner Owner;
        public double TargetX,TargetZ;
    }
    internal sealed partial class PlayableDomain
    {
        private static readonly PlayableEntityKind[] RallyKinds={PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval};
        private sealed class RallyWork
        {
            public PlayableRallyIntentState Intent; public int Kind,Candidate;
            public NavGeometry Geometry; public NavigationRequest Request;
            public NavPoint Target=>new NavPoint(Intent.TargetX,Intent.TargetZ);
        }
        private readonly AuthoritySlotMap<int,RallyWork> rallyWork=new AuthoritySlotMap<int,RallyWork>();
        private readonly List<PlayableCommandReceipt> rallyReceipts=new List<PlayableCommandReceipt>();
        private bool rallyPaused;
        private NavPoint? PendingRally(int id)=>rallyWork.TryGetValue(id,out var work)?(NavPoint?)work.Target:null;
        private PlayableCommandStatus Rally(int id,NavPoint target,PlayableOwner issuer,long sequence,out string message)
        {
            message="Select a ready live owned factory.";
            if(Producer(id,issuer)==null||Producer(id,issuer).Health<=0)return PlayableCommandStatus.InvalidEntity;
            if(rallyWork.TryGetValue(id,out var old))FinishRally(id,old,PlayableCommandStatus.Cancelled,"Superseded rally request.");
            rallyWork[id]=new RallyWork{Intent=new PlayableRallyIntentState{Generation=navigation.Generation,Sequence=sequence,Building=id,Owner=issuer,TargetX=target.X,TargetZ=target.Z}};
            // Complete route admission is external to the combat authority tick. The
            // previous point remains installed until every compatible type has proof.
            message="Rally validation pending.";return PlayableCommandStatus.Accepted;
        }
        internal PlayableRallyIntentState[] CaptureRallyIntents()=>rallyWork.Values.OrderBy(w=>w.Intent.Building).Select(w=>new PlayableRallyIntentState{
            Generation=w.Intent.Generation,Sequence=w.Intent.Sequence,Building=w.Intent.Building,Owner=w.Intent.Owner,TargetX=w.Intent.TargetX,TargetZ=w.Intent.TargetZ}).ToArray();
        internal void RestoreRallyIntents(PlayableRallyIntentState[] intents)
        {
            if(intents==null||rallyWork.Count!=0||intents.Any(i=>i==null||i.Generation!=navigation.Generation||i.Sequence<1||Producer(i.Building,i.Owner)==null)||intents.Select(i=>i.Building).Distinct().Count()!=intents.Length)
                throw new ArgumentException("Invalid rally intents.");
            foreach(var i in intents)rallyWork[i.Building]=new RallyWork{Intent=new PlayableRallyIntentState{Generation=i.Generation,Sequence=i.Sequence,Building=i.Building,Owner=i.Owner,TargetX=i.TargetX,TargetZ=i.TargetZ}};
        }
        internal PlayableCommandReceipt[] DrainRallyReceipts(){var result=rallyReceipts.ToArray();rallyReceipts.Clear();return result;}
        private void FinishRally(int id,RallyWork work,PlayableCommandStatus status,string message)
        {
            navigation.CancelProbe(id);rallyWork.Remove(id);
            rallyReceipts.Add(new PlayableCommandReceipt(work.Intent.Sequence,Tick,status,message,0,OwnerName(work.Intent.Owner)));
        }
        internal void CancelAllRally(){foreach(var row in rallyWork.ToArray())FinishRally(row.Key,row.Value,PlayableCommandStatus.Cancelled,"Runtime stopped.");}
        internal void SetRallyPaused(bool paused)
        {
            if(paused&&!rallyPaused)foreach(var row in rallyWork){navigation.CancelProbe(row.Key);row.Value.Request=null;row.Value.Geometry=null;row.Value.Kind=0;row.Value.Candidate=0;}
            rallyPaused=paused;
        }
        private static bool CompleteRallyRoute(NavigationRequest request,NavPoint[] route)
        {
            if(route.Length==0)return false;
            var end=route[route.Length-1];
            // Frozen browser end<0.001 protocol tolerance, not gameplay tuning.
            if(Math.Sqrt(Math.Pow(end.X-request.Goal.X,2)+Math.Pow(end.Z-request.Goal.Z,2))>=.001)return false;
            var previous=request.Start;
            foreach(var point in route){if(!request.Geometry.SegmentFree(previous,point,request.Profile.Radius))return false;previous=point;}
            return request.Geometry.SegmentFree(previous,request.Goal,request.Profile.Radius);
        }
        private void AdvanceRally()
        {
            if(rallyPaused)return;
            var answers=new Dictionary<NavigationRequest,NavigationAnswer>();
            while(navigation.TryProbeAnswer(out var answer))answers[answer.Request]=answer;
            foreach(var row in rallyWork.OrderBy(r=>r.Key).ToArray())
            {
                int id=row.Key;var work=row.Value;var b=Producer(id,work.Intent.Owner);
                if(b==null||b.Health<=0||Outcome!=PlayableMatchOutcome.Playing||work.Intent.Generation!=navigation.Generation){FinishRally(id,work,PlayableCommandStatus.Cancelled,"Producer no longer live/ready/owned.");continue;}
                if(!ReferenceEquals(work.Geometry,Geometry)){
                    navigation.CancelProbe(id);work.Geometry=Geometry;work.Request=null;work.Kind=0;work.Candidate=0;
                }
                if(work.Request!=null){
                    if(!answers.TryGetValue(work.Request,out var answer))continue;
                    if(CompleteRallyRoute(work.Request,answer.CopyRoute())){work.Kind++;work.Candidate=0;}
                    else work.Candidate++;
                    work.Request=null;
                }
                if(work.Kind==RallyKinds.Length){b.Rally=work.Target;b.HasRally=true;FinishRally(id,work,PlayableCommandStatus.Applied,"Rally updated.");continue;}
                var kind=RallyKinds[work.Kind];double radius=PlayableUnitRules.Radius(profile,kind);
                if(!Geometry.IsFree(work.Target,radius)){FinishRally(id,work,PlayableCommandStatus.InvalidTarget,"Invalid rally footprint.");continue;}
                var exits=FactoryExitCandidates(b,kind);
                // Bodies/HOLD are temporary occupancy, never topology obstacles.
                while(work.Candidate<exits.Length&&!Geometry.IsFree(exits[work.Candidate],radius))work.Candidate++;
                if(work.Candidate==exits.Length){FinishRally(id,work,PlayableCommandStatus.InvalidTarget,"Unreachable rally point.");continue;}
                work.Request=navigation.Probe(id,work.Intent.Sequence,profile.Navigation.ForUnit(radius,PlayableUnitRules.Speed(profile,kind,false),PlayableUnitRules.Turn(profile,kind)),exits[work.Candidate],work.Target);
            }
        }
    }
}
