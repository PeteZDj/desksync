using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
using System.ServiceProcess;
using Desksync;
string? Option(string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
var install=Path.GetFullPath(Option("--install")??throw new IOException("Missing install directory"));var home=Path.GetFullPath(Option("--home")??throw new IOException("Missing data directory"));var server=args.Contains("--server");var stateRoot=server?Path.Combine(Directory.GetParent(install.TrimEnd('\\'))!.FullName,"DesksyncUpdateState"):Path.Combine(home,"updates");var rollback=Path.Combine(stateRoot,"rollback-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));bool changed=false;Process? restarted=null;string? attemptedVersion=null;var addedFiles=new List<string>();
void Result(string message)=>File.WriteAllText(Path.Combine(home,"updates","last-result.txt"),message);
void Service(string verb){using var service=new ServiceController("DesksyncSync");if(verb=="stop"){if(service.Status!=ServiceControllerStatus.Stopped&&service.Status!=ServiceControllerStatus.StopPending)service.Stop();service.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(30));}else{if(service.Status!=ServiceControllerStatus.Running&&service.Status!=ServiceControllerStatus.StartPending)service.Start();service.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(30));}}
void StopEngines(){foreach(var p in Process.GetProcessesByName("syncthing")){try{if(string.Equals(p.MainModule?.FileName,Path.Combine(install,"syncthing.exe"),StringComparison.OrdinalIgnoreCase)&&!p.WaitForExit(4000))p.Kill(true);}catch(System.ComponentModel.Win32Exception){}finally{p.Dispose();}}}
void Copy(string source,string target){foreach(var file in Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories)){var relative=Path.GetRelativePath(source,file);var to=UpdatePolicy.SafeTarget(target,relative);Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Copy(file,to,true);}}
Process Launch(string? nonce=null){var p=new ProcessStartInfo(Path.Combine(install,"Desksync.exe")){UseShellExecute=false,WorkingDirectory=install};p.ArgumentList.Add("--background");if(nonce!=null)p.Environment["DESKSYNC_UPDATE_NONCE"]=nonce;return Process.Start(p)!;}
try
{
 if(!install.TrimEnd('\\').EndsWith(@"\Afrosoftware\Desksync",StringComparison.OrdinalIgnoreCase))throw new IOException("Unexpected update destination.");
 var bytes=File.ReadAllBytes(Option("--manifest")!);var signature=File.ReadAllBytes(Option("--signature")!);var release=UpdatePolicy.Verify(bytes,signature);attemptedVersion=release.Version;
 var current=FileVersionInfo.GetVersionInfo(Path.Combine(install,"Desksync.exe")).FileVersion;if(Version.TryParse(current,out var currentVersion)&&Version.Parse(release.Version)<=currentVersion)throw new IOException("This package is not newer than the installed application.");
 using var archiveStream=new FileStream(Option("--archive")!,FileMode.Open,FileAccess.Read,FileShare.Read);if(archiveStream.Length!=release.Bytes||!Convert.ToHexString(SHA256.HashData(archiveStream)).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("The update archive failed verification.");archiveStream.Position=0;
 try{using var parent=Process.GetProcessById(int.Parse(Option("--pid")!));if(!parent.WaitForExit(30000))throw new IOException("Desksync did not close for its update.");}catch(ArgumentException){ /* Parent already exited. */ }
 if(server)Service("stop");StopEngines();Directory.CreateDirectory(rollback);Copy(install,rollback);
 var staging=Path.Combine(stateRoot,"stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);using(var zip=new ZipArchive(archiveStream,ZipArchiveMode.Read)){if(zip.Entries.Count>5000||zip.Entries.Sum(e=>e.Length)>1_000_000_000)throw new IOException("Unexpected update archive size.");foreach(var entry in zip.Entries){var target=UpdatePolicy.SafeTarget(staging,entry.FullName);if(entry.FullName.EndsWith('/')){Directory.CreateDirectory(target);continue;}Directory.CreateDirectory(Path.GetDirectoryName(target)!);entry.ExtractToFile(target,true);}}
 if(!File.Exists(Path.Combine(staging,"Desksync.exe"))||!File.Exists(Path.Combine(staging,"syncthing.exe")))throw new IOException("The update is missing application files.");foreach(var file in Directory.EnumerateFiles(staging,"*",SearchOption.AllDirectories)){var target=UpdatePolicy.SafeTarget(install,Path.GetRelativePath(staging,file));if(!File.Exists(target))addedFiles.Add(target);}changed=true;Copy(staging,install);if(server)Service("start");var nonce=Guid.NewGuid().ToString("N");restarted=Launch(nonce);var health=Path.Combine(home,"updates","health-"+nonce+".txt");var until=DateTime.UtcNow.AddSeconds(60);while(!File.Exists(health)&&!restarted.HasExited&&DateTime.UtcNow<until)Thread.Sleep(500);if(!File.Exists(health))throw new IOException("The updated application did not become ready.");Result("Updated successfully to "+release.Version+". Previous files retained at "+rollback);
}
catch(Exception e)
{
 try{if(restarted!=null&&!restarted.HasExited){restarted.Kill(true);restarted.WaitForExit();}if(changed){if(server)try{Service("stop");}catch{}StopEngines();foreach(var file in addedFiles)if(File.Exists(file))File.Delete(file);Copy(rollback,install);}if(server)try{Service("start");}catch{}if(attemptedVersion!=null)File.WriteAllText(Path.Combine(home,"updates","blocked-version.txt"),attemptedVersion);Result("Update was not completed: "+e.Message+(changed?" Previous version restored.":" Existing application files were kept."));Launch();}catch{ }
 Environment.ExitCode=1;
}
