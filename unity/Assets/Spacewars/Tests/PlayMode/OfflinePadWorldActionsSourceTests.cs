using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
namespace Spacewars.Tests.PlayMode
{
    public sealed class OfflinePadWorldActionsSourceTests
    {
        [Serializable] private sealed class Matrix {public Sample[] proofs;}
        [Serializable] private sealed class Sample {public string kind,mode,sourceId;public SourceAction[] actions;}
        [Serializable] private sealed class SourceAction {public string id,hold;public bool enabled,repeatActive;public SourceCommand command,holdCommand;}
        [Serializable] private sealed class SourceCommand {public string type,unitKind;}
        [Test] public void ActualSourceOwnerProjectionMatches72BuildingLifecycleDescriptorCases()
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());const string path="unity/Tests/Fixtures/native-two-seat-gamepad-parity/source-building-matrix.json";while(root!=null&&!File.Exists(Path.Combine(root.FullName,path)))root=root.Parent;Assert.NotNull(root);
            var matrix=JsonUtility.FromJson<Matrix>(File.ReadAllText(Path.Combine(root.FullName,path)));var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text));
            foreach(var sample in matrix.proofs){
                var kind=(PlayableBuildingKind)Enum.Parse(typeof(PlayableBuildingKind),sample.kind,true);bool repairing=sample.mode=="repair-active",damaged=sample.mode.StartsWith("damaged")||repairing,locked=sample.mode=="damaged-locked",selling=sample.mode=="selling";
                int health=TerritoryRules.Health(profile,kind);var lifecycle=new PlayableBuildingLifecycleSnapshot(selling,0,repairing,false,0,30,locked?"combat":null,damaged&&!locked?null:"full",1,0,false);
                var upgrade=sample.mode.StartsWith("refinery-")?new PlayableRefineryUpgradeSnapshot(sample.mode=="refinery-active",sample.mode=="refinery-complete",10,5,10,null):null;
                var research=sample.mode.StartsWith("research-")?new[]{new PlayableResearchOrderSnapshot(1,PlayableResearchKind.TankChassis,100,10,5,10,sample.mode=="research-active",sample.mode=="research-complete")}:Array.Empty<PlayableResearchOrderSnapshot>();
                var building=new PlayableBuildingSnapshot(100,PlayableOwner.Player,kind,default(NavPoint),damaged?health/2:health,1,0,0,default(NavPoint),phase:sample.mode=="requested"?ConstructionPhase.Pending:ConstructionPhase.Ready,repeat:sample.mode=="repeat-active",lifecycle:lifecycle,upgrade:upgrade,refineryUpgraded:sample.mode=="refinery-complete",research:research);
                var availability=new[]{new PlayableResearchAvailabilitySnapshot(PlayableResearchKind.TankChassis,1000,true),new PlayableResearchAvailabilitySnapshot(PlayableResearchKind.ExplorerAssaultGuns,1000,true),new PlayableResearchAvailabilitySnapshot(PlayableResearchKind.ShkvalGuidance,1000,true)};
                var view=new PlayableSnapshot(profile.ProfileId,profile.Revision,1,19092026,1,300,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,sample.mode=="ready-poor"?0:100000,new NavGeometry(45,Array.Empty<NavObstacle>(),1),Array.Empty<PlayableEntitySnapshot>(),new[]{building},Array.Empty<PlayableProjectileSnapshot>(),null,null,researchAvailability:availability,ownerResearch:research);
                var actual=OfflinePadWorldActions.Building(view,profile,100);
                string SourceSuffix(string id)=>id.StartsWith(sample.sourceId+":",StringComparison.Ordinal)?id.Substring(sample.sourceId.Length+1):id;
                string NativeSuffix(string id)=>id.StartsWith("100:",StringComparison.Ordinal)?id.Substring(4):id;
                CollectionAssert.AreEqual(sample.actions.Select(a=>SourceSuffix(a.id)),actual.Select(a=>NativeSuffix(a.Sector.Id)),sample.kind+":"+sample.mode);
                for(int index=0;index<actual.Length;index++){
                    var expected=sample.actions[index];var action=actual[index];string context=sample.kind+":"+sample.mode+":"+expected.id;
                    Assert.AreEqual(expected.enabled,action.Sector.Enabled,context);Assert.AreEqual(expected.hold??null,action.Sector.Hold,context);Assert.AreEqual(expected.repeatActive,action.Repeat,context);
                    CheckCommand(expected.command,action.Command,context);CheckCommand(expected.holdCommand,action.HoldCommand,context);
                }
            }Assert.AreEqual(72,matrix.proofs.Length);
        }
        private static void CheckCommand(SourceCommand expected,OfflinePadActionCommand actual,string context)
        {
            if(expected==null||string.IsNullOrEmpty(expected.type)){Assert.IsNull(actual,context);return;}Assert.NotNull(actual,context);CollectionAssert.AreEqual(new[]{100},actual.Entities,context);
            PlayableCommandKind kind;
            switch(expected.type){case "enqueueProduction":kind=expected.unitKind=="explorer"?PlayableCommandKind.QueueExplorer:expected.unitKind=="shkval"?PlayableCommandKind.QueueShkval:PlayableCommandKind.QueueTank;break;case "toggleRepeatProduction":kind=PlayableCommandKind.ToggleRepeatProduction;break;case "sellBuilding":kind=PlayableCommandKind.SellBuilding;break;case "startBuildingRepair":kind=PlayableCommandKind.StartBuildingRepair;break;case "cancelBuildingRepair":kind=PlayableCommandKind.CancelBuildingRepair;break;case "startRefineryUpgrade":kind=PlayableCommandKind.UpgradeRefinery;break;case "startTankChassisUpgrade":case "startExplorerAssaultGunsUpgrade":case "startShkvalGuidanceResearch":kind=PlayableCommandKind.QueueResearch;break;default:throw new AssertionException("Unknown source command "+expected.type);}
            Assert.AreEqual(kind,actual.Kind,context);
            if(!string.IsNullOrEmpty(expected.unitKind))Assert.AreEqual(Enum.Parse(typeof(PlayableEntityKind),expected.unitKind,true),actual.Unit,context);
            if(expected.type=="startExplorerAssaultGunsUpgrade")Assert.AreEqual(PlayableResearchKind.ExplorerAssaultGuns,actual.Research,context);if(expected.type=="startShkvalGuidanceResearch")Assert.AreEqual(PlayableResearchKind.ShkvalGuidance,actual.Research,context);
        }
    }
}
