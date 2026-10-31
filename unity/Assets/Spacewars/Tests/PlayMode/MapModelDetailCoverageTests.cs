using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed class MapModelDetailCoverageTests
    {
        static T Data<T>(string name)=>JsonUtility.FromJson<T>(Resources.Load<TextAsset>(name).text);
        static IPlayableTerrain Map(string key)
        {
            switch(key){
                case "ThreeCrossings":return new ThreeCrossingsMap(Data<ThreeCrossingsProfileData>(key+"Profile"));
                case "WhiteMeridian":return new WhiteMeridianMap(Data<WhiteMeridianProfileData>(key+"Profile"));
                case "CrimsonGrove":return new CrimsonGroveMap(Data<CrimsonGroveProfileData>(key+"Profile"));
                case "MossHighland":return new MossHighlandMap(Data<MossHighlandProfileData>(key+"Profile"));
                case "NullDock":return new NullDockMap();
                case "BasaltCrown":return new BasaltCrownMap();case "Foundry":return new FoundryMap(Data<FoundryProfileData>(key+"Profile"));
                case "Campaign":return new CampaignOpeningMap();case "AiTest":return new AiTestMap();case "ProvingGround":return new ProvingGroundMap();default:return null;
            }
        }
        [UnityTest] public IEnumerator NullDockRetainsStaticStationAndLimitsLodToThreeNewMachines()
        {
            var host=new GameObject("NullDock excluded from LOD");PlayableWorld world=null;
            try{
                world=new PlayableWorld(host.transform,PlayableProfile.Create(PlayableProfile.Default.CopyData(),new NullDockMap()),staged:true);yield return world.Preparation;
                var machines=host.GetComponentsInChildren<ModelDetailLod>(true);Assert.AreEqual(3,machines.Length);
                Assert.True(machines.All(m=>m.name.StartsWith("Null field ")&&!m.IsCatalogBound),"Only the three authored replacement models use LOD");
                Assert.IsNull(Resources.Load<ModelDetailCatalog>("ModelDetail/NullDock"));
                var station=host.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name=="Station hull, decks and equipment");
                var source=station.sharedMesh;Assert.IsNull(station.GetComponent<ModelDetailLod>(),"Existing station remains excluded from LOD");
                Assert.AreEqual(world.DockMetrics.activeTriangles,source.triangles.Length/3+machines.Sum(m=>m.OriginalMeshes[0].triangles.Length/3));
                Assert.AreEqual(source.vertexCount,source.uv2.Length);Assert.True(source.uv2.All(v=>v.x>=0&&v.x<=6&&v.x==Mathf.Round(v.x)),"exact discrete shader roles");
                var camera=host.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=500;world.SelectEnvironmentDetail(camera);Assert.AreSame(source,station.sharedMesh);
                world.Clear();Assert.AreSame(source,station.sharedMesh);
            }finally{world?.Dispose();Object.Destroy(host);}
        }
        [UnityTest,Timeout(240000)] public IEnumerator AllShippedMapCatalogEntriesReachTheirActualGeneratedRenderers()
        {
            foreach(string key in new[]{"ThreeCrossings","WhiteMeridian","CrimsonGrove","MossHighland","BasaltCrown","Foundry","Campaign","AiTest","ProvingGround","Diagnostic","Nereida"}){
                var host=new GameObject("Catalog coverage "+key);PlayableWorld world=null;
                try{
                    if(key=="Nereida")host.AddComponent<NereidaPreview>().Build(NereidaPreview.LoadProfile());
                    else{world=new PlayableWorld(host.transform,PlayableProfile.Create(Data<PlayableProfileData>("PlayableProfile"),Map(key)),staged:true);yield return world.Preparation;}
                    var catalog=Resources.Load<ModelDetailCatalog>("ModelDetail/"+key);Assert.NotNull(catalog,key);
                    foreach(var filter in host.GetComponentsInChildren<MeshFilter>(true)){
                        if(!ModelDetailCatalog.IsModeled(key,filter.name)||Enumerable.Range(0,filter.sharedMesh.subMeshCount).All(i=>filter.sharedMesh.GetIndexCount(i)==0))continue;
                        var attached=filter.GetComponentInParent<ModelDetailLod>();if(attached&&!attached.IsCatalogBound)continue;
                        var source=attached?attached.OriginalMeshes[0]:filter.sharedMesh;
                        Assert.True(catalog.AccountsFor(source),key+"/"+filter.name+" must have reduced tiers or a measured source exception");
                    }
                    var bound=host.GetComponentsInChildren<ModelDetailLod>(true).Where(l=>catalog.Find(l.OriginalMeshes[0])!=null).ToArray();
                    var identities=bound.Select(l=>ModelDetailCatalog.Identity(l.OriginalMeshes[0])).Distinct().OrderBy(x=>x).ToArray();
                    CollectionAssert.AreEqual(catalog.entries.Select(e=>e.hash).OrderBy(x=>x).ToArray(),identities,key+" runtime coverage");
                    foreach(var detail in bound){
                        for(int i=0;i<detail.Renderers.Length;i++)Assert.AreEqual(detail.OriginalMeshes[i].bounds,detail.Renderers[i].localBounds,key+" close bounds");
                        var originals=detail.OriginalMeshes.ToArray();detail.SetDetail(2);detail.SetDetail(0);CollectionAssert.AreEqual(originals,detail.Renderers.Select(r=>r.GetComponent<MeshFilter>().sharedMesh));
                    }
                    foreach(var detail in bound)detail.SetDetail(2);
                    if(world!=null){world.Clear();foreach(var detail in bound)Assert.AreEqual(0,detail.Level,"new session resets scenery hysteresis");}
                    else foreach(var detail in bound)detail.SetDetail(0);
                }finally{world?.Dispose();Object.Destroy(host);}
                yield return null;
            }
        }
    }
}
