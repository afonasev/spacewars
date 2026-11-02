using System;
using System.Collections.Generic;
using System.IO;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal enum AiCombatSupportSelector { Flat, FirstDeclared, UniqueSemanticSurface, Unsupported }
    // Immutable catalog values detached from provider query callbacks. Polygon
    // values are the existing immutable authored geometry, not live navigation.
    internal sealed class AiCombatSupportCatalog
    {
        readonly MapSupport[] supports;
        internal readonly AiCombatSupportSelector Selector;
        internal readonly string SourceBinding;
        internal int Count=>supports.Length;
        internal readonly int BoundaryCutCapacity;
        internal MapSupport At(int i)=>supports[i];
        internal AiCombatSupportCatalog(AiCombatSupportSelector selector,string source,MapSupport[] transferred){Selector=selector;SourceBinding=source;supports=transferred;int cuts=2;foreach(var item in transferred)cuts=checked(cuts+2*(item.Bounds.Polygon?.Vertices.Count??4));BoundaryCutCapacity=cuts;}
        internal void WriteBinding(BinaryWriter w)
        {w.Write((int)Selector);WorldWire.String(w,SourceBinding);w.Write(Count);for(int i=0;i<Count;i++){var s=At(i);WorldWire.String(w,s.Id);WorldWire.String(w,s.SurfaceId);WorldWire.Write(w,s.Bounds);w.Write(s.IsBridge);w.Write(s.Height);WorldWire.Write(w,s.Gradient);WorldWire.Write(w,s.Origin);}}
        internal void CheckBinding(BinaryReader r){using(var stream=new PredicateBindingComparison(r))using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))WriteBinding(w);}
        internal sealed class Builder
        {
            IReadOnlyList<MapSupport> source;
            readonly MapSupport[] copy;
            readonly AiCombatSupportSelector selector;
            readonly string binding;
            int cursor;bool transferred;
            internal bool Ready=>cursor==copy.Length;
            internal Builder(IReadOnlyList<MapSupport> source,AiCombatSupportSelector selector,string binding){this.source=source;this.selector=selector;this.binding=binding;copy=new MapSupport[source?.Count??0];}
            internal int Advance(int grant)
            {if(grant<0)throw new ArgumentOutOfRangeException(nameof(grant));int used=0;while(used<grant&&cursor<copy.Length){var s=source[cursor];copy[cursor++]=new MapSupport(s.Id,s.Bounds,s.IsBridge,s.Height,s.Gradient,s.Origin,s.SurfaceId);used++;}return used;}
            internal AiCombatSupportCatalog Finish(){if(!Ready||transferred)throw new InvalidOperationException("Support snapshot pending or already transferred.");var result=new AiCombatSupportCatalog(selector,binding,copy);transferred=true;source=null;return result;}
        }
        // Exact concrete-type dispatch is FACTORY work only. It cannot silently
        // assign a first-support recipe to a subclass with changed semantics.
        internal static bool TryBegin(IPlayableTerrain terrain,string profileGeometryBinding,ref long allocationGrant,Func<long,bool> reserve,out Builder builder)
        {
            builder=null;if(profileGeometryBinding==null||profileGeometryBinding.Length>512)throw new ArgumentException("Invalid profile geometry binding.");var selector=AiCombatSupportSelector.Unsupported;IReadOnlyList<MapSupport> source=null;string typeName="none",terrainId="";int revision=0;
            if(terrain==null)selector=AiCombatSupportSelector.Flat;
            else{
                Type t=terrain.GetType();typeName=t.FullName;terrainId=terrain.Id;revision=terrain.Revision;if(typeName==null||typeName.Length>512||terrainId==null||terrainId.Length>512)throw new ArgumentException("Invalid terrain identity.");
                if(t==typeof(NereidaMap))selector=AiCombatSupportSelector.UniqueSemanticSurface;
                else if(t==typeof(ThreeCrossingsMap)||t==typeof(AntennaCityMap)||t==typeof(FoundryMap)||t==typeof(MossHighlandMap)||t==typeof(NullDockMap)||t==typeof(BasaltCrownMap)||t==typeof(WhiteMeridianMap)||t==typeof(CrimsonGroveMap)||t==typeof(AiTestMap)||t==typeof(ProvingGroundMap)||t==typeof(CampaignOpeningMap))selector=AiCombatSupportSelector.FirstDeclared;
                if(selector!=AiCombatSupportSelector.Unsupported)source=terrain.Supports;
            }
            int count=source?.Count??0;if(count>WorldWire.MaxItems)throw new ArgumentException("Support catalog too large.");
            // Bound both concatenations and the immutable catalog/array before birth.
            long bytes=checked(1024L+128L*count+4L*(profileGeometryBinding.Length+typeName.Length+terrainId.Length+32));
            if(allocationGrant<count||reserve==null||!reserve(bytes))return false;allocationGrant-=count;
            builder=new Builder(source,selector,profileGeometryBinding+":"+typeName+":"+terrainId+"@"+revision);return true;
        }
    }
    internal sealed class AiCombatMicroSupportWork
    {
        // Includes both bounded UTF-8 decode buffers and UTF-16 name strings
        // on cold read. The same conservative charge survives clone/cancel.
        internal const long RetainedBytes=16384;
        readonly AiCombatSupportCatalog catalog;
        readonly NavPoint point;
        readonly string typedSupport,typedSurface;
        bool Typed=>typedSurface!=null;
        bool Semantic=>Typed&&typedSupport==null;
        int support,edge;
        bool polygon,inside,ambiguous;
        int first=-1;
        internal bool Complete {get;private set;}
        internal bool Supported=>catalog.Selector!=AiCombatSupportSelector.Unsupported&&(!Typed||first>=0&&!ambiguous);
        internal double Height {get{if(!Complete||!Supported)throw new InvalidOperationException("Support height unavailable.");return first<0||ambiguous?0:catalog.At(first).HeightAt(point);}}
        internal bool MatchesPoint(NavPoint p,string expectedSupport=null,string expectedSurface=null)=>typedSupport==expectedSupport&&typedSurface==expectedSurface&&BitConverter.DoubleToInt64Bits(point.X)==BitConverter.DoubleToInt64Bits(p.X)&&BitConverter.DoubleToInt64Bits(point.Z)==BitConverter.DoubleToInt64Bits(p.Z);
        internal string ResolvedSupport{get{if(!Complete||!Supported||first<0)throw new InvalidOperationException("No concrete support selected.");return catalog.At(first).Id;}}
        internal bool HasResolvedSupport=>Complete&&Supported&&first>=0&&!ambiguous;
        internal string ResolvedSurface{get{if(!Complete||!Supported||first<0)throw new InvalidOperationException("No concrete support selected.");return catalog.At(first).SurfaceId;}}
        internal long Consumed {get;private set;}
        AiCombatMicroSupportWork(AiCombatSupportCatalog catalog,NavPoint point,string typedSupport=null,string typedSurface=null){this.catalog=catalog??throw new ArgumentNullException(nameof(catalog));if(!MotionExecutionBinding.Number(point.X)||!MotionExecutionBinding.Number(point.Z))throw new ArgumentException("Invalid support point.");this.point=point;this.typedSupport=typedSupport;this.typedSurface=typedSurface;}
        internal static bool TryCreate(AiCombatSupportCatalog catalog,NavPoint point,Func<long,bool> reserve,out AiCombatMicroSupportWork work)
        {work=null;if(reserve==null||!reserve(RetainedBytes))return false;work=new AiCombatMicroSupportWork(catalog,point);return true;}
        // Typed movement is bound to the exact support ID AND semantic surface.
        // Heights use native H+(x-OriginX)*GradientX+(z-OriginZ)*GradientZ order.
        // No normalized plane re-evaluation or ambiguous null->zero fallback.
        internal static bool TryCreateTyped(AiCombatSupportCatalog catalog,NavLocation point,string support,Func<long,bool> reserve,out AiCombatMicroSupportWork work)
        {work=null;MotionExecutionBinding.Name(support);MotionExecutionBinding.Name(point.SurfaceId);if(reserve==null||!reserve(RetainedBytes))return false;work=new AiCombatMicroSupportWork(catalog,point.Position,support,point.SurfaceId);return true;}
        internal static bool TryCreateSemantic(AiCombatSupportCatalog catalog,NavLocation point,Func<long,bool> reserve,out AiCombatMicroSupportWork work)
        {work=null;MotionExecutionBinding.Name(point.SurfaceId);if(reserve==null||!reserve(RetainedBytes))return false;work=new AiCombatMicroSupportWork(catalog,point.Position,null,point.SurfaceId);return true;}
        internal int Advance(int grant,Func<bool> admit=null)
        {if(grant<0)throw new ArgumentOutOfRangeException(nameof(grant));int used=0;while(used<grant&&!Complete){if(admit!=null&&!admit())break;Step();used++;Consumed++;}return used;}
        void Match()
        {if(first<0)first=support;else if(Semantic?!MotionExecutionBinding.Same(catalog.At(first).HeightAt(point),catalog.At(support).HeightAt(point)):catalog.At(first).SurfaceId!=catalog.At(support).SurfaceId)ambiguous=true;if(Typed&&!Semantic||!Typed&&catalog.Selector==AiCombatSupportSelector.FirstDeclared)Complete=true;else Next();}
        void Next(){support++;edge=0;polygon=inside=false;}
        void Step()
        {
            if(catalog.Selector==AiCombatSupportSelector.Flat||catalog.Selector==AiCombatSupportSelector.Unsupported||support==catalog.Count){Complete=true;return;}
            var candidate=catalog.At(support);if(Typed&&(typedSupport!=null&&candidate.Id!=typedSupport||candidate.SurfaceId!=typedSurface)){Next();return;}
            var bounds=candidate.Bounds;
            if(!polygon){
                if(point.X<bounds.MinX||point.X>bounds.MaxX||point.Z<bounds.MinZ||point.Z>bounds.MaxZ){Next();return;}
                if(bounds.Polygon==null){Match();return;}
                polygon=true;return;
            }
            var vertices=bounds.Polygon.Vertices;
            if(edge==vertices.Count){if(inside)Match();else Next();return;}
            var a=vertices[(edge+vertices.Count-1)%vertices.Count];var b=vertices[edge++];
            // Exactly the existing NavPolygon.Contains edge direction/epsilon.
            double x=b.X-a.X,z=b.Z-a.Z,t=Math.Max(0,Math.Min(1,((point.X-a.X)*x+(point.Z-a.Z)*z)/(x*x+z*z))),dx=point.X-(a.X+t*x),dz=point.Z-(a.Z+t*z);
            if(point.X>=Math.Min(a.X,b.X)-1e-9&&point.X<=Math.Max(a.X,b.X)+1e-9&&point.Z>=Math.Min(a.Z,b.Z)-1e-9&&point.Z<=Math.Max(a.Z,b.Z)+1e-9&&dx*dx+dz*dz<=1e-18){Match();return;}
            if((a.Z>point.Z)!=(b.Z>point.Z)&&point.X<(b.X-a.X)*(point.Z-a.Z)/(b.Z-a.Z)+a.X)inside=!inside;
        }
        internal void Write(BinaryWriter w)
        {w.Write(3);catalog.WriteBinding(w);w.Write(point.X);w.Write(point.Z);WorldWire.String(w,typedSupport);WorldWire.String(w,typedSurface);w.Write(support);w.Write(edge);w.Write(polygon);w.Write(inside);w.Write(ambiguous);w.Write(first);w.Write(Complete);w.Write(Consumed);}
        static string ReadName(BinaryReader r)
        {int count=r.ReadInt32();if(count==-1)return null;if(count<0||count>2048)throw new ArgumentException("Invalid bounded support name.");var bytes=r.ReadBytes(count);if(bytes.Length!=count)throw new EndOfStreamException();var name=System.Text.Encoding.UTF8.GetString(bytes);MotionExecutionBinding.Name(name);return name;}
        internal static AiCombatMicroSupportWork Read(BinaryReader r,AiCombatSupportCatalog catalog,Func<long,bool> reserve)
        {
            if(reserve==null||!reserve(RetainedBytes))throw new InvalidOperationException("Support cursor admission refused.");if(r.ReadInt32()!=3)throw new ArgumentException("Unknown support cursor.");catalog.CheckBinding(r);
            var at=new NavPoint(WorldWire.Number(r),WorldWire.Number(r));string typedSupport=ReadName(r),typedSurface=ReadName(r);if(typedSupport!=null&&typedSurface==null)throw new ArgumentException("Incomplete typed support binding.");if(typedSupport!=null)MotionExecutionBinding.Name(typedSupport);if(typedSurface!=null)MotionExecutionBinding.Name(typedSurface);
            var work=new AiCombatMicroSupportWork(catalog,at,typedSupport,typedSurface){support=r.ReadInt32(),edge=r.ReadInt32(),polygon=WorldWire.Boolean(r),inside=WorldWire.Boolean(r),ambiguous=WorldWire.Boolean(r),first=r.ReadInt32(),Complete=WorldWire.Boolean(r),Consumed=r.ReadInt64()};
            if(work.support<0||work.support>catalog.Count||work.first< -1||work.first>=catalog.Count||work.edge<0||work.Consumed<0||!work.polygon&&(work.edge!=0||work.inside)||work.ambiguous&&(!work.Semantic&&catalog.Selector!=AiCombatSupportSelector.UniqueSemanticSurface||work.first<0)||(work.Typed&&!work.Semantic||!work.Typed&&catalog.Selector==AiCombatSupportSelector.FirstDeclared)&&work.first>=0&&!work.Complete)throw new ArgumentException("Impossible support cursor.");
            if(work.polygon&&(work.support==catalog.Count||catalog.At(work.support).Bounds.Polygon==null||work.edge>catalog.At(work.support).Bounds.Polygon.Vertices.Count))throw new ArgumentException("Invalid support polygon cursor.");
            if(work.Complete&&work.Supported&&catalog.Selector!=AiCombatSupportSelector.Flat&&((work.Typed&&!work.Semantic||!work.Typed&&catalog.Selector==AiCombatSupportSelector.FirstDeclared)&&work.first>=0?work.first!=work.support:work.support!=catalog.Count))throw new ArgumentException("Incomplete support proof marked complete.");
            if(work.Typed&&work.first>=0&&(work.typedSupport!=null&&catalog.At(work.first).Id!=work.typedSupport||catalog.At(work.first).SurfaceId!=work.typedSurface))throw new ArgumentException("Foreign typed support result.");
            return work;
        }
        internal bool TryFork(Func<long,bool> reserve,out AiCombatMicroSupportWork fork){fork=null;if(reserve==null||!reserve(RetainedBytes))return false;fork=(AiCombatMicroSupportWork)MemberwiseClone();return true;}
    }
}
