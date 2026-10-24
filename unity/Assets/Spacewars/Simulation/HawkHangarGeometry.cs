namespace Spacewars.Simulation
{
    // Authored mesh anchors, not tunable flight/balance values. Shared by the
    // elevator presentation and aircraft birth so the handoff has one height.
    public static class HawkHangarGeometry
    {
        public const double ModelHeight=1.25;
        public const double AuthorHeight=1.754;
        public const double RestPlatformTop=.19*ModelHeight/AuthorHeight;
        public const double RaisedPlatformTop=1.58*ModelHeight/AuthorHeight;
        public const double HawkSoleHeight=.065;
        public static double PlatformHeight(PlayableProfile p)=>RaisedPlatformTop/ModelHeight*p.HangarModelHeightMeters*p.HangarModelScale;
        public static double LaunchHeight(PlayableProfile p)=>PlatformHeight(p)-HawkSoleHeight*p.HawkModelScale;
    }
}
