using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private const double MaximumDominationLossPerSecond=2;
        private bool DominationEnabled => offline?.Domination==true;
        private int leadTeam,scoreTeam,scoredWholePoints;
        private double dominationScore;
        private bool scoreWasInterrupted;
        private double warningSeconds=double.PositiveInfinity;
        private readonly List<(SiteState Site,int OldTeam,int NewTeam)> relayChanges=new();

        // Ownership is keyed by team. The first roster slot supplies a stable paint
        // and a compatible event/history identity, even after that player is out.
        internal bool ActiveDominationMission(string ownerId,int siteId)
        {
            if(DominationSnapshot() is not PlayableDominationSnapshot state||!state.Rising||
                !sites.TryGetValue(siteId,out var site)||!IsTeamRelay(site))return false;
            int team=TeamOf(OwnerFor(ownerId));
            if(team==leadTeam)return state.WarningActive&&site.RelayOwnerTeam==team;
            var counts=AiRelayObjectives.RelayCounts(sites.Values.Where(IsTeamRelay).Select(relay=>relay.RelayOwnerTeam));
            return site.RelayOwnerTeam!=team&&AiRelayObjectives.AttackImpact(counts,leadTeam,team,site.RelayOwnerTeam)>0;
        }

        private double DominationSeconds => Math.Max(0,(profile.DominationTarget-dominationScore)/profile.DominationGain);
        private PlayableDominationSnapshot? DominationSnapshot()=>DominationEnabled
            ?new PlayableDominationSnapshot(leadTeam,scoreTeam,dominationScore,DominationSeconds)
            :(PlayableDominationSnapshot?)null;

        private PlayableOwner TeamPaint(int team)=>Owners.First(owner=>TeamOf(owner)==team);
        private bool IsTeamRelay(SiteState site)=>DominationEnabled&&site.Site.Kind==PlayableBuildingKind.RelayTower;
        private PlayableOwner? RelayOwnerPaint(SiteState site)=>!IsTeamRelay(site)?site.Locked:
            site.RelayOwnerTeam==0?(PlayableOwner?)null:TeamPaint(site.RelayOwnerTeam);
        private PlayableOwner? RelayClaimantPaint(SiteState site)=>!IsTeamRelay(site)?site.Claimant:
            site.RelayClaimTeam==0?(PlayableOwner?)null:TeamPaint(site.RelayClaimTeam);

        private void AdvanceTeamRelayCapture(SiteState site,double dt)
        {
            if(site.RelayOwnerTeam!=0)site.ControlledSeconds+=dt;
            int present=0;
            site.Contested=false;
            foreach(var unit in units.Values)
            {
                if(unit.Kind==PlayableEntityKind.Skat||Materializing(unit)||Frozen(unit)||!InRelayZone(unit,site.Site))continue;
                int team=TeamOf(unit.Owner);
                if(present!=0&&present!=team){site.Contested=true;return;}
                present=team;
            }

            double delta=dt/profile.RelayCaptureSeconds;
            if(site.RelayOwnerTeam!=0)
            {
                bool hostile=present!=0&&present!=site.RelayOwnerTeam;
                site.Progress=hostile?Math.Max(0,site.Progress-delta):Math.Min(1,site.Progress+delta);
                if(site.Progress<=1e-9)ChangeTeamRelay(site,0);
                return;
            }

            if(present!=0&&present==site.RelayClaimTeam)
            {
                site.Progress=Math.Min(1,site.Progress+delta);
                if(site.Progress>=1-1e-9)ChangeTeamRelay(site,present);
            }
            else if(site.Progress>0)
            {
                site.Progress=Math.Max(0,site.Progress-delta);
                if(site.Progress<=1e-9){site.Progress=0;site.RelayClaimTeam=0;}
            }
            else if(present!=0)
            {
                site.RelayClaimTeam=present;
                site.Progress=Math.Min(1,delta);
                if(site.Progress>=1-1e-9)ChangeTeamRelay(site,present);
            }
        }

        private void ChangeTeamRelay(SiteState site,int team)
        {
            int former=site.RelayOwnerTeam;
            string name="Вышка связи №"+site.Site.Id;
            if(former!=0)
            {
                var paint=TeamPaint(former);
                Fact(paint,MatchFactKind.RelayNeutralized,0,(int)PlayableBuildingKind.RelayTower,name);
                MatchEvent(MatchEventKind.RelayControlLost,paint,site.Site.Position,site.Site.Id);
                AddLingeringVision(paint,site.Site.Position,profile.RelayVisionRange);
            }
            if(team!=0)
            {
                var paint=TeamPaint(team);
                Fact(paint,MatchFactKind.RelayCaptured,0,(int)PlayableBuildingKind.RelayTower,name);
                MatchEvent(MatchEventKind.RelayCaptured,paint,site.Site.Position,site.Site.Id);
            }
            site.RelayOwnerTeam=site.RelayClaimTeam=team;
            site.Progress=team==0?0:1;
            relayChanges.Add((site,former,team));
        }

        private void NeutralizeEliminatedTeams(int[] teams)
        {
            foreach(var site in sites.Values)
            {
                if(site.RelayOwnerTeam!=0&&!teams.Contains(site.RelayOwnerTeam))ChangeTeamRelay(site,0);
                else if(site.RelayClaimTeam!=0&&!teams.Contains(site.RelayClaimTeam))
                {
                    site.RelayClaimTeam=0;
                    site.Progress=0;
                }
            }
        }

        private void WriteDominationState(BinaryWriter writer)
        {
            writer.Write(leadTeam);
            writer.Write(scoreTeam);
            writer.Write(scoredWholePoints);
            WorldWire.Number(writer,dominationScore);
            writer.Write(!double.IsPositiveInfinity(warningSeconds));
            if(!double.IsPositiveInfinity(warningSeconds))WorldWire.Number(writer,warningSeconds);
            writer.Write(dominationVictory);
            writer.Write(scoreWasInterrupted);
            foreach(var site in sites.Values.Where(IsTeamRelay).OrderBy(site=>site.Site.Id))
            {
                writer.Write(site.RelayClaimTeam);
                writer.Write(site.RelayOwnerTeam);
            }
        }

        private void ReadDominationState(BinaryReader reader)
        {
            bool TeamValid(int team)=>team==0||offline.Roster.Any(player=>player.Team==team);
            leadTeam=reader.ReadInt32();
            scoreTeam=reader.ReadInt32();
            scoredWholePoints=reader.ReadInt32();
            dominationScore=WorldWire.Number(reader);
            warningSeconds=WorldWire.Boolean(reader)?WorldWire.Number(reader):double.PositiveInfinity;
            dominationVictory=WorldWire.Boolean(reader);
            scoreWasInterrupted=WorldWire.Boolean(reader);
            if(!TeamValid(leadTeam)||!TeamValid(scoreTeam)||scoredWholePoints<0||dominationScore<0||
                (scoreTeam==0&&dominationScore>0)||warningSeconds<0)
                throw new ArgumentException("Invalid domination score binding.");
            foreach(var site in sites.Values.Where(IsTeamRelay).OrderBy(site=>site.Site.Id))
            {
                site.RelayClaimTeam=reader.ReadInt32();
                site.RelayOwnerTeam=reader.ReadInt32();
                if(site.Claimant.HasValue||site.Locked.HasValue||!TeamValid(site.RelayClaimTeam)||!TeamValid(site.RelayOwnerTeam)||
                    site.RelayOwnerTeam!=0&&site.RelayClaimTeam!=site.RelayOwnerTeam)
                    throw new ArgumentException("Invalid domination relay binding.");
            }
        }

        private int RelayLeader()
        {
            int leader=0,highest=0;
            Span<int> counts=stackalloc int[9];
            counts.Clear();
            foreach(var site in sites.Values)if(site.RelayOwnerTeam!=0)counts[site.RelayOwnerTeam]++;
            for(int team=1;team<counts.Length;team++)
            {
                if(counts[team]>highest){highest=counts[team];leader=team;}
                else if(counts[team]==highest)leader=0;
            }
            return leader;
        }

        private void SetDominationScore(double value)
        {
            dominationScore=value;
            int whole=value<=0?0:(int)Math.Ceiling(value-1e-9);
            if(whole!=scoredWholePoints&&scoreTeam!=0)
                Fact(TeamPaint(scoreTeam),MatchFactKind.DominationPoints,value);
            scoredWholePoints=whole;
            if(value==0){scoreTeam=0;warningSeconds=double.PositiveInfinity;}
        }

        private void UpdateDominationLead()
        {
            if(relayChanges.Count>0)
            {
                int next=RelayLeader();
                if(next!=leadTeam)
                {
                    var decisive=relayChanges.LastOrDefault(change=>change.OldTeam==leadTeam||change.NewTeam==next).Site??relayChanges.Last().Site;
                    if(leadTeam!=0)
                    {
                        Fact(TeamPaint(leadTeam),MatchFactKind.DominationLeadLost,0,decisive.Site.Id);
                        MatchEvent(MatchEventKind.DominationLost,TeamPaint(leadTeam),decisive.Site.Position);
                    }
                    leadTeam=next;
                    if(next!=0)
                    {
                        Fact(TeamPaint(next),MatchFactKind.DominationLeadGained,0,decisive.Site.Id);
                        MatchEvent(MatchEventKind.DominationGained,TeamPaint(next),decisive.Site.Position);
                    }
                }
                relayChanges.Clear();
            }
        }

        private void AdvanceDomination(double dt)
        {
            if(scoreTeam!=0&&scoreTeam!=leadTeam)
            {
                double rate=Math.Min(MaximumDominationLossPerSecond,leadTeam==0?profile.DominationDecay:profile.DominationDisplacement);
                double loss=Math.Min(dominationScore,dt*rate);
                SetDominationScore(dominationScore-loss);
                warningSeconds=Math.Max(warningSeconds,DominationSeconds);
                scoreWasInterrupted=true;
                if(scoreTeam!=0||leadTeam==0)return;
                dt=Math.Max(0,dt-loss/rate);
            }
            if(leadTeam==0)return;
            scoreTeam=leadTeam;
            if(dt<=1e-9)return;
            bool resumed=scoreWasInterrupted||double.IsPositiveInfinity(warningSeconds);
            scoreWasInterrupted=false;
            double previousSeconds=DominationSeconds;
            if(dominationScore<profile.DominationTarget)
            {
                double gained=dominationScore+dt*profile.DominationGain;
                SetDominationScore(gained>=profile.DominationTarget-1e-9?profile.DominationTarget:gained);
            }
            var paint=TeamPaint(leadTeam);
            double seconds=DominationSeconds;
            if(warningSeconds>PlayableDominationSnapshot.WarningSeconds+1e-9&&seconds<=PlayableDominationSnapshot.WarningSeconds+1e-9)
                MatchEvent(MatchEventKind.DominationNinety,paint,default);
            if(warningSeconds>10+1e-9&&seconds<=10+1e-9)
                MatchEvent(MatchEventKind.DominationTen,paint,default);
            int remaining=(int)Math.Ceiling(seconds-1e-9);
            if(remaining>0&&remaining<=10&&(remaining<(int)Math.Ceiling(previousSeconds-1e-9)||resumed))
                MatchEvent(MatchEventKind.DominationCountdown,paint,default,remaining);
            warningSeconds=seconds;
            if(dominationScore>=profile.DominationTarget)
            {
                Fact(paint,MatchFactKind.DominationPoints,dominationScore);
                WinnerTeam=leadTeam;
                Outcome=PlayableMatchOutcome.TeamWon;
                dominationVictory=true;
            }
        }
    }
}
