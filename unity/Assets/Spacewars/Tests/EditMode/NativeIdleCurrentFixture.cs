using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Spacewars.Runtime;
using Spacewars.Simulation;

internal static class NativeIdleCurrentFixture
{
    internal static void Run(string output)
    {
        if(Directory.Exists(output))throw new ArgumentException("Output already exists");Directory.CreateDirectory(output);
        var profile=PlayableProfile.RecastGroupCorridorDefault;var geometry=new NavGeometry(32,Array.Empty<NavObstacle>(),2);
        NavigationSession hot=null,cold=null;IdleAuthority hotAudit=null,coldAudit=null;
        ulong native=RecastNative.sw_recast_allocation_count();long faults=RecastNative.ReleaseFaults;
        CheckBindingGuards(profile,geometry,output);
        byte[] Bytes(NavigationSession s){using var capture=s.CaptureState();return WorldWire.Pack(w=>WorldWire.Write(w,capture));}
        object Backing(NavigationSession s)=>new{Command=typeof(NavigationSession).GetField("groupCommandBacking",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(s)!=null,Arrival=typeof(NavigationSession).GetField("liveArrivalTables",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(s)!=null,s.LastNavigationStepPath,s.LastOwnedMovementNullReason,s.LastOwnedMovementNullDetail,s.OwnedMovementPublications,s.MotionOperationWatermark};
        try{
            hot=new NavigationSession(71,geometry,profile.Navigation){UsesRecastGroundRoutes=true};hot.ConfigureMarch(profile.GroupMarchMaximumStretch,profile.Revision);
            hot.Crowd.Add(1,new NavPoint(-4,0),.58,7,7.5);hot.Crowd.Add(2,new NavPoint(4,0),.58,7,7.5);
            hotAudit=new IdleAuthority(hot);
            for(int tick=1;tick<=15;tick++){hot.Step(1d/30);}
            var before=Bytes(hot);File.WriteAllBytes(Path.Combine(output,"before-nav14.bin"),before);var hotBefore=Backing(hot);
            cold=new NavigationSession(71,geometry,profile.Navigation){UsesRecastGroundRoutes=true};cold.ConfigureMarch(profile.GroupMarchMaximumStretch,profile.Revision);
            using(var parsed=WorldWire.Unpack(before,WorldWire.ReadNavigationSessionState))cold.RestoreState(parsed);
            coldAudit=new IdleAuthority(cold);var coldBefore=Backing(cold);
            if(!before.SequenceEqual(Bytes(cold)))throw new Exception("Immediate current roundtrip changed before the next operation");
            hot.Step(1d/30);cold.Step(1d/30);
            var a=Bytes(hot);var b=Bytes(cold);File.WriteAllBytes(Path.Combine(output,"hot-nav14.bin"),a);File.WriteAllBytes(Path.Combine(output,"cold-nav14.bin"),b);
            File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(new{Meaning="Native owning-boundary idle2/seedless/gen71/save15/next16, no AI private source or World/host service acceptance",Equal=a.SequenceEqual(b),BeforeBytes=before.Length,HotBytes=a.Length,ColdBytes=b.Length,HotBefore=hotBefore,ColdBefore=coldBefore,HotAfter=Backing(hot),ColdAfter=Backing(cold),HotPrepared=hotAudit.Prepared,ColdPrepared=coldAudit.Prepared,AllStationary=hot.Crowd.Units.All(u=>!u.Moving)&&cold.Crowd.Units.All(u=>!u.Moving)},Formatting.Indented));
            if(!a.SequenceEqual(b))throw new Exception("Native idle current hot/cold continuation differs");
        }finally{
            hotAudit?.Dispose();coldAudit?.Dispose();
            foreach(var s in new[]{hot,cold})if(s!=null){s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();var budget=s.PhysicalHostBudget;if(budget.PhysicalBytes!=0||budget.HeldBytes!=0||budget.ActiveScopes!=0)throw new Exception("Idle current budget leak");}
            var after=RecastNative.sw_recast_allocation_count();var fault=RecastNative.ReleaseFaults-faults;File.WriteAllText(Path.Combine(output,"cleanup.json"),JsonConvert.SerializeObject(new{NativeBefore=native,NativeAfter=after,NativeFaults=fault}));if(after!=native||fault!=0)throw new Exception("Idle current native leak");
        }
    }
    private static void CheckBindingGuards(PlayableProfile profile,NavGeometry geometry,string output)
    {
        var session=new NavigationSession(71,geometry,profile.Navigation){UsesRecastGroundRoutes=true};
        byte[] Bytes(){using var capture=session.CaptureState();return WorldWire.Pack(w=>WorldWire.Write(w,capture));}
        try{
            session.Crowd.Add(1,new NavPoint(-4,0),.58,7,7.5);session.Crowd.Add(2,new NavPoint(4,0),.58,7,7.5);
            var before=Bytes();var budget=session.PhysicalHostBudget;long physical=budget.PhysicalBytes,held=budget.HeldBytes;int scopes=budget.ActiveScopes;
            bool rejected=false;try{session.BindMotionAuthority(null);}catch(ArgumentNullException){rejected=true;}
            if(!rejected||!before.SequenceEqual(Bytes()))throw new Exception("Null binding changed Native state");
            int denials=0;bool refused=false;budget.AllocationAdmission=bytes=>{denials++;return false;};
            try{using var unused=new IdleAuthority(session);}catch(InvalidOperationException){refused=true;}finally{budget.AllocationAdmission=null;}
            if(!refused||denials==0||!before.SequenceEqual(Bytes())||budget.PhysicalBytes!=physical||budget.HeldBytes!=held||budget.ActiveScopes!=scopes)throw new Exception("Binding refusal changed source or leaked a reservation");
            using var authority=new IdleAuthority(session);var bound=Bytes();rejected=false;try{using var duplicate=new IdleAuthority(session);}catch(InvalidOperationException){rejected=true;}
            if(!rejected||!bound.SequenceEqual(Bytes()))throw new Exception("Duplicate binding changed Native state");
            File.WriteAllText(Path.Combine(output,"binding-guards.json"),JsonConvert.SerializeObject(new{NullRejected=true,ResourceDenied=true,denials,DuplicateRejected=true,SourceExact=true,PhysicalHeldScopesUnchanged=true},Formatting.Indented));
        }finally{session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();if(session.PhysicalHostBudget.PhysicalBytes!=0||session.PhysicalHostBudget.HeldBytes!=0||session.PhysicalHostBudget.ActiveScopes!=0)throw new Exception("Binding guard cleanup failed");}
    }
    private sealed class IdleAuthority : INavigationMotionAuthority,IDisposable
    {
        private readonly NavigationSession session;internal int Prepared,Committed;
        internal IdleAuthority(NavigationSession session){this.session=session;session.BindMotionAuthority(this);}
        public NavigationMotionAdoption Prepare(NavigationSession.MotionPublication publication)
        {
            Prepared++;
            if(publication.Transcript.Count!=session.Crowd.Units.Count)throw new Exception("Idle transcript omitted an actor");
            for(int i=0;i<publication.Transcript.Count;i++){var row=publication.Transcript[i];if(!row.Before.Position.Equals(row.After.Position)||row.Before.Heading!=row.After.Heading)throw new Exception("Idle proof changed physical motion");}
            return new Adoption(this,publication,publication.AcceptSource());
        }
        private sealed class Adoption : NavigationMotionAdoption
        {
            private readonly IdleAuthority owner;private readonly NavigationSession.MotionPublication publication;private NavigationSession.MotionSourcePin pin;
            internal Adoption(IdleAuthority owner,NavigationSession.MotionPublication publication,NavigationSession.MotionSourcePin pin){this.owner=owner;this.publication=publication;this.pin=pin;}
            internal override void ValidateCurrent(){if(publication.Committed||publication.Aborted)throw new Exception("Idle publication is not pending");}
            internal override void ApplyCommittedNoThrow(){if(!publication.Committed)throw new Exception("Idle publication has no committed receipt");owner.Committed++;}
            public override void Dispose(){if(pin==null)return;try{if(!owner.session.TryRetireMotionSource(pin))throw new Exception("Idle receipt retirement refused");}finally{pin.Dispose();pin=null;}}
        }
        public void Dispose(){}
    }

}
