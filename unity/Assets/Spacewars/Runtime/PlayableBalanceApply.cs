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
        internal void ApplyBalance(PlayableProfile candidate)
        {
            // Prepared immutable profile; the sole authority invokes this between ticks.
            BindTransactionTerms();profile=candidate;termProfiles[profile.Revision]=profile;
            foreach(var u in units.Values){u.Health=Math.Min(u.Health,PlayableUnitRules.Health(profile,u.Kind));navigation.Crowd.SetMobility(u.Id,PlayableUnitRules.Speed(profile,u.Kind,u.Kind==PlayableEntityKind.Tank&&UnitUpgraded(u)),PlayableUnitRules.Turn(profile,u.Kind));}
            foreach(var b in buildings.Values)b.Health=Math.Min(b.Health,TerritoryRules.Health(profile,b.Kind));
            foreach(var owner in Owners)RecordArmy(owner);
            SetRallyPaused(true);navigation.Rebind(profile.Navigation);navigation.ConfigureMarch(profile.GroupMarchMaximumStretch,profile.Revision);
        }
    }
}
