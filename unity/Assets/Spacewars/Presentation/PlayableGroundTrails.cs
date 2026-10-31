using System;
using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        const int TrailTerrainCacheCapacity=4096,TrailTerrainCacheMaximum=131072;
        int trailTerrainCapacity=TrailTerrainCacheCapacity;
        readonly Dictionary<NavPoint,GroundTrailSurfaceKind> trailTerrainKinds=new Dictionary<NavPoint,GroundTrailSurfaceKind>();
        readonly Queue<NavPoint> trailTerrainOrder=new Queue<NavPoint>();
        readonly GroundTrailVehicleSelection trailVehicles=new GroundTrailVehicleSelection();
        // Derived geometry only. Visibility and construction pads remain live.
        const int TrailGeometrySampleLimit=8192,TrailGeometryQuadLimit=32768;
        sealed class TrailGeometry
        {
            internal long Epoch;
            internal readonly List<Vector3> Vertices=new List<Vector3>(),PadPoints=new List<Vector3>(),Centers=new List<Vector3>();
            internal readonly List<Vector4> Uv=new List<Vector4>(),Data=new List<Vector4>();
            internal readonly List<Color> Colors=new List<Color>();
        }
        readonly Dictionary<PlayableGroundTrail,TrailGeometry> trailGeometry=new Dictionary<PlayableGroundTrail,TrailGeometry>();
        readonly List<PlayableGroundTrail> trailGeometryRetired=new List<PlayableGroundTrail>();
        readonly float[] trailGeometryStyle=new float[19];
        bool trailGeometryStyleValid;long trailGeometryEpoch;int trailGeometryQuads;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal bool DisableTrailTerrainReuseForQa;
        internal bool DisableTrailGeometryReuseForQa;
        internal long TrailTerrainQueriesForQa,TrailTerrainHitsForQa;
