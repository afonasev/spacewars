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
        // This dictionary is queried by value and evicted by its separate FIFO.
        // Explicit double equality keeps ValueType key semantics without boxing;
        // both coordinates contribute to the hash for straight trail segments.
        sealed class TrailPointComparer:IEqualityComparer<NavPoint>
        {
            internal static readonly TrailPointComparer Instance=new TrailPointComparer();
            public bool Equals(NavPoint a,NavPoint b)=>a.Equals(b);
            public int GetHashCode(NavPoint point)=>unchecked(point.X.GetHashCode()*397^point.Z.GetHashCode());
        }
        readonly Dictionary<NavPoint,GroundTrailSurfaceKind> trailTerrainKinds=new Dictionary<NavPoint,GroundTrailSurfaceKind>(TrailPointComparer.Instance);
        readonly Queue<NavPoint> trailTerrainOrder=new Queue<NavPoint>();
        readonly GroundTrailVehicleSelection trailVehicles=new GroundTrailVehicleSelection();
        // Derived geometry only. Visibility and construction pads remain live.
        const int TrailGeometrySampleLimit=8192,TrailGeometryQuadLimit=32768;
        const int TrailGeometryPoolLimit=256,TrailGeometryPooledQuadLimit=8;
        sealed class TrailGeometry
        {
            internal long Epoch;
            internal long PadRevision=-1;
            internal bool[] PadBlocked;
            internal readonly List<Vector3> Vertices=new List<Vector3>(),PadPoints=new List<Vector3>(),Centers=new List<Vector3>();
            internal readonly List<Vector4> Uv=new List<Vector4>(),Data=new List<Vector4>();
            internal readonly List<Color> Colors=new List<Color>();
            internal Vector2 PadMin=new Vector2(float.PositiveInfinity,float.PositiveInfinity);
            internal Vector2 PadMax=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
            internal bool FinitePadBounds=true;

            internal void Reset()
            {
                PadRevision=-1;
                Vertices.Clear();
                PadPoints.Clear();
                Centers.Clear();
                Uv.Clear();
                Data.Clear();
                Colors.Clear();
                PadMin=new Vector2(float.PositiveInfinity,float.PositiveInfinity);
                PadMax=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
                FinitePadBounds=true;
            }

            internal void IncludePadPoint(Vector3 point)
            {
                FinitePadBounds&=FiniteTrailPoint(point);
                var xz=new Vector2(point.x,point.z);
                PadMin=Vector2.Min(PadMin,xz);
                PadMax=Vector2.Max(PadMax,xz);
            }
        }
        readonly Dictionary<PlayableGroundTrail,TrailGeometry> trailGeometry=new Dictionary<PlayableGroundTrail,TrailGeometry>();
        readonly List<PlayableGroundTrail> trailGeometryRetired=new List<PlayableGroundTrail>();
        readonly Stack<TrailGeometry> trailGeometryPool=new Stack<TrailGeometry>();
        readonly float[] trailGeometryStyle=new float[31];
        bool trailGeometryStyleValid;long trailGeometryEpoch;int trailGeometryQuads;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal bool DisableTrailTerrainReuseForQa;
        internal bool DisableTrailGeometryReuseForQa;
        internal bool UseTrailAllocationReferenceForQa;
        internal int TrailGeometryCreatedForQa,TrailCornerArraysCreatedForQa;
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
            if(profile.AuthoredMap is DeafMirrorMap mirror){
                if(mirrorTrailPads==null)mirrorTrailPads=MirrorPads(mirror,profile);
                foreach(var pad in mirrorTrailPads)if(pad.Outer.Contains(point))return GroundTrailSurfaceKind.Hard;
            }
            var kind=OnRelayGround(point)?GroundTrailSurfaceKind.Hard:GroundTrailSurface.At(profile.AuthoredMap,point);
            if(kind==GroundTrailSurfaceKind.Soil&&profile.AuthoredMap is ThreeCrossingsMap crossings&&OnCrossingsMineConcrete(crossings,point))
                kind=GroundTrailSurfaceKind.Hard;
            if(reuse){if(trailTerrainKinds.Count==trailTerrainCapacity)trailTerrainKinds.Remove(trailTerrainOrder.Dequeue());trailTerrainKinds.Add(point,kind);trailTerrainOrder.Enqueue(point);}
            return kind;
        }
        AntennaCityPads.Pad[] mirrorTrailPads;
        ThreeCrossingsMaterials.MineAccess[] trailMineAccesses;
        ThreeCrossingsSurfaceProfile trailCrossingsArt;

        bool OnCrossingsMineConcrete(ThreeCrossingsMap map,NavPoint point)
        {
            EnsureTrailMineAccesses(map);
            // The concrete dominates at the midpoint of the same blend used for rendering.
            foreach(var access in trailMineAccesses)
                if(access.YardDepth(point,trailCrossingsArt,out _,out _)>=trailCrossingsArt.minePadContactWidth*.5f)
                    return map.SupportsFootprint(point,0);
            return false;
        }

        void EnsureTrailMineAccesses(ThreeCrossingsMap map)
        {
            if(trailMineAccesses!=null)return;
            trailCrossingsArt=surfaceMaterials?.Profile??ThreeCrossingsSurfaceProfile.Load();
            trailMineAccesses=surfaceMaterials?.MineAccesses??ThreeCrossingsMaterials.PlanMineAccess(trailCrossingsArt,map,profile);
        }

        readonly List<NavObstacle> trailHardAreas=new List<NavObstacle>();
        BasaltCrownDetails.SitePad[] trailBasaltPads;BasaltCrownRoads.RelayPlaza trailBasaltPlaza;bool trailAreasInitialized;
        int trailLifetimeTicks=-1;
        PlayableGroundTrail trailLast;int trailCount=-1;long trailFog=-1,trailExpiry=long.MaxValue;
        GameObject trailObject;Mesh trailMesh;Material trailMaterial;long trailSequence=-1,trailGeneration=-1;
        GroundTrailProfile trailArt;public GroundTrailProfile TrailArt=>trailArt??(trailArt=GroundTrailProfile.Load());
        readonly List<Vector3> trailVertices=new List<Vector3>();readonly List<int> trailIndices=new List<int>();
        readonly List<Vector4> trailUv=new List<Vector4>(),trailData=new List<Vector4>();readonly List<Color> trailColors=new List<Color>();
        private struct TrailPadQuery
        {
            internal Transform Node;
            internal Vector3 Center,Scale;
            internal Quaternion Rotation;
            internal Vector2 Half;
            internal bool Axis;

            internal bool SamePlacement(TrailPadQuery other)=>Node==other.Node&&
                SameShape(other);
            internal bool SameShape(TrailPadQuery other)=>
                Center.Equals(other.Center)&&Rotation.Equals(other.Rotation)&&Scale.Equals(other.Scale)&&Half.Equals(other.Half);
        }
        List<TrailPadQuery> trailPadQueries=new List<TrailPadQuery>();
        List<TrailPadQuery> previousTrailPadQueries=new List<TrailPadQuery>();
        long trailPadRevision;
        bool trailPadMemoActive;
        readonly List<TrailPadQuery> trailPadCandidates=new List<TrailPadQuery>();
        bool trailPadQueriesActive;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Exact predecessor control only; never exposed in game settings.
        public bool NativeTrailPadReferenceEvidence {get;set;}
        internal bool DisableTrailPadCandidateCullForQa;
        internal bool DisableTrailPadMemoForQa;
