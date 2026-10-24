using System;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Spacewars.Editor
{
    // Rendering-only inspection. No authority, navigation pump or gameplay replay.
    public static class WhiteMeridianProject
    {
        public static void Capture(){
            string output=Environment.GetEnvironmentVariable("WHITE_MERIDIAN_EVIDENCE");if(string.IsNullOrEmpty(output))throw new ArgumentException("WHITE_MERIDIAN_EVIDENCE required");Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var host=new GameObject("White Meridian terrain inspection");
            var map=new WhiteMeridianMap(JsonUtility.FromJson<WhiteMeridianProfileData>(Resources.Load<TextAsset>("WhiteMeridianProfile").text));
            var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            var sun=new GameObject("Sun").AddComponent<Light>();IndustrialLightingProfile.Load(map.Id).ApplyScene(sun);
            using(var world=new PlayableWorld(host.transform,p)){
                world.Fog.ShowPublic(); // All materials share the actual diagnostic fog texture.
                // Static projection of the initial authored state; no running authority.
                var sites=map.Sites(p);var buildings=sites.Take(4).Select(s=>new PlayableBuildingSnapshot(s.Id,(PlayableOwner)(s.Id-1),s.Kind,s.Position,100,1,0,0,s.Position,siteId:s.Id)).ToArray();
                var states=sites.Select(s=>new TerritorySiteSnapshot(s,null,s.Id<=4?(PlayableOwner?)(s.Id-1):null,0,false,s.Id<=4?s.Id:0,s.Id<=4)).ToArray();
                var snapshot=new PlayableSnapshot(p.ProfileId,p.Revision,1,7108,0,0,RuntimeStatus.Running,true,PlayableMatchOutcome.Playing,0,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),1),Array.Empty<PlayableEntitySnapshot>(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:states,discoveredSites:sites);
                world.OwnerPaint=owner=>!owner.HasValue?Color.white:(int)owner.Value<2?new Color(.18f,.64f,.72f):new Color(.85f,.36f,.16f);
                world.RenderSites(snapshot);
                foreach(var site in sites.Take(4)){var actor=world.Building(site.Id,"headquarters",site.Id<=2);actor.Root.position=new Vector3((float)site.Position.X,(float)map.SurfaceHeight(site.Position),(float)site.Position.Z);}
                var camera=new GameObject("Meridian inspection camera").AddComponent<Camera>();camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=600;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.18f,.19f);
                // Health transforms are separate from model roots; project the initial
                // healthy HQ state so no uninitialized bar remains over the wreck at origin.
                foreach(var actor in world.Actors.Values)PlayableWorld.UpdateHealth(actor,camera,1,false);
                camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(host.transform,IndustrialLightingProfile.Load(map.Id));
                if(Environment.GetEnvironmentVariable("WHITE_MERIDIAN_CAPTURE")=="representative"){
                    Shot(camera,output,"ramp--1-1",new Vector3(-62,2.5f,28),12,new Vector3(12,23,-26));
                }else if(Environment.GetEnvironmentVariable("WHITE_MERIDIAN_CAPTURE")=="tracks-representative"){
                    Shot(camera,output,"track-junction",new Vector3(0,0,30),10,new Vector3(0,35,-8));
                }else if(Environment.GetEnvironmentVariable("WHITE_MERIDIAN_CAPTURE")=="rocks"){
                    Shot(camera,output,"rocks-overview",Vector3.zero,88,new Vector3(0,150,-110));
                    foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1})
                        Shot(camera,output,"rock-ridge-"+(sx<0?"west":"east")+"-"+(sz<0?"south":"north"),new Vector3(sx*24,2,sz*40),12,new Vector3(0,24,-22));
                    foreach(int sx in new[]{-1,1})
                        Shot(camera,output,"rock-crossing-"+(sx<0?"west":"east"),new Vector3(sx*51,2,0),14,new Vector3(-sx*4,25,-22));
                    Shot(camera,output,"rock-shoulder",new Vector3(-22,1,11),9,new Vector3(0,18,-15));
                }else if(Environment.GetEnvironmentVariable("WHITE_MERIDIAN_CAPTURE")=="tracks"){
                    Shot(camera,output,"overview",Vector3.zero,88,new Vector3(0,150,-110));
                    Shot(camera,output,"hq-normal",new Vector3(-62,5,50),23,new Vector3(0,40,-40));
                    Shot(camera,output,"tracks-close",new Vector3(-40,0,20),17,new Vector3(0,23,-24));
                    Shot(camera,output,"track-junction-close",new Vector3(0,0,30),10,new Vector3(0,35,-8));
                    Shot(camera,output,"track-junction-normal",new Vector3(0,0,30),24,new Vector3(0,84,-19));
                }else if(Environment.GetEnvironmentVariable("WHITE_MERIDIAN_CAPTURE")=="foundation-representative"){
                    Shot(camera,output,"outpost-close",new Vector3(-40,0,20),11,new Vector3(0,19,-19));
                }else if(Environment.GetEnvironmentVariable("WHITE_MERIDIAN_CAPTURE")=="foundations"){
                    Shot(camera,output,"overview",Vector3.zero,88,new Vector3(0,150,-110));
                    Shot(camera,output,"hq-close",new Vector3(-62,5,50),11,new Vector3(0,19,-19));
                    Shot(camera,output,"hq-normal",new Vector3(-62,5,50),23,new Vector3(0,40,-40));
                    Shot(camera,output,"outpost-close",new Vector3(-40,0,20),11,new Vector3(0,19,-19));
                    Shot(camera,output,"outpost-normal",new Vector3(-40,0,20),20,new Vector3(0,35,-35));
                    Shot(camera,output,"ramp-join-close",new Vector3(-62,2.5f,28),12,new Vector3(12,23,-26));
                    Shot(camera,output,"rock-forms-close",new Vector3(-24,2,40),12,new Vector3(0,24,-22));
                    Shot(camera,output,"paired-tracks-close",new Vector3(-40,0,20),17,new Vector3(0,23,-24));
                    Shot(camera,output,"track-crossing-close",new Vector3(0,0,30),10,new Vector3(0,35,-8));
                }else if(Environment.GetEnvironmentVariable("WHITE_MERIDIAN_CAPTURE")!="geometry-audit"){
                    Shot(camera,output,"overview",Vector3.zero,88,new Vector3(0,150,-110));Shot(camera,output,"topology",Vector3.zero,83,new Vector3(0,210,0));
                    Shot(camera,output,"wreck-close",Vector3.zero,27,new Vector3(0,34,-46));Shot(camera,output,"shelf-barrier",new Vector3(-61,2,0),22,new Vector3(0,40,-24));
                    Shot(camera,output,"terrace-close",new Vector3(-62,3,43),16,new Vector3(0,26,-30));
                    Shot(camera,output,"outpost-close",new Vector3(-40,0,20),13,new Vector3(0,26,-30));
                    Shot(camera,output,"hq-normal",new Vector3(-62,5,50),23,new Vector3(0,40,-40));
                    foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1}){
                        Shot(camera,output,"ramp-"+sx+"-"+sz,new Vector3(sx*62,2.5f,sz*28),12,new Vector3(-sx*12,23,-sz*26));
                    }
                    Shot(camera,output,"inner-ramp",new Vector3(-40,2.5f,50),12,new Vector3(-20,24,-12));
                    Shot(camera,output,"gypsum-close",new Vector3(-24,2,40),12,new Vector3(0,24,-22));
                    Shot(camera,output,"tracks-close",new Vector3(-40,0,20),17,new Vector3(0,23,-24));
                    world.Fog.Reset();Shot(camera,output,"foundation-fog-hidden",new Vector3(-40,0,20),13,new Vector3(0,26,-30));
                    foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1}){
                        world.Fog.ShowPublic();
                        Shot(camera,output,"inner-ramp-"+sx+"-"+sz,new Vector3(sx*40,2.5f,sz*50),12,new Vector3(sx*20,24,-sz*12));
                    }
                }
                Shot(camera,output,"wreck-oblique",Vector3.zero,29,new Vector3(30,34,-40));
                Shot(camera,output,"cockpit-close",new Vector3(-27,1,-7),9,new Vector3(-14,18,-22));
                Shot(camera,output,"stern-close",new Vector3(28,1,7),9,new Vector3(20,18,-16));
                Shot(camera,output,"middle-wings-close",Vector3.zero,12,new Vector3(5,22,-20));
                // Runtime track masks discard CPU pixels. Read back only for this Editor audit.
                var roadMaterial=host.GetComponentsInChildren<MeshRenderer>().First(r=>r.name.StartsWith("Meridian decor expedition ")).sharedMaterial;
                var roadTexture=roadMaterial.GetTexture("_RoadMask");var roadTarget=RenderTexture.GetTemporary(2048,2048,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
                Graphics.Blit(roadTexture,roadTarget);var activeBefore=RenderTexture.active;RenderTexture.active=roadTarget;
                var roadReadback=new Texture2D(2048,2048,TextureFormat.RGBA32,false,true);roadReadback.ReadPixels(new Rect(0,0,2048,2048),0,0);roadReadback.Apply();RenderTexture.active=activeBefore;RenderTexture.ReleaseTemporary(roadTarget);
                foreach(var filter in host.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("Meridian decor expedition ")))foreach(var v in filter.sharedMesh.vertices){
                    var road=roadReadback.GetPixelBilinear(.5f+v.x/(2*(float)map.HalfExtent),.5f+v.z/(2*(float)map.HalfExtent));
                    if(road.r>.2f||road.g>.1f)throw new InvalidOperationException("Expedition decoration enters service tracks: "+filter.name);
                }
                UnityEngine.Object.DestroyImmediate(roadReadback);
                var decorations=host.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Meridian decor ")).ToArray();
                foreach(int scene in new[]{0,1,2}){
                    var prop=decorations.First(r=>r.name=="Meridian decor expedition "+scene+" casing");
                    var target=prop.bounds.center;var facing=-new Vector3(target.x,0,target.z).normalized;
                    Shot(camera,output,"decor-"+scene,target,4,new Vector3(facing.x*14,12,facing.z*14));
                    foreach(var renderer in decorations)renderer.enabled=false;
                    Shot(camera,output,"decor-"+scene+"-before",target,4,new Vector3(facing.x*14,12,facing.z*14));
                    foreach(var renderer in decorations)renderer.enabled=true;
                }
                File.WriteAllLines(Path.Combine(output,"decor-meshes.txt"),host.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("Meridian decor ")).Select(f=>f.name+" vertices="+f.sharedMesh.vertexCount+" bounds="+f.sharedMesh.bounds));
                // Resource routing is shared; ensure the new map does not change older maps.
                if(IndustrialLightingProfile.Load(map.Id).id!="white-meridian-warm-v1"||IndustrialLightingProfile.Load().id!="industrial-warm-cool-v1"||IndustrialLightingProfile.Load("three-crossings-greybox-v1").id!="three-crossings-warm-v1")throw new InvalidOperationException("Map lighting routing");
                foreach(var filter in host.GetComponentsInChildren<MeshFilter>())foreach(var v in filter.sharedMesh.vertices)if(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.y)||float.IsInfinity(v.z))throw new InvalidOperationException("Nonfinite production mesh: "+filter.name);
                var envelope=new System.Collections.Generic.List<string>();
                foreach(var filter in host.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("Wreck "))){
                    var words=filter.name.Split(' ');if(words.Length<3||!int.TryParse(words[1],out int section))continue;
                    double outside=filter.sharedMesh.vertices.Select(v=>Math.Sqrt(map.Wreck[section].Footprint.DistanceSquared(new NavPoint(v.x,v.z)))).DefaultIfEmpty(0).Max();
                    if(outside>.001)throw new InvalidOperationException("Wreck dressing enters passage: "+filter.name+" by "+outside);
                    envelope.Add(filter.name+" vertices="+filter.sharedMesh.vertexCount+" maxOutsideMetres="+outside.ToString("F4",System.Globalization.CultureInfo.InvariantCulture));
                }
                File.WriteAllLines(Path.Combine(output,"wreck-envelope.txt"),envelope);
                AuditTerrain(host,map,output);
                File.WriteAllLines(Path.Combine(output,"wreck-meshes.txt"),host.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("Wreck")).Select(f=>f.name+" "+f.sharedMesh.vertexCount+" "+string.Join(";",f.sharedMesh.vertices.Take(4).Select(v=>v.ToString("F3")))));
                File.WriteAllText(Path.Combine(output,"render-state.txt"),"Rendering-only Editor inspection. Production map meshes/materials. Static initial-state site and HQ projection with fog reveal. No gameplay replay, no Player/device/human acceptance. "+map.Id+"@"+map.Revision+"\n");
            }
            var textures=new System.Collections.Generic.List<string>();
            foreach(string family in new[]{"Concrete","Rock","Earth"})foreach(string suffix in new[]{"","-b","-c"}){
                var texture=Resources.Load<Texture2D>("Environment/NaturalFrontier/"+family+suffix);if(!texture)continue;
                var path=AssetDatabase.GetAssetPath(texture);var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                textures.Add(path+" | "+texture.width+"x"+texture.height+" "+texture.format+" mipCount="+texture.mipmapCount+" wrap="+texture.wrapMode+" filter="+texture.filterMode+" aniso="+texture.anisoLevel+" compression="+importer.textureCompression+" npot="+importer.npotScale);
            }
            File.WriteAllLines(Path.Combine(output,"texture-import.txt"),textures);
            Debug.Log("WHITE_MERIDIAN_RENDER_PASS "+output);
        }
        private static void AuditTerrain(GameObject host,WhiteMeridianMap map,string output)
        {
            var report=new System.Collections.Generic.List<string>{
                "Actual production mesh vertices/triangles, not a simulation test. Distances and heights in metres.",
                "Interior deltas are rendered minus physical height. Exterior means physical basin (unsupported at raised height).",
                "Edge bands are distance to the authoritative support polygon. Bounds apply only to this profile/scale."
            };
            string F(float value)=>value.ToString("F4",System.Globalization.CultureInfo.InvariantCulture);
            float Edge(MapSupport support,Vector3 v){
                float distance=float.MaxValue;var polygon=support.Bounds.Footprint;
                for(int i=0;i<polygon.Vertices.Count;i++){
                    var a=polygon.Vertices[i];var b=polygon.Vertices[(i+1)%polygon.Vertices.Count];
                    var delta=new Vector2((float)(b.X-a.X),(float)(b.Z-a.Z));var q=new Vector2(v.x-(float)a.X,v.z-(float)a.Z);
                    distance=Mathf.Min(distance,(q-delta*Mathf.Clamp01(Vector2.Dot(q,delta)/delta.sqrMagnitude)).magnitude);
                }
                return distance;
            }
            foreach(var filter in host.GetComponentsInChildren<MeshFilter>()){
                var mesh=filter.sharedMesh;if(!mesh||!mesh.name.StartsWith("Meridian continuous geology"))continue;
                var vertices=mesh.vertices;var normals=mesh.normals;
                var bands=new[]{.16f,.32f,.48f,.64f,1f,float.MaxValue};
                var count=new int[2,bands.Length];var min=new float[2,bands.Length];var max=new float[2,bands.Length];
                for(int r=0;r<2;r++)for(int k=0;k<bands.Length;k++){min[r,k]=float.MaxValue;max[r,k]=float.MinValue;}
                var levels=new[]{.02f,.1f,.25f,1f,2.5f,4f};var reach=new float[levels.Length];var shallowest=new float[levels.Length];
                for(int k=0;k<levels.Length;k++)shallowest[k]=90;
                int exterior=0,nonfinite=0;float maxExteriorHeight=0;
                for(int i=0;i<vertices.Length;i++){
                    var v=vertices[i];if(float.IsNaN(v.y)||float.IsInfinity(v.y)||float.IsNaN(normals[i].y))nonfinite++;
                    var point=new NavPoint(v.x,v.z);var support=map.SupportAt(point);
                    if(support!=null&&support.Id!="salt-basin"){
                        float edge=Edge(support,v);int band=0;while(edge>=bands[band])band++;
                        int role=support.Id.EndsWith("ramp")?0:1;float delta=v.y-(float)support.HeightAt(point);
                        count[role,band]++;min[role,band]=Mathf.Min(min[role,band],delta);max[role,band]=Mathf.Max(max[role,band],delta);
                    }else if(v.y>.02f){
                        float distance=float.MaxValue;
                        foreach(var raised in map.Supports)if(raised.Id!="salt-basin")distance=Mathf.Min(distance,Edge(raised,v));
                        exterior++;maxExteriorHeight=Mathf.Max(maxExteriorHeight,v.y);
                        for(int k=0;k<levels.Length;k++)if(v.y>=levels[k]){
                            reach[k]=Mathf.Max(reach[k],distance);
                            shallowest[k]=Mathf.Min(shallowest[k],Vector3.Angle(Vector3.up,normals[i]));
                        }
                    }
                }
                report.Add(mesh.name+" vertices="+mesh.vertexCount+" triangles="+mesh.triangles.Length/3+" nonfinite="+nonfinite);
                for(int r=0;r<2;r++)for(int k=0;k<bands.Length;k++)report.Add((r==0?"ramp":"shelf")+" edgeBand="+(k==0?"0":F(bands[k-1]))+".."+(k==bands.Length-1?"interior":F(bands[k]))+" count="+count[r,k]+" deltaMin="+F(min[r,k])+" deltaMax="+F(max[r,k]));
                report.Add("exteriorVertices="+exterior+" maxExteriorHeight="+F(maxExteriorHeight));
                for(int k=0;k<levels.Length;k++)report.Add("exteriorHeight>="+F(levels[k])+" maxDistanceFromPhysicalFootprint="+F(reach[k])+" minSlopeDegrees="+F(shallowest[k]));
                // Read the actual indexed grid triangles, including the landing between
                // samples. This does not resample the height function used to generate it.
                int nx=1;while(nx<vertices.Length&&vertices[nx].z==vertices[0].z)nx++;
                int nz=vertices.Length/nx;float step=vertices[1].x-vertices[0].x;
                float Sample(float x,float z){
                    float gx=(x-vertices[0].x)/step,gz=(z-vertices[0].z)/step;
                    int ix=Mathf.Clamp(Mathf.FloorToInt(gx),0,nx-2),iz=Mathf.Clamp(Mathf.FloorToInt(gz),0,nz-2);
                    float u=gx-ix,v=gz-iz;int i=iz*nx+ix;
                    return u+v<=1?vertices[i].y*(1-u-v)+vertices[i+1].y*u+vertices[i+nx].y*v:vertices[i+1].y*(1-v)+vertices[i+nx].y*(1-u)+vertices[i+nx+1].y*(u+v-1);
                }
                foreach(var ramp in map.Supports.Where(r=>r.Id.EndsWith("ramp")&&(r.Bounds.MinX+r.Bounds.MaxX)*(vertices[0].x<0?-1:1)>0)){
                    var b=ramp.Bounds;bool alongX=ramp.Gradient.X!=0;float gradient=(float)(alongX?ramp.Gradient.X:ramp.Gradient.Z);
                    float low=(float)(alongX?b.MinX:b.MinZ),high=(float)(alongX?b.MaxX:b.MaxZ);if(gradient<0){float swap=low;low=high;high=swap;}
                    foreach(float t in new[]{-.0625f,0,.03125f,.0625f,.125f,.5f,.875f,.9375f,.96875f,1,1.0625f}){
                        float axis=Mathf.LerpUnclamped(low,high,t),x=alongX?axis:(float)(b.MinX+b.MaxX)*.5f,z=alongX?(float)(b.MinZ+b.MaxZ)*.5f:axis;
                        float physical=(float)map.SurfaceHeight(new NavPoint(x,z));
                        report.Add(ramp.SurfaceId+" center t="+F(t)+" physical="+F(physical)+" mesh="+F(Sample(x,z))+" delta="+F(Sample(x,z)-physical));
                    }
                }
            }
            File.WriteAllLines(Path.Combine(output,"terrain-mesh.txt"),report);
        }
        private static void Shot(Camera camera,string output,string name,Vector3 target,float size,Vector3 offset){
            camera.transform.position=target+offset;camera.transform.LookAt(target);camera.orthographicSize=size;
            var rt=new RenderTexture(1600,1200,24){antiAliasing=4};rt.Create();camera.targetTexture=rt;camera.Render();var previous=RenderTexture.active;RenderTexture.active=rt;var png=new Texture2D(1600,1200,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,1600,1200),0,0);png.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),png.EncodeToPNG());RenderTexture.active=previous;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(png);UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
