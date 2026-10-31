using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using UnityEngine.TestTools;

namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        [UnityTest] public IEnumerator OpeningHiddenPrivateMapPublishesOwnFogBeforeReturning()
        {
            yield return StartOrdinary(2);
            var seat=Seats[1];var terrain=Get<PlayableMapTerrain>(seat,"mapTerrain");var fog=Get<PlayableWorld>(seat,"world").Fog;
            var otherTerrain=Get<PlayableMapTerrain>(Seats[0],"mapTerrain");long before=terrain.Uploads,otherBefore=otherTerrain.Uploads;
            Call(seat,"UpdateMaps");Assert.AreEqual(before,terrain.Uploads,"Closed private map must not perform hidden uploads.");
            var own=fog.Texture.GetPixels32().Select(p=>p.r).ToArray();
            var other=Get<PlayableWorld>(Seats[0],"world").Fog.Texture.GetPixels32().Select(p=>p.r).ToArray();
            Assert.False(own.SequenceEqual(other),"Opposing seats provide different fog masks for this regression.");
            Call(seat,"ToggleMap"); // Deliberately no UpdateFrame or yield before checking the first visible state.
            Assert.Greater(terrain.Uploads,before,"Opening must synchronously publish the previously hidden texture.");
            var painted=(byte[])typeof(PlayableMapTerrain).GetField("paintedOpacity",F).GetValue(terrain);
            CollectionAssert.AreEqual(own,painted,"First tactical frame uses this owner's fog, never the shared union.");
            Assert.AreEqual(otherBefore,otherTerrain.Uploads,"Opening one map must not refresh another hidden seat.");
            var map=Get<PlayableMapSurface>(seat,"tacticalMap");
            Assert.NotNull(typeof(PlayableMapSurface).GetField("marks",F).GetValue(map),"Markers must exist before the overlay is exposed.");
            Runtime.RequestStop();yield return Wait(()=>Runtime.IsStopped,"stop");
        }
    }
}
