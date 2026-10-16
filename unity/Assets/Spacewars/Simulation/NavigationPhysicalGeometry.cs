using System;

namespace Spacewars.Simulation
{
    internal static class NavPhysicalGeometryValue
    {
        internal static double Number(NavPhysicalBodyReader reader)
        {double value=reader.Input.ReadDouble();if(double.IsNaN(value)||double.IsInfinity(value))throw new ArgumentException("Invalid physical geometry number.");return value;}
        internal static NavPoint Point(NavPhysicalBodyReader reader)=>new NavPoint(Number(reader),Number(reader));
        internal static void Write(NavPhysicalBodyWriter writer,NavObstacle value)
        {
            writer.Output.Write(value.MinX);writer.Output.Write(value.MinZ);writer.Output.Write(value.MaxX);writer.Output.Write(value.MaxZ);
            writer.Output.Write(value.CircleRadius);writer.Output.Write(value.Bottom);writer.Output.Write(value.Top);writer.Edge(value.Polygon?.StorageRoot);
            if(value.Polygon!=null&&value.Polygon.StorageRoot==null)throw new ArgumentException("Unowned physical polygon payload.");
        }
        internal static NavObstacle Read(NavPhysicalBodyReader reader)
        {
            double minX=Number(reader),minZ=Number(reader),maxX=Number(reader),maxZ=Number(reader),radius=Number(reader),bottom=Number(reader),top=reader.Input.ReadDouble();
            if(double.IsInfinity(top)||minX>maxX||minZ>maxZ)throw new ArgumentException("Invalid physical obstacle bounds.");
            var root=reader.Edge<NavPolygon.OwnedBuffers>();
            if(root!=null){
                var polygon=root.Facade;if(polygon==null||radius!=0||minX!=polygon.MinX||minZ!=polygon.MinZ||maxX!=polygon.MaxX||maxZ!=polygon.MaxZ)throw new ArgumentException("Physical polygon bounds contradiction.");
                return new NavObstacle(polygon,bottom,top);
            }
            if(bottom!=0||!double.IsNaN(top)||radius<0)throw new ArgumentException("Invalid physical obstacle shape tags.");
            return radius==0?new NavObstacle(minX,minZ,maxX,maxZ):NavObstacle.RestoreCircle(minX,minZ,maxX,maxZ,radius);
        }
    }
    public sealed partial class NavPolygon
    {
        private NavPolygon(OwnedBuffers storage,double minX,double maxX,double minZ,double maxZ)
        {owned=storage;vertices=storage.Points.ReadOnlyBacking;triangles=storage.Indices.ReadOnlyBacking;Vertices=storage.Points;Triangles=storage.Indices;MinX=minX;MaxX=maxX;MinZ=minZ;MaxZ=maxZ;}
        internal sealed partial class OwnedBuffers : INavPhysicalBody
        {
            internal override bool IsImmutableCaptureRoot=>Facade!=null;
            public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Polygon);
            public void WritePhysicalBody(NavPhysicalBodyWriter writer)
            {if(Facade==null)throw new ArgumentException("Missing physical polygon facade.");writer.Output.Write(Facade.MinX);writer.Output.Write(Facade.MaxX);writer.Output.Write(Facade.MinZ);writer.Output.Write(Facade.MaxZ);writer.Edge(Points);writer.Edge(Indices);}
            internal static OwnedBuffers ReadPhysicalBody(NavPhysicalBodyReader reader)
            {
                double minX=NavPhysicalGeometryValue.Number(reader),maxX=NavPhysicalGeometryValue.Number(reader),minZ=NavPhysicalGeometryValue.Number(reader),maxZ=NavPhysicalGeometryValue.Number(reader);
                var points=reader.Edge<NavRetainedArray<NavPoint>>();var indices=reader.Edge<NavRetainedArray<int>>();
                if(points==null||indices==null||points.Count<3||(long)indices.Count!=3L*(points.Count-2))throw new ArgumentException("Physical polygon buffer shape contradiction.");
                double actualMinX=double.PositiveInfinity,actualMaxX=double.NegativeInfinity,actualMinZ=double.PositiveInfinity,actualMaxZ=double.NegativeInfinity,area=0;
                for(int n=0;n<points.Count;n++){
                    var a=points[n];var b=points[(n+1)%points.Count];if(Distance(a,b)<Epsilon)throw new ArgumentException("Zero physical polygon edge.");
                    actualMinX=Math.Min(actualMinX,a.X);actualMaxX=Math.Max(actualMaxX,a.X);actualMinZ=Math.Min(actualMinZ,a.Z);actualMaxZ=Math.Max(actualMaxZ,a.Z);area+=a.X*b.Z-b.X*a.Z;
                    for(int j=n+2;j<points.Count;j++){if(n==0&&j==points.Count-1)continue;if(Intersects(a,b,points[j],points[(j+1)%points.Count]))throw new ArgumentException("Self-intersecting physical polygon.");}
                }
                if(area<Epsilon||minX!=actualMinX||maxX!=actualMaxX||minZ!=actualMinZ||maxZ!=actualMaxZ)throw new ArgumentException("Physical polygon orientation/bounds contradiction.");
                // Validate the original deterministic ear sequence against the
                // retained raw indices; never replace it with rebuilt vectors.
                using(var scratch=NavStorageScratch.Reserve(reader.Reservation,64L+points.Count)){
                    var removed=new bool[points.Count];int remaining=points.Count,cursor=0;
                    int Previous(int n){do{n=(n+points.Count-1)%points.Count;}while(removed[n]);return n;}
                    int Next(int n){do{n=(n+1)%points.Count;}while(removed[n]);return n;}
                    while(remaining>3){bool clipped=false;
                        for(int b=0;b<points.Count;b++){
                            if(removed[b])continue;int a=Previous(b),c=Next(b);if(Cross(points[a],points[b],points[c])<=Epsilon)continue;
                            bool occupied=false;for(int q=0;q<points.Count;q++)if(!removed[q]&&q!=a&&q!=b&&q!=c&&InTriangle(points[q],points[a],points[b],points[c])){occupied=true;break;}
                            if(occupied)continue;
                            if(indices[cursor++]!=a||indices[cursor++]!=b||indices[cursor++]!=c)throw new ArgumentException("Physical polygon triangulation contradiction.");
                            removed[b]=true;remaining--;clipped=true;break;
                        }
                        if(!clipped)throw new ArgumentException("Invalid physical polygon triangulation.");
                    }
                    for(int n=0;n<points.Count;n++)if(!removed[n]&&indices[cursor++]!=n)throw new ArgumentException("Physical polygon final triangle contradiction.");
                }
                using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                    allocation.Reserve(192);var result=allocation.Register(new OwnedBuffers());result.RequireEdge(points);result.RequireEdge(indices);points.Retain();result.Points=points;indices.Retain();result.Indices=indices;
                    result.Facade=new NavPolygon(result,minX,maxX,minZ,maxZ);return result;
                }
            }
        }
    }
    public sealed partial class NavGeometry
    {
        private NavGeometry(OwnedBuffers storage,double halfExtent,int revision)
        {owned=storage;HalfExtent=halfExtent;Revision=revision;obstacles=storage.Obstacles.ReadOnlyBacking;obstacleView=storage.Obstacles;}
        internal sealed partial class OwnedBuffers : INavPhysicalBody
        {
            internal override bool IsImmutableCaptureRoot=>Facade!=null;
            public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Geometry);
            public void WritePhysicalBody(NavPhysicalBodyWriter writer)
            {if(Facade==null)throw new ArgumentException("Missing physical geometry facade.");writer.Output.Write(Facade.HalfExtent);writer.Output.Write(Facade.Revision);writer.Edge(Obstacles);}
            internal static OwnedBuffers ReadPhysicalBody(NavPhysicalBodyReader reader)
            {
                double extent=NavPhysicalGeometryValue.Number(reader);int revision=reader.Input.ReadInt32();
                if(extent<=0||revision<1)throw new ArgumentException("Invalid physical geometry identity.");var obstacles=reader.Edge<NavRetainedArray<NavObstacle>>();if(obstacles==null)throw new ArgumentException("Missing physical obstacle array.");
                using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                    allocation.Reserve(192);var result=allocation.Register(new OwnedBuffers());result.RequireEdge(obstacles);obstacles.Retain();result.Obstacles=obstacles;result.Facade=new NavGeometry(result,extent,revision);return result;
                }
            }
        }
    }
}
