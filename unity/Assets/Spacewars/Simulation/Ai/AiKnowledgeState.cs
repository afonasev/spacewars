using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation.Ai
{
    // Public placement geometry only. No live claimant, bank, queues or authority handle.
    public sealed class AiIntelEnvelope
    {
        public AiIntelEnvelope(int areaId,IEnumerable<VisionSource> placement)
        {AreaId=areaId;Placement=Array.AsReadOnly(placement.ToArray());}
        public int AreaId{get;}
        public IReadOnlyList<VisionSource> Placement{get;}
        public bool Contains(NavPoint point)=>PlayableVision.Visible(Placement,point);
        public bool FullyCovered(TeamVisionSnapshot vision)=>vision!=null&&Placement.Count>0&&Placement.All(p=>Covered(vision.Sources,p));
        private static bool Covered(IReadOnlyList<VisionSource> sources,VisionSource envelope)
        {
            if(!PlayableVision.Visible(sources,envelope.Position))return false;
            // Circumscribed boxes provide a conservative union proof. Unresolved boxes
            // at the technical subdivision limit deny proof; samples never certify absence.
            bool Box(double x,double z,double half,int depth)
            {
                double dx=Math.Max(0,Math.Abs(x-envelope.Position.X)-half),dz=Math.Max(0,Math.Abs(z-envelope.Position.Z)-half);
                if(dx*dx+dz*dz>envelope.Radius*envelope.Radius)return true;
                foreach(var s in sources)
                {double a=Math.Abs(x-s.Position.X)+half,b=Math.Abs(z-s.Position.Z)+half;if(a*a+b*b<=s.Radius*s.Radius)return true;}
                if(depth==0)return false;
                double h=half/2;return Box(x-h,z-h,h,depth-1)&&Box(x+h,z-h,h,depth-1)&&Box(x-h,z+h,h,depth-1)&&Box(x+h,z+h,h,depth-1);
            }
            if(sources.Any(s=>Math.Sqrt(Math.Pow(s.Position.X-envelope.Position.X,2)+Math.Pow(s.Position.Z-envelope.Position.Z,2))+envelope.Radius<=s.Radius))return true;
            return Box(envelope.Position.X,envelope.Position.Z,envelope.Radius,8);
        }
    }
    public sealed class AiIntelDelta
    {
        public AiIntelDelta(long tick,IEnumerable<AiIntelEnvelope> areas,TeamVisionSnapshot vision)
        {Tick=tick;Areas=Array.AsReadOnly(areas.OrderBy(a=>a.AreaId).Select(a=>new AiAreaCoverage(a,a.FullyCovered(vision))).ToArray());}
        public long Tick{get;}
        public IReadOnlyList<AiAreaCoverage> Areas{get;}
    }
    public sealed class AiAreaCoverage
    {
        public AiAreaCoverage(AiIntelEnvelope envelope,bool full){Envelope=envelope;Full=full;}
        public AiIntelEnvelope Envelope{get;} public bool Full{get;}
    }
    public sealed class AiKnownContact
    {
        public AiKnownContact(int id,PlayableOwner owner,bool building,int kind,NavPoint position,int health,long lastSeenTick,double confidence,bool visible)
        {Id=id;Owner=owner;Building=building;Kind=kind;Position=position;HealthAtLastSeen=health;LastSeenTick=lastSeenTick;Confidence=confidence;Visible=visible;}
        public int Id{get;} public PlayableOwner Owner{get;} public bool Building{get;} public int Kind{get;}
        public NavPoint Position{get;} public int HealthAtLastSeen{get;} public long LastSeenTick{get;}
        public double Confidence{get;} public bool Visible{get;}
    }
    public sealed class AiVisitedArea
    {
        public AiVisitedArea(int areaId,long actuallyCoveredTick,bool emptyAtLastCoverage,bool revisitDue)
        {AreaId=areaId;ActuallyCoveredTick=actuallyCoveredTick;EmptyAtLastCoverage=emptyAtLastCoverage;RevisitDue=revisitDue;}
        public int AreaId{get;} public long ActuallyCoveredTick{get;} public bool EmptyAtLastCoverage{get;} public bool RevisitDue{get;}
    }
    public sealed class AiKnowledgeState
    {
        public AiKnowledgeState(string ownerId,long generation,long observationTick,IEnumerable<AiKnownContact> contacts,IEnumerable<AiVisitedArea> areas)
        {OwnerId=ownerId;Generation=generation;ObservationTick=observationTick;Contacts=Array.AsReadOnly(contacts.OrderBy(c=>c.Id).ToArray());Areas=Array.AsReadOnly(areas.OrderBy(a=>a.AreaId).ToArray());}
        public string OwnerId{get;} public long Generation{get;} public long ObservationTick{get;}
        public IReadOnlyList<AiKnownContact> Contacts{get;} public IReadOnlyList<AiVisitedArea> Areas{get;}
        // A lack of observations cannot establish a zero enemy economy/army.
        public double? EnemyIncomeEstimate=>null;
        public double? EnemyForceEstimate=>null;
    }
}
