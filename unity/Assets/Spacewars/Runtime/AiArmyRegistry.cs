using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using static Spacewars.Runtime.WorldWire;

namespace Spacewars.Runtime
{
    public enum AiArmyRole { Attack, MobileDefense, Raid, Escort, AlliedSupport, Scout, Transport }
    public enum AiArmyPhase { Forming, Staging, Advancing, Engaging, Regrouping, Retreating, Recovering, Disbanded }
    [Serializable] public sealed class AiArmyState
    {
        public long Id; public string OwnerId,TacticalOwner,Objective,WithdrawalCondition;
        public AiArmyRole Role; public AiArmyPhase Phase;
        public int[] Members=System.Array.Empty<int>(),Reinforcements=System.Array.Empty<int>();
        public int LeaderId,MinimumViableForce; public NavPoint? Anchor,Rally;
        public long CreatedTick,ProgressTick;
        public bool Major=>Role!=AiArmyRole.Scout&&Role!=AiArmyRole.Transport;
        public AiArmyState Copy()=>new AiArmyState{Id=Id,OwnerId=OwnerId,TacticalOwner=TacticalOwner,Objective=Objective,WithdrawalCondition=WithdrawalCondition,Role=Role,Phase=Phase,Members=Members.ToArray(),Reinforcements=Reinforcements.ToArray(),LeaderId=LeaderId,MinimumViableForce=MinimumViableForce,Anchor=Anchor,Rally=Rally,CreatedTick=CreatedTick,ProgressTick=ProgressTick};
    }
    [Serializable] public sealed class AiArmyRegistryState
    {
        public string OwnerId; public long Generation,NextId; public AiArmyState[] Armies;
    }
    // One authority-owned registry. All input is the owner's detached observation.
    // Creating a group or changing membership is an atomic admission, never a proposal side effect.
    public sealed class AiArmyRegistry
    {
        private readonly Dictionary<long,AiArmyState> armies=new Dictionary<long,AiArmyState>();
        private readonly Dictionary<int,long> membership=new Dictionary<int,long>();
        private long nextId=1;private int majorCap,scoutCap,minimum;
        public string OwnerId{get;} public long Generation{get;}
        public int MajorCap=>majorCap;
        internal bool HasReinforcements=>Active.Any(a=>a.Reinforcements.Length>0);
        public AiArmyRegistry(string owner,long generation,AiProfile profile,AiDifficulty difficulty)
        {if(string.IsNullOrWhiteSpace(owner)||generation<=0)throw new ArgumentException("Registry identity");OwnerId=owner;Generation=generation;Rebind(profile,difficulty);}
        public void Rebind(AiProfile profile,AiDifficulty difficulty)
        {
            if(profile==null)throw new ArgumentNullException(nameof(profile));
            majorCap=(int)profile.DifficultyValue(difficulty,"majorArmies");scoutCap=(int)profile.DifficultyValue(difficulty,"scoutAssignments");minimum=(int)profile.Value("armies.minimumUnits");
            // At a profile barrier retain valid IDs; deterministic excess groups release claims.
            foreach(var a in Active.Where(a=>a.Major).Skip(majorCap).Concat(Active.Where(a=>a.Role==AiArmyRole.Scout).Skip(scoutCap)).ToArray())Disband(a.Id);
            AssertInvariants();
        }
        private IEnumerable<AiArmyState> Active=>armies.Values.Where(a=>a.Phase!=AiArmyPhase.Disbanded).OrderBy(a=>a.Id);
        public AiArmyRegistryState Capture()=>new AiArmyRegistryState{OwnerId=OwnerId,Generation=Generation,NextId=nextId,Armies=armies.Values.OrderBy(a=>a.Id).Select(a=>a.Copy()).ToArray()};
        // Selected reads retain the same detached state (including disbanded rows).
        // Call again after an admission: never cache or expose the owned army.
        internal AiArmyState[] CaptureArmy(long id)=>armies.TryGetValue(id,out var army)?new[]{army.Copy()}:System.Array.Empty<AiArmyState>();
        // Synchronous owner-guard view only. Never retain this registry-backed
        // view in detached work; copy each required value under its input grant.
        internal readonly struct GuardView
        {
            readonly AiArmyState army;
            internal GuardView(AiArmyState army){this.army=army;}
            internal long Id=>army.Id;internal string OwnerId=>army.OwnerId;
            internal bool Major=>army.Major;internal AiArmyPhase Phase=>army.Phase;
            internal int MemberCount=>army.Members.Length;internal int ReinforcementCount=>army.Reinforcements.Length;
            internal int MemberAt(int index)=>army.Members[index];internal int ReinforcementAt(int index)=>army.Reinforcements[index];
            // Linear membership predicates retain the original semantics; callers
            // must account these counts rather than claim a constant-time scan.
            internal bool ContainsMember(int id)=>System.Array.IndexOf(army.Members,id)>=0;
            internal bool ContainsReinforcement(int id)=>System.Array.IndexOf(army.Reinforcements,id)>=0;
        }
        internal bool TryReadGuard(long id,out GuardView view)
        {if(armies.TryGetValue(id,out var army)){view=new GuardView(army);return true;}view=default;return false;}
        public long ArmyFor(int unit)=>membership.TryGetValue(unit,out var id)?id:0;
        // The validated registry owns both maps. Query the current assignment;
        // a detached deep capture is only needed when exporting army state.
        internal bool IsScoutMember(int unit)=>membership.TryGetValue(unit,out var id)&&armies[id].Role==AiArmyRole.Scout;
        internal bool CanYieldScoutToDefense(int unit)=>membership.TryGetValue(unit,out var id)&&
            armies.TryGetValue(id,out var a)&&a.Phase!=AiArmyPhase.Disbanded&&a.Role==AiArmyRole.Scout&&
            a.TacticalOwner==AiScoutPlanner.Policy&&a.Members.Length==1&&a.Members[0]==unit;
        // Singleton AI scouts may yield to one whole major army or a new
        // defense slot. Transport and cross-major handoffs remain forbidden.
        internal bool CanAdmitDefense(IEnumerable<int> members)
        {
            var ids=members.Distinct().ToArray();
            var assigned=ids.Select(ArmyFor).Where(id=>id!=0).Distinct().Select(id=>armies[id]).ToArray();
            if(assigned.Any(a=>!a.Major&&!a.Members.All(CanYieldScoutToDefense)))return false;
            var majors=assigned.Where(a=>a.Major).ToArray();
            return majors.Length<=1&&(majors.Length==0?Active.Count(a=>a.Major)<majorCap:
                majors[0].TacticalOwner==AiDefensePlanner.Policy||majors[0].Members.All(ids.Contains));
        }
        public bool TryReinforce(PlayableAiObservation o,long id,string tacticalOwner,IEnumerable<int> units)
        {
            if(!Binding(o)||units==null||!armies.TryGetValue(id,out var a)||!a.Major||a.Phase==AiArmyPhase.Disbanded||a.TacticalOwner!=tacticalOwner)return false;
            var ids=units.OrderBy(x=>x).ToArray();var catalog=AiRosterCatalog.Initial;
            if(ids.Length==0||ids.Distinct().Count()!=ids.Length||ids.Any(membership.ContainsKey)||ids.Any(x=>!o.Entities.Any(u=>u.Id==x&&u.Owner==o.Owner&&u.Health>0&&catalog.LineWeight(u.Kind)+catalog.SupportWeight(u.Kind)>0)))return false;
            a.Members=a.Members.Concat(ids).OrderBy(x=>x).ToArray();a.Reinforcements=a.Reinforcements.Concat(ids).OrderBy(x=>x).ToArray();
            foreach(var unit in ids)membership.Add(unit,id);AssertInvariants();return true;
        }
        internal void ReleaseMembers(long id,string tacticalOwner,IEnumerable<int> ids)
        {
            if(!armies.TryGetValue(id,out var a)||a.Phase==AiArmyPhase.Disbanded||a.TacticalOwner!=tacticalOwner)return;
            var released=new HashSet<int>(ids);
            foreach(var unit in a.Members.Where(released.Contains))membership.Remove(unit);
            a.Members=a.Members.Where(unit=>!released.Contains(unit)).ToArray();a.Reinforcements=a.Reinforcements.Where(unit=>!released.Contains(unit)).ToArray();
            if(a.Members.Length==0)Disband(id);else if(!a.Members.Contains(a.LeaderId))a.LeaderId=a.Members[0];
            AssertInvariants();
        }
        internal void JoinReinforcements(long id,string tacticalOwner,IEnumerable<int> ids)
        {if(armies.TryGetValue(id,out var a)&&a.TacticalOwner==tacticalOwner){a.Reinforcements=a.Reinforcements.Except(ids).ToArray();AssertInvariants();}}
        internal bool BeginRepairReturn(long id,string tacticalOwner,int unit)
        {
            if(!armies.TryGetValue(id,out var a)||a.TacticalOwner!=tacticalOwner||a.Phase==AiArmyPhase.Disbanded||!a.Members.Contains(unit)||a.LeaderId==unit)return false;
            a.Reinforcements=a.Reinforcements.Append(unit).Distinct().OrderBy(x=>x).ToArray();AssertInvariants();return true;
        }
        private bool Binding(PlayableAiObservation o)=>o!=null&&o.OwnerId==OwnerId&&o.Generation==Generation;
        private bool Units(PlayableAiObservation o,int[] ids,AiArmyRole role,AiRosterCatalog catalog,int? required=null)
        {
            if(ids.Length==0||ids.Any(id=>id<=0)||ids.Distinct().Count()!=ids.Length)return false;
            var own=o.Entities.Where(e=>e.Owner==o.Owner&&e.Health>0).ToDictionary(e=>e.Id);
            if(ids.Any(id=>!own.ContainsKey(id)))return false;
            return role==AiArmyRole.Scout||role==AiArmyRole.Transport?ids.All(id=>catalog.ReconWeight(own[id].Kind)>0):ids.Count(id=>catalog.LineWeight(own[id].Kind)+catalog.SupportWeight(own[id].Kind)>0)>=(required??minimum)&&ids.Any(id=>catalog.LineWeight(own[id].Kind)>0);
        }
        public bool TryCreate(PlayableAiObservation o,AiArmyRole role,string tacticalOwner,IEnumerable<int> members,out long id,AiRosterCatalog catalog=null,int? requiredMinimum=null)
        {
            id=0;if(!Binding(o)||!Enum.IsDefined(typeof(AiArmyRole),role)||string.IsNullOrWhiteSpace(tacticalOwner)||members==null)return false;
            var units=members.OrderBy(x=>x).ToArray();bool major=role!=AiArmyRole.Scout&&role!=AiArmyRole.Transport;
            int required=role==AiArmyRole.MobileDefense&&(tacticalOwner==AiDefensePlanner.Policy||AiArmyPlanner.IsArmyPolicy(tacticalOwner))?1:AiArmyPlanner.IsArmyPolicy(tacticalOwner)&&requiredMinimum.HasValue?Math.Max(1,requiredMinimum.Value):minimum;
            if((major&&Active.Count(a=>a.Major)>=majorCap)||(role==AiArmyRole.Scout&&Active.Count(a=>a.Role==role)>=scoutCap)||!Units(o,units,role,catalog??AiRosterCatalog.Initial,required)||units.Any(membership.ContainsKey))return false;
            id=nextId;nextId=checked(nextId+1);var a=new AiArmyState{Id=id,OwnerId=OwnerId,Role=role,TacticalOwner=tacticalOwner,Members=units,LeaderId=units[0],MinimumViableForce=major?required:1,CreatedTick=o.Tick,ProgressTick=o.Tick,Phase=AiArmyPhase.Forming};
            armies.Add(id,a);foreach(var unit in units)membership.Add(unit,id);AssertInvariants();return true;
        }
        public bool TryTransfer(PlayableAiObservation o,long from,long to,IEnumerable<int> units,AiRosterCatalog catalog=null)
        {
            if(!Binding(o)||from==to||units==null||!armies.TryGetValue(from,out var source)||!armies.TryGetValue(to,out var target)||source.Phase==AiArmyPhase.Disbanded||target.Phase==AiArmyPhase.Disbanded)return false;
            var ids=units.OrderBy(x=>x).ToArray();if(ids.Length==0||ids.Distinct().Count()!=ids.Length||ids.Any(x=>ArmyFor(x)!=from))return false;
            var left=source.Members.Except(ids).ToArray();var right=target.Members.Concat(ids).OrderBy(x=>x).ToArray();var c=catalog??AiRosterCatalog.Initial;
            if(!Units(o,left,source.Role,c)||!Units(o,right,target.Role,c))return false;
            source.Members=left;target.Members=right;source.Reinforcements=source.Reinforcements.Except(ids).ToArray();source.LeaderId=left[0];target.LeaderId=right[0];foreach(var unit in ids)membership[unit]=to;AssertInvariants();return true;
        }
        // Mission metadata can only be changed by the current tactical owner. Membership
        // remains exclusively managed by the atomic registry operations above.
        public bool UpdateMission(long id,string tacticalOwner,AiArmyPhase phase,long tick,string objective,NavPoint? anchor=null,NavPoint? rally=null,bool progressed=false)
        {
            if(!armies.TryGetValue(id,out var a)||a.Phase==AiArmyPhase.Disbanded||a.TacticalOwner!=tacticalOwner||
                !Enum.IsDefined(typeof(AiArmyPhase),phase)||phase==AiArmyPhase.Disbanded||tick<a.ProgressTick||!Finite(anchor)||!Finite(rally))return false;
            a.Phase=phase;a.Objective=objective;a.Anchor=anchor;a.Rally=rally;
            if(progressed)a.ProgressTick=tick;
            AssertInvariants();return true;
        }
        // Survival preemption changes the complete group's role/owner in one authority transaction.
        internal bool PreemptDefense(long id,System.Collections.Generic.IEnumerable<int> members)
        {
            if(!armies.TryGetValue(id,out var a)||!a.Major||a.Phase==AiArmyPhase.Disbanded||!a.Members.SequenceEqual(members.OrderBy(x=>x)))return false;
            a.Role=AiArmyRole.MobileDefense;a.TacticalOwner=AiDefensePlanner.Policy;AssertInvariants();return true;
        }
        public bool TryClaim(long id,string expectedOwner,string nextOwner)
        {if(string.IsNullOrWhiteSpace(nextOwner)||!armies.TryGetValue(id,out var a)||a.Phase==AiArmyPhase.Disbanded||a.TacticalOwner!=expectedOwner)return false;a.TacticalOwner=nextOwner;return true;}
        public void DisbandAll(){foreach(var a in Active.ToArray())Disband(a.Id);AssertInvariants();}
        public void Disband(long id)
        {if(!armies.TryGetValue(id,out var a)||a.Phase==AiArmyPhase.Disbanded)return;foreach(var unit in a.Members)membership.Remove(unit);a.Members=System.Array.Empty<int>();a.Reinforcements=System.Array.Empty<int>();a.LeaderId=0;a.Phase=AiArmyPhase.Disbanded;}
        public void Observe(PlayableAiObservation o)
        {
            if(!Binding(o))throw new ArgumentException("Registry observation identity");
            var live=new HashSet<int>(o.Entities.Where(e=>e.Owner==o.Owner&&e.Health>0).Select(e=>e.Id));
            foreach(var a in Active.ToArray())
            {foreach(var id in a.Members.Where(id=>!live.Contains(id)).ToArray())membership.Remove(id);a.Members=a.Members.Where(live.Contains).ToArray();a.Reinforcements=a.Reinforcements.Where(live.Contains).ToArray();if(a.Members.Length==0)Disband(a.Id);else a.LeaderId=a.Members.Contains(a.LeaderId)?a.LeaderId:a.Members[0];}
            AssertInvariants();
        }
        // Bound by the owner loop to its current observed proposal/persisted reservation.
        // No authorization callback means no exception to tactical ownership.
        internal Func<AiIntent,bool> CombatMicroAuthorization;
        public string Reject(AiIntent intent)
        {
            if(intent.Action.PlayerId!=OwnerId||intent.Action.Generation!=Generation)return "army owner/generation mismatch";
            bool micro=intent.Policy==AiCombatMicroPolicy.Policy;
            if(micro&&CombatMicroAuthorization?.Invoke(intent)!=true)return "micro reservation not authorized";
            bool defenseHandoff=intent.Policy==AiDefensePlanner.Policy&&intent.Priority>=2&&
                intent.Action.Kind==PlayableCommandKind.Attack&&CanAdmitDefense(intent.Action.EntityIds);
            foreach(var id in intent.Action.EntityIds)
            {
                if(!membership.TryGetValue(id,out var armyId))continue;
                var a=armies[armyId];
                bool preparationHandoff=intent.Policy=="expansion"&&a.Role==AiArmyRole.Scout&&a.TacticalOwner==AiScoutPlanner.Policy&&
                    a.Members.Length==1&&a.Members[0]==id&&intent.Action.EntityIds.SequenceEqual(new[]{id})&&
                    (intent.Action.Kind==PlayableCommandKind.Move||intent.Action.Kind==PlayableCommandKind.BuildAt)&&
                    AiScoutPlanner.IsPreparationObjective(a.Objective,intent.Action.SiteId);
                bool defenseAssembly=intent.Policy==AiDefensePlanner.Policy&&(intent.Action.Kind==PlayableCommandKind.Move||intent.Action.Kind==PlayableCommandKind.Follow)&&
                    a.TacticalOwner==AiDefensePlanner.Policy&&a.Major&&a.Reinforcements.Contains(id)&&intent.Action.EntityIds.All(unit=>membership.TryGetValue(unit,out var assigned)&&assigned==a.Id);
                bool defenseCoreAttack=intent.Policy==AiDefensePlanner.Policy&&intent.Action.Kind==PlayableCommandKind.Attack&&a.TacticalOwner==AiDefensePlanner.Policy&&a.Major&&
                    a.Members.Contains(id)&&!a.Reinforcements.Contains(id)&&intent.Action.EntityIds.All(unit=>membership.TryGetValue(unit,out var assigned)&&assigned==a.Id&&!a.Reinforcements.Contains(unit));
                if(a.TacticalOwner!=intent.Policy&&!micro&&!preparationHandoff&&!defenseAssembly&&!defenseCoreAttack&&!defenseHandoff)return "unit tactical owner conflict";
                if(!a.Major&&intent.Action.Kind!=PlayableCommandKind.Move&&intent.Action.Kind!=PlayableCommandKind.Hold&&intent.Action.Kind!=PlayableCommandKind.Stop&&
                    !defenseHandoff&&!(preparationHandoff&&intent.Action.Kind==PlayableCommandKind.BuildAt)&&!(a.Role==AiArmyRole.Transport&&intent.Action.Kind==PlayableCommandKind.BuildAt))return "scout/transport combat requires major army";
            }
            return null;
        }
        private static bool Finite(NavPoint? point)=>!point.HasValue||!double.IsNaN(point.Value.X)&&!double.IsInfinity(point.Value.X)&&!double.IsNaN(point.Value.Z)&&!double.IsInfinity(point.Value.Z);
        // Immutable enum metadata; the defined-value contract still covers every row.
        private static readonly HashSet<AiArmyRole> definedRoles=new HashSet<AiArmyRole>((AiArmyRole[])Enum.GetValues(typeof(AiArmyRole)));
        private static readonly HashSet<AiArmyPhase> definedPhases=new HashSet<AiArmyPhase>((AiArmyPhase[])Enum.GetValues(typeof(AiArmyPhase)));
        public void AssertInvariants()
        {
            if(!RegistryInvariantsHold())throw new InvalidOperationException("Army registry invariant");
        }
        private bool RegistryInvariantsHold()
        {
            if(nextId<1)return false;
            int majors=0,scouts=0;var claimed=new HashSet<int>();
            // Check all historical and live rows, all reinforcement rules, both caps
            // and exact ownership in one traversal. No validation is skipped.
            foreach(var a in armies.Values){
                if(a.Id<=0||a.Id>=nextId||a.OwnerId!=OwnerId||string.IsNullOrWhiteSpace(a.TacticalOwner)||
                    !definedRoles.Contains(a.Role)||!definedPhases.Contains(a.Phase)||a.CreatedTick<0||a.ProgressTick<a.CreatedTick||
                    a.MinimumViableForce<1||!Finite(a.Anchor)||!Finite(a.Rally))return false;
                int previous=0;
                foreach(int id in a.Reinforcements){
                    if(id<=previous||System.Array.IndexOf(a.Members,id)<0)return false;
                    previous=id;
                }
                if(a.Phase==AiArmyPhase.Disbanded){
                    if(a.Members.Length!=0||a.Reinforcements.Length!=0||a.LeaderId!=0)return false;
                    continue;
                }
                if(a.Major)majors++;if(a.Role==AiArmyRole.Scout)scouts++;
                if(a.Members.Length==0)return false;
                bool leader=false;
                foreach(int id in a.Members){
                    if(id<=0||!claimed.Add(id)||ArmyFor(id)!=a.Id)return false;
                    if(id==a.LeaderId)leader=true;
                }
                if(!leader)return false;
            }
            return majors<=majorCap&&scouts<=scoutCap&&membership.Count==claimed.Count;
        }
        public static AiArmyRegistry Restore(AiArmyRegistryState state,AiProfile profile,AiDifficulty difficulty,long tick)
        {
            if(state==null||state.Armies==null)throw new InvalidDataException("Missing army state");
            var r=new AiArmyRegistry(state.OwnerId,state.Generation,profile,difficulty){nextId=state.NextId};
            try{foreach(var a in state.Armies){if(a==null||a.Members==null||a.Reinforcements==null||a.CreatedTick>tick||a.ProgressTick>tick||!a.Members.SequenceEqual(a.Members.OrderBy(x=>x))||a.Members.Distinct().Count()!=a.Members.Length)throw new InvalidDataException("Invalid army state");r.armies.Add(a.Id,a.Copy());if(a.Phase!=AiArmyPhase.Disbanded)foreach(var id in a.Members)r.membership.Add(id,a.Id);}r.AssertInvariants();}
            catch(Exception e) when(e is ArgumentException||e is InvalidOperationException){throw new InvalidDataException("Invalid army registry",e);}return r;
        }
        internal void WriteState(BinaryWriter w)
        {
            String(w,OwnerId);w.Write(Generation);w.Write(nextId);WorldWire.Array(w,Capture().Armies,a=>{w.Write(a.Id);String(w,a.OwnerId);String(w,a.TacticalOwner);w.Write((int)a.Role);w.Write((int)a.Phase);WorldWire.Array(w,a.Members,x=>w.Write(x));WorldWire.Array(w,a.Reinforcements,x=>w.Write(x));w.Write(a.LeaderId);w.Write(a.MinimumViableForce);w.Write(a.CreatedTick);w.Write(a.ProgressTick);String(w,a.Objective);String(w,a.WithdrawalCondition);w.Write(a.Anchor.HasValue);if(a.Anchor.HasValue){Number(w,a.Anchor.Value.X);Number(w,a.Anchor.Value.Z);}w.Write(a.Rally.HasValue);if(a.Rally.HasValue){Number(w,a.Rally.Value.X);Number(w,a.Rally.Value.Z);}});
        }
        internal static AiArmyRegistry ReadState(BinaryReader r,AiProfile p,AiDifficulty d,long tick,string owner,long generation)
        {
            var state=new AiArmyRegistryState{OwnerId=String(r),Generation=r.ReadInt64(),NextId=r.ReadInt64(),Armies=WorldWire.Array(r,()=>new AiArmyState{Id=r.ReadInt64(),OwnerId=String(r),TacticalOwner=String(r),Role=EnumValue<AiArmyRole>(r),Phase=EnumValue<AiArmyPhase>(r),Members=WorldWire.Array(r,()=>r.ReadInt32()),Reinforcements=WorldWire.Array(r,()=>r.ReadInt32()),LeaderId=r.ReadInt32(),MinimumViableForce=r.ReadInt32(),CreatedTick=r.ReadInt64(),ProgressTick=r.ReadInt64(),Objective=String(r),WithdrawalCondition=String(r),Anchor=Boolean(r)?new NavPoint(Number(r),Number(r)):(NavPoint?)null,Rally=Boolean(r)?new NavPoint(Number(r),Number(r)):(NavPoint?)null})};
            AiStateWire.Require(state.OwnerId==owner&&state.Generation==generation,"army registry binding");return Restore(state,p,d,tick);
        }
    }
}

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        internal void ValidateAiArmies(AiArmyRegistry registry)
        {
            var owner=OwnerFor(registry.OwnerId);registry.AssertInvariants();
            AiStateWire.Require(registry.Generation==navigation.Generation&&registry.Capture().Armies.SelectMany(a=>a.Members).All(id=>units.TryGetValue(id,out var u)&&u.Owner==owner&&u.Health>0),"army world membership");
            var catalog=AiRosterCatalog.Initial;
            AiStateWire.Require(registry.Capture().Armies.Where(a=>!a.Major).SelectMany(a=>a.Members).All(id=>{var role=catalog.For(units[id].Kind);return role.reconWeight>0;}),"army scout/transport world classification");
        }
    }
}
