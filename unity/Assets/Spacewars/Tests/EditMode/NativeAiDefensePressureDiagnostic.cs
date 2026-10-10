using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiInitiativeTests
    {
        [Test] public void DefensePressure_DiagnosticUsesOriginalScenarioAndReadOnlyProposal()
        {
            var c=DefenseConfig();var a=new PlayableAuthorityTick(c,71);var owner=Owner(a);
            var defense=(AiDefensePlanner)Get(owner,"defense");var planner=(AiArmyPlanner)Get(owner,"armyPlanner");
            var registry=(AiArmyRegistry)Get(owner,"armies");
            using(var host=new UnityHostRouteService()){
                for(int i=0;i<120&&!defense.Active;i++)Step(a,host);
                Assert.True(defense.Active);
                var tick=a.Tick;for(int i=0;i<15;i++)Assert.True(a.TryAdvance(paused:true));Assert.AreEqual(tick,a.Tick);
                long clear=-1;bool defended=false,macro=false,recovered=false;
                var timeline=new System.Collections.Generic.List<object>();
                for(int i=0;i<1500;i++){
                    Step(a,host);var cp=a.CaptureDiagnosticCheckpoints().Single();
                    var view=a.ParticipantView("west-owner");var observation=PlayableAiObservation.From(view);
                    defended|=cp.Records.Any(r=>r.Policy==AiDefensePlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied);
                    macro|=cp.Records.Any(r=>(r.Policy=="production"||r.Policy=="economy"||r.Policy=="research")&&r.Status==PlayableAiDeliveryStatus.Applied);
                    if(defended&&AiDefensePlanner.Threats(observation,P).Length==0&&clear<0)clear=a.Tick;
                    if(clear>=0&&cp.Records.Any(r=>r.Policy==AiArmyPlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ApplicationTick>clear)){recovered=true;break;}
                    if(a.Tick==clear||a.Tick%150==1||i==1499){
                        var before=a.CaptureBytes();var domain=Get(a,"domain");
                        var aiView=(PlayableSnapshot)domain.GetType().GetMethod("PlayerSnapshotForArmyAi",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(domain,
                            new object[]{view.Sequence,view.Status,view.Paused,view.Metrics,view.Failure,view.Seed,view.Owner,true,registry.Capture()});
                        var actual=PlayableAiObservation.From(aiView);
                        var proposal=planner.ProposeWithRecords(actual,cp.Opening,P,AiProfile.Initial,registry,cp.Records);
                        var requests=AiArmyPlanner.RouteRequests(aiView.Entities,aiView.Buildings,actual.PublicScoutObjectives,actual.Owner,actual.Generation,actual.Tick,P,actual.IsHostile);
                        var navigation=(NavigationSession)domain.GetType().GetProperty("Navigation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(domain);
                        var geometry=a.NavigationBinding.Geometry;
                        var observed=geometry.Obstacles.Where(o=>aiView.Vision.IsVisible(new NavPoint(o.MinX,o.MinZ))&&aiView.Vision.IsVisible(new NavPoint(o.MaxX,o.MinZ))&&aiView.Vision.IsVisible(new NavPoint(o.MinX,o.MaxZ))&&aiView.Vision.IsVisible(new NavPoint(o.MaxX,o.MaxZ))).ToArray();
                        var safe=new NavGeometry(geometry.HalfExtent,observed,1);
                        var rejected=new System.Collections.Generic.List<object>();
                        foreach(var request in requests.Where(r=>r.Kind==PlayableRouteTargetKind.PublicObjective)){
                            var unit=navigation.Crowd.Units.Single(n=>n.Id==request.UnitId);
                            var path=new SharedFlowRouter(safe,P.Navigation.ForUnit(unit.Radius,unit.Speed,unit.TurnSpeed)).FindPath(request.Origin,request.Target);
                            var segments=new System.Collections.Generic.List<object>();var from=request.Origin;
                            foreach(var point in path){
                                double length=Math.Sqrt(Math.Pow(point.X-from.X,2)+Math.Pow(point.Z-from.Z,2));
                                int count=Math.Max(1,(int)Math.Ceiling(length/(P.VisionCellSize/2)));
                                var hidden=Enumerable.Range(0,count+1).Select(k=>new NavPoint(from.X+(point.X-from.X)*k/count,from.Z+(point.Z-from.Z)*k/count)).Where(pt=>!aiView.Vision.IsVisible(pt)).ToArray();
                                segments.Add(new{From=from,To=point,AuthorityFree=geometry.SegmentFree(from,point,unit.Radius),ObservedFree=safe.SegmentFree(from,point,unit.Radius),Invisible=hidden});from=point;
                            }
                            rejected.Add(new{request.UnitId,request.TargetId,request.Radius,ActualRadius=unit.Radius,OriginVisible=aiView.Vision.IsVisible(request.Origin),TargetVisible=aiView.Vision.IsVisible(request.Target),OriginFree=safe.IsFree(request.Origin,unit.Radius),TargetFree=safe.IsFree(request.Target,unit.Radius),Path=path,Segments=segments});
                        }
                        CollectionAssert.AreEqual(before,a.CaptureBytes(),"Diagnostic AI projection and proposal must remain read-only authority queries.");
                        timeline.Add(new{a.Tick,defense.Active,planner.PendingId,cp.ArmyMission,Proposal=proposal,
                            Own=view.Entities.Where(e=>e.Owner==view.Owner).Select(e=>new{e.Id,e.Kind,e.Health,e.Position,e.NavigationOutcome,e.CurrentOrder,Army=registry.ArmyFor(e.Id)}).ToArray(),
                            Buildings=view.Buildings.Where(b=>b.Owner==view.Owner||view.Vision.IsVisible(b.Position)).Select(b=>new{b.Id,b.Owner,b.Kind,b.Health,b.Phase,b.Position}).ToArray(),
                            actual.PublicScoutObjectives,Proofs=actual.RouteProofs.Select(p=>new{p.UnitId,p.Kind,p.TargetId,p.Origin,p.Goal}).ToArray(),
                            Requests=requests,RouteRejections=rejected,VisionSources=aiView.Vision.Sources,ObservedSolids=observed,
                            Ground=aiView.Entities.Where(e=>e.Owner==aiView.Owner).Select(e=>new{e.Id,e.Kind,Weight=AiRosterCatalog.Initial.For(e.Kind).lineWeight,Radius=PlayableUnitRules.Radius(P,e.Kind),e.Health}).ToArray(),cp.Armies,cp.Knowledge,cp.EconomyDecision});
                    }
                }
                TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Diagnostic="same original1500tick scenario; single world isolates host port-switch; not qualification",a.Tick,clear,defended,macro,recovered,Timeline=timeline}));
                Assert.True(defended);Assert.True(macro);Assert.GreaterOrEqual(clear,0);
            }
            a.Stop();
        }
    }
}
