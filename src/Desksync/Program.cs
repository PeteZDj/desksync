using System.Diagnostics;
using System.Reflection;
using System.Security.AccessControl;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
namespace Desksync;
internal static class Program
{
 [STAThread] static void Main(string[] args)
 {
  ApplicationConfiguration.Initialize();string? Option(string key){var i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
  if(args.Contains("--install-server")){try{ServerSetup.Install(Option("--folder")??@"C:\Desksync");}catch(Exception e){MessageBox.Show(e.Message,"Server setup failed");Environment.ExitCode=1;}return;}
  using var service=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DesksyncSync");var managed=service!=null&&(Option("--data")==null||string.Equals(Option("--data"),@"C:\ProgramData\Desksync",StringComparison.OrdinalIgnoreCase));
  var home=Option("--data")??(managed?@"C:\ProgramData\Desksync":Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Afrosoftware","Desksync"));
  using var singleton=new Mutex(true,"Local\\Desksync-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(home.ToLowerInvariant())))[..16],out var first);if(!first){var existing=FindWindow(null,"Desksync");if(existing!=IntPtr.Zero){ShowWindow(existing,9);SetForegroundWindow(existing);}return;}
  try{using var engine=new Engine(home,Option("--folder"),int.Parse(Option("--engine-port")??"5391"));if(args.Contains("--initialize")){engine.Initialize().GetAwaiter().GetResult();engine.CompleteSetup(managed||args.Contains("--server")?"server":"desktop","selfhost");return;}Application.Run(new DesktopWindow(engine,managed,args.Contains("--background"),Option("--data")!=null&&!managed?Path.Combine(home,"desktop-ui"):null));}
  catch(Exception e){MessageBox.Show(e is UnauthorizedAccessException?"This is a managed server. Open Desksync as administrator to manage its private settings. Synchronization continues in the Windows service.":e.Message,"Desksync needs attention");Environment.ExitCode=1;}
 }
 [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]static extern IntPtr FindWindow(string? className,string title);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
}
sealed class DesktopWindow:Form
{
 const string Origin="https://app.desksync.local";
 const string IconOrigin="https://icons.desksync.local";
 readonly WindowsFileIcons fileIcons=new();
    readonly Engine engine;readonly bool managed,background;readonly Account account;readonly Updates updates;readonly string userHome;
 readonly WebView2 view=new(){Dock=DockStyle.Fill,DefaultBackgroundColor=Color.FromArgb(247,249,252)};
 readonly NotifyIcon tray;readonly System.Windows.Forms.Timer iconTimer=new(){Interval=500};Icon? statusIcon;JsonNode? lastSnapshot;ToolStripItem? trayHeader;readonly Dictionary<string,ToolStripItem> trayTransfers=[];string iconPhase="idle",lastIssue="";int iconFrame;DateTimeOffset healthChecked;bool reportingHealth;readonly System.Windows.Forms.Timer timer=new(){Interval=2500};
 bool ready,refreshing,quitting,updateCheckRunning,updateReady,autoInstall,uiLoaded,snapshotHealthy;DateTimeOffset updateChecked;string? error,notice;CancellationTokenSource? signIn;object? signInState;DateTimeOffset accountChecked;
 public DesktopWindow(Engine engine,bool managed,bool background,string? stateHome=null)
 {
  this.engine=engine;this.managed=managed;this.background=background;userHome=stateHome??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Afrosoftware","DesksyncDesktop");Directory.CreateDirectory(userHome);Engine.Restrict(userHome);account=new Account(userHome);updates=new Updates(userHome);Text="Desksync";Size=new Size(1080,770);MinimumSize=new Size(900,650);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(247,249,252);Icon=Brand.Icon();Controls.Add(view);AddMenus();
  var menu=BuildTrayMenu();
  tray=new NotifyIcon{Icon=Icon,Text="Desksync · starting",Visible=true,ContextMenuStrip=menu};tray.MouseClick+=(_,e)=>{if(e.Button==MouseButtons.Left)ShowPage(lastSnapshot?["presentation"]?.ToString()=="syncing"?"activity":lastSnapshot?["errors"]?.GetValue<int>()>0?"notifications":"home");};tray.DoubleClick+=(_,_)=>ShowPage("activity");iconTimer.Tick+=(_,_)=>{if(iconPhase=="syncing")SetTrayIcon();};iconTimer.Start();FormClosing+=(_,e)=>{if(!quitting&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};FormClosed+=(_,_)=>{timer.Dispose();iconTimer.Dispose();tray.Dispose();statusIcon?.Dispose();signIn?.Cancel();account.Dispose();view.Dispose();};Shown+=async(_,_)=>{if(background)Hide();await Start();};timer.Tick+=async(_,_)=>{await RefreshState();if(autoInstall&&updateReady)await ApplyUpdateIfIdle();if(DateTimeOffset.UtcNow-updateChecked>TimeSpan.FromHours(4)){updateChecked=DateTimeOffset.UtcNow;_=CheckUpdates(true);}};
 }
 async Task Start()
 {
  try
  {
   var runtimeHome=managed?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Afrosoftware","DesksyncUpdateState","ui-"+System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value):userHome;
   Directory.CreateDirectory(runtimeHome);if(managed){var acl=new System.Security.AccessControl.DirectorySecurity();acl.SetAccessRuleProtection(true,false);foreach(var sid in new[]{new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid,null),new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid,null)})acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(sid,System.Security.AccessControl.FileSystemRights.FullControl,System.Security.AccessControl.InheritanceFlags.ContainerInherit|System.Security.AccessControl.InheritanceFlags.ObjectInherit,System.Security.AccessControl.PropagationFlags.None,System.Security.AccessControl.AccessControlType.Allow));new DirectoryInfo(runtimeHome).SetAccessControl(acl);}
   var assembly=Assembly.GetExecutingAssembly();var ui=Path.Combine(runtimeHome,"ui-"+Assembly.GetExecutingAssembly().GetName().Version!.ToString(3));Directory.CreateDirectory(ui);foreach(var name in new[]{"index.html","app.js","styles.css"}){using var source=assembly.GetManifestResourceStream("Desksync.UI."+name)!;using var target=File.Create(Path.Combine(ui,name));await source.CopyToAsync(target);}
   var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(runtimeHome,"WebView2"),new CoreWebView2EnvironmentOptions("--disable-extensions"));await view.EnsureCoreWebView2Async(environment);var core=view.CoreWebView2;core.SetVirtualHostNameToFolderMapping("app.desksync.local",ui,CoreWebView2HostResourceAccessKind.DenyCors);
   core.AddWebResourceRequestedFilter(IconOrigin+"/file-icon?*",CoreWebView2WebResourceContext.Image);
   core.WebResourceRequested+=(_,e)=>{
    if(!e.Request.Uri.StartsWith(IconOrigin+"/file-icon?path=",StringComparison.Ordinal))return;
    try{var name=Uri.UnescapeDataString(new Uri(e.Request.Uri).Query[6..]);var bytes=fileIcons.Read(engine.Settings.Folder,name);e.Response=core.Environment.CreateWebResourceResponse(bytes==null?null:new MemoryStream(bytes),bytes==null?404:200,bytes==null?"Not Found":"OK","Content-Type: image/png\r\nCache-Control: private, max-age=60\r\nX-Content-Type-Options: nosniff");}
    catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){e.Response=core.Environment.CreateWebResourceResponse(null,404,"Not Found","");}
   };
   core.Settings.AreHostObjectsAllowed=false;core.Settings.AreDevToolsEnabled=false;core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreDefaultScriptDialogsEnabled=false;core.Settings.IsStatusBarEnabled=false;core.NavigationStarting+=(_,e)=>{if(!e.Uri.StartsWith(Origin+"/",StringComparison.Ordinal))e.Cancel=true;};core.FrameNavigationStarting+=(_,e)=>e.Cancel=true;core.NewWindowRequested+=(_,e)=>e.Handled=true;core.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;core.WebMessageReceived+=async(_,e)=>{if(e.Source.StartsWith(Origin+"/",StringComparison.Ordinal))await Command(e.WebMessageAsJson);};core.Navigate(Origin+"/index.html");
   await engine.Initialize(engine.Settings.SetupCompleted||managed);if(managed&&(!engine.Settings.SetupCompleted||engine.Settings.Role!="server"))engine.CompleteSetup("server",engine.Settings.Provider);if(!engine.Settings.SetupCompleted&&(await engine.Get("config/folders")).AsArray().Any(f=>f!["id"]!.GetValue<string>()==engine.Settings.FolderId&&f["devices"]!.AsArray().Count>1))engine.CompleteSetup("desktop");ready=true;await RefreshState();timer.Start();MarkUpdateHealthy();updateChecked=DateTimeOffset.UtcNow;_=CheckUpdates(true);
  }
  catch(WebView2RuntimeNotFoundException){MessageBox.Show("Microsoft Edge WebView2 is needed for the new Desksync interface. Run Desksync Setup to install this Microsoft component, then reopen Desksync.","One setup step needed");}
  catch(Exception e){File.WriteAllText(Path.Combine(userHome,"startup-error.log"),e.ToString());error=e.Message;Push(new{type="failure",error});MessageBox.Show(e.Message,"Desksync needs attention");}
 }
 void Push(object value){if(view.CoreWebView2!=null&&view.Source?.GetLeftPart(UriPartial.Authority)==Origin)view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value));}
 static void OpenFolder(string path){Directory.CreateDirectory(path);var p=new ProcessStartInfo("explorer.exe"){UseShellExecute=true};p.ArgumentList.Add(path);Process.Start(p);}
 void ShowPage(string page){Show();WindowState=FormWindowState.Normal;Activate();Push(new{type="navigate",page});}
 void SetTrayIcon(){var next=Brand.Icon(iconPhase,iconFrame++);tray.Icon=next;statusIcon?.Dispose();statusIcon=next;}
 string TraySummary()
 {
  if(lastSnapshot==null)return "Starting Desksync";
  if(lastSnapshot["paused"]?.GetValue<bool>()==true)return "Sync paused";
  if(lastSnapshot["errors"]?.GetValue<int>()>0)return "Needs attention";
  var active=lastSnapshot["transfers"]!.AsArray().Where(t=>t?["State"]?.ToString()=="active").ToArray();
  if(active.Length>0){var up=active.Count(t=>t!["Direction"]!.ToString()=="upload");return (up>0?"Uploading "+up+" file(s)":"Downloading "+active.Length+" file(s)")+" · "+Rate(lastSnapshot[up>0?"uploadRate":"downloadRate"]?.GetValue<double>()??0);}
  if(lastSnapshot["uploadNeededItems"]?.GetValue<long>()>0)return "Waiting to upload "+lastSnapshot["uploadNeededItems"]+" item(s)";
  if(lastSnapshot["needed"]?.GetValue<long>()>0)return "Waiting to download "+lastSnapshot["needed"]+" item(s)";
  return lastSnapshot["presentation"]?.ToString() switch{"offline"=>"Waiting for your other device","scanning"=>"Checking files",_=>"Up to date"};
 }
 static string Rate(double bytes)=>bytes>=1e6?(bytes/1e6).ToString("0.0")+" MB/s":bytes>=1e3?(bytes/1e3).ToString("0")+" KB/s":bytes.ToString("0")+" B/s";
 static string TransferLabel(JsonNode transfer){var name=Path.GetFileName(transfer["Name"]?.ToString()??"File");if(name.Length>36)name=name[..33]+"…";return(transfer["Direction"]?.ToString()=="upload"?"↑ ":"↓ ")+name+(transfer["State"]?.ToString()=="active"?" · "+transfer["Percent"]+"%":" · queued");}
 ContextMenuStrip BuildTrayMenu()
 {
  var menu=new ContextMenuStrip();
  menu.Opening+=(_,_)=>{
   foreach(var item in menu.Items.Cast<ToolStripItem>().ToArray())item.Dispose();menu.Items.Clear();trayTransfers.Clear();trayHeader=menu.Items.Add("Desksync · "+TraySummary());trayHeader.Enabled=false;
   foreach(var transfer in lastSnapshot?["transfers"]?.AsArray().Take(3)??[]){if(transfer==null)continue;var item=menu.Items.Add(TransferLabel(transfer),null,(_,_)=>ShowPage("activity"));trayTransfers[transfer["Id"]!.ToString()]=item;}
   if(lastSnapshot?["issues"]?.AsArray().FirstOrDefault() is JsonNode issue){var text=issue["Message"]?.ToString()??"Needs attention";menu.Items.Add(text.Length>75?text[..72]+"…":text,null,(_,_)=>ShowPage("notifications"));}
   menu.Items.Add(new ToolStripSeparator());menu.Items.Add("Open Desksync",null,(_,_)=>ShowPage("home"));menu.Items.Add("Open Desksync folder",null,(_,_)=>OpenFolder(engine.Settings.Folder));
   var paused=lastSnapshot?["paused"]?.GetValue<bool>()==true;menu.Items.Add(paused?"Resume sync":"Pause sync",null,(_,_)=>_=Run(async()=>await engine.Pause(!paused)));
   menu.Items.Add("Uploads & downloads",null,(_,_)=>ShowPage("activity"));menu.Items.Add("Devices",null,(_,_)=>ShowPage("devices"));menu.Items.Add("Scan now",null,(_,_)=>_=Run(engine.Rescan));menu.Items.Add("Settings",null,(_,_)=>ShowPage("settings"));menu.Items.Add("Check for updates",null,(_,_)=>_=CheckUpdates(false));menu.Items.Add(new ToolStripSeparator());
   menu.Items.Add(managed?"Close dashboard":"Quit Desksync",null,(_,_)=>_=Run(async()=>{if(ready&&!managed)await engine.Shutdown();quitting=true;Close();}));
  };return menu;
 }
 async Task Run(Func<Task> action){try{await action();}catch(Exception e){error=e.Message;}await RefreshState();}
 async Task ReportStatus(JsonNode snapshot)
 {
  if(reportingHealth)return;reportingHealth=true;try{await account.ReportHealth(snapshot);}catch(Exception e)when(e is IOException or HttpRequestException or TaskCanceledException){/* Hosted diagnostics are optional; encrypted peer transfers keep working. */}finally{reportingHealth=false;}
 }
 async Task RefreshState()
 {
  if(!ready||refreshing||IsDisposed)return;refreshing=true;
  try{
   if(account.Session!=null&&DateTimeOffset.UtcNow-accountChecked>TimeSpan.FromMinutes(5)){accountChecked=DateTimeOffset.UtcNow;try{await account.Refresh();}catch{notice="Account information is cached. Sync uses your approved device connection.";}}
   var snapshot=JsonSerializer.SerializeToNode(await engine.Snapshot())!;
   if(account.Session!=null&&DateTimeOffset.UtcNow-healthChecked>TimeSpan.FromSeconds(15)){healthChecked=DateTimeOffset.UtcNow;_=ReportStatus(snapshot.DeepClone());}
   PeerHealth.Merge(snapshot,account.Session?.Server["peerHealth"]);lastSnapshot=snapshot;snapshotHealthy=true;
   Push(new{type="state",sync=snapshot,account=account.Public,managed,error,notice,signIn=signInState,updates=updates.State,automaticUpdates=updates.Automatic,platform="windows",version=Assembly.GetExecutingAssembly().GetName().Version!.ToString(3)});
   var active=snapshot["transfers"]!.AsArray().Any(t=>t?["State"]?.ToString()=="active");var nextPhase=snapshot["paused"]!.GetValue<bool>()?"paused":snapshot["errors"]!.GetValue<int>()>0?"error":active?"syncing":snapshot["presentation"]?.ToString()=="syncing"?"pending":snapshot["presentation"]?.ToString()=="offline"?"offline":"idle";
   if(nextPhase!=iconPhase){iconPhase=nextPhase;SetTrayIcon();}
   var tooltip="Desksync · "+TraySummary();tray.Text=tooltip[..Math.Min(63,tooltip.Length)];
   if(tray.ContextMenuStrip?.Visible==true){if(trayHeader!=null)trayHeader.Text="Desksync · "+TraySummary();foreach(var(id,item)in trayTransfers){var transfer=snapshot["transfers"]!.AsArray().FirstOrDefault(t=>t?["Id"]?.ToString()==id);if(transfer!=null)item.Text=TransferLabel(transfer);else if(!(item.Text??"").EndsWith(" · checking status"))item.Text+=" · checking status";}}
   var issue=snapshot["issues"]!.AsArray().FirstOrDefault();var key=issue?["Code"]+"|"+issue?["DeviceId"];
   if(issue!=null&&key!=lastIssue){var message=issue["Message"]?.ToString();if(string.IsNullOrWhiteSpace(message))message="Open Desksync for details.";tray.ShowBalloonTip(4000,"Desksync needs attention",message[..Math.Min(240,message.Length)],ToolTipIcon.Warning);}lastIssue=issue==null?"":key;
   MarkUpdateHealthy();
  }catch(Exception e){error=e.Message;Push(new{type="failure",error});}finally{refreshing=false;}
 }
 async Task Command(string json)
 {
  if(json.Length>16000)return;try{var m=JsonNode.Parse(json)!;var action=m["action"]?.ToString();if(action=="ui-ready"){uiLoaded=true;MarkUpdateHealthy();return;}if(action=="ready"){await RefreshState();return;}if(!ready)return;error=null;notice=null;
   switch(action)
   {
    case "rescan":await engine.Rescan();notice="Scan requested. New files and queued changes will be checked.";break;
    case "open-folder":OpenFolder(engine.Settings.Folder);break;
    case "open-item":var localPath=LocalFiles.Resolve(engine.Settings.Folder,m["path"]?.ToString()??"");Process.Start(new ProcessStartInfo(localPath){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(localPath)});break;
    case "reveal-item":var revealPath=LocalFiles.Resolve(engine.Settings.Folder,m["path"]?.ToString()??"");Process.Start(new ProcessStartInfo("explorer.exe"){UseShellExecute=true,Arguments="/select,\""+revealPath+"\""});break;
    case "pause":await engine.Pause(m["paused"]?.GetValue<bool>()==true);break;
    case "browse":using(var picker=new FolderBrowserDialog{Description="Choose your Desksync folder",UseDescriptionForTitle=true,SelectedPath=engine.Settings.Folder})if(picker.ShowDialog(this)==DialogResult.OK)Push(new{type="folder-picked",path=picker.SelectedPath});break;
    case "configure":
     var role=m["role"]?.ToString()??"desktop";var path=m["folder"]?.ToString()??@"C:\Desksync";
     if(role=="server"&&!managed&&m["unattended"]?.GetValue<bool>()==true){Engine.ValidateFolder(path,@"C:\ProgramData\Desksync");await engine.Shutdown();timer.Stop();var p=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas"};p.ArgumentList.Add("--install-server");p.ArgumentList.Add("--folder");p.ArgumentList.Add(path);using var helper=Process.Start(p)!;await helper.WaitForExitAsync();if(helper.ExitCode!=0)throw new IOException("Windows server setup did not finish. Reopen Desksync to restart the local engine. Your files were kept.");quitting=true;Close();return;}
     if(engine.Settings.SetupCompleted&&!string.Equals(path,engine.Settings.Folder,StringComparison.OrdinalIgnoreCase)&&MessageBox.Show(this,"Copy your sync folder to the new empty location? Original files remain in the old folder.","Change folder location",MessageBoxButtons.OKCancel)!=DialogResult.OK)break;
     await engine.SetFolder(path);engine.CompleteSetup(managed?"server":role,m["provider"]?.ToString()=="selfhost"?"selfhost":"afrosoftware");notice="Your folder is ready. Changes and deletions synchronize both ways.";break;
    case "sign-in":if(!engine.Settings.SetupCompleted)await engine.SetFolder(m["folder"]?.ToString()??engine.Settings.Folder);_ = SignIn();break;
    case "cancel-sign-in":signIn?.Cancel();signInState=null;break;
    case "disconnect":if(MessageBox.Show(this,managed?"Sign out of your account? The server service will keep running.":"Disconnect this account, remove this computer's server access, and pause synchronization? Local files remain.","Disconnect account",MessageBoxButtons.YesNo)==DialogResult.Yes){if(!managed)await engine.Pause(true);await account.Disconnect();notice="Account disconnected. Local files were kept.";}break;
    case "remove-device":var id=m["id"]?.ToString()??"";if(System.Text.RegularExpressions.Regex.IsMatch(id,"^[A-Z2-7]{7}(-[A-Z2-7]{7}){7}$")&&MessageBox.Show(this,"Remove this device's access? It keeps any files already downloaded.","Remove device",MessageBoxButtons.YesNo)==DialogResult.Yes)await engine.Forget(id);break;
    case "save-connection":using(var save=new SaveFileDialog{Filter="Desksync connection (*.desksync)|*.desksync",FileName=engine.Settings.Role=="server"?"My Desksync server.desksync":"Approve "+engine.Settings.DeviceName+".desksync"})if(save.ShowDialog(this)==DialogResult.OK){await File.WriteAllTextAsync(save.FileName,engine.Code.Encode());notice="Connection file saved. Open it through Desksync on the other computer.";}break;
    case "load-connection":using(var open=new OpenFileDialog{Filter="Desksync connection (*.desksync)|*.desksync|Text file (*.txt)|*.txt"})if(open.ShowDialog(this)==DialogResult.OK){if(new FileInfo(open.FileName).Length>1500)throw new IOException("This is not a valid connection file.");await engine.EnsureFolder();await engine.Pair(Pairing.Parse(await File.ReadAllTextAsync(open.FileName)),engine.Settings.Role!="server");engine.CompleteSetup(engine.Settings.Role,"selfhost");notice=engine.Settings.Role=="server"?"Computer approved. Waiting for its connection.":"Server added. Save this computer's connection file and approve it on the server to finish.";}break;
    case "advanced-pair":await engine.EnsureFolder();await engine.Pair(Pairing.Parse(m["code"]?.ToString()??""),engine.Settings.Role!="server");engine.CompleteSetup(engine.Settings.Role,"selfhost");notice="Connection added. Both computers must approve each other.";break;
    case "versions":var versions=Path.Combine(engine.Settings.Folder,".stversions");if(!Directory.Exists(versions))throw new IOException("No retained versions yet. Replaced and deleted files received from another device are kept here for up to 30 days.");OpenFolder(versions);break;
    case "startup":Startup.Enable();notice="Desksync will start in the tray when you sign in to Windows.";break;
    case "check-updates":_=CheckUpdates(false);break;
    case "install-update":await ApplyUpdateIfIdle(true);break;
    case "auto-updates":updates.SetAutomatic(m["enabled"]?.GetValue<bool>()==true);break;
    case "help":Process.Start(new ProcessStartInfo("https://github.com/PeteZDj/desksync#readme"){UseShellExecute=true});break;
    case "clear-notice":notice=null;error=null;break;
    default:return;
   }await RefreshState();
  }catch(OperationCanceledException){signInState=null;await RefreshState();}catch(Exception e){error=e.Message;await RefreshState();}
 }
 void MarkUpdateHealthy(){if(!ready||!snapshotHealthy||!uiLoaded)return;var nonce=Environment.GetEnvironmentVariable("DESKSYNC_UPDATE_NONCE");if(System.Text.RegularExpressions.Regex.IsMatch(nonce??"","^[a-f0-9]{32}$")){Directory.CreateDirectory(Path.Combine(userHome,"updates"));File.WriteAllText(Path.Combine(userHome,"updates","health-"+nonce+".txt"),Assembly.GetExecutingAssembly().GetName().Version!.ToString(3));}}
 async Task EditCommand(string command){if(view.Source?.GetLeftPart(UriPartial.Authority)!=Origin)return;var core=view.CoreWebView2;if(command=="copy"||command=="cut"){var value=await core.ExecuteScriptAsync("(()=>{const e=document.activeElement;return e&&typeof e.selectionStart==='number'?e.value.slice(e.selectionStart,e.selectionEnd):getSelection().toString()})()");var text=JsonSerializer.Deserialize<string>(value);if(!string.IsNullOrEmpty(text))Clipboard.SetText(text);if(command=="cut")await core.ExecuteScriptAsync("document.execCommand('delete')");}else if(command=="paste"){if(Clipboard.ContainsText())await core.ExecuteScriptAsync("document.execCommand('insertText',false,"+JsonSerializer.Serialize(Clipboard.GetText())+")");}else await core.ExecuteScriptAsync("document.execCommand('"+command+"')");}
 void AddMenus()
 {
  var bar=new MenuStrip{BackColor=Color.White,ForeColor=Color.FromArgb(70,83,105),Font=new Font("Segoe UI",9)};
  ToolStripMenuItem Top(string text){var item=new ToolStripMenuItem(text);bar.Items.Add(item);return item;}
  void Item(ToolStripMenuItem menu,string text,Action action,string shortcut=""){var i=new ToolStripMenuItem(text){ShortcutKeyDisplayString=shortcut};if(shortcut.Length>0)try{i.ShortcutKeys=(Keys)new KeysConverter().ConvertFromInvariantString(shortcut)!;}catch(ArgumentException){}i.Click+=(_,_)=>action();menu.DropDownItems.Add(i);}
  void Navigate(string page)=>ShowPage(page);
  var file=Top("&File");Item(file,"Open Desksync folder",()=>OpenFolder(engine.Settings.Folder),"Ctrl+O");Item(file,"Server connection…",()=>Navigate("connections"));Item(file,"Settings…",()=>Navigate("settings"));file.DropDownItems.Add(new ToolStripSeparator());Item(file,"Close window",()=>Hide(),"Alt+F4");Item(file,managed?"Close dashboard":"Quit Desksync",async()=>{try{if(ready&&!managed)await engine.Shutdown();quitting=true;Close();}catch(Exception e){MessageBox.Show(e.Message,"Desksync");}});
  var edit=Top("&Edit");foreach(var (title,command,key) in new[]{("Undo","undo","Ctrl+Z"),("Redo","redo","Ctrl+Y"),("Cut","cut","Ctrl+X"),("Copy","copy","Ctrl+C"),("Paste","paste","Ctrl+V"),("Select all","selectAll","Ctrl+A")})Item(edit,title,async()=>{await EditCommand(command);},key);
  var screen=Top("&View");Item(screen,"Home",()=>Navigate("home"));Item(screen,"Sync activity",()=>Navigate("activity"));Item(screen,"Devices",()=>Navigate("devices"));Item(screen,"Notifications",()=>Navigate("notifications"));Item(screen,"Scan files now",()=>_=Run(engine.Rescan));Item(screen,"Refresh status",async()=>await RefreshState(),"F5");screen.DropDownItems.Add(new ToolStripSeparator());Item(screen,"Zoom in",()=>view.ZoomFactor=Math.Min(1.5,view.ZoomFactor+.1));Item(screen,"Zoom out",()=>view.ZoomFactor=Math.Max(.8,view.ZoomFactor-.1));Item(screen,"Actual size",()=>view.ZoomFactor=1);
  var help=Top("&Help");Item(help,"Setup and self-hosting guide",()=>Process.Start(new ProcessStartInfo("https://github.com/PeteZDj/desksync#readme"){UseShellExecute=true}));Item(help,"Check for updates",()=>{Navigate("settings");_=CheckUpdates(false);});Item(help,"Copy sync diagnostics",()=>{if(lastSnapshot!=null)Clipboard.SetText(JsonSerializer.Serialize(new{version=Assembly.GetExecutingAssembly().GetName().Version!.ToString(3),sync=lastSnapshot},new JsonSerializerOptions{WriteIndented=true}));});Item(help,"About Desksync",()=>MessageBox.Show("Desksync "+Assembly.GetExecutingAssembly().GetName().Version!.ToString(3)+"\nAfrosoftware\n\nA private, two-way folder for your devices.\nEncrypted synchronization by Syncthing 2.1.7 (MPL-2.0).\n\nAutomatic update packages are verified against the embedded Afrosoftware release key. This Windows preview is not yet signed with a publicly trusted code-signing certificate.","About Desksync"));MainMenuStrip=bar;Controls.Add(bar);bar.BringToFront();
 }
 async Task CheckUpdates(bool automatic)
 {
  if(updateCheckRunning)return;updateCheckRunning=true;try{updateReady=await updates.Check(()=>Push(new{type="update",updates=updates.State,automaticUpdates=updates.Automatic}),automatic);autoInstall=automatic&&updates.Automatic;if(updateReady&&autoInstall)await ApplyUpdateIfIdle();}finally{updateCheckRunning=false;}
 }
 async Task ApplyUpdateIfIdle(bool force=false)
 {
  if(!updateReady||signIn!=null||!engine.Settings.SetupCompleted)return;bool engineStopped=false;
  try{if(!force){var state=await engine.Get("db/status?folder="+Uri.EscapeDataString(engine.Settings.FolderId));if(state["state"]?.ToString() is not ("idle" or "error")||lastSnapshot?["transfers"]?.AsArray().Any(t=>t?["State"]?.ToString()=="active")==true)return;}if(!File.Exists(Path.Combine(AppContext.BaseDirectory,"Desksync.Updater.exe")))throw new IOException("The update helper is missing. Reinstall the current setup package to repair it.");timer.Stop();notice="Restarting to install the verified update. Your folder and device identity will be kept.";await RefreshState();if(!managed){await engine.Shutdown();engineStopped=true;}updates.Apply(managed);quitting=true;Close();}
  catch(Exception e){autoInstall=false;error="Update postponed: "+e.Message;if(engineStopped)try{Process.Start(Engine.StartInfo("serve","--home",Path.Combine(engine.Home,"engine"),"--no-browser","--no-upgrade"));}catch{}timer.Start();await RefreshState();}
 }
 async Task SignIn()
 {
  if(signIn!=null)return;signIn=new CancellationTokenSource(TimeSpan.FromMinutes(10));try{var grant=await account.Begin(engine.DeviceId,engine.Settings.DeviceName,signIn.Token);signInState=new{code=grant["user_code"]!.ToString(),message="Finish signing in in your browser"};await RefreshState();var url=grant["verification_uri_complete"]!.GetValue<string>();if(!url.StartsWith("https://afrosoftware.com/desksync/authorize?",StringComparison.Ordinal))throw new IOException("The sign-in destination was not recognized.");Process.Start(new ProcessStartInfo(url){UseShellExecute=true});while(!signIn.IsCancellationRequested){await Task.Delay(5000,signIn.Token);if(await account.Poll(grant["device_code"]!.GetValue<string>(),signIn.Token))break;}signIn.Token.ThrowIfCancellationRequested();await engine.EnsureFolder();var peer=Pairing.Parse(account.Session!.Server["connectionCode"]!.GetValue<string>());if(peer.Device!=engine.DeviceId)await engine.Pair(peer,true);engine.CompleteSetup(managed?"server":"desktop");await engine.Pause(false);notice=managed?"Signed in to your Afrosoftware account.":"Signed in. This computer is now connected to your server.";accountChecked=DateTimeOffset.UtcNow;}
  catch(OperationCanceledException){notice="Sign-in cancelled. Your files were kept.";}catch(Exception e){error=e.Message;}finally{signIn.Dispose();signIn=null;signInState=null;await RefreshState();}
 }
}
static class Startup{public static void Enable(){using var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");key.SetValue("Afrosoftware Desksync","\""+Environment.ProcessPath+"\" --background");}}
static class Brand
{
 public static Icon Icon(string state="app",int frame=0){using var b=new Bitmap(64,64);using var g=Graphics.FromImage(b);g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.Clear(Color.Transparent);using var blue=new SolidBrush(Color.FromArgb(46,103,238));using var path=new System.Drawing.Drawing2D.GraphicsPath();path.AddArc(2,2,28,28,180,90);path.AddArc(34,2,28,28,270,90);path.AddArc(34,34,28,28,0,90);path.AddArc(2,34,28,28,90,90);path.CloseFigure();g.FillPath(blue,path);if(state=="syncing"){g.TranslateTransform(32,32);g.RotateTransform(frame*45);g.TranslateTransform(-32,-32);}using var pen=new Pen(Color.White,5){StartCap=System.Drawing.Drawing2D.LineCap.Round,EndCap=System.Drawing.Drawing2D.LineCap.Round};g.DrawArc(pen,15,15,34,34,205,125);g.DrawArc(pen,15,15,34,34,25,125);g.DrawLines(pen,[new Point(37,13),new Point(48,17),new Point(45,29)]);g.DrawLines(pen,[new Point(27,51),new Point(16,47),new Point(19,35)]);g.ResetTransform();if(state!="app"){var badge=state=="error"?Color.FromArgb(215,75,72):state is "paused" or "pending"?Color.FromArgb(230,165,44):state=="syncing"?Color.FromArgb(58,130,240):state=="offline"?Color.FromArgb(130,145,166):Color.FromArgb(40,160,114);using var fill=new SolidBrush(badge);g.FillEllipse(Brushes.White,40,40,24,24);g.FillEllipse(fill,43,43,18,18);if(state=="error"){g.DrawLine(Pens.White,52,46,52,53);g.FillEllipse(Brushes.White,51,56,2,2);}else if(state=="paused"){g.FillRectangle(Brushes.White,48,47,3,10);g.FillRectangle(Brushes.White,53,47,3,10);}}var handle=b.GetHicon();try{return (Icon)System.Drawing.Icon.FromHandle(handle).Clone();}finally{DestroyIcon(handle);}}
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
}
