using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;

namespace Spacewars.Headless
{
    // Shared wire/validation only. The CLI never references or executes the game engine.
    public sealed class WorkerIdentity
    {
        public string CodeRevision {get;set;}
        public string UnityVersion {get;set;}
        public string Platform {get;set;}
        public string Architecture {get;set;}
        public string ExecutableHash {get;set;}
        public string BundleHash {get;set;}
        public string GameplayId {get;set;}
        public int GameplayRevision {get;set;}
        public string GameplayHash {get;set;}
        public string AiId {get;set;}
        public int AiRevision {get;set;}
        public string AiHash {get;set;}
        public string CatalogHash {get;set;}
        public string NavMeshSettings {get;set;}
        public string RouteDeliveryPolicy {get;set;}
    }
    public sealed class OwnerInput
    {
        public string Id {get;set;}
        public int LogicalPlayer {get;set;}
        public int Team {get;set;}
        public string Control {get;set;}
        public string Difficulty {get;set;}
    }
    public sealed class CommandInput
    {
        public int Tick {get;set;}
        public string OwnerId {get;set;}
        public string Kind {get;set;}
        public string Selector {get;set;}
        public int Site {get;set;}
        public int Slot {get;set;}
        public double X {get;set;}
        public double Z {get;set;}
        public string Building {get;set;}
        public string Research {get;set;}
    }
    public sealed class StateHash
    {
        public long Tick {get;set;}
        public string Hash {get;set;}
    }
    public sealed class ReplayExpectation
    {
        public StateHash[] States {get;set;}
        public string DecisionsHash {get;set;}
        public string Termination {get;set;}
        public string Outcome {get;set;}
        public long FinalTick {get;set;}
    }
    public sealed class MatchManifest
    {
        public string Schema {get;set;}
        public WorkerIdentity Worker {get;set;}
        public string WorkerRoot {get;set;}
        public string Executable {get;set;}
        public string Launcher {get;set;}
        public string Mode {get;set;}
        public string Fixture {get;set;}
        public string MapId {get;set;}
        public string MapHash {get;set;}
        public string GeometryHash {get;set;}
        public string RosterHash {get;set;}
        public OwnerInput[] Roster {get;set;}
        public int Seed {get;set;}
        public long Generation {get;set;}
        public int TickRate {get;set;}
        public int TicksLimit {get;set;}
        public int[] Checkpoints {get;set;}
        public int TickBatch {get;set;}
        public int RouteBudget {get;set;}
        public int TraceLimit {get;set;}
        public string EvaluationPolicy {get;set;}
        public string Openings {get;set;}
        public CommandInput[] Commands {get;set;}
        public ReplayExpectation Expected {get;set;}
    }
    public static class Wire
    {
        public static readonly string[] EconomyFixtures={"economy-rich-v1","economy-low-v1","economy-full-slots-v1","economy-lost-hq-v1","economy-blocked-exit-v1"};
        public static string Hash(byte[] data)
        {using(var sha=SHA256.Create())return string.Concat(sha.ComputeHash(data).Select(b=>b.ToString("x2")));}
        public static string Hash(string text)=>Hash(Encoding.UTF8.GetBytes(text));
        public static string FileHash(string path)
        {using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return string.Concat(sha.ComputeHash(stream).Select(b=>b.ToString("x2")));}
        public static string BundleHash(string root)
        {
            // Bind data, managed assembly and native libraries, not just the tiny launcher binary.
            var files=Directory.GetFiles(root,"*",SearchOption.AllDirectories).OrderBy(p=>p.Substring(root.Length).Replace('\\','/'),StringComparer.Ordinal);
            return Hash(string.Join("\n",files.Select(p=>p.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\','/')+":"+FileHash(p))));
        }
        public static void Require(bool valid,string message){if(!valid)throw new ArgumentException(message);}
        public static bool Digest(string s)=>s!=null&&s.Length==64&&s.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');
        public static string RosterDigest(OwnerInput[] roster)=>Hash(string.Join("\n",roster.Select(o=>o.Id+":"+o.LogicalPlayer+":"+o.Team+":"+o.Control+":"+o.Difficulty)));
        public static void Validate(MatchManifest m,bool replay)
        {
            Require(m!=null&&m.Schema=="native-ai-run-v1"&&m.Worker!=null,"Unsupported/missing manifest schema or worker identity.");
            var w=m.Worker;
            Require(w.CodeRevision!=null&&w.CodeRevision.Length==40&&w.CodeRevision.All(c=>"0123456789abcdef".Contains(c)),"Exact code revision required.");
            Require(new[]{w.ExecutableHash,w.BundleHash,w.GameplayHash,w.AiHash,w.CatalogHash,m.MapHash,m.GeometryHash,m.RosterHash}.All(Digest),"SHA256 identity required.");
            Require(new[]{w.UnityVersion,w.Platform,w.Architecture,w.GameplayId,w.AiId,w.NavMeshSettings,w.RouteDeliveryPolicy,m.WorkerRoot,m.Executable,m.Launcher,m.MapId}.All(s=>!string.IsNullOrWhiteSpace(s)),"Incomplete identity/path.");
            Require(w.GameplayRevision>0&&w.AiRevision>0&&m.Generation>0,"Invalid revisions/generation.");
            Require(m.Mode=="duel"&&(m.Fixture=="duel-coverage-v1"||m.Fixture=="obstacle-v1"||EconomyFixtures.Contains(m.Fixture)),"Unsupported mode/fixture; no fallback map.");
            Require(m.TickRate==30&&m.TicksLimit>=1&&m.TicksLimit<=108000&&m.TickBatch>=1&&m.TickBatch<=256&&m.RouteBudget>=1&&m.RouteBudget<=256&&m.TraceLimit>=1&&m.TraceLimit<=4096,"Invalid bounded execution settings.");
            Require(m.EvaluationPolicy=="native-ai-behavior-v1@1"&&m.Openings=="diagnostic-coverage","Unsupported evaluation/opening binding.");
            Require(m.Roster!=null&&m.Roster.Length==2&&m.Roster.All(o=>o!=null&&!string.IsNullOrWhiteSpace(o.Id)&&!o.Id.Contains(":")&&!o.Id.Contains("\n")&&o.Team>=1&&o.Team<=8&&(o.Control=="human"||o.Control=="ai")&&new[]{"recruit","fighter","veteran"}.Contains(o.Difficulty)),"Invalid roster.");
            Require(m.Roster.Select(o=>o.Id).Distinct().Count()==2&&m.Roster.Select(o=>o.LogicalPlayer).SequenceEqual(new[]{1,2})&&m.Roster[0].Team!=m.Roster[1].Team&&m.Roster.Select(o=>o.Difficulty).Distinct().Count()==1,"Unsupported roster binding/difficulty (v1 one match profile).");
            if(EconomyFixtures.Contains(m.Fixture))Require(m.Roster[0].Control=="ai"&&m.Roster[1].Control=="human", "Economy diagnostics require west AI/east human.");
            Require(m.RosterHash==RosterDigest(m.Roster),"Roster hash mismatch.");
            Require(m.Checkpoints!=null&&m.Checkpoints.Length<=2048&&m.Checkpoints.All(t=>t>=0&&t<=m.TicksLimit)&&m.Checkpoints.SequenceEqual(m.Checkpoints.Distinct().OrderBy(t=>t)),"Invalid checkpoint list.");
            Require(m.Commands!=null&&m.Commands.Length<=256&&m.Commands.All(c=>c!=null&&c.Tick>=0&&c.Tick<m.TicksLimit&&m.Roster.Any(o=>o.Id==c.OwnerId&&o.Control=="human")&&new[]{"Move","AttackMove","BuildAt","QueueTank","QueueResearch"}.Contains(c.Kind)&&new[]{"Explorer","Tanks","Factory","ScientificCenter","None"}.Contains(c.Selector)&&c.Site>=0&&c.Site<=3&&c.Slot>=0&&c.Slot<=2&&!double.IsNaN(c.X)&&!double.IsInfinity(c.X)&&!double.IsNaN(c.Z)&&!double.IsInfinity(c.Z)&&Math.Abs(c.X)<=50&&Math.Abs(c.Z)<=50&&new[]{"Headquarters","Factory","ScientificCenter"}.Contains(c.Building)&&c.Research=="TankChassis"),"Invalid/unknown command input.");
            Require(m.Commands.Select(c=>c.Tick).SequenceEqual(m.Commands.Select(c=>c.Tick).OrderBy(t=>t)),"Commands must be ordered by tick.");
            Require(!replay||m.Expected!=null,"Replay requires recorded expected hashes (use run output replay-inputs.json).");
            if(m.Expected!=null)Require(m.Expected.States!=null&&m.Expected.States.Length>0&&m.Expected.States.Length<=2050&&m.Expected.States.All(s=>s!=null&&s.Tick>=0&&s.Tick<=m.TicksLimit&&Digest(s.Hash))&&m.Expected.States.Select(s=>s.Tick).SequenceEqual(m.Expected.States.Select(s=>s.Tick).Distinct().OrderBy(t=>t))&&Digest(m.Expected.DecisionsHash)&&m.Expected.FinalTick>=0&&m.Expected.FinalTick<=m.TicksLimit&&new[]{"win","timeout"}.Contains(m.Expected.Termination)&&new[]{"Playing","PlayerWon","PlayerLost","TeamWon"}.Contains(m.Expected.Outcome),"Invalid replay expectation.");
        }
    }
}
