using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class MossRouteProgressTests
    {
        private static OfflineMatchConfiguration Configuration()
        {
            var map=new MossHighlandMap();
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var roster=Enumerable.Range(0,4).Select(i=>new OfflineParticipant(i==0?"player-1":"foundry-"+(i+1),i+1,i+1,OfflineControl.Ai)).ToArray();
            return map.Configuration(profile,1564176781,roster);
        }

        [Test] public void MossAdmissionPublishesItsSupportedAuthoredProvider()
        {
            var config=Configuration();var authority=new PlayableAuthorityTick(config,1,asynchronousRoutes:true);
            try{
                Assert.That(authority.NavigationBinding.Admission.AuthoredTerrain,Is.SameAs(config.Profile.AuthoredMap),
                    "The actual host selects certified routing from this admission; a missing map silently strands arrival commands.");
            }finally{authority.Stop();authority.Domain.Navigation.DestroyNavigationAuthority();}
        }

        [Test,Timeout(180000)] public void FourFightersMoveTheirInitialScoutsThroughTheActualHost()
        {
            var config=Configuration();var authority=new PlayableAuthorityTick(config,1,asynchronousRoutes:true);
            var initial=config.Roster.Select(p=>authority.ParticipantView(p.Id)).Select(v=>v.Entities.Single(e=>e.Owner==v.Owner&&e.Kind==PlayableEntityKind.Explorer)).ToArray();
            int receipts=0;authority.Domain.Navigation.RouteActivated+=_=>receipts++;
            try{using(var host=new UnityHostRouteService()){
                for(int tick=0;tick<360;tick++){
                    try{
                    host.Service(authority,64);
                    Assert.That(authority.TryAdvance(),Is.True,"async clocks at "+tick);
                    Assert.That(authority.Latest.Failure,Is.Null);
                    Assert.That(authority.Domain.Navigation.PhysicalHostBudget.RetainedBytes,Is.LessThanOrEqualTo(LayeredNavigationScheduler.MaxRetainedBytes));
                    if(authority.Tick%30==0){
                        var nav=authority.Domain.Navigation;
                        TestContext.WriteLine("MOSS_PROGRESS tick="+authority.Tick+" receipts="+receipts+" allocating="+nav.AllocationCount+" pending="+nav.PendingCount+" projection="+host.LastProjectionFailure+" bytes="+nav.PhysicalHostBudget.RetainedBytes+" phases="+string.Join(",",nav.ArrivalPhases));
                        var root=nav.PinCommittedHostRoot();try{if(root!=null)foreach(var row in root.ArrivalWork){var work=(NavigationArrivalWork)row.Value;var counts=work.RouteCounters;TestContext.WriteLine("MOSS_WORK id="+row.Key+" status="+work.Status+" phase="+work.WorkPhase+" graph="+counts.LogicalGraphWork+" field="+counts.LogicalFieldWork+" connector="+counts.ConnectorWork+" retained="+work.StructuralRetainedBytes+" blocked="+work.LastAdvanceRetentionBlocked);}}finally{root?.Release();}
                    }
                    }catch(Exception error){TestContext.WriteLine("MOSS_PRIMARY_FAILURE tick="+tick+" "+error);throw;}
                }
                foreach(var scout in initial){
                    var view=authority.ParticipantView(config.Roster[(int)scout.Owner].Id);
                    var current=view.Entities.Single(e=>e.Id==scout.Id);
                    Assert.That(current.Position,Is.Not.EqualTo(scout.Position),"actual scout movement for "+scout.Owner);
                }
                Assert.That(receipts,Is.GreaterThanOrEqualTo(initial.Length));
            }}catch(Exception error){TestContext.WriteLine("MOSS_BODY_OR_HOST_CLEANUP_FAILURE "+error);throw;}
            finally{authority.Stop();authority.Domain.Navigation.DestroyNavigationAuthority();}
        }
    }
}
