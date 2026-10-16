using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Detached policy only: public geometry, current observation and observed contact memory.
    internal static class AiIncomeSiteSelection
    {
        internal static bool Free(TerritorySiteSnapshot s,PlayableAiObservation o,PlayableProfile p)=>
            (s.Site.Kind==PlayableBuildingKind.Mine||s.Site.Kind==PlayableBuildingKind.Outpost)&&TerritoryRules.Income(p,s.Site.Kind)>0&&
            s.CenterId==0&&!s.Contested&&(!s.Owner.HasValue||s.Owner==o.Owner)&&(!s.Claimant.HasValue||s.Claimant==o.Owner);
        internal static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        private static NavPoint Position(PlayableAiObservation o,int site,NavPoint fallback)=>
            o.Intel.Areas.FirstOrDefault(a=>a.Envelope.AreaId==site)?.Envelope.Placement.FirstOrDefault().Position??
            o.Sites.FirstOrDefault(s=>s.Site.Id==site)?.Site.Position??fallback;
        internal static NavPoint Home(PlayableAiObservation o)=>Position(o,o.HomeSiteId,
            o.Buildings.FirstOrDefault(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Headquarters)?.Position??default);
        internal static AiKnownContact[] Mains(PlayableAiObservation o,AiKnowledgeState knowledge)=>
            (knowledge?.Contacts??Array.Empty<AiKnownContact>()).Where(c=>c.Building&&c.Kind==(int)PlayableBuildingKind.Headquarters&&o.IsHostile(c.Owner))
                .Concat(o.Buildings.Where(b=>b.Kind==PlayableBuildingKind.Headquarters&&b.Health>0&&o.IsHostile(b.Owner))
                    .Select(b=>new AiKnownContact(b.Id,b.Owner,true,(int)b.Kind,b.Position,b.Health,o.Tick,1,true))).GroupBy(c=>c.Id).Select(g=>g.Last()).ToArray();
        internal static bool DrawRemote(PlayableAiObservation o,AiProfile ai,long ordinal)=>
            AiRandom.Key(o.Seed,o.OwnerId,ai.Hash,"income-site-remote",ordinal)/((double)uint.MaxValue+1)<ai.Value("economy.remoteExpansionShare");
        internal static bool RemoteAllowed(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge,TerritorySiteSnapshot site)
        {
            foreach(var main in Mains(o,knowledge))
            {
                // Rank all public income locations, including occupied/hidden ones, without reading occupancy.
                var protectedSites=o.PublicScoutObjectives.Where(g=>g.Role!=PlayablePublicScoutObjectiveRole.PossibleEnemyStart)
                    .OrderBy(g=>Distance(Position(o,g.SiteId,g.Approach),main.Position)).ThenBy(g=>g.SiteId)
                    .Take((int)ai.Value("economy.enemyMainProtectedIncomeSites"));
                if(protectedSites.Any(g=>g.SiteId==site.Site.Id))return false;
                // Current observed free sites only. Hidden ownership is not guessed.
                var next=o.Sites.Where(s=>Free(s,o,p)).OrderBy(s=>Distance(s.Site.Position,main.Position)).ThenBy(s=>s.Site.Id).FirstOrDefault();
                if(next?.Site.Id==site.Site.Id)return false;
            }
            return true;
        }
        internal static TerritorySiteSnapshot Select(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge,
            IEnumerable<TerritorySiteSnapshot> candidates,bool remote,int localSite=0)
        {
            var home=Home(o);var ordered=candidates.GroupBy(s=>s.Site.Id).Select(g=>g.First())
                .OrderBy(s=>Distance(s.Site.Position,home)).ThenBy(s=>s.Site.Id).ToArray();
            var local=localSite==0?ordered.FirstOrDefault():ordered.FirstOrDefault(s=>s.Site.Id==localSite);if(!remote)return local;
            var mains=Mains(o,knowledge);
            return ordered.Where(s=>s.Site.Id!=(localSite!=0?localSite:local?.Site.Id??0)&&RemoteAllowed(o,p,ai,knowledge,s))
                .OrderByDescending(s=>mains.Select(m=>Distance(s.Site.Position,m.Position)).DefaultIfEmpty(0).Min())
                .ThenBy(s=>Distance(s.Site.Position,home)).ThenBy(s=>s.Site.Id).FirstOrDefault()??local;
        }
        // Recheck unaccepted goals when a main becomes known, without penalizing the local fallback.
        internal static bool TerritoryAllowed(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge,TerritorySiteSnapshot site)
        {
            var nearest=o.Sites.Where(s=>Free(s,o,p)&&SiteSafe(o,p,knowledge,s)).OrderBy(s=>Distance(s.Site.Position,Home(o))).ThenBy(s=>s.Site.Id).FirstOrDefault();
            return nearest?.Site.Id==site.Site.Id||RemoteAllowed(o,p,ai,knowledge,site);
        }
        internal static bool SiteSafe(PlayableAiObservation o,PlayableProfile p,AiKnowledgeState knowledge,TerritorySiteSnapshot s)=>
            !s.Contested&&!o.Entities.Any(e=>o.IsHostile(e.Owner)&&e.Health>0&&Distance(e.Position,s.Site.Position)<=p.ExplorerVision)&&
            !o.Buildings.Any(b=>o.IsHostile(b.Owner)&&b.Health>0&&Distance(b.Position,s.Site.Position)<=p.ExplorerVision)&&
            !(knowledge?.Contacts??Array.Empty<AiKnownContact>()).Any(c=>c.Building&&o.IsHostile(c.Owner)&&Distance(c.Position,s.Site.Position)<=p.ExplorerVision);
    }
}
