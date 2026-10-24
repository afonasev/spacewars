// QA-only: temporarily install in Simulation together with the observation patch.
// No solver-owned instance fields, decisions, operations or budget changes.
using System;
namespace Spacewars.Simulation
{
    internal struct NavDemandReopenEventQa
    {
        internal string Kind;internal int Node,From;
        internal double G,OldG,FromG,Cost,H,F;
        internal bool Had,Removed,Added,Decision;
    }
    internal static class NavDemandReopenProbeQa
    {
        internal static Action<NavDemandReopenEventQa> Sink;
        internal static void Entry(int node,double g,double h,double f)=>Sink?.Invoke(new NavDemandReopenEventQa{Kind="E",Node=node,G=g,H=h,F=f});
        internal static void Seed(int node,double g,double f,bool added)=>Sink?.Invoke(new NavDemandReopenEventQa{Kind="S",Node=node,G=g,F=f,Added=added});
        internal static void Remove(string kind,int node,double g,double f,bool removed)=>Sink?.Invoke(new NavDemandReopenEventQa{Kind=kind,Node=node,G=g,F=f,Removed=removed});
        internal static void Pop(int node,double g,double f,bool removed)=>Sink?.Invoke(new NavDemandReopenEventQa{Kind="P",Node=node,G=g,F=f,Removed=removed});
        internal static void Attempt(string kind,int node,int from,double g,double oldG,double fromG,double cost,bool had,bool decision)=>Sink?.Invoke(new NavDemandReopenEventQa{Kind=kind,Node=node,From=from,G=g,OldG=oldG,FromG=fromG,Cost=cost,Had=had,Decision=decision});
        internal static void Update(int node,int from,double g,double oldG,double fromG,double cost,bool had,bool removed,bool added)=>Sink?.Invoke(new NavDemandReopenEventQa{Kind="U",Node=node,From=from,G=g,OldG=oldG,FromG=fromG,Cost=cost,Had=had,Removed=removed,Added=added});
    }
}
