using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class DirectProjectilePresentationTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    GameObject host;PlayableBootstrap hud;PlayableWorld world;PlayableProfile profile;
    void Set(string key,object value)=>typeof(PlayableBootstrap).GetField(key,Flags).SetValue(hud,value);
    IDictionary Hits=>(IDictionary)typeof(PlayableBootstrap).GetField("directContacts",Flags).GetValue(hud);
    object Field(object value,string name)=>value.GetType().GetField(name,Flags).GetValue(value);
    void Render(long tick,PlayableSoundEvent[] sounds=null,PlayableProjectileSnapshot[] shots=null,bool paused=false,long generation=1)
    {
        Set("view",new PlayableSnapshot(profile.ProfileId,profile.Revision,generation,7,tick,tick,RuntimeStatus.Running,paused,PlayableMatchOutcome.Playing,0,null,
            Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),shots??Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sounds:sounds));
        typeof(PlayableBootstrap).GetMethod("RenderDirectContacts",Flags).Invoke(hud,null);
        typeof(PlayableBootstrap).GetMethod("RenderProjectiles",Flags).Invoke(hud,null);
    }
    PlayableSoundEvent Hit(long id,PlayableEntityKind weapon,int target,float x,long tick=30,double height=1)
        =>new PlayableSoundEvent(id,tick,PlayableSoundKind.Impact,new NavPoint(x,0),weapon,contact:new PlayableDirectContact((int)id,target,new BallisticPoint(x,height,-1.8),new BallisticPoint(x,height,0)));
    [SetUp] public void Setup()
    {
        profile=PlayableProfile.Default;host=new GameObject("Direct projectile fixture");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        Set("profile",profile);world=new PlayableWorld(host.transform,profile);Set("world",world);world.SetPresentationLayer(8);
        foreach(var item in new[]{(1,-1.4f),(2,1.4f)}){var actor=world.Tank(item.Item1,true,PlayableEntityKind.Tank);actor.Root.position=new Vector3(item.Item2,0,0);}
    }
    [TearDown] public void Cleanup(){UnityEngine.Object.DestroyImmediate(host);}
    [TestCase(PlayableBuildingKind.Headquarters,false,1d)][TestCase(PlayableBuildingKind.Factory,false,1d)][TestCase(PlayableBuildingKind.Mine,false,1d)][TestCase(PlayableBuildingKind.Outpost,false,1d)][TestCase(PlayableBuildingKind.ScientificCenter,false,1d)][TestCase(PlayableBuildingKind.Refinery,false,1d)][TestCase(PlayableBuildingKind.Refinery,true,1d)][TestCase(PlayableBuildingKind.Hangar,false,1d)]
    [TestCase(PlayableBuildingKind.Headquarters,false,.35)][TestCase(PlayableBuildingKind.Factory,false,0)][TestCase(PlayableBuildingKind.Hangar,false,.5)]
    public void SharedCombatContactLiesOnTheActualRenderedBuilding(PlayableBuildingKind kind,bool upgraded,double progress=1)
    {
        var at=new NavPoint(8,3);var actor=world.Building(20,kind.ToString(),false,upgraded);actor.Root.position=world.Point(at);world.FaceBuilding(actor);actor.Root.localScale=new Vector3(1,(float)BuildingCombatGeometry.ConstructionHeight(progress),1);
        var pose=BuildingCombatGeometry.Pose(profile,kind,upgraded,at,0,constructionProgress:progress);
        Assert.True(pose.Segment(new BallisticPoint(8,30,3),new BallisticPoint(8,-2,3),.06,out _,out var center));var point=pose.Closest(center);
        var hit=new PlayableSoundEvent(200,30,PlayableSoundKind.Impact,point.Ground,PlayableEntityKind.Hawk,true,contact:new PlayableDirectContact(200,20,new BallisticPoint(8,30,3),point));
        Render(30,new[]{hit});var root=(GameObject)Field(Hits[200L],"Root");
        Assert.Less(Vector3.Distance(root.transform.position,new Vector3((float)point.X,(float)point.Y,(float)point.Z)),.00001f,"Renderer must use authority contact without shifting it");
        Assert.True(OnMeshSurface(actor.Hull,root.transform.position),"Authority contact must lie on actual rendered triangles");
    }
    [Test] public void EachBulletHasItsOwnWeakerEffectAndTerminalSegmentIncludingLateFrame()
    {
        var events=new[]{Hit(100,PlayableEntityKind.Tank,1,-1.4f),Hit(101,PlayableEntityKind.Explorer,2,1.4f),Hit(102,PlayableEntityKind.Explorer,2,1.4f)};
        Render(31,events);Assert.AreEqual(3,Hits.Count);
        var tank=(GameObject)Field(Hits[100L],"Root");var explorer=(GameObject)Field(Hits[101L],"Root");
        Assert.Less(explorer.transform.GetChild(0).localScale.x,tank.transform.GetChild(0).localScale.x);
        foreach(var hit in Hits.Values)
        {
            var root=(GameObject)Field(hit,"Root");var trail=(GameObject)Field(hit,"Trail");Assert.NotNull(trail,"First rendered contact must bridge the gap even when its simulation tick was skipped.");
            Vector3 endpoint=trail.transform.position+trail.transform.right*trail.transform.localScale.x/2;
            Assert.Less(Vector3.Distance(endpoint,root.transform.position),.0001);
            // F1 replaces six primitive draws with exactly two shared particle batches.
            Assert.AreEqual(2,root.transform.childCount);Assert.AreEqual(2,root.GetComponentsInChildren<Renderer>().Length);
            Assert.IsEmpty(root.GetComponentsInChildren<Collider>());
            foreach(var renderer in root.GetComponentsInChildren<Renderer>())
            {Assert.AreEqual("Spacewars/DirectImpact",renderer.sharedMaterial.shader.name);Assert.IsTrue(renderer.sharedMaterial.shader.isSupported);}
            foreach(var part in root.GetComponentsInChildren<Transform>())Assert.AreEqual(8,part.gameObject.layer);
        }
        var point=tank.transform.position;
        var properties=new MaterialPropertyBlock();var hot=tank.transform.GetChild(0).GetComponent<Renderer>();hot.GetPropertyBlock(properties);float phase=properties.GetFloat("_Phase");
        Render(31,events,paused:true);Assert.AreEqual(point,tank.transform.position);hot.GetPropertyBlock(properties);Assert.AreEqual(phase,properties.GetFloat("_Phase"));
        var otherHot=explorer.transform.GetChild(0).GetComponent<Renderer>();Assert.AreSame(hot.sharedMaterial,otherHot.sharedMaterial,"Per-bullet particles share their material/mesh resources.");
        world.Actors[1].Root.position+=Vector3.right;Render(32,events);Assert.AreEqual(point+Vector3.right,tank.transform.position);
        Assert.IsFalse((GameObject)Field(Hits[100L],"Trail"));
        Render(40,events);Assert.Zero(Hits.Count);
    }
    [Test] public void ContactPreservesFlightHeightAndHiddenFactsRestartAndMissClearEffects()
    {
        var actor=world.Actors[1];Assert.IsTrue(actor.Hull.GetComponentsInChildren<MeshFilter>().All(m=>m.sharedMesh.isReadable),"Approved glTF model meshes must support presentation surface intersection.");
        Render(30,new[]{Hit(100,PlayableEntityKind.Tank,1,-1.4f)});var root=(GameObject)Field(Hits[100L],"Root");
        Assert.AreEqual(-1.4f,root.transform.position.x,.00001f);
        Assert.AreEqual(1,root.transform.position.y,.00001f,"Contact must preserve the incoming height instead of seeking lower armour");
        Render(31);Assert.Zero(Hits.Count,"Filtered/hidden contact must disappear on the same projection.");
        Render(30,new[]{Hit(100,PlayableEntityKind.Tank,1,-1.4f)});Assert.AreEqual(1,Hits.Count);
        Render(0,generation:2);Assert.Zero(Hits.Count);
    }
    [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)]
    public void OffAxisHullCannotBendTerminalFlight(PlayableEntityKind weapon)
    {
        // Authority collision volumes and visible armour need not coincide. A visual
        // miss must retain the physical endpoint rather than seek nearby geometry.
        var from=new Vector3(5,1,-4);var impact=new Vector3(5,1,0);
        var fact=new PlayableSoundEvent(300,30,PlayableSoundKind.Impact,new NavPoint(5,0),weapon,
            contact:new PlayableDirectContact(300,1,new BallisticPoint(from.x,from.y,from.z),new BallisticPoint(impact.x,impact.y,impact.z)));
        Render(30,new[]{fact});
        var root=(GameObject)Field(Hits[300L],"Root");var trail=(GameObject)Field(Hits[300L],"Trail");
        Assert.Less(Vector3.Distance(impact,root.transform.position),.00001f);
        Assert.Less(Vector3.Cross(trail.transform.right,(impact-from).normalized).magnitude,.00001f);
        Assert.Less(Vector3.Distance(trail.transform.position-trail.transform.right*trail.transform.localScale.x/2,from),.00001f);
        Assert.Less(Vector3.Distance(trail.transform.position+trail.transform.right*trail.transform.localScale.x/2,impact),.00001f);
    }
    [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)]
    public void OnAxisHullContactPreservesIncomingFlight(PlayableEntityKind weapon)
    {
        Render(30,new[]{Hit(301,weapon,1,-1.4f,height:.5)});
        var root=(GameObject)Field(Hits[301L],"Root");var trail=(GameObject)Field(Hits[301L],"Trail");
        Assert.True(OnMeshSurface(world.Actors[1].Hull,root.transform.position));
        Assert.AreEqual(-1.4f,root.transform.position.x,.00001f);
        Assert.AreEqual(.5f,root.transform.position.y,.00001f);
        Assert.Less(Vector3.Cross(trail.transform.right,Vector3.forward).magnitude,.00001f);
    }
    [Test] public void ZeroLengthUnguidedContactCannotJumpToHullCentre()
    {
        var point=new BallisticPoint(5,1,0);
        var fact=new PlayableSoundEvent(303,30,PlayableSoundKind.Impact,point.Ground,PlayableEntityKind.Tank,
            contact:new PlayableDirectContact(303,1,point,point));
        Render(30,new[]{fact});var root=(GameObject)Field(Hits[303L],"Root");
        Assert.Less(Vector3.Distance(new Vector3(5,1,0),root.transform.position),.00001f);
        Assert.IsNull(Field(Hits[303L],"Trail"));
    }
    [Test] public void ContactSearchKeepsExactExhaustiveResultsForDenseTransformedAndGrazingMeshes()
    {
        var dense=new GameObject("Dense contact reference");dense.transform.SetParent(host.transform,false);
        var mesh=new Mesh();const int side=24;var vertices=new Vector3[(side+1)*(side+1)];var triangles=new int[side*side*6];
        for(int z=0;z<=side;z++)for(int x=0;x<=side;x++)vertices[z*(side+1)+x]=new Vector3(x/(float)side*4-2,Mathf.Sin(x*.7f)*Mathf.Cos(z*.4f)*.2f,z/(float)side*4-2);
        int t=0;for(int z=0;z<side;z++)for(int x=0;x<side;x++){int a=z*(side+1)+x,b=a+1,c=a+side+1,d=c+1;foreach(int index in new[]{a,c,b,b,c,d})triangles[t++]=index;}
        mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateBounds();
        dense.AddComponent<MeshFilter>().sharedMesh=mesh;dense.AddComponent<MeshRenderer>();
        var type=typeof(PlayableWorld).Assembly.GetType("Spacewars.Presentation.DirectContactSurface",true);var surface=Activator.CreateInstance(type,true);
        var resolve=type.GetMethod("Resolve",Flags,null,new[]{typeof(MeshFilter[]),typeof(Vector3),typeof(Vector3),typeof(bool),typeof(bool),typeof(float)},null);
        var random=new System.Random(280102026);
        try{
            foreach(var scale in new[]{Vector3.one,new Vector3(-1.3f,.4f,2.2f),new Vector3(.03f,2,1)}){
                dense.transform.localScale=scale;dense.transform.position=new Vector3(4,1,-3);dense.transform.rotation=Quaternion.Euler(17,41,-12);
                var parts=dense.GetComponentsInChildren<MeshFilter>();
                for(int i=0;i<160;i++){
                    var from=dense.transform.TransformPoint(new Vector3((float)random.NextDouble()*8-4,i%3==0?0:3,(float)random.NextDouble()*8-4));
                    var fallback=i%4==0?from+Vector3.right*20:dense.transform.TransformPoint(vertices[i%vertices.Length]);
                    bool flight=i%2==0,boundsFallback=i%3==0;float maximum=i%5==0?.25f:float.PositiveInfinity;
                    var expected=ExhaustiveContact(parts,from,fallback,flight,boundsFallback,maximum);
                    var actual=(Vector3?)resolve.Invoke(surface,new object[]{parts,from,fallback,flight,boundsFallback,maximum});
                    Assert.AreEqual(expected,actual,"Exact contact/fallback changed for scale="+scale+" ray="+i);
                }
            }
            CollectionAssert.AreEqual(vertices,mesh.vertices,"Search must not alter authored geometry");CollectionAssert.AreEqual(triangles,mesh.triangles);
            var cache=(IDictionary)type.GetField("geometry",Flags).GetValue(surface);
            Assert.AreEqual(1,cache.Count,"Instances share one mesh index per contact surface");
            var data=cache[mesh];var candidates=new System.Collections.Generic.List<int>();
            data.GetType().GetMethod("Candidates",Flags).Invoke(data,new object[]{new Ray(new Vector3(0,3,0),Vector3.down),candidates});
            Assert.Less(candidates.Count,triangles.Length/12,"Dense surface queries must prune exact triangle tests, not merely rearrange exhaustive work");
        }
        finally{UnityEngine.Object.DestroyImmediate(dense);UnityEngine.Object.DestroyImmediate(mesh);}
    }
    // Independent exhaustive reference retains the pre-acceleration ray/triangle contract.
    static Vector3? ExhaustiveContact(MeshFilter[] parts,Vector3 from,Vector3 fallback,bool flight,bool boundsFallback,float maximum)
    {
        Bounds bounds=default;bool any=false;
        foreach(var part in parts)if(part.TryGetComponent<Renderer>(out var renderer)&&renderer.enabled){if(!any){bounds=renderer.bounds;any=true;}else bounds.Encapsulate(renderer.bounds);}
        if(!any)return fallback;var direction=((flight?fallback:bounds.center)-from).normalized;
        if(direction.sqrMagnitude<Mathf.Epsilon)return flight?fallback:bounds.center;
        float best=float.PositiveInfinity;Vector3 point=fallback;
        foreach(var part in parts){
            var mesh=part.sharedMesh;if(!mesh||!mesh.isReadable||!part.TryGetComponent<Renderer>(out var renderer)||!renderer.enabled)continue;
            var origin=part.transform.InverseTransformPoint(from);var ray=part.transform.InverseTransformVector(direction);
            if(!mesh.bounds.IntersectRay(new Ray(origin,ray)))continue;
            var vertices=mesh.vertices;var triangles=mesh.triangles;
            for(int i=0;i<triangles.Length;i+=3){
                var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];
                var e1=b-a;var e2=c-a;var cross=Vector3.Cross(ray,e2);float det=Vector3.Dot(e1,cross);if(Math.Abs(det)<1e-8f)continue;
                float inv=1/det;var offset=origin-a;float u=Vector3.Dot(offset,cross)*inv;if(u<0||u>1)continue;
                var q=Vector3.Cross(offset,e1);float v=Vector3.Dot(ray,q)*inv;if(v<0||u+v>1)continue;
                float distanceAlong=Vector3.Dot(e2,q)*inv;if(distanceAlong<0)continue;
                var hit=part.transform.TransformPoint(origin+ray*distanceAlong);float distance=Vector3.Distance(from,hit);
                if(distance<=maximum&&distance<best){best=distance;point=hit;}
            }
        }
        if(float.IsPositiveInfinity(best)){if(boundsFallback&&bounds.IntersectRay(new Ray(from,direction),out float near))return from+direction*near;return null;}
        return point;
    }
    static bool OnMeshSurface(Transform root,Vector3 point)
    {
        foreach(var part in root.GetComponentsInChildren<MeshFilter>())
        {
            var mesh=part.sharedMesh;if(!mesh||!mesh.isReadable)continue;
            var vertices=mesh.vertices;var triangles=mesh.triangles;
            for(int i=0;i<triangles.Length;i+=3)
            {
                Vector3 a=part.transform.TransformPoint(vertices[triangles[i]]),b=part.transform.TransformPoint(vertices[triangles[i+1]]),c=part.transform.TransformPoint(vertices[triangles[i+2]]);
                var normal=Vector3.Cross(b-a,c-a);float area=normal.magnitude;if(area<1e-9f)continue;
                if(Mathf.Abs(Vector3.Dot(normal/area,point-a))>.0001f)continue;
                float areaSum=(Vector3.Cross(a-point,b-point).magnitude+Vector3.Cross(b-point,c-point).magnitude+Vector3.Cross(c-point,a-point).magnitude)/area;
                if(Mathf.Abs(areaSum-1)<.0001f)return true;
            }
        }
        return false;
    }
    [TestCase(PlayableEntityKind.Tank)] [TestCase(PlayableEntityKind.Explorer)] [TestCase(PlayableEntityKind.Hawk)]
    public void HawkImpactIsOnActualTargetMeshAndItsTerminalSegmentEndsThere(PlayableEntityKind kind)
    {
        var actor=world.Tank(10,false,kind);actor.Root.position=new Vector3(5,kind==PlayableEntityKind.Hawk?3:0,0);
        var parts=actor.Hull.GetComponentsInChildren<Renderer>();var bounds=parts[0].bounds;foreach(var part in parts)bounds.Encapsulate(part.bounds);
        var from=bounds.center+new Vector3(-4,2,-2);var target=bounds.center;
        var hit=new PlayableSoundEvent(200,30,PlayableSoundKind.Impact,new NavPoint(target.x,target.z),PlayableEntityKind.Hawk,contact:new PlayableDirectContact(200,10,new BallisticPoint(from.x,from.y,from.z),new BallisticPoint(target.x,target.y,target.z)));
        Render(30,new[]{hit});var root=(GameObject)Field(Hits[200L],"Root");var trail=(GameObject)Field(Hits[200L],"Trail");
        Assert.True(OnMeshSurface(actor.Hull,root.transform.position),"Rocket flash must be on a real model triangle");
        Assert.AreEqual("Terminal Hawk missile",trail.name);
        Assert.False(trail.transform.Find("rocket-exhaust").gameObject.activeSelf,"Spent terminal missiles must not render an unconfigured exhaust cone");
        var nose=trail.transform.Find("rocket-nose");
        Vector3 Tip()=>nose.TransformPoint(Vector3.right*nose.GetComponent<MeshFilter>().sharedMesh.bounds.max.x);
        Assert.Less(Vector3.Distance(Tip(),root.transform.position),.0001f);
        Assert.Less(Vector3.Cross((root.transform.position-from).normalized,(target-from).normalized).magnitude,.0001f,"Terminal segment preserves incoming direction when it intersects the model");
        actor.Root.position+=Vector3.right*.2f;Render(30,new[]{hit});
        Assert.Less(Vector3.Distance(Tip(),root.transform.position),.0001f,"The terminal missile follows the interpolated target surface within the same simulation tick");
    }
    [Test] public void HawkObstacleImpactUsesVisibleWallSurfaceRatherThanExpandedCollisionBoundary()
    {
        world.Obstacle(new NavObstacle(8,-1,10,1));
        var hit=new PlayableSoundEvent(201,30,PlayableSoundKind.Impact,new NavPoint(7.93,0),PlayableEntityKind.Hawk,contact:new PlayableDirectContact(201,0,new BallisticPoint(7,1,0),new BallisticPoint(7.93,1,0)));
        Render(30,new[]{hit});var root=(GameObject)Field(Hits[201L],"Root");
        Assert.AreEqual(8,root.transform.position.x,.0001f);Assert.AreEqual(1,root.transform.position.y,.0001f);Assert.AreEqual(0,root.transform.position.z,.0001f);
    }
    [Test] public void HawkVisualScaleIsProfiledAndCannotChangeCombatDimensions()
    {
        var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="hawk.missileVisualScale");
        Assert.IsNotEmpty(field.Label);Assert.IsNotEmpty(field.Description);Assert.IsNotEmpty(field.Unit);Assert.Greater(field.Step,0);
        Assert.AreEqual(.8,profile.HawkMissileVisualScale);Assert.AreEqual(.8,PlayableProfile.Create(profile.CopyData()).HawkMissileVisualScale);
        var data=profile.CopyData();data.hawkMissileVisualScale=1;var previous=PlayableProfile.Create(data);
        Assert.AreEqual(previous.HawkMissileRadius,profile.HawkMissileRadius);Assert.AreEqual(previous.HawkMissileSpeed,profile.HawkMissileSpeed);Assert.AreEqual(previous.HawkDamage,profile.HawkDamage);Assert.AreEqual(previous.HawkMissileTurn,profile.HawkMissileTurn);
        var binding=typeof(Spacewars.Runtime.PlayableRuntime).Assembly.GetType("Spacewars.Runtime.WorldWire",true).GetMethod("Binding",BindingFlags.Static|BindingFlags.NonPublic);
        CollectionAssert.AreEqual((byte[])binding.Invoke(null,new object[]{previous,false}),(byte[])binding.Invoke(null,new object[]{profile,false}),"A visual scale must not alter gameplay/profile checkpoint binding bytes");
        foreach(double value in new[]{field.Minimum-.01,field.Maximum+.01,double.NaN,double.PositiveInfinity})
        {data=profile.CopyData();data.hawkMissileVisualScale=value;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));}
    }
    [UnityTest] public IEnumerator HawkLaunchAndContactFrames()
    {
        world.Remove(1);world.Remove(2);
        var vision=new PlayableVision(0,profile.ArenaHalfExtent,profile.ArenaHalfExtent,4,profile.FogEdgeFeather);
        vision.Refresh(new[]{new VisionSource(default,80)},Array.Empty<KnownBuilding>());world.Fog.Update(vision.Snapshot(),1);
        var lighting=new GameObject("Hawk evidence light");lighting.transform.SetParent(host.transform,false);var light=lighting.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;lighting.transform.rotation=Quaternion.Euler(45,-30,0);
        var cameraHost=new GameObject("Hawk evidence camera");cameraHost.transform.SetParent(host.transform,false);var camera=cameraHost.AddComponent<Camera>();camera.orthographic=true;camera.cullingMask=1<<8;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.08f,.1f);
        var texture=new RenderTexture(1600,1000,24);texture.Create();camera.targetTexture=texture;
        string folder=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.local/qa/hawk-contact/frames"));System.IO.Directory.CreateDirectory(folder);
        try
        {
            var hawk=world.Tank(3,true,PlayableEntityKind.Hawk);
            var pose=new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Hawk,new NavPoint(-4,0),60,false,0,0,0,altitude:3,pitch:8,roll:-12);
            Render(30);world.UpdateAircraft(hawk,pose,(PlayableSnapshot)typeof(PlayableBootstrap).GetField("view",Flags).GetValue(hud));
            var shots=Enumerable.Range(0,2).Select(side=>{var start=HawkWeaponGeometry.Muzzle(pose.Position,pose.Altitude,pose.HullHeading,pose.Pitch,pose.Roll,profile.HawkModelScale,side);return new PlayableProjectileSnapshot(301+side,3,0,start.Ground,PlayableEntityKind.Hawk,height:start.Y);}).ToArray();
            Render(30,shots:shots);Focus(new Vector3(-4,3.5f,0),2.4f);
            // Reconstruct the former scale at the identical camera/body pose for visual review.
            var shells=(IDictionary)typeof(PlayableBootstrap).GetField("shells",Flags).GetValue(hud);
            foreach(GameObject shell in shells.Values)shell.transform.localScale=Vector3.one;
            yield return null;Capture("00-before-missile-size");
            Render(30,shots:shots);
            foreach(GameObject shell in shells.Values)Assert.AreEqual(Vector3.one*(float)profile.HawkMissileVisualScale,shell.transform.localScale);
            yield return null;Capture("01-both-weapon-muzzles");
            var enemy=world.Tank(10,false,PlayableEntityKind.Tank);enemy.Root.position=new Vector3(2,0,0);
            var target=new Vector3(2,.45f,0);var from=new Vector3(-1,2,0);
            var contact=new PlayableSoundEvent(310,30,PlayableSoundKind.Impact,new NavPoint(2,0),PlayableEntityKind.Hawk,contact:new PlayableDirectContact(310,10,new BallisticPoint(from.x,from.y,from.z),new BallisticPoint(target.x,target.y,target.z)));
            Render(30,new[]{contact});Assert.True(OnMeshSurface(enemy.Hull,((GameObject)Field(Hits[310L],"Root")).transform.position));Focus(new Vector3(1,.6f,0),2.1f);yield return null;Capture("02-enemy-model-contact");
            world.Obstacle(new NavObstacle(8,-1,10,1));
            var obstacle=new PlayableSoundEvent(311,31,PlayableSoundKind.Impact,new NavPoint(7.93,0),PlayableEntityKind.Hawk,contact:new PlayableDirectContact(311,0,new BallisticPoint(7,1,0),new BallisticPoint(7.93,1,0)));
            Render(31,new[]{obstacle});Assert.AreEqual(8,((GameObject)Field(Hits[311L],"Root")).transform.position.x,.0001f);Focus(new Vector3(8.3f,1,0),2.2f);yield return null;Capture("03-obstacle-contact");
        }
        finally{camera.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
        void Focus(Vector3 target,float size){camera.orthographicSize=size;camera.transform.position=target+new Vector3(-4,3,-6);camera.transform.LookAt(target);}
        void Capture(string name)
        {
            camera.Render();var previous=RenderTexture.active;var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            try{RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,name+".png"),pixels.EncodeToPNG());}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
        }
    }
    [UnityTest] public IEnumerator NativeContactFrames()
    {
        var vision=new PlayableVision(0,profile.ArenaHalfExtent,profile.ArenaHalfExtent,4,profile.FogEdgeFeather);
        vision.Refresh(new[]{new VisionSource(default,80)},Array.Empty<KnownBuilding>());world.Fog.Update(vision.Snapshot(),1);
        world.Part("Contact evidence ground",host.transform,new Vector3(0,-.06f,0),new Vector3(14,.1f,14),new Color(.17f,.20f,.22f));
        var lightHost=new GameObject("Contact light");lightHost.transform.SetParent(host.transform,false);var light=lightHost.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-30,0);
        var cameraHost=new GameObject("Contact camera");cameraHost.transform.SetParent(host.transform,false);var camera=cameraHost.AddComponent<Camera>();
        camera.orthographic=true;camera.orthographicSize=2.3f;camera.transform.position=new Vector3(4,6,-8);camera.transform.LookAt(new Vector3(0,.2f,0));camera.cullingMask=1<<8;camera.backgroundColor=new Color(.06f,.08f,.1f);camera.clearFlags=CameraClearFlags.SolidColor;
        var texture=new RenderTexture(1400,800,24);texture.Create();camera.targetTexture=texture;
        var folder=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.local/qa/direct-contact/frames"));System.IO.Directory.CreateDirectory(folder);
        var events=new[]{Hit(100,PlayableEntityKind.Tank,1,-1.4f,height:.5),Hit(101,PlayableEntityKind.Explorer,2,1.4f,height:.5)};
        try
        {
            // Reconstruct the previous renderer path at the identical camera: shell gone,
            // damage/impact sound published but no direct visual contact fact consumed.
            Render(30);yield return null;Capture("legacy-removal-reconstruction");
            Render(30,events);yield return null;Capture("terminal-contact");
            Render(31,events);yield return null;Capture("early-sparks");
            Render(32,events);yield return null;Capture("warm-sparks");
            Render(34,events);yield return null;Capture("cooling-embers");
            Render(30,new[]{new PlayableSoundEvent(302,30,PlayableSoundKind.Impact,new NavPoint(.1,0),PlayableEntityKind.Tank,
                contact:new PlayableDirectContact(302,1,new BallisticPoint(.1,1,-1.8),new BallisticPoint(.1,1,0)))});
            yield return null;Capture("straight-off-axis-contact");
            Render(32,events);
            camera.orthographicSize=(float)profile.CameraOrthoSize;yield return null;Capture("ordinary-camera-scale");
        }
        finally{camera.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
        void Capture(string name)
        {
            camera.Render();var previous=RenderTexture.active;var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            try{RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,name+".png"),pixels.EncodeToPNG());}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
        }
    }
}
