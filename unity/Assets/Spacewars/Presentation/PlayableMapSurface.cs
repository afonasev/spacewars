using System;
using System.Collections.Generic;
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
        private readonly Color tint;
        public PlayableMapTerrain(PlayableProfile p)
        {
            int size=PlayableVision.RasterResolution;terrain=new Color[size*size];pixels=new Color32[terrain.Length];
            Texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name="Shared map terrain",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            tint=new Color((float)p.FogTintR,(float)p.FogTintG,(float)p.FogTintB);
            // Reuse the world's basalt palette; profile controls all added contrast tuning.
            Color ground=new Color(.20f,.25f,.25f);Color.RGBToHSV(ground,out float h,out float s,out float v);
            ground=Color.HSVToRGB(h,s*(float)p.MinimapTerrainSaturation,v*(float)p.MinimapTerrainBrightness);
            var walls=PlayableMap.SolidObstacles(p);
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                double wx=((x+.5)/size*2-1)*p.ArenaHalfExtent,wz=((y+.5)/size*2-1)*p.ArenaHalfExtent;
                bool obstacle=false;foreach(var wall in walls)if(wall.Contains(new NavPoint(wx,wz))){obstacle=true;break;}
                bool edge=false;
                if(obstacle)foreach(var wall in walls){double band=p.MinimapTerrainStroke*(p.ArenaHalfExtent*2/size);if(wall.Contains(new NavPoint(wx,wz))&&wall.BoundaryDistanceSquared(new NavPoint(wx,wz))<band*band)edge=true;}
                if(p.AuthoredMap is FoundryMap foundry){var point=new NavPoint(wx,wz);bool road=System.Linq.Enumerable.Any(foundry.Roads,r=>r.Contains(point));terrain[y*size+x]=System.Linq.Enumerable.Any(foundry.Lava,l=>l.Contains(point))?PlayableWorld.FoundryLava:obstacle?PlayableWorld.FoundryRock:road?PlayableWorld.FoundryRoad:PlayableWorld.FoundryGround;}
                else if(p.AuthoredMap!=null){var support=p.AuthoredMap.SupportAt(new NavPoint(wx,wz));terrain[y*size+x]=obstacle?PlayableWorld.RockColor:support==null?PlayableWorld.WaterColor:support.IsBridge||support.Id.StartsWith("platform-")?PlayableWorld.BridgeColor:PlayableWorld.BankColor;}
                else terrain[y*size+x]=obstacle?ground+Color.white*(float)(edge?-p.MinimapTerrainTierContrast:p.MinimapTerrainTierContrast):ground;
            }
        }
        public void ShowPublicTerrain()
        {
            if(publicTerrainShown)return;
            publicTerrainShown=true;
            Texture.SetPixels(terrain);Texture.Apply(false,false);Uploads++;
        }
        public bool UpdateUnion(IReadOnlyList<PlayableFogMask> masks)
        {
            if(unionUploads!=null&&unionUploads.Length==masks.Count){bool changed=false;for(int i=0;i<masks.Count;i++)changed|=unionUploads[i]!=masks[i].Uploads;if(!changed)return false;}
            unionUploads=new long[masks.Count];for(int i=0;i<masks.Count;i++)unionUploads[i]=masks[i].Uploads;
            var union=masks[0].Texture.GetPixels32();
            for(int n=1;n<masks.Count;n++){var next=masks[n].Texture.GetPixels32();for(int i=0;i<union.Length;i++)union[i].r=Math.Min(union[i].r,next[i].r);}
            for(int i=0;i<pixels.Length;i++)pixels[i]=MapFogColor(terrain[i],union[i].r/255f);
            Texture.SetPixels32(pixels);Texture.Apply(false,false);Uploads++;return true;
        }
        public bool Update(PlayableFogMask fog)
        {
            if(fogUpload==fog.Uploads)return false;fogUpload=fog.Uploads;
            // Texture readback is CPU resident (no GPU readback); stable frames do no work.
            var mask=fog.Texture.GetPixels32();
            for(int i=0;i<pixels.Length;i++)pixels[i]=MapFogColor(terrain[i],mask[i].r/255f);
            Texture.SetPixels32(pixels);Texture.Apply(false,false);Uploads++;return true;
        }
        // Fixed paint floor preserves public geography independently of hidden entities. Pixel-size bounds below
        // prevent dots disappearing or symbols covering compact terrain; these are UI packing invariants,
        // not changes to designer-owned vision radii, masks or marker eligibility.
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
        private PlayableMapTransform transform;
        public PlayableMapSurface(PlayableProfile profile,Texture2D terrain)
        {
            this.profile=profile;this.terrain=terrain;pickingMode=PickingMode.Ignore;style.overflow=Overflow.Hidden;
            generateVisualContent+=Draw;orderMarkers=new PlayableOrderMarkerLayer(location=>Point(location.Position),true);Add(orderMarkers);
        }
        public void Rebind(PlayableProfile next,Texture2D texture){profile=next;terrain=texture;marks=null;footprint=null;MarkDirtyRepaint();}
        public void Set(PlayableSnapshot view,HashSet<int> selection,NavPoint[] camera)
        {
            transform=new PlayableMapTransform(view.Vision.HalfWidth,view.Vision.HalfDepth);marks=PlayableMapView.Marks(view);selected=selection;footprint=camera;MarkDirtyRepaint();
        }
        public void SetPublic(IReadOnlyList<PlayableMapMark> publicMarks,double extent)
        {transform=new PlayableMapTransform(extent,extent);marks=publicMarks;selected=new HashSet<int>();footprint=null;MarkDirtyRepaint();}
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
            if(footprint!=null){painter.BeginPath();painter.strokeColor=Color.white;painter.lineWidth=(float)profile.MinimapCameraStroke;for(int i=0;i<footprint.Length;i++){if(i==0)painter.MoveTo(new Vector2(Mathf.Clamp(Point(footprint[i]).x,0,w),Mathf.Clamp(Point(footprint[i]).y,0,h)));else painter.LineTo(new Vector2(Mathf.Clamp(Point(footprint[i]).x,0,w),Mathf.Clamp(Point(footprint[i]).y,0,h)));}painter.ClosePath();painter.Stroke();}
        }
    }
}
