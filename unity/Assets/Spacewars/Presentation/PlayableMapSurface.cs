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
        public PlayableMapTerrain(PlayableProfile p)
        {
            int size=PlayableVision.RasterResolution;terrain=new Color[size*size];pixels=new Color32[terrain.Length];paintedOpacity=new byte[terrain.Length];
            Texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name="Shared map terrain",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            tint=new Color((float)p.FogTintR,(float)p.FogTintG,(float)p.FogTintB);
            // Reuse the world's basalt palette; profile controls all added contrast tuning.
            Color ground=new Color(.20f,.25f,.25f);Color.RGBToHSV(ground,out float h,out float s,out float v);
            ground=Color.HSVToRGB(h,s*(float)p.MinimapTerrainSaturation,v*(float)p.MinimapTerrainBrightness);
            var walls=PlayableMap.SolidObstacles(p);
            var mossArt=p.AuthoredMap is MossHighlandMap?MossHighlandArtProfile.Load():null;
            var basaltArt=p.AuthoredMap is BasaltCrownMap?BasaltCrownArtProfile.Load():null;
            var basaltMines=p.AuthoredMap is BasaltCrownMap bm?BasaltCrownDetails.Mines(bm,p):null;
            var sitePads=p.AuthoredMap is BasaltCrownMap padMap?BasaltCrownDetails.SitePads(padMap,p,basaltArt):Array.Empty<BasaltCrownDetails.SitePad>();
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                double wx=((x+.5)/size*2-1)*p.ArenaHalfExtent,wz=((y+.5)/size*2-1)*p.ArenaHalfExtent;
                bool obstacle=false;foreach(var wall in walls)if(wall.Contains(new NavPoint(wx,wz))){obstacle=true;break;}
                bool edge=false;
                if(obstacle)foreach(var wall in walls){double band=p.MinimapTerrainStroke*(p.ArenaHalfExtent*2/size);if(wall.Contains(new NavPoint(wx,wz))&&wall.BoundaryDistanceSquared(new NavPoint(wx,wz))<band*band)edge=true;}
                if(p.AuthoredMap is FoundryMap foundry){var point=new NavPoint(wx,wz);bool road=System.Linq.Enumerable.Any(foundry.Roads,r=>r.Contains(point));terrain[y*size+x]=System.Linq.Enumerable.Any(foundry.Lava,l=>l.Contains(point))?PlayableWorld.FoundryLava:obstacle?PlayableWorld.FoundryRock:road?PlayableWorld.FoundryRoad:PlayableWorld.FoundryGround;}
                else if(p.AuthoredMap is MossHighlandMap moss){
                    var q=new NavPoint(wx,wz);var patch=moss.Materials.FirstOrDefault(m=>m.Bounds.Contains(q));var color=mossArt.Palette(patch==null?4:MossHighlandArtProfile.Role(patch.Material));
                    Color.RGBToHSV(color,out float mh,out float ms,out float mv);color=Color.HSVToRGB(mh,ms*(float)p.MinimapTerrainSaturation,mv*(float)p.MinimapTerrainBrightness);
                    terrain[y*size+x]=obstacle?color*(float)(1-p.MinimapTerrainTierContrast):color;
                }
                else if(p.AuthoredMap is BasaltCrownMap basalt){
                    var q=new NavPoint(wx,wz);var patch=basalt.Materials.FirstOrDefault(m=>m.Bounds.Contains(q));
                    var color=basaltArt.Palette(patch==null?0:BasaltCrownArtProfile.Role(patch.Material));
                    if(!obstacle&&basaltMines.Any(site=>Math.Max(Math.Max(Math.Abs(wx-site.Position.X),Math.Abs(wz-site.Position.Z)),(Math.Abs(wx-site.Position.X)+Math.Abs(wz-site.Position.Z))*.70710678)<basaltArt.mineApronRadius-basaltArt.mineApronFeather))color=Color.HSVToRGB(basaltArt.warmHue,basaltArt.warmSaturation*basaltArt.pavingSaturation,basaltArt.mineApronValue);
                    if(!obstacle)color=Color.Lerp(color,basaltArt.Palette(2),BasaltCrownRoads.Coverage((float)wx,(float)wz,basaltArt));
                    if(!obstacle&&sitePads.Any(pad=>BasaltCrownDetails.PadDistance(q,pad,basaltArt.siteLinkWidth)<-basaltArt.siteApronFeather))color=Color.HSVToRGB(basaltArt.warmHue,basaltArt.warmSaturation*basaltArt.pavingSaturation,basaltArt.siteApronValue);
                    Color.RGBToHSV(color,out float bh,out float bs,out float bv);
                    color=Color.HSVToRGB(bh,bs*(float)p.MinimapTerrainSaturation,bv*(float)p.MinimapTerrainBrightness);
                    terrain[y*size+x]=basalt.SupportAt(q)==null?Color.clear:obstacle?color*(float)(1-p.MinimapTerrainTierContrast):color;
                }
                else if(p.AuthoredMap!=null){var support=p.AuthoredMap.SupportAt(new NavPoint(wx,wz));terrain[y*size+x]=obstacle?PlayableWorld.RockColor:support==null?PlayableWorld.WaterColor:support.IsBridge||support.Id.StartsWith("platform-")?PlayableWorld.BridgeColor:PlayableWorld.BankColor;}
                else terrain[y*size+x]=obstacle?ground+Color.white*(float)(edge?-p.MinimapTerrainTierContrast:p.MinimapTerrainTierContrast):ground;
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
        private Color MapFogColor(Color ground,float fog)=>Color.Lerp(ground*1.3f,tint,fog*.72f);
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
        public void Rebind(PlayableProfile next,Texture2D texture){profile=next;terrain=texture;marks=null;footprint=null;rallyBuildings=Array.Empty<PlayableBuildingSnapshot>();MarkDirtyRepaint();}
        public void Set(PlayableSnapshot view,HashSet<int> selection,NavPoint[] camera)
        {
            transform=new PlayableMapTransform(view.Vision.HalfWidth,view.Vision.HalfDepth);marks=PlayableMapView.Marks(view);selected=selection;footprint=camera;
            rallyBuildings=view.Buildings.Where(b=>selection.Contains(b.Id)&&b.Owner==view.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.HasRally==true&&b.PrivateState.Lifecycle?.Selling!=true).ToArray();MarkDirtyRepaint();
        }
        public void SetPublic(IReadOnlyList<PlayableMapMark> publicMarks,double extent)
        {transform=new PlayableMapTransform(extent,extent);marks=publicMarks;selected=new HashSet<int>();footprint=null;rallyBuildings=Array.Empty<PlayableBuildingSnapshot>();MarkDirtyRepaint();}
        public NavPoint Ground(Vector2 local)=>transform.Ground(new NavPoint(local.x/contentRect.width,local.y/contentRect.height));
        private Vector2 Point(NavPoint world){var uv=transform.Project(world);return new Vector2((float)uv.X*contentRect.width,(float)uv.Z*contentRect.height);}
        public Func<PlayableOwner?,Color> OwnerPaint;
        private Color Owner(PlayableOwner? owner)=>OwnerPaint!=null?OwnerPaint(owner):owner==PlayableOwner.Player?new Color(.19f,.72f,.77f):owner==PlayableOwner.Enemy?new Color(.93f,.34f,.23f):Color.white;
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
                float radius=unit?Mathf.Clamp((float)(profile.MinimapUnitMarkerSize*w/(2*transform.HalfWidth)),1.25f,2f):Mathf.Clamp((float)profile.MinimapLandmarkSize*w/720f,2f,5f);
                if(m.Kind==PlayableBuildingKind.Factory||m.Kind==PlayableBuildingKind.Refinery||m.Kind==PlayableBuildingKind.ScientificCenter)radius*=(float)profile.MinimapMarkerSize;
                Color color=Owner(m.Owner);if(m.State==MapMarkState.Memory)color=Color.Lerp(color,Color.gray,(float)profile.FogMemoryDesaturation)*(float)profile.FogMemoryBrightness;
                painter.strokeColor=selected.Contains(m.Id)?Color.white:color;
                painter.fillColor=color;painter.lineWidth=unit?.65f:1f;
                painter.BeginPath();
                if(unit||m.Kind==PlayableBuildingKind.Headquarters)painter.Arc(p,radius,0,360);
                else if(m.Kind==PlayableBuildingKind.Outpost){painter.MoveTo(p+new Vector2(0,-radius));painter.LineTo(p+new Vector2(radius,radius));painter.LineTo(p+new Vector2(-radius,radius));}
                else if(m.Kind==PlayableBuildingKind.Mine){painter.MoveTo(p+new Vector2(0,-radius));painter.LineTo(p+new Vector2(radius,0));painter.LineTo(p+new Vector2(0,radius));painter.LineTo(p+new Vector2(-radius,0));}
                else{painter.MoveTo(p+new Vector2(-radius,-radius));painter.LineTo(p+new Vector2(radius,-radius));painter.LineTo(p+new Vector2(radius,radius));painter.LineTo(p+new Vector2(-radius,radius));}
                painter.ClosePath();
                if(unit||m.State==MapMarkState.Ready)painter.Fill();painter.Stroke();
                if(m.State==MapMarkState.Construction){painter.BeginPath();painter.MoveTo(p+new Vector2(-radius,radius));painter.LineTo(p+new Vector2(radius,-radius));painter.Stroke();}
                if(m.Kind==PlayableBuildingKind.Mine&&!unit){painter.BeginPath();painter.MoveTo(p+new Vector2(-radius,0));painter.LineTo(p+new Vector2(radius,0));painter.MoveTo(p+new Vector2(0,-radius));painter.LineTo(p+new Vector2(0,radius));painter.Stroke();}
                if(m.Start>0)ctx.DrawText(m.Start.ToString(),p-new Vector2(radius/2,radius),radius*1.6f,Color.white,null);
            }
            foreach(var building in rallyBuildings)
            {
                var at=Point(building.Rally);float size=Mathf.Clamp((float)profile.MinimapLandmarkSize*w/720f,6,12);
                painter.strokeColor=Owner(building.Owner);painter.fillColor=Owner(building.Owner);painter.lineWidth=1.5f;
                painter.BeginPath();painter.MoveTo(at);painter.LineTo(at+new Vector2(0,-size*2));painter.Stroke();
                painter.BeginPath();painter.MoveTo(at+new Vector2(0,-size*2));painter.LineTo(at+new Vector2(size,-size*1.5f));painter.LineTo(at+new Vector2(0,-size));painter.ClosePath();painter.Fill();
            }
            if(footprint!=null){painter.BeginPath();painter.strokeColor=Color.white;painter.lineWidth=(float)profile.MinimapCameraStroke;for(int i=0;i<footprint.Length;i++){if(i==0)painter.MoveTo(new Vector2(Mathf.Clamp(Point(footprint[i]).x,0,w),Mathf.Clamp(Point(footprint[i]).y,0,h)));else painter.LineTo(new Vector2(Mathf.Clamp(Point(footprint[i]).x,0,w),Mathf.Clamp(Point(footprint[i]).y,0,h)));}painter.ClosePath();painter.Stroke();}
        }
    }
}
