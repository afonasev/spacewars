using System;
using System.IO;
using System.Linq;
using System.Collections;
using NUnit.Framework;
using Spacewars.Input;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed class KeyboardMouseControlTests
    {
        static KeyboardSelectable Unit(int id,double x=0,double z=0,bool own=true)=>new KeyboardSelectable(id,0,own,false,x,z);
        static KeyboardSelectable Building(int id,bool own=true)=>new KeyboardSelectable(id,0,own,true,20,30);
        [Test] public void AssignReplacesRecallPrunesAndEmptyAssignmentClears()
        {
            var g=new KeyboardControlGroups();var entities=new[]{Unit(1),Unit(2),Unit(3),Unit(9,own:false),Building(10)};
            CollectionAssert.AreEqual(new[]{1,2},g.Apply(3,true,0,new[]{1,2,9},entities,450,out _));
            g.Apply(3,true,1,new[]{3},entities,450,out _);CollectionAssert.AreEqual(new[]{3},g.Apply(3,false,2,Array.Empty<int>(),entities,450,out _));
            g.Apply(3,true,3,new[]{1,2},entities,450,out _);g.Prune(new[]{Unit(1)});CollectionAssert.AreEqual(new[]{1},g.Apply(3,false,4,Array.Empty<int>(),new[]{Unit(1)},450,out _));
            g.Apply(3,true,5,Array.Empty<int>(),entities,450,out _);Assert.IsEmpty(g.Apply(3,false,6,new[]{1},entities,450,out _));
        }
        [Test] public void ArmyIsRecomputedWithoutBuildingsAndNewMatchDropsAllGroups()
        {
            var g=new KeyboardControlGroups();var entities=new[]{Unit(1),Unit(2),Unit(9,own:false),Building(10)};
            g.Apply(1,true,0,new[]{1},entities,450,out _);CollectionAssert.AreEqual(new[]{1,2},g.Apply(0,false,1,new[]{10},entities,450,out _));
            CollectionAssert.AreEqual(new[]{1},g.Apply(1,false,2,Array.Empty<int>(),entities,450,out _));g.Reset();Assert.IsEmpty(g.Apply(1,false,3,new[]{1},entities,450,out _));
        }
        [Test] public void DoubleDigitUsesSameSlotWindowAndActiveLabelTracksActualSelectionChange()
        {
            var g=new KeyboardControlGroups();var entities=new[]{Unit(1),Unit(2)};g.Apply(1,true,0,new[]{1},entities,450,out _);
            g.Apply(1,false,1,Array.Empty<int>(),entities,450,out var focus);Assert.False(focus);
            g.Apply(1,false,1.4,new[]{1},entities,450,out focus);Assert.True(focus);Assert.AreEqual(1,g.Active);
            g.Observe(new[]{1});Assert.AreEqual(1,g.Active);g.Observe(new[]{1,2});Assert.IsNull(g.Active);
            g.Apply(2,false,1.5,new[]{1},entities,450,out focus);Assert.False(focus);g.Apply(1,false,1.6,Array.Empty<int>(),entities,450,out focus);Assert.False(focus);
        }
        [Test] public void BuildingGroupsAndLargestClusterShareStableFocusRule()
        {
            var entities=new[]{Unit(1,0,0),Unit(2,2,0),Unit(3,50,50),Building(10),Building(11,false)};var g=new KeyboardControlGroups();
            CollectionAssert.AreEqual(new[]{10},g.Apply(2,true,0,new[]{10,11},entities,450,out _));
            var building=KeyboardControlGroups.Focus(new[]{10},entities,0,0,12);Assert.AreEqual(10,building.Value.Id);
            var cluster=KeyboardControlGroups.Focus(new[]{3,2,1},entities,50,50,12);Assert.True(cluster.Value.Id==1||cluster.Value.Id==2);Assert.IsNull(KeyboardControlGroups.Focus(Array.Empty<int>(),entities,0,0,12));
        }
        [Test] public void EdgePanRampsNormalizesAndRejectsOutsidePointer()
        {
            var bounds=new Rect(0,0,100,80);Assert.AreEqual(Vector2.zero,AngularCommandCursor.EdgePan(new Vector2(50,40),bounds,20));
            Assert.AreEqual(new Vector2(-.5f,0),AngularCommandCursor.EdgePan(new Vector2(10,40),bounds,20));
            Assert.AreEqual(Vector2.zero,AngularCommandCursor.EdgePan(new Vector2(-1,40),bounds,20));
            Assert.AreEqual(1,AngularCommandCursor.EdgePan(new Vector2(0,0),bounds,20).magnitude,.0001);
        }
        [Test] public void ApprovedArrowUsesSameSilhouetteAndHotspotInBothColors()
        {
            var white=AngularCommandCursor.Create(false);var red=AngularCommandCursor.Create(true);
            try
            {
                var a=white.GetPixels32();var b=red.GetPixels32();Assert.AreEqual(32,white.width);Assert.AreEqual(new Vector2(3,2),AngularCommandCursor.Hotspot);
                for(int i=0;i<a.Length;i++)Assert.AreEqual(a[i].a,b[i].a);
                Assert.True(a.Any(p=>p.r>200&&p.g>200&&p.b>200));Assert.True(b.Any(p=>p.r>200&&p.g<100));
                string destination=Environment.GetEnvironmentVariable("SPACEWARS_CURSOR_EVIDENCE");
                if(!string.IsNullOrEmpty(destination)){Directory.CreateDirectory(destination);File.WriteAllBytes(Path.Combine(destination,"arrow-white.png"),white.EncodeToPNG());File.WriteAllBytes(Path.Combine(destination,"arrow-red.png"),red.EncodeToPNG());}
            }
            finally{UnityEngine.Object.DestroyImmediate(white);UnityEngine.Object.DestroyImmediate(red);}
        }
        [UnityTest] public IEnumerator NativeArrowPreviewRendersOnLightAndDarkSurfaces()
        {
            var host=new GameObject("approved native cursor preview");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
            panel.scaleMode=PanelScaleMode.ConstantPixelSize;panel.scale=1;
            var target=new RenderTexture(1000,440,24);target.Create();panel.targetTexture=target;
            var document=host.AddComponent<UIDocument>();document.panelSettings=panel;
            var root=document.rootVisualElement;root.style.width=1000;root.style.height=440;root.style.backgroundColor=new Color(.035f,.055f,.085f);
            var title=new Label("SPACEWARS · угловая стрелка без дополнительных элементов");title.style.fontSize=24;title.style.color=Color.white;title.style.marginLeft=24;title.style.marginTop=20;root.Add(title);
            var hint=new Label("Белая — обычно     Красная — видимый противник или режим A");hint.style.fontSize=17;hint.style.color=new Color(.65f,.72f,.8f);hint.style.marginLeft=24;root.Add(hint);
            var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;row.style.marginTop=26;row.style.marginLeft=24;root.Add(row);
            var white=AngularCommandCursor.Create(false);var red=AngularCommandCursor.Create(true);
            foreach(bool light in new[]{false,true})
            {
                var card=new VisualElement();card.style.width=466;card.style.height=274;card.style.marginRight=20;card.style.backgroundColor=light?new Color(.72f,.67f,.56f):new Color(.12f,.24f,.2f);row.Add(card);
                var caption=new Label(light?"Светлая поверхность":"Тёмная поверхность");caption.style.fontSize=18;caption.style.marginLeft=16;caption.style.marginTop=16;caption.style.color=light?Color.black:Color.white;card.Add(caption);
                var arrows=new VisualElement();arrows.style.flexDirection=FlexDirection.Row;arrows.style.marginTop=48;arrows.style.marginLeft=66;card.Add(arrows);
                foreach(var texture in new[]{white,red}){var icon=new Image {image=texture,scaleMode=ScaleMode.ScaleToFit};icon.style.width=32;icon.style.height=32;icon.style.marginRight=70;arrows.Add(icon);}
                var large=new VisualElement();large.style.flexDirection=FlexDirection.Row;large.style.marginTop=24;large.style.marginLeft=48;card.Add(large);
                foreach(var texture in new[]{white,red}){var icon=new Image {image=texture,scaleMode=ScaleMode.ScaleToFit};icon.style.width=64;icon.style.height=64;icon.style.marginRight=38;large.Add(icon);}
                var sizes=new Label("32 px сверху · увеличение ×2 снизу");sizes.style.fontSize=14;sizes.style.marginLeft=16;sizes.style.marginTop=16;sizes.style.color=light?Color.black:Color.white;card.Add(sizes);
            }
            try
            {
                yield return null;yield return null;yield return null;
                string path=Environment.GetEnvironmentVariable("SPACEWARS_CURSOR_EVIDENCE")??Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/cursor-preview"));Directory.CreateDirectory(path);
                var previous=RenderTexture.active;RenderTexture.active=target;var pixels=new Texture2D(1000,440,TextureFormat.RGBA32,false);
                pixels.ReadPixels(new Rect(0,0,1000,440),0,0);pixels.Apply();RenderTexture.active=previous;
                File.WriteAllBytes(Path.Combine(path,"native-arrow-preview.png"),pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);
                Assert.AreEqual(2,row.childCount);
            }
            finally{panel.targetTexture=null;UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(white);UnityEngine.Object.DestroyImmediate(red);}
        }
    }
}
