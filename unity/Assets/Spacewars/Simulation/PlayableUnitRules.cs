namespace Spacewars.Simulation
{
    // Immutable kind dispatch shared by simulation and native presentation.
    public static class PlayableUnitRules
    {
        public static bool Supported(PlayableEntityKind k)=>k==PlayableEntityKind.Tank||k==PlayableEntityKind.Explorer||k==PlayableEntityKind.Shkval;
        public static double Radius(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Shkval?p.ShkvalCollisionRadius:k==PlayableEntityKind.Explorer?p.ExplorerCollisionRadius:p.TankCollisionRadius;
        public static double Speed(PlayableProfile p,PlayableEntityKind k,bool chassis=false)=>k==PlayableEntityKind.Shkval?p.ShkvalSpeed:k==PlayableEntityKind.Explorer?p.ExplorerSpeed:chassis?p.TankChassisSpeed:p.TankSpeed;
        public static double Turn(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Shkval?p.ShkvalTurnSpeed:k==PlayableEntityKind.Explorer?p.ExplorerTurnSpeed:p.TankTurnSpeed;
        public static double Range(PlayableProfile p,PlayableEntityKind k,bool guidance=false)=>k==PlayableEntityKind.Shkval?(guidance?p.ShkvalGuidanceRange:p.ShkvalBaseRange):k==PlayableEntityKind.Explorer?p.ExplorerRange:p.TankRange;
        public static double Vision(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Shkval?p.ShkvalVision:k==PlayableEntityKind.Explorer?p.ExplorerVision:p.TankVisionRange;
        public static int Health(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Shkval?p.ShkvalHealth:k==PlayableEntityKind.Explorer?p.ExplorerHealth:p.TankHealth;
        public static int Cost(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Shkval?p.ShkvalCreditCost:k==PlayableEntityKind.Explorer?p.ExplorerCreditCost:p.TankCreditCost;
        public static int Population(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Shkval?p.ShkvalPopulationCost:k==PlayableEntityKind.Explorer?p.ExplorerPopulationCost:p.TankPopulationCost;
        public static double Duration(PlayableProfile p,PlayableEntityKind k)=>k==PlayableEntityKind.Shkval?p.ShkvalProductionDurationSec:k==PlayableEntityKind.Explorer?p.ExplorerProductionDurationSec:p.TankProductionSeconds;
    }
}
