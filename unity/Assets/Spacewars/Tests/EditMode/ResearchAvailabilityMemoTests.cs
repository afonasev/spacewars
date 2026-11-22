using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class ResearchAvailabilityMemoTests
    {
        static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static object Call(object d,string name,params object[] args)=>Domain.GetMethod(name,Private).Invoke(d,args);
        static void Set(object target,string field,object value)=>target.GetType().GetField(field,BindingFlags.Public|Private).SetValue(target,value);
        static object New(string name)=>Activator.CreateInstance(Domain.GetNestedType(name,BindingFlags.NonPublic),true);
        static void Equal(PlayableResearchAvailabilitySnapshot[] expected,PlayableResearchAvailabilitySnapshot[] actual)
        {
            Assert.AreEqual(expected.Length,actual.Length);
            for(int i=0;i<expected.Length;i++)
            {Assert.AreEqual(expected[i].Kind,actual[i].Kind);Assert.AreEqual(expected[i].Cost,actual[i].Cost);Assert.AreEqual(expected[i].Available,actual[i].Available);Assert.AreEqual(expected[i].BlockedReason,actual[i].BlockedReason);}
        }
        [Test] public void RepeatedProjectionMatchesUncachedRulesAcrossBuildingResearchAndProfileTransitions()
        {
            var d=Activator.CreateInstance(Domain,Private,null,new object[]{PlayableProfile.Default,1L},null);
            var buildings=(IDictionary)Domain.GetField("buildings",Private).GetValue(d);buildings.Clear();
            var random=new Random(838049288);var kinds=(PlayableResearchKind[])Enum.GetValues(typeof(PlayableResearchKind));
            var types=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Hangar,PlayableBuildingKind.ScientificCenter,PlayableBuildingKind.Refinery};
            PlayableResearchAvailabilitySnapshot[] prior=null,priorValues=null;
            for(int step=0;step<256;step++)
            {
                int id=100+step%12;
                if(step%9==0)buildings.Remove(id);
                else
                {
                    var b=New("Building");Set(b,"Id",id);Set(b,"Owner",(PlayableOwner)random.Next(2));Set(b,"Kind",types[random.Next(types.Length)]);
                    Set(b,"Ready",random.Next(3)!=0);Set(b,"Health",random.Next(4)==0?0d:200d);Set(b,"Sale",random.Next(4)==0?New("SaleState"):null);buildings[id]=b;
                }
                if(step%4==0)
                {
                    var saved=new PlayableResearchSaveState{Orders=Enumerable.Range(0,random.Next(24)).Select(i=>new PlayableResearchSaveOrder{
                        Id=i+1,Owner=random.Next(2),Kind=kinds[random.Next(kinds.Length)],CenterId=100+random.Next(12),Active=random.Next(3)==0,Complete=random.Next(3)==0,Elapsed=random.NextDouble()*5,Duration=10}).ToArray()};
                    Call(d,"RestoreResearchState",saved);
                }
                if(step%17==0){var p=PlayableProfile.Default.CopyData();p.tankChassisCost+=step;Set(d,"profile",PlayableProfile.Create(p));}
                foreach(var owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy})
                {
                    var actual=(PlayableResearchAvailabilitySnapshot[])Call(d,"ResearchAvailability",owner);
                    Equal((PlayableResearchAvailabilitySnapshot[])Call(d,"BuildResearchAvailability",owner),actual);
                    Assert.AreSame(actual,Call(d,"ResearchAvailability",owner),"Unchanged read set must reuse immutable entries");
                    if(prior!=null)Equal(priorValues,prior);
                    prior=actual;priorValues=actual.Select(a=>new PlayableResearchAvailabilitySnapshot(a.Kind,a.Cost,a.Available,a.BlockedReason)).ToArray();
                }
            }
            Call(d,"RestoreResearchState",new PlayableResearchSaveState());buildings.Clear();
            foreach(var owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy})
                Equal((PlayableResearchAvailabilitySnapshot[])Call(d,"BuildResearchAvailability",owner),(PlayableResearchAvailabilitySnapshot[])Call(d,"ResearchAvailability",owner));
        }
    }
}
