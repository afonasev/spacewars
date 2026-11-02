using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
namespace Spacewars.Simulation
{
 // Shared authored surfaces; these queries never read navigation, Unity objects or fog.
 public static partial class BuildingCombatGeometry
 {
  public static string Version=>BuildingCombatData.Version+"/sentry:"+SentryCombatData.SourceHash+"/repair:"+RepairTowerCombatData.SourceHash+"/gauss:"+GaussCombatData.SourceHash;
  public static double ApronLift=>BuildingCombatData.ApronLift;
  internal static string PreparedModelKey(PlayableBuildingKind kind,bool upgraded)=>kind==PlayableBuildingKind.Headquarters?"headquarters-a":kind==PlayableBuildingKind.Factory?"factory-armored":kind==PlayableBuildingKind.Mine?"mine-armored":kind==PlayableBuildingKind.Outpost?"outpost-b":kind==PlayableBuildingKind.ScientificCenter?(upgraded?"research-scanner-upgraded":"research-scanner"):kind==PlayableBuildingKind.Refinery?(upgraded?"refineryUpgraded":"refinery"):"hangar";
  static readonly Dictionary<string,CombatMesh> Cache=new Dictionary<string,CombatMesh>();
  public static double ModelScale(PlayableProfile p,PlayableBuildingKind k)=>TerritoryRules.Defensive(k)?p.TurretModelScale:k==PlayableBuildingKind.Hangar?p.HangarModelHeightMeters/1.25*p.HangarModelScale:k==PlayableBuildingKind.ScientificCenter?p.ScienceModelHeightMeters/1.6407*p.ScienceModelScale:k==PlayableBuildingKind.Factory?p.FactoryModelHeightMeters/2.06*p.FactoryModelScale:k==PlayableBuildingKind.Refinery?p.RefineryModelHeightMeters/2.545*p.RefineryModelScale:k==PlayableBuildingKind.Outpost?p.OutpostModelHeightMeters/4.5094*p.OutpostModelScale:k==PlayableBuildingKind.Mine?p.MineModelHeightMeters/1.9945*p.MineModelScale:p.HeadquartersModelHeightMeters/4.2*p.HeadquartersModelScale;
  // The existing construction presentation grows only the vertical axis.
  public static double ConstructionHeight(double progress)=>.2+.8*Math.Max(0,Math.Min(1,progress));
  public static BuildingCombatPose Pose(PlayableProfile p,PlayableBuildingKind kind,bool upgraded,NavPoint at,double ground,double heading=TerritoryRules.BuildingHeading,double opening=0,double platform=0,double constructionProgress=1)
  {
   if(TerritoryRules.Defensive(kind))return new BuildingCombatPose(new SentryCombatPose(at,ground+ApronLift,p.TurretModelScale,Math.PI/2,0,constructionProgress,kind==PlayableBuildingKind.RepairTower,kind==PlayableBuildingKind.GaussTower));
   string key=PreparedModelKey(kind,upgraded);
   CombatMesh mesh;lock(Cache){if(!Cache.TryGetValue(key,out mesh)){mesh=CombatMesh.Decode(BuildingCombatData.Encoded(key));mesh.PreparedKey=key;Cache.Add(key,mesh);}}
   return new BuildingCombatPose(mesh,at,ground+ApronLift,ModelScale(p,kind),Math.PI/2-heading,Math.Max(0,Math.Min(1,opening)),(HawkHangarGeometry.RaisedPlatformTop-HawkHangarGeometry.RestPlatformTop-ApronLift/ModelScale(p,kind))*Math.Max(0,Math.Min(1,platform)),ConstructionHeight(constructionProgress));
  }
 }
 public readonly partial struct BuildingCombatPose
 {
  readonly CombatMesh mesh;readonly V origin;readonly double scale,cos,sin,opening,platform,height;
  internal BuildingCombatPose(CombatMesh mesh,NavPoint at,double y,double scale,double yaw,double opening,double platform,double height){this.mesh=mesh;origin=new V(at.X,y,at.Z);this.scale=scale;cos=Math.Cos(yaw);sin=Math.Sin(yaw);this.opening=opening;this.platform=platform;this.height=height;sentry=null;}
  readonly SentryCombatPose? sentry;
  internal BuildingCombatPose(SentryCombatPose pose){mesh=null;origin=default;scale=cos=sin=opening=platform=height=0;sentry=pose;}
  public double Reach=>sentry.HasValue?sentry.Value.Reach:mesh.Reach*scale;
  V Local(BallisticPoint p){var d=new V(p.X,p.Y,p.Z)-origin;return new V(cos*d.X-sin*d.Z,d.Y,sin*d.X+cos*d.Z)/scale;}
  BallisticPoint World(V p){p=p*scale;return new BallisticPoint(origin.X+cos*p.X+sin*p.Z,origin.Y+p.Y,origin.Z-sin*p.X+cos*p.Z);}
  public bool Overlaps(BallisticPoint p,double radius)=>sentry.HasValue?sentry.Value.Overlaps(p,radius):mesh.Overlaps(Local(p),radius/scale,opening,platform,height);
  public bool Segment(BallisticPoint from,BallisticPoint to,double radius,out double fraction,out BallisticPoint contact)
  {if(sentry.HasValue)return sentry.Value.Segment(from,to,radius,out fraction,out contact);fraction=mesh.Segment(Local(from),Local(to),radius/scale,opening,platform,height);contact=double.IsPositiveInfinity(fraction)?to:new BallisticPoint(from.X+(to.X-from.X)*fraction,from.Y+(to.Y-from.Y)*fraction,from.Z+(to.Z-from.Z)*fraction);return !double.IsPositiveInfinity(fraction);}
  public BallisticPoint Closest(BallisticPoint p)=>sentry.HasValue?sentry.Value.Closest(p):World(mesh.Closest(Local(p),opening,platform,height));
 }
 internal readonly struct V
 {
  internal readonly double X,Y,Z;internal V(double x,double y,double z){X=x;Y=y;Z=z;}
  internal double Length2=>X*X+Y*Y+Z*Z;
  internal double Axis(int i)=>i==0?X:i==1?Y:Z;
  public static V operator +(V a,V b)=>new V(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
  public static V operator -(V a,V b)=>new V(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
  public static V operator *(V a,double n)=>new V(a.X*n,a.Y*n,a.Z*n);
  public static V operator /(V a,double n)=>a*(1/n);
  internal static double Dot(V a,V b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
  internal static V Cross(V a,V b)=>new V(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
  internal static V Min(V a,V b)=>new V(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Min(a.Z,b.Z));
  internal static V Max(V a,V b)=>new V(Math.Max(a.X,b.X),Math.Max(a.Y,b.Y),Math.Max(a.Z,b.Z));
 }
 internal readonly struct CombatTriangle
 {
  internal readonly V A,B,C;internal CombatTriangle(V a,V b,V c){A=a;B=b;C=c;}
  internal V Min=>V.Min(A,V.Min(B,C));internal V Max=>V.Max(A,V.Max(B,C));internal V Center=>(A+B+C)/3;
  internal V Closest(V p)
  {
   var ab=B-A;var ac=C-A;var ap=p-A;double d1=V.Dot(ab,ap),d2=V.Dot(ac,ap);if(d1<=0&&d2<=0)return A;
   var bp=p-B;double d3=V.Dot(ab,bp),d4=V.Dot(ac,bp);if(d3>=0&&d4<=d3)return B;
   double vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return A+ab*(d1/(d1-d3));
   var cp=p-C;double d5=V.Dot(ab,cp),d6=V.Dot(ac,cp);if(d6>=0&&d5<=d6)return C;
   double vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return A+ac*(d2/(d2-d6));
   double va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0)return B+(C-B)*((d4-d3)/((d4-d3)+(d5-d6)));
   double sum=va+vb+vc;if(Math.Abs(sum)<1e-24)return A;return A+ab*(vb/sum)+ac*(vc/sum);
  }
  internal bool Ray(V a,V d,out double t)
  {
   var ab=B-A;var ac=C-A;var h=V.Cross(d,ac);double det=V.Dot(ab,h);t=0;if(Math.Abs(det)<1e-15)return false;
   var s=a-A;double u=V.Dot(s,h)/det;if(u<0||u>1)return false;var q=V.Cross(s,ab);double v=V.Dot(d,q)/det;if(v<0||u+v>1)return false;t=V.Dot(ac,q)/det;return t>1e-10&&t<=1;
  }
  internal double Sweep(V a,V b,double r)
  {
   // Analytic swept sphere against a face, three edge capsules and vertices.
   if((Closest(a)-a).Length2<=r*r+1e-20)return 0;
   var d=b-a;var normal=V.Cross(B-A,C-A);double len=Math.Sqrt(normal.Length2);if(len<1e-14)return double.PositiveInfinity;normal/=len;
   double start=V.Dot(a-A,normal),speed=V.Dot(d,normal),best=double.PositiveInfinity;
   if(Math.Abs(speed)>1e-15)for(int side=-1;side<=1;side+=2){double t=(side*r-start)/speed;if(t<0||t>1||t>=best)continue;var p=a+d*t-normal*(side*r);if(V.Dot(V.Cross(B-A,p-A),normal)>=-1e-10&&V.Dot(V.Cross(C-B,p-B),normal)>=-1e-10&&V.Dot(V.Cross(A-C,p-C),normal)>=-1e-10)best=t;}
   best=Math.Min(best,Edge(a,d,A,B,r));best=Math.Min(best,Edge(a,d,B,C,r));return Math.Min(best,Edge(a,d,C,A,r));
  }
  static double Root(V p,V d,double r){double aa=d.Length2;if(aa<1e-24)return double.PositiveInfinity;double bb=V.Dot(p,d),cc=p.Length2-r*r,disc=bb*bb-aa*cc;if(disc<0)return double.PositiveInfinity;double t=(-bb-Math.Sqrt(disc))/aa;return t>=0&&t<=1?t:double.PositiveInfinity;}
  static double Edge(V a,V d,V p,V q,double r)
  {
   double best=Math.Min(Root(a-p,d,r),Root(a-q,d,r));var e=q-p;double l=e.Length2;if(l<1e-24)return best;var w=a-p;var n=w-e*(V.Dot(w,e)/l);var v=d-e*(V.Dot(d,e)/l);double t=Root(n,v,r);
   if(!double.IsPositiveInfinity(t)){double u=V.Dot(w+d*t,e)/l;if(u>=0&&u<=1)best=Math.Min(best,t);}return best;
  }
 }
 internal sealed class CombatNode
 {
  internal V Min,Max;internal int Start,Count;internal CombatNode Left,Right;
  internal static CombatNode Build(CombatTriangle[] tris,int[] order,int start,int count)
  {
   var n=new CombatNode{Start=start,Count=count,Min=new V(double.PositiveInfinity,double.PositiveInfinity,double.PositiveInfinity),Max=new V(double.NegativeInfinity,double.NegativeInfinity,double.NegativeInfinity)};
   for(int i=start;i<start+count;i++){n.Min=V.Min(n.Min,tris[order[i]].Min);n.Max=V.Max(n.Max,tris[order[i]].Max);}
   // Eight triangles/leaf is a fixed acceleration layout invariant, not gameplay tuning.
   if(count>8){var size=n.Max-n.Min;int axis=size.X>=size.Y&&size.X>=size.Z?0:size.Y>=size.Z?1:2;Array.Sort(order,start,count,Comparer<int>.Create((a,b)=>{int c=tris[a].Center.Axis(axis).CompareTo(tris[b].Center.Axis(axis));return c!=0?c:a.CompareTo(b);}));int half=count/2;n.Left=Build(tris,order,start,half);n.Right=Build(tris,order,start+half,count-half);}
   return n;
  }
  internal double Distance2(V p,double height=1){double d=0;for(int i=0;i<3;i++){double x=p.Axis(i),lo=Min.Axis(i)*(i==1?height:1),hi=Max.Axis(i)*(i==1?height:1),v=x<lo?lo-x:x>hi?x-hi:0;d+=v*v;}return d;}
  internal bool Segment(V a,V b,double r,double max=1,double height=1)
  {double enter=0,exit=max;var d=b-a;for(int i=0;i<3;i++){double p=a.Axis(i),v=d.Axis(i),lo=Min.Axis(i)*(i==1?height:1)-r,hi=Max.Axis(i)*(i==1?height:1)+r;if(Math.Abs(v)<1e-18){if(p<lo||p>hi)return false;}else{double t1=(lo-p)/v,t2=(hi-p)/v;enter=Math.Max(enter,Math.Min(t1,t2));exit=Math.Min(exit,Math.Max(t1,t2));if(enter>exit)return false;}}return true;}
 }
 internal sealed class CombatPart
 {
  internal int Motion,Index;internal double Closed;internal V Move;internal CombatTriangle[] Triangles;internal int[] Order;internal CombatNode Tree;
  internal bool Active(double open,double roller)=>Motion==1?Closed+BuildingCombatData.DoorTravel*open<roller:Motion==2?open<1:Motion!=3||open<.01;
  internal V Shift(double open,double platform)=>Motion==1?Move*(BuildingCombatData.DoorTravel*open):Motion==4?Move*platform:default;
  V Petal(V p,double open){double a=Index*Math.PI/4+Math.PI/8;p+=new V(Math.Cos(a),0,Math.Sin(a))*(2*.965*open);double r=Math.Sqrt(p.X*p.X+p.Z*p.Z);return r>.965?new V(p.X*.965/r,p.Y,p.Z*.965/r):p;}
  internal static V Stretch(V p,double height)=>new V(p.X,p.Y*height,p.Z);
  internal CombatTriangle Triangle(int i,double open,double height=1){var t=Triangles[i];if(Motion==2)t=new CombatTriangle(Petal(t.A,open),Petal(t.B,open),Petal(t.C,open));return height==1?t:new CombatTriangle(Stretch(t.A,height),Stretch(t.B,height),Stretch(t.C,height));}
  internal bool Inside(V p,double open,double height)
  {
   p=new V(p.X,p.Y/height,p.Z);
   if(Motion==2||Tree.Distance2(p)>0)return false;var d=new V(1,.3713906763541037,.217173291)*((Tree.Max-Tree.Min).Length2+1);return Winding(Tree,p,d)!=0;
  }
  int Winding(CombatNode n,V p,V d)
  {if(!n.Segment(p,p+d,0))return 0;if(n.Left!=null)return Winding(n.Left,p,d)+Winding(n.Right,p,d);int sum=0;for(int i=n.Start;i<n.Start+n.Count;i++){var t=Triangles[Order[i]];if(t.Ray(p,d,out _))sum+=V.Dot(V.Cross(t.B-t.A,t.C-t.A),d)>0?1:-1;}return sum;}
  internal V Nearest(V p,double open,double height,out double distance)
  {distance=double.PositiveInfinity;V result=p;if(Motion==2){for(int i=0;i<Triangles.Length;i++){var q=Triangle(i,open,height).Closest(p);double d=(q-p).Length2;if(d<distance){distance=d;result=q;}}}else Nearest(Tree,p,height,ref distance,ref result);return result;}
  void Nearest(CombatNode n,V p,double height,ref double best,ref V result)
  {if(n.Distance2(p,height)>best)return;if(n.Left!=null){Nearest(n.Left,p,height,ref best,ref result);Nearest(n.Right,p,height,ref best,ref result);return;}for(int i=n.Start;i<n.Start+n.Count;i++){var q=Triangle(Order[i],0,height).Closest(p);double d=(q-p).Length2;if(d<best){best=d;result=q;}}}
  internal double Sweep(V a,V b,double r,double open,double height)
  {double best=double.PositiveInfinity;if(Motion==2){for(int i=0;i<Triangles.Length;i++)best=Math.Min(best,Triangle(i,open,height).Sweep(a,b,r));}else Sweep(Tree,a,b,r,height,ref best);return best;}
  void Sweep(CombatNode n,V a,V b,double r,double height,ref double best)
  {if(!n.Segment(a,b,r,Math.Min(1,best),height))return;if(n.Left!=null){Sweep(n.Left,a,b,r,height,ref best);Sweep(n.Right,a,b,r,height,ref best);return;}for(int i=n.Start;i<n.Start+n.Count;i++)best=Math.Min(best,Triangle(Order[i],0,height).Sweep(a,b,r));}
 }
 internal sealed class CombatMesh
 {
  internal string PreparedKey;internal CombatPart[] Parts;internal double Roller,Reach;
  internal static CombatMesh Decode(string data)
  {
   using var compressed=new MemoryStream(Convert.FromBase64String(data));using var stream=new DeflateStream(compressed,CompressionMode.Decompress);using var r=new BinaryReader(stream);
   int count=r.ReadInt32();if(count<1||count>4096)throw new ArgumentException("Invalid combat part count");var mesh=new CombatMesh{Parts=new CombatPart[count],Roller=r.ReadSingle()};
   V Read(){double x=r.ReadSingle(),y=r.ReadSingle(),z=r.ReadSingle();if(double.IsNaN(x+y+z)||double.IsInfinity(x+y+z))throw new ArgumentException("Invalid combat vertex");return new V(x,y,z);}
   for(int k=0;k<count;k++)
   {
    var p=new CombatPart{Motion=r.ReadInt32(),Index=r.ReadInt32(),Closed=r.ReadSingle(),Move=Read()};int vc=r.ReadInt32(),ic=r.ReadInt32();if(vc<1||vc>2000000||ic<3||ic%3!=0||ic>6000000)throw new ArgumentException("Invalid combat surface");
    var vertices=new V[vc];for(int i=0;i<vc;i++){vertices[i]=Read();mesh.Reach=Math.Max(mesh.Reach,Math.Sqrt(vertices[i].X*vertices[i].X+vertices[i].Z*vertices[i].Z));}
    p.Triangles=new CombatTriangle[ic/3];p.Order=new int[ic/3];for(int i=0;i<ic/3;i++){int a=r.ReadInt32(),b=r.ReadInt32(),c=r.ReadInt32();if(a<0||b<0||c<0||a>=vc||b>=vc||c>=vc)throw new ArgumentException("Invalid combat triangle index");p.Triangles[i]=new CombatTriangle(vertices[a],vertices[b],vertices[c]);p.Order[i]=i;}
    p.Tree=CombatNode.Build(p.Triangles,p.Order,0,p.Triangles.Length);mesh.Parts[k]=p;
   }
   return mesh;
  }
  internal bool Overlaps(V p,double radius,double open,double platform,double height)
  {foreach(var part in Parts){if(!part.Active(open,Roller))continue;var at=p-CombatPart.Stretch(part.Shift(open,platform),height);if(part.Motion!=2&&part.Tree.Distance2(at,height)>radius*radius)continue;part.Nearest(at,open,height,out double d);if(d<=radius*radius+1e-20||part.Inside(at,open,height))return true;}return false;}
  internal double Segment(V a,V b,double radius,double open,double platform,double height)
  {double best=double.PositiveInfinity;foreach(var part in Parts){if(!part.Active(open,Roller))continue;var offset=CombatPart.Stretch(part.Shift(open,platform),height);double t=part.Sweep(a-offset,b-offset,radius,open,height);if(t<best)best=t;}return best;}
  internal V Closest(V p,double open,double platform,double height)
  {V result=p;double best=double.PositiveInfinity;foreach(var part in Parts){if(!part.Active(open,Roller))continue;var offset=CombatPart.Stretch(part.Shift(open,platform),height);var at=p-offset;if(part.Inside(at,open,height))return p;var q=part.Nearest(at,open,height,out double d);if(d<best){best=d;result=q+offset;}}return result;}
 }
}
