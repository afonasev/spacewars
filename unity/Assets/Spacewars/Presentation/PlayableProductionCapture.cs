using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using Spacewars.Simulation;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        // F12 diagnostic sidecar reads the immutable owner snapshot only. It never
        // injects entities, advances simulation, or issues gameplay commands.
        private void CaptureProductionSnapshot(int capture)
        {
            if(view==null||evidence==null)return;
            var text=new StringBuilder();
            string N(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
            text.AppendLine("owner-visible diagnostic; hidden enemy footprints are not inferred");
            text.AppendLine($"profile={profile.ProfileId}@{profile.Revision} source={profile.SourceCommit} sourceProfile={profile.SourceProfileId}@{profile.SourceProfileRevision} map={profile.AuthoredMap.Id}@{profile.AuthoredMap.Revision} seed={view.Seed} generation={view.Generation} tick={view.Tick} geometry={view.Geometry.Revision} paused={view.Paused} living={view.Population.Living} reserved={view.Population.Reserved} credits={view.Credits}");
            foreach(var u in view.Entities)
                text.AppendLine($"unit id={u.Id} owner={u.Owner} kind={u.Kind} health={u.Health} x={N(u.Position.X)} z={N(u.Position.Z)} radius={N(PlayableUnitRules.Radius(profile,u.Kind))} moving={u.Moving} held={u.Held} navigation={u.NavigationOutcome} target={u.TargetId} order={u.CurrentOrder?.Kind.ToString()??"none"}");
            foreach(var b in view.Buildings)text.AppendLine($"building id={b.Id} owner={b.Owner} kind={b.Kind} health={b.Health} phase={b.Phase} x={N(b.Position.X)} z={N(b.Position.Z)}");
            foreach(var b in view.Buildings.Where(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory&&b.PrivateState!=null))
            {
                text.AppendLine($"factory id={b.Id} phase={b.Phase} queue={b.QueueCount} progress={N(b.ProductionProgress)} hasRally={b.PrivateState?.HasRally} pendingRally={b.PrivateState?.PendingRally.HasValue} rallyX={N(b.Rally.X)} rallyZ={N(b.Rally.Z)}");
                foreach(var o in b.PrivateState.Orders)text.AppendLine($"order id={o.Id} kind={o.Kind} remaining={N(o.Remaining)} active={o.Active} paid={o.PaidCost} population={o.PopulationCost}");
                if(b.PrivateState.Orders.Count==0)continue;
                var kind=b.PrivateState.Orders[0].Kind;double r=PlayableUnitRules.Radius(profile,kind);
                var anchor=new NavPoint(b.Position.X+Math.Cos(b.Heading)*profile.FactoryExitDistance,b.Position.Z+Math.Sin(b.Heading)*profile.FactoryExitDistance);
                var candidates=FactoryProductionAnchors.Candidates(b.Position,profile.FactoryFootprintRadius,r,new[]{anchor});
                for(int i=0;i<candidates.Length;i++)
                {
                    var q=candidates[i];var blockers=view.Entities.Where(u=>{double dx=u.Position.X-q.X,dz=u.Position.Z-q.Z,sum=r+PlayableUnitRules.Radius(profile,u.Kind);return dx*dx+dz*dz<sum*sum-1e-9;}).Select(u=>u.Id);
                    text.AppendLine($"candidate index={i} kind={kind} x={N(q.X)} z={N(q.Z)} radius={N(r)} geometryFree={view.Geometry.IsFree(q,r)} visibleBlockers={string.Join(";",blockers)}");
                }
            }
            File.WriteAllText(Path.Combine(evidence,"native-"+capture.ToString("D2")+"-snapshot.txt"),text.ToString());
        }
    }
}
