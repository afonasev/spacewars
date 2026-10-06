using System;
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
            var column=new VisualElement();column.style.width=(float)profile.MinimapCompactSize+20;column.style.flexShrink=0;column.style.marginRight=4;StyleRegion(column);column.Add(Section("ТАКТИЧЕСКАЯ КАРТА"));row.Add(column);
            compactMap=new PlayableMapSurface(profile,mapTerrain.Texture){OwnerPaint=owner=>matchSetup!=null?LobbyPaint(owner):owner==PlayableOwner.Player?new Color(.19f,.72f,.77f):owner==PlayableOwner.Enemy?new Color(.93f,.34f,.23f):Color.white};compactMap.style.width=(float)profile.MinimapCompactSize;compactMap.style.height=(float)profile.MinimapCompactSize;column.Add(compactMap);
            mapClock=new Label();mapClock.style.unityTextAlign=TextAnchor.MiddleCenter;column.Add(mapClock);
            tacticalOverlay=Panel();tacticalOverlay.style.left=0;tacticalOverlay.style.right=0;tacticalOverlay.style.top=80;tacticalOverlay.style.bottom=240;tacticalOverlay.style.alignItems=Align.Center;tacticalOverlay.style.justifyContent=Justify.Center;tacticalOverlay.style.display=DisplayStyle.None;root.Add(tacticalOverlay);
            tacticalMap=new PlayableMapSurface(profile,mapTerrain.Texture){OwnerPaint=owner=>matchSetup!=null?LobbyPaint(owner):owner==PlayableOwner.Player?new Color(.19f,.72f,.77f):owner==PlayableOwner.Enemy?new Color(.93f,.34f,.23f):Color.white};tacticalOverlay.Add(tacticalMap);
            var hint=new Label("ЛКМ — камера · рамка — танки · ПКМ — движение / сбор · A — движение с атакой · Tab — закрыть");hint.style.fontSize=14;tacticalOverlay.Add(hint);
            tacticalOverlay.RegisterCallback<GeometryChangedEvent>(_=>{float size=Mathf.Min((float)profile.MinimapTacticalSize,tacticalOverlay.contentRect.width,tacticalOverlay.contentRect.height-hint.resolvedStyle.height);tacticalMap.style.width=size;tacticalMap.style.height=size;});
        }
        private Vector2 PanelPoint(Vector2 screen)=>RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(screen.x,Screen.height-screen.y));
        private int MapAt(Vector2 screen)
        {
            if(root?.panel==null)return 0;var point=PanelPoint(screen);
            if(mapOpen)return tacticalMap.worldBound.Contains(point)?2:3;
            return compactMap.worldBound.Contains(point)?1:0;
        }
        private NavPoint MapGround(int kind,Vector2 screen){var surface=kind==2?tacticalMap:compactMap;return surface.Ground(surface.WorldToLocal(PanelPoint(screen)));}
        private void CloseMap(){mapOpen=false;if(tacticalOverlay!=null)tacticalOverlay.style.display=DisplayStyle.None;}
        private void ToggleMap(){mapOpen=!mapOpen;tacticalOverlay.style.display=mapOpen?DisplayStyle.Flex:DisplayStyle.None;root.Focus();Record("tactical="+mapOpen);}
        private void MapSelect(int kind,Vector2 a,Vector2 b,bool shift)
        {
            if(view==null)return;confirmSaleBuilding=0;root.Focus();
            if(kind==2&&(b-a).magnitude>profile.SelectionDragPixels)
            {
                if(!shift)selection.Clear();foreach(int id in PlayableMapView.SelectOwnUnits(view,MapGround(kind,a),MapGround(kind,b)))selection.Add(id);
                selectedSite=selectedSlot=0;Record("map box shift="+shift+" ids="+string.Join(",",selection));
            }
            else
            {
                var destination=MapGround(kind,b);var center=Ground(new Vector2(Screen.width/2f,Screen.height/2f));
                cameraView.transform.position+=new Vector3((float)(destination.X-center.X),0,(float)(destination.Z-center.Z));
                Record("map focus="+destination.X+","+destination.Z);if(kind==2)CloseMap();
            }
        }
        private void MapOrder(int kind,Vector2 screen,bool attack)
        {
            var point=MapGround(kind,screen);bool factory=false;
            foreach(var b in view.Buildings)if(selection.Count==1&&selection.Contains(b.Id)&&b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready)factory=true;
            Submit(!attack&&factory?PlayableCommandKind.SetRally:attack?PlayableCommandKind.AttackMove:PlayableCommandKind.Move,null,point);
            Record("map order ground-only attack="+attack);root.Focus();
        }
        private void UpdateMaps()
        {
            if(view.Vision==null)return;mapTerrain.Update(world.Fog);
            var corners=new[]{Ground(new Vector2(0,0)),Ground(new Vector2(Screen.width,0)),Ground(new Vector2(Screen.width,Screen.height)),Ground(new Vector2(0,Screen.height))};
            compactMap.Set(view,selection,corners);if(mapOpen)tacticalMap.Set(view,selection,corners);
            // Native runtime tick frequency is the fixed 30 Hz authority contract.
            long seconds=view.Tick/30;mapClock.text=(seconds/60).ToString("D2")+":"+(seconds%60).ToString("D2")+"  ·  Tab карта";
            if(paused||restarting||view.Outcome!=PlayableMatchOutcome.Playing)CloseMap();
        }
    }
}
