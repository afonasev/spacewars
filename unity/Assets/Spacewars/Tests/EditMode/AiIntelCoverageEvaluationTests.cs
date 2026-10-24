using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class AiIntelCoverageEvaluationTests
    {
        private static PlayableSnapshot Snapshot(TeamVisionSnapshot vision, AiIntelEnvelope[] envelopes, long tick=17)
            =>new PlayableSnapshot("coverage",1,71,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,
                new NavGeometry(128,Array.Empty<NavObstacle>(),1),Array.Empty<PlayableEntitySnapshot>(),
                Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),
                new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision,intelEnvelopes:envelopes);

        [Test] public void DelayedCoverageRetainsOriginalVisionAndEnvelopeAfterWorldRefresh()
        {
            var placements=new[]{new VisionSource(default(NavPoint),4)};
            var envelopes=new[]{new AiIntelEnvelope(12,placements)};
            var vision=new PlayableVision(0,128,128,1,1);
            vision.Refresh(new[]{new VisionSource(new NavPoint(-3,0),5.5),new VisionSource(new NavPoint(3,0),5.5)},Array.Empty<KnownBuilding>());
            var original=vision.Snapshot();
            var observation=PlayableAiObservation.From(Snapshot(original,envelopes));
            placements[0]=new VisionSource(new NavPoint(80,0),2);
            envelopes[0]=new AiIntelEnvelope(99,placements);
            vision.Refresh(Array.Empty<VisionSource>(),Array.Empty<KnownBuilding>());
            var area=observation.Intel.Areas.Single();
            Assert.AreEqual(12,area.Envelope.AreaId);
            Assert.AreEqual(17,observation.Intel.Tick);
            Assert.True(area.Full);
            Assert.AreEqual(area.Envelope.FullyCovered(original),area.Full);
            Assert.False(area.Envelope.FullyCovered(vision.Snapshot()));
            Assert.True(area.Full,"Later world changes must not alter an earlier observation.");
        }

        [Test] public void DelayedCoveragePreservesHolesNullVisionAndCanonicalAreaOrder()
        {
            var area=new AiIntelEnvelope(12,new[]{new VisionSource(default(NavPoint),4)});
            var empty=new AiIntelEnvelope(1,Array.Empty<VisionSource>());
            var vision=new PlayableVision(0,128,128,1,1);
            vision.Refresh(new[]{new VisionSource(new NavPoint(-3,0),3),new VisionSource(new NavPoint(3,0),3)},Array.Empty<KnownBuilding>());
            var observation=PlayableAiObservation.From(Snapshot(vision.Snapshot(),new[]{area,empty}));
            CollectionAssert.AreEqual(new[]{1,12},observation.Intel.Areas.Select(a=>a.Envelope.AreaId));
            Assert.True(observation.Intel.Areas.All(a=>!a.Full));
            Assert.False(new AiIntelDelta(18,new[]{area},null).Areas.Single().Full);
            Assert.True(new AiAreaCoverage(area,true).Full);
            Assert.False(new AiAreaCoverage(area,false).Full);
        }

        // Local synthetic cost, not a match-throughput assertion. Identical source
        // runs on the baseline and candidate; no wall-clock threshold hides failures.
        [Test] public void CoverageConstructionAndConsumptionTiming()
        {
            var centers=Enumerable.Range(0,24).Select(i=>new NavPoint((i%6-3)*24,(i/6-2)*24)).ToArray();
            var areas=centers.Select((p,i)=>new AiIntelEnvelope(i+1,new[]{new VisionSource(p,4)})).ToArray();
            var sources=centers.SelectMany(p=>new[]{new VisionSource(new NavPoint(p.X-3,p.Z),5.05),new VisionSource(new NavPoint(p.X+3,p.Z),5.05)}).ToArray();
            var vision=new PlayableVision(0,128,128,1,1);vision.Refresh(sources,Array.Empty<KnownBuilding>());
            var snapshot=Snapshot(vision.Snapshot(),areas);
            Assert.True(PlayableAiObservation.From(snapshot).Intel.Areas.All(a=>a.Full));
            const int iterations=64;
            var constructed=new PlayableAiObservation[iterations];
            var watch=Stopwatch.StartNew();
            for(int i=0;i<iterations;i++)constructed[i]=PlayableAiObservation.From(snapshot);
            watch.Stop();double construction=watch.Elapsed.TotalMilliseconds;
            watch.Restart();int full=0;
            foreach(var o in constructed)foreach(var a in o.Intel.Areas)if(a.Full)full++;
            watch.Stop();double consumption=watch.Elapsed.TotalMilliseconds;
            Assert.AreEqual(iterations*areas.Length,full);
            TestContext.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "COVERAGE_COST iterations={0} areas={1} sources={2} construct_ms={3:F4} consume_ms={4:F4}",iterations,areas.Length,sources.Length,construction,consumption));
        }
    }
}
