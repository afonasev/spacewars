using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal enum RecastFormationPhase : byte { Collecting, Feeding, Recovering }
    internal enum RecastPassageStage : byte { Staging, Admitted, Recovering, Tombstone }
    internal readonly struct RecastFormationEntry
    {
        internal readonly int Entity,Slot,SpineIndex,EntryOrdinal,Lane;
        internal readonly RecastPassageStage Stage;
        internal readonly long Incarnation,Order,Mobility,Request;
        internal RecastFormationEntry(int entity,int slot,int spine,int ordinal,int lane,RecastPassageStage stage,long incarnation,long order,long mobility,long request)
        {Entity=entity;Slot=slot;SpineIndex=spine;EntryOrdinal=ordinal;Lane=lane;Stage=stage;Incarnation=incarnation;Order=order;Mobility=mobility;Request=request;}
        internal RecastFormationEntry WithStage(RecastPassageStage stage)=>new RecastFormationEntry(Entity,Slot,SpineIndex,EntryOrdinal,Lane,stage,Incarnation,Order,Mobility,Request);
        internal void Write(System.IO.BinaryWriter w){w.Write(Entity);w.Write(Slot);w.Write(SpineIndex);w.Write(EntryOrdinal);w.Write(Lane);w.Write((byte)Stage);w.Write(Incarnation);w.Write(Order);w.Write(Mobility);w.Write(Request);}
        internal static RecastFormationEntry Read(System.IO.BinaryReader r)=>new RecastFormationEntry(r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),(RecastPassageStage)r.ReadByte(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64());
    }
    // Accepted mapping is authority. Geometry is rebuilt from the retained,
    // knowledge-scoped providers; no inferred lane cache enters the checkpoint.
    internal sealed class RecastFormationPlan : NavOwnedStorage,INavPhysicalBody
    {
        internal readonly long World,Group,RootOrder,Command,BirthMembership;
        internal readonly int LaneCount;
        internal readonly RecastFormationPhase Phase;
        internal readonly double FrontAnchor;
        internal readonly bool AnchorInitialized;
        private RecastFormationEntry[] entries;private NavRecastSpine[] spines;
        private NavOwnedText entryPortal,exitPortal,seal;
        internal IReadOnlyList<RecastFormationEntry> Entries=>entries;
        internal IReadOnlyList<NavRecastSpine> Spines=>spines;
        internal string EntryPortal=>entryPortal.Value;internal string ExitPortal=>exitPortal.Value;internal string Seal=>seal.Value;
        private static long Align(long n)=>checked((n+7)/8*8);
        private static long Layout(int n,int m)=>checked(NavStorageLayout.ObjectBytesBound(typeof(RecastFormationPlan))+32+Align(56L*n)+32+Align(8L*m));
        internal override bool IsImmutableCaptureRoot=>seal!=null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.AuthorityGroup,value:NavPhysicalValueKind.RecastFormationPlanV1);
        private RecastFormationPlan(long world,long group,long root,long command,long membership,int lanes,RecastFormationPhase phase,double anchor,bool initialized,int n,int m):base(Layout(n,m))
        {World=world;Group=group;RootOrder=root;Command=command;BirthMembership=membership;LaneCount=lanes;Phase=phase;FrontAnchor=anchor;AnchorInitialized=initialized;}
        internal static RecastFormationPlan Create(long world,long group,long root,long command,long membership,int lanes,RecastFormationPhase phase,double anchor,bool initialized,string entry,string exit,IReadOnlyList<RecastFormationEntry> rows,IReadOnlyList<NavRecastSpine> paths,INavStorageReservation r,bool owner=true,string expectedSeal=null,NavOwnedText entryOwner=null,NavOwnedText exitOwner=null,NavOwnedText sealOwner=null)
        {
            if(world<1||group<1||root<1||command<0||membership<1||lanes<0||rows.Count<2||rows.Count>AuthoritySlotMap<int,int>.MaximumSlots||(byte)phase>2||!NavRecastSpine.Finite(anchor)||!initialized&&anchor!=0||phase==RecastFormationPhase.Collecting&&(lanes!=0||paths.Count!=0||initialized))throw new ArgumentException("Invalid formation dimensions.");
            foreach(var s in paths){NavStorageAdmission.RequireOwned(r,s);if(s.World!=world||s.Group!=group||s.Order!=root||s.Command!=command)throw new ArgumentException("Formation spine issuance changed.");}
            int previous=0;foreach(var e in rows){if(e.Entity<=previous||e.Slot<0||e.Incarnation<1||e.Order<root||e.Mobility<0||(byte)e.Stage>3||e.Request<0||phase==RecastFormationPhase.Collecting&&(e.SpineIndex!=-1||e.EntryOrdinal!=-1||e.Lane!=-1)||phase!=RecastFormationPhase.Collecting&&e.Stage!=RecastPassageStage.Tombstone&&(e.SpineIndex<0||e.SpineIndex>=paths.Count||e.EntryOrdinal<0||e.Lane<0||e.Lane>=lanes||e.Request<1))throw new ArgumentException("Invalid formation member mapping.");previous=e.Entity;}
            string digest=NavContract.Digest(w=>{w.Write("recast-formation-v1");w.Write(world);w.Write(group);w.Write(root);w.Write(command);w.Write(membership);w.Write(lanes);w.Write(entry??"");w.Write(exit??"");w.Write(paths.Count);foreach(var s in paths)w.Write(s.Binding);w.Write(rows.Count);foreach(var e in rows){w.Write(e.Entity);w.Write(e.Slot);w.Write(e.SpineIndex);w.Write(e.EntryOrdinal);w.Write(e.Lane);w.Write(e.Incarnation);w.Write(e.Order);w.Write(e.Mobility);w.Write(e.Request);}});
            if(expectedSeal!=null&&expectedSeal!=digest)throw new ArgumentException("Formation mapping seal changed.");
            NavOwnedText a=null,b=null,c=null;try{if(entryOwner!=null){entryOwner.Retain();a=entryOwner;}else a=NavOwnedText.Copy(entry??"",r);if(exitOwner!=null){exitOwner.Retain();b=exitOwner;}else b=NavOwnedText.Copy(exit??"",r);if(sealOwner!=null){sealOwner.Retain();c=sealOwner;}else c=NavOwnedText.Copy(digest,r);
                using(var alloc=new NavStorageAllocation(r.TryReserve)){alloc.Reserve(Layout(rows.Count,paths.Count));var p=alloc.Register(new RecastFormationPlan(world,group,root,command,membership,lanes,phase,anchor,initialized,rows.Count,paths.Count));
                    p.entries=new RecastFormationEntry[rows.Count];p.spines=new NavRecastSpine[paths.Count];for(int i=0;i<rows.Count;i++)p.entries[i]=rows[i];for(int i=0;i<paths.Count;i++)p.spines[i]=NavPhysicalMacro.Acquire(p,paths[i]);p.entryPortal=NavPhysicalMacro.Acquire(p,a);p.exitPortal=NavPhysicalMacro.Acquire(p,b);p.seal=NavPhysicalMacro.Acquire(p,c);if(owner)p.Retain();return p;}
            }finally{a?.Release();b?.Release();c?.Release();}
        }
        internal static RecastFormationPlan Collect(NavigationAuthorityGroup.Values d,IReadOnlyList<NavOwnedStorage> members,INavStorageReservation r)
        {using(NavStorageScratch.Reserve(r,checked(32+56L*members.Count))){var rows=new RecastFormationEntry[members.Count];for(int i=0;i<rows.Length;i++){var m=((NavigationAuthorityMember)members[i]).Data;rows[i]=new RecastFormationEntry(m.Entity,m.Slot,-1,-1,-1,RecastPassageStage.Staging,m.Incarnation,m.OrderRevision,m.MobilityRevision,0);}return Create(d.WorldGeneration,d.GroupId,d.OrderRevision,d.CommandSequence,d.MembershipRevision,0,RecastFormationPhase.Collecting,0,false,"","",rows,Array.Empty<NavRecastSpine>(),r);}}
        internal RecastFormationPlan Reconcile(NavigationAuthorityGroup.Values d,IReadOnlyList<NavOwnedStorage> members,INavStorageReservation r)
        {
            // Both immutable sequences are strictly ordered by entity. A gone member
            // or changed incarnation invalidates just that entry in the retained plan.
            int memberIndex=0;
            bool Present(RecastFormationEntry entry)
            {
                while(memberIndex<members.Count){
                    var member=((NavigationAuthorityMember)members[memberIndex]).Data;
                    if(member.Entity>entry.Entity)return false;
                    memberIndex++;
                    if(member.Entity==entry.Entity)return Matches(entry,member);
                }
                return false;
            }
            bool changed=false;
            foreach(var entry in entries)
                if(entry.Stage!=RecastPassageStage.Tombstone&&!Present(entry)){changed=true;break;}
            if(!changed)return null;
            using(NavStorageScratch.Reserve(r,checked(32+56L*entries.Length))){
                var rows=new RecastFormationEntry[entries.Length];
                memberIndex=0;
                for(int i=0;i<rows.Length;i++)
                    rows[i]=Present(entries[i])?entries[i]:entries[i].WithStage(RecastPassageStage.Tombstone);
                return Create(World,Group,RootOrder,Command,BirthMembership,LaneCount,Phase,FrontAnchor,AnchorInitialized,EntryPortal,ExitPortal,rows,spines,r,expectedSeal:Seal);
            }
        }

        private static bool Matches(RecastFormationEntry entry,in NavigationAuthorityMember.Values member)
            =>entry.Slot==member.Slot&&entry.Incarnation==member.Incarnation&&entry.Order==member.OrderRevision&&entry.Mobility==member.MobilityRevision;

        internal void ValidateGroup(NavigationAuthorityGroup.Values d,IReadOnlyList<NavOwnedStorage> members)
        {
            if(World!=d.WorldGeneration||Group!=d.GroupId||RootOrder!=d.OrderRevision||Command!=d.CommandSequence||BirthMembership>d.MembershipRevision)
                throw new ArgumentException("Formation group identity changed.");
            int entryIndex=0;
            foreach(var value in members){
                var member=((NavigationAuthorityMember)value).Data;
                while(entryIndex<entries.Length&&entries[entryIndex].Entity<member.Entity)entryIndex++;
                if(entryIndex==entries.Length||entries[entryIndex].Entity!=member.Entity){
                    if(d.MembershipRevision==BirthMembership)throw new ArgumentException("Formation member missing.");
                }else{
                    var entry=entries[entryIndex++];
                    if(entry.Stage!=RecastPassageStage.Tombstone&&!Matches(entry,member))
                        throw new ArgumentException("Formation member binding changed.");
                }
            }
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter w){w.Output.Write(1);w.Output.Write(World);w.Output.Write(Group);w.Output.Write(RootOrder);w.Output.Write(Command);w.Output.Write(BirthMembership);w.Output.Write(LaneCount);w.Output.Write((byte)Phase);w.Output.Write(FrontAnchor);w.Output.Write(AnchorInitialized);NavPhysicalMacro.WriteEdge(w,this,entryPortal);NavPhysicalMacro.WriteEdge(w,this,exitPortal);NavPhysicalMacro.WriteEdge(w,this,seal);w.Output.Write(spines.Length);foreach(var s in spines)NavPhysicalMacro.WriteEdge(w,this,s);w.Output.Write(entries.Length);foreach(var e in entries)e.Write(w.Output);}
        internal static RecastFormationPlan Read(NavPhysicalBodyReader r)
        {var b=r.Input;if(b.ReadInt32()!=1)throw new ArgumentException("Unsupported formation plan.");long w=b.ReadInt64(),g=b.ReadInt64(),o=b.ReadInt64(),c=b.ReadInt64(),m=b.ReadInt64();int lanes=b.ReadInt32();var phase=(RecastFormationPhase)b.ReadByte();double anchor=b.ReadDouble();bool initialized=NavPhysicalMacro.ReadBoolean(r);var entry=NavPhysicalMacro.ReadEdge<NavOwnedText>(r);var exit=NavPhysicalMacro.ReadEdge<NavOwnedText>(r);var seal=NavPhysicalMacro.ReadEdge<NavOwnedText>(r);int ns=b.ReadInt32();if(ns<0||ns>AuthoritySlotMap<int,int>.MaximumSlots)throw new ArgumentException("Invalid formation spine count.");using(NavStorageScratch.Reserve(r.Reservation,checked(64+8L*ns))){var paths=new NavRecastSpine[ns];for(int i=0;i<ns;i++)paths[i]=NavPhysicalMacro.ReadEdge<NavRecastSpine>(r);int n=b.ReadInt32();if(n<2||n>AuthoritySlotMap<int,int>.MaximumSlots)throw new ArgumentException("Invalid formation entry count.");using(NavStorageScratch.Reserve(r.Reservation,checked(32+56L*n))){var rows=new RecastFormationEntry[n];for(int i=0;i<n;i++)rows[i]=RecastFormationEntry.Read(b);return Create(w,g,o,c,m,lanes,phase,anchor,initialized,entry.Value,exit.Value,rows,paths,r.Reservation,false,seal.Value,entry,exit,seal);}}}
        protected override void ReleaseEdges(){foreach(var s in spines??Array.Empty<NavRecastSpine>())s?.Release();entryPortal?.Release();exitPortal?.Release();seal?.Release();spines=null;entries=null;entryPortal=exitPortal=seal=null;}
    }
}