#endif
        GroundTrailSurfaceKind TrailTerrainKind(NavPoint point)
        {
            // Only this sealed map's immutable support/material queries are audited.
            // Building pads and fog are deliberately evaluated after this memo.
            bool reuse=profile.AuthoredMap is ThreeCrossingsMap;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            reuse&=!DisableTrailTerrainReuseForQa;
#endif
            if(reuse&&trailTerrainKinds.TryGetValue(point,out var found)){
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                TrailTerrainHitsForQa++;
#endif
                return found;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            TrailTerrainQueriesForQa++;
#endif
            var kind=GroundTrailSurface.At(profile.AuthoredMap,point);
            if(reuse){if(trailTerrainKinds.Count==trailTerrainCapacity)trailTerrainKinds.Remove(trailTerrainOrder.Dequeue());trailTerrainKinds.Add(point,kind);trailTerrainOrder.Enqueue(point);}
            return kind;
        }
        readonly List<NavObstacle> trailHardAreas=new List<NavObstacle>();
        BasaltCrownDetails.SitePad[] trailBasaltPads;float trailPadLink;bool trailAreasInitialized;
        int trailLifetimeTicks=-1;
        PlayableGroundTrail trailLast;int trailCount=-1;long trailFog=-1,trailExpiry=long.MaxValue;
        GameObject trailObject;Mesh trailMesh;Material trailMaterial;long trailSequence=-1,trailGeneration=-1;
        GroundTrailProfile trailArt;public GroundTrailProfile TrailArt=>trailArt??(trailArt=GroundTrailProfile.Load());
        readonly List<Vector3> trailVertices=new List<Vector3>();readonly List<int> trailIndices=new List<int>();
        readonly List<Vector4> trailUv=new List<Vector4>(),trailData=new List<Vector4>();readonly List<Color> trailColors=new List<Color>();
        private struct TrailPadQuery {internal Transform Node;internal Vector3 Center;internal Vector2 Half;internal bool Axis;}
        readonly List<TrailPadQuery> trailPadQueries=new List<TrailPadQuery>();
        bool trailPadQueriesActive;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Exact predecessor control only; never exposed in game settings.
        public bool NativeTrailPadReferenceEvidence {get;set;}
#endif
        public int GroundTrailQuadCount=>trailIndices.Count/6;
        public void RenderGroundTrails(PlayableSnapshot view,Camera camera=null)
        {
            if(view==null)return;
            if(!NativeGraphicsSettings.GroundTrailsEnabled){if(trailObject)trailObject.SetActive(false);trailSequence=-1;trailCount=-1;return;}
            if(camera&&camera.orthographic&&camera.orthographicSize>=ModelDetailProfile.Current.trailHideZoom){
                if(trailObject)trailObject.SetActive(false);
                // Zoom can change while the simulation is paused on this same snapshot.
                // Rebuild on return so expired marks never reappear from the old mesh.
                trailSequence=-1;trailCount=-1;return;
            }
            // A full rebuild scans the live journal in order. A smaller FIFO than
            // that working set evicts the next frame's entries before reaching them.
            // Two strips, five surface probes and two usual quarter-metre pieces
            // per sample cover the moving group without changing clipping precision.
            trailTerrainCapacity=(int)Math.Max(trailTerrainCapacity,Math.Min(TrailTerrainCacheMaximum,(long)view.GroundTrails.Count*20));
            if(trailObject)trailObject.SetActive(true);
            int lifetimeTicks=(view.ActiveProfile??profile).GroundTrailLifetimeTicks;
            bool lifetimeChanged=trailLifetimeTicks!=lifetimeTicks;trailLifetimeTicks=lifetimeTicks;
            trailVehicles.Begin(view.Generation,view.Tick,lifetimeTicks);
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
            RefreshTrailGeometryStyle();trailGeometryEpoch++;
            trailVertices.Clear();trailIndices.Clear();trailUv.Clear();trailData.Clear();trailColors.Clear();
            int vehicleDivisor=GroundTrailDensity.Divisor(view,ModelDetailProfile.Current);
            CaptureTrailPadQueries();
            try{foreach(var sample in view.GroundTrails)
            {
                if(view.Tick<sample.Tick||view.Tick-sample.Tick>=lifetimeTicks)continue;
                if(!trailVehicles.Keep(sample,vehicleDivisor))continue;
                AddTrail(sample,view);
            }}finally{trailPadQueriesActive=false;}
            trailGeometryRetired.Clear();foreach(var row in trailGeometry)if(row.Value.Epoch!=trailGeometryEpoch)trailGeometryRetired.Add(row.Key);
            foreach(var sample in trailGeometryRetired){trailGeometryQuads-=trailGeometry[sample].Centers.Count;trailGeometry.Remove(sample);}
            trailGeometryRetired.Clear();
            trailMesh.Clear();trailMesh.SetVertices(trailVertices);trailMesh.SetUVs(0,trailUv);trailMesh.SetUVs(1,trailData);trailMesh.SetColors(trailColors);trailMesh.SetTriangles(trailIndices,0,true);
            trailMaterial.SetFloat("_Extent",(float)profile.ArenaHalfExtent);trailMaterial.SetFloat("_Now",view.Tick/30f);
            trailMaterial.SetFloat("_Lifetime",lifetimeTicks/30f);trailMaterial.SetFloat("_Fade",Mathf.Min(TrailArt.fadeSeconds,lifetimeTicks/30f));trailMaterial.SetFloat("_Opacity",TrailArt.opacity);trailMaterial.SetFloat("_Edge",TrailArt.edgeSoftness);trailMaterial.SetFloat("_Grain",TrailArt.grain);
        }
        void AddTrail(PlayableGroundTrail s,PlayableSnapshot view)
        {
            bool reuse=profile.AuthoredMap is ThreeCrossingsMap;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            reuse&=!DisableTrailTerrainReuseForQa&&!DisableTrailGeometryReuseForQa;
#endif
            if(!reuse){BuildTrail(s,view,null);return;}
            if(!trailGeometry.TryGetValue(s,out var geometry)){
                var delta=new Vector2((float)s.To.X-(float)s.From.X,(float)s.To.Z-(float)s.From.Z);
                long potential=2L*Mathf.Max(1,Mathf.CeilToInt(delta.magnitude/.25f));
                if(trailGeometry.Count>=TrailGeometrySampleLimit||potential>TrailGeometryQuadLimit-trailGeometryQuads){BuildTrail(s,view,null);return;}
                geometry=new TrailGeometry();BuildTrail(s,view,geometry);trailGeometry.Add(s,geometry);trailGeometryQuads+=geometry.Centers.Count;
            }
            geometry.Epoch=trailGeometryEpoch;
            for(int quad=0;quad<geometry.Centers.Count;quad++){
                if(TrailPadAt(geometry.Centers[quad]))continue;
                bool visible=true;int offset=quad*4;
                for(int k=0;k<4;k++){var point=geometry.PadPoints[offset+k];if(TrailPadAt(point)||view.Vision!=null&&!view.Vision.IsVisible(new NavPoint(point.x,point.z))){visible=false;break;}}
                if(!visible)continue;
                int first=trailVertices.Count;
                for(int k=0;k<4;k++){int at=offset+k;trailVertices.Add(geometry.Vertices[at]);trailUv.Add(geometry.Uv[at]);trailData.Add(geometry.Data[at]);trailColors.Add(geometry.Colors[at]);}
                trailIndices.Add(first);trailIndices.Add(first+1);trailIndices.Add(first+2);trailIndices.Add(first);trailIndices.Add(first+2);trailIndices.Add(first+3);
            }
        }
        bool TrailPadAt(Vector3 point)
        {
            foreach(var pad in territoryPads.Values)if(pad.Root){var local=pad.Root.transform.InverseTransformPoint(point);if(Mathf.Abs(local.x)<=pad.Half.x&&Mathf.Abs(local.z)<=pad.Half.y)return true;}
            return false;
        }
        void RefreshTrailGeometryStyle()
        {
            var art=TrailArt;
            Span<float> values=stackalloc float[]{(float)(profile.TankModelRadius*profile.TankModelScale/1.585),(float)(profile.ExplorerModelRadius*profile.ExplorerModelScale/1.0773),(float)(profile.ShkvalModelRadius*profile.ShkvalModelScale/2.05),art.tankGauge,art.tankWidth,art.tankPitch,art.tankRearContact,art.tankFrontContact,art.explorerGauge,art.explorerWidth,art.explorerPitch,art.explorerRearContact,art.explorerFrontContact,art.shkvalGauge,art.shkvalWidth,art.shkvalPitch,art.shkvalRearContact,art.shkvalFrontContact,art.grassStrength};
            bool changed=!trailGeometryStyleValid;for(int i=0;i<values.Length;i++){changed|=!values[i].Equals(trailGeometryStyle[i]);trailGeometryStyle[i]=values[i];}
            if(changed){trailGeometry.Clear();trailGeometryQuads=0;trailGeometryStyleValid=true;}
        }
        void BuildTrail(PlayableGroundTrail s,PlayableSnapshot view,TrailGeometry geometry)
        {
            var vertices=geometry?.Vertices??trailVertices;var uv=geometry?.Uv??trailUv;var data=geometry?.Data??trailData;var colors=geometry?.Colors??trailColors;
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
                var kind=geometry==null?TrailSurface(new NavPoint(ca.x,ca.y)):TrailTerrainKind(new NavPoint(ca.x,ca.y));if(kind==GroundTrailSurfaceKind.Hard)continue;
                bool valid=true;foreach(var p in q){var n=new NavPoint(p.x,p.y);if((geometry==null?TrailSurface(n):TrailTerrainKind(n))!=kind||geometry==null&&view.Vision!=null&&!view.Vision.IsVisible(n)){valid=false;break;}}
                if(!valid)continue;
                Color color=kind==GroundTrailSurfaceKind.Grass?new Color(.10f,.13f,.055f,TrailArt.grassStrength):kind==GroundTrailSurfaceKind.Soil?new Color(.16f,.12f,.085f,1):kind==GroundTrailSurfaceKind.Salt?new Color(.34f,.32f,.27f,.8f):new Color(.29f,.23f,.15f,.9f);
                int first=vertices.Count;
                for(int k=0;k<4;k++)
                {
                    var p=q[k];var n=new NavPoint(p.x,p.y);float h=(float)(profile.AuthoredMap?.SurfaceHeight(n)??0);
                    // Millimetre lift avoids coplanar depth conflict without floating tracks.
                    vertices.Add(new Vector3(p.x,h+.012f,p.y));
                    uv.Add(new Vector4(k==0||k==3?0:1,((float)s.Distance+length*(k<2?a:b))/pitch,0,0));
                    data.Add(new Vector4(s.Tick/30f,tank?0:scout?1:2,(int)kind,0));colors.Add(color);
                    geometry?.PadPoints.Add(new Vector3(p.x,h,p.y));
                }
                if(geometry==null){trailIndices.Add(first);trailIndices.Add(first+1);trailIndices.Add(first+2);trailIndices.Add(first);trailIndices.Add(first+2);trailIndices.Add(first+3);}
                else geometry.Centers.Add(Point(new NavPoint(ca.x,ca.y)));
            }
        }
        public GroundTrailSurfaceKind TrailSurface(NavPoint p)
        {
            if(!trailAreasInitialized){trailAreasInitialized=true;
                if(profile.AuthoredMap is FoundryMap foundry){var art=FoundrySurfaceProfile.Load();foreach(var site in foundry.Sites(profile))trailHardAreas.Add(FoundryTerrainMesh.SitePad(site,profile,art));}
                if(profile.AuthoredMap is MossHighlandMap moss){var art=MossHighlandArtProfile.Load();foreach(var site in moss.Sites(profile))if(TerritoryRules.Center(site.Kind))trailHardAreas.Add(MossApronBounds(site,profile,art));}
                if(profile.AuthoredMap is BasaltCrownMap basalt){var art=BasaltCrownArtProfile.Load();trailBasaltPads=BasaltCrownDetails.SitePads(basalt,profile,art);trailPadLink=art.siteLinkWidth;}
            }
            foreach(var area in trailHardAreas)if(area.Contains(p))return GroundTrailSurfaceKind.Hard;
            if(trailBasaltPads!=null)foreach(var pad in trailBasaltPads)if(BasaltCrownDetails.PadDistance(p,pad,trailPadLink)<=0)return GroundTrailSurfaceKind.Hard;
            var kind=TrailTerrainKind(p);if(kind==GroundTrailSurfaceKind.Hard)return kind;
            // Real construction apron geometry is elevated and depth-occludes impressions.
            // Exclude its footprint too, so no ground marks appear later through the slab.
            if(trailPadQueriesActive){
                if(trailPadQueries.Count==0)return kind;
                // Point/terrain height is independent of the pad being queried.
                var point=Point(p);bool finite=FiniteTrailPoint(point);
                foreach(var pad in trailPadQueries){
                    // Deliberately loose broad phase, not a changed apron edge.
                    // Within the certified coordinate/scale range its extra metre
                    // also dominates float roundoff. Every nearby point, including
                    // both sides of every edge, keeps the exact native predicate.
                    if(pad.Axis&&finite&&(Mathf.Abs(point.x-pad.Center.x)>2*pad.Half.x+1||Mathf.Abs(point.z-pad.Center.z)>2*pad.Half.y+1))continue;
                    var local=pad.Node.InverseTransformPoint(point);if(Mathf.Abs(local.x)<=pad.Half.x&&Mathf.Abs(local.z)<=pad.Half.y)return GroundTrailSurfaceKind.Hard;
                }
            }else foreach(var pad in territoryPads.Values)if(pad.Root)
            {var local=pad.Root.transform.InverseTransformPoint(Point(p));if(Mathf.Abs(local.x)<=pad.Half.x&&Mathf.Abs(local.z)<=pad.Half.y)return GroundTrailSurfaceKind.Hard;}
            return kind;
        }
        static bool FiniteTrailPoint(Vector3 p)=>!float.IsNaN(p.x)&&!float.IsInfinity(p.x)&&!float.IsNaN(p.y)&&!float.IsInfinity(p.y)&&!float.IsNaN(p.z)&&!float.IsInfinity(p.z)&&Mathf.Abs(p.x)<=1048576&&Mathf.Abs(p.z)<=1048576;
        void CaptureTrailPadQueries()
        {
            trailPadQueriesActive=false;trailPadQueries.Clear();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(NativeTrailPadReferenceEvidence)return;
#endif
            // Only exact identity ancestors qualify. General TRS/shear and every
            // other orientation keep Unity's native inverse-transform predicate.
            bool identity=true;
            for(var node=root;node;node=node.parent)if(!node.localPosition.Equals(Vector3.zero)||!node.localRotation.Equals(Quaternion.identity)||!node.localScale.Equals(Vector3.one)){identity=false;break;}
            foreach(var pad in territoryPads.Values)if(pad.Root){
                var node=pad.Root.transform;var center=node.localPosition;var rotation=node.localRotation;
                bool yaw=rotation.x==0&&rotation.z==0&&(rotation.y==0&&Mathf.Abs(rotation.w)==1||Mathf.Abs(rotation.y)==1&&rotation.w==0);
                // Bounds certify normal arithmetic for the native axis rotation;
                // they are numerical guards, not gameplay or art limits.
                bool axis=identity&&node.parent==root&&node.localScale.Equals(Vector3.one)&&yaw&&FiniteTrailPoint(center)&&pad.Half.x>=1f/1024&&pad.Half.x<=1024&&pad.Half.y>=1f/1024&&pad.Half.y<=1024;
                trailPadQueries.Add(new TrailPadQuery{Node=node,Center=center,Half=pad.Half,Axis=axis});
            }
            trailPadQueriesActive=true;
        }
        void ClearGroundTrails()
        {trailVehicles.Clear();trailGeometry.Clear();trailGeometryRetired.Clear();trailGeometryQuads=0;trailGeometryStyleValid=false;trailTerrainKinds.Clear();trailTerrainOrder.Clear();trailTerrainCapacity=TrailTerrainCacheCapacity;if(trailObject)ReleaseWorldObject(trailObject);if(trailMesh)ReleaseWorldObject(trailMesh);if(trailMaterial)ReleaseWorldObject(trailMaterial);trailObject=null;trailMesh=null;trailMaterial=null;trailSequence=-1;trailGeneration=-1;trailCount=-1;trailLast=null;trailVertices.Clear();trailIndices.Clear();trailPadQueries.Clear();trailPadQueriesActive=false;}
    }
}
