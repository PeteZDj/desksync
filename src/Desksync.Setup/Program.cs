using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;
ApplicationConfiguration.Initialize();Application.Run(new SetupWindow());
sealed class SetupWindow:Form
{
 readonly Button install=new(){Text="Install Desksync",AutoSize=false,Width=180,Height=43,BackColor=Color.FromArgb(50,107,234),ForeColor=Color.White,FlatStyle=FlatStyle.Flat};
 readonly CheckBox startup=new(){Text="Start in the system tray when I sign in",Checked=true,AutoSize=true};
 readonly Label status=new(){Text="Windows x64 · Includes its .NET runtime · Preview 0.2.1",AutoSize=true,MaximumSize=new Size(500,0),ForeColor=Color.FromArgb(137,151,174)};
 string? installed;
 readonly ProgressBar progress=new(){Width=510,Height=5,Style=ProgressBarStyle.Marquee,Visible=false};
 public SetupWindow()
 {
  Text="Desksync Setup";Size=new Size(660,650);StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;BackColor=Color.FromArgb(247,249,252);Font=new Font("Segoe UI",10);Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
  var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(42,30,35,20),FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};
  var heading=new FlowLayoutPanel{Width=530,Height=64};heading.Controls.Add(new PictureBox{Image=Icon!.ToBitmap(),SizeMode=PictureBoxSizeMode.Zoom,Width=47,Height=47,Margin=new Padding(0,4,12,0)});heading.Controls.Add(new Label{Text="Desksync",AutoSize=true,Font=new Font("Segoe UI",23,FontStyle.Bold),ForeColor=Color.FromArgb(42,60,91),Margin=new Padding(0,4,0,0)});layout.Controls.Add(heading);
  layout.Controls.Add(new Label{Text="Your own cloud.\nReady for everyday work.",AutoSize=true,Font=new Font("Segoe UI",24,FontStyle.Bold),ForeColor=Color.FromArgb(38,55,82),Margin=new Padding(0,14,0,15)});
  layout.Controls.Add(new Label{Text="Keep one folder in sync between your desktop and your server.\nSign in, choose your folder, and see what’s happening.",AutoSize=true,ForeColor=Color.FromArgb(123,140,166),Margin=new Padding(0,0,0,24)});
  var box=new Panel{Width=510,Height=100,BackColor=Color.White,Padding=new Padding(18)};box.Controls.Add(new Label{Text="Next, the setup wizard will help you:\n✓  Choose Desktop or Server mode\n✓  Select a folder and connect your account or own server",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(89,111,145)});layout.Controls.Add(box);
  startup.Margin=new Padding(0,21,0,9);layout.Controls.Add(startup);layout.Controls.Add(status);progress.Margin=new Padding(0,12,0,10);layout.Controls.Add(progress);install.Margin=new Padding(0,20,0,0);install.FlatAppearance.BorderSize=0;layout.Controls.Add(install);layout.Controls.Add(new Label{Text="Encrypted sync powered by Syncthing. This preview is unsigned.\nMicrosoft WebView2 is installed from Microsoft if it is missing.",AutoSize=true,ForeColor=Color.FromArgb(152,163,181),Font=new Font("Segoe UI",8),Margin=new Padding(0,16,0,0)});Controls.Add(layout);
  install.Click+=async(_,_)=>{if(installed!=null){Process.Start(new ProcessStartInfo(installed){UseShellExecute=true});return;}install.Enabled=false;progress.Visible=true;try
  {
   if(Process.GetProcessesByName("Desksync").Length>0)throw new IOException("Please quit the running Desksync app from its tray menu, then try again. Your files will be kept.");
   if(!HasWebView()){status.Text="Installing Microsoft’s WebView2 runtime…";await InstallWebView();}
   var target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Afrosoftware","Desksync");Directory.CreateDirectory(target);status.Text="Installing your Desksync desktop, service, and update components…";
   await Task.Run(()=>{using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Desksync.Payload")!;using var zip=new ZipArchive(stream);foreach(var entry in zip.Entries){var path=Path.GetFullPath(Path.Combine(target,entry.FullName));if(!path.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid setup archive.");if(entry.FullName.EndsWith('/')){Directory.CreateDirectory(path);continue;}Directory.CreateDirectory(Path.GetDirectoryName(path)!);entry.ExtractToFile(path,true);}});
   var exe=Path.Combine(target,"Desksync.exe");var programs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Afrosoftware");Directory.CreateDirectory(programs);Shortcut(Path.Combine(programs,"Desksync.lnk"),exe);Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Desksync.lnk"),exe);
   foreach(var name in new[]{"Desksync.lnk","Desksync.cmd"}){var old=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),name);if(File.Exists(old))File.Delete(old);}
   using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(startup.Checked)key.SetValue("Afrosoftware Desksync","\""+exe+"\" --background");else key.DeleteValue("Afrosoftware Desksync",false);}
   progress.Visible=false;status.Text="Installed. Your existing files and device identity have been kept.";install.Text="Open Desksync";install.Enabled=true;installed=exe;Process.Start(new ProcessStartInfo(exe){UseShellExecute=true});
  }catch(Exception e){status.Text=e.Message;progress.Visible=false;install.Enabled=true;}};
 }
 static bool HasWebView(){foreach(var root in new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft","EdgeWebView","Application"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","EdgeWebView","Application")})if(Directory.Exists(root)&&Directory.EnumerateDirectories(root).Any(d=>File.Exists(Path.Combine(d,"msedgewebview2.exe"))))return true;return false;}
 static async Task InstallWebView()
 {
  var folder=Path.Combine(Path.GetTempPath(),"Desksync-setup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var exe=Path.Combine(folder,"MicrosoftEdgeWebview2Setup.exe");using var http=new HttpClient{Timeout=TimeSpan.FromMinutes(5)};await File.WriteAllBytesAsync(exe,await http.GetByteArrayAsync("https://go.microsoft.com/fwlink/p/?LinkId=2124703"));
  var verify=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true};verify.ArgumentList.Add("-NoProfile");verify.ArgumentList.Add("-NonInteractive");verify.ArgumentList.Add("-Command");verify.ArgumentList.Add("-");verify.Environment["DESKSYNC_WEBVIEW_SETUP"]=exe;using(var p=Process.Start(verify)!){await p.StandardInput.WriteLineAsync("$s=Get-AuthenticodeSignature -LiteralPath $env:DESKSYNC_WEBVIEW_SETUP; if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){exit 1}; exit 0");p.StandardInput.Close();await p.WaitForExitAsync();if(p.ExitCode!=0)throw new IOException("The Microsoft runtime signature could not be verified. No runtime was installed.");}
  var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("/silent");start.ArgumentList.Add("/install");using var install=Process.Start(start)!;await install.WaitForExitAsync();if(install.ExitCode!=0&&!HasWebView())throw new IOException("Microsoft WebView2 setup did not finish. Install the Microsoft runtime, then retry Desksync setup.");
 }
 static void Shortcut(string file,string exe){var type=Type.GetTypeFromProgID("WScript.Shell")!;dynamic shell=Activator.CreateInstance(type)!;try{dynamic shortcut=shell.CreateShortcut(file);try{shortcut.TargetPath=exe;shortcut.WorkingDirectory=Path.GetDirectoryName(exe);shortcut.IconLocation=exe+",0";shortcut.Description="Desksync by Afrosoftware";shortcut.Save();}finally{System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);}}finally{System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}}
}
