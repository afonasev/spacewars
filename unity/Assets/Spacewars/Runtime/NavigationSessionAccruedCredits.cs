using System;
using System.Linq;
using Spacewars.Simulation;
using Bank=Spacewars.Runtime.NavigationAccruedCredits;
namespace Spacewars.Runtime
{
    internal enum NavigationCreditPromotion { NoCredit, Promoted, Blocked }
    // Concrete joint preparation, charged at its actual layout. A single scope
    // attaches/publishes this holder before atomically replacing both roots.
    internal sealed class NavigationCreditPublication : NavOwnedStorage, IDisposable
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationCreditPublication));
        internal NavigationHostRoot Host;internal NavPagedList<long> Bank;private bool released;
        private NavigationCreditPublication():base(Layout){Retain();}
        internal static NavigationCreditPublication Create(NavigationHostRoot host,NavPagedList<long> bank,INavStorageReservation r)
        {NavStorageAdmission.RequireOwned(r,host);NavStorageAdmission.RequireOwned(r,bank);using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);var p=a.Register(new NavigationCreditPublication());p.Host=NavPhysicalMacro.Acquire(p,host);p.Bank=NavPhysicalMacro.Acquire(p,bank);return p;}}
        public void Dispose(){if(released)return;released=true;Release();}internal override void ReleaseOwner()=>Dispose();protected override void ReleaseEdges(){Host?.Release();Bank?.Release();Host=null;Bank=null;}
    }
    public sealed partial class NavigationSession
    {
        private bool accruedCreditsEnabled;
        private int accruedCreditCap;
        private NavPagedList<long> creditBank;
        internal bool AccruedCreditsEnabled {get{using(EnterTransport())return accruedCreditsEnabled;}}
        internal NavPagedList<long> PinCreditBank(){using(EnterTransport()){creditBank?.Retain();return creditBank;}}
        internal void EnableAccruedCredits(int cap)
        {
            using(EnterTransport()){
                if(cap<1||cap>Bank.Maximum||committedHostRoot!=null||creditBank!=null||!tickQuantaEnabled)throw new InvalidOperationException("Accrued policy must be selected before host birth.");
                accruedCreditsEnabled=true;accruedCreditCap=cap;
            }
            try{InitializePhysicalHostForCapture();}catch{using(EnterTransport()){if(creditBank==null)accruedCreditsEnabled=false;}throw;}
        }
        // Called only under transportGate. Retained answers/history/ArrivalWork
        // without a live unfinished attempt are deliberately excluded.
        private bool HasUnfinishedCreditDemand()
        {
            var root=committedHostRoot;
            return pending.Count!=0||allocating.Count!=0||probes.Count!=0||Requests.Count!=0||root?.Rows.Count>0||root?.Scheduler.PendingCount>0||root?.Scheduler.ReadyCount>0||
                arrivalAttempts.Values.Any(a=>!a.Retiring&&!a.Published&&(a.SnapshotStatus==NavArrivalStatus.Pending||a.SnapshotStatus==NavArrivalStatus.BudgetExhausted));
        }
        internal bool AccountAccruedCredit(long upcomingTick,int budget,long revision,int cap,PlayableDomain authority=null)
        {
            NavPagedList<long> source=null,next=null,retired=null;NavigationRetentionBudget.Scope scope=null;long prefix,operation;PlayableProfile routing=authority?.RoutingProfile;bool demand,changed=false;
            using(EnterTransport()){
                if(!accruedCreditsEnabled||creditBank==null)throw new InvalidOperationException("Missing accrued authority bank.");
                source=creditBank;source.Retain();prefix=requestSequence;operation=authorityOperationIdentity;demand=HasUnfinishedCreditDemand();
            }
            try{
                if(source[Bank.Cap]==cap&&(!demand&&source[Bank.Count]==0||demand&&upcomingTick<=source[Bank.Watermark]))return true;
                if(!TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,Generation,Admission.PublicationIdentity,out scope))return false;
                next=Bank.Account(source,upcomingTick,prefix,budget,revision,cap,demand,scope.OwnedReservation());scope.Attach(next);
                scope.Publish(next,_=>{using(EnterTransport()){
                    if(authority!=null&&(authority.Tick+1!=upcomingTick||!ReferenceEquals(authority.RoutingProfile,routing)||routing.NavigationRequestsPerFrame!=budget||routing.Revision!=revision||routing.MaxAccruedCredits!=cap)||!ReferenceEquals(creditBank,source)||!ReferenceEquals(physicalHostBudget,scope.Identity)||operation!=authorityOperationIdentity||prefix!=requestSequence||demand!=HasUnfinishedCreditDemand())throw new PhysicalIngressRefused("Credit append lost authority operands.");
                    next.Retain();retired=creditBank;creditBank=next;accruedCreditCap=cap;changed=true;
                }});scope.ResolveCommitted();return true;
            }catch(InvalidOperationException e)when(e is PhysicalIngressRefused||IsPhysicalArrivalAdmissionRefusal(e)){return false;}
            finally{retired?.Release();next?.ReleaseStorage();scope?.Discard();source?.Release();if(changed)SignalHost();}
        }
        internal NavigationCreditPromotion TryPromoteAccruedCredit()
        {
            NavigationHostRoot source=null,next=null,retiredHost=null;NavPagedList<long> bank=null,nextBank=null,retiredBank=null;NavigationRetentionBudget.Scope scope=null;NavigationCreditPublication holder=null;
            using(EnterTransport()){
                if(!accruedCreditsEnabled||creditBank==null||creditBank[Bank.Count]==0||committedHostRoot==null||committedHostRoot.QuantumRunnable||committedHostRoot.Frontier.Stage!=CertifiedRouteKernel.PumpStage.Idle)return NavigationCreditPromotion.NoCredit;
                source=committedHostRoot;source.Retain();bank=creditBank;bank.Retain();
            }
            try{
                if(!TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,Generation,Admission.PublicationIdentity,out scope))return NavigationCreditPromotion.Blocked;
                var credit=Bank.First(bank);next=source.ForkOwned(scope.OwnedReservation());nextBank=Bank.Promote(bank,scope.OwnedReservation());
                if(next.TickQuantum&&next.QuantumSerial>next.ConsumedQuantum)next.ConsumeQuantum();
                next.IssueQuantum(Generation,credit.Tick,credit.Budget,credit.Prefix);next.SealPrepared();
                holder=NavigationCreditPublication.Create(next,nextBank,scope.OwnedReservation());scope.Attach(holder);
                scope.Publish(holder,_=>{using(EnterTransport()){
                    if(!ReferenceEquals(physicalHostBudget,scope.Identity)||!ReferenceEquals(committedHostRoot,source)||!ReferenceEquals(creditBank,bank))throw new PhysicalIngressRefused("Credit promotion lost source.");
                    next.Retain();nextBank.Retain();retiredHost=committedHostRoot;retiredBank=creditBank;committedHostRoot=next;creditBank=nextBank;
                }});scope.ResolveCommitted();return NavigationCreditPromotion.Promoted;
            }catch(InvalidOperationException e)when(e is PhysicalIngressRefused||IsPhysicalArrivalAdmissionRefusal(e)){return NavigationCreditPromotion.Blocked;}
            finally{retiredHost?.Release();retiredBank?.Release();holder?.Dispose();next?.ReleaseStorage();nextBank?.ReleaseStorage();scope?.Discard();source?.Release();bank?.Release();}
        }
    }
}
