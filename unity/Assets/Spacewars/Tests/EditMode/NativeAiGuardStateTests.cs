using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiInitiativeTests
    {
        [Test] public void QuietGuard_ActualAuthorityPendingAndMovementRetainExactCurrentState()
        {
            var c=Config(tanks:1,absent:true);var a=new PlayableAuthorityTick(c,71);PlayableAuthorityTick b=null;
            var planner=(AiArmyPlanner)Get(Owner(a),"armyPlanner");
            using(var host=new UnityHostRouteService())
            try
            {
                for(int i=0;i<240&&!planner.Active;i++)Step(a,host);
                Assert.True(planner.Active);Assert.AreEqual(PlayableRouteTargetKind.FriendlyAnchor,planner.Capture().TargetKind);Assert.Greater(planner.PendingId,0);
                var original=a.CaptureBytes();b=PlayableAuthorityTick.RestoreBytes(original,c);CollectionAssert.AreEqual(original,b.CaptureBytes());
                var initialTank=a.ParticipantView("west-owner").Entities.Single(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Tank);
                bool marched=false;
                for(int i=0;i<100;i++)
                {
                    Step(a,host);Step(b,host);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                    var tank=a.ParticipantView("west-owner").Entities.Single(u=>u.Id==initialTank.Id);
                    marched|=tank.OrderStamp?.Kind==PlayableCommandKind.AttackMove&&!tank.Position.Equals(initialTank.Position);
                }
                Assert.True(marched,"The applied defensive attack march produces actual motion.");
                Assert.True(a.CaptureDiagnosticCheckpoints().Single().Records.Any(r=>r.Policy==AiArmyPlanner.Policy&&r.Kind==PlayableCommandKind.AttackMove&&r.Status==PlayableAiDeliveryStatus.Applied));
                var data=AiProfile.Initial.CopyData();data.revision++;var next=new AiProfile(data);Assert.IsNull(a.ApplyProfile(c.Profile,next));
                var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,c,next);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());restored.Stop();
            }
            finally{a.Stop();b?.Stop();}
        }
    }
}
