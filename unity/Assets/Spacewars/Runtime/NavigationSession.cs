using System;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    [Serializable] public sealed class NavigationGeometryState
    {
        public double HalfExtent; public int Revision; public NavObstacle[] Obstacles;
    }
    [Serializable] public sealed class NavigationRequestState
    {
        public long Session, Request, Order; public int Entity;
        public NavPoint Start, Goal; public string ProfileId; public int ProfileRevision;
        public double[] ProfileValues; public NavigationGeometryState Geometry,BaseGeometry; public string HeldIdentity;
        public bool UsesCurrentGeometry,UsesCurrentBaseGeometry;
        public int GeometryIndex,BaseGeometryIndex;
    }
    [Serializable] public sealed class NavigationAnswerState
    {
        public int RequestIndex; public NavPoint[] Route;
    }
    [Serializable] public sealed class NavigationEntityPointState
    {
        public int Entity; public NavPoint Point;
    }
    [Serializable] public sealed class NavigationMovementIdentityState
    { public int Entity; public string Identity; }
    [Serializable] public sealed class NavigationEntityOrderState
    {
        public int Entity; public long Order;
    }
    // Authority-owned checkpoint. External route workers must be stopped at a tick boundary.
    [Serializable] public sealed class NavigationSessionState
    {
        public long Generation, RequestSequence;
        public NavigationGeometryState Geometry, NavigationGeometry;
        public NavigationGeometryState[] GeometryTable; public int NavigationGeometryIndex;
        public NavCrowdSaveState Crowd;
        public NavigationRequestState[] Requests;
        public NavigationEntityOrderState[] Orders;
        public NavigationEntityPointState[] Reservations, RetainedGoals;
        public int[] PendingRequestIndices, RequestMailboxIndices, ProbeRequestIndices;
        public NavigationMovementIdentityState[] MovementIdentities;
        public NavigationAnswerState[] ProbeAnswers;
        public NavigationAnswerState[] AnswerMailbox;
        public int RejectedResults, AppliedResults, UnreachableResults;
        public int[][] CollectionLayouts;
    }
    public sealed class NavigationRequest
    {
        public NavigationRequest(long session,long request,int entity,long order,NavigationProfile profile,NavGeometry geometry,NavPoint start,NavPoint goal,NavGeometry baseGeometry=null,string heldIdentity=null)
        { BaseGeometry=baseGeometry??geometry;HeldIdentity=heldIdentity;Session=session;Request=request;Entity=entity;Order=order;Profile=profile;ProfileId=profile.ProfileId;ProfileRevision=profile.Revision;Geometry=geometry;Start=start;Goal=goal; }
        public NavGeometry BaseGeometry{get;} public string HeldIdentity{get;}
        public long Session { get; } public long Request { get; } public int Entity { get; } public long Order { get; }
        public NavigationProfile Profile{get;} public string ProfileId { get; } public int ProfileRevision { get; } public NavGeometry Geometry { get; }
        public NavPoint Start { get; } public NavPoint Goal { get; }
    }
    public sealed class NavigationAnswer
    {
        private readonly NavPoint[] route;
        public NavigationAnswer(NavigationRequest request,NavPoint[] route){Request=request;this.route=(NavPoint[])route.Clone();}
        public NavigationRequest Request{get;}
        public NavPoint[] CopyRoute(){return (NavPoint[])route.Clone();}
    }
    // Owned by one domain worker. Only the bounded mailboxes are cross-thread surfaces.
    public sealed class NavigationSession
    {
        private readonly AuthoritySlotMap<int,NavigationRequest> pending=new AuthoritySlotMap<int,NavigationRequest>();
        private readonly AuthoritySlotMap<int,long> orders=new AuthoritySlotMap<int,long>();
        private readonly AuthoritySlotMap<int,NavPoint> reservations=new AuthoritySlotMap<int,NavPoint>();
        // Latest intent is deliberately retained while a replacement is planned. A
        // geometry revision reissues it from the current authoritative position.
        private readonly AuthoritySlotMap<int,NavPoint> retainedGoals=new AuthoritySlotMap<int,NavPoint>();
        // Negative entity IDs are producer topology probes, never crowd actors.
        private readonly AuthoritySlotMap<int,NavigationRequest> probes=new AuthoritySlotMap<int,NavigationRequest>();
        private readonly Queue<NavigationAnswer> probeAnswers=new Queue<NavigationAnswer>();
        public NavigationRequest Probe(int producer,long order,NavigationProfile typed,NavPoint start,NavPoint goal)
        {
            var request=new NavigationRequest(Generation,++requestSequence,-producer,order,typed,navigationGeometry,start,goal);
            if(!Requests.TryEnqueue(request))return null;
            probes[-producer]=request;return request;
        }
        public void CancelProbe(int producer){probes.Remove(-producer);}
        public bool TryProbeAnswer(out NavigationAnswer answer){if(probeAnswers.Count==0){answer=null;return false;}answer=probeAnswers.Dequeue();return true;}
        private long requestSequence;
        private NavGeometry geometry;
        private NavGeometry navigationGeometry;
        private NavigationProfile profile;
        public NavigationSession(long generation,NavGeometry geometry,NavigationProfile profile,NavGeometry navigationGeometry=null){if(generation<1)throw new ArgumentOutOfRangeException("generation");Generation=generation;this.geometry=geometry;this.navigationGeometry=navigationGeometry??geometry;this.profile=profile;Crowd=new NavCrowd(geometry,profile);}
        public long Generation {get;}
        public NavCrowd Crowd {get;}
        public NavMailbox<NavigationRequest> Requests {get;} = new NavMailbox<NavigationRequest>();
        public NavMailbox<NavigationAnswer> Answers {get;} = new NavMailbox<NavigationAnswer>();
        public int RejectedResults {get;private set;}
        public int AppliedResults {get;private set;}
        public int PendingCount => pending.Count+probes.Count;
        public bool IsPending(int id) { return pending.ContainsKey(id); }
        private static NavigationGeometryState SaveGeometry(NavGeometry value)
        {
            var result=new NavigationGeometryState{HalfExtent=value.HalfExtent,Revision=value.Revision,Obstacles=new NavObstacle[value.Obstacles.Count]};
            for(int i=0;i<result.Obstacles.Length;i++)result.Obstacles[i]=value.Obstacles[i];return result;
        }
        private static bool SameGeometry(NavigationGeometryState saved,NavGeometry value)
        {
            if(saved==null||saved.Obstacles==null||saved.HalfExtent!=value.HalfExtent||saved.Revision!=value.Revision||saved.Obstacles.Length!=value.Obstacles.Count)return false;
            for(int i=0;i<saved.Obstacles.Length;i++)if(!saved.Obstacles[i].GeometryEquals(value.Obstacles[i]))return false;
            return true;
        }
        private static NavGeometry LoadGeometry(NavigationGeometryState saved){if(saved==null||saved.Obstacles==null)throw new ArgumentException("Missing saved geometry.");return new NavGeometry(saved.HalfExtent,saved.Obstacles,saved.Revision);}
        private static double[] SaveProfile(NavigationProfile value)
        {
            var result=new double[NavigationProfile.Metadata.Count];
            for(int i=0;i<result.Length;i++)result[i]=NavigationProfile.Metadata[i].Read(value);return result;
        }
        private static NavigationProfile LoadProfile(NavigationRequestState saved)
        {
            var v=saved.ProfileValues;
            if(v==null||v.Length!=NavigationProfile.Metadata.Count)throw new ArgumentException("Invalid request profile values.");
            return new NavigationProfile(saved.ProfileId,saved.ProfileRevision,v[7],v[8],v[9],v[10],v[11],v[12],v[13],v[0],v[1],v[2],v[3],v[4],v[5],v[6]);
        }
        public NavigationSessionState CaptureState()
        {
            // Caller owns the authority and route-worker quiescence boundary.
            var queuedRequests=Requests.CopyItems();var queuedAnswers=Answers.CopyItems();
            var accounted=new HashSet<NavigationRequest>(queuedRequests);
            foreach(var answer in queuedAnswers)if(answer!=null)accounted.Add(answer.Request);
            foreach(var pair in probes)if(!accounted.Contains(pair.Value))throw new InvalidOperationException("A rally probe is held outside the quiescent mailboxes.");
            foreach(var pair in pending)if(!accounted.Contains(pair.Value))
                throw new InvalidOperationException("A pending navigation request is held outside the quiescent mailboxes.");
            var all=new List<NavigationRequest>();var indices=new Dictionary<NavigationRequest,int>();
            Action<NavigationRequest> include=r=>{if(r==null)throw new InvalidOperationException("Null navigation request in checkpoint.");if(!indices.ContainsKey(r)){indices[r]=all.Count;all.Add(r);}};
            foreach(var pair in pending)include(pair.Value);
            foreach(var pair in probes)include(pair.Value);
            foreach(var answer in probeAnswers)include(answer.Request);
            foreach(var request in queuedRequests)include(request);
            foreach(var answer in queuedAnswers){if(answer==null)throw new InvalidOperationException("Null navigation answer in checkpoint.");include(answer.Request);}
            var geometries=new List<NavGeometry>{geometry};var geometryIndices=new Dictionary<NavGeometry,int>{{geometry,0}};
            Func<NavGeometry,int> geometryIndex=g=>{if(!geometryIndices.TryGetValue(g,out var index)){index=geometries.Count;geometryIndices.Add(g,index);geometries.Add(g);}return index;};
            int baseIndex=geometryIndex(navigationGeometry);
            var state=new NavigationSessionState{NavigationGeometryIndex=baseIndex,Generation=Generation,RequestSequence=requestSequence,Geometry=SaveGeometry(geometry),NavigationGeometry=SaveGeometry(navigationGeometry),
                Crowd=Crowd.CaptureState(),Requests=new NavigationRequestState[all.Count],Orders=new NavigationEntityOrderState[orders.Count],
                Reservations=new NavigationEntityPointState[reservations.Count],RetainedGoals=new NavigationEntityPointState[retainedGoals.Count],
                MovementIdentities=new NavigationMovementIdentityState[movementIdentities.Count],ProbeRequestIndices=new int[probes.Count],ProbeAnswers=new NavigationAnswerState[probeAnswers.Count],
                PendingRequestIndices=new int[pending.Count],RequestMailboxIndices=new int[queuedRequests.Length],AnswerMailbox=new NavigationAnswerState[queuedAnswers.Length],
                RejectedResults=RejectedResults,AppliedResults=AppliedResults,UnreachableResults=UnreachableResults};
            for(int i=0;i<all.Count;i++){
                var r=all[i];state.Requests[i]=new NavigationRequestState{Session=r.Session,Request=r.Request,Entity=r.Entity,Order=r.Order,
                    Start=r.Start,Goal=r.Goal,ProfileId=r.ProfileId,ProfileRevision=r.ProfileRevision,ProfileValues=SaveProfile(r.Profile),
                    Geometry=SaveGeometry(r.Geometry),BaseGeometry=SaveGeometry(r.BaseGeometry),GeometryIndex=geometryIndex(r.Geometry),BaseGeometryIndex=geometryIndex(r.BaseGeometry),HeldIdentity=r.HeldIdentity,UsesCurrentGeometry=Object.ReferenceEquals(r.Geometry,navigationGeometry),UsesCurrentBaseGeometry=Object.ReferenceEquals(r.BaseGeometry,navigationGeometry)};
            }
            int n=0;foreach(var pair in orders)state.Orders[n++]=new NavigationEntityOrderState{Entity=pair.Key,Order=pair.Value};
            n=0;foreach(var pair in reservations)state.Reservations[n++]=new NavigationEntityPointState{Entity=pair.Key,Point=pair.Value};
            n=0;foreach(var pair in retainedGoals)state.RetainedGoals[n++]=new NavigationEntityPointState{Entity=pair.Key,Point=pair.Value};
            n=0;foreach(var pair in pending)state.PendingRequestIndices[n++]=indices[pair.Value];
            n=0;foreach(var pair in probes)state.ProbeRequestIndices[n++]=indices[pair.Value];
            n=0;foreach(var pair in movementIdentities)state.MovementIdentities[n++]=new NavigationMovementIdentityState{Entity=pair.Key,Identity=pair.Value};
            n=0;foreach(var answer in probeAnswers)state.ProbeAnswers[n++]=new NavigationAnswerState{RequestIndex=indices[answer.Request],Route=answer.CopyRoute()};
            for(int i=0;i<queuedRequests.Length;i++)state.RequestMailboxIndices[i]=indices[queuedRequests[i]];
            for(int i=0;i<queuedAnswers.Length;i++)state.AnswerMailbox[i]=new NavigationAnswerState{RequestIndex=indices[queuedAnswers[i].Request],Route=queuedAnswers[i].CopyRoute()};
            state.CollectionLayouts=new[]{pending.CaptureLayout(),orders.CaptureLayout(),reservations.CaptureLayout(),retainedGoals.CaptureLayout(),probes.CaptureLayout(),movementIdentities.CaptureLayout()};
            state.GeometryTable=geometries.ConvertAll(SaveGeometry).ToArray();return state;
        }
        public void RestoreState(NavigationSessionState state)=>RestoreWorldState(state);
        internal NavigationRequest[] RestoreWorldState(NavigationSessionState state)
        {
            if(state==null||state.Generation!=Generation||state.RequestSequence<0||state.Requests==null||state.Orders==null||
                state.Reservations==null||state.RetainedGoals==null||state.PendingRequestIndices==null||state.RequestMailboxIndices==null||state.AnswerMailbox==null||
                state.RequestMailboxIndices.Length>NavMailbox<NavigationRequest>.Capacity||state.AnswerMailbox.Length>NavMailbox<NavigationAnswer>.Capacity||
                state.RejectedResults<0||state.AppliedResults<0||state.UnreachableResults<0||pending.Count!=0||orders.Count!=0||reservations.Count!=0||retainedGoals.Count!=0||
                Requests.Count!=0||Answers.Count!=0||Crowd.Units.Count!=0||!SameGeometry(state.Geometry,geometry)||!SameGeometry(state.NavigationGeometry,navigationGeometry))
                throw new ArgumentException("Navigation session checkpoint or destination mismatch.",nameof(state));
            if(state.GeometryTable==null||state.GeometryTable.Length<1||state.NavigationGeometryIndex<0||state.NavigationGeometryIndex>=state.GeometryTable.Length||!SameGeometry(state.GeometryTable[0],geometry)||!SameGeometry(state.GeometryTable[state.NavigationGeometryIndex],navigationGeometry)||(state.NavigationGeometryIndex==0)!=Object.ReferenceEquals(geometry,navigationGeometry))throw new ArgumentException("Invalid geometry identity table.");
            var geometryTable=new NavGeometry[state.GeometryTable.Length];for(int i=0;i<geometryTable.Length;i++)geometryTable[i]=i==0?geometry:i==state.NavigationGeometryIndex?navigationGeometry:LoadGeometry(state.GeometryTable[i]);
            var restored=new NavigationRequest[state.Requests.Length];long maxRequest=0;var requestIds=new HashSet<string>();
            for(int i=0;i<restored.Length;i++){
                var row=state.Requests[i];if(row==null||row.Session<1||row.Request<1||row.Order<1||row.Geometry==null||row.Geometry.Obstacles==null)
                    throw new ArgumentException("Invalid navigation request checkpoint.",nameof(state));
                if(row.GeometryIndex<0||row.GeometryIndex>=geometryTable.Length||row.BaseGeometryIndex<0||row.BaseGeometryIndex>=geometryTable.Length||!requestIds.Add(row.Session+":"+row.Request))throw new ArgumentException("Invalid request identity.");
                var requestProfile=LoadProfile(row);var requestGeometry=geometryTable[row.GeometryIndex];
                if(!SameGeometry(row.Geometry,requestGeometry)||!SameGeometry(row.BaseGeometry,geometryTable[row.BaseGeometryIndex])||row.UsesCurrentGeometry!=(row.GeometryIndex==state.NavigationGeometryIndex)||row.UsesCurrentBaseGeometry!=(row.BaseGeometryIndex==state.NavigationGeometryIndex))throw new ArgumentException("Invalid request geometry identity.");
                if(row.BaseGeometry==null||row.UsesCurrentBaseGeometry&&!SameGeometry(row.BaseGeometry,navigationGeometry)||row.UsesCurrentGeometry&&!SameGeometry(row.Geometry,navigationGeometry))throw new ArgumentException("Request geometry mismatch.",nameof(state));
                restored[i]=new NavigationRequest(row.Session,row.Request,row.Entity,row.Order,requestProfile,requestGeometry,row.Start,row.Goal,geometryTable[row.BaseGeometryIndex],row.HeldIdentity);
                if(row.Session==Generation)maxRequest=Math.Max(maxRequest,row.Request);
            }
            if(maxRequest>state.RequestSequence)throw new ArgumentException("Request sequence mismatch.",nameof(state));
            var nextOrders=new Dictionary<int,long>();foreach(var row in state.Orders){if(row==null||row.Order<0||nextOrders.ContainsKey(row.Entity))throw new ArgumentException("Invalid order checkpoint.",nameof(state));nextOrders.Add(row.Entity,row.Order);}
            var nextPending=new Dictionary<int,NavigationRequest>();foreach(int index in state.PendingRequestIndices){
                if(index<0||index>=restored.Length)throw new ArgumentException("Invalid pending request index.",nameof(state));
                var r=restored[index];if(nextPending.ContainsKey(r.Entity)||r.Session!=Generation||r.ProfileId!=profile.ProfileId||r.ProfileRevision!=profile.Revision||
                    !Object.ReferenceEquals(r.BaseGeometry,navigationGeometry)||!nextOrders.TryGetValue(r.Entity,out var order)||order!=r.Order)
                    throw new ArgumentException("Invalid pending request binding.",nameof(state));nextPending.Add(r.Entity,r);
            }
            var nextReservations=new Dictionary<int,NavPoint>();foreach(var row in state.Reservations){if(row==null||nextReservations.ContainsKey(row.Entity))throw new ArgumentException("Invalid reservation checkpoint.",nameof(state));nextReservations.Add(row.Entity,row.Point);}
            var nextGoals=new Dictionary<int,NavPoint>();foreach(var row in state.RetainedGoals){if(row==null||nextGoals.ContainsKey(row.Entity))throw new ArgumentException("Invalid retained goal checkpoint.",nameof(state));nextGoals.Add(row.Entity,row.Point);}
            foreach(int index in state.RequestMailboxIndices)if(index<0||index>=restored.Length)throw new ArgumentException("Invalid request mailbox index.",nameof(state));
            foreach(var answer in state.AnswerMailbox)if(answer==null||answer.RequestIndex<0||answer.RequestIndex>=restored.Length||answer.Route==null)throw new ArgumentException("Invalid answer mailbox entry.",nameof(state));
            var accountedIndices=new HashSet<int>(state.RequestMailboxIndices);
            foreach(var answer in state.AnswerMailbox)accountedIndices.Add(answer.RequestIndex);
            foreach(int index in state.PendingRequestIndices)if(!accountedIndices.Contains(index))throw new ArgumentException("Pending request is outside the checkpoint mailboxes.",nameof(state));
            var identityRows=state.MovementIdentities??Array.Empty<NavigationMovementIdentityState>();
            var identities=new HashSet<int>();foreach(var row in identityRows)if(row==null||row.Identity==null||!identities.Add(row.Entity)||!nextOrders.ContainsKey(row.Entity))throw new ArgumentException("Invalid movement identity.");
            foreach(var pair in nextGoals)if(!identities.Contains(pair.Key))throw new ArgumentException("Missing saved movement identity.");
            var probeIds=new HashSet<int>();foreach(int index in state.ProbeRequestIndices??Array.Empty<int>())if(index<0||index>=restored.Length||restored[index].Entity>=0||restored[index].Session!=Generation||!probeIds.Add(restored[index].Entity)||!accountedIndices.Contains(index)||!Object.ReferenceEquals(restored[index].Geometry,navigationGeometry))throw new ArgumentException("Invalid probe binding.");
            foreach(var answer in state.ProbeAnswers??Array.Empty<NavigationAnswerState>())if(answer==null||answer.RequestIndex<0||answer.RequestIndex>=restored.Length||restored[answer.RequestIndex].Entity>=0||answer.Route==null)throw new ArgumentException("Invalid probe answer.");
            Crowd.RestoreState(state.Crowd);
            foreach(var pair in nextOrders)orders.Add(pair.Key,pair.Value);
            foreach(var pair in nextPending)pending.Add(pair.Key,pair.Value);
            foreach(var row in state.MovementIdentities??Array.Empty<NavigationMovementIdentityState>())movementIdentities.Add(row.Entity,row.Identity);
            foreach(var pair in nextGoals)if(!movementIdentities.ContainsKey(pair.Key))throw new ArgumentException("Missing saved movement identity.");
            foreach(int index in state.ProbeRequestIndices??Array.Empty<int>())probes.Add(restored[index].Entity,restored[index]);
            foreach(var answer in state.ProbeAnswers??Array.Empty<NavigationAnswerState>())probeAnswers.Enqueue(new NavigationAnswer(restored[answer.RequestIndex],answer.Route));
            foreach(var pair in nextReservations)reservations.Add(pair.Key,pair.Value);
            foreach(var pair in nextGoals)retainedGoals.Add(pair.Key,pair.Value);
            if(state.CollectionLayouts==null||state.CollectionLayouts.Length!=6)throw new ArgumentException("Missing navigation collection layouts.");
            pending.RestoreLayout(state.CollectionLayouts[0]);orders.RestoreLayout(state.CollectionLayouts[1]);reservations.RestoreLayout(state.CollectionLayouts[2]);retainedGoals.RestoreLayout(state.CollectionLayouts[3]);probes.RestoreLayout(state.CollectionLayouts[4]);movementIdentities.RestoreLayout(state.CollectionLayouts[5]);
            requestSequence=state.RequestSequence;RejectedResults=state.RejectedResults;AppliedResults=state.AppliedResults;UnreachableResults=state.UnreachableResults;
            foreach(int index in state.RequestMailboxIndices)Requests.TryEnqueue(restored[index]);
            foreach(var answer in state.AnswerMailbox)Answers.TryEnqueue(new NavigationAnswer(restored[answer.RequestIndex],answer.Route));
            return restored;
        }
        public bool MoveGroup(int[] ids, NavPoint[] goals)
        {
            if(ids==null||goals==null||ids.Length!=goals.Length||Requests.Count+ids.Length>NavMailbox<NavigationRequest>.Capacity)return false;
            var unique=new HashSet<int>();
            for(int i=0;i<ids.Length;i++)if(!unique.Add(ids[i])||!Crowd.TryGet(ids[i],out var member)||!geometry.IsFree(goals[i],member.Radius))return false;
            for(int i=0;i<ids.Length;i++)Move(ids[i],goals[i]);
            return true;
        }
        public int UnreachableResults {get;private set;}
        public NavPoint[] AllocateArrivalSlots(NavPoint center,IReadOnlyList<int> group)
        {
            var members=new HashSet<int>(group);var occupied=new List<NavPoint>();
            foreach(var unit in Crowd.Units)if(!members.Contains(unit.Id)){occupied.Add(unit.Position);NavPoint reserved;if(reservations.TryGetValue(unit.Id,out reserved))occupied.Add(reserved);}
            double radius=0;foreach(int id in group)if(Crowd.TryGet(id,out var member))radius=Math.Max(radius,member.Radius);
            return NavArrivalAllocator.Allocate(geometry,profile.ForUnit(radius>0?radius:profile.Radius,profile.TankSpeed,profile.TankTurnSpeed),center,group.Count,occupied);
        }
        private string HeldIdentity(int id)
        {
            if(!Crowd.TryGet(id,out var mover))return "";
            var rows=new List<string>();var culture=System.Globalization.CultureInfo.InvariantCulture;
            foreach(var other in Crowd.Units)if(other.Id!=id&&other.Held&&other.Team==mover.Team)
                rows.Add(other.Id+":"+other.Incarnation+":"+other.MobilityRevision+":"+other.Team+":"+other.Position.X.ToString("R",culture)+":"+other.Position.Z.ToString("R",culture)+":"+other.Radius.ToString("R",culture));
            return string.Join("|",rows);
        }
        private NavGeometry MovementGeometry(int id)
        {
            Crowd.TryGet(id,out var mover);var solids=new List<NavObstacle>(navigationGeometry.Obstacles);
            foreach(var other in Crowd.Units)if(other.Id!=id&&other.Held&&other.Team==mover.Team)solids.Add(new NavObstacle(other.Position,other.Radius));
            return solids.Count==navigationGeometry.Obstacles.Count?navigationGeometry:new NavGeometry(navigationGeometry.HalfExtent,solids.ToArray(),navigationGeometry.Revision);
        }
        private readonly AuthoritySlotMap<int,string> movementIdentities=new AuthoritySlotMap<int,string>();
        private void RefreshHeldMovement()
        {
            foreach(var pair in new List<KeyValuePair<int,NavPoint>>(retainedGoals))if(Crowd.TryGet(pair.Key,out var mover)&&movementIdentities.TryGetValue(pair.Key,out var identity)&&identity!=HeldIdentity(pair.Key)){
                // Freeze old movement while replanning; a held mover awaiting explicit replacement remains held.
                if(!mover.Held)Crowd.Stop(pair.Key,false);
                Move(pair.Key,pair.Value);
            }
        }
        public bool Move(int id,NavPoint goal)
        {
            NavUnit unit;
            if(!Crowd.TryGet(id,out unit)||!geometry.IsFree(goal,unit.Radius))return false;
            long order;orders.TryGetValue(id,out order);order++;
            var request=new NavigationRequest(Generation,++requestSequence,id,order,profile.ForUnit(unit.Radius,unit.Speed,unit.TurnSpeed),MovementGeometry(id),unit.Position,goal,navigationGeometry,HeldIdentity(id));
            if(!Requests.TryEnqueue(request))return false;
            orders[id]=order; pending.Remove(id); retainedGoals[id]=goal;
            pending[id]=request;movementIdentities[id]=request.HeldIdentity;reservations[id]=goal;return true;
        }
        public void Stop(int id,bool hold){long order;orders.TryGetValue(id,out order);orders[id]=order+1;pending.Remove(id);reservations.Remove(id);retainedGoals.Remove(id);movementIdentities.Remove(id);Crowd.Stop(id,hold);}
        public bool Remove(int id)
        {
            long order; orders.TryGetValue(id,out order); orders[id]=order+1;
            pending.Remove(id); reservations.Remove(id); retainedGoals.Remove(id);movementIdentities.Remove(id); return Crowd.Remove(id);
        }
        public bool CanRebind => pending.Count<=NavMailbox<NavigationRequest>.Capacity;
        public void Rebind(NavigationProfile next)
        {
            if(!CanRebind)throw new InvalidOperationException("Navigation still has too many in-flight routes to rebind atomically.");
            // Geometry and existing routes are unchanged. Reissue only in-flight solves
            // so a rule change neither restarts moving actors nor floods the mailbox.
            var waiting=new List<NavigationRequest>(pending.Values);
            profile=next;Crowd.Rebind(next);pending.Clear();probes.Clear();probeAnswers.Clear();
            while(Requests.TryDequeue(out _)){}while(Answers.TryDequeue(out _)){}
            foreach(var request in waiting)if(Crowd.TryGet(request.Entity,out _))Move(request.Entity,request.Goal);
        }
        public void ChangeGeometry(NavGeometry next)
        {
            if(next==null)throw new ArgumentNullException("next");
            var intents=new List<KeyValuePair<int,NavPoint>>(retainedGoals);
            geometry=next;navigationGeometry=next;pending.Clear();reservations.Clear();probes.Clear();probeAnswers.Clear();
            while(Requests.TryDequeue(out _)){} while(Answers.TryDequeue(out _)){}
            Crowd.SetGeometry(next);
            foreach(var intent in intents) if(Crowd.TryGet(intent.Key,out _)) Move(intent.Key,intent.Value);
        }
        public void ApplyResults()
        {
            RefreshHeldMovement();
            NavigationAnswer answer;
            while(Answers.TryDequeue(out answer)){
                var r=answer.Request;NavigationRequest expected;
                if(r.Entity<0){
                    if(r.Session==Generation&&Object.ReferenceEquals(r.Geometry,navigationGeometry)&&probes.TryGetValue(r.Entity,out expected)&&Object.ReferenceEquals(expected,r)){
                        probes.Remove(r.Entity);probeAnswers.Enqueue(answer);
                    }else RejectedResults++;
                    continue;
                }
                if(r.Session!=Generation||r.ProfileId!=profile.ProfileId||r.ProfileRevision!=profile.Revision||!Object.ReferenceEquals(r.BaseGeometry,navigationGeometry)||r.HeldIdentity!=HeldIdentity(r.Entity)
                    ||!pending.TryGetValue(r.Entity,out expected)||expected.Request!=r.Request||expected.Order!=r.Order||!Object.ReferenceEquals(expected,r)){RejectedResults++;continue;}
                pending.Remove(r.Entity);var route=answer.CopyRoute();
                if(route.Length==0){UnreachableResults++;reservations.Remove(r.Entity);retainedGoals.Remove(r.Entity);if(!Crowd.TryGet(r.Entity,out var heldUnreachable)||!heldUnreachable.Held)Crowd.Reject(r.Entity,NavigationOutcome.Unreachable);continue;}
                bool valid=Crowd.TryGet(r.Entity,out var actor);var previous=valid?actor.Position:r.Start;
                foreach(var point in route){if(!r.Geometry.SegmentFree(previous,point,r.Profile.Radius)){valid=false;break;}previous=point;}
                if(valid&&Crowd.SetRoute(r.Entity,r.Goal,route))AppliedResults++;
                else {
                    RejectedResults++;
                    // Replan from current position if the actor moved while this request was in flight.
                    if(Crowd.TryGet(r.Entity,out var current)&&
                       (Math.Abs(current.Position.X-r.Start.X)>1e-4||Math.Abs(current.Position.Z-r.Start.Z)>1e-4)&&Move(r.Entity,r.Goal))continue;
                    reservations.Remove(r.Entity);retainedGoals.Remove(r.Entity);if(!Crowd.TryGet(r.Entity,out var heldRejected)||!heldRejected.Held)Crowd.Reject(r.Entity,NavigationOutcome.Rejected);
                }
            }
        }
        public void Step(double dt){ApplyResults();Crowd.Step(dt);foreach(var unit in Crowd.Units)if(!unit.Moving&&!pending.ContainsKey(unit.Id)){reservations.Remove(unit.Id);retainedGoals.Remove(unit.Id);}}
    }
}
