using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed class HawkStagedRotor : MonoBehaviour
    {
        double lastTime,angle;bool spinning;
        public void Render(int phase,double speedFraction,double now,double degreesPerSecond)
        {
            if(phase!=3){angle=0;spinning=false;}
            else {if(spinning)angle+=(now-lastTime)*speedFraction*degreesPerSecond;spinning=true;}
            lastTime=now;
            foreach(var t in GetComponentsInChildren<Transform>(true)){
                if(t.name=="mainRotor")t.localRotation=Quaternion.Euler(0,(float)(angle%360),0);
                if(t.name=="tailRotor")t.localRotation=Quaternion.Euler((float)(angle%360),0,0);
            }
        }
    }
}
