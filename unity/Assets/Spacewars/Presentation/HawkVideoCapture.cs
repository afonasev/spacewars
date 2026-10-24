#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
namespace Spacewars.Presentation
{
    // Actual rendered frames with their wall-clock timestamps. Encoding runs off the
    // Unity thread; no fixed-delta/captureFramerate changes to gameplay or playback.
    public sealed class HawkVideoCapture : MonoBehaviour
    {
        readonly BlockingCollection<byte[]> queue=new BlockingCollection<byte[]>(8);
        readonly List<string> times=new List<string>{"frame,seconds"};
        RenderTexture target;Process encoder;Thread writer;Exception failure;bool recording;int pending,frames,dropped;string folder;
        public void Begin(string output)
        {
            folder=output;var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-hawkCaptureEncoder");
            if(index<0||index+1>=args.Length)throw new InvalidOperationException("Missing explicit Hawk capture encoder");
            target=new RenderTexture(Screen.width,Screen.height,0,RenderTextureFormat.ARGB32);target.Create();
            encoder=Process.Start(new ProcessStartInfo(args[index+1],"-y -loglevel error -f rawvideo -pixel_format rgb24 -video_size "+Screen.width+"x"+Screen.height+" -framerate 60 -i pipe:0 -an -c:v mjpeg -q:v 3 -threads 2 \""+Path.Combine(folder,"capture.mkv")+"\""){UseShellExecute=false,RedirectStandardInput=true,CreateNoWindow=true});
            writer=new Thread(()=>{try{foreach(var bytes in queue.GetConsumingEnumerable())encoder.StandardInput.BaseStream.Write(bytes,0,bytes.Length);encoder.StandardInput.Close();}catch(Exception e){failure=e;}}){IsBackground=true,Name="Hawk evidence encoder"};writer.Start();recording=true;StartCoroutine(Capture());
        }
        IEnumerator Capture()
        {
            while(recording){yield return new WaitForEndOfFrame();if(!recording)break;
                if(pending>=4||queue.Count>=7){dropped++;continue;}
                double stamp=Time.realtimeSinceStartupAsDouble;ScreenCapture.CaptureScreenshotIntoRenderTexture(target);pending++;
                AsyncGPUReadback.Request(target,0,TextureFormat.RGB24,request=>{
                    pending--;if(request.hasError){failure=new InvalidOperationException("GPU readback failed");return;}
                    var bytes=request.GetData<byte>().ToArray();
                    if(!queue.TryAdd(bytes)){dropped++;return;}
                    times.Add((frames++)+","+stamp.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                });
            }
        }
        public IEnumerator Finish()
        {
            recording=false;while(pending>0)yield return null;queue.CompleteAdding();while(writer.IsAlive)yield return null;
            while(!encoder.HasExited)yield return null;
            File.WriteAllLines(Path.Combine(folder,"frame-times.csv"),times);File.WriteAllText(Path.Combine(folder,"capture-result.txt"),"frames="+frames+" dropped="+dropped+" encoderExit="+encoder.ExitCode+" error="+failure);
            target.Release();Destroy(target);if(failure!=null||encoder.ExitCode!=0)throw new InvalidOperationException("Hawk capture failed",failure);
        }
    }
}
#endif
