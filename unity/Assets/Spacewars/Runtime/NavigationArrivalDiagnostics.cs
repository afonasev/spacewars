using System;
using System.IO;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public static class NavigationArrivalDiagnostics
    {
        private static NavigationRetentionBudget budget;
        public static bool Active=>NavArrivalDiagnostic.Buffer!=null;
        public static bool ShouldStop=>NavArrivalDiagnostic.Buffer?.ShouldStop??false;
        public static int Dropped=>NavArrivalDiagnostic.Buffer?.Dropped??0;
        public static void StartIfRequested()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(Environment.GetEnvironmentVariable("SPACEWARS_ARRIVAL_DIAGNOSTIC")=="1")Start(8192);
#endif
        }
        internal static void Start(int capacity)
        {
            if(Active)return;budget=new NavigationRetentionBudget();if(!budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope))throw new InvalidOperationException("Diagnostic admission refused.");
            using(scope){var buffer=NavArrivalDiagnosticBuffer.Create(capacity,scope.OwnedReservation());try{scope.Attach(buffer);scope.Commit(buffer,_=>{});NavArrivalDiagnostic.Buffer=buffer;}catch{buffer.ReleaseOwner();throw;}}
        }
        public static void Arm(Action requestStop=null){if(!Active)return;NavArrivalDiagnostic.RequestStop=requestStop;NavArrivalDiagnostic.Buffer.Arm();}
        public static void Save(string path)
        {if(!Active)return;using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write))NavArrivalDiagnostic.Buffer.WriteTo(stream);}
        public static void Stop()
        {var b=NavArrivalDiagnostic.Buffer;NavArrivalDiagnostic.Buffer=null;NavArrivalDiagnostic.RequestStop=null;b?.ReleaseOwner();if(budget!=null&&(budget.PhysicalBytes!=0||budget.HeldBytes!=0||budget.ActiveScopes!=0))throw new InvalidOperationException("Diagnostic owner leaked.");budget=null;}
    }
}
