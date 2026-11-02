using UnityEngine;
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
        static PortraitFraming cached;
        public static Rect UvFor(string key)
        {
            if(cached==null)cached=JsonUtility.FromJson<PortraitFraming>(Resources.Load<TextAsset>("UpgradePortraitFraming").text);
            foreach(var frame in cached.frames)if(frame.key==key)return frame.uv;
            return new Rect(0,0,1,1);
        }
    }
    // Every HUD context fits the same contour and model to exactly the same rectangle.
    public sealed class ModelPortrait : VisualElement
    {
        readonly Image outline,portrait;
        public Texture Texture=>portrait.image;
        public ModelPortrait(string imageName="model-portrait")
        {
            pickingMode=PickingMode.Ignore;
            outline=new Image{name="portrait-outline",scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};
            portrait=new Image{name=imageName,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};
            foreach(var layer in new[]{outline,portrait}){layer.style.position=Position.Absolute;layer.style.left=layer.style.right=layer.style.top=layer.style.bottom=0;Add(layer);}
        }
        public void SetOutlineColor(Color color)=>outline.tintColor=color;
        public void Set(string key,bool selected=false,bool available=true,bool fitContent=false)
        {
            var texture=DockArtwork.TextureFor(key);portrait.image=texture;outline.image=texture==null?null:DockArtwork.TextureFor(key+"-outline");
            portrait.uv=outline.uv=fitContent?PortraitFraming.UvFor(key):new Rect(0,0,1,1);
            outline.tintColor=selected?OrbitalTheme.Cyan:new Color(.60f,.72f,.77f,.65f);
            portrait.tintColor=available?Color.white:new Color(.76f,.79f,.82f);
            portrait.style.display=texture==null?DisplayStyle.None:DisplayStyle.Flex;
            outline.style.display=outline.image==null?DisplayStyle.None:DisplayStyle.Flex;
        }
    }
}
