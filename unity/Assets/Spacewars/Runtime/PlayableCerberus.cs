using System;
using System.Collections.Generic;
using System.IO;
using static Spacewars.Runtime.WorldWire;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private struct FlameArm
        {
            public int Target;
            public double Yaw, Heat, Length;
            public bool Locked;
            public void Write(BinaryWriter writer)
            {
                writer.Write(Target); writer.Write(Yaw); writer.Write(Heat);
                writer.Write(Length); writer.Write(Locked);
            }
            public static FlameArm Read(BinaryReader reader)
            {
                var arm = new FlameArm { Target = reader.ReadInt32(), Yaw = Number(reader),
                    Heat = Number(reader), Length = Number(reader), Locked = Boolean(reader) };
                if (arm.Target < 0 || arm.Heat < 0 || arm.Heat > 1 || arm.Length < 0)
                    throw new InvalidDataException("Invalid Cerberus weapon state.");
                return arm;
            }
        }

        private static CerberusWeaponPose? FlamePose(Unit u)=>u.Kind==PlayableEntityKind.Cerberus?new CerberusWeaponPose(u.FlameLeft.Yaw,u.FlameRight.Yaw,u.FlameLeft.Length,u.FlameRight.Length,u.FlameLeft.Heat,u.FlameRight.Heat):(CerberusWeaponPose?)null;

        private readonly List<(int victim, int source, PlayableOwner owner, double damage)> flameHits = new();
        private static NavPoint Offset(NavPoint at, double angle, double distance) =>
            new NavPoint(at.X + Math.Cos(angle) * distance, at.Z + Math.Sin(angle) * distance);
        private static double Bearing(NavPoint from, NavPoint to) => Math.Atan2(to.Z-from.Z,to.X-from.X);
        private double CerberusScale => PlayableUnitRules.CerberusModelScale(profile);
        private NavPoint FlamePivot(NavUnit body, int side) =>
            Offset(Offset(body.Position, body.Heading, .41*CerberusScale),body.Heading + side*Math.PI/2,1.17*CerberusScale);

        private static NavPoint FlameGradient(IPlayableTerrain terrain, NavLocation location)
        {
            if (terrain == null) return default;
            foreach (var support in terrain.Supports)
                if (support.SurfaceId == location.SurfaceId && support.Contains(location.Position))
                    return support.Gradient;
            return terrain.SurfaceGradient(location.Position);
        }

        private static double Slope(NavPoint gradient, NavPoint from, NavPoint to) =>
            gradient.X * (to.X - from.X) + gradient.Z * (to.Z - from.Z);

        private static double HorizontalLength(NavPoint gradient, double yaw, double length)
        {
            if (gradient.Equals(default)) return length;
            double slope = gradient.X * Math.Cos(yaw) + gradient.Z * Math.Sin(yaw);
            return length / Math.Sqrt(1 + slope * slope);
        }

        private static bool EntersRampSide(IPlayableTerrain terrain, NavLocation origin, NavPoint from, NavPoint to)
        {
            // A level-approach seam can be open at the foot or top. The
            // lateral edges behind that seam cannot be used as firing ports.
            if (!FlameGradient(terrain, origin).Equals(default)) return false;
            double floor = OrbitalRepairNanobots.Height(terrain, origin);
            foreach (var support in terrain.Supports)
            {
                var bounds = support.Bounds;
                if (support.Gradient.Equals(default) || bounds.MaxX < Math.Min(from.X, to.X) ||
                    bounds.MinX > Math.Max(from.X, to.X) || bounds.MaxZ < Math.Min(from.Z, to.Z) ||
                    bounds.MinZ > Math.Max(from.Z, to.Z)) continue;

                var start = bounds.Contains(from) ? origin.Position : from;
                if (bounds.Contains(start)) continue;
                double x = to.X - start.X, z = to.Z - start.Z;
                double first = double.PositiveInfinity;
                var vertices = bounds.Footprint.Vertices;
                for (int i = 0; i < vertices.Count; i++)
                {
                    var a = vertices[i];
                    var b = vertices[(i + 1) % vertices.Count];
                    double edgeX = b.X - a.X, edgeZ = b.Z - a.Z;
                    double cross = x * edgeZ - z * edgeX;
                    if (Math.Abs(cross) < 1e-10) continue;
                    double fromX = a.X - start.X, fromZ = a.Z - start.Z;
                    double fraction = (fromX * edgeZ - fromZ * edgeX) / cross;
                    double edgeFraction = (fromX * z - fromZ * x) / cross;
                    if (fraction >= 0 && fraction <= 1 && edgeFraction >= 0 && edgeFraction <= 1)
                        first = Math.Min(first, fraction);
                }

                if (double.IsPositiveInfinity(first)) continue;
                var crossing = new NavPoint(start.X + first * x, start.Z + first * z);
                if (Math.Abs(support.HeightAt(crossing) - floor) > 1e-6) return true;
            }
            return false;
        }

        internal static bool FlameTerrainClear(IPlayableTerrain terrain, NavLocation origin,
            BallisticPoint start, BallisticPoint end, double wallHeight)
        {
            // On stacked decks an XZ point alone cannot identify the floor beneath a nozzle.
            double Floor(NavPoint point)
            {
                if (!(terrain is ILayeredPlayableTerrain)) return terrain.SurfaceHeight(point);
                double lowest = double.PositiveInfinity;
                foreach (var support in terrain.Supports)
                {
                    if (!support.Contains(point)) continue;
                    double height = support.HeightAt(point);
                    if (support.SurfaceId == origin.SurfaceId) return height;
                    lowest = Math.Min(lowest, height);
                }
                return double.IsPositiveInfinity(lowest) ? double.NegativeInfinity : lowest;
            }
            return !EntersRampSide(terrain, origin, start.Ground, end.Ground) &&
                PlayableBallistics.TerrainLineClear(start, end, 0, terrain.Solids, wallHeight, Floor);
        }

        private bool FlameClear(NavLocation location, BallisticPoint start, BallisticPoint end) =>
            profile.AuthoredMap == null ? projectileGeometry.SegmentFree(start.Ground, end.Ground, 0) :
            FlameTerrainClear(profile.AuthoredMap, location, start, end, profile.BallisticWallHeight);

        private static bool FlameSurfacesConnect(IPlayableTerrain terrain, NavLocation from, NavLocation to) =>
            terrain == null || terrain.CompatibleCombatSurface(from, to) ||
            !FlameGradient(terrain, from).Equals(default) &&
                terrain.TryTraverse(from, to.Position, 0, out var reached) && reached.SurfaceId == to.SurfaceId;

        private bool FlameContact(Unit source, NavLocation location, int id, NavPoint from, out NavPoint point, double? streamYaw=null, double? damageLength=null, double? aimLimit=null)
        {
            point = default;
            if (id == source.Id || !Target(id,out point,out _) || AirTarget(id) || wrecks.ContainsKey(id)) return false;
            NavUnit nav = null;
            if (units.TryGetValue(id,out var victim) && (!TryActor(id,out nav) || Materializing(victim) ||
                !FlameSurfacesConnect(profile.AuthoredMap, location, nav.Location))) return false;
            var gradient = FlameGradient(profile.AuthoredMap, location);
            double height = OrbitalRepairNanobots.Height(profile.AuthoredMap, location) +
                1.34 * CerberusScale + Slope(gradient, location.Position, from);
            BallisticPoint AlongSlope(NavPoint pointOnSlope) =>
                new BallisticPoint(pointOnSlope.X, height + Slope(gradient, from, pointOnSlope), pointOnSlope.Z);
            var start = AlongSlope(from);
            if (buildings.TryGetValue(id,out var building))
                point = BuildingCombat(building).Closest(start).Ground;
            if(aimLimit.HasValue && Distance(from,point)>aimLimit.Value) return false;
            if(streamYaw.HasValue)
            {
                // Aim can track the outer cone. Damage requires overlap with the visible plume body.
                double delta=Angle(Bearing(from,point)-streamYaw.Value);
                double half=profile.CerberusAngle*Math.PI/360;
                double reach = HorizontalLength(gradient, streamYaw.Value, profile.CerberusRange);
                if (damageLength.HasValue)
                {
                    if (building==null) reach=damageLength.Value;
                    if (reach<=0) return false;
                    double distance=Distance(from,point);
                    double axial=Math.Clamp(distance*Math.Cos(delta)/reach,0,1);
                    // The rounded envelope narrows towards both endpoints. Recover the
                    // latitude used to make the mesh, without driving hits from shader noise.
                    double axis=1-Math.Pow(1-axial,1/1.36);
                    double radius=2*Math.Sqrt(axis*(1-axis))*(.12+1.18*axis);
                    double width=reach*Math.Tan(half)*PlayableUnitRules.CerberusPlumeWidthScale*radius;
                    half=Math.Min(half,Math.Atan2(width,Math.Max(.000001,axial*reach)));

                }
                double yaw=streamYaw.Value+Math.Max(-half,Math.Min(half,delta));
                var rayEnd=Offset(from,yaw,reach);
                if(building!=null)
                {
                    var shell=BuildingCombat(building);
                    if(!shell.Segment(start,AlongSlope(rayEnd),0,out var fraction,out var contact))return false;
                    if(damageLength.HasValue)
                    {
                        // The wall stops the visible jet at contact. Its surface takes
                        // damage only if the free jet would extend inside the mesh.
                        double interior=reach*fraction+.25*PlayableUnitRules.Radius(profile,source.Kind);
                        if(interior>reach)return false;
                        var probe=Offset(from,yaw,interior);
                        if(!shell.Overlaps(AlongSlope(probe),0))return false;
                    }
                    point=contact.Ground;
                }
                else
                {
                    double radius=PlayableUnitRules.Radius(profile,victim.Kind);
                    double floor=OrbitalRepairNanobots.Height(profile.AuthoredMap,nav.Location);
                    double verticalSpread=Distance(from,point)*Math.Tan(half);
                    double beamHeight = AlongSlope(point).Y;
                    if(beamHeight+verticalSpread<floor||beamHeight-verticalSpread>floor+2*radius)return false;
                    // A tangent to the collision footprint alone is insufficient.
                    double hitRadius=damageLength.HasValue ? radius*.75 : radius;
                    double t=Intersection(from,rayEnd,point,hitRadius);
                    if(double.IsPositiveInfinity(t))return false;
                    point=Offset(from,yaw,t*reach);
                }
            }
            double endHeight = AlongSlope(point).Y;
            if (!streamYaw.HasValue && nav != null)
                endHeight = OrbitalRepairNanobots.Height(profile.AuthoredMap,nav.Location) + 1.34*CerberusScale;
            var end = new BallisticPoint(point.X,endHeight,point.Z);
            if (!FlameClear(location,start,end)) return false;
            foreach (var obstacle in buildings.Values)
                if (obstacle.Id != id && obstacle.Phase!=ConstructionPhase.Pending && BuildingCombat(obstacle).Segment(start,end,0,out _,out _)) return false;
            return true;
        }

        private bool AimFlame(Unit unit, NavUnit body, int side, ref FlameArm arm, double dt)
        {
            var pivot = FlamePivot(body,side);
            int best = 0; bool preferredSide=false;
            double nearest = double.PositiveInfinity, heading = body.Heading;
            void Consider(int id)
            {
                if (!VisibleTarget(unit.Owner,id) || !FlameContact(unit,body.Location,id,pivot,out var point,aimLimit:profile.CerberusEngagementRange+.785*CerberusScale)) return;
                double yaw = Bearing(pivot,point), relative = Angle(yaw-body.Heading);
                // Both mounts cover the nose. Their outboard sectors do not cross the hull.
                if (Math.Abs(relative)>Math.PI/2 || side*relative < -Math.PI/4) return;
                double distance = Distance(pivot,point);
                bool ownSide=side*Angle(Bearing(body.Position,point)-body.Heading)>=0;
                if (preferredSide && !ownSide ||
                    preferredSide==ownSide && (distance>nearest || distance==nearest && id>=best)) return;
                preferredSide=ownSide; best=id; nearest=distance; heading=yaw;
            }
            if (unit.ExplicitTarget) Consider(unit.Target);
            else
            {
                targetSpatialIndex.Query(body.Position,profile.CerberusRange+2.5*CerberusScale+BuildingRadius(PlayableBuildingKind.Factory),targetSpatialIndex.Candidates);
                foreach (var candidate in targetSpatialIndex.Candidates)
                {
                    int id=candidate.Id;
                    if (units.TryGetValue(id,out var enemy) && Hostile(unit.Owner,enemy.Owner) ||
                        buildings.TryGetValue(id,out var fort) && Hostile(unit.Owner,fort.Owner)) Consider(id);
                }
            }
            arm.Target=best;
            arm.Yaw=Turn(arm.Yaw,Angle(heading-body.Heading),profile.TankTurretTurnSpeed*dt);
            if (best==0 || Math.Abs(Angle(heading-body.Heading-arm.Yaw))>profile.CerberusAngle*Math.PI/360)
                return false;
            double firingYaw=body.Heading+arm.Yaw;
            var nozzle=Offset(pivot,firingYaw,.785*CerberusScale);
            return FlameContact(unit,body.Location,best,nozzle,out _,firingYaw);
        }

        private void FireFlame(Unit unit,NavUnit body,int side,ref FlameArm arm,bool ready,double dt,double allowance)
        {
            arm.Length=0;
            if (!ready || arm.Locked || unit.FlameSync)
            {
                arm.Heat=Math.Max(0,arm.Heat-dt/profile.CerberusCooldown);
                if (arm.Heat<1e-10) { arm.Heat=0; arm.Locked=false; }
                return;
            }
            double active=Math.Min(allowance,(1-arm.Heat)*profile.CerberusFireTime);
            if (active<=0) return;
            arm.Heat=Math.Min(1,arm.Heat+active/profile.CerberusFireTime);
            if (arm.Heat>=1-1e-10) { arm.Heat=1; arm.Locked=true; }
            var direction=body.Heading+arm.Yaw;
            var muzzle=Offset(FlamePivot(body,side),direction,.785*CerberusScale);
            var gradient = FlameGradient(profile.AuthoredMap, body.Location);
            double range = HorizontalLength(gradient, direction, profile.CerberusRange);
            var end = Offset(muzzle, direction, range);
            double height = OrbitalRepairNanobots.Height(profile.AuthoredMap, body.Location) +
                1.34 * CerberusScale + Slope(gradient, body.Position, muzzle);
            BallisticPoint AlongSlope(NavPoint location) =>
                new BallisticPoint(location.X, height + Slope(gradient, muzzle, location), location.Z);
            var a = AlongSlope(muzzle);
            var b = AlongSlope(end);
            double clearRange = range;
            foreach (var fort in buildings.Values)
                if (fort.Phase!=ConstructionPhase.Pending && BuildingCombat(fort).Segment(a,b,0,out var fraction,out _))
                    clearRange = Math.Min(clearRange, range * fraction);
            bool ClearLength(double length)
            {
                var tip=Offset(muzzle,direction,length);
                return FlameClear(body.Location, a, AlongSlope(tip));
            }
            if (!ClearLength(clearRange))
            {
                double lo=0,hi=clearRange;
                for(int i=0;i<10;i++){double mid=(lo+hi)/2;if(ClearLength(mid))lo=mid;else hi=mid;}
                clearRange=lo;
            }
            arm.Length = gradient.Equals(default) ? clearRange : profile.CerberusRange * (clearRange / range);
            targetSpatialIndex.Query(muzzle,range+BuildingRadius(PlayableBuildingKind.Factory),targetSpatialIndex.Candidates);
            foreach (var candidate in targetSpatialIndex.Candidates)
            {
                if (!FlameContact(unit,body.Location,candidate.Id,muzzle,out _,direction,damageLength:clearRange)) continue;
                flameHits.Add((candidate.Id,unit.Id,unit.Owner,profile.CerberusDamage*WeaponMultiplier(unit.Owner)*active));
            }
        }

        // Evaluate the two real nozzle origins at the common navigator's proposed stance.
        private bool DualFlameStance(Unit unit, NavUnit body, NavPoint stance, int id, NavPoint target)
        {
            var location = body.Location;
            if (!stance.Equals(body.Position))
            {
                if (profile.AuthoredMap == null) location = new NavLocation(stance, NavLocation.FlatSurface);
                else if (!profile.AuthoredMap.TryLocate(stance, PlayableUnitRules.Radius(profile, unit.Kind), out location))
                    return false;
            }
            double forward=Bearing(stance,target);
            for(int side=-1;side<=1;side+=2)
            {
                var mount=Offset(Offset(stance,forward,.41*CerberusScale),forward+side*Math.PI/2,1.17*CerberusScale);
                double aim=Bearing(mount,target);
                if(Math.Abs(Angle(aim-forward))>Math.PI/2)return false;
                var nozzle=Offset(mount,aim,.785*CerberusScale);
                // Use the same aim point as acquisition. A cone grazing the hull
                // outside that range cannot serve as a dual-weapon firing stance.
                if(!FlameContact(unit,location,id,nozzle,out var aimPoint) ||
                    Distance(nozzle,aimPoint)>profile.CerberusEngagementRange ||
                    !FlameContact(unit,location,id,nozzle,out _,aim))return false;
            }
            return true;
        }

        private void AdvanceCerberus(Unit unit,NavUnit body,double dt)
        {
            RetireLostTargetOrder(unit);
            unit.Repath=Math.Max(0,unit.Repath-dt);
            bool left=AimFlame(unit,body,1,ref unit.FlameLeft,dt);
            bool right=AimFlame(unit,body,-1,ref unit.FlameRight,dt);
            int primary=unit.FlameLeft.Target!=0?unit.FlameLeft.Target:unit.FlameRight.Target;
            if(!unit.ExplicitTarget)unit.Target=primary;
            bool common=primary!=0&&unit.FlameLeft.Target==unit.FlameRight.Target;
            double allowance=dt;
            if(common && (unit.FlameLeft.Locked || unit.FlameRight.Locked))unit.FlameSync=true;
            if(common && left && right)
                allowance=Math.Min(dt,(1-Math.Max(unit.FlameLeft.Heat,unit.FlameRight.Heat))*profile.CerberusFireTime);
            FireFlame(unit,body,1,ref unit.FlameLeft,left,dt,allowance);
            FireFlame(unit,body,-1,ref unit.FlameRight,right,dt,allowance);
            if(common && (unit.FlameLeft.Locked || unit.FlameRight.Locked))unit.FlameSync=true;
            if(unit.FlameLeft.Heat==0 && unit.FlameRight.Heat==0)unit.FlameSync=false;
            if(primary!=0 && (unit.FlameLeft.Length>0||unit.FlameRight.Length>0))unit.LastCombatShotTarget=primary;
            bool combatOrder=unit.HasAttackMove||unit.ExplicitTarget;
            if(unit.Target!=0 && Target(unit.Target,out var target,out _))
            {
                bool twoSectors=unit.FlameLeft.Target!=0&&unit.FlameRight.Target!=0;
                bool canConverge=twoSectors||DualFlameStance(unit,body,body.Position,unit.Target,target);
                if(combatOrder && primary!=0 && canConverge && body.Moving)CombatStop(unit.Id);
                if(!body.Moving && unit.CurrentOrder?.Kind!=PlayableTacticalOrderKind.Move && (!twoSectors || common))
                    body.Heading=Turn(body.Heading,Bearing(body.Position,target),PlayableUnitRules.Turn(profile,unit.Kind)*dt);
                if(!canConverge && combatOrder && !body.Held && unit.Repath<=0 && !navigation.IsPending(unit.Id))
                {
                    unit.Repath=profile.AttackRepathSeconds;
                    bool found=TryAttackApproach(unit.Id,body.Position,target,out var approach,
                        point=>CanAdmitAttackMoveApproach(unit.Id,point));
                    if(found && (!body.Moving || Distance(body.Goal,approach)>profile.Navigation.ArrivalTolerance))
                        CombatMove(unit.Id,approach);
                    else if(!found && primary!=0 && body.Moving)CombatStop(unit.Id);
                }
            }
            if(primary==0 && unit.HasAttackMove && !body.Held)ContinueAttackMove(unit,body);
        }

        private const int FlameHitMarker=0x43455248;
        private void WriteFlameHits(BinaryWriter writer)
        {
            if(flameHits.Count==0)return;
            writer.Write(FlameHitMarker);writer.Write(flameHits.Count);
            foreach(var hit in flameHits)
            {writer.Write(hit.victim);writer.Write(hit.source);writer.Write((int)hit.owner);writer.Write(hit.damage);}
        }
        private void ReadFlameHits(BinaryReader reader)
        {
            long start=reader.BaseStream.Position;
            if(start==reader.BaseStream.Length)return;
            if(reader.ReadInt32()!=FlameHitMarker){reader.BaseStream.Position=start;return;}
            int count=reader.ReadInt32();
            if(count<1||count>(reader.BaseStream.Length-reader.BaseStream.Position)/20)throw new InvalidDataException("Invalid pending flame contacts.");
            for(int i=0;i<count;i++)
            {
                int victim=reader.ReadInt32(),source=reader.ReadInt32();
                var owner=EnumValue<PlayableOwner>(reader);double damage=Number(reader);
                if(victim<1||source<1||damage<=0)throw new InvalidDataException("Invalid flame contact.");
                flameHits.Add((victim,source,owner,damage));
            }
        }

        private void ApplyFlameHits()
        {
            int remaining=0;
            for(int i=0;i<flameHits.Count;i++)
            {
                var hit=flameHits[i];
                if(!units.ContainsKey(hit.victim)&&!buildings.ContainsKey(hit.victim))continue;
                if(!TryDamagePrepared(hit.victim,hit.damage,hit.source,hit.owner,null,default,null,null,uniformDamage:true))flameHits[remaining++]=hit;
            }
            flameHits.RemoveRange(remaining,flameHits.Count-remaining);
        }
    }
}
