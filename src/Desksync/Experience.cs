using System.Text.Json;
using System.Text.Json.Nodes;

namespace Desksync;

public sealed record SyncActivity(string Id, string Time, string Name, string Action, string Status, string Detail);
public sealed partial class Engine
{
    long eventCursor;
    readonly List<SyncActivity> activity = [];
    bool activityLoaded;
    public void CompleteSetup(string role, string provider = "afrosoftware") { Settings = Settings with { Role = role == "server" ? "server" : "desktop", Provider = provider, SetupCompleted = true }; Save(); }
    public static string ValidateFolder(string value, string privateHome)
    {
        var path = Path.GetFullPath(value.Trim()).TrimEnd(Path.DirectorySeparatorChar);
        if(path.StartsWith(@"\\",StringComparison.Ordinal))throw new ArgumentException("Choose a folder on a local drive. Each computer keeps its own mirrored files.");
        var blocked = new[] { Path.GetPathRoot(path)!.TrimEnd('\\'), Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\'), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\') };
        if (blocked.Any(x => string.Equals(path, x, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Choose a dedicated folder, such as C:\\Desksync, instead of an entire drive or system folder.");
        foreach (var protectedPath in new[] { Path.GetFullPath(privateHome).TrimEnd('\\'), Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Afrosoftware","DesksyncDesktop"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Afrosoftware","DesksyncUpdateState"),@"C:\ProgramData\DesksyncRelease" })
            if (path.Equals(protectedPath,StringComparison.OrdinalIgnoreCase)||path.StartsWith(protectedPath+'\\',StringComparison.OrdinalIgnoreCase)||protectedPath.StartsWith(path+'\\',StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Keep the sync folder separate from Desksync's application files and private settings.");
        return path;
    }
    public async Task SetFolder(string value)
    {
        var path = ValidateFolder(value, data);
        if (string.Equals(path.TrimEnd('\\'),Settings.Folder.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)) { await EnsureFolder(); return; }
        await mutation.WaitAsync();
        try
        {
            var folders = await Get("config/folders"); var folder = folders.AsArray().SingleOrDefault(f=>f!["id"]!.GetValue<string>()==Settings.FolderId);
            var oldPath=Settings.Folder;
            if (folder != null)
            {
                if(Directory.Exists(path)&&Directory.EnumerateFileSystemEntries(path).Any())throw new IOException("For an existing connection, choose an empty destination. Desksync will copy your files there and keep the originals.");
                var wasPaused=folder["paused"]?.GetValue<bool>()==true; folder["paused"]=true;await Put("config/folders",folders);
                try
                {
                    if(!Directory.Exists(path)){Directory.CreateDirectory(path);Restrict(path);}
                    GrantServerFolderAccess(path);
                    if(Directory.Exists(oldPath))CopyTree(oldPath,path);
                    folder["path"]=path;folder["paused"]=wasPaused;await Put("config/folders",folders);
                }
                catch {folder["path"]=oldPath;folder["paused"]=wasPaused;try{await Put("config/folders",folders);}catch{}throw;}
            }
            Settings=Settings with {Folder=path};Save();
        }
        finally{mutation.Release();}
        await EnsureFolder();
    }
    static void CopyTree(string source,string destination)
    {
        foreach(var item in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            if((item.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("The folder contains a link or junction. Choose a folder of ordinary files before changing its location.");
            var target=Path.Combine(destination,item.Name);
            if(item is DirectoryInfo) {Directory.CreateDirectory(target);CopyTree(item.FullName,target);}
            else {var file=(FileInfo)item;var size=file.Length;var changed=file.LastWriteTimeUtc;file.CopyTo(target,false);file.Refresh();if(file.Length!=size||file.LastWriteTimeUtc!=changed)throw new IOException("A file changed during the copy. Close applications using the folder and try an empty destination again.");}
        }
    }
    public async Task<object> Snapshot()
    {
        var folder = (await Get("config/folders")).AsArray().SingleOrDefault(f=>f!["id"]!.GetValue<string>()==Settings.FolderId);
        var connections=(await Get("system/connections"))["connections"]!.AsObject();
        var deviceConfig=(await Get("config/devices")).AsArray();
        var devices=deviceConfig.Where(d=>d!["deviceID"]!.GetValue<string>()!=DeviceId).Select(d=>new {id=d!["deviceID"]!.GetValue<string>(),name=d["name"]!.GetValue<string>(),connected=connections[d["deviceID"]!.GetValue<string>()]?["connected"]?.GetValue<bool>()==true,type=connections[d["deviceID"]!.GetValue<string>()]?["type"]?.ToString()??"",sent=connections[d["deviceID"]!.GetValue<string>()]?["outBytesTotal"]?.GetValue<long>()??0,received=connections[d["deviceID"]!.GetValue<string>()]?["inBytesTotal"]?.GetValue<long>()??0}).ToArray();
        JsonNode? summary=folder==null?null:await Get("db/status?folder="+Uri.EscapeDataString(Settings.FolderId));
        long uploadNeededBytes=0;
        if(folder!=null)foreach(var device in devices.Where(d=>d.connected)){try{var completion=await Get("db/completion?device="+Uri.EscapeDataString(device.id)+"&folder="+Uri.EscapeDataString(Settings.FolderId));uploadNeededBytes+=completion["needBytes"]?.GetValue<long>()??0;}catch(HttpRequestException){}}
        try
        {
            if(!activityLoaded){activityLoaded=true;try{if(File.Exists(Path.Combine(data,"activity.json")))activity.AddRange(JsonSerializer.Deserialize<List<SyncActivity>>(File.ReadAllText(Path.Combine(data,"activity.json")))??[]);}catch(Exception ex)when(ex is IOException or JsonException){/* A damaged activity cache must not stop new events. */}}
            var events=await Get("events?since="+eventCursor+"&timeout=1&limit=100&events=ItemStarted,ItemFinished,LocalChangeDetected,RemoteChangeDetected,FolderErrors,DeviceConnected,DeviceDisconnected");
            foreach(var e in events.AsArray())
            {
                eventCursor=Math.Max(eventCursor,e!["id"]!.GetValue<long>());var type=e["type"]!.GetValue<string>();var body=e["data"]!;
                if(body["folder"]!=null&&body["folder"]!.ToString()!=Settings.FolderId)continue;
                if(type=="ItemStarted"||type=="RemoteChangeDetected")continue;
                var name=body["item"]?.ToString()??body["path"]?.ToString()??"Device connection";
                var error=body["error"]?.ToString()??"";var action=body["action"]?.ToString()??"";
                var entry=new SyncActivity(e["time"]!.ToString()+"|"+type+"|"+name,e["time"]!.ToString(),name,type=="LocalChangeDetected"?(action.Contains("delete")?"Deleted locally":"Changed locally"):type=="ItemFinished"?(action=="delete"?"Deletion received":"Downloaded"):type=="DeviceConnected"?"Device connected":type=="DeviceDisconnected"?"Device disconnected":"Folder needs attention",error.Length>0||type=="FolderErrors"?"error":"complete",error);
                if(!activity.Any(x=>x.Id==entry.Id))activity.Insert(0,entry);
            }
            if(activity.Count>200)activity.RemoveRange(200,activity.Count-200);
            var activityPath=Path.Combine(data,"activity.json");File.WriteAllText(activityPath+".tmp",JsonSerializer.Serialize(activity));File.Move(activityPath+".tmp",activityPath,true);
        }
        catch(Exception ex)when(ex is HttpRequestException or TaskCanceledException or IOException or JsonException){ /* A transient event failure must not hide the current sync status. */ }
        var disk=new DriveInfo(Path.GetPathRoot(Settings.Folder)!);
        return new {deviceId=DeviceId,deviceName=Settings.DeviceName,folder=Settings.Folder,folderId=Settings.FolderId,role=Settings.Role,provider=Settings.Provider,setupCompleted=Settings.SetupCompleted,paused=folder?["paused"]?.GetValue<bool>()==true,phase=summary?["state"]?.ToString()??"not-configured",files=summary?["localFiles"]?.GetValue<long>()??0,bytes=summary?["localBytes"]?.GetValue<long>()??0,globalBytes=summary?["globalBytes"]?.GetValue<long>()??0,neededBytes=summary?["needBytes"]?.GetValue<long>()??0,uploadNeededBytes,needed=summary?["needTotalItems"]?.GetValue<long>()??0,errors=summary?["errors"]?.GetValue<long>()??0,diskTotal=disk.TotalSize,diskFree=disk.AvailableFreeSpace,devices,activity=activity.Take(100).ToArray()};
    }
}
