using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Desksync;

public sealed record Settings(string Folder, string FolderId, string DeviceName)
{
    public string Role { get; init; } = "desktop";
    public bool SetupCompleted { get; init; }
    public string Provider { get; init; } = "afrosoftware";
    public int StoragePolicyVersion { get; init; }
    public StorageMigration? StorageMigration { get; init; }
}
public sealed record Pairing(string Device, string Folder, string Name)
{
    public string Encode() => "desksync://pair?v=1&device=" + Uri.EscapeDataString(Device) + "&folder=" + Uri.EscapeDataString(Folder) + "&name=" + Uri.EscapeDataString(Name);
    public static Pairing Parse(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "desksync" || uri.Host != "pair" || value.Length > 1500)
            throw new ArgumentException("Paste a Desksync pairing code from your other device.");
        var fields = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length == 2 ? Uri.UnescapeDataString(p[1]) : "");
        var device = fields.GetValueOrDefault("device", "").ToUpperInvariant();
        var folder = fields.GetValueOrDefault("folder", "");
        if (fields.GetValueOrDefault("v") != "1" || !System.Text.RegularExpressions.Regex.IsMatch(device, "^[A-Z2-7]{7}(-[A-Z2-7]{7}){7}$") || !System.Text.RegularExpressions.Regex.IsMatch(folder, "^[a-zA-Z0-9_-]{1,80}$"))
            throw new ArgumentException("This pairing code is invalid or from an unsupported version.");
        var name = fields.GetValueOrDefault("name", "Desksync device");
        return new(device, folder, name.Length > 80 ? name[..80] : name);
    }
}

