using System;
using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        readonly List<NavObstacle> trailHardAreas=new List<NavObstacle>();
        BasaltCrownDetails.SitePad[] trailBasaltPads;float trailPadLink;bool trailAreasInitialized;
        int trailLifetimeTicks=-1;
        PlayableGroundTrail trailLast;int trailCount=-1;long trailFog=-1,trailExpiry=long.MaxValue;
        GameObject trailObject;Mesh trailMesh;Material trailMaterial;long trailSequence=-1,trailGeneration=-1;
        GroundTrailProfile trailArt;public GroundTrailProfile TrailArt=>trailArt??(trailArt=GroundTrailProfile.Load());
        readonly List<Vector3> trailVertices=new List<Vector3>();readonly List<int> trailIndices=new List<int>();
        readonly List<Vector4> trailUv=new List<Vector4>(),trailData=new List<Vector4>();readonly List<Color> trailColors=new List<Color>();
        public int GroundTrailQuadCount=>trailIndices.Count/6;
        public void RenderGroundTrails(PlayableSnapshot view)
        {
            if(view==null)return;
            if(!NativeGraphicsSettings.GroundTrailsEnabled){if(trailObject)trailObject.SetActive(false);trailSequence=-1;trailCount=-1;return;}
            if(trailObject)trailObject.SetActive(true);
            int lifetimeTicks=(view.ActiveProfile??profile).GroundTrailLifetimeTicks;
            bool lifetimeChanged=trailLifetimeTicks!=lifetimeTicks;trailLifetimeTicks=lifetimeTicks;
            if(!lifetimeChanged&&trailSequence==view.Sequence&&trailGeneration==view.Generation)return;
            trailSequence=view.Sequence;
            if(trailMaterial)trailMaterial.SetFloat("_Now",view.Tick/30f);
            var last=view.GroundTrails.Count==0?null:view.GroundTrails[view.GroundTrails.Count-1];long fog=view.Vision?.FogRevision??-1;
            if(!lifetimeChanged&&trailGeneration==view.Generation&&trailCount==view.GroundTrails.Count&&ReferenceEquals(last,trailLast)&&trailFog==fog&&view.Tick<trailExpiry)return;
            trailGeneration=view.Generation;trailCount=view.GroundTrails.Count;trailLast=last;trailFog=fog;trailExpiry=view.GroundTrails.Count==0?long.MaxValue:view.GroundTrails[0].Tick+lifetimeTicks;
            if(!trailObject)
            {
                trailObject=new GameObject("Ground vehicle trails");trailObject.transform.SetParent(root,false);Layer(trailObject);
                trailMesh=new Mesh{name="Batched ground impressions",indexFormat=IndexFormat.UInt32};trailMesh.MarkDynamic();
                trailObject.AddComponent<MeshFilter>().sharedMesh=trailMesh;
                trailMaterial=new Material(Resources.Load<Shader>("GroundTrails"));trailMaterial.SetTexture("_FogMask",Fog.Texture);
                var renderer=trailObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=trailMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            }
            trailVertices.Clear();trailIndices.Clear();trailUv.Clear();trailData.Clear();trailColors.Clear();
            foreach(var sample in view.GroundTrails)
            {
                if(view.Tick<sample.Tick||view.Tick-sample.Tick>=lifetimeTicks)continue;
                AddTrail(sample,view);
            }
            trailMesh.Clear();trailMesh.SetVertices(trailVertices);trailMesh.SetUVs(0,trailUv);trailMesh.SetUVs(1,trailData);trailMesh.SetColors(trailColors);trailMesh.SetTriangles(trailIndices,0,true);
            trailMaterial.SetFloat("_Extent",(float)profile.ArenaHalfExtent);trailMaterial.SetFloat("_Now",view.Tick/30f);
            trailMaterial.SetFloat("_Lifetime",lifetimeTicks/30f);trailMaterial.SetFloat("_Fade",Mathf.Min(TrailArt.fadeSeconds,lifetimeTicks/30f));trailMaterial.SetFloat("_Opacity",TrailArt.opacity);trailMaterial.SetFloat("_Edge",TrailArt.edgeSoftness);trailMaterial.SetFloat("_Grain",TrailArt.grain);
        }
        void AddTrail(PlayableGroundTrail s,PlayableSnapshot view)
        {
            bool tank=s.Kind==PlayableEntityKind.Tank,scout=s.Kind==PlayableEntityKind.Explorer;
            float scale=(float)(tank?profile.TankModelRadius*profile.TankModelScale/1.585:scout?profile.ExplorerModelRadius*profile.ExplorerModelScale/1.0773:profile.ShkvalModelRadius*profile.ShkvalModelScale/2.05);
            float gauge=(tank?TrailArt.tankGauge:scout?TrailArt.explorerGauge:TrailArt.shkvalGauge)*scale;
            float width=(tank?TrailArt.tankWidth:scout?TrailArt.explorerWidth:TrailArt.shkvalWidth)*scale;
            float pitch=(tank?TrailArt.tankPitch:scout?TrailArt.explorerPitch:TrailArt.shkvalPitch)*scale;
            var from=new Vector2((float)s.From.X,(float)s.From.Z);var to=new Vector2((float)s.To.X,(float)s.To.Z);var delta=to-from;float length=delta.magnitude;
            if(length<.00001f)return;
            var forward=new Vector2((float)Math.Cos(s.Heading),(float)Math.Sin(s.Heading));
            var side=new Vector2(-forward.y,forward.x);
            float rear=(tank?TrailArt.tankRearContact:scout?TrailArt.explorerRearContact:TrailArt.shkvalRearContact)*scale;
            float front=(tank?TrailArt.tankFrontContact:scout?TrailArt.explorerFrontContact:TrailArt.shkvalFrontContact)*scale;
            // Expose impressions at the trailing contact edge, not under the chassis centre.
            var contact=forward*(Vector2.Dot(delta,forward)>=0?-rear:front);from+=contact;to+=contact;
            // Quarter-metre tessellation is a geometric clipping precision, not an art value.
            int steps=Mathf.Max(1,Mathf.CeilToInt(length/.25f));
            for(int strip=-1;strip<=1;strip+=2)for(int j=0;j<steps;j++)
            {
                float a=(float)j/steps,b=(float)(j+1)/steps;
                Vector2 ca=Vector2.Lerp(from,to,a)+side*gauge*strip,cb=Vector2.Lerp(from,to,b)+side*gauge*strip;
                var q=new[]{ca-side*width*.5f,ca+side*width*.5f,cb+side*width*.5f,cb-side*width*.5f};
                var kind=TrailSurface(new NavPoint(ca.x,ca.y));if(kind==GroundTrailSurfaceKind.Hard)continue;
                bool valid=true;foreach(var p in q){var n=new NavPoint(p.x,p.y);if(TrailSurface(n)!=kind||view.Vision!=null&&!view.Vision.IsVisible(n)){valid=false;break;}}
                if(!valid)continue;
                Color color=kind==GroundTrailSurfaceKind.Grass?new Color(.10f,.13f,.055f,TrailArt.grassStrength):kind==GroundTrailSurfaceKind.Soil?new Color(.16f,.12f,.085f,1):kind==GroundTrailSurfaceKind.Salt?new Color(.34f,.32f,.27f,.8f):new Color(.29f,.23f,.15f,.9f);
                int first=trailVertices.Count;
                for(int k=0;k<4;k++)
                {
                    var p=q[k];var n=new NavPoint(p.x,p.y);float h=(float)(profile.AuthoredMap?.SurfaceHeight(n)??0);
                    // Millimetre lift avoids coplanar depth conflict without floating tracks.
                    trailVertices.Add(new Vector3(p.x,h+.012f,p.y));
                    trailUv.Add(new Vector4(k==0||k==3?0:1,((float)s.Distance+length*(k<2?a:b))/pitch,0,0));
                    trailData.Add(new Vector4(s.Tick/30f,tank?0:scout?1:2,(int)kind,0));trailColors.Add(color);
                }
                trailIndices.Add(first);trailIndices.Add(first+1);trailIndices.Add(first+2);trailIndices.Add(first);trailIndices.Add(first+2);trailIndices.Add(first+3);
            }
        }
        public GroundTrailSurfaceKind TrailSurface(NavPoint p)
        {
            if(!trailAreasInitialized){trailAreasInitialized=true;
                if(profile.AuthoredMap is MossHighlandMap moss){var art=MossHighlandArtProfile.Load();foreach(var site in moss.Sites(profile))if(TerritoryRules.Center(site.Kind))trailHardAreas.Add(MossApronBounds(site,profile,art));}
                if(profile.AuthoredMap is BasaltCrownMap basalt){var art=BasaltCrownArtProfile.Load();trailBasaltPads=BasaltCrownDetails.SitePads(basalt,profile,art);trailPadLink=art.siteLinkWidth;}
            }
            foreach(var area in trailHardAreas)if(area.Contains(p))return GroundTrailSurfaceKind.Hard;
            if(trailBasaltPads!=null)foreach(var pad in trailBasaltPads)if(BasaltCrownDetails.PadDistance(p,pad,trailPadLink)<=0)return GroundTrailSurfaceKind.Hard;
            var kind=GroundTrailSurface.At(profile.AuthoredMap,p);if(kind==GroundTrailSurfaceKind.Hard)return kind;
            // Real construction apron geometry is elevated and depth-occludes impressions.
            // Exclude its footprint too, so no ground marks appear later through the slab.
            foreach(var pad in territoryPads.Values)if(pad.Root)
            {var local=pad.Root.transform.InverseTransformPoint(Point(p));if(Mathf.Abs(local.x)<=pad.Half.x&&Mathf.Abs(local.z)<=pad.Half.y)return GroundTrailSurfaceKind.Hard;}
            return kind;
        }
        void ClearGroundTrails()
        {if(trailObject)ReleaseWorldObject(trailObject);if(trailMesh)ReleaseWorldObject(trailMesh);if(trailMaterial)ReleaseWorldObject(trailMaterial);trailObject=null;trailMesh=null;trailMaterial=null;trailSequence=-1;trailGeneration=-1;trailCount=-1;trailLast=null;trailVertices.Clear();trailIndices.Clear();}
    }
}
