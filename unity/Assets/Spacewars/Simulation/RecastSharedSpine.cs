using System;
using System.Collections.Generic;
namespace Spacewars.Simulation
{
    // Versioned, pointer-free accepted decision. Station is geometric distance,
    // independent of traversal cost and every member's connector lengths.
    internal sealed class NavRecastSpine : NavOwnedStorage, INavPhysicalBody
    {
        private static readonly long Layout=Math.Max(256,NavStorageLayout.ObjectBytesBound(typeof(NavRecastSpine)));
        internal NavRetainedArray<NavTypedLeg> Legs;
        private NavOwnedText binding,map,graph,provider,profile,knowledge;
        internal string GraphBinding=>graph.Value;internal string ProviderBinding=>provider.Value;internal string ProfileBinding=>profile.Value;internal string KnowledgeBinding=>knowledge.Value;internal string MapBinding=>map.Value;
        internal readonly int GeometryRevision;
        internal string Binding=>binding.Value;
        internal readonly long World,Group,Order,Command;
        internal readonly double Radius;internal readonly NavPoint FanCenter;internal readonly double FanRadius;internal readonly int FanNode;
        private NavRecastSpine(long world,long group,long order,long command,double radius,int geometryRevision,NavPoint fanCenter,double fanRadius,int fanNode):base(Layout)
        {World=world;Group=group;Order=order;Command=command;Radius=radius;GeometryRevision=geometryRevision;FanCenter=fanCenter;FanRadius=fanRadius;FanNode=fanNode;}
        internal override bool IsImmutableCaptureRoot=>Legs!=null&&binding!=null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.TypedRoute,value:NavPhysicalValueKind.RecastSpineV2);
        internal static NavRecastSpine Create(long world,long group,long order,long command,double radius,string identity,string mapBinding,string graphBinding,string providerBinding,string profileBinding,string knowledgeBinding,int geometryRevision,NavPoint fanCenter,double fanRadius,int fanNode,IReadOnlyList<NavTypedLeg> legs,INavStorageReservation r)
        {
            var text=NavOwnedText.Copy(identity,r);NavOwnedText m=null,g=null,p=null,f=null,k=null;NavRetainedArray<NavTypedLeg> copy=null;
            try{m=NavOwnedText.Copy(mapBinding,r);g=NavOwnedText.Copy(graphBinding,r);p=NavOwnedText.Copy(providerBinding,r);f=NavOwnedText.Copy(profileBinding,r);k=NavOwnedText.Copy(knowledgeBinding??"",r);copy=NavRetainedArray<NavTypedLeg>.Copy(legs,r.TryReserve);var result=Birth(world,group,order,command,radius,geometryRevision,fanCenter,fanRadius,fanNode,text,m,g,p,f,k,copy,r);result.Retain();return result;}
            finally{text.Release();m?.Release();g?.Release();p?.Release();f?.Release();k?.Release();copy?.Release();}
        }
        private static NavRecastSpine Birth(long world,long group,long order,long command,double radius,int geometryRevision,NavPoint fanCenter,double fanRadius,int fanNode,NavOwnedText identity,NavOwnedText map,NavOwnedText graph,NavOwnedText provider,NavOwnedText profile,NavOwnedText knowledge,NavRetainedArray<NavTypedLeg> legs,INavStorageReservation r)
        {
            if(world<1||group<0||order<0||command<0||!Finite(radius)||radius<=0||legs.Count==0||legs.Count>16384)throw new ArgumentException("Invalid Recast spine dimensions.");
            if(!Finite(fanCenter.X)||!Finite(fanCenter.Z)||!Finite(fanRadius)||fanRadius<=0||fanNode<0||fanNode>legs.Count)throw new ArgumentException("Invalid accepted terminal fan.");
            if(geometryRevision<0)throw new ArgumentException("Invalid Recast geometry revision.");NavContract.Sha256(identity.Value);NavContract.Sha256(map.Value);NavContract.Sha256(graph.Value);NavContract.Sha256(provider.Value);NavContract.Sha256(profile.Value);NavPhysicalSolveValidation.Legs(legs,out _);
            using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);var value=a.Register(new NavRecastSpine(world,group,order,command,radius,geometryRevision,fanCenter,fanRadius,fanNode));value.binding=NavPhysicalMacro.Acquire(value,identity);value.map=NavPhysicalMacro.Acquire(value,map);value.graph=NavPhysicalMacro.Acquire(value,graph);value.provider=NavPhysicalMacro.Acquire(value,provider);value.profile=NavPhysicalMacro.Acquire(value,profile);value.knowledge=NavPhysicalMacro.Acquire(value,knowledge);value.Legs=NavPhysicalMacro.Acquire(value,legs);return value;}
        }
        internal void ValidateCommonBindings(string provider,string knowledge,double radius,int revision)
        {if(ProviderBinding!=provider||KnowledgeBinding!=(knowledge??"")||Radius!=radius||GeometryRevision!=revision)throw new ArgumentException("Recast spine map/profile/clearance/knowledge binding changed.");}
        internal static bool Finite(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
        internal NavLocation Node(int n)=>n==0?Legs[0].From:Legs[n-1].To;
        internal double Station(int node){double s=0;for(int i=0;i<node;i++)s+=Length(Legs[i]);return s;}
        internal static double Length(NavTypedLeg l){double x=l.To.Position.X-l.From.Position.X,z=l.To.Position.Z-l.From.Position.Z;return Math.Sqrt(x*x+z*z);}
        public void WritePhysicalBody(NavPhysicalBodyWriter w){w.Output.Write(World);w.Output.Write(Group);w.Output.Write(Order);w.Output.Write(Command);w.Output.Write(Radius);w.Output.Write(GeometryRevision);w.Output.Write(FanCenter.X);w.Output.Write(FanCenter.Z);w.Output.Write(FanRadius);w.Output.Write(FanNode);NavPhysicalMacro.WriteEdge(w,this,binding);NavPhysicalMacro.WriteEdge(w,this,map);NavPhysicalMacro.WriteEdge(w,this,graph);NavPhysicalMacro.WriteEdge(w,this,provider);NavPhysicalMacro.WriteEdge(w,this,profile);NavPhysicalMacro.WriteEdge(w,this,knowledge);NavPhysicalMacro.WriteEdge(w,this,Legs);}
        internal static NavRecastSpine Read(NavPhysicalBodyReader r){long world=r.Input.ReadInt64(),group=r.Input.ReadInt64(),order=r.Input.ReadInt64(),command=r.Input.ReadInt64();double radius=r.Input.ReadDouble();int revision=r.Input.ReadInt32();var center=new NavPoint(r.Input.ReadDouble(),r.Input.ReadDouble());double fanRadius=r.Input.ReadDouble();int fanNode=r.Input.ReadInt32();var text=NavPhysicalMacro.ReadEdge<NavOwnedText>(r);var m=NavPhysicalMacro.ReadEdge<NavOwnedText>(r);var g=NavPhysicalMacro.ReadEdge<NavOwnedText>(r);var p=NavPhysicalMacro.ReadEdge<NavOwnedText>(r);var f=NavPhysicalMacro.ReadEdge<NavOwnedText>(r);var k=NavPhysicalMacro.ReadEdge<NavOwnedText>(r);var legs=NavPhysicalMacro.ReadEdge<NavRetainedArray<NavTypedLeg>>(r);return Birth(world,group,order,command,radius,revision,center,fanRadius,fanNode,text,m,g,p,f,k,legs,r.Reservation);}
        protected override void ReleaseEdges(){binding?.Release();map?.Release();graph?.Release();provider?.Release();profile?.Release();knowledge?.Release();Legs?.Release();binding=null;Legs=null;}
    }
    internal sealed class NavRecastMemberPlan : NavOwnedStorage, INavPhysicalBody
    {
        private static readonly long Layout=Math.Max(128,NavStorageLayout.ObjectBytesBound(typeof(NavRecastMemberPlan)));
        internal NavRecastSpine Spine;private NavOwnedText graph,profile;
        internal string GraphBinding=>graph.Value;internal string ProfileBinding=>profile.Value;
        internal readonly int Join,Exit,OriginLegs,TerminalLeg;
        private NavRecastMemberPlan(int join,int exit,int origin,int terminal):base(Layout){Join=join;Exit=exit;OriginLegs=origin;TerminalLeg=terminal;}
        internal override bool IsImmutableCaptureRoot=>Spine!=null;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.TypedRoute,value:NavPhysicalValueKind.RecastMemberPlanV2);
        internal static NavRecastMemberPlan Create(NavRecastSpine spine,int join,int exit,int origin,int terminal,string graphBinding,string profileBinding,INavStorageReservation r,bool owner=true)
        {
            if(join<0||exit<join||exit>spine.Legs.Count||origin<0||terminal!=origin+exit-join)throw new ArgumentException("Invalid accepted Recast correspondence.");
            NavStorageAdmission.RequireOwned(r,spine);
            NavContract.Sha256(graphBinding);NavContract.Sha256(profileBinding);
            var g=NavOwnedText.Copy(graphBinding,r);NavOwnedText p=null;
            try{p=NavOwnedText.Copy(profileBinding,r);return Birth(spine,join,exit,origin,terminal,g,p,r,owner);}
            finally{g.Release();p?.Release();}
        }
        private static NavRecastMemberPlan Birth(NavRecastSpine spine,int join,int exit,int origin,int terminal,NavOwnedText graph,NavOwnedText profile,INavStorageReservation r,bool owner)
        {
            if(join<0||exit<join||exit>spine.Legs.Count||origin<0||terminal!=origin+exit-join)throw new ArgumentException("Invalid accepted Recast correspondence.");NavContract.Sha256(graph.Value);NavContract.Sha256(profile.Value);
            using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);var v=a.Register(new NavRecastMemberPlan(join,exit,origin,terminal));v.Spine=NavPhysicalMacro.Acquire(v,spine);v.graph=NavPhysicalMacro.Acquire(v,graph);v.profile=NavPhysicalMacro.Acquire(v,profile);if(owner)v.Retain();return v;}
        }
        internal void ValidateBindings(string graph,string provider,string profile,string knowledge,double radius,int revision)
        {Spine.ValidateCommonBindings(provider,knowledge,radius,revision);if(GraphBinding!=graph||ProfileBinding!=profile)throw new ArgumentException("Recast member exact graph/profile binding changed.");}
        internal void Validate(NavTypedRoute route)
        {
            var s=route.Subscription;
            if(Spine.World!=s.WorldGeneration||Spine.Group!=s.GroupId||Spine.Order!=s.RootOrderRevision||Spine.Command!=s.CommandSequence||TerminalLeg>=route.Legs.Count||
                !route.Legs[OriginLegs==0?0:OriginLegs-1].To.Equals(Spine.Node(Join))&&OriginLegs!=0||
                OriginLegs==0&&!s.Origin.Equals(Spine.Node(Join))||!route.Legs[TerminalLeg].From.Equals(Spine.Node(Exit)))throw new ArgumentException("Recast member binding changed.");
            for(int i=Join;i<Exit;i++){var a=Spine.Legs[i];var b=route.Legs[OriginLegs+i-Join];if(!a.From.Equals(b.From)||!a.To.Equals(b.To)||a.Kind!=b.Kind||a.PortalId!=b.PortalId||a.Cost!=b.Cost)throw new ArgumentException("Recast shared transcript changed.");}
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter w){w.Output.Write(Join);w.Output.Write(Exit);w.Output.Write(OriginLegs);w.Output.Write(TerminalLeg);NavPhysicalMacro.WriteEdge(w,this,Spine);NavPhysicalMacro.WriteEdge(w,this,graph);NavPhysicalMacro.WriteEdge(w,this,profile);}
        internal static NavRecastMemberPlan Read(NavPhysicalBodyReader r){int j=r.Input.ReadInt32(),e=r.Input.ReadInt32(),o=r.Input.ReadInt32(),t=r.Input.ReadInt32();var spine=NavPhysicalMacro.ReadEdge<NavRecastSpine>(r);var graph=NavPhysicalMacro.ReadEdge<NavOwnedText>(r);var profile=NavPhysicalMacro.ReadEdge<NavOwnedText>(r);return Birth(spine,j,e,o,t,graph,profile,r.Reservation,false);}
        protected override void ReleaseEdges(){Spine?.Release();graph?.Release();profile?.Release();Spine=null;graph=profile=null;}
    }
}
