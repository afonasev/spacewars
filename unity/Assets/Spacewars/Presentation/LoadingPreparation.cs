using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace Spacewars.Presentation
{
    // Keep synchronous tooling callers and the real loading path on the same source.
    internal static class LoadingPreparation
    {
        public static void Drain(IEnumerator work)
        {
            using(var steps=Flatten(work))while(steps.MoveNext()){}
        }
        public static IEnumerator Run(IEnumerator work,Action<Exception> failed)
        {
            using(var steps=Flatten(work))
            {
                while(true)
                {
                    bool next=false;Exception error=null;
                    try{next=steps.MoveNext();}catch(Exception ex){error=ex;}
                    if(error!=null){failed(error);yield break;}
                    if(!next)yield break;
                    yield return steps.Current;
                }
            }
        }
        private static IEnumerator<object> Flatten(IEnumerator work)
        {
            var stack=new Stack<IEnumerator>();stack.Push(work);
            try
            {
                while(stack.Count>0)
                {
                    var current=stack.Peek();
                    if(!current.MoveNext()){(stack.Pop() as IDisposable)?.Dispose();continue;}
                    if(current.Current is IEnumerator nested){stack.Push(nested);continue;}
                    yield return current.Current;
                }
            }
            finally{while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();}
        }
    }
    internal sealed class LoadingFrameBudget
    {
        // Scheduling budget only; no sampling, geometry or gameplay quality reduction.
        private readonly Stopwatch clock=Stopwatch.StartNew();
        public bool Expired=>clock.Elapsed.TotalMilliseconds>=12;
        public void Restart()=>clock.Restart();
    }
}