#endif
        public int GroundTrailQuadCount=>trailMeshLease?.Quads??0;
        public void RenderGroundTrails(PlayableSnapshot view,Camera camera=null)=>
            RenderGroundTrails(view,camera,false,null);

        internal void RenderGroundTrails(PlayableSnapshot view,Camera camera,bool share,PlayableWorld source)
        {
            if(view==null)return;
            if(!NativeGraphicsSettings.GroundTrailsEnabled){HideTrailMesh();return;}
            if(camera&&camera.orthographic&&camera.orthographicSize>=ModelDetailProfile.Current.trailHideZoom){
                // Zoom can change while the simulation is paused on this same snapshot.
                // Rebuild on return so expired marks never reappear from the old mesh.
                HideTrailMesh();return;
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
                trailFilter=trailObject.AddComponent<MeshFilter>();
                trailMaterial=new Material(Resources.Load<Shader>("GroundTrails"));trailMaterial.SetTexture("_FogMask",Fog.Texture);
                var renderer=trailObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=trailMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            }
            RefreshTrailGeometryStyle();
            int vehicleDivisor=GroundTrailDensity.Divisor(view,ModelDetailProfile.Current);
            CaptureTrailPadQueries();
            trailShareVision=null;
            bool shareable=share&&view.Vision!=null&&trailPadMemoActive;
            try
            {
                if(shareable)CollectTrailSamples(view,vehicleDivisor,lifetimeTicks);
                if(shareable&&CanReuseTrailMesh(source,view))
                {
                    SetTrailMesh(source.trailMeshLease);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    TrailMeshReusesForQa++;
#endif
                }
                else
                {
                    RebuildTrailMesh(view,vehicleDivisor,lifetimeTicks,shareable);
                }
                if(shareable)RecordShareableTrailMesh(view);
            }
            finally{trailPadCandidates.Clear();trailPadQueriesActive=false;}
            trailMaterial.SetFloat("_Extent",(float)profile.ArenaHalfExtent);trailMaterial.SetFloat("_Now",view.Tick/30f);
            trailMaterial.SetFloat("_Lifetime",lifetimeTicks/30f);trailMaterial.SetFloat("_Fade",Mathf.Min(TrailArt.fadeSeconds,lifetimeTicks/30f));trailMaterial.SetFloat("_Opacity",TrailArt.opacity);trailMaterial.SetFloat("_Edge",TrailArt.edgeSoftness);trailMaterial.SetFloat("_Grain",TrailArt.grain);
        }

        void RebuildTrailMesh(PlayableSnapshot view,int vehicleDivisor,int lifetimeTicks,bool selected)
        {
            trailGeometryEpoch++;
            trailVertices.Clear();trailIndices.Clear();trailUv.Clear();trailData.Clear();trailColors.Clear();
            IReadOnlyList<PlayableGroundTrail> samples=selected?trailPicked:view.GroundTrails;
            for(int i=0;i<samples.Count;i++)
            {
                var sample=samples[i];
                if(selected||KeepTrailSample(sample,view.Tick,vehicleDivisor,lifetimeTicks))
                    AddTrail(sample,view);
            }
            trailGeometryRetired.Clear();foreach(var row in trailGeometry)if(row.Value.Epoch!=trailGeometryEpoch)trailGeometryRetired.Add(row.Key);
            foreach(var sample in trailGeometryRetired){
                var retired=trailGeometry[sample];
                trailGeometry.Remove(sample);
                trailGeometryQuads-=retired.Centers.Count;
                ReturnTrailGeometry(retired);
            }
            trailGeometryRetired.Clear();
            PrepareWritableTrailMesh();
            trailMesh.Clear();trailMesh.SetVertices(trailVertices);trailMesh.SetUVs(0,trailUv);trailMesh.SetUVs(1,trailData);trailMesh.SetColors(trailColors);trailMesh.SetTriangles(trailIndices,0,true);
            trailMeshLease.Quads=trailIndices.Count/6;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            TrailMeshBuildsForQa++;
#endif
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
                geometry=RentTrailGeometry();BuildTrail(s,view,geometry);trailGeometry.Add(s,geometry);trailGeometryQuads+=geometry.Centers.Count;
            }
            geometry.Epoch=trailGeometryEpoch;
            if(geometry.Centers.Count==0)return;
            bool memo=trailPadMemoActive;
            List<TrailPadQuery> pads=null;
            if(!memo)pads=CandidateTrailPads(geometry);
            else if(geometry.PadRevision!=trailPadRevision){
                pads=CandidateTrailPads(geometry);
                if(geometry.PadBlocked==null||geometry.PadBlocked.Length<geometry.Centers.Count)
                    geometry.PadBlocked=new bool[geometry.Centers.Count];
                for(int quad=0;quad<geometry.Centers.Count;quad++)geometry.PadBlocked[quad]=TrailQuadTouchesPad(geometry,quad,pads);
                geometry.PadRevision=trailPadRevision;
            }
            for(int quad=0;quad<geometry.Centers.Count;quad++){
                if(memo?geometry.PadBlocked[quad]:TrailPadAt(geometry.Centers[quad],pads))continue;
                bool visible=true;int offset=quad*4;
                for(int k=0;k<4;k++){
                    var point=geometry.PadPoints[offset+k];
                    if((!memo&&TrailPadAt(point,pads))||view.Vision!=null&&!view.Vision.IsVisible(new NavPoint(point.x,point.z))){
                        visible=false;
                        break;
                    }
                }
                if(!visible)continue;
                int first=trailVertices.Count;
                for(int k=0;k<4;k++){int at=offset+k;trailVertices.Add(geometry.Vertices[at]);trailUv.Add(geometry.Uv[at]);trailData.Add(geometry.Data[at]);trailColors.Add(geometry.Colors[at]);}
                trailIndices.Add(first);trailIndices.Add(first+1);trailIndices.Add(first+2);trailIndices.Add(first);trailIndices.Add(first+2);trailIndices.Add(first+3);
            }
        }
        TrailGeometry RentTrailGeometry()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(!UseTrailAllocationReferenceForQa)
