using Spacewars.Simulation;
namespace Spacewars.Presentation
{
    public enum GroundTrailSurfaceKind{Hard,Soil,Grass,Sand,Salt}
    public static class GroundTrailSurface
    {
        // Authored semantic materials, never inferred from a pixel's brightness.
        public static GroundTrailSurfaceKind At(IPlayableTerrain map,NavPoint p)
        {
            if(map==null)return GroundTrailSurfaceKind.Hard;
            foreach(var solid in map.Solids)if(solid.Contains(p))return GroundTrailSurfaceKind.Hard;
            // Bridge decks can overlap the bank support: hard coverage wins at the seam.
            foreach(var deck in map.Supports)if(deck.IsBridge&&deck.Contains(p))return GroundTrailSurfaceKind.Hard;
            var support=map.SupportAt(p);if(support==null||support.IsBridge)return GroundTrailSurfaceKind.Hard;
            if(map is MossHighlandMap moss){string material="moss-ground";foreach(var patch in moss.Materials)if(patch.Bounds.Contains(p))material=patch.Material;
                if(material=="moss-ground")return GroundTrailSurfaceKind.Grass;
                if(material=="damp-soil")return GroundTrailSurfaceKind.Soil;
                if(material=="scree"||material=="pebbles")return GroundTrailSurfaceKind.Sand;
                return GroundTrailSurfaceKind.Hard;}
            if(map is BasaltCrownMap basalt){string material="mountain.base-dark";foreach(var patch in basalt.Materials)if(patch.Bounds.Contains(p))material=patch.Material;
                return material=="mountain.transition-scree"||material=="archipelago-coastal.transition-basalt-sand"?GroundTrailSurfaceKind.Sand:GroundTrailSurfaceKind.Hard;}
            if(map is WhiteMeridianMap)return GroundTrailSurfaceKind.Salt;
            if(map is ThreeCrossingsMap)return support.Id.StartsWith("platform-")?GroundTrailSurfaceKind.Hard:GroundTrailSurfaceKind.Soil;
            if(map is CampaignOpeningMap||map is AiTestMap)return GroundTrailSurfaceKind.Sand;
            // Foundry is solid volcanic rock / industrial decks, including its roads.
            return GroundTrailSurfaceKind.Hard;
        }
    }
}
