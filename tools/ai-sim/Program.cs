using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spacewars.Headless;

var options=new JsonSerializerOptions {UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,WriteIndented=true};
string output=null;
Process worker=null;
bool interrupted=false;
bool ownsOutput=false;
void Interrupt()
{
    interrupted=true;
    try{if(worker is {HasExited:false})worker.Kill(true);}
    catch(InvalidOperationException){/* The owned process may have completed concurrently. */}
}
Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;Interrupt();};
using var termination=OperatingSystem.IsWindows()?null:PosixSignalRegistration.Create(PosixSignal.SIGTERM,context=>{context.Cancel=true;Interrupt();});
try
{
    if(args.Length!=5||!(args[0]=="run"||args[0]=="replay")||args[1]!="--manifest"||args[3]!="--output")throw new ArgumentException("Usage: run|replay --manifest <json> --output <new-directory>");
    output=Path.GetFullPath(args[4]);
    if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new ArgumentException("Output directory must be empty; existing evidence is never overwritten.");
    Directory.CreateDirectory(output);ownsOutput=true;
    string input=Path.GetFullPath(args[2]);
    if(new FileInfo(input).Length>1024*1024)throw new ArgumentException("Manifest exceeds 1 MiB.");
    string json=File.ReadAllText(input);
    File.WriteAllText(Path.Combine(output,"input.json"),json);
    using(var doc=JsonDocument.Parse(json)){CheckDuplicates(doc.RootElement);CheckShape(doc.RootElement,typeof(MatchManifest));}
    var manifest=JsonSerializer.Deserialize<MatchManifest>(json,options);
    Wire.Validate(manifest,args[0]=="replay");
    if(!OperatingSystem.IsMacOS()||RuntimeInformation.ProcessArchitecture!=Architecture.Arm64)throw new ArgumentException("Worker v1 requires the bound macOS arm64 host.");
    string Resolve(string p)=>Path.GetFullPath(p,Path.GetDirectoryName(input));
    manifest.WorkerRoot=Resolve(manifest.WorkerRoot);manifest.Executable=Resolve(manifest.Executable);manifest.Launcher=Resolve(manifest.Launcher);
    if(!Directory.Exists(manifest.WorkerRoot)||!File.Exists(manifest.Executable))throw new ArgumentException("Unity worker unavailable. Build with tools/ai-sim/build-worker.py (explicit QA/build scope); no fallback simulation.");
    if(!manifest.Executable.StartsWith(manifest.WorkerRoot+Path.DirectorySeparatorChar,StringComparison.Ordinal))throw new ArgumentException("Executable must reside in bound worker bundle.");
    if(!File.Exists(manifest.Launcher)||Path.GetFileName(manifest.Launcher)!="unity.sh")throw new ArgumentException("Project tools/unity.sh launcher required.");
    if(Wire.FileHash(manifest.Executable)!=manifest.Worker.ExecutableHash||Wire.BundleHash(manifest.WorkerRoot)!=manifest.Worker.BundleHash)throw new ArgumentException("Worker executable/bundle hash mismatch.");
    Directory.CreateDirectory(output);
    string bound=Path.Combine(output,"manifest.json");File.WriteAllText(bound,JsonSerializer.Serialize(manifest,options));
    var psi=new ProcessStartInfo(manifest.Launcher){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
    psi.Environment["UNITY_RUN_MODE"]="shared";
    foreach(var arg in new[]{"player",manifest.Executable,"-batchmode","-nographics","-logFile",Path.Combine(output,"worker.log"),"--ai-manifest",bound,"--ai-output",output,"--ai-mode",args[0]})psi.ArgumentList.Add(arg);
    worker=Process.Start(psi)??throw new InvalidOperationException("Could not launch Unity worker.");
    var stdout=worker.StandardOutput.ReadToEndAsync();var stderr=worker.StandardError.ReadToEndAsync();
    await worker.WaitForExitAsync();
    await File.WriteAllTextAsync(Path.Combine(output,"host.stdout.log"),await stdout);await File.WriteAllTextAsync(Path.Combine(output,"host.stderr.log"),await stderr);
    if(interrupted)return Failure(4,"interrupted","Interrupted worker; preserved partial evidence.");
    int code=worker.ExitCode;
    if(code!=0)return Failure(code is 2 or 4?code:3,code==2?"invalid":code==4?"interrupted":"crash","Unity worker/host exited "+code);
    foreach(var file in new[]{"summary.json","metrics.json","decisions.jsonl","state-hashes.jsonl","failures.jsonl","replay-inputs.json"})
        if(!File.Exists(Path.Combine(output,file)))return Failure(3,"crash","Worker omitted required output: "+file);
    using(var summary=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"summary.json"))))
        if(summary.RootElement.GetProperty("TechnicalResult").GetString()!="valid")return Failure(3,"crash","Worker did not report technical validity.");
    Console.WriteLine("Valid "+args[0]+"; evidence: "+output);return 0;
}
catch(Exception e) when(e is ArgumentException or JsonException or FileNotFoundException or DirectoryNotFoundException){return Failure(2,"invalid",e.Message);}
catch(Exception e){return Failure(interrupted?4:3,interrupted?"interrupted":"crash",e.Message);}
finally{worker?.Dispose();}
int Failure(int code,string termination,string message)
{
    Console.Error.WriteLine(message);
    // Do not overwrite evidence when the caller supplied a nonempty directory.
    if(ownsOutput)
    {
        if(!File.Exists(Path.Combine(output,"summary.json")))
            File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new {ExitCode=code,TechnicalResult=code==4?"incomplete":"failed",Termination=termination,Outcome=(string)null,Error=message},options));
        File.AppendAllText(Path.Combine(output,"failures.jsonl"),JsonSerializer.Serialize(new {ExitCode=code,Termination=termination,Error=message})+"\n");
        File.WriteAllText(Path.Combine(output,"orchestrator-result.json"),JsonSerializer.Serialize(new {ExitCode=code,TechnicalResult="failed",Termination=termination,Error=message},options));
    }
    return code;
}
void CheckDuplicates(JsonElement value)
{
    if(value.ValueKind==JsonValueKind.Object){var keys=new HashSet<string>(StringComparer.Ordinal);foreach(var p in value.EnumerateObject()){if(!keys.Add(p.Name))throw new ArgumentException("Duplicate JSON input: "+p.Name);CheckDuplicates(p.Value);}}
    else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())CheckDuplicates(item);
}

void CheckShape(JsonElement value,Type type)
{
    if(value.ValueKind==JsonValueKind.Null){if(type==typeof(ReplayExpectation))return;throw new ArgumentException("Null input: "+type.Name);}
    if(type.IsArray){if(value.ValueKind!=JsonValueKind.Array)throw new ArgumentException("Expected array: "+type.Name);foreach(var child in value.EnumerateArray())CheckShape(child,type.GetElementType());return;}
    if(type==typeof(string)||type==typeof(int)||type==typeof(long)||type==typeof(double))return;
    if(value.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Expected object: "+type.Name);
    foreach(var property in type.GetProperties())
    {
        if(!value.TryGetProperty(property.Name,out var child))throw new ArgumentException("Missing input: "+property.Name);
        CheckShape(child,property.PropertyType);
    }
}
