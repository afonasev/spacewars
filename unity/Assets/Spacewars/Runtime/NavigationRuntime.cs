using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed class NavigationView
    {
        private readonly IReadOnlyList<NavPoint> positions;
        public NavigationView(long tick,NavPoint[] positions,int rejected,string failure){Tick=tick;this.positions=Array.AsReadOnly((NavPoint[])positions.Clone());Rejected=rejected;Failure=failure;}
        public long Tick{get;} public IReadOnlyList<NavPoint> Positions{get{return positions;}} public int Rejected{get;} public string Failure{get;}
    }
    public sealed class NavigationIntent
    {
        public NavigationIntent(int entity,NavPoint goal,bool stop=false,bool hold=false){Entity=entity;Goal=goal;Stop=stop;Hold=hold;}
        public int Entity{get;} public NavPoint Goal{get;} public bool Stop{get;} public bool Hold{get;}
    }
    // Live diagnostic driver. Route services can stop entirely without blocking 30 Hz domain ticks.
    public sealed class NavigationRuntime : IDisposable
    {
        private readonly NavigationSession session;
        private readonly NavMailbox<NavigationIntent> intents=new NavMailbox<NavigationIntent>();
        private readonly Thread thread;
        private int stop,stopped;
        private NavigationView latest;
        public NavigationRuntime(long generation,NavGeometry geometry,NavigationProfile profile,NavPoint[] starts)
        {
            session=new NavigationSession(generation,geometry,profile);
            for(int i=0;i<starts.Length;i++)session.Crowd.Add(i,starts[i]);
            latest=View(0,null);
            thread=new Thread(Run){IsBackground=true,Name="Spacewars.Navigation.Live"};thread.Start();
        }
        public NavigationView Latest{get{return Volatile.Read(ref latest);}}
        public NavMailbox<NavigationRequest> Requests{get{return session.Requests;}}
        public NavMailbox<NavigationAnswer> Answers{get{return session.Answers;}}
        public bool IsStopped{get{return Volatile.Read(ref stopped)!=0;}}
        public bool TrySubmit(NavigationIntent intent){return Volatile.Read(ref stop)==0&&!IsStopped&&intent!=null&&intents.TryEnqueue(intent);}
        public void Dispose(){Volatile.Write(ref stop,1);}
        private NavigationView View(long tick,string failure){var positions=new NavPoint[session.Crowd.Units.Count];for(int i=0;i<positions.Length;i++)positions[i]=session.Crowd.Units[i].Position;return new NavigationView(tick,positions,session.RejectedResults,failure);}
        private void Run()
        {
            long tick=0;var clock=Stopwatch.StartNew();double next=0;
            try{
                while(Volatile.Read(ref stop)==0){
                    if(clock.Elapsed.TotalSeconds<next){Thread.Sleep(1);continue;}
                    NavigationIntent intent;while(intents.TryDequeue(out intent)){if(intent.Stop)session.Stop(intent.Entity,intent.Hold);else session.Move(intent.Entity,intent.Goal);}
                    session.Step(1d/30);Volatile.Write(ref latest,View(++tick,null));next+=1d/30;
                    if(next<clock.Elapsed.TotalSeconds)next=clock.Elapsed.TotalSeconds+1d/30;
                }
            }catch(Exception e){Volatile.Write(ref latest,View(tick,e.ToString()));}
            finally{Volatile.Write(ref stopped,1);}
        }
    }
}
