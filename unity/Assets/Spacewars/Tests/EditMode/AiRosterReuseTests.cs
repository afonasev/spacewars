using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class AiRosterReuseTests
    {
        [Test] public void InitialCatalogNeverExposesDescriptorOrArrayStorage()
        {
            var initial=AiRosterCatalog.Initial;string hash=initial.Hash;
            var d=initial.For(PlayableEntityKind.Tank);d.lineWeight=0;d.targetTags[0]="mutated";d.unlockDependencies=new[]{PlayableResearchKind.TankChassis};
            var data=initial.CopyData();data[0].targetTags[0]="also mutated";data[0].reconWeight=.25;
            var next=AiRosterCatalog.Initial;Assert.AreEqual(hash,next.Hash);Assert.AreEqual(1,next.For(PlayableEntityKind.Tank).lineWeight);
            CollectionAssert.AreEqual(new[]{"ground","building"},next.For(PlayableEntityKind.Tank).targetTags);
            Assert.IsEmpty(next.For(PlayableEntityKind.Tank).unlockDependencies);Assert.AreEqual(1,next.For(PlayableEntityKind.Explorer).reconWeight);
            Assert.AreEqual(hash,new AiRosterCatalog(next.CopyData().Reverse()).Hash);
        }
        [Test] public void ConcurrentConsumersAndCustomCatalogRemainIndependent()
        {
            string hash=AiRosterCatalog.Initial.Hash;Exception failure=null;
            var worker=new Thread(()=>{try{for(int i=0;i<100;i++){var row=AiRosterCatalog.Initial.For(PlayableEntityKind.Shkval);row.targetTags[0]="private";row.supportWeight=0;}}catch(Exception ex){failure=ex;}});
            worker.Start();for(int i=0;i<100;i++){var data=AiRosterCatalog.Initial.CopyData();data.Single(d=>d.kind==PlayableEntityKind.Tank).lineWeight=.5;var custom=new AiRosterCatalog(data);Assert.AreNotEqual(hash,custom.Hash);Assert.AreEqual(.5,custom.For(PlayableEntityKind.Tank).lineWeight);}
            Assert.True(worker.Join(5000));Assert.Null(failure);Assert.AreEqual(1,AiRosterCatalog.Initial.For(PlayableEntityKind.Tank).lineWeight);Assert.AreEqual(1,AiRosterCatalog.Initial.For(PlayableEntityKind.Shkval).supportWeight);
        }
        [Test] public void MeasureRepeatedInitialLookupAgainstValidatedConstruction()
        {
            var data=AiRosterCatalog.Initial.CopyData();double expected=0,actual=0;const int count=1000;
            // Warm both paths. Constructor baseline remains available after the repair.
            for(int i=0;i<10;i++){new AiRosterCatalog(data).For(PlayableEntityKind.Tank);AiRosterCatalog.Initial.For(PlayableEntityKind.Tank);}
            var sw=Stopwatch.StartNew();for(int i=0;i<count;i++)expected+=new AiRosterCatalog(data).For(PlayableEntityKind.Tank).lineWeight;sw.Stop();double control=sw.Elapsed.TotalMilliseconds;
            sw.Restart();for(int i=0;i<count;i++)actual+=AiRosterCatalog.Initial.For(PlayableEntityKind.Tank).lineWeight;sw.Stop();
            Assert.AreEqual(expected,actual);Assert.AreEqual(count,actual);
            TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,"ROSTER_LOOKUP count={0} constructed_ms={1:F6} initial_ms={2:F6}",count,control,sw.Elapsed.TotalMilliseconds));
        }
    }
}
