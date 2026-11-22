using System;

namespace Spacewars.Simulation
{
    public static class ExplorerRocketGeometry
    {
        public const double PivotHeight = 1.0712;
        public const double PodOffset = .49;
        public const double PortHeight = .34;
        public const double PortForward = .369;
        public const double PivotRear = .28;

        public static double Scale(PlayableProfile profile) => profile.ExplorerModelRadius * profile.ExplorerModelScale / 1.0773;

        public static BallisticPoint Muzzle(PlayableProfile profile, NavPoint from, double pivotY,
            double yaw, double pitch, int side)
        {
            double scale = Scale(profile), lateral = (side == 0 ? 1 : -1) * PodOffset * scale;
            double forward = (PortForward * Math.Cos(pitch) - PortHeight * Math.Sin(pitch)) * scale;
            double up = (PortForward * Math.Sin(pitch) + PortHeight * Math.Cos(pitch)) * scale;
            return new BallisticPoint(from.X + Math.Cos(yaw) * forward - Math.Sin(yaw) * lateral,
                pivotY + up, from.Z + Math.Sin(yaw) * forward + Math.Cos(yaw) * lateral);
        }
    }
}
