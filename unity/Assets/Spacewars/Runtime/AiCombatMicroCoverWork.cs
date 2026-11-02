using System;
using System.Collections.Generic;
using System.IO;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Detached immutable ballistic input. Its factory charges the array zeroing
    // and copy separately from query work; it never retains an observation/Domain.
    internal sealed class AiCombatMicroCoverScene:IDisposable
    {
        internal readonly struct Building
        {
            internal readonly int Id,Health;internal readonly PlayableBuildingKind Kind;internal readonly ConstructionPhase Phase;
            internal readonly NavPoint Position;internal readonly double Heading,Progress;internal readonly bool Upgraded;
            internal Building(PlayableBuildingSnapshot b){Id=b.Id;Health=b.Health;Kind=b.Kind;Phase=b.Phase;Position=b.Position;Heading=b.Heading;Progress=b.Progress;Upgraded=b.ModelUpgraded;}
            internal void Write(BinaryWriter w){w.Write(Id);w.Write(Health);w.Write((int)Kind);w.Write((int)Phase);WorldWire.Write(w,Position);w.Write(Heading);w.Write(Progress);w.Write(Upgraded);}
        }
        internal readonly PlayableProfile Profile;internal readonly AiCombatSupportCatalog Supports;internal readonly double DirectHeight;
        readonly Building[] buildings;readonly NavObstacle[] solids;
        readonly AiCombatMicroExecutionStorage.Lease memory,catalogMemory;
        internal int BuildingCount=>buildings.Length;internal int SolidCount=>solids.Length;
        internal Building At(int i)=>buildings[i];internal NavObstacle Solid(int i)=>solids[i];
        AiCombatMicroCoverScene(PlayableProfile p,AiCombatSupportCatalog catalog,IReadOnlyList<PlayableBuildingSnapshot> source,IReadOnlyList<NavObstacle> obstacles,AiCombatMicroExecutionStorage.Lease memory,AiCombatMicroExecutionStorage.Lease catalogMemory)
        {Profile=p;Supports=catalog;DirectHeight=p.AuthoredMap?.DirectFireHeight??1;this.memory=memory;this.catalogMemory=catalogMemory;buildings=new Building[source.Count];solids=new NavObstacle[obstacles.Count];for(int i=0;i<buildings.Length;i++)buildings[i]=new Building(source[i]);for(int i=0;i<solids.Length;i++)solids[i]=obstacles[i];}
        internal static bool TryCreate(PlayableProfile profile,AiCombatSupportCatalog catalog,AiCombatMicroExecutionStorage.Lease catalogRoot,IReadOnlyList<PlayableBuildingSnapshot> source,IReadOnlyList<NavObstacle> solids,ref long copyGrant,AiCombatMicroExecutionStorage storage,out AiCombatMicroCoverScene scene)
        {
            scene=null;if(profile==null||catalog==null||catalogRoot==null||source==null||solids==null||storage==null)throw new ArgumentNullException();if(source.Count>WorldWire.MaxItems||solids.Count>WorldWire.MaxItems)throw new ArgumentException("Ballistic scene too large.");
            long cost=checked(2L*(source.Count+solids.Count));if(copyGrant<cost)return false;
            if(!storage.TryReserve(checked(256L+160L*source.Count+80L*solids.Count),out var memory))return false;
            if(!catalogRoot.TryShare(out var pin)){memory.Dispose();return false;}
            try{scene=new AiCombatMicroCoverScene(profile,catalog,source,solids,memory,pin);copyGrant-=cost;return true;}catch{pin.Dispose();memory.Dispose();throw;}
        }
        internal bool TryRetain(out AiCombatMicroExecutionStorage.Lease scenePin,out AiCombatMicroExecutionStorage.Lease catalogPin)
        {catalogPin=null;if(!memory.TryShareRetainedRoot(out scenePin))return false;if(catalogMemory.TryShareRetainedRoot(out catalogPin))return true;scenePin.Dispose();scenePin=null;return false;}
        void WriteProfileBinding(BinaryWriter w)
        {
            // Include every scalar used by Covers, not a hash/reference shortcut.
            WorldWire.String(w,Profile.ProfileId);w.Write(Profile.Revision);WorldWire.String(w,BuildingCombatGeometry.Version);
            w.Write(DirectHeight);w.Write(Profile.GaussRange);w.Write(Profile.ExplorerRange);w.Write(Profile.TurretRangeMultiplier);w.Write(Profile.HawkModelScale);w.Write(Profile.TurretModelScale);w.Write(Profile.TurretDepression);w.Write(Profile.GaussBeamRadius);w.Write(Profile.BallisticWallHeight);
            foreach(PlayableBuildingKind kind in Enum.GetValues(typeof(PlayableBuildingKind)))w.Write(BuildingCombatGeometry.ModelScale(Profile,kind));
            Supports.WriteBinding(w);
        }
        internal void WriteBinding(BinaryWriter w){WriteProfileBinding(w);w.Write(buildings.Length);foreach(var building in buildings)building.Write(w);w.Write(solids.Length);foreach(var solid in solids)WorldWire.Write(w,solid);}
        internal static AiCombatMicroCoverScene ReadFrozen(BinaryReader r,AiCombatMicroCoverScene source,AiCombatMicroExecutionStorage storage,ref long grant)
        {
            using(var comparison=new PredicateBindingComparison(r))using(var writer=new BinaryWriter(comparison,System.Text.Encoding.UTF8,true))source.WriteProfileBinding(writer);
            long remaining=r.BaseStream.Length-r.BaseStream.Position;if(remaining<0||remaining>WorldWire.MaxBytes||grant<remaining||!storage.TryReserve(checked(8192L+16L*remaining),out var scratch))throw new InvalidOperationException("Cold scene preparation refused.");
            using(scratch){int count=r.ReadInt32();if(count<0||count>WorldWire.MaxItems)throw new ArgumentException("Invalid cold building count.");var rows=new PlayableBuildingSnapshot[count];
                for(int i=0;i<count;i++){int id=r.ReadInt32(),health=r.ReadInt32();var kind=WorldWire.EnumValue<PlayableBuildingKind>(r);var phase=WorldWire.EnumValue<ConstructionPhase>(r);var point=WorldWire.ReadPoint(r);double heading=WorldWire.Number(r),progress=WorldWire.Number(r);bool upgraded=WorldWire.Boolean(r);if(id<1||progress<0||progress>1)throw new ArgumentException("Invalid cold ballistic building.");rows[i]=new PlayableBuildingSnapshot(id,PlayableOwner.Player,kind,point,health,progress,0,0,default,phase:phase,heading:heading,includePrivateState:false,refineryUpgraded:kind==PlayableBuildingKind.Refinery&&upgraded,scientificCenterUpgraded:kind==PlayableBuildingKind.ScientificCenter&&upgraded,producerUpgraded:(kind==PlayableBuildingKind.Factory||kind==PlayableBuildingKind.Hangar)&&upgraded);}
                count=r.ReadInt32();if(count<0||count>WorldWire.MaxItems)throw new ArgumentException("Invalid cold solid count.");var obstacles=new NavObstacle[count];for(int i=0;i<count;i++)obstacles[i]=WorldWire.ReadObstacle(r);
                if(!source.TryRetain(out var scenePin,out var catalogPin))throw new InvalidOperationException("Cold catalog pin refused.");try{long copy=checked(2L*(rows.Length+obstacles.Length));if(grant<remaining+copy)throw new InvalidOperationException("Cold scene copy refused.");grant-=remaining;long copyGrant=copy;if(!TryCreate(source.Profile,source.Supports,catalogPin,rows,obstacles,ref copyGrant,storage,out var result))throw new InvalidOperationException("Cold scene retention refused.");grant-=copy;return result;}finally{scenePin.Dispose();catalogPin.Dispose();}
            }
        }
        internal void CheckBinding(BinaryReader r){using(var comparison=new PredicateBindingComparison(r))using(var w=new BinaryWriter(comparison,System.Text.Encoding.UTF8,true))WriteBinding(w);}
        public void Dispose(){memory.Dispose();catalogMemory.Dispose();}
    }
    // One paid support edge, terrain sample, solid edge or prepared mesh node per
    // Advance step. No ordinary Covers/SupportAt/Pose decode call is hidden here.
    internal sealed class AiCombatMicroCoverWork:IDisposable
    {
        readonly AiCombatMicroCoverScene scene;readonly AiCombatMicroExecutionStorage storage;
        readonly int towerId,unitId;readonly PlayableBuildingKind towerKind;readonly PlayableEntityKind unitKind;
        readonly NavPoint tower,at;readonly double altitude;
        readonly string targetSupport,targetSurface;bool TypedTarget=>targetSurface!=null;
        readonly AiCombatMicroExecutionStorage.Lease self,scenePin,catalogPin;
        AiCombatMicroExecutionStorage.Lease supportMemory,heightMemory,inputMemory,lineMemory,poseMemory;
        AiCombatMicroSupportWork supportWork;TerrainLineInput input;TerrainLineWork line;CombatPoseWork pose;
        double[] heights;int heightCursor,fillCursor,solidCursor,buildingCursor,phase;long preparation;
        double ground,targetGround,buildingGround,radius;BallisticPoint muzzle,target;
        bool complete,covered,unsupported,disposed;
        internal bool Complete=>complete;internal bool Supported=>!unsupported;
        internal bool Covered{get{if(!complete||unsupported)throw new InvalidOperationException("Coverage unavailable.");return covered;}}
        internal long Consumed{get;private set;}
        AiCombatMicroCoverWork(AiCombatMicroCoverScene scene,AiCombatMicroExecutionStorage storage,int towerId,PlayableBuildingKind towerKind,NavPoint tower,int unitId,PlayableEntityKind kind,double altitude,NavPoint at,AiCombatMicroExecutionStorage.Lease self,AiCombatMicroExecutionStorage.Lease scenePin,AiCombatMicroExecutionStorage.Lease catalogPin,string targetSupport=null,string targetSurface=null)
        {this.targetSupport=targetSupport;this.targetSurface=targetSurface;this.scene=scene;this.storage=storage;this.towerId=towerId;this.towerKind=towerKind;this.tower=tower;this.unitId=unitId;unitKind=kind;this.altitude=altitude;this.at=at;this.self=self;this.scenePin=scenePin;this.catalogPin=catalogPin;}
        internal static bool TryCreate(AiCombatMicroCoverScene scene,AiCombatMicroExecutionStorage storage,PlayableBuildingSnapshot tower,PlayableEntitySnapshot unit,NavPoint at,out AiCombatMicroCoverWork work)
        {return TryCreate(scene,storage,tower.Id,tower.Kind,tower.Position,unit.Id,unit.Kind,unit.Altitude,at,out work);}
        internal static bool TryCreateTypedTarget(AiCombatMicroCoverScene scene,AiCombatMicroExecutionStorage storage,PlayableBuildingSnapshot tower,PlayableEntitySnapshot unit,NavLocation at,string support,out AiCombatMicroCoverWork work)
        {if(unit.Kind==PlayableEntityKind.Hawk)throw new ArgumentException("Ground support is not an air execution height law.");MotionExecutionBinding.Name(support);MotionExecutionBinding.Name(at.SurfaceId);return TryCreate(scene,storage,tower.Id,tower.Kind,tower.Position,unit.Id,unit.Kind,unit.Altitude,at.Position,out work,support,at.SurfaceId);}
        internal static bool TryCreateSemanticTarget(AiCombatMicroCoverScene scene,AiCombatMicroExecutionStorage storage,PlayableBuildingSnapshot tower,PlayableEntitySnapshot unit,NavLocation at,out AiCombatMicroCoverWork work)
        {if(unit.Kind==PlayableEntityKind.Hawk)throw new ArgumentException("Ground support is not an air execution height law.");MotionExecutionBinding.Name(at.SurfaceId);return TryCreate(scene,storage,tower.Id,tower.Kind,tower.Position,unit.Id,unit.Kind,unit.Altitude,at.Position,out work,null,at.SurfaceId);}
        static bool TryCreate(AiCombatMicroCoverScene scene,AiCombatMicroExecutionStorage storage,int towerId,PlayableBuildingKind towerKind,NavPoint tower,int unitId,PlayableEntityKind kind,double altitude,NavPoint at,out AiCombatMicroCoverWork work,string targetSupport=null,string targetSurface=null)
        {
            work=null;if(scene==null||storage==null)throw new ArgumentNullException();if(!TerritoryRules.ArmedDefense(towerKind)||!MotionExecutionBinding.Number(altitude)||!MotionExecutionBinding.Number(at.X)||!MotionExecutionBinding.Number(at.Z))throw new ArgumentException("Invalid coverage input.");
            if(!storage.TryReserve(8192,out var self))return false;if(!scene.TryRetain(out var scenePin,out var catalogPin)){self.Dispose();return false;}
            try{work=new AiCombatMicroCoverWork(scene,storage,towerId,towerKind,tower,unitId,kind,altitude,at,self,scenePin,catalogPin,targetSupport,targetSurface);return true;}catch{self.Dispose();scenePin.Dispose();catalogPin.Dispose();throw;}
        }
        bool Reserve(long bytes,ref AiCombatMicroExecutionStorage.Lease lease){if(lease!=null)throw new InvalidOperationException("Live allocation replaced.");return storage.TryReserve(bytes,out lease);}
        bool BeginSupport(NavPoint p,bool typed=false){if(!Reserve(AiCombatMicroSupportWork.RetainedBytes,ref supportMemory))return false;try{if(typed?(targetSupport!=null?AiCombatMicroSupportWork.TryCreateTyped(scene.Supports,new NavLocation(p,targetSurface),targetSupport,_=>true,out supportWork):AiCombatMicroSupportWork.TryCreateSemantic(scene.Supports,new NavLocation(p,targetSurface),_=>true,out supportWork)):AiCombatMicroSupportWork.TryCreate(scene.Supports,p,_=>true,out supportWork))return true;}catch{supportMemory.Dispose();supportMemory=null;throw;}supportMemory.Dispose();supportMemory=null;return false;}
        void EndSupport(){supportWork=null;supportMemory?.Dispose();supportMemory=null;}
        void Finish(bool value){covered=value;complete=true;ReleaseQueries();}
        void Unsupported(){unsupported=true;Finish(false);}
        void ReleaseQueries(){EndSupport();heightMemory?.Dispose();heightMemory=null;heights=null;inputMemory?.Dispose();inputMemory=null;input=null;lineMemory?.Dispose();lineMemory=null;line=null;poseMemory?.Dispose();poseMemory=null;pose=null;}
        // A refused allocation leaves this step pending and consumes no grant.
        internal int Advance(int grant,Func<bool> admit=null)
        {if(disposed)throw new ObjectDisposedException(nameof(AiCombatMicroCoverWork));if(grant<0)throw new ArgumentOutOfRangeException(nameof(grant));int used=0;while(used<grant&&!complete){if(admit!=null&&!admit()||!Step())break;used++;Consumed++;}return used;}
        bool Step()
        {
            var p=scene.Profile;
            if(phase==0){double range=towerKind==PlayableBuildingKind.GaussTower?p.GaussRange:p.ExplorerRange*p.TurretRangeMultiplier;if(unitKind==PlayableEntityKind.Hawk&&towerKind==PlayableBuildingKind.GaussTower||AiCombatMicroSafety.Distance(tower,at)>range){Finish(false);return true;}if(!BeginSupport(tower))return false;phase=1;return true;}
            if(phase==1||phase==3||phase==7||phase==15){
                if(!supportWork.Complete){supportWork.Advance(1);return true;}if(!supportWork.Supported){Unsupported();return true;}
                double height=supportWork.Height;EndSupport();if(phase==1){ground=height;phase=2;}else if(phase==3){targetGround=height;phase=4;}else if(phase==7){heights[heightCursor++]=height;phase=6;}else{buildingGround=height;phase=16;}return true;
            }
            if(phase==2){if(unitKind==PlayableEntityKind.Hawk){targetGround=0;phase=4;return true;}if(!BeginSupport(at,TypedTarget))return false;phase=3;return true;}
            if(phase==4){
                double targetHeight=unitKind==PlayableEntityKind.Hawk?altitude+1.8145018368959427*p.HawkModelScale/2:targetGround+scene.DirectHeight;
                target=new BallisticPoint(at.X,targetHeight,at.Z);double heading=Math.Atan2(at.Z-tower.Z,at.X-tower.X);
                double pivot=ground+BuildingCombatGeometry.ApronLift+(towerKind==PlayableBuildingKind.GaussTower?GaussTowerGeometry.PitchHeight:SentryTurretGeometry.PitchHeight)*p.TurretModelScale;
                double pitch=Math.Atan2(targetHeight-pivot,AiCombatMicroSafety.Distance(tower,at));if(pitch < -p.TurretDepression*Math.PI/180){Finish(false);return true;}
                muzzle=towerKind==PlayableBuildingKind.GaussTower?GaussTowerGeometry.Muzzle(tower,ground+BuildingCombatGeometry.ApronLift,p.TurretModelScale,heading,pitch):SentryTurretGeometry.Muzzle(tower,ground+BuildingCombatGeometry.ApronLift,p.TurretModelScale,heading,pitch,0);
                radius=towerKind==PlayableBuildingKind.GaussTower?p.GaussBeamRadius:0;phase=5;return true;
            }
            if(phase==5){int count=HeightCount();if(count==0){Unsupported();return true;}if(preparation<count){preparation++;return true;}if(!Reserve(checked(32L+8L*count),ref heightMemory))return false;heights=new double[count];preparation=0;phase=6;return true;}
            if(phase==6){if(heightCursor==heights.Length){phase=8;return true;}var q=Sample(heightCursor,heights.Length);if(!BeginSupport(q.Ground))return false;phase=7;return true;}
            if(phase==8){long needed=2L*(heights.Length+scene.SolidCount);if(preparation<needed){preparation++;return true;}long bytes=checked(256L+8L*heights.Length+80L*scene.SolidCount);if(!Reserve(bytes,ref inputMemory))return false;try{if(!TerrainLineInput.TryCreate(muzzle,target,radius,p.BallisticWallHeight,scene.SolidCount,preparation,_=>true,out input))throw new InvalidOperationException("Admitted line shape changed.");}catch{inputMemory.Dispose();inputMemory=null;throw;}phase=9;return true;}
            if(phase==9){if(fillCursor<heights.Length){input.AppendHeight(heights[fillCursor++]);return true;}phase=10;return true;}
            if(phase==10){if(solidCursor<scene.SolidCount){input.AppendSolid(scene.Solid(solidCursor++));return true;}input.Seal();phase=11;return true;}
            if(phase==11){if(!Reserve(128,ref lineMemory))return false;TerrainLineWork.TryCreate(input,_=>true,out line);phase=12;return true;}
            if(phase==12){if(line.Status==CombatPredicateStatus.Pending){line.Advance(1);return true;}if(!line.Clear){Finish(false);return true;}line=null;lineMemory.Dispose();lineMemory=null;input=null;inputMemory.Dispose();inputMemory=null;heightMemory.Dispose();heightMemory=null;heights=null;phase=14;return true;}
            if(phase==14){if(buildingCursor==scene.BuildingCount){Finish(true);return true;}var b=scene.At(buildingCursor);if(b.Id==towerId||b.Id==unitId||b.Phase==ConstructionPhase.Pending||b.Health<=0){buildingCursor++;return true;}if(!BeginSupport(b.Position))return false;phase=15;return true;}
            if(phase==16){var b=scene.At(buildingCursor);if(!BuildingCombatGeometry.TryPreparedPose(p,b.Kind,b.Upgraded,b.Position,buildingGround,out var prepared,heading:b.Heading,constructionProgress:b.Progress)){Unsupported();return true;}if(!prepared.TryCreateSegmentWork(muzzle,target,radius,bytes=>Reserve(bytes,ref poseMemory),out pose))return false;phase=17;return true;}
            if(phase==17){if(pose.Status==CombatPredicateStatus.Pending){pose.Advance(1);return true;}bool hit=pose.Hit;pose=null;poseMemory.Dispose();poseMemory=null;if(hit){Finish(false);return true;}buildingCursor++;phase=14;return true;}
            throw new InvalidOperationException("Invalid coverage phase.");
        }
        int HeightCount(){double distance=Math.Sqrt(Math.Pow(target.X-muzzle.X,2)+Math.Pow(target.Z-muzzle.Z,2)+Math.Pow(target.Y-muzzle.Y,2));double steps=Math.Max(1,Math.Ceiling(distance/.025));return steps>=WorldWire.MaxItems?0:(int)steps+1;}
        BallisticPoint Sample(int i,int count){double t=(double)i/(count-1);return new BallisticPoint(muzzle.X+(target.X-muzzle.X)*t,muzzle.Y+(target.Y-muzzle.Y)*t,muzzle.Z+(target.Z-muzzle.Z)*t);}
        // Save/restore is a declared linear lifecycle operation over detached
        // input. It is never charged as one query Advance. Cold admission derives
        // all liability from validated counts and rebuilds the partially filled
        // TerrainLineInput; no serialized free credit is trusted by the allocator.
        internal void Write(BinaryWriter w)
        {
            if(disposed)throw new ObjectDisposedException(nameof(AiCombatMicroCoverWork));
            w.Write(3);scene.WriteBinding(w);w.Write(towerId);w.Write((int)towerKind);WorldWire.Write(w,tower);w.Write(unitId);w.Write((int)unitKind);w.Write(altitude);WorldWire.Write(w,at);w.Write(TypedTarget);if(TypedTarget){WorldWire.String(w,targetSupport);WorldWire.String(w,targetSurface);}
            w.Write(phase);w.Write(heightCursor);w.Write(fillCursor);w.Write(solidCursor);w.Write(buildingCursor);w.Write(preparation);w.Write(ground);w.Write(targetGround);w.Write(buildingGround);w.Write(radius);WritePoint(w,muzzle);WritePoint(w,target);w.Write(complete);w.Write(covered);w.Write(unsupported);w.Write(Consumed);
            w.Write(heights?.Length??0);if(heights!=null)foreach(double height in heights)w.Write(height);
            w.Write(supportWork!=null);supportWork?.Write(w);w.Write(input!=null);w.Write(line!=null);line?.Write(w);w.Write(pose!=null);pose?.Write(w);
        }
        static void WritePoint(BinaryWriter w,BallisticPoint p){w.Write(p.X);w.Write(p.Y);w.Write(p.Z);}
        static BallisticPoint ReadPoint(BinaryReader r)=>new BallisticPoint(WorldWire.Number(r),WorldWire.Number(r),WorldWire.Number(r));
        internal static AiCombatMicroCoverWork Read(BinaryReader r,AiCombatMicroCoverScene scene,AiCombatMicroExecutionStorage storage,NavLocation? typedTarget=null,string typedSupport=null,double? airHeight=null,NavPoint? airPoint=null)
        {
            // Streaming scene/name comparisons have bounded transient UTF-8
            // buffers. Admit them before the first codec object/buffer birth.
            if(!storage.TryReserve(32768,out var scratch))throw new InvalidOperationException("Coverage codec scratch refused.");
            using(scratch)return ReadCore(r,scene,storage,typedTarget,typedSupport,airHeight,airPoint);
        }
        static AiCombatMicroCoverWork ReadCore(BinaryReader r,AiCombatMicroCoverScene scene,AiCombatMicroExecutionStorage storage,NavLocation? typedTarget,string typedSupport,double? airHeight,NavPoint? airPoint)
        {
            if(r.ReadInt32()!=3)throw new ArgumentException("Unknown coverage cursor.");scene.CheckBinding(r);
            int towerId=r.ReadInt32();var towerKind=(PlayableBuildingKind)r.ReadInt32();var tower=new NavPoint(WorldWire.Number(r),WorldWire.Number(r));int unitId=r.ReadInt32();var unitKind=(PlayableEntityKind)r.ReadInt32();double altitude=WorldWire.Number(r);var at=new NavPoint(WorldWire.Number(r),WorldWire.Number(r));
            if(airHeight.HasValue!=airPoint.HasValue||airHeight.HasValue&&(unitKind!=PlayableEntityKind.Hawk||typedTarget.HasValue||!MotionExecutionBinding.Same(altitude,airHeight.Value)||!MotionExecutionBinding.Same(at.X,airPoint.Value.X)||!MotionExecutionBinding.Same(at.Z,airPoint.Value.Z)))throw new ArgumentException("Foreign accepted Air coverage sample.");
            bool typed=WorldWire.Boolean(r);if(typed!=typedTarget.HasValue||!typed&&typedSupport!=null)throw new ArgumentException("Coverage typed source binding missing.");
            if(typed){if(typedSupport!=null)MotionExecutionBinding.Name(typedSupport);MotionExecutionBinding.Name(typedTarget.Value.SurfaceId);if(unitKind==PlayableEntityKind.Hawk||!MotionExecutionBinding.Same(at.X,typedTarget.Value.Position.X)||!MotionExecutionBinding.Same(at.Z,typedTarget.Value.Position.Z))throw new ArgumentException("Foreign typed coverage point.");using(var comparison=new PredicateBindingComparison(r))using(var w=new BinaryWriter(comparison,System.Text.Encoding.UTF8,true)){WorldWire.String(w,typedSupport);WorldWire.String(w,typedTarget.Value.SurfaceId);}}
            if(!Enum.IsDefined(typeof(PlayableEntityKind),unitKind)||!TryCreate(scene,storage,towerId,towerKind,tower,unitId,unitKind,altitude,at,out var work,typedSupport,typedTarget?.SurfaceId))throw new InvalidOperationException("Coverage cursor admission refused.");
            try{
                work.phase=r.ReadInt32();work.heightCursor=r.ReadInt32();work.fillCursor=r.ReadInt32();work.solidCursor=r.ReadInt32();work.buildingCursor=r.ReadInt32();work.preparation=r.ReadInt64();work.ground=WorldWire.Number(r);work.targetGround=WorldWire.Number(r);work.buildingGround=WorldWire.Number(r);work.radius=WorldWire.Number(r);work.muzzle=ReadPoint(r);work.target=ReadPoint(r);work.complete=WorldWire.Boolean(r);work.covered=WorldWire.Boolean(r);work.unsupported=WorldWire.Boolean(r);work.Consumed=r.ReadInt64();
                int count=r.ReadInt32();if(count<0||count>WorldWire.MaxItems||work.phase<0||work.phase>17||work.phase==13||work.heightCursor<0||work.fillCursor<0||work.solidCursor<0||work.solidCursor>scene.SolidCount||work.buildingCursor<0||work.buildingCursor>scene.BuildingCount||work.preparation<0||work.Consumed<0||work.radius<0||work.unsupported&&!work.complete||work.covered&&(!work.complete||work.unsupported))throw new ArgumentException("Invalid coverage cursor.");
                bool needsHeights=!work.complete&&work.phase>=6&&work.phase<=12;
                if(needsHeights!=(count>0)||count>0&&(count!=work.HeightCount()||work.heightCursor>count||work.fillCursor>count))throw new ArgumentException("Invalid height preparation shape.");
                if(count>0){if(!work.Reserve(checked(32L+8L*count),ref work.heightMemory))throw new InvalidOperationException("Height cursor admission refused.");work.heights=new double[count];for(int i=0;i<count;i++)work.heights[i]=WorldWire.Number(r);}
                bool hasSupport=WorldWire.Boolean(r);if(hasSupport!=(!work.complete&&(work.phase==1||work.phase==3||work.phase==7||work.phase==15)))throw new ArgumentException("Missing support phase.");
                if(hasSupport){if(!work.Reserve(AiCombatMicroSupportWork.RetainedBytes,ref work.supportMemory))throw new InvalidOperationException("Support cursor admission refused.");work.supportWork=AiCombatMicroSupportWork.Read(r,scene.Supports,_=>true);NavPoint expected=work.phase==1?work.tower:work.phase==3?work.at:work.phase==7&&work.heightCursor<count?work.Sample(work.heightCursor,count).Ground:work.phase==15&&work.buildingCursor<scene.BuildingCount?scene.At(work.buildingCursor).Position:throw new ArgumentException("Invalid support query cursor.");if(!work.supportWork.MatchesPoint(expected,work.phase==3?work.targetSupport:null,work.phase==3?work.targetSurface:null))throw new ArgumentException("Foreign support query point.");}
                bool hasInput=WorldWire.Boolean(r);if(hasInput!=(!work.complete&&work.phase>=9&&work.phase<=12))throw new ArgumentException("Missing line preparation.");
                if(hasInput){if(!work.Reserve(checked(256L+8L*count+80L*scene.SolidCount),ref work.inputMemory))throw new InvalidOperationException("Line input admission refused.");if(!TerrainLineInput.TryCreate(work.muzzle,work.target,work.radius,scene.Profile.BallisticWallHeight,scene.SolidCount,2L*(count+scene.SolidCount),_=>true,out work.input))throw new ArgumentException("Invalid native line shape.");for(int i=0;i<work.fillCursor;i++)work.input.AppendHeight(work.heights[i]);for(int i=0;i<work.solidCursor;i++)work.input.AppendSolid(scene.Solid(i));if(work.phase>=11)work.input.Seal();}
                bool hasLine=WorldWire.Boolean(r);if(hasLine!=(!work.complete&&work.phase==12))throw new ArgumentException("Missing terrain phase.");if(hasLine){if(!work.Reserve(128,ref work.lineMemory))throw new InvalidOperationException("Line cursor admission refused.");work.line=TerrainLineWork.Read(r,work.input,_=>true);}
                bool hasPose=WorldWire.Boolean(r);if(hasPose!=(!work.complete&&work.phase==17))throw new ArgumentException("Missing pose phase.");
                if(hasPose){if(work.buildingCursor==scene.BuildingCount)throw new ArgumentException("Invalid building cursor.");var b=scene.At(work.buildingCursor);if(!BuildingCombatGeometry.TryPreparedPose(scene.Profile,b.Kind,b.Upgraded,b.Position,work.buildingGround,out var prepared,heading:b.Heading,constructionProgress:b.Progress))throw new ArgumentException("Unprepared pose input.");work.pose=CombatPoseWork.Read(r,prepared,bytes=>work.Reserve(bytes,ref work.poseMemory));}
                if(!work.complete){if(work.phase==5&&work.preparation>work.HeightCount()||work.phase==8&&work.preparation>2L*(count+scene.SolidCount)||work.phase>=8&&work.phase<=12&&work.heightCursor!=count||work.phase>=10&&work.phase<=12&&work.fillCursor!=count||work.phase>=11&&work.phase<=12&&work.solidCursor!=scene.SolidCount)throw new ArgumentException("Impossible coverage preparation phase.");}
                return work;
            }catch{work.Dispose();throw;}
        }
        public void Dispose(){if(disposed)return;disposed=true;ReleaseQueries();self.Dispose();scenePin.Dispose();catalogPin.Dispose();}
    }
}
