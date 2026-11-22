using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private PlayableProfile siteFrameProfile;
        private readonly Dictionary<Vector2,Quaternion> siteFrames=new();
        private readonly Dictionary<Vector2,Quaternion> childPadFrames=new();
        private static Vector2 SiteFrameKey(NavPoint point)=>new Vector2((float)point.X,(float)point.Z);

        private void EnsureSiteFrames()
        {
            if(ReferenceEquals(siteFrameProfile,profile))return;
            siteFrameProfile=profile;siteFrames.Clear();childPadFrames.Clear();
            var map=profile.AuthoredMap;if(map==null)return;
            var sites=map.Sites(profile);
            var accesses=map is ThreeCrossingsMap crossings
                ?ThreeCrossingsMaterials.PlanMineAccess(ThreeCrossingsSurfaceProfile.Load(),crossings,profile):null;
            foreach(var site in sites)
            {
                var key=SiteFrameKey(site.Position);
                if(map is AntennaCityMap city)
                {
                    if(site.Kind==PlayableBuildingKind.RelayTower)continue;
                    var frame=Quaternion.Euler(0,-(float)city.SiteHeading(site.Id)*Mathf.Rad2Deg,0);
                    siteFrames[key]=frame;
                    if(site.Slots.Count>0)
                    {
                        foreach(var slot in site.Slots)childPadFrames[SiteFrameKey(slot.Position)]=frame;
                        if(site.TryDefenseSlots(profile,out var defenses))
                            foreach(var slot in defenses)childPadFrames[SiteFrameKey(slot.Position)]=frame;
                    }
                    continue;
                }
                if(site.Kind!=PlayableBuildingKind.Mine)continue;
                var p=site.Position;Vector3 direction;
                // These are authored geometric frames, not balance/art tuning angles.
                Vector3 InwardX()=>new Vector3(-(float)Math.Sign(p.X),0,0);
                Vector3 InwardZ()=>new Vector3(0,0,-(float)Math.Sign(p.Z));
                if(map is BasaltCrownMap||map is NullDockMap||map is FoundryMap)
                    direction=Math.Abs(p.X)>Math.Abs(p.Z)?InwardX():InwardZ();
                else if(map is WhiteMeridianMap meridian)
                    direction=Math.Abs(p.X)>meridian.HalfExtent*.5?InwardX():InwardZ();
                else if(map is AiTestMap||map is CrimsonGroveMap)
                    direction=new Vector3(-(float)p.X,0,-(float)p.Z);
                else if(accesses!=null)
                {
                    var access=accesses.Single(a=>a.Site.Id==site.Id);
                    direction=new Vector3((float)(access.Connection.X-p.X),0,(float)(access.Connection.Z-p.Z));
                }
                else if(map is NereidaMap nereida)
                {
                    // All three mine terraces share the axes of their authored quarter.
                    int quarter=(site.Id-9)%4;var origin=nereida.Point(quarter,0,0);var inward=nereida.Point(quarter,-1,0);
                    direction=new Vector3((float)(inward.X-origin.X),0,(float)(inward.Z-origin.Z));
                }
                else if(map is MossHighlandMap||map is DeafMirrorMap||map is CampaignOpeningMap||map is ProvingGroundMap)
                    direction=Vector3.back;
                else continue;
                siteFrames[key]=direction.sqrMagnitude>0?Quaternion.LookRotation(direction,Vector3.up):BuildingFacing;
            }
        }

        private Quaternion SitePadFacing(NavPoint point,Camera camera,bool mine=false)
        {
            EnsureSiteFrames();
            return childPadFrames.TryGetValue(SiteFrameKey(point),out var frame)?frame:SiteFacing(point,camera,mine);
        }

        private Vector2 SiteApronHalf(NavPoint point,PlayableBuildingKind kind,bool upgraded=false)
        {
            var half=ApronHalf(kind,upgraded);
            var model=SiteFacing(point,null,kind==PlayableBuildingKind.Mine);
            var pad=SitePadFacing(point,null,kind==PlayableBuildingKind.Mine);
            if(Quaternion.Angle(model,pad)<.001f)return half;
            var relative=Quaternion.Inverse(pad)*model;
            var x=relative*Vector3.right;var z=relative*Vector3.forward;
            // The pad follows its courtyard while the factory keeps its authoritative exit.
            return new Vector2(Mathf.Abs(x.x)*half.x+Mathf.Abs(z.x)*half.y,
                Mathf.Abs(x.z)*half.x+Mathf.Abs(z.z)*half.y);
        }
    }
}
