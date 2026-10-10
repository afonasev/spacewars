using System;
namespace Spacewars.Simulation
{
    // Provider-issued region reference under the world's exact terrain binding.
    // XZ is never snapped; two layers at the same XZ remain distinct locations.
    public readonly struct NavLocation : IEquatable<NavLocation>
    {
        public const string FlatSurface="flat";
        public NavLocation(NavPoint position,string surfaceId)
        {
            if(string.IsNullOrWhiteSpace(surfaceId)||double.IsNaN(position.X)||double.IsInfinity(position.X)||double.IsNaN(position.Z)||double.IsInfinity(position.Z))throw new ArgumentException("Invalid navigation location.");
            Position=position;SurfaceId=surfaceId;
        }
        public NavPoint Position{get;} public string SurfaceId{get;}
        public bool Equals(NavLocation other)=>Position.Equals(other.Position)&&SurfaceId==other.SurfaceId;
        public override bool Equals(object other)=>other is NavLocation location&&Equals(location);
        public override int GetHashCode()=>Position.GetHashCode()^(SurfaceId?.GetHashCode()??0);
    }
    public sealed class NavSurfaceTransition
    {
        public NavSurfaceTransition(string id,string from,string to)
        {if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(from)||string.IsNullOrWhiteSpace(to)||from==to)throw new ArgumentException("Invalid surface transition.");Id=id;From=from;To=to;}
        public string Id{get;} public string From{get;} public string To{get;}
    }
}
