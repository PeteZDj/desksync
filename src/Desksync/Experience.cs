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
    public Task<object> Snapshot() => ReadSnapshot();
}
