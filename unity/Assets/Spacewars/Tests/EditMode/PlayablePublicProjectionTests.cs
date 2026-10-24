using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayablePublicProjectionTests
    {
        static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static T Bind<T>(object domain,string name) where T:class => Delegate.CreateDelegate(typeof(T),domain,D.GetMethod(name,F)) as T;
        static void Same(AiIntelEnvelope[] a,AiIntelEnvelope[] b)
        {
            CollectionAssert.AreEqual(a.Select(x=>x.AreaId),b.Select(x=>x.AreaId));
            for(int i=0;i<a.Length;i++)CollectionAssert.AreEqual(a[i].Placement,b[i].Placement);
        }
        static void Same(PlayablePublicScoutObjective[] a,PlayablePublicScoutObjective[] b)
        {
            CollectionAssert.AreEqual(a.Select(x=>Tuple.Create(x.SiteId,x.Approach,x.Role,x.Reachable)),b.Select(x=>Tuple.Create(x.SiteId,x.Approach,x.Role,x.Reachable)));
        }
        [Test] public void PublicDataMatchesColdRestoreAndDoesNotChangeCapture()
        {
            var config=OfflineParticipantAuthorityTests.Config(6);var authority=new OfflineParticipantAuthority(config,71);
            var domain=OfflineParticipantAuthorityTests.Domain(authority);
            var objectives=Bind<Func<PlayableOwner,PlayablePublicScoutObjective[]>>(domain,"PublicObjectives");
            var envelopes=Bind<Func<AiIntelEnvelope[]>>(domain,"PublicIntelEnvelopes");
            var before=authority.CaptureBytes();var first=envelopes();
            for(int i=0;i<6;i++){var own=objectives((PlayableOwner)i);Same(own,objectives((PlayableOwner)i));}
            Same(first,envelopes());CollectionAssert.AreEqual(before,authority.CaptureBytes());
            var cold=OfflineParticipantAuthority.Restore(before,config);var coldDomain=OfflineParticipantAuthorityTests.Domain(cold);
            Same(first,Bind<Func<AiIntelEnvelope[]>>(coldDomain,"PublicIntelEnvelopes")());
            var coldObjectives=Bind<Func<PlayableOwner,PlayablePublicScoutObjective[]>>(coldDomain,"PublicObjectives");
            for(int i=0;i<6;i++)Same(objectives((PlayableOwner)i),coldObjectives((PlayableOwner)i));
            CollectionAssert.AreEqual(before,cold.CaptureBytes());
        }
        [Test] public void PublicDataTracksBalanceAndPublishedCollectionsStayReadOnly()
        {
            var p=PlayableProfile.Default;var domain=Activator.CreateInstance(D,F,null,new object[]{p,1L,false},null);
            var envelopes=Bind<Func<AiIntelEnvelope[]>>(domain,"PublicIntelEnvelopes");var initial=envelopes();
            var objectives=Bind<Func<PlayableOwner,PlayablePublicScoutObjective[]>>(domain,"PublicObjectives");var initialObjectives=objectives(PlayableOwner.Player);var initialObjectiveValues=initialObjectives.Select(x=>Tuple.Create(x.SiteId,x.Approach,x.Role,x.Reachable)).ToArray();var initialPlacement=initial[0].Placement.ToArray();
            var data=p.CopyData();data.revision++;data.outpostCaptureRadius+=0.1d;var next=PlayableProfile.Create(data);
            Assert.Null(D.GetMethod("ValidateBalance",F).Invoke(domain,new object[]{next}));
            D.GetMethod("ApplyBalance",F).Invoke(domain,new object[]{next});
            var current=envelopes();Same(initial,current);
            Assert.AreNotEqual(initialObjectives.Single(x=>x.Role==PlayablePublicScoutObjectiveRole.ExpansionOutpost).Approach,objectives(PlayableOwner.Player).Single(x=>x.Role==PlayablePublicScoutObjectiveRole.ExpansionOutpost).Approach);
            var bytes=(byte[])D.GetMethod("CaptureWorldBytes",F).Invoke(domain,new object[]{7,"public-projection"});
            var cold=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,next,7,"public-projection"});
            Same(current,Bind<Func<AiIntelEnvelope[]>>(cold,"PublicIntelEnvelopes")());
            Same(objectives(PlayableOwner.Player),Bind<Func<PlayableOwner,PlayablePublicScoutObjective[]>>(cold,"PublicObjectives")(PlayableOwner.Player));
            var view=(PlayableSnapshot)D.GetMethod("PlayerSnapshot",F).Invoke(domain,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player});
            Assert.Throws<NotSupportedException>(()=>((System.Collections.Generic.IList<AiIntelEnvelope>)view.IntelEnvelopes)[0]=null);
            Assert.Throws<NotSupportedException>(()=>((System.Collections.Generic.IList<VisionSource>)view.IntelEnvelopes[0].Placement)[0]=default);
            Assert.Throws<NotSupportedException>(()=>((System.Collections.Generic.IList<PlayablePublicScoutObjective>)view.PublicScoutObjectives)[0]=null);
            CollectionAssert.AreEqual(initialPlacement,initial[0].Placement);
            CollectionAssert.AreEqual(initialObjectiveValues,initialObjectives.Select(x=>Tuple.Create(x.SiteId,x.Approach,x.Role,x.Reachable)));
        }
        [Test] public void ReportPublicProjectionCostWithoutThreshold()
        {
            var config=OfflineParticipantAuthorityTests.Config(6);var authority=new OfflineParticipantAuthority(config,71);
            var domain=OfflineParticipantAuthorityTests.Domain(authority);
            var objectives=Bind<Func<PlayableOwner,PlayablePublicScoutObjective[]>>(domain,"PublicObjectives");
            var envelopes=Bind<Func<AiIntelEnvelope[]>>(domain,"PublicIntelEnvelopes");
            int count=0;var watch=System.Diagnostics.Stopwatch.StartNew();
            for(int i=0;i<10000;i++)count+=objectives((PlayableOwner)(i%6)).Length;
            watch.Stop();Assert.Greater(count,0);TestContext.WriteLine("PUBLIC_OBJECTIVES_10K wall_ms="+watch.Elapsed.TotalMilliseconds);
            count=0;watch.Restart();for(int i=0;i<10000;i++)count+=envelopes().Length;
            watch.Stop();Assert.Greater(count,0);TestContext.WriteLine("PUBLIC_ENVELOPES_10K wall_ms="+watch.Elapsed.TotalMilliseconds);
        }
    }
}
