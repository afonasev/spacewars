using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class FormationContactDetectionTests
    {
        static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static object Call(object d,string name,params object[] args)=>D.GetMethod(name,F).Invoke(d,args);
        static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);
        static T Bind<T>(object d,string name) where T:class=>Delegate.CreateDelegate(typeof(T),d,D.GetMethod(name,F)) as T;
        static Func<PlayableOwner,NavPoint,double,double,bool> Detect(object d)=>Bind<Func<PlayableOwner,NavPoint,double,double,bool>>(d,"FormationContact");
        // Exact original evaluation order; deliberately independent of the extracted helper.
        static bool Original(object d,PlayableOwner owner,NavPoint from,double range,double radius)
        {
            var nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(d);
            var visible=Bind<Func<PlayableOwner,int,bool>>(d,"VisibleTarget");
            var clear=Bind<Func<NavPoint,NavPoint,double,bool>>(d,"GroundFireClear");
            double Distance(NavPoint a,NavPoint b){double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
            foreach(var u in ((IDictionary)D.GetField("units",F).GetValue(d)).Values){int id=Field<int>(u,"Id");
                if(visible(owner,id)&&nav.Crowd.TryGet(id,out var enemy)&&Distance(from,enemy.Position)<=range&&clear(from,enemy.Position,radius))return true;}
            foreach(var b in ((IDictionary)D.GetField("buildings",F).GetValue(d)).Values){int id=Field<int>(b,"Id");var p=Field<NavPoint>(b,"Position");
                if(visible(owner,id)&&Distance(from,p)<=range&&clear(from,p,radius))return true;}
            return false;
        }
        [Test] public void ExactOriginalResultsAcrossTeamsVisibilityRangesAndColdCapture()
        {
            var config=OfflineParticipantAuthorityTests.Config(6);var a=new OfflineParticipantAuthority(config,71);var d=OfflineParticipantAuthorityTests.Domain(a);
            var geometry=(NavGeometry)D.GetProperty("Geometry",F).GetValue(d);var nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(d);int placed=0;
            for(int x=-22;x<=22;x+=6)for(int z=-22;z<=22;z+=6)if(geometry.IsFree(new NavPoint(x,z),config.Profile.TankCollisionRadius)&&nav.Crowd.Units.All(u=>{double dx=u.Position.X-x,dz=u.Position.Z-z;return dx*dx+dz*dz>Math.Pow(u.Radius+config.Profile.TankCollisionRadius,2);})&&placed<30){Call(d,"SpawnUnit",new NavPoint(x,z),(PlayableOwner)(placed%6),PlayableEntityKind.Tank);placed++;}
            Assert.AreEqual(30,placed);
            Call(d,"RefreshVision");var before=a.CaptureBytes();var detect=Detect(d);int yes=0,no=0;
            foreach(PlayableOwner owner in Enumerable.Range(0,6).Select(i=>(PlayableOwner)i))
            foreach(var from in new[]{new NavPoint(-22,-22),new NavPoint(0,0),new NavPoint(24,24)})
            foreach(double range in new[]{0d,4d,8d,16d,64d})foreach(double radius in new[]{0d,config.Profile.TankProjectileCollisionRadius}){
                bool expected=Original(d,owner,from,range,radius);Assert.AreEqual(expected,detect(owner,from,range,radius));if(expected)yes++;else no++;}
            Assert.Greater(yes,0);Assert.Greater(no,0);CollectionAssert.AreEqual(before,a.CaptureBytes());
            var cold=OfflineParticipantAuthority.Restore(before,config);var cd=OfflineParticipantAuthorityTests.Domain(cold);Call(cd,"RefreshVision");var coldDetect=Detect(cd);
            for(int i=0;i<6;i++)Assert.AreEqual(detect((PlayableOwner)i,new NavPoint(0,0),16,0),coldDetect((PlayableOwner)i,new NavPoint(0,0),16,0));
            CollectionAssert.AreEqual(before,cold.CaptureBytes());
        }
        [Test] public void PendingBuildingsMissingBodiesAndHiddenTargetsMatchOriginal()
        {
            var d=Activator.CreateInstance(D,F,null,new object[]{PlayableProfile.Default,1L,false},null);
            int own=(int)Call(d,"SpawnUnit",new NavPoint(-20,-20),PlayableOwner.Player,PlayableEntityKind.Explorer);
            int hidden=(int)Call(d,"SpawnUnit",new NavPoint(20,20),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            Call(d,"AddBuilding",PlayableOwner.Enemy,PlayableBuildingKind.Factory,new NavPoint(-18,-20),true);
            Call(d,"RefreshVision");var detect=Detect(d);var nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(d);
            var buildings=((IDictionary)D.GetField("buildings",F).GetValue(d)).Values.Cast<object>().ToArray();
            foreach(var phase in new[]{ConstructionPhase.Pending,ConstructionPhase.Ready}){
                foreach(var b in buildings)b.GetType().GetField("Phase").SetValue(b,phase);
                foreach(var p in new[]{new NavPoint(-20,-20),new NavPoint(20,20)})foreach(double r in new[]{1d,2d,2.0000000001,64d})Assert.AreEqual(Original(d,PlayableOwner.Player,p,r,0),detect(PlayableOwner.Player,p,r,0));
            }
            nav.Crowd.Remove(hidden);Assert.AreEqual(Original(d,PlayableOwner.Player,new NavPoint(20,20),8,0),detect(PlayableOwner.Player,new NavPoint(20,20),8,0));
        }
        [TestCase(PlayableCommandKind.Move)] [TestCase(PlayableCommandKind.AttackMove)]
        public void ContactPublicationTracksAppearanceAndRemovalWithoutChangingIntent(PlayableCommandKind kind)
        {
            var d=Activator.CreateInstance(D,F,null,new object[]{PlayableProfile.Default,1L,false},null);
            int own=(int)Call(d,"SpawnUnit",new NavPoint(-12,-12),PlayableOwner.Player,PlayableEntityKind.Tank);
            int enemy=(int)Call(d,"SpawnUnit",new NavPoint(-8,-12),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            var nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"Apply",new PlayableCommand(1,1,"player-1",kind,new[]{own},new NavPoint(-10,-12)),null));
            var initial=nav.GroupFor(own);Call(d,"RefreshVision");
            Assert.True(Original(d,PlayableOwner.Player,new NavPoint(-12,-12),PlayableProfile.Default.TankRange,PlayableProfile.Default.TankProjectileCollisionRadius));
            Call(d,"RefreshFormationContacts");var seen=nav.GroupFor(own);
            Assert.True(seen.Members.Single().FormationReleased);Assert.AreEqual("PostMovementVisibility",seen.Members.Single().ContactPhase);
            Assert.AreEqual(initial.Kind,seen.Kind);Assert.AreEqual(initial.OriginalGoal,seen.OriginalGoal);Assert.AreEqual(initial.CommandSequence,seen.CommandSequence);
            Call(d,"Damage",enemy,10000);Call(d,"RefreshVision");Assert.False(Original(d,PlayableOwner.Player,new NavPoint(-12,-12),PlayableProfile.Default.TankRange,PlayableProfile.Default.TankProjectileCollisionRadius));
            Call(d,"RefreshFormationContacts");var lost=nav.GroupFor(own);Assert.False(lost.Members.Single().FormationReleased);Assert.AreEqual("PostMovementVisibility",lost.Members.Single().ContactPhase);
            var bytes=(byte[])Call(d,"CaptureWorldBytes",7,"contact-prefilter");
            var cold=D.GetMethod("RestoreWorldBytes",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,"contact-prefilter"});
            CollectionAssert.AreEqual(bytes,(byte[])Call(cold,"CaptureWorldBytes",7,"contact-prefilter"));
        }
        [Test] public void ReportDetectionCostWithoutTimingThreshold()
        {
            var d=Activator.CreateInstance(D,F,null,new object[]{PlayableProfile.Default,1L,false},null);
            var geometry=(NavGeometry)D.GetProperty("Geometry",F).GetValue(d);int placed=0;
            for(int x=-25;x<=25;x+=5)for(int z=-25;z<=25;z+=5)if(geometry.IsFree(new NavPoint(x,z),PlayableProfile.Default.TankCollisionRadius)&&placed<64){Call(d,"SpawnUnit",new NavPoint(x,z),placed%2==0?PlayableOwner.Player:PlayableOwner.Enemy,PlayableEntityKind.Tank);placed++;}
            Assert.AreEqual(64,placed);
            Call(d,"RefreshVision");var detect=Detect(d);var points=Enumerable.Range(0,64).Select(i=>new NavPoint(-25+i%8*7,-25+i/8*7)).ToArray();
            for(int i=0;i<64;i++)Assert.AreEqual(Original(d,PlayableOwner.Player,points[i],6,0),detect(PlayableOwner.Player,points[i],6,0));
            const int count=1000;int hits=0;var watch=Stopwatch.StartNew();for(int n=0;n<count;n++)foreach(var p in points)if(detect(PlayableOwner.Player,p,6,0))hits++;watch.Stop();
            TestContext.WriteLine($"CONTACT_DETECT queries={count*points.Length} hits={hits} wall_ms={watch.Elapsed.TotalMilliseconds:F6} profile={PerformanceProfile.Enabled}");
        }
    }
}
