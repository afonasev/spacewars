using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        // Derived owner-thread read set only. It is not authority or saved state.
        // A projection previously rebuilt every team's input enumeration, even
        // when only presentation sequence/receipts had changed.
        private struct VisionUnitInput
        {internal int Id;internal PlayableOwner Owner;internal PlayableEntityKind Kind;internal bool Source;internal NavPoint Position;internal double Radius;}
        private struct VisionBuildingInput
        {internal int Id;internal PlayableOwner Owner;internal PlayableBuildingKind Kind;internal ConstructionPhase Phase;internal NavPoint Position;internal double Heading;internal bool Upgraded;}
        private PlayableProfile visionInputProfile;
        private VisionUnitInput[] visionUnitInputs;
        private VisionBuildingInput[] visionBuildingInputs;
        private int visionInputTeams;
        private VisionUnitInput ReadVisionInput(Unit unit)
        {
            NavUnit actor=null;bool source=unit.Health>0&&TryActor(unit.Id,out actor);
            return new VisionUnitInput{Id=unit.Id,Owner=unit.Owner,Kind=unit.Kind,Source=source,
                Position=source?actor.Position:default,Radius=source?PlayableUnitRules.Vision(profile,unit.Kind):0};
        }
        private bool VisionInputsUnchanged()
        {
            if(offline?.ProvingGround!=null||!ReferenceEquals(visionInputProfile,profile)||visionUnitInputs==null||visionBuildingInputs==null||
                visionUnitInputs.Length!=units.Count||visionBuildingInputs.Length!=buildings.Count||visionInputTeams!=visions.Count)return false;
            int index=0;
            foreach(var unit in units.Values){
                var current=ReadVisionInput(unit);var prior=visionUnitInputs[index++];
                if(current.Id!=prior.Id||current.Owner!=prior.Owner||current.Kind!=prior.Kind||current.Source!=prior.Source||
                    current.Position.X!=prior.Position.X||current.Position.Z!=prior.Position.Z||current.Radius!=prior.Radius)return false;
            }
            index=0;
            foreach(var building in buildings.Values){
                var prior=visionBuildingInputs[index++];
                if(building.Id!=prior.Id||building.Owner!=prior.Owner||building.Kind!=prior.Kind||building.Phase!=prior.Phase||
                    building.Position.X!=prior.Position.X||building.Position.Z!=prior.Position.Z||building.Heading!=prior.Heading||
                    (building.Upgrade?.Complete==true||CenterUpgraded(building.Id)||building.ProducerUpgrade?.Complete==true)!=prior.Upgraded)return false;
            }
            return true;
        }
        private void RememberVisionInputs()
        {
            if(offline?.ProvingGround!=null)return;
            if(visionUnitInputs==null||visionUnitInputs.Length!=units.Count)visionUnitInputs=new VisionUnitInput[units.Count];
            if(visionBuildingInputs==null||visionBuildingInputs.Length!=buildings.Count)visionBuildingInputs=new VisionBuildingInput[buildings.Count];
            int index=0;foreach(var unit in units.Values)visionUnitInputs[index++]=ReadVisionInput(unit);
            index=0;foreach(var b in buildings.Values)visionBuildingInputs[index++]=new VisionBuildingInput{Id=b.Id,Owner=b.Owner,Kind=b.Kind,Phase=b.Phase,Position=b.Position,Heading=b.Heading,Upgraded=b.Upgrade?.Complete==true||CenterUpgraded(b.Id)||b.ProducerUpgrade?.Complete==true};
            visionInputProfile=profile;visionInputTeams=visions.Count;
        }
    }
}
