using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Desksync;
sealed class Updates
{
 readonly string home,cache;readonly HttpClient http=new(){Timeout=TimeSpan.FromMinutes(10)};bool busy;ReleaseManifest? release;string? manifestPath,signaturePath,archive;
 public object State {get;private set;}=new{phase="idle",message="Automatic updates are on",progress=0};
 public bool Automatic {get;private set;}
 public Updates(string home){this.home=home;cache=Path.Combine(home,"updates");Directory.CreateDirectory(cache);Automatic=!File.Exists(Path.Combine(home,"updates-off"));http.DefaultRequestHeaders.UserAgent.ParseAdd("Desksync/"+Assembly.GetExecutingAssembly().GetName().Version!.ToString(3));}
 public void SetAutomatic(bool enabled){Automatic=enabled;var flag=Path.Combine(home,"updates-off");if(enabled){if(File.Exists(flag))File.Delete(flag);}else File.WriteAllText(flag,"");}
 public async Task<bool> Check(Action notify,bool automatic=false)
 {
  if(busy)return false;busy=true;try
  {
   State=new{phase="checking",message="Checking for updates…",progress=0};notify();
   var metadata=await LatestMetadata();var bytes=metadata.Bytes;var signature=metadata.Signature;release=metadata.Release;
   if(Version.Parse(release.Version)<=Assembly.GetExecutingAssembly().GetName().Version){State=new{phase="current",message="You’re using the latest version",progress=100};return false;}
   if(automatic&&File.Exists(Path.Combine(cache,"blocked-version.txt"))&&File.ReadAllText(Path.Combine(cache,"blocked-version.txt")).Trim()==release.Version){State=new{phase="error",message="The previous update was rolled back. Automatic retry is paused for this version; check manually to retry.",progress=0};return false;}
   manifestPath=Path.Combine(cache,"update.json");signaturePath=Path.Combine(cache,"update.json.sig");await File.WriteAllBytesAsync(manifestPath,bytes);await File.WriteAllBytesAsync(signaturePath,signature);archive=Path.Combine(cache,"desksync-"+release.Version+".zip");
   State=new{phase="available",message="Desksync "+release.Version+" is available",progress=0};notify();if(automatic&&!Automatic)return false;
   using var response=await http.GetAsync(release.Url,HttpCompletionOption.ResponseHeadersRead);response.EnsureSuccessStatusCode();await using var input=await response.Content.ReadAsStreamAsync();await using(var output=File.Create(archive)){var buffer=new byte[131072];long total=0;int read;while((read=await input.ReadAsync(buffer))>0){total+=read;if(total>release.Bytes)throw new IOException("The update download exceeded its declared size.");await output.WriteAsync(buffer.AsMemory(0,read));State=new{phase="downloading",message="Downloading Desksync "+release.Version,progress=(int)(total*100/release.Bytes)};notify();}}
   UpdatePolicy.VerifyArchive(archive,release);State=new{phase="ready",message="Ready to restart into Desksync "+release.Version,progress=100};return true;
  }
  catch(Exception e){State=new{phase="error",message=e is HttpRequestException?"Unable to check for updates. Your current version will keep working.":e.Message,progress=0};return false;}
  finally{busy=false;notify();}
 }
 async Task<(byte[] Bytes,byte[] Signature,ReleaseManifest Release)> LatestMetadata()
 {
  (byte[] Bytes,byte[] Signature,ReleaseManifest Release)? best=null;
  try{var bytes=await http.GetByteArrayAsync("https://afrosoftware.com/downloads/desksync/update.json");var sig=Convert.FromBase64String((await http.GetStringAsync("https://afrosoftware.com/downloads/desksync/update.json.sig")).Trim());best=(bytes,sig,UpdatePolicy.Verify(bytes,sig));}catch(Exception e)when(e is HttpRequestException or IOException or FormatException or System.Security.Cryptography.CryptographicException or JsonException){}
  try
  {
   var list=JsonNode.Parse(await http.GetStringAsync("https://api.github.com/repos/PeteZDj/desksync/releases?per_page=3"))!.AsArray();
   foreach(var item in list.Where(r=>r?["draft"]?.GetValue<bool>()!=true))
   {
    var assets=item!["assets"]!.AsArray();var a=assets.FirstOrDefault(a=>a?["name"]?.ToString()=="update.json")?["browser_download_url"]?.ToString();var b=assets.FirstOrDefault(a=>a?["name"]?.ToString()=="update.json.sig")?["browser_download_url"]?.ToString();
    bool Allowed(string? v)=>Uri.TryCreate(v,UriKind.Absolute,out var u)&&u.Scheme=="https"&&u.Host=="github.com"&&u.AbsolutePath.StartsWith("/PeteZDj/desksync/releases/download/",StringComparison.OrdinalIgnoreCase);
    if(!Allowed(a)||!Allowed(b))continue;
    var bytes=await http.GetByteArrayAsync(a);var sig=Convert.FromBase64String((await http.GetStringAsync(b)).Trim());var candidate=UpdatePolicy.Verify(bytes,sig);if(item["tag_name"]?.ToString()!="v"+candidate.Version)continue;
    if(best==null||Version.Parse(candidate.Version)>Version.Parse(best.Value.Release.Version))best=(bytes,sig,candidate);
   }
  }
  catch(Exception e)when(e is HttpRequestException or IOException or FormatException or System.Security.Cryptography.CryptographicException or JsonException){}
  return best??throw new IOException("A verified update channel could not be reached. Your current version will keep working.");
 }
 public void Apply(bool managed)
 {
  if(release==null||archive==null||manifestPath==null||signaturePath==null)throw new IOException("No verified update is ready.");
  var runnerRoot=managed?Path.Combine(Directory.GetParent(AppContext.BaseDirectory.TrimEnd('\\'))!.FullName,"DesksyncUpdateState"):cache;
  var runner=Path.Combine(runnerRoot,"runner-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(runner);var executable=Path.Combine(runner,"Desksync.Updater.exe");File.Copy(Path.Combine(AppContext.BaseDirectory,"Desksync.Updater.exe"),executable);
  var p=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true};foreach(var arg in new[]{"--pid",Environment.ProcessId.ToString(),"--install",AppContext.BaseDirectory,"--home",home,"--manifest",manifestPath,"--signature",signaturePath,"--archive",archive,managed?"--server":"--desktop"})p.ArgumentList.Add(arg);Process.Start(p);
 }
}
