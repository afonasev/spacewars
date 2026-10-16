using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Explicit primitive nested work schema. External references are physical
    // IDs supplied by the containing host table, never serialized object totals.
    internal static class NavigationPrimitiveWire
    {
        internal const int Version=1;
        private static readonly System.Text.UTF8Encoding strictUtf8=new System.Text.UTF8Encoding(false,true);
        internal sealed class Writer
        {
            private readonly BinaryWriter w;
            private readonly Func<NavScheduledRequest,int> requestId;
            private readonly Func<NavTypedRoute,int> routeId;
            internal Func<NavArrivalProposal,int> proposalId;
            internal Writer(BinaryWriter w,Func<NavScheduledRequest,int> requestId,Func<NavTypedRoute,int> routeId)
            {this.w=w;this.requestId=requestId;this.routeId=routeId;}
            internal Writer(BinaryWriter w,NavigationCheckpointAliases aliases):this(w,aliases.ScheduledId,aliases.RouteId)
            {proposalId=aliases.ProposalId;requestIdForCompletion=aliases.CompletionRecipeId;}
            private void Items<T>(T[] values,Action<T> item)
            {
                w.Write(values==null?-1:values.Length);if(values==null)return;
                if(values.Length>1200000)throw new ArgumentException("Primitive array exceeds graph capacity.");
                for(int at=0;at<values.Length;at+=256){int count=Math.Min(256,values.Length-at);w.Write(count);for(int n=0;n<count;n++)item(values[at+n]);}
            }
            private void OptionalStatus(NavSolveStatus? value){w.Write(value.HasValue);if(value.HasValue)w.Write((int)value.Value);}
            private void Proposal(NavArrivalProposal value)=>w.Write(value==null?-1:proposalId(value));
            private void Region(NavTerminalRegion value){w.Write(value!=null);if(value==null)return;WorldWire.String(w,value.Id);w.Write(value.Revision);Location(value.Requested);Items(value.Candidates.ToArray(),v=>Location(v));}
            private void LongArrivalStatus(KeyValuePair<long,NavArrivalStatus> value){w.Write(value.Key);w.Write((int)value.Value);}
            private void Request(NavScheduledRequest value)=>w.Write(value==null?-1:requestId(value));
            private void Route(NavTypedRoute value)=>w.Write(value==null?-1:routeId(value));
            private void Location(NavLocation value){w.Write(value.Position.X);w.Write(value.Position.Z);WorldWire.String(w,value.SurfaceId);}
            private void Arc(NavGraphArc value){w.Write(value.From);w.Write(value.To);w.Write(value.Cost);w.Write((int)value.Kind);WorldWire.String(w,value.PortalId);Items(value.Trace?.ToArray(),v=>Leg(v));}
            private void Leg(NavTypedLeg value){w.Write(value!=null);if(value==null)return;Location(value.From);Location(value.To);w.Write(value.Cost);w.Write((int)value.Kind);WorldWire.String(w,value.PortalId);}
            private void Connector(NavConnectorResult value){w.Write(value!=null);if(value==null)return;w.Write((int)value.Status);WorldWire.String(w,value.ProviderBinding);WorldWire.String(w,value.PortalId);Location(value.Origin);Location(value.Entry);Location(value.Exit);Location(value.Endpoint);w.Write(value.Cost);}
            private void Queue(NavQueueEntry value){w.Write(value.Cost);w.Write(value.Node);w.Write(value.Candidate);}
            private void Completion(NavScheduledCompletion value){w.Write(value!=null);if(value==null)return;w.Write(requestIdForCompletion(value));w.Write((int)value.Status);Route(value.Route);w.Write(value.SubmittedEpoch);w.Write(value.CompletedEpoch);}
            // Ready completions retain their recipe even after subscriber release.
            internal Func<NavScheduledCompletion,int> requestIdForCompletion;
            private void GridPair(KeyValuePair<Tuple<string,long,long>,int> value){WorldWire.String(w,value.Key.Item1);w.Write(value.Key.Item2);w.Write(value.Key.Item3);w.Write(value.Value);}
            private void PairInts(Tuple<int,int> value){w.Write(value.Item1);w.Write(value.Item2);}
            private void LongDouble(KeyValuePair<long,double> value){w.Write(value.Key);w.Write(value.Value);}
            private void LongArc(KeyValuePair<long,NavGraphArc> value){w.Write(value.Key);Arc(value.Value);}
            private void IntDouble(KeyValuePair<int,double> value){w.Write(value.Key);w.Write(value.Value);}
            private void IntArc(KeyValuePair<int,NavGraphArc> value){w.Write(value.Key);Arc(value.Value);}
            private void StringLong(KeyValuePair<string,long> value){WorldWire.String(w,value.Key);w.Write(value.Value);}

            internal void Compile(LayeredNavigationCompileWork.Checkpoint s)
            {
                w.Write(s!=null);if(s==null)return;
                w.Write(s.Version);
                w.Write(s.Surface);
                w.Write(s.Portal);
                w.Write(s.GridCursor);
                w.Write(s.Neighbor);
                w.Write(s.End);
                w.Write(s.PortalGridCursor);
                w.Write(s.PortalArcCursor);
                w.Write(s.NodeCursor);
                w.Write(s.ArcCursor);
                w.Write(s.WrapCursor);
                w.Write(s.GridCount);
                w.Write(s.Consumed);
                WorldWire.String(w,s.GraphBinding);
                w.Write((int)s.Phase);
                w.Write(s.X);
                w.Write(s.Z);
                w.Write(s.MinX);
                w.Write(s.MaxX);
                w.Write(s.MinZ);
                w.Write(s.MaxZ);
                w.Write(s.Sampled);
                Items(s.Samples,v0=>Location(v0));
                Items(s.Parent,v0=>w.Write(v0));
                Items(s.PortalEnds,v0=>PairInts(v0));
                Items(s.Qualifications,v0=>Connector(v0));
                Items(s.PendingArcs,v0=>Arc(v0));
                Items(s.MaterializedArcs,v0=>Arc(v0));
                Partition(s.Partition);
                ArcShape(s.PendingArcShape);
                Items(s.GridEntries,v0=>GridPair(v0));
            }
            internal void Partition(PartitionNavigationCompileWork.Checkpoint s)
            {
                w.Write(s!=null);if(s==null)return;
                WorldWire.String(w,s.Binding);
                w.Write((int)s.Phase);
                w.Write(s.Version);
                w.Write(s.Consumed);
                w.Write(s.Min);
                w.Write(s.Max);
                w.Write(s.Width);
                w.Write(s.X);
                w.Write(s.Z);
                w.Write(s.Node);
                w.Write(s.Direction);
                w.Write(s.ComponentCursor);
                w.Write(s.LocationsDirectoryCapacity);
                w.Write(s.ComponentsDirectoryCapacity);
                w.Write(s.ArcSlotsDirectoryCapacity);
                w.Write(s.TracesDirectoryCapacity);
                w.Write(s.GridDirectoryCapacity);
                w.Write(s.NeighborsDirectoryCapacity);
                w.Write(s.IncomingDirectoryCapacity);
                w.Write(s.TraceSlotsDirectoryCapacity);
                w.Write(s.Sampled);
                w.Write(s.TraceBytes);
                Items(s.Locations,v0=>Location(v0));
                Items(s.Components,v0=>w.Write(v0));
                Items(s.ArcSlots,v0=>w.Write(v0));
                Items(s.Grid,v0=>Items(v0,v1=>w.Write(v1)));
                Items(s.Neighbors,v0=>Items(v0,v1=>w.Write(v1)));
                Items(s.Incoming,v0=>Items(v0,v1=>w.Write(v1)));
                Items(s.TraceSlots,v0=>Items(v0,v1=>w.Write(v1)));
                Items(s.Traces,v0=>Items(v0,v1=>Leg(v1)));
            }
            internal void Field(NavSharedTerminalField.Checkpoint s)
            {
                w.Write(s!=null);if(s==null)return;
                w.Write(s.Version);
                w.Write(s.Phase);
                w.Write(s.Candidate);
                w.Write(s.Node);
                w.Write(s.Current);
                w.Write(s.ArcCursor);
                w.Write(s.CurrentCandidate);
                w.Write(s.PackedLabels);
                w.Write(s.FrontierHeapCapacity);
                WorldWire.String(w,s.StableKey);
                w.Write((int)s.Status);
                w.Write(s.PackedStorage);
                Items(s.Distance,v0=>LongDouble(v0));
                Items(s.Next,v0=>LongArc(v0));
                Items(s.Frontier,v0=>Queue(v0));
                Items(s.FrontierHeap,v0=>Queue(v0));
                Items(s.PackedPages,v0=>PackedPage(v0));
                Items(s.FrontierPositionPages,v0=>w.Write(v0));
                QueueShape(s.FrontierShape);
            }
            internal void PackedPage(NavPackedFieldStorage.SavedPage s)
            {
                w.Write(s!=null);if(s==null)return;
                w.Write(s.Index);
                Items(s.Distance,v0=>w.Write(v0));
                Items(s.Next,v0=>Arc(v0));
                Items(s.Flags,v0=>w.Write(v0));
            }
            internal void Shared(NavIncrementalSharedRoute.Checkpoint s)
            {
                w.Write(s!=null);if(s==null)return;
                w.Write(s.Version);
                w.Write(s.Phase);
                w.Write(s.Cursor);
                w.Write(s.Candidate);
                w.Write(s.ChosenNode);
                w.Write(s.ChosenCandidate);
                w.Write(s.WalkNode);
                w.Write(s.Traversed);
                w.Write(s.ArcLeg);
                WorldWire.String(w,s.FieldKey);
                WorldWire.String(w,s.SubscriptionKey);
                w.Write((int)s.Status);
                w.Write(s.Best);
                w.Write(s.RouteCost);
                Items(s.Egress,v0=>IntDouble(v0));
                Items(s.Ingress,v0=>IntDouble(v0));
                Items(s.Legs,v0=>Leg(v0));
            }
            internal void Singleton(NavIncrementalSingletonRoute.Checkpoint s)
            {
                w.Write(s!=null);if(s==null)return;
                w.Write(s.Version);
                w.Write(s.Phase);
                w.Write(s.Cursor);
                w.Write(s.ArcCursor);
                w.Write(s.Current);
                w.Write(s.Chosen);
                w.Write(s.ReconstructCursor);
                w.Write(s.ReconstructLeg);
                w.Write(s.TraceNode);
                WorldWire.String(w,s.GraphKey);
                WorldWire.String(w,s.SubscriptionKey);
                w.Write((int)s.Status);
                w.Write(s.Best);
                w.Write(s.RouteCost);
                w.Write(s.StartSeeded);
                Items(s.Starts,v0=>IntDouble(v0));
                Items(s.Ends,v0=>IntDouble(v0));
                Items(s.Distance,v0=>IntDouble(v0));
                Items(s.Previous,v0=>IntArc(v0));
                Items(s.Frontier,v0=>Queue(v0));
                Items(s.Legs,v0=>Leg(v0));
                Items(s.Chain,v0=>Arc(v0));
                QueueShape(s.FrontierShape);
            }
            internal void Scheduler(LayeredNavigationScheduler.Checkpoint s)
            {
                w.Write(s!=null);if(s==null)return;
                w.Write(s.Version);
                w.Write(s.GraphQuota);
                w.Write(s.FieldQuota);
                w.Write(s.ConnectorQuota);
                w.Write(s.BookkeepingQuota);
                w.Write(s.MaintenanceQuota);
                w.Write(s.PeakQueue);
                w.Write(s.PeakLabels);
                w.Write(s.PeakFrontier);
                w.Write(s.OwnerGraphWork);
                w.Write(s.OwnerFieldWork);
                w.Write(s.CacheGraphWork);
                w.Write(s.LastEpoch);
                w.Write(s.LogicalGraph);
                w.Write(s.PhysicalGraph);
                w.Write(s.LogicalField);
                w.Write(s.PhysicalField);
                w.Write(s.ConnectorWork);
                w.Write(s.CacheHits);
                w.Write(s.GraphBuilds);
                w.Write(s.FieldBuilds);
                w.Write(s.Cancelled);
                w.Write(s.Evictions);
                w.Write(s.MaintenanceWork);
                w.Write(s.QueuedBytes);
                w.Write(s.ReadyBytes);
                w.Write(s.ProviderBytes);
                w.Write(s.PeakRetainedBytes);
                w.Write(s.PeakQueueAge);
                w.Write(s.PeakCacheBytes);
                w.Write(s.ExternalReservationBytes);
                WorldWire.String(w,s.AttemptKey);
                w.Write(s.SessionProviders);
                w.Write(s.RetentionBlocked);
                Items(s.SubmittedEpoch,v0=>StringLong(v0));
                Request(s.ArtifactOwner);
                Request(s.CacheGraphRecipe);
                Compile(s.OwnerGraph);
                Compile(s.CacheGraph);
                Field(s.OwnerField);
                Items(s.CacheFields,v0=>CacheField(v0));
                Items(s.Jobs,v0=>Job(v0));
                Items(s.Ready,v0=>Completion(v0));
            }
            internal void CacheField(LayeredNavigationScheduler.CacheFieldState s)
            {
                w.Write(s!=null);if(s==null)return;
                Request(s.Recipe);
                Field(s.Field);
                w.Write(s.Work);
            }
            internal void Job(LayeredNavigationScheduler.JobState s)
            {
                w.Write(s!=null);if(s==null)return;
                WorldWire.String(w,s.ArtifactKey);
                WorldWire.String(w,s.ActiveConnectorKey);
                WorldWire.String(w,s.OriginBinding);
                WorldWire.String(w,s.DirectSubscription);
                Items(s.SubscriptionKeys,v0=>WorldWire.String(w,v0));
                w.Write(s.Phase);
                w.Write(s.GraphWork);
                w.Write(s.FieldWork);
                w.Write(s.OriginCursor);
                OptionalStatus(s.Failure);
                w.Write(s.GraphDebt);
                w.Write(s.FieldDebt);
                Compile(s.Compiler);
                Compile(s.Graph);
                Field(s.Field);
                Shared(s.Connector);
                Singleton(s.Singleton);
                Route(s.DirectRoute);
                Request(s.GraphProviderOwner);
                w.Write(s.OwnedGraph);
                w.Write(s.OwnedField);
                w.Write(s.OriginValidated);
                w.Write(s.OriginComplete);
                w.Write(s.HasIngress);
            }
            internal void Arrival(NavigationArrivalWork.Checkpoint s)
            {
                w.Write(s!=null);if(s==null)return;
                w.Write(s.Version);
                w.Write(s.AllocationRemaining);
                w.Write(s.MemberIndex);
                w.Write(s.PageIndex);
                w.Write(s.CandidateCursor);
                w.Write(s.OccupantCursor);
                w.Write(s.PreparationCursor);
                w.Write(s.PreparationPageIndex);
                w.Write(s.MemberRouteCount);
                w.Write(s.Phase);
                WorldWire.String(w,s.InputKey);
                WorldWire.String(w,s.PreparingClearanceKey);
                w.Write(s.SessionProviders);
                w.Write(s.CandidateValidated);
                w.Write(s.CandidateBlocked);
                w.Write(s.MemberSawBlocked);
                w.Write(s.MemberAllUnreachable);
                w.Write(s.AuthorityBase);
                w.Write(s.LastEpoch);
                w.Write(s.AllocationWorkConsumed);
                w.Write(s.CandidateWorkConsumed);
                w.Write(s.OccupancyWorkConsumed);
                w.Write(s.DirectWorkConsumed);
                w.Write((int)s.MemberFailure);
                w.Write((int)s.Status);
                Items(s.PreparationValidCandidateIndices,v0=>w.Write(v0));
                Items(s.PageStarts,v0=>w.Write(v0));
                Items(s.PageCounts,v0=>w.Write(v0));
                Items(s.RequestCandidates,v0=>w.Write(v0));
                Items(s.PreparationCandidateIndices,v0=>Items(v0,v1=>w.Write(v1)));
                Items(s.PreparationRegions,v0=>Region(v0));
                Items(s.PreparedClearances,v0=>PreparedClearance(v0));
                Items(s.Requests,v0=>Request(v0));
                Items(s.Routes,v0=>ArrivalRoute(v0));
                Items(s.Assignments,v0=>Proposal(v0));
                Items(s.MemberOutcomes,v0=>LongArrivalStatus(v0));
                Scheduler(s.Scheduler);
            }
            internal void PreparedClearance(NavigationArrivalWork.PreparedCheckpoint s)
            {
                w.Write(s!=null);if(s==null)return;
                WorldWire.String(w,s.ClearanceKey);
                Items(s.CandidateIndices,v0=>Items(v0,v1=>w.Write(v1)));
                Items(s.Regions,v0=>Region(v0));
            }
            internal void ArrivalRoute(NavigationArrivalWork.RouteCheckpoint s)
            {
                w.Write(s!=null);if(s==null)return;
                w.Write(s.Candidate);
                w.Write((int)s.Status);
                Route(s.Route);
            }
            internal void ArcShape(NavTreeShape<NavGraphArc> s)
            {
                w.Write(s!=null);if(s==null)return;
                Items(s.Keys,v0=>Arc(v0));
                Items(s.Left,v0=>w.Write(v0));
                Items(s.Right,v0=>w.Write(v0));
                w.Write(s.Root);
            }
            internal void QueueShape(NavTreeShape<NavQueueEntry> s)
            {
                w.Write(s!=null);if(s==null)return;
                Items(s.Keys,v0=>Queue(v0));
                Items(s.Left,v0=>w.Write(v0));
                Items(s.Right,v0=>w.Write(v0));
                w.Write(s.Root);
            }
        }
        internal sealed class Reader
        {
            private readonly BinaryReader r;
            private readonly Func<int,NavScheduledRequest> request;
            private readonly Func<int,NavTypedRoute> route;
            internal Func<int,NavArrivalProposal> proposal;
            private long remaining;
            private readonly NavigationRetentionBudget.Scope allocationScope;
            internal Reader(BinaryReader r,Func<int,NavScheduledRequest> request,Func<int,NavTypedRoute> route,long stagedBytes)
            {if(stagedBytes<0||stagedBytes>LayeredNavigationScheduler.MaxRetainedBytes)throw new ArgumentException("Primitive staged retention exceeds cap.");this.r=r;this.request=request;this.route=route;remaining=LayeredNavigationScheduler.MaxRetainedBytes-stagedBytes;}
            internal Reader(BinaryReader r,Func<int,NavScheduledRequest> request,Func<int,NavTypedRoute> route,NavigationRetentionBudget.Scope allocationScope)
            {this.r=r;this.request=request;this.route=route;this.allocationScope=allocationScope??throw new ArgumentNullException(nameof(allocationScope));}
            internal Reader(BinaryReader r,NavigationCheckpointAliases aliases,NavigationRetentionBudget.Scope allocationScope):this(r,aliases.Scheduled.Resolve,aliases.Routes.Resolve,allocationScope)
            {proposal=aliases.Proposals.Resolve;}
            private void Charge(long bytes){if(allocationScope!=null){if(!allocationScope.TryReserve(bytes))throw new ArgumentException("Primitive decoded/reconstruction retention exceeds cap.");}
                else{if(bytes<0||bytes>remaining)throw new ArgumentException("Primitive decoded/staged retention exceeds cap.");remaining-=bytes;}}
            private T[] Items<T>(Func<T> item,int elementBytes)
            {
                int count=r.ReadInt32();if(count==-1)return null;
                if(count<0||count>1200000||count>r.BaseStream.Length-r.BaseStream.Position)throw new ArgumentException("Invalid primitive array count.");
                Charge(32L+(long)count*elementBytes);var values=new T[count];
                for(int at=0;at<count;at+=256){int page=r.ReadInt32();if(page!=Math.Min(256,count-at))throw new ArgumentException("Invalid primitive page length.");for(int n=0;n<page;n++)values[at+n]=item();}
                return values;
            }
            private string ReadString(){int count=r.ReadInt32();if(count==-1)return null;
                if(count<0||count>WorldWire.MaxBytes||count>r.BaseStream.Length-r.BaseStream.Position)throw new ArgumentException("Invalid primitive string length.");
                // Both the UTF8 staging array and maximum UTF16 result remain
                // charged until the owning decode/reconstruction scope resolves.
                Charge(64L+3L*count);return strictUtf8.GetString(r.ReadBytes(count));}
            private double ReadDouble(){double value=r.ReadDouble();if(double.IsNaN(value))throw new ArgumentException("Invalid primitive number.");return value;}
            private NavSolveStatus? OptionalStatus()=>WorldWire.Boolean(r)?(NavSolveStatus?)WorldWire.EnumValue<NavSolveStatus>(r):null;
            private NavArrivalProposal Proposal(){int id=r.ReadInt32();if(id<-1)throw new ArgumentException("Invalid primitive proposal alias.");return id==-1?null:proposal(id)??throw new ArgumentException("Missing primitive proposal alias.");}
            private NavTerminalRegion Region(){if(!WorldWire.Boolean(r))return null;Charge(128);string id=ReadString();int revision=r.ReadInt32();var requested=Location();return new NavTerminalRegion(id,revision,requested,Items(()=>Location(),64));}
            private KeyValuePair<long,NavArrivalStatus> LongArrivalStatus()=>new KeyValuePair<long,NavArrivalStatus>(r.ReadInt64(),WorldWire.EnumValue<NavArrivalStatus>(r));
            private NavScheduledRequest Request(){int id=r.ReadInt32();if(id<-1)throw new ArgumentException("Invalid primitive request alias.");return id==-1?null:request(id)??throw new ArgumentException("Missing primitive request alias.");}
            private NavTypedRoute Route(){int id=r.ReadInt32();if(id<-1)throw new ArgumentException("Invalid primitive route alias.");return id==-1?null:route(id)??throw new ArgumentException("Missing primitive route alias.");}
            private NavLocation Location(){var point=new NavPoint(WorldWire.Number(r),WorldWire.Number(r));string surface=ReadString();if(surface==null){if(!point.Equals(default(NavPoint)))throw new ArgumentException("Invalid default primitive location.");return default;}return new NavLocation(point,surface);}
            private NavGraphArc Arc(){int from=r.ReadInt32(),to=r.ReadInt32();double cost=WorldWire.Number(r);var kind=WorldWire.EnumValue<NavGraphArcKind>(r);string portal=ReadString();return new NavGraphArc(from,to,cost,kind,portal,Items(()=>Leg(),32));}
            private NavTypedLeg Leg(){if(!WorldWire.Boolean(r))return null;Charge(128);return new NavTypedLeg(Location(),Location(),WorldWire.Number(r),WorldWire.EnumValue<NavTypedLegKind>(r),ReadString());}
            private NavConnectorResult Connector(){if(!WorldWire.Boolean(r))return null;Charge(NavConnectorResult.StorageCharge);var status=WorldWire.EnumValue<NavConnectorStatus>(r);string binding=ReadString(),portal=ReadString();return new NavConnectorResult(status,binding,portal,Location(),Location(),Location(),Location(),WorldWire.Number(r));}
            private NavQueueEntry Queue()=>new NavQueueEntry{Cost=WorldWire.Number(r),Node=r.ReadInt32(),Candidate=r.ReadInt32()};
            private NavScheduledCompletion Completion(){if(!WorldWire.Boolean(r))return null;Charge(128);var recipe=Request();var status=WorldWire.EnumValue<NavSolveStatus>(r);return new NavScheduledCompletion(recipe,status,Route(),r.ReadInt64(),r.ReadInt64());}
            private KeyValuePair<Tuple<string,long,long>,int> GridPair()=>new KeyValuePair<Tuple<string,long,long>,int>(Tuple.Create(ReadString(),r.ReadInt64(),r.ReadInt64()),r.ReadInt32());
            private Tuple<int,int> PairInts()=>Tuple.Create(r.ReadInt32(),r.ReadInt32());
            private KeyValuePair<long,double> LongDouble()=>new KeyValuePair<long,double>(r.ReadInt64(),ReadDouble());
            private KeyValuePair<long,NavGraphArc> LongArc()=>new KeyValuePair<long,NavGraphArc>(r.ReadInt64(),Arc());
            private KeyValuePair<int,double> IntDouble()=>new KeyValuePair<int,double>(r.ReadInt32(),ReadDouble());
            private KeyValuePair<int,NavGraphArc> IntArc()=>new KeyValuePair<int,NavGraphArc>(r.ReadInt32(),Arc());
            private KeyValuePair<string,long> StringLong()=>new KeyValuePair<string,long>(ReadString(),r.ReadInt64());

            internal LayeredNavigationCompileWork.Checkpoint Compile()
            {
                if(!WorldWire.Boolean(r))return null;Charge(256);
                return new LayeredNavigationCompileWork.Checkpoint {
                    Version=r.ReadInt32(),
                    Surface=r.ReadInt32(),
                    Portal=r.ReadInt32(),
                    GridCursor=r.ReadInt32(),
                    Neighbor=r.ReadInt32(),
                    End=r.ReadInt32(),
                    PortalGridCursor=r.ReadInt32(),
                    PortalArcCursor=r.ReadInt32(),
                    NodeCursor=r.ReadInt32(),
                    ArcCursor=r.ReadInt32(),
                    WrapCursor=r.ReadInt32(),
                    GridCount=r.ReadInt32(),
                    Consumed=r.ReadInt32(),
                    GraphBinding=ReadString(),
                    Phase=WorldWire.EnumValue<LayeredNavigationCompileWork.Stage>(r),
                    X=r.ReadInt64(),
                    Z=r.ReadInt64(),
                    MinX=r.ReadInt64(),
                    MaxX=r.ReadInt64(),
                    MinZ=r.ReadInt64(),
                    MaxZ=r.ReadInt64(),
                    Sampled=r.ReadInt64(),
                    Samples=Items(()=>Location(),64),
                    Parent=Items(()=>r.ReadInt32(),4),
                    PortalEnds=Items(()=>PairInts(),32),
                    Qualifications=Items(()=>Connector(),32),
                    PendingArcs=Items(()=>Arc(),128),
                    MaterializedArcs=Items(()=>Arc(),128),
                    Partition=Partition(),
                    PendingArcShape=ArcShape(),
                    GridEntries=Items(()=>GridPair(),32),
                };
            }
            internal PartitionNavigationCompileWork.Checkpoint Partition()
            {
                if(!WorldWire.Boolean(r))return null;Charge(256);
                return new PartitionNavigationCompileWork.Checkpoint {
                    Binding=ReadString(),
                    Phase=WorldWire.EnumValue<PartitionNavigationCompileWork.Stage>(r),
                    Version=r.ReadInt32(),
                    Consumed=r.ReadInt32(),
                    Min=r.ReadInt32(),
                    Max=r.ReadInt32(),
                    Width=r.ReadInt32(),
                    X=r.ReadInt32(),
                    Z=r.ReadInt32(),
                    Node=r.ReadInt32(),
                    Direction=r.ReadInt32(),
                    ComponentCursor=r.ReadInt32(),
                    LocationsDirectoryCapacity=r.ReadInt32(),
                    ComponentsDirectoryCapacity=r.ReadInt32(),
                    ArcSlotsDirectoryCapacity=r.ReadInt32(),
                    TracesDirectoryCapacity=r.ReadInt32(),
                    GridDirectoryCapacity=r.ReadInt32(),
                    NeighborsDirectoryCapacity=r.ReadInt32(),
                    IncomingDirectoryCapacity=r.ReadInt32(),
                    TraceSlotsDirectoryCapacity=r.ReadInt32(),
                    Sampled=r.ReadInt64(),
                    TraceBytes=r.ReadInt64(),
                    Locations=Items(()=>Location(),64),
                    Components=Items(()=>r.ReadInt32(),4),
                    ArcSlots=Items(()=>r.ReadInt32(),4),
                    Grid=Items(()=>Items(()=>r.ReadInt32(),4),32),
                    Neighbors=Items(()=>Items(()=>r.ReadInt32(),4),32),
                    Incoming=Items(()=>Items(()=>r.ReadInt32(),4),32),
                    TraceSlots=Items(()=>Items(()=>r.ReadInt32(),4),32),
                    Traces=Items(()=>Items(()=>Leg(),32),32),
                };
            }
            internal NavSharedTerminalField.Checkpoint Field()
            {
                if(!WorldWire.Boolean(r))return null;Charge(256);
                return new NavSharedTerminalField.Checkpoint {
                    Version=r.ReadInt32(),
                    Phase=r.ReadInt32(),
                    Candidate=r.ReadInt32(),
                    Node=r.ReadInt32(),
                    Current=r.ReadInt32(),
                    ArcCursor=r.ReadInt32(),
                    CurrentCandidate=r.ReadInt32(),
                    PackedLabels=r.ReadInt32(),
                    FrontierHeapCapacity=r.ReadInt32(),
                    StableKey=ReadString(),
                    Status=WorldWire.EnumValue<NavSolveStatus>(r),
                    PackedStorage=WorldWire.Boolean(r),
                    Distance=Items(()=>LongDouble(),32),
                    Next=Items(()=>LongArc(),256),
                    Frontier=Items(()=>Queue(),16),
                    FrontierHeap=Items(()=>Queue(),16),
                    PackedPages=Items(()=>PackedPage(),128),
                    FrontierPositionPages=Items(()=>r.ReadInt32(),4),
                    FrontierShape=QueueShape(),
                };
            }
            internal NavPackedFieldStorage.SavedPage PackedPage()
            {
                if(!WorldWire.Boolean(r))return null;Charge(256);
                return new NavPackedFieldStorage.SavedPage {
                    Index=r.ReadInt32(),
                    Distance=Items(()=>ReadDouble(),8),
                    Next=Items(()=>Arc(),128),
                    Flags=Items(()=>r.ReadByte(),1),
                };
            }
            internal NavIncrementalSharedRoute.Checkpoint Shared()
            {
                if(!WorldWire.Boolean(r))return null;Charge(256);
                return new NavIncrementalSharedRoute.Checkpoint {
                    Version=r.ReadInt32(),
                    Phase=r.ReadInt32(),
                    Cursor=r.ReadInt32(),
                    Candidate=r.ReadInt32(),
                    ChosenNode=r.ReadInt32(),
                    ChosenCandidate=r.ReadInt32(),
                    WalkNode=r.ReadInt32(),
                    Traversed=r.ReadInt32(),
                    ArcLeg=r.ReadInt32(),
                    FieldKey=ReadString(),
                    SubscriptionKey=ReadString(),
                    Status=WorldWire.EnumValue<NavSolveStatus>(r),
                    Best=ReadDouble(),
                    RouteCost=ReadDouble(),
                    Egress=Items(()=>IntDouble(),32),
                    Ingress=Items(()=>IntDouble(),32),
                    Legs=Items(()=>Leg(),32),
                };
            }
            internal NavIncrementalSingletonRoute.Checkpoint Singleton()
            {
                if(!WorldWire.Boolean(r))return null;Charge(256);
                return new NavIncrementalSingletonRoute.Checkpoint {
                    Version=r.ReadInt32(),
                    Phase=r.ReadInt32(),
                    Cursor=r.ReadInt32(),
                    ArcCursor=r.ReadInt32(),
                    Current=r.ReadInt32(),
                    Chosen=r.ReadInt32(),
                    ReconstructCursor=r.ReadInt32(),
                    ReconstructLeg=r.ReadInt32(),
                    TraceNode=r.ReadInt32(),
                    GraphKey=ReadString(),
                    SubscriptionKey=ReadString(),
                    Status=WorldWire.EnumValue<NavSolveStatus>(r),
                    Best=ReadDouble(),
                    RouteCost=ReadDouble(),
                    StartSeeded=WorldWire.Boolean(r),
                    Starts=Items(()=>IntDouble(),32),
                    Ends=Items(()=>IntDouble(),32),
                    Distance=Items(()=>IntDouble(),32),
                    Previous=Items(()=>IntArc(),256),
                    Frontier=Items(()=>Queue(),16),
                    Legs=Items(()=>Leg(),32),
                    Chain=Items(()=>Arc(),128),
                    FrontierShape=QueueShape(),
                };
            }
            internal LayeredNavigationScheduler.Checkpoint Scheduler()
            {
                if(!WorldWire.Boolean(r))return null;Charge(256);
                return new LayeredNavigationScheduler.Checkpoint {
                    Version=r.ReadInt32(),
                    GraphQuota=r.ReadInt32(),
                    FieldQuota=r.ReadInt32(),
                    ConnectorQuota=r.ReadInt32(),
                    BookkeepingQuota=r.ReadInt32(),
                    MaintenanceQuota=r.ReadInt32(),
                    PeakQueue=r.ReadInt32(),
                    PeakLabels=r.ReadInt32(),
                    PeakFrontier=r.ReadInt32(),
                    OwnerGraphWork=r.ReadInt32(),
                    OwnerFieldWork=r.ReadInt32(),
                    CacheGraphWork=r.ReadInt32(),
                    LastEpoch=r.ReadInt64(),
                    LogicalGraph=r.ReadInt64(),
                    PhysicalGraph=r.ReadInt64(),
                    LogicalField=r.ReadInt64(),
                    PhysicalField=r.ReadInt64(),
                    ConnectorWork=r.ReadInt64(),
                    CacheHits=r.ReadInt64(),
                    GraphBuilds=r.ReadInt64(),
                    FieldBuilds=r.ReadInt64(),
                    Cancelled=r.ReadInt64(),
                    Evictions=r.ReadInt64(),
                    MaintenanceWork=r.ReadInt64(),
                    QueuedBytes=r.ReadInt64(),
                    ReadyBytes=r.ReadInt64(),
                    ProviderBytes=r.ReadInt64(),
                    PeakRetainedBytes=r.ReadInt64(),
                    PeakQueueAge=r.ReadInt64(),
                    PeakCacheBytes=r.ReadInt64(),
                    ExternalReservationBytes=r.ReadInt64(),
                    AttemptKey=ReadString(),
                    SessionProviders=WorldWire.Boolean(r),
                    RetentionBlocked=WorldWire.Boolean(r),
                    SubmittedEpoch=Items(()=>StringLong(),32),
                    ArtifactOwner=Request(),
                    CacheGraphRecipe=Request(),
                    OwnerGraph=Compile(),
                    CacheGraph=Compile(),
                    OwnerField=Field(),
                    CacheFields=Items(()=>CacheField(),128),
                    Jobs=Items(()=>Job(),128),
                    Ready=Items(()=>Completion(),32),
                };
            }
            internal LayeredNavigationScheduler.CacheFieldState CacheField()
            {
                if(!WorldWire.Boolean(r))return null;Charge(256);
                return new LayeredNavigationScheduler.CacheFieldState {
                    Recipe=Request(),
                    Field=Field(),
                    Work=r.ReadInt32(),
                };
            }
            internal LayeredNavigationScheduler.JobState Job()
            {
                if(!WorldWire.Boolean(r))return null;Charge(256);
                return new LayeredNavigationScheduler.JobState {
                    ArtifactKey=ReadString(),
                    ActiveConnectorKey=ReadString(),
                    OriginBinding=ReadString(),
                    DirectSubscription=ReadString(),
                    SubscriptionKeys=Items(()=>ReadString(),32),
                    Phase=r.ReadInt32(),
                    GraphWork=r.ReadInt32(),
                    FieldWork=r.ReadInt32(),
                    OriginCursor=r.ReadInt32(),
                    Failure=OptionalStatus(),
                    GraphDebt=r.ReadInt64(),
                    FieldDebt=r.ReadInt64(),
                    Compiler=Compile(),
                    Graph=Compile(),
                    Field=Field(),
                    Connector=Shared(),
                    Singleton=Singleton(),
                    DirectRoute=Route(),
                    GraphProviderOwner=Request(),
                    OwnedGraph=WorldWire.Boolean(r),
                    OwnedField=WorldWire.Boolean(r),
                    OriginValidated=WorldWire.Boolean(r),
                    OriginComplete=WorldWire.Boolean(r),
                    HasIngress=WorldWire.Boolean(r),
                };
            }
            internal NavigationArrivalWork.Checkpoint Arrival()
            {
                if(!WorldWire.Boolean(r))return null;Charge(256);
                return new NavigationArrivalWork.Checkpoint {
                    Version=r.ReadInt32(),
                    AllocationRemaining=r.ReadInt32(),
                    MemberIndex=r.ReadInt32(),
                    PageIndex=r.ReadInt32(),
                    CandidateCursor=r.ReadInt32(),
                    OccupantCursor=r.ReadInt32(),
                    PreparationCursor=r.ReadInt32(),
                    PreparationPageIndex=r.ReadInt32(),
                    MemberRouteCount=r.ReadInt32(),
                    Phase=r.ReadInt32(),
                    InputKey=ReadString(),
                    PreparingClearanceKey=ReadString(),
                    SessionProviders=WorldWire.Boolean(r),
                    CandidateValidated=WorldWire.Boolean(r),
                    CandidateBlocked=WorldWire.Boolean(r),
                    MemberSawBlocked=WorldWire.Boolean(r),
                    MemberAllUnreachable=WorldWire.Boolean(r),
                    AuthorityBase=r.ReadInt64(),
                    LastEpoch=r.ReadInt64(),
                    AllocationWorkConsumed=r.ReadInt64(),
                    CandidateWorkConsumed=r.ReadInt64(),
                    OccupancyWorkConsumed=r.ReadInt64(),
                    DirectWorkConsumed=r.ReadInt64(),
                    MemberFailure=WorldWire.EnumValue<NavArrivalStatus>(r),
                    Status=WorldWire.EnumValue<NavArrivalStatus>(r),
                    PreparationValidCandidateIndices=Items(()=>r.ReadInt32(),4),
                    PageStarts=Items(()=>r.ReadInt32(),4),
                    PageCounts=Items(()=>r.ReadInt32(),4),
                    RequestCandidates=Items(()=>r.ReadInt32(),4),
                    PreparationCandidateIndices=Items(()=>Items(()=>r.ReadInt32(),4),32),
                    PreparationRegions=Items(()=>Region(),32),
                    PreparedClearances=Items(()=>PreparedClearance(),128),
                    Requests=Items(()=>Request(),32),
                    Routes=Items(()=>ArrivalRoute(),128),
                    Assignments=Items(()=>Proposal(),32),
                    MemberOutcomes=Items(()=>LongArrivalStatus(),32),
                    Scheduler=Scheduler(),
                };
            }
            internal NavigationArrivalWork.PreparedCheckpoint PreparedClearance()
            {
                if(!WorldWire.Boolean(r))return null;Charge(256);
                return new NavigationArrivalWork.PreparedCheckpoint {
                    ClearanceKey=ReadString(),
                    CandidateIndices=Items(()=>Items(()=>r.ReadInt32(),4),32),
                    Regions=Items(()=>Region(),32),
                };
            }
            internal NavigationArrivalWork.RouteCheckpoint ArrivalRoute()
            {
                if(!WorldWire.Boolean(r))return null;Charge(256);
                return new NavigationArrivalWork.RouteCheckpoint {
                    Candidate=r.ReadInt32(),
                    Status=WorldWire.EnumValue<NavSolveStatus>(r),
                    Route=Route(),
                };
            }
            internal NavTreeShape<NavGraphArc> ArcShape()
            {
                if(!WorldWire.Boolean(r))return null;Charge(256);
                return new NavTreeShape<NavGraphArc> {
                    Keys=Items(()=>Arc(),128),
                    Left=Items(()=>r.ReadInt32(),4),
                    Right=Items(()=>r.ReadInt32(),4),
                    Root=r.ReadInt32(),
                };
            }
            internal NavTreeShape<NavQueueEntry> QueueShape()
            {
                if(!WorldWire.Boolean(r))return null;Charge(256);
                return new NavTreeShape<NavQueueEntry> {
                    Keys=Items(()=>Queue(),16),
                    Left=Items(()=>r.ReadInt32(),4),
                    Right=Items(()=>r.ReadInt32(),4),
                    Root=r.ReadInt32(),
                };
            }
        }
    }
}
