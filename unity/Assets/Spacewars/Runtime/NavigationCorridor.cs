using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // A3-0 carries the certified typed route without claiming that its shared
    // field is already a largest-footprint formation corridor.
    public enum NavCorridorSection { IndividualConnector, SharedTrunk, TerminalConnector }

    public sealed class NavCorridorLeg
    {
        public NavCorridorLeg(NavLocation from, NavLocation to, NavTypedLegKind kind,
            string portalId, double cost, NavCorridorSection section, long branch, int downstreamMergeLeg)
        {
            if (!Enum.IsDefined(typeof(NavTypedLegKind),kind) ||
                !Enum.IsDefined(typeof(NavCorridorSection),section) || branch<0 ||
                downstreamMergeLeg<0 || double.IsNaN(cost) || double.IsInfinity(cost) || cost<0 ||
                string.IsNullOrWhiteSpace(from.SurfaceId) || string.IsNullOrWhiteSpace(to.SurfaceId) ||
                (kind==NavTypedLegKind.Portal)==string.IsNullOrWhiteSpace(portalId) ||
                (kind==NavTypedLegKind.Surface && from.SurfaceId!=to.SurfaceId))
                throw new ArgumentException("Invalid typed corridor leg.");
            From=from;To=to;Kind=kind;PortalId=portalId;Cost=cost;
            Section=section;Branch=branch;DownstreamMergeLeg=downstreamMergeLeg;
        }
        public NavLocation From{get;} public NavLocation To{get;}
        public NavTypedLegKind Kind{get;} public string PortalId{get;} public double Cost{get;}
        public NavCorridorSection Section{get;} public long Branch{get;} public int DownstreamMergeLeg{get;}
    }

    public sealed class NavCorridorDescriptor
    {
        private readonly NavCorridorLeg[] legs;
        public const int CurrentVersion=1;
        public NavCorridorDescriptor(int version,string graphKey,string providerBinding,string profileBinding,
            string surfaceProviderId,string holdBinding,int topologyRevision,double radius,double gridCell,NavRouteProvenance provenance,
            long world,long group,long rootRevision,long commandSequence,long issuedTick,
            long activationRevision,long entity,long incarnation,long mobility,long request,long order,
            NavLocation origin,NavLocation endpoint,IEnumerable<NavCorridorLeg> route)
        {
            legs=(route??throw new ArgumentNullException(nameof(route))).Take(WorldWire.MaxItems+1).ToArray();
            if(version!=CurrentVersion||!Enum.IsDefined(typeof(NavRouteProvenance),provenance)||
                graphKey==null||providerBinding==null||profileBinding==null||
                graphKey.Length!=64||providerBinding.Length!=64||profileBinding.Length!=64||
                string.IsNullOrWhiteSpace(surfaceProviderId)||surfaceProviderId.Length>256||
                holdBinding!=null&&holdBinding.Length>256||topologyRevision<0||
                double.IsNaN(radius)||double.IsInfinity(radius)||radius<=0||
                double.IsNaN(gridCell)||double.IsInfinity(gridCell)||gridCell<=0||world<0||group<0||rootRevision<0||
                commandSequence<0||issuedTick<0||activationRevision<0||incarnation<0||mobility<0||mobility>int.MaxValue||request<1||order<0||
                (provenance==NavRouteProvenance.ProducerProbe?entity>=0||group!=0:entity<0)||
                (provenance==NavRouteProvenance.CommandMember&&group==0)||
                legs.Length==0||legs.Length>WorldWire.MaxItems||legs.Any(x=>x==null)||
                !legs[0].From.Equals(origin)||!legs[legs.Length-1].To.Equals(endpoint))
                throw new ArgumentException("Invalid corridor binding.");
            NavContract.Sha256(graphKey);NavContract.Sha256(providerBinding);NavContract.Sha256(profileBinding);
            for(int i=0;i<legs.Length;i++){
                if(legs[i]==null||legs[i].DownstreamMergeLeg>legs.Length||
                    i>0&&!legs[i-1].To.Equals(legs[i].From))throw new ArgumentException("Discontinuous corridor.");
            }
            Version=version;GraphKey=graphKey;ProviderBinding=providerBinding;ProfileBinding=profileBinding;
            SurfaceProviderId=surfaceProviderId;HoldBinding=holdBinding;TopologyRevision=topologyRevision;Radius=radius;GridCell=gridCell;
            Provenance=provenance;World=world;Group=group;RootRevision=rootRevision;
            CommandSequence=commandSequence;IssuedTick=issuedTick;ActivationRevision=activationRevision;
            Entity=entity;Incarnation=incarnation;Mobility=mobility;Request=request;Order=order;
            Origin=origin;Endpoint=endpoint;
            StableKey=NavContract.Digest(w=>{
                w.Write("corridor-descriptor-v1");w.Write(GraphKey);w.Write(ProviderBinding);w.Write(ProfileBinding);
                w.Write(SurfaceProviderId);w.Write(HoldBinding!=null);if(HoldBinding!=null)w.Write(HoldBinding);
                w.Write(TopologyRevision);w.Write(Radius);w.Write(GridCell);w.Write((int)Provenance);
                foreach(long value in new[]{World,Group,RootRevision,CommandSequence,IssuedTick,ActivationRevision,
                    Entity,Incarnation,Mobility,Request,Order})w.Write(value);
                NavContract.Location(w,Origin);NavContract.Location(w,Endpoint);w.Write(legs.Length);
                foreach(var leg in legs){NavContract.Location(w,leg.From);NavContract.Location(w,leg.To);
                    w.Write((int)leg.Kind);w.Write(leg.PortalId!=null);if(leg.PortalId!=null)w.Write(leg.PortalId);
                    w.Write(leg.Cost);w.Write((int)leg.Section);w.Write(leg.Branch);w.Write(leg.DownstreamMergeLeg);}
            });
            EstimatedRetainedBytes=checked(1024L+256L*legs.Length+
                2L*(SurfaceProviderId.Length+(HoldBinding?.Length??0)+legs.Sum(x=>(long)(x.PortalId?.Length??0)+x.From.SurfaceId.Length+x.To.SurfaceId.Length)));
        }
        public int Version{get;} public string GraphKey{get;} public string ProviderBinding{get;}
        public string ProfileBinding{get;} public string SurfaceProviderId{get;} public string HoldBinding{get;}
        public int TopologyRevision{get;} public double Radius{get;} public double GridCell{get;}
        public NavRouteProvenance Provenance{get;}
        public long World{get;} public long Group{get;} public long RootRevision{get;}
        public long CommandSequence{get;} public long IssuedTick{get;}
        public long ActivationRevision{get;} public long Entity{get;} public long Incarnation{get;}
        public long Mobility{get;} public long Request{get;} public long Order{get;}
        public NavLocation Origin{get;} public NavLocation Endpoint{get;}
        public string StableKey{get;} public long EstimatedRetainedBytes{get;}
        public IReadOnlyList<NavCorridorLeg> Legs=>Array.AsReadOnly(legs);
        public NavPoint[] ProjectedRoute()=>legs.Where(x=>x.Kind==NavTypedLegKind.Surface).Select(x=>x.To.Position).ToArray();
    }

    [Serializable] public sealed class NavigationInstalledExecutionState
    {
        public int Entity; public bool Detached; public NavCorridorDescriptor Corridor; public NavPoint[] InstalledRoute;
        public string ProfileId,HeldIdentity; public int ProfileRevision,GeometryIndex,BaseGeometryIndex;
        public double[] ProfileValues;
        internal NavigationProfile Profile; internal NavGeometry MovementGeometry,BaseGeometry;
    }
}
