using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Bounded worker proposal, not persisted native memory. The canonical typed
    // copy is admitted into the existing host BODY at publication.
    internal sealed class RecastMemberRoute
    {
        internal readonly RecastRoutePoint[] Waypoints;
        internal readonly NavTypedLeg[] Legs;
        internal readonly RecastSpineProposal Spine;
        internal readonly int Join,Exit,OriginLegs,TerminalLeg;
        internal RecastMemberRoute(RecastRoutePoint[] waypoints,NavTypedLeg[] legs,RecastSpineProposal spine,int join,int exit,int origin,int terminal,bool adopt)
        {if(!adopt||waypoints.Length==0||waypoints.Length>4096||legs.Length>16384)throw new InvalidOperationException("Recast branch capacity exceeded.");Spine=spine;Join=join;Exit=exit;OriginLegs=origin;TerminalLeg=terminal;Waypoints=waypoints;Legs=legs;}
        internal RecastMemberRoute(IEnumerable<RecastRoutePoint> waypoints,IEnumerable<NavTypedLeg> legs,RecastSpineProposal spine=null,int join=0,int exit=0,int origin=0,int terminal=0)
        {Spine=spine;Join=join;Exit=exit;OriginLegs=origin;TerminalLeg=terminal;Waypoints=waypoints.ToArray();Legs=legs.ToArray();if(Waypoints.Length==0||Waypoints.Length>4096||Legs.Length>16384)throw new InvalidOperationException("Recast branch capacity exceeded.");}
    }
    internal sealed class RecastSpineProposal
    {
        internal readonly NavTypedLeg[] Legs;internal readonly string Binding,MapBinding;internal readonly int Fan;internal readonly int[] Stops;internal readonly NavPoint FanCenter;internal readonly double FanRadius;
        internal RecastSpineProposal(NavTypedLeg[] legs,string binding,string mapBinding,int fan,int[] stops,NavPoint fanCenter,double fanRadius){Legs=legs;Binding=binding;MapBinding=mapBinding;Fan=fan;Stops=stops;FanCenter=fanCenter;FanRadius=fanRadius;}
        internal NavLocation Node(int n)=>n==0?Legs[0].From:Legs[n-1].To;
    }
}
