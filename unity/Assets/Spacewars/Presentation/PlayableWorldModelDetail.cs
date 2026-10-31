using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        readonly List<ModelDetailLod> sceneryDetail=new List<ModelDetailLod>();
        static string DetailMapKey(IPlayableTerrain map)=>map is CampaignOpeningMap?"Campaign":map==null?"Diagnostic":map.GetType().Name.Replace("Map","");
        IEnumerator GenerateTerrainAndDetail(MossHighlandArtProfile art,NullDockArtProfile dockArt)
        {
            yield return PrepareTerrain(art,dockArt);
            var catalog=Resources.Load<ModelDetailCatalog>("ModelDetail/"+DetailMapKey(profile.AuthoredMap));
            if(!catalog)yield break;
            var budget=new LoadingFrameBudget();
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true)){
                if(filter.GetComponentInParent<ModelDetailLod>()||!ModelDetailCatalog.IsModeled(DetailMapKey(profile.AuthoredMap),filter.name))continue;
                var row=catalog.Find(filter.sharedMesh);if(row==null)continue;
                var renderer=filter.GetComponent<MeshRenderer>();if(!renderer)continue;
                var detail=filter.gameObject.AddComponent<ModelDetailLod>();
                detail.Configure(new Renderer[]{renderer},new[]{filter.sharedMesh},new[]{row.normal},new[]{row.far},filter.sharedMesh.bounds);
                detail.ConfigureSelectionBounds(row.compactBounds);detail.MarkCatalogBound();sceneryDetail.Add(detail);
                if(budget.Expired){yield return null;budget.Restart();}
            }
        }
        sealed class WreckDetailBinding
        {
            internal Mesh[] Full,Normal,Far;internal Bounds Bounds;
        }
        // Only immutable imported wreck resources, scoped to this prepared world.
        // Do not cache arbitrary procedural meshes in ModelDetailCatalog.Find: those
        // can be edited in-place by authoring tools and must still be re-hashed.
        readonly Dictionary<string,WreckDetailBinding> wreckDetailBindings=new Dictionary<string,WreckDetailBinding>();
        WreckDetailBinding DescribeWreckDetail(GameObject model,string resource)
        {
            var catalog=Resources.Load<ModelDetailCatalog>("ModelDetail/Wrecks/"+resource);if(!catalog)return null;
            var filters=model.GetComponentsInChildren<MeshFilter>(true);
            var full=filters.Select(f=>f.sharedMesh).ToArray();var rows=full.Select(catalog.Find).ToArray();if(rows.All(r=>r==null))return null;
            var bounds=new Bounds();bool first=true;
            foreach(var f in filters)for(int mask=0;mask<8;mask++){
                var b=f.sharedMesh.bounds;var p=b.center+Vector3.Scale(b.extents,new Vector3((mask&1)==0?-1:1,(mask&2)==0?-1:1,(mask&4)==0?-1:1));
                p=model.transform.InverseTransformPoint(f.transform.TransformPoint(p));if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);
            }
            return new WreckDetailBinding{Full=full,Normal=rows.Select((r,i)=>r?.normal??full[i]).ToArray(),Far=rows.Select((r,i)=>r?.far??full[i]).ToArray(),Bounds=bounds};
        }
        ModelDetailLod BindWreckDetail(GameObject model,string resource)
        {
            if(!wreckDetailBindings.TryGetValue(resource,out var binding)){
                binding=DescribeWreckDetail(model,resource);wreckDetailBindings.Add(resource,binding);
            }
            if(binding==null)return null;
            var filters=model.GetComponentsInChildren<MeshFilter>(true);
            // A different prefab/mesh binding never inherits the prepared result.
            if(filters.Length!=binding.Full.Length||filters.Where((f,i)=>f.sharedMesh!=binding.Full[i]).Any())binding=DescribeWreckDetail(model,resource);
            if(binding==null)return null;
            var detail=model.AddComponent<ModelDetailLod>();
            detail.Configure(filters.Select(f=>(Renderer)f.GetComponent<MeshRenderer>()).ToArray(),(Mesh[])binding.Full.Clone(),(Mesh[])binding.Normal.Clone(),(Mesh[])binding.Far.Clone(),binding.Bounds);
            detail.MarkCatalogBound();return detail;
        }
        void ResetEnvironmentDetail(){if(root)foreach(var detail in root.GetComponentsInChildren<ModelDetailLod>(true))detail.SetDetail(0);}
        public void SelectEnvironmentDetail(Camera camera)
        {
            foreach(var detail in sceneryDetail)if(detail&&detail.gameObject.activeInHierarchy)detail.Select(camera);
            foreach(var wreck in unitWrecks.Values)if(wreck.Detail&&wreck.Model.activeInHierarchy)wreck.Detail.Select(camera);
        }
    }
}
