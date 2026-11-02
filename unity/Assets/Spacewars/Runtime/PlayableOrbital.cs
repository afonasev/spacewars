using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private PlayableProfile orbitalProfile;private PlayableProfileData orbitalData;
        private PlayableProfileData OrbitalData{get{if(!ReferenceEquals(orbitalProfile,profile)){orbitalProfile=profile;orbitalData=profile.CopyData();}return orbitalData;}}
        private readonly Dictionary<PlayableOwner,OrbitalEscalationState> orbital=new Dictionary<PlayableOwner,OrbitalEscalationState>();
        private OrbitalEscalationState Orbital(PlayableOwner owner)
        {if(!orbital.TryGetValue(owner,out var value))orbital.Add(owner,value=new OrbitalEscalationState());return value;}
        private void CreditOrbital(PlayableOwner? owner,PlayableOwner victim,double amount)
        {if(owner.HasValue&&HasOwner(owner.Value)&&Hostile(owner.Value,victim)&&amount>0)Orbital(owner.Value).Damage(amount,OrbitalData);}
        private sealed class Scan {internal PlayableOwner Owner;internal NavPoint Point;internal double Radius,Remaining,Duration;}
        private readonly List<Scan> scans=new List<Scan>();
        private OrbitalScanSnapshot[] TeamOrbitalScans(PlayableOwner owner)=>scans.Where(s=>s.Remaining>0&&TeamOf(s.Owner)==TeamOf(owner)).Select(s=>new OrbitalScanSnapshot(s.Owner,s.Point,s.Radius)).ToArray();
        private void AdvanceOrbital(double dt)
        {foreach(var scan in scans)scan.Remaining=Math.Max(0,scan.Remaining-dt);if(scans.RemoveAll(s=>s.Remaining<=1e-9)>0)visionInputProfile=null;if(orbital.Count==0)return;var data=OrbitalData;foreach(var state in orbital.Values)state.Advance(dt,data);}
        private OrbitalEscalationSnapshot OrbitalView(PlayableOwner owner)=>orbital.TryGetValue(owner,out var state)?state.Snapshot(OrbitalData):new OrbitalEscalationState().Snapshot(OrbitalData);
        private PlayableCommandStatus ChooseOrbital(PlayableOwner owner,int id)=>!eliminated.Contains(owner)&&Orbital(owner).Choose(id)?PlayableCommandStatus.Applied:PlayableCommandStatus.Rejected;
        private PlayableCommandStatus UseOrbital(PlayableOwner owner,int id,NavPoint target)
        {
            if(id==OrbitalAbilities.ControlHack)return PlayableCommandStatus.InvalidTarget;
            if((id==1||id==2)&&(double.IsNaN(target.X)||double.IsNaN(target.Z)||double.IsInfinity(target.X)||double.IsInfinity(target.Z)||Math.Abs(target.X)>profile.ArenaHalfExtent||Math.Abs(target.Z)>profile.ArenaHalfExtent))return PlayableCommandStatus.InvalidTarget;
            if(id==2&&!ValidMinePlacement(owner,target))return PlayableCommandStatus.InvalidTarget;
            if(eliminated.Contains(owner)||!Orbital(owner).Use(id,OrbitalData))return PlayableCommandStatus.Rejected;
            if(id==1){scans.Add(new Scan{Owner=owner,Point=target,Radius=OrbitalData.orbitalScanRadius,Remaining=OrbitalData.orbitalScanSeconds,Duration=OrbitalData.orbitalScanSeconds});visionInputProfile=null;RefreshVision();}
            if(id==2)PlaceMine(owner,target);
            return PlayableCommandStatus.Applied;
        }
        private double ScanEnvelope(Scan scan)
        {
            double t=Math.Min((scan.Duration-scan.Remaining)/OrbitalData.orbitalScanFadeSeconds,scan.Remaining/OrbitalData.orbitalScanFadeSeconds);
            t=Math.Max(0,Math.Min(1,t));return t*t*(3-2*t);
        }
        private OrbitalScanLine[] ScanLines(TeamVisionSnapshot vision)
        {
            if(scans.Count==0)return Array.Empty<OrbitalScanLine>();
            var lines=new List<OrbitalScanLine>();var data=OrbitalData;
            foreach(var scan in scans){
                NavPoint At(int i)=>new NavPoint(scan.Point.X+Math.Cos(i*Math.PI/64)*scan.Radius,scan.Point.Z+Math.Sin(i*Math.PI/64)*scan.Radius);
                // Fixed subdivisions and gap topology are renderer invariants, not tuning.
                for(int i=0;i<128;i++)if(i%16>0&&i%16<15)lines.AddRange(OrbitalScanVisibility.Clip(At(i),At(i+1),data.orbitalScanOpacity*data.orbitalScanRimOpacity*ScanEnvelope(scan),vision.Sources));
            }
            return lines.ToArray();
        }
        private OrbitalScanTriangle[] ScanTriangles(TeamVisionSnapshot vision)
        {
            if(scans.Count==0)return Array.Empty<OrbitalScanTriangle>();
            var faces=new List<OrbitalScanTriangle>();var data=OrbitalData;
            foreach(var scan in scans){
                double age=scan.Duration-scan.Remaining,head=age/data.orbitalScanSweepSeconds*Math.PI*2,span=data.orbitalScanSectorDegrees*Math.PI/180,core=data.orbitalScanCoreDegrees*Math.PI/180,envelope=ScanEnvelope(scan)*data.orbitalScanOpacity;
                NavPoint At(double angle,double radius)=>new NavPoint(scan.Point.X+Math.Cos(angle)*radius,scan.Point.Z+Math.Sin(angle)*radius);
                void Face(NavPoint a,NavPoint b,NavPoint c,(double opacity,double highlight) u,(double opacity,double highlight) v,(double opacity,double highlight) w){if(Math.Max(u.opacity,Math.Max(v.opacity,w.opacity))>1e-5&&OrbitalScanVisibility.Covered(a,b,c,vision.Sources))faces.Add(new OrbitalScanTriangle(a,b,c,u.opacity,u.highlight,v.opacity,v.highlight,w.opacity,w.highlight));}
                void Quad(double a,double b,double inner,double outer,Func<double,double,(double opacity,double highlight)> light){
                    if(inner<0||outer>scan.Radius||inner>=outer)return;
                    var x=At(a,inner);var y=At(b,inner);var z=At(a,outer);var w=At(b,outer);
                    if(inner>0)Face(x,y,z,light(a,inner),light(b,inner),light(a,outer));Face(y,w,z,light(b,inner),light(b,outer),light(a,outer));
                }
                // Fixed integration samples and Gaussian kernels approximate a continuous light field.
                // Editable brightness, angular extent and timing live in the named orbital profile.
                (double,double) Beam(double angle,double radius){
                    double delta=angle-head,bright=Math.Exp(-delta*delta/(2*core*core)),tail=delta<0?data.orbitalScanTrailOpacity*Math.Pow(Math.Max(0,1+delta/span),2):0;
                    double edge=Math.Sqrt(Math.Max(0,Math.Sin(Math.PI*radius/scan.Radius)));
                    return (envelope*(bright+tail)*edge,bright);
                }
                for(int i=0;i<96;i++){
                    double a=head-span+(span+core*3)*i/96,b=head-span+(span+core*3)*(i+1)/96;
                    for(int j=0;j<12;j++)Quad(a,b,scan.Radius*j/12d,scan.Radius*(j+1)/12d,Beam);
                }
                for(int wave=0;wave<2;wave++){
                    double phase=(age/data.orbitalScanWaveSeconds+wave/2d)%1,radius=phase*scan.Radius;
                    for(int band=0;band<8;band++){
                        double offset=(band-4)/4d,next=(band-3)/4d;
                        (double,double) Wave(double angle,double r){double x=(r-radius)*16/scan.Radius;return (envelope*data.orbitalScanWaveOpacity*Math.Exp(-x*x*5)*Math.Sin(Math.PI*phase),0);}
                        for(int i=0;i<96;i++)Quad(i*Math.PI/48,(i+1)*Math.PI/48,radius+offset*scan.Radius/16,radius+next*scan.Radius/16,Wave);
                    }
                }
            }
            return faces.ToArray();
        }
        private void WriteOrbital(BinaryWriter w)
        {
            w.Write(0x4f524233);w.Write(orbital.Count);
            foreach(var pair in orbital.OrderBy(x=>(int)x.Key)){
                w.Write((int)pair.Key);w.Write(pair.Value.Level);WorldWire.Number(w,pair.Value.Progress);WorldWire.Number(w,pair.Value.CommonRemaining);
                for(int i=0;i<OrbitalEscalationState.ChoiceCount;i++){w.Write(pair.Value.Choice(i));WorldWire.Number(w,pair.Value.Recovery(i));}
            }
            w.Write(scans.Count);foreach(var scan in scans){w.Write((int)scan.Owner);WorldWire.Number(w,scan.Point.X);WorldWire.Number(w,scan.Point.Z);WorldWire.Number(w,scan.Radius);WorldWire.Number(w,scan.Remaining);WorldWire.Number(w,scan.Duration);}
        }
        private void ReadOrbital(BinaryReader r)
        {
            if(r.ReadInt32()!=0x4f524233)throw new ArgumentException("Missing orbital state.");
            int count=r.ReadInt32();if(count<0||count>Owners.Count())throw new ArgumentException("Invalid orbital owner count.");
            for(int j=0;j<count;j++){
                var owner=WorldWire.EnumValue<PlayableOwner>(r);if(!HasOwner(owner)||orbital.ContainsKey(owner))throw new ArgumentException("Invalid orbital owner.");
                int level=r.ReadInt32();double progress=WorldWire.Number(r),common=WorldWire.Number(r);var ids=new int[OrbitalEscalationState.ChoiceCount];var times=new double[OrbitalEscalationState.ChoiceCount];
                for(int i=0;i<OrbitalEscalationState.ChoiceCount;i++){ids[i]=r.ReadInt32();times[i]=WorldWire.Number(r);}
                orbital.Add(owner,OrbitalEscalationState.Restore(level,progress,common,ids,times,OrbitalData));
            }
            int scanCount=r.ReadInt32();if(scanCount<0||scanCount>10000)throw new ArgumentException("Invalid scan count.");
            for(int i=0;i<scanCount;i++){
                var scan=new Scan{Owner=WorldWire.EnumValue<PlayableOwner>(r),Point=new NavPoint(WorldWire.Number(r),WorldWire.Number(r)),Radius=WorldWire.Number(r),Remaining=WorldWire.Number(r),Duration=WorldWire.Number(r)};
                if(!HasOwner(scan.Owner)||Math.Abs(scan.Point.X)>profile.ArenaHalfExtent||Math.Abs(scan.Point.Z)>profile.ArenaHalfExtent||scan.Radius<=0||scan.Remaining<=0||scan.Remaining>scan.Duration)throw new ArgumentException("Invalid scan state.");scans.Add(scan);
            }
            visionInputProfile=null;
        }
    }
}
