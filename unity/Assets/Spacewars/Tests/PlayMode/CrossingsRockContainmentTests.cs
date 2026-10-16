using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Tests.PlayMode
{
    public sealed class CrossingsRockContainmentTests
    {
        [Test] public void BaselineAndReadableRockTriangleInteriorsStayInsideAuthoritativeSolids()
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;var map=(ThreeCrossingsMap)profile.AuthoredMap;
            var art=EnvironmentArtProfile.Load();int baseline=0,after=0;
            int Check(Mesh mesh)
            {
                var vertices=mesh.vertices;var indices=mesh.triangles;int count=0;
                for(int i=0;i<indices.Length;i+=3)
                {
                    var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];
                    // Vertices have an existing full-volume regression. Also inspect
                    // edge interiors and face centroids: vertex containment alone can
                    // miss a triangle spanning a concave notch.
                    foreach(var p in new[]{(a+b+c)/3,(a+b)/2,(b+c)/2,(c+a)/2})
                    {
                        var point=new NavPoint(p.x,p.z);
                        Assert.IsTrue(map.Solids.Any(s=>p.y>=s.Bottom-.001&&p.y<=s.Top+.001&&(s.Contains(point)||s.BoundaryDistanceSquared(point)<.000001)),"Triangle spills outside authoritative rock at "+p);
                        count++;
                    }
                }
                return count;
            }
            foreach(var rock in map.Solids.Where(s=>s.Top>=map.WaterDepth&&!PlayableWorld.IsPlatformFence(s)))
            {
                var mesh=TerrainMesh.ErodedRock(rock,rock.Bottom>0?rock.Bottom-art.erosionDepth:rock.Bottom,rock.Top,art.erosionDepth);
                try{baseline+=Check(mesh);}finally{UnityEngine.Object.DestroyImmediate(mesh);}
            }
            var root=new GameObject("Triangle containment inspection");
            try{using(var world=new PlayableWorld(root.transform,profile))
            {
                var block=new MaterialPropertyBlock();
                foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())
                {
                    renderer.GetPropertyBlock(block);if(block.GetFloat("_SurfaceRole")!=4||renderer.name.StartsWith("Detail —"))continue;
                    after+=Check(renderer.GetComponent<MeshFilter>().sharedMesh);
                }
            }}finally{UnityEngine.Object.DestroyImmediate(root);}
            Assert.Greater(baseline,0);Assert.Greater(after,0);TestContext.WriteLine("ROCK_TRIANGLE_SAMPLES baseline="+baseline+" after="+after);
        }
    }
}
