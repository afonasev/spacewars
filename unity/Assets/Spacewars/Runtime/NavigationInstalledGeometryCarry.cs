using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private static bool PreservedEntity(PreparedRebind c,long entity){foreach(var a in c.Actors)if(a.Id==entity)return a.Preserve;return false;}
        private bool PrepareInstalledGeometryCarry(PreparedRebind c,NavGeometry basis,bool compatible,INavStorageReservation r)
        {
            using var carryTiming=PerformanceProfile.Measure(ProfileStage.WreckCarry);
            if(!c.GeometryChange||c.GeometryDomain==null||!compatible)return false;
            var oldGeometry=(NavGeometry)c.SourceCore.NavigationGeometry.Facade;
            if(InstalledGeometryCertificateV2.WreckDelta(oldGeometry,basis)){
                PrepareWreckGeometryCarry(c,oldGeometry,basis,r);
                CountGeometryCarry(c);
                return true;
            }
            if(c.Foundations.Length==0)return false;
            var before=(NavGeometry)c.SourceCore.NavigationGeometry.Facade;
            if(!InstalledGeometryCertificateV2.AddOnly(before,basis))return false;
            int added=0;foreach(var o in basis.Obstacles){bool old=false;foreach(var b in before.Obstacles)if(b.GeometryEquals(o)){old=true;break;}if(old)continue;bool foundation=false;foreach(var f in c.Foundations)if(o.GeometryEquals(f.Footprint(c.GeometryDomain))){foundation=true;break;}if(!foundation)return false;added++;}if(added!=c.Foundations.Length)return false;
            using(NavStorageScratch.Reserve(r,checked(128L+16L*c.Actors.Length))){
                for(int a=0;a<c.Actors.Length;a++){
                    var fact=c.Actors[a];if(fact.Group==null||fact.Version.Data.MarchVersion!=2)continue;
                    bool visited=false;for(int j=0;j<a;j++)if(ReferenceEquals(c.Actors[j].Group,fact.Group)){visited=true;break;}if(visited)continue;
                    int count=fact.Version.Members.Count;var rows=new NavOwnedStorage[count];bool eligible=true;
                    for(int m=0;m<count;m++){
                        var member=((NavigationAuthorityMember)fact.Version.Members[m]).Data;int at=-1;for(int j=0;j<c.Actors.Length;j++)if(c.Actors[j].Id==member.Entity)at=j;
                        if(at<0){eligible=false;break;}var row=c.Actors[at];var d=row.Installed?.Corridor.Corridor;
                        if(d==null||!row.Active||row.Detached||row.Held||!row.Moving||row.Installed.CurrentGeometry!=before||c.SourcePending.ContainsKey(row.Id)||c.SourceAllocating.ContainsKey(row.Id)||!c.SourceGoals.TryGetValue(row.Id,out var goal)||!goal.Equals(d.Endpoint.Position)||!c.SourceReservations.TryGetValue(row.Id,out var endpoint)||!endpoint.Equals(goal)||!CurrentHeldBinding(row.Installed.Request.Request.HeldIdentity,row.Id)||row.Installed.Request.Request.ProfileRevision!=((NavigationProfile)c.Core.Profile.Facade).Revision||!SameNavigationBinding(row.Installed.State.Profile,((NavigationProfile)c.Core.Profile.Facade).ForUnit(row.Radius,row.TargetSpeed,row.TargetTurn))){eligible=false;break;}
                    }
                    if(!eligible)continue;
                    NavRetainedArray<NavOwnedStorage> members=null;InstalledGeometryCertificateV2 certificate=null;
                    try{
                        for(int m=0;m<count;m++){
                            int entity=((NavigationAuthorityMember)fact.Version.Members[m]).Data.Entity;int at=0;while(c.Actors[at].Id!=entity)at++;var row=c.Actors[at];var local=row.Local as NavRetainedArray<NavPoint>;NavRetainedArray<NavPoint> empty=null;
                            try{if(local==null)local=empty=NavRetainedArray<NavPoint>.Copy(Array.Empty<NavPoint>(),r.TryReserve);rows[m]=InstalledGeometryMemberV2.Create(row.Installed.Request,row.Installed.Corridor,row.Installed.Route,local,row.Position,r);}finally{empty?.Release();}
                        }
                        members=NavRetainedArray<NavOwnedStorage>.Copy(rows,r.TryReserve);
                        certificate=InstalledGeometryCertificateV2.TryCreate(fact.Version,before,basis,members,r);
                        if(certificate==null)continue;
                        // No publication until every member owner has been prepared.
                        for(int m=0;m<count;m++){int entity=((NavigationAuthorityMember)fact.Version.Members[m]).Data.Entity;int at=0;while(c.Actors[at].Id!=entity)at++;ref var row=ref c.Actors[at];row.NextInstalled=NavigationInstalledExecutionOwner.CarryOwned(row.Installed,certificate,r);row.Preserve=true;}
                    }finally{certificate?.Release();members?.Release();foreach(var row in rows)row?.Release();}
                }
            }
            CountGeometryCarry(c);
            return true;
        }
        private void CountGeometryCarry(PreparedRebind c)
        {
            for(int i=0;i<c.Actors.Length;i++)if(c.Actors[i].Preserve){var owner=c.Actors[i].Installed;c.CarryCorridorBytes=checked(c.CarryCorridorBytes+owner.Corridor.Corridor.EstimatedRetainedBytes+16L*owner.Route.Count);
                for(int kind=0;kind<2;kind++){var geometry=kind==0?owner.State.MovementGeometry:owner.State.BaseGeometry;bool seen=kind==1&&ReferenceEquals(geometry,owner.State.MovementGeometry);for(int n=0;n<i;n++)if(c.Actors[n].Preserve&&(ReferenceEquals(geometry,c.Actors[n].Installed.State.MovementGeometry)||ReferenceEquals(geometry,c.Actors[n].Installed.State.BaseGeometry)))seen=true;if(!seen)c.CarryGeometryBytes=checked(c.CarryGeometryBytes+GeometryRetention(geometry));}
            }
            for(int i=0;i<c.Retries.Length;i++)c.Retries[i].GeometryCarried=c.Actors[c.Retries[i].ActorIndex].Preserve;
            foreach(var row in c.Actors)if(!c.Removes(row.Id)&&row.Installed!=null)PerformanceProfile.Add(row.Preserve?ProfileWork.WreckCarriedRoutes:ProfileWork.WreckResetRoutes,1);
        }
        private void PrepareWreckGeometryCarry(PreparedRebind c,NavGeometry before,NavGeometry basis,INavStorageReservation r)
        {
            for(int i=0;i<c.Actors.Length;i++){
                ref var row=ref c.Actors[i];var d=row.Installed?.Corridor.Corridor;
                if(c.Removes(row.Id)||d==null||row.Group==null||row.Version.Data.MarchVersion!=2||!row.Active||row.Detached||row.Held||!row.Moving||row.Installed.CurrentGeometry!=before||c.SourcePending.ContainsKey(row.Id)||c.SourceAllocating.ContainsKey(row.Id)||!c.SourceGoals.TryGetValue(row.Id,out var goal)||!goal.Equals(d.Endpoint.Position)||!c.SourceReservations.TryGetValue(row.Id,out var endpoint)||!endpoint.Equals(goal)||!CurrentHeldBinding(row.Installed.Request.Request.HeldIdentity,row.Id)||row.Installed.Request.Request.ProfileRevision!=((NavigationProfile)c.Core.Profile.Facade).Revision||!SameNavigationBinding(row.Installed.State.Profile,((NavigationProfile)c.Core.Profile.Facade).ForUnit(row.Radius,row.TargetSpeed,row.TargetTurn)))continue;
                NavRetainedArray<NavPoint> empty=null;InstalledGeometryMemberV2 member=null;NavRetainedArray<NavOwnedStorage> members=null;InstalledGeometryCertificateV2 certificate=null;
                try{
                    var local=row.Local as NavRetainedArray<NavPoint>;
                    if(local==null)local=empty=NavRetainedArray<NavPoint>.Copy(Array.Empty<NavPoint>(),r.TryReserve);
                    member=InstalledGeometryMemberV2.Create(row.Installed.Request,row.Installed.Corridor,row.Installed.Route,local,row.Position,r);
                    member.RemainingIndex=row.Actor.routeIndex;member.RemainingLocalIndex=row.Actor.localIndex;
                    members=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{member},r.TryReserve);
                    certificate=InstalledGeometryCertificateV2.TryCreate(row.Version,before,basis,members,r,wreckChanges:true);
                    if(certificate==null)continue;
                    row.NextInstalled=NavigationInstalledExecutionOwner.CarryOwned(row.Installed,certificate,r);row.Preserve=true;
                }finally{certificate?.Release();members?.Release();member?.Release();empty?.Release();}
            }
        }
    }
}
