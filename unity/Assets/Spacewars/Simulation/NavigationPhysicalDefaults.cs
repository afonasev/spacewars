using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    internal static class NavPhysicalDefaults
    {
        internal static void DirectoryCapacity(int capacity,int count)
        {if(capacity<count||capacity!=0&&(capacity<4||(capacity&(capacity-1))!=0))throw new ArgumentException("Invalid retained physical directory capacity.");}
        private static bool Name(NavNameReference name)=>name.Owner==null&&name.Role==NavNameRole.None&&name.Index==0;
        private static bool Location(NavLocation value)=>value.SurfaceId==null&&value.Position.Equals(default(NavPoint))&&Name(value.NameReference);
        private static bool Arc(NavGraphArc value)=>value.From==0&&value.To==0&&value.Cost==0&&value.Kind==NavGraphArcKind.Surface&&value.PortalId==null&&value.Trace==null&&Name(value.NameReference);
        internal static bool IsDefault<T>(T value)
        {
            if(typeof(T)==typeof(NavLocation))return Location((NavLocation)(object)value);
            if(typeof(T)==typeof(NavGraphNode)){var node=(NavGraphNode)(object)value;return node.Id==0&&node.Component==0&&Location(node.Location);}
            if(typeof(T)==typeof(NavGraphArc))return Arc((NavGraphArc)(object)value);
            if(typeof(T)==typeof(NavGridCoordinate)){var grid=(NavGridCoordinate)(object)value;return grid.Item1==null&&grid.Item2==0&&grid.Item3==0&&Name(grid.NameReference);}
            if(typeof(T)==typeof(NavNameKey)){var key=(NavNameKey)(object)value;return key.Value==null&&Name(key.Reference);}
            if(typeof(T)==typeof(NavNameReference))return Name((NavNameReference)(object)value);
            if(typeof(T)==typeof(NavPackedFieldCell)){var cell=(NavPackedFieldCell)(object)value;return cell.Distance==0&&cell.Flags==0&&Arc(cell.Next);}
            return EqualityComparer<T>.Default.Equals(value,default);
        }
    }
}
