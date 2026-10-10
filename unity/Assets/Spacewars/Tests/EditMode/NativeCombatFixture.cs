using System;
using System.Reflection;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    // Explicit combat arrangement; ordinary matches never receive these tanks.
    internal static class NativeCombatFixture
    {
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        internal static object WithTwoEnemyDefenders(PlayableProfile profile,long generation=1,bool patrol=true)
        {
            var domain=Activator.CreateInstance(Domain,Flags,null,new object[]{profile,generation,patrol,true,null},null);
            void Call(string name,params object[] args)=>Domain.GetMethod(name,Flags).Invoke(domain,args);
            Call("AddBuilding",PlayableOwner.Player,PlayableBuildingKind.Headquarters,profile.Headquarters(PlayableOwner.Player),true);
            Call("AddBuilding",PlayableOwner.Enemy,PlayableBuildingKind.Headquarters,profile.Headquarters(PlayableOwner.Enemy),true);
            Call("RebuildGeometry");
            var home=profile.Headquarters(PlayableOwner.Enemy);
            Call("SpawnEnemy",new NavPoint(home.X-profile.DefenderOffsetX,home.Z-profile.DefenderOffsetZ));
            Call("SpawnEnemy",new NavPoint(home.X-profile.DefenderOffsetX,home.Z+profile.DefenderOffsetZ));
            home=profile.Headquarters(PlayableOwner.Player);
            Call("SpawnUnit",new NavPoint(home.X+profile.DefenderOffsetX,home.Z-profile.DefenderOffsetZ),PlayableOwner.Player,PlayableEntityKind.Explorer);
            home=profile.Headquarters(PlayableOwner.Enemy);
            Call("SpawnUnit",new NavPoint(home.X-profile.DefenderOffsetX,home.Z),PlayableOwner.Enemy,PlayableEntityKind.Explorer);
            return domain;
        }
    }
}
