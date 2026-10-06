using System;
using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.AI;

namespace Spacewars.Presentation
{
    // Main-thread route provider only. Never creates a NavMeshAgent or writes domain positions.
    public sealed class UnityNavigationRouter : IDisposable
    {
        private NavMeshData data;
        private readonly List<Mesh> bakeMeshes=new List<Mesh>();
        private NavMeshDataInstance instance;
        private readonly NavMeshPath path = new NavMeshPath();
        private readonly NavGeometry geometry;
        private readonly NavigationProfile profile;
        public double BuildMilliseconds {get;private set;}
        public UnityNavigationRouter(NavGeometry geometry, NavigationProfile profile)
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();
            this.geometry = geometry; this.profile = profile;
            var sources = new List<NavMeshBuildSource>();
            sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                transform = Matrix4x4.TRS(new Vector3(0,-0.25f,0),Quaternion.identity,Vector3.one),
                size = new Vector3((float)geometry.HalfExtent*2,0.5f,(float)geometry.HalfExtent*2), area = 0 });
            foreach (var obstacle in geometry.Obstacles)
            {
                if(obstacle.Polygon==null)sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(new Vector3((float)(obstacle.MinX+obstacle.MaxX)/2,1,(float)(obstacle.MinZ+obstacle.MaxZ)/2),Quaternion.identity,Vector3.one),
                    size = new Vector3((float)(obstacle.MaxX-obstacle.MinX),2,(float)(obstacle.MaxZ-obstacle.MinZ)),area = 1 });
                else{var mesh=TerrainMesh.Prism(obstacle,0,2);bakeMeshes.Add(mesh);sources.Add(new NavMeshBuildSource{shape=NavMeshBuildSourceShape.Mesh,sourceObject=mesh,transform=Matrix4x4.identity,area=1});}
            }
            var settings = NavMesh.GetSettingsByIndex(0);
            // One bake voxel of conservative clearance absorbs NavMesh rasterization; domain radius stays identical.
            settings.agentRadius = (float)(profile.Radius+profile.GridCell/4);
            // Technical flat-fixture bake envelope; no ramps, stairs, or standing characters.
            settings.agentHeight = 1; settings.agentClimb = 0; settings.agentSlope = 0;
            settings.overrideVoxelSize = true; settings.voxelSize = (float)profile.GridCell/4;
            data = NavMeshBuilder.BuildNavMeshData(settings,sources,
                new Bounds(Vector3.zero,new Vector3((float)geometry.HalfExtent*2,8,(float)geometry.HalfExtent*2)),Vector3.zero,Quaternion.identity);
            if (data == null) throw new InvalidOperationException("NavMesh bake failed");
            instance = NavMesh.AddNavMeshData(data);BuildMilliseconds=timer.Elapsed.TotalMilliseconds;
        }
        public NavPoint[] FindPath(NavPoint start, NavPoint goal)
        {
            if (!geometry.IsFree(start,profile.Radius) || !geometry.IsFree(goal,profile.Radius)) return Array.Empty<NavPoint>();
            if (!NavMesh.CalculatePath(new Vector3((float)start.X,0,(float)start.Z), new Vector3((float)goal.X,0,(float)goal.Z),NavMesh.AllAreas,path)
                || path.status != NavMeshPathStatus.PathComplete) return Array.Empty<NavPoint>();
            var corners=path.corners;
            if(corners.Length==0)return Array.Empty<NavPoint>();
            // NavMesh corners are proposals. Keep the original double-precision goal
            // through a checked connector instead of exposing a mapped endpoint to authority.
            var result = new NavPoint[corners.Length+1];
            for(int i=0;i<corners.Length;i++) result[i]=new NavPoint(corners[i].x,corners[i].z);
            result[corners.Length]=goal;
            var previous=start;
            foreach(var point in result)
            {
                if(!geometry.IsFree(point,profile.Radius)||!geometry.SegmentFree(previous,point,profile.Radius))
                    return Array.Empty<NavPoint>();
                previous=point;
            }
            return result;
        }
        public void Dispose()
        {
            instance.Remove();foreach(var mesh in bakeMeshes)Release(mesh);bakeMeshes.Clear();Release(data);data=null;
        }
        private static void Release(UnityEngine.Object value)
        {
            if(value==null)return;
            // Editor diagnostics have no Player frame to complete deferred destruction.
            if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
