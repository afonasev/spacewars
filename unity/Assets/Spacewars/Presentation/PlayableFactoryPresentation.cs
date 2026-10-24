using System;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private void UpdateFactoryVehicle(Actor actor,PlayableBuildingSnapshot b,PlayableSnapshot view)
        {
            bool visible=b.LaunchKind.HasValue&&b.LaunchPhase>0&&b.LaunchPhase<FactoryReleaseRules.Closing&&b.PrivateState?.Lifecycle?.Selling!=true;
            if(!visible){if(actor.FactoryStaged)actor.FactoryStaged.gameObject.SetActive(false);return;}
            var kind=b.LaunchKind.Value;
            if(!actor.FactoryStaged||actor.FactoryStagedKind!=kind||actor.FactoryStagedUpgraded!=b.LaunchUpgraded)
            {
                if(actor.FactoryStaged)ReleaseWorldObject(actor.FactoryStaged.gameObject);
                string key=kind==PlayableEntityKind.Tank?(b.LaunchUpgraded?"tankUpgraded":"tank"):kind==PlayableEntityKind.Explorer?(b.LaunchUpgraded?"explorerUpgraded":"explorer"):(b.LaunchUpgraded?"shkval-guidance-3":"shkval");
                actor.FactoryStaged=Model(key,actor.Root,b.Owner==view.Owner);actor.FactoryStaged.name="stagedFactoryVehicle";
                actor.FactoryStagedKind=kind;actor.FactoryStagedUpgraded=b.LaunchUpgraded;actor.PresentationPaint=null;
                actor.FactoryStaged.localScale=Vector3.one*(float)(kind==PlayableEntityKind.Tank?profile.TankModelRadius*profile.TankModelScale/1.585:kind==PlayableEntityKind.Explorer?profile.ExplorerModelRadius*profile.ExplorerModelScale/1.0773:profile.ShkvalModelRadius*profile.ShkvalModelScale/2.05);
            }
            actor.FactoryStaged.gameObject.SetActive(true);
            double travel=b.LaunchPhase==FactoryReleaseRules.Driving?Math.Min(FactoryReleaseRules.ExitDistance(profile,kind),b.LaunchTime*profile.FactoryRolloutSpeed):0;
            double rampEnd=1.65*FactoryReleaseRules.ModelScale(profile),rampStart=FactoryReleaseRules.GatePlane(profile);
            float raised=Mathf.Clamp01((float)((rampEnd-travel)/(rampEnd-rampStart)));
            float y=(float)(FactoryReleaseRules.Floor(profile)+ApronArt.apronHeight)*raised-(float)ApronArt.apronHeight;
            actor.FactoryStaged.localPosition=new Vector3((float)travel,y,0);
            actor.FactoryStaged.localRotation=Quaternion.Euler(0,90,0);
        }
    }
}
