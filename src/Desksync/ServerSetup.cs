using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
namespace Desksync;
static class ServerSetup
{
 public static void Install(string folder)
 {
  var identity=WindowsIdentity.GetCurrent();if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new IOException("Windows server setup needs administrator permission.");
  using(var existing=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DesksyncSync"))if(existing!=null)throw new IOException("A Desksync server is already installed. Open its dashboard to manage it.");
  folder=Engine.ValidateFolder(folder,@"C:\ProgramData\Desksync");var install=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Afrosoftware","Desksync");Directory.CreateDirectory(install);
  foreach(var file in Directory.EnumerateFiles(AppContext.BaseDirectory)){var target=Path.Combine(install,Path.GetFileName(file));if(!string.Equals(file,target,StringComparison.OrdinalIgnoreCase))File.Copy(file,target,true);}
  var serviceExe=Path.Combine(install,"Desksync.Service.exe");if(!File.Exists(serviceExe))throw new IOException("The Windows service component is missing. Run the latest Desksync installer.");
  // Initialize from the installed application so the engine is not tied to a user's download directory.
  var initialize=new ProcessStartInfo(Path.Combine(install,"Desksync.exe")){UseShellExecute=false,CreateNoWindow=true};foreach(var arg in new[]{"--initialize","--data",@"C:\ProgramData\Desksync","--folder",folder})initialize.ArgumentList.Add(arg);using(var p=Process.Start(initialize)!){p.WaitForExit();if(p.ExitCode!=0)throw new IOException("The server engine could not initialize.");}
  var xml=System.Xml.Linq.XDocument.Load(@"C:\ProgramData\Desksync\engine\config.xml");using(var http=new HttpClient{Timeout=TimeSpan.FromSeconds(10)}){http.DefaultRequestHeaders.Add("X-API-Key",xml.Root!.Element("gui")!.Element("apikey")!.Value);http.PostAsync("http://127.0.0.1:5391/rest/system/shutdown",null).GetAwaiter().GetResult();}
  Thread.Sleep(1500);
  Sc("create","DesksyncSync","binPath=","\""+serviceExe+"\"","start=","auto","obj=",@"NT AUTHORITY\LocalService","DisplayName=","Desksync file synchronization");Sc("sidtype","DesksyncSync","unrestricted");
  var sid=(SecurityIdentifier)new NTAccount(@"NT SERVICE\DesksyncSync").Translate(typeof(SecurityIdentifier));foreach(var path in new[]{@"C:\ProgramData\Desksync",folder}){var info=new DirectoryInfo(path);var acl=info.GetAccessControl();acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));info.SetAccessControl(acl);}
  Sc("failure","DesksyncSync","reset=","86400","actions=","restart/5000/restart/15000/restart/30000");Sc("start","DesksyncSync");
  InstallTray(Path.Combine(install,"Desksync.exe"));
  Process.Start(new ProcessStartInfo(Path.Combine(install,"Desksync.exe")){UseShellExecute=true});
 }
 static void InstallTray(string exe)
 {
  var p=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{"-NoProfile","-NonInteractive","-Command","-"})p.ArgumentList.Add(arg);p.Environment["DESKSYNC_TRAY_EXE"]=exe;p.Environment["DESKSYNC_TRAY_USER"]=WindowsIdentity.GetCurrent().Name;
  using var process=Process.Start(p)!;process.StandardInput.WriteLine("$ErrorActionPreference='Stop';$a=New-ScheduledTaskAction -Execute $env:DESKSYNC_TRAY_EXE -Argument '--background';$t=New-ScheduledTaskTrigger -AtLogOn -User $env:DESKSYNC_TRAY_USER;$p=New-ScheduledTaskPrincipal -UserId $env:DESKSYNC_TRAY_USER -LogonType Interactive -RunLevel Highest;$s=New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([timespan]::Zero) -MultipleInstances IgnoreNew;Register-ScheduledTask -TaskName 'Afrosoftware Desksync Tray' -Action $a -Trigger $t -Principal $p -Settings $s -Description 'Desksync server owner tray. File sync runs separately as LocalService.' -Force | Out-Null");process.StandardInput.Close();var output=process.StandardOutput.ReadToEnd();var error=process.StandardError.ReadToEnd();process.WaitForExit();if(process.ExitCode!=0)throw new IOException("The sync service was installed, but Windows could not register the tray at sign-in: "+error);
  using var run=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true);run?.DeleteValue("Afrosoftware Desksync",false);
 }
 static void Sc(params string[] args){var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"sc.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in args)start.ArgumentList.Add(arg);using var p=Process.Start(start)!;var output=p.StandardOutput.ReadToEnd();var error=p.StandardError.ReadToEnd();p.WaitForExit();if(p.ExitCode!=0)throw new IOException("Windows service setup failed: "+output+error);}
}
