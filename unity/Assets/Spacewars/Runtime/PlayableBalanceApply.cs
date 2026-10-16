using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private readonly Dictionary<int,PlayableProfile> termProfiles=new Dictionary<int,PlayableProfile>();
        private PlayableProfile Terms(int revision)=>revision==0||revision==profile.Revision?profile:termProfiles.TryGetValue(revision,out var bound)?bound:throw new ArgumentException("Unknown transaction revision.");
        private void BindTransactionTerms()
        {
            termProfiles[profile.Revision]=profile;
            foreach(var b in buildings.Values){if(b.TermsRevision==0)b.TermsRevision=profile.Revision;foreach(var o in b.Orders)if(o.TermsRevision==0)o.TermsRevision=profile.Revision;
                if(b.Upgrade!=null&&b.Upgrade.TermsRevision==0)b.Upgrade.TermsRevision=profile.Revision;
                if(b.Sale!=null&&b.Sale.TermsRevision==0)b.Sale.TermsRevision=profile.Revision;
                if(b.Repair!=null&&b.Repair.TermsRevision==0)b.Repair.TermsRevision=profile.Revision;}
            foreach(var p in projectiles)if(p.TermsRevision==0)p.TermsRevision=profile.Revision;
        }
        internal int CurrentBalanceRevision=>profile.Revision;
        internal string ValidateBalance(PlayableProfile candidate)=>ValidateAuthorityBalance(candidate,false);
        internal string ValidateAuthorityBalance(PlayableProfile candidate,bool allowConfigured)
        {
            if(candidate==null)return "Ревизия отсутствует.";
            var error=NativeBalanceFields.ValidateLiveDifference(profile,candidate);if(error!=null)return error;
            if(!navigation.CanRebind)return "Выполняется слишком много маршрутов. Продолжите матч и повторите применение.";
            if(Outcome!=PlayableMatchOutcome.Playing)return "Матч уже завершён.";
            if(offline!=null&&!allowConfigured)return "Этот диагностический режим не поддерживает лабораторию.";
            if(candidate.Revision==profile.Revision&&!WorldWire.Binding(profile).SequenceEqual(WorldWire.Binding(candidate)))return "Номер текущей ревизии уже занят.";
            if(termProfiles.TryGetValue(candidate.Revision,out var prior)&&!WorldWire.Binding(prior).SequenceEqual(WorldWire.Binding(candidate)))return "Номер ревизии уже связан с другими параметрами.";
            foreach(var owner in Owners){int living=units.Values.Where(u=>u.Owner==owner).Sum(u=>PlayableUnitRules.Population(candidate,u.Kind));int reserved=buildings.Values.Where(b=>b.Owner==owner).SelectMany(b=>b.Orders).Where(o=>o.Active).Sum(o=>PlayableUnitRules.Population(candidate,o.Kind));if(living+reserved>candidate.ArmyCapacity)return "Лимит армии ниже занятой вместимости.";}
            if(tacticalQueues.Values.Any(q=>q.Deferred.Count>candidate.UnitOrderQueueLimit))return "Лимит приказов ниже уже принятой очереди.";
            return null;
        }
        internal const string BalancePreparationRefusedMessage="Ревизия не применена. Повторите попытку.";
        internal void ApplyBalance(PlayableProfile candidate)
        {if(!TryApplyBalance(candidate))throw new InvalidOperationException(BalancePreparationRefusedMessage);}
        internal bool TryApplyBalance(PlayableProfile candidate)
        {
            var prepared=navigation.TryRebindBalance(this,candidate);if(prepared.HasValue)return prepared.Value;
            ApplyBalanceTerms(candidate);
            SetRallyPaused(true);navigation.Rebind(profile.Navigation);navigation.ConfigureMarch(profile.GroupMarchMaximumStretch,profile.Revision);return true;
        }
        internal PlayableProfile PreparedBalanceSource=>profile;
        internal bool CanCommitPreparedBalance(PlayableProfile source)=>ReferenceEquals(profile,source);
        internal int PreparedBalanceRallyCount=>rallyWork.Count;
        internal bool PreparedBalanceRallyPaused=>rallyPaused;
        internal readonly struct BalanceRallySource
        {
            internal readonly int Slot,Building,Kind,Candidate;internal readonly object Work,Intent;internal readonly NavGeometry Geometry;
            internal readonly long Request,Generation,Sequence;internal readonly PlayableOwner Owner;internal readonly double X,Z;
            internal BalanceRallySource(int slot,int building,object work,object intent,int kind,int candidate,NavGeometry geometry,long request,long generation,long sequence,PlayableOwner owner,double x,double z){Slot=slot;Building=building;Work=work;Intent=intent;Kind=kind;Candidate=candidate;Geometry=geometry;Request=request;Generation=generation;Sequence=sequence;Owner=owner;X=x;Z=z;}
        }
        internal void ObservePreparedBalanceRallies(BalanceRallySource[] rows)
        {
            if(rows.Length!=rallyWork.Count)throw new InvalidOperationException("Balance rally observation changed.");int n=0;
            for(int slot=0;slot<rallyWork.SlotCount;slot++)if(rallyWork.TryReadSlot(slot,out int id,out var w)){var i=w.Intent;rows[n++]=new BalanceRallySource(slot,id,w,i,w.Kind,w.Candidate,w.Geometry,w.RequestSequence,i.Generation,i.Sequence,i.Owner,i.TargetX,i.TargetZ);}
        }
        internal bool PreparedBalanceRalliesCurrent(BalanceRallySource[] rows,bool paused)
        {
            if(rallyPaused!=paused||rallyWork.Count!=rows.Length)return false;
            for(int n=0;n<rows.Length;n++){var r=rows[n];if(!rallyWork.TryReadSlot(r.Slot,out int id,out var w)||id!=r.Building||!ReferenceEquals(w,r.Work)||!ReferenceEquals(w.Intent,r.Intent)||w.Kind!=r.Kind||w.Candidate!=r.Candidate||!ReferenceEquals(w.Geometry,r.Geometry)||w.RequestSequence!=r.Request)return false;var i=w.Intent;if(i.Building!=r.Building||i.Generation!=r.Generation||i.Sequence!=r.Sequence||i.Owner!=r.Owner||BitConverter.DoubleToInt64Bits(i.TargetX)!=BitConverter.DoubleToInt64Bits(r.X)||BitConverter.DoubleToInt64Bits(i.TargetZ)!=BitConverter.DoubleToInt64Bits(r.Z))return false;}return true;
        }
        internal void ApplyPreparedBalanceRallyPauseNoThrow(BalanceRallySource[] rows,bool paused)
        {if(!paused)for(int n=0;n<rows.Length;n++){var w=(RallyWork)rows[n].Work;w.RequestSequence=0;w.Geometry=null;w.Kind=w.Candidate=0;}rallyPaused=true;}
        internal readonly struct BalanceMobilitySource
        {
            internal readonly object Unit;internal readonly PlayableEntityKind Kind;internal readonly PlayableOwner Owner;internal readonly bool? Authored;internal readonly bool Upgraded;
            internal BalanceMobilitySource(object unit,PlayableEntityKind kind,PlayableOwner owner,bool? authored,bool upgraded){Unit=unit;Kind=kind;Owner=owner;Authored=authored;Upgraded=upgraded;}
        }
        private bool ReadBalanceUpgrade(Unit unit)
        {
            if(unit.AuthoredUpgrade.HasValue)return unit.AuthoredUpgrade.Value;
            if(!research.TryGetValue(unit.Owner,out var rows))return false;
            var kind=unit.Kind==PlayableEntityKind.Tank?PlayableResearchKind.TankChassis:unit.Kind==PlayableEntityKind.Explorer?PlayableResearchKind.ExplorerAssaultGuns:PlayableResearchKind.ShkvalGuidance;
            for(int i=0;i<rows.Count;i++)if(rows[i].Kind==kind&&rows[i].Complete)return true;return false;
        }
        internal bool TryBalanceMobility(int entity,PlayableProfile candidate,out BalanceMobilitySource source,out double speed,out double turn)
        {source=default;speed=turn=0;if(!units.TryGetValue(entity,out var unit))return false;bool upgraded=ReadBalanceUpgrade(unit);source=new BalanceMobilitySource(unit,unit.Kind,unit.Owner,unit.AuthoredUpgrade,upgraded);speed=PlayableUnitRules.Speed(candidate,unit.Kind,unit.Kind==PlayableEntityKind.Tank&&upgraded);turn=PlayableUnitRules.Turn(candidate,unit.Kind);return true;}
        internal bool BalanceMobilityCurrent(int entity,BalanceMobilitySource source)
        {return units.TryGetValue(entity,out var unit)&&ReferenceEquals(unit,source.Unit)&&unit.Kind==source.Kind&&unit.Owner==source.Owner&&unit.AuthoredUpgrade==source.Authored&&ReadBalanceUpgrade(unit)==source.Upgraded;}
        internal void FinishPreparedBalance(PlayableProfile candidate)
        {ApplyBalanceTerms(candidate);}
        private void ApplyBalanceTerms(PlayableProfile candidate)
        {
            // Called only after navigation admission succeeds in the prepared lane.
            BindTransactionTerms();profile=candidate;termProfiles[profile.Revision]=profile;
            foreach(var u in units.Values){u.Health=Math.Min(u.Health,PlayableUnitRules.Health(profile,u.Kind));navigation.Crowd.SetMobility(u.Id,PlayableUnitRules.Speed(profile,u.Kind,u.Kind==PlayableEntityKind.Tank&&UnitUpgraded(u)),PlayableUnitRules.Turn(profile,u.Kind));}
            foreach(var b in buildings.Values)b.Health=Math.Min(b.Health,TerritoryRules.Health(profile,b.Kind));
            foreach(var owner in Owners)RecordArmy(owner);
        }
    }
}
