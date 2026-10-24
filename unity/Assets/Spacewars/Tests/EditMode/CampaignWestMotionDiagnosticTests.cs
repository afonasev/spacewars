using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class CampaignWestMotionDiagnosticTests
    {
        [Test] public void ReadActualWestScoutFailureCheckpoint()
        {
            string source=Environment.GetEnvironmentVariable("CAMPAIGN_WEST_BODY"),folder=Environment.GetEnvironmentVariable("CAMPAIGN_WEST_OUTPUT");Assert.IsNotEmpty(source);Assert.IsNotEmpty(folder);Directory.CreateDirectory(folder);var bytes=File.ReadAllBytes(source);
            using(var h=System.Security.Cryptography.SHA256.Create())Assert.AreEqual("9df50d2c599235ec930c457fe3a52910f2fa58546de49a5a753035a03b67b62e",BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant());
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap());var config=CampaignReconOpening.Configuration(p,false);var a=PlayableAuthorityTick.RestoreBytes(bytes,config);
            try{
                var flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;var domain=typeof(PlayableAuthorityTick).GetField("domain",flags).GetValue(a);var session=(NavigationSession)domain.GetType().GetProperty("Navigation",flags).GetValue(domain);var state=session.Crowd.CaptureState();
                Assert.That(a.Tick,Is.InRange(6136L,6145L));Assert.True(a.Latest.Mission.WestCleared==false);Assert.AreEqual(4,state.Units.Count(u=>u.Id>=10&&u.Id<=13));
                File.WriteAllText(Path.Combine(folder,"actual-crowd.json"),JsonConvert.SerializeObject(state,Formatting.Indented));
                var slots=session.GetType().GetField("groups",flags).GetValue(session);var groups=slots.GetType().GetProperty("Values",flags).GetValue(slots);File.WriteAllText(Path.Combine(folder,"actual-groups.json"),JsonConvert.SerializeObject(groups,Formatting.Indented));
                foreach(string name in new[]{"pending","allocating","probes","installedExecutions"}){var values=session.GetType().GetField(name,flags).GetValue(session);File.WriteAllText(Path.Combine(folder,"actual-"+name+".json"),JsonConvert.SerializeObject(values,Formatting.Indented));}
                // Isolate the production crowd from group speed/pending gates.
                // Advancing this detached clone is component diagnosis only.
                var clone=new NavCrowd(new NavGeometry(state.HalfExtent,state.Obstacles,state.GeometryRevision),p.Navigation,p.AuthoredMap);clone.RestoreState(state);
                for(int tick=0;tick<60;tick++)clone.Step(1d/30);
                File.WriteAllText(Path.Combine(folder,"isolated-crowd-after60.json"),JsonConvert.SerializeObject(clone.CaptureState(),Formatting.Indented));
                foreach(var u in state.Units.Where(u=>u.Id>=10&&u.Id<=13))TestContext.WriteLine("actor="+u.Id+" pos="+u.Position.X+","+u.Position.Z+" goal="+u.Goal.X+","+u.Goal.Z+" moving="+u.Moving+" route="+u.RouteIndex+"/"+u.Route.Length+" local="+u.LocalIndex+"/"+u.LocalRoute.Length+" noProgress="+u.NoProgressTicks+" blocked="+u.BlockedSeconds);
            }finally{a.Stop();}
        }
    }
}
