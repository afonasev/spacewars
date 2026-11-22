using System;
using System.Collections.Generic;
using UnityEngine;
namespace Spacewars.Input
{
    [Serializable] public sealed class NativeLocalInputProfile
    {
        public float deadzone,cursorSpeed,mapCursorSpeed,cameraSpeed,zoomSpeed,rotationSpeed,holdMs,holdFeedbackDelayMs,selectAllHoldMs,selectionGrowth,cursorRadius,cursorWidth,doubleTapMs,cameraDefaultZoom,cameraMinZoom,cameraMaxZoom,radialDeadzone,radialRadius,basePageSize,controlGroupFocusRadius,cursorRecenterIdleMs;
        private const int RadialHoldMultiplier = 2;
        public double RadialHoldMs => RadialHoldMultiplier * holdMs;
        public double RadialHoldFeedbackDelayMs => RadialHoldMultiplier * holdFeedbackDelayMs;
        public Field[] metadata;
        [Serializable] public sealed class Field {public string path,label,description,unit;public float min,max,step,value;}
        public void Validate()
        {
            if(metadata==null)throw new ArgumentException("Missing source Balance Lab metadata.");
            foreach(var field in metadata)if(string.IsNullOrEmpty(field.path)||string.IsNullOrEmpty(field.label)||string.IsNullOrEmpty(field.description)||string.IsNullOrEmpty(field.unit)||field.step<=0||field.value<field.min||field.value>field.max)throw new ArgumentException("Invalid source input metadata.");
            foreach(var field in metadata){var key=field.path.Substring(field.path.LastIndexOf('.')+1);if(key=="controlGroupDoubleTapMs")key="doubleTapMs";var member=GetType().GetField(key);if(member!=null&&member.FieldType==typeof(float)){float value=(float)member.GetValue(this);if(value!=field.value||Math.Abs((value-field.min)/field.step-Math.Round((value-field.min)/field.step))>.001)throw new ArgumentException("Input differs from source metadata: "+field.path);}}
            if(cameraMinZoom<=0||cameraMaxZoom<=cameraMinZoom||cameraDefaultZoom<cameraMinZoom||cameraDefaultZoom>cameraMaxZoom)throw new ArgumentException("Invalid source camera metadata.");
            if((float.IsNaN(cursorRecenterIdleMs)||float.IsInfinity(cursorRecenterIdleMs))||cursorRecenterIdleMs<0||!Array.Exists(metadata,f=>f.path=="system.gamepad.cursorRecenterIdleMs"))throw new ArgumentException("Missing cursor recenter timing metadata.");
            if(mapCursorSpeed<=0||!Array.Exists(metadata,f=>f.path=="system.gamepad.mapCursorSpeed"))throw new ArgumentException("Missing tactical map cursor speed metadata.");
            if(rotationSpeed<=0||!Array.Exists(metadata,f=>f.path=="system.gamepad.rotationSpeed"))throw new ArgumentException("Missing camera rotation speed metadata.");
            if(doubleTapMs<=0)throw new ArgumentException("Missing source double-tap timing.");
            if(holdFeedbackDelayMs<0||holdFeedbackDelayMs>=holdMs||!Array.Exists(metadata,f=>f.path=="system.gamepad.holdFeedbackDelayMs"))throw new ArgumentException("Invalid hold feedback delay metadata.");
            if(deadzone<=0||deadzone>=1||holdMs<=0||selectAllHoldMs<=holdMs||cursorSpeed<=0||cameraSpeed<=0)throw new ArgumentException("Invalid source input values.");
        }
    }
    public sealed class OfflinePadSector
    {
        public string Id;public bool Enabled,RecallEnabled,CanAssign,CanAdd;public string Hold;
        public OfflinePadSector(string id,bool enabled,string hold=null){Id=id;Enabled=enabled;RecallEnabled=enabled;Hold=hold;}
        public OfflinePadSector Copy()=>new OfflinePadSector(Id,Enabled,Hold){RecallEnabled=RecallEnabled,CanAssign=CanAssign,CanAdd=CanAdd};
    }
    public readonly struct OfflinePadIntent
    {
        public readonly string Kind,Id;public readonly double HeldMs;public readonly bool Append;
        public OfflinePadIntent(string kind,string id=null,double heldMs=0,bool append=false){Kind=kind;Id=id;HeldMs=heldMs;Append=append;}
    }
    // Per-seat gestures with explicit time and no authority/UI access.
    public sealed class OfflinePadGestures
    {
        private sealed class Gesture{public double Since;public string Mode;public bool Ground,Consumed,DoubleTap,WheelRevealed,TapCancelled;public OfflinePadSector Sector;}
        private readonly Dictionary<int,Gesture> gestures=new Dictionary<int,Gesture>();
        private int previous;private double? rbTapAt;
        public string Mode{get;private set;}="world";
        public bool Blocked{get;private set;}=true;
        public bool Map=>Mode=="tacticalMap"||Mode=="rallyMap";
        public bool AuxiliaryWheelVisible=>Mode=="baseWheel"||Mode=="groupAssign"||(Mode=="groupWheel"&&gestures.TryGetValue(32,out var rb)&&rb.WheelRevealed);
        public bool Added{get;private set;}
        public string AddedSectorId=>Added&&gestures.TryGetValue(32,out var rb)?rb.Sector?.Id:null;
        public bool AttackPreview{get;private set;}
        public bool QueuePreview{get;private set;}
        public bool ScreenPreview{get;private set;}
        public double SelectionHeldMs{get;private set;}
        public double LastHeldMs{get;private set;}
        public double Progress{get;private set;}
        public void SetMode(string mode){Mode=mode;Blocked=true;gestures.Clear();Progress=0;Added=false;rbTapAt=null;AttackPreview=false;QueuePreview=false;}
        public void Cancel(){SetMode("world");SelectionHeldMs=0;AttackPreview=false;ScreenPreview=false;Progress=0;}
        public IReadOnlyList<string> Step(double now,int mask,bool connected,bool focused,NativeLocalInputProfile profile,bool ground=true)
        {var result=StepDetailed(now,mask,connected,focused,profile,ground);var kinds=new List<string>();foreach(var intent in result)kinds.Add(intent.Kind);return kinds;}
        public IReadOnlyList<OfflinePadIntent> StepDetailed(double now,int mask,bool connected,bool focused,NativeLocalInputProfile profile,bool ground=true,OfflinePadSector sector=null)
        {
            // RT is deliberately unbound; it must not affect neutral gates or pending RB taps.
            mask&=~128;int prior=previous;previous=mask;QueuePreview=false;AttackPreview=false;ScreenPreview=false;SelectionHeldMs=0;Progress=0;
            var intents=new List<OfflinePadIntent>();bool pressed(int bit)=>(mask&bit)!=0&&(prior&bit)==0;
            void add(string kind,string id=null,double held=0,bool append=false)=>intents.Add(new OfflinePadIntent(kind,id,held,append));
            bool WorldOrMap()=>Mode=="world"||Mode=="tacticalMap";
            if(!connected||!focused){Cancel();return intents;}
            if(Blocked){if(mask==0)Blocked=false;return intents;}
            if((mask&~32)!=0){rbTapAt=null;if(gestures.TryGetValue(32,out var second)){second.TapCancelled=true;if(second.DoubleTap&&!second.Consumed)second.DoubleTap=false;}}
            if(pressed(16)){add("pause");SetMode("world");return intents;}
            if(pressed(8)&&(WorldOrMap()||Mode=="rallyTarget"||Mode=="rallyMap"))
            {SetMode(Mode=="rallyTarget"?"rallyMap":Mode=="rallyMap"?"rallyTarget":Mode=="world"?"tacticalMap":"world");return intents;}
            if(pressed(2)&&!WorldOrMap())
            {bool rally=Mode=="rallyTarget"||Mode=="rallyMap";if(rally)add("cancelRally");else if(Mode=="buildingWheel")add("deselect");SetMode(rally?"buildingWheel":"world");return intents;}
            if(rbTapAt.HasValue&&now-rbTapAt.Value>profile.doubleTapMs)
            {
                rbTapAt=null;
                // The first tap already changed selection. Only a confirmed single tap can move the camera.
                add("focusCycledGroup");
            }
            bool AuxiliaryA()=>AuxiliaryWheelVisible&&!Added;
            foreach(int bit in new[]{32,64,1,2,256,4})
            {
                if(pressed(bit))
                {
                    if((bit==32||bit==64||bit==4)&&!WorldOrMap())continue;
                    if(bit==1)
                    {
                        bool acceptsA=WorldOrMap()
                            ? !gestures.ContainsKey(64)
                            : AuxiliaryA()||Mode=="buildingWheel"||Mode=="rallyTarget"||Mode=="rallyMap";
                        if(!acceptsA)continue;
                    }
                    if((bit==2||bit==256)&&!WorldOrMap())continue;
                    var started=new Gesture{Since=now,Mode=Mode,Ground=ground,Sector=sector?.Copy()};gestures[bit]=started;
                    if(bit==32){started.DoubleTap=rbTapAt.HasValue;rbTapAt=null;Mode="groupWheel";gestures.Clear();gestures[32]=started;}
                }
                if(!gestures.TryGetValue(bit,out var gesture))continue;
                if(bit==32)
                {
                    bool heldOpen=now-gesture.Since>=profile.RadialHoldMs;
                    if((mask&32)!=0)
                    {
                        if(heldOpen)gesture.WheelRevealed=true;
                        if(!gesture.Consumed&&pressed(4)&&sector!=null&&sector.Enabled&&sector.CanAdd)
                        {
                            gesture.Sector=sector.Copy();
                            gesture.Consumed=true;
                            gestures.Remove(1);
                            Added=true;
                            add("addGroup",sector.Id);
                        }
                    }
                    else
                    {
                        bool tap=!heldOpen&&!gesture.Consumed&&!gesture.TapCancelled;
                        bool doubleTap=gesture.DoubleTap;
                        SetMode(gesture.Mode=="tacticalMap"?"tacticalMap":"world");
                        // This release is already a neutral sample. Do not require an
                        // extra idle frame before the fastest second RB press.
                        Blocked=mask!=0;
                        if(tap){if(doubleTap)add("selectAllArmy");else{add("cycleGroup");rbTapAt=now;}}
                        return intents;
                    }
                    continue;
                }
                double held=Math.Max(0,now-gesture.Since);LastHeldMs=held;bool longer=held>=profile.holdMs;
                bool radialLonger=held>=profile.RadialHoldMs;
                if(bit==1&&gesture.Mode==Mode&&AuxiliaryA())
                {
                    bool valid=SameEnabledSector(gesture.Sector,sector);
                    bool canAssign=Mode=="groupWheel"&&valid&&gesture.Sector.CanAssign&&sector.CanAssign;
                    if((mask&1)!=0)
                    {
                        if(!valid)gesture.Consumed=true;
                        if(!gesture.Consumed&&canAssign)Progress=HoldFeedback(held,profile);
                    }
                    else
                    {
                        gestures.Remove(1);
                        if(!gesture.Consumed&&valid)
                        {
                            if(canAssign&&radialLonger)add("assignGroup",sector.Id);
                            if(!radialLonger&&Mode=="groupWheel"&&sector.RecallEnabled)add("recallGroup",sector.Id);
                            if(!radialLonger&&Mode=="baseWheel")add("selectBase",sector.Id);
                        }
                    }
                    continue;
                }
                if((mask&bit)!=0)
                {
                    if(bit==64&&radialLonger&&!gesture.Consumed){Mode="baseWheel";gesture.Consumed=true;gestures.Clear();gestures[bit]=gesture;}
                    if(bit==1&&gesture.Mode=="world"&&!gesture.Consumed&&held>=profile.selectAllHoldMs){add("selectScreen",held:held);gesture.Consumed=true;}
                    if(bit==1&&gesture.Mode=="buildingWheel")
                    {
                        bool valid=SameEnabledSector(gesture.Sector,sector);
                        if(!valid){gesture.Consumed=true;continue;}
                        if(!gesture.Consumed&&gesture.Sector.Hold!=null){Progress=HoldFeedback(held,profile);if(radialLonger){add(gesture.Sector.Hold,gesture.Sector.Id);gesture.Consumed=true;}}
                    }
                    continue;
                }
                gestures.Remove(bit);
                if(bit==64)
                {
                    if(gesture.Consumed)
                    {
                        SetMode(gesture.Mode);
                        Blocked=mask!=0;
                        return intents;
                    }
                    if(!radialLonger&&gesture.Mode==Mode)add("cycleBase");
                    continue;
                }
                if(gesture.Consumed||gesture.Mode!=Mode)continue;
                if(bit==4||bit==256)add(bit==256?"attackMove":"context",held:held,append:longer);
                if(bit==2)add(longer?"hold":"stop");
                if(bit==1)
                {
                    if(Mode=="world")add(held>=profile.selectAllHoldMs?"selectScreen":longer?"selectCircle":"select",held:held);
                    if(Mode=="tacticalMap"){if(longer)add("selectMapCircle",held:held);else{add("cameraJump");SetMode("world");}}
                    if(Mode=="rallyTarget"||Mode=="rallyMap"){add("rally");SetMode("buildingWheel");}
                    // Confirming rally changes Mode to buildingWheel; the same release
                    // must not fall through and activate the petal under a held stick.
                    else if(Mode=="buildingWheel"&&SameEnabledSector(gesture.Sector,sector)){if(radialLonger&&gesture.Sector.Hold!=null)add(gesture.Sector.Hold,gesture.Sector.Id);else if(gesture.Sector.Hold!="sell")add("activate",gesture.Sector.Id);}
                }
            }
            bool wheel=Mode=="buildingWheel"||Mode=="groupWheel"||Mode=="groupAssign"||Mode=="baseWheel";
            if(!wheel){if(gestures.TryGetValue(1,out var a)&&(a.Mode=="world"||a.Mode=="tacticalMap")&&now-a.Since>=profile.holdMs){SelectionHeldMs=Math.Max(0,now-a.Since);ScreenPreview=a.Mode=="world"&&SelectionHeldMs>=profile.selectAllHoldMs;}AttackPreview=gestures.ContainsKey(256);QueuePreview=(gestures.TryGetValue(4,out var x)&&now-x.Since>=profile.holdMs)||(gestures.TryGetValue(256,out var y)&&now-y.Since>=profile.holdMs);}
            return intents;
        }
        private static bool SameEnabledSector(OfflinePadSector started,OfflinePadSector current)
            =>started!=null&&started.Enabled&&current!=null&&current.Enabled&&started.Id==current.Id;
        private static double HoldFeedback(double elapsed,NativeLocalInputProfile profile)
            =>elapsed<=profile.RadialHoldFeedbackDelayMs?0:Math.Min(1,(elapsed-profile.RadialHoldFeedbackDelayMs)/(profile.RadialHoldMs-profile.RadialHoldFeedbackDelayMs));
        public static int? RingSector(double x,double y,int count,double deadzone)
        {if(count<=0||Math.Sqrt(x*x+y*y)<=deadzone)return null;double turn=Math.PI*2;return ((int)Math.Floor(((Math.Atan2(x,-y)+turn)%turn)/turn*count+.5))%count;}
    }
}
