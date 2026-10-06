using System;
using System.IO;
using System.Linq;
using System.Globalization;
using NUnit.Framework;
using Spacewars.Input;
using UnityEngine;

namespace Spacewars.Tests.PlayMode
{
    public sealed class OfflinePadSelectionSourceTests
    {
        [Test] public void ActualSourceControllerSelectionMatchesEveryNativeSample()
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());const string path="unity/Tests/Fixtures/native-two-seat-gamepad-parity/source-controller-selection.tsv";
            while(root!=null&&!File.Exists(Path.Combine(root.FullName,path)))root=root.Parent;Assert.NotNull(root);
            var profile=JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text);profile.Validate();
            var obj=new GameObject("source-controller-projection");var camera=obj.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=10;camera.aspect=1;camera.nearClipPlane=.1f;camera.farClipPlane=200;camera.transform.position=new Vector3(0,100,0);camera.transform.rotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);
            var gesture=new OfflinePadGestures();var selection=new OfflinePadSelection();string name=null;int[] selected=Array.Empty<int>();int samples=0;
            try {foreach(var line in File.ReadAllLines(Path.Combine(root.FullName,path)).Skip(1)){
                var r=line.Split('|');if(name!=r[0]){name=r[0];gesture=new OfflinePadGestures();selection=new OfflinePadSelection();selected=Array.Empty<int>();}
                double Number(string v)=>double.Parse(v,CultureInfo.InvariantCulture);
                double now=Number(r[1]),x=Number(r[5]),z=Number(r[6]);int mask=int.Parse(r[2]);
                // Standard source button indices are mapped to the native bounded gesture bits.
                int nativeMask=(mask&7)|((mask&(1<<8))!=0?8:0)|((mask&(1<<9))!=0?16:0);
                var units=r[7].Split(';').Select(value=>{var u=value.Split(',');double ux=Number(u[3]),uz=Number(u[4]);return new OfflinePadSelectable(int.Parse(u[0]),u[1]=="tank"?0:1,u[2]=="1",ux,uz,.5,OfflinePadSelection.InViewport(camera,new Vector3((float)ux,0,(float)uz)));}).ToArray();
                var intents=gesture.Step(now,nativeMask,r[3]=="1",r[4]=="1",profile);
                foreach(var intent in intents){
                    if(intent=="select")selected=selection.Tap(now,OfflinePadSelection.Hit(units,x,z),units,profile.doubleTapMs);
                    if(intent=="selectCircle"||intent=="selectMapCircle"||intent=="selectScreen")selected=OfflinePadSelection.Area(units,x,z,intent=="selectScreen"?double.PositiveInfinity:(gesture.LastHeldMs-profile.holdMs)/1000*profile.selectionGrowth,intent=="selectMapCircle");
                }
                CollectionAssert.AreEqual(r[8].Length==0?Array.Empty<int>():r[8].Split(',').Select(int.Parse).ToArray(),selected,line);
                Assert.AreEqual("0",r[9],"Source selection must not dispatch authority commands.");samples++;
            }Assert.AreEqual(68,samples);}finally{UnityEngine.Object.DestroyImmediate(obj);}
        }
        [Test] public void SourceCameraProjectionZoomClampResetAndBothSeatRaysMatch()
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());const string path="unity/Tests/Fixtures/native-two-seat-gamepad-parity/source-camera.tsv";
            while(root!=null&&!File.Exists(Path.Combine(root.FullName,path)))root=root.Parent;Assert.NotNull(root);
            var profile=JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text);profile.Validate();
            var obj=new GameObject("source-perspective-camera");var camera=obj.AddComponent<Camera>();camera.aspect=1;var state=new OfflinePadCamera(2,3,profile);int samples=0;string previousAction=null,previousZoom=null;
            try{foreach(var line in File.ReadAllLines(Path.Combine(root.FullName,path)).Skip(1)){
                var r=line.Split('|');double Number(string v)=>double.Parse(v,CultureInfo.InvariantCulture);
                if(r[0]!=previousAction||r[4]!=previousZoom){if(r[0]=="zoom")state.AdjustZoom(Number(r[1]),profile);if(r[0]=="reset")state.Reset();previousAction=r[0];previousZoom=r[4];}
                state.Apply(camera);Assert.AreEqual(Number(r[4]),state.Zoom,1e-8,line);var point=new Vector3((float)Number(r[5]),0,(float)Number(r[6]));var projected=OfflinePadCamera.ProjectViewport(camera,point);
                // Single-precision Unity matrices vs source double precision: fixed normalized tolerance.
                Assert.AreEqual(Number(r[7]),projected.x,2e-5,line);Assert.AreEqual(Number(r[8]),projected.y,2e-5,line);
                foreach(var rect in new[]{new Rect(0,0,.5f,1),new Rect(.5f,0,.5f,1)}){camera.rect=rect;camera.aspect=1;var screen=OfflinePadCamera.ProjectScreen(camera,point);var ray=OfflinePadCamera.ScreenRay(camera,new Vector2(screen.x,screen.y));Assert.True(new Plane(Vector3.up,Vector3.zero).Raycast(ray,out var distance));Assert.Less(Vector3.Distance(point,ray.GetPoint(distance)),.001f,line);}
                camera.rect=new Rect(0,0,1,1);camera.aspect=1;samples++;
            }Assert.AreEqual(20,samples);}finally{UnityEngine.Object.DestroyImmediate(obj);}
        }
        [Test] public void FullModesAndStableSectorGesturesMatchSourceReplay()
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());const string path="unity/Tests/Fixtures/native-two-seat-gamepad-parity/source-full-gestures.tsv";
            while(root!=null&&!File.Exists(Path.Combine(root.FullName,path)))root=root.Parent;Assert.NotNull(root);
            var profile=JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text);profile.Validate();var gesture=new OfflinePadGestures();int samples=0;
            foreach(var line in File.ReadAllLines(Path.Combine(root.FullName,path)).Skip(1)){
                var r=line.Split('|');if(r[6].Length>0)gesture.SetMode(r[6]);double Number(string v)=>double.Parse(v,CultureInfo.InvariantCulture);
                var sector=new OfflinePadSector(r[7],r[8]=="1",r[9].Length>0?r[9]:null);
                var intents=gesture.StepDetailed(Number(r[1]),int.Parse(r[2]),r[4]=="1",r[5]=="1",profile,r[3]=="1",sector);
                Assert.AreEqual(r[10],gesture.Mode,line);Assert.AreEqual(r[11]=="1",gesture.Blocked,line);Assert.AreEqual(r[12]=="1",gesture.Added,line);Assert.AreEqual(r[13]=="1",gesture.AttackPreview,line);Assert.AreEqual(Number(r[14]),gesture.SelectionHeldMs,1e-8,line);Assert.AreEqual(r[15]=="1",gesture.ScreenPreview,line);Assert.AreEqual(Number(r[16]),gesture.Progress,1e-8,line);
                string actual=string.Join(";",intents.Select(i=>i.Kind+","+(i.Id??"")+","+((i.Kind=="selectCircle"||i.Kind=="selectMapCircle")?i.HeldMs:0).ToString(CultureInfo.InvariantCulture)));Assert.AreEqual(r[17],actual,line);samples++;
            }Assert.AreEqual(1400,samples);
            foreach(var line in File.ReadAllLines(Path.Combine(root.FullName,"unity/Tests/Fixtures/native-two-seat-gamepad-parity/source-ring-sectors.tsv")).Skip(1)){var r=line.Split('|');double Number(string v)=>double.Parse(v,CultureInfo.InvariantCulture);var index=OfflinePadGestures.RingSector(Number(r[0]),Number(r[1]),int.Parse(r[2]),Number(r[3]));Assert.AreEqual(r[4],index.HasValue?index.Value.ToString():"",line);}
        }
        [Test] public void ExplicitRbRtLbBuildingRallyAndMapContractsMatchActualSource()
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());const string path="unity/Tests/Fixtures/native-two-seat-gamepad-parity/source-gesture-contracts.tsv";
            while(root!=null&&!File.Exists(Path.Combine(root.FullName,path)))root=root.Parent;Assert.NotNull(root);
            var profile=JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text);profile.Validate();var gesture=new OfflinePadGestures();int samples=0;string name=null;
            foreach(var line in File.ReadAllLines(Path.Combine(root.FullName,path)).Skip(1)){
                var r=line.Split('|');if(name!=r[0]){name=r[0];gesture=new OfflinePadGestures();}if(r[3].Length>0)gesture.SetMode(r[3]);double Number(string v)=>double.Parse(v,CultureInfo.InvariantCulture);
                var sector=r[4].Length==0?null:new OfflinePadSector(r[4],r[5]=="1",r[6].Length>0?r[6]:null);
                var intents=gesture.StepDetailed(Number(r[1]),int.Parse(r[2]),true,true,profile,true,sector);
                Assert.AreEqual(r[7],gesture.Mode,line);Assert.AreEqual(r[8]=="1",gesture.Blocked,line);Assert.AreEqual(r[9]=="1",gesture.Added,line);Assert.AreEqual(Number(r[10]),gesture.Progress,1e-8,line);
                string actual=string.Join(";",intents.Select(i=>i.Kind+","+(i.Id??"")+","+((i.Kind=="selectCircle"||i.Kind=="selectMapCircle")?i.HeldMs:0).ToString(CultureInfo.InvariantCulture)));Assert.AreEqual(r[11],actual,line);samples++;
            }Assert.AreEqual(92,samples);
        }
        [Test] public void OwnerGroupsNormalizeAndEnableExactlyLikeActualSource()
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());const string path="unity/Tests/Fixtures/native-two-seat-gamepad-parity/source-group-actions.tsv";
            while(root!=null&&!File.Exists(Path.Combine(root.FullName,path)))root=root.Parent;Assert.NotNull(root);var groups=new OfflinePadGroups();int samples=0;
            foreach(var line in File.ReadAllLines(Path.Combine(root.FullName,path)).Skip(1)){
                var r=line.Split('|');int slot=int.Parse(r[1]);int[] Ids(string s)=>s.Length==0?Array.Empty<int>():s.Split(',').Select(int.Parse).ToArray();
                var units=Enumerable.Range(1,8).Where(id=>id.ToString()!=r[3]).Select(id=>new OfflinePadSelectable(id,0,id!=8&&id.ToString()!=r[4],0,0,.5,true)).ToArray();var selected=Ids(r[2]);int[] result=Array.Empty<int>();
                if(r[0]=="assign"){groups.Assign(slot,selected,units,false);result=selected.Where(id=>units.Any(u=>u.Id==id&&u.Own)).ToArray();}
                if(r[0]=="add"){groups.Assign(slot,selected,units,true);result=groups.Recall(slot,units);}
                if(r[0]=="recall")result=groups.Recall(slot,units);
                CollectionAssert.AreEqual(Ids(r[5]),result,line);Assert.AreEqual(int.Parse(r[6]),groups.SlotCount(units),line);
                string Enabled(bool assign)=>string.Join(",",groups.Actions(units,selected,assign,groups.SlotCount(units)).Select(a=>a.Enabled?"1":"0"));Assert.AreEqual(r[7],Enabled(false),line);Assert.AreEqual(r[8],Enabled(true),line);samples++;
            }Assert.AreEqual(12,samples);
        }
        [Test] public void BoundedGroupFocusMatches200ActualSourceCases()
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());const string path="unity/Tests/Fixtures/native-two-seat-gamepad-parity/source-group-focus.tsv";
            while(root!=null&&!File.Exists(Path.Combine(root.FullName,path)))root=root.Parent;Assert.NotNull(root);int samples=0;
            foreach(var line in File.ReadAllLines(Path.Combine(root.FullName,path)).Skip(1)){
                var r=line.Split('|');double N(string v)=>double.Parse(v,CultureInfo.InvariantCulture);
                var units=r[3].Split(';').Select(s=>{var v=s.Split(',');return new OfflinePadSelectable(int.Parse(v[0]),0,v[1]=="1",N(v[2]),N(v[3]),.5,true);}).ToArray();var selected=r[4].Split(',').Select(int.Parse);
                var result=OfflinePadGroups.Focus(selected,units,N(r[1]),N(r[2]),12);Assert.True(result.HasValue,line);Assert.AreEqual(N(r[5]),result.Value.X,1e-8,line);Assert.AreEqual(N(r[6]),result.Value.Z,1e-8,line);samples++;
            }Assert.AreEqual(200,samples);
        }
        [Test] public void ViewportRejectsBehindAndBeyondDepthWhileIncludingScreenBoundary()
        {
            var obj=new GameObject("depth-boundary");var camera=obj.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=10;camera.aspect=1;camera.nearClipPlane=.1f;camera.farClipPlane=200;camera.transform.position=new Vector3(0,100,0);camera.transform.rotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);
            try{Assert.True(OfflinePadSelection.InViewport(camera,new Vector3(10,0,0)));Assert.False(OfflinePadSelection.InViewport(camera,new Vector3(11,0,0)));Assert.False(OfflinePadSelection.InViewport(camera,new Vector3(0,101,0)));Assert.False(OfflinePadSelection.InViewport(camera,new Vector3(0,-101,0)));}finally{UnityEngine.Object.DestroyImmediate(obj);}
        }
    }
}
