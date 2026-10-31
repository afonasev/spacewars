using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
 // Two articulated telescoping rams and a rear elevation bearing. No frame clocks.
 public sealed class ShkvalMechanismPresentation:MonoBehaviour
 {
  private Transform launcher;
  private readonly Transform[] housings=new Transform[2],rods=new Transform[2];
  private readonly Quaternion[] rodRestRotations=new Quaternion[2];
  private static readonly Vector3 BaseDirection=new Vector3(0,.44f,-.27f);
  private static readonly Vector3 RodDirection=new Vector3(0,.18f,-.11f);
  public void Initialize(Transform pitch)
  {
   launcher=pitch;
   var nodes=GetComponentsInChildren<Transform>(true);
   for(int i=0;i<2;i++){
    string name=i==0?"elevationRamLeft":"elevationRamRight";
    foreach(var node in nodes)if(node.name==name){housings[i]=node;foreach(Transform child in node)if(child.name.StartsWith("elevationRod"))rods[i]=child;rodRestRotations[i]=rods[i].localRotation;}
   }
  }
  public void Aim(double pitch)
  {
   var offset=ShkvalLaunchGeometry.LauncherPosition(pitch);
   launcher.localPosition=new Vector3((float)offset.X,(float)offset.Y,(float)offset.Z);
   launcher.localRotation=Quaternion.Euler(-(float)pitch*Mathf.Rad2Deg,0,0);
   for(int i=0;i<2;i++){
    var housing=housings[i];if(!housing)continue;
    float x=i==0?.49f:-.49f; // glTF X reflection of Blender's left/right.
    var anchor=new Vector3(x,-.06f,.4f);
    var upperNeutral=new Vector3(x,.38f,.13f);
    var upperLocal=Quaternion.Euler(32,0,0)*(upperNeutral-new Vector3(0,.22f,0));
    var upper=launcher.localPosition+launcher.localRotation*upperLocal;
    var rotation=Quaternion.FromToRotation(BaseDirection,upper-anchor);
    housing.localRotation=rotation;housing.localPosition=anchor-rotation*anchor;
    var rod=rods[i];var lower=new Vector3(x,.20f,.24f);
    var vector=Quaternion.Inverse(rotation)*(upper-housing.localPosition)-lower;
    rod.localRotation=Quaternion.LookRotation(Vector3.Cross(rodRestRotations[i]*Vector3.right,vector).normalized,vector);
    rod.localScale=new Vector3(1,vector.magnitude/RodDirection.magnitude,1);
   }
  }
 }
}
