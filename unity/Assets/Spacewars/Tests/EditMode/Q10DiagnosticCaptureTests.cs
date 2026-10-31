using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        [Test,Explicit("Known-invalid diagnostic149/100 is not a Q10 replay gate; original population FAIL remains preserved.")] public void Q10UnchangedDiagnostic400ConfiguredCaptureRestoresExactCurrentFormat()
        {
            Contact30Source(false,(config,views,profile)=>{
                Assert.AreEqual(100,profile.ArmyCapacity); // The actual diagnostic binding, never normalized.
                Assert.True(views.All(v=>v.Entities.Count(e=>e.Owner==v.Owner)==50));
                var source=new PlayableAuthorityTick(config,1);var domain=(PlayableDomain)typeof(PlayableAuthorityTick).GetField("domain",CostFlags).GetValue(source);var budget=domain.Navigation.PhysicalHostBudget;
                PlayableAuthorityTick cold=null;PlayableDomain coldDomain=null;NavigationRetentionBudget coldBudget=null;
                try{
                    byte[] bytes=source.CaptureBytes();string output=Environment.GetEnvironmentVariable("SPACEWARS_Q10_CAPTURE_OUTPUT");
                    if(output!=null){Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,"diagnostic-configured.save"),bytes);File.WriteAllText(Path.Combine(output,"binding.txt"),"producer="+profile.ProfileId+"@"+profile.Revision+" seed="+config.Seed+" map="+profile.AuthoredMap.Id+" source="+config.SourceIdentity+" armyCapacity="+profile.ArmyCapacity+" ownActors=50 diagnosticPopulation=149\n");}
                    cold=PlayableAuthorityTick.RestoreBytes(bytes,config);coldDomain=(PlayableDomain)typeof(PlayableAuthorityTick).GetField("domain",CostFlags).GetValue(cold);coldBudget=coldDomain.Navigation.PhysicalHostBudget;
                    CollectionAssert.AreEqual(bytes,cold.CaptureBytes(),"Unchanged current-format configured world/AI must restore exactly.");
                }finally{
                    cold?.Stop();coldDomain?.DestroyOwnedNavigation();if(coldBudget!=null){Assert.Zero(coldBudget.PhysicalBytes);Assert.Zero(coldBudget.HeldBytes);Assert.Zero(coldBudget.ActiveScopes);}
                    source.Stop();domain.DestroyOwnedNavigation();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                }
            });
        }
    }
}
