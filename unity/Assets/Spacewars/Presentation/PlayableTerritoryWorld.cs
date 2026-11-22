using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private sealed class PadView { public GameObject Root,Fill,ReserveBar; public Transform ReserveFill; public Transform[] ReserveDividers; public float ReserveFraction; public Renderer ReserveFillRenderer,ReserveBackground; public Quaternion Facing; public Mesh Mesh; public MeshFilter Surface; public Vector2 Half; public bool IntegratedMine,PublicFallback; public double Progress=double.NaN; public Color Color=Color.clear; }
        private readonly Dictionary<string,PadView> territoryPads=new Dictionary<string,PadView>();
        private readonly HashSet<int> readyPadSites=new HashSet<int>();
        private readonly Dictionary<string,Vector2> apronModelBounds=new Dictionary<string,Vector2>();
        private EnvironmentArtProfile apronArt;
        private Material apronMaterial,capturePadMaterial;
        private PlayableSnapshot padView;
        private const int MineReserveCells=4;
        private const float MineReserveWidth=1.25f, MineReserveBarHeight=.085f, MineReserveBorder=.0225f;
        private static readonly Color32 MineReserveLow=new Color32(205,118,103,255);
        private static readonly Color32 MineReserveMedium=new Color32(217,172,85,255);
        private static readonly Color32 MineReserveHigh=new Color32(138,188,120,255);
        private readonly MaterialPropertyBlock reserveBarPaint=new MaterialPropertyBlock();
        private EnvironmentArtProfile ApronArt=>apronArt??(apronArt=EnvironmentArtProfile.Load());
        private static void DestroyPad(PadView p){UnityEngine.Object.Destroy(p.Surface.sharedMesh);UnityEngine.Object.Destroy(p.Mesh);UnityEngine.Object.Destroy(p.Root);}
        public void ClearPads(){ClearVacantMirrorSites();ClearRelayViews();foreach(var p in territoryPads.Values)DestroyPad(p);territoryPads.Clear();readyPadSites.Clear();apronModelBounds.Clear();padView=null;}
        private Material ApronMaterial
        {
            get
            {
                if(apronMaterial)return apronMaterial;
                var shader=Resources.Load<Shader>("ConstructionApron");if(!shader)throw new InvalidOperationException("Missing construction apron shader");
                apronMaterial=new Material(shader);apronMaterial.SetTexture("_ConcreteTex",Resources.Load<Texture2D>("Environment/NaturalFrontier/Concrete"));
                apronMaterial.SetTexture("_FogMask",Fog.Texture);apronMaterial.SetColor("_FogColor",material.GetColor("_FogColor"));apronMaterial.SetVector("_FogBounds",material.GetVector("_FogBounds"));
                return apronMaterial;
            }
        }
        private Material CapturePadMaterial
        {
            get
            {
                if(capturePadMaterial)return capturePadMaterial;
                capturePadMaterial=new Material(ApronMaterial){name="Translucent capture pad",renderQueue=3000};
                capturePadMaterial.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                capturePadMaterial.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                capturePadMaterial.SetInt("_ZWrite",0);
                return capturePadMaterial;
            }
        }
        // The lower platform is the support datum; balconies/equipment may overhang it.
        private Vector2 ApronModelHalf(string key)
        {
            if(key=="factoryUpgraded")return ApronModelHalf("factory");
            if(key=="hangarUpgraded")return ApronModelHalf("hangar");
            if(apronModelBounds.TryGetValue(key,out var size))return size;
            var prefab=Resources.Load<GameObject>("StyleA/"+key);if(!prefab)throw new InvalidOperationException("Missing approved model: "+key);
            var inverse=prefab.transform.worldToLocalMatrix;
            if(key=="research-scanner"||key=="research-scanner-upgraded"){
                var markers=prefab.GetComponentsInChildren<Transform>(true);
                var min=markers.Single(t=>t.name=="footprintMin");var max=markers.Single(t=>t.name=="footprintMax");
                var a=inverse.MultiplyPoint3x4(min.position);var b=inverse.MultiplyPoint3x4(max.position);
                size=new Vector2(Mathf.Max(Mathf.Abs(a.x),Mathf.Abs(b.x)),Mathf.Max(Mathf.Abs(a.z),Mathf.Abs(b.z)))*(float)BuildingScale(key);
                apronModelBounds.Add(key,size);return size;
            }
            // Defense prefabs batch their concrete support by material; core buildings retain the named foundation.
            bool defense=key=="repair-tower"||key=="sentry-turret"||key=="gauss-tower";
            var foundations=prefab.GetComponentsInChildren<Renderer>(true).Where(r=>
                r.name.EndsWith(":foundation-concrete",StringComparison.Ordinal)||
                defense&&r.sharedMaterials.Length==1&&r.sharedMaterial&&r.sharedMaterial.name==key+"-concrete").ToArray();
            if(foundations.Length!=1)throw new InvalidOperationException("Expected one approved lower platform: "+key);
            foreach(var renderer in foundations)
            {
                var bounds=renderer.localBounds;var transform=inverse*renderer.transform.localToWorldMatrix;
                for(int i=0;i<8;i++)
                {
                    var corner=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    var p=transform.MultiplyPoint3x4(corner)*(float)BuildingScale(key);
                    size=Vector2.Max(size,new Vector2(Mathf.Abs(p.x),Mathf.Abs(p.z)));
                }
            }
            apronModelBounds.Add(key,size);return size;
        }
        private Vector2 ApronHalf(PlayableBuildingKind kind,bool upgraded=false)
            =>TerritoryRules.Defensive(kind)?ConstructionApronMesh.DefenseSupportHalf(profile,ApronArt)
                :ApronModelHalf(BuildingKey(kind.ToString(),upgraded))+Vector2.one*(ApronArt.apronMargin+(kind==PlayableBuildingKind.Outpost?ApronArt.outpostApronMargin:0));
        private Vector2 ObservedSupportHalf(string key,NavPoint point,PlayableBuildingKind emptyKind,PlayableBuildingSnapshot observed,PlayableSnapshot view)
        {
            if(observed!=null)return SiteApronHalf(point,observed.Kind,observed.ModelUpgraded);
            if(territoryPads.TryGetValue(key,out var retained))return retained.Half;
            var known=view.Vision?.KnownBuildings.FirstOrDefault(b=>b.Position.X==point.X&&b.Position.Z==point.Z);
            return known!=null?SiteApronHalf(point,known.Kind,known.ModelUpgraded):SiteApronHalf(point,emptyKind);
        }
        public void RenderSites(PlayableSnapshot view,Camera camera=null)
        {
            // A spectator can change perspective at the same paused tick. Do not retain
            // geometry knowledge learned from another owner or the omniscient overview.
            if(padView!=null&&(padView.Generation!=view.Generation||padView.Owner!=view.Owner||padView.Vision?.Team!=view.Vision?.Team))ClearPads();
            RenderRelayViews(view,camera);
            // Camera motion can occur while paused at the same tick; keep the support square to the facade.
            foreach(var pad in territoryPads.Values)
            {
                pad.Root.transform.rotation=pad.Facing;
                if(pad.ReserveBar&&camera)pad.ReserveBar.transform.rotation=camera.transform.rotation;
            }
            if(ReferenceEquals(padView,view))return;
            if(padView==null&&profile.AuthoredMap is DeafMirrorMap mirror)CreateVacantMirrorSites(mirror,view);
            padView=view;
            var live=new HashSet<string>();
            foreach(var state in view.Sites)
            {
                if(state.Site.Kind==PlayableBuildingKind.RelayTower)continue;
                var center=view.Buildings.FirstOrDefault(b=>b.Id==state.CenterId&&b.Phase!=ConstructionPhase.Pending);
                UpdatePad(state.Site.Id+":0",state.Site.Position,ObservedSupportHalf(state.Site.Id+":0",state.Site.Position,state.Site.Kind,center,view),state.Owner??state.Claimant,center==null?state.Progress:0,state.Contested,view.Tick,SiteFacing(state.Site.Position,camera,state.Site.Kind==PlayableBuildingKind.Mine),live,profile.AuthoredMap is WhiteMeridianMap&&state.Site.Kind!=PlayableBuildingKind.Mine,profile.AuthoredMap is FoundryMap&&state.Site.Kind==PlayableBuildingKind.Mine,lunarMine:profile.AuthoredMap is DeafMirrorMap&&state.Site.Kind==PlayableBuildingKind.Mine);
                if(state.Site.Kind==PlayableBuildingKind.Mine)
                    UpdateMineSite(territoryPads[state.Site.Id+":0"],state.MineReserve,state.MineReserveCapacity,center!=null||RememberedMine(view,state.Site.Position),camera);
                if(state.Ready)readyPadSites.Add(state.Site.Id);else readyPadSites.Remove(state.Site.Id);
                if(!state.Ready)continue;
                foreach(var slot in state.Site.Slots.Concat(state.Site.DefenseSlots(profile)))
                {
                    var observed=view.Buildings.FirstOrDefault(b=>b.SiteId==state.Site.Id&&b.SlotId==slot.Id&&b.Phase!=ConstructionPhase.Pending);
                    string key=state.Site.Id+":"+slot.Id;
                    UpdatePad(key,slot.Position,ObservedSupportHalf(key,slot.Position,TerritoryRules.DefenseSlot(slot.Id)?PlayableBuildingKind.SentryTurret:PlayableBuildingKind.Factory,observed,view),state.Owner,0,false,view.Tick,SitePadFacing(slot.Position,camera),live,profile.AuthoredMap is WhiteMeridianMap&&state.Site.Kind!=PlayableBuildingKind.Mine);
                }
            }
            foreach(var site in view.DiscoveredSites)
            {
                if(site.Kind==PlayableBuildingKind.RelayTower||view.Sites.Any(s=>s.Site.Id==site.Id))continue;
                // Static discovered ground is safe to remember; never inspect hidden live buildings here.
                UpdatePad(site.Id+":0",site.Position,ObservedSupportHalf(site.Id+":0",site.Position,site.Kind,null,view),null,0,false,view.Tick,SiteFacing(site.Position,camera,site.Kind==PlayableBuildingKind.Mine),live,profile.AuthoredMap is WhiteMeridianMap&&site.Kind!=PlayableBuildingKind.Mine,profile.AuthoredMap is FoundryMap&&site.Kind==PlayableBuildingKind.Mine,lunarMine:profile.AuthoredMap is DeafMirrorMap&&site.Kind==PlayableBuildingKind.Mine);
                if(site.Kind==PlayableBuildingKind.Mine)
                    UpdateMineSite(territoryPads[site.Id+":0"],null,0,RememberedMine(view,site.Position),camera);
                // Last observed readiness only: discovered ground must not disclose hidden construction.
                if(!readyPadSites.Contains(site.Id))continue;
                foreach(var slot in site.Slots.Concat(site.DefenseSlots(profile)))UpdatePad(site.Id+":"+slot.Id,slot.Position,ObservedSupportHalf(site.Id+":"+slot.Id,slot.Position,TerritoryRules.DefenseSlot(slot.Id)?PlayableBuildingKind.SentryTurret:PlayableBuildingKind.Factory,null,view),null,0,false,view.Tick,SitePadFacing(slot.Position,camera),live,profile.AuthoredMap is WhiteMeridianMap&&site.Kind!=PlayableBuildingKind.Mine);
            }
            // Match the public map geography without reading live state or revealing child slots.
            foreach(var site in view.PublicSites)
            {
                string key=site.Id+":0";
                if(site.Kind==PlayableBuildingKind.RelayTower||live.Contains(key))continue;
                bool mine=site.Kind==PlayableBuildingKind.Mine;
                UpdatePad(key,site.Position,ApronHalf(site.Kind),null,0,false,view.Tick,SiteFacing(site.Position,camera,mine),live,
                    lightPad:profile.AuthoredMap is WhiteMeridianMap&&!mine,
                    integratedMine:profile.AuthoredMap is FoundryMap&&mine,publicFallback:true,lunarMine:profile.AuthoredMap is DeafMirrorMap&&mine);
                if(mine)UpdateMineSite(territoryPads[key],null,0,false,camera);
            }
            foreach(var key in territoryPads.Keys.ToArray())if(!live.Contains(key)){DestroyPad(territoryPads[key]);territoryPads.Remove(key);}
        }
        public void UpdateBuildingHealth(Actor actor, PlayableBuildingSnapshot building, Camera camera, float ratio)
            => UpdateBuildingHealth(actor, building, new HealthBarView(camera), ratio);

        internal void UpdateBuildingHealth(Actor actor, PlayableBuildingSnapshot building, in HealthBarView view, float ratio)
        {
            UpdateHealth(actor, view, ratio);
            if(building.Kind!=PlayableBuildingKind.Mine||
                !territoryPads.TryGetValue(building.SiteId+":0",out var pad)||
                !pad.ReserveBar||!pad.ReserveBar.activeSelf)return;

            var reserve=pad.ReserveBar.transform;
            var health=actor.Health.parent;
            if(!health.gameObject.activeSelf)
            {
                reserve.localPosition=Vector3.up*MineReserveHeight(true);
                reserve.localScale=Vector3.one;
                LayoutMineReserveCells(pad,MineReserveBorder);
                return;
            }

            // Use the actual pixel-snapped outline, which changes with the viewport and zoom.
            const float reserveWidthRatio=.9f;
            var outline=actor.HealthOutline.localScale;
            var background=pad.ReserveBackground.transform.localScale;
            float gap=(outline.y-actor.HealthBackground.localScale.y)*.5f;
            reserve.rotation=health.rotation;
            reserve.position=health.position-health.up*((outline.y+background.y)*.5f+gap);
            reserve.localScale=new Vector3(outline.x*reserveWidthRatio/background.x,1,1);
            // The health outline is at least one pixel thick at this zoom.
            LayoutMineReserveCells(pad,gap/reserve.localScale.x);
        }
        private float MineReserveHeight(bool occupied)=>occupied?(float)profile.MineModelHeightMeters+.48f:.36f;

        private static bool RememberedMine(PlayableSnapshot view,NavPoint point)
            =>view.Vision!=null&&!view.Vision.IsVisible(point)&&view.Vision.KnownBuildings.Any(b=>b.Kind==PlayableBuildingKind.Mine&&b.Position.Equals(point));

        private void UpdateMineSite(PadView pad,double? reserve,double capacity,bool occupied,Camera camera)
        {
            pad.Surface.GetComponent<MeshRenderer>().enabled=!occupied;
            UpdateMineReserveBar(pad,reserve,capacity,occupied,camera);
        }

        private void UpdateMineReserveBar(PadView pad,double? reserve,double capacity,bool occupied,Camera camera)
        {
            if(!reserve.HasValue||capacity<=0||!camera)
            {
                if(pad.ReserveBar)pad.ReserveBar.SetActive(false);
                return;
            }

            if(!pad.ReserveBar)
            {
                pad.ReserveBar=new GameObject("Mine reserve");
                pad.ReserveBar.transform.SetParent(pad.Root.transform,false);
                pad.ReserveBar.layer=presentationLayer;
                var parent=pad.ReserveBar.transform;
                pad.ReserveBackground=ReservePart("Reserve background",Vector3.zero,
                    new Vector3(MineReserveWidth+2*MineReserveBorder,MineReserveBarHeight+.04f,.018f),Color.black);
                ReservePart("Reserve track",new Vector3(0,0,-.012f),
                    new Vector3(MineReserveWidth,MineReserveBarHeight,.01f),Dark);
                pad.ReserveFillRenderer=ReservePart("Reserve fill",Vector3.zero,Vector3.one,Color.white);
                pad.ReserveFill=pad.ReserveFillRenderer.transform;
                pad.ReserveDividers=new Transform[MineReserveCells-1];
                for(int i=0;i<pad.ReserveDividers.Length;i++)
                    pad.ReserveDividers[i]=ReservePart("Reserve divider "+(i+1),Vector3.zero,Vector3.one,Color.black).transform;

                Renderer ReservePart(string name,Vector3 position,Vector3 scale,Color color)
                {
                    var renderer=Part(name,parent,position,scale,color).GetComponent<Renderer>();
                    renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows=false;
                    return renderer;
                }
            }

            pad.ReserveBar.SetActive(true);
            pad.ReserveBar.transform.localScale=Vector3.one;
            pad.ReserveBar.transform.localPosition=Vector3.up*MineReserveHeight(occupied);
            pad.ReserveBar.transform.rotation=camera.transform.rotation;
            float fraction=Mathf.Clamp01((float)(reserve.Value/capacity));
            pad.ReserveFraction=fraction;
            Color color=fraction<.5f?Color.Lerp(MineReserveLow,MineReserveMedium,2*fraction):
                Color.Lerp(MineReserveMedium,MineReserveHigh,2*fraction-1);
            reserveBarPaint.SetColor("_BaseColor",color);
            pad.ReserveFillRenderer.SetPropertyBlock(reserveBarPaint);
            pad.ReserveFill.gameObject.SetActive(fraction>0);
            LayoutMineReserveCells(pad,MineReserveBorder);
        }

        private static void LayoutMineReserveCells(PadView pad,float gap)
        {
            gap=Mathf.Min(gap,MineReserveWidth/(2*MineReserveCells));
            float cellWidth=(MineReserveWidth-gap*(MineReserveCells-1))/MineReserveCells;
            float cellsFilled=pad.ReserveFraction*MineReserveCells;
            int separatorsFilled=Mathf.Max(0,Mathf.CeilToInt(cellsFilled)-1);
            float fillWidth=cellsFilled*cellWidth+separatorsFilled*gap;
            pad.ReserveFill.localScale=new Vector3(fillWidth,MineReserveBarHeight,.01f);
            pad.ReserveFill.localPosition=new Vector3((fillWidth-MineReserveWidth)*.5f,0,-.024f);
            for(int i=0;i<pad.ReserveDividers.Length;i++)
            {
                var divider=pad.ReserveDividers[i];
                divider.localScale=new Vector3(gap,MineReserveBarHeight,.01f);
                divider.localPosition=new Vector3(-MineReserveWidth*.5f+(i+1)*cellWidth+(i+.5f)*gap,0,-.036f);
            }
        }

        private void UpdatePad(string key,NavPoint point,Vector2 half,PlayableOwner? owner,double progress,bool contested,long tick,Quaternion facing,HashSet<string> live,bool lightPad=false,bool integratedMine=false,bool publicFallback=false,bool lunarMine=false)
        {
            live.Add(key);
            if(!territoryPads.TryGetValue(key,out var pad))
            {
                var obj=new GameObject("Site pad "+key);obj.transform.SetParent(root,false);obj.transform.localPosition=Point(point);obj.transform.rotation=facing;
                var surface=new GameObject("Technical apron");surface.transform.SetParent(obj.transform,false);var filter=surface.AddComponent<MeshFilter>();filter.sharedMesh=integratedMine?new Mesh{name="Integrated mine yard - no duplicate apron"}:ConstructionApronMesh.Build(half,SiteApronArt(lightPad),lunarMine);surface.AddComponent<MeshRenderer>().sharedMaterial=ApronMaterial;
                var fill=new GameObject("Capture");fill.transform.SetParent(obj.transform,false);var mesh=new Mesh{name="Capture pad fill"};fill.AddComponent<MeshFilter>().sharedMesh=mesh;fill.AddComponent<MeshRenderer>().sharedMaterial=CapturePadMaterial;
                Layer(obj);pad=new PadView{Root=obj,Fill=fill,Facing=facing,Mesh=mesh,Surface=filter,Half=half,IntegratedMine=integratedMine};territoryPads.Add(key,pad);
            }
            pad.Facing=facing;pad.Root.transform.rotation=facing;
            if(pad.Half!=half||pad.IntegratedMine!=integratedMine)
            {
                // Resize the same physical support only from an observed model; keep its lifecycle identity.
                ReleaseWorldObject(pad.Surface.sharedMesh);pad.Surface.sharedMesh=integratedMine?new Mesh{name="Integrated mine yard - no duplicate apron"}:ConstructionApronMesh.Build(half,SiteApronArt(lightPad),lunarMine);
                pad.Half=half;pad.IntegratedMine=integratedMine;pad.Progress=double.NaN;
            }
            if(pad.PublicFallback!=publicFallback)
            {
                var surfaceBlock=new MaterialPropertyBlock();
                surfaceBlock.SetFloat("_FogLimit",publicFallback?(float)profile.FogExploredOpacity:1f);
                pad.Surface.GetComponent<Renderer>().SetPropertyBlock(surfaceBlock);
                pad.PublicFallback=publicFallback;
            }
            if(pad.Progress!=progress)
            {
                // Sweep the entire inset apron continuously, following its chamfered contour.
                var polygon=ConstructionApronMesh.CapturePolygon(pad.Half,ApronArt,(float)progress);
                SetMesh(pad.Mesh,polygon);pad.Fill.transform.localPosition=Vector3.up*(ApronArt.apronHeight+(lunarMine?ConstructionApronMesh.MineCurbHeight:0)+.006f);pad.Progress=progress;
            }
            var color=OwnerPaint!=null&&owner.HasValue?OwnerPaint(owner):owner==PlayableOwner.Enemy?Enemy:Ally;
            if(contested)color*=.65f+.35f*(float)(.5+.5*Math.Sin(tick/30d*Math.PI*2*profile.CapturePulseHz));
            color.a=ApronArt.captureFillOpacity;
            if(color!=pad.Color){var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);pad.Fill.GetComponent<Renderer>().SetPropertyBlock(block);pad.Color=color;}
        }
        private static void SetMesh(Mesh mesh,List<Vector3> polygon)
        {
            mesh.Clear();if(polygon.Count<3)return;mesh.SetVertices(polygon);mesh.SetColors(Enumerable.Repeat(Color.white,polygon.Count).ToList());
            var indices=new List<int>();for(int i=1;i<polygon.Count-1;i++){indices.Add(0);indices.Add(i+1);indices.Add(i);}mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        }
    }
}
