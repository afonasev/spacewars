using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using Spacewars.Runtime;
using Spacewars.Simulation;

internal static class Program
{
    static long comparisons;
    static void Need(bool v,string detail){if(!v)throw new Exception(detail);}
    // Literal old algorithm, plus the independent integer oracle already used
    // by NavigationExactCostPreflight. Neither uses the new field sequence.
    static int Old(NavExactCost a,NavExactCost b){for(int n=NavExactCost.Limbs-1;n>=0;n--){ulong x=a.Limb(n),y=b.Limb(n);if(x!=y)return x<y?-1:1;}return 0;}
    static BigInteger Integer(NavExactCost a){BigInteger n=0;for(int i=33;i>=0;i--)n=(n<<64)+a.Limb(i);return n;}
    static byte[] Bytes(Action<BinaryWriter> action){using var s=new MemoryStream();using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))action(w);return s.ToArray();}
    static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
    static NavExactCost Cost(ulong[] words){var b=Bytes(w=>{foreach(var n in words)w.Write(n);});using var s=new MemoryStream(b);using var r=new BinaryReader(s);return NavExactCost.Read(r);}
    static void Pair(NavExactCost a,NavExactCost b){int expected=Old(a,b);Need(expected==Math.Sign(Integer(a).CompareTo(Integer(b))),"independent integer order");Need(a.CompareTo(b)==expected,"exact old result");Need((a==b)==(expected==0)&&(a!=b)==(expected!=0)&&(a<b)==(expected<0)&&(a>b)==(expected>0)&&(a<=b)==(expected<=0)&&(a>=b)==(expected>=0),"operators");Need(a.Equals(b)==(expected==0),"equality");comparisons++;}
    static int Entry(NavExactQueueEntry a,NavExactQueueEntry b){int c=Old(a.Cost,b.Cost);return c!=0?c:a.Node.CompareTo(b.Node);}
    static byte[] Physical(NavigationRetentionBudget b,NavOwnedStorage root){Need(b.TryOpenScope(NavigationPreparationKind.Capture,1,1,out var s),"capture");using(s){NavigationCaptureBytes value=null;try{var a=s.OwnedReservation();value=NavigationCaptureBytes.Write(a,w=>NavPhysicalBodyWire.WriteTo(w,root,a));s.Attach(value);s.Commit(value,_=>{});return value.TransferPublic();}finally{value?.Dispose();}}}
    static NavCowOrderedSet<NavExactQueueEntry> Decode(NavigationRetentionBudget b,byte[] bytes){Need(b.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var s),"decode");using(s){using var m=new MemoryStream(bytes);using var r=new BinaryReader(m);var v=(NavCowOrderedSet<NavExactQueueEntry>)NavPhysicalBodyWire.ReadFrom(r,s.OwnedReservation());Need(m.Position==m.Length,"EOF");s.Attach(v);s.Commit(v,_=>{});return v;}}
    static NavCowOrderedSet<NavExactQueueEntry> Take(NavigationRetentionBudget b,NavCowOrderedSet<NavExactQueueEntry> source,out NavExactQueueEntry item){Need(b.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var s),"take");using(s){var v=source.ForkPrepared(s.OwnedReservation().TryReserve);
#if CANDIDATE
        Need(v.TryTakeMin(out item),"nonempty take");
#else
        item=v.Min;Need(v.Remove(item),"nonempty remove");
#endif
        v.Seal();s.Attach(v);s.Commit(v,_=>{});source.Release();return v;}}
    static int Main(string[] args)
    {
        var values=new List<NavExactCost>();var random=new Random(515260688);int dominance=0,carry=0;
        for(int word=0;word<34;word++){
            ulong[] cases=word==33?new[]{0UL,1UL,1UL<<17,(1UL<<18)-1}:new[]{0UL,1UL,(1UL<<63)-1,1UL<<63,ulong.MaxValue};
            for(int k=0;k<cases.Length-1;k++){
                var a=new ulong[34];var b=new ulong[34];for(int i=word+1;i<34;i++)a[i]=b[i]=i==33?0x25555UL:0xaaaaaaaaaaaaaaaaUL;
                a[word]=cases[k+1];b[word]=cases[k];for(int i=0;i<word;i++)b[i]=ulong.MaxValue;
                var x=Cost(a);var y=Cost(b);Pair(x,y);Pair(y,x);Need(x.CompareTo(y)==1&&y.CompareTo(x)==-1,"higher word dominates opposite lower suffix");values.Add(x);dominance++;
            }
        }
        foreach(var a in values.Take(34).ToArray()){Pair(a,a);Need(Bytes(a.Write).Length==272,"wire size");using var s=new MemoryStream(Bytes(a.Write));using var r=new BinaryReader(s);Pair(a,NavExactCost.Read(r));}
        var zero=default(NavExactCost);Pair(zero,zero);Pair(zero,NavExactCost.FromDouble(-0.0));
        foreach(int exponent in new[]{0,1,2,511,1023,1024,1535,2045,2046})foreach(ulong fraction in new[]{0UL,1UL,0xfffffffffffffUL}){
            double d=BitConverter.Int64BitsToDouble((long)(((ulong)exponent<<52)|fraction));var v=NavExactCost.FromDouble(d);Pair(v,zero);Pair(v,v);values.Add(v);
        }
        for(int count=1;count<=33;count++){var words=new ulong[34];for(int i=0;i<count;i++)words[i]=ulong.MaxValue;var a=Cost(words);var sum=a.Add(1);Need(Integer(sum)==Integer(a)+(BigInteger.One<<1074),"binary64 one addition oracle");Pair(a,sum);Pair(sum,a);
            // One accumulator unit carries through every selected low word.
            var unit=new ulong[34];unit[0]=1;sum=a.Add(Cost(unit));Need(Integer(sum)==Integer(a)+1,"long carry chain oracle");Pair(a,sum);Pair(sum,a);carry++;values.Add(sum);
        }
        for(int i=0;i<4096;i++){var a=new byte[272];var b=new byte[272];random.NextBytes(a);random.NextBytes(b);a[266]&=3;b[266]&=3;Array.Clear(a,267,5);Array.Clear(b,267,5);using var sa=new MemoryStream(a);using var sb=new MemoryStream(b);using var ra=new BinaryReader(sa);using var rb=new BinaryReader(sb);Pair(NavExactCost.Read(ra),NavExactCost.Read(rb));}
        Need(NavStorageLayout.ValueBytes(typeof(NavExactCost))==272&&NavStorageLayout.ValueBytes(typeof(NavExactQueueEntry))==280,"layout");
        var entries=Enumerable.Range(0,96).Select(i=>new NavExactQueueEntry{Node=96-i,Cost=values[i/3]}).ToArray();var expected=entries.ToList();expected.Sort(Entry);
        foreach(var group in entries.GroupBy(v=>Integer(v.Cost)))foreach(var a in group)foreach(var b in group)Need(a.CompareTo(b)==Entry(a,b),"queue node tie");
        var budget=new NavigationRetentionBudget();var coldBudget=new NavigationRetentionBudget();NavCowOrderedSet<NavExactQueueEntry> hot=null,cold=null;var hashes=new List<string>();
        try{
            Need(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var s),"birth");using(s){hot=NavCowOrderedSet<NavExactQueueEntry>.Create(reserve:s.TryReserve);foreach(var item in entries.OrderBy(_=>random.Next()))Need(hot.Add(item),"unique entry");hot.Seal();s.Attach(hot);s.Commit(hot,_=>{});}
            var before=Physical(budget,hot);File.WriteAllBytes(Path.Combine(args[0],"frontier-before.bin"),before);cold=Decode(coldBudget,before);Need(before.SequenceEqual(Physical(coldBudget,cold)),"cold source bytes");
            for(int i=0;i<16;i++){hot=Take(budget,hot,out var a);cold=Take(coldBudget,cold,out var b);Need(Entry(a,expected[i])==0&&Entry(a,b)==0,"exact extraction order");var bytes=Physical(budget,hot);Need(bytes.SequenceEqual(Physical(coldBudget,cold)),"physical cold-next full bytes");Need(budget.PhysicalBytes==coldBudget.PhysicalBytes,"cold charge");File.WriteAllBytes(Path.Combine(args[0],"frontier-after-"+i+".bin"),bytes);hashes.Add(Hash(bytes));}
        }finally{hot?.Release();cold?.Release();}
        Need(budget.PhysicalBytes==0&&budget.HeldBytes==0&&budget.ActiveScopes==0&&coldBudget.PhysicalBytes==0&&coldBudget.HeldBytes==0&&coldBudget.ActiveScopes==0,"zero ledgers");
        File.WriteAllText(Path.Combine(args[0],"result.json"),JsonSerializer.Serialize(new{status="PASS",comparisons,dominanceWords=34,dominanceCases=dominance,carryChains=carry,seed=515260688,randomPairs=4096,frontierEntries=96,physicalColdNextPairs=16,frontierSHA256=hashes,ledger="hot0/0/0,cold0/0/0",boundary="functional actual C# only; no query/Unity/CPU performance claim"},new JsonSerializerOptions{WriteIndented=true}));return 0;
    }
}
