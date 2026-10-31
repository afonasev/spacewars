using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        [Test] public void LifecycleBoundAndOriginalGroupFilteringKeepLossExplicit()
        {
            string path=Path.GetTempFileName();try{
                QaArrivalLifecycle.ResetForTests(10);for(int i=1;i<=8;i++)QaArrivalLifecycle.Register(1,i);
                var t=QaArrivalLifecycle.BeginBirth(1,7,1,9,Array.Empty<NavigationRequest>());QaArrivalLifecycle.FinishBirth(t,null,false,"not-installed");
                var foreign=QaArrivalLifecycle.BeginBirth(1,8,999,10,Array.Empty<NavigationRequest>());QaArrivalLifecycle.FinishBirth(foreign,null,false,"not-installed");
                Assert.AreEqual(9,QaArrivalLifecycle.RowsForTests().Length);Assert.Zero(QaArrivalLifecycle.WriteCsv(path));StringAssert.Contains("filtered=1",File.ReadAllText(path));
                QaArrivalLifecycle.FinishBirth(t,null,false,"scope-refused");QaArrivalLifecycle.FinishBirth(t,null,false,"scope-refused");Assert.Greater(QaArrivalLifecycle.WriteCsv(path),0);StringAssert.Contains("dropped=1",File.ReadAllText(path));Assert.AreEqual(10,QaArrivalLifecycle.RowsForTests().Length);
            }finally{QaArrivalLifecycle.ResetForTests();File.Delete(path);}
        }
        [Test] public void LifecycleSnapshotsObserveRealDiscardedForkWithoutChangingOwnedBytes()
        {
            QaArrivalLifecycle.ResetForTests(128);using(var fixture=new OwnedArrivalFixture(true)){
                NavigationArrivalWork work=null,proposed=null;try{
                    work=fixture.NewWork();var m=work.PhysicalInput.Members[0];QaArrivalLifecycle.Register(m.WorldGeneration,m.CommandSequence);
                    var bytes=CaptureGraph(fixture.Budget,work);var t=QaArrivalLifecycle.BeginAdvance(m.WorldGeneration,9,1,2,16,work);
                    proposed=Fork(work,fixture.Budget,90,w=>w.Advance(Math.Max(0,w.LogicalEpoch+1),16,new NavEpochQuota(64,64,64,64,64)));
                    long physical=fixture.Budget.PhysicalBytes;QaArrivalLifecycle.FinishAdvance(t,proposed,"source-owner-refused");
                    var row=QaArrivalLifecycle.RowsForTests().Last();Assert.False(row.Committed);Assert.True(row.Known);Assert.AreEqual("source-owner-refused",row.Reason);Assert.AreEqual(work.AllocationWorkConsumed,row.Id.Before.Total);Assert.AreEqual(proposed.AllocationWorkConsumed,row.Proposed.Total);
                    QaArrivalLifecycle.FinishAdvance(t,proposed,"exception");string unknown=Path.GetTempFileName();try{QaArrivalLifecycle.WriteCsv(unknown);StringAssert.Contains(",-1,exception,",File.ReadAllText(unknown));}finally{File.Delete(unknown);}
                    CollectionAssert.AreEqual(bytes,CaptureGraph(fixture.Budget,work));Assert.AreEqual(physical,fixture.Budget.PhysicalBytes);Assert.Zero(fixture.Budget.HeldBytes);
                    QaArrivalLifecycle.ResetForTests();Assert.False(QaArrivalLifecycle.Enabled);var disabled=QaArrivalLifecycle.BeginAdvance(m.WorldGeneration,9,1,2,16,work);Assert.IsNull(disabled.Owner);CollectionAssert.AreEqual(bytes,CaptureGraph(fixture.Budget,work));
                }finally{proposed?.ReleaseOwner();work?.ReleaseOwner();QaArrivalLifecycle.ResetForTests();}
            }
        }
    }
    public sealed class QaArrivalLifecycleSessionTests
    {
        [TestCase(false)][TestCase(true)][Timeout(30000)] public void ActualPhysicalBirthAndServicePublishExactRequestBindings(bool cancelBirth)
        {
            const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);object domain=null;CertifiedRouteKernel host=null;string path=Path.GetTempFileName();
            try{
                domain=Activator.CreateInstance(type,f,null,new object[]{PlayableProfile.Default,1L,false},null);var nav=(NavigationSession)type.GetProperty("Navigation",f).GetValue(domain);host=new CertifiedRouteKernel();
                int actor=(int)type.GetMethod("SpawnUnit",f).Invoke(domain,new object[]{new NavPoint(-18,-14),PlayableOwner.Player,PlayableEntityKind.Tank});
                QaArrivalLifecycle.ResetForTests(8192);for(int i=1;i<=8;i++)QaArrivalLifecycle.Register(1,i);
                var command=new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,new[]{actor},new NavPoint(-10,-14));Assert.AreEqual(PlayableCommandStatus.Applied,type.GetMethod("Apply",f).Invoke(domain,new object[]{command,null}));
                if(cancelBirth)nav.BeforeArrivalProviderConstruction=()=>{nav.BeforeArrivalProviderConstruction=null;nav.Stop(actor,false);};
                for(int i=0;i<64&&!QaArrivalLifecycle.RowsForTests().Any(r=>r.Kind==(cancelBirth?"birth":"advance")&&r.Committed!=cancelBirth);i++)host.Pump(new PlayableRouteBinding((NavGeometry)type.GetProperty("Geometry",f).GetValue(domain),PlayableProfile.Default,nav.Admission,nav.RoutePort),4);
                var rows=QaArrivalLifecycle.RowsForTests();Assert.True(rows.Any(r=>r.Kind=="birth"&&r.Committed!=cancelBirth));Assert.True(rows.Any(r=>r.Kind=="binding"&&r.Entity==actor&&r.Request>0&&r.Incarnation>0&&r.Id.Sequence==1));if(cancelBirth)Assert.True(rows.Any(r=>r.Kind=="birth"&&!r.Committed&&r.Reason=="authority-observation-changed"));else Assert.True(rows.Any(r=>r.Kind=="advance"&&r.Committed&&r.Reason=="committed"&&r.Known));Assert.Zero(QaArrivalLifecycle.WriteCsv(path));
                var columns=File.ReadAllLines(path);Assert.True(columns[1].Split(',').Length==columns[0].Split(',').Length);TestContext.WriteLine(columns.Last());
                var output=Environment.GetEnvironmentVariable("SPACEWARS_LIFECYCLE_OUTPUT");if(output!=null){Directory.CreateDirectory(output);File.Copy(path,Path.Combine(output,"birth-cancel-"+cancelBirth+".csv"),true);}
            }finally{host?.Clear();if(domain!=null)type.GetMethod("DestroyOwnedNavigation",f).Invoke(domain,null);QaArrivalLifecycle.ResetForTests();File.Delete(path);}
        }
    }
}
