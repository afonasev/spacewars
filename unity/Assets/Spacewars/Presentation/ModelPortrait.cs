using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    [System.Serializable] public sealed class PortraitFrame
    {
        public string key;public Rect uv;
    }
    [System.Serializable] public sealed class PortraitFraming
    {
        public PortraitFrame[] frames;
        public PortraitFrame[] eventFrames;
        static PortraitFraming cached;
        static PortraitFraming Framing => cached ??= JsonUtility.FromJson<PortraitFraming>(
            Resources.Load<TextAsset>("UpgradePortraitFraming").text);

        public static Rect UvFor(string key) => FindUv(Framing.frames,key);
        internal static Rect SquareUvFor(string key) => FindUv(Framing.eventFrames,key);

        static Rect FindUv(PortraitFrame[] frames,string key)
        {
            foreach(var frame in frames)
                if(frame.key==key)return frame.uv;
            return new Rect(0,0,1,1);
        }
    }
    // Every HUD context fits the same contour and model to exactly the same rectangle.
    public sealed class ModelPortrait : VisualElement
    {
        internal const string RocketLauncherArtwork="explorerRocket";
        readonly Image outline,portrait;
        PortraitUpgradeMark upgradeMark;
        float rankScale,eventInset;
        private const int FilteredOutlineSize = 64;
        private static readonly Dictionary<Texture,RenderTexture> filteredOutlines = new();
        internal bool HasUpgradeMark=>upgradeMark!=null&&upgradeMark.style.display.value==DisplayStyle.Flex;
        public Texture Texture=>portrait.image;
        public ModelPortrait(string imageName="model-portrait")
        {
            pickingMode=PickingMode.Ignore;
            outline=new Image{name="portrait-outline",scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};
            portrait=new Image{name=imageName,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};
            foreach(var layer in new[]{outline,portrait}){layer.style.position=Position.Absolute;layer.style.left=layer.style.right=layer.style.top=layer.style.bottom=0;Add(layer);}
        }
        public void SetOutlineColor(Color color)=>outline.tintColor=color;
        internal void FitSquareContent(float inset)
        {
            if(Texture==null)return;
            portrait.uv=outline.uv=PortraitFraming.SquareUvFor(Texture.name);
            eventInset=inset;
            style.left=style.right=style.top=style.bottom=inset;
        }

        internal void FilterOutlineForResults()
        {
            var source = outline.image;
            if (source==null || SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) return;

            // Repeated half-size bilinear passes average alpha coverage before the
            // much smaller UI draw. The original HUD textures remain untouched.
            int longest = Mathf.Max(source.width,source.height);
            if (longest<=FilteredOutlineSize) return;
            if (!filteredOutlines.TryGetValue(source,out var filtered) || filtered==null)
            {
                float ratio = FilteredOutlineSize/(float)longest;
                int width = Mathf.Max(1,Mathf.RoundToInt(source.width*ratio));
                int height = Mathf.Max(1,Mathf.RoundToInt(source.height*ratio));
                filtered = new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear)
                {
                    name = source.name+" (filtered results contour)",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            if (!filtered.IsCreated())
            {
                if (!filtered.Create())
                {
                    filteredOutlines.Remove(source);
                    Object.Destroy(filtered);
                    return;
                }
                DownsampleOutline(source,filtered);
                filteredOutlines[source] = filtered;
            }
            outline.image = filtered;
        }

        private static void DownsampleOutline(Texture source,RenderTexture destination)
        {
            var previous = RenderTexture.active;
            RenderTexture level = null;
            try
            {
                Texture input = source;
                while (input.width>2*destination.width || input.height>2*destination.height)
                {
                    var next = RenderTexture.GetTemporary(
                        Mathf.Max(destination.width,(input.width+1)/2),
                        Mathf.Max(destination.height,(input.height+1)/2),
                        0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
                    next.filterMode = FilterMode.Bilinear;
                    next.wrapMode = TextureWrapMode.Clamp;
                    var previousLevel = level;
                    level = next;
                    try { Graphics.Blit(input,level); }
                    finally { if (previousLevel!=null) RenderTexture.ReleaseTemporary(previousLevel); }
                    input = level;
                }
                Graphics.Blit(input,destination);
            }
            finally
            {
                RenderTexture.active = previous;
                if (level!=null) RenderTexture.ReleaseTemporary(level);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFilteredOutlines()
        {
            foreach (var outlineTexture in filteredOutlines.Values)
            {
                if (outlineTexture==null) continue;
                outlineTexture.Release();
                Object.Destroy(outlineTexture);
            }
            filteredOutlines.Clear();
        }

        internal void AddOutlineStroke(float thickness)
        {
            if(outline.image==null)return;
            void AddStrokeLayer(float x,float y)
            {
                var layer=new Image
                {
                    image=outline.image,uv=outline.uv,tintColor=outline.tintColor,
                    scaleMode=outline.scaleMode,pickingMode=PickingMode.Ignore
                };
                layer.style.position=Position.Absolute;
                layer.style.left=x;layer.style.right=-x;layer.style.top=y;layer.style.bottom=-y;
                Insert(0,layer);
            }
            AddStrokeLayer(-thickness,0);
            AddStrokeLayer(thickness,0);
            AddStrokeLayer(0,-thickness);
            AddStrokeLayer(0,thickness);
        }

        private void SizeUpgradeMark()
        {
            float size=Mathf.Min(contentRect.width,contentRect.height)+2*eventInset;
            ResearchIcon.LayoutBadge(upgradeMark,size,rankScale);
        }

        public void Set(string key,bool selected=false,bool available=true,bool fitContent=false)
        {
            var texture=DockArtwork.TextureFor(key);portrait.image=texture;outline.image=texture==null?null:DockArtwork.TextureFor(key+"-outline");
            portrait.uv=outline.uv=fitContent?PortraitFraming.UvFor(key):new Rect(0,0,1,1);
            outline.tintColor=selected?OrbitalTheme.Cyan:new Color(.60f,.72f,.77f,.65f);
            portrait.tintColor=available?Color.white:new Color(.76f,.79f,.82f);
            portrait.style.display=texture==null?DisplayStyle.None:DisplayStyle.Flex;
            outline.style.display=outline.image==null?DisplayStyle.None:DisplayStyle.Flex;
            bool showRocketMark=texture!=null&&key==RocketLauncherArtwork;
            if(showRocketMark&&upgradeMark==null)
            {
                rankScale=RadialCardProfile.Load().researchRankSize;
                upgradeMark=new PortraitUpgradeMark(3){name="portrait-upgrade-mark",pickingMode=PickingMode.Ignore};
                Add(upgradeMark);
                RegisterCallback<GeometryChangedEvent>(_=>SizeUpgradeMark());
                SizeUpgradeMark();
            }
            if(upgradeMark!=null)
            {
                upgradeMark.Available=available;
                upgradeMark.style.display=showRocketMark?DisplayStyle.Flex:DisplayStyle.None;
                upgradeMark.MarkDirtyRepaint();
            }
        }
    }
}
