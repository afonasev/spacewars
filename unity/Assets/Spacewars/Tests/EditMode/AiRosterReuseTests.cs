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
        [Test] public void ScalarQueriesPreserveCustomValuesAndDetachedOwnership()
        {
            var data=AiRosterCatalog.Initial.CopyData();
            foreach(var row in data){row.lineWeight=.25;row.supportWeight=.5;row.reconWeight=.75;}
            var custom=new AiRosterCatalog(data);
            foreach(var catalog in new[]{AiRosterCatalog.Initial,custom})foreach(PlayableEntityKind kind in Enum.GetValues(typeof(PlayableEntityKind))){
                var descriptor=catalog.For(kind);
                Assert.AreEqual(descriptor.lineWeight,catalog.LineWeight(kind));
                Assert.AreEqual(descriptor.supportWeight,catalog.SupportWeight(kind));
                Assert.AreEqual(descriptor.reconWeight,catalog.ReconWeight(kind));
                foreach(var tag in new[]{"air","ground","building","absent",null})Assert.AreEqual(descriptor.targetTags.Contains(tag),catalog.Targets(kind,tag));
                descriptor.lineWeight=99;descriptor.targetTags[0]="mutated";
                Assert.AreNotEqual(99,catalog.LineWeight(kind));Assert.False(catalog.Targets(kind,"mutated"));
            }
            Assert.Throws<InvalidOperationException>(()=>custom.For((PlayableEntityKind)999));
            Assert.Throws<InvalidOperationException>(()=>custom.LineWeight((PlayableEntityKind)999));
        }
        [Test] public void MeasureScalarArmyQueriesAgainstDetachedDescriptors()
        {
            const int repeats=1000,count=400;
            double Run(bool scalar,out double sum){
                sum=0;var watch=Stopwatch.StartNew();
                for(int repeat=0;repeat<repeats;repeat++)for(int row=0;row<count;row++){
                    var kind=(PlayableEntityKind)(row%4);var catalog=AiRosterCatalog.Initial;
                    sum+=scalar?catalog.LineWeight(kind)+catalog.SupportWeight(kind)+(catalog.Targets(kind,"ground")?1:0):
                        catalog.For(kind).lineWeight+catalog.For(kind).supportWeight+(catalog.For(kind).targetTags.Contains("ground")?1:0);
                }
                watch.Stop();return watch.Elapsed.TotalMilliseconds;
            }
            Run(false,out _);Run(true,out _);
            double a=Run(false,out double first),b=Run(true,out double candidate),c=Run(false,out double last);
            Assert.AreEqual(first,candidate);Assert.AreEqual(first,last);
            TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,"ROSTER_SCALAR_ABA count={0} repeats={1} original_ms={2:R} scalar_ms={3:R} original2_ms={4:R} sum={5:R}",count,repeats,a,b,c,candidate));
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
