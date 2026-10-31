using System;
using System.Collections.Generic;
public interface IRouteBenchmarkPacket : IDisposable
{
    void Run(Action checkpoint);
    string[] Facts();
    Dictionary<string,long> Counters();
}
