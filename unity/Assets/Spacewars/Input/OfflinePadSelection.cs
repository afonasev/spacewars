using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Spacewars.Input
{
    public readonly struct OfflinePadSelectable
    {
        public readonly int Id,Kind;
        public readonly bool Own,Visible;
        public readonly double X,Z,Radius;
        public OfflinePadSelectable(int id,int kind,bool own,double x,double z,double radius,bool visible)
        {Id=id;Kind=kind;Own=own;X=x;Z=z;Radius=radius;Visible=visible;}
    }

    // Controller-level A timing is separate from the lower gesture state machine.
    // Source cancel keeps lastA; preserve that behavior across focus/map transitions.
    public sealed class OfflinePadSelection
    {
        private double? lastA;
        public int[] Tap(double now,int hitId,IReadOnlyList<OfflinePadSelectable> units,double doubleTapMs)
        {
            bool same=lastA.HasValue&&now-lastA.Value<=doubleTapMs;
            lastA=now;
            var hit=units.FirstOrDefault(u=>u.Id==hitId);
            if(hitId==0||!hit.Own)return Array.Empty<int>();
            return same?units.Where(u=>u.Own&&u.Kind==hit.Kind&&u.Visible).Select(u=>u.Id).ToArray():new[]{hitId};
        }
        public static int Hit(IReadOnlyList<OfflinePadSelectable> units,double x,double z)
        {
            return units.Where(u=>Math.Sqrt((u.X-x)*(u.X-x)+(u.Z-z)*(u.Z-z))<=u.Radius)
                .OrderBy(u=>(u.X-x)*(u.X-x)+(u.Z-z)*(u.Z-z)).ThenBy(u=>u.Id.ToString(),StringComparer.Ordinal).Select(u=>u.Id).FirstOrDefault();
        }
        public static int[] Area(IReadOnlyList<OfflinePadSelectable> units,double x,double z,double radius,bool map)
        {
            return units.Where(u=>u.Own&&(map||u.Visible)&&Math.Sqrt((u.X-x)*(u.X-x)+(u.Z-z)*(u.Z-z))<=radius).Select(u=>u.Id).ToArray();
        }
        public static bool InViewport(Camera camera,Vector3 point)
        {
            var p=camera.WorldToViewportPoint(point);
            return p.x>=0&&p.x<=1&&p.y>=0&&p.y<=1&&p.z>=camera.nearClipPlane&&p.z<=camera.farClipPlane;
        }
    }
}
