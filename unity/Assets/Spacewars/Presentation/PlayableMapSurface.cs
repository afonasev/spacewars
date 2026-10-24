using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // One bounded derived terrain texture shared by both maps, sourced from the same team mask.
    public sealed class PlayableMapTerrain : IDisposable
    {
        public Texture2D Texture{get;}
        public long Uploads{get;private set;}
        private long fogUpload=-1;private long[] unionUploads;
        private bool publicTerrainShown;
        private readonly Color[] terrain;
        private readonly Color32[] pixels;
        private readonly byte[] paintedOpacity;
        private bool paintedValid,unionPaint;
        public long PaintedPixels{get;private set;}
        private readonly Color tint;
        public PlayableMapTerrain(PlayableProfile p,bool staged=false)
        {
            int size=PlayableVision.RasterResolution;terrain=new Color[size*size];pixels=new Color32[terrain.Length];paintedOpacity=new byte[terrain.Length];
            Texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name="Shared map terrain",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            tint=new Color((float)p.FogTintR,(float)p.FogTintG,(float)p.FogTintB);
            Preparation=Prepare(p,size);
            if(!staged)LoadingPreparation.Drain(Preparation);
        }
        public System.Collections.IEnumerator Preparation{get;}
        private System.Collections.IEnumerator Prepare(PlayableProfile p,int size)
        {
            var budget=new LoadingFrameBudget();
            // Reuse the world's basalt palette; profile controls all added contrast tuning.
            Color ground=new Color(.20f,.25f,.25f);Color.RGBToHSV(ground,out float h,out float s,out float v);
            ground=Color.HSVToRGB(h,s*(float)p.MinimapTerrainSaturation,v*(float)p.MinimapTerrainBrightness);
            var walls=PlayableMap.SolidObstacles(p);
            var meridianArt=p.AuthoredMap is WhiteMeridianMap?WhiteMeridianArtProfile.Load():null;
            var mossArt=p.AuthoredMap is MossHighlandMap?MossHighlandArtProfile.Load():null;
            var basaltArt=p.AuthoredMap is BasaltCrownMap?BasaltCrownArtProfile.Load():null;
            var basaltMines=p.AuthoredMap is BasaltCrownMap bm?BasaltCrownDetails.Mines(bm,p):null;
            var sitePads=p.AuthoredMap is BasaltCrownMap padMap?BasaltCrownDetails.SitePads(padMap,p,basaltArt):Array.Empty<BasaltCrownDetails.SitePad>();
            var heights=p.AuthoredMap==null?null:new float[terrain.Length];
            var groveArt=p.AuthoredMap is CrimsonGroveMap?CrimsonGroveArtProfile.Load():null;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                if(x==0&&budget.Expired){yield return null;budget.Restart();}
                double wx=((x+.5)/size*2-1)*p.ArenaHalfExtent,wz=((y+.5)/size*2-1)*p.ArenaHalfExtent;
                var terrainPoint=new NavPoint(wx,wz);
                var mapSupport=heights==null?null:p.AuthoredMap.SupportAt(terrainPoint);
                if(heights!=null)heights[y*size+x]=mapSupport==null?float.NaN:(float)mapSupport.HeightAt(terrainPoint);
                bool obstacle=false;foreach(var wall in walls)if(wall.Contains(terrainPoint)){obstacle=true;break;}
                bool edge=false;
                if(obstacle)foreach(var wall in walls){double band=p.MinimapTerrainStroke*(p.ArenaHalfExtent*2/size);if(wall.Contains(terrainPoint)&&wall.BoundaryDistanceSquared(terrainPoint)<band*band)edge=true;}
                if(p.AuthoredMap is FoundryMap foundry){bool road=System.Linq.Enumerable.Any(foundry.Roads,r=>r.Contains(terrainPoint));terrain[y*size+x]=System.Linq.Enumerable.Any(foundry.Lava,l=>l.Contains(terrainPoint))?PlayableWorld.FoundryLava:obstacle?PlayableWorld.FoundryRock:road?PlayableWorld.FoundryRoad:PlayableWorld.FoundryGround;}
                else if(p.AuthoredMap is CrimsonGroveMap){var art=groveArt;terrain[y*size+x]=mapSupport==null?Color.clear:obstacle?art.Leaves:art.Ground;}
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
                    if(!obstacle)color=Color.Lerp(color,basaltArt.Palette(2),BasaltCrownRoads.Coverage((float)wx,(float)wz,basaltArt));
                    if(!obstacle&&sitePads.Any(pad=>BasaltCrownDetails.PadDistance(q,pad,basaltArt.siteLinkWidth)<-basaltArt.siteApronFeather))color=Color.HSVToRGB(basaltArt.warmHue,basaltArt.warmSaturation*basaltArt.pavingSaturation,basaltArt.siteApronValue);
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
                float step=(float)(p.ArenaHalfExtent*2/size)*.5f;
                for(int y=1;y<size-1;y++)for(int x=1;x<size-1;x++){
                    int i=y*size+x;float elevation=heights[i];if(float.IsNaN(elevation)||terrain[i].a<=0)continue;
                    if(elevation-heights[i-1]>step||elevation-heights[i+1]>step||elevation-heights[i-size]>step||elevation-heights[i+size]>step)
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
            if(unionPaint&&unionUploads!=null&&unionUploads.Length==masks.Count){bool changed=false;for(int i=0;i<masks.Count;i++)changed|=unionUploads[i]!=masks[i].Uploads;if(!changed)return false;}
            unionPaint=true;unionUploads=new long[masks.Count];for(int i=0;i<masks.Count;i++)unionUploads[i]=masks[i].Uploads;
            for(int i=0;i<pixels.Length;i++){
                byte opacity=masks[0].OpacityAt(i);
                for(int n=1;n<masks.Count;n++)opacity=Math.Min(opacity,masks[n].OpacityAt(i));
                PaintFogPixel(i,opacity);
            }
            paintedValid=true;
            Texture.SetPixels32(pixels);Texture.Apply(false,false);Uploads++;return true;
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
        private readonly PlayableOrderMarkerLayer orderMarkers;
        public void SetOrderMarkers(IReadOnlyList<PlayableQueueMarker> markers)=>orderMarkers.Set(markers);
        private Texture2D terrain;
        private IReadOnlyList<PlayableMapMark> marks;
        private HashSet<int> selected;
        private NavPoint[] footprint;
        private PlayableBuildingSnapshot[] rallyBuildings=Array.Empty<PlayableBuildingSnapshot>();
        public int RallyFlagCount=>rallyBuildings.Length;
        private PlayableMapTransform transform;
        public PlayableMapSurface(PlayableProfile profile,Texture2D terrain)
        {
            this.profile=profile;this.terrain=terrain;pickingMode=PickingMode.Ignore;style.overflow=Overflow.Hidden;
            generateVisualContent+=Draw;orderMarkers=new PlayableOrderMarkerLayer(location=>Point(location.Position),true);Add(orderMarkers);
        }
        public void Rebind(PlayableProfile next,Texture2D texture){profile=next;terrain=texture;marks=null;campaignGoals=null;footprint=null;rallyBuildings=Array.Empty<PlayableBuildingSnapshot>();MarkDirtyRepaint();}
        public void Set(PlayableSnapshot view,HashSet<int> selection,NavPoint[] camera)
        {
            transform=new PlayableMapTransform(view.Vision.HalfWidth,view.Vision.HalfDepth);marks=PlayableMapView.Marks(view);selected=selection;footprint=camera;
            rallyBuildings=view.Buildings.Where(b=>selection.Contains(b.Id)&&b.Owner==view.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.HasRally==true&&b.PrivateState.Lifecycle?.Selling!=true).ToArray();MarkDirtyRepaint();
        }
        public void SetPublic(IReadOnlyList<PlayableMapMark> publicMarks,double extent)
        {transform=new PlayableMapTransform(extent,extent);marks=publicMarks;selected=new HashSet<int>();footprint=null;rallyBuildings=Array.Empty<PlayableBuildingSnapshot>();MarkDirtyRepaint();}
        public NavPoint Ground(Vector2 local)=>transform.Ground(new NavPoint(local.x/contentRect.width,local.y/contentRect.height));
        private Vector2 Point(NavPoint world){var uv=transform.Project(world);return new Vector2((float)uv.X*contentRect.width,(float)uv.Z*contentRect.height);}
        private System.Collections.Generic.IReadOnlyList<CampaignObjective> campaignGoals;
        public void SetCampaignGoals(System.Collections.Generic.IReadOnlyList<CampaignObjective> goals){campaignGoals=goals;MarkDirtyRepaint();}
        public Func<PlayableOwner?,Color> OwnerPaint;
        private Color Owner(PlayableOwner? owner)=>!owner.HasValue?new Color(.32f,.37f,.36f):OwnerPaint!=null?OwnerPaint(owner):owner==PlayableOwner.Player?new Color(.19f,.72f,.77f):new Color(.93f,.34f,.23f);
        private void Draw(MeshGenerationContext ctx)
        {
            float w=contentRect.width,h=contentRect.height;if(w<=0||h<=0||marks==null)return;
            var mesh=ctx.Allocate(4,6,terrain);
            mesh.SetNextVertex(new Vertex{position=new Vector3(0,h,Vertex.nearZ),tint=Color.white,uv=new Vector2(0,0)});
            mesh.SetNextVertex(new Vertex{position=new Vector3(0,0,Vertex.nearZ),tint=Color.white,uv=new Vector2(0,1)});
            mesh.SetNextVertex(new Vertex{position=new Vector3(w,0,Vertex.nearZ),tint=Color.white,uv=new Vector2(1,1)});
            mesh.SetNextVertex(new Vertex{position=new Vector3(w,h,Vertex.nearZ),tint=Color.white,uv=new Vector2(1,0)});
            mesh.SetNextIndex(0);mesh.SetNextIndex(1);mesh.SetNextIndex(2);mesh.SetNextIndex(2);mesh.SetNextIndex(3);mesh.SetNextIndex(0);
            var painter=ctx.painter2D;
            foreach(var m in marks)
            {
                Vector2 p=Point(m.Position);bool unit=m.State==MapMarkState.Unit;
                float headquartersRadius=Mathf.Clamp((float)profile.MinimapLandmarkSize*w/720f,5.8f,10f);
                // Fixed shape hierarchy: even the square's diagonal must fit inside the HQ hexagon.
                float radius=unit?Mathf.Clamp((float)(profile.MinimapUnitMarkerSize*w/(2*transform.HalfWidth)),1.25f,2f):
                    m.Kind==PlayableBuildingKind.Headquarters?headquartersRadius:
                    m.Kind==PlayableBuildingKind.Outpost?headquartersRadius*.62f:
                    m.Kind==PlayableBuildingKind.Mine?headquartersRadius*.54f:
                    Mathf.Clamp(headquartersRadius*.38f*(float)profile.MinimapMarkerSize,1.8f,headquartersRadius*.48f);
                Color color=Owner(m.Owner);if(m.State==MapMarkState.Memory)color=Color.Lerp(color,Color.gray,(float)profile.FogMemoryDesaturation)*(float)profile.FogMemoryBrightness;
                painter.strokeColor=unit?color:selected.Contains(m.Id)?Color.white:new Color(.13f,.19f,.19f);
                painter.fillColor=color;painter.lineWidth=unit?.65f:.8f;
                painter.BeginPath();
                if(unit||m.Kind==PlayableBuildingKind.Mine)painter.Arc(p,radius,0,360);
                else if(m.Kind==PlayableBuildingKind.Headquarters){
                    for(int i=0;i<6;i++){
                        float angle=(i/3f-.5f)*Mathf.PI;
                        var corner=p+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
                        if(i==0)painter.MoveTo(corner);else painter.LineTo(corner);
                    }
                }
                else if(m.Kind==PlayableBuildingKind.Outpost){painter.MoveTo(p+new Vector2(-radius,-radius));painter.LineTo(p+new Vector2(radius,-radius));painter.LineTo(p+new Vector2(radius,radius));painter.LineTo(p+new Vector2(-radius,radius));}
                else{painter.MoveTo(p+new Vector2(-radius,-radius));painter.LineTo(p+new Vector2(radius,-radius));painter.LineTo(p+new Vector2(radius,radius));painter.LineTo(p+new Vector2(-radius,radius));}
                painter.ClosePath();
                painter.Fill();
                if(!unit){painter.strokeColor=new Color(.97f,.94f,.82f);painter.lineWidth=Mathf.Min((float)profile.MinimapLandmarkOutline*.55f,radius*.35f);painter.Stroke();painter.strokeColor=selected.Contains(m.Id)?Color.white:new Color(.13f,.19f,.19f);painter.lineWidth=.8f;}
                painter.Stroke();
                if(m.State==MapMarkState.Construction){painter.BeginPath();painter.MoveTo(p+new Vector2(-radius,radius));painter.LineTo(p+new Vector2(radius,-radius));painter.Stroke();}
                if(m.Start>0)ctx.DrawText(m.Start.ToString(),p-new Vector2(radius/2,radius),radius*1.6f,new Color(.08f,.13f,.14f),null);
            }
            foreach(var building in rallyBuildings)
            {
                var at=Point(building.Rally);float size=Mathf.Clamp((float)profile.MinimapLandmarkSize*w/720f,6,12);
                painter.strokeColor=Owner(building.Owner);painter.fillColor=Owner(building.Owner);painter.lineWidth=1.5f;
                painter.BeginPath();painter.MoveTo(at);painter.LineTo(at+new Vector2(0,-size*2));painter.Stroke();
                painter.BeginPath();painter.MoveTo(at+new Vector2(0,-size*2));painter.LineTo(at+new Vector2(size,-size*1.5f));painter.LineTo(at+new Vector2(0,-size));painter.ClosePath();painter.Fill();
            }
            if(campaignGoals!=null)foreach(var goal in campaignGoals.Where(g=>!g.Complete)){var p=Point(goal.Point);painter.strokeColor=Color.cyan;painter.fillColor=Color.cyan;painter.lineWidth=2;painter.BeginPath();painter.MoveTo(p);painter.LineTo(p+new Vector2(0,-14));painter.LineTo(p+new Vector2(9,-10));painter.LineTo(p+new Vector2(0,-6));painter.Stroke();}
            if(footprint!=null){painter.BeginPath();painter.strokeColor=Color.white;painter.lineWidth=(float)profile.MinimapCameraStroke;for(int i=0;i<footprint.Length;i++){if(i==0)painter.MoveTo(new Vector2(Mathf.Clamp(Point(footprint[i]).x,0,w),Mathf.Clamp(Point(footprint[i]).y,0,h)));else painter.LineTo(new Vector2(Mathf.Clamp(Point(footprint[i]).x,0,w),Mathf.Clamp(Point(footprint[i]).y,0,h)));}painter.ClosePath();painter.Stroke();}
        }
    }
}
