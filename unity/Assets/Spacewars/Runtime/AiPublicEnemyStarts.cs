using System;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Public topology and genuine owner knowledge only. A possible start is a
    // scouting destination until these facts establish an enemy zone there.
    internal static class AiPublicEnemyStarts
    {
        internal static bool CanAttack(PlayableAiObservation observation,AiKnowledgeState knowledge,int siteId)
        {
            if(knowledge!=null&&(knowledge.OwnerId!=observation.OwnerId||knowledge.Generation!=observation.Generation||knowledge.ObservationTick>observation.Tick))throw new ArgumentException("Public target knowledge binding");
            var starts=observation.PublicScoutObjectives.Where(g=>g.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart).ToArray();
            var target=starts.FirstOrDefault(g=>g.SiteId==siteId&&g.Reachable);if(target==null)return false;
            AiAreaCoverage Area(int id)=>observation.Intel.Areas.FirstOrDefault(a=>a.Envelope.AreaId==id);
            bool VisibleEnemy(int id){var area=Area(id);return area!=null&&observation.Buildings.Any(b=>b.Health>0&&observation.IsHostile(b.Owner)&&observation.Vision?.IsVisible(b.Position)==true&&area.Envelope.Contains(b.Position));}
            bool Empty(int id){
                if(VisibleEnemy(id))return false;
                var area=Area(id);if(area?.Full==true)return true;
                var known=knowledge?.Areas.FirstOrDefault(a=>a.AreaId==id);
                return known?.EmptyAtLastCoverage==true;
            }
            if(Empty(siteId))return false;
            var envelope=Area(siteId)?.Envelope;
            if(VisibleEnemy(siteId)||envelope!=null&&knowledge?.Contacts.Any(c=>c.Building&&observation.IsHostile(c.Owner)&&envelope.Contains(c.Position))==true)return true;
            // Count every possible public start, including an unreachable one:
            // route availability cannot turn an ambiguous location into knowledge.
            var remaining=starts.Where(g=>!Empty(g.SiteId)).ToArray();
            if(remaining.Length==1)return true;
            int hostileParticipants=observation.Participants.Count==0?1:observation.Participants.Count(p=>p.Team!=observation.Team);
            return starts.Length==hostileParticipants;
        }
    }
}
