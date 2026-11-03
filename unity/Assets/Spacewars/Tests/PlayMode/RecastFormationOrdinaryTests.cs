using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed partial class RecastFormationOrdinaryTests
    {
        // Optional visual evidence observes LIVE published views; never replays saved poses.
        // Disabled ordinary QA keeps its original World-only path and assertions.
        sealed class LiveVideo : IDisposable
        {
            readonly HashSet<int> selectedVideo;readonly GameObject root;readonly PlayableWorld world;readonly Camera camera;readonly RenderTexture target;readonly Texture2D pixels;readonly string directory;readonly List<object> timeline=new List<object>();double next;int count;
            internal LiveVideo(PlayableProfile profile,string directory,int[] selection=null){
                selectedVideo=selection==null?null:new HashSet<int>(selection);
                this.directory=directory;Directory.CreateDirectory(directory);root=new GameObject("S2 live rendered evidence");world=new PlayableWorld(root.transform,profile);world.InspectionReveal();var lighting=IndustrialLightingProfile.Load(profile.AuthoredMap.Id);world.ApplyGraphics(ActorMaterialProfile.Load(),lighting);
                var light=new GameObject("Fixed evidence sun").AddComponent<Light>();light.transform.SetParent(root.transform);light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(48,-35,0);
                camera=new GameObject("Fixed north crossing evidence camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.orthographic=true;camera.orthographicSize=28;camera.transform.position=new Vector3(0,90,-22);camera.transform.LookAt(new Vector3(0,0,38));camera.nearClipPlane=.1f;camera.farClipPlane=300;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.045f,.075f,.10f);camera.enabled=false;
                if(selectedVideo!=null){camera.orthographicSize=45;camera.transform.position=new Vector3(0,110,-40);camera.transform.LookAt(new Vector3(0,0,28));}
                target=new RenderTexture(960,540,24){antiAliasing=1};target.Create();camera.targetTexture=target;pixels=new Texture2D(960,540,TextureFormat.RGB24,false);
                File.WriteAllText(Path.Combine(directory,"recipe.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Meaning=selectedVideo==null?"LIVE actual published runtime views rendered with native PlayableWorld/models; no saved pose playback; visual run separate from World-only functional evidence":"LIVE first PhaseB100 ordinary scenario, published allied views; own fixed camera/scale establishes future PhaseB baseline. A50 is a different recipe, no causal comparison",CameraPosition=new{camera.transform.position.x,camera.transform.position.y,camera.transform.position.z},CameraRotation=new{camera.transform.rotation.x,camera.transform.rotation.y,camera.transform.rotation.z,camera.transform.rotation.w},camera.orthographicSize,Width=960,Height=540,SamplingWallSeconds=.125,Quality=QualitySettings.GetQualityLevel(),QualitySettings.antiAliasing,Graphics=SystemInfo.graphicsDeviceType.ToString(),Source=Environment.GetEnvironmentVariable("RECAST_FORMATION_CANDIDATE"),CaptureSource=Environment.GetEnvironmentVariable("RECAST_FORMATION_CAPTURE_SOURCE"),Seed=RecastFormationInputs.Seed,Generation=RecastFormationInputs.Generation},Newtonsoft.Json.Formatting.Indented));
            }
            internal void Read(PlayableRuntime runtime,bool force=false){
                double wall=Time.realtimeSinceStartupAsDouble;if(!force&&wall<next)return;next=wall+.125;var view=runtime.Latest;
                if(selectedVideo!=null&&view.Entities.Count(e=>selectedVideo.Contains(e.Id))!=selectedVideo.Count)throw new InvalidOperationException("PhaseB LIVE publication omitted selected members");
                foreach(var e in view.Entities){if(!world.Actors.TryGetValue(e.Id,out var a))a=world.Tank(e.Id,e.Owner==PlayableOwner.Player,e.Kind);a.Root.position=world.Point(e.Position);a.Hull.localRotation=Quaternion.Euler(0,90-(float)e.HullHeading*Mathf.Rad2Deg,0);a.Turret.localRotation=Quaternion.Euler(0,(float)(e.HullHeading-e.TurretHeading)*Mathf.Rad2Deg,0);a.Selection.SetActive(selectedVideo==null?e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer:selectedVideo.Contains(e.Id));a.Health.gameObject.SetActive(false);}
                camera.Render();var prior=RenderTexture.active;try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,960,540),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(directory,"frame-"+count.ToString("D05")+".png"),pixels.EncodeToPNG());}finally{RenderTexture.active=prior;}
                timeline.Add(new{Frame=count++,Tick=view.Tick,Wall=wall,SelectedArrived=view.Entities.Count(e=>(selectedVideo==null?e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer:selectedVideo.Contains(e.Id))&&e.NavigationOutcome==NavigationOutcome.Arrived),SelectedVisible=selectedVideo==null?(int?)null:view.Entities.Count(e=>selectedVideo.Contains(e.Id))});
            }
            public void Dispose(){File.WriteAllText(Path.Combine(directory,"timeline.json"),Newtonsoft.Json.JsonConvert.SerializeObject(timeline,Newtonsoft.Json.Formatting.Indented));world.Dispose();target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(root);}
        }
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly PropertyInfo UnitStorage=typeof(NavUnit).GetProperty("StorageOwner",Hidden);
        static readonly FieldInfo InstalledOwner=UnitStorage.PropertyType.GetField("InstalledExecution",Hidden);
        static readonly Type NativeType=typeof(RecastNavigationMap).Assembly.GetType("Spacewars.Simulation.RecastNative",true);
        static ulong NativeCount()=>(ulong)NativeType.GetMethod("sw_recast_allocation_count",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        static long ReleaseFaults()=>(long)NativeType.GetProperty("ReleaseFaults",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        static PlayableDomain Domain(PlayableRuntime r)=>(PlayableDomain)typeof(PlayableRuntime).GetField("domain",Hidden).GetValue(r);
        static string Binding(PlayableProfile p){using var sha=System.Security.Cryptography.SHA256.Create();return BitConverter.ToString(sha.ComputeHash(WorldWire.Binding(p))).Replace("-","").ToLowerInvariant();}
        static string Spine(NavUnit a){var storage=UnitStorage.GetValue(a);var o=storage==null?null:InstalledOwner.GetValue(storage) as NavigationInstalledExecutionOwner;var plan=o?.Corridor?.Corridor?.RecastPlan;if(plan==null)return null;var spine=plan.GetType().GetField("Spine",Hidden).GetValue(plan);return (string)spine.GetType().GetProperty("Binding",Hidden).GetValue(spine);}
        sealed class Body {public int Id,Slot;public NavPoint Position,Goal,Assigned;public NavLocation Location;public double Radius,Speed;public bool Selected,Moving,HasSlot,AllSlots;public NavigationOutcome Outcome;public long Installed;public RecastMemberProgress Progress;public string Spine;}
        sealed class Frame {public long Tick,Group,Revision;public NavPoint OriginalGoal;public string FormationPhase;public Body[] Bodies;}
        // Detached observations only; never retain an authority storage owner.
        sealed class MovementObservation
        {
            internal const int SampleCapacity=(RecastFormationInputs.Horizon+64)*5,RouteCapacity=64,LegCapacity=131072;
            readonly List<object> routes=new List<object>(),spines=new List<object>(),samples=new List<object>();
            readonly Dictionary<long,double> joins=new Dictionary<long,double>();
            readonly HashSet<string> spineBindings=new HashSet<string>();
            internal int DroppedSamples,DroppedRoutes,DroppedLegs,LegsCopied;
            static readonly int[] focus={34,44,49,52,53};
            static object Value(object o,string name){var t=o.GetType();return t.GetField(name,Hidden|BindingFlags.Public)?.GetValue(o)??t.GetProperty(name,Hidden|BindingFlags.Public)?.GetValue(o)??throw new MissingMemberException(t.FullName,name);}
            static object Point(NavPoint p)=>new{p.X,p.Z};
            static object Location(NavLocation p)=>new{Position=Point(p.Position),Surface=p.SurfaceId};
            static object Leg(NavTypedLeg l)=>new{From=Location(l.From),To=Location(l.To),Kind=l.Kind.ToString(),l.PortalId,l.Cost};
            static NavigationInstalledExecutionOwner Owner(NavUnit a){var storage=UnitStorage.GetValue(a);return storage==null?null:InstalledOwner.GetValue(storage) as NavigationInstalledExecutionOwner;}
            internal static void ValidateFields()
            {
                foreach(string name in new[]{"routeIndex","localRoute","localIndex","blockedSeconds","noProgressTicks"})if(typeof(NavUnit).GetField(name,Hidden)==null)throw new MissingFieldException(typeof(NavUnit).FullName,name);
                var asm=typeof(NavTypedRoute).Assembly;
                foreach(var spec in new[]{new[]{"Spacewars.Simulation.NavRecastMemberPlan","Spine","Join","Exit","OriginLegs","TerminalLeg","GraphBinding","ProfileBinding"},new[]{"Spacewars.Simulation.NavRecastSpine","Legs","Binding","MapBinding","GraphBinding","ProviderBinding","ProfileBinding","KnowledgeBinding","GeometryRevision","Radius","FanCenter","FanRadius","FanNode"}}){var t=asm.GetType(spec[0],true);foreach(string name in spec.Skip(1))if(t.GetField(name,Hidden)==null&&t.GetProperty(name,Hidden)==null)throw new MissingMemberException(t.FullName,name);}
            }
            void CopyRoute(NavUnit a,NavigationInstalledExecutionOwner owner,long tick)
            {
                if(joins.ContainsKey(a.InstalledRequest))return;
                var typed=owner.Corridor.TypedRoute;var plan=owner.Corridor.Corridor.RecastPlan;if(plan==null)throw new InvalidOperationException("Installed request lacks actual Recast plan.");
                var spine=Value(plan,"Spine");string binding=(string)Value(spine,"Binding");var spineLegs=(IReadOnlyList<NavTypedLeg>)Value(spine,"Legs");bool newSpine=!spineBindings.Contains(binding);
                int legCount=typed.Legs.Count+owner.Corridor.Corridor.Legs.Count+(newSpine?spineLegs.Count:0);
                if(routes.Count>=RouteCapacity){DroppedRoutes++;return;}if(legCount>LegCapacity-LegsCopied){DroppedLegs+=legCount;return;}
                int join=(int)Value(plan,"Join"),exit=(int)Value(plan,"Exit"),origin=(int)Value(plan,"OriginLegs"),terminal=(int)Value(plan,"TerminalLeg");double joinStation=0;
                for(int i=0;i<join;i++){var l=spineLegs[i];double dx=l.To.Position.X-l.From.Position.X,dz=l.To.Position.Z-l.From.Position.Z;joinStation+=Math.Sqrt(dx*dx+dz*dz);}
                var c=owner.Corridor.Corridor;
                routes.Add(new{Tick=tick,a.Id,a.InstalledRequest,c.Group,c.RootRevision,c.ActivationRevision,c.CommandSequence,c.Incarnation,c.Mobility,c.Order,c.GraphKey,c.ProviderBinding,c.ProfileBinding,c.HoldBinding,c.StableKey,Origin=Location(c.Origin),Endpoint=Location(c.Endpoint),Spine=binding,Join=join,Exit=exit,OriginLegs=origin,TerminalLeg=terminal,JoinStation=joinStation,PlanGraphBinding=Value(plan,"GraphBinding"),PlanProfileBinding=Value(plan,"ProfileBinding"),Legs=typed.Legs.Select(Leg).ToArray(),CorridorLegs=c.Legs.Select(l=>new{From=Location(l.From),To=Location(l.To),Kind=l.Kind.ToString(),l.PortalId,l.Cost,Section=l.Section.ToString(),l.Branch,l.DownstreamMergeLeg}).ToArray(),Route=a.Route.Select(Point).ToArray()});
                if(newSpine){spines.Add(new{Binding=binding,MapBinding=Value(spine,"MapBinding"),GraphBinding=Value(spine,"GraphBinding"),ProviderBinding=Value(spine,"ProviderBinding"),ProfileBinding=Value(spine,"ProfileBinding"),KnowledgeBinding=Value(spine,"KnowledgeBinding"),GeometryRevision=Value(spine,"GeometryRevision"),Radius=Value(spine,"Radius"),FanCenter=Point((NavPoint)Value(spine,"FanCenter")),FanRadius=Value(spine,"FanRadius"),FanNode=Value(spine,"FanNode"),Legs=spineLegs.Select(Leg).ToArray()});spineBindings.Add(binding);}
                joins.Add(a.InstalledRequest,joinStation);LegsCopied+=legCount;
            }
            internal void Read(PlayableDomain domain,Frame frame,double stretch)
            {
                var n=domain.Navigation;var group=n.GroupFor(3);if(group==null)return;var actors=n.Crowd.Units.ToDictionary(a=>a.Id);var members=group.Members.ToDictionary(m=>m.Entity);var eligible=new HashSet<int>();
                foreach(var b in frame.Bodies.Where(b=>b.Selected&&b.Installed>0)){
                    var a=actors[b.Id];var owner=Owner(a);if(owner==null)throw new InvalidOperationException("Installed request has no actual owner.");CopyRoute(a,owner,frame.Tick);
                    var c=owner.Corridor.Corridor;var m=members[a.Id];
                    if(!m.FormationReleased&&a.Moving&&!a.Held&&!owner.State.Detached&&a.InstalledGroupId==c.Group&&a.InstalledRevision==c.ActivationRevision&&a.InstalledRequest==c.Request&&a.InstalledOrder==c.Order&&a.Incarnation==c.Incarnation&&a.MobilityRevision==c.Mobility&&group.OrderRevision==c.RootRevision&&m.OrderRevision==c.ActivationRevision&&owner.Route.Count()==a.Route.Count&&owner.Route.SequenceEqual(a.Route))eligible.Add(a.Id);
                }
                foreach(int id in focus){if(!actors.TryGetValue(id,out var a)||!members.TryGetValue(id,out var m)||a.InstalledRequest==0)continue;if(samples.Count>=SampleCapacity){DroppedSamples++;continue;}
                    var b=frame.Bodies.Single(x=>x.Id==id);var cohort=frame.Bodies.Where(x=>eligible.Contains(x.Id)&&x.Spine==b.Spine&&x.Progress.Phase!=RecastMovementPhase.UnmappedProduction).ToArray();double pace=frame.Bodies.Where(x=>eligible.Contains(x.Id)).Select(x=>x.Speed).DefaultIfEmpty(a.Speed).Min();var peers=cohort.Where(x=>x.Progress.Phase!=RecastMovementPhase.Terminal&&joins.ContainsKey(x.Installed)).ToArray();double? common=peers.Length==0?(double?)null:peers.Min(x=>x.Progress.Phase==RecastMovementPhase.Origin?joins[x.Installed]:x.Progress.Station);double bound=Math.Min(a.Speed,pace);double reconstructed=b.Progress.Phase==RecastMovementPhase.Shared&&common.HasValue?Math.Min(a.Speed,Math.Min(bound,Math.Max(0,(common.Value+stretch-b.Progress.Station)*30))):bound;
                    int global=(int)Value(a,"routeIndex"),local=(int)Value(a,"localIndex");var localRoute=(NavPoint[])Value(a,"localRoute");NavPoint? target=local<localRoute.Length?localRoute[local]:global<a.Route.Count?a.Route[global]:(NavPoint?)null;
                    int typedIndex=-1,surfaceIndex=0;var typed=Owner(a).Corridor.TypedRoute;if(local>=localRoute.Length&&global<a.Route.Count)for(int i=0;i<typed.Legs.Count;i++)if(typed.Legs[i].Kind==NavTypedLegKind.Surface&&surfaceIndex++==global){typedIndex=i;break;}
                    if(local>=localRoute.Length&&global<a.Route.Count&&typedIndex<0&&global==surfaceIndex)typedIndex=typed.Legs.Count-1;
                    samples.Add(new{frame.Tick,a.Id,a.Position,a.Location,a.Heading,a.Moving,a.Held,a.InstalledRequest,Spine=b.Spine,m.Slot,m.Phase,m.Branch,m.Reservation,m.ContactTick,m.ContactPhase,m.FormationReleased,m.ProgressRequest,Recast=m.Recast,GlobalIndex=global,GlobalCount=a.Route.Count,LocalIndex=local,LocalCount=localRoute.Length,Target=target,TypedTargetLeg=typedIndex,BlockedSeconds=Value(a,"blockedSeconds"),NoProgressTicks=Value(a,"noProgressTicks"),Eligible=eligible.Contains(id),Cohort=cohort.Select(x=>x.Id).ToArray(),CommonContributors=peers.Select(x=>x.Id).ToArray(),Common=common,Pace=pace,Stretch=stretch,ReconstructedOwnBound=reconstructed,Reconstruction="post-step canonical state; not transient selected proposal",Unavailable="transient peer/joint/yield/clearance reasons are not retained"});
                }
            }
            internal void Save(string path,string point)=>File.WriteAllText(path,Newtonsoft.Json.JsonConvert.SerializeObject(new{Point=point,SampleCapacity,RouteCapacity,LegCapacity,LegsCopied,DroppedSamples,DroppedRoutes,DroppedLegs,Routes=routes,Spines=spines,Samples=samples}));
        }
        // Compact ordinary proof: one detached frozen mapping and bounded
        // phase/count rows. No route copying, focused samples or new callback.
        sealed class FormationObservation
        {
            readonly int capacity;internal FormationObservation(int horizon=RecastFormationInputs.Horizon){capacity=horizon+64;}
            readonly ConcurrentQueue<object> frames=new ConcurrentQueue<object>();readonly object publicationGate=new object();object mapping;string seal;
            internal int DroppedFrames,MappingRows,UniqueRanks,Lanes,MaxAdmitted,MaxStaging,MaxRecovering;
            internal bool SawCollecting,SawFeeding,SawRecovering;
            static object Value(object owner,string name){var type=owner.GetType();return type.GetField(name,Hidden|BindingFlags.Public)?.GetValue(owner)??type.GetProperty(name,Hidden|BindingFlags.Public)?.GetValue(owner)??throw new MissingMemberException(type.FullName,name);}
            internal void Read(GroupOrderState group,long tick)
            {
                lock(publicationGate){
                var plan=group?.FormationPlan;if(plan==null)return;
                SawCollecting|=plan.Phase==RecastFormationPhase.Collecting;SawFeeding|=plan.Phase==RecastFormationPhase.Feeding;SawRecovering|=plan.Phase==RecastFormationPhase.Recovering;
                int staging=0,admitted=0,recovering=0,tombstone=0;foreach(var e in plan.Entries){if(e.Stage==RecastPassageStage.Staging)staging++;else if(e.Stage==RecastPassageStage.Admitted)admitted++;else if(e.Stage==RecastPassageStage.Recovering)recovering++;else tombstone++;}
                if(plan.Phase!=RecastFormationPhase.Collecting){
                    if(seal!=null&&seal!=plan.Seal)throw new InvalidOperationException("Frozen formation mapping identity changed.");
                    if(seal==null){seal=plan.Seal;MappingRows=plan.Entries.Count;UniqueRanks=plan.Entries.Select(e=>e.EntryOrdinal).Distinct().Count();Lanes=plan.LaneCount;
                        mapping=new{Tick=tick,plan.World,plan.Group,plan.RootOrder,plan.Command,plan.BirthMembership,Seal=seal,plan.EntryPortal,plan.ExitPortal,plan.LaneCount,Rows=plan.Entries.Select(e=>new{e.Entity,e.Slot,e.SpineIndex,e.EntryOrdinal,e.Lane,e.Incarnation,e.Order,e.Mobility,e.Request}).ToArray(),Spines=((IEnumerable)plan.Spines).Cast<object>().Select(spine=>{
                            var legs=(IReadOnlyList<NavTypedLeg>)Value(spine,"Legs");var provider=Value(legs[0],"RecastAuthorityProvider") as PartitionNavigationProvider??throw new InvalidOperationException("Formation proof lacks actual owned partition provider.");
                            return new{Binding=Value(spine,"Binding"),MapBinding=Value(spine,"MapBinding"),GraphBinding=Value(spine,"GraphBinding"),ProviderBinding=Value(spine,"ProviderBinding"),ProfileBinding=Value(spine,"ProfileBinding"),KnowledgeBinding=Value(spine,"KnowledgeBinding"),GeometryRevision=Value(spine,"GeometryRevision"),Radius=Value(spine,"Radius"),Seams=provider.Seams.Where(seam=>seam.Id==plan.EntryPortal||seam.Id==plan.ExitPortal).Select(seam=>new{seam.Id,seam.FromSurface,seam.ToSurface,seam.From,seam.To,seam.Allowed}).ToArray()};}).ToArray()};
                    }
                    MaxAdmitted=Math.Max(MaxAdmitted,admitted);MaxStaging=Math.Max(MaxStaging,staging);MaxRecovering=Math.Max(MaxRecovering,recovering);
                }
                if(frames.Count>=capacity){DroppedFrames++;return;}
                frames.Enqueue(new{Tick=tick,Phase=plan.Phase.ToString(),plan.FrontAnchor,plan.AnchorInitialized,plan.LaneCount,Staging=staging,Admitted=admitted,Recovering=recovering,Tombstones=tombstone});
                }
            }
            internal void Save(string path,string point,bool complete){object snapshot;lock(publicationGate)snapshot=new{Point=point,Complete=complete,Capacity=capacity,DroppedFrames,MappingRows,UniqueRanks,Lanes,MaxAdmitted,MaxStaging,MaxRecovering,SawCollecting,SawFeeding,SawRecovering,Mapping=mapping,Frames=frames.ToArray(),Claim="Detached post-step prefix; only acknowledged producer completion qualifies the full observation"};File.WriteAllText(path,Newtonsoft.Json.JsonConvert.SerializeObject(snapshot));}
        }
        // One bounded causal ledger from the actual threaded session host.
        // Only scalar copies survive the transport/root pin; no authority owner is retained.
        sealed class PipelineObservation
        {
            const BindingFlags All=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            readonly int Capacity,focusEntity,groupMember;
            internal PipelineObservation(int horizon=RecastFormationInputs.Horizon,int focus=53,int first=3){Capacity=horizon+64;focusEntity=focus;groupMember=first;}
            readonly ConcurrentQueue<object> rows=new ConcurrentQueue<object>(),lastMemberRefusals=new ConcurrentQueue<object>();readonly ConcurrentDictionary<string,object> focusBoundaries=new ConcurrentDictionary<string,object>();object firstRefusalIdentity,first49;long previousRefusals,previousMovementRefusals;string previousBatchFailure;object expectedKernel;byte[] terminalBytes;int lastInstalled=-1;string lastPhase,lastError;internal int Dropped;internal string SnapshotError,SnapshotStatus="UNAVAILABLE:no terminal pause ACK";
            static readonly string[] SessionNames={"LastNavigationStepPath","LastResultBatchFailure","LastResultBatchStage","LastResultBatchSourceReason","LastResultBatchDetail","OwnedResultBatchPublications","OwnedMovementPublications","OwnedMovementRefusals","LastOwnedMovementRefusal","LastOwnedMovementRefusalMessage","LastOwnedMovementNullReason","LastOwnedMovementNullDetail","LastAnswerNamesFailure"};
            static readonly string[] KernelNames={"RecastAttachments","RecastLongQueries","RecastIterations","RecastPreparationCommittedWork","RecastPreparationRefusals","RecastPreparationPreScopeRefusals","RecastPreparationPreForkRefusals","RecastPreparationAdvanceEnteredRefusals","RecastPreparationPublicationRefusals","LastProjectionFailure"};
            static MemberInfo Member(Type t,string name)=> (MemberInfo)t.GetField(name,All)??t.GetProperty(name,All);
            static object Value(object o,string name){if(o==null)return null;var m=Member(o.GetType(),name);if(m is FieldInfo f)return f.GetValue(o);if(m is PropertyInfo p)return p.GetValue(o);throw new MissingMemberException(o.GetType().FullName,name);}
            static Type MemberType(Type t,string name){var m=Member(t,name);if(m is FieldInfo f)return f.FieldType;if(m is PropertyInfo p)return p.PropertyType;throw new MissingMemberException(t.FullName,name);}
            static MethodInfo Method(Type t,string name)=>t.GetMethod(name,All)??throw new MissingMethodException(t.FullName,name);
            static object Invoke(object o,string name)=>Method(o.GetType(),name).Invoke(o,null);
            static object Count(object o)=>o==null?(object)"UNKNOWN":Value(o,"Count");
            static object Row(object row){if(row==null)return null;var request=(NavigationRequest)Value(row,"Request");var projection=Value(row,"ProjectionOwner");var answer=Value(row,"Answer") as NavigationAnswer;return new{request.Entity,request.Request,Status=answer==null?"no-answer":answer.Status.ToString(),Solved=Value(row,"Solved")!=null,LegCursor=projection==null?(object)"UNKNOWN":Value(projection,"LegCursor"),ReplayCursor=projection==null?(object)"UNKNOWN":Value(projection,"ReplayCursor"),PointCount=projection==null?(object)"UNKNOWN":Value(projection,"PointCount")};}
            object Focus(object dictionary,out object key){key=null;if(dictionary==null)return "UNKNOWN";foreach(var pair in (IEnumerable)dictionary){var row=Value(pair,"Value");var request=Value(row,"Request") as NavigationRequest;if(request?.Entity==focusEntity){key=Value(Value(pair,"Key"),"Value");return Row(row);}}return null;}
            static bool InQueue(object queue,object key){if(queue==null||key==null)return false;foreach(var item in (IEnumerable)queue)if(Equals(Value(item,"Value"),key))return true;return false;}
            const int PreparationCapacity=2048;
            readonly ConcurrentQueue<object> preparationRows=new ConcurrentQueue<object>();
            object preparationSource;Action preparationHook;FieldInfo preparationHookField;string preparationKey;
            internal string PreparationError;internal int PreparationDropped;
            internal bool HasPreparationSource=>preparationSource!=null;
            internal void Bind(UnityHostRouteService host){var worker=Value(host,"worker")??throw new InvalidOperationException("No actual threaded worker");expectedKernel=Value(worker,"kernel")??throw new InvalidOperationException("No actual worker kernel");
                if(Environment.GetEnvironmentVariable("RECAST_FORMATION_PREPARATION_CAPTURE")=="1"){
                    preparationHookField=expectedKernel.GetType().GetField("BeforePrepare",All)??throw new MissingFieldException("BeforePrepare");
                    var previous=(Action)preparationHookField.GetValue(expectedKernel);if(previous!=null&&previous.GetInvocationList().Any(h=>!(h.Target is PipelineObservation)))throw new InvalidOperationException("Preparation observer would replace another hook");
                    preparationHook=ReadPreparation;preparationHookField.SetValue(expectedKernel,(Action)Delegate.Combine(previous,preparationHook));
                }
            }
            // Runs only at the existing worker-owned rendezvous. Detached values
            // enter a bounded test buffer; no trace/query, per-quantum I/O or Nav write.
            void ReadPreparation(){
                if(PreparationError!=null)return;
                try{
                    var preparation=Value(expectedKernel,"recastPreparation");if(preparation==null)return;
                    for(var context=Value(preparation,"first");context!=null;context=Value(context,"Next")){
                        var members=(NavigationRequest[])Value(context,"Members");if(members.Length!=50||!members.Any(q=>q.Entity==groupMember))continue;
                        var helper=Value(context,"Attachment");if(helper==null)continue;
                        object Leg(object value){var leg=(NavTypedLeg)value;return new{leg.From,leg.To,Kind=leg.Kind.ToString(),leg.PortalId};}
                        if(preparationSource==null){var spine=Value(context,"Spine");var routes=(Array)Value(context,"Routes");
                            preparationSource=new{ProviderBinding=Value(Value(context,"Provider"),"Binding"),Members=members.Select(q=>new{q.Entity,q.Request,Start=q.StartLocation,Goal=q.GoalLocation,q.HeldIdentity,q.Profile.ProfileId,q.Profile.Revision,Profile=Invoke(q.Profile,"CaptureWireValues")}).ToArray(),Geometry=new{members[0].Geometry.Revision,members[0].Geometry.HalfExtent,Obstacles=members[0].Geometry.Obstacles.ToArray()},Spine=new{Binding=Value(spine,"Binding"),MapBinding=Value(spine,"MapBinding"),Fan=Value(spine,"Fan"),Stops=((int[])Value(spine,"Stops")).ToArray(),FanCenter=Value(spine,"FanCenter"),FanRadius=Value(spine,"FanRadius"),Legs=((IEnumerable)Value(spine,"Legs")).Cast<object>().Select(Leg).ToArray()},Baseline=routes.Cast<object>().Select(route=>route==null?null:new{Join=Value(route,"Join"),Exit=Value(route,"Exit"),OriginLegs=Value(route,"OriginLegs"),TerminalLeg=Value(route,"TerminalLeg"),Legs=((IEnumerable)Value(route,"Legs")).Cast<object>().Select(Leg).ToArray(),Waypoints=((IEnumerable)Value(route,"Waypoints")).Cast<object>().Select(w=>new{Location=Value(w,"Location"),Height=Value(w,"Height"),PortalId=Value(w,"PortalId")}).ToArray()}).ToArray()};
                        }
                        long work=(long)Value(helper,"Work");var stage=Value(helper,"stage").ToString();var origin=Value(helper,"origin");var field=Value(helper,"field");var fieldPhase=field==null?null:Value(field,"phase").ToString();string key=stage+":"+origin+":"+work/4096;
                        if(key==preparationKey)return;preparationKey=key;if(preparationRows.Count==PreparationCapacity){PreparationDropped++;return;}
                        preparationRows.Enqueue(new{Work=work,Stage=stage,Origin=origin,Member=Value(helper,"member"),Cursor=Value(helper,"cursor"),Count=Value(helper,"count"),SeedCount=Value(helper,"seedCount"),Field=field==null?null:new{Phase=fieldPhase,Work=Value(field,"Work"),Traces=Value(field,"Traces"),Expansions=Value(field,"Expansions"),Remaining=Value(field,"remainingMembers"),Targets=((NavPoint[])Value(field,"targets")).Take((int)Value(field,"members")).ToArray(),Resolved=((int[])Value(field,"resolved")).Take((int)Value(field,"members")).ToArray()}});return;
                    }
                }catch(Exception error){PreparationError=error.ToString();}
            }
            internal static string[] Validate()
            {
                var result=new List<string>();var session=typeof(NavigationSession);var kernel=MemberType(session,"certifiedHost");var host=Method(session,"PinCommittedHostRoot").ReturnType;
                var capture=Method(MemberType(session,"RoutePort"),"AcquireCapture");if(capture.ReturnType!=typeof(IDisposable)||capture.GetParameters().Length!=1||capture.GetParameters()[0].ParameterType!=typeof(Func<bool>))throw new InvalidOperationException("Capture exclusion metadata changed");result.Add("terminal protocol: actual pause ACK→AcquireCapture(Func<bool>) exclusion OUTSIDE transport→CaptureState→finally Dispose; no live capture");
                Method(session,"EnterTransport");Method(host,"Release");Method(session,"CaptureState");MemberType(typeof(CertifiedRouteWorker),"kernel");MemberType(typeof(UnityHostRouteService),"worker");
                foreach(string n in SessionNames)result.Add(session.FullName+"."+n+":"+MemberType(session,n).FullName);
                foreach(string n in KernelNames)result.Add(kernel.FullName+"."+n+":"+MemberType(kernel,n).FullName);
                foreach(string n in new[]{"Rows","Projection","Completion","Delivered","Frontier","QuantumSerial","QuantumRunnable"})result.Add(host.FullName+"."+n+":"+MemberType(host,n).FullName);
                var frontier=MemberType(host,"Frontier");foreach(string n in new[]{"Stage","OriginalBudget","SolveWorkLimit","SolveConsumed","ProjectionUsed","ProjectionAllowance","PhaseUsed"})MemberType(frontier,n);
                var dictionary=MemberType(host,"Rows");MemberType(dictionary,"Count");var keyType=dictionary.GetGenericArguments()[0];MemberType(keyType,"Value");foreach(string n in new[]{"Projection","Completion","Delivered"})MemberType(MemberType(host,n),"Count");
                foreach(string n in new[]{"pending","allocating","creditBank"})result.Add(session.FullName+"."+n+":"+MemberType(session,n).FullName);
                var row=session.Assembly.GetType("Spacewars.Runtime.NavigationHostRow",true);foreach(string n in new[]{"Request","Answer","Solved","ProjectionOwner"})MemberType(row,n);
                var projection=session.Assembly.GetType("Spacewars.Runtime.NavigationHostProjection",true);foreach(string n in new[]{"LegCursor","ReplayCursor","PointCount"})MemberType(projection,n);
                var bank=MemberType(session,"creditBank");if(bank.GetProperty("Item",All)==null)throw new MissingMemberException("creditBank.Item");
                result.Add("quota:OriginalBudget=requests4;SolveWorkLimit=work4096;SolveConsumed=work; ledger=bytes; capture=Nav14/GRP1/FormationV1");return result.ToArray();
            }
            internal void Read(PlayableDomain domain,Frame frame,bool force=false)
            {
                // Functional telemetry: critical changes and the journal's30-tick cadence.
                int installedNow=frame.Bodies.Count(b=>b.Selected&&b.Installed>0);string errorNow=Value(domain.Navigation,"LastResultBatchFailure") as string;
                bool critical=installedNow!=lastInstalled||frame.FormationPhase!=lastPhase||errorNow!=lastError;
                lastInstalled=installedNow;lastPhase=frame.FormationPhase;lastError=errorNow;
                if(!force&&!critical&&frame.Tick%30!=0)return;
                if(rows.Count>=Capacity){Dropped++;return;}var n=domain.Navigation;object scalar;bool refusal;bool pendingFocus=false,rowFocus=false,projectionFocus=false,completionFocus=false,deliveredFocusSeen=false,mailboxFocus=false,installedFocus=false;object root=null;
                using((IDisposable)Invoke(n,"EnterTransport")){
                    root=Invoke(n,"PinCommittedHostRoot");try{
                        var kernel=Value(n,"certifiedHost");if(!ReferenceEquals(kernel,expectedKernel))throw new InvalidOperationException("Session kernel differs from actual threaded worker");var diagnostics=SessionNames.ToDictionary(name=>name,name=>Value(n,name));var worker=KernelNames.ToDictionary(name=>name,name=>kernel==null?(object)"UNKNOWN:no-actual-host":Value(kernel,name));
                        var bank=Value(n,"creditBank");var bankType=sessionAssembly(n).GetType("Spacewars.Runtime.NavigationAccruedCredits",true);var credits=new Dictionary<string,object>();foreach(string name in new[]{"Watermark","Cap","Count","Accepted","Promoted","Discarded","SaturationDrops"}){var index=(int)bankType.GetField(name,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).GetRawConstantValue();credits[name]=bank==null?(object)"UNKNOWN:no-bank":bank.GetType().GetProperty("Item",All).GetValue(bank,new object[]{index});}
                        var hostRows=Value(root,"Rows");var delivered=Value(root,"Delivered");var focus=Focus(hostRows,out var key);var deliveredFocus=Focus(delivered,out var deliveredKey);var projection=Value(root,"Projection");var completion=Value(root,"Completion");var frontier=Value(root,"Frontier");var quota=new Dictionary<string,object>();foreach(string name in new[]{"Stage","OriginalBudget","SolveWorkLimit","SolveConsumed","ProjectionUsed","ProjectionAllowance","PhaseUsed"})quota[name]=frontier==null?(object)"UNKNOWN":Value(frontier,name)?.ToString();
                        var pending=n.Admission.Requests.FirstOrDefault(q=>q.Entity==focusEntity);var answer=n.Answers.CopyItems().FirstOrDefault(a=>a.Request.Entity==focusEntity);var budget=n.PhysicalHostBudget;
                        long refusals=kernel==null?0:Convert.ToInt64(Value(kernel,"RecastPreparationRefusals"));long movementRefusals=Convert.ToInt64(diagnostics["OwnedMovementRefusals"]);long preparationDelta=refusals-previousRefusals,movementDelta=movementRefusals-previousMovementRefusals;string batchFailure=diagnostics["LastResultBatchFailure"] as string;bool newBatchFailure=batchFailure!=null&&batchFailure!=previousBatchFailure;
                        pendingFocus=pending!=null;rowFocus=focus!=null;projectionFocus=InQueue(projection,key);completionFocus=InQueue(completion,key);deliveredFocusSeen=deliveredFocus!=null;mailboxFocus=answer!=null;installedFocus=frame.Bodies.Any(b=>b.Id==focusEntity&&b.Installed>0);
                        scalar=new{frame.Tick,PreparationRefusalDelta=preparationDelta,MovementRefusalDelta=movementDelta,NewBatchFailure=newBatchFailure,ActualKernel=kernel==null?"UNKNOWN":kernel.GetType().AssemblyQualifiedName,Applied=n.AppliedResults,Rejected=n.RejectedResults,Pending=n.Admission.Requests.Count,FocusPending=pending?.Request,Registered=n.RegisteredRouteCount,HostRows=Count(hostRows),Projection=Count(projection),Completion=Count(completion),Delivered=Count(delivered),FocusRow=focus,FocusInProjection=InQueue(projection,key),FocusInCompletion=InQueue(completion,key),FocusDelivered=deliveredFocus,MailboxCount=n.Answers.Count,FocusMailbox=answer==null?null:new{answer.Request.Entity,answer.Request.Request,Status=answer.Status.ToString()},FocusInstalled=frame.Bodies.FirstOrDefault(b=>b.Id==focusEntity)?.Installed,Installed=frame.Bodies.Count(b=>b.Selected&&b.Installed>0),Phase=n.GroupFor(groupMember)?.FormationPlan?.Phase.ToString(),Serial=Value(root,"QuantumSerial"),QuantumRunnable=Value(root,"QuantumRunnable"),Quota=quota,Credits=credits,Diagnostics=diagnostics,Worker=worker,budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes};
                        refusal=preparationDelta>0||movementDelta>0||newBatchFailure;previousRefusals=refusals;previousMovementRefusals=movementRefusals;previousBatchFailure=batchFailure;
                    }finally{if(root!=null)Invoke(root,"Release");}
                }
                rows.Enqueue(scalar);
                if(refusal&&firstRefusalIdentity==null)firstRefusalIdentity=scalar;
                int installed=frame.Bodies.Count(b=>b.Selected&&b.Installed>0);if(installed==49&&first49==null)first49=scalar;if(installed==49&&refusal&&lastMemberRefusals.Count<32)lastMemberRefusals.Enqueue(scalar);
                void Boundary(string name,bool observed){if(observed)focusBoundaries.TryAdd(name,scalar);}
                Boundary("pending",pendingFocus);Boundary("row",rowFocus);Boundary("projection",projectionFocus);Boundary("completion",completionFocus);Boundary("delivered",deliveredFocusSeen);Boundary("mailbox",mailboxFocus);Boundary("installed",installedFocus);
            }
            static Assembly sessionAssembly(NavigationSession n)=>n.GetType().Assembly;
            internal void CaptureTerminal(NavigationSession n,bool pauseAcknowledged,Func<bool> cancelled)
            {
                if(!pauseAcknowledged){SnapshotStatus="UNAVAILABLE:no terminal pause ACK";return;}
                if(terminalBytes!=null)return;
                try{var port=Value(n,"RoutePort");using((IDisposable)Method(port.GetType(),"AcquireCapture").Invoke(port,new object[]{cancelled})){
                    if(cancelled())throw new OperationCanceledException("Terminal capture cancelled");
                    using var state=n.CaptureState();terminalBytes=WorldWire.Pack(w=>WorldWire.Write(w,state));SnapshotStatus="CAPTURED:terminal pause ACK plus existing host exclusion";
                }}catch(Exception error){SnapshotError=error.ToString();SnapshotStatus="FAIL:terminal snapshot error";}
            }
            internal void ExportCapture(string output){if(terminalBytes!=null)File.WriteAllBytes(Path.Combine(output,"terminal-nav14.bin"),terminalBytes);}
            internal void Save(string output,bool complete)
            {
                if(preparationHook!=null){if(complete){var current=(Action)preparationHookField.GetValue(expectedKernel);if(current==null||!current.GetInvocationList().Contains(preparationHook))throw new InvalidOperationException("Preparation hook ownership changed");preparationHookField.SetValue(expectedKernel,(Action)Delegate.Remove(current,preparationHook));}File.WriteAllText(Path.Combine(output,"preparation-cause.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Complete=complete,PreparationCapacity,PreparationDropped,PreparationError,Source=preparationSource,Rows=preparationRows.ToArray(),Meaning="Existing worker-owned BeforePrepare value capture; no new trace/query/mutation. Diagnostic observer may affect scheduling; no performance or gameplay acceptance inferred."}));}
                var snapshot=new{Complete=complete,Capacity,FocusEntity=focusEntity,GroupMember=groupMember,Dropped,SnapshotStatus,SnapshotError,FirstAnyRefusal=Volatile.Read(ref firstRefusalIdentity),First49=Volatile.Read(ref first49),Focus53Boundaries=focusBoundaries.ToArray(),NewRefusalsAt49=lastMemberRefusals.ToArray(),Rows=rows.ToArray()};
                File.WriteAllText(Path.Combine(output,"pipeline-observation.json"),Newtonsoft.Json.JsonConvert.SerializeObject(snapshot));
            }
        }
        // Test-only bounded scalar capture from the existing actual movement callback.
        // Post-step counters at T-1 survive rejection at T; no retained live route owner.
        sealed class ApproachObservation
        {
            readonly int Capacity,SnapshotCapacity,PointCapacity;
            static readonly FieldInfo ActorField=typeof(NavigationSession).GetField("MovementDelayActor",Hidden);
            static readonly FieldInfo Computation=typeof(NavigationSession).GetField("MovementDelayComputation",Hidden);
            static readonly Type CausalType=typeof(NavUnit).Assembly.GetType("Spacewars.Simulation.NavCrowdMotion",true);
            static readonly FieldInfo CausalActor=CausalType.GetField("CausalActor",BindingFlags.Static|BindingFlags.NonPublic),CausalFrame=CausalType.GetField("Causal",BindingFlags.Static|BindingFlags.NonPublic);
            static readonly FieldInfo[] ComputationFields=Computation.FieldType.GetFields(BindingFlags.Public|BindingFlags.Instance);
            static readonly string[] CounterNames={"routeIndex","localIndex","noProgressTicks","blockedSeconds","localRoute"};
            static readonly FieldInfo[] Counters=CounterNames.Select(n=>typeof(NavUnit).GetField(n,Hidden)).ToArray();
            readonly ConcurrentQueue<object> rows=new ConcurrentQueue<object>(),snapshots=new ConcurrentQueue<object>();
            int focus;readonly bool adaptive;readonly double blockedTimeout,nearFailureLead;internal int NearFailureActor;internal long NearFailureTick;internal ApproachObservation(double timeout,int actor,bool autoFocus,int horizon=RecastFormationInputs.Horizon,int members=50,double captureNearFailureLead=0){blockedTimeout=timeout;nearFailureLead=captureNearFailureLead;focus=actor;adaptive=autoFocus;Capacity=horizon+64;SnapshotCapacity=256*members/50;PointCapacity=131072*members/50;}
            readonly HashSet<(int,long,int)> copied=new HashSet<(int,long,int)>();readonly Dictionary<int,int> priorCounters=new Dictionary<int,int>();readonly Dictionary<int,long> incrementAt=new Dictionary<int,long>(),lastObserved=new Dictionary<int,long>();
            int lastProgress,lastLocalLength,points;bool firstRefusal,nearTerminal;internal int DroppedRows,DroppedSnapshots;
            internal static string[] Validate()
            {
                if(ActorField?.FieldType!=typeof(int)||Computation==null||CausalActor==null||CausalFrame==null||Counters.Any(f=>f==null))throw new MissingMemberException("Actual approach capture metadata");
                foreach(var name in new[]{"Computed","ComputePath","BodyX","BodyZ","TargetX","TargetZ","Cap","ProposalX","ProposalZ","VelocityX","VelocityZ","Active","Yielding","Refused","Blocked","Overflow","StaticNoWinner","IndependentTurn"})if(!ComputationFields.Any(f=>f.Name==name))throw new MissingFieldException(Computation.FieldType.FullName,name);
                if(ComputationFields.Any(f=>!f.FieldType.IsPrimitive&&f.FieldType!=typeof(string)))throw new InvalidOperationException("Capture must detach scalar fields only");
                return new[]{"PASS existing MovementDelayActor:int; existing scalar MovementDelayComputation", "PASS actual routeIndex/localIndex/noProgressTicks/blockedSeconds/localRoute metadata", "post-step T-1 counters plus actual T computation; pre-rejection routes copied at first refusal/counter/local transition and timeout approach", "bounded2764 rows/256 snapshots/131072 points; adaptiveactualcounterfocus andall-memberonsetroutes; no Session/Step/Pump/query/production observer/per-tick I/O"};
            }
            internal void Bind(NavigationSession n){Validate();if(n.MovementDiagnosticObserver!=null)throw new InvalidOperationException("Legacy movement observer is forbidden");ActorField.SetValue(n,focus);Computation.SetValue(n,Activator.CreateInstance(Computation.FieldType));}
            void CopySnapshot(NavigationSession n,NavUnit a,long tick,int kind,string reason)
            {
                if(a.Route.Count==0||!copied.Add((a.Id,a.InstalledRequest,kind)))return;
                var local=(NavPoint[])Counters[4].GetValue(a);var storage=UnitStorage.GetValue(a);var owner=storage==null?null:InstalledOwner.GetValue(storage) as NavigationInstalledExecutionOwner;var typed=owner?.Corridor?.TypedRoute;var plan=n.GroupFor(a.Id)?.FormationPlan;int count=a.Route.Count+local.Length+(typed?.Legs.Count??0);
                if(snapshots.Count>=SnapshotCapacity||count>PointCapacity-points){DroppedSnapshots++;return;}
                snapshots.Enqueue(new{Tick=tick,Actor=a.Id,a.InstalledRequest,Reason=reason,a.Position,a.Location,a.Heading,a.Goal,NoProgressTicks=(int)Counters[2].GetValue(a),BlockedSeconds=(double)Counters[3].GetValue(a),RouteIndex=(int)Counters[0].GetValue(a),LocalIndex=(int)Counters[1].GetValue(a),Route=a.Route.ToArray(),LocalRoute=local.ToArray(),TypedLegs=typed?.Legs.Select(l=>new{l.From,l.To,Kind=l.Kind.ToString(),l.PortalId}).ToArray(),Roles=plan?.Entries.Select(e=>new{e.Entity,e.EntryOrdinal,e.Lane,Stage=e.Stage.ToString()}).ToArray()});points+=count;
            }
            internal void Read(PlayableDomain domain,Frame frame,bool capturePhysicalFailure=false)
            {
                var n=domain.Navigation;if(!n.Crowd.TryGet(focus,out var a))throw new InvalidOperationException("Missing actual focus actor");
                var causal=CausalFrame.GetValue(null);
                object Detach(object value){var type=value.GetType();if(type.IsPrimitive||type==typeof(string)||type.IsEnum)return value;return type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).ToDictionary(f=>f.Name,f=>Detach(f.GetValue(value)));}
                var causalDetached=Detach(causal);var computation=Computation.GetValue(n);var scalar=ComputationFields.ToDictionary(f=>f.Name,f=>f.GetValue(computation));
                var values=Counters.Select(f=>f.GetValue(a)).ToArray();int progress=(int)values[2],localLength=((NavPoint[])values[4]).Length;
                var plan=n.GroupFor(focus)?.FormationPlan;var role=plan?.Entries.FirstOrDefault(e=>e.Entity==focus);
                if(capturePhysicalFailure&&nearFailureLead>0&&NearFailureActor==0&&a.Moving&&!a.Held&&progress/30d>=blockedTimeout-nearFailureLead){
                    var failureFrame=(Dictionary<string,object>)causalDetached;var failureBody=(Dictionary<string,object>)failureFrame["Body"];var failureProposal=(Dictionary<string,object>)failureFrame["Final"];
                    // Existing value-only hint bits: Wait2/PolicyWait8. This
                    // observer must capture an actual physical refusal, not an
                    // old counter retained during a voluntary formation wait.
                    if((Convert.ToInt32(failureFrame["HintFlags"])&(2|8))==0&&(bool)failureBody["Active"]&&(bool)failureBody["OwnMoving"]&&!(bool)failureBody["Yielding"]&&!(bool)failureProposal["Overflow"]&&((bool)failureProposal["JointRefused"]||(bool)failureProposal["Blocked"]||(bool)failureProposal["StaticNoWinner"])){NearFailureActor=a.Id;NearFailureTick=frame.Tick;}
                }

                if(rows.Count>=Capacity){DroppedRows++;return;}
                rows.Enqueue(new{frame.Tick,SamplePoint="existing post-navigation CombatStepForQa",RepairCpuMs=n.Crowd.RepairCpuMs,RepairCount=n.Crowd.RepairCount,RepairFailed=n.Crowd.RepairFailed,Actor=a.Id,Computation=scalar,Causal=causalDetached,CommittedReceipt=typeof(NavigationSession).GetField("MovementDelayRecastReceipt",Hidden).GetValue(n),a.Position,a.Location,a.Heading,a.Goal,a.Radius,a.Speed,a.TurnSpeed,a.Moving,a.Held,a.Outcome,a.InstalledRequest,a.InstalledOrder,a.InstalledGroupId,RouteIndex=(int)values[0],RouteLength=a.Route.Count,LocalIndex=(int)values[1],LocalLength=localLength,NoProgressTicks=progress,BlockedSeconds=(double)values[3],Phase=plan?.Phase.ToString(),Stage=role?.Stage.ToString(),Role=role?.EntryOrdinal,Lane=role?.Lane,Anchor=plan?.FrontAnchor,Path=n.LastNavigationStepPath,SourceMismatch=typeof(NavigationSession).GetField("MovementDelaySourceMismatch",Hidden).GetValue(n)});
                bool computed=(bool)scalar["Computed"],refused=computed&&(bool)scalar["Active"]&&!(bool)scalar["Yielding"]&&((bool)scalar["Refused"]||(bool)scalar["Blocked"]||(bool)scalar["StaticNoWinner"]);
                bool near=progress>0&&progress/30d>=blockedTimeout-2d/30;
                if(!firstRefusal&&refused)CopySnapshot(n,a,frame.Tick,4,"first focused actual refusal");
                if(progress>0&&localLength>0&&localLength!=lastLocalLength)CopySnapshot(n,a,frame.Tick,2,"first focused local detour");
                if(near)CopySnapshot(n,a,frame.Tick,3,"pre-clear timeout approach");
                lastObserved[focus]=frame.Tick;int nextFocus=focus,oldest=0,fallback=focus;long leastObserved=long.MaxValue;
                foreach(var unit in n.Crowd.Units){
                    int counter=(int)Counters[2].GetValue(unit);priorCounters.TryGetValue(unit.Id,out int previous);
                    if(counter>previous)incrementAt[unit.Id]=frame.Tick;priorCounters[unit.Id]=counter;
                    if(!unit.Moving||unit.Held||unit.InstalledRequest<=0)continue;
                    long observed=lastObserved.TryGetValue(unit.Id,out long seen)?seen:long.MinValue;if(observed<leastObserved||observed==leastObserved&&unit.Id<fallback){leastObserved=observed;fallback=unit.Id;}
                    if(counter<=0)continue;
                    CopySnapshot(n,unit,frame.Tick,1,"first actual noProgress onset before clearing");
                    if(counter/30d>=blockedTimeout-2d/30)CopySnapshot(n,unit,frame.Tick,3,"all-member pre-clear timeout approach");
                    if(adaptive&&incrementAt.TryGetValue(unit.Id,out long at)&&frame.Tick-at<=1&&(counter>oldest||counter==oldest&&unit.Id<nextFocus)){oldest=counter;nextFocus=unit.Id;}
                }
                if(adaptive&&oldest==0)nextFocus=fallback; // Round-robin also exposes zero-counter formation waits.
                firstRefusal|=refused;nearTerminal|=near;lastProgress=progress;lastLocalLength=localLength;
                // Clear diagnostics only, so a refused/no-compute next tick cannot reuse this row.
                if(adaptive&&nextFocus!=focus){focus=nextFocus;lastProgress=lastLocalLength=0;firstRefusal=nearTerminal=false;}
                ActorField.SetValue(n,focus);Computation.SetValue(n,Activator.CreateInstance(Computation.FieldType));CausalFrame.SetValue(null,Activator.CreateInstance(CausalFrame.FieldType));CausalActor.SetValue(null,focus);
            }
            internal void Save(string output,bool complete)=>File.WriteAllText(Path.Combine(output,"approach-observation.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Complete=complete,Capacity,SnapshotCapacity,PointCapacity,nearFailureLead,NearFailureActor,NearFailureTick,DroppedRows,DroppedSnapshots,PointsCopied=points,Rows=rows.ToArray(),Snapshots=snapshots.ToArray(),Meaning="Actual post-step scalars; previous tick counters/routes qualify pre-clear interval. No specific refusing peer inferred from proximity."}));
        }
        public static string[] ValidateApproachMetadata()=>ApproachObservation.Validate();
        public static string[] ValidatePipelineMetadata()=>PipelineObservation.Validate();
        sealed class GuardAwait
        {
            readonly Action<string> service;readonly Func<double> now;Exception failure;
            internal string FailureName,FailurePhase;internal double FailureDeadline,FailureTime;
            internal string OriginalFailure=>failure?.ToString();
            internal GuardAwait(Action<string> service,Func<double> now){this.service=service;this.now=now;}
            void Remember(Exception error,string name,string phase,double deadline){if(failure!=null)return;failure=error;FailureName=name;FailurePhase=phase;FailureDeadline=deadline;FailureTime=now();}
            internal void Rethrow(){if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();}
            internal IEnumerator Until(Func<bool> done,string name,double deadline,string phase){while(true){bool waiting=false;try{waiting=!done()&&now()<deadline;}catch(Exception e){Remember(e,name,phase,deadline);}if(failure!=null)yield break;if(!waiting)break;try{service(name);}catch(Exception e){Remember(e,name,phase,deadline);}if(failure!=null)yield break;yield return null;}try{Assert.True(done(),name);}catch(Exception e){Remember(e,name,phase,deadline);}}
        }
        static void Attempt(ConcurrentQueue<string> errors,string name,Action action){try{action?.Invoke();}catch(Exception error){errors.Enqueue(name+": "+error);}}
        // Shared by the real witness and the offline fault-injection preflight.
        // Never throw from finally; all callbacks, including receipt/export, are isolated.
        static void FinalizeObserved(bool pauseAck,Action capture,Action cleanup,Action shutdown,Action receipt,IEnumerable<KeyValuePair<string,Action>> exports,ConcurrentQueue<string> errors)
        {
            if(pauseAck)Attempt(errors,"capture",capture);
            Attempt(errors,"cleanup",cleanup);
            Attempt(errors,"journal-shutdown",shutdown);
            Attempt(errors,"minimal-exit-receipt",receipt);
            foreach(var export in exports)Attempt(errors,export.Key,export.Value);
        }
        sealed class CleanupReceipt
        {
            public bool RuntimeStopped,WorkerStopped;public long? PhysicalBytes,HeldBytes;public int? ActiveScopes;
            public string Census="UNKNOWN:not acknowledged";
            internal bool Ack=>RuntimeStopped&&WorkerStopped;
        }
        static CleanupReceipt Cleanup(PlayableRuntime runtime,UnityHostRouteService host,PlayableDomain domain,ConcurrentQueue<string> errors)
        {
            var result=new CleanupReceipt();if(runtime==null){result.Census="UNAVAILABLE:no runtime";return result;}
            CertifiedRouteWorker worker=null;bool workerKnown=host==null;
            Attempt(errors,"resolve-domain",()=>domain=domain??Domain(runtime));
            Attempt(errors,"detach-observer",()=>{if(domain!=null)domain.CombatStepForQa=null;});
            Attempt(errors,"retain-worker-handle",()=>{worker=host==null?null:(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",Hidden).GetValue(host);workerKnown=true;});
            Attempt(errors,"request-stop",runtime.RequestStop);
            Attempt(errors,"host-dispose",()=>host?.Dispose());
            Attempt(errors,"runtime-dispose",runtime.Dispose);
            Attempt(errors,"runtime-worker-ACK",()=>{SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000);result.RuntimeStopped=runtime.IsStopped;result.WorkerStopped=workerKnown&&(worker==null||worker.IsStopped);if(!result.Ack)throw new InvalidOperationException("runtime/worker ACK unavailable");});
            if(result.Ack&&domain!=null)Attempt(errors,"release-owned-navigation",()=>{var n=domain.Navigation;n.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();var b=n.PhysicalHostBudget;result.PhysicalBytes=b.PhysicalBytes;result.HeldBytes=b.HeldBytes;result.ActiveScopes=b.ActiveScopes;result.Census="OBSERVED after runtime/worker ACK";if(b.PhysicalBytes!=0||b.HeldBytes!=0||b.ActiveScopes!=0)throw new InvalidOperationException("Owned navigation census nonzero");});
            return result;
        }
        static string FormatExitReceipt(string candidate,string phase,double deadline,GuardAwait wait,long? tick,PlayableRuntimeMetrics metrics,long lastTick,long count,bool pauseAck,CleanupReceipt census,string snapshot,string[] faults)
        {
            string Number(double value)=>value.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            return "Candidate="+candidate+"\nPhase="+phase+"\nDeadline="+Number(deadline)+"\nWaitFailureName="+wait.FailureName+"\nWaitFailurePhase="+wait.FailurePhase+"\nWaitFailureDeadline="+Number(wait.FailureDeadline)+"\nWaitFailureTime="+Number(wait.FailureTime)+"\nOriginalWaitFailure="+wait.OriginalFailure+
                "\nPublishedTick="+tick+"\nTickCpuMs="+(metrics==null?"UNKNOWN":Number(metrics.TickCpuMilliseconds))+"\nMaximumTickCpuMs="+(metrics==null?"UNKNOWN":Number(metrics.MaximumTickCpu))+"\nTickIntervalMs="+(metrics==null?"UNKNOWN":Number(metrics.TickIntervalMilliseconds))+"\nMissedDeadlines="+metrics?.MissedDeadlines+"\nNavigationPending="+metrics?.NavigationPending+
                "\nObserverLastTick="+lastTick+"\nObserverCount="+count+"\nPauseACK="+pauseAck+"\nRuntimeACK="+census?.RuntimeStopped+"\nWorkerACK="+census?.WorkerStopped+"\nSnapshot="+snapshot+"\nFaults="+string.Join("\n",faults)+"\n";
        }
        public static string[] ValidateFinalizationOffline()
        {
            var proof=new List<string>();
            foreach(bool pauseAck in new[]{false,true}){
                var errors=new ConcurrentQueue<string>();var calls=new List<string>();var original=new InvalidOperationException("original wait failure");var wait=new GuardAwait(_=>throw original,()=>0);var pending=wait.Until(()=>false,"terminal pause",180,"movement");pending.MoveNext();Exception retained=null;try{wait.Rethrow();}catch(Exception error){retained=error;}string receipt=null;
                var exports=new[]{new KeyValuePair<string,Action>("broken-export",()=>{calls.Add("export1");throw new IOException("export fault");}),new KeyValuePair<string,Action>("later-export",()=>calls.Add("export2"))};
                FinalizeObserved(pauseAck,()=>{calls.Add("capture");throw new IOException("capture fault");},()=>calls.Add("cleanup"),()=>calls.Add("shutdown"),()=>{calls.Add("receipt");receipt=FormatExitReceipt("offline","movement",180,wait,1950,null,1951,1952,pauseAck,null,"UNAVAILABLE",errors.ToArray());if(!ReferenceEquals(retained,original))throw new InvalidOperationException("Original failure masked");},exports,errors);
                if(calls.Contains("capture")!=pauseAck||calls.IndexOf("cleanup")>calls.IndexOf("export1")||calls.IndexOf("shutdown")>calls.IndexOf("export1")||!calls.Contains("export2")||!calls.Contains("receipt")||!ReferenceEquals(retained,original)||errors.Count!=(pauseAck?2:1)||!receipt.Contains("WaitFailureName=terminal pause")||!receipt.Contains("OriginalWaitFailure=System.InvalidOperationException: original wait failure")||!receipt.Contains("Deadline=180"))throw new InvalidOperationException("Shared finalization fault contract failed");
                proof.Add("PASS pauseAck="+pauseAck+" calls="+string.Join(",",calls)+" isolatedFaults="+errors.Count+" original retained");
            }
            {var calls=new List<string>();var errors=new ConcurrentQueue<string>();FinalizeObserved(false,null,()=>{calls.Add("cleanup");throw new IOException("cleanup fault");},()=>{calls.Add("shutdown");throw new IOException("shutdown fault");},()=>{calls.Add("receipt");throw new IOException("receipt fault");},new[]{new KeyValuePair<string,Action>("later-export",()=>calls.Add("export"))},errors);if(!calls.SequenceEqual(new[]{"cleanup","shutdown","receipt","export"})||errors.Count!=3)throw new InvalidOperationException("Cleanup fault skipped later stages");proof.Add("PASS cleanup/shutdown/receipt faults isolated; later export attempted");}
            return proof.ToArray();
        }
        [UnityTest,Timeout(240000)]
        public IEnumerator FiftyMembersNarrowRecoverAndNaturallyArrive()
        {
            string output=Environment.GetEnvironmentVariable("RECAST_FORMATION_OUTPUT")??Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/recast-formation/ordinary-a1"));Directory.CreateDirectory(output);
            Assert.True(NativeSilentStartup.IsSilent);string testLogs=null;LiveVideo video=null;
            var p=PlayableProfile.RecastGroupCorridorDefault;var map=(ThreeCrossingsMap)p.AuthoredMap;var config=RecastFormationInputs.Create(p,out var scenario,out var rejected);var goal=new NavPoint(-23.2,29.4);
            var bridge=map.Supports.Single(s=>s.SurfaceId=="bridge/north");double bridgeWidth=bridge.Bounds.MaxZ-bridge.Bounds.MinZ;
            PlayableRuntime runtime=null;UnityHostRouteService host=null;PlayableDomain domain=null;int[] ids=Array.Empty<int>();HashSet<int> selected=null;var queue=new ConcurrentQueue<Frame>();var frames=new List<Frame>();var observerErrors=new ConcurrentQueue<string>();var cleanupErrors=new ConcurrentQueue<string>();
            long issued=-1,frameCount=0;bool arrived=false,complete=false;string phase="startup";CleanupReceipt census=null;bool terminalPauseAck=false;long observerLastTick=-1;CertifiedRoutePublication lastHost=null;double deadline=Time.realtimeSinceStartupAsDouble+180;ulong native=NativeCount();
            var firstMotion=new Dictionary<int,long>();var arrivals=new Dictionary<int,long>();var assigned=new Dictionary<int,Body>();var visited=new HashSet<int>();var failureCounts=new Dictionary<string,int>();var samples=new List<string>();long failures=0;double initialSpan=0,bridgeSpan=0,finalSpan=0,goalSpan=0;int simultaneous=0;
            ApproachObservation approach=Environment.GetEnvironmentVariable("RECAST_FORMATION_CAUSAL")=="1"?new ApproachObservation(p.Navigation.BlockedTimeoutSeconds,int.TryParse(Environment.GetEnvironmentVariable("RECAST_FORMATION_FOCUS"),out var focusActor)?focusActor:13,Environment.GetEnvironmentVariable("RECAST_FORMATION_AUTOFOCUS")!="0"):null;MovementObservation movement=null;var formation=new FormationObservation();var pipeline=new PipelineObservation();string[] metadata=null; // Ordinary A4 disables the spent A3 diagnostic.
            var wait=new GuardAwait(name=>{host.Service(runtime,8);Assert.IsNull(runtime.Latest?.Failure,name);},()=>Time.realtimeSinceStartupAsDouble);
            IEnumerator Until(Func<bool> done,string name)=>wait.Until(done,name,deadline,phase);
            void Drain(){while(queue.TryDequeue(out var f))frames.Add(f);}
            Frame Read(){var n=domain.Navigation;var g=n.GroupFor(ids[0]);bool all=g!=null&&g.Members.Count()==50&&g.Members.All(m=>m.HasGoal&&m.GoalLocation.HasValue);var result=new Frame{Tick=domain.Tick,Group=g?.GroupId??0,Revision=g?.OrderRevision??0,OriginalGoal=g?.OriginalGoal??default,FormationPhase=g?.FormationPlan?.Phase.ToString(),Bodies=n.Crowd.Units.Select(a=>{var m=g?.Members.SingleOrDefault(x=>x.Entity==a.Id);bool has=m!=null&&m.HasGoal&&m.GoalLocation.HasValue;return new Body{Id=a.Id,Position=a.Position,Location=a.Location,Goal=a.Goal,Assigned=has?m.GoalLocation.Value.Position:a.Goal,Radius=a.Radius,Speed=a.Speed,Selected=selected.Contains(a.Id),Moving=a.Moving,Outcome=a.Outcome,Installed=a.InstalledRequest,Slot=m?.Slot??-1,HasSlot=has,AllSlots=all,Progress=m?.Recast??default,Spine=Spine(a)};}).OrderBy(a=>a.Id).ToArray()};formation.Read(g,result.Tick);return result;}
            void Observe(){try{if(Interlocked.Increment(ref frameCount)>RecastFormationInputs.Horizon+64){observerErrors.Enqueue("frame capacity exceeded");runtime.RequestPause(true);return;}var f=Read();Interlocked.Exchange(ref observerLastTick,f.Tick);pipeline.Read(domain,f);approach?.Read(domain,f);movement?.Read(domain,f,p.GroupMarchMaximumStretch);queue.Enqueue(f);long start=Interlocked.Read(ref issued);if(start>=0&&(f.Tick-start>=RecastFormationInputs.Horizon||f.Bodies.Where(b=>b.Selected).All(b=>b.Outcome==NavigationOutcome.Arrived)))runtime.RequestPause(true);}catch(Exception e){observerErrors.Enqueue(e.ToString());runtime.RequestPause(true);}}
            void Fail(string kind,string detail){failures++;failureCounts.TryGetValue(kind,out int count);failureCounts[kind]=count+1;if(samples.Count<128)samples.Add(kind+": "+detail);}
            try{
                metadata=ValidatePipelineMetadata();if(approach!=null)metadata=metadata.Concat(ValidateApproachMetadata()).ToArray();TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));testLogs=TestMatchLogSession.CurrentDirectory;Assert.IsNotNull(testLogs);
                runtime=new PlayableRuntime(config,RecastFormationInputs.Generation,startPaused:true);host=new UnityHostRouteService();yield return Until(()=>runtime.IsPrepared&&runtime.Latest.Paused&&host.CertifiedPublication?.RecastFoundationBuilds==1,"actual public native startup");wait.Rethrow();
                Assert.Zero(runtime.Latest.Tick);domain=Domain(runtime);ids=runtime.Latest.Entities.Where(a=>a.Owner==PlayableOwner.Player&&a.Kind==PlayableEntityKind.Explorer).Select(a=>a.Id).OrderBy(x=>x).ToArray();Assert.AreEqual(50,ids.Length);CollectionAssert.AreEqual(new[]{3}.Concat(Enumerable.Range(5,49)).ToArray(),ids);selected=new HashSet<int>(ids);
                Assert.AreEqual(51,domain.Navigation.Crowd.Units.Count);Assert.AreEqual(100,runtime.Latest.Population.Living);Assert.Zero(runtime.Latest.Population.Reserved);Assert.AreEqual(100,runtime.Latest.Population.Capacity);Assert.True(config.Roster.All(a=>a.Control==OfflineControl.Human));Assert.AreEqual(5,map.Revision);
                Assert.True(map.TryLocate(goal,p.ExplorerCollisionRadius,out var typed));Assert.AreEqual("bank/west",typed.SurfaceId);var initial=Read();frames.Add(initial);var parked=initial.Bodies.Where(b=>!b.Selected).Single();Assert.AreEqual(4,parked.Id);initialSpan=initial.Bodies.Where(b=>b.Selected).Max(b=>b.Position.Z)-initial.Bodies.Where(b=>b.Selected).Min(b=>b.Position.Z);
                if(Environment.GetEnvironmentVariable("RECAST_FORMATION_VIDEO")=="1"){video=new LiveVideo(p,Path.Combine(output,"live-video"));video.Read(runtime,true);}
                pipeline.Bind(host);approach?.Bind(domain.Navigation);Assert.IsNull(domain.CombatStepForQa);domain.CombatStepForQa=Observe;runtime.RequestPause(false);yield return Until(()=>!runtime.Latest.Paused,"ordinary resume");wait.Rethrow();
                Assert.True(runtime.TrySubmit(new PlayableCommand(805,1,"player-1",PlayableCommandKind.Move,ids,goal).WithResolvedLocation(typed)).Accepted);
                yield return Until(()=>runtime.ReceiptsAfter(0).Any(r=>r.Sequence==1&&r.Status!=PlayableCommandStatus.Accepted),"ordinary Move receipt");wait.Rethrow();var receipt=runtime.ReceiptsAfter(0).Single(r=>r.Sequence==1&&r.Status!=PlayableCommandStatus.Accepted);Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);Interlocked.Exchange(ref issued,receipt.AppliedTick);phase="ordinary movement";
                while(Time.realtimeSinceStartupAsDouble<deadline){host.Service(runtime,8);Assert.IsNull(runtime.Latest.Failure);video?.Read(runtime);Drain();var last=frames.LastOrDefault();arrived=last!=null&&last.Bodies.Where(b=>b.Selected).All(b=>b.Outcome==NavigationOutcome.Arrived);if(arrived||last!=null&&last.Tick-issued>=RecastFormationInputs.Horizon||!observerErrors.IsEmpty)break;yield return null;}
                runtime.RequestPause(true);yield return Until(()=>runtime.Latest.Paused,"terminal pause");terminalPauseAck=runtime.Latest?.Paused==true;wait.Rethrow();Drain();phase="safety/formation analysis";Frame previous=null;var geometry=runtime.NavigationGeometry;
                foreach(var f in frames){
                    if(f.Group>0&&(f.Group!=1||f.Revision!=1||!f.OriginalGoal.Equals(goal)))Fail("group intent",f.Group+"/"+f.Revision+"@"+f.Tick);
                    var onBridge=f.Bodies.Where(b=>b.Selected&&b.Location.SurfaceId=="bridge/north").ToArray();foreach(var b in onBridge)visited.Add(b.Id);simultaneous=Math.Max(simultaneous,onBridge.Length);if(onBridge.Length>=2)bridgeSpan=Math.Max(bridgeSpan,onBridge.Max(b=>b.Position.Z)-onBridge.Min(b=>b.Position.Z));
                    foreach(var b in f.Bodies){
                        if(!map.IsValidLocation(b.Location,b.Radius)||!geometry.IsFree(b.Position,b.Radius))Fail("footprint",b.Id+"@"+f.Tick);
                        if(b.Selected){if(b.Moving&&!b.AllSlots)Fail("motion before slots",b.Id+"@"+f.Tick);if(b.Outcome==NavigationOutcome.Rejected||b.Outcome==NavigationOutcome.Blocked||b.Outcome==NavigationOutcome.Unreachable)Fail("terminal failure",b.Id+"@"+f.Tick+"="+b.Outcome);
                            if(b.HasSlot){if(!assigned.TryGetValue(b.Id,out var old))assigned.Add(b.Id,b);else if(old.Slot!=b.Slot||!old.Assigned.Equals(b.Assigned))Fail("slot identity",b.Id+"@"+f.Tick);}if(b.Outcome==NavigationOutcome.Arrived&&!arrivals.ContainsKey(b.Id))arrivals.Add(b.Id,f.Tick);
                        }else if(!b.Position.Equals(parked.Position))Fail("parked moved",b.Id+"@"+f.Tick);
                        if(previous==null)continue;var before=previous.Bodies.Single(a=>a.Id==b.Id);double dx=b.Position.X-before.Position.X,dz=b.Position.Z-before.Position.Z;
                        if(dx*dx+dz*dz>b.Speed*b.Speed/900+1e-9)Fail("speed",b.Id+"@"+f.Tick);
                        if(!geometry.SegmentFree(before.Position,b.Position,b.Radius)||!map.TryTraverse(before.Location,b.Position,b.Radius,out var reached)||!reached.Equals(b.Location))Fail("typed sweep",b.Id+"@"+f.Tick);
                        if(b.Selected&&dx*dx+dz*dz>1e-12&&!firstMotion.ContainsKey(b.Id))firstMotion.Add(b.Id,f.Tick);
                        foreach(var peer in f.Bodies.Where(a=>a.Id<b.Id)){var oldPeer=previous.Bodies.Single(a=>a.Id==peer.Id);double sx=before.Position.X-oldPeer.Position.X,sz=before.Position.Z-oldPeer.Position.Z,vx=dx-(peer.Position.X-oldPeer.Position.X),vz=dz-(peer.Position.Z-oldPeer.Position.Z),q=vx*vx+vz*vz,t=q==0?0:Math.Max(0,Math.Min(1,-(sx*vx+sz*vz)/q)),x=sx+vx*t,z=sz+vz*t;if(x*x+z*z<Math.Pow(b.Radius+peer.Radius,2)-1e-8)Fail("continuous peer sweep",b.Id+"/"+peer.Id+"@"+f.Tick);}
                    }previous=f;
                }
                Assert.IsEmpty(observerErrors);Assert.Zero(pipeline.Dropped);Assert.Zero(failures,string.Join("\n",samples));Assert.True(arrived,"all fifty naturally Arrived within2700ticks/deadline180");Assert.AreEqual(50,assigned.Count);Assert.AreEqual(50,firstMotion.Count);Assert.AreEqual(50,arrivals.Count);Assert.AreEqual(50,visited.Count,"every member traversed actual north bridge");Assert.GreaterOrEqual(simultaneous,2);
                Assert.Zero(formation.DroppedFrames);Assert.AreEqual(50,formation.MappingRows,"actual frozen formation rows");Assert.AreEqual(50,formation.UniqueRanks,"stable unique entry ranks");Assert.GreaterOrEqual(formation.Lanes,2);Assert.True(formation.SawCollecting&&formation.SawFeeding&&formation.SawRecovering,"ordinary collecting/feed/recovery phases");Assert.Greater(formation.MaxAdmitted,0);Assert.Less(formation.MaxAdmitted,50,"finite narrow window");Assert.Greater(formation.MaxStaging,0);Assert.Greater(formation.MaxRecovering,0);
                var final=frames.Last().Bodies.Where(b=>b.Selected).ToArray();foreach(var b in final){Assert.AreEqual(NavigationOutcome.Arrived,b.Outcome);Assert.AreEqual("bank/west",b.Location.SurfaceId);Assert.IsNotNull(b.Spine);Assert.LessOrEqual(RecastFormationInputs.Distance(b.Position,assigned[b.Id].Assigned),p.Navigation.ArrivalTolerance);Assert.GreaterOrEqual(firstMotion[b.Id],issued);}
                finalSpan=final.Max(b=>b.Position.Z)-final.Min(b=>b.Position.Z);goalSpan=assigned.Values.Max(b=>b.Assigned.Z)-assigned.Values.Min(b=>b.Assigned.Z);double fit=bridgeWidth-2*p.ExplorerCollisionRadius;
                Assert.Greater(initialSpan,fit);Assert.LessOrEqual(bridgeSpan,fit+1e-8);Assert.Greater(finalSpan,fit);Assert.GreaterOrEqual(finalSpan,goalSpan-2*p.Navigation.ArrivalTolerance-1e-8);lastHost=host.CertifiedPublication;Assert.AreEqual(2,lastHost.RecastLongQueries);complete=true;phase="complete";
            }finally{
                // The receipt uses only immutable published runtime data and atomic counters.
                // Never enumerate an observer buffer before owned shutdown/ACK.
                var exitSnapshot=runtime?.Latest;
                if(video!=null){try{video.Read(runtime,true);video.Dispose();}catch(Exception e){cleanupErrors.Enqueue("live-video: "+e);}}
                var exports=new[]{
                    new KeyValuePair<string,Action>("metadata-export",()=>File.WriteAllText(Path.Combine(output,"pipeline-metadata.json"),Newtonsoft.Json.JsonConvert.SerializeObject(metadata))),
                    new KeyValuePair<string,Action>("terminal-nav14-export",()=>pipeline.ExportCapture(output)),
                    new KeyValuePair<string,Action>("approach-export",()=>approach?.Save(output,census?.Ack==true)),
                    new KeyValuePair<string,Action>("pipeline-export",()=>pipeline.Save(output,census?.Ack==true)),
                    new KeyValuePair<string,Action>("formation-export",()=>formation.Save(Path.Combine(output,"formation-observation.json"),"after cleanup attempt",census?.Ack==true)),
                    new KeyValuePair<string,Action>("frames-export",()=>{Drain();File.WriteAllText(Path.Combine(output,"frames.json"),Newtonsoft.Json.JsonConvert.SerializeObject(frames.ToArray()));}),
                    new KeyValuePair<string,Action>("result-export",()=>{File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Result=complete&&cleanupErrors.Count==0?"PASS":"FAIL",phase,Seed=RecastFormationInputs.Seed,Generation=RecastFormationInputs.Generation,Horizon=RecastFormationInputs.Horizon,Dt=1d/30,DeadlineSeconds=180,TimeoutMs=240000,Candidate=Environment.GetEnvironmentVariable("RECAST_FORMATION_CANDIDATE"),ObservationMode="baseline all-actor frames; functional transitions plus30-tick pipeline telemetry; detached immutable export",Map=map.Id,map.Revision,Profile=p.ProfileId,ProfileRevision=p.Revision,ProfileBinding=Binding(p),config.SourceIdentity,config.MapIdentity,Scenario=scenario,RejectedCandidates=rejected,Ids=ids,Goal=goal,Issued=issued,FirstMotion=firstMotion,Arrivals=arrivals,Arrived=arrived,Assigned=assigned.ToDictionary(x=>x.Key,x=>new{x.Value.Slot,x.Value.Assigned}),InitialSpan=initialSpan,BridgeWidth=bridgeWidth,BridgeSpan=bridgeSpan,SimultaneousOnBridge=simultaneous,BridgeVisited=visited.ToArray(),FinalSpan=finalSpan,GoalSpan=goalSpan,FailureCount=failures,FailureCounts=failureCounts,FailureSamples=samples,SamplesBoundedAt=128,ObserverErrors=observerErrors.ToArray(),CleanupErrors=cleanupErrors.ToArray(),Census=census,LastHost=lastHost,NativeBefore=native,NativeAfter=census?.Ack==true?(ulong?)NativeCount():null,ReleaseFaults=census?.Ack==true?(long?)ReleaseFaults():null,TestLogs=testLogs},Newtonsoft.Json.Formatting.Indented));}),
                    new KeyValuePair<string,Action>("export-faults-receipt",()=>File.WriteAllText(Path.Combine(output,"export-faults.txt"),string.Join("\n",cleanupErrors.ToArray())))
                };
                FinalizeObserved(terminalPauseAck,
                    ()=>{if(domain!=null){pipeline.Read(domain,Read(),true);pipeline.CaptureTerminal(domain.Navigation,true,()=>runtime.IsStopRequested);if(pipeline.SnapshotError!=null)throw new InvalidOperationException(pipeline.SnapshotError);}},
                    ()=>{Attempt(cleanupErrors,"last-host-publication",()=>{if(host!=null)lastHost=host.CertifiedPublication;});census=Cleanup(runtime,host,domain,cleanupErrors);},
                    TestMatchLogSession.Shutdown,
                    ()=>File.WriteAllText(Path.Combine(output,"exit-receipt.txt"),FormatExitReceipt(Environment.GetEnvironmentVariable("RECAST_FORMATION_CANDIDATE"),phase,deadline,wait,exitSnapshot?.Tick,exitSnapshot?.Metrics,Interlocked.Read(ref observerLastTick),Interlocked.Read(ref frameCount),terminalPauseAck,census,pipeline.SnapshotStatus,cleanupErrors.ToArray())),
                    exports,cleanupErrors);
            }
            Assert.IsEmpty(cleanupErrors);Assert.IsNull(pipeline.SnapshotError);Assert.True(pipeline.SnapshotStatus.StartsWith("CAPTURED:"),pipeline.SnapshotStatus);Assert.True(census?.Ack==true,"runtime/worker ACK");Assert.AreEqual(native,NativeCount());Assert.Zero(ReleaseFaults());
        }
    }
}
