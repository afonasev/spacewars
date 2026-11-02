using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableWorld
    {
        private readonly Dictionary<int,GameObject> mineModels=new Dictionary<int,GameObject>();
        private readonly Dictionary<int,GameObject> mineEffects=new Dictionary<int,GameObject>();
        private PlayableProfileData mineArtData;private readonly Dictionary<int,long> mineEffectTicks=new Dictionary<int,long>();
        private Mesh[] mineMeshes;private Material mineMaterial,mineBurstMaterial;
        private void ClearMines()
        {
            foreach(var go in mineModels.Values)ReleaseWorldObject(go);mineModels.Clear();
            foreach(var go in mineEffects.Values){ReleaseWorldObject(go.GetComponent<MeshFilter>().sharedMesh);ReleaseWorldObject(go);}mineEffects.Clear();
            mineEffectTicks.Clear();mineArtData=null;
            if(mineMeshes!=null)foreach(var mesh in mineMeshes)ReleaseWorldObject(mesh);mineMeshes=null;
            ReleaseWorldObject(mineMaterial);mineMaterial=null;ReleaseWorldObject(mineBurstMaterial);mineBurstMaterial=null;
        }
        public void RenderMines(PlayableSnapshot view,Camera camera)
        {
            if(mineMeshes==null){mineArtData=profile.CopyData();mineMeshes=Enumerable.Range(0,3).Select(OrbitalMineModel.Build).ToArray();mineMaterial=new Material(Resources.Load<Shader>("TrapMine"));mineBurstMaterial=new Material(Resources.Load<Shader>("TrapMineBurst"));}
            foreach(var id in mineModels.Keys.Where(id=>!view.Mines.Any(m=>m.Id==id)).ToArray()){ReleaseWorldObject(mineModels[id]);mineModels.Remove(id);}
            foreach(var mine in view.Mines){
                if(!mineModels.TryGetValue(mine.Id,out var model)){
                    model=new GameObject("Trap mine B "+mine.Id);model.transform.SetParent(root,false);var filter=model.AddComponent<MeshFilter>();filter.sharedMesh=mineMeshes[0];var renderer=model.AddComponent<MeshRenderer>();renderer.sharedMaterial=mineMaterial;
                    var lod=model.AddComponent<ModelDetailLod>();lod.Configure(new Renderer[]{renderer},new[]{mineMeshes[0]},new[]{mineMeshes[1]},new[]{mineMeshes[2]},mineMeshes[0].bounds);Layer(model);mineModels.Add(mine.Id,model);
                }
                model.transform.position=Point(mine.Point);var slope=profile.AuthoredMap?.SurfaceGradient(mine.Point)??default;model.transform.rotation=Quaternion.FromToRotation(Vector3.up,new Vector3(-(float)slope.X,1,-(float)slope.Z).normalized);
                model.transform.localScale=new Vector3((float)mine.ModelRadius,(float)mine.ModelHeight,(float)mine.ModelRadius);
                var block=new MaterialPropertyBlock();block.SetColor("_OwnerColor",OwnerPaint?.Invoke(mine.Owner)??(mine.Owner==view.Owner?Ally:Enemy));model.GetComponent<MeshRenderer>().SetPropertyBlock(block);model.GetComponent<ModelDetailLod>().Select(camera);
            }
            foreach(var id in mineEffects.Keys.Where(id=>!view.MineBlasts.Any(b=>b.Id==id)).ToArray()){var go=mineEffects[id];ReleaseWorldObject(go.GetComponent<MeshFilter>().sharedMesh);ReleaseWorldObject(go);mineEffects.Remove(id);mineEffectTicks.Remove(id);}
            var tuning=mineArtData;
            foreach(var blast in view.MineBlasts){
                if(!mineEffects.TryGetValue(blast.Id,out var effect)){
                    effect=new GameObject("Trap mine ground burst "+blast.Id);effect.transform.SetParent(root,false);effect.AddComponent<MeshFilter>().sharedMesh=new Mesh{name="Mine ground burst"};effect.AddComponent<MeshRenderer>().sharedMaterial=mineBurstMaterial;Layer(effect);mineEffects.Add(blast.Id,effect);
                }
                if(mineEffectTicks.TryGetValue(blast.Id,out var lastTick)&&lastTick==view.Tick)continue;mineEffectTicks[blast.Id]=view.Tick;
                effect.transform.position=Point(blast.Point);float phase=Mathf.Clamp01((float)((view.Tick-blast.Tick)/30d/blast.Duration));
                var mesh=effect.GetComponent<MeshFilter>().sharedMesh;BuildMineBurst(mesh,phase,(float)blast.Radius,tuning,blast.Id);
                if(profile.AuthoredMap!=null){var vertices=mesh.vertices;float ground=Point(blast.Point).y;for(int i=0;i<vertices.Length;i++)vertices[i].y+=(float)profile.AuthoredMap.SurfaceHeight(new NavPoint(blast.Point.X+vertices[i].x,blast.Point.Z+vertices[i].z))-ground;mesh.vertices=vertices;mesh.RecalculateBounds();}
            }
        }
        // The outer edge is always exactly the authority radius, including at the first
        // frame. Dust/flash/fragments remain inside; this is not an expanding damage zone.
        public static void BuildMineBurst(Mesh mesh,float phase,float radius,PlayableProfileData p,int seed)
        {
            var vertices=new List<Vector3>();var colors=new List<Color>();var indices=new List<int>();float fade=1-phase;
            void Triangle(Vector3 a,Vector3 b,Vector3 c,Color ca,Color cb,Color cc){int n=vertices.Count;vertices.AddRange(new[]{a,b,c});colors.AddRange(new[]{ca,cb,cc});indices.AddRange(new[]{n,n+1,n+2});}
            Vector3 At(float a,float r,float y)=>new Vector3(Mathf.Cos(a)*r,y,Mathf.Sin(a)*r);
            float width=Mathf.Min(radius,(float)p.orbitalMineRimWidth),lift=width*.5f;
            for(int i=0;i<96;i++){
                float a=i*Mathf.PI/48,b=(i+1)*Mathf.PI/48;var rim=new Color(1,.66f,.20f,fade);
                var x=At(a,radius,lift);var y=At(b,radius,lift);var z=At(a,radius-width,lift);var w=At(b,radius-width,lift);Triangle(x,y,z,rim,rim,rim);Triangle(y,w,z,rim,rim,rim);
                float dustRadius=radius*Mathf.Min(1,phase*2+.2f);var dust=new Color(.57f,.43f,.29f,(float)p.orbitalMineDustOpacity*fade);var transparent=new Color(dust.r,dust.g,dust.b,0);
                Triangle(new Vector3(0,lift,0),At(a,dustRadius,lift),At(b,dustRadius,lift),dust,transparent,transparent);
                float flashRadius=radius*(1-phase)*.65f;var hot=new Color(1,.88f,.48f,Mathf.Max(0,1-phase*4));
                Triangle(new Vector3(0,lift+(float)p.orbitalMineEffectHeight*Mathf.Sin(phase*Mathf.PI),0),At(a,flashRadius,lift),At(b,flashRadius,lift),hot,transparent,transparent);
            }
            int count=(int)p.orbitalMineFragmentCount;
            for(int i=0;i<count;i++){
                float random=((i*73+seed*17)%101)/101f,angle=(i+random*.7f)*Mathf.PI*2/count;
                float size=Mathf.Min(radius*.05f,(float)p.orbitalMineFragmentSize)*fade;
                var center=At(angle,(radius-size)*phase*(.35f+.6f*random),lift+(float)p.orbitalMineEffectHeight*Mathf.Sin(phase*Mathf.PI)*(.4f+.6f*random));
                var c=Color.Lerp(new Color(.25f,.21f,.17f,fade),new Color(1,.54f,.13f,fade),Mathf.Max(0,1-phase*3));
                var x=center+At(angle,size,0);var y=center+At(angle+2.1f,size,0);var z=center+At(angle+4.2f,size,0);var top=center+Vector3.up*size;
                Triangle(x,y,top,c,c,c);Triangle(y,z,top,c,c,c);Triangle(z,x,top,c,c,c);
                // Sparse low puffs break the perfect radial fan without a smoke column.
                if(i%2!=0)continue;
                var dustCenter=At(angle,radius*phase*(.4f+.4f*random),lift+(float)p.orbitalMineEffectHeight*.15f);
                float puff=radius*(.08f+.08f*random)*(1+phase);puff=Mathf.Min(puff,radius-new Vector2(dustCenter.x,dustCenter.z).magnitude);
                var dust=new Color(.55f,.41f,.28f,(float)p.orbitalMineDustOpacity*fade*.7f);var clear=new Color(dust.r,dust.g,dust.b,0);
                for(int j=0;j<8;j++)Triangle(dustCenter,dustCenter+At(j*Mathf.PI/4,puff,0),dustCenter+At((j+1)*Mathf.PI/4,puff,0),dust,clear,clear);
            }
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
        }
    }
}
