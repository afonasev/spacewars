using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Tests.PlayMode
{
    public sealed class MinimapSilhouetteTests
    {
        static readonly Color Own=new Color(.19f,.72f,.77f), Enemy=new Color(.93f,.34f,.23f);
        static readonly PlayableBuildingKind[] Kinds={PlayableBuildingKind.Headquarters,PlayableBuildingKind.Outpost,PlayableBuildingKind.Mine,PlayableBuildingKind.RelayTower};
        static Color Paint(PlayableOwner? owner)=>owner==PlayableOwner.Player?Own:Enemy;
        static PlayableMapMark[] Records(double extent,bool memory)
        {
            var result=new List<PlayableMapMark>();int id=1;
            for(int row=0;row<4;row++)for(int col=0;col<3;col++)
                result.Add(new PlayableMapMark(id++,new NavPoint((col-1)*extent*.65,extent*(.78-row*.35)),Kinds[row],memory?MapMarkState.Memory:MapMarkState.Ready,col==0?(PlayableOwner?)null:col==1?PlayableOwner.Player:PlayableOwner.Enemy));
            for(int col=0;col<3;col++)
            {
                var owner=col==0?(PlayableOwner?)null:col==1?PlayableOwner.Player:PlayableOwner.Enemy;
                var center=new NavPoint((col-1)*extent*.65,-extent*.72);
                result.Add(new PlayableMapMark(id++,center,PlayableBuildingKind.Headquarters,memory?MapMarkState.Memory:MapMarkState.Ready,owner));
                foreach(var delta in new[]{new NavPoint(-.13,.09),new NavPoint(.13,.09),new NavPoint(-.13,-.1),new NavPoint(.13,-.1)})
                    result.Add(new PlayableMapMark(id++,new NavPoint(center.X+delta.X*extent,center.Z+delta.Z*extent),PlayableBuildingKind.Factory,memory?MapMarkState.Memory:MapMarkState.Ready,owner));
            }
            return result.ToArray();
        }
        [UnityTest] public IEnumerator SolidMarkersKeepLastOwnerHueAndDenseAnchorsOnLightAndDarkMaps()
        {
            var output=Environment.GetEnvironmentVariable("MINIMAP_SILHOUETTE_EVIDENCE")??Path.GetFullPath("../.local/minimap-clear-silhouettes/captures");Directory.CreateDirectory(output);
            var host=new GameObject("Minimap silhouette matrix");var panel=ScriptableObject.CreateInstance<PanelSettings>();
            var target=new RenderTexture(1220,820,24);target.Create();var textures=new List<Texture2D>();var terrains=new List<PlayableMapTerrain>();
            try
            {
                var doc=host.AddComponent<UIDocument>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.scaleMode=PanelScaleMode.ConstantPixelSize;panel.targetTexture=target;doc.panelSettings=panel;
                var root=doc.rootVisualElement;root.style.backgroundColor=new Color(.04f,.065f,.085f);root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                Label Label(string title,int size=15){var label=new Label(title);label.style.fontSize=size;label.style.color=Color.white;return label;}
                var heading=Label("Чёткие силуэты · реальный Unity UI Toolkit · матрица отрисовки",23);heading.style.left=14;heading.style.top=8;heading.style.position=Position.Absolute;root.Add(heading);
                var note=Label("В каждой карте: нейтральная / своя / враг. Ряды: штаб, форпост, шахта, связь, плотная база.");note.style.position=Position.Absolute;note.style.left=14;note.style.top=43;root.Add(note);
                var profile=PlayableProfile.Default;
                var foundry=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new FoundryMap(new FoundryProfileData()));
                var meridian=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new WhiteMeridianMap());
                var mapElements=new List<PlayableMapSurface>();
                Texture2D Flat(Color color){var texture=new Texture2D(2,2);texture.SetPixels(new[]{color,color,color,color});texture.Apply();textures.Add(texture);return texture;}
                var dark=Flat(new Color(.12f,.19f,.2f));var light=Flat(new Color(.77f,.78f,.7f));
                Texture2D Terrain(PlayableProfile p){var terrain=new PlayableMapTerrain(p);terrain.ShowPublicTerrain();terrains.Add(terrain);return terrain.Texture;}
                var foundryTexture=Terrain(foundry);var meridianTexture=Terrain(meridian);
                void AddMap(string title,int x,int y,int size,PlayableProfile p,Texture2D texture,bool memory)
                {
                    var label=Label(title);label.style.position=Position.Absolute;label.style.left=x;label.style.top=y;root.Add(label);
                    var map=new PlayableMapSurface(p,texture){OwnerPaint=Paint};map.style.position=Position.Absolute;map.style.left=x;map.style.top=y+26;map.style.width=size;map.style.height=size;
                    map.SetPublic(Records(p.ArenaHalfExtent,memory),p.ArenaHalfExtent);root.Add(map);mapElements.Add(map);
                }
                AddMap("Тёмная · обзор",14,85,180,profile,dark,false);
                AddMap("Светлая · обзор",216,85,180,profile,light,false);
                AddMap("Тёмная · память",418,85,180,profile,dark,true);
                AddMap("Светлая · память",620,85,180,profile,light,true);
                AddMap("Литейная · 180 px",822,85,180,foundry,foundryTexture,false);
                AddMap("Меридиан · 180 px",1024,85,180,meridian,meridianTexture,false);
                AddMap("TAB · 360 px · Литейная",14,325,360,foundry,foundryTexture,false);
                AddMap("TAB · 360 px · память",402,325,360,meridian,meridianTexture,true);
                var legend=Label("Сплошная форма сохраняется в тумане.\nЦвет — последний известный владелец.\nСерый — нейтральная.\n\nСкрытый захват проверен отдельным\nRelayFogMapPresentationTests.\n\nТестовые записи на авторских подложках;\nэто не приёмка игрового матча.");legend.style.position=Position.Absolute;legend.style.left=794;legend.style.top=390;root.Add(legend);
                for(int i=0;i<10;i++)yield return null;
                var shot=Read(target);textures.Add(shot);File.WriteAllBytes(Path.Combine(output,"silhouette-matrix.png"),shot.EncodeToPNG());
                foreach(var map in mapElements)
                {
                    Assert.That(map.resolvedStyle.width,Is.EqualTo(map.resolvedStyle.height));
                    var bounds=map.worldBound;var crop=new Texture2D((int)bounds.width,(int)bounds.height,TextureFormat.RGB24,false);textures.Add(crop);
                    crop.SetPixels(shot.GetPixels((int)bounds.x,(int)(target.height-bounds.yMax),crop.width,crop.height));crop.Apply();File.WriteAllBytes(Path.Combine(output,"map-"+mapElements.IndexOf(map)+".png"),crop.EncodeToPNG());
                }
                // Verify the rendered output, rather than duplicating the painter's color math.
                // The outpost centers have no text: own stays cyan, enemy stays red even in memory.
                for(int mapIndex=0;mapIndex<4;mapIndex++)
                {
                    var bounds=mapElements[mapIndex].worldBound;
                    Color At(float u,float v)=>shot.GetPixel((int)(bounds.x+bounds.width*u),(int)(target.height-bounds.y-bounds.height*v));
                    var own=At(.5f,.285f);var enemy=At(.825f,.285f);var neutral=At(.175f,.285f);
                    Assert.That(own.g,Is.GreaterThan(own.r*2),"Rendered own marker must retain cyan hue, including in fog");
                    Assert.That(enemy.r,Is.GreaterThan(enemy.g*2),"Rendered enemy marker must retain red hue, including in fog");
                    Assert.That(Mathf.Max(neutral.r,neutral.g,neutral.b)-Mathf.Min(neutral.r,neutral.g,neutral.b),Is.LessThan(.2f),"Neutral stays gray");
                }
            }
            finally
            {
                panel.targetTexture=null;UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);target.Release();UnityEngine.Object.DestroyImmediate(target);
                foreach(var texture in textures)UnityEngine.Object.DestroyImmediate(texture);foreach(var terrain in terrains)terrain.Dispose();
            }
        }
        static Texture2D Read(RenderTexture target)
        {
            var previous=RenderTexture.active;RenderTexture.active=target;var shot=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try{shot.ReadPixels(new Rect(0,0,target.width,target.height),0,0);shot.Apply();return shot;}finally{RenderTexture.active=previous;}
        }
    }
}
