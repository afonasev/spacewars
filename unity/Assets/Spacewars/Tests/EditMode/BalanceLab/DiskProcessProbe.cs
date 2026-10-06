using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;
using Spacewars.BalanceLab;
namespace Spacewars.Tests.EditMode.BalanceLab
{
    // Compiled separately by run-process-fixtures.py against the exact production sources.
    internal static class DiskProcessProbe
    {
        public static int Main(string[] args)
        {
            string path = args[0], command = args[1];
            var store = new AtomicStore(path, () => new JObject { ["counter"] = 0 }, phase =>
            {
                if (args.Length > 3 && phase == args[3])
                {
                    File.WriteAllText(path + ".signal", phase);
                    if (args[2] == "crash") { Thread.Sleep(60000); }
                    if (args[2] == "fail") throw new IOException("injected");
                }
            });
            try
            {
                if (command == "recover") { store.RecoverTemps(); Console.WriteLine(store.Read().Token.Hash); return 0; }
                var read = store.Read(); File.WriteAllText(path + ".ready-" + args[2], read.Token.Hash);
                if (command == "race") while (!File.Exists(path + ".go")) Thread.Sleep(10);
                var payload = read.Payload; payload["counter"] = (double)payload["counter"] + 1;
                var result = store.Commit(read.Token, payload); Console.WriteLine("committed:" + result.Token.Generation); return 0;
            }
            catch (StoreConflictException) { Console.WriteLine("conflict"); return 10; }
            catch (StoreCommitException e) { Console.WriteLine("commit-error:" + e.ReplacementOccurred); return 11; }
            catch (IOException) { Console.WriteLine("lock-busy"); return 12; }
            catch (FormatException) { Console.WriteLine("corrupt"); return 13; }
        }
    }
}
