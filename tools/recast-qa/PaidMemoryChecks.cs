using System;
using System.Threading;
using Spacewars.Runtime;
using Spacewars.Simulation;

internal static class PaidMemoryChecks
{
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static NavigationRetentionBudget.Scope Open(NavigationRetentionBudget budget){Require(budget.TryOpenScope(NavigationPreparationKind.Solve,0,0,out var scope),"scope refused");return scope;}
    internal static void Run(RecastAuthorityGeometry geometry,NavLocation from,NavLocation to)
    {
        ulong before=RecastNative.sw_recast_allocation_count();var budget=new NavigationRetentionBudget();
        var scope=Open(budget);RecastNavigationMap map;
        try{using var memory=RecastNative.Memory(scope.OwnedReservation());map=new RecastNavigationMap(geometry);Require(budget.PhysicalBytes>0,"native map was uncharged");}
        finally{scope.Discard();}
        Require(budget.ActiveScopes==0&&budget.PhysicalBytes>0,"resolved scope lost native owner");
        var other=new NavigationRetentionBudget();var outer=Open(budget);var inner=Open(other);RecastNavigationQuery query;
        try{
            using var a=RecastNative.Memory(outer.OwnedReservation());
            using(var b=RecastNative.Memory(inner.OwnedReservation())){query=map.Query(from,to);Require(other.PhysicalBytes>0,"nested query charged wrong owner");}
        }finally{inner.Discard();outer.Discard();}
        map.Dispose();Require(budget.PhysicalBytes>0,"query did not retain retired map pin");
        var step=Open(other);
        try{using var memory=RecastNative.Memory(step.OwnedReservation());while(query.Status==RecastQueryStatus.Pending)query.Step(256);Require(query.Status==RecastQueryStatus.Full,"pinned map query failed");}
        finally{step.Discard();}
        Exception failure=null;var thread=new Thread(()=>{try{query.Dispose();}catch(Exception error){failure=error;}});thread.Start();thread.Join();
        Require(failure==null&&budget.PhysicalBytes==0&&other.PhysicalBytes==0,"other-thread final native release leaked ledger bytes");
        var denied=new NavigationRetentionBudget();var failed=Open(denied);int calls=0;denied.AllocationAdmission=_=>++calls!=2;
        try{using var memory=RecastNative.Memory(failed.OwnedReservation());try{using var bad=new RecastNavigationMap(geometry);throw new Exception("native allocation denial ignored");}catch(RecastNative.CapacityRefused){}}
        finally{failed.Discard();}
        Require(denied.PhysicalBytes==0&&denied.HeldBytes==0,"denied native bake leaked credits");
        Require(RecastNative.ReleaseFaults==0&&RecastNative.sw_recast_allocation_count()==before,"native release fault/leak");
        Console.WriteLine("PAID native map/query/step/nested/original-ledger/retiredpin/otherthread/denial PASS");
    }
}
