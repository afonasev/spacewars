using System;
using System.Collections.Generic;
using UnityEngine;
namespace Spacewars.Input
{
    [Serializable] public sealed class NativeLocalInputProfile
    {
        public float deadzone,cursorSpeed,cameraSpeed,zoomSpeed,holdMs,selectAllHoldMs,selectionGrowth,cursorRadius,cursorWidth,doubleTapMs,cameraDefaultZoom,cameraMinZoom,cameraMaxZoom,radialDeadzone,radialRadius,basePageSize,controlGroupFocusRadius;
        public Field[] metadata;
        [Serializable] public sealed class Field {public string path,label,description,unit;public float min,max,step,value;}
        public void Validate()
        {
            if(metadata==null)throw new ArgumentException("Missing source Balance Lab metadata.");
            foreach(var field in metadata)if(string.IsNullOrEmpty(field.path)||string.IsNullOrEmpty(field.label)||string.IsNullOrEmpty(field.description)||string.IsNullOrEmpty(field.unit)||field.step<=0||field.value<field.min||field.value>field.max)throw new ArgumentException("Invalid source input metadata.");
            foreach(var field in metadata){var key=field.path.Substring(field.path.LastIndexOf('.')+1);if(key=="controlGroupDoubleTapMs")key="doubleTapMs";var member=GetType().GetField(key);if(member!=null&&member.FieldType==typeof(float)){float value=(float)member.GetValue(this);if(value!=field.value||Math.Abs((value-field.min)/field.step-Math.Round((value-field.min)/field.step))>.001)throw new ArgumentException("Input differs from source metadata: "+field.path);}}
            if(cameraMinZoom<=0||cameraMaxZoom<=cameraMinZoom||cameraDefaultZoom<cameraMinZoom||cameraDefaultZoom>cameraMaxZoom)throw new ArgumentException("Invalid source camera metadata.");
            if(doubleTapMs<=0)throw new ArgumentException("Missing source double-tap timing.");
            if(deadzone<=0||deadzone>=1||holdMs<=0||selectAllHoldMs<=holdMs||cursorSpeed<=0||cameraSpeed<=0)throw new ArgumentException("Invalid source input values.");
        }
    }
    public sealed class OfflinePadSector
    {
        public string Id;public bool Enabled;public string Hold;
        public OfflinePadSector(string id,bool enabled,string hold=null){Id=id;Enabled=enabled;Hold=hold;}
        public OfflinePadSector Copy()=>new OfflinePadSector(Id,Enabled,Hold);
    }
    public readonly struct OfflinePadIntent
    {
        public readonly string Kind,Id;public readonly double HeldMs;
        public OfflinePadIntent(string kind,string id=null,double heldMs=0){Kind=kind;Id=id;HeldMs=heldMs;}
    }
    // Port of source gamepadGestures: per-seat, explicit time, no authority/UI access.
    public sealed class OfflinePadGestures
    {
        private sealed class Gesture{public double Since;public string Mode;public bool Ground,Consumed,Add,DoubleTap;public OfflinePadSector Sector;public double? SectorSince;}
        private readonly Dictionary<int,Gesture> gestures=new Dictionary<int,Gesture>();
        private int previous;private double? rbTapAt;
        public string Mode{get;private set;}="world";
        public bool Blocked{get;private set;}=true;
        public bool Map=>Mode=="tacticalMap";
        public bool Added{get;private set;}
        public string AddedSectorId=>Added&&gestures.TryGetValue(128,out var rt)?rt.Sector?.Id:null;
        public bool AttackPreview{get;private set;}
        public bool ScreenPreview{get;private set;}
        public double SelectionHeldMs{get;private set;}
        public double LastHeldMs{get;private set;}
        public double Progress{get;private set;}
        public void SetMode(string mode){Mode=mode;Blocked=true;gestures.Clear();Added=false;rbTapAt=null;}
        public void Cancel(){SetMode("world");SelectionHeldMs=0;AttackPreview=false;ScreenPreview=false;Progress=0;}
        public IReadOnlyList<string> Step(double now,int mask,bool connected,bool focused,NativeLocalInputProfile profile,bool ground=true)
        {var result=StepDetailed(now,mask,connected,focused,profile,ground);var kinds=new List<string>();foreach(var intent in result)kinds.Add(intent.Kind);return kinds;}
        public IReadOnlyList<OfflinePadIntent> StepDetailed(double now,int mask,bool connected,bool focused,NativeLocalInputProfile profile,bool ground=true,OfflinePadSector sector=null)
        {
            int prior=previous;previous=mask;AttackPreview=false;ScreenPreview=false;SelectionHeldMs=0;Progress=0;
            var intents=new List<OfflinePadIntent>();bool pressed(int bit)=>(mask&bit)!=0&&(prior&bit)==0;
            void add(string kind,string id=null,double held=0)=>intents.Add(new OfflinePadIntent(kind,id,held));
            bool WorldOrMap()=>Mode=="world"||Mode=="tacticalMap";
            if(!connected||!focused){Cancel();return intents;}
            if(Blocked){if(mask==0)Blocked=false;return intents;}
            if((mask&~32)!=0){rbTapAt=null;if(gestures.TryGetValue(32,out var second)&&second.DoubleTap&&!second.Consumed)gestures.Remove(32);}
            if(pressed(16)){add("pause");SetMode("world");return intents;}
            if(pressed(8)&&(WorldOrMap()||Mode=="rallyTarget")){SetMode(Mode=="world"||Mode=="rallyTarget"?"tacticalMap":"world");return intents;}
            if(pressed(2)&&!WorldOrMap()){if(Mode=="buildingWheel")add("deselect");SetMode(Mode=="rallyTarget"?"buildingWheel":"world");return intents;}
            if(rbTapAt.HasValue&&now-rbTapAt.Value>profile.doubleTapMs){rbTapAt=null;if(WorldOrMap())add("cycleGroup");}
            foreach(int bit in new[]{128,32,64,1,2,4})
            {
                if(pressed(bit))
                {
                    if((bit==32||bit==64||bit==128||bit==4)&&!WorldOrMap())continue;
                    if(bit==1&&!(WorldOrMap()||Mode=="buildingWheel"||Mode=="rallyTarget"))continue;
                    if(bit==2&&!WorldOrMap())continue;
                    var started=new Gesture{Since=now,Mode=Mode,Ground=ground,Sector=sector?.Copy()};gestures[bit]=started;
                    if(bit==32){started.DoubleTap=rbTapAt.HasValue;rbTapAt=null;}
                    if(bit==128){Mode="groupAssign";gestures.Clear();gestures[128]=started;}
                }
                if(!gestures.TryGetValue(bit,out var gesture))continue;
                if(bit==128)
                {
                    var valid=sector!=null&&sector.Enabled&&sector.Id!="all"?sector:null;
                    double? since=valid==null?null:gesture.Sector?.Id==valid.Id&&gesture.SectorSince.HasValue?gesture.SectorSince:now;
                    double progress=since.HasValue?Math.Min(1,Math.Max(0,now-since.Value)/profile.holdMs):0;
                    if(!gesture.Consumed){gesture.Sector=valid?.Copy();gesture.SectorSince=since;if(valid!=null&&progress>=1){add("addGroup",valid.Id);gesture.Consumed=true;Added=true;}}
                    if((mask&128)!=0)Progress=gesture.Consumed?1:progress;
                    else{if(!gesture.Consumed&&valid!=null)add("assignGroup",valid.Id);SetMode(gesture.Mode=="tacticalMap"?"tacticalMap":"world");}
                    continue;
                }
                double held=Math.Max(0,now-gesture.Since);LastHeldMs=held;bool longer=held>=profile.holdMs;
                if((mask&bit)!=0)
                {
                    if((bit==32||bit==64)&&longer&&!gesture.Consumed){Mode=bit==64?"baseWheel":gesture.Add?"groupAssign":"groupWheel";gesture.Consumed=true;gestures.Clear();gestures[bit]=gesture;}
                    if(bit==1&&gesture.Mode=="buildingWheel")
                    {
                        bool valid=sector!=null&&sector.Enabled&&gesture.Sector!=null&&gesture.Sector.Enabled&&sector.Id==gesture.Sector.Id;
                        if(!valid){gesture.Consumed=true;continue;}
                        if(!gesture.Consumed&&gesture.Sector.Hold!=null){Progress=Math.Min(1,held/profile.holdMs);if(longer){add(gesture.Sector.Hold,gesture.Sector.Id);gesture.Consumed=true;}}
                    }
                    continue;
                }
                gestures.Remove(bit);
                if(bit==32||bit==64)
                {
                    if(gesture.Consumed){if(sector!=null&&sector.Enabled&&Mode!="groupAssign")add(bit==32?"recallGroup":"selectBase",sector.Id);SetMode(gesture.Mode=="tacticalMap"?"tacticalMap":"world");}
                    else if(!longer&&!(bit==32&&gesture.Add)&&gesture.Mode==Mode){if(bit==64)add("cycleBase");else if(gesture.DoubleTap)add("selectAllArmy");else rbTapAt=now;}
                    continue;
                }
                if(gesture.Consumed||gesture.Mode!=Mode)continue;
                if(bit==2)add(longer&&gesture.Ground?"attackMove":"context");
                if(bit==4)add(longer?"hold":"stop");
                if(bit==1)
                {
                    if(Mode=="world")add(held>=profile.selectAllHoldMs?"selectScreen":longer?"selectCircle":"select",held:held);
                    if(Mode=="tacticalMap"){if(longer)add("selectMapCircle",held:held);else{add("cameraJump");SetMode("world");}}
                    if(Mode=="rallyTarget"){add("rally");SetMode("buildingWheel");}
                    if(Mode=="buildingWheel"&&sector!=null&&sector.Enabled&&gesture.Sector!=null&&gesture.Sector.Enabled&&sector.Id==gesture.Sector.Id){if(longer&&gesture.Sector.Hold!=null)add(gesture.Sector.Hold,gesture.Sector.Id);else if(gesture.Sector.Hold!="sell")add("activate",gesture.Sector.Id);}
                }
            }
            bool wheel=Mode=="buildingWheel"||Mode=="groupWheel"||Mode=="groupAssign"||Mode=="baseWheel";
            if(!wheel){if(gestures.TryGetValue(1,out var a)&&(a.Mode=="world"||a.Mode=="tacticalMap")&&now-a.Since>=profile.holdMs){SelectionHeldMs=Math.Max(0,now-a.Since);ScreenPreview=a.Mode=="world"&&SelectionHeldMs>=profile.selectAllHoldMs;}if(gestures.TryGetValue(2,out var b))AttackPreview=b.Ground&&now-b.Since>=profile.holdMs;}
            return intents;
        }
        public static int? RingSector(double x,double y,int count,double deadzone)
        {if(count<=0||Math.Sqrt(x*x+y*y)<=deadzone)return null;double turn=Math.PI*2;return ((int)Math.Floor(((Math.Atan2(x,-y)+turn)%turn)/turn*count+.5))%count;}
    }
}
