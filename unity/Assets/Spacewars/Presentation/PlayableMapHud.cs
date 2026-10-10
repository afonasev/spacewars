using System;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private PlayableMapTerrain mapTerrain;
        private PlayableMapSurface compactMap,tacticalMap;
        private VisualElement tacticalOverlay;
        private Label mapClock;
        private bool mapOpen;
        private void CreateMaps(VisualElement row)
        {
            mapTerrain=new PlayableMapTerrain(profile);
            var column=new VisualElement();column.style.width=(float)profile.MinimapCompactSize+30;column.style.flexShrink=0;column.style.marginRight=4;StyleRegion(column);column.name="tactical-minimap-frame";row.Add(column);
            compactMap=new PlayableMapSurface(profile,mapTerrain.Texture){OwnerPaint=owner=>matchSetup!=null?LobbyPaint(owner):owner==LocalOwner?new Color(.19f,.72f,.77f):owner==PlayableOwner.Enemy?new Color(.93f,.34f,.23f):Color.white};compactMap.style.width=(float)profile.MinimapCompactSize;compactMap.style.height=(float)profile.MinimapCompactSize;column.Add(compactMap);
            mapClock=new Label();mapClock.style.unityTextAlign=TextAnchor.MiddleCenter;column.Add(mapClock);
            tacticalOverlay=Panel();tacticalOverlay.style.left=0;tacticalOverlay.style.right=0;tacticalOverlay.style.top=90;tacticalOverlay.style.bottom=24;StyleRegion(tacticalOverlay);tacticalOverlay.style.alignItems=Align.Center;tacticalOverlay.style.justifyContent=Justify.Center;tacticalOverlay.style.display=DisplayStyle.None;root.Add(tacticalOverlay);
            tacticalMap=new PlayableMapSurface(profile,mapTerrain.Texture){OwnerPaint=owner=>matchSetup!=null?LobbyPaint(owner):owner==LocalOwner?new Color(.19f,.72f,.77f):owner==PlayableOwner.Enemy?new Color(.93f,.34f,.23f):Color.white};tacticalOverlay.Add(tacticalMap);
            var hint=new Label("ЛКМ — камера · рамка — танки · ПКМ — движение / сбор · A — движение с атакой · Tab — закрыть");hint.style.fontSize=14;tacticalOverlay.Add(hint);
            tacticalOverlay.RegisterCallback<GeometryChangedEvent>(_=>{float size=Mathf.Min((float)profile.MinimapTacticalSize,tacticalOverlay.contentRect.width,tacticalOverlay.contentRect.height-hint.resolvedStyle.height);tacticalMap.style.width=size;tacticalMap.style.height=size;});
        }
        private Vector2 GlobalPanelPoint(Vector2 screen)=>RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(screen.x,Screen.height-screen.y));
        private Vector2 PanelPoint(Vector2 screen)=>GlobalPanelPoint(screen)-root.worldBound.position;
        private int MapAt(Vector2 screen)
        {
            if(root?.panel==null)return 0;var point=GlobalPanelPoint(screen);
            if(mapOpen)return tacticalMap.worldBound.Contains(point)?2:3;
            return CompactInputMap.worldBound.Contains(point)?1:0;
        }
        private NavPoint MapGround(int kind,Vector2 screen){var surface=kind==2?tacticalMap:CompactInputMap;return surface.Ground(surface.WorldToLocal(GlobalPanelPoint(screen)));}
        private void CloseMap(){mapOpen=false;if(tacticalOverlay!=null)tacticalOverlay.style.display=DisplayStyle.None;}
        private void ToggleMap(){mapOpen=!mapOpen;tacticalOverlay.style.display=mapOpen?DisplayStyle.Flex:DisplayStyle.None;root.Focus();Record("tactical="+mapOpen);}
        private void MapSelect(int kind,Vector2 a,Vector2 b,bool shift)
        {
            if(view==null)return;confirmSaleBuilding=0;root.Focus();
            if(matchSetup?.Spectator==true)
            {
                var destination=MapGround(kind,b);var center=Ground(SeatScreenCenter);
                cameraView.transform.position+=new Vector3((float)(destination.X-center.X),0,(float)(destination.Z-center.Z));
                Record("spectator map focus="+destination.X+","+destination.Z);if(kind==2)CloseMap();return;
            }
            if(kind==2&&(b-a).magnitude>profile.SelectionDragPixels)
            {
                runtime?.RecordHumanAction(LocalOwnerId);if(!shift)selection.Clear();else selection.RemoveWhere(id=>!view.Entities.Any(e=>e.Id==id&&e.Owner==view.Owner));foreach(int id in PlayableMapView.SelectOwnUnits(view,MapGround(kind,a),MapGround(kind,b)))selection.Add(id);
                SelectionMarkerEvent();selectedSite=selectedSlot=0;keyboardGroups.Observe(selection);Record("map box shift="+shift+" ids="+string.Join(",",selection));
            }
            else
            {
                var destination=MapGround(kind,b);var center=Ground(SeatScreenCenter);
                cameraView.transform.position+=new Vector3((float)(destination.X-center.X),0,(float)(destination.Z-center.Z));
                Record("map focus="+destination.X+","+destination.Z);if(kind==2)CloseMap();
            }
        }
        private void MapOrder(int kind,Vector2 screen,bool attack,bool append)
        {
            var point=MapGround(kind,screen);bool factory=false;
            foreach(var b in view.Buildings)if(selection.Count==1&&selection.Contains(b.Id)&&b.Owner==LocalOwner&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready)factory=true;
            Submit(!attack&&factory?PlayableCommandKind.SetRally:attack?PlayableCommandKind.AttackMove:PlayableCommandKind.Move,null,point,mode:append&&!factory?PlayableOrderMode.Append:PlayableOrderMode.Replace);
            Record("map order ground-only attack="+attack);root.Focus();
        }
        private void UpdateMaps()
        {
            if(matchSetup?.Spectator==true)
            {
                mapTerrain.ShowPublicTerrain();var marks=PlayableMapView.Marks(view);compactMap.SetPublic(marks,profile.ArenaHalfExtent);tacticalMap.SetPublic(marks,profile.ArenaHalfExtent);compactMap.SetOrderMarkers(Array.Empty<PlayableQueueMarker>());tacticalMap.SetOrderMarkers(Array.Empty<PlayableQueueMarker>());
                long publicSeconds=view.Tick/30;mapClock.text=(publicSeconds/60).ToString("D2")+":"+(publicSeconds%60).ToString("D2");if(paused||restarting||view.Outcome!=PlayableMatchOutcome.Playing)CloseMap();return;
            }
            if(view.Vision==null)return;mapTerrain.Update(world.Fog);
            var r=cameraView.pixelRect;var corners=new[]{Ground(new Vector2(r.xMin,r.yMin)),Ground(new Vector2(r.xMax,r.yMin)),Ground(new Vector2(r.xMax,r.yMax)),Ground(new Vector2(r.xMin,r.yMax))};
            compactMap.Set(view,selection,corners);if(mapOpen)tacticalMap.Set(view,selection,corners);
            // Native runtime tick frequency is the fixed 30 Hz authority contract.
            long seconds=view.Tick/30;mapClock.text=(seconds/60).ToString("D2")+":"+(seconds%60).ToString("D2");
            if(paused||restarting||view.Outcome!=PlayableMatchOutcome.Playing)CloseMap();
        }
    }
}