#endif
            if(trailGeometryPool.Count>0)return trailGeometryPool.Pop();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            TrailGeometryCreatedForQa++;
#endif
            return new TrailGeometry();
        }
        void ReturnTrailGeometry(TrailGeometry geometry)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(UseTrailAllocationReferenceForQa)return;
#endif
            // Keep only small buffers and cap how many survive an expiry burst.
            if(trailGeometryPool.Count>=TrailGeometryPoolLimit||geometry.Vertices.Capacity>TrailGeometryPooledQuadLimit*4)return;
            geometry.Reset();
            trailGeometryPool.Push(geometry);
        }
        bool TrailQuadTouchesPad(TrailGeometry geometry,int quad,List<TrailPadQuery> pads)
        {
            if(TrailPadAt(geometry.Centers[quad],pads))return true;
            int offset=quad*4;
            for(int vertex=0;vertex<4;vertex++)
                if(TrailPadAt(geometry.PadPoints[offset+vertex],pads))return true;
            return false;
        }
        List<TrailPadQuery> CandidateTrailPads(TrailGeometry geometry)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(DisableTrailPadCandidateCullForQa)return trailPadQueries;
#endif
            if(!trailPadQueriesActive||!geometry.FinitePadBounds)return trailPadQueries;
            trailPadCandidates.Clear();
            foreach(var pad in trailPadQueries){
                // The closest point on the sample bounds uses the same broad-phase
                // arithmetic as each individual point. General transforms stay exact.
                if(pad.Axis){
                    float x=Mathf.Clamp(pad.Center.x,geometry.PadMin.x,geometry.PadMax.x);
                    float z=Mathf.Clamp(pad.Center.z,geometry.PadMin.y,geometry.PadMax.y);
                    if(Mathf.Abs(x-pad.Center.x)>2*pad.Half.x+1||Mathf.Abs(z-pad.Center.z)>2*pad.Half.y+1)continue;
                }
                trailPadCandidates.Add(pad);
            }
            return trailPadCandidates;
        }
        bool TrailPadAt(Vector3 point,List<TrailPadQuery> pads)
        {
            // Cached trail quads use the same captured, conservative broad
            // phase as uncached construction. Nearby/general-transform cases
            // still execute Unity's original exact inverse-transform predicate.
            if(trailPadQueriesActive){
                pads??=trailPadQueries;
                if(pads.Count==0)return false;
                bool finite=FiniteTrailPoint(point);
                foreach(var pad in pads){
                    if(pad.Axis&&finite&&(Mathf.Abs(point.x-pad.Center.x)>2*pad.Half.x+1||Mathf.Abs(point.z-pad.Center.z)>2*pad.Half.y+1))continue;
                    var local=pad.Node.InverseTransformPoint(point);if(Mathf.Abs(local.x)<=pad.Half.x&&Mathf.Abs(local.z)<=pad.Half.y)return true;
                }
                return false;
            }
            foreach(var pad in territoryPads.Values)if(pad.Root){var local=pad.Root.transform.InverseTransformPoint(point);if(Mathf.Abs(local.x)<=pad.Half.x&&Mathf.Abs(local.z)<=pad.Half.y)return true;}
            return false;
        }
        void RefreshTrailGeometryStyle()
        {
            var art=TrailArt;
            Span<float> values=stackalloc float[]{(float)(profile.TankModelRadius*profile.TankModelScale/1.585),(float)(profile.ExplorerModelRadius*profile.ExplorerModelScale/1.0773),(float)(profile.ShkvalModelRadius*profile.ShkvalModelScale/2.05),art.tankGauge,art.tankWidth,art.tankPitch,art.tankRearContact,art.tankFrontContact,art.explorerGauge,art.explorerWidth,art.explorerPitch,art.explorerRearContact,art.explorerFrontContact,art.shkvalGauge,art.shkvalWidth,art.shkvalPitch,art.shkvalRearContact,art.shkvalFrontContact,art.grassStrength,(float)PlayableUnitRules.CerberusModelScale(profile),(float)PlayableUnitRules.IglaModelScale,art.cerberusGauge,art.cerberusWidth,art.cerberusPitch,art.cerberusRearContact,art.cerberusFrontContact,art.iglaGauge,art.iglaWidth,art.iglaPitch,art.iglaRearContact,art.iglaFrontContact};
            bool changed=!trailGeometryStyleValid;for(int i=0;i<values.Length;i++){changed|=!values[i].Equals(trailGeometryStyle[i]);trailGeometryStyle[i]=values[i];}
            if(changed){trailGeometry.Clear();trailGeometryQuads=0;trailGeometryStyleValid=true;}
        }
        void BuildTrail(PlayableGroundTrail s,PlayableSnapshot view,TrailGeometry geometry)
        {
            var vertices=geometry?.Vertices??trailVertices;var uv=geometry?.Uv??trailUv;var data=geometry?.Data??trailData;var colors=geometry?.Colors??trailColors;
            bool tank=s.Kind==PlayableEntityKind.Tank,scout=s.Kind==PlayableEntityKind.Explorer,cerberus=s.Kind==PlayableEntityKind.Cerberus,igla=s.Kind==PlayableEntityKind.Igla;
            float scale=(float)(cerberus?PlayableUnitRules.CerberusModelScale(profile):igla?PlayableUnitRules.IglaModelScale:tank?profile.TankModelRadius*profile.TankModelScale/1.585:scout?profile.ExplorerModelRadius*profile.ExplorerModelScale/1.0773:profile.ShkvalModelRadius*profile.ShkvalModelScale/2.05);
            float gauge=(cerberus?TrailArt.cerberusGauge:igla?TrailArt.iglaGauge:tank?TrailArt.tankGauge:scout?TrailArt.explorerGauge:TrailArt.shkvalGauge)*scale;
            float width=(cerberus?TrailArt.cerberusWidth:igla?TrailArt.iglaWidth:tank?TrailArt.tankWidth:scout?TrailArt.explorerWidth:TrailArt.shkvalWidth)*scale;
            float pitch=(cerberus?TrailArt.cerberusPitch:igla?TrailArt.iglaPitch:tank?TrailArt.tankPitch:scout?TrailArt.explorerPitch:TrailArt.shkvalPitch)*scale;
            var from=new Vector2((float)s.From.X,(float)s.From.Z);var to=new Vector2((float)s.To.X,(float)s.To.Z);var delta=to-from;float length=delta.magnitude;
            if(length<.00001f)return;
            var forward=new Vector2((float)Math.Cos(s.Heading),(float)Math.Sin(s.Heading));
            var side=new Vector2(-forward.y,forward.x);
            float rear=(cerberus?TrailArt.cerberusRearContact:igla?TrailArt.iglaRearContact:tank?TrailArt.tankRearContact:scout?TrailArt.explorerRearContact:TrailArt.shkvalRearContact)*scale;
            float front=(cerberus?TrailArt.cerberusFrontContact:igla?TrailArt.iglaFrontContact:tank?TrailArt.tankFrontContact:scout?TrailArt.explorerFrontContact:TrailArt.shkvalFrontContact)*scale;
            // Expose impressions at the trailing contact edge, not under the chassis centre.
            var contact=forward*(Vector2.Dot(delta,forward)>=0?-rear:front);from+=contact;to+=contact;
            // Quarter-metre tessellation is a geometric clipping precision, not an art value.
            int steps=Mathf.Max(1,Mathf.CeilToInt(length/.25f));
            Span<Vector2> corners=stackalloc Vector2[4];
            for(int strip=-1;strip<=1;strip+=2)for(int j=0;j<steps;j++)
            {
                float a=(float)j/steps,b=(float)(j+1)/steps;
                Vector2 ca=Vector2.Lerp(from,to,a)+side*gauge*strip,cb=Vector2.Lerp(from,to,b)+side*gauge*strip;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if(UseTrailAllocationReferenceForQa){
                    corners=new Vector2[4];
                    TrailCornerArraysCreatedForQa++;
                }
#endif
                corners[0]=ca-side*width*.5f;
                corners[1]=ca+side*width*.5f;
                corners[2]=cb+side*width*.5f;
                corners[3]=cb-side*width*.5f;
                var kind=geometry==null?TrailSurface(new NavPoint(ca.x,ca.y)):TrailTerrainKind(new NavPoint(ca.x,ca.y));if(kind==GroundTrailSurfaceKind.Hard)continue;
                bool valid=true;foreach(var p in corners){var n=new NavPoint(p.x,p.y);if((geometry==null?TrailSurface(n):TrailTerrainKind(n))!=kind||geometry==null&&view.Vision!=null&&!view.Vision.IsVisible(n)){valid=false;break;}}
                if(!valid)continue;
                Color color=kind==GroundTrailSurfaceKind.Grass?new Color(.10f,.13f,.055f,TrailArt.grassStrength):kind==GroundTrailSurfaceKind.Soil?new Color(.16f,.12f,.085f,1):kind==GroundTrailSurfaceKind.Salt?new Color(.34f,.32f,.27f,.8f):new Color(.29f,.23f,.15f,.9f);
                int first=vertices.Count;
                for(int k=0;k<4;k++)
                {
                    var p=corners[k];var n=new NavPoint(p.x,p.y);float h=(float)(profile.AuthoredMap?.SurfaceHeight(n)??0);
                    // Millimetre lift avoids coplanar depth conflict without floating tracks.
                    vertices.Add(new Vector3(p.x,h+.012f,p.y));
                    uv.Add(new Vector4(k==0||k==3?0:1,((float)s.Distance+length*(k<2?a:b))/pitch,0,0));
                    data.Add(new Vector4(s.Tick/30f,tank||cerberus||igla?0:scout?1:2,(int)kind,0));colors.Add(color);
                    if(geometry!=null){
                        var point=new Vector3(p.x,h,p.y);
                        geometry.PadPoints.Add(point);
                        geometry.IncludePadPoint(point);
                    }
                }
                if(geometry==null){trailIndices.Add(first);trailIndices.Add(first+1);trailIndices.Add(first+2);trailIndices.Add(first);trailIndices.Add(first+2);trailIndices.Add(first+3);}
                else{
                    var center=Point(new NavPoint(ca.x,ca.y));
                    geometry.Centers.Add(center);
                    geometry.IncludePadPoint(center);
                }
            }
        }
        public GroundTrailSurfaceKind TrailSurface(NavPoint p)
        {
            if(!trailAreasInitialized){trailAreasInitialized=true;
                if(profile.AuthoredMap is FoundryMap foundry){var art=FoundrySurfaceProfile.Load();foreach(var site in foundry.Sites(profile))trailHardAreas.Add(FoundryTerrainMesh.SitePad(site,profile,art));}
                if(profile.AuthoredMap is MossHighlandMap moss){var art=MossHighlandArtProfile.Load();foreach(var site in moss.Sites(profile))if(TerritoryRules.Center(site.Kind))trailHardAreas.Add(MossApronBounds(site,profile,art));}
                if(profile.AuthoredMap is CrimsonGroveMap grove)
                {
                    var art=CrimsonGroveArtProfile.Load();
                    foreach(var site in grove.Sites(profile))
                        if(TerritoryRules.Center(site.Kind))trailHardAreas.Add(CrimsonGroveSurfaces.Pad(site,profile,art));
                }
                if(profile.AuthoredMap is BasaltCrownMap basalt){
                    trailBasaltPads=BasaltCrownDetails.SitePads(basalt,profile,BasaltCrownArtProfile.Load());
                    trailBasaltPlaza=BasaltCrownRoads.Plaza(basalt,profile);
                }
            }
            foreach(var area in trailHardAreas)if(area.Contains(p))return GroundTrailSurfaceKind.Hard;
            if(trailBasaltPads!=null){
                if(trailBasaltPlaza.SignedDistance((float)p.X,(float)p.Z)<=0)return GroundTrailSurfaceKind.Hard;
                foreach(var pad in trailBasaltPads)if(BasaltCrownDetails.PadDistance(p,pad)<=0)return GroundTrailSurfaceKind.Hard;
            }
            var kind=TrailTerrainKind(p);if(kind==GroundTrailSurfaceKind.Hard)return kind;
            if(profile.AuthoredMap is CrimsonGroveMap groveMap&&
                (!groveSurfaceMask||CrimsonGroveSurfaces.HardGround(groveSurfaceMask,groveMap,p)))return GroundTrailSurfaceKind.Hard;
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
            bool previouslyMemoized=trailPadMemoActive;
            trailPadQueriesActive=false;
            trailPadMemoActive=false;
            (trailPadQueries,previousTrailPadQueries)=(previousTrailPadQueries,trailPadQueries);
            trailPadQueries.Clear();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(NativeTrailPadReferenceEvidence){previousTrailPadQueries.Clear();return;}
#endif
            // Only exact identity ancestors qualify. General TRS/shear and every
            // other orientation keep Unity's native inverse-transform predicate.
            bool identity=true;
            for(var node=root;node;node=node.parent)if(!node.localPosition.Equals(Vector3.zero)||!node.localRotation.Equals(Quaternion.identity)||!node.localScale.Equals(Vector3.one)){identity=false;break;}
            bool memoizable=identity&&profile.AuthoredMap is ThreeCrossingsMap;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            memoizable&=!DisableTrailPadMemoForQa&&!DisableTrailPadCandidateCullForQa;
#endif
            foreach(var pad in territoryPads.Values)if(pad.Root){
                var node=pad.Root.transform;var center=node.localPosition;var rotation=node.localRotation;var scale=node.localScale;
                bool direct=node.parent==root;
                memoizable&=direct&&FiniteTrailPoint(center)&&FiniteTrailPoint(scale);
                bool yaw=rotation.x==0&&rotation.z==0&&(rotation.y==0&&Mathf.Abs(rotation.w)==1||Mathf.Abs(rotation.y)==1&&rotation.w==0);
                // Bounds certify normal arithmetic for the native axis rotation;
                // they are numerical guards, not gameplay or art limits.
                bool axis=identity&&direct&&scale.Equals(Vector3.one)&&yaw&&FiniteTrailPoint(center)&&pad.Half.x>=1f/1024&&pad.Half.x<=1024&&pad.Half.y>=1f/1024&&pad.Half.y<=1024;
                trailPadQueries.Add(new TrailPadQuery{Node=node,Center=center,Rotation=rotation,Scale=scale,Half=pad.Half,Axis=axis});
            }
            trailPadQueriesActive=true;
            trailPadMemoActive=memoizable;
            if(memoizable){
                // Direct children of identity ancestors are fully described by
                // captured local TRS and apron size. Any change invalidates all
                // derived masks. Fog remains live even on a cache hit.
                bool unchanged=previouslyMemoized&&trailPadQueries.Count==previousTrailPadQueries.Count;
                for(int i=0;unchanged&&i<trailPadQueries.Count;i++)
                    unchanged=trailPadQueries[i].SamePlacement(previousTrailPadQueries[i]);
                if(!unchanged)trailPadRevision++;
            }
            previousTrailPadQueries.Clear();
        }
        void ClearGroundTrails()
        {
            trailAreasInitialized=false;trailHardAreas.Clear();trailBasaltPads=null;
            trailMineAccesses=null;trailCrossingsArt=null;
            ClearTrailMeshLeases();
            trailVehicles.Clear();trailGeometry.Clear();trailGeometryPool.Clear();trailGeometryRetired.Clear();trailGeometryQuads=0;trailGeometryStyleValid=false;trailTerrainKinds.Clear();trailTerrainOrder.Clear();trailTerrainCapacity=TrailTerrainCacheCapacity;if(trailObject)ReleaseWorldObject(trailObject);if(trailMaterial)ReleaseWorldObject(trailMaterial);trailObject=null;trailMaterial=null;trailSequence=-1;trailGeneration=-1;trailCount=-1;trailLast=null;trailVertices.Clear();trailIndices.Clear();trailPadQueries.Clear();previousTrailPadQueries.Clear();trailPadCandidates.Clear();trailPadQueriesActive=trailPadMemoActive=false;}
    }
}
