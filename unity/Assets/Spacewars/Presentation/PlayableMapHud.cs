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
        private bool mapOpen,tacticalHudOpen;
        private int tacticalTopIndex,tacticalArmyIndex;
        private void CreateMaps(VisualElement row)
        {
            mapTerrain=mapTerrain??new PlayableMapTerrain(profile);
            var column=new VisualElement();column.style.width=(float)profile.MinimapCompactSize+30;column.style.flexShrink=0;column.style.marginRight=4;StyleRegion(column);column.name="tactical-minimap-frame";row.Add(column);
            compactMap=new PlayableMapSurface(profile,mapTerrain.Texture){OwnerPaint=owner=>matchSetup!=null||campaignConfiguration!=null?LobbyPaint(owner):owner==LocalOwner?new Color(.19f,.72f,.77f):owner==PlayableOwner.Enemy?new Color(.93f,.34f,.23f):Color.white};compactMap.style.width=(float)profile.MinimapCompactSize;compactMap.style.height=(float)profile.MinimapCompactSize;column.Add(compactMap);
            mapClock=new Label();mapClock.style.unityTextAlign=TextAnchor.MiddleCenter;column.Add(mapClock);
            // Equal inset pairs center the panel without changing the existing usable map area.
            tacticalOverlay=Panel();tacticalOverlay.name="tactical-screen";StyleRegion(tacticalOverlay);tacticalOverlay.style.left=tacticalOverlay.style.right=tacticalOverlay.style.top=tacticalOverlay.style.bottom=0;tacticalOverlay.style.paddingLeft=tacticalOverlay.style.paddingRight=(24+140)*.5f;tacticalOverlay.style.paddingTop=tacticalOverlay.style.paddingBottom=(74+24)*.5f;tacticalOverlay.style.alignItems=Align.Center;tacticalOverlay.style.justifyContent=Justify.Center;tacticalOverlay.style.display=DisplayStyle.None;root.Add(tacticalOverlay);
            var mapFrame=new VisualElement{name="tactical-map-background"};mapFrame.style.flexShrink=0;
            mapFrame.style.backgroundColor=new Color(.025f,.05f,.065f,1);mapFrame.style.paddingLeft=mapFrame.style.paddingRight=mapFrame.style.paddingTop=mapFrame.style.paddingBottom=8;
            mapFrame.style.borderLeftWidth=mapFrame.style.borderRightWidth=mapFrame.style.borderTopWidth=mapFrame.style.borderBottomWidth=2;
            var mapEdge=new Color(Cyan.r,Cyan.g,Cyan.b,1);mapFrame.style.borderLeftColor=mapFrame.style.borderRightColor=mapFrame.style.borderTopColor=mapFrame.style.borderBottomColor=mapEdge;tacticalOverlay.Add(mapFrame);
            tacticalMap=new PlayableMapSurface(profile,mapTerrain.Texture){OwnerPaint=owner=>matchSetup!=null||campaignConfiguration!=null?LobbyPaint(owner):owner==LocalOwner?new Color(.19f,.72f,.77f):owner==PlayableOwner.Enemy?new Color(.93f,.34f,.23f):Color.white};mapFrame.Add(tacticalMap);
            tacticalOverlay.RegisterCallback<GeometryChangedEvent>(_=>LayoutTacticalMap());
        }
        private void LayoutTacticalMap()
        {
            float size=Mathf.Min((float)profile.MinimapTacticalSize,Mathf.Max(0,tacticalOverlay.contentRect.width-(campaignConfiguration!=null?320:20)),Mathf.Max(0,tacticalOverlay.contentRect.height-20));
            if(campaignConfiguration==null&&mapOpen&&armyComposition!=null&&armyComposition.resolvedStyle.display!=DisplayStyle.None&&armyComposition.worldBound.width>0)
            {
                var center=root.worldBound.center;var hud=armyComposition.worldBound;
                // A centered square clears a corner HUD when either axis has enough room.
                float clearance=Mathf.Max(center.x-hud.xMax,hud.xMin-center.x,center.y-hud.yMax,hud.yMin-center.y);
                size=Mathf.Min(size,Mathf.Max(0,clearance*2-20));
            }
            tacticalMap.style.width=size;tacticalMap.style.height=size;
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
        private void CloseMap(){mapOpen=false;if(tacticalOverlay!=null)tacticalOverlay.style.display=DisplayStyle.None;SyncTacticalMapHud();}
        private void ToggleMap(){mapOpen=!mapOpen;if(mapOpen&&view?.Vision!=null)UpdateMaps();if(ProvingPreparation){CloseProvingConstruction();if(mapOpen)tacticalOverlay.BringToFront();}if(battleRally)battleGestures.SetMode(mapOpen?"rallyMap":"rallyTarget");tacticalOverlay.style.display=mapOpen?DisplayStyle.Flex:DisplayStyle.None;root.Focus();SyncTacticalMapHud();Record("tactical="+mapOpen);}
        private void SyncTacticalMapHud()
        {
            if(tacticalOverlay==null)return;
            compactMap.parent.style.display=mapOpen||(localCoordinator??this).sharedMapFrame!=null?DisplayStyle.None:DisplayStyle.Flex;
            if(mapOpen)
            {
                if(!tacticalHudOpen){tacticalTopIndex=root.IndexOf(top);tacticalArmyIndex=root.IndexOf(armyComposition);}
                bottom.style.display=DisplayStyle.None;battleRing.style.display=DisplayStyle.None;
                top.BringToFront();UpdateArmyCompositionCounts();ShowArmyComposition(false);armyComposition.BringToFront();LayoutTacticalMap();
            }
            else if(tacticalHudOpen)
            {
                top.RemoveFromHierarchy();armyComposition.RemoveFromHierarchy();
                // Restore earlier siblings first: removing both elements shrinks the valid index range.
                foreach(var saved in new[]{(element:top,index:tacticalTopIndex),(element:armyComposition,index:tacticalArmyIndex)}.OrderBy(item=>item.index))
                    root.Insert(Mathf.Clamp(saved.index,0,root.childCount),saved.element);
                bottom.style.display=!inLobby&&!preparing?DisplayStyle.Flex:DisplayStyle.None;
                UpdateArmyCompositionCounts();ShowArmyComposition(armyCompositionHovered||root.focusController?.focusedElement==hudFocusButton);
            }
            tacticalHudOpen=mapOpen;
            var coordinator=localCoordinator??this;
            if(coordinator.sharedMapFrame!=null)coordinator.sharedMapFrame.style.display=coordinator.paused||coordinator.ResultsVisible||coordinator.inLobby||coordinator.localPresentations.Any(seat=>seat.mapOpen)?DisplayStyle.None:DisplayStyle.Flex;
        }
        private void PanMap(int kind,Vector2 screen)
        {
            if(BotCameraLocked||view==null||cameraView==null)return;
            var destination=MapGround(kind,screen);var center=Ground(SeatScreenCenter);
            cameraView.transform.position+=new Vector3((float)(destination.X-center.X),0,(float)(destination.Z-center.Z));
        }
        private void MapSelect(int kind,Vector2 a,Vector2 b,bool shift)
        {
            if(view==null)return;if(scanTargeting){ConfirmScan(MapGround(kind,b),kind==2);return;}confirmSaleBuilding=0;root.Focus();
            if(matchSetup?.Spectator==true)
            {
                if(BotCameraLocked)return;
                var destination=MapGround(kind,b);var center=Ground(SeatScreenCenter);
                cameraView.transform.position+=new Vector3((float)(destination.X-center.X),0,(float)(destination.Z-center.Z));
                Record("spectator map focus="+destination.X+","+destination.Z);if(kind==2)CloseMap();return;
            }
            if(ProvingPreparation){PanMap(kind,b);if(kind==2)CloseMap();return;}
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
            var point=MapGround(kind,screen);if(scanTargeting){ConfirmScan(point,kind==2);return;}var producer=SelectedRallyProducer();
            if(battleRally)CompleteRallyPlacement(point,battleController);
            else if(producer!=null)Submit(PlayableCommandKind.SetRally,new[]{producer.Id},point);
            else Submit(attack?PlayableCommandKind.AttackMove:PlayableCommandKind.Move,null,point,mode:append?PlayableOrderMode.Append:PlayableOrderMode.Replace);
            Record("map order ground-only attack="+attack);root.Focus();
        }
        private void UpdateMaps()
        {
            if(view.Vision==null)return;
            // Each split-screen compact map is hidden behind the shared union map.
            // Its private terrain/markers are needed again immediately when Tab opens.
            if((localCoordinator??this).sharedMapFrame!=null&&!mapOpen){
                if(paused&&!ProvingPreparation||restarting||view.Outcome!=PlayableMatchOutcome.Playing)CloseMap();
                SyncTacticalMapHud();return;
            }
            mapTerrain.Update(world.Fog);
            var r=cameraView.pixelRect;var corners=new[]{Ground(new Vector2(r.xMin,r.yMin)),Ground(new Vector2(r.xMax,r.yMin)),Ground(new Vector2(r.xMax,r.yMax)),Ground(new Vector2(r.xMin,r.yMax))};
            compactMap.Set(view,selection,corners);if(mapOpen)tacticalMap.Set(view,selection,corners);
            if(spectatorMode){compactMap.SetOrderMarkers(Array.Empty<PlayableQueueMarker>());tacticalMap.SetOrderMarkers(Array.Empty<PlayableQueueMarker>());}
            // Native runtime tick frequency is the fixed 30 Hz authority contract.
            long seconds=view.Tick/30;mapClock.text=(seconds/60).ToString("D2")+":"+(seconds%60).ToString("D2");
            if(paused&&!ProvingPreparation||restarting||view.Outcome!=PlayableMatchOutcome.Playing)CloseMap();
            SyncTacticalMapHud();
        }
    }
}
