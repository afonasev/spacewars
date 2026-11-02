using System.Collections;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        // Authored greybox swatches, shared with the public minimap. Detailed finish is deferred.
        public static readonly Color CityGround=new Color(.48f,.51f,.49f),CityBlock=new Color(.30f,.40f,.47f),CityPlaza=new Color(.37f,.47f,.53f),CityRamp=new Color(.66f,.56f,.39f),CityRoad=new Color(.23f,.29f,.30f);
        IEnumerator CreateAntennaCity(AntennaCityMap map)
        {
            var budget=new LoadingFrameBudget();
            void Emit(string name,Mesh mesh,Color color){
                FinalProceduralMesh.IndexOwned(mesh);terrainMeshes.Add(mesh);var obj=new GameObject(name);obj.transform.SetParent(root,false);
                obj.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                var properties=new MaterialPropertyBlock();properties.SetColor("_BaseColor",color);properties.SetFloat("_SurfaceRole",0);renderer.SetPropertyBlock(properties);
            }
            // Order is visual only; Simulation support precedence is the authoritative ramp/plaza/street partition.
            foreach(var support in map.Supports.Reverse()){
                Emit(support.Id,TerrainMesh.Prism(support.Bounds,-.4,support.Height,support.HeightAt),support.Id.Contains("ramp")?CityRamp:support.Id.Contains("plaza")?CityPlaza:CityGround);
                if(budget.Expired){yield return null;budget.Restart();}
            }
            Mesh Combined(System.Collections.Generic.IEnumerable<NavObstacle> shapes,double height){
                var pieces=shapes.Select(shape=>TerrainMesh.Prism(shape,0,height)).ToArray();
                var mesh=new Mesh{name="City compact group",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
                mesh.CombineMeshes(pieces.Select(piece=>new CombineInstance{mesh=piece,transform=Matrix4x4.identity}).ToArray(),true,false);
                foreach(var piece in pieces)ReleaseWorldObject(piece);return mesh;
            }
            // Keep compact quarter bounds, but batch coplanar cut fragments without
            // changing their mesh attributes or authoritative blocked footprints.
            foreach(var quarter in map.Quarters){Emit("City blocked quarter",Combined(quarter,map.BlockHeight),CityBlock);if(budget.Expired){yield return null;budget.Restart();}}
            Emit("City central utility block",TerrainMesh.Prism(map.Blocks.Last(),0,map.BlockHeight),CityBlock);
            // Street/yard markings are flat terrain, so each can share one surface mesh.
            Emit("City open yards",Combined(map.Yards,.01),CityGround);
            Emit("City streets",Combined(map.Streets,.012),CityRoad);
            var pads=AntennaCityPads.Create(map,profile);
            // Minimal flat glyphs: four batched meshes, no hidden side/bottom faces or LOD tiers.
            Emit("City key point borders",AntennaCityPads.Mesh(pads,true),AntennaCityPads.Edge);
            foreach(var group in pads.GroupBy(p=>p.Kind))Emit("City key point "+group.Key,AntennaCityPads.Mesh(group,false),AntennaCityPads.Fill(group.Key));
        }
    }
}
