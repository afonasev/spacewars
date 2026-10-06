using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal static class OfflineConfigurationWire
    {
        internal static byte[] Binding(OfflineMatchConfiguration c)=>Spacewars.Runtime.WorldWire.Pack(w=>{
            Spacewars.Runtime.WorldWire.String(w,c.SourceIdentity);Spacewars.Runtime.WorldWire.String(w,c.MapIdentity);Spacewars.Runtime.WorldWire.String(w,c.RouteProvenance);w.Write(c.Seed);
            w.Write(c.Roster.Count);foreach(var p in c.Roster){Spacewars.Runtime.WorldWire.String(w,p.Id);w.Write(p.LogicalPlayer);w.Write(p.Team);w.Write((int)p.Control);}w.Write(c.Spectators.Count);foreach(var s in c.Spectators)Spacewars.Runtime.WorldWire.String(w,s);
            w.Write(c.Starts.Count);for(int i=0;i<c.Starts.Count;i++){var s=c.Starts[i];Spacewars.Runtime.WorldWire.String(w,s.Id);w.Write(s.SiteId);Spacewars.Runtime.WorldWire.Write(w,s.Position);Spacewars.Runtime.WorldWire.Write(w,s.ExplorerAnchor);w.Write(s.Heading);w.Write(s.PinnedLogicalPlayer??0);for(int j=0;j<c.Starts.Count;j++)w.Write(c.RouteCost(i,j));}
            w.Write(c.Sites.Count);foreach(var s in c.Sites){w.Write(s.Id);w.Write((int)s.Kind);Spacewars.Runtime.WorldWire.Write(w,s.Position);foreach(var slot in s.Slots){w.Write(slot.Id);Spacewars.Runtime.WorldWire.Write(w,slot.Position);w.Write(slot.Heading);}}
            w.Write(c.Obstacles.Count);foreach(var o in c.Obstacles)Spacewars.Runtime.WorldWire.Write(w,o);w.Write(c.ScenarioUnits.Count);foreach(var u in c.ScenarioUnits){w.Write(u.LogicalPlayer);w.Write((int)u.Kind);Spacewars.Runtime.WorldWire.Write(w,u.Position);w.Write(u.Heading);w.Write(u.Upgraded);}w.Write(c.ScenarioBuildings.Count);foreach(var b in c.ScenarioBuildings){w.Write(b.LogicalPlayer);w.Write((int)b.Kind);w.Write(b.SiteId);w.Write(b.SlotId);WorldWire.Write(w,b.Position);w.Write(b.Heading);}foreach(int a in c.Assignments)w.Write(a);var profileBinding=WorldWire.Binding(c.Profile);w.Write(profileBinding.Length);w.Write(profileBinding);
        });
    }
}
