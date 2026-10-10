using System;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Headless
{
    [Serializable] public sealed class ArmyRecoveryCommand
    {
        public long Tick;public string OwnerId,Kind,Purpose;public int[] EntityIds;public double X,Z;public long Sequence;
        public PlayableCommand Create(long generation)=>new PlayableCommand(generation,Sequence,OwnerId,(PlayableCommandKind)Enum.Parse(typeof(PlayableCommandKind),Kind),EntityIds,new NavPoint(X,Z),origin:PlayableOrderOrigin.Human);
    }
    [Serializable] public sealed class ArmyRecoveryManifest
    {
        public string Schema="native-a3-e6-derived-v1",OriginalFixture,OriginalManifestHash,SourceIdentity="native-a3-e6-hq-guard-v1",MapIdentity;
        public MatchManifest OriginalManifest;
        public int GuardLogicalPlayer=2;public PlayableEntityKind GuardKind=PlayableEntityKind.Tank;public double GuardX=18,GuardZ=18,GuardHeading;
        public int[] OriginalRaidIds;public int GuardId;public ArmyRecoveryCommand[] Commands;
    }
    // Explicit test-input derivation, never used by normal match creation. The
    // archived raid selector is resolved on its original genesis before adding a guard.
    public static class ArmyRecoveryFixture
    {
        public static OfflineMatchConfiguration Create(ArmyRecoveryManifest input)
        {
            if(input==null||input.Schema!="native-a3-e6-derived-v1"||input.OriginalManifest==null||input.OriginalManifest.Fixture!="economy-lost-hq-v1"||input.GuardKind!=PlayableEntityKind.Tank||input.GuardLogicalPlayer!=2)throw new ArgumentException("A3 recovery input binding");
            var c=HeadlessFixtures.Create(input.OriginalManifest);
            return new OfflineMatchConfiguration(c.Profile,input.SourceIdentity,input.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},
                scenario:c.ScenarioUnits.Concat(new[]{new OfflineScenarioUnit(input.GuardLogicalPlayer,input.GuardKind,new NavPoint(input.GuardX,input.GuardZ),input.GuardHeading)}).ToArray(),scenarioBuildings:c.ScenarioBuildings.ToArray());
        }
        public static void BindCommands(ArmyRecoveryManifest input,PlayableAuthorityTick derived)
        {
            var original=new PlayableAuthorityTick(HeadlessFixtures.Create(input.OriginalManifest),input.OriginalManifest.Generation);
            var raidInput=input.OriginalManifest.Commands.Single();
            if(raidInput.Tick!=0||raidInput.OwnerId!="east-owner"||raidInput.Kind!="AttackMove"||raidInput.Selector!="Tanks")throw new ArgumentException("Unexpected original raid contract");
            var originalCommand=HeadlessFixtures.Command(original,raidInput,input.OriginalManifest.Generation,1);
            var baseline=original.ParticipantView("east-owner");var current=derived.ParticipantView("east-owner");
            input.OriginalRaidIds=baseline.Entities.Where(u=>u.Owner==baseline.Owner&&u.Kind==PlayableEntityKind.Tank).OrderBy(u=>u.Id).Select(u=>u.Id).ToArray();
            if(input.OriginalRaidIds.Length!=6||!input.OriginalRaidIds.SequenceEqual(originalCommand.EntityIds.OrderBy(x=>x)))throw new InvalidOperationException("Original six-tank raid membership changed");
            foreach(var id in input.OriginalRaidIds)
            {
                var before=baseline.Entities.Single(u=>u.Id==id);var after=current.Entities.Single(u=>u.Id==id);
                if(before.Kind!=after.Kind||before.Owner!=after.Owner||!before.Position.Equals(after.Position)||before.Health!=after.Health)throw new InvalidOperationException("Derived raid genesis changed original unit");
            }
            input.GuardId=current.Entities.Single(u=>u.Owner==current.Owner&&u.Kind==PlayableEntityKind.Tank&&!input.OriginalRaidIds.Contains(u.Id)).Id;
            input.Commands=new[]{new ArmyRecoveryCommand{Tick=raidInput.Tick,OwnerId=raidInput.OwnerId,Kind=raidInput.Kind,Purpose="original raid, exact six IDs resolved before guard addition",EntityIds=input.OriginalRaidIds,X=raidInput.X,Z=raidInput.Z,Sequence=1},
                new ArmyRecoveryCommand{Tick=0,OwnerId="east-owner",Kind="Hold",Purpose="separate stock enemy HQ guard",EntityIds=new[]{input.GuardId},X=input.GuardX,Z=input.GuardZ,Sequence=2}};
            original.Stop();
        }
        public static void AssertOrders(ArmyRecoveryManifest input,PlayableAuthorityTick authority)
        {
            var v=authority.ParticipantView("east-owner");
            if(input.OriginalRaidIds.Any(id=>!v.Entities.Any(u=>u.Id==id&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.AttackMove&&u.CurrentOrder.Destination.Equals(new NavPoint(input.Commands[0].X,input.Commands[0].Z))))||
                !v.Entities.Any(u=>u.Id==input.GuardId&&u.Held&&!u.Moving&&u.OrderStamp?.Kind==PlayableCommandKind.Hold))throw new InvalidOperationException("Raid/guard ordinary orders not effective");
        }
    }
}
