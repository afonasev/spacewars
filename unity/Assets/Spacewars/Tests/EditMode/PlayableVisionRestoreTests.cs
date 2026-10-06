using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Runtime.Serialization;
using System.Text;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableVisionRestoreTests
    {
        private static readonly KnownBuilding[] Empty = Array.Empty<KnownBuilding>();
        private static PlayableVision Fresh(int team = 1, double width = 32, double depth = 20, double cell = 4, double feather = 1.5) =>
            new PlayableVision(team, width, depth, cell, feather);
        private static VisionSource Source(double x, double radius = 5) => new VisionSource(new NavPoint(x, 0), radius);
        private static KnownBuilding Building(int id = 7, double x = 10, bool upgraded = true) =>
            new KnownBuilding(id, 2, PlayableOwner.Enemy, PlayableBuildingKind.Refinery, new NavPoint(x, 0), .7, upgraded);
        private static PlayableVision Prepared()
        {
            var vision = Fresh();
            vision.Refresh(new[] { Source(10) }, new[] { Building() });
            vision.Refresh(new[] { Source(-20) }, Empty); // Unseen destruction must not clear memory.
            return vision;
        }
        private static string Json(PlayableVisionState state)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(PlayableVisionState)).WriteObject(stream, state);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
        private static PlayableVisionState FromJson(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (PlayableVisionState)new DataContractJsonSerializer(typeof(PlayableVisionState)).ReadObject(stream);
        }
        private static PlayableVisionState RoundTrip(PlayableVisionState state) => FromJson(Json(state));
        private static void Equal(PlayableVision continuous, PlayableVision restored)
        {
            Assert.AreEqual(Json(continuous.CaptureState()), Json(restored.CaptureState()));
            var a = continuous.Snapshot(); var b = restored.Snapshot();
            Assert.AreEqual(a.Revision, b.Revision); Assert.AreEqual(a.FogRevision, b.FogRevision);
            CollectionAssert.AreEqual(a.Coverage, b.Coverage);
            CollectionAssert.AreEqual(a.DiscoveredCells, b.DiscoveredCells);
            foreach (var point in new[] { new NavPoint(-20, 0), new NavPoint(10, 0), new NavPoint(0, 20), new NavPoint(33, 0) })
            {
                Assert.AreEqual(continuous.IsVisible(point), restored.IsVisible(point));
                Assert.AreEqual(continuous.IsDiscovered(point), restored.IsDiscovered(point));
            }
        }

        [Test] public void SerializedFreshRestoreMatchesContinuousSuffixWithoutGenesisReplay()
        {
            var continuous = Prepared(); var restored = Fresh();
            restored.RestoreState(RoundTrip(continuous.CaptureState())); Equal(continuous, restored);
            Assert.False(restored.IsVisible(new NavPoint(10, 0)));
            Assert.True(restored.IsDiscovered(new NavPoint(10, 0)));
            Assert.True(restored.Snapshot().KnownBuildings.Single().RefineryUpgraded);
            var snapshot = restored.Snapshot(); long updates = restored.CoverageUpdates;
            continuous.Refresh(new[] { Source(-20) }, Empty); restored.Refresh(new[] { Source(-20) }, Empty);
            Equal(continuous, restored); Assert.AreSame(snapshot, restored.Snapshot()); Assert.AreEqual(updates, restored.CoverageUpdates);
            continuous.Refresh(new[] { Source(0) }, new[] { Building(8) }); restored.Refresh(new[] { Source(0) }, new[] { Building(8) }); Equal(continuous, restored);
            continuous.Refresh(new[] { Source(10) }, Empty); restored.Refresh(new[] { Source(10) }, Empty);
            Equal(continuous, restored); Assert.IsEmpty(restored.Snapshot().KnownBuildings);
            Assert.AreEqual(1, snapshot.KnownBuildings.Count); // Previously published memory stays frozen.
        }

        [Test] public void CaptureAndRestoreDoNotAliasMutableStateOrPublishedSnapshots()
        {
            var source = Prepared(); string original = Json(source.CaptureState());
            var state = source.CaptureState(); var restored = Fresh(); restored.RestoreState(state);
            var before = restored.Snapshot(); byte[] coverage = before.Coverage.ToArray();
            state.Sources[0].X = 0; state.KnownBuildings[0].X = -30; state.DiscoveredCells[0] = long.MaxValue; state.Coverage[0] ^= 255;
            Assert.AreEqual(original, Json(source.CaptureState()));
            Assert.AreEqual(original, Json(restored.CaptureState()));
            restored.Refresh(new[] { Source(10) }, Empty);
            CollectionAssert.AreEqual(coverage, before.Coverage); Assert.AreEqual(10, before.KnownBuildings.Single().Position.X);
            Assert.AreEqual(original, Json(source.CaptureState()));
        }

        [Test] public void TeamsRestoreIndependentKnowledgeAndCannotLoadOpposingState()
        {
            var a = Prepared(); var b = Fresh(2); b.Refresh(new[] { Source(20, 2) }, Empty);
            var restoredA = Fresh(); var restoredB = Fresh(2);
            Assert.Throws<ArgumentException>(() => restoredB.RestoreState(a.CaptureState()));
            restoredA.RestoreState(RoundTrip(a.CaptureState())); restoredB.RestoreState(RoundTrip(b.CaptureState()));
            Equal(a, restoredA); Equal(b, restoredB);
            Assert.False(restoredB.IsDiscovered(new NavPoint(-20, 0)));
            Assert.False(restoredA.IsVisible(new NavPoint(20, 0)));
        }

        [Test] public void EmptyCheckpointRoundTripsAndNonFreshDestinationIsRejected()
        {
            var fresh = Fresh(); fresh.RestoreState(RoundTrip(Fresh().CaptureState())); Equal(Fresh(), fresh);
            var used = Prepared(); string before = Json(used.CaptureState());
            Assert.Throws<ArgumentException>(() => used.RestoreState(Fresh().CaptureState()));
            Assert.AreEqual(before, Json(used.CaptureState()));
        }

        [TestCase("version")][TestCase("team")][TestCase("raster")][TestCase("width")][TestCase("depth")]
        [TestCase("cell")][TestCase("feather")][TestCase("revision")][TestCase("updates")][TestCase("counterOrder")]
        [TestCase("sources")][TestCase("sourceNull")][TestCase("sourceNaN")][TestCase("sourceInfinity")][TestCase("radius")]
        [TestCase("cells")][TestCase("duplicateCell")][TestCase("outOfBoundsCell")]
        [TestCase("coverage")][TestCase("coverageLength")][TestCase("memory")][TestCase("memoryNull")]
        [TestCase("duplicateBuilding")][TestCase("buildingId")][TestCase("ownBuilding")][TestCase("owner")]
        [TestCase("kind")][TestCase("buildingNaN")][TestCase("headingInfinity")]
        public void InvalidCheckpointRejectsAtomicallyAndDestinationRemainsReusable(string corruption)
        {
            var state = Prepared().CaptureState();
            switch (corruption)
            {
                case "version": state.Version++; break; case "team": state.Team++; break; case "raster": state.RasterResolution++; break;
                case "width": state.HalfWidth++; break; case "depth": state.HalfDepth++; break; case "cell": state.CellSize++; break;
                case "feather": state.Feather++; break; case "revision": state.Revision = -1; break; case "updates": state.CoverageUpdates = -1; break;
                case "counterOrder": state.CoverageUpdates = state.Revision + 1; break;
                case "sources": state.Sources = null; break; case "sourceNull": state.Sources[0] = null; break;
                case "sourceNaN": state.Sources[0].X = double.NaN; break; case "sourceInfinity": state.Sources[0].Z = double.PositiveInfinity; break;
                case "radius": state.Sources[0].Radius = 0; break; case "cells": state.DiscoveredCells = null; break;
                case "duplicateCell": state.DiscoveredCells = new[] { state.DiscoveredCells[0], state.DiscoveredCells[0] }; break;
                case "outOfBoundsCell": state.DiscoveredCells = new[] { PlayableVision.CellKey(8, 0) }; break;
                case "coverage": state.Coverage = null; break; case "coverageLength": state.Coverage = new byte[1]; break;
                case "memory": state.KnownBuildings = null; break; case "memoryNull": state.KnownBuildings[0] = null; break;
                case "duplicateBuilding": state.KnownBuildings = new[] { state.KnownBuildings[0], state.KnownBuildings[0] }; break;
                case "buildingId": state.KnownBuildings[0].Id = 0; break; case "ownBuilding": state.KnownBuildings[0].Team = 1; break;
                case "owner": state.KnownBuildings[0].Owner = (PlayableOwner)99; break; case "kind": state.KnownBuildings[0].Kind = (PlayableBuildingKind)99; break;
                case "buildingNaN": state.KnownBuildings[0].Z = double.NaN; break; case "headingInfinity": state.KnownBuildings[0].Heading = double.PositiveInfinity; break;
                default: Assert.Fail("Unknown corruption"); break;
            }
            var destination = Fresh(); var snapshot = destination.Snapshot(); string before = Json(destination.CaptureState());
            Assert.Throws<ArgumentException>(() => destination.RestoreState(state));
            Assert.AreSame(snapshot, destination.Snapshot()); Assert.AreEqual(before, Json(destination.CaptureState()));
            destination.RestoreState(Prepared().CaptureState()); Equal(Prepared(), destination);
        }

        [Test] public void NullOrMissingFormatAndDifferentDestinationConfigurationAreRejected()
        {
            var destination = Fresh(); Assert.Throws<ArgumentException>(() => destination.RestoreState(null));
            Assert.Throws<SerializationException>(() => FromJson("{}"));
            Assert.Throws<ArgumentException>(() => destination.RestoreState(RoundTrip(new PlayableVisionState())));
            foreach (var other in new[] { Fresh(width: 33), Fresh(depth: 21), Fresh(cell: 5), Fresh(feather: 2) })
                Assert.Throws<ArgumentException>(() => other.RestoreState(Prepared().CaptureState()));
            destination.RestoreState(Prepared().CaptureState()); Equal(Prepared(), destination);
        }
    }
}
