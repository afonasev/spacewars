using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation
{
    // Port of src/editor/startAssignment.ts. Route costs are supplied by a validated
    // authored binding, never Euclidean guesses over unsupported terrain.
    public static class OfflineStartAssignment
    {
        private static uint Tie(int seed,string text){uint v=unchecked((uint)seed);foreach(char c in text){v^=c;v=unchecked((v^(v>>16))*0x45d9f3bu);}return v;}
        // Geographic grouping for the current maps' peripheral lobby starts. These
        // distances describe map placement, not movement costs across terrain.
        // Authored scenario pins are deliberately handled only by Resolve below.
        public static int[] ResolveLobby(IReadOnlyList<OfflineStart> starts,IReadOnlyList<OfflineParticipant> roster,int seed)
        {
            if(starts==null||roster==null||roster.Count<2||starts.Count>8||roster.Count>starts.Count||
                roster.Select(p=>p.LogicalPlayer).Distinct().Count()!=roster.Count||roster.Select(p=>p.Id).Distinct().Count()!=roster.Count)
                throw new ArgumentException("Invalid lobby participants/capacity.");
            double x=starts.Average(s=>s.Position.X),z=starts.Average(s=>s.Position.Z);
            var ring=Enumerable.Range(0,starts.Count).OrderBy(i=>Math.Atan2(starts[i].Position.Z-z,starts[i].Position.X-x))
                .ThenBy(i=>starts[i].Id,StringComparer.Ordinal).ToArray();
            var distances=new double[starts.Count,starts.Count];
            for(int a=0;a<starts.Count;a++)for(int b=a+1;b<starts.Count;b++){
                double dx=starts[a].Position.X-starts[b].Position.X,dz=starts[a].Position.Z-starts[b].Position.Z;
                distances[a,b]=distances[b,a]=Math.Sqrt(dx*dx+dz*dz);
            }
            var order=Enumerable.Range(0,roster.Count).OrderBy(i=>roster[i].LogicalPlayer).ToArray();
            var assigned=new int[roster.Count];var occupied=Enumerable.Repeat(-1,starts.Count).ToArray();
            int teams=roster.Select(p=>p.Team).Distinct().Count();int[] best=null;double[] bestScore=null;uint bestTie=0;
            void Search(int index)
            {
                if(index<order.Length){int player=order[index];foreach(int site in ring)if(occupied[site]<0){
                    occupied[site]=player;assigned[player]=site;Search(index+1);occupied[site]=-1;
                }return;}
                // At most one occupied arc per team; empty starts do not split a group.
                int first=-1,last=-1,changes=0;
                foreach(int site in ring)if(occupied[site]>=0){int team=roster[occupied[site]].Team;
                    if(first<0)first=team;else if(team!=last)changes++;last=team;
                }
                if(last!=first)changes++;
                if(changes>(teams==1?0:teams))return;
                double allyMax=0,allySum=0,enemyMin=double.PositiveInfinity,enemySum=0;
                for(int a=0;a<roster.Count;a++)for(int b=a+1;b<roster.Count;b++){
                    double d=distances[assigned[a],assigned[b]];
                    if(roster[a].Team==roster[b].Team){allyMax=Math.Max(allyMax,d);allySum+=d;}
                    else{enemyMin=Math.Min(enemyMin,d);enemySum+=d;}
                }
                // Symmetric authored coordinates can differ by floating-point noise.
                // Nanometre rounding prevents that noise from fixing the chosen side.
                var score=new[]{allyMax,allySum,double.IsPositiveInfinity(enemyMin)?0:-enemyMin,-enemySum}
                    .Select(value=>Math.Round(value,9)).ToArray();
                int cmp=0;if(best!=null)for(int i=0;i<score.Length&&cmp==0;i++)cmp=score[i].CompareTo(bestScore[i]);
                if(best!=null&&cmp>0)return;
                uint tie=Tie(seed,string.Join("|",order.Select(i=>roster[i].LogicalPlayer+":"+starts[assigned[i]].Id)));
                if(best==null||cmp<0||tie<bestTie){best=(int[])assigned.Clone();bestScore=score;bestTie=tie;}
            }
            Search(0);return best??throw new ArgumentException("No contiguous lobby team placement.");
        }
        public static int[] Resolve(IReadOnlyList<OfflineStart> starts,IReadOnlyList<OfflineParticipant> roster,int seed,double[,] costs)
        {
            if(roster.Count<2||roster.Count>8||roster.Count>starts.Count||roster.Select(x=>x.Id).Distinct().Count()!=roster.Count||roster.Select(x=>x.LogicalPlayer).Distinct().Count()!=roster.Count)throw new ArgumentException("Invalid authored participants/capacity.");
            var assigned=Enumerable.Repeat(-1,roster.Count).ToArray();var used=new HashSet<int>();
            for(int s=0;s<starts.Count;s++)if(starts[s].PinnedLogicalPlayer.HasValue){int p=Enumerable.Range(0,roster.Count).Where(i=>roster[i].LogicalPlayer==starts[s].PinnedLogicalPlayer.Value).DefaultIfEmpty(-1).First();if(p<0)continue;if(assigned[p]>=0||!used.Add(s))throw new ArgumentException("Authored start pin conflict.");assigned[p]=s;}
            var remaining=Enumerable.Range(0,roster.Count).Where(i=>assigned[i]<0).OrderBy(i=>roster[i].LogicalPlayer).ThenBy(i=>roster[i].Id,StringComparer.Ordinal).ToArray();
            var available=Enumerable.Range(0,starts.Count).Where(i=>!used.Contains(i)).OrderBy(i=>starts[i].Id,StringComparer.Ordinal).ToArray();
            int[] best=null;double[] bestScore=null;uint bestTie=0;
            void Search(int index){if(index<remaining.Length){foreach(int s in available)if(used.Add(s)){assigned[remaining[index]]=s;Search(index+1);used.Remove(s);}return;}
                var ally=new List<double>();var enemy=new List<double>();for(int a=0;a<roster.Count;a++)for(int b=a+1;b<roster.Count;b++){double cost=costs[assigned[a],assigned[b]];if(double.IsNaN(cost)||double.IsInfinity(cost))return;(roster[a].Team==roster[b].Team?ally:enemy).Add(cost);}
                double[] score={ally.Count==0?0:ally.Max(),ally.Sum(),enemy.Count==0?0:-enemy.Min(),-enemy.Sum()};
                string signature=string.Join("|",Enumerable.Range(0,roster.Count).OrderBy(i=>roster[i].LogicalPlayer).Select(i=>roster[i].LogicalPlayer+":"+starts[assigned[i]].Id));uint tie=Tie(seed,signature);int cmp=0;if(best!=null)for(int i=0;i<4&&cmp==0;i++)cmp=score[i].CompareTo(bestScore[i]);
                if(best==null||cmp<0||(cmp==0&&tie<bestTie)){best=(int[])assigned.Clone();bestScore=score;bestTie=tie;}
            }
            Search(0);if(best==null)throw new ArgumentException("Authored start routes unreachable.");return best;
        }
    }
}
