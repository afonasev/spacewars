using System;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed class CerberusPresentation : MonoBehaviour
    {
        private sealed class Mount
        {
            internal Transform Turret, Plume;
            internal float LastYaw=float.NaN, LastWidth=float.NaN, LastLength=float.NaN, LastHeat=float.NaN;
            internal Renderer Cooler, PlumeRenderer;
        }

        private sealed class Rig
        {
            internal GameObject Root;
            internal Track[] Tracks;
            internal Mount[] Mounts;
        }

        private sealed class Track
        {
            const float Length=3.38f, Radius=.37f, Arc=Radius*Mathf.PI, Loop=2*(Length+Arc);
            readonly Transform[] shoes, wheels;
            readonly Quaternion[] rest, wheelRest;
            float lastPhase;
            internal Track(Transform root)
            {
                var nodes=root.GetComponentsInChildren<Transform>(true);
                wheels=nodes.Where(t=>t.name.StartsWith("wheel",StringComparison.Ordinal)&&t.name.EndsWith("Joint",StringComparison.Ordinal)).ToArray();
                wheelRest=wheels.Select(t=>t.localRotation).ToArray();
                shoes=nodes.Where(t=>t.name.StartsWith("tread",StringComparison.Ordinal)&&t.name.EndsWith("Joint",StringComparison.Ordinal))
                    .OrderBy(t=>int.Parse(t.name.Substring(6,t.name.Length-11))).ToArray();
                rest=shoes.Select(t=>t.localRotation).ToArray();
            }
            static (float z,float y,float angle) Sample(float d)
            {
                d=Mathf.Repeat(d,Loop);
                if(d<Length)return (Length/2-d,Radius,0);
                d-=Length;
                if(d<Arc){float a=d/Radius;return (-Length/2-Radius*Mathf.Sin(a),Radius*Mathf.Cos(a),-a);}
                d-=Arc;
                if(d<Length)return (-Length/2+d,-Radius,-Mathf.PI);
                float b=(d-Length)/Radius;
                return (Length/2+Radius*Mathf.Sin(b),-Radius*Mathf.Cos(b),-Mathf.PI-b);
            }
            internal void Drive(float phase)
            {
                if (phase==lastPhase) return;
                lastPhase=phase;
                var rotation=Quaternion.AngleAxis(phase*Mathf.Rad2Deg/.285f,Vector3.right);
                for(int i=0;i<wheels.Length;i++)wheels[i].localRotation=rotation*wheelRest[i];
                for(int i=0;i<shoes.Length;i++)
                {
                    float origin=Loop*i/shoes.Length;
                    var next=Sample(origin-phase);
                    shoes[i].localPosition=new Vector3(shoes[i].localPosition.x,next.y,next.z);
                    shoes[i].localRotation=Quaternion.AngleAxis((next.angle-Sample(origin).angle)*Mathf.Rad2Deg,Vector3.right)*rest[i];
                }
            }
        }
        private Rig[] rigs;
        private static Material plumeMaterial;
        private static int materialUsers;
        private bool usesMaterial;
        private ModelDetailLod detail;
        private static readonly int Tint = Shader.PropertyToID("_BaseColor"), Phase = Shader.PropertyToID("_FlowPhase");
        private static Mesh jetMesh;
        private Vector3 lastPosition;
        private bool positioned;
        private float lastYaw,leftTravel,rightTravel;
        private MaterialPropertyBlock block;

        public static GameObject Create(Transform parent,bool wreck)
        {
            var root = new GameObject(wreck ? "Cerberus wreck" : "Cerberus");
            root.transform.SetParent(parent,false);
            var component=root.AddComponent<CerberusPresentation>();
            component.Build(wreck);
            return root;
        }

        private void Build(bool wreck)
        {
            if(!wreck)
            {
                block = new MaterialPropertyBlock();
                if (!plumeMaterial) plumeMaterial = new Material(Resources.Load<Shader>("CerberusPlasma"));
                materialUsers++;
                usesMaterial = true;
            }
            rigs = new Rig[3];
            for(int i=0;i<rigs.Length;i++)
            {
                string key="StyleA/Cerberus/"+(wreck?"wreck":"cerberus")+i;
                var model=Instantiate(Resources.Load<GameObject>(key),transform,false);
                var nodes=model.GetComponentsInChildren<Transform>(true);
                var rig=rigs[i]=new Rig{Root=model};
                if(!wreck)rig.Tracks=nodes.Where(n=>n.name=="trackL"||n.name=="trackR")
                    .OrderBy(n=>transform.InverseTransformPoint(n.position).x).Select(n=>new Track(n)).ToArray();
                if(wreck)
                {
                    foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
                    {
                        var slots=renderer.sharedMaterials;
                        for(int m=0;m<slots.Length;m++)
                        {
                            string finish=slots[m].name=="neutral-metal"?"neutral":"structure";
                            slots[m]=Resources.Load<Material>("Wrecks/tank-1-wreck-"+finish);
                        }
                        renderer.sharedMaterials=slots;
                    }
                    continue;
                }
                // The glTF importer reflects X. Sort the physical sockets in Unity space.
                var turrets=nodes.Where(n=>n.name=="turretYawL"||n.name=="turretYawR")
                    .OrderBy(n=>transform.InverseTransformPoint(n.position).x).ToArray();
                rig.Mounts=new Mount[2];
                for(int j=0;j<2;j++)
                {
                    string side=turrets[j].name.Substring("turretYaw".Length);
                    var muzzle=nodes.Single(n=>n.name=="muzzle"+side);
                    var mount=rig.Mounts[j]=new Mount{Turret=turrets[j],Plume=Jet(muzzle,plumeMaterial),
                        Cooler=nodes.Single(n=>n.name=="radiator"+side).GetComponentInChildren<Renderer>()};
                    mount.PlumeRenderer=mount.Plume.GetComponent<Renderer>();
                }
            }
            detail=gameObject.AddComponent<ModelDetailLod>();
            detail.ConfigureGroups(rigs.Select(r=>r.Root).ToArray(),
                new Bounds(new Vector3(0,.76f,0),new Vector3(3.1f,1.55f,4.5f)));
        }

        private static Mesh CreatePlumeMesh()
        {
            const int sides = 12, rings = 12;
            var vertices = new Vector3[2 + rings * sides];
            var indices = new int[6 * rings * sides];
            int tip = vertices.Length - 1, index = 0;
            vertices[tip] = Vector3.forward;
            for (int ring = 0; ring < rings; ring++)
            {
                float latitude = Mathf.PI * (ring + 1) / (rings + 1);
                float axis = (1 - Mathf.Cos(latitude)) * .5f;
                // Keep the nozzle and tip fixed; compress the rounded cap axially.
                float z = 1 - Mathf.Pow(1 - axis, 1.36f);
                float radius = Mathf.Sin(latitude) * (.12f + 1.18f * axis);
                for (int side = 0; side < sides; side++)
                {
                    float angle = 2 * Mathf.PI * side / sides;
                    int vertex = 1 + ring * sides + side;
                    int next = 1 + ring * sides + (side + 1) % sides;
                    vertices[vertex] = new Vector3(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle), z);
                    if (ring == 0)
                    {
                        indices[index++] = 0; indices[index++] = next; indices[index++] = vertex;
                    }
                    if (ring == rings - 1)
                    {
                        indices[index++] = vertex; indices[index++] = next; indices[index++] = tip;
                    }
                    else
                    {
                        indices[index++] = vertex; indices[index++] = next; indices[index++] = vertex + sides;
                        indices[index++] = next; indices[index++] = next + sides; indices[index++] = vertex + sides;
                    }
                }
            }
            var mesh = new Mesh { name = "Rounded plasma plume", vertices = vertices, triangles = indices };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            // Draw both layers in one mesh. UV.x identifies the narrow luminous core.
            int n=vertices.Length;
            var coreLayer=new Vector2[2*n];
            for (int i=n;i<2*n;i++) coreLayer[i]=Vector2.right;
            var combined=new Mesh {name=mesh.name,vertices=vertices.Concat(vertices).ToArray(),
                normals=mesh.normals.Concat(mesh.normals).ToArray(),uv=coreLayer,
                triangles=indices.Concat(indices.Select(i=>i+n)).ToArray()};
            combined.RecalculateBounds();
            if(Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
            return combined;
        }

        private Transform Jet(Transform muzzle, Material material)
        {
            if (!jetMesh) jetMesh = CreatePlumeMesh();
            var jet=new GameObject("Plasma");
            jet.transform.SetParent(muzzle,false);
            jet.AddComponent<MeshFilter>().sharedMesh=jetMesh;
            var renderer=jet.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows=false;
            jet.SetActive(false);
            return jet.transform;
        }

        public void Display(PlayableEntitySnapshot entity, double fullAngle, Color owner, double animationSeconds = 0)
        {
            var pose=entity.Flames.GetValueOrDefault();
            Vector3 position=transform.position;
            float travel=positioned?Vector3.Dot(position-lastPosition,transform.forward):0;
            float yaw=transform.eulerAngles.y;
            float turn=positioned?Mathf.DeltaAngle(lastYaw,yaw)*Mathf.Deg2Rad:0;
            float scale=transform.lossyScale.z;
            float left=travel/scale+turn*1.17f, right=travel/scale-turn*1.17f;
            leftTravel+=left;rightTravel+=right;
            lastPosition=position;lastYaw=yaw;positioned=true;
            var rig=rigs[detail.Level];
            rig.Tracks[0].Drive(leftTravel);
            rig.Tracks[1].Drive(rightTravel);
            for(int i=0;i<2;i++)
            {
                var mount=rig.Mounts[i];
                float angle=(float)(i==0?pose.LeftYaw:pose.RightYaw);
                if(angle!=mount.LastYaw){
                    mount.Turret.localRotation=Quaternion.Euler(0,-angle*Mathf.Rad2Deg,0);
                    mount.LastYaw=angle;
                }
                float length=(float)(i==0?pose.LeftLength:pose.RightLength)/scale;
                float width=length>0 ? (float)PlayableUnitRules.CerberusPlumeWidthScale*length*Mathf.Tan((float)fullAngle*Mathf.Deg2Rad*.5f) : 0;
                if(length!=mount.LastLength || width!=mount.LastWidth){
                    mount.Plume.gameObject.SetActive(length>0);
                    if(length>0)mount.Plume.localScale=new Vector3(width,width,length);
                    mount.LastLength=length;mount.LastWidth=width;
                }
                if(length>0){
                    block.Clear();
                    block.SetColor(Tint,Color.Lerp(new Color(.89f,.94f,1f),owner,.46f));
                    block.SetFloat(Phase,(float)(animationSeconds*8.0)+entity.Id*.73f+i*2.4f);
                    mount.PlumeRenderer.SetPropertyBlock(block,0);
                }
                float heat=(float)(i==0?pose.LeftHeat:pose.RightHeat);
                if(heat!=mount.LastHeat){
                    mount.Cooler.GetPropertyBlock(block,0);
                    block.SetColor(Tint,Color.Lerp(new Color(.27f,.30f,.34f),new Color(1,.28f,.05f),heat));
                    mount.Cooler.SetPropertyBlock(block,0);
                    mount.LastHeat=heat;
                }
            }
        }

        private void OnDestroy()
        {
            if(usesMaterial && --materialUsers==0 && plumeMaterial){
                if(Application.isPlaying)Destroy(plumeMaterial);else DestroyImmediate(plumeMaterial);
                plumeMaterial=null;
            }
        }
    }
}
