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
    [TestCase(PlayableBuildingKind.Headquarters,false,1d)][TestCase(PlayableBuildingKind.Factory,false,1d)][TestCase(PlayableBuildingKind.Mine,false,1d)][TestCase(PlayableBuildingKind.Outpost,false,1d)][TestCase(PlayableBuildingKind.ScientificCenter,false,1d)][TestCase(PlayableBuildingKind.ScientificCenter,true,1d)][TestCase(PlayableBuildingKind.ScientificCenter,true,.35)][TestCase(PlayableBuildingKind.Refinery,false,1d)][TestCase(PlayableBuildingKind.Refinery,true,1d)][TestCase(PlayableBuildingKind.Hangar,false,1d)]
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
                float planeDistance=Vector3.Dot(normal/area,point-a);
                if(Mathf.Abs(planeDistance)>.0001f)continue;
                // The area test is planar. Do not count an already accepted normal offset
                // again as relative area error on tiny triangles near a shared vertex.
                var projected=point-normal/area*planeDistance;
                float areaSum=(Vector3.Cross(a-projected,b-projected).magnitude+Vector3.Cross(b-projected,c-projected).magnitude+Vector3.Cross(c-projected,a-projected).magnitude)/area;
                if(Mathf.Abs(areaSum-1)<.0001f)return true;
            }
        }
        return false;
    }
    [Test] public void SurfacePredicateProjectsTinyTrianglesButStillRejectsOutsidePlaneAndEdges()
    {
        // Actual compressed-scanner triangle and float-rounded authority contact from the regression.
        var a=new Vector3(7.98306751f,1.0440619f,2.97454f);
        var b=new Vector3(7.983086f,1.04384708f,2.97694826f);
        var c=new Vector3(7.98493958f,1.0436641f,2.976605f);
        var contact=new Vector3(7.98493958f,1.04366422f,2.976605f);
        var model=new GameObject("Tiny contact triangle");model.transform.SetParent(host.transform,false);
        var mesh=new Mesh{vertices=new[]{a,b,c},triangles=new[]{0,1,2}};model.AddComponent<MeshFilter>().sharedMesh=mesh;
        try
        {
            Assert.True(OnMeshSurface(model.transform,contact),"An accepted plane offset must not be counted again as relative triangle-area error");
            var normal=Vector3.Cross(b-a,c-a);normal/=normal.magnitude;
            Assert.False(OnMeshSurface(model.transform,c+normal*.0002f),"The existing 0.0001 m plane tolerance must remain enforced");
            Assert.False(OnMeshSurface(model.transform,c+(c-a)),"A coplanar point outside the triangle must still be rejected");
        }
        finally{UnityEngine.Object.DestroyImmediate(mesh);}
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
        var before=(byte[])binding.Invoke(null,new object[]{previous,false});var after=(byte[])binding.Invoke(null,new object[]{profile,false});
        // EPB1 intentionally identifies the complete effective profile, including presentation.
        // Retain exact gameplay-prefix equality while checking the current full-profile contract.
        var effective=binding.DeclaringType.GetMethod("WriteEffectiveProfileBindingV1",BindingFlags.Static|BindingFlags.NonPublic);
        byte[] Extension(PlayableProfile p)
        {
            using var bytes=new System.IO.MemoryStream();using var writer=new System.IO.BinaryWriter(bytes);
            effective.Invoke(null,new object[]{writer,p});writer.Flush();return bytes.ToArray();
        }
        var priorExtension=Extension(previous);var currentExtension=Extension(profile);
        // EPB1 is followed by other profile bindings, so it is not a wire suffix.
        // Locate its complete exact payload and preserve both surrounding byte ranges.
        int Offset(byte[] wire,byte[] extension)=>Enumerable.Range(0,wire.Length-extension.Length+1).Single(i=>wire.Skip(i).Take(extension.Length).SequenceEqual(extension));
        int beforeOffset=Offset(before,priorExtension),afterOffset=Offset(after,currentExtension);Assert.AreEqual(beforeOffset,afterOffset);
        CollectionAssert.AreEqual(before.Take(beforeOffset).ToArray(),after.Take(afterOffset).ToArray(),"A visual scale must not alter the gameplay binding prefix");
        CollectionAssert.AreEqual(before.Skip(beforeOffset+priorExtension.Length).ToArray(),after.Skip(afterOffset+currentExtension.Length).ToArray(),"A visual scale must not alter following profile bindings");
        CollectionAssert.AreNotEqual(before,after,"EPB1 must still distinguish different effective visual profiles");
        foreach(double value in new[]{field.Minimum-.01,field.Maximum+.01,double.NaN,double.PositiveInfinity})
        {data=profile.CopyData();data.hawkMissileVisualScale=value;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));}
    }
    [Test] public void HawkExhaustIsIndependentOfShkvalTuningAndKeepsItsOwnValidatedLabControl()
    {
        var art=HawkVisualProfile.Load();
        var control=art.Parameters().Single(p=>p.Path=="visual.hawk.exhaustOpacity");
        Assert.Greater(control.Step,0);Assert.AreEqual("opacity",control.Unit);
        foreach(float invalid in new[]{control.Minimum-control.Step,control.Maximum+control.Step,float.NaN,float.PositiveInfinity})
        {control.Write(invalid);Assert.Throws<ArgumentOutOfRangeException>(()=>art.Validate());}
        foreach(double opacity in new[]{.1,.9})
        {
            var data=profile.CopyData();data.shkvalExhaustOpacity=opacity;profile=PlayableProfile.Create(data);Set("profile",profile);
            Render(30,shots:new[]{new PlayableProjectileSnapshot(301,3,0,default,PlayableEntityKind.Hawk,height:3),new PlayableProjectileSnapshot(302,4,0,new NavPoint(4,0),PlayableEntityKind.Shkval,height:3)});
            var shells=(IDictionary)typeof(PlayableBootstrap).GetField("shells",Flags).GetValue(hud);
            var shell=(GameObject)shells[301];var exhaust=shell.transform.Find("rocket-exhaust");
            foreach(var renderer in exhaust.GetComponentsInChildren<Renderer>())
            {
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha,renderer.sharedMaterial.GetFloat("_DstBlend"),"Hawk flames must not add white energy over the missile body.");
                Assert.AreEqual(HawkVisualProfile.Load().exhaustOpacity,block.GetColor("_BaseColor").a,.0001f,
                    "Changing artillery flame tuning must not wash out a Hawk missile");
            }
            var artillery=((GameObject)shells[302]).transform.Find("rocket-exhaust");
            foreach(var renderer in artillery.GetComponentsInChildren<Renderer>())Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.One,renderer.sharedMaterial.GetFloat("_DstBlend"),"Shkval keeps its existing additive flame.");
            Assert.AreEqual(Vector3.one*(float)profile.HawkMissileVisualScale,shell.transform.localScale);
            var output=Environment.GetEnvironmentVariable("SPACEWARS_HAWK_PROJECTILE_EVIDENCE");
            if(!string.IsNullOrEmpty(output))
            {
                System.IO.Directory.CreateDirectory(output);
                var renderers=shell.GetComponentsInChildren<Renderer>();var meshes=shell.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).Distinct().ToArray();
                var cost=new HawkRocketCost{renderers=renderers.Length,materialSlots=renderers.Sum(r=>r.sharedMaterials.Length),materials=renderers.SelectMany(r=>r.sharedMaterials).Distinct().Count(),meshes=meshes.Length,vertices=meshes.Sum(m=>m.vertexCount),triangles=meshes.Sum(m=>m.triangles.Length/3),meshBytes=meshes.Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m))};
                System.IO.File.WriteAllText(System.IO.Path.Combine(output,"rocket-cost.json"),JsonUtility.ToJson(cost,true));
            }
        }
    }
    [Serializable] private sealed class HawkRocketCost { public int renderers,materialSlots,materials,meshes,vertices,triangles;public long meshBytes; }
    [Test] public void HawkFinsHaveNonzeroLightingNormalsAndUseDoubleSidedMaterial()
    {
        Render(30,shots:new[]{new PlayableProjectileSnapshot(301,3,0,default,PlayableEntityKind.Hawk,height:3)});
        var shells=(IDictionary)typeof(PlayableBootstrap).GetField("shells",Flags).GetValue(hud);
        var fins=((GameObject)shells[301]).GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="hawk-fin").ToArray();Assert.AreEqual(4,fins.Length);
        foreach(var fin in fins){foreach(var normal in fin.sharedMesh.normals)Assert.AreEqual(1,normal.magnitude,.001f,"Opposing triangles must not cancel lighting normals.");Assert.AreEqual((float)UnityEngine.Rendering.CullMode.Off,fin.GetComponent<Renderer>().sharedMaterial.GetFloat("_Cull"));}
    }
    [UnityTest] public IEnumerator HawkBloomPipelinePositiveControl()
    {
        world.Remove(1);world.Remove(2);world.Fog.ShowPublic();
        var camera=new GameObject("Bloom positive control camera").AddComponent<Camera>();camera.transform.SetParent(host.transform,false);
        camera.orthographic=true;camera.orthographicSize=6;camera.cullingMask=1<<8;camera.backgroundColor=new Color(.06f,.08f,.1f);
        camera.transform.position=new Vector3(-10,9,5);camera.transform.LookAt(new Vector3(-6,3,15));
        var glow=camera.gameObject.AddComponent<ActorGlowCamera>();glow.Configure(ActorMaterialProfile.Load());Assert.True(glow.Profile.TryGet<UnityEngine.Rendering.Universal.Bloom>(out var bloom));
        var probe=GameObject.CreatePrimitive(PrimitiveType.Sphere);probe.transform.SetParent(host.transform,false);probe.transform.position=new Vector3(-6,3,15);probe.transform.localScale=Vector3.one*.3f;probe.layer=8;probe.GetComponent<Collider>().enabled=false;
        // Deliberately over-threshold HDR input validates the camera's bloom path.
        var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.SetColor("_BaseColor",new Color(8,8,8));probe.GetComponent<Renderer>().sharedMaterial=material;
        var image=new RenderTexture(640,400,24,RenderTextureFormat.ARGBHalf);image.Create();camera.targetTexture=image;var readback=new RenderTexture(640,400,0,RenderTextureFormat.ARGB32);readback.Create();var pixels=new Texture2D(640,400,TextureFormat.RGB24,false);
        string folder=Environment.GetEnvironmentVariable("SPACEWARS_HAWK_PROJECTILE_EVIDENCE");long on=0,off=0;Color32[] onPixels=null;int changedPixels=0;
        try
        {
            yield return null;yield return null;
            foreach(bool active in new[]{true,false})
            {
                bloom.active=active;var data=camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();var manager=UnityEngine.Rendering.VolumeManager.instance;manager.Update(data.volumeStack??manager.stack,camera.transform,data.volumeLayerMask);
                camera.Render();Graphics.Blit(image,readback);var previous=RenderTexture.active;RenderTexture.active=readback;pixels.ReadPixels(new Rect(0,0,640,400),0,0);pixels.Apply();RenderTexture.active=previous;
                var samples=pixels.GetPixels32();long signal=samples.Sum(c=>(long)c.r+c.g+c.b);if(active){on=signal;onPixels=samples;}else{off=signal;changedPixels=samples.Where((c,i)=>Math.Abs(c.r-onPixels[i].r)+Math.Abs(c.g-onPixels[i].g)+Math.Abs(c.b-onPixels[i].b)>=6).Count();}
                if(!string.IsNullOrEmpty(folder)){System.IO.Directory.CreateDirectory(folder);System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,"bloom-positive-"+(active?"on":"off")+".png"),pixels.EncodeToPNG());}
            }
            if(!string.IsNullOrEmpty(folder))System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"bloom-positive.txt"),"intensity="+bloom.intensity.value+" threshold="+bloom.threshold.value+" on="+on+" off="+off+" visibleChangedPixels="+changedPixels+" target="+image.graphicsFormat);
            Assert.Greater(changedPixels,32,"The positive control must produce a visible halo, not only subpixel rounding differences.");
            Assert.Greater(on,off,"An HDR positive control must visibly change pixels when bloom is enabled; a no-op pipeline cannot justify a negative result.");
        }
        finally{camera.targetTexture=null;image.Release();readback.Release();UnityEngine.Object.DestroyImmediate(readback);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(material);}
    }
    [UnityTest] public IEnumerator HawkMovingFlightEffectIsolation()
    {
        var folder=Environment.GetEnvironmentVariable("SPACEWARS_HAWK_PROJECTILE_EVIDENCE");
        bool capture=!string.IsNullOrEmpty(folder);
        var visual=HawkVisualProfile.Load();ColorUtility.TryParseHtmlString("#b8c1c8",out var legacyBody);ColorUtility.TryParseHtmlString("#"+ColorUtility.ToHtmlStringRGB(legacyBody*visual.missileBodyBrightness),out var currentBody);
        var variants=new[]{"current","no-exhaust","no-bloom","neither","before","before-no-bloom"};
        if(capture)foreach(string variant in variants)System.IO.Directory.CreateDirectory(System.IO.Path.Combine(folder,variant));
        world.Remove(1);world.Remove(2);world.Fog.ShowPublic();
        var domainType=typeof(Spacewars.Runtime.PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,new NavPoint(-16,-14),Array.Empty<TerritorySlot>()),new TerritorySite(2,PlayableBuildingKind.Headquarters,new NavPoint(16,14),Array.Empty<TerritorySlot>())};
        var starts=sites.Select((site,i)=>new OfflineStart("start-"+i,site.Id,site.Position,new NavPoint(site.Position.X+5,site.Position.Z),pin:i+1)).ToArray();
        var roster=new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)};
        var configuration=new OfflineMatchConfiguration(profile,"hawk-effect-flat-v1","hawk-effect-flat","diagnostic flat surface",7,roster,starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,40},{40,0}});
        var domain=Activator.CreateInstance(domainType,Flags,null,new object[]{profile,1L,configuration},null);
        object Call(string name,params object[] args)=>domainType.GetMethod(name,Flags).Invoke(domain,args);
        var units=(IDictionary)domainType.GetField("units",Flags).GetValue(domain);
        foreach(int id in units.Keys.Cast<int>().ToArray()){var navigation=domainType.GetProperty("Navigation",Flags).GetValue(domain);navigation.GetType().GetMethod("Remove",Flags|BindingFlags.Public).Invoke(navigation,new object[]{id});units.Remove(id);}
        int shooter=(int)Call("SpawnUnit",new NavPoint(-10,15),PlayableOwner.Player,PlayableEntityKind.Hawk);
        int target=(int)Call("SpawnUnit",new NavPoint(-2,15),PlayableOwner.Enemy,PlayableEntityKind.Tank);
        units[target].GetType().GetField("Reload").SetValue(units[target],1000d);
        PlayableSnapshot Snapshot()=>(PlayableSnapshot)Call("Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        Call("Step",1d/30); // Publish normal vision inputs before issuing the player's attack.
        int initialTargetHealth=Snapshot().Entities.Single(e=>e.Id==target).Health;
        Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,1,"player-1",PlayableCommandKind.Attack,new[]{shooter},targetId:target),null));
        var camera=new GameObject("Moving Hawk effect camera").AddComponent<Camera>();camera.transform.SetParent(host.transform,false);
        camera.orthographic=true;camera.orthographicSize=6;camera.cullingMask=1<<8;camera.backgroundColor=new Color(.06f,.08f,.1f);
        camera.transform.position=new Vector3(-10,9,5);camera.transform.LookAt(new Vector3(-6,3,15));
        var glow=camera.gameObject.AddComponent<ActorGlowCamera>();glow.Configure(ActorMaterialProfile.Load());
        Assert.True(glow.Profile.TryGet<UnityEngine.Rendering.Universal.Bloom>(out var bloom));
        var light=new GameObject("Moving Hawk light").AddComponent<Light>();light.transform.SetParent(host.transform,false);light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-30,0);
        var image=new RenderTexture(640,400,24,RenderTextureFormat.ARGBHalf);image.Create();camera.targetTexture=image;var readback=new RenderTexture(640,400,0,RenderTextureFormat.ARGB32);readback.Create();var pixels=new Texture2D(640,400,TextureFormat.RGB24,false);
        var telemetry=new System.Collections.Generic.List<string>{"frame,tick,projectiles,shots,impacts,targetHealth"};bool flight=false,contact=false;int maxWhite=0,maxBeforeWhite=0;
        var validFins=new System.Collections.Generic.Dictionary<MeshFilter,Mesh>();var brokenFins=new System.Collections.Generic.Dictionary<MeshFilter,Mesh>();
        try
        {
            int warmup=0;while(warmup++<240&&!Snapshot().Projectiles.Any(p=>p.Kind==PlayableEntityKind.Hawk))Call("Step",1d/30);
            Assert.True(Snapshot().Projectiles.Any(p=>p.Kind==PlayableEntityKind.Hawk),"Actual domain must launch within the bounded preparation horizon.");
            for(int frame=0;frame<90;frame++)
            {
                var snapshot=Snapshot();Set("view",snapshot);world.AdvanceAircraftAnimation(snapshot,snapshot.Tick/30d);
                foreach(var entity in snapshot.Entities.Where(e=>e.Id==shooter||e.Id==target))
                {
                    if(!world.Actors.TryGetValue(entity.Id,out var actor))actor=world.Tank(entity.Id,entity.Owner==PlayableOwner.Player,entity.Kind);
                    if(entity.Kind==PlayableEntityKind.Hawk)world.UpdateAircraft(actor,entity,snapshot);
                    else{actor.Root.position=world.Point(entity.Position);actor.Hull.localRotation=Quaternion.Euler(0,90-(float)entity.HullHeading*Mathf.Rad2Deg,0);}
                    actor.Health.parent.gameObject.SetActive(false);
                }
                typeof(PlayableBootstrap).GetMethod("RenderDirectContacts",Flags).Invoke(hud,null);typeof(PlayableBootstrap).GetMethod("RenderProjectiles",Flags).Invoke(hud,null);
                var shells=(IDictionary)typeof(PlayableBootstrap).GetField("shells",Flags).GetValue(hud);
                flight|=snapshot.Projectiles.Any(p=>p.Kind==PlayableEntityKind.Hawk);contact|=snapshot.Sounds.Any(e=>e.Kind==PlayableSoundKind.Impact&&e.Weapon==PlayableEntityKind.Hawk);
                telemetry.Add(frame+","+snapshot.Tick+","+snapshot.Projectiles.Count+","+snapshot.Sounds.Count(e=>e.Kind==PlayableSoundKind.Shot)+","+snapshot.Sounds.Count(e=>e.Kind==PlayableSoundKind.Impact)+","+snapshot.Entities.Single(e=>e.Id==target).Health);
                yield return null;
                var visibleRenderers=host.GetComponentsInChildren<Renderer>(true).ToDictionary(r=>r,r=>r.enabled);
                var flightRockets=shells.Values.Cast<GameObject>().ToArray();
                var rockets=flightRockets.Concat(Hits.Values.Cast<object>().Select(hit=>Field(hit,"Trail") as GameObject).Where(g=>g&&g.name=="Terminal Hawk missile")).Distinct().ToArray();
                foreach(string variant in capture?variants:new[]{"current","no-bloom","before"})
                {
                    foreach(var entry in visibleRenderers)if(entry.Key)entry.Key.enabled=entry.Value;
                    bool legacy=variant.StartsWith("legacy",StringComparison.Ordinal),neither=variant.EndsWith("neither",StringComparison.Ordinal);
                    bloom.active=!variant.Contains("no-bloom")&&!neither;
                    foreach(GameObject shell in rockets)
                    {
                        foreach(var filter in shell.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name=="hawk-fin"))
                        {
                            if(!validFins.TryGetValue(filter,out var valid))
                            {
                                valid=filter.sharedMesh;validFins.Add(filter,valid);
                                var broken=UnityEngine.Object.Instantiate(valid);broken.triangles=new[]{0,1,2,2,1,0};broken.RecalculateNormals();brokenFins.Add(filter,broken);
                            }
                            filter.sharedMesh=variant.StartsWith("before",StringComparison.Ordinal)?brokenFins[filter]:valid;
                        }
                        var exhaust=shell.transform.Find("rocket-exhaust");exhaust.gameObject.SetActive(flightRockets.Contains(shell)&&!variant.Contains("no-exhaust")&&!neither);
                        foreach(var renderer in exhaust.GetComponentsInChildren<Renderer>(true))
                        {
                            renderer.sharedMaterial.SetFloat("_DstBlend",(float)(legacy?UnityEngine.Rendering.BlendMode.One:UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
                            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);var color=renderer.sharedMaterial.GetColor("_BaseColor");color.a=legacy?.3f:visual.exhaustOpacity;block.SetColor("_BaseColor",color);renderer.SetPropertyBlock(block);
                        }
                        var body=shell.transform.Find("rocket-body").GetComponent<Renderer>();
                        body.sharedMaterial.SetColor("_BaseColor",legacy?legacyBody:currentBody);body.sharedMaterial.SetFloat("_Smoothness",legacy?.6f:1-visual.missileRoughness);
                    }
                    var data=camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();var manager=UnityEngine.Rendering.VolumeManager.instance;
                    manager.Update(data.volumeStack??manager.stack,camera.transform,data.volumeLayerMask);
                    camera.Render();Assert.AreEqual(bloom.active?bloom.intensity.value:0,(data.volumeStack??manager.stack).GetComponent<UnityEngine.Rendering.Universal.Bloom>().intensity.value,.0001f,"Each ablation must reach the actual camera volume stack.");
                    Graphics.Blit(image,readback);var previous=RenderTexture.active;RenderTexture.active=readback;pixels.ReadPixels(new Rect(0,0,640,400),0,0);pixels.Apply();RenderTexture.active=previous;
                    // Resolution-specific regression guard: the previous white ball covered thousands
                    // of pixels; a small missile/highlight must never become a 128-pixel white patch.
                    int white=pixels.GetPixels32().Count(c=>c.r>=245&&c.g>=245&&c.b>=245);
                    if(variant=="current")maxWhite=Math.Max(maxWhite,white);
                    if(variant=="before")maxBeforeWhite=Math.Max(maxBeforeWhite,white);
                    if(capture)System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,variant,"frame-"+frame.ToString("D3")+".png"),pixels.EncodeToPNG());
                }
                // Restore cached shared materials before the next simulation publication,
                // including terminal missile instances created by the impact renderer.
                bloom.active=true;
                foreach(var entry in validFins)if(entry.Key)entry.Key.sharedMesh=entry.Value;
                foreach(var entry in visibleRenderers)if(entry.Key)entry.Key.enabled=entry.Value;
                foreach(GameObject shell in rockets)
                {
                    var exhaust=shell.transform.Find("rocket-exhaust");exhaust.gameObject.SetActive(flightRockets.Contains(shell));
                    foreach(var renderer in exhaust.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterial.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    var body=shell.transform.Find("rocket-body").GetComponent<Renderer>();body.sharedMaterial.SetColor("_BaseColor",currentBody);body.sharedMaterial.SetFloat("_Smoothness",1-visual.missileRoughness);
                }
                Call("Step",1d/30);
            }
            Assert.True(flight);Assert.True(contact,"The replay must include actual impact facts, not only floating projectile geometry.");
            Assert.Less(Snapshot().Entities.Single(e=>e.Id==target).Health,initialTargetHealth);
            if(capture){System.IO.File.WriteAllLines(System.IO.Path.Combine(folder,"telemetry.csv"),telemetry);System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"white-pixels.txt"),"currentMax="+maxWhite+" beforeMax="+maxBeforeWhite+" bloomIntensity="+bloom.intensity.value+" threshold="+bloom.threshold.value);}
            Assert.Greater(maxBeforeWhite,128,"Control must reproduce the pre-fix white ball with the same bloom and flight.");
            Assert.Less(maxWhite,128,"Bloom must stay active without inflating a Hawk missile into a white ball.");
        }
        finally
        {
            var navigation=domainType.GetProperty("Navigation",Flags).GetValue(domain);navigation.GetType().GetMethod("ReleaseCommittedHostRoot",Flags).Invoke(navigation,null);Call("DestroyOwnedNavigation");
            foreach(var mesh in brokenFins.Values)UnityEngine.Object.DestroyImmediate(mesh);
            camera.targetTexture=null;image.Release();readback.Release();UnityEngine.Object.DestroyImmediate(readback);UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
    [UnityTest] public IEnumerator HawkLaunchAndContactFrames()
    {
        world.Remove(1);world.Remove(2);
        var vision=new PlayableVision(0,profile.ArenaHalfExtent,profile.ArenaHalfExtent,4,profile.FogEdgeFeather);
        vision.Refresh(new[]{new VisionSource(default,80)},Array.Empty<KnownBuilding>());world.Fog.Update(vision.Snapshot(),1);
        var lighting=new GameObject("Hawk evidence light");lighting.transform.SetParent(host.transform,false);var light=lighting.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;lighting.transform.rotation=Quaternion.Euler(45,-30,0);
        var cameraHost=new GameObject("Hawk evidence camera");cameraHost.transform.SetParent(host.transform,false);var camera=cameraHost.AddComponent<Camera>();camera.orthographic=true;camera.cullingMask=1<<8;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.08f,.1f);
        camera.gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());
        var texture=new RenderTexture(1600,1000,24);texture.Create();camera.targetTexture=texture;
        string folder=Environment.GetEnvironmentVariable("SPACEWARS_HAWK_PROJECTILE_EVIDENCE");
        if(string.IsNullOrEmpty(folder))folder=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.local/qa/hawk-contact/frames"));System.IO.Directory.CreateDirectory(folder);
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
            var flying=new[]{new PlayableProjectileSnapshot(301,3,0,new NavPoint(0,0),PlayableEntityKind.Hawk,height:2.3)};
            Render(30,shots:flying);Focus(new Vector3(-1,2,0),6);
            yield return null;Capture("04-missile-flight-normal");
            Focus(new Vector3(0,2.3f,0),1.4f);
            yield return null;Capture("05-missile-flight-close");
            var enemy=world.Tank(10,false,PlayableEntityKind.Tank);enemy.Root.position=new Vector3(2,0,0);
            var target=new Vector3(2,.45f,0);var from=new Vector3(-1,2,0);
            var contact=new PlayableSoundEvent(310,30,PlayableSoundKind.Impact,new NavPoint(2,0),PlayableEntityKind.Hawk,contact:new PlayableDirectContact(310,10,new BallisticPoint(from.x,from.y,from.z),new BallisticPoint(target.x,target.y,target.z)));
            Render(30,new[]{contact});Assert.True(OnMeshSurface(enemy.Hull,((GameObject)Field(Hits[310L],"Root")).transform.position));Focus(new Vector3(1,.6f,0),2.1f);yield return null;Capture("02-enemy-model-contact");
            world.Obstacle(new NavObstacle(8,-1,10,1));
            var obstacle=new PlayableSoundEvent(311,31,PlayableSoundKind.Impact,new NavPoint(7.93,0),PlayableEntityKind.Hawk,contact:new PlayableDirectContact(311,0,new BallisticPoint(7,1,0),new BallisticPoint(7.93,1,0)));
            Render(31,new[]{obstacle});Assert.AreEqual(8,((GameObject)Field(Hits[311L],"Root")).transform.position.x,.0001f);Focus(new Vector3(8.3f,1,0),2.2f);yield return null;Capture("03-obstacle-contact");
            int index=0;
            foreach(var model in new[]{(PlayableBuildingKind.Mine,false),(PlayableBuildingKind.ScientificCenter,false),(PlayableBuildingKind.ScientificCenter,true)})
            {
                int id=40+index;var at=new NavPoint(12+index*8,6);
                var actor=world.Building(id,model.Item1.ToString(),false,model.Item2);actor.Root.position=world.Point(at);world.FaceBuilding(actor);
                var buildingPose=BuildingCombatGeometry.Pose(profile,model.Item1,model.Item2,at,0);
                var above=new BallisticPoint(at.X,30,at.Z);Assert.True(buildingPose.Segment(above,new BallisticPoint(at.X,-2,at.Z),0,out _,out var impact));
                var surface=new PlayableSoundEvent(400+index,32+index,PlayableSoundKind.Impact,at,PlayableEntityKind.Hawk,true,contact:new PlayableDirectContact(400+index,id,above,impact));
                Render(32+index,new[]{surface});var effect=(GameObject)Field(Hits[400L+index],"Root");Assert.True(OnMeshSurface(actor.Hull,effect.transform.position));
                Focus(actor.Root.position+Vector3.up,3.2f);yield return null;Capture("0"+(6+index)+"-"+model.Item1+(model.Item2?"-upgraded":"")+"-contact");index++;
            }
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
