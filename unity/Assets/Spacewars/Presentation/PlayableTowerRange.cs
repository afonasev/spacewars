using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        public const string TowerRangeStyleId="tower-range-zone-v1";
        private GameObject towerRangeObject;
        private Mesh towerRangeMesh;
        private Material towerRangeMaterial;
        private MaterialPropertyBlock towerRangeBlock;
        public int RangeTowerId {get;private set;}
        public GameObject TowerRangeVisual=>towerRangeObject;
        public static double TowerActionRange(PlayableProfile current,PlayableBuildingKind kind)
        {return kind==PlayableBuildingKind.GaussTower?current.GaussRange:TerritoryRules.Defensive(kind)?current.TurretRange:0;}
        public void RenderTowerRange(PlayableSnapshot view,ISet<int> selected,int preferred=0,bool visible=true)
        {
            PlayableBuildingSnapshot chosen=null;
            if(visible&&view!=null&&selected!=null)foreach(var building in view.Buildings)
            {
                if(building.Owner!=view.Owner||building.Phase!=ConstructionPhase.Ready||building.Health<=0||!TerritoryRules.Defensive(building.Kind)||!selected.Contains(building.Id))continue;
                if(chosen==null||building.Id==preferred||chosen.Id!=preferred&&building.Id<chosen.Id)chosen=building;
            }
            RangeTowerId=chosen?.Id??0;
            if(chosen==null){if(towerRangeObject)towerRangeObject.SetActive(false);return;}
            if(!towerRangeObject)CreateTowerRange();
            var current=view.ActiveProfile??profile;
            var color=Color.HSVToRGB((float)(chosen.Kind==PlayableBuildingKind.RepairTower?current.TowerRangeRepairHue:current.TowerRangeCombatHue),(float)current.TowerRangeSaturation,(float)current.TowerRangeBrightness);
            towerRangeBlock.SetVector("_Zone",new Vector4((float)chosen.Position.X,(float)chosen.Position.Z,0,(float)TowerActionRange(current,chosen.Kind)));
            towerRangeBlock.SetColor("_Color",color);
            towerRangeBlock.SetVector("_Appearance",new Vector4((float)current.TowerRangeFillOpacity,(float)current.TowerRangeBorderOpacity,(float)current.TowerRangeBorderWidth,(float)current.TowerRangeEdgeFeather));
            towerRangeBlock.SetFloat("_Lift",(float)current.TowerRangeLift);towerRangeBlock.SetFloat("_InnerFade",(float)current.TowerRangeInnerFade);
            towerRangeObject.GetComponent<MeshRenderer>().SetPropertyBlock(towerRangeBlock);towerRangeObject.SetActive(true);
        }
        private void CreateTowerRange()
        {
            var shader=Resources.Load<Shader>("TowerRangeZone");if(!shader)throw new System.InvalidOperationException("Missing tower coverage shader");
            towerRangeMaterial=new Material(shader);towerRangeMaterial.SetTexture("_FogMask",Fog.Texture);
            towerRangeMaterial.SetVector("_FogBounds",new Vector4((float)profile.ArenaHalfExtent,(float)profile.ArenaHalfExtent,0,0));
            towerRangeMesh=BuildTowerRangeSurface(profile);
            towerRangeObject=new GameObject("Selected tower action radius");towerRangeObject.transform.SetParent(root,false);Layer(towerRangeObject);
            towerRangeObject.AddComponent<MeshFilter>().sharedMesh=towerRangeMesh;
            var renderer=towerRangeObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=towerRangeMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            towerRangeBlock=new MaterialPropertyBlock();
        }
        public static Mesh BuildTowerRangeSurface(PlayableProfile current)
        {
            var vertices=new List<Vector3>();var indices=new List<int>();
            void Add(MapSupport support)
            {
                var bounds=support.Bounds;int start=vertices.Count;
                if(bounds.Polygon!=null)
                {
                    foreach(var point in bounds.Polygon.Vertices)vertices.Add(new Vector3((float)point.X,(float)support.HeightAt(point),(float)point.Z));
                    foreach(int index in bounds.Polygon.Triangles)indices.Add(start+index);
                }
                else
                {
                    // Rectangle triangulation is topology, not a readability/balance setting.
                    var points=new[]{new NavPoint(bounds.MinX,bounds.MinZ),new NavPoint(bounds.MinX,bounds.MaxZ),new NavPoint(bounds.MaxX,bounds.MaxZ),new NavPoint(bounds.MaxX,bounds.MinZ)};
                    foreach(var point in points)vertices.Add(new Vector3((float)point.X,(float)support.HeightAt(point),(float)point.Z));
                    foreach(int index in new[]{0,1,2,0,2,3})indices.Add(start+index);
                }
            }
            if(current.AuthoredMap!=null)foreach(var support in current.AuthoredMap.Supports)Add(support);
            else
            {
                double r=current.ArenaHalfExtent;var polygon=new NavPolygon(new[]{new NavPoint(-r,-r),new NavPoint(r,-r),new NavPoint(r,r),new NavPoint(-r,r)});
                Add(new MapSupport("flat-range-surface",new NavObstacle(polygon),false));
            }
            var mesh=new Mesh{name="Tower range supported surfaces",indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();return mesh;
        }
        private void ClearTowerRange()
        {
            if(towerRangeObject){towerRangeObject.SetActive(false);ReleaseWorldObject(towerRangeObject);}if(towerRangeMesh)ReleaseWorldObject(towerRangeMesh);if(towerRangeMaterial)ReleaseWorldObject(towerRangeMaterial);
            towerRangeObject=null;towerRangeMesh=null;towerRangeMaterial=null;towerRangeBlock=null;RangeTowerId=0;
        }
    }
}
