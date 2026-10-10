using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class WebReleaseBalanceTests
    {
        [Test] public void DefaultUsesAuditedWebNumbers()
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());
            while(root!=null&&!File.Exists(Path.Combine(root.FullName,"docs/web-release-balance/numeric-fields.tsv")))root=root.Parent;
            Assert.NotNull(root,"Project audit must be available.");
            var defaults=PlayableProfile.Default.CopyData();
            int checkedFields=0;
            foreach(var line in File.ReadAllLines(Path.Combine(root.FullName,"docs/web-release-balance/numeric-fields.tsv")))
            {
                var parts=line.Split('\t');
                var field=typeof(PlayableProfileData).GetField(parts[1]);
                Assert.NotNull(field,parts[0]);
                double expected=double.Parse(parts[2],System.Globalization.CultureInfo.InvariantCulture);
                Assert.AreEqual(expected,Convert.ToDouble(field.GetValue(defaults)),1e-9,parts[0]);
                checkedFields++;
            }
            Assert.Greater(checkedFields,170);
            Assert.IsTrue(defaults.tankFiresWhileMoving);
            Assert.AreEqual("kinetic-shell",defaults.tankProjectileType);
        }
        [Test] public void GuidancePriceAndEffectiveUnitStatsMatchWebRelease()
        {
            var p=PlayableProfile.Default;
            Assert.AreEqual(600,p.ShkvalGuidanceCost);
            Assert.AreEqual(13,PlayableUnitRules.Range(p,PlayableEntityKind.Shkval));
            Assert.AreEqual(16,PlayableUnitRules.Range(p,PlayableEntityKind.Shkval,true));
            Assert.AreEqual(4,p.ExplorerBurstSize);
            Assert.AreEqual(7,p.ExplorerSpreadDeg);
        }
    }
}
