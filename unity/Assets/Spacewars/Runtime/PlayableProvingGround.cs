using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        internal void StartProvingGroundBattle()
        {
            if(offline?.ProvingGround==null)return;
            foreach(var owner in Owners){var ids=units.Values.Where(u=>u.Owner==owner).Select(u=>u.Id).ToArray();if(ids.Length==0)continue;
                // The setup host issues one initial ordinary tactical order; it never runs an economic AI.
                var destination=((ProvingGroundMap)profile.AuthoredMap).AttackDestination(owner);
                var command=new PlayableCommand(navigation.Generation,1,OwnerName(owner),PlayableCommandKind.AttackMove,ids,destination);
                var result=ApplyWithoutHumanStatistics(command,out var message);
                if(result!=PlayableCommandStatus.Applied)throw new System.InvalidOperationException("Proving ground attack: "+result+" "+message);
            }
        }
        private void InitializeProvingGroundResearch()
        {
            if(offline?.ProvingGround==null)return;
            foreach(var owner in Owners){ownerCredits[owner]=profile.ProvingGroundStartingCredits;
                foreach(var kind in offline.ProvingGround.Research((int)owner))Research(owner).Add(new ResearchOrder{Id=nextResearchSequence++,Kind=kind,Complete=true});}
        }
    }
}
