using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Deterministic geometric policy. The authority owns all mutable progress;
    // the crowd still chooses and validates the actual step on its installed route.
    internal static class GroupMarch
    {
        internal static double Length(NavCorridorDescriptor corridor)
        {
            double length=0;foreach(var leg in corridor.Legs){
                double dx=leg.To.Position.X-leg.From.Position.X,dz=leg.To.Position.Z-leg.From.Position.Z;
                length+=Math.Sqrt(dx*dx+dz*dz);
            }
            return length;
        }
        internal static double Project(NavCorridorDescriptor corridor,NavLocation position,double previous)
        {
            double traveled=0,bestTravel=0,bestDistance=double.PositiveInfinity;
            foreach(var leg in corridor.Legs){
                double dx=leg.To.Position.X-leg.From.Position.X,dz=leg.To.Position.Z-leg.From.Position.Z;
                double length=Math.Sqrt(dx*dx+dz*dz);
                if(length>0&&leg.From.SurfaceId==position.SurfaceId){
                    double fraction=Math.Max(0,Math.Min(1,((position.Position.X-leg.From.Position.X)*dx+
                        (position.Position.Z-leg.From.Position.Z)*dz)/(length*length)));
                    double x=leg.From.Position.X+fraction*dx,z=leg.From.Position.Z+fraction*dz;
                    double distance=(position.Position.X-x)*(position.Position.X-x)+(position.Position.Z-z)*(position.Position.Z-z);
                    if(distance<bestDistance){bestDistance=distance;bestTravel=traveled+fraction*length;}
                }
                if(leg.To.SurfaceId==position.SurfaceId){
                    double ex=position.Position.X-leg.To.Position.X,ez=position.Position.Z-leg.To.Position.Z;
                    double distance=ex*ex+ez*ez;
                    if(distance<bestDistance){bestDistance=distance;bestTravel=traveled+length;}
                }
                traveled+=length;
            }
            // All members share zero at their assigned terminal. This avoids
            // comparing distances traveled from unrelated starting positions.
            // Detours may be off the centerline; never move the cursor backwards.
            return bestDistance==double.PositiveInfinity?(double.IsNegativeInfinity(previous)?-traveled:previous):
                Math.Max(previous,Math.Min(0,bestTravel-traveled));
        }

        // Shared by movement-only and projected result+movement preparation.
        // Actor rows and installed/group inputs are already private projections.
        internal static NavigationAuthorityGroup PrepareOwned(NavPreparedCrowdStep frame,NavigationAuthorityGroup source,NavigationInstalledExecutionOwner[] installed,bool[] detached,NavigationCommandSlots<int,long> memberGroups,double configuredStretch,int configuredRevision,double dt,INavStorageReservation reservation)
        {
            var data=source.Data;if(data.MarchVersion!=1||(data.Kind!=PlayableCommandKind.Move&&data.Kind!=PlayableCommandKind.AttackMove))return null;
            int count=source.Members.Count;using(NavStorageScratch.Reserve(reservation,checked(512L+32L*count))){
                var progress=new double[count];var requests=new long[count];var active=new bool[count];var rows=new int[count];
                double anchor=data.MarchAnchor,stretch=data.MarchProfileRevision==configuredRevision?data.MarchStretch:configuredStretch;bool initialized=data.MarchInitialized;int moving=0;double pace=double.PositiveInfinity,minimum=double.PositiveInfinity;
                for(int i=0;i<count;i++){
                    var member=((NavigationAuthorityMember)source.Members[i]).Data;progress[i]=member.Progress;requests[i]=member.ProgressRequest;int row=frame.Find(member.Entity);
                    if(member.FormationReleased||row<0||!InstalledActive(frame.After[row],source,member,installed[row],detached[row],memberGroups))continue;
                    var actor=frame.After[row];var execution=installed[row].Corridor.Corridor;
                    if(requests[i]!=execution.Request){progress[i]=double.NegativeInfinity;requests[i]=execution.Request;initialized=false;}
                    progress[i]=Project(execution,actor.Location,progress[i]);active[i]=true;rows[i]=row;moving++;pace=Math.Min(pace,actor.Speed);minimum=Math.Min(minimum,progress[i]);
                }
                if(moving<2){anchor=0;initialized=false;}
                else{anchor=Anchor(anchor,initialized,pace,dt,minimum);initialized=true;for(int i=0;i<count;i++)if(active[i]){int row=rows[i];frame.Preferred[row]=MaxSpeed(frame.After[row].Speed,pace,progress[i],anchor,stretch,dt);}}
                var values=new NavigationAuthorityGroup.Values(data.WorldGeneration,data.GroupId,data.OrderRevision,data.CommandSequence,data.IssuedTick,data.MembershipRevision,data.JobId,data.ActionId,data.MarchVersion,configuredRevision,data.TeamId,data.TargetId,stretch,anchor,initialized,data.Origin,data.Kind,data.OriginalGoal,data.HasLocation,data.Location);
                NavigationAuthorityGroup next=null;try{
                    for(int i=0;i<count;i++){
                        var before=(NavigationAuthorityMember)source.Members[i];var d=before.Data;var changedValues=new NavigationAuthorityMember.Values(d.Entity,d.Slot,d.Incarnation,d.OrderRevision,d.MobilityRevision,d.Phase,d.Goal,d.Location,d.HasLocation,d.HasGoal,d.FormationReleased,d.Branch,d.Reservation,d.ContactTick,requests[i],progress[i]);if(changedValues.BitwiseSame(d))continue;
                        var changed=NavigationAuthorityMember.CopyWithValuesOwned(before,changedValues,reservation);try{var version=NavigationAuthorityGroup.CopyWithMemberAndMarchOwned(next??source,i,changed,values,reservation);next?.ReleaseStorage();next=version;}finally{changed.ReleaseStorage();}
                    }
                    if(next==null&&!values.BitwiseSame(data))next=NavigationAuthorityGroup.CopyWithValuesOwned(source,values,reservation);
                    var result=next;next=null;return result;
                }finally{next?.ReleaseStorage();}
            }
        }
        private static bool InstalledActive(NavUnit actor,NavigationAuthorityGroup group,NavigationAuthorityMember.Values member,NavigationInstalledExecutionOwner owner,bool detached,NavigationCommandSlots<int,long> memberGroups)
        {
            if(owner==null||detached||!actor.Moving||actor.Held)return false;var d=owner.Corridor.Corridor;
            if(actor.InstalledGroupId!=d.Group||actor.InstalledRevision!=d.ActivationRevision||actor.InstalledRequest!=d.Request||actor.InstalledOrder!=d.Order||actor.Incarnation!=d.Incarnation||actor.MobilityRevision!=d.Mobility||!memberGroups.TryGetValue(actor.Id,out var g)||g!=d.Group||group.Data.OrderRevision!=d.RootRevision||member.OrderRevision!=d.ActivationRevision||owner.Route.Count!=actor.route.Length)return false;
            for(int i=0;i<actor.route.Length;i++)if(!owner.Route[i].Equals(actor.route[i]))return false;return true;
        }
        internal static double Anchor(double previous,bool initialized,double slowest,double dt,double minimumProgress)
            =>initialized?Math.Min(minimumProgress,previous+slowest*dt):minimumProgress;

        internal static double MaxSpeed(double actorSpeed,double slowest,double progress,double anchor,double stretch,double dt)
            =>Math.Min(actorSpeed,Math.Min(slowest,Math.Max(0,(anchor+stretch-progress)/dt)));
    }

    public sealed partial class NavigationSession
    {
        private bool marchConfigured;
        private double marchStretch;
        private int marchProfileRevision;

        private static void ValidateMarchConfiguration(double maximumStretch,int profileRevision)
        {
            var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="groupMarch.maximumStretch");
            double steps=(maximumStretch-field.Minimum)/field.Step;
            if(double.IsNaN(maximumStretch)||double.IsInfinity(maximumStretch)||
                maximumStretch<field.Minimum||maximumStretch>field.Maximum||
                Math.Abs(steps-Math.Round(steps))>1e-8||profileRevision<1)
                throw new ArgumentException("Invalid group march profile.");
        }
        public void ConfigureMarch(double maximumStretch,int profileRevision)
        {
            ValidateMarchConfiguration(maximumStretch,profileRevision);
            lock(transportGate){authorityOperationIdentity=checked(authorityOperationIdentity+1);marchStretch=maximumStretch;marchProfileRevision=profileRevision;marchConfigured=true;}
        }

        private void ValidateMarchCheckpoint()
        {
            foreach(var group in groups.Values){
                if(group.MarchVersion!=1)continue;
                double longestBoundRoute=0;
                foreach(var member in group.Members){
                    if(member.ProgressRequest==0){
                        if(member.Progress!=0)throw new ArgumentException("Unbound march progress.");
                        continue;
                    }
                    if(!installedExecutions.TryGetValue(member.Entity,out var installed)||
                        installed.Corridor.Group!=group.GroupId||
                        installed.Corridor.Request!=member.ProgressRequest||
                        installed.Corridor.Incarnation!=member.Incarnation||
                        installed.Corridor.ActivationRevision!=member.OrderRevision||
                        member.Progress < -GroupMarch.Length(installed.Corridor)-1e-6||member.Progress>0)
                        throw new ArgumentException("Invalid installed march progress binding.");
                    longestBoundRoute=Math.Max(longestBoundRoute,GroupMarch.Length(installed.Corridor));
                }
                if(group.MarchInitialized&&(longestBoundRoute<=0||group.MarchAnchor < -longestBoundRoute-1e-6))
                    throw new ArgumentException("March anchor is outside installed route geometry.");
            }
        }

        private void PrepareMarch(double dt)
        {
            Crowd.ClearPreferredMotion();
            if(!marchConfigured)return;
            foreach(var group in groups.Values.OrderBy(g=>g.GroupId)){
                if(group.MarchVersion!=1||(group.Kind!=PlayableCommandKind.Move&&group.Kind!=PlayableCommandKind.AttackMove))continue;
                if(group.MarchProfileRevision!=marchProfileRevision){
                    group.MarchProfileRevision=marchProfileRevision;group.MarchStretch=marchStretch;
                }
                var moving=new List<Tuple<GroupMemberState,NavUnit,NavigationInstalledExecutionState>>();
                foreach(var member in group.Members){
                    if(member.FormationReleased||!Crowd.TryGet(member.Entity,out var actor)||!actor.Moving||actor.Held||
                        !TryActiveInstalledExecution(member.Entity,out var execution))continue;
                    if(member.ProgressRequest!=execution.Corridor.Request){
                        member.Progress=double.NegativeInfinity;member.ProgressRequest=execution.Corridor.Request;
                        group.MarchInitialized=false;
                    }
                    member.Progress=GroupMarch.Project(execution.Corridor,actor.Location,member.Progress);
                    moving.Add(Tuple.Create(member,actor,execution));
                }
                if(moving.Count<2){group.MarchAnchor=0;group.MarchInitialized=false;continue;}
                double pace=moving.Min(x=>x.Item2.Speed);
                double minimum=moving.Min(x=>x.Item1.Progress);
                group.MarchAnchor=GroupMarch.Anchor(group.MarchAnchor,group.MarchInitialized,pace,dt,minimum);
                group.MarchInitialized=true;
                foreach(var row in moving)Crowd.PreferMaxSpeed(row.Item2.Id,
                    GroupMarch.MaxSpeed(row.Item2.Speed,pace,row.Item1.Progress,group.MarchAnchor,group.MarchStretch,dt));
            }
        }
    }
}
