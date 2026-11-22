using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // The compact map retains its existing fog-resolution raster.
    public sealed class PlayableMapTerrain : IDisposable
    {
        public Texture2D Texture{get;}
        public long Uploads{get;private set;}
        private long fogUpload=-1;
        private long[] unionUploads;
        private PlayableFogMask[] unionMasks;
        private bool publicTerrainShown;
        private readonly Color[] terrain;
        private readonly Color32[] pixels;
        private readonly byte[] paintedOpacity;
        private bool paintedValid,unionPaint;
        public long PaintedPixels{get;private set;}
        private readonly Color tint;
        private readonly bool overhead;
        public PlayableMapTerrain(PlayableProfile p,bool staged=false,Color[] overheadTerrain=null)
        {
            int size=PlayableVision.RasterResolution;overhead=overheadTerrain!=null;terrain=overheadTerrain??new Color[size*size];pixels=new Color32[terrain.Length];paintedOpacity=new byte[terrain.Length];
            Texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name="Shared map terrain",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            tint=new Color((float)p.FogTintR,(float)p.FogTintG,(float)p.FogTintB);
            PreparationCacheHit=overhead;
            Preparation=overhead ? ReadyOverhead() : Prepare(p,size);
            if(!staged)LoadingPreparation.Drain(Preparation);
        }
        private static System.Collections.IEnumerator ReadyOverhead(){yield break;}
        public bool PreparationCacheHit{get;private set;}
        public System.Collections.IEnumerator Preparation{get;}
        private System.Collections.IEnumerator Prepare(PlayableProfile p,int size)
        {
            string group="Minimap-"+PreparedMapCache.MapKey(p.AuthoredMap),key=PreparedMapCache.Key(p.AuthoredMap,p);
            if(PreparedMapCache.TryColors(group,key,terrain)){PreparationCacheHit=true;yield break;}
            yield return Rasterize(p,size,terrain);
            PreparedMapCache.SaveColors(group,key,terrain);
        }
        internal static System.Collections.IEnumerator Rasterize(PlayableProfile p,int size,Color[] terrain)
        {
            var budget=new LoadingFrameBudget();
            var obstacleBand=p.MinimapTerrainStroke*(p.ArenaHalfExtent*2/PlayableVision.RasterResolution);
            // Reuse the world's basalt palette; profile controls all added contrast tuning.
            Color ground=new Color(.20f,.25f,.25f);Color.RGBToHSV(ground,out float h,out float s,out float v);
            ground=Color.HSVToRGB(h,s*(float)p.MinimapTerrainSaturation,v*(float)p.MinimapTerrainBrightness);
            var walls=PlayableMap.SolidObstacles(p);
            var meridianArt=p.AuthoredMap is WhiteMeridianMap?WhiteMeridianArtProfile.Load():null;
            var mossArt=p.AuthoredMap is MossHighlandMap?MossHighlandArtProfile.Load():null;
            var basaltArt=p.AuthoredMap is BasaltCrownMap?BasaltCrownArtProfile.Load():null;
            var basaltMap=p.AuthoredMap as BasaltCrownMap;
            var basaltMines=basaltMap!=null?BasaltCrownDetails.Mines(basaltMap,p):null;
            var sitePads=basaltMap!=null?BasaltCrownDetails.SitePads(basaltMap,p,basaltArt):Array.Empty<BasaltCrownDetails.SitePad>();
            var basaltPlaza=basaltMap!=null?BasaltCrownRoads.Plaza(basaltMap,p):default;
            var cityPads=p.AuthoredMap is AntennaCityMap cityPadMap?AntennaCityPads.Create(cityPadMap,p):Array.Empty<AntennaCityPads.Pad>();
            var mirrorPads=p.AuthoredMap is DeafMirrorMap moon?PlayableWorld.MirrorPads(moon,p):Array.Empty<AntennaCityPads.Pad>();
            var foundryArt=p.AuthoredMap is FoundryMap?FoundrySurfaceProfile.Load():null;
            var heights=p.AuthoredMap==null?null:new float[terrain.Length];
            var dockMap=p.AuthoredMap as NullDockMap;
            var dockArt=dockMap!=null?NullDockArtProfile.Load():null;
            var dockHome=dockMap!=null?dockMap.Supports.Single(s=>s.Id=="H-A").Bounds:default(NavObstacle);
            var dockNorth=dockMap!=null?dockMap.Supports.Single(s=>s.Id=="F-N").Bounds:default(NavObstacle);
            var dockSouth=dockMap!=null?dockMap.Supports.Single(s=>s.Id=="F-S").Bounds:default(NavObstacle);
            var dockSites=dockMap?.Sites(p).Where(s=>s.Kind!=PlayableBuildingKind.Mine).ToArray();
            var dockRelays=dockMap!=null&&dockMap.Revision>=4
                ? dockSites.Where(s=>s.Kind==PlayableBuildingKind.RelayTower).Select(s=>s.Position).ToArray()
                : Array.Empty<NavPoint>();
            float texelMetres=(float)(2*p.ArenaHalfExtent/size);
            var grove=p.AuthoredMap as CrimsonGroveMap;
            var groveArt=grove!=null?CrimsonGroveArtProfile.Load():null;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                if(x==0&&budget.Expired){yield return null;budget.Restart();}
                double wx=((x+.5)/size*2-1)*p.ArenaHalfExtent,wz=((y+.5)/size*2-1)*p.ArenaHalfExtent;
                var terrainPoint=new NavPoint(wx,wz);
                var mapSupport=heights==null?null:p.AuthoredMap.SupportAt(terrainPoint);
                if(heights!=null)heights[y*size+x]=mapSupport==null?float.NaN:(float)mapSupport.HeightAt(terrainPoint);
                bool obstacle=false;foreach(var wall in walls)if(wall.Contains(terrainPoint)){obstacle=true;break;}
                bool edge=false;
                if(obstacle)foreach(var wall in walls){if(wall.Contains(terrainPoint)&&wall.BoundaryDistanceSquared(terrainPoint)<obstacleBand*obstacleBand)edge=true;}
                if(p.AuthoredMap is FoundryMap foundry){bool road=System.Linq.Enumerable.Any(foundry.Roads,r=>r.Contains(terrainPoint));terrain[y*size+x]=System.Linq.Enumerable.Any(foundry.Lava,l=>l.Contains(terrainPoint))?PlayableWorld.FoundryLava:obstacle?PlayableWorld.FoundryRock:road?PlayableWorld.FoundryRoad:foundryArt.GroundAtHeight(mapSupport?.HeightAt(terrainPoint)??0,foundry);}
                else if(dockMap!=null){
                    Color color=Color.clear;
                    if(mapSupport!=null){
                        color=dockArt.Upper;
                        if(mapSupport.Id=="station-deck"){
                            // Same static support boundaries and sRGB palette as NullDockSurface.
                            bool central=Math.Abs(wx)<=-dockHome.MaxX&&Math.Abs(wz)<=dockNorth.MinZ;
                            bool rear=wx>=-dockSouth.MinX&&wz>=-dockHome.MinZ||wx<=dockSouth.MinX&&wz<=dockHome.MinZ;
                            color=central?dockArt.Battle:rear?dockArt.Rear:dockArt.Contested;
                            if(!obstacle)
                                color=NullDockArtProfile.PaintRelayPaving(color,terrainPoint,dockRelays,(float)p.RelayCaptureRadius,texelMetres);
                        }else if(mapSupport.Id.StartsWith("H-")||mapSupport.Id.StartsWith("F-")){
                            var b=mapSupport.Bounds;
                            if(wx>=b.MinX+1.2&&wx<=b.MaxX-1.2&&wz>=b.MinZ+1.2&&wz<=b.MaxZ-1.2)
                                color=mapSupport.Id.StartsWith("H-")?dockArt.Headquarters:dockArt.Outpost;
                        }
                        if(mapSupport.HeightAt(terrainPoint)>0&&dockSites.Any(site=>Math.Pow(site.Position.X-wx,2)+Math.Pow(site.Position.Z-wz,2)<11.3*11.3))
                            color=Color.Lerp(color,Color.white,dockArt.siteDiskLift);
                    }
                    terrain[y*size+x]=mapSupport==null?Color.clear:obstacle?new Color(.22f,.28f,.32f):color;
                }
                else if(p.AuthoredMap is DeafMirrorMap mirror){
                    var pad=mirrorPads.FirstOrDefault(a=>a.Outer.Contains(terrainPoint));
                    terrain[y*size+x]=mapSupport==null?Color.clear:obstacle?new Color(.29f,.29f,.32f):pad!=null?AntennaCityPads.At(pad,terrainPoint):mirror.Bridges.Any(b=>b.Contains(terrainPoint))?new Color(.65f,.68f,.70f):PlayableWorld.MirrorDistrictAt(mirror,terrainPoint);
                    if(mapSupport!=null)terrain[y*size+x].a=1;
                }
                else if(p.AuthoredMap is AntennaCityMap city){
                    var pad=cityPads.FirstOrDefault(a=>a.Outer.Contains(terrainPoint));
                    terrain[y*size+x]=mapSupport==null?Color.clear:obstacle?PlayableWorld.CityBlock:pad!=null?AntennaCityPads.At(pad,terrainPoint):mapSupport.Id.Contains("ramp")?PlayableWorld.CityRamp:mapSupport.Height>0?PlayableWorld.CityPlaza:city.Streets.Any(r=>r.Contains(terrainPoint))?PlayableWorld.CityRoad:PlayableWorld.CityGround;
                }
                else if(grove!=null){
                    // Match the ground-to-terrace blend in CrimsonGroveSurface.shader.
                    float upper=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.12f,.9f,heights[y*size+x]/(float)grove.TerraceHeight));
                    terrain[y*size+x]=mapSupport==null?Color.clear:obstacle?groveArt.Rock:Color.Lerp(groveArt.Ground,groveArt.Terrace,upper);
                }
                else if(p.AuthoredMap is WhiteMeridianMap meridian){
                    var q=new NavPoint(wx,wz);var c=obstacle?meridianArt.Gypsum*(float)(1-p.MinimapTerrainTierContrast):meridian.SurfaceHeight(q)>0?meridianArt.Gypsum:meridianArt.Salt;
                    if(meridian.Wreck.Any(w=>w.Contains(q)))c=new Color(.26f,.29f,.28f);
                    terrain[y*size+x]=mapSupport==null?Color.clear:c;
                }
                else if(p.AuthoredMap is MossHighlandMap moss){
                    var q=new NavPoint(wx,wz);var patch=moss.Materials.FirstOrDefault(m=>m.Bounds.Contains(q));var color=mossArt.Palette(patch==null?4:MossHighlandArtProfile.Role(patch.Material));
                    Color.RGBToHSV(color,out float mh,out float ms,out float mv);color=Color.HSVToRGB(mh,ms*(float)p.MinimapTerrainSaturation,mv*(float)p.MinimapTerrainBrightness);
                    terrain[y*size+x]=moss.BorderRivers.Any(r=>r.Contains(q))?mossArt.RiverColor:moss.BorderMountains.Any(r=>r.Contains(q))?mossArt.Palette(1)*(float)(1-p.MinimapTerrainTierContrast):obstacle?color*(float)(1-p.MinimapTerrainTierContrast):color;
                }
                else if(p.AuthoredMap is BasaltCrownMap basalt){
                    var q=new NavPoint(wx,wz);var patch=basalt.Materials.FirstOrDefault(m=>m.Bounds.Contains(q));
                    var color=basaltArt.Palette(patch==null?0:BasaltCrownArtProfile.Role(patch.Material));
                    if(!obstacle&&basaltMines.Any(site=>Math.Max(Math.Max(Math.Abs(wx-site.Position.X),Math.Abs(wz-site.Position.Z)),(Math.Abs(wx-site.Position.X)+Math.Abs(wz-site.Position.Z))*.70710678)<basaltArt.mineApronRadius-basaltArt.mineApronFeather))color=Color.HSVToRGB(basaltArt.warmHue,basaltArt.warmSaturation*basaltArt.pavingSaturation,basaltArt.mineApronValue);
                    if(!obstacle){
                        float road=BasaltCrownRoads.Coverage((float)wx,(float)wz,basaltArt,basaltPlaza);
                        float site=0;
                        foreach(var pad in sitePads){
                            float distance=BasaltCrownDetails.PadDistance(q,pad);
                            site=Mathf.Max(site,1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(-basaltArt.siteApronFeather,0,distance)));
                        }
                        color=Color.Lerp(color,basaltArt.Palette(2),Mathf.Max(road,site));
                    }
                    Color.RGBToHSV(color,out float bh,out float bs,out float bv);
                    color=Color.HSVToRGB(bh,bs*(float)p.MinimapTerrainSaturation,bv*(float)p.MinimapTerrainBrightness);
                    terrain[y*size+x]=mapSupport==null?Color.clear:obstacle?color*(float)(1-p.MinimapTerrainTierContrast):color;
                }
                else if(p.AuthoredMap!=null)terrain[y*size+x]=obstacle?PlayableWorld.RockColor:mapSupport==null?PlayableWorld.WaterColor:mapSupport.IsBridge||mapSupport.Id.StartsWith("platform-")?PlayableWorld.BridgeColor:PlayableWorld.BankColor;
                else terrain[y*size+x]=obstacle?ground+Color.white*(float)(edge?-p.MinimapTerrainTierContrast:p.MinimapTerrainTierContrast):ground;
                if(p.AuthoredMap!=null&&obstacle&&edge&&terrain[y*size+x].a>0)
                    terrain[y*size+x]=Color.Lerp(terrain[y*size+x],new Color(.10f,.14f,.14f,terrain[y*size+x].a),(float)p.MinimapTerrainTierContrast*2f);
            }
            if(heights!=null){
                int stride=Mathf.Max(1,Mathf.RoundToInt((float)size/PlayableVision.RasterResolution));
                float step=(float)(p.ArenaHalfExtent*2/size)*stride*.5f;
                for(int y=stride;y<size-stride;y++)for(int x=stride;x<size-stride;x++){
                    if(x==stride&&budget.Expired){yield return null;budget.Restart();}
                    int i=y*size+x;float elevation=heights[i];if(float.IsNaN(elevation)||terrain[i].a<=0)continue;
                    if(elevation-heights[i-stride]>step||elevation-heights[i+stride]>step||elevation-heights[i-size*stride]>step||elevation-heights[i+size*stride]>step)
                        terrain[i]=Color.Lerp(terrain[i],new Color(.10f,.14f,.14f,terrain[i].a),(float)p.MinimapTerrainTierContrast*3.5f);
                }
            }
        }
        public void ShowPublicTerrain()
        {
            if(publicTerrainShown)return;
            publicTerrainShown=true;paintedValid=false;
            Texture.SetPixels(terrain);Texture.Apply(false,false);Uploads++;
        }
        public bool UpdateUnion(IReadOnlyList<PlayableFogMask> masks)
        {
            bool delta = paintedValid && unionPaint && unionMasks?.Length == masks.Count;
            bool uploadsChanged = false;
            for (int seat = 0; delta && seat < masks.Count; seat++)
            {
                var mask = masks[seat];
                delta = ReferenceEquals(unionMasks[seat], mask) &&
                    mask.TryReadOpacityChanges(unionUploads[seat], out _);
                uploadsChanged |= unionUploads[seat] != mask.Uploads;
            }
            if (delta && !uploadsChanged) return false;

            long before = PaintedPixels;
            if (delta)
            {
                for (int seat = 0; seat < masks.Count; seat++)
                {
                    masks[seat].TryReadOpacityChanges(unionUploads[seat], out var indices);
                    foreach (int index in indices) PaintUnionPixel(masks, index);
                }
            }
            else
            {
                for (int index = 0; index < pixels.Length; index++) PaintUnionPixel(masks, index);
            }

            if (unionMasks?.Length != masks.Count)
            {
                unionMasks = new PlayableFogMask[masks.Count];
                unionUploads = new long[masks.Count];
            }
            for (int seat = 0; seat < masks.Count; seat++)
            {
                unionMasks[seat] = masks[seat];
                unionUploads[seat] = masks[seat].Uploads;
            }
            unionPaint = paintedValid = true;
            if (before == PaintedPixels) return false;
            Texture.SetPixels32(pixels);
            Texture.Apply(false, false);
            Uploads++;
            return true;
        }

        private void PaintUnionPixel(IReadOnlyList<PlayableFogMask> masks, int index)
        {
            byte opacity = masks[0].OpacityAt(index);
            for (int seat = 1; seat < masks.Count; seat++)
                opacity = Math.Min(opacity, masks[seat].OpacityAt(index));
            PaintFogPixel(index, opacity);
        }

        public bool Update(PlayableFogMask fog)
        {
            if(!unionPaint&&fogUpload==fog.Uploads)return false;unionPaint=false;fogUpload=fog.Uploads;
            // Read the same authoritative presentation bytes without copying a texture.
            for(int i=0;i<pixels.Length;i++)PaintFogPixel(i,fog.OpacityAt(i));
            paintedValid=true;
            Texture.SetPixels32(pixels);Texture.Apply(false,false);Uploads++;return true;
        }
        // Fixed paint floor preserves public geography independently of hidden entities. Pixel-size bounds below
        // prevent dots disappearing or symbols covering compact terrain; these are UI packing invariants,
        // not changes to designer-owned vision radii, masks or marker eligibility.
        private void PaintFogPixel(int index,byte opacity)
        {
            if(paintedValid&&paintedOpacity[index]==opacity)return;
            pixels[index]=MapFogColor(terrain[index],opacity/255f);paintedOpacity[index]=opacity;PaintedPixels++;
        }
        private Color MapFogColor(Color ground,float fog){
            if(overhead)return Color.Lerp(ground,tint,fog*.72f);
            float brightest=Mathf.Max(ground.r,Mathf.Max(ground.g,ground.b));
            float boost=Mathf.Min(1.3f,.97f/Mathf.Max(.01f,brightest));
            var visible=ground;visible.r*=boost;visible.g*=boost;visible.b*=boost;
            return Color.Lerp(visible,tint,fog*.72f);
        }
        public void Dispose(){if(Texture)UnityEngine.Object.Destroy(Texture);}
    }

    public sealed class PlayableMapSurface : VisualElement
    {
        private PlayableProfile profile;
        private float scanLineWidth;
        private readonly PlayableOrderMarkerLayer orderMarkers;
        public void SetOrderMarkers(IReadOnlyList<PlayableQueueMarker> markers,IReadOnlyList<NavLocation> pulse=null,double progress=1)=>orderMarkers.Set(markers,pulse,progress);
        private Texture terrain;
        private IReadOnlyList<PlayableMapMark> marks;
        private HashSet<int> selected;
        private NavPoint[] footprint;
        private NavPoint? scanPreview;private double scanRadius;private IReadOnlyList<OrbitalScanLine> scanLines=Array.Empty<OrbitalScanLine>();
        private IReadOnlyList<OrbitalScanTriangle> scanTriangles=Array.Empty<OrbitalScanTriangle>();
        private IReadOnlyList<OrbitalScanTriangle>[] scanTriangleLayers;
        private IReadOnlyList<OrbitalScanLine>[] scanLineLayers;
        public void SetScanLayers(IReadOnlyList<OrbitalScanTriangle>[] triangles,IReadOnlyList<OrbitalScanLine>[] lines)
        {scanTriangleLayers=triangles;scanLineLayers=lines;scanTriangles=Array.Empty<OrbitalScanTriangle>();scanLines=Array.Empty<OrbitalScanLine>();MarkDirtyRepaint();}
        public void SetScanTriangles(IReadOnlyList<OrbitalScanTriangle> triangles){scanTriangleLayers=null;scanTriangles=triangles;MarkDirtyRepaint();}
        public void SetScanLines(IReadOnlyList<OrbitalScanLine> lines){scanLineLayers=null;scanLines=lines;MarkDirtyRepaint();}
        private bool scanPreviewBlocked;
        private int? scanPreviewCount;
        private readonly Label scanCountLabel;
        private NavPoint[] reinforcementPreview;
        private PlayableEntityKind reinforcementKind;
        private double reinforcementFootprint;
        private bool reinforcementValid;
        internal void SetReinforcementPreview(NavPoint[] points,PlayableEntityKind kind,double radius,bool valid)
        {
            if((ReferenceEquals(reinforcementPreview,points)||reinforcementPreview!=null&&points!=null&&reinforcementPreview.SequenceEqual(points))&&
                reinforcementKind==kind&&reinforcementFootprint==radius&&reinforcementValid==valid)return;
            reinforcementPreview=points;reinforcementKind=kind;reinforcementFootprint=radius;reinforcementValid=valid;MarkDirtyRepaint();
        }
        private bool nuclearPreview;private string nuclearPreviewReason;
        public bool NuclearPreview=>nuclearPreview&&scanPreview.HasValue;
        public void SetNuclearPreview(bool active,string reason=null)
        {
            if(nuclearPreview==active&&nuclearPreviewReason==reason)return;
            nuclearPreview=active;nuclearPreviewReason=reason;MarkDirtyRepaint();
        }
        public void SetScanPreview(NavPoint? point,double radius,bool blocked=false,int? count=null)
        {
            if(scanPreview.Equals(point)&&scanRadius==radius&&scanPreviewBlocked==blocked&&scanPreviewCount==count)return;
            scanPreview=point;scanRadius=radius;scanPreviewBlocked=blocked;scanPreviewCount=count;
            PositionScanCount();MarkDirtyRepaint();
        }
        private void PositionScanCount()
        {
            bool show=scanPreview.HasValue&&scanPreviewCount.HasValue&&contentRect.width>0&&contentRect.height>0;
            scanCountLabel.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
            if(!show)return;
            var at=Point(scanPreview.Value);
            scanCountLabel.text=scanPreviewCount.Value.ToString();
            scanCountLabel.style.left=Mathf.Clamp(at.x+8,0,Mathf.Max(0,contentRect.width-18));
            scanCountLabel.style.top=Mathf.Clamp(at.y-9,0,Mathf.Max(0,contentRect.height-18));
            scanCountLabel.style.color=scanPreviewBlocked?PlayableBootstrap.InvalidOrbitalPreview:OrbitalTheme.Cyan;
        }
        private PlayableBuildingSnapshot[] rallyBuildings=Array.Empty<PlayableBuildingSnapshot>();
        public int RallyFlagCount=>rallyBuildings.Length;
        private PlayableMapTransform transform;
        public PlayableMapSurface(PlayableProfile profile,Texture terrain)
        {
            this.profile=profile;scanLineWidth=(float)profile.CopyData().orbitalScanLineWidth;this.terrain=terrain;pickingMode=PickingMode.Ignore;style.overflow=Overflow.Hidden;
            generateVisualContent+=Draw;orderMarkers=new PlayableOrderMarkerLayer(location=>Point(location.Position),true);Add(orderMarkers);
            scanCountLabel=new Label{name="orbital-preview-count",pickingMode=PickingMode.Ignore};
            scanCountLabel.style.position=Position.Absolute;scanCountLabel.style.backgroundColor=OrbitalTheme.Panel;
            scanCountLabel.style.paddingLeft=scanCountLabel.style.paddingRight=4;scanCountLabel.style.fontSize=12;
            scanCountLabel.style.display=DisplayStyle.None;Add(scanCountLabel);
            RegisterCallback<GeometryChangedEvent>(_=>PositionScanCount());
        }
        public void Rebind(PlayableProfile next,Texture texture){profile=next;scanLineWidth=(float)next.CopyData().orbitalScanLineWidth;terrain=texture;marks=null;campaignGoals=null;footprint=null;rallyBuildings=Array.Empty<PlayableBuildingSnapshot>();MarkDirtyRepaint();}
        public void Set(PlayableSnapshot view,HashSet<int> selection,NavPoint[] camera,bool showOrbitalMines=false,double? cameraYawDegrees=null)
        {
            scanTriangleLayers=null;scanLineLayers=null;scanLines=view.ScanLines;scanTriangles=view.ScanTriangles;
            transform=new PlayableMapTransform(view.Vision.HalfWidth,view.Vision.HalfDepth,cameraYawDegrees);marks=LayeredMarks(PlayableMapView.Marks(view,showOrbitalMines));
            selected=selection;footprint=camera;
            orderMarkers.MarkDirtyRepaint();PositionScanCount();
            rallyBuildings=view.Buildings.Where(b=>PlayableMenuDescriptions.RallyProducer(b.Kind)&&selection.Contains(b.Id)&&b.Owner==view.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.HasRally==true&&b.PrivateState.Lifecycle?.Selling!=true).ToArray();MarkDirtyRepaint();
        }
        public void SetPublic(IReadOnlyList<PlayableMapMark> publicMarks,double extent)
        {teamSignals=Array.Empty<PlayableTeamSignal>();transform=new PlayableMapTransform(extent,extent);marks=LayeredMarks(publicMarks);selected=new HashSet<int>();footprint=null;rallyBuildings=Array.Empty<PlayableBuildingSnapshot>();MarkDirtyRepaint();}
        public NavPoint Ground(Vector2 local)=>transform.Ground(new NavPoint(local.x/contentRect.width,local.y/contentRect.height));
        public Vector2 Point(NavPoint world){var uv=transform.Project(world);return new Vector2((float)uv.X*contentRect.width,(float)uv.Z*contentRect.height);}
        public int[] SelectOwnUnits(PlayableSnapshot view,Vector2 a,Vector2 b)
        {
            var box=Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));
            return view.Entities.Where(e=>{var p=Point(e.Position);return e.Owner==view.Owner&&p.x>=box.xMin&&p.x<=box.xMax&&p.y>=box.yMin&&p.y<=box.yMax;}).Select(e=>e.Id).ToArray();
        }
        private System.Collections.Generic.IReadOnlyList<CampaignObjective> campaignGoals;
        public void SetCampaignGoals(System.Collections.Generic.IReadOnlyList<CampaignObjective> goals){campaignGoals=goals;MarkDirtyRepaint();}
        private IReadOnlyList<OrbitalNuclearSnapshot> nuclearSignals=Array.Empty<OrbitalNuclearSnapshot>();
        internal void SetNuclearSignals(IReadOnlyList<OrbitalNuclearSnapshot> signals)
        {
            if(nuclearSignals.Count==0 && signals.Count==0)return;
            nuclearSignals=signals;
            MarkDirtyRepaint();
        }
        private IReadOnlyList<PlayableTeamSignal> teamSignals=Array.Empty<PlayableTeamSignal>();private double signalTick;
        public int TeamSignalCount=>teamSignals.Count;
        public void SetTeamSignals(IReadOnlyList<PlayableTeamSignal> signals,double tick){teamSignals=signals??Array.Empty<PlayableTeamSignal>();signalTick=tick;MarkDirtyRepaint();}
        // Stable painter order: supporting structures cannot cover a strategic anchor;
        // units remain the topmost map records. Positions and records are unchanged.
        private static IReadOnlyList<PlayableMapMark> LayeredMarks(IReadOnlyList<PlayableMapMark> source)=>source
            .OrderBy(m=>m.State==MapMarkState.Unit||m.State==MapMarkState.OrbitalMine?2:IsLandmark(m.Kind)?1:0).ToArray();
        private static bool IsLandmark(PlayableBuildingKind kind)=>kind==PlayableBuildingKind.Headquarters||kind==PlayableBuildingKind.Outpost||kind==PlayableBuildingKind.Mine||kind==PlayableBuildingKind.RelayTower;
        public Func<PlayableOwner?,Color> OwnerPaint;
        private Color Owner(PlayableOwner? owner)=>!owner.HasValue?new Color(.66f,.71f,.75f):OwnerPaint!=null?OwnerPaint(owner):owner==PlayableOwner.Player?new Color(.19f,.72f,.77f):new Color(.93f,.34f,.23f);
        private void Draw(MeshGenerationContext ctx)
        {
            float w=contentRect.width,h=contentRect.height;if(w<=0||h<=0||marks==null)return;
            var mesh=ctx.Allocate(4,6,terrain);
            void TerrainVertex(double x,double z,Vector2 uv)
            {
                var point=Point(new NavPoint(x,z));
                mesh.SetNextVertex(new Vertex{position=new Vector3(point.x,point.y,Vertex.nearZ),tint=Color.white,uv=uv});
            }
            TerrainVertex(-transform.HalfWidth,-transform.HalfDepth,new Vector2(0,0));
            TerrainVertex(-transform.HalfWidth,transform.HalfDepth,new Vector2(0,1));
            TerrainVertex(transform.HalfWidth,transform.HalfDepth,new Vector2(1,1));
            TerrainVertex(transform.HalfWidth,-transform.HalfDepth,new Vector2(1,0));
            mesh.SetNextIndex(0);mesh.SetNextIndex(1);mesh.SetNextIndex(2);mesh.SetNextIndex(2);mesh.SetNextIndex(3);mesh.SetNextIndex(0);
            var painter=ctx.painter2D;
            if(profile.AuthoredMap is ProvingGroundMap){
                painter.BeginPath();painter.strokeColor=new Color(.97f,.94f,.82f);painter.lineWidth=(float)profile.MinimapCameraStroke;
                painter.MoveTo(Point(new NavPoint(0,-profile.ArenaHalfExtent)));painter.LineTo(Point(new NavPoint(0,profile.ArenaHalfExtent)));painter.Stroke();
            }
            foreach(var m in marks)
            {
                Vector2 p=Point(m.Position);bool unit=m.State==MapMarkState.Unit;
                float headquartersRadius=Mathf.Clamp((float)profile.MinimapLandmarkSize*w/720f,5.8f,10f);
                // Fixed shape hierarchy: even the square's diagonal must fit inside the HQ hexagon.
                float radius=unit?Mathf.Clamp((float)(profile.MinimapUnitMarkerSize*w/(2*transform.HalfWidth)),1.25f,2f)*1.1f:
                    m.State==MapMarkState.OrbitalMine?headquartersRadius*.42f:
                    m.Kind==PlayableBuildingKind.Headquarters?headquartersRadius:
                    m.Kind==PlayableBuildingKind.Outpost?headquartersRadius*.62f:
                    (m.Kind==PlayableBuildingKind.Mine||m.Kind==PlayableBuildingKind.RelayTower)?headquartersRadius*.54f:
                    Mathf.Clamp(headquartersRadius*.38f*(float)profile.MinimapMarkerSize,1.8f,headquartersRadius*.48f);
                // Owner/neutral palette is an authored symbol invariant, not a simulation value.
                // Fog dims the last-observed paint without changing its owner hue or fill.
                Color color=Owner(m.Owner);if(m.State==MapMarkState.Memory){color*= (float)profile.FogMemoryBrightness;color.a=1;}
                painter.strokeColor=unit?color:selected.Contains(m.Id)?Color.white:new Color(.13f,.19f,.19f);
                painter.fillColor=color;painter.lineWidth=unit?(.65f*1.1f):.8f;
                painter.BeginPath();
                if(m.State==MapMarkState.OrbitalMine){
                    painter.MoveTo(p+Vector2.up*radius);
                    painter.LineTo(p+Vector2.right*radius);
                    painter.LineTo(p+Vector2.down*radius);
                    painter.LineTo(p+Vector2.left*radius);
                }
                else if(unit||m.Kind==PlayableBuildingKind.Mine)painter.Arc(p,radius,0,360);
                else if(m.Kind==PlayableBuildingKind.Headquarters){
                    for(int i=0;i<6;i++){
                        float angle=(i/3f-.5f)*Mathf.PI;
                        var corner=p+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
                        if(i==0)painter.MoveTo(corner);else painter.LineTo(corner);
                    }
                }
                else if(m.Kind==PlayableBuildingKind.RelayTower){painter.MoveTo(p+new Vector2(0,-radius));painter.LineTo(p+new Vector2(radius*.8f,radius));painter.LineTo(p+new Vector2(-radius*.8f,radius));}
                else if(m.Kind==PlayableBuildingKind.Outpost){painter.MoveTo(p+new Vector2(-radius,-radius));painter.LineTo(p+new Vector2(radius,-radius));painter.LineTo(p+new Vector2(radius,radius));painter.LineTo(p+new Vector2(-radius,radius));}
                else{painter.MoveTo(p+new Vector2(-radius,-radius));painter.LineTo(p+new Vector2(radius,-radius));painter.LineTo(p+new Vector2(radius,radius));painter.LineTo(p+new Vector2(-radius,radius));}
                painter.ClosePath();
                if(!unit)
                {
                    // Stroke first, then fill: the configured outline separates nearby
                    // silhouettes without consuming their small colored interiors.
                    painter.strokeColor=new Color(.06f,.10f,.13f);
                    painter.lineWidth=Mathf.Min((float)profile.MinimapLandmarkOutline,radius)*2;
                    painter.Stroke();
                }
                painter.Fill();
                if(unit||selected.Contains(m.Id))
                {
                    painter.strokeColor=unit?color:Color.white;
                    painter.lineWidth=unit?(.65f*1.1f):(float)profile.MinimapLandmarkOutline;
                    painter.Stroke();
                }
                painter.strokeColor=new Color(.06f,.10f,.13f);painter.lineWidth=.8f;
                if(m.State==MapMarkState.Construction){painter.BeginPath();painter.MoveTo(p+new Vector2(-radius,radius));painter.LineTo(p+new Vector2(radius,-radius));painter.Stroke();}
            }
            foreach(var building in rallyBuildings)
            {
                var at=Point(building.Rally);float size=Mathf.Clamp((float)profile.MinimapLandmarkSize*w/720f,6,12);
                painter.strokeColor=Owner(building.Owner);painter.fillColor=Owner(building.Owner);painter.lineWidth=1.5f;
                painter.BeginPath();painter.MoveTo(at);painter.LineTo(at+new Vector2(0,-size*2));painter.Stroke();
                painter.BeginPath();painter.MoveTo(at+new Vector2(0,-size*2));painter.LineTo(at+new Vector2(size,-size*1.5f));painter.LineTo(at+new Vector2(0,-size));painter.ClosePath();painter.Fill();
            }
            if(campaignGoals!=null)foreach(var goal in campaignGoals.Where(g=>!g.Complete)){var p=Point(goal.Point);painter.strokeColor=Color.cyan;painter.fillColor=Color.cyan;painter.lineWidth=2;painter.BeginPath();painter.MoveTo(p);painter.LineTo(p+new Vector2(0,-14));painter.LineTo(p+new Vector2(9,-10));painter.LineTo(p+new Vector2(0,-6));painter.Stroke();}
            float scanWidth=scanLineWidth;
            PlayableBootstrap.DrawScanFaces(ctx,scanTriangles,Point);
            if(scanTriangleLayers!=null)foreach(var layer in scanTriangleLayers)PlayableBootstrap.DrawScanFaces(ctx,layer,Point);
            foreach(var segment in scanLines)PlayableBootstrap.DrawScanLine(painter,Point(segment.From),Point(segment.To),(float)segment.Opacity,scanWidth);
            if(scanLineLayers!=null)foreach(var layer in scanLineLayers)foreach(var segment in layer)PlayableBootstrap.DrawScanLine(painter,Point(segment.From),Point(segment.To),(float)segment.Opacity,scanWidth);
            if(reinforcementPreview!=null)PlayableBootstrap.DrawReinforcementSilhouettes(painter,reinforcementPreview,reinforcementKind,reinforcementFootprint,reinforcementValid,Point,minScreenRadius:3);
            if(scanPreview.HasValue&&nuclearPreview)
                NuclearTargetArtwork.Draw(ctx,angle=>Point(new NavPoint(scanPreview.Value.X+Math.Cos(angle)*scanRadius,scanPreview.Value.Z+Math.Sin(angle)*scanRadius)),Point(scanPreview.Value),!scanPreviewBlocked,w<NuclearTargetArtwork.Style.mapLabelMinPixels,nuclearPreviewReason);
            if(scanPreview.HasValue&&!nuclearPreview)for(int i=0;i<128;i++){
                NavPoint At(int step)=>new NavPoint(scanPreview.Value.X+Math.Cos(step*Math.PI/64)*scanRadius,scanPreview.Value.Z+Math.Sin(step*Math.PI/64)*scanRadius);
                PlayableBootstrap.DrawScanLine(painter,Point(At(i)),Point(At(i+1)),1,scanWidth,scanPreviewBlocked?PlayableBootstrap.InvalidOrbitalPreview:OrbitalTheme.Cyan);
            }
            foreach(var signal in nuclearSignals)
                if(signal.Center.HasValue)PlayableBootstrap.DrawNuclearGlyph(painter,Point(signal.Center.Value.Position),
                    (float)profile.TeamSignalMapPixels*.55f);
            foreach(var signal in teamSignals)TeamSignalArtwork.Mark(painter,Point(signal.Point),signal.Kind,(float)profile.TeamSignalMapPixels,(float)signal.Alpha(signalTick,profile),true);
            if(footprint!=null){painter.BeginPath();painter.strokeColor=Color.white;painter.lineWidth=(float)profile.MinimapCameraStroke;for(int i=0;i<footprint.Length;i++){if(i==0)painter.MoveTo(new Vector2(Mathf.Clamp(Point(footprint[i]).x,0,w),Mathf.Clamp(Point(footprint[i]).y,0,h)));else painter.LineTo(new Vector2(Mathf.Clamp(Point(footprint[i]).x,0,w),Mathf.Clamp(Point(footprint[i]).y,0,h)));}painter.ClosePath();painter.Stroke();}
        }
    }
}
