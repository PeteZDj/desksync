using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Desksync;

if(!OperatingSystem.IsWindows())Native.umask(0x3f); // 0077: private config, keys, tokens and logs.
string? Option(string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
var data=Path.GetFullPath(Option("--data")??Path.Combine(Environment.GetEnvironmentVariable("XDG_STATE_HOME")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".local","state"),"desksync"));
var port=int.Parse(Option("--port")??"5392");var enginePort=int.Parse(Option("--engine-port")??"5391");
if(port<1024||port>65535||port==enginePort)throw new ArgumentException("Choose distinct local ports between 1024 and 65535.");
Directory.CreateDirectory(data);Engine.Restrict(data);
if(!OperatingSystem.IsWindows())for(var p=new DirectoryInfo(data);p!=null;p=p.Parent)if(p.LinkTarget!=null)throw new IOException("Private state cannot use a symbolic link.");
// A file lock prevents two dashboard processes from mutating the same identity.
using var singleton=new FileStream(Path.Combine(data,"dashboard.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
using var engine=new Engine(data,Option("--folder"),enginePort);
using var account=new Account(data);
await engine.Initialize(engine.Settings.SetupCompleted);
if(args.Contains("--initialize")){await engine.EnsureFolder();engine.CompleteSetup("desktop","selfhost");return;}
var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var origin=$"http://127.0.0.1:{port}";
var builder=WebApplication.CreateSlimBuilder(Array.Empty<string>());
builder.Logging.ClearProviders();builder.WebHost.UseUrls(origin);builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=16000);
var app=builder.Build();using var gate=new SemaphoreSlim(1,1);
CancellationTokenSource? signingIn=null;object? signInState=null;string? error=null,notice=null;
async Task<object> State(){var snapshot=JsonSerializer.SerializeToNode(await engine.Snapshot())!;PeerHealth.Merge(snapshot,account.Session?.Server["peerHealth"]);return new{type="state",sync=snapshot,account=account.Public,managed=false,error,notice,signIn=signInState,updates=new{phase="manual",message="Linux preview updates are installed manually. See the Linux setup guide."},automaticUpdates=false,platform="linux",version="0.3.1"};}
app.Use(async(context,next)=>{
 context.Response.Headers.CacheControl="no-store";context.Response.Headers["X-Content-Type-Options"]="nosniff";context.Response.Headers["Referrer-Policy"]="no-referrer";context.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https://*.googleusercontent.com; connect-src 'self'; frame-src 'none'; frame-ancestors 'none'; object-src 'none'; base-uri 'none'";
 if(context.Request.Host.Value!=$"127.0.0.1:{port}"){context.Response.StatusCode=403;return;}
 if(context.Request.Path.StartsWithSegments("/api")&&(context.Request.Headers["X-Desksync-Token"]!=token||(context.Request.Headers.Origin.Count>0&&context.Request.Headers.Origin!=origin))){context.Response.StatusCode=403;return;}
 await next();
});
app.MapGet("/{file?}",(string? file)=>{
 file=string.IsNullOrEmpty(file)?"index.html":file;
 if(!new[]{"index.html","app.js","styles.css","bridge.js"}.Contains(file))return Results.NotFound();
 using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Desksync.UI."+file)!;
 using var reader=new StreamReader(stream);var text=reader.ReadToEnd();
 if(file=="index.html")text=text.Replace("connect-src 'none'","connect-src 'self'").Replace("<title>",$"<meta name=\"desksync-token\" content=\"{token}\"><title>").Replace("<script src=\"app.js\"", "<script src=\"bridge.js\"></script><script src=\"app.js\"");
 return Results.Text(text,file.EndsWith(".js")?"text/javascript":file.EndsWith(".css")?"text/css":"text/html");
});
app.MapGet("/api/state",async()=>{await gate.WaitAsync();try{return Results.Json(await State());}catch(Exception){return Results.Json(new{type="failure",error="The sync engine is unavailable. Check the local engine log."});}finally{gate.Release();}});
app.MapPost("/api/command",async(HttpRequest request)=>{
 await gate.WaitAsync();try{
 var m=await JsonNode.ParseAsync(request.Body)??throw new ArgumentException("Missing command.");error=null;notice=null;
 switch(m["action"]?.ToString()){
 case "rescan":await engine.Rescan();notice="Scan requested.";break;
 case "pause":await engine.Pause(m["paused"]?.GetValue<bool>()==true);break;
 case "configure":
  if(m["role"]?.ToString()=="server")throw new IOException("This Linux preview connects desktops. Use the Windows server setup for hosting.");
  await engine.SetFolder(m["folder"]?.ToString()??engine.Settings.Folder);engine.CompleteSetup("desktop",m["provider"]?.ToString()=="selfhost"?"selfhost":"afrosoftware");notice="Folder ready. Connect through your account or a server connection file.";break;
 case "advanced-pair":await engine.EnsureFolder();await engine.Pair(Pairing.Parse(m["code"]?.ToString()??""),true);engine.CompleteSetup("desktop","selfhost");notice="Server added. Save this computer's connection file and approve it on your server.";break;
 case "save-connection":return Results.Json(new{type="connection",code=engine.Code.Encode()});
 case "open-folder":Open(engine.Settings.Folder);notice="Folder: "+engine.Settings.Folder;break;
 case "versions":var versions=Path.Combine(engine.Settings.Folder,".stversions");if(!Directory.Exists(versions))throw new IOException("No retained versions yet.");Open(versions);break;
 case "remove-device":await engine.Forget(m["id"]?.ToString()??"");break;
 case "disconnect":if(signingIn!=null)throw new IOException("Cancel sign-in first.");await engine.Pause(true);await account.Disconnect();notice="Disconnected. Local files were kept.";break;
 case "cancel-sign-in":signingIn?.Cancel();break;
 case "sign-in":
  if(signingIn!=null)break;
  if(!engine.Settings.SetupCompleted)await engine.SetFolder(m["folder"]?.ToString()??engine.Settings.Folder);
  signingIn=new CancellationTokenSource(TimeSpan.FromMinutes(10));
  try{var grant=await account.Begin(engine.DeviceId,engine.Settings.DeviceName,signingIn.Token);var url=grant["verification_uri_complete"]!.ToString();
  if(!url.StartsWith("https://afrosoftware.com/desksync/authorize?",StringComparison.Ordinal))throw new IOException("Unrecognized sign-in address.");
  signInState=new{code=grant["user_code"]!.ToString()};_ = FinishSignIn(grant,signingIn);return Results.Json(new{type="open-url",url});
  }catch{signingIn.Dispose();signingIn=null;throw;}
 case "help":return Results.Json(new{type="open-url",url="https://github.com/PeteZDj/desksync/blob/main/docs/linux.md"});
 case "startup":notice="Run install.sh from your Linux download to enable user startup. The Linux guide includes the fallback for computers without systemd.";break;
 case "check-updates":case "install-update":case "auto-updates":notice="Linux preview updates are manual. Use the versioned Linux download and checksum from the setup guide.";break;
 case "clear-notice":error=null;notice=null;break;
 default:throw new ArgumentException("Unsupported command.");
 }
 return Results.Json(await State());
 }catch(Exception e){error=e is IOException or ArgumentException?e.Message:"The command failed. Check your folder permissions and local engine log.";return Results.Json(await State());}finally{gate.Release();}
});
async Task FinishSignIn(JsonNode grant,CancellationTokenSource operation){
 try{
  var interval=Math.Max(5,grant["interval"]?.GetValue<int>()??5);
  while(true){await Task.Delay(TimeSpan.FromSeconds(interval),operation.Token);await gate.WaitAsync(operation.Token);try{
   if(!await account.Poll(grant["device_code"]!.ToString(),operation.Token))continue;
   await engine.EnsureFolder();await engine.Pair(Pairing.Parse(account.Session!.Server["connectionCode"]!.ToString()),true);engine.CompleteSetup("desktop");await engine.Pause(false);notice="Connected to your private Afrosoftware server.";break;
  }finally{gate.Release();}}
 }catch(OperationCanceledException){notice="Sign-in cancelled or expired. Your files were kept.";}catch(Exception){error="Sign-in could not finish. Try again or use a server connection file.";}finally{await gate.WaitAsync();try{operation.Dispose();signingIn=null;signInState=null;}finally{gate.Release();}}
}
app.Lifetime.ApplicationStopping.Register(()=>signingIn?.Cancel());
Console.WriteLine("Desksync Linux preview: "+origin+" — close the dashboard tab without stopping file sync.");
if(!args.Contains("--no-browser"))Open(origin);
await app.RunAsync();
static void Open(string target){try{var p=new ProcessStartInfo(OperatingSystem.IsWindows()?"explorer.exe":"xdg-open"){UseShellExecute=false,CreateNoWindow=true};p.ArgumentList.Add(target);Process.Start(p);}catch{} }
static class Native{[DllImport("libc",EntryPoint="umask")]internal static extern uint umask(uint mask);}
