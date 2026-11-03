using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private sealed class LingeringVisionSource
        {
            internal int Team;
            internal NavPoint Point;
            internal double Radius, Duration, Remaining;
        }
        private readonly List<LingeringVisionSource> lingeringVision=new List<LingeringVisionSource>();

        private void AddLingeringVision(PlayableOwner owner,NavPoint point,double radius)
        {
            double duration=profile.LingeringVisionSeconds;
            if(duration<=0||radius<=0)return;
            lingeringVision.Add(new LingeringVisionSource{Team=TeamOf(owner),Point=point,Radius=radius,Duration=duration,Remaining=duration});
            visionInputProfile=null;
        }

        private void AdvanceLingeringVision(double dt)
        {
            if(lingeringVision.Count==0)return;
            foreach(var source in lingeringVision)source.Remaining-=dt;
            // Match tick arithmetic can leave a sub-nanosecond remainder at the deadline.
            lingeringVision.RemoveAll(source=>source.Remaining<=1e-9);
            // Expiration must invalidate too, including when the final circle vanished.
            visionInputProfile=null;
        }

        private void WriteLingeringVision(BinaryWriter w)
        {
            WorldWire.Array(w,lingeringVision.ToArray(),source=>{
                w.Write(source.Team);WorldWire.Write(w,source.Point);
                WorldWire.Number(w,source.Radius);WorldWire.Number(w,source.Duration);WorldWire.Number(w,source.Remaining);
            });
        }

        private void ReadLingeringVision(BinaryReader r)
        {
            int count=r.ReadInt32();
            if(count<0||count>WorldWire.MaxItems)throw new ArgumentException("Invalid lingering vision count.");
            for(int i=0;i<count;i++){
                var source=new LingeringVisionSource{Team=r.ReadInt32(),Point=WorldWire.ReadPoint(r),Radius=WorldWire.Number(r),Duration=WorldWire.Number(r),Remaining=WorldWire.Number(r)};
                if(!Owners.Any(owner=>TeamOf(owner)==source.Team)||source.Radius<=0||source.Duration<=0||source.Remaining<=0||source.Remaining>source.Duration||Math.Abs(source.Point.X)>profile.ArenaHalfExtent||Math.Abs(source.Point.Z)>profile.ArenaHalfExtent)
                    throw new ArgumentException("Invalid lingering vision source.");
                lingeringVision.Add(source);
            }
            visionInputProfile=null;
        }
    }
}
