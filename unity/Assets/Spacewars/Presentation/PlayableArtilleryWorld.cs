using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private readonly Dictionary<int,GameObject> impactMarkers=new Dictionary<int,GameObject>(),impactEffects=new Dictionary<int,GameObject>();
        private Material artilleryTransparent;
        private Material TransparentArtillery
        {
            get
            {
                if(artilleryTransparent)return artilleryTransparent;
                artilleryTransparent=new Material(Shader.Find("Universal Render Pipeline/Lit"));
                artilleryTransparent.SetFloat("_Surface",1);artilleryTransparent.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);artilleryTransparent.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);artilleryTransparent.SetFloat("_ZWrite",0);artilleryTransparent.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");artilleryTransparent.renderQueue=3000;return artilleryTransparent;
            }
        }
        private Color ShotColor(PlayableOwner owner,float alpha=1)=>owner==LocalOwner?new Color(.19f,.72f,.77f,alpha):new Color(.93f,.34f,.23f,alpha);
        private GameObject EffectPart(string name,Transform parent,Vector3 position,Vector3 scale,Color color,PrimitiveType shape,bool transparent=false)
        {
            var g=world.Part(name,parent,position,scale,color,shape);
            if(transparent)g.GetComponent<Renderer>().sharedMaterial=TransparentArtillery;
            var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);g.GetComponent<Renderer>().SetPropertyBlock(block);return g;
        }
        private void ClearArtilleryEffects()
        {
            foreach(var g in impactMarkers.Values)Destroy(g);impactMarkers.Clear();foreach(var g in impactEffects.Values)Destroy(g);impactEffects.Clear();
        }
        private void RenderProjectiles()
        {
            var live=new HashSet<int>();var markerIds=new HashSet<int>();
            foreach(var p in view.Projectiles)
            {
                // Ground offset and wafer thickness avoid depth fighting; they do not size the danger area.
                if(p.Marker!=null)
                {
                    markerIds.Add(p.Id);
                    if(!impactMarkers.TryGetValue(p.Id,out var marker)){marker=EffectPart("Predicted impact "+p.Id,transform,Vector3.zero,Vector3.one,ShotColor(p.Faction,(float)p.Marker.Opacity),PrimitiveType.Cylinder,true);impactMarkers.Add(p.Id,marker);}
                    marker.transform.position=world.Point(p.Marker.Position)+Vector3.up*.025f;
                    marker.transform.localScale=new Vector3((float)p.Marker.Radius*2,.012f,(float)p.Marker.Radius*2);
                }
                if(!p.Visible)continue;live.Add(p.Id);
                if(!shells.TryGetValue(p.Id,out var shell))
                {
                    if(p.Kind==PlayableEntityKind.Shkval)
                    {
                        shell=new GameObject("Rocket "+p.Id);shell.transform.SetParent(transform,false);
                        EffectPart("Missile body",shell.transform,Vector3.zero,new Vector3((float)profile.ShkvalProjectileLength,(float)profile.ShkvalProjectileRadius*2,(float)profile.ShkvalProjectileRadius*2),ShotColor(p.Faction),PrimitiveType.Cube);
                        EffectPart("Rear exhaust",shell.transform,Vector3.zero,Vector3.one,new Color(1,.38f,.03f,(float)profile.ShkvalExhaustOpacity),PrimitiveType.Sphere,true);
                        EffectPart("Hot exhaust core",shell.transform,Vector3.zero,Vector3.one,new Color(1,.96f,.65f,(float)profile.ShkvalExhaustOpacity),PrimitiveType.Sphere,true);
                    }
                    else shell=world.Part(p.Kind==PlayableEntityKind.Explorer?"Tracer":"Shell",transform,Vector3.zero,p.Kind==PlayableEntityKind.Explorer?new Vector3((float)profile.ExplorerTracerLength,(float)profile.ExplorerTracerThickness,(float)profile.ExplorerTracerThickness):new Vector3(.22f,.22f,.22f),new Color(1,.83f,.27f),p.Kind==PlayableEntityKind.Explorer?PrimitiveType.Cube:PrimitiveType.Sphere);
                    shells[p.Id]=shell;
                }
                shell.transform.position=new Vector3((float)p.Position.X,(float)p.Height,(float)p.Position.Z);
                shell.transform.rotation=Quaternion.Euler(0,-(float)p.Heading*Mathf.Rad2Deg,0)*Quaternion.Euler(0,0,(float)p.Pitch*Mathf.Rad2Deg);
                if(p.Kind==PlayableEntityKind.Shkval)
                {
                    float boost=1+(float)(profile.ShkvalExhaustStartBoost-1)*Mathf.Clamp01(1-(float)(p.Age/profile.ShkvalExhaustStartupSec));
                    for(int i=1;i<=2;i++){var tail=shell.transform.GetChild(i);float ratio=i==2?(float)profile.WeaponMuzzleCoreRatio:1;float length=(float)profile.ShkvalExhaustLength*boost*ratio;tail.localPosition=new Vector3(-(float)profile.ShkvalProjectileLength/2-length/2,0,0);tail.localScale=new Vector3(length,(float)profile.ShkvalExhaustRadius*2*boost*ratio,(float)profile.ShkvalExhaustRadius*2*boost*ratio);}
                }
            }
            foreach(var id in shells.Keys.Where(id=>!live.Contains(id)).ToArray()){Destroy(shells[id]);shells.Remove(id);}
            foreach(var id in impactMarkers.Keys.Where(id=>!markerIds.Contains(id)).ToArray()){Destroy(impactMarkers[id]);impactMarkers.Remove(id);}
            var effects=new HashSet<int>();foreach(var p in view.Impacts)
            {
                effects.Add(p.Id);if(!impactEffects.TryGetValue(p.Id,out var effect)){effect=EffectPart("Artillery blast "+p.Id,transform,Vector3.zero,Vector3.one,new Color(1,.6f,.1f,(float)profile.ShkvalExhaustOpacity),PrimitiveType.Sphere,true);impactEffects.Add(p.Id,effect);}
                effect.transform.position=new Vector3((float)p.Point.X,(float)p.Point.Y,(float)p.Point.Z);float age=(float)((view.Tick-p.Tick)/30d/profile.ImpactEffectSec);effect.transform.localScale=Vector3.one*(float)p.Radius*2;
            }
            foreach(var id in impactEffects.Keys.Where(id=>!effects.Contains(id)).ToArray()){Destroy(impactEffects[id]);impactEffects.Remove(id);}
        }
    }
}