public sealed partial class Engine : IDisposable
{
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    readonly string data;
    readonly string engineHome;
    readonly int guiPort;
    readonly SemaphoreSlim mutation = new(1, 1);
    public Settings Settings { get; private set; }
    public string DeviceId { get; private set; } = "";
    public string Home => data;
    public static string Binary => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "syncthing.exe" : "syncthing");
    internal static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    internal static string CanonicalPath(string value) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    internal static bool SamePath(string a, string b) => string.Equals(CanonicalPath(a), CanonicalPath(b), PathComparison);
    internal static bool Within(string path, string parent) => SamePath(path,parent) || CanonicalPath(path).StartsWith(Path.EndsInDirectorySeparator(CanonicalPath(parent))?CanonicalPath(parent):CanonicalPath(parent)+Path.DirectorySeparatorChar,PathComparison);
    internal static DriveInfo DiskFor(string path) => OperatingSystem.IsWindows() ? new DriveInfo(Path.GetPathRoot(path)!) : DriveInfo.GetDrives().Where(d=>d.IsReady&&Within(path,d.Name)).OrderByDescending(d=>d.Name.Length).First();
    public Engine(string dataDirectory, string? folder = null, int guiPort = 5391)
    {
        if (guiPort < 1024 || guiPort > 65535) throw new ArgumentException("Choose a local API port between 1024 and 65535.");
        this.guiPort = guiPort;
        data = Path.GetFullPath(dataDirectory); engineHome = Path.Combine(data, "engine");
        Directory.CreateDirectory(data);
        var settingsFile = Path.Combine(data, "settings.json");
        Settings = File.Exists(settingsFile) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsFile))! : new(folder ?? (OperatingSystem.IsWindows() ? @"C:\Desksync" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Desksync")), "desksync-" + Guid.NewGuid().ToString("N"), Environment.MachineName);
        if (!File.Exists(settingsFile)) Save();
    }
    internal static void Restrict(string directory)
    {
        if(!OperatingSystem.IsWindows()) { File.SetUnixFileMode(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute); return; }
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        if (string.Equals(Path.GetFullPath(directory).TrimEnd('\\'), @"C:\ProgramData\Desksync", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFullPath(directory).TrimEnd('\\'), @"C:\Desksync", StringComparison.OrdinalIgnoreCase))
        {
            try { var serviceSid = (SecurityIdentifier)new NTAccount(@"NT SERVICE\DesksyncSync").Translate(typeof(SecurityIdentifier)); acl.AddAccessRule(new FileSystemAccessRule(serviceSid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow)); }
            catch (IdentityNotMappedException) { /* The optional server service has not been installed. */ }
        }
        new DirectoryInfo(directory).SetAccessControl(acl);
    }
    void Save()
    {
        var temp = Path.Combine(data, "settings.tmp"); File.WriteAllText(temp, JsonSerializer.Serialize(Settings)); File.Move(temp, Path.Combine(data, "settings.json"), true);
    }
    void GrantServerFolderAccess(string folder)
    {
        if(!OperatingSystem.IsWindows())return;
        if(!string.Equals(data.TrimEnd('\\'),@"C:\ProgramData\Desksync",StringComparison.OrdinalIgnoreCase))return;
        try{var sid=(SecurityIdentifier)new NTAccount(@"NT SERVICE\DesksyncSync").Translate(typeof(SecurityIdentifier));var info=new DirectoryInfo(folder);var acl=info.GetAccessControl();acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));info.SetAccessControl(acl);}
        catch(IdentityNotMappedException){/* First-time setup grants access after creating the service. */}
    }
    public async Task Initialize(bool ensureFolder = true)
    {
        if (!File.Exists(Binary)) throw new FileNotFoundException("The sync engine is missing. Reinstall Desksync.");
        Restrict(data); Directory.CreateDirectory(engineHome);
        var configFile = Path.Combine(engineHome, "config.xml");
        if (!File.Exists(configFile))
        {
            var generate = StartInfo("generate", "--home", engineHome, "--no-port-probing", "--gui-user", "desksync", "--gui-password", "-");
            generate.RedirectStandardInput = true; generate.RedirectStandardOutput = true; generate.RedirectStandardError = true;
            using var process = Process.Start(generate)!;
            await process.StandardInput.WriteLineAsync(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))); process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(); await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0) throw new IOException("The sync engine could not create its private configuration.");
            var document = XDocument.Load(configFile); var root = document.Root!;
            root.Elements("folder").Remove();
            root.Element("gui")!.Element("address")!.Value = "127.0.0.1:" + guiPort;
            root.Element("options")!.Element("urAccepted")!.Value = "-1";
            root.Element("options")!.Element("startBrowser")!.Value = "false";
            // Connections may use public discovery and relays; folder contents remain encrypted in transit.
            // Do not silently create router port mappings on someone else's network.
            root.Element("options")!.Element("natEnabled")!.Value = "false";
            document.Save(configFile);
        }
        var xml = XDocument.Load(configFile);
        var gui = xml.Root!.Element("gui")!;
        var address = gui.Element("address")!.Value;
        if (address != "127.0.0.1:" + guiPort) throw new IOException("Desksync requires its engine administration API to stay on its configured loopback port.");
        http.BaseAddress = new Uri("http://" + address + "/rest/");
        http.DefaultRequestHeaders.Add("X-API-Key", gui.Element("apikey")!.Value);
        var available = false;
        try { using var ping = await http.GetAsync("system/ping"); available = ping.IsSuccessStatusCode; } catch (HttpRequestException) { }
        if (!available)
        {
            Process.Start(StartInfo("serve", "--home", engineHome, "--no-browser", "--no-upgrade", "--log-file", Path.Combine(data, "engine.log"), "--log-max-size", "5242880", "--log-max-old-files", "2"));
            for (var i = 0; i < 40; i++)
            {
                await Task.Delay(250);
                try { using var ping = await http.GetAsync("system/ping"); if (ping.IsSuccessStatusCode) { available = true; break; } } catch (HttpRequestException) { }
            }
        }
        if (!available) throw new IOException("The sync engine did not start. Check for another application using port 5391.");
        var status = await Get("system/status"); DeviceId = status["myID"]!.GetValue<string>();
        if (ensureFolder) await EnsureFolder();
        await ApplyStoragePolicy();
    }
    public static ProcessStartInfo StartInfo(params string[] args)
    {
        var info = new ProcessStartInfo(Binary) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment["STVERSIONEXTRA"] = "Desksync "+typeof(Engine).Assembly.GetName().Version!.ToString(3); return info;
    }
    public async Task<JsonNode> Get(string route) => await http.GetFromJsonAsync<JsonNode>(route) ?? throw new IOException("The sync engine returned an empty response.");
    async Task Put(string route, JsonNode value)
    {
        using var response = await http.PutAsJsonAsync(route, value);
        if (!response.IsSuccessStatusCode) throw new IOException("The sync engine rejected this change (" + (int)response.StatusCode + "). Check the device ID and folder permissions.");
    }
    public async Task EnsureFolder()
    {
        if (!Directory.Exists(Settings.Folder)) { Directory.CreateDirectory(Settings.Folder); Restrict(Settings.Folder); }
        GrantServerFolderAccess(Settings.Folder);
        var folders = await Get("config/folders");
        if (folders.AsArray().Any(f => f!["id"]!.GetValue<string>() == Settings.FolderId)) return;
        if (folders.AsArray().Any(f => SamePath(f!["path"]!.GetValue<string>(),Settings.Folder)))
            throw new IOException("This directory is already configured under another sync folder.");
        var folder = await Get("config/defaults/folder");
        folder["id"] = Settings.FolderId; folder["label"] = "Desksync"; folder["path"] = Settings.Folder;
        folder["type"] = "sendreceive"; folder["fsWatcherEnabled"] = true; folder["rescanIntervalS"] = 3600;
        folder["minDiskFree"] = new JsonObject { ["value"] = 2, ["unit"] = "GB" };
        folder["devices"] = new JsonArray(new JsonObject { ["deviceID"] = DeviceId });
        folder["versioning"] = new JsonObject { ["type"] = "staggered", ["params"] = new JsonObject { ["maxAge"] = "2592000", ["cleanInterval"] = "3600" }, ["cleanupIntervalS"] = 3600, ["fsType"] = "basic", ["fsPath"] = "" };
        folders.AsArray().Add(folder); await Put("config/folders", folders);
    }
    public Pairing Code => new(DeviceId, Settings.FolderId, Settings.DeviceName);
    public async Task Pair(Pairing peer, bool join)
    {
        await mutation.WaitAsync();
        try
        {
            if (peer.Device == DeviceId) throw new ArgumentException("That is this computer's code. Use the code from the other device.");
            var devices = await Get("config/devices"); var folders = await Get("config/folders");
            var folder = folders.AsArray().Single(f => f!["id"]!.GetValue<string>() == Settings.FolderId)!;
            if (peer.Folder != Settings.FolderId)
            {
                if (!join || folder["devices"]!.AsArray().Any(d => d!["deviceID"]!.GetValue<string>() != DeviceId))
                    throw new ArgumentException("This code belongs to another folder. Use Join folder on a new, unpaired desktop.");
                // Keep local files and history; change only the unpaired folder's identity.
                folder["id"] = peer.Folder;
            }
            if (!devices.AsArray().Any(d => d!["deviceID"]!.GetValue<string>() == peer.Device))
            {
                var device = await Get("config/defaults/device"); device["deviceID"] = peer.Device; device["name"] = peer.Name;
                device["addresses"] = new JsonArray("dynamic"); device["introducer"] = false; device["autoAcceptFolders"] = false;
                devices.AsArray().Add(device); await Put("config/devices", devices);
            }
            if (!folder["devices"]!.AsArray().Any(d => d!["deviceID"]!.GetValue<string>() == peer.Device))
                folder["devices"]!.AsArray().Add(new JsonObject { ["deviceID"] = peer.Device });
            await Put("config/folders", folders);
            if (Settings.FolderId != peer.Folder) { Settings = Settings with { FolderId = peer.Folder }; Save(); }
        }
        finally { mutation.Release(); }
    }
    public async Task Pause(bool paused)
    {
        await mutation.WaitAsync();
        try { var folders = await Get("config/folders"); var folder = folders.AsArray().Single(f => f!["id"]!.GetValue<string>() == Settings.FolderId)!; folder["paused"] = paused; await Put("config/folders", folders); }
        finally { mutation.Release(); }
    }
    public async Task Forget(string device)
    {
        await mutation.WaitAsync();
        try
        {
            if (device == DeviceId) throw new ArgumentException("You cannot remove this computer.");
            var folders = await Get("config/folders");
            foreach (var folder in folders.AsArray())
                foreach (var entry in folder!["devices"]!.AsArray().Where(d => d!["deviceID"]!.GetValue<string>() == device).ToArray()) folder["devices"]!.AsArray().Remove(entry);
            await Put("config/folders", folders);
            using var response = await http.DeleteAsync("config/devices/" + Uri.EscapeDataString(device)); response.EnsureSuccessStatusCode();
        }
        finally { mutation.Release(); }
    }
    public async Task Shutdown() { using var response = await http.PostAsync("system/shutdown", null); response.EnsureSuccessStatusCode(); }
    public void Dispose() { http.Dispose(); mutation.Dispose(); }
}
