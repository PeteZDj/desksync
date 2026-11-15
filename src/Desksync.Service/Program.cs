using System.Diagnostics;
using System.ServiceProcess;
using System.Xml.Linq;
ServiceBase.Run(new SyncService());
sealed class SyncService:ServiceBase
{
 Process? engine;bool stopping;
 public SyncService(){ServiceName="DesksyncSync";CanStop=true;AutoLog=true;}
 protected override void OnStart(string[] args){stopping=false;var start=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"syncthing.exe")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=AppContext.BaseDirectory};foreach(var arg in new[]{"serve","--home=C:\\ProgramData\\Desksync\\engine","--no-browser","--no-upgrade","--no-restart","--log-file=C:\\ProgramData\\Desksync\\engine.log","--log-max-size=5242880","--log-max-old-files=2"})start.ArgumentList.Add(arg);engine=Process.Start(start)!;engine.EnableRaisingEvents=true;engine.Exited+=(_,_)=>{if(!stopping)Stop();};}
 protected override void OnStop(){stopping=true;try{var xml=XDocument.Load(@"C:\ProgramData\Desksync\engine\config.xml");using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(5)};client.DefaultRequestHeaders.Add("X-API-Key",xml.Root!.Element("gui")!.Element("apikey")!.Value);client.PostAsync("http://127.0.0.1:5391/rest/system/shutdown",null).GetAwaiter().GetResult();}catch{}if(engine!=null){if(!engine.HasExited&&!engine.WaitForExit(5000))engine.Kill(true);engine.Dispose();}}
}
