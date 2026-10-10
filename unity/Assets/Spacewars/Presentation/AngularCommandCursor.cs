using System;
using UnityEngine;

namespace Spacewars.Presentation
{
    // A plain arrow only; hotspot is its upper-left tip in both color states.
    public sealed class AngularCommandCursor : IDisposable
    {
        public const int Size=32;
        public static readonly Vector2 Hotspot=new Vector2(3,2);
        private readonly Texture2D normal=Create(false),attack=Create(true);
        private bool? red;
        public void Set(bool hostile)
        {
            if(red==hostile)return;red=hostile;
            if(!Application.isBatchMode)Cursor.SetCursor(hostile?attack:normal,Hotspot,CursorMode.Auto);
        }
        public static Texture2D Create(bool hostile)
        {
            var texture=new Texture2D(Size,Size,TextureFormat.RGBA32,false){name=hostile?"angular-arrow-attack":"angular-arrow-normal",filterMode=FilterMode.Point};
            var pixels=new Color32[Size*Size];var ink=new Color32(9,13,20,255);var fill=hostile?new Color32(255,75,82,255):new Color32(242,248,255,255);
            var shape=new[]{new Vector2(3,2),new Vector2(3,27),new Vector2(10,20),new Vector2(23,20)};
            bool Inside(float x,float y)
            {
                bool inside=false;
                for(int i=0,j=shape.Length-1;i<shape.Length;j=i++)if((shape[i].y>y)!=(shape[j].y>y)&&x<(shape[j].x-shape[i].x)*(y-shape[i].y)/(shape[j].y-shape[i].y)+shape[i].x)inside=!inside;
                return inside;
            }
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)
            {
                if(!Inside(x+.5f,y+.5f))continue;
                bool border=!Inside(x-1,y+.5f)||!Inside(x+2,y+.5f)||!Inside(x+.5f,y-1)||!Inside(x+.5f,y+2);
                pixels[(Size-1-y)*Size+x]=border?ink:fill;
            }
            texture.SetPixels32(pixels);texture.Apply(false,false);return texture;
        }
        public static Vector2 EdgePan(Vector2 point,Rect bounds,float zone)
        {
            if(zone<=0||!bounds.Contains(point))return Vector2.zero;
            float Axis(float p,float low,float high)=>p<low+zone?-Mathf.Clamp01((low+zone-p)/zone):p>high-zone?Mathf.Clamp01((p-high+zone)/zone):0;
            return Vector2.ClampMagnitude(new Vector2(Axis(point.x,bounds.xMin,bounds.xMax),Axis(point.y,bounds.yMin,bounds.yMax)),1);
        }
        public void Dispose()
        { if(!Application.isBatchMode)Cursor.SetCursor(null,Vector2.zero,CursorMode.Auto);UnityEngine.Object.Destroy(normal);UnityEngine.Object.Destroy(attack); }
    }
}
